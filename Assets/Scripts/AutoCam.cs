using UnityEngine;
using UnityEngine.Audio;
using Hoverbike.Track;
using System.Collections.Generic;

namespace Hoverbike
{
    [RequireComponent(typeof(Camera))]
    public class AutoCam : MonoBehaviour
    {
        private enum Mode
        {
            None = 0,
            Sideline = 1,
            Follow = 2
        };
        private Mode mode = Mode.None;
        private Camera cam;

        [Space, SerializeField]
        private float clusterLooseness = 20;
        [SerializeField]
        private float sidelineToFollowRatio = 0.5f;
        [SerializeField]
        private float targetYOffset = 2;

        [Space, SerializeField]
        private float sidelineTargetDamp = 0.15f;
        [SerializeField]
        private float sidelineCameraDamp = 1;
        [SerializeField]
        private float sidelineXOffset = 6;
        [SerializeField]
        private float sidelineYOffset = 0;
        [SerializeField]
        private float sidelineZOffset = 50;
        [SerializeField]
        private float sidelineStopThresh = 20;
        private Position sidelinePos;

        [Space, SerializeField]
        private float followTargetDamp = 0.15f;
        [SerializeField]
        private float followCameraDamp = 0.25f;
        [SerializeField]
        private float followMinDuration = 4;
        [SerializeField]
        private float followMaxDuration = 8;
        [SerializeField]
        private float followYOffset = 3;
        [SerializeField]
        private float followLeadZOffset = 24;
        [SerializeField]
        private float followTailZOffset = 1;
        private float followZOffset;
        private bool isFollowingTail;

        // [Space, SerializeField]
        // private bool enableAutoZoom;
        // [SerializeField]
        // private float zoomInFOV = 30;
        // [SerializeField]
        // private float zoomOutFOV = 60;

        // [SerializeField]
        // private float zoomInThresh = 90;
        // [SerializeField]
        // private float zoomOutThresh = 80;
        // [SerializeField]
        // private float zoomDamp = 0.2f;
        // private float zoomVelocity;
        // private bool isZoomedIn;

        private Vector3 cameraPos;
        private Vector3 smoothCameraPos;
        private Vector3 cameraVelocity;
        private float cameraDamp;

        private BikeAgent targetAgent;
        private Vector3 smoothTargetPos;
        private Vector3 targetVelocity;
        private float targetDamp;

        private BikeAgent[] agents;
        private TrackManager track;
        private List<BikeAgent> cluster;
        private BikeAgent clusterTail;
        private BikeAgent clusterLead;

        [Space, SerializeField]
        private AudioMixer audioMixer;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            agents = FindObjectsOfType<BikeAgent>();
            track = FindObjectOfType<TrackManager>();
            cluster = new List<BikeAgent>();
        }

        private void InterpolatePositions()
        {
            smoothCameraPos = Vector3.SmoothDamp(
                smoothCameraPos, 
                cameraPos,
                ref cameraVelocity, 
                cameraDamp);
            smoothTargetPos = Vector3.SmoothDamp(
                smoothTargetPos,
                targetAgent.Position + targetAgent.Shared.CrntPos.Normal * targetYOffset,
                ref targetVelocity, 
                targetDamp);
        }

        private void FindCluster()
        {
            cluster.Clear();
            for (int i = 0; i < agents.Length; i++)
            {
                var tmp = new List<BikeAgent>();
                for (int j = 0; j < agents.Length; j++)
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(agents[i].Shared.CrntPos.PosDeg,
                        agents[j].Shared.CrntPos.PosDeg)) < clusterLooseness)
                    {
                        tmp.Add(agents[j]);
                    }
                }
                if (tmp.Count > cluster.Count)
                {
                    cluster = tmp;
                }
            }

            if (cluster.Count == 1)
            {
                // No cluster, find closest excl. previous target.
                float min = Mathf.Infinity;
                foreach (BikeAgent agent in agents)
                {
                    float d = (smoothCameraPos - agent.Position).sqrMagnitude;
                    if (d < min && agent != targetAgent)
                    {
                        min = d;
                        cluster.Clear();
                        cluster.Add(agent);
                    }
                }
            }
            else
            {
                cluster.Sort();
            }

            clusterTail = cluster[0];
            clusterLead = cluster[cluster.Count - 1];
        }

        // Stationary Camera.

        private void StartSidelineMode()
        {
            mode = Mode.Sideline;
            FindCluster();

            targetDamp = sidelineTargetDamp;
            cameraDamp = sidelineCameraDamp;
            targetAgent = clusterTail;
            sidelinePos = track.Positions.AtDegree(
                clusterLead.Shared.CrntPos.PosDeg + sidelineZOffset);
            cameraPos =
                sidelinePos.PosV3 +
                sidelinePos.Orthogonal * sidelineXOffset * Mathf.Sign(sidelinePos.Curvature) +
                sidelinePos.Normal * sidelineYOffset;
        }

        private void UpdateSidelineMode()
        {
            if (Mathf.DeltaAngle(sidelinePos.PosDeg,
                targetAgent.Shared.CrntPos.PosDeg) > sidelineStopThresh)
            {
                StopSidelineMode();
            }
        }

        private void StopSidelineMode()
        {
            PickRandomMode();
        }

        // Following Camera.

        private void StartFollowMode()
        {
            mode = Mode.Follow;
            FindCluster();

            targetDamp = followTargetDamp;
            cameraDamp = followCameraDamp;
            isFollowingTail = !isFollowingTail;
            targetAgent = isFollowingTail ? clusterTail : clusterLead;
            followZOffset = isFollowingTail ? followTailZOffset : followLeadZOffset;
            Invoke("StopFollowMode", Random.Range(followMinDuration, followMaxDuration));
        }

        private void UpdateFollowMode()
        {
            Position pos = targetAgent.Shared.CrntPos;
            cameraPos = targetAgent.Position +
                pos.Normal * followYOffset +
                pos.Tangent * followZOffset;
        }

        private void StopFollowMode()
        {
            CancelInvoke();
            PickRandomMode();
        }

        private void PickRandomMode()
        {
            if (RndFlip(sidelineToFollowRatio))
            {
                StartFollowMode();
            }
            else
            {
                StartSidelineMode();
            }
        }

        private void FixedUpdate()
        {
            switch (mode)
            {
                case Mode.Sideline:
                    UpdateSidelineMode();
                    InterpolatePositions();
                    break;

                case Mode.Follow:
                    UpdateFollowMode();
                    InterpolatePositions();
                    break;

                default:
                    if (agents[0].Shared.CrntPos != null)
                    {
                        StartFollowMode();
                    }
                    break;
            }
        }

        private void LateUpdate()
        {
            transform.position = smoothCameraPos;
            transform.LookAt(smoothTargetPos);

            float vol = Mathf.Min(-(cameraVelocity.magnitude - 50) / 25f, 0);
            audioMixer?.SetFloat("BikeGroupVolume", vol);
        }

        private static bool RndFlip(float probability = 0.5f)
        {
            return Random.value <= probability;
        }

        // private void UpdateZoom()
        // {
        //     float distance = (targetAgent.Position - transform.position).magnitude;

        //     if (isZoomedIn)
        //     {
        //         if (distance < zoomOutThresh)
        //         {
        //             isZoomedIn = false;
        //         }
        //     }
        //     else if (distance > zoomInThresh)
        //     {
        //         isZoomedIn = true;
        //     }

        //     cam.fieldOfView = Mathf.SmoothDamp(cam.fieldOfView,
        //         isZoomedIn ? zoomInFOV : zoomOutFOV, ref zoomVelocity, zoomDamp);
        // }
    }
}