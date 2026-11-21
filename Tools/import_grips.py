"""The grip station's saves (PHGripStation: SAVE TO LOG) -> Tools/weapon_grips.tsv

The station writes one log line per weapon changed: "[PHGrip] <id>\t<grip x y z>\t<turn x y z>\t<size>". This reads
the newest VRChat output log (or the logs given) and puts each weapon's last saved line into weapon_grips.tsv,
keeping the file's order and comments (a weapon not in it yet goes at the end). Then Build world scene bakes them in.

    python Tools/import_grips.py                    (the newest output_log_*.txt of VRChat on this computer)
    python Tools/import_grips.py <log> [<log>...]   (these logs, e.g. the Unity editor's Editor.log after ClientSim)
    --dry                                           (show what would change, write nothing)
"""
import os, sys, glob, re

HERE = os.path.dirname(os.path.abspath(__file__))
TSV = os.path.join(HERE, 'weapon_grips.tsv')
LINE = re.compile(r'\[PHGrip\] ((?:melee|gun):[^\t]+)\t(-?[\d.]+ -?[\d.]+ -?[\d.]+)\t(-?[\d.]+ -?[\d.]+ -?[\d.]+)\t([\d.]+)')


def newest_vrchat_log():
    base = os.path.join(os.path.expanduser('~'), 'AppData', 'LocalLow', 'VRChat', 'VRChat')
    logs = sorted(glob.glob(os.path.join(base, 'output_log_*.txt')), key=os.path.getmtime)
    if not logs: sys.exit('no VRChat output log in ' + base)
    return [logs[-1]]


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    logs = args or newest_vrchat_log()
    saved = {}
    for path in logs:
        for raw in open(path, encoding='utf8', errors='replace'):
            m = LINE.search(raw)
            if m: saved[m.group(1).strip()] = '\t'.join(g.strip() for g in m.groups())
    print('%d saved weapon(s) in %s' % (len(saved), ', '.join(logs)))
    lines = open(TSV, encoding='utf8').read().splitlines() if os.path.exists(TSV) else []
    out, seen = [], set()
    for line in lines:
        key = line.split('\t')[0].strip()
        if key in saved and not line.startswith('#'):
            if line != saved[key]: print('  changed', saved[key])
            out.append(saved[key]); seen.add(key)
        else:
            out.append(line)
    for key, line in saved.items():
        if key not in seen: print('  added  ', line); out.append(line)
    if '--dry' in sys.argv: return
    open(TSV, 'w', encoding='utf8', newline='\n').write('\n'.join(out) + '\n')
    print('wrote', TSV)


if __name__ == '__main__':
    main()
