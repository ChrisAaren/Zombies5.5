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

        // Worn canvas gear: the flat terracotta read as a glowing orange block in full sun.
        static Material CourierGearMat() { return Surf("S_CourierGear", new Color(0.33f, 0.215f, 0.16f), null, Vector2.one, 0.1f); }

        static Material CourierWebbingMat() { return Surf("S_CourierWebbing", new Color(0.075f, 0.08f, 0.075f), null, Vector2.one, 0.12f); }

        // The cap: faded navy cotton, so it reads as cloth instead of a painted helmet.
        static Material CourierCapMat() { return Surf("S_CourierCap", new Color(0.1f, 0.14f, 0.23f), null, Vector2.one, 0.08f); }

        static Shader _skinShader;

        // Skin on the Zombies55/Skin shader: wrapped diffuse with a warm terminator, tiled pore normal, face-zone shine and
        // cavity map, eye-socket tint. Built on MpfbMat so the albedo, tint and smoothness plumbing stay the same.
        static Material MpfbSkin(string name, string albedo, Color tint, float smooth, float shine, float socket, float desat,
            Color socketColor, Color sss, float sssStrength, float wrap,
            float pore = 0.8f, float stubble = 0f, float micro = 0f, float mottle = 0f, float cavity = 0.85f,
            float stubbleDots = 0f, float lipDesat = 0f, float lipSmooth = 1f, float poreTiling = 36f, Color? stubbleColor = null)
        {
            if (_mats.TryGetValue(name, out var cached)) return cached;
            var mat = MpfbMat(name, albedo, null, null, tint, smooth);
            if (_skinShader == null) _skinShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Shaders/Skin/Skin.shader");
            if (_skinShader == null) throw new InvalidOperationException("Shader 'Zombies55/Skin' not found.");
            mat.shader = _skinShader;
            mat.SetTexture("_SkinZones", Tex("SkinZones_Mask"));
            mat.SetTexture("_PoreMap", Tex("SkinPore_Normal"));
            mat.SetFloat("_PoreTiling", poreTiling);
            mat.SetFloat("_StubbleDots", stubbleDots);
            mat.SetFloat("_LipDesat", lipDesat);
            mat.SetFloat("_LipSmooth", lipSmooth);
            mat.SetFloat("_PoreStrength", 0.8f);
            mat.SetTexture("_SkinZones2", Tex("SkinZones2_Mask"));
            mat.SetFloat("_StubbleStrength", 0f);
            mat.SetFloat("_HairlineStrength", 0f);
            mat.SetFloat("_MottleAmount", 0f);
            mat.SetFloat("_MicroStrength", micro);
            mat.SetFloat("_StubbleStrength", stubble);
            mat.SetFloat("_HairlineStrength", stubble > 0f ? 0.4f : 0.3f);
            mat.SetFloat("_MottleAmount", mottle);
            mat.SetColor("_StubbleColor", stubbleColor ?? new Color(0.6f, 0.55f, 0.55f));
            mat.SetColor("_MottleColor", new Color(0.66f, 0.52f, 0.62f));
            mat.SetFloat("_PoreStrength", pore);
            mat.SetFloat("_Wrap", wrap);
            mat.SetColor("_SSSColor", sss);
            mat.SetFloat("_SSSStrength", sssStrength);
            mat.SetFloat("_ShineBoost", shine);
            mat.SetFloat("_CavityStrength", cavity);
            mat.SetColor("_SocketColor", socketColor);
            mat.SetFloat("_SocketAmount", socket);
            mat.SetFloat("_Desaturate", desat);
            mat.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ the characters

        static Dictionary<string, Material> CourierMaterials()
        {
            return new Dictionary<string, Material>
            {
                { "Skin", MpfbSkin("Courier_Skin", "Courier_Skin_Albedo", new Color(0.95f, 0.87f, 0.82f), 0.2f, 0.13f, 0.1f, 0.1f,
                    new Color(0.55f, 0.45f, 0.45f), new Color(0.8f, 0.45f, 0.35f), 0.25f, 0.5f, 1.2f, 0.2f, 0.3f, 0f, 0.25f,
                    0.55f, 0.6f, 0.55f, 12f, new Color(0.5f, 0.45f, 0.44f)) },
                { "Eyes", MpfbMat("Courier_Eyes", "Courier_Eyes_Albedo", null, null, Color.white, 0.97f) },
                { "EyeWet", EyeWetMat() },
                { "Brows", MpfbMat("Courier_Brows", "Courier_Brows_Albedo", null, null, Color.white, 0.2f, AlphaMode.Clip, 0.3f, true) },
                { "Lashes", MpfbMat("Courier_Lashes", "Courier_Lashes_Albedo", null, null, Color.white, 0.2f, AlphaMode.Clip, 0.3f, true) },
                { "Hair", MpfbMat("Courier_Hair", "Courier_Hair_Albedo", "Courier_Hair_Normal", null, Color.white, 0.3f, AlphaMode.Clip, 0.5f, true) },
                // Worn, darker teal jacket: the albedo times a muted tint.
                { "Top", MpfbMat("Courier_Top", "Courier_Top_Albedo", "Courier_Clothes_Normal", "Courier_Clothes_AO", CourierJacketTint, 0.2f) },
                { "Pants", MpfbMat("Courier_Pants", "Courier_Pants_Albedo", "Courier_Clothes_Normal", "Courier_Clothes_AO", CourierJeansTint, 0.2f) },
                { "Shoes", MpfbMat("Courier_Shoes", "Courier_Shoes_Albedo", null, null, Color.white, 0.3f) },
            };
        }

        static readonly Color CourierJacketTint = new Color(0.4f, 0.45f, 0.43f);
        static readonly Color CourierJeansTint = new Color(0.75f, 0.75f, 0.8f);

        static Dictionary<string, Material> HollowMaterials()
        {
            return new Dictionary<string, Material>
            {
                // Sickly, pale grey with sunken, dark sockets; the wrap tint is a cold grey-violet, not a warm blush.
                { "Skin", MpfbSkin("Hollow_Skin", "Hollow_Skin_Albedo", new Color(1.0f, 1.02f, 1.1f), 0.28f, 0.25f, 0.9f, 0.45f,
                    new Color(0.22f, 0.2f, 0.26f), new Color(0.38f, 0.3f, 0.38f), 0.35f, 0.4f, 1.0f, 0f, 0.5f, 0.85f) },
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
            if (mats.ContainsKey("EyeWet")) AddEyeWetShell(go, mats["EyeWet"]);
            return go;
        }

        // A second skinned renderer over the eyeballs with the wet-eye shell shader (cornea reflection, lid shadow, tear line).
        static void AddEyeWetShell(GameObject go, Material wet)
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMaterials.Length != 1 || MpfbPart(smr.sharedMaterials[0].name) != "Eyes") continue;
                var shell = new GameObject(smr.name + "_Wet");
                shell.transform.SetParent(smr.transform.parent, false);
                shell.transform.localPosition = smr.transform.localPosition;
                shell.transform.localRotation = smr.transform.localRotation;
                shell.transform.localScale = smr.transform.localScale;
                var r = shell.AddComponent<SkinnedMeshRenderer>();
                r.sharedMesh = smr.sharedMesh;
                r.bones = smr.bones;
                r.rootBone = smr.rootBone;
                r.localBounds = smr.localBounds;
                r.sharedMaterial = wet;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = true;
                r.updateWhenOffscreen = false;
                r.quality = SkinQuality.Bone4;
                Debug.Log("EyeWet shell added to " + smr.name);
                return;
            }
        }

        static Material EyeWetMat()
        {
            if (_mats.TryGetValue("Courier_EyeWet", out var c)) return c;
            var sh = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Shaders/EyeWet.shader");
            if (sh == null) throw new InvalidOperationException("Shader 'Zombies55/EyeWet' not found.");
            var m = LoadOrCreateMat("Courier_EyeWet", sh);
            m.SetFloat("_Wet", 1.0f); m.SetFloat("_Glint", 5f); m.SetFloat("_LidShadow", 0.5f); m.SetFloat("_Tear", 0.18f);
            EditorUtility.SetDirty(m);
            _mats["Courier_EyeWet"] = m;
            return m;
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
            EnsureRagdollLayer();
            IgnoreRagdollCollisions();
            Physics.IgnoreLayerCollision(GameLayers.Player, GameLayers.Enemy, true);
            // Pooled blast debris never collides with the player, enemies, explosive props or itself (it cannot trap or push anyone).
            Physics.IgnoreLayerCollision(GameLayers.Debris, GameLayers.Player, true);
            Physics.IgnoreLayerCollision(GameLayers.Debris, GameLayers.Enemy, true);
            Physics.IgnoreLayerCollision(GameLayers.Debris, GameLayers.Explosive, true);
            Physics.IgnoreLayerCollision(GameLayers.Debris, GameLayers.Debris, true);
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
            var gearLook = new Look { Override = new Dictionary<string, Material> { { "Cap", CourierCapMat() }, { "Bag", CourierGearMat() }, { "BagStrap", CourierWebbingMat() } } };
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

        // Char2_Cap_V2 (Tools/Blender/make_courier_cap.py): a smooth crown with a rolled band and a curved bill, authored in the
        // MPFB_Courier rest pose against its own skin and hair, so it is placed at the character root and rides the Head bone.
        // The hair under it is covered by the crown; the hair at the sides and back stays visible below the band.
        static void FitCap(GameObject go, Transform head, Look look)
        {
            var cap = Spawn("Char2_Cap_V2", head, Vector3.zero, 0f, look, "Char2_Cap_V2");
            foreach (var c in cap.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            cap.transform.SetPositionAndRotation(go.transform.position, go.transform.rotation);
            CutHairUnderCap(go);
        }

        // Keep in step with EDGE_FRONT / EDGE_BACK and CENTER in make_courier_cap.py (character-local metres, +Z forward).
        const float CapEdgeFront = 1.716f, CapEdgeBack = 1.688f, CapCenterForward = 0.062f;
        const string HairFitPath = ModelDir + "/Characters/Generated/Courier_Hair_Fit.asset";

        // Hides the hair the crown covers: every hair triangle whose three vertices lie above the cap's band edge. The hair
        // below the band (fringe, sides, nape) stays. Saved as a mesh copy, weights and bind poses unchanged.
        static void CutHairUnderCap(GameObject go)
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!HasPart(smr, "Hair")) continue;
                var mesh = Object.Instantiate(smr.sharedMesh);
                mesh.name = "Courier_Hair_Fit";
                var v = mesh.vertices;
                var covered = new bool[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    var p = go.transform.InverseTransformPoint(smr.transform.TransformPoint(v[i]));
                    float theta = Mathf.Atan2(p.x, p.z - CapCenterForward);
                    float edge = Mathf.Lerp(CapEdgeFront, CapEdgeBack, (1f - Mathf.Cos(theta)) * 0.5f);
                    covered[i] = p.y > edge;
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var tris = mesh.GetTriangles(sub);
                    var kept = new List<int>(tris.Length);
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        if (covered[tris[i]] && covered[tris[i + 1]] && covered[tris[i + 2]]) continue;
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

        // Char2_Backpack_V2 (Tools/Blender/make_backpack.py): a day-pack with straps draped over the courier's own jacket, authored in
        // the MPFB_Courier rest pose, so it is placed at the character root and rides the upper-chest bone.
        static void FitBackpack(GameObject go, Look look)
        {
            var pack = Spawn("Char2_Backpack_V2", UpperTorso(go), Vector3.zero, 0f, look, "Char2_Backpack_V2");
            foreach (var c in pack.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            pack.transform.SetPositionAndRotation(go.transform.position, go.transform.rotation);
        }

        // ------------------------------------------------------------------ hollow

        static GameObject BuildHollow(Transform parent)
        {
            var go = BuildMpfb("MPFB_Hollow", parent, EnemyControllerPath, true, HollowMaterials());
            var head = HBone(go, HumanBodyBones.Head);
            var skull = SkinBoundsAbove(go, head.position.y);
            var look = new Look { Override = new Dictionary<string, Material> { { "Hood", Mat("EnemyHood", new Color(0.11f, 0.1f, 0.13f)) } } };
            // Char2_Hood head part (model space): 0.22 wide, top at y 0.22, centred at z -0.035.
            float scale = skull.size.x * HoodWidthOverSkull / 0.22f;
            var origin = new Vector3(skull.center.x, skull.max.y + HoodRaise - 0.22f * scale, skull.center.z + 0.035f * scale);
            var hood = PlaceAccessory("Char2_Hood", head, go.transform, look, Vector3.one * scale, origin, FitHoodMantle);
            AddHollowVariants(go, hood);
            return go;
        }

        static void FindSlot(GameObject go, string part, out SkinnedMeshRenderer smr, out int slot)
        {
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && MpfbPart(mats[i].name) == part) { smr = r; slot = i; return; }
            }
            throw new InvalidOperationException("MPFB_Hollow has no '" + part + "' material slot.");
        }

        static Material HollowSkinVariant(string name, Color tint, float desat, float mottle, Color mottleColor, Color socket)
        {
            var m = MpfbSkin(name, "Hollow_Skin_Albedo", tint, 0.28f, 0.25f, 0.9f, desat, socket, new Color(0.38f, 0.3f, 0.38f), 0.35f, 0.4f, 1.0f, 0f, 0.5f, mottle, 0.85f);
            m.SetColor("_MottleColor", mottleColor);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material HollowClothes(string name, string albedo, Color tint)
        {
            return MpfbMat(name, albedo, "Hollow_Clothes_Normal", "Hollow_Clothes_AO", tint, 0.15f);
        }

        // Five looks the pool picks from at spawn: the original pale grey in a dark suit under the hood, a bruised hood-down walker,
        // a sallow green one in an olive jacket, a dark-skinned one in a rust jacket and faded jeans, and a livid one in oxblood.
        static void AddHollowVariants(GameObject go, GameObject hood)
        {
            var mats = HollowMaterials();
            FindSlot(go, "Skin", out var skinR, out int skinSlot);
            FindSlot(go, "Top", out var clothesR, out int topSlot);
            FindSlot(go, "Pants", out _, out int pantsSlot);
            FindSlot(go, "Hair", out var hairR, out int hairSlot);
            var hairGrey = MpfbMat("Hollow_Hair_Grey", "Hollow_Hair_Albedo", null, null, new Color(2.2f, 2.1f, 2.0f), 0.3f, AlphaMode.Clip, 0.5f, true);
            var hairBrown = MpfbMat("Hollow_Hair_Brown", "Hollow_Hair_Albedo", null, null, new Color(1.9f, 1.3f, 0.9f), 0.3f, AlphaMode.Clip, 0.5f, true);
            var darkSocket = new Color(0.22f, 0.2f, 0.26f);
            var v = go.AddComponent<EnemyVariant>();
            var so = new SerializedObject(v);
            var list = new List<(Material skin, Material top, Material pants, Material hair, bool hood, float w)>
            {
                (mats["Skin"], mats["Top"], mats["Pants"], mats["Hair"], true, 3f),
                (HollowSkinVariant("Hollow_Skin_Bruised", new Color(1.0f, 0.9f, 0.95f), 0.4f, 0.9f, new Color(0.45f, 0.22f, 0.34f), darkSocket),
                    HollowClothes("Hollow_Top_Charcoal", "Hollow_Top_Albedo", new Color(0.8f, 0.8f, 0.82f)), mats["Pants"], mats["Hair"], false, 2f),
                (HollowSkinVariant("Hollow_Skin_Sallow", new Color(0.82f, 0.95f, 0.74f), 0.5f, 0.6f, new Color(0.35f, 0.42f, 0.22f), darkSocket),
                    HollowClothes("Hollow_Top_Olive", "Hollow_Top_Albedo", new Color(1.15f, 1.2f, 0.7f)), mats["Pants"], hairGrey, true, 2f),
                (HollowSkinVariant("Hollow_Skin_Dark", new Color(0.62f, 0.5f, 0.48f), 0.2f, 0.5f, new Color(0.3f, 0.12f, 0.16f), new Color(0.1f, 0.08f, 0.1f)),
                    HollowClothes("Hollow_Top_Rust", "Hollow_Top_Albedo", new Color(1.6f, 0.9f, 0.55f)), HollowClothes("Hollow_Pants_Denim", "Hollow_Pants_Albedo", new Color(0.7f, 0.85f, 1.5f)), hairBrown, false, 2f),
                (HollowSkinVariant("Hollow_Skin_Livid", new Color(0.95f, 0.8f, 1.05f), 0.35f, 1.0f, new Color(0.4f, 0.16f, 0.4f), darkSocket),
                    HollowClothes("Hollow_Top_Oxblood", "Hollow_Top_Albedo", new Color(1.4f, 0.5f, 0.55f)), mats["Pants"], mats["Hair"], true, 2f),
            };
            so.FindProperty("skinRenderer").objectReferenceValue = skinR;
            so.FindProperty("clothesRenderer").objectReferenceValue = clothesR;
            so.FindProperty("hairRenderer").objectReferenceValue = hairR;
            so.FindProperty("skinSlot").intValue = skinSlot;
            so.FindProperty("topSlot").intValue = topSlot;
            so.FindProperty("pantsSlot").intValue = pantsSlot;
            so.FindProperty("hairSlot").intValue = hairSlot;
            so.FindProperty("hood").objectReferenceValue = hood;
            var looks = so.FindProperty("looks");
            looks.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                var e = looks.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("skin").objectReferenceValue = list[i].skin;
                e.FindPropertyRelative("top").objectReferenceValue = list[i].top;
                e.FindPropertyRelative("pants").objectReferenceValue = list[i].pants;
                e.FindPropertyRelative("hair").objectReferenceValue = list[i].hair;
                e.FindPropertyRelative("hood").boolValue = list[i].hood;
                e.FindPropertyRelative("weight").floatValue = list[i].w;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
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
