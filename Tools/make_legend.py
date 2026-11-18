import math, sys
from PIL import Image, ImageDraw, ImageFont
S = 6            # supersample: draw at 6x, save at 2x
OUT = 2
W = (255, 255, 255, 255)
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
CELL, H, PAD = 87, 120, 10          # at 1x, taken from VRChat's own HUD at 1342 px tall

def P(*v): return [x * S for x in v]

def tri(d, pts): d.polygon([(x * S, y * S) for x, y in pts], fill=W)
def line(d, pts, w): d.line([(x * S, y * S) for x, y in pts], fill=W, width=int(w * S), joint="curve")
def dot(d, x, y, r): d.ellipse(P(x - r, y - r, x + r, y + r), fill=W)

def ic_move(d, cx, cy):
    for a in range(4):
        c, s = math.cos(a * math.pi / 2), math.sin(a * math.pi / 2)
        tip, base, hw = 20, 9, 8
        tri(d, [(cx + c * tip, cy + s * tip), (cx + c * base - s * hw, cy + s * base + c * hw), (cx + c * base + s * hw, cy + s * base - c * hw)])
    dot(d, cx, cy, 4)

def ic_run(d, cx, cy):
    for off in (-9, 5):
        line(d, [(cx + off - 5, cy - 13), (cx + off + 8, cy), (cx + off - 5, cy + 13)], 6)

def ic_jump(d, cx, cy):
    line(d, [(cx, cy + 8), (cx, cy - 8)], 6)
    tri(d, [(cx, cy - 21), (cx - 12, cy - 6), (cx + 12, cy - 6)])
    line(d, [(cx - 15, cy + 17), (cx + 15, cy + 17)], 5)

def ic_rot(d, cx, cy, cw):
    r = 14
    # an arc of 270 degrees, open at the top, with a head on one end
    a0, a1 = (-60, 210) if cw else (-30, 240)
    box = P(cx - r, cy - r, cx + r, cy + r)
    d.arc(box, a0 if cw else a0, a1, fill=W, width=int(5.5 * S))
    a = math.radians(a0 if cw else a1)
    ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
    # tangent: clockwise on screen is increasing angle; the head points back along the open end
    t = a - math.pi / 2 if cw else a + math.pi / 2
    tx, ty = math.cos(t), math.sin(t)
    nx, ny = -ty, tx
    tri(d, [(ex + tx * 11, ey + ty * 11), (ex + nx * 9, ey + ny * 9), (ex - nx * 9, ey - ny * 9)])

def ic_become(d, cx, cy):
    # a cube, corner on
    r = 18
    pts = [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a))) for a in (-90, -30, 30, 90, 150, 210)]
    line(d, pts + [pts[0]], 4.5)
    for i in (1, 3, 5): line(d, [(cx, cy), pts[i]], 4.5)
    for p in pts: dot(d, p[0], p[1], 2.2)

def ic_taunt(d, cx, cy):
    x = cx - 4
    tri(d, [(x - 12, cy - 5), (x + 6, cy - 15), (x + 6, cy + 15)])
    tri(d, [(x - 12, cy - 5), (x + 6, cy + 15), (x - 12, cy + 5)])
    d.rounded_rectangle(P(x - 18, cy - 6, x - 10, cy + 6), radius=2 * S, fill=W)
    line(d, [(x - 8, cy + 6), (x - 5, cy + 16)], 5)
    for r in (13, 20):
        d.arc(P(x + 4 - r, cy - r, x + 4 + r, cy + r), -38, 38, fill=W, width=int(3.5 * S))

def ic_shoot(d, cx, cy):
    r = 13
    d.ellipse(P(cx - r, cy - r, cx + r, cy + r), outline=W, width=int(4.5 * S))
    for a in range(4):
        c, s = math.cos(a * math.pi / 2), math.sin(a * math.pi / 2)
        line(d, [(cx + c * 8, cy + s * 8), (cx + c * 21, cy + s * 21)], 4.5)
    dot(d, cx, cy, 2.5)

ICON = {"move": ic_move, "run": ic_run, "jump": ic_jump, "ccw": lambda d, x, y: ic_rot(d, x, y, True), "cw": lambda d, x, y: ic_rot(d, x, y, False),
        "become": ic_become, "taunt": ic_taunt, "shoot": ic_shoot}

def legend(cells, path):
    w = PAD * 2 + CELL * len(cells)
    im = Image.new("RGBA", (w * S, H * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle(P(0, 0, w, H), radius=14 * S, fill=(14, 18, 22, 150))
    f = ImageFont.truetype(FONT, 17 * S)
    for i, (icon, key) in enumerate(cells):
        cx = PAD + CELL * i + CELL / 2
        ICON[icon](d, cx, 43)
        tw = d.textlength(key, font=f) / S
        kw = max(34, tw + 18)
        d.rounded_rectangle(P(cx - kw / 2, 73, cx + kw / 2, 103), radius=6 * S, fill=(12, 16, 28, 235))
        d.text((cx * S, 88 * S), key, font=f, fill=W, anchor="mm")
    im = im.resize((w * OUT, H * OUT), Image.LANCZOS)
    im.save(path)
    print(path, im.size)

legend([("move", "WASD"), ("run", "Shift"), ("jump", "Space"), ("ccw", "Q"), ("cw", "E"), ("become", "Click"), ("taunt", "T")], "LegendHider.png")
legend([("ccw", "Q"), ("cw", "E"), ("taunt", "T")], "LegendHiderShort.png")
legend([("shoot", "Click")], "LegendHunter.png")
