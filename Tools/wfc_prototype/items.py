"""Items by wave function collapse, everywhere: rooms, gardens, the wood and the green.

A second collapse on a grid of 1 m cells (four to a side of an outer cell), after the site layout. One tile set
and one table of what may stand beside what for the whole map; a room, a garden, the wood and the green differ
only in which tiles they may hold, how heavily each is drawn, and the few pieces each must have.

Walls close the sides of a cell: nothing is passed on through a wall, and a walled side counts as the border, so
"stands against a wall" is an ordinary neighbour rule.
"""
from wfc import Rng, Wave, DX, DZ, popcount
import sitegen as S

F = 4   # item cells to a side of an outer cell

NAMES = ["VOID", "FLOOR", "GRASS", "LAWN", "PAVING", "BOARDS", "RUG",
         "TREE", "BUSH", "FERN", "ROCK", "LOG", "STUMP", "MUSHROOM", "FLOWERS", "WATER", "REEDS",
         "FIRE", "TENT", "FLOWERBED", "HEDGE", "TABLE", "BLANKET", "POST", "CRATE", "BARREL", "HAY", "GRAVE",
         "VEG_NS", "VEG_EW", "LINE_NS", "LINE_EW", "RACK_NS", "RACK_EW", "PEW_NS", "PEW_EW",
         "NIGHTSTAND", "WARDROBE", "DESK", "SOFA", "TV", "ARMCHAIR", "PLANT", "SHELF", "COUNTER", "STOVE", "FRIDGE",
         "SINK", "TOILET", "TUB", "BASIN", "TILL", "WORKBENCH", "TOOLCHEST", "ALTAR"]
for base in ("SEAT", "CHAIR", "BEDH", "BEDF"):
    NAMES += [base + str(d) for d in range(4)]
NAMES += ["TRAIL%d" % m for m in range(1, 16)]
NT = len(NAMES)
ID = {n: i for i, n in enumerate(NAMES)}
VOID = 0


def group(t):
    n = NAMES[t]
    if n.startswith("TRAIL"):
        return "TRAIL"
    if n[-1].isdigit():
        return n[:-1]
    if n.endswith("_NS") or n.endswith("_EW"):
        return n[:-3]
    return n


GROUP = [group(t) for t in range(NT)]
GROUND = {ID[n] for n in ("FLOOR", "GRASS", "LAWN", "PAVING", "BOARDS", "RUG")}
WALK = GROUND | {t for t in range(NT) if GROUP[t] in ("FERN", "MUSHROOM", "FLOWERS", "TRAIL", "BLANKET", "REEDS", "VEG", "LINE")}
NATURAL = {t for t in range(NT) if GROUP[t] in ("TREE", "BUSH", "FERN", "ROCK", "LOG", "STUMP", "MUSHROOM", "FLOWERS", "WATER", "REEDS", "HEDGE", "FLOWERBED", "TRAIL")}
NEED_WALL = {"NIGHTSTAND", "WARDROBE", "DESK", "SOFA", "TV", "SHELF", "COUNTER", "STOVE", "FRIDGE", "SINK", "TOILET", "TUB", "BASIN", "TILL", "WORKBENCH", "ALTAR"}
FORCED = {"CHAIR", "SEAT", "BEDF", "POST", "LINE", "TRAIL"}      # pieces that only come with another: never cut by the cover budget


def trail_mask(t): return int(NAMES[t][5:])
def dirn(t): return int(NAMES[t][-1])


def allowed(a, d, b):
    """May b stand on side d of a, as far as a is concerned?"""
    od = (d + 2) & 3
    ga, gb = GROUP[a], GROUP[b]
    if ga == "TRAIL":
        if trail_mask(a) >> d & 1:
            return gb == "TRAIL" and (trail_mask(b) >> od & 1) == 1
        return gb != "TRAIL"
    if ga == "SEAT":
        return gb == "FIRE" if d == dirn(a) else True                 # a seat faces the fire
    if ga == "CHAIR":
        return gb in ("TABLE", "DESK") if d == dirn(a) else True      # a chair is drawn up to a table or a desk
    if ga == "BEDH":                                                  # a bed's head is at a wall, its foot out into the room
        if d == dirn(a):
            return b == VOID
        if d == (dirn(a) + 2) & 3:
            return b == ID["BEDF%d" % dirn(a)]
        return True
    if ga == "BEDF":
        return b == ID["BEDH%d" % dirn(a)] if d == dirn(a) else True
    if ga in ("RACK", "PEW"):                                         # rows with an aisle between them
        along = (d & 1) == (0 if NAMES[a].endswith("_NS") else 1)
        return b == a or b in GROUND or b == VOID if along else (b in GROUND or b == VOID)
    if ga == "VEG":                                                   # one crop to a row, rows the same way
        return not (gb == "VEG" and b != a)
    if ga == "LINE":                                                  # a washing line runs from post to post
        along = (d & 1) == (0 if NAMES[a].endswith("_NS") else 1)
        return b == a or gb == "POST" if along else gb != "LINE"
    if ga == "WATER":
        return gb in ("WATER", "REEDS")
    if ga == "TREE":
        return gb not in ("TREE", "ROCK")                             # room to walk between trunks
    if ga == "TV":
        return gb != "SOFA"
    return True


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
TRAILS = sum(1 << t for t in range(NT) if GROUP[t] == "TRAIL")

# ---- what each kind of place holds: ground, weights (ground = 1000), the pieces it must have, caps, how strongly a
# piece draws more of its own beside it, and the most of its cells that may be blocked (per cent) ----
def prof(ground, w, must=(), cap=None, aff=None, cover=(15, 30)):
    return dict(ground=ground, w=w, must=list(must), cap=cap or {}, aff=aff or {}, cover=cover)


PROFILE = {
    "wood": prof("FLOOR", dict(TREE=300, BUSH=70, FERN=110, ROCK=30, LOG=30, STUMP=20, MUSHROOM=25, SEAT=4000, TENT=4), cap=dict(TENT=2, SEAT=4), cover=(22, 34)),
    "green": prof("GRASS", dict(FLOWERS=60, BUSH=30, ROCK=20, TREE=30), aff=dict(FLOWERS=4), cover=(6, 14)),
    "picnic": prof("LAWN", dict(TABLE=6, CHAIR=5000, BLANKET=8, FLOWERBED=25, HEDGE=6, BUSH=15, TREE=12), must=["TABLE"], cap=dict(TABLE=2, BLANKET=1, CHAIR=6), aff=dict(FLOWERBED=3)),
    "veg": prof("LAWN", dict(VEG=45, HEDGE=6, BUSH=10, BARREL=6, CRATE=6), must=["VEG"], cap=dict(BARREL=1, CRATE=2, VEG=20), aff=dict(VEG=10), cover=(10, 20)),
    "drying": prof("LAWN", dict(HEDGE=6, BUSH=12, FLOWERBED=20, CRATE=6), must=["LINE", "LINE"], cap=dict(CRATE=1), aff=dict(FLOWERBED=3)),
    "firepit": prof("LAWN", dict(SEAT=5000, LOG=25, STUMP=12, BUSH=12, TREE=10, HEDGE=6), must=["FIRE"], cap=dict(SEAT=4)),
    "campsite": prof("LAWN", dict(SEAT=3000, TENT=10, LOG=20, TREE=14, BUSH=10, CRATE=5), must=["FIRE", "TENT"], cap=dict(SEAT=3, TENT=3, CRATE=2)),
    "patio": prof("PAVING", dict(TABLE=30, CHAIR=5000, PLANT=20, HEDGE=6), must=["TABLE"], cap=dict(TABLE=5, PLANT=3)),
    "yard": prof("PAVING", dict(CRATE=35, BARREL=18, BUSH=8), aff=dict(CRATE=2), cover=(8, 20)),
    "farm": prof("LAWN", dict(VEG=60, HAY=14, BARREL=5, TREE=6, HEDGE=5), must=["VEG"], aff=dict(VEG=12, HAY=2), cover=(8, 16)),
    "churchyard": prof("LAWN", dict(GRAVE=45, FLOWERBED=15, HEDGE=6, TREE=14), aff=dict(FLOWERBED=2), cover=(14, 24)),
    "cabinyard": prof("LAWN", dict(SEAT=4000, LOG=40, STUMP=30, TREE=25, BUSH=15), must=["FIRE"], cap=dict(SEAT=3), cover=(14, 26)),
    "park": prof("LAWN", dict(FLOWERS=60, TREE=30, BUSH=15, TABLE=5, CHAIR=2500, WATER=1), must=["WATER"], cap=dict(TABLE=3, WATER=14, REEDS=12, CHAIR=5), aff=dict(FLOWERS=4), cover=(12, 22)),
    "zoo": prof("LAWN", dict(HEDGE=50, ROCK=35, TREE=25, BUSH=20, WATER=1), must=["WATER"], cap=dict(WATER=10, REEDS=8), aff=dict(HEDGE=4), cover=(16, 28)),
    # indoors
    "living": prof("BOARDS", dict(RUG=50, SOFA=40, ARMCHAIR=35, TABLE=15, PLANT=40, SHELF=45, CHAIR=600), must=["SOFA", "TV"], cap=dict(SOFA=3, TV=1, ARMCHAIR=2, TABLE=1, PLANT=2, SHELF=2, RUG=6, CHAIR=1), aff=dict(RUG=6, SOFA=3)),
    "kitchen": prof("BOARDS", dict(COUNTER=160, TABLE=12, CHAIR=3000, PLANT=10), must=["STOVE", "FRIDGE", "SINK"], cap=dict(COUNTER=6, TABLE=1, CHAIR=3, PLANT=1), aff=dict(COUNTER=3)),
    "dining": prof("BOARDS", dict(TABLE=25, CHAIR=6000, PLANT=30, SHELF=40, RUG=20), must=["TABLE"], cap=dict(TABLE=2, PLANT=2, SHELF=2, RUG=4, CHAIR=6), aff=dict(TABLE=20, RUG=6)),
    "dinerdining": prof("BOARDS", dict(TABLE=70, CHAIR=6000, PLANT=20, COUNTER=30), must=["TABLE"], cap=dict(TABLE=8, PLANT=3, COUNTER=4, CHAIR=18), aff=dict(COUNTER=5), cover=(25, 40)),
    "bedroom": prof("BOARDS", dict(BEDH=6, WARDROBE=60, DESK=25, CHAIR=3000, RUG=40, PLANT=15, SHELF=30, NIGHTSTAND=1), must=["BEDH"], cap=dict(BEDH=2, WARDROBE=1, DESK=1, CHAIR=1, RUG=4, PLANT=1, SHELF=1, NIGHTSTAND=2), aff=dict(RUG=6)),
    "bunkroom": prof("BOARDS", dict(BEDH=40, WARDROBE=40, RUG=20, CRATE=15, NIGHTSTAND=1), must=["BEDH", "BEDH"], cap=dict(BEDH=4, WARDROBE=2, RUG=4, CRATE=2, NIGHTSTAND=2)),
    "bath": prof("BOARDS", dict(PLANT=10, SHELF=15), must=["TOILET", "BASIN", "TUB"], cap=dict(PLANT=1, SHELF=1)),
    "office": prof("BOARDS", dict(SHELF=80, PLANT=30, CHAIR=5000, WARDROBE=25, RUG=20), must=["DESK"], cap=dict(SHELF=3, PLANT=2, CHAIR=2, WARDROBE=1, RUG=4), aff=dict(RUG=6)),
    "hall": prof("BOARDS", dict(PLANT=20, SHELF=30, RUG=40), cap=dict(PLANT=1, SHELF=1, RUG=4), aff=dict(RUG=8), cover=(4, 10)),
    "store": prof("BOARDS", dict(CRATE=140, SHELF=120, BARREL=50), aff=dict(CRATE=2), cover=(25, 40)),
    "shop": prof("BOARDS", dict(RACK=120, PLANT=10), must=["TILL"], cap=dict(PLANT=2), aff=dict(RACK=8), cover=(22, 34)),
    "workshop": prof("BOARDS", dict(TOOLCHEST=40, CRATE=40, BARREL=35, SHELF=70), must=["WORKBENCH"], cap=dict(TOOLCHEST=2, WORKBENCH=2), cover=(18, 30)),
    "barn": prof("BOARDS", dict(HAY=140, BARREL=40, CRATE=40), aff=dict(HAY=3), cover=(22, 36)),
    "cabin": prof("BOARDS", dict(SOFA=30, TABLE=15, CHAIR=3000, STOVE=20, SHELF=40, RUG=40, ARMCHAIR=25, PLANT=10), must=["SOFA", "TABLE"], cap=dict(SOFA=2, TABLE=1, CHAIR=3, STOVE=1, SHELF=2, RUG=6, ARMCHAIR=2, PLANT=1), aff=dict(RUG=6)),
    "nave": prof("BOARDS", dict(PEW=200, PLANT=10), must=["ALTAR"], cap=dict(PLANT=2), aff=dict(PEW=8), cover=(25, 40)),
    "playroom": prof("BOARDS", dict(RUG=80, SHELF=50, CRATE=35, TABLE=10, CHAIR=3000), cap=dict(SHELF=2, CRATE=3, TABLE=1, CHAIR=2, RUG=8), aff=dict(RUG=6)),
}
ROOM_KIND = {1: "living", 2: "kitchen", 3: "dining", 4: "bedroom", 5: "bath", 6: "office", 7: "bedroom", 8: "hall", 9: "store", 37: "bedroom", 38: "playroom", 39: "store",
             20: "store", 21: "store", 22: "office", 24: "shop", 25: "store", 26: "office", 27: "workshop", 28: "store", 34: "barn", 35: "store", 36: "workshop",
             29: "cabin", 33: "bunkroom", 59: "shop", 60: "kitchen", 61: "store", 64: "nave", 65: "office", 30: "dinerdining", 31: "kitchen", 32: "store"}
YARD_KIND = {S.Z_DINER: "patio", S.Z_SHOP: "yard", S.Z_GARAGE: "yard", S.Z_WAREHOUSE: "yard", S.Z_FAIR: "yard", S.Z_BARN: "farm", S.Z_CHAPEL: "churchyard",
             S.Z_CABIN: "cabinyard", S.Z_PARK: "park", S.Z_ZOO: "zoo"}
HOUSE_PURPOSES = ["picnic", "veg", "drying", "firepit", "campsite"]      # dealt from a shuffled deck, as YardPurposes does
ATTRACT = {("NIGHTSTAND", "BEDH"): 900, ("REEDS", "WATER"): 700, ("WATER", "WATER"): 900, ("COUNTER", "STOVE"): 300, ("COUNTER", "SINK"): 300, ("ARMCHAIR", "RUG"): 60,
           ("TABLE", "RUG"): 60, ("LOG", "FIRE"): 0}
IMPLIED = {"BEDH": ["BEDF"], "LINE": ["POST"], "WATER": ["REEDS"], "TABLE": [], "FIRE": []}


class Items:
    def __init__(self, site, attempt=0):
        self.site, self.attempt = site, attempt
        self.n = site.Gn * F
        self.fail = ""

    # ---- the places: which region every item cell belongs to, the walls and the doors ----------------------------
    def layout(self):
        s, n, G = self.site, self.n, self.site.Gn
        rng = Rng.sub(s.seed, 100, 3, 0, s.size)
        T = s.tiles
        room = {}
        for k, p in enumerate(s.plots):
            q = p["plan"]
            if q is None:
                continue
            for py in range(q["d"]):
                for px in range(q["w"]):
                    r = q["cells"][py * q["w"] + px]
                    if r >= 0:
                        room[p["cols"][p["off"] + (q["w"] - 1 - px if p["mir"] else px)][py]] = (k, r)
        deck = []
        keys, self.regions = {}, []
        okey = [None] * (G * G)
        for oc in range(G * G):
            if oc in room:
                k, r = room[oc]
                key = ("room", k, r)
                kind = ROOM_KIND.get(s.plots[k]["plan"]["roles"][r], "store")
            elif s.cell_plot[oc] >= 0:
                k = s.cell_plot[oc]
                key = ("yard", k)
                kind = YARD_KIND.get(s.plots[k]["zone"])
                if kind is None and key not in keys:
                    if not deck:
                        deck = list(HOUSE_PURPOSES)
                        rng.shuffle(deck)
                    kind = deck.pop()
            elif T[oc] == S.W:
                key, kind = ("wood",), "wood"
            elif T[oc] == S.G:
                key, kind = ("green",), "green"
            else:
                continue
            if key not in keys:
                keys[key] = len(self.regions)
                pr = PROFILE[kind]
                self.regions.append(dict(key=key, kind=kind, prof=pr, cells=[], count={}, block=0,
                                         cover=pr["cover"][0] + rng.range(pr["cover"][1] - pr["cover"][0] + 1), lean=70 + rng.range(71)))
            okey[oc] = keys[key]
        self.reg = [-1] * (n * n)
        for c in range(n * n):
            r = okey[(c // n // F) * G + c % n // F]
            if r is not None:
                self.reg[c] = r
                self.regions[r]["cells"].append(c)
        open_air = lambda r: r >= 0 and self.regions[r]["key"][0] in ("wood", "green")
        self.walls = [0] * (n * n)
        self.indoor = [self.reg[c] >= 0 and self.regions[self.reg[c]]["key"][0] == "room" for c in range(n * n)]
        self.edge = [False] * (n * n)
        for c in range(n * n):
            if self.reg[c] < 0:
                continue
            x, z = c % n, c // n
            for d in range(4):
                nx, nz = x + DX[d], z + DZ[d]
                nr = self.reg[nz * n + nx] if 0 <= nx < n and 0 <= nz < n else -2
                if nr != self.reg[c]:
                    self.edge[c] = True
                    if nr == -2 or not (open_air(nr) and open_air(self.reg[c])) and (nr >= 0 or self.indoor[c]):
                        self.walls[c] |= 1 << d
        # doors: the two middle cells of the outer cell's side, and the way through them kept clear
        self.doors = set()
        self.clear = set()
        for k, p in enumerate(s.plots):
            q = p["plan"]
            if q is None:
                continue
            d0 = p["d"]
            ad = (d0 + 1) & 3
            world = {0: (d0 + 2) & 3, 2: d0, 1: (ad + 2) & 3 if p["mir"] else ad, 3: ad if p["mir"] else (ad + 2) & 3}
            for code in list(q["doors"]) + [q["front"], q["back"]]:
                if code < 0:
                    continue
                cell, pd = code // 4, code % 4
                px, py = cell % q["w"], cell // q["w"]
                if py >= q["d"] or q["cells"][cell] < 0:
                    continue
                oc = p["cols"][p["off"] + (q["w"] - 1 - px if p["mir"] else px)][py]
                wd = world[pd]
                ox, oz = oc % G * F, oc // G * F
                for j in (1, 2):
                    if wd == 0: fx, fz = ox + j, oz + F - 1
                    elif wd == 2: fx, fz = ox + j, oz
                    elif wd == 1: fx, fz = ox + F - 1, oz + j
                    else: fx, fz = ox, oz + j
                    self.doors.add((fz * n + fx, wd))
                    for step in (-1, 0, 1, 2):
                        cx, cz = fx + DX[wd] * step, fz + DZ[wd] * step
                        if 0 <= cx < n and 0 <= cz < n:
                            self.clear.add(cz * n + cx)
                    ax, az = fx + DX[wd], fz + DZ[wd]
                    if 0 <= ax < n and 0 <= az < n:
                        self.doors.add((az * n + ax, (wd + 2) & 3))

    # ---- the collapse --------------------------------------------------------------------------------------------
    def run(self):
        s, n = self.site, self.n
        if not hasattr(self, "reg"):
            self.layout()
        rng = self.rng = Rng.sub(s.seed, 100, 4, self.attempt, s.size)
        for r in self.regions:
            r["count"], r["block"] = {}, 0
        wv = self.wv = Wave(n, n, NT, COMPAT, VOID, self.walls)
        self.missed = 0
        # what each cell may hold: its place's pieces; ground only on the way through a door; wall pieces at walls
        for c in range(n * n):
            r = self.reg[c]
            if r < 0:
                wv.restrict(c, 1 << VOID)
                continue
            pr = self.regions[r]["prof"]
            groups = set(pr["w"]) | set(pr["must"])
            for g in list(groups):
                groups |= set(IMPLIED.get(g, []))
            if self.regions[r]["kind"] in ("wood", "green"):
                groups |= {"TRAIL"}
            if self.regions[r]["kind"] == "wood":
                groups |= {"FIRE"}
            m = 1 << ID[pr["ground"]]
            if c not in self.clear:
                for t in range(1, NT):
                    if GROUP[t] in groups and not (GROUP[t] in NEED_WALL and self.walls[c] == 0):
                        m |= 1 << t
            wv.restrict(c, m)
        if not wv.propagate():
            return self.failed("setup")
        # the pieces a place must have: its defining minimum, each at a cell that can still hold it
        for ri, r in enumerate(self.regions):
            musts = list(r["prof"]["must"])
            if r["kind"] == "wood" and len(r["cells"]) >= 48:
                musts += ["FIRE"]
            for g in musts:
                cands = [c for c in r["cells"] if not wv.decided(c) and any(wv.wave[c] >> t & 1 and GROUP[t] == g for t in range(NT))]
                if g in ("FIRE", "TABLE", "WATER", "TENT"):      # with room around it
                    inner = [c for c in cands if not self.edge[c]]
                    cands = inner or cands
                if not cands:
                    self.missed += 1
                    continue
                c = cands[rng.range(len(cands))]
                opts = [t for t in range(NT) if wv.wave[c] >> t & 1 and GROUP[t] == g]
                if not self.put(c, opts[rng.range(len(opts))]):
                    return self.failed("must " + g)
        # a trail into the wood from where it meets the lane or the green
        for r in self.regions:
            if r["kind"] != "wood":
                continue
            r["trail_max"] = 6 + rng.range(max(1, len(r["cells"]) // 10))
            trails = sum(1 << t for t in range(NT) if GROUP[t] == "TRAIL")
            ents = []
            for c in r["cells"]:
                x, z = c % n, c // n
                for d in range(4):
                    nx, nz = x + DX[d], z + DZ[d]
                    if 0 <= nx < n and 0 <= nz < n and not wv.decided(c) and wv.wave[c] & trails:
                        oc = (nz // F) * s.Gn + nx // F
                        if self.reg[nz * n + nx] != self.reg[c] and (S.is_lane(s.tiles[oc]) or s.tiles[oc] == S.G):
                            ents.append(c)
            if ents:
                wv.restrict(ents[rng.range(len(ents))], trails)
                if not wv.propagate():
                    return self.failed("trail")
        # everything else: the cell with the fewest options left
        todo = [c for c in range(n * n) if not wv.decided(c)]
        self.collapses = 0
        while todo:
            best, cand = 999, []
            keep = []
            for c in todo:
                m = wv.wave[c]
                if m & (m - 1) == 0:
                    self.note(c)
                    continue
                keep.append(c)
                e = popcount(m)
                if e < best:
                    best, cand = e, [c]
                elif e == best:
                    cand.append(c)
            todo = keep
            if not cand:
                break
            c = cand[rng.range(len(cand))]
            opts = [t for t in range(NT) if wv.wave[c] >> t & 1]
            wts = [self.weight(c, t, opts) for t in opts]
            if sum(wts) == 0:
                g = ID[self.regions[self.reg[c]]["prof"]["ground"]]
                wts = [1 if t == g else 0 for t in opts]
                if sum(wts) == 0:
                    wts = [1] * len(opts)
            roll = rng.range(sum(wts))
            for t, w in zip(opts, wts):
                if roll < w:
                    break
                roll -= w
            if not self.put(c, t):
                return self.failed("contradiction")
            self.collapses += 1
        self.tiles = [wv.tile(c) for c in range(n * n)]
        self.repair()
        return True

    def failed(self, why):
        self.fail = why
        return False

    def put(self, c, t):
        self.noted = getattr(self, "noted", set())
        ok = self.wv.collapse(c, t)
        if ok:
            self.note(c)
        return ok

    def note(self, c):
        """Counts a decided cell once: per place and piece, and the cells it blocks."""
        if not hasattr(self, "noted") or self.noted is None:
            self.noted = set()
        if c in self.noted or self.reg[c] < 0:
            return
        self.noted.add(c)
        t = self.wv.tile(c)
        r = self.regions[self.reg[c]]
        r["count"][GROUP[t]] = r["count"].get(GROUP[t], 0) + 1
        if t not in WALK:
            r["block"] += 1

    def weight(self, c, t, opts):
        n, wv = self.n, self.wv
        r = self.regions[self.reg[c]]
        pr = r["prof"]
        g = GROUP[t]
        if t == ID[pr["ground"]]:
            return 1000
        x, z = c % n, c // n
        if g == "TRAIL":
            if any(GROUP[o] != "TRAIL" for o in opts):
                return 0                         # a trail only goes on from a trail
            m = trail_mask(t)
            new, loop = 0, 0
            for d in range(4):
                nc = (z + DZ[d]) * n + x + DX[d]
                if m >> d & 1 and not wv.decided(nc):
                    new += 1
                    if wv.wave[nc] & ~TRAILS == 0:
                        loop += 1                # that cell is already a trail to be: joining it closes a ring
            w = 1000 if m in (5, 10) else (300 if popcount(m) == 2 else (60 if popcount(m) == 3 else 5))
            done = r["count"].get("TRAIL", 0)
            if popcount(m) == 1:
                w = 0 if done * 2 < r.get("trail_max", 6) else 300
            if loop:
                w //= 60
            if done >= r.get("trail_max", 6) and new > 0:
                w = 0
            return w
        w = pr["w"].get(g, 0)
        if r["count"].get(g, 0) >= pr["cap"].get(g, 999):
            return 0
        if g in ("SEAT", "CHAIR"):
            # only drawn up to a fire, a table or a desk that already stands there: a seat never calls for one
            d = dirn(t)
            nc = (z + DZ[d]) * n + x + DX[d]
            if self.walls[c] >> d & 1 or not wv.decided(nc):
                return 0
        if t not in WALK and g not in FORCED and r["block"] * 100 >= r["cover"] * len(r["cells"]):
            return 0
        if g in ("TREE", "BUSH", "HAY", "CRATE", "RACK", "PEW", "GRAVE", "VEG", "FLOWERS"):
            w = w * r["lean"] // 100             # how full this place is on this seed
        same, extra = 0, 0
        for d in range(4):
            if self.walls[c] >> d & 1:
                continue
            nc = (z + DZ[d]) * n + x + DX[d]
            if wv.decided(nc):
                ng = GROUP[wv.tile(nc)]
                if ng == g:
                    same += 1
                extra += ATTRACT.get((g, ng), 0)
        w = w * (1 + pr["aff"].get(g, 0) * same)
        if g in pr["w"] or g in IMPLIED.get("WATER", []) and "WATER" in pr["w"] or g == "NIGHTSTAND":
            w += extra
        if g == "HEDGE" and self.edge[c]:
            w *= 12
        return w

    # ---- every walkable cell of a place can be reached: a natural piece in the way is taken out --------------------
    def repair(self):
        n = self.n
        T = self.tiles
        self.repairs = 0
        self.cut_off = 0
        for r in self.regions:
            ground = ID[r["prof"]["ground"]]
            for _ in range(40):
                walk = [c for c in r["cells"] if T[c] in WALK]
                if not walk:
                    break
                seen = set()
                comps = []
                for s0 in walk:
                    if s0 in seen:
                        continue
                    comp, q = [], [s0]
                    seen.add(s0)
                    while q:
                        c = q.pop()
                        comp.append(c)
                        x, z = c % n, c // n
                        for d in range(4):
                            if self.walls[c] >> d & 1 and (c, d) not in self.doors:
                                continue
                            nx, nz = x + DX[d], z + DZ[d]
                            nc = nz * n + nx
                            if 0 <= nx < n and 0 <= nz < n and nc not in seen and self.reg[nc] == self.reg[c] and T[nc] in WALK:
                                seen.add(nc)
                                q.append(nc)
                    comps.append(comp)
                # pieces of one place that are apart on the grid (the wood round a plot) are not cut off
                comps.sort(key=len, reverse=True)
                small = [cp for cp in comps[1:] if len(cp) < 6 or r["key"][0] == "room"]
                if not small:
                    break
                fixed = False
                for comp in small:
                    for c in comp:
                        x, z = c % n, c // n
                        for d in range(4):
                            nx, nz = x + DX[d], z + DZ[d]
                            nc = nz * n + nx
                            if 0 <= nx < n and 0 <= nz < n and self.reg[nc] == self.reg[c] and not (self.walls[c] >> d & 1) and T[nc] not in WALK and GROUP[T[nc]] not in ("BEDH", "BEDF", "FIRE", "TABLE", "WATER"):
                                T[nc] = ground
                                self.repairs += 1
                                fixed = True
                                break
                        if fixed:
                            break
                    if fixed:
                        break
                if not fixed:
                    self.cut_off += len(small)
                    break
        self.props = sum(1 for c in range(n * n) if self.reg[c] >= 0 and T[c] not in NATURAL and T[c] not in GROUND)
        self.scenery = sum(1 for c in range(n * n) if self.reg[c] >= 0 and T[c] in NATURAL)


def generate(site):
    why = []
    it = Items(site)
    it.layout()
    for attempt in range(8):
        it.attempt = attempt
        it.noted = set()
        if it.run():
            it.retries = why
            return it
        why.append(it.fail)
    return None


if __name__ == "__main__":
    import collections, time, sys
    N = int(sys.argv[1]) if len(sys.argv) > 1 else 20
    print("tiles", NT)
    for size in range(4):
        t0 = time.time()
        att = collections.Counter(); whys = collections.Counter(); fails = 0
        cells = []; coll = []; prop = []; props = []; scen = []; rep = []; missed = 0; cut = 0
        for seed in range(1, N + 1):
            s = S.generate(seed, size)
            it = generate(s)
            if it is None:
                fails += 1
                continue
            att[it.attempt] += 1
            for w in it.retries:
                whys[w] += 1
            cells.append(sum(1 for r in it.reg if r >= 0)); coll.append(it.collapses); prop.append(it.wv.propagations)
            props.append(it.props); scen.append(it.scenery); rep.append(it.repairs); missed += it.missed; cut += it.cut_off
        m = lambda a: sum(a) // max(1, len(a))
        print(S.SIZE_NAME[size], "failed", fails, "of", N, "| attempts", sorted(att.items()), dict(whys), "| item cells", m(cells), "decisions", m(coll), "neighbour updates", m(prop),
              "| props", m(props), "scenery", m(scen), "| repairs/map", round(sum(rep) / max(1, len(rep)), 1), "missed musts", missed, "cut off", cut, "| %.1f s/map" % ((time.time() - t0) / N))
