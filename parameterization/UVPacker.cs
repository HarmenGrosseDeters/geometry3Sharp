// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;

namespace g3
{
    /// <summary>
    /// Packs the UV charts of a mesh into the unit square without overlap. A chart is a set of triangles
    /// connected through shared vertices (with per-vertex UVs, seams are duplicated vertices, so every
    /// parameterized component is one chart).
    ///
    /// Each chart is optionally rescaled to its surface area (so that texel density is uniform), rotated to its
    /// minimum-area bounding rectangle (rotating calipers, ContMinBox2), and the rectangles are packed with the
    /// skyline bottom-left heuristic (J. Jylänki, A Thousand Ways to Pack the Bin, 2010), trying 90 degree
    /// rotations and several bin widths for the smallest square. Charts keep their relative scale.
    /// Charts are at least Padding apart and from the border of the square. The placed charts are checked with
    /// exact predicates: a chart whose rotated UVs overlap or flip (rounding at exactly collinear vertices) is
    /// placed again without the rotation, which keeps the order of each coordinate.
    /// </summary>
    public class UVPacker
    {
        public DMesh3 Mesh;
        /// <summary> UV per vertex id; null: the vertex UVs of the mesh </summary>
        public IList<Vector2d> UV;
        /// <summary> minimum gap between charts and to the border, in units of the unit square </summary>
        public double Padding = 0.005;
        /// <summary> rotate charts to their minimum-area bounding rectangle and by 90 degrees when it packs better </summary>
        public bool AllowRotation = true;
        /// <summary>
        /// check the placed charts with exact predicates and undo rounding defects (see the class summary). Its cost
        /// grows with the overlapping triangle pairs of the input: switch it off for charts that overlap themselves
        /// in the input UVs anyway
        /// </summary>
        public bool ExactCheck = true;
        /// <summary> scale each chart so that its UV area equals its surface area before packing </summary>
        public bool ScaleChartsToSurfaceArea = true;

        public ParameterizationResult Result;
        /// <summary> number of charts after Compute() </summary>
        public int ChartCount;
        /// <summary> chart index by triangle id (-1 for unused ids) after Compute() </summary>
        public int[] TriangleChart;
        /// <summary> fraction of the unit square covered by UV triangles after Compute() </summary>
        public double Coverage;
        /// <summary> placed bounding rectangle of each chart (in the unit square), after Compute() </summary>
        public List<AxisAlignedBox2d> ChartBounds;
        /// <summary>
        /// gap kept between the bounding rectangles of the charts and to the border, in units of the unit square,
        /// after Compute(). Below Padding (and Result.Converged false) if the padding could not be met, eg when
        /// there are so many charts that the gaps alone fill the square.
        /// </summary>
        public double AchievedPadding;
        /// <summary>
        /// charts placed without the rotation to their minimum-area rectangle, after Compute(): the rotated UVs had
        /// overlapping, inverted or degenerate triangles (rounding at exactly collinear vertices, eg T-junctions)
        /// </summary>
        public int UnrotatedCharts;
        /// <summary>
        /// vertices moved by a few ulps after Compute(), so that each keeps its side of the nearby triangle edges it
        /// has in the input UVs (scaling and translating rounds; nearly collinear vertices can change sides)
        /// </summary>
        public int RepairedVertices;
        /// <summary>
        /// UV triangles (and overlapping pairs) that the packing broke and that could not be repaired, after Compute()
        /// (0 if the repair succeeded, and 0 if ExactCheck is off: then nothing is checked)
        /// </summary>
        public int RemainingDefects;

        public UVPacker(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        class Chart
        {
            public List<int> Verts = new List<int>();
            public List<int> Tris = new List<int>();
            public Vector2d[] P;       // local coordinates, bounding box [0,W] x [0,H]
            public double W, H;
            public bool Rotated;       // placed rotated by 90 degrees
            public double X, Y;        // placement of the padded rectangle
            public bool Angled;        // rotated to its minimum-area rectangle (by an angle that is rounded)
            public bool KeepAxes;      // not rotated to the minimum-area rectangle
        }

        public ParameterizationResult Compute()
        {
            ChartCount = 0; TriangleChart = null; Coverage = 0; ChartBounds = null; AchievedPadding = 0; inputOrientation = null; UnrotatedCharts = 0; RepairedVertices = 0; RemainingDefects = 0;
            IList<Vector2d> uv = UV;
            if (uv == null) {
                if (!Mesh.HasVertexUVs)
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidMesh, "mesh has no vertex UVs and UV is null");
                Vector2d[] a = new Vector2d[Mesh.MaxVertexID];
                foreach (int vid in Mesh.VertexIndices())
                    a[vid] = (Vector2d)Mesh.GetVertexUV(vid);
                uv = a;
            } else if (uv.Count < Mesh.MaxVertexID)
                return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, "UV has fewer entries than MaxVertexID");
            if (Mesh.TriangleCount == 0)
                return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidMesh, "mesh has no triangles");
            if (!(Padding >= 0 && Padding < 0.25))
                return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, "Padding must be in [0, 0.25)");
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                for (int j = 0; j < 3; ++j)
                    if (!MathUtil.IsFinite(uv[t[j]].x) || !MathUtil.IsFinite(uv[t[j]].y))
                        return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, "UV of vertex " + t[j] + " is not finite");
            }

            List<Chart> charts = findCharts(uv);
            double pad, side, scale;
            bool padded;
            int iterations = 0;
            Vector2d[] outUV;
            Defects defects;
            while (true) {
                foreach (Chart c in charts)
                    prepare(c, uv);
                int packIterations;
                if (!pack(charts, out pad, out side, out padded, out packIterations))
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidMesh, "all charts have zero size");
                iterations += packIterations;
                scale = 1.0 / side;
                outUV = place(charts, pad, scale);
                defects = ExactCheck ? roundingDefects(uv, outUV) : new Defects();
                // a rotation by any other angle than a multiple of 90 degrees is rounded: vertices exactly on the
                // line of another triangle's edge (T-junctions, collinear chains of a flat CAD face) can move to the
                // wrong side, and the chart overlaps itself. Scaling and translating keep each coordinate's order,
                // so such charts are packed again without the rotation.
                if (!AllowRotation || !unrotateDefectCharts(charts, defects))
                    break;
            }
            // scaling and translating still round: a vertex nearly on the line of another triangle's edge can end
            // on its other side. Such vertices are moved by a few ulps back to their side in the input UVs.
            RemainingDefects = repairRounding(uv, outUV, defects);
            for (int ci = 0; ci < charts.Count; ++ci) {
                AxisAlignedBox2d box = AxisAlignedBox2d.Empty;
                foreach (int v in charts[ci].Verts) box.Contain(outUV[v]);
                ChartBounds[ci] = box;
            }
            ChartCount = charts.Count;
            Coverage = 0;
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                Coverage += 0.5 * Math.Abs((outUV[t.b] - outUV[t.a]).Cross(outUV[t.c] - outUV[t.a]));
            }
            AchievedPadding = pad * scale;
            Result = new ParameterizationResult();
            Result.UV = outUV;
            Result.Iterations = iterations;
            Result.Converged = padded;
            Result.Distortion = ParameterizationDistortion.Compute(Mesh, outUV);
            return Result;
        }

        // pack, then grow the padding (in chart units) until it is at least Padding in the unit square
        bool pack(List<Chart> charts, out double pad, out double side, out bool padded, out int iterations)
        {
            double area = charts.Sum(c => c.W * c.H);
            pad = Padding * Math.Sqrt(Math.Max(area, 1e-300)) * 1.2;
            side = 0;
            padded = false;
            iterations = 0;
            while (true) {
                iterations++;
                side = packBest(charts, pad);
                padded = pad >= Padding * side * (1 - 1e-9) || Padding == 0;
                if (padded || iterations == 20)
                    break;
                pad = Padding * side * 1.0001;
            }
            return side > 0;
        }

        // UVs of the placed charts in the unit square, and their bounds
        Vector2d[] place(List<Chart> charts, double pad, double scale)
        {
            Vector2d[] outUV = new Vector2d[Mesh.MaxVertexID];
            ChartBounds = new List<AxisAlignedBox2d>();
            foreach (Chart c in charts) {
                double ox = c.X + pad, oy = c.Y + pad;
                AxisAlignedBox2d box = AxisAlignedBox2d.Empty;
                for (int k = 0; k < c.Verts.Count; ++k) {
                    Vector2d p = c.Rotated ? new Vector2d(c.H - c.P[k].y, c.P[k].x) : c.P[k];
                    Vector2d q = new Vector2d((ox + p.x) * scale, (oy + p.y) * scale);
                    outUV[c.Verts[k]] = q;
                    box.Contain(q);
                }
                ChartBounds.Add(box);
            }
            return outUV;
        }

        // UV triangles of the placed charts that overlap (within a chart), flip or collapse, but do not in the
        // input UVs: defects of the input itself (overlapping or mirrored input charts) are not the packer's
        class Defects
        {
            public List<Index2i> Pairs = new List<Index2i>();
            public List<int> Triangles = new List<int>();
            public int Total;   // also the pairs beyond MaxCheckedPairs
        }

        const int MaxCheckedPairs = 100000;
        // orientation of each triangle in the input UVs (during Compute)
        int[] inputOrientation;

        Defects roundingDefects(IList<Vector2d> uv, Vector2d[] outUV)
        {
            var d = new Defects();
            // single triangles: the orientation changed (mirrored, clockwise input charts included)
            if (inputOrientation == null) {
                inputOrientation = new int[Mesh.MaxTriangleID];
                foreach (int tid in Mesh.TriangleIndices()) {
                    Index3i t = Mesh.GetTriangle(tid);
                    inputOrientation[tid] = ExactPredicates.Orient2d(uv[t.a], uv[t.b], uv[t.c]);
                }
            }
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                int s = inputOrientation[tid];
                if (s != 0 && ExactPredicates.Orient2d(outUV[t.a], outUV[t.b], outUV[t.c]) != s)
                    d.Triangles.Add(tid);
            }
            // pairs: overlapping in a chart, but not in the input (the filter runs inside the test, so overlaps of
            // the input do not count against the stored pairs)
            Func<int, int, bool> fromRounding = (ta, tb) => {
                if (TriangleChart[ta] != TriangleChart[tb])
                    return false;
                Index3i a = Mesh.GetTriangle(ta), b = Mesh.GetTriangle(tb);
                int sa = inputOrientation[ta], sb = inputOrientation[tb];
                if (sa == 0 || sb == 0)
                    return false;
                Vector2d a1 = sa > 0 ? uv[a.b] : uv[a.c], a2 = sa > 0 ? uv[a.c] : uv[a.b];
                Vector2d b1 = sb > 0 ? uv[b.b] : uv[b.c], b2 = sb > 0 ? uv[b.c] : uv[b.b];
                return !UVOverlap.InteriorsOverlap(uv[a.a], a1, a2, uv[b.a], b1, b2);
            };
            UVOverlap ov = UVOverlap.Compute(Mesh, outUV, MaxCheckedPairs, fromRounding);
            d.Pairs.AddRange(ov.Pairs);
            d.Total = ov.OverlappingPairs + d.Triangles.Count;
            return d;
        }

        // charts rotated to their minimum-area rectangle with defects lose the rotation; true if there are such
        bool unrotateDefectCharts(List<Chart> charts, Defects defects)
        {
            var bad = new HashSet<int>();
            foreach (Index2i pr in defects.Pairs) bad.Add(TriangleChart[pr.a]);
            foreach (int t in defects.Triangles) bad.Add(TriangleChart[t]);
            bool any = false;
            foreach (int ci in bad)
                if (charts[ci].Angled) {
                    charts[ci].KeepAxes = true;
                    UnrotatedCharts++;
                    any = true;
                }
            return any;
        }

        // the vertices of each defect triangle (pair) that changed sides relative to an edge of the other (or the
        // opposite edge of its own) triangle are moved back to their side in uv, a few ulps at a time, checked with
        // exact predicates. A round that does not reduce the defects is undone.
        int repairRounding(IList<Vector2d> uv, Vector2d[] outUV, Defects defects)
        {
            for (int round = 0; round < 8 && defects.Total > 0; ++round) {
                Vector2d[] before = (Vector2d[])outUV.Clone();
                int moved = 0;
                foreach (Index2i pr in defects.Pairs) {
                    Index3i a = Mesh.GetTriangle(pr.a), b = Mesh.GetTriangle(pr.b);
                    moved += restoreSides(uv, outUV, a, b) + restoreSides(uv, outUV, b, a);
                }
                foreach (int tid in defects.Triangles) {
                    Index3i t = Mesh.GetTriangle(tid);
                    moved += restoreSides(uv, outUV, t, t);
                }
                if (moved == 0)
                    break;
                Defects after = roundingDefects(uv, outUV);
                if (after.Total >= defects.Total) {
                    Array.Copy(before, outUV, outUV.Length);
                    break;
                }
                RepairedVertices += moved;
                defects = after;
            }
            return defects.Total;
        }

        // the vertices of a that are not vertices of b, against the lines of the edges of b
        int restoreSides(IList<Vector2d> uv, Vector2d[] outUV, Index3i a, Index3i b)
        {
            int moved = 0;
            for (int i = 0; i < 3; ++i) {
                int x = a[i];
                if (x == b.a || x == b.b || x == b.c)
                    if (a != b) continue;
                for (int e = 0; e < 3; ++e) {
                    int p = b[e], q = b[(e + 1) % 3], r = b[(e + 2) % 3];
                    if (x == p || x == q)
                        continue;
                    // side of x in the input; on the line: the side away from the rest of b
                    int want = ExactPredicates.Orient2d(uv[p], uv[q], uv[x]);
                    if (want == 0) {
                        if (a == b) continue;
                        want = -ExactPredicates.Orient2d(uv[p], uv[q], uv[r]);
                    }
                    if (want == 0 || ExactPredicates.Orient2d(outUV[p], outUV[q], outUV[x]) == want)
                        continue;
                    // only a vertex beside the edge (a T-junction): far from the segment the side of a nearly
                    // collinear vertex depends on the rounded direction of a short edge, and moving it does not help
                    Vector2d d = outUV[q] - outUV[p];
                    double tpar = (outUV[x] - outUV[p]).Dot(d) / d.LengthSquared;
                    if (!(tpar > 0 && tpar < 1))
                        continue;
                    Vector2d n = want * new Vector2d(-d.y, d.x);
                    if (!(n.Length > 0))
                        continue;
                    n = n.Normalized;
                    Vector2d x0 = outUV[x];
                    double step = 4 * MathUtil.Epsilon * Math.Max(1, Math.Max(Math.Abs(x0.x), Math.Abs(x0.y)));
                    // at most 2^13 machine epsilons (relative to max(1, |x|)), inside the unit square; else the vertex stays
                    bool done = false;
                    for (int k = 0; k < 12 && !done; ++k, step *= 2) {
                        Vector2d y = x0 + step * n;
                        if (!(y.x >= 0 && y.x <= 1 && y.y >= 0 && y.y <= 1))
                            break;
                        outUV[x] = y;
                        done = ExactPredicates.Orient2d(outUV[p], outUV[q], outUV[x]) == want;
                    }
                    if (done)
                        moved++;
                    else
                        outUV[x] = x0;
                }
            }
            return moved;
        }

        List<Chart> findCharts(IList<Vector2d> uv)
        {
            int[] parent = new int[Mesh.MaxVertexID];
            for (int i = 0; i < parent.Length; ++i) parent[i] = i;
            Func<int, int> find = (x) => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                int ra = find(t.a);
                parent[find(t.b)] = ra;
                parent[find(t.c)] = ra;
            }
            var rootChart = new Dictionary<int, int>();
            var charts = new List<Chart>();
            TriangleChart = new int[Mesh.MaxTriangleID];
            for (int i = 0; i < TriangleChart.Length; ++i) TriangleChart[i] = -1;
            bool[] seen = new bool[Mesh.MaxVertexID];
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                int r = find(t.a), ci;
                if (!rootChart.TryGetValue(r, out ci)) {
                    ci = charts.Count;
                    rootChart[r] = ci;
                    charts.Add(new Chart());
                }
                TriangleChart[tid] = ci;
                charts[ci].Tris.Add(tid);
                for (int j = 0; j < 3; ++j)
                    if (!seen[t[j]]) { seen[t[j]] = true; charts[ci].Verts.Add(t[j]); }
            }
            return charts;
        }

        // scale, rotate and move the chart to its bounding box at the origin
        void prepare(Chart c, IList<Vector2d> uv)
        {
            int n = c.Verts.Count;
            Vector2d[] p = new Vector2d[n];
            for (int k = 0; k < n; ++k) p[k] = uv[c.Verts[k]];
            if (ScaleChartsToSurfaceArea) {
                double a3 = 0, auv = 0;
                foreach (int tid in c.Tris) {
                    Index3i t = Mesh.GetTriangle(tid);
                    Vector3d p0 = Mesh.GetVertex(t.a);
                    a3 += 0.5 * (Mesh.GetVertex(t.b) - p0).Cross(Mesh.GetVertex(t.c) - p0).Length;
                    auv += 0.5 * Math.Abs((uv[t.b] - uv[t.a]).Cross(uv[t.c] - uv[t.a]));
                }
                if (auv > 0 && a3 > 0) {
                    double s = Math.Sqrt(a3 / auv);
                    for (int k = 0; k < n; ++k) p[k] *= s;
                }
            }
            c.Angled = false;
            if (AllowRotation && !c.KeepAxes && n >= 3) {
                Box2d box = new ContMinBox2(p, 1e-10, QueryNumberType.QT_DOUBLE, false).MinBox;
                Vector2d ax = box.AxisX;
                if (box.Extent.y > box.Extent.x)
                    ax = box.AxisY;
                if (ax.Length > 0 && MathUtil.IsFinite(ax.x)) {
                    ax = ax.Normalized;
                    c.Angled = !(ax.y == 0 && ax.x > 0);
                    for (int k = 0; k < n; ++k)
                        p[k] = new Vector2d(p[k].x * ax.x + p[k].y * ax.y, -p[k].x * ax.y + p[k].y * ax.x);
                }
            }
            AxisAlignedBox2d b = AxisAlignedBox2d.Empty;
            for (int k = 0; k < n; ++k) b.Contain(p[k]);
            for (int k = 0; k < n; ++k) p[k] -= b.Min;
            c.P = p;
            c.W = b.Width; c.H = b.Height;
        }

        // pack with several bin widths, keep the smallest square; returns its side
        double packBest(List<Chart> charts, double pad)
        {
            List<Chart> order = charts.OrderByDescending(c => Math.Max(c.W, c.H) + pad).ThenByDescending(c => Math.Min(c.W, c.H)).ToList();
            double area = 0, minW = 0;
            foreach (Chart c in charts) {
                area += (c.W + pad) * (c.H + pad);
                minW = Math.Max(minW, (AllowRotation ? Math.Min(c.W, c.H) : c.W) + pad);
            }
            double s0 = Math.Max(Math.Sqrt(area), minW);
            double bestSide = double.PositiveInfinity;
            double bestW = s0;
            const int steps = 24;
            for (int k = 0; k <= steps; ++k) {
                double W = s0 * (1 + 1.0 * k / steps);
                double H = skyline(order, W, pad, false);
                double side = Math.Max(W, H) + pad;
                if (side < bestSide) { bestSide = side; bestW = W; }
            }
            skyline(order, bestW, pad, true);
            return bestSide;
        }

        struct Segment { public double X, Y, W; }

        // skyline bottom-left: each rectangle goes where its top is lowest (then leftmost); returns the height
        double skyline(List<Chart> order, double W, double pad, bool apply)
        {
            var sky = new List<Segment>() { new Segment() { X = 0, Y = 0, W = W } };
            double height = 0;
            foreach (Chart c in order) {
                double bestTop = double.PositiveInfinity, bestX = 0, bestY = 0;
                int bestI = -1; bool bestRot = false;
                for (int rot = 0; rot < (AllowRotation ? 2 : 1); ++rot) {
                    double rw = (rot == 0 ? c.W : c.H) + pad, rh = (rot == 0 ? c.H : c.W) + pad;
                    for (int i = 0; i < sky.Count; ++i) {
                        double x = sky[i].X;
                        if (x + rw > W * (1 + 1e-12)) break;
                        double y = 0, end = x + rw;
                        for (int j = i; j < sky.Count && sky[j].X < end - 1e-12 * W; ++j)
                            y = Math.Max(y, sky[j].Y);
                        double top = y + rh;
                        if (top < bestTop - 1e-12 * W || (Math.Abs(top - bestTop) <= 1e-12 * W && x < bestX)) {
                            bestTop = top; bestX = x; bestY = y; bestI = i; bestRot = rot == 1;
                        }
                    }
                }
                if (bestI < 0)
                    return double.PositiveInfinity;
                double w = (bestRot ? c.H : c.W) + pad;
                if (apply) { c.X = bestX; c.Y = bestY; c.Rotated = bestRot; }
                height = Math.Max(height, bestTop);
                // update the skyline over [bestX, bestX + w]
                double x0 = bestX, x1 = bestX + w;
                var next = new List<Segment>(sky.Count + 2);
                bool inserted = false;
                foreach (Segment s in sky) {
                    double s0 = s.X, s1 = s.X + s.W;
                    if (s1 <= x0 || s0 >= x1) {
                        if (s0 >= x1 && !inserted) { next.Add(new Segment() { X = x0, Y = bestTop, W = w }); inserted = true; }
                        next.Add(s);
                        continue;
                    }
                    if (s0 < x0) next.Add(new Segment() { X = s0, Y = s.Y, W = x0 - s0 });
                    if (!inserted) { next.Add(new Segment() { X = x0, Y = bestTop, W = w }); inserted = true; }
                    if (s1 > x1) next.Add(new Segment() { X = x1, Y = s.Y, W = s1 - x1 });
                }
                if (!inserted) next.Add(new Segment() { X = x0, Y = bestTop, W = w });
                // merge neighbours of equal height
                sky.Clear();
                foreach (Segment s in next) {
                    if (s.W <= 0) continue;
                    if (sky.Count > 0 && sky[sky.Count - 1].Y == s.Y) {
                        Segment last = sky[sky.Count - 1];
                        last.W = s.X + s.W - last.X;
                        sky[sky.Count - 1] = last;
                    } else
                        sky.Add(s);
                }
            }
            return height;
        }
    }
}
