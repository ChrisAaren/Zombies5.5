using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    // V2 street pass: street trees, traffic signals and signs, curbside furniture, road markings, densely parked cars,
    // baked reflection probes.
    public static partial class SceneBuilder
    {
        static readonly List<Vector2> _hydrants = new List<Vector2>();
        static readonly List<Collider> _carColliders = new List<Collider>();
        static readonly List<Renderer> _signalZ = new List<Renderer>();
        static readonly List<Renderer> _signalX = new List<Renderer>();
        static Transform _signalRoot;

        static bool NearKiosk(Vector2 p, float r)
        {
            float r2 = r * r;
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                    foreach (var n in SideNormals)
                        foreach (float s in new[] { -8.8f, 8.8f })
                        {
                            var q = new Vector2(cx, cz) + n * KioskLine + new Vector2(-n.y, n.x) * s;
                            if ((q - p).sqrMagnitude < r2) return true;
                        }
            return false;
        }

        // A sidewalk spot that is free of colliders and well clear of spawn candidates and kiosk positions.
        static bool SidewalkSpotOk(Vector3 p, float radius, float height, float clear)
        {
            var v = new Vector2(p.x, p.z);
            if (NearSpawnCandidate(v, clear) || NearKiosk(v, clear)) return false;
            return !Physics.CheckCapsule(p + Vector3.up * 0.9f, p + Vector3.up * Mathf.Max(1.2f, height), radius, ~0, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ trees

        static void MakeStreetTree(Vector3 basePos)
        {
            string[] models = { "StreetTree_A", "StreetTree_B", "StreetTree_C" };
            var tints = new[] { new Color(0.92f, 0.96f, 0.84f), new Color(1f, 1f, 0.86f), new Color(0.84f, 0.92f, 0.78f) };
            string model = models[(_treeCount * 7 + _treeCount / 3) % 3];
            _treeCount++;
            var look = new Look { Leaf = tints[_v2.Next(tints.Length)] };
            var trees = Sub("Trees");
            Spawn("TreePitGrate", trees, basePos, 0f, look, "TreePit");
            var tree = Spawn(model, trees, basePos, (float)_v2.NextDouble() * 360f, look, "Tree");
            tree.transform.localScale = Vector3.one * (0.9f + (float)_v2.NextDouble() * 0.2f);
            ThinBox(tree, 0.4f, 3f);
        }

        // ------------------------------------------------------------------ intersections

        static GameObject SpawnSignalPole(Vector3 pos, float yaw, string axis)
        {
            var look = new Look { Axis = axis };
            var holder = Node("TrafficLight_" + axis, _signalRoot, pos);
            var go = Spawn("TrafficLight_Pole", holder, Vector3.zero, yaw, look, "TrafficLight_Pole");
            go.transform.localScale = new Vector3(-1f, 1f, 1f);     // the arm is authored toward -X; mirror it so the heads face traffic
            ThinBox(holder.gameObject, 0.3f, 6f);                    // box colliders cannot sit on a mirrored transform
            var list = axis == "Z" ? _signalZ : _signalX;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) list.Add(r);
            return go;
        }

        static void BuildIntersections()
        {
            _signalRoot = Node("TrafficSignals", _game, Vector3.zero);
            var lamps = Sub("Lamps");
            var signs = Sub("Signs");
            int postIndex = 0;
            foreach (float rx in RoadCenters)
                foreach (float rz in RoadCenters)
                {
                    bool signalized = Mathf.Abs(rx) < 60f && Mathf.Abs(rz) < 60f;
                    var corners = new List<Vector2>();
                    foreach (float sx in new[] { -1f, 1f })
                        foreach (float sz in new[] { -1f, 1f })
                        {
                            if (Mathf.Abs(rx + sx * 19f) > 77f || Mathf.Abs(rz + sz * 19f) > 77f) continue;
                            corners.Add(new Vector2(sx, sz));
                            var c = new Vector3(rx + sx * 6.8f, SidewalkTop, rz + sz * 6.8f);
                            if (signalized)
                            {
                                float yaw; string axis;
                                if (sx > 0 && sz < 0) { yaw = 180f; axis = "Z"; }
                                else if (sx < 0 && sz > 0) { yaw = 0f; axis = "Z"; }
                                else if (sx < 0 && sz < 0) { yaw = -90f; axis = "X"; }
                                else { yaw = 90f; axis = "X"; }
                                SpawnSignalPole(c, yaw, axis);
                            }
                            else
                            {
                                var lamp = Spawn("StreetLamp_V2", lamps, c, Mathf.Atan2(-sz, sx) * Mathf.Rad2Deg);
                                ThinBox(lamp, 0.3f, 6f);
                                // Stop sign on the near-right corner of each approach.
                                Vector3 sp; float syaw;
                                if (sx == -sz) { sp = new Vector3(rx + sx * 6.4f, SidewalkTop, rz + sz * 8.4f); syaw = sz > 0 ? 0f : 180f; }
                                else { sp = new Vector3(rx + sx * 8.4f, SidewalkTop, rz + sz * 6.4f); syaw = sx > 0 ? 90f : 270f; }
                                var ss = Spawn("StopSign", signs, sp, syaw);
                                ThinBox(ss, 0.12f, 2.5f);
                            }
                        }
                    // One street-name post per intersection, on a random corner, clear of the pole.
                    if (corners.Count > 0)
                    {
                        var cn = corners[_v2.Next(corners.Count)];
                        var sp2 = new Vector3(rx + cn.x * 6.8f, SidewalkTop, rz + cn.y * 9.6f);
                        var post = Spawn((postIndex++ % 2 == 0) ? "StreetSign_Post" : "StreetSign_Post_B", signs, sp2, _v2.Next(2) * 90f);
                        ThinBox(post, 0.12f, 3.3f);
                    }
                }
        }

        // Cobra-head lamps along the curbs, mid-block, three per block.
        static void BuildBlockLamps()
        {
            var lamps = Sub("Lamps");
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (_v2.NextDouble() < 0.3) continue;
                        var n = SideNormals[i];
                        var t = new Vector2(-n.y, n.x);
                        float along = (_v2.Next(2) == 0 ? -1f : 1f) * (3f + (float)_v2.NextDouble() * 3f);
                        var p2 = new Vector2(cx, cz) + n * 12.35f + t * along;
                        var p = new Vector3(p2.x, SidewalkTop, p2.y);
                        if (!SidewalkSpotOk(p, 0.45f, 3f, 2.2f)) continue;
                        // The arm swings out over the street.
                        float yaw = Mathf.Atan2(-n.y, n.x) * Mathf.Rad2Deg;
                        var lamp = Spawn("StreetLamp_V2", lamps, p, yaw);
                        ThinBox(lamp, 0.3f, 6f);
                        Physics.SyncTransforms();
                    }
                }
        }

        // ------------------------------------------------------------------ curbside furniture

        static void BuildCurbside()
        {
            var meters = Sub("ParkingMeters");
            var misc = Sub("CurbsideProps");
            int busStops = 0, booths = 0, noPark = 0;
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                {
                    if (Mathf.Approximately(cx, 0f) && Mathf.Approximately(cz, 0f)) continue;
                    for (int i = 0; i < 4; i++)
                    {
                        var n = SideNormals[i];
                        var t = new Vector2(-n.y, n.x);
                        bool metered = _v2.NextDouble() < 0.5;
                        if (metered)
                        {
                            float start = -7f + (float)_v2.NextDouble() * 2f;
                            for (float a = start; a < 8f; a += 6.3f + (float)_v2.NextDouble() * 1.5f)
                            {
                                var p2 = new Vector2(cx, cz) + n * 12.5f + t * a;
                                var p = new Vector3(p2.x, SidewalkTop, p2.y);
                                if (!SidewalkSpotOk(p, 0.3f, 1.6f, 1.8f)) continue;
                                var m = Spawn("ParkingMeter", meters, p, Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg + 180f);
                                ThinBox(m, 0.2f, 1.5f);
                                Physics.SyncTransforms();
                            }
                        }
                        float rr = (float)_v2.NextDouble();
                        if (rr < 0.12 && (busStops < 6 || booths < 6 || noPark < 8))
                        {
                            string kind = busStops < 6 ? "BusStopSign" : booths < 6 ? "PhoneBooth" : "NoParkingSign";
                            float a = (float)(_v2.NextDouble() - 0.5) * 8f;
                            float line = kind == "PhoneBooth" ? 10.6f : 12.45f;
                            var p2 = new Vector2(cx, cz) + n * line + t * a;
                            var p = new Vector3(p2.x, SidewalkTop, p2.y);
                            if (!SidewalkSpotOk(p, kind == "PhoneBooth" ? 0.65f : 0.3f, 2.4f, 2.2f)) continue;
                            // Faces point along the sidewalk toward the street; the booth opens onto the walkway.
                            float yaw = Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg + (kind == "PhoneBooth" ? 0f : 90f);
                            var go = Spawn(kind, misc, p, yaw);
                            if (kind == "PhoneBooth") { FitBox(go, 0.1f); booths++; }
                            else { ThinBox(go, 0.2f, 2.5f); if (kind == "BusStopSign") busStops++; else noPark++; }
                            Physics.SyncTransforms();
                        }
                    }
                }
        }

        // ------------------------------------------------------------------ road markings

        class MeshAcc
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> I = new List<int>();
        }

        // One 2 x 2 m cell of the RoadMarkingsV2 atlas, laid flat. yaw 0: u along +x, v along +z. sx scales across, sz along.
        static void AddMarkTile(MeshAcc m, int cell, Vector3 c, float yaw, float sx = 2f, float sz = 2f)
        {
            const float e = 0.0015f;
            float u0 = (cell % 4) * 0.25f + e, v0 = 1f - (cell / 4 + 1) * 0.25f + e, u1 = u0 + 0.25f - 2f * e, v1 = v0 + 0.25f - 2f * e;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            int b = m.V.Count;
            m.V.Add(c + rot * new Vector3(-0.5f * sx, 0f, -0.5f * sz));
            m.V.Add(c + rot * new Vector3(-0.5f * sx, 0f, 0.5f * sz));
            m.V.Add(c + rot * new Vector3(0.5f * sx, 0f, 0.5f * sz));
            m.V.Add(c + rot * new Vector3(0.5f * sx, 0f, -0.5f * sz));
            m.UV.Add(new Vector2(u0, v0)); m.UV.Add(new Vector2(u0, v1)); m.UV.Add(new Vector2(u1, v1)); m.UV.Add(new Vector2(u1, v0));
            m.I.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }

        static Mesh SaveMarkMesh(string name, MeshAcc m)
        {
            EnsureFolder(GeneratedDir);
            string path = GeneratedDir + "/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(m.V);
            mesh.SetUVs(0, m.UV);
            var normals = new Vector3[m.V.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.SetTriangles(m.I, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static int PickMark(double q, int fresh, int worn, int cracked)
        {
            return q < 0.5 ? fresh : q < 0.85 ? worn : cracked;
        }

        static void BuildV2Markings(Transform lines)
        {
            var paint = DecalMat("RoadMarkingsV2", "RoadMarkingsV2", new Color(0.96f, 0.95f, 0.92f, 1f));
            var m = new MeshAcc();
            var dark = new MeshAcc();
            const float y = 0.02f;
            var rnd = _v2;
            var arms = new[] { new Vector2(0, 1), new Vector2(0, -1), new Vector2(1, 0), new Vector2(-1, 0) };

            for (int axis = 0; axis < 2; axis++)           // 0: road along Z at x = road; 1: road along X at z = road
                foreach (float road in RoadCenters)
                    for (int seg = 0; seg + 1 < RoadCenters.Length; seg++)
                    {
                        float a = RoadCenters[seg] + 9.6f, b = RoadCenters[seg + 1] - 9.6f;
                        float yaw = axis == 0 ? 0f : 90f;
                        // Mid-road dashes inside the stripe layers: double yellow centreline, white parking lines.
                        for (float s = a + 1f; s <= b; s += 2f)
                        {
                            Vector3 P(float lat) { return axis == 0 ? new Vector3(road + lat, y, s) : new Vector3(s, y, road + lat); }
                            AddMarkTile(m, PickMark(rnd.NextDouble(), 0, 1, 2), P(0f), yaw);
                            AddMarkTile(m, PickMark(rnd.NextDouble(), 3, 5, 6), P(-3.75f), yaw);
                            AddMarkTile(m, PickMark(rnd.NextDouble(), 3, 5, 6), P(3.75f), yaw);
                            double q = rnd.NextDouble();
                            if (q < 0.35) AddMarkTile(dark, 15, P(rnd.Next(2) == 0 ? -2.1f : 2.1f), yaw, 3.2f, 2f);
                            if (rnd.NextDouble() < 0.12) AddMarkTile(dark, 13, P((float)(rnd.NextDouble() - 0.5) * 8f), (float)rnd.NextDouble() * 360f);
                            if (rnd.NextDouble() < 0.035) AddMarkTile(dark, 14, P((float)(rnd.NextDouble() - 0.5) * 7f), yaw + (rnd.Next(2) == 0 ? 0f : 90f));
                        }
                    }

            // Crosswalks and stop bars on every approach of every intersection.
            foreach (float rx in RoadCenters)
                foreach (float rz in RoadCenters)
                    foreach (var d in arms)
                    {
                        var c = new Vector2(rx, rz) + d * 7.6f;
                        if (Mathf.Abs(c.x) > 100f || Mathf.Abs(c.y) > 100f) continue;
                        bool alongZ = d.y != 0f;
                        float yaw = alongZ ? 0f : 90f;
                        bool signalized = Mathf.Abs(rx) < 60f && Mathf.Abs(rz) < 60f;
                        if (!signalized && rnd.NextDouble() < 0.25) continue;
                        for (int k = 0; k < 6; k++)
                        {
                            float lat = -5f + 2f * k;
                            var p = alongZ ? new Vector3(c.x + lat, y, c.y) : new Vector3(c.x, y, c.y + lat);
                            AddMarkTile(m, rnd.NextDouble() < 0.7 ? 10 : 11, p, yaw);
                        }
                        // Stop bar across the lanes entering the intersection from this arm (right-hand traffic).
                        var lane = new Vector2(rx, rz) + d * 9.75f + new Vector2(-d.y, d.x) * 2f;
                        for (int k = 0; k < 2; k++)
                        {
                            float lat = k == 0 ? -0.7f : 0.7f;
                            var p = alongZ ? new Vector3(lane.x + lat, y, lane.y) : new Vector3(lane.x, y, lane.y + lat);
                            AddMarkTile(m, rnd.NextDouble() < 0.75 ? 8 : 9, p, yaw);
                        }
                    }

            var mesh = SaveMarkMesh("MarkingsV2", m);
            var go = new GameObject("RoadMarkingsV2");
            go.transform.SetParent(lines, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = paint;
            mr.shadowCastingMode = ShadowCastingMode.Off;

            var darkMesh = SaveMarkMesh("MarkingsV2Dark", dark);
            var dgo = new GameObject("RoadWearV2");
            dgo.transform.SetParent(lines, false);
            dgo.transform.localPosition = new Vector3(0f, -0.004f, 0f);
            dgo.AddComponent<MeshFilter>().sharedMesh = darkMesh;
            var dmr = dgo.AddComponent<MeshRenderer>();
            dmr.sharedMaterial = paint;
            dmr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ parked cars

        static readonly string[] CarModels =
            { "CarV2_Sedan", "CarV2_Coupe", "CarV2_Hatch", "CarV2_Wagon", "CarV2_Van", "CarV2_Pickup", "CarV2_Cab", "CarV2_SUV", "CarV2_Minivan" };
        static readonly float[] CarWeights = { 0.2f, 0.07f, 0.12f, 0.08f, 0.1f, 0.1f, 0.1f, 0.12f, 0.11f };
        static readonly Dictionary<string, Vector2> _carExtent = new Dictionary<string, Vector2>();   // z length, z centre offset

        static readonly Color[] CarColors =
        {
            new Color(0.78f, 0.78f, 0.76f), new Color(0.58f, 0.6f, 0.62f), new Color(0.15f, 0.16f, 0.17f), new Color(0.05f, 0.05f, 0.06f),
            new Color(0.07f, 0.12f, 0.24f), new Color(0.38f, 0.07f, 0.09f), new Color(0.09f, 0.2f, 0.15f), new Color(0.6f, 0.54f, 0.42f),
            new Color(0.33f, 0.47f, 0.62f), new Color(0.62f, 0.1f, 0.1f), new Color(0.72f, 0.72f, 0.7f), new Color(0.4f, 0.42f, 0.44f),
        };

        static Vector2 CarExtent(string model)
        {
            if (_carExtent.TryGetValue(model, out var e)) return e;
            var tmp = (GameObject)Object.Instantiate(Model(model));
            var b = LocalBounds(tmp);
            Object.DestroyImmediate(tmp);
            e = new Vector2(b.size.z, b.center.z);
            _carExtent[model] = e;
            return e;
        }

        static string PickCarModel()
        {
            float q = (float)_v2.NextDouble();
            for (int i = 0; i < CarModels.Length; i++) { q -= CarWeights[i]; if (q <= 0f) return CarModels[i]; }
            return CarModels[0];
        }

        const int MaxParkedCars = 600;

        struct CarSlot { public Vector3 Pos; public float Yaw; public string Model; public Color Paint; public float Dirt; public float Dist; }

        static void BuildParkedCars()
        {
            Physics.SyncTransforms();
            var slots = new List<CarSlot>();
            var player = new Vector3(0f, 0f, -11.5f);
            for (int axis = 0; axis < 2; axis++)
                foreach (float road in RoadCenters)
                    for (int seg = 0; seg + 1 < RoadCenters.Length; seg++)
                    {
                        float a = RoadCenters[seg] + 10.8f, b = RoadCenters[seg + 1] - 10.8f;
                        float mid = (RoadCenters[seg] + RoadCenters[seg + 1]) * 0.5f;
                        var mid2 = axis == 0 ? new Vector2(road, mid) : new Vector2(mid, road);
                        float dist = mid2.magnitude;
                        const float density = 0.94f;          // bumper to bumper on both curbs of every street, with the odd empty space
                        foreach (int side in new[] { -1, 1 })
                        {
                            float cursor = a + (float)_v2.NextDouble() * 1.2f;
                            for (int guard = 0; guard < 12; guard++)
                            {
                                string model = PickCarModel();
                                var ext = CarExtent(model);
                                float len = ext.x;
                                float c = cursor + len * 0.5f;
                                if (c + len * 0.5f > b) break;
                                cursor = c + len * 0.5f + 0.25f + (float)_v2.NextDouble() * 0.45f;
                                if (_v2.NextDouble() > density) continue;
                                float lat = side * (4.85f + ((float)_v2.NextDouble() - 0.5f) * 0.2f);
                                Vector3 pos = axis == 0 ? new Vector3(road + lat, 0f, c - ext.y) : new Vector3(c - ext.y, 0f, road + lat);
                                float yaw = axis == 0 ? (side > 0 ? 0f : 180f) : (side < 0 ? 90f : 270f);
                                if (_v2.NextDouble() < 0.07) yaw += 180f;
                                yaw += ((float)_v2.NextDouble() - 0.5f) * 5f;
                                var box = axis == 0 ? new Bounds(pos, new Vector3(1.9f, 2f, len)) : new Bounds(pos, new Vector3(len, 2f, 1.9f));
                                bool clear = box.SqrDistance(player) > 3.5f * 3.5f;
                                foreach (var h in _hydrants)
                                    if (clear && box.SqrDistance(new Vector3(h.x, 0f, h.y)) < 2.5f * 2.5f) clear = false;
                                if (!clear) continue;
                                Color paint = model == "CarV2_Cab" ? new Color(0.95f, 0.68f, 0.08f) : CarColors[_v2.Next(CarColors.Length)];
                                double dq = _v2.NextDouble();
                                slots.Add(new CarSlot { Pos = pos, Yaw = yaw, Model = model, Paint = paint, Dirt = dq < 0.45 ? 0f : dq < 0.8 ? 0.35f : 0.7f, Dist = dist });
                            }
                        }
                    }

            // Over the cap: drop slots at random, favouring the far ones, so every street keeps a believable rhythm.
            while (slots.Count > MaxParkedCars)
            {
                int i = _v2.Next(slots.Count);
                if (_v2.NextDouble() < Mathf.Clamp01(slots[i].Dist / 100f)) slots.RemoveAt(i);
            }
            foreach (var s in slots) MakeCar(s.Model, s.Pos, s.Yaw, s.Paint, s.Dirt);
            Debug.Log("Cars: " + slots.Count + " parked cars");
        }

        static void MakeCar(string model, Vector3 pos, float yaw, Color paint, float dirt)
        {
            var look = new Look { Car = paint, Dirt = dirt, Override = new Dictionary<string, Material> { { "Glass", CarGlassMat() } } };
            var car = Spawn(model, Sub("Cars"), pos, yaw, look, "ParkedCar");
            FitBox(car, 0.2f);
            var lod = car.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.01f, car.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();
        }

        // ------------------------------------------------------------------ signals controller

        static void BuildSignalController()
        {
            var go = new GameObject("TrafficSignalController");
            go.transform.SetParent(_game, false);
            var sig = go.AddComponent<TrafficSignals>();
            var z = new[] { SignalMat("Red", "Z", default), SignalMat("Amber", "Z", default), SignalMat("Green", "Z", default) };
            var x = new[] { SignalMat("Red", "X", default), SignalMat("Amber", "X", default), SignalMat("Green", "X", default) };
            sig.Configure(z, x, _signalZ.ToArray(), _signalX.ToArray());
            EditorUtility.SetDirty(sig);
        }

        // ------------------------------------------------------------------ reflection probes

        static void BuildReflectionProbes()
        {
            var root = Node("Reflection Probes", _env, Vector3.zero);
            const string dir = "Assets/Scenes/Zombies55/Probes";
            EnsureFolder(dir);
            foreach (float rx in RoadCenters)
                foreach (float rz in RoadCenters)
                {
                    var go = new GameObject("Probe_" + rx + "_" + rz);
                    go.transform.SetParent(root, false);
                    go.transform.position = new Vector3(rx, 6f, rz);
                    var probe = go.AddComponent<ReflectionProbe>();
                    probe.mode = ReflectionProbeMode.Baked;
                    probe.resolution = 256;
                    probe.boxProjection = true;
                    probe.hdr = true;
                    probe.importance = 1;
                    probe.intensity = 1f;
                    probe.size = new Vector3(38f, 32f, 38f);
                    probe.center = new Vector3(0f, 8f, 0f);
                    probe.nearClipPlane = 0.3f;
                    probe.farClipPlane = 300f;
                    string path = dir + "/Probe_" + rx + "_" + rz + ".exr";
                    if (Lightmapping.BakeReflectionProbe(probe, path))
                    {
                        AssetDatabase.ImportAsset(path);
                        probe.bakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                    }
                    else Debug.LogWarning("Reflection probe bake failed at " + rx + "," + rz);
                }
        }
    }
}
