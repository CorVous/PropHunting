using System.Collections.Generic;
using UnityEngine;

// The lobby (owner decisions G33, G43, G50, G51): the room the player starts in, away from any map.
//   the zone (G43, G51): an 11 x 11 m green square right against the north wall, big enough for a full instance of
//     32 players standing about 1.9 m apart (121 m2); whoever stands in it takes part in the next round
//   the host panel on that wall (G50, G51, G55; HostWall.cs, a world-space UI canvas): every setting and START, on the wall and
//     used in place by the host (point and click; in VR point and pull the trigger, G38); everyone else reads it.
//     START only works while the player stands in the zone.
//   the sign on the south wall, by the way in, says where everything is.
// Idle bot figures stand in for the other players: those in the zone play, those at the walls watch. In the VRChat
// world the zone is a trigger volume and the players in it are the round's players.
// ?seed=... in the URL still skips the lobby and goes straight into a map (for testing).
public class Lobby : MonoBehaviour
{
    public static Lobby I;

    // the lobby stands well away from the maps, which are built from the origin towards +x and +z
    public static readonly Vector3 Origin = new Vector3(-120f, 0f, -120f);
    const float RoomW = 24f, RoomD = 22f, WallH = 5.5f;
    // the zone: a square on the floor in the middle of the room
    const float ZoneHalf = 5.5f;
    static readonly Vector3 ZoneCentre = new Vector3(0f, 0f, RoomD / 2 - 0.15f - ZoneHalf);   // its edge strip meets the wall
    static readonly Vector3 BoardPos = new Vector3(0f, 0f, -RoomD / 2 + 0.25f);   // the sign on the south wall
    // the host panel on the north wall, its board's face 14 cm off the wall
    static readonly Vector3 HostPos = new Vector3(0f, 0f, RoomD / 2);
    HostWall hostWall;
    public int botsInZone = Balance.LobbyBotsInZone[0];
    readonly List<GameObject> bots = new List<GameObject>();
    GameObject root;
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

    // what the view points at: 2 a button of the host panel, 0 nothing (only ever 2 for the host)
    public int Aim() { return hostWall != null && hostWall.Owns(CentrePointerModule.Hovered) ? 2 : 0; }

    public void Say(string text) { hint = text; hintUntil = Time.time + 3f; }
    public string Hint { get { return Time.time < hintUntil ? hint : ""; } }

    // ---------------------------------------------------------------- entering and leaving
    public void Enter()
    {
        if (root == null) Build();
        root.SetActive(true);
        ArrangeBots();
        var g = Game.I;
        g.cam.enabled = true;
        g.player.Activate(Player.ROLE_EXPLORE, Spawn, 0f);
    }

    public void Leave()
    {
        if (root != null) root.SetActive(false);
    }

    void Update()
    {
        var g = Game.I;
        if (g.state != Game.State.Lobby) return;
        // the host panel's state; its buttons are pressed through the EventSystem (CentrePointerModule)
        if (hostWall != null) hostWall.Tick();
        // the bots look at the panel
        Vector3 b = Origin + new Vector3(0f, 0f, RoomD / 2);
        foreach (var bot in bots)
        {
            Vector3 to = b - bot.transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) bot.transform.rotation = Quaternion.Slerp(bot.transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 2f);
        }
    }

    // test hook: turn the view to a button of the host wall (the click itself is then real input)
    public string AimAt(string name)
    {
        float yaw, pitch;
        if (hostWall == null || !hostWall.AimAt(name, Game.I.player.EyePos, out yaw, out pitch)) return "no button '" + name + "'; buttons: " + (hostWall != null ? hostWall.Names() : "none");
        Game.I.player.SetLook(yaw, pitch);
        return "aimed at " + name;
    }

    // the size a round would have now: the host's override, or Auto from the players in the zone (G44)
    public int RoundSize { get { var g = Game.I; return g.sizeAuto ? Balance.SizeFor(Participants) : g.size; } }

    // START: only while the player stands in the zone; the round takes the host's settings
    public void TryGenerate(bool acceptOlder)
    {
        if (!Game.I.isHost) return;   // a guest's press does nothing (G51)
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
        // in the zone on a 1.7 m grid (6 x 6 spots, 31 used), filled from the middle out, so a small group stands
        // together and leaves the panel in view; the watchers along the side walls
        var spots = new List<Vector3>();
        for (int r = 0; r < 6; r++)
            for (int c = 0; c < 6; c++)
            {
                Vector3 l = new Vector3(-4.25f + c * 1.7f + (r % 2) * 0.3f, 0f, -4.25f + r * 1.7f);
                spots.Add(l);
            }
        spots.Sort((a, b) => (a.sqrMagnitude + a.x * 0.01f).CompareTo(b.sqrMagnitude + b.x * 0.01f));
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
        // the host panel on the north wall, facing the zone: a board for HostWall's buttons
        Vector3 hp = HostPos;
        Color32 hostBoard = new Color32(34, 48, 70, 255);
        float hw2 = HostWall.HalfW + 0.25f;
        mb.Box(new Vector3(hp.x - hw2, HostWall.Y0 - 0.12f, hp.z - 0.12f), new Vector3(hp.x + hw2, HostWall.Y1, hp.z), frame, frame, frame, MeshBuf.FACE_ALL & ~MeshBuf.FACE_ZP);
        mb.Box(new Vector3(hp.x - hw2 + 0.12f, HostWall.Y0, hp.z - 0.14f), new Vector3(hp.x + hw2 - 0.12f, HostWall.Y1 - 0.12f, hp.z - 0.12f), hostBoard, hostBoard, hostBoard, MeshBuf.FACE_ALL & ~MeshBuf.FACE_ZP);
        var go = mb.ToObject("LobbyRoom", root.transform, ModelLibrary.lit);
        go.transform.position = Origin;
        // walls and floor stop the walking body like a map's architecture
        AddCollider(new Vector3(-hw - 2f, -1f, -hd - 2f), new Vector3(hw + 2f, 0f, hd + 2f));
        AddCollider(new Vector3(-hw - t, 0f, -hd - t), new Vector3(hw + t, WallH, -hd));
        AddCollider(new Vector3(-hw - t, 0f, hd), new Vector3(hw + t, WallH, hd + t));
        AddCollider(new Vector3(-hw - t, 0f, -hd), new Vector3(-hw, WallH, hd));
        AddCollider(new Vector3(hw, 0f, -hd), new Vector3(hw + t, WallH, hd));
        // labels: on the zone's floor, the sign, the host panel and the stand
        Label("STAND HERE TO PLAY", ZoneCentre + new Vector3(0f, 0.03f, -ZoneHalf + 0.6f), Quaternion.Euler(90f, 0f, 0f), 0.08f, new Color(1f, 1f, 1f));
        Label("UP TO 32 PLAYERS", ZoneCentre + new Vector3(0f, 0.03f, -ZoneHalf + 1.25f), Quaternion.Euler(90f, 0f, 0f), 0.04f, new Color(0.9f, 1f, 0.9f));
        Quaternion facing = Quaternion.Euler(0f, 180f, 0f);
        Label("PROP HUNT", b + new Vector3(0f, 2.05f, 0.1f), facing, 0.05f, new Color(1f, 0.85f, 0.4f));
        Label("Stand in the green zone to take part", b + new Vector3(0f, 1.62f, 0.1f), facing, 0.022f, new Color(0.7f, 1f, 0.7f));
        Label("Settings and START: the host panel", b + new Vector3(0f, 1.38f, 0.1f), facing, 0.018f, Color.white);
        Label("on the far wall, above the zone", b + new Vector3(0f, 1.16f, 0.1f), facing, 0.018f, Color.white);
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>().sendNavigationEvents = false;
            es.AddComponent<CentrePointerModule>();
        }
        hostWall = new HostWall();
        hostWall.Build(root.transform, Origin + hp + new Vector3(0f, 0f, -0.14f));
        // benches and plants for those who watch
        Prop("loungeSofa", new Vector3(-hw + 0.6f, 0f, 0f), 90f, 2f);
        Prop("loungeSofa", new Vector3(hw - 0.6f, 0f, 0f), -90f, 2f);
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

    // a text in the room (G55: TextMeshPro on a world-space canvas, like the host panel); rot turns it to face into
    // the room (the sign) or up (the floor)
    void Label(string text, Vector3 local, Quaternion rot, float size, Color col)
    {
        HostWall.WorldLabel(root.transform, text, Origin + local, rot, size, col);
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
