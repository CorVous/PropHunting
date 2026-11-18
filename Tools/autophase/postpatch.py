"""after autophase4: the hand-made steppers drive the helpers' phases"""
D = 'D:/VRChat Worlds/PropHunting/Assets/Scripts/Gen/'
def patch(p, pairs):
    s = open(D + p, encoding='utf8').read()
    for a, b in pairs:
        assert s.count(a) == 1, (p, a[:80], s.count(a)); s = s.replace(a, b)
    open(D + p, 'w', encoding='utf8', newline='').write(s)
patch('MapGenOutdoor.cs', [
    ("        if (ftmK < 24 && roomFloor[room] < roomFloorMin[room]) { FillOne(ftmPass, ftmK, ftmForest); ftmK++; return false; }\n",
     """        if (ftmRun || (ftmK < 24 && roomFloor[room] < roomFloorMin[room]))
        {
            // one try, in its own phases (FillOne_Body), a step a call
            if (!ftmRun) { hq_FillOne_pass = ftmPass; hq_FillOne_k = ftmK; hq_FillOne_forest = ftmForest; PhSubReset(); ftmRun = true; }
            PhPush(); FillOne_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { ftmRun = false; ftmK++; }
            return false;
        }
"""),
    ("            ftmPass = 0; ftmK = 0;\n", "            ftmPass = 0; ftmK = 0; ftmRun = false;\n"),
    ("    bool ftmForest;\n", "    bool ftmForest, ftmRun;\n"),
])
patch('MapGenSlots.cs', [
    ("""        for (int k = 0; k < 2; k++)
        {
            if (!(lkPlaced < lkTarget && lkAttempts-- > 0)) return true;
            LeakAttempt();
        }
        return false;
""", """        // one attempt, in its own phases (LeakAttempt_Body), a step a call
        if (!lkRun)
        {
            if (!(lkPlaced < lkTarget && lkAttempts-- > 0)) return true;
            PhResetAll(); PhSubReset(); lkRun = true;
        }
        PhPush(); LeakAttempt_Body(); bool fin = PhAdvance(); PhPop();
        if (fin) lkRun = false;
        return false;
"""),
    ("            lkPhase = 1;\n            return false;\n", "            lkPhase = 1; lkRun = false;\n            return false;\n"),
])
import re
s = open(D + 'MapGenSlots.cs', encoding='utf8').read()
if 'bool lkRun;' not in s:
    s = s.replace('    bool PlaceLeaksStep()\n', '    bool lkRun;\n\n    bool PlaceLeaksStep()\n', 1)
    open(D + 'MapGenSlots.cs', 'w', encoding='utf8', newline='').write(s)

# the stages whose functions are in phases now: a step a call (MapGen.Step)
s = open(D + 'MapGen.cs', encoding='utf8').read()
def rp(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:80], s.count(a)); s = s.replace(a, b)
rp("""                if (!Layout()) { Retry("layout"); break; }
""", """                if (!stRun) StBegin();
                PhPush(); LayoutSite_Body(); stFin = PhAdvance(); PhPop();
                if (!stFin) break;
                stRun = false;
                if (!hq_LayoutSite_ret) { Retry("layout"); break; }
""")
rp("""            case 0:
                for (int i = 0;""", """            case 0:
                if (!stRun)
                for (int i = 0;""")
rp("""                statRepairs = 0; statMandatoryFail = 0;""", """                if (!stRun) { statRepairs = 0; statMandatoryFail = 0;""")
rp("""statLeakPlausible = 0; statLoose = 0;
""", """statLeakPlausible = 0; statLoose = 0; }
""")
rp("""                if (!ValidateSite()) { Retry("layout tests"); break; }
""", """                if (!stRun) StBegin();
                PhPush(); ValidateSite_Body(); stFin = PhAdvance(); PhPop();
                if (!stFin) break;
                stRun = false;
                if (!hq_ValidateSite_ret) { Retry("layout tests"); break; }
""")
rp("popPhase = 0; ftmK = -1; tdK = -1;", "popPhase = 0; ftmK = -1; tdK = -1; stRun = false;", 2)
rp("""    public string lastRetry = "";
""", """    public string lastRetry = "";

    // a stage whose function is in phases (autophase4: X_Body) runs a phase a step
    bool stRun, stFin;
    void StBegin() { PhResetAll(); PhSubReset(); stRun = true; }
""")
open(D + 'MapGen.cs', 'w', encoding='utf8', newline='').write(s)
s = open(D + 'MapGen.cs', encoding='utf8').read()
rp("""                Finish();
                stage = 7;""", """                if (!stRun) StBegin();
                PhPush(); Finish_Body(); stFin = PhAdvance(); PhPop();
                if (!stFin) break;
                stRun = false;
                stage = 7;""")
open(D + 'MapGen.cs', 'w', encoding='utf8', newline='').write(s)
