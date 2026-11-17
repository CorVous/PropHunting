using UdonSharp;
using UnityEngine;
using VRCLightVolumes;

// The map's lamps (Cor: VRC Light Volumes). A pool of Point Light Volumes is put on the map builder's light points
// once a map stands (MapBuilderUdon.lightPos: ceiling lights, porch lanterns, street lamps, canopy panels, lamp props),
// spread out over the map when there are more points than lights. Brighter in the evening and under an overcast sky,
// a cool white on a space base, warm everywhere else. PropHunt/Lit adds their light on top of the sun.
// Every client places its own; nothing is synced.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHLamps : UdonSharpBehaviour
{
    public MapBuilderUdon builder;
    public PointLightVolumeInstance[] pool;
    public int questCap = 12;              // a lighter pool on Android
    public float baseIntensity = 26f;
    public float sourceSize = 0.25f;       // metres: Light Volumes' falloff is size^2 / (d^2 + size^2)
    public float followSeconds = 0.5f;     // how often a knocked-over lamp prop's light catches up

    bool wasBuilt;
    int used;
    int[] pointOf;                          // pool slot -> light point index
    float followTimer;

    void Start()
    {
        for (int i = 0; i < pool.Length; i++) if (pool[i] != null) pool[i].gameObject.SetActive(false);
    }

    void Update()
    {
        if (builder == null || pool == null) return;
        bool b = builder.built;
        if (b && !wasBuilt) SendCustomEventDelayedFrames(nameof(Place), 3);
        if (!b && wasBuilt) Off();
        wasBuilt = b;
        if (!b || used == 0) return;
        followTimer += Time.deltaTime;
        if (followTimer < followSeconds) return;
        followTimer = 0f;
        // lamp props can be knocked over: their light follows them
        Transform[] follow = builder.lightFollow;
        Vector3[] pos = builder.lightPos;
        for (int s = 0; s < used; s++)
        {
            int p = pointOf[s];
            if (p < 0 || p >= builder.nLights || follow[p] == null) continue;
            Transform t = pool[s].transform;
            Vector3 w = follow[p].TransformPoint(pos[p]);
            if ((t.position - w).sqrMagnitude < 0.0004f) continue;
            t.position = w;
            pool[s].UpdateTransform();
        }
    }

    public void Off()
    {
        for (int i = 0; i < pool.Length; i++) if (pool[i] != null) pool[i].gameObject.SetActive(false);
        used = 0;
    }

    Vector3 PointWorld(int p)
    {
        Transform f = builder.lightFollow[p];
        return f != null ? f.TransformPoint(builder.lightPos[p]) : builder.lightPos[p];
    }

    public void Place()
    {
        Off();
        if (!builder.built) return;
        int n = builder.nLights;
        int cap = pool.Length;
#if UNITY_ANDROID
        if (questCap < cap) cap = questCap;
#endif
        if (n == 0 || cap == 0) return;
        if (pointOf == null || pointOf.Length < pool.Length) pointOf = new int[pool.Length];
        // spread the lights: the first point, then each time the point farthest from every one chosen so far
        Vector3[] w = new Vector3[n];
        for (int p = 0; p < n; p++) w[p] = PointWorld(p);
        float[] dist = new float[n];
        for (int p = 0; p < n; p++) dist[p] = float.MaxValue;
        int pick = 0;
        int count = n < cap ? n : cap;
        for (int s = 0; s < count; s++)
        {
            pointOf[s] = pick;
            float best = -1f; int next = -1;
            for (int p = 0; p < n; p++)
            {
                float d = (w[p] - w[pick]).sqrMagnitude;
                if (d < dist[p]) dist[p] = d;
                if (dist[p] > best) { best = dist[p]; next = p; }
            }
            if (next < 0 || best <= 0f) { count = s + 1; break; }
            pick = next;
        }
        // the mood: how much the lamps matter against the daylight
        int mood = builder.moodTime;
        float k = mood == 3 ? 1.0f : (mood == 4 ? 0.8f : (mood == 0 ? 0.6f : 0.4f));
        Color col = builder.gen.mapTheme == GenData.THEME_SPACE ? new Color(0.80f, 0.90f, 1f) : new Color(1f, 0.84f, 0.62f);
        for (int s = 0; s < count; s++)
        {
            PointLightVolumeInstance l = pool[s];
            if (l == null) continue;
            int p = pointOf[s];
            // a street lamp reaches further than a lamp on a table
            int kind = builder.lightKind[p];
            float scale = kind == MapBuilderUdon.LK_STREET ? 1.6f : (kind == MapBuilderUdon.LK_LAMP ? 0.6f : 1f);
            l.transform.position = w[p];
            l.gameObject.SetActive(true);
            l.SetPointLight();
            l.SetLightSourceSize(sourceSize);
            l.SetColorAndIntensity(col, baseIntensity * k * scale);
            l.UpdateTransform();
        }
        used = count;
    }
}
