using UdonSharp;
using UnityEngine;

// Test aid: generates one map on start, a slice per frame, and logs how long the generator took in Udon and
// whether its hash matches the plain C# generator's (expectedHash, filled by the table baker).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MapGenBench : UdonSharpBehaviour
{
    public MapGenUdon gen;
    public MapBuilderUdon builder;
    public uint benchSeed = 12345u;
    public int benchSize = 1;
    public int benchDensity = 0;
    public uint expectedHash;
    public float budgetMs = 8f;

    bool running;
    int frames, steps;
    double workMs, maxStepMs;
    float startTime;
    System.Diagnostics.Stopwatch sw;

    void Start()
    {
        sw = new System.Diagnostics.Stopwatch();
        gen.Begin(benchSeed, benchSize, benchDensity);
        running = true;
        startTime = Time.realtimeSinceStartup;
        Debug.Log("[MapGenBench] start seed " + benchSeed + " size " + benchSize);
    }

    void Update()
    {
        if (!running) return;
        frames++;
        sw.Reset(); sw.Start();
        bool finished = false;
        while (sw.Elapsed.TotalMilliseconds < budgetMs)
        {
            double before = sw.Elapsed.TotalMilliseconds;
            int st = gen.CurrentStage(), cu = gen.CurrentCursor();
            finished = gen.Step();
            steps++;
            double one = sw.Elapsed.TotalMilliseconds - before;
            if (one > maxStepMs) maxStepMs = one;
            if (one > 50) Debug.Log("[MapGenBench] slow step: stage " + st + " cursor " + cu + " took " + one.ToString("0") + " ms");
            if (finished) break;
        }
        sw.Stop();
        workMs += sw.Elapsed.TotalMilliseconds;
        if (!finished) return;
        running = false;
        if (builder != null && !gen.failed) builder.BeginBuild();
        Debug.Log("[MapGenBench] done failed=" + gen.failed + " hash " + gen.mapHash.ToString("X8") + " expected " + expectedHash.ToString("X8")
            + (gen.mapHash == expectedHash ? " MATCH" : " MISMATCH") + " emits " + gen.nEmit + " rerolls " + gen.attempt
            + " | work " + workMs.ToString("0") + " ms, wall " + ((Time.realtimeSinceStartup - startTime) * 1000f).ToString("0")
            + " ms, frames " + frames + ", steps " + steps + ", longest step " + maxStepMs.ToString("0") + " ms");
    }
}
