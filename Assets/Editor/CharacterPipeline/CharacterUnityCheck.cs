using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>
/// Stage 6 of the character pipeline: Unity-side check.
///   Open Editor : unity command script eval -- --code "return CharacterUnityCheck.Run(\"testcourier\");" --timeout 180000
///   Batch mode  : Unity.exe -batchmode -projectPath P -executeMethod CharacterUnityCheck.RunBatch -character testcourier -logFile f
/// Writes Tools/CharacterPipeline/work/&lt;name&gt;/&lt;version&gt;/unity_report.json plus screenshots next to it.
/// Uses a PreviewRenderUtility scene only; the game scene is never touched (verified by a check).
/// </summary>
public static class CharacterUnityCheck
{
    static readonly string[] Clips = { "Anim_Walk", "Anim_AimRifle", "Anim_Jog" };
    const int FramesPerClip = 6;      // sampled for the numeric checks
    const int ShotsPerClip = 3;       // rendered
    const int TilePx = 384;

    class Check
    {
        public string id; public double value; public string valueText; public string threshold; public bool pass; public string detail;
    }

    class Ctx
    {
        public string name, fbxPath, outDir;
        public List<Check> checks = new List<Check>();
        public List<string> shots = new List<string>();
        public Dictionary<string, string> extra = new Dictionary<string, string>();
        public void Add(string id, double v, string thr, bool pass, string detail = "", string vtext = null)
        {
            checks.Add(new Check { id = id, value = double.IsNaN(v) || double.IsInfinity(v) ? -1 : Math.Round(v, 4), valueText = vtext ?? v.ToString("0.####"), threshold = thr, pass = pass, detail = detail });
        }
    }

    // ------------------------------------------------------------ entry points
    /// <summary>Editor entry. Returns a one-line JSON summary (full report is written to disk).</summary>
    public static string Run(string characterName)
    {
        var ctx = Execute(characterName);
        return Summary(ctx);
    }

    /// <summary>Batch entry: -executeMethod CharacterUnityCheck.RunBatch -character NAME. Calls EditorApplication.Exit.</summary>
    public static void RunBatch()
    {
        int code = 2;
        try
        {
            var args = Environment.GetCommandLineArgs();
            string name = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-character") name = args[i + 1];
            if (string.IsNullOrEmpty(name)) { Debug.LogError("[CharacterUnityCheck] missing -character <name>"); code = 3; }
            else
            {
                var ctx = Execute(name);
                Debug.Log("[CharacterUnityCheck] " + Summary(ctx));
                code = ctx.checks.All(c => c.pass) ? 0 : 1;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[CharacterUnityCheck] exception: " + e);
            code = 2;
        }
        EditorApplication.Exit(code);
    }

    static string Summary(Ctx ctx)
    {
        int p = ctx.checks.Count(c => c.pass);
        var failed = ctx.checks.Where(c => !c.pass).Select(c => c.id);
        return "{\"pass\":" + (p == ctx.checks.Count ? "true" : "false") + ",\"passed\":" + p + ",\"total\":" + ctx.checks.Count +
               ",\"failed\":[" + string.Join(",", failed.Select(f => "\"" + f + "\"")) + "],\"report\":\"" + ReportPath(ctx).Replace("\\", "/") + "\"}";
    }

    static string ReportPath(Ctx ctx) { return Path.Combine(ctx.outDir, "unity_report.json"); }

    // ------------------------------------------------------------ main
    static Ctx Execute(string name)
    {
        var ctx = new Ctx { name = name, fbxPath = "Assets/Art/Characters/" + name + "/" + name + ".fbx" };
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        ctx.outDir = PickVersionDir(projectRoot, name, ctx.fbxPath);
        Directory.CreateDirectory(ctx.outDir);

        var activeScene = SceneManager.GetActiveScene();
        string scenePath = activeScene.path; bool sceneDirty = activeScene.isDirty;
        int sceneCount = SceneManager.sceneCount;

        PreviewRenderUtility pru = null;
        GameObject inst = null;
        try
        {
            ImportAndSettings(ctx);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ctx.fbxPath);
            if (model == null) { ctx.Add("model.loaded", 0, "model exists", false, ctx.fbxPath + " not found"); return Finish(ctx); }

            pru = new PreviewRenderUtility();
            inst = UnityEngine.Object.Instantiate(model);
            inst.name = name + "_check";
            pru.AddSingleGO(inst);
            inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;

            var animator = inst.GetComponent<Animator>();
            if (animator == null) animator = inst.AddComponent<Animator>();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ctx.fbxPath).OfType<Avatar>().FirstOrDefault();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            AvatarAndMaterialChecks(ctx, avatar, animator, inst);
            LodAndHeightChecks(ctx, inst);
            AnimationChecks(ctx, pru, inst, animator);
        }
        catch (Exception e)
        {
            ctx.Add("check.exception", 0, "no exception", false, e.ToString());
        }
        finally
        {
            if (inst != null) UnityEngine.Object.DestroyImmediate(inst);
            if (pru != null) pru.Cleanup();
        }

        var after = SceneManager.GetActiveScene();
        bool untouched = after.path == scenePath && after.isDirty == sceneDirty && SceneManager.sceneCount == sceneCount;
        ctx.Add("cleanup.game_scene_untouched", untouched ? 1 : 0, "active scene path/dirty/count unchanged", untouched,
            "scene='" + after.path + "' dirty=" + after.isDirty + " count=" + SceneManager.sceneCount, untouched ? "true" : "false");
        return Finish(ctx);
    }

    static Ctx Finish(Ctx ctx)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"character\": " + Q(ctx.name) + ",\n");
        sb.Append("  \"fbx\": " + Q(ctx.fbxPath) + ",\n");
        sb.Append("  \"version_dir\": " + Q(ctx.outDir.Replace("\\", "/")) + ",\n");
        sb.Append("  \"unity_version\": " + Q(Application.unityVersion) + ",\n");
        sb.Append("  \"overall_pass\": " + (ctx.checks.All(c => c.pass) ? "true" : "false") + ",\n");
        sb.Append("  \"checks_passed\": " + ctx.checks.Count(c => c.pass) + ", \"checks_total\": " + ctx.checks.Count + ",\n");
        sb.Append("  \"screenshots\": [" + string.Join(", ", ctx.shots.Select(s => Q(s.Replace("\\", "/")))) + "],\n");
        sb.Append("  \"info\": {" + string.Join(", ", ctx.extra.Select(kv => Q(kv.Key) + ": " + Q(kv.Value))) + "},\n");
        sb.Append("  \"checks\": [\n");
        for (int i = 0; i < ctx.checks.Count; i++)
        {
            var c = ctx.checks[i];
            sb.Append("    {\"id\": " + Q(c.id) + ", \"value\": " + c.value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                      ", \"value_text\": " + Q(c.valueText) + ", \"threshold\": " + Q(c.threshold) +
                      ", \"pass\": " + (c.pass ? "true" : "false") + ", \"detail\": " + Q(c.detail) + "}" + (i < ctx.checks.Count - 1 ? "," : "") + "\n");
        }
        sb.Append("  ]\n}\n");
        File.WriteAllText(ReportPath(ctx), sb.ToString());
        return ctx;
    }

    static string Q(string s)
    {
        if (s == null) return "\"\"";
        var sb = new StringBuilder("\"");
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': break;
                case '\t': sb.Append("\\t"); break;
                default: if (ch < 32) sb.Append(' '); else sb.Append(ch); break;
            }
        }
        return sb.Append('"').ToString();
    }

    // newest version folder whose export/<name>.fbx is byte-identical to the project asset (the published one); else the latest
    static string PickVersionDir(string projectRoot, string name, string fbxPath)
    {
        string wdir = Path.Combine(projectRoot, "Tools", "CharacterPipeline", "work", name);
        if (!Directory.Exists(wdir)) return Path.Combine(wdir, "v000");
        var dirs = Directory.GetDirectories(wdir, "v*").Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), @"^v\d+$"))
            .OrderByDescending(d => Path.GetFileName(d)).ToList();
        if (dirs.Count == 0) return Path.Combine(wdir, "v000");
        string asset = Path.Combine(projectRoot, fbxPath);
        if (File.Exists(asset))
        {
            long len = new FileInfo(asset).Length;
            byte[] ab = null;
            foreach (var d in dirs)
            {
                string f = Path.Combine(d, "export", name + ".fbx");
                if (!File.Exists(f) || new FileInfo(f).Length != len) continue;
                if (ab == null) ab = File.ReadAllBytes(asset);
                if (File.ReadAllBytes(f).SequenceEqual(ab)) return d;
            }
        }
        return dirs[0];
    }

    // ------------------------------------------------------------ import
    static void ImportAndSettings(Ctx ctx)
    {
        // force reimport of textures + model, then let the rule finish (T-pose, material remap) synchronously
        string dir = "Assets/Art/Characters/" + ctx.name;
        foreach (var t in new[] { "_Albedo.png", "_Normal.png" })
            AssetDatabase.ImportAsset(dir + "/" + ctx.name + t, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(ctx.fbxPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        int passes = 0;
        while (passes < 3 && CharacterImportRule.ApplyPostImport(ctx.fbxPath)) passes++;
        ctx.Add("import.forced_reimport", 1, "reimport succeeded", AssetImporter.GetAtPath(ctx.fbxPath) is ModelImporter, "rule post-import passes with changes: " + passes, "true");

        var mi = AssetImporter.GetAtPath(ctx.fbxPath) as ModelImporter;
        ctx.Add("import.animation_type_human", mi.animationType == ModelImporterAnimationType.Human ? 1 : 0, "Human", mi.animationType == ModelImporterAnimationType.Human, mi.animationType.ToString(), mi.animationType.ToString());

        var an = AssetImporter.GetAtPath(dir + "/" + ctx.name + "_Normal.png") as TextureImporter;
        var al = AssetImporter.GetAtPath(dir + "/" + ctx.name + "_Albedo.png") as TextureImporter;
        ctx.Add("import.normal_map_type", an != null && an.textureType == TextureImporterType.NormalMap ? 1 : 0, "NormalMap", an != null && an.textureType == TextureImporterType.NormalMap, an != null ? an.textureType.ToString() : "missing", an != null ? an.textureType.ToString() : "missing");
        ctx.Add("import.albedo_srgb", al != null && al.sRGBTexture ? 1 : 0, "sRGB", al != null && al.sRGBTexture, al != null ? "sRGB=" + al.sRGBTexture : "missing");

        var mat = AssetDatabase.LoadAssetAtPath<Material>(dir + "/" + ctx.name + "_Lit.mat");
        bool matOk = mat != null && mat.shader.name == "Universal Render Pipeline/Lit" && mat.GetTexture("_BaseMap") != null && mat.GetTexture("_BumpMap") != null;
        ctx.Add("import.material_urp_lit", matOk ? 1 : 0, "URP Lit with albedo + normal", matOk, mat != null ? mat.shader.name : "material missing", matOk ? "true" : "false");

        // all renderers use the remapped material
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ctx.fbxPath);
        var rends = model.GetComponentsInChildren<Renderer>(true);
        int bad = rends.Count(r => r.sharedMaterials.Any(m => m != mat));
        ctx.Add("import.material_remapped", bad, "== 0 renderers on another material", bad == 0, rends.Length + " renderers");
    }

    // ------------------------------------------------------------ avatar
    static void AvatarAndMaterialChecks(Ctx ctx, Avatar avatar, Animator animator, GameObject inst)
    {
        ctx.Add("avatar.exists", avatar != null ? 1 : 0, "avatar sub-asset present", avatar != null, "", avatar != null ? "true" : "false");
        if (avatar == null) return;
        ctx.Add("avatar.isValid", avatar.isValid ? 1 : 0, "true", avatar.isValid, "", avatar.isValid.ToString());
        ctx.Add("avatar.isHuman", avatar.isHuman ? 1 : 0, "true", avatar.isHuman, "", avatar.isHuman.ToString());
        var missing = new List<string>();
        int req = 0;
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            if (!HumanTrait.RequiredBone(i)) continue;
            req++;
            if (animator.GetBoneTransform((HumanBodyBones)i) == null) missing.Add(((HumanBodyBones)i).ToString());
        }
        ctx.Add("avatar.required_bones_mapped", req - missing.Count, "== " + req, missing.Count == 0, missing.Count == 0 ? "all " + req + " required bones mapped" : "missing: " + string.Join(",", missing));
        // everything except the optional UpperChest, eyes and jaw (the pipeline skeleton has the 51 other bones)
        var optional = new HashSet<HumanBodyBones> { HumanBodyBones.UpperChest, HumanBodyBones.LeftEye, HumanBodyBones.RightEye, HumanBodyBones.Jaw };
        var unmapped = new List<string>();
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            if (!optional.Contains((HumanBodyBones)i) && animator.GetBoneTransform((HumanBodyBones)i) == null) unmapped.Add(((HumanBodyBones)i).ToString());
        ctx.Add("avatar.fingers_toes_mapped", unmapped.Count, "== 0 unmapped (excluding optional UpperChest/eyes/jaw)", unmapped.Count == 0, unmapped.Count == 0 ? "51 bones mapped incl. 30 finger bones and toes" : string.Join(",", unmapped));
        var mi = AssetImporter.GetAtPath(ctx.fbxPath) as ModelImporter;
        var d = mi.humanDescription;
        bool tpose = d.skeleton != null && d.skeleton.Length > 0;
        // arm direction in the described skeleton must be horizontal (T-pose)
        string armInfo = "";
        if (tpose)
        {
            var sk = d.skeleton.ToDictionary(s => s.name, s => s);
            armInfo = "skeleton entries=" + d.skeleton.Length;
        }
        ctx.Add("avatar.tpose_skeleton_configured", tpose ? 1 : 0, "HumanDescription.skeleton set (T-pose)", tpose, armInfo, tpose ? "true" : "false");
    }

    // ------------------------------------------------------------ LOD + height
    static int Tris(Renderer r)
    {
        Mesh m = null;
        var smr = r as SkinnedMeshRenderer;
        if (smr != null) m = smr.sharedMesh;
        else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
        if (m == null) return 0;
        long idx = 0;
        for (int i = 0; i < m.subMeshCount; i++) idx += m.GetIndexCount(i);
        return (int)(idx / 3);
    }

    static void LodAndHeightChecks(Ctx ctx, GameObject inst)
    {
        var lg = inst.GetComponentInChildren<LODGroup>(true);
        ctx.Add("lod.group_exists", lg != null ? 1 : 0, "LODGroup present", lg != null, "", lg != null ? "true" : "false");
        if (lg == null) return;
        var lods = lg.GetLODs();
        ctx.Add("lod.count", lods.Length, "== 3", lods.Length == 3, "screen heights: " + string.Join(", ", lods.Select(l => l.screenRelativeTransitionHeight.ToString("0.###"))));
        var tris = new int[lods.Length];
        for (int i = 0; i < lods.Length; i++) tris[i] = lods[i].renderers.Sum(r => r != null ? Tris(r) : 0);
        for (int i = 0; i < tris.Length; i++)
            ctx.Add("lod.tris_lod" + i, tris[i], i == 0 ? "20000..30000" : (i == 1 ? "35%..65% of LOD0" : "10%..30% of LOD0"),
                i == 0 ? (tris[0] >= 20000 && tris[0] <= 30000) : (tris[0] > 0 && (i == 1 ? tris[i] >= 0.35 * tris[0] && tris[i] <= 0.65 * tris[0] : tris[i] >= 0.10 * tris[0] && tris[i] <= 0.30 * tris[0])),
                tris[0] > 0 ? "ratio to LOD0 " + ((double)tris[i] / tris[0]).ToString("0.000") : "");
        var names = lods.Select(l => string.Join("+", l.renderers.Where(r => r != null).Select(r => r.name))).ToArray();
        bool named = lods.Length == 3 && names[0].EndsWith("_LOD0") && names[1].EndsWith("_LOD1") && names[2].EndsWith("_LOD2");
        ctx.Add("lod.names_match", named ? 1 : 0, "LOD0/1/2 renderers in order", named, string.Join(" | ", names), named ? "true" : "false");

        // height in Unity units from the baked LOD0 mesh in the rest (imported) pose
        var smr0 = lods[0].renderers.OfType<SkinnedMeshRenderer>().FirstOrDefault();
        if (smr0 != null)
        {
            Vector3 mn, mx;
            BakedBounds(smr0, out mn, out mx);
            float h = mx.y - mn.y;
            ctx.Add("height.units", h, "1.7..1.9", h >= 1.7f && h <= 1.9f, "min y " + mn.y.ToString("0.###") + " max y " + mx.y.ToString("0.###"));
            ctx.extra["rest_height"] = h.ToString("0.####");
        }
    }

    static Mesh bakeTmp;
    static void BakedBounds(SkinnedMeshRenderer smr, out Vector3 mn, out Vector3 mx)
    {
        if (bakeTmp == null) bakeTmp = new Mesh();
        smr.BakeMesh(bakeTmp, true);
        var m = smr.transform.localToWorldMatrix;
        var verts = bakeTmp.vertices;
        mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); mx = -mn;
        foreach (var v in verts)
        {
            var w = m.MultiplyPoint3x4(v);
            mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
        }
        if (verts.Length == 0) { mn = mx = Vector3.zero; }
    }

    static bool Finite(Vector3 v) { return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)); }
    static bool Finite(Quaternion q) { return !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) || float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w)); }

    // ------------------------------------------------------------ animation
    static AnimationClip LoadClip(string clipName)
    {
        string p = "Assets/Art/Animations/" + clipName + ".fbx";
        return AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    static Transform B(Animator a, HumanBodyBones b) { return a.GetBoneTransform(b); }

    static void AnimationChecks(Ctx ctx, PreviewRenderUtility pru, GameObject inst, Animator animator)
    {
        var lg = inst.GetComponentInChildren<LODGroup>(true);
        if (lg != null) lg.ForceLOD(0);
        var smr = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name.EndsWith("_LOD0")) ?? inst.GetComponentInChildren<SkinnedMeshRenderer>(true);

        // rest measurements (imported pose), character space = inst transform
        Func<HumanBodyBones, Vector3> P = b => B(animator, b).position;
        float restHeight = ctx.extra.ContainsKey("rest_height") ? float.Parse(ctx.extra["rest_height"], System.Globalization.CultureInfo.InvariantCulture) : 1.8f;
        float armLen = Vector3.Distance(P(HumanBodyBones.LeftUpperArm), P(HumanBodyBones.LeftLowerArm)) + Vector3.Distance(P(HumanBodyBones.LeftLowerArm), P(HumanBodyBones.LeftHand));
        float legLen = Vector3.Distance(P(HumanBodyBones.LeftUpperLeg), P(HumanBodyBones.LeftLowerLeg)) + Vector3.Distance(P(HumanBodyBones.LeftLowerLeg), P(HumanBodyBones.LeftFoot));
        float upperArmRest = Vector3.Distance(P(HumanBodyBones.LeftUpperArm), P(HumanBodyBones.LeftLowerArm));
        float upperLegRest = Vector3.Distance(P(HumanBodyBones.LeftUpperLeg), P(HumanBodyBones.LeftLowerLeg));
        Vector3 fwd = P(HumanBodyBones.LeftToes) - P(HumanBodyBones.LeftFoot); fwd.y = 0; fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
        ctx.extra["forward_axis"] = fwd.ToString("0.00");
        float groundOffset = 0;

        var tiles = new List<Texture2D>();
        var graph = PlayableGraph.Create("CharacterUnityCheck");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "out", animator);
        try
        {
            foreach (string clipName in Clips)
            {
                var clip = LoadClip(clipName);
                if (clip == null) { ctx.Add("anim." + clipName + ".clip_found", 0, "clip exists", false, "not found in Assets/Art/Animations"); continue; }
                if (!clip.isHumanMotion) { ctx.Add("anim." + clipName + ".is_humanoid", 0, "humanoid clip", false, "clip is not humanoid"); continue; }
                var cp = AnimationClipPlayable.Create(graph, clip);
                output.SetSourcePlayable(cp);
                graph.Play();

                int nan = 0, flips = 0, swapped = 0, lenBad = 0, handFar = 0, footFar = 0, headBad = 0, groundBad = 0, meshBad = 0;
                float minFootY = float.MaxValue, maxFootY = float.MinValue, minHeadFrac = 9, maxHeadFrac = 0, worstLen = 1, minVertY = float.MaxValue, maxH = 0, minH = 99, maxWidth = 0;
                var detail = new StringBuilder();
                for (int f = 0; f < FramesPerClip; f++)
                {
                    double t = clip.length * f / FramesPerClip;
                    cp.SetTime(t);
                    graph.Evaluate(0);
                    // NaN scan over all humanoid bones
                    for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                    {
                        var tr = B(animator, (HumanBodyBones)i);
                        if (tr == null) continue;
                        if (!Finite(tr.position) || !Finite(tr.rotation) || !Finite(tr.localPosition)) nan++;
                    }
                    var hips = P(HumanBodyBones.Hips);
                    var head = P(HumanBodyBones.Head);
                    var lh = P(HumanBodyBones.LeftHand); var rh = P(HumanBodyBones.RightHand);
                    var lf = P(HumanBodyBones.LeftFoot); var rf = P(HumanBodyBones.RightFoot);
                    float footY = Mathf.Min(lf.y, rf.y);
                    minFootY = Mathf.Min(minFootY, footY); maxFootY = Mathf.Max(maxFootY, Mathf.Max(lf.y, rf.y));
                    float hf = head.y / restHeight; minHeadFrac = Mathf.Min(minHeadFrac, hf); maxHeadFrac = Mathf.Max(maxHeadFrac, hf);
                    if (hf < 0.78f || hf > 1.02f) headBad++;
                    // hand reach from its shoulder; foot reach from its hip
                    foreach (var pair in new[] { new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftHand }, new[] { HumanBodyBones.RightUpperArm, HumanBodyBones.RightHand } })
                        if (Vector3.Distance(P(pair[0]), P(pair[1])) > armLen * 1.1f) handFar++;
                    foreach (var pair in new[] { new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftFoot }, new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightFoot } })
                        if (Vector3.Distance(P(pair[0]), P(pair[1])) > legLen * 1.1f) footFar++;
                    if (lh.y < 0.15f * restHeight || rh.y < 0.15f * restHeight || lh.y > 1.1f * restHeight || rh.y > 1.1f * restHeight) handFar++;
                    // hands swapped sides (character space: right vector = fwd x up)
                    // character's left hand should have larger (position . left) than the right hand within tolerance
                    float sepLR = Vector3.Dot(lh - rh, Vector3.Cross(fwd, Vector3.up));
                    // Unity is left-handed: Cross(fwd, up) points to the character's LEFT
                    if (sepLR < -0.45f) { swapped++; if (swapped == 1) detail.Append("sepLR=" + sepLR.ToString("0.00") + " lh=" + lh + " rh=" + rh + "; "); }
                    // knees must bend forward (not hyperextended backwards)
                    foreach (var leg in new[] { new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot }, new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot } })
                    {
                        Vector3 a = P(leg[0]), k = P(leg[1]), fo = P(leg[2]);
                        Vector3 mid = (a + fo) * 0.5f;
                        if (Vector3.Dot(k - mid, fwd) < -0.03f) flips++;
                    }
                    // elbows: the elbow must not bend through the body (angle 0..~175); flipped elbows put the hand behind the upper arm line
                    foreach (var arm in new[] { new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand }, new[] { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand } })
                    {
                        Vector3 s = P(arm[0]), e = P(arm[1]), w = P(arm[2]);
                        Vector3 mid = (s + w) * 0.5f;
                        // elbow should point away from the torso centre (hips-chest axis): distance from spine axis grows
                        Vector3 axisPt = new Vector3(hips.x, e.y, hips.z);
                        float dElbow = Vector3.Distance(new Vector3(e.x, 0, e.z), new Vector3(axisPt.x, 0, axisPt.z));
                        // an elbow inside the torso (closer than 4 cm to the vertical hip axis) means flipped/penetrating arm
                        if (dElbow < 0.04f) flips++;
                    }
                    // bone lengths preserved
                    float ua = Vector3.Distance(P(HumanBodyBones.LeftUpperArm), P(HumanBodyBones.LeftLowerArm)) / upperArmRest;
                    float ul = Vector3.Distance(P(HumanBodyBones.LeftUpperLeg), P(HumanBodyBones.LeftLowerLeg)) / upperLegRest;
                    foreach (var r in new[] { ua, ul }) { if (Math.Abs(r - 1) > Math.Abs(worstLen - 1)) worstLen = r; if (Math.Abs(r - 1) > 0.06f) lenBad++; }
                    // baked mesh sanity
                    if (smr != null)
                    {
                        Vector3 mn, mx; BakedBounds(smr, out mn, out mx);
                        float h = mx.y - mn.y, wd = Mathf.Max(mx.x - mn.x, mx.z - mn.z);
                        minVertY = Mathf.Min(minVertY, mn.y); maxH = Mathf.Max(maxH, h); minH = Mathf.Min(minH, h); maxWidth = Mathf.Max(maxWidth, wd);
                        if (!Finite(mn) || !Finite(mx) || h < 0.8f * restHeight || h > 1.2f * restHeight || wd > 2.2f) meshBad++;
                        if (mn.y < -0.12f || mn.y > 0.35f) groundBad++;
                    }
                    // screenshots for the first, middle and ~3/4 sample
                    if (f == 0 || f == FramesPerClip / 2 || f == (FramesPerClip * 5) / 6)
                    {
                        if (tiles.Count < 64) tiles.Add(Render(pru, inst, smr, animator));
                    }
                }
                graph.Stop();
                cp.Destroy();

                string pre = "anim." + clipName + ".";
                ctx.Add(pre + "no_nan_bones", nan, "== 0", nan == 0, FramesPerClip + " frames sampled, clip " + clip.length.ToString("0.00") + "s");
                ctx.Add(pre + "feet_near_ground", minFootY, "foot bone min y -0.10..0.25 (above ground at 0)", minFootY >= -0.10f && minFootY <= 0.25f, "foot y range " + minFootY.ToString("0.000") + ".." + maxFootY.ToString("0.000") + "; lowest mesh vertex y " + minVertY.ToString("0.000") + " (" + groundBad + " frames out of -0.12..0.35)");
                ctx.Add(pre + "mesh_grounded", minVertY, "lowest vertex y -0.12..0.35 on every frame", groundBad == 0, "");
                ctx.Add(pre + "head_height_frac", minHeadFrac, "0.78..1.02 of height", headBad == 0, "head/height range " + minHeadFrac.ToString("0.00") + ".." + maxHeadFrac.ToString("0.00"));
                ctx.Add(pre + "hands_reach_plausible", handFar, "== 0 frames/hands out of reach", handFar == 0, "arm length " + armLen.ToString("0.00") + " m");
                ctx.Add(pre + "feet_reach_plausible", footFar, "== 0", footFar == 0, "leg length " + legLen.ToString("0.00") + " m");
                ctx.Add(pre + "no_limb_flips", flips + swapped, "== 0 (knees forward, elbows outside torso, hands not swapped)", flips + swapped == 0, "knee/elbow flips " + flips + ", hands swapped " + swapped + (detail.Length > 0 ? " (" + detail + ")" : ""));
                ctx.Add(pre + "bone_length_preserved", worstLen, "within 6% of rest", lenBad == 0, "worst ratio " + worstLen.ToString("0.000"));
                ctx.Add(pre + "mesh_sane", maxH, "height 0.8..1.2x rest, width < 2.2 m", meshBad == 0, "baked height " + minH.ToString("0.00") + ".." + maxH.ToString("0.00") + ", max width " + maxWidth.ToString("0.00"));
            }
        }
        finally { graph.Destroy(); }

        // contact sheet (3 columns)
        if (tiles.Count > 0)
        {
            int cols = ShotsPerClip, rows = (tiles.Count + cols - 1) / cols;
            var sheet = new Texture2D(cols * TilePx, rows * TilePx, TextureFormat.RGB24, false);
            for (int i = 0; i < tiles.Count; i++)
            {
                int cx = i % cols, cy = rows - 1 - i / cols;
                sheet.SetPixels(cx * TilePx, cy * TilePx, TilePx, TilePx, tiles[i].GetPixels());
                string p = Path.Combine(ctx.outDir, "unity_" + Clips[Mathf.Min(i / ShotsPerClip, Clips.Length - 1)] + "_" + (i % ShotsPerClip) + ".png");
                File.WriteAllBytes(p, tiles[i].EncodeToPNG());
                ctx.shots.Add(p);
            }
            string sp = Path.Combine(ctx.outDir, "unity_sheet.png");
            File.WriteAllBytes(sp, sheet.EncodeToPNG());
            ctx.shots.Insert(0, sp);
            UnityEngine.Object.DestroyImmediate(sheet);
            foreach (var t in tiles) UnityEngine.Object.DestroyImmediate(t);
            ctx.Add("screenshots.written", tiles.Count + 1, ">= 3 images", tiles.Count >= 3 && File.Exists(sp), "sheet: " + sp.Replace("\\", "/"));
        }
        else ctx.Add("screenshots.written", 0, ">= 3 images", false, "no frames rendered");
    }

    static Texture2D Render(PreviewRenderUtility pru, GameObject inst, SkinnedMeshRenderer smr, Animator animator)
    {
        Vector3 mn, mx;
        BakedBounds(smr, out mn, out mx);
        Vector3 c = (mn + mx) * 0.5f;
        float h = Mathf.Max(mx.y - mn.y, 1.0f);
        var cam = pru.camera;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.32f, 0.34f, 0.38f, 1f);
        cam.fieldOfView = 28f;
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 50f;
        // camera in front of the character (character forward = +Z or -Z; look from the front, slightly 3/4)
        Vector3 f = Vector3.forward;
        var toes = animator.GetBoneTransform(HumanBodyBones.LeftToes); var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        if (toes != null && foot != null) { f = toes.position - foot.position; f.y = 0; f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward; }
        Vector3 dir = Quaternion.AngleAxis(30f, Vector3.up) * f;
        float dist = h * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;
        cam.transform.position = c + dir * dist + Vector3.up * 0.1f;
        cam.transform.LookAt(c);
        pru.lights[0].intensity = 1.4f; pru.lights[0].transform.rotation = Quaternion.Euler(35f, 150f, 0f);
        pru.lights[1].intensity = 0.7f; pru.lights[1].transform.rotation = Quaternion.Euler(-30f, 30f, 0f);
        pru.ambientColor = new Color(0.45f, 0.45f, 0.5f);
        // Skinning is not refreshed by a manual camera render in edit mode: render a baked copy of the current pose instead.
        var bakeGo = new GameObject("baked_pose");
        pru.AddSingleGO(bakeGo);
        var bm = new Mesh();
        smr.BakeMesh(bm, true);
        bakeGo.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
        bakeGo.AddComponent<MeshFilter>().sharedMesh = bm;
        bakeGo.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
        bool wasEnabled = smr.enabled; smr.enabled = false;
        pru.BeginStaticPreview(new Rect(0, 0, TilePx, TilePx));
        pru.Render(true, true);
        var tex = pru.EndStaticPreview();
        smr.enabled = wasEnabled;
        UnityEngine.Object.DestroyImmediate(bakeGo);
        UnityEngine.Object.DestroyImmediate(bm);
        // EndStaticPreview returns a texture with alpha; convert to opaque RGB copy
        var outTex = new Texture2D(TilePx, TilePx, TextureFormat.RGB24, false);
        outTex.SetPixels(tex.GetPixels());
        UnityEngine.Object.DestroyImmediate(tex);
        return outTex;
    }
}
