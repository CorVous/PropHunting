using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Debug: generate a fresh map with a random seed (the panel's size and props settings) and walk around it, outside
// any round. Only the presser's client builds it and only they are teleported there; nothing is synced. Use VRChat's
// Respawn to come back to the lobby. A real round's START rebuilds its own map as usual.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHExplore : UdonSharpBehaviour
{
    public PHGame game;
    public TextMeshProUGUI label;
    public AudioSource sfx;
    public AudioClip select, error, done;

    bool generating;

    public void Press()
    {
        MapBuilderUdon builder = game.builder;
        // (not while a round's map is still being made: back in the lobby early, PHGame goes on stepping the generator
        // and would build and report it over this one)
        if (generating || game.phase != PHGame.PH_LOBBY || builder.busy || game.building)
        {
            if (sfx != null && error != null) sfx.PlayOneShot(error);
            return;
        }
        if (sfx != null && select != null) sfx.PlayOneShot(select);
        generating = true;
        builder.Clear();
        // the doors' list pointed at the props just destroyed, and the round does not change in the lobby, so PHDoors
        // would keep it (IndexOf then reads the null objects array); with objects null this empties it
        if (game.local != null && game.local.doors != null) game.local.doors.ListDoors();
        uint seed = game.ThemedSeed();   // of the debug THEME button's theme, if one is chosen
        game.gen.Begin(seed, game.RoundSize(), game.densityChoice, 0);
        label.text = "GENERATING...";
    }

    void Update()
    {
        if (!generating) return;
        if (game.phase != PHGame.PH_LOBBY) { generating = false; label.text = "EXPLORE A NEW MAP"; return; }   // a round took over
        MapBuilderUdon builder = game.builder;
        MapGenUdon gen = game.gen;
        if (!builder.busy && !builder.built)
        {
            float t0 = Time.realtimeSinceStartup;
            bool finished = false;
            while (!finished && Time.realtimeSinceStartup - t0 < 0.006f) finished = gen.Step();
            if (finished)
            {
                if (gen.failed) { generating = false; label.text = "FAILED, TRY AGAIN"; return; }
                builder.BeginBuild();
                label.text = "BUILDING...";
            }
        }
        else if (builder.built)
        {
            generating = false;
            builder.ShowRoofs(true);
            if (game.local != null && game.local.doors != null) game.local.doors.ListDoors();   // openable props
            Vector3 at = builder.ReleasePoint();
            if (gen.nSpawns > 0)
            {
                int c = gen.spawnCell[Random.Range(0, gen.nSpawns)];
                at = builder.InnerWorld(c);   // upstairs or in a basement too
            }
            Networking.LocalPlayer.TeleportTo(at + Vector3.up * 0.1f, Quaternion.Euler(0f, Random.Range(0, 24) * 15f, 0f));
            if (sfx != null && done != null) sfx.PlayOneShot(done);
            label.text = "EXPLORE A NEW MAP";
        }
    }
}
