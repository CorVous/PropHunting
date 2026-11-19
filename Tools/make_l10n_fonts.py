"""Fallback fonts for the translated text (CJK and Cyrillic; the Kenney fonts hold Latin only).

Takes Google Fonts' variable TTFs (SIL OFL 1.1: Noto Sans JP / KR / SC / TC, Rubik), pins them at Bold and cuts
each down to the characters its language commonly uses, plus every character in Assets/Udon/Text/*.tsv, so new
strings keep working without running this again (unless they use rare characters):

  Noto Sans JP: kana + JIS level-1 kanji      Noto Sans KR: the 2,350 KS X 1001 hangul
  Noto Sans SC: GB 2312 level-1 hanzi         Noto Sans TC: Big5 common hanzi
  Rubik: Cyrillic and Latin Extended-A (Polish, Czech, Hungarian, Turkish, Romanian...)
  Noto Sans: Vietnamese
  Noto Sans Arabic: Arabic with its presentation forms (the bake shapes Arabic into them); Rubik also Hebrew
  Noto Sans Thai: Thai

Usage: python -I Tools/make_l10n_fonts.py <folder with NotoSansJP.ttf, NotoSansKR.ttf, NotoSansSC.ttf,
NotoSansTC.ttf, Rubik.ttf, NotoSans.ttf (the [wght] files from github.com/google/fonts/tree/main/ofl)>
Writes Assets/Lobby/Fonts/L10n/<name>-Bold.ttf. "PropHunt/Bake text table" makes the SDF atlases from them.
"""
import glob, os, sys
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
from fontTools import subset

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Lobby", "Fonts", "L10n")


def table_chars():
    s = set()
    for f in glob.glob(os.path.join(ROOT, "Assets", "Udon", "Text", "*.tsv")):
        for ln in open(f, encoding="utf-8"):
            s.update(ln)
    return s


def dbcs(codec, hi, lo_range):
    s = set()
    for a in hi:
        for b in lo_range:
            try:
                s.update(bytes([a, b]).decode(codec))
            except UnicodeDecodeError:
                pass
    return s


def common(name):
    punct = set(chr(c) for c in range(0x3000, 0x3040)) | set(chr(c) for c in range(0xFF01, 0xFF5F)) | set("・ー…—–")
    if name == "NotoSansJP":
        kana = set(chr(c) for c in range(0x3040, 0x3100))
        return kana | punct | dbcs("euc_jp", range(0xB0, 0xD0), range(0xA1, 0xFF))
    if name == "NotoSansKR":
        return punct | dbcs("euc_kr", range(0xB0, 0xC9), range(0xA1, 0xFF))
    if name == "NotoSansSC":
        return punct | dbcs("gb2312", range(0xB0, 0xD8), range(0xA1, 0xFF))
    if name == "NotoSansTC":
        return punct | dbcs("big5", range(0xA4, 0xC7), list(range(0x40, 0x7F)) + list(range(0xA1, 0xFF)))
    if name == "Rubik":
        return set(chr(c) for c in list(range(0x0400, 0x0460)) + list(range(0x0490, 0x04A0)) + list(range(0x0100, 0x0180)) + list(range(0x0218, 0x021C)) + list(range(0x05D0, 0x05F5))) | set("№«»—–…„“”")
    if name == "NotoSansArabic":
        return set(chr(c) for c in list(range(0x0600, 0x0700)) + list(range(0xFE70, 0xFF00)))
    if name == "NotoSansThai":
        return set(chr(c) for c in range(0x0E00, 0x0E80))
    if name == "NotoSans":
        return set(chr(c) for c in list(range(0x1EA0, 0x1EFA)) + list(range(0x0100, 0x0180)) + [0x01A0, 0x01A1, 0x01AF, 0x01B0, 0x0300, 0x0301, 0x0303, 0x0309, 0x0323])
    return set()


def main(src):
    os.makedirs(OUT, exist_ok=True)
    extra = table_chars()
    for name in ("NotoSansJP", "NotoSansKR", "NotoSansSC", "NotoSansTC", "Rubik", "NotoSans", "NotoSansArabic", "NotoSansThai"):
        f = TTFont(os.path.join(src, name + ".ttf"))
        if "fvar" in f:
            f = instancer.instantiateVariableFont(f, {a.axisTag: (700 if a.axisTag == "wght" else a.defaultValue) for a in f["fvar"].axes})
        cmap = f.getBestCmap()
        want = common(name) | extra
        keep = sorted(ord(c) for c in want if ord(c) in cmap)
        opt = subset.Options()
        opt.layout_features = ["*"]; opt.name_IDs = ["*"]; opt.notdef_outline = True; opt.hinting = False
        sub = subset.Subsetter(opt)
        sub.populate(unicodes=keep)
        sub.subset(f)
        path = os.path.join(OUT, name + "-Bold.ttf")
        f.save(path)
        print(name, len(keep), "chars", os.path.getsize(path) // 1024, "KB")


if __name__ == "__main__":
    main(sys.argv[1])
