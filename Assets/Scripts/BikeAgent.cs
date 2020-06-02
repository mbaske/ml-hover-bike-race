using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Policies;
using Hoverbike.Track;
using System;
using System.Collections.Generic;

namespace Hoverbike
{
    public class BikeAgent : Agent, IComparable<BikeAgent>
    {
        // Accessed by all agents and camera.
        public class SharedObs
        {
            public Position CrntPos { get; set; }
            public Vector3 Velocity { get; set; }
            public float NormXPos { get; set; }
            // Agent-to-agent distance for sorting.
            public float TmpDelta { get; set; }
        }
        public SharedObs Shared { get; private set; }
        public int CompareTo(BikeAgent other)
        {
            return Shared.TmpDelta.CompareTo(other.Shared.TmpDelta);
        }

        public Bike Bike { get; private set; }
        public Vector3 Position => Bike.transform.position;

        private TrackManager track;
        private BikeAgent[] allAgents;
        private List<BikeAgent> observedAgents;
        private bool isDiscrete;

        // Current track position, see TrackManager.
        private Position crntPos;
        // Current track segment, see TrackManager.
        private Segment crntSeg;
        // TODO doesn't consider agent lead > 180.
        [SerializeField, Tooltip("DEBUG: Current rank, higher is better")]
        private int crntRank;

        [Header("Boost")]
        [SerializeField, Tooltip("DEBUG: Normalized boost, can be positive or negative (slowing down)")]
        private float crntBoost;
        [SerializeField, Tooltip("How quickly crntBoost levels off to 0 (is multiplied by delta time)")]
        private float boostLevelOff = 0.2f;
        [SerializeField, Tooltip("How strongly the acting lane boost value affects crntBoost")]
        private float boostStrength = 0.1f;

        [Header("Observations")]
        [SerializeField, Tooltip("The closest [n] agents will be detected")]
        private int observedAgentsCount = 4;
        [SerializeField, Tooltip("Threshold, measured in degrees (see TrackManager)")]
        private float maxAgentDistance = 7f;
        // Set values below depending on track shape and segment length.
        [SerializeField, Tooltip("Offset indices (segments)")]
        private int[] lookAheadBoost = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        [SerializeField, Tooltip("Offset indices (segments)")]
        private int[] lookAheadCurve = new int[] { 9 };
        [SerializeField, Tooltip("Angle")]
        private float maxCurve = 45f;
        [SerializeField, Tooltip("y-delta")]
        private float maxSlope = 5f; 

        [Header("Rewards & Penalties")]
        [SerializeField, Tooltip("Multiplied with dot product of velocity and track tangent")]
        private float speedRewardMultiplier = 0.001f; 
        [SerializeField, Tooltip("Set when this agent is passed by another one")]
        private float loosingRankPenalty = -0.5f; 
        [SerializeField, Tooltip("Set only when this agent is tailing the one it collides with")]
        private float collisionPenalty = -0.25f; 

        
        public void Initialize(bool animateBike)
        {
            isDiscrete = GetComponent<BehaviorParameters>()
                .BrainParameters.VectorActionSpaceType == SpaceType.Discrete;

            Shared = new SharedObs();
            Bike = GetComponentInChildren<Bike>();
            Bike.Initialize(animateBike);
            track = FindObjectOfType<TrackManager>();

            allAgents = FindObjectsOfType<BikeAgent>();
            observedAgents = new List<BikeAgent>(observedAgentsCount);
        }

        public override void OnEpisodeBegin()
        {
            Vector3 pV3 = Position;
            crntSeg = track.Segments.FindClosest(pV3);
            crntPos = track.Positions.FindClosest(pV3);
            Shared.CrntPos = crntPos;
            crntRank = -1;
            crntBoost = 0;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            // Sort by relative lead.
            foreach (BikeAgent other in allAgents)
            {
                other.Shared.TmpDelta = Mathf.DeltaAngle(
                    Shared.CrntPos.PosDeg, other.Shared.CrntPos.PosDeg);
            }
            Array.Sort(allAgents);

            int index = Array.IndexOf(allAgents, this);
            if (index < crntRank)
            {
                AddReward(loosingRankPenalty);
            }
            crntRank = index;

            // Filter closest.
            observedAgents.Clear();
            observedAgents.AddRange(allAgents);
            observedAgents.RemoveAt(index);
            while (observedAgents.Count > observedAgentsCount)
            {
                if (Mathf.Abs(observedAgents[0].Shared.TmpDelta) > 
                    Mathf.Abs(observedAgents[observedAgents.Count - 1].Shared.TmpDelta) )
                {
                    observedAgents.RemoveAt(0);
                }
                else
                {
                    observedAgents.RemoveAt(observedAgents.Count - 1);
                }
            }

            foreach (BikeAgent other in observedAgents)
            {
                bool ignore = Mathf.Abs(other.Shared.TmpDelta) > maxAgentDistance;
                sensor.AddObservation(Mathf.Clamp(
                    other.Shared.TmpDelta / maxAgentDistance, -1f, 1f));
                sensor.AddObservation(ignore ? 0 : other.Shared.NormXPos);
                // Relative velocity in local space.
                sensor.AddObservation(ignore ? Vector3.zero : Normalization
                    .Sigmoid(Bike.Localize(other.Shared.Velocity - Bike.Body.velocity)));
            }

            // Bike physics.
            Vector3 pV3 = Position;
            Vector3 fwd = Bike.transform.forward;
            Vector3 locVelocity = Bike.Localize(Bike.Body.velocity);
            Vector3 locAngVelocity = Bike.Localize(Bike.Body.angularVelocity);

            sensor.AddObservation(Mathf.Clamp(locVelocity.z / 30f - 1f, -1f, 1f)); // 0/+60
            sensor.AddObservation(Normalization.Sigmoid(locVelocity.x));
            sensor.AddObservation(Normalization.Sigmoid(locVelocity.y));
            sensor.AddObservation(Normalization.Sigmoid(locAngVelocity));
            sensor.AddObservation(crntPos.GetNormLocalXPos(pV3));
            sensor.AddObservation(crntPos.GetNormLocalYAngle(fwd));
            sensor.AddObservation(Bike.Inclination);
            if (isDiscrete)
            {
                sensor.AddObservation(Bike.Throttle);
                sensor.AddObservation(Bike.Steer);
            }

            // Modify boost.
            crntSeg = track.Segments.FindClosest(pV3, crntSeg);
            if (track.NormLanes.Contains(crntSeg.GetNormLocalXPos(pV3), out index))
            {
                crntBoost = Mathf.Clamp(crntBoost +
                    crntSeg.BoostValues[index] * boostStrength, -1f, 1f);
            }
            sensor.AddObservation(crntBoost);

            // Look ahead.
            index = crntSeg.Index;
            foreach (int offset in lookAheadBoost)
            {
                Segment seg = track.Segments[index + offset];
                sensor.AddObservation(seg.BoostValues);
            }
            foreach (int offset in lookAheadCurve)
            {
                Segment seg = track.Segments[index + offset];
                float curve = Vector3.SignedAngle(crntSeg.TangentXZ, seg.TangentXZ, Vector3.up);
                sensor.AddObservation(curve / maxCurve);
                float slope = seg.PosV3.y - crntSeg.PosV3.y;
                sensor.AddObservation(slope / maxSlope);
            }
        }

        public override void OnActionReceived(float[] vectorAction)
        {
            if (isDiscrete)
            {
                Bike.OnAgentAction(vectorAction, Time.fixedDeltaTime, crntBoost);
            }
            else
            {
                Bike.OnAgentAction(vectorAction, crntBoost);
            }

            crntBoost -= Time.fixedDeltaTime * boostLevelOff * Mathf.Sign(crntBoost);

            Vector3 pV3 = Position;
            crntPos = track.Positions.FindClosest(pV3, crntPos);
            // Set here, so data for all agents is available on next CollectObservations call.
            Shared.CrntPos = crntPos;
            Shared.Velocity = Bike.Body.velocity;
            Shared.NormXPos = crntPos.GetNormLocalXPos(pV3);

            AddReward(Vector3.Dot(crntPos.Tangent, Shared.Velocity) * speedRewardMultiplier);
            if (Bike.IsColliding)
            {
                AddReward(collisionPenalty);
            }
        }

        public override void Heuristic(float[] actionsOut)
        {
            float v = Input.GetAxis("Vertical");
            float h = Input.GetAxis("Horizontal");

            if (isDiscrete)
            {
                actionsOut[0] = 1;
                actionsOut[1] = 1;

                if (Mathf.Abs(v) > Mathf.Epsilon)
                {
                    actionsOut[0] += Mathf.Sign(v);
                }

                if (Mathf.Abs(h) > Mathf.Epsilon)
                {
                    actionsOut[1] += Mathf.Sign(h);
                }
            }
            else
            {
                actionsOut[0] = v;
                actionsOut[1] = h;
            }
        }
    }
}