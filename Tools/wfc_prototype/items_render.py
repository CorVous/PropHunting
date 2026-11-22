"""Pictures of the item pass: every 1 m cell drawn as what stands on it."""
from PIL import Image, ImageDraw
from wfc import DX, DZ
import sitegen as S
import items as I
import render as R

GROUND_COL = {"FLOOR": (112, 150, 96), "GRASS": (176, 207, 132), "LAWN": (200, 224, 162), "PAVING": (205, 203, 196), "BOARDS": (226, 205, 170), "RUG": (176, 108, 100)}
WOODC = (150, 104, 62)
DARKW = (104, 70, 42)


def draw(it, px=14, title=None):
    s, n = it.site, it.n
    top = 34 if title else 0
    im = Image.new("RGB", (n * px, n * px + top), R.PAPER)
    dr = ImageDraw.Draw(im)
    T = it.tiles
    G = s.Gn
    P = px * I.F

    def box(c):
        x, z = c % n, c // n
        return x * px, top + (n - 1 - z) * px, (x + 1) * px, top + (n - z) * px

    def sub(c, a0, b0, a1, b1):      # a part of the cell, in tenths, x right and y down on the picture
        x0, y0, _, _ = box(c)
        return [x0 + a0 * px // 10, y0 + b0 * px // 10, x0 + a1 * px // 10, y0 + b1 * px // 10]

    def toward(c, d, depth):         # the strip of the cell on side d, `depth` tenths deep
        return [sub(c, 0, 0, 10, depth), sub(c, 10 - depth, 0, 10, 10), sub(c, 0, 10 - depth, 10, 10), sub(c, 0, 0, depth, 10)][d]

    def wall_side(c):
        for d in range(4):
            if it.walls[c] >> d & 1 and (c, d) not in it.doors:
                return d
        return 0

    # lanes and the release (outer cells)
    for oc in range(G * G):
        t = s.tiles[oc]
        if not S.is_lane(t):
            continue
        x, z = oc % G, oc // G
        x0, y0, x1, y1 = x * P, top + (G - 1 - z) * P, (x + 1) * P, top + (G - z) * P
        dr.rectangle([x0, y0, x1, y1], fill=R.VERGE)
        m = S.lane_mask(t)
        a = P // 5
        dr.rectangle([x0 + a, y0 + a, x1 - a, y1 - a], fill=R.LANE)
        if m & 1: dr.rectangle([x0 + a, y0, x1 - a, y0 + a], fill=R.LANE)
        if m & 2: dr.rectangle([x1 - a, y0 + a, x1, y1 - a], fill=R.LANE)
        if m & 4: dr.rectangle([x0 + a, y1 - a, x1 - a, y1], fill=R.LANE)
        if m & 8: dr.rectangle([x0, y0 + a, x0 + a, y1 - a], fill=R.LANE)
        if oc == s.release:
            dr.ellipse([x0 + P // 3, y0 + P // 3, x1 - P // 3, y1 - P // 3], fill=(204, 60, 60), outline=R.WALL, width=2)

    # ground
    for c in range(n * n):
        r = it.reg[c]
        if r < 0:
            continue
        g = it.regions[r]["prof"]["ground"]
        col = GROUND_COL["RUG" if T[c] == I.ID["RUG"] else g]
        dr.rectangle(box(c), fill=col)
    # pieces
    for c in range(n * n):
        if it.reg[c] < 0:
            continue
        t = T[c]
        g = I.GROUP[t]
        nm = I.NAMES[t]
        x0, y0, x1, y1 = box(c)
        cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
        h = (c * 2654435761) >> 7
        ws = wall_side(c)
        if t in I.GROUND:
            continue
        if g == "TREE":
            rr = px * (52 + h % 12) // 100
            dr.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=(52, 100, 56))
            dr.ellipse([cx - rr // 2, cy - rr // 2 - 1, cx + rr // 3, cy + rr // 3 - 1], fill=(78, 130, 74))
        elif g == "BUSH":
            rr = px * 34 // 100
            dr.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=(96, 150, 78))
        elif g == "FERN":
            for i in range(3):
                dr.line([cx, cy + 3, cx - 4 + i * 4, cy - 3], fill=(70, 122, 66), width=1)
        elif g == "ROCK":
            dr.ellipse(sub(c, 2, 3, 8, 8), fill=(150, 150, 146), outline=(110, 110, 108))
        elif g == "LOG":
            dr.rounded_rectangle(sub(c, 1, 3, 9, 7) if h & 1 else sub(c, 3, 1, 7, 9), 3, fill=WOODC, outline=DARKW)
        elif g == "STUMP":
            dr.ellipse(sub(c, 3, 3, 7, 7), fill=(176, 136, 90), outline=DARKW)
        elif g == "MUSHROOM":
            dr.ellipse(sub(c, 4, 4, 6, 6), fill=(214, 70, 60))
            dr.ellipse(sub(c, 2, 6, 4, 8), fill=(230, 220, 200))
        elif g == "FLOWERS":
            col = [(236, 200, 70), (226, 120, 170), (240, 240, 235), (150, 130, 220)][it.reg[c] * 7 % 4 if False else (c // n // 6 + c % n // 6) % 4]
            for i in range(4):
                fx, fy = x0 + 2 + (h >> (i * 3) & 7) * (px - 5) // 7, y0 + 2 + (h >> (i * 3 + 9) & 7) * (px - 5) // 7
                dr.ellipse([fx - 1, fy - 1, fx + 2, fy + 2], fill=col)
        elif g == "WATER":
            dr.rectangle([x0, y0, x1, y1], fill=(110, 170, 214))
        elif g == "REEDS":
            dr.rectangle([x0, y0, x1, y1], fill=(150, 196, 176))
            for i in range(3):
                dr.line([x0 + 3 + i * 4, y1 - 2, x0 + 3 + i * 4, y0 + 3], fill=(84, 130, 80), width=1)
        elif g == "TRAIL":
            m = I.trail_mask(t)
            a = 3
            col = (196, 170, 120)
            dr.rectangle(sub(c, a, a, 10 - a, 10 - a), fill=col)
            if m & 1: dr.rectangle(sub(c, a, 0, 10 - a, a), fill=col)
            if m & 2: dr.rectangle(sub(c, 10 - a, a, 10, 10 - a), fill=col)
            if m & 4: dr.rectangle(sub(c, a, 10 - a, 10 - a, 10), fill=col)
            if m & 8: dr.rectangle(sub(c, 0, a, a, 10 - a), fill=col)
        elif g == "FIRE":
            dr.ellipse(sub(c, 1, 1, 9, 9), fill=(120, 116, 112))
            dr.ellipse(sub(c, 3, 3, 7, 7), fill=(240, 140, 40))
        elif g == "SEAT":
            dr.rounded_rectangle(toward(c, (I.dirn(t) + 2) & 3, 5), 2, fill=WOODC, outline=DARKW)
        elif g == "TENT":
            dr.polygon([(x0 + 1, y1 - 1), (x1 - 1, y1 - 1), (cx, y0 + 1)], fill=(70, 130, 190), outline=(40, 80, 130))
        elif g == "FLOWERBED":
            dr.rectangle(sub(c, 1, 1, 9, 9), fill=(134, 100, 72))
            for i in range(3):
                dr.ellipse([x0 + 3 + i * 3, y0 + 3 + (h >> i & 3) * 2, x0 + 6 + i * 3, y0 + 6 + (h >> i & 3) * 2], fill=(232, 120, 160))
        elif g == "HEDGE":
            dr.rectangle(sub(c, 0, 0, 10, 10), fill=(62, 112, 62))
            dr.rectangle(sub(c, 2, 2, 8, 8), fill=(82, 134, 76))
        elif g == "TABLE":
            dr.rectangle(sub(c, 1, 1, 9, 9), fill=(186, 140, 90), outline=DARKW)
        elif g == "CHAIR":
            dr.rectangle(toward(c, I.dirn(t), 5), fill=(120, 90, 150), outline=(70, 50, 90))
        elif g == "DESK":
            dr.rectangle(toward(c, ws, 7), fill=(200, 160, 110), outline=DARKW)
        elif g == "BLANKET":
            dr.rectangle(sub(c, 1, 1, 9, 9), fill=(214, 70, 70))
            dr.line([x0 + 1, cy, x1 - 1, cy], fill=(245, 240, 235), width=1)
            dr.line([cx, y0 + 1, cx, y1 - 1], fill=(245, 240, 235), width=1)
        elif g == "POST":
            dr.ellipse(sub(c, 3, 3, 7, 7), fill=DARKW)
        elif g == "LINE":
            if nm.endswith("_NS"):
                dr.line([cx, y0, cx, y1], fill=(70, 70, 70), width=1)
                dr.rectangle([cx - 3, y0 + 2, cx + 3, y1 - 2], fill=[(240, 240, 240), (120, 170, 220), (230, 130, 130)][h % 3])
            else:
                dr.line([x0, cy, x1, cy], fill=(70, 70, 70), width=1)
                dr.rectangle([x0 + 2, cy - 3, x1 - 2, cy + 3], fill=[(240, 240, 240), (120, 170, 220), (230, 130, 130)][h % 3])
        elif g == "VEG":
            dr.rectangle([x0, y0, x1, y1], fill=(142, 106, 74))
            col = [(90, 160, 70), (200, 120, 50), (150, 190, 90)][(c % n if nm.endswith("_NS") else c // n) % 3]
            for i in range(3):
                if nm.endswith("_NS"):
                    dr.ellipse([cx - 2, y0 + 1 + i * (px // 3), cx + 2, y0 + 5 + i * (px // 3)], fill=col)
                else:
                    dr.ellipse([x0 + 1 + i * (px // 3), cy - 2, x0 + 5 + i * (px // 3), cy + 2], fill=col)
        elif g in ("RACK", "PEW"):
            col = (150, 156, 170) if g == "RACK" else WOODC
            dr.rectangle(sub(c, 2, 0, 8, 10) if nm.endswith("_NS") else sub(c, 0, 2, 10, 8), fill=col, outline=(90, 94, 104) if g == "RACK" else DARKW)
        elif g == "CRATE":
            dr.rectangle(sub(c, 1, 1, 9, 9), fill=(206, 170, 110), outline=DARKW)
            dr.line([x0 + 2, y0 + 2, x1 - 2, y1 - 2], fill=DARKW, width=1)
        elif g == "BARREL":
            dr.ellipse(sub(c, 1, 1, 9, 9), fill=(160, 110, 70), outline=DARKW)
            dr.ellipse(sub(c, 3, 3, 7, 7), outline=DARKW)
        elif g == "HAY":
            dr.rounded_rectangle(sub(c, 0, 1, 10, 9), 3, fill=(226, 196, 96), outline=(170, 140, 60))
        elif g == "GRAVE":
            dr.rounded_rectangle(sub(c, 3, 1, 7, 6), 2, fill=(170, 170, 176), outline=(110, 110, 116))
            dr.rectangle(sub(c, 3, 6, 7, 9), fill=(150, 176, 120))
        elif g == "BEDH":
            dr.rectangle([x0 + 1, y0 + 1, x1 - 1, y1 - 1], fill=(120, 150, 200), outline=(60, 80, 120))
            dr.rectangle(toward(c, I.dirn(t), 5), fill=(245, 245, 240), outline=(60, 80, 120))
        elif g == "BEDF":
            dr.rectangle([x0 + 1, y0 + 1, x1 - 1, y1 - 1], fill=(120, 150, 200), outline=(60, 80, 120))
        elif g == "NIGHTSTAND":
            dr.rectangle(sub(c, 2, 2, 8, 8), fill=(170, 124, 80), outline=DARKW)
        elif g == "WARDROBE":
            dr.rectangle(toward(c, ws, 6), fill=(124, 84, 52), outline=(70, 46, 28))
        elif g == "SOFA":
            dr.rectangle([x0 + 1, y0 + 1, x1 - 1, y1 - 1], fill=(104, 160, 150), outline=(50, 96, 90))
            dr.rectangle(toward(c, ws, 3), fill=(70, 122, 114))
        elif g == "TV":
            dr.rectangle(toward(c, ws, 3), fill=(30, 30, 36))
        elif g == "ARMCHAIR":
            dr.rounded_rectangle(sub(c, 1, 1, 9, 9), 3, fill=(210, 150, 90), outline=(140, 96, 50))
        elif g == "PLANT":
            dr.ellipse(sub(c, 2, 2, 8, 8), fill=(170, 110, 80))
            dr.ellipse(sub(c, 3, 3, 7, 7), fill=(70, 150, 80))
        elif g == "SHELF":
            dr.rectangle(toward(c, ws, 4), fill=(170, 124, 80), outline=DARKW)
        elif g in ("COUNTER", "STOVE", "SINK", "TILL"):
            dr.rectangle(toward(c, ws, 7), fill=(196, 198, 204), outline=(120, 122, 130))
            if g == "STOVE":
                for (a, b) in ((3, 3), (7, 3), (3, 7), (7, 7)):
                    dr.ellipse([x0 + a * px // 10 - 1, y0 + b * px // 10 - 1, x0 + a * px // 10 + 1, y0 + b * px // 10 + 1], fill=(40, 40, 40))
            elif g == "SINK":
                dr.rectangle(sub(c, 3, 3, 7, 7), fill=(110, 170, 214))
            elif g == "TILL":
                dr.rectangle(sub(c, 3, 3, 7, 7), fill=(80, 170, 100))
        elif g == "FRIDGE":
            dr.rectangle(sub(c, 1, 1, 9, 9), fill=(240, 242, 245), outline=(130, 134, 140))
        elif g == "TOILET":
            dr.ellipse(sub(c, 2, 2, 8, 9), fill=(250, 250, 250), outline=(130, 134, 140))
        elif g == "TUB":
            dr.rounded_rectangle(sub(c, 0, 1, 10, 9), 3, fill=(250, 250, 250), outline=(130, 134, 140))
            dr.rounded_rectangle(sub(c, 2, 3, 8, 7), 2, fill=(150, 200, 230))
        elif g == "BASIN":
            dr.ellipse(toward(c, ws, 6), fill=(250, 250, 250), outline=(130, 134, 140))
        elif g == "WORKBENCH":
            dr.rectangle(toward(c, ws, 7), fill=(150, 110, 70), outline=DARKW)
            dr.rectangle(sub(c, 4, 4, 6, 6), fill=(120, 124, 130))
        elif g == "TOOLCHEST":
            dr.rectangle(sub(c, 1, 2, 9, 8), fill=(200, 60, 50), outline=(120, 30, 26))
        elif g == "ALTAR":
            dr.rectangle(toward(c, ws, 7), fill=(245, 242, 232), outline=(180, 150, 60))
    # walls, doors and fences
    for c in range(n * n):
        if it.reg[c] < 0:
            continue
        x0, y0, x1, y1 = box(c)
        for d in range(4):
            if not it.walls[c] >> d & 1:
                continue
            ln = [(x0, y0, x1, y0), (x1, y0, x1, y1), (x0, y1, x1, y1), (x0, y0, x0, y1)][d]
            x, z = c % n, c // n
            nx, nz = x + DX[d], z + DZ[d]
            other = it.indoor[nz * n + nx] if 0 <= nx < n and 0 <= nz < n else False
            if it.indoor[c] or other:
                if (c, d) in it.doors:
                    continue
                dr.line(ln, fill=R.WALL, width=3)
            else:
                k = it.regions[it.reg[c]]["key"]
                oc = (nz // I.F) * G + nx // I.F if 0 <= nx < n and 0 <= nz < n else -1
                if oc >= 0 and S.is_lane(s.tiles[oc]) and s.plots[k[1]]["zone"] in (3, 7, 8, 10, 11, 13):
                    continue
                dr.line(ln, fill=R.FENCE, width=1)
    if title:
        dr.text((6, 6), title, fill=R.INK, font=R.font(18, True))
    return im


if __name__ == "__main__":
    import sys
    out = sys.argv[1]
    for size, seed, px in ((2, 5, 16), (3, 3, 13), (1, 5, 18), (2, 8, 16), (0, 2, 20)):
        s = S.generate(seed, size)
        it = I.generate(s)
        kinds = sorted({r["kind"] for r in it.regions})
        im = draw(it, px, "%s  seed %d" % (S.SIZE_NAME[size], seed))
        # crop to the site
        n = it.n
        xs = [c % n for c in range(n * n) if it.reg[c] >= 0]; zs = [c // n for c in range(n * n) if it.reg[c] >= 0]
        x0 = max(0, min(xs) * px - 2 * px); x1 = min(im.width, (max(xs) + 1) * px + 2 * px)
        y0 = max(0, 34 + (n - 1 - max(zs)) * px - 2 * px); y1 = im.height
        body = im.crop((x0, y0, x1, y1))
        outim = Image.new("RGB", (body.width, body.height + 34), R.PAPER)
        outim.paste(im.crop((0, 0, min(im.width, body.width), 34)), (0, 0))
        outim.paste(body, (0, 34))
        outim.save("%s/wfc_items_%s_%d.png" % (out, S.SIZE_NAME[size], seed))
        print(S.SIZE_NAME[size], seed, outim.size, "props", it.props, "scenery", it.scenery, "repairs", it.repairs, "cut", it.cut_off, kinds)
