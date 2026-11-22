using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;
using gs;

// A simple floating-point prototype of local self-intersection untangling, for the upper-bound measurement of
// docs/research/SELF_INTERSECTION_UNTANGLING_2026-11.md. Benchmark only, not library code. The connectivity is never changed:
// only vertices move. Every success is decided by the exact check (MeshRobustRepair.NotCleanReason), never by the pushes.
//
// One round:
//  1. the intersecting triangle pairs, exactly (MeshRepairMetrics.SelfIntersectionPairs); none left: stop.
//  2. Signed push (option Signed; the "shape-level" signal): the winding number just in front of every triangle should be 0.
//     Where it is >= 1 the triangle is buried in other material and retreats against its normal; where it is <= -1 the
//     triangle bounds an inverted region and advances along its normal. A fraction of its shortest edge per round.
//  3. Local push (the "mesh-level" signal) for the pairs the signed push does not move: the minimum translation that
//     separates the two triangles (smallest overlap over the separating-axis candidates), half to each triangle, plus a
//     margin. Pairs that share a vertex or an edge (folds) pull their vertices towards the centroid of their one-ring.
//  4. Diffusion (option Diffusion = m): m Jacobi averaging sweeps carry the displacement to the vertices around the moved ones.
// After a clean result, the pull-back searches the smallest fraction t of the total displacement that is still clean
// (bisection, every candidate decided by the exact check).
sealed class UntangleOptions
{
    public bool Signed = true;
    public int Diffusion = 0;
    public int MaxRounds = 200;
    public int StallRounds = 40;        // stop after this many rounds without fewer intersecting pairs
    public double Step = 0.25;          // signed push per round, as a fraction of the triangle's shortest edge
    public double Margin = 1e-3;        // clearance of the local push, as a fraction of the mean edge length
    public bool PullBack = true;
    public string Name { get { return (Signed ? "signed" : "local") + "+d" + Diffusion; } }
}

sealed class UntangleResult
{
    public DMesh3 Mesh;                 // the last state (clean or not)
    public bool Clean;
    public string Reason = "";          // NotCleanReason of Mesh (of an open input: "self-intersections" or "")
    public int Rounds, PairsStart, PairsEnd, PairsBest;
    public double PullBackT = 1;
    public double MaxMove;              // the largest vertex displacement of Mesh against the input
    public long Milliseconds;
}

static class Untangler
{
    public static UntangleResult Run(DMesh3 input, UntangleOptions o)
    {
        var sw = Stopwatch.StartNew();
        var m = new DMesh3(input, false, MeshComponents.None);
        var r = new UntangleResult { Mesh = m };
        double meanEdge = 0;
        foreach (int e in m.EdgeIndices()) { Index2i ev = m.GetEdgeV(e); meanEdge += m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); }
        meanEdge /= Math.Max(1, m.EdgeCount);
        double margin = o.Margin * meanEdge;

        int best = int.MaxValue, bestRound = 0;
        for (int round = 0; ; ++round) {
            List<Index2i> pairs = MeshRepairMetrics.SelfIntersectionPairs(m);
            if (round == 0) r.PairsStart = pairs.Count;
            r.PairsEnd = pairs.Count; r.Rounds = round;
            if (pairs.Count < best) { best = pairs.Count; bestRound = round; }
            if (pairs.Count == 0 || round >= o.MaxRounds || round - bestRound >= o.StallRounds)
                break;
            var D = new Vector3d[m.MaxVertexID];
            var W = new int[m.MaxVertexID];
            bool[] flagged = new bool[m.MaxTriangleID];
            if (o.Signed)
                signed_push(m, o.Step, D, W, flagged);
            foreach (Index2i p in pairs) {
                Index3i ta = m.GetTriangle(p.a), tb = m.GetTriangle(p.b);
                int shared = 0;
                for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) if (ta[i] == tb[j]) shared++;
                if (shared > 0) {
                    for (int i = 0; i < 3; ++i) { relax(m, ta[i], D, W); relax(m, tb[i], D, W); }
                } else if (!(flagged[p.a] || flagged[p.b])) {
                    Vector3d dir; double depth;
                    min_translation(m, ta, tb, out dir, out depth);
                    Vector3d d = dir * (0.5 * (depth + margin));
                    for (int i = 0; i < 3; ++i) { D[ta[i]] += d; W[ta[i]]++; D[tb[i]] -= d; W[tb[i]]++; }
                }
            }
            foreach (int v in m.VertexIndices())
                if (W[v] > 1) D[v] /= W[v];
            for (int it = 0; it < o.Diffusion; ++it) {
                var next = (Vector3d[])D.Clone();
                foreach (int v in m.VertexIndices()) {
                    if (W[v] > 0) continue;     // (the moved vertices are the sources)
                    Vector3d s = Vector3d.Zero; int n = 0;
                    foreach (int u in m.VtxVerticesItr(v)) { s += D[u]; n++; }
                    if (n > 0) next[v] = s / n;
                }
                D = next;
            }
            foreach (int v in m.VertexIndices())
                if (D[v].LengthSquared > 0) m.SetVertex(v, m.GetVertex(v) + D[v]);
        }
        r.PairsBest = best;
        // (an open input can only be judged by its self-intersections: the holes are not the untangler's)
        bool open = !input.IsClosed();
        Func<DMesh3, string> reason = x => open ? (MeshRepairMetrics.HasSelfIntersections(x) ? "self-intersections" : "") : MeshRobustRepair.NotCleanReason(x);
        r.Reason = reason(m);
        r.Clean = r.Reason == "";
        if (r.Clean && o.PullBack && r.PairsStart > 0) {
            double lo = 0, hi = 1;      // hi is clean, lo is not (the input)
            DMesh3 bestMesh = m;
            for (int it = 0; it < 24; ++it) {
                double t = 0.5 * (lo + hi);
                var c = new DMesh3(input, false, MeshComponents.None);
                foreach (int v in c.VertexIndices())
                    c.SetVertex(v, input.GetVertex(v) + t * (m.GetVertex(v) - input.GetVertex(v)));
                if (reason(c) == "") { hi = t; bestMesh = c; } else lo = t;
            }
            r.PullBackT = hi; r.Mesh = bestMesh;
        }
        foreach (int v in input.VertexIndices())
            r.MaxMove = Math.Max(r.MaxMove, r.Mesh.GetVertex(v).Distance(input.GetVertex(v)));
        r.Milliseconds = sw.ElapsedMilliseconds;
        return r;
    }

    static void signed_push(DMesh3 m, double step, Vector3d[] D, int[] W, bool[] flagged)
    {
        var tree = new DMeshAABBTree3(m, true);
        tree.FastWindingNumber(Vector3d.Zero);
        var dirs = new Vector3d[m.MaxTriangleID];
        gParallel.ForEach(m.TriangleIndices(), t => {
            Vector3d n, c; double area;
            m.GetTriInfo(t, out n, out area, out c);
            if (area <= 0) return;
            double h = 1e-3 * Math.Sqrt(area);
            double wf = Math.Round(tree.FastWindingNumber(c + h * n));
            if (wf == 0) return;
            Index3i tv = m.GetTriangle(t);
            Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), cc = m.GetVertex(tv.c);
            double shortest = Math.Min(a.Distance(b), Math.Min(b.Distance(cc), cc.Distance(a)));
            dirs[t] = (wf >= 1 ? -1.0 : 1.0) * step * shortest * n;
            flagged[t] = true;
        });
        foreach (int t in m.TriangleIndices())
            if (flagged[t]) {
                Index3i tv = m.GetTriangle(t);
                for (int i = 0; i < 3; ++i) { D[tv[i]] += dirs[t]; W[tv[i]]++; }
            }
    }

    static void relax(DMesh3 m, int v, Vector3d[] D, int[] W)
    {
        Vector3d s = Vector3d.Zero; int n = 0;
        foreach (int u in m.VtxVerticesItr(v)) { s += m.GetVertex(u); n++; }
        if (n == 0) return;
        D[v] += 0.5 * (s / n - m.GetVertex(v)); W[v]++;
    }

    // the smallest translation of triangle A (dir * depth) that separates it from B, over the separating-axis candidates of
    // two triangles: the two normals, the nine edge cross products and the six in-plane edge normals
    static void min_translation(DMesh3 m, Index3i ta, Index3i tb, out Vector3d dir, out double depth)
    {
        var A = new[] { m.GetVertex(ta.a), m.GetVertex(ta.b), m.GetVertex(ta.c) };
        var B = new[] { m.GetVertex(tb.a), m.GetVertex(tb.b), m.GetVertex(tb.c) };
        Vector3d nA = (A[1] - A[0]).Cross(A[2] - A[0]), nB = (B[1] - B[0]).Cross(B[2] - B[0]);
        var axes = new List<Vector3d> { nA, nB };
        for (int i = 0; i < 3; ++i) {
            Vector3d ea = A[(i + 1) % 3] - A[i], eb = B[(i + 1) % 3] - B[i];
            axes.Add(nA.Cross(ea)); axes.Add(nB.Cross(eb));
            for (int j = 0; j < 3; ++j)
                axes.Add(ea.Cross(B[(j + 1) % 3] - B[j]));
        }
        dir = Vector3d.AxisZ; depth = double.MaxValue;
        foreach (Vector3d raw in axes) {
            double len = raw.Length;
            if (!(len > 1e-300)) continue;
            Vector3d ax = raw / len;
            double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
            for (int i = 0; i < 3; ++i) {
                double pa = ax.Dot(A[i]), pb = ax.Dot(B[i]);
                minA = Math.Min(minA, pa); maxA = Math.Max(maxA, pa); minB = Math.Min(minB, pb); maxB = Math.Max(maxB, pb);
            }
            double plus = Math.Max(0, maxB - minA), minus = Math.Max(0, maxA - minB);   // move A by +ax / by -ax
            if (plus < depth) { depth = plus; dir = ax; }
            if (minus < depth) { depth = minus; dir = -ax; }
        }
        if (depth == double.MaxValue) depth = 0;
    }
}
