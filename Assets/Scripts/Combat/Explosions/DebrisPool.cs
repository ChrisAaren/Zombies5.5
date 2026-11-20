using System.Collections.Generic;
using UnityEngine;

namespace Zombies55
{
    // Pooled rigid-body debris for cars and barrels. Parts are pre-instantiated once (no allocation while playing), capped by
    // EffectsDetail.MaxDebris, recycled oldest-first (the old part sinks into the ground), and never collide with the player,
    // enemies, explosive props or each other, so debris cannot trap or push anyone.
    public class DebrisPool : MonoBehaviour
    {
        public static DebrisPool Instance { get; private set; }

        [SerializeField] ExplosionAssets assets;

        const float LifeSeconds = 26f;
        const float SinkSeconds = 1.1f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly List<DebrisPart> _all = new List<DebrisPart>(128);
        readonly List<DebrisPart> _live = new List<DebrisPart>(128);
        readonly List<DebrisPart>[] _byGroup = new List<DebrisPart>[System.Enum.GetValues(typeof(DebrisGroup)).Length];
        MaterialPropertyBlock _mpb;
        Transform _root;
        float _clatterWindow;
        int _clatterCount;

        public int ActiveCount => _live.Count;
        public int PoolSize => _all.Count;
        // Seconds from spawn to rest of the most recent part that settled (for tests).
        public float LastSettleSeconds { get; private set; }

        void Awake()
        {
            Instance = this;
            // Debris never trips, blocks or shoves anyone. Set at runtime so the project's physics settings stay untouched.
            Physics.IgnoreLayerCollision(GameLayers.Debris, GameLayers.Player, true);
            Physics.IgnoreLayerCollision(GameLayers.Debris, GameLayers.Enemy, true);
            Physics.IgnoreLayerCollision(GameLayers.Debris, GameLayers.Explosive, true);
            Physics.IgnoreLayerCollision(GameLayers.Debris, GameLayers.Debris, true);
            Build();
        }

        void OnEnable() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Build()
        {
            if (assets == null || assets.parts == null) return;
            _mpb = new MaterialPropertyBlock();
            _root = new GameObject("DebrisParts").transform;
            _root.SetParent(transform, false);
            for (int i = 0; i < _byGroup.Length; i++) _byGroup[i] = new List<DebrisPart>();
            foreach (var def in assets.parts)
            {
                if (def.prefab == null) continue;
                for (int n = 0; n < def.poolCount; n++)
                {
                    var go = Instantiate(def.prefab, _root);
                    go.name = def.name + "_" + n;
                    var p = go.GetComponent<DebrisPart>();
                    p.Body = go.GetComponent<Rigidbody>();
                    p.Hull = go.GetComponentInChildren<Collider>(true);
                    p.Rend = go.GetComponentInChildren<Renderer>(true);
                    p.Def = def;
                    p.Pool = this;
                    var mats = p.Rend.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++) if (mats[m] == assets.debrisPaint) p.PaintIndex = m;
                    p.Body.sleepThreshold = 0.25f;
                    p.Body.maxDepenetrationVelocity = 4f;
                    p.Body.solverIterations = 4;
                    p.Body.solverVelocityIterations = 1;
                    // Active and parked far below the world until Start: the colliders are built before the first blast, not during it.
                    p.Body.isKinematic = true;
                    go.transform.position = new Vector3(0f, -2000f, 0f);
                    _all.Add(p);
                    _byGroup[(int)def.group].Add(p);
                }
            }
        }

        System.Collections.IEnumerator Start()
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            foreach (var p in _all) if (p.State == 0) p.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ spawning

        DebrisPart Take(DebrisGroup group)
        {
            var list = _byGroup[(int)group];
            if (list == null || list.Count == 0) return null;
            int cap = EffectsDetail.MaxDebris;

            // Over the cap: the oldest live part sinks away to make room.
            while (_live.Count >= cap) { if (!SinkOldest()) break; }

            int start = Random.Range(0, list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[(start + i) % list.Count];
                if (p.State == 0) return p;
            }
            // Every part of this kind is out: take over the oldest of this kind.
            DebrisPart oldest = null;
            foreach (var p in list) if (p.State == 1 && (oldest == null || p.Spawned < oldest.Spawned)) oldest = p;
            if (oldest != null) { Release(oldest); return oldest; }
            return null;
        }

        bool SinkOldest()
        {
            DebrisPart oldest = null;
            foreach (var p in _live) if (p.State == 1 && (oldest == null || p.Spawned < oldest.Spawned)) oldest = p;
            if (oldest == null) return false;
            BeginSink(oldest);
            _live.Remove(oldest);   // sinking parts stop counting against the cap
            return true;
        }

        void BeginSink(DebrisPart p)
        {
            p.State = 2;
            p.SinkStart = Time.time;
            p.Hull.enabled = false;
            p.Body.isKinematic = true;
        }

        void Release(DebrisPart p)
        {
            _live.Remove(p);
            p.State = 0;
            p.gameObject.SetActive(false);
        }

        DebrisPart Spawn(DebrisGroup group, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 spin, Color paint)
        {
            var p = Take(group);
            if (p == null) return null;
            var t = p.transform;
            t.SetPositionAndRotation(pos, rot);
            p.Hull.enabled = true;
            p.Body.isKinematic = false;
            p.gameObject.SetActive(true);
            p.Body.linearVelocity = vel;
            p.Body.angularVelocity = spin;
            p.Body.WakeUp();
            p.State = 1;
            p.Spawned = Time.time;
            p.Settled = -1f;
            p.StillFor = 0f;
            if (p.PaintIndex >= 0)
            {
                _mpb.Clear();
                _mpb.SetColor(BaseColorId, paint);
                p.Rend.SetPropertyBlock(_mpb, p.PaintIndex);
            }
            _live.Add(p);
            return p;
        }

        static Vector3 Away(Vector3 from, Vector3 at, float up)
        {
            Vector3 d = at - from;
            d.y = 0f;
            if (d.sqrMagnitude < 0.04f) { var r = Random.insideUnitCircle; d = new Vector3(r.x, 0f, r.y); }
            d.Normalize();
            d.y = up;
            return d;
        }

        // A car blows apart: hood, 1-2 doors, a trunk lid or a bumper, 1-2 wheels, scraps and chunks. localCenter / size describe the
        // car's collider box in the car's local space (length along Z, width X, height Y); parts start near their place on the body.
        public int SpawnCar(Transform car, Vector3 localCenter, Vector3 size, Color paint, Vector3 blast, float force)
        {
            if (_root == null) return 0;
            float k = Mathf.Clamp(force / 15f, 0.6f, 1.3f);
            int before = _live.Count;
            Quaternion q = car.rotation;

            Vector3 P(float x, float y, float z) => car.TransformPoint(localCenter + new Vector3(x * size.x, y * size.y, z * size.z));
            Vector3 V(Vector3 at, float speed, float up) => Away(blast, at, 0f) * speed + Vector3.up * up;
            Vector3 Spin(float m) => Random.insideUnitSphere * m;

            // hood: pops up and tumbles forward
            Vector3 at0 = P(0f, 0.32f, 0.3f);
            Spawn(DebrisGroup.Hood, at0 + Vector3.up * 0.15f, q * Quaternion.Euler(Random.Range(-10f, 10f), 0f, Random.Range(-8f, 8f)),
                V(at0, Random.Range(5f, 9f) * k, Random.Range(7f, 11f) * k), Spin(7f), paint);

            // doors
            int doors = Random.value < 0.55f ? 2 : 1;
            float side0 = Random.value < 0.5f ? -1f : 1f;
            for (int i = 0; i < doors; i++)
            {
                float side = i == 0 ? side0 : -side0;
                Vector3 at = P(0.55f * side, 0.08f, Random.Range(-0.05f, 0.1f));
                Spawn(DebrisGroup.Door, at, q * Quaternion.Euler(0f, side < 0f ? 180f : 0f, 0f),
                    V(at, Random.Range(4f, 8f) * k, Random.Range(3.5f, 7f) * k) + car.right * side * 2.5f, Spin(5f), paint);
            }

            // trunk lid or a bumper
            if (Random.value < 0.6f)
            {
                Vector3 at = P(0f, 0.3f, -0.34f);
                Spawn(DebrisGroup.Trunk, at + Vector3.up * 0.1f, q, V(at, Random.Range(4f, 8f) * k, Random.Range(6f, 10f) * k), Spin(6f), paint);
            }
            else
            {
                float end = Random.value < 0.5f ? 1f : -1f;
                Vector3 at = P(0f, -0.28f, 0.5f * end);
                Spawn(DebrisGroup.Bumper, at, q * Quaternion.Euler(0f, end < 0f ? 180f : 0f, 0f), V(at, Random.Range(5f, 9f) * k, Random.Range(4f, 8f) * k), Spin(6f), paint);
            }

            // wheels: they roll away down the street
            int wheels = Random.value < 0.5f ? 2 : 1;
            for (int i = 0; i < wheels; i++)
            {
                float sx = Random.value < 0.5f ? -1f : 1f, sz = Random.value < 0.5f ? -1f : 1f;
                Vector3 at = P(0.42f * sx, -0.32f, 0.31f * sz);
                Spawn(DebrisGroup.Wheel, at + Vector3.up * 0.2f, q, V(at, Random.Range(6f, 10f) * k, Random.Range(2.5f, 6f) * k), new Vector3(Random.Range(-3f, 3f), Random.Range(-2f, 2f), Random.Range(-3f, 3f)), paint);
            }

            // scraps (light, fast) and chunks (heavy, short throws)
            int scraps = Random.Range(4, 7);
            for (int i = 0; i < scraps; i++)
            {
                Vector3 at = P(Random.Range(-0.4f, 0.4f), Random.Range(-0.1f, 0.4f), Random.Range(-0.45f, 0.45f));
                Spawn(DebrisGroup.Scrap, at, Random.rotation, V(at, Random.Range(6f, 13f) * k, Random.Range(5f, 12f) * k), Spin(14f), paint);
            }
            int chunks = Random.Range(2, 4);
            for (int i = 0; i < chunks; i++)
            {
                Vector3 at = P(Random.Range(-0.3f, 0.3f), Random.Range(-0.15f, 0.25f), Random.Range(-0.35f, 0.35f));
                Spawn(DebrisGroup.Chunk, at + Vector3.up * 0.2f, Random.rotation, V(at, Random.Range(3.5f, 7.5f) * k, Random.Range(4f, 8f) * k), Spin(6f), paint);
            }
            return _live.Count - before;
        }

        // A barrel throws its lid and both halves.
        public int SpawnBarrel(Vector3 center, Color paint, Vector3 blast, float force)
        {
            if (_root == null) return 0;
            float k = Mathf.Clamp(force / 11f, 0.6f, 1.3f);
            int before = _live.Count;
            Spawn(DebrisGroup.BarrelLid, center + Vector3.up * 0.4f, Random.rotation, Away(blast, center, 0f) * Random.Range(3f, 6f) * k + Vector3.up * Random.Range(9f, 14f) * k, Random.insideUnitSphere * 14f, paint);
            for (int i = 0; i < 2; i++)
            {
                float a = Random.value * 6.28f + i * 3.14f;
                Vector3 off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.2f;
                Vector3 vel = new Vector3(off.x, 0f, off.z).normalized * Random.Range(3.5f, 7f) * k + Vector3.up * Random.Range(4f, 9f) * k;
                Spawn(DebrisGroup.BarrelHalf, center + off, Random.rotation, vel, Random.insideUnitSphere * 9f, paint);
            }
            return _live.Count - before;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float now = Time.time;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var p = _live[i];
                if (p.State != 1) continue;
                var rb = p.Body;
                if (p.Settled < 0f)
                {
                    if (rb.IsSleeping() || (rb.linearVelocity.sqrMagnitude < 0.04f && rb.angularVelocity.sqrMagnitude < 0.09f)) p.StillFor += Time.deltaTime;
                    else p.StillFor = 0f;
                    if (p.StillFor > 0.5f) { p.Settled = now; LastSettleSeconds = now - p.Spawned - 0.5f; }
                }
                if (now - p.Spawned > LifeSeconds || p.transform.position.y < -8f)
                {
                    BeginSink(p);
                    _live.RemoveAt(i);
                }
            }
            // sinking parts (not in _live): walk the whole list only while something sinks
            for (int i = 0; i < _all.Count; i++)
            {
                var p = _all[i];
                if (p.State != 2) continue;
                float k = (now - p.SinkStart) / SinkSeconds;
                if (k >= 1f) { p.State = 0; p.gameObject.SetActive(false); continue; }
                p.transform.position += Vector3.down * (0.6f / SinkSeconds * Time.deltaTime);
            }
        }

        // ------------------------------------------------------------------ impacts and blasts

        internal void Impact(DebrisPart p, Vector3 point, float speed)
        {
            if (speed < 2.5f) return;
            float now = Time.unscaledTime;
            if (now - _clatterWindow > 0.5f) { _clatterWindow = now; _clatterCount = 0; }
            if (_clatterCount >= 4) return;   // at most 8 clinks a second
            _clatterCount++;
            CombatAudio.Instance?.PlayClatter(point, Mathf.Clamp01(speed / 10f), p.Def.mass);
        }

        // Another explosion shoves parts already lying around.
        public void OnBlast(BlastInfo b)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var p = _live[i];
                if (p.State != 1) continue;
                Vector3 pos = p.transform.position;
                if ((pos - b.Position).sqrMagnitude > b.Radius * b.Radius) continue;
                float lightness = Mathf.Clamp(8f / Mathf.Max(1f, p.Def.mass), 0.25f, 1.6f);
                p.Body.AddForce(b.VelocityAt(pos) * 0.55f * lightness, ForceMode.VelocityChange);
                p.Settled = -1f; p.StillFor = 0f;
            }
        }

        // Clears every part at once (round reset).
        public void ResetAll()
        {
            _live.Clear();
            foreach (var p in _all)
            {
                p.State = 0;
                p.gameObject.SetActive(false);
            }
        }
    }
}
