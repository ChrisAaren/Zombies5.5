using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Zombies55
{
    public enum ImpactKind { World, Enemy, Metal }

    // Pooled combat visuals: tracers, muzzle flash and light, impact puffs, explosions.
    // Particle systems are created once and reused through Emit. No blood: enemies spark.
    public class CombatFX : MonoBehaviour
    {
        public static CombatFX Instance { get; private set; }

        [SerializeField] Material particleMaterial;   // Sprites/Default; the soft texture is applied to a copy
        [SerializeField] int tracerPool = 20;
        [SerializeField] float tracerSpeed = 260f;

        struct TracerSlot
        {
            public LineRenderer Line;
            public Vector3 A, B;
            public float Start, Duration, Length, Dist;
            public bool Active;
        }

        Material _soft;
        ParticleSystem _dust, _sparks, _flash, _wisp, _fireball, _blastSmoke, _blastSparks, _blastDust;
        TracerSlot[] _tracers;
        int _nextTracer;
        Light _flashLight, _boomLight;
        float _flashUntil, _boomStart;
        CinemachineImpulseSource _impulse;

        void Awake()
        {
            Instance = this;
            var src = particleMaterial != null ? particleMaterial : new Material(Shader.Find("Sprites/Default"));
            _soft = new Material(src) { name = "CombatSoft", mainTexture = MakeSoftTexture() };

            _dust = MakeSystem("Dust", 80, -0.05f, Fade(1f, 0f), Curve(0.5f, 1.6f));
            _sparks = MakeSystem("Sparks", 120, 2.2f, SparkGradient(), Curve(1f, 0.2f), true);
            _flash = MakeSystem("MuzzleFlash", 16, 0f, Fade(1f, 0f), Curve(1f, 0.4f));
            _wisp = MakeSystem("MuzzleWisp", 40, -0.1f, Fade(0.5f, 0f), Curve(0.6f, 1.6f));
            _fireball = MakeSystem("Fireball", 40, -0.15f, FireGradient(), new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.7f)));
            _blastSmoke = MakeSystem("BlastSmoke", 60, -0.08f, Fade(0.75f, 0f), Curve(0.4f, 1.4f));
            _blastSparks = MakeSystem("BlastSparks", 100, 1.4f, SparkGradient(), Curve(1f, 0.3f), true);
            _blastDust = MakeSystem("BlastDust", 40, 0f, Fade(0.5f, 0f), Curve(0.6f, 1.5f));

            _tracers = new TracerSlot[Mathf.Max(4, tracerPool)];
            for (int i = 0; i < _tracers.Length; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.sharedMaterial = _soft;
                lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.01f), new Keyframe(1f, 0.05f));
                lr.startColor = Palette.WithAlpha(Palette.TracerWarm, 0f);
                lr.endColor = Palette.TracerWarm;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 2;
                lr.enabled = false;
                _tracers[i].Line = lr;
            }

            _flashLight = MakeLight("MuzzleLight", Palette.MuzzleFlash, 7f);
            _boomLight = MakeLight("BlastLight", Palette.FireOrange, 12f);
            _impulse = gameObject.AddComponent<CinemachineImpulseSource>();
        }

        void OnEnable() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ frame update

        void Update()
        {
            float now = Time.time;
            for (int i = 0; i < _tracers.Length; i++)
            {
                if (!_tracers[i].Active) continue;
                ref var t = ref _tracers[i];
                float k = (now - t.Start) / t.Duration;
                if (k >= 1f) { t.Active = false; t.Line.enabled = false; continue; }
                float travelled = k * (t.Dist + t.Length);
                float head = Mathf.Min(t.Dist, travelled);
                float tail = Mathf.Clamp(travelled - t.Length, 0f, t.Dist);
                Vector3 dir = (t.B - t.A) / Mathf.Max(0.001f, t.Dist);
                t.Line.SetPosition(0, t.A + dir * tail);
                t.Line.SetPosition(1, t.A + dir * head);
            }

            if (_flashLight.enabled && now >= _flashUntil) _flashLight.enabled = false;

            if (_boomLight.enabled)
            {
                float k = (now - _boomStart) / 0.4f;
                if (k >= 1f) _boomLight.enabled = false;
                else _boomLight.intensity = 12f * (1f - k) * (1f - k);
            }
        }

        // ------------------------------------------------------------------ public effects

        public void Tracer(Vector3 from, Vector3 to)
        {
            float dist = Vector3.Distance(from, to);
            if (dist < 1f) return;
            ref var t = ref _tracers[_nextTracer];
            _nextTracer = (_nextTracer + 1) % _tracers.Length;
            t.A = from;
            t.B = to;
            t.Dist = dist;
            t.Length = Mathf.Min(5f, dist * 0.5f);
            t.Start = Time.time;
            t.Duration = Mathf.Max(0.06f, (dist + t.Length) / tracerSpeed);
            t.Active = true;
            t.Line.SetPosition(0, from);
            t.Line.SetPosition(1, from);
            t.Line.enabled = true;
        }

        public void MuzzleFlash(Transform muzzle, float size)
        {
            Vector3 p = muzzle.position;
            Vector3 f = muzzle.forward;

            var ep = new ParticleSystem.EmitParams
            {
                position = p + f * 0.04f,
                velocity = Vector3.zero,
                startLifetime = 0.05f,
                startSize = 0.45f * size * Random.Range(0.85f, 1.2f),
                startColor = Palette.MuzzleFlash,
                rotation = Random.Range(0f, 360f),
            };
            _flash.Emit(ep, 1);
            ep.position = p + f * 0.14f;
            ep.startSize *= 0.5f;
            ep.startColor = Palette.UiLight;
            _flash.Emit(ep, 1);

            for (int i = 0; i < 3; i++)
            {
                var sp = new ParticleSystem.EmitParams
                {
                    position = p,
                    velocity = f * Random.Range(7f, 14f) + Random.insideUnitSphere * 1.5f,
                    startLifetime = Random.Range(0.08f, 0.16f),
                    startSize = 0.05f,
                    startColor = Palette.SparkYellow,
                };
                _sparks.Emit(sp, 1);
            }

            var wp = new ParticleSystem.EmitParams
            {
                position = p + f * 0.1f,
                velocity = f * Random.Range(0.8f, 1.6f) + Vector3.up * 0.4f,
                startLifetime = Random.Range(0.3f, 0.5f),
                startSize = 0.12f * size,
                startColor = Palette.WithAlpha(Palette.SmokeGrey, 0.5f),
            };
            _wisp.Emit(wp, 1);

            _flashLight.transform.position = p + f * 0.3f;
            _flashLight.intensity = Random.Range(2.5f, 4f) * size;
            _flashLight.enabled = true;
            _flashUntil = Time.time + 0.05f;
        }

        public void Impact(Vector3 point, Vector3 normal, ImpactKind kind)
        {
            Vector3 p = point + normal * 0.04f;
            int sparks = kind == ImpactKind.World ? 2 : kind == ImpactKind.Metal ? 8 : 6;
            for (int i = 0; i < sparks; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = p,
                    velocity = normal * Random.Range(2f, 5f) + Random.insideUnitSphere * 2.5f,
                    startLifetime = Random.Range(0.12f, 0.3f),
                    startSize = 0.045f,
                    startColor = kind == ImpactKind.Enemy ? Palette.UiLight : Palette.SparkYellow,
                };
                _sparks.Emit(ep, 1);
            }

            int puffs = kind == ImpactKind.World ? 4 : kind == ImpactKind.Enemy ? 2 : 1;
            Color dust = kind == ImpactKind.Enemy ? Palette.Trim : Palette.ConcreteTint;
            for (int i = 0; i < puffs; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = p,
                    velocity = normal * Random.Range(0.8f, 1.6f) + Random.insideUnitSphere * 0.5f,
                    startLifetime = Random.Range(0.35f, 0.6f),
                    startSize = Random.Range(0.2f, 0.38f),
                    startColor = Palette.WithAlpha(dust, 0.55f),
                    rotation = Random.Range(0f, 360f),
                };
                _dust.Emit(ep, 1);
            }
        }

        public void Explosion(Vector3 pos, float radius)
        {
            float s = Mathf.Clamp(radius / 6f, 0.6f, 1.6f);

            // Bright core
            var core = new ParticleSystem.EmitParams
            {
                position = pos + Vector3.up * 0.4f,
                velocity = Vector3.zero,
                startLifetime = 0.14f,
                startSize = 6f * s,
                startColor = Palette.UiLight,
            };
            _fireball.Emit(core, 1);

            for (int i = 0; i < 8; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = pos + Random.insideUnitSphere * 0.6f * s + Vector3.up * 0.4f,
                    velocity = Vector3.up * Random.Range(1f, 3.5f) + Random.insideUnitSphere * 1.8f,
                    startLifetime = Random.Range(0.45f, 0.8f),
                    startSize = Random.Range(2.2f, 4f) * s,
                    startColor = Color.white,
                    rotation = Random.Range(0f, 360f),
                };
                _fireball.Emit(ep, 1);
            }

            for (int i = 0; i < 14; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = pos + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.3f, 1.8f), Random.Range(-0.5f, 0.5f)) * s,
                    velocity = Vector3.up * Random.Range(2.5f, 5.5f) + Random.insideUnitSphere * 1.2f,
                    startLifetime = Random.Range(2.2f, 3.4f),
                    startSize = Random.Range(1.8f, 3.2f) * s,
                    startColor = Color.Lerp(Palette.SmokeGrey, Palette.SmokeDark, Random.value),
                    rotation = Random.Range(0f, 360f),
                };
                _blastSmoke.Emit(ep, 1);
            }

            for (int i = 0; i < 28; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = pos + Vector3.up * 0.5f,
                    velocity = (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(7f, 17f),
                    startLifetime = Random.Range(0.5f, 1.1f),
                    startSize = 0.08f,
                    startColor = Random.value < 0.5f ? Palette.SparkYellow : Palette.FireOrange,
                };
                _blastSparks.Emit(ep, 1);
            }

            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                var ep = new ParticleSystem.EmitParams
                {
                    position = pos + Vector3.up * 0.15f,
                    velocity = new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)) * Random.Range(4f, 7f) * s,
                    startLifetime = Random.Range(0.6f, 1f),
                    startSize = Random.Range(1f, 1.6f) * s,
                    startColor = Palette.WithAlpha(Palette.ConcreteTint, 0.5f),
                    rotation = Random.Range(0f, 360f),
                };
                _blastDust.Emit(ep, 1);
            }

            _boomLight.transform.position = pos + Vector3.up * 1.5f;
            _boomLight.range = Mathf.Max(10f, radius * 2.5f);
            _boomLight.intensity = 12f;
            _boomLight.enabled = true;
            _boomStart = Time.time;

            var cam = Camera.main;
            if (cam != null && _impulse != null)
            {
                float dist = Vector3.Distance(cam.transform.position, pos);
                float k = Mathf.Clamp01(1f - dist / (radius * 6f));
                if (k > 0.03f) _impulse.GenerateImpulse(1.2f * k * k * Mathf.Clamp(s, 0.8f, 1.4f) + 0.1f * k);
            }
        }

        // Looping smoke column (rate set by the owner through emission.rateOverTime).
        public ParticleSystem CreateSmoke(Transform parent, Vector3 localPos)
        {
            var ps = MakeSystem("Smoke", 60, -0.05f, Fade(0.65f, 0f), Curve(0.5f, 1.8f), false, parent);
            ps.transform.localPosition = localPos;
            ConfigureLoop(ps, 1.8f, 2.8f, 0.8f, 1.5f, 0.6f, 1.1f, Color.Lerp(Palette.SmokeGrey, Palette.SmokeDark, 0.5f), 12f);
            return ps;
        }

        public ParticleSystem CreateFire(Transform parent, Vector3 localPos)
        {
            var ps = MakeSystem("Fire", 50, -0.1f, FireGradient(), Curve(1f, 0.3f), false, parent);
            ps.transform.localPosition = localPos;
            ConfigureLoop(ps, 0.4f, 0.8f, 1.2f, 2f, 0.35f, 0.7f, Color.white, 20f);
            return ps;
        }

        // ------------------------------------------------------------------ construction

        static void ConfigureLoop(ParticleSystem ps, float lifeMin, float lifeMax, float speedMin, float speedMax,
            float sizeMin, float sizeMax, Color color, float coneAngle)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.25f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }

        ParticleSystem MakeSystem(string name, int max, float gravity, Gradient color, AnimationCurve size,
            bool stretch = false, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = 1f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(color);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, size);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _soft;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.05f;
                r.lengthScale = 1.5f;
            }
            return ps;
        }

        Light MakeLight(string name, Color color, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        static AnimationCurve Curve(float from, float to) => AnimationCurve.Linear(0f, from, 1f, to);

        // White with an alpha fade (the start color supplies the tint).
        static Gradient Fade(float a0, float a1)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, 1f) });
            return g;
        }

        static Gradient SparkGradient()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Palette.FireOrange, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static Gradient FireGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(Palette.SparkYellow, 0f),
                    new GradientColorKey(Palette.FireOrange, 0.35f),
                    new GradientColorKey(Palette.SmokeDark, 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static Texture2D MakeSoftTexture()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "SoftCircle", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f;
                    float dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
