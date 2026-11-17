using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

// The host panel in the lobby (the "Board Game" design): four coloured tracks (map size, props, round length,
// forced taunt) with a red meeple on the current pick, a die that starts the round, and a bench showing who stands
// in the zone. Built by LobbyBuilder. Everyone sees the same synced settings; only the host's presses count.
// Every change of a setting plays its sound for everyone near the panel: map size plucks higher for bigger maps
// (Auto rattles), props drop 1-5 crates, round length ticks 2-4 times, taunt rings 3/2/1 bells.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHPanel : UdonSharpBehaviour
{
    public PHGame game;

    // the tiles, in parallel arrays: kind 0 size, 1 props, 2 round, 3 taunt; arg is the PHGame.Panel* argument
    public int[] tileKind;
    public int[] tileArg;
    public GameObject[] tileMarker;      // the red meeple on the picked tile
    public GameObject[] tileAuto;        // the dashed outline: the size Auto picked
    public RectTransform[] tileRect;
    public TextMeshProUGUI autoCaption;  // "Auto · M"
    public TextMeshProUGUI[] roundCaption;   // hunt time for Short, Normal, Long at the current size

    public TextMeshProUGUI benchText;    // "5 Props · 2 Hunters"
    public Image[] benchMeeples;
    public Sprite meepleHunt, meepleHide;

    public RectTransform dice;
    public Image diceImage;
    public TextMeshProUGUI rollCaption;
    public GameObject veil;              // "Round in progress..."
    public RectTransform veilHourglass;

    public AudioSource sfx;
    public AudioClip pluck, select, drop, tick, bong, error, jingle;

    readonly string[] sizeCap = { "S", "M", "L", "XL" };
    readonly float[] huntPhase = { 180f, 210f, 240f, 270f };       // PHGame's balance
    readonly float[] roundFactor = { 0.75f, 1f, 1.5f };

    int seenSize = -99, seenDensity = -1, seenRound = -1, seenTaunt = -1;
    float nextRefresh;
    int wobble = -2; float wobbleStart;
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
        return game.tauntInterval;
    }

    // ------------------------------------------------------------------ presses (PHPanelTile)
    public void TilePress(int i)
    {
        if (!CanEdit) { Deny(i); return; }
        int kind = tileKind[i], arg = tileArg[i];
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
            else Queue(pluck, 0f, arg == 0 ? 0.72f : arg == 1 ? 0.95f : arg == 2 ? 1.2f : 1.5f);
        }
        else if (kind == 1)
        {
            int n = arg == 3 ? 1 : arg == 1 ? 2 : arg == 0 ? 3 : arg == 2 ? 4 : 5;
            for (int k = 0; k < n; k++) Queue(drop, k * 0.11f, 1f);
        }
        else if (kind == 2)
        {
            float gap = arg == 0 ? 0.14f : arg == 1 ? 0.26f : 0.4f;
            for (int k = 0; k < arg + 2; k++) Queue(tick, k * gap, 1f);
        }
        else
        {
            int n = arg <= 30 ? 3 : arg <= 40 ? 2 : 1;
            for (int k = 0; k < n; k++) Queue(bong, k * 0.28f, 1.05f);
        }
    }

    // ------------------------------------------------------------------ every frame
    void Update()
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

        if (now >= nextRefresh) { nextRefresh = now + 0.25f; Refresh(); }

        // little animations: a refused press wobbles, START spins the die, the veil's hourglass turns
        if (wobble != -2)
        {
            float t = (now - wobbleStart) / 0.38f;
            RectTransform r = wobble < 0 ? dice : tileRect[wobble];
            if (t >= 1f) { r.localRotation = Quaternion.identity; wobble = -2; }
            else r.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 18f) * 7f * (1f - t));
        }
        float dt = (now - diceStart) / 0.7f;
        if (dt >= 0f && dt <= 1.2f)
        {
            float e = dt >= 1f ? 1f : 1f - (1f - dt) * (1f - dt) * (1f - dt);
            dice.localRotation = Quaternion.Euler(0f, 0f, -360f * e);
            dice.anchoredPosition = new Vector2(dice.anchoredPosition.x, diceY + Mathf.Sin(Mathf.Min(1f, dt) * Mathf.PI) * 50f);
        }
        if (veil.activeSelf) veilHourglass.localRotation = Quaternion.Euler(0f, 0f, Mathf.Floor(now / 1.2f) * 180f + Mathf.SmoothStep(0f, 180f, Mathf.Clamp01((now - Mathf.Floor(now / 1.2f) * 1.2f - 0.7f) / 0.5f)));
    }

    [HideInInspector] public float diceY;

    // ------------------------------------------------------------------ the picture
    public void Refresh()
    {
        if (game == null) return;
        int auto = game.RoundSize();
        if (!game.sizeAuto) { int n0 = game.Participants(); auto = n0 <= 8 ? 0 : (n0 <= 16 ? 1 : (n0 <= 24 ? 2 : 3)); }
        for (int i = 0; i < tileKind.Length; i++)
        {
            int kind = tileKind[i];
            bool on = Value(kind) == tileArg[i];
            if (tileMarker[i].activeSelf != on) tileMarker[i].SetActive(on);
            bool ap = kind == 0 && game.sizeAuto && tileArg[i] == auto;
            if (tileAuto[i].activeSelf != ap) tileAuto[i].SetActive(ap);
        }
        autoCaption.text = "Auto · " + sizeCap[auto];
        int sizeNow = game.sizeAuto ? auto : game.sizeChoice;
        for (int r = 0; r < roundCaption.Length; r++)
        {
            int s = Mathf.RoundToInt(Mathf.Round(huntPhase[sizeNow] * roundFactor[r]));
            roundCaption[r].text = (s / 60) + ":" + (s % 60).ToString("00");
        }

        int n = game.Participants();
        int h = n > 0 ? PHGame.Hunters(n) : 0;
        int props = n - h;
        benchText.text = props + (props == 1 ? " Prop" : " Props") + " · " + h + (h == 1 ? " Hunter" : " Hunters");
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
        bool lockOn = game.phase != PHGame.PH_LOBBY;
        if (veil.activeSelf != lockOn) veil.SetActive(lockOn);
    }
}
