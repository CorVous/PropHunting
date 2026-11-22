"""Cor's drawn melee weapons (2026-11-21), kept as drawn (Cor: "don't model them, just keep them as-is"): each drawing
is a flat card in the hand, the picture on both faces, cut out along its own edge.

    python Tools/make_card_weapons.py

Reads Tools/card_weapons/<name>.png (Cor's drawings, 2048 x 2048, transparent around) and writes
  Assets/Udon/Textures/CardWeapons.png       the pieces packed into one atlas (RGBA; alpha = the cut-out)
  Assets/Udon/Textures/CardWeaponsWear.png   the same layout, grey: the wear at which each bit is nibbled away
                                             (0..254 = 0..1, 255 = never: the handle), as bake_wear.py's grids
  Tools/card_weapons.json                    per weapon: its pieces' quads in the model's space and their atlas
                                             rects, the head, the chip spots, the head colour; and the shelf's quads
WeaponBuilder (Build world scene / PropHunt > Build weapons) makes the meshes from the json; the weapons' shaders draw
the atlas wherever a vertex's colour has alpha 0. Two-piece sets (Cor): the main piece in the weapon hand, the other in
the off hand; both strike, except a shield. Ids "gun:cor:..." are hider guns (one piece, a muzzle); a melee entry with
gun=dict(id, muzzle) is also that hider gun (the gunblade), written as a second entry naming it as its "twin".

The model's space is make_weapons.py's: the grip at the origin, the length along +Z (head at +Z), the head's broad side
along Y. A card lies in the YZ plane (the drawing's left edge towards +Y, the way the fist's fingers point, so a blade's
edge drawn on the left leads a swing). A shield lies in the XZ plane, a little in front of the fist (+Y).
"""
import os, json, math
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'card_weapons')
TEX = os.path.join(HERE, '..', 'Assets', 'Udon', 'Textures')
ATLAS = 2048
PIECE_PX = 640           # a piece's longest side in the atlas, at most
SPOTS = 24               # PHWeapons.SPOTS

# per weapon: the drawing, and its pieces: the part of the drawing (x range, source pixels), the role, the fist's
# point (source pixels), where the head begins (source y), and how long the piece is held (metres, its drawn height).
# Lengths after the catalog's (a dagger 0.42 x 1.6, a sword 0.8 to 1.05: Tools/weapon_grips.tsv)
WEAPONS = [
    dict(id='melee:cor:twin_daggers', src='twin_daggers', pieces=[
        dict(role='main', x=(0, 960), grip=(700, 1484), head=1300, length=0.62),
        dict(role='off', x=(960, 2048), grip=(1352, 1484), head=1300, length=0.62)]),
    dict(id='melee:cor:spoke_mace', src='spoke_mace', pieces=[
        dict(role='main', x=(0, 2048), grip=(991, 1700), head=1130, length=0.95)]),
    dict(id='melee:cor:horn_club', src='horn_club', pieces=[
        dict(role='main', x=(0, 2048), grip=(1049, 1505), head=880, length=0.9)]),
    dict(id='melee:cor:hook_dagger', src='hook_dagger_shield', pieces=[
        dict(role='main', x=(0, 1000), grip=(652, 1654), head=1460, length=0.66),
        dict(role='shield', x=(1000, 2048), grip=(1516, 1060), head=0, length=0.5)]),
    dict(id='melee:cor:scimitar', src='scimitar', pieces=[
        dict(role='main', x=(0, 2048), grip=(1018, 1844), head=1600, length=1.0)]),
    dict(id='melee:cor:cleaver', src='cleaver', pieces=[
        dict(role='main', x=(0, 2048), grip=(1219, 1219), head=830, length=0.78)]),
    # the second batch (Cor, 2026-11-22)
    dict(id='melee:cor:lance', src='lance', pieces=[
        dict(role='main', x=(0, 900), grip=(590, 1520), head=1380, length=1.25),
        dict(role='shield', x=(900, 2048), grip=(1380, 1200), head=0, length=0.5)]),
    # the gunblade (Cor: "hunter and hider, unlock one for both"): also a hider gun, the same card, the shot from the
    # gun's front (source pixels); the catalog's two entries name each other as "twin"
    dict(id='melee:cor:gunblade', src='gunblade', gun=dict(id='gun:cor:gunblade', muzzle=(410, 560)), pieces=[
        dict(role='main', x=(0, 900), grip=(460, 1560), head=1380, length=0.95),
        dict(role='shield', x=(900, 2048), grip=(1460, 1300), head=0, length=0.55)]),
    dict(id='melee:cor:great_cleaver', src='great_cleaver', pieces=[
        dict(role='main', x=(0, 2048), grip=(1000, 1760), head=1560, length=1.15)]),
    dict(id='melee:cor:knife', src='knife_kite_shield', pieces=[
        dict(role='main', x=(0, 1000), grip=(625, 1600), head=1340, length=0.6),
        dict(role='shield', x=(1000, 2048), grip=(1580, 1200), head=0, length=0.5)]),
    dict(id='melee:cor:gem_sword', src='gem_sword', pieces=[
        dict(role='main', x=(0, 2048), grip=(1055, 1400), head=1030, length=1.0)]),
    # hider guns (Cor: "the bow, light bowgun and heavy bowgun are all hider weapons"): one piece, the shot from the
    # muzzle (source pixels). The bowguns are drawn from above: their card lies flat (plane 'flat', the XZ plane);
    # the bow from the side, turned a quarter (rot 1: its right side, where the arrow flies, up)
    dict(id='gun:cor:light_bowgun', src='light_bowgun', muzzle=(1080, 140), pieces=[
        dict(role='main', x=(0, 2048), grip=(1070, 1660), head=0, length=0.75, plane='flat')]),
    dict(id='gun:cor:heavy_bowgun', src='heavy_bowgun', muzzle=(1010, 170), pieces=[
        dict(role='main', x=(0, 2048), grip=(770, 1300), head=0, length=0.85, plane='flat')]),
    dict(id='gun:cor:bow', src='bow', rot=1, muzzle=(1100, 800), pieces=[
        dict(role='main', x=(0, 2048), grip=(1150, 990), head=0, length=0.43)]),
]


def crop(rgba, x0, x1):
    a = rgba[..., 3].copy()
    a[:, :x0] = 0; a[:, x1:] = 0
    ys, xs = np.nonzero(a > 8)
    pad = 4
    return max(0, xs.min() - pad), max(0, ys.min() - pad), min(rgba.shape[1], xs.max() + pad + 1), min(rgba.shape[0], ys.max() + pad + 1)


def bleed(img):
    """the colour of the cut-out's edge spread into the transparent pixels around it (no dark fringe when filtered)"""
    a = np.array(img).astype(np.float32)
    rgb, al = a[..., :3], a[..., 3:] / 255.0
    have = al[..., 0] > 0.5
    out = rgb.copy()
    for _ in range(12):
        acc = np.zeros_like(out); cnt = np.zeros(have.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            s = np.roll(np.roll(out, dy, 0), dx, 1); h = np.roll(np.roll(have, dy, 0), dx, 1)
            acc += s * h[..., None]; cnt += h
        grow = (~have) & (cnt > 0)
        out[grow] = acc[grow] / cnt[grow][:, None]
        have = have | grow
    a[..., :3] = out
    return Image.fromarray(a.clip(0, 255).astype(np.uint8), 'RGBA')


def edge_depth(mask):
    """how far each pixel is inside the mask (4-connected steps), by repeated erosion"""
    d = np.zeros(mask.shape, np.float32)
    cur = mask.copy(); k = 0
    while cur.any() and k < 400:
        k += 1; d[cur] = k
        cur = cur & np.roll(cur, 1, 0) & np.roll(cur, -1, 0) & np.roll(cur, 1, 1) & np.roll(cur, -1, 1)
    return d


def blob_noise(shape, seed, cell=24):
    """smooth noise in 0..1 (bilinear over a coarse random grid)"""
    rng = np.random.RandomState(seed)
    gh, gw = shape[0] // cell + 2, shape[1] // cell + 2
    g = Image.fromarray((rng.rand(gh, gw) * 255).astype(np.uint8), 'L').resize((gw * cell, gh * cell), Image.BICUBIC)
    return np.array(g)[:shape[0], :shape[1]].astype(np.float32) / 255.0


def wear_map(alpha, head_row, shield, seed):
    """the wear at which each pixel is eaten (0..1, over 1 never): nibbled in from the edges and down from the tip;
    below the head (the handle) it stays, so a stick is left (Cor). A shield wears from its rim in."""
    mask = alpha > 127
    depth = edge_depth(mask)
    dn = depth / max(depth.max(), 1.0)
    h = alpha.shape[0]
    rows = np.arange(h, dtype=np.float32)[:, None] / max(h - 1, 1)          # 0 top (the tip) .. 1 bottom
    noise = blob_noise(alpha.shape, seed)
    if shield:
        v = 0.12 + 0.75 * dn + 0.25 * (noise - 0.5)
    else:
        along = np.clip(rows / max(head_row / h, 0.05), 0, 1)               # 0 at the tip .. 1 where the head begins
        v = 0.08 + 0.45 * along + 0.55 * dn + 0.3 * (noise - 0.5)
        v[int(head_row):, :] = 2.0                                          # the handle: never
    v[~mask] = 0.0
    return v


_SRC = {}
def source(name, rot=0):
    """a drawing, turned rot quarters anticlockwise (np.rot90: a point (x, y) goes to (y, 2047 - x))"""
    if (name, rot) not in _SRC:
        rgba = np.ascontiguousarray(np.rot90(np.array(Image.open(os.path.join(SRC, name + '.png')).convert('RGBA')), rot))
        despeck(rgba)
        _SRC[(name, rot)] = rgba
    return _SRC[(name, rot)]


def despeck(rgba, cell=8, keep=40):
    """stray specks of paint away from the drawing (a dot would float beside the card): blobs smaller than keep cells
    of cell x cell pixels are cleared"""
    h, w = rgba.shape[:2]
    m = (rgba[..., 3] > 8).reshape(h // cell, cell, w // cell, cell).any(axis=(1, 3))
    lab = np.zeros(m.shape, np.int32); n = 0; sizes = [0]
    for sy, sx in zip(*np.nonzero(m)):
        if lab[sy, sx]: continue
        n += 1; stack = [(sy, sx)]; lab[sy, sx] = n; c = 0
        while stack:
            y, x = stack.pop(); c += 1
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < m.shape[0] and 0 <= xx < m.shape[1] and m[yy, xx] and not lab[yy, xx]:
                        lab[yy, xx] = n; stack.append((yy, xx))
        sizes.append(c)
    for k in range(1, n + 1):
        if sizes[k] < keep:
            big = np.kron((lab == k).astype(np.uint8), np.ones((cell, cell), np.uint8)).astype(bool)
            rgba[big[:h, :w], 3] = 0


def cut(piece_px):
    """every piece cut out of its drawing, at most piece_px on its longest side"""
    pieces = []
    for w in WEAPONS:
        rgba = source(w['src'], w.get('rot', 0))
        for k, p in enumerate(w['pieces']):
            x0, y0, x1, y1 = crop(rgba, *p['x'])
            sub = Image.fromarray(rgba[y0:y1, x0:x1].copy(), 'RGBA')
            sc = min(1.0, piece_px / max(sub.size))
            size = (max(1, round(sub.size[0] * sc)), max(1, round(sub.size[1] * sc)))
            pieces.append(dict(w=w, p=p, k=k, box=(x0, y0, x1, y1), img=sub.resize(size, Image.LANCZOS), sc=sc))
    return pieces


def pack(pieces):
    """shelf-pack, tallest first; False when they do not fit"""
    order = sorted(range(len(pieces)), key=lambda i: -pieces[i]['img'].size[1])
    x = y = rowh = 0; GAP = 6
    for i in order:
        iw, ih = pieces[i]['img'].size
        if x + iw > ATLAS: x = 0; y += rowh + GAP; rowh = 0
        pieces[i]['at'] = (x, y)
        x += iw + GAP; rowh = max(rowh, ih)
    return y + rowh <= ATLAS


def main():
    for px in range(PIECE_PX, 255, -16):
        pieces = cut(px)
        if pack(pieces): break
    else:
        raise SystemExit('the atlas is full')
    print('pieces at most', px, 'px')
    atlas = Image.new('RGBA', (ATLAS, ATLAS), (0, 0, 0, 0))
    wear = np.zeros((ATLAS, ATLAS), np.uint8)
    for i, pc in enumerate(pieces):
        img = bleed(pc['img'])
        atlas.paste(img, pc['at'])
        al = np.array(pc['img'])[..., 3]
        x0, y0, x1, y1 = pc['box']
        head_row = (pc['p']['head'] - y0) * pc['sc']
        v = wear_map(al, head_row, pc['p']['role'] == 'shield', 17 + i)
        enc = np.where(v >= 1.0, 255, np.clip(v * 254, 0, 254)).astype(np.uint8)
        ax, ay = pc['at']
        wear[ay:ay + al.shape[0], ax:ax + al.shape[1]] = enc
    os.makedirs(TEX, exist_ok=True)
    atlas.save(os.path.join(TEX, 'CardWeapons.png'))
    Image.fromarray(wear, 'L').save(os.path.join(TEX, 'CardWeaponsWear.png'))

    out = []
    for w in WEAPONS:
        entry = dict(id=w['id'], pieces=[], shelf=[])
        mine = [pc for pc in pieces if pc['w'] is w]
        # one scale for the whole drawing (both pieces as drawn): from the main piece's held length
        main = mine[0]
        mpp = main['p']['length'] / (main['box'][3] - main['box'][1])        # metres per source pixel
        for pc in mine:
            p = pc['p']; x0, y0, x1, y1 = pc['box']
            if p['role'] == 'shield': mpp_p = p['length'] / max(x1 - x0, y1 - y0)
            else: mpp_p = p['length'] / (y1 - y0)
            gx, gy = p['grip']
            ax, ay = pc['at']; iw, ih = pc['img'].size
            u0, u1 = ax / ATLAS, (ax + iw) / ATLAS
            v1, v0 = 1 - ay / ATLAS, 1 - (ay + ih) / ATLAS                      # (Unity: v up)
            shield = p['role'] == 'shield'
            flat = p.get('plane') == 'flat'

            def at(px, py, gx=gx, gy=gy, mpp_p=mpp_p, shield=shield, flat=flat):
                if shield: return [(px - gx) * mpp_p, 0.04, (gy - py) * mpp_p]
                if flat: return [(px - gx) * mpp_p, 0.0, (gy - py) * mpp_p]
                return [0.0, -(px - gx) * mpp_p, (gy - py) * mpp_p]
            if pc is main: main_at = at
            # corners: bottom-left, bottom-right, top-right, top-left of the picture, with their uvs
            corners = [dict(p=at(x0, y1), uv=[u0, v0]), dict(p=at(x1, y1), uv=[u1, v0]),
                       dict(p=at(x1, y0), uv=[u1, v1]), dict(p=at(x0, y0), uv=[u0, v1])]
            # the chip spots: opaque pixels of the head, spread out (farthest-point picks)
            al = np.array(pc['img'])[..., 3]
            ys, xs = np.nonzero(al > 200)
            hr = (p['head'] - y0) * pc['sc']
            sel = ys < hr if not shield else np.ones_like(ys, bool)
            ys, xs = ys[sel], xs[sel]
            pts = np.array([at(x0 + xx / pc['sc'], y0 + yy / pc['sc']) for yy, xx in zip(ys[::37], xs[::37])])
            spots = []
            if len(pts):
                c = pts.mean(0); pick = int(np.argmax(((pts - c) ** 2).sum(1)))
                dist = np.full(len(pts), np.inf)
                for _ in range(SPOTS):
                    spots.append([round(float(t), 4) for t in pts[pick]])
                    dist = np.minimum(dist, ((pts - pts[pick]) ** 2).sum(1)); pick = int(np.argmax(dist))
            # the head's colour (opaque, not the white halo or the black ink): its mean
            rgb = np.array(pc['img'])[..., :3][..., :].reshape(-1, 3)[(al.reshape(-1) > 200)]
            lum = rgb.mean(1)
            body = rgb[(lum > 60) & (lum < 225)]
            col = body.mean(0) if len(body) else np.array([128, 128, 128])
            tip = (gy - y0) * mpp_p
            head = max(0.0, (gy - p['head']) * mpp_p) if not shield else 0.0
            entry['pieces'].append(dict(role=p['role'], corners=corners, plane='xz' if shield or flat else 'yz',
                                        tip=round(tip, 4), head=round(max(head, tip * 0.45), 4) if not shield else 0.0,
                                        color=[int(t) for t in col], spots=[t for sp in spots for t in sp]))
            # the shelf: the whole drawing as drawn, facing the room (the XZ plane), the main piece's grip at the origin
            mgx, mgy = main['p']['grip']
            entry['shelf'].append(dict(corners=[dict(p=[(px - mgx) * mpp, 0.0, (mgy - py) * mpp], uv=c['uv']) for c, (px, py) in
                                                zip(corners, [(x0, y1), (x1, y1), (x1, y0), (x0, y0)])]))
        gun = w['id'] if w['id'].startswith('gun:') else (w['gun']['id'] if w.get('gun') else None)
        if gun:
            # a hider gun: the main piece in the hand, the shot from the drawn muzzle; on the gun shelf it lies along
            # the board seen from the side (the YZ plane, which the shelf turns to face the room)
            mx_, my_ = w['muzzle'] if 'muzzle' in w else w['gun']['muzzle']
            mgx, mgy = main['p']['grip']
            x0, y0, x1, y1 = main['box']
            side = [dict(p=[0.0, -(px - mgx) * mpp, (mgy - py) * mpp], uv=c['uv']) for c, (px, py) in
                    zip(entry['pieces'][0]['corners'], [(x0, y1), (x1, y1), (x1, y0), (x0, y0)])]
            g = dict(id=gun, pieces=[dict(entry['pieces'][0])], shelf=[dict(corners=side)],
                     muzzle=[round(float(t), 4) for t in main_at(mx_, my_)])
            if gun == w['id']: entry = g
            else: out.append(entry); entry = g; entry['twin'] = w['id']
        out.append(entry)
    json.dump(dict(note='Made by Tools/make_card_weapons.py: Cor\'s drawn weapons as cards (WeaponBuilder).', atlas=ATLAS,
                   weapons=out), open(os.path.join(HERE, 'card_weapons.json'), 'w'), indent=1)
    print('atlas', os.path.join(TEX, 'CardWeapons.png'), 'pieces', len(pieces))


if __name__ == '__main__':
    main()
