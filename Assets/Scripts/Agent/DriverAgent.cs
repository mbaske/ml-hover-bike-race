using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using MBaske.Sensors.Grid;
using System.Collections.Generic;
using System;

namespace MBaske.Hoverbikes
{
    public class Opponent
    {
        public DriverAgent Agent;
        public int Delta;
    }

    public class DriverAgent : Agent, IPixelGridProvider
    {
        public int DecisionInterval { get; set; }
        public int NumNearbyOpponents { get; set; }

        public Track.Point TrackPoint { get; private set; }
        public float NormalizedXOffset { get; private set; }
        public float ZOffset { get; private set; }

        private float m_NormalizedFwdAngle;
        private const float c_MaxAngle = 45;

        private float[] m_PrevActions;
        private Quaternion m_DefRot;
        private Vector3 m_DefPos;

        private AgentController m_Ctrl;
        private BikePhysics m_Physics;

        private Track m_Track;
        private Lanes m_Lanes;
        private int m_NumLanes;
        private int m_CrntBoost;

        private List<Opponent> m_Opponents;
        private int m_NumOpponents;
        private const int c_NearbyThresh = 50;

        private const int c_GridHeight = 64;
        private const int c_GridWidth = 20;
        private PixelGrid m_Grid;
        private GridSensor m_Sensor;


        public override void Initialize()
        {
            m_Ctrl = FindObjectOfType<AgentController>();
            m_PrevActions = new float[2];

            m_Physics = GetComponent<BikePhysics>();
            m_Physics.Initialize();

            m_Track = FindObjectOfType<Track>();
            m_Lanes = FindObjectOfType<Lanes>();
            m_NumLanes = Lanes.NumLanes;

            m_DefRot = transform.rotation;
            m_DefPos = transform.position;

            var agents = FindObjectsOfType<DriverAgent>();
            m_NumOpponents = agents.Length - 1;

            m_Opponents = new List<Opponent>(m_NumOpponents);

            for (int i = 0; i < agents.Length; i++)
            {
                if (agents[i] != this)
                {
                    m_Opponents.Add(new Opponent()
                    {
                        Agent = agents[i]
                    }); 
                }
            }

            InitGrid();
        }

        public void ResetToStartPosition()
        {
            m_Physics.ManagedReset();
            transform.rotation = m_DefRot;
            transform.position = m_DefPos;
            TrackPoint = m_Track.FindPoint(m_DefPos);
        }

        public void ResetPosition()
        {
            m_Physics.ManagedReset();
            TrackPoint = m_Track.GetPoint(TrackPoint.Index - 5); // TBD offset
            transform.position = TrackPoint.Position;
            transform.rotation = TrackPoint.Rotation;
        }


        public override void OnEpisodeBegin()
        {
            if (m_Sensor == null)
            {
                m_Sensor = GetComponent<GridSensorComponentBase>().Sensor;
                m_Sensor.UpdateEvent += OnSensorUpdate;
            }
            Array.Clear(m_PrevActions, 0, 2);
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            Vector3 pos = transform.position;
            TrackPoint = m_Track.FindPoint(pos, TrackPoint);
            UpdateOpponentDeltas();

            Vector3 delta = pos - TrackPoint.Position;
            float dot = Vector3.Dot(delta, TrackPoint.Forward);
            ZOffset = dot;
            Vector3 closest = TrackPoint.Position + TrackPoint.Forward * dot;
            delta = closest - pos;
            dot = Vector3.Dot(delta, TrackPoint.Right);
            NormalizedXOffset = Mathf.Clamp(
                delta.magnitude / m_Track.HalfWidth * Mathf.Sign(dot), 
                -1f, 1f);
            // Left/right offset from track center.
            sensor.AddObservation(NormalizedXOffset);

            m_NormalizedFwdAngle = Mathf.Clamp(
                Vector3.SignedAngle(TrackPoint.Forward, transform.forward, TrackPoint.Up) / c_MaxAngle, 
                -1f, 1f);
            // Angle facing away from track forward.
            sensor.AddObservation(m_NormalizedFwdAngle);

            // Track curvature look ahead, TBD spacing.
            for (int i = 10; i <= 50; i += 10)
            {
                sensor.AddObservation(m_Physics.Localize(m_Track.GetPoint(TrackPoint.Index + i).Forward));
            }

            sensor.AddObservation(m_Physics.Inclination);
            sensor.AddObservation(m_Physics.NormalizedBoost);

            Vector3 vlc = m_Physics.LocalVelocity;
            sensor.AddObservation(Sigmoid(vlc.x));
            sensor.AddObservation(Sigmoid(vlc.y));
            // Forward speed 0 - 100.
            // http://fooplot.com/#W3sidHlwZSI6MCwiZXEiOiIoeCowLjA1KS8oMSthYnMoKHgqMC4wNSkpKSoyLjQtMSIsImNvbG9yIjoiIzAwMDAwMCJ9LHsidHlwZSI6MTAwMCwid2luZG93IjpbIi01MS4wMzA0Mzc3NjI5MjA2MyIsIjEyNy43ODM0OTY1NjMyNTExIiwiLTguMjk2NzY2MjgxMTI3OTIiLCI5LjU4NDYyNzE1MTQ4OTI0OSJdfV0-
            sensor.AddObservation(Mathf.Clamp(Sigmoid(vlc.z, 0.05f) * 2.4f - 1f, -1f, 1f));
            sensor.AddObservation(Sigmoid(m_Physics.LocalAngularVelocity));

            int lane = Mathf.FloorToInt((NormalizedXOffset + 1) * m_NumLanes * 0.5f);
            m_CrntBoost = m_Lanes.GetBoost(Mathf.Min(lane, m_NumLanes - 1), TrackPoint.Index);
        }

        public override void OnActionReceived(ActionBuffers actionBuffers)
        {
            var actions = actionBuffers.ContinuousActions.Array;
            int step = StepCount % DecisionInterval;

            if (step == 0)
            {
                // Pre-decision step: apply and store current actions.
                Array.Copy(actions, 0, m_PrevActions, 0, 2);
            }
            else
            {
                // Interpolate from previous to current actions.
                float t = step / (float)DecisionInterval;
                for (int i = 0; i < 2; i++)
                {
                    actions[i] = Mathf.Lerp(m_PrevActions[i], actions[i], t);
                }
            }

            m_Physics.ManagedUpdate(actions, m_CrntBoost);
       
            if (m_Physics.IsOffTrack)
            {
                AddReward(-1);
                m_Ctrl.EndEpisode(this);
            }
            else
            {
                // Reward for speed, TBD factor.
                AddReward(m_Physics.LocalVelocity.z * 0.01f);
            }
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var actions = actionsOut.ContinuousActions;
            actions[0] = Input.GetAxis("Vertical");
            actions[1] = Input.GetAxis("Horizontal");
        }

        public void UpdateOpponentDeltas(bool penalize = true, int lookAhead = 5)
        {
            NumNearbyOpponents = 0;

            int n = m_Track.Length;
            int n2 = n / 2;
            int index = TrackPoint.Index; 

            for (int i = 0; i < m_NumOpponents; i++)
            {
                int delta = m_Opponents[i].Agent.TrackPoint.Index - index;
                if (Mathf.Abs(delta) > n2)
                {
                    // Wrap delta.
                    delta -= (int)Mathf.Sign(delta) * n;
                }

                if (penalize && delta > 0 && delta < lookAhead)
                {
                    // Opponent is in front of this agent.
                    if (m_Opponents[i].Delta <= 0)
                    {
                        // Previous delta <= 0 -> opponent was behind.
                        // Penalize for being overtaken.
                        AddReward(-1);
                    }
                }
                m_Opponents[i].Delta = delta;

                if (Mathf.Abs(delta) < c_NearbyThresh)
                {
                    NumNearbyOpponents++;
                }
            }
        }

        public PixelGrid GetPixelGrid()
        {
            InitGrid();
            return m_Grid;
        }

        private void InitGrid()
        {
            m_Grid ??= new PixelGrid(3, c_GridWidth, c_GridHeight);
        }

        private void OnSensorUpdate()
        {
            m_Grid.Clear();

            // TBD offset -> agent rear view extent.
            const int offset = 10;

            // Lanes, channels 0 and 1.

            int wLane = c_GridWidth / m_NumLanes;
            int iStart = TrackPoint.Index - offset;

            for (int y = 0; y < c_GridHeight; y++)
            {
                for (int lane = 0; lane < m_NumLanes; lane++)
                {
                    float boost = m_Lanes.GetBoost(lane, iStart + y);

                    if (boost != 0)
                    {
                        int channel = boost > 0 ? 0 : 1;
                        for (int x = lane * wLane, n = (lane + 1) * wLane; x < n; x++)
                        {
                            m_Grid.Write(channel, x, y, 1);
                        }
                    }
                }
            }

            // Opponents, channel 2.

            int w2 = c_GridWidth / 2;
            int wMax = c_GridWidth - 1;

            for (int i = 0; i < m_NumOpponents; i++)
            {
                int y = m_Opponents[i].Delta + offset;

                if (y >= 0 && y < c_GridHeight)
                {
                    int x = Mathf.RoundToInt((m_Opponents[i].Agent.NormalizedXOffset + 1) * w2);
                    m_Grid.Write(2, Mathf.Min(x, wMax), y, 1);
                }
            }
        }

        private void OnCollisionEnter(Collision other)
        {
            if (other.gameObject.CompareTag("Agent"))
            {
                // Penalize collision if this bike is tailing other.
                if (Vector3.Dot(transform.forward,
                    other.transform.position - transform.position) > 0)
                {
                    AddReward(-0.1f);
                }
            }
        }

        private void OnDestroy()
        {
            m_Sensor.UpdateEvent += OnSensorUpdate;
        }

        private static float Sigmoid(float val, float scale = 1f)
        {
            val *= scale;
            return val / (1f + Mathf.Abs(val));
        }

        private static Vector3 Sigmoid(Vector3 v3, float scale = 1f)
        {
            v3.x = Sigmoid(v3.x, scale);
            v3.y = Sigmoid(v3.y, scale);
            v3.z = Sigmoid(v3.z, scale);
            return v3;
        }
    }
}