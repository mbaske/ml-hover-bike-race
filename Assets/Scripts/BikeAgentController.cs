using Unity.MLAgents;
using UnityEngine;
using Hoverbike.Track;

namespace Hoverbike
{
    public class BikeAgentController : MonoBehaviour
    {
        private enum Mode
        {
            Demo = 0,
            Training = 1
        }
        [SerializeField]
        private Mode mode;
        private bool isTraining => mode == Mode.Training;
        [SerializeField]
        private float crashPenalty = -1f;

        private BikeAgent[] agents;
        private TrackManager track;

        private void Awake()
        {
            agents = FindObjectsOfType<BikeAgent>();
            track = FindObjectOfType<TrackManager>();

            foreach (BikeAgent agent in agents)
            {
                agent.Initialize(!isTraining);
            }

            Academy.Instance.AgentPreStep += OnAgentPreStep;
        }

        private void OnDestroy()
        {
            if (Academy.IsInitialized)
            {
                Academy.Instance.AgentPreStep -= OnAgentPreStep;
            }
        }

        private void OnAgentPreStep(int academyStepCount)
        {
            bool resetAll = false;
            foreach (BikeAgent agent in agents)
            {
                if (agent.Bike.State == Bike.BikeState.Drive && agent.Bike.IsOffTrack)
                {
                    if (isTraining)
                    {
                        agent.SetReward(crashPenalty);
                        track.Randomize();
                        resetAll = true;
                    }
                    else
                    {
                        agent.Bike.SetState(Bike.BikeState.Crash);
                    }
                }
            }

            foreach (BikeAgent agent in agents)
            {
                if (resetAll)
                {
                    agent.Bike.SetState(Bike.BikeState.Reset);
                    agent.EndEpisode();
                }
                else
                {
                    switch (agent.Bike.State)
                    {
                        case Bike.BikeState.Drive:
                            agent.RequestDecision();
                            break;

                        case Bike.BikeState.Start:
                        case Bike.BikeState.Reset:
                            agent.Bike.SetState(isTraining ? Bike.BikeState.Drive : Bike.BikeState.Spawn);
                            break;

                        case Bike.BikeState.Spawn:
                            if (agent.Bike.IsReady)
                            {
                                agent.Bike.SetState(Bike.BikeState.Drive);
                            }
                            break;

                        case Bike.BikeState.Crash:
                            if (agent.Bike.IsReady)
                            {
                                agent.Bike.SetState(Bike.BikeState.Reset);
                                agent.EndEpisode();
                            }
                            break;
                    }
                }
            }
        }
    }
}