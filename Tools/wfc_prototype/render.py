"""Top-down pictures of the prototype's sites."""
from PIL import Image, ImageDraw, ImageFont
from wfc import DX, DZ, popcount
import sitegen as S

PAPER = (243, 239, 230)
LANE = (214, 193, 150)
VERGE = (196, 214, 160)
GREEN = (176, 207, 132)
YARD = (206, 226, 170)
WOOD = (86, 138, 78)
TREE = (58, 104, 58)
FENCE = (120, 92, 60)
WALL = (52, 46, 44)
INK = (40, 40, 40)
ZONE_COL = {1: (232, 170, 120), 2: (150, 160, 178), 3: (236, 128, 128), 7: (240, 204, 104), 8: (128, 176, 204), 9: (190, 108, 88),
            10: (150, 206, 150), 11: (168, 200, 120), 12: (170, 130, 96), 13: (222, 150, 214), 14: (200, 196, 220)}


def font(sz, bold=False):
    for p in ["/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"]:
        try:
            return ImageFont.truetype(p, sz)
        except Exception:
            pass
    return ImageFont.load_default()


def draw_site(s, px=44, title=None):
    n = s.Gn
    top = 34 if title else 0
    im = Image.new("RGB", (n * px, n * px + top), PAPER)
    dr = ImageDraw.Draw(im)
    T = s.tiles

    def box(c):
        x, z = c % n, c // n
        return x * px, top + (n - 1 - z) * px, (x + 1) * px, top + (n - z) * px

    def side(c, d):       # the cell's edge on side d as a line
        x0, y0, x1, y1 = box(c)
        return [(x0, y0, x1, y0), (x1, y0, x1, y1), (x0, y1, x1, y1), (x0, y0, x0, y1)][d]

    # ground
    for c in range(n * n):
        t = T[c]
        x0, y0, x1, y1 = box(c)
        if t == S.O:
            continue
        if t == S.W:
            dr.rectangle([x0, y0, x1, y1], fill=WOOD)
            h = (c * 2654435761) & 0xFFFF
            for i in range(5):
                tx = x0 + 6 + (h >> (i * 3) & 7) * (px - 12) // 7
                ty = y0 + 6 + (h >> (i * 3 + 1) & 7) * (px - 12) // 7
                r = 5 + (i & 1) * 2
                dr.ellipse([tx - r, ty - r, tx + r, ty + r], fill=TREE)
        elif t == S.G:
            dr.rectangle([x0, y0, x1, y1], fill=GREEN)
            h = (c * 40503) & 0xFFFF
            for i in range(2):
                tx = x0 + 8 + (h >> (i * 4) & 15) * (px - 16) // 15
                ty = y0 + 8 + (h >> (i * 4 + 2) & 15) * (px - 16) // 15
                dr.ellipse([tx - 3, ty - 3, tx + 3, ty + 3], fill=(140, 180, 104))
        elif S.is_lane(t):
            dr.rectangle([x0, y0, x1, y1], fill=VERGE)
            m = S.lane_mask(t)
            a = px // 5
            dr.rectangle([x0 + a, y0 + a, x1 - a, y1 - a], fill=LANE)
            if m & 1: dr.rectangle([x0 + a, y0, x1 - a, y0 + a], fill=LANE)
            if m & 2: dr.rectangle([x1 - a, y0 + a, x1, y1 - a], fill=LANE)
            if m & 4: dr.rectangle([x0 + a, y1 - a, x1 - a, y1], fill=LANE)
            if m & 8: dr.rectangle([x0, y0 + a, x0 + a, y1 - a], fill=LANE)
        else:
            dr.rectangle([x0, y0, x1, y1], fill=YARD)
    # release
    x0, y0, x1, y1 = box(s.release)
    dr.ellipse([x0 + px // 4, y0 + px // 4, x1 - px // 4, y1 - px // 4], fill=(204, 60, 60), outline=WALL, width=2)

    # buildings
    room = {}
    bz = {}
    for p in s.plots:
        q = p["plan"]
        if q is None:
            for col in p["cols"]:
                for c in col:
                    x0, y0, x1, y1 = box(c)
                    dr.rectangle([x0, y0, x1, y1], fill=ZONE_COL[p["zone"]])
            continue
        for py in range(q["d"]):
            for pxx in range(q["w"]):
                r = q["cells"][py * q["w"] + pxx]
                if r < 0:
                    continue
                u = p["off"] + (q["w"] - 1 - pxx if p["mir"] else pxx)
                c = p["cols"][u][py]
                room[c] = (id(p), r)
                bz[c] = p["zone"]
    for c, key in room.items():
        x0, y0, x1, y1 = box(c)
        col = ZONE_COL[bz[c]]
        k = key[1] % 3
        col = tuple(min(255, v + (k - 1) * 12) for v in col)
        dr.rectangle([x0, y0, x1, y1], fill=col)
    for c, key in room.items():
        x, z = c % n, c // n
        for d in range(4):
            nx, nz = x + DX[d], z + DZ[d]
            nc = nz * n + nx if 0 <= nx < n and 0 <= nz < n else -1
            other = room.get(nc)
            if other is None or other[0] != key[0]:
                dr.line(side(c, d), fill=WALL, width=4)
            elif other != key:
                dr.line(side(c, d), fill=WALL, width=1)
    # plot fences
    for c in range(n * n):
        i = s.cell_plot[c]
        if i < 0:
            continue
        x, z = c % n, c // n
        for d in range(4):
            nx, nz = x + DX[d], z + DZ[d]
            nc = nz * n + nx if 0 <= nx < n and 0 <= nz < n else -1
            if (nc < 0 or s.cell_plot[nc] != i) and c not in room:
                if nc >= 0 and S.is_lane(T[nc]) and s.plots[i]["zone"] in (3, 7, 8, 10, 11, 13):
                    continue      # open to the lane
                dr.line(side(c, d), fill=FENCE, width=2)
    # names
    f = font(max(10, px // 4), True)
    for p in s.plots:
        cells = [c for col in p["cols"] for c in col]
        mx = sum(box(c)[0] for c in cells) / len(cells) + px / 2
        my = sum(box(c)[1] for c in cells) / len(cells) + px / 2
        if p["plan"] is not None:
            bc = [c for c in cells if c in room]
            mx = sum(box(c)[0] for c in bc) / len(bc) + px / 2
            my = sum(box(c)[1] for c in bc) / len(bc) + px / 2
        name = S.ZONE_NAME[p["zone"]]
        w = dr.textlength(name, font=f)
        dr.text((mx - w / 2, my - px // 7), name, fill=INK, font=f)
    if title:
        dr.text((6, 6), title, fill=INK, font=font(18, True))
    return im


def sheet(sites, cols, px, pad=14, bg=(255, 255, 255)):
    ims = [draw_site(s, px, t) for s, t in sites]
    w = max(i.width for i in ims); h = max(i.height for i in ims)
    rows = (len(ims) + cols - 1) // cols
    out = Image.new("RGB", (cols * (w + pad) + pad, rows * (h + pad) + pad), bg)
    for i, im in enumerate(ims):
        out.paste(im, (pad + (i % cols) * (w + pad) + (w - im.width) // 2, pad + (i // cols) * (h + pad)))
    return out


if __name__ == "__main__":
    import sys
    size = int(sys.argv[1]) if len(sys.argv) > 1 else 2
    seeds = [int(a) for a in sys.argv[3:]] or list(range(1, 13))
    sites = []
    for seed in seeds:
        s = S.generate(seed, size)
        if s:
            sites.append((s, "%s seed %d (try %d)" % (S.SIZE_NAME[size], seed, s.attempt + 1)))
    sheet(sites, 4, 30).save(sys.argv[2] if len(sys.argv) > 2 else "sheet.png")
