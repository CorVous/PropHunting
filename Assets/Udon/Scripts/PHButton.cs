using UdonSharp;
using UnityEngine;

// A pressable thing in the world (Interact: click on desktop, trigger in VR) that calls one of PHGame's public
// methods by name, with an optional number (PHGame.panelArg). The game itself checks who may use it (the host's
// settings and START are host only), so everyone can press, and only the host's presses count.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHButton : UdonSharpBehaviour
{
    public PHGame game;
    public UdonSharpBehaviour target;   // if set, the action is this script's (the profiler's buttons), not the game's
    public string action = "StartRound";
    public int arg;

    // from a UI button's OnClick (the host panel)
    public void Press() { Interact(); }

    public override void Interact()
    {
        if (target != null) { target.SendCustomEvent(action); return; }
        if (game == null) return;
        game.panelArg = arg;
        game.SendCustomEvent(action);
    }
}
