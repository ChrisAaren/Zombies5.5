using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Zombies55
{
    public enum ExplosiveKind { Barrel, Car }

    // Barrels blow almost at once and throw a lid and two halves. Parked cars smoke, burn for a fuse, then blow: the car jumps (a
    // temporary 1300 kg Rigidbody, sometimes rolling or flipping), throws its hood, doors, wheels and scraps, turns into a charred wreck
    // and burns for 10-15 s. It settles, becomes kinematic again and carves the NavMesh at its new pose. A round reset restores everything.
    // Credit: any player damage marks the prop as player-caused, and its blast carries FromPlayer onward.
    [RequireComponent(typeof(Collider))]
    public class ExplosiveProp : MonoBehaviour, IDamageable
    {
        [SerializeField] ExplosiveKind kind = ExplosiveKind.Barrel;
        [SerializeField] float maxHealth = 60f;
        [SerializeField] float blastRadius = 6f;
        [SerializeField] float blastDamage = 140f;
        [SerializeField] float carFuseSeconds = 1.5f;
        [SerializeField] Material wreckMaterial;

        // Launch tuning (a parked car is about 1.3 tonnes).
        const float CarMass = 1300f;
        const float HopMin = 1.5f, HopMax = 3f;      // metres of jump
        const float SettleTimeout = 8f;

        static readonly List<ExplosiveProp> s_All = new List<ExplosiveProp>(256);

        float _hp;
        bool _exploded;
        bool _playerCaused;
        float _fuseEnd = -1f;
        float _resetAt = -1f;   // a round started mid-fuse: let the blast happen, then reset
        Renderer[] _renderers;
        Material[][] _origMats;
        Collider[] _colliders;
        NavMeshObstacle _obstacle;
        ParticleSystem _smoke;
        ExplosionFX.BurnSlot _burn;
        RoundManager _rounds;
        Vector3 _homePos;
        Quaternion _homeRot;
        Color _paint = new Color(0.5f, 0.5f, 0.5f);

        // launch state
        Rigidbody _rb;
        float _launchedAt, _stillSince = -1f, _launchY;
        bool _airborne;

        // rock state (a neighbouring car rocks on its suspension when something blows nearby)
        float _rockStart = -1f, _rockAmp;
        Vector3 _rockAxis;
        Vector3 _rockBasePos;
        Quaternion _rockBaseRot;

        public bool IsAlive => !_exploded && _hp > 0f;
        public float Health => _hp;
        public float MaxHealth => maxHealth;
        public ExplosiveKind Kind => kind;
        public bool PlayerCaused => _playerCaused;
        public bool Exploded => _exploded;
        public bool Airborne => _airborne;
        // Measured by the last launch (for tests): peak height above the start, travel on the ground plane, final tilt from upright, seconds to settle.
        public float LastPeakHeight { get; private set; }
        public float LastTravel { get; private set; }
        public float LastTiltDegrees { get; private set; }
        public float LastSettleSeconds { get; private set; } = -1f;
        public float LastMaxTilt { get; private set; }
        public static IReadOnlyList<ExplosiveProp> All => s_All;

        // Editor-time setup with per-kind defaults.
        public void Configure(ExplosiveKind k, Material wreck)
        {
            kind = k;
            wreckMaterial = wreck;
            if (k == ExplosiveKind.Barrel)
            {
                maxHealth = 60f; blastRadius = 6f; blastDamage = 140f;
            }
            else
            {
                maxHealth = 350f; blastRadius = 8f; blastDamage = 190f;
            }
        }

        void Awake()
        {
            _hp = maxHealth;
            var rs = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer)) rs.Add(r);
            _renderers = rs.ToArray();
            _origMats = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++) _origMats[i] = _renderers[i].sharedMaterials;
            _colliders = GetComponentsInChildren<Collider>(true);
            _obstacle = GetComponent<NavMeshObstacle>();
            _homePos = transform.position;
            _homeRot = transform.rotation;

            // The body or barrel colour, so the flying parts match it.
            foreach (var mats in _origMats)
                foreach (var m in mats)
                {
                    if (m == null || !m.HasProperty("_BaseColor")) continue;
                    if (m.name.Contains("CarPaint") || (kind == ExplosiveKind.Barrel && m.name.Contains("Paint")))
                    { _paint = m.GetColor("_BaseColor"); goto found; }
                }
            found:;
        }

        void OnEnable() => s_All.Add(this);

        void OnDisable() => s_All.Remove(this);

        void OnDestroy()
        {
            if (_rounds != null) _rounds.RoundStarted -= OnRoundStarted;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;
            _hp -= info.Amount;
            if (info.FromPlayer) _playerCaused = true;

            if (kind == ExplosiveKind.Car) UpdateSmoke();

            if (_hp <= 0f)
            {
                _hp = 0f;
                if (kind == ExplosiveKind.Barrel)
                {
                    _fuseEnd = Time.time + Random.Range(0.06f, 0.18f);
                }
                else
                {
                    _fuseEnd = Time.time + carFuseSeconds;
                    StartFire();
                }
            }
        }

        // Benchmark and test hook: blow it now, as if it had run its fuse.
        public void DetonateNow(bool fromPlayer = false)
        {
            if (!IsAlive) return;
            _hp = 0f;
            _playerCaused |= fromPlayer;
            Explode();
            if (_resetAt == 0f) _resetAt = Time.time + 2.5f;
        }

        void Update()
        {
            if (_resetAt > 0f && Time.time >= _resetAt) ResetProp();

            if (_rounds == null && RoundManager.Instance != null)
            {
                _rounds = RoundManager.Instance;
                _rounds.RoundStarted += OnRoundStarted;
            }

            if (_rockStart >= 0f) UpdateRock();
            if (_airborne) UpdateAirborne();

            if (_fuseEnd > 0f && Time.time >= _fuseEnd)
            {
                Explode();
                if (_resetAt == 0f) _resetAt = Time.time + 2.5f;
            }
        }

        // ------------------------------------------------------------------ smoke and fire

        void UpdateSmoke()
        {
            float damaged = 1f - _hp / maxHealth;
            if (damaged < 0.3f) return;
            if (_smoke == null)
            {
                if (CombatFX.Instance == null) return;
                _smoke = CombatFX.Instance.CreateSmoke(transform, SmokeLocalPoint());
                _smoke.Play();
            }
            var em = _smoke.emission;
            em.rateOverTime = Mathf.Lerp(4f, 22f, Mathf.InverseLerp(0.3f, 1f, damaged));
        }

        void StartFire()
        {
            UpdateSmoke();
            // The fire-loop flames start on the fuse and carry on through the wreck's burn.
            if (ExplosionFX.Instance != null)
                _burn = ExplosionFX.Instance.StartBurn(transform, BurnLocalPoint(), carFuseSeconds + 12f, 1f, BurnFootprint());
        }

        // Flames sit low in the body, across the whole footprint (cabin and engine bay).
        Vector3 BurnLocalPoint()
        {
            var col = _colliders.Length > 0 ? _colliders[0] as BoxCollider : null;
            return col != null ? col.center + new Vector3(0f, col.size.y * 0.1f, 0f) : Vector3.up * 0.9f;
        }

        Vector3 BurnFootprint()
        {
            var col = _colliders.Length > 0 ? _colliders[0] as BoxCollider : null;
            return col != null ? col.size : default;
        }

        Vector3 SmokeLocalPoint()
        {
            // Roughly the engine bay of a car along its length, or the barrel top.
            var col = _colliders.Length > 0 ? _colliders[0] as BoxCollider : null;
            if (col != null) return col.center + new Vector3(0f, col.size.y * 0.35f, col.size.z * 0.22f);
            return Vector3.up * 0.9f;
        }

        // ------------------------------------------------------------------ explosion

        void Explode()
        {
            _exploded = true;
            _fuseEnd = -1f;
            Vector3 center = WorldCenter();
            bool big = kind == ExplosiveKind.Car;
            float force = Blast.DefaultForce(big ? BlastKind.Car : BlastKind.Barrel);

            Blast.Detonate(center, blastRadius, blastDamage, gameObject, _playerCaused,
                big ? BlastKind.Car : BlastKind.Barrel, this, force);

            if (_smoke != null) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            var pool = DebrisPool.Instance;
            if (kind == ExplosiveKind.Barrel)
            {
                foreach (var r in _renderers) r.enabled = false;
                foreach (var c in _colliders) c.enabled = false;
                if (_obstacle != null) _obstacle.enabled = false;
                pool?.SpawnBarrel(center, _paint, center, force);
            }
            else
            {
                var box = _colliders.Length > 0 ? _colliders[0] as BoxCollider : null;
                if (box != null) pool?.SpawnCar(transform, box.center, box.size, _paint, center, force);
                ApplyWreckMaterials();
                Launch(center);
                float burn = Random.Range(10f, 15f);
                if (_burn != null && _burn.Active && _burn.Follow == transform) ExplosionFX.Instance.Extend(_burn, burn);
                else if (ExplosionFX.Instance != null) _burn = ExplosionFX.Instance.StartBurn(transform, BurnLocalPoint(), burn, 1f, BurnFootprint());
            }
        }

        // Charred body and soot-black glass; tyres and wire keep their rubber.
        void ApplyWreckMaterials()
        {
            var fx = ExplosionFX.Instance;
            Material body = fx != null && fx.Assets != null && fx.Assets.wreckBody != null ? fx.Assets.wreckBody : wreckMaterial;
            Material glass = fx != null && fx.Assets != null && fx.Assets.wreckGlass != null ? fx.Assets.wreckGlass : body;
            if (body == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var src = _origMats[i];
                var mats = new Material[src.Length];
                for (int m = 0; m < src.Length; m++)
                {
                    string n = src[m] != null ? src[m].name : "";
                    if (n.Contains("Rubber") || n.Contains("Wire")) mats[m] = src[m];
                    else if (n.Contains("Glass") || n.Contains("Interior") || n.Contains("light") || n.Contains("Light")) mats[m] = glass;
                    else mats[m] = body;
                }
                _renderers[i].sharedMaterials = mats;
            }
        }

        Vector3 WorldCenter()
        {
            if (_colliders.Length > 0 && _colliders[0] != null)
            {
                var b = _colliders[0].bounds;
                return new Vector3(b.center.x, b.min.y + (kind == ExplosiveKind.Car ? 0.7f : 0.45f), b.center.z);
            }
            return transform.position + Vector3.up * 0.5f;
        }

        // ------------------------------------------------------------------ car launch

        void Launch(Vector3 blast)
        {
            var box = _colliders.Length > 0 ? _colliders[0] as BoxCollider : null;
            if (box == null) return;
            if (_obstacle != null) _obstacle.enabled = false;   // nobody paths round a car that is in the air

            _rockStart = -1f;
            _rb = gameObject.AddComponent<Rigidbody>();
            _rb.mass = CarMass;
            _rb.linearDamping = 0.04f;
            _rb.angularDamping = 0.35f;
            _rb.interpolation = RigidbodyInterpolation.None;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _rb.centerOfMass = box.center + Vector3.down * (box.size.y * 0.18f);

            // The jump: 1.5-3 m. v = sqrt(2 g h).
            float h = Random.Range(HopMin, HopMax);
            float vy = Mathf.Sqrt(2f * Physics.gravity.magnitude * h);
            float air = 2f * vy / Physics.gravity.magnitude;

            // Sideways drift toward the more open side of the car (the street), never into a wall.
            Vector3 right = transform.right; right.y = 0f; right.Normalize();
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 origin = box.bounds.center;
            float openR = Clearance(origin, right), openL = Clearance(origin, -right);
            Vector3 dir = openR >= openL ? right : -right;
            float open = Mathf.Max(openR, openL);
            dir = (dir + fwd * Random.Range(-0.35f, 0.35f)).normalized;
            float drift = Mathf.Min(Random.Range(1.2f, 2.6f), Mathf.Max(0f, open - 2.2f) / air);

            // Roll: mostly a lurch, sometimes a half roll onto the roof, now and then a full flip.
            float r = Random.value, angle;
            if (r < 0.50f) angle = Random.Range(15f, 45f);
            else if (r < 0.75f) angle = Random.Range(80f, 110f);
            else if (r < 0.90f) angle = Random.Range(170f, 200f);
            else angle = Random.Range(340f, 380f);
            Vector3 rollAxis = Vector3.Cross(Vector3.up, dir).normalized;   // topple toward the drift
            if (Random.value < 0.5f) rollAxis = -rollAxis;
            Vector3 ang = rollAxis * (angle * Mathf.Deg2Rad / air) + Random.insideUnitSphere * 0.6f;

            _rb.linearVelocity = dir * drift + Vector3.up * vy;
            _rb.angularVelocity = ang;
            _airborne = true;
            _launchedAt = Time.time;
            _stillSince = -1f;
            _launchY = transform.position.y;
            LastPeakHeight = 0f;
            LastMaxTilt = 0f;
            LastSettleSeconds = -1f;
        }

        static float Clearance(Vector3 origin, Vector3 dir)
        {
            if (Physics.Raycast(origin, dir, out var hit, 8f, 1 << GameLayers.Default, QueryTriggerInteraction.Ignore)) return hit.distance;
            return 8f;
        }

        void UpdateAirborne()
        {
            if (_rb == null) { _airborne = false; return; }
            float t = Time.time - _launchedAt;
            LastPeakHeight = Mathf.Max(LastPeakHeight, transform.position.y - _launchY);
            LastMaxTilt = Mathf.Max(LastMaxTilt, Vector3.Angle(transform.up, Vector3.up));
            bool still = _rb.linearVelocity.sqrMagnitude < 0.03f && _rb.angularVelocity.sqrMagnitude < 0.05f;
            if (t > 0.5f && still)
            {
                if (_stillSince < 0f) _stillSince = Time.time;
                if (Time.time - _stillSince > 0.35f) { Settle(); return; }
            }
            else _stillSince = -1f;
            if (t > SettleTimeout) Settle();
        }

        // Freezes the wreck where it came to rest and makes it an obstacle again at that pose.
        void Settle()
        {
            _airborne = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            LastSettleSeconds = Time.time - _launchedAt;
            LastTravel = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(_homePos.x, _homePos.z));
            LastTiltDegrees = Vector3.Angle(transform.up, Vector3.up);
            if (_obstacle != null)
            {
                _obstacle.enabled = false;
                _obstacle.enabled = true;   // carving restarts at the new pose
            }
        }

        // ------------------------------------------------------------------ neighbours rocking

        // Cars close to a blast rock on their springs: a short tilt away from it and a small hop that dies out in a second.
        public static void RockNearby(BlastInfo b)
        {
            float reach = b.Radius * 2.2f;
            for (int i = 0; i < s_All.Count; i++)
            {
                var p = s_All[i];
                if (p.kind != ExplosiveKind.Car || p._exploded || p._fuseEnd > 0f || p._airborne) continue;
                Vector3 d = p.transform.position - b.Position;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > reach || dist < 0.1f) continue;
                p.StartRock(d / dist, Mathf.Pow(1f - dist / reach, 1.2f));
            }
        }

        void StartRock(Vector3 away, float strength)
        {
            if (_rockStart < 0f) { _rockBasePos = transform.position; _rockBaseRot = transform.rotation; }
            _rockStart = Time.time;
            _rockAmp = strength;
            _rockAxis = Vector3.Cross(Vector3.up, away);   // top leans away from the blast
        }

        void UpdateRock()
        {
            float t = Time.time - _rockStart;
            if (t > 1.1f)
            {
                transform.SetPositionAndRotation(_rockBasePos, _rockBaseRot);
                _rockStart = -1f;
                return;
            }
            float decay = Mathf.Exp(-t * 4.2f);
            float tilt = 4.5f * _rockAmp * decay * Mathf.Sin(t * 19f);
            float hop = 0.07f * _rockAmp * decay * Mathf.Abs(Mathf.Sin(t * 13f));
            transform.SetPositionAndRotation(_rockBasePos + Vector3.up * hop, Quaternion.AngleAxis(tilt, _rockAxis) * _rockBaseRot);
        }

        // ------------------------------------------------------------------ reset

        void OnRoundStarted(int round)
        {
            if (_fuseEnd > 0f) { _resetAt = 0f; return; }   // mid-fuse: reset 2.5 s after the pending explosion instead of silently now
            ResetProp();
        }

        public static void ResetAll()
        {
            for (int i = s_All.Count - 1; i >= 0; i--) s_All[i].ResetProp();
        }

        public void ResetProp()
        {
            _hp = maxHealth;
            _exploded = false;
            _playerCaused = false;
            _fuseEnd = -1f;
            _resetAt = -1f;
            _airborne = false;
            _rockStart = -1f;
            if (_rb != null) { _rb.isKinematic = true; Destroy(_rb); _rb = null; }
            if (kind == ExplosiveKind.Car) transform.SetPositionAndRotation(_homePos, _homeRot);
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].enabled = true;
                _renderers[i].sharedMaterials = _origMats[i];
            }
            foreach (var c in _colliders) c.enabled = true;
            if (_obstacle != null) { _obstacle.enabled = false; _obstacle.enabled = true; }
            if (_smoke != null) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_burn != null) { ExplosionFX.Instance?.StopBurn(_burn); _burn = null; }
        }
    }
}
