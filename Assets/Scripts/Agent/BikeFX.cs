using UnityEngine;

namespace MBaske.Hoverbikes
{ 
    public class BikeFX : MonoBehaviour
    {
        [SerializeField]
        private GameObject m_FX;

        [Space, SerializeField]
        private GameObject m_SparksPrefab;

        // Not used.
        [SerializeField]
        private ParticleSystem m_Smoke;
        private bool m_EmitSmoke;
        private float m_EmitStart;
        [SerializeField]
        private float m_EmitDuration = 2;

        [Space, SerializeField]
        private Transform m_PilotNeck;
        [SerializeField]
        private float m_TurnHead = 10;

        [Header("Audio")]
        [SerializeField]
        private AudioSource m_Fuzz;
        [SerializeField]
        private AudioSource m_Scream;
        [SerializeField]
        private AudioSource m_Tie;
        [SerializeField]
        private AudioSource m_OneShotAudio;
        [SerializeField]
        private AudioClip[] m_CollisionClips;
        [SerializeField]
        private AudioClip[] m_CrashClips;

        private Rigidbody m_Rigidbody;
        private BikePhysics m_BikePhysics;

        private void Start()
        {
            m_FX.SetActive(true);
            m_Rigidbody = GetComponent<Rigidbody>();
            m_BikePhysics = GetComponent<BikePhysics>();
        }

        private void Update()
        {
            float turn = m_BikePhysics.LocalAngularVelocity.y;
            m_PilotNeck.localRotation = Quaternion.Euler(-20, turn * m_TurnHead, 0);

            float speed = m_Rigidbody.velocity.magnitude;
            m_Fuzz.volume = Mathf.Min(1, speed / 120f);
            m_Tie.volume = Mathf.Min(1, speed / 120f);
            m_Scream.volume = Mathf.Min(1, speed / 60f);
            m_Scream.pitch = 1f + Mathf.Min(1, speed / 180f);

            //if (m_EmitSmoke)
            //{
            //    m_Smoke.Emit(1);
            //    m_EmitSmoke = Time.time - m_EmitStart < m_EmitDuration;
            //}
        }

        private void OnCollisionEnter(Collision other)
        {
            if (m_FX.activeSelf)
            {
                // Only register collision if this bike is tailing other.
                bool isColliding = Vector3.Dot(transform.forward,
                    other.transform.position - transform.position) > 0;

                if (isColliding)
                {
                    float force = other.relativeVelocity.magnitude;

                    if (force > 5)
                    {
                        Instantiate(m_SparksPrefab, other.GetContact(0).point, Quaternion.identity, transform);

                        //if (force > 10)
                        //{
                        //    StartSmoke();
                        //}
                    }

                    float volume = Mathf.Clamp(force / 10f, 0.2f, 1f);
                    m_OneShotAudio.PlayOneShot(m_CollisionClips[Random.Range(0, m_CollisionClips.Length)], volume);
                }
            }
        }


        // Not used.

        private void StartSmoke()
        {
            m_EmitSmoke = true;
            m_EmitStart = Time.time;
        }

        private void OnCrash()
        {
            m_Rigidbody.drag = 0;
            m_Rigidbody.angularDrag = 0;
            // TBD crash force.
            m_Rigidbody.AddForce(transform.forward * 10f, ForceMode.VelocityChange);
            m_Rigidbody.AddTorque(transform.right * -25f, ForceMode.VelocityChange);
            m_OneShotAudio.PlayOneShot(m_CrashClips[Random.Range(0, m_CrashClips.Length)]);
            StartSmoke();
        }
    }
}