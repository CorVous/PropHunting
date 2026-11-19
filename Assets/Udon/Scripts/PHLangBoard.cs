using UdonSharp;
using UnityEngine;

// The lobby's language board (Board Game style): a globe, then one tile per language with its own name, the
// player's pick under the marker meeple, and an "auto" tile (VRChat's language) with the dashed ring when the pick
// follows VRChat. Local only: each player sees their own pick.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHLangBoard : UdonSharpBehaviour
{
    public PHText text;
    public PHAudio sound;
    public RectTransform[] tiles;      // one per language, same order as PHText.langCode
    public RectTransform autoTile;
    public RectTransform marker;       // the meeple that sits on the picked tile
    public GameObject autoRing;        // dashed ring on the language VRChat picked, shown while following VRChat

    void Start() { if (text != null) text.Register(this); }

    public void Clicked() { if (sound != null) sound.Play("click_001", 0.5f); }

    public void _OnTextLanguage()
    {
        if (text == null || tiles == null) return;
        int l = text.lang;
        if (l < 0 || l >= tiles.Length || tiles[l] == null) return;
        if (marker != null)
        {
            RectTransform on = text.picked || autoTile == null ? tiles[l] : autoTile;
            marker.SetParent(on, false);
            marker.anchoredPosition = new Vector2(on.sizeDelta.x * 0.5f - 12f, on.sizeDelta.y * 0.5f - 6f);
        }
        if (autoRing != null)
        {
            autoRing.SetActive(!text.picked);
            var rt = (RectTransform)autoRing.transform;
            rt.SetParent(tiles[l], false);
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
