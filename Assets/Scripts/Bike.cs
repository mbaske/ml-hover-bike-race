using UnityEngine;
using System.Linq;

namespace Hoverbike
{
    public class Bike : MonoBehaviour
    {
        public Vector3 Inclination => new Vector3(
            transform.right.y, transform.up.y, transform.forward.y);
        public Rigidbody Body { get; private set; }
        public bool IsOffTrack { get; private set; }
        public bool IsColliding { get; private set; }
        // Controlled by agent, normalized -1/+1.
        public float Throttle { get; private set; }
        public float Steer { get; private set; }

        public enum BikeState
        {
            Start = 0,
            Reset = 1,
            Spawn = 2,
            Drive = 3,
            Crash = 4
        };
        public BikeState State { get; private set; }
        public bool IsReady => Time.time > readyTime;
        private float readyTime;

        [Header("Discrete control")]
        [SerializeField, Tooltip("Per step change of normalized value, multiplied by delta time")]
        private float throttleIncr = 1;
        [SerializeField, Tooltip("Per step change of normalized value, multiplied by delta time")]
        private float steerIncr = 3;

        [Header("Forces")]
        [SerializeField, Tooltip("Fixed maximum force")]
        private float turn = 20000f;
        [SerializeField, Tooltip("Fixed maximum force")]
        private float swerve = 10000f;
        [SerializeField, Tooltip("Default force without boost")]
        private float defaultThrottle = 25000f;
        [SerializeField, Tooltip("Maximum boost range (positive and negative)")]
        private float boost = 20000f;
        [SerializeField, Tooltip("DEBUG: Current maximum throttle (default + boost)")]
        private float crntThrottle;
        private float locAngVeloY;

        [Header("Hover")]
        [SerializeField]
        private Transform[] sensors;
        [SerializeField, Tooltip("From sensor to ground (in sensor z-direction)")]
        private float distance = 1.5f;
        private float rayLength;
        [SerializeField, Tooltip("How strongly the bike tilts sideways when steering")]
        private float tilt = 0.15f;

        private Vector3 defPos;
        private Quaternion defRot;
        private float defDrag;
        private float defAngDrag;

        private bool fxEnabled;
        [Header("FX")]
        [SerializeField]
        private GameObject FXContainer;
        [SerializeField]
        private ParticleSystem smoke;
        [SerializeField]
        private GameObject sparksPrefab;
        [SerializeField]
        private Transform pilotNeck;
        [SerializeField]
        private float turnHead = 10;
        [SerializeField]
        private Transform rotor1;
        [SerializeField]
        private float spinSpeed1 = 500;
        [SerializeField]
        private Transform rotor2;
        [SerializeField]
        private float spinSpeed2 = -500;

        [SerializeField]
        private float spawnTimeout = 0;
        [SerializeField]
        private float crashTimeout = 3;

        [Header("Audio")]
        [SerializeField]
        private AudioSource fuzz;
        [SerializeField]
        private AudioSource scream;
        [SerializeField]
        private AudioSource tie;
        [SerializeField]
        private AudioSource audioSrc;
        [SerializeField]
        private AudioClip[] collisionClips;
        [SerializeField]
        private AudioClip[] crashClips;

        private static int mask = 1 << Layers.Ground;
        // If less than 3 out of 4 sensors can detect any
        // ground, the bike is considered to be off track.
        private static int minReqContacts = 3;

        public void Initialize(bool enableFX = false)
        {
            fxEnabled = enableFX;
            FXContainer.SetActive(enableFX);

            rayLength = distance * 3f; // TBD

            defPos = transform.position;
            defRot = transform.rotation;

            Body = GetComponent<Rigidbody>();
            Body.centerOfMass = new Vector3(
                sensors.Average(v => v.localPosition.x),
                sensors.Average(v => v.localPosition.y),
                sensors.Average(v => v.localPosition.z));

            defDrag = Body.drag;
            defAngDrag = Body.angularDrag;
        }

        // TODO Dynamic spawn location.
        public void SetState(BikeState state, Vector3? pos = null, Quaternion? rot = null)
        {
            State = state;

            switch (state)
            {
                case BikeState.Reset:
                    transform.position = pos ?? defPos;
                    transform.rotation = rot ?? defRot;
                    Body.velocity = Vector3.zero;
                    Body.angularVelocity = Vector3.zero;
                    Body.drag = defDrag;
                    Body.angularDrag = defAngDrag;
                    Steer = 0;
                    Throttle = 0;
                    IsOffTrack = false;
                    IsColliding = false;
                    locAngVeloY = 0;
                    break;

                // TODO Spawn animation?
                case BikeState.Spawn:
                    readyTime = Time.time + spawnTimeout;
                    StopSmoke();
                    break;
                    
                case BikeState.Crash:
                    readyTime = Time.time + crashTimeout;
                    Body.drag = 0;
                    Body.angularDrag = 0;
                    // TBD crash force.
                    Body.AddForce(transform.forward * 10f, ForceMode.VelocityChange);
                    Body.AddTorque(transform.right * -25f, ForceMode.VelocityChange);
                    audioSrc.PlayOneShot(crashClips[Random.Range(0, crashClips.Length)]);
                    StartSmoke();
                    break;
            }
        }

        // Agent can brake (throttle action < 0), but not go backwards.

        // Continuous.
        public void OnAgentAction(float[] actions, float boost)
        {
            crntThrottle = defaultThrottle + this.boost * boost;
            Throttle = Localize(Body.velocity).z > 0 || actions[0] > 0 ? actions[0] : 0;
            Steer = actions[1];

            ApplyForces();
        }

        // Discrete.
        public void OnAgentAction(float[] actions, float deltaTime, float boost)
        {
            crntThrottle = defaultThrottle + this.boost * boost;
            Throttle += throttleIncr * deltaTime * Mathf.RoundToInt(actions[0] - 1);
            Throttle = Localize(Body.velocity).z > 0 || Throttle > 0 ? Throttle : 0;
            Throttle = Mathf.Clamp(Throttle, -1f, 1f);
            Steer += steerIncr * deltaTime * Mathf.RoundToInt(actions[1] - 1);
            Steer = Mathf.Clamp(Steer, -1f, 1f);

            ApplyForces();
        }

        public Vector3 Localize(Vector3 v)
        {
            return transform.InverseTransformVector(v);
        }

        private void ApplyForces()
        {
            locAngVeloY = Localize(Body.angularVelocity).y;

            int n = 0, i = 0;
            for (; i < sensors.Length; i++)
            {
                Transform s = sensors[i];
                Vector3 pos = s.position;
                Vector3 fwd = s.forward;
                if (Physics.Raycast(pos, fwd, out RaycastHit hit, rayLength, mask))
                {
                    float zTilt = locAngVeloY * Mathf.Sign(s.localPosition.x);
                    float error = Mathf.Max(0, distance - hit.distance - zTilt * tilt);
                    Body.AddForceAtPosition(fwd * -error, pos, ForceMode.VelocityChange);
                    n++;
                }
            }

            Body.AddRelativeForce(Vector3.forward * Throttle * crntThrottle);
            Body.AddRelativeForce(Vector3.right * Steer * swerve);
            Body.AddRelativeTorque(Vector3.up * Steer * turn);

            IsOffTrack = n < minReqContacts;
        }

        private void Update()
        {
            if (fxEnabled)
            {
                switch (State)
                {
                    case BikeState.Drive:
                        pilotNeck.localRotation = Quaternion.Euler(
                            0, locAngVeloY * locAngVeloY * turnHead, 0);
                        rotor1.Rotate(0, spinSpeed1 * Time.deltaTime, 0);
                        rotor2.Rotate(0, spinSpeed2 * Time.deltaTime, 0);

                        float speed = Body.velocity.magnitude;
                        fuzz.volume = Mathf.Min(1, speed / 120f);
                        tie.volume = Mathf.Min(1, speed / 120f);
                        scream.volume = Mathf.Min(1, speed / 60f);
                        scream.pitch = 1f + Mathf.Min(1, speed / 180f);
                        break;
                }
            }
        }

        private void OnCollisionEnter(Collision other)
        {
            // Only register collision if this bike is tailing other.
            IsColliding = Vector3.Dot(transform.forward,
                other.transform.position - transform.position) > 0;

            if (IsColliding && fxEnabled)
            {
                float impact = other.relativeVelocity.magnitude;
                if (impact > 5)
                {
                    Instantiate(sparksPrefab, other.GetContact(0).point, Quaternion.identity, transform);

                    if (impact > 10)
                    {
                        StartSmoke();
                    }
                }

                float vol = Mathf.Clamp(impact / 10f, 0.2f, 1f);
                audioSrc.PlayOneShot(collisionClips[Random.Range(0, collisionClips.Length)], vol);
            }
        }

        private void OnCollisionExit(Collision other)
        {
            IsColliding = false;
        }

        private void StartSmoke()
        {
            if (!smoke.isPlaying)
            {
                smoke.Play();
                Invoke("StopSmoke", 2);
            }
        }

        private void StopSmoke()
        {
            CancelInvoke();
            smoke.Stop();
        }
    }
}