using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// The grip boards (Cor): one on each half's panel of the weapon wall (beside its up / down buttons), the hunter's
// (melee) on the south half's and the hider's (gun) on the north half's, for each player's own hold of that kind (PHHand). A player in front of a half
// holds their chosen weapon of that kind, seen by everyone. The words are in the text tables (strings_hand.tsv):
// the static labels by their English (TextBaker), the rest set by PHHand.
public static partial class LobbyBuilder
{
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
        // (Cor: a board standing in the room was in the way) on each half's own panel, the hip-height plate by the
        // middle of the wall with the up / down buttons: a second plate beside it, on its outer side, tilted with it
        var ps = store != null ? store.GetComponent<PHStore>() : null;
        for (int k = 0; k < 2; k++)
        {
            PHShelf shelf = ps == null ? null : (k == 0 ? ps.shelfMelee : ps.shelfGun);
            Transform tilt = shelf != null && shelf.cart != null ? shelf.cart.Find("Tilt") : null;
            if (tilt == null) { Debug.LogWarning("[prophunt] no store panel for the " + (k == 0 ? "hunter" : "hider") + " grip board"); continue; }
            HandBoard(tilt, hand, k, k == 0 ? -1f : 1f);
        }
        UdonSharpEditorUtility.CopyProxyToUdon(hand);
        UdonSharpEditorUtility.CopyProxyToUdon(weapons);
        Debug.Log("[prophunt] grip boards on the weapon wall's panels");
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

    // kind 0: the hunter's board, 1: the hider's; on the panel's tilted plate (Tilt), out to side (-1: -x, +1: +x)
    static void HandBoard(Transform tilt, PHHand hand, int kind, float side)
    {
        const float W = 600f, H = 400f, S = 0.0013f;   // 0.78 x 0.52 m, the panel plate's depth
        float x0 = side * (0.53f + 0.04f + W * S / 2f);
        var wood = Mat("StoreWoodDark", C("7d5232"));
        var felt = Mat("StoreFelt", C("173d2c"));
        Oval(tilt, kind == 0 ? "HunterGripRim" : "HiderGripRim", new Vector3(x0, -0.015f, 0f), new Vector3(W * S + 0.06f, 0.04f, 0.58f), wood);
        Oval(tilt, kind == 0 ? "HunterGripPlate" : "HiderGripPlate", new Vector3(x0, 0f, 0f), new Vector3(W * S, 0.04f, 0.52f), felt);
        // lying on the plate, read from above, its top away from the player (as the coins' number)
        var cv = Canvas(tilt, kind == 0 ? "HunterGripBoard" : "HiderGripBoard", new Vector3(x0, 0.022f, 0f), Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 180f, 0f), new Vector2(W, H), S, true);
        Txt(cv, "Title", new Vector2(0, 178), new Vector2(580, 34), kind == 0 ? "HUNTER GRIP" : "HIDER GRIP", future, 20, C("f6d773"), TextAlignmentOptions.Center);
        var vals = Txt(cv, "Values", new Vector2(0, 122), new Vector2(570, 74), "", future, 12, C("f6ecd4"), TextAlignmentOptions.TopLeft);
        hand.values[kind] = vals;

        int b = kind * 100;
        Color cMove = C("9bd18a"), cTurn = C("f19a8a"), cSize = C("f2c45a"), cOther = C("7fb3e6");
        string[] axis = { "X-", "X+", "Y-", "Y+", "Z-", "Z+" };
        int[] moveArg = { 2, 1, 4, 3, 6, 5 };
        float rowMove = 62f, rowTurn = 14f, rowSize = -34f, rowHand = -82f;
        Vector2 bs = new Vector2(68, 42), ls = new Vector2(116, 42);
        Txt(cv, "MoveLabel", new Vector2(-238, rowMove), ls, "MOVE", future, 13, C("e1cfa5"), TextAlignmentOptions.Center);
        Txt(cv, "TurnLabel", new Vector2(-238, rowTurn), ls, "TURN", future, 13, C("e1cfa5"), TextAlignmentOptions.Center);
        Txt(cv, "SizeLabel", new Vector2(-238, rowSize), ls, "SIZE", future, 13, C("e1cfa5"), TextAlignmentOptions.Center);
        for (int i = 0; i < 6; i++)
        {
            float x = -132f + i * 74f;
            HandButton(cv, "Move " + axis[i], axis[i], "Nudge", b + moveArg[i], new Vector2(x, rowMove), bs, cMove, hand);
            HandButton(cv, "Turn " + axis[i], axis[i], "Nudge", b + 6 + moveArg[i], new Vector2(x, rowTurn), bs, cTurn, hand);
        }
        HandButton(cv, "Smaller", "-", "Grow", b + 2, new Vector2(-132f, rowSize), bs, cSize, hand);
        HandButton(cv, "Bigger", "+", "Grow", b + 1, new Vector2(-58f, rowSize), bs, cSize, hand);
        hand.stepText[kind] = HandButton(cv, "Step", "", "StepSize", 0, new Vector2(127f, rowSize), new Vector2(290, 42), cOther, hand);
        hand.handText[kind] = HandButton(cv, "Hand", "", "ToggleHand", 0, new Vector2(-95f, rowHand), new Vector2(360, 42), cOther, hand);
        HandButton(cv, "Reset", "RESET", "ResetKind", kind + 1, new Vector2(164f, rowHand), new Vector2(142, 42), C("e98a6b"), hand);
        var hint = Txt(cv, "Hint", new Vector2(0, -146), new Vector2(570, 60), "VR: grab the weapon with your other hand to move it. Everyone sees it.", future, 11, C("e1cfa5"), TextAlignmentOptions.Center);
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
