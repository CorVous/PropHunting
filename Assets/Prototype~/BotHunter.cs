using System.Collections.Generic;
using UnityEngine;

// A bot hunter for "hide" rounds: patrols the map, walks to taunts and other noises, shoots props it sees
// moving, and now and then guesses at a still prop. A wrong prop costs it health like any hunter (G17);
// at 0 it is out for the round.
public class BotHunter : MonoBehaviour
{
    public int health = Balance.HunterHealth;
    public bool alive = true;
    public string label = "";
    public int wrongProps, hitsLanded;

    const int IDLE = 0, PATROL = 1, INVESTIGATE = 2;
    int state = IDLE;
    List<int> path;
    int pathIndex;
    Vector3 noisePos;
    int guessesLeft;
    float nextGuess, nextLook, nextShot, pauseUntil;
    static readonly Collider[] near = new Collider[48];

    public Vector3 Eye { get { return transform.position + Vector3.up * Balance.HunterEye; } }

    public void Release()
    {
        state = PATROL; path = null;
        nextGuess = Time.time + Random.Range(3f, Balance.BotHunterGuessInterval);
    }

    // a taunt, a running prop or a disguise change within earshot
    public void Hear(Vector3 pos)
    {
        if (!alive || state == IDLE) return;
        noisePos = pos; state = INVESTIGATE; guessesLeft = 1;
        GoTo(pos);
    }

    void GoTo(Vector3 pos)
    {
        var map = Game.I.map;
        int from = map.CellAt(transform.position), to = NearestWalkable(pos);
        path = map.FindPath(from, to, false, true);
        pathIndex = 1;
    }

    int NearestWalkable(Vector3 pos)
    {
        var map = Game.I.map; var g = map.gen;
        int c = map.CellAt(pos);
        if (c >= 0 && g.walk[c] == 1 && g.reach[c] != 0) return c;
        int cx = Mathf.Clamp(Mathf.FloorToInt(pos.x / 0.5f), 0, g.IW - 1), cz = Mathf.Clamp(Mathf.FloorToInt(pos.z / 0.5f), 0, g.IH - 1);
        for (int r = 1; r < 12; r++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= g.IW || z >= g.IH) continue;
                    int i = z * g.IW + x;
                    if (g.walk[i] == 1 && g.reach[i] != 0) return i;
                }
        return map.CellAt(transform.position);
    }

    void Update()
    {
        var round = Game.I.round;
        if (!alive || round.phase != Round.PH_HUNT || state == IDLE) return;
        float dt = Time.deltaTime;
        if (Time.time >= nextLook)
        {
            nextLook = Time.time + 0.3f;
            LookForMovers();
        }
        if (Time.time < pauseUntil) return;

        // walk the path
        if (path != null && pathIndex < path.Count)
        {
            Vector3 target = Game.I.map.CellCentre(path[pathIndex]);
            Vector3 p = transform.position;
            Vector3 np = Vector3.MoveTowards(p, target, 3.2f * dt);
            Vector3 dir = target - p; dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 420f * dt);
            // no footsteps (G41)
            transform.position = np;
            if ((np - target).sqrMagnitude < 0.0004f) pathIndex++;
            if (state == INVESTIGATE && Vector3.Distance(np, noisePos) < 2.5f) path = null;
        }
        else if (state == INVESTIGATE)
        {
            Investigate();
        }
        else
        {
            // patrol: somewhere new
            var g = Game.I.map.gen;
            for (int k = 0; k < 30; k++)
            {
                int i = Random.Range(0, g.IW * g.IH);
                if (g.walk[i] != 1 || g.reach[i] == 0) continue;
                path = Game.I.map.FindPath(Game.I.map.CellAt(transform.position), i, false, true);
                pathIndex = 1;
                break;
            }
        }
        if (state == PATROL && Time.time >= nextGuess)
        {
            nextGuess = Time.time + Balance.BotHunterGuessInterval * Random.Range(0.7f, 1.4f);
            GuessNear(transform.position, 5f, false);
        }
    }

    void Investigate()
    {
        // open furniture next to the noise, then guess at what is there
        int n = Physics.OverlapSphereNonAlloc(noisePos, 3f, near, 1 << Layers.Door, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var op = near[i].GetComponentInParent<Openable>();
            if (op != null && !op.open)
            {
                op.Toggle(); op.lastOpenedByHunter = Time.time;
                AudioHub.I.PlayAt("doorOpen_1", op.transform.position, 12f, 0.8f);
                pauseUntil = Time.time + 0.5f;
                return;
            }
        }
        if (guessesLeft > 0)
        {
            guessesLeft--;
            GuessNear(noisePos, 4f, true);
            pauseUntil = Time.time + 0.9f;
            return;
        }
        state = PATROL; path = null;
    }

    bool Sees(Vector3 point)
    {
        Vector3 e = Eye;
        return !Physics.Linecast(e, point, Layers.CameraMask, QueryTriggerInteraction.Ignore);
    }

    // props seen moving are shot at once
    void LookForMovers()
    {
        if (Time.time < nextShot) return;
        var round = Game.I.round;
        var pl = Game.I.player;
        if (pl.role == Player.ROLE_HIDER && pl.def >= 0 && !pl.settled && !pl.locked && pl.MovedRecently)
        {
            Vector3 c = pl.body.Centre;
            if (Vector3.Distance(c, Eye) < Balance.BotHunterSight && Sees(c)) { ShootAt(c, Balance.BotHunterMoverScatter); return; }
        }
        foreach (var h in round.hiders)
        {
            if (!h.alive || !h.moving) continue;
            Vector3 c = h.Centre;
            if (Vector3.Distance(c, Eye) < Balance.BotHunterSight && Sees(c)) { ShootAt(c, Balance.BotHunterMoverScatter); return; }
        }
    }

    // pick one prop near a point and shoot it; hiders standing there are among the candidates
    void GuessNear(Vector3 centre, float radius, bool afterNoise)
    {
        if (Time.time < nextShot) return;
        // a guess costs health when it is wrong (G17): no guess that could put this hunter out, and no idle
        // guesses on patrol once health is low
        int penalty = Game.I.round.Penalty;
        if (health <= penalty || (!afterNoise && health <= Balance.BotHunterIdleGuessHealth)) return;
        var pl = Game.I.player;
        int n = Physics.OverlapSphereNonAlloc(centre + Vector3.up * 0.5f, radius, near, (1 << Layers.Prop) | (1 << Layers.Solid) | (1 << Layers.Hider) | (1 << Layers.HiderSolid), QueryTriggerInteraction.Ignore);
        float total = 0f;
        var cands = new List<Vector3>();
        var weights = new List<float>();
        var hiderAt = new List<int>();
        for (int i = 0; i < n; i++)
        {
            var pi = near[i].GetComponentInParent<PropInstance>();
            var bh = near[i].GetComponentInParent<BotHider>();
            if (pi == null && bh == null) continue;
            if (pi != null && (GenData.defDisguise[pi.def] == 0 || !Game.I.map.gen.menu[pi.def])) continue;   // not a possible disguise
            Vector3 c = near[i].bounds.center;
            if (!Sees(c)) continue;
            if (bh != null) hiderAt.Add(cands.Count);
            cands.Add(c); weights.Add(1f); total += 1f;
        }
        if (pl.role == Player.ROLE_HIDER && pl.def >= 0)
        {
            Vector3 c = pl.body.Centre;
            if (Vector3.Distance(c, centre) < radius + 0.5f && Sees(c))
            {
                float w = pl.settled || pl.locked ? 1f : 6f;
                hiderAt.Add(cands.Count);
                cands.Add(c); weights.Add(w); total += w;
            }
        }
        if (cands.Count == 0) return;
        if (afterNoise && hiderAt.Count > 0 && hiderAt.Count < cands.Count)
        {
            // the sound gave the place away: the hiders standing there share BotHunterNoiseHit of the guess
            float props = total;
            foreach (int i in hiderAt) props -= weights[i];
            float each = props * Balance.BotHunterNoiseHit / (1f - Balance.BotHunterNoiseHit) / hiderAt.Count;
            foreach (int i in hiderAt) { total += each - weights[i]; weights[i] = each; }
        }
        float r = Random.value * total;
        for (int i = 0; i < cands.Count; i++)
        {
            if (r < weights[i]) { ShootAt(cands[i]); return; }
            r -= weights[i];
        }
    }

    // after a hit the bot needs a moment before its next dart, so a hider that is found can run
    public void HitPause()
    {
        nextShot = Mathf.Max(nextShot, Time.time + Balance.BotHunterFollowUp);
    }

    void ShootAt(Vector3 point, float scatter = 1f)
    {
        nextShot = Time.time + Balance.DartInterval + 0.3f;
        Vector3 e = Eye - Vector3.up * 0.2f;
        Vector3 flat = point - transform.position; flat.y = 0f;
        if (flat.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(flat);
        float dist = Vector3.Distance(e, point);
        Vector3 aim = point + Random.insideUnitSphere * (Balance.BotHunterAimError * scatter * dist / 10f);
        AudioHub.I.PlayAt("pluck_002", e, 18f, 0.8f, Random.Range(0.9f, 1.05f));
        Game.I.round.BotShot(this, e, (aim - e).normalized);
        pauseUntil = Mathf.Max(pauseUntil, Time.time + 0.35f);
    }
}
