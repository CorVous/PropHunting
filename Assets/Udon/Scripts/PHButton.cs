using UdonSharp;
using UnityEngine;

// A pressable thing in the world (Interact: click on desktop, trigger in VR) that calls one of PHGame's public
// methods by name. The game itself checks who may use it (the host's settings and START are host only).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHButton : UdonSharpBehaviour
{
    public PHGame game;
    public string action = "StartRound";

    public override void Interact()
    {
        if (game != null) game.SendCustomEvent(action);
    }
}
