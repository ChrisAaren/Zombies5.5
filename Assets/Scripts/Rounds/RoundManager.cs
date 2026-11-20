using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Zombies55
{
    // Round flow, spawning and the enemy pool.
    //   total(r)  = round(0.5 r^2 + 2 r + 4)
    //   cap(r)    = min(48, 6 + 4 (r - 1))
    //   interval  = max(0.15, 1.0 - 0.06 r)
    public class RoundManager : MonoBehaviour
    {
        public const int MaxAlive = 48;

        public static RoundManager Instance { get; private set; }

        // Benchmark mode: ignore the round table, keep MaxAlive enemies in the world at all times.
        public static bool BenchmarkMode;
        const int BenchmarkRound = 10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => BenchmarkMode = false;

        [Header("Setup (filled by the scene builder)")]
        [Tooltip("Inactive enemy cloned into the pool.")]
        [SerializeField] Enemy enemyTemplate;
        [Tooltip("World positions on the NavMesh enemies may spawn at.")]
        [SerializeField] Vector3[] spawnPoints = new Vector3[0];

        [Header("Timing")]
        [SerializeField] float firstRoundDelay = 2f;
        [SerializeField] float breakSeconds = 12f;

        [Header("Spawn distance from the player")]
        [SerializeField] float minSpawnDistance = 25f;
        [SerializeField] float maxSpawnDistance = 90f;

        public int Round { get; private set; }               // 0 before the first round starts
        public int AliveCount { get; private set; }          // enemies currently in the world
        public int RemainingThisRound { get; private set; }  // alive + not yet spawned
        public bool InBreak { get; private set; }            // between rounds (and before round 1)
        public float BreakTimeLeft { get; private set; }     // seconds until the next round
        public int TotalKills { get; private set; }
        public int TotalHeadshots { get; private set; }
        public int RoundsSurvived => Mathf.Max(0, InBreak ? Round : Round - 1);

        public event Action<int> RoundStarted;            // round number; explosives reset on this
        public event Action<int> RoundCompleted;          // round number
        public event Action<Enemy, DamageInfo> EnemyKilled;

        public static int TotalForRound(int r) => Mathf.RoundToInt(0.5f * r * r + 2f * r + 4f);
        public static int AliveCapForRound(int r) => Mathf.Min(MaxAlive, 6 + 4 * (r - 1));
        public static float SpawnIntervalForRound(int r) => Mathf.Max(0.15f, 1f - 0.06f * r);

        readonly Stack<Enemy> _free = new Stack<Enemy>(MaxAlive);
        readonly List<Enemy> _all = new List<Enemy>(MaxAlive);
        float[] _pointReadyAt;
        readonly List<int> _cand = new List<int>(64);
        readonly List<int> _candScore = new List<int>(64);
        int _toSpawn;
        float _spawnTimer;
        bool _started;
        Transform _poolRoot;
        bool _lastPickInView;

        void Awake() => Instance = this;
        void OnEnable() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _pointReadyAt = new float[spawnPoints.Length];
            Warmup();
            InBreak = true;
            BreakTimeLeft = firstRoundDelay;
        }

        void Warmup()
        {
            if (enemyTemplate == null) return;
            _poolRoot = enemyTemplate.transform.parent != null ? enemyTemplate.transform.parent : transform;
            for (int i = 0; i < MaxAlive; i++)
            {
                var e = Instantiate(enemyTemplate, _poolRoot);
                e.name = "Enemy_" + (i + 1).ToString("00");
                e.SetOwner(this);
                e.gameObject.SetActive(false);
                _free.Push(e);
                _all.Add(e);
            }
        }

        void Update()
        {
            var loop = GameLoop.Instance;
            if (loop == null || loop.State != RunState.Running) return;
            float dt = Time.deltaTime;

            if (BenchmarkMode)
            {
                if (Round == 0) { Round = BenchmarkRound; InBreak = false; _started = true; RoundStarted?.Invoke(Round); }
                _spawnTimer -= dt;
                if (_spawnTimer <= 0f && AliveCount < MaxAlive && _free.Count > 0)
                {
                    if (TrySpawn()) { AliveCount++; RemainingThisRound = Mathf.Max(RemainingThisRound, AliveCount); _spawnTimer = 0.05f; }
                    else _spawnTimer = 0.25f;
                }
                return;
            }

            if (InBreak)
            {
                BreakTimeLeft -= dt;
                if (BreakTimeLeft <= 0f) StartRound(Round + 1);
                return;
            }

            _spawnTimer -= dt;
            if (_toSpawn > 0 && _spawnTimer <= 0f && AliveCount < AliveCapForRound(Round) && _free.Count > 0)
            {
                if (TrySpawn())
                {
                    _toSpawn--;
                    AliveCount++;
                    _spawnTimer = SpawnIntervalForRound(Round);
                }
                else _spawnTimer = 0.25f;
            }
        }

        void StartRound(int round)
        {
            Round = round;
            InBreak = false;
            BreakTimeLeft = 0f;
            _toSpawn = TotalForRound(round);
            RemainingThisRound = _toSpawn;
            _spawnTimer = 0.5f;
            _started = true;
            RoundStarted?.Invoke(round);
        }

        bool TrySpawn()
        {
            if (!TryGetSpawnPoint(out var pos)) return false;
            var e = _free.Pop();
            e.Activate(pos, Round);
            if (_lastPickInView) ZoneManager.Instance?.PlaySpawnSmoke(pos);
            return true;
        }

        internal void ReportKilled(Enemy enemy, DamageInfo info)
        {
            AliveCount = Mathf.Max(0, AliveCount - 1);
            RemainingThisRound = Mathf.Max(0, RemainingThisRound - 1);
            TotalKills++;
            if (info.Zone == HitZone.Head && info.Kind == DamageKind.Bullet) TotalHeadshots++;
            EnemyKilled?.Invoke(enemy, info);

            if (_started && !InBreak && RemainingThisRound <= 0 && _toSpawn <= 0)
            {
                InBreak = true;
                BreakTimeLeft = breakSeconds;
                RoundCompleted?.Invoke(Round);
            }
        }

        internal void Release(Enemy enemy) => _free.Push(enemy);

        // Shockwave pickup: kills every enemy that is alive right now. The kills are credited to the player and count toward the
        // round, but carry no per-kill points and never roll a drop (DamageInfo.Scripted). Returns how many died.
        public int KillAllAlive(Vector3 origin)
        {
            int killed = 0;
            for (int i = 0; i < _all.Count; i++)
            {
                var e = _all[i];
                if (e == null || !e.gameObject.activeSelf || !e.IsAlive) continue;
                Vector3 dir = e.transform.position - origin;
                dir.y = 0f;
                e.TakeDamage(new DamageInfo
                {
                    Amount = e.Health + 1f,
                    Point = e.transform.position + Vector3.up,
                    Direction = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward,
                    Zone = HitZone.Body,
                    Kind = DamageKind.Melee,
                    FromPlayer = true,
                    Scripted = true,
                });
                killed++;
            }
            return killed;
        }

        // Picks a spawn point in an owned district, 25-90 m from the player (the minimum shrinks to
        // 0.8x the farthest owned point, never below 10 m, so small districts work), preferring points
        // outside the camera view and hidden behind geometry. Falls back to the farthest owned point.
        // Result is snapped to the NavMesh.
        public bool TryGetSpawnPoint(out Vector3 position)
        {
            position = default;
            _lastPickInView = false;
            if (spawnPoints == null || spawnPoints.Length == 0) return false;

            var playerT = PlayerHealth.Instance != null ? PlayerHealth.Instance.transform : null;
            Vector3 pp = playerT != null ? playerT.position : Vector3.zero;
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : pp + Vector3.up * 1.6f;
            const int occluders = (1 << GameLayers.Default) | (1 << GameLayers.Explosive);

            var zones = ZoneManager.Instance;
            _cand.Clear();
            _candScore.Clear();
            int farthest = -1;
            float farthestD = -1f;
            float farInRange = 0f;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (zones != null && !zones.IsOwnedAt(spawnPoints[i])) continue;
                Vector3 d = spawnPoints[i] - pp;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > farthestD) { farthestD = dist; farthest = i; }
                if (dist <= maxSpawnDistance && dist > farInRange) farInRange = dist;
            }
            if (farthest < 0) return false;
            float effMin = Mathf.Clamp(0.8f * farInRange, 10f, minSpawnDistance);

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                Vector3 p = spawnPoints[i];
                if (zones != null && !zones.IsOwnedAt(p)) continue;
                Vector3 d = p - pp;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < effMin || dist > maxSpawnDistance) continue;

                bool inView = false;
                if (cam != null)
                {
                    Vector3 v = cam.WorldToViewportPoint(p + Vector3.up * 0.9f);
                    inView = v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
                }
                bool occluded = Physics.Linecast(eye, p + Vector3.up * 1.4f, occluders, QueryTriggerInteraction.Ignore);
                bool ready = _pointReadyAt == null || i >= _pointReadyAt.Length || Time.time >= _pointReadyAt[i];
                int score = (inView ? 0 : 4) + (ready ? 2 : 0) + (occluded ? 1 : 0);   // off-screen first, then a point not used in the last 1.5 s, then hidden ones
                _cand.Add(i);
                _candScore.Add(score);
            }

            // Best score first; random among ties; skip points that are not on the NavMesh.
            while (_cand.Count > 0)
            {
                int best = -1;
                for (int k = 0; k < _cand.Count; k++) best = Mathf.Max(best, _candScore[k]);
                int pick = -1, ties = 0;
                for (int k = 0; k < _cand.Count; k++)
                {
                    if (_candScore[k] != best) continue;
                    ties++;
                    if (UnityEngine.Random.Range(0, ties) == 0) pick = k;
                }
                int idx = _cand[pick];
                if (NavMesh.SamplePosition(spawnPoints[idx], out var hit, 3f, NavMesh.AllAreas))
                {
                    position = hit.position;
                    _lastPickInView = IsInView(cam, hit.position);
                    if (_pointReadyAt != null && idx < _pointReadyAt.Length) _pointReadyAt[idx] = Time.time + 1.5f;
                    return true;
                }
                _cand.RemoveAt(pick);
                _candScore.RemoveAt(pick);
            }

            if (NavMesh.SamplePosition(spawnPoints[farthest], out var far, 3f, NavMesh.AllAreas))
            {
                position = far.position;
                _lastPickInView = IsInView(cam, far.position);
                return true;
            }
            return false;
        }

        static bool IsInView(Camera cam, Vector3 p)
        {
            if (cam == null) return false;
            Vector3 v = cam.WorldToViewportPoint(p + Vector3.up * 0.9f);
            return v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
        }
    }
}
