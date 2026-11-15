using System.Collections.Generic;
using UnityEngine;

// Round, part 2: where bot hiders hide. A bot looks for a place where a real prop of its type would be:
// an empty slot the generator left (a missing chair at a table, a free place on a shelf, 04 section 4.7.4),
// or a floor cell that matches the type's anchor in a room where the type is native.
public partial class Round
{
    class Sock
    {
        public Vector3 pos; public float yaw; public int accept, size, type, host; public bool used, hiding;
    }

    readonly List<Sock> socks = new List<Sock>();

    void AddSock(int host, int sl, bool hiding)
    {
        var map = Map;
        if (host < 0 || map.objects[host] == null) return;
        Transform ht = map.objects[host].transform;
        var s = new Sock();
        s.host = host; s.type = GenData.slotType[sl]; s.accept = GenData.slotAccept[sl]; s.size = GenData.slotSize[sl]; s.hiding = hiding;
        Vector3 local = new Vector3(GenData.slotX[sl], GenData.slotY[sl], GenData.slotZ[sl]) / 1000f;
        s.pos = ht.position + Quaternion.Euler(0f, map.gen.emitYaw[host], 0f) * local;
        s.yaw = map.gen.emitYaw[host] + GenData.slotYaw[sl];
        socks.Add(s);
    }

    readonly List<int> surfaceHosts = new List<int>();   // emits with a top that small things stand on

    void BuildSockets()
    {
        socks.Clear();
        var g = Map.gen;
        surfaceHosts.Clear();
        for (int e = 0; e < g.nEmit; e++)
        {
            int d = g.emitDef[e];
            if (g.emitParent[e] >= 0 || (GenData.defFlags[d] & GenData.FL_SOLID) == 0) continue;
            if (GenData.defTopY[d] < 250 || GenData.defTopY[d] > 1300) continue;
            bool top = false;
            for (int k = GenData.defSlotStart[d]; k < GenData.defSlotStart[d] + GenData.defSlotCount[d]; k++)
                if (GenData.slotType[k] == GenData.S_ON_TOP) top = true;
            if (top) surfaceHosts.Add(e);
        }
        for (int k = 0; k < g.nSockets; k++) AddSock(g.socketHost[k], g.socketSlot[k], true);
        for (int k = 0; k < g.nFree; k++) AddSock(g.freeHost[k], g.freeSlot[k], false);
    }

    void ResetSockets()
    {
        foreach (var s in socks) s.used = false;
    }

    bool SockFree(Sock s)
    {
        if (s.used) return false;
        if (s.type != GenData.S_ADJACENT) return true;
        int c = Map.CellAt(s.pos);
        return c >= 0 && Map.gen.occ[c] == 0 && Map.gen.walk[c] == 1;
    }

    // a hider's prop that settles within 0.3 m of an empty socket accepting its type is pinned into it
    public void SnapToSocket(Player p)
    {
        if (p.def < 0) return;
        int tags = GenData.defTags[p.def];
        if (tags == 0) return;
        foreach (var s in socks)
        {
            if ((s.accept & tags) == 0 || s.size < GenData.defSizeClass[p.def] || !SockFree(s)) continue;
            Vector3 d = s.pos - p.body.pos;
            if (new Vector2(d.x, d.z).magnitude > 0.3f || Mathf.Abs(d.y) > 0.2f) continue;
            if (!p.body.Fits(s.pos + Vector3.up * 0.01f, s.yaw)) continue;
            p.body.pos = s.pos; p.body.yaw = s.yaw;
            p.Say("Snapped into the empty place", 2f);
            AudioHub.I.Play("tick_001", 0.5f);
            return;
        }
    }

    string HostName(int host)
    {
        return GameUI.DefName(Map.gen.emitDef[host]).ToLowerInvariant();
    }

    string RoomNameAt(Vector3 pos)
    {
        int c = Map.CellAt(pos);
        if (c < 0 || Map.gen.innerRoom[c] < 0) return "map";
        return GenData.RoomName(Map.gen.roomType[Map.gen.innerRoom[c]]).ToLowerInvariant();
    }

    // Finds a plausible pose for a prop of this type. Returns false if nothing was found.
    bool PickSpot(int def, out Vector3 pos, out float yaw, out string note, out Sock sock)
    {
        var g = Map.gen;
        sock = null; pos = Vector3.zero; yaw = 0f; note = "";
        int tags = GenData.defTags[def];
        Vector3 rel = Map.ReleasePoint();
        // 1. an empty slot that accepts the type: hiding sockets first
        if (tags != 0)
        {
            Sock best = null;
            int seen = 0;
            for (int pass = 0; pass < 2 && best == null; pass++)
                foreach (var s in socks)
                {
                    if (s.hiding != (pass == 0)) continue;
                    if ((s.accept & tags) == 0 || s.size < GenData.defSizeClass[def] || !SockFree(s)) continue;
                    if (Vector3.Distance(s.pos, rel) < 6f) continue;
                    seen++;
                    if (Random.Range(0, seen) == 0) best = s;   // uniform pick while scanning
                }
            if (best != null)
            {
                sock = best; pos = best.pos; yaw = best.yaw + Random.Range(-8f, 8f);
                string where = best.type == GenData.S_ADJACENT ? "at" : (best.type == GenData.S_INSIDE ? "in" : "on");
                note = where + " a " + HostName(best.host) + " in the " + RoomNameAt(pos);
                return true;
            }
        }
        // 2. small things that live on furniture: a free place on some table top, counter or cabinet
        if (GenData.defAnchor[def] == GenData.A_SLOT_ONLY && GenData.defSizeClass[def] <= GenData.SZ_M && surfaceHosts.Count > 0)
        {
            float half = Mathf.Max(GenData.defSx[def], GenData.defSz[def]) / 2000f;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                int host = surfaceHosts[Random.Range(0, surfaceHosts.Count)];
                int hd = g.emitDef[host];
                Transform ht = Map.objects[host].transform;
                if (Map.props[host] != null && Map.props[host].displaced) continue;
                if (Vector3.Distance(ht.position, rel) < 6f) continue;
                float mx = GenData.defSx[hd] / 2000f - half - 0.06f, mz = GenData.defSz[hd] / 2000f - half - 0.06f;
                if (mx < 0.02f || mz < 0.02f) continue;
                Vector3 local = new Vector3(Random.Range(-mx, mx), GenData.defTopY[hd] / 1000f, Random.Range(-mz, mz));
                Vector3 p = ht.position + ht.rotation * local;
                bool clear = true;
                for (int e = 0; e < g.nEmit && clear; e++)
                    if (g.emitParent[e] == host && Vector3.Distance(Map.objects[e].transform.position, p) < half + 0.16f) clear = false;
                foreach (var h in hiders) if (h != null && h.alive && Vector3.Distance(h.transform.position, p) < half + 0.25f) clear = false;
                if (!clear) continue;
                pos = p; yaw = Random.Range(0f, 360f);
                note = "on a " + HostName(host) + " in the " + RoomNameAt(pos);
                return true;
            }
        }
        // 3. a floor cell matching the type's anchor, in a room where the type is native if possible
        //    (a chair with no table to go to stands against a wall)
        int anchor = GenData.defAnchor[def];
        if (anchor == GenData.A_SLOT_ONLY) anchor = GenData.A_WALL;
        int w = GenData.defW[def], d = GenData.defD[def];
        float sx = GenData.defSx[def] / 1000f, sz = GenData.defSz[def] / 1000f;
        int n = g.IW * g.IH;
        for (int attempt = 0; attempt < 600; attempt++)
        {
            int i = Random.Range(0, n);
            int t = g.tags[i];
            if ((t & GenData.T_FLOOR) == 0 || g.reach[i] == 0 || g.occ[i] != 0) continue;
            int room = g.innerRoom[i];
            if (attempt < 400 && !g.IsNativeRoom(def, room)) continue;
            bool outdoor = (t & GenData.T_OUTDOOR) != 0;
            // fallback rooms: still nothing indoor-only outdoors, nothing outdoor-only indoors
            if (attempt >= 400 && (GenData.defFlags[def] & (outdoor ? GenData.FL_LEAK_OUT : GenData.FL_LEAK_IN)) == 0) continue;
            int cx = i % g.IW, cz = i / g.IW;
            Vector3 centre = new Vector3((cx + 0.5f) * 0.5f, 0f, (cz + 0.5f) * 0.5f);
            if (Vector3.Distance(centre, rel) < 6f) continue;
            int rot, x0, z0, gw, gd;
            bool wall = (anchor == GenData.A_WALL || anchor == GenData.A_OUT_EDGE || anchor == GenData.A_CORNER) && attempt < 500;
            int wd = -1;
            if (wall)
            {
                int d0 = Random.Range(0, 4);
                for (int k = 0; k < 4 && wd < 0; k++) if ((t & MapGen.WallBit((d0 + k) & 3)) != 0) wd = (d0 + k) & 3;
                if (wd < 0) continue;
                rot = (wd + 2) & 3;
                gw = (rot & 1) == 0 ? w : d; gd = (rot & 1) == 0 ? d : w;
                if (wd == 2) { x0 = cx; z0 = cz; }
                else if (wd == 0) { x0 = cx; z0 = cz - gd + 1; }
                else if (wd == 3) { x0 = cx; z0 = cz; }
                else { x0 = cx - gw + 1; z0 = cz; }
            }
            else
            {
                if (anchor == GenData.A_SOFT && (t & GenData.T_SOFT_GROUND) == 0 && attempt < 400) continue;
                if (anchor == GenData.A_CENTER && (t & GenData.T_CENTER) == 0 && attempt < 400) continue;
                rot = Random.Range(0, 4);
                gw = (rot & 1) == 0 ? w : d; gd = (rot & 1) == 0 ? d : w;
                x0 = cx; z0 = cz;
            }
            if (!RectClear(x0, z0, gw, gd, room, wall ? wd : -1)) continue;
            float px = (x0 + gw * 0.5f) * 0.5f, pz = (z0 + gd * 0.5f) * 0.5f;
            if (wall)
            {
                float off = 0.1f + 0.02f + sz / 2f;
                if (wd == 2) pz = z0 * 0.5f + off;
                else if (wd == 0) pz = (z0 + gd) * 0.5f - off;
                else if (wd == 3) px = x0 * 0.5f + off;
                else px = (x0 + gw) * 0.5f - off;
            }
            pos = new Vector3(px, 0f, pz);
            float jitter = GenData.defJitYaw[def];
            yaw = rot * 90f + Random.Range(-jitter, jitter) * (wall ? 0.3f : 1f);
            note = (wall ? "against a wall in the " : "on the floor of the ") + RoomNameAt(pos);
            return true;
        }
        return false;
    }

    bool RectClear(int x0, int z0, int gw, int gd, int room, int wallDir)
    {
        var g = Map.gen;
        if (x0 < 0 || z0 < 0 || x0 + gw > g.IW || z0 + gd > g.IH) return false;
        for (int z = z0; z < z0 + gd; z++)
            for (int x = x0; x < x0 + gw; x++)
            {
                int i = z * g.IW + x;
                int t = g.tags[i];
                if ((t & GenData.T_FLOOR) == 0 || g.innerRoom[i] != room || g.occ[i] != 0 || g.reserve[i] != 0) return false;
                if ((t & (GenData.T_DOOR_CLEAR | GenData.T_NO_SPAWN | GenData.T_PATH | GenData.T_SWING)) != 0) return false;
                if (wallDir >= 0)
                {
                    // the back row stands against the wall
                    bool back = (wallDir == 2 && z == z0) || (wallDir == 0 && z == z0 + gd - 1) || (wallDir == 3 && x == x0) || (wallDir == 1 && x == x0 + gw - 1);
                    if (back && (t & MapGen.WallBit(wallDir)) == 0) return false;
                }
            }
        // not on top of another bot
        Vector3 c = new Vector3((x0 + gw * 0.5f) * 0.5f, 0f, (z0 + gd * 0.5f) * 0.5f);
        foreach (var h in hiders) if (h != null && h.alive && Vector3.Distance(h.transform.position, c) < 1.2f) return false;
        return true;
    }

    // bot hiders pick disguises from the props on the map: the round's menu, weighted by class and copies
    int PickDisguise()
    {
        var g = Map.gen;
        float total = 0f;
        for (int d = 0; d < GenData.nDefs; d++) total += DisguiseWeight(d);
        if (total <= 0f) return -1;
        float r = Random.value * total;
        for (int d = 0; d < GenData.nDefs; d++)
        {
            float w = DisguiseWeight(d);
            if (w <= 0f) continue;
            if (r < w) return d;
            r -= w;
        }
        return -1;
    }

    float DisguiseWeight(int d)
    {
        var g = Map.gen;
        if (!g.menu[d]) return 0f;
        int cls = GenData.defDisguise[d];
        float w = cls == 1 ? 3f : (cls == 2 ? 5f : 1.2f);
        return w * Mathf.Min(g.defCopies[d], 10);
    }

    void SpawnBotHider(int index)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            int def = PickDisguise();
            if (def < 0) return;
            Vector3 pos; float yaw; string note; Sock sock;
            if (!PickSpot(def, out pos, out yaw, out note, out sock)) continue;
            if (sock != null) sock.used = true;
            var go = new GameObject("BotHider " + GenData.defId[def]);
            var bh = go.AddComponent<BotHider>();
            bh.Init(def, pos, yaw, "Hider " + (index + 1), note);
            hiders.Add(bh);
            return;
        }
    }

    // move a bot to a new hiding place: walking after a taunt, running after a hit
    void RelocateBot(BotHider h, bool run)
    {
        if (!h.alive) return;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector3 pos; float yaw; string note; Sock sock;
            if (!PickSpot(h.def, out pos, out yaw, out note, out sock)) continue;
            float dist = Vector3.Distance(pos, h.transform.position);
            if (dist < 3f || dist > (run ? 30f : 18f)) continue;
            int from = Map.CellAt(h.transform.position), to = Map.CellAt(pos);
            var path = Map.FindPath(from, to, false);
            if (path == null) continue;
            if (sock != null) sock.used = true;
            h.Relocate(path, pos, yaw, run, note);
            return;
        }
    }
}
