using System.Collections.Generic;
using UnityEngine;

// The lobby (owner decision G33): the room the player starts in, away from any map. A board on the wall holds the
// start panel (walk up to it, look at it and click: pointing and pulling the trigger in VR, G38): seed, size,
// density, role, Generate. A zone is marked on the floor;
// whoever stands in it takes part in the next round, and Generate only starts a round while the player stands in
// it too. Idle bot figures show the idea: those in the zone play, those outside watch. In the VRChat world the
// zone is a trigger volume and the players in it are the round's players; here the bots stand in for them.
// ?seed=... in the URL still skips the lobby and goes straight into a map (for testing).
public class Lobby : MonoBehaviour
{
    public static Lobby I;

    // the lobby stands well away from the maps, which are built from the origin towards +x and +z
    public static readonly Vector3 Origin = new Vector3(-120f, 0f, -120f);
    const float RoomW = 18f, RoomD = 14f, WallH = 3.2f;
    // the zone: a square on the floor in the middle of the room
    static readonly Vector3 ZoneCentre = new Vector3(0f, 0f, 1.5f);
    const float ZoneHalf = 2.6f;
    static readonly Vector3 BoardPos = new Vector3(0f, 0f, -RoomD / 2 + 0.25f);   // on the south wall

    public bool panelOpen;
    public int botsInZone;
    readonly List<GameObject> bots = new List<GameObject>();
    readonly List<bool> botIn = new List<bool>();
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

    public bool NearBoard
    {
        get
        {
            var p = Game.I.player;
            Vector3 b = Origin + BoardPos;
            return p.active && new Vector2(p.body.pos.x - b.x, p.body.pos.z - b.z).magnitude < 2.6f;
        }
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
        // a click on the board (the mouse captured, the view on the board) opens it
        if (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked && NearBoard)
        {
            Vector3 to = Origin + BoardPos + Vector3.up * 1.6f - g.cam.transform.position;
            if (Vector3.Dot(to.normalized, g.cam.transform.forward) > 0.75f) OpenPanel();
        }
        // the bots look at the board
        Vector3 b = Origin + BoardPos;
        foreach (var bot in bots)
        {
            Vector3 to = b - bot.transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) bot.transform.rotation = Quaternion.Slerp(bot.transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 2f);
        }
    }

    public void OpenPanel()
    {
        panelOpen = true;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        AudioHub.I.Play("open_001", 0.5f);
    }

    public void ClosePanel()
    {
        panelOpen = false;
        AudioHub.I.Play("close_001", 0.5f);
    }

    // Generate from the board: only while the player stands in the zone
    public void TryGenerate(bool acceptOlder)
    {
        if (!PlayerInZone)
        {
            Say("Stand in the green zone to take part: a round starts with the players in it.");
            AudioHub.I.Play("error_004", 0.6f);
            return;
        }
        Game.I.participants = Participants;
        Game.I.Generate(acceptOlder);
    }

    // ---------------------------------------------------------------- the bots: how many stand in the zone follows the map size
    public void ArrangeBots()
    {
        if (root == null) return;
        int want = Balance.LobbyBotsInZone[Game.I.size], watchers = Balance.LobbyWatchers;
        while (bots.Count < want + watchers)
        {
            Color32[] shirts = { new Color32(206, 84, 72, 255), new Color32(222, 150, 60, 255), new Color32(150, 90, 170, 255), new Color32(70, 140, 190, 255), new Color32(90, 160, 100, 255) };
            var go = FigureMesh.Build("LobbyBot", shirts[bots.Count % shirts.Length]);
            go.transform.SetParent(root.transform, true);
            bots.Add(go); botIn.Add(false);
        }
        // a fixed scatter: in the zone on a grid with a little offset, the watchers along the side walls
        botsInZone = 0;
        for (int i = 0; i < bots.Count; i++)
        {
            bool inside = i < want;
            Vector3 l;
            if (inside)
            {
                int col = i % 4, row = i / 4;
                l = ZoneCentre + new Vector3(-1.95f + col * 1.3f + (row % 2) * 0.4f, 0f, 1.6f - row * 1.15f);
            }
            else
            {
                int k = i - want;
                l = new Vector3(k % 2 == 0 ? -RoomW / 2 + 1.6f : RoomW / 2 - 1.6f, 0f, -1f + (k / 2) * 2.2f);
            }
            bots[i].transform.position = Origin + l;
            bots[i].SetActive(i < want + watchers);
            botIn[i] = inside;
            if (inside) botsInZone++;
        }
    }

    // ---------------------------------------------------------------- the room
    void Build()
    {
        root = new GameObject("Lobby");
        var mb = new MeshBuf();
        Color32 floor = new Color32(176, 150, 120, 255), wall = new Color32(214, 206, 190, 255), wallTop = new Color32(180, 172, 160, 255);
        Color32 zone = new Color32(96, 190, 110, 255), edge = new Color32(240, 240, 232, 255), board = new Color32(40, 60, 84, 255), frame = new Color32(120, 86, 60, 255);
        float hw = RoomW / 2, hd = RoomD / 2, t = 0.2f;
        mb.Box(new Vector3(-hw, -0.2f, -hd), new Vector3(hw, 0f, hd), floor);
        mb.Box(new Vector3(-hw - t, 0f, -hd - t), new Vector3(hw + t, WallH, -hd), wall, wallTop, wall);
        mb.Box(new Vector3(-hw - t, 0f, hd), new Vector3(hw + t, WallH, hd + t), wall, wallTop, wall);
        mb.Box(new Vector3(-hw - t, 0f, -hd), new Vector3(-hw, WallH, hd), wall, wallTop, wall);
        mb.Box(new Vector3(hw, 0f, -hd), new Vector3(hw + t, WallH, hd), wall, wallTop, wall);
        // the zone: a green square with a white edge, a hand's breadth above the floor so it never flickers
        Vector3 z = ZoneCentre;
        mb.Box(new Vector3(z.x - ZoneHalf, 0f, z.z - ZoneHalf), new Vector3(z.x + ZoneHalf, 0.012f, z.z + ZoneHalf), zone);
        float e = 0.12f;
        mb.Box(new Vector3(z.x - ZoneHalf - e, 0f, z.z - ZoneHalf - e), new Vector3(z.x + ZoneHalf + e, 0.016f, z.z - ZoneHalf), edge);
        mb.Box(new Vector3(z.x - ZoneHalf - e, 0f, z.z + ZoneHalf), new Vector3(z.x + ZoneHalf + e, 0.016f, z.z + ZoneHalf + e), edge);
        mb.Box(new Vector3(z.x - ZoneHalf - e, 0f, z.z - ZoneHalf), new Vector3(z.x - ZoneHalf, 0.016f, z.z + ZoneHalf), edge);
        mb.Box(new Vector3(z.x + ZoneHalf, 0f, z.z - ZoneHalf), new Vector3(z.x + ZoneHalf + e, 0.016f, z.z + ZoneHalf), edge);
        // the board on the south wall, facing into the room
        Vector3 b = BoardPos;
        mb.Box(new Vector3(b.x - 1.5f, 0.9f, b.z - 0.08f), new Vector3(b.x + 1.5f, 2.5f, b.z + 0.06f), frame);
        mb.Box(new Vector3(b.x - 1.38f, 1.0f, b.z + 0.06f), new Vector3(b.x + 1.38f, 2.4f, b.z + 0.08f), board);
        var go = mb.ToObject("LobbyRoom", root.transform, ModelLibrary.lit);
        go.transform.position = Origin;
        // walls and floor stop the walking body like a map's architecture
        AddCollider(new Vector3(-hw - 2f, -1f, -hd - 2f), new Vector3(hw + 2f, 0f, hd + 2f));
        AddCollider(new Vector3(-hw - t, 0f, -hd - t), new Vector3(hw + t, WallH, -hd));
        AddCollider(new Vector3(-hw - t, 0f, hd), new Vector3(hw + t, WallH, hd + t));
        AddCollider(new Vector3(-hw - t, 0f, -hd), new Vector3(-hw, WallH, hd));
        AddCollider(new Vector3(hw, 0f, -hd), new Vector3(hw + t, WallH, hd));
        // labels: on the zone's floor and on the board
        Label("STAND HERE TO PLAY", ZoneCentre + new Vector3(0f, 0.03f, -ZoneHalf + 0.5f), Quaternion.Euler(90f, 0f, 0f), 0.06f, new Color(1f, 1f, 1f));
        Quaternion facing = Quaternion.Euler(0f, 180f, 0f);
        Label("PROP HUNT", b + new Vector3(0f, 2.05f, 0.1f), facing, 0.05f, new Color(1f, 0.85f, 0.4f));
        Label("Click here: map, size, role", b + new Vector3(0f, 1.6f, 0.1f), facing, 0.022f, Color.white);
        Label("Stand in the green zone to take part", b + new Vector3(0f, 1.35f, 0.1f), facing, 0.018f, new Color(0.7f, 1f, 0.7f));
        // a few benches for those who watch
        Prop("loungeSofa", new Vector3(-hw + 0.6f, 0f, 3.5f), 90f, 2f);
        Prop("loungeSofa", new Vector3(hw - 0.6f, 0f, 3.5f), -90f, 2f);
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

    void Label(string text, Vector3 local, Quaternion rot, float size, Color col)
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
