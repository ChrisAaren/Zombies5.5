using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Zombies55
{
    // The explosion director. Listens to Blast.Detonated and stages the blast in pooled pieces, none of which allocate while playing:
    //   white-hot flash (light spike + exposure punch) -> rolling HDR fireballs (flipbook) -> shockwave ring and dust ring ->
    //   sparks, embers and debris bits (world collision) -> dark smoke column (flipbook, 6-10 s) -> scorch mark on the ground.
    // Wrecks burn through BurnSlots (fire-loop flipbook plus smoke). Everything is sized by the blast radius and counted by EffectsDetail.
    public class ExplosionFX : MonoBehaviour
    {
        public static ExplosionFX Instance { get; private set; }

        [SerializeField] ExplosionAssets assets;

        // ------------------------------------------------------------------ pending staged emits

        enum Stage : byte { Fireball, Smoke, Dust }

        struct Pending
        {
            public float Time;
            public Stage Kind;
            public Vector3 Pos, Vel;
            public float Size, Life, Rot, Spin;
            public bool Live;
        }

        const int PendingMax = 160;
        Pending[] _pending;
        int _pendingCount;

        // ------------------------------------------------------------------ pooled systems

        Transform _pool;
        ParticleSystem _fireball, _smoke, _dust, _ring, _flash, _sparks, _embers, _bits;
        ParticleSystem[] _all;
        Light[] _lights;
        float[] _lightStart, _lightDur, _lightPeak;
        bool[] _lightGlow;

        Volume _punchVolume;
        float _punchStart = -10f, _punchAmp;

        CinemachineImpulseSource _impulse;

        MeshRenderer[] _scorch;
        int _scorchNext;
        Mesh _scorchMesh;

        public sealed class BurnSlot
        {
            internal Transform Root;
            internal ParticleSystem Fire, Smoke;
            public Transform Follow;
            public bool Active;
            internal ParticleSystem Arch, Glow;
            internal Vector3 Local;
            internal float End, Start, Rate = 1f;
        }
        BurnSlot[] _burns;

        RoundManager _rounds;
        Camera _cam;
        static readonly List<ParticleSystemVertexStream> s_Streams = new List<ParticleSystemVertexStream>();

        // ------------------------------------------------------------------ lifecycle

        void Awake() => EnsureInit();
        void OnEnable() { EnsureInit(); Blast.Detonated += OnDetonated; }
        void OnDisable() { Blast.Detonated -= OnDetonated; }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_rounds != null) _rounds.RoundStarted -= OnRoundStarted;
        }

        void EnsureInit()
        {
            Instance = this;
            if (_pool != null && _fireball != null) return;
            if (assets == null) { Debug.LogError("ExplosionFX: assets are not assigned. Rebuild the scene."); return; }
            var old = transform.Find("ExplosionFXPool");
            if (old != null) Destroy(old.gameObject);
            _pool = new GameObject("ExplosionFXPool").transform;
            _pool.SetParent(transform, false);
            _pending = new Pending[PendingMax];
            _pendingCount = 0;

            s_Streams.Clear();
            s_Streams.Add(ParticleSystemVertexStream.Position);
            s_Streams.Add(ParticleSystemVertexStream.Color);
            s_Streams.Add(ParticleSystemVertexStream.UV);
            s_Streams.Add(ParticleSystemVertexStream.Custom1X);

            // Rolling fireball: white-hot at birth (heat stream), orange, then dark as it cools and hands over to the smoke.
            _fireball = Make("Fireball", assets.fireball, 56, 8, 8, -0.04f, FireballColor(), SizeCurve(0.55f, 1f, 1.25f), true, 1f);
            Heat(_fireball, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.2f, 0.55f), new Keyframe(0.45f, 0.05f), new Keyframe(1f, 0f)));
            Limit(_fireball, 14f, 0.12f);
            Pivot(_fireball, 0.42f);

            _smoke = Make("Smoke", assets.smoke, 128, 8, 8, -0.025f, SmokeColor(), SizeCurve(0.55f, 1f, 1.3f), true, 4f);
            Heat(_smoke, AnimationCurve.Constant(0f, 1f, 0f));
            {
                var tsa = _smoke.textureSheetAnimation;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 0.8f));
                tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.2f);
                _smoke.GetComponent<ParticleSystemRenderer>().flip = new Vector3(0.5f, 0f, 0f);
            }
            Limit(_smoke, 3f, 0.12f);
            Pivot(_smoke, 0.1f);

            _dust = Make("DustRing", assets.dust, 48, 8, 8, 0f, DustColor(), SizeCurve(0.5f, 1f, 1.4f), true, 3f);
            Heat(_dust, AnimationCurve.Constant(0f, 1f, 0f));
            Limit(_dust, 16f, 0.2f);
            Pivot(_dust, 0.1f);

            _ring = Make("Shockwave", assets.ring, 8, 1, 1, 0f, RingColor(), new AnimationCurve(new Keyframe(0f, 0.06f), new Keyframe(0.2f, 0.55f), new Keyframe(1f, 1f)), false, 2f);
            _ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            _flash = Make("Flash", assets.flash, 8, 1, 1, 0f, FlashColor(), SizeCurve(0.7f, 1f, 1.3f), false, 0f);

            _sparks = Make("Sparks", assets.spark, 160, 1, 1, 1.1f, SparkColor(), SizeCurve(1f, 1f, 0.7f), false, 0f);
            {
                var r = _sparks.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 2.2f;
                r.velocityScale = 0.045f;
            }

            _embers = Make("Embers", assets.ember, 120, 1, 1, -0.06f, EmberColor(), SizeCurve(1f, 1f, 0.4f), false, 0f);
            Collide(_embers, 0.3f, 0.5f, 0.35f);

            _bits = Make("DebrisBits", assets.bits, 100, 4, 2, 1.0f, BitsColor(), SizeCurve(1f, 1f, 0.9f), false, 0f);
            {
                var tsa = _bits.textureSheetAnimation;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
            }
            Collide(_bits, 0.35f, 0.45f, 0.4f);
            _all = new[] { _fireball, _smoke, _dust, _ring, _flash, _sparks, _embers, _bits };

            // lights: no shadows, enabled only while one is playing
            int nl = 3;
            _lights = new Light[nl];
            _lightStart = new float[nl]; _lightDur = new float[nl]; _lightPeak = new float[nl]; _lightGlow = new bool[nl];
            for (int i = 0; i < nl; i++)
            {
                var go = new GameObject("BlastLight" + i);
                go.transform.SetParent(_pool, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.enabled = false;
                _lights[i] = l;
            }

            BuildPunchVolume();

            _impulse = gameObject.GetComponent<CinemachineImpulseSource>();
            if (_impulse == null) _impulse = gameObject.AddComponent<CinemachineImpulseSource>();
            ConfigureImpulse();

            BuildScorch();
            BuildBurnSlots();
        }

        // ------------------------------------------------------------------ construction

        ParticleSystem Make(string name, Material mat, int max, int tilesX, int tilesY, float gravity, Gradient color, AnimationCurve size, bool heatStream, float fudge)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_pool, false);
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
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
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
            if (tilesX * tilesY > 1)
            {
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = tilesX;
                tsa.numTilesY = tilesY;
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
                tsa.cycleCount = 1;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.1f);
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.allowOcclusionWhenDynamic = false;
            r.sortingFudge = fudge;
            if (heatStream) r.SetActiveVertexStreams(s_Streams);
            return ps;
        }

        static void Heat(ParticleSystem ps, AnimationCurve c)
        {
            var cd = ps.customData;
            cd.enabled = true;
            cd.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            cd.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 1);
            cd.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(1f, c));
        }

        // Speed is clamped by a curve that closes from max to a crawl: fast burst, then a heavy slow roll.
        static void Limit(ParticleSystem ps, float max, float end)
        {
            var lv = ps.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.separateAxes = false;
            lv.limit = new ParticleSystem.MinMaxCurve(max, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.25f, 0.45f), new Keyframe(1f, end)));
            lv.dampen = 0.2f;
        }

        // Shifts the quad so the emit position sits near the foot of the flipbook cell (the fire ground plane is ~7% above the bottom).
        static void Pivot(ParticleSystem ps, float up)
        {
            ps.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, up, 0f);
        }

        static void Collide(ParticleSystem ps, float bounce, float dampen, float lifetimeLoss)
        {
            var c = ps.collision;
            c.enabled = true;
            c.type = ParticleSystemCollisionType.World;
            c.mode = ParticleSystemCollisionMode.Collision3D;
            c.quality = ParticleSystemCollisionQuality.Low;
            c.collidesWith = 1 << GameLayers.Default;
            c.enableDynamicColliders = false;
            c.bounce = bounce;
            c.dampen = dampen;
            c.lifetimeLoss = lifetimeLoss * 0.2f;
            c.minKillSpeed = 0.05f;
            c.radiusScale = 0.5f;
            c.sendCollisionMessages = false;
            c.maxCollisionShapes = 128;
        }

        static AnimationCurve SizeCurve(float a, float b, float c) =>
            new AnimationCurve(new Keyframe(0f, a), new Keyframe(0.3f, b), new Keyframe(1f, c));

        static Gradient Grad(Color[] cols, float[] ct, float[] alphas, float[] at)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[cols.Length];
            for (int i = 0; i < cols.Length; i++) ck[i] = new GradientColorKey(cols[i], ct[i]);
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i], at[i]);
            g.SetKeys(ck, ak);
            return g;
        }

        static Gradient FireballColor() => Grad(
            new[] { new Color(1f, 0.97f, 0.88f), new Color(1f, 0.78f, 0.5f), new Color(0.3f, 0.26f, 0.24f), new Color(0.12f, 0.11f, 0.1f) },
            new[] { 0f, 0.2f, 0.5f, 0.85f }, new[] { 1f, 1f, 0f }, new[] { 0f, 0.4f, 0.72f });

        // Warm-lit from the fire at birth, then black, then lifting toward the scene's warm haze as it thins out.
        static Gradient SmokeColor() => Grad(
            new[] { new Color(0.55f, 0.40f, 0.30f), new Color(0.42f, 0.34f, 0.29f), new Color(0.33f, 0.30f, 0.27f), new Color(0.35f, 0.32f, 0.29f), new Color(0.62f, 0.57f, 0.5f) },
            new[] { 0f, 0.07f, 0.24f, 0.6f, 1f }, new[] { 0f, 0.9f, 1f, 0f }, new[] { 0f, 0.07f, 0.7f, 1f });

        static Gradient DustColor() => Grad(
            new[] { new Color(0.66f, 0.6f, 0.52f), new Color(0.58f, 0.54f, 0.48f) },
            new[] { 0f, 1f }, new[] { 0.85f, 0.5f, 0f }, new[] { 0f, 0.35f, 1f });

        static Gradient RingColor() => Grad(
            new[] { new Color(1f, 0.93f, 0.82f), new Color(0.8f, 0.75f, 0.68f) },
            new[] { 0f, 1f }, new[] { 0.9f, 0.5f, 0f }, new[] { 0f, 0.3f, 1f });

        static Gradient FlashColor() => Grad(
            new[] { new Color(1f, 0.96f, 0.85f), new Color(1f, 0.7f, 0.35f) },
            new[] { 0f, 1f }, new[] { 1f, 0.6f, 0f }, new[] { 0f, 0.3f, 1f });

        static Gradient SparkColor() => Grad(
            new[] { new Color(1f, 0.95f, 0.75f), new Color(1f, 0.55f, 0.18f), new Color(0.6f, 0.15f, 0.05f) },
            new[] { 0f, 0.4f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, 0.6f, 1f });

        static Gradient EmberColor() => Grad(
            new[] { new Color(1f, 0.7f, 0.3f), new Color(0.9f, 0.3f, 0.08f) },
            new[] { 0f, 1f }, new[] { 0f, 1f, 1f, 0f }, new[] { 0f, 0.1f, 0.6f, 1f });

        static Gradient BitsColor() => Grad(
            new[] { new Color(0.5f, 0.46f, 0.42f), new Color(0.28f, 0.26f, 0.24f) },
            new[] { 0f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, 0.7f, 1f });

        void ConfigureImpulse()
        {
            // A shaking waveform that decays: random-looking swings on a falling envelope, so the camera jolts then settles.
            var rng = new System.Random(4242);
            var keys = new Keyframe[28];
            for (int i = 0; i < keys.Length; i++)
            {
                float t = i / (float)(keys.Length - 1);
                float env = Mathf.Pow(1f - t, 1.6f);
                float v = (i == 0 ? 0f : (i % 2 == 0 ? 1f : -1f) * (0.55f + 0.45f * (float)rng.NextDouble())) * env;
                keys[i] = new Keyframe(t, v);
            }
            var def = _impulse.ImpulseDefinition;
            def.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Custom;
            def.CustomImpulseShape = new AnimationCurve(keys);
            def.ImpulseDuration = 1f;
            _impulse.ImpulseDefinition = def;
        }

        void BuildPunchVolume()
        {
            var go = new GameObject("BlastExposurePunch");
            go.transform.SetParent(_pool, false);
            _punchVolume = go.AddComponent<Volume>();
            _punchVolume.isGlobal = true;
            _punchVolume.priority = 80f;
            _punchVolume.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var exposure = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
            exposure.postExposure.Override(1.6f);
            exposure.contrast.Override(8f);
            var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
            bloom.intensity.Override(1.1f);
            bloom.threshold.Override(0.8f);
            _punchVolume.sharedProfile = profile;
            go.SetActive(false);
        }

        void BuildScorch()
        {
            _scorchMesh = new Mesh { name = "ScorchQuad" };
            _scorchMesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            _scorchMesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _scorchMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            _scorchMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _scorchMesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 0.1f, 1f));
            int n = 40;
            _scorch = new MeshRenderer[n];
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Scorch" + i);
                go.transform.SetParent(_pool, false);
                go.AddComponent<MeshFilter>().sharedMesh = _scorchMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = assets.scorch;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                go.SetActive(false);
                _scorch[i] = mr;
            }
        }

        void BuildBurnSlots()
        {
            _burns = new BurnSlot[6];
            for (int i = 0; i < _burns.Length; i++)
            {
                var b = new BurnSlot();
                var root = new GameObject("Burn" + i).transform;
                root.SetParent(_pool, false);
                b.Root = root;
                b.Fire = MakeBurnFire(root);
                b.Smoke = MakeBurnSmoke(root);
                b.Arch = MakeBurnArch(root);
                b.Glow = MakeBurnGlow(root);
                _burns[i] = b;
            }
        }

        // The body of a car fire: a few big, overlapping flames over the car's footprint (cabin and engine bay), randomly flipped and offset
        // in the flipbook. They taper (size, then colour to a deep orange and out) as they break into the smoke above.
        ParticleSystem MakeBurnFire(Transform parent)
        {
            var ps = Make("Fire", assets.fireLoop, 44, 8, 8, -0.06f,
                Grad(new[] { new Color(1f, 0.95f, 0.8f), new Color(1f, 0.75f, 0.45f), new Color(0.7f, 0.32f, 0.12f) }, new[] { 0f, 0.4f, 1f },
                    new[] { 0f, 1f, 0.8f, 0f }, new[] { 0f, 0.1f, 0.55f, 1f }),
                new AnimationCurve(new Keyframe(0f, 0.85f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.45f)), false, 0.5f);
            ps.transform.SetParent(parent, false);
            ps.GetComponent<ParticleSystemRenderer>().flip = new Vector3(0.5f, 0f, 0f);
            Pivot(ps, 0.3f);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(3.6f, 5.8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            var tsa = ps.textureSheetAnimation;
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.3f, 2.8f, 0.1f);   // after the -90 turn: width x length of the footprint; set per car in StartBurn
            shape.rotation = new Vector3(-90f, 0f, 0f);
            return ps;
        }

        // Small licking flames along the footprint's edge: the wheel arches and sills.
        ParticleSystem MakeBurnArch(Transform parent)
        {
            var ps = Make("ArchFlames", assets.fireLoop, 40, 8, 8, -0.04f,
                Grad(new[] { new Color(1f, 0.85f, 0.6f), new Color(0.8f, 0.4f, 0.15f) }, new[] { 0f, 1f }, new[] { 0f, 1f, 0.8f, 0f }, new[] { 0f, 0.15f, 0.6f, 1f }),
                new AnimationCurve(new Keyframe(0f, 0.9f), new Keyframe(1f, 0.5f)), false, 0.6f);
            ps.transform.SetParent(parent, false);
            ps.GetComponent<ParticleSystemRenderer>().flip = new Vector3(0.5f, 0f, 0f);
            Pivot(ps, 0.3f);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.3f, 2.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var tsa = ps.textureSheetAnimation;
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.BoxEdge;
            shape.scale = new Vector3(1.8f, 4f, 0.1f);
            shape.rotation = new Vector3(-90f, 0f, 0f);
            return ps;
        }

        // A warm pool of light on the street under the wreck.
        ParticleSystem MakeBurnGlow(Transform parent)
        {
            var ps = Make("BaseGlow", assets.ember, 8, 1, 1, 0f,
                Grad(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 0f, 0.2f, 0.2f, 0f }, new[] { 0f, 0.25f, 0.7f, 1f }),
                new AnimationCurve(new Keyframe(0f, 0.9f), new Keyframe(1f, 1.1f)), false, 6f);
            ps.transform.SetParent(parent, false);
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(6f, 8.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            return ps;
        }

        ParticleSystem MakeBurnSmoke(Transform parent)
        {
            var ps = Make("BurnSmoke", assets.smoke, 40, 8, 8, -0.02f, SmokeColor(), SizeCurve(0.6f, 1f, 1.5f), true, 4f);
            ps.transform.SetParent(parent, false);
            Heat(ps, AnimationCurve.Constant(0f, 1f, 0f));
            Pivot(ps, 0.1f);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(3.4f, 5.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.8f, 0.8f), Color.white);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.5f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Limit(ps, 4f, 0.25f);
            return ps;
        }

        // ------------------------------------------------------------------ update

        // The first blast of a session must not hitch: draw one invisible particle per system (and a scorch quad) a couple of frames after
        // the scene starts, so the shader pipelines are built while the player is still on the title screen.
        System.Collections.IEnumerator Start()
        {
            yield return null;
            yield return null;
            if (_pool == null) yield break;
            var cam = Camera.main;
            if (cam == null) yield break;
            Vector3 p = cam.transform.position + cam.transform.forward * 5f;
            var ep = new ParticleSystem.EmitParams { position = p, startLifetime = 0.2f, startSize = 0.05f, startColor = new Color(1f, 1f, 1f, 0.004f) };
            foreach (var ps in _all) ps.Emit(ep, 1);
            foreach (var b in _burns) { b.Fire.Emit(ep, 1); b.Smoke.Emit(ep, 1); b.Arch.Emit(ep, 1); b.Glow.Emit(ep, 1); }
            var mr = _scorch[0];
            mr.transform.SetPositionAndRotation(p, Quaternion.identity);
            mr.transform.localScale = new Vector3(0.05f, 1f, 0.05f);
            mr.gameObject.SetActive(true);
            yield return null;
            yield return null;
            mr.gameObject.SetActive(false);
        }

        void Update()
        {
            if (_pool == null) { EnsureInit(); if (_pool == null) return; }
            HookRounds();
            float now = Time.time;
            FlushPending(now);
            UpdateLights(now);
            UpdatePunch(now);
        }

        void LateUpdate()
        {
            if (_burns == null) return;
            float now = Time.time;
            for (int i = 0; i < _burns.Length; i++)
            {
                var b = _burns[i];
                if (!b.Active) continue;
                if (b.Follow != null) b.Root.SetPositionAndRotation(b.Follow.TransformPoint(b.Local), Quaternion.Euler(0f, b.Follow.eulerAngles.y, 0f));
                float remain = b.End - now;
                float k = Mathf.Clamp01((now - b.Start) / 0.8f) * Mathf.Clamp01(remain / 2.5f);
                var fe = b.Fire.emission; fe.rateOverTime = 5f * b.Rate * k;
                var ae = b.Arch.emission; ae.rateOverTime = 7f * b.Rate * k;
                var ge = b.Glow.emission; ge.rateOverTime = 1.6f * Mathf.Clamp01(k * 1.2f);
                var se = b.Smoke.emission; se.rateOverTime = 1.5f * b.Rate * Mathf.Clamp01(remain / 1.5f);
                if (remain <= 0f)
                {
                    b.Active = false;
                    b.Fire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    b.Smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    b.Arch.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    b.Glow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        void HookRounds()
        {
            if (_rounds != null || RoundManager.Instance == null) return;
            _rounds = RoundManager.Instance;
            _rounds.RoundStarted += OnRoundStarted;
        }

        void OnRoundStarted(int round) => ResetAll();

        // Clears every blast artefact: marks, flying parts, fires, smoke still in the air.
        public void ResetAll()
        {
            if (_pool == null) return;
            _pendingCount = 0;
            for (int i = 0; i < _pending.Length; i++) _pending[i].Live = false;
            foreach (var ps in _all) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            for (int i = 0; i < _scorch.Length; i++) _scorch[i].gameObject.SetActive(false);
            _scorchNext = 0;
            foreach (var l in _lights) l.enabled = false;
            _punchAmp = 0f;
            _punchVolume.weight = 0f;
            _punchVolume.gameObject.SetActive(false);
            foreach (var b in _burns)
            {
                b.Active = false;
                b.Fire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                b.Smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                b.Arch.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                b.Glow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            DebrisPool.Instance?.ResetAll();
        }

        // ------------------------------------------------------------------ burn slots (wreck fire)

        // Starts a burning loop that follows a transform for duration seconds. Returns null when no slot exists.
        public BurnSlot StartBurn(Transform follow, Vector3 localPoint, float duration, float scale = 1f, Vector3 footprint = default)
        {
            if (_burns == null) return null;
            int cap = Mathf.Min(_burns.Length, EffectsDetail.MaxBurningWrecks);
            BurnSlot pick = null, soonest = null;
            int active = 0;
            for (int i = 0; i < _burns.Length; i++)
            {
                var b = _burns[i];
                if (b.Active) { active++; if (soonest == null || b.End < soonest.End) soonest = b; }
                else if (pick == null) pick = b;
            }
            if (active >= cap || pick == null) pick = soonest;   // steal the one about to go out
            if (pick == null) return null;
            pick.Fire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            pick.Smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            pick.Arch.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            pick.Glow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (footprint.x > 0.1f)
            {
                // footprint = (width, height, length) of the car's box: flames fill most of the body, the edge flames hug its sides
                var fs = pick.Fire.shape; fs.scale = new Vector3(footprint.x * 0.65f, footprint.z * 0.7f, 0.1f);
                var ash = pick.Arch.shape; ash.scale = new Vector3(footprint.x * 0.95f, footprint.z * 0.85f, 0.1f);
                var gs = pick.Glow.main; gs.startSize = new ParticleSystem.MinMaxCurve(footprint.z * 1.2f, footprint.z * 1.7f);
            }
            pick.Follow = follow;
            pick.Local = localPoint;
            pick.Start = Time.time;
            pick.End = Time.time + duration * Mathf.Lerp(1f, EffectsDetail.SmokeLifetimeScale, 0.5f);
            pick.Rate = EffectsDetail.ParticleScale * scale;
            pick.Active = true;
            pick.Root.SetPositionAndRotation(follow.TransformPoint(localPoint), Quaternion.Euler(0f, follow.eulerAngles.y, 0f));
            var sm = pick.Smoke.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(5f * EffectsDetail.SmokeLifetimeScale, 8f * EffectsDetail.SmokeLifetimeScale);
            pick.Fire.Play();
            pick.Smoke.Play();
            pick.Arch.Play();
            pick.Glow.Play();
            return pick;
        }

        // Keeps a running burn going for another duration seconds from now.
        public void Extend(BurnSlot slot, float duration)
        {
            if (slot != null && slot.Active) slot.End = Time.time + duration * Mathf.Lerp(1f, EffectsDetail.SmokeLifetimeScale, 0.5f);
        }

        public ExplosionAssets Assets => assets;

        public void StopBurn(BurnSlot slot)
        {
            if (slot == null || !slot.Active) return;
            slot.End = Time.time + 1f;
        }

        // ------------------------------------------------------------------ the blast

        void OnDetonated(BlastInfo b)
        {
            if (_pool == null) return;
            Play(b);
            DebrisPool.Instance?.OnBlast(b);
            ExplosiveProp.RockNearby(b);
        }

        void Play(BlastInfo b)
        {
            float now = Time.time;
            float s = Mathf.Clamp(b.Radius / 8f, 0.5f, 1.3f);
            float pc = EffectsDetail.ParticleScale;
            if (_cam == null) _cam = Camera.main;
            float camDist = _cam != null ? Vector3.Distance(_cam.transform.position, b.Position) : 30f;

            // The ground under the blast: scorch, rings and the foot of the fireballs sit on it.
            Vector3 ground = b.Position;
            Vector3 normal = Vector3.up;
            if (Physics.Raycast(b.Position + Vector3.up * 0.5f, Vector3.down, out var hit, 4f, 1 << GameLayers.Default, QueryTriggerInteraction.Ignore))
            {
                ground = hit.point;
                normal = hit.normal;
            }
            ground.y += 0.02f;
            Vector3 center = b.Position;
            if (center.y < ground.y + 0.3f) center.y = ground.y + 0.3f;

            // 1. white-hot flash: one hot sprite for ~0.1 s plus the light spike
            var fp = new ParticleSystem.EmitParams
            {
                position = center + Vector3.up * 0.8f * s,
                startLifetime = 0.11f,
                startSize = 13f * s,
                startColor = Color.white,
                rotation = Random.value * 6.28f,
            };
            _flash.Emit(fp, 1);
            LightBlast(center, b.Radius, s);

            // 2. fireballs
            int fireballs = Mathf.Max(2, Mathf.RoundToInt(6f * pc * Mathf.Lerp(0.7f, 1f, s)));
            for (int i = 0; i < fireballs; i++)
            {
                bool main = i == 0;
                Vector2 o = main ? Vector2.zero : Random.insideUnitCircle * 1.7f * s;
                Queue(new Pending
                {
                    Time = main ? now : now + Random.Range(0.03f, 0.38f),
                    Kind = Stage.Fireball,
                    Pos = ground + new Vector3(o.x, main ? 0.1f : Random.Range(0.1f, 1.6f) * s, o.y),
                    Vel = new Vector3(o.x * 0.8f, Random.Range(2.5f, 6f) * s, o.y * 0.8f),
                    Size = main ? 15f * s : Random.Range(8.5f, 12.5f) * s,
                    Life = main ? 1.9f : Random.Range(1.4f, 2.1f),
                    Rot = Random.Range(-0.4f, 0.4f),
                    Spin = Random.Range(-0.25f, 0.25f),
                    Live = true,
                });
            }

            // 3. shockwave ring (ground-hugging) and dust ring
            var rp = new ParticleSystem.EmitParams
            {
                position = ground + Vector3.up * 0.06f,
                startLifetime = 0.5f,
                startSize = b.Radius * 2.5f,
                startColor = Color.white,
                rotation = Random.value * 6.28f,
            };
            _ring.Emit(rp, 1);
            int dust = Mathf.RoundToInt(14f * pc * s);
            for (int i = 0; i < dust; i++)
            {
                float a = i / (float)Mathf.Max(1, dust) * 6.2832f + Random.Range(-0.25f, 0.25f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Queue(new Pending
                {
                    Time = now + Random.Range(0f, 0.08f),
                    Kind = Stage.Dust,
                    Pos = ground + dir * 0.6f * s,
                    Vel = dir * Random.Range(9f, 15f) * s + Vector3.up * Random.Range(0.4f, 1.2f),
                    Size = Random.Range(6f, 10f) * s,
                    Life = Random.Range(1.6f, 2.6f),
                    Rot = Random.value * 6.28f,
                    Spin = Random.Range(-0.5f, 0.5f),
                    Live = true,
                });
            }

            // 4. sparks (streaks), embers, flying bits
            int sparks = Mathf.RoundToInt(44f * pc * s);
            for (int i = 0; i < sparks; i++)
            {
                Vector3 d = (Random.onUnitSphere + Vector3.up * 0.5f).normalized;
                if (d.y < 0.05f) d.y = 0.05f + Random.value * 0.3f;
                var ep = new ParticleSystem.EmitParams
                {
                    position = center + Random.insideUnitSphere * 0.5f * s,
                    velocity = d * Random.Range(9f, 28f) * Mathf.Lerp(0.8f, 1f, s),
                    startLifetime = Random.Range(0.45f, 1.5f),
                    startSize = Random.Range(0.1f, 0.2f),
                    startColor = Color.white,
                };
                _sparks.Emit(ep, 1);
            }
            int embers = Mathf.RoundToInt(26f * pc * s);
            for (int i = 0; i < embers; i++)
            {
                Vector3 d = (Random.onUnitSphere + Vector3.up * 1.3f).normalized;
                var ep = new ParticleSystem.EmitParams
                {
                    position = center + Random.insideUnitSphere * 1f * s,
                    velocity = d * Random.Range(2f, 10f),
                    startLifetime = Random.Range(1.6f, 3.8f),
                    startSize = Random.Range(0.08f, 0.18f),
                    startColor = Color.white,
                };
                _embers.Emit(ep, 1);
            }
            int bits = Mathf.RoundToInt(22f * pc * s);
            for (int i = 0; i < bits; i++)
            {
                Vector3 d = (Random.onUnitSphere + Vector3.up * 0.8f).normalized;
                if (d.y < 0.1f) d.y = 0.1f + Random.value * 0.4f;
                var ep = new ParticleSystem.EmitParams
                {
                    position = center + Random.insideUnitSphere * 0.6f * s,
                    velocity = d * Random.Range(6f, 18f),
                    startLifetime = Random.Range(2f, 3.6f),
                    startSize = Random.Range(0.14f, 0.45f),
                    startColor = Color.white,
                    rotation = Random.value * 6.28f,
                    angularVelocity = Random.Range(-6f, 6f),
                };
                _bits.Emit(ep, 1);
            }

            // 5. the smoke column: dense, dark and slow, lingering 6-10 s (scaled by detail)
            int puffs = Mathf.Max(4, Mathf.RoundToInt(28f * pc * Mathf.Lerp(0.6f, 1f, s)));
            float lifeScale = EffectsDetail.SmokeLifetimeScale;
            for (int i = 0; i < puffs; i++)
            {
                // spread across the fireball volume, not one point; sizes vary a lot so the silhouette is lumpy
                Vector3 off = Random.insideUnitSphere;
                off = new Vector3(off.x * 2.6f, Mathf.Abs(off.y) * 4.2f + 0.4f, off.z * 2.6f) * s;
                float k = Mathf.Clamp01(off.y / (4.6f * s));
                bool big = Random.value < 0.22f;
                float g = Random.Range(0.7f, 1f);
                Queue(new Pending
                {
                    Time = now + Random.Range(0.04f, 1.25f),
                    Kind = Stage.Smoke,
                    Pos = ground + off,
                    Vel = new Vector3(off.x * 0.55f, Random.Range(1.2f, 3.4f) * s * (0.7f + k * 0.6f), off.z * 0.55f),
                    Size = (big ? Random.Range(12f, 17f) : Random.Range(5f, 11f)) * s,
                    Life = Random.Range(6f, 10f) * lifeScale,
                    Rot = Random.value * 6.28f,
                    Spin = Random.Range(-0.2f, 0.2f),
                    Live = true,
                });
            }

            // 6. scorch mark that stays until the round resets
            PlaceScorch(ground, normal, b.Kind == BlastKind.Car ? 7.5f : b.Kind == BlastKind.Barrel ? 4.6f : 3.6f);

            // 7. camera: exposure punch, shake and wobble, all falling off with distance
            float closeness = Mathf.Clamp01(1f - camDist / (b.Radius * 3.2f));
            if (EffectsDetail.ExposurePunch && closeness > 0.02f)
            {
                float amp = Mathf.Clamp01(0.25f + closeness * 0.75f) * 0.75f * Mathf.Clamp(s, 0.7f, 1.1f);
                if (amp > _punchAmp * Mathf.Max(0f, 1f - (now - _punchStart) / 0.4f)) { _punchAmp = amp; _punchStart = now; }
            }
            float shakeK = Mathf.Clamp01(1f - camDist / (b.Radius * 7f));
            if (shakeK > 0.02f && _impulse != null)
            {
                var def = _impulse.ImpulseDefinition;
                def.ImpulseDuration = Mathf.Lerp(0.3f, 1.7f, shakeK) * Mathf.Clamp(s, 0.7f, 1.2f);
                _impulse.ImpulseDefinition = def;
                float force = (0.12f + 1.15f * shakeK * shakeK) * Mathf.Clamp(s, 0.7f, 1.25f);
                _impulse.GenerateImpulse(new Vector3(Random.Range(-0.5f, 0.5f), 1f, Random.Range(-0.5f, 0.5f)) * force);
            }
        }

        void Queue(Pending p)
        {
            if (p.Time <= Time.time + 0.0005f) { Emit(p); return; }
            for (int i = 0; i < _pending.Length; i++)
            {
                if (_pending[i].Live) continue;
                _pending[i] = p;
                _pendingCount++;
                return;
            }
            Emit(p);   // queue full (chain reaction): emit at once rather than lose it
        }

        void FlushPending(float now)
        {
            if (_pendingCount <= 0) return;
            for (int i = 0; i < _pending.Length; i++)
            {
                if (!_pending[i].Live || _pending[i].Time > now) continue;
                Emit(_pending[i]);
                _pending[i].Live = false;
                _pendingCount--;
            }
        }

        void Emit(Pending p)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = p.Pos,
                velocity = p.Vel,
                startLifetime = p.Life,
                startSize = p.Size,
                startColor = Color.white,
                rotation = p.Rot,
                angularVelocity = p.Spin,
            };
            switch (p.Kind)
            {
                case Stage.Fireball: _fireball.Emit(ep, 1); break;
                case Stage.Smoke:
                    float g = Random.Range(0.7f, 1.05f);
                    ep.startColor = new Color(g, g * 0.97f, g * 0.93f, 1f);
                    _smoke.Emit(ep, 1);
                    break;
                case Stage.Dust: _dust.Emit(ep, 1); break;
            }
        }

        // ------------------------------------------------------------------ lights and exposure

        void LightBlast(Vector3 center, float radius, float s)
        {
            int cap = Mathf.Min(_lights.Length, EffectsDetail.MaxLights);
            if (cap <= 0) return;
            StartLight(cap, center + Vector3.up * 1.2f, new Color(1f, 0.93f, 0.78f), radius * 3.2f, 150f * s, cap >= 2 ? 0.16f : 0.4f, false);
            if (cap >= 2)
                StartLight(cap, center + Vector3.up * 2.8f * s, new Color(1f, 0.52f, 0.18f), radius * 3.4f, 55f * s, 1.5f, true);
        }

        void StartLight(int cap, Vector3 pos, Color color, float range, float peak, float dur, bool glow)
        {
            int idx = -1;
            float oldest = float.MaxValue;
            for (int i = 0; i < cap; i++)
            {
                if (!_lights[i].enabled) { idx = i; break; }
                if (_lightStart[i] < oldest) { oldest = _lightStart[i]; idx = i; }
            }
            if (idx < 0) return;
            var l = _lights[idx];
            l.transform.position = pos;
            l.color = color;
            l.range = range;
            l.intensity = 0f;
            l.enabled = true;
            _lightStart[idx] = Time.time;
            _lightDur[idx] = dur;
            _lightPeak[idx] = peak;
            _lightGlow[idx] = glow;
        }

        void UpdateLights(float now)
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                var l = _lights[i];
                if (!l.enabled) continue;
                float k = (now - _lightStart[i]) / _lightDur[i];
                if (k >= 1f) { l.enabled = false; continue; }
                l.intensity = _lightGlow[i]
                    ? _lightPeak[i] * Mathf.Exp(-3.2f * k) * Mathf.Clamp01(k / 0.04f)
                    : _lightPeak[i] * (1f - k) * (1f - k) * (1f - k);
            }
        }

        void UpdatePunch(float now)
        {
            if (_punchAmp <= 0f) return;
            float t = now - _punchStart;
            float w = (t < 0.03f ? t / 0.03f : Mathf.Exp(-(t - 0.03f) * 9f)) * _punchAmp;
            if (t > 0.6f || w < 0.003f) { _punchAmp = 0f; _punchVolume.weight = 0f; _punchVolume.gameObject.SetActive(false); return; }
            if (!_punchVolume.gameObject.activeSelf) _punchVolume.gameObject.SetActive(true);
            _punchVolume.weight = w;
        }

        // ------------------------------------------------------------------ scorch

        void PlaceScorch(Vector3 ground, Vector3 normal, float size)
        {
            int cap = Mathf.Min(_scorch.Length, EffectsDetail.MaxScorchMarks);
            if (cap <= 0) return;
            if (_scorchNext >= cap) _scorchNext = 0;
            var mr = _scorch[_scorchNext++];
            var t = mr.transform;
            t.position = ground + normal * 0.012f;
            t.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.value * 360f, 0f);
            float sz = size * Random.Range(0.9f, 1.1f);
            t.localScale = new Vector3(sz, 1f, sz);
            mr.gameObject.SetActive(true);
        }
    }
}
