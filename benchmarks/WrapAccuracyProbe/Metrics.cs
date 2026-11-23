// Metrics of the probe: signed volume, certified one-sided Hausdorff distances (MeshHausdorffDistance), area shares of surface that
// lies on no surface of the other mesh, and the validity verdict. Only public library API.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using g3;
using gs;

static class Metrics
{
    public static double SignedVolume(DMesh3 m, Vector3d origin)
    {
        double v = 0;
        foreach (int tid in m.TriangleIndices()) {
            Index3i t = m.GetTriangle(tid);
            Vector3d a = m.GetVertex(t.a) - origin, b = m.GetVertex(t.b) - origin, c = m.GetVertex(t.c) - origin;
            v += a.Dot(b.Cross(c));
        }
        return v / 6.0;
    }

    public static double Area(DMesh3 m)
    {
        double a = 0;
        foreach (int tid in m.TriangleIndices()) a += m.GetTriArea(tid);
        return a;
    }

    public class Hd { public double Lower, Upper; public bool Converged; public int Subdivisions; public double Ms; }

    // one-sided distance from A to B, certified interval [Lower, Upper] when Converged; tol absolute; time limit in seconds
    public static Hd OneSided(DMesh3 a, DMesh3 b, DMeshAABBTree3 treeB, double tol, double seconds)
    {
        var sw = Stopwatch.StartNew();
        var h = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = tol, SpatialB = treeB, MaxSubdivisions = 3000000 };
        h.CancelF = () => sw.Elapsed.TotalSeconds > seconds;
        bool ok = h.Compute();
        return new Hd { Lower = h.LowerBound, Upper = h.UpperBound, Converged = ok && h.Converged, Subdivisions = h.Subdivisions, Ms = sw.Elapsed.TotalMilliseconds };
    }

    // area-weighted share of `from` whose sample points are farther than tau from `to` (deterministic samples: n per mesh)
    public static double FarShare(DMesh3 from, DMeshAABBTree3 toTree, double tau, int n, out double maxDist, int seed = 1)
    {
        var tids = new List<int>(); var cum = new List<double>(); double total = 0;
        foreach (int tid in from.TriangleIndices()) { total += from.GetTriArea(tid); tids.Add(tid); cum.Add(total); }
        if (tids.Count == 0 || !(total > 0)) { maxDist = 0; return 0; }
        // stratified: sample k lies in the triangle at cumulative area (k + 0.5) / n * total; random point in it
        var pts = new Vector3d[n];
        var rnd = new Random(seed);
        int ti = 0;
        for (int k = 0; k < n; ++k) {
            double target = (k + 0.5) / n * total;
            while (ti < cum.Count - 1 && cum[ti] < target) ti++;
            Index3i t = from.GetTriangle(tids[ti]);
            double u = rnd.NextDouble(), v = rnd.NextDouble();
            if (u + v > 1) { u = 1 - u; v = 1 - v; }
            Vector3d a = from.GetVertex(t.a), b = from.GetVertex(t.b), c = from.GetVertex(t.c);
            pts[k] = a + u * (b - a) + v * (c - a);
        }
        var far = new bool[n]; var dist = new double[n];
        Parallel.For(0, n, k => {
            int tid = toTree.FindNearestTriangle(pts[k], out double d2);
            dist[k] = Math.Sqrt(d2); far[k] = dist[k] > tau;
        });
        int cnt = 0; double mx = 0;
        for (int k = 0; k < n; ++k) { if (far[k]) cnt++; if (dist[k] > mx) mx = dist[k]; }
        maxDist = mx;
        return (double)cnt / n;
    }

    // area-stratified sample distances from `from` to `toTree`: p50, p90, p99 and the sampled maximum
    public static double[] DistPercentiles(DMesh3 from, DMeshAABBTree3 toTree, int n, int seed = 3)
    {
        var tids = new List<int>(); var cum = new List<double>(); double total = 0;
        foreach (int tid in from.TriangleIndices()) { total += from.GetTriArea(tid); tids.Add(tid); cum.Add(total); }
        if (tids.Count == 0 || !(total > 0)) return new double[] { 0, 0, 0, 0 };
        var pts = new Vector3d[n]; var rnd = new Random(seed); int ti = 0;
        for (int k = 0; k < n; ++k) {
            double target = (k + 0.5) / n * total;
            while (ti < cum.Count - 1 && cum[ti] < target) ti++;
            Index3i t = from.GetTriangle(tids[ti]);
            double u = rnd.NextDouble(), v = rnd.NextDouble();
            if (u + v > 1) { u = 1 - u; v = 1 - v; }
            Vector3d a = from.GetVertex(t.a), b = from.GetVertex(t.b), c = from.GetVertex(t.c);
            pts[k] = a + u * (b - a) + v * (c - a);
        }
        var dist = new double[n];
        Parallel.For(0, n, k => { toTree.FindNearestTriangle(pts[k], out double d2); dist[k] = Math.Sqrt(d2); });
        Array.Sort(dist);
        return new[] { dist[n / 2], dist[(int)(0.9 * (n - 1))], dist[(int)(0.99 * (n - 1))], dist[n - 1] };
    }

    public static string Verdict(DMesh3 m)
    {
        try {
            string r = MeshRobustRepair.NotCleanReason(m);
            return r == "" ? "clean" : r;
        } catch (Exception e) { return "exception: " + e.GetType().Name; }
    }

    public static int BoundaryEdges(DMesh3 m)
    {
        int c = 0;
        foreach (int eid in m.EdgeIndices()) if (m.IsBoundaryEdge(eid)) c++;
        return c;
    }

    public static int BoundaryLoopCount(DMesh3 m)
    {
        try { var bl = new MeshBoundaryLoops(m, true); return bl.Count + bl.SpanCount; } catch { return -1; }
    }
}
