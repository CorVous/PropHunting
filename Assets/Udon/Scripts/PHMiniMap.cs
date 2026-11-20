using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

// The LAST ROUND board in the lobby (Cor's group playtest, item 40: a mini map with paths and stats). A top-down
// photo of the round's map, every player's path over it, a mark where each hider ended (a cross where caught), and a
// short list: who, how far they went, caught when or survived. Shown from the END reveal until the next round's map
// starts loading; blank before the first round.
// Nothing is synced here: each client samples the positions it already has (a hider's synced prop position, or the
// player while they walk as themselves; a hunter's avatar) once a second through the hide and the hunt. A player
// who joins mid-round has the path from when they joined.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHMiniMap : UdonSharpBehaviour
{
    public PHGame game;
    public MapBuilderUdon builder;
    public Camera cam;                   // off; renders the photo once per map (Render() by hand)
    public GameObject content;           // the photo, the paths, the marks and the list (off when there is nothing to show)
    public RectTransform mapRect;        // the photo's rectangle: the paths and marks are laid in its local space
    public LineRenderer[] paths;         // one per slot
    public RectTransform[] marks;        // one per slot: where they ended
    public Image[] markImage;
    public Sprite markEnd, markCaught;
    public TextMeshProUGUI list, title, waiting;

    const int MAXP = 360;                // a point a second: six minutes of hide and hunt
    const float STEP = 1f, MIN_MOVE = 0.3f;
    Vector3[] pts;                       // slot * MAXP + i
    int[] npts;
    float[] dist;
    int[] roleOf;                        // the slot's role this round (0 none)
    int[] playerOf;
    string[] nameOf;
    float[] caughtAt;                    // seconds into the hunt, -1 survived
    int[] catches;
    float huntStart, nextSample, half;
    Vector3 centre;
    int recRound = -1, shownRound = -1, photoRound = -1, lastPhase = -1;
    float photoAt = -1f;
    bool hasData;

    Color[] palette;

    void Start()
    {
        int n = paths.Length;
        pts = new Vector3[n * MAXP]; npts = new int[n]; dist = new float[n]; roleOf = new int[n]; playerOf = new int[n];
        nameOf = new string[n]; caughtAt = new float[n]; catches = new int[n];
        palette = new Color[] {
            new Color(0.95f, 0.45f, 0.35f), new Color(0.35f, 0.75f, 0.95f), new Color(0.98f, 0.8f, 0.3f), new Color(0.55f, 0.85f, 0.45f),
            new Color(0.85f, 0.5f, 0.9f), new Color(1f, 0.62f, 0.2f), new Color(0.4f, 0.9f, 0.8f), new Color(0.95f, 0.55f, 0.7f) };
        if (game.text != null) game.text.Register(this);
        Show(false);
    }

    public void _OnTextLanguage() { if (hasData && content.activeSelf) Fill(); else SetTitle(); }

    void SetTitle()
    {
        if (game.text == null) return;
        if (title != null) title.text = game.text.Get("mini.title");
        if (waiting != null) waiting.text = game.text.Get("mini.waiting");
    }

    void Update()
    {
        int phase = game.phase;
        // a new round's map: the photo, a moment after it is built (as the map trail's); the paths start again
        if (game.MapReady && game.round != photoRound && phase != PHGame.PH_LOBBY)
        {
            if (photoAt < 0f) photoAt = Time.time + 1.5f;
            else if (Time.time >= photoAt) { Photo(); photoRound = game.round; photoAt = -1f; }
        }
        if (phase != lastPhase)
        {
            if (phase == PHGame.PH_BRIEF || phase == PHGame.PH_HIDE) BeginRound();
            if (phase == PHGame.PH_HUNT) huntStart = Time.time;
            lastPhase = phase;
        }
        bool rec = recRound == game.round && (phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT);
        if (rec && Time.time >= nextSample) { nextSample = Time.time + STEP; Sample(phase == PHGame.PH_HUNT); }
        // shown from the reveal until the next map starts loading
        bool show = hasData && (phase == PHGame.PH_END || phase == PHGame.PH_LOBBY);
        if (show && shownRound != recRound) { shownRound = recRound; Fill(); }
        if (content.activeSelf != show) Show(show);
    }

    void Show(bool on)
    {
        content.SetActive(on);
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
        }
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
                if (s.caught) { if (caughtAt[i] < 0f) caughtAt[i] = hunt ? Time.time - huntStart : 0f; continue; }
                Add(i, s.def >= 0 ? s.propPos : p.GetPosition());
            }
            else if (role == PHGame.ROLE_HUNTER && hunt)
            {
                catches[i] = s.catches;
                Add(i, p.GetPosition());
            }
        }
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

    // the photo: straight down over the site, the roofs off for the frame, the fog pushed out (the camera is high up)
    void Photo()
    {
        float w = builder.SiteWidth(), d = builder.SiteDepth();
        centre = builder.transform.position + new Vector3(w * 0.5f, 0f, d * 0.5f);
        half = Mathf.Max(w, d) * 0.5f + 2f;
        cam.orthographicSize = half;
        // just over the roofs, under the clouds (they drift over the map, and a photo from high up had them in it)
        cam.transform.SetPositionAndRotation(centre + Vector3.up * 24f, Quaternion.Euler(90f, 0f, 0f));
        bool roofs = builder.roofsFilter != null && builder.roofsFilter.GetComponent<MeshRenderer>().enabled;
        float fs = RenderSettings.fogStartDistance, fe = RenderSettings.fogEndDistance;
        RenderSettings.fogStartDistance = 4000f; RenderSettings.fogEndDistance = 5000f;
        builder.ShowRoofs(false);
        cam.Render();
        builder.ShowRoofs(roofs);
        RenderSettings.fogStartDistance = fs; RenderSettings.fogEndDistance = fe;
    }

    // a world position on the photo, in the photo rectangle's local space (its pivot in the middle)
    Vector3 OnMap(Vector3 p)
    {
        Rect r = mapRect.rect;
        float u = half > 0f ? (p.x - centre.x) / (2f * half) : 0f, v = half > 0f ? (p.z - centre.z) / (2f * half) : 0f;
        return new Vector3(Mathf.Clamp(u, -0.5f, 0.5f) * r.width, Mathf.Clamp(v, -0.5f, 0.5f) * r.height, -2f);
    }

    void Fill()
    {
        SetTitle();
        string text = "";
        int colour = 0;
        for (int i = 0; i < paths.Length; i++)
        {
            LineRenderer lr = paths[i];
            int k = npts[i];
            bool on = roleOf[i] != 0 && k > 0;
            lr.gameObject.SetActive(on);
            marks[i].gameObject.SetActive(on && roleOf[i] == PHGame.ROLE_HIDER);
            if (!on) continue;
            Color c = roleOf[i] == PHGame.ROLE_HUNTER ? new Color(0.92f, 0.2f, 0.18f) : palette[colour++ % palette.Length];
            lr.positionCount = k;
            for (int j = 0; j < k; j++) lr.SetPosition(j, OnMap(pts[i * MAXP + j]));
            lr.startColor = c; lr.endColor = c;
            if (roleOf[i] == PHGame.ROLE_HIDER)
            {
                Vector3 m = OnMap(pts[i * MAXP + k - 1]);
                marks[i].anchoredPosition = new Vector2(m.x, m.y);
                markImage[i].sprite = caughtAt[i] >= 0f ? markCaught : markEnd;
                markImage[i].color = c;
            }
            string hex = Hex(c);
            string metres = Mathf.RoundToInt(dist[i]).ToString();
            string line;
            if (roleOf[i] == PHGame.ROLE_HUNTER) line = game.text.F2("mini.hunter", metres, catches[i].ToString());
            else if (caughtAt[i] >= 0f) line = game.text.F2("mini.caught", metres, Clock(caughtAt[i]));
            else line = game.text.F1("mini.survived", metres);
            text += "<color=#" + hex + ">■</color> " + Clean(nameOf[i]) + "  <color=#B8AE98>" + line + "</color>\n";
        }
        list.text = text;
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
