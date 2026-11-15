using System;
using UnityEngine;

// Loads the baked model blob (Resources/PropHunt/models.bytes, written by prototype/tools/convert_models.py)
// into one Mesh per model. Every model uses the same material: colours are in the vertices.
public static class ModelLibrary
{
    public static Mesh[] mesh;
    public static Bounds[] bounds;         // in model units; the origin is the centre of the base
    public static Vector3[] pivot;         // for door/lid parts: the hinge position in the parent model
    public static int[] parent;            // model index of the parent, -1 for whole models
    public static int[][] parts;           // per model: the indices of its door/lid parts
    public static Material lit, ghost;
    public static int vertexCount, triangleCount;
    static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        lit = new Material(Shader.Find("PropHunt/Lit"));
        lit.enableInstancing = true;
        ghost = new Material(Shader.Find("PropHunt/Ghost"));
        var asset = Resources.Load<TextAsset>("PropHunt/models");
        if (asset == null) throw new Exception("models.bytes is missing from Resources/PropHunt");
        byte[] b = asset.bytes;
        int p = 0;
        if (b[0] != 'P' || b[1] != 'H' || b[2] != 'M' || b[3] != '1') throw new Exception("models.bytes: bad header");
        p = 4;
        int n = BitConverter.ToInt32(b, p); p += 4;
        if (n != GenModels.Name.Length) throw new Exception("models.bytes does not match GenModels.cs: re-run convert_models.py");
        mesh = new Mesh[n]; bounds = new Bounds[n]; pivot = new Vector3[n]; parent = new int[n]; parts = new int[n][];
        for (int m = 0; m < n; m++)
        {
            int nameLen = b[p++];
            string name = System.Text.Encoding.UTF8.GetString(b, p, nameLen); p += nameLen;
            int parLen = b[p++];
            string par = System.Text.Encoding.UTF8.GetString(b, p, parLen); p += parLen;
            if (name != GenModels.Name[m]) throw new Exception("models.bytes order differs from GenModels.cs at " + name);
            pivot[m] = new Vector3(BitConverter.ToSingle(b, p), BitConverter.ToSingle(b, p + 4), BitConverter.ToSingle(b, p + 8)); p += 12;
            Vector3 mn = new Vector3(BitConverter.ToSingle(b, p), BitConverter.ToSingle(b, p + 4), BitConverter.ToSingle(b, p + 8)); p += 12;
            Vector3 mx = new Vector3(BitConverter.ToSingle(b, p), BitConverter.ToSingle(b, p + 4), BitConverter.ToSingle(b, p + 8)); p += 12;
            int nv = BitConverter.ToInt32(b, p); p += 4;
            int ni = BitConverter.ToInt32(b, p); p += 4;
            var verts = new Vector3[nv];
            var norms = new Vector3[nv];
            var cols = new Color32[nv];
            for (int i = 0; i < nv; i++)
            {
                verts[i] = new Vector3(BitConverter.ToSingle(b, p), BitConverter.ToSingle(b, p + 4), BitConverter.ToSingle(b, p + 8));
                norms[i] = new Vector3((sbyte)b[p + 12] / 127f, (sbyte)b[p + 13] / 127f, (sbyte)b[p + 14] / 127f);
                cols[i] = new Color32(b[p + 15], b[p + 16], b[p + 17], 255);
                p += 19;
            }
            var tris = new int[ni];
            if (nv < 65536) { for (int i = 0; i < ni; i++) { tris[i] = BitConverter.ToUInt16(b, p); p += 2; } }
            else { for (int i = 0; i < ni; i++) { tris[i] = BitConverter.ToInt32(b, p); p += 4; } }
            var me = new Mesh();
            me.name = name;
            if (nv >= 65536) me.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            me.vertices = verts; me.normals = norms; me.colors32 = cols; me.triangles = tris;
            var bd = new Bounds();
            bd.SetMinMax(mn, mx);
            me.bounds = bd;
            me.UploadMeshData(true);
            mesh[m] = me; bounds[m] = bd;
            parent[m] = par.Length == 0 ? -1 : Array.IndexOf(GenModels.Name, par);
            vertexCount += nv; triangleCount += ni / 3;
        }
        for (int m = 0; m < n; m++)
        {
            int c = 0;
            for (int k = 0; k < n; k++) if (parent[k] == m) c++;
            parts[m] = new int[c];
            c = 0;
            for (int k = 0; k < n; k++) if (parent[k] == m) parts[m][c++] = k;
        }
    }

    public static int Find(string name) { return Array.IndexOf(GenModels.Name, name); }
}
