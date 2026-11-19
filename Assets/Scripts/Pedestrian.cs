using System;
using UnityEngine;

namespace Zombies55
{
    // Ambient NPC. Walks a closed loop of waypoints on a sidewalk, kinematically (no physics queries).
    // Yields to the player, pauses at random corners, and lets CharacterMotion do the animation.
    public class Pedestrian : MonoBehaviour, ICharacterMover
    {
        [Tooltip("World-space loop. Only x and z are used; the NPC keeps its spawn height.")]
        [SerializeField] Vector3[] waypoints;
        [Tooltip("Index of the waypoint this NPC spawned just after; it heads for the next one.")]
        [SerializeField] int startIndex;
        [SerializeField] float speed = 1.4f;

        [Header("Movement")]
        [SerializeField] float turnRate = 200f;       // deg/s
        [SerializeField] float acceleration = 3f;
        [SerializeField] float deceleration = 6f;
        [SerializeField] float reachDistance = 0.3f;

        [Header("Corner pauses")]
        [SerializeField, Range(0f, 1f)] float pauseChance = 0.3f;
        [SerializeField] Vector2 pauseRange = new Vector2(1f, 3f);

        [Header("Yield to player")]
        [SerializeField] float yieldDistance = 1.2f;
        [SerializeField] float yieldAngle = 70f;      // half-angle of the front cone
        [SerializeField] float personalSpace = 0.8f;  // stop for the player at any side but behind

        public const float TopSpeed = 1.7f;

        CharacterMotion _motion;
        Transform _player;
        int _next;
        float _currentSpeed;
        float _pauseTimer;
        float _y;
        Vector3 _velocity;

        public Vector3 Velocity => _velocity;
        public float MaxSpeed => TopSpeed;
        public bool IsGrounded => true;

#pragma warning disable CS0067 // Part of ICharacterMover; walkers never jump or fall.
        public event Action Jumped;
        public event Action<float> Landed;
#pragma warning restore CS0067

        void Awake()
        {
            _y = transform.position.y;
            _motion = GetComponentInChildren<CharacterMotion>();
            if (waypoints != null && waypoints.Length > 0) _next = (startIndex + 1) % waypoints.Length;
        }

        void Start()
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                _player = player.transform;
                if (_motion != null) _motion.LookTarget = _player;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || waypoints == null || waypoints.Length < 2) return;

            Vector3 pos = transform.position;
            Vector3 toTarget = waypoints[_next] - pos;
            toTarget.y = 0f;

            bool paused = _pauseTimer > 0f;
            if (paused) _pauseTimer -= dt;

            // Steer toward the next waypoint, also while paused so corners are turned on the spot.
            float yaw = transform.eulerAngles.y;
            float err = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                float targetYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                err = Mathf.Abs(Mathf.DeltaAngle(yaw, targetYaw));
                yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, turnRate * dt);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            bool hold = paused || PlayerInWay(pos);
            float goal = hold ? 0f : speed;
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, goal, (goal > _currentSpeed ? acceleration : deceleration) * dt);

            // Slow down while still turning toward the waypoint.
            float turnScale = Mathf.Clamp01(1f - err / 90f);
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            _velocity = forward * (_currentSpeed * turnScale);

            Vector3 next = pos + _velocity * dt;
            next.y = _y;
            transform.position = next;

            if (!paused && toTarget.sqrMagnitude < reachDistance * reachDistance)
            {
                _next = (_next + 1) % waypoints.Length;
                if (UnityEngine.Random.value < pauseChance)
                    _pauseTimer = UnityEngine.Random.Range(pauseRange.x, pauseRange.y);
            }
        }

        bool PlayerInWay(Vector3 pos)
        {
            if (_player == null) return false;
            Vector3 d = _player.position - pos;
            if (Mathf.Abs(d.y) > 1.7f) return false; // standing on a crate stack or mid-jump
            d.y = 0f;
            float dist = d.magnitude;
            if (dist > yieldDistance) return false;
            if (dist < 0.01f) return true;
            float angle = Vector3.Angle(transform.forward, d);
            if (angle < yieldAngle) return true;
            return dist < personalSpace && angle < 110f;
        }
    }
}
