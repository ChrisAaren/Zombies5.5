using UnityEngine;

namespace Zombies55
{
    // Synthesized round stings and the enemy death sound. Every volume is multiplied by
    // GameAudio.SfxVolume at play time.
    public class RoundAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        const int WorldVoices = 8;

        public static RoundAudio Instance { get; private set; }

        AudioSource _ui;
        AudioSource[] _world;
        int _nextVoice;
        AudioClip _roundStart, _roundComplete, _death;
        RoundManager _rounds;

        void Awake()
        {
            Instance = this;
            _roundStart = MakeRoundStart();
            _roundComplete = MakeRoundComplete();
            _death = MakeDeath();

            _ui = gameObject.AddComponent<AudioSource>();
            _ui.playOnAwake = false;
            _ui.spatialBlend = 0f;

            _world = new AudioSource[WorldVoices];
            for (int i = 0; i < WorldVoices; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 3f;
                s.maxDistance = 45f;
                _world[i] = s;
            }
        }

        void OnEnable() => Instance = this;

        void Start()
        {
            _rounds = RoundManager.Instance;
            if (_rounds != null)
            {
                _rounds.RoundStarted += OnRoundStarted;
                _rounds.RoundCompleted += OnRoundCompleted;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_rounds != null)
            {
                _rounds.RoundStarted -= OnRoundStarted;
                _rounds.RoundCompleted -= OnRoundCompleted;
            }
        }

        void OnRoundStarted(int round) => _ui.PlayOneShot(_roundStart, 0.8f * GameAudio.SfxVolume);

        void OnRoundCompleted(int round) => _ui.PlayOneShot(_roundComplete, 0.8f * GameAudio.SfxVolume);

        public void PlayEnemyDeath(Vector3 position)
        {
            var s = _world[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _world.Length;
            s.transform.position = position;
            s.pitch = Random.Range(0.8f, 1.15f);
            s.volume = 0.6f * GameAudio.SfxVolume;
            s.clip = _death;
            s.Play();
        }

        // ---- Synthesis ----------------------------------------------------------------------

        static AudioClip Finish(string name, float[] d)
        {
            int n = Mathf.Min(d.Length / 2, Mathf.RoundToInt(0.005f * SampleRate));
            for (int i = 0; i < n; i++)
            {
                float g = i / (float)n;
                d[i] *= g;
                d[d.Length - 1 - i] *= g;
            }
            var clip = AudioClip.Create(name, d.Length, 1, SampleRate, false);
            clip.SetData(d, 0);
            return clip;
        }

        // Adds a soft note (sine with a touch of square) with an exponential decay.
        static void AddNote(float[] d, float start, float freq, float dur, float decay, float amp)
        {
            int s0 = Mathf.RoundToInt(start * SampleRate);
            int count = Mathf.Min(d.Length - s0, Mathf.RoundToInt(dur * SampleRate));
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Min(1f, t / 0.01f) * Mathf.Exp(-t * decay);
                float w = Mathf.Sin(2f * Mathf.PI * freq * t);
                d[s0 + i] += (w * 0.6f + Mathf.Sign(w) * 0.08f) * env * amp;
            }
        }

        // Round start: a low boom and a tense two-note rise.
        static AudioClip MakeRoundStart()
        {
            var d = new float[Mathf.RoundToInt(1.4f * SampleRate)];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float f = Mathf.Lerp(90f, 45f, Mathf.Clamp01(t / 0.5f));
                d[i] += Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 4f) * 0.7f;
            }
            AddNote(d, 0.05f, 196f, 0.9f, 3.5f, 0.35f);
            AddNote(d, 0.28f, 233.08f, 1.0f, 3.2f, 0.35f);
            AddNote(d, 0.28f, 349.23f, 1.0f, 3.2f, 0.18f);
            return Finish("RoundStart", d);
        }

        // Round complete: a bright rising major arpeggio.
        static AudioClip MakeRoundComplete()
        {
            var d = new float[Mathf.RoundToInt(1.6f * SampleRate)];
            float[] notes = { 392f, 493.88f, 587.33f, 783.99f };
            for (int n = 0; n < notes.Length; n++)
                AddNote(d, n * 0.12f, notes[n], n == notes.Length - 1 ? 1.1f : 0.5f, n == notes.Length - 1 ? 3f : 8f, 0.3f);
            return Finish("RoundComplete", d);
        }

        // Enemy goes down: a falling breathy groan with a thud underneath.
        static AudioClip MakeDeath()
        {
            var d = new float[Mathf.RoundToInt(0.55f * SampleRate)];
            var rng = new System.Random(311);
            double phase = 0;
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float u = i / (float)d.Length;
                float f = Mathf.Lerp(150f, 55f, u);
                phase += 2.0 * Mathf.PI * f / SampleRate;
                float tone = (float)System.Math.Sin(phase) * 0.5f + (float)System.Math.Sin(phase * 2.0) * 0.15f;
                lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.15f;
                float env = Mathf.Min(1f, t / 0.02f) * Mathf.Exp(-t * 5f);
                float thud = Mathf.Sin(2f * Mathf.PI * 60f * t) * Mathf.Exp(-t * 14f) * (t > 0.18f ? 0f : 0.5f);
                d[i] = (tone + lp * 0.6f) * env * 0.7f + thud;
            }
            return Finish("EnemyDeath", d);
        }
    }
}
