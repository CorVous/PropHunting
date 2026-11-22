using System;
using System.Diagnostics;
class Program {
    static void Main(string[] a) {
        int N = a.Length > 0 ? int.Parse(a[0]) : 100;
        string[] names = { "S", "M", "L", "XL" };
        var g = new MapGen();
        for (int w = 0; w < 30; w++) g.RunAll((uint)(9000 + w), w % 4);   // warm up the JIT
        for (int size = 0; size < 4; size++) {
            double l0 = 0; double tot = 0, lay = 0, pop = 0, rest = 0; long steps = 0; int fails = 0, retries = 0, retried = 0; long emits = 0; double worst = 0; int rooms = 0;
            for (uint seed = 1; seed <= N; seed++) {
                g.Begin(seed, size, 0);
                var sw = Stopwatch.StartNew();
                double l = 0, p = 0, r = 0;
                bool fin = false;
                while (!fin) {
                    int before = g.progressPermille;
                    long t0 = Stopwatch.GetTimestamp();
                    fin = g.Step();
                    double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                    if (ms > worst) worst = ms;
                    if (before < 60) l0 += ms; if (before < 150) l += ms; else if (before < 750) p += ms; else r += ms;
                }
                tot += sw.Elapsed.TotalMilliseconds; lay += l; pop += p; rest += r;
                steps += g.statSteps; if (g.failed) fails++; retries += g.attempt; if (g.attempt > 0) retried++; emits += g.nEmit; rooms += g.nRooms;
            }
            Console.WriteLine($"{names[size]}: total {tot / N:F2} ms | layout+walls+tags {lay / N:F2} | rooms and areas {pop / N:F2} | leaks+validate+finish {rest / N:F2} | LayoutSite alone {l0 / N:F2} | steps {steps / N} | objects {emits / N} rooms {rooms / N} | maps re-rolled {retried} of {N}, re-rolls {retries}, failed {fails} | slowest step {worst:F2} ms");
        }
    }
}
