using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRCStation = VRC.SDK3.Components.VRCStation;
using Mobility = VRC.SDKBase.VRCStation.Mobility;

// The grip station (Cor): a chair beside the debug sign and a small board in front of it, at arm's length for a seated
// player, for lining up each weapon in the hand (PHGripStation). A tool like the debug sign, so its words are English
// only (not in the text tables).
public static partial class LobbyBuilder
{
    const float GW = 600f, GH = 640f;   // the board's canvas units (at 1.6 mm: 0.96 x 1.02 m)

    // at: the chair's foot (lobby space); yaw: the way the seated player faces
    public static PHGripStation BuildGripStation(Transform lobby, PHWeapons weapons, Vector3 at, float yaw)
    {
        var root = new GameObject("GripStation").transform;
        root.SetParent(lobby, false);
        root.localPosition = at; root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var wood = Mat("StoreWoodDark", C("7d5232"));
        var cloth = Mat("GripSeat", C("21573f"));
        // the chair: four legs, a seat, a back
        foreach (var x in new[] { -0.22f, 0.22f })
            foreach (var z in new[] { -0.2f, 0.2f })
                Box(root, "Leg", new Vector3(x, 0.22f, z), new Vector3(0.05f, 0.44f, 0.05f), wood);
        Box(root, "SeatTop", new Vector3(0f, 0.46f, 0f), new Vector3(0.52f, 0.05f, 0.48f), cloth);
        Box(root, "Back", new Vector3(0f, 0.8f, -0.23f), new Vector3(0.52f, 0.62f, 0.05f), wood);

        // the station itself: the seat's collider (Interact to sit), VRC Station and the script
        var seatGo = new GameObject("Seat");
        seatGo.transform.SetParent(root, false);
        seatGo.transform.localPosition = new Vector3(0f, 0.48f, 0f);
        var col = seatGo.AddComponent<BoxCollider>(); col.size = new Vector3(0.52f, 0.12f, 0.48f); col.isTrigger = false;
        var sitAt = new GameObject("SitAt").transform;
        sitAt.SetParent(seatGo.transform, false);
        sitAt.localPosition = new Vector3(0f, 0.02f, 0.02f);
        var st = seatGo.AddComponent<VRCStation>();
        st.seated = true;
        st.PlayerMobility = Mobility.Immobilize;
        st.disableStationExit = false;
        st.canUseStationFromStation = false;
        st.stationEnterPlayerLocation = sitAt;
        st.stationExitPlayerLocation = root;
        var gs = seatGo.AddUdonSharpComponent<PHGripStation>();
        gs.weapons = weapons;
        gs.seat = st;

        // the weapon shown in the hand (its own object: placed each frame in world space)
        var pv = new GameObject("GripPreview");
        pv.transform.SetParent(root, false);
        var mf = pv.AddComponent<MeshFilter>();
        mf.sharedMesh = weapons.meleeMesh != null && weapons.meleeMesh.Length > 0 ? weapons.meleeMesh[0] : null;
        var mr = pv.AddComponent<MeshRenderer>();
        mr.sharedMaterial = weapons.heldRenderer != null && weapons.heldRenderer.Length > 0 ? weapons.heldRenderer[0].sharedMaterial : null;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        gs.preview = pv.transform; gs.previewFilter = mf;

        // the board: in front of the seat, a little to the left (the right hand holds the weapon), leaning back
        var cv = Canvas(root, "GripBoard", new Vector3(-0.18f, 1.05f, 0.62f), Quaternion.Euler(20f, 0f, 0f), new Vector2(GW, GH), 0.0016f, true);
        var bg = cv.gameObject.AddComponent<Image>(); bg.color = Felt; bg.raycastTarget = false;
        Txt(cv, "Label", new Vector2(0, 296), new Vector2(580, 30), "GRIP STATION", future, 18, C("e1cfa5"), TextAlignmentOptions.Center);
        gs.title = Txt(cv, "Title", new Vector2(0, 248), new Vector2(580, 60), "", future, 15, Color.white, TextAlignmentOptions.Center);
        var lib = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        gs.values = Txt(cv, "Values", new Vector2(0, 160), new Vector2(580, 110), "", lib != null ? lib : future, 14, C("f6ecd4"), TextAlignmentOptions.TopLeft);
        Txt(cv, "Hint", new Vector2(0, 96), new Vector2(580, 22), "VR: grab the weapon with your LEFT grip, move it, let go", future, 10, C("e1cfa5"), TextAlignmentOptions.Center);

        // the buttons: four columns
        string[,] rows =
        {
            { "<< 10", "Prev10", "0" },   { "<", "Prev", "0" },        { ">", "Next", "0" },         { "10 >>", "Next10", "0" },
            { "MELEE/GUN", "Kind", "0" }, { "SMALLER", "Smaller", "0" }, { "BIGGER", "Bigger", "0" }, { "RESET", "ResetOne", "0" },
            { "GRIP X-", "Nudge", "2" },  { "GRIP X+", "Nudge", "1" },  { "GRIP Y-", "Nudge", "4" },  { "GRIP Y+", "Nudge", "3" },
            { "GRIP Z-", "Nudge", "6" },  { "GRIP Z+", "Nudge", "5" },  { "TURN X-", "Nudge", "8" },  { "TURN X+", "Nudge", "7" },
            { "TURN Y-", "Nudge", "10" }, { "TURN Y+", "Nudge", "9" },  { "TURN Z-", "Nudge", "12" }, { "TURN Z+", "Nudge", "11" },
        };
        int n = rows.GetLength(0);
        for (int i = 0; i < n; i++)
        {
            int r = i / 4, c = i % 4;
            var pos = new Vector2(-219f + c * 146f, 44f - r * 62f);
            Color colour = r == 0 ? C("7fb3e6") : r == 1 ? C("f2c45a") : r < 4 ? C("9bd18a") : C("f19a8a");
            GripButton(cv, rows[i, 0], rows[i, 1], int.Parse(rows[i, 2]), pos, new Vector2(138, 54), colour, gs);
        }
        GripButton(cv, "SAVE TO LOG", "Save", 0, new Vector2(0f, 44f - 5 * 62f), new Vector2(580, 54), C("e98a6b"), gs);

        UdonSharpEditorUtility.CopyProxyToUdon(gs);
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(gs);
        udon.interactText = "Sit to line up weapons";
        Debug.Log("[prophunt] grip station at " + at);
        return gs;
    }

    static void GripButton(Transform cv, string label, string action, int arg, Vector2 at, Vector2 size, Color colour, UdonSharpBehaviour target)
    {
        var bimg = Img(cv, "Button " + label, at, size, sRound, new Color(1f, 1f, 1f, 0.01f), 3f);
        bimg.raycastTarget = true;
        var bt = bimg.gameObject.AddComponent<Button>();
        var bn = bt.navigation; bn.mode = Navigation.Mode.None; bt.navigation = bn; bt.targetGraphic = bimg;
        Img(bimg.transform, "Face", Vector2.zero, size, sTile, colour, 2f);
        Txt(bimg.transform, "Text", new Vector2(0, 2), size - new Vector2(10, 14), label, future, 13, C("1e190f"), TextAlignmentOptions.Center);
        var btn = bimg.gameObject.AddUdonSharpComponent<PHButton>();
        btn.action = action; btn.arg = arg; btn.target = target;
        UdonSharpEditorUtility.CopyProxyToUdon(btn);
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(btn);
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(bt.onClick, udon.SendCustomEvent, nameof(PHButton.Press));
        Pressy(bimg.gameObject, bt);
    }
}
