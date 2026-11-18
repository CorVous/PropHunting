using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// A profiler that writes to the debug log (VRChat's output log), so a play session can be read afterwards: search
// the log for "[PHProf]". START PROFILING / STOP PROFILING on the lobby's PROFILER plaque; each player profiles
// their own client. While it runs it logs
//   sec      once a second: frames, average and longest frame, frames over 20 / 33 / 50 / 100 ms, and how much of
//            a frame our own scripts took (udon, ms per frame)
//   scripts  every ten seconds: each script's share (ms per frame; a group is all the instances of one script)
//   hitch    at once, for a frame of hitchMs or more: how long, how much of it was our scripts and which, and
//            what the game was doing
//   phase, players, build start, map (a built map's generation and build times, its steps' histogram, the time
//            per stage and the slowest step), and the slow generator / builder steps themselves ([PHGame] slow
//            generator step, [MapBuilder] slow step: from stepMs)
//   stop     the run's totals: the frame histogram, the five longest frames, the scripts' shares
// Off, it costs each watched script one test a frame. On, it reads one number per watched script a frame.
// What it cannot see: anything inside the engine (drawing, physics, memory); VRChat does not show a world those.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHProfiler : UdonSharpBehaviour
{
    public PHGame game;
    public UdonSharpBehaviour[] scripts;   // every script that keeps profWatch / profMs (WorldSceneBuilder lists them)
    public int[] scriptGroup;              // per script: its group (index into groupNames)
    public string[] groupNames;
    public TextMeshProUGUI status;
    public float hitchMs = 50f;            // a frame this long is logged at once
    public float stepMs = 12f;             // generator and builder steps this long are logged

    [System.NonSerialized] public bool running;

    System.Diagnostics.Stopwatch watch;
    double lastT, secT0, tenT0, selfMs, selfTen0;
    int frames, secFrames, tenFrames;
    double secMax, secUdon, runSum, runWorst, runUdon;
    int secO20, secO33, secO50, secO100;
    int[] hist = new int[10];
    readonly float[] histEdge = { 8.4f, 11.2f, 13.9f, 16.7f, 22.3f, 33.4f, 50f, 100f, 250f };
    double[] lastMs, tenMs0, groupFrame, groupSum;
    double[] worstMs = new double[5];
    string[] worstNote = new string[5];
    int lastPhase = -1, lastPlayers = -1, maps;
    bool wasBuilding, savedLog;
    float savedGenStep, savedBuildStep;
    double buildT0;
    readonly string[] phaseName = { "LOBBY", "LOADING", "BRIEF", "HIDE", "HUNT", "END" };
    readonly string[] roleName = { "NONE", "HIDER", "HUNTER" };

    string F1(double v) { return v.ToString("0.0"); }
    string F2(double v) { return v.ToString("0.00"); }

    string Platform()
    {
#if UNITY_ANDROID
        return "Quest";
#else
        return "PC";
#endif
    }

    string PhaseName(int p) { return p >= 0 && p < phaseName.Length ? phaseName[p] : p.ToString(); }

    // what the game is doing right now
    string Context()
    {
        int s = game.SlotOf(Networking.LocalPlayer.playerId);
        int role = s >= 0 ? game.slotRole[s] : 0;
        string c = "phase=" + PhaseName(game.phase) + " role=" + (role >= 0 && role < roleName.Length ? roleName[role] : role.ToString());
        if (game.building)
        {
            MapBuilderUdon b = game.builder;
            if (b.busy) c += " building=mesh/props stage=" + b.stage + " cursor=" + b.cursor;
            else c += " building=generating st=" + game.gen.CurrentStage() + " cu=" + game.gen.CurrentCursor() + " ph=" + game.gen.CurrentPhase();
        }
        return c;
    }

    public void Begin()
    {
        if (running) return;
        if (watch == null) watch = new System.Diagnostics.Stopwatch();
        watch.Reset(); watch.Start();
        int n = scripts.Length, ng = groupNames.Length;
        lastMs = new double[n]; tenMs0 = new double[n]; groupFrame = new double[ng]; groupSum = new double[ng];
        for (int i = 0; i < n; i++)
        {
            if (scripts[i] == null) continue;
            scripts[i].SetProgramVariable("profMs", 0.0);
            scripts[i].SetProgramVariable("profWatch", watch);
        }
        for (int i = 0; i < hist.Length; i++) hist[i] = 0;
        for (int i = 0; i < worstMs.Length; i++) { worstMs[i] = 0; worstNote[i] = ""; }
        frames = 0; runSum = 0; runWorst = 0; runUdon = 0; selfMs = 0; selfTen0 = 0; maps = 0;
        lastPhase = game.phase; lastPlayers = VRCPlayerApi.GetPlayerCount(); wasBuilding = game.building;
        savedLog = game.logSlowSteps; savedGenStep = game.slowStepMs; savedBuildStep = game.builder.slowStepMs;
        game.logSlowSteps = true; game.slowStepMs = stepMs; game.builder.slowStepMs = stepMs;
        running = true;
        Debug.Log("[PHProf] start v=1 platform=" + Platform() + " vr=" + (Networking.LocalPlayer.IsUserInVR() ? 1 : 0) + " players=" + lastPlayers
            + " host=" + (game.IsHost ? 1 : 0) + " scripts=" + n + " hitchMs=" + hitchMs.ToString("0") + " stepMs=" + stepMs.ToString("0") + " " + Context());
        if (status != null) status.text = "PROFILING 0:00";
    }

    public void End()
    {
        if (!running) return;
        running = false;
        double t = watch.Elapsed.TotalMilliseconds;
        int n = scripts.Length;
        for (int g = 0; g < groupNames.Length; g++) groupSum[g] = 0;
        for (int i = 0; i < n; i++)
        {
            if (scripts[i] == null) continue;
            groupSum[scriptGroup[i]] += (double)scripts[i].GetProgramVariable("profMs");
            scripts[i].SetProgramVariable("profWatch", null);
        }
        game.logSlowSteps = savedLog; game.slowStepMs = savedGenStep; game.builder.slowStepMs = savedBuildStep;
        int f = frames > 3 ? frames - 3 : 1;
        string h = "";
        for (int i = 0; i < hist.Length; i++) h += (i < histEdge.Length ? "<" + histEdge[i].ToString("0.#") : ">=250") + ":" + hist[i] + (i + 1 < hist.Length ? " " : "");
        Debug.Log("[PHProf] stop t=" + F1(t / 1000.0) + "s frames=" + f + " avg=" + F1(runSum / f) + "ms fps=" + F1(1000.0 * f / (runSum > 0 ? runSum : 1)) + " worst=" + F1(runWorst)
            + "ms udon=" + F2(runUdon / f) + "ms/frame maps=" + maps + " hist(ms:frames) " + h);
        string w = "";
        for (int i = 0; i < worstMs.Length; i++) if (worstMs[i] > 0) w += " | " + F1(worstMs[i]) + "ms " + worstNote[i];
        Debug.Log("[PHProf] stop worst frames" + w);
        string sc = "";
        for (int g = 0; g < groupNames.Length; g++) sc += " " + groupNames[g] + "=" + F2(groupSum[g] / f);
        Debug.Log("[PHProf] stop scripts (ms per frame)" + sc + " PHProfiler=" + F2(selfMs / f));
        if (status != null) status.text = "PROFILING OFF";
    }

    void Update()
    {
        if (!running) return;
        double now = watch.Elapsed.TotalMilliseconds;
        int n = scripts.Length;
        if (frames < 3)
        {
            // the first frames only set the marks (the start itself takes a moment)
            frames++;
            for (int i = 0; i < n; i++) { lastMs[i] = scripts[i] != null ? (double)scripts[i].GetProgramVariable("profMs") : 0; tenMs0[i] = lastMs[i]; }
            lastT = now; secT0 = now; tenT0 = now; secFrames = 0; tenFrames = 0; secMax = 0; secUdon = 0; secO20 = 0; secO33 = 0; secO50 = 0; secO100 = 0;
            return;
        }
        double dt = now - lastT;
        lastT = now; frames++;
        bool hitch = dt >= hitchMs;
        if (hitch) for (int g = 0; g < groupFrame.Length; g++) groupFrame[g] = 0;
        double udon = 0;
        for (int i = 0; i < n; i++)
        {
            if (scripts[i] == null) continue;
            double m = (double)scripts[i].GetProgramVariable("profMs");
            double d = m - lastMs[i];
            lastMs[i] = m; udon += d;
            if (hitch) groupFrame[scriptGroup[i]] += d;
        }
        runSum += dt; runUdon += udon;
        secFrames++; tenFrames++; secUdon += udon;
        if (dt > secMax) secMax = dt;
        if (dt > runWorst) runWorst = dt;
        if (dt >= 20.0) secO20++;
        if (dt >= 33.4) secO33++;
        if (dt >= 50.0) secO50++;
        if (dt >= 100.0) secO100++;
        int b = 0;
        while (b < histEdge.Length && dt >= histEdge[b]) b++;
        hist[b]++;
        if (hitch)
        {
            string top = "";
            for (int g = 0; g < groupFrame.Length; g++) if (groupFrame[g] >= 0.5) top += " " + groupNames[g] + "=" + F1(groupFrame[g]);
            Debug.Log("[PHProf] hitch t=" + F2(now / 1000.0) + "s frame=" + F1(dt) + "ms udon=" + F1(udon) + "ms" + (top != "" ? " (" + top.Substring(1) + ")" : "") + " " + Context());
        }
        if (dt > worstMs[worstMs.Length - 1])
        {
            int k = worstMs.Length - 1;
            while (k > 0 && worstMs[k - 1] < dt) { worstMs[k] = worstMs[k - 1]; worstNote[k] = worstNote[k - 1]; k--; }
            worstMs[k] = dt; worstNote[k] = "at " + F1(now / 1000.0) + "s udon=" + F1(udon) + "ms " + Context();
        }

        // what happened
        int ph = game.phase;
        if (ph != lastPhase) { Debug.Log("[PHProf] phase t=" + F1(now / 1000.0) + "s " + PhaseName(lastPhase) + ">" + PhaseName(ph) + " " + Context()); lastPhase = ph; }
        bool building = game.building;
        if (building && !wasBuilding)
        {
            buildT0 = now;
            Debug.Log("[PHProf] build start t=" + F1(now / 1000.0) + "s code=" + game.seedCode + " size=" + game.mapSize + " density=" + game.mapDensity + " evo=" + game.mapEvo);
        }
        if (!building && wasBuilding) MapLine(now);
        wasBuilding = building;

        if (now - secT0 >= 1000.0)
        {
            int pc = VRCPlayerApi.GetPlayerCount();
            if (pc != lastPlayers) { Debug.Log("[PHProf] players t=" + F1(now / 1000.0) + "s " + lastPlayers + ">" + pc); lastPlayers = pc; }
            Debug.Log("[PHProf] sec t=" + (now / 1000.0).ToString("0") + " n=" + secFrames + " avg=" + F1((now - secT0) / secFrames) + " max=" + F1(secMax)
                + " o20=" + secO20 + " o33=" + secO33 + " o50=" + secO50 + " o100=" + secO100 + " udon=" + F2(secUdon / secFrames) + " phase=" + PhaseName(ph) + (building ? " building" : ""));
            secT0 = now; secFrames = 0; secMax = 0; secUdon = 0; secO20 = 0; secO33 = 0; secO50 = 0; secO100 = 0;
            if (status != null)
            {
                int s = (int)(now / 1000.0);
                status.text = "PROFILING " + (s / 60) + ":" + (s % 60).ToString("00");
            }
        }
        if (now - tenT0 >= 10000.0)
        {
            for (int g = 0; g < groupSum.Length; g++) groupSum[g] = 0;
            for (int i = 0; i < n; i++) { groupSum[scriptGroup[i]] += lastMs[i] - tenMs0[i]; tenMs0[i] = lastMs[i]; }
            string sc = "";
            for (int g = 0; g < groupSum.Length; g++) sc += " " + groupNames[g] + "=" + F2(groupSum[g] / tenFrames);
            Debug.Log("[PHProf] scripts t=" + (now / 1000.0).ToString("0") + " (ms per frame)" + sc + " PHProfiler=" + F2((selfMs - selfTen0) / tenFrames));
            tenT0 = now; tenFrames = 0; selfTen0 = selfMs;
        }
        selfMs += watch.Elapsed.TotalMilliseconds - now;
    }

    // a map has been built (or its generation failed): its generation and build in one line
    void MapLine(double now)
    {
        MapBuilderUdon b = game.builder;
        MapGenUdon gen = game.gen;
        maps++;
        int[] gh = game.genHist;
        float[] gs = game.genStageMs;
        double[] bs = b.stageMs;
        string h = "<2:" + gh[0] + " <5:" + gh[1] + " <10:" + gh[2] + " <15:" + gh[3] + " <20:" + gh[4] + " <30:" + gh[5] + " <50:" + gh[6] + " >=50:" + gh[7];
        string st = "";
        for (int i = 0; i < gs.Length; i++) if (gs[i] >= 0.5f) st += " " + i + ":" + gs[i].ToString("0");
        string bst = "";
        for (int i = 0; i < bs.Length; i++) if (bs[i] >= 0.5) bst += " " + i + ":" + bs[i].ToString("0");
        Debug.Log("[PHProf] map t=" + F1(now / 1000.0) + "s code=" + game.seedCode + " size=" + game.mapSize + (gen.failed ? " FAILED" : "") + " wall=" + F1((now - buildT0) / 1000.0) + "s rooms=" + gen.nRooms + " props=" + gen.nEmit
            + " gen=" + game.genMs.ToString("0") + "ms steps=" + game.genSteps + " genWorst=" + game.genWorstMs.ToString("0.0") + "ms@" + game.genWorstAt
            + " genHist(ms:steps) " + h + " genStages(stage:ms)" + st
            + " build=" + b.buildMs.ToString("0") + "ms buildWorst=" + b.worstStepMs.ToString("0.0") + "ms@stage" + b.worstStage + "/cursor" + b.worstCursor + " buildStages(stage:ms)" + bst);
    }
}
