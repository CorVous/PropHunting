using System.Collections.Generic;
using UnityEngine;

// Collects boxes and quads with vertex colours into one mesh (floors, walls, roofs).
public class MeshBuf
{
    public readonly List<Vector3> verts = new List<Vector3>();
    public readonly List<Vector3> norms = new List<Vector3>();
    public readonly List<Color32> cols = new List<Color32>();
    public readonly List<int> tris = new List<int>();

    public const int FACE_XN = 1, FACE_XP = 2, FACE_YN = 4, FACE_YP = 8, FACE_ZN = 16, FACE_ZP = 32, FACE_ALL = 63;

    // a, b, c, d clockwise seen from the side the normal points to
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color32 col)
    {
        int s = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
        norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
        cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
        tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
    }

    // the same with a colour per corner (blended ground)
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color32 ca, Color32 cb, Color32 cc, Color32 cd)
    {
        int s = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
        norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
        cols.Add(ca); cols.Add(cb); cols.Add(cc); cols.Add(cd);
        tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
    }

    public void Box(Vector3 mn, Vector3 mx, Color32 col, int faces = FACE_ALL)
    {
        Box(mn, mx, col, col, col, faces);
    }

    public void Box(Vector3 mn, Vector3 mx, Color32 side, Color32 top, Color32 bottom, int faces = FACE_ALL)
    {
        float x0 = mn.x, y0 = mn.y, z0 = mn.z, x1 = mx.x, y1 = mx.y, z1 = mx.z;
        if ((faces & FACE_ZP) != 0) Quad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, side);
        if ((faces & FACE_ZN) != 0) Quad(new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), Vector3.back, side);
        if ((faces & FACE_YP) != 0) Quad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up, top);
        if ((faces & FACE_YN) != 0) Quad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), Vector3.down, bottom);
        if ((faces & FACE_XP) != 0) Quad(new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), Vector3.right, side);
        if ((faces & FACE_XN) != 0) Quad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, side);
    }

    // G40: faces that lie in one plane, look the same way and overlap (by more than 1 mm each way) fight for the
    // same pixels. Counts such pairs among the axis-aligned quads, and how many of them differ in colour (those
    // flicker visibly). "pairs N, in two colours M".
    public static string Coplanar(MeshBuf b)
    {
        var groups = new Dictionary<long, List<float[]>>();
        for (int q = 0; q + 3 < b.verts.Count; q += 4)
        {
            Vector3 n = b.norms[q];
            int axis = Mathf.Abs(n.x) > 0.9f ? 0 : (Mathf.Abs(n.y) > 0.9f ? 1 : (Mathf.Abs(n.z) > 0.9f ? 2 : -1));
            if (axis < 0) continue;
            int u = axis == 0 ? 1 : 0, v = axis == 2 ? 1 : 2;
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                Vector3 p = b.verts[q + k];
                u0 = Mathf.Min(u0, p[u]); u1 = Mathf.Max(u1, p[u]); v0 = Mathf.Min(v0, p[v]); v1 = Mathf.Max(v1, p[v]);
            }
            float sgn = n[axis] > 0 ? 1f : 0f;
            long key = ((long)Mathf.RoundToInt(b.verts[q][axis] * 1000f) << 3) | (long)(axis * 2 + (int)sgn);
            Color32 c = b.cols[q];
            List<float[]> list;
            if (!groups.TryGetValue(key, out list)) { list = new List<float[]>(); groups[key] = list; }
            list.Add(new float[] { u0, u1, v0, v1, c.r * 65536 + c.g * 256 + c.b });
        }
        int pairs = 0, coloured = 0;
        const float eps = 0.001f;
        foreach (var list in groups.Values)
        {
            list.Sort((p, q) => p[0].CompareTo(q[0]));
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count && list[j][0] < list[i][1] - eps; j++)
                {
                    float ou = Mathf.Min(list[i][1], list[j][1]) - Mathf.Max(list[i][0], list[j][0]);
                    float ov = Mathf.Min(list[i][3], list[j][3]) - Mathf.Max(list[i][2], list[j][2]);
                    if (ou <= eps || ov <= eps) continue;
                    pairs++;
                    if (list[i][4] != list[j][4]) coloured++;
                }
        }
        return "pairs " + pairs + ", in two colours " + coloured;
    }

    public Mesh ToMesh(string name)
    {
        var m = new Mesh();
        m.name = name;
        if (verts.Count >= 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetNormals(norms);
        m.SetColors(cols);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    public GameObject ToObject(string name, Transform parent, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = ToMesh(name);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }
}
