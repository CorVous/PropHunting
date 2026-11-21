using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// The lobby's audio board (Cor), on the north wall over the how-to-play panels, Board Game style: one row per kind
// of sound (PHAudio.C_*), each a speaker tile (mute) and a track of ten spaces, filled up to the player's level.
// Local only: each player sees and sets their own (PHAudio keeps and saves them).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHAudioBoard : UdonSharpBehaviour
{
    public PHAudio sound;
    public Image[] spaces;          // KINDS * STEPS, row by row
    public Image[] speakers;        // one per row
    public Sprite speakerOn, speakerOff;
    public Color[] fill;            // each row's colour
    public Color empty, mutedFill;

    void Start() { Show(); }

    public void Show()
    {
        if (sound == null || spaces == null || sound.level == null) return;
        int n = PHAudio.KINDS, steps = PHAudio.STEPS;
        for (int k = 0; k < n; k++)
        {
            bool m = sound.muted[k];
            int lv = sound.level[k];
            Color c = fill != null && k < fill.Length ? fill[k] : Color.white;
            for (int s = 0; s < steps; s++)
            {
                int i = k * steps + s;
                if (i >= spaces.Length || spaces[i] == null) continue;
                spaces[i].color = s < lv ? (m ? mutedFill : c) : empty;
            }
            if (speakers != null && k < speakers.Length && speakers[k] != null) speakers[k].sprite = m ? speakerOff : speakerOn;
        }
    }
}
