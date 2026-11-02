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
                if (!op.Compute())
                    break;
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

    // the vertices of the degenerate triangles (MeshRobustRepair.NotCleanReason's threshold, or exactly collinear) and of
    // the intersecting pairs
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
