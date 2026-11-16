// Upper-bound check for a certified patch bound (#848, docs/design/CERTIFIED_PATCH_BOUND.md section 7): how many subdivisions and
// how much time would MeshHausdorffDistance.CertifiedUpper save if the patch bound could be used? The existing, uncertified
// patch_bound is the oracle (its slack is estimated, so the numbers are what a certified version with the same tightness would
// give; the certified one is a little wider and slower). No library code is changed: the internals are reached by reflection.
//
// A one-sided branch and bound A -> B written out here (sequential, one thread, so the times are not noise from the pool): the
// same pieces as MeshHausdorffDistance (corners, 4-split by midpoints, a piece is dropped when its bound is <= the largest distance
// found, stop when the largest bound is <= lower + tolerance). The midpoints are rounded without the allowance (off); this is a
// measurement of counts, not a bound. Variants: default / certified cascade, each without and with the patch bound used as
//   u = min(u, max(largest corner bound, patch_bound))
// (the rule of TriangleDeviationBounds.UpperBound) whenever u > threshold.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using g3;
using gs;

static partial class Program
{
    delegate int NearestFn(Vector3d p, out double d);
    delegate double PatchFn(Vector3d p0, Vector3d p1, Vector3d p2, int n0, int n1, int n2);
    delegate double PointUpperFn(Vector3d x, Vector3d v0, Vector3d v1, Vector3d v2, double w1, double w2);

    sealed class OracleResult
    {
        public double Lower, Upper, Ms, PatchMs;
        public int Subdivisions, PatchCalls, PatchUseful, PatchFinite;
        public bool Converged;
    }

    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;

    // patchMode: 0 = none, 1 = the patch bound as is, 2 = scaled by 1 / (the largest component of the unit normal of the triangle), what a
    // projection along the dominant coordinate axis (exact Orient2d on the input doubles, no frame) gives: the vertical gap instead of
    // the perpendicular one
    static OracleResult Oracle(DMesh3 a, DMesh3 b, bool certified, int patchMode, double tol, int maxSubdivisions)
    {
        var asm = typeof(TriangleDeviationBounds).Assembly;
        var ctb = asm.GetType("g3.CertifiedTriangleBounds");
        var pointUpper = (PointUpperFn)Delegate.CreateDelegate(typeof(PointUpperFn), ctb.GetMethod("PointTriangleUpper", NP));
        var bounds = new TriangleDeviationBounds(b);
        typeof(TriangleDeviationBounds).GetField("Certified", NP).SetValue(bounds, certified);
        typeof(TriangleDeviationBounds).GetField("PointDistance", NP).SetValue(bounds, true);
        var patchFn = (PatchFn)Delegate.CreateDelegate(typeof(PatchFn), bounds, typeof(TriangleDeviationBounds).GetMethod("patch_bound", NP));
        var spatial = new DMeshAABBTree3(b, true);
        var nearest = (NearestFn)Delegate.CreateDelegate(typeof(NearestFn), spatial, typeof(DMeshAABBTree3).GetMethod("FindNearestTriangleByClosestPoint", NP));

        var res = new OracleResult();
        var pos = new List<Vector3d>(); var dist = new List<double>(); var up = new List<double>(); var near = new List<int>();
        double lower = 0;
        int add(Vector3d p)
        {
            int t = nearest(p, out double d);
            double u = d;
            if (certified) {
                var q = MeshQueries.TriangleDistance(b, t, p);
                u = pointUpper(p, q.Triangle.V0, q.Triangle.V1, q.Triangle.V2, q.TriangleBaryCoords.y, q.TriangleBaryCoords.z);
            }
            pos.Add(p); dist.Add(d); up.Add(u); near.Add(t);
            if (d > lower) lower = d;
            return pos.Count - 1;
        }
        double ubound(Index3i t)
        {
            double th = lower;
            double u = bounds.UpperBound(pos[t.a], pos[t.b], pos[t.c], up[t.a], up[t.b], up[t.c], near[t.a], near[t.b], near[t.c], th);
            if (patchMode > 0 && !(u <= th) && !(near[t.a] == near[t.b] && near[t.b] == near[t.c])) {
                long t0 = Stopwatch.GetTimestamp();
                double pb = patchFn(pos[t.a], pos[t.b], pos[t.c], near[t.a], near[t.b], near[t.c]);
                res.PatchMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                res.PatchCalls++;
                if (patchMode == 2) {
                    Vector3d nn = (pos[t.b] - pos[t.a]).Cross(pos[t.c] - pos[t.a]).Normalized;
                    double c = Math.Max(Math.Abs(nn.x), Math.Max(Math.Abs(nn.y), Math.Abs(nn.z)));
                    pb = (c > 0) ? pb / c : double.PositiveInfinity;
                }
                if (!double.IsInfinity(pb)) res.PatchFinite++;
                double dmax = Math.Max(up[t.a], Math.Max(up[t.b], up[t.c]));
                double v = Math.Min(u, Math.Max(dmax, pb));
                if (v < u) res.PatchUseful++;
                u = v;
            }
            return u;
        }

        var sw = Stopwatch.StartNew();
        var vmap = new Dictionary<int, int>();
        foreach (int vid in a.VertexIndices())
            if (a.GetVtxEdgeCount(vid) > 0) vmap[vid] = add(a.GetVertex(vid));
        var heap = new PriorityQueue<Index3i, double>();
        var pending = new List<Index3i>();
        foreach (int tid in a.TriangleIndices()) {
            Index3i t = a.GetTriangle(tid);
            pending.Add(new Index3i(vmap[t.a], vmap[t.b], vmap[t.c]));
        }
        foreach (var t in pending) {
            double u = ubound(t);
            if (!(u <= lower)) heap.Enqueue(t, -(double.IsNaN(u) ? double.MaxValue : u));
        }
        int splits = 0;
        while (heap.Count > 0 && splits < maxSubdivisions) {
            heap.TryPeek(out Index3i t, out double negTop);
            if (!(-negTop > lower + tol)) break;
            heap.Dequeue();
            ++splits;
            int m01 = add(0.5 * (pos[t.a] + pos[t.b])), m12 = add(0.5 * (pos[t.b] + pos[t.c])), m20 = add(0.5 * (pos[t.c] + pos[t.a]));
            foreach (var c in new[] { new Index3i(t.a, m01, m20), new Index3i(m01, t.b, m12), new Index3i(m20, m12, t.c), new Index3i(m01, m12, m20) }) {
                double u = ubound(c);
                if (!(u <= lower)) heap.Enqueue(c, -(double.IsNaN(u) ? double.MaxValue : u));
            }
        }
        res.Ms = sw.Elapsed.TotalMilliseconds;
        res.Subdivisions = splits;
        res.Lower = lower;
        res.Upper = heap.Count > 0 ? Math.Max(lower, -PeekPriority(heap)) : lower;
        res.Converged = !(res.Upper > lower + tol);
        return res;
    }

    static double PeekPriority(PriorityQueue<Index3i, double> h) { h.TryPeek(out _, out double p); return p; }

    static DMesh3 Reduced(DMesh3 big, int divisor)
    {
        var red = new DMesh3(big);
        new Reducer(red).ReduceToTriangleCount(big.TriangleCount / divisor);
        return red;
    }

    // One-sided reduced -> original rows, and a few non-reduction rows. runs: median of that many timed runs
    static void PatchOracle(int runs)
    {
        var cases = new List<(string, DMesh3, DMesh3)>();
        var big1 = Bumpy(9, 30); var big2 = Bumpy(11, 60);
        cases.Add(("reduced 1/8 of 5k bumpy", Reduced(big1, 8), big1));
        cases.Add(("reduced 1/32 of 5k bumpy", Reduced(big1, 32), big1));
        cases.Add(("reduced 1/8 of 21k bumpy", Reduced(big2, 8), big2));
        cases.Add(("reduced 1/32 of 21k bumpy", Reduced(big2, 32), big2));
        var smooth = Sphere(60, 1, Vector3d.Zero);
        cases.Add(("reduced 1/16 of 21k sphere", Reduced(smooth, 16), smooth));
        cases.Add(("spheres6 vs 20", Sphere(6, 1, Vector3d.Zero), Sphere(20, 1, Vector3d.Zero)));
        cases.Add(("bumpy5 -> bumpy6", Bumpy(5, 12), Bumpy(6, 8)));
        cases.Add(("coplanar grids 16 -> 17 (jitter .3)", Grid(16, 0, 1), Grid(17, 0.3, 2)));
        Console.WriteLine("Certified cascade, one-sided A -> B, tolerance 1e-4 of the diagonal. subdiv = subdivisions; ms = median of " + runs +
            " runs, one thread; (p) = of it the patch bound's time; k* = how many times slower the certified patch bound may be than the uncertified one before the run is no faster than without it");
        Console.WriteLine("| case (tris A / B) | no patch: subdiv, ms | patch oracle: subdiv, ms (p) | subdiv saved, time saved | k* | axis-aligned patch (x 1/cos): subdiv, ms (p) | subdiv saved, k* |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        foreach (var (name, a, b) in cases) {
            double tol = 1e-4 * a.GetBounds().DiagonalLength;
            var rs = new OracleResult[3]; var ts = new List<double>[] { new List<double>(), new List<double>(), new List<double>() };
            var ps = new List<double>[] { new List<double>(), new List<double>(), new List<double>() };
            for (int r = 0; r <= runs; ++r)
                for (int m = 0; m < 3; ++m) {
                    rs[m] = Oracle(a, b, true, m, tol, 3000000);
                    if (r > 0) { ts[m].Add(rs[m].Ms); ps[m].Add(rs[m].PatchMs); }
                }
            double[] med = new double[3], pm = new double[3];
            for (int m = 0; m < 3; ++m) { ts[m].Sort(); ps[m].Sort(); med[m] = ts[m][runs / 2]; pm[m] = ps[m][runs / 2]; }
            string kstar(int m) => pm[m] > 0 ? ((med[0] - (med[m] - pm[m])) / pm[m]).ToString("F1") : "-";
            string saved(int m) => $"{100.0 * (rs[0].Subdivisions - rs[m].Subdivisions) / Math.Max(1, rs[0].Subdivisions):F0}%";
            bool conv = rs[0].Converged && rs[1].Converged && rs[2].Converged;
            Console.WriteLine($"| {name} ({a.TriangleCount} / {b.TriangleCount}){(conv ? "" : " (not converged)")} | {rs[0].Subdivisions}, {med[0]:F0} | " +
                $"{rs[1].Subdivisions}, {med[1]:F0} ({pm[1]:F0}) | {saved(1)}, {100.0 * (med[0] - med[1]) / med[0]:F0}% | {kstar(1)} | " +
                $"{rs[2].Subdivisions}, {med[2]:F0} ({pm[2]:F0}) | {saved(2)}, {kstar(2)} |");
        }
    }
}

// Cost of the building block of the certified patch bound (docs/design/CERTIFIED_PATCH_BOUND.md section 7): the sign of
// det[q - p, r - p, w] with a running error bound (a double evaluation, the permanent, one comparison), against the same sign by
// Orient2d of AdaptivePredicates (exact, adaptive; a projection along a coordinate axis) and against an outward-rounded interval
// evaluation (a local struct with the semantics of ExactInterval: every operation rounded to nearest, then one step out).
static partial class Program
{
    struct Iv
    {
        public double Lo, Hi;
        public Iv(double lo, double hi) { Lo = lo; Hi = hi; }
        public static Iv Of(double x) => new Iv(x, x);
        public static Iv operator +(Iv a, Iv b) => new Iv(Math.BitDecrement(a.Lo + b.Lo), Math.BitIncrement(a.Hi + b.Hi));
        public static Iv operator -(Iv a, Iv b) => new Iv(Math.BitDecrement(a.Lo - b.Hi), Math.BitIncrement(a.Hi - b.Lo));
        public static Iv operator *(Iv a, Iv b)
        {
            double p1 = a.Lo * b.Lo, p2 = a.Lo * b.Hi, p3 = a.Hi * b.Lo, p4 = a.Hi * b.Hi;
            return new Iv(Math.BitDecrement(Math.Min(Math.Min(p1, p2), Math.Min(p3, p4))), Math.BitIncrement(Math.Max(Math.Max(p1, p2), Math.Max(p3, p4))));
        }
    }

    static int SignFiltered(Vector3d p, Vector3d q, Vector3d r, Vector3d w)
    {
        double ax = q.x - p.x, ay = q.y - p.y, az = q.z - p.z, bx = r.x - p.x, by = r.y - p.y, bz = r.z - p.z;
        double m1 = by * w.z, m2 = bz * w.y, m3 = bz * w.x, m4 = bx * w.z, m5 = bx * w.y, m6 = by * w.x;
        double det = ax * (m1 - m2) + ay * (m3 - m4) + az * (m5 - m6);
        double perm = Math.Abs(ax) * (Math.Abs(m1) + Math.Abs(m2)) + Math.Abs(ay) * (Math.Abs(m3) + Math.Abs(m4)) + Math.Abs(az) * (Math.Abs(m5) + Math.Abs(m6));
        double err = 1.1102230246251566e-15 * perm;        // 10 u, u = 2^-53
        return det > err ? 1 : (det < -err ? -1 : 0);
    }

    static int SignInterval(Vector3d p, Vector3d q, Vector3d r, Vector3d w)
    {
        Iv ax = Iv.Of(q.x) - Iv.Of(p.x), ay = Iv.Of(q.y) - Iv.Of(p.y), az = Iv.Of(q.z) - Iv.Of(p.z);
        Iv bx = Iv.Of(r.x) - Iv.Of(p.x), by = Iv.Of(r.y) - Iv.Of(p.y), bz = Iv.Of(r.z) - Iv.Of(p.z);
        Iv wx = Iv.Of(w.x), wy = Iv.Of(w.y), wz = Iv.Of(w.z);
        Iv d = ax * (by * wz - bz * wy) + ay * (bz * wx - bx * wz) + az * (bx * wy - by * wx);
        return d.Lo > 0 ? 1 : (d.Hi < 0 ? -1 : 0);
    }

    delegate int Orient2dFn(double ax, double ay, double bx, double by, double cx, double cy);

    static void PredCost()
    {
        var rnd = new Random(5);
        int n = 4096;
        var pts = new Vector3d[3 * n]; var ws = new Vector3d[n];
        for (int i = 0; i < n; ++i) {
            Vector3d c = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
            for (int k = 0; k < 3; ++k) pts[3 * i + k] = c + 1e-2 * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), 0.01 * rnd.NextDouble());
            ws[i] = new Vector3d(0.05 * rnd.NextDouble(), 0.05 * rnd.NextDouble(), 1);
        }
        var o2 = (Orient2dFn)Delegate.CreateDelegate(typeof(Orient2dFn), typeof(TriangleDeviationBounds).Assembly.GetType("g3.AdaptivePredicates").GetMethod("Orient2d"));
        int reps = 2000;
        long sink = 0; int zeros = 0;
        double time(Func<int, int, int> f)
        {
            for (int r = 0; r < 50; ++r) for (int i = 0; i < n; ++i) sink += f(i, r);
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < reps; ++r) for (int i = 0; i < n; ++i) sink += f(i, r);
            return sw.Elapsed.TotalMilliseconds * 1e6 / ((double)reps * n);
        }
        double nsF = time((i, r) => SignFiltered(pts[3 * i], pts[3 * i + 1], pts[3 * i + 2], ws[i]));
        double nsI = time((i, r) => SignInterval(pts[3 * i], pts[3 * i + 1], pts[3 * i + 2], ws[i]));
        double nsE = time((i, r) => o2(pts[3 * i].x, pts[3 * i].y, pts[3 * i + 1].x, pts[3 * i + 1].y, pts[3 * i + 2].x, pts[3 * i + 2].y));
        for (int i = 0; i < n; ++i) if (SignFiltered(pts[3 * i], pts[3 * i + 1], pts[3 * i + 2], ws[i]) == 0) zeros++;
        Console.WriteLine($"ns per sign (4096 random small triangles, one thread): filtered double {nsF:F1}, interval {nsI:F1}, AdaptivePredicates.Orient2d {nsE:F1}; filter undecided {zeros} of {n} (sink {sink})");
    }
}
