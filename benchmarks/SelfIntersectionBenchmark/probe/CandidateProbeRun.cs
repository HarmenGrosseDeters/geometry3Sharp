// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Where TriangleTriangleFilter's time goes in the self-intersection checks, and upper bounds of the candidates that
// attack it (docs/research/TRI_TRI_CANDIDATE_PAIRS_2026-11.md). Compiled only with -p:CandidateProbe=true, against a
// library with probe/candidate_probe.patch applied (see README.md).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

static class TriTriProbeRun
{
    static double overheadNs = -1;

    // one Stopwatch.GetTimestamp pair plus the bookkeeping of Record (without capture)
    static double OverheadNs()
    {
        if (overheadNs >= 0) return overheadNs;
        const int n = 2_000_000;
        var A = new Triangle3d(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY);
        double best = double.MaxValue;
        for (int r = 0; r < 5; ++r) {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; ++i) {
                long t0 = Stopwatch.GetTimestamp();
                TriTriProbe.Record(new Index3i(0, 1, 2), ref A, new Index3i(0, 3, 4), ref A, 0, Stopwatch.GetTimestamp() - t0);
            }
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / n);
        }
        TriTriProbe.Reset();
        return overheadNs = best;
    }

    internal static void Begin() { OverheadNs(); TriTriProbe.Reset(); TriTriProbe.Stride = 0; }

    internal static void Report(string name, double totalMs, Action captureRun)
    {
        double ov = overheadNs * 1e-6, tick = 1000.0 / Stopwatch.Frequency;
        long calls = TriTriProbe.Calls.Sum();
        double net = totalMs - calls * ov;
        double filterMs = TriTriProbe.Ticks.Sum() * tick;
        Console.WriteLine($"#### {name}: caller {totalMs:F0} ms, probe overhead {calls * ov:F0} ms ({calls} filter calls x {overheadNs:F1} ns), net {net:F0} ms; filter {filterMs:F0} ms = {100 * filterMs / net:F1} %" +
            (TriTriProbe.CheckCalls > 0 ? $"; Reducer check {TriTriProbe.CheckCalls} calls, {TriTriProbe.CheckTicks * tick - calls * ov:F0} ms net = {100 * (TriTriProbe.CheckTicks * tick - calls * ov) / net:F1} %" : ""));
        Console.WriteLine("| site | shared | calls | filter ms | share of caller | separate | intersecting | undecided |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        var cells = new Dictionary<(int, int), long>();
        for (int s = 0; s < TriTriProbe.Sites; ++s)
            for (int sh = 0; sh < 4; ++sh) {
                long c = 0; double ms = 0; var byR = new long[3];
                for (int r = 0; r < 3; ++r) { int k = (s * 4 + sh) * 3 + r; c += TriTriProbe.Calls[k]; ms += TriTriProbe.Ticks[k] * tick; byR[r] = TriTriProbe.Calls[k]; }
                if (c == 0) continue;
                cells[(s, sh)] = c;
                Console.WriteLine($"| {TriTriProbe.SiteNames[s]} | {sh} | {c} | {ms:F1} | {100 * ms / net:F2} % | {100.0 * byR[1] / c:F1} % | {100.0 * byR[2] / c:F1} % | {100.0 * byR[0] / c:F1} % |");
            }
        // capture run: a sample of about 300000 filter inputs, spread evenly over the run
        TriTriProbe.Reset();
        TriTriProbe.Stride = (int)Math.Max(1, calls / 300000);
        captureRun();
        TriTriProbe.Stride = 0;
        Micro(name, TriTriProbe.Sample.ToList(), cells, net);
        TriTriProbe.Reset();
    }

    // per (site, shared): the filter alone against the plane pretest first (filter only where it does not decide);
    // saving = calls x (filter - combined) per pair
    static void Micro(string name, List<TriTriProbe.Pair> sample, Dictionary<(int, int), long> cells, double netMs)
    {
        Console.WriteLine($"{name}: pretest bound on {sample.Count} sampled pairs");
        Console.WriteLine("| site | shared | pretest decides | filter ns | preamble ns | ideal 2D ns | pretest ns | pretest + filter ns | gated ns | filter ms (micro) | saving ms | gated saving ms | ideal 2D saving ms |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        double total = 0, totalG = 0, totalF = 0, total2 = 0;
        foreach (var g in sample.GroupBy(p => (p.site, p.shared)).OrderBy(g => g.Key)) {
            var P = g.ToArray();
            int n = P.Length, decided = 0;
            foreach (var p in P) {
                var q = p;
                int pre = Pretest(ref q);
                if (pre == TriangleTriangleFilter.Separate) {
                    decided++;
                    if (p.result == TriangleTriangleFilter.Intersecting) throw new Exception("pretest contradicts the filter");
                }
            }
            int reps = Math.Max(1, 200000 / n);
            double tF = Time(() => { int c = 0; for (int r = 0; r < reps; ++r) for (int i = 0; i < n; ++i) c += TriangleTriangleFilter.beyond_core(P[i].ia, ref P[i].A, P[i].ib, ref P[i].B); return c; }) / (n * reps);
            double tP = Time(() => { int c = 0; for (int r = 0; r < reps; ++r) for (int i = 0; i < n; ++i) c += Pretest(ref P[i]); return c; }) / (n * reps);
            double tC = Time(() => {
                int c = 0;
                for (int r = 0; r < reps; ++r)
                    for (int i = 0; i < n; ++i) {
                        int x = Pretest(ref P[i]);
                        c += x != TriangleTriangleFilter.Undecided ? x : TriangleTriangleFilter.beyond_core(P[i].ia, ref P[i].A, P[i].ib, ref P[i].B);
                    }
                return c;
            }) / (n * reps);
            double tG = Time(() => {
                int c = 0;
                for (int r = 0; r < reps; ++r)
                    for (int i = 0; i < n; ++i) {
                        int x = common_axis_plane(ref P[i].A, ref P[i].B) >= 0 ? TriangleTriangleFilter.Undecided : Pretest(ref P[i]);
                        c += x != TriangleTriangleFilter.Undecided ? x : TriangleTriangleFilter.beyond_core(P[i].ia, ref P[i].A, P[i].ib, ref P[i].B);
                    }
                return c;
            }) / (n * reps);
            double tPre = Time(() => { int c = 0; for (int r = 0; r < reps; ++r) for (int i = 0; i < n; ++i) c += Preamble(ref P[i]); return c; }) / (n * reps);
            // ideal 2D separation for pairs without shared corners: A's projection axis and edge lines precomputed (free,
            // as if reused over all of A's candidates), 3 x 3 signs against A's lines and 3 x 3 against B's (no bound)
            double t2 = double.NaN;
            if (g.Key.Item2 == 0) {
                var L = new double[n * 16];
                for (int i = 0; i < n; ++i) Lines(ref P[i].A, L, 16 * i);
                t2 = Time(() => { int c = 0; for (int r = 0; r < reps; ++r) for (int i = 0; i < n; ++i) c += Ideal2D(L, 16 * i, ref P[i].B); return c; }) / (n * reps);
            }
            long calls = cells.TryGetValue(g.Key, out long cc) ? cc : 0;
            double save = calls * (tF - tC) * 1e-6, saveG = calls * (tF - tG) * 1e-6, fMs = calls * tF * 1e-6;
            double save2 = double.IsNaN(t2) ? 0 : calls * (tF - t2) * 1e-6;
            total += save; totalG += saveG; totalF += fMs; total2 += save2;
            Console.WriteLine($"| {TriTriProbe.SiteNames[g.Key.Item1]} | {g.Key.Item2} | {100.0 * decided / n:F1} % | {tF:F1} | {tPre:F1} | {t2:F1} | {tP:F1} | {tC:F1} | {tG:F1} | {fMs:F1} | {save:F1} | {saveG:F1} | {save2:F1} |");
        }
        {
            var P = sample.ToArray(); int n = P.Length, reps = Math.Max(1, 200000 / Math.Max(1, n));
            double tc = Time(() => { int c = 0; for (int r = 0; r < reps; ++r) for (int i = 0; i < n; ++i) c += FiniteChain(ref P[i].A, ref P[i].B) ? 1 : 0; return c; }) / (n * reps);
            double tt = Time(() => { int c = 0; for (int r = 0; r < reps; ++r) for (int i = 0; i < n; ++i) c += FiniteTree(ref P[i].A, ref P[i].B) ? 1 : 0; return c; }) / (n * reps);
            long all = cells.Values.Sum();
            Console.WriteLine($"{name}: all_finite as written {tc:F1} ns, as a balanced sum {tt:F1} ns per pair: saves <= {all * (tc - tt) * 1e-6:F1} ms = {100 * all * (tc - tt) * 1e-6 / netMs:F2} % of the caller");
        }
        Console.WriteLine($"{name}: filter (micro) {totalF:F1} ms = {100 * totalF / netMs:F2} %; plane pretest saves <= {total:F1} ms = {100 * total / netMs:F2} %, gated {totalG:F1} ms = {100 * totalG / netMs:F2} %; ideal 2D for unshared pairs saves <= {total2:F1} ms = {100 * total2 / netMs:F2} % of the caller (negative: slower)");
    }

    // Separate where Orient3d's floating-point filter alone proves the non-shared corners of one triangle strictly on
    // one side of the other's plane (then they meet at most in the shared vertices or edge), else Undecided
    static int Pretest(ref TriTriProbe.Pair p)
    {
        if (p.shared >= 3) return TriangleTriangleFilter.Undecided;
        if (side_except(ref p.A, p.ia, ref p.B, p.ib) || (p.shared < 2 && side_except(ref p.B, p.ib, ref p.A, p.ia)))
            return TriangleTriangleFilter.Separate;
        return TriangleTriangleFilter.Undecided;
    }

    // the corners of U not shared with T strictly on one side of T's plane, decided by the filter
    static bool side_except(ref Triangle3d T, Index3i it, ref Triangle3d U, Index3i iu)
    {
        Vector3d a = T.V0, b = T.V1, c = T.V2;
        double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
        int side = 0;
        for (int k = 0; k < 3; ++k) {
            int id = iu[k];
            if (id == it.a || id == it.b || id == it.c) continue;
            Vector3d d = U[k];
            int o = ExactPredicates.Orient3dFilter(ux, uy, uz, vx, vy, vz, d.x - a.x, d.y - a.y, d.z - a.z);
            if (o == 0 || (side != 0 && o != side)) return false;
            side = o;
        }
        return side != 0;
    }

    static int common_axis_plane(ref Triangle3d A, ref Triangle3d B)
    {
        for (int k = 0; k < 3; ++k) {
            double v = A.V0[k];
            if (A.V1[k] == v && A.V2[k] == v && B.V0[k] == v && B.V1[k] == v && B.V2[k] == v) return k;
        }
        return -1;
    }

    // the filter's work before its first Orient2d: finiteness, shared corners, common axis plane, A's normal and axis
    static int Preamble(ref TriTriProbe.Pair p)
    {
        ref Triangle3d A = ref p.A; ref Triangle3d B = ref p.B;
        double z = (A.V0.x - A.V0.x) + (A.V0.y - A.V0.y) + (A.V0.z - A.V0.z) + (A.V1.x - A.V1.x) + (A.V1.y - A.V1.y)
            + (A.V1.z - A.V1.z) + (A.V2.x - A.V2.x) + (A.V2.y - A.V2.y) + (A.V2.z - A.V2.z) + (B.V0.x - B.V0.x)
            + (B.V0.y - B.V0.y) + (B.V0.z - B.V0.z) + (B.V1.x - B.V1.x) + (B.V1.y - B.V1.y) + (B.V1.z - B.V1.z)
            + (B.V2.x - B.V2.x) + (B.V2.y - B.V2.y) + (B.V2.z - B.V2.z);
        if (z != 0) return -1;
        int ns = 0;
        for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) if (p.ia[i] == p.ib[j]) ns++;
        int axis = common_axis_plane(ref A, ref B);
        if (axis < 0) {
            Vector3d nn = (A.V1 - A.V0).Cross(A.V2 - A.V0);
            double x = Math.Abs(nn.x), y = Math.Abs(nn.y), w = Math.Abs(nn.z);
            axis = (x >= y && x >= w) ? 0 : (y >= w ? 1 : 2);
        }
        return axis + ns;
    }

    static Vector2d drop(Vector3d v, int axis)
    {
        return (axis == 0) ? new Vector2d(v.y, v.z) : (axis == 1) ? new Vector2d(v.z, v.x) : new Vector2d(v.x, v.y);
    }

    // A's axis and its three edge lines n . x <= c (oriented outward)
    static void Lines(ref Triangle3d A, double[] L, int o)
    {
        Vector3d nn = (A.V1 - A.V0).Cross(A.V2 - A.V0);
        double x = Math.Abs(nn.x), y = Math.Abs(nn.y), w = Math.Abs(nn.z);
        int axis = (x >= y && x >= w) ? 0 : (y >= w ? 1 : 2);
        Vector2d a0 = drop(A.V0, axis), a1 = drop(A.V1, axis), a2 = drop(A.V2, axis);
        double or = Math.Sign((a1 - a0).x * (a2 - a0).y - (a1 - a0).y * (a2 - a0).x);
        L[o] = axis;
        Vector2d[] v = { a0, a1, a2 };
        for (int e = 0; e < 3; ++e) {
            Vector2d p = v[e], q = v[(e + 1) % 3];
            double nx = or * (q.y - p.y), ny = -or * (q.x - p.x);
            L[o + 1 + 3 * e] = nx; L[o + 2 + 3 * e] = ny; L[o + 3 + 3 * e] = nx * p.x + ny * p.y;
            L[o + 10 + 2 * e] = p.x; L[o + 11 + 2 * e] = p.y;
        }
    }

    static int Ideal2D(double[] L, int o, ref Triangle3d B)
    {
        int axis = (int)L[o];
        Vector2d b0 = drop(B.V0, axis), b1 = drop(B.V1, axis), b2 = drop(B.V2, axis);
        for (int e = 0; e < 3; ++e) {
            double nx = L[o + 1 + 3 * e], ny = L[o + 2 + 3 * e], c = L[o + 3 + 3 * e];
            if (nx * b0.x + ny * b0.y > c && nx * b1.x + ny * b1.y > c && nx * b2.x + ny * b2.y > c) return 0;
        }
        // B's edge lines (B changes per pair) against A's corners
        double ob = Math.Sign((b1 - b0).x * (b2 - b0).y - (b1 - b0).y * (b2 - b0).x);
        double ax0 = L[o + 10], ay0 = L[o + 11], ax1 = L[o + 12], ay1 = L[o + 13], ax2 = L[o + 14], ay2 = L[o + 15];
        for (int e = 0; e < 3; ++e) {
            Vector2d p = e == 0 ? b0 : (e == 1 ? b1 : b2), q = e == 0 ? b1 : (e == 1 ? b2 : b0);
            double nx = ob * (q.y - p.y), ny = -ob * (q.x - p.x), c = nx * p.x + ny * p.y;
            if (nx * ax0 + ny * ay0 > c && nx * ax1 + ny * ay1 > c && nx * ax2 + ny * ay2 > c) return 0;
        }
        return 1;
    }

    static bool FiniteChain(ref Triangle3d A, ref Triangle3d B)
    {
        double z = (A.V0.x - A.V0.x) + (A.V0.y - A.V0.y) + (A.V0.z - A.V0.z) + (A.V1.x - A.V1.x) + (A.V1.y - A.V1.y)
            + (A.V1.z - A.V1.z) + (A.V2.x - A.V2.x) + (A.V2.y - A.V2.y) + (A.V2.z - A.V2.z) + (B.V0.x - B.V0.x)
            + (B.V0.y - B.V0.y) + (B.V0.z - B.V0.z) + (B.V1.x - B.V1.x) + (B.V1.y - B.V1.y) + (B.V1.z - B.V1.z)
            + (B.V2.x - B.V2.x) + (B.V2.y - B.V2.y) + (B.V2.z - B.V2.z);
        return z == 0;
    }

    static bool FiniteTree(ref Triangle3d A, ref Triangle3d B)
    {
        double a = ((A.V0.x - A.V0.x) + (A.V0.y - A.V0.y)) + ((A.V0.z - A.V0.z) + (A.V1.x - A.V1.x));
        double b = ((A.V1.y - A.V1.y) + (A.V1.z - A.V1.z)) + ((A.V2.x - A.V2.x) + (A.V2.y - A.V2.y));
        double c = ((A.V2.z - A.V2.z) + (B.V0.x - B.V0.x)) + ((B.V0.y - B.V0.y) + (B.V0.z - B.V0.z));
        double d = ((B.V1.x - B.V1.x) + (B.V1.y - B.V1.y)) + ((B.V1.z - B.V1.z) + (B.V2.x - B.V2.x));
        double e = (B.V2.y - B.V2.y) + (B.V2.z - B.V2.z);
        return ((a + b) + (c + d)) + e == 0;
    }

    static int sink;

    static double Time(Func<int> f)
    {
        double best = double.MaxValue;
        for (int r = 0; r < 5; ++r) { var sw = Stopwatch.StartNew(); sink += f(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6); }
        return best;
    }
}
