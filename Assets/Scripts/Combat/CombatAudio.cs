using System.Collections.Generic;
using UnityEngine;

namespace Zombies55
{
    // Synthesized combat sounds (no audio files): a distinct shot per weapon, reload clicks, dry fire,
    // buy chime, deny buzz and the layered explosion. All volumes multiply GameAudio.SfxVolume.
    public class CombatAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        const int SourceCount = 18;

        public static CombatAudio Instance { get; private set; }

        readonly Dictionary<string, AudioClip> _shots = new Dictionary<string, AudioClip>();
        AudioSource[] _sources;
        int _next;
        AudioClip _dry, _reloadOut, _reloadIn, _reloadSlide, _swap, _buy, _deny, _bolt, _dud;
        // The layered blast: a near crack, a body boom, a distant rumble tail, glass, and a ringing for very close ones.
        AudioClip _crack, _body, _tail, _glass, _ring;
        AudioClip[] _clatter;
        AudioLowPassFilter _muffle;
        float _muffleStart = -10f, _muffleDur, _muffleStrength;
        float _clatterWindow; int _clatterCount;
        // Per-gun reload sounds (magazine out, magazine in, slide / close) for the guns that do not use the shared ones.
        readonly Dictionary<string, AudioClip[]> _reloads = new Dictionary<string, AudioClip[]>();

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

            _shots["mule"] = AddEcho(MakeShot("Shot_Mule", 17, 0.62f, 0.78f, 10f, 135f, 55f, 15f, 1.0f, 1.3f), 0.09f, 0.22f);
            _shots["rook"] = MakeShot("Shot_Rook", 18, 0.15f, 0.58f, 38f, 210f, 115f, 44f, 0.4f, 1.15f);
            _shots["bulldog"] = MakeShot("Shot_Bulldog", 19, 0.4f, 0.85f, 13f, 92f, 38f, 13f, 0.85f, 1.0f);
            _shots["warden"] = AddEcho(MakeShot("Shot_Warden", 20, 1.3f, 0.72f, 5.5f, 78f, 26f, 5.5f, 1.35f, 1.7f), 0.21f, 0.3f);
            _shots["hammerhead"] = MakeThoomp();

            _reloads["mule"] = new[] { MakeRatchet("Mule_Eject", 5, 1500f, 0.032f, 0.55f), MakeRatchet("Mule_Load", 3, 900f, 0.07f, 0.7f),
                MakeRatchet("Mule_Close", 4, 1100f, 0.026f, 0.6f, 0.35f) };
            _reloads["rook"] = new[] { MakeClick("Rook_Out", 1750f, 0.05f, 0.4f), MakeClick("Rook_In", 1050f, 0.06f, 0.5f),
                MakeSlideVar("Rook_Slide", 0.13f, 1500f, 2300f, 0.45f) };
            _reloads["bulldog"] = new[] { MakeThunk("Bulldog_Out", 320f, 0.1f), MakeThunk("Bulldog_In", 190f, 0.13f),
                MakeSlideVar("Bulldog_Slide", 0.2f, 620f, 980f, 0.55f) };
            _reloads["warden"] = new[] { MakeSlideVar("Warden_BoltUp", 0.22f, 900f, 1400f, 0.5f), MakeThunk("Warden_Mag", 150f, 0.14f),
                MakeSlideVar("Warden_BoltDown", 0.18f, 700f, 1200f, 0.5f) };
            _reloads["hammerhead"] = new[] { MakeRatchet("GL_Latch", 2, 1300f, 0.05f, 0.55f), MakeThunk("GL_Shell", 260f, 0.09f),
                MakeThunk("GL_Close", 420f, 0.07f) };
            _bolt = MakeSlideVar("Bolt", 0.3f, 800f, 1500f, 0.5f);
            _dud = MakeDud();

            _dry = MakeClick("Dry", 2200f, 0.03f, 0.3f);
            _reloadOut = MakeClick("ReloadOut", 1300f, 0.06f, 0.5f);
            _reloadIn = MakeClick("ReloadIn", 850f, 0.07f, 0.6f);
            _reloadSlide = MakeSlide();
            _swap = MakeSwap();
            _buy = MakeChime();
            _deny = MakeDeny();
            _crack = MakeCrack();
            _body = MakeBody();
            _tail = MakeTail();
            _glass = MakeGlass();
            _ring = MakeRing();
            _clatter = new[] { MakeClatter(1), MakeClatter(2), MakeClatter(3), MakeClatter(4) };

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

        // 0 = magazine out, 1 = magazine in, 2 = slide / pump. A gun with its own sounds uses them.
        public void PlayReloadStage(int stage, string weaponId = null)
        {
            if (weaponId != null && _reloads.TryGetValue(weaponId, out var set))
            {
                Play(set[Mathf.Clamp(stage, 0, 2)], 0.6f, 1f + Random.Range(-0.03f, 0.03f));
                return;
            }
            switch (stage)
            {
                case 0: Play(_reloadOut, 0.55f, 1f); break;
                case 1: Play(_reloadIn, 0.6f, 1f); break;
                default: Play(_reloadSlide, 0.6f, 1f); break;
            }
        }

        public void PlayBolt() => Play(_bolt, 0.6f, 1f);

        // A shell hit something closer than its arming distance: a flat thump, no boom.
        public void PlayDud(Vector3 position)
        {
            float dist = 0f;
            var cam = Camera.main;
            if (cam != null) dist = Vector3.Distance(cam.transform.position, position);
            Play(_dud, 0.7f * Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(dist / 50f)), 1f + Random.Range(-0.06f, 0.06f));
        }

        public void PlaySwap() => Play(_swap, 0.4f, 1f + Random.Range(-0.05f, 0.05f));
        public void PlayBuy() => Play(_buy, 0.7f, 1f);
        public void PlayDeny() => Play(_deny, 0.6f, 1f);

        void PlayDelayed(AudioClip clip, float volume, float pitch, float delay)
        {
            if (clip == null || _sources == null || volume <= 0.003f) return;
            var s = _sources[_next];
            _next = (_next + 1) % _sources.Length;
            s.clip = clip;
            s.pitch = pitch;
            s.volume = Mathf.Clamp01(volume * GameAudio.SfxVolume);
            if (delay > 0.001f) s.PlayDelayed(delay); else s.Play();
        }

        // A blast heard in layers. The crack is sharp and carries only a short way; the body boom is heavy and carries further; the
        // rumble tail arrives a beat later and lasts. Glass follows for cars (the debris clatter comes from the parts themselves).
        // A blast that close deafens for a moment: a muffled mix and a ringing tone.
        public void PlayExplosion(Vector3 position, BlastKind kind, float radius)
        {
            float dist = 0f;
            var cam = Camera.main;
            if (cam != null) dist = Vector3.Distance(cam.transform.position, position);
            float size = kind == BlastKind.Car ? 1f : kind == BlastKind.Barrel ? 0.85f : 0.7f;
            float pitch = (kind == BlastKind.Car ? 0.9f : kind == BlastKind.Barrel ? 1f : 1.12f) + Random.Range(-0.04f, 0.04f);
            float travel = Mathf.Min(0.45f, dist / 343f);

            float near = Mathf.Clamp01(1f - dist / 55f);
            float mid = Mathf.Lerp(1f, 0.22f, Mathf.Clamp01(dist / 95f));
            float far = Mathf.Lerp(0.8f, 0.45f, Mathf.Clamp01(dist / 140f));

            PlayDelayed(_crack, 1.0f * size * (0.25f + 0.75f * near * near), pitch * 1.03f, travel);
            PlayDelayed(_body, 0.95f * size * mid, pitch, travel + 0.01f);
            PlayDelayed(_tail, 0.8f * size * far, pitch, travel + 0.12f + Mathf.Min(0.2f, dist / 600f));
            if (kind == BlastKind.Car && dist < 45f)
                PlayDelayed(_glass, 0.5f * Mathf.Clamp01(1f - dist / 45f), 1f + Random.Range(-0.1f, 0.1f), travel + Random.Range(0.12f, 0.3f));

            float ringRange = radius * 1.25f;
            if (dist < ringRange)
            {
                float k = 1f - dist / ringRange;
                PlayDelayed(_ring, 0.1f + 0.22f * k, 1f, travel + 0.05f);
                Muffle(k, 1.2f + 2.2f * k);
            }
        }

        void Muffle(float strength, float seconds)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (_muffle == null)
            {
                var listener = cam.GetComponent<AudioListener>();
                if (listener == null) return;
                _muffle = listener.GetComponent<AudioLowPassFilter>();
                if (_muffle == null) _muffle = listener.gameObject.AddComponent<AudioLowPassFilter>();
            }
            _muffle.enabled = true;
            _muffleStart = Time.unscaledTime;
            _muffleDur = seconds;
            _muffleStrength = Mathf.Clamp01(strength);
        }

        void Update()
        {
            if (_muffle == null || !_muffle.enabled) return;
            float k = (Time.unscaledTime - _muffleStart) / _muffleDur;
            if (k >= 1f) { _muffle.cutoffFrequency = 22000f; _muffle.enabled = false; return; }
            // slams shut instantly, holds a moment, then opens back up
            float closed = Mathf.Lerp(22000f, 650f, _muffleStrength);
            float open = k < 0.25f ? 0f : Mathf.SmoothStep(0f, 1f, (k - 0.25f) / 0.75f);
            _muffle.cutoffFrequency = Mathf.Lerp(closed, 22000f, open * open);
        }

        // Debris hitting the street: a metal clink or clank, quieter with distance and rate-limited so a car's worth of parts is not a wall of noise.
        public void PlayClatter(Vector3 position, float intensity, float mass)
        {
            float now = Time.unscaledTime;
            if (now - _clatterWindow > 0.4f) { _clatterWindow = now; _clatterCount = 0; }
            if (_clatterCount >= 3) return;
            _clatterCount++;
            var cam = Camera.main;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, position) : 0f;
            float atten = Mathf.Clamp01(1f - dist / 45f);
            if (atten <= 0f) return;
            int i = mass > 15f ? Random.Range(2, 4) : Random.Range(0, 3);
            PlayDelayed(_clatter[i], (0.25f + 0.5f * intensity) * atten, (mass > 15f ? 0.75f : 1.05f) * Random.Range(0.9f, 1.15f), 0f);
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

        // Copies of the clip, delayed and quieter: the report bouncing off the buildings.
        static AudioClip AddEcho(AudioClip clip, float delay, float gain)
        {
            var d = new float[clip.samples];
            clip.GetData(d, 0);
            int off = Mathf.RoundToInt(delay * SampleRate);
            var o = (float[])d.Clone();
            for (int k = 1; k <= 2; k++)
                for (int i = 0; i + off * k < o.Length; i++) o[i + off * k] += d[i] * gain / k;
            for (int i = 0; i < o.Length; i++) o[i] = Soft(o[i]) * 0.92f;
            return Build(clip.name, o);
        }

        // A 40 mm launcher: a hollow "thoomp" (pitch-dropping body, a pipe resonance, a short puff of air) with no crack.
        static AudioClip MakeThoomp()
        {
            var d = new float[Mathf.RoundToInt(0.55f * SampleRate)];
            var rng = new System.Random(21);
            float y = 0f;
            double p1 = 0, p2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                y += (n - y) * 0.12f;
                p1 += 2 * System.Math.PI * Mathf.Lerp(150f, 42f, Mathf.Clamp01(t / 0.12f)) / SampleRate;
                p2 += 2 * System.Math.PI * 310f / SampleRate;
                float body = (float)System.Math.Sin(p1) * Mathf.Exp(-t * 11f) * 1.5f;
                float pipe = (float)System.Math.Sin(p2) * Mathf.Exp(-t * 26f) * 0.45f;
                float puff = y * Mathf.Exp(-t * 30f) * 2.2f;
                d[i] = Soft((body + pipe + puff) * 1.3f) * 0.9f;
            }
            return Build("Shot_Hammerhead", d);
        }

        // Several short metallic ticks (a revolver's ejector, a speed loader). The last tick can be quieter.
        static AudioClip MakeRatchet(string name, int ticks, float freq, float spacing, float gain, float lastGain = 1f)
        {
            var d = new float[Mathf.RoundToInt((ticks * spacing + 0.06f) * SampleRate)];
            var rng = new System.Random(name.GetHashCode());
            for (int k = 0; k < ticks; k++)
            {
                int s0 = Mathf.RoundToInt(k * spacing * SampleRate);
                float f = freq * (1f + 0.08f * k);
                float g = gain * (k == ticks - 1 ? lastGain : 1f);
                for (int i = 0; s0 + i < d.Length && i < 0.05f * SampleRate; i++)
                {
                    float t = i / (float)SampleRate;
                    float nz = (float)(rng.NextDouble() * 2 - 1);
                    d[s0 + i] += (Mathf.Sin(2 * Mathf.PI * f * t) * 0.6f + nz * 0.4f) * Mathf.Exp(-t * 120f) * g * 0.7f;
                }
            }
            return Build(name, d);
        }

        // A low mechanical knock (a heavy magazine seating, a breech closing).
        static AudioClip MakeThunk(string name, float freq, float len)
        {
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            var rng = new System.Random(name.GetHashCode());
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                y += (n - y) * 0.5f;
                float body = Mathf.Sin(2 * Mathf.PI * freq * t) * Mathf.Exp(-t * 38f);
                float click = y * Mathf.Exp(-t * 160f) * 0.8f;
                d[i] = (body * 0.9f + click) * 0.85f;
            }
            return Build(name, d);
        }

        // A scrape (filtered noise) ending in two clacks at the given frequencies.
        static AudioClip MakeSlideVar(string name, float len, float f1, float f2, float scrapeGain)
        {
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            var rng = new System.Random(name.GetHashCode());
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                y += (n - y) * 0.35f;
                float scrape = y * Mathf.Exp(-t * 14f) * scrapeGain;
                float c1 = Mathf.Sin(2 * Mathf.PI * f1 * t) * Mathf.Exp(-t * 120f);
                float t2 = t - len * 0.55f;
                float c2 = t2 > 0f ? Mathf.Sin(2 * Mathf.PI * f2 * t2) * Mathf.Exp(-t2 * 110f) : 0f;
                d[i] = (scrape + c1 * 0.5f + c2 * 0.6f) * 0.8f;
            }
            return Build(name, d);
        }

        // Grenade dud: a flat thud and a rattle, with none of the boom.
        static AudioClip MakeDud()
        {
            var d = new float[Mathf.RoundToInt(0.3f * SampleRate)];
            var rng = new System.Random(31);
            float y = 0f;
            double ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                y += (n - y) * 0.25f;
                ph += 2 * System.Math.PI * Mathf.Lerp(120f, 60f, Mathf.Clamp01(t / 0.1f)) / SampleRate;
                float thud = (float)System.Math.Sin(ph) * Mathf.Exp(-t * 22f);
                float rattle = y * Mathf.Exp(-t * 18f) * Mathf.Max(0f, Mathf.Sin(t * 150f)) * 0.5f;
                d[i] = Soft(thud * 1.2f + rattle) * 0.8f;
            }
            return Build("Dud", d);
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

        static AudioClip MakeCrack()
        {
            var d = new float[Mathf.RoundToInt(0.45f * SampleRate)];
            var rng = new System.Random(311);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                lp += (n - lp) * 0.35f;
                lp2 += (n - lp2) * 0.05f;
                float hp = n - lp2;                                  // bright noise
                float snap = (hp * Mathf.Exp(-t * 55f) * 2.2f) + (lp * Mathf.Exp(-t * 18f) * 0.9f);
                float slap = Mathf.Sin(2 * Mathf.PI * 210f * t) * Mathf.Exp(-t * 45f) * 0.8f;
                d[i] = Soft((snap + slap) * 1.2f) * 0.95f;
            }
            return Build("BoomCrack", d);
        }

        static AudioClip MakeBody()
        {
            var d = new float[Mathf.RoundToInt(1.7f * SampleRate)];
            var rng = new System.Random(312);
            float brown = 0f, mid = 0f;
            double phase = 0, phase2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                brown += (n - brown) * 0.05f;
                mid += (n - mid) * 0.2f;
                float f = Mathf.Lerp(110f, 34f, Mathf.Clamp01(t / 0.45f));
                phase += 2 * System.Math.PI * f / SampleRate;
                phase2 += 2 * System.Math.PI * f * 2.02f / SampleRate;
                float sub = (float)System.Math.Sin(phase) * Mathf.Exp(-t * 2.8f) * 1.3f;
                float thump = (float)System.Math.Sin(phase2) * Mathf.Exp(-t * 9f) * 0.5f;
                float roar = (brown * 8f + mid * 1.2f) * Mathf.Exp(-t * 3.6f);
                float attack = Mathf.Clamp01(t / 0.006f);
                d[i] = Soft((sub + thump + roar) * attack) * 0.95f;
            }
            return Build("BoomBody", d);
        }

        static AudioClip MakeTail()
        {
            var d = new float[Mathf.RoundToInt(3.6f * SampleRate)];
            var rng = new System.Random(313);
            float brown = 0f, slow = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                brown += (n - brown) * 0.025f;
                slow += (n - slow) * 0.006f;
                // a swell, then a long decay, with a couple of building-side echoes
                float env = Mathf.Clamp01(t / 0.18f) * Mathf.Exp(-t * 1.15f);
                float echo = 0.5f * Mathf.Exp(-Mathf.Pow((t - 0.42f) * 5f, 2f)) + 0.35f * Mathf.Exp(-Mathf.Pow((t - 0.85f) * 4f, 2f));
                float rumble = (brown * 14f + slow * 22f) * (env + echo * 0.5f);
                float tone = Mathf.Sin(2 * Mathf.PI * 41f * t) * env * 0.35f;
                d[i] = Soft(rumble + tone) * 0.9f;
            }
            return Build("BoomTail", d);
        }

        static AudioClip MakeGlass()
        {
            var d = new float[Mathf.RoundToInt(1.1f * SampleRate)];
            var rng = new System.Random(314);
            for (int k = 0; k < 46; k++)
            {
                float start = -Mathf.Log(1f - (float)rng.NextDouble() * 0.95f) * 0.16f;   // most shards early
                float f = Mathf.Lerp(2800f, 8500f, (float)rng.NextDouble());
                float decay = Mathf.Lerp(35f, 110f, (float)rng.NextDouble());
                float amp = Mathf.Lerp(0.05f, 0.22f, (float)rng.NextDouble());
                int s0 = Mathf.RoundToInt(start * SampleRate);
                for (int i = s0; i < d.Length && i < s0 + SampleRate / 4; i++)
                {
                    float t = (i - s0) / (float)SampleRate;
                    d[i] += Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * decay) * amp;
                }
            }
            for (int i = 0; i < d.Length; i++) d[i] = Soft(d[i] * 1.4f) * 0.8f;
            return Build("BoomGlass", d);
        }

        static AudioClip MakeRing()
        {
            var d = new float[Mathf.RoundToInt(3.4f * SampleRate)];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Clamp01(t / 0.04f) * Mathf.Exp(-t * 0.9f);
                float v = Mathf.Sin(2 * Mathf.PI * 3150f * t) * 0.7f + Mathf.Sin(2 * Mathf.PI * 4210f * t + 1f) * 0.3f;
                d[i] = v * env * 0.5f;
            }
            return Build("BoomRing", d);
        }

        // One metal clink or clank: inharmonic partials with fast decay and a click.
        static AudioClip MakeClatter(int seed)
        {
            var d = new float[Mathf.RoundToInt(0.45f * SampleRate)];
            var rng = new System.Random(500 + seed);
            float baseF = seed <= 2 ? Mathf.Lerp(900f, 1700f, (float)rng.NextDouble()) : Mathf.Lerp(240f, 520f, (float)rng.NextDouble());
            var ratios = new[] { 1f, 1.51f, 2.32f, 3.9f };
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float v = 0f;
                for (int k = 0; k < ratios.Length; k++) v += Mathf.Sin(2 * Mathf.PI * baseF * ratios[k] * t) * Mathf.Exp(-t * (14f + 16f * k)) / (1f + k);
                float n = (float)(rng.NextDouble() * 2 - 1);
                lp += (n - lp) * 0.5f;
                v += lp * Mathf.Exp(-t * 220f) * 0.9f;
                d[i] = Soft(v * 1.1f) * 0.7f;
            }
            return Build("Clatter" + seed, d);
        }
    }
}
