using System.Collections.Generic;
using UnityEngine;

// The lobby (owner decisions G33, G39, G43, G44): the room the player starts in, away from any map.
//   the zone (G43): an 11 x 11 m green square, the main thing on the lobby's floor, big enough for a full instance
//     of 32 players standing about 1.9 m apart (121 m2, about 3.8 m2 each); whoever stands in it takes part in the
//     next round
//   the start stand (G39): in the middle of the zone, a pedestal with a big START button on top and a screen on
//     each face that shows what START will use; from anywhere in the zone it is a short walk inside it (at most
//     7.8 m, from a corner) and it can be pressed from every side. A press from outside the zone is refused with a hint.
//   the host panel (G44): a board on the north wall, facing the zone, pointed at and clicked from anywhere in the
//     room (in VR: point and pull the trigger, G38). It opens the host's settings: the map (seed, size Auto or an
//     override, density), the rules (round length, forced-taunt interval, wrong-prop penalty), the run cap. Only
//     the host can change them; in this single-player prototype the player is the host.
//   the sign on the south wall, by the way in, says where everything is.
// Idle bot figures stand in for the other players: those in the zone play, those at the walls watch. In the VRChat
// world the zone is a trigger volume and the players in it are the round's players.
// ?seed=... in the URL still skips the lobby and goes straight into a map (for testing).
public class Lobby : MonoBehaviour
{
    public static Lobby I;

    // the lobby stands well away from the maps, which are built from the origin towards +x and +z
    public static readonly Vector3 Origin = new Vector3(-120f, 0f, -120f);
    const float RoomW = 24f, RoomD = 22f, WallH = 3.2f;
    // the zone: a square on the floor in the middle of the room
    static readonly Vector3 ZoneCentre = new Vector3(0f, 0f, 1f);
    const float ZoneHalf = 5.5f;
    static readonly Vector3 BoardPos = new Vector3(0f, 0f, -RoomD / 2 + 0.25f);   // the sign on the south wall
    // the host panel on the north wall, its face 14 cm off the wall
    static readonly Vector3 HostPos = new Vector3(0f, 0f, RoomD / 2);
    const float HostHalfW = 1.9f, HostY0 = 0.8f, HostY1 = 3.0f, HostReach = 16f;
    // the start stand: in the middle of the zone; the button on top of its pedestal
    static readonly Vector3 StandPos = ZoneCentre;
    const float PedHalfX = 0.55f, PedHalfZ = 0.4f, PedH = 1.0f;
    static readonly Vector3 ButtonPos = StandPos + new Vector3(0f, PedH + 0.08f, 0f);
    const float ButtonReach = 2.4f, ButtonRadius = 0.22f;

    public bool panelOpen;          // the host panel is open
    public float panelOpenedAt;
    public int botsInZone = Balance.LobbyBotsInZone[0];
    readonly List<GameObject> bots = new List<GameObject>();
    GameObject root;
    TextMesh standSouth, standNorth;
    float hintUntil;
    string hint = "";

    void Awake() { I = this; }

    public Vector3 Spawn { get { return Origin + new Vector3(0f, 0.05f, -RoomD / 2 + 2.2f); } }

    // is a point (world) inside the zone?
    public bool InZone(Vector3 p)
    {
        Vector3 l = p - Origin - ZoneCentre;
        return Mathf.Abs(l.x) <= ZoneHalf && Mathf.Abs(l.z) <= ZoneHalf;
    }

    public bool PlayerInZone { get { var p = Game.I.player; return p.active && InZone(p.body.pos); } }

    // everyone taking part: the player and the bots in the zone
    public int Participants { get { return 1 + botsInZone; } }

    public bool NearStand
    {
        get
        {
            var p = Game.I.player;
            Vector3 b = Origin + StandPos;
            return p.active && new Vector2(p.body.pos.x - b.x, p.body.pos.z - b.z).magnitude < ButtonReach + 0.6f;
        }
    }

    // what the view points at: 1 the START button (within reach), 2 the host panel, 0 neither
    public int Aim()
    {
        Transform ct = Game.I.cam.transform;
        Vector3 o = ct.position, f = ct.forward;
        Vector3 bc = Origin + ButtonPos;
        float t = Vector3.Dot(bc - o, f);
        if (t > 0f && t < ButtonReach && (o + f * t - bc).magnitude < ButtonRadius) return 1;
        // the host panel: the plane of its face
        float zf = Origin.z + HostPos.z - 0.14f;
        if (f.z > 0.05f)
        {
            float u = (zf - o.z) / f.z;
            Vector3 h = o + f * u - Origin - HostPos;
            if (u > 0f && u < HostReach && Mathf.Abs(h.x) <= HostHalfW && h.y >= HostY0 && h.y <= HostY1) return 2;
        }
        return 0;
    }

    public void Say(string text) { hint = text; hintUntil = Time.time + 3f; }
    public string Hint { get { return Time.time < hintUntil ? hint : ""; } }

    // ---------------------------------------------------------------- entering and leaving
    public void Enter()
    {
        if (root == null) Build();
        root.SetActive(true);
        panelOpen = false;
        ArrangeBots();
        var g = Game.I;
        g.cam.enabled = true;
        g.player.Activate(Player.ROLE_EXPLORE, Spawn, 0f);
    }

    public void Leave()
    {
        panelOpen = false;
        if (root != null) root.SetActive(false);
    }

    void Update()
    {
        var g = Game.I;
        if (g.state != Game.State.Lobby) return;
        if (panelOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) ClosePanel();
            return;
        }
        // a click (the mouse captured): START starts a round, the host panel opens
        if (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked)
        {
            int aim = Aim();
            if (aim == 1) { AudioHub.I.Play("click_001", 0.6f); TryGenerate(false); }
            else if (aim == 2) OpenPanel();
        }
        if (standSouth != null) { string st = SettingsText(); standSouth.text = st; standNorth.text = st; }
        // the bots look at the stand
        Vector3 b = Origin + StandPos;
        foreach (var bot in bots)
        {
            Vector3 to = b - bot.transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) bot.transform.rotation = Quaternion.Slerp(bot.transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 2f);
        }
    }

    public void OpenPanel()
    {
        panelOpen = true;
        panelOpenedAt = Time.unscaledTime;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        AudioHub.I.Play("open_001", 0.5f);
    }

    public void ClosePanel()
    {
        panelOpen = false;
        AudioHub.I.Play("close_001", 0.5f);
    }

    // the size a round would have now: the host's override, or Auto from the players in the zone (G44)
    public int RoundSize { get { var g = Game.I; return g.sizeAuto ? Balance.SizeFor(Participants) : g.size; } }

    // what START will use, as the stand's screens show it
    string SettingsText()
    {
        var g = Game.I;
        string seed = g.seedText.Trim().Length > 0 ? "seed " + g.seedText.Trim() + (g.seedText.Trim() == g.lastPlayed ? " (the last map again)" : (g.keepSeed ? " (kept)" : " (new)")) : "a new random map";
        string role = g.mode == Game.MODE_RANDOM ? "drawn at random" : (g.mode == Game.MODE_EXPLORE ? "explore" : (g.mode == Game.MODE_HUNT ? "hunter" : "hider"));
        return "START A ROUND\n\n" + seed + "\nsize " + GenData.SizeName[RoundSize] + (g.sizeAuto ? " (auto, " + Participants + " players)" : "") + ", " + GenData.DensityName[g.density]
            + "\n" + Balance.RoundLengthName[g.roundLength] + " round, taunt every " + g.tauntInterval + " s, wrong prop -" + g.wrongPenalty
            + "\nyour role: " + role + "\n\nsettings: the host panel on the north wall";
    }

    // START: only while the player stands in the zone; the round takes the host's settings
    public void TryGenerate(bool acceptOlder)
    {
        if (!PlayerInZone)
        {
            Say("Stand in the green zone to take part: a round starts with the players in it.");
            AudioHub.I.Play("error_004", 0.6f);
            return;
        }
        var g = Game.I;
        g.participants = Participants;
        if (g.sizeAuto && g.size != RoundSize) g.SetSize(RoundSize);
        g.Generate(acceptOlder);
        if (g.state == Game.State.Lobby && g.panelMessage.Length > 0) Say(g.panelMessage);
    }

    // ---------------------------------------------------------------- the bots in the zone (the host panel's
    // "players in the zone", a stand-in for other players) and three watchers at the side walls
    public void SetBots(int n)
    {
        botsInZone = Mathf.Clamp(n, 1, Balance.MaxPlayers - 1);
        ArrangeBots();
    }

    public void ArrangeBots()
    {
        if (root == null) return;
        int want = botsInZone, watchers = Balance.LobbyWatchers;
        while (bots.Count < want + watchers)
        {
            Color32[] shirts = { new Color32(206, 84, 72, 255), new Color32(222, 150, 60, 255), new Color32(150, 90, 170, 255), new Color32(70, 140, 190, 255), new Color32(90, 160, 100, 255) };
            var go = FigureMesh.Build("LobbyBot", shirts[bots.Count % shirts.Length]);
            go.transform.SetParent(root.transform, true);
            bots.Add(go);
        }
        // in the zone on a 1.7 m grid (6 x 6 spots less the four round the stand, 32 in all), filled from the middle
        // out so a small group stands together; the watchers along the side walls
        var spots = new List<Vector3>();
        for (int r = 0; r < 6; r++)
            for (int c = 0; c < 6; c++)
            {
                Vector3 l = new Vector3(-4.25f + c * 1.7f + (r % 2) * 0.3f, 0f, -4.25f + r * 1.7f);
                if (Mathf.Abs(l.x) < 1.5f && Mathf.Abs(l.z) < 1.5f) continue;
                spots.Add(l);
            }
        spots.Sort((a, b) => (a.sqrMagnitude + a.z * 0.01f).CompareTo(b.sqrMagnitude + b.z * 0.01f));
        for (int i = 0; i < bots.Count; i++)
        {
            Vector3 l;
            if (i < want) l = ZoneCentre + spots[i % spots.Count];
            else
            {
                int k = i - want;
                l = new Vector3(k % 2 == 0 ? -RoomW / 2 + 1.6f : RoomW / 2 - 1.6f, 0f, -1f + (k / 2) * 2.2f);
            }
            bots[i].transform.position = Origin + l;
            bots[i].SetActive(i < want + watchers);
        }
    }

    // the lobby room's mesh, kept for the G40 check (Game's "zcheck" command)
    public MeshBuf roomMesh;

    // ---------------------------------------------------------------- the room
    void Build()
    {
        root = new GameObject("Lobby");
        var mb = new MeshBuf();
        roomMesh = mb;
        Color32 floor = new Color32(176, 150, 120, 255), wall = new Color32(214, 206, 190, 255), wallTop = new Color32(180, 172, 160, 255);
        Color32 zone = new Color32(96, 190, 110, 255), edge = new Color32(240, 240, 232, 255), board = new Color32(40, 60, 84, 255), frame = new Color32(120, 86, 60, 255);
        float hw = RoomW / 2, hd = RoomD / 2, t = 0.2f;
        mb.Box(new Vector3(-hw, -0.2f, -hd), new Vector3(hw, 0f, hd), floor);
        // the long walls own the corners; the short ones stop at their faces (G40: no two faces share a plane)
        mb.Box(new Vector3(-hw - t, 0f, -hd - t), new Vector3(hw + t, WallH, -hd), wall, wallTop, wall);
        mb.Box(new Vector3(-hw - t, 0f, hd), new Vector3(hw + t, WallH, hd + t), wall, wallTop, wall);
        mb.Box(new Vector3(-hw - t, 0f, -hd), new Vector3(-hw, WallH, hd), wall, wallTop, wall, MeshBuf.FACE_ALL & ~(MeshBuf.FACE_ZN | MeshBuf.FACE_ZP));
        mb.Box(new Vector3(hw, 0f, -hd), new Vector3(hw + t, WallH, hd), wall, wallTop, wall, MeshBuf.FACE_ALL & ~(MeshBuf.FACE_ZN | MeshBuf.FACE_ZP));
        // the zone: a green square with a white edge, a little above the floor; the edge strips meet end to end
        Vector3 z = ZoneCentre;
        mb.Box(new Vector3(z.x - ZoneHalf, 0f, z.z - ZoneHalf), new Vector3(z.x + ZoneHalf, 0.012f, z.z + ZoneHalf), zone, zone, zone, MeshBuf.FACE_ALL & ~MeshBuf.FACE_YN);
        float e = 0.15f;
        int noBottom = MeshBuf.FACE_ALL & ~MeshBuf.FACE_YN;
        mb.Box(new Vector3(z.x - ZoneHalf - e, 0f, z.z - ZoneHalf - e), new Vector3(z.x + ZoneHalf + e, 0.016f, z.z - ZoneHalf), edge, edge, edge, noBottom);
        mb.Box(new Vector3(z.x - ZoneHalf - e, 0f, z.z + ZoneHalf), new Vector3(z.x + ZoneHalf + e, 0.016f, z.z + ZoneHalf + e), edge, edge, edge, noBottom);
        mb.Box(new Vector3(z.x - ZoneHalf - e, 0f, z.z - ZoneHalf), new Vector3(z.x - ZoneHalf, 0.016f, z.z + ZoneHalf), edge, edge, edge, noBottom & ~(MeshBuf.FACE_ZN | MeshBuf.FACE_ZP));
        mb.Box(new Vector3(z.x + ZoneHalf, 0f, z.z - ZoneHalf), new Vector3(z.x + ZoneHalf + e, 0.016f, z.z + ZoneHalf), edge, edge, edge, noBottom & ~(MeshBuf.FACE_ZN | MeshBuf.FACE_ZP));
        // the sign on the south wall, facing into the room
        Vector3 b = BoardPos;
        mb.Box(new Vector3(b.x - 1.5f, 0.9f, b.z - 0.08f), new Vector3(b.x + 1.5f, 2.5f, b.z + 0.06f), frame);
        mb.Box(new Vector3(b.x - 1.38f, 1.0f, b.z + 0.06f), new Vector3(b.x + 1.38f, 2.4f, b.z + 0.08f), board, board, board, MeshBuf.FACE_ALL & ~MeshBuf.FACE_ZN);
        // the host panel on the north wall, facing the zone
        Vector3 hp = HostPos;
        Color32 hostBoard = new Color32(34, 48, 70, 255);
        mb.Box(new Vector3(hp.x - HostHalfW, HostY0, hp.z - 0.12f), new Vector3(hp.x + HostHalfW, HostY1, hp.z), frame, frame, frame, MeshBuf.FACE_ALL & ~MeshBuf.FACE_ZP);
        mb.Box(new Vector3(hp.x - HostHalfW + 0.12f, HostY0 + 0.12f, hp.z - 0.14f), new Vector3(hp.x + HostHalfW - 0.12f, HostY1 - 0.12f, hp.z - 0.12f), hostBoard, hostBoard, hostBoard, MeshBuf.FACE_ALL & ~MeshBuf.FACE_ZP);
        // the start stand (G39, G43): a pedestal with the START button on top; above it, on two posts, a screen with a
        // face to the south and one to the north
        Vector3 s = StandPos;
        Color32 post = new Color32(70, 74, 80, 255), start = new Color32(70, 190, 90, 255);
        mb.Box(new Vector3(s.x - PedHalfX, 0f, s.z - PedHalfZ), new Vector3(s.x + PedHalfX, PedH, s.z + PedHalfZ), post, new Color32(90, 94, 100, 255), post, MeshBuf.FACE_ALL & ~MeshBuf.FACE_YN);
        Vector3 bp = ButtonPos;
        mb.Box(new Vector3(bp.x - 0.18f, PedH, bp.z - 0.18f), new Vector3(bp.x + 0.18f, bp.y, bp.z + 0.18f), start, new Color32(110, 230, 130, 255), start, MeshBuf.FACE_ALL & ~MeshBuf.FACE_YN);
        for (int k = -1; k <= 1; k += 2)
            mb.Box(new Vector3(s.x + k * 0.45f - 0.03f, PedH, s.z - 0.02f), new Vector3(s.x + k * 0.45f + 0.03f, 1.36f, s.z + 0.02f), post, post, post, MeshBuf.FACE_ALL & ~(MeshBuf.FACE_YN | MeshBuf.FACE_YP));
        mb.Box(new Vector3(s.x - 0.6f, 1.36f, s.z - 0.04f), new Vector3(s.x + 0.6f, 2.06f, s.z + 0.04f), frame, frame, frame, MeshBuf.FACE_ALL & ~(MeshBuf.FACE_ZN | MeshBuf.FACE_ZP));
        mb.Box(new Vector3(s.x - 0.6f, 1.36f, s.z - 0.06f), new Vector3(s.x + 0.6f, 2.06f, s.z - 0.04f), board, board, board, MeshBuf.FACE_ALL & ~MeshBuf.FACE_ZP);
        mb.Box(new Vector3(s.x - 0.6f, 1.36f, s.z + 0.04f), new Vector3(s.x + 0.6f, 2.06f, s.z + 0.06f), board, board, board, MeshBuf.FACE_ALL & ~MeshBuf.FACE_ZN);
        var go = mb.ToObject("LobbyRoom", root.transform, ModelLibrary.lit);
        go.transform.position = Origin;
        // walls and floor stop the walking body like a map's architecture
        AddCollider(new Vector3(-hw - 2f, -1f, -hd - 2f), new Vector3(hw + 2f, 0f, hd + 2f));
        AddCollider(new Vector3(-hw - t, 0f, -hd - t), new Vector3(hw + t, WallH, -hd));
        AddCollider(new Vector3(-hw - t, 0f, hd), new Vector3(hw + t, WallH, hd + t));
        AddCollider(new Vector3(-hw - t, 0f, -hd), new Vector3(-hw, WallH, hd));
        AddCollider(new Vector3(hw, 0f, -hd), new Vector3(hw + t, WallH, hd));
        AddCollider(new Vector3(s.x - PedHalfX, 0f, s.z - PedHalfZ), new Vector3(s.x + PedHalfX, 2.06f, s.z + PedHalfZ));
        // labels: on the zone's floor, the sign, the host panel and the stand
        Label("STAND HERE TO PLAY", ZoneCentre + new Vector3(0f, 0.03f, -ZoneHalf + 0.6f), Quaternion.Euler(90f, 0f, 0f), 0.08f, new Color(1f, 1f, 1f));
        Label("UP TO 32 PLAYERS", ZoneCentre + new Vector3(0f, 0.03f, -ZoneHalf + 1.25f), Quaternion.Euler(90f, 0f, 0f), 0.04f, new Color(0.9f, 1f, 0.9f));
        Quaternion facing = Quaternion.Euler(0f, 180f, 0f);
        Label("PROP HUNT", b + new Vector3(0f, 2.05f, 0.1f), facing, 0.05f, new Color(1f, 0.85f, 0.4f));
        Label("Stand in the green zone to take part", b + new Vector3(0f, 1.62f, 0.1f), facing, 0.022f, new Color(0.7f, 1f, 0.7f));
        Label("START: the stand in the middle of the zone", b + new Vector3(0f, 1.38f, 0.1f), facing, 0.018f, Color.white);
        Label("Settings: the host panel on the far wall", b + new Vector3(0f, 1.16f, 0.1f), facing, 0.018f, Color.white);
        Label("HOST PANEL", hp + new Vector3(0f, 2.55f, -0.16f), Quaternion.identity, 0.045f, new Color(1f, 0.85f, 0.4f));
        Label("Host: you", hp + new Vector3(0f, 2.15f, -0.16f), Quaternion.identity, 0.03f, Color.white);
        Label("map, size, rules, run cap", hp + new Vector3(0f, 1.75f, -0.16f), Quaternion.identity, 0.024f, new Color(0.8f, 0.88f, 1f));
        Label("point here and click to change", hp + new Vector3(0f, 1.3f, -0.16f), Quaternion.identity, 0.02f, new Color(0.7f, 0.8f, 0.9f));
        standSouth = Label(SettingsText(), s + new Vector3(0f, 1.71f, -0.07f), Quaternion.identity, 0.0072f, Color.white);
        standNorth = Label(SettingsText(), s + new Vector3(0f, 1.71f, 0.07f), facing, 0.0072f, Color.white);
        Label("START", bp + new Vector3(0f, 0.012f, 0f), Quaternion.Euler(90f, 0f, 0f), 0.012f, Color.white);
        // benches and plants for those who watch
        Prop("loungeSofa", new Vector3(-hw + 0.6f, 0f, 6.5f), 90f, 2f);
        Prop("loungeSofa", new Vector3(hw - 0.6f, 0f, 6.5f), -90f, 2f);
        Prop("pottedPlant", new Vector3(-hw + 0.6f, 0f, -hd + 0.6f), 0f, 2f);
        Prop("pottedPlant", new Vector3(hw - 0.6f, 0f, -hd + 0.6f), 0f, 2f);
    }

    void AddCollider(Vector3 mn, Vector3 mx)
    {
        var go = new GameObject("LobbyCollider");
        go.transform.SetParent(root.transform, false);
        go.transform.position = Origin;
        go.layer = Layers.Arch;
        var bc = go.AddComponent<BoxCollider>();
        bc.center = (mn + mx) * 0.5f; bc.size = mx - mn;
    }

    TextMesh Label(string text, Vector3 local, Quaternion rot, float size, Color col)
    {
        var go = new GameObject("LobbyLabel");
        go.transform.SetParent(root.transform, false);
        go.transform.position = Origin + local;
        // a text mesh is read from -z: rot turns it to face into the room (the board) or up (the floor)
        go.transform.rotation = rot;
        var tm = go.AddComponent<TextMesh>();
        var font = Resources.Load<Font>("PropHunt/Fonts/KenneyFuture");
        if (font != null) { tm.font = font; go.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
        tm.text = text; tm.characterSize = size; tm.fontSize = 48; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = col;
        return tm;
    }

    void Prop(string model, Vector3 local, float yaw, float scale)
    {
        int m = ModelLibrary.Find(model);
        if (m < 0) return;
        var go = new GameObject("LobbyProp");
        go.transform.SetParent(root.transform, false);
        go.transform.position = Origin + local;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.mesh[m];
        go.AddComponent<MeshRenderer>().sharedMaterial = ModelLibrary.lit;
        go.layer = Layers.Solid;
        var bc = go.AddComponent<BoxCollider>();
        bc.center = ModelLibrary.bounds[m].center; bc.size = ModelLibrary.bounds[m].size;
    }
}
