"""Builds Assets/Resources/PropHunt/models_quaternius.bytes: Quaternius models (CC0, from the mirror at
http://cc0.cor.home/) that take the place of props once made in code. Each keeps the old model's name and is fitted
inside the old model's bounds (base on the floor, centred), so the generator's sizes and the maps do not change.
PropPrefabBaker.LoadReplacements reads the file after models.bytes and replaces the models of the same name.

    python Tools/make_quaternius.py [out.bytes]

Faces are flat-shaded and take one colour each (the material's, or the texture at the face's centre).

Two models are changed beyond scaling, because the generator stands things on and in them (GenModels.Surf):
- the wardrobe (Ultimate Furniture Pack, Closet): its right leaf is the door part `wardrobe_door`, its left leaf one
  more door part `wardrobe_doorL` (not in GenModels: PropPrefabBaker adds it as a second door); the legs are shortened so the inside floor is at 0.12 m as before;
- the rack (Ultimate Modular Sci-Fi, Props_Shelf_Tall): two of its five boards are left out and the heights are moved
  so the three boards' tops are at 0.2, 0.9 and 1.6 m as before."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
import quaternius_lib as qlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BLOB = os.path.join(ROOT, 'Assets', 'Resources', 'PropHunt', 'models.bytes')
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'PropHunt', 'models_quaternius.bytes')

# name of the replaced model, pack slug, item name, (x, y, z) of the old bounds when the model is not in models.bytes
SWAPS = [
    ('gen_dumpster', 'quaternius-toonshootergamekit', 'TrashContainer', None),
    ('gen_coop', 'quaternius-lowpoly-farm-buildings', 'ChickenCoop', None),
    ('gen_car', 'quaternius-lowpoly-cars', 'SportsCar', (1.8, 1.4, 4.0)),
    ('gen_tireStack', 'quaternius-zombieapocalypsekit', 'Wheels_Stack', (0.65, 0.8, 0.65)),
]


def obj_path(pack, item):
    f = qlib.record(pack, item)['formats']['obj']
    for d in f['deps']: qlib.fetch(d)
    return qlib.fetch(f['file'])


def wardrobe(old):
    """-> [body entry, door entry]"""
    ob = []
    V, T, C = qlib.load_obj(obj_path('quaternius-ultimatefurniture', 'Closet'), ob)
    ob = np.array(ob)
    # the inside floor is at 0.40 of 2.97 in the file: it goes to 0.12 of 2.0 (the old wardrobe's), the top to 2.0
    top = V[:, 1].max()
    V = qlib.warp_y(V, [(V[:, 1].min(), 0.0), (0.40, 0.12), (top, 2.0)])
    o = old['gen_wardrobe']
    V = qlib.fit_verts(V, o['mn'], o['mx'], 'box')
    left = np.char.find(ob, 'LeftDoor') >= 0; right = np.char.find(ob, 'RightDoor') >= 0
    res = [qlib.make_entry('gen_wardrobe', '', (0, 0, 0), V, T[~left & ~right], C[~left & ~right])]
    # each leaf's hinge: its outer edge, its foot, its back face (PHDoors swings a door by the side its hinge is on)
    for name, sel, edge in (('wardrobe_door', right, 1), ('wardrobe_doorL', left, 0)):
        dv = V[T[sel]].reshape(-1, 3)
        piv = np.array([dv[:, 0].max() if edge else dv[:, 0].min(), dv[:, 1].min(), dv[:, 2].min()])
        res.append(qlib.make_entry(name, 'gen_wardrobe', piv, V - piv, T[sel], C[sel]))
    return res


def rack(old):
    V, T, C = qlib.load_obj(obj_path('quaternius-lowpoly-modular-sci-fi-environments', 'Props_Shelf_Tall'))
    cy = V[T][:, :, 1].mean(1)
    frame = (C == C[np.argmin(V[T][:, :, 1].min(1))]).all(1)     # the frame's colour: that of the lowest face
    drop = ~frame & (((cy > 0.95) & (cy < 1.10)) | ((cy > 2.17) & (cy < 2.31)))   # the second and fourth boards
    T, C = T[~drop], C[~drop]
    # a board is 0.39 to 0.43 high in the file, with its flat top at 0.41 inside a rim: the flat goes to the old height
    V = qlib.warp_y(V, [(V[:, 1].min(), 0.0), (0.39, 0.16), (0.41, 0.20), (0.43, 0.215), (1.61, 0.86), (1.63, 0.90), (1.65, 0.915), (2.83, 1.56), (2.85, 1.60), (2.87, 1.615), (V[:, 1].max(), 2.0)])
    o = old['gen_rack']
    return [qlib.make_entry('gen_rack', '', (0, 0, 0), qlib.fit_verts(V, o['mn'], o['mx'], 'box'), T, C)]


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else OUT
    old = {m['name']: m for m in qlib.load_blob(BLOB)}
    res = []
    for name, pack, item, box in SWAPS:
        o = old[name] if box is None else dict(name=name, parent='', pivot=(0, 0, 0), mn=(-box[0] / 2, 0, -box[2] / 2), mx=(box[0] / 2, box[1], box[2] / 2))
        (V, T, C), fmt = qlib.load_item(pack, item)
        res.append(qlib.fit_entry(o, V, T, C, mode='snug'))
    res += wardrobe(old) + rack(old)
    for e in res:
        sz = np.array(e['mx']) - np.array(e['mn'])
        print('%-14s %5d triangles, %.2f x %.2f x %.2f m, pivot %s' % (e['name'], len(e['t']) // 3, sz[0], sz[1], sz[2], np.round(e['pivot'], 3)))
    qlib.save_blob(out, res)
    print('wrote', out, os.path.getsize(out), 'bytes')


if __name__ == '__main__':
    main()
