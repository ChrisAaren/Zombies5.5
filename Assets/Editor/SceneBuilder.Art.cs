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
                bool mask = path.EndsWith("_Mask.png") || path.EndsWith("_MS.png") || path.EndsWith("_AO.png");   // MS = packed metallic (R) + smoothness (A)
                bool dirty = false;
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != type) { ti.textureType = type; dirty = true; }
                bool srgb = !(normal || mask);
                if (ti.sRGBTexture != srgb && !normal) { ti.sRGBTexture = srgb; dirty = true; }
                bool decal = path.EndsWith("/Grime_Albedo.png") || path.EndsWith("/RoadMarkings_Albedo.png") || path.EndsWith("/RoadMarkingsV2_Albedo.png") || path.EndsWith("/SideAds_Albedo.png");
                bool leaf = path.EndsWith("/LeafCards_Albedo.png");
                if (leaf && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
                if (leaf && !ti.mipMapsPreserveCoverage) { ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f; dirty = true; }
                if (decal && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
                // MPFB alpha cards (brows, lashes, hair): colour bleeds into the transparent texels, and coverage is kept
                // through the mips so distant hair does not thin out under alpha clip.
                bool cards = path.Contains("/MPFB/") && (path.Contains("_Brows_") || path.Contains("_Lashes_") || path.Contains("_Hair_Albedo"));
                if (cards && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
                if (cards && !ti.mipMapsPreserveCoverage) { ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = path.Contains("_Hair_") ? 0.5f : 0.3f; dirty = true; }
                if (decal && ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; dirty = true; }
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
                // Trees keep their authored crown-radial normals; cars keep their second UV set (dirt mask).
                if (path.Contains("/Nature/V2/") && mi.importNormals != ModelImporterNormals.Import) { mi.importNormals = ModelImporterNormals.Import; dirty = true; }
                // The MPFB characters are configured by ConfigureCharacterImports (Humanoid); the retired Char_Human keeps its
                // old settings; the rigid models keep no rig.
                if (path.EndsWith("/Char_Human.fbx")) continue;
                if (IsMpfbModel(path)) { if (dirty) { mi.SaveAndReimport(); changed++; } continue; }
                var animType = ModelImporterAnimationType.None;
                if (mi.animationType != animType) { mi.animationType = animType; dirty = true; }
                if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
                if (mi.isReadable) { mi.isReadable = false; dirty = true; }
                if (mi.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; dirty = true; }
                if (mi.materialLocation != ModelImporterMaterialLocation.InPrefab) { mi.materialLocation = ModelImporterMaterialLocation.InPrefab; dirty = true; }
                if (mi.materialName != ModelImporterMaterialName.BasedOnMaterialName) { mi.materialName = ModelImporterMaterialName.BasedOnMaterialName; dirty = true; }
                if (mi.materialSearch != ModelImporterMaterialSearch.Local) { mi.materialSearch = ModelImporterMaterialSearch.Local; dirty = true; }
                if (dirty) { mi.SaveAndReimport(); changed++; }
            }
            changed += ConfigureCharacterImports();
            if (changed > 0) AssetDatabase.Refresh();
            _models.Clear();
            _tex.Clear();
        }

        // ------------------------------------------------------------------ humanoid character and clips

        const string CourierPath = ModelDir + "/Characters/MPFB_Courier.fbx";
        const string HollowPath = ModelDir + "/Characters/MPFB_Hollow.fbx";

        static bool IsMpfbModel(string path) { return path == CourierPath || path == HollowPath; }
        const string AnimDir = "Assets/Art/Animations";

        // Clips that loop (loop time and loop pose); the rest play once.
        static readonly HashSet<string> LoopingClips = new HashSet<string>
        {
            "Idle", "Walk", "Jog", "Sprint", "WalkBack", "StrafeLeft", "StrafeRight", "Fall",
            "AimPistol", "AimRifle", "HoldRifleLow", "EnemyIdle", "Shamble", "EnemyJog", "EnemySprint",
        };

        // MPFB_Courier and MPFB_Hollow: Humanoid, avatar from the T-pose model. Each Anim_*: Humanoid, avatar created from the
        // clip's own skeleton; the humanoid retarget carries the clips onto the MPFB avatars. Returns the number of importers that changed.
        static int ConfigureCharacterImports()
        {
            int changed = 0;
            foreach (var modelPath in new[] { CourierPath, HollowPath })
            {
                var human = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (human == null) throw new InvalidOperationException(modelPath + " is missing.");
                bool dirty = false;
                if (human.animationType != ModelImporterAnimationType.Human) { human.animationType = ModelImporterAnimationType.Human; dirty = true; }
                if (human.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { human.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
                if (human.importAnimation) { human.importAnimation = false; dirty = true; }
                if (human.optimizeGameObjects) { human.optimizeGameObjects = false; dirty = true; }
                if (human.isReadable) { human.isReadable = false; dirty = true; }
                if (human.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { human.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; dirty = true; }
                if (human.materialLocation != ModelImporterMaterialLocation.InPrefab) { human.materialLocation = ModelImporterMaterialLocation.InPrefab; dirty = true; }
                if (dirty) { human.SaveAndReimport(); changed++; }
                for (int i = 0; i < 3 && EnsureHumanDescription(human, modelPath); i++) changed++;

                var avatar = AvatarAt(modelPath);
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                    throw new InvalidOperationException(modelPath + " avatar is not a valid humanoid: "
                        + (avatar == null ? "no avatar" : "isValid=" + avatar.isValid + " isHuman=" + avatar.isHuman));
            }

            foreach (var guid in AssetDatabase.FindAssets("Anim_ t:Model", new[] { AnimDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;
                string clipName = System.IO.Path.GetFileNameWithoutExtension(path);
                bool loop = LoopingClips.Contains(clipName.Substring("Anim_".Length));

                bool d = false;
                if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; d = true; }
                if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; d = true; }
                if (!mi.importAnimation) { mi.importAnimation = true; d = true; }
                if (mi.importCameras) { mi.importCameras = false; d = true; }
                if (mi.importLights) { mi.importLights = false; d = true; }
                if (mi.importVisibility) { mi.importVisibility = false; d = true; }
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; d = true; }
                if (mi.animationCompression != ModelImporterAnimationCompression.Optimal) { mi.animationCompression = ModelImporterAnimationCompression.Optimal; d = true; }

                // The take list is only known after the rig and animation settings are imported.
                if (d) { mi.SaveAndReimport(); changed++; d = false; }
                var defaults = mi.defaultClipAnimations;
                if (defaults.Length == 0 && mi.clipAnimations.Length > 0)
                {
                    // A previous clip table that no longer matches the take hides it; start again from the file's take.
                    mi.clipAnimations = new ModelImporterClipAnimation[0];
                    mi.SaveAndReimport();
                    defaults = mi.defaultClipAnimations;
                }
                if (defaults.Length == 0) throw new InvalidOperationException(path + " has no animation take.");
                var clip = defaults[0];
                clip.name = clipName;
                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.lockRootRotation = true;           // root rotation baked into pose, based upon original
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;            // root height baked into pose, based upon original
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = true;         // root XZ baked into pose (clips are in place; Death drifts ~0.3 m)
                clip.keepOriginalPositionXZ = true;
                clip.heightOffset = 0f;
                clip.rotationOffset = 0f;
                clip.cycleOffset = 0f;
                clip.maskType = ClipAnimationMaskType.None;

                var cur = mi.clipAnimations;
                bool same = cur.Length == 1 && cur[0].name == clip.name && cur[0].loopTime == loop && cur[0].loopPose == loop
                    && cur[0].lockRootRotation && cur[0].lockRootHeightY && cur[0].lockRootPositionXZ
                    && cur[0].keepOriginalOrientation && cur[0].keepOriginalPositionY && cur[0].keepOriginalPositionXZ
                    && cur[0].firstFrame == clip.firstFrame && cur[0].lastFrame == clip.lastFrame;
                if (!same) { mi.clipAnimations = new[] { clip }; d = true; }
                if (d) { mi.SaveAndReimport(); changed++; }
                for (int i = 0; i < 3 && EnsureHumanDescription(mi, path); i++) changed++;

                var clipAvatar = AvatarAt(path);
                if (clipAvatar == null || !clipAvatar.isValid || !clipAvatar.isHuman)
                    throw new InvalidOperationException(path + ": avatar is not a valid humanoid.");
            }
            return changed;
        }

        static bool IsFingerBone(string humanName)
        {
            return humanName.Contains("Thumb") || humanName.Contains("Index") || humanName.Contains("Middle")
                || humanName.Contains("Ring") || humanName.Contains("Little");
        }

        // Two fixes to the avatar's human description, then a reimport:
        // 1. Unity's automatic mapping leaves the optional Chest bone out, which discards the Chest rotation in every clip
        //    ("'Chest' is inbetween humanoid transforms").
        // 2. The clips twist the forearms and curl the fingers past Mecanim's default muscle ranges (finger Spread values up
        //    to +-7), and Mecanim clamps at the range: palms stayed down and fingers straight. Every bone's limit goes to
        //    +-150 degrees so the retarget reproduces the authored rotations (same skeleton, so nothing is lost).
        static bool EnsureHumanDescription(ModelImporter mi, string path)
        {
            var avatar = AvatarAt(path);
            if (avatar == null) return false;
            var hd = avatar.humanDescription;
            var list = new List<HumanBone>(hd.human);
            bool changed = false;
            bool hasChest = false;
            for (int i = 0; i < list.Count; i++)
            {
                var hb = list[i];
                if (hb.humanName == "Chest") hasChest = true;
                if (!hb.limit.useDefaultValues && hb.limit.max.x >= 149f) continue;
                hb.limit = new HumanLimit { useDefaultValues = false, min = new Vector3(-150f, -150f, -150f), max = new Vector3(150f, 150f, 150f), center = Vector3.zero, axisLength = 0f };
                list[i] = hb;
                changed = true;
            }
            if (!hasChest)
            {
                // Unity's automatic mapping leaves the optional Chest out: the clip skeleton's Chest sits between Spine and
                // Neck, which on the mixamo rig is Spine1.
                string chestBone = null;
                foreach (var t in AssetDatabase.LoadAllAssetsAtPath(path)) if (t is GameObject g) { var f = FindDeep(g.transform, "mixamorig:Spine1"); if (f != null) chestBone = f.name; }
                list.Add(new HumanBone { humanName = "Chest", boneName = chestBone ?? "Chest", limit = new HumanLimit { useDefaultValues = true } });
                changed = true;
            }
            if (!changed) return false;
            hd.human = list.ToArray();
            mi.humanDescription = hd;
            mi.SaveAndReimport();
            return true;
        }

        static Avatar AvatarAt(string path)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Avatar a) return a;
            return null;
        }

        static AnimationClip LoadClip(string name)
        {
            string path = AnimDir + "/Anim_" + name + ".fbx";
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
            throw new InvalidOperationException("Animation clip not found: " + path);
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

        static Texture2D TexOrNull(string name)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/" + name + ".png");
            return t;
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
