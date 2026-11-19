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
    public static class SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Zombies55.unity";
        const string MaterialDir = "Assets/Art/Materials";
        const string VolumePath = "Assets/Settings/Zombies55Volume.asset";

        const float BlockSize = 26f;
        const float RoadWidth = 12f;
        const float SidewalkTop = 0.15f;

        static readonly float[] RoadCenters = { -57f, -19f, 19f, 57f };
        static readonly float[] BlockCenters = { -38f, 0f, 38f };

        static System.Random _rng;
        static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        static Shader _litShader;
        static int _buildingIndex;

        // Shared materials
        static Material mAsphalt, mRoadLine, mSidewalk, mCurb, mPlaza;
        static Material mTerracotta, mSage, mSand, mDustyBlue, mWindow, mTrim;
        static Material mPrimary, mSecondary, mPants, mLeaf, mTrunk, mShadow;
        static Material mGold, mTokenFace, mLampHead, mBeacon, mParticle, mSky;
        static Material[] _buildingMats;

        // Roots
        static Transform _env, _city, _props, _tokens, _player, _game, _camera;

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
            _tokens = Root("Tokens");
            _player = Root("Player");
            _game = Root("Game");
            _camera = Root("Camera");

            BuildMaterials();
            BuildLighting();
            BuildCity();
            BuildProps();
            BuildTokens();
            BuildPlayer();
            BuildPedestrians();
            BuildCamera();
            BuildGame();

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
            Debug.Log("Zombies 5.5 scene built");
        }

        // ------------------------------------------------------------------ materials

        static void BuildMaterials()
        {
            _litShader = GraphicsSettings.currentRenderPipeline.defaultMaterial.shader;
            if (_litShader == null) throw new InvalidOperationException("URP Lit shader not found.");

            mAsphalt = Mat("Asphalt", Palette.Asphalt);
            mRoadLine = Mat("RoadLine", Palette.RoadLine);
            mSidewalk = Mat("Sidewalk", Palette.Sidewalk);
            mCurb = Mat("Curb", Palette.Curb);
            mPlaza = Mat("Plaza", Palette.Plaza);
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
            mGold = Mat("GoldToken", Palette.Accent, 2.5f);
            mTokenFace = Mat("GoldTokenFace", Palette.Accent * 0.8f, 1.2f);
            mLampHead = Mat("LampHead", Palette.SunKey, 1.2f);
            _buildingMats = new[] { mTerracotta, mSage, mSand, mDustyBlue };

            // Transparent beacon
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) throw new InvalidOperationException("Shader 'Universal Render Pipeline/Unlit' not found.");
            mBeacon = LoadOrCreateMat("Beacon", unlit);
            mBeacon.SetFloat("_Surface", 1f);
            mBeacon.SetFloat("_Blend", 0f);
            mBeacon.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mBeacon.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mBeacon.SetFloat("_ZWrite", 0f);
            mBeacon.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mBeacon.SetOverrideTag("RenderType", "Transparent");
            mBeacon.SetShaderPassEnabled("ShadowCaster", false);
            mBeacon.renderQueue = 3000;
            var beaconColor = Palette.WithAlpha(Palette.Accent, 0.22f);
            mBeacon.SetColor("_BaseColor", beaconColor);
            mBeacon.color = beaconColor;
            EditorUtility.SetDirty(mBeacon);

            // Particles
            var sprites = Shader.Find("Sprites/Default");
            if (sprites == null) throw new InvalidOperationException("Shader 'Sprites/Default' not found.");
            mParticle = LoadOrCreateMat("Particle", sprites);
            EditorUtility.SetDirty(mParticle);

            // Sky
            var skyShader = Shader.Find("Zombies55/GradientSky");
            if (skyShader == null) throw new InvalidOperationException("Shader 'Zombies55/GradientSky' not found. Make sure Shaders/GradientSky.shader is imported.");
            mSky = LoadOrCreateMat("Sky", skyShader);
            mSky.SetColor("_TopColor", Palette.SkyTop);
            mSky.SetColor("_HorizonColor", Palette.SkyHorizon);
            mSky.SetColor("_BottomColor", Palette.GroundAmbient);
            mSky.SetFloat("_Exponent", 0.6f);
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

        static Material Mat(string name, Color color, float emissionMultiplier = 0f)
        {
            if (_mats.TryGetValue(name, out var cached)) return cached;
            var mat = LoadOrCreateMat(name, _litShader);
            mat.SetColor("_BaseColor", color);
            mat.color = color;
            mat.SetFloat("_Smoothness", 0.15f);
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
            RenderSettings.skybox = mSky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Ambient is the fill that keeps shadowed streets readable (HDR values above 1 are fine here).
            RenderSettings.ambientSkyColor = Color.Lerp(Palette.SkyTop, Palette.UiLight, 0.3f) * 1.35f;
            RenderSettings.ambientEquatorColor = Palette.Sidewalk * 0.9f;
            RenderSettings.ambientGroundColor = Color.Lerp(Palette.GroundAmbient, Palette.Sidewalk, 0.3f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.006f;
            RenderSettings.fogColor = Palette.SkyHorizon;

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(_env, false);
            sunGo.transform.rotation = Quaternion.Euler(38f, 35f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Palette.SunKey;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                urp.shadowDistance = 120f;
                urp.supportsHDR = true;
                EditorUtility.SetDirty(urp);
            }

            // Post-processing
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
            }
            GetOrAdd<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var bloom = GetOrAdd<Bloom>(profile);
            bloom.intensity.Override(0.35f);
            bloom.threshold.Override(1.1f);
            GetOrAdd<Vignette>(profile).intensity.Override(0.22f);
            var grade = GetOrAdd<ColorAdjustments>(profile);
            grade.saturation.Override(8f);
            grade.contrast.Override(5f);
            EditorUtility.SetDirty(profile);

            var volGo = new GameObject("Global Volume");
            volGo.transform.SetParent(_env, false);
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
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
            Box("Ground", _city, new Vector3(0f, -0.5f, 0f), new Vector3(240f, 1f, 240f), mAsphalt, true);

            BuildRoadLines();

            int k = 0;
            int[] counts = { 4, 3, 2, 4, 3, 4, 2, 3 };
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    float cx = BlockCenters[i];
                    float cz = BlockCenters[j];
                    bool plaza = i == 1 && j == 1;
                    BuildBlockBase(cx, cz, plaza);
                    if (!plaza)
                    {
                        BuildBlockBuildings(cx, cz, k, counts[k]);
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
                for (float s = -60f; s <= 60f; s += 6f)
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
            Box("CurbN", block, new Vector3(0f, rimY, edge), new Vector3(BlockSize, 0.1f, 0.3f), mCurb, false);
            Box("CurbS", block, new Vector3(0f, rimY, -edge), new Vector3(BlockSize, 0.1f, 0.3f), mCurb, false);
            Box("CurbE", block, new Vector3(edge, rimY, 0f), new Vector3(0.3f, 0.1f, BlockSize), mCurb, false);
            Box("CurbW", block, new Vector3(-edge, rimY, 0f), new Vector3(0.3f, 0.1f, BlockSize), mCurb, false);
        }

        static void BuildBlockBuildings(float cx, float cz, int k, int count)
        {
            // Quadrants: 0 (-,-) 1 (+,-) 2 (-,+) 3 (+,+). Gaps of >= 3 m between quadrants form alleys.
            var missing = new bool[4];
            if (count == 3) missing[k % 4] = true;
            else if (count == 2) { int q = k % 2; missing[q] = true; missing[3 - q] = true; }

            var group = Node("Buildings", _city, new Vector3(cx, 0f, cz));
            for (int q = 0; q < 4; q++)
            {
                if (missing[q]) continue;
                float sx = (q % 2 == 0) ? -1f : 1f;
                float sz = q < 2 ? -1f : 1f;
                float w = Rf(6.5f, 8.6f);
                float d = Rf(6.5f, 8.6f);
                float h = Rf(6f, 22f);
                float bx = sx * (1.5f + w * 0.5f + Rf(0f, 8.75f - w));
                float bz = sz * (1.5f + d * 0.5f + Rf(0f, 8.75f - d));
                MakeBuilding(group, bx, bz, w, d, h, NextBuildingMat(), 15);
            }
        }

        static Material NextBuildingMat()
        {
            return _buildingMats[_buildingIndex++ % _buildingMats.Length];
        }

        // faceMask bits: 1 = +z, 2 = -z, 4 = +x, 8 = -x
        static void MakeBuilding(Transform parent, float x, float z, float w, float d, float h, Material mat, int faceMask)
        {
            var b = Node("Building", parent, new Vector3(x, 0f, z));
            Box("Walls", b, new Vector3(0f, SidewalkTop + h * 0.5f, 0f), new Vector3(w, h, d), mat, true);

            const float bandH = 1f;
            for (float y = SidewalkTop + 2.4f; y + bandH * 0.5f < SidewalkTop + h - 1f; y += 3f)
            {
                if ((faceMask & 1) != 0) Box("Win", b, new Vector3(0f, y, d * 0.5f + 0.03f), new Vector3(w - 1f, bandH, 0.08f), mWindow, false);
                if ((faceMask & 2) != 0) Box("Win", b, new Vector3(0f, y, -d * 0.5f - 0.03f), new Vector3(w - 1f, bandH, 0.08f), mWindow, false);
                if ((faceMask & 4) != 0) Box("Win", b, new Vector3(w * 0.5f + 0.03f, y, 0f), new Vector3(0.08f, bandH, d - 1f), mWindow, false);
                if ((faceMask & 8) != 0) Box("Win", b, new Vector3(-w * 0.5f - 0.03f, y, 0f), new Vector3(0.08f, bandH, d - 1f), mWindow, false);
            }

            Box("RoofCap", b, new Vector3(0f, SidewalkTop + h + 0.2f, 0f), new Vector3(w + 0.4f, 0.4f, d + 0.4f), mTrim, false);
        }

        static void BuildBoundary()
        {
            var ring = Node("Skyline", _env, Vector3.zero);
            // North / south rows cover the corners; east / west rows fill between them.
            Row(ring, true, 1f, -77f, 77f, 66f, 2);
            Row(ring, true, -1f, -77f, 77f, 66f, 1);
            Row(ring, false, 1f, -65f, 65f, 66f, 8);
            Row(ring, false, -1f, -65f, 65f, 66f, 4);

            var walls = Node("Boundary", _env, Vector3.zero);
            Wall(walls, "WallN", new Vector3(0f, 4f, 64f), new Vector3(132f, 8f, 1f));
            Wall(walls, "WallS", new Vector3(0f, 4f, -64f), new Vector3(132f, 8f, 1f));
            Wall(walls, "WallE", new Vector3(64f, 4f, 0f), new Vector3(1f, 8f, 132f));
            Wall(walls, "WallW", new Vector3(-64f, 4f, 0f), new Vector3(1f, 8f, 132f));
        }

        static void Row(Transform parent, bool alongX, float sign, float from, float to, float near, int mask)
        {
            float pos = from;
            while (pos < to)
            {
                float w = Rf(11f, 15f);
                float depth = 14f;
                float h = Rf(18f, 40f);
                float along = pos + w * 0.5f;
                float across = sign * (near + depth * 0.5f);
                if (alongX) MakeBuilding(parent, along, across, w, depth, h, NextBuildingMat(), mask);
                else MakeBuilding(parent, across, along, depth, w, h, NextBuildingMat(), mask);
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
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    float cx = BlockCenters[i];
                    float cz = BlockCenters[j];
                    bool plaza = i == 1 && j == 1;
                    BuildCornerLamps(cx, cz);
                    if (plaza) continue;
                    BuildBlockProps(cx, cz, k, counts[k]);
                    k++;
                }
            }

            BuildParkedCars();
        }

        static void BuildCornerLamps(float cx, float cz)
        {
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    MakeLamp(new Vector3(cx + sx * 12.2f, SidewalkTop, cz + sz * 12.2f));
        }

        static void MakeLamp(Vector3 basePos)
        {
            var lamp = Node("StreetLamp", Sub("Lamps"), basePos);
            Box("Pole", lamp, new Vector3(0f, 2.25f, 0f), new Vector3(0.15f, 4.5f, 0.15f), mShadow, true);
            Box("Arm", lamp, new Vector3(0f, 4.5f, 0f), new Vector3(0.3f, 0.12f, 0.3f), mShadow, false);
            Prim(PrimitiveType.Sphere, "Head", lamp, new Vector3(0f, 4.75f, 0f), Vector3.one * 0.5f, mLampHead, false);
        }

        static void MakeTree(Vector3 basePos)
        {
            var tree = Node("Tree", Sub("Trees"), basePos);
            Prim(PrimitiveType.Cylinder, "Trunk", tree, new Vector3(0f, 1.1f, 0f), new Vector3(0.35f, 1.1f, 0.35f), mTrunk, true);
            Prim(PrimitiveType.Sphere, "LeavesA", tree, new Vector3(0f, 2.9f, 0f), Vector3.one * 2.6f, mLeaf, false);
            Prim(PrimitiveType.Sphere, "LeavesB", tree, new Vector3(0.5f, 3.4f, 0.3f), Vector3.one * 2.0f, mLeaf, false);
        }

        static void MakeCrate(Vector3 basePos, float yaw, Material mat)
        {
            Prim(PrimitiveType.Cube, "Crate", Sub("Crates"), basePos + new Vector3(0f, 0.55f, 0f), Vector3.one * 1.1f, mat, true,
                new Vector3(0f, yaw, 0f));
        }

        static void BuildBlockProps(float cx, float cz, int k, int count)
        {
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
                MakeTree(p);
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
                MakeCrate(c, Rf(-20f, 20f), mSand);
                MakeCrate(c + new Vector3(1.3f, 0f, 0.4f), Rf(-20f, 20f), mTrunk);
                MakeCrate(c + new Vector3(0.3f, 0f, 1.4f), Rf(-20f, 20f), mSand);
            }
        }

        static void BuildPlaza()
        {
            float b = SidewalkTop;

            // Fountain
            var fountain = Node("Fountain", Sub("Plaza"), new Vector3(0f, 0f, 0f));
            Prim(PrimitiveType.Cylinder, "Base", fountain, new Vector3(0f, b + 0.3f, 0f), new Vector3(8f, 0.3f, 8f), mTrim, true);
            Prim(PrimitiveType.Cylinder, "Water", fountain, new Vector3(0f, b + 0.62f, 0f), new Vector3(7f, 0.03f, 7f), mDustyBlue, false);
            Prim(PrimitiveType.Cylinder, "Pillar", fountain, new Vector3(0f, b + 1.5f, 0f), new Vector3(1f, 0.9f, 1f), mTrim, false);
            Prim(PrimitiveType.Cylinder, "UpperBowl", fountain, new Vector3(0f, b + 2.4f, 0f), new Vector3(3f, 0.12f, 3f), mTrim, false);
            Prim(PrimitiveType.Cylinder, "UpperWater", fountain, new Vector3(0f, b + 2.54f, 0f), new Vector3(2.6f, 0.03f, 2.6f), mDustyBlue, false);
            Prim(PrimitiveType.Sphere, "Finial", fountain, new Vector3(0f, b + 2.9f, 0f), Vector3.one * 0.6f, mTrim, false);

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
            MakeCrate(new Vector3(4.5f, b, 10.5f), 0f, mSand);
            MakeCrate(new Vector3(4.5f, b + 1.1f, 10.5f), 0f, mTrunk);
            MakeCrate(new Vector3(4.5f, b, 9.4f), 0f, mTrunk);

            // Loose crates
            MakeCrate(new Vector3(-4.5f, b, 10.5f), 12f, mSand);
            MakeCrate(new Vector3(-5.7f, b, 10.8f), -8f, mTrunk);
            MakeCrate(new Vector3(11f, b, -3f), 20f, mSand);
        }

        static void MakeBench(Vector3 basePos, float yaw)
        {
            var bench = Node("Bench", Sub("Benches"), basePos);
            bench.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box("Seat", bench, new Vector3(0f, 0.5f, 0f), new Vector3(2f, 0.12f, 0.6f), mTrunk, true);
            Box("Back", bench, new Vector3(0f, 0.85f, -0.27f), new Vector3(2f, 0.5f, 0.08f), mTrunk, false);
            Box("LegL", bench, new Vector3(-0.85f, 0.22f, 0f), new Vector3(0.1f, 0.44f, 0.5f), mShadow, false);
            Box("LegR", bench, new Vector3(0.85f, 0.22f, 0f), new Vector3(0.1f, 0.44f, 0.5f), mShadow, false);
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
            };
            for (int i = 0; i < cars.Length; i++)
                MakeCar(new Vector3(cars[i].x, 0f, cars[i].y), cars[i].z, _buildingMats[(i + 1) % _buildingMats.Length]);
        }

        static void MakeCar(Vector3 pos, float yaw, Material bodyMat)
        {
            var car = Node("ParkedCar", Sub("Cars"), pos);
            car.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box("Body", car, new Vector3(0f, 0.7f, 0f), new Vector3(1.9f, 0.7f, 4.2f), bodyMat, true);
            Box("Cabin", car, new Vector3(0f, 1.325f, -0.2f), new Vector3(1.6f, 0.55f, 2.2f), mWindow, false);
            foreach (float sx in new[] { -0.95f, 0.95f })
                foreach (float sz in new[] { -1.3f, 1.3f })
                    Prim(PrimitiveType.Cylinder, "Wheel", car, new Vector3(sx, 0.35f, sz), new Vector3(0.7f, 0.12f, 0.7f), mShadow, false,
                        new Vector3(0f, 0f, 90f));
        }

        // ------------------------------------------------------------------ tokens

        static void BuildTokens()
        {
            float b = SidewalkTop;
            var spots = new[]
            {
                // 4 on road intersections
                new Vector3(-19f, 0f, -19f),
                new Vector3(19f, 0f, 19f),
                new Vector3(57f, 0f, -19f),
                new Vector3(-57f, 0f, 19f),
                // 3 along mid-block streets
                new Vector3(-38f, 0f, 19f),
                new Vector3(19f, 0f, -38f),
                new Vector3(57f, 0f, 38f),
                // 2 in the plaza
                new Vector3(-4.5f, b, -4.5f),
                new Vector3(4.5f, b, 4.5f),
                // on top of the 2-high crate stack
                new Vector3(4.5f, b + 2.2f, 10.5f),
                // between parked cars
                new Vector3(23.8f, 0f, -30.3f),
                // corner alley between the four buildings of the first block
                new Vector3(-38f, b, -38f),
            };
            for (int i = 0; i < spots.Length; i++) MakeToken(i, spots[i]);
        }

        static void MakeToken(int index, Vector3 pos)
        {
            var root = Node("Token_" + (index + 1).ToString("00"), _tokens, pos);

            var trigger = root.gameObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.9f;
            trigger.center = new Vector3(0f, 1f, 0f);
            var rb = root.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var pickup = root.gameObject.AddComponent<Pickup>();

            var visual = Node("Visual", root, new Vector3(0f, 1f, 0f));
            // A thick gold coin with an embossed, slightly darker face so it reads solid from a distance.
            Prim(PrimitiveType.Cylinder, "Coin", visual, Vector3.zero, new Vector3(0.9f, 0.08f, 0.9f), mGold, false, new Vector3(90f, 0f, 0f));
            Prim(PrimitiveType.Cylinder, "Face", visual, Vector3.zero, new Vector3(0.62f, 0.095f, 0.62f), mTokenFace, false, new Vector3(90f, 0f, 0f));

            var beacon = Prim(PrimitiveType.Cylinder, "Beacon", root, new Vector3(0f, 5f, 0f), new Vector3(0.12f, 5f, 0.12f), mBeacon, false);
            var r = beacon.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            SetRef(pickup, "visual", visual);
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

        enum HeadGear { Cap, ShortHair, LongHair }

        struct CharacterRig
        {
            public Transform Visual, Body;
        }

        // Builds the primitive character (about 15 primitives) under root and wires it into motion.
        // gearMat is the cap or hair color; packMat null means no backpack.
        static CharacterRig BuildCharacterRig(Transform root, CharacterMotion motion, Material top, Material pants,
            Material skin, Material shoes, HeadGear gear, Material gearMat, Material packMat, float height, float width)
        {
            var visual = Node("Visual", root, Vector3.zero);
            visual.localScale = new Vector3(width, height, width);
            var body = Node("Body", visual, new Vector3(0f, 0.95f, 0f));

            var legL = Node("LegL", body, new Vector3(-0.14f, 0f, 0f));
            var legR = Node("LegR", body, new Vector3(0.14f, 0f, 0f));
            foreach (var leg in new[] { legL, legR })
            {
                Prim(PrimitiveType.Capsule, "Pants", leg, new Vector3(0f, -0.45f, 0f), new Vector3(0.2f, 0.42f, 0.2f), pants, false);
                Prim(PrimitiveType.Cube, "Shoe", leg, new Vector3(0f, -0.88f, 0.05f), new Vector3(0.2f, 0.12f, 0.32f), shoes, false);
            }

            Prim(PrimitiveType.Capsule, "Torso", body, new Vector3(0f, 0.38f, 0f), new Vector3(0.5f, 0.36f, 0.36f), top, false);

            Transform backpack = null;
            if (packMat != null)
            {
                backpack = Node("Backpack", body, new Vector3(0f, 0.55f, -0.2f));
                Prim(PrimitiveType.Cube, "Pack", backpack, new Vector3(0f, -0.15f, -0.06f), new Vector3(0.38f, 0.42f, 0.2f), packMat, false);
            }

            var head = Node("Head", body, new Vector3(0f, 0.72f, 0f));
            Prim(PrimitiveType.Sphere, "Face", head, new Vector3(0f, 0.12f, 0f), Vector3.one * 0.42f, skin, false);
            switch (gear)
            {
                case HeadGear.Cap:
                    Prim(PrimitiveType.Sphere, "Cap", head, new Vector3(0f, 0.25f, 0f), new Vector3(0.45f, 0.24f, 0.45f), gearMat, false);
                    Prim(PrimitiveType.Cube, "Brim", head, new Vector3(0f, 0.2f, 0.24f), new Vector3(0.36f, 0.04f, 0.26f), gearMat, false);
                    break;
                case HeadGear.ShortHair:
                    Prim(PrimitiveType.Sphere, "Hair", head, new Vector3(0f, 0.24f, -0.02f), new Vector3(0.45f, 0.26f, 0.46f), gearMat, false);
                    break;
                default:
                    Prim(PrimitiveType.Sphere, "Hair", head, new Vector3(0f, 0.24f, -0.02f), new Vector3(0.45f, 0.26f, 0.46f), gearMat, false);
                    Prim(PrimitiveType.Sphere, "HairBack", head, new Vector3(0f, 0.04f, -0.12f), new Vector3(0.4f, 0.46f, 0.26f), gearMat, false);
                    break;
            }
            Prim(PrimitiveType.Sphere, "EyeL", head, new Vector3(-0.08f, 0.14f, 0.19f), Vector3.one * 0.06f, mShadow, false);
            Prim(PrimitiveType.Sphere, "EyeR", head, new Vector3(0.08f, 0.14f, 0.19f), Vector3.one * 0.06f, mShadow, false);

            var armL = Node("ArmL", body, new Vector3(-0.33f, 0.55f, 0f));
            var armR = Node("ArmR", body, new Vector3(0.33f, 0.55f, 0f));
            armL.localRotation = Quaternion.Euler(0f, 0f, -8f);
            armR.localRotation = Quaternion.Euler(0f, 0f, 8f);
            foreach (var arm in new[] { armL, armR })
            {
                Prim(PrimitiveType.Capsule, "Sleeve", arm, new Vector3(0f, -0.28f, 0f), new Vector3(0.15f, 0.28f, 0.15f), top, false);
                Prim(PrimitiveType.Sphere, "Hand", arm, new Vector3(0f, -0.58f, 0f), Vector3.one * 0.13f, skin, false);
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

        // ------------------------------------------------------------------ pedestrians

        const int PedestrianCount = 16;
        const float InnerLane = 10.8f;      // block inner lane half-extent: buildings end at 10.25 (+0.3 radius); trunk edge 11.425 (-0.3)
        const float OuterLane = 12.3f;      // block outer lane: trunk edge 11.775 + 0.3 < 12.0; curb inner edge 12.7
        const float PlazaInnerLane = 8f;    // clears benches (6.8), crates (z from 8.85), fountain (radius 4), trees at (+-9,+-9)
        const float PlazaOuterLane = 12.4f; // clears the loose crate at (11,-3) (edge 11.7) and the z=10.5 crates (edge 11.4)
        const float LaneChamfer = 1.4f;     // outer lane cuts corners, keeping 0.85 m from the corner lamps at (+-12.2, +-12.2)

        // Counter-clockwise loop (seen from above) around a square of half-extent h; chamfer > 0 gives 8 points.
        static Vector3[] LoopPoints(float cx, float cz, float h, float chamfer, bool clockwise)
        {
            float a = h - chamfer;
            var list = new List<Vector2>();
            if (chamfer > 0f)
            {
                list.Add(new Vector2(h, -a)); list.Add(new Vector2(h, a));
                list.Add(new Vector2(a, h)); list.Add(new Vector2(-a, h));
                list.Add(new Vector2(-h, a)); list.Add(new Vector2(-h, -a));
                list.Add(new Vector2(-a, -h)); list.Add(new Vector2(a, -h));
            }
            else
            {
                list.Add(new Vector2(h, -h)); list.Add(new Vector2(h, h));
                list.Add(new Vector2(-h, h)); list.Add(new Vector2(-h, -h));
            }
            if (clockwise) list.Reverse();
            var pts = new Vector3[list.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector3(cx + list[i].x, SidewalkTop, cz + list[i].y);
            return pts;
        }

        static void BuildPedestrians()
        {
            var peds = Root("Pedestrians");

            // Block loop centers in grid order; index 4 is the plaza.
            var centers = new List<Vector2>();
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    centers.Add(new Vector2(BlockCenters[i], BlockCenters[j]));

            // First pass: one walker per loop (alternating lanes). Second pass: the other lane on seven loops,
            // so every lane holds at most one walker and nobody has to overtake.
            int[] secondPass = { 4, 0, 2, 6, 8, 1, 7 };

            var skins = new[] { mSecondary, Mat("SkinTan", Palette.SkinTan), Mat("SkinDeep", Palette.SkinDeep), Mat("SkinRich", Palette.SkinRich) };
            var hairs = new[] { Mat("HairDark", Palette.HairDark), Mat("HairAuburn", Palette.HairAuburn), mTrunk };
            var tops = new[] { mSand, mDustyBlue, mTerracotta, mSage, mTrim, mWindow, mLeaf };
            var legsMats = new[] { mPants, mWindow, mTrunk, mShadow };
            var hats = new[] { mDustyBlue, mSand, mSage, mTrim };
            var packs = new[] { mTrunk, mWindow, mSage, mSand };

            for (int n = 0; n < PedestrianCount; n++)
            {
                int loop = n < 9 ? n : secondPass[n - 9];
                bool plaza = loop == 4;
                bool inner = n < 9 ? (loop % 2 == 0) : (loop % 2 != 0);
                float h = plaza ? (inner ? PlazaInnerLane : PlazaOuterLane) : (inner ? InnerLane : OuterLane);
                // Inner lane walks clockwise, outer lane counter-clockwise, so lane-mates never meet head-on.
                var pts = LoopPoints(centers[loop].x, centers[loop].y, h, inner ? 0f : LaneChamfer, inner);

                int seg = _rng.Next(pts.Length);
                float t = Rf(0f, 1f);
                Vector3 a = pts[seg], b = pts[(seg + 1) % pts.Length];
                Vector3 pos = Vector3.Lerp(a, b, t);
                Vector3 dir = (b - a).normalized;

                var go = new GameObject("Pedestrian_" + (n + 1).ToString("00"));
                go.transform.SetParent(peds, false);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = 0.3f;
                capsule.height = 1.8f;
                capsule.center = new Vector3(0f, 0.9f, 0f);
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                var ped = go.AddComponent<Pedestrian>();
                var motion = go.AddComponent<CharacterMotion>();

                var skin = skins[_rng.Next(skins.Length)];
                var top = tops[_rng.Next(tops.Length)];
                var pants = legsMats[_rng.Next(legsMats.Length)];
                var gear = (HeadGear)_rng.Next(3);
                Material gearMat = gear == HeadGear.Cap ? hats[_rng.Next(hats.Length)] : hairs[_rng.Next(hairs.Length)];
                Material pack = _rng.NextDouble() < 0.3 ? packs[_rng.Next(packs.Length)] : null;
                float height = Rf(0.94f, 1.06f);
                float width = Rf(0.92f, 1.08f);
                BuildCharacterRig(go.transform, motion, top, pants, skin, mShadow, gear, gearMat, pack, height, width);

                // Slower, shorter strides than the sprinting player.
                SetFloat(motion, "stepFrequency", 1.15f);
                SetFloat(motion, "legSwing", 26f);
                SetFloat(motion, "armSwing", 20f);
                SetFloat(motion, "bobAmplitude", 0.04f);

                var so = new SerializedObject(ped);
                var wp = so.FindProperty("waypoints");
                wp.arraySize = pts.Length;
                for (int i = 0; i < pts.Length; i++) wp.GetArrayElementAtIndex(i).vector3Value = pts[i];
                so.FindProperty("startIndex").intValue = seg;
                so.FindProperty("speed").floatValue = Rf(1.1f, 1.7f);
                so.ApplyModifiedPropertiesWithoutUndo();

                SetLayerRecursive(go, 2); // Ignore Raycast: keeps Cinemachine obstacle avoidance (Default layer) off them
            }
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

            // Pickup burst
            var burst = MakeSystem("PickupBurst", gameGo.transform, 1f, false, false, 64);
            {
                var main = burst.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 3f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.25f);
                main.gravityModifier = 0.5f;
                main.startColor = new ParticleSystem.MinMaxGradient(Palette.Accent, Palette.Trim);
                var shape = burst.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.2f;
            }

            // Confetti
            var confetti = MakeSystem("Confetti", gameGo.transform, 1f, false, false, 128);
            {
                var main = confetti.main;
                main.startLifetime = 1.2f;
                main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
                main.gravityModifier = 1f;
                var gradient = new Gradient();
                gradient.mode = GradientMode.Fixed;
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Palette.Accent, 0.34f),
                        new GradientColorKey(Palette.CharacterPrimary, 0.67f),
                        new GradientColorKey(Palette.Terracotta, 1f),
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                var mmg = new ParticleSystem.MinMaxGradient(gradient);
                mmg.mode = ParticleSystemGradientMode.RandomColor;
                main.startColor = mmg;
                var shape = confetti.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 25f;
                shape.radius = 0.3f;
                shape.rotation = new Vector3(-90f, 0f, 0f); // cone axis is +Z; rotate so it emits +Y
            }

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

            // Floating text template
            var textGo = new GameObject("FloatingText");
            textGo.transform.SetParent(gameGo.transform, false);
            var text = textGo.AddComponent<TextMeshPro>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSharedMaterial = ShellBuilder.GetOrCreateOutlineMaterial();
            text.fontSize = 6f;
            text.fontStyle = FontStyles.Bold;
            text.color = Palette.Accent;
            text.alignment = TextAlignmentOptions.Center;
            text.text = "+1";
            textGo.SetActive(false);

            SetRef(fx, "player", _playerController);
            SetRef(fx, "popTarget", _bodyPivot);
            SetRef(fx, "pickupBurst", burst);
            SetRef(fx, "confetti", confetti);
            SetRef(fx, "landingPuff", puff);
            SetRef(fx, "floatingTextTemplate", text);
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
