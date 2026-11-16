using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Test aid for multi-client runs (switched on only in test builds: PropHunt > Build test world): every client
// plays by itself and logs what it sees, so a run of local VRChat clients checks the networking.
//   all: walk into the zone; the host starts a round once `players` are in it
//   hider: wander a little during HIDE, then stand still; taunt now and then
//   hunter: during HUNT, every few seconds pick a hider's prop, stand 2.5 m from it and shoot; now and then
//           shoot a real prop instead (a wrong prop and a knock)
//   log: "[PHTest] ..." lines with the phase, the roles, and per slot where this client sees the prop, the
//        player (the avatar: parked or not), health, caught; every hit and catch
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHTestPilot : UdonSharpBehaviour
{
    public PHGame game;
    public PHLocal local;
    public int players = 2;
    public int rounds = 2;

    float nextAct, nextLog, zoneAt;
    int startedRounds, lastPhase = -1;
    Vector3 wander;

    void Start()
    {
        zoneAt = Time.time + 6f;
        Log("start me=" + Networking.LocalPlayer.playerId + " master=" + Networking.IsMaster);
    }

    void Log(string s) { Debug.Log("[PHTest] t=" + Networking.GetServerTimeInMilliseconds() + " me=" + Networking.LocalPlayer.playerId + " " + s); }

    void Update()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        int phase = game.phase;
        if (phase != lastPhase)
        {
            lastPhase = phase;
            Log("PHASE " + phase + " round=" + game.round + " role=" + local.Role() + " seed=" + game.seed + " size=" + game.mapSize);
            nextAct = Time.time + 2f;
            wander = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * 1.5f;
        }
        if (phase == PHGame.PH_LOBBY)
        {
            if (zoneAt > 0f && Time.time >= zoneAt && game.lobbyZone != null)
            {
                zoneAt = -1f;
                Vector3 c = game.lobbyZone.bounds.center;
                me.TeleportTo(new Vector3(c.x + Random.Range(-3f, 3f), game.lobbyZone.bounds.min.y + 0.1f, c.z + Random.Range(-3f, 3f)), Quaternion.identity);
            }
            if (game.IsHost && startedRounds < rounds && game.Participants() >= players && Time.time >= nextAct)
            {
                startedRounds++;
                Log("HOST START round " + startedRounds);
                game.StartRound();
            }
            if (game.round > 0 && zoneAt < 0f && !game.InLobbyZone(me.GetPosition())) zoneAt = Time.time + 3f;
        }
        int role = local.Role();
        if (role == PHGame.ROLE_HIDER && phase == PHGame.PH_HIDE && Time.time < nextAct + 4f) me.SetVelocity(new Vector3(wander.x, me.GetVelocity().y, wander.z));
        if (role == PHGame.ROLE_HIDER && phase == PHGame.PH_HUNT && Time.time >= nextAct) { nextAct = Time.time + 12f; local.PilotTaunt(); Log("TAUNT"); }
        if (role == PHGame.ROLE_HUNTER && phase == PHGame.PH_HUNT && Time.time >= nextAct) { nextAct = Time.time + 4f; HunterAct(); }
        if (Time.time >= nextLog) { nextLog = Time.time + 2f; Snapshot(); }
    }

    void HunterAct()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        // one shot in four at a real prop near the release
        if (Random.Range(0, 4) == 0 && game.builder.objects != null)
        {
            GameObject[] objs = game.builder.objects;
            for (int k = 0; k < 20; k++)
            {
                GameObject o = objs[Random.Range(0, objs.Length)];
                if (o == null || o.GetComponent<Rigidbody>() == null) continue;
                Vector3 p = o.transform.position + Vector3.up * 0.3f;
                Shoot(p + new Vector3(0f, 1.2f, -1.5f), p, "PROP " + o.name);
                return;
            }
        }
        for (int i = 0; i < game.slots.Length; i++)
        {
            PHSlot s = game.slots[i];
            if (game.slotPlayer[i] == 0 || game.slotRole[i] != PHGame.ROLE_HIDER || s.caught || !s.hiding || s.def < 0) continue;
            // the middle of the prop as this client draws it
            Vector3 target = s.propPos + Vector3.up * 0.3f;
            Transform proxy = s.proxyRoot.Find("Hider" + i);
            if (proxy != null)
            {
                Renderer r = proxy.GetComponentInChildren<Renderer>();
                if (r != null) target = r.bounds.center;
            }
            // from a clear side within 1.5 m (walls are everywhere indoors), else from just above
            Vector3 from = target + Vector3.up * 0.9f;
            for (int k = 0; k < 8; k++)
            {
                float ang = k * 45f * Mathf.Deg2Rad;
                Vector3 c = target + new Vector3(Mathf.Sin(ang) * 1.5f, 0.4f, Mathf.Cos(ang) * 1.5f);
                if (!Physics.Linecast(c, target + (c - target).normalized * 0.4f, 1 << 11, QueryTriggerInteraction.Ignore)) { from = c; break; }
            }
            me.TeleportTo(new Vector3(from.x, s.propPos.y + 0.1f, from.z), Quaternion.identity);
            Shoot(from, target, "HIDER slot" + i);
            return;
        }
    }

    void Shoot(Vector3 from, Vector3 to, string what)
    {
        local.aimOverride = true;
        local.aimOrigin = from; local.aimDir = (to - from).normalized;
        local.PilotFire();
        local.aimOverride = false;
        PHSlot me = game.LocalSlot();
        Log("SHOT at " + what + " hit " + local.lastHitName + " -> hits=" + (me != null ? me.hits : -1) + " wrong=" + (me != null ? me.wrongProps : -1) + " hp=" + (me != null ? me.hunterHealth : -1));
    }

    void Snapshot()
    {
        string s = "SNAP phase=" + game.phase + " left=" + Mathf.RoundToInt(game.Left) + " ready=" + game.MapReady;
        for (int i = 0; i < game.slots.Length; i++)
        {
            int pid = game.slotPlayer[i];
            if (pid == 0) continue;
            PHSlot sl = game.slots[i];
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
            Vector3 at = Utilities.IsValid(p) ? p.GetPosition() : Vector3.zero;
            s += " | S" + i + " P" + pid + " role=" + game.slotRole[i] + " zone=" + sl.inZone + " hiding=" + sl.hiding + " def=" + sl.def
                + " prop=" + sl.propPos.ToString("F1") + " avatar=" + at.ToString("F1") + " hp=" + sl.health + "/" + sl.maxHealth + " caught=" + sl.caught
                + " hunter=" + sl.hunterHealth + "/" + sl.hits + "/" + sl.wrongProps;
        }
        for (int k = 0; k < game.knocks.Length; k++)
            if (game.knocks[k].emit >= 0) s += " | K" + k + " e" + game.knocks[k].emit + " moving=" + game.knocks[k].moving + " at=" + game.knocks[k].pos.ToString("F2");
        Log(s);
    }
}
