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
        static Transform _bodyPivot;

        [MenuItem("Tools/Zombies 5.5/Build Scene")]
        public static void BuildScene()
        {
            if (TMP_Settings.defaultFontAsset == null)
                throw new InvalidOperationException(
                    "TextMeshPro Essential Resources are missing. Import them (Window > TextMeshPro > Import TMP Essential Resources) before building the scene.");
            if (GraphicsSettings.currentRenderPipeline == null)
                throw new InvalidOperationException("No render pipeline asset is active. Zombies 5.5 needs the URP template.");

            _rng = new System.Random(7);
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
            BuildMaterials();
            BuildLighting();
            BuildCity();
            BuildProps();
            BuildPlayer();
            BuildCamera();
            BuildGame();
            BuildCombat();
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

            mAsphalt = Surf("Asphalt", Palette.Asphalt, "Asphalt", new Vector2(60f, 60f), 0.1f);
            mRoadLine = Mat("RoadLine", Palette.RoadLine);
            mSidewalk = Surf("Sidewalk", Palette.Sidewalk, "Concrete", new Vector2(BlockSize / 2f, BlockSize / 2f), 0.15f);
            mCurb = Surf("Curb", Palette.Curb, "Concrete", new Vector2(BlockSize / 2f, 0.15f), 0.15f);
            mCurbZ = Surf("CurbZ", Palette.Curb, "Concrete", new Vector2(0.15f, BlockSize / 2f), 0.15f);
            mPlaza = Surf("Plaza", Palette.Plaza, "Concrete", new Vector2(BlockSize / 2f, BlockSize / 2f), 0.15f);
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

            // Sky: Unity's physically based procedural sky (sun disc follows RenderSettings.sun).
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader == null) throw new InvalidOperationException("Shader 'Skybox/Procedural' not found.");
            mSky = LoadOrCreateMat("SkyProcedural", skyShader);
            mSky.SetFloat("_SunDisk", 2f);                 // high quality disc
            mSky.SetFloat("_SunSize", 0.045f);
            mSky.SetFloat("_SunSizeConvergence", 5f);
            mSky.SetFloat("_AtmosphereThickness", 1.25f);  // thicker air = warmer, redder low sun
            mSky.SetColor("_SkyTint", new Color(0.42f, 0.5f, 0.66f));
            mSky.SetColor("_GroundColor", Palette.GroundAmbient);
            mSky.SetFloat("_Exposure", 1.6f);
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
            sunGo.transform.rotation = Quaternion.Euler(32f, 35f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Palette.SunKey;
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f; // leave some sky fill in cast shadows so shaded streets stay readable
            RenderSettings.sun = sun;

            RenderSettings.skybox = mSky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.15f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.004f;
            RenderSettings.fogColor = new Color(0.92f, 0.78f, 0.6f);
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
            var bloom = GetOrAdd<Bloom>(profile);
            bloom.intensity.Override(0.25f);
            bloom.threshold.Override(1.2f);
            bloom.scatter.Override(0.6f);
            GetOrAdd<Vignette>(profile).intensity.Override(0.18f);
            var grade = GetOrAdd<ColorAdjustments>(profile);
            grade.postExposure.Override(0.35f);
            grade.saturation.Override(0f);
            grade.contrast.Override(8f);
            var wb = GetOrAdd<WhiteBalance>(profile);
            wb.temperature.Override(8f);
            wb.tint.Override(0f);
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
                    settings.FindPropertyRelative("Intensity").floatValue = 0.7f;
                    settings.FindPropertyRelative("Radius").floatValue = 0.4f;
                    settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.25f;
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
            foreach (float road in RoadCenters)
            {
                for (float s = -102f; s <= 102f; s += 6f)
                {
                    bool nearIntersection = false;
                    foreach (float r in RoadCenters)
                        if (Mathf.Abs(s - r) < 7.5f) { nearIntersection = true; break; }
                    if (nearIntersection) continue;
                    // Road running along Z (x = road) and road running along X (z = road)
                    Box("LineZ", lines, new Vector3(road, 0.015f, s), new Vector3(0.25f, 0.03f, 3f), mRoadLine, false);
                    Box("LineX", lines, new Vector3(s, 0.015f, road), new Vector3(3f, 0.03f, 0.25f), mRoadLine, false);
                }
            }
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
            var motion = root.AddComponent<CharacterMotion>();

            var rig = BuildCharacterRig(root.transform, motion, mPrimary, mPants, mSecondary, mShadow,
                HeadGear.Cap, mTerracotta, mTerracotta, 1f, 1f);
            _bodyPivot = rig.Body;
        }

        // ------------------------------------------------------------------ character rig

        enum HeadGear { Cap, ShortHair, LongHair, Hood }

        struct CharacterRig
        {
            public Transform Visual, Body;
        }

        // Rest-pose facts about the skinned body, measured while it is rebound (character space, metres).
        static float _handTipY;

        const string RigMeshPath = ModelDir + "/Characters/Generated/Char_Body_Rig.asset";

        // Builds the character under root and wires it into motion: our own pivot transforms (what CharacterMotion,
        // AimPose and EnemyAttackPose drive), one skinned body bound to them, and rigid hair / headwear / backpack.
        // gearMat is the cap, hair or hood color; packMat null means no backpack.
        static CharacterRig BuildCharacterRig(Transform root, CharacterMotion motion, Material top, Material pants,
            Material skin, Material shoes, HeadGear gear, Material gearMat, Material packMat, float height, float width)
        {
            var bodyLook = new Look
            {
                Override = new Dictionary<string, Material>
                {
                    { "Top", top }, { "Pants", pants }, { "Skin", skin }, { "Shoe", shoes }, { "Eye", mEye },
                },
            };
            var gearLook = new Look
            {
                Override = new Dictionary<string, Material>
                {
                    { "Hair", gearMat }, { "Cap", gearMat }, { "Hood", gearMat }, { "Bag", packMat },
                },
            };

            var visual = Node("Visual", root, Vector3.zero);
            visual.localScale = new Vector3(width, height, width);
            var body = Node("Body", visual, new Vector3(0f, 0.95f, 0f));
            var legL = Node("LegL", body, new Vector3(-0.11f, 0f, 0f));
            var legR = Node("LegR", body, new Vector3(0.11f, 0f, 0f));
            var armL = Node("ArmL", body, new Vector3(-0.21f, 0.55f, 0f));
            var armR = Node("ArmR", body, new Vector3(0.21f, 0.55f, 0f));
            var head = Node("Head", body, new Vector3(0f, 0.60f, 0f));

            BuildSkinnedBody(visual, new Dictionary<string, Transform>
            {
                { "Body", body }, { "LegL", legL }, { "LegR", legR }, { "ArmL", armL }, { "ArmR", armR }, { "Head", head },
            }, bodyLook);

            Transform backpack = null;
            if (packMat != null)
            {
                // Pivot: top centre of the pack's back face, resting on the upper back (body surface at z = -0.125).
                backpack = Node("Backpack", body, new Vector3(0f, 0.50f, -0.10f));
                Spawn("Char_Backpack", backpack, Vector3.zero, 0f, gearLook, "Pack");
            }

            switch (gear)
            {
                case HeadGear.Cap: Spawn("Char_Cap", head, Vector3.zero, 0f, gearLook, "Cap"); break;
                case HeadGear.ShortHair: Spawn("Char_HairShort", head, Vector3.zero, 0f, gearLook, "Hair"); break;
                case HeadGear.Hood: Spawn("Char_Hood", head, Vector3.zero, 0f, gearLook, "Hood"); break;
                default: Spawn("Char_HairLong", head, Vector3.zero, 0f, gearLook, "Hair"); break;
            }

            SetRef(motion, "visualRoot", visual);
            SetRef(motion, "body", body);
            SetRef(motion, "legL", legL);
            SetRef(motion, "legR", legR);
            SetRef(motion, "armL", armL);
            SetRef(motion, "armR", armR);
            SetRef(motion, "head", head);
            if (backpack != null) SetRef(motion, "backpack", backpack);

            return new CharacterRig { Visual = visual, Body = body };
        }

        // Copies Char_Body's mesh, rebinds it to our pivots (matched by name, mirrored when the FBX bone sits on the
        // other side of the body than our pivot of the same name) and saves the rebound mesh as an asset.
        static SkinnedMeshRenderer BuildSkinnedBody(Transform visual, Dictionary<string, Transform> pivots, Look look)
        {
            var model = Model("Char_Body");
            var src = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (src == null || src.sharedMesh == null) throw new InvalidOperationException("Char_Body.fbx has no skinned mesh.");

            var srcBones = src.bones;
            var bones = new Transform[srcBones.Length];
            for (int i = 0; i < srcBones.Length; i++)
            {
                string n = srcBones[i].name;
                if (!pivots.TryGetValue(n, out var pivot)) throw new InvalidOperationException("Char_Body bone '" + n + "' has no matching pivot.");
                float srcX = model.transform.InverseTransformPoint(srcBones[i].position).x;
                float pivotX = visual.InverseTransformPoint(pivot.position).x;
                if (Mathf.Abs(srcX) > 0.05f && Mathf.Sign(srcX) != Mathf.Sign(pivotX))
                    pivot = pivots[n.Substring(0, n.Length - 1) + (n.EndsWith("L") ? "R" : "L")];
                bones[i] = pivot;
            }

            // The FBX stores vertices already in Y-up character space (the 270 degree armature rotation lives only in the
            // bone hierarchy), so the rest-pose bind matrices are just bone-inverse times the renderer's matrix.
            var go = new GameObject("Char_Body");
            go.transform.SetParent(visual, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();

            var mesh = Object.Instantiate(src.sharedMesh);
            mesh.name = "Char_Body_Rig";
            var bind = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * go.transform.localToWorldMatrix;
            mesh.bindposes = bind;

            // Right-hand tip, for the weapon holder: lowest vertex mostly weighted to the arm that ends up as our ArmR.
            int armRIndex = Array.IndexOf(bones, pivots["ArmR"]);
            var verts = mesh.vertices;
            var perVertex = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            float tip = float.MaxValue;
            int off = 0;
            for (int v = 0; v < verts.Length; v++)
            {
                int n = perVertex[v];
                int best = -1;
                float bw = -1f;
                for (int k = 0; k < n; k++)
                    if (weights[off + k].weight > bw) { bw = weights[off + k].weight; best = weights[off + k].boneIndex; }
                off += n;
                if (best == armRIndex) tip = Mathf.Min(tip, verts[v].y);
            }
            _handTipY = tip;

            EnsureFolder(ModelDir + "/Characters/Generated");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(RigMeshPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
            }
            else AssetDatabase.CreateAsset(mesh, RigMeshPath);
            EditorUtility.SetDirty(mesh);

            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = pivots["Body"];
            smr.updateWhenOffscreen = false;
            smr.localBounds = new Bounds(new Vector3(0f, 0f, 0.05f), new Vector3(1.4f, 2.2f, 1.2f)); // relative to the Body pivot
            smr.quality = SkinQuality.Bone4;

            var srcMats = src.sharedMaterials;
            var mats = new Material[srcMats.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = SlotMat(srcMats[i] != null ? srcMats[i].name : "", look);
            smr.sharedMaterials = mats;
            return smr;
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
