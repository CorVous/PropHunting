using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// The LAST ROUND diorama in the ready area (Cor's group playtest, item 40; then "a 3D diorama style instead, showing
// what props people hid as for the most time, on a table in the play area"): the last round's map in miniature on a
// table (its ground, walls and scenery, the roofs off), every player's path over it, and each hider's disguise of
// the round, the one they were longest, standing where they stayed longest in it, with their name on a pin; a card
// on the table's edge lists who went how far and who was caught when.
// The map's meshes are the builder's own (shared, nothing copied); the hiders' props are copies of the map's prop
// prefabs, colliders off. Shown from the END reveal until the next map starts loading; blank before the first round.
// Nothing is synced: each client samples positions it already has (a hider's synced prop position and disguise, or
// the player while they walk as themselves; a hunter's avatar) once a second through the hide and the hunt.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHMiniMap : UdonSharpBehaviour
{
    public PHGame game;
    public MapBuilderUdon builder;
    public Transform model;              // the miniature's root on the table top (its scale set per map)
    public GameObject content;           // the miniature, the paths, the pins and the list (off when there is nothing)
    public MeshFilter dioGround, dioWalls;
    public MeshFilter[] dioScenery;      // the merged scenery tiles (fences, hedges, trees)
    public LineRenderer[] paths;         // one per slot, under model
    public Transform[] pins;             // one per slot: a pin over the hider's prop, its name on it
    public TextMeshPro[] pinLabel;
    public TextMeshProUGUI list, title, waiting;
    public float tableSize = 2.3f;       // the table top's usable side, metres
    public Material dioMat;              // PropHunt/Diorama on the walls: cut above _ClipY when the upper floors are off
    public Transform propRoot;           // the map's props in miniature (under model)
    public Material outlineMat;          // PropHunt/DioramaOutline: a hiding spot's prop outlined in the player's colour
    public TextMeshProUGUI upperLabel, soloLabel;   // the table's two buttons

    const int MAXP = 1440;               // four points a second: six minutes of hide and hunt
    const float STEP = 0.25f, MIN_MOVE = 0.3f;   // (Cor: a point a second cut corners through the walls)
    const int MAXD = 6;                  // disguises remembered per hider
    Vector3[] pts;                       // slot * MAXP + i
    int[] npts;
    float[] dist;
    int[] roleOf, playerOf, catches;
    string[] nameOf;
    float[] caughtAt;                    // seconds into the hunt, -1 survived
    // per hider and disguise (slot * MAXD + k): the def, its seconds, the longest stay in it and where
    int[] dDef; float[] dTime, dBestStill; Vector3[] dBestPos; float[] dBestYaw;
    float[] still; Vector3[] stillAt; int[] stillDef; float[] stillYaw;
    // every hiding spot (Cor: where they stood still a little while, in the prop they hid as): slot * MAXS + k
    const int MAXS = 10;
    const float STAY_MIN = 5f;
    int[] sDef, nStay, longestStay; Vector3[] sPos; float[] sYaw, sDur;
    [HideInInspector] public int buttonArg;   // PHButton: the slot + 1 of the name tag pressed
    float huntStart, nextSample;
    Vector3 origin;                      // the map's middle on the ground: the table's middle
    int recRound = -1, shownRound = -1, snapRound = -1, lastPhase = -1;
    float snapAt = -1f;
    bool hasData;
    GameObject[] shownOutline;
    MaterialPropertyBlock outlineBlock;
    GameObject[] shownProp;              // per slot * MAXD: a hider's disguise where they stayed a long while in it
    GameObject[] mapProp; bool[] mapUpper; int nMapProp, propNext = 99999;
    bool upperOn = true;
    int solo = -1;                       // the one player shown alone, -1 everyone
    const float LONG_STAY = 15f, UPPER_Y = 2.0f, CLIP_Y = 2.45f;   // (the cut under the slab between floors: the lowest walls stand 2.6 m)
    const int MAX_MAP_PROPS = 400;
    Color[] palette;

    void Start()
    {
        int n = paths.Length;
        pts = new Vector3[n * MAXP]; npts = new int[n]; dist = new float[n]; roleOf = new int[n]; playerOf = new int[n];
        nameOf = new string[n]; caughtAt = new float[n]; catches = new int[n];
        dDef = new int[n * MAXD]; dTime = new float[n * MAXD]; dBestStill = new float[n * MAXD]; dBestPos = new Vector3[n * MAXD]; dBestYaw = new float[n * MAXD];
        still = new float[n]; stillAt = new Vector3[n]; stillDef = new int[n]; stillYaw = new float[n];
        sDef = new int[n * MAXS]; nStay = new int[n]; longestStay = new int[n]; sPos = new Vector3[n * MAXS]; sYaw = new float[n * MAXS]; sDur = new float[n * MAXS];
        shownProp = new GameObject[n * MAXS]; shownOutline = new GameObject[n * MAXS];
        mapProp = new GameObject[MAX_MAP_PROPS]; mapUpper = new bool[MAX_MAP_PROPS];
        palette = new Color[] {
            new Color(0.95f, 0.45f, 0.35f), new Color(0.35f, 0.75f, 0.95f), new Color(0.98f, 0.8f, 0.3f), new Color(0.55f, 0.85f, 0.45f),
            new Color(0.85f, 0.5f, 0.9f), new Color(1f, 0.62f, 0.2f), new Color(0.4f, 0.9f, 0.8f), new Color(0.95f, 0.55f, 0.7f) };
        if (game.text != null) game.text.Register(this);
        Show(false);
    }

    public void _OnTextLanguage() { if (hasData && content.activeSelf) FillList(); else SetTitle(); }

    void SetTitle()
    {
        if (game.text == null) return;
        if (title != null) title.text = game.text.Get("mini.title");
        if (waiting != null) waiting.text = game.text.Get("mini.waiting");
        if (upperLabel != null) upperLabel.text = game.text.Get(upperOn ? "mini.upperOn" : "mini.upperOff");
        if (soloLabel != null) soloLabel.text = solo < 0 || solo >= nameOf.Length ? game.text.Get("mini.all") : Clean(nameOf[solo]);
    }

    void Update()
    {
        int phase = game.phase;
        // a new round's map: the miniature takes its meshes a moment after it is built (the scenery is merged last)
        if (game.MapReady && game.round != snapRound && phase != PHGame.PH_LOBBY)
        {
            if (snapAt < 0f) snapAt = Time.time + 2f;
            else if (Time.time >= snapAt) { Snap(); snapRound = game.round; snapAt = -1f; }
        }
        if (phase != lastPhase)
        {
            if (phase == PHGame.PH_BRIEF || phase == PHGame.PH_HIDE) BeginRound();
            if (phase == PHGame.PH_HUNT) huntStart = Time.time;
            lastPhase = phase;
        }
        bool rec = recRound == game.round && (phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT);
        if (rec && Time.time >= nextSample) { nextSample = Time.time + STEP; Sample(phase == PHGame.PH_HUNT); }
        // shown from the reveal until the next map starts loading (and only while the miniature has its map)
        bool show = hasData && snapRound == recRound && dioWalls.sharedMesh != null && (phase == PHGame.PH_END || phase == PHGame.PH_LOBBY);
        if (show && shownRound != recRound)
        {
            shownRound = recRound;
            for (int i = 0; i < npts.Length; i++) EndStay(i);
            FillList(); drawNext = 0; ClearProps();
        }
        if (content.activeSelf != show) Show(show);
        // Perf: a path or a prop a frame (all at once was a hitch at END)
        if (show && drawNext < paths.Length) { DrawOne(drawNext); drawNext++; if (drawNext >= paths.Length) { propNext = 0; fenceNext = 0; Apply(); } }
        else if (show && builder.objects != null && propNext < builder.objects.Length) PlaceMapProps();
        else if (show && builder.fenceRoot != null && fenceNext < builder.fenceRoot.childCount) PlaceFences();
        if (show) FaceLabels();
    }

    // the name tags turn to face the viewer (Cor: billboarded)
    void FaceLabels()
    {
        Vector3 eye = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        for (int i = 0; i < pins.Length; i++)
        {
            if (!pins[i].gameObject.activeSelf) continue;
            Transform t = pins[i].GetChild(pins[i].childCount - 1);   // (the name: the pin's last child; a TextMeshPro's transform is not exposed to Udon)
            Vector3 d = t.position - eye; d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(d, Vector3.up);
        }
    }

    int drawNext = 9999;

    void Show(bool on)
    {
        content.SetActive(on);
        if (list != null) list.gameObject.SetActive(on);
        if (waiting != null) waiting.gameObject.SetActive(!on);
        SetTitle();
    }

    void BeginRound()
    {
        if (recRound == game.round) return;
        recRound = game.round; hasData = true; shownRound = -1;
        nextSample = 0f; huntStart = Time.time;
        for (int i = 0; i < npts.Length; i++)
        {
            npts[i] = 0; dist[i] = 0f; caughtAt[i] = -1f; catches[i] = 0; roleOf[i] = 0; playerOf[i] = 0; nameOf[i] = "";
            still[i] = 0f; stillDef[i] = -1; nStay[i] = 0;
        }
        for (int k = 0; k < dDef.Length; k++) { dDef[k] = -1; dTime[k] = 0f; dBestStill[k] = 0f; }
    }

    void Sample(bool hunt)
    {
        int n = Mathf.Min(paths.Length, game.slots.Length);
        for (int i = 0; i < n; i++)
        {
            int pid = game.slotPlayer[i];
            int role = game.RoleOf(i);
            PHSlot s = game.slots[i];
            if (pid <= 0 || s == null || s.round != game.round || role == PHGame.ROLE_NONE) continue;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
            if (p == null || !Utilities.IsValid(p)) continue;
            if (roleOf[i] == 0) { roleOf[i] = role; playerOf[i] = pid; nameOf[i] = p.displayName; }
            if (role == PHGame.ROLE_HIDER)
            {
                if (s.caught) { if (caughtAt[i] < 0f) { caughtAt[i] = hunt ? Time.time - huntStart : 0f; EndStay(i); } continue; }
                Vector3 at = s.def >= 0 ? s.propPos : p.GetPosition();
                Add(i, at);
                if (s.def >= 0) Dwell(i, s.def, at, s.propYaw);
            }
            else if (role == PHGame.ROLE_HUNTER && hunt)
            {
                catches[i] = s.catches;
                Add(i, p.GetPosition());
            }
        }
    }

    // a second in a disguise: its time, and the longest it stood still in it (where, facing which way)
    void Dwell(int i, int def, Vector3 at, float yaw)
    {
        int k = -1, free = -1;
        for (int j = 0; j < MAXD; j++)
        {
            int x = i * MAXD + j;
            if (dDef[x] == def) { k = x; break; }
            if (dDef[x] < 0 && free < 0) free = x;
        }
        if (k < 0) { if (free < 0) return; k = free; dDef[k] = def; dTime[k] = 0f; dBestStill[k] = 0f; dBestPos[k] = at; dBestYaw[k] = yaw; }
        dTime[k] += STEP;
        Vector3 d = at - stillAt[i];
        if (stillDef[i] == def && new Vector2(d.x, d.z).magnitude < 0.5f) still[i] += STEP;
        else { EndStay(i); still[i] = STEP; stillAt[i] = at; stillDef[i] = def; stillYaw[i] = yaw; }
        if (still[i] >= dBestStill[k]) { dBestStill[k] = still[i]; dBestPos[k] = at; dBestYaw[k] = yaw; }
    }

    // a stay that has ended: kept when it lasted STAY_MIN or more (the same disguise in the same spot again adds to it)
    void EndStay(int i)
    {
        if (stillDef[i] < 0 || still[i] < STAY_MIN) { still[i] = 0f; return; }
        int b = i * MAXS;
        for (int k = 0; k < nStay[i]; k++)
        {
            Vector3 d = sPos[b + k] - stillAt[i];
            if (sDef[b + k] == stillDef[i] && new Vector2(d.x, d.z).magnitude < 0.5f) { sDur[b + k] += still[i]; still[i] = 0f; return; }
        }
        if (nStay[i] < MAXS)
        {
            int x = b + nStay[i]++;
            sDef[x] = stillDef[i]; sPos[x] = stillAt[i]; sYaw[x] = stillYaw[i]; sDur[x] = still[i];
        }
        still[i] = 0f;
    }

    void Add(int i, Vector3 at)
    {
        int k = npts[i];
        if (k > 0)
        {
            Vector3 last = pts[i * MAXP + k - 1];
            float d = Vector3.Distance(new Vector3(last.x, 0f, last.z), new Vector3(at.x, 0f, at.z));
            if (d < MIN_MOVE) return;
            if (d < 40f) dist[i] += d;   // (a teleport is not a walk)
        }
        if (k >= MAXP) return;
        pts[i * MAXP + k] = at;
        npts[i] = k + 1;
    }

    // the miniature takes the map's own meshes (ground, walls, merged scenery; no roofs), scaled to the table
    void Snap()
    {
        float w = builder.SiteWidth(), d = builder.SiteDepth();
        origin = builder.transform.position + new Vector3(w * 0.5f, 0f, d * 0.5f);
        float k = tableSize / Mathf.Max(8f, Mathf.Max(w, d));
        model.localScale = new Vector3(k, k, k);
        // the ground (floors indoors too): its mesh runs hundreds of metres past the map, so the diorama's material
        // keeps only the map's rectangle (Cor: the floors)
        Place(dioGround, builder.groundFilter);
        siteW = w; siteD = d;
        SetBox();
        Place(dioWalls, builder.wallsFilter);
        // (no scenery tiles: they run past the map's edge, Cor: only the game area)
        for (int i = 0; i < dioScenery.Length; i++) dioScenery[i].gameObject.SetActive(false);
    }

    float siteW, siteD;

    void SetBox()
    {
        if (dioMat == null) return;
        float k = model.localScale.x;
        Vector3 c = model.position;
        dioMat.SetVector("_Box", new Vector4(c.x - siteW * 0.5f * k, c.z - siteD * 0.5f * k, c.x + siteW * 0.5f * k, c.z + siteD * 0.5f * k));
    }

    void Place(MeshFilter dio, MeshFilter src)
    {
        if (dio == null) return;
        if (src == null || src.sharedMesh == null) { dio.gameObject.SetActive(false); return; }
        dio.sharedMesh = src.sharedMesh;
        Transform t = dio.transform;
        t.localPosition = src.transform.position - origin;
        t.localRotation = src.transform.rotation;
        t.localScale = src.transform.lossyScale;
        dio.gameObject.SetActive(true);
    }

    void ClearProps()
    {
        for (int i = 0; i < shownProp.Length; i++) if (shownProp[i] != null) { Destroy(shownProp[i]); shownProp[i] = null; }
        for (int i = 0; i < shownOutline.Length; i++) if (shownOutline[i] != null) { Destroy(shownOutline[i]); shownOutline[i] = null; }
        for (int i = 0; i < nMapProp; i++) if (mapProp[i] != null) { Destroy(mapProp[i]); mapProp[i] = null; }
        nMapProp = 0; propNext = 99999; solo = -1;
        for (int i = 0; i < nFence; i++) if (fence[i] != null) { Destroy(fence[i]); fence[i] = null; }
        nFence = 0; fenceNext = 99999;
        for (int i = 0; i < pins.Length; i++) { pins[i].gameObject.SetActive(false); pinLabel[i].text = ""; }
        for (int i = 0; i < paths.Length; i++) paths[i].gameObject.SetActive(false);
    }

    Color ColourOf(int i)
    {
        if (roleOf[i] == PHGame.ROLE_HUNTER) return new Color(0.92f, 0.2f, 0.18f);
        int c = 0;
        for (int j = 0; j < i; j++) if (roleOf[j] == PHGame.ROLE_HIDER && npts[j] > 0) c++;
        return palette[c % palette.Length];
    }

    // one player's path, and a hider's longest disguise standing where they stayed longest in it
    void DrawOne(int i)
    {
        int k = npts[i];
        if (roleOf[i] == 0 || k == 0) return;
        Color c = ColourOf(i);
        LineRenderer lr = paths[i];
        Vector3[] line = new Vector3[k];
        Vector3 lift = new Vector3(0f, 0.15f, 0f) - origin;
        for (int j = 0; j < k; j++) line[j] = pts[i * MAXP + j] + lift;
        lr.positionCount = k;
        lr.SetPositions(line);   // (one call: a point at a time was a call each)
        lr.widthMultiplier = 0.0012f / Mathf.Max(0.001f, model.localScale.x);   // (Cor: thinner)
        lr.startColor = c; lr.endColor = c;
        lr.gameObject.SetActive(true);
        if (roleOf[i] != PHGame.ROLE_HIDER) return;
        // every spot they hid in (stood still STAY_MIN or more), the prop they were there, (Cor:
        // outlined); the pin with their name over the longest
        int bs = i * MAXS, longest = -1;
        for (int q = 0; q < nStay[i]; q++)
        {
            int x = bs + q;
            if (longest < 0 || sDur[x] > sDur[longest]) longest = x;
            int def = sDef[x];
            if (def < 0 || def >= builder.propPrefabs.Length || builder.propPrefabs[def] == null) continue;
            GameObject go = Copy(builder.propPrefabs[def], sPos[x], sYaw[x], 1f);
            shownProp[x] = go;
            // its outline: the same copy again, in the outline material (the back faces pushed out), the player's colour
            if (outlineMat != null)
            {
                GameObject ol = Copy(builder.propPrefabs[def], sPos[x], sYaw[x], 1f);
                if (outlineBlock == null) outlineBlock = new MaterialPropertyBlock();
                outlineBlock.SetColor("_Color", c);
                MeshRenderer[] rs = ol.GetComponentsInChildren<MeshRenderer>(true);
                for (int r = 0; r < rs.Length; r++)
                {
                    Material[] ms = new Material[rs[r].sharedMaterials.Length];
                    for (int m = 0; m < ms.Length; m++) ms[m] = outlineMat;
                    rs[r].sharedMaterials = ms;
                    rs[r].SetPropertyBlock(outlineBlock);
                }
                shownOutline[x] = ol;
            }
        }
        longestStay[i] = longest;
        Transform pin = pins[i];
        pin.localPosition = (longest >= 0 ? sPos[longest] : pts[i * MAXP + k - 1]) - origin;
        pin.gameObject.SetActive(true);
        pinLabel[i].text = Clean(nameOf[i]);
        pinLabel[i].color = c;
    }

    // a prop's copy on the table: no colliders, on the default layer
    GameObject Copy(GameObject prefab, Vector3 at, float yaw, float size)
    {
        GameObject go = Instantiate(prefab);
        Transform t = go.transform;
        Vector3 sc = t.localScale;
        t.SetParent(model, false);
        t.localPosition = at - origin;
        t.localRotation = Quaternion.Euler(0f, yaw, 0f);
        t.localScale = sc * size;
        Collider[] cols = go.GetComponentsInChildren<Collider>(true);
        for (int q = 0; q < cols.Length; q++) cols[q].enabled = false;
        Transform[] parts = go.GetComponentsInChildren<Transform>(true);
        for (int q = 0; q < parts.Length; q++) parts[q].gameObject.layer = 0;
        // (no dark patch under it: one draw less a prop)
        if (t.childCount > 0 && t.GetChild(t.childCount - 1).name == "Shade") t.GetChild(t.childCount - 1).gameObject.SetActive(false);
        return go;
    }

    // the map's own props, true to scale, a few a frame (Cor: show all the props): those that stand on the floor
    void PlaceMapProps()
    {
        GameObject[] objs = builder.objects;
        int[] par = builder.gen.emitParent, ed = builder.gen.emitDef, fl = builder.gen.defFlags;
        for (int n = 0; n < 6 && propNext < objs.Length; propNext++)
        {
            GameObject src = objs[propNext];
            if (src == null || nMapProp >= MapPropCap() || propNext >= par.Length) continue;
            if (par[propNext] >= 0 || (fl[ed[propNext]] & GenData.FL_DECOR) != 0) continue;
            int def = ed[propNext];
            if (def >= builder.propPrefabs.Length || builder.propPrefabs[def] == null) continue;
            Transform st = src.transform;
            GameObject go = Copy(builder.propPrefabs[def], st.position, st.eulerAngles.y, 1f);
            mapUpper[nMapProp] = st.position.y - origin.y > UPPER_Y;
            go.SetActive(upperOn || !mapUpper[nMapProp]);
            mapProp[nMapProp++] = go;
            n++;
        }
    }

    // every copy is a draw of its own unless dynamic batching joins the small ones (on for Android too since Cor's
    // go, 2026-11-20)
    int MapPropCap() { return MAX_MAP_PROPS; }

    // the map's fences, as they stand (Cor: include the fences), a few a frame; only those on the map itself
    GameObject[] fence = new GameObject[300];
    int nFence, fenceNext = 99999;

    void PlaceFences()
    {
        Transform root = builder.fenceRoot;
        for (int n = 0; n < 8 && fenceNext < root.childCount; fenceNext++)
        {
            if (nFence >= FenceCap()) { fenceNext = root.childCount; break; }
            Transform src = root.GetChild(fenceNext);
            Vector3 at = src.position - origin;
            if (Mathf.Abs(at.x) > siteW * 0.5f + 0.5f || Mathf.Abs(at.z) > siteD * 0.5f + 0.5f) continue;
            GameObject go = Instantiate(src.gameObject);
            Transform t = go.transform;
            t.SetParent(model, false);
            t.localPosition = at;
            t.localRotation = src.rotation;
            t.localScale = src.lossyScale;
            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            for (int q = 0; q < cols.Length; q++) cols[q].enabled = false;
            // (the map's pieces have their renderers off: the merged scenery draws them)
            MeshRenderer[] rs = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int q = 0; q < rs.Length; q++) rs[q].enabled = true;
            Transform[] parts = go.GetComponentsInChildren<Transform>(true);
            for (int q = 0; q < parts.Length; q++) parts[q].gameObject.layer = 0;
            go.SetActive(true);
            fence[nFence++] = go;
            n++;
        }
    }

    int FenceCap() { return 300; }

    // the buttons: the upper floors on or off; everyone, or one player alone (each press the next one)
    public void ToggleUpper() { upperOn = !upperOn; Apply(); }

    // a name tag pressed (PHButton, buttonArg = slot + 1): that player alone, or everyone again
    public void PickSolo()
    {
        int c = buttonArg - 1;
        solo = solo == c || c < 0 ? -1 : c;
        Apply();
    }

    public void NextSolo()
    {
        int n = paths.Length;
        for (int k = 1; k <= n + 1; k++)
        {
            int c = solo + k;
            if (c >= n) { solo = -1; break; }
            if (roleOf[c] != 0 && npts[c] > 0) { solo = c; break; }
        }
        Apply();
    }

    void Apply()
    {
        if (dioMat != null) dioMat.SetFloat("_ClipY", upperOn ? 100000f : model.position.y + CLIP_Y * model.localScale.x);
        SetBox();
        for (int i = 0; i < paths.Length; i++)
        {
            bool mine = solo < 0 || solo == i;
            bool has = roleOf[i] != 0 && npts[i] > 0;
            paths[i].gameObject.SetActive(has && mine);
            bool pinOn = has && mine && roleOf[i] == PHGame.ROLE_HIDER && pinLabel[i].text != "" && (upperOn || pins[i].localPosition.y <= UPPER_Y);
            if (pins[i].gameObject.activeSelf != pinOn) pins[i].gameObject.SetActive(pinOn);
            for (int j = 0; j < MAXS; j++)
            {
                GameObject go = shownProp[i * MAXS + j];
                if (go == null) continue;
                // everyone: each player's longest hide; one player alone: all of their hiding spots (Cor)
                bool spot = solo == i || (solo < 0 && longestStay[i] == i * MAXS + j);
                bool on = spot && (upperOn || go.transform.localPosition.y <= UPPER_Y);
                go.SetActive(on);
                if (shownOutline[i * MAXS + j] != null) shownOutline[i * MAXS + j].SetActive(on);
            }
        }
        for (int i = 0; i < nMapProp; i++) if (mapProp[i] != null) mapProp[i].SetActive(upperOn || !mapUpper[i]);
        SetTitle();
    }

    void FillList()
    {
        SetTitle();
        string text = "";
        for (int i = 0; i < paths.Length; i++)
        {
            if (roleOf[i] == 0 || npts[i] == 0) continue;
            Color c = ColourOf(i);
            string metres = Mathf.RoundToInt(dist[i]).ToString();
            string line;
            if (roleOf[i] == PHGame.ROLE_HUNTER) line = game.text.F2("mini.hunter", metres, catches[i].ToString());
            else if (caughtAt[i] >= 0f) line = game.text.F2("mini.caught", metres, Clock(caughtAt[i]));
            else line = game.text.F1("mini.survived", metres);
            text += "<color=#" + Hex(c) + ">" + Clean(nameOf[i]) + "</color>  <color=#6D5524>" + line + "</color>\n";
        }
        if (list != null) list.text = text;
    }

    string Hex(Color c)
    {
        int v = (Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f) << 16) | (Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f) << 8) | Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
        return v.ToString("X6");
    }

    string Clock(float t)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(t));
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    // a display name as plain text (no rich-text tags of its own)
    string Clean(string n)
    {
        if (n == null) return "";
        n = n.Replace("<", "‹").Replace(">", "›");
        return n.Length > 18 ? n.Substring(0, 17) + "…" : n;
    }
}
