using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    // Buyable districts: ZoneManager, barrier segments on every cell edge between two districts,
    // and a gate terminal on each owned side. See Assets/Scripts/Zones.
    public static partial class SceneBuilder
    {
        const float ZoneBarrierThickness = 1.8f;     // covers the jersey blocks on either side of the fence
        const float ZoneSignScale = 2f;
        const int ZonePanelsPerSegment = 13;

        static Transform _zoneGates;
        static Look _zoneLook;

        static void BuildZones()
        {
            Physics.SyncTransforms();
            ConfigureFenceTexture();

            var root = Root("Zones");
            var mgrGo = new GameObject("ZoneManager");
            mgrGo.transform.SetParent(root, false);
            var mgr = mgrGo.AddComponent<ZoneManager>();
            mgrGo.AddComponent<ZoneAudio>();

            var dust = MakeSystem("BarrierDust", mgrGo.transform, 1f, false, false, 256);
            {
                var main = dust.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
                main.gravityModifier = -0.04f;
                main.startColor = Palette.WithAlpha(Palette.ConcreteTint, 0.7f);
                var shape = dust.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.4f;
            }
            var smoke = MakeSystem("SpawnSmoke", mgrGo.transform, 1f, false, false, 128);
            {
                var main = smoke.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.0f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
                main.gravityModifier = -0.15f;
                main.startColor = Palette.WithAlpha(Palette.SmokeDark, 0.75f);
                var shape = smoke.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.35f;
            }
            SetRef(mgr, "dustPuff", dust);
            SetRef(mgr, "smokePuff", smoke);

            _zoneLook = new Look
            {
                Paint = Palette.Shadow,
                Override = new Dictionary<string, Material>
                {
                    { "Metal", Meters("GalvMetal", Palette.FenceGalv, null, 1f, 0.45f, 0.6f) },
                },
            };

            var barriers = Node("Barriers", root, Vector3.zero);
            _zoneGates = Node("Gates", root, Vector3.zero);

            int segs = 0;
            for (int ix = -ZoneLayout.Half; ix <= ZoneLayout.Half; ix++)
                for (int iz = -ZoneLayout.Half; iz <= ZoneLayout.Half; iz++)
                {
                    int a = ZoneLayout.DistrictOfCell(ix, iz);
                    if (ix < ZoneLayout.Half)
                    {
                        int b = ZoneLayout.DistrictOfCell(ix + 1, iz);
                        if (a != b) { MakeBarrierSegment(barriers, ix, iz, true, a, b); segs++; }
                    }
                    if (iz < ZoneLayout.Half)
                    {
                        int b = ZoneLayout.DistrictOfCell(ix, iz + 1);
                        if (a != b) { MakeBarrierSegment(barriers, ix, iz, false, a, b); segs++; }
                    }
                }
            Debug.Log("Zones: " + segs + " barrier segments");
        }

        static void ConfigureFenceTexture()
        {
            var path = TextureDir + "/Fence_Albedo.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            if (ti.alphaIsTransparency && ti.mipMapsPreserveCoverage && ti.alphaSource == TextureImporterAlphaSource.FromInput) return;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = 0.5f;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.SaveAndReimport();
            _tex.Remove("Fence_Albedo");
        }

        static Material ZoneFenceMat()
        {
            var m = Surf("S_Fence", Color.white, null, Vector2.one, 0.45f, 0.5f);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/Fence_Albedo.png");
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void ApplyZoneLook(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    string n = src[i] != null ? src[i].name : "";
                    int dot = n.IndexOf('.');
                    if (dot > 0) n = n.Substring(0, dot);
                    dst[i] = n == "Fence" ? ZoneFenceMat() : CombatSlotMat(n, _zoneLook);
                }
                r.sharedMaterials = dst;
            }
        }

        static GameObject ZoneSpawn(string model, Transform parent, Vector3 localPos, float localYaw, string name)
        {
            var go = (GameObject)Object.Instantiate(Model(model));
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, localYaw, 0f);
            go.transform.localScale = Vector3.one;
            ApplyZoneLook(go);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            return go;
        }

        // east = the edge between (ix, iz) and (ix + 1, iz); otherwise between (ix, iz) and (ix, iz + 1).
        // Local +X runs along the edge, local +Z points toward district b.
        static void MakeBarrierSegment(Transform parent, int ix, int iz, bool east, int a, int b)
        {
            float cs = ZoneLayout.CellSize;
            Vector3 center = east ? new Vector3(ix * cs + cs * 0.5f, 0f, iz * cs) : new Vector3(ix * cs, 0f, iz * cs + cs * 0.5f);
            float yaw = east ? 90f : 0f;

            var go = new GameObject("Barrier_" + ZoneLayout.Names[a] + "|" + ZoneLayout.Names[b] + "_" + (east ? "E" : "N") + ix + "_" + iz);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, yaw, 0f));
            go.layer = GameLayers.Barrier;

            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 3f, 0f);
            col.size = new Vector3(cs, 6f, ZoneBarrierThickness);
            var ob = go.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Box;
            ob.center = new Vector3(0f, 1.5f, 0f);
            ob.size = new Vector3(cs, 3f, ZoneBarrierThickness);
            ob.carving = true;

            var visual = Node("Visual", go.transform, Vector3.zero);
            float w = cs / ZonePanelsPerSegment;
            for (int k = 0; k < ZonePanelsPerSegment; k++)
            {
                float x = -cs * 0.5f + w * (k + 0.5f);
                var panel = ZoneSpawn("Barrier_FencePanel", visual, new Vector3(x, 0f, 0f), 0f, "Panel_" + k);
                panel.transform.localScale = new Vector3(w / 2.98f, 1f, 1f);
                var jersey = ZoneSpawn("Barrier_Jersey", visual, new Vector3(x, 0f, (k % 2 == 0 ? -0.58f : 0.58f)), 0f, "Jersey_" + k);
                jersey.transform.localScale = new Vector3(0.98f * Mathf.Min(1f, (w - 0.1f) / 2f), 1f, 1f);
            }

            go.AddComponent<ZoneBarrier>().Configure(a, b, visual);

            // Signs: the plate on the A side names B (and its price), and the other way round.
            MakeZoneSign(visual, 10f, -1f, b);
            MakeZoneSign(visual, 10f, 1f, a);

            // Gates: each owned side buys the district beyond.
            MakeZoneGate(go.transform, yaw, -1f, a, b);
            MakeZoneGate(go.transform, yaw, 1f, b, a);
        }

        // side = -1: faces the A side (local -Z); +1: faces the B side.
        static void MakeZoneSign(Transform visual, float x, float side, int beyond)
        {
            var sign = ZoneSpawn("Barrier_Sign", visual, new Vector3(x, 1.1f, side * 0.03f), side < 0f ? 180f : 0f,
                "Sign_" + ZoneLayout.Names[beyond]);
            sign.transform.localScale = Vector3.one * ZoneSignScale;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(sign.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.3f, 0.04f);
            labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // TMP reads from -Z, the plate faces +Z
            var label = labelGo.AddComponent<TextMeshPro>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSharedMaterial = ShellBuilder.GetOrCreateOutlineMaterial();
            label.enableAutoSizing = true;
            label.fontSizeMin = 0.5f;
            label.fontSizeMax = 4f;
            label.fontStyle = FontStyles.Bold;
            label.color = Palette.Accent;
            label.alignment = TextAlignmentOptions.Center;
            label.text = ZoneLayout.Names[beyond].ToUpperInvariant() + "\n" + ZoneLayout.Costs[beyond];
            label.rectTransform.sizeDelta = new Vector2(1.08f, 0.4f);
        }

        // side = -1: terminal on the A side (owner a), +1: on the B side (owner b).
        static void MakeZoneGate(Transform segment, float segYaw, float side, int owner, int target)
        {
            // Slide along the segment until the spot is clear (kiosks, cars, props).
            float[] offsets = { 0f, 3f, -3f, 6f, -6f, 9f, -9f, 12f, -12f, 15f, -15f };
            foreach (float off in offsets)
            {
                Vector3 local = new Vector3(off, 0f, side * 1.3f);
                Vector3 world = segment.TransformPoint(local);
                float yaw = segYaw + (side < 0f ? 180f : 0f);
                var center = world + Vector3.up * 0.75f;
                if (Physics.CheckBox(center, new Vector3(0.3f, 0.55f, 0.3f), Quaternion.Euler(0f, yaw, 0f),
                        ~(1 << GameLayers.Barrier), QueryTriggerInteraction.Ignore)) continue;

                var go = (GameObject)Object.Instantiate(Model("Gate_Terminal"));
                go.name = "Gate_" + ZoneLayout.Names[owner] + "_to_" + ZoneLayout.Names[target];
                go.transform.SetParent(_zoneGates, false);
                go.transform.SetPositionAndRotation(world, Quaternion.Euler(0f, yaw, 0f));
                ApplyZoneLook(go);
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                FitBox(go);
                go.AddComponent<ZoneGate>().Configure(owner, target);
                Physics.SyncTransforms();
                return;
            }
            Debug.LogWarning("Zones: no free spot for the gate " + ZoneLayout.Names[owner] + " -> " + ZoneLayout.Names[target]);
        }
    }
}
