"""The lobby's how-to-play wall animations (Cor: pictographic only, no text; the meeple icons; the lobby's board game
look): hide (aim at a prop, turn into it), hunt (a plain prop wears the weapon, a hider loses a heart), the final stand
(hiders shoot the weapons apart). Writes one flipbook atlas each (COLS x ROWS cells, read left to right, top to
bottom, transparent outside the card) for the PropHunt/Flipbook shader, and with --gif a preview gif each.
usage (from the project root): python Tools/make_howto.py [--gif <dir>]
   or: make_howto.py <icons dir> <WeaponIcons.png> <out dir> [--gif <dir>]"""
import math, sys
from PIL import Image, ImageDraw

args = sys.argv[1:]
GIF = None
if "--gif" in args: i = args.index("--gif"); GIF = args[i + 1]; del args[i:i + 2]
ICONS, WEAPONS, OUT = args if len(args) == 3 else ("Assets/Lobby/Icons", "Assets/Lobby/WeaponIcons.png", "Assets/Lobby/HowTo")
COLS, ROWS, CELL_W, CELL_H = 8, 8, 512, 320
W, H, S = 480, 300, 2
FELT = (35, 67, 65); CARD = (233, 218, 182); CARD_LIP = (196, 180, 142); DASH = (247, 238, 212)
BLUE = (155, 190, 229); YELLOW = (242, 196, 90); GREEN = (155, 209, 138); SALMON = (241, 154, 138)
RED = (200, 48, 48); WHITE = (255, 255, 255); LASER = (255, 244, 170); WOOD = (150, 92, 58)
GY = 228   # the ground line
TILE = (18, 22, 462, 268)


def lerp(a, b, t): return a + (b - a) * max(0.0, min(1.0, t))
def ease(t): t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)
def seg(f, a, b): return (f - a) / float(b - a)
def dark(c, k=.8): return tuple(int(v * k) for v in c)
def hop(f, a, b, h=10): return -h * math.sin(math.pi * seg(f, a, b)) if a <= f <= b else 0


def trim(im): return im.crop(im.getbbox())
def icon(name): return trim(Image.open(f"{ICONS}/{name}.png").convert("RGBA"))
def cell(r, c):
    return Image.open(WEAPONS).convert("RGBA").crop((c * 128, r * 128, c * 128 + 128, r * 128 + 128))

HIDER, HUNTER, CRATE, GLASS = icon("meeple_hide"), icon("meeple_hunt"), icon("crate"), icon("hourglass")
CLUB = trim(cell(2, 3))                                             # upright, handle down
GUN = trim(cell(6, 1).rotate(-24, resample=Image.BICUBIC, expand=True))   # levelled, barrel to the right


class Frame:
    def __init__(self, tile):
        self.im = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))
        self.tile = tile
        d = ImageDraw.Draw(self.im)
        x0, y0, x1, y1 = [v * S for v in (8, 8, W - 8, H - 12)]
        d.rounded_rectangle([x0, y0 + 6 * S, x1, y1 + 6 * S], 18 * S, fill=CARD_LIP)
        d.rounded_rectangle([x0, y0, x1, y1], 18 * S, fill=CARD)
        x0, y0, x1, y1 = [v * S for v in TILE]
        d.rounded_rectangle([x0, y0 + 4 * S, x1, y1 + 4 * S], 12 * S, fill=dark(tile, .72))
        d.rounded_rectangle([x0, y0, x1, y1], 12 * S, fill=tile)
        x = 40
        while x < W - 40:   # the board's dotted line, as the ground
            d.rounded_rectangle([x * S, (GY + 3) * S, (x + 7) * S, (GY + 7) * S], 2 * S, fill=DASH)
            x += 13

    def draw(self, fn, alpha=1.0):
        if alpha <= 0.01: return
        l = Image.new("RGBA", self.im.size, (0, 0, 0, 0))
        fn(ImageDraw.Draw(l))
        if alpha < 1: l.putalpha(l.split()[3].point(lambda v: int(v * alpha)))
        self.im = Image.alpha_composite(self.im, l)

    def shadow(self, cx, w, alpha=1.0, y=GY + 5):
        self.draw(lambda d: d.ellipse([(cx - w / 2) * S, (y - w * .11) * S, (cx + w / 2) * S, (y + w * .11) * S],
                                      fill=dark(self.tile, .62) + (150,)), alpha)

    def put(self, img, cx, base, h, alpha=1.0, sx=1.0, sy=1.0, rot=0.0, flip=False, white=0.0):
        """an icon, standing on `base`, `h` high; sx/sy squash it about its foot"""
        if alpha <= 0.01 or sx <= 0.02 or sy <= 0.02: return
        k = h / float(img.height)
        w2, h2 = max(1, int(img.width * k * sx * S)), max(1, int(h * sy * S))
        t = img.resize((w2, h2), Image.LANCZOS)
        if flip: t = t.transpose(Image.FLIP_LEFT_RIGHT)
        if white > 0:
            wimg = Image.new("RGBA", t.size, WHITE + (0,)); wimg.putalpha(t.split()[3])
            t = Image.blend(t, wimg, min(1.0, white))
        if rot: t = t.rotate(rot, resample=Image.BICUBIC, expand=True)
        if alpha < 1: t.putalpha(t.split()[3].point(lambda v: int(v * alpha)))
        self.im.alpha_composite(t, (max(0, int(cx * S - t.width / 2)), max(0, int(base * S - (t.height + h2) / 2 if rot else base * S - t.height))))

    def club(self, hx, hy, ang, length=62, frac=1.0, alpha=1.0):
        """the hunter's club from the hand, `ang` degrees clockwise from upright; returns its tip"""
        k = length / float(CLUB.height)
        t = CLUB.resize((max(1, int(CLUB.width * k * S)), int(length * S)), Image.LANCZOS)
        if frac < 1:
            cut = int(t.height * (1 - frac))
            t.paste((0, 0, 0, 0), (0, 0, t.width, cut))
        R = int(length * S) + 4
        c = Image.new("RGBA", (2 * R, 2 * R), (0, 0, 0, 0))
        c.alpha_composite(t, (R - t.width // 2, R - t.height + 6 * S))
        c = c.rotate(-ang, resample=Image.BICUBIC)
        if alpha < 1: c.putalpha(c.split()[3].point(lambda v: int(v * alpha)))
        dst = (int(hx * S) - R, int(hy * S) - R)
        self.im.alpha_composite(c, (max(0, dst[0]), max(0, dst[1])), (max(0, -dst[0]), max(0, -dst[1])))
        a = math.radians(ang)
        return hx + math.sin(a) * length * frac * .92, hy - math.cos(a) * length * frac * .92

    def out(self): return self.im


def line(d, pts, col, w):
    d.line([(x * S, y * S) for x, y in pts], fill=col, width=int(w * S))
    for x, y in (pts[0], pts[-1]): d.ellipse([(x - w / 2) * S, (y - w / 2) * S, (x + w / 2) * S, (y + w / 2) * S], fill=col)


def poof(fr, cx, cy, t, r=34):
    """a puff of cloud and sparkles, t 0..1"""
    def fn(d):
        for i in range(7):
            a = i * 2 * math.pi / 7 + .4
            rr = r * (.35 + .75 * t)
            x, y, q = cx + math.cos(a) * rr, cy + math.sin(a) * rr * .8, r * .42 * (1 - .5 * t)
            d.ellipse([(x - q) * S, (y - q) * S, (x + q) * S, (y + q) * S], fill=WHITE)
        d.ellipse([(cx - r * .5) * S, (cy - r * .5) * S, (cx + r * .5) * S, (cy + r * .5) * S], fill=WHITE)
    fr.draw(fn, (1 - t) ** .7)
    def stars(d):
        for i in range(5):
            a = i * 2 * math.pi / 5 - .6
            x, y, q = cx + math.cos(a) * r * (1 + .7 * t), cy + math.sin(a) * r * (1 + .7 * t), 6 * (1 - t) + 2
            d.polygon([((x + dx * q) * S, (y + dy * q) * S) for dx, dy in ((0, -1), (.28, -.28), (1, 0), (.28, .28), (0, 1), (-.28, .28), (-1, 0), (-.28, -.28))], fill=DASH)
    fr.draw(stars, 1 - t)


def burst(fr, x, y, t, col=WHITE, r=20, n=8):
    def fn(d):
        for i in range(n):
            a = i * 2 * math.pi / n + .3
            line(d, [(x + math.cos(a) * r * (.4 + .5 * t), y + math.sin(a) * r * (.4 + .5 * t)),
                     (x + math.cos(a) * r * (.9 + .6 * t), y + math.sin(a) * r * (.9 + .6 * t))], col, 4)
    fr.draw(fn, 1 - t)


def chips(fr, x, y, t, n=5, seed=0.0):
    """pieces flying off the weapon"""
    def fn(d):
        for i in range(n):
            a = -2.6 + i * (2.2 / max(1, n - 1)) + seed
            dist = 6 + 44 * t
            cx, cy = x + math.cos(a) * dist, y + math.sin(a) * dist + 46 * t * t
            r = a * 3 + t * 10
            line(d, [(cx - math.cos(r) * 4, cy - math.sin(r) * 4), (cx + math.cos(r) * 4, cy + math.sin(r) * 4)], dark(WOOD, .55), 7)
            line(d, [(cx - math.cos(r) * 4, cy - math.sin(r) * 4), (cx + math.cos(r) * 4, cy + math.sin(r) * 4)], WOOD, 4)
    fr.draw(fn, 1 - max(0, t - .6) / .4)


def heart(d, x, y, r, col):
    d.ellipse([(x - r) * S, (y - r) * S, x * S, y * S], fill=col)
    d.ellipse([x * S, (y - r) * S, (x + r) * S, y * S], fill=col)
    d.polygon([((x - r * .93) * S, (y - r * .3) * S), ((x + r * .93) * S, (y - r * .3) * S), (x * S, (y + r * .95) * S)], fill=col)


def tag(fr, cx, y, alpha=1.0, hearts=None, lost=-1, pop=0.0):
    """the little marker over a prop that is a player: a meeple on a cream chip, hearts beside it"""
    wdt = 34 if hearts is None else 34 + 17 * hearts
    def fn(d):
        d.rounded_rectangle([(cx - wdt / 2) * S, (y - 15 + 3) * S, (cx + wdt / 2) * S, (y + 15 + 3) * S], 9 * S, fill=CARD_LIP)
        d.rounded_rectangle([(cx - wdt / 2) * S, (y - 15) * S, (cx + wdt / 2) * S, (y + 15) * S], 9 * S, fill=CARD)
        if hearts is not None:
            for i in range(hearts):
                hx = cx - wdt / 2 + 38 + i * 17
                gone = lost >= 0 and i >= lost
                r = 6.5 * (1 + pop if i == lost else 1)
                heart(d, hx, y + 1, r, CARD_LIP if gone and not (i == lost and pop > 0) else (WHITE if i == lost and pop > 0 else RED))
    fr.draw(fn, alpha)
    fr.put(HIDER, cx - wdt / 2 + 17, y + 12, 24, alpha)


def cross(fr, x, y, t):
    k = min(1.0, t * 4) * (1.15 - .15 * min(1.0, t * 4))
    def fn(d):
        r = 13 * k
        d.ellipse([(x - r) * S, (y - r + 2) * S, (x + r) * S, (y + r + 2) * S], fill=CARD_LIP)
        d.ellipse([(x - r) * S, (y - r) * S, (x + r) * S, (y + r) * S], fill=CARD)
        q = 5.5 * k
        line(d, [(x - q, y - q), (x + q, y + q)], RED, 4.5 * k)
        line(d, [(x + q, y - q), (x - q, y + q)], RED, 4.5 * k)
    fr.draw(fn, 1 - max(0, t - .75) / .25)


MH, CH = 92, 74   # a meeple's and a crate's height on the card
def hand(cx, base, lean=0): return cx + 30 + lean, base - MH * .50


# ---------------------------------------------------------------- hide: aim at a prop, turn into it
def hide(f):
    fr = Frame(BLUE)
    px, tx = 120, 360                     # the player, the prop aimed at
    aim = 8 <= f < 24
    morph = seg(f, 24, 32)                # the player turns into the prop where they stand
    walk = ease(seg(f, 42, 62))
    x = lerp(px, 245, walk)
    y = GY + hop(f, 42, 48) + hop(f, 49, 55) + hop(f, 56, 62) + hop(f, 33, 38, 6)
    fr.shadow(tx, 78); fr.shadow(x, lerp(60, 78, ease(morph)))
    if aim or 24 <= f < 28:   # the picked prop lights up
        pulse = .65 + .35 * math.sin(f * 1.2)
        fr.draw(lambda d: d.rounded_rectangle([(tx - 46) * S, (GY - CH - 8) * S, (tx + 46) * S, (GY + 8) * S], 14 * S, outline=LASER, width=5 * S), pulse)
    fr.put(CRATE, tx, GY, CH)
    if morph < 1:
        m = ease(morph)
        gx, gy = hand(px, GY)
        up = ease(seg(f, 3, 8))
        fr.put(HIDER, px, GY, MH, alpha=1 - m, sx=1 + .25 * m, sy=1 - .2 * m, white=m)
        if morph <= 0:
            fr.put(GUN, gx + 16, gy + lerp(22, 4, up), 26)
            if aim:
                fr.draw(lambda d: line(d, [(gx + 44, gy - 12), (tx - 34, GY - CH * .55)], LASER, 3.5))
                fr.draw(lambda d: d.ellipse([(tx - 40) * S, (GY - CH * .55 - 6) * S, (tx - 28) * S, (GY - CH * .55 + 6) * S], fill=LASER))
    if morph > 0:
        m = ease(morph)
        fr.put(CRATE, x, y, CH, alpha=min(1, m * 1.6), sx=lerp(.7, 1, m), sy=lerp(.6, 1, m) * (1 + .08 * math.sin(math.pi * seg(f, 32, 38)) if 32 <= f <= 38 else lerp(.6, 1, m)) if False else lerp(.6, 1, m), white=1 - m)
    if 24 <= f < 36: poof(fr, px, GY - 42, seg(f, 24, 36), r=40)
    if f >= 32: tag(fr, x, y - CH - 22, ease(seg(f, 32, 37)))
    return fr.out()


# ---------------------------------------------------------------- hunt: a plain prop wears the weapon, a hider is hit
def hunt(f):
    fr = Frame(YELLOW)
    ax, bx = 205, 385                     # a plain prop, a prop that is a player
    h1, h2 = 17, 47
    if f < 26: mx = lerp(70, 132, ease(seg(f, 0, 8))); my = GY + hop(f, 0, 4, 7) + hop(f, 4, 8, 7)
    else: mx = lerp(132, 310, ease(seg(f, 26, 38))); my = GY + hop(f, 26, 30, 9) + hop(f, 30, 34, 9) + hop(f, 34, 38, 9)
    def swing(h):   # wind up, strike, come back
        if f < h - 9: return 28
        if f < h - 3: return lerp(28, -55, ease(seg(f, h - 9, h - 3)))
        if f < h: return lerp(-55, 105, seg(f, h - 3, h))
        return lerp(105, 28, ease(seg(f, h + 5, h + 13)))
    ang = swing(h1) if f < 30 else swing(h2)
    lean = 6 * math.sin(math.radians(max(0, ang))) if ang > 28 else -3 * (28 - ang) / 83.0
    ja = 7 * math.sin((f - h1) * 1.9) * (1 - seg(f, h1, h1 + 10)) if h1 <= f < h1 + 10 else 0
    jb = 6 * math.sin((f - h2) * 2.2) * (1 - seg(f, h2, h2 + 9)) if h2 <= f < h2 + 9 else 0
    push = 10 * ease(seg(f, h1, h1 + 4))
    fr.shadow(ax + push, 78); fr.shadow(bx, 78); fr.shadow(mx, 60)
    fr.put(CRATE, ax + push + ja * .4, GY, CH, rot=-ja * .8)
    flash = max(0.0, 1 - seg(f, h2, h2 + 5)) if f >= h2 else 0
    fr.put(CRATE, bx + jb, GY, CH, white=flash * .9)
    if f >= h2:   # the player inside shows through for a moment
        fr.put(HIDER, bx + jb, GY - 4, MH * .8, alpha=.85 * math.sin(math.pi * min(1, seg(f, h2, h2 + 12))))
    fr.put(HUNTER, mx + lean, my, MH, rot=-lean * .8)
    hx, hy = hand(mx, my, lean)
    worn = .82 if f >= h1 else 1.0
    tip = fr.club(hx, hy, ang, frac=worn)
    if h1 <= f < h1 + 5: burst(fr, ax - 30, GY - 46, seg(f, h1, h1 + 5), col=DASH)
    if h1 <= f < h1 + 14: chips(fr, ax - 34, GY - 52, seg(f, h1, h1 + 14))
    if h1 + 2 <= f < h1 + 22: cross(fr, ax + push, GY - CH - 24, seg(f, h1 + 2, h1 + 22))
    if h2 <= f < h2 + 6: burst(fr, bx - 30, GY - 46, seg(f, h2, h2 + 6), col=WHITE, r=26, n=10)
    if f >= h2 + 1:
        t = seg(f, h2 + 1, h2 + 6)
        tag(fr, bx, GY - CH - 24, ease(t), hearts=3, lost=2, pop=max(0, 1 - t) if t < 1 else 0)
    return fr.out()


# ---------------------------------------------------------------- the final stand: the hiders shoot the weapons
def stand(f):
    fr = Frame(SALMON)
    mx, cx = 130, 355
    s1, s2 = 20, 40
    # the hourglass, running out
    beat = .5 + .5 * math.sin(f * .9)
    fr.draw(lambda d: d.ellipse([(240 - 30) * S, (64 - 30 + 3) * S, (240 + 30) * S, (64 + 30 + 3) * S], fill=CARD_LIP))
    fr.draw(lambda d: d.ellipse([(240 - 30) * S, (64 - 30) * S, (240 + 30) * S, (64 + 30) * S], fill=CARD))
    fr.draw(lambda d: d.ellipse([(240 - 30) * S, (64 - 30) * S, (240 + 30) * S, (64 + 30) * S], outline=RED, width=int(4 * S)), .35 + .65 * beat)
    fr.put(GLASS, 240, 88, 48, rot=6 * math.sin(f * .9))
    ghost = ease(seg(f, s2 + 3, s2 + 14))
    my = GY - 10 * ghost
    fr.shadow(cx, 78); fr.shadow(mx, 60, 1 - .7 * ghost)
    up = ease(seg(f, 5, 12))
    kick = 5 * max(0, 1 - seg(f, s1, s1 + 4)) if f >= s1 and f < s2 else (5 * max(0, 1 - seg(f, s2, s2 + 4)) if f >= s2 else 0)
    gyy = GY - CH + 36 - 26 * up
    fr.put(GUN, cx - 8 + kick, gyy, 30, alpha=min(1, up * 2), flip=True)
    fr.put(CRATE, cx, GY, CH)
    tag(fr, cx + 56, GY - CH - 26, 1.0)
    fr.put(HUNTER, mx, my, MH, alpha=1 - .45 * ghost, white=.8 * ghost)
    hx, hy = hand(mx, my)
    frac = 1.0 if f < s1 else (.72 if f < s2 else .3)
    tip = fr.club(hx, hy, 24, frac=frac, alpha=1 - .6 * ghost)
    full = (hx + math.sin(math.radians(24)) * 62 * .92, hy - math.cos(math.radians(24)) * 62 * .92)
    mzx, mzy = cx - 50, gyy - 17
    for s, n, sd, at in ((s1, 4, 0.0, full), (s2, 8, .5, tip if f < s2 else (hx + 14, hy - 30))):
        if s <= f < s + 4:
            fr.draw(lambda d, at=at: line(d, [(mzx, mzy), at], LASER, 6), 1 - .7 * seg(f, s, s + 4))
            burst(fr, mzx, mzy, seg(f, s, s + 4), col=WHITE, r=12, n=6)
            burst(fr, at[0], at[1], seg(f, s, s + 4), col=WHITE, r=18)
        if s <= f < s + 16: chips(fr, at[0], at[1], seg(f, s, s + 16), n=n, seed=sd)
    return fr.out()


import os
os.makedirs(OUT, exist_ok=True)
for name, fn, n in (("hide", hide, 76), ("hunt", hunt, 78), ("final_stand", stand, 72)):
    cells = COLS * ROWS
    atlas = Image.new("RGBA", (COLS * CELL_W, ROWS * CELL_H), (0, 0, 0, 0))
    gif = []
    for i in range(cells):
        im = fn(i * n / float(cells))
        atlas.paste(im.resize((CELL_W, CELL_H), Image.LANCZOS), ((i % COLS) * CELL_W, (i // COLS) * CELL_H))
        if GIF:
            bg = Image.new("RGBA", im.size, FELT + (255,)); bg.alpha_composite(im)
            gif.append(bg.resize((W, H), Image.LANCZOS).convert("RGB"))
    atlas.save(f"{OUT}/howto_{name}.png")
    if GIF: gif[0].save(f"{GIF}/wall_{name}.gif", save_all=True, append_images=gif[1:], duration=int(70 * n / cells), loop=0)
    print(name, n * 0.07, "s")
