"""A first guess at each weapon's grip and size -> Tools/weapon_grips.tsv (the lines PHWeapons holds them by)

For every model in Resources/PropHunt/weapons.bytes (make_weapons.py) without a line in weapon_grips.tsv yet, a line
is added (lines already there are kept: they may have been tuned at the grip station, PHGripStation):
  melee: the model is cut into 1 cm slices along its length (+Z); the handle is the lowest run of thin slices at least
         7 cm long in the lower part (a pommel's tip and a guard are wider or shorter). A handle up to 22 cm is held in
         its middle (a sword's grip), a longer one 7 cm from its end (an axe, a hammer); a pole a metre or longer
         (a spear, a halberd) 30 % of the way up. The grip's x and y are the middle of the slices there.
  gun:   cut into 1 cm slices from the bottom up (+Y); the handle is the run of narrow slices (11 cm or less along
         the barrel) from the bottom; the fist's middle is 3 cm under its top, in the middle of the slice there. A
         gun with no such handle keeps its origin (make_weapons.py put that at the handle already).
  size:  (Cor, 2026-11-21: some weapons need to be a lot bigger) daggers, knives and other short melee models
         (0.45 m or less) x1.6; one-hand guns (shorter than 0.35 m) x1.6; long guns x1.25.

    python Tools/estimate_grips.py           (adds the missing lines)
    python Tools/estimate_grips.py --all     (guesses every line again: loses what the station tuned)
"""
import os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import quaternius_lib as q

BLOB = os.path.join(HERE, '..', 'Assets', 'Resources', 'PropHunt', 'weapons.bytes')
TSV = os.path.join(HERE, 'weapon_grips.tsv')
STEP = 0.01
HEADER = """# Each weapon's grip in the hand (PHWeapons; WeaponBuilder reads this file at Build world scene):
#   id <TAB> grip x y z (the point of the model in the middle of the closed fist, metres in the model's own space)
#      <TAB> turn x y z (degrees about that point, Unity Euler) <TAB> size (1 = as make_weapons.py made it)
# First guesses: Tools/estimate_grips.py. Tuned at the lobby's grip station: Tools/import_grips.py."""


def slice_pts(V, T, axis, val):
    tv = V[T.reshape(-1, 3)]
    pts = []
    for a, b in ((0, 1), (1, 2), (2, 0)):
        pa, pb = tv[:, a], tv[:, b]
        da, db = pa[:, axis] - val, pb[:, axis] - val
        m = da * db < 0
        t = da[m] / (da[m] - db[m])
        pts.append(pa[m] + (pb[m] - pa[m]) * t[:, None])
    return np.concatenate(pts)


def melee_grip(V, T):
    z0, z1 = V[:, 2].min(), V[:, 2].max(); L = z1 - z0
    zs = np.arange(z0 + STEP / 2, z1, STEP)
    W, C = [], []
    for z in zs:
        p = slice_pts(V, T, 2, z)
        if len(p) < 3: W.append(np.nan); C.append((np.nan, np.nan)); continue
        W.append(max(np.ptp(p[:, 0]), np.ptp(p[:, 1])))
        C.append(((p[:, 0].min() + p[:, 0].max()) / 2, (p[:, 1].min() + p[:, 1].max()) / 2))
    W = np.array(W); C = np.array(C)
    low = (zs < z0 + 0.6 * L) & ~np.isnan(W)
    thr = np.percentile(W[low], 25) * 1.3 + 0.006
    thin = (W <= thr) & (zs < z0 + 0.7 * L)
    runs, i = [], 0
    while i < len(zs):
        if thin[i]:
            j = i
            while j + 1 < len(zs) and thin[j + 1]: j += 1
            runs.append((i, j)); i = j + 1
        else: i += 1
    runs = [r for r in runs if (r[1] - r[0] + 1) * STEP >= 0.07 and zs[r[0]] < z0 + 0.45 * L]
    if L >= 1.0:
        zg = z0 + 0.3 * L
    elif runs:
        a, b = runs[0]; za, zb = zs[a] - STEP / 2, zs[b] + STEP / 2
        zg = (za + zb) / 2 if zb - za <= 0.22 else za + 0.07
    else:
        zg = z0 + 0.08
    c = np.nanmean(C[np.abs(zs - zg) <= 0.025], 0)
    return [float(c[0]), float(c[1]), float(zg)]


def gun_grip(V, T):
    y0, y1 = V[:, 1].min(), V[:, 1].max()
    ys = np.arange(y0 + STEP / 2, y1, STEP)
    rows = []
    for y in ys:
        p = slice_pts(V, T, 1, y)
        rows.append(None if len(p) < 3 else (p[:, 2].min(), p[:, 2].max(), (p[:, 0].min() + p[:, 0].max()) / 2))
    top = -1
    for i, r in enumerate(rows):
        if r is None:
            if i < 2: continue
            break
        if r[1] - r[0] <= 0.11: top = i
        else: break
    if top < 2: return [0.0, 0.0, 0.0]
    yg = max(ys[top] + STEP / 2 - 0.03, y0 + 0.03)
    r = rows[int(np.argmin(np.abs(ys - yg)))]
    return [float(r[2]), float(yg), float((r[0] + r[1]) / 2)]


def size_for(kind, V):
    L = float(np.ptp(V[:, 2]))
    if kind == 'melee': return 1.6 if L <= 0.45 else 1.0
    return 1.6 if L < 0.35 else 1.25


def line(name, grip, turn, size):
    return '%s\t%.4f %.4f %.4f\t%.1f %.1f %.1f\t%.2f' % ((name,) + tuple(grip) + tuple(turn) + (size,))


def main():
    have = {}
    lines = []
    if os.path.exists(TSV) and '--all' not in sys.argv:
        lines = open(TSV, encoding='utf8').read().splitlines()
        have = {l.split('\t')[0] for l in lines if l and not l.startswith('#')}
    if not lines: lines = HEADER.splitlines()
    added = 0
    for m in q.load_blob(BLOB):
        if m['name'] in have: continue
        kind = m['name'].split(':')[0]
        V, T = m['v'], m['t']
        g = melee_grip(V, T) if kind == 'melee' else gun_grip(V, T)
        lines.append(line(m['name'], g, (0, 0, 0), size_for(kind, V))); added += 1
    open(TSV, 'w', encoding='utf8', newline='\n').write('\n'.join(lines) + '\n')
    print('added', added, 'lines to', TSV)


if __name__ == '__main__':
    main()
