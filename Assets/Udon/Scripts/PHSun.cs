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

    void Update()
    {
        if (builder == null || spot == null) return;
        bool b = builder.built;
        if (b && !wasBuilt) SendCustomEventDelayedFrames(nameof(Bake), 2);
        if (!b && wasBuilt) Off();
        wasBuilt = b;
    }

    public void Off()
    {
        VRCShader.SetGlobalFloat(globalId, 0f);
        spot.gameObject.SetActive(false);
    }

    public void Bake()
    {
        if (!builder.built) return;
        MapGenUdon gen = builder.gen;
        float w = gen.W * 4f, h = gen.H * 4f;                     // MapBuilderUdon.CELL
        Vector3 centre = new Vector3(w * 0.5f, 0f, h * 0.5f);
        float radius = Mathf.Sqrt(w * w + h * h) * 0.5f + margin;
        float height = radius / Mathf.Tan(coneDeg * 0.5f * Mathf.Deg2Rad);

        // the casters: walls, roofs (shown for the bake) and fences
        bool roofsShown = builder.roofsFilter.GetComponent<MeshRenderer>().enabled;
        builder.ShowRoofs(true);
        builder.wallsFilter.gameObject.layer = casterLayer;
        builder.roofsFilter.gameObject.layer = casterLayer;
        Transform fences = builder.fenceRoot;
        for (int i = 0; i < fences.childCount; i++) fences.GetChild(i).gameObject.layer = casterLayer;

        Vector3 dir = sun.transform.forward;
        Transform t = spot.transform;
        t.position = centre - dir * height;
        t.rotation = Quaternion.LookRotation(dir);
        spot.gameObject.SetActive(true);
        spot.SetSpotLight(coneDeg, 0.03f);   // a hard cone edge: the default falloff dims most of a narrow cone
        // Light Volumes' point attenuation is size^2 / (d^2 + size^2): scale so that the light reaching the map's
        // centre equals the directional light's (the range follows the intensity, so it reaches well past the map)
        // a large source keeps the numbers small (a tiny one needs intensities that overflow the shader's maths)
        spot.SetLightSourceSize(height * 2f);   // also flattens the falloff across the map
        float size = spot.LightSourceSize;
        spot.SetColorAndIntensity(sun.color, sun.intensity * (height * height + size * size) / (size * size) * intensityScale);
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
        builder.ShowRoofs(roofsShown);
        // where the spot reaches: the shader blends back to the shadowless base sun towards the cone's edge
        VRCShader.SetGlobalVector(areaId, new Vector4(centre.x, centre.z, radius - margin, radius - 1f));
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
