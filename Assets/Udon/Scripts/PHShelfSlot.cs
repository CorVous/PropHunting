using UdonSharp;
using UnityEngine;

// One place on a weapon-wall shelf (PHShelf): a click (desktop) or the trigger (VR) on it picks, buys or explains
// the weapon standing there.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHShelfSlot : UdonSharpBehaviour
{
    public PHShelf shelf;
    public int index;   // row root * 10 + column

    public override void Interact() { if (shelf != null) shelf.SlotClicked(index); }
}
