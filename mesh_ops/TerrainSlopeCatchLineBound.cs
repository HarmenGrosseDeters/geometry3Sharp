// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// The proven horizontal bound of the catch line (docs/design/TERRAIN.md 5.5, "The proven bound"). With the true line
    /// {S - T = 0} in the band {lo &lt;= D̃ &lt;= hi} of the realized difference D̃ = dir (S̃ - T̃) (lo = -Tolerance - ε_T,
    /// hi = ε_T), every overlay face's part of the band is a convex polygon P_f; its vertices are enclosed in boxes by
    /// interval arithmetic, and the distance to a computed catch-line segment, convex, is bounded at the boxes' corners,
    /// rounded up. Each P_f is assigned to the segment with the smallest bound among those of its own face and its band
    /// neighbours, or else of the nearest band faces with a segment within MaxSteps steps; a segment's bound is the largest
    /// of its pieces'. Pieces without a segment are
    /// returned as boxes (a possible contact away from the computed line).
    /// </summary>
    internal static class SlopeCatchLineBound
    {
        /// <summary> band faces searched from a piece for a segment (breadth first) </summary>
        internal const int MaxSteps = 8;

        /// <summary>
        /// per catch line of the overlay, per segment, the bound (without the snap's move); 'without' gets the boxes of
        /// the band pieces with no segment within MaxSteps
        /// </summary>
        internal static List<List<double>> Compute(SlopeTerrainOverlay o, double tolerance, double terrainUncertainty, double ez,
                                                   List<AxisAlignedBox2d> without, out double slack)
        {
            DMesh3 A = o.Slope, B = o.Terrain;
            int dir = o.Dir;
            // the band, rounded outwards, with a slack on both sides for the realization error of the stored surface: a stored
            // height is within e_z = 2 sMax e_xy + e_eval of its plane's exact value at the rounded XY (TERRAIN.md 5.4; the
            // budget certifies S̃ against S with exact planes, and at creases near the toe the stored mesh can be off by it),
            // plus 8 ulps of the heights. Powers of two and ulps only: 2^k scales it bit for bit
            double zmax = 0;
            foreach (Vector3d v in A.Vertices()) zmax = Math.Max(zmax, Math.Abs(v.z));
            slack = ExactInterval.Up(ez + 8 * (ExactInterval.Up(zmax) - zmax));
            double lo, hi;
            Band(tolerance, terrainUncertainty, slack, out lo, out hi);
            var result = new List<List<double>>();
            foreach (CatchLine l in o.CatchLines) {
                var u = new List<double>();
                int n = l.Closed ? l.Points.Count : l.Points.Count - 1;
                for (int i = 0; i < n; ++i) u.Add(0);
                result.Add(u);
            }
            // the segments of each face: (line, segment)
            var segOf = new Dictionary<int, List<Index2i>>();
            for (int li = 0; li < o.CatchFaces.Count; ++li)
                for (int i = 0; i < o.CatchFaces[li].Count && i < result[li].Count; ++i) {
                    List<Index2i> s;
                    if (segOf.TryGetValue(o.CatchFaces[li][i], out s) == false) segOf[o.CatchFaces[li][i]] = s = new List<Index2i>();
                    s.Add(new Index2i(li, i));
                }
            // the faces the true catch line can be in: it bounds the true region, connected to the toe through {D <= 0}, and
            // D <= 0 implies D̃ <= hi; so the faces reached from the computed region, or from a low face on the toe, through
            // faces where D̃ <= hi somewhere
            // (sharing an overlay vertex: a closed set can connect through a point). A band ring elsewhere, eg around a hill
            // the cone passes under (TERRAIN.md 5.5, "Region"), is cut off by faces with D̃ > hi, where D > 0: not the line
            var reach = reachable(o, dir, hi);
            // the band piece of each face, as corner points of boxes (null: no band there, or not reachable)
            var corners = new List<Vector2d>[o.Faces.Count];
            for (int fi = 0; fi < o.Faces.Count; ++fi) corners[fi] = reach[fi] ? piece(o, fi, dir, lo, hi) : null;
            // each piece to its best segment, breadth first over band faces through shared overlay edges
            for (int fi = 0; fi < o.Faces.Count; ++fi) {
                if (corners[fi] == null) continue;
                double best = double.PositiveInfinity;
                Index2i bestSeg = new Index2i(-1, -1);
                var seen = new HashSet<int> { fi };
                var level = new List<int> { fi };
                for (int step = 0; step <= MaxSteps && level.Count > 0 && (step <= 1 || double.IsPositiveInfinity(best)); ++step) {
                    foreach (int g in level) {
                        List<Index2i> segs;
                        if (segOf.TryGetValue(g, out segs) == false) continue;
                        foreach (Index2i sg in segs) {
                            CatchLine l = o.CatchLines[sg.a];
                            Vector3d p = l.Points[sg.b], q = l.Points[(sg.b + 1) % l.Points.Count];
                            double d = 0;
                            foreach (Vector2d c in corners[fi]) d = Math.Max(d, DistanceUp(c, p.xy, q.xy));
                            if (d < best || (d == best && (sg.a < bestSeg.a || (sg.a == bestSeg.a && sg.b < bestSeg.b)))) { best = d; bestSeg = sg; }
                        }
                    }
                    var next = new List<int>();
                    foreach (int g in level)
                        foreach (var oe in o.Faces[g].Edges) {
                            Index2i ef;
                            if (o.EdgeFaces.TryGetValue(oe.Key, out ef) == false) continue;
                            foreach (int h in new[] { ef.a, ef.b })
                                if (h >= 0 && corners[h] != null && seen.Add(h)) next.Add(h);
                        }
                    next.Sort();
                    level = next;
                }
                if (bestSeg.a < 0) {
                    var box = AxisAlignedBox2d.Empty;
                    foreach (Vector2d c in corners[fi]) box.Contain(c);
                    without.Add(box);
                    continue;
                }
                result[bestSeg.a][bestSeg.b] = Math.Max(result[bestSeg.a][bestSeg.b], best);
            }
            return result;
        }

        /// <summary>
        /// the band [lo, hi] of D̃ for the true line (TERRAIN.md 5.5): lo = -(Tolerance + εT + slack), hi = εT + slack, rounded
        /// outwards (D = D̃ + (S - S̃) + (T̃ - T) with 0 &lt;= S - S̃ &lt;= Tolerance up to the slack, |T̃ - T| &lt;= εT)
        /// </summary>
        internal static void Band(double tolerance, double terrainUncertainty, double slack, out double lo, out double hi)
        {
            lo = -ExactInterval.Up(ExactInterval.Up(tolerance + terrainUncertainty) + slack);
            hi = ExactInterval.Up(terrainUncertainty + slack);
        }

        // the faces reached from the region's faces and the low faces on the toe through faces where D̃ <= hi somewhere (interval lower bounds at their
        // vertices; a face whose planes are not decided counts as such), over shared overlay vertices
        static bool[] reachable(SlopeTerrainOverlay o, int dir, double hi)
        {
            int n = o.Faces.Count;
            var low = new bool[n];
            var facesAt = new Dictionary<long, List<int>>(EdgeKeyComparer.Instance);
            for (int fi = 0; fi < n; ++fi) {
                var f = o.Faces[fi];
                Index3i ta = o.Slope.GetTriangle(f.TA), tb = o.Terrain.GetTriangle(f.TB);
                Plane pa = Plane.Of(o.Slope.GetVertex(ta.a), o.Slope.GetVertex(ta.b), o.Slope.GetVertex(ta.c));
                Plane pb = Plane.Of(o.Terrain.GetVertex(tb.a), o.Terrain.GetVertex(tb.b), o.Terrain.GetVertex(tb.c));
                bool any = pa.Ok == false || pb.Ok == false;
                foreach (var oe in f.Edges)
                    foreach (long k in new[] { oe.P, oe.Q }) {
                        List<int> l;
                        if (facesAt.TryGetValue(k, out l) == false) facesAt[k] = l = new List<int>();
                        if (l.Count == 0 || l[l.Count - 1] != fi) l.Add(fi);
                        if (any == false) {
                            IBox b = vertex_box(o, k);
                            ExactInterval d = pa.At(b) - pb.At(b);
                            if ((dir > 0 ? d.Lo : -d.Hi) <= hi) any = true;
                        }
                    }
                low[fi] = any;
            }
            var reach = new bool[n];
            var queue = new Queue<int>();
            // (seeds: the region's faces, and the low faces on the toe: a true cut can start at a toe stretch within εT above
            // the given terrain where the computed one has none, cut off from the computed region by toe clearly above it)
            for (int fi = 0; fi < n; ++fi) {
                var f = o.Faces[fi];
                bool seed = f.Region;
                if (seed == false && low[fi])
                    foreach (var oe in f.Edges)
                        if (SlopeTerrainOverlay.ekind(oe.Key) == 0 && o.BoundaryKind(SlopeTerrainOverlay.eedge(oe.Key)) == SlopeTerrainOverlay.KindToe) { seed = true; break; }
                if (seed) { reach[fi] = true; queue.Enqueue(fi); }
            }
            while (queue.Count > 0) {
                int fi = queue.Dequeue();
                foreach (var oe in o.Faces[fi].Edges)
                    foreach (long k in new[] { oe.P, oe.Q })
                        foreach (int g in facesAt[k])
                            if (reach[g] == false && low[g]) { reach[g] = true; queue.Enqueue(g); }
            }
            return reach;
        }

        /// <summary> tests: the corners of boxes that hold face fi's part of the band [lo, hi] (null if it is empty for sure) </summary>
        internal static List<Vector2d> Piece(SlopeTerrainOverlay o, int fi, double lo, double hi) { return piece(o, fi, o.Dir, lo, hi); }

        /// <summary> tests: whether the intervals decide the plane through a, b, c (its nz away from 0) </summary>
        internal static bool PlaneDecided(Vector3d a, Vector3d b, Vector3d c) { return Plane.Of(a, b, c).Ok; }

        // the corners of boxes that hold the face's part of the band (null if the part is empty for sure)
        static List<Vector2d> piece(SlopeTerrainOverlay o, int fi, int dir, double lo, double hi)
        {
            var f = o.Faces[fi];
            DMesh3 A = o.Slope, B = o.Terrain;
            Index3i ta = A.GetTriangle(f.TA), tb = B.GetTriangle(f.TB);
            Plane pa = Plane.Of(A.GetVertex(ta.a), A.GetVertex(ta.b), A.GetVertex(ta.c));
            Plane pb = Plane.Of(B.GetVertex(tb.a), B.GetVertex(tb.b), B.GetVertex(tb.c));
            // (a plane not decided by the intervals, a sliver with nz near 0: the whole face, all its vertices' boxes)
            if (pa.Ok == false || pb.Ok == false) {
                var all = new List<Vector2d>();
                foreach (var oe in f.Edges) { add_corners(all, vertex_box(o, oe.P)); add_corners(all, vertex_box(o, oe.Q)); }
                return all.Count > 0 ? all : null;
            }
            // D̃ on a box: dir (S̃ - T̃), each an interval
            Func<IBox, ExactInterval> dOf = (bx) => {
                ExactInterval d = pa.At(bx) - pb.At(bx);
                return dir > 0 ? d : -d;
            };
            var cs = new List<Vector2d>();
            bool any = false;
            foreach (var oe in f.Edges) {
                IBox bp = vertex_box(o, oe.P), bq = vertex_box(o, oe.Q);
                ExactInterval dp = dOf(bp), dq = dOf(bq);
                // the ends in the band
                if (dp.Hi >= lo && dp.Lo <= hi) { add_corners(cs, bp); any = true; }
                if (dq.Hi >= lo && dq.Lo <= hi) { add_corners(cs, bq); any = true; }
                // where D̃ = lo or hi on the edge: t = (c - dp) / (dq - dp), or the whole edge where that is not decided
                foreach (double c in new[] { lo, hi }) {
                    bool may = (Math.Min(dp.Lo, dq.Lo) <= c && Math.Max(dp.Hi, dq.Hi) >= c);
                    if (may == false) continue;
                    any = true;
                    ExactInterval den = dq - dp;
                    if (den.Lo > 0 || den.Hi < 0) {
                        ExactInterval t = Div(ExactInterval.Of(c) - dp, den);
                        double t0 = Math.Max(0, t.Lo), t1 = Math.Min(1, t.Hi);
                        if (t0 > t1) { t0 = 0; t1 = 1; }
                        add_corners(cs, Lerp(bp, bq, new ExactInterval(t0, t1)));
                    } else {
                        add_corners(cs, bp); add_corners(cs, bq);
                    }
                }
            }
            return any ? cs : null;
        }

        /// <summary> an axis-aligned box of points, its bounds as intervals </summary>
        internal struct IBox
        {
            public ExactInterval X, Y;
        }

        static void add_corners(List<Vector2d> cs, IBox b)
        {
            cs.Add(new Vector2d(b.X.Lo, b.Y.Lo)); cs.Add(new Vector2d(b.X.Hi, b.Y.Lo));
            cs.Add(new Vector2d(b.X.Lo, b.Y.Hi)); cs.Add(new Vector2d(b.X.Hi, b.Y.Hi));
        }

        // the points p + t (q - p) for p, q in their boxes and t in [t.Lo, t.Hi] within [0, 1] (a box that holds them)
        static IBox Lerp(IBox p, IBox q, ExactInterval t)
        {
            ExactInterval x = p.X + t * (q.X - p.X), y = p.Y + t * (q.Y - p.Y);
            // (on the segment between the boxes: also within their hull)
            return new IBox() {
                X = new ExactInterval(Math.Max(x.Lo, Math.Min(p.X.Lo, q.X.Lo)), Math.Min(x.Hi, Math.Max(p.X.Hi, q.X.Hi))),
                Y = new ExactInterval(Math.Max(y.Lo, Math.Min(p.Y.Lo, q.Y.Lo)), Math.Min(y.Hi, Math.Max(p.Y.Hi, q.Y.Hi)))
            };
        }

        // the box of an overlay vertex: a vertex of A or B exactly; a crossing of an edge of A with an edge of B within
        // both edges' boxes and the interval of its parameter on the A edge
        static IBox vertex_box(SlopeTerrainOverlay o, long k)
        {
            int kind = SlopeTerrainOverlay.vkind(k);
            if (kind == 0 || kind == 1) {
                Vector2d p = (kind == 0 ? o.Slope : o.Terrain).GetVertex(SlopeTerrainOverlay.vfirst(k)).xy;
                return new IBox() { X = ExactInterval.Of(p.x), Y = ExactInterval.Of(p.y) };
            }
            Index2i va = o.Slope.GetEdgeV(SlopeTerrainOverlay.vfirst(k)), vb = o.Terrain.GetEdgeV(SlopeTerrainOverlay.vsecond(k));
            Vector2d a0 = o.Slope.GetVertex(va.a).xy, a1 = o.Slope.GetVertex(va.b).xy, b0 = o.Terrain.GetVertex(vb.a).xy, b1 = o.Terrain.GetVertex(vb.b).xy;
            // (the crossing lies on both closed segments)
            double x0 = Math.Max(Math.Min(a0.x, a1.x), Math.Min(b0.x, b1.x)), x1 = Math.Min(Math.Max(a0.x, a1.x), Math.Max(b0.x, b1.x));
            double y0 = Math.Max(Math.Min(a0.y, a1.y), Math.Min(b0.y, b1.y)), y1 = Math.Min(Math.Max(a0.y, a1.y), Math.Max(b0.y, b1.y));
            Func<double, ExactInterval> I = ExactInterval.Of;
            ExactInterval dax = I(a1.x) - I(a0.x), day = I(a1.y) - I(a0.y), dbx = I(b1.x) - I(b0.x), dby = I(b1.y) - I(b0.y);
            ExactInterval d0x = I(b0.x) - I(a0.x), d0y = I(b0.y) - I(a0.y);
            ExactInterval den = dax * dby - day * dbx;
            if (den.Lo > 0 || den.Hi < 0) {
                ExactInterval t = Div(d0x * dby - d0y * dbx, den);
                t = new ExactInterval(Math.Max(0, t.Lo), Math.Min(1, t.Hi));
                if (t.Lo <= t.Hi) {
                    ExactInterval x = I(a0.x) + t * dax, y = I(a0.y) + t * day;
                    x0 = Math.Max(x0, x.Lo); x1 = Math.Min(x1, x.Hi); y0 = Math.Max(y0, y.Lo); y1 = Math.Min(y1, y.Hi);
                }
            }
            return new IBox() { X = new ExactInterval(x0, x1), Y = new ExactInterval(y0, y1) };
        }

        /// <summary> a / b for an interval b not containing 0, rounded outwards </summary>
        internal static ExactInterval Div(ExactInterval a, ExactInterval b)
        {
            double q0 = a.Lo / b.Lo, q1 = a.Lo / b.Hi, q2 = a.Hi / b.Lo, q3 = a.Hi / b.Hi;
            double mn = Math.Min(Math.Min(q0, q1), Math.Min(q2, q3)), mx = Math.Max(Math.Max(q0, q1), Math.Max(q2, q3));
            return new ExactInterval(ExactInterval.Down(mn), ExactInterval.Up(mx));
        }

        // z = c0 + cx (x - ax) + cy (y - ay) of the plane through three points, the slopes as intervals
        struct Plane
        {
            public bool Ok;
            double ax, ay, az;
            ExactInterval cx, cy;
            public static Plane Of(Vector3d a, Vector3d b, Vector3d c)
            {
                Func<double, ExactInterval> I = ExactInterval.Of;
                ExactInterval ux = I(b.x) - I(a.x), uy = I(b.y) - I(a.y), uz = I(b.z) - I(a.z);
                ExactInterval vx = I(c.x) - I(a.x), vy = I(c.y) - I(a.y), vz = I(c.z) - I(a.z);
                ExactInterval nz = ux * vy - uy * vx;
                var p = new Plane() { ax = a.x, ay = a.y, az = a.z };
                if (!(nz.Lo > 0 || nz.Hi < 0)) return p;
                ExactInterval nx = uy * vz - uz * vy, ny = uz * vx - ux * vz;
                p.cx = -Div(nx, nz); p.cy = -Div(ny, nz);
                p.Ok = true;
                return p;
            }
            public ExactInterval At(IBox b)
            {
                return ExactInterval.Of(az) + cx * (b.X - ExactInterval.Of(ax)) + cy * (b.Y - ExactInterval.Of(ay));
            }
        }

        /// <summary> an upper bound of the distance from p to the segment ab (rounded up) </summary>
        internal static double DistanceUp(Vector2d p, Vector2d a, Vector2d b)
        {
            Func<double, ExactInterval> I = ExactInterval.Of;
            Func<ExactInterval, double> rootUp = (sq) => ExactInterval.Up(Math.Sqrt(Math.Max(0, sq.Hi)));
            ExactInterval pax = I(p.x) - I(a.x), pay = I(p.y) - I(a.y), pbx = I(p.x) - I(b.x), pby = I(p.y) - I(b.y);
            double d = Math.Min(rootUp(ExactInterval.Square(pax) + ExactInterval.Square(pay)), rootUp(ExactInterval.Square(pbx) + ExactInterval.Square(pby)));
            ExactInterval ux = I(b.x) - I(a.x), uy = I(b.y) - I(a.y);
            ExactInterval uu = ExactInterval.Square(ux) + ExactInterval.Square(uy);
            if (uu.Lo > 0) {
                ExactInterval t = Div(pax * ux + pay * uy, uu);
                // (the foot of the perpendicular on the segment: the distance to the line is the distance to the segment)
                if (t.Lo >= 0 && t.Hi <= 1) {
                    ExactInterval cr = pax * uy - pay * ux;
                    ExactInterval q = Div(ExactInterval.Square(cr), uu);
                    d = Math.Min(d, rootUp(q));
                }
            }
            return d;
        }
    }
}
