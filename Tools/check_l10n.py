"""Localization check (run before an upload; Cor: "make sure localizations stay up to date as we add more text").

1. Rows in Assets/Udon/Text/*.tsv with empty language cells (those languages show English).
2. Player-facing English still hard-coded in the Udon scripts: string literals with words in .text = ..., Say(...),
   seedStatus/endReason assignments, instead of game.text.Get/F1/... (debug plaques and logs are skipped).
3. The column set of every TSV against strings_core.tsv.

Usage: python Tools/check_l10n.py      (exit code 1 when something is missing)
"""
import glob, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEXT = os.path.join(ROOT, "Assets", "Udon", "Text")
bad = 0

core_cols = None
for path in sorted(glob.glob(os.path.join(TEXT, "*.tsv")), key=lambda p: (os.path.basename(p) != "strings_core.tsv", p)):
    head = None
    for n, ln in enumerate(open(path, encoding="utf-8"), 1):
        ln = ln.rstrip("\r\n")
        if not ln.strip() or ln.startswith("#"): continue
        cells = ln.split("\t")
        if head is None:
            head = cells
            if core_cols is None: core_cols = head[1:]
            elif set(head[1:]) != set(core_cols):
                print(f"{os.path.basename(path)}: columns differ from strings_core.tsv: missing {sorted(set(core_cols) - set(head[1:]))}")
                bad += 1
            continue
        gaps = [core_cols[i] for i, c in enumerate(core_cols) if (head.index(c) if c in head else 99) >= len(cells) or not cells[head.index(c)].strip()] if core_cols else []
        if gaps:
            print(f"{os.path.basename(path)}:{n} {cells[0]}: no {' '.join(gaps) if len(gaps) < len(core_cols) - 1 else 'translations'}")
            bad += 1

SKIP = ("Debug.Log", "[PH", "[prophunt]", "SendCustomEvent", "Play(", "PlayAt(", "PlayThen(", ".name", "Find(", "Shader", "StartsWith(", "== \"", "themeLabel")
english = set()
for path in glob.glob(os.path.join(TEXT, "*.tsv")):
    for ln in open(path, encoding="utf-8"):
        c = ln.rstrip("\r\n").split("\t")
        if len(c) > 1 and not ln.startswith("#"): english.add(c[1])
target = re.compile(r'(\.text\s*=|Say\(|seedStatus\s*=|End\(|endReason\s*=)')
for path in glob.glob(os.path.join(ROOT, "Assets", "Udon", "Scripts", "*.cs")):
    name = os.path.basename(path)
    if name in ("PHExplore.cs", "PHProfiler.cs", "PHTestPilot.cs", "MapGenBench.cs"): continue   # debug only
    for n, ln in enumerate(open(path, encoding="utf-8-sig"), 1):
        s = ln.strip()
        if s.startswith("//") or not target.search(s) or any(k in s for k in SKIP): continue
        for lit in re.findall(r'"((?:[^"\\]|\\.)*)"', s):
            if lit in english: continue                                      # English a table row holds (PHText.En)
            if re.fullmatch(r"[a-z]+(\.[A-Za-z0-9]+)+\|?", lit): continue     # a table key
            if " " in lit.strip() or re.fullmatch(r"[A-Z]{3,}[A-Z ]*", lit):
                print(f"{name}:{n} hard-coded text: {lit!r}")
                bad += 1

print("OK" if bad == 0 else f"{bad} problem(s)")
sys.exit(1 if bad else 0)
