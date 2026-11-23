using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// The diorama's full round replay (Cor: "a full round replay as well", then "I'd like it to be synced"): the last
// round played again on the table, every player moving along their path, each hider as the prop they were at that
// moment, with taunts, hits and catches shown as they happened.
//   Recording: each player records only themselves, on their own client, where their place is exact (a hider's prop
//   as PHHiderBody moves it, a hunter's own feet), four samples a second on the server clock's grid (so every track
//   lines up), packed small (PACKING below). At the END reveal each player sends theirs once on their slot's
//   PHReplayTrack: everyone, a late joiner too, plays the same recording.
//   Playback: shared at the table. Play, pause, speed and the scrub bar are synced (whoever presses takes over), so
//   the people round the table watch the same moment. Shown only while the diorama shows the round's map (PHMiniMap);
//   while it plays, the diorama's own paths, pins and hiding spots step aside (PHMiniMap.SetReplay).
//
// PACKING (PHReplayTrack.data): a header, then one sample per 0.25 s tick:
//   header: 'R', version 2, role, first tick (int32, server ms / 250), sample count (uint16),
//           the hunter's weapon + 1 (PHWeapons model; 0 = none, a hider)                                    10 bytes
//           (version 1, still read: 9 bytes, no weapon)
//   sample: a flags byte, then by the flags
//     F_ABS   x, y, z as int16 in 2 cm steps, map-local (PHGame.MapLocalOf)                          + 6
//     F_DELTA dx, dy, dz as int8 in the same steps (a move of up to 2.5 m a tick)                   + 3
//             (neither: the same place as the last sample)
//     F_YAW   the facing, a byte (256 steps)                                                        + 1
//     F_DEF   hider: the disguise + 1 as uint16 (0 = none)                                          + 2
//             hunter (version 2): swung the weapon this tick (a slash, a VR swing, a strike)         + 0
//     F_EV1   hider: taunted / hunter: hit a wrong prop
//     F_EV2   hider: was hit / hunter: hit a hider
//     F_CATCH hunter: caught a hider
//     F_END   hider caught or hunter out: the track ends here
//   A hider standing still costs one byte a tick: a six minute round is ~2-6 KB per player.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PHReplay : UdonSharpBehaviour
{
    public PHGame game;
    public MapBuilderUdon builder;
    public PHMiniMap mini;
    public PHReplayTrack[] tracks;        // one per slot
    public Transform[] pawns;             // per slot, under the model: a hunter, or a hider not (yet) in a prop
    public Renderer[] pawnRenderer;
    public Transform[] pops;              // per slot: a burst for a taunt, a hit, a catch
    public Renderer[] popRenderer;
    public LineRenderer[] tails;          // per slot: the last few seconds of the path behind them
    public Transform[] tags;              // per slot: their name over them
    public TextMeshPro[] tagLabel;
    // per slot, a hunter (Cor, 2026-11-22): the red meeple holding their weapon, swinging it hard when they swung or
    // hit something (LobbyBuilder.BuildReplay); its body leans with the swing, the hands turn the weapon
    public Transform[] meeples, meepleBody, meepleHand, meepleOffHand;
    public MeshFilter[] meepleWeapon, meepleOff;
    // the controls, a row on each side of the table (Cor: a seek bar on every side), index 0 the near edge's under
    // the diorama's buttons
    public GameObject[] bar;
    public GameObject[] playingOnly;      // the controls shown only while the replay is open (speed, scrub, clock, summary; every side's)
    public TextMeshProUGUI[] playLabel, speedLabel, clock, summaryLabel;
    public Transform[] playhead, huntMark;   // on the scrub bars (canvas units, the middle at 0)
    public float barWidth = 760f;
    public int segments = 20;             // the scrub bar's pressable pieces

    public const int TICK_MS = 250;
    const float TICK_S = 0.25f;
    const int MAXT = 2400;                // ten minutes of hide and hunt
    const int HDR = 10, HDR1 = 9;
    const int F_ABS = 1, F_DELTA = 2, F_YAW = 4, F_DEF = 8, F_EV1 = 16, F_EV2 = 32, F_CATCH = 64, F_END = 128;
    const int F_SWING = 256;              // (decoded only: a hunter's F_DEF in version 2)
    const float WEAPON_BIG = 1.5f;        // the weapon a bit larger than life, to read at the table's scale
    const float SWING_S = 0.6f;           // a replayed swing, in real seconds (as a burst: the same at any speed)
    const float Q = 0.02f;                // metres per step
    const int TAIL = 40;                  // ten seconds of trail
    const float UPPER_Y = 2.0f;           // (PHMiniMap's: above it a marker hides with the upper floors)

    // ---- synced: the table's shared playback
    [UdonSynced] public int viewRound = -1;   // the round being watched (open only while it is still the last round)
    [UdonSynced] public bool open, playing;
    [UdonSynced] public float atSec;          // the replay's time at atMs (server ms)
    [UdonSynced] public int atMs;
    [UdonSynced] public int speed = 1;        // 1, 2, 4
    [HideInInspector] public int buttonArg;   // PHButton: side * SIDE_ARG + the scrub bar piece pressed + 1
    public const int SIDE_ARG = 100;

    // ---- recording (this player only)
    byte[] buf;
    int bufLen, recRound = -1, recSlot = -1, recRole, firstTick, lastTick, count;
    int qx, qy, qz, qyaw, qdef;
    bool recEnded, published;
    int pendFlags, lastHealth, lastHits, lastCatches, lastWrong, recWeapon = -1;
    bool pendSwing;

    // ---- decoded tracks (slot * MAXT + k): places relative to the builder's origin
    Vector3[] tPos; float[] tYaw; int[] tDef, tFlag;
    int[] tFirst, tCount, tRole, tRound, tWeapon;
    string[] tWho;
    bool[] dirty;
    int originTick, endTick, huntTick;
    bool timeline;

    // ---- shown
    GameObject[] prop; int[] propDef;
    int[] lastK, weaponShown;
    float[] popAt, swingAt; Color[] colour;
    Vector3[] tailBuf;
    MaterialPropertyBlock block;
    bool shownOpen;
    int clockShown = -1;
    Color[] palette;

    void Start()
    {
        int n = tracks.Length;
        buf = new byte[HDR + MAXT * 10];
        tPos = new Vector3[n * MAXT]; tYaw = new float[n * MAXT]; tDef = new int[n * MAXT]; tFlag = new int[n * MAXT];
        tFirst = new int[n]; tCount = new int[n]; tRole = new int[n]; tRound = new int[n]; tWeapon = new int[n]; tWho = new string[n]; dirty = new bool[n];
        prop = new GameObject[n]; propDef = new int[n]; lastK = new int[n]; popAt = new float[n]; colour = new Color[n];
        weaponShown = new int[n]; swingAt = new float[n];
        for (int i = 0; i < n; i++) { tRound[i] = -1; propDef[i] = -2; lastK[i] = -1; popAt[i] = -9f; tWho[i] = ""; weaponShown[i] = -2; swingAt[i] = -9f; }
        tailBuf = new Vector3[TAIL];
        block = new MaterialPropertyBlock();
        palette = new Color[] {
            new Color(0.95f, 0.45f, 0.35f), new Color(0.35f, 0.75f, 0.95f), new Color(0.98f, 0.8f, 0.3f), new Color(0.55f, 0.85f, 0.45f),
            new Color(0.85f, 0.5f, 0.9f), new Color(1f, 0.62f, 0.2f), new Color(0.4f, 0.9f, 0.8f), new Color(0.95f, 0.55f, 0.7f) };
        if (game.text != null) game.text.Register(this);
        HideMarkers();
        Labels();
    }

    public void _OnTextLanguage() { Labels(); }

    void Update()
    {
        Record();
        // a track that arrived: decoded one a frame
        for (int i = 0; i < dirty.Length; i++) if (dirty[i]) { dirty[i] = false; Decode(i); timeline = false; break; }
        if (!timeline) Timeline();
        bool avail = Available();
        for (int b = 0; b < bar.Length; b++) if (bar[b] != null && bar[b].activeSelf != avail) bar[b].SetActive(avail);
        bool on = avail && open && viewRound == game.round;
        if (on != shownOpen)
        {
            shownOpen = on;
            mini.SetReplay(on);
            for (int i = 0; i < playingOnly.Length; i++) if (playingOnly[i] != null) playingOnly[i].SetActive(on);
            if (!on) HideMarkers();
            for (int i = 0; i < lastK.Length; i++) lastK[i] = -1;
            Labels();
        }
        if (on) Play();
    }

    // ------------------------------------------------------------------ recording
    int Tick(int ms) { int t = ms / TICK_MS; if (ms < 0 && t * TICK_MS != ms) t--; return t; }

    void Record()
    {
        int phase = game.phase;
        if (phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT)
        {
            if (recRound != game.round)
            {
                recRound = game.round; published = false; recEnded = false; count = 0; recSlot = -1; pendFlags = 0; pendSwing = false; recWeapon = -1;
                int slot = game.SlotOf(Networking.LocalPlayer.playerId);
                int role = game.RoleOf(slot);
                if (slot < 0 || slot >= tracks.Length || role == PHGame.ROLE_NONE) return;
                recSlot = slot; recRole = role;
                lastHealth = 0; lastHits = 0; lastCatches = 0; lastWrong = 0;
            }
            if (recSlot < 0 || recEnded) return;
            // a hunter from the release (in the hide they wait off the map)
            if (recRole == PHGame.ROLE_HUNTER && phase != PHGame.PH_HUNT) return;
            PHSlot s = game.slots[recSlot];
            if (s == null || s.round != game.round) return;
            int tick = Tick(game.Now);
            if (count == 0) { firstTick = tick; lastTick = tick - 1; }
            for (int n = 0; n < 8 && lastTick < tick && count < MAXT && !recEnded; n++) { lastTick++; Sample(s); }
            return;
        }
        if (phase == PHGame.PH_END && recRound == game.round && !published)
        {
            published = true;
            if (recSlot < 0 || count == 0) return;
            buf[0] = 82; buf[1] = 2; buf[2] = (byte)recRole;
            Put32(3, firstTick);
            buf[7] = (byte)(count & 255); buf[8] = (byte)((count >> 8) & 255);
            buf[9] = (byte)Mathf.Clamp(recWeapon + 1, 0, 255);
            byte[] data = new byte[bufLen];
            System.Array.Copy(buf, data, bufLen);
            tracks[recSlot].Publish(recRound, data, Networking.LocalPlayer.displayName);
        }
    }

    // a hider's taunt (PHSlot.PlayTaunt, on the hider's own client)
    public void Taunted() { if (recRole == PHGame.ROLE_HIDER) pendFlags |= F_EV1; }

    // a hunter's swing (PHWeapons: a desktop slash, a real VR swing; PHLocal.MeleeStrike: anything struck), on their own
    // client. (Underscore: not a network event.)
    public void _Swung() { if (recRole == PHGame.ROLE_HUNTER && recSlot >= 0 && !recEnded && game.phase == PHGame.PH_HUNT) pendSwing = true; }

    // the melee model this hunter holds this round (as PHWeapons shows it)
    int WeaponOf(PHSlot s)
    {
        PHWeapons w = game.local != null ? game.local.weapons : null;
        if (w == null || w.meleeMesh == null) return -1;
        return s.meleeChoice >= 0 && s.meleeChoice < w.meleeMesh.Length ? s.meleeChoice : w.ModelNow();
    }

    void Sample(PHSlot s)
    {
        if (count == 0) bufLen = HDR;
        VRCPlayerApi me = Networking.LocalPlayer;
        int flags = pendFlags; pendFlags = 0;
        Vector3 at = game.MapLocalOf(recSlot, me.GetPosition());   // (map-local: with HIDE CAMERAS each team plays on its own copy)
        float yaw = me.GetRotation().eulerAngles.y;
        int def = -1;
        if (recRole == PHGame.ROLE_HIDER)
        {
            def = s.def;
            if (s.def >= 0 && s.hiding) { at = s.propPos; yaw = s.propYaw; }
            if (s.health < lastHealth) flags |= F_EV2;
            lastHealth = s.health;
            if (s.caught) flags |= F_END;
        }
        else
        {
            if (recWeapon < 0) recWeapon = WeaponOf(s);
            flags &= ~F_DEF;
            if (pendSwing) { flags |= F_DEF; pendSwing = false; }   // (a hunter's F_DEF: swung)
            if (s.wrongProps > lastWrong) flags |= F_EV1;
            if (s.hits > lastHits) flags |= F_EV2;
            if (s.catches > lastCatches) flags |= F_CATCH;
            lastWrong = s.wrongProps; lastHits = s.hits; lastCatches = s.catches;
            if (s.hunterOut) flags |= F_END;
        }
        Vector3 rel = at;   // (propPos is map-local already)
        int x = Mathf.Clamp(Mathf.RoundToInt(rel.x / Q), -32767, 32767);
        int y = Mathf.Clamp(Mathf.RoundToInt(rel.y / Q), -32767, 32767);
        int z = Mathf.Clamp(Mathf.RoundToInt(rel.z / Q), -32767, 32767);
        int dx = x - qx, dy = y - qy, dz = z - qz;
        int big = Mathf.Max(Mathf.Abs(dx), Mathf.Max(Mathf.Abs(dy), Mathf.Abs(dz)));
        if (count == 0 || big > 127) flags |= F_ABS;
        else if (big > 1) flags |= F_DELTA;   // (a step's jitter standing still is no move)
        int yq = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 360f * 256f) & 255;
        int dyaw = Mathf.Abs(yq - qyaw); if (dyaw > 128) dyaw = 256 - dyaw;
        if (count == 0 || dyaw > 2) flags |= F_YAW;
        if (recRole == PHGame.ROLE_HIDER && (count == 0 || def != qdef)) flags |= F_DEF;
        buf[bufLen++] = (byte)flags;
        if ((flags & F_ABS) != 0) { Put16(x); Put16(y); Put16(z); qx = x; qy = y; qz = z; }
        else if ((flags & F_DELTA) != 0) { Put8(dx); Put8(dy); Put8(dz); qx = x; qy = y; qz = z; }
        if ((flags & F_YAW) != 0) { buf[bufLen++] = (byte)yq; qyaw = yq; }
        if ((flags & F_DEF) != 0 && recRole == PHGame.ROLE_HIDER) { Put16(def + 1); qdef = def; }
        if ((flags & F_END) != 0) recEnded = true;
        count++;
    }

    void Put8(int v) { buf[bufLen++] = (byte)(v & 255); }
    void Put16(int v) { buf[bufLen++] = (byte)(v & 255); buf[bufLen++] = (byte)((v >> 8) & 255); }
    void Put32(int at, int v) { for (int b = 0; b < 4; b++) buf[at + b] = (byte)((v >> (8 * b)) & 255); }

    // ------------------------------------------------------------------ decoding
    public void TrackArrived(int i) { if (i >= 0 && i < dirty.Length) dirty[i] = true; }

    void Decode(int i)
    {
        PHReplayTrack tr = tracks[i];
        byte[] d = tr.data;
        tRound[i] = -1; tCount[i] = 0;
        if (d == null || d.Length < HDR1 || d[0] != 82 || (d[1] != 1 && d[1] != 2)) return;
        int ver = (int)d[1], hdr = ver >= 2 ? HDR : HDR1;
        if (d.Length < hdr) return;
        tRole[i] = (int)d[2];
        tWeapon[i] = ver >= 2 ? (int)d[9] - 1 : -1;
        bool swings = ver >= 2 && tRole[i] == PHGame.ROLE_HUNTER;
        tFirst[i] = (int)d[3] | ((int)d[4] << 8) | ((int)d[5] << 16) | ((int)d[6] << 24);
        int n = Mathf.Min(MAXT, (int)d[7] | ((int)d[8] << 8));
        int p = hdr, x = 0, y = 0, z = 0, yq = 0, def = -1, b = i * MAXT, k = 0;
        for (; k < n && p < d.Length; k++)
        {
            int f = (int)d[p++];
            if ((f & F_ABS) != 0) { if (p + 6 > d.Length) break; x = S16(d, p); y = S16(d, p + 2); z = S16(d, p + 4); p += 6; }
            else if ((f & F_DELTA) != 0) { if (p + 3 > d.Length) break; x += S8(d[p]); y += S8(d[p + 1]); z += S8(d[p + 2]); p += 3; }
            if ((f & F_YAW) != 0) { if (p + 1 > d.Length) break; yq = (int)d[p++]; }
            if ((f & F_DEF) != 0 && swings) f = (f & ~F_DEF) | F_SWING;
            else if ((f & F_DEF) != 0) { if (p + 2 > d.Length) break; def = ((int)d[p] | ((int)d[p + 1] << 8)) - 1; p += 2; }
            tPos[b + k] = new Vector3(x * Q, y * Q, z * Q);
            tYaw[b + k] = yq * (360f / 256f);
            tDef[b + k] = def;
            tFlag[b + k] = f;
        }
        tCount[i] = k;
        tRound[i] = tr.round;
        tWho[i] = tr.who;
        lastK[i] = -1;
        weaponShown[i] = -2;
    }

    int S16(byte[] d, int p) { int v = (int)d[p] | ((int)d[p + 1] << 8); return v > 32767 ? v - 65536 : v; }
    int S8(byte v) { int x = (int)v; return x > 127 ? x - 256 : x; }

    // the replay's span: from the first track's first sample to the last one's last
    void Timeline()
    {
        timeline = true;
        bool any = false;
        int hider = 0;
        for (int i = 0; i < tracks.Length; i++)
        {
            if (!Has(i)) continue;
            if (!any || tFirst[i] < originTick) originTick = tFirst[i];
            if (!any || tFirst[i] + tCount[i] > endTick) endTick = tFirst[i] + tCount[i];
            any = true;
            // the colours as the diorama gives them: the hunters red, the hiders the palette in slot order
            if (tRole[i] == PHGame.ROLE_HUNTER) colour[i] = new Color(0.92f, 0.2f, 0.18f);
            else colour[i] = palette[hider++ % palette.Length];
        }
        if (!any) { originTick = 0; endTick = 0; }
        huntTick = Tick(game.huntStartMs);
        if (huntMark.Length > 0)
        {
            float span = Mathf.Max(1, endTick - originTick);
            float u = Mathf.Clamp01((huntTick - originTick) / span);
            for (int b = 0; b < huntMark.Length; b++) if (huntMark[b] != null) huntMark[b].localPosition = new Vector3((u - 0.5f) * barWidth, 0f, 0f);
        }
    }

    bool Has(int i) { return tRound[i] == game.round && tCount[i] > 0; }

    float Duration() { return Mathf.Max(0f, (endTick - originTick) * TICK_S); }

    // the replay can be watched while the diorama shows the round's map and some track of that round has come
    bool Available()
    {
        if (mini == null || !mini.content.activeSelf) return false;
        if (game.phase != PHGame.PH_END && game.phase != PHGame.PH_LOBBY) return false;
        for (int i = 0; i < tracks.Length; i++) if (Has(i)) return true;
        return false;
    }

    // ------------------------------------------------------------------ playback
    float Pos()
    {
        float t = atSec;
        if (playing) t += (game.Now - atMs) * 0.001f * speed;
        return Mathf.Clamp(t, 0f, Duration());
    }

    void Play()
    {
        float t = Pos();
        float tickF = originTick + t / TICK_S;
        Vector3 off = -mini.Origin();   // (the tracks are map-local, as the diorama's model)
        bool upper = mini.UpperOn();
        Vector3 eye = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        for (int i = 0; i < tracks.Length; i++)
        {
            if (!Has(i)) { Hide(i); continue; }
            int b = i * MAXT, n = tCount[i];
            float u = tickF - tFirst[i];
            int k = Mathf.FloorToInt(u);
            bool gone = k >= n - 1 && (tFlag[b + n - 1] & F_END) != 0 && u > n - 1 + 2f;   // (a caught hider: gone a moment after)
            if (k < 0 || gone) { Hide(i); Pops(i, Mathf.Min(k, n - 1)); continue; }
            if (k > n - 1) k = n - 1;
            float frac = k < n - 1 ? Mathf.Clamp01(u - k) : 0f;
            Vector3 rel = k < n - 1 ? Vector3.Lerp(tPos[b + k], tPos[b + k + 1], frac) : tPos[b + k];
            Vector3 at = rel + off;
            bool shown = upper || rel.y <= UPPER_Y;
            int def = tDef[b + k];
            bool asProp = tRole[i] == PHGame.ROLE_HIDER && def >= 0 && def < builder.propPrefabs.Length && builder.propPrefabs[def] != null;
            // the hider as their prop of the moment (a fresh copy when it changes)
            if (asProp && propDef[i] != def)
            {
                if (prop[i] != null) Destroy(prop[i]);
                prop[i] = Copy(builder.propPrefabs[def]);
                propDef[i] = def;
            }
            if (prop[i] != null)
            {
                bool on = asProp && shown;
                if (prop[i].activeSelf != on) prop[i].SetActive(on);
                if (on) { prop[i].transform.localPosition = at; prop[i].transform.localRotation = Quaternion.Euler(0f, tYaw[b + k], 0f); }
            }
            // a hunter: the red meeple with their weapon (a capsule only if the scene has no meeple)
            bool meeple = tRole[i] == PHGame.ROLE_HUNTER && meeples != null && i < meeples.Length && meeples[i] != null;
            if (meeple) Meeple(i, at, tYaw[b + k], shown);
            Transform pw = pawns[i];
            bool pawnOn = !asProp && !meeple && shown;
            if (pw.gameObject.activeSelf != pawnOn)
            {
                pw.gameObject.SetActive(pawnOn);
                block.SetColor("_Color", colour[i]); pawnRenderer[i].SetPropertyBlock(block);
            }
            if (pawnOn) { pw.localPosition = at; pw.localRotation = Quaternion.Euler(0f, tYaw[b + k], 0f); }
            // the name over them, turned to the viewer
            Transform tg = tags[i];
            if (tg.gameObject.activeSelf != shown) tg.gameObject.SetActive(shown);
            if (shown)
            {
                tg.localPosition = at + new Vector3(0f, 3.2f, 0f);
                Vector3 dd = tg.position - eye; dd.y = 0f;
                if (dd.sqrMagnitude > 1e-4f) tg.rotation = Quaternion.LookRotation(dd, Vector3.up);
                if (tagLabel[i].text == "") { tagLabel[i].text = Clean(tWho[i]); tagLabel[i].color = colour[i]; }
            }
            // the tail: the last few seconds, when the sample changes
            LineRenderer lr = tails[i];
            if (k != lastK[i])
            {
                int from = Mathf.Max(0, k - TAIL + 1), m = k - from + 1;
                if (m >= 2)
                {
                    for (int j = 0; j < TAIL; j++) tailBuf[j] = tPos[b + Mathf.Min(from + j, k)] + off + new Vector3(0f, 0.15f, 0f);
                    lr.positionCount = TAIL;
                    lr.SetPositions(tailBuf);
                    lr.widthMultiplier = 0.00045f / Mathf.Max(0.001f, mini.model.localScale.x);   // (Cor: a quarter of the first width)
                    Color c0 = colour[i]; c0.a = 0f;
                    lr.startColor = c0; lr.endColor = colour[i];
                }
                if (lr.gameObject.activeSelf != (m >= 2 && shown)) lr.gameObject.SetActive(m >= 2 && shown);
                Pops(i, k);
            }
            // a burst grows and goes in half a second
            Transform pp = pops[i];
            float age = Time.time - popAt[i];
            bool popOn = age < 0.5f && shown;
            if (pp.gameObject.activeSelf != popOn) pp.gameObject.SetActive(popOn);
            if (popOn) { pp.localPosition = at + new Vector3(0f, 1f, 0f); pp.localScale = Vector3.one * (1f + age * 6f); }
        }
        // the controls
        float dur = Duration();
        Vector3 hp = new Vector3((dur > 0f ? t / dur - 0.5f : -0.5f) * barWidth, 0f, 0f);
        for (int b = 0; b < playhead.Length; b++) if (playhead[b] != null) playhead[b].localPosition = hp;
        int sec = Mathf.FloorToInt(t) * 10000 + Mathf.FloorToInt(dur);
        if (sec != clockShown)   // (a text rebuild only when it changes)
        {
            clockShown = sec;
            string ct = Clock(t) + " / " + Clock(dur);
            for (int b = 0; b < clock.Length; b++) if (clock[b] != null) clock[b].text = ct;
        }
        bool atEnd = t >= dur - 0.01f;
        string pl = game.text != null ? game.text.Get(playing && !atEnd ? "replay.pause" : "replay.play") : "";
        for (int b = 0; b < playLabel.Length; b++) if (playLabel[b] != null && playLabel[b].text != pl) playLabel[b].text = pl;
    }

    // the events passed since the last sample shown (forward, a short way: a jump of the scrub bar shows none)
    void Pops(int i, int k)
    {
        int was = lastK[i];
        lastK[i] = k;
        if (was < 0 || k <= was || k - was > 8) return;
        int b = i * MAXT;
        for (int j = was + 1; j <= k; j++)
        {
            int f = tFlag[b + j];
            // a hunter swings hard when they swung, and when they struck a prop, a hider or a catch (an older track
            // has only the strikes)
            if (tRole[i] == PHGame.ROLE_HUNTER && (f & (F_SWING | F_EV1 | F_EV2 | F_CATCH)) != 0) swingAt[i] = Time.time;
            if ((f & (F_EV1 | F_EV2 | F_CATCH | F_END)) == 0) continue;
            Color c = colour[i];
            if ((f & (F_EV2 | F_END)) != 0 && tRole[i] == PHGame.ROLE_HIDER) c = new Color(0.92f, 0.2f, 0.18f);   // hit, caught: red
            else if ((f & (F_EV2 | F_CATCH)) != 0) c = Color.white;                                              // a hunter's hit
            else if (tRole[i] == PHGame.ROLE_HUNTER) c = new Color(0.5f, 0.5f, 0.5f);                            // a wrong prop, out
            popAt[i] = Time.time;
            block.SetColor("_Color", c); popRenderer[i].SetPropertyBlock(block);
        }
    }

    void Hide(int i)
    {
        if (pawns[i].gameObject.activeSelf) pawns[i].gameObject.SetActive(false);
        if (tags[i].gameObject.activeSelf) tags[i].gameObject.SetActive(false);
        if (tails[i].gameObject.activeSelf) tails[i].gameObject.SetActive(false);
        if (pops[i].gameObject.activeSelf && Time.time - popAt[i] >= 0.5f) pops[i].gameObject.SetActive(false);
        if (prop[i] != null && prop[i].activeSelf) prop[i].SetActive(false);
        if (meeples != null && i < meeples.Length && meeples[i] != null && meeples[i].gameObject.activeSelf) meeples[i].gameObject.SetActive(false);
    }

    // the hunter's meeple at 'at', turned to yaw: their weapon in its hand (and a set's other piece in the other), raised
    // high, and on a swing brought far back over the head, slammed down in front as the body lunges, and back up
    void Meeple(int i, Vector3 at, float yaw, bool shown)
    {
        Transform m = meeples[i];
        if (m.gameObject.activeSelf != shown) m.gameObject.SetActive(shown);
        if (!shown) return;
        m.localPosition = at; m.localRotation = Quaternion.Euler(0f, yaw, 0f);
        PHWeapons w = game.local != null ? game.local.weapons : null;
        int wm = tWeapon[i];
        if (w != null && w.meleeMesh != null && (wm < 0 || wm >= w.meleeMesh.Length)) wm = w.ModelNow();
        if (weaponShown[i] != wm)
        {
            weaponShown[i] = wm;
            bool has = w != null && w.meleeMesh != null && wm >= 0 && wm < w.meleeMesh.Length;
            Transform wt = meepleWeapon[i].transform;
            wt.gameObject.SetActive(has);
            if (has)
            {
                meepleWeapon[i].sharedMesh = w.meleeMesh[wm];
                float sz = w.MeleeSize(wm) * WEAPON_BIG;
                wt.localScale = Vector3.one * sz; wt.localPosition = -w.MeleeGrip(wm) * sz; wt.localRotation = Quaternion.identity;
            }
            bool off = has && w.offMesh != null && wm < w.offMesh.Length && w.offMesh[wm] != null;
            Transform ot = meepleOff[i].transform;
            ot.gameObject.SetActive(off);
            if (off)
            {
                meepleOff[i].sharedMesh = w.offMesh[wm];
                ot.localScale = Vector3.one * (w.MeleeSize(wm) * WEAPON_BIG); ot.localPosition = Vector3.zero; ot.localRotation = Quaternion.identity;
            }
        }
        // the hand's turn about its x (the weapon's head along +z): -70 held up and a little forward
        float t = Time.time - swingAt[i];
        float arm = -70f, lean = 0f, hop = 0f;
        if (t >= 0f && t < SWING_S)
        {
            float u = t / SWING_S;
            if (u < 0.35f) { float k = Smooth(u / 0.35f); arm = Mathf.Lerp(-70f, -170f, k); lean = Mathf.Lerp(0f, -12f, k); hop = Mathf.Lerp(0f, 0.25f, k); }
            else if (u < 0.5f) { float k = (u - 0.35f) / 0.15f; arm = Mathf.Lerp(-170f, 45f, k); lean = Mathf.Lerp(-12f, 28f, k); hop = Mathf.Lerp(0.25f, 0f, k); }
            else { float k = Smooth((u - 0.5f) / 0.5f); arm = Mathf.Lerp(45f, -70f, k); lean = Mathf.Lerp(28f, 0f, k); }
        }
        meepleBody[i].localRotation = Quaternion.Euler(lean, 0f, 0f);
        meepleBody[i].localPosition = new Vector3(0f, hop, 0f);
        Quaternion hr = Quaternion.Euler(arm, 0f, 0f);
        meepleHand[i].localRotation = hr;
        meepleOffHand[i].localRotation = hr;
    }

    float Smooth(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

    void HideMarkers()
    {
        for (int i = 0; i < tracks.Length; i++)
        {
            pawns[i].gameObject.SetActive(false); tags[i].gameObject.SetActive(false); tails[i].gameObject.SetActive(false); pops[i].gameObject.SetActive(false);
            if (meeples != null && i < meeples.Length && meeples[i] != null) meeples[i].gameObject.SetActive(false);
            swingAt[i] = -9f;
            if (prop[i] != null) { Destroy(prop[i]); prop[i] = null; }
            propDef[i] = -2; tagLabel[i].text = "";
        }
    }

    // a prop's copy on the table: no colliders, on the default layer (as PHMiniMap's)
    GameObject Copy(GameObject prefab)
    {
        GameObject go = Instantiate(prefab);
        Transform t = go.transform;
        Vector3 sc = t.localScale;
        t.SetParent(mini.model, false);
        t.localScale = sc;
        Collider[] cols = go.GetComponentsInChildren<Collider>(true);
        for (int q = 0; q < cols.Length; q++) cols[q].enabled = false;
        Transform[] parts = go.GetComponentsInChildren<Transform>(true);
        for (int q = 0; q < parts.Length; q++) parts[q].gameObject.layer = 0;
        if (t.childCount > 0 && t.GetChild(t.childCount - 1).name == "Shade") t.GetChild(t.childCount - 1).gameObject.SetActive(false);
        return go;
    }

    // ------------------------------------------------------------------ the buttons (anyone at the table; shared)
    void Take()
    {
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
    }

    void Send() { viewRound = game.round; RequestSerialization(); Labels(); }

    // the first button: watch the replay; then pause and play (from the start again at the end)
    public void PlayPause()
    {
        if (!Available()) return;
        Take();
        if (!open || viewRound != game.round) { open = true; playing = true; atSec = 0f; atMs = game.Now; speed = 1; for (int i = 0; i < lastK.Length; i++) lastK[i] = -1; }
        else if (playing && Pos() < Duration() - 0.01f) { atSec = Pos(); playing = false; }
        else { atSec = Pos() >= Duration() - 0.01f ? 0f : Pos(); atMs = game.Now; playing = true; }
        Send();
    }

    public void Speed()
    {
        if (!open) return;
        Take();
        atSec = Pos(); atMs = game.Now;
        speed = speed >= 4 ? 1 : speed * 2;
        Send();
    }

    // a piece of a scrub bar (buttonArg = side * SIDE_ARG + the piece + 1): the replay from there
    public void Seek()
    {
        if (!open) return;
        Take();
        atSec = Duration() * Mathf.Clamp01(((buttonArg - 1) % SIDE_ARG) / (float)Mathf.Max(1, segments - 1)); atMs = game.Now;
        Send();
    }

    // back to the diorama's summary
    public void Close()
    {
        Take();
        open = false; playing = false;
        Send();
    }

    public override void OnDeserialization() { Labels(); }

    void Labels()
    {
        if (game.text == null) return;
        string pl = game.text.Get(open && viewRound == game.round ? (playing ? "replay.pause" : "replay.play") : "replay.watch");
        string sp = game.text.F1("replay.speed", speed.ToString());   // (a string row, so the text bake gives the font its ×)
        string su = game.text.Get("replay.summary");
        for (int b = 0; b < playLabel.Length; b++) if (playLabel[b] != null) playLabel[b].text = pl;
        for (int b = 0; b < speedLabel.Length; b++) if (speedLabel[b] != null) speedLabel[b].text = sp;
        for (int b = 0; b < summaryLabel.Length; b++) if (summaryLabel[b] != null) summaryLabel[b].text = su;
    }

    string Clock(float t)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(t));
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    string Clean(string n)
    {
        if (n == null) return "";
        n = n.Replace("<", "‹").Replace(">", "›");
        return n.Length > 18 ? n.Substring(0, 17) + "…" : n;
    }
}
