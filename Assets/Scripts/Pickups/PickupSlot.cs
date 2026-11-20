using UnityEngine;
using UnityEngine.Rendering;

namespace Zombies55
{
    // One pooled drop in the world: the four pickup models (one visible), a light beam and a ground glow. The director owns the
    // lifetime and calls Tick; nothing here allocates per frame (a shared MaterialPropertyBlock drives the colors).
    public class PickupSlot : MonoBehaviour
    {
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static MaterialPropertyBlock s_Block;

        [SerializeField] GameObject visual;           // everything that blinks
        [SerializeField] Transform spin;              // models hover, bob and spin here
        [SerializeField] GameObject[] models;         // indexed by PickupType
        [SerializeField] Transform beamRoot;
        [SerializeField] Renderer[] beams;
        [SerializeField] Renderer ground;

        Renderer[][] _glowRenderers;
        int[][] _glowIndex;

        public bool Active { get; private set; }
        public PickupType Type { get; private set; }
        public float Age { get; private set; }

        float _yaw, _phase, _blinkPhase;
        bool _visible = true;

        void Awake() => EnsureInit();

        void EnsureInit()
        {
            if (s_Block == null) s_Block = new MaterialPropertyBlock();
            if (_glowRenderers != null) return;
            _glowRenderers = new Renderer[models.Length][];
            _glowIndex = new int[models.Length][];
            for (int m = 0; m < models.Length; m++)
            {
                var rs = models[m].GetComponentsInChildren<Renderer>(true);
                var list = new System.Collections.Generic.List<Renderer>();
                var idx = new System.Collections.Generic.List<int>();
                foreach (var r in rs)
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && mats[i].name.StartsWith("S_PickupGlow")) { list.Add(r); idx.Add(i); }
                }
                _glowRenderers[m] = list.ToArray();
                _glowIndex[m] = idx.ToArray();
            }
            gameObject.SetActive(false);
        }

        public void Show(PickupType type, Vector3 groundPos)
        {
            EnsureInit();
            Type = type;
            Active = true;
            Age = 0f;
            _blinkPhase = 0f;
            _yaw = Random.value * 360f;
            _phase = Random.value * 6.28f;
            transform.position = groundPos;
            for (int i = 0; i < models.Length; i++) models[i].SetActive(i == (int)type);
            SetVisible(true);
            gameObject.SetActive(true);
            Tick(0f);
        }

        public void Hide()
        {
            Active = false;
            gameObject.SetActive(false);
        }

        public float Remaining => PickupTuning.DropLifetime - Age;

        void SetVisible(bool v)
        {
            if (_visible == v && visual.activeSelf == v) return;
            _visible = v;
            visual.SetActive(v);
        }

        public void Tick(float dt)
        {
            if (!Active) return;
            Age += dt;
            float time = Time.time;

            // blink in the last seconds, faster as the end nears
            float remaining = Remaining;
            if (remaining < PickupTuning.BlinkSeconds)
            {
                float k = 1f - Mathf.Clamp01(remaining / PickupTuning.BlinkSeconds);
                _blinkPhase += dt * Mathf.Lerp(PickupTuning.BlinkSlowHz, PickupTuning.BlinkFastHz, k);
                SetVisible(_blinkPhase - Mathf.Floor(_blinkPhase) < 0.6f);
            }
            if (!_visible) return;

            float pop = Mathf.Clamp01(Age / PickupTuning.PopSeconds);
            float scale = Ease.OutBack(pop);
            _yaw += dt * PickupTuning.SpinDegreesPerSecond;
            spin.localPosition = new Vector3(0f, PickupTuning.Hover + Mathf.Sin(time * 2.1f + _phase) * PickupTuning.BobAmplitude, 0f);
            spin.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            spin.localScale = Vector3.one * (PickupTuning.ModelScale * scale);

            // glow pulse 2..4 on the PickupGlow slot
            float p = 0.5f + 0.5f * Mathf.Sin(time * PickupTuning.PulseHz * 6.2832f + _phase);
            Color baseColor = PickupTuning.ColorOf(Type);
            float emission = Mathf.Lerp(PickupTuning.EmissionMin, PickupTuning.EmissionMax, p);
            Color e = baseColor * emission;
            e.a = 1f;
            s_Block.Clear();
            s_Block.SetColor(EmissionId, e);
            var rs = _glowRenderers[(int)Type];
            var ix = _glowIndex[(int)Type];
            for (int i = 0; i < rs.Length; i++) rs[i].SetPropertyBlock(s_Block, ix[i]);

            // beam and ground glow (additive): the beam breathes, both grow in with the pop
            beamRoot.localScale = new Vector3(1f, scale, 1f);
            float fx = scale * (0.75f + 0.25f * p);
            s_Block.Clear();
            s_Block.SetColor(ColorId, baseColor * (PickupTuning.BeamIntensity * fx));
            for (int i = 0; i < beams.Length; i++) beams[i].SetPropertyBlock(s_Block);
            s_Block.Clear();
            s_Block.SetColor(ColorId, baseColor * (PickupTuning.GroundGlowIntensity * fx));
            ground.SetPropertyBlock(s_Block);
        }
    }
}
