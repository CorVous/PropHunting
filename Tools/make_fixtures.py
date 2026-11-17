"""Builds Assets/Resources/PropHunt/fixtures.bytes: the light fixtures the map builder places (ceiling lights, wall
lights by outside doors, street lamps), one set per map theme, from CC0 models (Kenney, KayKit) mirrored at
http://cc0.cor.home. Each model is baked to vertex colours (the material's colour, or its texture sampled at the
vertex's UV), scaled, turned and moved so that:

    ceiling  hangs from its origin: the top at y 0, centred on x and z
    wall     stands off the wall: its back on z 0, reaching out along +z, centred on x, its middle at y 0
    street   stands on its origin, the arm (if any) reaching along +z

The file uses the models.bytes layout (PropPrefabBaker reads both); the "pivot" field carries the fixture's light
point (where a VRC Light Volumes point light goes). Run it again after changing the list:

    python Tools/make_fixtures.py <folder with the downloaded OBJ files>

The folder holds <pack>/<model>.obj with its .mtl and textures beside it (Textures/ for Kenney's city and town kits).
"""
import math
import os
import struct
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "PropHunt", "fixtures.bytes")

# name, pack/model, kind, size (metres along the measured axis), turn (degrees about y, before placing)
FIXTURES = [
    ("fx_ceiling_modern",   "kenney-furniture-kit/lampSquareCeiling", "ceiling", 0.60, 0),
    ("fx_ceiling_space",    "kenney-furniture-kit/lampSquareCeiling", "ceiling", 0.60, 0),
    ("fx_ceiling_medieval", "halloween-bits/lantern_hanging",         "ceiling", 0.80, 0),
    ("fx_wall_modern",      "kenney-furniture-kit/lampWall",          "wall",    0.45, 180),
    ("fx_wall_space",       "kenney-furniture-kit/lampWall",          "wall",    0.45, 180),
    ("fx_wall_medieval",    "kaykit-dungeon-pack/torch_mounted",      "wall",    0.45, 0),
    ("fx_street_modern",    "kenney-city-kit-roads/light-curved",     "street",  4.80, 180),
    ("fx_street_space",     "space-base-bits/lights",                 "street",  3.60, 0),
    ("fx_street_medieval",  "holiday-bits/lantern",                   "street",  3.40, 0),
    # the sky's scenery (Cor: fill the sky): hills and mountains round the map, clouds, a space base's rocky ridge.
    # They stand on their origin like a street lamp; the size is their height
    ("sky_hill_a",          "kaykit-medieval-hexagon/hills_A_trees",  "street",  9.0, 0),
    ("sky_hill_b",          "kaykit-medieval-hexagon/hills_B_trees",  "street",  9.0, 0),
    ("sky_mount_a",         "kaykit-medieval-hexagon/mountain_A_grass_trees", "street", 30.0, 0),
    ("sky_mount_b",         "kaykit-medieval-hexagon/mountain_B_grass", "street", 26.0, 0),
    ("sky_mount_c",         "kaykit-medieval-hexagon/mountain_C",     "street", 22.0, 0),
    ("sky_cloud_big",       "kaykit-medieval-hexagon/cloud_big",      "street",  7.0, 0),
    ("sky_cloud_small",     "kaykit-medieval-hexagon/cloud_small",    "street",  5.0, 0),
    ("sky_rock_a",          "kenney-space-kit/rock_largeA",           "street", 16.0, 0),
    ("sky_rock_b",          "kenney-space-kit/rock_largeB",           "street", 14.0, 0),
]


def load_mtl(path):
    mats, cur = {}, None
    for line in open(path, encoding="utf8", errors="replace"):
        p = line.split()
        if not p:
            continue
        if p[0] == "newmtl":
            cur = p[1]
            mats[cur] = {"kd": (1.0, 1.0, 1.0), "tex": None}
        elif p[0] == "Kd" and cur:
            mats[cur]["kd"] = tuple(float(x) for x in p[1:4])
        elif p[0] == "map_Kd" and cur:
            mats[cur]["tex"] = " ".join(p[1:])
    return mats


def srgb(c):
    # Kenney's Kd values are linear-ish 0..1 colours meant as sRGB swatches: take them as they are
    return max(0, min(255, int(round(c * 255))))


# a bulb, a flame or a lit pane glows (vertex alpha 0: PropHunt/Lit draws it at full brightness, unlit): the
# materials named for it, and bright warm colours in a texture atlas
GLOW_ALPHA = 0


def glowing(material, c):
    if material and material.lower() in ("lamp", "holiday_glow", "glow", "light", "emission"):
        return True
    r, g, b = c
    return r > 225 and g > 150 and b < 150 and r - b > 110


def load_obj(folder, model, skip=()):
    path = os.path.join(folder, model + ".obj")
    base = os.path.dirname(path)
    v, vt, vn, tris = [], [], [], []   # tris: (pos, uv, normal, material) x 3
    mats, mat = {}, None
    for line in open(path, encoding="utf8", errors="replace"):
        p = line.split()
        if not p:
            continue
        if p[0] == "mtllib":
            mats.update(load_mtl(os.path.join(base, " ".join(p[1:]))))
        elif p[0] == "usemtl":
            mat = p[1]
        elif p[0] == "v":
            v.append(tuple(float(x) for x in p[1:4]))
        elif p[0] == "vt":
            vt.append(tuple(float(x) for x in p[1:3]))
        elif p[0] == "vn":
            vn.append(tuple(float(x) for x in p[1:4]))
        elif p[0] == "f":
            corners = []
            for c in p[1:]:
                ids = c.split("/")
                pi = int(ids[0]) - 1
                ti = int(ids[1]) - 1 if len(ids) > 1 and ids[1] else -1
                ni = int(ids[2]) - 1 if len(ids) > 2 and ids[2] else -1
                corners.append((pi, ti, ni))
            for k in range(1, len(corners) - 1):
                tris.append((corners[0], corners[k], corners[k + 1], mat))
    textures = {}
    def colour(m, ti):
        info = mats.get(m, {"kd": (1, 1, 1), "tex": None})
        if info["tex"] and ti >= 0:
            if info["tex"] not in textures:
                tp = os.path.join(base, info["tex"])
                if not os.path.exists(tp):
                    tp = os.path.join(base, os.path.basename(info["tex"]))
                textures[info["tex"]] = Image.open(tp).convert("RGB")
            im = textures[info["tex"]]
            u, w = vt[ti]
            x = int((u - math.floor(u)) * im.width) % im.width
            y = int((1 - (w - math.floor(w))) * im.height) % im.height
            r, g, b = im.getpixel((x, y))
            kd = info["kd"]
            c = (int(r * kd[0]), int(g * kd[1]), int(b * kd[2]))
        else:
            kd = info["kd"]
            c = (srgb(kd[0]), srgb(kd[1]), srgb(kd[2]))
        return c + (GLOW_ALPHA if glowing(m, c) else 255,)
    out = []
    for a, b, c, m in tris:
        if m in skip:
            continue   # a window's glass: left out, so a window stays a window
        pa, pb, pc = v[a[0]], v[b[0]], v[c[0]]
        # the face normal (flat shading, like the rest of the map)
        ux, uy, uz = pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2]
        wx, wy, wz = pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2]
        nx, ny, nz = uy * wz - uz * wy, uz * wx - ux * wz, ux * wy - uy * wx
        l = math.sqrt(nx * nx + ny * ny + nz * nz) or 1.0
        n = (nx / l, ny / l, nz / l)
        out.append([(v[k[0]], n, colour(m, k[1])) for k in (a, b, c)])
    return out


def place(tris, kind, size, turn):
    t = math.radians(turn)
    cs, sn = math.cos(t), math.sin(t)
    def rot(p):
        return (p[0] * cs + p[2] * sn, p[1], -p[0] * sn + p[2] * cs)
    tris = [[(rot(p), rot(n), c) for p, n, c in tri] for tri in tris]
    pts = [p for tri in tris for p, _, _ in tri]
    mn = [min(p[i] for p in pts) for i in range(3)]
    mx = [max(p[i] for p in pts) for i in range(3)]
    # the measured axis: the height, except a wall light's width
    span = (mx[0] - mn[0]) if kind == "wall" else (mx[1] - mn[1])
    s = size / span
    cx, cz = (mn[0] + mx[0]) / 2, (mn[2] + mx[2]) / 2
    if kind == "ceiling":
        off = (-cx, -mx[1], -cz)
    elif kind == "wall":
        off = (-cx, -(mn[1] + mx[1]) / 2, -mn[2])
    else:
        # a street lamp stands on its post: the middle of its lowest tenth
        base = [p for p in pts if p[1] < mn[1] + 0.1 * (mx[1] - mn[1])]
        off = (-sum(p[0] for p in base) / len(base), -mn[1], -sum(p[2] for p in base) / len(base))
    def tr(p):
        return ((p[0] + off[0]) * s, (p[1] + off[1]) * s, (p[2] + off[2]) * s)
    tris = [[(tr(p), n, c) for p, n, c in tri] for tri in tris]
    pts = [p for tri in tris for p, _, _ in tri]
    mn = [min(p[i] for p in pts) for i in range(3)]
    mx = [max(p[i] for p in pts) for i in range(3)]
    h = mx[1] - mn[1]
    # the light point: under a ceiling light's shade, in front of a wall light, under a street lamp's head (the
    # middle of its highest tenth, a little lower)
    if kind == "ceiling":
        light = (0.0, mn[1] - 0.1, 0.0)
    elif kind == "wall":
        light = (0.0, 0.0, mx[2] + 0.1)
    elif mx[2] > 1.0 and mn[2] > -0.4:
        # an arm: under the head at its end
        head = [p for p in pts if p[2] > mx[2] - 0.3]
        light = (sum(p[0] for p in head) / len(head), min(p[1] for p in head) - 0.15, sum(p[2] for p in head) / len(head))
    else:
        top = [p for p in pts if p[1] > mx[1] - 0.1 * h]
        light = (sum(p[0] for p in top) / len(top), mx[1] - 0.18 * h, sum(p[2] for p in top) / len(top))
    return tris, mn, mx, light


def write(models):
    b = bytearray(b"PHM1")
    b += struct.pack("<i", len(models))
    for name, tris, mn, mx, light in models:
        nb = name.encode("utf8")
        b += bytes([len(nb)]) + nb + bytes([0])
        b += struct.pack("<3f", *light) + struct.pack("<3f", *mn) + struct.pack("<3f", *mx)
        nv = len(tris) * 3
        b += struct.pack("<ii", nv, nv)
        for tri in tris:
            for p, n, c in tri:
                b += struct.pack("<3f", *p)
                b += struct.pack("<3b", *(max(-127, min(127, int(round(x * 127)))) for x in n))
                b += bytes([c[0], c[1], c[2], c[3] if len(c) > 3 else 255])
        for i in range(nv):
            b += struct.pack("<H", i)
    open(OUT, "wb").write(bytes(b))


def main():
    folder = sys.argv[1]
    models = []
    for name, model, kind, size, turn in FIXTURES:
        tris, mn, mx, light = place(load_obj(folder, model), kind, size, turn)
        if name.startswith("sky_"):
            # scenery never glows (a bright cloud is not a bulb)
            tris = [[(p, n, c[:3] + (255,)) for p, n, c in tri] for tri in tris]
        models.append((name, tris, mn, mx, light))
        print("%-20s %5d tris  size %.2f x %.2f x %.2f  light %s" % (name, len(tris), mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2], tuple(round(x, 2) for x in light)))
    write(models)
    print("wrote", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
