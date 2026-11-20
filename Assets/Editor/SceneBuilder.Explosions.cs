using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    // Explosion art: FX texture import settings, FX and wreck materials, the pooled debris prefabs, the ExplosionAssets catalogue,
    // and the scene object that runs it all (ExplosionFX + DebrisPool).
    public static partial class SceneBuilder
    {
        const string FxDir = "Assets/Art/FX";
        const string FxTexDir = "Assets/Art/FX/Textures";
        const string DebrisModelDir = "Assets/Art/Models/Debris";
        const string DebrisPrefabDir = "Assets/Art/FX/Debris";
        const string ExplosionAssetsPath = "Assets/Art/FX/ExplosionAssets.asset";
        const string DebrisJsonPath = "Assets/Art/FX/Debris_Parts.json";

        static ExplosionAssets _fxAssets;
        static Material _wreckBodyMat;

        [Serializable] class DebrisJsonPart { public string name; public float mass_kg; public float[] dims_m; }
        [Serializable] class DebrisJson { public DebrisJsonPart[] parts; }

        // ------------------------------------------------------------------ pipeline

        static void BuildExplosionAssets()
        {
            EnsureFolder(FxDir);
            EnsureFolder(FxTexDir);
            EnsureFolder(DebrisPrefabDir);
            EnsureDebrisLayer();
            WriteSoftDot();
            ConfigureFxTextures();
            _fxAssets = LoadOrCreateFxAssets();
            BuildFxMaterials(_fxAssets);
            BuildDebrisPrefabs(_fxAssets);
            EditorUtility.SetDirty(_fxAssets);
            AssetDatabase.SaveAssets();
        }

        // Layer 12 "Debris": pooled blast debris collides with the world only (the ignore pairs are written to the physics settings).
        static void EnsureDebrisLayer()
        {
            var tm = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/TagManager.asset");
            if (tm == null) return;
            var so = new SerializedObject(tm);
            var layers = so.FindProperty("layers");
            var l = layers.GetArrayElementAtIndex(GameLayers.Debris);
            if (l.stringValue != "Debris")
            {
                if (!string.IsNullOrEmpty(l.stringValue)) throw new InvalidOperationException("Layer " + GameLayers.Debris + " is taken by '" + l.stringValue + "'.");
                l.stringValue = "Debris";
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void WriteSoftDot()
        {
            string path = FxTexDir + "/FX_SoftDot.png";
            if (File.Exists(path)) return;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * a * (a * (a * 6f - 15f) + 10f);        // smootherstep: a bright hot centre and a clean zero edge
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
        }

        static void ConfigureFxTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { FxTexDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                bool premult = path.EndsWith("FX_FireLoop_8x8.png");
                bool dirty = false;
                if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; dirty = true; }
                if (!ti.sRGBTexture) { ti.sRGBTexture = true; dirty = true; }
                if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; dirty = true; }
                if (ti.alphaSource != TextureImporterAlphaSource.FromInput) { ti.alphaSource = TextureImporterAlphaSource.FromInput; dirty = true; }
                if (ti.alphaIsTransparency == premult) { ti.alphaIsTransparency = !premult; dirty = true; }   // straight alpha: dilate colour; the premultiplied loop stays untouched
                if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
                if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; dirty = true; }
                int aniso = path.EndsWith("FX_Scorch.png") || path.EndsWith("FX_ShockwaveRing.png") ? 8 : 0;
                if (ti.anisoLevel != aniso) { ti.anisoLevel = aniso; dirty = true; }
                if (ti.maxTextureSize < 2048) { ti.maxTextureSize = 2048; dirty = true; }
                if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
        }

        static ExplosionAssets LoadOrCreateFxAssets()
        {
            var a = AssetDatabase.LoadAssetAtPath<ExplosionAssets>(ExplosionAssetsPath);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<ExplosionAssets>();
                AssetDatabase.CreateAsset(a, ExplosionAssetsPath);
            }
            return a;
        }

        // ------------------------------------------------------------------ materials

        static Texture2D FxTex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(FxTexDir + "/" + name + ".png");

        // mode: 0 alpha blend, 1 additive premultiplied, 2 additive straight.
        static Material FxMat(string name, string tex, int mode, Color tint, float heat, Vector2 nearFade, float fog, float offset = 0f, float desat = 0f)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Shaders/FXParticle.shader");
            if (shader == null) throw new InvalidOperationException("Shader 'Zombies55/FXParticle' not found.");
            var m = LoadOrCreateMat(name, shader);
            m.SetTexture("_BaseMap", FxTex(tex));
            m.SetColor("_Tint", tint);
            m.SetFloat("_HeatBoost", heat);
            m.SetFloat("_Desat", desat);
            m.SetFloat("_Mode", mode);
            m.SetVector("_NearFade", new Vector4(nearFade.x, nearFade.y, 0f, 0f));
            m.SetFloat("_FogAmount", fog);
            m.SetFloat("_SrcBlend", mode == 0 ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", mode == 0 ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_OffsetFactor", offset);
            m.SetFloat("_OffsetUnits", offset);
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildFxMaterials(ExplosionAssets a)
        {
            // Fire colours in the flipbook are tone-mapped to <= 1: the tint and the heat boost bring them into HDR.
            a.fireball = FxMat("FX_Fireball", "FX_Fireball_8x8", 0, new Color(1.8f, 1.62f, 1.3f, 1f), 4.5f, new Vector2(2f, 6f), 0.35f, 0f, 1f);
            a.smoke = FxMat("FX_Smoke", "FX_Smoke_8x8", 0, new Color(1f, 0.97f, 0.93f, 1f), 0f, new Vector2(2f, 7f), 1f);
            a.dust = FxMat("FX_Dust", "FX_Smoke_8x8", 0, new Color(1.25f, 1.12f, 0.95f, 0.9f), 0f, new Vector2(2f, 7f), 1f);
            a.fireLoop = FxMat("FX_FireLoop", "FX_FireLoop_8x8", 1, new Color(1.5f, 0.95f, 0.5f, 1f), 0f, new Vector2(1f, 3f), 0.6f);
            a.ring = FxMat("FX_Shockwave", "FX_ShockwaveRing", 0, new Color(1f, 0.95f, 0.9f, 0.75f), 0f, new Vector2(0.5f, 1.5f), 1f);
            a.spark = FxMat("FX_Spark", "FX_SparkStreak", 2, new Color(4.5f, 2.6f, 1f, 1f), 0f, new Vector2(0.3f, 1f), 0.5f);
            a.ember = FxMat("FX_Ember", "FX_SoftDot", 2, new Color(3.2f, 1.3f, 0.45f, 1f), 0f, new Vector2(0.5f, 1.5f), 0.5f);
            a.flash = FxMat("FX_Flash", "FX_SoftDot", 2, new Color(12f, 10f, 7f, 1f), 0f, new Vector2(1.5f, 5f), 0f);
            a.bits = FxMat("FX_Bits", "FX_DebrisBits_4x2", 0, new Color(0.7f, 0.66f, 0.62f, 1f), 0f, new Vector2(0.4f, 1.2f), 1f);
            a.scorch = FxMat("FX_ScorchMark", "FX_Scorch", 0, new Color(1.5f, 1.4f, 1.3f, 0.95f), 0f, new Vector2(0f, 0.01f), 1f, -2f);

            // Wreck: burnt, charred metal in place of the old flat material. Soot-black glass.
            _wreckBodyMat = Surf("FX_WreckChar", new Color(1f, 0.95f, 0.9f), "FX_Charred", new Vector2(2f, 2f), 0.28f, 0.35f);
            a.wreckBody = _wreckBodyMat;
            a.wreckGlass = Surf("FX_WreckGlass", new Color(0.018f, 0.018f, 0.02f), null, Vector2.one, 0.55f, 0f);
            // Debris
            a.debrisPaint = Surf("FX_DebrisPaint", Color.white, "MetalPainted", new Vector2(1.5f, 1.5f), 0.5f, 0.2f);
        }

        static Material DebrisSlotMat(string slot, ExplosionAssets a)
        {
            switch (slot)
            {
                case "Paint": return a.debrisPaint;
                case "CarMetal": return Surf("FX_DebrisCarMetal", new Color(0.46f, 0.47f, 0.48f), null, Vector2.one, 0.45f, 0.75f);
                case "Charred": return Surf("FX_DebrisCharred", new Color(0.8f, 0.76f, 0.72f), "FX_Charred", new Vector2(1.5f, 1.5f), 0.3f, 0.3f);
                case "Rubber": return Surf("FX_DebrisRubber", new Color(0.05f, 0.05f, 0.055f), null, Vector2.one, 0.2f, 0f);
                case "Rim": return Surf("FX_DebrisRim", new Color(0.55f, 0.55f, 0.56f), null, Vector2.one, 0.6f, 0.9f);
                case "BarrelPaint": return Surf("FX_DebrisBarrelPaint", Palette.BarrelRed, "MetalPainted", new Vector2(1.5f, 1.5f), 0.4f, 0.3f);
            }
            throw new InvalidOperationException("Unknown debris slot '" + slot + "'");
        }

        // ------------------------------------------------------------------ debris prefabs

        static DebrisGroup GroupOf(string n)
        {
            if (n.Contains("Hood")) return DebrisGroup.Hood;
            if (n.Contains("Door")) return DebrisGroup.Door;
            if (n.Contains("Bumper")) return DebrisGroup.Bumper;
            if (n.Contains("Trunk")) return DebrisGroup.Trunk;
            if (n.Contains("Wheel")) return DebrisGroup.Wheel;
            if (n.Contains("Scrap")) return DebrisGroup.Scrap;
            if (n.Contains("Chunk")) return DebrisGroup.Chunk;
            if (n.Contains("BarrelLid")) return DebrisGroup.BarrelLid;
            if (n.Contains("BarrelHalf")) return DebrisGroup.BarrelHalf;
            throw new InvalidOperationException("Unknown debris part '" + n + "'");
        }

        static int PoolCountOf(DebrisGroup g)
        {
            switch (g)
            {
                case DebrisGroup.Hood: case DebrisGroup.Bumper: case DebrisGroup.Trunk: return 3;
                case DebrisGroup.Door: case DebrisGroup.Wheel: return 8;
                case DebrisGroup.Scrap: case DebrisGroup.Chunk: return 3;
                default: return 4;
            }
        }

        static void BuildDebrisPrefabs(ExplosionAssets a)
        {
            string jsonSrc = "Tools/Blender/staging/explosions/Debris_Parts.json";
            if (!File.Exists(DebrisJsonPath) && File.Exists(jsonSrc)) { File.Copy(jsonSrc, DebrisJsonPath); AssetDatabase.ImportAsset(DebrisJsonPath); }
            var json = JsonUtility.FromJson<DebrisJson>(File.ReadAllText(DebrisJsonPath));
            var defs = new List<DebrisPartDef>();
            foreach (var jp in json.parts)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(DebrisModelDir + "/" + jp.name + ".fbx");
                if (model == null) throw new InvalidOperationException("Debris model missing: " + jp.name);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = jp.name;
                var rend = go.GetComponentInChildren<MeshRenderer>();
                var filter = go.GetComponentInChildren<MeshFilter>();
                var src = rend.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = DebrisSlotMat(src[i].name, a);
                rend.sharedMaterials = dst;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                rend.receiveShadows = true;
                var hull = rend.gameObject.AddComponent<MeshCollider>();
                hull.sharedMesh = filter.sharedMesh;
                hull.convex = true;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = jp.mass_kg;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = jp.mass_kg < 5f ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;
                go.AddComponent<DebrisPart>();
                SetLayerRecursive(go, GameLayers.Debris);
                string path = DebrisPrefabDir + "/" + jp.name + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);
                var g = GroupOf(jp.name);
                defs.Add(new DebrisPartDef
                {
                    name = jp.name, group = g, prefab = prefab, mass = jp.mass_kg,
                    size = new Vector3(jp.dims_m[0], jp.dims_m[1], jp.dims_m[2]), poolCount = PoolCountOf(g),
                });
            }
            a.parts = defs.ToArray();
        }

        // ------------------------------------------------------------------ scene object

        static void BuildExplosions()
        {
            var go = new GameObject("Explosions");
            go.transform.SetParent(_combatRoot, false);
            var fx = go.AddComponent<ExplosionFX>();
            SetRef(fx, "assets", _fxAssets);
            var pool = go.AddComponent<DebrisPool>();
            SetRef(pool, "assets", _fxAssets);
        }
    }
}
