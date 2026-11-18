"""hand rewrites of the generator source into shapes the splitter knows (same behaviour)"""
import re
D = 'D:/VRChat Worlds/PropHunting/Assets/Scripts/Gen/'
def patch(p, pairs):
    s = open(D + p, encoding='utf8').read()
    for a, b in pairs:
        assert s.count(a) == 1, (p, a[:80], s.count(a)); s = s.replace(a, b)
    open(D + p, 'w', encoding='utf8', newline='').write(s)

patch('MapGenPlansZooCabin.cs', [
    ('        string[] big; string grp = null; string[] pool;\n', '        string[] big = null;\n        string grp = null;\n        string[] pool = null;\n'),
])
patch('MapGenOutdoor.cs', [
    ('        for (int s = 0, tries = 0; s < n && tries < 6 && Fit(1); tries++)\n', '        int s = 0;\n        for (int tries = 0; s < n && tries < 6 && Fit(1); tries++)\n'),
])
patch('MapGenPlansWork.cs', [
    ('''        int row = 0;
        for (int rr = rowStart; rr <= rowEnd && placed < max; rr += 5, row++)
        {
            int a0 = (alongX ? x0 : z0) + 3 + rng.Range(2), a1 = (alongX ? x1 : z1) - 3;
            int inRow = 0;
            for (int a = a0; a + len - 1 <= a1 && placed < max; a += len)
            {
                if (inRow == 2)''', '''        for (int rr = rowStart; rr <= rowEnd && placed < max; rr += 5)
        {
            int row = (rr - rowStart) / 5;
            int a0 = (alongX ? x0 : z0) + 3 + rng.Range(2), a1 = (alongX ? x1 : z1) - 3;
            int inRow = 0;
            int a = a0 - len;
            while (true)   // (a for loop over a, written so a step can stop between two racks)
            {
                a += len;
                if (!(a + len - 1 <= a1 && placed < max)) break;
                if (inRow == 2)'''),
])

# phase blocks whose lines were left at the block's own indent: indent them
import glob
for p in glob.glob(D + 'MapGen*.cs'):
    L = open(p, encoding='utf8').read().split('\n'); ch = False; i = 0
    while i < len(L) - 2:
        if re.match(r'^        if \(Ph\(\d+\)\)$', L[i]) and L[i + 1] == '        {' and L[i + 2].strip() and len(L[i + 2]) - len(L[i + 2].lstrip()) == 8 and L[i + 2].strip() != '}':
            d = 1; j = i + 2
            while d:
                c = L[j].split('//')[0]; d += c.count('{') - c.count('}')
                if d: j += 1
            for k in range(i + 2, j):
                if L[k].strip(): L[k] = '    ' + L[k]
            ch = True; i = j
        i += 1
    if ch: open(p, 'w', encoding='utf8', newline='').write('\n'.join(L)); print('indented a phase block in', p[len(D):])

# bodies left a level too deep (FillOne, LeakAttempt): back to the function's own indent
def dedent(p, sig):
    s = open(D + p, encoding='utf8').read()
    i = s.index(sig); j = s.index('{', i) + 1; d = 1; k = j
    while d:
        if s[k] == '{': d += 1
        elif s[k] == '}': d -= 1
        k += 1
    body = s[j:k - 1].split('\n')
    body = [l[4:] if l.startswith('            ') else l for l in body]
    s = s[:j] + '\n'.join(body).rstrip(' ') + '    }' + s[k:]
    open(D + p, 'w', encoding='utf8', newline='').write(s)
dedent('MapGenOutdoor.cs', '    void FillOne(int pass, int k, bool forest)')
dedent('MapGenSlots.cs', '    void LeakAttempt()')
patch('MapGenSlots.cs', [
    ('        e = comedic ? ComedicSpot(def, room, flags, outdoor, ref lkRng) : DumpSpot(def, room, flags, outdoor, ref lkRng);\n',
     '        if (comedic) e = ComedicSpot(def, room, flags, outdoor, ref lkRng);\n        else e = DumpSpot(def, room, flags, outdoor, ref lkRng);\n'),
])

# the pool and the borders a piece a step, hand-made (PoolStep, BorderStep): as plain calls, which the splitter steps
n = 0
for p in glob.glob(D + 'MapGen*.cs'):
    s = open(p, encoding='utf8').read()
    def pool(m):
        global n
        n += 1
        return 'if (Ph(%s))\n        {\n            FillFromPool(%s, %s);\n        }' % (m.group(1), m.group(2), m.group(3))
    s2 = re.sub(r'if \(PhLoop\((\d+), (?:PoolLen\((?:(?!PoolStep).)*?\)|\w+\.Length)\) && !PoolStep\((\d+), ((?:(?!PhBreak).)*?)\)\) PhBreak\(\);', pool, s, flags=re.S)
    def border(m):
        global n
        n += 1
        return 'if (Ph(%s))\n        {\n            Borders(%s, %s, %s);\n        }' % (m.group(1), m.group(3), m.group(2), m.group(4))
    s2 = re.sub(r'if \(PhLoop\((\d+), (\d+)\) && !BorderStep\((\d+), planIter, (\w+)\)\) PhBreak\(\);', border, s2)
    if s2 != s: open(p, 'w', encoding='utf8', newline='').write(s2)
print('pool and border steps rewritten:', n)

# the stage functions: where a step may end (markers for the splitter)
patch('MapGenSite.cs', [
    ("        int dmax0L = 0, dmax0R = 0;\n        for (int i = 0; i < nk; i++)\n", "        int dmax0L = 0, dmax0R = 0;\n        for (int i = 0; i < nk; i++)   // @step\n"),
    ("        for (int k = 0; k < nPlots; k++) StampPlot(k, ref rng);\n", "        // @cut\n        for (int k = 0; k < nPlots; k++)   // @step\n            StampPlot(k, ref rng);\n        // @cut\n"),
    ("        SplitLane();\n", "        // @cut\n        SplitLane();\n"),
    ("        if (!LayoutWood(offL, offR, ref rng)) return false;\n", "        // @cut\n        if (!LayoutWood(offL, offR, ref rng)) return false;\n"),
    ("        for (int k = 0; k < nPlots; k++) Gates(k, ref rng);\n", "        // @cut\n        for (int k = 0; k < nPlots; k++) Gates(k, ref rng);\n"),
])
patch('MapGenSiteTests.cs', [
    ("        for (int inst = 0; inst < nInst; inst++)\n", "        for (int inst = 0; inst < nInst; inst++)   // @step\n"),
    ("        for (int k = 0; k < nPlots; k++)\n", "        for (int k = 0; k < nPlots; k++)   // @step\n"),
    ("        // T8: everything reachable", "        // @cut\n        // T8: everything reachable"),
    ("        // T10: the wood", "        // @cut\n        // T10: the wood"),
    ("        // T11, T13: rooms", "        // @cut\n        // T11, T13: rooms"),
])
patch('MapGenSiteTests.cs', [
    ("        // T11, T13: rooms\n        for (int r = 0; r < nRooms; r++)\n", "        // T11, T13: rooms\n        for (int r = 0; r < nRooms; r++)   // @step\n"),
])
patch('MapGenFinish.cs', [
    ("    const int FS_CHUNK = 3000;", "    const int FS_CHUNK = 1200;   // cells a step (Cor: under 60 ms a frame on a Quest too)"),
])
patch('MapGenSlots.cs', [
    ("        for (int d = 0; d < GenData.nDefs; d++)\n        {\n            int w = LeakWeight(d, table, zone, outdoor, comedic);\n",
     "        for (int d = 0; d < GenData.nDefs; d++)   // @step 40\n        {\n            int w = LeakWeight(d, table, zone, outdoor, comedic);\n"),
])
patch('MapGenRooms.cs', [("    const int BT_CHUNK = 3000;", "    const int BT_CHUNK = 1500;")])
for p in ('MapGenOutdoor.cs', 'MapGenPlansChapel.cs', 'MapGenPlansNew.cs'):
    s = open(D + p, encoding='utf8').read()
    s2 = re.sub(r'        if \(PhLoop\(\d+, planN\)\) LawnTree\(60 \+ planIter\);', '        for (int lt = 0; lt < planN; lt++)\n            LawnTree(60 + lt);', s)
    assert s2 != s
    open(D + p, 'w', encoding='utf8', newline='').write(s2)
patch('MapGenFinish.cs', [
    ("        for (int e = 0; e < m; e++)\n", "        // @cut\n        for (int e = 0; e < m; e++)\n"),
    ("        int nf = 0;\n", "        // @cut\n        int nf = 0;\n"),
    ("        // Disguises (G25)", "        // @cut\n        // Disguises (G25)"),
])
s = open(D + 'MapGenFinish.cs', encoding='utf8').read()
i = s.index('    void Finish()'); j = s.index('        int n = IW * IH;\n', i)
s = s[:j] + '        // @cut\n' + s[j:]
open(D + 'MapGenFinish.cs', 'w', encoding='utf8', newline='').write(s)
