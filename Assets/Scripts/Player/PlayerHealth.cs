using System;
using UnityEngine;

namespace Zombies55
{
    // The player's health: five melee hits to go down, a short grace window after each hit,
    // and regeneration once nothing has hurt the player for a few seconds.
    // Lives on the player root. Reaching zero ends the run through GameLoop.Lose().
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        public static PlayerHealth Instance { get; private set; }

        public const float MeleeDamage = 20f;

        // Benchmark mode: the player cannot be hurt, so the run never ends.
        public static bool GodMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => GodMode = false;

        [SerializeField] float maxHealth = 100f;
        [Tooltip("Seconds after a hit during which further damage is ignored.")]
        [SerializeField] float invulnerableSeconds = 0.25f;
        [Tooltip("Seconds without taking damage before regeneration starts.")]
        [SerializeField] float regenDelay = 4f;
        [Tooltip("Health per second while regenerating (full from empty in 2.5 s).")]
        [SerializeField] float regenPerSecond = 40f;
        [SerializeField] float explosionScale = 0.5f;

        public float MaxHealth => maxHealth;
        public float Health { get; private set; } = 100f;
        public float Normalized => Health / maxHealth;
        public bool IsAlive => Health > 0f;

        // How many melee hits it takes to go down (5 with the defaults).
        public int HitsToDown => Mathf.CeilToInt(maxHealth / MeleeDamage);
        public bool IsRegenerating { get; private set; }
        public float TimeSinceDamage => Time.time - _lastDamageTime;

        public event Action<DamageInfo> Damaged;
        public event Action<float> HealthChanged;   // normalized 0..1
        public event Action Died;

        const int SampleRate = 44100;

        float _lastDamageTime = -999f;
        float _invulnerableUntil;
        AudioSource _voice;
        AudioClip _grunt;

        void Awake()
        {
            Instance = this;
            Health = maxHealth;
            _grunt = MakeGrunt();
            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.spatialBlend = 0f;
        }

        // Re-register after a script reload during Play mode, which clears statics.
        void OnEnable() => Instance = this;

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (!IsAlive || Health >= maxHealth)
            {
                IsRegenerating = false;
                return;
            }
            var loop = GameLoop.Instance;
            if (loop != null && loop.State != RunState.Running) return;
            if (Time.time - _lastDamageTime < regenDelay)
            {
                IsRegenerating = false;
                return;
            }
            IsRegenerating = true;
            Health = Mathf.MoveTowards(Health, maxHealth, regenPerSecond * Time.deltaTime);
            HealthChanged?.Invoke(Normalized);
            if (Health >= maxHealth) IsRegenerating = false;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive || GodMode) return;
            var loop = GameLoop.Instance;
            if (loop != null && loop.State == RunState.Lost) return;
            if (Time.time < _invulnerableUntil) return;

            float amount = info.Kind == DamageKind.Explosion ? info.Amount * explosionScale : info.Amount;
            if (amount <= 0f) return;

            Health = Mathf.Max(0f, Health - amount);
            _lastDamageTime = Time.time;
            _invulnerableUntil = Time.time + invulnerableSeconds;
            IsRegenerating = false;

            PlayGrunt();
            Damaged?.Invoke(info);
            HealthChanged?.Invoke(Normalized);

            if (Health <= 0f)
            {
                Died?.Invoke();
                if (loop != null) loop.Lose();
            }
        }

        void PlayGrunt()
        {
            if (_voice == null || _grunt == null) return;
            _voice.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
            _voice.PlayOneShot(_grunt, 0.7f * GameAudio.SfxVolume);
        }

        // A short low grunt: a falling buzzy tone with breath noise, quick attack, fast decay.
        static AudioClip MakeGrunt()
        {
            float len = 0.22f;
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            var rng = new System.Random(77);
            double phase = 0;
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float u = i / (float)d.Length;
                float f = Mathf.Lerp(170f, 105f, u);
                phase += 2 * Math.PI * f / SampleRate;
                float saw = (float)((phase / (2 * Math.PI)) % 1.0) * 2f - 1f;
                float tone = saw * 0.5f + (float)Math.Sin(phase) * 0.35f;
                lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.25f;
                float env = Mathf.Min(1f, t / 0.012f) * Mathf.Exp(-t * 14f);
                d[i] = (tone * 0.8f + lp * 0.5f) * env * 0.8f;
            }
            int fade = Mathf.RoundToInt(0.005f * SampleRate);
            for (int i = 0; i < fade; i++)
            {
                float g = i / (float)fade;
                d[i] *= g;
                d[d.Length - 1 - i] *= g;
            }
            var clip = AudioClip.Create("PlayerGrunt", d.Length, 1, SampleRate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
