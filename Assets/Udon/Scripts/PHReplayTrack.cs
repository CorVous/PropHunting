using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// One slot's recording of the last round, for the diorama's replay (PHReplay; Cor: "I'd like it to be synced"). The
// slot's player records their own path on their own client (their true place: the prop where it really is, their own
// feet), and at the END reveal sends it here once; everyone gets the same copy, a late joiner too. Its own object, not
// on PHSlot: PHSlot is sent many times a second and would carry the recording every time.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PHReplayTrack : UdonSharpBehaviour
{
    public PHReplay replay;
    public int index;                      // the slot
    [UdonSynced] public int round = -1;    // the round recorded
    [UdonSynced] public string who = "";   // their name (the track's owner changes if they leave)
    [UdonSynced] public byte[] data = new byte[0];   // PHReplay's encoding (header, then a sample every 0.25 s)

    public void Publish(int r, byte[] bytes, string name)
    {
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        round = r; data = bytes; who = name;
        RequestSerialization();
        if (replay != null) replay.TrackArrived(index);
    }

    public override void OnDeserialization() { if (replay != null) replay.TrackArrived(index); }
}
