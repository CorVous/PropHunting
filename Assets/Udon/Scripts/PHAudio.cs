using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

// The sounds (the prototype's AudioHub): every clip of Assets/Udon/Audio by name (filled by the scene builder),
// a 2D source for the local player's own cues, and a small pool of 3D sources for sounds in the world.
// Nothing here is networked: callers play a sound where every client already knows it happens (a synced knock,
// a taunt event, a phase change).
//
// The audio board (Cor): each player's own volume per kind of sound, 1 to 10 steps or muted, kept in PlayerData
// ("ph.audio": one byte per kind, its level, +128 when muted) and applied when it is restored on join. Every clip
// has its kind (cats, filled by the scene builder from its name or folder); the other sources the game plays
// through (the host panel's and the explore button's clicks, the hiders' taunt voices) are listed in extra with
// their kind and their own volume. Player voices and avatar sounds are scaled in PHLocal.UpdateVoices (Gain, ver).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHAudio : UdonSharpBehaviour
{
    public string[] names;
    public AudioClip[] clips;
    public int[] cats;                 // each clip's kind (C_*), same order as names
    public AudioSource ui;
    public AudioSource[] pool;
    public float master = 0.8f;        // the mix's own level (not the player's)
    public AudioSource[] extra;        // other sources with a kind: their volume = extraBase * Gain(extraCat)
    public int[] extraCat;
    public float[] extraBase;
    public PHAudioBoard board;

    // the kinds, in the order of the board's rows and of the saved bytes
    public const int C_MASTER = 0, C_ANNOUNCER = 1, C_EFFECTS = 2, C_BUTTONS = 3, C_JINGLES = 4, C_TAUNTS = 5, C_VOICES = 6, C_AVATARS = 7;
    public const int KINDS = 8, STEPS = 10;
    public const string SAVE_KEY = "ph.audio";

    [System.NonSerialized] public int[] level;      // 1..STEPS
    [System.NonSerialized] public bool[] muted;
    [System.NonSerialized] public int ver;          // goes up on every change (PHLocal re-sets the voices)

    public readonly string[] taunts = { "jingles_SAX03", "jingles_SAX07", "jingles_SAX10", "pepSound1", "pepSound2", "pepSound3", "pepSound4",
        "pepSound5", "threeTone1", "lowRandom", "phaseJump1", "zapThreeToneUp" };
    public readonly string[] woodKnock = { "impactWood_medium_000", "impactWood_medium_001", "impactWood_medium_002", "impactWood_light_000" };
    public readonly string[] softKnock = { "impactSoft_medium_000", "impactSoft_medium_001" };
    public readonly string[] thud = { "impactGeneric_light_000", "impactGeneric_light_001" };
    // what a row plays when its level changes, so the player hears it ("" = nothing of its own: a click)
    readonly string[] sample = { "confirmation_001", "ready", "impactWood_medium_000", "click_001", "jingles_PIZZI07", "pepSound1", "", "" };

    int next;

    void Start() { Init(); Apply(); }

    void Init()
    {
        if (level != null) return;
        level = new int[KINDS]; muted = new bool[KINDS];
        for (int i = 0; i < KINDS; i++) level[i] = STEPS;
    }

    // ------------------------------------------------------------------ the player's levels
    // a kind's share of its sounds' volume: the master's and its own, each step squared (finer steps when quiet)
    public float Gain(int cat)
    {
        Init();
        float g = Step(C_MASTER);
        if (cat > C_MASTER && cat < KINDS) g *= Step(cat);
        return g;
    }

    float Step(int k)
    {
        if (muted[k]) return 0f;
        float f = level[k] / (float)STEPS;
        return f * f;
    }

    int CatOf(int clip) { return cats != null && clip < cats.Length ? cats[clip] : C_EFFECTS; }

    public void SetLevel(int k, int v)
    {
        Init();
        if (k < 0 || k >= KINDS) return;
        level[k] = Mathf.Clamp(v, 1, STEPS);
        muted[k] = false;
        Changed(k);
    }

    public void ToggleMute(int k)
    {
        Init();
        if (k < 0 || k >= KINDS) return;
        muted[k] = !muted[k];
        Changed(muted[k] ? -1 : k);
    }

    public void ResetAll()
    {
        Init();
        for (int i = 0; i < KINDS; i++) { level[i] = STEPS; muted[i] = false; }
        Changed(C_MASTER);
    }

    void Changed(int heard)
    {
        Save();
        Apply();
        // a taste of the row's sounds at the new level (a muted row: the click alone, at the buttons' level)
        string n = heard >= 0 ? sample[heard] : "";
        if (n == "") PlayKind("click_001", 0.5f, C_BUTTONS);
        else PlayKind(n, 0.8f, heard);
    }

    void Save()
    {
        byte[] b = new byte[KINDS];
        for (int i = 0; i < KINDS; i++) b[i] = (byte)(level[i] + (muted[i] ? 128 : 0));
        PlayerData.SetBytes(SAVE_KEY, b);
    }

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;
        Init();
        byte[] b;
        if (PlayerData.TryGetBytes(player, SAVE_KEY, out b) && b != null)
            for (int i = 0; i < KINDS && i < b.Length; i++)
            {
                int v = b[i];
                muted[i] = v >= 128;
                level[i] = Mathf.Clamp(v & 127, 1, STEPS);
            }
        Apply();
    }

    // the levels onto the sources that keep a volume of their own, the board, and the voices (ver)
    public void Apply()
    {
        Init();
        ver++;
        if (extra != null)
            for (int i = 0; i < extra.Length; i++)
            {
                if (extra[i] == null) continue;
                int c = extraCat != null && i < extraCat.Length ? extraCat[i] : C_EFFECTS;
                float b = extraBase != null && i < extraBase.Length ? extraBase[i] : 1f;
                extra[i].volume = b * Gain(c);
            }
        if (board != null) board.Show();
    }

    // ------------------------------------------------------------------ playing
    public AudioClip Clip(string n)
    {
        int i = IndexOf(n);
        return i >= 0 ? clips[i] : null;
    }

    int IndexOf(string n)
    {
        if (names == null || clips == null) return -1;
        // (names and clips filled apart: one longer than the other must not halt the sounds)
        int n0 = Mathf.Min(names.Length, clips.Length);
        for (int i = 0; i < n0; i++) if (names[i] == n) return i;
        return -1;
    }

    // any int, even a negative one from the network (-int.MinValue is still negative); an empty set picks nothing
    public string Pick(string[] set, int r)
    {
        if (set == null || set.Length == 0) return "";
        int k = r % set.Length;
        return set[k < 0 ? k + set.Length : k];
    }

    // one of Cor's recorded takes for a cue (Audio/Voice/vo_<cue>_<line>_<n>), picked at random; the fallback
    // clip when the cue has none
    public string Voice(string cue, string fallback)
    {
        if (names == null || clips == null) return fallback;
        string p = "vo_" + cue + "_";
        int n0 = Mathf.Min(names.Length, clips.Length), n = 0;
        for (int i = 0; i < n0; i++) if (names[i] != null && names[i].StartsWith(p)) n++;
        if (n == 0) return fallback;
        int k = Random.Range(0, n);
        for (int i = 0; i < n0; i++)
        {
            if (names[i] == null || !names[i].StartsWith(p)) continue;
            if (k == 0) return names[i];
            k--;
        }
        return fallback;
    }

    // the local player's own cue, not placed in the world
    public void Play(string n, float volume)
    {
        int i = IndexOf(n);
        if (i < 0 || clips[i] == null || ui == null) return;
        float v = volume * master * Gain(CatOf(i));
        if (v > 0f) ui.PlayOneShot(clips[i], v);
    }

    // the same, as another kind than its clip's (the board's taste of a row)
    void PlayKind(string n, float volume, int cat)
    {
        int i = IndexOf(n);
        if (i < 0 || clips[i] == null || ui == null) return;
        float v = volume * master * Gain(cat);
        if (v > 0f) ui.PlayOneShot(clips[i], v);
    }

    public void PlayAt(string n, Vector3 pos, float range, float volume, float pitch)
    {
        int i = IndexOf(n);
        if (i < 0 || clips[i] == null || pool == null || pool.Length == 0) return;
        float v = volume * master * Gain(CatOf(i));
        if (v <= 0f) return;
        if (next >= pool.Length) next = 0;
        AudioSource s = pool[next];
        next = (next + 1) % pool.Length;
        if (s == null) return;
        s.transform.position = pos;
        s.maxDistance = range;
        s.pitch = pitch;
        s.clip = clips[i];
        s.volume = v;
        s.Play();
    }
}
