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
            var voiceGo = new GameObject("Voice");
            voiceGo.transform.SetParent(gameGo.transform, false);
            voiceGo.name = "Voice" + i;
            var voice = voiceGo.AddComponent<AudioSource>();
            voice.spatialBlend = 1f; voice.rolloffMode = AudioRolloffMode.Linear; voice.maxDistance = 20f; voice.playOnAwake = false;
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

        // the host panel on the north wall, above the zone (G50, G51): every setting and START. Everyone can read
        // it; only the host's presses count (PHGame checks)
        // a world-space UI canvas (VRC Ui Shape): point and click on desktop, point and pull the trigger in VR
        float wz = RoomD / 2 - 0.12f;
        var canvas = PanelCanvas(lobby.transform, new Vector3(0f, 3.0f, wz), new Vector2(1800f, 560f));
        PanelText(canvas, "Stand in the green square to play. The host sets up the round here.", new Vector2(0f, 245f), new Vector2(1740f, 60f), 40f, TextAlignmentOptions.Center);
        Row(canvas, game, "Size", nameof(PHGame.PanelSize), new[] { "Auto", "S", "M", "L", "XL" }, new[] { -1, 0, 1, 2, 3 }, 160f);
        Row(canvas, game, "Props", nameof(PHGame.PanelDensity), new[] { "Normal", "Sparse", "Dense", "V. sparse", "V. dense" }, new[] { 0, 1, 2, 3, 4 }, 90f);
        Row(canvas, game, "Round", nameof(PHGame.PanelRoundLength), new[] { "Short", "Normal", "Long" }, new[] { 0, 1, 2 }, 20f);
        Row(canvas, game, "Taunt every", nameof(PHGame.PanelTaunt), new[] { "30 s", "40 s", "60 s" }, new[] { 30, 40, 60 }, -50f);
        UiButton(canvas, game, "START", nameof(PHGame.StartRound), 0, new Vector2(-260f, -215f), new Vector2(300f, 80f), new Color(0.85f, 0.32f, 0.22f));
        var settings = PanelText(canvas, "", new Vector2(650f, 20f), new Vector2(440f, 380f), 30f, TextAlignmentOptions.TopLeft);
        local.panelUiText = settings;
        // the last round's results, on the west wall
        var results = Label(lobby.transform, "No round played yet.", new Vector3(-RoomW / 2 + 0.05f, 2.8f, 0f), 0.24f, 14f);
        results.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        results.alignment = TextAlignmentOptions.TopLeft;
        results.rectTransform.sizeDelta = new Vector2(14f, 4f);
        local.resultsText = results;

        UdonSharpEditorUtility.CopyProxyToUdon(local);
        UdonSharpEditorUtility.CopyProxyToUdon(game);
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

    // PHView: the hider's health (desktop low on the screen, VR on the left wrist) and the third-person view
    // (desktop lower right corner, VR a grabbable panel)
    static void BuildView(Transform parent, PHGame game, PHLocal local)
    {
        var viewGo = new GameObject("View");
        viewGo.transform.SetParent(parent, false);
        var view = viewGo.AddUdonSharpComponent<PHView>();
        view.game = game; view.local = local;

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
        HealthBar(desk.transform, sprite, new Vector2(0f, -360f), new Vector2(420f, 26f), out bar, out fill);
        view.desktopHealthBar = bar; view.desktopHealth = fill;
        var pip = new GameObject("Pip");
        pip.transform.SetParent(desk.transform, false);
        pip.layer = 5;
        var raw = pip.AddComponent<RawImage>(); raw.texture = rt; raw.raycastTarget = false; raw.material = hudMat;
        var prt = pip.GetComponent<RectTransform>(); prt.anchoredPosition = new Vector2(560f, -300f); prt.sizeDelta = new Vector2(360f, 240f);
        view.desktopPip = pip;
        view.desktopRoot = desk.transform;

        // VR: the left wrist
        var wrist = new GameObject("Wrist");
        wrist.transform.SetParent(viewGo.transform, false);
        wrist.layer = 5;
        var wc = wrist.AddComponent<Canvas>(); wc.renderMode = RenderMode.WorldSpace;
        wrist.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 40);
        wrist.transform.localScale = Vector3.one * 0.0004f;
        HealthBar(wrist.transform, sprite, Vector2.zero, new Vector2(180f, 26f), out bar, out fill);
        view.wristHealthBar = bar; view.wristHealth = fill;
        view.wristRoot = wrist.transform;

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

        UdonSharpEditorUtility.CopyProxyToUdon(view);
    }

    static Material hudMat;

    static void HealthBar(Transform parent, Sprite sprite, Vector2 at, Vector2 size, out GameObject bar, out Image fill)
    {
        bar = new GameObject("HealthBar");
        bar.transform.SetParent(parent, false);
        bar.layer = 5;
        var bg = bar.AddComponent<Image>(); bg.sprite = sprite; bg.color = new Color(0f, 0f, 0f, 0.55f); bg.raycastTarget = false; bg.material = hudMat;
        var brt = bar.GetComponent<RectTransform>(); brt.anchoredPosition = at; brt.sizeDelta = size;
        var f = new GameObject("Fill");
        f.transform.SetParent(bar.transform, false);
        f.layer = 5;
        fill = f.AddComponent<Image>(); fill.sprite = sprite; fill.color = new Color(0.35f, 0.85f, 0.4f, 0.9f); fill.raycastTarget = false; fill.material = hudMat;
        fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillAmount = 1f;
        var frt = f.GetComponent<RectTransform>(); frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = new Vector2(3, 3); frt.offsetMax = new Vector2(-3, -3);
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
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.color = c;
        EditorUtility.SetDirty(m);
        return m;
    }
}
