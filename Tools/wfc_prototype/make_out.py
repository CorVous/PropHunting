import sys
from PIL import Image, ImageDraw
import sitegen as S, render as R

out = sys.argv[1]
# 1. sheets: per size
picks = {0: [1, 2, 3, 4, 5, 6], 1: [1, 2, 3, 4, 5, 6, 7, 8, 9], 2: [1, 2, 3, 4, 5, 6, 7, 8, 9], 3: [1, 2, 3, 4, 5, 6, 7, 8, 9]}
for size, seeds in picks.items():
    sites = []
    for seed in seeds:
        s = S.generate(seed, size)
        sites.append((s, "%s  seed %d" % (S.SIZE_NAME[size], seed)))
    R.sheet(sites, 3, [40, 36, 32, 28][size]).save("%s/wfc_maps_%s.png" % (out, S.SIZE_NAME[size]))

# 2. the collapse as a film
def frame(s, wave, px):
    n = s.Gn
    im = Image.new("RGB", (n * px, n * px), R.PAPER)
    dr = ImageDraw.Draw(im)
    for c in range(n * n):
        x, z = c % n, c // n
        x0, y0 = x * px, (n - 1 - z) * px
        m = wave[c]
        if m & (m - 1) == 0:
            t = m.bit_length() - 1
            if t == S.O:
                col = R.PAPER
            elif t == S.W:
                col = R.WOOD
            elif t == S.G:
                col = R.GREEN
            elif S.is_lane(t):
                col = R.VERGE
            else:
                k = S.plot_k(t)
                col = tuple(v - k * 9 for v in R.YARD)
            dr.rectangle([x0, y0, x0 + px, y0 + px], fill=col)
            if S.is_lane(t):
                mm = S.lane_mask(t); a = px // 5; x1, y1 = x0 + px, y0 + px
                dr.rectangle([x0 + a, y0 + a, x1 - a, y1 - a], fill=R.LANE)
                if mm & 1: dr.rectangle([x0 + a, y0, x1 - a, y0 + a], fill=R.LANE)
                if mm & 2: dr.rectangle([x1 - a, y0 + a, x1, y1 - a], fill=R.LANE)
                if mm & 4: dr.rectangle([x0 + a, y1 - a, x1 - a, y1], fill=R.LANE)
                if mm & 8: dr.rectangle([x0, y0 + a, x0 + a, y1 - a], fill=R.LANE)
            elif S.is_plot(t):
                d = S.plot_d(t); cxp, cyp = x0 + px // 2, y0 + px // 2
                ex, ey = cxp + S.DX[d] * px // 3, cyp - S.DZ[d] * px // 3
                dr.line([cxp, cyp, ex, ey], fill=(120, 150, 90), width=2)
        else:
            cnt = bin(m).count("1")
            lane = bool(m & S.LANES)
            plot = bool(m >> S.P0)
            g = 215 - cnt * 2
            col = (g, g, g + 8)
            if lane and not plot: col = (228, 206, 170)
            elif plot and not lane: col = (g - 10, g + 6, g - 14)
            dr.rectangle([x0, y0, x0 + px, y0 + px], fill=col)
            dr.rectangle([x0, y0, x0 + px, y0 + px], outline=(190, 190, 196))
            if cnt <= 9:
                dr.text((x0 + px // 2 - 3, y0 + px // 2 - 5), str(cnt), fill=(90, 90, 100))
    return im

for seed, size in ((5, 2), (3, 3)):
    s = None
    for attempt in range(12):
        s = S.Site(seed, size, attempt, record=True)
        if s.run():
            break
    px = 34 if size == 2 else 28
    frames = [frame(s, w, px) for w in s.frames[::2]] + [frame(s, s.frames[-1], px)]
    final = R.draw_site(s, px)
    frames += [final]
    dur = [70] * (len(frames) - 2) + [900, 3500]
    frames = [f.convert("P", palette=Image.ADAPTIVE, colors=48) for f in frames]
    frames[0].save("%s/wfc_collapse_%s.gif" % (out, S.SIZE_NAME[size]), save_all=True, append_images=frames[1:], duration=dur, loop=0, optimize=True)
    print(len(frames))
