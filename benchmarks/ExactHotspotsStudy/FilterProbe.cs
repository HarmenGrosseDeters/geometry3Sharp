// Hot spot 1: the orient3d calls whose floating-point filter does not decide (the exact stage). Per case: how many, which
// exact path they take (the fixed-width integer path orient3d_int, or the expansions), and how far |det| is from the filter
// bound. Two ratios: r = |det| / (u * permanent) with the exact det, and r_fp = |det_fp| / (u * permanent) with the
// floating-point det the filter computes (u = 2^-53). A filter decides by r_fp: the library's where r_fp exceeds about 7
// (Orient3dBound = (7 + 56u) u), a filter with a proven tighter bound theta u * permanent where r_fp > theta (its
// candidates). The exact r says how far the call is from degenerate. The library records the calls by reservoir sampling
// (up to 200 000 per case, uniform over the case's calls).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using g3;

static class FilterProbe
{
    static readonly Type EP = typeof(ExactPredicates);
    static readonly MethodInfo OrientInt = EP.GetMethod("orient3d_int", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo OrientExact = EP.GetMethod("Orient3dExact", BindingFlags.NonPublic | BindingFlags.Static);

    // the exact determinant ((b - a) x (c - a)) . (d - a) of doubles, as a BigInteger times 2^(3 e)
    static BigInteger ExactDet(Vector3d a, Vector3d b, Vector3d c, Vector3d d, out int e3)
    {
        var v = new[] { a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z };
        var m = new BigInteger[12]; var ex = new int[12]; int e0 = int.MaxValue;
        for (int i = 0; i < 12; ++i) { ExactPoint3.Decompose(v[i], out m[i], out ex[i]); if (!m[i].IsZero) e0 = Math.Min(e0, ex[i]); }
        if (e0 == int.MaxValue) e0 = 0;
        var x = new BigInteger[12];
        for (int i = 0; i < 12; ++i) x[i] = m[i].IsZero ? BigInteger.Zero : m[i] << (ex[i] - e0);
        BigInteger ux = x[3] - x[0], uy = x[4] - x[1], uz = x[5] - x[2], vx = x[6] - x[0], vy = x[7] - x[1], vz = x[8] - x[2];
        BigInteger wx = x[9] - x[0], wy = x[10] - x[1], wz = x[11] - x[2];
        e3 = 3 * e0;
        return wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx);
    }

    // the floating-point determinant as Orient3dFilter computes it (the same differences and products)
    static double DetFp(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
    {
        double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z, wx = d.x - a.x, wy = d.y - a.y, wz = d.z - a.z;
        double uyvz = uy * vz, uzvy = uz * vy, uzvx = uz * vx, uxvz = ux * vz, uxvy = ux * vy, uyvx = uy * vx;
        return wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);
    }

    static double Permanent(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
    {
        double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z, wx = d.x - a.x, wy = d.y - a.y, wz = d.z - a.z;
        return Math.Abs(wx) * (Math.Abs(uy * vz) + Math.Abs(uz * vy)) + Math.Abs(wy) * (Math.Abs(uz * vx) + Math.Abs(ux * vz)) + Math.Abs(wz) * (Math.Abs(ux * vy) + Math.Abs(uy * vx));
    }

    static readonly MethodInfo Filter = EP.GetMethod("Orient3dFilter", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly Func<double, double, double, double, double, double, double, double, double, int> FilterOnly =
        (Func<double, double, double, double, double, double, double, double, double, int>)Filter.CreateDelegate(typeof(Func<double, double, double, double, double, double, double, double, double, int>));

    static readonly string[] Bins = { "r = 0", "0 < r < 1", "1 <= r < 3", "3 <= r <= 7", "r > 7" };
    static int Bin(double r) => r == 0 ? 0 : r < 1 ? 1 : r < 3 ? 2 : r <= 7 ? 3 : 4;

    public static void Run()
    {
        var recF = EP.GetField("ProbeRecord");
        if (recF == null) throw new InvalidOperationException("mode filter needs results/probe_counters.patch applied to the library");
        var callsF = EP.GetField("ProbeExactCalls");
        foreach (var (name, make) in Workload.Cases()) Workload.Run(make, out _);      // JIT warm-up
        Console.WriteLine("| case | ms | exact-stage calls | int path | expansions | " + string.Join(" | ", Bins.Select(b => b + " (r_fp)")) + " | Orient3d ns/call (filter + exact) | filter ns/call | exact-stage share of case |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        long totMs = 0; double totExactMs = 0; var totBins = new long[5]; var totFp = new long[5]; var totCross = new double[5, 5]; long totCalls = 0;
        foreach (var (name, make) in Workload.Cases()) {
            recF.SetValue(null, new List<Vector3d[]>()); callsF.SetValue(null, 0L);
            Workload.Run(make, out _);                                  // recording run (the lock slows it)
            var rec = (List<Vector3d[]>)recF.GetValue(null); long calls = (long)callsF.GetValue(null);
            recF.SetValue(null, null);
            long ms = Workload.Run(make, out _);                        // timing run, no recording
            var bins = new long[5]; var binsFp = new long[5]; var cross = new long[5, 5]; int intPath = 0;
            foreach (var q in rec) {
                var args = new object[] { q[0], q[1], q[2], q[3], 0 };
                if ((bool)OrientInt.Invoke(null, args)) intPath++;
                BigInteger det = ExactDet(q[0], q[1], q[2], q[3], out int e3);
                double perm = Permanent(q[0], q[1], q[2], q[3]);
                double r = det.IsZero ? 0 : Math.Exp(BigInteger.Log(BigInteger.Abs(det)) + e3 * Math.Log(2) - Math.Log(perm) + 53 * Math.Log(2));
                double rfp = perm == 0 ? 0 : Math.Abs(DetFp(q[0], q[1], q[2], q[3])) / (perm * Math.Pow(2, -53));   // permanent 0: det_fp is 0 too
                bins[Bin(r)]++; binsFp[Bin(rfp)]++; cross[Bin(r), Bin(rfp)]++;
            }
            // per-call cost of Orient3d (filter + exact stage) on the recorded inputs, all of which reach the exact stage
            double exactNs = 0, filterNs = 0;
            if (rec.Count > 0) {
                int reps = Math.Max(3, 2000000 / rec.Count);
                int acc = 0;
                foreach (var q in rec) acc += ExactPredicates.Orient3d(q[0], q[1], q[2], q[3]);
                var sw = Stopwatch.StartNew();
                for (int r = 0; r < reps; ++r) foreach (var q in rec) acc += ExactPredicates.Orient3d(q[0], q[1], q[2], q[3]);
                exactNs = sw.Elapsed.TotalMilliseconds * 1e6 / ((double)reps * rec.Count);
                // the filter alone on the same inputs (its sign is 0 for all of them), as the library computes it
                double fx = 0;
                sw.Restart();
                for (int r = 0; r < reps; ++r) foreach (var q in rec) { Vector3d a = q[0], b = q[1], c = q[2], d = q[3]; fx += FilterOnly(b.x - a.x, b.y - a.y, b.z - a.z, c.x - a.x, c.y - a.y, c.z - a.z, d.x - a.x, d.y - a.y, d.z - a.z); }
                filterNs = sw.Elapsed.TotalMilliseconds * 1e6 / ((double)reps * rec.Count);
                if (acc == int.MinValue || fx == 1.5) Console.Write("");
            }
            double scale = rec.Count == 0 ? 0 : (double)calls / rec.Count;
            double exactMs = calls * Math.Max(0, exactNs - filterNs) * 1e-6;
            totMs += ms; totExactMs += exactMs; totCalls += calls;
            for (int i = 0; i < 5; ++i) { totBins[i] += (long)(bins[i] * scale); totFp[i] += (long)(binsFp[i] * scale); for (int j = 0; j < 5; ++j) totCross[i, j] += cross[i, j] * scale; }
            Console.WriteLine($"| {name} | {ms} | {calls} | {intPath}/{rec.Count} | {rec.Count - intPath} | " + string.Join(" | ", bins.Select((b, i) => $"{b} ({binsFp[i]})")) + $" | {exactNs:F0} | {filterNs:F0} | {100 * exactMs / Math.Max(1, ms):F1} % |");
        }
        Console.WriteLine();
        Console.WriteLine($"total: {totMs} ms, {totCalls} exact-stage calls, est. exact-stage time {totExactMs:F0} ms = {100 * totExactMs / totMs:F1} % of the workload");
        Console.WriteLine("calls by bin of the exact r (scaled to all calls): " + string.Join(", ", Bins.Select((b, i) => $"{b}: {totBins[i]} ({100.0 * totBins[i] / Math.Max(1, totCalls):F1} %)")));
        Console.WriteLine("calls by bin of r_fp, the filter's view (scaled): " + string.Join(", ", Bins.Select((b, i) => $"{b.Replace("r", "r_fp")}: {totFp[i]} ({100.0 * totFp[i] / Math.Max(1, totCalls):F1} %)")));
        Console.WriteLine();
        Console.WriteLine("| exact r \\ r_fp | " + string.Join(" | ", Bins.Select(b => b.Replace("r", "r_fp"))) + " |");
        Console.WriteLine("|---|---|---|---|---|---|");
        for (int i = 0; i < 5; ++i) Console.WriteLine($"| {Bins[i]} | " + string.Join(" | ", Enumerable.Range(0, 5).Select(j => $"{100.0 * totCross[i, j] / Math.Max(1, totCalls):F2} %")) + " |");
    }
}
