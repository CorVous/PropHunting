using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

// The little card that pops up over a weapon on the weapon wall (Cor): for a shop weapon a plain "Confirm purchase?"
// and a buy button with the price (greyed when the player is short; no picture, the weapon is right there: Cor); for
// a trophy weapon the trophy that unlocks it and its goal. Local to the player; it closes on its X, after a purchase,
// when a shelf turns, or when they walk off. While it is open the shelves' slot colliders are off (hop playtest: in
// VR they caught the laser in front of the Buy button); every close turns them back on.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHStoreDialog : UdonSharpBehaviour
{
    public PHStore store;
    public GameObject root, buyGroup, trophyGroup;
    public TextMeshProUGUI priceText, nameText, goalText;
    public Image buyButton;
    public Color buyOn = new Color(0.847f, 0.278f, 0.227f), buyOff = new Color(0.486f, 0.455f, 0.396f);
    public const int ICON_COLS = 16, ICON_ROWS = 12;   // Assets/Lobby/WeaponIcons.png

    int cur = -1;
    bool open;

    void Start() { root.SetActive(false); }

    public void ShowBuy(int c, Vector3 at, Quaternion facing)
    {
        cur = c;
        Place(at, facing);
        buyGroup.SetActive(true); trophyGroup.SetActive(false);
        bool afford = store.coins >= store.wPrice[c];
        priceText.text = store.wPrice[c].ToString();
        buyButton.color = afford ? buyOn : buyOff;
    }

    public void ShowTrophy(int c, Vector3 at, Quaternion facing)
    {
        cur = c;
        int t = store.wTrophy[c];
        if (t < 0) return;
        Place(at, facing);
        buyGroup.SetActive(false); trophyGroup.SetActive(true);
        PHText tx = store.game != null ? store.game.text : null;   // the player's language (the English is a table row)
        nameText.text = tx != null ? tx.En(store.tName[t]) : store.tName[t];
        goalText.text = tx != null ? tx.En(store.tGoal[t]) : store.tGoal[t];
    }

    void Place(Vector3 at, Quaternion facing)
    {
        root.transform.SetPositionAndRotation(at, facing);
        root.SetActive(true);
        Slots(false);
        if (!open) { open = true; SendCustomEventDelayedSeconds(nameof(_Watch), 0.5f); }
    }

    // the buttons (PHButton -> these)
    public void BuyPress() { if (cur >= 0 && store.Buy(cur)) Close(); }
    public void Close() { root.SetActive(false); open = false; cur = -1; Slots(true); }

    void Slots(bool on)
    {
        if (store == null) return;
        if (store.shelfMelee != null) store.shelfMelee.SetSlots(on);
        if (store.shelfGun != null) store.shelfGun.SetSlots(on);
    }

    // closes once the player walks away from it
    public void _Watch()
    {
        if (!open) return;
        VRCPlayerApi p = Networking.LocalPlayer;
        if (p != null && Utilities.IsValid(p) && (p.GetPosition() - root.transform.position).sqrMagnitude > 25f) { Close(); return; }
        SendCustomEventDelayedSeconds(nameof(_Watch), 0.5f);
    }
}
