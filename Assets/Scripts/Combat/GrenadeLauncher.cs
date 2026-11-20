using System.Collections.Generic;
using UnityEngine;

namespace Zombies55
{
    // Pool and launch maths for the 40 mm grenades. Lives on the player; the scene builder supplies the template (the
    // Proj_Grenade40 model with a smoke trail) and PlayerWeapons calls Launch.
    public class GrenadeLauncher : MonoBehaviour
    {
        const float Gravity = 9.81f;

        public static GrenadeLauncher Instance { get; private set; }

        [SerializeField] GameObject template;
        [SerializeField] int poolSize = 8;

        readonly Stack<GrenadeProjectile> _free = new Stack<GrenadeProjectile>();
        bool _built;

        public void Configure(GameObject grenadeTemplate) => template = grenadeTemplate;

        void Awake() { Instance = this; Build(); }
        void OnEnable() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Build()
        {
            if (_built || template == null) return;
            _built = true;
            template.SetActive(false);
            var parent = new GameObject("GrenadePool").transform;
            parent.SetParent(null, false);
            for (int i = 0; i < Mathf.Max(2, poolSize); i++)
            {
                var go = Instantiate(template, parent);
                go.name = "Grenade";
                var g = go.GetComponent<GrenadeProjectile>();
                if (g == null) g = go.AddComponent<GrenadeProjectile>();
                g.Init(this);
                _free.Push(g);
            }
        }

        internal void Release(GrenadeProjectile g) => _free.Push(g);

        // Fires from the muzzle on an arc that passes through aimPoint (when the shell can reach it), so the crosshair
        // is where the grenade lands. Out of reach it flies the flattest line the speed allows.
        public bool Launch(Vector3 muzzle, Vector3 aimPoint, WeaponDefinition def, GameObject owner)
        {
            Build();
            if (_free.Count == 0) return false;
            var g = _free.Pop();
            Vector3 vel = Solve(muzzle, aimPoint, def.ProjectileSpeed);
            g.Launch(muzzle, vel, def, owner, true);
            return true;
        }

        // Low-arc ballistic direction at the given speed from a to p.
        public static Vector3 Solve(Vector3 a, Vector3 p, float speed)
        {
            Vector3 d = p - a;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            float x = flat.magnitude;
            if (x < 0.5f) return d.normalized * speed;
            float v2 = speed * speed;
            float disc = v2 * v2 - Gravity * (Gravity * x * x + 2f * d.y * v2);
            float tan = disc >= 0f ? (v2 - Mathf.Sqrt(disc)) / (Gravity * x) : v2 / (Gravity * x);   // 45 degrees when out of reach
            Vector3 dirFlat = flat / x;
            return (dirFlat + Vector3.up * tan).normalized * speed;
        }
    }
}
