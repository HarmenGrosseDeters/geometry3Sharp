// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// A ramp (haul road, access ramp) cut into the slope of a TerrainSlopeDesign (docs/design/TERRAIN_RAMPS.md, R1): a
    /// road of constant grade along a path, Width wide on the Side of the path, rising from its start on a toe.
    /// </summary>
    public class SlopeRamp
    {
        /// <summary> the index of the toe in TerrainSlopeDesign.Toes the ramp leaves; Path[0] must lie on it (exactly) </summary>
        public int Toe;
        /// <summary> the road's inner edge in XY (the pit side): an open polyline of straight segments, starting on the toe </summary>
        public List<Vector2d> Path = new List<Vector2d>();
        /// <summary> the road's height at Path[0]; NaN (default): the toe's height there </summary>
        public double StartHeight = double.NaN;
        /// <summary> the road's grade along the path: Percent or Rise (exactly checkable), below the wall's slope </summary>
        public SlopeSpec Grade;
        /// <summary> the road's width, from the inner edge to the outer edge (positive) </summary>
        public double Width;
        /// <summary> the side of the path the road lies on, Left or Right of the direction of travel (given, never inferred) </summary>
        public SlopeToe.Sides Side = SlopeToe.Sides.Left;
        /// <summary> the slope of the wall above the road (and of the road's end faces); null (default): the cut slope of the toe segment the path starts on </summary>
        public SlopeSpec? WallSlope;
    }

    // Ramps (docs/design/TERRAIN_RAMPS.md, R1). Each straight path segment is a convex polygon piece of the cone envelope:
    // F = max(the road plane, the outward strip planes of its edges, the fans at its corners), which is the road plane on
    // the polygon and the slope cone of its boundary outside (the wall above the road, and the end faces). The polygon
    // runs from the path inwards (towards the toe, so that the crest {S_lo = Z_R} lies on it) to the outer edge at Width
    // (1 + 2 delta). A bend away from the road gets a flat kite at the vertex height. The envelope then gives
    // min(S_lo, the road with its walls) with the existing exact labels, realization and checks: road triangles are
    // checked against the Grade, wall triangles against the wall's slope, both supported by the polygon's corners.
    public partial class TerrainSlopeDesign
    {
        /// <summary> optional: ramps cut into the slope (Direction Cut only in R1; else NotImplemented) </summary>
        public List<SlopeRamp> Ramps = new List<SlopeRamp>();
        /// <summary> the road's grade margin used (TERRAIN_RAMPS.md, "Margins"): the roads are built that much flatter than their Grade; 0 without ramps </summary>
        public double UsedRoadMargin;
        /// <summary> (ramp, path segment) of the problems of a ramp (InvalidInput, NotImplemented, ToeSteeperThanSlope, RampNotOnWall) </summary>
        public List<Index2i> RampProblems = new List<Index2i>();

        internal sealed class RampUnit
        {
            public int Seg;
            public bool Bend;
            public Vector3d[] Corners;     // counter-clockwise, at the road plane's heights (rounded down), per build
            public int RoadPlane;
            public List<int> Walls = new List<int>();
            public List<Vector2d> Apexes = new List<Vector2d>();
            public List<List<int>> Fans = new List<List<int>>();
            public List<List<double>> Angles = new List<List<double>>();
            public AxisAlignedBox2d Box, Reach;
            public double ZMin, Slb;
        }

        /// <summary> tests: a nominal road polygon of the width oracle (its width at least the ramp's Width, exactly) </summary>
        internal sealed class RampNominal
        {
            public int Ramp, Seg;
            public bool Bend;
            public Vector2d[] Poly;
            // the inner side (a segment: the path from A to B) or the vertex A and the outer corners Q1, M, Q2 (a bend)
            public Vector2d A, B, Q1, M, Q2;
            public AxisAlignedBox2d Box;
        }
        internal List<RampNominal> rampNominal = new List<RampNominal>();

        sealed class RampIn
        {
            public int Index;
            public SlopeRamp R;
            public List<Vector2d> P;
            public double Sigma, Z0, G, Length;
            public double[] L, Extend;
            public SlopeSpec Wall;
        }
        List<RampIn> rampsIn = new List<RampIn>();

        Statuses ramp_problem(Statuses st, int ramp, int seg, string message)
        {
            RampProblems.Add(new Index2i(ramp, seg));
            return fail(st, $"ramp {ramp}" + (seg >= 0 ? $" at {seg}" : "") + ": " + message);
        }

        // the left normal of u, on the road's side for sigma = +1
        static Vector2d side_normal(Vector2d u, double sigma) { return new Vector2d(-u.y * sigma, u.x * sigma); }

        // validates the ramps and appends their pieces (road, then wall, per path segment and per bend away from the road)
        Statuses prepare_ramps(List<InPiece> pieces, List<List<Vector3d>> toePts)
        {
            rampsIn = new List<RampIn>();
            for (int r = 0; r < Ramps.Count; ++r) {
                SlopeRamp ramp = Ramps[r];
                if (ramp == null || ramp.Path == null)
                    return ramp_problem(Statuses.InvalidInput, r, -1, "ramp without a path");
                if (ramp.Toe < 0 || ramp.Toe >= Toes.Count)
                    return ramp_problem(Statuses.InvalidInput, r, -1, "Toe is not the index of a toe");
                List<Vector2d> P = ramp.Path;
                int n = P.Count;
                if (n < 2)
                    return ramp_problem(Statuses.InvalidInput, r, -1, "a path needs at least two points");
                for (int i = 0; i < n; ++i)
                    if (MathUtil.IsFinite(P[i].x) == false || MathUtil.IsFinite(P[i].y) == false)
                        return ramp_problem(Statuses.InvalidInput, r, i, "path point not finite");
                for (int i = 0; i + 1 < n; ++i)
                    if (P[i] == P[i + 1])
                        return ramp_problem(Statuses.InvalidInput, r, i, "zero-length path segment");
                if (ramp.Side != SlopeToe.Sides.Left && ramp.Side != SlopeToe.Sides.Right)
                    return ramp_problem(Statuses.InvalidInput, r, -1, "a ramp needs Side Left or Right");
                if (!(ramp.Width > 0) || double.IsInfinity(ramp.Width))
                    return ramp_problem(Statuses.InvalidInput, r, -1, "Width must be positive and finite");
                if (ramp.Grade.IsValid == false || (ramp.Grade.Kind != SlopeSpec.Kinds.Percent && ramp.Grade.Kind != SlopeSpec.Kinds.Rise))
                    return ramp_problem(Statuses.InvalidInput, r, -1, "the grade must be a valid Percent or Rise (exactly checkable)");
                if (double.IsNaN(ramp.StartHeight) == false && MathUtil.IsFinite(ramp.StartHeight) == false)
                    return ramp_problem(Statuses.InvalidInput, r, -1, "StartHeight must be finite (or NaN: the toe's height)");
                // the start on the toe, exactly
                List<Vector3d> toe = toePts[ramp.Toe];
                int segs = Toes[ramp.Toe].SegmentCount, on = -1;
                for (int j = 0; j < segs && on < 0; ++j) {
                    Vector3d a = toe[j], b = toe[(j + 1) % toe.Count];
                    if (ExactPredicates.Orient2d(a.xy, b.xy, P[0]) == 0 && Math.Min(a.x, b.x) <= P[0].x && P[0].x <= Math.Max(a.x, b.x)
                        && Math.Min(a.y, b.y) <= P[0].y && P[0].y <= Math.Max(a.y, b.y)) on = j;
                }
                if (on < 0)
                    return ramp_problem(Statuses.InvalidInput, r, 0, "the path does not start on its toe");
                SlopeSpec wall = ramp.WallSlope ?? Toes[ramp.Toe].CutStart[on];
                if (wall.IsValid == false)
                    return ramp_problem(Statuses.InvalidInput, r, -1, "WallSlope must be a valid slope");
                double z0 = ramp.StartHeight;
                if (double.IsNaN(z0)) {
                    Vector3d a = toe[on], b = toe[(on + 1) % toe.Count];
                    bool xs = Math.Abs(b.x - a.x) >= Math.Abs(b.y - a.y);
                    double t = xs ? (P[0].x - a.x) / (b.x - a.x) : (P[0].y - a.y) / (b.y - a.y);
                    z0 = a.z + t * (b.z - a.z);
                }
                double sigma = ramp.Side == SlopeToe.Sides.Left ? 1 : -1;
                var u = new Vector2d[n - 1];
                var L = new double[n - 1];
                for (int i = 0; i + 1 < n; ++i) {
                    Vector2d d = P[i + 1] - P[i];
                    L[i] = d.Length;
                    u[i] = d / L[i];
                }
                // R1 turns (TERRAIN_RAMPS.md, "Bends"): only away from the road (towards the pit: a convex wall), and with
                // s cos(turn) >= g / 0.9 (else the end face of a segment, rising at s, would cut into the next segment's
                // road, rising at g; 0.9 covers the facets of the cone); a bend towards the road (a concave wall) needs a road
                // that is not constant across, R2
                for (int k = 1; k + 1 < n; ++k) {
                    double cross = u[k - 1].x * u[k].y - u[k - 1].y * u[k].x;
                    if (cross * sigma > 0)
                        return ramp_problem(Statuses.NotImplemented, r, k, "a bend towards the road's side (a concave wall) is not in R1");
                    if (!(0.9 * wall.S * u[k - 1].Dot(u[k]) >= ramp.Grade.S))
                        return ramp_problem(Statuses.NotImplemented, r, k, "the turn is too sharp for the grade (s cos(turn) < g / 0.9; a switchback is not in R1)");
                }
                // the path and its outer edge do not intersect themselves (exact, ToeGeometry)
                string why;
                Index2i where;
                var path3 = P.Select(p => new Vector3d(p.x, p.y, 0)).ToList();
                if (ToeGeometry.Validate(new List<List<Vector3d>>() { path3 }, new List<bool>() { false }, out why, out where) == false)
                    return ramp_problem(Statuses.InvalidInput, r, where.b, "the path self-intersects or touches itself");
                var outer = new List<Vector3d>();
                for (int k = 0; k < n; ++k) {
                    Vector2d q;
                    if (k == 0) q = P[0] + ramp.Width * side_normal(u[0], sigma);
                    else if (k == n - 1) q = P[k] + ramp.Width * side_normal(u[k - 1], sigma);
                    else q = P[k] + miter(side_normal(u[k - 1], sigma), side_normal(u[k], sigma), ramp.Width);
                    outer.Add(new Vector3d(q.x, q.y, 0));
                }
                if (ToeGeometry.Validate(new List<List<Vector3d>>() { outer }, new List<bool>() { false }, out why, out where) == false)
                    return ramp_problem(Statuses.InvalidInput, r, where.b, "the outer edge self-intersects or touches itself");
                // the inward extension of each segment's polygon: to the distance of its ends from the toes (the crest
                // lies between the path and the toe), in doubles (it only decides where the road plane ends inwards)
                var dist = new double[n];
                for (int k = 0; k < n; ++k) {
                    double best = double.MaxValue;
                    foreach (InPiece pc in pieces)
                        if (pc.Kind == 0) best = Math.Min(best, Math.Sqrt(new Segment2d(pc.A.xy, pc.B.xy).DistanceSquared(P[k])));
                    dist[k] = best;
                }
                var ri = new RampIn() { Index = r, R = ramp, P = P, Sigma = sigma, Z0 = z0, G = ramp.Grade.S, L = L, Wall = wall, Extend = new double[n - 1] };
                for (int i = 0; i + 1 < n; ++i) ri.Extend[i] = Math.Max(dist[i], dist[i + 1]);
                ri.Length = L.Sum();
                rampsIn.Add(ri);
                // the pieces, at the specified grade (each build lowers it by its margin)
                var z = new double[n];
                z[0] = z0;
                for (int i = 0; i + 1 < n; ++i) z[i + 1] = z[i] + ri.G * L[i];
                Action<int, bool, Vector3d, Vector3d> add = (seg, bend, a, b) => {
                    pieces.Add(new InPiece() { Toe = ramp.Toe, Segment = seg, Kind = 1, Ramp = r, Bend = bend, Spec = ramp.Grade, S = wall.S, A = a, B = b,
                                               Unit = new RampUnit() { Seg = seg, Bend = bend } });
                    pieces.Add(new InPiece() { Toe = ramp.Toe, Segment = seg, Kind = 2, Ramp = r, Bend = bend, Spec = wall, S = wall.S, A = a, B = b });
                };
                for (int i = 0; i + 1 < n; ++i) {
                    add(i, false, new Vector3d(P[i].x, P[i].y, z[i]), new Vector3d(P[i + 1].x, P[i + 1].y, z[i + 1]));
                    if (i + 2 < n && turns_away(u[i], u[i + 1], sigma))
                        add(i + 1, true, new Vector3d(P[i + 1].x, P[i + 1].y, z[i + 1]), new Vector3d(P[i + 1].x, P[i + 1].y, z[i + 1]));
                }
            }
            return Statuses.Success;
        }

        // the path turns away from the road's side (a gap between the segments' polygons on the road's side)
        static bool turns_away(Vector2d u0, Vector2d u1, double sigma)
        {
            double cross = u0.x * u1.y - u0.y * u1.x;
            return cross * sigma < 0;
        }

        // the miter offset at a vertex: the point at distance w from both offset lines (normals n0, n1, unit)
        static Vector2d miter(Vector2d n0, Vector2d n1, double w)
        {
            return (w / (1 + n0.Dot(n1))) * (n0 + n1);
        }

        // the road's grade margin: the road is built at g - delta sMin / 2. A road vertex's height is off its plane by at
        // most the rounding of its height and its XY times the road's grade (below sMin), so a triangle with altitudes of
        // at least MinFeatureSize = 2 (8 e_xy + 4 e_eval / sMin) / delta is steeper than its plane by at most about
        // delta sMin / 4; the exact check decides, and a failure raises delta
        internal static double RoadMargin(double delta, double sMin) { return 0.5 * delta * sMin; }

        // the ramps' share of the tolerance budget: the road at the grade g - RoadMargin lies below its specified heights
        // by at most RoadMargin times the path's length (rounded up)
        double ramp_share(double delta, double sMin)
        {
            double m = 0;
            if (rampsIn == null || Ramps == null || Ramps.Count == 0) return 0;
            foreach (RampIn ri in rampsIn) m = Math.Max(m, ri.Length);
            return ExactInterval.Up(ExactInterval.Up(ExactInterval.Up(RoadMargin(delta, sMin)) * ExactInterval.Up(m * (1 + 1e-12))) * 1.01);
        }

        // the box of the toes grown by the ramps' road polygons (with room for the margins): the domain and the
        // numerical resolution cover the walls above the roads
        void ramp_box(ref AxisAlignedBox2d box)
        {
            if (rampsIn == null || Ramps == null || Ramps.Count == 0) return;
            foreach (RampIn ri in rampsIn) {
                double grow = 1.1 * Math.Max(ri.R.Width, ri.Extend.Max());
                foreach (Vector2d p in ri.P) {
                    var b = new AxisAlignedBox2d(p, p);
                    b.Expand(grow);
                    box.Contain(b);
                }
            }
        }

        // the height of the plane at (x, y), rounded down (exact: the rounded height is lowered by an ulp if it is above)
        internal static double height_down(ExactSlopeKernel kernel, int plane, Vector3d p, Vector3d q, Vector3d r, double x, double y)
        {
            double h = kernel.Height(plane, x, y);
            int o2 = ExactPredicates.Orient2d(p.xy, q.xy, r.xy);
            if (ExactPredicates.Orient3d(p, q, r, new Vector3d(x, y, h)) * o2 > 0) h = ExactInterval.Down(h);
            return h;
        }

        // one build's road polygons and planes (the margin delta: the grade g - RoadMargin, the outer edge at
        // Width (1 + 2 delta)), and the nominal polygons of the width oracle (Width (1 + delta) wide)
        Statuses build_ramps(ExactSlopeKernel kernel, List<InPiece> pieces, double delta, double sMin, double theta, int nTheta,
                             double rFan, double H, double dMax, double minFeatureMax)
        {
            foreach (RampIn ri in rampsIn) {
                int r = ri.Index, n = ri.P.Count;
                List<Vector2d> P = ri.P;
                UsedRoadMargin = RoadMargin(delta, sMin);
                double gb = ri.G - RoadMargin(delta, sMin);
                if (!(gb > 0.5 * ri.G))
                    return ramp_problem(Statuses.InvalidInput, r, -1, $"the grade is too small for the slope margin (below {2 * RoadMargin(delta, sMin):G3})");
                if (ri.R.Width < minFeatureMax)
                    return ramp_problem(Statuses.InvalidInput, r, -1, $"Width is below the smallest feature that can be realized ({minFeatureMax:G3})");
                for (int i = 0; i + 1 < n; ++i)
                    if (ri.L[i] < minFeatureMax)
                        return ramp_problem(Statuses.InvalidInput, r, i, $"the path segment is shorter than the smallest feature that can be realized ({minFeatureMax:G3})");
                // (the nominal polygons lie strictly inside the road's: from the path at Width delta / 2 to Width
                // (1 + 3 delta / 2), their ends Width delta inside the segment's, so that no rounding of a boundary the
                // road shares with them decides the oracle)
                double W = ri.R.Width, Wb = W * (1 + 2 * delta), Wi = 0.5 * delta * W, Wn = W * (1 + 1.5 * delta), We = delta * W;
                var z = new double[n];
                z[0] = ri.Z0;
                for (int i = 0; i + 1 < n; ++i) z[i + 1] = z[i] + gb * ri.L[i];
                var u = new Vector2d[n - 1];
                for (int i = 0; i + 1 < n; ++i) u[i] = (P[i + 1] - P[i]) / ri.L[i];
                double sb = ri.Wall.S * (1 - delta);
                var sbSpec = SlopeSpec.Rise(sb);
                for (int k = 0; k < pieces.Count; ++k) {
                    InPiece pc = pieces[k];
                    if (pc.Kind != 1 || pc.Ramp != r) continue;
                    InPiece wall = pieces[k + 1];
                    RampUnit unit = pc.Unit;
                    int i = unit.Seg;
                    Vector2d[] c2;
                    Vector3d[] c3, roadPts;
                    if (unit.Bend == false) {
                        Vector2d nn = side_normal(u[i], ri.Sigma);
                        double E = ri.Extend[i];
                        c2 = new[] { P[i] - E * nn, P[i + 1] - E * nn, P[i + 1] + Wb * nn, P[i] + Wb * nn };
                        if (E == 0) c2 = new[] { P[i], P[i + 1], c2[2], c2[3] };
                        var p0 = new Vector3d(P[i].x, P[i].y, z[i]);
                        var p1 = new Vector3d(P[i + 1].x, P[i + 1].y, z[i + 1]);
                        var p2 = new Vector3d(c2[3].x, c2[3].y, z[i]);
                        if (Slope.IsNotSteeper(p0, p1, p2, ri.R.Grade) == false)
                            return ramp_problem(Statuses.InvalidInput, r, i, "the road plane is steeper than the grade (the margin does not cover the rounding)");
                        unit.RoadPlane = kernel.AddPlane(p0, p1, p2);
                        c3 = c2.Select(c => new Vector3d(c.x, c.y, height_down(kernel, unit.RoadPlane, p0, p1, p2, c.x, c.y))).ToArray();
                        roadPts = new[] { p0, p1, p2 };
                        pc.A = pc.A1 = p0; pc.B = pc.B1 = p1;
                        Vector2d na = P[i] + We * u[i] + Wi * nn, nb = P[i + 1] - We * u[i] + Wi * nn;
                        add_nominal(new RampNominal() { Ramp = r, Seg = i, A = na, B = nb,
                                                        Poly = new[] { na, nb, P[i + 1] - We * u[i] + Wn * nn, P[i] + We * u[i] + Wn * nn } });
                    } else {
                        Vector2d n0 = side_normal(u[i - 1], ri.Sigma), n1 = side_normal(u[i], ri.Sigma);
                        Vector2d v = P[i];
                        c2 = new[] { v, v + Wb * n0, v + miter(n0, n1, Wb), v + Wb * n1 };
                        unit.RoadPlane = kernel.AddHorizontalPlane(z[i]);
                        c3 = c2.Select(c => new Vector3d(c.x, c.y, z[i])).ToArray();
                        roadPts = new[] { c3[0], c3[1], c3[2] };
                        pc.A = pc.A1 = pc.B = pc.B1 = new Vector3d(v.x, v.y, z[i]);
                        Vector2d vi = v + miter(n0, n1, Wi), q1 = v + Wn * n0, mq = v + miter(n0, n1, Wn), q2 = v + Wn * n1;
                        add_nominal(new RampNominal() { Ramp = r, Seg = i, Bend = true, A = vi, Q1 = q1, M = mq, Q2 = q2, Poly = new[] { vi, q1, mq, q2 } });
                    }
                    // counter-clockwise
                    if (polygon_area(c3.Select(c => c.xy).ToArray()) < 0) Array.Reverse(c3);
                    unit.Corners = c3;
                    pc.Support = roadPts; wall.Support = c3;
                    wall.A = wall.A1 = pc.A; wall.B = wall.B1 = pc.B;
                    Statuses ws = make_polygon_walls(kernel, unit, sb, sbSpec, theta, nTheta, rFan, r);
                    if (ws != Statuses.Success)
                        return ws;
                    unit.Box = AxisAlignedBox2d.Empty;
                    foreach (Vector3d c in c3) unit.Box.Contain(c.xy);
                    unit.ZMin = c3.Min(c => c.z);
                    double gmax = 0;
                    for (int j = 0; j < c3.Length; ++j) {
                        Vector3d a = c3[j], b = c3[(j + 1) % c3.Length];
                        gmax = Math.Max(gmax, Math.Abs(b.z - a.z) / a.xy.Distance(b.xy));
                    }
                    unit.Slb = sb * (1 - gmax * gmax / (sb * sb)) * Math.Cos(2 * theta) * (1 - 1e-6);
                    pc.Slb = wall.Slb = unit.Slb;
                    pc.Sb = wall.Sb = sb;
                    var reach = unit.Box;
                    reach.Expand((H - unit.ZMin) / unit.Slb * 1.01 + 1e-9 * dMax);
                    unit.Reach = reach;
                    pc.Reach = wall.Reach = reach;
                }
            }
            return Statuses.Success;
        }

        void add_nominal(RampNominal nm)
        {
            if (polygon_area(nm.Poly) < 0) Array.Reverse(nm.Poly);
            nm.Box = AxisAlignedBox2d.Empty;
            foreach (Vector2d p in nm.Poly) nm.Box.Contain(p);
            rampNominal.Add(nm);
        }

        static double polygon_area(Vector2d[] p)
        {
            double a = 0;
            for (int i = 0; i < p.Length; ++i) {
                Vector2d q = p[i], w = p[(i + 1) % p.Length];
                a += q.x * w.y - q.y * w.x;
            }
            return 0.5 * a;
        }

        // the wall planes of a road polygon: per edge the strip plane rising outwards at sb, per corner the cone tangent
        // planes on the angle grid between its edges' strips; each checked exactly: not steeper than sb, and on or below
        // every corner (so the polygon's F is its road plane on the polygon, and the walls lie below its slope cone)
        Statuses make_polygon_walls(ExactSlopeKernel kernel, RampUnit unit, double sb, SlopeSpec sbSpec, double theta, int nTheta,
                                    double rFan, int ramp)
        {
            Vector3d[] C = unit.Corners;
            int m = C.Length;
            unit.Walls.Clear(); unit.Apexes.Clear(); unit.Fans.Clear(); unit.Angles.Clear();
            Func<Vector3d, Vector3d, Vector3d, bool> belowCorners = (p, q, rr) => {
                foreach (Vector3d c in C) if (Slope.IsPlaneOnOrBelow(p, q, rr, c) == false) return false;
                return true;
            };
            var stripAngle = new double[m];
            ExactDyadic es = ExactDyadic.Of(sb);
            for (int j = 0; j < m; ++j) {
                Vector3d a = C[j], b = C[(j + 1) % m];
                ExactDyadic edx = ExactDyadic.Of(b.x) - ExactDyadic.Of(a.x), edy = ExactDyadic.Of(b.y) - ExactDyadic.Of(a.y),
                    edz = ExactDyadic.Of(b.z) - ExactDyadic.Of(a.z);
                if ((edz * edz - es * es * (edx * edx + edy * edy)).Sign >= 0)
                    return ramp_problem(Statuses.ToeSteeperThanSlope, ramp, unit.Seg, "the road's edge is at least as steep as the wall's slope");
                double dx = b.x - a.x, dy = b.y - a.y, L = Math.Sqrt(dx * dx + dy * dy);
                Vector2d uu = new Vector2d(dx / L, dy / L), outward = new Vector2d(uu.y, -uu.x);
                double g = (b.z - a.z) / L, gn = Math.Sqrt(Math.Max(0, sb * sb - g * g));
                double rise = gn * L;
                Vector3d R = Vector3d.Zero;
                bool ok = false;
                for (int tries = 0; tries <= 60; ++tries) {
                    R = new Vector3d(b.x + outward.x * L, b.y + outward.y * L, b.z + rise);
                    if (Slope.IsNotSteeper(a, b, R, sbSpec)) { ok = true; break; }
                    rise *= 1 - 1e-12 * Math.Pow(2, tries);
                }
                if (ok == false || belowCorners(a, b, R) == false)
                    return ramp_problem(Statuses.Failed, ramp, unit.Seg, "a wall plane of the road polygon");
                unit.Walls.Add(kernel.AddPlane(a, b, R));
                Vector2d G = g * uu + gn * outward;
                stripAngle[j] = Math.Atan2(G.y, G.x);
            }
            for (int j = 0; j < m; ++j) {
                Vector3d c = C[j], prev = C[(j + m - 1) % m], next = C[(j + 1) % m];
                double l1 = c.xy.Distance(prev.xy), l2 = c.xy.Distance(next.xy);
                Vector2d e1 = (prev.xy - c.xy) / l1, e2 = (next.xy - c.xy) / l2;
                double g1 = (prev.z - c.z) / l1, g2 = (next.z - c.z) / l2;
                var planes = new List<int>();
                var angles = new List<double>();
                for (int mm = 0; mm < nTheta; ++mm) {
                    double ang = mm * (2 * Math.PI / nTheta);
                    var d = new Vector2d(Math.Cos(ang), Math.Sin(ang));
                    if (!(d.Dot(e1) < g1 / sb) || !(d.Dot(e2) < g2 / sb)) continue;
                    if (Math.Abs(wrap(stripAngle[(j + m - 1) % m] - ang)) < 0.5 * theta || Math.Abs(wrap(stripAngle[j] - ang)) < 0.5 * theta) continue;
                    double rise = sb * rFan;
                    var p1 = new Vector3d(c.x - d.y * rFan, c.y + d.x * rFan, c.z);
                    for (int tries = 0; tries < 60; ++tries) {
                        var p2 = new Vector3d(c.x + d.x * rFan, c.y + d.y * rFan, c.z + rise);
                        if (Slope.IsNotSteeper(c, p1, p2, sbSpec)) {
                            if (belowCorners(c, p1, p2)) {
                                planes.Add(kernel.AddPlane(c, p1, p2));
                                angles.Add(ang);
                            }
                            break;
                        }
                        rise *= 1 - 1e-12 * Math.Pow(2, tries);
                    }
                }
                unit.Apexes.Add(c.xy);
                unit.Fans.Add(planes);
                unit.Angles.Add(angles);
            }
            return Statuses.Success;
        }

        // the width oracle (TERRAIN_RAMPS.md): every nominal polygon (Width (1 + delta) wide, its width at least
        // Width, checked exactly) is road: no triangle of another kind overlaps its interior (exact), and the road
        // triangles cover its area (in doubles, to 1e-7: a hole can only be where nothing is built, outside the slope's
        // domain or above its height limit, which is no sliver). Else the crest comes closer
        // than Width to the outer edge, or the road hangs: RampNotOnWall
        Statuses ramp_coverage(DMesh3 mesh, List<InPiece> pieces)
        {
            var tris = new List<int>();
            var boxes = new List<AxisAlignedBox2d>();
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                var b = new AxisAlignedBox2d(mesh.GetVertex(tv.a).xy, mesh.GetVertex(tv.a).xy);
                b.Contain(mesh.GetVertex(tv.b).xy); b.Contain(mesh.GetVertex(tv.c).xy);
                tris.Add(t); boxes.Add(b);
            }
            string firstProblem = "";
            var problems = new SortedSet<Index2i>(Comparer<Index2i>.Create((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b.CompareTo(y.b)));
            foreach (RampNominal nm in rampNominal) {
                checkCancel();
                if (nominal_width_ok(nm) == false)
                    return ramp_problem(Statuses.Failed, nm.Ramp, nm.Seg, "the nominal road polygon is narrower than Width (rounding)");
                // (the areas relative to the polygon's first corner: at UTM the absolute cross products cancel)
                Vector2d o = nm.Poly[0];
                Vector2d[] local = nm.Poly.Select(p => p - o).ToArray();
                double area = polygon_area(local), covered = 0;
                bool intruded = false;
                Vector2d where = Vector2d.Zero;
                for (int k = 0; k < tris.Count && intruded == false; ++k) {
                    if (boxes[k].Intersects(nm.Box) == false) continue;
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    mesh.GetTriVertices(tris[k], ref a, ref b, ref c);
                    var txy = new[] { a.xy, b.xy, c.xy };
                    if (pieces[mesh.GetTriangleGroup(tris[k])].Kind == 1)
                        covered += Math.Abs(polygon_area(clip_convex(txy.Select(p => p - o).ToArray(), local).ToArray()));
                    else if (interiors_overlap(nm.Poly, txy)) {
                        intruded = true;
                        where = (a.xy + b.xy + c.xy) / 3;
                    }
                }
                if (intruded || covered < area * (1 - 1e-7)) {
                    if (problems.Count == 0)
                        firstProblem = $" (first: ramp {nm.Ramp} {(nm.Bend ? "bend" : "segment")} {nm.Seg}: " +
                                       (intruded ? $"a triangle at ({where.x:G6}, {where.y:G6}) enters the road)" : $"{covered / area:P3} of the road is built)");
                    problems.Add(new Index2i(nm.Ramp, nm.Seg));
                }
            }
            if (problems.Count == 0)
                return Statuses.Success;
            RampProblems.AddRange(problems);
            return fail(Statuses.RampNotOnWall, $"{problems.Count} road segments are not on the wall: the road needs fill, or the crest comes closer than Width to the outer edge" + firstProblem);
        }

        // the nominal polygon's outer side is at least Width from the path (exact): a segment's quad (its outer corners
        // from the path's line) or a bend's kite (its outer edges' lines from the vertex)
        bool nominal_width_ok(RampNominal nm)
        {
            double w = Ramps[nm.Ramp].Width;
            ExactDyadic ew = ExactDyadic.Of(w);
            Func<Vector2d, Vector2d, Vector2d, bool> far = (a, b, p) => {
                // dist(p, line ab)^2 >= w^2: cross^2 >= w^2 |b - a|^2
                ExactDyadic dx = ExactDyadic.Of(b.x) - ExactDyadic.Of(a.x), dy = ExactDyadic.Of(b.y) - ExactDyadic.Of(a.y);
                ExactDyadic px = ExactDyadic.Of(p.x) - ExactDyadic.Of(a.x), py = ExactDyadic.Of(p.y) - ExactDyadic.Of(a.y);
                ExactDyadic cr = dx * py - dy * px;
                return (cr * cr - ew * ew * (dx * dx + dy * dy)).Sign >= 0;
            };
            if (nm.Bend == false) {
                foreach (Vector2d p in nm.Poly)
                    if (p != nm.A && p != nm.B && far(nm.A, nm.B, p) == false) return false;
                return true;
            }
            // a bend: the vertex is at least w from each outer edge's line
            return far(nm.Q1, nm.M, nm.A) && far(nm.M, nm.Q2, nm.A);
        }

        // the interiors of a convex counter-clockwise polygon and a triangle overlap: no edge line of either separates
        // them (exact)
        internal static bool interiors_overlap(Vector2d[] poly, Vector2d[] tri)
        {
            int ot = ExactPredicates.Orient2d(tri[0], tri[1], tri[2]);
            if (ot == 0) return false;
            for (int i = 0; i < poly.Length; ++i) {
                bool separates = true;
                for (int j = 0; j < 3 && separates; ++j)
                    if (ExactPredicates.Orient2d(poly[i], poly[(i + 1) % poly.Length], tri[j]) > 0) separates = false;
                if (separates) return false;
            }
            for (int i = 0; i < 3; ++i) {
                bool separates = true;
                for (int j = 0; j < poly.Length && separates; ++j)
                    if (ExactPredicates.Orient2d(tri[i], tri[(i + 1) % 3], poly[j]) * ot > 0) separates = false;
                if (separates) return false;
            }
            return true;
        }

        // the intersection of a triangle with a counter-clockwise convex polygon (Sutherland-Hodgman, approximate)
        static List<Vector2d> clip_convex(Vector2d[] subject, Vector2d[] clipper)
        {
            var poly = new List<Vector2d>(subject);
            for (int e = 0; e < clipper.Length && poly.Count > 0; ++e) {
                Vector2d c0 = clipper[e], c1 = clipper[(e + 1) % clipper.Length];
                Func<Vector2d, double> side = (p) => (c1.x - c0.x) * (p.y - c0.y) - (c1.y - c0.y) * (p.x - c0.x);
                var next = new List<Vector2d>(poly.Count + 2);
                for (int i = 0; i < poly.Count; ++i) {
                    Vector2d p = poly[i], q = poly[(i + 1) % poly.Count];
                    double sp = side(p), sq = side(q);
                    if (sp >= 0) next.Add(p);
                    if ((sp > 0 && sq < 0) || (sp < 0 && sq > 0)) next.Add(p + (sp / (sp - sq)) * (q - p));
                }
                poly = next;
            }
            return poly;
        }
    }
}
