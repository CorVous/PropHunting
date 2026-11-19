using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// The weapon store (Cor, 2026-11-19; mock-up https://claude.ai/artifact/3mxFCbw1ypkM12yCjgjDtT):
//   the weapon wall   the east wall: two paternoster shelves (PHShelf), guns on the north half, melee on the south,
//                     every weapon of the catalog, floor to ceiling out of black holes, a floating panel with
//                     up / down and the coins following the player
//   the dialog        the buy card / trophy note over a clicked weapon (PHStoreDialog)
//   the Trophies board  the west wall, left of the score pad (PHTrophyBoard)
//   PHStore           the catalog, the trophies' texts, the player's coins and trophies
// Data: Tools/weapon_catalog.json (the melee work's), Tools/store_prices.json, Assets/Udon/Text/strings_store.tsv
// Tools/store_free.json and Assets/Lobby/WeaponIcons.png (Tools/make_store.py makes the last three). Models: the sub-assets of
// Assets/Udon/Generated/Weapons.asset (WeaponBuilder), named by id with ':' -> '_'; a weapon not converted yet
// shows only its tag. Call after the weapons are built (PHWeapons' lists give the picks' numbers).
public static partial class LobbyBuilder
{
    [System.Serializable] class StoreCatalog { public string note; public StoreWeapon[] weapons; }
    [System.Serializable] class StoreWeapon { public string id, kind, category, pack, model, thumb; public int sheet; }

    // the east wall (Cor): an alcove 1 m deep behind an opening from 0.5 to 5.85 m up (WorldSceneBuilder); the
    // shelves stand in it, flat against its back, five showing, sixteen weapons wide
    public const float Alcove = 1.0f, OpeningLow = 0.5f, OpeningHigh = 5.85f, OpeningHalf = 9.4f;
    const float SlotW = 0.55f, RowH = 1.17f, BaseY = 0.55f, Depth = 0.8f, Ceiling = 7f;
    const int Shown = 5;
    const int Cols = 16;

    public static PHStore BuildStore(Transform lobby, PHGame game, PHAudio sound, float roomW, float roomD)
    {
        EnsureAssets();
        string proj = System.IO.Path.GetDirectoryName(Application.dataPath);
        string catPath = System.IO.Path.Combine(proj, "Tools", "weapon_catalog.json");
        if (!System.IO.File.Exists(catPath)) { Debug.LogWarning("[store] no Tools/weapon_catalog.json: no weapon wall"); return null; }
        var cat = JsonUtility.FromJson<StoreCatalog>(System.IO.File.ReadAllText(catPath)).weapons;
        var prices = new Dictionary<string, int>();
        string pp = System.IO.Path.Combine(proj, "Tools", "store_prices.json");
        if (System.IO.File.Exists(pp))
            foreach (Match m in Regex.Matches(System.IO.File.ReadAllText(pp), "\"([^\"]+)\"\\s*:\\s*(\\d+)")) prices[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        // the free weapons (Cor: 10 cheap shop weapons a side, picked once by make_store.py): owned by everyone
        var free = new HashSet<string>();
        string fp = System.IO.Path.Combine(proj, "Tools", "store_free.json");
        if (System.IO.File.Exists(fp))
            foreach (Match m in Regex.Matches(System.IO.File.ReadAllText(fp), "\"([^\"]+)\"")) free.Add(m.Groups[1].Value);
        var text = ReadTsv(System.IO.Path.Combine(Application.dataPath, "Udon/Text/strings_store.tsv"));

        // the models, by name
        var meshByName = new Dictionary<string, Mesh>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Udon/Generated/Weapons.asset"))
            if (o is Mesh) meshByName[o.name] = (Mesh)o;
        // the picks' numbers: positions in PHWeapons' lists (by reflection: that script is the melee work's)
        string[] gameMelee = null, gameGun = null;
        foreach (var u in Object.FindObjectsOfType<UdonSharp.UdonSharpBehaviour>(true))
        {
            if (u.GetType().Name != "PHWeapons") continue;
            var f1 = u.GetType().GetField("meleeId"); var f2 = u.GetType().GetField("gunId");
            if (f1 != null) gameMelee = f1.GetValue(u) as string[];
            if (f2 != null) gameGun = f2.GetValue(u) as string[];
        }

        int n = cat.Length;
        var root = new GameObject("Store").transform;
        root.SetParent(lobby, false);
        var store = root.gameObject.AddUdonSharpComponent<PHStore>();
        store.game = game; store.sound = sound;
        store.wId = new string[n]; store.wKind = new int[n]; store.wCat = new int[n]; store.wPrice = new int[n];
        store.wTrophy = new int[n]; store.wGameIdx = new int[n];
        for (int i = 0; i < n; i++)
        {
            var w = cat[i];
            store.wId[i] = w.id;
            store.wKind[i] = w.kind == "gun" ? 1 : 0;
            store.wCat[i] = w.category == "starter" ? PHStore.CAT_STARTER : w.category == "cool" ? PHStore.CAT_COOL : w.category == "goofy" ? PHStore.CAT_GOOFY : PHStore.CAT_SHOP;
            if (free.Contains(w.id)) store.wCat[i] = PHStore.CAT_STARTER;
            int price; store.wPrice[i] = prices.TryGetValue(w.id, out price) ? price : 200;
            store.wTrophy[i] = -1;
            string[] list = store.wKind[i] == 0 ? gameMelee : gameGun;
            store.wGameIdx[i] = list != null ? System.Array.IndexOf(list, w.id) : -1;
        }

        // the trophies, in strings_store.tsv's order; each one's weapon and rounds-to-earn range from
        // Tools/store_trophies.json (make_store.py: the hardest trophy gives the most interesting weapon)
        var tw = new Dictionary<string, string>(); var tlo = new Dictionary<string, int>(); var thi = new Dictionary<string, int>();
        string tp = System.IO.Path.Combine(proj, "Tools", "store_trophies.json");
        if (System.IO.File.Exists(tp))
            foreach (Match m in Regex.Matches(System.IO.File.ReadAllText(tp), "\"key\":\\s*\"([^\"]+)\",\\s*\"weapon\":\\s*\"([^\"]+)\",\\s*\"lo\":\\s*(\\d+),\\s*\"hi\":\\s*(\\d+)"))
            { tw[m.Groups[1].Value] = m.Groups[2].Value; tlo[m.Groups[1].Value] = int.Parse(m.Groups[3].Value); thi[m.Groups[1].Value] = int.Parse(m.Groups[4].Value); }
        var keys = new List<string>();
        foreach (var k in text.Keys) if (k.StartsWith("trophy.") && k.EndsWith(".name")) keys.Add(k.Substring(7, k.Length - 12));
        int T = PHStore.TROPHIES, H = PHStore.HUNTER_TROPHIES;
        if (keys.Count != T) Debug.LogError("[store] strings_store.tsv has " + keys.Count + " trophies, not " + T);
        store.tKey = new string[T]; store.tName = new string[T]; store.tGoal = new string[T];
        store.tLo = new int[T]; store.tHi = new int[T]; store.tWeapon = new int[T];
        for (int t = 0; t < T && t < keys.Count; t++)
        {
            store.tKey[t] = keys[t];
            store.tName[t] = Get(text, "trophy." + keys[t] + ".name");
            store.tGoal[t] = Get(text, "trophy." + keys[t] + ".goal");
            string wid; store.tWeapon[t] = tw.TryGetValue(keys[t], out wid) ? System.Array.IndexOf(store.wId, wid) : -1;
            if (store.tWeapon[t] < 0) Debug.LogError("[store] trophy " + keys[t] + " has no weapon (store_trophies.json)");
            else store.wTrophy[store.tWeapon[t]] = t;
            int lo, hi; store.tLo[t] = tlo.TryGetValue(keys[t], out lo) ? lo : 1; store.tHi[t] = thi.TryGetValue(keys[t], out hi) ? hi : 1;
        }

        // the wall: two halves on the east wall (x = roomW / 2), facing west into the room
        float hz = 0.3f + Cols * SlotW / 2f;
        var melee = BuildShelf(root, store, 0, cat, meshByName, new Vector3(roomW / 2f + Alcove, 0f, -hz));
        var guns = BuildShelf(root, store, 1, cat, meshByName, new Vector3(roomW / 2f + Alcove, 0f, hz));
        store.shelfMelee = melee; store.shelfGun = guns;
        BuildOpeningTrim(root, roomW);
        store.dialog = BuildStoreDialog(root, store, text);
        store.board = BuildTrophyBoard(lobby, store, text, new Vector3(-roomW / 2f + 0.06f, 2.75f, -5.2f));   // west wall, left of the score pad (Cor)
        UdonSharpEditorUtility.CopyProxyToUdon(store);
        int conv = 0; foreach (var w in cat) if (meshByName.ContainsKey(w.id.Replace(':', '_'))) conv++;
        Debug.Log("[store] " + n + " weapons (" + conv + " with models), " + T + " trophies; picks numbered " + (gameMelee != null ? "from PHWeapons" : "later (no PHWeapons in the scene)"));
        return store;
    }

    // rounds to earn each trophy, a rough range for an 8-person lobby (make_store.py's list, same order)
    static readonly int[,] TrophyRounds = {
        {1,8},{4,20},{20,60},{30,100},{20,80},{30,120},{15,40},{200,400},{5,25},{2,10},{60,100},{10,40},{150,250},
        {5,25},{10,50},{150,300},{1,8},{1,8},{30,150},{40,200},{80,160},{1,2},
        {1,4},{2,8},{10,40},{20,35},{110,160},{30,100},{3,10},{20,80},{30,120},{25,40},{5,20},{60,80},{5,20},
        {20,80},{15,60},{5,20},{1,5},{15,25},{3,10},{1,40},{30,60},{5,20} };

    static Dictionary<string, string> ReadTsv(string path)
    {
        var d = new Dictionary<string, string>();
        if (!System.IO.File.Exists(path)) { Debug.LogWarning("[store] no " + path); return d; }
        var lines = System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8);
        for (int i = 1; i < lines.Length; i++)
        {
            var c = lines[i].Split('\t');
            if (c.Length >= 2 && c[0].Length > 0) d[c[0]] = c[1];
        }
        return d;
    }

    static string Get(Dictionary<string, string> d, string k) { string s; return d.TryGetValue(k, out s) ? s : k; }

    // ------------------------------------------------------------------ one half of the weapon wall
    static PHShelf BuildShelf(Transform parent, PHStore store, int kind, StoreWeapon[] cat, Dictionary<string, Mesh> meshes, Vector3 at)
    {
        var go = new GameObject(kind == 0 ? "ShelfMelee" : "ShelfGuns");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);   // +z into the room (west), +x north
        var t = go.transform;
        var shelf = go.AddUdonSharpComponent<PHShelf>();
        shelf.store = store; shelf.kind = kind; shelf.baseY = BaseY; shelf.rowH = RowH; shelf.cols = Cols;

        // the order: the free ones, the trophy weapons (quickest trophy first), then the shop from cheap to dear
        var order = new List<int>();
        var part = new List<int>();
        for (int i = 0; i < cat.Length; i++) if (store.wKind[i] == kind && store.wCat[i] == PHStore.CAT_STARTER) part.Add(i);
        order.AddRange(part); part.Clear();
        for (int i = 0; i < cat.Length; i++) if (store.wKind[i] == kind && store.wCat[i] != PHStore.CAT_STARTER && store.wTrophy[i] >= 0) part.Add(i);
        part.Sort((a, b) => { int ta = store.wTrophy[a], tb = store.wTrophy[b]; int da = store.tLo[ta] + store.tHi[ta], db = store.tLo[tb] + store.tHi[tb]; return da != db ? da.CompareTo(db) : ta.CompareTo(tb); });
        order.AddRange(part); part.Clear();
        for (int i = 0; i < cat.Length; i++) if (store.wKind[i] == kind && store.wCat[i] != PHStore.CAT_STARTER && store.wTrophy[i] < 0) part.Add(i);
        part.Sort((a, b) => store.wPrice[a] != store.wPrice[b] ? store.wPrice[a].CompareTo(store.wPrice[b]) : a.CompareTo(b));
        order.AddRange(part);
        shelf.order = order.ToArray();

        // each model fitted into its place: melee stands up (its length +Z up), guns lie along the shelf (barrel to
        // the player's left), on the shelf board, centred
        shelf.itemRot = kind == 0 ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.Euler(0f, 90f, 0f);
        Vector3 fit = kind == 0 ? new Vector3(0.44f, 0.54f, 0.36f) : new Vector3(0.5f, 0.34f, 0.36f);
        int n = cat.Length;
        shelf.meshes = new Mesh[n]; shelf.itemPos = new Vector3[n]; shelf.itemScale = new float[n];
        foreach (int i in order)
        {
            Mesh m; meshes.TryGetValue(cat[i].id.Replace(':', '_'), out m);
            shelf.meshes[i] = m;
            if (m == null) continue;
            var b = m.bounds;
            Vector3 mn = Vector3.one * float.MaxValue, mx = -mn;
            for (int k = 0; k < 8; k++)
            {
                var p = shelf.itemRot * new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
            }
            Vector3 size = mx - mn, centre = (mx + mn) / 2f;
            float s = Mathf.Min(fit.x / Mathf.Max(size.x, 1e-4f), Mathf.Min(fit.y / Mathf.Max(size.y, 1e-4f), fit.z / Mathf.Max(size.z, 1e-4f)));
            shelf.itemScale[i] = s;
            shelf.itemPos[i] = new Vector3(-centre.x * s, -mn.y * s + 0.01f, -centre.z * s);
        }

        // no cabinet (Cor): the shelves rise out of a black hole in the floor and go into one at the ceiling, two thin
        // dark rods at the ends
        Material wood = Mat("StoreWood", C("b07848")), woodDark = Mat("StoreWoodDark", C("7d5232")), back = Mat("StoreBack", C("efe2c2"));
        Material cream = Mat("LobbyCream", C("f3e7c9")), felt = Mat("StoreFelt", C("173d2c")), red = Mat("StoreRed", C("d8473a"));
        Material yellow = Mat("StoreYellow", C("f2c45a")), coral = Mat("StoreCoral", C("f19a8a")), gold = Mat("StoreGold", C("e2b13c"));
        float halfW = Cols * SlotW / 2f;
        var voidMat = Mat("StoreVoid", C("030303"));
        Slab(t, "FloorHole", new Vector3(0f, 0.006f, Depth / 2f + 0.02f), new Vector3(2 * halfW + 0.16f, 0.012f, Depth + 0.14f), voidMat);
        Slab(t, "RodL", new Vector3(halfW + 0.04f, Ceiling / 2f, Depth - 0.05f), new Vector3(0.035f, Ceiling, 0.035f), woodDark);
        Slab(t, "RodR", new Vector3(-halfW - 0.04f, Ceiling / 2f, Depth - 0.05f), new Vector3(0.035f, Ceiling, 0.035f), woodDark);

        // the shelf rows (one more than show, out of sight) with ten places each
        var mat = ShelfMat();
        int roots = Shown + 1;
        shelf.shown = Shown;
        shelf.rows = new Transform[roots];
        int slots = roots * Cols;
        shelf.items = new Transform[slots]; shelf.itemMf = new MeshFilter[slots]; shelf.itemMr = new MeshRenderer[slots];
        shelf.tagShop = new GameObject[slots]; shelf.tagCool = new GameObject[slots]; shelf.tagGoofy = new GameObject[slots];
        shelf.sel = new GameObject[slots]; shelf.price = new TextMeshPro[slots];
        for (int r = 0; r < roots; r++)
        {
            var row = new GameObject("Row" + r).transform;
            row.SetParent(t, false);
            row.localPosition = new Vector3(0f, BaseY + r * RowH, 0f);
            shelf.rows[r] = row;
            var board = new GameObject("Board");   // rounded off (Cor)
            board.transform.SetParent(row, false);
            board.transform.localPosition = new Vector3(0f, -0.045f, (Depth - 0.12f) / 2f);   // its back 0.12 m into the wall: flat there (Cor)
            board.AddComponent<MeshFilter>().sharedMesh = RoundedBoard(2 * halfW + 0.06f, Depth + 0.12f, 0.045f, 0.1f);
            var br = board.AddComponent<MeshRenderer>(); br.sharedMaterial = wood;
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int c = 0; c < Cols; c++)
            {
                int k = r * Cols + c;
                var place = new GameObject("Place" + c).transform;
                place.SetParent(row, false);
                place.localPosition = new Vector3((Cols / 2f - 0.5f - c) * SlotW, 0f, Depth / 2f);
                var col = place.gameObject.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.28f, 0f); col.size = new Vector3(SlotW - 0.04f, 0.56f, Depth - 0.08f);
                var ps = place.gameObject.AddUdonSharpComponent<PHShelfSlot>();
                ps.shelf = shelf; ps.index = k;
                UdonSharpEditorUtility.CopyProxyToUdon(ps);
                var pu = UdonSharpEditorUtility.GetBackingUdonBehaviour(ps);
                pu.proximity = 8f; pu.interactText = "";   // (the high shelves: clicked from the floor)

                var item = new GameObject("Item");
                item.transform.SetParent(place, false);
                shelf.items[k] = item.transform;
                shelf.itemMf[k] = item.AddComponent<MeshFilter>();
                shelf.itemMr[k] = item.AddComponent<MeshRenderer>();
                shelf.itemMr[k].sharedMaterial = mat;
                shelf.itemMr[k].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // the tags on the shelf's lip: a price (shop), a star (trophy: gold cool, coral goofy), the red
                // strip under the weapon being carried
                float lipZ = Depth / 2f + 0.005f;
                var shop = new GameObject("PriceTag").transform; shop.SetParent(place, false);
                Slab(shop, "Tag", new Vector3(0f, -0.03f, lipZ), new Vector3(0.22f, 0.075f, 0.012f), felt);
                Disc(shop, "Coin", new Vector3(-0.075f, -0.03f, lipZ + 0.008f), 0.05f, gold);
                shop.GetChild(1).localRotation = Quaternion.Euler(90f, 0f, 0f);
                var pt = WorldText(shop, "Price", new Vector3(0.025f, -0.03f, lipZ + 0.008f), Quaternion.Euler(0f, 180f, 0f), "200", 0.6f, C("f6d773"), new Vector2(0.16f, 0.07f));
                pt.font = future;
                shelf.tagShop[k] = shop.gameObject; shelf.price[k] = pt;
                shelf.tagCool[k] = StarTag(place, "CoolTag", lipZ, yellow);
                shelf.tagGoofy[k] = StarTag(place, "GoofyTag", lipZ, coral);
                var selGo = new GameObject("Carried").transform; selGo.SetParent(place, false);
                Slab(selGo, "Strip", new Vector3(0f, -0.068f, lipZ + 0.004f), new Vector3(SlotW - 0.06f, 0.03f, 0.012f), red);
                shelf.sel[k] = selGo.gameObject;
                shop.gameObject.SetActive(false); shelf.tagCool[k].SetActive(false); shelf.tagGoofy[k].SetActive(false); selGo.gameObject.SetActive(false);
            }
        }

        // the floating panel (Cor: round buttons, slanted towards the player, no rail), 1 m out at hip height: up /
        // down, and the coins. Its own +z faces the player; the plate tilts its top towards them.
        float cz = Alcove + 0.9f;
        var cart = new GameObject("Panel").transform;
        cart.SetParent(t, false); cart.localPosition = new Vector3(0f, 0f, cz);
        shelf.cart = cart; shelf.cartMin = -halfW + 0.5f; shelf.cartMax = halfW - 0.5f; shelf.cartZ = cz;
        var tilt = new GameObject("Tilt").transform;
        tilt.SetParent(cart, false); tilt.localPosition = new Vector3(0f, 1.05f, 0f); tilt.localRotation = Quaternion.Euler(35f, 0f, 0f);
        Oval(tilt, "Rim", new Vector3(0f, -0.015f, 0f), new Vector3(1.06f, 0.04f, 0.58f), woodDark);
        Oval(tilt, "Plate", Vector3.zero, new Vector3(1.0f, 0.04f, 0.52f), felt);
        RoundButton(tilt, shelf, "Up", new Vector3(0.21f, 0.025f, -0.06f), true);
        RoundButton(tilt, shelf, "Down", new Vector3(-0.21f, 0.025f, -0.06f), false);
        Disc(tilt, "Coin", new Vector3(0.15f, 0.023f, 0.16f), 0.09f, gold);
        var coins = WorldText(tilt, "Coins", new Vector3(-0.15f, 0.025f, 0.16f), Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 180f, 0f), "0", 1.1f, C("f6d773"), new Vector2(0.32f, 0.12f));
        coins.font = future; coins.alignment = TextAlignmentOptions.Left;
        shelf.cartCoins = coins;

        UdonSharpEditorUtility.CopyProxyToUdon(shelf);
        return shelf;
    }

    static GameObject StarTag(Transform place, string name, float lipZ, Material m)
    {
        var tag = new GameObject(name).transform; tag.SetParent(place, false);
        Slab(tag, "Tag", new Vector3(0f, -0.03f, lipZ), new Vector3(0.09f, 0.075f, 0.012f), m);
        var s = WorldText(tag, "Star", new Vector3(0f, -0.04f, lipZ + 0.008f), Quaternion.Euler(0f, 180f, 0f), "*", 1.2f, C("1e190f"), new Vector2(0.08f, 0.08f));
        s.font = future;
        return tag.gameObject;
    }

    // a shelf board with rounded corners and a rounded front and back edge: w x d, h thick (bottom at y = 0), r the
    // corners' radius
    static Mesh RoundedBoard(float w, float d, float h, float r)
    {
        string path = "Assets/Udon/Generated/StoreBoard.asset";
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool fresh = m == null;
        if (fresh) m = new Mesh { name = "StoreBoard" };
        // the outline: a rounded rectangle, 6 steps per corner, counter-clockwise from above
        var ring = new List<Vector2>();
        float hx = w / 2f - r, hz = d / 2f - r;
        Vector2[] centres = { new Vector2(hx, hz), new Vector2(-hx, hz), new Vector2(-hx, -hz), new Vector2(hx, -hz) };
        for (int c = 0; c < 4; c++)
            for (int s = 0; s <= 6; s++)
            {
                float a = (c * 90f + s * 15f) * Mathf.Deg2Rad;
                ring.Add(centres[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        // the side is a half round (the edge rounded off, top to bottom), 4 steps
        const int E = 4;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
        int N = ring.Count;
        for (int e = 0; e <= E; e++)
        {
            float a = -90f + 180f * e / E, ca = Mathf.Cos(a * Mathf.Deg2Rad), sa = Mathf.Sin(a * Mathf.Deg2Rad);
            for (int i = 0; i < N; i++)
            {
                Vector2 p = ring[i], o = p.normalized;
                // the outward direction of this outline point (its corner's centre to it, or straight out on a side)
                Vector2 cc = new Vector2(Mathf.Clamp(p.x, -hx, hx), Mathf.Clamp(p.y, -hz, hz));
                Vector2 dir = (p - cc).sqrMagnitude > 1e-8f ? (p - cc).normalized : o;
                float bulge = h / 2f * ca;
                v.Add(new Vector3(p.x - dir.x * h / 2f + dir.x * bulge, h / 2f + h / 2f * sa, p.y - dir.y * h / 2f + dir.y * bulge));
                n.Add(new Vector3(dir.x * ca, sa, dir.y * ca));
            }
        }
        for (int e = 0; e < E; e++)
            for (int i = 0; i < N; i++)
            {
                int a0 = e * N + i, a1 = e * N + (i + 1) % N, b0 = a0 + N, b1 = a1 + N;
                tri.Add(a0); tri.Add(b0); tri.Add(a1); tri.Add(a1); tri.Add(b0); tri.Add(b1);
            }
        // the top and bottom: fans from a middle point
        int top = v.Count; v.Add(new Vector3(0f, h, 0f)); n.Add(Vector3.up);
        int tr = v.Count; for (int i = 0; i < N; i++) { v.Add(v[E * N + i]); n.Add(Vector3.up); }
        for (int i = 0; i < N; i++) { tri.Add(top); tri.Add(tr + (i + 1) % N); tri.Add(tr + i); }
        int bot = v.Count; v.Add(Vector3.zero); n.Add(Vector3.down);
        int brr = v.Count; for (int i = 0; i < N; i++) { v.Add(v[i]); n.Add(Vector3.down); }
        for (int i = 0; i < N; i++) { tri.Add(bot); tri.Add(brr + i); tri.Add(brr + (i + 1) % N); }
        m.Clear(); m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds();
        if (fresh) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
        return m;
    }

    // the dressing across the weapon wall's opening, from the same KayKit Board Game Bits (CC0) as the room's giant
    // pieces (Cor: the flat art did not fit): a cream ledge on the bottom strip with a parade of small game pieces
    // in the board's four colours, and pennant flags leaning out from the band over the top
    static void BuildOpeningTrim(Transform root, float roomW)
    {
        float face = roomW / 2f, w = 2f * OpeningHalf;
        var cream = Mat("LobbyCream", C("f3e7c9"));
        Box(root, "Ledge", new Vector3(face - 0.11f, OpeningLow - 0.03f, 0f), new Vector3(0.22f, 0.06f, w), cream);
        Slab(root, "LedgeTrim", new Vector3(face - 0.222f, OpeningLow - 0.05f, 0f), new Vector3(0.008f, 0.03f, w), Mat("LobbyWallRule", C("c9a46a")));
        string[] colours = { "blue", "yellow", "green", "red" };
        int n = Mathf.FloorToInt(w / 0.62f);
        for (int i = 0; i < n; i++)
        {
            float z = -w / 2f + 0.31f + i * (w - 0.62f) / (n - 1);
            string c = colours[i % 4];
            string model; float size;
            switch ((i / 4) % 4)
            {
                case 0: model = "pawn_A_" + c; size = 0.3f; break;
                case 1: model = "meeple_" + c; size = 0.26f; break;
                case 2: model = "D6_A_" + c; size = 0.16f; break;
                default: model = c == "yellow" ? "coin_5_gold" : "pawn_A_" + c; size = c == "yellow" ? 0.14f : 0.3f; break;
            }
            var go = Piece(root, "board-game-bits/" + model, new Vector3(face - 0.11f, OpeningLow, z), size, 270f + (i % 3 - 1) * 12f, false);
            if (model.StartsWith("coin")) go.transform.RotateAround(root.TransformPoint(new Vector3(face - 0.11f, OpeningLow, z)), root.TransformDirection(Vector3.forward), 80f);
        }
        // the pennants: one every 3.1 m, poles out of the band's lower edge, leaning into the room
        for (int i = 0; i < 6; i++)
        {
            float z = -w / 2f + 1.55f + i * (w - 3.1f) / 5f;
            var at = new Vector3(face - 0.02f, 5.86f, z);
            var go = Piece(root, "board-game-bits/flag_A_" + colours[i % 4], at, 0.7f, 90f, false);
            go.transform.RotateAround(root.TransformPoint(at), root.TransformDirection(Vector3.forward), 55f);   // the top out over the room
        }
        // no shadows from the dressing (the flags' fell across the shelves)
        foreach (Transform c in root)
            if (c.name.StartsWith("pawn") || c.name.StartsWith("meeple") || c.name.StartsWith("D6") || c.name.StartsWith("coin") || c.name.StartsWith("flag"))
                foreach (var r in c.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // a flattened cylinder (an oval plate)
    static void Oval(Transform root, string name, Vector3 pos, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos; go.transform.localScale = new Vector3(size.x, size.y / 2f, size.z);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    // a round button on the floating panel: a pale cap with an arrow, pressed like any world button (PHButton)
    static void RoundButton(Transform panel, PHShelf shelf, string action, Vector3 at, bool up)
    {
        var key = new GameObject(action).transform;
        key.SetParent(panel, false); key.localPosition = at;
        Oval(key, "Cap", new Vector3(0f, 0.03f, 0f), new Vector3(0.25f, 0.06f, 0.25f), Mat("StoreKey", C("e1cfa5")));
        var arrow = new GameObject("Arrow");
        arrow.transform.SetParent(key, false);
        arrow.transform.localPosition = new Vector3(0f, 0.062f, 0f);
        arrow.transform.localRotation = Quaternion.Euler(0f, up ? 180f : 0f, 0f);   // (the panel's -z is away from the player: up)
        arrow.transform.localScale = Vector3.one * 1.15f;
        arrow.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
        arrow.AddComponent<MeshRenderer>().sharedMaterial = Mat("StoreInk", C("1e190f"));
        var btn = key.gameObject.AddUdonSharpComponent<PHButton>();
        btn.target = shelf; btn.action = action;
        UdonSharpEditorUtility.CopyProxyToUdon(btn);
        var u = UdonSharpEditorUtility.GetBackingUdonBehaviour(btn);
        u.proximity = 3f; u.interactText = up ? "Up" : "Down";
        var col = key.gameObject.AddComponent<SphereCollider>(); col.radius = 0.14f; col.center = new Vector3(0f, 0.03f, 0f);
        col.isTrigger = true;   // the panel follows the player: nothing on it may push them
        // pressed by hand in VR (Cor): the player's own fingertips touching the cap (PHButton.OnContactEnter)
        var rc = key.gameObject.AddComponent<VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver>();
        rc.shapeType = VRC.Dynamics.ContactBase.ShapeType.Sphere;
        rc.radius = 0.11f; rc.position = new Vector3(0f, 0.05f, 0f);
        rc.allowSelf = true; rc.allowOthers = false; rc.localOnly = true;
        rc.collisionTags = new System.Collections.Generic.List<string> { "Finger", "FingerIndex" };
        rc.receiverType = VRC.Dynamics.ContactReceiver.ReceiverType.OnEnter;
    }

    // an up / down button on the cart: a pale key with an arrow, pressed like any world button (PHButton)
    static void CartButton(Transform cart, PHShelf shelf, string action, Vector3 at, bool up)
    {
        var key = new GameObject(action).transform;
        key.SetParent(cart, false); key.localPosition = at;
        Slab(key, "Key", Vector3.zero, new Vector3(0.3f, 0.1f, 0.3f), Mat("StoreKey", C("e1cfa5")));
        var arrow = new GameObject("Arrow");
        arrow.transform.SetParent(key, false);
        arrow.transform.localPosition = new Vector3(0f, 0.052f, 0f);
        arrow.transform.localRotation = Quaternion.Euler(0f, up ? 0f : 180f, 0f);
        arrow.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
        arrow.AddComponent<MeshRenderer>().sharedMaterial = Mat("StoreInk", C("1e190f"));
        var btn = key.gameObject.AddUdonSharpComponent<PHButton>();
        btn.target = shelf; btn.action = action;
        UdonSharpEditorUtility.CopyProxyToUdon(btn);
        var u = UdonSharpEditorUtility.GetBackingUdonBehaviour(btn);
        u.proximity = 3f; u.interactText = up ? "Up" : "Down";
        var col = key.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(0.3f, 0.12f, 0.3f);
        col.isTrigger = true;   // the cart follows the player: nothing on it may push them
    }

    // a flat triangle pointing +z (into the room, which reads as "up" on the cart's top from where the player stands)
    static Mesh ArrowMesh()
    {
        const string path = "Assets/Udon/Generated/StoreArrow.asset";
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m != null) return m;
        m = new Mesh { name = "StoreArrow" };
        m.vertices = new[] { new Vector3(0f, 0f, 0.09f), new Vector3(0.08f, 0f, -0.06f), new Vector3(-0.08f, 0f, -0.06f) };
        m.triangles = new[] { 0, 1, 2 };
        m.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material ShelfMat()
    {
        const string path = "Assets/Udon/Generated/ShelfWeapon.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("PropHunt/ShelfWeapon");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh; m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ the buy card / trophy note
    static PHStoreDialog BuildStoreDialog(Transform parent, PHStore store, Dictionary<string, string> text)
    {
        var holder = new GameObject("StoreDialog").transform;
        holder.SetParent(parent, false);
        var dlg = holder.gameObject.AddUdonSharpComponent<PHStoreDialog>();
        dlg.store = store;
        dlg.roundsFormat = Get(text, "store.rounds");
        var cv = Canvas(holder, "Card", Vector3.zero, Quaternion.identity, new Vector2(380, 200), 0.0025f, true);
        dlg.root = cv.gameObject;
        Img(cv, "Shadow", new Vector2(0, -6), new Vector2(380, 200), sRound, C("0e2a1e"), 2f);
        Img(cv, "Card", Vector2.zero, new Vector2(380, 200), sRound, C("173d2c"), 2f);
        StoreButton(cv, dlg, "Close", new Vector2(160, 70), new Vector2(44, 44), C("e1cfa5"), "X", C("1e190f"));

        var buy = Rect(cv, "Buy", Vector2.zero, new Vector2(380, 200)).gameObject;
        dlg.buyGroup = buy;
        Img(buy.transform, "IconBack", new Vector2(-100, 0), new Vector2(150, 150), sRound, C("f3e7c9"), 2f);
        var ri = Rect(buy.transform, "Icon", new Vector2(-100, 0), new Vector2(140, 140)).gameObject.AddComponent<RawImage>();
        ri.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Lobby/WeaponIcons.png"); ri.raycastTarget = false;
        dlg.icon = ri;
        var bb = StoreButton(buy.transform, dlg, "BuyPress", new Vector2(75, -20), new Vector2(170, 76), C("d8473a"), "", Color.white);
        dlg.buyButton = bb;
        var coinImg = Img(bb.transform, "Coin", new Vector2(-50, 2), new Vector2(34, 34), sRound, C("f2c45a"), 4f);
        dlg.priceText = Txt(bb.transform, "Price", new Vector2(18, 3), new Vector2(100, 50), "200", future, 34, Color.white, TextAlignmentOptions.Center);

        var tro = Rect(cv, "Trophy", Vector2.zero, new Vector2(380, 200)).gameObject;
        dlg.trophyGroup = tro;
        dlg.nameText = Txt(tro.transform, "Name", new Vector2(-20, 58), new Vector2(300, 36), "", future, 22, C("f6d773"), TextAlignmentOptions.Left);
        dlg.goalText = Txt(tro.transform, "Goal", new Vector2(-6, 0), new Vector2(330, 80), "", future, 18, Color.white, TextAlignmentOptions.TopLeft);
        dlg.goalText.enableWordWrapping = true;
        dlg.roundsText = Txt(tro.transform, "Rounds", new Vector2(-6, -70), new Vector2(330, 30), "", future, 16, C("f2c45a"), TextAlignmentOptions.Left);
        buy.SetActive(false); tro.SetActive(false);
        UdonSharpEditorUtility.CopyProxyToUdon(dlg);
        return dlg;
    }

    static Image StoreButton(Transform cv, UdonSharp.UdonSharpBehaviour target, string action, Vector2 at, Vector2 size, Color face, string label, Color ink)
    {
        var img = Img(cv, action, at, size, sRound, face, 3f);
        img.raycastTarget = true;
        var bt = img.gameObject.AddComponent<Button>();
        var nav = bt.navigation; nav.mode = Navigation.Mode.None; bt.navigation = nav; bt.targetGraphic = img;
        if (label.Length > 0) Txt(img.transform, "Text", new Vector2(0, 2), size, label, future, size.y * 0.5f, ink, TextAlignmentOptions.Center);
        var btn = img.gameObject.AddUdonSharpComponent<PHButton>();
        btn.target = target; btn.action = action;
        UdonSharpEditorUtility.CopyProxyToUdon(btn);
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(btn);
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(bt.onClick, udon.SendCustomEvent, nameof(PHButton.Press));
        return img;
    }

    // ------------------------------------------------------------------ the Trophies board (south wall)
    // 2900 x 1190 canvas units at 3.7 mm: 10.7 x 4.4 m, on the west wall. Two halves of 4 x 10 cards (39 used), a
    // count over each.
    const int BoardCols = 4;
    const float CardW = 330f, CardH = 100f, Gap = 10f, HalfW = BoardCols * CardW + (BoardCols - 1) * Gap;

    static PHTrophyBoard BuildTrophyBoard(Transform lobby, PHStore store, Dictionary<string, string> text, Vector3 at)
    {
        const float W = 2900f, Hh = 1190f;
        var cv = Canvas(lobby, "Trophies", at, Quaternion.Euler(0f, -90f, 0f), new Vector2(W, Hh), 0.0037f, false);
        var board = cv.gameObject.AddUdonSharpComponent<PHTrophyBoard>();
        board.store = store;
        board.progressFormat = Get(text, "store.progress");
        Img(cv, "Shadow", new Vector2(0, -8), new Vector2(W, Hh), sRound, C("173d2c"), 1.4f);
        Img(cv, "Edge", Vector2.zero, new Vector2(W, Hh), sRound, BoardEdge, 1.4f);
        Img(cv, "Board", Vector2.zero, new Vector2(W - 12, Hh - 12), sRound, C("f3e7c9"), 1.4f);

        float halfShift = HalfW + 60f;
        float x0 = -halfShift / 2f - HalfW / 2f + CardW / 2f;   // the left half's first column
        float y0 = Hh / 2f - 60f - CardH / 2f;                  // under the heading
        board.halfShift = halfShift;
        board.place = new Vector2[PHStore.HUNTER_TROPHIES];
        for (int i = 0; i < board.place.Length; i++) board.place[i] = new Vector2(x0 + (i % BoardCols) * (CardW + Gap), y0 - (i / BoardCols) * (CardH + Gap));
        for (int h = 0; h < 2; h++)
        {
            float cx = (h == 0 ? -1 : 1) * halfShift / 2f;
            Img(cv, h == 0 ? "HunterDot" : "HiderDot", new Vector2(cx - HalfW / 2f + 18, Hh / 2f - 32), new Vector2(30, 30), sRound, h == 0 ? C("d8473a") : C("7fb3e6"), 6f);
            Img(cv, "Rule", new Vector2(cx, Hh / 2f - 54), new Vector2(HalfW, 4), sDash, C("c9a46a"), 2f);
            var count = Txt(cv, h == 0 ? "HunterCount" : "HiderCount", new Vector2(cx + HalfW / 2f - 60, Hh / 2f - 32), new Vector2(120, 30), "0 / 39", future, 20, C("6b5d40"), TextAlignmentOptions.Right);
            if (h == 0) board.countHunter = count; else board.countHider = count;
        }

        int T = PHStore.TROPHIES;
        board.card = new RectTransform[T]; board.cardBg = new Image[T]; board.medal = new RawImage[T];
        board.tick = new GameObject[T]; board.bar = new GameObject[T]; board.rounds = new GameObject[T];
        board.barFill = new RectTransform[T]; board.progText = new TextMeshProUGUI[T];
        var icons = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Lobby/WeaponIcons.png");
        string roundsFmt = Get(text, "store.rounds");
        for (int t = 0; t < T; t++)
        {
            int slot = t % PHStore.HUNTER_TROPHIES, h = t / PHStore.HUNTER_TROPHIES;
            var cpos = board.place[slot] + new Vector2(h * halfShift, 0f);
            var card = Rect(cv, "Card " + store.tKey[t], cpos, new Vector2(CardW, CardH));
            board.card[t] = card;
            board.cardBg[t] = Img(card, "Back", Vector2.zero, new Vector2(CardW, CardH), sRound, C("e9dab6"), 3f);
            Img(card, "Medal", new Vector2(-CardW / 2 + 48, 0), new Vector2(76, 76), sRound, C("fff6e0"), 8f);
            var ri = Rect(card, "Icon", new Vector2(-CardW / 2 + 48, 0), new Vector2(66, 66)).gameObject.AddComponent<RawImage>();
            ri.texture = icons; ri.raycastTarget = false;
            int w = store.tWeapon[t];
            if (w >= 0) ri.uvRect = new Rect((w % PHStoreDialog.ICON_COLS) / (float)PHStoreDialog.ICON_COLS, 1f - (w / PHStoreDialog.ICON_COLS + 1) / (float)PHStoreDialog.ICON_ROWS, 1f / PHStoreDialog.ICON_COLS, 1f / PHStoreDialog.ICON_ROWS);
            board.medal[t] = ri;
            float tx = -CardW / 2 + 96, tw = CardW - 96 - 14;
            Txt(card, "Name", new Vector2(tx + tw / 2, 32), new Vector2(tw, 26), store.tName[t].ToUpperInvariant(), future, 16, C("1e190f"), TextAlignmentOptions.Left);
            var goal = Txt(card, "Goal", new Vector2(tx + tw / 2, 1), new Vector2(tw, 36), store.tGoal[t], future, 12.5f, C("6b5d40"), TextAlignmentOptions.TopLeft);
            goal.enableWordWrapping = true; goal.overflowMode = TextOverflowModes.Ellipsis;
            var bar = Rect(card, "Bar", new Vector2(tx + 50, -26), new Vector2(100, 8)).gameObject;
            Img(bar.transform, "Back", Vector2.zero, new Vector2(100, 8), sRound, C("d8c9a3"), 12f);
            var fill = Img(bar.transform, "Fill", new Vector2(-50, 0), new Vector2(100, 8), sRound, C("21573f"), 12f).rectTransform;
            fill.pivot = new Vector2(0f, 0.5f); fill.anchoredPosition = new Vector2(-50, 0);
            board.barFill[t] = fill;
            board.progText[t] = Txt(bar.transform, "Count", new Vector2(92, 0), new Vector2(70, 18), "", future, 11, C("6b5d40"), TextAlignmentOptions.Left);
            board.bar[t] = bar;
            string est = roundsFmt.Replace("{0}", store.tLo[t].ToString()).Replace("{1}", store.tHi[t].ToString());
            board.rounds[t] = Txt(card, "Rounds", new Vector2(tx + tw / 2, -40), new Vector2(tw, 18), est, future, 11, C("21573f"), TextAlignmentOptions.Left).gameObject;
            var tick = Img(card, "Tick", new Vector2(CardW / 2 - 20, CardH / 2 - 20), new Vector2(26, 26), sRound, C("21573f"), 10f);
            Img(tick.transform, "A", new Vector2(-4, -2), new Vector2(9, 3), null, Color.white).rectTransform.localRotation = Quaternion.Euler(0, 0, -45f);
            Img(tick.transform, "B", new Vector2(2, 1), new Vector2(15, 3), null, Color.white).rectTransform.localRotation = Quaternion.Euler(0, 0, 50f);
            board.tick[t] = tick.gameObject;
            tick.gameObject.SetActive(false); bar.SetActive(false);
        }
        UdonSharpEditorUtility.CopyProxyToUdon(board);
        return board;
    }
}
