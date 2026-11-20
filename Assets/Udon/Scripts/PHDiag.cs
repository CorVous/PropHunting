using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// The game log on the back of the lobby's debug sign (Cor: testers can copy it instead of digging up VRChat's
// output_log): the game's diagnostic lines (phase changes, stations sat in and left, seat and leak checks) as they
// happen on this client, the newest at the bottom. Clicking the field brings up VRChat's text box with the whole log,
// ready to copy; whatever is typed is undone (PHCopyLink's way). Local only, nothing synced. PHGame.Note feeds it.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHDiag : UdonSharpBehaviour
{
    public TMP_InputField field;
    const int MAX = 60;
    string[] lines = new string[MAX];
    int head, count;
    string text = "";

    public void Add(string s)
    {
        if (count == 0)
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            Put("PropHunt log, " + (Utilities.IsValid(me) ? me.displayName + (me.IsUserInVR() ? " (VR)" : " (desktop)") : "?"));
        }
        Put(s);
        string all = "";
        for (int i = 0; i < count; i++) all += lines[(head - count + i + MAX) % MAX] + "\n";
        text = all;
        if (field != null) field.text = text;
    }

    void Put(string s)
    {
        int t = Mathf.FloorToInt(Time.time);
        lines[head] = (t / 60).ToString("00") + ":" + (t % 60).ToString("00") + " " + s;
        head = (head + 1) % MAX;
        if (count < MAX) count++;
    }

    // from the field's OnEndEdit and OnValueChanged
    public void Restore()
    {
        if (field != null && field.text != text) field.text = text;
    }
}
