"""Finds z-fighting inside the models: two faces of one model that lie in the same plane (within a millimetre or
two), face the same way, overlap, and have different colours. Such a pair flickers in the client.

    python Tools/zfight_models.py [--min-cm2 1.0] [--blob Assets/Resources/PropHunt/models.bytes] [name ...]

Reads the model blob the prop baker reads (PropPrefabBaker.LoadModels). Prints one line per model with the area of
overlap and the worst pair (height or plane, the two colours). Models the baker corrects (PropPrefabBaker.FixModels)
are listed in FIXED and reported as such. Exit code 1 when anything unfixed is found."""
import struct, sys, math
from collections import defaultdict

FIXED = {'gen_laundrybasket'}   # corrected at bake time (PropPrefabBaker.FixModels)
EPS_D = 0.0015                  # planes closer than this fight (metres)
EPS_N = 0.02                    # normals closer than this are the same direction


def load(path):
    b = open(path, 'rb').read()
    assert b[:4] == b'PHM1'
    p = 4
    n, = struct.unpack_from('<i', b, p); p += 4
    out = []
    for _ in range(n):
        ln = b[p]; p += 1
        name = b[p:p + ln].decode(); p += ln
        ln = b[p]; p += 1 + ln
        p += 36
        nv, ni = struct.unpack_from('<ii', b, p); p += 8
        verts, cols = [], []
        for i in range(nv):
            verts.append(struct.unpack_from('<fff', b, p)); cols.append((b[p + 15], b[p + 16], b[p + 17])); p += 19
        if nv < 65536:
            tris = struct.unpack_from('<%dH' % ni, b, p); p += 2 * ni
        else:
            tris = struct.unpack_from('<%di' % ni, b, p); p += 4 * ni
        out.append((name, verts, cols, tris))
    return out


def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def clip(poly, a, b):
    """the part of convex polygon poly (2D, counter-clockwise) on the left of the line a -> b"""
    out = []
    ex, ey = b[0] - a[0], b[1] - a[1]
    for i in range(len(poly)):
        p, q = poly[i], poly[(i + 1) % len(poly)]
        sp = ex * (p[1] - a[1]) - ey * (p[0] - a[0]); sq = ex * (q[1] - a[1]) - ey * (q[0] - a[0])
        if sp >= 0: out.append(p)
        if (sp > 0 and sq < 0) or (sp < 0 and sq > 0):
            t = sp / (sp - sq); out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
    return out


def area(poly):
    s = 0.0
    for i in range(len(poly)):
        p, q = poly[i], poly[(i + 1) % len(poly)]
        s += p[0] * q[1] - q[0] * p[1]
    return s * 0.5


def overlap(t1, t2):
    poly = t1
    for i in range(3):
        poly = clip(poly, t2[i], t2[(i + 1) % 3])
        if len(poly) < 3: return 0.0
    return abs(area(poly))


def scan(verts, cols, tris):
    """-> (total overlapping area in m2, worst pair description)"""
    groups = defaultdict(list)
    for k in range(0, len(tris), 3):
        a, b, c = verts[tris[k]], verts[tris[k + 1]], verts[tris[k + 2]]
        n = cross(sub(b, a), sub(c, a))
        l = math.sqrt(dot(n, n))
        if l < 1e-10: continue
        n = (n[0] / l, n[1] / l, n[2] / l)
        key = (round(n[0] / EPS_N), round(n[1] / EPS_N), round(n[2] / EPS_N))
        groups[key].append((n, dot(n, a), (a, b, c), cols[tris[k]]))
    total, worst, worstA = 0.0, '', 0.0
    keys = list(groups)
    seen = set()
    for key in keys:
        # this direction and its neighbours in the quantised grid (a normal near a cell's edge)
        cand = []
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    k2 = (key[0] + dx, key[1] + dy, key[2] + dz)
                    if k2 in groups and k2 >= key: cand.append(k2)
        A = groups[key]
        for k2 in cand:
            B = groups[k2]
            for i, (n1, d1, t1, c1) in enumerate(A):
                # a 2D frame in the plane
                u = cross(n1, (1, 0, 0) if abs(n1[0]) < 0.9 else (0, 1, 0)); lu = math.sqrt(dot(u, u)); u = (u[0] / lu, u[1] / lu, u[2] / lu)
                w = cross(n1, u)
                p1 = [(dot(p, u), dot(p, w)) for p in t1]
                if area(p1) < 0: p1 = p1[::-1]
                for j, (n2, d2, t2, c2) in enumerate(B):
                    if k2 == key and j <= i: continue
                    if c1 == c2: continue
                    if dot(n1, n2) < 1 - 2e-4: continue
                    if abs(d1 - d2) > EPS_D: continue
                    p2 = [(dot(p, u), dot(p, w)) for p in t2]
                    if area(p2) < 0: p2 = p2[::-1]
                    if max(p[0] for p in p1) <= min(p[0] for p in p2) or max(p[0] for p in p2) <= min(p[0] for p in p1): continue
                    if max(p[1] for p in p1) <= min(p[1] for p in p2) or max(p[1] for p in p2) <= min(p[1] for p in p1): continue
                    ov = overlap(p1, p2)
                    if ov < 1e-6: continue
                    total += ov
                    if ov > worstA:
                        worstA = ov
                        worst = 'normal (%.2f %.2f %.2f) at %.3f, colours %s and %s, gap %.1f mm' % (n1[0], n1[1], n1[2], d1, c1, c2, abs(d1 - d2) * 1000)
    return total, worst


def main():
    args = sys.argv[1:]
    min_cm2, blob, names = 1.0, 'Assets/Resources/PropHunt/models.bytes', []
    while args:
        a = args.pop(0)
        if a == '--min-cm2': min_cm2 = float(args.pop(0))
        elif a == '--blob': blob = args.pop(0)
        else: names.append(a.lower())
    bad = 0
    for name, verts, cols, tris in load(blob):
        if names and name.lower() not in names: continue
        if len(tris) > 60000: continue
        total, worst = scan(verts, cols, tris)
        if total * 1e4 < min_cm2: continue
        fixed = name.lower() in FIXED
        print('%-34s %8.1f cm2  %s%s' % (name, total * 1e4, worst, '  [fixed at bake]' if fixed else ''))
        if not fixed: bad += 1
    print('%d model(s) with z-fighting' % bad)
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
