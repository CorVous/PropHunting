"""The melee weapons' wear (Cor: "like a mouse nibbled along the entire weapon a lot, leaving a stick near the end"):
for every melee model in Resources/PropHunt/weapons.bytes, the wear (0..1) at which each bit of it is eaten or comes
loose, in a small voxel grid -> Resources/PropHunt/weapon_wear.bytes. WeaponBuilder turns each grid into a Texture3D;
the PropHunt/Weapon shader draws only where the grid's value is above the hunter's current wear, tracing the view ray
into the grid from the model's surface, so the gnawed faces look solid.

How the wear eats a weapon:
  - u, the normalised depth of a point: 0 on the surface, 1 on the long axis through the grip (depth / (depth + the
    distance to the axis)). Every part is eaten towards the same core, so near the end a thin stick is left.
  - nibbles: a cell noise (7 cm cells, 3 cm on top) gives each region its own pace, so it is gnawed unevenly in
    angular bites instead of shrinking smoothly.
  - a point is eaten once wear * (1 - STICK) * (0.35 + 0.65 * nibble) passes its u.
  - whatever no longer hangs on the grip (z < GRIP, never eaten) goes at the same moment.

    python Tools/bake_wear.py                      (after make_weapons.py; reads and writes Resources/PropHunt)
    python Tools/bake_wear.py --in X --out Y       (other files)

File "PHW1": int count; per model: len+name (as in weapons.bytes), lo 3f, voxel size f, dims 3i,
then dims.x*dims.y*dims.z bytes, x fastest: 0..254 = the wear at which it goes (v / 254), 255 = never (the grip),
0 = never there (outside the model).
"""
import os, sys, struct, math
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import quaternius_lib as q

RES = 0.008          # finest voxel size (m); the shader samples the grid trilinearly
MAXDIM = 48          # voxels along the longest side at most (a big weapon gets coarser voxels)
GRIP = 0.10          # the grip in the hand is never eaten
NIB = 0.07           # nibble cells (m)
STICK = 0.12         # the deepest share that is left at the very end
STEPS = 96           # wear steps of the simulation
RES0 = RES


def hash3(c):
    c = c.astype(np.int64)
    h = (c[..., 0] * 73856093) ^ (c[..., 1] * 19349663) ^ (c[..., 2] * 83492791)
    h = (h ^ (h >> 13)) * 1274126177
    h = h ^ (h >> 16)
    return (h & 0xffffff) / float(0xffffff)


def cellval(p, s, salt):
    g = np.floor(p / s)
    best = np.full(p.shape[:-1], 1e9); val = np.zeros(p.shape[:-1])
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                c = g + np.array([dx, dy, dz])
                j = np.stack([hash3(c + 11 + salt), hash3(c + 23 + salt), hash3(c + 37 + salt)], -1)
                d = np.linalg.norm(p - (c + 0.1 + 0.8 * j) * s, axis=-1)
                closer = d < best
                val = np.where(closer, hash3(c + 101 + salt), val)
                best = np.where(closer, d, best)
    return val


def dilate(m):
    o = m.copy()
    o[1:] |= m[:-1]; o[:-1] |= m[1:]
    o[:, 1:] |= m[:, :-1]; o[:, :-1] |= m[:, 1:]
    o[:, :, 1:] |= m[:, :, :-1]; o[:, :, :-1] |= m[:, :, 1:]
    return o


def grow(seed, within):
    seed = seed & within
    while True:
        n = dilate(seed) & within
        if n.sum() == seed.sum(): return seed
        seed = n


def bake(m):
    global RES
    v = np.asarray(m['v'], float); t = np.asarray(m['t']).reshape(-1, 3)
    RES = max(RES0, float((v.max(0) - v.min(0)).max()) / (MAXDIM - 5))
    lo = v.min(0) - 2 * RES
    dims = (np.ceil((v.max(0) + 2 * RES - lo) / RES)).astype(int) + 1
    shape = tuple(dims)
    surf = np.zeros(shape, bool)
    tv = v[t]
    for k in range(len(t)):
        a, b, c = tv[k]
        n = int(max(np.linalg.norm(b - a), np.linalg.norm(c - a), np.linalg.norm(c - b)) / (RES * 0.4)) + 2
        uu, ww = np.meshgrid(np.linspace(0, 1, n), np.linspace(0, 1, n))
        ok = uu + ww <= 1
        pts = a + uu[ok][:, None] * (b - a) + ww[ok][:, None] * (c - a)
        ij = np.floor((pts - lo) / RES).astype(int)
        surf[ij[:, 0], ij[:, 1], ij[:, 2]] = True
    border = np.zeros(shape, bool); border[0] = border[-1] = True; border[:, 0] = border[:, -1] = True; border[:, :, 0] = border[:, :, -1] = True
    outside = grow(border & ~surf, ~surf)
    solid = ~outside
    # depth from the surface (city-block steps of erosion)
    depth = np.zeros(shape); cur = solid.copy(); k = 0
    while cur.any():
        depth[cur] = k * RES
        er = cur.copy()
        er[1:] &= cur[:-1]; er[:-1] &= cur[1:]; er[:, 1:] &= cur[:, :-1]; er[:, :-1] &= cur[:, 1:]; er[:, :, 1:] &= cur[:, :, :-1]; er[:, :, :-1] &= cur[:, :, 1:]
        er[0] = er[-1] = False
        cur = er; k += 1
    P = (np.indices(shape).transpose(1, 2, 3, 0) + 0.5) * RES + lo
    r = np.linalg.norm(P[..., :2], axis=-1)
    u = np.where(solid, (depth + 0.5 * RES) / np.maximum(depth + 0.5 * RES + r, 1e-6), 0.0)
    nib = cellval(P, NIB, 0) * 0.8 + 0.2 * cellval(P, NIB * 0.45, 7)
    pace = (1 - STICK) * (0.35 + 0.65 * nib)          # eaten once wear * pace > u
    grip = solid & (P[..., 2] < GRIP)
    gone = np.full(shape, 2.0)                         # the wear at which each voxel goes (2 = never)
    alive_prev = solid.copy()
    for s in range(1, STEPS + 1):
        w = s / STEPS
        keep = solid & ((u > w * pace) | grip)
        alive = grow(keep & grip, keep)
        newly = alive_prev & ~alive
        gone[newly] = w
        alive_prev = alive
    out = np.zeros(shape, np.uint8)
    out[solid] = np.clip(np.round(np.minimum(gone[solid], 1.0) * 254), 1, 254).astype(np.uint8)
    out[grip] = 255
    out[~solid] = 0
    # two voxels of padding outside take their solid neighbour's value: a trilinear sample on the surface then reads
    # the surface's own value, not half of the empty outside
    for _ in range(2):
        pad = (out == 0)
        best = out.copy()
        for ax in range(3):
            for sh in (1, -1):
                best = np.maximum(best, np.roll(out, sh, axis=ax))
        out = np.where(pad, best, out)
    return lo, out


def main():
    src = os.path.join(HERE, '..', 'Assets', 'Resources', 'PropHunt', 'weapons.bytes')
    dst = os.path.join(HERE, '..', 'Assets', 'Resources', 'PropHunt', 'weapon_wear.bytes')
    if '--in' in sys.argv: src = sys.argv[sys.argv.index('--in') + 1]
    if '--out' in sys.argv: dst = sys.argv[sys.argv.index('--out') + 1]
    models = [m for m in q.load_blob(src) if m['name'].startswith('melee:')]
    o = bytearray(b'PHW1') + struct.pack('<i', len(models))
    for m in models:
        lo, g = bake(m)
        nb = m['name'].encode()
        o += bytes([len(nb)]) + nb + struct.pack('<3f', *lo) + struct.pack('<f', RES) + struct.pack('<3i', *g.shape)
        o += np.ascontiguousarray(g.transpose(2, 1, 0)).tobytes()   # x fastest
        print('%-50s %s  %d KB' % (m['name'], g.shape, g.size // 1024))
    open(dst, 'wb').write(bytes(o))
    print('wrote', os.path.normpath(dst), len(o), 'bytes')


if __name__ == '__main__':
    main()
