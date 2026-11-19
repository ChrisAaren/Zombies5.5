using System;
using UnityEngine;
using UnityEngine.AI;

namespace Zombies55
{
    // A hooded chaser. NavMeshAgent steering, melee attack with a raised-arm windup, topple-and-sink death,
    // pooled by RoundManager. CharacterMotion animates the rig through ICharacterMover.
    [RequireComponent(typeof(NavMeshAgent))]
    public class Enemy : MonoBehaviour, IDamageable, ICharacterMover
    {
        public const float ShambleSpeed = 1.6f;
        public const float JogSpeed = 3.2f;
        public const float SprintSpeed = 5.0f;

        [Header("Chase")]
        [SerializeField] float repathInterval = 0.25f;
        [SerializeField] float turnRate = 420f;

        [Header("Attack")]
        [SerializeField] float attackRange = 1.6f;
        [SerializeField] float hitRange = 2.0f;
        [SerializeField] float windupSeconds = 0.45f;
        [SerializeField] float recoverSeconds = 0.3f;
        [SerializeField] float attackCooldown = 1.1f;
        [SerializeField] float attackDamage = PlayerHealth.MeleeDamage;

        [Header("Damage")]
        [Tooltip("Multiplier on damage that lands in the head hitbox. 1 = the weapon already scales its own headshots.")]
        [SerializeField] float headDamageMultiplier = 1f;
        [SerializeField] float knockbackSpeed = 4.5f;

        [Header("Death")]
        [SerializeField] float toppleSeconds = 0.55f;
        [SerializeField] float deathSeconds = 2f;
        [SerializeField] float sinkDepth = 0.9f;

        [Header("Stuck")]
        [SerializeField] float stuckSeconds = 6f;

        enum State { Chase, Windup, Recover, Dead }

        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public bool IsAlive => _state != State.Dead && Health > 0f;
        public int Round => _round;
        // 0..1 weight for EnemyAttackPose.
        public float AttackPose { get; private set; }

        // ICharacterMover
        public Vector3 Velocity => _state == State.Dead || _agent == null || !_agent.enabled ? Vector3.zero : _agent.velocity;
        public float MaxSpeed => SprintSpeed;
        public bool IsGrounded => true;
#pragma warning disable CS0067 // Part of ICharacterMover; enemies never jump or fall.
        public event Action Jumped;
        public event Action<float> Landed;
#pragma warning restore CS0067

        NavMeshAgent _agent;
        Collider[] _colliders;
        CharacterMotion _motion;
        RoundManager _owner;

        State _state;
        int _round = 1;
        float _baseSpeed;
        float _repathTimer;
        float _cooldown;
        float _stateTimer;
        float _stagger;
        Vector3 _knock;
        Vector3 _baseScale;

        float _stuckTimer;
        Vector3 _stuckAnchor;

        float _deathTimer;
        Vector3 _deathPos;
        Quaternion _deathStartRot, _deathEndRot;

        int _lastBlastFrame = -1;
        GameObject _lastBlastSource;

        public static float HealthForRound(int round)
        {
            round = Mathf.Max(1, round);
            if (round <= 9) return 100f + 50f * (round - 1);
            return (100f + 50f * 8f) * Mathf.Pow(1.1f, round - 9);
        }

        static float PickSpeed(int round)
        {
            float r = UnityEngine.Random.value;
            float v;
            if (round < 4) v = ShambleSpeed;
            else if (round < 8) v = r < Mathf.Min(0.75f, 0.25f + 0.15f * (round - 4)) ? JogSpeed : ShambleSpeed;
            else
            {
                float sprint = Mathf.Min(0.4f, 0.15f + 0.05f * (round - 8));
                if (r < sprint) v = SprintSpeed;
                else v = (r - sprint) / (1f - sprint) < 0.6f ? JogSpeed : ShambleSpeed;
            }
            return v * UnityEngine.Random.Range(0.92f, 1.08f);
        }

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _agent.updateRotation = false;
            _colliders = GetComponentsInChildren<Collider>(true);
            _motion = GetComponentInChildren<CharacterMotion>(true);
            _baseScale = transform.localScale;
        }

        internal void SetOwner(RoundManager owner) => _owner = owner;

        // Called by the pool: position first, then enable, so the agent lands on the NavMesh.
        public void Activate(Vector3 position, int round)
        {
            _round = round;
            var player = PlayerTransform;
            Vector3 face = player != null ? player.position - position : Vector3.forward;
            face.y = 0f;
            if (face.sqrMagnitude < 0.01f) face = Vector3.forward;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(face));
            transform.localScale = _baseScale == Vector3.zero ? Vector3.one : _baseScale;
            gameObject.SetActive(true);
        }

        static Transform PlayerTransform => PlayerHealth.Instance != null ? PlayerHealth.Instance.transform : null;

        void OnEnable()
        {
            if (_agent == null) return;
            MaxHealth = HealthForRound(_round);
            Health = MaxHealth;
            _state = State.Chase;
            AttackPose = 0f;
            _knock = Vector3.zero;
            _stagger = 0f;
            _stuckTimer = 0f;
            _cooldown = UnityEngine.Random.Range(0.1f, 0.6f);
            _repathTimer = UnityEngine.Random.value * repathInterval;
            _baseSpeed = PickSpeed(_round);

            foreach (var c in _colliders) c.enabled = true;

            _agent.enabled = true;
            _agent.speed = _baseSpeed;
            _agent.acceleration = 14f;
            _agent.angularSpeed = 360f;
            _agent.stoppingDistance = 1.2f;
            _agent.radius = 0.35f;
            _agent.height = 1.8f;
            _agent.avoidancePriority = UnityEngine.Random.Range(30, 70);
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            if (!_agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
            if (_agent.isOnNavMesh) _agent.isStopped = false;
            _stuckAnchor = transform.position;

            if (_motion != null) _motion.LookTarget = PlayerTransform;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_state == State.Dead) { UpdateDeath(dt); return; }
            if (dt <= 0f) return;

            var loop = GameLoop.Instance;
            if (loop != null && loop.State == RunState.Lost)
            {
                if (_agent.enabled && _agent.isOnNavMesh) { _agent.isStopped = true; _agent.velocity = Vector3.zero; }
                AttackPose = Mathf.MoveTowards(AttackPose, 0f, dt * 4f);
                return;
            }

            var player = PlayerTransform;
            if (player == null || !_agent.enabled || !_agent.isOnNavMesh)
            {
                CheckStuck(dt, false, true);
                return;
            }

            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            ApplyKnockback(dt);
            if (_stagger > 0f) _stagger -= dt;
            _agent.speed = _baseSpeed * (_stagger > 0f ? 0.35f : 1f);
            _cooldown -= dt;

            switch (_state)
            {
                case State.Chase:
                    AttackPose = Mathf.MoveTowards(AttackPose, 0f, dt * 5f);
                    _repathTimer -= dt;
                    if (_repathTimer <= 0f)
                    {
                        _repathTimer = repathInterval;
                        _agent.SetDestination(player.position);
                    }
                    Face(_agent.desiredVelocity.sqrMagnitude > 0.04f ? _agent.desiredVelocity : toPlayer, dt);
                    if (dist <= attackRange && _cooldown <= 0f && PlayerHealth.Instance.IsAlive)
                    {
                        _state = State.Windup;
                        _stateTimer = 0f;
                        _agent.isStopped = true;
                        _agent.velocity = Vector3.zero;
                    }
                    CheckStuck(dt, dist > 3f, false);
                    break;

                case State.Windup:
                    _stateTimer += dt;
                    Face(toPlayer, dt);
                    float p = Mathf.Clamp01(_stateTimer / windupSeconds);
                    AttackPose = p * p * (3f - 2f * p);
                    if (_stateTimer >= windupSeconds)
                    {
                        if (dist <= hitRange && PlayerHealth.Instance.IsAlive) Strike(player, toPlayer);
                        _cooldown = attackCooldown;
                        _state = State.Recover;
                        _stateTimer = 0f;
                    }
                    break;

                case State.Recover:
                    _stateTimer += dt;
                    AttackPose = Mathf.Clamp01(1f - _stateTimer / recoverSeconds);
                    if (_stateTimer >= recoverSeconds)
                    {
                        _state = State.Chase;
                        _agent.isStopped = false;
                        _repathTimer = 0f;
                    }
                    break;
            }
        }

        void Strike(Transform player, Vector3 toPlayer)
        {
            PlayerHealth.Instance.TakeDamage(new DamageInfo
            {
                Amount = attackDamage,
                Point = player.position + Vector3.up * 1.2f,
                Direction = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : transform.forward,
                Zone = HitZone.Body,
                Kind = DamageKind.Melee,
                Source = gameObject,
                FromPlayer = false,
            });
        }

        void Face(Vector3 dir, float dt)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnRate * dt);
        }

        void ApplyKnockback(float dt)
        {
            if (_knock.sqrMagnitude < 0.01f) return;
            _agent.Move(_knock * dt);
            _knock = Vector3.Lerp(_knock, Vector3.zero, 1f - Mathf.Exp(-7f * dt));
        }

        // No progress for stuckSeconds (while far from the player) or off the NavMesh for 1 s: respawn elsewhere.
        void CheckStuck(float dt, bool counts, bool offMesh)
        {
            if (offMesh)
            {
                _stuckTimer += dt * (stuckSeconds / 1f);
            }
            else if (counts)
            {
                if ((transform.position - _stuckAnchor).sqrMagnitude > 0.5f * 0.5f)
                {
                    _stuckAnchor = transform.position;
                    _stuckTimer = 0f;
                }
                else _stuckTimer += dt;
            }
            else
            {
                _stuckAnchor = transform.position;
                _stuckTimer = 0f;
            }

            if (_stuckTimer < stuckSeconds) return;
            _stuckTimer = 0f;
            _stuckAnchor = transform.position;
            if (_owner != null && _owner.TryGetSpawnPoint(out var p))
            {
                if (_agent.enabled) _agent.Warp(p); else transform.position = p;
                _agent.enabled = true;
                if (_agent.isOnNavMesh) _agent.isStopped = false;
                _stuckAnchor = p;
            }
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;

            // Blast overlaps can reach several hitboxes of one enemy; count each explosion once.
            if (info.Kind == DamageKind.Explosion)
            {
                if (_lastBlastFrame == Time.frameCount && _lastBlastSource == info.Source) return;
                _lastBlastFrame = Time.frameCount;
                _lastBlastSource = info.Source;
            }

            float amount = info.Amount * (info.Zone == HitZone.Head ? headDamageMultiplier : 1f);
            if (amount <= 0f) return;
            Health -= amount;
            bool lethal = Health <= 0f;

            if (info.Kind == DamageKind.Explosion)
            {
                Vector3 dir = info.Direction;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = transform.position - info.Point;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    _knock = dir.normalized * knockbackSpeed * Mathf.Clamp(info.Amount / 60f, 0.5f, 1.5f);
            }
            else _stagger = 0.15f;

            var wallet = PlayerWallet.Instance;
            if (info.FromPlayer && wallet != null)
            {
                if (!lethal)
                {
                    if (info.Kind == DamageKind.Bullet) wallet.Add(10, "Hit");
                }
                else if (info.Kind == DamageKind.Explosion) wallet.Add(50, "Explosion kill");
                else if (info.Kind == DamageKind.Bullet && info.Zone == HitZone.Head) wallet.Add(100, "Headshot");
                else wallet.Add(60, "Kill");
            }

            if (lethal) Die(info);
        }

        void Die(DamageInfo info)
        {
            Health = 0f;
            _state = State.Dead;
            AttackPose = 0f;
            foreach (var c in _colliders) c.enabled = false;
            if (_agent.enabled) _agent.enabled = false;
            if (_motion != null) _motion.LookTarget = null;

            Vector3 away = info.Direction;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f) away = -transform.forward;
            away.Normalize();

            _deathTimer = 0f;
            _deathPos = transform.position;
            _deathStartRot = transform.rotation;
            _deathEndRot = Quaternion.AngleAxis(90f, Vector3.Cross(Vector3.up, away)) * _deathStartRot;

            EnemyEffects.Instance?.DustPuff(_deathPos);
            RoundAudio.Instance?.PlayEnemyDeath(_deathPos + Vector3.up);

            if (_owner != null) _owner.ReportKilled(this, info);
        }

        void UpdateDeath(float dt)
        {
            _deathTimer += dt;
            // Topple: ease in like a fall, with a short settle.
            float f = Mathf.Clamp01(_deathTimer / toppleSeconds);
            transform.rotation = Quaternion.Slerp(_deathStartRot, _deathEndRot, f * f);

            // Sink and shrink away over the rest of the time.
            float sinkStart = toppleSeconds + 0.3f;
            float s = Mathf.Clamp01((_deathTimer - sinkStart) / Mathf.Max(0.01f, deathSeconds - sinkStart));
            transform.position = _deathPos + Vector3.down * (sinkDepth * s * s);
            transform.localScale = _baseScale * (1f - 0.5f * s);

            if (_deathTimer >= deathSeconds)
            {
                gameObject.SetActive(false);
                if (_owner != null) _owner.Release(this);
            }
        }
    }
}
