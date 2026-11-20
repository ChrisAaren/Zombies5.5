using UnityEngine;

namespace Zombies55
{
    // Synthesized item-drop sounds (no audio files): a distinct pickup sound per drop, the spawn ping, the expiry fizzle, the
    // effect-over tick and the big Shockwave. All volumes multiply GameAudio.SfxVolume.
    public class PickupAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        const int SourceCount = 6;

        public static PickupAudio Instance { get; private set; }

        readonly AudioClip[] _pickup = new AudioClip[4];
        AudioClip _spawn, _expire, _end, _warn;
        AudioSource[] _sources;
        int _next;

        void Awake()
        {
            Instance = this;
            _pickup[(int)PickupType.AmmoCache] = MakeAmmo();
            _pickup[(int)PickupType.Shockwave] = MakeShockwave();
            _pickup[(int)PickupType.Deadeye] = MakeDeadeye();
            _pickup[(int)PickupType.Payday] = MakePayday();
            _spawn = MakeSpawn();
            _expire = MakeExpire();
            _end = MakeEnd();
            _warn = MakeWarn();

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

        public void PlayPickup(PickupType t) => Play(_pickup[(int)t], t == PickupType.Shockwave ? 1f : 0.85f, 1f);
        public void PlaySpawn(float volume) => Play(_spawn, volume, 1f + Random.Range(-0.06f, 0.06f));
        public void PlayExpire() => Play(_expire, 0.55f, 1f + Random.Range(-0.05f, 0.05f));
        public void PlayEffectEnd() => Play(_end, 0.6f, 1f);
        public void PlayWarn() => Play(_warn, 0.35f, 1f);

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

        static void AddTone(float[] d, float startSec, float freq, float decay, float gain, float overtone = 0.25f)
        {
            int s0 = Mathf.RoundToInt(startSec * SampleRate);
            for (int i = 0; s0 + i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-t * decay);
                if (env < 0.001f) break;
                float w = Mathf.Sin(2 * Mathf.PI * freq * t) + overtone * Mathf.Sin(4 * Mathf.PI * freq * t)
                          + overtone * 0.6f * Mathf.Sin(2 * Mathf.PI * freq * 2.76f * t);
                d[s0 + i] += w * env * gain;
            }
        }

        static void AddClick(float[] d, float startSec, float freq, float noiseMix, float gain, System.Random rng)
        {
            int s0 = Mathf.RoundToInt(startSec * SampleRate);
            for (int i = 0; s0 + i < d.Length && i < 0.08f * SampleRate; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * 90f);
                float n = (float)(rng.NextDouble() * 2 - 1);
                d[s0 + i] += (Mathf.Sin(2 * Mathf.PI * freq * t) * (1f - noiseMix) + n * noiseMix) * env * gain;
            }
        }

        // Magazine slap and bolt rack, then a warm confirmation ding.
        static AudioClip MakeAmmo()
        {
            var d = new float[Mathf.RoundToInt(0.6f * SampleRate)];
            var rng = new System.Random(31);
            AddClick(d, 0f, 900f, 0.55f, 0.8f, rng);
            AddClick(d, 0.085f, 1500f, 0.45f, 0.7f, rng);
            AddClick(d, 0.17f, 620f, 0.5f, 0.9f, rng);
            AddTone(d, 0.2f, 988f, 7f, 0.3f);
            AddTone(d, 0.27f, 1318f, 6f, 0.28f);
            for (int i = 0; i < d.Length; i++) d[i] = Soft(d[i] * 1.1f) * 0.85f;
            return Build("PickupAmmo", d);
        }

        // Big: a quick air-pressure suck-in, then a deep boom with a falling sub and a falling shimmer.
        static AudioClip MakeShockwave()
        {
            float len = 2.1f;
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            var rng = new System.Random(32);
            float brown = 0f, y = 0f;
            double phase = 0, shimmer = 0;
            const float hitAt = 0.18f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                brown += (n - brown) * 0.05f;
                y += (n - y) * 0.35f;
                float swell = t < hitAt ? t / hitAt : 0f;
                float after = Mathf.Max(0f, t - hitAt);
                float hit = t >= hitAt ? Mathf.Exp(-after * 2.4f) : 0f;
                float rumble = brown * (hit * 10f + swell * 2f);
                float crack = y * Mathf.Exp(-after * 22f) * (t >= hitAt ? 1.6f : 0f);
                float f = t < hitAt ? Mathf.Lerp(40f, 140f, swell) : Mathf.Lerp(70f, 24f, Mathf.Clamp01(after / 0.7f));
                phase += 2 * System.Math.PI * f / SampleRate;
                float sub = (float)System.Math.Sin(phase) * (hit * 1.5f + swell * 0.5f);
                shimmer += 2 * System.Math.PI * Mathf.Lerp(2400f, 700f, Mathf.Clamp01(t / 1.4f)) / SampleRate;
                float tail = (float)System.Math.Sin(shimmer) * Mathf.Exp(-t * 3f) * 0.18f * (t >= hitAt ? 1f : 0f);
                d[i] = Soft(rumble + crack + sub + tail) * 0.95f;
            }
            return Build("PickupShockwave", d);
        }

        // Two heartbeat thumps, then a sharp focus ping that rings out.
        static AudioClip MakeDeadeye()
        {
            var d = new float[Mathf.RoundToInt(0.95f * SampleRate)];
            for (int b = 0; b < 2; b++)
            {
                int s0 = Mathf.RoundToInt(b * 0.17f * SampleRate);
                for (int i = 0; s0 + i < d.Length && i < 0.2f * SampleRate; i++)
                {
                    float t = i / (float)SampleRate;
                    float f = Mathf.Lerp(95f, 48f, Mathf.Clamp01(t / 0.1f));
                    d[s0 + i] += Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 22f) * (b == 0 ? 1f : 0.8f);
                }
            }
            AddTone(d, 0.36f, 2093f, 5f, 0.45f, 0.12f);
            AddTone(d, 0.36f, 1397f, 8f, 0.2f, 0.1f);
            for (int i = 0; i < d.Length; i++) d[i] = Soft(d[i]) * 0.9f;
            return Build("PickupDeadeye", d);
        }

        // Three quick metallic register pings, rising.
        static AudioClip MakePayday()
        {
            var d = new float[Mathf.RoundToInt(0.75f * SampleRate)];
            AddTone(d, 0f, 1568f, 9f, 0.3f, 0.45f);
            AddTone(d, 0.075f, 2093f, 8f, 0.3f, 0.45f);
            AddTone(d, 0.15f, 2637f, 6f, 0.34f, 0.45f);
            AddTone(d, 0.15f, 3136f, 7f, 0.12f, 0.2f);
            for (int i = 0; i < d.Length; i++) d[i] = Soft(d[i] * 1.2f) * 0.8f;
            return Build("PickupPayday", d);
        }

        // A drop landing: a soft thud and a high ping, short.
        static AudioClip MakeSpawn()
        {
            var d = new float[Mathf.RoundToInt(0.32f * SampleRate)];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                d[i] += Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(190f, 80f, Mathf.Clamp01(t / 0.12f)) * t) * Mathf.Exp(-t * 20f) * 0.5f;
            }
            AddTone(d, 0.03f, 1760f, 12f, 0.25f, 0.3f);
            return Build("PickupSpawn", d);
        }

        // A fading drop: a falling fizzle.
        static AudioClip MakeExpire()
        {
            var d = new float[Mathf.RoundToInt(0.4f * SampleRate)];
            var rng = new System.Random(33);
            float y = 0f;
            double phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                y += (n - y) * Mathf.Lerp(0.5f, 0.08f, t / 0.4f);
                phase += 2 * System.Math.PI * Mathf.Lerp(900f, 180f, t / 0.4f) / SampleRate;
                d[i] = (y * 0.5f + (float)System.Math.Sin(phase) * 0.35f) * Mathf.Exp(-t * 7f);
            }
            return Build("PickupExpire", d);
        }

        // Effect over: two falling ticks.
        static AudioClip MakeEnd()
        {
            var d = new float[Mathf.RoundToInt(0.4f * SampleRate)];
            AddTone(d, 0f, 880f, 22f, 0.5f, 0.1f);
            AddTone(d, 0.11f, 587f, 18f, 0.5f, 0.1f);
            return Build("PickupEnd", d);
        }

        static AudioClip MakeWarn()
        {
            var d = new float[Mathf.RoundToInt(0.08f * SampleRate)];
            AddTone(d, 0f, 1800f, 50f, 0.5f, 0.05f);
            return Build("PickupWarn", d);
        }
    }
}
