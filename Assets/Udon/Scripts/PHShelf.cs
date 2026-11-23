using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// One half of the weapon wall on the lobby's east wall (Cor, 2026-11-19): a paternoster shelf with every weapon of
// one kind on it, ten to a shelf, floor to ceiling: the shelves come up out of a black hole in the floor and go into
// one at the ceiling (Cor), eight showing. Owned weapons are solid, the rest dithered (the
// PropHunt/ShelfWeapon shader's _Dither) with a price tag (shop) or a star (trophy).
// A small panel (two round buttons, PHButton -> Up / Down, and the player's coins), slanted towards the player,
// stands at a fixed place at this half's inner end, by the middle of the wall (Cor, hop playtest: it no longer
// follows the player, so it never sits between them and the shelves); only its height follows the player's size.
// Clicking a weapon (PHShelfSlot) carries it, or opens the buy dialog or the trophy's note (PHStore.Clicked); while
// that card is open the slots' colliders are off (SetSlots), so the VR laser reaches the card's Buy button.
// Everything here is the local player's own: nothing is synced.
// This object's space: x along the wall (x = 0 the half's middle; column 0 at +x, the player's left), y up from
// the floor, z out of the wall into the room.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHShelf : UdonSharpBehaviour
{
    public PHStore store;
    public int kind;                 // 0 melee, 1 gun
    public int[] order;              // catalog indices in shelf order (starters, trophy weapons, the shop by price)
    public Mesh[] meshes;            // by catalog index (null: not converted yet, the slot shows only its tag)
    public Vector3[] itemPos;        // by catalog index: where the model sits in its slot, its scale (x of itemSize)
    public float[] itemScale;
    public Quaternion itemRot;       // melee stands up, guns lie along the shelf

    public Transform[] rows;         // 5 shelf rows (4 showing, 1 out of sight for the turn)
    public Transform[] items;        // row * cols + column
    public MeshFilter[] itemMf;
    public MeshRenderer[] itemMr;
    public GameObject[] tagShop, tagCool, tagGoofy;
    public GameObject[] glow;        // the warm pool of light under the carried weapon (with the shader's _Glow)
    public TextMeshPro[] price;
    public TextMeshPro[] rowNum;     // root * 2 + end: the shelf row's number on the lip at both ends
    public float baseY = 0.9f, rowH = 0.62f;

    public Collider[] slotCol;       // the PHShelfSlot colliders, by slot (off while the buy card is open)

    public Transform cart;           // the panel: fixed where LobbyBuilder put it (PHShelf moves only its height)
    public float panelY = 1.05f;     // the plate's height in the panel's own space
    public TextMeshPro cartCoins;

    public int shown = 8;            // shelves showing (one more row root goes round out of sight)
    public int cols = 16;
    int COLS, SHOWN, ROOTS;
    const float TURN = 0.4f;
    int[] content;                   // catalog index per slot (-1 empty)
    int[] ro;                        // the row root at each place: 0 bottom .. 3 top, 4 out of sight
    int off;                         // the shelf row at the bottom
    int nRows;
    float turnT = -1f;
    int turnDir;
    MaterialPropertyBlock mpb;
    VRCPlayerApi local;

    void Start()
    {
        SHOWN = shown; ROOTS = shown + 1; COLS = cols;
        content = new int[ROOTS * COLS];
        ro = new int[ROOTS];
        for (int i = 0; i < ROOTS; i++) ro[i] = i;
        nRows = (order.Length + COLS - 1) / COLS;
        mpb = new MaterialPropertyBlock();
        local = Networking.LocalPlayer;
        Place();
        Refresh();
    }

    // ------------------------------------------------------------------ the shelves
    void Place()
    {
        for (int p = 0; p < ROOTS; p++)
        {
            Transform r = rows[ro[p]];
            r.localPosition = new Vector3(0f, baseY + p * rowH, 0f);
            r.gameObject.SetActive(p < SHOWN);
        }
    }

    public void Refresh()
    {
        if (content == null) return;
        store.Init();
        for (int p = 0; p < SHOWN; p++) Bind(ro[p], Wrap(off + p));
        if (cartCoins != null) cartCoins.text = store.coins.ToString();
    }

    int Wrap(int r) { return ((r % nRows) + nRows) % nRows; }

    void Bind(int root, int shelfRow)
    {
        if (rowNum != null)
            for (int e = 0; e < 2; e++)
            {
                int i = root * 2 + e;
                if (i < rowNum.Length && rowNum[i] != null) rowNum[i].text = (shelfRow + 1).ToString();
            }
        for (int col = 0; col < COLS; col++)
        {
            int k = root * COLS + col;
            int o = shelfRow * COLS + col;
            int c = o < order.Length ? order[o] : -1;
            content[k] = c;
            bool any = c >= 0;
            bool own = any && store.own[c];
            int cat = any ? store.wCat[c] : -1;
            Mesh m = any ? meshes[c] : null;
            items[k].gameObject.SetActive(m != null);
            if (m != null)
            {
                itemMf[k].sharedMesh = m;
                items[k].localPosition = itemPos[c];
                items[k].localRotation = itemRot;
                items[k].localScale = Vector3.one * itemScale[c];
                mpb.SetFloat("_Dither", own ? 0f : 1f);
                mpb.SetFloat("_Glow", store.Picked(c) ? 1f : 0f);   // the one being carried glows (Cor)
                itemMr[k].SetPropertyBlock(mpb);
            }
            bool trophy = any && !own && store.wTrophy[c] >= 0;
            bool shop = any && !own && !trophy && cat == PHStore.CAT_SHOP;
            tagShop[k].SetActive(shop);
            if (shop) price[k].text = store.wPrice[c].ToString();
            glow[k].SetActive(m != null && store.Picked(c));
            tagCool[k].SetActive(trophy && cat != PHStore.CAT_GOOFY);
            tagGoofy[k].SetActive(trophy && cat == PHStore.CAT_GOOFY);
        }
    }

    // ------------------------------------------------------------------ the buttons (PHButton on the cart)
    public void Up() { Turn(1); }
    public void Down() { Turn(-1); }

    // +1: every shelf goes up one place and the row below comes in at the bottom; -1 the other way
    void Turn(int dir)
    {
        if (turnT >= 0f || nRows <= 1) return;
        if (store.dialog != null) store.dialog.Close();   // the card would hang over a shelf that moved away
        int stage = ro[ROOTS - 1];
        Bind(stage, dir > 0 ? Wrap(off - 1) : Wrap(off + SHOWN));
        rows[stage].gameObject.SetActive(true);
        rows[stage].localPosition = new Vector3(0f, baseY + (dir > 0 ? -1 : SHOWN) * rowH, 0f);
        turnDir = dir; turnT = 0f;
        if (store.sound != null) store.sound.Play("switch_002", 0.5f);
    }

    void EndTurn()
    {
        int stage = ro[ROOTS - 1];
        if (turnDir > 0)
        {
            int top = ro[SHOWN - 1];
            for (int p = SHOWN - 1; p > 0; p--) ro[p] = ro[p - 1];
            ro[0] = stage; ro[ROOTS - 1] = top;
            off = Wrap(off - 1);
        }
        else
        {
            int bottom = ro[0];
            for (int p = 0; p < SHOWN - 1; p++) ro[p] = ro[p + 1];
            ro[SHOWN - 1] = stage; ro[ROOTS - 1] = bottom;
            off = Wrap(off + 1);
        }
        turnT = -1f;
        Place();
    }

    // ------------------------------------------------------------------ each frame: the turn, the panel's height
    void Update()
    {
        if (turnT >= 0f)
        {
            turnT += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(turnT / TURN));
            int stage = ro[ROOTS - 1];
            for (int p = 0; p < SHOWN; p++) rows[ro[p]].localPosition = new Vector3(0f, baseY + (p + turnDir * k) * rowH, 0f);
            rows[stage].localPosition = new Vector3(0f, baseY + ((turnDir > 0 ? -1 : SHOWN) + turnDir * k) * rowH, 0f);
            if (turnT >= TURN) EndTurn();
        }
        if (cart == null || local == null || !Utilities.IsValid(local)) return;
        Vector3 lp = transform.InverseTransformPoint(local.GetPosition());
        if (lp.z < 0f || lp.z > 9f || lp.y < -1f || lp.y > 4f) return;   // not in front of this wall
        // the panel stays where it was built (Cor: a fixed place); only its height follows the player's size (Cor):
        // the plate (panelY up in the panel) at 60% of their eye height, between 0.8 and 1.3 m
        Vector3 cp = cart.localPosition;
        float eye = local.GetAvatarEyeHeightAsMeters();
        if (eye < 0.3f || eye > 5f) eye = 1.6f;
        float plate = Mathf.Clamp(eye * 0.6f, 0.8f, 1.3f);
        float want = plate - panelY;
        if (Mathf.Abs(cp.y - want) < 0.001f) return;
        cp.y = Mathf.Lerp(cp.y, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
        cart.localPosition = cp;
    }

    // the slots' colliders on or off (PHStoreDialog: off while its card is open, so they don't catch the laser)
    public void SetSlots(bool on)
    {
        if (slotCol == null) return;
        for (int i = 0; i < slotCol.Length; i++) if (slotCol[i] != null) slotCol[i].enabled = on;
    }

    // ------------------------------------------------------------------ a click on a weapon (PHShelfSlot)
    public void SlotClicked(int k)
    {
        if (turnT >= 0f || content == null || k < 0 || k >= content.Length) return;
        int c = content[k];
        if (c < 0) return;
        // the card 0.65 m out from the slot: clear of the slots' colliders (whose fronts are 0.36 m out) even with
        // them on, and out of the alcove (hop playtest: the laser hit the slots, not the Buy button)
        Vector3 at = items[k].parent.position + transform.up * 0.45f + transform.forward * 0.65f;
        store.Clicked(c, at, transform.rotation * Quaternion.Euler(0f, 180f, 0f));   // a canvas reads from its back (+z away)
        Refresh();
    }
}
