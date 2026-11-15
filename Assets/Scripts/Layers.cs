using UnityEngine;

// Physics layers by index (the VRChat template's names are not used). The player has no collider of its own:
// movement is done with casts, so walking can never push anything (D12).
public static class Layers
{
    public const int Arch = 11;         // walls, floors, roofs, fences, tree trunks
    public const int Solid = 8;         // solid furniture: blocks players and prop bodies
    public const int Prop = 13;         // small loose props: darts hit them, movement passes through
    public const int Door = 14;         // doors and lids of openables
    public const int Hider = 22;        // disguise hitboxes of non-solid types
    public const int HiderSolid = 23;   // disguise hitboxes of solid types (block while pinned)
    public const int Tumble = 24;       // knocked props while they fly: nothing blocks on them
    public const int Body = 2;          // helper colliders of the casts' own bodies (Ignore Raycast)

    public const int MoveMask = (1 << Arch) | (1 << Solid) | (1 << Door) | (1 << HiderSolid);
    public const int DartMask = (1 << Arch) | (1 << Solid) | (1 << Prop) | (1 << Door) | (1 << Hider) | (1 << HiderSolid) | (1 << Tumble);
    public const int AimMask = DartMask;
    public const int CameraMask = (1 << Arch) | (1 << Solid) | (1 << Door);

    public static void Setup()
    {
        int[] all = { Arch, Solid, Prop, Door, Hider, HiderSolid, Tumble };
        // knocked props collide with the world and with resting props; resting props never collide with each other
        for (int i = 0; i < all.Length; i++)
            for (int k = 0; k < all.Length; k++)
                Physics.IgnoreLayerCollision(all[i], all[k], !(all[i] == Tumble || all[k] == Tumble));
        for (int i = 0; i < all.Length; i++) Physics.IgnoreLayerCollision(Body, all[i], true);
    }
}
