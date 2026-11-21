// MapGen, part 1b: the outer grid laid out like a real place (owner decision G32; research report 16, section 9).
//
//   a lane from the hunter release into the map  ->  plots on both sides of it, fronts to the lane  ->  one building
//   on each plot, a plan from the library (GenPlans.cs, built offline by tools/planlib/make_plans.py), set back
//   behind its front garden and turned to face the lane  ->  the plot's outdoor cells typed by where they lie
//   (front garden, side, a patio at the back door, beds along the back fence, a fire pit away from the walls, lawn
//   the rest; a diner's forecourt and service yard, a warehouse's yard)  ->  a wood at the lane's end, beside the
//   last plot and behind the plots, in one piece with an uneven edge  ->  the whole site turned and mirrored
//
// Irregularity stays where real sites have it: the footprints (rectangles, L and T shapes made of wings), plot
// widths, depths and setbacks, which side of the lane a plot is on, the wood's edge. Walls, doors, gates and fences
// follow from the plots (BuildEdgesSite); the layout tests (ValidateSite) run before any prop is placed, and a
// layout that fails one is drawn again (a seed re-roll, as for an unreachable room).
// Canonical frame while laying out: the lane runs along +y at x = CO, the release is at (CO, 0); left plots
// (side -1) lie at x < CO with their fronts facing +x, right plots (side +1) at x > CO facing -x.
public partial class MapGen
{
    // where an outdoor cell lies on its plot, and what the lane, the wood and the buildings are
    public const int PL_NONE = 0, PL_FRONT = 1, PL_BACK = 2, PL_SIDE = 3, PL_LANE = 4, PL_WOOD = 5, PL_BUILDING = 6;

    const int CV = 64, CO = 32;
    int[] cvZone, cvInst, cvRoom, cvPlace, cvPlot;

    public int[] siteType = new int[160 * 26];    // per instance and room letter: the room type
    public int[] sitePlace = new int[160 * 26];   // ... and where the room lies (PL_*)
    public int[] cellPlot;                        // per outer cell: its plot, -1 for the lane, the wood and the release
    public int[] roomPlace = new int[400];        // per room: PL_*
    public int[] roomPlot = new int[400];         // per room: its plot or -1

    // doors and open edges, canonical cell and direction while laying out, world after the turn
    const int SD_DOOR = 0, SD_OPEN = 1, SD_FRONT = 2, SD_BACK = 3;
    int nSd;
    int[] sdCell = new int[800], sdDir = new int[800], sdKind = new int[800];

    // plots
    public int nPlots;
    public int[] plotZone = new int[16], plotPlan = new int[16], plotSide = new int[16], plotInstB = new int[16], plotInstY = new int[16];
    int[] plotY0 = new int[16], plotAlong = new int[16], plotDepth = new int[16], plotSet = new int[16], plotSL = new int[16], plotSR = new int[16];
    int[] plotBack = new int[16], plotMir = new int[16];
    int laneEnd, siteInstLane, siteInstWood;

    // ---- the plan library ----------------------------------------------------------------------------------
    static int PlS(int p) { return GenPlans.Start[p]; }
    public static int PlZone(int p) { return GenPlans.Data[PlS(p)]; }
    public static int PlW(int p) { return GenPlans.Data[PlS(p) + 1]; }
    public static int PlD(int p) { return GenPlans.Data[PlS(p) + 2]; }
    public static int PlRooms(int p) { return GenPlans.Data[PlS(p) + 3]; }
    public static int PlRole(int p, int r) { return GenPlans.Data[PlS(p) + 4 + r]; }
    public static int PlCell(int p, int x, int y) { return GenPlans.Data[PlS(p) + 4 + PlRooms(p) + y * PlW(p) + x]; }
    static int PlDoorBase(int p) { return PlS(p) + 4 + PlRooms(p) + PlW(p) * PlD(p); }
    public static int PlDoors(int p) { return GenPlans.Data[PlDoorBase(p)]; }
    public static int PlDoor(int p, int k) { return GenPlans.Data[PlDoorBase(p) + 1 + k]; }
    public static int PlFront(int p) { return GenPlans.Data[PlDoorBase(p) + 1 + PlDoors(p)]; }
    public static int PlBack(int p) { return GenPlans.Data[PlDoorBase(p) + 2 + PlDoors(p)]; }

    static int PlCellCount(int p)
    {
        int n = 0;
        for (int y = 0; y < PlD(p); y++) for (int x = 0; x < PlW(p); x++) if (PlCell(p, x, y) >= 0) n++;
        return n;
    }

    // a plan of a zone whose size suits the map: houses of 5 or 6 cells at S up to 6 to 10 at XL, warehouses of 6
    // to 12, diners of 6 to 8
    int PickPlan(int zone, ref GenRng rng)
    {
        int lo, hi;
        if (zone == GenData.Z_HOUSE) { lo = GenData.SizeHouseCellsMin[size]; hi = GenData.SizeHouseCellsMax[size]; }
        else if (zone == GenData.Z_WAREHOUSE) { lo = GenData.SizeWarehouseCellsMin[size]; hi = GenData.SizeWarehouseCellsMax[size]; }
        else if (zone == GenData.Z_SHOP) { lo = GenData.SizeShopCellsMin[size]; hi = GenData.SizeShopCellsMax[size]; }
        else if (zone == GenData.Z_GARAGE) { lo = GenData.SizeGarageCellsMin[size]; hi = GenData.SizeGarageCellsMax[size]; }
        else if (zone == GenData.Z_BARN) { lo = GenData.SizeBarnCellsMin[size]; hi = GenData.SizeBarnCellsMax[size]; }
        else if (zone == GenData.Z_CABIN) { lo = GenData.SizeCabinCellsMin[size]; hi = GenData.SizeCabinCellsMax[size]; }
        else if (zone == GenData.Z_FAIR) { lo = GenData.SizeFairCellsMin[size]; hi = GenData.SizeFairCellsMax[size]; }
        else if (zone == GenData.Z_CHAPEL) { lo = GenData.SizeChapelCellsMin[size]; hi = GenData.SizeChapelCellsMax[size]; }
        else { hi = GenData.SizeDinerCellsMax[size]; lo = 6; }   // more inside (Cor): the bigger diners
        int n = 0;
        for (int p = 0; p < GenPlans.Count; p++)
        {
            if (PlZone(p) != zone) continue;
            int c = PlCellCount(p);
            if (c >= lo && c <= hi) cand[n++] = p;
        }
        if (n == 0) return -1;
        // a mix of small and big buildings (Cor): a size drawn evenly from the range, then a plan of that size or
        // the nearest one there is
        int target = lo + rng.Range(hi - lo + 1), best = int.MaxValue, m = 0;
        for (int i = 0; i < n; i++) { int dd = PlCellCount(cand[i]) - target; if (dd < 0) dd = -dd; if (dd < best) { best = dd; m = 0; } if (dd == best) cand[m++] = cand[i]; }
        return cand[rng.Range(m)];
    }

    // ---- the canvas ---------------------------------------------------------------------------------------------
    int NewInst(int zone)
    {
        int i = nInst++;
        instZone[i] = zone;
        for (int l = 0; l < 26; l++) { siteType[i * 26 + l] = 0; sitePlace[i * 26 + l] = PL_NONE; }
        return i;
    }

    void Put(int x, int y, int zone, int inst, int room, int place, int plot)
    {
        int c = y * CV + x;
        cvZone[c] = zone; cvInst[c] = inst; cvRoom[c] = room; cvPlace[c] = place; cvPlot[c] = plot;
    }

    bool Free(int x, int y) { return x >= 0 && y >= 0 && x < CV && y < CV && cvZone[y * CV + x] == GenData.Z_NONE; }
    int CvZone(int x, int y) { return x < 0 || y < 0 || x >= CV || y >= CV ? GenData.Z_NONE : cvZone[y * CV + x]; }

    void Sd(int x, int y, int dir, int kind)
    {
        if (nSd >= sdCell.Length) return;
        sdCell[nSd] = y * CV + x; sdDir[nSd] = dir; sdKind[nSd] = kind; nSd++;
    }

    // plot-local cell (u along the lane, v away from it) on the canvas
    void PlotXY(int k, int u, int v, out int x, out int y)
    {
        int cy = plotY0[k] + u;
        int cx = plotSide[k] < 0 ? CO - 1 - v : CO + 1 + v;
        Canon(plotSeg[k], cx, cy, out x, out y);
    }

    // a plan direction (0 back, 1 right, 2 front, 3 left) on the canvas: +u is +y (direction 0) in the plot's
    // segment, turned with the segment
    int PlotDir(int k, int planDir)
    {
        int front = plotSide[k] < 0 ? 1 : 3, d;
        if (planDir == 2) d = front;
        else if (planDir == 0) d = (front + 2) & 3;
        else d = ((planDir == 1) != (plotMir[k] != 0)) ? 0 : 2;
        return (d + laneRot[plotSeg[k]]) & 3;
    }

    // ---- site shapes and lane segments
    public const int SHAPE_STRAIGHT = 0, SHAPE_BEND = 1, SHAPE_T = 2, SHAPE_CUL = 3;
    // Cor: more kinds of layout than one lane: a crossroads (four arms), a village square (a T whose junction is a
    // three by three square), and a U (the lane turns twice and comes back: the second lane runs behind the
    // first lane's plots, a back lane)
    public const int SHAPE_CROSS = 4, SHAPE_SQUARE = 5, SHAPE_U = 6;
    public int siteShape;
    public int forceShape = -1;
    int nLane, laneEnd0;
    int[] laneOX = new int[4], laneOY = new int[4], laneRot = new int[4], laneLen = new int[4];
    public int[] plotSeg = new int[16];

    // a canonical cell (lane along +y at x = CO from y = 0) of segment s on the canvas: turned laneRot[s] quarter
    // turns clockwise about the segment's origin
    void Canon(int s, int cx, int cy, out int x, out int y)
    {
        int rx = cx - CO, ry = cy;
        for (int r = 0; r < laneRot[s]; r++) { int t = rx; rx = ry; ry = -t; }
        x = laneOX[s] + rx; y = laneOY[s] + ry;
    }

    // a canonical direction of segment s on the canvas
    int LaneDir(int s, int d) { return (d + laneRot[s]) & 3; }

    // the lane of a segment runs to the far end of its longer row of plots
    void FinishSegment(int s, int offL, int offR, int lastL, int lastR)
    {
        // the last plot on each side keeps a side yard: no building at the edge of the site
        if (lastL >= 0 && plotSR[lastL] == 0) { plotSR[lastL] = 1; plotAlong[lastL]++; offL++; }
        if (lastR >= 0 && plotSR[lastR] == 0) { plotSR[lastR] = 1; plotAlong[lastR]++; offR++; }
        laneLen[s] = offL > offR ? offL : offR;
        if (laneLen[s] < 2) laneLen[s] = 2;
    }

    // a segment after a corner: the side that faces back down the first lane starts past that lane's plots
    void StartSegment(int s, int dmax0L, int dmax0R, out int offL, out int offR)
    {
        offL = 1; offR = 1;
        int lx, ly, rx, ry;
        Canon(s, CO - 1, 1, out lx, out ly);
        Canon(s, CO + 1, 1, out rx, out ry);
        // a side whose cells lie below the corner (toward the release) is the inner side
        int inner = dmax0L > dmax0R ? dmax0L : dmax0R;
        if (ly < laneOY[s]) offL = inner + 1;
        if (ry < laneOY[s]) offR = inner + 1;
    }

    // how far past the crossing (along the first lane) the plots of the side arms reach
    void CrossFar(ref int far)
    {
        for (int q = 0; q < nPlots; q++)
        {
            if (plotSeg[q] < 1 || plotSeg[q] > 2) continue;
            for (int u = 0; u < plotAlong[q]; u++)
                for (int v = 0; v < plotDepth[q]; v++)
                {
                    int x, y;
                    PlotXY(q, u, v, out x, out y);
                    if (y - laneEnd0 > far) far = y - laneEnd0;
                }
        }
    }

    // every cell of the plot on the canvas and free
    bool PlotFits(int k)
    {
        for (int u = 0; u < plotAlong[k]; u++)
            for (int v = 0; v < plotDepth[k]; v++)
            {
                int x, y;
                PlotXY(k, u, v, out x, out y);
                if (x < 1 || y < 0 || x >= CV - 1 || y >= CV - 1) return false;
                for (int q = 0; q < k; q++)
                {
                    // no two plots overlap
                    for (int u2 = 0; u2 < plotAlong[q]; u2++)
                        for (int v2 = 0; v2 < plotDepth[q]; v2++)
                        {
                            int x2, y2;
                            PlotXY(q, u2, v2, out x2, out y2);
                            if (x2 == x && y2 == y) return false;
                        }
                }
            }
        return true;
    }

    // a plan cell (x along its front, y back from it) as plot-local u and v
    // buildings may stand on the map's edge (Cor, option a: more inside); PS: plan cells drawn PS x PS (the doubled-
    // rooms option, not taken: 1)
    public const bool EdgeOK = true;
    public const int PS = 1;

    void PlanUV(int k, int px, int py, out int u, out int v)
    {
        int w = PlW(plotPlan[k]) * PS;
        u = plotSL[k] + (plotMir[k] != 0 ? w - 1 - px : px);
        v = plotSet[k] + py;
    }

    // =====================================================================================================
    // The site
    // =====================================================================================================
    bool LayoutSite()
    {
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); LayoutSite_Body(); fin = PhAdvance(); PhPop(); }
        return hq_LayoutSite_ret;
    }

    // LayoutSite in phases, a step a call (autophase4); LayoutSite() above runs them to the end
    void LayoutSite_Body()
    {
        planMax = 10;
        if (planPhase == 0)
        {
            GenRng rng = Rng(ST_LAYOUT, 0, 0);
            if (cvZone == null) { cvZone = new int[CV * CV]; cvInst = new int[CV * CV]; cvRoom = new int[CV * CV]; cvPlace = new int[CV * CV]; cvPlot = new int[CV * CV]; }
            for (int i = 0; i < CV * CV; i++) { cvZone[i] = GenData.Z_NONE; cvInst[i] = -1; cvRoom[i] = -1; cvPlace[i] = PL_NONE; cvPlot[i] = -1; }
            nInst = 0; nSd = 0; nPlots = 0;

            // the hunter release, where the lane begins
            int rel = NewInst(GenData.Z_RELEASE);
            siteType[rel * 26] = GenData.R_RELEASE;
            Put(CO, 0, GenData.Z_RELEASE, rel, 0, PL_NONE, -1);

            // (1) the plots of the map's size (layout 4: a mix drawn per map, never two of one kind but houses), the
            // commercial ones near the road's start more often than not, a farm at the far end, towards the wood
            string prog = GenData.SizePlots[size];
            int[] kinds = new int[16];
            int nk = 0;
            hq_LayoutSite_rng = rng;
            hq_LayoutSite_prog = prog;
            hq_LayoutSite_kinds = kinds;
            hq_LayoutSite_nk = nk;
        }
        if (planPhase == 1 && PhLoop(1, 100000))
        {
            GenRng rng = hq_LayoutSite_rng;
            string prog = hq_LayoutSite_prog;
            int[] kinds = hq_LayoutSite_kinds;
            int nk = hq_LayoutSite_nk;
            int i = planIter;
            if (!(i < prog.Length)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                char ch = prog[i];
                int z = GenData.Z_HOUSE;
                if (ch != 'H' && !(ch == '*' && rng.Chance(GenData.PLOT_ANY_HOUSE))) z = DrawPlotKind(kinds, nk, ref rng);
                kinds[nk++] = z;
            }
            hq_LayoutSite_rng = rng;
            hq_LayoutSite_prog = prog;
            hq_LayoutSite_kinds = kinds;
            hq_LayoutSite_nk = nk;
        }
        if (planPhase == 2)
        {
            GenRng rng = hq_LayoutSite_rng;
            int[] kinds = hq_LayoutSite_kinds;
            int nk = hq_LayoutSite_nk;
            Shuffle(kinds, nk, ref rng);
            bool comFirst = rng.Chance(600);
            {
                // shops at the road's start, houses, then the farm and the park towards the wood
                int w = 0;
                for (int pass = 0; pass < 3; pass++)
                    for (int i = 0; i < nk; i++)
                    {
                        int z = kinds[i];
                        int rank = (z == GenData.Z_BARN || z == GenData.Z_PARK || z == GenData.Z_ZOO || z == GenData.Z_CABIN || z == GenData.Z_CHAPEL) ? 2 : (z != GenData.Z_HOUSE && comFirst ? 0 : 1);
                        if (rank == pass) cand[w++] = z;
                    }
                for (int i = 0; i < nk; i++) kinds[i] = cand[i];
            }

            // (2) the shape of the site (Cor: maps should not all look alike): a straight lane, a lane with a bend, a T at
            // its end, or a cul-de-sac with a plot across its end. Each lane piece is a segment with its own frame; the
            // plots of a segment are laid out in the canonical frame and turned onto the canvas (Canon).
            siteShape = SHAPE_STRAIGHT;
            int shapeRoll = rng.Range(100);
            if (nk >= 4 && shapeRoll < 14) siteShape = SHAPE_CROSS;
            else if (nk >= 3 && shapeRoll < 28) siteShape = SHAPE_SQUARE;
            else if (nk >= 4 && shapeRoll < 42) siteShape = SHAPE_U;
            else if (nk >= 3 && shapeRoll < 56) siteShape = SHAPE_T;
            else if (nk >= 2 && shapeRoll < 74) siteShape = SHAPE_BEND;
            else if (nk >= 2 && shapeRoll < 88) siteShape = SHAPE_CUL;
            if (forceShape >= 0) siteShape = forceShape;   // test aid
            int bendDir = rng.Chance(500) ? 1 : 3;
            // which plots go on which segment
            int n0 = nk;
            if (siteShape == SHAPE_BEND) n0 = (nk + 1) / 2;
            else if (siteShape == SHAPE_T) n0 = nk - 2 * ((nk - 1) / 2) > 0 ? nk - 2 * ((nk - 1) / 2) : 1;
            else if (siteShape == SHAPE_CUL) n0 = nk - 1;
            else if (siteShape == SHAPE_SQUARE) n0 = nk - 2 * ((nk - 1) / 2) > 0 ? nk - 2 * ((nk - 1) / 2) : 1;
            else if (siteShape == SHAPE_U) n0 = nk - (nk / 3 > 1 ? nk / 3 : 1);   // the back lane: a third of the plots, on one side
            else if (siteShape == SHAPE_CROSS) n0 = nk - 3 * ((nk - 1) / 3) > 0 ? nk - 3 * ((nk - 1) / 3) : 1;
            int perArm = siteShape == SHAPE_CROSS ? (nk - n0) / 3 : 0;
            int crossFar = 0;   // the crossroads: how deep the side arms' plots reach past the crossing
            nLane = 1; laneOX[0] = CO; laneOY[0] = 0; laneRot[0] = 0;
            int offL = 1, offR = 1;
            bool touchL = false, touchR = false;
            int lastL = -1, lastR = -1;
            int seg = 0;
            int dmax0L = 0, dmax0R = 0;
            hq_LayoutSite_rng = rng;
            hq_LayoutSite_kinds = kinds;
            hq_LayoutSite_nk = nk;
            hq_LayoutSite_bendDir = bendDir;
            hq_LayoutSite_n0 = n0;
            hq_LayoutSite_perArm = perArm;
            hq_LayoutSite_crossFar = crossFar;
            hq_LayoutSite_offL = offL;
            hq_LayoutSite_offR = offR;
            hq_LayoutSite_touchL = touchL;
            hq_LayoutSite_touchR = touchR;
            hq_LayoutSite_lastL = lastL;
            hq_LayoutSite_lastR = lastR;
            hq_LayoutSite_seg = seg;
            hq_LayoutSite_dmax0L = dmax0L;
            hq_LayoutSite_dmax0R = dmax0R;
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            int nk = hq_LayoutSite_nk;
            if (!PhSubActive())
            {
                int i = planIter;
                if (!(i < nk)) PhBreak();
                else { hq_LayoutSite_i = i; PhSubBegin(); }
            }
            hq_LayoutSite_nk = nk;
            if (PhSubActive())
            {
                PhPush(); LayoutSite_L1(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 4)
        {
            int offL = hq_LayoutSite_offL;
            int offR = hq_LayoutSite_offR;
            int lastL = hq_LayoutSite_lastL;
            int lastR = hq_LayoutSite_lastR;
            int seg = hq_LayoutSite_seg;
            if (nPlots == 0) { hq_LayoutSite_ret = false; hq_LayoutSite_offL = offL; hq_LayoutSite_offR = offR; hq_LayoutSite_lastL = lastL; hq_LayoutSite_lastR = lastR; hq_LayoutSite_seg = seg; PhEnd(); return; }
            if (siteShape == SHAPE_STRAIGHT)
            {
                // the last plot on each side keeps a side yard toward the wood at the lane's end
                if (lastL >= 0 && plotSR[lastL] == 0) { plotSR[lastL] = 1; plotAlong[lastL]++; offL++; }
                if (lastR >= 0 && plotSR[lastR] == 0) { plotSR[lastR] = 1; plotAlong[lastR]++; offR++; }
                laneLen[0] = offL > offR ? offL : offR;
            }
            else if (siteShape == SHAPE_CUL)
            {
                // the plot across the end: centred on the lane, its front to the lane's last cell
                int ek = nPlots - 1;
                if (plotSR[ek] == 0) { plotSR[ek] = 1; plotAlong[ek]++; }
                plotY0[ek] = 0;
                laneOX[1] = CO - plotAlong[ek] / 2;
                laneOY[1] = laneEnd0;
                laneLen[1] = 0;
            }
            else FinishSegment(seg, offL, offR, lastL, lastR);
            hq_LayoutSite_offL = offL;
            hq_LayoutSite_offR = offR;
            hq_LayoutSite_lastL = lastL;
            hq_LayoutSite_lastR = lastR;
            hq_LayoutSite_seg = seg;
        }
        if (planPhase == 5 && PhLoop(5, 100000))
        {
            int k = planIter;
            if (!(k < nPlots)) PhBreak();
            else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
            {
                if (!PlotFits(k)) { hq_LayoutSite_ret = false; PhEnd(); return; }
            }
        }
        // @cut
        if (planPhase == 6 && PhLoop(6, 100000))
        {
            if (!PhSubActive())
            {
                int k = planIter;
                if (!(k < nPlots)) PhBreak();
                else { hq_LayoutSite_k = k; PhSubBegin(); }
            }
            if (PhSubActive())
            {
                PhPush(); LayoutSite_L2(); bool fin = PhAdvance(); PhPop();
                if (fin) PhSubEnd(); else PhStay();
                if (phBreak) { phBreak = false; PhBreak(); }
                if (phReturn) { phReturn = false; PhEnd(); return; }
            }
        }
        if (planPhase == 7)
        {
            // @cut

            // (3) the lanes: from the release, then each further segment from the corner
            laneEnd = laneLen[0];
            siteInstLane = NewInst(GenData.Z_LAWN);
            siteType[siteInstLane * 26] = GenData.R_LANE; sitePlace[siteInstLane * 26] = PL_LANE;
            int laneTo = siteShape == SHAPE_STRAIGHT ? laneEnd : laneEnd + 1;   // a bent lane includes its corner cell
            for (int y = 1; y < laneTo; y++) Put(CO, y, GenData.Z_LAWN, siteInstLane, 0, PL_LANE, -1);
            // a cul-de-sac's turning head: the lane widens along the whole front of the plot across its end
            if (siteShape == SHAPE_CUL)
            {
                int ek = nPlots - 1;
                for (int x = laneOX[1]; x < laneOX[1] + plotAlong[ek]; x++)
                    if (x != CO && Free(x, laneEnd)) Put(x, laneEnd, GenData.Z_LAWN, siteInstLane, 0, PL_LANE, -1);
            }
            for (int s = 1; s < nLane; s++)
                for (int y = 1; y < laneLen[s]; y++)
                {
                    int x, yy;
                    Canon(s, CO, y, out x, out yy);
                    if (!Free(x, yy)) { if (CvZone(x, yy) == GenData.Z_LAWN && cvInst[yy * CV + x] == siteInstLane) continue; { hq_LayoutSite_ret = false; PhEnd(); return; } }
                    Put(x, yy, GenData.Z_LAWN, siteInstLane, 0, PL_LANE, -1);
                }
            // the village square: three by three around the junction
            if (siteShape == SHAPE_SQUARE)
                for (int y = laneEnd0; y <= laneEnd0 + 2; y++)
                    for (int x = CO - 1; x <= CO + 1; x++)
                    {
                        if (Free(x, y)) Put(x, y, GenData.Z_LAWN, siteInstLane, 0, PL_LANE, -1);
                        else if (!(CvZone(x, y) == GenData.Z_LAWN && cvInst[y * CV + x] == siteInstLane)) { hq_LayoutSite_ret = false; PhEnd(); return; }
                    }
            Sd(CO, 0, 0, SD_OPEN);
        }
        if (planPhase == 8)
        {
            // @cut
            SplitLane();
        }
        if (planPhase == 9)
        {
            GenRng rng = hq_LayoutSite_rng;
            int offL = hq_LayoutSite_offL;
            int offR = hq_LayoutSite_offR;

            // (4) the wood
            // @cut
            if (!LayoutWood(offL, offR, ref rng)) { hq_LayoutSite_ret = false; hq_LayoutSite_rng = rng; hq_LayoutSite_offL = offL; hq_LayoutSite_offR = offR; PhEnd(); return; }
            hq_LayoutSite_rng = rng;
            hq_LayoutSite_offL = offL;
            hq_LayoutSite_offR = offR;
        }
        if (planPhase == 10)
        {
            GenRng rng = hq_LayoutSite_rng;

            // (5) gates: a house's front path meets the lane; a shop's front is open to the lane; some back gardens
            // have a gate into the wood
            // @cut
            for (int k = 0; k < nPlots; k++) Gates(k, ref rng);

            // (6) turn and mirror the whole site, and take it onto the outer grid
            layRng = rng;   // PlaceSite goes on with this stream, a step later (MapGen.Step, stage 9)
            { hq_LayoutSite_ret = true; hq_LayoutSite_rng = rng; PhEnd(); return; }
            hq_LayoutSite_rng = rng;
        }
    }
    void LayoutSite_L1()
    {
        planMax = 4;
        if (planPhase == 0)
        {
            int[] kinds = hq_LayoutSite_kinds;
            int nk = hq_LayoutSite_nk;
            int bendDir = hq_LayoutSite_bendDir;
            int n0 = hq_LayoutSite_n0;
            int perArm = hq_LayoutSite_perArm;
            int crossFar = hq_LayoutSite_crossFar;
            int offL = hq_LayoutSite_offL;
            int offR = hq_LayoutSite_offR;
            bool touchL = hq_LayoutSite_touchL;
            bool touchR = hq_LayoutSite_touchR;
            int lastL = hq_LayoutSite_lastL;
            int lastR = hq_LayoutSite_lastR;
            int seg = hq_LayoutSite_seg;
            int dmax0L = hq_LayoutSite_dmax0L;
            int dmax0R = hq_LayoutSite_dmax0R;
            int i = hq_LayoutSite_i;
            if (i == n0 && siteShape != SHAPE_STRAIGHT)
            {
                // seg 0 is done: its lane ends where the next segment (or the cul-de-sac) begins
                FinishSegment(0, offL, offR, lastL, lastR);
                laneEnd0 = laneLen[0];
                for (int q = 0; q < nPlots; q++)
                {
                    if (plotSide[q] < 0) { if (plotDepth[q] > dmax0L) dmax0L = plotDepth[q]; }
                    else if (plotDepth[q] > dmax0R) dmax0R = plotDepth[q];
                }
                seg = 1;
                if (siteShape == SHAPE_CUL) { laneRot[1] = 1; laneOX[1] = CO; laneOY[1] = laneEnd0; }
                else if (siteShape == SHAPE_SQUARE) { laneRot[1] = 1; laneOX[1] = CO; laneOY[1] = laneEnd0 + 1; }
                else if (siteShape == SHAPE_U)
                {
                    // the connector runs along the first lane's end behind its right-hand plots; the back lane
                    // comes down beside them, its plots on its far side
                    int gap = dmax0R + 1;
                    laneRot[1] = 1; laneOX[1] = CO; laneOY[1] = laneEnd0; laneLen[1] = gap + 1;
                    laneRot[2] = 2; laneOX[2] = CO + gap; laneOY[2] = laneEnd0;
                    seg = 2;
                }
                else { laneRot[1] = siteShape == SHAPE_T || siteShape == SHAPE_CROSS ? 1 : bendDir; laneOX[1] = CO; laneOY[1] = laneEnd0; }
                nLane = seg + 1;
                if (siteShape == SHAPE_U) { offL = 1; offR = 1; }
                else StartSegment(1, dmax0L, dmax0R, out offL, out offR);
                // the square: the side arms start past the square on its far side
                if (siteShape == SHAPE_SQUARE) { if (offL == 1) offL = 2; if (offR == 1) offR = 2; }
                touchL = false; touchR = false; lastL = -1; lastR = -1;
            }
            if ((siteShape == SHAPE_T || siteShape == SHAPE_SQUARE) && seg == 1 && i == n0 + (nk - n0) / 2
                || siteShape == SHAPE_CROSS && seg == 1 && i == n0 + perArm)
            {
                FinishSegment(1, offL, offR, lastL, lastR);
                CrossFar(ref crossFar);
                seg = 2; nLane = 3;
                laneRot[2] = 3; laneOX[2] = CO; laneOY[2] = laneOY[1];
                StartSegment(2, dmax0L, dmax0R, out offL, out offR);
                if (siteShape == SHAPE_SQUARE) { if (offL == 1) offL = 2; if (offR == 1) offR = 2; }
                touchL = false; touchR = false; lastL = -1; lastR = -1;
            }
            if (siteShape == SHAPE_CROSS && seg == 2 && i == n0 + 2 * perArm)
            {
                // the crossroads' fourth arm runs straight on, past the side arms' plots
                FinishSegment(2, offL, offR, lastL, lastR);
                CrossFar(ref crossFar);
                seg = 3; nLane = 4;
                laneRot[3] = 0; laneOX[3] = CO; laneOY[3] = laneEnd0;
                offL = crossFar + 1; offR = crossFar + 1;
                touchL = false; touchR = false; lastL = -1; lastR = -1;
            }
            int k = nPlots;
            plotSeg[k] = seg;
            bool park = kinds[i] == GenData.Z_PARK || kinds[i] == GenData.Z_ZOO;   // no building (revision 12: the zoo too)
            hq_LayoutSite_kinds = kinds;
            hq_LayoutSite_nk = nk;
            hq_LayoutSite_bendDir = bendDir;
            hq_LayoutSite_n0 = n0;
            hq_LayoutSite_perArm = perArm;
            hq_LayoutSite_crossFar = crossFar;
            hq_LayoutSite_offL = offL;
            hq_LayoutSite_offR = offR;
            hq_LayoutSite_touchL = touchL;
            hq_LayoutSite_touchR = touchR;
            hq_LayoutSite_lastL = lastL;
            hq_LayoutSite_lastR = lastR;
            hq_LayoutSite_seg = seg;
            hq_LayoutSite_dmax0L = dmax0L;
            hq_LayoutSite_dmax0R = dmax0R;
            hq_LayoutSite_k = k;
            hq_LayoutSite_park = park;
        }
        if (planPhase == 1)
        {
            GenRng rng = hq_LayoutSite_rng;
            int[] kinds = hq_LayoutSite_kinds;
            int nk = hq_LayoutSite_nk;
            int i = hq_LayoutSite_i;
            bool park = hq_LayoutSite_park;
            int p = park ? -1 : PickPlan(kinds[i], ref rng);
            if (p < 0 && !park) { hq_LayoutSite_rng = rng; hq_LayoutSite_kinds = kinds; hq_LayoutSite_nk = nk; hq_LayoutSite_park = park; hq_LayoutSite_p = p; PhEnd(); return; }
            // evolution step 3: one plot's building takes another plan of its kind (the main draw stays as it was,
            // so the other plots keep theirs)
            hq_LayoutSite_sel1 = 0;
            if (!park && evo >= 3 && i == (int)(GenRng.Hash32(seed, 0xE70u, 3u, 0u, (uint)evoVar) >> 1) % nk) hq_LayoutSite_sel1 = 1;
            hq_LayoutSite_rng = rng;
            hq_LayoutSite_kinds = kinds;
            hq_LayoutSite_nk = nk;
            hq_LayoutSite_park = park;
            hq_LayoutSite_p = p;
        }
        if (planPhase == 2)
        {
            if (hq_LayoutSite_sel1 == 1)
            {
                GenRng alt = Rng(ST_LAYOUT, 7, 3);
                hq_LayoutSite_alt = alt;
            }
        }
        if (planPhase == 3 && PhLoop(3, 100000))
        {
            if (!(hq_LayoutSite_sel1 == 1)) PhBreak();
            else
            {
                int[] kinds = hq_LayoutSite_kinds;
                int i = hq_LayoutSite_i;
                int p = hq_LayoutSite_p;
                GenRng alt = hq_LayoutSite_alt;
                int tries = planIter;
                if (!(tries < 8)) PhBreak();
                else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it
                {
                    int q = PickPlan(kinds[i], ref alt);
                    if (q >= 0 && q != p) { p = q; { PhBreak(); break; } }
                }
                hq_LayoutSite_kinds = kinds;
                hq_LayoutSite_p = p;
                hq_LayoutSite_alt = alt;
            }
        }
        if (planPhase == 4)
        {
            GenRng rng = hq_LayoutSite_rng;
            int[] kinds = hq_LayoutSite_kinds;
            int offL = hq_LayoutSite_offL;
            int offR = hq_LayoutSite_offR;
            bool touchL = hq_LayoutSite_touchL;
            bool touchR = hq_LayoutSite_touchR;
            int lastL = hq_LayoutSite_lastL;
            int lastR = hq_LayoutSite_lastR;
            int seg = hq_LayoutSite_seg;
            int i = hq_LayoutSite_i;
            int k = hq_LayoutSite_k;
            bool park = hq_LayoutSite_park;
            int p = hq_LayoutSite_p;
            nPlots++;
            plotZone[k] = kinds[i]; plotPlan[k] = p; plotMir[k] = rng.Chance(500) ? 1 : 0;
            int side = offL < offR ? -1 : (offR < offL ? 1 : (rng.Chance(500) ? -1 : 1));
            if (siteShape == SHAPE_CUL && seg == 1) side = -1;   // the cul-de-sac's plot lies across the lane's end
            if (siteShape == SHAPE_U && seg == 2) side = -1;     // the back lane's plots stand on its far side
            plotSide[k] = side;
            // a park has no building: its plot is the whole of it, as if a building of its size stood on it
            int w = park ? GenData.SizeParkAlong[size] : PlW(p) * PS, d = park ? GenData.SizeParkDeep[size] : PlD(p) * PS;
            if (kinds[i] == GenData.Z_ZOO) { w = 3; d = GenData.SizeZooDeep[size]; }
            plotBodyD[k] = d;
            // front garden 1 cell (2 now and then on L and XL), back garden 1 or 2 cells deep (2 or 3 on L and XL)
            if (kinds[i] == GenData.Z_HOUSE) { plotSet[k] = GenData.SizeHouseFront[size] + (GenData.SizeHouseFront2[size] > 0 && rng.Chance(GenData.SizeHouseFront2[size]) ? 1 : 0); plotBack[k] = GenData.SizeHouseBack[size] + (rng.Chance(GenData.SizeHouseBackExtra[size]) ? 1 : 0); }
            else if (kinds[i] == GenData.Z_DINER || kinds[i] == GenData.Z_SHOP || kinds[i] == GenData.Z_GARAGE) { plotSet[k] = 1; plotBack[k] = 1; }
            else if (kinds[i] == GenData.Z_FAIR) { plotSet[k] = 1; plotBack[k] = 1; }   // the midway, the rides behind
            else if (kinds[i] == GenData.Z_CHAPEL) { plotSet[k] = 1; plotBack[k] = 1; } // the churchyard, the graveyard behind
            else if (kinds[i] == GenData.Z_BARN) { plotSet[k] = 1; plotBack[k] = 1; }   // the farmyard, the field behind
            else if (kinds[i] == GenData.Z_CABIN) { plotSet[k] = 1; plotBack[k] = 1; }  // the porch, the campground behind
            else if (park) { plotSet[k] = 0; plotBack[k] = 0; }
            else { plotSet[k] = GenData.SizeWarehouseFront[size]; plotBack[k] = GenData.SizeWarehouseBack2[size] > 0 && rng.Chance(GenData.SizeWarehouseBack2[size]) ? 2 : 1; }
            plotSL[k] = park ? 0 : (rng.Chance(GenData.SizeSideYard[size]) ? 1 : 0); plotSR[k] = park ? 0 : (rng.Chance(GenData.SizeSideYard[size]) ? 1 : 0);
            // a farm's paddock lies beside the barn
            if ((kinds[i] == GenData.Z_BARN || kinds[i] == GenData.Z_CABIN || kinds[i] == GenData.Z_FAIR || kinds[i] == GenData.Z_CHAPEL) && plotSL[k] + plotSR[k] == 0) { if (rng.Chance(500)) plotSL[k] = 1; else plotSR[k] = 1; }   // the paddock, the wood yard
            // a back door in a side wall opens onto this plot's own side yard
            int bd = park ? 0 : PlBack(p) & 3;
            if (bd == 1 || bd == 3) { if ((bd == 1) != (plotMir[k] != 0)) plotSR[k] = 1; else plotSL[k] = 1; }
            // a building never touches the map's edge or its neighbour's building (report 16, T3, T12)
            int off = side < 0 ? offL : offR, prev = side < 0 ? lastL : lastR;
            if ((off == 1 && !EdgeOK) || (side < 0 ? touchL : touchR) || (seg > 0 && prev < 0)) plotSL[k] = 1;
            if (park) plotSL[k] = 0;
            if (prev >= 0 && !park)
            {
                // nor stands beside the open end of a shallower neighbour: this plot's side yard, or a deeper garden
                // beside the neighbour's building
                if (plotSL[k] == 0 && plotDepth[prev] < plotSet[k] + d) plotSL[k] = 1;
                int prevEnd = plotSet[prev] + plotBodyD[prev];
                if (plotSR[prev] == 0 && plotSet[k] + d + plotBack[k] < prevEnd) plotBack[k] = prevEnd - plotSet[k] - d;
            }
            // revision 10: from M up a farm's paddock is two cells wide (on the side that has the side yard)
            if (kinds[i] == GenData.Z_BARN && size >= 1 && size != 4 /* XS, GenData.SIZE_XS */) { if (plotSR[k] >= 1) plotSR[k] = 2; else plotSL[k] = 2; }
            plotY0[k] = off;
            plotAlong[k] = w + plotSL[k] + plotSR[k];
            plotDepth[k] = plotSet[k] + d + plotBack[k];
            if (side < 0) { offL += plotAlong[k]; touchL = plotSR[k] == 0; lastL = k; }
            else { offR += plotAlong[k]; touchR = plotSR[k] == 0; lastR = k; }
            hq_LayoutSite_rng = rng;
            hq_LayoutSite_kinds = kinds;
            hq_LayoutSite_offL = offL;
            hq_LayoutSite_offR = offR;
            hq_LayoutSite_touchL = touchL;
            hq_LayoutSite_touchR = touchR;
            hq_LayoutSite_lastL = lastL;
            hq_LayoutSite_lastR = lastR;
            hq_LayoutSite_seg = seg;
            hq_LayoutSite_k = k;
            hq_LayoutSite_park = park;
            hq_LayoutSite_p = p;
        }
    }

    void LayoutSite_L2()
    {
        planMax = 0;
        if (planPhase == 0 && PhLoop(0, 100000))
        {
            GenRng rng = hq_LayoutSite_rng;
            int k = hq_LayoutSite_k;
            if (planIter == 0) { hq_StampPlot_k = k; hq_StampPlot_rng = rng; PhSubReset(); }
            PhPush(); StampPlot_Body(); bool fin = PhAdvance(); PhPop();
            if (fin) { rng = hq_StampPlot_rng; PhBreak(); }
            hq_LayoutSite_rng = rng;
        }
    }


    GenRng layRng;

    // one of the plot kinds that are not houses, by weight, not one the map has already
    int DrawPlotKind(int[] kinds, int nk, ref GenRng rng)
    {
        int total = 0;
        for (int i = 0; i < GenData.PlotPoolZone.Length; i++) if (KindAllowed(kinds, nk, GenData.PlotPoolZone[i])) total += GenData.PlotPoolWeight[i];
        int roll = rng.Range(total > 0 ? total : 1);
        for (int i = 0; i < GenData.PlotPoolZone.Length; i++)
        {
            if (!KindAllowed(kinds, nk, GenData.PlotPoolZone[i])) continue;
            if (roll < GenData.PlotPoolWeight[i]) return GenData.PlotPoolZone[i];
            roll -= GenData.PlotPoolWeight[i];
        }
        return GenData.Z_HOUSE;
    }

    static bool Has(int[] a, int n, int v) { for (int i = 0; i < n; i++) if (a[i] == v) return true; return false; }

    // a kind the map has not drawn yet and its theme allows (revision 12)
    bool KindAllowed(int[] kinds, int nk, int z)
    {
        if (Has(kinds, nk, z)) return false;
        if (mapTheme == GenData.THEME_NONE) return true;
        int[] ok = GenData.ThemeKinds[mapTheme];
        for (int i = 0; i < ok.Length; i++) if (ok[i] == z) return true;
        return false;
    }

    void StampPlot(int k, ref GenRng rng)
    {
        hq_StampPlot_k = k;
        hq_StampPlot_rng = rng;
        PhSubReset();
        bool fin = false;
        while (!fin) { PhPush(); StampPlot_Body(); fin = PhAdvance(); PhPop(); }
        rng = hq_StampPlot_rng;
    }

    // StampPlot in phases, a step a call (autophase4); StampPlot() above runs them to the end
    void StampPlot_Body()
    {
        planMax = 2;
        if (planPhase == 0)
        {
            int k = hq_StampPlot_k;
            GenRng rng = hq_StampPlot_rng;
            int p = plotPlan[k], zone = plotZone[k];
            if (p < 0 && zone == GenData.Z_ZOO)
            {
                // revision 12, the zoo: the plaza along the lane and the keepers' walk up the middle are one outdoor
                // instance (TypeYard types them); every other cell is an enclosure, an instance of its own, so a fence
                // stands round it, with a gate onto the walk. One of them is the keepers' yard.
                plotInstB[k] = -1;
                int zi = NewInst(GenData.Z_LAWN);
                plotInstY[k] = zi;
                int[] enc = { GenData.R_ENC_FARM, GenData.R_ENC_SAVANNA, GenData.R_ENC_ARCTIC, GenData.R_ENC_WOODS, GenData.R_ENC_JUNGLE, GenData.R_AVIARY };
                Shuffle(enc, enc.Length, ref rng);
                // the enclosure columns either side of the walk; a side yard the site added (the last plot's, towards the
                // wood) is more of the walk
                int walkU = plotSL[k] + 1;
                int slots = 2 * (plotDepth[k] - 1), keeper = rng.Range(slots), ne = 0, slot = 0;
                for (int u = 0; u < plotAlong[k]; u++)
                    for (int v = 0; v < plotDepth[k]; v++)
                    {
                        int x, y;
                        PlotXY(k, u, v, out x, out y);
                        if (v == 0 || u == walkU || u < walkU - 1 || u > walkU + 1) { Put(x, y, GenData.Z_LAWN, zi, -1, v == 0 ? PL_FRONT : PL_BACK, k); continue; }
                        int ei = NewInst(GenData.Z_LAWN);
                        Put(x, y, GenData.Z_LAWN, ei, 0, PL_BACK, k);
                        siteType[ei * 26] = slot++ == keeper && slots >= 4 ? GenData.R_KEEPER : enc[ne++ % enc.Length]; sitePlace[ei * 26] = PL_BACK;
                        // the gate onto the walk
                        int wx, wy;
                        PlotXY(k, walkU, v, out wx, out wy);
                        for (int dd = 0; dd < 4; dd++) if (x + DX[dd] == wx && y + DZ[dd] == wy) Sd(x, y, dd, SD_OPEN);
                    }
                TypeYard(k, ref rng);
                { hq_StampPlot_k = k; hq_StampPlot_rng = rng; hq_StampPlot_p = p; hq_StampPlot_zone = zone; PhEnd(); return; }
            }
            hq_StampPlot_k = k;
            hq_StampPlot_rng = rng;
            hq_StampPlot_p = p;
            hq_StampPlot_zone = zone;
        }
        if (planPhase == 1)
        {
            int k = hq_StampPlot_k;
            GenRng rng = hq_StampPlot_rng;
            int p = hq_StampPlot_p;
            int zone = hq_StampPlot_zone;
            if (p < 0)
            {
                // a park: no building, every cell of the plot is the park (TypeYard)
                plotInstB[k] = -1;
                int pi = NewInst(GenData.Z_LAWN);
                plotInstY[k] = pi;
                for (int u = 0; u < plotAlong[k]; u++)
                    for (int v = 0; v < plotDepth[k]; v++)
                    {
                        int x, y;
                        PlotXY(k, u, v, out x, out y);
                        Put(x, y, GenData.Z_LAWN, pi, -1, v == 0 ? PL_FRONT : PL_BACK, k);
                    }
                TypeYard(k, ref rng);
                { hq_StampPlot_k = k; hq_StampPlot_rng = rng; hq_StampPlot_p = p; hq_StampPlot_zone = zone; PhEnd(); return; }
            }
            int bi = NewInst(zone), yi = NewInst(GenData.Z_LAWN);
            plotInstB[k] = bi; plotInstY[k] = yi;
            for (int r = 0; r < PlRooms(p); r++) { siteType[bi * 26 + r] = PlRole(p, r); sitePlace[bi * 26 + r] = PL_BUILDING; }
            if (zone == GenData.Z_HOUSE)
            {
                // revision 11: a kids' room is a kids' room, a playroom or a teen's room; a second bedroom is now and then
                // a teen's room or the box room (the first bedroom stays, so a house always has one)
                int beds = 0;
                for (int r = 0; r < PlRooms(p); r++)
                {
                    int t = PlRole(p, r), roll = rng.Range(1000);
                    if (t == GenData.R_KIDS) siteType[bi * 26 + r] = roll < 350 ? GenData.R_KIDS : (roll < 700 ? GenData.R_PLAYROOM : GenData.R_TEEN);
                    else if (t == GenData.R_BEDROOM && beds++ > 0) siteType[bi * 26 + r] = roll < 220 ? GenData.R_TEEN : (roll < 440 ? GenData.R_BOXROOM : GenData.R_BEDROOM);
                }
            }
            int w = PlW(p) * PS, d = PlD(p) * PS;
            for (int u = 0; u < plotAlong[k]; u++)
                for (int v = 0; v < plotDepth[k]; v++)
                {
                    int x, y;
                    PlotXY(k, u, v, out x, out y);
                    int px = u - plotSL[k], py = v - plotSet[k];
                    if (plotMir[k] != 0) px = w - 1 - px;
                    int room = (px >= 0 && px < w && py >= 0 && py < d) ? PlCell(p, px / PS, py / PS) : -1;
                    if (room >= 0) { Put(x, y, zone, bi, room, PL_BUILDING, k); continue; }
                    int place = v < plotSet[k] ? PL_FRONT : (v >= plotSet[k] + d ? PL_BACK : ((u < plotSL[k] || u >= plotSL[k] + w) ? PL_SIDE : PL_BACK));
                    Put(x, y, GenData.Z_LAWN, yi, -1, place, k);
                }
            // the plan's doors: interior ones, then the front door and the back (service) door
            for (int i = 0; i < PlDoors(p); i++) PlanDoor(k, PlDoor(p, i), SD_DOOR);
            PlanDoor(k, PlFront(p), SD_FRONT);
            PlanDoor(k, PlBack(p), SD_BACK);
            hq_StampPlot_k = k;
            hq_StampPlot_rng = rng;
            hq_StampPlot_p = p;
            hq_StampPlot_zone = zone;
        }
        if (planPhase == 2)
        {
            int k = hq_StampPlot_k;
            GenRng rng = hq_StampPlot_rng;
            TypeYard(k, ref rng);
            hq_StampPlot_k = k;
            hq_StampPlot_rng = rng;
        }
    }

    int[] plotBodyD = new int[16];   // the depth of each plot's body (its building, or all of a park)

    void PlanDoor(int k, int code, int kind)
    {
        int p = plotPlan[k], w = PlW(p);
        int cell = code >> 2, dir = code & 3;
        int u, v, x, y;
        int cx = cell % w, cy = cell / w, sx = cx * PS, sy = cy * PS, j = PS / 2;
        if (dir == 0) { sx += j; sy += PS - 1; } else if (dir == 1) { sx += PS - 1; sy += j; } else if (dir == 2) sx += j; else sy += j;
        PlanUV(k, sx, sy, out u, out v);
        PlotXY(k, u, v, out x, out y);
        Sd(x, y, PlotDir(k, dir), kind);
    }

    // the canvas cell outside door kind (SD_FRONT or SD_BACK) of plot k
    bool DoorOutside(int k, int kind, out int ox, out int oy, out int dir)
    {
        ox = oy = dir = -1;
        for (int i = nSd - 1; i >= 0; i--)
        {
            if (sdKind[i] != kind || cvPlot[sdCell[i]] != k) continue;
            dir = sdDir[i];
            ox = sdCell[i] % CV + DX[dir]; oy = sdCell[i] / CV + DZ[dir];
            return true;
        }
        return false;
    }

    // The outdoor cells of a plot get their types from where they lie (report 16, section 7):
    //   house      front and sides: lawn; the cell at the back door: patio; the back row: garden beds along the
    //              fence (with a back garden two or more deep); one cell clear of the walls: a fire pit
    //              (sometimes); the rest of the back garden: lawn
    //   diner      front: the forecourt (paved, the patio plan); the service door's cell and the back: the yard;
    //              sides: lawn
    //   warehouse  front: a lawn verge; sides and back: the yard
    // Each connected piece of one type is a room.
    void TypeYard(int k, ref GenRng rng)
    {
        int zone = plotZone[k], yi = plotInstY[k];
        int bx, by, bdir;
        DoorOutside(k, SD_BACK, out bx, out by, out bdir);
        int[] type = cand;   // per plot cell, scratch (u * 16 + v)
        // a park (revision 10): its lawn along the lane; behind it, side by side in a shuffled order, a skate corner, a
        // pond and a cafe terrace: one column each, two for the first (and the second) when the park is 4 (5) wide
        int[] feat = { GenData.R_PLAYGROUND, GenData.R_POND, GenData.R_TERRACE };
        if (zone == GenData.Z_PARK) Shuffle(feat, 3, ref rng);
        for (int u = 0; u < plotAlong[k]; u++)
            for (int v = 0; v < plotDepth[k]; v++)
            {
                int x, y;
                PlotXY(k, u, v, out x, out y);
                int c = y * CV + x, t = 0;
                if (cvInst[c] == yi)
                {
                    int pl = cvPlace[c];
                    bool atBack = x == bx && y == by;
                    if (zone == GenData.Z_HOUSE)
                    {
                        if (atBack) t = GenData.R_PATIO;
                        else if (pl == PL_BACK && v == plotDepth[k] - 1 && plotBack[k] >= 2) t = GenData.R_GARDEN;
                        else t = GenData.R_LAWN_OPEN;
                    }
                    else if (zone == GenData.Z_DINER) t = pl == PL_FRONT ? GenData.R_PATIO : ((atBack || pl == PL_BACK) ? GenData.R_YARD : GenData.R_LAWN_OPEN);
                    else if (zone == GenData.Z_SHOP || zone == GenData.Z_GARAGE) t = pl == PL_FRONT ? GenData.R_FORECOURT : ((atBack || pl == PL_BACK) ? GenData.R_YARD : GenData.R_LAWN_OPEN);
                    else if (zone == GenData.Z_BARN) t = pl == PL_FRONT ? GenData.R_FARMYARD : (pl == PL_SIDE ? GenData.R_PADDOCK : GenData.R_FIELD);
                    else if (zone == GenData.Z_CABIN) t = pl == PL_FRONT ? GenData.R_PORCH : (pl == PL_SIDE ? GenData.R_WOODYARD : GenData.R_CAMPSITE);
                    else if (zone == GenData.Z_ZOO) t = v == 0 ? GenData.R_ZOO_PLAZA : GenData.R_ZOO_WALK;
                    else if (zone == GenData.Z_FAIR) t = pl == PL_FRONT ? GenData.R_MIDWAY : GenData.R_RIDES;
                    else if (zone == GenData.Z_CHAPEL) t = pl == PL_FRONT ? GenData.R_CHURCHYARD : GenData.R_GRAVEYARD;
                    else if (zone == GenData.Z_PARK)
                    {
                        int a = plotAlong[k], f = a <= 3 ? u : (a == 4 ? (u < 2 ? 0 : u - 1) : (u < 4 ? u / 2 : 2));
                        t = v == 0 ? GenData.R_PARK : feat[f < 3 ? f : 2];
                    }
                    else t = pl == PL_FRONT ? GenData.R_LAWN_OPEN : GenData.R_YARD;
                }
                type[u * 16 + v] = t;
            }
        // a fire pit in a house's back garden: a lawn cell with no building around it (8 neighbours)
        if (zone == GenData.Z_HOUSE && (plotBack[k] >= 3 || rng.Chance(350)))
        {
            int n = 0;
            for (int u = 0; u < plotAlong[k]; u++)
                for (int v = plotSet[k]; v < plotDepth[k]; v++)
                {
                    if (type[u * 16 + v] != GenData.R_LAWN_OPEN) continue;
                    int x, y;
                    PlotXY(k, u, v, out x, out y);
                    if (cvPlace[y * CV + x] != PL_BACK) continue;
                    bool clear = true;
                    for (int dz = -1; dz <= 1 && clear; dz++)
                        for (int dx = -1; dx <= 1 && clear; dx++)
                            if (GenData.IsBuilding(CvZone(x + dx, y + dz))) clear = false;
                    if (clear) cand[2048 + n++] = u * 16 + v;
                }
            if (n > 0) type[cand[2048 + rng.Range(n)]] = GenData.R_FIREPIT;
        }
        LetterRooms(k, yi, type);
    }

    // connected pieces of one type: one room letter each
    void LetterRooms(int k, int yi, int[] type)
    {
        int letters = 0;
        for (int u = 0; u < plotAlong[k]; u++)
            for (int v = 0; v < plotDepth[k]; v++)
            {
                int x, y;
                PlotXY(k, u, v, out x, out y);
                int c = y * CV + x;
                if (cvInst[c] != yi || cvRoom[c] >= 0 || letters >= 26) continue;
                int t = type[u * 16 + v], l = letters++;
                siteType[yi * 26 + l] = t; sitePlace[yi * 26 + l] = cvPlace[c];
                // flood over plot cells of the same type, at most ROOM_CELLS_MAX of them (layout 14: a big lawn is two)
                int qh = 0, qt = 0;
                cvRoom[c] = l; cand[1024 + qt++] = u * 16 + v;
                while (qh < qt && qt < ROOM_CELLS_MAX)
                {
                    int uv = cand[1024 + qh++], uu = uv / 16, vv = uv % 16;
                    for (int d = 0; d < 4 && qt < ROOM_CELLS_MAX; d++)
                    {
                        int nu = uu + (d == 0 ? 1 : (d == 2 ? -1 : 0)), nv = vv + (d == 1 ? 1 : (d == 3 ? -1 : 0));
                        if (nu < 0 || nv < 0 || nu >= plotAlong[k] || nv >= plotDepth[k] || type[nu * 16 + nv] != t) continue;
                        int nx, ny;
                        PlotXY(k, nu, nv, out nx, out ny);
                        int nc = ny * CV + nx;
                        if (cvInst[nc] != yi || cvRoom[nc] >= 0) continue;
                        cvRoom[nc] = l; cand[1024 + qt++] = nu * 16 + nv;
                    }
                }
            }
    }

    // The wood (report 16, section 7): it grows from the lane's end, one cell at a time, until the map has its
    // ground cells (never fewer than the size's minimum): beside the shorter row of plots, behind the plots, around
    // the lane's end. A cell joins next to the wood, never next to a building, and preferably where the wood is
    // already on two or more sides, so it stays one compact piece; the random part of that choice gives the
    // uneven edge.
    bool LayoutWood(int offL, int offR, ref GenRng rng)
    {
        siteInstWood = NewInst(GenData.Z_FOREST);
        int wi = siteInstWood;
        int dmaxL = 2, dmaxR = 2;
        for (int k = 0; k < nPlots; k++)
        {
            if (plotSide[k] < 0) { if (plotDepth[k] > dmaxL) dmaxL = plotDepth[k]; }
            else if (plotDepth[k] > dmaxR) dmaxR = plotDepth[k];
        }
        int ground = 0;
        for (int i = 0; i < CV * CV; i++) if (cvZone[i] != GenData.Z_NONE && cvZone[i] != GenData.Z_RELEASE) ground++;
        int need = GenData.SizeGroundCells[size] - ground;
        if (need < GenData.SizeWoodMin[size]) need = GenData.SizeWoodMin[size];
        // the box the wood may take: as wide as the plots and two cells more, up to three rows past the lane's end
        int bx0 = CO - dmaxL - 2, bx1 = CO + dmaxR + 2, by0 = 1, by1 = laneEnd + 2;
        int sx = CO, sy = laneEnd, intoDir = 0, intoX = CO, intoY = laneEnd - 1;
        if (siteShape != SHAPE_STRAIGHT)
        {
            // the box: everything laid out so far, two cells more
            bx0 = CV; by0 = CV; bx1 = -1; by1 = -1;
            for (int i = 0; i < CV * CV; i++)
            {
                if (cvZone[i] == GenData.Z_NONE) continue;
                int x = i % CV, y = i / CV;
                if (x < bx0) bx0 = x; if (x > bx1) bx1 = x; if (y < by0) by0 = y; if (y > by1) by1 = y;
            }
            bx0 = bx0 - 2 < 0 ? 0 : bx0 - 2; by0 = by0 < 1 ? 1 : by0; bx1 = bx1 + 2 > CV - 1 ? CV - 1 : bx1 + 2; by1 = by1 + 2 > CV - 1 ? CV - 1 : by1 + 2;
            if (siteShape == SHAPE_CUL)
            {
                // beside the lane's last cell, on whichever side has room
                sx = -1;
                int ek = nPlots - 1, hx0 = laneOX[1], hx1 = laneOX[1] + plotAlong[ek] - 1;
                if (Free(hx1 + 1, laneEnd) && !BuildingNear(hx1 + 1, laneEnd)) { sx = hx1 + 1; sy = laneEnd; intoX = hx1; intoY = laneEnd; intoDir = 1; }
                else if (Free(hx0 - 1, laneEnd) && !BuildingNear(hx0 - 1, laneEnd)) { sx = hx0 - 1; sy = laneEnd; intoX = hx0; intoY = laneEnd; intoDir = 3; }
                if (sx < 0) return false;
            }
            else
            {
                // at the end of the last segment's lane
                int s = nLane - 1;
                Canon(s, CO, laneLen[s], out sx, out sy);
                Canon(s, CO, laneLen[s] - 1, out intoX, out intoY);
                intoDir = LaneDir(s, 0);
                if (!Free(sx, sy)) return false;
            }
        }
        Put(sx, sy, GenData.Z_FOREST, wi, -1, PL_WOOD, -1);
        int wood = 1;
        while (wood < need)
        {
            int best = -1, bestScore = -1;
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    if (!Free(x, y) || BuildingNear(x, y)) continue;
                    int n = 0, n8 = 0;
                    for (int d = 0; d < 4; d++) if (CvZone(x + DX[d], y + DZ[d]) == GenData.Z_FOREST) n++;
                    if (n == 0) continue;
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (CvZone(x + dx, y + dz) == GenData.Z_FOREST) n8++;
                    // next to the lane on the shorter side first: the lane is not left with an open side
                    bool byLane = siteShape == SHAPE_STRAIGHT ? (x == CO - 1 || x == CO + 1) && y < laneEnd : NextToLane(x, y);
                    int score = n * 20 + n8 * 6 + (byLane ? 40 : 0) + rng.Range(30);
                    if (score > bestScore) { bestScore = score; best = y * CV + x; }
                }
            if (best < 0) break;
            Put(best % CV, best / CV, GenData.Z_FOREST, wi, -1, PL_WOOD, -1);
            wood++;
        }
        Sd(intoX, intoY, intoDir, SD_OPEN);   // the lane runs on into the wood
        // a second way in from the lane where the wood lies beside it
        if (siteShape == SHAPE_STRAIGHT)
            for (int side = -1; side <= 1; side += 2)
            {
                int off = side < 0 ? offL : offR, mid = (off + laneEnd) / 2;
                if (laneEnd - off >= 2 && CvZone(CO + side, mid) == GenData.Z_FOREST) Sd(CO, mid, side < 0 ? 3 : 1, SD_OPEN);
            }
        WoodRooms(ref rng);
        return wood >= GenData.SizeWoodMin[size];
    }

    bool NextToLane(int x, int y)
    {
        for (int d = 0; d < 4; d++)
        {
            int nx = x + DX[d], ny = y + DZ[d];
            if (nx >= 0 && ny >= 0 && nx < CV && ny < CV && cvPlace[ny * CV + nx] == PL_LANE && cvZone[ny * CV + nx] == GenData.Z_LAWN) return true;
        }
        return false;
    }

    bool BuildingNear(int x, int y)
    {
        for (int d = 0; d < 4; d++) if (GenData.IsBuilding(CvZone(x + DX[d], y + DZ[d]))) return true;
        return false;
    }

    // The wood as rooms: a camp in a clearing of two or three cells deep inside it (most woods of 8 cells or
    // more), the rest dense forest in pieces of up to 5 cells; a piece of 1 or 2 cells joins its neighbour.
    void WoodRooms(ref GenRng rng)
    {
        int wi = siteInstWood, n = 0, letters = 0;
        for (int i = 0; i < CV * CV; i++) if (cvInst[i] == wi) n++;
        if (n >= 8 && rng.Chance(700))
        {
            // the cell with the most wood around it
            int best = -1, bestN = -1;
            for (int i = 0; i < CV * CV; i++)
            {
                if (cvInst[i] != wi) continue;
                int x = i % CV, y = i / CV, m = 0;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (x + dx >= 0 && y + dz >= 0 && x + dx < CV && y + dz < CV && cvInst[(y + dz) * CV + x + dx] == wi) m++;
                m = m * 8 + rng.Range(8);
                if (m > bestN) { bestN = m; best = i; }
            }
            GrowRoom(best, wi, letters, 2 + rng.Range(2));
            siteType[wi * 26 + letters] = GenData.R_FOREST_CLEARING; sitePlace[wi * 26 + letters] = PL_WOOD;
            letters++;
        }
        for (int i = 0; i < CV * CV && letters < 26; i++)
        {
            if (cvInst[i] != wi || cvRoom[i] >= 0) continue;
            int got = GrowRoom(i, wi, letters, 5);
            if (got <= 2)
            {
                // too small to be a forest of its own: it joins a neighbouring piece of dense forest
                int join = -1;
                for (int j = 0; j < CV * CV && join < 0; j++)
                {
                    if (cvInst[j] != wi || cvRoom[j] != letters) continue;
                    for (int d = 0; d < 4 && join < 0; d++)
                    {
                        int x = j % CV + DX[d], y = j / CV + DZ[d];
                        if (x < 0 || y < 0 || x >= CV || y >= CV) continue;
                        int o = cvRoom[y * CV + x];
                        if (cvInst[y * CV + x] == wi && o >= 0 && o != letters && siteType[wi * 26 + o] == GenData.R_FOREST_DENSE) join = o;
                    }
                }
                if (join >= 0)
                {
                    for (int j = 0; j < CV * CV; j++) if (cvInst[j] == wi && cvRoom[j] == letters) cvRoom[j] = join;
                    continue;
                }
            }
            siteType[wi * 26 + letters] = GenData.R_FOREST_DENSE; sitePlace[wi * 26 + letters] = PL_WOOD;
            letters++;
        }
    }

    // Layout 14 (Cor: build freezes): the lane in rooms of at most six cells, from the release on. Furnishing a room
    // costs about its size times what goes in it, and the whole lane in one room took up to 0.6 s in one frame.
    public const int ROOM_CELLS_MAX = 6;

    void SplitLane()
    {
        int li = siteInstLane, letters = 0;
        for (int i = 0; i < CV * CV; i++) if (cvInst[i] == li) cvRoom[i] = -1;
        // the cells in order of their distance from the release, along the lane
        int qh = 0, qt = 0;
        int[] order = new int[CV * CV];
        bool[] seen = new bool[CV * CV];
        int s0 = 1 * CV + CO;
        if (cvInst[s0] != li) return;
        order[qt++] = s0; seen[s0] = true;
        while (qh < qt)
        {
            int c = order[qh++];
            for (int d = 0; d < 4; d++)
            {
                int x = c % CV + DX[d], y = c / CV + DZ[d];
                if (x < 0 || y < 0 || x >= CV || y >= CV) continue;
                int n = y * CV + x;
                if (seen[n] || cvInst[n] != li) continue;
                seen[n] = true; order[qt++] = n;
            }
        }
        for (int k = 0; k < qt && letters < 26; k++)
        {
            int c = order[k];
            if (cvRoom[c] >= 0) continue;
            GrowRoom(c, li, letters, ROOM_CELLS_MAX);
            siteType[li * 26 + letters] = GenData.R_LANE; sitePlace[li * 26 + letters] = PL_LANE;
            letters++;
        }
        for (int k = 0; k < qt; k++) if (cvRoom[order[k]] < 0) cvRoom[order[k]] = letters - 1;   // past 26 rooms: the last one
    }

    // breadth-first from a cell over unassigned cells of the instance, up to max cells: room letter l
    int GrowRoom(int start, int inst, int l, int max)
    {
        int qh = 0, qt = 0;
        cvRoom[start] = l; cand[qt++] = start;
        while (qh < qt && qt < max)
        {
            int c = cand[qh++];
            for (int d = 0; d < 4 && qt < max; d++)
            {
                int x = c % CV + DX[d], y = c / CV + DZ[d];
                if (x < 0 || y < 0 || x >= CV || y >= CV) continue;
                int n = y * CV + x;
                if (cvInst[n] != inst || cvRoom[n] >= 0) continue;
                cvRoom[n] = l; cand[qt++] = n;
            }
        }
        return qt;
    }

    void Gates(int k, ref GenRng rng)
    {
        int front = PlotDir(k, 2), back = (front + 2) & 3;
        if (plotZone[k] == GenData.Z_HOUSE)
        {
            // the garden gate where the front path meets the lane (none when the front door opens onto the lane)
            int fx, fy, fd;
            if (DoorOutside(k, SD_FRONT, out fx, out fy, out fd) && cvPlot[fy * CV + fx] == k)
            {
                int x = fx, y = fy;
                while (CvZone(x + DX[front], y + DZ[front]) == GenData.Z_LAWN && cvPlot[(y + DZ[front]) * CV + x + DX[front]] == k) { x += DX[front]; y += DZ[front]; }
                Sd(x, y, front, SD_OPEN);
            }
        }
        else
        {
            // a shop's front is open to the lane
            for (int u = 0; u < plotAlong[k]; u++) { int x, y; PlotXY(k, u, 0, out x, out y); if (cvZone[y * CV + x] == GenData.Z_LAWN) Sd(x, y, front, SD_OPEN); }
        }
        // a gate from the back garden or the yard into the wood
        if (rng.Chance(plotZone[k] == GenData.Z_HOUSE ? 500 : 650))
        {
            int n = 0;
            for (int u = 0; u < plotAlong[k]; u++)
            {
                int x, y;
                PlotXY(k, u, plotDepth[k] - 1, out x, out y);
                if (CvZone(x + DX[back], y + DZ[back]) == GenData.Z_FOREST && cvZone[y * CV + x] == GenData.Z_LAWN && cvInst[y * CV + x] == plotInstY[k]) cand[n++] = y * CV + x;
            }
            if (n > 0) { int c = cand[rng.Range(n)]; Sd(c % CV, c / CV, back, SD_OPEN); }
        }
    }

    // ---- the turn onto the outer grid -----------------------------------------------------------------------
    int siteRot, siteMir, siteMinX, siteMinY, siteCW, siteCH;

    void ToWorld(int x, int y, out int wx, out int wz)
    {
        int lx = x - siteMinX, ly = y - siteMinY;
        if (siteMir != 0) lx = siteCW - 1 - lx;
        if (siteRot == 0) { wx = lx; wz = ly; }
        else if (siteRot == 1) { wx = ly; wz = siteCW - 1 - lx; }
        else if (siteRot == 2) { wx = siteCW - 1 - lx; wz = siteCH - 1 - ly; }
        else { wx = siteCH - 1 - ly; wz = lx; }
    }

    int DirToWorld(int d)
    {
        int vx = DX[d], vy = DZ[d];
        if (siteMir != 0) vx = -vx;
        int wx, wz;
        if (siteRot == 0) { wx = vx; wz = vy; }
        else if (siteRot == 1) { wx = vy; wz = -vx; }
        else if (siteRot == 2) { wx = -vx; wz = -vy; }
        else { wx = -vy; wz = vx; }
        for (int k = 0; k < 4; k++) if (DX[k] == wx && DZ[k] == wz) return k;
        return 0;
    }

    bool PlaceSite(ref GenRng rng)
    {
        int x0 = CV, y0 = CV, x1 = -1, y1 = -1;
        for (int i = 0; i < CV * CV; i++)
        {
            if (cvZone[i] == GenData.Z_NONE) continue;
            int x = i % CV, y = i / CV;
            if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
        }
        siteMinX = x0; siteMinY = y0; siteCW = x1 - x0 + 1; siteCH = y1 - y0 + 1;
        siteRot = rng.Range(4); siteMir = rng.Chance(500) ? 1 : 0;
        int w = (siteRot & 1) == 0 ? siteCW : siteCH, h = (siteRot & 1) == 0 ? siteCH : siteCW;
        Alloc(w, h);
        for (int c = 0; c < W * H; c++) { cellInst[c] = -1; cellZone[c] = GenData.Z_NONE; cellRoom[c] = -1; cellPlot[c] = -1; }
        for (int i = 0; i < CV * CV; i++)
        {
            if (cvZone[i] == GenData.Z_NONE) continue;
            int wx, wz;
            ToWorld(i % CV, i / CV, out wx, out wz);
            int c = wz * W + wx;
            cellZone[c] = cvZone[i]; cellInst[c] = cvInst[i]; cellRoom[c] = cvRoom[i]; cellPlot[c] = cvPlot[i];
        }
        for (int k = 0; k < nSd; k++)
        {
            int wx, wz;
            ToWorld(sdCell[k] % CV, sdCell[k] / CV, out wx, out wz);
            sdCell[k] = wz * W + wx; sdDir[k] = DirToWorld(sdDir[k]);
        }
        {
            int wx, wz;
            ToWorld(CO, 0, out wx, out wz);
            releaseX = wx; releaseZ = wz; releaseDir = DirToWorld(0);
        }
        statGroundCells = 0;
        for (int c = 0; c < W * H; c++) if (cellZone[c] != GenData.Z_NONE && cellZone[c] != GenData.Z_RELEASE) statGroundCells++;
        AddLevels();   // generator 18: upper floors and basements, beside the map (MapGenLevels.cs)
        return true;
    }

    // =====================================================================================================
    // Walls, doors, windows, fences and gates from the plots
    // =====================================================================================================
    //   inside one building: the same room is open, two rooms have a wall, or the plan's door
    //   a building against anything: a wall, or the plan's front or back door; windows on outside walls
    //   one plot's outdoor rooms: open between them (the ground blends, G30)
    //   two plots, a plot and the lane or the wood, the release: a fence, or a gate (open)
    //   the map's edge: a fence (a wall for a building, which the layout never puts there)
    void BuildEdgesSite()
    {
        GenRng rng = Rng(ST_EDGES, 0, 0);
        for (int i = 0; i < edgeH.Length; i++) { edgeH[i] = GenData.E_NONE; edgeHOff[i] = 0; }
        for (int i = 0; i < edgeV.Length; i++) { edgeV[i] = GenData.E_NONE; edgeVOff[i] = 0; }
        for (int c = 0; c < W * H; c++)
        {
            int za = cellZone[c];
            if (za == GenData.Z_NONE) continue;
            int x = c % W, z = c / W;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DX[d], nz = z + DZ[d];
                int zb = ZoneAt(nx, nz), type;
                if (zb == GenData.Z_NONE) type = GenData.IsBuilding(za) ? GenData.E_WALL : GenData.E_FENCE;
                else
                {
                    int n = nz * W + nx;
                    if (cellInst[n] == cellInst[c]) type = (cellRoom[n] == cellRoom[c] || !GenData.IsBuilding(za)) ? GenData.E_NONE : GenData.E_WALL;
                    else if (GenData.IsBuilding(za) || GenData.IsBuilding(zb)) type = GenData.E_WALL;
                    else type = GenData.E_FENCE;
                }
                SetEdge(x, z, d, type, 0);
            }
        }
        for (int k = 0; k < nSd; k++)
        {
            int c = sdCell[k];
            if (sdKind[k] == SD_OPEN) SetEdge(c % W, c / W, sdDir[k], GenData.E_NONE, 0);
            else SetEdge(c % W, c / W, sdDir[k], GenData.E_DOOR, DoorOff(ref rng));
        }
        // windows: most outside walls of the rooms people live in (report 16, H7); every one of them has at least one
        for (int c = 0; c < W * H; c++)
        {
            int za = cellZone[c];
            if (!GenData.IsBuilding(za) || instLevel[cellInst[c]] < 0) continue;   // no windows in a basement
            int x = c % W, z = c / W, rt = roomType[cellRoom[c]];
            int chance = za == GenData.Z_WAREHOUSE ? (rt == GenData.R_WH_OFFICE ? 700 : 220) : (rt == GenData.R_BATH || rt == GenData.R_HALL ? 350 : (rt == GenData.R_DINER_STORE ? 150 : 650));
            // revision 9: a shop's windows on its floor; a garage bay's walls carry its roller door, not windows
            if (rt == GenData.R_SHOP_FLOOR) chance = 850;
            else if (rt == GenData.R_GARAGE_BAY) chance = 0;
            else if (rt == GenData.R_STOCKROOM || rt == GenData.R_PARTS || rt == GenData.R_FEED_STORE) chance = 150;
            else if (rt == GenData.R_FARM_WORK) chance = 450;
            else if (rt == GenData.R_PANTRY || rt == GenData.R_BOXROOM) chance = 150;   // revision 11
            else if (rt == GenData.R_BARN) chance = 250;
            for (int d = 0; d < 4; d++)
            {
                if (EdgeType(x, z, d) != GenData.E_WALL || GenData.IsBuilding(ZoneAt(x + DX[d], z + DZ[d]))) continue;
                if (rng.Chance(chance)) SetEdge(x, z, d, GenData.E_WINDOW, 2);
            }
        }
        for (int r = 0; r < nRooms; r++)
        {
            int rt = roomType[r];
            bool habitable = rt == GenData.R_LIVING || rt == GenData.R_DINING || rt == GenData.R_KITCHEN || rt == GenData.R_BEDROOM || rt == GenData.R_OFFICE
                || rt == GenData.R_KIDS || rt == GenData.R_DINER_DINING || rt == GenData.R_WH_OFFICE || rt == GenData.R_SHOP_FLOOR || rt == GenData.R_BACK_OFFICE
                || rt == GenData.R_TEEN || rt == GenData.R_PLAYROOM;
            if (!habitable || instLevel[roomInst[r]] < 0) continue;
            int wall = -1, seen = 0;
            bool has = false;
            for (int c = 0; c < W * H && !has; c++)
            {
                if (cellRoom[c] != r || !GenData.IsBuilding(cellZone[c])) continue;
                for (int d = 0; d < 4; d++)
                {
                    int et = EdgeType(c % W, c / W, d);
                    if (et == GenData.E_WINDOW) { has = true; break; }
                    if (et == GenData.E_WALL && !GenData.IsBuilding(ZoneAt(c % W + DX[d], c / W + DZ[d])) && rng.Range(++seen) == 0) wall = c * 4 + d;
                }
            }
            if (!has && wall >= 0) SetEdge(wall / 4 % W, wall / 4 / W, wall % 4, GenData.E_WINDOW, 2);
        }
    }

    // locals of the split plans and helpers, kept from one phase to the next (autophase4)
    GenRng hq_LayoutSite_alt;
    int hq_LayoutSite_bendDir;
    int hq_LayoutSite_crossFar;
    int hq_LayoutSite_dmax0L;
    int hq_LayoutSite_dmax0R;
    int hq_LayoutSite_i;
    int hq_LayoutSite_k;
    int[] hq_LayoutSite_kinds;
    int hq_LayoutSite_lastL;
    int hq_LayoutSite_lastR;
    int hq_LayoutSite_n0;
    int hq_LayoutSite_nk;
    int hq_LayoutSite_offL;
    int hq_LayoutSite_offR;
    int hq_LayoutSite_p;
    bool hq_LayoutSite_park;
    int hq_LayoutSite_perArm;
    string hq_LayoutSite_prog;
    bool hq_LayoutSite_ret;
    GenRng hq_LayoutSite_rng;
    int hq_LayoutSite_seg;
    int hq_LayoutSite_sel1;
    bool hq_LayoutSite_touchL;
    bool hq_LayoutSite_touchR;
    int hq_StampPlot_k;
    int hq_StampPlot_p;
    GenRng hq_StampPlot_rng;
    int hq_StampPlot_zone;
}
