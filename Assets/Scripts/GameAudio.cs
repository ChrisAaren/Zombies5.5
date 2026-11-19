using UnityEngine;

namespace Zombies55
{
    // Synthesized placeholder audio (no asset files) and a simple gain layer.
    // Music and SFX volumes are static floats every source multiplies in; the pause menu's
    // sliders set them. Swap in an AudioMixer (Master > Music, SFX, Ambient) later if you like.
    public class GameAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        const string MusicKey = "Zombies55.MusicVolume";
        const string SfxKey = "Zombies55.SfxVolume";

        public static GameAudio Instance { get; private set; }

        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicKey, 0.8f);
            set { PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value)); if (Instance != null) Instance.ApplyVolumes(); }
        }

        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(SfxKey, 0.9f);
            set { PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value)); if (Instance != null) Instance.ApplyVolumes(); }
        }

        [SerializeField] float ambientBaseVolume = 0.35f;

        AudioSource _ambient;
        AudioSource _sfx;
        AudioClip _blip, _winSting, _loseSting, _click, _jump, _land, _tick, _bed;
        float _ambientDuck = 1f;

        public AudioSource AmbientSource => _ambient;

        void Awake()
        {
            Instance = this;
            _blip = MakeBlip();
            _winSting = MakeSting(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.13f);
            _loseSting = MakeSting(new[] { 392f, 329.63f, 261.63f }, 0.18f);
            _click = MakeNoiseBurst("Click", 0.02f, 0.5f);
            _jump = MakeSweep("Jump", 300f, 520f, 0.12f, 0.25f);
            _land = MakeNoiseBurst("Land", 0.08f, 0.85f);
            _tick = MakeSweep("Tick", 1500f, 1400f, 0.04f, 0.25f);
            _bed = MakeAmbientBed();

            _ambient = gameObject.AddComponent<AudioSource>();
            _ambient.clip = _bed;
            _ambient.loop = true;
            _ambient.playOnAwake = false;
            _ambient.spatialBlend = 0f;

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f;

            ApplyVolumes();
            _ambient.Play();
        }

        // Re-register after a script reload during Play mode, which clears statics.
        void OnEnable()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void ApplyVolumes()
        {
            if (_ambient != null) _ambient.volume = ambientBaseVolume * MusicVolume * _ambientDuck;
            if (_sfx != null) _sfx.volume = SfxVolume;
        }

        // Lowers the ambient bed (about -6 dB) while paused.
        public void SetPausedDuck(bool paused)
        {
            _ambientDuck = paused ? 0.5f : 1f;
            ApplyVolumes();
        }

        public void PlayPickup() => PlayVaried(_blip, 0.08f, 0.9f);
        public void PlayWin() => _sfx.PlayOneShot(_winSting, 0.9f);
        public void PlayLose() => _sfx.PlayOneShot(_loseSting, 0.9f);
        public void PlayClick() => _sfx.PlayOneShot(_click, 0.6f);
        public void PlayJump() => PlayVaried(_jump, 0.06f, 0.45f);
        public void PlayLand(float strength) => PlayVaried(_land, 0.1f, Mathf.Lerp(0.25f, 0.8f, strength));
        public void PlayTick() => _sfx.PlayOneShot(_tick, 0.5f);

        void PlayVaried(AudioClip clip, float pitchRange, float volume)
        {
            _sfx.pitch = 1f + Random.Range(-pitchRange, pitchRange);
            _sfx.PlayOneShot(clip, volume);
            _sfx.pitch = 1f;
        }

        // ---- Synthesis ------------------------------------------------------------------------

        static AudioClip Build(string name, float[] data)
        {
            ApplyEdgeFades(data, 0.005f);
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void ApplyEdgeFades(float[] d, float seconds)
        {
            int n = Mathf.Min(d.Length / 2, Mathf.RoundToInt(seconds * SampleRate));
            for (int i = 0; i < n; i++)
            {
                float g = i / (float)n;
                d[i] *= g;
                d[d.Length - 1 - i] *= g;
            }
        }

        static AudioClip MakeBlip()
        {
            float len = 0.11f;
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            double phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float f = Mathf.Lerp(880f, 1320f, Mathf.Clamp01(t / 0.09f));
                phase += 2 * Mathf.PI * f / SampleRate;
                float env = Mathf.Exp(-t * 28f);
                d[i] = (float)(System.Math.Sin(phase) * 0.6 + System.Math.Sin(phase * 2) * 0.15) * env;
            }
            return Build("Blip", d);
        }

        static AudioClip MakeSting(float[] notes, float noteLength)
        {
            float overlap = noteLength * 0.25f;
            float len = notes.Length * (noteLength - overlap) + noteLength * 2.5f;
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            for (int n = 0; n < notes.Length; n++)
            {
                bool last = n == notes.Length - 1;
                float start = n * (noteLength - overlap);
                float dur = last ? noteLength * 3f : noteLength * 1.4f;
                int s0 = Mathf.RoundToInt(start * SampleRate);
                int count = Mathf.Min(d.Length - s0, Mathf.RoundToInt(dur * SampleRate));
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Min(1f, t / 0.008f) * Mathf.Exp(-t * (last ? 3f : 9f));
                    float w = Mathf.Sin(2 * Mathf.PI * notes[n] * t);
                    float sq = Mathf.Sign(w) * 0.12f; // a little square for bite
                    d[s0 + i] += (w * 0.35f + sq * 0.5f) * env;
                }
            }
            return Build("Sting", d);
        }

        static AudioClip MakeNoiseBurst(string name, float len, float lowpass)
        {
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            var rng = new System.Random(name.GetHashCode());
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float x = (float)(rng.NextDouble() * 2 - 1);
                y += (x - y) * (1f - lowpass);
                d[i] = y * (1f - t) * (1f - t) * 1.6f;
            }
            return Build(name, d);
        }

        static AudioClip MakeSweep(string name, float f0, float f1, float len, float amp)
        {
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            double phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                phase += 2 * Mathf.PI * Mathf.Lerp(f0, f1, t) / SampleRate;
                d[i] = (float)System.Math.Sin(phase) * amp * (1f - t);
            }
            return Build(name, d);
        }

        // City room tone: brown noise with a slow swell and a soft low drone. Loops seamlessly
        // because the swell and drone complete whole cycles and the noise crossfades its ends.
        static AudioClip MakeAmbientBed()
        {
            float len = 6f;
            int n = Mathf.RoundToInt(len * SampleRate);
            var d = new float[n];
            var rng = new System.Random(1234);
            float brown = 0f;
            for (int i = 0; i < n; i++)
            {
                brown = Mathf.Clamp(brown + (float)(rng.NextDouble() * 2 - 1) * 0.02f, -1f, 1f);
                brown *= 0.998f;
                float t = i / (float)SampleRate;
                float swell = 0.75f + 0.25f * Mathf.Sin(2 * Mathf.PI * t / len);
                float drone = Mathf.Sin(2 * Mathf.PI * 55f * t) * 0.05f + Mathf.Sin(2 * Mathf.PI * 82.5f * t) * 0.03f;
                d[i] = brown * 0.9f * swell + drone;
            }
            int xf = SampleRate / 2;
            for (int i = 0; i < xf; i++)
            {
                float g = i / (float)xf;
                d[i] = d[i] * g + d[n - xf + i] * (1f - g);
            }
            System.Array.Resize(ref d, n - xf);
            var clip = AudioClip.Create("AmbientBed", d.Length, 1, SampleRate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
