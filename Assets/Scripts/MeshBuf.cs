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
