using System.Runtime.InteropServices;
using UnityEngine;

// Sounds: Kenney's CC0 audio packs from the owner's mirror, as stand-ins (taunts in particular: the mirror has
// no taunt, ambience or music tracks, so taunts are jingles and synth sounds, and there is no music).
//
// The clips are the packs' own .ogg files in StreamingAssets, decoded and played by the browser through
// Assets/Plugins/WebGL/PropHuntAudio.jslib (see that file for why Unity's audio is not used). A positional
// sound gets its loudness and left/right pan from the listener's pose at the moment it starts.
public class AudioHub : MonoBehaviour
{
    public static AudioHub I;
    public float volume = Balance.MasterDefault;
    public int wanted;
    float reportAt = 6f;
    bool reported;

    public static readonly string[] Taunts = { "jingles_SAX03", "jingles_SAX07", "jingles_SAX10", "pepSound1", "pepSound2", "pepSound3", "pepSound4",
        "pepSound5", "threeTone1", "lowRandom", "phaseJump1", "zapThreeToneUp" };
    public static readonly string[] WoodKnock = { "impactWood_medium_000", "impactWood_medium_001", "impactWood_medium_002", "impactWood_light_000" };
    public static readonly string[] SoftKnock = { "impactSoft_medium_000", "impactSoft_medium_001" };
    public static readonly string[] Thud = { "impactGeneric_light_000", "impactGeneric_light_001" };

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void PH_AudioInit();
    [DllImport("__Internal")] static extern void PH_AudioLoad(string name, string url);
    [DllImport("__Internal")] static extern int PH_AudioPlay(string name, float gain, float pan, float rate);
    [DllImport("__Internal")] static extern void PH_AudioVolume(float v);
    [DllImport("__Internal")] static extern int PH_AudioLoaded();
    [DllImport("__Internal")] static extern int PH_AudioFailed();
#else
    static void PH_AudioInit() { }
    static void PH_AudioLoad(string name, string url) { }
    static int PH_AudioPlay(string name, float gain, float pan, float rate) { return 0; }
    static void PH_AudioVolume(float v) { }
    static int PH_AudioLoaded() { return 0; }
    static int PH_AudioFailed() { return 0; }
#endif

    void Awake()
    {
        I = this;
        PH_AudioInit();
        PH_AudioVolume(volume);
        var index = Resources.Load<TextAsset>("PropHunt/audio_index");
        if (index == null) { Debug.LogWarning("[prophunt] audio_index.txt is missing"); return; }
        foreach (string line in index.text.Split('\n'))
        {
            string n = line.Trim();
            if (n.Length == 0) continue;
            PH_AudioLoad(n, Application.streamingAssetsPath + "/PropHunt/Audio/" + n + ".ogg");
            wanted++;
        }
    }

    void Update()
    {
        if (reported || Time.realtimeSinceStartup < reportAt) return;
        int ok = PH_AudioLoaded(), bad = PH_AudioFailed();
        if (ok + bad >= wanted || Time.realtimeSinceStartup > 30f)
        {
            reported = true;
            Debug.Log("[prophunt] sounds loaded " + ok + " of " + wanted + (bad > 0 ? ", failed " + bad : ""));
        }
        else reportAt += 1f;
    }

    public int Loaded { get { return PH_AudioLoaded(); } }

    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        PH_AudioVolume(volume);
    }

    // flat, for interface sounds and the player's own cues
    public void Play(string name, float vol = 1f, float pitch = 1f)
    {
        PH_AudioPlay(name, vol, 0f, pitch);
    }

    // positional, heard up to `range` metres (a taunt: 20 m)
    public void PlayAt(string name, Vector3 pos, float range, float vol = 1f, float pitch = 1f)
    {
        Transform ear = Game.I.cam.transform;
        // during the reveal the listener stays with the player, not with the camera above the map
        Vector3 at = Game.I.player.active ? Game.I.player.EyePos : ear.position;
        Vector3 to = pos - at;
        float d = to.magnitude;
        if (d >= range) return;
        float gain = vol * Mathf.Pow(1f - d / range, 1.3f);
        float pan = d > 0.3f ? Mathf.Clamp(Vector3.Dot(to / d, ear.right), -1f, 1f) * 0.85f : 0f;
        PH_AudioPlay(name, gain, pan, pitch);
    }

    public static string Pick(string[] set)
    {
        return set[Random.Range(0, set.Length)];
    }

    // the sound a prop makes when a dart knocks it, by its rough material
    public static string KnockSound(int def)
    {
        int tags = GenData.defTags[def];
        if ((tags & (GenData.I_PILLOW | GenData.I_TOY | GenData.I_RUG | GenData.I_BEDROLL)) != 0) return Pick(SoftKnock);
        if ((tags & (GenData.I_TABLEWARE | GenData.I_SETTING)) != 0) return "impactPlate_light_000";
        if ((tags & (GenData.I_KITCHENWARE | GenData.I_TOOL)) != 0) return "impactMetal_light_000";
        if ((tags & GenData.I_FOOD) != 0) return "impactGlass_light_000";
        return Pick(WoodKnock);
    }
}
