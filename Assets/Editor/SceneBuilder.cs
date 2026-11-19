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
            _v2 = new System.Random(512);
            _hydrants.Clear();
            _carColliders.Clear();
            _signalZ.Clear();
            _signalX.Clear();
            _treeCount = 0;
            _signIndex = 0;
            _quads.Clear();
            _mats.Clear();
            _buildingIndex = 0;
            _ringIndex = 0;

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

            ConfigurePhysicsLayers();
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
            BuildReflectionProbes();

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
            mAsphalt = Surf("Asphalt", new Color(0.78f, 0.77f, 0.75f), "AsphaltWorn", new Vector2(60f, 60f), 1f);
            mRoadLine = Mat("RoadLine", Palette.RoadLine);
            mSidewalk = SurfW("Sidewalk", new Color(0.8f, 0.75f, 0.67f), "ConcreteStained", new Vector2(BlockSize / 2f, BlockSize / 2f), 1f, 1.0f, 1f, 0.6f, 0f);
            mCurb = SurfW("Curb", new Color(0.72f, 0.67f, 0.6f), "ConcreteStained", new Vector2(BlockSize / 2f, 0.15f), 1f, 1.0f, 0f, 0.6f, 0f);
            mCurbZ = SurfW("CurbZ", new Color(0.72f, 0.67f, 0.6f), "ConcreteStained", new Vector2(0.15f, BlockSize / 2f), 1f, 1.0f, 0f, 0.6f, 0f);
            mPlaza = SurfW("Plaza", new Color(0.78f, 0.72f, 0.64f), "ConcreteStained", new Vector2(BlockSize / 2f, BlockSize / 2f), 1f, 0.8f, 1f, 0.6f, 0f);
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
            mSky.SetColor("_ZenithColor", new Color(0.17f, 0.35f, 0.68f));
            mSky.SetColor("_HorizonColor", new Color(0.74f, 0.84f, 0.93f));
            mSky.SetColor("_GroundColor", Palette.GroundAmbient);
            mSky.SetColor("_SunColor", Palette.SunHaze);
            mSky.SetColor("_CloudLit", new Color(1f, 0.97f, 0.92f));
            mSky.SetColor("_CloudShadow", new Color(0.62f, 0.7f, 0.82f));
            mSky.SetFloat("_Exposure", 1.1f);
            mSky.SetFloat("_HazeWidth", 0.3f);
            mSky.SetFloat("_SunSize", 0.022f);
            mSky.SetFloat("_SunIntensity", 8f);
            mSky.SetFloat("_CloudCover", 0.6f);
            mSky.SetFloat("_CloudSoft", 0.09f);
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
            sunGo.transform.rotation = Quaternion.Euler(55f, 34f, 0f);   // high clear-day sun: short, crisp shadows
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.83f);
            sun.intensity = 3.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            RenderSettings.sun = sun;
            mSky.SetVector("_SunDir", -sunGo.transform.forward);
            EditorUtility.SetDirty(mSky);

            RenderSettings.skybox = mSky;
            // Warm-gray fill (never blue-green): a tri-light ambient instead of the sky gradient. Reflections still come from the sky and the baked probes.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.72f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.66f, 0.63f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.48f, 0.43f, 0.38f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0016f;                      // light distance haze only
            RenderSettings.fogColor = new Color(0.68f, 0.78f, 0.9f);
            DynamicGI.UpdateEnvironment();

            ConfigureUrpAssets();

            // Post-processing
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
            }
            GetOrAdd<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);   // contrasty filmic curve
            var bloom = GetOrAdd<Bloom>(profile);                      // gentle halo round the sky, lamps and muzzle flashes
            bloom.intensity.Override(0.38f);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.7f);
            var vig = GetOrAdd<Vignette>(profile);
            vig.intensity.Override(0.16f);
            vig.smoothness.Override(0.45f);
            var grade = GetOrAdd<ColorAdjustments>(profile);
            grade.postExposure.Override(0.45f);                        // brighter, warm exposure
            grade.saturation.Override(-15f);                           // desaturated, grounded
            grade.contrast.Override(16f);
            var wb = GetOrAdd<WhiteBalance>(profile);
            wb.temperature.Override(4f);                              // warm, slightly sepia
            wb.tint.Override(0f);
            var split = GetOrAdd<SplitToning>(profile);                // warm-gray shadows (no blue-green), warm highlights
            split.shadows.Override(new Color(0.58f, 0.52f, 0.46f));
            split.highlights.Override(new Color(0.98f, 0.9f, 0.76f));
            split.balance.Override(-10f);
            var grain = GetOrAdd<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.1f);
            grain.response.Override(0.8f);
            var smh = GetOrAdd<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(1.06f, 1.0f, 0.92f, 0.04f));
            smh.midtones.Override(new Vector4(1.02f, 1f, 0.95f, 0f));
            smh.highlights.Override(new Vector4(1.03f, 1f, 0.95f, -0.03f));
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
                SetFloat("m_ShadowDistance", 140f);
                SetFloat("m_ShadowDepthBias", 0.8f);
                SetFloat("m_ShadowNormalBias", 0.8f);
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
                    settings.FindPropertyRelative("Radius").floatValue = 0.6f;
                    settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.4f;
                }
                sso.ApplyModifiedPropertiesWithoutUndo();
                ssao.SetActive(true);
                EditorUtility.SetDirty(ssao);
            }
            EditorUtility.SetDirty(rendererData);
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
            BuildV2Markings(lines);       // double yellow centreline, parking lines, stop bars, crosswalks (SceneBuilder.Street.cs)
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
                if (alongX) MakeRingBuilding(parent, new Vector3(along, 0f, across), w, depth, floors, mask);
                else MakeRingBuilding(parent, new Vector3(across, 0f, along), depth, w, floors, mask);
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
            Physics.SyncTransforms();
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
                    if (plaza) continue;
                    BuildBlockProps(cx, cz, k, counts[k % counts.Length]);
                    k++;
                }
            }

            BuildIntersections();
            BuildBlockLamps();
            BuildCurbside();
            BuildExtraStreetTrees();
            BuildParkedCars();
            BuildStreetGrit();
            BuildShopTrash();
            BuildStreetDirt();
            BuildStreetNoise();
            BuildSignalController();
        }

        static int _treeCount;

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

            // Street trees in the sidewalk margin: fewer than before, only where the spot is clear.
            for (int t = 0; t < 3; t++)
            {
                int side = (k * 3 + t) % 4;
                float along = Rf(-7f, 7f);
                const float m = 11.9f;
                Vector3 p;
                switch (side)
                {
                    case 0: p = new Vector3(cx + along, SidewalkTop, cz + m); break;
                    case 1: p = new Vector3(cx + along, SidewalkTop, cz - m); break;
                    case 2: p = new Vector3(cx + m, SidewalkTop, cz + along); break;
                    default: p = new Vector3(cx - m, SidewalkTop, cz + along); break;
                }
                if (t == 2 || (t == 1 && k % 2 == 1)) continue;
                if (!SidewalkSpotOk(p, 0.7f, 3f, 2.4f)) continue;
                taken.Add(new Vector2(p.x, p.z));
                MakeStreetTree(p);
                Physics.SyncTransforms();
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
            if (kind == "Hydrant") _hydrants.Add(new Vector2(pos.x, pos.z));
            var go = Spawn(kind == "Bollard" ? "Bollard_V2" : kind, Sub("Furniture"), pos, yaw, new Look { Paint = kind == "Bollard" ? Palette.BollardPaint : Palette.HydrantRed });
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
                    MakeStreetTree(new Vector3(sx * 9f, b, sz * 9f));

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

            AddPlayerCrowdObstacle(root);
            var pc = root.AddComponent<PlayerController>();
            _playerController = pc;

            var character = BuildCourier(root.transform);
            _playerCharacter = character.transform;
            character.AddComponent<PlayerAnimation>();
        }

        static Transform _playerCharacter;

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
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.TemporalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;

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
