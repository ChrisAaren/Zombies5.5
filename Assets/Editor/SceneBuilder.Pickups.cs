using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Zombies55.EditorTools
{
    // Item drops: the pooled pickup slots (models, light beam, ground glow), the shockwave rings and the director.
    // The HUD half (announcement, effect chips, x2 badge) is built by ShellBuilder.
    public static partial class SceneBuilder
    {
        const string PickupModelDir = "Assets/Art/Models/Pickups/";
        const string PickupDecalPath = "Assets/Art/Textures/Pickups_Decals.png";
        const string PickupFxTexDir = "Assets/Art/Textures/PickupFx";
        const string PickupShaderPath = "Assets/Art/Shaders/PickupAdditive.shader";
        static readonly string[] PickupModelNames = { "Pickup_AmmoCache", "Pickup_Shockwave", "Pickup_Deadeye", "Pickup_Payday" };

        static void BuildPickups()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(PickupShaderPath);
            if (shader == null) throw new System.InvalidOperationException("Shader 'Zombies55/PickupAdditive' not found at " + PickupShaderPath);

            EnsureFolder(PickupFxTexDir);
            var glowTex = WritePickupTexture("PickupGlowDisc", 128, 128, (u, v) =>
            {
                float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                float a = Mathf.Clamp01(1f - r);
                return a * a * (3f - 2f * a);
            });
            var beamTex = WritePickupTexture("PickupBeam", 32, 128, (u, v) =>
            {
                float side = 1f - Mathf.Abs(u * 2f - 1f);
                side = Mathf.Pow(Mathf.Clamp01(side), 1.8f);
                float up = Mathf.Pow(1f - v, 1.25f) * Mathf.Clamp01(v / 0.02f + 0.35f);
                return side * up;
            });
            var ringTex = WritePickupTexture("PickupRing", 256, 256, (u, v) =>
            {
                float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                float inner = Mathf.SmoothStep(0.62f, 0.95f, r);          // soft inner falloff, bright at the leading edge
                float outer = 1f - Mathf.SmoothStep(0.95f, 1f, r);
                return inner * outer;
            });
            var beamMat = PickupFxMat("Pickup_Beam", shader, beamTex);
            var glowMat = PickupFxMat("Pickup_Glow", shader, glowTex);
            var ringMat = PickupFxMat("Pickup_Ring", shader, ringTex);

            var root = new GameObject("Pickups");
            root.transform.SetParent(_game, false);
            var director = root.AddComponent<PickupDirector>();
            root.AddComponent<PickupAudio>();

            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var slots = new PickupSlot[PickupTuning.PoolSize];
            for (int i = 0; i < slots.Length; i++) slots[i] = MakePickupSlot(root.transform, i, quad, beamMat, glowMat);

            var rings = new Transform[2];
            var ringRenderers = new Renderer[2];
            var ringRoot = Node("ShockwaveRings", root.transform, Vector3.zero);
            for (int i = 0; i < rings.Length; i++)
            {
                var q = MakeFxQuad("Ring" + (i + 1), ringRoot, quad, ringMat);
                q.gameObject.SetActive(false);
                rings[i] = q.transform;
                ringRenderers[i] = q;
            }

            var so = new SerializedObject(director);
            var sp = so.FindProperty("slots");
            sp.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++) sp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            var rp = so.FindProperty("rings");
            rp.arraySize = rings.Length;
            var rrp = so.FindProperty("ringRenderers");
            rrp.arraySize = rings.Length;
            for (int i = 0; i < rings.Length; i++)
            {
                rp.GetArrayElementAtIndex(i).objectReferenceValue = rings[i];
                rrp.GetArrayElementAtIndex(i).objectReferenceValue = ringRenderers[i];
            }
            so.FindProperty("particleMaterial").objectReferenceValue = mParticle;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static PickupSlot MakePickupSlot(Transform parent, int index, Mesh quad, Material beamMat, Material glowMat)
        {
            var go = new GameObject("PickupSlot_" + (index + 1).ToString("00"));
            go.transform.SetParent(parent, false);
            var slot = go.AddComponent<PickupSlot>();

            var visual = Node("Visual", go.transform, Vector3.zero);
            var spin = Node("Spin", visual, new Vector3(0f, PickupTuning.Hover, 0f));
            var models = new GameObject[PickupModelNames.Length];
            for (int t = 0; t < models.Length; t++)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(PickupModelDir + PickupModelNames[t] + ".fbx");
                if (src == null) throw new System.InvalidOperationException("Pickup model not found: " + PickupModelDir + PickupModelNames[t] + ".fbx");
                var m = (GameObject)Object.Instantiate(src);
                m.name = PickupModelNames[t];
                m.transform.SetParent(spin, false);
                m.transform.localPosition = Vector3.zero;
                m.transform.localRotation = Quaternion.identity;
                m.transform.localScale = Vector3.one;
                foreach (var c in m.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var r in m.GetComponentsInChildren<Renderer>(true))
                {
                    var srcMats = r.sharedMaterials;
                    var dst = new Material[srcMats.Length];
                    for (int i = 0; i < srcMats.Length; i++) dst[i] = PickupSlotMat(srcMats[i] != null ? srcMats[i].name : "", (PickupType)t);
                    r.sharedMaterials = dst;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                models[t] = m;
            }

            // Light beam: two crossed quads that grow from the ground; the ground glow is a flat quad just above the asphalt.
            var fx = Node("Fx", visual, Vector3.zero);
            var beamRoot = Node("Beam", fx, Vector3.zero);
            var beams = new Renderer[2];
            for (int i = 0; i < 2; i++)
            {
                var b = MakeFxQuad("BeamQuad" + (i + 1), beamRoot, quad, beamMat);
                b.transform.localPosition = new Vector3(0f, PickupTuning.BeamHeight * 0.5f, 0f);
                b.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                b.transform.localScale = new Vector3(PickupTuning.BeamWidth, PickupTuning.BeamHeight, 1f);
                beams[i] = b;
            }
            var ground = MakeFxQuad("GroundGlow", fx, quad, glowMat);
            ground.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ground.transform.localScale = new Vector3(PickupTuning.GroundGlowSize, PickupTuning.GroundGlowSize, 1f);

            var so = new SerializedObject(slot);
            so.FindProperty("visual").objectReferenceValue = visual.gameObject;
            so.FindProperty("spin").objectReferenceValue = spin;
            var mp = so.FindProperty("models");
            mp.arraySize = models.Length;
            for (int i = 0; i < models.Length; i++) mp.GetArrayElementAtIndex(i).objectReferenceValue = models[i];
            so.FindProperty("beamRoot").objectReferenceValue = beamRoot;
            var bp = so.FindProperty("beams");
            bp.arraySize = beams.Length;
            for (int i = 0; i < beams.Length; i++) bp.GetArrayElementAtIndex(i).objectReferenceValue = beams[i];
            so.FindProperty("ground").objectReferenceValue = ground;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false);
            return slot;
        }

        static MeshRenderer MakeFxQuad(string name, Transform parent, Mesh quad, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return mr;
        }

        // FBX material slot -> project material (same idea as CombatSlotMat). PickupGlow is per drop type: its emission is the pulse.
        static Material PickupSlotMat(string slot, PickupType type)
        {
            int dot = slot.IndexOf('.');
            if (dot > 0) slot = slot.Substring(0, dot);
            switch (slot)
            {
                case "PickupBody": return Surf("S_PickupBody", Palette.PickupBody, null, Vector2.one, 0.35f);
                case "PickupMetal": return Surf("S_PickupMetal", Palette.PickupMetal, null, Vector2.one, 0.55f, 0.85f);
                case "Paint": return Surf("S_PickupPaint", Palette.PickupPaint, null, Vector2.one, 0.3f);
                case "PickupGlow":
                    return Surf("S_PickupGlow_" + type, PickupTuning.ColorOf(type), null, Vector2.one, 0.5f, 0f, PickupTuning.EmissionMin);
                case "PickupDecal":
                    {
                        var m = Surf("S_PickupDecal", Color.white, null, Vector2.one, 0.25f);
                        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PickupDecalPath));
                        m.SetFloat("_AlphaClip", 1f);
                        m.SetFloat("_Cutoff", 0.3f);
                        m.EnableKeyword("_ALPHATEST_ON");
                        m.SetOverrideTag("RenderType", "TransparentCutout");
                        m.renderQueue = (int)RenderQueue.AlphaTest;
                        EditorUtility.SetDirty(m);
                        return m;
                    }
            }
            return SlotMat(slot, DefaultLook);
        }

        static Material PickupFxMat(string name, Shader shader, Texture2D tex)
        {
            var mat = LoadOrCreateMat(name, shader);
            mat.SetTexture("_MainTex", tex);
            mat.SetColor("_Color", Color.white);
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // White RGB, the profile in alpha (the additive shader reads alpha).
        static Texture2D WritePickupTexture(string name, int w, int h, System.Func<float, float, float> profile)
        {
            string path = PickupFxTexDir + "/" + name + ".png";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float a = Mathf.Clamp01(profile((x + 0.5f) / w, (y + 0.5f) / h));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            var bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            if (!File.Exists(path) || !System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(path), bytes)) File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp.wrapMode != TextureWrapMode.Clamp || imp.mipmapEnabled || imp.textureCompression != TextureImporterCompression.Uncompressed)
            {
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = false;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
