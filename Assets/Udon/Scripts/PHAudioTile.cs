using UdonSharp;
using UnityEngine;

// One button of the audio board (a UI Button's OnClick calls Press): a space of a row's track (value 1..10 sets
// the level), the row's speaker (value 0: mute or unmute), or the reset tile (kind -1).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHAudioTile : UdonSharpBehaviour
{
    public PHAudio sound;
    public int kind;
    public int value;

    public void Press()
    {
        if (sound == null) return;
        if (kind < 0) sound.ResetAll();
        else if (value <= 0) sound.ToggleMute(kind);
        else sound.SetLevel(kind, value);
    }
}
