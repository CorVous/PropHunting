using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Turns the prototype's model blob (Resources/PropHunt/models.bytes) into assets Udon can use, since Udon cannot
// read the blob or add components at runtime:
//   Assets/Udon/Generated/Models.asset          every model as a Mesh (sub-assets, named after GenModels.Name)
//   Assets/Udon/Generated/PropLit.mat           the one vertex-coloured material (shader PropHunt/Lit)
//   Assets/Udon/Generated/Props/<def>.prefab    one prefab per prop definition: mesh, scale, box colliders fitted
//                                               to the model's own shape (FitBoxes), and the door or lid parts of openables as
//                                               children named "door0", "door1", ... (hinge at the child's origin)
// MapBuilderUdon instantiates the prefabs by def index (its propPrefabs array, filled by this baker).
// Menu PropHunt > Bake prop prefabs. Run it after the models or the defs change.
public static class PropPrefabBaker
{
    const string OUT = "Assets/Udon/Generated";
    public const int LAYER_PROP = 11;   // VRChat "Environment"; switched per client at runtime (hunters walk through)

    static Mesh[] mesh;
    static Bounds[] bounds;
    static Vector3[] pivot;
    static int[] parent;

    [MenuItem("PropHunt/Bake prop prefabs")]
    public static void Bake()
    {
        GenData.Build();
        Directory.CreateDirectory(OUT + "/Props");
        Directory.CreateDirectory(OUT + "/Fixtures");
        AssetDatabase.Refresh();
        LoadModels();
        FixModels();
        // faces of one model that lie in one plane and flicker: the smaller patch is lifted off it (ZFight)
        float fought = 0f; int nFought = 0;
        for (int m = 0; m < mesh.Length; m++)
        {
            if (mesh[m] == null) continue;
            float a = ZFight.Declash(mesh[m]);
            // layers of three: what was lifted may meet the next layer
            for (int pass = 0; pass < 3 && a > 1e-4f && ZFight.Scan(mesh[m]) > 1e-4f; pass++) ZFight.Declash(mesh[m]);
            if (a > 1e-4f) { fought += a; nFought++; bounds[m] = mesh[m].bounds; }
        }
        // against the floor (ZFight.ScanScene found these on every map): a model's upward faces in its base plane
        // (the inside of an open box, a flower's leaves, a placemat) lay in the floor's plane and flickered. They sit
        // 6 mm up; a flat thing (a rug, a carpet) has its top 10 mm up, over the base faces of what stands on it.
        for (int m = 0; m < mesh.Length; m++)
        {
            if (mesh[m] == null || bounds[m].min.y > 0.002f || bounds[m].min.y < -0.002f) continue;
            Vector3[] v = mesh[m].vertices; Vector3[] nr = mesh[m].normals;
            bool flat = bounds[m].size.y < 0.03f, any = false;
            for (int i = 0; i < v.Length; i++)
            {
                if (nr[i].y < 0.9f) continue;
                float want = flat ? 0.010f : 0.006f;
                if (flat ? v[i].y < want : v[i].y < 0.002f) { v[i].y = want; any = true; }
            }
            if (any) { mesh[m].vertices = v; mesh[m].RecalculateBounds(); bounds[m] = mesh[m].bounds; }
        }
        Debug.Log("[prophunt] z-fighting faces separated in " + nFought + " models (" + (fought * 1e4f).ToString("0") + " cm2)");
        Harmonise();
        MakeShade();

        var mat = AssetDatabase.LoadAssetAtPath<Material>(OUT + "/PropLit.mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("PropHunt/Lit")) { enableInstancing = true };
            AssetDatabase.CreateAsset(mat, OUT + "/PropLit.mat");
        }

        // all meshes in one asset file
        string modelsPath = OUT + "/Models.asset";
        AssetDatabase.DeleteAsset(modelsPath);
        AssetDatabase.CreateAsset(mesh[0], modelsPath);
        for (int m = 1; m < mesh.Length; m++) AssetDatabase.AddObjectToAsset(mesh[m], mesh[0]);
        AssetDatabase.SaveAssets();

        fitProps = 0; fitBoxes = 0; fitSingle = 0; fitCache.Clear();
        var prefabs = new GameObject[GenData.nDefs];
        for (int d = 0; d < GenData.nDefs; d++)
            prefabs[d] = MakePrefab(d, mat);
        var cages = BakeCages();

        var fence = new GameObject("fence");
        int fm = Array.IndexOf(GenModels.Name, "fence_planks");
        fence.AddComponent<MeshFilter>().sharedMesh = mesh[fm];
        fence.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var fencePrefab = PrefabUtility.SaveAsPrefabAsset(fence, OUT + "/Fence.prefab");
        UnityEngine.Object.DestroyImmediate(fence);

        // a space base's fence (more-places): the "rail" model, fitted inside the plank fence's bounds so the builder
        // stretches it the same way
        GameObject railPrefab = null;
        int rm = Array.IndexOf(GenModels.Name, "rail");
        if (rm >= 0)
        {
            var rail = new GameObject("rail");
            var inner = new GameObject("mesh");
            inner.transform.SetParent(rail.transform, false);
            Bounds fb = bounds[fm], rb = bounds[rm];
            Vector3 s = new Vector3(fb.size.x / Mathf.Max(1e-4f, rb.size.x), fb.size.y / Mathf.Max(1e-4f, rb.size.y), fb.size.z / Mathf.Max(1e-4f, rb.size.z));
            inner.transform.localScale = s;
            inner.transform.localPosition = fb.center - Vector3.Scale(rb.center, s);
            inner.AddComponent<MeshFilter>().sharedMesh = mesh[rm];
            inner.AddComponent<MeshRenderer>().sharedMaterial = mat;
            railPrefab = PrefabUtility.SaveAsPrefabAsset(rail, OUT + "/Rail.prefab");
            UnityEngine.Object.DestroyImmediate(rail);
        }

        AssetDatabase.SaveAssets();
        // the light fixtures (fixtures.bytes, Tools/make_fixtures.py): ceiling, wall, street x ordinary, space, medieval
        var fixtures = BakeFixtures(mat);
        var decor = BakeDecor(mat);
        BakeParts();
        BakeSkyExtras();
        AssetDatabase.SaveAssets();
        MapBuilderBaker.Assign(prefabs, fencePrefab, mat, bounds[fm], railPrefab, fixtures, decor, partMesh, partOpening, partBody, skyPrefabs, starDome, moonPrefab, cages);
        Debug.Log("[prophunt] baked " + mesh.Length + " meshes and " + prefabs.Length + " prop prefabs");
        Debug.Log("[prophunt] fitted colliders: " + fitProps + " props, " + fitBoxes + " boxes (" + fitSingle + " props of one box)");
    }

    static GameObject MakePrefab(int def, Material mat)
    {
        int model = GenData.defModel[def];
        float sc = GenData.defScale[def] / 1000f;
        var go = new GameObject(GenData.defId[def]);
        go.transform.localScale = new Vector3(sc, sc, sc);
        go.AddComponent<MeshFilter>().sharedMesh = mesh[model];
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.layer = LAYER_PROP;
        BuildColliders(go, def, model, sc);
        // knockable props carry a kinematic rigidbody that a blow's knock switches on (PHKnock): every prop but the
        // ground cover and the fixed dressing (Cor: most things knockable, heavy furniture and trees too)
        int fl = GenData.defFlags[def];
        if ((fl & (GenData.FL_DECOR | GenData.FL_FIXTURE)) == 0)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.mass = GenData.defMassKg[def];
            rb.drag = 0.2f; rb.angularDrag = 0.6f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.interpolation = RigidbodyInterpolation.None;   // interpolation would pull a fresh instance back to the origin
        }
        int k = 0;
        if ((GenData.defFlags[def] & GenData.FL_OPENABLE) != 0)
            for (int pm = 0; pm < mesh.Length; pm++)
            {
                if (parent[pm] != model) continue;
                var d = new GameObject("door" + k++);
                d.transform.SetParent(go.transform, false);
                // a door whose face lies in the plane of its cupboard's front (the wardrobe's did) stands 3 mm proud
                d.transform.localPosition = DoorAt(model, pm);
                d.layer = LAYER_PROP;
                d.AddComponent<MeshFilter>().sharedMesh = mesh[pm];
                d.AddComponent<MeshRenderer>().sharedMaterial = mat;
                var bc = d.AddComponent<BoxCollider>();
                Bounds b = bounds[pm];
                bc.center = b.center;
                bc.size = new Vector3(b.size.x, b.size.y, Mathf.Max(b.size.z, 0.03f));
            }
        // the dark patch under it (PropShade): the last child, "Shade". Only for a thing that stands on something and
        // has some height (not a rug, not a thing on a wall); MapBuilderUdon switches it off on a prop that stands on
        // another (a patch would hang over a table's edge)
        Bounds mb = bounds[model];
        if (mb.min.y < 0.05f && mb.size.y * sc > 0.08f && (fl & GenData.FL_SCENERY) == 0)
        {
            var sh = new GameObject("Shade");
            sh.transform.SetParent(go.transform, false);
            float grow = 1.0f + Mathf.Clamp(0.25f / Mathf.Max(0.05f, Mathf.Max(mb.size.x, mb.size.z) * sc), 0.12f, 0.6f);   // a rim of about 12 cm
            sh.transform.localPosition = new Vector3(mb.center.x, Mathf.Max(mb.min.y, 0f) + 0.012f / sc, mb.center.z);
            sh.transform.localScale = new Vector3(mb.size.x * 0.5f * grow, 1f, mb.size.z * 0.5f * grow);
            sh.layer = 1;   // TransparentFX: no dart, no pointer, no body meets it
            sh.AddComponent<MeshFilter>().sharedMesh = shadeMesh;
            var smr = sh.AddComponent<MeshRenderer>();
            smr.sharedMaterial = shadeMat;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; smr.receiveShadows = false;
            smr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; smr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, OUT + "/Props/" + def.ToString("000") + "_" + GenData.defId[def] + ".prefab");
        UnityEngine.Object.DestroyImmediate(go);
        return prefab;
    }

    // a door or lid part's place in its prefab: its hinge, 3 mm proud when its face lay in its cupboard's front
    static Vector3 DoorAt(int model, int pm) { return pivot[pm] + ZFight.PartShift(mesh[model], mesh[pm], pivot[pm]); }

    // ------------------------------------------------------------------ the cage (shader PropHunt/Cage)
    // The hider's own prop in first person: its feature edges as lines (MapBuilderUdon.Cage lends one object to the
    // local hider's prop). An edge is a feature where the mesh ends (an open edge, or the rim of a two-sided card) or
    // where its faces meet at more than CAGE_ANGLE degrees (positions welded, so split normals and uv seams do not
    // count). Runs of edges in one line become one edge. A busy model gets a steeper angle, 10 degrees at a time up to
    // 80, until it has no more than CAGE_CAP lines; past that the longest and sharpest are kept. Lines shorter than
    // CAGE_SHORT on the largest prop that uses the model are dropped (bevels and screws would be dots). In the
    // prefab's space before its scale (the cage hangs under the prop's root), so one mesh serves a model at every
    // scale: one per model (and its doors, for openables). Each edge is a quad of 4 vertices in two half-float
    // streams (16 bytes a vertex): position = this end; TEXCOORD1 = the other end, and the side (-1, +1) in w. The
    // shader widens it on screen. All in Cages.asset; the prefabs themselves are untouched.
    public const int CAGE_CAP = 160;
    const float CAGE_ANGLE = 40f, CAGE_SHORT = 0.015f;
    public static int cageMade, cageLines, cageMaxLines, cageCapped, cageStepped, cageVerts;

    static Mesh[] BakeCages()
    {
        var t0 = System.Diagnostics.Stopwatch.StartNew();
        string path = OUT + "/Cages.asset";
        AssetDatabase.DeleteAsset(path);
        var byKey = new System.Collections.Generic.Dictionary<string, Mesh>();
        // the largest scale each model is used at (the short lines are judged there)
        var bigScale = new System.Collections.Generic.Dictionary<string, float>();
        for (int d = 0; d < GenData.nDefs; d++)
        {
            string k = GenData.defModel[d] + "/" + ((GenData.defFlags[d] & GenData.FL_OPENABLE) != 0);
            float s0;
            bigScale[k] = Mathf.Max(bigScale.TryGetValue(k, out s0) ? s0 : 0f, GenData.defScale[d] / 1000f);
        }
        cageVerts = 0;
        var result = new Mesh[GenData.nDefs];
        cageMade = 0; cageLines = 0; cageMaxLines = 0; cageCapped = 0; cageStepped = 0;
        Mesh first = null;
        for (int d = 0; d < GenData.nDefs; d++)
        {
            int model = GenData.defModel[d];
            if (model < 0 || model >= mesh.Length || mesh[model] == null) continue;
            bool open = (GenData.defFlags[d] & GenData.FL_OPENABLE) != 0;
            string key = model + "/" + open;
            Mesh m;
            if (!byKey.TryGetValue(key, out m))
            {
                m = MakeCage(model, bigScale[key], open);
                byKey[key] = m;
                if (m != null)
                {
                    m.name = "Cage_" + GenModels.Name[model] + (open ? "_doors" : "");
                    if (first == null) { first = m; AssetDatabase.CreateAsset(m, path); }
                    else AssetDatabase.AddObjectToAsset(m, first);
                    var so = new SerializedObject(m);   // only drawn, never read: no CPU copy in the build
                    so.FindProperty("m_IsReadable").boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            result[d] = m;
        }
        AssetDatabase.SaveAssets();
        long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        Debug.Log("[prophunt] cages: " + cageMade + " meshes, " + cageLines + " lines, " + cageVerts + " vertices (most " + cageMaxLines + ", cap " + CAGE_CAP + "; " + cageStepped
            + " took a steeper angle, " + cageCapped + " cut to the cap), " + (bytes / 1024) + " KB, " + t0.ElapsedMilliseconds + " ms");
        return result;
    }

    struct CageEdge { public int a, b; public float sharp; }

    static Mesh MakeCage(int model, float sc, bool open)
    {
        // the parts as they stand in the prefab (doors closed)
        var parts = new System.Collections.Generic.List<Mesh> { mesh[model] };
        var at = new System.Collections.Generic.List<Vector3> { Vector3.zero };
        if (open)
            for (int pm = 0; pm < mesh.Length; pm++)
                if (parent[pm] == model && mesh[pm] != null) { parts.Add(mesh[pm]); at.Add(DoorAt(model, pm)); }
        // weld by position (half a millimetre on the prop)
        float weld = 0.0005f / sc;
        var ids = new System.Collections.Generic.Dictionary<Vector3Int, int>();
        var pts = new System.Collections.Generic.List<Vector3>();
        var faces = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<Vector3>>();
        for (int k = 0; k < parts.Count; k++)
        {
            Vector3[] v = parts[k].vertices;
            int[] tri = parts[k].triangles;
            var id = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = v[i] + at[k];
                var key = new Vector3Int(Mathf.RoundToInt(p.x / weld), Mathf.RoundToInt(p.y / weld), Mathf.RoundToInt(p.z / weld));
                int n;
                if (!ids.TryGetValue(key, out n)) { n = pts.Count; pts.Add(p); ids[key] = n; }
                id[i] = n;
            }
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                int a = id[tri[i]], b = id[tri[i + 1]], c = id[tri[i + 2]];
                if (a == b || b == c || a == c) continue;
                Vector3 nr = Vector3.Cross(pts[b] - pts[a], pts[c] - pts[a]);
                if (nr.sqrMagnitude < 1e-14f) continue;
                nr.Normalize();
                AddFace(faces, a, b, nr); AddFace(faces, b, c, nr); AddFace(faces, c, a, nr);
            }
        }
        // each edge's sharpness: the widest angle between its faces (an open edge, or a two-sided card's rim: 90)
        var all = new System.Collections.Generic.List<CageEdge>();
        foreach (var kv in faces)
        {
            var l = kv.Value;
            float sharp;
            if (l.Count == 1 || (l.Count == 2 && Vector3.Dot(l[0], l[1]) < -0.999f)) sharp = 90f;
            else
            {
                float minDot = 1f;
                for (int i = 0; i < l.Count; i++)
                    for (int j = i + 1; j < l.Count; j++)
                    {
                        float dt = Vector3.Dot(l[i], l[j]);
                        if (dt > -0.999f && dt < minDot) minDot = dt;   // two faces back to back are one sheet
                    }
                sharp = Mathf.Acos(Mathf.Clamp(minDot, -1f, 1f)) * Mathf.Rad2Deg;
            }
            if (sharp < CAGE_ANGLE) continue;
            all.Add(new CageEdge { a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffffL), sharp = sharp });
        }
        // steeper and steeper until it fits
        System.Collections.Generic.List<CageEdge> keep = null;
        float shortest = CAGE_SHORT / sc;
        bool stepped = false;
        for (float angle = CAGE_ANGLE; angle <= 80.5f; angle += 10f)
        {
            var sel = new System.Collections.Generic.List<CageEdge>();
            for (int i = 0; i < all.Count; i++) if (all[i].sharp >= angle) sel.Add(all[i]);
            keep = JoinRuns(sel, pts);
            keep.RemoveAll(e => (pts[e.a] - pts[e.b]).magnitude < shortest);
            if (keep.Count <= CAGE_CAP) break;
            stepped = true;
        }
        if (keep == null || keep.Count == 0) return null;
        if (stepped) cageStepped++;
        if (keep.Count > CAGE_CAP)
        {
            cageCapped++;
            keep.Sort((x, y) => Score(y, pts).CompareTo(Score(x, pts)));
            keep.RemoveRange(CAGE_CAP, keep.Count - CAGE_CAP);
        }
        // the quads: two half-float streams
        int nv = keep.Count * 4;
        var pos = new ushort[nv * 4];
        var oth = new ushort[nv * 4];
        var tris = new ushort[keep.Count * 6];
        Bounds bb = new Bounds(pts[keep[0].a], Vector3.zero);
        for (int i = 0; i < keep.Count; i++)
        {
            Vector3 p = pts[keep[i].a], q = pts[keep[i].b];
            bb.Encapsulate(p); bb.Encapsulate(q);
            int k = i * 4;
            CageVertex(pos, oth, k, p, q, 1f);
            CageVertex(pos, oth, k + 1, p, q, -1f);
            CageVertex(pos, oth, k + 2, q, p, -1f);   // seen from q, its -1 side is p's +1 side
            CageVertex(pos, oth, k + 3, q, p, 1f);
            int t = i * 6;
            tris[t] = (ushort)k; tris[t + 1] = (ushort)(k + 1); tris[t + 2] = (ushort)(k + 3);
            tris[t + 3] = (ushort)(k + 1); tris[t + 4] = (ushort)(k + 2); tris[t + 5] = (ushort)(k + 3);
        }
        var m = new Mesh();
        m.SetVertexBufferParams(nv,
            new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position, UnityEngine.Rendering.VertexAttributeFormat.Float16, 4, 0),
            new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.TexCoord1, UnityEngine.Rendering.VertexAttributeFormat.Float16, 4, 1));
        m.SetVertexBufferData(pos, 0, 0, pos.Length, 0);
        m.SetVertexBufferData(oth, 0, 0, oth.Length, 1);
        m.SetIndexBufferParams(tris.Length, UnityEngine.Rendering.IndexFormat.UInt16);
        m.SetIndexBufferData(tris, 0, 0, tris.Length);
        m.subMeshCount = 1;
        m.SetSubMesh(0, new UnityEngine.Rendering.SubMeshDescriptor(0, tris.Length), UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
        bb.Expand(0.05f / sc);   // the lines' width on screen
        m.bounds = bb;
        cageVerts += nv;
        cageMade++; cageLines += keep.Count; cageMaxLines = Mathf.Max(cageMaxLines, keep.Count);
        return m;
    }

    static void CageVertex(ushort[] pos, ushort[] oth, int k, Vector3 p, Vector3 q, float side)
    {
        pos[k * 4] = Mathf.FloatToHalf(p.x); pos[k * 4 + 1] = Mathf.FloatToHalf(p.y); pos[k * 4 + 2] = Mathf.FloatToHalf(p.z); pos[k * 4 + 3] = Mathf.FloatToHalf(1f);
        oth[k * 4] = Mathf.FloatToHalf(q.x); oth[k * 4 + 1] = Mathf.FloatToHalf(q.y); oth[k * 4 + 2] = Mathf.FloatToHalf(q.z); oth[k * 4 + 3] = Mathf.FloatToHalf(side);
    }

    static float Score(CageEdge e, System.Collections.Generic.List<Vector3> pts)
    {
        return (pts[e.a] - pts[e.b]).magnitude * (0.5f + 0.5f * Mathf.Clamp01(e.sharp / 90f));
    }

    static void AddFace(System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<Vector3>> faces, int a, int b, Vector3 n)
    {
        long key = ((long)Mathf.Min(a, b) << 32) | (long)Mathf.Max(a, b);
        System.Collections.Generic.List<Vector3> l;
        if (!faces.TryGetValue(key, out l)) { l = new System.Collections.Generic.List<Vector3>(2); faces[key] = l; }
        l.Add(n);
    }

    // edges that continue each other in a straight line through a point no other edge meets become one
    static System.Collections.Generic.List<CageEdge> JoinRuns(System.Collections.Generic.List<CageEdge> sel, System.Collections.Generic.List<Vector3> pts)
    {
        var edges = new System.Collections.Generic.List<CageEdge>(sel);
        var alive = new System.Collections.Generic.List<bool>();
        var at = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
        for (int i = 0; i < edges.Count; i++) { alive.Add(true); Link(at, edges[i].a, i); Link(at, edges[i].b, i); }
        bool joined = true;
        while (joined)
        {
            joined = false;
            foreach (var kv in at)
            {
                var l = kv.Value;
                l.RemoveAll(i => !alive[i]);
                if (l.Count != 2) continue;
                int v = kv.Key;
                CageEdge e0 = edges[l[0]], e1 = edges[l[1]];
                int p = e0.a == v ? e0.b : e0.a, q = e1.a == v ? e1.b : e1.a;
                if (p == q) continue;
                Vector3 d0 = (pts[v] - pts[p]).normalized, d1 = (pts[q] - pts[v]).normalized;
                if (Vector3.Dot(d0, d1) < 0.9995f) continue;
                alive[l[0]] = false; alive[l[1]] = false;
                edges.Add(new CageEdge { a = p, b = q, sharp = Mathf.Min(e0.sharp, e1.sharp) });
                alive.Add(true);
                int ni = edges.Count - 1;
                l.Clear();
                Link(at, p, ni); Link(at, q, ni);
                joined = true;   // (the keys never change while this runs: p and q are in it already)
            }
        }
        var outList = new System.Collections.Generic.List<CageEdge>();
        for (int i = 0; i < edges.Count; i++) if (alive[i]) outList.Add(edges[i]);
        return outList;
    }

    static void Link(System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>> at, int v, int e)
    {
        System.Collections.Generic.List<int> l;
        if (!at.TryGetValue(v, out l)) { l = new System.Collections.Generic.List<int>(); at[v] = l; }
        l.Add(e);
    }

    // ------------------------------------------------------------------ the blob (as ModelLibrary.Load)
    // the themes' set dressing (MapBuilderUdon.DECOR_*): model, its largest side in metres, solid (a collider)
    static readonly string[] DecorModel = { "roofmodule_solarpanels", "roofmodule_cargo_A", "satelliteDish_large", "craterLarge", "crater",
        "meteor_detailed", "rock_crystalsLargeA", "fountain-round-detail", "building_well_blue", "stall-red", "stall-green", "banner-red", "hedge-large" };
    static readonly float[] DecorSize = { 3.2f, 2.6f, 2.4f, 7f, 3.5f, 2.2f, 2.6f, 3.2f, 2.2f, 2.8f, 2.8f, 2.6f, 2.2f };
    static readonly bool[] DecorSolid = { false, false, false, false, false, true, true, true, true, true, true, true, true };

    // the CC0 facade parts (parts.bytes, Tools/make_parts.py): meshes for the builder to merge into the walls; the
    // file's pivot field is a part's body colour and its bounds its window or door hole
    static Mesh[] partMesh;
    static Vector4[] partOpening;
    static Color[] partBody;

    static void BakeParts()
    {
        partMesh = null; partOpening = null; partBody = null;
        var asset = Resources.Load<TextAsset>("PropHunt/parts");
        if (asset == null) { Debug.LogWarning("[prophunt] parts.bytes is missing: no facades"); return; }
        byte[] b = asset.bytes;
        int p = 4, n = BitConverter.ToInt32(b, p); p += 4;
        partMesh = new Mesh[n]; partOpening = new Vector4[n]; partBody = new Color[n];
        string path = OUT + "/Parts.asset";
        AssetDatabase.DeleteAsset(path);
        for (int m = 0; m < n; m++)
        {
            int nameLen = b[p++];
            string name = System.Text.Encoding.UTF8.GetString(b, p, nameLen); p += nameLen;
            int parLen = b[p++]; p += parLen;
            Vector3 body = V(b, p); p += 12;
            Vector3 mn = V(b, p); p += 12;
            Vector3 mx = V(b, p); p += 12;
            int nv = BitConverter.ToInt32(b, p); p += 4;
            int ni = BitConverter.ToInt32(b, p); p += 4;
            var verts = new Vector3[nv]; var norms = new Vector3[nv]; var cols = new Color32[nv];
            for (int i = 0; i < nv; i++)
            {
                verts[i] = V(b, p);
                norms[i] = new Vector3((sbyte)b[p + 12] / 127f, (sbyte)b[p + 13] / 127f, (sbyte)b[p + 14] / 127f);
                cols[i] = new Color32(b[p + 15], b[p + 16], b[p + 17], 255);
                p += 19;
            }
            var tris = new int[ni];
            for (int i = 0; i < ni; i++) { tris[i] = BitConverter.ToUInt16(b, p); p += 2; }
            var me = new Mesh { name = name };
            me.vertices = verts; me.normals = norms; me.colors32 = cols; me.triangles = tris;
            me.RecalculateBounds();
            if (m == 0) AssetDatabase.CreateAsset(me, path); else AssetDatabase.AddObjectToAsset(me, partMesh[0]);
            partMesh[m] = me;
            partOpening[m] = new Vector4(mn.x, mn.y, mx.x, mx.y);
            partBody[m] = new Color(body.x, body.y, body.z, 1f);
        }
    }

    static GameObject[] BakeDecor(Material mat)
    {
        var result = new GameObject[DecorModel.Length];
        for (int i = 0; i < DecorModel.Length; i++)
        {
            int m = Array.IndexOf(GenModels.Name, DecorModel[i]);
            if (m < 0 || mesh[m] == null) { Debug.LogWarning("[prophunt] decor model missing: " + DecorModel[i]); continue; }
            Bounds b = bounds[m];
            float big = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (DecorModel[i] == "banner-red") big = b.size.y;
            float s = DecorSize[i] / Mathf.Max(1e-4f, big);
            var go = new GameObject("decor_" + DecorModel[i]);
            var inner = new GameObject("mesh");
            inner.transform.SetParent(go.transform, false);
            inner.transform.localScale = new Vector3(s, s, s);
            // standing on its origin, centred
            inner.transform.localPosition = new Vector3(-b.center.x * s, -b.min.y * s, -b.center.z * s);
            inner.AddComponent<MeshFilter>().sharedMesh = mesh[m];
            inner.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (DecorSolid[i])
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, b.size.y * s * 0.5f, 0f);
                bc.size = new Vector3(b.size.x * s, b.size.y * s, b.size.z * s);
                go.layer = 11;
            }
            result[i] = PrefabUtility.SaveAsPrefabAsset(go, OUT + "/Fixtures/" + go.name + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }
        return result;
    }

    static readonly string[] FixtureNames = {
        "fx_ceiling_modern", "fx_ceiling_space", "fx_ceiling_medieval",
        "fx_wall_modern", "fx_wall_space", "fx_wall_medieval",
        "fx_street_modern", "fx_street_space", "fx_street_medieval" };

    // the sky's scenery (MapBuilderUdon.SKY_*, same order), from fixtures.bytes; and the star dome and the moon
    static readonly string[] SkyNames = { "sky_hill_a", "sky_hill_b", "sky_mount_a", "sky_mount_b", "sky_mount_c", "sky_cloud_big", "sky_cloud_small", "sky_rock_a", "sky_rock_b" };
    static GameObject[] skyPrefabs;
    static GameObject starDome, moonPrefab;

    static void BakeSkyExtras()
    {
        var sky = AssetDatabase.LoadAssetAtPath<Material>(OUT + "/SkyUnlit.mat");
        if (sky == null) { sky = new Material(Shader.Find("PropHunt/SkyUnlit")) { enableInstancing = true }; AssetDatabase.CreateAsset(sky, OUT + "/SkyUnlit.mat"); }
        // the stars: 700 small quads on the upper half of a 340 m sphere, facing in, a few tinted
        var rnd = new System.Random(17);
        var v = new System.Collections.Generic.List<Vector3>(); var c = new System.Collections.Generic.List<Color32>();
        var n = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 700; i++)
        {
            float az = (float)(rnd.NextDouble() * Math.PI * 2), el = (float)Math.Asin(rnd.NextDouble() * 0.95 + 0.04);
            Vector3 dir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
            Vector3 p = dir * 340f, right = Vector3.Cross(dir, Vector3.up).normalized, up = Vector3.Cross(right, dir).normalized;
            float s = 0.5f + (float)rnd.NextDouble() * (rnd.NextDouble() < 0.08 ? 1.6f : 0.7f);
            byte b = (byte)(170 + rnd.Next(85));
            Color32 col = rnd.NextDouble() < 0.15 ? new Color32(b, (byte)(b * 0.85f), (byte)(b * 0.7f), 255) : (rnd.NextDouble() < 0.15 ? new Color32((byte)(b * 0.8f), (byte)(b * 0.88f), b, 255) : new Color32(b, b, b, 255));
            int st = v.Count;
            v.Add(p - right * s - up * s); v.Add(p + right * s - up * s); v.Add(p + right * s + up * s); v.Add(p - right * s + up * s);
            for (int k = 0; k < 4; k++) { c.Add(col); n.Add(-dir); }
            t.Add(st); t.Add(st + 2); t.Add(st + 1); t.Add(st); t.Add(st + 3); t.Add(st + 2);
        }
        var starMesh = new Mesh { name = "Stars" };
        starMesh.SetVertices(v); starMesh.SetColors(c); starMesh.SetNormals(n); starMesh.SetTriangles(t, 0);
        starMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 700f);
        AssetDatabase.AddObjectToAsset(starMesh, AssetDatabase.LoadAssetAtPath<Mesh>(OUT + "/Fixtures.asset"));
        var go = new GameObject("StarDome");
        go.AddComponent<MeshFilter>().sharedMesh = starMesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = sky; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        starDome = PrefabUtility.SaveAsPrefabAsset(go, OUT + "/Fixtures/StarDome.prefab");
        UnityEngine.Object.DestroyImmediate(go);
        // the moon or a space base's planet: Unity's sphere, white (the builder tints and scales it)
        var moon = new GameObject("Moon");
        moon.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        var mr2 = moon.AddComponent<MeshRenderer>(); mr2.sharedMaterial = sky; mr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        moonPrefab = PrefabUtility.SaveAsPrefabAsset(moon, OUT + "/Fixtures/Moon.prefab");
        UnityEngine.Object.DestroyImmediate(moon);
    }

    static GameObject[] BakeFixtures(Material mat)   // (byte 18 of a vertex: its alpha; 0 glows, Tools/make_fixtures.py)
    {
        var result = new GameObject[FixtureNames.Length];
        skyPrefabs = new GameObject[SkyNames.Length];
        var asset = Resources.Load<TextAsset>("PropHunt/fixtures");
        if (asset == null) { Debug.LogWarning("[prophunt] fixtures.bytes is missing: fixtures are drawn from boxes"); return result; }
        byte[] b = asset.bytes;
        int p = 4, n = BitConverter.ToInt32(b, p); p += 4;
        string meshPath = OUT + "/Fixtures.asset";
        AssetDatabase.DeleteAsset(meshPath);
        Mesh first = null;
        for (int m = 0; m < n; m++)
        {
            int nameLen = b[p++];
            string name = System.Text.Encoding.UTF8.GetString(b, p, nameLen); p += nameLen;
            int parLen = b[p++]; p += parLen;
            Vector3 light = V(b, p); p += 12;
            Vector3 mn = V(b, p); p += 12;
            Vector3 mx = V(b, p); p += 12;
            int nv = BitConverter.ToInt32(b, p); p += 4;
            int ni = BitConverter.ToInt32(b, p); p += 4;
            var verts = new Vector3[nv]; var norms = new Vector3[nv]; var cols = new Color32[nv];
            for (int i = 0; i < nv; i++)
            {
                verts[i] = V(b, p);
                norms[i] = new Vector3((sbyte)b[p + 12] / 127f, (sbyte)b[p + 13] / 127f, (sbyte)b[p + 14] / 127f);
                cols[i] = new Color32(b[p + 15], b[p + 16], b[p + 17], b[p + 18]);
                p += 19;
            }
            var tris = new int[ni];
            for (int i = 0; i < ni; i++) { tris[i] = BitConverter.ToUInt16(b, p); p += 2; }
            var me = new Mesh { name = name };
            me.vertices = verts; me.normals = norms; me.colors32 = cols; me.triangles = tris;
            me.RecalculateBounds();
            if (first == null) { first = me; AssetDatabase.CreateAsset(me, meshPath); } else AssetDatabase.AddObjectToAsset(me, first);

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = me;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var lp = new GameObject("LightPoint");
            lp.transform.SetParent(go.transform, false);
            lp.transform.localPosition = light;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, OUT + "/Fixtures/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
            int k = Array.IndexOf(FixtureNames, name);
            if (k >= 0) result[k] = prefab;
            int sk = Array.IndexOf(SkyNames, name);
            if (sk >= 0) skyPrefabs[sk] = prefab;
        }
        return result;
    }

    static Mesh shadeMesh;
    static Material shadeMat;

    // the patch under a prop: a flat fan of 12, radius 1, dark (alpha 1) in a wide middle and clear at the rim
    static void MakeShade()
    {
        string path = OUT + "/Shade.asset", mpath = OUT + "/PropShade.mat";
        AssetDatabase.DeleteAsset(path);
        const int N = 12;
        var v = new Vector3[1 + 2 * N]; var c = new Color32[1 + 2 * N]; var t = new int[N * 9];
        v[0] = Vector3.zero; c[0] = new Color32(0, 0, 0, 255);
        for (int i = 0; i < N; i++)
        {
            float a = i * Mathf.PI * 2f / N;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v[1 + i] = d * 0.62f; c[1 + i] = new Color32(0, 0, 0, 255);
            v[1 + N + i] = d; c[1 + N + i] = new Color32(0, 0, 0, 0);
            int j = (i + 1) % N, k = i * 9;
            t[k] = 0; t[k + 1] = 1 + j; t[k + 2] = 1 + i;
            t[k + 3] = 1 + i; t[k + 4] = 1 + j; t[k + 5] = 1 + N + j;
            t[k + 6] = 1 + i; t[k + 7] = 1 + N + j; t[k + 8] = 1 + N + i;
        }
        shadeMesh = new Mesh { name = "Shade" };
        shadeMesh.vertices = v; shadeMesh.colors32 = c; shadeMesh.triangles = t;
        shadeMesh.RecalculateNormals(); shadeMesh.RecalculateBounds();
        AssetDatabase.CreateAsset(shadeMesh, path);
        shadeMat = AssetDatabase.LoadAssetAtPath<Material>(mpath);
        if (shadeMat == null)
        {
            shadeMat = new Material(Shader.Find("PropHunt/Shade")) { enableInstancing = true };
            AssetDatabase.CreateAsset(shadeMat, mpath);
        }
    }

    // One family of colours across the packs (Cor: more cohesion between the assets). The packs disagree most on
    // wood: one's is a saturated salmon orange, another's a dull brown, a third's nearly yellow. Every warm brown
    // moves most of the way to one hue and into one band of saturation, keeping its lightness, so light and dark
    // woods stay apart; and no colour anywhere is more saturated than the rest of the map can carry.
    static void Harmonise()
    {
        for (int m = 0; m < mesh.Length; m++)
        {
            if (mesh[m] == null) continue;
            Color32[] c = mesh[m].colors32;
            if (c == null || c.Length == 0) continue;
            for (int i = 0; i < c.Length; i++)
            {
                float h, s, val;
                Color.RGBToHSV(c[i], out h, out s, out val);
                float deg = h * 360f;
                // wood: a warm brown that is not a pure bright orange (a cone, a carrot, a space base's trim stay)
                if (deg >= 8f && deg <= 44f && s >= 0.22f && s <= 0.78f && val >= 0.2f && !(s >= 0.66f && val >= 0.96f))
                {
                    deg = Mathf.Lerp(deg, 25f, 0.7f);
                    s = Mathf.Lerp(s, Mathf.Clamp(s, 0.36f, 0.52f), 0.8f);
                    if (val > 0.7f) val = Mathf.Lerp(val, 0.82f, 0.5f);   // the pale packs and the bright ones meet in the middle
                }
                else if (s > 0.85f) s = 0.85f + (s - 0.85f) * 0.5f;
                Color o = Color.HSVToRGB(deg / 360f, s, val);
                c[i] = new Color32((byte)Mathf.RoundToInt(o.r * 255f), (byte)Mathf.RoundToInt(o.g * 255f), (byte)Mathf.RoundToInt(o.b * 255f), c[i].a);
            }
            mesh[m].colors32 = c;
        }
    }

    // Small corrections to the models as they come from the packs (Cor's playtest):
    //   crop_carrot, crop_turnip: the root stood on the ground; it goes into it (the leaves and its shoulder show)
    //   gen_laundrybasket: the lining's top lay in the rim's plane and flickered; it sits a little lower
    static void FixModels()
    {
        for (int m = 0; m < mesh.Length; m++)
        {
            if (mesh[m] == null) continue;
            string n = mesh[m].name.ToLower();
            if (n == "crop_carrot" || n == "crop_turnip")
            {
                Vector3[] v = mesh[m].vertices;
                float sink = bounds[m].size.y * 0.4f;
                for (int i = 0; i < v.Length; i++) v[i].y -= sink;
                mesh[m].vertices = v; mesh[m].RecalculateBounds(); bounds[m] = mesh[m].bounds;
            }
            else if (n == "gen_laundrybasket")
            {
                Vector3[] v = mesh[m].vertices; Color32[] c = mesh[m].colors32;
                float top = bounds[m].max.y;
                for (int i = 0; i < v.Length; i++)
                {
                    bool lining = c[i].r == 214 && c[i].g == 182 && c[i].b == 138;
                    if (lining && v[i].y > 0.35f) v[i].y -= 0.008f;
                }
                mesh[m].vertices = v; mesh[m].RecalculateBounds(); bounds[m] = mesh[m].bounds;
            }
        }
    }

    static void LoadModels()
    {
        var asset = Resources.Load<TextAsset>("PropHunt/models");
        if (asset == null) throw new Exception("models.bytes is missing from Resources/PropHunt");
        byte[] b = asset.bytes;
        if (b[0] != 'P' || b[1] != 'H' || b[2] != 'M' || b[3] != '1') throw new Exception("models.bytes: bad header");
        int p = 4;
        int n = BitConverter.ToInt32(b, p); p += 4;
        // models after the blob's are made here from boxes (GenModels lists them last: the gas station's)
        int total = GenModels.Name.Length;
        if (n > total) throw new Exception("models.bytes does not match GenModels.cs");
        mesh = new Mesh[total]; bounds = new Bounds[total]; pivot = new Vector3[total]; parent = new int[total];
        var parName = new string[n];
        for (int m = 0; m < n; m++)
        {
            int nameLen = b[p++];
            string name = System.Text.Encoding.UTF8.GetString(b, p, nameLen); p += nameLen;
            int parLen = b[p++];
            parName[m] = System.Text.Encoding.UTF8.GetString(b, p, parLen); p += parLen;
            pivot[m] = V(b, p); p += 12;
            Vector3 mn = V(b, p); p += 12;
            Vector3 mx = V(b, p); p += 12;
            int nv = BitConverter.ToInt32(b, p); p += 4;
            int ni = BitConverter.ToInt32(b, p); p += 4;
            var verts = new Vector3[nv]; var norms = new Vector3[nv]; var cols = new Color32[nv];
            for (int i = 0; i < nv; i++)
            {
                verts[i] = V(b, p);
                norms[i] = new Vector3((sbyte)b[p + 12] / 127f, (sbyte)b[p + 13] / 127f, (sbyte)b[p + 14] / 127f);
                cols[i] = new Color32(b[p + 15], b[p + 16], b[p + 17], 255);
                p += 19;
            }
            var tris = new int[ni];
            if (nv < 65536) { for (int i = 0; i < ni; i++) { tris[i] = BitConverter.ToUInt16(b, p); p += 2; } }
            else { for (int i = 0; i < ni; i++) { tris[i] = BitConverter.ToInt32(b, p); p += 4; } }
            var me = new Mesh { name = name };
            if (nv >= 65536) me.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            me.vertices = verts; me.normals = norms; me.colors32 = cols; me.triangles = tris;
            var bd = new Bounds(); bd.SetMinMax(mn, mx);
            me.bounds = bd;
            mesh[m] = me; bounds[m] = bd;
        }
        for (int m = 0; m < n; m++) parent[m] = parName[m].Length == 0 ? -1 : Array.IndexOf(GenModels.Name, parName[m]);
        for (int m = n; m < total; m++)
        {
            mesh[m] = MakeModel(GenModels.Name[m]);
            bounds[m] = mesh[m].bounds;
            pivot[m] = Vector3.zero; parent[m] = -1;
        }
        LoadReplacements();
    }

    // Quaternius models that take the place of props once made in code (Tools/make_quaternius.py writes the file in
    // the format of models.bytes). Each has the name of the model it replaces and lies inside that model's bounds.
    // A model with a new name and a parent is one more door of that parent (the wardrobe's left leaf): it is added
    // after GenModels' models, so it has no index in the generator's tables.
    static void LoadReplacements()
    {
        var asset = Resources.Load<TextAsset>("PropHunt/models_quaternius");
        if (asset == null) return;
        byte[] b = asset.bytes;
        if (b[0] != 'P' || b[1] != 'H' || b[2] != 'M' || b[3] != '1') throw new Exception("models_quaternius.bytes: bad header");
        int p = 4;
        int n = BitConverter.ToInt32(b, p); p += 4;
        for (int k = 0; k < n; k++)
        {
            int nameLen = b[p++];
            string name = System.Text.Encoding.UTF8.GetString(b, p, nameLen); p += nameLen;
            int parLen = b[p++];
            string par = System.Text.Encoding.UTF8.GetString(b, p, parLen); p += parLen;
            Vector3 piv = V(b, p); p += 12;
            Vector3 mn = V(b, p); p += 12;
            Vector3 mx = V(b, p); p += 12;
            int nv = BitConverter.ToInt32(b, p); p += 4;
            int ni = BitConverter.ToInt32(b, p); p += 4;
            var verts = new Vector3[nv]; var norms = new Vector3[nv]; var cols = new Color32[nv];
            for (int i = 0; i < nv; i++)
            {
                verts[i] = V(b, p);
                norms[i] = new Vector3((sbyte)b[p + 12] / 127f, (sbyte)b[p + 13] / 127f, (sbyte)b[p + 14] / 127f);
                cols[i] = new Color32(b[p + 15], b[p + 16], b[p + 17], 255);
                p += 19;
            }
            var tris = new int[ni];
            if (nv < 65536) { for (int i = 0; i < ni; i++) { tris[i] = BitConverter.ToUInt16(b, p); p += 2; } }
            else { for (int i = 0; i < ni; i++) { tris[i] = BitConverter.ToInt32(b, p); p += 4; } }
            int m = Array.IndexOf(GenModels.Name, name);
            if (m < 0)
            {
                int pi = par.Length == 0 ? -1 : Array.IndexOf(GenModels.Name, par);
                if (pi < 0) throw new Exception("models_quaternius.bytes: no model named " + name);
                m = mesh.Length;
                Array.Resize(ref mesh, m + 1); Array.Resize(ref bounds, m + 1); Array.Resize(ref pivot, m + 1); Array.Resize(ref parent, m + 1);
                parent[m] = pi;
            }
            var me = new Mesh { name = name };
            if (nv >= 65536) me.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            me.vertices = verts; me.normals = norms; me.colors32 = cols; me.triangles = tris;
            var bd = new Bounds(); bd.SetMinMax(mn, mx);
            me.bounds = bd;
            mesh[m] = me; bounds[m] = bd; pivot[m] = piv;
        }
    }

    // ------------------------------------------------------------------ generated models (vertex colours, base at y 0)
    static System.Collections.Generic.List<Vector3> gv, gn;
    static System.Collections.Generic.List<Color32> gc;
    static System.Collections.Generic.List<int> gt;

    static void GQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color32 col)
    {
        int s = gv.Count;
        gv.Add(a); gv.Add(b); gv.Add(c); gv.Add(d);
        for (int i = 0; i < 4; i++) { gn.Add(n); gc.Add(col); }
        gt.Add(s); gt.Add(s + 1); gt.Add(s + 2); gt.Add(s); gt.Add(s + 2); gt.Add(s + 3);
    }

    // an axis-aligned box from min to max (metres)
    static void GBox(Vector3 mn, Vector3 mx, Color32 col)
    {
        float x0 = mn.x, y0 = mn.y, z0 = mn.z, x1 = mx.x, y1 = mx.y, z1 = mx.z;
        GQuad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, col);
        GQuad(new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), Vector3.back, col);
        GQuad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up, col);
        GQuad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), Vector3.down, col);
        GQuad(new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), Vector3.right, col);
        GQuad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, col);
    }

    static Color32 Col(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }

    static Mesh MakeModel(string name)
    {
        gv = new System.Collections.Generic.List<Vector3>(); gn = new System.Collections.Generic.List<Vector3>();
        gc = new System.Collections.Generic.List<Color32>(); gt = new System.Collections.Generic.List<int>();
        if (name == "gen_fuelPump")
        {
            // 0.6 x 1.7 x 0.4: a plinth, a red body with a white head, a dark screen, a hose and nozzle on the side
            GBox(new Vector3(-0.30f, 0f, -0.20f), new Vector3(0.30f, 0.12f, 0.20f), Col(150, 150, 150));
            GBox(new Vector3(-0.26f, 0.12f, -0.16f), new Vector3(0.26f, 1.25f, 0.16f), Col(200, 56, 48));
            GBox(new Vector3(-0.28f, 1.25f, -0.18f), new Vector3(0.28f, 1.70f, 0.18f), Col(236, 236, 232));
            GBox(new Vector3(-0.16f, 1.34f, -0.185f), new Vector3(0.16f, 1.58f, -0.18f), Col(40, 44, 52));
            GBox(new Vector3(-0.12f, 0.70f, -0.165f), new Vector3(0.12f, 0.90f, -0.16f), Col(230, 200, 60));
            GBox(new Vector3(0.26f, 0.55f, -0.04f), new Vector3(0.30f, 1.10f, 0.04f), Col(30, 30, 34));
            GBox(new Vector3(0.26f, 1.02f, -0.06f), new Vector3(0.36f, 1.12f, 0.06f), Col(30, 30, 34));
        }
        else if (name == "gen_car")
        {
            // 1.8 x 1.4 x 4.0: wheels, a body, a cabin with glass, lights
            Color32 paint = Col(60, 110, 170), glass = Col(150, 190, 210), tyre = Col(28, 28, 30), trim = Col(210, 210, 214);
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? -0.90f : 0.62f, z = (i & 2) == 0 ? -1.45f : 0.95f;
                GBox(new Vector3(x, 0f, z), new Vector3(x + 0.28f, 0.62f, z + 0.62f), tyre);
            }
            GBox(new Vector3(-0.88f, 0.28f, -1.98f), new Vector3(0.88f, 0.86f, 1.98f), paint);
            GBox(new Vector3(-0.80f, 0.86f, -0.95f), new Vector3(0.80f, 1.40f, 0.85f), paint);
            GBox(new Vector3(-0.81f, 0.92f, -0.90f), new Vector3(0.81f, 1.32f, 0.80f), glass);
            GBox(new Vector3(-0.75f, 0.94f, 0.80f), new Vector3(0.75f, 1.30f, 0.86f), glass);
            GBox(new Vector3(-0.75f, 0.94f, -0.96f), new Vector3(0.75f, 1.30f, -0.90f), glass);
            GBox(new Vector3(-0.70f, 0.58f, 1.98f), new Vector3(-0.40f, 0.72f, 2.00f), Col(250, 240, 200));
            GBox(new Vector3(0.40f, 0.58f, 1.98f), new Vector3(0.70f, 0.72f, 2.00f), Col(250, 240, 200));
            GBox(new Vector3(-0.70f, 0.58f, -2.00f), new Vector3(-0.40f, 0.72f, -1.98f), Col(200, 40, 40));
            GBox(new Vector3(0.40f, 0.58f, -2.00f), new Vector3(0.70f, 0.72f, -1.98f), Col(200, 40, 40));
            GBox(new Vector3(-0.90f, 0.30f, -2.00f), new Vector3(0.90f, 0.42f, 2.00f), trim);
        }
        else if (name == "gen_tireStack")
        {
            // 0.65 x 0.8 x 0.65: four tyres, each two crossed boxes, slightly off-centre
            for (int i = 0; i < 4; i++)
            {
                float y0 = i * 0.2f, o = (i % 2 == 0 ? 0.01f : -0.01f);
                GBox(new Vector3(-0.32f + o, y0, -0.22f), new Vector3(0.32f + o, y0 + 0.19f, 0.22f), Col(32, 32, 34));
                GBox(new Vector3(-0.22f + o, y0, -0.32f), new Vector3(0.22f + o, y0 + 0.19f, 0.32f), Col(32, 32, 34));
                GBox(new Vector3(-0.12f + o, y0 + 0.19f, -0.12f), new Vector3(0.12f + o, y0 + 0.191f, 0.12f), Col(70, 70, 74));
            }
        }
        var m = new Mesh { name = name };
        m.SetVertices(gv); m.SetNormals(gn); m.SetColors(gc); m.SetTriangles(gt, 0);
        m.RecalculateBounds();
        return m;
    }

    static Vector3 V(byte[] b, int p) { return new Vector3(BitConverter.ToSingle(b, p), BitConverter.ToSingle(b, p + 4), BitConverter.ToSingle(b, p + 8)); }

    // ------------------------------------------------------------------ colliders (as PropColliders.Build)
    static void Add(GameObject go, Vector3 mn, Vector3 mx)
    {
        var c = go.AddComponent<BoxCollider>();
        c.center = (mn + mx) * 0.5f;
        c.size = new Vector3(Mathf.Max(0.001f, mx.x - mn.x), Mathf.Max(0.001f, mx.y - mn.y), Mathf.Max(0.001f, mx.z - mn.z));
    }

    static void BuildColliders(GameObject go, int def, int model, float sc)
    {
        int kind = GenData.defCollider[def];
        int fl = GenData.defFlags[def];
        Bounds b = bounds[model];
        Vector3 mn = b.min, mx = b.max;
        float top = GenData.defTopY[def] / 1000f / sc;
        float cm = 0.01f / sc;
        if ((fl & GenData.FL_DECOR) != 0) return;
        // Fitted to the model (Cor: a small hider should get into the gaps a thing has: under a chair, between a
        // table's legs, into an open crate). The kinds whose collider is not the model's shape on purpose keep
        // theirs: a tree is only its trunk, a tent stays open, a flat thing is a thin base.
        if (kind != GenData.C_TRUNK && kind != GenData.C_TENT && kind != GenData.C_NONE && FitColliders(go, model, sc)) return;
        switch (kind)
        {
            case GenData.C_TABLE:
            {
                float slab = Mathf.Max(0.1f * (mx.y - mn.y), 5f * cm);
                float bottom = Mathf.Max(mn.y, top - slab);
                Add(go, new Vector3(mn.x, bottom, mn.z), new Vector3(mx.x, Mathf.Max(top, mx.y), mx.z));
                float leg = Mathf.Max(0.09f * Mathf.Min(mx.x - mn.x, mx.z - mn.z), 5f * cm);
                float inset = 2f * cm;
                for (int i = 0; i < 4; i++)
                {
                    float x0 = (i & 1) == 0 ? mn.x + inset : mx.x - inset - leg;
                    float z0 = (i & 2) == 0 ? mn.z + inset : mx.z - inset - leg;
                    Add(go, new Vector3(x0, mn.y, z0), new Vector3(x0 + leg, bottom, z0 + leg));
                }
                break;
            }
            case GenData.C_SEAT:
            {
                float seat = Mathf.Clamp(top, mn.y + 5f * cm, mx.y);
                Add(go, mn, new Vector3(mx.x, seat, mx.z));
                Add(go, new Vector3(mn.x, seat, mn.z), new Vector3(mx.x, mx.y, mn.z + 0.25f * (mx.z - mn.z)));
                break;
            }
            case GenData.C_HOLLOW:
            {
                float t = Mathf.Max(4f * cm, 0.04f * (mx.x - mn.x));
                Add(go, mn, new Vector3(mn.x + t, mx.y, mx.z));
                Add(go, new Vector3(mx.x - t, mn.y, mn.z), mx);
                Add(go, mn, new Vector3(mx.x, mx.y, mn.z + t));
                Add(go, new Vector3(mn.x, mx.y - t, mn.z), mx);
                Add(go, mn, new Vector3(mx.x, mn.y + Mathf.Max(t, InsideFloor(model)), mx.z));
                break;
            }
            case GenData.C_SHELF:
            {
                bool generated = (fl & GenData.FL_GENERATED) != 0;
                int a = GenModels.SurfStart[model], e = GenModels.SurfStart[model + 1];
                int maxArea = 0;
                for (int i = a; i < e; i++) if (GenModels.Surf[i * 6 + 5] > maxArea) maxArea = GenModels.Surf[i * 6 + 5];
                for (int i = a; i < e; i++)
                {
                    if (GenModels.Surf[i * 6 + 5] * 2 < maxArea) continue;
                    float y = GenModels.Surf[i * 6] / 1000f;
                    Add(go, new Vector3(mn.x, y - 3f * cm, mn.z), new Vector3(mx.x, y, mx.z));
                }
                if (!generated)
                {
                    float t = 3f * cm;
                    Add(go, mn, new Vector3(mn.x + t, mx.y, mx.z));
                    Add(go, new Vector3(mx.x - t, mn.y, mn.z), mx);
                    Add(go, mn, new Vector3(mx.x, mx.y, mn.z + t));
                }
                break;
            }
            case GenData.C_TRUNK:
            {
                float r = 0.22f / sc;
                Add(go, new Vector3(-r, mn.y, -r), new Vector3(r, mx.y, r));
                break;
            }
            case GenData.C_TENT:
            {
                float t = 6f * cm;
                float h = mx.y - mn.y;
                Add(go, mn, new Vector3(mx.x, mn.y + 0.75f * h, mn.z + t));
                Add(go, mn, new Vector3(mn.x + t, mn.y + 0.5f * h, mx.z));
                Add(go, new Vector3(mx.x - t, mn.y, mn.z), new Vector3(mx.x, mn.y + 0.5f * h, mx.z));
                break;
            }
            case GenData.C_NONE:
                Add(go, mn, new Vector3(mx.x, mn.y + 3f * cm, mx.z));
                break;
            default:
                Add(go, mn, new Vector3(mx.x, Mathf.Max(mx.y, mn.y + 2f * cm), mx.z));
                break;
        }
    }

    // ------------------------------------------------------------------ colliders fitted to the model
    // The model is cut into a grid of cells about 5 cm across (in the world, at the prop's scale). A cell a
    // triangle passes through is full, and so is a cell shut in on every side (the inside of a closed thing). The
    // full cells are merged into boxes, the largest first, each shrunk to the part of the model it holds. A
    // knockable prop is a rigidbody, which takes no hollow mesh collider: a handful of boxes is what it can have.
    // A model that needs more than FIT_MAX boxes is cut coarser until it does not (small gaps close first).
    const int FIT_MAX = 8, FIT_GRID = 18;
    const float FIT_CELL = 0.05f, FIT_SMALL = 0.05f, FIT_THIN = 0.02f;
    public static int fitBoxes, fitProps, fitSingle;
    static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Bounds>> fitCache = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Bounds>>();

    static bool FitColliders(GameObject go, int model, float sc)
    {
        Mesh m = mesh[model];
        if (m == null) return false;
        Bounds b = bounds[model];
        // (defs that share a model at one scale share the work)
        string key = model + "@" + sc.ToString("0.000");
        System.Collections.Generic.List<Bounds> known;
        if (fitCache.TryGetValue(key, out known))
        {
            if (known == null) return false;
            for (int i = 0; i < known.Count; i++) Add(go, known[i].min, known[i].max);
            fitProps++; fitBoxes += known.Count; if (known.Count == 1) fitSingle++;
            return true;
        }
        float cell = Mathf.Max(FIT_CELL / sc, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) / FIT_GRID);
        System.Collections.Generic.List<Bounds> boxes = null;
        for (int pass = 0; pass < 8; pass++)
        {
            boxes = FitBoxes(m, b, cell, FIT_SMALL / sc, FIT_SPARE);
            if (boxes.Count <= FIT_MAX) break;
            // Too many: where the extra boxes are small bits (a chair's stretchers, a slat) they are left out and
            // the gaps kept; cutting coarser instead closed them (a chair came out a solid block)
            if (boxes.Count <= FIT_SPARE)
            {
                boxes.Sort((p, q) => Vol(q).CompareTo(Vol(p)));
                float all = 0f, drop = 0f;
                for (int i = 0; i < boxes.Count; i++) { all += Vol(boxes[i]); if (i >= FIT_MAX) drop += Vol(boxes[i]); }
                if (drop <= FIT_DROP * all) { boxes.RemoveRange(FIT_MAX, boxes.Count - FIT_MAX); break; }
            }
            cell *= 1.35f;
        }
        if (boxes == null || boxes.Count == 0 || boxes.Count > FIT_MAX) { fitCache[key] = null; return false; }
        float thin = FIT_THIN / sc;
        var kept = new System.Collections.Generic.List<Bounds>();
        for (int i = 0; i < boxes.Count; i++)
        {
            Vector3 c = boxes[i].center, s = boxes[i].size;
            s = new Vector3(Mathf.Max(s.x, thin), Mathf.Max(s.y, thin), Mathf.Max(s.z, thin));
            // (a box made thicker stays inside the model's bounds)
            Vector3 mn = Vector3.Max(c - s * 0.5f, b.min), mx = Vector3.Min(c + s * 0.5f, b.max);
            if (mx.x - mn.x < thin) { mn.x = Mathf.Min(mn.x, b.max.x - thin); mx.x = mn.x + thin; }
            if (mx.y - mn.y < thin) { mn.y = Mathf.Min(mn.y, b.max.y - thin); mx.y = mn.y + thin; }
            if (mx.z - mn.z < thin) { mn.z = Mathf.Min(mn.z, b.max.z - thin); mx.z = mn.z + thin; }
            Add(go, mn, mx);
            var kb = new Bounds(); kb.SetMinMax(mn, mx); kept.Add(kb);
        }
        fitCache[key] = kept;
        fitProps++; fitBoxes += boxes.Count; if (boxes.Count == 1) fitSingle++;
        return true;
    }

    const int FIT_SPARE = 24;          // boxes found before giving up on a cut (the smallest beyond FIT_MAX may be left out)
    const float FIT_DROP = 0.1f;       // ... when together they are at most this share of the volume
    static float Vol(Bounds k) { return k.size.x * k.size.y * k.size.z; }

    static System.Collections.Generic.List<Bounds> FitBoxes(Mesh m, Bounds b, float cell, float small, int limit)
    {
        Vector3 mn = b.min, size = b.size;
        int nx = Mathf.Clamp(Mathf.CeilToInt(size.x / cell), 1, 24), ny = Mathf.Clamp(Mathf.CeilToInt(size.y / cell), 1, 24), nz = Mathf.Clamp(Mathf.CeilToInt(size.z / cell), 1, 24);
        Vector3 cs = new Vector3(Mathf.Max(size.x, 1e-5f) / nx, Mathf.Max(size.y, 1e-5f) / ny, Mathf.Max(size.z, 1e-5f) / nz);
        int n = nx * ny * nz;
        var occ = new bool[n];
        var lo = new Vector3[n];
        var hi = new Vector3[n];
        Vector3[] v = m.vertices;
        int[] t = m.triangles;
        float stepLen = Mathf.Min(cs.x, Mathf.Min(cs.y, cs.z)) * 0.5f;
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], e1 = v[t[i + 1]] - a, e2 = v[t[i + 2]] - a;
            float longest = Mathf.Max(e1.magnitude, Mathf.Max(e2.magnitude, (e2 - e1).magnitude));
            int k = Mathf.Clamp(Mathf.CeilToInt(longest / stepLen), 1, 96);
            for (int p = 0; p <= k; p++)
                for (int q = 0; q <= k - p; q++)
                {
                    Vector3 s = a + e1 * ((float)p / k) + e2 * ((float)q / k);
                    int ix = Mathf.Clamp((int)((s.x - mn.x) / cs.x), 0, nx - 1), iy = Mathf.Clamp((int)((s.y - mn.y) / cs.y), 0, ny - 1), iz = Mathf.Clamp((int)((s.z - mn.z) / cs.z), 0, nz - 1);
                    int id = ix + nx * (iy + ny * iz);
                    if (!occ[id]) { occ[id] = true; lo[id] = s; hi[id] = s; }
                    else { lo[id] = Vector3.Min(lo[id], s); hi[id] = Vector3.Max(hi[id], s); }
                }
        }
        // what the outside cannot reach is inside a closed thing: full
        var reached = new bool[n];
        var queue = new int[n];
        int qh = 0, qt = 0;
        for (int iz = 0; iz < nz; iz++)
            for (int iy = 0; iy < ny; iy++)
                for (int ix = 0; ix < nx; ix++)
                {
                    if (ix > 0 && ix < nx - 1 && iy > 0 && iy < ny - 1 && iz > 0 && iz < nz - 1) continue;
                    int id = ix + nx * (iy + ny * iz);
                    if (!occ[id] && !reached[id]) { reached[id] = true; queue[qt++] = id; }
                }
        while (qh < qt)
        {
            int id = queue[qh++];
            int ix = id % nx, iy = (id / nx) % ny, iz = id / (nx * ny);
            for (int d = 0; d < 6; d++)
            {
                int jx = ix + (d == 0 ? 1 : d == 1 ? -1 : 0), jy = iy + (d == 2 ? 1 : d == 3 ? -1 : 0), jz = iz + (d == 4 ? 1 : d == 5 ? -1 : 0);
                if (jx < 0 || jy < 0 || jz < 0 || jx >= nx || jy >= ny || jz >= nz) continue;
                int jd = jx + nx * (jy + ny * jz);
                if (occ[jd] || reached[jd]) continue;
                reached[jd] = true; queue[qt++] = jd;
            }
        }
        for (int id = 0; id < n; id++)
        {
            if (occ[id] || reached[id]) continue;
            int ix = id % nx, iy = (id / nx) % ny, iz = id / (nx * ny);
            occ[id] = true;
            lo[id] = mn + new Vector3(ix * cs.x, iy * cs.y, iz * cs.z);
            hi[id] = lo[id] + cs;
        }
        // boxes, the one that takes the most cells first; a seed grows along the axes in three orders
        var used = new bool[n];
        var boxes = new System.Collections.Generic.List<Bounds>();
        int[] g0 = new int[3], g1 = new int[3], dim = { nx, ny, nz };
        int[][] orders = { new[] { 0, 2, 1 }, new[] { 0, 1, 2 }, new[] { 2, 1, 0 } };
        for (int round = 0; round < 48; round++)
        {
            int best = 0; int[] b0 = null, b1 = null;
            for (int id = 0; id < n; id++)
            {
                if (!occ[id] || used[id]) continue;
                int sx = id % nx, sy = (id / nx) % ny, sz = id / (nx * ny);
                for (int o = 0; o < 3; o++)
                {
                    g0[0] = sx; g0[1] = sy; g0[2] = sz; g1[0] = sx; g1[1] = sy; g1[2] = sz;
                    for (int ai = 0; ai < 3; ai++)
                    {
                        int ax = orders[o][ai];
                        while (g1[ax] + 1 < dim[ax] && FreeSlab(occ, used, nx, ny, g0, g1, ax, g1[ax] + 1)) g1[ax]++;
                        while (g0[ax] - 1 >= 0 && FreeSlab(occ, used, nx, ny, g0, g1, ax, g0[ax] - 1)) g0[ax]--;
                    }
                    int cells = (g1[0] - g0[0] + 1) * (g1[1] - g0[1] + 1) * (g1[2] - g0[2] + 1);
                    if (cells > best) { best = cells; b0 = (int[])g0.Clone(); b1 = (int[])g1.Clone(); }
                }
            }
            if (best == 0) break;
            Vector3 bl = Vector3.positiveInfinity, bh = Vector3.negativeInfinity;
            for (int iz = b0[2]; iz <= b1[2]; iz++)
                for (int iy = b0[1]; iy <= b1[1]; iy++)
                    for (int ix = b0[0]; ix <= b1[0]; ix++)
                    {
                        int id = ix + nx * (iy + ny * iz);
                        used[id] = true;
                        bl = Vector3.Min(bl, lo[id]); bh = Vector3.Max(bh, hi[id]);
                    }
            Vector3 sz3 = bh - bl;
            // a crumb (a knob, a leaf's tip) is left out
            if (Mathf.Max(sz3.x, Mathf.Max(sz3.y, sz3.z)) < small) continue;
            boxes.Add(new Bounds((bl + bh) * 0.5f, sz3));
            if (boxes.Count > limit) break;   // too many: the caller cuts coarser
        }
        return boxes;
    }

    // whether the layer at index `at` along axis `ax`, across the box's other two extents, is all full and unused
    static bool FreeSlab(bool[] occ, bool[] used, int nx, int ny, int[] g0, int[] g1, int ax, int at)
    {
        int a = (ax + 1) % 3, b = (ax + 2) % 3;
        int[] c = new int[3];
        c[ax] = at;
        for (c[a] = g0[a]; c[a] <= g1[a]; c[a]++)
            for (c[b] = g0[b]; c[b] <= g1[b]; c[b]++)
            {
                int id = c[0] + nx * (c[1] + ny * c[2]);
                if (!occ[id] || used[id]) return false;
            }
        return true;
    }

    static float InsideFloor(int model)
    {
        int a = GenModels.SurfStart[model], e = GenModels.SurfStart[model + 1];
        float best = 0f;
        float h = GenModels.SizeMm[model * 3 + 1] / 1000f;
        for (int i = a; i < e; i++)
        {
            float y = GenModels.Surf[i * 6] / 1000f;
            if (y > 0.01f && y < 0.3f * h && y > best) best = y;
        }
        return best;
    }
}
