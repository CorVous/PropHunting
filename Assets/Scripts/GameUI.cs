using UnityEngine;

// All screens, drawn with Unity's immediate-mode GUI: the lobby (its hints and the prototype's aids; the host's
// settings are on its wall, HostWall.cs), the progress bar, the in-round display, the help panel, the Esc menu, the
// debug overlay and the round result.
public class GameUI : MonoBehaviour
{
    public static GameUI I;
    void Awake() { I = this; }

    GUIStyle label, small, title, heading, box, button, bigButton, field, centre, bigCentre, toggleOn;
    Texture2D white, panelTex, darkTex, btnTex, btnHoverTex, btnOnTex, fieldTex;
    Font kenney;
    bool stylesReady;
    float W, H;

    // prop counts at Normal density, from the generator console test (they scale with the density setting)
    static readonly string[] SizeNote =
    {
        "S: a house and a second plot on a lane, a wood; about 43 cells, 165 props at Normal density. For up to 8 players.",
        "M: three plots (a house, then a mix of houses, a diner, warehouse, shop, garage, farm, park, zoo or log cabin), about 75 cells, 275 props. For 9 to 16 players.",
        "L: four plots, about 119 cells, 435 props. For 17 to 24 players.",
        "XL: five plots, about 154 cells, 570 props. For 25 to 32 players.",
    };
    static readonly string[] ModeName = { "Explore", "Hunter", "Hider", "Drawn at random" };
    static readonly int[] ModeOrder = { 3, 1, 2, 0 };
    static readonly string[] ModeNote =
    {
        "Walk the map freely. Look at a prop and click to become it; the wrist panel (Esc) turns you back.",
        "You hunt, with the other hunters (bots). Bot hiders are disguised as props: find them with the dart tagger. Wrong props cost health.",
        "You hide, with the other hiders (bots). Pick a prop, find a spot, lock in place. Bot hunters come looking after the hide phase.",
        "As in the game: about 1 in 4 of the players in the zone hunt, drawn at random every round; everyone else hides.",
    };

    static string ZoneCells(MapGen gen)
    {
        string s = "";
        for (int z = GenData.Z_HOUSE; z < GenData.ZoneName.Length; z++)
            if (z != GenData.Z_RELEASE && gen.statZoneCells[z] > 0) s += (s.Length > 0 ? ", " : "") + GenData.ZoneName[z] + " " + gen.statZoneCells[z];
        return s;
    }

    static Texture2D Tex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c); t.Apply();
        return t;
    }

    void Styles()
    {
        if (stylesReady) return;
        stylesReady = true;
        kenney = Resources.Load<Font>("PropHunt/Fonts/KenneyFuture");
        white = Tex(Color.white);
        panelTex = Tex(new Color(0.10f, 0.12f, 0.15f, 0.88f));
        darkTex = Tex(new Color(0.03f, 0.04f, 0.05f, 0.94f));
        btnTex = Tex(new Color(0.24f, 0.29f, 0.36f, 1f));
        btnHoverTex = Tex(new Color(0.32f, 0.39f, 0.48f, 1f));
        btnOnTex = Tex(new Color(0.30f, 0.58f, 0.36f, 1f));
        fieldTex = Tex(new Color(0.05f, 0.06f, 0.08f, 1f));
        Color text = new Color(0.93f, 0.94f, 0.95f);
        label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = true };
        label.normal.textColor = text;
        small = new GUIStyle(label) { fontSize = 13 };
        small.normal.textColor = new Color(0.80f, 0.83f, 0.86f);
        heading = new GUIStyle(label) { fontSize = 18, fontStyle = FontStyle.Bold };
        title = new GUIStyle(label) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
        if (kenney != null) title.font = kenney;
        centre = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
        bigCentre = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 26, fontStyle = FontStyle.Bold };
        box = new GUIStyle(GUI.skin.box);
        box.normal.background = panelTex;
        box.border = new RectOffset(0, 0, 0, 0);
        button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
        button.normal.background = btnTex; button.hover.background = btnHoverTex; button.active.background = btnOnTex;
        button.normal.textColor = text; button.hover.textColor = Color.white; button.active.textColor = Color.white;
        button.border = new RectOffset(0, 0, 0, 0);
        toggleOn = new GUIStyle(button);
        toggleOn.normal.background = btnOnTex; toggleOn.hover.background = btnOnTex;
        bigButton = new GUIStyle(button) { fontSize = 20, fontStyle = FontStyle.Bold };
        field = new GUIStyle(GUI.skin.textField) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
        field.normal.background = fieldTex; field.focused.background = fieldTex; field.hover.background = fieldTex;
        field.normal.textColor = text; field.focused.textColor = Color.white; field.hover.textColor = text;
        field.padding = new RectOffset(8, 8, 4, 4);
    }

    // "k_chairCushion" -> "Chair cushion"
    public static string DefName(int d)
    {
        string id = GenData.defId[d];
        int us = id.IndexOf('_');
        if (us >= 0 && us <= 4) id = id.Substring(us + 1);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < id.Length; i++)
        {
            char c = id[i];
            if (c == '_') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(id[i - 1])) sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    void Fill(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, white);
        GUI.color = old;
    }

    public bool hidden;   // for clean screenshots in the build checks

    void OnGUI()
    {
        if (hidden) return;
        Styles();
        float s = Mathf.Max(0.7f, Screen.height / 860f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        W = Screen.width / s; H = Screen.height / s;
        var g = Game.I;
        if (g.state == Game.State.StartPanel) StartPanel();
        else if (g.state == Game.State.Lobby) LobbyScreen();
        else if (g.state == Game.State.Generating) Progress();
        else Playing();
    }

    // ---------------------------------------------------------------- lobby (G33)
    void LobbyScreen()
    {
        var g = Game.I;
        var lb = Lobby.I;
        int n = lb.Participants, hunters = Balance.Hunters(n);
        bool inZone = lb.PlayerInZone;
        Rect r = new Rect(12, 12, 560, 128);
        GUI.Box(r, GUIContent.none, box);
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 8, r.width - 28, r.height - 12));
        GUILayout.Label("Lobby", heading);
        GUILayout.Label("Whoever stands in the green zone takes part in the next round. The panel on the wall above the zone holds the settings "
            + "and START; the host points at a button and clicks. Esc: the prototype's aids.", small);
        GUILayout.Label("In the zone: " + (inZone ? "<b>you</b> and " : "") + lb.botsInZone + " bots" + (inZone ? "" : " (step in to play)")
            + ". A round of " + n + " players has " + hunters + " hunter" + (hunters == 1 ? "" : "s") + " (1 in 4, drawn at random).", label);
        GUILayout.EndArea();
        string tip = lb.Hint.Length > 0 ? lb.Hint : (Cursor.lockState != CursorLockMode.Locked ? "Click to capture the mouse, then W A S D to walk" : (lb.Aim() == 2 && g.isHost ? "Click to press this button" : ""));
        if (tip.Length > 0)
        {
            Fill(new Rect(W / 2 - 320, H - 120, 640, 34), new Color(0.04f, 0.05f, 0.07f, 0.8f));
            GUI.Label(new Rect(W / 2 - 320, H - 118, 640, 30), tip, centre);
        }
        if (inZone)
        {
            Fill(new Rect(W / 2 - 200, 20, 400, 34), new Color(0.15f, 0.45f, 0.20f, 0.85f));
            GUI.Label(new Rect(W / 2 - 200, 22, 400, 30), "You are in the zone: you take part", centre);
        }
        LobbyAids();
    }

    // ---------------------------------------------------------------- the lobby's prototype aids (G50)
    // The host's settings are on the lobby wall (HostWall.cs). What is only the prototype's (your role, the number of
    // bot players in the zone, typing a seed code, volume, mouse speed) sits here, on the screen, while the mouse is
    // free (Esc); a click outside it captures the mouse again.
    public Rect LobbyAidsRect { get { return Cursor.lockState == CursorLockMode.Locked ? new Rect(0, 0, 0, 0) : new Rect(12, 150, 470, 362); } }

    void LobbyAids()
    {
        var g = Game.I;
        var lb = Lobby.I;
        Rect r = LobbyAidsRect;
        if (r.width <= 0f) return;
        GUI.Box(r, GUIContent.none, box);
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 8, r.width - 28, r.height - 12));
        GUILayout.Label("Prototype aids (not part of the game)", heading);
        if (GUILayout.Button(g.isHost ? "You are the host: yes (click: see the panel as a guest)" : "You are the host: no (click: be the host)", g.isHost ? toggleOn : button, GUILayout.Height(26))) { g.isHost = !g.isHost; AudioHub.I.Play("select_001", 0.5f); }
        GUILayout.Label("Your role", small);
        GUILayout.BeginHorizontal();
        foreach (int i in ModeOrder)
            if (GUILayout.Button(ModeName[i], g.mode == i ? toggleOn : button, GUILayout.Height(28))) { g.mode = i; AudioHub.I.Play("select_001", 0.5f); }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Players in the zone", small, GUILayout.Width(130));
        if (GUILayout.Button("-", button, GUILayout.Width(40), GUILayout.Height(26))) lb.SetBots(lb.botsInZone - 1);
        GUILayout.Label("<b>" + lb.Participants + "</b> (you and " + lb.botsInZone + " bots)", small, GUILayout.Width(150));
        if (GUILayout.Button("+", button, GUILayout.Width(40), GUILayout.Height(26))) lb.SetBots(lb.botsInZone + 1);
        GUILayout.EndHorizontal();
        GUILayout.Label("Type a seed code (in VR the system keyboard); the host wall's New map makes a random one", small);
        GUI.SetNextControlName("seed");
        string typed = GUILayout.TextField(g.seedText, 24, field, GUILayout.Height(30));
        if (typed != g.seedText) { g.seedText = typed; g.keepSeed = true; }   // a seed typed in is kept
        if (g.panelMessage.Length > 0)
        {
            GUILayout.Label("<color=#ffb060>" + g.panelMessage + "</color>", small);
            if (g.remixOffered && GUILayout.Button("Use it as a remix on generator v" + GenData.GEN_VERSION, button, GUILayout.Height(26)))
            {
                int v, sz; uint sd;
                SeedCode.Decode(g.seedText, GenData.GEN_VERSION, out v, out sz, out sd);
                g.seedText = SeedCode.Encode(GenData.GEN_VERSION, sz, sd, g.density); g.panelMessage = ""; g.remixOffered = false;
            }
        }
        Sliders();
        GUILayout.EndArea();
    }

    // ---------------------------------------------------------------- start panel (when a map could not be made outside the lobby)
    void StartPanel()
    {
        var g = Game.I;
        bool lobby = g.state == Game.State.Lobby;
        if (!lobby) Fill(new Rect(0, 0, W, H), new Color(0.13f, 0.17f, 0.20f, 1f));
        else Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.35f));
        float pw = 680f, ph = 790f;
        Rect r = new Rect((W - pw) / 2, Mathf.Max(8f, (H - ph) / 2), pw, ph);
        GUI.Box(r, GUIContent.none, box);
        GUILayout.BeginArea(new Rect(r.x + 26, r.y + 16, r.width - 52, r.height - 28));
        GUILayout.Label("PROP HUNT", title, GUILayout.Height(44));
        GUILayout.Label("Browser prototype: generated maps, disguise as a prop, hunt bot hiders. Single player, mouse and keyboard.", small);
        GUILayout.Space(10);

        GUILayout.Label("Map seed (share this code to replay the same map)", heading);
        GUILayout.BeginHorizontal();
        GUI.SetNextControlName("seed");
        g.seedText = GUILayout.TextField(g.seedText, 24, field, GUILayout.Height(34), GUILayout.Width(330));
        if (GUILayout.Button("New random seed", button, GUILayout.Height(34))) { g.NewRandomSeed(); AudioHub.I.Play("click_001", 0.5f); }
        GUILayout.EndHorizontal();
        GUILayout.Label("Generator version " + GenData.GEN_VERSION + ". The code holds the version, the size, the prop density and the seed.", small);
        GUILayout.Space(8);

        GUILayout.Label("Map size", heading);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < 4; i++)
            if (GUILayout.Button(GenData.SizeName[i], g.size == i ? toggleOn : button, GUILayout.Height(32))) { g.SetSize(i); AudioHub.I.Play("select_001", 0.5f); }
        GUILayout.EndHorizontal();
        GUILayout.Label(SizeNote[g.size], small);
        GUILayout.Space(8);

        GUILayout.Label("Prop density", heading);
        GUILayout.BeginHorizontal();
        for (int k = 0; k < GenData.DensityOrder.Length; k++)
        {
            int d = GenData.DensityOrder[k];
            if (GUILayout.Button(GenData.DensityName[d], g.density == d ? toggleOn : button, GUILayout.Height(32))) { g.SetDensity(d); AudioHub.I.Play("select_001", 0.5f); }
        }
        GUILayout.EndHorizontal();
        GUILayout.Label(GenData.DensityName[g.density] + ": every room type's prop range at " + GenData.DensityPercent[g.density] + "%. Same seed, same buildings; only how much stands in them changes. F3 in the map shows the counts per room type.", small);
        GUILayout.Space(8);

        GUILayout.Label("Your role", heading);
        GUILayout.BeginHorizontal();
        foreach (int i in ModeOrder)
            if (GUILayout.Button(ModeName[i], g.mode == i ? toggleOn : button, GUILayout.Height(32))) { g.mode = i; AudioHub.I.Play("select_001", 0.5f); }
        GUILayout.EndHorizontal();
        GUILayout.Label(ModeNote[g.mode], small, GUILayout.Height(36));
        GUILayout.Space(8);

        bool inZone = !lobby || Lobby.I.PlayerInZone;
        if (GUILayout.Button(inZone ? "Generate map and start" : "Generate map (stand in the zone first)", bigButton, GUILayout.Height(48)))
        {
            if (lobby) Lobby.I.TryGenerate(false); else g.Generate(false);
        }
        if (lobby && Lobby.I.Hint.Length > 0) GUILayout.Label("<color=#ffb060>" + Lobby.I.Hint + "</color>", label);
        if (g.panelMessage.Length > 0)
        {
            GUILayout.Label("<color=#ffb060>" + g.panelMessage + "</color>", label);
            if (g.remixOffered && GUILayout.Button("Play as a remix on generator v" + GenData.GEN_VERSION, button, GUILayout.Height(30))) { if (lobby) Lobby.I.TryGenerate(true); else g.Generate(true); }
        }
        else GUILayout.Label("Nothing is generated until you press Generate; every new map needs a new press. "
            + (lobby ? Lobby.I.Participants + " players in the zone take part." : ""), small);
        GUILayout.Space(8);

        Sliders();
        GUILayout.Space(6);
        GUILayout.Label("Controls stand in for VR: W A S D and the mouse for the sticks and the head, Space for the jump button, a click for pointing and the trigger, Esc for looking at the wrist panel (lock, taunt, views, the third-person screen, the menu).", small);
        GUILayout.Label("Models, sounds and fonts: Kenney (kenney.nl) and KayKit (Kay Lousberg), CC0. Sounds are stand-ins; there is no music yet.", small);
        GUILayout.EndArea();
    }

    void Sliders()
    {
        var g = Game.I;
        GUILayout.BeginHorizontal();
        GUILayout.Label("Volume " + Mathf.RoundToInt(AudioHub.I.volume * 100) + "%", small, GUILayout.Width(110));
        float v = GUILayout.HorizontalSlider(AudioHub.I.volume, 0f, 1f, GUILayout.Width(170), GUILayout.Height(18));
        if (!Mathf.Approximately(v, AudioHub.I.volume)) AudioHub.I.SetVolume(v);
        GUILayout.Space(20);
        GUILayout.Label("Mouse speed " + g.mouseSensitivity.ToString("0.0"), small, GUILayout.Width(120));
        g.mouseSensitivity = GUILayout.HorizontalSlider(g.mouseSensitivity, 0.2f, 3f, GUILayout.Width(170), GUILayout.Height(18));
        GUILayout.EndHorizontal();
    }

    // ---------------------------------------------------------------- progress
    void Progress()
    {
        var g = Game.I;
        Fill(new Rect(0, 0, W, H), new Color(0.13f, 0.17f, 0.20f, 1f));
        float bw = 520f;
        Rect r = new Rect((W - bw) / 2, H / 2 - 60, bw, 120);
        GUI.Box(r, GUIContent.none, box);
        GUI.Label(new Rect(r.x, r.y + 10, bw, 30), "Building map " + g.seedCode + "  (" + GenData.SizeName[g.size] + ")", centre);
        Rect bar = new Rect(r.x + 30, r.y + 50, bw - 60, 22);
        Fill(bar, new Color(0.05f, 0.06f, 0.08f, 1f));
        Fill(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * Mathf.Clamp01(g.map.progress), bar.height - 4), new Color(0.40f, 0.76f, 0.45f, 1f));
        GUI.Label(new Rect(r.x, r.y + 80, bw, 26), g.map.phase + "  " + Mathf.RoundToInt(g.map.progress * 100) + "%", centre);
    }

    // ---------------------------------------------------------------- in the map
    void Playing()
    {
        var g = Game.I;
        var p = g.player;
        var rd = g.round;
        var gen = g.map.gen;

        // the hunter is held and sees nothing while the hiders hide
        if (rd.phase == Round.PH_HIDE && p.role == Player.ROLE_HUNTER)
        {
            Fill(new Rect(0, 0, W, H), new Color(0.04f, 0.05f, 0.07f, 1f));
            GUI.Label(new Rect(0, H / 2 - 70, W, 40), "The hiders are hiding", bigCentre);
            GUI.Label(new Rect(0, H / 2 - 20, W, 30), "You are held at the release point for " + Mathf.CeilToInt(rd.left) + " s.", centre);
        }
        if (rd.phase == Round.PH_BRIEF)
        {
            Fill(new Rect(0, H / 2 - 90, W, 150), new Color(0.04f, 0.05f, 0.07f, 0.8f));
            string who = rd.mode == Game.MODE_HUNT
                ? "You hunt" + (rd.startHunters > 1 ? ", with " + (rd.startHunters - 1) + " other hunter" + (rd.startHunters > 2 ? "s" : "") : "") + ". " + rd.startHiders + " hiders are disguised as props."
                : "You hide, with " + (rd.startHiders - 1) + " others. " + rd.startHunters + " hunter" + (rd.startHunters > 1 ? "s are" : " is") + " coming.";
            if (rd.requested == Game.MODE_RANDOM) who = "Drawn this round: " + who;
            GUI.Label(new Rect(0, H / 2 - 80, W, 40), who, bigCentre);
            string what = rd.mode == Game.MODE_HUNT
                ? "Hitting a real prop costs " + rd.basePenalty + " of your 100 health; at 0 you are out. A hit on a hider gives 5 back. In the last 45 s wrong props and hits count double."
                : "Look at a prop and click to become it, find a place, stand still; lock and taunt are on the wrist panel (Esc). You will be forced to taunt now and then.";
            GUI.Label(new Rect(W / 2 - 430, H / 2 - 34, 860, 50), what, centre);
            GUI.Label(new Rect(0, H / 2 + 20, W, 30), "Starting in " + Mathf.CeilToInt(rd.left) + " s", centre);
        }

        // top left: the seed and basic generation statistics, always on screen
        // top left: the seed and the prop counts, always on screen: on the floor + on furniture, in all and by zone
        string zones = "";
        for (int z = GenData.Z_HOUSE; z < GenData.ZoneName.Length; z++)
            if (gen.statZoneCells[z] > 0) zones += (zones.Length > 0 ? ", " : "") + GenData.ZoneName[z] + " " + gen.statZoneFloor[z] + "+" + gen.statZoneTop[z];
        string stats = "Seed <b>" + g.seedCode + "</b>   size " + GenData.SizeName[gen.size] + "   density " + GenData.DensityName[gen.density] + "   generator v" + GenData.GEN_VERSION
            + "\n" + gen.statProps + " props: " + gen.statFloor + " on the floor + " + gen.statTop + " on furniture, " + gen.statScenery + " trees, " + gen.nRooms + " rooms, "
            + (gen.statLeakPlausible + gen.statLeakComedic) + " out of place (" + gen.statLeakComedic + " comedic), " + gen.menuCount + " disguise types"
            + "\nBy zone, floor + furniture: " + zones + "   (F3: per room type)";
        float sw = Mathf.Min(600f, W / 2 - 170f);
        GUI.Box(new Rect(8, 8, sw, sw < 560f ? 100 : 68), GUIContent.none, box);
        GUI.Label(new Rect(16, 11, sw - 12, sw < 560f ? 96 : 64), stats, small);
        if (g.seedFromUrl)
        {
            float y = sw < 560f ? 112 : 80;
            Fill(new Rect(8, y, sw, 44), new Color(0.45f, 0.30f, 0.05f, 0.9f));
            GUI.Label(new Rect(16, y + 3, sw - 12, 40), "This map is pinned by the page address (?seed=). Back to the lobby (wrist panel, Menu) drops it; then START makes a new map.", small);
        }

        // top centre: the round
        if (rd.phase != Round.PH_NONE)
        {
            string ph = rd.phase == Round.PH_BRIEF ? "Get ready" : (rd.phase == Round.PH_HIDE ? "Hide phase" : (rd.phase == Round.PH_HUNT ? (rd.endgame ? "Hunt: endgame" : "Hunt") : "Round over"));
            GUI.Box(new Rect(W / 2 - 150, 8, 300, 50), GUIContent.none, box);
            GUI.Label(new Rect(W / 2 - 150, 10, 300, 24), ph + "   " + Round.FormatTime(Mathf.Max(0f, rd.left)), centre);
            GUI.Label(new Rect(W / 2 - 150, 32, 300, 22), "Hiders left " + rd.HidersAlive + " of " + rd.startHiders + "   Hunters " + rd.HuntersAlive + " of " + rd.startHunters, small.alignment == TextAnchor.MiddleCenter ? small : CentreSmall());
        }
        else
        {
            GUI.Box(new Rect(W / 2 - 110, 8, 220, 28), GUIContent.none, box);
            GUI.Label(new Rect(W / 2 - 110, 10, 220, 24), "Explore mode", centre);
        }

        // health
        if (p.role == Player.ROLE_HUNTER || (p.role == Player.ROLE_DEAD && rd.mode == Game.MODE_HUNT))
            Bar(new Rect(16, H - 46, 260, 26), p.hunterHealth / 100f, "Hunter health " + p.hunterHealth + "   wrong props " + p.wrongProps, new Color(0.70f, 0.22f, 0.20f));
        else if (p.role == Player.ROLE_HIDER && p.maxHealth > 0)
            Bar(new Rect(16, H - 46, 260, 26), (float)p.health / p.maxHealth, "Prop health " + p.health + " / " + p.maxHealth, new Color(0.20f, 0.52f, 0.28f));
        if (p.def >= 0)
        {
            string st = GameUI.DefName(p.def) + "  ·  " + (p.locked ? "LOCKED" : (p.settled ? "settled" : "moving")) + "  ·  view " + (p.third ? "third person" : "first person")
                + (p.role == Player.ROLE_HIDER && rd.phase == Round.PH_HUNT ? "  ·  changes left " + p.changesLeft + "  ·  " + (rd.PlayerTauntSkips ? "moved " + rd.PlayerMoved.ToString("0") + " m: next forced taunt skipped" : "forced taunt in " + Mathf.Max(0, Mathf.CeilToInt(rd.nextForced - Time.time)) + " s (moved " + rd.PlayerMoved.ToString("0.0") + " m since the last check; 4 m skips it)") : "");
            GUI.Box(new Rect(16, H - 80, Mathf.Min(820f, W - 32f), 28), GUIContent.none, box);
            GUI.Label(new Rect(24, H - 78, Mathf.Min(808f, W - 44f), 24), st, small);
        }
        else if (p.role == Player.ROLE_DEAD)
        {
            GUI.Box(new Rect(16, H - 80, 560, 28), GUIContent.none, box);
            GUI.Label(new Rect(24, H - 78, 548, 24), "You are out for this round: you can walk the arena, unseen and unheard.", small);
        }

        // crosshair, hit mark, hurt flash
        bool cross = !g.menuOpen && rd.phase != Round.PH_RESULT && rd.phase != Round.PH_END && !g.topView;
        if (cross)
        {
            float cx = W / 2, cy = H / 2;
            Color c = Time.time < p.hitMarkUntil ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 1f, 1f, 0.85f);
            Fill(new Rect(cx - 9, cy - 1, 7, 2), c); Fill(new Rect(cx + 2, cy - 1, 7, 2), c);
            Fill(new Rect(cx - 1, cy - 9, 2, 7), c); Fill(new Rect(cx - 1, cy + 2, 2, 7), c);
            if (Time.time < p.hitMarkUntil)
            {
                Fill(new Rect(cx - 16, cy - 16, 6, 2), c); Fill(new Rect(cx + 10, cy - 16, 6, 2), c);
                Fill(new Rect(cx - 16, cy + 14, 6, 2), c); Fill(new Rect(cx + 10, cy + 14, 6, 2), c);
            }
            string aim = p.AimText();
            if (aim.Length > 0) GUI.Label(new Rect(cx - 300, cy + 22, 600, 24), aim, centre);
        }
        if (Time.time < p.hurtUntil)
        {
            Color red = new Color(0.9f, 0.1f, 0.1f, 0.28f);
            Fill(new Rect(0, 0, W, 40), red); Fill(new Rect(0, H - 40, W, 40), red);
            Fill(new Rect(0, 40, 40, H - 80), red); Fill(new Rect(W - 40, 40, 40, H - 80), red);
        }
        string note = p.Notice;
        if (note.Length > 0) GUI.Label(new Rect(W / 2 - 400, H / 2 + 50, 800, 26), note, centre);
        string ban = rd.Banner;
        if (ban.Length > 0)
        {
            Fill(new Rect(W / 2 - 420, 70, 840, 38), new Color(0.04f, 0.05f, 0.07f, 0.75f));
            GUI.Label(new Rect(W / 2 - 420, 72, 840, 34), ban, bigCentre);
        }
        if (Cursor.lockState != CursorLockMode.Locked && cross && !wristOpen)
            GUI.Label(new Rect(W / 2 - 300, H - 120, 600, 26), "Click to capture the mouse (Esc frees it and shows the wrist panel)", centre);

        if (g.helpOpen && rd.phase != Round.PH_RESULT) Help();
        if (wristOpen && !g.menuOpen && rd.phase != Round.PH_RESULT) Wrist();
        if (g.debugOpen) DebugPanel();
        if (rd.phase == Round.PH_END || rd.phase == Round.PH_RESULT) Reveal();
        if (rd.phase == Round.PH_RESULT) Result();
        if (g.menuOpen) Menu();
    }

    GUIStyle centreSmall;
    GUIStyle CentreSmall()
    {
        if (centreSmall == null) centreSmall = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
        return centreSmall;
    }

    void Bar(Rect r, float f, string text, Color c)
    {
        Fill(r, new Color(0.05f, 0.06f, 0.08f, 0.9f));
        Fill(new Rect(r.x + 2, r.y + 2, (r.width - 4) * Mathf.Clamp01(f), r.height - 4), c);
        GUI.Label(new Rect(r.x + 8, r.y + 2, r.width + 200, r.height), "<color=#ffffff>" + text + "</color>", small);
    }

    // Every control is a stand-in for something a VR player does (owner decision G38): the help names both.
    void Help()
    {
        var p = Game.I.player;
        string t;
        const string move = "W A S D  move  <i>(VR: left stick)</i>\nMouse  look  <i>(VR: head, right stick turns)</i>\n";
        const string wrist = "Esc  wrist panel: lock, taunt, views, screen, menu  <i>(VR: look at the wrist)</i>\n";
        if (p.role == Player.ROLE_HUNTER)
            t = "<b>Hunter</b>\n" + move + "Space  jump  <i>(VR: jump button)</i>\nClick  fire a dart  <i>(VR: point the tagger, trigger)</i>\nClick a door  open or close it  <i>(VR: grab the handle)</i>\n" + wrist
                + "\nDarts knock loose props around. A real prop costs health; a hider flinches and you get a hit mark. Listen for taunts.";
        else if (p.def >= 0)
            t = "<b>Prop</b>\n" + move + "Shift  run  <i>(VR: stick pushed all the way)</i>\nSpace  hop, not for large props  <i>(VR: jump button)</i>\nClick a prop  become it  <i>(VR: point, trigger)</i>\nClick a door  open or close it  <i>(VR: grab the handle)</i>\n" + wrist
                + "\nThe prop faces the way you move; standing still pins it where it is. Small props can hop onto furniture and go under or into it.";
        else
            t = "<b>Walking</b>\n" + move + "Space  jump  <i>(VR: jump button)</i>\nClick a prop  become it  <i>(VR: point, trigger)</i>\nClick a door  open or close it  <i>(VR: grab the handle)</i>\n" + wrist;
        float w = 380f, h = p.def >= 0 ? 300f : (p.role == Player.ROLE_HUNTER ? 270f : 210f);
        Rect r = new Rect(W - w - 8, 8, w, h);
        GUI.Box(r, GUIContent.none, box);
        GUI.Label(new Rect(r.x + 10, r.y + 6, w - 20, h - 10), t, small);
    }

    // ---------------------------------------------------------------- the wrist panel (G38)
    // In VR the player raises a wrist and points at a panel worn there. Here Esc frees the mouse and shows it; a
    // click in the view outside it captures the mouse again and hides it. The round goes on meanwhile (the menu,
    // one of its buttons, pauses it). Everything the old letter keys did is here.
    public bool wristOpen;
    public Rect WristRect { get { float s = Mathf.Max(0.7f, Screen.height / 860f); return new Rect(16 * s, (H - 456) * s, 330 * s, 360 * s); } }

    void Wrist()
    {
        var g = Game.I;
        var p = g.player;
        var rd = g.round;
        Rect r = new Rect(16, H - 456, 330, 360);
        GUI.Box(r, GUIContent.none, box);
        GUILayout.BeginArea(new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 14));
        GUILayout.Label("Wrist panel", heading);
        if (p.def >= 0 && p.role != Player.ROLE_DEAD)
        {
            if (GUILayout.Button(p.locked ? "Unlock the prop" : (p.settled ? "Lock in place" : "Lock in place (stand still first)"), button, GUILayout.Height(30))) p.ToggleLock();
            if (p.role == Player.ROLE_HIDER && GUILayout.Button("Taunt", button, GUILayout.Height(30))) p.Taunt(false);
            if (GUILayout.Button("Unstick", button, GUILayout.Height(30))) p.Unstick();
            if (p.role == Player.ROLE_EXPLORE && GUILayout.Button("Back to the walking body", button, GUILayout.Height(30))) { p.ToWalker(); p.Say("Back to the walking body"); }
        }
        var pip = PipScreen.I;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(pip.On ? "Screen: on" : "Screen: off", button, GUILayout.Height(30))) pip.Toggle();
        if (pip.On && GUILayout.Button("Size", button, GUILayout.Height(30))) pip.NextSize();
        if (pip.On && GUILayout.Button("Side", button, GUILayout.Height(30))) pip.NextView();
        GUILayout.EndHorizontal();
        if (pip.On) GUILayout.Label("Drag the screen with the mouse to move it (VR: grab it).", small);
        if (GUILayout.Button(p.third ? "View: third person (desktop only)" : "View: first person", button, GUILayout.Height(30))) p.SetThird(!p.third);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(g.helpOpen ? "Help: shown" : "Help: hidden", button, GUILayout.Height(30))) g.helpOpen = !g.helpOpen;
        if (GUILayout.Button("Menu (pauses)", button, GUILayout.Height(30))) { wristOpen = false; g.ToggleMenu(); }
        GUILayout.EndHorizontal();
        GUILayout.Label("Click in the view to go back to playing.", small);
        GUILayout.EndArea();
    }

    void DebugPanel()
    {
        var g = Game.I;
        var p = g.player;
        var gen = g.map.gen;
        int cell = g.map.CellAt(p.body.pos);
        string where = "outside the map";
        if (cell >= 0 && gen.innerRoom[cell] >= 0)
        {
            int room = gen.innerRoom[cell];
            int t = gen.tags[cell];
            int rt = gen.roomType[room];
            where = "This room: " + gen.roomCellCount[room] / 4 + " m2, " + gen.roomFloor[room] + " props on the floor (target " + gen.roomFloorTarget[room] + ", range up to " + gen.roomFloorMax[room]
                + "), " + gen.roomTop[room] + " on furniture (cap " + gen.roomTopCap[room] + ")" + (gen.roomTrees[room] > 0 ? ", " + gen.roomTrees[room] + " trees" : "") + "\n"
                + GenData.ZoneName[gen.roomZone[room]] + " / " + GenData.RoomName(rt) + " (room " + room + ")  cell " + (cell % gen.IW) + "," + (cell / gen.IW)
                + "  tags" + ((t & GenData.T_WALL_ANY) != 0 ? " WALL" : "") + ((t & GenData.T_CORNER) != 0 ? " CORNER" : "") + ((t & GenData.T_CENTER) != 0 ? " CENTER" : "")
                + ((t & GenData.T_DOOR_CLEAR) != 0 ? " DOOR_CLEAR" : "") + ((t & GenData.T_PATH) != 0 ? " PATH" : "") + ((t & GenData.T_OUTDOOR) != 0 ? " OUTDOOR" : "")
                + ((t & GenData.T_SOFT_GROUND) != 0 ? " SOFT" : "") + ((t & GenData.T_NO_SPAWN) != 0 ? " NO_SPAWN" : "");
        }
        string aim = "";
        if (p.aimProp != null)
        {
            int e = p.aimProp.emit;
            int fl = gen.emitFlags[e];
            aim = "\nAim: emit " + e + " " + GenData.defId[p.aimProp.def] + " x" + gen.defCopies[p.aimProp.def] + (gen.menu[p.aimProp.def] ? " [disguise]" : " [never a disguise]")
                + ((fl & MapGen.EF_LEAK) != 0 ? ((fl & MapGen.EF_COMEDIC) != 0 ? " COMEDIC LEAK" : " PLAUSIBLE LEAK") : "") + ((fl & MapGen.EF_LOOSE) != 0 ? " loose remainder" : "") + HostText(gen, e)
                + (gen.emitParent[e] >= 0 ? " in a slot of " + GenData.defId[gen.emitDef[gen.emitParent[e]]] : "") + "  " + p.aimDist.ToString("0.0") + " m";
        }
        string text = "<b>Debug</b>  " + g.fps.ToString("0") + " fps   " + Screen.width + " x " + Screen.height
            + "\nMap hash " + gen.mapHash.ToString("X8") + "   generated in " + g.map.genMs.ToString("0") + " ms, built in " + g.map.buildMs.ToString("0") + " ms, over " + g.genFrames + " frames (" + g.map.wallMs.ToString("0") + " ms)"
            + "\nCells " + gen.statGroundCells + ": " + ZoneCells(gen)
            + "\nEmits " + gen.nEmit + ": props " + gen.statProps + " (" + gen.statFloor + " floor, " + gen.statTop + " furniture), prop groups " + gen.statGroups + ", loose remainder " + gen.statLoose + ", leaks " + gen.statLeakPlausible + " plausible + " + gen.statLeakComedic + " comedic"
            + "\nHiding sockets " + gen.nSockets + ", seed re-rolls " + gen.attempt + ", repairs " + gen.statRepairs + ", renderers " + g.map.rendererCount + ", colliders " + g.map.colliderCount
            + "\nPlayer " + p.body.pos.x.ToString("0.00") + ", " + p.body.pos.y.ToString("0.00") + ", " + p.body.pos.z.ToString("0.00") + "  eye " + p.eye.ToString("0.00") + (p.body.grounded ? "  grounded" : "  in the air")
            + "\n" + where + aim;
        Rect r = new Rect(8, 116, 700, 208);
        GUI.Box(r, GUIContent.none, box);
        GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, r.height - 8), text, small);
        RoomTypeTable(r.x, r.yMax + 6);
    }

    // what the object was placed against (G28: nothing is placed without a host)
    static string HostText(MapGen gen, int e)
    {
        int h = gen.emitHost[e];
        if (gen.emitParent[e] >= 0) return "";
        if (h >= 0) return " placed by " + GenData.defId[gen.emitDef[h]];
        return h == MapGen.H_WALL ? " placed at a wall or fence" : (h == MapGen.H_FREE ? " centre piece" : (h == MapGen.H_NATURE ? " planting" : (h == MapGen.H_GROUND ? " on the ground" : "")));
    }

    // The tuning table (G24): per room type on this map, what stands on the floor and on furniture, per room
    // and per 100 m2, next to the range the generator aims for at this map's density.
    void RoomTypeTable(float x, float y)
    {
        var gen = Game.I.map.gen;
        int rows = 0;
        for (int t = 0; t < gen.statTypeRooms.Length; t++) if (gen.statTypeRooms[t] > 0 && t != GenData.R_RELEASE) rows++;
        float[] col = { 8, 150, 200, 262, 372, 470, 580 };
        Rect r = new Rect(x, y, 700, 44 + rows * 17);
        GUI.Box(r, GUIContent.none, box);
        int pct = GenData.DensityPercent[gen.density];
        GUI.Label(new Rect(r.x + 8, r.y + 3, 690, 20), "<b>Props per room type</b>   density " + GenData.DensityName[gen.density] + " (" + pct + "% of the Normal ranges)", small);
        string[] head = { "Room type", "Rooms", "Area m2", "On the floor", "per 100 m2", "range per 100 m2", "On furniture (max)" };
        for (int c = 0; c < head.Length; c++) GUI.Label(new Rect(r.x + col[c], r.y + 21, 140, 20), "<color=#9fb4c8>" + head[c] + "</color>", small);
        float ry = r.y + 38;
        for (int t = 0; t < gen.statTypeRooms.Length; t++)
        {
            if (gen.statTypeRooms[t] == 0 || t == GenData.R_RELEASE) continue;
            int m2 = gen.statTypeCells[t] / 4;
            int per = m2 > 0 ? gen.statTypeFloor[t] * 100 / m2 : 0;
            int lo = GenData.rangeFloorMin[t] * pct / 100, hi = GenData.rangeFloorMax[t] * pct / 100;
            // outside the range: mandatory pieces and chairs in small rooms push it up, the cover cap holds it down
            string mark = per > hi ? "#ffb060" : (per < lo ? "#8fc8ff" : "#e8e8e8");
            string[] cells =
            {
                GenData.RoomName(t), gen.statTypeRooms[t].ToString(), m2.ToString(), gen.statTypeFloor[t].ToString(),
                "<color=" + mark + ">" + per + "</color>", lo + " to " + hi, gen.statTypeTop[t] + " (" + GenData.rangeTopMax[t] * pct / 100 * m2 / 100 + ")",
            };
            for (int c = 0; c < cells.Length; c++) GUI.Label(new Rect(r.x + col[c], ry, 150, 20), cells[c], small);
            ry += 17;
        }
    }

    // labels over the map from above: where every hider was
    void Reveal()
    {
        var rd = Game.I.round;
        Camera c = Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null;
        foreach (var cam in Camera.allCameras) if (cam.enabled && cam.orthographic) c = cam;
        if (c == null) return;
        float s = Mathf.Max(0.7f, Screen.height / 860f);
        for (int i = 0; i < rd.beaconPos.Count; i++)
        {
            Vector3 sp = c.WorldToScreenPoint(rd.beaconPos[i] + Vector3.up * 4f);
            float x = sp.x / s, y = (Screen.height - sp.y) / s;
            Fill(new Rect(x - 130, y - 34, 260, 22), new Color(0.04f, 0.05f, 0.07f, 0.8f));
            GUI.Label(new Rect(x - 130, y - 35, 260, 22), rd.beaconText[i], CentreSmall());
        }
        if (rd.phase == Round.PH_END)
        {
            Fill(new Rect(W / 2 - 330, H - 70, 660, 34), new Color(0.04f, 0.05f, 0.07f, 0.8f));
            GUI.Label(new Rect(W / 2 - 330, H - 68, 660, 30), rd.Winner + ". " + rd.endReason + ". This is where the hiders were. (Click: results)", centre);
        }
    }

    void Result()
    {
        var g = Game.I;
        var rd = g.round;
        float pw = 620f, ph = 150f + rd.resultLines.Count * 24f + 150f;
        Rect r = new Rect(W - pw - 16, 70, pw, ph);
        GUI.Box(r, GUIContent.none, box);
        GUILayout.BeginArea(new Rect(r.x + 20, r.y + 12, pw - 40, ph - 20));
        GUILayout.Label("<b>" + rd.Winner.ToUpperInvariant() + "</b>", title, GUILayout.Height(44));
        GUILayout.Label(rd.endReason, heading);
        GUILayout.Space(6);
        foreach (string line in rd.resultLines) GUILayout.Label(line, label, GUILayout.Height(22));
        GUILayout.Space(8);
        // no "another round on this map": a run is one round, then the next map (G34)
        if (GUILayout.Button("Back to the lobby (START there makes a new map)", button, GUILayout.Height(34))) { AudioHub.I.Play("back_001", 0.5f); g.BackToStart(); }
        GUILayout.EndArea();
    }

    void Menu()
    {
        var g = Game.I;
        Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.45f));
        float pw = 660f, ph = 420f;
        Rect r = new Rect((W - pw) / 2, (H - ph) / 2, pw, ph);
        Fill(r, new Color(0.08f, 0.10f, 0.12f, 1f));
        GUILayout.BeginArea(new Rect(r.x + 24, r.y + 14, pw - 48, ph - 24));
        GUILayout.Label("Menu", title, GUILayout.Height(40));
        GUILayout.Label("Seed " + g.seedCode + " (" + GenData.SizeName[g.map.gen.size] + "). The round is paused while the menu is open.", small);
        GUILayout.Space(8);
        if (GUILayout.Button("Resume", bigButton, GUILayout.Height(40))) g.ToggleMenu();
        GUILayout.Space(4);
        Sliders();
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(g.player.third ? "View: third person" : "View: first person", button, GUILayout.Height(32))) g.player.SetThird(!g.player.third);
        if (GUILayout.Button(g.helpOpen ? "Help panel: shown" : "Help panel: hidden", button, GUILayout.Height(32))) g.helpOpen = !g.helpOpen;
        if (GUILayout.Button(g.debugOpen ? "Debug: shown" : "Debug: hidden", button, GUILayout.Height(32))) g.debugOpen = !g.debugOpen;
        GUILayout.EndHorizontal();
        GUILayout.Label("Testing aids, not the game's flow (a run is one round, then a new map): play this map again as", small);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Explore this map", button, GUILayout.Height(32))) { g.mode = Game.MODE_EXPLORE; g.round.Begin(g.mode); g.menuOpen = false; }
        if (GUILayout.Button("New round", button, GUILayout.Height(32))) { g.mode = Game.MODE_RANDOM; g.round.Begin(g.mode); g.menuOpen = false; }
        if (GUILayout.Button("Hunt here", button, GUILayout.Height(32))) { g.mode = Game.MODE_HUNT; g.round.Begin(g.mode); g.menuOpen = false; }
        if (GUILayout.Button("Hide here", button, GUILayout.Height(32))) { g.mode = Game.MODE_HIDE; g.round.Begin(g.mode); g.menuOpen = false; }
        GUILayout.EndHorizontal();
        if (GUILayout.Button(g.topView ? "Map from above: on" : "Map from above: off (debug)", button, GUILayout.Height(32))) g.SetTopView(!g.topView);
        GUILayout.Space(6);
        if (GUILayout.Button("Back to the lobby", button, GUILayout.Height(36))) { AudioHub.I.Play("back_001", 0.5f); g.BackToStart(); }
        GUILayout.Label("Back in the lobby START makes a new map, unless the host keeps the seed on the host panel.", small);
        GUILayout.EndArea();
    }
}
