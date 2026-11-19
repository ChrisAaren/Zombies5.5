using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import rule for the automated character pipeline. Scope: ONLY assets under Assets/Art/Characters/.
///  - models (.fbx): Humanoid rig, avatar from the model, a T-posed skeleton in the HumanDescription
///    (the rest pose of the FBX is an A-pose), URP Lit material remap.
///  - textures: *_Normal as normal map, *_Albedo as sRGB colour.
/// Post-import steps that need the imported model (T-pose, material remap) run from OnPostprocessAllAssets
/// through ApplyPostImport, which CharacterUnityCheck also calls synchronously.
/// </summary>
public class CharacterImportRule : AssetPostprocessor
{
    public const string Root = "Assets/Art/Characters/";

    public static bool InScope(string path)
    {
        return path != null && path.Replace('\\', '/').StartsWith(Root, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsModel(string path) { return InScope(path) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase); }

    // ---------------------------------------------------------------- textures
    void OnPreprocessTexture()
    {
        if (!InScope(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        string n = Path.GetFileNameWithoutExtension(assetPath);
        if (n.EndsWith("_Normal", StringComparison.OrdinalIgnoreCase))
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.sRGBTexture = false;
            ti.maxTextureSize = 2048;
        }
        else if (n.EndsWith("_Albedo", StringComparison.OrdinalIgnoreCase))
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.maxTextureSize = 2048;
        }
    }

    // ---------------------------------------------------------------- model
    void OnPreprocessModel()
    {
        if (!IsModel(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = false;
        mi.importCameras = false;
        mi.importLights = false;
        mi.optimizeGameObjects = false;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var p in imported)
        {
            if (!IsModel(p)) continue;
            string path = p;
            EditorApplication.delayCall += () => ApplyPostImport(path);
        }
    }

    /// <summary>Idempotent. Returns true when something changed (and the model was reimported).</summary>
    public static bool ApplyPostImport(string fbxPath)
    {
        if (!IsModel(fbxPath)) return false;
        var mi = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (mi == null) return false;
        bool changed = false;

        // settings that must hold (also set in OnPreprocessModel; guards a meta edited by hand)
        if (mi.animationType != ModelImporterAnimationType.Human || mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            changed = true;
        }

        // material
        string dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
        string name = Path.GetFileNameWithoutExtension(fbxPath);
        var mat = EnsureMaterial(dir, name);
        if (mat != null)
        {
            var map = mi.GetExternalObjectMap();
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Material>().ToArray())
            {
                if (m == mat) continue;
                if (map.Any(kv => kv.Key.name == m.name && kv.Key.type == typeof(Material) && kv.Value == mat)) continue;
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), mat);
                changed = true;
            }
        }

        // T-pose skeleton
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (model != null)
        {
            HumanDescription desc;
            string why;
            if (TryBuildTPoseDescription(model, out desc, out why))
            {
                if (!SameDescription(mi.humanDescription, desc))
                {
                    mi.humanDescription = desc;
                    changed = true;
                }
            }
            else Debug.LogWarning("[CharacterImportRule] " + fbxPath + ": " + why);
        }

        if (changed)
        {
            mi.SaveAndReimport();
            Debug.Log("[CharacterImportRule] applied rig/material settings to " + fbxPath);
        }
        return changed;
    }

    // ---------------------------------------------------------------- material
    public static Material EnsureMaterial(string dir, string name)
    {
        string matPath = dir + "/" + name + "_Lit.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + name + "_Albedo.png");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + name + "_Normal.png");
        if (albedo == null) return mat;
        if (mat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) { Debug.LogError("URP Lit shader not found"); return null; }
            mat = new Material(sh) { name = name + "_Lit" };
            AssetDatabase.CreateAsset(mat, matPath);
        }
        bool dirty = false;
        if (mat.GetTexture("_BaseMap") != albedo) { mat.SetTexture("_BaseMap", albedo); dirty = true; }
        mat.SetColor("_BaseColor", Color.white);
        if (normal != null && mat.GetTexture("_BumpMap") != normal)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            dirty = true;
        }
        if (Math.Abs(mat.GetFloat("_Metallic")) > 1e-4f) { mat.SetFloat("_Metallic", 0f); dirty = true; }
        if (Math.Abs(mat.GetFloat("_Smoothness") - 0.2f) > 1e-4f) { mat.SetFloat("_Smoothness", 0.2f); dirty = true; }
        if (dirty) { EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); }
        return mat;
    }

    // ---------------------------------------------------------------- T-pose
    class Node
    {
        public string name; public Node parent; public List<Node> kids = new List<Node>();
        public Vector3 lp, ls; public Quaternion lr;
        public Quaternion WRot() { return parent == null ? lr : parent.WRot() * lr; }
        public Vector3 WScale() { return parent == null ? ls : Vector3.Scale(parent.WScale(), ls); }
        public Vector3 WPos()
        {
            if (parent == null) return lp;
            return parent.WPos() + parent.WRot() * Vector3.Scale(parent.WScale(), lp);
        }
        public void SetWRot(Quaternion w) { lr = parent == null ? w : Quaternion.Inverse(parent.WRot()) * w; }
        // rotate this node (and so its subtree) by world rotation q
        public void RotateWorld(Quaternion q) { SetWRot(q * WRot()); }
    }

    static Node Build(Transform t, Node parent, Dictionary<string, Node> byName)
    {
        var n = new Node { name = t.name, parent = parent, lp = t.localPosition, lr = t.localRotation, ls = t.localScale };
        if (parent != null) parent.kids.Add(n);
        if (!byName.ContainsKey(n.name)) byName[n.name] = n;
        foreach (Transform c in t) Build(c, n, byName);
        return n;
    }

    static void Flatten(Node n, List<Node> list) { list.Add(n); foreach (var k in n.kids) Flatten(k, list); }

    static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
    static readonly string[] Segs = { "Proximal", "Intermediate", "Distal" };

    /// <summary>Computes the "Enforce T-Pose" equivalent: arms along the +-X axis, legs straight down, feet keep
    /// their rest orientation, fingers extended along the arm axis. Pure math on a copy of the hierarchy.</summary>
    public static bool TryBuildTPoseDescription(GameObject model, out HumanDescription desc, out string why)
    {
        desc = new HumanDescription(); why = null;
        var byName = new Dictionary<string, Node>();
        var root = Build(model.transform, null, byName);

        // humanoid bone mapping by Unity enum name
        var human = new List<HumanBone>();
        var missingRequired = new List<string>();
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            var hb = (HumanBodyBones)i;
            string bn = hb.ToString();
            if (byName.ContainsKey(bn))
                human.Add(new HumanBone { humanName = HumanTrait.BoneName[i], boneName = bn, limit = new HumanLimit { useDefaultValues = true } });
            else if (HumanTrait.RequiredBone(i)) missingRequired.Add(bn);
        }
        if (missingRequired.Count > 0) { why = "missing required bones: " + string.Join(",", missingRequired); return false; }

        Node N(string n) { Node r; byName.TryGetValue(n, out r); return r; }
        void Align(Node b, Node child, Vector3 target)
        {
            if (b == null || child == null) return;
            Vector3 d = child.WPos() - b.WPos();
            if (d.sqrMagnitude < 1e-12f) return;
            b.RotateWorld(Quaternion.FromToRotation(d.normalized, target));
        }

        float side = 1f; // +1 when the character's Left side is +X
        if (N("LeftUpperArm") != null && N("RightUpperArm") != null)
            side = Mathf.Sign(N("LeftUpperArm").WPos().x - N("RightUpperArm").WPos().x);
        if (side == 0f) side = 1f;

        foreach (string s in new[] { "Left", "Right" })
        {
            Vector3 armDir = new Vector3(s == "Left" ? side : -side, 0, 0);
            Align(N(s + "UpperArm"), N(s + "LowerArm"), armDir);
            Align(N(s + "LowerArm"), N(s + "Hand"), armDir);
            Align(N(s + "Hand"), N(s + "MiddleProximal"), armDir);
            foreach (var f in Fingers)
                for (int k = 0; k < 2; k++)
                    Align(N(s + f + Segs[k]), N(s + f + Segs[k + 1]), armDir);
            // the distal bones have no child: they inherit the intermediate's rotation (straight finger)
            var foot = N(s + "Foot");
            Quaternion footRest = foot != null ? foot.WRot() : Quaternion.identity;
            Align(N(s + "UpperLeg"), N(s + "LowerLeg"), Vector3.down);
            Align(N(s + "LowerLeg"), foot, Vector3.down);
            if (foot != null) foot.SetWRot(footRest);
        }

        var flat = new List<Node>(); Flatten(root, flat);
        var skel = new SkeletonBone[flat.Count];
        for (int i = 0; i < flat.Count; i++)
            skel[i] = new SkeletonBone { name = flat[i].name, position = flat[i].lp, rotation = flat[i].lr, scale = flat[i].ls };

        desc.human = human.ToArray();
        desc.skeleton = skel;
        desc.upperArmTwist = 0.5f; desc.lowerArmTwist = 0.5f; desc.upperLegTwist = 0.5f; desc.lowerLegTwist = 0.5f;
        desc.armStretch = 0.05f; desc.legStretch = 0.05f; desc.feetSpacing = 0f; desc.hasTranslationDoF = false;
        return true;
    }

    static bool SameDescription(HumanDescription a, HumanDescription b)
    {
        if (a.human == null || b.human == null || a.skeleton == null || b.skeleton == null) return false;
        if (a.human.Length != b.human.Length || a.skeleton.Length != b.skeleton.Length) return false;
        var map = new Dictionary<string, SkeletonBone>(StringComparer.Ordinal);
        foreach (var s in a.skeleton) map[s.name] = s;
        foreach (var s in b.skeleton)
        {
            SkeletonBone o;
            if (!map.TryGetValue(s.name, out o)) return false;
            if ((o.position - s.position).sqrMagnitude > 1e-8f) return false;
            if (Quaternion.Angle(o.rotation, s.rotation) > 0.01f) return false;
            if ((o.scale - s.scale).sqrMagnitude > 1e-8f) return false;
        }
        var hm = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var h in a.human) hm[h.humanName] = h.boneName;
        foreach (var h in b.human)
        {
            string bn;
            if (!hm.TryGetValue(h.humanName, out bn) || bn != h.boneName) return false;
        }
        return true;
    }
}
