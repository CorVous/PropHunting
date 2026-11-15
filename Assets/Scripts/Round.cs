using System.Collections.Generic;
using UnityEngine;

// One round: BRIEF -> HIDE -> HUNT -> ROUND_END (reveal) -> result panel (05 section 4).
// Hunt mode: the player hunts bot hiders. Hide mode: the player hides with bot hiders from bot hunters.
// A round has no winner: it is scored by the hiders' survival share (G7, 05 section 8.1).
// Round.cs: flow, shots, scoring. RoundSpots.cs: where bot hiders hide, and hiding sockets.
public partial class Round : MonoBehaviour
{
    public const int PH_NONE = 0, PH_BRIEF = 1, PH_HIDE = 2, PH_HUNT = 3, PH_END = 4, PH_RESULT = 5;

    public int phase, mode;
    public float left;                  // seconds left in the phase
    public float huntLength, huntElapsed;
    public bool endgame;
    public string banner = "";
    float bannerUntil;
    public readonly List<BotHider> hiders = new List<BotHider>();
    public readonly List<BotHunter> hunters = new List<BotHunter>();
    public int totalPlayers, startHiders, startHunters;
    public int basePenalty;
    float forcedInterval;

    // the player as a hider
    public bool playerCaught;
    public float playerSurvived;
    public float nextForced;
    bool warned;
    Vector3 playerCaughtPos;
    int playerCaughtDef;

    // results
    public int survivalShare;
    public readonly List<string> resultLines = new List<string>();
    public string endReason = "";
    readonly List<GameObject> beacons = new List<GameObject>();
    public readonly List<Vector3> beaconPos = new List<Vector3>();
    public readonly List<string> beaconText = new List<string>();
    static Material beamGreen, beamRed;

    Player P { get { return Game.I.player; } }
    MapBuilder Map { get { return Game.I.map; } }

    public bool InputBlocked { get { return phase == PH_RESULT; } }

    // hunters are held during BRIEF and HIDE; everyone is frozen for the reveal
    public bool MovementBlocked(Player p)
    {
        if (phase == PH_END) return true;
        if (p.role == Player.ROLE_HUNTER && (phase == PH_BRIEF || phase == PH_HIDE)) return true;
        if (p.role == Player.ROLE_HIDER && phase == PH_BRIEF) return true;
        return false;
    }

    public int Penalty
    {
        get
        {
            // halved, rounded up, in the endgame (6.6); the small-group halving (9.4) is already in basePenalty
            return endgame ? (basePenalty + 1) / 2 : basePenalty;
        }
    }

    public int HidersAlive
    {
        get
        {
            int n = 0;
            foreach (var h in hiders) if (h.alive) n++;
            if (mode == Game.MODE_HIDE && !playerCaught) n++;
            return n;
        }
    }

    public int HuntersAlive
    {
        get
        {
            int n = 0;
            foreach (var h in hunters) if (h.alive) n++;
            if (mode == Game.MODE_HUNT && P.role == Player.ROLE_HUNTER) n++;
            return n;
        }
    }

    public void Say(string text, float seconds = 3f)
    {
        banner = text; bannerUntil = Time.time + seconds;
    }

    public string Banner { get { return Time.time < bannerUntil ? banner : ""; } }

    // ---------------------------------------------------------------- start and stop
    public void Begin(int newMode)
    {
        Stop();
        mode = newMode;
        BuildSockets();
        if (mode == Game.MODE_EXPLORE)
        {
            phase = PH_NONE;
            P.Activate(Player.ROLE_EXPLORE, Map.ReleasePoint() + Vector3.up * 0.05f, Map.ReleaseYaw());
            P.changesLeft = 99;
            P.Say("Explore: walk the map. Look at a prop and press E to disguise as it.", 6f);
            return;
        }
        StartRound();
    }

    public void Stop()
    {
        foreach (var h in hiders) if (h != null) Destroy(h.gameObject);
        foreach (var h in hunters) if (h != null) Destroy(h.gameObject);
        hiders.Clear(); hunters.Clear();
        ClearBeacons();
        DartFx.ClearMarks();
        phase = PH_NONE; endgame = false; banner = "";
        if (Game.I.topView) Game.I.SetTopView(false);
    }

    void ClearBeacons()
    {
        foreach (var b in beacons) if (b != null) Destroy(b);
        beacons.Clear(); beaconPos.Clear(); beaconText.Clear();
    }

    // props return to their generated poses before every round on the same map (D11)
    public void StartRound()
    {
        foreach (var h in hiders) if (h != null) Destroy(h.gameObject);
        foreach (var h in hunters) if (h != null) Destroy(h.gameObject);
        hiders.Clear(); hunters.Clear();
        ClearBeacons();
        DartFx.ClearMarks();
        if (Game.I.topView) Game.I.SetTopView(false);
        Map.ResetProps();
        ResetSockets();
        int size = Map.gen.size;
        huntLength = Balance.HuntPhase[size];
        huntElapsed = 0f; endgame = false; playerCaught = false; playerSurvived = 0f; warned = false;
        resultLines.Clear(); endReason = "";

        int botHiders = mode == Game.MODE_HUNT ? Balance.BotHiders[size] : Balance.BotHiders[size] - 1;
        for (int i = 0; i < botHiders; i++) SpawnBotHider(i);
        if (mode == Game.MODE_HUNT)
        {
            startHunters = 1; startHiders = hiders.Count;
            P.Activate(Player.ROLE_HUNTER, Map.ReleasePoint() + Vector3.up * 0.05f, Map.ReleaseYaw());
            P.hunterHealth = Balance.HunterHealth; P.wrongProps = 0; P.hitsLanded = 0; P.catches = 0;
        }
        else
        {
            int n = Balance.BotHunters[size];
            for (int i = 0; i < n; i++) SpawnBotHunter(i);
            startHunters = hunters.Count; startHiders = hiders.Count + 1;
            SpawnPlayerHider();
        }
        totalPlayers = startHiders + startHunters;
        bool small = totalPlayers < Balance.SmallGroup;
        basePenalty = small ? (Balance.WrongPropPenalty + 1) / 2 : Balance.WrongPropPenalty;
        forcedInterval = small ? Balance.ForcedTauntSmallGroup : Balance.ForcedTaunt;
        phase = PH_BRIEF; left = Balance.Brief;
        AudioHub.I.Play("ready", 0.8f);
    }

    void SpawnBotHunter(int i)
    {
        Color32[] shirts = { new Color32(206, 84, 72, 255), new Color32(222, 150, 60, 255), new Color32(150, 90, 170, 255) };
        var go = FigureMesh.Build("BotHunter", shirts[i % shirts.Length]);
        var bh = go.AddComponent<BotHunter>();
        bh.label = "Hunter " + (i + 1);
        Vector3 rp = Map.ReleasePoint();
        go.transform.position = rp + new Vector3((i - 1) * 0.8f, 0f, 0f);
        go.transform.rotation = Quaternion.Euler(0f, Map.ReleaseYaw(), 0f);
        hunters.Add(bh);
    }

    void SpawnPlayerHider()
    {
        var g = Map.gen;
        Vector3 at = Map.ReleasePoint();
        if (g.nSpawns > 0) at = Map.CellCentre(g.spawnCell[Random.Range(0, g.nSpawns)]);
        P.Activate(Player.ROLE_HIDER, at + Vector3.up * 0.05f, Random.Range(0f, 360f));
        P.maxHealth = 0; P.health = 0;
        P.changesLeft = Balance.HuntChanges;
        // starting disguise: a random medium type from the menu that fits at the spawn point (05 section 3.8)
        var cands = new List<int>();
        for (int d = 0; d < GenData.nDefs; d++) if (g.menu[d] && GenData.defDisguise[d] == 2) cands.Add(d);
        for (int k = 0; k < 30 && cands.Count > 0; k++)
        {
            int d = cands[Random.Range(0, cands.Count)];
            if (P.SetDisguise(d)) break;
        }
        if (P.def < 0) for (int d = 0; d < GenData.nDefs && P.def < 0; d++) if (g.menu[d]) P.SetDisguise(d);
    }

    // ---------------------------------------------------------------- the clock
    void Update()
    {
        if (Game.I.state != Game.State.Playing || phase == PH_NONE || phase == PH_RESULT) return;
        if (Game.I.menuOpen) return;   // single player: the menu pauses the round
        float dt = Time.deltaTime;
        left -= dt;
        if (phase == PH_BRIEF)
        {
            if (left <= 0f) { phase = PH_HIDE; left = Balance.HidePhase[Map.gen.size]; }
        }
        else if (phase == PH_HIDE)
        {
            // prototype convenience: the bots are already hidden, so a waiting hunter may skip the rest
            if (mode == Game.MODE_HUNT && Input.GetKeyDown(KeyCode.Return) && left > 3f) left = 3f;
            if (left <= 0f) StartHunt();
        }
        else if (phase == PH_HUNT)
        {
            huntElapsed += dt;
            UpdateHunt();
        }
        else if (phase == PH_END)
        {
            if (left <= 0f || Input.GetKeyDown(KeyCode.Return)) { phase = PH_RESULT; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
    }

    void StartHunt()
    {
        phase = PH_HUNT; left = huntLength; huntElapsed = 0f;
        AudioHub.I.Play("go", 0.8f);
        AudioHub.I.Play("jingles_PIZZI07", 0.5f);
        // every hider has their own forced-taunt clock, started when the hunters are released (6.4);
        // the bots' clocks are staggered so the taunts come one at a time
        foreach (var h in hiders) h.nextTaunt = Time.time + forcedInterval * Random.Range(0.35f, 1f);
        nextForced = Time.time + forcedInterval;
        foreach (var h in hunters) h.Release();
        Say(mode == Game.MODE_HUNT ? "Hunt! Find the props that are hiders." : "The hunters are released!", 3f);
    }

    void UpdateHunt()
    {
        // endgame: one hider left, one hunter left, or 45 s left (6.6)
        if (!endgame)
        {
            string why = null;
            if (left <= Balance.EndgameAt) why = "45 seconds!";
            else if (startHiders > 1 && HidersAlive == 1) why = "Last hider!";
            else if (startHunters > 1 && HuntersAlive == 1) why = "Last hunter!";
            if (why != null)
            {
                endgame = true;
                Say(why + " Taunts come faster and a wrong prop costs less.", 4f);
                AudioHub.I.Play("jingles_HIT03", 0.6f);
                AudioHub.I.Play("hurry_up", 0.8f);
                float iv = Balance.ForcedTauntEndgame;
                foreach (var h in hiders) if (h.nextTaunt > Time.time + iv) h.nextTaunt = Time.time + iv * Random.Range(0.4f, 1f);
                if (nextForced > Time.time + iv) nextForced = Time.time + iv;
            }
        }
        float interval = endgame ? Balance.ForcedTauntEndgame : forcedInterval;
        // forced taunts of the bots
        foreach (var h in hiders)
        {
            if (!h.alive || Time.time < h.nextTaunt) continue;
            h.nextTaunt = Time.time + interval;
            AudioHub.I.PlayAt(AudioHub.Pick(AudioHub.Taunts), h.Centre, Balance.TauntRange, 1f, Random.Range(0.8f, 1.25f));
            NoiseAt(h.transform.position, Balance.TauntRange, h);
            if (!h.moving && Random.value < Balance.BotHiderMoveChance) RelocateBot(h, false);
        }
        // the player's forced taunt, with a 3 s warning
        if (mode == Game.MODE_HIDE && !playerCaught)
        {
            float t = nextForced - Time.time;
            if (t <= Balance.ForcedTauntWarning && !warned) { warned = true; AudioHub.I.Play("tick_001", 0.8f); P.Say("Forced taunt in 3 s: move, or taunt now with T", 3f); }
            if (t <= 0f) P.Taunt(true);
        }
        // the end of the round
        if (HidersAlive == 0) EndRound("Every hider was caught");
        else if (HuntersAlive == 0) EndRound("Every hunter is out");
        else if (left <= 0f) EndRound("Time is up");
    }

    public void PlayerTaunted(bool forced)
    {
        // a taunt restarts that hider's forced-taunt clock (6.3)
        nextForced = Time.time + (endgame ? Balance.ForcedTauntEndgame : forcedInterval);
        warned = false;
    }

    // a sound that hunters within earshot walk to
    public void NoiseAt(Vector3 pos, float range, object source)
    {
        if (phase != PH_HUNT) return;
        foreach (var h in hunters)
            if (h.alive && Vector3.Distance(h.transform.position, pos) <= range) h.Hear(pos);
    }

    // ---------------------------------------------------------------- shots (05 section 5.3)
    // Returns what the dart hit: 0 nothing, 1 architecture or a door, 2 a real prop, 3 a hider.
    int Shot(Vector3 origin, Vector3 dir, Vector3 muzzle, out RaycastHit hit)
    {
        Physics.SyncTransforms();
        bool any = Physics.SphereCast(origin, Balance.DartRayRadius, dir, out hit, Balance.DartRange, Layers.DartMask, QueryTriggerInteraction.Ignore);
        Vector3 end = any ? hit.point : origin + dir * Balance.DartRange;
        if (!any) { DartFx.Spawn(muzzle, end, false, Vector3.zero); return 0; }
        var bh = hit.collider.GetComponentInParent<BotHider>();
        var hb = hit.collider.GetComponentInParent<PlayerHitbox>();
        var pi = hit.collider.GetComponentInParent<PropInstance>();
        bool door = hit.collider.gameObject.layer == Layers.Door;
        int kind = (bh != null || hb != null) ? 3 : ((pi != null && !door) ? 2 : 1);
        DartFx.Spawn(muzzle, end, kind == 1, hit.normal);
        return kind;
    }

    // the wrong-prop consequences that are the same for every shooter: the knock and its sounds
    void KnockProp(PropInstance pi, Vector3 dir, Vector3 point)
    {
        bool moved = pi.Knock(dir, point, 1f);
        AudioHub.I.PlayAt(moved ? AudioHub.KnockSound(pi.def) : AudioHub.Pick(AudioHub.Thud), point, 16f, 0.9f, Random.Range(0.9f, 1.1f));
    }

    // the player's dart
    public void ResolveShot(Player p, Vector3 origin, Vector3 dir, Vector3 muzzle)
    {
        RaycastHit hit;
        int kind = Shot(origin, dir, muzzle, out hit);
        if (kind == 0) return;
        if (kind == 1) { AudioHub.I.PlayAt(AudioHub.Pick(AudioHub.Thud), hit.point, 12f, 0.6f); return; }
        if (kind == 3)
        {
            var bh = hit.collider.GetComponentInParent<BotHider>();
            if (bh == null || !bh.alive) return;
            bool caught = bh.TakeHit(Balance.DartDamage, dir);
            p.hitsLanded++;
            p.hunterHealth = Mathf.Min(Balance.HunterHealth, p.hunterHealth + Balance.HitReward);
            p.hitMarkUntil = Time.time + 0.35f;
            AudioHub.I.Play("confirmation_002", 0.8f);
            AudioHub.I.PlayAt("impactPunch_medium_000", hit.point, 14f, 0.8f);
            if (caught)
            {
                p.catches++;
                bh.survived = huntElapsed;
                Say("Caught " + bh.label + ", who was a " + GameUI.DefName(bh.def).ToLowerInvariant() + "! " + HidersAlive + " left.", 4f);
                AudioHub.I.Play("jingles_STEEL04", 0.6f);
            }
            else
            {
                p.Say("Hit! +" + Balance.HitReward + " health. It runs...", 2f);
                RelocateBot(bh, true);   // found: run or take the next dart
            }
            return;
        }
        // a real prop: knocked around, and it costs the hunter health (G17)
        var pi = hit.collider.GetComponentInParent<PropInstance>();
        KnockProp(pi, dir, hit.point);
        if (phase != PH_HUNT) return;
        if (Time.time - pi.lastCharged < Balance.WrongPropSameWindow) return;   // one mistake, one cost
        pi.lastCharged = Time.time;
        p.wrongProps++;
        p.hunterHealth -= Penalty;
        p.hurtUntil = Time.time + 0.4f;
        AudioHub.I.PlayAt("lowDown", hit.point, 14f, 0.7f);
        AudioHub.I.Play("error_006", 0.6f);
        p.Say("Just a " + GameUI.DefName(pi.def).ToLowerInvariant() + ". -" + Penalty + " health", 2f);
        if (p.hunterHealth <= 0)
        {
            p.hunterHealth = 0;
            p.role = Player.ROLE_DEAD;
            Say("You are out of health: out for the round.", 4f);
            AudioHub.I.Play("game_over", 0.8f);
        }
    }

    // a bot hunter's dart
    public void BotShot(BotHunter b, Vector3 origin, Vector3 dir)
    {
        RaycastHit hit;
        // the player's disguise is only a target while a shot is being resolved (its colliders would block the player otherwise)
        P.SetHitbox(mode == Game.MODE_HIDE && !playerCaught && P.def >= 0);
        int kind = Shot(origin, dir, origin + dir * 0.4f, out hit);
        P.SetHitbox(false);
        if (kind == 0) return;
        if (kind == 1) { AudioHub.I.PlayAt(AudioHub.Pick(AudioHub.Thud), hit.point, 12f, 0.6f); return; }
        if (kind == 3)
        {
            b.hitsLanded++;
            b.health = Mathf.Min(Balance.HunterHealth, b.health + Balance.HitReward);
            AudioHub.I.PlayAt("impactPunch_medium_000", hit.point, 14f, 0.8f);
            var bh = hit.collider.GetComponentInParent<BotHider>();
            if (bh != null)
            {
                if (bh.TakeHit(Balance.DartDamage, dir)) { bh.survived = huntElapsed; Say(b.label + " caught " + bh.label + ".", 3f); }
                else RelocateBot(bh, true);
                return;
            }
            // the player was hit: damage, a 0.3 m slide, unpinned for 1.5 s (05 section 3.9)
            P.health -= Balance.DartDamage;
            P.hurtUntil = Time.time + 0.4f;
            P.Unpin(Balance.HitNoSettle);
            Vector3 slide = new Vector3(dir.x, 0f, dir.z).normalized * Balance.HitSlide;
            if (P.body.Fits(P.body.pos + slide, P.body.yaw)) P.body.pos += slide;
            AudioHub.I.Play("error_006", 0.7f);
            if (P.health <= 0)
            {
                P.health = 0;
                playerCaught = true; playerSurvived = huntElapsed;
                playerCaughtPos = P.body.pos; playerCaughtDef = P.def;
                Say("You were caught by " + b.label + ". You are out: walk the arena unseen.", 5f);
                AudioHub.I.Play("you_lose", 0.8f);
                P.role = Player.ROLE_DEAD;
                P.BecomeWalker();
            }
            else P.Say("Hit by " + b.label + "! " + P.health + " health left. Move!", 2.5f);
            return;
        }
        var pi = hit.collider.GetComponentInParent<PropInstance>();
        KnockProp(pi, dir, hit.point);
        if (Time.time - pi.lastCharged < Balance.WrongPropSameWindow) return;
        pi.lastCharged = Time.time;
        b.wrongProps++;
        b.health -= Penalty;
        if (b.health <= 0)
        {
            b.health = 0; b.alive = false;
            b.gameObject.SetActive(false);
            Say(b.label + " ran out of health and is out.", 3f);
        }
    }

    // ---------------------------------------------------------------- the end: survival share and the reveal
    void EndRound(string reason)
    {
        endReason = reason;
        phase = PH_END; left = Balance.RoundEnd;
        AudioHub.I.Play("time_over", 0.8f);
        AudioHub.I.Play("jingles_STEEL10", 0.5f);
        // survival share (8.1): a hider never caught gets the full hunt length, also when every hunter is out
        float sum = 0f;
        int n = 0;
        resultLines.Clear();
        ClearBeacons();
        foreach (var h in hiders)
        {
            float t = h.caught ? h.survived : huntLength;
            sum += t; n++;
            string what = GameUI.DefName(h.def).ToLowerInvariant();
            resultLines.Add(h.label + ": a " + what + " " + h.spotNote + (h.caught ? " - caught after " + FormatTime(h.survived) : " - survived"));
            AddBeacon(h.transform.position, h.label + ": " + what + (h.caught ? " (caught " + FormatTime(h.survived) + ")" : " (survived)"), !h.caught);
            if (!h.caught) h.gameObject.SetActive(true);
        }
        if (mode == Game.MODE_HIDE)
        {
            float t = playerCaught ? playerSurvived : huntLength;
            sum += t; n++;
            int d = playerCaught ? playerCaughtDef : P.def;
            string what = d >= 0 ? GameUI.DefName(d).ToLowerInvariant() : "prop";
            resultLines.Insert(0, "You: a " + what + (playerCaught ? " - caught after " + FormatTime(playerSurvived) : " - survived"));
            AddBeacon(playerCaught ? playerCaughtPos : P.body.pos, "You: " + what + (playerCaught ? " (caught " + FormatTime(playerSurvived) + ")" : " (survived)"), !playerCaught);
        }
        survivalShare = n > 0 ? Mathf.RoundToInt(100f * sum / (n * huntLength)) : 0;
        if (mode == Game.MODE_HUNT)
            resultLines.Add("You: " + P.hitsLanded + " hits, " + P.catches + " catches, " + P.wrongProps + " wrong props, " + P.hunterHealth + " health left" + (P.role == Player.ROLE_DEAD ? " (out)" : ""));
        foreach (var h in hunters)
            resultLines.Add(h.label + ": " + h.hitsLanded + " hits, " + h.wrongProps + " wrong props, " + h.health + " health left" + (h.alive ? "" : " (out)"));
        // the reveal: the map from above, roofs off, a marker on every hider
        Game.I.SetTopView(true);
    }

    void AddBeacon(Vector3 pos, string text, bool survived)
    {
        if (beamGreen == null)
        {
            beamGreen = new Material(Shader.Find("PropHunt/Unlit")); beamGreen.SetColor("_Color", new Color(0.2f, 0.9f, 0.3f, 0.85f));
            beamRed = new Material(Shader.Find("PropHunt/Unlit")); beamRed.SetColor("_Color", new Color(0.95f, 0.25f, 0.2f, 0.85f));
        }
        var mb = new MeshBuf();
        Color32 w = new Color32(255, 255, 255, 255);
        mb.Box(new Vector3(-0.06f, 0f, -0.06f), new Vector3(0.06f, 30f, 0.06f), w);
        // a ring on the ground plane, seen from above
        mb.Box(new Vector3(-0.9f, 4.0f, -0.9f), new Vector3(0.9f, 4.02f, -0.75f), w);
        mb.Box(new Vector3(-0.9f, 4.0f, 0.75f), new Vector3(0.9f, 4.02f, 0.9f), w);
        mb.Box(new Vector3(-0.9f, 4.0f, -0.75f), new Vector3(-0.75f, 4.02f, 0.75f), w);
        mb.Box(new Vector3(0.75f, 4.0f, -0.75f), new Vector3(0.9f, 4.02f, 0.75f), w);
        var go = mb.ToObject("Beacon", null, survived ? beamGreen : beamRed);
        go.transform.position = new Vector3(pos.x, 0f, pos.z);
        beacons.Add(go); beaconPos.Add(pos); beaconText.Add(text);
    }

    public static string FormatTime(float s)
    {
        int t = Mathf.Max(0, Mathf.RoundToInt(s));
        return (t / 60) + ":" + (t % 60).ToString("00");
    }

    // ---------------------------------------------------------------- checks
    public string StatusLine()
    {
        string s = "round phase " + phase + " left " + left.ToString("0") + " hiders " + HidersAlive + "/" + startHiders + " hunters " + HuntersAlive + "/" + startHunters
            + " endgame " + endgame + " penalty " + Penalty + " share " + survivalShare + " banner '" + Banner + "'";
        for (int i = 0; i < hiders.Count; i++)
        {
            var h = hiders[i];
            s += " | h" + i + " " + GenData.defId[h.def] + " " + h.transform.position.x.ToString("0.0") + "," + h.transform.position.y.ToString("0.00") + "," + h.transform.position.z.ToString("0.0")
                + " hp " + h.health + (h.alive ? "" : " caught") + (h.moving ? " moving" : "") + " [" + h.spotNote + "]";
        }
        for (int i = 0; i < hunters.Count; i++)
            s += " | bot" + i + " hp " + hunters[i].health + " wrong " + hunters[i].wrongProps + " hits " + hunters[i].hitsLanded + (hunters[i].alive ? "" : " out");
        return s;
    }

    // commands for the build checks: round skip | round end | round goto <i> | round restart
    public string RoundCommand(string[] a)
    {
        if (a.Length < 2) return "?";
        if (a[1] == "skip") { left = Mathf.Min(left, 0.2f); return "skipped"; }
        if (a[1] == "end" && phase == PH_HUNT) { EndRound("Ended by a test command"); return "ended"; }
        if (a[1] == "restart") { StartRound(); return "restarted"; }
        if (a[1] == "goto" && a.Length > 2)
        {
            // stand 2.5 m from bot hider i, looking at it
            var h = hiders[int.Parse(a[2])];
            Vector3 c = h.Centre;
            var g = Map.gen;
            int cell = Map.CellAt(c);
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < g.IW * g.IH; i++)
            {
                if (g.walk[i] != 1 || g.reach[i] == 0) continue;
                Vector3 cc = Map.CellCentre(i);
                float d = Mathf.Abs(Vector3.Distance(cc, new Vector3(c.x, 0f, c.z)) - 2.5f);
                if (d < bestD && !Physics.Linecast(cc + Vector3.up * Balance.HunterEye, c, Layers.CameraMask)) { bestD = d; best = i; }
            }
            if (best < 0) return "no free spot near " + cell;
            Vector3 at = Map.CellCentre(best);
            P.Teleport(at + Vector3.up * 0.05f);
            Vector3 to = c - (at + Vector3.up * Balance.HunterEye);
            P.SetLook(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
            return "at " + at.x.ToString("0.0") + " " + at.z.ToString("0.0");
        }
        return "?";
    }
}
