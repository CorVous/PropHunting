using TMPro;
using UnityEngine.UI;
using VRC.SDK3.Components;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRCSceneDescriptor = VRC.SDK3.Components.VRCSceneDescriptor;
using VRCStation = VRC.SDK3.Components.VRCStation;
using Mobility = VRC.SDKBase.VRCStation.Mobility;

// Builds the world's fixed objects in the open scene (menu PropHunt > Build world scene). Everything it makes
// lives under "World"; running it again replaces that. The map objects (MapGenUdon, MapBuilderUdon, prefabs)
// come from PropHunt > Bake prop prefabs, which this runs first if they are missing.
//   VRCWorld          the scene descriptor, spawning in the lobby
//   Sun               the prototype's light and ambient colours
//   Lobby             a room well away from the maps (the prototype's Lobby.Origin), with the green zone (a
//                     trigger box: whoever stands in it takes part) and a START button for the host
//   Game              PHGame, PHLocal (with a head-locked HUD), and 32 player slots (station + voice each)
public static class WorldSceneBuilder
{
    // in the distance on the map's own level, just past the hills and mountains round the largest map (SkyDressing: up
    // to about 190 m from its centre; it leaves a gap facing the lobby). Cor: late joiners spawn here, so not close.
    // 5 cm up so its floor does not fight the map's ground plane, which reaches out under it
    static readonly Vector3 LobbyOrigin = new Vector3(-170f, 0.05f, -170f);
    const float RoomW = 24f, RoomD = 22f, WallH = 7f, ZoneHalf = 5.5f;
    static readonly Vector3 MiniMapAt = new Vector3(-7.0f, 0f, 4.0f);    // the LAST ROUND diorama table, lobby space (centre of the room at 0, north +z): in the ready area's west half
    const float MiniMapYaw = 0f;                                             // its card on the south edge, towards the spawn
    const int SLOTS = 32;

    // the credits board alone, after CREDITS.md changes (the open scene's lobby keeps everything else)
    [MenuItem("PropHunt/Rebuild credits board")]
    public static void RebuildCredits()
    {
        var lobby = GameObject.Find("World/Lobby");
        if (lobby == null) { Debug.LogWarning("[prophunt] no World/Lobby in the open scene"); return; }
        LobbyBuilder.EnsureAssets();
        var old = lobby.transform.Find("Credits");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        BuildCredits(lobby.transform);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lobby.scene);
    }

    // on the north wall left of the host panel (Cor), level with its centre: 5.4 m wide; facing south, like the panel
    static void BuildCredits(Transform lobby)
    {
        LobbyBuilder.BuildCredits(lobby, new Vector3(-7.9f, 2.58f, RoomD / 2 - 0.12f), Quaternion.identity, 0.0036f);
    }

    [MenuItem("PropHunt/Build world scene")]
    public static void Build()
    {
        if (Object.FindObjectOfType<MapBuilderUdon>(true) == null) PropPrefabBaker.Bake();
        LobbyBuilder.EnsureAssets();
        var builder = Object.FindObjectOfType<MapBuilderUdon>(true);
        var gen = Object.FindObjectOfType<MapGenUdon>(true);
        var bench = Object.FindObjectOfType<MapGenBench>(true);
        if (bench != null) Object.DestroyImmediate(bench.gameObject);

        var old = GameObject.Find("World");
        if (old != null) Object.DestroyImmediate(old);
        var world = new GameObject("World");

        // ---- light
        var sunGo = new GameObject("Sun");
        sunGo.transform.SetParent(world.transform, false);
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional; sun.color = new Color(1f, 0.96f, 0.88f); sun.intensity = 1.0f; sun.shadows = LightShadows.Soft;
        sunGo.transform.rotation = Quaternion.Euler(52f, 38f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.52f, 0.53f, 0.54f);
        RenderSettings.ambientGroundColor = new Color(0.50f, 0.48f, 0.45f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.56f, 0.76f, 0.93f);
        RenderSettings.fogStartDistance = 45f; RenderSettings.fogEndDistance = 160f;
        // a procedural sky the map's mood tints (MapBuilderUdon.ApplyMood: dawn, dusk, night, a space base's sky)
        var skyPath = "Assets/Udon/Generated/Sky.mat";
        var skyMat = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if (skyMat == null) { skyMat = new Material(Shader.Find("PropHunt/SkyGradient")); AssetDatabase.CreateAsset(skyMat, skyPath); }
        else if (skyMat.shader.name != "PropHunt/SkyGradient") skyMat.shader = Shader.Find("PropHunt/SkyGradient");   // the cheap sky (profiling)
        RenderSettings.skybox = skyMat;
        RenderSettings.sun = sun;

        // ---- lobby
        var lobby = new GameObject("Lobby");
        lobby.transform.SetParent(world.transform, false);
        lobby.transform.position = LobbyOrigin;
        lobby.AddUdonSharpComponent<PHLobbyCull>();   // not drawn while the player is out on the map (profiling)
        // the inside of a game box (LobbyBuilder dresses it)
        var floorMat = Mat("LobbyFloor", new Color(0.953f, 0.906f, 0.788f));
        var wallMat = Mat("LobbyWall", new Color(0.141f, 0.376f, 0.290f));
        var zoneMat = Mat("LobbyZone", new Color(0.373f, 0.749f, 0.447f));
        var buttonMat = Mat("StartButton", new Color(0.9f, 0.35f, 0.25f));
        Box(lobby.transform, "Floor", new Vector3(0, -0.25f, 0), new Vector3(RoomW, 0.5f, RoomD), floorMat, true);
        Box(lobby.transform, "WallN", new Vector3(0, WallH / 2, RoomD / 2 + 0.1f), new Vector3(RoomW, WallH, 0.2f), wallMat, true);
        Box(lobby.transform, "WallS", new Vector3(0, WallH / 2, -RoomD / 2 - 0.1f), new Vector3(RoomW, WallH, 0.2f), wallMat, true);
        // the east wall (Cor, the weapon wall): pushed out 1 m into an alcove behind an opening; the old wall's line
        // keeps panels above and below the opening and at its ends, so the room's band and trim run on
        float al = LobbyBuilder.Alcove, lo = LobbyBuilder.OpeningLow, hi = LobbyBuilder.OpeningHigh, oh = LobbyBuilder.OpeningHalf;
        Box(lobby.transform, "WallE", new Vector3(RoomW / 2 + 0.1f, (hi + WallH) / 2, 0), new Vector3(0.2f, WallH - hi, RoomD), wallMat, true);
        Box(lobby.transform, "WallEBelow", new Vector3(RoomW / 2 + 0.1f, lo / 2, 0), new Vector3(0.2f, lo, 2 * oh), wallMat, true);
        Box(lobby.transform, "WallENorth", new Vector3(RoomW / 2 + 0.1f, hi / 2, (oh + RoomD / 2) / 2), new Vector3(0.2f, hi, RoomD / 2 - oh), wallMat, true);
        Box(lobby.transform, "WallESouth", new Vector3(RoomW / 2 + 0.1f, hi / 2, -(oh + RoomD / 2) / 2), new Vector3(0.2f, hi, RoomD / 2 - oh), wallMat, true);
        Box(lobby.transform, "AlcoveBack", new Vector3(RoomW / 2 + al + 0.1f, WallH / 2, 0), new Vector3(0.2f, WallH, 2 * oh + 0.4f), wallMat, true);
        Box(lobby.transform, "AlcoveFloor", new Vector3(RoomW / 2 + al / 2 + 0.1f, -0.25f, 0), new Vector3(al + 0.2f, 0.5f, 2 * oh + 0.4f), floorMat, true);
        Box(lobby.transform, "AlcoveTop", new Vector3(RoomW / 2 + al / 2 + 0.1f, WallH + 0.2f, 0), new Vector3(al + 0.4f, 0.4f, 2 * oh + 0.4f), Mat("StoreVoid", new Color(0.012f, 0.012f, 0.012f)), false);
        Box(lobby.transform, "AlcoveNorth", new Vector3(RoomW / 2 + al / 2 + 0.1f, WallH / 2, oh + 0.1f), new Vector3(al, WallH, 0.2f), wallMat, true);
        Box(lobby.transform, "AlcoveSouth", new Vector3(RoomW / 2 + al / 2 + 0.1f, WallH / 2, -oh - 0.1f), new Vector3(al, WallH, 0.2f), wallMat, true);
        Box(lobby.transform, "WallW", new Vector3(-RoomW / 2 - 0.1f, WallH / 2, 0), new Vector3(0.2f, WallH, RoomD), wallMat, true);
        Vector3 zoneCentre = new Vector3(0f, 0f, RoomD / 2 - 0.15f - ZoneHalf);
        // the ready area spans the room from wall to wall (Cor's playtest), up to the coloured track along each side
        float zoneHalfX = RoomW / 2 - 1.45f;
        Box(lobby.transform, "ZoneFloor", zoneCentre + new Vector3(0, 0.005f, 0), new Vector3(zoneHalfX * 2, 0.01f, ZoneHalf * 2), zoneMat, false);
        var zone = new GameObject("Zone");
        zone.transform.SetParent(lobby.transform, false);
        zone.transform.localPosition = zoneCentre + new Vector3(0, 1.5f, 0);
        var zoneBox = zone.AddComponent<BoxCollider>();
        zoneBox.isTrigger = true; zoneBox.size = new Vector3(RoomW, 3f, ZoneHalf * 2);   // (to the walls themselves)
        zone.layer = 2;   // Ignore Raycast
        var spawn = new GameObject("Spawn");
        spawn.transform.SetParent(lobby.transform, false);
        spawn.transform.localPosition = new Vector3(0f, 0.05f, -RoomD / 2 + 2.3f);   // on the spawn disc
        LobbyBuilder.DressRoom(lobby.transform, RoomW, RoomD, WallH, ZoneHalf, zoneHalfX);
        // the lobby as seen from the map (Cor): one plain box round it, outside the lobby object so PHLobbyCull leaves
        // it drawn; unlit and unfogged so it shows that far off; invisible from inside (its faces point outwards)
        var shellMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/LobbyShell.mat");
        if (shellMat == null) { shellMat = new Material(Shader.Find("PropHunt/LobbyShell")); AssetDatabase.CreateAsset(shellMat, "Assets/Udon/Generated/LobbyShell.mat"); }
        else shellMat.shader = Shader.Find("PropHunt/LobbyShell");
        var shell = new GameObject("LobbyShell");
        shell.transform.SetParent(world.transform, false);
        shell.transform.position = LobbyOrigin;
        var shellBox = Box(shell.transform, "Box", new Vector3(0f, (WallH + 1.0f) / 2f - 0.6f, 0f), new Vector3(RoomW + 1.2f + 2f * (LobbyBuilder.Alcove + 0.2f), WallH + 1.0f, RoomD + 1.2f), shellMat, false);   // (wide enough for the alcove)
        var shellR = shellBox.GetComponent<MeshRenderer>();
        shellR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; shellR.receiveShadows = false;

        // ---- the scene descriptor
        var vrcWorld = new GameObject("VRCWorld");
        vrcWorld.transform.SetParent(world.transform, false);
        vrcWorld.transform.position = spawn.transform.position;
        var descriptor = vrcWorld.AddComponent<VRCSceneDescriptor>();
        descriptor.spawns = new[] { spawn.transform };
        descriptor.RespawnHeightY = -50f;
        // the reference camera gives the players' cameras their settings: everything but the hider's own prop
        // (layer PHSlot.HIDER_SELF), which only the third-person camera draws
        var refCam = new GameObject("ReferenceCamera");
        refCam.transform.SetParent(vrcWorld.transform, false);
        var rc = refCam.AddComponent<Camera>();
        rc.enabled = false;
        rc.cullingMask = ~(1 << PHSlot.HIDER_SELF);
        rc.nearClipPlane = 0.02f; rc.farClipPlane = 400f;
        rc.clearFlags = CameraClearFlags.Skybox;
        descriptor.ReferenceCamera = refCam;
        vrcWorld.AddComponent<VRC.Core.PipelineManager>();

        // ---- the game
        var gameGo = new GameObject("Game");
        gameGo.transform.SetParent(world.transform, false);
        var game = gameGo.AddUdonSharpComponent<PHGame>();
        game.gen = gen; game.builder = builder;
        game.lobbyZone = zoneBox; game.lobbySpawn = spawn.transform;

        var localGo = new GameObject("Local");
        localGo.transform.SetParent(gameGo.transform, false);
        var local = localGo.AddUdonSharpComponent<PHLocal>();
        local.game = game;
        local.sound = BuildAudio(gameGo.transform);
        local.hud = null; local.hudRoot = null;   // no head-locked text (Cor): PHView shows the little there is
        game.local = local;
        builder.sun = sun;   // the map's mood turns and tints it (MapBuilderUdon.ApplyMood)
        builder.lobby = lobby.transform;   // the hills and mountains leave a gap facing it (SkyDressing)
        // the hider's own prop and the prop pointed at are drawn with materials of their own (MapBuilderUdon.SelfOnly, Hover)
        builder.propLitMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/PropLit.mat");
        builder.selfMat = Variant("PropLitSelf", builder.propLitMat, "_PipOnly");
        builder.fadeMat = Variant("PropLitFade", builder.propLitMat, "_Fade");   // a map's props: see-through in the prop viewer's cutout
        builder.hoverMat = Variant("PropLitHover", builder.propLitMat, "_Hover");
        if (builder.hoverMat != null) { builder.hoverMat.SetFloat("_Fade", 1f); EditorUtility.SetDirty(builder.hoverMat); }   // pointed at, still a prop
        builder.shadeSelfMat = Variant("PropShadeSelf", AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/PropShade.mat"), "_PipOnly");
        UdonSharpEditorUtility.CopyProxyToUdon(builder);
        LightingBuilder.Build(world.transform, builder, sun);   // the map's sun shadows (Light Volumes spot, PHSun)
        LampBuilder.Build(world.transform, builder);           // the map's lamps (Point Light Volumes on the light points, PHLamps)
        var seedTool = gameGo.AddUdonSharpComponent<PHSeed>();
        UdonSharpEditorUtility.CopyProxyToUdon(seedTool);
        game.seedTool = seedTool;
        // its own object: it changes owner with every toggle, which must never move the game (the host) along
        var doorsGo = new GameObject("Doors");
        doorsGo.transform.SetParent(gameGo.transform, false);
        var doors = doorsGo.AddUdonSharpComponent<PHDoors>();
        doors.game = game;
        UdonSharpEditorUtility.CopyProxyToUdon(doors);
        local.doors = doors;
        BuildView(gameGo.transform, game, local);
        var body = localGo.AddUdonSharpComponent<PHHiderBody>();
        body.game = game; body.local = local;
        UdonSharpEditorUtility.CopyProxyToUdon(body);
        local.body = body;
        GenData.Build();
        local.defNames = new string[GenData.nDefs];
        for (int d = 0; d < GenData.nDefs; d++) local.defNames[d] = DefName(GenData.defId[d]);

        var beaconsRoot = new GameObject("Beacons").transform;
        beaconsRoot.SetParent(gameGo.transform, false);
        local.beaconGreen = Mat("BeaconGreen", new Color(0.2f, 0.9f, 0.3f));
        local.beaconRed = Mat("BeaconRed", new Color(0.95f, 0.25f, 0.2f));
        var hidersRoot = new GameObject("Hiders").transform;
        hidersRoot.SetParent(gameGo.transform, false);
        var slots = new PHSlot[SLOTS];
        for (int i = 0; i < SLOTS; i++)
        {
            var go = new GameObject("Slot" + i);
            go.transform.SetParent(gameGo.transform, false);
            go.transform.position = new Vector3(i * 3f, -400f, 0f);
            var st = go.AddComponent<VRCStation>();
            st.seated = false;
            st.PlayerMobility = Mobility.Immobilize;
            st.disableStationExit = true;
            st.canUseStationFromStation = true;
            st.stationEnterPlayerLocation = go.transform;
            st.stationExitPlayerLocation = go.transform;
            var slot = go.AddUdonSharpComponent<PHSlot>();
            slot.game = game; slot.index = i; slot.station = st; slot.proxyRoot = hidersRoot;
            // the prop hangs in a rig that keeps the world's axes: squashed across a doorway whatever way the prop faces
            var rigGo = new GameObject("Hider" + i);
            rigGo.transform.SetParent(hidersRoot, false);
            slot.squashRig = rigGo.transform;
            var voiceGo = new GameObject("Voice");
            voiceGo.transform.SetParent(gameGo.transform, false);
            voiceGo.name = "Voice" + i;
            var voice = voiceGo.AddComponent<AudioSource>();
            voice.spatialBlend = 1f; voice.rolloffMode = AudioRolloffMode.Linear; voice.maxDistance = 32f; voice.playOnAwake = false;   // (group playtest: taunts heard a little further)
            slot.voice = voice;
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "Beacon" + i;
            Object.DestroyImmediate(beacon.GetComponent<Collider>());
            beacon.transform.SetParent(beaconsRoot, false);
            beacon.transform.localScale = new Vector3(0.25f, 15f, 0.25f);   // a 30 m beam, centred at ground level
            beacon.GetComponent<MeshRenderer>().sharedMaterial = local.beaconGreen;
            beacon.SetActive(false);
            slot.beacon = beacon; slot.beaconRenderer = beacon.GetComponent<MeshRenderer>();
            UdonSharpEditorUtility.CopyProxyToUdon(slot);
            slots[i] = slot;
        }
        game.slots = slots;
        // the reveal's amber beams on where each hider spent the most time (PHDwell, Cor's playtest)
        {
            var amber = Mat("BeaconAmber", new Color(1f, 0.72f, 0.2f));
            var dwell = gameGo.AddUdonSharpComponent<PHDwell>();
            dwell.game = game; dwell.beams = new GameObject[SLOTS];
            for (int i = 0; i < SLOTS; i++)
            {
                var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beam.name = "Dwell" + i;
                Object.DestroyImmediate(beam.GetComponent<Collider>());
                beam.transform.SetParent(beaconsRoot, false);
                beam.transform.localScale = new Vector3(0.16f, 15f, 0.16f);
                var br = beam.GetComponent<MeshRenderer>(); br.sharedMaterial = amber; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                beam.SetActive(false);
                dwell.beams[i] = beam;
            }
            UdonSharpEditorUtility.CopyProxyToUdon(dwell);
        }
        game.revealMat = Mat2("Reveal", Shader.Find("PropHunt/Reveal"));   // the END reveal's striped highlight (PHSlot)

        var knocksRoot = new GameObject("Knocks");
        knocksRoot.transform.SetParent(gameGo.transform, false);
        var knocks = new PHKnock[16];
        for (int i = 0; i < knocks.Length; i++)
        {
            var go = new GameObject("Knock" + i);
            go.transform.SetParent(knocksRoot.transform, false);
            var k = go.AddUdonSharpComponent<PHKnock>();
            k.game = game;
            UdonSharpEditorUtility.CopyProxyToUdon(k);
            knocks[i] = k;
        }
        game.knocks = knocks;
        WeaponBuilder.Build(gameGo.transform, game, local, slots);   // the hunters' weapons, the hiders' guns, their effects (PHWeapons)

        // the host panel on the north wall, above the zone (G50, G51): every setting and START, the "Board Game"
        // design (LobbyBuilder). Everyone can read it; only the host's presses count (PHPanel and PHGame check).
        // A world-space UI canvas (VRC Ui Shape): point and click on desktop, point and pull the trigger in VR.
        // the map trail above it (PHHistory): the maps played here, with their photos; made first, since the panel's
        // REPLAY card is one of its cards
        game.history = LobbyBuilder.BuildTrail(lobby.transform, game, builder, new Vector3(0f, 4.98f, RoomD / 2 - 0.22f));
        LobbyBuilder.BuildPanel(lobby.transform, game, new Vector3(0f, 0.9f + 1.68f, RoomD / 2 - 0.12f));   // 7.2 x 3.36 m, bottom 0.9 m up
        LobbyBuilder.BuildFailNotice(lobby.transform, game, new Vector3(0f, 0.9f + 3.36f + 0.5f, RoomD / 2 - 0.12f));   // just over the panel
        // the last round's results: a score pad right of the panel (Cor: smaller, beside the options board; the credits
        // take the left), 3.5 x 2.45 m, level with the panel
        LobbyBuilder.BuildScorePad(lobby.transform, game, local, new Vector3(5.6f, 0.9f + 1.68f, RoomD / 2 - 0.12f), 0f, 0.0025f);
        local.panelUiText = null; local.resultsText = null;
        // the credits (from CREDITS.md), on the north wall left of the host panel
        BuildCredits(lobby.transform);
        // the language board: a lectern ahead and right of the spawn disc, turned towards it
        LobbyBuilder.BuildLanguageBoard(lobby.transform, game, new Vector3(2.7f, 0f, -RoomD / 2 + 3.6f), 28f);
        // the how-to-play wall: three looping picture panels on the spawn wall at eye height, centred behind the spawn
        LobbyBuilder.BuildHowTo(lobby.transform, new Vector3(0f, 1.6f, -RoomD / 2 + 0.03f), 0f);
        // the LAST ROUND diorama (Cor, item 40: the last round in miniature on a table in the ready area): move it here
        LobbyBuilder.BuildMiniMap(lobby.transform, game, builder, MiniMapAt, MiniMapYaw);

        // the DEBUG plaque (explore a map, spawn as hunter / hider, end round, theme) and the profiler on a sign standing
        // left of the spawn, the language board's twin (Cor: no wall space for debug); the scripts the profiler watches:
        // every one that runs each frame
        var prof = LobbyBuilder.BuildDebugSign(lobby.transform, game, new Vector3(-3.0f, 0f, -RoomD / 2 + 4.4f), -28f);   // (clear of the blue pawn behind it)
        {
            string[] names = { "PHGame", "PHLocal", "PHSlot", "PHHiderBody", "PHKnock", "PHLamps", "PHLobbyCull", "PHPanel", "PHScorePad", "PHSun", "PHView", "PHDoors", "PHWeapons", "MapBuilderUdon" };
            var list = new System.Collections.Generic.List<UdonSharp.UdonSharpBehaviour>();
            var groups = new System.Collections.Generic.List<int>();
            foreach (var u in Object.FindObjectsOfType<UdonSharp.UdonSharpBehaviour>(true))
            {
                int g = System.Array.IndexOf(names, u.GetType().Name);
                if (g < 0) continue;
                list.Add(u); groups.Add(g);
            }
            prof.scripts = list.ToArray(); prof.scriptGroup = groups.ToArray(); prof.groupNames = names;
            UdonSharpEditorUtility.CopyProxyToUdon(prof);
            Debug.Log("[prophunt] the profiler watches " + list.Count + " scripts");
        }

        // the weapon wall (east wall) and the Trophies board (spawn wall): after the weapons, whose lists number the picks
        LobbyBuilder.BuildStore(lobby.transform, game, local.sound, RoomW, RoomD);

        UdonSharpEditorUtility.CopyProxyToUdon(local);
        UdonSharpEditorUtility.CopyProxyToUdon(game);
        TextBaker.Bake();   // the text in every language (PHText under World/Game), its labels and fallback fonts
        EditorSceneManager.MarkSceneDirty(world.scene);
        Debug.Log("[prophunt] built the world scene: lobby, " + SLOTS + " slots");
    }

    // the prototype's GameUI.DefName: "k_chairCushion" -> "Chair cushion"
    static string DefName(string id)
    {
        int us = id.IndexOf('_');
        if (us >= 0 && us <= 4) id = id.Substring(us + 1);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < id.Length; i++)
        {
            char c = id[i];
            if (c == '_') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(id[i - 1])) sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    // PHView: the hider's health (desktop low on the screen, VR above the screen device) and the third-person view
    // (desktop lower right corner, VR a grabbable panel)
    static void BuildView(Transform parent, PHGame game, PHLocal local)
    {
        var viewGo = new GameObject("View");
        viewGo.transform.SetParent(parent, false);
        var view = viewGo.AddUdonSharpComponent<PHView>();
        view.game = game; view.local = local; local.view = view;

        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>("Assets/Udon/Generated/Pip.renderTexture");
        if (rt == null)
        {
            rt = new RenderTexture(384, 256, 16) { name = "Pip" };
            AssetDatabase.CreateAsset(rt, "Assets/Udon/Generated/Pip.renderTexture");
        }
        var camGo = new GameObject("PipCamera");
        camGo.transform.SetParent(viewGo.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.targetTexture = rt; cam.fieldOfView = 60f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 120f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.56f, 0.76f, 0.93f);
        // no players (9 Player, 10 PlayerLocal, 18 MirrorReflection: the local avatar's copy for mirrors), no UI;
        // the hider's own prop is on layer HIDER_SELF, which only this camera draws
        cam.cullingMask = ~((1 << 5) | (1 << 9) | (1 << 10) | (1 << 12) | (1 << 18) | (1 << 19));
        cam.enabled = false;
        view.pipCam = cam;
        // drawn over everything (no depth test): walls and props never cover the view or the health bar
        var pipMat = Mat2("PipDisplay", Shader.Find("PropHunt/Overlay"));
        hudMat = Mat2("HudOverlay", Shader.Find("PropHunt/Overlay"));
        pipMat.mainTexture = rt;
        var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        // desktop: head-locked, 0.5 m ahead
        var desk = new GameObject("Desktop");
        desk.transform.SetParent(viewGo.transform, false);
        desk.layer = 5;
        var dc = desk.AddComponent<Canvas>(); dc.renderMode = RenderMode.WorldSpace;
        var drt = desk.GetComponent<RectTransform>(); drt.sizeDelta = new Vector2(1600, 900);
        desk.transform.localScale = Vector3.one * 0.0004f;
        GameObject bar; Image fill;
        // (PHView fits the canvas to the screen: 900 units high, as wide as the screen)
        HealthBar(desk.transform, sprite, new Vector2(0f, -410f), new Vector2(280f, 18f), out bar, out fill);
        view.desktopHealthBar = bar; view.desktopHealth = fill;
        view.desktopShields = Shields(bar, new Vector2(280f, 18f));
        var pip = new GameObject("Pip");
        pip.transform.SetParent(desk.transform, false);
        pip.layer = 5;
        var raw = pip.AddComponent<RawImage>(); raw.texture = rt; raw.raycastTarget = false; raw.material = hudMat;
        var prt = pip.GetComponent<RectTransform>();   // the bottom right corner
        prt.anchorMin = new Vector2(1f, 0f); prt.anchorMax = new Vector2(1f, 0f); prt.pivot = new Vector2(1f, 0f);
        prt.anchoredPosition = new Vector2(-24f, 24f); prt.sizeDelta = new Vector2(420f, 280f);   // a margin inside the screen's edge; clear of the health bar (centre, 140 each side) down to 4:3 (Cor: bigger)
        view.desktopPip = pip;
        view.desktopRoot = desk.transform;

        // VR: the third-person panel, grabbable
        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "PipPanel";
        panel.transform.SetParent(viewGo.transform, false);
        panel.transform.localScale = new Vector3(0.36f, 0.24f, 1f);
        panel.layer = 13;   // Pickup
        Object.DestroyImmediate(panel.GetComponent<MeshCollider>());
        var bc = panel.AddComponent<BoxCollider>(); bc.size = new Vector3(1f, 1f, 0.05f);
        panel.GetComponent<MeshRenderer>().sharedMaterial = pipMat;
        var rb = panel.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
        var pickup = panel.AddComponent<VRCPickup>();
        pickup.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.No;
        pickup.InteractionText = "Third-person view";
        view.vrPanel = panel.transform; view.vrPanelPickup = pickup;

        // ---- a VR hider's devices (Cor): flat-coloured, without colliders, on the UI layer (the third-person camera
        // leaves it out). PHView puts them in the hands each frame; nobody else sees them.
        var dark = Flat("DeviceDark", new Color(0.16f, 0.18f, 0.22f));
        var mid = Flat("DeviceMid", new Color(0.30f, 0.33f, 0.38f));
        var teal = Flat("DeviceTeal", new Color(0.25f, 0.80f, 0.75f));
        var amber = Flat("DeviceAmber", new Color(1.00f, 0.72f, 0.20f));
        var red = Flat("DeviceRed", new Color(0.92f, 0.26f, 0.22f));
        var grey = Flat("DeviceGrey", new Color(0.45f, 0.42f, 0.42f));
        var beamIdle = Flat("BeamIdle", new Color(0.82f, 0.84f, 0.86f));
        var beamHit = Flat("BeamHit", new Color(0.45f, 1.00f, 0.55f));

        // right hand: the selector (it points along +z, the ray that picks)
        var gun = Empty(viewGo.transform, "Selector");
        Part(gun, PrimitiveType.Cube, "Barrel", new Vector3(0f, 0f, 0.05f), Vector3.zero, new Vector3(0.028f, 0.034f, 0.15f), dark);
        Part(gun, PrimitiveType.Cube, "Muzzle", new Vector3(0f, 0f, 0.126f), Vector3.zero, new Vector3(0.036f, 0.042f, 0.014f), teal);
        Part(gun, PrimitiveType.Cube, "Sight", new Vector3(0f, 0.022f, 0.085f), Vector3.zero, new Vector3(0.008f, 0.010f, 0.03f), teal);
        Part(gun, PrimitiveType.Cube, "Grip", new Vector3(0f, -0.045f, -0.005f), new Vector3(20f, 0f, 0f), new Vector3(0.026f, 0.07f, 0.032f), mid);
        view.gun = gun;
        var beam = Part(viewGo.transform, PrimitiveType.Cube, "SelectorBeam", Vector3.zero, Vector3.zero, new Vector3(0.004f, 0.004f, 1f), beamIdle);
        view.beam = beam.transform; view.beamRenderer = beam.GetComponent<MeshRenderer>();
        view.beamIdle = beamIdle; view.beamHit = beamHit;

        // the screen device: a casing around the panel (the panel is its screen and its grab), with a chin that
        // carries the taunt button. The casing's front is -z, as the screen's.
        var casing = Empty(viewGo.transform, "ScreenCasing");
        Part(casing, PrimitiveType.Cube, "Back", new Vector3(0f, 0f, 0.014f), Vector3.zero, new Vector3(0.40f, 0.28f, 0.024f), dark);
        Part(casing, PrimitiveType.Cube, "Chin", new Vector3(0f, -0.175f, 0.004f), Vector3.zero, new Vector3(0.40f, 0.09f, 0.028f), mid);
        for (int i = 0; i < 4; i++)
            Part(casing, PrimitiveType.Cube, "Slot" + i, new Vector3(0.07f + i * 0.022f, -0.175f, -0.0105f), Vector3.zero, new Vector3(0.008f, 0.04f, 0.002f), dark);
        view.casing = casing;
        // VR: the hider's health, a slim bar a little above the screen device (Cor: "can the health go above the camera
        // view in vr? anchored along with it?"). A child of the casing, so it is carried, put down and sized with it;
        // no collider, clear of the handle (its top at 0.164) and of the taunt button on the chin
        var vrHealth = new GameObject("ScreenHealth");
        vrHealth.transform.SetParent(casing, false);
        vrHealth.layer = 5;
        var vhc = vrHealth.AddComponent<Canvas>(); vhc.renderMode = RenderMode.WorldSpace;
        var vhr = vrHealth.GetComponent<RectTransform>();
        vhr.sizeDelta = new Vector2(360f, 20f);
        vhr.localPosition = new Vector3(0f, 0.19f, 0f); vhr.localRotation = Quaternion.identity;
        vhr.localScale = Vector3.one * 0.001f;   // canvas units are millimetres: a bar 34 cm by 1.6 cm
        HealthBar(vrHealth.transform, sprite, Vector2.zero, new Vector2(340f, 16f), out bar, out fill);
        view.vrHealthBar = bar; view.vrHealth = fill;
        view.vrShields = Shields(bar, new Vector2(340f, 16f));
        var button = Empty(viewGo.transform, "TauntButton");
        Part(button, PrimitiveType.Cylinder, "Base", new Vector3(0f, 0f, -0.006f), new Vector3(90f, 0f, 0f), new Vector3(0.082f, 0.006f, 0.082f), dark);
        var cap = Part(button, PrimitiveType.Cylinder, "Cap", new Vector3(0f, 0f, -0.022f), new Vector3(90f, 0f, 0f), new Vector3(0.066f, 0.012f, 0.066f), red);
        view.tauntButton = button; view.tauntCap = cap.transform; view.tauntCapRenderer = cap.GetComponent<MeshRenderer>();
        view.capReady = red; view.capWait = grey;
        // a speaker beside the button (Cor: so it is obvious what it does): a box, its cone, and three sound waves,
        // light on the dark chin, just right of the button
        var light = Flat("DeviceLight", new Color(0.88f, 0.90f, 0.92f));
        var spk = Empty(casing, "Speaker");
        spk.localPosition = new Vector3(-0.058f, -0.175f, -0.012f);
        Part(spk, PrimitiveType.Cube, "Box", new Vector3(-0.012f, 0f, 0f), Vector3.zero, new Vector3(0.010f, 0.012f, 0.003f), light);
        Part(spk, PrimitiveType.Cube, "Cone", new Vector3(-0.003f, 0f, 0f), Vector3.zero, new Vector3(0.008f, 0.026f, 0.003f), light);
        for (int w = 0; w < 3; w++)
            Part(spk, PrimitiveType.Cube, "Wave" + w, new Vector3(0.007f + w * 0.0065f, 0f, 0f), Vector3.zero, new Vector3(0.0025f, 0.012f + w * 0.009f, 0.003f), light);

        // the own prop's footprint on the ground (PHView sizes its edges to the prop)
        var outlineMat = Flat("OutlineTeal", new Color(0.30f, 0.95f, 0.85f));
        var outline = Empty(viewGo.transform, "PropOutline");
        // 40 short pieces for the edges, each laid on what is under it, and the front's nub (the last)
        view.outlineParts = new Transform[41];
        for (int i = 0; i < view.outlineParts.Length; i++)
            view.outlineParts[i] = Part(outline, PrimitiveType.Cube, i < 40 ? "Piece" + i : "Nub", Vector3.zero, Vector3.zero, new Vector3(0.02f, 0.004f, 0.02f), outlineMat).transform;
        view.outline = outline;
        view.outlineTurnMat = amber;   // while the prop is grabbed for turning (Cor)

        // the grab ring (Cor's experiment): 24 short flat pieces round a circle of radius 1, scaled by PHView to the
        // avatar's size round the seat; teal, amber while gripped. On the UI layer, no colliders.
        var ring = Empty(viewGo.transform, "GrabRing");
        const int ringN = 24;
        float seg = 2f * Mathf.PI / ringN * 1.04f;
        for (int i = 0; i < ringN; i++)
        {
            float a = i * 360f / ringN;
            Vector3 at = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            Part(ring, PrimitiveType.Cube, "Seg" + i, at, new Vector3(0f, a, 0f), new Vector3(seg, 0.03f, 0.05f), teal);
        }
        // two hands on the ring in front (Cor: hand icons, so it is clear it is grabbed), left and right of straight
        // ahead, upright, facing in; built in the ring's own units (radius 1)
        view.ringHands = new Transform[2];
        for (int side = -1; side <= 1; side += 2)
        {
            float a = side * 38f;
            var hand = Empty(ring, side < 0 ? "HandLeft" : "HandRight");
            view.ringHands[side < 0 ? 0 : 1] = hand;   // (they pulse until the player's first grab: PHView)
            hand.localPosition = Quaternion.Euler(0f, a, 0f) * Vector3.forward + Vector3.up * 0.16f;
            hand.localRotation = Quaternion.Euler(0f, a, 0f);
            hand.localScale = new Vector3(-side, 1f, 1f);   // (mirrored: thumbs towards the middle)
            Part(hand, PrimitiveType.Cube, "Palm", new Vector3(0f, 0f, 0f), Vector3.zero, new Vector3(0.12f, 0.13f, 0.025f), light);
            for (int f = 0; f < 4; f++)
                Part(hand, PrimitiveType.Cube, "Finger" + f, new Vector3(-0.045f + f * 0.03f, 0.11f - Mathf.Abs(f - 1.5f) * 0.012f, 0f), Vector3.zero, new Vector3(0.022f, 0.09f, 0.025f), light);
            Part(hand, PrimitiveType.Cube, "Thumb", new Vector3(0.075f, 0.01f, 0f), new Vector3(0f, 0f, -35f), new Vector3(0.024f, 0.075f, 0.025f), light);
        }
        view.grabRing = ring; view.ringIdle = teal; view.ringHeld = amber;

        // the final stand's flames on the hider's own gun (PHView.UpdateFire): small additive embers rising from the
        // muzzle, on the UI layer (the third-person view leaves them out)
        var fireMat = Mat2("GunFire", Shader.Find("PropHunt/Fx"));
        fireMat.SetColor("_Color", new Color(1f, 0.55f, 0.15f, 0.9f));
        fireMat.SetFloat("_Src", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        fireMat.SetFloat("_Dst", (float)UnityEngine.Rendering.BlendMode.One);
        fireMat.SetFloat("_Round", 1f); fireMat.SetFloat("_Trail", 0f);
        fireMat.renderQueue = 3000;
        EditorUtility.SetDirty(fireMat);
        var fireGo = new GameObject("GunFire");
        fireGo.transform.SetParent(viewGo.transform, false);
        fireGo.layer = 5;
        var fire = fireGo.AddComponent<ParticleSystem>();
        fire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var fm = fire.main;
        fm.playOnAwake = false; fm.loop = false; fm.duration = 1f;
        fm.simulationSpace = ParticleSystemSimulationSpace.World;
        fm.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        fm.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        fm.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
        fm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.35f, 0.05f));
        fm.gravityModifier = -0.25f;   // (they rise)
        fm.maxParticles = 120;
        var fe = fire.emission; fe.enabled = true; fe.rateOverTime = 0f;
        var fs = fire.shape; fs.enabled = true; fs.shapeType = ParticleSystemShapeType.Sphere; fs.radius = 0.012f;
        var fc = fire.colorOverLifetime; fc.enabled = true;
        var fg = new Gradient();
        fg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fc.color = fg;
        var fsz = fire.sizeOverLifetime; fsz.enabled = true; fsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
        var fr = fireGo.GetComponent<ParticleSystemRenderer>();
        fr.sharedMaterial = fireMat; fr.renderMode = ParticleSystemRenderMode.Billboard;
        fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; fr.receiveShadows = false;
        view.gunFire = fire;

        // and the own prop's shape in first person: the cage of its edges (MapBuilderUdon.Cage lends this one object
        // to the local hider's prop; the meshes are PropPrefabBaker's). Off until lent; no collider, no shadows.
        var cageMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Udon/Generated/PropCage.mat");
        var cageShader = Shader.Find("PropHunt/Cage");
        if (cageMat == null) { cageMat = new Material(cageShader); AssetDatabase.CreateAsset(cageMat, "Assets/Udon/Generated/PropCage.mat"); }
        cageMat.shader = cageShader;
        cageMat.SetColor("_Core", new Color(0.30f, 0.95f, 0.85f));   // the outline's teal
        cageMat.SetColor("_Rim", new Color(0.02f, 0.30f, 0.27f));
        cageMat.enableInstancing = true;   // single-pass instanced stereo
        EditorUtility.SetDirty(cageMat);
        var cageGo = new GameObject("PropCage");
        cageGo.transform.SetParent(viewGo.transform, false);
        var cageFilter = cageGo.AddComponent<MeshFilter>();
        var cageRenderer = cageGo.AddComponent<MeshRenderer>();
        cageRenderer.sharedMaterial = cageMat;
        cageRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; cageRenderer.receiveShadows = false;
        cageRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; cageRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        cageRenderer.allowOcclusionWhenDynamic = false;
        cageGo.SetActive(false);
        game.builder.cage = cageGo; game.builder.cageFilter = cageFilter; game.builder.cageRenderer = cageRenderer;
        UdonSharpEditorUtility.CopyProxyToUdon(game.builder);
        EditorUtility.SetDirty(game.builder);

        // desktop: the controls in the bottom left corner, drawn as VRChat's own panels are (Cor): a dark rounded
        // panel, a white picture over a key cap. One picture for each state; PHView shows and sizes them.
        var lg = new GameObject("Legend");
        lg.transform.SetParent(desk.transform, false);
        lg.layer = 5;
        var li = lg.AddComponent<RawImage>(); li.raycastTarget = false; li.material = hudMat;
        var lrt = lg.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.zero; lrt.pivot = Vector2.zero;
        lrt.anchoredPosition = new Vector2(24f, 24f); lrt.sizeDelta = new Vector2(420f, 80f);
        view.legend = li;
        view.legendHider = LegendTexture("LegendHider");
        view.legendHiderShort = LegendTexture("LegendHiderShort");
        view.legendHunter = LegendTexture("LegendHunter");
        view.desktopCanvas = drt;

        UdonSharpEditorUtility.CopyProxyToUdon(view);
    }

    // a copy of a material with one of its float marks set
    static Material Variant(string name, Material from, string flag)
    {
        if (from == null) { Debug.LogError("[prophunt] no material to make " + name + " from"); return null; }
        string path = "Assets/Udon/Generated/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(from); AssetDatabase.CreateAsset(m, path); }
        m.shader = from.shader;
        m.CopyPropertiesFromMaterial(from);
        m.enableInstancing = true;
        m.SetFloat(flag, 1f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // a picture of the desktop controls (Assets/Udon/Textures, drawn by Tools/make_legend.py)
    static Texture2D LegendTexture(string name)
    {
        string path = "Assets/Udon/Textures/" + name + ".png";
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogError("[prophunt] no legend picture at " + path); return null; }
        ti.textureType = TextureImporterType.Default;
        ti.alphaIsTransparency = true; ti.sRGBTexture = true;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Transform Empty(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.layer = 5;
        return go.transform;
    }

    static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.layer = 5;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    static Material Flat(string name, Color c)
    {
        string path = "Assets/Udon/Generated/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("Unlit/Color");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.color = c;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material hudMat;

    static void HealthBar(Transform parent, Sprite sprite, Vector2 at, Vector2 size, out GameObject bar, out Image fill)
    {
        bar = new GameObject("HealthBar");
        bar.transform.SetParent(parent, false);
        bar.layer = 5;
        var bg = bar.AddComponent<Image>(); bg.sprite = null; bg.color = new Color(0f, 0f, 0f, 0.55f); bg.raycastTarget = false; bg.material = hudMat;
        var brt = bar.GetComponent<RectTransform>(); brt.anchoredPosition = at; brt.sizeDelta = size;
        // the track inside the border; the fill's right edge is its anchor (PHView: anchorMax.x = the health's share)
        var track = new GameObject("Track");
        track.transform.SetParent(bar.transform, false);
        track.layer = 5;
        var trt = track.AddComponent<RectTransform>(); trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(3, 3); trt.offsetMax = new Vector2(-3, -3);
        var f = new GameObject("Fill");
        f.transform.SetParent(track.transform, false);
        f.layer = 5;
        fill = f.AddComponent<Image>(); fill.sprite = null; fill.color = new Color(0.35f, 0.85f, 0.4f, 0.9f); fill.raycastTarget = false; fill.material = hudMat;
        var frt = f.GetComponent<RectTransform>(); frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
    }

    // The disguise's toughness over the bar (Cor): 0 to 3 shields, one shape in one colour with a dark outline, read
    // by their count and not by colour. Children of the bar, so they show with it; PHView switches them on.
    static GameObject[] Shields(GameObject bar, Vector2 barSize)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Udon/Textures/Shield.png");
        var list = new GameObject[3];
        float h = barSize.y * 1.6f, w = h * 48f / 56f, gap = h * 0.25f;
        for (int i = 0; i < 3; i++)
        {
            var g = new GameObject("Shield" + i);
            g.transform.SetParent(bar.transform, false);
            g.layer = 5;
            var img = g.AddComponent<RawImage>(); img.texture = tex; img.raycastTarget = false; img.material = hudMat;
            var rt = g.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(i * (w + gap), barSize.y * 0.3f);
            g.SetActive(false);
            list[i] = g;
        }
        return list;
    }

    static Material Mat2(string name, Shader shader)
    {
        string path = "Assets/Udon/Generated/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void BuildHud(Transform parent, PHLocal local)
    {
        var root = new GameObject("Hud");
        root.transform.SetParent(parent, false);
        var canvasGo = new GameObject("HudCanvas");
        canvasGo.transform.SetParent(root.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = canvasGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(700, 220);
        canvasGo.transform.localScale = Vector3.one * 0.0006f;
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(canvasGo.transform, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.fontSize = 34; text.alignment = TextAlignmentOptions.Bottom; text.color = Color.white;
        text.outlineWidth = 0.2f; text.outlineColor = Color.black;
        text.raycastTarget = false;
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        local.hud = text; local.hudRoot = root.transform;
    }

    static TextMeshPro Label(Transform parent, string s, Vector3 localPos, float height, float width)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var t = go.AddComponent<TextMeshPro>();
        t.text = s; t.fontSize = height * 10f; t.alignment = TextAlignmentOptions.Center; t.color = new Color(0.15f, 0.15f, 0.15f);
        t.rectTransform.sizeDelta = new Vector2(width, height * 2f);
        return t;
    }

    // ---- the host panel: a world-space canvas set up as VRChat's UI page asks (VRC Ui Shape, a GraphicRaycaster,
    // a BoxCollider the size of the canvas, layer Default, buttons with Navigation None, text not a raycast
    // target); 1 canvas unit = 2 mm. The canvas faces the room (its -z is towards the players).
    static Transform PanelCanvas(Transform parent, Vector3 localPos, Vector2 size)
    {
        var go = new GameObject("HostPanel");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.layer = 0;
        var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace;
        go.AddComponent<GraphicRaycaster>();
        var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = size;
        go.transform.localScale = Vector3.one * 0.005f;
        var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(size.x, size.y, 1f);
        go.AddComponent<VRC.SDK3.Components.VRCUiShape>();
        var bg = go.AddComponent<Image>(); bg.color = new Color(0.12f, 0.14f, 0.18f, 0.92f); bg.raycastTarget = false;
        return go.transform;
    }

    static TextMeshProUGUI PanelText(Transform canvas, string s, Vector2 at, Vector2 size, float fontSize, TextAlignmentOptions align)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(canvas, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = s; t.fontSize = fontSize; t.alignment = align; t.color = Color.white; t.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>(); rt.anchoredPosition = at; rt.sizeDelta = size;
        return t;
    }

    static void Row(Transform canvas, PHGame game, string title, string action, string[] labels, int[] args, float y)
    {
        PanelText(canvas, title, new Vector2(-590f, y), new Vector2(240f, 60f), 32f, TextAlignmentOptions.Right);
        for (int i = 0; i < labels.Length; i++)
            UiButton(canvas, game, labels[i], action, args[i], new Vector2(-380f + i * 165f, y), new Vector2(150f, 56f), new Color(0.26f, 0.42f, 0.70f));
    }

    static void UiButton(Transform canvas, PHGame game, string label, string action, int arg, Vector2 at, Vector2 size, Color colour)
    {
        var go = new GameObject("Button " + label);
        go.transform.SetParent(canvas, false);
        var img = go.AddComponent<Image>(); img.color = colour;
        var b = go.AddComponent<Button>();
        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
        var rt = go.GetComponent<RectTransform>(); rt.anchoredPosition = at; rt.sizeDelta = size;
        var btn = go.AddUdonSharpComponent<PHButton>();
        btn.game = game; btn.action = action; btn.arg = arg;
        UdonSharpEditorUtility.CopyProxyToUdon(btn);
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(btn);
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(b.onClick, udon.SendCustomEvent, nameof(PHButton.Press));
        PanelText(go.transform, label, Vector2.zero, size, 28f, TextAlignmentOptions.Center);
    }

    static PHAudio BuildAudio(Transform parent)
    {
        var go = new GameObject("Audio");
        go.transform.SetParent(parent, false);
        var a = go.AddUdonSharpComponent<PHAudio>();
        var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Udon/Audio" });
        a.names = new string[guids.Length];
        a.clips = new AudioClip[guids.Length];
        for (int i = 0; i < guids.Length; i++)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[i]));
            a.clips[i] = c; a.names[i] = c.name;
        }
        a.ui = go.AddComponent<AudioSource>();
        a.ui.spatialBlend = 0f; a.ui.playOnAwake = false;
        a.pool = new AudioSource[8];
        for (int i = 0; i < a.pool.Length; i++)
        {
            var s = new GameObject("Source" + i);
            s.transform.SetParent(go.transform, false);
            var src = s.AddComponent<AudioSource>();
            src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 1f; src.maxDistance = 16f; src.playOnAwake = false;
            a.pool[i] = src;
        }
        UdonSharpEditorUtility.CopyProxyToUdon(a);
        return a;
    }

    static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material m, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        if (!collider) Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        else go.layer = 11;
        return go;
    }

    static Material Mat(string name, Color c)
    {
        string path = "Assets/Udon/Generated/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        // Light Volumes' PBR (Standard's properties) so the lobby's lamps light the floor and walls
        var lit = Shader.Find("Light Volume Samples/Light Volume PBR");
        if (m == null) { m = new Material(lit != null ? lit : Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        if (lit != null) m.shader = lit;
        m.color = c; LobbyBuilder.Matte(m, 0f);
        EditorUtility.SetDirty(m);
        return m;
    }
}
