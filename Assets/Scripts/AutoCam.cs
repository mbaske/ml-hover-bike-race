using System.Linq;
using UnityEngine;

namespace MBaske.Hoverbikes
{
    public class AutoCam : MonoBehaviour
    {
        private Track m_Track;
        private DriverAgent[] m_Agents;
        private DriverAgent m_TargetAgent;

        [SerializeField]
        private float m_FollowDuration = 10;

        [SerializeField]
        private int m_SwitchDistance = 100;
        [SerializeField]
        private int m_StationaryOffset = 200;
        [SerializeField]
        private int m_DistanceFollowing = 10;

        private bool m_IsStationary = true;
        private bool m_DistanceSwitchEnabled = true;

        [SerializeField]
        private float m_MoveDampStationary = 1f;
        [SerializeField]
        private float m_LookDampStationary = 1f;

        [SerializeField]
        private float m_MoveDampFollow = 0.5f;
        [SerializeField]
        private float m_LookDampFollow = 0.5f;

        private Vector3 m_CamPos;
        private Vector3 m_LookDir;

        private Vector3 m_MoveVlc;
        private Vector3 m_LookVlc;

        private void Awake()
        {
            m_Track = FindObjectOfType<Track>();
            m_Agents = FindObjectsOfType<DriverAgent>();

            FindTarget();
            SetStationaryCamPos();
        }

        private void LateUpdate()
        {
            if (m_IsStationary)
            {
                UpdateStationary();
            }
            else
            {
                UpdateFollow();
            }
        }

        private void Switch()
        {
            FindTarget();

            m_IsStationary = !m_IsStationary || Random.value < 0.5f;

            if (m_IsStationary)
            {
                m_DistanceSwitchEnabled = false;
                SetStationaryCamPos();
            }
            else
            {
                Invoke("Switch", m_FollowDuration);
            }
        }

        private void FindTarget()
        {
            m_TargetAgent = m_Agents.OrderByDescending(o => o.NumNearbyOpponents).First();
        }

        private void SetStationaryCamPos()
        {
            var p = m_Track.GetPoint(m_TargetAgent.TrackPoint.Index + m_StationaryOffset);
            bool inside = Random.value < 0.5f;

            if (inside)
            {
                m_CamPos = p.Position;
            }
            else
            {
                m_CamPos = p.Position + p.Up * Random.Range(5f, 20f);
            }
        }

        private void UpdateStationary()
        {
            Vector3 pSelf = transform.position;
            Vector3 pAgent = m_TargetAgent.transform.position;
            transform.position = Vector3.SmoothDamp(pSelf, m_CamPos, ref m_MoveVlc, m_MoveDampStationary);
            m_LookDir = Vector3.SmoothDamp(m_LookDir, (pAgent - pSelf).normalized, ref m_LookVlc, m_LookDampStationary);
            transform.rotation = Quaternion.LookRotation(m_LookDir);

            float distance = (pAgent - pSelf).magnitude;
            if (m_DistanceSwitchEnabled)
            {
                if (distance > m_SwitchDistance)
                {
                    Switch();
                }
            }
            else if (distance < m_SwitchDistance)
            {
                m_DistanceSwitchEnabled = true;
            }
        }

        private void UpdateFollow()
        {
            var p = m_Track.GetPoint(m_TargetAgent.TrackPoint.Index - m_DistanceFollowing);
            m_CamPos = p.Position + p.Forward * m_TargetAgent.ZOffset;

            Vector3 pSelf = transform.position;
            transform.position = Vector3.SmoothDamp(pSelf, m_CamPos, ref m_MoveVlc, m_MoveDampFollow);
            m_LookDir = Vector3.SmoothDamp(m_LookDir, p.Forward, ref m_LookVlc, m_LookDampFollow);
            transform.rotation = Quaternion.LookRotation(m_LookDir);
        }
    }
}