using UdonSharp;
using UnityEngine;

// One tile of the lobby's language board (a UI Button's OnClick calls Press). index -1 = "follow VRChat's language".
// The board shows the picked tile with the marker; PHText.Apply tells the board through _OnTextLanguage.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHLangTile : UdonSharpBehaviour
{
    public PHText text;
    public int index;
    public PHLangBoard board;

    public void Press()
    {
        if (text == null) return;
        text.Pick(index);
        if (board != null) board.Clicked();
    }
}
