using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    // Art pipeline half of the scene builder: import settings, model loading, slot-name materials.
    public static partial class SceneBuilder
    {
        const string ModelDir = "Assets/Art/Models";
        const string TextureDir = "Assets/Art/Textures";

        static readonly Dictionary<string, GameObject> _models = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();

        // ------------------------------------------------------------------ import settings

        static void ConfigureImports()
        {
            AssetDatabase.Refresh();
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                bool normal = path.EndsWith("_Normal.png");
                bool mask = path.EndsWith("_Mask.png");
                bool dirty = false;
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != type) { ti.textureType = type; dirty = true; }
                bool srgb = !(normal || mask);
                if (ti.sRGBTexture != srgb && !normal) { ti.sRGBTexture = srgb; dirty = true; }
                if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; dirty = true; }
                if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
                if (ti.anisoLevel != 4) { ti.anisoLevel = 4; dirty = true; }
                if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; dirty = true; }
                if (ti.maxTextureSize < 1024) { ti.maxTextureSize = 1024; dirty = true; }
                if (dirty) { ti.SaveAndReimport(); changed++; }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;
                bool dirty = false;
                if (!Mathf.Approximately(mi.globalScale, 1f)) { mi.globalScale = 1f; dirty = true; }
                if (!mi.useFileScale) { mi.useFileScale = true; dirty = true; }
                if (mi.importCameras) { mi.importCameras = false; dirty = true; }
                if (mi.importLights) { mi.importLights = false; dirty = true; }
                if (mi.importVisibility) { mi.importVisibility = false; dirty = true; }
                // Only the skinned body keeps its bones; with animation type None the importer drops the SkinnedMeshRenderer.
                var animType = path.EndsWith("/Char_Body.fbx") ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
                if (mi.animationType != animType) { mi.animationType = animType; dirty = true; }
                if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
                if (mi.isReadable) { mi.isReadable = false; dirty = true; }
                if (mi.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; dirty = true; }
                if (mi.materialLocation != ModelImporterMaterialLocation.InPrefab) { mi.materialLocation = ModelImporterMaterialLocation.InPrefab; dirty = true; }
                if (mi.materialName != ModelImporterMaterialName.BasedOnMaterialName) { mi.materialName = ModelImporterMaterialName.BasedOnMaterialName; dirty = true; }
                if (mi.materialSearch != ModelImporterMaterialSearch.Local) { mi.materialSearch = ModelImporterMaterialSearch.Local; dirty = true; }
                if (dirty) { mi.SaveAndReimport(); changed++; }
            }
            if (changed > 0) AssetDatabase.Refresh();
            _models.Clear();
            _tex.Clear();
        }

        static GameObject Model(string name)
        {
            if (_models.TryGetValue(name, out var m) && m != null) return m;
            var guids = AssetDatabase.FindAssets(name + " t:Model", new[] { ModelDir });
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                m = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                _models[name] = m;
                return m;
            }
            throw new InvalidOperationException("Model not found: " + name);
        }

        static Texture2D Tex(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_tex.TryGetValue(name, out var t)) return t;
            t = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/" + name + ".png");
            if (t == null) throw new InvalidOperationException("Texture not found: " + name);
            _tex[name] = t;
            return t;
        }

        // Removes slot materials (S_*) that this build did not use, so tint experiments do not pile up.
        static void CleanupSlotMaterials()
        {
            foreach (var guid in AssetDatabase.FindAssets("S_ t:Material", new[] { MaterialDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("S_") && !_mats.ContainsKey(name)) AssetDatabase.DeleteAsset(path);
            }
        }

        [MenuItem("Tools/Zombies 5.5/Apply Import Settings")]
        public static void ApplyImportSettings() { ConfigureImports(); }
    }
}
