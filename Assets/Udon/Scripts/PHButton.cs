using UdonSharp;
using UnityEngine;

// A pressable thing in the world (Interact: click on desktop, trigger in VR) that calls one of PHGame's public
// methods by name, with an optional number (PHGame.panelArg). The game itself checks who may use it (the host's
// settings and START are host only), so everyone can press, and only the host's presses count.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHButton : UdonSharpBehaviour
{
    public PHGame game;
    public string action = "StartRound";
    public int arg;

    public override void Interact()
    {
        if (game == null) return;
        game.panelArg = arg;
        game.SendCustomEvent(action);
    }
}
