using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    // Draw-call reduction: far, never-touched geometry is welded into one mesh per material per building.
    public static partial class SceneBuilder
    {
        const string MergedAssetPath = GeneratedDir + "/MergedBuildings.asset";
        static bool _mergedAssetStarted;

        // Merges every MeshRenderer under `building` (static, no scripts) into one renderer per material. Colliders and
        // non-renderer children survive; emptied renderer objects are destroyed. Returns the renderer count before / after.
        static Vector2Int MergeBuildingRenderers(Transform building, ShadowCastingMode shadows, float dropTrimBelow = 0f)
        {
            var inv = building.worldToLocalMatrix;
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var order = new List<Material>();
            var doomed = new List<GameObject>();
            int before = 0;
            foreach (var mr in building.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                before++;
                var mats = mr.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    var m = mats[Mathf.Min(s, mats.Length - 1)];
                    if (m == null) continue;
                    // Far-away window sills and lintels read as noise at skyline range; large cornices stay.
                    if (dropTrimBelow > 0f && m.name.StartsWith("StoneTrim") && mr.bounds.size.magnitude < dropTrimBelow) continue;
                    if (!groups.TryGetValue(m, out var list)) { list = new List<CombineInstance>(); groups[m] = list; order.Add(m); }
                    list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = inv * mr.transform.localToWorldMatrix });
                }
                doomed.Add(mr.gameObject);
            }
            foreach (var go in doomed)
            {
                if (go == building.gameObject) continue;
                var mr = go.GetComponent<MeshRenderer>();
                Object.DestroyImmediate(mr);
                Object.DestroyImmediate(go.GetComponent<MeshFilter>());
                bool empty = go.transform.childCount == 0 && go.GetComponents<Component>().Length == 1;
                if (empty) Object.DestroyImmediate(go);
            }
            EnsureFolder(GeneratedDir);
            if (!_mergedAssetStarted)
            {
                AssetDatabase.DeleteAsset(MergedAssetPath);
                if (System.IO.File.Exists(MergedAssetPath)) System.IO.File.Delete(MergedAssetPath);
                AssetDatabase.CreateAsset(new Mesh { name = "MergedRoot" }, MergedAssetPath);
                _mergedAssetStarted = true;
            }
            int after = 0;
            foreach (var m in order)
            {
                // Chunks stay under the 16-bit index limit so the stored index buffer is half the size.
                var chunk = new List<CombineInstance>();
                int verts = 0, part = 0;
                foreach (var ci in groups[m])
                {
                    int vc = ci.mesh.vertexCount;
                    if (chunk.Count > 0 && verts + vc > 64000) { after += EmitMerged(building, m, chunk, shadows, part++); chunk.Clear(); verts = 0; }
                    chunk.Add(ci); verts += vc;
                }
                if (chunk.Count > 0) after += EmitMerged(building, m, chunk, shadows, part);
            }
            return new Vector2Int(before, after);
        }


        [StructLayout(LayoutKind.Sequential)]
        struct PackedVertex { public Vector3 pos; public sbyte nx, ny, nz, nw, tx, ty, tz, tw; public Vector2 uv; }

        [StructLayout(LayoutKind.Sequential)]
        struct FlatVertex { public Vector3 pos; public sbyte nx, ny, nz, nw; }

        static sbyte Snorm(float f) { return (sbyte)Mathf.Clamp(Mathf.RoundToInt(f * 127f), -127, 127); }

        // Position + SNorm8 normal (+ tangent, and UV only when the material samples textures): 16 or 28 bytes per vertex
        // instead of 56, with 16-bit indices.
        static int EmitMerged(Transform building, Material m, List<CombineInstance> chunk, ShadowCastingMode shadows, int part)
        {
            var big = new Mesh { indexFormat = IndexFormat.UInt32 };
            big.CombineMeshes(chunk.ToArray(), true, true);
            var pos = big.vertices; var nor = big.normals; var tan = big.tangents; var uv = big.uv;
            bool textured = (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null)
                || (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null) || (m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") != null);
            int n = pos.Length;
            var mesh = new Mesh { name = building.name + "_" + m.name + (part > 0 ? "_" + part : ""), indexFormat = n <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32 };
            if (textured)
            {
                var vd = new PackedVertex[n];
                for (int i = 0; i < n; i++)
                {
                    var nn = nor.Length > i ? nor[i] : Vector3.up;
                    var t = tan.Length > i ? tan[i] : new Vector4(1f, 0f, 0f, 1f);
                    vd[i] = new PackedVertex { pos = pos[i], nx = Snorm(nn.x), ny = Snorm(nn.y), nz = Snorm(nn.z), tx = Snorm(t.x), ty = Snorm(t.y), tz = Snorm(t.z), tw = Snorm(t.w), uv = uv.Length > i ? uv[i] : Vector2.zero };
                }
                mesh.SetVertexBufferParams(n,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.SNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
                mesh.SetVertexBufferData(vd, 0, 0, n);
            }
            else
            {
                var vd = new FlatVertex[n];
                for (int i = 0; i < n; i++)
                {
                    var nn = nor.Length > i ? nor[i] : Vector3.up;
                    vd[i] = new FlatVertex { pos = pos[i], nx = Snorm(nn.x), ny = Snorm(nn.y), nz = Snorm(nn.z) };
                }
                mesh.SetVertexBufferParams(n,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4));
                mesh.SetVertexBufferData(vd, 0, 0, n);
            }
            var tris = big.triangles;
            mesh.SetIndexBufferParams(tris.Length, mesh.indexFormat);
            if (mesh.indexFormat == IndexFormat.UInt16)
            {
                var small = new ushort[tris.Length];
                for (int i = 0; i < small.Length; i++) small[i] = (ushort)tris[i];
                mesh.SetIndexBufferData(small, 0, 0, small.Length);
            }
            else mesh.SetIndexBufferData(tris, 0, 0, tris.Length);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, tris.Length));
            mesh.RecalculateBounds();
            Object.DestroyImmediate(big);
            AssetDatabase.AddObjectToAsset(mesh, MergedAssetPath);
            var go = new GameObject("Merged_" + m.name + (part > 0 ? "_" + part : ""));
            go.transform.SetParent(building, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadows;
            return 1;
        }

        // The skyline ring is only ever seen from inside the grid: one mesh per material per building.
        static void MergeSkyline()
        {
            var sky = _env.Find("Skyline");
            if (sky == null) return;
            _mergedAssetStarted = false;   // a fresh asset per build: appending to the previous one would store every mesh twice
            int before = 0, after = 0;
            foreach (Transform b in sky)
            {
                var v = MergeBuildingRenderers(b, ShadowCastingMode.Off, 2.2f);
                before += v.x; after += v.y;
            }
            Debug.Log("Skyline merge: " + before + " renderers -> " + after);
        }
    }
}
