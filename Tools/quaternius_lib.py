"""Quaternius -> PropHunt model blob helpers: blob read/write, glTF/OBJ loading, a flat-shaded software renderer."""
import struct, json, os, math, base64, urllib.request, urllib.parse
import numpy as np
from PIL import Image, ImageDraw

MIRROR = 'http://cc0.cor.home/'
HERE = os.path.dirname(os.path.abspath(__file__))
DL = os.environ.get('CC0_CACHE', os.path.join(os.path.expanduser('~'), '.cache', 'cc0-mirror'))


# ------------------------------------------------------------------ blob
def load_blob(path):
    b = open(path, 'rb').read()
    assert b[:4] == b'PHM1'
    p = 4
    n, = struct.unpack_from('<i', b, p); p += 4
    out = []
    for _ in range(n):
        ln = b[p]; p += 1; name = b[p:p + ln].decode(); p += ln
        ln = b[p]; p += 1; par = b[p:p + ln].decode(); p += ln
        piv = struct.unpack_from('<3f', b, p); p += 12
        mn = struct.unpack_from('<3f', b, p); p += 12
        mx = struct.unpack_from('<3f', b, p); p += 12
        nv, ni = struct.unpack_from('<ii', b, p); p += 8
        raw = np.frombuffer(b, dtype=np.uint8, count=nv * 19, offset=p).reshape(nv, 19); p += nv * 19
        v = raw[:, :12].copy().view('<f4').reshape(nv, 3).astype(np.float64)
        nr = raw[:, 12:15].copy().view(np.int8).astype(np.float64) / 127.0
        c = raw[:, 15:18].copy()
        if nv < 65536:
            t = np.frombuffer(b, dtype='<u2', count=ni, offset=p).astype(np.int64); p += 2 * ni
        else:
            t = np.frombuffer(b, dtype='<i4', count=ni, offset=p).astype(np.int64); p += 4 * ni
        out.append(dict(name=name, parent=par, pivot=piv, mn=mn, mx=mx, v=v, n=nr, c=c, t=t))
    assert p == len(b), (p, len(b))
    return out


def save_blob(path, models):
    o = bytearray(b'PHM1'); o += struct.pack('<i', len(models))
    for m in models:
        nb = m['name'].encode(); pb = m['parent'].encode()
        o += bytes([len(nb)]) + nb + bytes([len(pb)]) + pb
        o += struct.pack('<3f', *m['pivot']) + struct.pack('<3f', *m['mn']) + struct.pack('<3f', *m['mx'])
        v = np.asarray(m['v'], dtype='<f4'); nv = len(v); t = np.asarray(m['t']); ni = len(t)
        o += struct.pack('<ii', nv, ni)
        raw = np.zeros((nv, 19), dtype=np.uint8)
        raw[:, :12] = v.view(np.uint8).reshape(nv, 12)
        raw[:, 12:15] = np.clip(np.round(np.asarray(m['n']) * 127), -127, 127).astype(np.int8).view(np.uint8)
        raw[:, 15:18] = np.asarray(m['c'], dtype=np.uint8)
        o += raw.tobytes()
        o += (t.astype('<u2') if nv < 65536 else t.astype('<i4')).tobytes()
    open(path, 'wb').write(bytes(o))


# ------------------------------------------------------------------ download
def fetch(path):
    """mirror path -> local file (cached under dl/, same layout)"""
    loc = os.path.join(DL, *path.split('/'))
    if not os.path.exists(loc):
        os.makedirs(os.path.dirname(loc), exist_ok=True)
        url = MIRROR + '/'.join(urllib.parse.quote(s) for s in path.split('/'))
        urllib.request.urlretrieve(url, loc)
    return loc


_packs = {}
def record(pack, name):
    if pack not in _packs:
        r = json.load(urllib.request.urlopen(MIRROR + 'index/by-pack/%s.json' % pack))
        _packs[pack] = r['items'] if isinstance(r, dict) else r
    for it in _packs[pack]:
        if it['name'] == name: return it
    raise KeyError((pack, name))


# ------------------------------------------------------------------ loaders -> (verts Nx3, tris Mx3, face colours Mx3 uint8)
def _qmat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def _node_mat(nd):
    if 'matrix' in nd: return np.array(nd['matrix']).reshape(4, 4).T
    M = np.eye(4)
    S = np.diag(nd.get('scale', [1, 1, 1])); R = _qmat(nd.get('rotation', [0, 0, 0, 1]))
    M[:3, :3] = R @ S; M[:3, 3] = nd.get('translation', [0, 0, 0])
    return M


def load_gltf(path):
    g = json.load(open(path, encoding='utf8')); base = os.path.dirname(path)
    bufs = []
    for bf in g['buffers']:
        u = bf['uri']
        bufs.append(base64.b64decode(u.split(',', 1)[1]) if u.startswith('data:') else open(os.path.join(base, urllib.parse.unquote(u)), 'rb').read())
    CT = {5126: ('<f4', 4), 5123: ('<u2', 2), 5125: ('<u4', 4), 5121: ('u1', 1), 5122: ('<i2', 2), 5120: ('i1', 1)}
    NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}

    def acc(i):
        a = g['accessors'][i]; bv = g['bufferViews'][a['bufferView']]
        dt, sz = CT[a['componentType']]; nc = NC[a['type']]
        off = bv.get('byteOffset', 0) + a.get('byteOffset', 0); stride = bv.get('byteStride', sz * nc)
        buf = bufs[bv['buffer']]
        arr = np.ndarray((a['count'], nc), dtype=dt, buffer=buf, offset=off, strides=(stride, sz)).astype(np.float64)
        if a.get('normalized'):
            arr /= {5121: 255., 5123: 65535., 5120: 127., 5122: 32767.}[a['componentType']]
        return arr

    texcache = {}
    def tex(ti):
        if ti not in texcache:
            img = g['images'][g['textures'][ti]['source']]
            import io
            if 'uri' not in img:
                bv = g['bufferViews'][img['bufferView']]; o = bv.get('byteOffset', 0)
                src = io.BytesIO(bufs[bv['buffer']][o:o + bv['byteLength']])
            elif img['uri'].startswith('data:'): src = io.BytesIO(base64.b64decode(img['uri'].split(',', 1)[1]))
            else: src = os.path.join(base, urllib.parse.unquote(img['uri']))
            texcache[ti] = np.asarray(Image.open(src).convert('RGB')).astype(np.float64)
        return texcache[ti]

    V, T, C = [], [], []
    def walk(ni, M):
        nd = g['nodes'][ni]; M = M @ _node_mat(nd)
        if 'mesh' in nd:
            for pr in g['meshes'][nd['mesh']]['primitives']:
                if pr.get('mode', 4) != 4: continue
                pos = acc(pr['attributes']['POSITION'])
                idx = acc(pr['indices'])[:, 0].astype(np.int64) if 'indices' in pr else np.arange(len(pos))
                tri = idx.reshape(-1, 3)
                pw = (M[:3, :3] @ pos.T).T + M[:3, 3]
                if np.linalg.det(M[:3, :3]) < 0: tri = tri[:, ::-1]
                col = np.full((len(tri), 3), 200.0)
                mat = g['materials'][pr['material']] if 'material' in pr else {}
                pbr = mat.get('pbrMetallicRoughness', {})
                fac = np.array(pbr.get('baseColorFactor', [1, 1, 1, 1])[:3])
                if 'baseColorTexture' in pbr and 'TEXCOORD_0' in pr['attributes']:
                    uv = acc(pr['attributes']['TEXCOORD_0']); im = tex(pbr['baseColorTexture']['index'])
                    h, w, _ = im.shape
                    cu = uv[tri].mean(axis=1)
                    px = np.clip((cu[:, 0] % 1.0) * w, 0, w - 1).astype(int); py = np.clip((cu[:, 1] % 1.0) * h, 0, h - 1).astype(int)
                    col = im[py, px] * fac
                else:
                    col = np.tile(lin2srgb(fac) * 255, (len(tri), 1)) if True else col
                if 'COLOR_0' in pr['attributes']:
                    vc = acc(pr['attributes']['COLOR_0'])[:, :3]
                    col = col * lin2srgb(vc[tri].mean(axis=1))
                o = sum(len(x) for x in V)
                V.append(pw); T.append(tri + o); C.append(col)
        for ch in nd.get('children', []): walk(ch, M)
    sc = g['scenes'][g.get('scene', 0)]
    for r in sc['nodes']: walk(r, np.eye(4))
    return np.concatenate(V), np.concatenate(T), np.clip(np.concatenate(C), 0, 255).astype(np.uint8)


def lin2srgb(c):
    c = np.clip(np.asarray(c, dtype=np.float64), 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def load_obj(path, objects=None):
    """objects: when a list is passed, it receives each face's object name (the 'o' lines)"""
    base = os.path.dirname(path); mats = {}; cur = None; obj = ''
    V, F, C = [], [], []
    for line in open(path, encoding='utf8', errors='replace'):
        s = line.split()
        if not s: continue
        if s[0] == 'mtllib':
            mp = os.path.join(base, ' '.join(s[1:]))
            if os.path.exists(mp):
                m = None
                for l2 in open(mp, encoding='utf8', errors='replace'):
                    t = l2.split()
                    if not t: continue
                    if t[0] == 'newmtl': m = ' '.join(t[1:]); mats[m] = (0.8, 0.8, 0.8)
                    elif t[0] == 'Kd' and m: mats[m] = tuple(float(x) for x in t[1:4])
        elif s[0] == 'usemtl': cur = ' '.join(s[1:])
        elif s[0] == 'o': obj = ' '.join(s[1:])
        elif s[0] == 'v': V.append([float(x) for x in s[1:4]])
        elif s[0] == 'f':
            ids = [int(x.split('/')[0]) for x in s[1:]]
            ids = [i - 1 if i > 0 else len(V) + i for i in ids]
            kd = mats.get(cur, (0.8, 0.8, 0.8))
            for k in range(1, len(ids) - 1):
                F.append([ids[0], ids[k], ids[k + 1]]); C.append(kd)
                if objects is not None: objects.append(obj)
    # Kd in these exports is linear
    return np.array(V), np.array(F), (lin2srgb(np.array(C)) * 255).astype(np.uint8)


def load_item(pack, name):
    it = record(pack, name); f = it['formats']
    for k in ('gltf', 'glb', 'obj'):
        if k in f:
            loc = fetch(f[k]['file'])
            for d in f[k]['deps']: fetch(d)
            return (load_gltf if k != 'obj' else load_obj)(loc), k
    raise KeyError('no loadable format', pack, name, list(f))


# ------------------------------------------------------------------ to blob entry
def warp_y(V, knots):
    """piecewise-linear change of heights: knots = [(source y, target y), ...] rising"""
    V = V.copy(); V[:, 1] = np.interp(V[:, 1], [k[0] for k in knots], [k[1] for k in knots])
    return V


def fit_verts(V, omn, omx, mode='snug', yaw=0):
    """vertices (+Y up, front +Z) moved into the bounds omn..omx: 'box' scales each axis to the bounds, 'uniform'
    keeps the proportions (inside the bounds, centred in x and z, base on the bounds' floor), 'snug' is uniform
    with up to 15% of stretch per axis toward the bounds. yaw: degrees about Y applied first."""
    V = V.copy()
    a = math.radians(yaw); ca, sa = math.cos(a), math.sin(a)
    V = np.stack([V[:, 0] * ca + V[:, 2] * sa, V[:, 1], -V[:, 0] * sa + V[:, 2] * ca], 1)
    lo, hi = V.min(0), V.max(0); omn, omx = np.array(omn, dtype=float), np.array(omx, dtype=float)
    if mode == 'box':
        return (V - lo) * ((omx - omn) / (hi - lo)) + omn
    r = (omx - omn) / (hi - lo); s = r.min()
    if mode == 'snug': s = np.minimum(r, s * 1.15)
    c = (lo + hi) / 2; oc = (omn + omx) / 2
    V = (V - c) * s + oc; V[:, 1] += omn[1] - V[:, 1].min()
    return V


def make_entry(name, parent, pivot, V, T, C):
    """a flat-shaded blob entry: every triangle gets its own three vertices, its face normal and one colour"""
    tv = V[T]
    n = np.cross(tv[:, 1] - tv[:, 0], tv[:, 2] - tv[:, 0]); l = np.linalg.norm(n, axis=1)
    keep = l > 1e-12; tv, n, l, C = tv[keep], n[keep], l[keep], C[keep]
    n = n / l[:, None]
    ent = dict(name=name, parent=parent, pivot=tuple(float(x) for x in pivot))
    ent['v'] = tv.reshape(-1, 3); ent['n'] = np.repeat(n, 3, 0); ent['c'] = np.repeat(C, 3, 0)
    ent['t'] = np.arange(3 * len(tv))
    ent['mn'] = tuple(ent['v'].min(0)); ent['mx'] = tuple(ent['v'].max(0))
    return ent


def fit_entry(old, V, T, C, yaw=0, mode='snug'):
    return make_entry(old['name'], old['parent'], old['pivot'], fit_verts(V, old['mn'], old['mx'], mode, yaw), T, C)


# ------------------------------------------------------------------ renderer
def render(ent, size=320, yaw=35, pitch=25, bg=(236, 238, 242), light=(0.45, 0.8, 0.35)):
    v = np.asarray(ent['v'], dtype=np.float64); t = np.asarray(ent['t']).reshape(-1, 3); c = np.asarray(ent['c'], dtype=np.float64)
    a, b = math.radians(yaw), math.radians(pitch)
    Ry = np.array([[math.cos(a), 0, -math.sin(a)], [0, 1, 0], [math.sin(a), 0, math.cos(a)]])
    Rx = np.array([[1, 0, 0], [0, math.cos(b), -math.sin(b)], [0, math.sin(b), math.cos(b)]])
    R = Rx @ Ry
    ctr = (v.min(0) + v.max(0)) / 2
    p = (R @ (v - ctr).T).T  # x right, y up, z toward viewer? use -z depth
    ext = np.abs(p[:, :2]).max() * 1.1 + 1e-6
    sc = size / 2 / ext
    sx = size / 2 + p[:, 0] * sc; sy = size / 2 - p[:, 1] * sc; sz = p[:, 2]
    tv = v[t]; n = np.cross(tv[:, 1] - tv[:, 0], tv[:, 2] - tv[:, 0]); n /= np.linalg.norm(n, axis=1)[:, None] + 1e-12
    L = np.array(light); L /= np.linalg.norm(L)
    sh = 0.55 + 0.45 * np.clip(n @ L, 0, 1)
    fc = c[t[:, 0]] * sh[:, None]
    img = np.zeros((size, size, 3)); img[:] = bg; zb = np.full((size, size), -1e9)
    for k in range(len(t)):
        i0, i1, i2 = t[k]
        xs = np.array([sx[i0], sx[i1], sx[i2]]); ys = np.array([sy[i0], sy[i1], sy[i2]]); zs = np.array([sz[i0], sz[i1], sz[i2]])
        x0, x1 = max(int(xs.min()), 0), min(int(xs.max()) + 1, size - 1); y0, y1 = max(int(ys.min()), 0), min(int(ys.max()) + 1, size - 1)
        if x1 < x0 or y1 < y0: continue
        d = (xs[1] - xs[0]) * (ys[2] - ys[0]) - (xs[2] - xs[0]) * (ys[1] - ys[0])
        if abs(d) < 1e-9: continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        w1 = ((X - xs[0]) * (ys[2] - ys[0]) - (xs[2] - xs[0]) * (Y - ys[0])) / d
        w2 = ((xs[1] - xs[0]) * (Y - ys[0]) - (X - xs[0]) * (ys[1] - ys[0])) / d
        w0 = 1 - w1 - w2
        m = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not m.any(): continue
        z = w0 * zs[0] + w1 * zs[1] + w2 * zs[2]
        sub = zb[y0:y1 + 1, x0:x1 + 1]
        m &= z > sub
        sub[m] = z[m]; img[y0:y1 + 1, x0:x1 + 1][m] = fc[k]
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
