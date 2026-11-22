"""The site layout by wave function collapse (prototype of a replacement for MapGenSite.LayoutSite).

The outer grid of 4 m cells, as now. Every cell is one of:
    O        outside the map
    W        the wood
    G        green: open ground nobody owns (verge, meadow), scenery only
    lane     15 tiles, one per set of sides the lane goes on through
    P(d, k)  a plot cell: d is the side its lane lies on, k how many cells back from the lane it is

What may stand beside what is the whole of the rules (allowed() below). The buildings are still the
plan library's: a plot the collapse leaves is given a plan that fits it, as StampPlot does today.
"""
from wfc import Rng, Wave, DX, DZ, popcount

K = 6          # a plot is at most this deep
MIND = 3       # ... and at least this deep (a building and a back garden)
O, W, G = 0, 1, 2
LANE0 = 3      # lane with side mask m (1..15) is LANE0 + m - 1
P0 = LANE0 + 15
NT = P0 + 4 * K

SIZE_NAME = ["S", "M", "L", "XL"]
SIZE_GRID = [9, 11, 13, 16]
SIZE_PLOTS = ["H*", "H*+", "H*++", "HH*++"]          # GenData.SizePlots
LANE_MIN = [3, 6, 9, 13]
LANE_MAX = [5, 8, 11, 16]
WOOD_MAX = [8, 12, 18, 24]
MIN_STRAIGHT = 3
WOOD_MIN = [3, 3, 4, 5]                               # GenData.SizeWoodMin

Z_HOUSE, Z_WAREHOUSE, Z_DINER, Z_SHOP, Z_GARAGE, Z_BARN, Z_PARK, Z_ZOO, Z_CABIN, Z_FAIR, Z_CHAPEL = 1, 2, 3, 7, 8, 9, 10, 11, 12, 13, 14
POOL_ZONE = [Z_DINER, Z_WAREHOUSE, Z_SHOP, Z_GARAGE, Z_BARN, Z_PARK, Z_ZOO, Z_CABIN, Z_FAIR, Z_CHAPEL]
POOL_WEIGHT = [13, 9, 15, 12, 4, 3, 3, 6, 4, 5]       # GenData.PlotPoolWeight
ZONE_NAME = {1: "House", 2: "Warehouse", 3: "Diner", 7: "Shop", 8: "Garage", 9: "Barn", 10: "Park", 11: "Zoo", 12: "Cabin", 13: "Funfair", 14: "Chapel"}
# cells of a plan by size, least and most (GenData.Size*CellsMin/Max)
ZONE_CELLS = {
    1: ([5, 6, 6, 7], [8, 10, 12, 14]), 2: ([6, 6, 6, 7], [8, 9, 10, 11]), 3: ([6, 6, 6, 6], [10, 12, 14, 16]),
    7: ([4, 4, 4, 5], [9, 11, 13, 13]), 8: ([4, 4, 5, 5], [10, 12, 14, 16]), 9: ([3, 3, 4, 4], [8, 10, 12, 12]),
    12: ([2, 2, 3, 3], [6, 8, 10, 10]), 13: ([4, 4, 5, 5], [10, 12, 15, 15]), 14: ([3, 3, 3, 4], [8, 10, 12, 12]),
}


def is_lane(t): return LANE0 <= t < P0
def lane_mask(t): return t - LANE0 + 1
def is_plot(t): return t >= P0
def plot_d(t): return (t - P0) // K
def plot_k(t): return (t - P0) % K
def P(d, k): return P0 + d * K + k


def allowed(a, d, b):
    """May b stand on side d of a, as far as a is concerned?"""
    od = (d + 2) & 3
    if is_lane(a):
        if lane_mask(a) >> d & 1:
            return is_lane(b) and (lane_mask(b) >> od & 1) == 1     # a lane goes on into a lane
        return not is_lane(b)                                        # no two lanes side by side unjoined
    if is_plot(a):
        pd, pk = plot_d(a), plot_k(a)
        if d == pd:                                                  # toward its lane
            return (is_lane(b) and not (lane_mask(b) >> od & 1)) if pk == 0 else b == P(pd, pk - 1)
        if d == (pd + 2) & 3:                                        # behind it
            if pk + 1 < K and b == P(pd, pk + 1):
                return True
            if pk < MIND - 1:
                return False                                         # too shallow to end here
            return not (is_plot(b) and plot_d(b) == pd)
        # beside it: the same row of the same frontage, or something else altogether
        if is_plot(b) and plot_d(b) == pd:
            return plot_k(b) == pk
        return True
    return True                                                      # O, W, G: no wishes of their own


def build_compat():
    compat = [[0] * NT for _ in range(4)]
    for d in range(4):
        for a in range(NT):
            m = 0
            for b in range(NT):
                if allowed(a, d, b) and allowed(b, (d + 2) & 3, a):
                    m |= 1 << b
            compat[d][a] = m
    return compat


COMPAT = build_compat()
LANES = sum(1 << t for t in range(LANE0, P0))


class Site:
    def __init__(self, seed, size, attempt=0, record=False):
        self.seed, self.size, self.attempt = seed, size, attempt
        self.Gn = SIZE_GRID[size]
        self.rng = Rng.sub(seed, 100, 1, attempt, size)
        self.record = record
        self.frames = []
        self.fail = ""
        self.collapses = 0

    # ---- the collapse -------------------------------------------------------------------------------------
    def snap(self):
        if self.record:
            self.frames.append(list(self.wv.wave))

    def run(self):
        n = self.Gn
        rng = self.rng
        wv = self.wv = Wave(n, n, NT, COMPAT, O)
        cx = n // 2
        self.release = cx
        self.snap()
        # the hunter release: where the lane begins, at the middle of the south edge
        if not wv.collapse(cx, LANE0 + 0):          # mask 1: on to the north only
            return self.failed("release")
        self.snap()

        # a lane keeps MIND cells from the map's edge, so that a plot fits on either side (the first lane comes in
        # from the edge at the release)
        for c in range(n * n):
            x, z = c % n, c // n
            if (x < MIND or x >= n - MIND or z < MIND or z >= n - MIND) and not (x == cx and z < MIND):
                wv.restrict(c, ~LANES)
        if not wv.propagate():
            return self.failed("edge")
        self.snap()

        # A. the lanes: follow the open ends until the lane budget is spent
        lane_n = 1
        straight_n = {cx: 0}      # per lane cell: how far the lane has run straight to it
        budget = LANE_MIN[self.size] + rng.range(LANE_MAX[self.size] - LANE_MIN[self.size] + 1)
        while True:
            ends = [c for c in range(n * n) if not wv.decided(c) and wv.wave[c] & ~LANES == 0]
            if not ends:
                break
            c = ends[rng.range(len(ends))]
            x, z = c % n, c // n
            opts, wts, wts2 = [], [], []
            came, run = -1, 99      # the side the lane comes in from, and how far it has run straight
            for d in range(4):
                nc = (z + DZ[d]) * n + x + DX[d]
                if 0 <= x + DX[d] < n and 0 <= z + DZ[d] < n and nc in straight_n and lane_mask(wv.tile(nc)) >> ((d + 2) & 3) & 1:
                    if straight_n[nc] < run:
                        came, run = d, straight_n[nc]
            for t in range(LANE0, P0):
                if not wv.wave[c] >> t & 1:
                    continue
                m = lane_mask(t)
                new = 0          # sides that would open a further end
                for d in range(4):
                    if m >> d & 1:
                        nc = (z + DZ[d]) * n + x + DX[d]
                        if not wv.decided(nc):
                            new += 1
                sides = popcount(m)
                straight = m in (5, 10)
                w = 14 if straight else (6 if sides == 2 else (3 if sides == 3 else (1 if sides == 4 else 2)))
                w2 = w
                if run < MIN_STRAIGHT and not straight and new > 0:
                    w = 0                      # too soon after the last turn: plots need the room
                if lane_n + len(ends) >= budget and new > 0:
                    w = 0                      # the budget is spent: close the ends
                    w2 = 0
                if lane_n < LANE_MIN[self.size] and len(ends) == 1 and new == 0:
                    w = 0                      # the last open end may not close yet
                    w2 = 0
                opts.append(t); wts.append(w); wts2.append(w2)
            if sum(wts) == 0:
                wts = wts2                     # the straight-run rule gives way before the lane ends early
            if sum(wts) == 0:
                wts = [1] * len(opts)
            t = self.draw(opts, wts)
            straight_n[c] = run + 1 if lane_mask(t) in (5, 10) else 0
            if not wv.collapse(c, t):
                return self.failed("lanes")
            lane_n += 1
            self.collapses += 1
            self.snap()
        self.lane_n = lane_n
        if lane_n < LANE_MIN[self.size]:
            return self.failed("short lane")
        for c in range(n * n):
            if not wv.decided(c):
                wv.restrict(c, ~LANES)
        if not wv.propagate():
            return self.failed("no more lanes")
        self.snap()

        # B. everything else: the cell with the fewest options left, beside what is already decided
        wood_n, wood_max = 0, WOOD_MAX[self.size]
        while True:
            best, cand = 99, []
            for c in range(n * n):
                if wv.decided(c):
                    continue
                x, z = c % n, c // n
                near = False
                for d in range(4):
                    nx, nz = x + DX[d], z + DZ[d]
                    if nx < 0 or nz < 0 or nx >= n or nz >= n or wv.decided(nz * n + nx):
                        near = True
                e = popcount(wv.wave[c]) + (0 if near else 50)
                if e < best:
                    best, cand = e, [c]
                elif e == best:
                    cand.append(c)
            if not cand:
                break
            c = cand[rng.range(len(cand))]
            x, z = c % n, c // n
            opts, wts = [], []
            for t in range(NT):
                if not wv.wave[c] >> t & 1:
                    continue
                same, built = 0, 0
                for d in range(4):
                    nx, nz = x + DX[d], z + DZ[d]
                    if nx < 0 or nz < 0 or nx >= n or nz >= n:
                        same += 1 if t == O else 0
                    elif wv.decided(nz * n + nx):
                        nt = wv.tile(nz * n + nx)
                        if nt == t:
                            same += 1
                        if nt >= LANE0:
                            built += 1
                if t == O:
                    w = 4 * (1 + 4 * same)
                elif t == W:
                    w = (1 + 8 * same + 3 * built) if wood_n < wood_max else 1
                elif t == G:
                    w = 1
                else:
                    k = plot_k(t)
                    w = (3000 if k == 0 else 300 if k < MIND else (60 if k == 3 else (20 if k == 4 else 6))) * (1 + 2 * same)
                opts.append(t); wts.append(w)
            t = self.draw(opts, wts)
            if t == W:
                wood_n += 1
            if not wv.collapse(c, t):
                return self.failed("contradiction")
            self.collapses += 1
            self.snap()
        self.tiles = [wv.tile(c) for c in range(n * n)]
        return self.finish()

    def draw(self, opts, wts):
        r = self.rng.range(sum(wts))
        for t, w in zip(opts, wts):
            if r < w:
                return t
            r -= w
        return opts[-1]

    def failed(self, why):
        self.fail = why
        return False

    # ---- after the collapse: one wood, the plots, their buildings ---------------------------------------------
    def comps(self, pred):
        n = self.Gn
        seen = [False] * (n * n)
        out = []
        for s in range(n * n):
            if seen[s] or not pred(self.tiles[s]):
                continue
            comp, q = [], [s]
            seen[s] = True
            while q:
                c = q.pop()
                comp.append(c)
                x, z = c % n, c // n
                for d in range(4):
                    nx, nz = x + DX[d], z + DZ[d]
                    if 0 <= nx < n and 0 <= nz < n and not seen[nz * n + nx] and pred(self.tiles[nz * n + nx]):
                        seen[nz * n + nx] = True
                        q.append(nz * n + nx)
            out.append(comp)
        return out

    def finish(self):
        n = self.Gn
        rng = Rng.sub(self.seed, 100, 2, self.attempt, self.size)
        T = self.tiles

        # the frontage cells (k = 0) side by side along a lane make a run; a run's columns go back from the lane
        seen = set()
        runs = []
        for c in range(n * n):
            if not (is_plot(T[c]) and plot_k(T[c]) == 0) or c in seen:
                continue
            d = plot_d(T[c])
            ad = (d + 1) & 3                       # along the frontage
            x, z = c % n, c // n
            while True:                            # back to the run's first cell
                px, pz = x - DX[ad], z - DZ[ad]
                if 0 <= px < n and 0 <= pz < n and T[pz * n + px] == P(d, 0):
                    x, z = px, pz
                else:
                    break
            cols = []
            while 0 <= x < n and 0 <= z < n and T[z * n + x] == P(d, 0):
                seen.add(z * n + x)
                col = []
                bx, bz, k = x, z, 0
                while 0 <= bx < n and 0 <= bz < n and T[bz * n + bx] == P(d, k):
                    col.append(bz * n + bx)
                    bx -= DX[d]; bz -= DZ[d]; k += 1
                cols.append(col)
                x += DX[ad]; z += DZ[ad]
            runs.append(dict(d=d, cols=cols, taken=[False] * len(cols)))
        # the lane distance of every frontage cell from the release
        dist = {self.release: 0}
        q = [self.release]
        while q:
            c = q.pop(0)
            x, z = c % n, c // n
            for d in range(4):
                if lane_mask(T[c]) >> d & 1:
                    nc = (z + DZ[d]) * n + x + DX[d]
                    if nc not in dist:
                        dist[nc] = dist[c] + 1
                        q.append(nc)
        self.lane_cells = len(dist)
        self.frontage = sum(len(r["cols"]) for r in runs)

        # the kinds of the map's size (LayoutSite (1)): shops at the road's start, the farm and the park far out
        prog = SIZE_PLOTS[self.size]
        kinds = []
        for ch in prog:
            z = Z_HOUSE
            if ch != "H" and not (ch == "*" and rng.chance(350)):
                tot = sum(w for zz, w in zip(POOL_ZONE, POOL_WEIGHT) if zz not in kinds)
                r = rng.range(tot)
                for zz, w in zip(POOL_ZONE, POOL_WEIGHT):
                    if zz in kinds:
                        continue
                    if r < w:
                        z = zz
                        break
                    r -= w
            kinds.append(z)
        rng.shuffle(kinds)
        com_first = rng.chance(600)
        rank = lambda z: 2 if z in (Z_BARN, Z_PARK, Z_ZOO, Z_CABIN, Z_CHAPEL) else (0 if z != Z_HOUSE and com_first else 1)
        self.kinds = kinds

        # every kind takes a window of some run's frontage that holds one of its plans: the kinds hardest to seat
        # first, the shops near the release and the farm far out. A building never stands wall to wall with
        # another (layout test T2): a window where it would is not taken.
        self.plots = []
        self.bcell = {}
        self.swapped = 0

        def cells_of(r, i, off, plan, mir):
            out = []
            for py in range(plan["d"]):
                for px in range(plan["w"]):
                    if plan["cells"][py * plan["w"] + px] >= 0:
                        out.append(r["cols"][i + off + (plan["w"] - 1 - px if mir else px)][py])
            return out

        def clear(cells):
            for c in cells:
                x, z = c % n, c // n
                for d in range(4):
                    nx, nz = x + DX[d], z + DZ[d]
                    if 0 <= nx < n and 0 <= nz < n and (nz * n + nx) in self.bcell:
                        return False
            return True

        def wdist(r, i, w):
            c = r["cols"][i + w // 2][0]
            return dist.get((c // n + DZ[r["d"]]) * n + c % n + DX[r["d"]], 99)

        def seat(z):
            wins = []            # (run, first column, width, depth every column has)
            for r in runs:
                L = len(r["cols"])
                for i in range(L):
                    dep = 99
                    for w in range(1, 8):
                        if i + w > L or r["taken"][i + w - 1]:
                            break
                        dep = min(dep, len(r["cols"][i + w - 1]))
                        # flush against the run's end or a neighbour: no slivers between plots
                        if i == 0 or r["taken"][i - 1] or i + w == L or r["taken"][i + w]:
                            wins.append((r, i, w, dep))
            mir = rng.chance(500)
            cands = []           # (run, first column, width, depth, building's offset, has a side yard)
            if z in (Z_PARK, Z_ZOO):
                plan = None
                ww = 3 if z == Z_ZOO else [3, 4, 4, 5][self.size]
                cands = [(r, i, w, dep, 0, 1) for r, i, w, dep in wins if w == ww and dep >= 3]
                if not cands:
                    cands = [(r, i, w, dep, 0, 1) for r, i, w, dep in wins if w == 3 and dep >= 3]
            else:
                lo, hi = ZONE_CELLS[z][0][self.size], ZONE_CELLS[z][1][self.size]
                shapes = {(w, min(dep - 1, 6)) for r, i, w, dep in wins}
                ok = lambda q: any(q["w"] == sw and q["d"] <= sd for sw, sd in shapes)
                fit = [q for q in PLANS if q["zone"] == z and lo <= q["n"] <= hi and ok(q)]
                if not fit:
                    fit = [q for q in PLANS if q["zone"] == z and ok(q)]
                if not fit:
                    return False
                target = lo + rng.range(hi - lo + 1)
                # the plan nearest the drawn size that some window holds clear of the other buildings
                for q in sorted(fit, key=lambda q: abs(q["n"] - target)):
                    for r, i, w, dep in wins:
                        if dep < q["d"] + 1 or not (w == q["w"] or w == q["w"] + 1):
                            continue
                        for off in range(w - q["w"] + 1):
                            if clear(cells_of(r, i, off, q, mir)):
                                cands.append((r, i, w, dep, off, w - q["w"]))
                    if cands:
                        plan = q
                        break
            if not cands:
                return False
            yard = max(v[5] for v in cands)          # a side yard where there is room for one
            cands = [v for v in cands if v[5] == yard]
            dd = [wdist(v[0], v[1], v[2]) for v in cands]
            pick = min(dd) if rank(z) == 0 else (max(dd) if rank(z) == 2 else dd[rng.range(len(dd))])
            cands = [v for v, dv in zip(cands, dd) if dv == pick]
            r, i, w, dep, off, _ = cands[rng.range(len(cands))]
            body = plan["d"] if plan else [3, 3, 4, 4][self.size] if z == Z_ZOO else 3
            back = 0 if plan is None else 1 + (1 if rng.chance(350) else 0)
            for j in range(i, i + w):
                r["taken"][j] = True
            k = len(self.plots)
            self.plots.append(dict(d=r["d"], run=r, i0=i, along=w, depth=min(dep, body + back), zone=z, plan=plan, mir=mir, off=off, dist=pick))
            if plan is not None:
                for c in cells_of(r, i, off, plan, mir):
                    self.bcell[c] = k
            return True

        for z in sorted(kinds, key=lambda z: -self.need(z)):
            if seat(z):
                continue
            # no room for this kind: another kind the map has not got, a house at the last
            done = False
            for zz in [q for q in POOL_ZONE if q not in kinds] + [Z_HOUSE]:
                if z != Z_HOUSE and seat(zz):
                    kinds[kinds.index(z)] = zz
                    self.swapped += 1
                    done = True
                    break
            if not done:
                return self.failed("no plot for a " + ZONE_NAME[z])
        # a sliver of frontage left beside a plot is its side yard
        for p in self.plots:
            r = p["run"]
            for step in (-1, 1):
                j = p["i0"] - 1 if step < 0 else p["i0"] + p["along"]
                m = 0
                while 0 <= j < len(r["cols"]) and not r["taken"][j]:
                    m += 1; j += step
                if 0 < m <= 1 and p["plan"] is not None:
                    j = p["i0"] - 1 if step < 0 else p["i0"] + p["along"]
                    if len(r["cols"][j]) >= p["depth"]:
                        r["taken"][j] = True
                        p["along"] += 1
                        if step < 0:
                            p["i0"] -= 1
                            p["off"] += 1
        self.cell_plot = [-1] * (n * n)
        for k, p in enumerate(self.plots):
            p["cols"] = [col[:p["depth"]] for col in p["run"]["cols"][p["i0"]:p["i0"] + p["along"]]]
            for col in p["cols"]:
                for c in col:
                    self.cell_plot[c] = k
        # plot cells nobody took go back to the wood
        self.spare = 0
        for c in range(n * n):
            if is_plot(T[c]) and self.cell_plot[c] < 0:
                T[c] = W
                self.spare += 1

        def ground_nb(c, pred):
            x, z = c % n, c // n
            k = 0
            for d in range(4):
                nx, nz = x + DX[d], z + DZ[d]
                if 0 <= nx < n and 0 <= nz < n and pred(T[nz * n + nx]):
                    k += 1
            return k

        # one wood: the largest piece stays; a smaller piece is green where it lies against a lane, else outside
        woods = sorted(self.comps(lambda t: t == W), key=len, reverse=True)
        for comp in woods[1:]:
            for c in comp:
                T[c] = G
        # outside that the map has closed in is green
        for comp in self.comps(lambda t: t == O):
            if not any(c % n in (0, n - 1) or c // n in (0, n - 1) for c in comp):
                for c in comp:
                    T[c] = G
        # green and wood are trimmed (Cor: trim unused lawn): a piece that touches no lane goes, and so does a
        # cell that sticks out
        for comp in self.comps(lambda t: t == G):
            if not any(ground_nb(c, is_lane) for c in comp):
                for c in comp:
                    T[c] = O
        changed = True
        while changed:
            changed = False
            for c in range(n * n):
                if T[c] in (G, W) and ground_nb(c, lambda t: t != O) <= 1 and not ground_nb(c, is_lane):
                    T[c] = O
                    changed = True
        wood = {c for c in range(n * n) if T[c] == W}
        woods = sorted(self.comps(lambda t: t == W), key=len, reverse=True)
        for comp in woods[1:]:
            for c in comp:
                T[c] = O
        wood = set(woods[0]) if woods else set()
        grow = 0
        while len(wood) < WOOD_MIN[self.size] and grow < 50:
            grow += 1
            edge = []
            src = wood if wood else {c for c in range(n * n) if T[c] == G or is_lane(T[c])}
            for c in sorted(src):
                x, z = c % n, c // n
                for d in range(4):
                    nx, nz = x + DX[d], z + DZ[d]
                    if 0 <= nx < n and 0 <= nz < n and T[nz * n + nx] in (O, G):
                        edge.append(nz * n + nx)
            if not edge:
                break
            c = edge[rng.range(len(edge))]
            T[c] = W
            wood.add(c)
        if len(wood) < WOOD_MIN[self.size]:
            return self.failed("no room for the wood")
        # the wood can be walked to: it lies against a lane, green, or a garden (a back gate)
        if not any(ground_nb(c, lambda t: t != O and t != W) for c in wood):
            return self.failed("wood cut off")
        self.wood_n = len(wood)
        self.green_n = sum(1 for t in T if t == G)
        self.ground = sum(1 for t in T if t != O)
        return True

    def need(self, z):
        if z in (Z_PARK, Z_ZOO):
            return 9
        return ZONE_CELLS[z][0][self.size]


import plans as _plans
PLANS = _plans.load()


def generate(seed, size, record=False):
    """The generator's Retry: a failed layout is the seed's next attempt."""
    why = []
    for attempt in range(12):
        s = Site(seed, size, attempt, record)
        if s.run():
            s.retries = why
            return s
        why.append(s.fail)
    return None


if __name__ == "__main__":
    import collections, time
    for size in range(4):
        t0 = time.time()
        att = collections.Counter(); whys = collections.Counter(); fails = 0
        lanes = []; ground = []; spare = 0; prop = []; coll = []; swapped = 0; shapes = set(); junc = collections.Counter(); bends = collections.Counter()
        N = 200
        for seed in range(1, N + 1):
            s = generate(seed, size)
            if s is None:
                fails += 1
                continue
            att[s.attempt] += 1
            for w in s.retries:
                whys[w.split(" a ")[0]] += 1
            lanes.append(s.lane_cells); ground.append(s.ground); spare += s.spare; swapped += s.swapped
            lc = sorted(c for c in range(s.Gn * s.Gn) if is_lane(s.tiles[c]))
            shapes.add(tuple(lc))
            ms = [lane_mask(s.tiles[c]) for c in lc]
            junc[max(popcount(m) for m in ms)] += 1
            bends[min(3, sum(1 for m in ms if popcount(m) == 2 and m not in (5, 10)))] += 1
            prop.append(s.wv.propagations); coll.append(s.collapses)
        print(SIZE_NAME[size], "failed seeds", fails, "of", N, "| first try", att[0], "| mean attempts", round(sum((a + 1) * c for a, c in att.items()) / max(1, sum(att.values())), 2),
              "| ground", sum(ground) // len(ground), "lane", round(sum(lanes) / len(lanes), 1), "spare plot cells/map", round(spare / len(lanes), 2),
              "| collapses", sum(coll) // len(coll), "propagation visits", sum(prop) // len(prop), "| %.0f ms/map" % ((time.time() - t0) * 1000 / N))
        print("   retries:", dict(whys), "| attempts", sorted(att.items()))
        print("   distinct lane networks", len(shapes), "| no junction", junc[1] + junc[2], "T", junc[3], "cross", junc[4], "| bends 0/1/2/3+", [bends[i] for i in range(4)], "| kinds swapped", swapped)
