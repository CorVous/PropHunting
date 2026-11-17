"""Adds bigger house plans to the plan library (Assets/Scripts/Gen/GenPlans.cs). Cor: more inside than outside.

The library's original plans came from a tool that is not in this repository (prototype/tools/planlib). This one
keeps them as they are and appends plans of 11 to 20 cells: a hall runs back from the front door, and every room
opens off it, so the house keeps today's room sizes (one or two cells) while having many more of them. Each plan is
checked here against the plan rules the generator's layout tests enforce (MapGenSiteTests.cs):

    T1  a rectangle or an L (at most 6 outline corners), no hole, no notch
    T4  the front door is in the hall (or the living room when there is no hall)
    T5  a living room, a kitchen, a bedroom and a bathroom
    T6  interior doors only join rooms that may connect; a private room has exactly one door, to the hall or the
        living room
    T7  no outside door into a private room
    T9  the back door leaves from a public room (it opens onto the patio)
    T13 every room is one piece; every room can be reached from the front door

Run it again to regenerate (it replaces its own block, marked below, and leaves the rest):

    python Tools/make_plans.py
"""
import os
import random
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
PLANS = os.path.join(ROOT, "Assets", "Scripts", "Gen", "GenPlans.cs")
MARK_A = "        // ---- bigger houses (Tools/make_plans.py)"
MARK_B = "        // ---- end of bigger houses"

Z_HOUSE = 1
LIVING, KITCHEN, DINING, BEDROOM, BATH, OFFICE, KIDS, HALL = 1, 2, 3, 4, 5, 6, 7, 8
LETTER = {LIVING: "L", KITCHEN: "K", DINING: "D", BEDROOM: "B", BATH: "T", OFFICE: "O", KIDS: "C", HALL: "H"}
PRIVATE = {BEDROOM, BATH, OFFICE, KIDS}
PUBLIC = {HALL, LIVING, DINING, KITCHEN}
# direction codes of the plan format: 0 back (+y), 1 right (+x), 2 front (-y), 3 left (-x); row 0 is the front
DX = {0: 0, 1: 1, 2: 0, 3: -1}
DY = {0: 1, 1: 0, 2: -1, 3: 0}


def make(rng, w, d, cut):
    """One attempt at a plan w x d (cut: cells removed at a back corner for an L, as (cw, cd, left))."""
    cells = {(x, y) for x in range(w) for y in range(d)}
    if cut:
        cw, cd, left = cut
        for x in range(cw):
            for y in range(d - cd, d):
                cells.discard((x if left else w - 1 - x, y))
    room = {}
    rooms = []   # list of cell lists
    # the hall: a column from the front, reaching the back row of the cells it runs through, sometimes turning
    # along the back row
    hx = rng.choice([x for x in range(1, w - 1)]) if w >= 3 else 0
    hall = [(hx, y) for y in range(d) if (hx, y) in cells]
    hall = hall[:max(2, len(hall) - rng.choice([0, 1]))]
    if rng.random() < 0.4 and w >= 5:
        y = hall[-1][1]
        step = rng.choice([-1, 1])
        x = hx + step
        while (x, y) in cells and 0 < x < w - 1 and rng.random() < 0.7:
            hall.append((x, y))
            x += step
    for c in hall:
        room[c] = 0
    rooms.append(list(hall))

    def free(c):
        return c in cells and c not in room

    def touches_hall(cs):
        return any(room.get((x + DX[k], y + DY[k])) == 0 for x, y in cs for k in range(4))

    # rooms: cells beside the hall first; a room is one cell, or two in a line (rarely an L of three for the
    # living room), and must touch the hall
    order = sorted(cells - set(hall), key=lambda c: (0 if touches_hall([c]) else 1, rng.random()))
    for c in order:
        if c in room:
            continue
        cs = [c]
        if not touches_hall(cs):
            continue   # filled later by merging into a neighbour
        # now and then a double room: two by two cells (Cor: some rooms double size)
        if rng.random() < 0.3:
            squares = []
            for ox in (-1, 0):
                for oy in (-1, 0):
                    sq = [(c[0] + ox + i, c[1] + oy + j) for i in (0, 1) for j in (0, 1)]
                    if all(free(s) for s in sq):
                        squares.append(sq)
            if squares:
                cs = rng.choice(squares)
                for x in cs:
                    room[x] = len(rooms)
                rooms.append(cs)
                continue
        nbrs = [(c[0] + DX[k], c[1] + DY[k]) for k in range(4)]
        far = [n for n in nbrs if free(n) and not touches_hall([n])]
        grow = [n for n in nbrs if free(n)]
        if far:
            cs.append(rng.choice(far))
        elif grow and rng.random() < 0.35:
            cs.append(rng.choice(grow))
        for x in cs:
            room[x] = len(rooms)
        rooms.append(cs)
    # cells still free: join the neighbouring room that is a single cell (keeps rooms at most two cells)
    for c in sorted(cells - set(room)):
        opts = [room[(c[0] + DX[k], c[1] + DY[k])] for k in range(4) if room.get((c[0] + DX[k], c[1] + DY[k]), 0) > 0]
        opts = [r for r in opts if len(rooms[r]) < 4]
        opts = [r for r in opts if len(rooms[r]) == 1]
        if not opts:
            return None
        r = rng.choice(opts)
        room[c] = r
        rooms[r].append(c)

    n = len(rooms)
    if n < 6:
        return None
    # roles: the living room at the front beside the front door's hall cell, the kitchen on the back row (its back
    # door), a dining room beside the kitchen or the living room now and then, then the private rooms
    role = [0] * n
    role[0] = HALL
    front_rooms = [r for r in range(1, n) if any(y == 0 for x, y in rooms[r])]
    back_y = max(y for x, y in cells)
    back_rooms = [r for r in range(1, n) if any((x, y + 1) not in cells for x, y in rooms[r]) and r not in front_rooms]
    if not front_rooms or not back_rooms:
        return None
    big_front = [r for r in front_rooms if len(rooms[r]) >= 2] or front_rooms
    liv = rng.choice(big_front)
    role[liv] = LIVING
    backs = [r for r in back_rooms if role[r] == 0]
    if not backs:
        return None
    kit = rng.choice(backs)
    role[kit] = KITCHEN
    adj = adjacency(rooms, room)
    left = [r for r in range(1, n) if role[r] == 0]
    rng.shuffle(left)
    if left and rng.random() < 0.7:
        dn = [r for r in left if kit in adj[r] or liv in adj[r]]
        if dn:
            role[dn[0]] = DINING
            left.remove(dn[0])
    # the bathrooms take one-cell rooms; the rest are bedrooms, kids' rooms and offices (a double room is a main
    # bedroom more often than not)
    singles = [r for r in left if len(rooms[r]) == 1]
    if not singles:
        return None
    baths = 1 + (1 if len(left) >= 7 and len(singles) >= 2 else 0)
    for r in singles[:baths]:
        role[r] = BATH
        left.remove(r)
    extra = [BEDROOM, BEDROOM, KIDS, OFFICE, BEDROOM, KIDS, OFFICE, BEDROOM, BEDROOM, KIDS]
    rng.shuffle(extra)
    privs = [BEDROOM] + extra
    left.sort(key=lambda r: -len(rooms[r]))
    for i, r in enumerate(left):
        role[r] = privs[i] if i < len(privs) else BEDROOM
    # a bathroom is one cell
    for r in range(n):
        if role[r] == BATH and len(rooms[r]) > 1:
            return None

    # doors: every room one door to the hall (on a random shared edge); the public rooms also join each other
    doors = []
    for r in range(1, n):
        edges = shared_edges(rooms[r], rooms[0])
        if not edges:
            if role[r] in PRIVATE:
                # a private room may open onto the living room instead
                edges = shared_edges(rooms[r], rooms[liv])
            if not edges:
                return None
        doors.append(rng.choice(edges))
    for a in range(1, n):
        for b in range(a + 1, n):
            if role[a] in PUBLIC and role[b] in PUBLIC and b in adj[a] and rng.random() < 0.6:
                doors.append(rng.choice(shared_edges(rooms[a], rooms[b])))
    front = (hx, 0, 2)
    kc = [c for c in rooms[kit] if (c[0], c[1] + 1) not in cells]
    back = (kc[0][0], kc[0][1], 0)
    return w, d, cells, room, rooms, role, doors, front, back


def adjacency(rooms, room):
    adj = [set() for _ in rooms]
    for r, cs in enumerate(rooms):
        for x, y in cs:
            for k in range(4):
                o = room.get((x + DX[k], y + DY[k]))
                if o is not None and o != r:
                    adj[r].add(o)
    return adj


def shared_edges(a, b):
    bs = set(b)
    out = []
    for x, y in a:
        for k in range(4):
            if (x + DX[k], y + DY[k]) in bs:
                out.append((x, y, k))
    return out


def check(plan):
    """The plan rules (see the module's comment). True when the plan passes."""
    w, d, cells, room, rooms, role, doors, front, back = plan
    # T1: corners (a rectangle 4, an L 6) and no notch: count grid points with 1 or 3 cells around
    corners = 0
    for x in range(w + 1):
        for y in range(d + 1):
            k = sum((x - i, y - j) in cells for i in (0, 1) for j in (0, 1))
            if k in (1, 3):
                corners += 1
            if k == 2 and ((x - 1, y - 1) in cells) == ((x, y) in cells):
                return False
    if corners > 6:
        return False
    # T13: rooms in one piece
    for cs in rooms:
        seen, todo = {cs[0]}, [cs[0]]
        s = set(cs)
        while todo:
            x, y = todo.pop()
            for k in range(4):
                n = (x + DX[k], y + DY[k])
                if n in s and n not in seen:
                    seen.add(n)
                    todo.append(n)
        if len(seen) != len(cs):
            return False
    # T5
    if not {LIVING, KITCHEN, BEDROOM, BATH} <= set(role):
        return False
    # T6: interior doors
    count = [0] * len(rooms)
    links = [set() for _ in rooms]
    for x, y, k in doors:
        a, b = room[(x, y)], room[(x + DX[k], y + DY[k])]
        ra, rb = role[a], role[b]
        ok = (ra in PUBLIC and rb in PUBLIC) or (ra in PRIVATE and rb in (HALL, LIVING)) or (rb in PRIVATE and ra in (HALL, LIVING))
        if not ok or b in links[a]:
            return False
        links[a].add(b)
        links[b].add(a)
        count[a] += 1
        count[b] += 1
    for r in range(len(rooms)):
        if role[r] in PRIVATE and count[r] != 1:
            return False
    # T4, T7, T9: the outside doors
    fx, fy, fk = front
    if role[room[(fx, fy)]] != HALL or (fx + DX[fk], fy + DY[fk]) in cells:
        return False
    bx, by, bk = back
    if role[room[(bx, by)]] not in PUBLIC or (bx + DX[bk], by + DY[bk]) in cells:
        return False
    # every room reached from the hall
    seen, todo = {0}, [0]
    while todo:
        r = todo.pop()
        for o in links[r]:
            if o not in seen:
                seen.add(o)
                todo.append(o)
    return len(seen) == len(rooms)


def encode(plan):
    w, d, cells, room, rooms, role, doors, front, back = plan
    vals = [Z_HOUSE, w, d, len(rooms)] + role
    for y in range(d):
        for x in range(w):
            vals.append(room.get((x, y), -1))
    vals.append(len(doors))
    for x, y, k in doors:
        vals.append((y * w + x) * 4 + k)
    vals.append((front[1] * w + front[0]) * 4 + front[2])
    vals.append((back[1] * w + back[0]) * 4 + back[2])
    rows = "/".join("".join(LETTER[role[room[(x, y)]]] if (x, y) in room else "." for x in range(w)) for y in range(d))
    return vals, rows


def parse_library(text):
    """The library's plans as value lists (before the bigger houses)."""
    body = text[text.index("public static readonly int[] Data = {"):]
    body = body[body.index("{") + 1:body.index("    };")]
    plans = []
    for line in body.split("\n"):
        nums = [int(v) for v in re.findall(r"-?\d+", line.split("//")[0])]
        if nums:
            plans.append((nums, line.split("//")[1].strip() if "//" in line else ""))
    return plans


def stretch(vals, axis, at, k):
    """A plan with column (axis 0) or row (axis 1) `at` repeated k more times: every room keeps its neighbours and
    its doors, the rooms crossing that column or row grow."""
    zone, w, d, nr = vals[0], vals[1], vals[2], vals[3]
    roles = vals[4:4 + nr]
    grid = vals[4 + nr:4 + nr + w * d]
    rest = vals[4 + nr + w * d:]
    nd = rest[0]
    doors, front, back = rest[1:1 + nd], rest[1 + nd], rest[2 + nd]
    w2, d2 = (w + k, d) if axis == 0 else (w, d + k)
    g2 = []
    for y in range(d2):
        for x in range(w2):
            ox = x if axis == 1 or x <= at else max(at, x - k)
            oy = y if axis == 0 or y <= at else max(at, y - k)
            g2.append(grid[oy * w + ox])

    def move(code):
        cell, dr = code >> 2, code & 3
        x, y = cell % w, cell // w
        if axis == 0:
            # a door on the far side of the stretched column moves with it, one in it stays on its first copy
            if x > at or (x == at and dr == 1):
                x += k
        else:
            if y > at or (y == at and dr == 0):
                y += k
        return (y * w2 + x) * 4 + dr
    return [zone, w2, d2, nr] + roles + g2 + [nd] + [move(c) for c in doors] + [move(front), move(back)]


def stretched(text):
    """Bigger versions of every plan but the houses: one column or row repeated, up to 16 cells (Cor: more inside)."""
    out, seen = [], set()
    for vals, rows in parse_library(text):
        zone, w, d, nr = vals[0], vals[1], vals[2], vals[3]
        if zone == Z_HOUSE:
            continue
        cells = sum(1 for v in vals[4 + nr:4 + nr + w * d] if v >= 0)
        mine = []
        for axis in (0, 1):
            for at in range(w if axis == 0 else d):
                line = [vals[4 + nr + (y * w + at if axis == 0 else at * w + y)] for y in range(d if axis == 0 else w)]
                if min(line) < 0:
                    continue   # a column or row with a gap would widen the gap: the building, not its yard, should grow
                for k in (1, 2):
                    v2 = stretch(vals, axis, at, k)
                    c2 = sum(1 for v in v2[4 + nr:4 + nr + v2[1] * v2[2]] if v >= 0)
                    if c2 > 16 or v2[1] > 6 or v2[2] > 5 or c2 < cells + 2:
                        continue
                    key = tuple(v2)
                    if key in seen:
                        continue
                    seen.add(key)
                    mine.append((v2, "%s stretched" % rows))
        random.Random(cells * 1000 + len(out)).shuffle(mine)
        out += mine[:3]   # three bigger versions of each plan at most
    print("stretched:", len(out), "plans")
    return out


def main():
    rng = random.Random(14)
    out, keys = [], set()
    shapes = [(3, 4, None), (3, 5, None), (4, 3, None), (4, 4, None), (5, 3, None), (4, 5, None), (5, 4, None),
              (3, 6, None), (4, 4, (1, 1, True)), (4, 5, (1, 2, False)), (5, 4, (2, 1, True)), (5, 4, (1, 2, False)),
              (5, 3, (2, 1, False)), (4, 4, (2, 1, False)),
              (5, 5, None), (6, 4, None), (5, 5, (2, 2, True)), (6, 4, (2, 1, False)), (6, 5, (3, 2, True))]
    per_shape = 6
    for w, d, cut in shapes:
        got, tries = 0, 0
        while got < per_shape and tries < 20000:
            tries += 1
            p = make(rng, w, d, cut)
            if p is None or not check(p):
                continue
            vals, rows = encode(p)
            key = tuple(vals)
            if key in keys:
                continue
            keys.add(key)
            out.append((vals, rows))
            got += 1
        print("%dx%d%s: %d plans (%d tries)" % (w, d, " L" if cut else "", got, tries))

    text = open(PLANS, encoding="utf8").read()
    if MARK_A in text:
        a = text.index(MARK_A)
        b = text.index(MARK_B) + len(MARK_B) + 1
        text = text[:a] + text[b:]
    out += stretched(text)
    data_end = text.index("    };", text.index("public static readonly int[] Data = {"))
    block = MARK_A + "\n" + "".join("        " + ", ".join(str(v) for v in vals) + ",   // " + rows + "\n" for vals, rows in out) + MARK_B + "\n"
    text = text[:data_end] + block + text[data_end:]
    # Count and Start, from the data
    body = text[text.index("public static readonly int[] Data = {"):]
    body = body[body.index("{") + 1:body.index("    };")]
    starts, pos = [], 0
    for line in body.split("\n"):
        code = line.split("//")[0]
        nums = [int(v) for v in re.findall(r"-?\d+", code)]
        if nums:
            starts.append(pos)
            pos += len(nums)
    text = re.sub(r"public const int Count = \d+;", "public const int Count = %d;" % len(starts), text)
    text = re.sub(r"public static readonly int\[\] Start = \{[^}]*\};", "public static readonly int[] Start = { " + ", ".join(map(str, starts)) + " };", text)
    open(PLANS, "w", encoding="utf8", newline="\n").write(text)
    print("library:", len(starts), "plans,", len(out), "of them new")


if __name__ == "__main__":
    main()
