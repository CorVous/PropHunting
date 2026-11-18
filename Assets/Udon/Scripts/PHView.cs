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

    Vector3 panelOffset = new Vector3(0.45f, 1.1f, 0.55f);   // relative to the player, in the player's facing
    float panelYawOffset;
    bool vr;
    int frame;

    void Start()
    {
        vr = Networking.LocalPlayer.IsUserInVR();
        if (desktopRoot != null) desktopRoot.gameObject.SetActive(!vr);
        if (wristRoot != null) wristRoot.gameObject.SetActive(vr);
        if (vrPanel != null) vrPanel.gameObject.SetActive(false);
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
        VRCPlayerApi.TrackingData hand = me.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
        // on the back of the left wrist, facing up when the palm faces down
        wristRoot.SetPositionAndRotation(hand.position + hand.rotation * new Vector3(0f, 0.03f, -0.05f), hand.rotation * Quaternion.Euler(90f, 0f, 0f));
        if (!vrPanel.gameObject.activeSelf) return;
        Quaternion facing = Quaternion.Euler(0f, me.GetRotation().eulerAngles.y, 0f);
        if (vrPanelPickup.IsHeld)
        {
            // remember where the player put it, relative to themselves
            panelOffset = Quaternion.Inverse(facing) * (vrPanel.position - me.GetPosition());
            panelYawOffset = vrPanel.rotation.eulerAngles.y - facing.eulerAngles.y;
            return;
        }
        vrPanel.SetPositionAndRotation(me.GetPosition() + facing * panelOffset, Quaternion.Euler(0f, facing.eulerAngles.y + panelYawOffset, 0f));
    }
}
