using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    // The MPFB characters: the courier (player) and the hollow (enemy). Humanoid rigs, URP Lit materials from the
    // MPFB textures, accessories fitted to the head and back, and bone lookups through the avatar.
    public static partial class SceneBuilder
    {
        const string MpfbTex = "Characters/MPFB/";

        // ------------------------------------------------------------------ materials

        enum AlphaMode { Opaque, Clip }

        // URP Lit from MPFB maps: <albedo>_Albedo (+ optional normal and AO), tinted, with alpha clip and two-sided for the cards.
        static Material MpfbMat(string name, string albedo, string normal, string ao, Color tint, float smooth,
            AlphaMode alpha = AlphaMode.Opaque, float cutoff = 0.5f, bool twoSided = false, Color? emission = null, bool emissionFromAlbedo = false)
        {
            if (_mats.TryGetValue(name, out var cached)) return cached;
            var mat = LoadOrCreateMat(name, _litShader);
            mat.SetColor("_BaseColor", tint);
            mat.color = tint;
            mat.SetTexture("_BaseMap", Tex(MpfbTex + albedo));
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
            var nm = normal != null ? Tex(MpfbTex + normal) : null;
            mat.SetTexture("_BumpMap", nm);
            mat.SetFloat("_BumpScale", 1f);
            if (nm != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
            var occ = ao != null ? Tex(MpfbTex + ao) : null;
            mat.SetTexture("_OcclusionMap", occ);
            mat.SetFloat("_OcclusionStrength", 1f);
            if (occ != null) mat.EnableKeyword("_OCCLUSIONMAP"); else mat.DisableKeyword("_OCCLUSIONMAP");
            mat.SetTexture("_MetallicGlossMap", null);
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", smooth);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Surface", 0f);
            bool clip = alpha == AlphaMode.Clip;
            mat.SetFloat("_AlphaClip", clip ? 1f : 0f);
            mat.SetFloat("_Cutoff", cutoff);
            if (clip) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
            if (mat.HasProperty("_AlphaToMask")) mat.SetFloat("_AlphaToMask", clip && twoSided ? 1f : 0f);
            mat.SetFloat("_Cull", twoSided ? (float)CullMode.Off : (float)CullMode.Back);
            mat.renderQueue = clip ? (int)RenderQueue.AlphaTest : -1;
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.SetTexture("_EmissionMap", emissionFromAlbedo ? Tex(MpfbTex + albedo) : null);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
                mat.SetTexture("_EmissionMap", null);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(mat);
            _mats[name] = mat;
            return mat;
        }

        // ------------------------------------------------------------------ the characters

        static Dictionary<string, Material> CourierMaterials()
        {
            return new Dictionary<string, Material>
            {
                { "Skin", MpfbMat("Courier_Skin", "Courier_Skin_Albedo", null, null, Color.white, 0.4f) },
                { "Eyes", MpfbMat("Courier_Eyes", "Courier_Eyes_Albedo", null, null, Color.white, 0.75f) },
                { "Brows", MpfbMat("Courier_Brows", "Courier_Brows_Albedo", null, null, Color.white, 0.2f, AlphaMode.Clip, 0.3f, true) },
                { "Lashes", MpfbMat("Courier_Lashes", "Courier_Lashes_Albedo", null, null, Color.white, 0.2f, AlphaMode.Clip, 0.3f, true) },
                { "Hair", MpfbMat("Courier_Hair", "Courier_Hair_Albedo", "Courier_Hair_Normal", null, Color.white, 0.3f, AlphaMode.Clip, 0.5f, true) },
                // Worn, darker teal jacket: the albedo times a muted tint.
                { "Top", MpfbMat("Courier_Top", "Courier_Top_Albedo", "Courier_Clothes_Normal", "Courier_Clothes_AO", CourierJacketTint, 0.2f) },
                { "Pants", MpfbMat("Courier_Pants", "Courier_Pants_Albedo", "Courier_Clothes_Normal", "Courier_Clothes_AO", CourierJeansTint, 0.2f) },
                { "Shoes", MpfbMat("Courier_Shoes", "Courier_Shoes_Albedo", null, null, Color.white, 0.3f) },
            };
        }

        static readonly Color CourierJacketTint = new Color(0.6f, 0.66f, 0.66f);
        static readonly Color CourierJeansTint = new Color(0.75f, 0.75f, 0.8f);

        static Dictionary<string, Material> HollowMaterials()
        {
            return new Dictionary<string, Material>
            {
                { "Skin", MpfbMat("Hollow_Skin", "Hollow_Skin_Albedo", null, null, new Color(0.85f, 0.85f, 0.85f), 0.35f) },
                // Whole eye is tinted red and emissive: the signature read at distance.
                { "Eyes", MpfbMat("Hollow_Eyes", "Hollow_Eyes_Albedo", null, null, new Color(1f, 0.1f, 0.08f), 0.5f, AlphaMode.Opaque, 0.5f, false,
                    Palette.EnemyEyeRed * 1.6f, true) },
                { "Brows", MpfbMat("Hollow_Brows", "Hollow_Brows_Albedo", null, null, Color.white, 0.2f, AlphaMode.Clip, 0.3f, true) },
                { "Hair", MpfbMat("Hollow_Hair", "Hollow_Hair_Albedo", null, null, Color.white, 0.3f, AlphaMode.Clip, 0.5f, true) },
                { "Top", MpfbMat("Hollow_Top", "Hollow_Top_Albedo", "Hollow_Clothes_Normal", "Hollow_Clothes_AO", new Color(0.45f, 0.42f, 0.5f), 0.15f) },
                { "Pants", MpfbMat("Hollow_Pants", "Hollow_Pants_Albedo", "Hollow_Clothes_Normal", "Hollow_Clothes_AO", new Color(0.5f, 0.5f, 0.55f), 0.15f) },
            };
        }

        // Instantiates an MPFB model with its Humanoid avatar and controller, drops the teeth and tongue, and maps the
        // materials by their <Char>_<Part> names.
        static GameObject BuildMpfb(string model, Transform parent, string controllerPath, bool cullTransforms, Dictionary<string, Material> mats)
        {
            var go = (GameObject)Object.Instantiate(Model(model));
            go.name = model;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            var avatar = AvatarAt(AssetDatabase.GetAssetPath(Model(model)));
            if (avatar == null || !avatar.isHuman) throw new InvalidOperationException(model + " has no humanoid avatar.");
            animator.avatar = avatar;
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = cullTransforms ? AnimatorCullingMode.CullUpdateTransforms : AnimatorCullingMode.AlwaysAnimate;

            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // One renderer can carry several materials (the clothes mesh has the top and the pants).
                var shared = smr.sharedMaterials;
                bool drop = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    string part = MpfbPart(shared[i] != null ? shared[i].name : smr.name);
                    if (part == "Teeth" || part == "Tongue") { drop = true; break; }
                    if (!mats.TryGetValue(part, out var m))
                        throw new InvalidOperationException(model + ": no material for part '" + part + "' on " + smr.name);
                    shared[i] = m;
                }
                if (drop) { Object.DestroyImmediate(smr.gameObject); continue; }
                smr.sharedMaterials = shared;
                smr.updateWhenOffscreen = false;
                smr.quality = SkinQuality.Bone4;
                string first = MpfbPart(shared[0].name);
                smr.shadowCastingMode = first == "Brows" || first == "Lashes" || first == "Eyes" ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }
            return go;
        }

        static bool HasPart(Renderer r, string part)
        {
            foreach (var m in r.sharedMaterials) if (m != null && MpfbPart(m.name) == part) return true;
            return false;
        }

        static string MpfbPart(string materialName)
        {
            int dot = materialName.IndexOf('.');
            if (dot > 0) materialName = materialName.Substring(0, dot);
            int us = materialName.LastIndexOf('_');
            return us >= 0 ? materialName.Substring(us + 1) : materialName;
        }

        // A bone of the humanoid avatar (never by name).
        static Transform HBone(GameObject character, HumanBodyBones bone)
        {
            var t = character.GetComponent<Animator>().GetBoneTransform(bone);
            if (t == null) throw new InvalidOperationException("Humanoid bone " + bone + " is not mapped on " + character.name);
            return t;
        }

        // Spine2 (UpperChest) when the avatar maps it, else Chest.
        static Transform UpperTorso(GameObject character)
        {
            var a = character.GetComponent<Animator>();
            return a.GetBoneTransform(HumanBodyBones.UpperChest) ?? HBone(character, HumanBodyBones.Chest);
        }

        // World bounds of the skin mesh in the rest pose.
        static Bounds SkinRestBounds(GameObject character)
        {
            foreach (var smr in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (HasPart(smr, "Skin")) return smr.bounds;
            throw new InvalidOperationException("No skin renderer on " + character.name);
        }

        // Bounds of the baked skin vertices at or above a world height (rest pose): the head and neck.
        static Bounds SkinBoundsAbove(GameObject character, float minY)
        {
            foreach (var smr in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!HasPart(smr, "Skin")) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                var m = smr.transform.localToWorldMatrix;
                bool first = true;
                Bounds b = default;
                foreach (var v in baked.vertices)
                {
                    var w = m.MultiplyPoint3x4(v);
                    if (w.y < minY) continue;
                    if (first) { b = new Bounds(w, Vector3.zero); first = false; }
                    else b.Encapsulate(w);
                }
                Object.DestroyImmediate(baked);
                return b;
            }
            throw new InvalidOperationException("No skin renderer on " + character.name);
        }

        // ------------------------------------------------------------------ player / enemy separation

        // Enemies' bone hitbox colliders must never push the CharacterController: Player and Enemy do not collide (queries
        // such as bullets and blasts are unaffected). The crowd stays solid through a non-carving NavMeshObstacle on the player.
        static void ConfigurePhysicsLayers()
        {
            Physics.IgnoreLayerCollision(GameLayers.Player, GameLayers.Enemy, true);
            // The editor call writes the layer collision matrix in ProjectSettings/DynamicsManager.asset.
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");
            if (assets != null && assets.Length > 0) EditorUtility.SetDirty(assets[0]);
            AssetDatabase.SaveAssets();
            if (!Physics.GetIgnoreLayerCollision(GameLayers.Player, GameLayers.Enemy))
                throw new InvalidOperationException("Player/Enemy layer collision is still enabled.");
        }

        static void AddPlayerCrowdObstacle(GameObject root)
        {
            var ob = root.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            ob.shape = UnityEngine.AI.NavMeshObstacleShape.Capsule;
            ob.carving = false;
            ob.center = new Vector3(0f, 0.9f, 0f);
            ob.radius = 0.6f;       // a little wider than the body, so the crowd keeps its distance
            ob.height = 1.8f;
        }

        // ------------------------------------------------------------------ courier

        static GameObject BuildCourier(Transform parent)
        {
            var go = BuildMpfb("MPFB_Courier", parent, PlayerControllerPath, false, CourierMaterials());
            var head = HBone(go, HumanBodyBones.Head);
            var gearLook = new Look { Override = new Dictionary<string, Material> { { "Cap", mTerracotta }, { "Bag", mTerracotta } } };
            FitCap(go, head, gearLook);
            FitBackpack(go, gearLook);
            return go;
        }

        // Accessory models are authored upright with the pivot at the head (or spine) bone of the old rig. Placed with the
        // character's orientation, uniformly scaled, with the model origin at a world rest-pose position.
        static GameObject PlaceAccessory(string model, Transform bone, Transform character, Look look, Vector3 scale, Vector3 originWorld,
            Action<GameObject> prepare = null)
        {
            var acc = Spawn(model, bone, Vector3.zero, 0f, look, model);
            foreach (var c in acc.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            prepare?.Invoke(acc);
            acc.transform.SetPositionAndRotation(originWorld, character.rotation);
            acc.transform.localScale = scale;
            return acc;
        }

        static Bounds RendererBounds(GameObject go)
        {
            bool first = true;
            Bounds b = default;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // Char2_Cap mesh (model space, pivot at the old head bone): crown 0.264 wide, top at y 0.219, crown centred on x = z = 0.
        const float CapModelWidth = 0.264f;
        const float CapModelTop = 0.219f;
        // Tunables, fitted against the captures: crown width over the hair width, and how far the crown top sits above the hair.
        const float CapWidthOverHair = 1.3f;
        const float CapRaise = 0.0f;
        const float CapHeightScale = 1.0f;

        static void FitCap(GameObject go, Transform head, Look look)
        {
            var hair = HairBounds(go);
            var scale = new Vector3(1f, CapHeightScale, 1f) * (hair.size.x * CapWidthOverHair / CapModelWidth);
            scale.y = scale.x * CapHeightScale;
            var skull = SkinBoundsAbove(go, head.position.y);
            var origin = new Vector3(skull.center.x, skull.max.y - CapModelTop * scale.y, (skull.center.z + hair.center.z) * 0.5f + CapForward);
            // Raise the cap until no skull or hair vertex pokes through the dome.
            var points = new List<Vector3>();
            CollectWorldVertices(go, "Skin", head.position.y, points);
            origin.y += CapClearance(Model("Char2_Cap").GetComponentInChildren<MeshFilter>().sharedMesh, scale, origin, points) + CapRaise;
            PlaceAccessory("Char2_Cap", head, go.transform, look, scale, origin);
            float rim = origin.y + CapModelRim * scale.y;
            CutHairAbove(go, rim + HairCutAboveRim);
            BuildSkullCap(go, head, rim, look.Override["Cap"]);
        }

        const string SkullCapPath = ModelDir + "/Characters/Generated/Courier_SkullCap.asset";
        const float SkullCapThickness = 0.012f;

        // The crown of the cap is a shell cut from the head itself: every skin triangle above the rim, pushed out along its
        // normal, rigid on the Head bone. It always fits the skull; the cap model supplies the bill (and a second, loose crown
        // under this one).
        static void BuildSkullCap(GameObject go, Transform head, float rimY, Material material)
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!HasPart(smr, "Skin")) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                var m = smr.transform.localToWorldMatrix;
                var verts = baked.vertices;
                var normals = baked.normals;
                var world = new Vector3[verts.Length];
                for (int i = 0; i < verts.Length; i++) world[i] = m.MultiplyPoint3x4(verts[i]);
                var map = new Dictionary<int, int>();
                var pos = new List<Vector3>();
                var nor = new List<Vector3>();
                var tris = new List<int>();
                var src = baked.triangles;
                for (int t = 0; t < src.Length; t += 3)
                {
                    int a = src[t], b = src[t + 1], c = src[t + 2];
                    if (world[a].y < rimY || world[b].y < rimY || world[c].y < rimY) continue;
                    foreach (int idx in new[] { a, b, c })
                    {
                        if (!map.TryGetValue(idx, out int n))
                        {
                            n = pos.Count;
                            map[idx] = n;
                            var wn = m.MultiplyVector(normals[idx]).normalized;
                            pos.Add(head.InverseTransformPoint(world[idx] + wn * SkullCapThickness));
                            nor.Add(head.InverseTransformDirection(wn));
                        }
                        tris.Add(n);
                    }
                }
                Object.DestroyImmediate(baked);
                var mesh = new Mesh { name = "Courier_SkullCap" };
                mesh.SetVertices(pos);
                mesh.SetNormals(nor);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                EnsureFolder(ModelDir + "/Characters/Generated");
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(SkullCapPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(mesh, existing);
                    Object.DestroyImmediate(mesh);
                    mesh = existing;
                }
                else AssetDatabase.CreateAsset(mesh, SkullCapPath);
                var cap = new GameObject("Courier_SkullCap");
                cap.transform.SetParent(head, false);
                cap.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = cap.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                return;
            }
        }

        // Char2_Cap: the dome's lowest edge (model space).
        const float CapModelRim = 0.119f;
        const float HairCutAboveRim = 0.012f;

        const string HairFitPath = ModelDir + "/Characters/Generated/Courier_Hair_Fit.asset";

        // The hair is a thick shell that would poke through the cap's crown. The triangles above the cap's rim are cut (the
        // cap covers that part of the head anyway); saved as a mesh copy, weights and bind poses unchanged.
        static void CutHairAbove(GameObject go, float worldY)
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!HasPart(smr, "Hair")) continue;
                var mesh = Object.Instantiate(smr.sharedMesh);
                mesh.name = "Courier_Hair_Fit";
                var v = mesh.vertices;
                float cut = smr.transform.InverseTransformPoint(new Vector3(0f, worldY, 0f)).y;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var tris = mesh.GetTriangles(sub);
                    var kept = new List<int>(tris.Length);
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        if (v[tris[i]].y > cut && v[tris[i + 1]].y > cut && v[tris[i + 2]].y > cut) continue;
                        kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]);
                    }
                    mesh.SetTriangles(kept, sub);
                }
                EnsureFolder(ModelDir + "/Characters/Generated");
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(HairFitPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(mesh, existing);
                    Object.DestroyImmediate(mesh);
                    mesh = existing;
                }
                else AssetDatabase.CreateAsset(mesh, HairFitPath);
                smr.sharedMesh = mesh;
            }
        }

        static void CollectWorldVertices(GameObject go, string part, float minY, List<Vector3> into)
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!HasPart(smr, part)) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                var m = smr.transform.localToWorldMatrix;
                foreach (var v in baked.vertices)
                {
                    var w = m.MultiplyPoint3x4(v);
                    if (w.y >= minY) into.Add(w);
                }
                Object.DestroyImmediate(baked);
            }
        }

        // How far the cap must rise so every point under its dome sits below the dome surface. The dome surface is a height
        // field of the cap's vertices (cells of 2 cm, looking one cell around for the sparse mesh).
        static float CapClearance(Mesh cap, Vector3 scale, Vector3 origin, List<Vector3> points)
        {
            const float cell = 0.02f;
            var top = new Dictionary<Vector2Int, float>();
            var bottom = new Dictionary<Vector2Int, float>();
            foreach (var v in cap.vertices)
            {
                var k = new Vector2Int(Mathf.RoundToInt(v.x / cell), Mathf.RoundToInt(v.z / cell));
                top[k] = top.TryGetValue(k, out var t) ? Mathf.Max(t, v.y) : v.y;
                bottom[k] = bottom.TryGetValue(k, out var b) ? Mathf.Min(b, v.y) : v.y;
            }
            float shift = 0f;
            foreach (var w in points)
            {
                var k = new Vector2Int(Mathf.RoundToInt((w.x - origin.x) / scale.x / cell), Mathf.RoundToInt((w.z - origin.z) / scale.z / cell));
                float capTop = float.NegativeInfinity, capBottom = float.PositiveInfinity;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var n = new Vector2Int(k.x + dx, k.y + dz);
                        if (top.TryGetValue(n, out var tv)) capTop = Mathf.Max(capTop, tv);
                        if (bottom.TryGetValue(n, out var bv)) capBottom = Mathf.Min(capBottom, bv);
                    }
                if (float.IsNegativeInfinity(capTop)) continue;
                float topW = origin.y + capTop * scale.y, bottomW = origin.y + capBottom * scale.y;
                if (w.y > bottomW) shift = Mathf.Max(shift, w.y - topW);
            }
            return shift;
        }

        const float CapForward = 0.0f;

        static Bounds HairBounds(GameObject character)
        {
            foreach (var smr in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (HasPart(smr, "Hair")) return smr.bounds;
            throw new InvalidOperationException("No hair renderer on " + character.name);
        }

        const float PackWidth = 0.28f;

        static void FitBackpack(GameObject go, Look look)
        {
            var spine2 = UpperTorso(go);
            var spine = HBone(go, HumanBodyBones.Spine);
            var neck = HBone(go, HumanBodyBones.Neck);
            // Torso depth: back surface at the spine; the pack sits behind it.
            float yMid = Mathf.Lerp(spine.position.y, neck.position.y, 0.68f);
            // Char2_Backpack mesh: 0.389 wide, 0.507 tall, 0.489 deep, centre (0, 0.044, -0.117) from the pivot. The bag is
            // flattened to a day-pack's depth and set against the jacket's back.
            float sx = PackWidth / 0.389f;
            var scale = new Vector3(sx, sx, sx * PackDepthScale);
            float back = BackSurfaceZ(go, yMid);
            var origin = new Vector3(0f, yMid - 0.044f * scale.y, back + PackEmbed - 0.1275f * scale.z);
            PlaceAccessory("Char2_Backpack", spine2, go.transform, look, scale, origin);
        }

        const float PackDepthScale = 0.5f;
        const float PackEmbed = 0.085f;

        // The jacket's back surface (most negative z) at the pack's height, within 10 cm of the spine line, rest pose.
        static float BackSurfaceZ(GameObject go, float y)
        {
            var points = new List<Vector3>();
            CollectWorldVertices(go, "Top", float.NegativeInfinity, points);
            float z = float.MaxValue;
            foreach (var p in points)
                if (Mathf.Abs(p.x) < 0.1f && Mathf.Abs(p.y - y) < 0.12f) z = Mathf.Min(z, p.z);
            if (z == float.MaxValue) throw new InvalidOperationException("No jacket vertices at the pack height on " + go.name);
            return z;
        }

        // ------------------------------------------------------------------ hollow

        static GameObject BuildHollow(Transform parent)
        {
            var go = BuildMpfb("MPFB_Hollow", parent, EnemyControllerPath, true, HollowMaterials());
            var head = HBone(go, HumanBodyBones.Head);
            var skull = SkinBoundsAbove(go, head.position.y);
            // The hood covers the hair.
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (HasPart(smr, "Hair")) Object.DestroyImmediate(smr.gameObject);
            var look = new Look { Override = new Dictionary<string, Material> { { "Hood", Mat("EnemyHood", new Color(0.11f, 0.1f, 0.13f)) } } };
            // Char2_Hood head part (model space): 0.22 wide, top at y 0.22, centred at z -0.035.
            float scale = skull.size.x * HoodWidthOverSkull / 0.22f;
            var origin = new Vector3(skull.center.x, skull.max.y + HoodRaise - 0.22f * scale, skull.center.z + 0.035f * scale);
            PlaceAccessory("Char2_Hood", head, go.transform, look, Vector3.one * scale, origin, FitHoodMantle);
            return go;
        }

        const float HoodWidthOverSkull = 1.3f;
        const float HoodRaise = 0.025f;

        const string HoodFitPath = ModelDir + "/Characters/Generated/Char2_Hood_Fit.asset";

        // Char2_Hood is authored for the T-pose: its shoulder mantle runs out along the arms, and the hood is rigid on the
        // head, so with the arms down those wings would hang as loose flaps. The mantle (everything below the neck line or
        // out past the shoulders, in model space) is cut away, leaving the cowl; saved as a mesh asset.
        const float HoodNeckLine = -0.045f;
        const float HoodSideLimit = 0.13f;

        static void FitHoodMantle(GameObject hood)
        {
            var mf = hood.GetComponentInChildren<MeshFilter>();
            var src = mf.sharedMesh;
            var mesh = Object.Instantiate(src);
            mesh.name = "Char2_Hood_Fit";
            var v = mesh.vertices;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var tris = mesh.GetTriangles(sub);
                var kept = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    bool cut = false;
                    for (int k = 0; k < 3; k++)
                    {
                        var p = v[tris[i + k]];
                        if (p.y < HoodNeckLine || Mathf.Abs(p.x) > HoodSideLimit) { cut = true; break; }
                    }
                    if (cut) continue;
                    kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]);
                }
                mesh.SetTriangles(kept, sub);
            }
            mesh.RecalculateBounds();   // the authored normals stay: recomputing them splits the shading along every UV seam (a light diamond on the back)
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
    }
}
