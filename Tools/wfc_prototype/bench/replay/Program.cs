using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;

// Replays the prototype's collapses: the same restrictions and decisions on the same grids, with the same
// propagation (a stack of changed cells, each neighbour's options cut to what the cell's options allow), the scan
// for the cell with the fewest options at every decision, and a weight per option.
class Wave {
    public int W, H, NT, border; public int[] walls; public UInt128[][] compat; public UInt128[] wave; public List<(char, int, UInt128)> ev = new();
    int[] stack; int sp; bool failed; public long visits, decisions;
    static readonly int[] DX = { 0, 1, 0, -1 }, DZ = { 1, 0, -1, 0 };
    public bool memo; Dictionary<UInt128, UInt128>[] cache;

    void Restrict(int c, UInt128 mask) {
        UInt128 old = wave[c], nw = old & mask;
        if (nw == old) return;
        wave[c] = nw;
        if (nw == 0) { failed = true; return; }
        stack[sp++] = c;
    }
    UInt128 AllowedFrom(UInt128 m, int d) {
        UInt128 o = 0; var comp = compat[d];
        ulong lo = (ulong)m, hi = (ulong)(m >> 64);
        while (lo != 0) { int t = BitOperations.TrailingZeroCount(lo); o |= comp[t]; lo &= lo - 1; }
        while (hi != 0) { int t = BitOperations.TrailingZeroCount(hi); o |= comp[64 + t]; hi &= hi - 1; }
        return o;
    }
    void Propagate() {
        while (sp > 0 && !failed) {
            int c = stack[--sp]; int x = c % W, z = c / W; UInt128 m = wave[c];
            for (int d = 0; d < 4; d++) {
                int nx = x + DX[d], nz = z + DZ[d];
                if (nx < 0 || nz < 0 || nx >= W || nz >= H || (walls != null && (walls[c] >> d & 1) != 0)) continue;
                visits++;
                Restrict(nz * W + nx, AllowedFrom(m, d));
            }
        }
        sp = 0;
    }
    static int Pop(UInt128 m) { return BitOperations.PopCount((ulong)m) + BitOperations.PopCount((ulong)(m >> 64)); }
    public long sink;
    public void Run() {
        int n = W * H;
        wave = new UInt128[n]; stack = new int[n * 8 + 64]; sp = 0; failed = false;
        if (memo) { cache = new Dictionary<UInt128, UInt128>[4]; for (int d = 0; d < 4; d++) cache[d] = new(); }
        UInt128 full = (UInt128.One << NT) - 1;
        for (int i = 0; i < n; i++) wave[i] = full;
        for (int z = 0; z < H; z++) for (int x = 0; x < W; x++) for (int d = 0; d < 4; d++) {
            int nx = x + DX[d], nz = z + DZ[d];
            if (nx < 0 || nz < 0 || nx >= W || nz >= H || (walls != null && (walls[z * W + x] >> d & 1) != 0)) { if (stack.Length - sp < 8) Propagate(); Restrict(z * W + x, compat[(d + 2) & 3][border]); }
        }
        Propagate();
        int[] todo = new int[n]; int nTodo = n; for (int i = 0; i < n; i++) todo[i] = i;
        foreach (var (k, c, m) in ev) {
            if (failed) break;
            if (k == 'r') { if (stack.Length - sp < 8) Propagate(); Restrict(c, m); }
            else if (k == 'p') Propagate();
            else {
                decisions++;
                // the scan for the cell with the fewest options, beside something decided
                int best = 999, bi = -1;
                if (memo) {
                    // the undecided cells kept as a list, no look at the neighbours (as the item pass does)
                    int k2 = 0;
                    for (int j = 0; j < nTodo; j++) { int i = todo[j]; UInt128 w = wave[i]; if ((w & (w - 1)) == 0) continue; todo[k2++] = i; int e = Pop(w); if (e < best) { best = e; bi = i; } }
                    nTodo = k2;
                } else
                for (int i = 0; i < n; i++) {
                    UInt128 w = wave[i];
                    if ((w & (w - 1)) == 0) continue;
                    int e = Pop(w);
                    int x = i % W, z = i / W; bool near = false;
                    for (int d = 0; d < 4; d++) { int nx = x + DX[d], nz = z + DZ[d]; if (nx < 0 || nz < 0 || nx >= W || nz >= H) near = true; else { UInt128 q = wave[nz * W + nx]; if ((q & (q - 1)) == 0) near = true; } }
                    if (!near) e += 50;
                    if (e < best) { best = e; bi = i; }
                }
                sink += bi;
                // a weight for every option of the cell: its neighbours looked at
                UInt128 opts = wave[c]; int cx = c % W, cz = c / W; long tot = 0;
                for (int t = 0; t < NT; t++) {
                    if (((opts >> t) & 1) == 0) continue;
                    int same = 0;
                    for (int d = 0; d < 4; d++) { int nx = cx + DX[d], nz = cz + DZ[d]; if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue; UInt128 q = wave[nz * W + nx]; if (q == (UInt128.One << t)) same++; }
                    tot += 100 * (1 + 3 * same);
                }
                sink += tot;
                Restrict(c, UInt128.One << (int)m); Propagate();
            }
        }
    }
}
class Program {
    static List<List<Wave>> Load(string path) {
        var maps = new List<List<Wave>>(); Wave w = null;
        foreach (var line in File.ReadLines(path)) {
            if (line.Length == 0) continue;
            char k = line[0];
            if (k == 'M') { maps.Add(new List<Wave>()); continue; }
            var p = line.Split(' ');
            if (k == 'W') { w = new Wave { W = int.Parse(p[1]), H = int.Parse(p[2]), NT = int.Parse(p[3]), border = int.Parse(p[4]) }; maps[^1].Add(w); }
            else if (k == 'L') { if (p[1] != "-") { w.walls = new int[p.Length - 1]; for (int i = 1; i < p.Length; i++) w.walls[i - 1] = int.Parse(p[i]); } }
            else if (k == 'C') { w.compat = new UInt128[4][]; for (int d = 0; d < 4; d++) { w.compat[d] = new UInt128[w.NT]; for (int t = 0; t < w.NT; t++) w.compat[d][t] = UInt128.Parse(p[1 + d * w.NT + t], NumberStyles.HexNumber); } }
            else if (k == 'r') w.ev.Add(('r', int.Parse(p[1]), UInt128.Parse(p[2], NumberStyles.HexNumber)));
            else if (k == 'p') w.ev.Add(('p', 0, 0));
            else if (k == 'c') w.ev.Add(('c', int.Parse(p[1]), (UInt128)int.Parse(p[2])));
        }
        return maps;
    }
    static void Main() {
        string[] names = { "S", "M", "L", "XL" };
        foreach (var pass in new[] { "layout", "items" })
            foreach (var memo in new[] { false, true })
                for (int size = 0; size < 4; size++) {
                    var maps = Load($"{pass}_{size}.txt");
                    double best = 1e9; long visits = 0, dec = 0, waves = 0;
                    for (int rep = 0; rep < 6; rep++) {
                        visits = 0; dec = 0; waves = 0;
                        var sw = Stopwatch.StartNew();
                        foreach (var m in maps) foreach (var w in m) { w.memo = memo; w.visits = 0; w.decisions = 0; w.Run(); visits += w.visits; dec += w.decisions; waves++; }
                        double ms = sw.Elapsed.TotalMilliseconds / maps.Count;
                        if (ms < best) best = ms;
                    }
                    Console.WriteLine($"{pass} {(memo ? "list scan" : "full scan")} {names[size]}: {best:F2} ms/map | tries/map {(double)waves / maps.Count:F2} | decisions {dec / maps.Count} | neighbour updates {visits / maps.Count}");
                }
    }
}
