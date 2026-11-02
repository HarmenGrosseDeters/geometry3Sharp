using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

// Prototype of option B of the snap-rounding experiment (docs/design/SAFE_ROUNDING.md sections 3 and 7): after
// SafeRounding, while defects are left, snap the vertices of the defect triangles (degenerate, or in an intersecting
// pair) to a grid and resolve the mesh again (ExactMeshBoolean, union with nothing, with SafeRounding within the drift
// left), after the iterated snap rounding of Valque and Lazard (CGF 44(5), 2025; from the published description only,
// see docs/SOURCES.md). The grids run from none (a second resolve only) over 2^j ulps of the largest coordinate up to
// the drift left; for each grid up to Rounds rounds, each from the result of the last. A grid is taken when its result is
// clean; the drift adds up: the first repair's, the snap's (the farthest a vertex moves) and the second repair's.
// Not proven: the second resolve keeps the outer boundary of the snapped solid, so its surface lies on the snapped
// surface, but parts of the snapped surface may be removed (inside the solid); the experiment measures the Hausdorff
// distance to the exact result instead.
sealed class SnapFallback
{
    public DMesh3 Result;
    public double Drift;
    public bool Clean;
    public bool SnapGrids = true;
    public int Rounds, RemovedComponents;
    public double GridUlps;
    public int MaxRounds = 3;
    /// <summary>
    /// the published method instead (SAFE_ROUNDING.md section 7): a grid fixed per axis (a power of two that puts the
    /// largest absolute coordinate on the axis into [2^23, 2^24) grid units), the defect vertices and every vertex in a
    /// voxel with one of them (hot voxels) snapped, resolved again, at most 5 iterations; no drift bound (the drift is
    /// reported, and whether it stays within maxLength). Deviations: the defects include degenerate triangles (the paper
    /// only knows intersecting pairs), and the resolve is the union with nothing (the paper subdivides exactly)
    /// </summary>
    public bool PaperGrid = false;
    /// <summary> the paper's grid: whether the drift stayed within the first repair's bound </summary>
    public bool WithinBound = true;

    readonly DMesh3 start;
    readonly MeshRoundingRepair first;

    public SnapFallback(DMesh3 result, MeshRoundingRepair repair)
    {
        start = result;
        first = repair;
        Result = result;
        Drift = repair?.MaxDrift ?? 0;
        Clean = repair != null && repair.IsClean && gs.MeshRobustRepair.NotCleanReasons(result) == "";
    }

    public void Apply()
    {
        if (Clean || first == null || start.TriangleCount == 0)
            return;
        if (PaperGrid) {
            apply_paper();
            return;
        }
        double maxLength = first.MaxLength;
        double ulp = Program.Ulp(Program.Largest(start));
        var grids = new List<double> { 0 };
        if (SnapGrids)
            for (double g = 2 * ulp; g <= maxLength; g *= 4)
                grids.Add(g);
        foreach (double g in grids) {
            DMesh3 cur = start;
            double used = first.MaxDrift;
            int removed = 0, rounds = 0;
            bool clean = false;
            while (rounds < MaxRounds) {
                rounds++;
                var m = new DMesh3(cur);
                double snap = 0;
                if (g > 0)
                    foreach (int v in DefectVertices(cur)) {
                        Vector3d p = m.GetVertex(v);
                        var q = new Vector3d(Math.Round(p.x / g) * g, Math.Round(p.y / g) * g, Math.Round(p.z / g) * g);
                        snap = Math.Max(snap, q.Distance(p));
                        m.SetVertex(v, q);
                    }
                used += snap;
                double leftOver = maxLength - used;
                if (!(leftOver > 0))
                    break;
                var op = new ExactMeshBoolean(m, new DMesh3(), Ops.Union);
                var trace = Environment.GetEnvironmentVariable("SNAP_TRACE") == "1" ? System.Diagnostics.Stopwatch.StartNew() : null;
                // the drift left as the bound of the second repair: the ulp term where the first one used it (far from
                // the origin), else the relative one
                double diag = gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength;
                if (first.MaxLength > first.MaxDisplacement * gs.MeshRepairMetrics.TriangleBounds(start).DiagonalLength) {
                    op.RoundingMaxDisplacement = 0;
                    op.RoundingUlpFactor = leftOver / Program.Ulp(Program.Largest(m));
                } else {
                    op.RoundingMaxDisplacement = leftOver / diag;
                    op.RoundingUlpFactor = 0;
                }
                if (trace != null)
                    Console.Error.WriteLine($"grid {g / ulp:G4} ulps round {rounds}: {m.TriangleCount} triangles, snapped {(g > 0 ? DefectVertices(cur).Count : 0)} vertices, resolving...");
                if (!op.Compute())
                    break;
                if (trace != null)
                    Console.Error.WriteLine($"  resolved in {trace.Elapsed.TotalSeconds:F1} s, clean {op.RoundingClean}");
                used += op.Rounding.MaxDrift;
                removed += op.Rounding.RemovedComponents;
                cur = op.Result;
                if (op.RoundingClean && gs.MeshRobustRepair.NotCleanReasons(cur) == "") {
                    clean = true;
                    break;
                }
                if (g == 0)
                    break;          // (a second resolve without a snap: once)
            }
            if (clean && used <= maxLength) {
                Result = cur; Drift = used; Clean = true; Rounds = rounds; RemovedComponents = removed; GridUlps = g / ulp;
                return;
            }
        }
    }

    void apply_paper()
    {
        // the grid unit per axis: 2^(e - 23), e the exponent of the largest absolute coordinate on the axis
        AxisAlignedBox3d b = gs.MeshRepairMetrics.TriangleBounds(start);
        var unit = new double[3];
        for (int k = 0; k < 3; ++k) {
            double m = Math.Max(Math.Abs(b.Min[k]), Math.Abs(b.Max[k]));
            unit[k] = m > 0 ? Program.Ulp(m) * Math.Pow(2, 52 - 23) : 1;
        }
        DMesh3 cur = start;
        double used = first.MaxDrift;
        int removed = 0, rounds = 0;
        while (rounds < 5) {
            HashSet<int> verts = DefectVertices(cur);
            if (verts.Count == 0)
                break;
            rounds++;
            Func<Vector3d, (long, long, long)> voxel = p => ((long)Math.Round(p.x / unit[0]), (long)Math.Round(p.y / unit[1]), (long)Math.Round(p.z / unit[2]));
            // (hot voxels: every vertex in a voxel with a defect vertex moves with it)
            var hot = new HashSet<(long, long, long)>();
            foreach (int v in verts) hot.Add(voxel(cur.GetVertex(v)));
            var m = new DMesh3(cur);
            double snap = 0;
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                var key = voxel(p);
                if (hot.Contains(key) == false) continue;
                var q = new Vector3d(key.Item1 * unit[0], key.Item2 * unit[1], key.Item3 * unit[2]);
                snap = Math.Max(snap, q.Distance(p));
                m.SetVertex(v, q);
            }
            var op = new ExactMeshBoolean(m, new DMesh3(), Ops.Union);
            if (!op.Compute())
                break;
            // (the snap counts only once its mesh is taken: a failed resolve leaves the result unsnapped)
            used += snap + op.Rounding.MaxDrift;
            removed += op.Rounding.RemovedComponents;
            cur = op.Result;
            if (op.RoundingClean && gs.MeshRobustRepair.NotCleanReasons(cur) == "") {
                Clean = true;
                break;
            }
        }
        Result = cur; Drift = used; Rounds = rounds; RemovedComponents = removed;
        GridUlps = unit.Max() / Program.Ulp(Program.Largest(start));
        WithinBound = used <= first.MaxLength;
    }

    // the vertices of the degenerate triangles (MeshRobustRepair.NotCleanReason's threshold, or exactly collinear) and of
    // the intersecting pairs
    internal static List<int> DefectTriangles(DMesh3 m)
    {
        double diag = gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength;
        double eps = 1e-12 * diag * diag;
        var tris = new List<int>();
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            var T = new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c));
            if (m.GetTriArea(t) < eps || ExactTriangleIntersection.IsDegenerate(ref T))
                tris.Add(t);
        }
        foreach (Index2i p in gs.MeshRepairMetrics.SelfIntersectionPairs(m)) { tris.Add(p.a); tris.Add(p.b); }
        return tris;
    }

    static HashSet<int> DefectVertices(DMesh3 m)
    {
        double diag = gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength;
        double eps = 1e-12 * diag * diag;
        var tris = new List<int>();
        foreach (int t in m.TriangleIndices()) {
            Triangle3d T = m.GetTriangle(t) is Index3i tv ? new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c)) : default;
            if (m.GetTriArea(t) < eps || ExactTriangleIntersection.IsDegenerate(ref T))
                tris.Add(t);
        }
        foreach (Index2i p in gs.MeshRepairMetrics.SelfIntersectionPairs(m)) { tris.Add(p.a); tris.Add(p.b); }
        var verts = new HashSet<int>();
        foreach (int t in tris) { Index3i tv = m.GetTriangle(t); verts.Add(tv.a); verts.Add(tv.b); verts.Add(tv.c); }
        return verts;
    }
}
