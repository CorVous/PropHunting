using UnityEngine;

// Physics layers by index (the VRChat template's names are not used). The player has no collider of its own:
// movement is done with casts, so walking can never push anything (D12).
public static class Layers
{
    public const int Arch = 11;         // walls, floors, roofs, fences
    public const int Solid = 8;         // furniture: blocks players and prop bodies, the third-person camera and bots' sight
    public const int Prop = 13;         // small props: block players and prop bodies too (G26), but not the camera or sight
    public const int Door = 14;         // doors and lids of openables
    public const int Hider = 22;        // a disguise while it moves, and the player's own hitbox: darts hit it, nothing walks into it
    public const int HiderSolid = 23;   // a bot's disguise standing still: as solid as the real prop it copies
    public const int Tumble = 24;       // knocked props while they fly: nothing blocks on them
    public const int Body = 2;          // helper colliders of the casts' own bodies (Ignore Raycast)

    // Every prop is solid, small ones included (G26), to prop bodies (G45 lets walking bodies through). Walking still
    // never moves one (D12): bodies are moved by casts and push nothing. Low props are stepped onto within the
    // body's step height (BoxBody).
    public const int MoveMask = (1 << Arch) | (1 << Solid) | (1 << Prop) | (1 << Door) | (1 << HiderSolid);
    // G45: a walking body (a hunter, the dead, explore and the lobby) is not blocked by props, furniture, furniture
    // doors, trees or a still bot hider; walls, fences, roofs and the ground still stop it.
    // Prop bodies keep MoveMask; darts and aim keep every prop.
    public const int WalkerMask = 1 << Arch;
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
