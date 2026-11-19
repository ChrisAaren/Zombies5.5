using System;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    /// <summary>
    /// Builds the whole Zombies 5.5 scene from code. Deterministic and safe to re-run.
    /// </summary>
    public static partial class SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Zombies55.unity";
        const string MaterialDir = "Assets/Art/Materials";
        const string VolumePath = "Assets/Settings/Zombies55Volume.asset";

        const float BlockSize = 26f;
        const float RoadWidth = 12f;
        const float SidewalkTop = 0.15f;

        static readonly float[] RoadCenters = { -95f, -57f, -19f, 19f, 57f, 95f };
        static readonly float[] BlockCenters = { -76f, -38f, 0f, 38f, 76f };

        static System.Random _rng;
        static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        static Shader _litShader;
        static int _buildingIndex;

        // Shared materials
        static Material mAsphalt, mRoadLine, mSidewalk, mCurb, mCurbZ, mPlaza, mEye;
        static Material mTerracotta, mSage, mSand, mDustyBlue, mWindow, mTrim;
        static Material mPrimary, mSecondary, mPants, mLeaf, mTrunk, mShadow;
        static Material mLampHead, mParticle, mSky;
        static Material[] _buildingMats;

        // Roots
        static Transform _env, _city, _props, _player, _game, _camera;

        // Results shared between steps
        static Transform _playerRoot, _cameraPivot;
        static PlayerController _playerController;

        [MenuItem("Tools/Zombies 5.5/Build Scene")]
        public static void BuildScene()
        {
            if (TMP_Settings.defaultFontAsset == null)
                throw new InvalidOperationException(
                    "TextMeshPro Essential Resources are missing. Import them (Window > TextMeshPro > Import TMP Essential Resources) before building the scene.");
            if (GraphicsSettings.currentRenderPipeline == null)
                throw new InvalidOperationException("No render pipeline asset is active. Zombies 5.5 needs the URP template.");

            _rng = new System.Random(7);
            _grit = new System.Random(2024);
            _signIndex = 0;
            _quads.Clear();
            _mats.Clear();
            _buildingIndex = 0;

            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Art");
            EnsureFolder(MaterialDir);
            EnsureFolder("Assets/Settings");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            _env = Root("Environment");
            _city = Root("City");
            _props = Root("Props");
            _player = Root("Player");
            _game = Root("Game");
            _camera = Root("Camera");

            ConfigureImports();
            BuildAnimatorControllers();
            BuildMaterials();
            BuildLighting();
            BuildCity();
            BuildProps();
            BuildPlayer();
            BuildCamera();
            BuildGame();
            BuildCombat();
            BuildZones();
            BuildEnemies();

            MarkStatic(_city);
            MarkStatic(_props);
            MarkStatic(_env.Find("Boundary"));
            MarkStatic(_env.Find("Skyline"));

            ShellBuilder.Build();

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            CleanupSlotMaterials();
            Debug.Log("Zombies 5.5 scene built");
        }

        // ------------------------------------------------------------------ materials

        static void BuildMaterials()
        {
            _litShader = GraphicsSettings.currentRenderPipeline.defaultMaterial.shader;
            if (_litShader == null) throw new InvalidOperationException("URP Lit shader not found.");

            // Worn texture sets; the packed _MS maps carry smoothness (and metal), so the float is a multiplier.
            mAsphalt = Surf("Asphalt", Palette.AsphaltWorn, "AsphaltWorn", new Vector2(60f, 60f), 1f);
            mRoadLine = Mat("RoadLine", Palette.RoadLine);
            mSidewalk = Surf("Sidewalk", Palette.SidewalkWorn, "ConcreteStained", new Vector2(BlockSize / 2f, BlockSize / 2f), 1f);
            mCurb = Surf("Curb", Palette.CurbWorn, "ConcreteStained", new Vector2(BlockSize / 2f, 0.15f), 1f);
            mCurbZ = Surf("CurbZ", Palette.CurbWorn, "ConcreteStained", new Vector2(0.15f, BlockSize / 2f), 1f);
            mPlaza = Surf("Plaza", Palette.Plaza, "ConcreteStained", new Vector2(BlockSize / 2f, BlockSize / 2f), 1f);
            mEye = Mat("Eye", Palette.EyeBlack);
            mTerracotta = Mat("Terracotta", Palette.Terracotta);
            mSage = Mat("Sage", Palette.Sage);
            mSand = Mat("Sand", Palette.Sand);
            mDustyBlue = Mat("DustyBlue", Palette.DustyBlue);
            mWindow = Mat("Window", Palette.Window);
            mTrim = Mat("Trim", Palette.Trim);
            mPrimary = Mat("CharacterPrimary", Palette.CharacterPrimary);
            mSecondary = Mat("CharacterSecondary", Palette.CharacterSecondary);
            mPants = Mat("Pants", Palette.Pants);
            mLeaf = Mat("Leaf", Palette.Leaf);
            mTrunk = Mat("Trunk", Palette.Trunk);
            mShadow = Mat("Shadow", Palette.Shadow);
            mLampHead = Mat("LampHead", Palette.SunKey, 1.2f);
            _buildingMats = new[] { mTerracotta, mSage, mSand, mDustyBlue };

            // Particles
            var sprites = Shader.Find("Sprites/Default");
            if (sprites == null) throw new InvalidOperationException("Shader 'Sprites/Default' not found.");
            mParticle = LoadOrCreateMat("Particle", sprites);
            EditorUtility.SetDirty(mParticle);

            // Sky: our own atmospheric cloud skybox (gradient + haze + sun disc + fbm cumulus); the sun direction is set in BuildLighting.
            var skyShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Shaders/CloudSky.shader");
            if (skyShader == null) skyShader = Shader.Find("Zombies55/CloudSky");
            if (skyShader == null) throw new InvalidOperationException("Shader 'Zombies55/CloudSky' not found.");
            mSky = LoadOrCreateMat("SkyClouds", skyShader);
            mSky.SetColor("_ZenithColor", Palette.SkyZenithGritty);
            mSky.SetColor("_HorizonColor", Palette.SkyHorizonGritty);
            mSky.SetColor("_GroundColor", Palette.GroundAmbient);
            mSky.SetColor("_SunColor", Palette.SunHaze);
            mSky.SetColor("_CloudLit", new Color(1f, 0.94f, 0.84f));
            mSky.SetColor("_CloudShadow", new Color(0.52f, 0.58f, 0.68f));
            mSky.SetFloat("_Exposure", 1.25f);
            mSky.SetFloat("_HazeWidth", 0.3f);
            mSky.SetFloat("_SunSize", 0.022f);
            mSky.SetFloat("_SunIntensity", 8f);
            mSky.SetFloat("_CloudCover", 0.5f);
            mSky.SetFloat("_CloudSoft", 0.3f);
            mSky.SetFloat("_CloudScale", 1.5f);
            EditorUtility.SetDirty(mSky);
        }

        static Material LoadOrCreateMat(string name, Shader shader)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        static float SmoothnessFor(string name)
        {
            switch (name)
            {
                case "Asphalt": return 0.1f;
                case "RoadLine": return 0.2f;
                case "Sidewalk":
                case "Curb":
                case "Plaza": return 0.15f;
                case "Eye": return 0.8f;
                case "Window": return 0.85f;   // glass-like strips
                case "Terracotta":
                case "Sage":
                case "Sand":
                case "DustyBlue":
                case "Trim": return 0.35f;      // painted surfaces
                default: return 0.25f;          // cloth, skin, foliage, wood, props
            }
        }

        static Material Mat(string name, Color color, float emissionMultiplier = 0f)
        {
            if (_mats.TryGetValue(name, out var cached)) return cached;
            var mat = LoadOrCreateMat(name, _litShader);
            mat.SetColor("_BaseColor", color);
            mat.color = color;
            mat.SetFloat("_Smoothness", SmoothnessFor(name));
            mat.SetFloat("_Metallic", 0f);
            if (emissionMultiplier > 0f)
            {
                var emission = color * emissionMultiplier;
                emission.a = 1f;
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(mat);
            _mats[name] = mat;
            return mat;
        }

        // ------------------------------------------------------------------ lighting

        static void BuildLighting()
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(_env, false);
            sunGo.transform.rotation = Quaternion.Euler(24f, 38f, 0f);   // low, warm sun
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Palette.SunHaze;
            sun.intensity = 2.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.88f; // leave some sky fill in cast shadows so shaded streets stay readable
            RenderSettings.sun = sun;
            mSky.SetVector("_SunDir", -sunGo.transform.forward);
            EditorUtility.SetDirty(mSky);

            RenderSettings.skybox = mSky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.75f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0085f;                      // warm-gray distance haze
            RenderSettings.fogColor = Palette.HazeFog;
            DynamicGI.UpdateEnvironment();

            ConfigureUrpAssets();
            BuildReflectionProbe();

            // Post-processing
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
            }
            GetOrAdd<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var bloom = GetOrAdd<Bloom>(profile);                      // restrained: only real highlights (muzzle flash, lamps, gold UI glow)
            bloom.intensity.Override(0.16f);
            bloom.threshold.Override(1.25f);
            bloom.scatter.Override(0.55f);
            var vig = GetOrAdd<Vignette>(profile);
            vig.intensity.Override(0.27f);
            vig.smoothness.Override(0.42f);
            var grade = GetOrAdd<ColorAdjustments>(profile);
            grade.postExposure.Override(0.6f);
            grade.saturation.Override(-20f);                           // desaturated, grounded
            grade.contrast.Override(16f);
            var wb = GetOrAdd<WhiteBalance>(profile);
            wb.temperature.Override(9f);
            wb.tint.Override(0f);
            var split = GetOrAdd<SplitToning>(profile);                // cool shadows, warm highlights
            split.shadows.Override(new Color(0.47f, 0.53f, 0.62f));
            split.highlights.Override(new Color(0.95f, 0.76f, 0.52f));
            split.balance.Override(0f);
            var grain = GetOrAdd<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.14f);
            grain.response.Override(0.8f);
            var smh = GetOrAdd<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(0.95f, 1f, 1.08f, -0.05f));
            EditorUtility.SetDirty(profile);

            var volGo = new GameObject("Global Volume");
            volGo.transform.SetParent(_env, false);
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        // Shadow quality and SSAO live on the URP assets; this reapplies them on every rebuild.
        static void ConfigureUrpAssets()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (urp != null)
            {
                var so = new SerializedObject(urp);
                void SetInt(string n, int v) { var p = so.FindProperty(n); if (p != null) p.intValue = v; }
                void SetFloat(string n, float v) { var p = so.FindProperty(n); if (p != null) p.floatValue = v; }
                SetInt("m_MainLightShadowmapResolution", 4096);
                SetInt("m_ShadowCascadeCount", 4);
                SetInt("m_SoftShadowQuality", 3); // High
                SetFloat("m_ShadowDistance", 120f);
                SetFloat("m_ShadowDepthBias", 0.8f);
                SetFloat("m_ShadowNormalBias", 1.0f);
                so.ApplyModifiedPropertiesWithoutUndo();
                urp.supportsHDR = true;
                EditorUtility.SetDirty(urp);
            }

            // SSAO is an internal URP type: edit the existing feature, or create it by type name once.
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            if (rendererData == null) return;
            ScriptableRendererFeature ssao = null;
            foreach (var f in rendererData.rendererFeatures)
                if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") { ssao = f; break; }
            if (ssao == null)
            {
                var type = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
                if (type != null)
                {
                    ssao = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                    ssao.name = "ScreenSpaceAmbientOcclusion";
                    AssetDatabase.AddObjectToAsset(ssao, rendererData);
                    rendererData.rendererFeatures.Add(ssao);
                    typeof(ScriptableRendererData).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.Invoke(rendererData, null);
                }
            }
            if (ssao != null)
            {
                var sso = new SerializedObject(ssao);
                var settings = sso.FindProperty("m_Settings");
                if (settings != null)
                {
                    settings.FindPropertyRelative("Source").intValue = 1; // DepthNormals
                    settings.FindPropertyRelative("Intensity").floatValue = 1.0f;
                    settings.FindPropertyRelative("Radius").floatValue = 0.5f;
                    settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.32f;
                }
                sso.ApplyModifiedPropertiesWithoutUndo();
                ssao.SetActive(true);
                EditorUtility.SetDirty(ssao);
            }
            EditorUtility.SetDirty(rendererData);
        }

        static void BuildReflectionProbe()
        {
            var go = new GameObject("City Reflection Probe");
            go.transform.SetParent(_env, false);
            go.transform.position = new Vector3(0f, 12f, 0f);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 256;
            probe.boxProjection = false;
            probe.hdr = true;
            probe.importance = 1;
            probe.intensity = 1f;
            probe.size = new Vector3(300f, 80f, 300f);
            probe.center = Vector3.zero;
            probe.nearClipPlane = 0.3f;
            probe.farClipPlane = 400f;
        }

        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T existing)) return existing;
            var added = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(added, profile);
            return added;
        }

        // ------------------------------------------------------------------ city

        static void BuildCity()
        {
            // Ground: one big asphalt box, top at y = 0
            Box("Ground", _city, new Vector3(0f, -0.5f, 0f), new Vector3(280f, 1f, 280f), mAsphalt, true);

            BuildRoadLines();

            int k = 0;
            int[] counts = { 4, 3, 2, 4, 3, 4, 2, 3 };
            for (int i = 0; i < BlockCenters.Length; i++)
            {
                for (int j = 0; j < BlockCenters.Length; j++)
                {
                    float cx = BlockCenters[i];
                    float cz = BlockCenters[j];
                    bool plaza = i == BlockCenters.Length / 2 && j == BlockCenters.Length / 2;
                    BuildBlockBase(cx, cz, plaza);
                    if (!plaza)
                    {
                        BuildBlockBuildings(cx, cz, k, counts[k % counts.Length]);
                        k++;
                    }
                }
            }

            BuildBoundary();
        }

        static void BuildRoadLines()
        {
            var lines = Node("RoadLines", _city, Vector3.zero);
            BuildWornMarkings(lines);     // worn painted dashes and zebra crossings (SceneBuilder.Grit.cs)
        }

        static void BuildBlockBase(float cx, float cz, bool plaza)
        {
            var block = Node(plaza ? "PlazaBlock" : "Block", _city, new Vector3(cx, 0f, cz));
            Box("Slab", block, new Vector3(0f, SidewalkTop * 0.5f, 0f),
                new Vector3(BlockSize, SidewalkTop, BlockSize), plaza ? mPlaza : mSidewalk, true);
            float edge = BlockSize * 0.5f - 0.15f;
            float rimY = SidewalkTop + 0.05f;
            Box("CurbN", block, new Vector3(0f, rimY, edge), new Vector3(BlockSize, 0.1f, 0.3f), mCurb, true);
            Box("CurbS", block, new Vector3(0f, rimY, -edge), new Vector3(BlockSize, 0.1f, 0.3f), mCurb, true);
            Box("CurbE", block, new Vector3(edge, rimY, 0f), new Vector3(0.3f, 0.1f, BlockSize), mCurbZ, true);
            Box("CurbW", block, new Vector3(-edge, rimY, 0f), new Vector3(0.3f, 0.1f, BlockSize), mCurbZ, true);
        }

        static void BuildBlockBuildings(float cx, float cz, int k, int count)
        {
            // Quadrants: 0 (-,-) 1 (+,-) 2 (-,+) 3 (+,+). Gaps of >= 3 m between quadrants form alleys.
            var missing = new bool[4];
            if (count == 3) missing[k % 4] = true;
            else if (count == 2) { int q = k % 2; missing[q] = true; missing[3 - q] = true; }

            // Footprints snap to multiples of 3 m (6 or 9). Outer wall planes stay within 10.0 m of the block
            // center (NPC lanes run at 10.8 / 12.3); a 9 m side hugs 1.0 m from the center, so its alley partner stays 6 m.
            var wq = new int[4];
            var dq = new int[4];
            for (int q = 0; q < 4; q++)
            {
                if (missing[q]) continue;
                int w = 6, d = 6;
                if (_rng.NextDouble() < 0.4 && wq[q ^ 1] != 9) w = 9;
                if (_rng.NextDouble() < 0.4 && dq[q ^ 2] != 9) d = 9;
                wq[q] = w;
                dq[q] = d;
            }

            var group = Node("Buildings", _city, new Vector3(cx, 0f, cz));
            for (int q = 0; q < 4; q++)
            {
                if (missing[q]) continue;
                float sx = (q % 2 == 0) ? -1f : 1f;
                float sz = q < 2 ? -1f : 1f;
                int w = wq[q], d = dq[q];
                float innerX = w == 9 ? 1f : Rf(2f, 10f - w);
                float innerZ = d == 9 ? 1f : Rf(2f, 10f - d);
                float bx = sx * (innerX + w * 0.5f);
                float bz = sz * (innerZ + d * 0.5f);
                int[] floorChoices = { 1, 2, 2, 3, 3, 4, 5 };
                int floors = floorChoices[_rng.Next(floorChoices.Length)];
                MakeKitBuilding(group, new Vector3(bx, 0f, bz), w, d, floors, 15, false);
            }
        }

        static void BuildBoundary()
        {
            var ring = Node("Skyline", _env, Vector3.zero);
            // North / south rows cover the corners; east / west rows fill between them.
            const float near = 104f;
            Row(ring, true, 1f, -119f, 119f, near, 2);
            Row(ring, true, -1f, -119f, 119f, near, 1);
            Row(ring, false, 1f, -104f, 104f, near, 8);
            Row(ring, false, -1f, -104f, 104f, near, 4);

            var walls = Node("Boundary", _env, Vector3.zero);
            Wall(walls, "WallN", new Vector3(0f, 4f, 102f), new Vector3(206f, 8f, 1f));
            Wall(walls, "WallS", new Vector3(0f, 4f, -102f), new Vector3(206f, 8f, 1f));
            Wall(walls, "WallE", new Vector3(102f, 4f, 0f), new Vector3(1f, 8f, 206f));
            Wall(walls, "WallW", new Vector3(-102f, 4f, 0f), new Vector3(1f, 8f, 206f));
        }

        static void Row(Transform parent, bool alongX, float sign, float from, float to, float near, int mask)
        {
            float pos = from;
            while (pos < to)
            {
                int w = _rng.Next(2) == 0 ? 12 : 15;
                const int depth = 15;
                int floors = 4 + _rng.Next(5);
                float along = pos + w * 0.5f;
                float across = sign * (near + depth * 0.5f);
                if (alongX) MakeKitBuilding(parent, new Vector3(along, 0f, across), w, depth, floors, mask, true);
                else MakeKitBuilding(parent, new Vector3(across, 0f, along), depth, w, floors, mask, true);
                pos += w + Rf(0.5f, 1.5f);
            }
        }

        static void Wall(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<BoxCollider>().size = size;
        }

        // ------------------------------------------------------------------ props

        static void BuildProps()
        {
            BuildPlaza();

            int k = 0;
            int[] counts = { 4, 3, 2, 4, 3, 4, 2, 3 };
            for (int i = 0; i < BlockCenters.Length; i++)
            {
                for (int j = 0; j < BlockCenters.Length; j++)
                {
                    float cx = BlockCenters[i];
                    float cz = BlockCenters[j];
                    bool plaza = i == BlockCenters.Length / 2 && j == BlockCenters.Length / 2;
                    BuildCornerLamps(cx, cz);
                    if (plaza) continue;
                    BuildBlockProps(cx, cz, k, counts[k % counts.Length]);
                    k++;
                }
            }

            BuildParkedCars();
            BuildStreetGrit();
        }

        static void BuildCornerLamps(float cx, float cz)
        {
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    MakeLamp(new Vector3(cx + sx * 12.2f, SidewalkTop, cz + sz * 12.2f), Mathf.Atan2(sx, sz) * Mathf.Rad2Deg);
        }

        static void MakeLamp(Vector3 basePos, float yaw)
        {
            // The lamp arm points +Z; yaw swings it out over the intersection.
            var lamp = Spawn("StreetLamp", Sub("Lamps"), basePos, yaw);
            ThinBox(lamp, 0.2f, 5f);
        }

        static int _treeCount;

        static void MakeTree(Vector3 basePos)
        {
            string model = (_treeCount++ % 2 == 0) ? "Tree_A" : "Tree_B";
            var look = new Look { Leaf = (_treeCount % 3 == 0) ? Palette.LeafDark : Palette.Leaf };
            var tree = Spawn(model, Sub("Trees"), basePos, Rf(0f, 360f), look, "Tree");
            ThinBox(tree, 0.35f, 3f);
        }

        static void MakeCrate(Vector3 basePos, float yaw, Color wood)
        {
            var crate = Spawn("Crate", Sub("Crates"), basePos, yaw, new Look { Wood = wood });
            var box = crate.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.55f, 0f);
            box.size = Vector3.one * 1.1f;
        }

        // Sidewalk furniture line, measured from the block center.
        const float FurnitureLine = 11.55f;
        const float OuterLane = 12.3f;      // sidewalk lane near the curb (spawn candidates and trees)

        static void BuildBlockProps(float cx, float cz, int k, int count)
        {
            var taken = new List<Vector2>();

            // Trees in the sidewalk margin
            for (int t = 0; t < 3; t++)
            {
                int side = (k * 3 + t) % 4;
                float along = Rf(-7f, 7f);
                const float m = 11.6f;
                Vector3 p;
                switch (side)
                {
                    case 0: p = new Vector3(cx + along, SidewalkTop, cz + m); break;
                    case 1: p = new Vector3(cx + along, SidewalkTop, cz - m); break;
                    case 2: p = new Vector3(cx + m, SidewalkTop, cz + along); break;
                    default: p = new Vector3(cx - m, SidewalkTop, cz + along); break;
                }
                taken.Add(new Vector2(p.x, p.z));
                MakeTree(p);
            }

            // Hydrants, trash cans, bollards and planters, kept off the NPC lanes
            var kinds = new[] { "Hydrant", "TrashCan", "Bollard", "Planter" };
            for (int n = 0; n < 4; n++)
            {
                string kind = kinds[(n + k) % 4];
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    int side = _rng.Next(4);
                    float along = Rf(-7f, 7f);
                    Vector2 pos2;
                    switch (side)
                    {
                        case 0: pos2 = new Vector2(cx + along, cz + FurnitureLine); break;
                        case 1: pos2 = new Vector2(cx + along, cz - FurnitureLine); break;
                        case 2: pos2 = new Vector2(cx + FurnitureLine, cz + along); break;
                        default: pos2 = new Vector2(cx - FurnitureLine, cz + along); break;
                    }
                    bool clear = true;
                    foreach (var o in taken) if ((o - pos2).magnitude < 2.4f) { clear = false; break; }
                    if (!clear) continue;
                    taken.Add(pos2);
                    MakeFurniture(kind, new Vector3(pos2.x, SidewalkTop, pos2.y), side < 2 ? 0f : 90f);
                    break;
                }
            }

            // Crates in empty quadrants (courtyards)
            var missing = new bool[4];
            if (count == 3) missing[k % 4] = true;
            else if (count == 2) { int q = k % 2; missing[q] = true; missing[3 - q] = true; }
            for (int q = 0; q < 4; q++)
            {
                if (!missing[q]) continue;
                float sx = (q % 2 == 0) ? -1f : 1f;
                float sz = q < 2 ? -1f : 1f;
                var c = new Vector3(cx + sx * 6f, SidewalkTop, cz + sz * 6f);
                MakeCrate(c, Rf(-20f, 20f), Palette.WoodLight);
                MakeCrate(c + new Vector3(1.3f, 0f, 0.4f), Rf(-20f, 20f), Palette.WoodNatural);
                MakeCrate(c + new Vector3(0.3f, 0f, 1.4f), Rf(-20f, 20f), Palette.WoodLight);
            }
        }

        static void MakeFurniture(string kind, Vector3 pos, float yaw)
        {
            var go = Spawn(kind, Sub("Furniture"), pos, yaw, new Look { Paint = kind == "Bollard" ? Palette.BollardPaint : Palette.HydrantRed });
            // Colliders stay within 0.25 m of the line so they clear both lanes by 0.5 m.
            FitBox(go, float.NegativeInfinity, 0.25f);
        }

        static void BuildPlaza()
        {
            float b = SidewalkTop;

            // Fountain: stone basin with a basin-height collider, plus the water meshes
            var fountain = Spawn("Fountain", Sub("Plaza"), new Vector3(0f, b, 0f));
            // The basin top is higher than an agent can step, so keep the NavMesh off it.
            var noWalk = fountain.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
            noWalk.overrideArea = true;
            noWalk.area = 1; // Not Walkable
            var basin = fountain.AddComponent<BoxCollider>();
            basin.center = new Vector3(0f, 0.3f, 0f);
            basin.size = new Vector3(8f, 0.6f, 8f);
            Spawn("Fountain_Water", Sub("Plaza"), new Vector3(0f, b, 0f));

            // Trees
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    MakeTree(new Vector3(sx * 9f, b, sz * 9f));

            // Benches facing the fountain
            MakeBench(new Vector3(0f, b, -6.5f), 0f);
            MakeBench(new Vector3(0f, b, 6.5f), 180f);
            MakeBench(new Vector3(6.5f, b, 0f), 270f);
            MakeBench(new Vector3(-6.5f, b, 0f), 90f);

            // Two-high crate stack with a one-high step crate, reachable with a 1.5 m jump
            MakeCrate(new Vector3(4.5f, b, 10.5f), 0f, Palette.WoodLight);
            MakeCrate(new Vector3(4.5f, b + 1.1f, 10.5f), 0f, Palette.WoodNatural);
            MakeCrate(new Vector3(4.5f, b, 9.4f), 0f, Palette.WoodNatural);

            // Loose crates
            MakeCrate(new Vector3(-4.5f, b, 10.5f), 12f, Palette.WoodLight);
            MakeCrate(new Vector3(-5.7f, b, 10.8f), -8f, Palette.WoodNatural);
            MakeCrate(new Vector3(11f, b, -3f), 20f, Palette.WoodLight);
        }

        static void MakeBench(Vector3 basePos, float yaw)
        {
            var bench = Spawn("Bench", Sub("Benches"), basePos, yaw, new Look { Wood = Palette.WoodNatural });
            FitBox(bench);
        }

        static void BuildParkedCars()
        {
            // x, z, yaw (0 = along Z, 90 = along X). Cars sit in the road next to the curb.
            var cars = new[]
            {
                new Vector3(23.8f, -34f, 0f),
                new Vector3(23.8f, -26.6f, 0f),
                new Vector3(-14.2f, 8f, 0f),
                new Vector3(-23.8f, -30f, 0f),
                new Vector3(-38f, 23.8f, 90f),
                new Vector3(38f, -14.2f, 90f),
                new Vector3(52.2f, -10f, 0f),
                new Vector3(-8f, 52.2f, 90f),
                // Outer streets of the 5x5 grid
                new Vector3(-61.8f, 38f, 0f),
                new Vector3(61.8f, -76f, 0f),
                new Vector3(90.2f, 20f, 0f),
                new Vector3(-99.8f, -50f, 0f),
                new Vector3(99.8f, 70f, 0f),
                new Vector3(-52.2f, -76f, 0f),
                new Vector3(-76f, 90.2f, 90f),
                new Vector3(45f, -99.8f, 90f),
                new Vector3(-76f, 23.8f, 90f),
                new Vector3(76f, 14.2f, 90f),
                new Vector3(-38f, -14.2f, 90f),
                new Vector3(0f, -23.8f, 90f),
            };
            var models = new[] { "Car_Sedan", "Car_Hatchback", "Car_Van" };
            var paintSet = new[] { Palette.CarRed, Palette.CarBlue, Palette.CarWhite, Palette.CarSilver, Palette.CarGreen, Palette.CarYellow, Palette.CarBlack };
            for (int i = 0; i < cars.Length; i++)
                MakeCar(models[(i * 5 + i / 3) % models.Length], new Vector3(cars[i].x, 0f, cars[i].y), cars[i].z, paintSet[(i * 3 + i / 7) % paintSet.Length]);
        }

        static void MakeCar(string model, Vector3 pos, float yaw, Color paint)
        {
            var car = Spawn(model, Sub("Cars"), pos, yaw, new Look { Car = paint }, "ParkedCar");
            FitBox(car, 0.2f);
        }

        // ------------------------------------------------------------------ player

        static void BuildPlayer()
        {
            var root = new GameObject("Player");
            root.tag = "Player";
            root.transform.SetParent(_player, false);
            root.transform.position = new Vector3(0f, SidewalkTop + 0.05f, -11.5f);
            root.transform.rotation = Quaternion.identity;
            _playerRoot = root.transform;

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 50f;

            var pc = root.AddComponent<PlayerController>();
            _playerController = pc;

            // The courier: teal top, dark jeans, sneakers, terracotta cap and backpack; skin straight from Skin_Albedo.
            var look = new CharacterLook
            {
                Skin = Surf("CharSkin", Color.white, "Characters/Skin", Vector2.one, 0.3f),
                Top = Surf("CharTop", Palette.CharacterPrimary, "Characters/Cloth", Vector2.one, 0.2f),
                Pants = Surf("CharPants", Palette.Pants, "Characters/Denim", Vector2.one, 0.2f),
                Shoe = Surf("CharShoe", Palette.Sneaker, "Characters/Leather", Vector2.one, 0.3f),
                EyeWhite = EyeWhiteMat(),
                Iris = IrisMat(),
                Brow = Mat("CharBrow", Palette.HairDark),
            };
            var character = BuildHumanoid(root.transform, look, PlayerControllerPath, false, HeadGear.Cap, mTerracotta, mTerracotta);
            _playerCharacter = character.transform;
            character.AddComponent<PlayerAnimation>();
        }

        // ------------------------------------------------------------------ humanoid character

        enum HeadGear { Cap, ShortHair, LongHair, Hood }

        class CharacterLook
        {
            public Material Skin, Top, Pants, Shoe, EyeWhite, Iris, Brow;
        }

        static Transform _playerCharacter;

        static Material EyeWhiteMat() { return Surf("CharEyeWhite", new Color(0.93f, 0.91f, 0.88f), null, Vector2.one, 0.8f); }

        static Material IrisMat()
        {
            var m = Surf("CharIris", Color.white, null, Vector2.one, 0.85f);
            m.SetTexture("_BaseMap", Tex("Characters/Eye_Albedo"));
            EditorUtility.SetDirty(m);
            return m;
        }

        // Char_Human with an Animator (humanoid avatar + controller), slot materials, and the accessories fitted to the
        // Head / Chest bones. Returns the Char_Human root, which is where the animation components go.
        static GameObject BuildHumanoid(Transform parent, CharacterLook look, string controllerPath, bool cullTransforms,
            HeadGear gear, Material gearMat, Material packMat)
        {
            var go = (GameObject)Object.Instantiate(Model("Char_Human"));
            go.name = "Char_Human";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            animator.avatar = HumanAvatar();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = cullTransforms ? AnimatorCullingMode.CullUpdateTransforms : AnimatorCullingMode.AlwaysAnimate;

            var bodyLook = new Look
            {
                Override = new Dictionary<string, Material>
                {
                    { "Skin", look.Skin }, { "Top", look.Top }, { "Pants", look.Pants }, { "Shoe", look.Shoe },
                    { "EyeWhite", look.EyeWhite }, { "Iris", look.Iris }, { "Brow", look.Brow },
                },
            };
            ApplyLook(go, bodyLook);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.updateWhenOffscreen = false;
                smr.quality = SkinQuality.Bone4;
            }

            var gearLook = new Look
            {
                Override = new Dictionary<string, Material>
                {
                    { "Hair", gearMat }, { "Cap", gearMat }, { "Hood", gearMat }, { "Bag", packMat },
                },
            };
            var head = FindDeep(go.transform, "Head");
            var chest = FindDeep(go.transform, "Chest");
            switch (gear)
            {
                case HeadGear.Cap: AttachAccessory("Char2_Cap", head, go.transform, gearLook); break;
                case HeadGear.ShortHair: AttachAccessory("Char2_HairShort", head, go.transform, gearLook); break;
                case HeadGear.Hood: AttachAccessory("Char2_Hood", head, go.transform, gearLook); break;
                default: AttachAccessory("Char2_HairLong", head, go.transform, gearLook); break;
            }
            if (packMat != null) AttachAccessory("Char2_Backpack", chest, go.transform, gearLook);
            return go;
        }

        // The accessory meshes are authored in the T-pose with their pivot at the bone head, so they go on in the rest
        // pose with world axes and then follow the bone.
        static void AttachAccessory(string model, Transform bone, Transform character, Look look)
        {
            var acc = Spawn(model, bone, Vector3.zero, 0f, look, model);
            acc.transform.SetPositionAndRotation(bone.position, character.rotation);
            foreach (var c in acc.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            if (model == "Char2_Hood") FitHoodMantle(acc);
        }

        const string HoodFitPath = ModelDir + "/Characters/Generated/Char2_Hood_Fit.asset";

        // Char2_Hood is authored for the T-pose: its shoulder mantle runs out along the arms (to 0.57 m from the centre line).
        // The hood is rigid on the head, so with the arms down those tips stick out sideways. The mantle vertices are bent
        // down and in, as if draped over the shoulder, and the result is saved as a mesh asset.
        static void FitHoodMantle(GameObject hood)
        {
            var mf = hood.GetComponentInChildren<MeshFilter>();
            var src = mf.sharedMesh;
            var mesh = Object.Instantiate(src);
            mesh.name = "Char2_Hood_Fit";
            var v = mesh.vertices;
            const float shoulder = 0.15f;
            for (int i = 0; i < v.Length; i++)
            {
                float ax = Mathf.Abs(v[i].x);
                if (ax <= shoulder) continue;
                float d = ax - shoulder;
                v[i].x = Mathf.Sign(v[i].x) * (shoulder + d * 0.25f);
                v[i].y -= d * 0.6f;
            }
            mesh.vertices = v;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            EnsureFolder(ModelDir + "/Characters/Generated");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(HoodFitPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
            }
            else AssetDatabase.CreateAsset(mesh, HoodFitPath);
            mf.sharedMesh = mesh;
        }

        static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
                throw new InvalidOperationException("Field '" + field + "' not found on " + target.GetType().Name);
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        // ------------------------------------------------------------------ camera

        static void BuildCamera()
        {
            var pivotGo = new GameObject("CameraPivot");
            pivotGo.transform.SetParent(_camera, false);
            pivotGo.transform.position = _playerRoot.position + Vector3.up * 1.5f;
            _cameraPivot = pivotGo.transform;
            var orbit = pivotGo.AddComponent<CameraOrbit>();
            SetRef(orbit, "target", _playerRoot);
            SetRef(_playerController, "cameraYaw", _cameraPivot);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(_camera, false);
            camGo.transform.position = _playerRoot.position + new Vector3(0f, 3f, -6f);
            camGo.transform.rotation = Quaternion.Euler(14f, 0f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CinemachineBrain>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            var cmGo = new GameObject("Gameplay Camera");
            cmGo.transform.SetParent(_camera, false);
            cmGo.transform.position = camGo.transform.position;
            cmGo.transform.rotation = camGo.transform.rotation;
            var cm = cmGo.AddComponent<CinemachineCamera>();
            cm.Follow = _cameraPivot;
            var lens = cm.Lens;
            lens.FieldOfView = 55f;
            cm.Lens = lens;

            var follow = cmGo.AddComponent<CinemachineThirdPersonFollow>();
            follow.ShoulderOffset = new Vector3(0.6f, 0.35f, 0f);
            follow.VerticalArmLength = 0.4f;
            follow.CameraSide = 1f;
            follow.CameraDistance = 5.5f;
            follow.Damping = new Vector3(0.3f, 0.4f, 0.3f);
            var avoid = follow.AvoidObstacles;
            avoid.Enabled = true;
            avoid.CollisionFilter = 1 << 0; // Default layer
            avoid.IgnoreTag = "Player";
            avoid.CameraRadius = 0.25f;
            follow.AvoidObstacles = avoid;

            cmGo.AddComponent<CinemachineImpulseListener>();
        }

        // ------------------------------------------------------------------ game

        static void BuildGame()
        {
            var gameGo = new GameObject("Game");
            gameGo.transform.SetParent(_game, false);
            gameGo.AddComponent<GameLoop>();
            gameGo.AddComponent<GameAudio>();
            var fx = gameGo.AddComponent<FeelFX>();
            var impulse = gameGo.AddComponent<CinemachineImpulseSource>();

            // Landing puff
            var puff = MakeSystem("LandingPuff", gameGo.transform, 1f, false, false, 32);
            {
                var main = puff.main;
                main.startLifetime = 0.3f;
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                main.gravityModifier = 0f;
                main.startColor = Palette.Sidewalk;
                var shape = puff.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.2f;
            }

            // Ambient motes (loops, plays on awake)
            var motes = MakeSystem("Motes", _cameraPivot, 5f, true, true, 30);
            {
                var main = motes.main;
                main.prewarm = true;
                main.startLifetime = 4f;
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
                main.startColor = Palette.WithAlpha(Palette.Trim, 0.45f);
                var emission = motes.emission;
                emission.rateOverTime = 5f;
                var shape = motes.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(24f, 6f, 24f);
                var vol = motes.velocityOverLifetime;
                vol.enabled = true;
                vol.space = ParticleSystemSimulationSpace.World;
                vol.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
                vol.y = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
                vol.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            }

            SetRef(fx, "player", _playerController);
            SetRef(fx, "landingPuff", puff);
            SetRef(fx, "impulse", impulse);
        }

        static ParticleSystem MakeSystem(string name, Transform parent, float duration, bool loop, bool playOnAwake, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = duration;
            main.loop = loop;
            main.playOnAwake = playOnAwake;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mParticle;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        // ------------------------------------------------------------------ helpers

        static float Rf(float min, float max)
        {
            return min + (float)_rng.NextDouble() * (max - min);
        }

        static Transform Root(string name)
        {
            return new GameObject(name).transform;
        }

        static Transform Node(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        // Lazily created sub-groups under Props.
        static Transform Sub(string name)
        {
            var t = _props.Find(name);
            return t != null ? t : Node(name, _props, Vector3.zero);
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider)
        {
            return Prim(PrimitiveType.Cube, name, parent, pos, scale, mat, keepCollider);
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat,
            bool keepCollider, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            StripCollider(go);
            if (keepCollider)
            {
                // Box colliders only: capsule / sphere colliders on non-uniformly scaled meshes misbehave.
                var box = go.AddComponent<BoxCollider>();
                switch (type)
                {
                    case PrimitiveType.Cylinder:
                    case PrimitiveType.Capsule:
                        box.size = new Vector3(1f, 2f, 1f);
                        break;
                    default:
                        box.size = Vector3.one;
                        break;
                }
            }
            return go;
        }

        static void StripCollider(GameObject go)
        {
            foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
        }

        static void MarkStatic(Transform root)
        {
            if (root == null) return;
            const StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic
                | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
                throw new InvalidOperationException("Field '" + field + "' not found on " + target.GetType().Name);
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
