using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    // Slot-name materials, model spawning and the facade kit.
    public static partial class SceneBuilder
    {
        // Per-instance tints for the slots that vary; Override wins for any slot (used by characters).
        class Look
        {
            public Color Wall = Palette.Sand;
            public string WallTex = "Plaster";
            public Color Frame = Palette.FrameLight;
            public Color Door = Palette.DoorGreen;
            public Color Awning = Palette.AwningRed;
            public Color Car = Palette.CarSilver;
            public Color Wood = Palette.WoodNatural;
            public Color Paint = Palette.HydrantRed;
            public Color Leaf = Palette.Leaf;
            public Dictionary<string, Material> Override;
        }

        static string Hx(Color c) { return ColorUtility.ToHtmlStringRGB(c); }

        // Textured URP Lit material, cached by name and saved under Assets/Art/Materials.
        static Material Surf(string name, Color color, string tex, Vector2 tiling, float smooth, float metal = 0f, float emission = 0f)
        {
            if (_mats.TryGetValue(name, out var cached)) return cached;
            var mat = LoadOrCreateMat(name, _litShader);
            mat.SetColor("_BaseColor", color);
            mat.color = color;
            var albedo = tex != null ? Tex(tex + "_Albedo") : null;
            var normal = tex != null ? Tex(tex + "_Normal") : null;
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTextureScale("_BaseMap", tiling);
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", 1f);
            if (normal != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
            mat.SetFloat("_Smoothness", smooth);
            mat.SetFloat("_Metallic", metal);
            if (emission > 0f)
            {
                var e = color * emission;
                e.a = 1f;
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", e);
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

        // World-meter UV materials: tiling = 1 / real-world texture size.
        static Material Meters(string slot, Color tint, string tex, float size, float smooth, float metal = 0f)
        {
            float t = 1f / size;
            return Surf("S_" + slot + "_" + (tex ?? "none") + "_" + Hx(tint), tint, tex, new Vector2(t, t), smooth, metal);
        }

        static Material SlotMat(string slot, Look l)
        {
            int dot = slot.IndexOf('.');
            if (dot > 0) slot = slot.Substring(0, dot);
            if (l.Override != null && l.Override.TryGetValue(slot, out var o) && o != null) return o;
            switch (slot)
            {
                case "Wall": return Meters("Wall", l.Wall, l.WallTex, 2f, 0.2f);
                case "Trim": return Meters("Trim", Palette.Stone, "Plaster", 2f, 0.3f);
                case "Frame": return Meters("Frame", l.Frame, null, 1f, 0.45f);
                case "Glass": return Meters("Glass", Palette.GlassTint, null, 1f, 0.92f, 0.3f);
                case "Interior": return Meters("Interior", Palette.InteriorDark, null, 1f, 0.05f);
                case "Door": return Meters("Door", l.Door, null, 1f, 0.4f);
                case "Awning": return Meters("Awning", l.Awning, null, 1f, 0.12f);
                case "Roof": return Meters("Roof", Palette.RoofGravel, "Roof", 4f, 0.1f);
                case "Metal": return Meters("Metal", Palette.MetalDark, null, 1f, 0.5f, 0.5f);
                case "Concrete": return Meters("Concrete", Palette.ConcreteTint, "Concrete", 2f, 0.15f);
                case "Wood": return Meters("Wood", l.Wood, "Wood", 1f, 0.25f);
                case "Paint": return Meters("Paint", l.Paint, null, 1f, 0.45f);
                case "CarPaint": return Meters("CarPaint", l.Car, null, 1f, 0.7f, 0.3f);
                case "Chrome": return Meters("Chrome", Palette.ChromeSilver, null, 1f, 0.85f, 0.9f);
                case "Rubber": return Meters("Rubber", Palette.RubberBlack, null, 1f, 0.1f);
                case "Headlight": return Surf("S_Headlight", Palette.HeadlightWarm, null, Vector2.one, 0.8f, 0f, 1.5f);
                case "Taillight": return Surf("S_Taillight", Palette.TaillightRed, null, Vector2.one, 0.8f, 0f, 1.2f);
                case "LampGlass": return Surf("S_LampGlass", Palette.SunKey, null, Vector2.one, 0.8f, 0f, 1.5f);
                case "Bark": return Meters("Bark", Palette.Trunk, null, 1f, 0.1f);
                case "Leaf": return Meters("Leaf", l.Leaf, null, 1f, 0.15f);
                case "Water": return Meters("Water", Palette.WaterTint, null, 1f, 0.95f);
                case "Eye": return Meters("Eye", Palette.EyeBlack, null, 1f, 0.8f);
                case "Skin": return Meters("Skin", Palette.CharacterSecondary, null, 1f, 0.25f);
                case "Top": return Meters("Top", Palette.CharacterPrimary, null, 1f, 0.25f);
                case "Pants": return Meters("Pants", Palette.Pants, null, 1f, 0.25f);
                case "Shoe": return Meters("Shoe", Palette.Shadow, null, 1f, 0.25f);
                case "Hair": return Meters("Hair", Palette.HairDark, null, 1f, 0.25f);
                case "Cap": return Meters("Cap", Palette.Terracotta, null, 1f, 0.25f);
                case "Bag": return Meters("Bag", Palette.Terracotta, null, 1f, 0.25f);
            }
            throw new InvalidOperationException("Unknown material slot '" + slot + "'");
        }

        static void ApplyLook(GameObject go, Look look)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = SlotMat(src[i] != null ? src[i].name : "", look);
                r.sharedMaterials = dst;
            }
        }

        static readonly Look DefaultLook = new Look();

        static GameObject Spawn(string model, Transform parent, Vector3 localPos, float yaw = 0f, Look look = null, string name = null)
        {
            var go = (GameObject)Object.Instantiate(Model(model));
            go.name = name ?? model;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one;
            ApplyLook(go, look ?? DefaultLook);
            return go;
        }

        // Bounds of all meshes under go, in go's local space.
        static Bounds LocalBounds(GameObject go)
        {
            var inv = go.transform.worldToLocalMatrix;
            Bounds b = default;
            bool first = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mb = mf.sharedMesh.bounds;
                var m = inv * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        // One fitted BoxCollider over the mesh bounds; optional floor raise and horizontal clamp (half extent).
        static BoxCollider FitBox(GameObject go, float minY = float.NegativeInfinity, float maxHalfXZ = float.PositiveInfinity)
        {
            var b = LocalBounds(go);
            var min = b.min; var max = b.max;
            min.y = Mathf.Max(min.y, minY);
            var c = (min + max) * 0.5f;
            var s = max - min;
            s.x = Mathf.Min(s.x, maxHalfXZ * 2f);
            s.z = Mathf.Min(s.z, maxHalfXZ * 2f);
            var box = go.AddComponent<BoxCollider>();
            box.center = c;
            box.size = s;
            return box;
        }

        static BoxCollider ThinBox(GameObject go, float width, float height)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, height * 0.5f, 0f);
            box.size = new Vector3(width, height, width);
            return box;
        }

        // ------------------------------------------------------------------ facade kit

        static readonly Color[] WallTints = { Palette.Terracotta, Palette.Sage, Palette.Sand, Palette.DustyBlue };
        static readonly string[] WallTexs = { "Brick", "Plaster", "Brick", "Plaster" };

        static Look NextBuildingLook()
        {
            int i = _buildingIndex++;
            var awnings = new[] { Palette.AwningRed, Palette.AwningGreen, Palette.AwningNavy, Palette.AwningCream };
            var doors = new[] { Palette.DoorGreen, Palette.DoorNavy, Palette.DoorRed };
            return new Look
            {
                Wall = WallTints[i % 4],
                WallTex = WallTexs[i % 4],
                Frame = _rng.NextDouble() < 0.6 ? Palette.FrameLight : Palette.FrameDark,
                Door = doors[_rng.Next(doors.Length)],
                Awning = awnings[_rng.Next(awnings.Length)],
            };
        }

        static Material RoofSlabMat(float w, float d)
        {
            return Surf("S_RoofSlab_" + w.ToString("0.#") + "x" + d.ToString("0.#"), Palette.RoofGravel, "Roof",
                new Vector2(w / 4f, d / 4f), 0.1f);
        }

        // Facade-kit building. w, d are multiples of 3 (outer wall planes at +-w/2, +-d/2 from pos).
        // faceMask bits: 1 = +z, 2 = -z, 4 = +x, 8 = -x.
        static void MakeKitBuilding(Transform parent, Vector3 pos, int w, int d, int floors, int faceMask, bool ring)
        {
            var look = NextBuildingLook();
            var b = Node("Building", parent, pos);
            float baseY = SidewalkTop;
            float height = 4f + 3.2f * floors;

            var yaws = new[] { 0f, 180f, 90f, 270f };
            for (int f = 0; f < 4; f++)
            {
                if ((faceMask & (1 << f)) == 0) continue;
                bool zFace = f < 2;
                int bays = (zFace ? w : d) / 3;
                float half = (zFace ? d : w) * 0.5f;
                var rot = Quaternion.Euler(0f, yaws[f], 0f);
                var face = Node("Face" + f, b, Vector3.zero);

                // Ground floor: mix of storefronts and entrances, some awnings.
                var ground = new string[bays];
                for (int i = 0; i < bays; i++) ground[i] = "Facade_Storefront";
                int entrances = bays >= 4 && _rng.NextDouble() < 0.5 ? 2 : 1;
                for (int e = 0; e < entrances; e++) ground[_rng.Next(bays)] = "Facade_Entrance";
                bool awnings = !ring && _rng.NextDouble() < 0.65;

                // Upper floors: one rhythm per facade, columns consistent across floors.
                string primary = _rng.NextDouble() < 0.6 ? "Facade_Window" : "Facade_WindowPair";
                string other = primary == "Facade_Window" ? "Facade_WindowPair" : "Facade_Window";
                int rhythm = _rng.Next(3);
                var cols = new string[bays];
                for (int i = 0; i < bays; i++)
                {
                    switch (rhythm)
                    {
                        case 0: cols[i] = primary; break;
                        case 1: cols[i] = (i % 2 == 0) ? primary : "Facade_Blank"; break;
                        default: cols[i] = (i % 2 == 0) ? primary : other; break;
                    }
                    if (ring && i % 2 == 1) cols[i] = "Facade_Blank";
                }

                for (int i = 0; i < bays; i++)
                {
                    float x = -(bays * 3f) * 0.5f + 1.5f + 3f * i;
                    var p = rot * new Vector3(x, 0f, half);
                    Spawn(ground[i], face, p + new Vector3(0f, baseY, 0f), yaws[f], look);
                    if (awnings && (ground[i] == "Facade_Storefront" || i % 2 == 0))
                        Spawn("Awning", face, p + new Vector3(0f, baseY + 3.2f, 0f), yaws[f], look);
                    for (int fl = 0; fl < floors; fl++)
                        Spawn(cols[i], face, p + new Vector3(0f, baseY + 4f + 3.2f * fl, 0f), yaws[f], look);
                    Spawn("Cornice", face, p + new Vector3(0f, baseY + height, 0f), yaws[f], look);
                }
            }

            // Corners
            var corners = Node("Corners", b, Vector3.zero);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    var c = new Vector3(sx * w * 0.5f, baseY, sz * d * 0.5f);
                    Spawn("Corner_Pilaster_Ground", corners, c, 0f, look);
                    for (int fl = 0; fl < floors; fl++)
                        Spawn("Corner_Pilaster", corners, c + new Vector3(0f, 4f + 3.2f * fl, 0f), 0f, look);
                    Spawn("Cornice_Corner", corners, c + new Vector3(0f, height, 0f), 0f, look);
                }

            // Roof slab and rooftop units
            Box("Roof", b, new Vector3(0f, baseY + height + 0.15f, 0f), new Vector3(w - 0.5f, 0.3f, d - 0.5f),
                RoofSlabMat(w - 0.5f, d - 0.5f), false);
            float roofY = baseY + height + 0.3f;
            var units = new[] { "Roof_ACUnit", "Roof_Vent", "Roof_ACUnit", "Roof_Vent" };
            int count = ring ? 2 : 1 + _rng.Next(3);
            // 3x3 candidate cells; the water tank (if any) claims one and keeps neighbours clear.
            var cells = new List<int>();
            for (int i = 0; i < 9; i++) cells.Add(i);
            for (int i = cells.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (cells[i], cells[j]) = (cells[j], cells[i]); }
            int tankCell = -1;
            bool tank = (w >= 9 || d >= 9) && _rng.NextDouble() < 0.3;
            float cw = (w - 2.6f) / 2f, cd = (d - 2.6f) / 2f;
            Vector3 CellPos(int c) { return new Vector3(((c % 3) - 1) * cw, roofY, ((c / 3) - 1) * cd); }
            if (tank) { tankCell = cells[0]; cells.RemoveAt(0); Spawn("Roof_WaterTank", b, CellPos(tankCell), _rng.Next(4) * 90f, look); }
            foreach (int c in cells)
            {
                if (count <= 0) break;
                if (tankCell >= 0 && Mathf.Abs(c % 3 - tankCell % 3) < 2 && Mathf.Abs(c / 3 - tankCell / 3) < 2) continue;
                Spawn(units[count % units.Length], b, CellPos(c), _rng.Next(4) * 90f, look);
                count--;
            }

            // One collider over the whole volume.
            var col = b.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, baseY + (height + 0.3f) * 0.5f, 0f);
            col.size = new Vector3(w, height + 0.3f, d);
        }
    }
}
