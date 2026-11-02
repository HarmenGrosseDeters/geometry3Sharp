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
    /// Terrain phase 1a: the slope surface from the cone envelope (docs/design/TERRAIN.md 5.1-5.4, 7). Every success
    /// path checks the result with oracles that share no code with the construction: O1 (Slope.IsSupportedBy per
    /// triangle, exact), O2 (a brute-force minimum of exact cones: 0 &lt;= S_ref - S &lt;= Tolerance), and the surface
    /// (finite, a height field with counter-clockwise triangles, manifold, boundary only at the toe, the lateral limits
    /// and the level line at HeightLimit).
    /// </summary>
    public class TerrainSlopeDesignTests
    {
        readonly ITestOutputHelper output;
        public TerrainSlopeDesignTests(ITestOutputHelper output) { this.output = output; }

        internal static List<Vector3d> Square(double half, double z, Vector2d c = default(Vector2d)) => new List<Vector3d> {
            new Vector3d(c.x - half, c.y - half, z), new Vector3d(c.x + half, c.y - half, z), new Vector3d(c.x + half, c.y + half, z), new Vector3d(c.x - half, c.y + half, z) };

        internal static TerrainSlopeDesign Design(double height, double tol, params SlopeToe[] toes) =>
            new TerrainSlopeDesign() { Toes = toes.ToList(), HeightLimit = height, Tolerance = tol };

        // ---- oracles ----

        // brute force: the lowest exact cone over all pieces (cut), sampled densely along each segment and exactly
        // minimized per segment (the cone of a segment: min over t of z(t) + s |x - p(t)|, by ternary search; convex in t)
        internal static double ConeEnvelope(List<SlopePiece> pieces, Vector2d x)
        {
            double best = double.MaxValue;
            foreach (var p in pieces) {
                double s = p.Spec.S;
                Func<double, double> f = (t) => {
                    Vector3d q = Vector3d.Lerp(p.P0, p.P1, t);
                    return q.z + s * q.xy.Distance(x);
                };
                double lo = 0, hi = 1;
                for (int k = 0; k < 100; ++k) {
                    double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
                    if (f(m1) < f(m2)) hi = m2; else lo = m1;
                }
                best = Math.Min(best, Math.Min(f(0.5 * (lo + hi)), Math.Min(f(0), f(1))));
            }
            return best;
        }

        internal static void AssertO1(TerrainSlopeDesign d, bool fill = false)
        {
            DMesh3 m = d.SlopeSurface;
            foreach (int tid in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(tid, ref a, ref b, ref c);
                SlopePiece p = d.Pieces[m.GetTriangleGroup(tid)];
                Vector3d p0 = p.P0, p1 = p.P1;
                if (fill) { a.z = -a.z; b.z = -b.z; c.z = -c.z; p0.z = -p0.z; p1.z = -p1.z; }
                Assert.True(Slope.IsSupportedBy(a, b, c, p0, p1, p.Spec), $"triangle {tid} of piece {m.GetTriangleGroup(tid)}");
            }
        }

        // O2 at the centroid and vertices of every triangle: 0 <= min(S_ref, HeightLimit) - S <= Tolerance (cut)
        internal static void AssertO2(TerrainSlopeDesign d, bool fill = false)
        {
            DMesh3 m = d.SlopeSurface;
            double sign = fill ? -1 : 1, worst = 0;
            foreach (int tid in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(tid, ref a, ref b, ref c);
                foreach (Vector3d q in new[] { a, b, c, (a + b + c) / 3 }) {
                    var pieces = d.Pieces.Select(p => new SlopePiece() { P0 = new Vector3d(p.P0.x, p.P0.y, sign * p.P0.z), P1 = new Vector3d(p.P1.x, p.P1.y, sign * p.P1.z), Spec = p.Spec }).ToList();
                    double diff = Math.Min(ConeEnvelope(pieces, q.xy), sign * d.HeightLimit) - sign * q.z;
                    double slack = 1e-9 * (Math.Abs(q.z) + 1);
                    Assert.True(diff >= -slack, $"above the cones by {-diff} at {q}");
                    Assert.True(diff <= d.Tolerance + slack, $"below the cones by {diff} > {d.Tolerance} at {q}");
                    worst = Math.Max(worst, diff);
                }
            }
        }

        // finite, manifold, counter-clockwise in XY, no XY overlaps (a height field), z within [toe, limit]
        internal static void AssertSurface(TerrainSlopeDesign d)
        {
            DMesh3 m = d.SlopeSurface;
            Assert.NotNull(m);
            Assert.True(m.TriangleCount > 0);
            m.CheckValidity(true, FailMode.ReturnOnly);
            Assert.True(Invariance.AllFinite(m));
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                Assert.Equal(1, ExactPredicates.Orient2d(m.GetVertex(t.a).xy, m.GetVertex(t.b).xy, m.GetVertex(t.c).xy));
            }
            Assert.Equal(0, TerrainVolumeDifference.XYOverlaps(m));
        }

        internal static void AssertAll(TerrainSlopeDesign d, bool fill = false)
        {
            Assert.Equal(TerrainSlopeDesign.Statuses.Success, d.Status);
            AssertSurface(d);
            AssertO1(d, fill);
            AssertO2(d, fill);
            Assert.True(d.MaxSlopeRatio <= 1);
        }

        // ---- cases ----

        [Fact]
        public void StraightToe_IsAPlane()
        {
            // open toe along x at z = 5, slope 1:2 to the left (+y), up to 15: the plane z = 5 + y / 2 for y in [0, 20]
            var toe = SlopeToe.Constant(new List<Vector3d> { new Vector3d(0, 0, 5), new Vector3d(40, 0, 5) }, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(2));
            var d = Design(15, 0.01, toe);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            output.WriteLine($"{d.SlopeSurface.TriangleCount} triangles, lowering {d.UsedToeLowering}, facet {d.FacetAngle}");
            foreach (Vector3d v in d.SlopeSurface.Vertices()) output.WriteLine(v.ToString());
            AssertAll(d);
            foreach (Vector3d v in d.SlopeSurface.Vertices()) {
                Assert.InRange(v.x, -1e-9, 40 + 1e-9);         // the lateral limits at the end normals
                Assert.InRange(v.y, -1e-9, 20 * (1 + 1e-5));
                Assert.True(Math.Abs(v.z - (5 + v.y / 2)) <= 0.01, $"{v}");
            }
            // the toe lies just below its specified height, the level line at the limit
            var b = d.SlopeSurface.Vertices().ToList();
            Assert.Contains(b, v => v.y == 0 && v.z <= 5 - d.UsedToeLowering && v.z >= 5 - 2 * d.UsedToeLowering);
            // (the level line of the cap: the map is lowered by λ, the cap with it, so it ends at H - λ)
            Assert.Contains(b, v => v.z <= 15 && v.z >= 15 - 2 * d.UsedToeLowering);
        }

        [Fact]
        public void Square_Outside_FansAtTheCorners()
        {
            var toe = SlopeToe.Constant(Square(10, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var d = Design(10, 0.01, toe);
            bool ok = d.Compute();
            Assert.True(ok, d.Status + " " + d.Message);
            output.WriteLine($"{d.SlopeSurface.TriangleCount} triangles, lowering {d.UsedToeLowering}, facet {d.FacetAngle}");
            AssertAll(d);
        }

        static List<Vector3d> Ngon(int n, double r, double z, double phase = 0.1) =>
            Enumerable.Range(0, n).Select(i => new Vector3d(r * Math.Cos(phase + 2 * Math.PI * i / n), r * Math.Sin(phase + 2 * Math.PI * i / n), z)).ToList();

        static TerrainSlopeDesign Run(double height, double tol, params SlopeToe[] toes)
        {
            var d = Design(height, tol, toes);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            return d;
        }

        [Theory]
        [InlineData(3)]
        [InlineData(7)]
        [InlineData(24)]
        public void RegularPolygon_Outside(int n)
        {
            var d = Run(8, 0.01, SlopeToe.Constant(Ngon(n, 15, 2), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)));
            AssertAll(d);
            // closed and complete: the boundary is the toe and the level line
            AssertBoundary(d);
        }

        // concave corners get valleys (the minimum clips), convex ones fans; no overlaps (a height field)
        [Theory]
        [InlineData("L")]
        [InlineData("U")]
        [InlineData("star")]
        public void ConcaveShapes_Outside(string shape)
        {
            List<Vector3d> pts;
            if (shape == "L")
                pts = new[] { (0.0, 0.0), (30, 0), (30, 10), (10, 10), (10, 30), (0, 30) }.Select(p => new Vector3d(p.Item1, p.Item2, 0)).ToList();
            else if (shape == "U")
                pts = new[] { (0.0, 0.0), (40, 0), (40, 30), (30, 30), (30, 10), (10, 10), (10, 30), (0, 30) }.Select(p => new Vector3d(p.Item1, p.Item2, 0)).ToList();
            else
                pts = Enumerable.Range(0, 10).Select(i => { double r = (i % 2 == 0) ? 20 : 8, a = Math.PI * i / 5; return new Vector3d(r * Math.Cos(a), r * Math.Sin(a), 0); }).ToList();
            var d = Run(6, 0.01, SlopeToe.Constant(pts, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)));
            AssertAll(d);
            AssertBoundary(d);
        }

        [Fact]
        public void Inside_Hill_And_Island()
        {
            // a hill inside a closed toe (cut rising inwards), and a pit floor with an island: outer toe outside,
            // the island's toe inside
            var hill = Run(5, 0.01, SlopeToe.Constant(Ngon(9, 20, 0), true, SlopeToe.Sides.Inside, SlopeSpec.Ratio(2)));
            AssertAll(hill);
            var outer = SlopeToe.Constant(Square(30, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var island = SlopeToe.Constant(Ngon(6, 8, 0), true, SlopeToe.Sides.Inside, SlopeSpec.Ratio(1));
            var d = Run(5, 0.01, outer, island);
            AssertAll(d);
            Assert.Contains(d.Pieces, p => p.Toe == 1);
            Assert.Contains(d.SlopeSurface.TriangleIndices(), t => d.Pieces[d.SlopeSurface.GetTriangleGroup(t)].Toe == 1);
            Assert.Contains(d.SlopeSurface.TriangleIndices(), t => d.Pieces[d.SlopeSurface.GetTriangleGroup(t)].Toe == 0);
        }

        // values per segment: a jump at a vertex (1:3 overburden, 1:1 rock), kept as specified; the gentle cone spreads
        // into the steep sector by h sqrt(sb^2 / sa^2 - 1) = 2.83 h (TERRAIN.md 2.2, the R1 transition width)
        [Fact]
        public void SlopeJump_AtAVertex_R1TransitionWidth()
        {
            var pts = new List<Vector3d> { new Vector3d(-60, 0, 0), new Vector3d(0, 0, 0), new Vector3d(60, 0, 0) };
            var toe = new SlopeToe() { Points = pts, Side = SlopeToe.Sides.Left,
                CutStart = new[] { SlopeSpec.Ratio(3), SlopeSpec.Ratio(1) } };
            var d = Run(10, 0.01, toe);
            AssertAll(d);
            DMesh3 m = d.SlopeSurface;
            // at height h = 5 in the steep sector (y = 5), x < 2.83 * 5 = 14.1 belongs to the gentle piece 0
            foreach (int t in m.TriangleIndices()) {
                Vector3d c = m.GetTriCentroid(t);
                int g = m.GetTriangleGroup(t);
                double h = c.y;       // above the toe in the steep sector
                if (c.x > 0 && c.y > 1) {
                    double edge = h * Math.Sqrt(9 - 1);
                    if (c.x < 0.9 * edge) Assert.Equal(0, g);
                    if (c.x > 1.1 * edge + 1) Assert.Equal(1, g);
                }
            }
        }

        // an inclined toe (a ramp of grade 0.1): the strip plane has gradient g u + sqrt(s^2 - g^2) n
        [Fact]
        public void InclinedToe()
        {
            var pts = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(50, 0, 5) };
            var d = Run(12, 0.01, SlopeToe.Constant(pts, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(1)));
            AssertAll(d);
            double gn = Math.Sqrt(1 - 0.01);
            foreach (Vector3d v in d.SlopeSurface.Vertices())
                if (v.z < 11.9 && v.x > 10 && v.x < 40) Assert.True(Math.Abs(v.z - (0.1 * v.x + gn * v.y)) < 0.01 + 1e-9, v.ToString());   // in the strip (its side lines are not perpendicular)
        }

        // |g| >= s_build exactly: the toe rises as steeply as the slope
        [Fact]
        public void ToeSteeperThanSlope_IsReported()
        {
            var pts = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(10, 0, 0), new Vector3d(20, 0, 10) };
            var d = Design(15, 0.01, SlopeToe.Constant(pts, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(1)));
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.ToeSteeperThanSlope, d.Status);
            Assert.Equal(new[] { new Index2i(0, 1) }, d.ToeProblems);
            Assert.Null(d.SlopeSurface);
            // slightly gentler than the slope: fine
            pts[2] = new Vector3d(20, 0, 9.9);
            Assert.True(d.Compute(), d.Message);
            AssertAll(d);
        }

        // a two-level pit floor: the low part's cone passes below the high part of the toe
        [Fact]
        public void TwoLevelToe_IsUndercut()
        {
            // a narrow open toe folded into a U: the far leg is 8 m lower, 10 m away
            // the legs 20 m apart, joined by a gentle ramp around the east end (grade 0.18 < 0.25)
            var pts = new List<Vector3d> { new Vector3d(0, 20, 8), new Vector3d(40, 20, 8), new Vector3d(60, 10, 4), new Vector3d(40, 0, 0), new Vector3d(0, 0, 0) };
            var d = Design(20, 0.01, SlopeToe.Constant(pts, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(4)));
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.ToeUndercut, d.Status);
            Assert.Contains(new Index2i(0, 0), d.ToeProblems);
            Assert.Null(d.SlopeSurface);
            // 1:2 (s = 0.5): the low leg's cone reaches the high leg at 10 m, above it: no undercut
            d.Toes[0] = SlopeToe.Constant(pts, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(2));
            Assert.True(d.Compute(), d.Message);
            AssertAll(d);
        }

        [Fact]
        public void InvalidInputs_AreRejected()
        {
            Func<SlopeToe, double, double, TerrainSlopeDesign.Statuses> run = (toe, h, tol) => {
                var d = Design(h, tol, toe);
                Assert.False(d.Compute());
                Assert.Null(d.SlopeSurface);
                return d.Status;
            };
            var ok = SlopeToe.Constant(Square(10, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, run(ok, 5, double.NaN));            // no Tolerance
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, run(ok, double.NaN, 0.01));         // no height
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, run(ok, -1, 0.01));                 // below the toe
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, run(ok, 5, 1e-12));                // below the margins
            // side: a closed toe needs Inside / Outside, an open one Left / Right
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, run(SlopeToe.Constant(Square(10, 0), true, SlopeToe.Sides.Left, SlopeSpec.Ratio(1)), 5, 0.01));
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, run(SlopeToe.Constant(Square(10, 0), false, SlopeToe.Sides.Inside, SlopeSpec.Ratio(1)), 5, 0.01));
            // duplicate point, self-intersection (a bow tie), NaN, invalid slope, slopes not matching the segments
            var dup = Square(10, 0); dup.Insert(1, dup[1]);
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, run(SlopeToe.Constant(dup, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)), 5, 0.01));
            var bow = Square(10, 0); var tmp = bow[1]; bow[1] = bow[2]; bow[2] = tmp;
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, run(SlopeToe.Constant(bow, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)), 5, 0.01));
            var nan = Square(10, 0); nan[2] = new Vector3d(double.NaN, 1, 0);
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, run(SlopeToe.Constant(nan, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)), 5, 0.01));
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, run(SlopeToe.Constant(Square(10, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(0)), 5, 0.01));
            var mism = SlopeToe.Constant(Square(10, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)); mism.CutStart = mism.CutStart.Take(3).ToArray();
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, run(mism, 5, 0.01));
            // two toes that cross
            var d2 = Design(5, 0.01, ok, SlopeToe.Constant(Square(10, 0, new Vector2d(5, 5)), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)));
            Assert.False(d2.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d2.Status);
            // not in phase 1a: Auto, a slope changing along a segment
            var auto = Design(5, 0.01, ok); auto.Direction = TerrainSlopeDesign.Directions.Auto;
            Assert.False(auto.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.NotImplemented, auto.Status);
            var vary = SlopeToe.Constant(Square(10, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)); vary.CutEnd[1] = SlopeSpec.Ratio(2);
            Assert.Equal(TerrainSlopeDesign.Statuses.NotImplemented, run(vary, 5, 0.01));
        }

        // fill: the dump falls from its crest (the toe) outwards, down to the limit; checked mirrored (z -> -z)
        [Fact]
        public void Fill_IsTheMirroredCut()
        {
            var toe = SlopeToe.Constant(Square(10, 20), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5));
            var d = Design(12, 0.01, toe);
            d.Direction = TerrainSlopeDesign.Directions.Fill;
            Assert.True(d.Compute(), d.Message);
            AssertAll(d, true);
            Assert.All(d.SlopeSurface.Vertices(), v => Assert.InRange(v.z, 12 - 1e-9, 20 + 2 * d.UsedToeLowering));
            // the mirrored cut gives the mirrored surface, bit for bit
            var mirrored = SlopeToe.Constant(Square(10, -20), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5));
            var c = Run(-12, 0.01, mirrored);
            var back = new DMesh3(c.SlopeSurface);
            foreach (int v in back.VertexIndices()) { var p = back.GetVertex(v); back.SetVertex(v, new Vector3d(p.x, p.y, -p.z)); }
            Assert.Equal("", Invariance.Difference(d.SlopeSurface, back));
        }

        // several toes interact in one envelope: two pits whose walls meet get a ridge
        [Fact]
        public void TwoPits_MeetInARidge()
        {
            var a = SlopeToe.Constant(Square(8, 0, new Vector2d(-12, 0)), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var b = SlopeToe.Constant(Square(8, 0, new Vector2d(12, 0)), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var d = Run(10, 0.01, a, b);
            AssertAll(d);
            // the ridge at x = 0 lies at height 4 (4 m from both toes)
            double top = d.SlopeSurface.Vertices().Where(v => Math.Abs(v.x) < 1e-9 && Math.Abs(v.y) < 4).Select(v => v.z).DefaultIfEmpty(double.NaN).Max();
            Assert.True(Math.Abs(top - 4) < 0.01, top.ToString());
        }

        [Fact]
        public void Utm_Coordinates()
        {
            var c = new Vector2d(632117.25, 5712043.5);
            var d = Run(140, 0.01, SlopeToe.Constant(Ngon(11, 40, 113.75).Select(p => new Vector3d(p.x + c.x, p.y + c.y, p.z)).ToList(), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)));
            output.WriteLine($"{d.SlopeSurface.TriangleCount} triangles, lowering {d.UsedToeLowering}, margin {d.UsedSlopeMargin}, feature {d.UsedMinFeatureSize}");
            AssertAll(d);
        }

        // closed and without gaps: every boundary edge lies on the toe (z at most the toe) or on the level line
        // (both ends at the limit), or on a lateral limit (open toes)
        internal static void AssertBoundary(TerrainSlopeDesign d)
        {
            DMesh3 m = d.SlopeSurface;
            foreach (int eid in m.BoundaryEdgeIndices()) {
                Index2i ev = m.GetEdgeV(eid);
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                // (the level line ends at HeightLimit - λ: the map is lowered by λ, the cap with it)
                double eps = 1e-9 * (1 + Math.Abs(d.HeightLimit)), level0 = d.HeightLimit + (d.Direction == TerrainSlopeDesign.Directions.Fill ? 1 : -1) * d.UsedToeLowering;
                bool level = Math.Abs(a.z - level0) <= eps && Math.Abs(b.z - level0) <= eps;
                bool toe = d.Pieces.Any(p => on_segment(p.P0.xy, p.P1.xy, a.xy) && on_segment(p.P0.xy, p.P1.xy, b.xy));
                Assert.True(level || toe, $"boundary edge {a} - {b}");
            }
        }

        static bool on_segment(Vector2d p, Vector2d q, Vector2d x)
        {
            double t = (x - p).Dot(q - p) / (q - p).LengthSquared;
            return t >= -1e-12 && t <= 1 + 1e-12 && (p + t * (q - p)).Distance(x) <= 1e-9 * (1 + p.Length);
        }

        // ---- regressions of the independent review (uneven toe heights with mixed slopes per segment) ----

        static List<Vector3d> P(params (double, double, double)[] p) => p.Select(q => new Vector3d(q.Item1, q.Item2, q.Item3)).ToList();

        // A: open, uneven, mixed specs (failed with SlopeViolation: the sector window wrapped at +-pi, and the
        // planes of a failed attempt were kept for the next one)
        [Fact]
        public void RegularPolygon_StripCreaseThroughTheOppositeVertex()
        {
            // found by the 1b stress test: on a regular 10-gon the line where the strips of two pieces are equal passes
            // within a few ulps of the opposite toe vertex, which left a cluster of map vertices 1e-14 apart there; their
            // edges could not all be collapsed (link condition) and a degenerate triangle failed the check at every margin
            var toe = SlopeToe.Constant(Ngon(10, 9.642226746139222, 0, 0.29190628476995334), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.9346294006959672));
            var d = Run(13.65915220256275, 0.05, toe);
            AssertAll(d);
            // the merged vertices' triangles may rise above S̃ by 2 sMax (r + e_xy), r = 64 ulps: the toe lowering of a
            // run that merges covers it (a lower bound from the output's largest coordinate, whose ulp is at most e_xy)
            double m = d.SlopeSurface.Vertices().Max(v => Math.Max(Math.Abs(v.x), Math.Abs(v.y)));
            double ulp = Math.Abs(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(m) + 1) - m);
            Assert.True(d.UsedToeLowering >= 2 * d.Pieces.Max(p => p.Spec.S) * 65 * ulp, $"λ {d.UsedToeLowering}");
            output.WriteLine($"attempts {d.Attempts}, λ {d.UsedToeLowering:G3}, δ {d.UsedSlopeMargin:G3}");
        }

        // regular polygons whose realization merges positions within 64 ulps (found by a sweep over random regular
        // polygons) and whose measured lowering alone stays below what the merge needs
        [Theory]
        [InlineData(16, 21.30606409921593, 0.07628677649250569, 1.4751108086086395)]
        [InlineData(4, 23.804800668174774, 0.6008761141453293, 2.775734977692242)]
        public void Merge_RaisesTheToeLowering(int n, double r, double phase, double ratio)
        {
            var toe = SlopeToe.Constant(Ngon(n, r, 0, phase), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(ratio));
            var d = Run(r * 0.6, 0.05, toe);
            AssertAll(d);
            // the triangles of a merged vertex may rise above S̃ by 2 sMax (r + e_xy): the lowering covers it
            Assert.True(d.MergedPositions > 0);
            Assert.True(d.MergeLambdaRequired > 0);
            Assert.True(d.UsedToeLowering >= d.MergeLambdaRequired, $"λ {d.UsedToeLowering} < {d.MergeLambdaRequired}");
        }

        [Fact]
        public void Merge_Shares()
        {
            // below S̃ by at most 4 sMax (r + e_xy), above by at most 2 sMax (r + e_xy) plus the vertex rounding 4 e_z,
            // r = 64 e_xy (TERRAIN_1B_PLAN.md, reviews)
            double exy = Math.Pow(2, -30), ez = 3 * Math.Pow(2, -31), s = 1.5;
            Assert.Equal(64 * exy, TerrainSlopeDesign.MergeRadius(exy));
            // (merge 4 sMax (r + e_xy) and a needle flip 2 sMax (r + e_xy) below; each 2 sMax (r + e_xy) above)
            Assert.Equal(2 * s * 65 * exy, TerrainSlopeDesign.FlipShare(exy, s));
            Assert.Equal(4 * s * 65 * exy + 2 * s * 65 * exy, TerrainSlopeDesign.MergeShare(exy, s));
            Assert.Equal(4 * ez + 2 * s * 65 * exy, TerrainSlopeDesign.MergeLambda(ez, exy, s));
            Assert.Equal(4 * ez + 4 * s * 65 * exy, TerrainSlopeDesign.RepairLambda(ez, exy, s, 2));
        }

        // the uneven wavy toe of the Hambach test (TerrainBenchmark README), without the terrain, at UTM: needles of the
        // realization failed the check at every margin (SlopeViolation at δ 5e-4): an edge collapse next to vertices on
        // one crease line joined them into a triangle with an altitude of 1e-13 m. Collapses now keep the shapes (fails
        // without it: 3 builds and a margin raise)
        internal static TerrainSlopeDesign WavyHambach(double h)
        {
            var c = new Vector3d(328375.25, 5642875.5, -340);
            var pts = new List<Vector3d>();
            for (int i = 0; i < 48; ++i) {
                double a = 2 * Math.PI * i / 48, rr = 150 * (1 + 0.15 * Math.Sin(5 * a));
                pts.Add(c + new Vector3d(rr * Math.Cos(a), rr * Math.Sin(a), 4 * Math.Sin(3 * a)));
            }
            return Design(c.z + h, 0.05, SlopeToe.Constant(pts, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)));
        }

        [Fact]
        public void WavyToe_130mHigh_AtUtm()
        {
            // 130 m: fans of needles 90 m long and 1e-10 m wide from a face of the map between two creases that meet at
            // an angle of 1e-12 (nearly coplanar tangent planes of neighbouring apexes); flips across its sides remove
            // them (fails without: 3 builds and two margin raises)
            var d = WavyHambach(130);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            AssertO1(d);
            AssertSurface(d);
            Assert.Equal(d.SlopeMargin, d.UsedSlopeMargin);
            Assert.Equal(1, d.Attempts);
            // the flips need the lowering of a repair (2 sMax (r + e_xy) above S̃)
            Assert.True(d.NeedleFlips > 0);
            Assert.True(d.UsedToeLowering >= d.MergeLambdaRequired, $"λ {d.UsedToeLowering} < {d.MergeLambdaRequired}");
        }

        [Fact]
        public void WavyToe_100mHigh_AtUtm()
        {
            var d = WavyHambach(100);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            AssertO1(d);
            AssertSurface(d);
            // (without the needles no margin raise is needed: one build)
            Assert.Equal(d.SlopeMargin, d.UsedSlopeMargin);
            Assert.Equal(1, d.Attempts);
            Assert.True(d.UsedToeLowering >= d.MergeLambdaRequired);
            output.WriteLine($"builds {d.Attempts}, realizations {d.Realizations}, δ {d.UsedSlopeMargin:G3}, λ {d.UsedToeLowering:G3}, {d.SlopeSurface.TriangleCount} triangles");
        }

        [Fact]
        public void Review_OpenUneven_MixedSlopes()
        {
            var pts = P((12, 16, 0), (0.934, 24, 0), (-12.773, 19.097, 0), (-11.63, 4.284, 1), (-24, -6.67, 0), (-14, -18, 1), (-1, -19, 0), (13.011, -19, 0), (16, -6, 0));
            var specs = new[] { SlopeSpec.Percent(107), SlopeSpec.Percent(122), SlopeSpec.Ratio(1.5), SlopeSpec.Ratio(2), SlopeSpec.Ratio(0.5), SlopeSpec.Percent(139), SlopeSpec.Percent(126), SlopeSpec.Ratio(3) };
            var d = Run(8.592199603837077, 0.01, new SlopeToe() { Points = pts, Side = SlopeToe.Sides.Right, CutStart = specs });
            AssertAll(d);
        }

        // B: closed, fill, uneven, mixed specs
        [Fact]
        public void Review_ClosedFill_MixedSlopes()
        {
            var pts = P((13, 11.003, 0), (1.715, 11, 0), (-11, 17, 0), (-21.84, 7, 0), (-10, -4, 0), (-6, -10, 2), (2.304, -11.61, 2), (18, -14.676, 0), (19.496, 0.418, 0));
            var specs = new[] { SlopeSpec.Ratio(1.5), SlopeSpec.Percent(48), SlopeSpec.Percent(69), SlopeSpec.Percent(115), SlopeSpec.Ratio(0.5), SlopeSpec.Ratio(1.5), SlopeSpec.Percent(81), SlopeSpec.Percent(83), SlopeSpec.Ratio(3) };
            var d = Design(-8.409746392355181, 0.01, new SlopeToe() { Points = pts, Closed = true, Side = SlopeToe.Sides.Outside, FillStart = specs });
            d.Direction = TerrainSlopeDesign.Directions.Fill;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            AssertAll(d, true);
        }

        // MaxSlopeRatio is an upper bound below 1 also with needles (the double ratio was 1.00104 there)
        [Fact]
        public void Review_MaxSlopeRatio_IsABound()
        {
            var pts = P((6.227, 9, 0), (-7.869, 10, 2), (-9.524, -3.464, 0), (1, -16, 0), (12.617, -3.621, 0));
            var specs = new[] { SlopeSpec.Percent(44), SlopeSpec.Percent(87), SlopeSpec.Ratio(3), SlopeSpec.Ratio(2), SlopeSpec.Percent(40) };
            var d = Run(8.20194518808366, 0.01, new SlopeToe() { Points = pts, Closed = true, Side = SlopeToe.Sides.Outside, CutStart = specs });
            AssertAll(d);
            Assert.True(d.MaxSlopeRatio <= 1);
        }

        // an open toe with its slope on the concave side: the end normals cross, and each ray ends at the other
        [Fact]
        public void Review_OpenConcave_RaysCross()
        {
            var pts = P((0, 0, 0), (10, 0, 0), (10, 10, 0));
            var d = Run(3, 0.01, SlopeToe.Constant(pts, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(1)));
            AssertAll(d);
            Assert.All(d.SlopeSurface.Vertices(), v => { Assert.InRange(v.x, -1e-9, 10 + 1e-9); Assert.InRange(v.y, -1e-9, 10 + 1e-9); });
            var right = Run(3, 0.01, SlopeToe.Constant(pts, false, SlopeToe.Sides.Right, SlopeSpec.Ratio(1)));
            AssertAll(right);
        }

        // statuses instead of exceptions; slopes whose S is 0 or subnormal are invalid
        [Fact]
        public void Review_ExtremeSlopes_GiveStatuses()
        {
            foreach (var spec in new[] { SlopeSpec.Ratio(1e300), SlopeSpec.Percent(4.9e-324), SlopeSpec.Rise(1e-310), SlopeSpec.Degrees(1e-320) }) {
                var d = Design(5, 0.01, SlopeToe.Constant(Square(10, 0), true, SlopeToe.Sides.Outside, spec));
                Assert.False(d.Compute());
                Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
                Assert.Null(d.SlopeSurface);
            }
            Assert.False(SlopeSpec.Percent(4.9e-324).IsValid);
            Assert.True(SlopeSpec.Ratio(1e300).IsValid);    // S = 1e-300 is a valid slope, too gentle for any height here
        }

        // a notch shorter than the feature size is rejected with its location, not a SlopeViolation
        [Fact]
        public void Review_ShortToeSegment_IsInvalid()
        {
            var pts = P((0, 0, 0), (10, 0, 0), (10, 1e-12, 0), (20, 0, 0), (20, 20, 0), (0, 20, 0));
            var d = Design(6, 0.01, SlopeToe.Constant(pts, true, SlopeToe.Sides.Inside, SlopeSpec.Ratio(1)));
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            Assert.Contains(d.ToeProblems, p => p.b == 1 || p.b == 2);
        }

        // owner: the toe lowering is measured on the realized output. Near the origin it is tiny, at UTM it follows
        // the rounding of the output coordinates; the verdicts agree
        [Fact]
        public void ToeLowering_Origin_vs_Utm()
        {
            Func<Vector2d, TerrainSlopeDesign> make = (c) => Run(40 + 113.75, 0.01, SlopeToe.Constant(Ngon(9, 60, 113.75).Select(p => new Vector3d(p.x + c.x, p.y + c.y, p.z)).ToList(), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)));
            var origin = make(Vector2d.Zero);
            var utm = make(new Vector2d(632117.25, 5712043.5));
            AssertAll(origin);
            AssertAll(utm);
            output.WriteLine($"lowering: origin {origin.UsedToeLowering:G3}, utm {utm.UsedToeLowering:G3}; margin {origin.UsedSlopeMargin} / {utm.UsedSlopeMargin}");
            Assert.True(origin.UsedToeLowering < 1e-9, origin.UsedToeLowering.ToString());
            Assert.True(utm.UsedToeLowering < 1e-4, utm.UsedToeLowering.ToString());
            Assert.True(utm.UsedToeLowering > origin.UsedToeLowering);
        }

        // the toe lowering is measured per run (not a constant): it grows with the reach D (the extrapolation distance of
        // check (ii)), stays below its reserved bound, and every run passes the exact check
        [Fact]
        public void ToeLowering_IsMeasuredPerRun()
        {
            var c = new Vector2d(632117.25, 5712043.5);
            var lows = new List<double>();
            foreach (double h in new[] { 5.0, 20.0, 80.0 }) {
                var d = Run(113.75 + h, 0.05, SlopeToe.Constant(Ngon(9, 60, 113.75).Select(p => new Vector3d(p.x + c.x, p.y + c.y, p.z)).ToList(), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)));
                AssertAll(d);
                output.WriteLine($"h {h}: lowering {d.UsedToeLowering:G3}, builds {d.Attempts}");
                Assert.True(d.UsedToeLowering > 0 && d.UsedToeLowering < 1.1 * d.UsedSlopeMargin * 1.0 * (h / (1 - d.UsedSlopeMargin)) * 1.1 + 1e-12);
                lows.Add(d.UsedToeLowering);
            }
            Assert.True(lows[2] > lows[0], string.Join(", ", lows));
        }

        // bounds review: the ratio bound must be at least limit / S exactly; the former Up(Up(r)) was below it for
        // r = 0.9962444311474499, S = 4.026449396535283 (limit = Up(r S))
        [Fact]
        public void RatioBound_IsAtLeastLimitOverS()
        {
            var rnd = new Random(23);
            var cases = new List<(double, double)> { (0.9962444311474499, 4.026449396535283) };
            for (int i = 0; i < 20000; ++i) cases.Add((0.5 + 0.5 * rnd.NextDouble(), Math.Exp(rnd.NextDouble() * 20 - 10)));
            int oldFails = 0;
            foreach (var (r, S) in cases) {
                double limit = ExactInterval.Up(r * S);
                double bound = TerrainSlopeDesign.RatioOfLimit(limit, S);
                // bound * S >= limit, exactly
                Assert.True((ExactDyadic.Of(bound) * ExactDyadic.Of(S) - ExactDyadic.Of(limit)).Sign >= 0, $"{r} {S}");
                double old = ExactInterval.Up(ExactInterval.Up(r));
                if ((ExactDyadic.Of(old) * ExactDyadic.Of(S) - ExactDyadic.Of(limit)).Sign < 0) oldFails++;
            }
            // the old formula fails on the review's case (and others)
            double r0 = 0.9962444311474499, S0 = 4.026449396535283;
            Assert.True((ExactDyadic.Of(ExactInterval.Up(ExactInterval.Up(r0))) * ExactDyadic.Of(S0) - ExactDyadic.Of(ExactInterval.Up(r0 * S0))).Sign < 0);
            Assert.True(oldFails > 0);
        }
    }
}
