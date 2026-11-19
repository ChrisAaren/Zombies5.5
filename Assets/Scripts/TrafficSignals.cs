using UnityEngine;

namespace Zombies55
{
    // Cycles every traffic signal in the city on two shared phases (cross streets alternate), by driving the emission of
    // six runtime material clones. Cheap: no per-signal components, and it only touches materials when a phase changes.
    public class TrafficSignals : MonoBehaviour
    {
        [SerializeField] Material[] templatesZ = new Material[3];   // red, amber, green for traffic along Z
        [SerializeField] Material[] templatesX = new Material[3];   // red, amber, green for traffic along X
        [SerializeField] Renderer[] headsZ;
        [SerializeField] Renderer[] headsX;

        const float GreenTime = 9f, AmberTime = 2f, ClearTime = 1.5f;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        readonly Material[] _z = new Material[3];
        readonly Material[] _x = new Material[3];
        int _stateZ = -1, _stateX = -1;
        float _offset;

        public void Configure(Material[] z, Material[] x, Renderer[] zHeads, Renderer[] xHeads)
        {
            templatesZ = z;
            templatesX = x;
            headsZ = zHeads;
            headsX = xHeads;
        }

        void Awake()
        {
            Clone(templatesZ, _z, headsZ);
            Clone(templatesX, _x, headsX);
            _offset = Random.value * 20f;
        }

        void OnDestroy()
        {
            foreach (var m in _z) if (m != null) Destroy(m);
            foreach (var m in _x) if (m != null) Destroy(m);
        }

        static void Clone(Material[] templates, Material[] clones, Renderer[] heads)
        {
            for (int k = 0; k < 3; k++)
            {
                clones[k] = templates[k] != null ? new Material(templates[k]) : null;
                if (clones[k] != null) clones[k].EnableKeyword("_EMISSION");
            }
            foreach (var r in heads)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    for (int k = 0; k < 3; k++)
                        if (mats[i] == templates[k]) mats[i] = clones[k];
                r.sharedMaterials = mats;
            }
        }

        // 0 green, 1 amber, 2 red.
        static int Phase(float t)
        {
            if (t < GreenTime) return 0;
            if (t < GreenTime + AmberTime) return 1;
            return 2;
        }

        void Update()
        {
            float half = GreenTime + AmberTime + ClearTime;
            float t = (Time.time + _offset) % (half * 2f);
            int z = Phase(t);
            int x = Phase((t + half) % (half * 2f));
            if (z != _stateZ) { _stateZ = z; Apply(_z, z); }
            if (x != _stateX) { _stateX = x; Apply(_x, x); }
        }

        static void Apply(Material[] clones, int state)
        {
            for (int k = 0; k < 3; k++)
            {
                var m = clones[k];
                if (m == null) continue;
                // material index 0 red, 1 amber, 2 green; state 0 green, 1 amber, 2 red
                bool on = (k == 0 && state == 2) || (k == 1 && state == 1) || (k == 2 && state == 0);
                var c = m.GetColor("_BaseColor");
                m.SetColor(EmissionColor, on ? new Color(c.r, c.g, c.b, 1f) * 3f : Color.black);
            }
        }
    }
}
