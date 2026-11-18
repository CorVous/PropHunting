using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Z-fighting, found by geometry (Cor: "is there a way to detect z fighting?"): two faces that lie in one plane
// (within 1.5 mm), face the same way, overlap, and differ in colour flicker in the client.
//   ZFight.Declash(mesh)      the prop baker's fix for a model: the smaller patch of such a pair is lifted 2 mm off the
//                             plane (a decal on top of what it lies on). Returns the area that fought.
//   PropHunt > Check models for z-fighting    scans the baked models (after Declash: what is left)
//   ZFight.ScanScene()        in Play mode with a map built: the same test between everything that is drawn (props,
//                             walls, floors, roofs, fixtures, scenery), reported by the pair of things involved.
//                             Tools/zfight_maps.py runs it over many maps.
// A face looking straight down at a model's very bottom is never seen and does not count; nor do two colours a
// player could not tell apart.
public static class ZFight
{
    const float EPS_D = 0.0015f, LIFT = 0.002f;

    struct Tri
    {
        public Vector3 a, b, c, n;
        public float d;
        public Color32 col;
        public int obj, i0, i1, i2;
    }

    static bool SameColour(Color32 x, Color32 y)
    {
        return Mathf.Abs(x.r - y.r) + Mathf.Abs(x.g - y.g) + Mathf.Abs(x.b - y.b) < 14;
    }

    // ---- 2D clipping
    static readonly List<Vector2> tmpA = new List<Vector2>(), tmpB = new List<Vector2>();
    static Vector2 lastMid;      // the middle of the last overlap, in the plane's frame
    static Vector3 lastPoint;    // ... and in space

    static float Overlap(Vector2[] t1, Vector2[] t2)
    {
        tmpA.Clear(); tmpA.AddRange(t1);
        for (int i = 0; i < 3; i++)
        {
            Vector2 a = t2[i], b = t2[(i + 1) % 3];
            float ex = b.x - a.x, ey = b.y - a.y;
            tmpB.Clear();
            for (int k = 0; k < tmpA.Count; k++)
            {
                Vector2 p = tmpA[k], q = tmpA[(k + 1) % tmpA.Count];
                float sp = ex * (p.y - a.y) - ey * (p.x - a.x), sq = ex * (q.y - a.y) - ey * (q.x - a.x);
                if (sp >= 0) tmpB.Add(p);
                if ((sp > 0 && sq < 0) || (sp < 0 && sq > 0)) { float t = sp / (sp - sq); tmpB.Add(p + (q - p) * t); }
            }
            tmpA.Clear(); tmpA.AddRange(tmpB);
            if (tmpA.Count < 3) return 0f;
        }
        float s = 0f;
        lastMid = Vector2.zero;
        for (int k = 0; k < tmpA.Count; k++) { Vector2 p = tmpA[k], q = tmpA[(k + 1) % tmpA.Count]; s += p.x * q.y - q.x * p.y; lastMid += p; }
        lastMid /= tmpA.Count;
        return Mathf.Abs(s) * 0.5f;
    }

    static Vector2[] Flat(Tri t, Vector3 u, Vector3 w)
    {
        var p = new[] { new Vector2(Vector3.Dot(t.a, u), Vector3.Dot(t.a, w)), new Vector2(Vector3.Dot(t.b, u), Vector3.Dot(t.b, w)), new Vector2(Vector3.Dot(t.c, u), Vector3.Dot(t.c, w)) };
        float ar = (p[1].x - p[0].x) * (p[2].y - p[0].y) - (p[2].x - p[0].x) * (p[1].y - p[0].y);
        if (ar < 0) { var q = p[1]; p[1] = p[2]; p[2] = q; }
        return p;
    }

    // the overlap of two triangles that fight, 0 when they do not
    static float Fight(Tri s, Tri t)
    {
        if (Vector3.Dot(s.n, t.n) < 1f - 2e-4f) return 0f;
        if (Mathf.Abs(s.d - t.d) > EPS_D) return 0f;
        if (SameColour(s.col, t.col)) return 0f;
        Vector3 u = Vector3.Cross(s.n, Mathf.Abs(s.n.x) < 0.9f ? Vector3.right : Vector3.up).normalized, w = Vector3.Cross(s.n, u);
        Vector2[] p1 = Flat(s, u, w), p2 = Flat(t, u, w);
        if (Mathf.Max(p1[0].x, Mathf.Max(p1[1].x, p1[2].x)) <= Mathf.Min(p2[0].x, Mathf.Min(p2[1].x, p2[2].x))) return 0f;
        if (Mathf.Max(p2[0].x, Mathf.Max(p2[1].x, p2[2].x)) <= Mathf.Min(p1[0].x, Mathf.Min(p1[1].x, p1[2].x))) return 0f;
        if (Mathf.Max(p1[0].y, Mathf.Max(p1[1].y, p1[2].y)) <= Mathf.Min(p2[0].y, Mathf.Min(p2[1].y, p2[2].y))) return 0f;
        if (Mathf.Max(p2[0].y, Mathf.Max(p2[1].y, p2[2].y)) <= Mathf.Min(p1[0].y, Mathf.Min(p1[1].y, p1[2].y))) return 0f;
        float ov = Overlap(p1, p2);
        if (ov < 1e-6f) return 0f;
        lastPoint = u * lastMid.x + w * lastMid.y + s.n * s.d;
        return ov;
    }

    static string Box(Tri t)
    {
        Vector3 mn = Vector3.Min(t.a, Vector3.Min(t.b, t.c)), mx = Vector3.Max(t.a, Vector3.Max(t.b, t.c));
        return "[" + mn.x.ToString("0.000") + " " + mn.y.ToString("0.000") + " " + mn.z.ToString("0.000") + " .. " + mx.x.ToString("0.000") + " " + mx.y.ToString("0.000") + " " + mx.z.ToString("0.000") + "]";
    }

    // Is a place on a pair of fighting faces covered? They face along n; a face that looks the same way a little in
    // front of them (the far side of a wall they are buried in, a closed box's lid) hides them.
    static Dictionary<long, List<int>> grid;
    static List<Tri> gridTris;

    static long Cell(Vector3 p) { return ((long)(Mathf.FloorToInt(p.x / 2f) + 2048) << 24 | (long)(Mathf.FloorToInt(p.y / 2f) + 2048) << 12 | (long)(Mathf.FloorToInt(p.z / 2f) + 2048)); }

    static bool Hidden(Vector3 p, Vector3 n)
    {
        const float REACH = 0.35f;
        for (int pass = 0; pass < 2; pass++)
        {
            long ck = Cell(p + n * (pass * REACH));
            if (pass == 1 && ck == Cell(p)) break;
            if (!grid.TryGetValue(ck, out var l)) continue;
            foreach (int j in l)
            {
                Tri t = gridTris[j];
                float facing = Vector3.Dot(t.n, n);
                if (facing < 0.3f) continue;
                float dist = (t.d - Vector3.Dot(t.n, p)) / facing;
                if (dist < 0.003f || dist > REACH) continue;
                Vector3 q = p + n * dist;
                // inside the triangle?
                Vector3 c0 = Vector3.Cross(t.b - t.a, q - t.a), c1 = Vector3.Cross(t.c - t.b, q - t.b), c2 = Vector3.Cross(t.a - t.c, q - t.c);
                if (Vector3.Dot(c0, t.n) >= 0f && Vector3.Dot(c1, t.n) >= 0f && Vector3.Dot(c2, t.n) >= 0f) return true;
            }
        }
        return false;
    }

    static long Key(Vector3 n, float d)
    {
        // direction to 0.02, distance to 4 mm (neighbouring distance cells are looked at too)
        long kx = Mathf.RoundToInt(n.x * 50f) + 64, ky = Mathf.RoundToInt(n.y * 50f) + 64, kz = Mathf.RoundToInt(n.z * 50f) + 64;
        long kd = Mathf.FloorToInt(d / 0.004f) + (1 << 20);
        return (((kx << 7 | ky) << 7 | kz) << 24) | kd;
    }

    static List<Tri> Tris(Mesh m, Matrix4x4 mx, Color tint, int obj, bool skipBottom, List<Tri> into)
    {
        Vector3[] v = m.vertices; Color32[] c = m.colors32; int[] t = m.triangles;
        float minY = m.bounds.min.y;
        for (int k = 0; k + 2 < t.Length; k += 3)
        {
            Vector3 la = v[t[k]], lb = v[t[k + 1]], lc = v[t[k + 2]];
            Vector3 ln = Vector3.Cross(lb - la, lc - la);
            if (ln.sqrMagnitude < 1e-14f) continue;
            if (skipBottom && ln.normalized.y < -0.95f && la.y < minY + 0.01f) continue;   // the underside: never seen
            var tr = new Tri { a = mx.MultiplyPoint3x4(la), b = mx.MultiplyPoint3x4(lb), c = mx.MultiplyPoint3x4(lc), obj = obj, i0 = t[k], i1 = t[k + 1], i2 = t[k + 2] };
            Vector3 n = Vector3.Cross(tr.b - tr.a, tr.c - tr.a);
            if (n.sqrMagnitude < 1e-14f) continue;
            tr.n = n.normalized; tr.d = Vector3.Dot(tr.n, tr.a);
            Color32 col = c != null && c.Length > t[k] ? c[t[k]] : new Color32(255, 255, 255, 255);
            tr.col = new Color32((byte)(col.r * tint.r), (byte)(col.g * tint.g), (byte)(col.b * tint.b), 255);
            into.Add(tr);
        }
        return into;
    }

    // ------------------------------------------------------------------ a model
    // Lifts the smaller of two fighting patches of a model off the plane. Returns the area (m2) that fought.
    public static float Declash(Mesh m)
    {
        var tris = Tris(m, Matrix4x4.identity, Color.white, 0, true, new List<Tri>());
        if (tris.Count > 30000) return 0f;
        var buckets = new Dictionary<long, List<int>>();
        for (int i = 0; i < tris.Count; i++)
        {
            long k = Key(tris[i].n, tris[i].d);
            if (!buckets.TryGetValue(k, out var l)) buckets[k] = l = new List<int>();
            l.Add(i);
        }
        // every fighting pair; per plane and colour the area of that colour's patch
        var pairs = new List<int>();
        float total = 0f;
        foreach (var kv in buckets)
        {
            for (int dk = 0; dk <= 1; dk++)
            {
                if (!buckets.TryGetValue(kv.Key + dk, out var other)) continue;
                foreach (int i in kv.Value) foreach (int j in other)
                    {
                        if (dk == 0 && j <= i) continue;
                        float ov = Fight(tris[i], tris[j]);
                        if (ov <= 0f) continue;
                        total += ov; pairs.Add(i); pairs.Add(j);
                    }
            }
        }
        if (pairs.Count == 0) return 0f;
        // the patch a triangle belongs to: all triangles of its colour in its plane; the smaller patch is lifted
        var lift = new HashSet<int>();
        var areaOf = new Dictionary<int, float>();
        System.Func<int, float> patch = i =>
        {
            if (areaOf.TryGetValue(i, out float a)) return a;
            a = 0f;
            Tri s = tris[i];
            for (int q = 0; q < tris.Count; q++)
            {
                Tri t = tris[q];
                if (Vector3.Dot(s.n, t.n) < 1f - 2e-4f || Mathf.Abs(s.d - t.d) > EPS_D || !SameColour(s.col, t.col)) continue;
                a += Vector3.Cross(t.b - t.a, t.c - t.a).magnitude * 0.5f;
            }
            areaOf[i] = a;
            return a;
        };
        Vector3[] v = m.vertices;
        for (int p = 0; p < pairs.Count; p += 2)
        {
            int i = pairs[p], j = pairs[p + 1];
            float ai = patch(i), aj = patch(j);
            int lose = ai < aj || (ai == aj && tris[i].d > tris[j].d) ? i : j;
            // the whole of the losing patch, so it stays flat
            Tri s = tris[lose];
            for (int q = 0; q < tris.Count; q++)
            {
                Tri t = tris[q];
                if (Vector3.Dot(s.n, t.n) < 1f - 2e-4f || Mathf.Abs(s.d - t.d) > EPS_D || !SameColour(s.col, t.col)) continue;
                lift.Add(q);
            }
        }
        var moved = new HashSet<int>();
        foreach (int q in lift)
        {
            Tri t = tris[q];
            foreach (int vi in new[] { t.i0, t.i1, t.i2 })
                if (moved.Add(vi)) v[vi] += t.n * LIFT;
        }
        m.vertices = v;
        m.RecalculateBounds();
        return total;
    }

    // A door or lid part (its mesh about its hinge, the hinge at pivot in the model) whose faces fight with the
    // model's: how far to move the part so they do not (3 mm along each fighting face's direction).
    public static Vector3 PartShift(Mesh parent, Mesh part, Vector3 pivot)
    {
        var a = Tris(parent, Matrix4x4.identity, Color.white, 0, true, new List<Tri>());
        var b = Tris(part, Matrix4x4.Translate(pivot), Color.white, 1, false, new List<Tri>());
        if (a.Count * b.Count > 4000000) return Vector3.zero;
        Vector3 shift = Vector3.zero;
        foreach (Tri t in b)
        {
            if (Mathf.Abs(Vector3.Dot(shift, t.n)) > 1e-4f) continue;   // already moved that way
            foreach (Tri s in a)
            {
                if (Fight(s, t) < 1e-5f) continue;
                shift += t.n * 0.003f;
                break;
            }
        }
        return shift;
    }

    // what still fights in a model (m2), without changing it
    public static float Scan(Mesh m)
    {
        var tris = Tris(m, Matrix4x4.identity, Color.white, 0, true, new List<Tri>());
        if (tris.Count > 30000) return 0f;
        var buckets = new Dictionary<long, List<int>>();
        for (int i = 0; i < tris.Count; i++)
        {
            long k = Key(tris[i].n, tris[i].d);
            if (!buckets.TryGetValue(k, out var l)) buckets[k] = l = new List<int>();
            l.Add(i);
        }
        float total = 0f;
        foreach (var kv in buckets)
            for (int dk = 0; dk <= 1; dk++)
            {
                if (!buckets.TryGetValue(kv.Key + dk, out var other)) continue;
                foreach (int i in kv.Value) foreach (int j in other)
                    {
                        if (dk == 0 && j <= i) continue;
                        total += Fight(tris[i], tris[j]);
                    }
            }
        return total;
    }

    [MenuItem("PropHunt/Check models for z-fighting")]
    public static void CheckModels()
    {
        Debug.Log("[zfight] " + CheckModelsText());
    }

    public static string CheckModelsText()
    {
        var sb = new StringBuilder();
        int bad = 0, n = 0;
        foreach (string path in new[] { "Assets/Udon/Generated/Models.asset", "Assets/Udon/Generated/Fixtures.asset", "Assets/Udon/Generated/Parts.asset" })
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                Mesh m = o as Mesh;
                if (m == null || m.name == "Stars") continue;   // the star dome has its own shader and no depth
                n++;
                float a = Scan(m) * 1e4f;
                if (a < 1f) continue;
                bad++;
                sb.Append(m.name + " " + a.ToString("0.0") + " cm2; ");
            }
        return bad + " of " + n + " models fight: " + sb;
    }

    // ------------------------------------------------------------------ a built map (Play mode)
    // Everything drawn under the map's root, in the world. Reports pairs of things whose faces fight, by name, with
    // the area and one place. A prop against itself is the model check's business and is skipped; the architecture's
    // own meshes are checked against themselves.
    public static string ScanScene()
    {
        GameObject map = GameObject.Find("Map");
        if (map == null) return "no Map";
        var tris = new List<Tri>();
        var names = new List<string>();
        var self = new List<bool>();
        var block = new MaterialPropertyBlock();
        foreach (MeshRenderer mr in map.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (!mr.enabled || mr.name == "Shade") continue;
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mr.sharedMaterial == null) continue;
            if (mr.sharedMaterial.shader.name != "PropHunt/Lit") continue;
            if (mf.sharedMesh.vertexCount > 200000) continue;
            Color tint = Color.white;
            mr.GetPropertyBlock(block);
            if (!block.isEmpty && block.HasColor("_Color")) tint = block.GetColor("_Color");
            Transform top = mr.transform;
            while (top.parent != null && top.parent.parent != null && top.parent.parent != map.transform && top.parent != map.transform) top = top.parent;
            string nm = top.name.Replace("(Clone)", "");
            bool arch = mf.sharedMesh.name == "Ground" || mf.sharedMesh.name == "Walls" || mf.sharedMesh.name == "Roofs" || mf.sharedMesh.name.StartsWith("Scenery");
            if (arch) nm = mf.sharedMesh.name.StartsWith("Scenery") ? "Scenery" : mf.sharedMesh.name;
            names.Add(nm); self.Add(arch);
            Tris(mf.sharedMesh, mr.transform.localToWorldMatrix, tint, names.Count - 1, false, tris);
        }
        // every face by the 2 m cells its box touches (Hidden)
        grid = new Dictionary<long, List<int>>(); gridTris = tris;
        for (int i = 0; i < tris.Count; i++)
        {
            Tri t = tris[i];
            Vector3 mn = Vector3.Min(t.a, Vector3.Min(t.b, t.c)), mxv = Vector3.Max(t.a, Vector3.Max(t.b, t.c));
            int x0 = Mathf.FloorToInt(mn.x / 2f), x1 = Mathf.FloorToInt(mxv.x / 2f), y0 = Mathf.FloorToInt(mn.y / 2f), y1 = Mathf.FloorToInt(mxv.y / 2f), z0 = Mathf.FloorToInt(mn.z / 2f), z1 = Mathf.FloorToInt(mxv.z / 2f);
            if ((x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > 4096) continue;
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++)
                    {
                        long ck = ((long)(x + 2048) << 24 | (long)(y + 2048) << 12 | (long)(z + 2048));
                        if (!grid.TryGetValue(ck, out var l)) grid[ck] = l = new List<int>();
                        l.Add(i);
                    }
        }
        // plane buckets, then a 2 m grid on the triangle's box inside the bucket
        var buckets = new Dictionary<long, List<int>>();
        for (int i = 0; i < tris.Count; i++)
        {
            long k = Key(tris[i].n, tris[i].d);
            if (!buckets.TryGetValue(k, out var l)) buckets[k] = l = new List<int>();
            l.Add(i);
        }
        var found = new Dictionary<string, float>();
        var where = new Dictionary<string, Vector3>();
        var note = new Dictionary<string, string>();
        var cells = new Dictionary<long, List<int>>();
        var done = new HashSet<long>();
        foreach (var kv in buckets)
        {
            for (int dk = 0; dk <= 1; dk++)
            {
                if (!buckets.TryGetValue(kv.Key + dk, out var other)) continue;
                if (dk == 0 && kv.Value.Count < 2) continue;
                // the other bucket's triangles by grid cell
                cells.Clear();
                foreach (int j in other)
                {
                    Tri t = tris[j];
                    Vector3 mn = Vector3.Min(t.a, Vector3.Min(t.b, t.c)), mxv = Vector3.Max(t.a, Vector3.Max(t.b, t.c));
                    int x0 = Mathf.FloorToInt(mn.x / 2f), x1 = Mathf.FloorToInt(mxv.x / 2f), y0 = Mathf.FloorToInt(mn.y / 2f), y1 = Mathf.FloorToInt(mxv.y / 2f), z0 = Mathf.FloorToInt(mn.z / 2f), z1 = Mathf.FloorToInt(mxv.z / 2f);
                    if ((x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > 4096) { x1 = x0; y1 = y0; z1 = z0; x0 = y0 = z0 = 9999; x1 = y1 = z1 = 9999; }   // a huge face: one catch-all cell
                    for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++)
                            {
                                long ck = ((long)(x + 2048) << 24 | (long)(y + 2048) << 12 | (long)(z + 2048));
                                if (!cells.TryGetValue(ck, out var l)) cells[ck] = l = new List<int>();
                                l.Add(j);
                            }
                }
                done.Clear();
                foreach (int i in kv.Value)
                {
                    Tri s = tris[i];
                    Vector3 mn = Vector3.Min(s.a, Vector3.Min(s.b, s.c)), mxv = Vector3.Max(s.a, Vector3.Max(s.b, s.c));
                    int x0 = Mathf.FloorToInt(mn.x / 2f), x1 = Mathf.FloorToInt(mxv.x / 2f), y0 = Mathf.FloorToInt(mn.y / 2f), y1 = Mathf.FloorToInt(mxv.y / 2f), z0 = Mathf.FloorToInt(mn.z / 2f), z1 = Mathf.FloorToInt(mxv.z / 2f);
                    bool huge = (x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > 4096;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        if (pass == 1) { x0 = y0 = z0 = 9999; x1 = y1 = z1 = 9999; }
                        else if (huge) continue;
                        for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++)
                                {
                                    long ck = ((long)(x + 2048) << 24 | (long)(y + 2048) << 12 | (long)(z + 2048));
                                    if (!cells.TryGetValue(ck, out var l)) continue;
                                    foreach (int j in l)
                                    {
                                        if (dk == 0 && j <= i) continue;
                                        Tri t = tris[j];
                                        if (s.obj == t.obj && !self[s.obj]) continue;
                                        long pk = (long)i << 32 | (uint)j;
                                        if (!done.Add(pk)) continue;
                                        if (s.n.y < -0.95f && s.a.y < 0.05f) continue;   // undersides on or under the ground
                                        float ov = Fight(s, t);
                                        if (ov < 1e-5f) continue;
                                        if (Hidden(lastPoint, s.n)) continue;
                                        string a = names[s.obj], b = names[t.obj];
                                        if (string.CompareOrdinal(a, b) > 0) { string q = a; a = b; b = q; }
                                        string key = a + " | " + b;
                                        found.TryGetValue(key, out float sum);
                                        found[key] = sum + ov;
                                        if (!where.ContainsKey(key)) { where[key] = (s.a + s.b + s.c) / 3f; note[key] = "n " + s.n.x.ToString("0.0") + " " + s.n.y.ToString("0.0") + " " + s.n.z.ToString("0.0") + " c " + s.col.r + "," + s.col.g + "," + s.col.b + " / " + t.col.r + "," + t.col.g + "," + t.col.b + " A " + Box(s) + " B " + Box(t); }
                                    }
                                }
                    }
                    if (huge)
                    {
                        // a huge face against everything in the other bucket
                        foreach (int j in other)
                        {
                            if (dk == 0 && j <= i) continue;
                            Tri t = tris[j];
                            if (s.obj == t.obj && !self[s.obj]) continue;
                            long pk = (long)i << 32 | (uint)j;
                            if (!done.Add(pk)) continue;
                            float ov = Fight(s, t);
                            if (ov < 1e-5f) continue;
                            if (s.n.y < -0.95f && s.a.y < 0.05f) continue;
                            if (Hidden(lastPoint, s.n)) continue;
                            string a = names[s.obj], b = names[t.obj];
                            if (string.CompareOrdinal(a, b) > 0) { string q = a; a = b; b = q; }
                            string key = a + " | " + b;
                            found.TryGetValue(key, out float sum);
                            found[key] = sum + ov;
                            if (!where.ContainsKey(key)) { where[key] = (s.a + s.b + s.c) / 3f; note[key] = "n " + s.n.x.ToString("0.0") + " " + s.n.y.ToString("0.0") + " " + s.n.z.ToString("0.0") + " c " + s.col.r + "," + s.col.g + "," + s.col.b + " / " + t.col.r + "," + t.col.g + "," + t.col.b + " A " + Box(s) + " B " + Box(t); }
                        }
                    }
                }
            }
        }
        var sb = new StringBuilder();
        sb.Append(tris.Count + " faces\n");
        foreach (var kv in found)
        {
            if (kv.Value * 1e4f < 2f) continue;   // under 2 cm2: not seen
            Vector3 w = where[kv.Key];
            sb.Append(kv.Key + " | " + (kv.Value * 1e4f).ToString("0") + " | " + w.x.ToString("0.00") + " " + w.y.ToString("0.00") + " " + w.z.ToString("0.00") + " " + note[kv.Key] + "\n");
        }
        return sb.ToString();
    }
}
