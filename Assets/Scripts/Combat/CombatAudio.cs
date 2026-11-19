using System.Collections.Generic;
using UnityEngine;

namespace Zombies55
{
    // Synthesized combat sounds (no audio files): a distinct shot per weapon, reload clicks, dry fire,
    // buy chime, deny buzz and the explosion boom. All volumes multiply GameAudio.SfxVolume.
    public class CombatAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        const int SourceCount = 10;

        public static CombatAudio Instance { get; private set; }

        readonly Dictionary<string, AudioClip> _shots = new Dictionary<string, AudioClip>();
        AudioSource[] _sources;
        int _next;
        AudioClip _dry, _reloadOut, _reloadIn, _reloadSlide, _swap, _buy, _deny, _boom;

        void Awake()
        {
            Instance = this;
            //                         id          seed len   lowpass noiseDecay thumpHz0 thumpHz1 thumpDecay crack
            _shots["p9"] = MakeShot("Shot_P9", 11, 0.22f, 0.55f, 28f, 150f, 70f, 30f, 0.5f, 1f);
            _shots["wasp"] = MakeShot("Shot_Wasp", 12, 0.16f, 0.62f, 40f, 180f, 95f, 42f, 0.4f, 0.9f);
            _shots["breaker"] = MakeShot("Shot_Breaker", 13, 0.55f, 0.82f, 9f, 95f, 40f, 12f, 0.7f, 1.2f);
            _shots["kestrel"] = MakeShot("Shot_Kestrel", 14, 0.22f, 0.66f, 26f, 125f, 60f, 28f, 0.6f, 1f);
            _shots["longshot"] = MakeShot("Shot_Longshot", 15, 0.5f, 0.76f, 12f, 105f, 45f, 14f, 0.9f, 1.1f);
            _shots["atlas"] = MakeShot("Shot_Atlas", 16, 0.2f, 0.72f, 30f, 85f, 45f, 24f, 0.5f, 1f);

            _dry = MakeClick("Dry", 2200f, 0.03f, 0.3f);
            _reloadOut = MakeClick("ReloadOut", 1300f, 0.06f, 0.5f);
            _reloadIn = MakeClick("ReloadIn", 850f, 0.07f, 0.6f);
            _reloadSlide = MakeSlide();
            _swap = MakeSwap();
            _buy = MakeChime();
            _deny = MakeDeny();
            _boom = MakeBoom();

            _sources = new AudioSource[SourceCount];
            for (int i = 0; i < SourceCount; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                _sources[i] = s;
            }
        }

        void OnEnable() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ play

        void Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || _sources == null) return;
            var s = _sources[_next];
            _next = (_next + 1) % _sources.Length;
            s.clip = clip;
            s.pitch = pitch;
            s.volume = Mathf.Clamp01(volume * GameAudio.SfxVolume);
            s.Play();
        }

        public void PlayShot(string weaponId)
        {
            if (!_shots.TryGetValue(weaponId ?? "", out var clip)) clip = _shots["p9"];
            Play(clip, 0.7f, 1f + Random.Range(-0.05f, 0.05f));
        }

        public void PlayDryFire() => Play(_dry, 0.5f, 1f + Random.Range(-0.04f, 0.04f));

        // 0 = magazine out, 1 = magazine in, 2 = slide / pump.
        public void PlayReloadStage(int stage)
        {
            switch (stage)
            {
                case 0: Play(_reloadOut, 0.55f, 1f); break;
                case 1: Play(_reloadIn, 0.6f, 1f); break;
                default: Play(_reloadSlide, 0.6f, 1f); break;
            }
        }

        public void PlaySwap() => Play(_swap, 0.4f, 1f + Random.Range(-0.05f, 0.05f));
        public void PlayBuy() => Play(_buy, 0.7f, 1f);
        public void PlayDeny() => Play(_deny, 0.6f, 1f);

        public void PlayExplosion(Vector3 position, bool big)
        {
            float dist = 0f;
            var cam = Camera.main;
            if (cam != null) dist = Vector3.Distance(cam.transform.position, position);
            float atten = Mathf.Lerp(1f, 0.2f, Mathf.Clamp01(dist / 70f));
            Play(_boom, (big ? 1f : 0.85f) * atten, (big ? 0.85f : 1.05f) + Random.Range(-0.05f, 0.05f));
        }

        // ------------------------------------------------------------------ synthesis

        static AudioClip Build(string name, float[] data)
        {
            int n = Mathf.Min(data.Length / 2, Mathf.RoundToInt(0.005f * SampleRate)); // 5 ms edge fades
            for (int i = 0; i < n; i++)
            {
                float g = i / (float)n;
                data[i] *= g;
                data[data.Length - 1 - i] *= g;
            }
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Soft(float v) => (float)System.Math.Tanh(v);

        // Noise burst (filtered), low thump and a bright crack at the start.
        static AudioClip MakeShot(string name, int seed, float len, float lowpass, float noiseDecay,
            float f0, float f1, float thumpDecay, float thumpGain, float crackGain)
        {
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            var rng = new System.Random(seed);
            float y = 0f;
            double phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                y += (n - y) * (1f - lowpass);
                float noise = y * Mathf.Exp(-t * noiseDecay) * 2.2f;
                float crack = (n - y) * Mathf.Exp(-t * 350f) * 0.9f * crackGain;
                float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / 0.08f));
                phase += 2 * System.Math.PI * f / SampleRate;
                float thump = (float)System.Math.Sin(phase) * Mathf.Exp(-t * thumpDecay) * thumpGain * 1.4f;
                d[i] = Soft((noise + crack + thump) * 1.2f) * 0.9f;
            }
            return Build(name, d);
        }

        static AudioClip MakeClick(string name, float freq, float len, float noiseMix)
        {
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * 110f);
                float tone = Mathf.Sin(2 * Mathf.PI * freq * t);
                float n = (float)(rng.NextDouble() * 2 - 1);
                d[i] = (tone * (1f - noiseMix) + n * noiseMix) * env * 0.7f;
            }
            return Build(name, d);
        }

        static AudioClip MakeSlide()
        {
            var d = new float[Mathf.RoundToInt(0.16f * SampleRate)];
            var rng = new System.Random(77);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                y += (n - y) * 0.4f;
                float scrape = y * Mathf.Exp(-t * 18f) * 0.35f;
                float c1 = Mathf.Sin(2 * Mathf.PI * 1100f * t) * Mathf.Exp(-t * 130f);
                float t2 = t - 0.085f;
                float c2 = t2 > 0f ? Mathf.Sin(2 * Mathf.PI * 1800f * t2) * Mathf.Exp(-t2 * 120f) : 0f;
                d[i] = (scrape + c1 * 0.5f + c2 * 0.55f) * 0.8f;
            }
            return Build("ReloadSlide", d);
        }

        static AudioClip MakeSwap()
        {
            var d = new float[Mathf.RoundToInt(0.1f * SampleRate)];
            var rng = new System.Random(5);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float n = (float)(rng.NextDouble() * 2 - 1);
                y += (n - y) * 0.2f;
                d[i] = y * Mathf.Sin(t * Mathf.PI) * 0.8f;
            }
            return Build("Swap", d);
        }

        static AudioClip MakeChime()
        {
            var d = new float[Mathf.RoundToInt(0.4f * SampleRate)];
            float[] notes = { 783.99f, 1174.66f };
            for (int n = 0; n < notes.Length; n++)
            {
                int s0 = Mathf.RoundToInt(n * 0.09f * SampleRate);
                for (int i = 0; s0 + i < d.Length; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Min(1f, t / 0.006f) * Mathf.Exp(-t * 9f);
                    float w = Mathf.Sin(2 * Mathf.PI * notes[n] * t) + 0.25f * Mathf.Sin(4 * Mathf.PI * notes[n] * t);
                    d[s0 + i] += w * env * 0.35f;
                }
            }
            return Build("Buy", d);
        }

        static AudioClip MakeDeny()
        {
            var d = new float[Mathf.RoundToInt(0.2f * SampleRate)];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float saw = 2f * (t * 110f - Mathf.Floor(t * 110f + 0.5f));
                float trem = 0.6f + 0.4f * Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 28f * t));
                d[i] = saw * trem * 0.4f * (1f - t / 0.2f * 0.4f);
            }
            return Build("Deny", d);
        }

        static AudioClip MakeBoom()
        {
            float len = 1.8f;
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            var rng = new System.Random(99);
            float brown = 0f, y = 0f;
            double phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                brown += (n - brown) * 0.06f;
                y += (n - y) * 0.45f;
                float rumble = brown * Mathf.Exp(-t * 2.3f) * 9f;
                float crack = y * Mathf.Exp(-t * 25f) * 1.4f;
                float f = Mathf.Lerp(65f, 28f, Mathf.Clamp01(t / 0.6f));
                phase += 2 * System.Math.PI * f / SampleRate;
                float sub = (float)System.Math.Sin(phase) * Mathf.Exp(-t * 2.6f) * 1.2f;
                d[i] = Soft(rumble + crack + sub) * 0.95f;
            }
            return Build("Boom", d);
        }
    }
}
