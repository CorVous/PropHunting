using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Components;

// What the local player sees besides the world: as little as possible (most cues are sounds, PHAudio).
//   health: a hider's health bar only. Desktop: low on the screen. VR: on the left wrist, never in front of
//           the eyes.
//   third-person view (G27, the prototype's PipScreen): a camera behind the hider's prop shows how the prop
//           sits in the scene (the hider's own prop is on the MirrorReflection layer, which only this camera
//           draws). Desktop: a picture in the lower right corner. VR: a panel floating beside the player that
//           can be grabbed and put where it suits; it keeps that place relative to the player.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHView : UdonSharpBehaviour
{
    public PHGame game;
    public PHLocal local;
    public Transform desktopRoot;      // head-locked, desktop only
    public Image desktopHealth;        // its right edge (anchorMax.x) = health share
    public GameObject desktopHealthBar;
    public GameObject desktopPip;
    public Transform wristRoot;        // follows the left hand, VR only
    public Image wristHealth;
    public GameObject wristHealthBar;
    public Camera pipCam;
    public Transform vrPanel;          // the grabbable third-person panel (VRCPickup)
    public VRCPickup vrPanelPickup;
    public LayerMask pipBlockers = 1 << 11;

    // The panel keeps its place in the real room (Cor): its pose is kept relative to the playspace's origin, in
    // units of the avatar's size, so walking with the stick or turning with it does not move it about the room.
    Vector3 panelLocal;
    Quaternion panelLocalRot = Quaternion.identity;
    bool panelPlaced;
    Vector3 panelBaseScale = new Vector3(0.36f, 0.24f, 1f);
    Vector3 wristBaseScale = Vector3.one * 0.0004f;
    bool vr;
    int frame;

    void Start()
    {
        vr = Networking.LocalPlayer.IsUserInVR();
        if (desktopRoot != null) desktopRoot.gameObject.SetActive(!vr);
        if (wristRoot != null) wristRoot.gameObject.SetActive(vr);
        if (vrPanel != null) { panelBaseScale = vrPanel.localScale; vrPanel.gameObject.SetActive(false); }
        if (wristRoot != null) wristBaseScale = wristRoot.localScale;
        // the third-person camera does not draw the panel that shows its picture (Cor: the screen saw itself)
        if (pipCam != null && vrPanel != null) pipCam.cullingMask &= ~(1 << vrPanel.gameObject.layer);
    }

    // the avatar's size against a 1.6 m eye height: what is worn or held scales with it
    float AvatarScale()
    {
        float eye = Networking.LocalPlayer.GetAvatarEyeHeightAsMeters();
        return Mathf.Clamp(eye > 0.01f ? eye / 1.6f : 1f, 0.2f, 5f);
    }

    PHSlot Me() { return game.LocalSlot(); }

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
        PHSlot s = Me();
        bool hider = s != null && game.RoleOf(s.index) == PHGame.ROLE_HIDER && s.hiding && !s.caught && s.def >= 0
            && (game.phase == PHGame.PH_BRIEF || game.phase == PHGame.PH_HIDE || game.phase == PHGame.PH_HUNT);
        float share = hider && s.maxHealth > 0 ? Mathf.Clamp01((float)s.health / s.maxHealth) : 0f;
        if (vr)
        {
            wristHealthBar.SetActive(hider);
            if (hider) wristHealth.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);
            if (vrPanel.gameObject.activeSelf != hider) vrPanel.gameObject.SetActive(hider);
        }
        else
        {
            desktopHealthBar.SetActive(hider);
            if (hider) desktopHealth.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);
            desktopPip.SetActive(hider);
        }
        // the third-person camera renders every second frame while there is a prop to show
        frame++;
        bool pip = hider;
        pipCam.enabled = pip && (frame & 1) == 0;
        if (pip) PlaceCamera(s);
    }

    // behind the prop, looking at it; pulled in front of walls and ceilings
    void PlaceCamera(PHSlot s)
    {
        float h = game.gen.defSy[s.def] / 1000f;
        Vector3 target = s.propPos + Vector3.up * (h * 0.6f);
        Quaternion head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
        Vector3 back = head * Vector3.back; back.y = 0f;
        if (back.sqrMagnitude < 1e-4f) back = Vector3.back;
        back.Normalize();
        float dist = Mathf.Clamp(2.2f + h, 2.2f, 5f);
        Vector3 want = target + back * dist + Vector3.up * (0.8f + h * 0.5f);
        Vector3 dir = want - target;
        RaycastHit r;
        if (Physics.Raycast(target, dir.normalized, out r, dir.magnitude, pipBlockers, QueryTriggerInteraction.Ignore))
            want = target + dir.normalized * Mathf.Max(0.3f, r.distance - 0.15f);
        pipCam.transform.SetPositionAndRotation(want, Quaternion.LookRotation(target - want, Vector3.up));
    }

    public override void PostLateUpdate()
    {
        if (profWatch == null) { PostLateBody(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        PostLateBody();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    void PostLateBody()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (!vr)
        {
            VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            desktopRoot.SetPositionAndRotation(head.position + head.rotation * new Vector3(0f, 0f, 0.5f), head.rotation);
            return;
        }
        float sc = AvatarScale();
        VRCPlayerApi.TrackingData headT = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        // The health bar lies along the left forearm, near the wrist, and faces the eyes (Cor: it floated off the
        // arm and kept one size). From the avatar's bones, so it fits any avatar; from the hand's tracking where
        // an avatar has no such bones.
        Vector3 elbow = me.GetBonePosition(HumanBodyBones.LeftLowerArm), wrist = me.GetBonePosition(HumanBodyBones.LeftHand);
        Vector3 arm = wrist - elbow;
        if (elbow.sqrMagnitude > 1e-6f && wrist.sqrMagnitude > 1e-6f && arm.sqrMagnitude > 1e-6f)
        {
            float len = arm.magnitude;
            Vector3 along = arm / len;
            Vector3 at = elbow + arm * 0.72f;
            Vector3 toEye = headT.position - at;
            Vector3 up = toEye - along * Vector3.Dot(toEye, along);   // off the arm, towards the eyes
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up.Normalize();
            // the canvas's right runs along the arm; its face looks at the eyes
            wristRoot.SetPositionAndRotation(at + up * (0.2f * len), Quaternion.LookRotation(-up, Vector3.Cross(-up, along)));
            wristRoot.localScale = wristBaseScale * (len / 0.25f) * 0.75f;
        }
        else
        {
            VRCPlayerApi.TrackingData hand = me.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
            wristRoot.SetPositionAndRotation(hand.position + hand.rotation * new Vector3(0f, 0.03f, -0.05f) * sc, hand.rotation * Quaternion.Euler(90f, 0f, 0f));
            wristRoot.localScale = wristBaseScale * sc;
        }
        if (!vrPanel.gameObject.activeSelf) return;
        vrPanel.localScale = new Vector3(panelBaseScale.x * sc, panelBaseScale.y * sc, panelBaseScale.z);
        VRCPlayerApi.TrackingData origin = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
        Quaternion inv = Quaternion.Inverse(origin.rotation);
        if (!panelPlaced)
        {
            // where it first appears: to the right of the view, a little below the eyes, turned to the player
            panelPlaced = true;
            Quaternion yawOnly = Quaternion.Euler(0f, headT.rotation.eulerAngles.y, 0f);
            Vector3 p = headT.position + yawOnly * (new Vector3(0.38f, -0.22f, 0.55f) * sc);
            Quaternion r = Quaternion.LookRotation(p - headT.position + Vector3.up * (0.1f * sc), Vector3.up);
            panelLocal = inv * (p - origin.position) / sc;
            panelLocalRot = inv * r;
        }
        if (vrPanelPickup.IsHeld)
        {
            // remember where the player put it, in the room
            panelLocal = inv * (vrPanel.position - origin.position) / sc;
            panelLocalRot = inv * vrPanel.rotation;
            return;
        }
        vrPanel.SetPositionAndRotation(origin.position + origin.rotation * (panelLocal * sc), origin.rotation * panelLocalRot);
    }
}
