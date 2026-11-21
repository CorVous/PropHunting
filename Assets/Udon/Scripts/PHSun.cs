using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRCLightVolumes;

// The map's sun shadows (design D1, C3, G48): one VRC Light Volumes spot far above the map, aimed along the sun,
// whose shadows are baked in-game once after every map build. Only building fabric casts: the walls, the roofs and
// the boundary fences, moved to the caster layer for the bake. Props and trees cast nothing, because a hider can
// disguise as any of them (G46). PropHunt/Lit then takes the direct sunlight from the spot (per pixel, shadowed)
// instead of from its shadowless base; avatars with Light Volumes shaders get the same sun.
// Every client bakes its own copy after its own build; nothing is synced.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHSun : UdonSharpBehaviour
{
    public MapBuilderUdon builder;
    public PointLightVolumeInstance spot;
    public Light sun;                    // the scene's directional light: gives the direction and the colour
    public int casterLayer = 22;
    public float coneDeg = 24f;          // the spot's cone; its height follows the map's size
    public float margin = 8f;            // metres of shadow beyond the map's edge
    public float intensityScale = 0.45f; // calibrates the spot's sunlight against the base's (measured in Play mode)
    public int resolutionPc = 1024, resolutionQuest = 1024;

    bool wasBuilt;
    int globalId, areaId;

    void Start()
    {
        globalId = VRCShader.PropertyToID("_UdonPHSunSpot");
        areaId = VRCShader.PropertyToID("_UdonPHSunArea");
        VRCShader.SetGlobalFloat(globalId, 0f);
        if (spot != null) spot.gameObject.SetActive(false);
    }

    // profiling (PHProfiler): while it runs, the time this script's updates take adds up in profMs
    [System.NonSerialized] public System.Diagnostics.Stopwatch profWatch;
    [System.NonSerialized] public double profMs;

    void Update()
    {
        if (profWatch == null) { UpdateBody(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        UpdateBody();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    void UpdateBody()
    {
        if (builder == null || spot == null) return;
        bool b = builder.built;
        if (b && !wasBuilt) { bakeQueued = true; SendCustomEventDelayedFrames(nameof(Bake), 2); }
        if (!b && wasBuilt) Off();
        wasBuilt = b;
        // the map's copy moved (PHGame.layers: the player's team changed): the spot goes with it, and bakes again
        if (b && !bakeQueued && builder.originVer != bakedVer) { bakeQueued = true; SendCustomEventDelayedFrames(nameof(Bake), 2); }
    }

    public void Off()
    {
        VRCShader.SetGlobalFloat(globalId, 0f);
        spot.gameObject.SetActive(false);
    }

    bool bakeQueued;
    int bakedVer;

    public void Bake()
    {
        bakeQueued = false;
        if (!builder.built) return;
        bakedVer = builder.originVer;
        MapGenUdon gen = builder.gen;
        float w = builder.SiteWidth(), h = builder.SiteDepth();   // the map without its upper floors and basements, laid out beside it in the generator
        Vector3 centre = builder.ToWorld(new Vector3(w * 0.5f, 0f, h * 0.5f));   // (this client's copy of the map)
        float radius = Mathf.Sqrt(w * w + h * h) * 0.5f + Mathf.Max(margin, 15f);   // covers PropLit's 10 m fade out to the corners
        float height = radius / Mathf.Tan(coneDeg * 0.5f * Mathf.Deg2Rad);

        // the casters: walls, roofs (shown for the bake) and fences
        bool roofsShown = builder.roofsFilter.GetComponent<MeshRenderer>().enabled;
        builder.ShowRoofs(true);
        builder.wallsFilter.gameObject.layer = casterLayer;
        builder.roofsFilter.gameObject.layer = casterLayer;
        Transform fences = builder.fenceRoot;
        for (int i = 0; i < fences.childCount; i++) fences.GetChild(i).gameObject.layer = casterLayer;
        for (int i = 0; i < builder.SceneryTileCount(); i++) { GameObject st = builder.SceneryTile(i); if (st != null) st.layer = casterLayer; }   // the fences, hedges and decor, merged (batching)

        // the map's sun (MapBuilderUdon.ApplyMood), not the light: a hunter waiting in the lobby has the lobby's (Cor)
        Vector3 dir = builder.moodSunRot * Vector3.forward;
        Color sunColor = builder.moodSunColor;
        float sunIntensity = builder.moodSunIntensity;
        Transform t = spot.transform;
        t.position = centre - dir * height;
        t.rotation = Quaternion.LookRotation(dir);
        spot.gameObject.SetActive(true);
        // a hard cone edge: the default falloff dims most of a narrow cone
        spot.SetSpotLight(coneDeg, 0.03f);
        // Light Volumes' point attenuation is size^2 / (d^2 + size^2): scale so that the light reaching the map's
        // centre equals the directional light's (the range follows the intensity, so it reaches well past the map)
        // a large source keeps the numbers small (a tiny one needs intensities that overflow the shader's maths)
        // also flattens the falloff across the map. A dim sun (night, overcast) gets a larger source: Light Volumes cuts
        // a light's range short where it falls under its brightness cutoff, and a faint moon's spot would end before the
        // ground and leave the map darker than the land past it (an arc where they meet)
        spot.SetLightSourceSize(height * 2f * Mathf.Max(1f, Mathf.Sqrt(0.6f / Mathf.Max(0.05f, sunIntensity))));
        float size = spot.LightSourceSize;
        spot.SetColorAndIntensity(sunColor, sunIntensity * (height * height + size * size) / (size * size) * intensityScale);
        spot.LayerMask = 1 << casterLayer;
        spot.FarClip = height + radius * 2f;
        spot.NearClip = Mathf.Max(1f, height - radius * 2f);
        spot.ShadowBakeResolution = IsQuest() ? resolutionQuest : resolutionPc;
        spot.RuntimeShadowResolution = spot.ShadowBakeResolution;
        spot.UpdateTransform();
        spot.BakeShadows();

        // the walls and the roofs go back to the architecture layer (fences have no collider of their own)
        builder.wallsFilter.gameObject.layer = 11;
        builder.roofsFilter.gameObject.layer = 11;
        for (int i = 0; i < fences.childCount; i++) fences.GetChild(i).gameObject.layer = 11;
        for (int i = 0; i < builder.SceneryTileCount(); i++) { GameObject st = builder.SceneryTile(i); if (st != null) st.layer = 11; }
        builder.ShowRoofs(roofsShown);
        // where the spot reaches: the shader blends back to the shadowless base sun towards the cone's edge
        // where the spot hands back to the base sun: the map's own rectangle (centre, half width, half depth). The land
        // past the fence, out to the hills, takes the base sun: the spot's footprint is a tilted ellipse that does not
        // cover it evenly, and the edge showed as a dark arc (Cor)
        VRCShader.SetGlobalVector(areaId, new Vector4(centre.x, centre.z, w * 0.5f, h * 0.5f));
        VRCShader.SetGlobalFloat(globalId, 1f);
    }

    bool IsQuest()
    {
#if UNITY_ANDROID
        return true;
#else
        return false;
#endif
    }
}
