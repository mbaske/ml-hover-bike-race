using UnityEngine;

namespace MBaske.Hoverbikes
{
    public class AgentPlacer : MonoBehaviour
    {
        [SerializeField]
        private int m_TrackIndex;
        [SerializeField]
        private int m_Spacing = 10;
        [SerializeField]
        private float m_Offset = 1;

        private void OnValidate()
        {
            var track = GetComponent<Track>();
            var agents = FindObjectsOfType<DriverAgent>();

            for (int i = 0, j = m_TrackIndex; i < agents.Length; i++)
            {
                var p = track.GetPoint(j); 
                float t = agents.Length > 1 ? i / (agents.Length - 1f) : 0;
                Vector3 offset = p.Right * m_Offset;
                offset = Vector3.Lerp(-offset, offset, t);

                agents[i].transform.position = p.Position + offset;
                agents[i].transform.rotation = p.Rotation;
                j += m_Spacing;
            }
        }
    }
}