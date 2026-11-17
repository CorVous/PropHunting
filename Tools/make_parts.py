"""Builds Assets/Resources/PropHunt/parts.bytes: the CC0 building parts the map builder dresses outside walls with
(Cor: the buildings should look more interesting; the shortlist from the CC0 mirror, 2026-11-17). One facade kit per
theme: a plain wall, a wall with a window and a wall with a door.

Each part is baked to vertex colours (make_fixtures.load_obj; a window's glass left out so windows stay see-through)
and normalised so the builder can stretch it over any piece of wall:

    x  along the wall, 0..1 (the builder scales it to the span)
    y  up the wall, 0..1 (scaled to the wall's height)
    z  out from the wall face, in metres (0 on the face)

The models.bytes layout is reused (PropPrefabBaker reads it). Two of its fields carry part data: the "pivot" holds
the part's main body colour (r, g, b in 0..1; the builder repaints that colour with the building's own), and the
bounds hold the opening of a window or door part (min = (x0, y0, 0), max = (x1, y1, 0), normalised; zero for a plain
wall). Run it again after changing the list:

    python Tools/make_parts.py <folder with the downloaded OBJ files>
"""
import collections
import math
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(__file__))
import make_fixtures as fx

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "PropHunt", "parts.bytes")

# name (theme_kind), pack/model, thin axis of the model ("x" or "z"), turn so the outside faces +z, depth scale
# ... and the hole of a window or door part (x0, y0, x1, y1, normalised: inside its frame), read off the models
PARTS = [
    ("town_wall",     "kenney-building-kit/wall",                    "x", 1.0, None),
    ("town_window",   "kenney-building-kit/wall-window-square-detailed", "x", 1.0, (0.28, 0.31, 0.72, 0.77)),
    ("town_door",     "kenney-building-kit/wall-doorway-square",     "x", 1.0, (0.28, 0.0, 0.72, 0.86)),
    ("space_wall",    "kenney-space-station-kit/wall",               "z", 1.0, None),
    ("space_window",  "kenney-space-station-kit/wall-window",        "z", 1.0, (0.2, 0.3, 0.8, 0.7)),
    ("space_door",    "kenney-space-station-kit/wall-door",          "z", 1.0, (0.3, 0.0, 0.7, 0.7)),
    ("med_wall",      "kenney-fantasy-town-kit/wall-wood",           "x", 1.5, None),
    ("med_window",    "kenney-fantasy-town-kit/wall-wood-window-glass", "x", 1.5, (0.36, 0.26, 0.64, 0.74)),
    ("med_door",      "kenney-fantasy-town-kit/wall-wood-door",      "x", 1.5, (0.27, 0.0, 0.73, 0.80)),
]


def normalise(tris, thin, depth):
    # turn so the wall runs along x and is thin along z
    if thin == "x":
        tris = [[((p[2], p[1], p[0]), (n[2], n[1], n[0]), c) for p, n, c in tri] for tri in tris]
        # swapping two axes mirrors the mesh: restore the winding
        tris = [[tri[0], tri[2], tri[1]] for tri in tris]
    pts = [p for tri in tris for p, _, _ in tri]
    mn = [min(p[i] for p in pts) for i in range(3)]
    mx = [max(p[i] for p in pts) for i in range(3)]
    w, h = mx[0] - mn[0], mx[1] - mn[1]
    # the face that looks out: the side with the larger spread of geometry (frames and sills stand out)
    zmid = (mn[2] + mx[2]) / 2
    out_front = sum(1 for p in pts if p[2] > zmid) >= sum(1 for p in pts if p[2] < zmid)
    res = []
    for tri in tris:
        t2 = []
        for p, n, c in tri:
            x = (p[0] - mn[0]) / w
            y = (p[1] - mn[1]) / h
            z = ((p[2] - mn[2]) if out_front else (mx[2] - p[2])) * depth
            nz = n[2] if out_front else -n[2]
            t2.append(((x, y, z), (n[0], n[1], nz), c))
        if not out_front:
            # mirrored in z: restore the winding
            t2 = [t2[0], t2[2], t2[1]]
        res.append(t2)
    return res


def area(tri):
    (a, _, _), (b, _, _), (c, _, _) = tri
    u = [b[i] - a[i] for i in range(3)]
    v = [c[i] - a[i] for i in range(3)]
    cr = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
    return 0.5 * math.sqrt(sum(x * x for x in cr))


def body_colour(tris):
    acc = collections.Counter()
    for tri in tris:
        acc[tri[0][2]] += area(tri)
    return acc.most_common(1)[0][0]


def opening(tris):
    """The window or door hole: the largest empty rectangle on the wall's front face, found on a grid."""
    N = 64
    cover = [[False] * N for _ in range(N)]
    for tri in tris:
        # rasterise the triangle's x/y footprint (any depth: a frame covers too)
        (a, _, _), (b, _, _), (c, _, _) = tri
        xs = [a[0], b[0], c[0]]
        ys = [a[1], b[1], c[1]]
        for gx in range(max(0, int(min(xs) * N)), min(N, int(max(xs) * N) + 1)):
            for gy in range(max(0, int(min(ys) * N)), min(N, int(max(ys) * N) + 1)):
                px, py = (gx + 0.5) / N, (gy + 0.5) / N
                d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
                if abs(d) < 1e-12:
                    continue
                l1 = ((b[1] - c[1]) * (px - c[0]) + (c[0] - b[0]) * (py - c[1])) / d
                l2 = ((c[1] - a[1]) * (px - c[0]) + (a[0] - c[0]) * (py - c[1])) / d
                if l1 >= -0.01 and l2 >= -0.01 and l1 + l2 <= 1.01:
                    cover[gx][gy] = True
    # the largest rectangle of uncovered cells
    best, rect = 0, None
    for x0 in range(N):
        for x1 in range(x0, N):
            run = 0
            for y in range(N):
                if all(not cover[x][y] for x in range(x0, x1 + 1)):
                    run += 1
                    a = run * (x1 - x0 + 1)
                    if a > best:
                        best, rect = a, (x0, y - run + 1, x1, y)
                else:
                    run = 0
    if rect is None or best < 20:
        return None
    x0, y0, x1, y1 = rect
    return (x0 / N, y0 / N, (x1 + 1) / N, (y1 + 1) / N)


def main():
    folder = sys.argv[1]
    models = []
    for name, model, thin, depth, hole in PARTS:
        tris = fx.load_obj(folder, model, skip=("glass",))
        tris = normalise(tris, thin, depth)
        body = body_colour(tris)
        mn = (hole[0], hole[1], 0.0) if hole else (0.0, 0.0, 0.0)
        mx = (hole[2], hole[3], 0.0) if hole else (0.0, 0.0, 0.0)
        models.append((name, tris, mn, mx, (body[0] / 255.0, body[1] / 255.0, body[2] / 255.0)))
        zmax = max(p[2] for tri in tris for p, _, _ in tri)
        print("%-14s %4d tris  depth %.2f m  body %s  opening %s" % (name, len(tris), zmax, body, tuple(round(v, 2) for v in hole) if hole else "-"))
    fx.OUT = OUT
    fx.write(models)
    print("wrote", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
