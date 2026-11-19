using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    // Gritty urban dressing: facade clutter (fire escapes, AC units, signs, grime), street clutter (utility poles and
    // wires, dumpsters, bags, news boxes, manholes, storm grates) and worn road markings.
    // Uses its own RNG so the base layout (buildings, cars, props) keeps its seed.
    public static partial class SceneBuilder
    {
        static System.Random _grit;
        static int _signIndex;
        const string GeneratedDir = "Assets/Art/Models/Generated";

        static float Gf(float min, float max) { return min + (float)_grit.NextDouble() * (max - min); }
        static bool Gp(float p) { return _grit.NextDouble() < p; }

        // ------------------------------------------------------------------ quads and decal materials

        static readonly Dictionary<string, Mesh> _quads = new Dictionary<string, Mesh>();

        // Unit quad asset. wall: vertical in XY facing +Z; flat: horizontal in XZ facing +Y. u0..u1 is the UV window.
        static Mesh GritQuad(bool wall, float u0, float u1)
        {
            string key = (wall ? "wall" : "flat") + "_" + u0.ToString("0.00") + "_" + u1.ToString("0.00");
            if (_quads.TryGetValue(key, out var cached) && cached != null) return cached;
            EnsureFolder(GeneratedDir);
            string path = GeneratedDir + "/GritQuad_" + key + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = "GritQuad_" + key };
                AssetDatabase.CreateAsset(mesh, path);
            }
            mesh.Clear();
            Vector3[] v;
            Vector3 n;
            if (wall)
            {
                // Seen from +Z the viewer's right is world -X, so u runs from +X (left) to -X (right).
                v = new[] { new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(-.5f, -.5f, 0) };
                n = Vector3.forward;
            }
            else
            {
                v = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) };
                n = Vector3.up;
            }
            mesh.vertices = v;
            mesh.normals = new[] { n, n, n, n };
            mesh.uv = new[] { new Vector2(u0, 0), new Vector2(u0, 1), new Vector2(u1, 1), new Vector2(u1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            _quads[key] = mesh;
            return mesh;
        }

        // Alpha-blended URP Lit decal material (no shadows, no reflections).
        static Material DecalMat(string name, string tex, Color tint)
        {
            if (_mats.TryGetValue(name, out var cached)) return cached;
            var mat = LoadOrCreateMat(name, _litShader);
            mat.SetColor("_BaseColor", tint);
            mat.color = tint;
            mat.SetTexture("_BaseMap", Tex(tex + "_Albedo"));
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetTexture("_BumpMap", null);
            mat.DisableKeyword("_NORMALMAP");
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", 5f);
            mat.SetFloat("_DstBlend", 10f);
            mat.SetFloat("_SrcBlendAlpha", 1f);
            mat.SetFloat("_DstBlendAlpha", 10f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.SetFloat("_Cull", 2f);
            mat.SetFloat("_Smoothness", 0.08f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(mat);
            _mats[name] = mat;
            return mat;
        }

        static GameObject PlaceQuad(Mesh mesh, Material mat, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 scale, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static void NoShadows(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Material GrimeMat() { return DecalMat("GrimeDecal", "Grime", Palette.GrimeTint); }

        static GameObject GrimeQuad(Transform parent, Vector3 localPos, Quaternion rot, float w, float h)
        {
            float u0 = _grit.Next(4) * 0.25f;
            bool flip = Gp(0.5f);
            var mesh = GritQuad(true, flip ? u0 + 0.6f : u0, flip ? u0 : u0 + 0.6f);
            return PlaceQuad(mesh, GrimeMat(), parent, localPos, rot, new Vector3(w, h, 1f), "Grime");
        }

        // ------------------------------------------------------------------ facade bays

        static readonly Color[] SignColors =
            { Palette.SignGreen, Palette.SignRed, Palette.SignNavy, Palette.SignBrown, Palette.SignTeal, Palette.SignRed };

        // Dresses one facade bay (face-local origin = the bay's bottom centre on the outer wall plane). p = that point in building space.
        static void DressBay(Transform face, Look look, Vector3 p, float yaw, float baseY, int floors, string ground, string upper,
            bool fireEscape, int bay, int bays)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float height = 4f + 3.2f * floors;

            // Storefront signs (own design, fictional shop names).
            if (ground == "Facade_Storefront" && Gp(0.38f))
            {
                int k = _signIndex++ % 6;
                var sl = look.WithSign(SignColors[k]);
                var go = Spawn("Storefront_Sign_" + (k + 1), face, p + new Vector3(0f, baseY + 3.3f, 0f), yaw, sl);
                NoShadows(go);
            }

            bool window = upper == "Facade_Window" || upper == "Facade_WindowPair";
            for (int fl = 0; fl < floors; fl++)
            {
                float y0 = baseY + 4f + 3.2f * fl;
                if (fireEscape && fl >= 1)
                    Spawn("FireEscape_Floor", face, p + new Vector3(0f, y0, 0f), yaw, look);
                if (window && !fireEscape && upper == "Facade_Window" && Gp(0.16f))
                {
                    var ac = Spawn("AC_WindowUnit", face, p + rot * new Vector3(Gf(-0.25f, 0.25f), 0f, 0f) + new Vector3(0f, y0 + 0.87f, 0f), yaw,
                        new Look { Paint = Palette.PaintWorn, Wall = look.Wall });
                    NoShadows(ac);
                }
                // Grime dripping from under the sills.
                if (window && Gp(0.55f))
                {
                    float w = upper == "Facade_Window" ? 1.9f : 2.4f;
                    GrimeQuad(face, p + rot * new Vector3(Gf(-0.1f, 0.1f), 0f, 0.04f) + new Vector3(0f, y0 + 0.78f - 0.55f, 0f),
                        rot * Quaternion.Euler(0f, 0f, 0f), w, Gf(1.0f, 1.5f));
                }
            }
            // Soot under the cornice, every bay.
            GrimeQuad(face, p + rot * new Vector3(0f, 0f, 0.04f) + new Vector3(0f, baseY + height - 0.55f, 0f), rot, 3.05f, Gf(1.0f, 1.4f));
        }

        // ------------------------------------------------------------------ road markings

        static void BuildWornMarkings(Transform lines)
        {
            var paint = DecalMat("RoadMarkingsWorn", "RoadMarkings", Palette.RoadLine);
            var dash = GritQuad(false, 0f, 1f);
            const float y = 0.02f;
            foreach (float road in RoadCenters)
            {
                for (float s = -102f; s <= 102f; s += 6f)
                {
                    bool near = false;
                    foreach (float r in RoadCenters)
                        if (Mathf.Abs(s - r) < 7.5f) { near = true; break; }
                    if (near) continue;
                    PlaceQuad(dash, paint, lines, new Vector3(road, y, s), Quaternion.identity, new Vector3(0.34f, 1f, 3f), "LineZ");
                    PlaceQuad(dash, paint, lines, new Vector3(s, y, road), Quaternion.Euler(0f, 90f, 0f), new Vector3(0.34f, 1f, 3f), "LineX");
                }
            }

            // Zebra crossings on most approaches (skipping the centreline stripe where district barriers will run).
            var stripe = GritQuad(false, 0.1f, 0.9f);
            float[] offs = { -5.4f, -4.2f, -3f, -1.8f, -0.6f, 0.6f, 1.8f, 3f, 4.2f, 5.4f };
            var dirs = new[] { new Vector2(0, 1), new Vector2(0, -1), new Vector2(1, 0), new Vector2(-1, 0) };
            foreach (float rx in RoadCenters)
                foreach (float rz in RoadCenters)
                    foreach (var d in dirs)
                    {
                        var c = new Vector2(rx, rz) + d * 7.6f;
                        if (Mathf.Abs(c.x) > 99f || Mathf.Abs(c.y) > 99f) continue;
                        if (!Gp(0.7f)) continue;
                        bool alongZ = d.y != 0f;
                        foreach (float t in offs)
                        {
                            var pos = alongZ ? new Vector3(c.x + t, y, c.y) : new Vector3(c.x, y, c.y + t);
                            PlaceQuad(stripe, paint, lines, pos, alongZ ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f), new Vector3(0.55f, 1f, 3f), "Crosswalk");
                        }
                    }
        }

        // ------------------------------------------------------------------ street clutter

        static bool NearSpawnCandidate(Vector2 p, float r)
        {
            float r2 = r * r;
            foreach (float x in RoadCenters)
                foreach (float z in RoadCenters)
                    if ((p - new Vector2(x, z)).sqrMagnitude < r2) return true;
            foreach (float rd in RoadCenters)
                for (int i = 0; i + 1 < RoadCenters.Length; i++)
                {
                    float mid = (RoadCenters[i] + RoadCenters[i + 1]) * 0.5f;
                    if ((p - new Vector2(rd, mid)).sqrMagnitude < r2) return true;
                    if ((p - new Vector2(mid, rd)).sqrMagnitude < r2) return true;
                }
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                    if ((p - new Vector2(cx, cz - OuterLane)).sqrMagnitude < r2) return true;
            if ((p - new Vector2(0f, -11.5f)).sqrMagnitude < (r + 1.5f) * (r + 1.5f)) return true;   // player start
            return false;
        }

        static readonly Vector2[] GritSides = { new Vector2(0, 1), new Vector2(0, -1), new Vector2(1, 0), new Vector2(-1, 0) };

        static bool BoxFree(Vector3 center, Vector3 half, float yaw)
        {
            return !Physics.CheckBox(center, half, Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore);
        }

        static void BuildStreetGrit()
        {
            Physics.SyncTransforms();
            BuildUtilityLines();
            BuildManholesAndGrates();
            BuildBlockClutter();
        }

        const float PoleHeight = 8.62f;
        static readonly float[] PoleArms = { -0.9f, 0f, 0.9f };

        static void BuildUtilityLines()
        {
            var poles = Sub("UtilityPoles");
            var wires = Sub("Wires");
            var look = new Look { Wood = Palette.PoleWood };
            for (int axis = 0; axis < 2; axis++)               // 0: roads along Z (x = road), 1: roads along X (z = road)
            {
                for (int ri = 0; ri < RoadCenters.Length; ri++)
                {
                    float road = RoadCenters[ri];
                    float side = (ri + axis) % 2 == 0 ? 1f : -1f;
                    float lineOff = road + side * 6.45f;       // sidewalk strip next to the curb
                    var placed = new List<Vector3>();
                    var yawDeg = axis == 0 ? 0f : 90f;
                    foreach (float bc in BlockCenters)
                    {
                        // Slide along the sidewalk until the spot is free of trees, furniture and spawn points.
                        foreach (float shift in new[] { 5f, -5f, 7.5f, -7.5f, 2.5f })
                        {
                            float s = bc + shift;
                            var pos = axis == 0 ? new Vector3(lineOff, SidewalkTop, s) : new Vector3(s, SidewalkTop, lineOff);
                            if (NearSpawnCandidate(new Vector2(pos.x, pos.z), 3f)) continue;
                            if (Physics.CheckCapsule(pos + Vector3.up * 0.9f, pos + Vector3.up * 3f, 0.45f, ~0, QueryTriggerInteraction.Ignore)) continue;
                            var go = Spawn("UtilityPole", poles, pos, yawDeg, look);
                            ThinBox(go, 0.32f, 9f);
                            Physics.SyncTransforms();
                            placed.Add(pos);
                            break;
                        }
                    }
                    for (int i = 0; i + 1 < placed.Count; i++)
                    {
                        if (Vector3.Distance(placed[i], placed[i + 1]) > 48f) continue;
                        var rotP = Quaternion.Euler(0f, yawDeg, 0f);
                        for (int w = 0; w < 4; w++)
                        {
                            var local = w < 3 ? new Vector3(PoleArms[w], PoleHeight, 0f) : new Vector3(0f, PoleHeight + 0.56f, 0f);
                            Vector3 a = placed[i] + rotP * local;
                            Vector3 b = placed[i + 1] + rotP * local;
                            PlaceWire(wires, a, b);
                        }
                    }
                }
            }
        }

        static void PlaceWire(Transform parent, Vector3 a, Vector3 b)
        {
            var dir = b - a;
            float len = dir.magnitude;
            dir /= len;
            var z = Vector3.Cross(dir, Vector3.up).normalized;
            var up = Vector3.Cross(z, dir);
            var w = Spawn("Wire", parent, a, 0f, null, "Wire");
            w.transform.position = a;
            w.transform.rotation = Quaternion.LookRotation(z, up);
            w.transform.localScale = Vector3.one * len;
            NoShadows(w);
        }

        static void BuildManholesAndGrates()
        {
            var manholes = Sub("Manholes");
            var grates = Sub("StormGrates");
            // Manholes in the middle lanes, one per street segment most of the time.
            foreach (float road in RoadCenters)
                for (int i = 0; i + 1 < RoadCenters.Length; i++)
                {
                    foreach (int axis in new[] { 0, 1 })
                    {
                        if (!Gp(0.8f)) continue;
                        float s = Gf(RoadCenters[i] + 12f, RoadCenters[i + 1] - 12f);
                        float o = (Gp(0.5f) ? 1f : -1f) * Gf(1.8f, 3.2f);
                        var pos = axis == 0 ? new Vector3(road + o, 0f, s) : new Vector3(s, 0f, road + o);
                        if (NearSpawnCandidate(new Vector2(pos.x, pos.z), 3f)) continue;
                        if (!BoxFree(pos + Vector3.up * 0.35f, new Vector3(0.45f, 0.3f, 0.45f), 0f)) continue;
                        Spawn("Manhole", manholes, pos, Gf(0f, 360f));
                    }
                }
            // Storm grates in the gutter at the foot of the curb, mid-block.
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                    foreach (var n in GritSides)
                    {
                        if (!Gp(0.5f)) continue;
                        var t = new Vector2(-n.y, n.x);
                        var p2 = new Vector2(cx, cz) + n * 13.35f + t * Gf(-7f, 7f);
                        if (NearSpawnCandidate(p2, 2.5f)) continue;
                        // The curb throat sits in the curb face (13.0 from the block centre); the grate lies in the gutter.
                        var pos = new Vector3(p2.x - n.x * 0.35f, 0f, p2.y - n.y * 0.35f);
                        float yaw = Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg;
                        if (!BoxFree(pos + Vector3.up * 0.45f, new Vector3(0.7f, 0.3f, 0.5f), yaw)) continue;
                        Spawn("CurbDrain", grates, pos, yaw);
                    }
        }

        // Dumpsters and bags against walls in alleys and courtyards, news boxes on sidewalks.
        static void BuildBlockClutter()
        {
            var dumpsters = Sub("Dumpsters");
            var bags = Sub("TrashBags");
            var boxes = Sub("NewsBoxes");
            var dumpLooks = new[] { Palette.DumpsterGreen, Palette.DumpsterBlue, Palette.BarrelRed };
            int dn = 0;
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                {
                    if (Mathf.Approximately(cx, 0f) && Mathf.Approximately(cz, 0f)) continue;   // plaza
                    var block = new Vector2(cx, cz);

                    // Dumpster: find open floor inside the block, snap to the nearest wall, check 2 m of clearance in front.
                    int want = Gp(0.8f) ? 1 : 0;
                    for (int attempt = 0; attempt < 40 && want > 0; attempt++)
                    {
                        var pt = block + new Vector2(Gf(-9.5f, 9.5f), Gf(-9.5f, 9.5f));
                        var o = new Vector3(pt.x, SidewalkTop + 1.0f, pt.y);
                        if (Physics.CheckSphere(o, 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (!NearestWall(o, 3.4f, out var hit)) continue;
                        var nrm = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                        float yaw = Mathf.Atan2(nrm.x, nrm.z) * Mathf.Rad2Deg;
                        var pos = new Vector3(hit.point.x, SidewalkTop, hit.point.z) + nrm * 0.64f;
                        if (NearSpawnCandidate(new Vector2(pos.x, pos.z), 2.5f)) continue;
                        if (!BoxFree(pos + Vector3.up * 0.7f, new Vector3(0.95f, 0.5f, 0.6f), yaw)) continue;
                        var front = pos + nrm * (0.55f + 1.15f) + Vector3.up * 1.25f;
                        if (!BoxFree(front, new Vector3(1.1f, 0.9f, 1.15f), yaw)) continue;
                        var d = Spawn("Dumpster", dumpsters, pos, yaw, new Look { Paint = dumpLooks[dn++ % dumpLooks.Length] });
                        FitBox(d, 0.1f);
                        Physics.SyncTransforms();
                        want--;
                        // Bags piled beside it.
                        var tang = new Vector3(nrm.z, 0f, -nrm.x);
                        foreach (float sgn in new[] { 1f, -1f })
                        {
                            if (!Gp(0.7f)) continue;
                            var bp = pos + tang * sgn * 1.55f + nrm * 0.1f;
                            if (!BoxFree(bp + Vector3.up * 0.45f, new Vector3(0.55f, 0.3f, 0.55f), yaw)) continue;
                            if (!BoxFree(bp + nrm * 1.3f + Vector3.up * 1.2f, new Vector3(0.6f, 0.9f, 0.6f), yaw)) continue;
                            NoShadows(Spawn("TrashBags", bags, bp, Gf(0f, 360f)));
                            break;
                        }
                    }

                    // News boxes against street-facing walls.
                    int boxesWanted = 1 + (Gp(0.4f) ? 1 : 0);
                    for (int attempt = 0; attempt < 24 && boxesWanted > 0; attempt++)
                    {
                        var n = GritSides[_grit.Next(4)];
                        var t = new Vector2(-n.y, n.x);
                        var s2 = block + n * 12.4f + t * Gf(-6f, 6f);
                        var o = new Vector3(s2.x, SidewalkTop + 0.9f, s2.y);
                        var dirIn = new Vector3(-n.x, 0f, -n.y);
                        if (!Physics.Raycast(o, dirIn, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (hit.collider.name != "Building" || Mathf.Abs(hit.normal.y) > 0.1f) continue;
                        var nrm = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                        var pos = new Vector3(hit.point.x, SidewalkTop, hit.point.z) + nrm * 0.3f;
                        if (NearSpawnCandidate(new Vector2(pos.x, pos.z), 2.2f)) continue;
                        float yaw = Mathf.Atan2(nrm.x, nrm.z) * Mathf.Rad2Deg;
                        if (!BoxFree(pos + Vector3.up * 0.6f, new Vector3(0.3f, 0.55f, 0.3f), yaw)) continue;
                        if (!BoxFree(pos + nrm * 1.0f + Vector3.up * 1.3f, new Vector3(0.7f, 0.9f, 0.8f), yaw)) continue;
                        var nb = Spawn("NewsBox", boxes, pos, yaw, new Look { Paint = Gp(0.5f) ? Palette.NewsBoxBlue : Palette.NewsBoxRed });
                        FitBox(nb, 0.1f);
                        Physics.SyncTransforms();
                        boxesWanted--;
                    }
                }
        }

        // Closest vertical wall of a building within reach of o (four horizontal rays).
        static bool NearestWall(Vector3 o, float reach, out RaycastHit best)
        {
            best = default;
            float bd = float.MaxValue;
            bool found = false;
            foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
                if (Physics.Raycast(o, d, out var hit, reach, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.name == "Building" && Mathf.Abs(hit.normal.y) < 0.1f && hit.distance < bd)
                { bd = hit.distance; best = hit; found = true; }
            return found;
        }
    }
}
