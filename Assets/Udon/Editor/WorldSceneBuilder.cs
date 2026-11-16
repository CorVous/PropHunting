using TMPro;
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
    static readonly Vector3 LobbyOrigin = new Vector3(-120f, 0f, -120f);
    const float RoomW = 24f, RoomD = 22f, WallH = 5.5f, ZoneHalf = 5.5f;
    const int SLOTS = 32;

    [MenuItem("PropHunt/Build world scene")]
    public static void Build()
    {
        if (Object.FindObjectOfType<MapBuilderUdon>(true) == null) PropPrefabBaker.Bake();
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

        // ---- lobby
        var lobby = new GameObject("Lobby");
        lobby.transform.SetParent(world.transform, false);
        lobby.transform.position = LobbyOrigin;
        var floorMat = Mat("LobbyFloor", new Color(0.78f, 0.74f, 0.68f));
        var wallMat = Mat("LobbyWall", new Color(0.86f, 0.84f, 0.80f));
        var zoneMat = Mat("LobbyZone", new Color(0.35f, 0.75f, 0.35f));
        var buttonMat = Mat("StartButton", new Color(0.9f, 0.35f, 0.25f));
        Box(lobby.transform, "Floor", new Vector3(0, -0.25f, 0), new Vector3(RoomW, 0.5f, RoomD), floorMat, true);
        Box(lobby.transform, "WallN", new Vector3(0, WallH / 2, RoomD / 2 + 0.1f), new Vector3(RoomW, WallH, 0.2f), wallMat, true);
        Box(lobby.transform, "WallS", new Vector3(0, WallH / 2, -RoomD / 2 - 0.1f), new Vector3(RoomW, WallH, 0.2f), wallMat, true);
        Box(lobby.transform, "WallE", new Vector3(RoomW / 2 + 0.1f, WallH / 2, 0), new Vector3(0.2f, WallH, RoomD), wallMat, true);
        Box(lobby.transform, "WallW", new Vector3(-RoomW / 2 - 0.1f, WallH / 2, 0), new Vector3(0.2f, WallH, RoomD), wallMat, true);
        Vector3 zoneCentre = new Vector3(0f, 0f, RoomD / 2 - 0.15f - ZoneHalf);
        Box(lobby.transform, "ZoneFloor", zoneCentre + new Vector3(0, 0.005f, 0), new Vector3(ZoneHalf * 2, 0.01f, ZoneHalf * 2), zoneMat, false);
        var zone = new GameObject("Zone");
        zone.transform.SetParent(lobby.transform, false);
        zone.transform.localPosition = zoneCentre + new Vector3(0, 1.5f, 0);
        var zoneBox = zone.AddComponent<BoxCollider>();
        zoneBox.isTrigger = true; zoneBox.size = new Vector3(ZoneHalf * 2, 3f, ZoneHalf * 2);
        zone.layer = 2;   // Ignore Raycast
        var spawn = new GameObject("Spawn");
        spawn.transform.SetParent(lobby.transform, false);
        spawn.transform.localPosition = new Vector3(0f, 0.05f, -RoomD / 2 + 2.2f);

        // ---- the scene descriptor
        var vrcWorld = new GameObject("VRCWorld");
        vrcWorld.transform.SetParent(world.transform, false);
        vrcWorld.transform.position = spawn.transform.position;
        var descriptor = vrcWorld.AddComponent<VRCSceneDescriptor>();
        descriptor.spawns = new[] { spawn.transform };
        descriptor.RespawnHeightY = -50f;
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
        local.sfx = localGo.AddComponent<AudioSource>();
        local.sfx.spatialBlend = 0f; local.sfx.playOnAwake = false;
        BuildHud(localGo.transform, local);
        game.local = local;

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
            var voiceGo = new GameObject("Voice");
            voiceGo.transform.SetParent(gameGo.transform, false);
            voiceGo.name = "Voice" + i;
            var voice = voiceGo.AddComponent<AudioSource>();
            voice.spatialBlend = 1f; voice.rolloffMode = AudioRolloffMode.Linear; voice.maxDistance = 20f; voice.playOnAwake = false;
            slot.voice = voice;
            UdonSharpEditorUtility.CopyProxyToUdon(slot);
            slots[i] = slot;
        }
        game.slots = slots;

        // START, on the north wall above the zone (host only; the game checks)
        var start = Box(lobby.transform, "StartButton", new Vector3(0f, 1.4f, RoomD / 2 - 0.15f), new Vector3(1.2f, 0.5f, 0.2f), buttonMat, true);
        var btn = start.AddUdonSharpComponent<PHButton>();
        btn.game = game; btn.action = nameof(PHGame.StartRound);
        UdonSharpEditorUtility.CopyProxyToUdon(btn);
        Label(lobby.transform, "START (host)", new Vector3(0f, 1.9f, RoomD / 2 - 0.25f), 0.35f);
        Label(lobby.transform, "Stand in the green square to play", new Vector3(0f, 3.4f, RoomD / 2 - 0.25f), 0.45f);

        UdonSharpEditorUtility.CopyProxyToUdon(local);
        UdonSharpEditorUtility.CopyProxyToUdon(game);
        EditorSceneManager.MarkSceneDirty(world.scene);
        Debug.Log("[prophunt] built the world scene: lobby, " + SLOTS + " slots");
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

    static void Label(Transform parent, string s, Vector3 localPos, float height)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var t = go.AddComponent<TextMeshPro>();
        t.text = s; t.fontSize = height * 10f; t.alignment = TextAlignmentOptions.Center; t.color = new Color(0.15f, 0.15f, 0.15f);
        t.rectTransform.sizeDelta = new Vector2(20f, 2f);
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
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.color = c;
        EditorUtility.SetDirty(m);
        return m;
    }
}
