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
    /// Charts are at least Padding apart and from the border of the square.
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
        }

        public ParameterizationResult Compute()
        {
            ChartCount = 0; TriangleChart = null; Coverage = 0; ChartBounds = null;
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
            foreach (Chart c in charts)
                prepare(c, uv);

            // pack, then grow the padding (in chart units) until it is at least Padding in the unit square
            double area = charts.Sum(c => c.W * c.H);
            double pad = Padding * Math.Sqrt(Math.Max(area, 1e-300)) * 1.2;
            double side = 0;
            for (int iter = 0; iter < 20; ++iter) {
                side = packBest(charts, pad);
                if (pad >= Padding * side * (1 - 1e-9) || Padding == 0)
                    break;
                pad = Padding * side * 1.0001;
            }
            if (!(side > 0))
                return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidMesh, "all charts have zero size");

            double scale = 1.0 / side;
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
            ChartCount = charts.Count;
            Coverage = 0;
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                Coverage += 0.5 * Math.Abs((outUV[t.b] - outUV[t.a]).Cross(outUV[t.c] - outUV[t.a]));
            }
            Result = new ParameterizationResult();
            Result.UV = outUV;
            Result.Distortion = ParameterizationDistortion.Compute(Mesh, outUV);
            return Result;
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
            if (AllowRotation && n >= 3) {
                Box2d box = new ContMinBox2(p, 1e-10, QueryNumberType.QT_DOUBLE, false).MinBox;
                Vector2d ax = box.AxisX;
                if (box.Extent.y > box.Extent.x)
                    ax = box.AxisY;
                if (ax.Length > 0 && MathUtil.IsFinite(ax.x)) {
                    ax = ax.Normalized;
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
