using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using TMPro;

// The local player's side of the game (the prototype's Player and PlayerActions, without its own movement:
// VRChat moves the player). One per world; it acts for whoever is running this client.
//   Hider: Use (desktop click, VR trigger) on a prop within 4 m becomes it (G5, G25); the avatar shrinks to the
//          prop's eye height, sits in its parked station and the synced prop follows them. T taunts.
//   Hunter: Use fires the tagger (05 section 5.3): a hider's prop takes a hit (sent to its owner), a real prop
//          costs health (G17) and gets knocked, anything else is a miss. Hunters walk through props (G45).
//   Everyone: frozen during the reveal; hunters are held at the release during BRIEF and HIDE.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHLocal : UdonSharpBehaviour
{
    public PHGame game;
    public TextMeshProUGUI hud;          // head-locked text
    public Transform hudRoot;
    public PHAudio sound;
    public TextMeshPro panelText;
    public TextMeshPro resultsText;      // the last round's results, on the lobby wall
    public Material beaconGreen, beaconRed;
    public string[] defNames;            // readable names of the defs ("k_chairCushion" -> "Chair cushion"), baked        // the host panel's settings, for everyone to read
    float nextPanel;

    // ---- balance (Balance.cs)
    public const float TAKE_RANGE = 4f, DART_RANGE = 25f, DART_INTERVAL = 0.5f, WRONG_SAME_WINDOW = 5f, TAUNT_COOLDOWN = 8f;
    public const int DART_DAMAGE = 25, HIT_REWARD = 5, HUNTER_HEALTH = 100, HUNT_CHANGES = 3;
    public const float HUNT_CHANGE_COOLDOWN = 5f;
    public const int LAYER_PROPS_SOLID = 11, LAYER_PROPS_WALKTHROUGH = 17;
    readonly int[] hiderHealth = { 50, 100, 150, 200 };

    int role, slotIndex = -1;
    float nextDart, lastChange = -99f, lastTaunt = -99f;
    int changesLeft;
    float originalEye = -1f;
    int lastCharged = -1;
    float lastChargedAt = -99f;
    string notice = "";
    float noticeUntil;
    int propLayer = -1;
    bool wasEndgame;
    // forced taunts (6.4, G47): every hider's own clock from the release of the hunters; a hider that has moved
    // 4 m (straight line) since the last check skips it
    float nextForced = -1f;
    bool warned;
    Vector3 checkPos;
    public const float FORCED_WARNING = 3f, FORCED_MOVED = 4f;

    PHSlot Me() { return slotIndex >= 0 ? game.slots[slotIndex] : null; }

    void Say(string text, float seconds) { notice = text; noticeUntil = Time.time + seconds; }

    // ------------------------------------------------------------------ phases (called by PHGame on every client)
    public void OnPhase(int phase)
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        slotIndex = game.SlotOf(me.playerId);
        PHSlot s = Me();
        role = game.RoleOf(slotIndex);
        if (s != null) s.Claim();
        switch (phase)
        {
            case PHGame.PH_LOBBY:
                ShowReveal(false);
                if (s != null) s.StopHiding();
                RestoreEye();
                SetPropLayer(LAYER_PROPS_SOLID);
                SetFrozen(false);
                if (role == PHGame.ROLE_NONE && game.lobbySpawn != null && game.round > 0) me.TeleportTo(game.lobbySpawn.position, game.lobbySpawn.rotation);
                break;
            case PHGame.PH_LOADING:
                sound.Play("confirmation_001", 0.6f);
                if (s != null && role != PHGame.ROLE_NONE) s.ResetFor(game.round, role);
                Say(role == PHGame.ROLE_NONE ? "A round is starting. You are watching." : "Building the map...", 5f);
                break;
            case PHGame.PH_BRIEF:
                sound.Play("ready", 0.8f);
                if (role == PHGame.ROLE_HUNTER)
                {
                    SetPropLayer(LAYER_PROPS_WALKTHROUGH);
                    me.TeleportTo(game.builder.ReleasePoint() + Vector3.up * 0.1f, Quaternion.Euler(0f, game.builder.ReleaseYaw(), 0f));
                    SetFrozen(true);
                    Say("You hunt. Wait at the release while the hiders hide.", 6f);
                }
                else if (role == PHGame.ROLE_HIDER)
                {
                    SetPropLayer(LAYER_PROPS_SOLID);
                    Vector3 at = game.builder.ReleasePoint();
                    if (game.gen.nSpawns > 0)
                    {
                        int c = game.gen.spawnCell[Random.Range(0, game.gen.nSpawns)];
                        at = new Vector3((c % game.gen.IW + 0.5f) * 0.5f, 0f, (c / game.gen.IW + 0.5f) * 0.5f);
                    }
                    me.TeleportTo(at + Vector3.up * 0.1f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    changesLeft = HUNT_CHANGES;
                    StartingDisguise();
                    if (s != null) s.StartHiding();
                    SetFrozen(true);
                    Say("You hide. Look at a prop and click (trigger) to become it.", 6f);
                }
                break;
            case PHGame.PH_HIDE:
                if (role == PHGame.ROLE_HIDER) SetFrozen(false);
                break;
            case PHGame.PH_HUNT:
                sound.Play("go", 0.8f);
                sound.Play("jingles_PIZZI07", 0.5f);
                if (role == PHGame.ROLE_HUNTER) { SetFrozen(false); Say("Hunt! Find the props that are hiders.", 3f); }
                else if (role == PHGame.ROLE_HIDER)
                {
                    Say("The hunters are released!", 3f);
                    nextForced = Time.time + ForcedInterval() * Random.Range(0.35f, 1f);
                    checkPos = me.GetPosition(); warned = false;
                }
                break;
            case PHGame.PH_END:
                ShowReveal(true);
                sound.Play("time_over", 0.8f);
                if (role != PHGame.ROLE_NONE) sound.Play((role == PHGame.ROLE_HIDER) == game.hidersWin ? "you_win" : "game_over", 0.8f);
                SetFrozen(true);
                Say((game.hidersWin ? "Hiders win. " : "Hunters win. ") + game.endReason, 10f);
                break;
        }
    }

    public void OnSlotChanged(PHSlot s) { }

    // ------------------------------------------------------------------ the reveal (prototype Round.EndRound)
    // The roofs come off and a beam marks every hider: green survived, red caught. The results go on the HUD for
    // the reveal and stay on the lobby wall.
    void ShowReveal(bool on)
    {
        game.builder.ShowRoofs(!on);
        for (int i = 0; i < game.slots.Length; i++)
        {
            PHSlot s = game.slots[i];
            if (s.beacon == null) continue;
            bool show = on && game.slotPlayer[i] != 0 && game.slotRole[i] == PHGame.ROLE_HIDER;
            s.beacon.SetActive(show);
            if (!show) continue;
            s.beacon.transform.position = new Vector3(s.propPos.x, 0f, s.propPos.z);
            s.beaconRenderer.sharedMaterial = s.caught ? beaconRed : beaconGreen;
        }
        if (on && resultsText != null) resultsText.text = Results();
    }

    public string DefName(int d) { return defNames != null && d >= 0 && d < defNames.Length ? defNames[d] : game.gen.defId[d]; }

    string Name(int playerId)
    {
        VRCPlayerApi p = VRCPlayerApi.GetPlayerById(playerId);
        return Utilities.IsValid(p) ? p.displayName : "Player " + playerId;
    }

    string Clock(int ms) { int t = Mathf.Max(0, Mathf.RoundToInt(ms / 1000f)); return (t / 60) + ":" + (t % 60).ToString("00"); }

    string Results()
    {
        string r = (game.hidersWin ? "Hiders win" : "Hunters win") + ": " + game.endReason;
        float huntMs = game.HuntLength * 1000f, sum = 0f;
        int n = 0;
        for (int i = 0; i < game.slots.Length; i++)
        {
            if (game.slotPlayer[i] == 0 || game.slotRole[i] != PHGame.ROLE_HIDER) continue;
            PHSlot s = game.slots[i];
            string what = s.lastDef >= 0 ? DefName(s.lastDef).ToLower() : "prop";
            r += "\n" + Name(game.slotPlayer[i]) + ": " + what + (s.caught ? " - caught after " + Clock(s.survivedMs) : " - survived");
            sum += s.caught ? s.survivedMs : huntMs; n++;
        }
        for (int i = 0; i < game.slots.Length; i++)
        {
            if (game.slotPlayer[i] == 0 || game.slotRole[i] != PHGame.ROLE_HUNTER) continue;
            PHSlot s = game.slots[i];
            r += "\n" + Name(game.slotPlayer[i]) + " (hunter): " + s.hits + " hits, " + s.catches + " catches, " + s.wrongProps + " wrong props, " + s.hunterHealth + " health" + (s.hunterOut ? " (out)" : "");
        }
        // survival share (05 section 8.1)
        if (n > 0) r += "\nSurvival share: " + Mathf.RoundToInt(100f * sum / (n * huntMs)) + "%";
        return r;
    }

    public void Reseat() { PHSlot s = Me(); if (s != null) s.Reseat(); }

    void SetFrozen(bool frozen)
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        me.SetWalkSpeed(frozen ? 0f : 2f);
        me.SetRunSpeed(frozen ? 0f : 4f);
        me.SetStrafeSpeed(frozen ? 0f : 2f);
        me.SetJumpImpulse(frozen ? 0f : 3f);
    }

    // hunters pass through props (G45); hiders and everyone else collide with them
    void SetPropLayer(int layer)
    {
        if (propLayer == layer || game.builder.objects == null) return;
        propLayer = layer;
        GameObject[] objs = game.builder.objects;
        for (int i = 0; i < objs.Length; i++)
        {
            if (objs[i] == null) continue;
            objs[i].layer = layer;
            Transform t = objs[i].transform;
            for (int k = 0; k < t.childCount; k++) t.GetChild(k).gameObject.layer = layer;
        }
    }

    // ------------------------------------------------------------------ disguise
    void StartingDisguise()
    {
        // a random medium type from the map's menu (05 section 3.8)
        int n = game.gen.defDisguise.Length;
        for (int k = 0; k < 60; k++)
        {
            int d = Random.Range(0, n);
            if (d < game.gen.menu.Length && game.gen.menu[d] && game.gen.defDisguise[d] == 2) { SetDisguise(d); return; }
        }
        for (int d = 0; d < n; d++) if (d < game.gen.menu.Length && game.gen.menu[d]) { SetDisguise(d); return; }
    }

    void SetDisguise(int d)
    {
        PHSlot s = Me();
        if (s == null) return;
        int cls = Mathf.Clamp(game.gen.defDisguise[d] - 1, 0, 3);
        int oldMax = s.maxHealth;
        s.maxHealth = hiderHealth[cls];
        s.health = oldMax > 0 ? Mathf.Max(1, Mathf.RoundToInt((float)s.health / oldMax * s.maxHealth)) : s.maxHealth;
        s.def = d; s.lastDef = d;
        s.propPos = Networking.LocalPlayer.GetPosition();
        s.propYaw = Mathf.Round(Networking.LocalPlayer.GetRotation().eulerAngles.y / 15f) * 15f;
        s.Send();
        float h = game.gen.defSy[d] / 1000f;
        SetEye(h > 2.5f ? 1.6f : Mathf.Clamp(Mathf.Round(0.9f * h / 0.05f) * 0.05f, 0.25f, 2.0f));
    }

    void SetEye(float eye)
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (originalEye < 0f) originalEye = me.GetAvatarEyeHeightAsMeters();
        me.SetAvatarEyeHeightByMeters(Mathf.Max(0.1f, eye));
    }

    void RestoreEye()
    {
        if (originalEye < 0f) return;
        Networking.LocalPlayer.SetAvatarEyeHeightByMeters(originalEye);
        originalEye = -1f;
    }

    // ------------------------------------------------------------------ input
    public override void InputUse(bool value, VRC.Udon.Common.UdonInputEventArgs args)
    {
        if (!value) return;
        int phase = game.phase;
        if (role == PHGame.ROLE_HIDER && (phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT)) TryTakeDisguise();
        else if (role == PHGame.ROLE_HUNTER && phase == PHGame.PH_HUNT) Fire();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T)) Taunt();
        bool eg = game.Endgame;
        if (eg && !wasEndgame && role != PHGame.ROLE_NONE)
        {
            sound.Play("hurry_up", 0.8f); sound.Play("jingles_HIT03", 0.6f);
            Say("45 seconds! Taunts come twice as often; wrong props and hits count double.", 4f);
            float iv = game.tauntInterval * 0.5f;
            if (nextForced > Time.time + iv) nextForced = Time.time + iv;
        }
        wasEndgame = eg;
        UpdateForcedTaunt();
        UpdateHud();
        if (panelText != null && Time.time >= nextPanel) { nextPanel = Time.time + 0.5f; panelText.text = game.SettingsText(); }
    }

    // the host's interval; a small group (under 6) three quarters of it, the endgame half (9.4, G53)
    float ForcedInterval()
    {
        if (game.Endgame) return game.tauntInterval * 0.5f;
        return game.Participants() < 6 ? game.tauntInterval * 0.75f : game.tauntInterval;
    }

    void UpdateForcedTaunt()
    {
        PHSlot s = Me();
        if (role != PHGame.ROLE_HIDER || game.phase != PHGame.PH_HUNT || s == null || s.caught || nextForced < 0f) return;
        Vector3 d = Networking.LocalPlayer.GetPosition() - checkPos; d.y = 0f;
        bool skip = d.magnitude >= FORCED_MOVED;
        float t = nextForced - Time.time;
        if (t <= FORCED_WARNING && !warned && !skip)
        {
            warned = true;
            sound.Play("tick_001", 0.8f);
            Say("Forced taunt in 3 s: move 4 m, or taunt now (T)", 3f);
        }
        if (t > 0f) return;
        checkPos = Networking.LocalPlayer.GetPosition();
        nextForced = Time.time + ForcedInterval();
        warned = false;
        if (skip) { Say("You moved: no forced taunt", 2f); return; }
        lastTaunt = -99f;
        Taunt();
    }

    bool Aim(out RaycastHit hit, float range)
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        VRCPlayerApi.TrackingData t = me.IsUserInVR() ? me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand) : me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 dir = me.IsUserInVR() ? t.rotation * Vector3.forward : t.rotation * Vector3.forward;
        return Physics.SphereCast(t.position, 0.04f, dir, out hit, range, ~(1 << 9 | 1 << 10 | 1 << 18), QueryTriggerInteraction.Ignore);
    }

    // the emit index of a map prop that was hit, or -1
    int EmitOf(Transform t)
    {
        Transform root = game.builder.propRoot;
        while (t != null && t.parent != root) t = t.parent;
        if (t == null) return -1;
        GameObject[] objs = game.builder.objects;
        for (int i = 0; i < objs.Length; i++) if (objs[i] == t.gameObject) return i;
        return -1;
    }

    // the slot whose hider prop was hit, or -1
    int HiderOf(Transform t)
    {
        Transform root = game.slots.Length > 0 ? game.slots[0].proxyRoot : null;
        while (t != null && t.parent != root) t = t.parent;
        if (t == null) return -1;
        string n = t.gameObject.name;
        if (!n.StartsWith("Hider")) return -1;
        return int.Parse(n.Substring(5));
    }

    void TryTakeDisguise()
    {
        RaycastHit hit;
        if (!Aim(out hit, TAKE_RANGE)) return;
        int e = EmitOf(hit.collider.transform);
        if (e < 0) return;
        int d = game.gen.emitDef[e];
        if (game.gen.defDisguise[d] <= 0) { Say("That can't be a disguise.", 2f); return; }
        if (game.phase == PHGame.PH_HUNT)
        {
            if (changesLeft <= 0) { Say("No disguise changes left.", 2f); return; }
            if (Time.time - lastChange < HUNT_CHANGE_COOLDOWN) return;
            changesLeft--;
        }
        lastChange = Time.time;
        SetDisguise(d);
        Say("You are a " + DefName(d).ToLower(), 2f);
    }

    void Fire()
    {
        PHSlot s = Me();
        if (s == null || s.hunterOut || Time.time < nextDart) return;
        nextDart = Time.time + DART_INTERVAL;
        sound.Play("switch_002", 0.5f);
        RaycastHit hit;
        if (!Aim(out hit, DART_RANGE)) return;
        int damage = game.Endgame ? DART_DAMAGE * 2 : DART_DAMAGE;
        int h = HiderOf(hit.collider.transform);
        if (h >= 0)
        {
            PHSlot target = game.slots[h];
            if (target.caught) return;
            target.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(PHSlot.TakeHit), damage, Networking.LocalPlayer.playerId);
            s.hits++;
            s.hunterHealth = Mathf.Min(HUNTER_HEALTH, s.hunterHealth + HIT_REWARD);
            if (target.health - damage <= 0) s.catches++;
            s.Send();
            sound.Play("confirmation_002", 0.8f);
            sound.PlayAt("impactPunch_medium_000", hit.point, 14f, 0.8f, 1f);
            Say("Hit!", 1.5f);
            return;
        }
        int e = EmitOf(hit.collider.transform);
        if (e < 0) return;   // a wall or a door: nothing happens
        KnockProp(e, hit.point - Aim0(), hit.point);
        // a real prop: it costs health (G17), but the same prop again within 5 s costs nothing
        if (e == lastCharged && Time.time - lastChargedAt < WRONG_SAME_WINDOW) return;
        lastCharged = e; lastChargedAt = Time.time;
        int penalty = game.wrongPenalty;
        if (game.Participants() < 6) penalty = (penalty + 1) / 2;
        if (game.Endgame) penalty *= 2;
        s.wrongProps++;
        s.hunterHealth -= penalty;
        if (s.hunterHealth <= 0) { s.hunterHealth = 0; s.hunterOut = true; Say("Out of health: you are out for the round.", 4f); SetFrozen(true); }
        else Say("Just a " + DefName(game.gen.emitDef[e]).ToLower() + ". -" + penalty + " health", 2f);
        s.Send();
        sound.Play("error_006", 0.6f);
    }

    Vector3 Aim0()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        return (me.IsUserInVR() ? me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand) : me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head)).position;
    }

    // a dart knocks a real prop (04 section 4.8): LIGHT and KNOCKABLE props fly, heavy ones only thud
    void KnockProp(int e, Vector3 dir, Vector3 point)
    {
        int fl = game.gen.defFlags[game.gen.emitDef[e]];
        if ((fl & (GenData.FL_HEAVY | GenData.FL_FIXTURE | GenData.FL_SCENERY)) != 0) return;
        PHKnock k = game.KnockOf(e);
        if (k != null && k.Busy) return;   // already flying
        if (k == null) k = game.FreeKnock();
        if (k != null) k.Knock(e, dir, point);
    }

    // test aid (editor, MCP): knock prop testKnockEmit as if a dart hit it from the south
    [HideInInspector] public int testKnockEmit;
    public void TestKnock()
    {
        GameObject go = game.builder.objects[testKnockEmit];
        if (go != null) KnockProp(testKnockEmit, Vector3.forward, go.transform.position + Vector3.up * 0.2f);
    }

    void Taunt()
    {
        PHSlot s = Me();
        if (s == null || role != PHGame.ROLE_HIDER || s.caught || game.phase != PHGame.PH_HUNT) return;
        if (Time.time - lastTaunt < TAUNT_COOLDOWN) return;
        lastTaunt = Time.time;
        s.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PHSlot.PlayTaunt), Random.Range(0, 1000));
    }

    // a hunter's dart reached this player's prop (PHSlot.TakeHit, on the owner)
    public void OnHit(int damage, bool nowCaught, int shooter)
    {
        PHSlot s = Me();
        if (nowCaught)
        {
            Say("You were caught! You stay hidden as a ghost until the round ends.", 5f);
            sound.Play("you_lose", 0.8f);
            s.def = -1; s.Send();
            RestoreEye();
        }
        else Say("Hit! " + (s != null ? s.health.ToString() : "?") + " health left. Move!", 2.5f);
    }

    // ------------------------------------------------------------------ HUD
    void UpdateHud()
    {
        if (hud == null) return;
        PHSlot s = Me();
        string t = "";
        int phase = game.phase;
        if (phase == PHGame.PH_LOBBY)
            t = "Lobby: stand in the green zone to play. " + game.Participants() + " in the zone.";
        else if (phase == PHGame.PH_LOADING)
            t = "Loading the map... " + Mathf.RoundToInt(game.gen.progressPermille / 10f) + "%";
        else
        {
            string ph = phase == PHGame.PH_BRIEF ? "Get ready" : (phase == PHGame.PH_HIDE ? "Hide" : (phase == PHGame.PH_HUNT ? (game.Endgame ? "Hunt (endgame)" : "Hunt") : "Round over"));
            int left = Mathf.CeilToInt(game.Left);
            t = ph + "  " + (left / 60) + ":" + (left % 60).ToString("00");
            if (s != null && role == PHGame.ROLE_HIDER) t += "\nHealth " + s.health + "/" + s.maxHealth + (s.caught ? " (caught)" : "") + "  changes " + changesLeft;
            if (s != null && role == PHGame.ROLE_HUNTER) t += "\nHealth " + s.hunterHealth + "  hits " + s.hits + "  wrong " + s.wrongProps + (s.hunterOut ? " (out)" : "");
        }
        if (phase == PHGame.PH_END && resultsText != null) t += "\n" + resultsText.text;
        if (Time.time < noticeUntil) t += "\n" + notice;
        hud.text = t;
    }

    public override void PostLateUpdate()
    {
        if (hudRoot == null) return;
        VRCPlayerApi.TrackingData head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        hudRoot.SetPositionAndRotation(head.position + head.rotation * new Vector3(0f, -0.18f, 0.6f), head.rotation);
    }
}
