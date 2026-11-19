using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// One half of the weapon wall on the lobby's east wall (Cor, 2026-11-19): a paternoster shelf with every weapon of
// one kind on it, ten to a shelf, floor to ceiling: the shelves come up out of a black hole in the floor and go into
// one at the ceiling (Cor), eight showing. Owned weapons are solid, the rest dithered (the
// PropHunt/ShelfWeapon shader's _Dither) with a price tag (shop) or a star (trophy). A cart on a rail at the
// A small floating panel (two round buttons, PHButton -> Up / Down, and the player's coins), slanted towards them,
// follows them along this half. Clicking a weapon (PHShelfSlot) carries it, or opens the buy dialog or the trophy's note
// (PHStore.Clicked). Everything here is the local player's own: nothing is synced.
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
    public GameObject[] tagShop, tagCool, tagGoofy, sel;
    public TextMeshPro[] price;
    public float baseY = 0.9f, rowH = 0.62f;

    public Transform cart;
    public float cartMin = -2.5f, cartMax = 2.5f, cartZ = 1.2f;
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
                itemMr[k].SetPropertyBlock(mpb);
            }
            bool trophy = any && !own && store.wTrophy[c] >= 0;
            bool shop = any && !own && !trophy && cat == PHStore.CAT_SHOP;
            tagShop[k].SetActive(shop);
            if (shop) price[k].text = store.wPrice[c].ToString();
            tagCool[k].SetActive(trophy && cat != PHStore.CAT_GOOFY);
            tagGoofy[k].SetActive(trophy && cat == PHStore.CAT_GOOFY);
            sel[k].SetActive(any && store.Picked(c));
        }
    }

    // ------------------------------------------------------------------ the buttons (PHButton on the cart)
    public void Up() { Turn(1); }
    public void Down() { Turn(-1); }

    // +1: every shelf goes up one place and the row below comes in at the bottom; -1 the other way
    void Turn(int dir)
    {
        if (turnT >= 0f || nRows <= 1) return;
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

    // ------------------------------------------------------------------ each frame: the turn, the cart
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
        Vector3 cp = cart.localPosition;
        float want = Mathf.Clamp(lp.x, cartMin, cartMax);
        cp.x = Mathf.Lerp(cp.x, want, 1f - Mathf.Exp(-10f * Time.deltaTime));
        cp.z = cartZ;
        cart.localPosition = cp;
    }

    // ------------------------------------------------------------------ a click on a weapon (PHShelfSlot)
    public void SlotClicked(int k)
    {
        if (turnT >= 0f || content == null || k < 0 || k >= content.Length) return;
        int c = content[k];
        if (c < 0) return;
        Vector3 at = items[k].parent.position + transform.up * 0.45f + transform.forward * 0.35f;
        store.Clicked(c, at, transform.rotation * Quaternion.Euler(0f, 180f, 0f));   // a canvas reads from its back (+z away)
        Refresh();
    }
}
