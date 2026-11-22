"""Before and after pictures of two plots: the round 2 rules (items_v2) beside the current ones."""
from PIL import Image, ImageDraw
import sitegen as S, render as R
import items_render as IR, items as I
import items_v2 as V2


def crop_plot(it, im, px, k):
    n = it.n
    cs = [c for c in range(n * n) if it.reg[c] >= 0 and it.regions[it.reg[c]]["key"][0] in ("room", "yard") and it.regions[it.reg[c]]["key"][1] == k]
    xs = [c % n for c in cs]; zs = [c // n for c in cs]
    return im.crop((min(xs) * px - 4, (n - 1 - max(zs)) * px - 4, (max(xs) + 1) * px + 4, (n - min(zs)) * px + 4))


for idx, (size, seed) in enumerate(((2, 5), (3, 3)), 1):
    s = S.generate(seed, size)
    new = I.generate(s)
    IR.I = V2
    old = V2.generate(s)
    old.tops = {c: 1 for c in range(old.n ** 2) if old.reg[c] >= 0 and old.tiles[c] in V2.TWO}
    px = 30
    imo = IR.draw(old, px)
    IR.I = I
    imn = IR.draw(new, px)
    ks = sorted([k for k, p in enumerate(s.plots) if p["plan"] is not None], key=lambda k: -len([1 for r in new.regions if r["key"][0] == "room" and r["key"][1] == k]))[:2]
    rows = [(crop_plot(old, imo, px, k), crop_plot(new, imn, px, k)) for k in ks]
    W = max(a.width + b.width + 30 for a, b in rows); H = sum(max(a.height, b.height) + 50 for a, b in rows)
    out = Image.new("RGB", (W + 20, H + 10), (255, 255, 255))
    dr = ImageDraw.Draw(out); y = 10
    for a, b in rows:
        dr.text((10, y), "Round 2", fill=R.INK, font=R.font(22, True)); dr.text((a.width + 40, y), "Now", fill=R.INK, font=R.font(22, True))
        out.paste(a, (10, y + 32)); out.paste(b, (a.width + 40, y + 32)); y += max(a.height, b.height) + 50
    out.save("out/wfc_rich_before_after_%d.png" % idx)
