"""Hand-held models (hunters' melee weapons, the hider's selector gun) from the CC0 mirror -> Assets/Resources/PropHunt/weapons.bytes

Same "PHM1" blob as models.bytes (see quaternius_lib.save_blob). Each model is turned so that it is held the same way:
  melee: the grip at the origin, the length along +Z with the head at +Z, the head's broad side along Y
  gun:   the grip at the origin, the barrel along +Z (muzzle at +Z), up = +Y
and scaled to the length given below (metres). The entry's name is "<kind>:<pack>:<model>" (kind melee or gun); its
pivot holds (length, muzzle/tip z, 0). Any model on the mirror works: add it to WEAPONS and run again, then
PropHunt > Build weapons in Unity (or Build world scene).

    python Tools/make_weapons.py            (the starters only; writes the blob and a preview in the temp folder)
    python Tools/make_weapons.py --all      (every weapon in Tools/weapon_catalog.json)
    --out <path>                            (somewhere else than Resources)
"""
import os, sys, json, struct, tempfile, math
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import quaternius_lib as q

OUT = os.path.join(HERE, '..', 'Assets', 'Resources', 'PropHunt', 'weapons.bytes')

# (kind, pack, model, length in metres)
WEAPONS = [
    ('melee', 'kenney-survival-kit', 'tool-hammer', 0.70),      # ordinary town
    ('melee', 'fantasy-weapons-bits', 'hammer_B', 0.80),        # medieval: the spiked mace
    ('melee', 'kenney-survival-kit', 'tool-hammer-upgraded', 0.75),  # space: the metal sledge
    ('gun', 'kenney-blaster-kit', 'blaster-b', 0.24),           # the hider's selector
]


def length_for(kind, name):
    """a held length for a catalog model by what it is (metres)"""
    n = name.lower()
    if kind == 'gun':
        if any(k in n for k in ('sniper', 'rifle', 'shotgun', 'ar_', 'assault', 'bullpup', 'launcher', 'cannon', 'crossbow', 'ak47', 'p90', 'smg', 'submachine', 'uzi', 'grenade')):
            return 0.55
        return 0.26
    if any(k in n for k in ('dagger', 'knife', 'scissors', 'wrench', 'screwdriver', 'pencil', 'whisk', 'banana', 'hotdog', 'popsicle', 'icecream')):
        return 0.42
    if any(k in n for k in ('spear', 'halberd', 'staff', 'scythe', 'paddle', 'club-', 'umbrella', 'guitar', 'lute', 'shovel')):
        return 1.2
    if any(k in n for k in ('sword_2handed', 'longsword', 'sword_big', 'sword_d')):
        return 1.05
    return 0.8


def catalog():
    """every weapon of Tools/weapon_catalog.json (the store's list), the starters as in WEAPONS"""
    c = json.load(open(os.path.join(HERE, 'weapon_catalog.json')))
    fixed = {(k, p, m): l for k, p, m, l in WEAPONS}
    out = list(WEAPONS)
    for w in c['weapons']:
        key = (w['kind'], w['pack'], w['model'])
        if key in fixed: continue
        out.append((w['kind'], w['pack'], w['model'], length_for(w['kind'], w['model'])))
    return out


def load_glb(path):
    """a .glb as a .gltf next to it (its buffer as a data uri), so quaternius_lib.load_gltf reads it with its textures"""
    import base64
    b = open(path, 'rb').read()
    assert b[:4] == b'glTF'
    p = 12; js = None; binc = b''
    while p < len(b):
        ln, kind = struct.unpack_from('<I4s', b, p); p += 8
        if kind == b'JSON': js = json.loads(b[p:p + ln])
        elif kind == b'BIN\x00': binc = b[p:p + ln]
        p += ln
    js['buffers'][0]['uri'] = 'data:application/octet-stream;base64,' + base64.b64encode(binc).decode()
    tmp = os.path.join(os.path.dirname(path), '_tmp_' + os.path.basename(path) + '.gltf')
    json.dump(js, open(tmp, 'w'))
    try: return q.load_gltf(tmp)
    finally: os.remove(tmp)


def load(pack, name):
    f = q.record(pack, name)['formats']
    for k in ('gltf', 'glb'):
        if k in f:
            loc = q.fetch(f[k]['file'])
            for d in f[k]['deps']: q.fetch(d)
            return load_gltf_any(loc)
    if 'obj' in f:
        loc = q.fetch(f['obj']['file'])
        for d in f['obj']['deps']: q.fetch(d)
        V, T, C = q.load_obj(loc)
        return V, T, recolour_grey(loc, T, C)
    raise KeyError((pack, name, list(f)))


def load_gltf_any(loc):
    return load_glb(loc) if loc.endswith('.glb') else q.load_gltf(loc)


def frame_melee(V):
    lo, hi = V.min(0), V.max(0); ext = hi - lo
    ax = int(np.argmax(ext))                         # the length
    L = ext[ax]
    t = (V[:, ax] - lo[ax]) / L
    others = [i for i in range(3) if i != ax]
    def spread(mask): return np.prod(np.ptp(V[mask][:, others], axis=0) + 1e-6)
    head_hi = spread(t > 0.7) >= spread(t < 0.3)
    if not head_hi: t = 1 - t
    # the broad side of the head
    hm = t > 0.6
    up = others[int(np.argmax(np.ptp(V[hm][:, others], axis=0)))]
    side = [i for i in others if i != up][0]
    hand = t < 0.2
    c = V[hand].mean(0)
    z = t * L
    y = V[:, up] - c[up]; x = V[:, side] - c[side]
    W = np.stack([x, y, z - 0.12 * L], 1)
    # right-handed: flipping one axis mirrors the model, so turn the triangles too
    det = np.linalg.det(np.eye(3)[[side, up, ax]]) * (1 if head_hi else -1)
    return W, det < 0


def frame_gun(V):
    lo, hi = V.min(0), V.max(0); ext = hi - lo
    ax = 0 if ext[0] >= ext[2] else 2              # the barrel: the longer level axis (models stand upright, +Y up)
    low = V[:, 1] < lo[1] + 0.25 * ext[1]           # the handle hangs down at the back
    hc = V[low].mean(0)
    mid = (lo[ax] + hi[ax]) / 2
    front = 1 if hc[ax] < mid else -1               # the muzzle is away from the handle
    z = (V[:, ax] - hc[ax]) * front
    other = 2 if ax == 0 else 0
    x = V[:, other] - hc[other]
    y = V[:, 1] - (lo[1] + 0.4 * ext[1])
    W = np.stack([x, y, z], 1)
    M = np.zeros((3, 3)); M[0, other] = 1; M[1, 1] = 1; M[2, ax] = front
    return W, np.linalg.det(M) < 0


# ------------------------------------------------------------------ optimising (Cor: the whole catalog is fine "as long as it's optimized")
TRI_CAP = 1500        # triangles per weapon at most (vertex clustering above it)
SMOOTH_DEG = 35.0     # faces meeting at less than this share their corner vertices (a smooth normal); sharper edges stay hard


def decimate(V, T, C, cap):
    """vertex clustering on a grid, made finer or coarser until at most cap triangles are left; the colour of a
    triangle stays its own (faces that fold to a line or a point are dropped)"""
    if len(T) <= cap: return V, T, C
    lo, hi = V.min(0), V.max(0); ext = float((hi - lo).max())
    best = None
    a, b = 8, 400                      # cells along the longest side
    for _ in range(14):
        n = (a + b) // 2
        cell = ext / n
        key = np.floor((V - lo) / cell).astype(np.int64)
        k1 = key[:, 0] * 1000003 + key[:, 1] * 1009 + key[:, 2]
        uk, inv = np.unique(k1, return_inverse=True)
        NV = np.zeros((len(uk), 3)); cnt = np.zeros(len(uk))
        np.add.at(NV, inv, V); np.add.at(cnt, inv, 1); NV /= cnt[:, None]
        NT = inv[T]
        ok = (NT[:, 0] != NT[:, 1]) & (NT[:, 1] != NT[:, 2]) & (NT[:, 0] != NT[:, 2])
        NT, NC = NT[ok], C[ok]
        # the same triangle twice (two faces collapsed onto one) once
        srt = np.sort(NT, axis=1)
        _, first = np.unique(srt[:, 0] * 10**12 + srt[:, 1] * 10**6 + srt[:, 2], return_index=True)
        first.sort(); NT, NC = NT[first], NC[first]
        if len(NT) <= cap: best = (NV, NT, NC); a = n + 1
        else: b = n - 1
        if a > b: break
    return best if best is not None else (V, T, C)


def shared_entry(name, parent, pivot, V, T, C):
    """a blob entry whose triangles share corners where they meet at a shallow angle with the same colour"""
    tv = V[T]
    fn = np.cross(tv[:, 1] - tv[:, 0], tv[:, 2] - tv[:, 0]); area = np.linalg.norm(fn, axis=1)
    keep = area > 1e-12; T, C, fn, area = T[keep], C[keep], fn[keep], area[keep]
    fu = fn / area[:, None]
    cos_lim = math.cos(math.radians(SMOOTH_DEG))
    q_ = np.round(V / 1e-5).astype(np.int64)
    groups = {}
    verts, norms, cols = [], [], []
    out = np.zeros_like(T)
    for f in range(len(T)):
        c = tuple(int(x) for x in C[f])
        for k in range(3):
            vi = T[f, k]
            key = (q_[vi, 0], q_[vi, 1], q_[vi, 2]) + c
            lst = groups.setdefault(key, [])
            hit = -1
            for (idx, n0) in lst:
                if float(np.dot(n0, fu[f])) >= cos_lim: hit = idx; break
            if hit < 0:
                hit = len(verts); verts.append(V[vi]); norms.append(np.zeros(3)); cols.append(c)
                lst.append((hit, fu[f]))
            norms[hit] = norms[hit] + fn[f]
            out[f, k] = hit
    Vn = np.array(verts); N = np.array(norms); N /= np.linalg.norm(N, axis=1)[:, None] + 1e-12
    ent = dict(name=name, parent=parent, pivot=tuple(float(x) for x in pivot))
    ent['v'] = Vn; ent['n'] = N; ent['c'] = np.array(cols, dtype=np.uint8); ent['t'] = out.reshape(-1)
    ent['mn'] = tuple(Vn.min(0)); ent['mx'] = tuple(Vn.max(0))
    return ent


# untextured packs (every material the same grey): colours by material name, else by its place in the file
NAMED = [('wood', (126, 82, 48)), ('dark', (52, 54, 60)), ('metal', (112, 118, 128)), ('magazine', (40, 42, 46)),
         ('grip', (38, 38, 42)), ('bright', (196, 172, 120)), ('gold', (212, 168, 60)), ('handle', (92, 60, 38)),
         ('black', (30, 30, 34)), ('silver', (170, 176, 184)), ('red', (170, 50, 40)), ('scope', (36, 40, 46))]
ORDER = [(70, 74, 82), (38, 38, 42), (122, 80, 46), (150, 156, 166), (196, 172, 120), (90, 96, 104)]


def recolour_grey(path, T, C):
    """for an OBJ whose materials are all one grey: a colour per material from its name or its order"""
    mats, cur, faces = [], None, []
    for line in open(path, encoding='utf8', errors='replace'):
        s = line.split()
        if not s: continue
        if s[0] == 'usemtl':
            cur = ' '.join(s[1:])
            if cur not in mats: mats.append(cur)
        elif s[0] == 'f':
            for _ in range(len(s) - 3): faces.append(cur)
    if len(faces) != len(T) or len(set(map(tuple, C.tolist()))) > 1: return C
    pal = {}
    for i, m in enumerate(mats):
        low = (m or '').lower()
        col = next((c for k, c in NAMED if k in low), None)
        pal[m] = col if col is not None else ORDER[i % len(ORDER)]
    return np.array([pal[m] for m in faces], dtype=np.uint8)


def main():
    global OUT
    todo = catalog() if '--all' in sys.argv else WEAPONS
    if '--out' in sys.argv: OUT = sys.argv[sys.argv.index('--out') + 1]
    models = []
    stats = []
    for kind, pack, name, length in todo:
        try:
            (V, T, C) = load(pack, name)
        except Exception as ex:
            print('SKIP', kind, pack, name, ex); continue
        # stray parts far off (the animated Revolver keeps its bullets 400 units away): faces well outside the bulk go
        cen = np.median(V, 0); dist = np.linalg.norm(V[T].mean(1) - cen, axis=1)
        far = dist > 6 * max(np.median(dist), 1e-6)
        if far.any() and far.mean() < 0.5:
            T, C = T[~far], C[~far]
            used = np.unique(T); remap = np.full(len(V), -1); remap[used] = np.arange(len(used)); V = V[used]; T = remap[T]
        W, flip = (frame_melee if kind == 'melee' else frame_gun)(V)
        if flip: T = T[:, ::-1]
        s = length / np.ptp(W[:, 2])
        W = W * s
        n0 = len(T)
        W, T, C = decimate(W, T, C, TRI_CAP)
        e = shared_entry('%s:%s:%s' % (kind, pack, name), pack, (length, float(W[:, 2].max()), 0.0), W, T, C)
        stats.append((n0, len(T), len(e['v'])))
        models.append(e)
        print('%-6s %-24s %-14s %5d tris  z %.2f..%.2f  x %.2f  y %.2f..%.2f' % (kind, pack, name, len(T), W[:, 2].min(), W[:, 2].max(), np.ptp(W[:, 0]), W[:, 1].min(), W[:, 1].max()))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    q.save_blob(OUT, models)
    a = np.array(stats)
    print('triangles %d -> %d, vertices %d (flat shading would be %d)' % (a[:, 0].sum(), a[:, 1].sum(), a[:, 2].sum(), 3 * a[:, 1].sum()))
    print('wrote', os.path.normpath(OUT), os.path.getsize(OUT), 'bytes')
    # a look at them as they are held: from the right side (+X), the length (+Z) to the right, up (+Y) up
    try:
        from PIL import Image
        tiles = [q.render(m, size=240, yaw=90, pitch=10) for m in models[:40]]
        sheet = Image.new('RGB', (240 * len(tiles), 240), 'white')
        for i, im in enumerate(tiles): sheet.paste(im, (240 * i, 0))
        sheet.save(os.path.join(tempfile.gettempdir(), 'weapons_preview.png'))
        print('preview', os.path.join(tempfile.gettempdir(), 'weapons_preview.png'))
    except Exception as ex:
        print('no preview:', ex)


if __name__ == '__main__':
    main()
