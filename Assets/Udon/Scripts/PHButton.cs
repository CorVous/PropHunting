using UdonSharp;
using VRC.SDKBase;
using UnityEngine;
using VRC.Dynamics;

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
    public PHPress fx;                  // the button going down when hit (Cor), if it has one

    // from a UI button's OnClick (the host panel)
    public void Press() { Interact(); }

    // a hand pressing it (a VRC Contact Receiver on this object, the player's own fingers only): once per touch,
    // and not again for a moment (a hand resting on it, or brushing past twice, presses once)
    float lastTouch = -9f;
    public override void OnContactEnter(ContactEnterInfo contactInfo)
    {
        // only this player's own hand (Cor's playtest: the weapon wall's arrows turned for everyone when anyone
        // pressed them, as each client's receiver also felt the other players' fingers)
        VRCPlayerApi who = contactInfo.contactSender.player;
        if (!Utilities.IsValid(who) || !who.isLocal) return;
        if (Time.time - lastTouch < 0.4f) { lastTouch = Time.time; return; }
        lastTouch = Time.time;
        Interact();
    }

    public override void Interact()
    {
        if (fx != null) fx.Press();
        if (target != null) { if (arg > 0) target.SetProgramVariable("buttonArg", arg); target.SendCustomEvent(action); return; }   // (a target that takes a number has buttonArg: the diorama's name tags)
        if (game == null) return;
        game.panelArg = arg;
        game.SendCustomEvent(action);
    }
}
