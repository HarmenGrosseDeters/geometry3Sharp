// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Upper bound of a plane filter in front of ExactTriangleIntersection (docs/research/TRI_TRI_FILTER_UPPER_BOUND_2026-11.md).
// Compiled only with -p:TriTriProbe=true, against a library with probe/tritri_probe.patch applied (see README.md).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

static class TriTriProbeRun
{
    // per-call cost of one Enter/Leave pair of the probe (subtracted from the site and the caller times)
    static double overheadNs = -1;

    internal static double OverheadNs()
    {
        if (overheadNs >= 0) return overheadNs;
        const int n = 2_000_000;
        double best = double.MaxValue;
        for (int r = 0; r < 5; ++r) {
            TriTriProbe.Reset();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; ++i) { long t = TriTriProbe.Enter(TriTriProbe.Site.Count - 1); TriTriProbe.Leave(TriTriProbe.Site.Count - 1, t); }
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / n);
        }
        TriTriProbe.Reset();
        return overheadNs = best;
    }

    internal static void Begin() { OverheadNs(); TriTriProbe.Reset(); TriTriProbe.Capture = false; }

    // after the last timed run of a caller (its total ms, probe overhead included): the share of each site; then one more
    // run (untimed) that captures the pairs, and the micro bound on them
    internal static void Report(string name, double totalMs, Action captureRun)
    {
        double ov = overheadNs * 1e-6;
        long allCalls = 0;
        for (var s = (TriTriProbe.Site)0; s < TriTriProbe.Site.Count; ++s) allCalls += TriTriProbe.Calls(s);
        double net = totalMs - allCalls * ov;
        Console.WriteLine($"#### {name}: caller {totalMs:F0} ms, probe overhead {allCalls * ov:F0} ms ({allCalls} timed calls x {overheadNs:F1} ns), net {net:F0} ms");
        Console.WriteLine("| site | calls | ms (net) | share of caller |");
        Console.WriteLine("|---|---:|---:|---:|");
        for (var s = (TriTriProbe.Site)0; s < TriTriProbe.Site.Count; ++s) {
            long c = TriTriProbe.Calls(s);
            if (c == 0) continue;
            // (a nested site's own overhead is inside its parent's time: subtract the parent's and the nested ones)
            double ms = TriTriProbe.Ms(s) - c * ov;
            Console.WriteLine($"| {TriTriProbe.Names[(int)s]} | {c} | {ms:F1} | {100 * ms / net:F2} % |");
        }
        // the exact tests the callers reach (outermost): BeyondShared + direct Intersects (+ the arrangement's sites)
        double exactMs = TriTriProbe.Ms(TriTriProbe.Site.BeyondShared) + TriTriProbe.Ms(TriTriProbe.Site.IntersectsTop)
            - (TriTriProbe.Calls(TriTriProbe.Site.BeyondShared) + TriTriProbe.Calls(TriTriProbe.Site.IntersectsTop)) * ov;
        Console.WriteLine($"ceiling, self-intersection callers (BeyondShared + direct Intersects free): {100 * exactMs / net:F2} % of the caller");
        long cTouch = TriTriProbe.Calls(TriTriProbe.Site.ArrTouchOnly), cBS = TriTriProbe.Calls(TriTriProbe.Site.IntersectsTop), cNest = TriTriProbe.Calls(TriTriProbe.Site.IntersectsNested), cSep = TriTriProbe.Calls(TriTriProbe.Site.ArrSeparated);
        TriTriProbe.Reset(); TriTriProbe.Capture = true;
        captureRun();
        TriTriProbe.Capture = false;
        long nInt = cBS + cNest;
        Micro(name + " Intersects pairs", TriTriProbe.IntersectsPairs, nInt, net, full: true);
        Micro(name + " arr.separated pairs", TriTriProbe.SeparatedPairs, cSep, net, full: false);
        MicroTouch(name + " arr.touch_only plane sides", TriTriProbe.TouchPairs, TriTriProbe.TouchCorners, cTouch, net);
    }

    // arr.touch_only's plane test (one_side_except_shared of T against U, then of U against T where the code runs it):
    // the Orient3d calls of the non-shared corners against the ideal (one normal per triangle, one dot per corner)
    static void MicroTouch(string name, List<Triangle3d> pairs, List<Index3i> corners, long calls, double netMs)
    {
        int n = pairs.Count / 2;
        if (n == 0) return;
        var A = new Triangle3d[n]; var B = new Triangle3d[n]; var CA = new Index3i[n]; var CB = new Index3i[n];
        for (int i = 0; i < n; ++i) { A[i] = pairs[2 * i]; B[i] = pairs[2 * i + 1]; CA[i] = corners[2 * i]; CB[i] = corners[2 * i + 1]; }
        int reps = Math.Max(1, 200000 / n);
        int chk = 0;
        double tO = Time(() => { int c = 0; for (int r = 0; r < reps; ++r) c += Touch(A, CA, B, CB, false); return c; }) / (n * reps);
        double tI = Time(() => { int c = 0; for (int r = 0; r < reps; ++r) c += Touch(A, CA, B, CB, true); return c; }) / (n * reps);
        double saveMs = calls * (tO - tI) * 1e-6;
        Console.WriteLine($"{name}: {n} pairs; per pair: Orient3d plane sides {tO:F1} ns, ideal {tI:F1} ns; ideal plane filter saves <= {saveMs:F1} ms = {100 * saveMs / netMs:F2} % of the caller");
    }

    static int Touch(Triangle3d[] A, Index3i[] CA, Triangle3d[] B, Index3i[] CB, bool ideal)
    {
        int c = 0;
        for (int i = 0; i < A.Length; ++i) {
            int shared = 0;
            for (int k = 0; k < 3; ++k) if (CA[i].a == CB[i][k] || CA[i].b == CB[i][k] || CA[i].c == CB[i][k]) shared++;
            if (shared >= 3) continue;
            int st = Side(ref A[i], CA[i], ref B[i], CB[i], ideal);
            c += st;
            if (st < 0 && shared < 2) c += Side(ref B[i], CB[i], ref A[i], CA[i], ideal);
        }
        return c;
    }

    static int Side(ref Triangle3d T, Index3i ct, ref Triangle3d U, Index3i cu, bool ideal)
    {
        Vector3d t0 = T.V0, t1 = T.V1, t2 = T.V2;
        Vector3d nrm = ideal ? (t1 - t0).Cross(t2 - t0) : Vector3d.Zero;
        int side = 0; bool zero = false;
        for (int k = 0; k < 3; ++k) {
            int c = cu[k];
            if (c == ct.a || c == ct.b || c == ct.c) continue;
            int o = ideal ? Math.Sign(nrm.Dot(U[k] - t0)) : ExactPredicates.Orient3d(t0, t1, t2, U[k]);
            if (o == 0) { if (side != 0) return -1; zero = true; }
            else { if (zero || (side != 0 && o != side)) return -1; side = o; }
        }
        return zero ? 0 : (side != 0) ? 1 : -1;
    }

    // on the captured pairs: the 6 Orient3d calls (with the early exit of Intersects / separated) against an ideal plane
    // filter that computes each normal once and decides every sign by one dot product (no error bound, no fallback:
    // the cheapest possible filter), and the full Intersects. Saving = calls x (orient - ideal) per pair.
    static void Micro(string name, List<Triangle3d> pairs, long calls, double netMs, bool full)
    {
        int n = pairs.Count / 2;
        if (n == 0) return;
        var A = new Triangle3d[n]; var B = new Triangle3d[n];
        for (int i = 0; i < n; ++i) { A[i] = pairs[2 * i]; B[i] = pairs[2 * i + 1]; }
        if (full) {
            // Intersects runs the 6 Orient3d only for non-degenerate pairs
            var keep = Enumerable.Range(0, n).Where(i => !ExactTriangleIntersection.IsDegenerate(ref A[i]) && !ExactTriangleIntersection.IsDegenerate(ref B[i])).ToArray();
            A = keep.Select(i => A[i]).ToArray(); B = keep.Select(i => B[i]).ToArray(); n = keep.Length;
        }
        long exact0 = PredicateStats.ExactStages((int)PredicateStats.Kind.Orient3d), calls0 = PredicateStats.Calls((int)PredicateStats.Kind.Orient3d);
        int chk = Orient6(A, B);
        long oc = PredicateStats.Calls((int)PredicateStats.Kind.Orient3d) - calls0, oe = PredicateStats.ExactStages((int)PredicateStats.Kind.Orient3d) - exact0;
        int reps = Math.Max(1, 200000 / n);
        double tOrient = Time(() => { int c = 0; for (int r = 0; r < reps; ++r) c += Orient6(A, B); return c; }) / (n * reps);
        double tIdeal = Time(() => { int c = 0; for (int r = 0; r < reps; ++r) c += Ideal(A, B); return c; }) / (n * reps);
        double tFull = full ? Time(() => { int c = 0; for (int r = 0; r < reps; ++r) c += Full(A, B); return c; }) / (n * reps) : double.NaN;
        double saveMs = calls * (tOrient - tIdeal) * 1e-6;
        Console.WriteLine($"{name}: {n} pairs; Orient3d {oc} calls, {oe} exact ({(oc > 0 ? 100.0 * oe / oc : 0):F2} %); per pair: 6 Orient3d {tOrient:F1} ns, ideal 2 cross + 6 dots {tIdeal:F1} ns" +
            (full ? $", full Intersects {tFull:F1} ns" : "") + $"; ideal plane filter saves <= {saveMs:F1} ms = {100 * saveMs / netMs:F2} % of the caller (chk {chk & 1})");
    }

    static int sink;

    static double Time(Func<int> f)
    {
        double best = double.MaxValue;
        for (int r = 0; r < 7; ++r) { var sw = Stopwatch.StartNew(); sink += f(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6); }
        return best;
    }

    static int Full(Triangle3d[] A, Triangle3d[] B)
    {
        int c = 0;
        for (int i = 0; i < A.Length; ++i) if (ExactTriangleIntersection.intersects_core(ref A[i], ref B[i])) c++;
        return c;
    }

    static int Orient6(Triangle3d[] A, Triangle3d[] B)
    {
        int c = 0;
        for (int i = 0; i < A.Length; ++i) {
            Vector3d p1 = A[i].V0, q1 = A[i].V1, r1 = A[i].V2, p2 = B[i].V0, q2 = B[i].V1, r2 = B[i].V2;
            int a = ExactPredicates.Orient3d(p2, q2, r2, p1), b = ExactPredicates.Orient3d(p2, q2, r2, q1), d = ExactPredicates.Orient3d(p2, q2, r2, r1);
            c += a + 2 * b + 3 * d;
            if (a * b > 0 && a * d > 0) continue;
            a = ExactPredicates.Orient3d(p1, q1, r1, p2); b = ExactPredicates.Orient3d(p1, q1, r1, q2); d = ExactPredicates.Orient3d(p1, q1, r1, r2);
            c += a + 2 * b + 3 * d;
        }
        return c;
    }

    static int Ideal(Triangle3d[] A, Triangle3d[] B)
    {
        int c = 0;
        for (int i = 0; i < A.Length; ++i) {
            Vector3d p1 = A[i].V0, q1 = A[i].V1, r1 = A[i].V2, p2 = B[i].V0, q2 = B[i].V1, r2 = B[i].V2;
            Vector3d n2 = (q2 - p2).Cross(r2 - p2);
            int a = Math.Sign(n2.Dot(p1 - p2)), b = Math.Sign(n2.Dot(q1 - p2)), d = Math.Sign(n2.Dot(r1 - p2));
            c += a + 2 * b + 3 * d;
            if (a * b > 0 && a * d > 0) continue;
            Vector3d n1 = (q1 - p1).Cross(r1 - p1);
            a = Math.Sign(n1.Dot(p2 - p1)); b = Math.Sign(n1.Dot(q2 - p1)); d = Math.Sign(n1.Dot(r2 - p1));
            c += a + 2 * b + 3 * d;
        }
        return c;
    }
}
