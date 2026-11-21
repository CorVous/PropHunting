using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// The grip boards (Cor): one at the far end of each half of the weapon wall, the hunter's (melee) at the south half
// and the hider's (gun) at the north, for each player's own hold of that kind (PHHand). A player in front of a half
// holds their chosen weapon of that kind, seen by everyone. The words are in the text tables (strings_hand.tsv):
// the static labels by their English (TextBaker), the rest set by PHHand.
public static partial class LobbyBuilder
{
    const float HBW = 600f, HBH = 520f;   // a board's canvas units (at 1.5 mm: 0.9 x 0.78 m)
    const int ShowPool = 8;               // shown weapons of each kind at once

    public static PHHand BuildHandBoards(Transform lobby, PHGame game, PHWeapons weapons, float roomW)
    {
        if (weapons == null) { Debug.LogWarning("[prophunt] no PHWeapons: no grip boards"); return null; }
        var store = lobby.Find("Store");
        var root = new GameObject("HandBoards").transform;
        root.SetParent(lobby, false);
        var hand = root.gameObject.AddUdonSharpComponent<PHHand>();
        hand.game = game; hand.weapons = weapons;
        hand.zone = store != null ? store : lobby;
        hand.wallX = roomW / 2f;
        hand.zoneDepth = 4f;
        hand.zoneHalf = 0.3f + Cols * SlotW + 0.3f;   // (to each half's far end, as BuildStore lays them out)
        weapons.hand = hand;

        // the shown weapons: a small pool of each kind, the weapons' own material
        Material meleeMat = weapons.heldRenderer != null && weapons.heldRenderer.Length > 0 ? weapons.heldRenderer[0].sharedMaterial : null;
        Material gunMat = null;
        if (weapons.vrSelectorModel != null) { var r = weapons.vrSelectorModel.GetComponent<MeshRenderer>(); if (r != null) gunMat = r.sharedMaterial; }
        if (gunMat == null) gunMat = meleeMat;
        hand.showMelee = new Transform[ShowPool]; hand.showMeleeFilter = new MeshFilter[ShowPool];
        hand.showGun = new Transform[ShowPool]; hand.showGunFilter = new MeshFilter[ShowPool];
        hand.showOff = new Transform[ShowPool]; hand.showOffFilter = new MeshFilter[ShowPool];
        for (int i = 0; i < ShowPool; i++)
        {
            ShownWeapon(root, "ShowMelee" + i, meleeMat, out hand.showMelee[i], out hand.showMeleeFilter[i]);
            ShownWeapon(root, "ShowGun" + i, gunMat, out hand.showGun[i], out hand.showGunFilter[i]);
            ShownWeapon(root, "ShowOff" + i, meleeMat, out hand.showOff[i], out hand.showOffFilter[i]);
        }

        hand.values = new TextMeshProUGUI[2]; hand.stepText = new TextMeshProUGUI[2]; hand.handText = new TextMeshProUGUI[2];
        // at each half's far end, 0.45 m out from the opening, facing into the room (west)
        for (int k = 0; k < 2; k++)
        {
            float z = (k == 0 ? -1f : 1f) * (LabelZ + LabelHalf - 1.4f);
            HandBoard(root, hand, k, new Vector3(roomW / 2f - 0.45f, 0f, z));
        }
        UdonSharpEditorUtility.CopyProxyToUdon(hand);
        UdonSharpEditorUtility.CopyProxyToUdon(weapons);
        Debug.Log("[prophunt] grip boards at the weapon wall's ends");
        return hand;
    }

    static void ShownWeapon(Transform root, string name, Material m, out Transform t, out MeshFilter f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        f = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        go.SetActive(false);
        t = go.transform;
    }

    // kind 0: the hunter's board, 1: the hider's
    static void HandBoard(Transform root, PHHand hand, int kind, Vector3 at)
    {
        var holder = new GameObject(kind == 0 ? "HunterGripBoard" : "HiderGripBoard").transform;
        holder.SetParent(root, false);
        holder.localPosition = at; holder.localRotation = Quaternion.Euler(0f, 90f, 0f);   // (+z to the wall: read from the room)
        var wood = Mat("StoreWoodDark", C("7d5232"));
        Box(holder, "Post", new Vector3(0f, 0.45f, 0.12f), new Vector3(0.1f, 0.9f, 0.1f), wood);
        Box(holder, "Foot", new Vector3(0f, 0.02f, 0.12f), new Vector3(0.5f, 0.04f, 0.4f), wood);
        // the board, leaning back (its top towards the wall)
        var cv = Canvas(holder, "Board", new Vector3(0f, 1.15f, 0f), Quaternion.Euler(30f, 0f, 0f), new Vector2(HBW, HBH), 0.0015f, true);
        var bg = cv.gameObject.AddComponent<Image>(); bg.color = Felt; bg.raycastTarget = false;
        Txt(cv, "Title", new Vector2(0, 228), new Vector2(580, 44), kind == 0 ? "HUNTER GRIP" : "HIDER GRIP", future, 26, C("f6d773"), TextAlignmentOptions.Center);
        var vals = Txt(cv, "Values", new Vector2(0, 150), new Vector2(570, 104), "", future, 15, C("f6ecd4"), TextAlignmentOptions.TopLeft);
        hand.values[kind] = vals;

        int b = kind * 100;
        Color cMove = C("9bd18a"), cTurn = C("f19a8a"), cSize = C("f2c45a"), cOther = C("7fb3e6");
        string[] axis = { "X-", "X+", "Y-", "Y+", "Z-", "Z+" };
        int[] moveArg = { 2, 1, 4, 3, 6, 5 };
        float rowMove = 62f, rowTurn = 2f, rowSize = -60f, rowHand = -122f;
        Txt(cv, "MoveLabel", new Vector2(-238, rowMove), new Vector2(116, 50), "MOVE", future, 15, C("e1cfa5"), TextAlignmentOptions.Center);
        Txt(cv, "TurnLabel", new Vector2(-238, rowTurn), new Vector2(116, 50), "TURN", future, 15, C("e1cfa5"), TextAlignmentOptions.Center);
        Txt(cv, "SizeLabel", new Vector2(-238, rowSize), new Vector2(116, 50), "SIZE", future, 15, C("e1cfa5"), TextAlignmentOptions.Center);
        for (int i = 0; i < 6; i++)
        {
            float x = -132f + i * 74f;
            HandButton(cv, "Move " + axis[i], axis[i], "Nudge", b + moveArg[i], new Vector2(x, rowMove), new Vector2(68, 50), cMove, hand);
            HandButton(cv, "Turn " + axis[i], axis[i], "Nudge", b + 6 + moveArg[i], new Vector2(x, rowTurn), new Vector2(68, 50), cTurn, hand);
        }
        HandButton(cv, "Smaller", "-", "Grow", b + 2, new Vector2(-132f, rowSize), new Vector2(68, 50), cSize, hand);
        HandButton(cv, "Bigger", "+", "Grow", b + 1, new Vector2(-58f, rowSize), new Vector2(68, 50), cSize, hand);
        hand.stepText[kind] = HandButton(cv, "Step", "", "StepSize", 0, new Vector2(127f, rowSize), new Vector2(290, 50), cOther, hand);
        hand.handText[kind] = HandButton(cv, "Hand", "", "ToggleHand", 0, new Vector2(-95f, rowHand), new Vector2(360, 50), cOther, hand);
        HandButton(cv, "Reset", "RESET", "ResetKind", kind + 1, new Vector2(164f, rowHand), new Vector2(142, 50), C("e98a6b"), hand);
        var hint = Txt(cv, "Hint", new Vector2(0, -205), new Vector2(570, 66), "VR: grab the weapon with your other hand to move it. Everyone sees it.", future, 13, C("e1cfa5"), TextAlignmentOptions.Center);
        hint.enableWordWrapping = true;
    }

    // a board button (PHButton on a UI Button: desktop click, VR pointer, a fingertip); returns its text
    static TextMeshProUGUI HandButton(Transform cv, string name, string label, string action, int arg, Vector2 at, Vector2 size, Color colour, UdonSharpBehaviour target)
    {
        var bimg = Img(cv, "Button " + name, at, size, sRound, new Color(1f, 1f, 1f, 0.01f), 3f);
        bimg.raycastTarget = true;
        var bt = bimg.gameObject.AddComponent<Button>();
        var bn = bt.navigation; bn.mode = Navigation.Mode.None; bt.navigation = bn; bt.targetGraphic = bimg;
        Img(bimg.transform, "Face", Vector2.zero, size, sTile, colour, 2f);
        var t = Txt(bimg.transform, "Text", new Vector2(0, 2), size - new Vector2(10, 12), label, future, 15, C("1e190f"), TextAlignmentOptions.Center);
        var btn = bimg.gameObject.AddUdonSharpComponent<PHButton>();
        btn.action = action; btn.arg = arg; btn.target = target;
        UdonSharpEditorUtility.CopyProxyToUdon(btn);
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(btn);
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(bt.onClick, udon.SendCustomEvent, nameof(PHButton.Press));
        Pressy(bimg.gameObject, bt);
        return t;
    }
}
