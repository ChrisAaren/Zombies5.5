using System;
using System.Text;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Zombies55
{
    // Item drops. Rolls drops from player-credited kills (DropRules + PickupTuning), owns the pooled world pickups, applies what
    // they do (Ammo Cache, Shockwave, Deadeye, Payday) and keeps the two timed effects. All numbers live in PickupTuning.
    // Nothing allocates per frame: slots, particles and the shockwave rings are built once.
    public class PickupDirector : MonoBehaviour
    {
        public static PickupDirector Instance { get; private set; }

        // Read by Enemy.TakeDamage on every player hit.
        public static bool DeadeyeActive => Instance != null && Instance._deadeyeLeft > 0f;
        public static bool PaydayActive => Instance != null && Instance._paydayLeft > 0f;

        [Header("Setup (filled by the scene builder)")]
        [SerializeField] PickupSlot[] slots = new PickupSlot[0];
        [SerializeField] Transform[] rings = new Transform[0];
        [SerializeField] Renderer[] ringRenderers = new Renderer[0];
        [SerializeField] Material particleMaterial;

        public event Action<PickupType> Collected;
        public event Action<PickupType> Expired;
        public event Action<int> ShockwaveFired;        // enemies killed
        public event Action EffectsChanged;             // Deadeye or Payday started or ended

        public DropRules Rules { get; } = new DropRules();
        public float DeadeyeLeft => _deadeyeLeft;
        public float PaydayLeft => _paydayLeft;
        public int TotalDrops { get; private set; }

        static readonly int ColorId = Shader.PropertyToID("_Color");

        RoundManager _rm;
        GameLoop _loop;
        PlayerWallet _wallet;
        float _deadeyeLeft, _paydayLeft;
        int _deadeyeWarnSecond = -1, _paydayWarnSecond = -1;
        ParticleSystem _burst;
        CinemachineImpulseSource _impulse;
        float[] _ringAge;
        Vector3 _ringOrigin;
        MaterialPropertyBlock _block;

        void Awake()
        {
            Instance = this;
            _block = new MaterialPropertyBlock();
            _ringAge = new float[rings.Length];
            for (int i = 0; i < _ringAge.Length; i++) { _ringAge[i] = -1f; rings[i].gameObject.SetActive(false); }
            BuildBurst();
            _impulse = GetComponent<CinemachineImpulseSource>();
            if (_impulse == null) _impulse = gameObject.AddComponent<CinemachineImpulseSource>();
        }

        void OnEnable() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_rm != null) { _rm.EnemyKilled -= OnEnemyKilled; _rm.RoundStarted -= OnRoundStarted; }
            if (_loop != null) _loop.StateChanged -= OnStateChanged;
            if (_wallet != null) _wallet.Multiplier = 1;
        }

        void Start() => Bind();

        void Bind()
        {
            if (!ReferenceEquals(_rm, RoundManager.Instance))
            {
                if (_rm != null) { _rm.EnemyKilled -= OnEnemyKilled; _rm.RoundStarted -= OnRoundStarted; }
                _rm = RoundManager.Instance;
                if (_rm != null) { _rm.EnemyKilled += OnEnemyKilled; _rm.RoundStarted += OnRoundStarted; }
            }
            if (!ReferenceEquals(_loop, GameLoop.Instance))
            {
                if (_loop != null) _loop.StateChanged -= OnStateChanged;
                _loop = GameLoop.Instance;
                if (_loop != null) _loop.StateChanged += OnStateChanged;
            }
            _wallet = PlayerWallet.Instance;
        }

        void OnRoundStarted(int round) => Rules.NewRound();

        // Drops stay through round transitions; they are cleared when the run is lost (a restart reloads the scene).
        void OnStateChanged(RunState state)
        {
            if (state == RunState.Lost) ClearAll();
        }

        public void ClearAll()
        {
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null && slots[i].Active) slots[i].Hide();
            _deadeyeLeft = _paydayLeft = 0f;
            if (_wallet != null) _wallet.Multiplier = 1;
            for (int i = 0; i < _ringAge.Length; i++) { _ringAge[i] = -1f; rings[i].gameObject.SetActive(false); }
            EffectsChanged?.Invoke();
        }

        // ------------------------------------------------------------------ drops

        void OnEnemyKilled(Enemy enemy, DamageInfo info)
        {
            // Benchmark runs stay comparable: no drops. Only kills credited to the player roll; Shockwave kills never do.
            if (RoundManager.BenchmarkMode || !info.FromPlayer || info.Scripted) return;
            if (!Rules.OnKill(out var type)) return;
            if (!NavMesh.SamplePosition(enemy.transform.position, out var hit, 4f, NavMesh.AllAreas)) return;
            SpawnDrop(type, hit.position);
        }

        public PickupSlot SpawnDrop(PickupType type, Vector3 groundPos)
        {
            PickupSlot slot = null, oldest = null;
            float oldestAge = -1f;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Active) { slot = slots[i]; break; }
                if (slots[i].Age > oldestAge) { oldestAge = slots[i].Age; oldest = slots[i]; }
            }
            if (slot == null) slot = oldest;
            if (slot == null) return null;

            slot.Show(type, groundPos);
            TotalDrops++;
            Color c = PickupTuning.ColorOf(type);
            Burst(groundPos + Vector3.up * PickupTuning.Hover, c, 22, 3.2f);
            PickupAudio.Instance?.PlaySpawn(Attenuation(groundPos) * 0.7f);
            return slot;
        }

        static float Attenuation(Vector3 pos)
        {
            var cam = Camera.main;
            if (cam == null) return 1f;
            return Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(Vector3.Distance(cam.transform.position, pos) / 60f));
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (_loop == null || _rm == null || _wallet == null) Bind();
            if (_loop == null || _loop.State != RunState.Running) return;
            float dt = Time.deltaTime;

            TickEffects(dt);
            TickRings(dt);

            var hp = PlayerHealth.Instance;
            bool canCollect = hp != null && hp.IsAlive;
            Vector3 pp = canCollect ? hp.transform.position : Vector3.zero;
            float r2 = PickupTuning.PickupRadius * PickupTuning.PickupRadius;

            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (!s.Active) continue;
                s.Tick(dt);
                if (s.Remaining <= 0f) { Expire(s); continue; }
                if (!canCollect) continue;
                Vector3 d = pp - s.transform.position;
                if (Mathf.Abs(d.y) > PickupTuning.PickupHeight) continue;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) Collect(s);
            }
        }

        void TickEffects(float dt)
        {
            if (_deadeyeLeft > 0f)
            {
                _deadeyeLeft -= dt;
                WarnTick(_deadeyeLeft, ref _deadeyeWarnSecond);
                if (_deadeyeLeft <= 0f) EndEffect();
            }
            if (_paydayLeft > 0f)
            {
                _paydayLeft -= dt;
                WarnTick(_paydayLeft, ref _paydayWarnSecond);
                if (_paydayLeft <= 0f) { if (_wallet != null) _wallet.Multiplier = 1; EndEffect(); }
            }
        }

        void WarnTick(float left, ref int lastSecond)
        {
            if (left <= 0f || left > PickupTuning.EffectWarnSeconds) return;
            int sec = Mathf.CeilToInt(left);
            if (sec == lastSecond) return;
            lastSecond = sec;
            PickupAudio.Instance?.PlayWarn();
        }

        void EndEffect()
        {
            PickupAudio.Instance?.PlayEffectEnd();
            EffectsChanged?.Invoke();
        }

        void Expire(PickupSlot s)
        {
            var pos = s.transform.position + Vector3.up * PickupTuning.Hover;
            var type = s.Type;
            s.Hide();
            Burst(pos, PickupTuning.ColorOf(type) * 0.7f, 10, 1.4f);
            PickupAudio.Instance?.PlayExpire();
            Expired?.Invoke(type);
        }

        void Collect(PickupSlot s)
        {
            var type = s.Type;
            var pos = s.transform.position + Vector3.up * PickupTuning.Hover;
            s.Hide();
            Burst(pos, PickupTuning.ColorOf(type), 34, 5f);
            Apply(type);
        }

        // What a pickup does. Public so tests can apply one without walking to it.
        public void Apply(PickupType type)
        {
            if (_wallet == null) _wallet = PlayerWallet.Instance;
            PickupAudio.Instance?.PlayPickup(type);
            switch (type)
            {
                case PickupType.AmmoCache:
                    PlayerWeapons.Instance?.RefillAllAmmo();
                    break;
                case PickupType.Shockwave:
                    DoShockwave();
                    break;
                case PickupType.Deadeye:
                    _deadeyeLeft = PickupTuning.DeadeyeSeconds;      // picking it up again resets the timer
                    _deadeyeWarnSecond = -1;
                    EffectsChanged?.Invoke();
                    break;
                case PickupType.Payday:
                    _paydayLeft = PickupTuning.PaydaySeconds;
                    _paydayWarnSecond = -1;
                    if (_wallet != null) _wallet.Multiplier = PickupTuning.PaydayMultiplier;
                    EffectsChanged?.Invoke();
                    break;
            }
            Collected?.Invoke(type);
        }

        void DoShockwave()
        {
            var hp = PlayerHealth.Instance;
            Vector3 origin = hp != null ? hp.transform.position : transform.position;
            int killed = _rm != null ? _rm.KillAllAlive(origin) : 0;
            _wallet?.Add(PickupTuning.ShockwaveBonus, "Shockwave");     // flat bonus instead of per-kill points; doubled by Payday
            for (int i = 0; i < _ringAge.Length; i++) _ringAge[i] = -i * 0.14f;
            _ringOrigin = origin;
            Burst(origin + Vector3.up * 1f, Palette.PickupCyan, 60, 9f);
            _impulse?.GenerateImpulse(1.4f);
            ShockwaveFired?.Invoke(killed);
        }

        void TickRings(float dt)
        {
            for (int i = 0; i < _ringAge.Length; i++)
            {
                if (_ringAge[i] <= -1f) continue;
                _ringAge[i] += dt;
                float t = _ringAge[i] / PickupTuning.ShockwaveRingSeconds;
                var go = rings[i].gameObject;
                if (t < 0f) { if (go.activeSelf) go.SetActive(false); continue; }
                if (t >= 1f) { go.SetActive(false); _ringAge[i] = -1f; continue; }
                if (!go.activeSelf) go.SetActive(true);
                float r = Mathf.Lerp(1f, PickupTuning.ShockwaveRadius, 1f - Mathf.Pow(1f - t, 1.6f));   // sweeps across the screen, then fades
                rings[i].SetPositionAndRotation(_ringOrigin + Vector3.up * 0.18f, Quaternion.Euler(90f, 0f, 0f));
                rings[i].localScale = new Vector3(r * 2f, r * 2f, 1f);
                float a = Mathf.Pow(1f - t, 1.2f) * (i == 0 ? 4.5f : 2.4f);
                _block.Clear();
                _block.SetColor(ColorId, Palette.PickupCyan * a);
                ringRenderers[i].SetPropertyBlock(_block);
            }
        }

        // ------------------------------------------------------------------ particles

        void BuildBurst()
        {
            var go = new GameObject("PickupBurst");
            go.transform.SetParent(transform, false);
            _burst = go.AddComponent<ParticleSystem>();
            _burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _burst.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.35f;
            main.startLifetime = 0.7f;
            main.startSpeed = 0f;
            main.startSize = 0.2f;
            var emission = _burst.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var shape = _burst.shape;
            shape.enabled = false;
            var col = _burst.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var sol = _burst.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            var src = particleMaterial != null ? particleMaterial : new Material(Shader.Find("Sprites/Default"));
            r.sharedMaterial = new Material(src) { name = "PickupSoft", mainTexture = MakeSoftTexture() };
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void Burst(Vector3 pos, Color color, int count, float speed)
        {
            if (_burst == null) return;
            var p = new ParticleSystem.EmitParams { position = pos, startColor = color };
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = UnityEngine.Random.onUnitSphere;
                p.velocity = dir * (speed * UnityEngine.Random.Range(0.4f, 1f)) + Vector3.up * (speed * 0.25f);
                p.startSize = UnityEngine.Random.Range(0.1f, 0.28f);
                p.startLifetime = UnityEngine.Random.Range(0.45f, 0.9f);
                _burst.Emit(p, 1);
            }
        }

        static Texture2D MakeSoftTexture()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "PickupSoftCircle", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f;
                    float dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // ------------------------------------------------------------------ debug and tests (called through unity command script eval)

        // Spawns a drop on the NavMesh in front of the camera (or the player when there is no camera).
        public static PickupSlot DebugSpawn(PickupType type, float distance = 4f, float side = 0f)
        {
            var d = Instance;
            if (d == null) return null;
            var cam = Camera.main;
            var hp = PlayerHealth.Instance;
            Vector3 origin = hp != null ? hp.transform.position : Vector3.zero;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 0.01f ? Vector3.forward : fwd.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 want = origin + fwd * distance + right * side;
            if (NavMesh.SamplePosition(want, out var hit, 6f, NavMesh.AllAreas)) want = hit.position;
            return d.SpawnDrop(type, want);
        }

        public static void DebugSpawnAll()
        {
            for (int i = 0; i < 4; i++) DebugSpawn((PickupType)i, 5f, (i - 1.5f) * 2.2f);
        }

        public static void DebugApply(PickupType type) => Instance?.Apply(type);

        public static string DebugState()
        {
            var d = Instance;
            if (d == null) return "no director";
            int active = 0;
            for (int i = 0; i < d.slots.Length; i++) if (d.slots[i].Active) active++;
            return "drops active=" + active + " total=" + d.TotalDrops + " killsSince=" + d.Rules.KillsSinceDrop + " round=" + d.Rules.DropsThisRound
                   + " deadeye=" + d._deadeyeLeft.ToString("0.0") + " payday=" + d._paydayLeft.ToString("0.0")
                   + " mult=" + (PlayerWallet.Instance != null ? PlayerWallet.Instance.Multiplier : 0);
        }

        // Runs the drop rules over a batch of kills with a seeded RNG (rounds of roundKills kills) and reports the rates.
        public static string DebugSimulate(int kills, int roundKills, int seed)
        {
            var rules = new DropRules(new System.Random(seed));
            var counts = new int[4];
            int drops = 0, maxGap = 0, gap = 0, repeats = 0, maxRound = 0, inRound = 0;
            int last = -1;
            for (int k = 0; k < kills; k++)
            {
                if (k % roundKills == 0) { rules.NewRound(); inRound = 0; }
                gap++;
                if (rules.OnKill(out var t))
                {
                    drops++; counts[(int)t]++; inRound++;
                    if (inRound > maxRound) maxRound = inRound;
                    if (gap > maxGap) maxGap = gap;
                    gap = 0;
                    if ((int)t == last) repeats++;
                    last = (int)t;
                }
            }
            var sb = new StringBuilder();
            sb.Append("kills=").Append(kills).Append(" drops=").Append(drops)
              .Append(" perDrop=").Append(drops > 0 ? ((float)kills / drops).ToString("0.0") : "-")
              .Append(" maxGap=").Append(maxGap).Append(" maxPerRound=").Append(maxRound).Append(" repeats=").Append(repeats)
              .Append(" ammo=").Append(counts[0]).Append(" shock=").Append(counts[1]).Append(" deadeye=").Append(counts[2]).Append(" payday=").Append(counts[3]);
            return sb.ToString();
        }
    }
}
