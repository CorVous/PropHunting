"""more hand rewrites before the splitter runs (after prepatch.py): the scans that are slow in Udon"""
import re
D = 'D:/VRChat Worlds/PropHunting/Assets/Scripts/Gen/'


def patch(p, pairs):
    s = open(D + p, encoding='utf8').read()
    for a, b in pairs:
        assert s.count(a) == 1, (p, a[:80], s.count(a)); s = s.replace(a, b)
    open(D + p, 'w', encoding='utf8', newline='').write(s)


# LeakAttempt: the slots of what was placed a host a step; the walkability test a step of its own
patch('MapGenSlots.cs', [
    ("            if (comedic) e = ComedicSpot(def, room, flags, outdoor, ref lkRng);\n        else e = DumpSpot(def, room, flags, outdoor, ref lkRng);\n",
     "            if (comedic) e = ComedicSpot(def, room, flags, outdoor, ref lkRng);\n            else e = DumpSpot(def, room, flags, outdoor, ref lkRng);\n"),
    ("        for (int h = first; h < nEmit; h++) FillSlots(h, 0);\n        if (emitParent[e] < 0 && !KeepIfWalkable(room, first, true)) return;\n",
     "        // @cut\n        for (int h = first; h < nEmit; h++)   // @step\n            FillSlots(h, 0);\n        // @cut\n        bool leakKept = true;\n        if (emitParent[e] < 0) leakKept = KeepIfWalkable(room, first, true);\n        if (!leakKept) return;\n"),
    ("        for (int h = first; h < nEmit; h++) FillSlots(h, 1);\n",
     "        // @cut\n        for (int h = first; h < nEmit; h++)   // @step\n            FillSlots(h, 1);\n"),
])

# the flood's start: a fresh array instead of a loop over every cell (one operation in Udon)
patch('MapGenFinish.cs', [
    ("        int n = IW * IH;\n        for (int i = 0; i < n; i++) reach[i] = 0;\n",
     "        reach = new byte[IW * IH];   // cleared (a loop over every cell is slow in Udon)\n"),
    # the ground blend: fewer cells a step
    ("            for (int i = fsCursor; i < e; i++) BlendGround(i);\n",
     "            if (e > fsCursor + FS_CHUNK / 3) e = fsCursor + FS_CHUNK / 3;\n            for (int i = fsCursor; i < e; i++) BlendGround(i);\n"),
    # the statistics and the first part of the hash: steps of their own; the emits' hash in chunks
    ("""        for (int e = 0; e < nEmit; e++)
        {
            int d = emitDef[e];
            if (Has(d, GenData.FL_DECOR)) { statDecor++; continue; }""",
     """            fsPhase = 60; fsCursor = 0;
            return false;
        }
        if (fsPhase == 60)
        {
        int e1 = fsCursor + 250 < nEmit ? fsCursor + 250 : nEmit;
        for (int e = fsCursor; e < e1; e++)
        {
            int d = emitDef[e];
            if (Has(d, GenData.FL_DECOR)) { statDecor++; continue; }"""),
    ("""            else { statTop++; statTypeTop[t]++; statZoneTop[z]++; }
        }
""", """            else { statTop++; statTypeTop[t]++; statZoneTop[z]++; }
        }
            fsCursor = e1;
            if (e1 >= nEmit) fsPhase = 61;
            return false;
        }
        if (fsPhase == 61)
        {
"""),
    ("""        {
            uint h2 = fsHash;
            for (int e = 0; e < nEmit; e++)
            {
                h2 = Fnv(h2, emitDef[e]); h2 = Fnv(h2, emitX[e]); h2 = Fnv(h2, emitY[e]); h2 = Fnv(h2, emitZ[e]);
                h2 = Fnv(h2, emitYaw[e]); h2 = Fnv(h2, emitParent[e]); h2 = Fnv(h2, emitFlags[e]);
            }
            mapHash = h2;
            return true;
        }
""", """        {
            uint h2 = fsHash;
            if (fsPhase == 8) { fsPhase = 9; fsCursor = 0; }
            int e1 = fsCursor + 120 < nEmit ? fsCursor + 120 : nEmit;
            for (int e = fsCursor; e < e1; e++)
            {
                h2 = Fnv(h2, emitDef[e]); h2 = Fnv(h2, emitX[e]); h2 = Fnv(h2, emitY[e]); h2 = Fnv(h2, emitZ[e]);
                h2 = Fnv(h2, emitYaw[e]); h2 = Fnv(h2, emitParent[e]); h2 = Fnv(h2, emitFlags[e]);
            }
            fsHash = h2; fsCursor = e1;
            if (e1 < nEmit) return false;
            mapHash = h2;
            return true;
        }
"""),
])

# Finish: the compaction and the cell loops so many a step
s = open(D + 'MapGenFinish.cs', encoding='utf8').read()
i = s.index('    void Finish()'); j = s.index('\n    }\n', i)
body = s[i:j]
for a, b in [
    ("        for (int e = 0; e < nEmit; e++)\n        {\n            if ((emitFlags[e] & EF_REMOVED) != 0)", "        for (int e = 0; e < nEmit; e++)   // @step 120\n        {\n            if ((emitFlags[e] & EF_REMOVED) != 0)"),
    ("        for (int e = 0; e < m; e++)\n", "        for (int e = 0; e < m; e++)   // @step 300\n"),
    ("        for (int i = 0; i < n; i++)\n", "        for (int i = 0; i < n; i++)   // @step 1500\n"),
]:
    assert body.count(a) == 1, a[:60]; body = body.replace(a, b)
s = s[:i] + body + s[j:]
open(D + 'MapGenFinish.cs', 'w', encoding='utf8', newline='').write(s)

# BuildTagsStep: smaller chunks; the doors' loop in chunks; the entries and the stairs' tags steps of their own
patch('MapGenRooms.cs', [
    ("    const int BT_CHUNK = 1500;", "    const int BT_CHUNK = 800;"),
    ("            if (e >= W * H) btPhase = 2;\n", "            if (e >= W * H) { btPhase = 2; btCursor = 0; }\n"),
    ("        for (int c = 0; c < W * H; c++)\n        {\n            if (cellZone[c] == GenData.Z_NONE) continue;\n            int cx = c % W, cz = c / W;\n            for (int d = 0; d < 4; d++)\n            {\n                if (EdgeType(cx, cz, d) != GenData.E_DOOR) continue;\n                int off = EdgeOff(cx, cz, d);\n",
     "        int btE = btCursor + 100 < W * H ? btCursor + 100 : W * H;\n        for (int c = btCursor; c < btE; c++)\n        {\n            if (cellZone[c] == GenData.Z_NONE) continue;\n            int cx = c % W, cz = c / W;\n            for (int d = 0; d < 4; d++)\n            {\n                if (EdgeType(cx, cz, d) != GenData.E_DOOR) continue;\n                int off = EdgeOff(cx, cz, d);\n"),
    ("        RoomEntries();\n        StairTags();",
     "            btCursor = btE;\n            if (btE >= W * H) btPhase = 20;\n            return false;\n        }\n        if (btPhase == 20) { RoomEntries(); btPhase = 21; return false; }\n        if (btPhase == 21)\n        {\n        StairTags();"),
])

# the campsite's tents: a plain loop again (it was a loop phase by hand), which the splitter steps finer
s = open(D + 'MapGenPlansZooCabin.cs', encoding='utf8').read()
a = s.index('        if (PhLoop(3, planA >= 0 ? 1 + AreaM2(room) / 24 : 0))\n')
b = s.index('        string[] pool = { "n_cot"', a)
old = s[a:b]
m = re.search(r'                int tent = (.*)\n                (if \(tent >= 0 && Fit\(1\)\) .*)\n', old)
assert m
new = ('        for (int k = 0; k < (planA >= 0 ? 1 + AreaM2(room) / 24 : 0) && Fit(1); k++)\n        {\n'
       '            int tent = ' + m.group(1) + '\n            ' + m.group(2) + '\n        }\n')
s = s[:a] + new + s[b:]
open(D + 'MapGenPlansZooCabin.cs', 'w', encoding='utf8', newline='').write(s)

# RoomWalkable (the test behind every Keep): the wide floor, the start, each flood round, the rest: a step each
patch('MapGenFinish.cs', [
    ("        BuildWide(room);\n        int start = -1, free = 0;\n", "        BuildWide(room);\n        // @cut\n        int start = -1, free = 0;\n"),
    ("        int seen = 0;\n        for (int round = 0; round < 3; round++)\n", "        int seen = 0;\n        for (int round = 0; round < 3; round++)   // @step\n"),
    ("            // the narrow leftovers that hang on the wide floor\n", "            // @cut\n            // the narrow leftovers that hang on the wide floor\n"),
    ("        if (!ExitsJoined(room)) return false;\n        if (seen == free) return true;\n", "        // @cut\n        if (!ExitsJoined(room)) return false;\n        if (seen == free) return true;\n"),
])

# the map-wide check of the rooms' reach: so many cells a step, also within one (large) room
patch('MapGenFinish.cs', [
    ("""        while (vRoom < nRooms && bad < 0 && cells < FS_CHUNK)
        {
            int r = vRoom++;
            int s = roomCellStart[r], n = roomCellCount[r], free = 0, ok = 0;
            for (int k = s; k < s + n; k++)
            {
                int i = roomCells[k];
                if (SolidAt(i)) continue;
                free++;
                if (reach[i] != 0) ok++;
            }
            cells += n;
            if (free - ok > SealedAllowance(r)) bad = r;
        }
""", """        while (vRoom < nRooms && bad < 0 && cells < FS_CHUNK / 4)
        {
            int r = vRoom;
            int s = roomCellStart[r], n = roomCellCount[r];
            int k1 = vK + (FS_CHUNK / 4 - cells);   // this step's share of the room
            if (k1 > n) k1 = n;
            for (int k = s + vK; k < s + k1; k++)
            {
                int i = roomCells[k];
                if (SolidAt(i)) continue;
                vFree++;
                if (reach[i] != 0) vOk++;
            }
            cells += k1 - vK; vK = k1;
            if (vK < n) break;   // the rest of the room next step
            if (vFree - vOk > SealedAllowance(r)) bad = r;
            vRoom++; vK = 0; vFree = 0; vOk = 0;
        }
"""),
    ("        if (vPhase == 0) { FloodBegin(); vPhase = 1; vRoom = 0; return false; }\n", "        if (vPhase == 0) { FloodBegin(); vPhase = 1; vRoom = 0; vK = 0; vFree = 0; vOk = 0; return false; }\n"),
    ("    int vPhase, vRepair, vRoom;\n", "    int vPhase, vRepair, vRoom, vK, vFree, vOk;\n"),
    # what a failed Keep takes back: a piece a step
    ("        for (int e = first; e < nEmit; e++)\n            if (emitParent[e] < 0) Remove(e);\n        statWalkRejects++;\n",
     "        for (int e = first; e < nEmit; e++)   // @step\n            if (emitParent[e] < 0) Remove(e);\n        statWalkRejects++;\n"),
])

# OpenSpot: the clearance map in chunks (a large room has hundreds of cells), then the choice
patch('MapGenCompose.cs', [
    ("        int qh = 0, qt = 0;\n        for (int k = s; k < s + n; k++)\n        {\n            int i = roomCells[k], t = tags[i];\n",
     "        int qh = 0, qt = 0;\n        for (int k = s; k < s + n; k++)   // @step 60\n        {\n            int i = roomCells[k], t = tags[i];\n"),
    ("        while (qh < qt)\n        {\n            int i = queue[qh++];\n            int ix = i % IW, iz = i / IW;\n            for (int d = 0; d < 8; d++)\n",
     "        while (qh < qt)   // @step 60\n        {\n            int i = queue[qh++];\n            int ix = i % IW, iz = i / IW;\n            for (int d = 0; d < 8; d++)\n"),
    ("        int best = -1, bestScore = -2000000000;\n        for (int k = s; k < s + n; k++)\n",
     "        int best = -1, bestScore = -2000000000;\n        for (int k = s; k < s + n; k++)   // @step 150\n"),
])

# RemoveOne: who holds the cell now. The cell test first (a few operations; nearly every emit fails it), then
# the rest: the same answer, a fifth of the work in Udon (a taken-back leak cost 50 ms here)
patch('MapGenFinish.cs', [
    ("""                        if (o == e || (emitFlags[o] & EF_REMOVED) != 0 || (emitParent[o] >= 0 && (emitFlags[o] & EF_ADJACENT) == 0) || Has(emitDef[o], GenData.FL_FLAT) || Has(emitDef[o], GenData.FL_DECOR)) continue;
                        if (x >= emitRX[o] && x < emitRX[o] + emitRW[o] && z >= emitRZ[o] && z < emitRZ[o] + emitRD[o]) { occ[i] = o + 1; break; }
""", """                        if (x < emitRX[o] || z < emitRZ[o] || x >= emitRX[o] + emitRW[o] || z >= emitRZ[o] + emitRD[o]) continue;
                        if (o == e || (emitFlags[o] & EF_REMOVED) != 0 || (emitParent[o] >= 0 && (emitFlags[o] & EF_ADJACENT) == 0) || Has(emitDef[o], GenData.FL_FLAT) || Has(emitDef[o], GenData.FL_DECOR)) continue;
                        occ[i] = o + 1; break;
"""),
])
