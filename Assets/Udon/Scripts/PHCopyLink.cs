using TMPro;
using UdonSharp;
using UnityEngine;

// A link on the credits board (LobbyBuilder.BuildCredits): a text field holding a web address. VRChat worlds cannot
// open a browser, so clicking the field brings up VRChat's text box with the address in it, ready to copy. The field
// is editable (so that text box opens); whatever is typed is undone when editing ends.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHCopyLink : UdonSharpBehaviour
{
    public TMP_InputField field;
    public string url;

    // from the field's OnEndEdit and OnValueChanged
    public void Restore()
    {
        if (field != null && field.text != url) field.text = url;
    }
}
