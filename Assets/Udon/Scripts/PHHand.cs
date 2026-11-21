using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;
using TMPro;
using VRC.SDK3.Persistence;

// How each player holds their weapons (Cor): one setting for the hunter's melee weapon and one for the hider's gun,
// not one per weapon, and a left-handed mode. Each player's setting is in their PlayerData ("ph.hand"), so every
// client reads it and draws that player's weapon the same way.
//   the setting: a move (metres at avatar size 1, in the hand's own space), a turn (degrees) and a size (1 = as
//   the world holds it), on top of the weapon's own grip (Tools/weapon_grips.tsv); and which hand.
//   the store (Cor): a player standing in front of the weapon wall's hunter half holds their chosen melee weapon,
//   in front of the hider half their chosen gun, seen by everyone. A board at each half sets that kind's grip:
//   buttons (desktop too) at a fine, medium or coarse step, and in VR the other hand's grip takes the weapon, moves
//   and turns it, and lets go.
// Kept as "1|L|mx|my|mz|tx|ty|tz|size" for the hunter then the same for the hider ("L": 1 left-handed), moves in
// 1/10 mm, turns in 1/10 degree, sizes in 1/1000.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHHand : UdonSharpBehaviour
{
    public PHGame game;
    public PHWeapons weapons;
    public Transform zone;              // the store's root; the wall's halves in its space
    public float wallX = 12f;           // the wall's opening (x, zone space)
    public float zoneDepth = 4f;        // how far out from it a player counts as in front of it
    public float zoneHalf = 10.6f;      // each half runs from z 0 to +-zoneHalf (melee south, guns north)
    public Transform[] showMelee;       // the shown weapons, a small pool (WeaponBuilder's materials)
    public MeshFilter[] showMeleeFilter;
    public Transform[] showGun;
    public MeshFilter[] showGunFilter;
    public TextMeshProUGUI[] values;    // per board (0 hunter, 1 hider): the setting and the hand's diagnosis
    public TextMeshProUGUI[] stepText;
    public TextMeshProUGUI[] handText;
    public int buttonArg;               // set by PHButton

    public const string SAVE_KEY = "ph.hand";
    public const int SLOTS = 32;
    float[] STEP_MOVE = { 0.001f, 0.005f, 0.02f };   // fine, medium, coarse
    float[] STEP_TURN = { 1f, 5f, 15f };
    float[] STEP_SIZE = { 0.01f, 0.05f, 0.1f };

    // ---- this player's own setting
    [System.NonSerialized] public bool myLeft;
    Vector3[] myMove = new Vector3[2], myTurn = new Vector3[2];
    Quaternion[] myTurnQ = { Quaternion.identity, Quaternion.identity };
    float[] mySize = { 1f, 1f };
    int step = 1;
    bool dirty, restored;
    float saveAt;

    // ---- everyone else's, by slot (read from their PlayerData, a slot every frame)
    int[] cPid = new int[SLOTS];
    string[] cRaw = new string[SLOTS];
    bool[] cLeft = new bool[SLOTS];
    Vector3[] cMove = new Vector3[SLOTS * 2];
    Quaternion[] cTurn = new Quaternion[SLOTS * 2];
    float[] cSize = new float[SLOTS * 2];
    int nextRead;

    // ---- the hold, as Apply worked it out
    [System.NonSerialized] public Vector3 holdPos;
    [System.NonSerialized] public Quaternion holdRot = Quaternion.identity;
    [System.NonSerialized] public float holdSize = 1f;
    [System.NonSerialized] public bool holdBones;
    int applyFrame = -1, applyPid = -1;
    bool applyGun;

    // ---- the store's show
    int[] zoneOf = new int[SLOTS];     // per slot: -1 none, 0 melee half, 1 gun half (looked at every few frames)
    int myZone = -1;
    bool showing;
    bool dragging;
    int dragKind;
    Vector3 dragPos;
    Quaternion dragRot = Quaternion.identity;
    float nextShow;

    void Start()
    {
        for (int i = 0; i < SLOTS; i++) { cPid[i] = -1; zoneOf[i] = -1; }
        for (int i = 0; i < SLOTS * 2; i++) { cTurn[i] = Quaternion.identity; cSize[i] = 1f; }
        HideAll();
        if (game != null && game.text != null) game.text.Register(this);
        Show();
    }

    public void _OnTextLanguage() { Show(); }

    // ------------------------------------------------------------------ the setting, for the weapons
    // where player p (in slot) holds a melee weapon (gun false) or a gun: holdPos (the grip's point), holdRot, holdSize
    // (times the weapon's own size and the avatar's), holdBones (from the avatar's hand bones, else the controller)
    public void Apply(VRCPlayerApi p, int slot, bool gun)
    {
        if (p == null || !Utilities.IsValid(p)) return;
        if (applyFrame == Time.frameCount && applyPid == p.playerId && applyGun == gun) return;
        applyFrame = Time.frameCount; applyPid = p.playerId; applyGun = gun;
        bool me = p.isLocal;
        bool ok = me || (slot >= 0 && slot < SLOTS && cPid[slot] == p.playerId);
        bool left = me ? myLeft : ok && cLeft[slot];
        holdBones = weapons.BasePose(p, left, gun);
        holdPos = weapons.basePos; holdRot = weapons.baseRot; holdSize = 1f;
        if (!ok) return;
        int k = gun ? 1 : 0;
        Vector3 mv = me ? myMove[k] : cMove[slot * 2 + k];
        Quaternion tq = me ? myTurnQ[k] : cTurn[slot * 2 + k];
        holdPos += holdRot * (mv * weapons.AvatarScale(p));
        holdRot = holdRot * tq;
        holdSize = me ? mySize[k] : cSize[slot * 2 + k];
    }

    // ------------------------------------------------------------------ kept (PlayerData)
    void Read(int slot)
    {
        int pid = game.slotPlayer[slot];
        VRCPlayerApi p = pid > 0 ? VRCPlayerApi.GetPlayerById(pid) : null;
        if (p == null || !Utilities.IsValid(p) || p.isLocal) { cPid[slot] = -1; cRaw[slot] = null; return; }
        string raw;
        if (!PlayerData.TryGetString(p, SAVE_KEY, out raw)) raw = "";
        if (cPid[slot] == pid && raw == cRaw[slot]) return;
        cPid[slot] = pid; cRaw[slot] = raw;
        cLeft[slot] = false;
        for (int k = 0; k < 2; k++) { cMove[slot * 2 + k] = Vector3.zero; cTurn[slot * 2 + k] = Quaternion.identity; cSize[slot * 2 + k] = 1f; }
        int[] v = Parse(raw);
        if (v == null) return;
        cLeft[slot] = v[1] == 1;
        for (int k = 0; k < 2; k++)
        {
            int o = 2 + k * 7;
            cMove[slot * 2 + k] = new Vector3(v[o], v[o + 1], v[o + 2]) / 10000f;
            cTurn[slot * 2 + k] = Quaternion.Euler(new Vector3(v[o + 3], v[o + 4], v[o + 5]) / 10f);
            cSize[slot * 2 + k] = Mathf.Clamp(v[o + 6] / 1000f, 0.2f, 5f);
        }
    }

    // "1|L|7 numbers|7 numbers" into 16 ints, or null
    int[] Parse(string raw)
    {
        if (raw == null || raw.Length == 0) return null;
        string[] f = raw.Split('|');
        if (f.Length != 16 || f[0] != "1") return null;
        int[] v = new int[16];
        for (int i = 0; i < 16; i++)
        {
            int x;
            if (!int.TryParse(f[i], out x)) return null;
            v[i] = x;
        }
        return v;
    }

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;
        restored = true;
        string raw;
        if (!PlayerData.TryGetString(player, SAVE_KEY, out raw)) { Show(); return; }
        int[] v = Parse(raw);
        if (v == null) { Show(); return; }
        myLeft = v[1] == 1;
        for (int k = 0; k < 2; k++)
        {
            int o = 2 + k * 7;
            myMove[k] = new Vector3(v[o], v[o + 1], v[o + 2]) / 10000f;
            myTurn[k] = new Vector3(v[o + 3], v[o + 4], v[o + 5]) / 10f;
            myTurnQ[k] = Quaternion.Euler(myTurn[k]);
            mySize[k] = Mathf.Clamp(v[o + 6] / 1000f, 0.2f, 5f);
        }
        Show();
    }

    void Store()
    {
        if (!restored) return;   // (never over what the account holds before it has been read)
        string o = "1|" + (myLeft ? "1" : "0");
        for (int k = 0; k < 2; k++)
        {
            Vector3 t = myTurn[k];
            o += "|" + Mathf.RoundToInt(myMove[k].x * 10000f) + "|" + Mathf.RoundToInt(myMove[k].y * 10000f) + "|" + Mathf.RoundToInt(myMove[k].z * 10000f)
                + "|" + Mathf.RoundToInt(Ang(t.x) * 10f) + "|" + Mathf.RoundToInt(Ang(t.y) * 10f) + "|" + Mathf.RoundToInt(Ang(t.z) * 10f)
                + "|" + Mathf.RoundToInt(mySize[k] * 1000f);
        }
        PlayerData.SetString(SAVE_KEY, o);
    }

    float Ang(float a) { return Mathf.Repeat(a + 180f, 360f) - 180f; }

    void Changed() { dirty = true; Show(); }

    // ------------------------------------------------------------------ the boards' buttons
    // arg: kind * 100 + 1..6 move along x+, x-, y+, y-, z+, z-; 7..12 turn about x+, x-, y+, y-, z+, z-
    public void Nudge()
    {
        int k = buttonArg / 100, a = buttonArg % 100;
        if (k < 0 || k > 1 || a < 1 || a > 12) return;
        int axis = ((a - 1) % 6) / 2;
        float sign = ((a - 1) % 2) == 0 ? 1f : -1f;
        Vector3 d = axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
        if (a <= 6) myMove[k] = myMove[k] + d * (STEP_MOVE[step] * sign);
        else
        {
            myTurnQ[k] = myTurnQ[k] * Quaternion.AngleAxis(STEP_TURN[step] * sign, d);
            myTurn[k] = myTurnQ[k].eulerAngles;
        }
        Changed();
    }

    // arg: kind * 100 + 1 bigger, 2 smaller
    public void Grow()
    {
        int k = buttonArg / 100, a = buttonArg % 100;
        if (k < 0 || k > 1) return;
        float s = mySize[k] + (a == 1 ? STEP_SIZE[step] : -STEP_SIZE[step]);
        mySize[k] = Mathf.Clamp(Mathf.Round(s * 100f) / 100f, 0.2f, 5f);
        Changed();
    }

    public void StepSize() { step = (step + 1) % 3; Show(); }

    public void ToggleHand() { myLeft = !myLeft; dragging = false; Changed(); }

    // arg: kind + 1
    public void ResetKind()
    {
        int k = buttonArg - 1;
        if (k < 0 || k > 1) return;
        myMove[k] = Vector3.zero; myTurn[k] = Vector3.zero; myTurnQ[k] = Quaternion.identity; mySize[k] = 1f;
        dragging = false;
        Changed();
    }

    // ------------------------------------------------------------------ the store's show
    bool InRound()
    {
        int ph = game.phase;
        return ph == PHGame.PH_BRIEF || ph == PHGame.PH_HIDE || ph == PHGame.PH_HUNT || ph == PHGame.PH_END;
    }

    int ZoneAt(Vector3 world)
    {
        if (zone == null) return -1;
        Vector3 l = zone.InverseTransformPoint(world);
        if (l.x < wallX - zoneDepth || l.x > wallX + 1.5f || l.z < -zoneHalf || l.z > zoneHalf || l.y < -1f || l.y > 4f) return -1;
        return l.z < 0f ? 0 : 1;
    }

    void HideAll()
    {
        if (showMelee != null) for (int i = 0; i < showMelee.Length; i++) if (showMelee[i] != null) showMelee[i].gameObject.SetActive(false);
        if (showGun != null) for (int i = 0; i < showGun.Length; i++) if (showGun[i] != null) showGun[i].gameObject.SetActive(false);
        showing = false;
    }

    public override void PostLateUpdate()
    {
        if (game == null || weapons == null) return;
        Read(nextRead);
        nextRead = (nextRead + 1) % SLOTS;
        if (dirty && Time.time >= saveAt) { dirty = false; saveAt = Time.time + 1f; Store(); }
        if (InRound()) { if (showing) HideAll(); dragging = false; myZone = -1; return; }
        VRCPlayerApi me = Networking.LocalPlayer;
        if (me == null) return;
        // who stands where: a few times a second
        if ((Time.frameCount % 8) == 0)
        {
            for (int i = 0; i < SLOTS; i++)
            {
                int pid = game.slotPlayer[i];
                VRCPlayerApi p = pid > 0 && pid != me.playerId ? VRCPlayerApi.GetPlayerById(pid) : null;
                zoneOf[i] = p != null && Utilities.IsValid(p) ? ZoneAt(p.GetPosition()) : -1;
            }
            int z = ZoneAt(me.GetPosition());
            if (z != myZone) { myZone = z; if (z < 0) dragging = false; Show(); }
        }
        if (Time.time >= nextShow) { nextShow = Time.time + 0.5f; if (myZone >= 0) Show(); }
        int nm = 0, ng = 0;
        PHSlot ms = game.LocalSlot();
        if (myZone == 0) PlaceShown(me, ms != null ? ms.index : -1, ms, false, nm++, true);
        else if (myZone == 1) PlaceShown(me, ms != null ? ms.index : -1, ms, true, ng++, true);
        for (int i = 0; i < SLOTS; i++)
        {
            int z = zoneOf[i];
            if (z < 0) continue;
            if (z == 0 && nm >= showMelee.Length) continue;
            if (z == 1 && ng >= showGun.Length) continue;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(game.slotPlayer[i]);
            if (p == null || !Utilities.IsValid(p)) continue;
            PHSlot s = i < game.slots.Length ? game.slots[i] : null;
            if (z == 0) PlaceShown(p, i, s, false, nm++, false);
            else PlaceShown(p, i, s, true, ng++, false);
        }
        for (int k = nm; k < showMelee.Length; k++) if (showMelee[k].gameObject.activeSelf) showMelee[k].gameObject.SetActive(false);
        for (int k = ng; k < showGun.Length; k++) if (showGun[k].gameObject.activeSelf) showGun[k].gameObject.SetActive(false);
        showing = nm + ng > 0;
    }

    // one shown weapon: pool entry n of its kind, in player p's hand (the local desktop player: in the view's corner)
    void PlaceShown(VRCPlayerApi p, int slot, PHSlot s, bool gun, int n, bool mine)
    {
        Transform t = gun ? showGun[n] : showMelee[n];
        MeshFilter f = gun ? showGunFilter[n] : showMeleeFilter[n];
        int m = gun ? weapons.GunIndex(s) : weapons.MeleeIndex(s);
        Mesh[] meshes = gun ? weapons.gunMesh : weapons.meleeMesh;
        if (meshes == null || m < 0 || m >= meshes.Length) return;
        if (f.sharedMesh != meshes[m]) f.sharedMesh = meshes[m];
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        float sc = weapons.AvatarScale(p);
        if (mine && !p.IsUserInVR())
        {
            VRCPlayerApi.TrackingData head = p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            if (gun) weapons.PutGun(t, m, head.position + head.rotation * (weapons.deskSelectorAt * sc), head.rotation * Quaternion.Euler(weapons.deskSelectorEuler), sc * mySize[1]);
            else weapons.PutMelee(t, m, head.position + head.rotation * (weapons.deskAt * sc), head.rotation * Quaternion.Euler(weapons.deskEuler), sc * weapons.deskScale * mySize[0]);
            return;
        }
        Vector3 at; Quaternion r;
        if (mine && dragging && dragKind == (gun ? 1 : 0))
        {
            VRCPlayerApi.TrackingData oh = p.GetTrackingData(myLeft ? VRCPlayerApi.TrackingDataType.RightHand : VRCPlayerApi.TrackingDataType.LeftHand);
            at = oh.position + oh.rotation * dragPos; r = oh.rotation * dragRot;
        }
        else { Apply(p, slot, gun); at = holdPos; r = holdRot; }
        float size = mine ? mySize[gun ? 1 : 0] : holdSize;
        if (gun) weapons.PutGun(t, m, at, r, sc * size);
        else weapons.PutMelee(t, m, at, r, sc * size);
    }

    // VR: the other hand's grip takes the shown weapon (near it or the weapon hand), and lets it go where it is
    public override void InputGrab(bool value, UdonInputEventArgs args)
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (me == null || !me.IsUserInVR() || myZone < 0 || InRound()) return;
        bool other = myLeft ? args.handType == HandType.RIGHT : args.handType == HandType.LEFT;
        if (!other) return;
        VRCPlayerApi.TrackingData oh = me.GetTrackingData(myLeft ? VRCPlayerApi.TrackingDataType.RightHand : VRCPlayerApi.TrackingDataType.LeftHand);
        PHSlot ms = game.LocalSlot();
        int slot = ms != null ? ms.index : -1;
        bool gun = myZone == 1;
        float sc = weapons.AvatarScale(me);
        if (value && !dragging)
        {
            Apply(me, slot, gun);
            if ((oh.position - holdPos).magnitude > 0.5f * sc) return;
            Quaternion inv = Quaternion.Inverse(oh.rotation);
            dragPos = inv * (holdPos - oh.position);
            dragRot = inv * holdRot;
            dragKind = myZone;
            dragging = true;
        }
        else if (!value && dragging)
        {
            dragging = false;
            int k = dragKind;
            Vector3 at = oh.position + oh.rotation * dragPos;
            Quaternion r = oh.rotation * dragRot;
            weapons.BasePose(me, myLeft, k == 1);
            Quaternion bInv = Quaternion.Inverse(weapons.baseRot);
            myMove[k] = bInv * (at - weapons.basePos) / sc;
            myTurnQ[k] = bInv * r;
            myTurn[k] = myTurnQ[k].eulerAngles;
            applyFrame = -1;
            Changed();
        }
    }

    // ------------------------------------------------------------------ the boards' words
    void Show()
    {
        if (game == null || game.text == null) return;
        PHText tx = game.text;
        string st = tx.F1("hand.step", tx.Get(step == 0 ? "hand.fine" : step == 1 ? "hand.medium" : "hand.coarse"));
        string hd = tx.Get(myLeft ? "hand.left" : "hand.right");
        for (int k = 0; k < 2; k++)
        {
            if (stepText != null && k < stepText.Length && stepText[k] != null) stepText[k].text = st;
            if (handText != null && k < handText.Length && handText[k] != null) handText[k].text = hd;
            if (values == null || k >= values.Length || values[k] == null) continue;
            Vector3 mv = myMove[k] * 1000f;
            values[k].text = tx.Get("hand.move") + "   " + Num(mv.x) + "   " + Num(mv.y) + "   " + Num(mv.z) + "\n"
                + tx.Get("hand.turn") + "   " + Num(Ang(myTurn[k].x)) + "   " + Num(Ang(myTurn[k].y)) + "   " + Num(Ang(myTurn[k].z)) + "\n"
                + tx.Get("hand.size") + "   " + Mathf.RoundToInt(mySize[k] * 100f) + "%" + (myZone == k ? "\n" + HandLine(k == 1) : "");
        }
    }

    string Num(float v) { return v.ToString("0"); }

    // which way the hand is found (Cor, VR: the weapon sat in the wrist): the avatar's bones, or the controller's point
    // (and why), and how far the grip is from the hand bone (the wrist)
    string HandLine(bool gun)
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (me == null || !me.IsUserInVR()) return "";
        PHSlot ms = game.LocalSlot();
        applyFrame = -1;
        Apply(me, ms != null ? ms.index : -1, gun);
        Vector3 wrist = me.GetBonePosition(myLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        string line = holdBones ? game.text.Get("hand.bones") : game.text.F1("hand.controller", weapons.handWhy);
        if (wrist != Vector3.zero) line += "\n" + game.text.F1("hand.wrist", ((holdPos - wrist).magnitude * 100f).ToString("0.0"));
        return line;
    }
}
