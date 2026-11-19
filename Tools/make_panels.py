"""Flat board-game detailing for the two wall strips across the lobby's east shelf window.

Outputs (Assets/Lobby/Panels; the preview to the temp folder):
  bottom_strip.png  2048 x 256  opaque, tiles horizontally, 4.0 m x 0.5 m  at 512 px/m
  top_trim.png      2048 x 128  RGBA,   tiles horizontally, 4.0 m x 0.25 m at 512 px/m
  preview.png       both tiled over 12 m of wall with a grey stand-in for the shelf window
"""
import math
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'Assets', 'Lobby', 'Panels')
SS = 4  # supersampling factor for crisp antialiased edges

WALL = (0x1D, 0x4A, 0x47)
CREAM = (0xF3, 0xE7, 0xC9)
TAN = (0xE1, 0xCF, 0xA5)
TRIM = (0xC9, 0xA4, 0x6A)
BLUE = (0x7F, 0xB3, 0xE6)
YELLOW = (0xF2, 0xC4, 0x5A)
GREEN = (0x9B, 0xD1, 0x8A)
CORAL = (0xF1, 0x9A, 0x8A)
RED = (0xD8, 0x47, 0x3A)
SPACES = [BLUE, YELLOW, GREEN, CORAL]


def s(v):
    return int(round(v * SS))


def down(img, size):
    """Downsample; premultiply alpha first so transparent edges do not darken."""
    if img.mode == "RGBA":
        return img.convert("RGBa").resize(size, Image.BOX).convert("RGBA")
    return img.resize(size, Image.BOX)  # exact 4x box average: no ringing on flat colour


def make_bottom():
    W, H = 2048, 256
    img = Image.new("RGB", (W * SS, H * SS), WALL)
    d = ImageDraw.Draw(img)

    # slim cream border lines, full width so they tile
    for y in (26, 224):
        d.rectangle([0, s(y), W * SS, s(y + 6) - 1], fill=CREAM)

    cy = 128

    def wrapped(fn, x):
        for dx in (-W, 0, W):
            fn(x + dx)

    def space(col):
        def f(x):
            r = 40
            d.rounded_rectangle([s(x - r), s(cy - r), s(x + r), s(cy + r)], radius=s(14), fill=col)
        return f

    def dot(x):
        r = 9
        d.ellipse([s(x - r), s(cy - r), s(x + r), s(cy + r)], fill=CREAM)

    def chevron(x):
        # small arrow pointing up toward the shelves
        w, h, t = 26, 22, 12
        pts = [(x - w, cy + h / 2), (x, cy - h / 2), (x + w, cy + h / 2)]
        d.line([(s(px), s(py)) for px, py in pts], fill=TRIM, width=s(t), joint="curve")
        for px, py in (pts[0], pts[2]):
            d.ellipse([s(px - t / 2), s(py - t / 2), s(px + t / 2), s(py + t / 2)], fill=TRIM)

    # one coloured space per metre; dot, up-arrow, dot between spaces
    for i in range(4):
        base = 128 + 512 * i
        wrapped(space(SPACES[i]), base)
        wrapped(dot, base + 128)
        wrapped(chevron, base + 256)
        wrapped(dot, base + 384)

    out = down(img, (W, H))
    out.save(os.path.join(OUT, "bottom_strip.png"))
    return out


def make_top():
    W, H = 2048, 128
    img = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    rail = 12          # rail along the top edge the pennants hang from
    pitch = 256        # 8 pennants per tile, 2 colour cycles: sparse on purpose
    hw, top, tip = 58, rail, 132
    tr = 10            # tip rounding radius (pennant ends about 108 px down)

    for i in range(W // pitch):
        cx = pitch * i + pitch / 2
        col = SPACES[i % 4]
        h = tip - top
        hyp = math.hypot(hw, h)
        sin_a = hw / hyp                     # half angle at the tip
        ccy = tip - tr / sin_a               # centre of the tip circle
        # tangent points of the tip circle on the two sides
        tx = tr * (h / hyp)
        ty = ccy + tr * sin_a
        poly = [(cx - hw, top), (cx + hw, top), (cx + tx, ty), (cx - tx, ty)]
        d.polygon([(s(px), s(py)) for px, py in poly], fill=col + (255,))
        d.ellipse([s(cx - tr), s(ccy - tr), s(cx + tr), s(ccy + tr)], fill=col + (255,))
        # small cream bead on the rail between pennants (wraps across the seam)
        bx, by, br = pitch * i, rail + 13, 9
        for dx in (0, W):
            d.ellipse([s(bx + dx - br), s(by - br), s(bx + dx + br), s(by + br)], fill=CREAM + (255,))
    d.rectangle([0, 0, W * SS, s(rail) - 1], fill=CREAM + (255,))
    # thin gold-brown underline so the rail still reads when laid over the cream band
    d.rectangle([0, s(rail), W * SS, s(rail + 4) - 1], fill=TRIM + (255,))

    out = down(img, (W, H))
    out.save(os.path.join(OUT, "top_trim.png"))
    return out


def tile(img, width):
    out = Image.new(img.mode, (width, img.height))
    for x in range(0, width, img.width):
        out.paste(img, (x, 0))
    return out


def make_preview(bottom, top):
    PPM = 128                    # preview pixels per metre
    WM, HM = 12.0, 7.0
    W, H = int(WM * PPM), int(HM * PPM)

    def y(m):                    # metres above floor -> pixel row
        return int(round(H - m * PPM))

    img = Image.new("RGB", (W, H), WALL)
    d = ImageDraw.Draw(img)
    # shelf window stand-in
    d.rectangle([0, y(5.85), W, y(0.5) - 1], fill=(0x8A, 0x8A, 0x8A))
    # existing trim on the top strip: cream band, tan dashes, dark red lid rim
    d.rectangle([0, y(6.6), W, y(5.9) - 1], fill=CREAM)
    for x in range(8, W, 48):
        d.rectangle([x, y(6.6) + 5, x + 24, y(6.6) + 10], fill=TAN)
    d.rectangle([0, y(7.0), W, y(6.76) - 1], fill=(0x9E, 0x2F, 0x28))

    k = PPM / 512.0
    b = tile(bottom, int(WM * 512)).resize((W, int(bottom.height * k)), Image.LANCZOS)
    img.paste(b, (0, y(0.5)))
    t = tile(top, int(WM * 512))
    t = t.convert("RGBa").resize((W, int(top.height * k)), Image.LANCZOS).convert("RGBA")
    img.paste(t, (0, y(5.9)), t)   # rail on the band's lower edge, pennants hang below
    import tempfile; img.save(os.path.join(tempfile.gettempdir(), "panels_preview.png"))


def seam_report(name, img):
    """Tiling check: the wrapped column pair (last, first) must look like any interior neighbour pair."""
    px = img.convert("RGBA").convert("RGBa").load()  # premultiplied, so invisible pixels compare equal
    w, h = img.size

    def diff(x0, x1):
        return max(max(abs(a - b) for a, b in zip(px[x0, yy], px[x1, yy])) for yy in range(h))

    interior = max(diff(x, x + 1) for x in range(w - 1))
    print(f"{name}: {w}x{h} {img.mode}  seam step {diff(w - 1, 0)}  (largest interior step {interior})")


if __name__ == "__main__":
    b = make_bottom()
    t = make_top()
    make_preview(b, t)
    seam_report("bottom_strip.png", b)
    seam_report("top_trim.png", t)
