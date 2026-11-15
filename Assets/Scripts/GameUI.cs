using UnityEngine;

// All screens, drawn with Unity's immediate-mode GUI: the start panel (seed, size, mode, Generate), the
// progress bar, the in-round display, the help panel, the Esc menu, the debug overlay and the round result.
public class GameUI : MonoBehaviour
{
    GUIStyle label, small, title, heading, box, button, bigButton, field, centre, bigCentre, toggleOn;
    Texture2D white, panelTex, darkTex, btnTex, btnHoverTex, btnOnTex, fieldTex;
    Font kenney;
    bool stylesReady;
    float W, H;

    // prop counts at Normal density, from the generator console test (they scale with the density setting)
    static readonly string[] SizeNote =
    {
        "S: 7 x 7 cells, about 28 used, about 130 props at Normal density. For up to 8 players.",
        "M: 9 x 9 cells, about 52 used, about 235 props at Normal density. For 9 to 16 players.",
        "L: 12 x 12 cells, about 85 used, about 370 props at Normal density. For 17 to 24 players.",
        "XL: 14 x 14 cells, about 108 used, about 490 props at Normal density. For 25 to 32 players.",
    };
    static readonly string[] ModeName = { "Explore", "Hunt", "Hide" };
    static readonly string[] ModeNote =
    {
        "Walk the map freely. Look at a prop and press E to become it; G turns you back.",
        "You are the hunter. Bot hiders are disguised as props: find them with the dart tagger. Wrong props cost health.",
        "You are a hider. Pick a prop, find a spot, lock in place. Bot hunters come looking after the hide phase.",
    };

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
        else if (g.state == Game.State.Generating) Progress();
        else Playing();
    }

    // ---------------------------------------------------------------- start panel
    void StartPanel()
    {
        var g = Game.I;
        Fill(new Rect(0, 0, W, H), new Color(0.13f, 0.17f, 0.20f, 1f));
        float pw = 680f, ph = 748f;
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

        GUILayout.Label("Mode", heading);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < 3; i++)
            if (GUILayout.Button(ModeName[i], g.mode == i ? toggleOn : button, GUILayout.Height(32))) { g.mode = i; AudioHub.I.Play("select_001", 0.5f); }
        GUILayout.EndHorizontal();
        GUILayout.Label(ModeNote[g.mode], small, GUILayout.Height(36));
        GUILayout.Space(8);

        if (GUILayout.Button("Generate map", bigButton, GUILayout.Height(48))) g.Generate(false);
        if (g.panelMessage.Length > 0)
        {
            GUILayout.Label("<color=#ffb060>" + g.panelMessage + "</color>", label);
            if (g.remixOffered && GUILayout.Button("Play as a remix on generator v" + GenData.GEN_VERSION, button, GUILayout.Height(30))) g.Generate(true);
        }
        else GUILayout.Label("Nothing is generated until you press Generate; every new map needs a new press.", small);
        GUILayout.Space(8);

        Sliders();
        GUILayout.Space(6);
        GUILayout.Label("Controls: W A S D move, mouse look, Shift run (as a prop), Space jump or hop, E disguise, Q lock, V third person, H help, Esc menu.", small);
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
            GUI.Label(new Rect(0, H / 2 + 15, W, 30), "Press Enter to skip the wait: the bots are already hidden.", centre);
        }
        if (rd.phase == Round.PH_BRIEF)
        {
            Fill(new Rect(0, H / 2 - 90, W, 150), new Color(0.04f, 0.05f, 0.07f, 0.8f));
            string who = rd.mode == Game.MODE_HUNT
                ? "You are the hunter. " + rd.startHiders + " hiders are disguised as props."
                : "You are a hider, with " + (rd.startHiders - 1) + " others. " + rd.startHunters + " hunters are coming.";
            GUI.Label(new Rect(0, H / 2 - 80, W, 40), who, bigCentre);
            string what = rd.mode == Game.MODE_HUNT
                ? "Hitting a real prop costs " + rd.basePenalty + " of your 100 health; at 0 you are out. A hit on a hider gives 5 back."
                : "Look at a prop and press E to become it, find a place, stand still, press Q to lock. You will be forced to taunt now and then.";
            GUI.Label(new Rect(W / 2 - 430, H / 2 - 34, 860, 50), what, centre);
            GUI.Label(new Rect(0, H / 2 + 20, W, 30), "Starting in " + Mathf.CeilToInt(rd.left) + " s", centre);
        }

        // top left: the seed and basic generation statistics, always on screen
        // top left: the seed and the prop counts, always on screen: on the floor + on furniture, in all and by zone
        string zones = "";
        for (int z = GenData.Z_HOUSE; z <= GenData.Z_FOREST; z++)
            if (gen.statZoneCells[z] > 0) zones += (zones.Length > 0 ? ", " : "") + GenData.ZoneName[z] + " " + gen.statZoneFloor[z] + "+" + gen.statZoneTop[z];
        string stats = "Seed <b>" + g.seedCode + "</b>   size " + GenData.SizeName[gen.size] + "   density " + GenData.DensityName[gen.density] + "   generator v" + GenData.GEN_VERSION
            + "\n" + gen.statProps + " props: " + gen.statFloor + " on the floor + " + gen.statTop + " on furniture, " + gen.statScenery + " trees, " + gen.nRooms + " rooms, "
            + (gen.statLeakPlausible + gen.statLeakComedic) + " out of place (" + gen.statLeakComedic + " comedic), " + gen.menuCount + " disguise types"
            + "\nBy zone, floor + furniture: " + zones + "   (F3: per room type)";
        float sw = Mathf.Min(600f, W / 2 - 170f);
        GUI.Box(new Rect(8, 8, sw, sw < 560f ? 100 : 68), GUIContent.none, box);
        GUI.Label(new Rect(16, 11, sw - 12, sw < 560f ? 96 : 64), stats, small);

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
                + (p.role == Player.ROLE_HIDER && rd.phase == Round.PH_HUNT ? "  ·  changes left " + p.changesLeft + "  ·  forced taunt in " + Mathf.Max(0, Mathf.CeilToInt(rd.nextForced - Time.time)) + " s" : "");
            GUI.Box(new Rect(16, H - 80, 560, 28), GUIContent.none, box);
            GUI.Label(new Rect(24, H - 78, 548, 24), st, small);
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
        if (Cursor.lockState != CursorLockMode.Locked && cross)
            GUI.Label(new Rect(W / 2 - 300, H - 120, 600, 26), "Click to capture the mouse (Esc releases it and opens the menu)", centre);

        if (g.helpOpen && rd.phase != Round.PH_RESULT) Help();
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

    void Help()
    {
        var p = Game.I.player;
        string t;
        if (p.role == Player.ROLE_HUNTER)
            t = "<b>Hunter</b>\nW A S D  move\nMouse  look\nSpace  jump\nLeft click  fire a dart (one per press)\nLeft click on a door  open or close it\nV  third-person view\nH  hide this panel\nEsc or M  menu\nF3  debug overlay\n\nDarts knock loose props around. A real prop costs health; a hider flinches and you get a hit mark. Listen for taunts.";
        else if (p.def >= 0)
            t = "<b>Prop</b>\nW A S D  move (Shift: run)\nMouse  look\nSpace  hop (not for large props)\nE or click  become the prop you look at\nQ  lock in place / unlock\nZ, C  turn 15 degrees while still\nR  rotation lock on or off\nT  taunt\nClick on a door  open or close it\nV  third-person view\nU  unstick" + (p.role == Player.ROLE_EXPLORE ? "\nG  back to the walking body" : "") + "\nH  hide this panel\nEsc or M  menu\n\nSmall props can hop onto furniture and go under or into it.";
        else
            t = "<b>Walking</b>\nW A S D  move\nMouse  look\nSpace  jump\nE or click  become the prop you look at\nClick on a door  open or close it\nV  third-person view\nH  hide this panel\nEsc or M  menu\nF3  debug overlay";
        float w = 300f, h = p.def >= 0 ? 350f : (p.role == Player.ROLE_HUNTER ? 310f : 230f);
        Rect r = new Rect(W - w - 8, 8, w, h);
        GUI.Box(r, GUIContent.none, box);
        GUI.Label(new Rect(r.x + 10, r.y + 6, w - 20, h - 10), t, small);
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
            aim = "\nAim: emit " + e + " " + GenData.defId[p.aimProp.def] + " x" + gen.defCopies[p.aimProp.def] + (gen.menu[p.aimProp.def] ? " [menu]" : "")
                + ((fl & MapGen.EF_LEAK) != 0 ? ((fl & MapGen.EF_COMEDIC) != 0 ? " COMEDIC LEAK" : " PLAUSIBLE LEAK") : "") + ((fl & MapGen.EF_QUOTA) != 0 ? " quota copy" : "")
                + (gen.emitParent[e] >= 0 ? " in a slot of " + GenData.defId[gen.emitDef[gen.emitParent[e]]] : "") + "  " + p.aimDist.ToString("0.0") + " m";
        }
        string text = "<b>Debug</b>  " + g.fps.ToString("0") + " fps   " + Screen.width + " x " + Screen.height
            + "\nMap hash " + gen.mapHash.ToString("X8") + "   generated in " + g.map.genMs.ToString("0") + " ms, built in " + g.map.buildMs.ToString("0") + " ms, over " + g.genFrames + " frames (" + g.map.wallMs.ToString("0") + " ms)"
            + "\nCells " + gen.statGroundCells + ": House " + gen.statZoneCells[1] + ", Warehouse " + gen.statZoneCells[2] + ", Diner " + gen.statZoneCells[3] + ", Lawn " + gen.statZoneCells[4] + ", Forest " + gen.statZoneCells[5]
            + "\nEmits " + gen.nEmit + ": props " + gen.statProps + " (" + gen.statFloor + " floor, " + gen.statTop + " furniture), prop groups " + gen.statGroups + ", quota copies " + gen.statQuotaAdded + ", leaks " + gen.statLeakPlausible + " plausible + " + gen.statLeakComedic + " comedic"
            + "\nHiding sockets " + gen.nSockets + ", seed re-rolls " + gen.attempt + ", repairs " + gen.statRepairs + ", renderers " + g.map.rendererCount + ", colliders " + g.map.colliderCount
            + "\nPlayer " + p.body.pos.x.ToString("0.00") + ", " + p.body.pos.y.ToString("0.00") + ", " + p.body.pos.z.ToString("0.00") + "  eye " + p.eye.ToString("0.00") + (p.body.grounded ? "  grounded" : "  in the air")
            + "\n" + where + aim;
        Rect r = new Rect(8, 116, 700, 208);
        GUI.Box(r, GUIContent.none, box);
        GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, r.height - 8), text, small);
        RoomTypeTable(r.x, r.yMax + 6);
    }

    // The tuning table (G24): per room type on this map, what stands on the floor and on furniture, per room
    // and per 100 m2, next to the range the generator aims for at this map's density.
    void RoomTypeTable(float x, float y)
    {
        var gen = Game.I.map.gen;
        int rows = 0;
        for (int t = 0; t < 64; t++) if (gen.statTypeRooms[t] > 0 && t != GenData.R_RELEASE) rows++;
        float[] col = { 8, 150, 200, 262, 372, 470, 580 };
        Rect r = new Rect(x, y, 700, 44 + rows * 17);
        GUI.Box(r, GUIContent.none, box);
        int pct = GenData.DensityPercent[gen.density];
        GUI.Label(new Rect(r.x + 8, r.y + 3, 690, 20), "<b>Props per room type</b>   density " + GenData.DensityName[gen.density] + " (" + pct + "% of the Normal ranges)", small);
        string[] head = { "Room type", "Rooms", "Area m2", "On the floor", "per 100 m2", "range per 100 m2", "On furniture (max)" };
        for (int c = 0; c < head.Length; c++) GUI.Label(new Rect(r.x + col[c], r.y + 21, 140, 20), "<color=#9fb4c8>" + head[c] + "</color>", small);
        float ry = r.y + 38;
        for (int t = 0; t < 64; t++)
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
            GUI.Label(new Rect(W / 2 - 330, H - 68, 660, 30), rd.endReason + ". This is where the hiders were. (Enter: results)", centre);
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
        GUILayout.Label("Round over: " + rd.endReason.ToLowerInvariant(), heading);
        GUILayout.Label("Hiders' survival share: <b>" + rd.survivalShare + "%</b>", bigCentre, GUILayout.Height(40));
        GUILayout.Label("A round has no winner: the hiding side is scored by how long its hiders survived. In the full game the two teams then swap sides and the better share wins the run.", small);
        GUILayout.Space(6);
        foreach (string line in rd.resultLines) GUILayout.Label(line, label, GUILayout.Height(22));
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Play again on this map", button, GUILayout.Height(34))) { AudioHub.I.Play("click_001", 0.5f); rd.StartRound(); }
        if (GUILayout.Button(rd.mode == Game.MODE_HUNT ? "Swap sides: hide" : "Swap sides: hunt", button, GUILayout.Height(34)))
        {
            AudioHub.I.Play("click_001", 0.5f);
            g.mode = rd.mode == Game.MODE_HUNT ? Game.MODE_HIDE : Game.MODE_HUNT;
            rd.Begin(g.mode);
        }
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Back to the start panel (a new map needs Generate)", button, GUILayout.Height(34))) { AudioHub.I.Play("back_001", 0.5f); g.BackToStart(); }
        GUILayout.Label("Knocked props return to their places before the next round.", small);
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
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Explore this map", button, GUILayout.Height(32))) { g.mode = Game.MODE_EXPLORE; g.round.Begin(g.mode); g.menuOpen = false; }
        if (GUILayout.Button("Hunt here", button, GUILayout.Height(32))) { g.mode = Game.MODE_HUNT; g.round.Begin(g.mode); g.menuOpen = false; }
        if (GUILayout.Button("Hide here", button, GUILayout.Height(32))) { g.mode = Game.MODE_HIDE; g.round.Begin(g.mode); g.menuOpen = false; }
        GUILayout.EndHorizontal();
        if (GUILayout.Button(g.topView ? "Map from above: on" : "Map from above: off (debug)", button, GUILayout.Height(32))) g.SetTopView(!g.topView);
        GUILayout.Space(6);
        if (GUILayout.Button("Back to the start panel", button, GUILayout.Height(36))) { AudioHub.I.Play("back_001", 0.5f); g.BackToStart(); }
        GUILayout.Label("Returning keeps nothing: a new map is generated only when you press Generate there.", small);
        GUILayout.EndArea();
    }
}
