"""Draws two 256 px icons for the lobby's pictographic plaques (LobbyBuilder.Plaques), in the flat-shaded look of the
other icons in Assets/Lobby/Icons (lit faces a shade lighter on the left, darker on the right):
  trophy.png  a gold cup on a stand (the Trophies board)
  crown.png   a gold crown (the host panel)
Run from the project root: python Tools/make_plaque_icons.py (needs Pillow)."""
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Lobby", "Icons")
S = 4                      # supersampling
N = 256 * S
LIGHT, MID, DARK, EDGE = (255, 214, 102), (242, 186, 64), (205, 142, 36), (150, 98, 22)


def P(pts):
    return [(x * S, y * S) for x, y in pts]


def finish(im, name):
    im = im.resize((256, 256), Image.LANCZOS)
    im.save(os.path.join(OUT, name))
    print("wrote", name)


def trophy():
    im = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    w = 6 * S
    # handles (rings either side of the cup)
    d.ellipse(P([(34, 58), (92, 128)]), outline=EDGE, width=16 * S)
    d.ellipse(P([(164, 58), (222, 128)]), outline=EDGE, width=16 * S)
    d.ellipse(P([(38, 62), (88, 124)]), outline=MID, width=9 * S)
    d.ellipse(P([(168, 62), (218, 124)]), outline=DARK, width=9 * S)
    # the cup: a bowl, light left half, darker right half
    cup = [(66, 40), (190, 40), (186, 96), (170, 128), (146, 146), (110, 146), (86, 128), (70, 96)]
    d.polygon(P(cup), fill=MID, outline=EDGE, width=w)
    d.polygon(P([(70, 46), (128, 46), (128, 140), (110, 140), (90, 124), (76, 96)]), fill=LIGHT)
    d.polygon(P([(128, 46), (184, 46), (180, 96), (166, 124), (146, 140), (128, 140)]), fill=DARK)
    d.line(P([(66, 40), (190, 40)]), fill=EDGE, width=w)
    d.polygon(P(cup), outline=EDGE, width=w)
    # the stem, the knob and the base
    d.polygon(P([(116, 146), (140, 146), (136, 182), (120, 182)]), fill=DARK, outline=EDGE, width=w)
    d.rounded_rectangle(P([(92, 180), (164, 200)]), radius=6 * S, fill=MID, outline=EDGE, width=w)
    d.rounded_rectangle(P([(76, 198), (180, 226)]), radius=6 * S, fill=DARK, outline=EDGE, width=w)
    d.rectangle(P([(82, 204), (128, 220)]), fill=MID)
    # a star on the cup
    import math
    cx, cy, r1, r2 = 128, 88, 24, 10
    star = [(cx + (r1 if i % 2 == 0 else r2) * math.sin(i * math.pi / 5), cy - (r1 if i % 2 == 0 else r2) * math.cos(i * math.pi / 5)) for i in range(10)]
    d.polygon(P(star), fill=(255, 244, 200), outline=EDGE, width=3 * S)
    finish(im, "trophy.png")


def crown():
    im = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    w = 6 * S
    body = [(34, 186), (28, 80), (80, 132), (128, 52), (176, 132), (228, 80), (222, 186)]
    d.polygon(P(body), fill=MID, outline=EDGE, width=w)
    d.polygon(P([(36, 180), (32, 92), (80, 140), (128, 64), (128, 180)]), fill=LIGHT)
    d.polygon(P([(128, 64), (176, 140), (224, 92), (220, 180), (128, 180)]), fill=DARK)
    d.polygon(P(body), outline=EDGE, width=w)
    d.rounded_rectangle(P([(30, 180), (226, 214)]), radius=8 * S, fill=DARK, outline=EDGE, width=w)
    d.rectangle(P([(38, 188), (128, 206)]), fill=MID)
    for x, y in [(28, 80), (128, 52), (228, 80)]:
        d.ellipse(P([(x - 14, y - 14), (x + 14, y + 14)]), fill=LIGHT, outline=EDGE, width=w)
    for x, c in [(78, (231, 84, 72)), (128, (127, 179, 230)), (178, (155, 209, 138))]:
        d.ellipse(P([(x - 11, 186), (x + 11, 208)]), fill=c, outline=EDGE, width=3 * S)
    finish(im, "crown.png")


if __name__ == "__main__":
    trophy()
    crown()
