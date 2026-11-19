using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Zombies55.EditorTools
{
    // Street dirt and clutter pass: oil stains, asphalt patches, sealed cracks, gutter leaves and litter decals (one combined
    // mesh from the StreetDirt atlas), extra street trees, and trash bags against shop walls. Uses its own RNG so the base layout keeps its seed.
    public static partial class SceneBuilder
    {
        static System.Random _dirt;

        // Atlas cells (4 x 4): 0-3 oil, 4-5 patches, 6 cracks, 7 gum, 8 paper, 9 newspaper, 10 cups, 11 wrappers,
        // 12 grime stain, 13 leaves, 14 dirt streak, 15 butts.
        static readonly int[] LitterCells = {
            8, 9, 9, 10, 11, 13, 15 };

        static float Df(float a, float b) {
            return a + (float)_dirt.NextDouble() * (b - a); }

        static void BuildStreetDirt()
        {
            _dirt = new System.Random(4242);
            var acc = new MeshAcc();
            float yRoad = 0.012f, ySide = SidewalkTop + 0.012f;

            for (int axis = 0; axis < 2; axis++)
                foreach (float road in RoadCenters)
                    for (int seg = 0; seg + 1 < RoadCenters.Length; seg++)
                    {
                        float a = RoadCenters[seg] + 9f, b = RoadCenters[seg + 1] - 9f;
                        float yaw = axis == 0 ? 0f : 90f;
                        Vector3 P(float lat, float s, float y) {
            return axis == 0 ? new Vector3(road + lat, y, s) : new Vector3(s, y, road + lat); }
                        for (int k = 0; k < 12; k++)                                   // oil drips in the lanes and parking strips
                        {

                            float lat = (_dirt.Next(2) == 0 ? -1f : 1f) * (_dirt.Next(3) == 0 ? Df(1.3f, 2.6f) : Df(3.4f, 5f));
                            float sz = Df(1.6f, 3.4f);
                            AddMarkTile(acc, _dirt.Next(4), P(lat, Df(a, b), yRoad), Df(0f, 360f), sz, sz * Df(0.8f, 1.2f));
                        }
                        for (int k = 0; k < 2; k++)
                            AddMarkTile(acc, 12, P((_dirt.Next(2) == 0 ? -1f : 1f) * Df(1f, 4f), Df(a, b), yRoad), yaw, Df(3f, 5f), Df(4f, 7f));
                        if (_dirt.NextDouble() < 0.85)
                            AddMarkTile(acc, 4 + _dirt.Next(2), P((_dirt.Next(2) == 0 ? -1f : 1f) * Df(1.5f, 3.2f), Df(a, b), yRoad + 0.002f), yaw + (_dirt.Next(2) == 0 ? 0f : 90f), Df(1.2f, 1.9f), Df(1.4f, 2.2f));
                        if (_dirt.NextDouble() < 0.9)
                            AddMarkTile(acc, 6, P(Df(-3f, 3f), Df(a, b), yRoad + 0.002f), Df(0f, 360f), Df(2.5f, 4f), Df(2.5f, 4f));
                        for (int side = -1; side <= 1; side += 2)
                            for (int k = 0; k < 9; k++)                                // gutter litter and leaves
                            {
                                int cell = LitterCells[_dirt.Next(LitterCells.Length)];
                                float sz = cell == 13 ? Df(1.2f, 2f) : Df(0.5f, 0.9f);
                                AddMarkTile(acc, cell, P(side * Df(5.3f, 5.9f), Df(a, b), yRoad + 0.003f), Df(0f, 360f), sz, sz);
                            }
                    }

            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                {

                    if (Mathf.Approximately(cx, 0f) && Mathf.Approximately(cz, 0f)) continue;
                    foreach (var n in SideNormals)
                    {
                        var t = new Vector2(-n.y, n.x);
                        float yaw = n.x != 0f ? 90f : 0f;
                        Vector3 P(float dist, float along) {
            var q = new Vector2(cx, cz) + n * dist + t * along; return new Vector3(q.x, ySide, q.y); }
                        for (int k = 0; k < 14; k++)                                   // litter and gum across the walkway
                        {

                            int cell = _dirt.NextDouble() < 0.25 ? 7 : LitterCells[_dirt.Next(LitterCells.Length)];
                            float sz = cell == 13 ? Df(1.2f, 2f) : cell == 7 ? Df(1.2f, 2f) : Df(0.5f, 0.9f);
                            AddMarkTile(acc, cell, P(Df(10.4f, 12.9f), Df(-10f, 10f)), Df(0f, 360f), sz, sz);
                        }
                        for (int k = 0; k < 3; k++)                                    // grime along the wall base and the curb
                            AddMarkTile(acc, 14, P(Df(10.3f, 10.9f), Df(-8f, 8f)), yaw, Df(4f, 6f), Df(2f, 3f));
                        for (int k = 0; k < 2; k++)
                            AddMarkTile(acc, 12, P(Df(11f, 12.6f), Df(-9f, 9f)), Df(0f, 360f), Df(2.5f, 4f), Df(2.5f, 4f));
                        if (_dirt.NextDouble() < 0.7)
                            AddMarkTile(acc, _dirt.Next(4), P(Df(11f, 12.5f), Df(-9f, 9f)), Df(0f, 360f), Df(1f, 1.8f), Df(1f, 1.8f));
                    }
                }

            var mesh = SaveMarkMesh("StreetDirtV2", acc);
            var mat = DecalMat("StreetDirtV2", "StreetDirt", new Color(0.52f, 0.48f, 0.44f, 1f));
            mat.SetFloat("_Smoothness", 0.02f);
            var go = new GameObject("StreetDirtV2");
            go.transform.SetParent(Sub("Dirt"), false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            Debug.Log("Dirt: " + acc.I.Count / 6 + " decal tiles");
        }

        // More street trees along the curb lane of every block: only where the spot is clear.
        static void BuildExtraStreetTrees()
        {

            Physics.SyncTransforms();
            int placed = 0;
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                {
                    if (Mathf.Approximately(cx, 0f) && Mathf.Approximately(cz, 0f)) continue;
                    foreach (var n in SideNormals)
                    {

                        var t = new Vector2(-n.y, n.x);
                        int want = 1 + (_v2.NextDouble() < 0.4 ? 1 : 0);
                        for (int attempt = 0; attempt < 10 && want > 0; attempt++)
                        {

                            var q = new Vector2(cx, cz) + n * 12.0f + t * (float)(_v2.NextDouble() * 20.0 - 10.0);
                            var p = new Vector3(q.x, SidewalkTop, q.y);
                            if (!SidewalkSpotOk(p, 0.9f, 3f, 2.4f)) continue;
                            MakeStreetTree(p);
                            Physics.SyncTransforms();
                            want--; placed++;
                        }
                    }
                }
            Debug.Log("Trees: " + placed + " extra street trees");
        }

        // Black bags against the street-facing walls, where the shops are.
        static void BuildShopTrash()
        {
            _dirt = new System.Random(4343);
            var bags = Sub("TrashBags");
            int placed = 0;
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                {

                    if (Mathf.Approximately(cx, 0f) && Mathf.Approximately(cz, 0f)) continue;
                    foreach (var n in SideNormals)
                    {
                        if (_dirt.NextDouble() < 0.4) continue;
                        var t = new Vector2(-n.y, n.x);
                        for (int attempt = 0; attempt < 10; attempt++)
                        {
                            var s2 = new Vector2(cx, cz) + n * 12.4f + t * Df(-8f, 8f);
                            var o = new Vector3(s2.x, SidewalkTop + 0.9f, s2.y);
                            var dirIn = new Vector3(-n.x, 0f, -n.y);
                            if (!Physics.Raycast(o, dirIn, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                            if (hit.collider.name != "Building" || Mathf.Abs(hit.normal.y) > 0.1f) continue;
                            var nrm = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                            var pos = new Vector3(hit.point.x, SidewalkTop, hit.point.z) + nrm * 0.5f;
                            if (NearSpawnCandidate(new Vector2(pos.x, pos.z), 2.2f) || NearKiosk(new Vector2(pos.x, pos.z), 2.2f)) continue;
                            if (!BoxFree(pos + Vector3.up * 0.45f, new Vector3(0.6f, 0.35f, 0.6f), 0f)) continue;
                            NoShadows(Spawn("TrashBags", bags, pos, Df(0f, 360f)));
                            placed++;
                            break;
                        }
                    }
                }
            Debug.Log("Trash: " + placed + " bag piles at shop fronts");
        }
    }
}
