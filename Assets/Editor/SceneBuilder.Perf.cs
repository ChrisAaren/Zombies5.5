using System.Collections.Generic;
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
        static Vector2Int MergeBuildingRenderers(Transform building, ShadowCastingMode shadows)
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
                AssetDatabase.CreateAsset(new Mesh { name = "MergedRoot" }, MergedAssetPath);
                _mergedAssetStarted = true;
            }
            int after = 0;
            foreach (var m in order)
            {
                var mesh = new Mesh { name = building.name + "_" + m.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(groups[m].ToArray(), true, true);
                mesh.RecalculateBounds();
                AssetDatabase.AddObjectToAsset(mesh, MergedAssetPath);
                var go = new GameObject("Merged_" + m.name);
                go.transform.SetParent(building, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = shadows;
                after++;
            }
            return new Vector2Int(before, after);
        }

        // The skyline ring is only ever seen from inside the grid: one mesh per material per building.
        static void MergeSkyline()
        {
            var sky = _env.Find("Skyline");
            if (sky == null) return;
            int before = 0, after = 0;
            foreach (Transform b in sky)
            {
                var v = MergeBuildingRenderers(b, ShadowCastingMode.Off);
                before += v.x; after += v.y;
            }
            Debug.Log("Skyline merge: " + before + " renderers -> " + after);
        }
    }
}
