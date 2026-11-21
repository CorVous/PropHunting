using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;
using TMPro;

// The grip station (Cor): a chair in the lobby, next to the debug sign, for lining up each weapon in the hand. Sitting
// in it shows the chosen melee weapon or gun in the right hand exactly as the game holds it (PHWeapons.HandPose, then
// the model's grip, turn and size from Tools/weapon_grips.tsv).
//   VR: grab the weapon with the LEFT hand's grip, move and turn it, let go: the right hand keeps it as it was left.
//   The board's buttons (desktop too): step through the weapons, switch melee / gun, nudge it (1 cm, 5 degrees),
//   resize it (5 %), put it back as built, and save.
// A change applies to this player's own game at once (until they rejoin). SAVE writes a log line per weapon changed
// here ("[PHGrip] " + its Tools/weapon_grips.tsv line); Tools/import_grips.py copies them from VRChat's output log (or
// the editor's log) into the file, and the next Build world scene bakes them in for everyone.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHGripStation : UdonSharpBehaviour
{
    public PHWeapons weapons;
    public VRC.SDK3.Components.VRCStation seat;
    public Transform preview;            // the weapon shown in the hand (a MeshFilter and the weapon material)
    public MeshFilter previewFilter;
    public TextMeshProUGUI title, values;
    public int buttonArg;                // set by PHButton before Nudge

    public const float STEP = 0.01f, TURN = 5f, GROW = 1.05f;

    bool seated, gun, dragging, ready;
    int index;
    Vector3 dragPos;
    Quaternion dragRot = Quaternion.identity;
    // as built (for RESET, and to know what changed)
    Vector3[] meleeGrip0, meleeTurn0, gunGrip0, gunTurn0;
    float[] meleeSize0, gunSize0;

    void Start() { Setup(); preview.gameObject.SetActive(false); Show(); }

    void Setup()
    {
        if (ready || weapons == null) return;
        ready = true;
        int nm = weapons.meleeMesh.Length, ng = weapons.gunMesh.Length;
        // (an older scene without grips: start everything at the origin, no turn, size 1)
        if (weapons.meleeGrip == null || weapons.meleeGrip.Length != nm) { weapons.meleeGrip = new Vector3[nm]; weapons.meleeTurn = new Vector3[nm]; weapons.meleeSize = Ones(nm); }
        if (weapons.gunGrip == null || weapons.gunGrip.Length != ng) { weapons.gunGrip = new Vector3[ng]; weapons.gunTurn = new Vector3[ng]; weapons.gunSize = Ones(ng); }
        meleeGrip0 = Copy3(weapons.meleeGrip); meleeTurn0 = Copy3(weapons.meleeTurn); meleeSize0 = Copy1(weapons.meleeSize);
        gunGrip0 = Copy3(weapons.gunGrip); gunTurn0 = Copy3(weapons.gunTurn); gunSize0 = Copy1(weapons.gunSize);
    }

    float[] Ones(int n) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = 1f; return a; }
    Vector3[] Copy3(Vector3[] a) { var b = new Vector3[a.Length]; for (int i = 0; i < a.Length; i++) b[i] = a[i]; return b; }
    float[] Copy1(float[] a) { var b = new float[a.Length]; for (int i = 0; i < a.Length; i++) b[i] = a[i]; return b; }

    // ---- sitting
    public override void Interact() { seat.UseStation(Networking.LocalPlayer); }

    public override void OnStationEntered(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        Setup();
        seated = true; dragging = false;
        preview.gameObject.SetActive(true);
        SetMesh(); Show();
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        seated = false; dragging = false;
        preview.gameObject.SetActive(false);
    }

    // ---- the weapon in the hand
    float AvatarScale(VRCPlayerApi p)
    {
        float eye = p.GetAvatarEyeHeightAsMeters();
        return Mathf.Clamp(eye > 0.01f ? eye / 1.6f : 1f, 0.2f, 5f);
    }

    // where the game holds it: the fist's middle and the hand's turn for this kind (as PHWeapons and PHLocal place them)
    void Hand(VRCPlayerApi me, out Vector3 at, out Quaternion r)
    {
        if (weapons.HandPose(me)) { at = weapons.handPos; r = gun ? weapons.handGun : weapons.handMelee; return; }
        VRCPlayerApi.TrackingData rh = me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
        at = rh.position; r = rh.rotation * Quaternion.Euler(weapons.handEuler);
    }

    float nextShow;

    public override void PostLateUpdate()
    {
        if (!seated) return;
        if (Time.time >= nextShow) { nextShow = Time.time + 0.5f; Show(); }
        VRCPlayerApi me = Networking.LocalPlayer;
        if (dragging)
        {
            VRCPlayerApi.TrackingData lh = me.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
            preview.SetPositionAndRotation(lh.position + lh.rotation * dragPos, lh.rotation * dragRot);
            return;
        }
        Vector3 at; Quaternion r;
        Hand(me, out at, out r);
        float sc = AvatarScale(me);
        if (!gun) { weapons.PutMelee(preview, index, at, r, sc); return; }
        float s = sc * weapons.GunSize(index);
        Quaternion q = r * weapons.GunTurn(index);
        preview.SetPositionAndRotation(at - q * (weapons.GunGrip(index) * s), q);
        preview.localScale = Vector3.one * s;
    }

    // VR: the left hand's grip takes the weapon (anywhere near the right hand) and lets it go
    public override void InputGrab(bool value, UdonInputEventArgs args)
    {
        if (!seated || args.handType != HandType.LEFT) return;
        VRCPlayerApi me = Networking.LocalPlayer;
        if (!me.IsUserInVR()) return;
        VRCPlayerApi.TrackingData lh = me.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
        if (value && !dragging)
        {
            Vector3 at; Quaternion r;
            Hand(me, out at, out r);
            if ((lh.position - at).magnitude > 0.6f * AvatarScale(me) && (lh.position - preview.position).magnitude > 0.6f * AvatarScale(me)) return;
            Quaternion inv = Quaternion.Inverse(lh.rotation);
            dragPos = inv * (preview.position - lh.position);
            dragRot = inv * preview.rotation;
            dragging = true;
        }
        else if (!value && dragging)
        {
            dragging = false;
            Keep(me);
        }
    }

    // the model as it is now, against the right hand: its new grip and turn (its size stays)
    void Keep(VRCPlayerApi me)
    {
        Vector3 at; Quaternion r;
        Hand(me, out at, out r);
        Quaternion q = preview.rotation;
        float s = preview.localScale.x;
        Vector3 turn = (Quaternion.Inverse(r) * q).eulerAngles;
        Vector3 grip = Quaternion.Inverse(q) * (at - preview.position) / s;
        Vector3[] g = gun ? weapons.gunGrip : weapons.meleeGrip;
        Vector3[] t = gun ? weapons.gunTurn : weapons.meleeTurn;
        g[index] = grip; t[index] = turn;
        Show();
    }

    // ---- the board's buttons
    public void Next() { Step(1); }
    public void Prev() { Step(-1); }
    public void Next10() { Step(10); }
    public void Prev10() { Step(-10); }

    void Step(int d)
    {
        Setup();
        int n = gun ? weapons.gunMesh.Length : weapons.meleeMesh.Length;
        index = ((index + d) % n + n) % n;
        dragging = false;
        SetMesh(); Show();
    }

    public void Kind()
    {
        Setup();
        gun = !gun; index = 0; dragging = false;
        SetMesh(); Show();
    }

    // 1..6: the grip along the model's x+, x-, y+, y-, z+, z- (STEP); 7..12: the turn about x+, x-, y+, y-, z+, z- (TURN)
    public void Nudge()
    {
        Setup();
        int a = buttonArg;
        if (a < 1 || a > 12) return;
        int axis = ((a - 1) % 6) / 2;
        float sign = ((a - 1) % 2) == 0 ? 1f : -1f;
        Vector3 d = axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
        if (a <= 6)
        {
            Vector3[] g = gun ? weapons.gunGrip : weapons.meleeGrip;
            g[index] = g[index] + d * (STEP * sign);
        }
        else
        {
            Vector3[] t = gun ? weapons.gunTurn : weapons.meleeTurn;
            t[index] = (Quaternion.Euler(t[index]) * Quaternion.AngleAxis(TURN * sign, d)).eulerAngles;
        }
        Show();
    }

    public void Bigger() { Grow(GROW); }
    public void Smaller() { Grow(1f / GROW); }

    void Grow(float k)
    {
        Setup();
        float[] s = gun ? weapons.gunSize : weapons.meleeSize;
        s[index] = Mathf.Round(s[index] * k * 100f) / 100f;
        Show();
    }

    public void ResetOne()
    {
        Setup();
        Vector3[] g = gun ? weapons.gunGrip : weapons.meleeGrip;
        Vector3[] t = gun ? weapons.gunTurn : weapons.meleeTurn;
        float[] s = gun ? weapons.gunSize : weapons.meleeSize;
        g[index] = gun ? gunGrip0[index] : meleeGrip0[index];
        t[index] = gun ? gunTurn0[index] : meleeTurn0[index];
        s[index] = gun ? gunSize0[index] : meleeSize0[index];
        Show();
    }

    // every weapon changed here, to the log
    public void Save()
    {
        Setup();
        int n = 0;
        for (int i = 0; i < weapons.meleeMesh.Length; i++)
            if (Changed(false, i)) { Debug.Log("[PHGrip] " + Line(false, i)); n++; }
        for (int i = 0; i < weapons.gunMesh.Length; i++)
            if (Changed(true, i)) { Debug.Log("[PHGrip] " + Line(true, i)); n++; }
        Debug.Log("[PHGrip] saved " + n);
        Show();
        if (values != null) values.text += "\nSaved " + n + " to the log (Tools/import_grips.py)";
    }

    bool Changed(bool g, int i)
    {
        if (g) return weapons.gunGrip[i] != gunGrip0[i] || weapons.gunTurn[i] != gunTurn0[i] || weapons.gunSize[i] != gunSize0[i];
        return weapons.meleeGrip[i] != meleeGrip0[i] || weapons.meleeTurn[i] != meleeTurn0[i] || weapons.meleeSize[i] != meleeSize0[i];
    }

    string Line(bool g, int i)
    {
        string id = g ? weapons.gunId[i] : weapons.meleeId[i];
        Vector3 p = g ? weapons.gunGrip[i] : weapons.meleeGrip[i];
        Vector3 t = g ? weapons.gunTurn[i] : weapons.meleeTurn[i];
        float s = g ? weapons.gunSize[i] : weapons.meleeSize[i];
        return id + "\t" + F(p.x) + " " + F(p.y) + " " + F(p.z) + "\t" + A(t.x) + " " + A(t.y) + " " + A(t.z) + "\t" + s.ToString("0.00");
    }

    string F(float v) { return v.ToString("0.0000"); }
    // an angle as -180..180, one decimal
    string A(float v) { v = Mathf.Repeat(v + 180f, 360f) - 180f; return v.ToString("0.0"); }

    void SetMesh()
    {
        if (previewFilter == null) return;
        previewFilter.sharedMesh = gun ? weapons.gunMesh[index] : weapons.meleeMesh[index];
    }

    void Show()
    {
        if (weapons == null) return;
        int n = gun ? weapons.gunMesh.Length : weapons.meleeMesh.Length;
        string id = gun ? weapons.gunId[index] : weapons.meleeId[index];
        if (title != null) title.text = (gun ? "GUN " : "MELEE ") + (index + 1) + " / " + n + "\n" + Short(id);
        if (values == null) return;
        int changed = 0;
        if (ready)
        {
            for (int i = 0; i < weapons.meleeMesh.Length; i++) if (Changed(false, i)) changed++;
            for (int i = 0; i < weapons.gunMesh.Length; i++) if (Changed(true, i)) changed++;
        }
        Vector3 p = gun ? weapons.GunGrip(index) : weapons.MeleeGrip(index);
        Vector3 t = (gun ? weapons.GunTurn(index) : weapons.MeleeTurn(index)).eulerAngles;
        float s = gun ? weapons.GunSize(index) : weapons.MeleeSize(index);
        values.text = "Grip " + F(p.x) + "  " + F(p.y) + "  " + F(p.z) + "\nTurn " + A(t.x) + "  " + A(t.y) + "  " + A(t.z)
            + "\nSize " + s.ToString("0.00") + (ready && Changed(gun, index) ? "  (changed)" : "") + "\nChanged here: " + changed
            + (seated ? "\n" + HandLine() : "\nSit in the chair to hold it");
    }

    // which way the hand is found (Cor, VR: the weapon sat in the wrist): the avatar's bones, or the controller's point,
    // and how far the grip is from the hand bone (the wrist) in cm
    string HandLine()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (me == null) return "";
        bool bones = weapons.HandPose(me);
        Vector3 wrist = me.GetBonePosition(HumanBodyBones.RightHand);
        Vector3 at = bones ? weapons.handPos : me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
        string d = wrist != Vector3.zero ? "  grip " + ((at - wrist).magnitude * 100f).ToString("0.0") + " cm from wrist" : "";
        return (bones ? "Hand: avatar bones" : "Hand: CONTROLLER (" + weapons.handWhy + ")") + d;
    }

    // the model's name without its kind and pack
    string Short(string id)
    {
        int k = id.LastIndexOf(':');
        return k >= 0 ? id.Substring(k + 1) : id;
    }
}
