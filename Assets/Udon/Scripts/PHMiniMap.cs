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
    // (the round's replay with the upper floors off, Cor 2026-11-22: they stay, drawn as a see-through screen) the
    // props' own material and its screened twin (PropHunt/LitDither), swapped on the upper floors' copies
    public Material propLit, propLitDither;
    public Material dioMat;              // PropHunt/Diorama on the walls: cut above _ClipY when the upper floors are off
    public Transform propRoot;           // the map's props in miniature (under model)
    public Material outlineMat;
    // the map's props on the table as cheap stand-ins (lobby profiling 2026-11-24: an XL map's 1,132 props were about
    // 767,000 vertices): one simplified mesh per def (DioramaMiniBaker; null: the full prefab), drawn with a copy of
    // the template (inactive, the props' material)
    public Mesh[] miniMeshes;
    public GameObject miniTemplate;          // PropHunt/DioramaOutline: a hiding spot's prop outlined in the player's colour
    public TextMeshProUGUI upperLabel, soloLabel;   // the table's two buttons

    const int MAXP = 1440;               // four points a second: six minutes of hide and hunt
    const float STEP = 0.25f, MIN_MOVE = 0.3f;   // (Cor: a point a second cut corners through the walls)
    const int MAXD = 6;                  // disguises remembered per hider
    const int MAXSEG = 4;                // path pieces per player: a jump over JUMP metres (a teleport) starts a new one
    const float JUMP = 40f, LABEL_Y = 8.6f;   // (LABEL_Y: the name's height on its pin, as LobbyBuilder.BuildMiniMap)
    int[] segAt, nSeg;                   // slot * MAXSEG + s: the point piece s starts at (piece 0 at 0)
    LineRenderer[] segLine;              // slot * MAXSEG + s, s >= 1: copies of the slot's path, made when first needed
    Vector3 siteMin, siteMax;            // the map's rectangle this sample: points outside it are not kept
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
    const int PROPS_A_FRAME = 6, SCAN_A_FRAME = 200;   // copies a frame, and emits looked at a frame (grass tufts skipped)
    bool propsUpperOn = true;
    bool propsDither;                    // the upper floors' props as Apply last left them: screened
    MeshRenderer[] upperR = new MeshRenderer[0]; int nUpperR;   // the upper floors' copies' PropLit renderers            // the upper floors' map props as Apply last left them
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
        mapProp = new GameObject[0]; mapUpper = new bool[0];   // (sized to the map's objects when they are placed)
        segAt = new int[n * MAXSEG]; nSeg = new int[n]; segLine = new LineRenderer[n * MAXSEG];
        palette = new Color[] {
            new Color(0.5f, 0.45f, 0.95f), new Color(0.35f, 0.75f, 0.95f), new Color(0.98f, 0.8f, 0.3f), new Color(0.55f, 0.85f, 0.45f),
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
        // (Cor, 2026-11-22: the old summary is gone, the paths, the pins and the hiding spots; the round's replay
        // (PHReplay) is the diorama's view now, so nothing is sampled for it any more)
        // shown from the reveal until the next map starts loading (and only while the miniature has its map)
        bool show = hasData && snapRound == recRound && dioWalls.sharedMesh != null && (phase == PHGame.PH_END || phase == PHGame.PH_LOBBY);
        if (show && shownRound != recRound)
        {
            shownRound = recRound;
            for (int i = 0; i < npts.Length; i++) EndStay(i);
            FillList(); drawNext = 0; ClearProps();
        }
        if (content.activeSelf != show) Show(show);
        if (!show && nMapProp > 0) DropSomeProps();
        // Perf: a path or a prop a frame (all at once was a hitch at END)
        if (show && drawNext < paths.Length) { drawNext = paths.Length; propNext = 0; fenceNext = 0; Apply(); }
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
            still[i] = 0f; stillDef[i] = -1; nStay[i] = 0; nSeg[i] = 1;
        }
        for (int k = 0; k < dDef.Length; k++) { dDef[k] = -1; dTime[k] = 0f; dBestStill[k] = 0f; }
    }

    void Sample(bool hunt)
    {
        int n = Mathf.Min(paths.Length, game.slots.Length);
        // (places on the map, map-local: PHGame.layers puts each team on its own copy, PHGame.MapLocalOf)
        siteMin = Vector3.zero; siteMax = siteMin + new Vector3(builder.SiteWidth(), 0f, builder.SiteDepth());
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
                Vector3 at = s.def >= 0 ? s.propPos : game.MapLocalOf(i, p.GetPosition());
                Add(i, at);
                if (s.def >= 0) Dwell(i, s.def, at, s.propYaw);
            }
            else if (role == PHGame.ROLE_HUNTER && hunt)
            {
                catches[i] = s.catches;
                Add(i, game.MapLocalOf(i, p.GetPosition()));
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
        if (stillDef[i] == def && new Vector2(d.x, d.z).magnitude < 0.5f) { still[i] += STEP; stillYaw[i] = yaw; }   // (the facing they ended the stay in: a turn while standing still counts)
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
            if (sDef[b + k] == stillDef[i] && new Vector2(d.x, d.z).magnitude < 0.5f) { sDur[b + k] += still[i]; sYaw[b + k] = stillYaw[i]; still[i] = 0f; return; }
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
        // only on the map (a hunter's first samples can fall in the lobby, before the release: Strelia's red line
        // ran in through the walls)
        if (at.x < siteMin.x - 1f || at.x > siteMax.x + 1f || at.z < siteMin.z - 1f || at.z > siteMax.z + 1f) return;
        int k = npts[i];
        if (k > 0)
        {
            Vector3 last = pts[i * MAXP + k - 1];
            float d = Vector3.Distance(new Vector3(last.x, 0f, last.z), new Vector3(at.x, 0f, at.z));
            if (d < MIN_MOVE) return;
            if (d < JUMP) dist[i] += d;   // (a teleport is not a walk: and it starts a new piece of the line)
            else if (k < MAXP && nSeg[i] < MAXSEG) { segAt[i * MAXSEG + nSeg[i]] = k; nSeg[i]++; }
        }
        if (k >= MAXP) return;
        pts[i * MAXP + k] = at;
        npts[i] = k + 1;
    }

    // the miniature takes the map's own meshes (ground, walls, merged scenery; no roofs), scaled to the table
    void Snap()
    {
        float w = builder.SiteWidth(), d = builder.SiteDepth();
        // (map-local, as the recorded paths: the map's own objects are taken back from this client's copy, ToLocal)
        origin = new Vector3(w * 0.5f, 0f, d * 0.5f);
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
        t.localPosition = builder.ToLocal(src.transform.position) - origin;
        t.localRotation = src.transform.rotation;
        t.localScale = src.transform.lossyScale;
        dio.gameObject.SetActive(true);
    }

    void ClearProps()
    {
        for (int i = 0; i < shownProp.Length; i++) if (shownProp[i] != null) { Destroy(shownProp[i]); shownProp[i] = null; }
        for (int i = 0; i < shownOutline.Length; i++) if (shownOutline[i] != null) { Destroy(shownOutline[i]); shownOutline[i] = null; }
        for (int i = 0; i < nMapProp; i++) if (mapProp[i] != null) { Destroy(mapProp[i]); mapProp[i] = null; }
        nMapProp = 0; propNext = 99999; solo = -1; nUpperR = 0;
        for (int i = 0; i < nFence; i++) if (fence[i] != null) { Destroy(fence[i]); fence[i] = null; }
        nFence = 0; fenceNext = 99999;
        for (int i = 0; i < pins.Length; i++) { pins[i].gameObject.SetActive(false); pinLabel[i].text = ""; }
        for (int i = 0; i < paths.Length; i++) paths[i].gameObject.SetActive(false);
        for (int i = 0; i < segLine.Length; i++) if (segLine[i] != null) segLine[i].gameObject.SetActive(false);
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
        Vector3 lift = new Vector3(0f, 0.15f, 0f) - origin;
        int ns = Mathf.Clamp(nSeg[i], 1, MAXSEG);
        for (int s = 0; s < ns; s++)
        {
            int a = s == 0 ? 0 : segAt[i * MAXSEG + s], b = s + 1 < ns ? segAt[i * MAXSEG + s + 1] : k;
            LineRenderer lr = SegLine(i, s);
            Vector3[] line = new Vector3[b - a];
            for (int j = a; j < b; j++) line[j - a] = pts[i * MAXP + j] + lift;
            lr.positionCount = b - a;
            lr.SetPositions(line);   // (one call: a point at a time was a call each)
            lr.widthMultiplier = 0.0003f / Mathf.Max(0.001f, model.localScale.x);   // (Cor: thinner, then a quarter of that, 2026-11-21)
            lr.startColor = c; lr.endColor = c;
            lr.gameObject.SetActive(true);
        }
        Transform pin = pins[i];
        Transform tag = pin.GetChild(pin.childCount - 1);   // (the name: the pin's last child)
        // a hunter's tag (Cor: clickable like the hiders'): at the release point, stacked over the other hunters'
        if (roleOf[i] == PHGame.ROLE_HUNTER)
        {
            int h = 0;
            for (int j = 0; j < i; j++) if (roleOf[j] == PHGame.ROLE_HUNTER && npts[j] > 0) h++;
            pin.localPosition = builder.ReleasePoint() - origin;
            tag.localPosition = new Vector3(0f, LABEL_Y + h * 3.2f, 0f);
            pin.gameObject.SetActive(true);
            pinLabel[i].text = Clean(nameOf[i]);
            pinLabel[i].color = c;
            return;
        }
        if (roleOf[i] != PHGame.ROLE_HIDER) return;
        tag.localPosition = new Vector3(0f, LABEL_Y, 0f);
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
        pin.localPosition = (longest >= 0 ? sPos[longest] : pts[i * MAXP + k - 1]) - origin;
        pin.gameObject.SetActive(true);
        pinLabel[i].text = Clean(nameOf[i]);
        pinLabel[i].color = c;
    }

    // piece s of slot i's path: piece 0 is the slot's own line, the others copies of it made the first time needed
    LineRenderer SegLine(int i, int s)
    {
        if (s == 0) return paths[i];
        int x = i * MAXSEG + s;
        if (segLine[x] == null)
        {
            GameObject go = Instantiate(paths[i].gameObject);
            Transform t = go.transform;
            t.SetParent(paths[i].transform.parent, false);
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            segLine[x] = go.GetComponent<LineRenderer>();
        }
        return segLine[x];
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

    // the map's own props, true to scale, a few a frame (Cor: no cap, show all the props; the map is gone by then):
    // every object the map placed, on the floor or standing on furniture, but the grass tufts (FL_DECOR: ground
    // cover, not props). A dense map has a few thousand: at PROPS_A_FRAME they fill in over several seconds
    void PlaceMapProps()
    {
        GameObject[] objs = builder.objects;
        int[] ed = builder.gen.emitDef, fl = builder.gen.defFlags;
        if (mapProp.Length < objs.Length) { mapProp = new GameObject[objs.Length]; mapUpper = new bool[objs.Length]; }
        int scan = 0;
        for (int n = 0; n < PROPS_A_FRAME && scan < SCAN_A_FRAME && propNext < objs.Length; propNext++)
        {
            scan++;
            GameObject src = objs[propNext];
            if (src == null || nMapProp >= mapProp.Length || propNext >= ed.Length) continue;
            int def = ed[propNext];
            if (def < 0 || def >= fl.Length || (fl[def] & GenData.FL_DECOR) != 0) continue;
            if (def >= builder.propPrefabs.Length || builder.propPrefabs[def] == null) continue;
            Transform st = src.transform;
            bool mini = miniTemplate != null && miniMeshes != null && def < miniMeshes.Length && miniMeshes[def] != null;
            GameObject go = Copy(mini ? miniTemplate : builder.propPrefabs[def], builder.ToLocal(st.position), st.eulerAngles.y, 1f);
            // (the template's renderer may be off: PHLobbyCull switches the lobby's off while the player is away)
            if (mini) { go.GetComponent<MeshFilter>().sharedMesh = miniMeshes[def]; go.GetComponent<MeshRenderer>().enabled = true; }
            mapUpper[nMapProp] = st.position.y - origin.y > UPPER_Y;
            go.SetActive(upperOn || propsDither || !mapUpper[nMapProp]);
            if (mapUpper[nMapProp] && propLit != null)
            {
                MeshRenderer[] rs = go.GetComponentsInChildren<MeshRenderer>(true);
                for (int q = 0; q < rs.Length; q++)
                {
                    if (rs[q].sharedMaterial != propLit) continue;
                    if (nUpperR >= upperR.Length) { MeshRenderer[] more = new MeshRenderer[Mathf.Max(64, upperR.Length * 2)]; System.Array.Copy(upperR, more, nUpperR); upperR = more; }
                    upperR[nUpperR++] = rs[q];
                    if (propsDither) rs[q].sharedMaterial = propLitDither;
                }
            }
            mapProp[nMapProp++] = go;
            n++;
        }
    }

    // the last round's map props go a few a frame while the diorama is hidden (thousands destroyed at once at the
    // next reveal would be a long frame); shown again before they are all gone, it is drawn afresh. (Every copy is
    // a draw of its own unless dynamic batching joins the small ones: on for Android too since Cor's go, 2026-11-20)
    void DropSomeProps()
    {
        shownRound = -1;
        for (int n = 0; n < 4 && nMapProp > 0; n++)
        {
            nMapProp--;
            if (mapProp[nMapProp] != null) { Destroy(mapProp[nMapProp]); mapProp[nMapProp] = null; }
        }
        if (nMapProp == 0) nUpperR = 0;
    }

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
            Vector3 at = builder.ToLocal(src.position) - origin;
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

    // the replay on the table (PHReplay): while it plays, the paths, pins and hiding spots step aside
    [HideInInspector] public bool replaying;
    public void SetReplay(bool on) { replaying = on; Apply(); }
    public Vector3 Origin() { return origin; }
    public bool UpperOn() { return upperOn; }
    public float UpperY() { return UPPER_Y; }

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
        // the upper floors off: cut away; but in the replay they stay, screened (Cor), the players on them too (PHReplay)
        bool dither = replaying && !upperOn, upperShown = upperOn || dither;
        float cut = model.position.y + CLIP_Y * model.localScale.x;
        if (dioMat != null) { dioMat.SetFloat("_ClipY", upperShown ? 100000f : cut); dioMat.SetFloat("_DitherY", dither ? cut : 100000f); }
        SetBox();
        for (int i = 0; i < paths.Length; i++)
        {
            bool mine = false;   // (the old summary's paths and pins: never, Cor)
            bool has = roleOf[i] != 0 && npts[i] > 0;
            paths[i].gameObject.SetActive(has && mine);
            for (int s = 1; s < MAXSEG; s++) { LineRenderer e = segLine[i * MAXSEG + s]; if (e != null) e.gameObject.SetActive(has && mine && s < nSeg[i]); }
            bool pinOn = has && mine && pinLabel[i].text != "" && (upperOn || pins[i].localPosition.y <= UPPER_Y);
            if (pins[i].gameObject.activeSelf != pinOn) pins[i].gameObject.SetActive(pinOn);
            for (int j = 0; j < MAXS; j++)
            {
                GameObject go = shownProp[i * MAXS + j];
                if (go == null) continue;
                // everyone: each player's longest hide; one player alone: all of their hiding spots (Cor)
                bool spot = solo == i || (solo < 0 && longestStay[i] == i * MAXS + j);
                bool on = false;   // (the old summary's hiding spots: never, Cor)
                go.SetActive(on);
                if (shownOutline[i * MAXS + j] != null) shownOutline[i * MAXS + j].SetActive(on);
            }
        }
        // (only when the upper floors switch: with every prop shown that is thousands of switches, not for a name tag)
        if (propsUpperOn != upperShown)
        {
            propsUpperOn = upperShown;
            for (int i = 0; i < nMapProp; i++) if (mapUpper[i] && mapProp[i] != null) mapProp[i].SetActive(upperShown);
        }
        if (propsDither != dither && propLitDither != null)
        {
            propsDither = dither;
            for (int i = 0; i < nUpperR; i++) if (upperR[i] != null) upperR[i].sharedMaterial = dither ? propLitDither : propLit;
        }
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
