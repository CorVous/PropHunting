using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// The map trail on the lobby's north wall, above the host panel (Cor, 2026-11-19): a card for every map played here
// (PHGame's trail, up to 12), the one played last on the left, each with a photo of the map, its size and how often
// it was played; a red meeple stands on the one REPLAY will play. The same card, larger, is the panel's REPLAY button.
// A VRChat world cannot save pictures, so the photo is taken in the world: when a round's map is built, a camera
// over it draws one frame into that slot's render texture. Each client takes its own, so a player who joins later
// has no photo of the maps played before (a plain card with the size's building) until they are played again.
// Nothing here is synced: the cards follow PHGame's synced trail.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHHistory : UdonSharpBehaviour
{
    public PHGame game;
    public MapBuilderUdon builder;
    public Camera cam;                   // off; switched on for the one frame of a photo
    public RenderTexture[] shots;        // one per trail slot
    public float pitch = 52f, yaw = 35f; // the photo's angle: from the south-west, looking down (the lobby lies behind it)

    // the cards: 0 to 11 on the wall (newest first), 12 the panel's REPLAY card
    public GameObject[] cardFace;        // the card itself (off: an empty space on the trail)
    public GameObject[] cardEmpty;       // the dashed outline of an empty space
    public RawImage[] cardPhoto;
    public GameObject[] cardBlank;       // no photo here yet: the size's building on blue
    public Image[] cardBlankIcon;
    public TextMeshProUGUI[] cardSize, cardRuns;
    public GameObject[] cardMarker;      // the red meeple: REPLAY plays this one
    public GameObject[] cardBonus;       // the "1.5x" sticker: replaying this map pays it (PHGame.SlotBonus)
    public RectTransform[] cardRect;
    public Sprite[] sizeIcon;            // home, tavern, church, castle (the panel's MAP row)

    readonly string[] sizeCap = { "S", "M", "L", "XL", "XS" };
    uint[] shotSeed = new uint[12];      // what each slot's photo shows (0: none)
    int[] shotSize = new int[12];
    int snapSlot = -1; float snapAt; bool camOn;
    float fogStartWas, fogEndWas;
    int[] order = new int[12];
    int[] shownSlot; int[] shownRuns; bool[] shownPhoto; bool[] shownPick; uint[] shownSeed; bool[] shownBonus;
    float nextRefresh;

    // PHGame: this round's map is built; its photo is taken a moment later (the sun's shadows are baked first)
    public void Snap(int slot)
    {
        if (slot < 0 || slot >= shots.Length) return;
        snapSlot = slot; snapAt = Time.time + 1.2f;
    }

    void Update()
    {
        if (camOn)
        {
            cam.enabled = false; camOn = false; nextRefresh = 0f;
            RenderSettings.fogStartDistance = fogStartWas; RenderSettings.fogEndDistance = fogEndWas;
        }
        if (snapSlot >= 0 && Time.time >= snapAt)
        {
            int s = snapSlot; snapSlot = -1;
            if (game.MapReady && game.lastRot == s && game.phase != PHGame.PH_LOBBY)
            {
                Frame();
                cam.targetTexture = shots[s];
                cam.enabled = true; camOn = true;
                // the map's fog would paint the whole photo in its colour (the camera stands far off): pushed out for
                // this one frame, then put back (above)
                fogStartWas = RenderSettings.fogStartDistance; fogEndWas = RenderSettings.fogEndDistance;
                RenderSettings.fogStartDistance = 4000f; RenderSettings.fogEndDistance = 5000f;
                shotSeed[s] = game.rotSeed[s]; shotSize[s] = game.rotSize[s];
            }
        }
        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + 0.3f;
        Refresh();
    }

    // the camera over the map: looking down at an angle, the whole site (and its roofs) in the picture
    void Frame()
    {
        float w = builder.SiteWidth(), d = builder.SiteDepth();
        Vector3 centre = builder.transform.position + new Vector3(w * 0.5f, 0f, d * 0.5f);
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 right = rot * Vector3.right, up = rot * Vector3.up;
        float halfW = 0f, halfH = 0f;
        for (int k = 0; k < 8; k++)
        {
            Vector3 c = new Vector3((k & 1) == 0 ? -w * 0.5f : w * 0.5f, (k & 4) == 0 ? 0f : 7f, (k & 2) == 0 ? -d * 0.5f : d * 0.5f);
            halfW = Mathf.Max(halfW, Mathf.Abs(Vector3.Dot(c, right)));
            halfH = Mathf.Max(halfH, Mathf.Abs(Vector3.Dot(c, up)));
        }
        float aspect = 4f / 3f;
        cam.orthographicSize = Mathf.Max(halfH, halfW / aspect) * 1.04f;
        // (no further off than the picture needs: less of the frame is at the mercy of fog and clip planes)
        float back = Mathf.Sqrt(w * w + d * d) * 0.5f + 30f;
        cam.transform.SetPositionAndRotation(centre + Vector3.up * 2f - (rot * Vector3.forward) * back, rot);
    }

    public bool HasPhoto(int slot) { return slot >= 0 && slot < shotSeed.Length && shotSeed[slot] != 0 && shotSeed[slot] == game.rotSeed[slot] && shotSize[slot] == game.rotSize[slot]; }

    void Refresh()
    {
        int cards = cardFace.Length;
        if (shownSlot == null)
        {
            shownSlot = new int[cards]; shownRuns = new int[cards]; shownPhoto = new bool[cards]; shownPick = new bool[cards]; shownSeed = new uint[cards]; shownBonus = new bool[cards];
            for (int i = 0; i < cards; i++) shownSlot[i] = -99;
        }
        int n = game.TrailOrder(order);
        int pick = game.replayPick;
        for (int i = 0; i < cards; i++)
        {
            int slot = i < 12 ? (i < n ? order[i] : -1) : (pick >= 0 && pick < 12 && game.rotSeed[pick] != 0 ? pick : -1);
            uint seed = slot >= 0 ? game.rotSeed[slot] : 0u;
            int runs = slot >= 0 ? game.rotRuns[slot] : 0;
            bool photo = HasPhoto(slot);
            bool picked = slot >= 0 && slot == pick && i < 12;
            bool bonus = slot >= 0 && game.phase == PHGame.PH_LOBBY && game.SlotBonus(slot) > 0;
            if (slot == shownSlot[i] && seed == shownSeed[i] && runs == shownRuns[i] && photo == shownPhoto[i] && picked == shownPick[i] && bonus == shownBonus[i]) continue;
            shownSlot[i] = slot; shownSeed[i] = seed; shownRuns[i] = runs; shownPhoto[i] = photo; shownPick[i] = picked; shownBonus[i] = bonus;
            bool on = slot >= 0;
            if (cardFace[i].activeSelf != on) cardFace[i].SetActive(on);
            if (cardEmpty[i] != null && cardEmpty[i].activeSelf == on) cardEmpty[i].SetActive(!on);
            if (!on) continue;
            int size = Mathf.Clamp(game.rotSize[slot], 0, 4);   // (4: XS)
            cardSize[i].text = sizeCap[size];
            cardRuns[i].text = "x" + Mathf.Max(1, runs);
            if (cardBonus != null && i < cardBonus.Length && cardBonus[i] != null && cardBonus[i].activeSelf != bonus) cardBonus[i].SetActive(bonus);
            if (cardPhoto[i].gameObject.activeSelf != photo) cardPhoto[i].gameObject.SetActive(photo);
            if (cardBlank[i].activeSelf == photo) cardBlank[i].SetActive(!photo);
            if (photo) cardPhoto[i].texture = shots[slot];
            else cardBlankIcon[i].sprite = sizeIcon[size < sizeIcon.Length ? size : 0];   // (XS: S's picture, if it has none)
            if (cardMarker[i] != null)
            {
                if (cardMarker[i].activeSelf != picked) cardMarker[i].SetActive(picked);
                // the picked card is lifted a little and tipped, like a card pulled half out of the row
                cardRect[i].localRotation = Quaternion.Euler(0f, 0f, picked ? 2.5f : 0f);
                Vector2 p = cardRect[i].anchoredPosition; p.y = picked ? 8f : 0f; cardRect[i].anchoredPosition = p;
            }
        }
    }
}
