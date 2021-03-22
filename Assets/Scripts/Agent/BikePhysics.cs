using UnityEngine;

namespace MBaske.Hoverbikes
{
    public class BikePhysics : MonoBehaviour
    {
        public Vector3 Inclination => new Vector3(transform.right.y, transform.up.y, transform.forward.y);
        public Vector3 LocalAngularVelocity => Localize(m_Rigidbody.angularVelocity);
        public Vector3 LocalVelocity => Localize(m_Rigidbody.velocity);

        // Can be positive or negative (slows bike down).
        public float NormalizedBoost { get; private set; }
        public bool IsOffTrack { get; private set; }

        [Header("Forces")]
        [SerializeField]
        private float m_Turn = 10000;
        [SerializeField]
        private float m_Swerve = 5000;
        [SerializeField]
        private float m_DefaultThrottle = 25000;
        [SerializeField]
        private float m_MaxBoost = 20000;
        [SerializeField]
        private float m_BoostIncrement = 0.02f;
        [SerializeField]
        private float m_BoostAttenuation = 0.99f;
        //[SerializeField]
        private float m_CrntThrottle;

        [Header("Hover")]
        [SerializeField]
        private float m_HoverHeight = 1.1f;
        private float m_RayLength;
        [SerializeField]
        private float m_HoverTilt = 0.2f;
        [SerializeField]
        private Transform m_Hover;
        private Transform[] m_HoverHelpers;
        private Rigidbody m_Rigidbody;

        private float m_DefAngularDrag;
        private float m_DefDrag;

        private const int c_LayerMask = 1 << 6;
        private const int c_MinContacts = 3;

        private void Awake()
        {
            Initialize();
        }

        public void Initialize()
        {
            m_RayLength = m_HoverHeight * 5f; // TBD

            m_Rigidbody = GetComponent<Rigidbody>();
            m_DefAngularDrag = m_Rigidbody.angularDrag;
            m_DefDrag = m_Rigidbody.drag;

            m_HoverHelpers = new Transform[4];
            Vector3 com = Vector3.zero;

            for (int i = 0; i < 4; i++)
            {
                m_HoverHelpers[i] = m_Hover.GetChild(i);
                com += m_HoverHelpers[i].localPosition;
            }
            m_Rigidbody.centerOfMass = com / 4f;
        }

        public void ManagedReset()
        {
            IsOffTrack = false;
            NormalizedBoost = 0;
            m_Rigidbody.angularDrag = m_DefAngularDrag;
            m_Rigidbody.drag = m_DefDrag;
            m_Rigidbody.angularVelocity = Vector3.zero;
            m_Rigidbody.velocity = Vector3.zero;
        }

        public Vector3 Localize(Vector3 v)
        {
            return transform.InverseTransformVector(v);
        }

        public void ManagedUpdate(float[] actions, int boost)
        {
            float turn = LocalAngularVelocity.y;

            int n = 0, i = 0;
            for (; i < 4; i++)
            {
                Transform tf = m_HoverHelpers[i];
                Vector3 pos = tf.position;
                Vector3 fwd = tf.forward;

                if (Physics.Raycast(pos, fwd, out RaycastHit hit, m_RayLength, c_LayerMask))
                {
                    float zTilt = turn * Mathf.Sign(tf.localPosition.x);
                    float error = Mathf.Max(0, m_HoverHeight - hit.distance - zTilt * m_HoverTilt);
                    m_Rigidbody.AddForceAtPosition(fwd * -error, pos, ForceMode.VelocityChange);
                    n++;
                }
            }
            IsOffTrack = n < c_MinContacts;

            NormalizedBoost = Mathf.Clamp(NormalizedBoost + boost * m_BoostIncrement, -1f, 1f);
            m_CrntThrottle = m_DefaultThrottle + NormalizedBoost * m_MaxBoost;
            NormalizedBoost *= m_BoostAttenuation;

            m_Rigidbody.AddRelativeForce(Vector3.forward * actions[0] * m_CrntThrottle);
            m_Rigidbody.AddRelativeForce(Vector3.right * actions[1] * m_Swerve);
            m_Rigidbody.AddRelativeTorque(Vector3.up * actions[1] * m_Turn);
        }
    }
}