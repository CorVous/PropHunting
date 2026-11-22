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
         "NIGHTSTAND", "WARDROBE", "DESK", "ARMCHAIR", "PLANT", "SHELF", "COUNTER", "STOVE", "FRIDGE",
         "SINK", "TOILET", "TUB", "BASIN", "TILL", "WORKBENCH", "TOOLCHEST", "ALTAR",
         "LAMP", "COATRACK", "BENCH", "DRESSER", "TOYBOX", "WASHER", "BASKET", "FIREPLACE", "CABINET", "FREEZER", "DISPLAY", "ARCADE", "BIN",
         "TABLESET", "COUNTERKIT", "DESKPC", "SHELFSET", "NIGHTLAMP", "DRESSERSET", "CABINETSET",    # a piece with things on it
         "VIEW_NS", "VIEW_EW", "VIEWT_NS", "VIEWT_EW"]           # the rug between a TV and its sofa (VIEWT: with the coffee table on it)
for base in ("SEAT", "CHAIR", "BEDH", "BEDF", "TV", "SOFA"):
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
    return {"TABLESET": "TABLE", "COUNTERKIT": "COUNTER", "DESKPC": "DESK", "SHELFSET": "SHELF", "NIGHTLAMP": "NIGHTSTAND", "DRESSERSET": "DRESSER", "CABINETSET": "CABINET"}.get(n, n)


GROUP = [group(t) for t in range(NT)]
GROUND = {ID[n] for n in ("FLOOR", "GRASS", "LAWN", "PAVING", "BOARDS", "RUG")}
WALK = GROUND | {t for t in range(NT) if GROUP[t] in ("FERN", "MUSHROOM", "FLOWERS", "TRAIL", "BLANKET", "REEDS", "VEG", "LINE", "VIEW")}
NATURAL = {t for t in range(NT) if GROUP[t] in ("TREE", "BUSH", "FERN", "ROCK", "LOG", "STUMP", "MUSHROOM", "FLOWERS", "WATER", "REEDS", "HEDGE", "FLOWERBED", "TRAIL")}
NEED_WALL = {"LAMP", "COATRACK", "BENCH", "DRESSER", "WASHER", "FIREPLACE", "CABINET", "FREEZER", "ARCADE", "NIGHTSTAND", "WARDROBE", "DESK", "SHELF", "COUNTER", "STOVE", "FRIDGE", "SINK", "TOILET", "TUB", "BASIN", "TILL", "WORKBENCH", "ALTAR"}
FORCED = {"CHAIR", "SEAT", "BEDF", "POST", "LINE", "TRAIL", "VIEW", "VIEWT", "SOFA", "TV"}
TWO = {ID[x] for x in ("TABLESET", "COUNTERKIT", "DESKPC", "SHELFSET", "NIGHTLAMP", "DRESSERSET", "CABINETSET")} | {t for t in range(NT) if GROUP[t] == "VIEWT"}      # a piece with another on it
SETRATE = {"TABLE": 7, "COUNTER": 4, "DESK": 8, "SHELF": 7, "NIGHTSTAND": 7, "DRESSER": 6, "CABINET": 5}
FORCEDW = {"VIEW": 5, "VIEWT": 1, "SOFA": 5, "TV": 5, "LINE": 3, "POST": 2}      # pieces that only come with another: never cut by the cover budget


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
    if ga == "TV":                                                    # a TV stands at a wall and looks out over a rug
        if d == dirn(a):
            return b == VOID
        if d == (dirn(a) + 2) & 3:                                    # (in a small room the sofa stands right before it)
            return gb in ("VIEW", "VIEWT") and NAMES[b].endswith("_NS" if d & 1 == 0 else "_EW") or b == ID["SOFA%d" % dirn(a)]
        return True
    if ga == "SOFA":                                                  # a sofa looks along a rug
        if d == dirn(a):
            return gb in ("VIEW", "VIEWT") and NAMES[b].endswith("_NS" if d & 1 == 0 else "_EW") or b == ID["TV%d" % d]
        return True
    if ga in ("VIEW", "VIEWT"):                                       # the rug runs on until a TV or a sofa that looks back along it
        if (d & 1) == (0 if NAMES[a].endswith("_NS") else 1):
            return gb in ("VIEW", "VIEWT") and NAMES[b][-3:] == NAMES[a][-3:] or b == ID["TV%d" % d] or b == ID["SOFA%d" % od]
        return True
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
    # indoors: a cap of (n, m) is n in a room of one outer cell and m more for every further cell
    "living": prof("BOARDS", dict(SOFA=25, ARMCHAIR=60, PLANT=120, LAMP=120, SHELF=220, CABINET=120, FIREPLACE=60, TABLE=15, CHAIR=2500, RUG=40), must=[("TV", "SOFA", "ARMCHAIR")],
                   cap=dict(TV=1, SOFA=(2, 1), ARMCHAIR=(1, 1), PLANT=(1, 1), LAMP=(1, 1), SHELF=(2, 1), CABINET=(1, 1), FIREPLACE=1, TABLE=1, CHAIR=2, RUG=(4, 3), VIEWT=1), aff=dict(RUG=6), cover=(26, 38)),
    "kitchen": prof("BOARDS", dict(COUNTER=400, CABINET=150, TABLE=25, CHAIR=4000, BIN=80, PLANT=30), must=["STOVE", "FRIDGE", "SINK"],
                    cap=dict(COUNTER=(4, 3), CABINET=(1, 1), TABLE=1, CHAIR=4, BIN=1, PLANT=1), aff=dict(COUNTER=3), cover=(28, 40)),
    "dining": prof("BOARDS", dict(TABLE=30, CHAIR=8000, CABINET=200, SHELF=150, PLANT=100, LAMP=80, RUG=30), must=["TABLE"],
                   cap=dict(TABLE=(2, 1), CHAIR=(6, 3), CABINET=(1, 1), SHELF=(1, 1), PLANT=(1, 1), LAMP=1, RUG=(4, 2)), aff=dict(TABLE=25, RUG=6), cover=(28, 40)),
    "dinerdining": prof("BOARDS", dict(TABLE=110, CHAIR=8000, COUNTER=250, PLANT=60, ARCADE=40, BIN=40), must=["TABLE"],
                        cap=dict(TABLE=(3, 3), CHAIR=(8, 8), COUNTER=(3, 2), PLANT=(1, 1), ARCADE=1, BIN=1), aff=dict(COUNTER=4), cover=(30, 42)),
    "bedroom": prof("BOARDS", dict(BEDH=8, WARDROBE=250, DRESSER=200, DESK=120, CHAIR=4000, NIGHTSTAND=1, RUG=60, PLANT=80, LAMP=100, SHELF=150, TOYBOX=60, BASKET=40), must=["BEDH"],
                    cap=dict(BEDH=(1, 1), WARDROBE=(1, 1), DRESSER=1, DESK=1, CHAIR=1, NIGHTSTAND=2, RUG=(4, 2), PLANT=1, LAMP=1, SHELF=(1, 1), TOYBOX=1, BASKET=1), aff=dict(RUG=6), cover=(30, 42)),
    "bunkroom": prof("BOARDS", dict(BEDH=60, WARDROBE=200, NIGHTSTAND=1, CRATE=40, RUG=40, LAMP=60, DRESSER=100), must=["BEDH", "BEDH"],
                     cap=dict(BEDH=(2, 2), WARDROBE=(1, 1), NIGHTSTAND=3, CRATE=2, RUG=4, LAMP=1, DRESSER=1), cover=(32, 45)),
    "bath": prof("BOARDS", dict(WASHER=200, BASKET=120, CABINET=150, PLANT=60, RUG=60), must=["TOILET", "BASIN", "TUB"],
                 cap=dict(WASHER=1, BASKET=1, CABINET=1, PLANT=1, RUG=2), cover=(25, 40)),
    "office": prof("BOARDS", dict(DESK=60, CHAIR=6000, SHELF=300, CABINET=220, PLANT=120, LAMP=100, BIN=60, ARMCHAIR=40, RUG=30), must=["DESK"],
                   cap=dict(DESK=(1, 1), CHAIR=(2, 1), SHELF=(2, 2), CABINET=(1, 1), PLANT=(1, 1), LAMP=1, BIN=1, ARMCHAIR=1, RUG=(3, 2)), aff=dict(RUG=6), cover=(28, 40)),
    "hall": prof("BOARDS", dict(COATRACK=250, BENCH=200, SHELF=150, PLANT=120, CABINET=100, RUG=60, LAMP=60),
                 cap=dict(COATRACK=1, BENCH=1, SHELF=1, PLANT=(1, 1), CABINET=1, RUG=(3, 3), LAMP=1), aff=dict(RUG=8), cover=(10, 20)),
    "store": prof("BOARDS", dict(CRATE=260, SHELF=400, BARREL=90, CABINET=100, BIN=40), cap=dict(BIN=1, CABINET=(1, 1)), aff=dict(CRATE=2), cover=(32, 46)),
    "shop": prof("BOARDS", dict(RACK=200, FREEZER=250, DISPLAY=60, COUNTER=100, PLANT=30, BIN=30), must=["TILL"],
                 cap=dict(FREEZER=(2, 2), DISPLAY=(1, 1), COUNTER=2, PLANT=1, BIN=1), aff=dict(RACK=8), cover=(28, 40)),
    "arcade": prof("BOARDS", dict(ARCADE=500, TABLE=30, CHAIR=5000, BIN=40, PLANT=30, DISPLAY=40), must=["TILL"],
                   cap=dict(ARCADE=(4, 4), TABLE=(1, 1), CHAIR=(3, 3), BIN=1, PLANT=1, DISPLAY=(1, 1)), cover=(26, 38)),
    "workshop": prof("BOARDS", dict(WORKBENCH=200, TOOLCHEST=120, SHELF=300, CRATE=80, BARREL=80, CABINET=100, BIN=40), must=["WORKBENCH"],
                     cap=dict(WORKBENCH=(1, 1), TOOLCHEST=(1, 1), CABINET=(1, 1), BIN=1), cover=(24, 36)),
    "barn": prof("BOARDS", dict(HAY=220, BARREL=80, CRATE=80, WORKBENCH=60), cap=dict(WORKBENCH=1), aff=dict(HAY=3), cover=(26, 40)),
    "cabin": prof("BOARDS", dict(SOFA=20, TABLE=20, CHAIR=3000, STOVE=80, FIREPLACE=150, SHELF=200, RUG=40, ARMCHAIR=60, LAMP=80, PLANT=40, CABINET=100, COUNTER=120), must=["SOFA", "TABLE"],
                  cap=dict(TV=1, SOFA=(2, 1), TABLE=(1, 1), CHAIR=4, STOVE=1, FIREPLACE=1, SHELF=(1, 1), RUG=(4, 2), ARMCHAIR=(1, 1), LAMP=1, PLANT=1, CABINET=1, COUNTER=3, VIEWT=1), aff=dict(RUG=6), cover=(28, 40)),
    "nave": prof("BOARDS", dict(PEW=320, PLANT=60, BENCH=80, CABINET=60), must=["ALTAR"], cap=dict(PLANT=2, BENCH=2, CABINET=1), aff=dict(PEW=8), cover=(30, 44)),
    "playroom": prof("BOARDS", dict(RUG=100, SHELF=250, TOYBOX=200, CRATE=50, TABLE=30, CHAIR=4000, ARMCHAIR=50, LAMP=60),
                     cap=dict(SHELF=(1, 1), TOYBOX=(2, 1), CRATE=2, TABLE=1, CHAIR=3, ARMCHAIR=1, LAMP=1, RUG=(6, 3)), aff=dict(RUG=6), cover=(22, 34)),
}
ROOM_KIND = {1: "living", 2: "kitchen", 3: "dining", 4: "bedroom", 5: "bath", 6: "office", 7: "bedroom", 8: "hall", 9: "store", 37: "bedroom", 38: "playroom", 39: "store",
             20: "store", 21: "store", 22: "office", 24: "shop", 25: "store", 26: "office", 27: "workshop", 28: "store", 34: "barn", 35: "store", 36: "workshop",
             29: "cabin", 33: "bunkroom", 59: "arcade", 60: "kitchen", 61: "store", 64: "nave", 65: "office", 30: "dinerdining", 31: "kitchen", 32: "store"}
YARD_KIND = {S.Z_DINER: "patio", S.Z_SHOP: "yard", S.Z_GARAGE: "yard", S.Z_WAREHOUSE: "yard", S.Z_FAIR: "yard", S.Z_BARN: "farm", S.Z_CHAPEL: "churchyard",
             S.Z_CABIN: "cabinyard", S.Z_PARK: "park", S.Z_ZOO: "zoo"}
HOUSE_PURPOSES = ["picnic", "veg", "drying", "firepit", "campsite"]      # dealt from a shuffled deck, as YardPurposes does
ATTRACT = {("ARMCHAIR", "VIEW"): 500, ("LAMP", "SOFA"): 400, ("ARMCHAIR", "VIEWT"): 500, ("NIGHTSTAND", "BEDH"): 900, ("REEDS", "WATER"): 700, ("WATER", "WATER"): 900, ("COUNTER", "STOVE"): 300, ("COUNTER", "SINK"): 300, ("ARMCHAIR", "RUG"): 60,
           ("TABLE", "RUG"): 60, ("LOG", "FIRE"): 0}
IMPLIED = {"BEDH": ["BEDF"], "LINE": ["POST"], "WATER": ["REEDS"], "TV": ["VIEW", "VIEWT", "SOFA"], "SOFA": ["VIEW", "VIEWT", "TV"]}


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
            groups = set(pr["w"])
            for alts in pr["must"]:
                groups |= set(alts) if isinstance(alts, tuple) else {alts}
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
            for alts in musts:
              for g in (alts if isinstance(alts, tuple) else (alts,)):      # the first of the alternatives that fits
                cands = [c for c in r["cells"] if not wv.decided(c) and any(wv.wave[c] >> t & 1 and GROUP[t] == g for t in range(NT))]
                if g in ("FIRE", "TABLE", "WATER", "TENT"):      # with room around it
                    inner = [c for c in cands if not self.edge[c]]
                    cands = inner or cands
                if not cands:
                    if g == (alts[-1] if isinstance(alts, tuple) else alts):
                        self.missed += 1
                    continue
                c = cands[rng.range(len(cands))]
                opts = [t for t in range(NT) if wv.wave[c] >> t & 1 and GROUP[t] == g]
                if not self.put(c, opts[rng.range(len(opts))]):
                    return self.failed("must " + g)
                break
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
                    # a cell another piece calls for (the rug before a TV, the line to a post): what may end or carry it on
                    rr = self.regions[self.reg[c]]
                    wts = [0 if self.capped(rr, GROUP[t]) else FORCEDW.get(GROUP[t], 1) for t in opts]
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
        if self.capped(r, g):
            return 0
        if g in ("VIEW", "VIEWT"):
            return 0                             # a rug strip only runs on from a TV or a sofa
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
        if g in SETRATE:
            w = w * (SETRATE[g] if t in TWO else 10 - SETRATE[g]) // 5
        if g in ("PLANT", "LAMP") and popcount(self.walls[c]) >= 2:
            w *= 5                               # corners
        return w

    def capped(self, r, g):
        cap = r["prof"]["cap"].get(g, 999)
        if isinstance(cap, tuple):
            cap = cap[0] + cap[1] * max(0, len(r["cells"]) // (F * F) - 1)
        return r["count"].get(g, 0) >= cap

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
                            if 0 <= nx < n and 0 <= nz < n and self.reg[nc] == self.reg[c] and not (self.walls[c] >> d & 1) and T[nc] not in WALK and GROUP[T[nc]] not in ("BEDH", "BEDF", "FIRE", "TABLE", "WATER", "TV", "SOFA", "VIEWT"):
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
        isprop = lambda t: t not in NATURAL and t not in GROUND and GROUP[t] not in ("BEDF", "VIEW", "LINE")
        self.props = sum((1 if isprop(T[c]) else 0) + (1 if T[c] in TWO else 0) for c in range(n * n) if self.reg[c] >= 0)
        self.in_cells = sum(1 for c in range(n * n) if self.indoor[c])
        self.in_props = sum(1 for c in range(n * n) if self.indoor[c] and isprop(T[c]))
        self.in_top = sum(1 for c in range(n * n) if self.indoor[c] and T[c] in TWO)
        self.in_block = sum(1 for c in range(n * n) if self.indoor[c] and T[c] not in WALK)
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
        cells = []; coll = []; prop = []; props = []; scen = []; rep = []; missed = 0; cut = 0; ic = 0; ip = 0; itp = 0; ib = 0
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
            props.append(it.props); scen.append(it.scenery); rep.append(it.repairs); missed += it.missed; cut += it.cut_off; ic += it.in_cells; ip += it.in_props; itp += it.in_top; ib += it.in_block
        m = lambda a: sum(a) // max(1, len(a))
        print(S.SIZE_NAME[size], "failed", fails, "of", N, "| attempts", sorted(att.items()), dict(whys), "| item cells", m(cells), "decisions", m(coll), "neighbour updates", m(prop),
              "| props", m(props), "scenery", m(scen), "| repairs/map", round(sum(rep) / max(1, len(rep)), 1), "missed musts", missed, "cut off", cut, "| indoor: floor pieces per 100 m2 %.1f, on furniture %.1f, floor blocked %d%%" % (100.0 * ip / ic, 100.0 * itp / ic, 100 * ib // ic), "| %.1f s/map" % ((time.time() - t0) / N))
