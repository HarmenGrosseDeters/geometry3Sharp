using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Terrain ramps R1 (docs/design/TERRAIN_RAMPS.md). Every success path is checked by oracles that share no code with
    /// the construction: the surface (finite, counter-clockwise, manifold, no XY overlap), each triangle not steeper than
    /// its piece's spec (the Grade for road triangles), the slope triangles supported by their toe piece (O1), the surface
    /// between a brute-force reference from below (Tolerance) and above (the specified road and its slope cone, the toe's
    /// cone envelope), the road's heights, and its width (the strip from the path to Width is road).
    /// </summary>
    public class TerrainSlopeRampTests
    {
        readonly ITestOutputHelper output;
        public TerrainSlopeRampTests(ITestOutputHelper output) { this.output = output; }

        // a square pit (toe at z, slope 1:1) with an extra toe vertex at the ramp's start, so that the start is exactly on
        // the toe in every frame
        internal static List<Vector3d> PitWithStart(double half, double z, Vector2d start)
        {
            var sq = TerrainSlopeDesignTests.Square(half, z);
            sq.Insert(1, new Vector3d(start.x, start.y, z));
            return sq;
        }

        internal static TerrainSlopeDesign Straight(double grade = 10, double width = 4, Vector2d? end = null)
        {
            var start = new Vector2d(-10, -20);
            var toe = SlopeToe.Constant(PitWithStart(20, 0, start), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var ramp = new SlopeRamp() {
                Toe = 0, Path = new List<Vector2d>() { start, end ?? new Vector2d(15, -23) },
                Grade = SlopeSpec.Percent(grade), Width = width, Side = SlopeToe.Sides.Right
            };
            return new TerrainSlopeDesign() { Toes = new List<SlopeToe>() { toe }, Ramps = new List<SlopeRamp>() { ramp }, HeightLimit = 10, Tolerance = 0.01 };
        }

        // along the bottom wall, then around the pit's convex corner up the right wall (a bend away from the road)
        internal static TerrainSlopeDesign AroundCorner()
        {
            var start = new Vector2d(-10, -20);
            var toe = SlopeToe.Constant(PitWithStart(20, 0, start), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var ramp = new SlopeRamp() {
                Toe = 0, Path = new List<Vector2d>() { start, new Vector2d(24, -25), new Vector2d(33, 0) },
                Grade = SlopeSpec.Percent(10), Width = 4, Side = SlopeToe.Sides.Right
            };
            return new TerrainSlopeDesign() { Toes = new List<SlopeToe>() { toe }, Ramps = new List<SlopeRamp>() { ramp }, HeightLimit = 16, Tolerance = 0.01 };
        }

        // (debug) the slope surface as triangles with kinds, for a picture: G3_RAMP_DUMP=<directory>
        internal static void Dump(TerrainSlopeDesign d, string name)
        {
            string dir = Environment.GetEnvironmentVariable("G3_RAMP_DUMP");
            DMesh3 m = d.Composite ?? d.SlopeSurface ?? d.LastRealized;
            if (string.IsNullOrEmpty(dir) || m == null) return;
            using (var w = new System.IO.StreamWriter(System.IO.Path.Combine(dir, name + ".txt")))
                foreach (int t in m.TriangleIndices()) {
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    m.GetTriVertices(t, ref a, ref b, ref c);
                    int g = m.GetTriangleGroup(t);
                    int k = g >= 0 && g < d.Pieces.Count ? (int)d.Pieces[g].Kind : (d.LastPieceKinds != null && g >= 0 && g < d.LastPieceKinds.Length ? d.LastPieceKinds[g] : 0);
                    w.WriteLine(FormattableString.Invariant($"{k} {a.x} {a.y} {a.z} {b.x} {b.y} {b.z} {c.x} {c.y} {c.z}"));
                }
        }

        // ---- oracles (independent of the construction) ----

        // the specified road of a ramp: per path segment its polygon (inner edge, outer edge at Width; inner side moved
        // 'inward' towards the pit) with the road's plane, per bend away from the road the flat kite
        sealed class RefUnit
        {
            public Vector2d[] Poly;      // counter-clockwise
            public Func<Vector2d, double> Z;
            public int Seg; public bool Bend;
        }

        static double StartHeight(TerrainSlopeDesign d, SlopeRamp r)
        {
            if (double.IsNaN(r.StartHeight) == false) return r.StartHeight;
            var pts = d.Toes[r.Toe].Points;
            for (int i = 0; i < pts.Count; ++i) {
                Vector3d a = pts[i], b = pts[(i + 1) % pts.Count];
                var seg = new Segment2d(a.xy, b.xy);
                if (seg.DistanceSquared(r.Path[0]) <= 1e-24 * (1 + r.Path[0].LengthSquared)) {
                    double t = (r.Path[0] - a.xy).Length / (b.xy - a.xy).Length;
                    return a.z + t * (b.z - a.z);
                }
            }
            throw new Exception("the path does not start on its toe");
        }

        static List<RefUnit> RefUnits(TerrainSlopeDesign d, SlopeRamp r, double inward)
        {
            var units = new List<RefUnit>();
            var P = r.Path;
            double g = r.Grade.S, sigma = r.Side == SlopeToe.Sides.Left ? 1 : -1, z = StartHeight(d, r), W = r.Width;
            var zs = new List<double>() { z };
            for (int i = 0; i + 1 < P.Count; ++i) zs.Add(zs[i] + g * P[i].Distance(P[i + 1]));
            Func<Vector2d, Vector2d> nrm = (u) => new Vector2d(-u.y * sigma, u.x * sigma);
            for (int i = 0; i + 1 < P.Count; ++i) {
                Vector2d u = (P[i + 1] - P[i]).Normalized, n = nrm(u), v = P[i];
                double zi = zs[i];
                var poly = new[] { P[i] - inward * n, P[i + 1] - inward * n, P[i + 1] + W * n, P[i] + W * n };
                if (Area(poly) < 0) Array.Reverse(poly);
                units.Add(new RefUnit() { Poly = poly, Z = (x) => zi + g * (x - v).Dot(u), Seg = i });
                if (i + 2 < P.Count) {
                    Vector2d u1 = (P[i + 2] - P[i + 1]).Normalized, n1 = nrm(u1);
                    if ((u.x * u1.y - u.y * u1.x) * sigma < 0) {
                        Vector2d m = P[i + 1] + (W / (1 + n.Dot(n1))) * (n + n1);
                        var kite = new[] { P[i + 1], P[i + 1] + W * n, m, P[i + 1] + W * n1 };
                        if (Area(kite) < 0) Array.Reverse(kite);
                        double zk = zs[i + 1];
                        units.Add(new RefUnit() { Poly = kite, Z = (x) => zk, Seg = i + 1, Bend = true });
                    }
                }
            }
            return units;
        }

        static double Area(Vector2d[] p)
        {
            double a = 0;
            for (int i = 0; i < p.Length; ++i) a += p[i].x * p[(i + 1) % p.Length].y - p[i].y * p[(i + 1) % p.Length].x;
            return a / 2;
        }

        static bool Inside(Vector2d[] ccw, Vector2d x)
        {
            for (int i = 0; i < ccw.Length; ++i) {
                Vector2d a = ccw[i], b = ccw[(i + 1) % ccw.Length];
                if ((b.x - a.x) * (x.y - a.y) - (b.y - a.y) * (x.x - a.x) < 0) return false;
            }
            return true;
        }

        // a road unit and its slope cone s: the plane on the polygon, min over its boundary of z(q) + s |x - q| outside
        static double UnitCone(RefUnit u, double s, Vector2d x)
        {
            if (Inside(u.Poly, x)) return u.Z(x);
            double best = double.MaxValue;
            for (int i = 0; i < u.Poly.Length; ++i) {
                Vector2d a = u.Poly[i], b = u.Poly[(i + 1) % u.Poly.Length];
                Func<double, double> f = (t) => { Vector2d q = Vector2d.Lerp(a, b, t); return u.Z(q) + s * q.Distance(x); };
                double lo = 0, hi = 1;
                for (int k = 0; k < 100; ++k) {
                    double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
                    if (f(m1) < f(m2)) hi = m2; else lo = m1;
                }
                best = Math.Min(best, Math.Min(f(0.5 * (lo + hi)), Math.Min(f(0), f(1))));
            }
            return best;
        }

        static double WallSlope(TerrainSlopeDesign d, SlopeRamp r)
        {
            var toe = d.Toes[r.Toe];
            for (int i = 0; i < toe.SegmentCount; ++i) {
                var seg = new Segment2d(toe.Points[i].xy, toe.Points[(i + 1) % toe.Points.Count].xy);
                if (seg.DistanceSquared(r.Path[0]) <= 1e-24 * (1 + r.Path[0].LengthSquared)) return toe.CutStart[i].S;
            }
            throw new Exception("no toe segment");
        }

        // the reference surfaces: the toe's cone envelope with the specified roads (inner side at the path: an upper
        // bound of the built surface, whose road polygons are larger and lower) or with roads that reach far inwards
        // (a lower bound up to Tolerance)
        static double Reference(TerrainSlopeDesign d, Vector2d x, bool upper)
        {
            var slopes = d.Pieces.Where(p => p.Kind == SlopePiece.Kinds.Slope).ToList();
            double best = TerrainSlopeDesignTests.ConeEnvelope(slopes, x);
            foreach (SlopeRamp r in d.Ramps) {
                double s = WallSlope(d, r);
                foreach (RefUnit u in RefUnits(d, r, upper ? 0 : 1e3))
                    best = Math.Min(best, UnitCone(u, s, x));
            }
            return best;
        }

        static int Containing(DMesh3 m, Vector2d x)
        {
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                Vector2d a = m.GetVertex(tv.a).xy, b = m.GetVertex(tv.b).xy, c = m.GetVertex(tv.c).xy;
                if (ExactPredicates.Orient2d(a, b, x) >= 0 && ExactPredicates.Orient2d(b, c, x) >= 0 && ExactPredicates.Orient2d(c, a, x) >= 0) return t;
            }
            return -1;
        }

        static bool OnSegment(Vector2d p, Vector2d q, Vector2d x)
        {
            double t = (x - p).Dot(q - p) / (q - p).LengthSquared;
            return t >= -1e-12 && t <= 1 + 1e-12 && (p + t * (q - p)).Distance(x) <= 1e-9 * (1 + p.Length);
        }

        internal static void AssertRamps(TerrainSlopeDesign d, int samples = 12)
        {
            Assert.Equal(TerrainSlopeDesign.Statuses.Success, d.Status);
            TerrainSlopeDesignTests.AssertSurface(d);
            DMesh3 m = d.SlopeSurface;
            Assert.True(d.MaxSlopeRatio <= 1);
            Assert.Contains(d.Pieces, p => p.Kind == SlopePiece.Kinds.Road);
            // (with a terrain the surface ends at its local height limit: the highest vertex, up to the toe lowering)
            double limit = double.IsNaN(d.HeightLimit) ? m.Vertices().Max(v => v.z) + d.UsedToeLowering : d.HeightLimit;
            // the boundary: only the toe and the level line of the height limit (the walls above the roads are complete)
            var toes = d.Pieces.Where(p => p.Kind == SlopePiece.Kinds.Slope).ToList();
            foreach (int eid in m.BoundaryEdgeIndices()) {
                Index2i ev = m.GetEdgeV(eid);
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                double eps = 1e-9 * (1 + Math.Abs(limit));
                bool level = Math.Abs(a.z - (limit - d.UsedToeLowering)) <= eps && Math.Abs(b.z - (limit - d.UsedToeLowering)) <= eps;
                bool toe = toes.Any(p => OnSegment(p.P0.xy, p.P1.xy, a.xy) && OnSegment(p.P0.xy, p.P1.xy, b.xy));
                Assert.True(level || toe, $"boundary edge {a} - {b}");
            }
            foreach (int tid in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(tid, ref a, ref b, ref c);
                SlopePiece p = d.Pieces[m.GetTriangleGroup(tid)];
                Assert.True(Slope.IsNotSteeper(a, b, c, p.Spec), $"triangle {tid} ({p.Kind}) steeper than {p.Spec}");
                if (p.Kind == SlopePiece.Kinds.Slope) Assert.True(Slope.IsSupportedBy(a, b, c, p.P0, p.P1, p.Spec), $"triangle {tid}");
                if (p.Kind == SlopePiece.Kinds.Road) {
                    // the road's heights: within Tolerance below the specified road
                    SlopeRamp r = d.Ramps[p.Ramp];
                    RefUnit u = RefUnits(d, r, 1e3).First(x => x.Seg == p.Segment && x.Bend == p.Bend);
                    foreach (Vector3d q in new[] { a, b, c }) {
                        double zr = u.Z(q.xy), slack = 1e-9 * (Math.Abs(zr) + 1);
                        Assert.True(q.z <= zr + slack && q.z >= zr - d.Tolerance - slack, $"road vertex {q}: specified {zr}");
                    }
                }
                foreach (Vector3d q in new[] { a, b, c, (a + b + c) / 3 }) {
                    double slack = 1e-9 * (Math.Abs(q.z) + 1);
                    double up = Math.Min(Reference(d, q.xy, true), limit), lo = Math.Min(Reference(d, q.xy, false), limit);
                    Assert.True(q.z <= up + slack, $"above the reference by {q.z - up} at {q}");
                    Assert.True(q.z >= lo - d.Tolerance - slack, $"below the reference by {lo - q.z} at {q}");
                }
            }
            // the width: the strip from the path to Width (strictly inside) is road
            foreach (SlopeRamp r in d.Ramps) {
                double sigma = r.Side == SlopeToe.Sides.Left ? 1 : -1;
                for (int i = 0; i + 1 < r.Path.Count; ++i) {
                    Vector2d u = (r.Path[i + 1] - r.Path[i]).Normalized, n = new Vector2d(-u.y * sigma, u.x * sigma);
                    for (int a = 1; a < samples; ++a)
                        foreach (double w in new[] { 1e-3, 0.37, 0.71, 0.999 }) {
                            Vector2d x = Vector2d.Lerp(r.Path[i], r.Path[i + 1], (double)a / samples) + w * r.Width * n;
                            int t = Containing(m, x);
                            Assert.True(t >= 0 && d.Pieces[m.GetTriangleGroup(t)].Kind == SlopePiece.Kinds.Road, $"ramp {d.Ramps.IndexOf(r)} segment {i}: not road at {x}");
                        }
                }
            }
        }

        // ---- design cases ----

        [Fact]
        public void StraightRamp_InASquarePit()
        {
            var d = Straight();
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Dump(d, "straight");
            AssertRamps(d);
            Assert.Equal(2, d.Pieces.Count(p => p.Kind != SlopePiece.Kinds.Slope));
            // the wall above the road is pushed back: at the path's middle, Width outwards the surface is the road's
            // height, where the pit without a ramp has its slope
            var plain = Straight(); plain.Ramps.Clear();
            Assert.True(plain.Compute());
            Vector2d x = new Vector2d(2.5, -21.5) + 3.9 * new Vector2d(-1.5, -12.5).Normalized;
            double zRamp = TerrainSlopeDesignTests.HeightAt(d.SlopeSurface, x), zPlain = TerrainSlopeDesignTests.HeightAt(plain.SlopeSurface, x);
            Assert.True(zPlain - zRamp > 3, $"{zRamp} vs {zPlain}");
        }

        [Fact]
        public void Ramp_AroundAConvexCorner_HasAFlatBend()
        {
            var d = AroundCorner();
            bool ok = d.Compute();
            Dump(d, "corner");
            Assert.True(ok, d.Status + " " + d.Message + " " + string.Join(",", d.RampProblems));
            AssertRamps(d);
            SlopePiece bend = d.Pieces.Single(p => p.Kind == SlopePiece.Kinds.Road && p.Bend);
            Assert.Equal(1, bend.Segment);
            // the bend's road is flat at the vertex height (its vertices take the lowest plane there, within the toe lowering)
            DMesh3 m = d.SlopeSurface;
            int g = d.Pieces.IndexOf(bend);
            var tris = m.TriangleIndices().Where(t => m.GetTriangleGroup(t) == g).ToList();
            Assert.NotEmpty(tris);
            foreach (int t in tris) {
                Index3i tv = m.GetTriangle(t);
                foreach (int v in new[] { tv.a, tv.b, tv.c })
                    Assert.InRange(m.GetVertex(v).z, bend.P0.z - 1e-6, bend.P0.z);
            }
        }
    
        [Fact]
        public void Ramp_EndingAtDaylight_WithTerrainAndComposite()
        {
            var start = new Vector2d(-30, -40);
            var toe = SlopeToe.Constant(PitWithStart(40, 0, start), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var ramp = new SlopeRamp() {
                Toe = 0, Path = new List<Vector2d>() { start, new Vector2d(35, -47) }, Grade = SlopeSpec.Percent(10), Width = 5, Side = SlopeToe.Sides.Right
            };
            // a flat terrain 5 above the toe: the road (up to 6.5) daylights about 50 along the path
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-90, -90, 90, 90), 18, 18, (x, y) => 5);
            var d = new TerrainSlopeDesign() {
                Toes = new List<SlopeToe>() { toe }, Ramps = new List<SlopeRamp>() { ramp }, Terrain = terrain, Tolerance = 0.05, BuildComposite = true
            };
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Dump(d, "daylight");
            AssertRamps(d, 8);
            TerrainSlopeCatchLineTests.AssertComposite(d);
            // the catch lines: on the terrain, and between the references (the cones of the toe and of the roads)
            Assert.NotEmpty(d.CatchLines);
            foreach (CatchLine l in d.CatchLines)
                foreach (Vector3d q in l.Points) {
                    Assert.True(Math.Abs(q.z - 5) <= 1e-9, $"catch point {q} off the terrain");
                    Assert.True(q.z <= Reference(d, q.xy, true) + 1e-9, $"catch point {q} above the reference");
                    Assert.True(q.z >= Reference(d, q.xy, false) - d.Tolerance - 1e-9, $"catch point {q} below the reference");
                }
            // the composite's road: below the terrain, and only up to where the road reaches it
            DMesh3 r = d.SlopeRegion;
            var road = r.TriangleIndices().Where(t => d.Pieces[r.GetTriangleGroup(t)].Kind == SlopePiece.Kinds.Road).ToList();
            Assert.NotEmpty(road);
            double far = 0;
            foreach (int t in road) {
                Index3i tv = r.GetTriangle(t);
                foreach (int v in new[] { tv.a, tv.b, tv.c }) {
                    Vector3d q = r.GetVertex(v);
                    Assert.True(q.z <= 5 + 1e-9, $"road above the terrain at {q}");
                    far = Math.Max(far, (q.xy - start).Dot((ramp.Path[1] - start).Normalized));
                }
            }
            // (z = 0.1 x arc: the road daylights at 50, the end face and the crest a little beyond)
            Assert.InRange(far, 49.5, 56);
            // the cut volume grows by the road's cut
            var plain = new TerrainSlopeDesign() { Toes = new List<SlopeToe>() { toe }, Terrain = terrain, Tolerance = 0.05, BuildComposite = true };
            Assert.True(plain.Compute(), plain.Message);
            Assert.True(d.Volume > plain.Volume + 100, $"{d.Volume} vs {plain.Volume}");
        }

        // ---- statuses ----

        [Fact]
        public void RampNotOnWall_TheRoadHangs()
        {
            // the path leaves the toe at a smaller angle than the grade over the slope: the road would need fill
            var d = Straight(10, 4, new Vector2d(15, -21));
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.RampNotOnWall, d.Status);
            Assert.Equal(TerrainSlopeDesign.StatusKinds.GeometricallyInfeasible, d.StatusKind);
            Assert.Contains(new Index2i(0, 0), d.RampProblems);
            Assert.Null(d.SlopeSurface);
            // the same angle with a flatter grade is on the wall
            var ok = Straight(3, 4, new Vector2d(15, -21));
            Assert.True(ok.Compute(), ok.Status + " " + ok.Message);
            AssertRamps(ok);
        }

        [Fact]
        public void RampNotOnWall_TheCrestComesTooClose()
        {
            // a road on the wall whose crest lies inside the strip near its start: StartHeight above the toe
            var d = Straight();
            d.Ramps[0].StartHeight = 1.5;
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.RampNotOnWall, d.Status);
            Assert.Contains(new Index2i(0, 0), d.RampProblems);
        }

        [Fact]
        public void ToeSteeperThanSlope_TheGradeReachesTheWall()
        {
            var d = Straight(120, 4, new Vector2d(15, -40));
            d.HeightLimit = 50;
            Assert.False(d.Compute());
            Assert.True(d.Status == TerrainSlopeDesign.Statuses.ToeSteeperThanSlope, d.Status + " " + d.Message);
            Assert.Equal(TerrainSlopeDesign.Statuses.ToeSteeperThanSlope, d.Status);
            Assert.Contains(new Index2i(0, 0), d.RampProblems);
        }

        [Fact]
        public void InvalidInputs_AndLimits_AreReported()
        {
            Func<Action<TerrainSlopeDesign>, TerrainSlopeDesign> run = (change) => { var d = Straight(); change(d); d.Compute(); return d; };
            Action<TerrainSlopeDesign.Statuses, Action<TerrainSlopeDesign>> expect = (st, change) => {
                var d = run(change);
                Assert.True(st == d.Status, $"{d.Status} ({d.Message}) instead of {st}");
                Assert.NotEmpty(d.RampProblems);
            };
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Path[0] = new Vector2d(-10, -20.5));       // not on the toe
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Path = new List<Vector2d>() { new Vector2d(-10, -20) });
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Path.Add(d.Ramps[0].Path[1]));
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Path.Add(new Vector2d(double.NaN, 0)));
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Width = 0);
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Width = double.PositiveInfinity);
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Grade = SlopeSpec.Degrees(5));
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Grade = SlopeSpec.Ratio(10));
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Grade = SlopeSpec.Percent(0));
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Toe = 1);
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Side = SlopeToe.Sides.Outside);
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].StartHeight = double.PositiveInfinity);
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.HeightLimit = 2);                                     // below the road's top
            // a self-intersecting path (each turn away from the road, so only the crossing is wrong)
            expect(TerrainSlopeDesign.Statuses.InvalidInput, d => d.Ramps[0].Path = Enumerable.Range(0, 9)
                .Select(k => new Vector2d(-10, -20) + Enumerable.Range(0, k).Aggregate(Vector2d.Zero, (acc, j) => acc + 3 * new Vector2d(Math.Cos(j * Math.PI / 3), Math.Sin(j * Math.PI / 3)))).ToList());
            // R1 limits: a bend towards the road (a concave wall), a turn too sharp for the grade, fill and Auto
            expect(TerrainSlopeDesign.Statuses.NotImplemented, d => d.Ramps[0].Path = new List<Vector2d>() { new Vector2d(-10, -20), new Vector2d(5, -23), new Vector2d(15, -28) });
            expect(TerrainSlopeDesign.Statuses.NotImplemented, d => d.Ramps[0].Path = new List<Vector2d>() { new Vector2d(-10, -20), new Vector2d(5, -23), new Vector2d(5.5, -10) });
            var fill = run(d => d.Direction = TerrainSlopeDesign.Directions.Fill);
            Assert.Equal(TerrainSlopeDesign.Statuses.NotImplemented, fill.Status);
            var auto = run(d => d.Direction = TerrainSlopeDesign.Directions.Auto);
            Assert.Equal(TerrainSlopeDesign.Statuses.NotImplemented, auto.Status);
            Assert.Equal(TerrainSlopeDesign.StatusKinds.UnsupportedByMethod, auto.StatusKind);
        }

        // ---- invariance, reuse ----

        static TerrainSlopeDesign Make(Similarity s, bool local)
        {
            var d = AroundCorner();
            foreach (var t in d.Toes) t.Points = t.Points.Select(p => s.Apply(p)).ToList();
            foreach (var r in d.Ramps) r.Path = r.Path.Select(p => s.Apply(new Vector3d(p.x, p.y, 0)).xy).ToList();
            d.HeightLimit = s.Apply(new Vector3d(0, 0, d.HeightLimit)).z;
            d.Tolerance = s.ApplyLength(d.Tolerance);
            d.Ramps[0].Width = s.ApplyLength(d.Ramps[0].Width);
            d.LocalFrame = local;
            return d;
        }

        static Similarity ZUp(Similarity s) =>
            s.IsRotated ? new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation) : s;

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(Similarity.Identity, true);
            Assert.True(d.Compute(), d.Message);
            return d;
        });

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities_InALocalFrame(string name)
        {
            Similarity s = ZUp(Similarity.ByName(name));
            var d = Make(s, true);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.False(d.LocalFrameFallback);
            var r = reference.Value;
            if (s.IsExact) {
                // 2^k: the same result, scaled, bit for bit
                Assert.Equal("", Invariance.Difference(r.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, s)));
                Assert.Equal(r.Pieces.Select(p => (p.Kind, p.Segment, p.Bend)), d.Pieces.Select(p => (p.Kind, p.Segment, p.Bend)));
            } else {
                // the same pieces, every triangle checked in its own frame, and the same road area (the local frame is
                // the input's up to the translation by UsedOrigin)
                Assert.Equal(r.Pieces.Select(p => (p.Kind, p.Segment, p.Bend)), d.Pieces.Select(p => (p.Kind, p.Segment, p.Bend)));
                DMesh3 m = d.SlopeSurface;
                foreach (int tid in m.TriangleIndices()) {
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    m.GetTriVertices(tid, ref a, ref b, ref c);
                    Assert.True(Slope.IsNotSteeper(a, b, c, d.Pieces[m.GetTriangleGroup(tid)].Spec));
                }
                Func<TerrainSlopeDesign, double> roadArea = (x) => x.SlopeSurface.TriangleIndices()
                    .Where(t => x.Pieces[x.SlopeSurface.GetTriangleGroup(t)].Kind == SlopePiece.Kinds.Road)
                    .Sum(t => { Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero; x.SlopeSurface.GetTriVertices(t, ref a, ref b, ref c); return Math.Abs((b.xy - a.xy).Cross(c.xy - a.xy)) / 2; });
                double ar = roadArea(r), ad = roadArea(d) / (s.Scale * s.Scale);
                Assert.True(Math.Abs(ar - ad) <= 1e-3 * ar, $"road area {ad} vs {ar}");
            }
        }

        [Fact]
        public void Reuse_ComputeTwice_AndAfterAnotherInput()
        {
            var a = AroundCorner();
            Assert.True(a.Compute());
            DMesh3 first = a.SlopeSurface;
            Assert.True(a.Compute());
            Assert.Equal("", Invariance.Difference(first, a.SlopeSurface));
            // a failing ramp, then the first input again on the same instance
            var path = a.Ramps[0].Path;
            a.Ramps[0].Path = new List<Vector2d>() { new Vector2d(-10, -20), new Vector2d(15, -21) };
            Assert.False(a.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.RampNotOnWall, a.Status);
            a.Ramps[0].Path = path;
            Assert.True(a.Compute());
            Assert.Equal("", Invariance.Difference(first, a.SlopeSurface));
            Assert.Empty(a.RampProblems);
            // without ramps the instance gives the plain design
            a.Ramps.Clear();
            Assert.True(a.Compute());
            var plain = AroundCorner(); plain.Ramps.Clear();
            Assert.True(plain.Compute());
            Assert.Equal("", Invariance.Difference(plain.SlopeSurface, a.SlopeSurface));
        }
    }
}
