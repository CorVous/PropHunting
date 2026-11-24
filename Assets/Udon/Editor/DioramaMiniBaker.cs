using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Cheap stand-ins for the diorama's copies of the map's props (Cor, lobby profiling 2026-11-24: after an XL round
// the table held 1,132 props and about 767,000 vertices, the most likely cause of slow lobby frames on Quest).
// Each prop's mesh is simplified by vertex clustering on a coarse grid (a prop is a few centimetres tall on the
// table): vertices in one cell merge into one, at their average position, colour and normal, and triangles that
// collapse are dropped. Kept in Generated/DioramaMini.asset by def index; PHMiniMap draws them with one template
// object instead of instantiating the full prefab.
public static class DioramaMiniBaker
{
    const string Path = "Assets/Udon/Generated/DioramaMini.asset";
    const int Cells = 8;          // grid cells along the prop's longest side
    const int MaxVerts = 300;     // stay under dynamic batching's limit (Android has it on)

    public static Mesh[] Bake(GameObject[] prefabs)
    {
        var existing = new Dictionary<string, Mesh>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Path)) if (o is Mesh) existing[o.name] = (Mesh)o;
        var result = new Mesh[prefabs.Length];
        long before = 0, after = 0;
        for (int i = 0; i < prefabs.Length; i++)
        {
            var p = prefabs[i];
            if (p == null) continue;
            var mf = p.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            // a prop in several pieces (an openable's door) keeps its full prefab
            bool pieces = false;
            foreach (var other in p.GetComponentsInChildren<MeshFilter>(true)) if (other != mf && other.name != "Shade") pieces = true;
            if (pieces) continue;
            Mesh src = mf.sharedMesh;
            before += src.vertexCount;
            string name = "mini_" + i;
            Mesh mesh;
            bool isNew = !existing.TryGetValue(name, out mesh);
            if (isNew) mesh = new Mesh { name = name }; else mesh.Clear();
            // the prefab root's scale goes into the vertices (PHMiniMap's template has scale 1)
            Simplify(src, p.transform.localScale, mesh);
            after += mesh.vertexCount;
            if (isNew)
            {
                if (AssetDatabase.LoadMainAssetAtPath(Path) == null) AssetDatabase.CreateAsset(mesh, Path);
                else AssetDatabase.AddObjectToAsset(mesh, Path);
            }
            EditorUtility.SetDirty(mesh);
            result[i] = mesh;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[diorama] stand-in props: " + before + " -> " + after + " vertices over " + prefabs.Length + " props");
        return result;
    }

    static void Simplify(Mesh src, Vector3 scale, Mesh dst)
    {
        var v = src.vertices; var n = src.normals; var c = src.colors;
        for (int q = 0; q < v.Length; q++) v[q] = Vector3.Scale(v[q], scale);
        var b = new Bounds(v.Length > 0 ? v[0] : Vector3.zero, Vector3.zero);
        foreach (var x in v) b.Encapsulate(x);
        float big = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        int[] tris = src.triangles;
        // coarser until it fits under MaxVerts (a few props are very detailed)
        for (int cells = Cells; ; cells = Mathf.Max(2, cells * 3 / 4))
        {
            float cell = Mathf.Max(big / cells, 1e-4f);
            var map = new Dictionary<long, int>();
            var idx = new int[v.Length];
            var sumP = new List<Vector3>(); var sumN = new List<Vector3>(); var sumC = new List<Color>(); var cnt = new List<int>();
            for (int q = 0; q < v.Length; q++)
            {
                Vector3 r = (v[q] - b.min) / cell;
                long key = ((long)Mathf.FloorToInt(r.x) * 4096 + Mathf.FloorToInt(r.y)) * 4096 + Mathf.FloorToInt(r.z);
                // a cell keeps faces that point opposite ways apart (a thin board's two sides)
                Vector3 nn = q < n.Length ? n[q] : Vector3.up;
                int octant = (nn.x >= 0 ? 1 : 0) | (nn.y >= 0 ? 2 : 0) | (nn.z >= 0 ? 4 : 0);
                key = key * 8 + octant;
                int k;
                if (!map.TryGetValue(key, out k))
                {
                    k = sumP.Count; map[key] = k;
                    sumP.Add(Vector3.zero); sumN.Add(Vector3.zero); sumC.Add(new Color(0, 0, 0, 0)); cnt.Add(0);
                }
                idx[q] = k;
                sumP[k] += v[q]; sumN[k] += nn; sumC[k] += q < c.Length ? c[q] : Color.white; cnt[k]++;
            }
            if (sumP.Count > MaxVerts && cells > 2) continue;
            var outT = new List<int>();
            var seen = new HashSet<long>();
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = idx[tris[t]], bb = idx[tris[t + 1]], cc = idx[tris[t + 2]];
                if (a == bb || bb == cc || a == cc) continue;
                // the same triangle twice (two faces that merged) once only
                int lo = Mathf.Min(a, Mathf.Min(bb, cc)), hi = Mathf.Max(a, Mathf.Max(bb, cc)), mid = a + bb + cc - lo - hi;
                long tk = ((long)lo * 65536 + mid) * 65536 + hi;
                if (!seen.Add(tk * 2 + (IsCcw(a, bb, cc, lo) ? 1 : 0))) continue;
                outT.Add(a); outT.Add(bb); outT.Add(cc);
            }
            var pv = new Vector3[sumP.Count]; var pn = new Vector3[sumP.Count]; var pc = new Color[sumP.Count];
            for (int k = 0; k < pv.Length; k++)
            {
                pv[k] = sumP[k] / cnt[k];
                pn[k] = sumN[k].sqrMagnitude > 1e-8f ? sumN[k].normalized : Vector3.up;
                pc[k] = sumC[k] / cnt[k];
            }
            dst.vertices = pv; dst.normals = pn; dst.colors = pc;
            dst.triangles = outT.ToArray();
            dst.RecalculateBounds();
            return;
        }
    }

    // the winding of a, b, c as seen from its smallest index (keeps a face and its back apart)
    static bool IsCcw(int a, int b, int c, int lo)
    {
        if (a == lo) return b < c;
        if (b == lo) return c < a;
        return a < b;
    }
}
