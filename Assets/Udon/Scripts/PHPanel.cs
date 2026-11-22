using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

// The host panel in the lobby (the "Board Game" design): four coloured tracks (map size, props, round length,
// forced taunt) with a red meeple on the current pick, a die that starts the round on a NEW MAP, beside it the card
// of the map REPLAY plays again (PHHistory draws it; arrows step through the trail of played maps), and a bench
// showing who stands in the zone. Built by LobbyBuilder. Everyone sees the same synced settings; only the host's presses count.
// Every change of a setting plays its sound for everyone near the panel: map size plucks higher for bigger maps
// (Auto rattles), props drop 1-5 crates, round length ticks 2-4 times, taunt rings 3/2/1 bells.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHPanel : UdonSharpBehaviour
{
    public PHGame game;

    // the tiles, in parallel arrays: kind 0 size, 1 props, 2 round, 3 taunt, 4 see hiders, 5 no placing (PHGame.noPlacing); arg
    // is the PHGame.Panel* argument (kinds 4 and 5 are switches: one tile each, arg 1, its meeple on while it is on)
    public int[] tileKind;
    public int[] tileArg;
    public GameObject[] tileMarker;      // the red meeple on the picked tile
    public GameObject[] tileAuto;        // the dashed outline: the size Auto picked
    public RectTransform[] tileRect;
    public TextMeshProUGUI autoCaption;  // "Auto · M"
    public TextMeshProUGUI[] roundCaption;   // hunt time for Short, Normal, Long at the current size
    public TextMeshProUGUI[] switchCaption;  // ON / OFF on the switch tiles (kinds 4, 5: index kind - 4)
    public Image noPlacingIcon;              // NO PLACING's picture (Cor: a sticker and an item in a crossed-out circle), grey while off
    public Sprite noPlacingOn, noPlacingOff;
    public Image noPlacingTile;              // ...and the tile itself, its row colour while on, grey while off (Cor)
    public Color noPlacingColour, noPlacingGrey;

    public TextMeshProUGUI benchText;    // "5 Props · 2 Hunters"
    public Image[] benchMeeples;
    public Sprite meepleHunt, meepleHide;

    public RectTransform dice;
    public Image diceImage;
    public TextMeshProUGUI rollCaption;
    public GameObject veil;              // "Round in progress..."
    public RectTransform veilHourglass;
    // the host bar: who hosts, and the buttons to hand the panel over (PHGame's hostLocked / UnlockHost / LockHost /
    // ClaimHost, called by name)
    public TextMeshProUGUI hostName;
    public GameObject claimLock;
    // the map bar: the map's code, a host-only code entry, the seed message (or Play anyway / Discard), runs, votes
    public TextMeshProUGUI seedCodeText, seedStatusText, runsText, scrapText, undoText;
    public TMP_InputField seedInput;
    public GameObject seedInputRoot, confirmRoot;           // shown on Claim host instead of its text while it cannot be used
    // the bar buttons (PHPanelTile index -2 - k): 0 unlock/lock host, 1 claim host, 2 the seed field, 3 play anyway,
    // 4 discard, 5 unused (Cor: no retire button), 6 the REPLAY card, 7 scrap vote, 8 undo (a vote; the host's is at once),
    // 9 and 10 the arrows beside REPLAY (older, newer)
    public TextMeshProUGUI replayCaption;
    public GameObject replayBadge;   // "1.5x COINS!!" on the REPLAY card while the picked map pays it (PHGame.SlotBonus)
    public GameObject newBadge;      // "2x COINS!!" on the die while NEW MAP is on offer (PHGame.offerNew)
    public RectTransform[] hostButton;
    public Image[] hostButtonImage;
    public TextMeshProUGUI[] hostButtonText;
    public Transform spinner;           // the ceiling spinner's arrow: spins when a round starts

    public AudioSource sfx;
    public AudioClip pluck, select, drop, tick, bong, error, jingle;

    readonly string[] sizeCap = { "S", "M", "L", "XL", "XS" };
    readonly float[] huntPhase = { 180f, 210f, 240f, 270f, 150f };       // PHGame's balance (S, M, L, XL, XS)
    readonly float[] roundFactor = { 0.75f, 1f, 1.5f };

    int seenPhase = -1; float spinStart = -10f; float spinFrom;
    int seenSize = -99, seenDensity = -1, seenRound = -1, seenTaunt = -1, seenSee = -1, seenLayers = -1;
    float nextRefresh;
    int wobble = -99; float wobbleStart;
    // the map code's feedback (Cor's playtest: entering a code said nothing): a local note for a moment after the
    // field is used (a code typed by a guest, or one being checked), then the game's synced message, in colour, and a
    // little pop whenever it changes
    string localNote = ""; float localUntil; string lastStatus = ""; float statusPop = -9f; bool noteChecking; string statusAtNote = "";
    public Color statusOk = new Color(0.62f, 0.86f, 0.55f), statusWarn = new Color(1f, 0.78f, 0.36f), statusPlain = new Color(0.79f, 0.85f, 0.79f);
    float diceStart = -10f;

    // queued sounds (sequences: crate drops, ticks, bells)
    AudioClip[] qClip = new AudioClip[12];
    float[] qTime = new float[12];
    float[] qPitch = new float[12];

    bool Host { get { return Networking.IsOwner(game.gameObject); } }
    bool CanEdit { get { return Host && game.phase == PHGame.PH_LOBBY; } }
    bool CanStart { get { return CanEdit && game.Participants() >= game.minPlayers; } }
    int SizeValue { get { return game.sizeAuto ? -1 : game.sizeChoice; } }

    int Value(int kind)
    {
        if (kind == 0) return SizeValue;
        if (kind == 1) return game.densityChoice;
        if (kind == 2) return game.roundLength;
        if (kind == 4) return game.seeHiders ? 1 : 0;
        if (kind == 5) return game.noPlacing ? 1 : 0;
        return game.tauntInterval;
    }

    // ------------------------------------------------------------------ presses (PHPanelTile)
    public void TilePress(int i)
    {
        if (!CanEdit) { Deny(i); return; }
        int kind = tileKind[i], arg = tileArg[i];
        if (kind >= 4)
        {
            // a switch: each press turns it over
            game.panelArg = Value(kind) == 1 ? 0 : 1;
            if (kind == 4) game.PanelSeeHiders(); else game._PanelNoPlacing();
            Refresh();
            return;
        }
        if (Value(kind) == arg) { OptionSound(kind, arg); return; }   // no change, so no change sound: play it here
        game.panelArg = arg;
        if (kind == 0) game.PanelSize();
        else if (kind == 1) game.PanelDensity();
        else if (kind == 2) game.PanelRoundLength();
        else game.PanelTaunt();
        Refresh();
    }

    public void StartPress()
    {
        if (!CanStart) { Deny(-1); return; }
        diceStart = Time.time;
        Queue(select, 0f, 1.3f); Queue(select, 0.18f, 1.1f); Queue(jingle, 0.52f, 1f);
        game.StartRound();
    }

    void Fade(int k, bool on)
    {
        Color c = hostButtonImage[k].color; float a = on ? 1f : 0.35f;
        if (c.a != a) { c.a = a; hostButtonImage[k].color = c; }
    }

    bool HostLocked
    {
        get { object o = game.GetProgramVariable("hostLocked"); return o == null || (bool)o; }
    }

    public void BarPress(int k)
    {
        bool host = Host;
        if (k == 0)
        {
            if (!host) { Deny(-2); return; }
            Queue(select, 0f, 1f);
            game.SendCustomEvent(HostLocked ? "UnlockHost" : "LockHost");
        }
        else if (k == 1)
        {
            if (host || !game.CanClaim()) { Deny(-3); return; }
            Queue(jingle, 0f, 1.25f);
            game.SendCustomEvent("ClaimHost");
        }
        else if (k == 2) { }   // the code field: anyone may open it to copy the code; a host's new code goes in SeedEntered
        else if (k == 3 || k == 4)
        {
            if (!host || !game.seedNeedsConfirm) { Deny(-2 - k); return; }
            Queue(select, 0f, 1f);
            if (k == 3) game.SeedPlayAnyway(); else game.SeedDiscard();
        }
        else if (k == 6)
        {
            // REPLAY: the picked map again (two card flicks, then the jingle a little lower than NEW MAP's)
            if (!CanStart || !game.CanReplay()) { Deny(-8); return; }
            Queue(tick, 0f, 1.2f); Queue(tick, 0.12f, 1.4f); Queue(jingle, 0.4f, 0.84f);
            game.StartReplay();
        }
        else if (k == 9 || k == 10)
        {
            if (!CanEdit || !game.CanReplay()) { Deny(-2 - k); return; }
            Queue(tick, 0f, k == 9 ? 0.9f : 1.1f);
            if (k == 9) game.PickOlder(); else game.PickNewer();
        }
        Refresh();
    }

    // the code field (Cor: the map's code and the code entry are one field): clicking it opens VRChat's text box
    // with the code in it, ready to copy; the host may type another code there instead. OnSelect:
    public void SeedSelected() { seedEditing = true; }

    // the code field's OnEndEdit: a different code from the host is submitted; the field shows the map's code again
    public void SeedEntered()
    {
        seedEditing = false;
        string t = seedInput.text;
        if (seedInput.text != seedShown) seedInput.text = seedShown;
        if (t == null) return;
        t = t.Trim();
        if (t.Length == 0 || t == seedShown) return;
        if (!CanEdit) { Deny(-4); Note(Host ? "seed.lobbyOnly" : "seed.hostOnly"); return; }
        Queue(select, 0f, 1f);
        game.SubmitSeed(t);   // (the host owns the game: its message is set at once)
        localUntil = 0f; lastStatus = ""; nextRefresh = 0f;   // show it now, with a pop even if it is the same as before
    }

    void Note(string key)
    {
        localNote = game.text.Get(key); localUntil = Time.time + 4f; nextRefresh = 0f;
        noteChecking = key == "seed.checking"; statusAtNote = game.seedStatus;
    }

    void Deny(int i)
    {
        Queue(error, 0f, 1f);
        wobble = i; wobbleStart = Time.time;
    }

    // ------------------------------------------------------------------ sounds
    void Queue(AudioClip c, float delay, float pitch)
    {
        if (c == null) return;
        for (int k = 0; k < qClip.Length; k++)
            if (qClip[k] == null) { qClip[k] = c; qTime[k] = Time.time + delay; qPitch[k] = pitch; return; }
    }

    void OptionSound(int kind, int arg)
    {
        if (kind == 0)
        {
            if (arg < 0) { Queue(select, 0f, 1.2f); Queue(select, 0.07f, 1.45f); Queue(select, 0.15f, 1.3f); }
            else Queue(pluck, 0f, arg == 4 ? 0.58f : arg == 0 ? 0.72f : arg == 1 ? 0.95f : arg == 2 ? 1.2f : 1.5f);
        }
        else if (kind == 1)
        {
            int n = arg == 5 ? 1 : arg == 3 ? 1 : arg == 1 ? 2 : arg == 0 ? 3 : arg == 2 ? 4 : 5;
            for (int k = 0; k < n; k++) Queue(drop, k * 0.11f, arg == 5 ? 1.25f : 1f);   // (Minimal: one small crate)
        }
        else if (kind == 2)
        {
            float gap = arg == 0 ? 0.14f : arg == 1 ? 0.26f : 0.4f;
            for (int k = 0; k < arg + 2; k++) Queue(tick, k * gap, 1f);
        }
        else if (kind >= 4) { Queue(select, 0f, arg == 1 ? 1.25f : 0.85f); }   // a switch: up when on, down when off
        else
        {
            int n = arg <= 30 ? 3 : arg <= 40 ? 2 : 1;
            for (int k = 0; k < n; k++) Queue(bong, k * 0.28f, 1.05f);
        }
    }

    // ------------------------------------------------------------------ every frame
    // profiling (PHProfiler): while it runs, the time this script's updates take adds up in profMs
    [System.NonSerialized] public System.Diagnostics.Stopwatch profWatch;
    [System.NonSerialized] public double profMs;

    void Update()
    {
        if (profWatch == null) { UpdateBody(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        UpdateBody();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    void UpdateBody()
    {
        if (game == null) return;
        float now = Time.time;
        for (int k = 0; k < qClip.Length; k++)
        {
            if (qClip[k] == null || now < qTime[k]) continue;
            sfx.pitch = qPitch[k];
            sfx.PlayOneShot(qClip[k]);
            qClip[k] = null;
        }

        // a setting changed (anyone's client: it is synced): play its sound
        int sz = SizeValue, de = game.densityChoice, rl = game.roundLength, ta = game.tauntInterval;
        if (seenSize == -99) { seenSize = sz; seenDensity = de; seenRound = rl; seenTaunt = ta; }
        if (sz != seenSize) { seenSize = sz; OptionSound(0, sz); Refresh(); }
        if (de != seenDensity) { seenDensity = de; OptionSound(1, de); Refresh(); }
        if (rl != seenRound) { seenRound = rl; OptionSound(2, rl); Refresh(); }
        if (ta != seenTaunt) { seenTaunt = ta; OptionSound(3, ta); Refresh(); }
        int sh = Value(4), ly = Value(5);
        if (seenSee < 0) { seenSee = sh; seenLayers = ly; }
        if (sh != seenSee) { seenSee = sh; OptionSound(4, sh); Refresh(); }
        if (ly != seenLayers) { seenLayers = ly; OptionSound(5, ly); Refresh(); }

        // (Perf: four times a second in the lobby and at a round's end, where the votes are counted; while a map
        // loads and the round is played, behind the veil, every two seconds; at once when the phase changes. The
        // panel shows no clock)
        bool busyPhase = game.phase == PHGame.PH_LOADING || game.phase == PHGame.PH_BRIEF || game.phase == PHGame.PH_HIDE || game.phase == PHGame.PH_HUNT;
        if (seedStatusText != null && now - statusPop < 0.5f)
        {
            float pop = now - statusPop;
            seedStatusText.transform.localScale = Vector3.one * (pop < 0.35f ? 1f + 0.18f * (1f - pop / 0.35f) : 1f);
        }
        if (now >= nextRefresh) { nextRefresh = now + (busyPhase ? 2f : 0.25f); Refresh(); }

        // little animations: a refused press wobbles, START spins the die, the veil's hourglass turns
        if (wobble != -99)
        {
            float t = (now - wobbleStart) / 0.38f;
            RectTransform r = wobble == -1 ? dice : wobble < -1 ? hostButton[-2 - wobble] : tileRect[wobble];
            if (t >= 1f) { r.localRotation = Quaternion.identity; wobble = -99; }
            else r.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 18f) * 7f * (1f - t));
        }
        float dt = (now - diceStart) / 0.7f;
        if (dt >= 0f && dt <= 1.2f)
        {
            float e = dt >= 1f ? 1f : 1f - (1f - dt) * (1f - dt) * (1f - dt);
            dice.localRotation = Quaternion.Euler(0f, 0f, -360f * e);
            dice.anchoredPosition = new Vector2(dice.anchoredPosition.x, diceY + Mathf.Sin(Mathf.Min(1f, dt) * Mathf.PI) * 50f);
        }
        // a round starts: the ceiling spinner whirls three and a bit turns (everyone sees it)
        if (game.phase != seenPhase)
        {
            if (seenPhase == PHGame.PH_LOBBY && game.phase != PHGame.PH_LOBBY && spinner != null) { spinStart = now; spinFrom = spinner.localEulerAngles.y; }
            seenPhase = game.phase;
            nextRefresh = 0f;
        }
        float st = (now - spinStart) / 2.6f;
        if (spinner != null && st >= 0f && st <= 1f)
        {
            float e = 1f - (1f - st) * (1f - st) * (1f - st);
            spinner.localRotation = Quaternion.Euler(0f, spinFrom + e * (1080f + 137f), 0f);
        }
        // the hourglass rests, then turns over now and again (Cor: not constantly)
        if (veil.activeSelf)
        {
            float k = Mathf.Floor(now / HOURGLASS_EVERY), into = now - k * HOURGLASS_EVERY;
            veilHourglass.localRotation = Quaternion.Euler(0f, 0f, k * 180f + Mathf.SmoothStep(0f, 180f, Mathf.Clamp01((into - (HOURGLASS_EVERY - 0.6f)) / 0.6f)));
        }
    }

    const float HOURGLASS_EVERY = 6f;   // seconds between the veil's hourglass turning over

    [HideInInspector] public float diceY;
    int codeSlot = -1; uint codeSeed; string codeText = "";
    string seedShown = ""; bool seedEditing;   // the merged code field: the code it shows, and whether VRChat's text box is open on it

    // ------------------------------------------------------------------ the picture
    void Start() { if (game != null && game.text != null) game.text.Register(this); }
    public void _OnTextLanguage() { Refresh(); }

    public void Refresh()
    {
        if (game == null) return;
        int auto = game.RoundSize();
        if (!game.sizeAuto) auto = game.AutoSize(game.Participants());
        for (int i = 0; i < tileKind.Length; i++)
        {
            int kind = tileKind[i];
            bool on = Value(kind) == tileArg[i];
            if (tileMarker[i].activeSelf != on) tileMarker[i].SetActive(on);
            bool ap = kind == 0 && game.sizeAuto && tileArg[i] == auto;
            if (tileAuto[i].activeSelf != ap) tileAuto[i].SetActive(ap);
        }
        autoCaption.text = game.text.F1("panel.auto", sizeCap[auto]);
        if (switchCaption != null)
            for (int k = 0; k < switchCaption.Length; k++)
                if (switchCaption[k] != null) switchCaption[k].text = game.text.Get(Value(4 + k) == 1 ? "panel.on" : "panel.off");
        if (noPlacingIcon != null)
        {
            Sprite want = game.noPlacing ? noPlacingOn : noPlacingOff;
            if (noPlacingIcon.sprite != want) noPlacingIcon.sprite = want;
        }
        if (noPlacingTile != null)
        {
            Color want = game.noPlacing ? noPlacingColour : noPlacingGrey;
            if (noPlacingTile.color != want) noPlacingTile.color = want;
        }
        int sizeNow = game.sizeAuto ? auto : game.sizeChoice;
        for (int r = 0; r < roundCaption.Length; r++)
        {
            int s = Mathf.RoundToInt(Mathf.Round(huntPhase[sizeNow] * roundFactor[r]));
            roundCaption[r].text = (s / 60) + ":" + (s % 60).ToString("00");
        }

        int n = game.Participants();
        int h = n > 0 ? PHGame.Hunters(n) : 0;
        int props = n - h;
        benchText.text = game.text.F2(props == 1 ? (h == 1 ? "panel.bench.11" : "panel.bench.1n") : (h == 1 ? "panel.bench.n1" : "panel.bench.nn"), props.ToString(), h.ToString());
        // meeples in rows of up to 11, each row centred
        int perRow = 11;
        for (int i = 0; i < benchMeeples.Length; i++)
        {
            Image m = benchMeeples[i];
            bool show = i < n;
            if (m.gameObject.activeSelf != show) m.gameObject.SetActive(show);
            if (!show) continue;
            m.sprite = i < h ? meepleHunt : meepleHide;
            int row = i / perRow, col = i % perRow;
            int inRow = Mathf.Min(perRow, n - row * perRow);
            m.rectTransform.anchoredPosition = new Vector2((col - (inRow - 1) * 0.5f) * 22f, -row * 26f);
        }

        bool go = CanStart;
        diceImage.color = go ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.6f);
        rollCaption.color = go ? new Color(0.13f, 0.34f, 0.25f) : new Color(0.6f, 0.54f, 0.41f);
        bool again = go && game.CanReplay();
        if (replayCaption != null) replayCaption.color = again ? new Color(0.13f, 0.34f, 0.25f) : new Color(0.6f, 0.54f, 0.41f);
        bool lobbyNow = game.phase == PHGame.PH_LOBBY;
        bool b15 = lobbyNow && game.CanReplay() && game.SlotBonus(game.replayPick) > 0;
        if (replayBadge != null && replayBadge.activeSelf != b15) replayBadge.SetActive(b15);
        bool b2 = lobbyNow && game.offerNew;
        if (newBadge != null && newBadge.activeSelf != b2) newBadge.SetActive(b2);
        if (hostButtonImage.Length > 10) { Fade(6, again); Fade(9, CanEdit && game.CanReplay()); Fade(10, CanEdit && game.CanReplay()); }
        if (hostName != null)
        {
            VRCPlayerApi owner = Networking.GetOwner(game.gameObject);
            hostName.text = Utilities.IsValid(owner) ? owner.displayName : "";
            bool locked = HostLocked, host = Host;
            hostButtonText[0].text = game.text.Get(locked ? "panel.unlockHost" : "panel.lockHost");
            Color c0 = hostButtonImage[0].color; c0.a = host ? 1f : 0.35f; hostButtonImage[0].color = c0;
            bool claimable = !host && game.CanClaim();
            Color c1 = hostButtonImage[1].color; c1.a = claimable ? 1f : 0.35f; hostButtonImage[1].color = c1;
            if (hostButtonText[1].gameObject.activeSelf != claimable) hostButtonText[1].gameObject.SetActive(claimable);
            if (claimLock.activeSelf == claimable) claimLock.SetActive(!claimable);
        }
        if (seedCodeText != null)
        {
            bool lobby = game.phase == PHGame.PH_LOBBY, hostNow = Host;
            // the code of the map REPLAY shows (the last one played unless the host stepped to another)
            string code = game.seedCode;
            int pk = game.replayPick;
            if (pk >= 0 && pk < game.rotSeed.Length && pk != game.lastRot && game.rotSeed[pk] != 0 && game.seedTool != null)
            {
                if (pk != codeSlot || game.rotSeed[pk] != codeSeed)
                {
                    codeSlot = pk; codeSeed = game.rotSeed[pk];
                    codeText = game.seedTool.Encode(GenData.GEN_VERSION, game.rotSize[pk], codeSeed, game.rotDensity[pk]);
                }
                code = codeText;
            }
            // a code the host entered for the next map shows in the field until that map is built (Cor: feedback)
            if (lobby && game.pendingCode != null && game.pendingCode != "") code = game.pendingCode;
            seedShown = code;
            if (!seedEditing && seedInput.text != seedShown) seedInput.text = seedShown;
            bool confirm = game.seedNeedsConfirm && hostNow;
            if (confirmRoot.activeSelf != confirm) confirmRoot.SetActive(confirm);
            string st = confirm ? "" : game.text.Coded(game.seedStatus);
            // (a "checking" note gives way as soon as the game's answer comes)
            bool noted = Time.time < localUntil && !(noteChecking && game.seedStatus != statusAtNote);
            string show = noted ? localNote : st;
            if (show != lastStatus) { lastStatus = show; statusPop = Time.time; }
            seedStatusText.text = show;
            string sk = game.seedStatus;
            seedStatusText.color = noted ? statusWarn : (sk.StartsWith("seed.next") ? statusOk : (sk == "" ? statusPlain : statusWarn));

            if (!seedInputRoot.activeSelf) seedInputRoot.SetActive(true);
            bool anyPick = pk >= 0 && pk < game.rotRuns.Length && game.rotSeed[pk] != 0;
            runsText.text = anyPick ? game.text.F1("maps.played", game.rotRuns[pk].ToString()) : "";
        }
        bool lockOn = game.phase != PHGame.PH_LOBBY;
        if (veil.activeSelf != lockOn) veil.SetActive(lockOn);
    }
}
