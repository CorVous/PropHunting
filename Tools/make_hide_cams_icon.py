"""The HIDE CAMERAS switch's pictures (host panel, experimental): a camera; while the switch is on, in a red
crossed-out circle (as NO PLACING). Writes Assets/Lobby/Icons/hide_cams_on.png and hide_cams_off.png.
    python Tools/make_hide_cams_icon.py [out_dir]
"""
import os, sys
from PIL import Image, ImageDraw

S = 1024
OUT = (38, 48, 82, 255)          # the icons' dark outline
BODY = (233, 218, 182, 255)      # cream, as the cards
LIP = (196, 180, 142, 255)
LENS = (127, 179, 230, 255)      # the panel's blue
RED = (214, 64, 54, 255)


def camera(d):
    # the body, a viewfinder hump and a shutter button, outlined
    d.rounded_rectangle((330, 250, 560, 380), radius=40, fill=OUT)
    d.rounded_rectangle((360, 280, 530, 380), radius=24, fill=LIP)
    d.rounded_rectangle((690, 290, 800, 370), radius=24, fill=OUT)
    d.rounded_rectangle((170, 330, 854, 800), radius=90, fill=OUT)
    d.rounded_rectangle((204, 364, 820, 766), radius=62, fill=BODY)
    d.rounded_rectangle((204, 690, 820, 766), radius=40, fill=LIP)
    # the lens
    d.ellipse((352, 400, 672, 720), fill=OUT)
    d.ellipse((386, 434, 638, 686), fill=LENS)
    d.ellipse((440, 488, 584, 632), fill=OUT)
    d.ellipse((462, 472, 522, 532), fill=(255, 255, 255, 255))
    # the recording light
    d.ellipse((716, 410, 776, 470), fill=RED)


def make(on):
    im = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    camera(d)
    if on:
        ring = Image.new('RGBA', (S, S), (0, 0, 0, 0))
        r = ImageDraw.Draw(ring)
        r.ellipse((40, 40, S - 40, S - 40), outline=RED, width=84)
        r.line((218, 218, S - 218, S - 218), fill=RED, width=84)
        im.alpha_composite(ring)
    return im


if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Lobby', 'Icons')
    make(True).save(os.path.join(out, 'hide_cams_on.png'))
    make(False).save(os.path.join(out, 'hide_cams_off.png'))
