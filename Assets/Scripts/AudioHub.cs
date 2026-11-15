using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

// Sounds: Kenney's CC0 audio packs from the owner's mirror, as stand-ins (taunts in particular: the mirror has
// no taunt, ambience or music tracks, so taunts are jingles and synth sounds, and there is no music).
public class AudioHub : MonoBehaviour
{
    public static AudioHub I;
    public float volume = Balance.MasterDefault;
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    readonly List<AudioSource> pool = new List<AudioSource>();
    AudioSource ui;
    int next;

    public static readonly string[] Taunts = { "jingles_SAX03", "jingles_SAX07", "jingles_SAX10", "pepSound1", "pepSound2", "pepSound3", "pepSound4",
        "pepSound5", "threeTone1", "lowRandom", "phaseJump1", "zapThreeToneUp" };
    public static readonly string[] WoodKnock = { "impactWood_medium_000", "impactWood_medium_001", "impactWood_medium_002", "impactWood_light_000" };
    public static readonly string[] SoftKnock = { "impactSoft_medium_000", "impactSoft_medium_001" };
    public static readonly string[] Thud = { "impactGeneric_light_000", "impactGeneric_light_001" };

    void Awake()
    {
        I = this;
        ui = gameObject.AddComponent<AudioSource>();
        ui.spatialBlend = 0f; ui.playOnAwake = false;
        for (int i = 0; i < 12; i++)
        {
            var go = new GameObject("Sound " + i);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear; s.dopplerLevel = 0f;
            pool.Add(s);
        }
        AudioListener.volume = volume;
    }

    // The clips are the packs' own .ogg files in StreamingAssets, decoded by the browser at run time
    // (the Linux editor cannot encode audio for WebGL without ffmpeg, so they are not imported assets).
    public int loaded, failed, wanted;

    void Start()
    {
        var index = Resources.Load<TextAsset>("PropHunt/audio_index");
        if (index == null) { Debug.LogWarning("[prophunt] audio_index.txt is missing"); return; }
        string[] names = index.text.Split('\n');
        var queue = new Queue<string>();
        foreach (string n in names) if (n.Trim().Length > 0) queue.Enqueue(n.Trim());
        wanted = queue.Count;
        for (int i = 0; i < 4; i++) StartCoroutine(LoadWorker(queue));
    }

    IEnumerator LoadWorker(Queue<string> queue)
    {
        while (queue.Count > 0)
        {
            string name = queue.Dequeue();
            string url = Application.streamingAssetsPath + "/PropHunt/Audio/" + name + ".ogg";
            using (var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.OGGVORBIS))
            {
                yield return req.SendWebRequest();
                AudioClip c = null;
                if (req.result == UnityWebRequest.Result.Success) c = DownloadHandlerAudioClip.GetContent(req);
                if (c != null) { c.name = name; clips[name] = c; loaded++; }
                else { failed++; Debug.LogWarning("[prophunt] sound " + name + " did not load: " + req.error); }
            }
            if (loaded + failed == wanted) Debug.Log("[prophunt] sounds loaded " + loaded + " of " + wanted);
        }
    }

    AudioClip Clip(string name)
    {
        AudioClip c;
        return clips.TryGetValue(name, out c) ? c : null;
    }

    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        AudioListener.volume = volume;
    }

    // flat, for interface sounds and the player's own cues
    public void Play(string name, float vol = 1f, float pitch = 1f)
    {
        var c = Clip(name);
        if (c == null) return;
        ui.pitch = pitch;
        ui.PlayOneShot(c, vol);
    }

    // positional, heard up to `range` metres (a taunt: 20 m)
    public void PlayAt(string name, Vector3 pos, float range, float vol = 1f, float pitch = 1f)
    {
        var c = Clip(name);
        if (c == null) return;
        var s = pool[next];
        next = (next + 1) % pool.Count;
        s.transform.position = pos;
        s.clip = c; s.volume = vol; s.pitch = pitch;
        s.minDistance = Mathf.Min(2f, range * 0.2f); s.maxDistance = range;
        s.Play();
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
