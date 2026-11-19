"""Draws the hider health bar's shield (Assets/Udon/Textures/Shield.png): a plain heater shield, light fill with a dark
outline, so the count of shields over the bar reads by shape and number, not by colour (Cor: colour-blind friendly).
Run: python Tools/make_shield.py (needs Pillow)."""
import os
from PIL import Image, ImageDraw

S = 8                     # supersample
W, H = 48, 56             # saved size
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Udon", "Textures", "Shield.png")

def shield(w, h, inset):
    # flat top with rounded shoulders, straight sides, curving to a point at the bottom
    pts = []
    x0, x1, y0 = inset, w - inset, inset
    r = (x1 - x0) * 0.18
    import math
    for k in range(0, 11):                       # left shoulder
        a = math.pi + k * (math.pi / 2) / 10
        pts.append((x0 + r + r * math.cos(a), y0 + r + r * math.sin(a)))
    for k in range(0, 11):                       # right shoulder
        a = 1.5 * math.pi + k * (math.pi / 2) / 10
        pts.append((x1 - r + r * math.cos(a), y0 + r + r * math.sin(a)))
    side = h * 0.45
    tip = (w / 2, h - inset)
    for k in range(0, 21):                       # right side down and curving in to the tip
        t = k / 20
        pts.append(((1 - t) ** 2 * x1 + 2 * (1 - t) * t * x1 + t * t * tip[0], (1 - t) ** 2 * (y0 + r) + 2 * (1 - t) * t * (y0 + side + (h - side) * 0.35) + t * t * tip[1]))
    for k in range(0, 21):                       # and back up the left
        t = k / 20
        pts.append(((1 - t) ** 2 * tip[0] + 2 * (1 - t) * t * x0 + t * t * x0, (1 - t) ** 2 * tip[1] + 2 * (1 - t) * t * (y0 + side + (h - side) * 0.35) + t * t * (y0 + r)))
    return pts

img = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.polygon([(x * S, y * S) for x, y in shield(W, H, 1.0)], fill=(20, 24, 30, 255))          # the dark outline
d.polygon([(x * S, y * S) for x, y in shield(W, H, 5.0)], fill=(236, 240, 245, 255))      # the light face
# a centre ridge, so the shape reads as a shield and not a drop
d.line([(W / 2 * S, 10 * S), (W / 2 * S, (H - 12) * S)], fill=(20, 24, 30, 255), width=3 * S)
img = img.resize((W, H), Image.LANCZOS)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.save(OUT)
print("wrote", os.path.normpath(OUT), img.size)
