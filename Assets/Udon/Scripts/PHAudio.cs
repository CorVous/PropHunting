using UdonSharp;
using UnityEngine;

// The sounds (the prototype's AudioHub): every clip of Assets/Udon/Audio by name (filled by the scene builder),
// a 2D source for the local player's own cues, and a small pool of 3D sources for sounds in the world.
// Nothing here is networked: callers play a sound where every client already knows it happens (a synced knock,
// a taunt event, a phase change).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHAudio : UdonSharpBehaviour
{
    public string[] names;
    public AudioClip[] clips;
    public AudioSource ui;
    public AudioSource[] pool;
    public float master = 0.8f;

    public readonly string[] taunts = { "jingles_SAX03", "jingles_SAX07", "jingles_SAX10", "pepSound1", "pepSound2", "pepSound3", "pepSound4",
        "pepSound5", "threeTone1", "lowRandom", "phaseJump1", "zapThreeToneUp" };
    public readonly string[] woodKnock = { "impactWood_medium_000", "impactWood_medium_001", "impactWood_medium_002", "impactWood_light_000" };
    public readonly string[] softKnock = { "impactSoft_medium_000", "impactSoft_medium_001" };
    public readonly string[] thud = { "impactGeneric_light_000", "impactGeneric_light_001" };

    int next;

    public AudioClip Clip(string n)
    {
        if (names == null || clips == null) return null;
        // (names and clips filled apart: one longer than the other must not halt the sounds)
        int n0 = Mathf.Min(names.Length, clips.Length);
        for (int i = 0; i < n0; i++) if (names[i] == n) return clips[i];
        return null;
    }

    // any int, even a negative one from the network (-int.MinValue is still negative); an empty set picks nothing
    public string Pick(string[] set, int r)
    {
        if (set == null || set.Length == 0) return "";
        int k = r % set.Length;
        return set[k < 0 ? k + set.Length : k];
    }

    // the local player's own cue, not placed in the world
    public void Play(string n, float volume)
    {
        AudioClip c = Clip(n);
        if (c != null && ui != null) ui.PlayOneShot(c, volume * master);
    }

    public void PlayAt(string n, Vector3 pos, float range, float volume, float pitch)
    {
        AudioClip c = Clip(n);
        if (c == null || pool == null || pool.Length == 0) return;
        if (next >= pool.Length) next = 0;
        AudioSource s = pool[next];
        next = (next + 1) % pool.Length;
        if (s == null) return;
        s.transform.position = pos;
        s.maxDistance = range;
        s.pitch = pitch;
        s.clip = c;
        s.volume = volume * master;
        s.Play();
    }
}
