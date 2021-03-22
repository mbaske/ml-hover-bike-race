using UnityEngine;
using Unity.MLAgents;

namespace MBaske.Hoverbikes
{
    public class AgentController : MonoBehaviour
    {
        [SerializeField]
        private int m_DecisionInterval = 5;
        [SerializeField]
        private int m_MaxEpisodeLength = 5000;
        private int m_EpisodeStepCount;

        private Lanes m_Lanes;
        private DriverAgent[] m_Agents;

        private void Start()
        {
            m_Lanes = FindObjectOfType<Lanes>();
            m_Lanes.RandomizeBoost();

            m_Agents = FindObjectsOfType<DriverAgent>();

            for (int i = 0; i < m_Agents.Length; i++)
            {
                m_Agents[i].DecisionInterval = m_DecisionInterval;
                m_Agents[i].ResetToStartPosition();
            }

            for (int i = 0; i < m_Agents.Length; i++)
            {
                m_Agents[i].UpdateOpponentDeltas(false);
            }

            Academy.Instance.AgentPreStep += MakeRequests;
        }

        public void EndEpisode(DriverAgent agent)
        {
            agent.EndEpisode();
            agent.ResetPosition();
            agent.UpdateOpponentDeltas(false);
        }

        public void EndEpisode()
        {
            m_Lanes.RandomizeBoost();

            for (int i = 0; i < m_Agents.Length; i++)
            {
                m_Agents[i].EndEpisode();
                m_Agents[i].ResetToStartPosition();
            }

            for (int i = 0; i < m_Agents.Length; i++)
            {
                m_Agents[i].UpdateOpponentDeltas(false);
            }
        }

        private void MakeRequests(int academyStepCount)
        {
            if (academyStepCount % m_DecisionInterval == 0)
            {
                for (int i = 0; i < m_Agents.Length; i++)
                {
                    m_Agents[i].RequestDecision();
                }
            }
            else
            {
                for (int i = 0; i < m_Agents.Length; i++)
                {
                    m_Agents[i].RequestAction();
                }
            }

            if (++m_EpisodeStepCount % m_MaxEpisodeLength == 0)
            {
                m_EpisodeStepCount = 0;
                EndEpisode();
            }
        }

        private void OnDestroy()
        {
            if (Academy.IsInitialized)
            {
                Academy.Instance.AgentPreStep -= MakeRequests;
            }
        }
    }
}