using UnityEngine;

namespace Zombies55
{
    // Synthesized barrier clank (no audio files). Volume scales with distance to the player.
    public class ZoneAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        AudioClip _clank;
        AudioSource _src;

        void Awake()
        {
            _clank = MakeClank();
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
        }

        public void PlayClank(Vector3 at)
        {
            if (_src == null) return;
            var p = PlayerHealth.Instance != null ? PlayerHealth.Instance.transform.position : at;
            float d = Vector3.Distance(p, at);
            float v = Mathf.Clamp01(1f - d / 70f);
            if (v <= 0.02f) return;
            _src.pitch = 1f + Random.Range(-0.04f, 0.04f);
            _src.PlayOneShot(_clank, 0.8f * v * GameAudio.SfxVolume);
        }

        static AudioClip MakeClank()
        {
            float len = 1.3f;
            var d = new float[Mathf.RoundToInt(len * SampleRate)];
            var rng = new System.Random(77);
            float brown = 0f;
            float[] hz = { 310f, 740f, 1290f, 2050f };
            float[] amp = { 0.5f, 0.35f, 0.25f, 0.15f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float s = 0f;
                // latch clank, then a smaller one when the panels bottom out
                for (int hit = 0; hit < 2; hit++)
                {
                    float th = t - (hit == 0 ? 0f : 0.55f);
                    if (th < 0f) continue;
                    float g = hit == 0 ? 1f : 0.6f;
                    for (int k = 0; k < hz.Length; k++)
                        s += Mathf.Sin(2f * Mathf.PI * hz[k] * (hit == 0 ? 1f : 0.8f) * th) * amp[k] * Mathf.Exp(-th * (9f + k * 5f)) * g;
                }
                // grinding rumble while the panels sink
                brown = brown * 0.985f + ((float)rng.NextDouble() * 2f - 1f) * 0.06f;
                float env = t < 1.0f ? Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) : 0f;
                s += brown * 2.2f * env * (0.6f + 0.4f * Mathf.Sin(t * 40f));
                d[i] = Mathf.Clamp(s * 0.6f, -1f, 1f);
            }
            int n = Mathf.RoundToInt(0.005f * SampleRate);
            for (int i = 0; i < n; i++) { float g = i / (float)n; d[i] *= g; d[d.Length - 1 - i] *= g; }
            var clip = AudioClip.Create("BarrierClank", d.Length, 1, SampleRate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
