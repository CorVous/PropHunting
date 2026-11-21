using System;
using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// The hand-held models and their effects (PHWeapons): the meshes from Resources/PropHunt/weapons.bytes
// (Tools/make_weapons.py) into Assets/Udon/Generated/Weapons.asset, one held weapon per slot, the particle systems,
// the desktop selector, and the blaster in PHView's VR selector. Called by WorldSceneBuilder.Build; the menu item
// rebuilds just this part in the open scene.
public static class WeaponBuilder
{
    const string GEN = "Assets/Udon/Generated/";
    const string MESHES = GEN + "Weapons.asset";
    const string WEAR = GEN + "WeaponWear.asset";

    [MenuItem("PropHunt/Build weapons")]
    static void Menu()
    {
        var game = Object.FindObjectOfType<PHGame>(true);
        var local = Object.FindObjectOfType<PHLocal>(true);
        if (game == null || local == null) { Debug.LogError("[prophunt] no world scene open (Build world scene first)"); return; }
        var old = game.transform.Find("Weapons");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Build(game.transform, game, local, game.slots);
        UdonSharpEditorUtility.CopyProxyToUdon(local);
        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
    }

    // Tools/weapon_grips.tsv: one line per weapon id: id, the grip point (x y z, metres in the model's own space), the
    // turn about it (x y z degrees), the size (1 = as made); # starts a comment
    const string GRIPS = "Tools/weapon_grips.tsv";
    struct Grip { public Vector3 grip, turn; public float size; }

    static Dictionary<string, Grip> LoadGrips()
    {
        var d = new Dictionary<string, Grip>();
        string path = System.IO.Path.Combine(Application.dataPath, "..", GRIPS);
        if (!System.IO.File.Exists(path)) { Debug.LogWarning("[prophunt] " + GRIPS + " is missing: every weapon held at its origin"); return d; }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var raw in System.IO.File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('\t');
            if (f.Length < 4) { Debug.LogWarning("[prophunt] " + GRIPS + ": bad line " + line); continue; }
            Vector3 V3(string t) { var c = t.Trim().Split(' '); return new Vector3(float.Parse(c[0], inv), float.Parse(c[1], inv), float.Parse(c[2], inv)); }
            d[f[0].Trim()] = new Grip { grip = V3(f[1]), turn = V3(f[2]), size = float.Parse(f[3].Trim(), inv) };
        }
        return d;
    }

    class Model { public string kind, name, id; public Mesh mesh; public float tip, head; public Vector3[] spots; public Color color;
        public bool card; public Mesh off, shelf; public bool offShield; public float offTip, offHead; }

    // ---- Cor's drawn weapons (Tools/make_card_weapons.py -> Tools/card_weapons.json and the atlas
    // Assets/Udon/Textures/CardWeapons.png, with the wear in CardWeaponsWear.png): each piece a flat card, the drawing
    // on both faces. Their vertices are white with alpha 0, which tells the weapon shaders to draw the atlas at their
    // uv (and cut it out along its alpha). A two-piece set's other piece is the off hand's (PHWeapons.offMesh).
    const string CARDS = "Tools/card_weapons.json";
    public const string CARD_ATLAS = "Assets/Udon/Textures/CardWeapons.png", CARD_WEAR = "Assets/Udon/Textures/CardWeaponsWear.png";
    [Serializable] class CardCorner { public float[] p; public float[] uv; }
    [Serializable] class CardPiece { public string role, plane; public CardCorner[] corners; public float tip, head; public int[] color; public float[] spots; }   // spots: x y z, x y z, ...
    [Serializable] class CardShelf { public CardCorner[] corners; }
    [Serializable] class CardWeapon { public string id; public CardPiece[] pieces; public CardShelf[] shelf; }
    [Serializable] class CardFile { public CardWeapon[] weapons; }

    static Mesh SubMesh(string name, Dictionary<string, Mesh> keep, ref Mesh holder)
    {
        Mesh me;
        if (!keep.TryGetValue(name, out me))
        {
            me = new Mesh { name = name };
            if (holder == null) { AssetDatabase.CreateAsset(me, MESHES); holder = me; }
            else AssetDatabase.AddObjectToAsset(me, MESHES);
            keep[name] = me;
        }
        me.Clear();
        return me;
    }

    // quads (corners: bottom-left, bottom-right, top-right, top-left of the picture) as cards: a front face and a back
    // face a hair behind it, each facing out
    static void CardMesh(Mesh me, List<CardCorner[]> quads)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        foreach (var q in quads)
        {
            var p = new Vector3[4];
            for (int k = 0; k < 4; k++) p[k] = new Vector3(q[k].p[0], q[k].p[1], q[k].p[2]);
            Vector3 nf = Vector3.Cross(p[3] - p[0], p[2] - p[0]).normalized;   // towards whoever sees the drawing as drawn
            for (int side = 0; side < 2; side++)
            {
                Vector3 nn = side == 0 ? nf : -nf;
                int b = v.Count;
                for (int k = 0; k < 4; k++) { v.Add(p[k] + nn * 0.0008f); n.Add(nn); uv.Add(new Vector2(q[k].uv[0], q[k].uv[1])); }
                if (side == 0) t.AddRange(new[] { b, b + 3, b + 2, b, b + 2, b + 1 });
                else t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
        }
        me.SetVertices(v); me.SetNormals(n); me.SetUVs(0, uv);
        var c = new Color32[v.Count];
        for (int i = 0; i < c.Length; i++) c[i] = new Color32(255, 255, 255, 0);
        me.colors32 = c;
        me.SetTriangles(t, 0);
        me.RecalculateBounds();
        EditorUtility.SetDirty(me);
    }

    static List<Model> LoadCards()
    {
        var list = new List<Model>();
        string path = System.IO.Path.Combine(Application.dataPath, "..", CARDS);
        if (!System.IO.File.Exists(path)) return list;
        var root = JsonUtility.FromJson<CardFile>(System.IO.File.ReadAllText(path));
        var keep = new Dictionary<string, Mesh>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(MESHES)) if (o is Mesh mm) keep[mm.name] = mm;
        Mesh holder = AssetDatabase.LoadAssetAtPath<Mesh>(MESHES);
        foreach (var w in root.weapons)
        {
            string baseName = w.id.Replace(':', '_');
            var mo = new Model { kind = "melee", id = w.id, card = true };
            string[] parts = w.id.Split(':'); mo.name = parts[parts.Length - 1];
            foreach (var p in w.pieces)
            {
                var quads = new List<CardCorner[]> { p.corners };
                if (p.role == "main")
                {
                    mo.mesh = SubMesh(baseName, keep, ref holder); CardMesh(mo.mesh, quads);
                    mo.tip = p.tip; mo.head = p.head;
                    mo.color = new Color(p.color[0] / 255f, p.color[1] / 255f, p.color[2] / 255f);
                    mo.spots = new Vector3[PHWeapons.SPOTS];
                    for (int s = 0; s < mo.spots.Length; s++)
                    {
                        int ns = p.spots != null ? p.spots.Length / 3 : 0;
                        mo.spots[s] = ns > 0 ? new Vector3(p.spots[(s % ns) * 3], p.spots[(s % ns) * 3 + 1], p.spots[(s % ns) * 3 + 2]) : new Vector3(0f, 0f, p.tip * 0.7f);
                    }
                }
                else
                {
                    mo.off = SubMesh(baseName + "_off", keep, ref holder); CardMesh(mo.off, quads);
                    mo.offShield = p.role == "shield"; mo.offTip = p.tip; mo.offHead = p.head;
                }
            }
            if (w.shelf != null && w.shelf.Length > 0)
            {
                var sq = new List<CardCorner[]>();
                foreach (var q in w.shelf) sq.Add(q.corners);
                mo.shelf = SubMesh(baseName + "_shelf", keep, ref holder); CardMesh(mo.shelf, sq);
            }
            if (mo.mesh != null) list.Add(mo);
        }
        AssetDatabase.SaveAssets();
        return list;
    }

    // the atlas and its wear onto a weapon material (the textures' import: the wear is data, not colour)
    public static void CardTextures(Material m)
    {
        foreach (var path in new[] { CARD_ATLAS, CARD_WEAR })
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) continue;
            bool data = path == CARD_WEAR;
            if (imp.sRGBTexture == !data && imp.alphaIsTransparency == !data && imp.textureCompression == TextureImporterCompression.Uncompressed
                && imp.wrapMode == TextureWrapMode.Clamp && imp.mipmapEnabled == !data && imp.npotScale == TextureImporterNPOTScale.None) continue;
            imp.sRGBTexture = !data; imp.alphaIsTransparency = !data;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = !data; imp.npotScale = TextureImporterNPOTScale.None;
            imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = 2048;
            if (data) imp.textureType = TextureImporterType.SingleChannel;
            imp.SaveAndReimport();
        }
        m.SetTexture("_CardTex", AssetDatabase.LoadAssetAtPath<Texture2D>(CARD_ATLAS));
        m.SetTexture("_CardWear", AssetDatabase.LoadAssetAtPath<Texture2D>(CARD_WEAR));
        EditorUtility.SetDirty(m);
    }

    static List<Model> Load()
    {
        var asset = Resources.Load<TextAsset>("PropHunt/weapons");
        if (asset == null) throw new Exception("weapons.bytes is missing from Resources/PropHunt (run Tools/make_weapons.py)");
        byte[] b = asset.bytes;
        if (b[0] != 'P' || b[1] != 'H' || b[2] != 'M' || b[3] != '1') throw new Exception("weapons.bytes: bad header");
        int p = 4;
        int n = BitConverter.ToInt32(b, p); p += 4;
        var list = new List<Model>();
        // the meshes live in one asset, reused (so a rebuild keeps the references)
        var meshAsset = AssetDatabase.LoadAllAssetsAtPath(MESHES);
        var keep = new Dictionary<string, Mesh>();
        foreach (var o in meshAsset) if (o is Mesh mm) keep[mm.name] = mm;
        Mesh holder = AssetDatabase.LoadAssetAtPath<Mesh>(MESHES);
        for (int m = 0; m < n; m++)
        {
            int nl = b[p++]; string name = System.Text.Encoding.UTF8.GetString(b, p, nl); p += nl;
            int pl = b[p++]; p += pl;
            Vector3 piv = V(b, p); p += 12;
            p += 24;   // bounds
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
            string meshName = name.Replace(':', '_');
            Mesh me;
            if (!keep.TryGetValue(meshName, out me))
            {
                me = new Mesh { name = meshName };
                if (holder == null) { AssetDatabase.CreateAsset(me, MESHES); holder = me; }
                else AssetDatabase.AddObjectToAsset(me, MESHES);
            }
            me.Clear();
            if (nv >= 65536) me.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            me.vertices = verts; me.normals = norms; me.colors32 = cols; me.triangles = tris;
            me.RecalculateBounds();
            EditorUtility.SetDirty(me);
            string[] parts = name.Split(':');
            var mo = new Model { kind = parts[0], name = parts.Length > 2 ? parts[2] : name, id = name, mesh = me, tip = piv.y };
            Head(mo, verts, cols, tris);
            list.Add(mo);
        }
        AssetDatabase.SaveAssets();
        return list;
    }

    // the nibbled wear grids (Tools/bake_wear.py -> Resources/PropHunt/weapon_wear.bytes): one Texture3D per melee model,
    // kept as sub-assets of WeaponWear.asset; a model without a grid is drawn whole whatever its wear
    static void LoadWear(PHWeapons w, List<Model> melee)
    {
        int n = melee.Count;
        w.meleeWear = new Texture[n]; w.meleeWearLo = new Vector4[n]; w.meleeWearInv = new Vector4[n];
        var asset = Resources.Load<TextAsset>("PropHunt/weapon_wear");
        if (asset == null) { Debug.LogWarning("[prophunt] weapon_wear.bytes is missing (run Tools/bake_wear.py): weapons will not wear"); return; }
        byte[] b = asset.bytes;
        if (b[0] != 'P' || b[1] != 'H' || b[2] != 'W' || b[3] != '1') throw new Exception("weapon_wear.bytes: bad header");
        var old = new Dictionary<string, Texture3D>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(WEAR)) if (o is Texture3D t3) old[t3.name] = t3;
        Texture3D holder = AssetDatabase.LoadAssetAtPath<Texture3D>(WEAR);
        int p = 4;
        int count = BitConverter.ToInt32(b, p); p += 4;
        int found = 0;
        for (int m = 0; m < count; m++)
        {
            int nl = b[p++]; string name = System.Text.Encoding.UTF8.GetString(b, p, nl); p += nl;
            Vector3 lo = V(b, p); p += 12;
            float res = BitConverter.ToSingle(b, p); p += 4;
            int dx = BitConverter.ToInt32(b, p), dy = BitConverter.ToInt32(b, p + 4), dz = BitConverter.ToInt32(b, p + 8); p += 12;
            int len = dx * dy * dz;
            int k = melee.FindIndex(mo => mo.id == name);
            if (k >= 0)
            {
                string texName = name.Replace(':', '_');
                Texture3D tex;
                if (old.TryGetValue(texName, out tex) && (tex.width != dx || tex.height != dy || tex.depth != dz)) { AssetDatabase.RemoveObjectFromAsset(tex); tex = null; }
                if (tex == null)
                {
                    tex = new Texture3D(dx, dy, dz, TextureFormat.R8, false) { name = texName };
                    if (holder == null) { AssetDatabase.CreateAsset(tex, WEAR); holder = tex; }
                    else AssetDatabase.AddObjectToAsset(tex, WEAR);
                }
                tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
                var data = new byte[len];
                Buffer.BlockCopy(b, p, data, 0, len);
                tex.SetPixelData(data, 0);
                tex.Apply(false, false);
                EditorUtility.SetDirty(tex);
                w.meleeWear[k] = tex;
                w.meleeWearLo[k] = new Vector4(lo.x, lo.y, lo.z, res);
                w.meleeWearInv[k] = new Vector4(1f / (dx * res), 1f / (dy * res), 1f / (dz * res), 0f);
                found++;
            }
            p += len;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[prophunt] weapon wear grids: " + found + " of " + n + " melee models");
    }

    static Vector3 V(byte[] b, int p) { return new Vector3(BitConverter.ToSingle(b, p), BitConverter.ToSingle(b, p + 4), BitConverter.ToSingle(b, p + 8)); }

    // where the head begins (the slices above it are all wider than the handle by half again), spots spread over
    // the head's corners (farthest-point picks, where chunks bite best), and the head's main colour
    static void Head(Model mo, Vector3[] v, Color32[] c, int[] t)
    {
        float r0 = 0.005f;
        foreach (var p in v) if (p.z > -0.05f && p.z < 0.1f) r0 = Mathf.Max(r0, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)));
        const int SL = 40;
        float tip = mo.tip;
        var rad = new float[SL];
        foreach (var p in v)
        {
            if (p.z < 0f) continue;
            int k = Mathf.Clamp((int)(p.z / tip * SL), 0, SL - 1);
            rad[k] = Mathf.Max(rad[k], Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)));
        }
        int from = SL - 1;
        while (from > 0 && (rad[from - 1] > r0 * 1.5f || rad[from - 1] == 0f)) from--;
        mo.head = Mathf.Min(tip * from / SL, tip * 0.75f);
        // chunks come off the business end: never lower than 45% of the way up (a wide band on a handle read as the head)
        mo.head = Mathf.Max(mo.head, tip * 0.45f);
        var cand = new List<Vector3>();
        foreach (var p in v) if (p.z >= mo.head && !cand.Exists(q => (q - p).sqrMagnitude < 1e-6f)) cand.Add(p);
        var spots = new Vector3[PHWeapons.SPOTS];
        Vector3 centre = new Vector3(0f, 0f, (mo.head + tip) * 0.5f);
        // the first: the corner farthest from the head's middle; then each the farthest from those picked
        int first = 0;
        for (int i = 1; i < cand.Count; i++) if ((cand[i] - centre).sqrMagnitude > (cand[first] - centre).sqrMagnitude) first = i;
        var dist = new float[cand.Count];
        for (int i = 0; i < cand.Count; i++) dist[i] = float.MaxValue;
        int pick = first;
        for (int s = 0; s < spots.Length; s++)
        {
            spots[s] = cand.Count > 0 ? cand[pick] : centre;
            for (int i = 0; i < cand.Count; i++) dist[i] = Mathf.Min(dist[i], (cand[i] - cand[pick]).sqrMagnitude);
            int best = 0;
            for (int i = 1; i < cand.Count; i++) if (dist[i] > dist[best]) best = i;
            pick = best;
        }
        mo.spots = spots;
        // the head's colour, by face area
        Vector3 sum = Vector3.zero; float area = 0f;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], bb = v[t[i + 1]], cc = v[t[i + 2]];
            if ((a.z + bb.z + cc.z) / 3f < mo.head) continue;
            float ar = Vector3.Cross(bb - a, cc - a).magnitude;
            Color32 k = c[t[i]];
            sum += new Vector3(k.r, k.g, k.b) / 255f * ar; area += ar;
        }
        mo.color = area > 0 ? new Color(sum.x / area, sum.y / area, sum.z / area) : Color.gray;
    }

    static Material Mat(string name, string shader)
    {
        string path = GEN + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find(shader);
        if (sh == null) throw new Exception("shader " + shader + " is missing");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        return m;
    }

    static Material FxMat(string name, Color tint, bool add, bool round, bool trail)
    {
        var m = Mat(name, "PropHunt/Fx");
        m.SetColor("_Color", tint);
        m.SetFloat("_Src", (float)(add ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.SrcAlpha));
        m.SetFloat("_Dst", (float)(add ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_Round", round ? 1f : 0f);
        m.SetFloat("_Trail", trail ? 1f : 0f);
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);
        return m;
    }

    public static PHWeapons Build(Transform gameRoot, PHGame game, PHLocal local, PHSlot[] slots)
    {
        var models = Load();
        var melee = models.FindAll(m => m.kind == "melee");
        melee.AddRange(LoadCards());   // (after the blob's: the picks' numbers of the others stay)
        var gun = models.Find(m => m.kind == "gun" && m.name == "blaster-b") ?? models.Find(m => m.kind == "gun");
        var root = new GameObject("Weapons");
        root.transform.SetParent(gameRoot, false);
        var w = root.AddUdonSharpComponent<PHWeapons>();
        w.game = game; w.local = local; w.sound = local.sound;
        w.meleeMesh = melee.ConvertAll(m => m.mesh).ToArray();
        w.meleeId = melee.ConvertAll(m => m.id).ToArray();
        var guns = models.FindAll(m => m.kind == "gun");
        // the starter selector first (index 0 = the default)
        guns.Sort((a, b) => (b.name == "blaster-b").CompareTo(a.name == "blaster-b"));
        w.gunMesh = guns.ConvertAll(m => m.mesh).ToArray();
        w.gunId = guns.ConvertAll(m => m.id).ToArray();
        w.gunMuzzle = guns.ConvertAll(m => m.tip).ToArray();
        // where the barrel ends, in the model's own space: the middle of its front-most vertices (the barrel need
        // not lie on the model's z axis; Cor: the laser must come out of the gun)
        w.gunMuzzlePoint = guns.ConvertAll(m =>
        {
            var v = m.mesh.vertices;
            float zMax = float.MinValue;
            foreach (var p in v) zMax = Mathf.Max(zMax, p.z);
            Vector2 sum = Vector2.zero; int n = 0;
            foreach (var p in v) if (p.z > zMax - 0.012f) { sum += new Vector2(p.x, p.y); n++; }
            if (n > 0) sum /= n;
            return new Vector3(sum.x, sum.y, zMax);
        }).ToArray();
        // each model's grip, turn and size (Tools/weapon_grips.tsv; each player's own hold goes on top, PHHand)
        var grips = LoadGrips();
        Grip Gr(Model m) { Grip g; return grips.TryGetValue(m.id, out g) ? g : new Grip { size = 1f }; }
        w.meleeGrip = melee.ConvertAll(m => Gr(m).grip).ToArray();
        w.meleeTurn = melee.ConvertAll(m => Gr(m).turn).ToArray();
        w.meleeSize = melee.ConvertAll(m => Gr(m).size).ToArray();
        w.gunGrip = guns.ConvertAll(m => Gr(m).grip).ToArray();
        w.gunTurn = guns.ConvertAll(m => Gr(m).turn).ToArray();
        w.gunSize = guns.ConvertAll(m => Gr(m).size).ToArray();
        Debug.Log("[prophunt] weapon grips: " + grips.Count + " from " + GRIPS);
        w.meleeTip = melee.ConvertAll(m => m.tip).ToArray();
        w.meleeHead = melee.ConvertAll(m => m.head).ToArray();
        w.meleeColor = melee.ConvertAll(m => m.color).ToArray();
        LoadWear(w, melee);
        // the drawn ones: the atlas and the off hand's pieces
        w.meleeCard = melee.ConvertAll(m => m.card).ToArray();
        w.offMesh = melee.ConvertAll(m => m.off).ToArray();
        w.offShield = melee.ConvertAll(m => m.offShield).ToArray();
        w.offTip = melee.ConvertAll(m => m.offTip).ToArray();
        w.offHead = melee.ConvertAll(m => m.offHead).ToArray();
        var spots = new List<Vector3>();
        foreach (var m in melee) spots.AddRange(m.spots);
        w.chipSpots = spots.ToArray();
        // by map theme (GenData.THEME_NONE, SPACE, MEDIEVAL): the town hammer, the metal sledge, the mace
        int Find(string n) { int i = melee.FindIndex(m => m.name == n); return i < 0 ? 0 : i; }
        w.themeMelee = new[] { Find("tool-hammer"), Find("tool-hammer-upgraded"), Find("hammer_B") };
        foreach (var m in melee) Debug.Log("[prophunt] weapon " + m.name + ": tip " + m.tip.ToString("0.00") + " head from " + m.head.ToString("0.00"));

        var weaponMat = Mat("Weapon", "PropHunt/Weapon");
        CardTextures(weaponMat);
        var trailMat = FxMat("FxTrail", new Color(1f, 1f, 1f, 0.55f), true, false, true);
        int n = slots.Length;
        w.held = new Transform[n]; w.heldFilter = new MeshFilter[n]; w.heldRenderer = new MeshRenderer[n]; w.heldTrail = new TrailRenderer[n];
        w.heldOff = new Transform[n]; w.heldOffFilter = new MeshFilter[n]; w.heldOffRenderer = new MeshRenderer[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Held" + i);
            go.transform.SetParent(root.transform, false);
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = w.meleeMesh[0];
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = weaponMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var tipGo = new GameObject("Tip");
            tipGo.transform.SetParent(go.transform, false);
            tipGo.transform.localPosition = new Vector3(0f, 0f, melee[0].tip * 0.85f);
            var tr = tipGo.AddComponent<TrailRenderer>();
            tr.time = 0.14f; tr.minVertexDistance = 0.02f; tr.emitting = false; tr.autodestruct = false;
            tr.widthMultiplier = 0.32f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.9f, 1f), 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.sharedMaterial = trailMat;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tr.receiveShadows = false;
            tr.alignment = LineAlignment.View;
            go.SetActive(false);
            w.held[i] = go.transform; w.heldFilter[i] = mf; w.heldRenderer[i] = mr; w.heldTrail[i] = tr;
            // the off hand's piece of a two-piece set (Cor's drawn weapons)
            var off = new GameObject("HeldOff" + i);
            off.transform.SetParent(root.transform, false);
            var omf = off.AddComponent<MeshFilter>();
            var omr = off.AddComponent<MeshRenderer>(); omr.sharedMaterial = weaponMat;
            omr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; omr.receiveShadows = false;
            omr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; omr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            off.SetActive(false);
            w.heldOff[i] = off.transform; w.heldOffFilter[i] = omf; w.heldOffRenderer[i] = omr;
            slots[i].weapons = w;
            UdonSharpEditorUtility.CopyProxyToUdon(slots[i]);
        }

        // ---- effects
        var fxRoot = new GameObject("Fx").transform;
        fxRoot.SetParent(root.transform, false);
        w.fxDust = Fx(fxRoot, "Dust", FxMat("FxDust", new Color(0.86f, 0.80f, 0.70f, 0.55f), false, true, false),
            new Vector2(0.5f, 0.9f), new Vector2(0.4f, 1.4f), new Vector2(0.08f, 0.2f), 0.05f, -0.05f, new Color(1, 1, 1, 1), new Color(0.9f, 0.88f, 0.84f, 1), true, false);
        w.fxChips = Fx(fxRoot, "Chips", FxMat("FxChips", Color.white, false, false, false),
            new Vector2(0.6f, 1.0f), new Vector2(1.4f, 3.2f), new Vector2(0.018f, 0.05f), 0.04f, 1.6f, new Color(0.55f, 0.40f, 0.26f, 1), new Color(0.45f, 0.45f, 0.48f, 1), false, true);
        // the weapon's own pieces when it wears (PHWeapons.ChipOff, onboarding): a handful of little tumbling blocks,
        // bigger than the splinters, steel and wood, that fly off and drop (cube meshes, one pooled system)
        w.fxWear = Fx(fxRoot, "WearPieces", FxMat("FxWear", Color.white, false, false, false),
            new Vector2(0.7f, 1.1f), new Vector2(1.2f, 2.6f), new Vector2(0.014f, 0.03f), 0.03f, 1.5f, new Color(0.62f, 0.64f, 0.68f, 1), new Color(0.50f, 0.36f, 0.22f, 1), false, false);
        {
            var main = w.fxWear.main;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 60;
            var rol = w.fxWear.rotationOverLifetime; rol.enabled = true; rol.separateAxes = true;
            rol.x = new ParticleSystem.MinMaxCurve(-12f, 12f); rol.y = new ParticleSystem.MinMaxCurve(-12f, 12f); rol.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            var sh = w.fxWear.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere;   // (mostly up and out)
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var pr = w.fxWear.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Mesh;
            pr.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            pr.alignment = ParticleSystemRenderSpace.World;
        }
        w.fxSpark = Fx(fxRoot, "Spark", FxMat("FxSpark", Color.white, true, true, false),
            new Vector2(0.15f, 0.35f), new Vector2(1.8f, 4.2f), new Vector2(0.015f, 0.035f), 0.02f, 0.9f, new Color(1f, 0.9f, 0.5f, 1), new Color(1f, 1f, 0.85f, 1), false, false);
        w.fxHider = Fx(fxRoot, "Confetti", FxMat("FxConfetti", Color.white, false, false, false),
            new Vector2(0.8f, 1.3f), new Vector2(2.0f, 3.6f), new Vector2(0.035f, 0.07f), 0.05f, 0.8f, Color.white, Color.white, false, true);
        {   // confetti: any of the board game's colours
            var main = w.fxHider.main;
            var cg = new Gradient();
            cg.SetKeys(new[] { new GradientColorKey(new Color(0.92f, 0.26f, 0.22f), 0f), new GradientColorKey(new Color(1f, 0.72f, 0.2f), 0.33f),
                               new GradientColorKey(new Color(0.25f, 0.8f, 0.75f), 0.66f), new GradientColorKey(new Color(0.45f, 0.4f, 0.9f), 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            cg.mode = GradientMode.Fixed;
            main.startColor = new ParticleSystem.MinMaxGradient(cg) { mode = ParticleSystemGradientMode.RandomColor };
        }
        w.fxMuzzle = Fx(fxRoot, "Muzzle", FxMat("FxMuzzle", Color.white, true, true, false),
            new Vector2(0.06f, 0.11f), new Vector2(0.2f, 0.8f), new Vector2(0.03f, 0.07f), 0.01f, 0f, new Color(0.5f, 1f, 0.9f, 1), new Color(1f, 1f, 1f, 1), true, false);

        // ---- the selector: a blaster in the VR hand (PHView's Selector keeps placing it) and a desktop one in the view
        if (gun != null)
        {
            w.selectorMuzzle = gun.tip;
            var view = Object.FindObjectOfType<PHView>(true);
            if (view != null && view.gun != null)
            {
                foreach (Transform ch in view.gun) if (ch.name != "Model") ch.gameObject.SetActive(false);
                var oldModel = view.gun.Find("Model");
                if (oldModel != null) Object.DestroyImmediate(oldModel.gameObject);
                w.vrSelectorModel = GunModel(view.gun, gun.mesh, weaponMat);
            }
            var desk = new GameObject("DeskSelector");
            desk.transform.SetParent(root.transform, false);
            desk.layer = 5;
            w.deskSelector = desk.transform;
            w.deskSelectorModel = GunModel(desk.transform, gun.mesh, weaponMat);
            desk.SetActive(false);
            // the final stand: each hider's gun as others see it, at their prop (default layer: every view draws it)
            var gunRoot = new GameObject("HiderGuns").transform;
            gunRoot.SetParent(root.transform, false);
            w.hiderGun = new Transform[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                var g = new GameObject("Gun" + i);
                g.transform.SetParent(gunRoot, false);
                var model = GunModel(g.transform, gun.mesh, weaponMat);
                model.gameObject.layer = 0;
                g.SetActive(false);
                w.hiderGun[i] = g.transform;
            }
            // the bullets: a small bright pellet each, with a trail
            var pelletMat = FxMat("FxPellet", new Color(0.75f, 1f, 0.95f, 1f), true, false, false);
            var bulletTrail = FxMat("FxBulletTrail", new Color(0.5f, 1f, 0.9f, 0.8f), true, false, true);
            int nb = 6;
            w.bullets = new Transform[nb]; w.bulletTrails = new TrailRenderer[nb];
            for (int b2 = 0; b2 < nb; b2++)
            {
                var pellet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pellet.name = "Bullet" + b2;
                Object.DestroyImmediate(pellet.GetComponent<Collider>());
                pellet.transform.SetParent(root.transform, false);
                pellet.transform.localScale = Vector3.one * 0.08f;
                var pmr = pellet.GetComponent<MeshRenderer>();
                pmr.sharedMaterial = pelletMat;
                pmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pmr.receiveShadows = false;
                var trail = pellet.AddComponent<TrailRenderer>();
                trail.time = PHWeapons.BULLET_TRAIL; trail.minVertexDistance = 0.1f; trail.emitting = false; trail.autodestruct = false;
                trail.widthMultiplier = 0.09f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                var tg = new Gradient();
                tg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.4f, 1f, 0.85f), 1f) },
                           new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = tg;
                trail.sharedMaterial = bulletTrail;
                trail.alignment = LineAlignment.View;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; trail.receiveShadows = false;
                pellet.SetActive(false);
                w.bullets[b2] = pellet.transform; w.bulletTrails[b2] = trail;
            }
        }
        // ---- the halos over the dead (one per slot; an unlit gold ring on the default layer)
        {
            var haloMesh = HaloMesh();
            var haloMat = Mat("Halo", "Unlit/Color");
            haloMat.SetColor("_Color", new Color(1f, 0.86f, 0.42f, 1f));
            EditorUtility.SetDirty(haloMat);
            var haloRoot = new GameObject("Halos").transform;
            haloRoot.SetParent(root.transform, false);
            w.halo = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                var hg = new GameObject("Halo" + i);
                hg.transform.SetParent(haloRoot, false);
                hg.AddComponent<MeshFilter>().sharedMesh = haloMesh;
                var hr = hg.AddComponent<MeshRenderer>(); hr.sharedMaterial = haloMat;
                hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; hr.receiveShadows = false;
                hr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; hr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                hg.SetActive(false);
                w.halo[i] = hg.transform;
            }
        }
        UdonSharpEditorUtility.CopyProxyToUdon(w);
        local.weapons = w;
        Debug.Log("[prophunt] weapons: " + melee.Count + " melee, selector " + (gun != null ? gun.name : "none"));
        return w;
    }

    // a flat torus in the xz plane (a halo: 0.26 m across the ring's middle, 0.036 m thick), kept in Weapons.asset
    static Mesh HaloMesh()
    {
        Mesh me = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(MESHES)) if (o is Mesh mm && mm.name == "Halo") me = mm;
        if (me == null)
        {
            me = new Mesh { name = "Halo" };
            if (AssetDatabase.LoadAssetAtPath<Mesh>(MESHES) == null) AssetDatabase.CreateAsset(me, MESHES);
            else AssetDatabase.AddObjectToAsset(me, MESHES);
        }
        const int SEG = 32, SIDES = 8;
        const float R = 0.13f, r = 0.018f;
        var verts = new Vector3[SEG * SIDES]; var norms = new Vector3[SEG * SIDES];
        var tris = new int[SEG * SIDES * 6];
        int t = 0;
        for (int i = 0; i < SEG; i++)
        {
            float a = i * Mathf.PI * 2f / SEG;
            Vector3 out1 = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            for (int j = 0; j < SIDES; j++)
            {
                float b = j * Mathf.PI * 2f / SIDES;
                Vector3 nrm = out1 * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                verts[i * SIDES + j] = out1 * R + nrm * r;
                norms[i * SIDES + j] = nrm;
                int i2 = (i + 1) % SEG, j2 = (j + 1) % SIDES;
                int v00 = i * SIDES + j, v10 = i2 * SIDES + j, v01 = i * SIDES + j2, v11 = i2 * SIDES + j2;
                tris[t++] = v00; tris[t++] = v01; tris[t++] = v10;
                tris[t++] = v10; tris[t++] = v01; tris[t++] = v11;
            }
        }
        me.Clear();
        me.vertices = verts; me.normals = norms; me.triangles = tris;
        me.RecalculateBounds();
        EditorUtility.SetDirty(me);
        AssetDatabase.SaveAssets();
        return me;
    }

    static Transform GunModel(Transform parent, Mesh mesh, Material mat)
    {
        var go = new GameObject("Model");
        go.transform.SetParent(parent, false);
        go.layer = 5;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        return go.transform;
    }

    static ParticleSystem Fx(Transform parent, string name, Material mat, Vector2 life, Vector2 speed, Vector2 size, float radius, float gravity,
        Color c0, Color c1, bool grow, bool spin)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
        main.gravityModifier = gravity;
        main.maxParticles = 400;
        if (spin) main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
        var col = ps.colorOverLifetime; col.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = fade;
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, grow ? AnimationCurve.Linear(0f, 0.7f, 1f, 1.8f) : AnimationCurve.Linear(0f, 1f, 1f, 0.5f));
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.dampen = 0.08f; lim.limit = 100f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }
}
