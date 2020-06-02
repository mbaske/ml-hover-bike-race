using UnityEngine;
using Hoverbike.Track;
using System.Collections.Generic;

namespace Hoverbike
{
    [RequireComponent(typeof(Camera))]
    public class PilotCam : MonoBehaviour
    {
        private enum Mode
        {
            None = 0,
            Pilot = 1,
        };
        private Mode mode = Mode.None;
        private Camera cam;

        [SerializeField]
        private float clusterLooseness = 25;
        [SerializeField]
        private float directionDamp = 0.5f;
        [SerializeField]
        private float offsetY = 0.5f;

        private Vector3 smoothDir;
        private Vector3 dirVelocity;

        private BikeAgent[] agents;
        private TrackManager track;
        private List<BikeAgent> cluster;
        private BikeAgent clusterTail;
        private BikeAgent clusterLead;
        private BikeAgent camAgent;
        private bool isTailing;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            agents = FindObjectsOfType<BikeAgent>();
            track = FindObjectOfType<TrackManager>();
            cluster = new List<BikeAgent>();
            smoothDir = Vector3.forward;
        }

        private void InterpolatePositions()
        {
            Vector3 tangent = camAgent.Shared.CrntPos.Tangent * (isTailing ? 1 : -1);
            smoothDir = Vector3.SmoothDamp(
                smoothDir,
                tangent,
                ref dirVelocity,
                directionDamp);
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
            cluster.Sort();
            clusterTail = cluster[0];
            clusterLead = cluster[cluster.Count - 1];
        }


        private void PickAgent()
        {
            FindCluster();
            isTailing = !isTailing; // RndFlip();
            camAgent = isTailing ? clusterTail : clusterLead;
        }

        private void FixedUpdate()
        {
            switch (mode)
            {
                case Mode.Pilot:
                    InterpolatePositions();
                    break;

                default:
                    if (agents[0].Shared.CrntPos != null)
                    {
                        mode = Mode.Pilot;
                        PickAgent();
                    }
                    break;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                PickAgent();
            }
        }

        private void LateUpdate()
        {
            if (mode == Mode.Pilot)
            {
                Vector3 normal = camAgent.Shared.CrntPos.Normal;
                Vector3 tangent = camAgent.Shared.CrntPos.Tangent * (isTailing ? 2 : -2);
                transform.position = camAgent.Position + normal * offsetY + tangent;
                transform.rotation = Quaternion.LookRotation(smoothDir);
            }
        }

        private static bool RndFlip(float probability = 0.5f)
        {
            return Random.value <= probability;
        }
    }
}