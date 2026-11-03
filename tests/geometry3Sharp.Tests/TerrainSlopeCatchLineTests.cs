using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Terrain phase 1b: the overlay of the slope surface with the terrain, the catch lines and the region
    /// (docs/design/TERRAIN.md 5.5, TERRAIN_1B_PLAN.md). Oracles independent of the construction: every catch point lies
    /// on the terrain (both surfaces are linear on its overlay edge, so only rounding is left) and on the slope surface,
    /// between the exact cones and Tolerance below them; the lines are simple, closed or ending with a reason; the exact
    /// signs of D agree with rational arithmetic.
    /// </summary>
    public class TerrainSlopeCatchLineTests
    {
        readonly ITestOutputHelper output;
        public TerrainSlopeCatchLineTests(ITestOutputHelper output) { this.output = output; }

        // ---- terrains ----

        internal static DMesh3 Grid(AxisAlignedBox2d box, int nx, int ny, Func<double, double, double> f, bool flip = false)
        {
            var m = new DMesh3();
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i) {
                    double x = box.Min.x + (box.Max.x - box.Min.x) * i / nx, y = box.Min.y + (box.Max.y - box.Min.y) * j / ny;
                    m.AppendVertex(new Vector3d(x, y, f(x, y)));
                }
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 2, d = a + nx + 1;
                    if (((i + j) % 2 == 0) != flip) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                    else { m.AppendTriangle(a, b, d); m.AppendTriangle(b, c, d); }
                }
            return m;
        }

        // the terrain height at p (the triangle that contains it; NaN outside)
        internal static double TerrainZ(DMesh3 t, Vector2d p)
        {
            foreach (int tid in t.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                t.GetTriVertices(tid, ref a, ref b, ref c);
                double o = Orient(a.xy, b.xy, c.xy);
                double l0 = Orient(b.xy, c.xy, p) / o, l1 = Orient(c.xy, a.xy, p) / o, l2 = Orient(a.xy, b.xy, p) / o;
                double eps = -1e-12;
                if (l0 >= eps && l1 >= eps && l2 >= eps) return l0 * a.z + l1 * b.z + l2 * c.z;
            }
            return double.NaN;
        }

        static double Orient(Vector2d a, Vector2d b, Vector2d c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        // ---- oracles ----

        // every catch point on the terrain (up to rounding) and between the exact cones and Tolerance below them;
        // the lines simple (exact segment tests), closed or ending with a reason
        internal static void AssertCatchLines(TerrainSlopeDesign d, bool fill = false)
        {
            double sign = fill ? -1 : 1;
            var pieces = d.Pieces.Select(p => new SlopePiece() { P0 = new Vector3d(p.P0.x, p.P0.y, sign * p.P0.z), P1 = new Vector3d(p.P1.x, p.P1.y, sign * p.P1.z), Spec = p.Spec }).ToList();
            foreach (CatchLine line in d.CatchLines) {
                Assert.True(line.Points.Count >= 2);
                if (line.Closed) Assert.True(line.Points.Count >= 3);
                else {
                    Assert.NotEqual(CatchLine.EndReasons.None, line.StartReason);
                    Assert.NotEqual(CatchLine.EndReasons.None, line.EndReason);
                }
                foreach (Vector3d c in line.Points) {
                    Assert.True(MathUtil.IsFinite(c.x) && MathUtil.IsFinite(c.y) && MathUtil.IsFinite(c.z));
                    double scale = 1e-9 * (Math.Abs(c.z) + Math.Abs(c.x) + Math.Abs(c.y) + 1);
                    double tz = TerrainZ(d.Terrain, c.xy);
                    Assert.True(Math.Abs(tz - c.z) <= scale, $"catch point {c} is {c.z - tz} off the terrain");
                    double diff = sign * (TerrainSlopeDesignTests.ConeEnvelope(pieces, c.xy) - sign * c.z);
                    Assert.True(sign * diff >= -scale && sign * diff <= d.Tolerance + scale, $"catch point {c}: cones - S = {diff}");
                }
            }
            // simple: no two segments of all lines cross or touch except neighbours
            var segs = new List<(Vector2d, Vector2d, int, int)>();
            for (int l = 0; l < d.CatchLines.Count; ++l) {
                var p = d.CatchLines[l].Points;
                int n = d.CatchLines[l].Closed ? p.Count : p.Count - 1;
                for (int i = 0; i < n; ++i) segs.Add((p[i].xy, p[(i + 1) % p.Count].xy, l, i));
            }
            for (int i = 0; i < segs.Count; ++i)
                for (int j = i + 1; j < segs.Count; ++j) {
                    var s = segs[i]; var t = segs[j];
                    if (s.Item1 == s.Item2 || t.Item1 == t.Item2) continue;
                    bool shared = s.Item1 == t.Item1 || s.Item1 == t.Item2 || s.Item2 == t.Item1 || s.Item2 == t.Item2;
                    if (shared) continue;
                    Assert.False(cross(s.Item1, s.Item2, t.Item1, t.Item2), $"catch lines cross: line {s.Item3} seg {s.Item4}, line {t.Item3} seg {t.Item4}");
                }
        }

        internal static double XYArea(DMesh3 m)
        {
            double area = 0;
            // (relative to a corner of the bounds: at UTM the products of raw coordinates would round away the area)
            Vector2d o = m.GetBounds().Min.xy;
            foreach (int tid in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(tid, ref a, ref b, ref c);
                area += 0.5 * Math.Abs(Orient(a.xy - o, b.xy - o, c.xy - o));
            }
            return area;
        }

        // the composite: finite, counter-clockwise, a height field covering exactly the terrain's footprint, the slope
        // part passing the exact check against its pieces (O1), every catch point a vertex of both parts
        internal static void AssertComposite(TerrainSlopeDesign d, bool fill = false)
        {
            DMesh3 c = d.Composite, r = d.SlopeRegion;
            Assert.NotNull(c); Assert.NotNull(r);
            foreach (int tid in c.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, e = Vector3d.Zero;
                c.GetTriVertices(tid, ref a, ref b, ref e);
                Assert.True(MathUtil.IsFinite(a.z) && MathUtil.IsFinite(b.z) && MathUtil.IsFinite(e.z));
                Assert.True(ExactPredicates.Orient2d(a.xy, b.xy, e.xy) > 0, $"composite triangle {tid} not counter-clockwise");
            }
            Assert.Equal(0, TerrainVolumeDifference.XYOverlaps(c));
            // one vertex per position (a copied terrain triangle shares the vertices of the cut ones, no crack between
            // them; where the slope steps off the terrain, at the toe, the cap or a lateral limit, the z differ)
            Assert.Equal(c.VertexCount, c.Vertices().Distinct().Count());
            // (no gaps: the area of the terrain, up to the rounding of the area sums and the ulps by which terrain
            // vertices on the toe and the lateral limits are moved off the slope side, a strip of a few ulps there)
            double at = XYArea(d.Terrain), ac = XYArea(c);
            var bb = d.Terrain.GetBounds();
            double maxAbs = Math.Max(Math.Max(Math.Abs(bb.Min.x), Math.Abs(bb.Max.x)), Math.Max(Math.Abs(bb.Min.y), Math.Abs(bb.Max.y)));
            double ulp = Math.Abs(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(maxAbs) + 1) - maxAbs);
            double toeLength = d.Toes.Sum(t => Enumerable.Range(0, t.SegmentCount).Sum(i => t.Points[i].xy.Distance(t.Points[(i + 1) % t.Points.Count].xy)));
            double slack = 1e-12 * at + 8 * ulp * (toeLength + 2 * (bb.Width + bb.Height));
            Assert.True(Math.Abs(at - ac) <= slack, $"composite area {ac} vs terrain {at} (slack {slack})");
            // (exactly on a toe segment, in the outputs' frame)
            Func<Vector2d, bool> onToe = (q) => d.Toes.Any(toe => Enumerable.Range(0, toe.SegmentCount).Any(i => {
                Vector2d p0 = toe.Points[i].xy - d.UsedOrigin, p1 = toe.Points[(i + 1) % toe.Points.Count].xy - d.UsedOrigin;
                return ExactPredicates.Orient2d(p0, p1, q) == 0 && (q - p0).Dot(p1 - p0) >= 0 && (q - p1).Dot(p0 - p1) >= 0;
            }));
            string tj = TJunction(c, onToe, d.Pieces.Count); Assert.True(tj == "", tj);
            // (and no vertical crack: two vertices at one XY are a step, at heights at least λ apart)
            foreach (var atXY in c.Vertices().GroupBy(p => p.xy).Where(x => x.Count() > 1)) {
                var zs = atXY.Select(p => p.z).OrderBy(z => z).ToList();
                for (int i = 0; i + 1 < zs.Count; ++i)
                    Assert.True(zs[i + 1] - zs[i] >= d.UsedToeLowering, $"two vertices at {atXY.Key} {zs[i]:R} {zs[i + 1]:R}, less than λ {d.UsedToeLowering:R} apart");
            }
            foreach (int tid in r.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, e = Vector3d.Zero;
                r.GetTriVertices(tid, ref a, ref b, ref e);
                SlopePiece p = d.Pieces[r.GetTriangleGroup(tid)];
                Vector3d p0 = p.P0, p1 = p.P1;
                // (in a mixed run each piece by its own direction)
                if (fill || p.Direction == TerrainSlopeDesign.Directions.Fill) { a.z = -a.z; b.z = -b.z; e.z = -e.z; p0.z = -p0.z; p1.z = -p1.z; }
                Assert.True(Slope.IsSupportedBy(a, b, e, p0, p1, p.Spec), $"region triangle {tid}");
            }
            var cv = new HashSet<Vector3d>(c.Vertices());
            var rv = new HashSet<Vector3d>(r.Vertices());
            foreach (var line in d.CatchLines)
                foreach (var q in line.Points) {
                    // (the composite moves catch points nearer than MinFeatureSize together, or onto a slope vertex, by
                    // less than MinFeatureSize each)
                    double dc = cv.Min(x => x.xy.Distance(q.xy)), dr = rv.Min(x => x.xy.Distance(q.xy));
                    Assert.True(dc < d.UsedMinFeatureSize, $"catch point {q:R} not in the composite ({dc}, MinFeatureSize {d.UsedMinFeatureSize})");
                    Assert.True(dr < d.UsedMinFeatureSize, $"catch point {q:R} not in the slope region ({dr}, MinFeatureSize {d.UsedMinFeatureSize})");
                }
        }

        // a crack in a height field (a T-junction): a vertex exactly (Orient2d) strictly inside a boundary edge in XY, so
        // the triangles on its two sides do not share that edge; the area and the overlaps do not see it. Where the slope
        // steps off the terrain (the toe, the cap, a lateral limit, a toe crossing in a mixed run) the boundary is open by
        // design, between a slope and a terrain triangle at heights that differ by λ at least. So it is a crack where the
        // vertex has a triangle of the edge's group (terrain next to terrain, or one slope piece), or where its height is
        // within 2 ulps of the edge's there (eg slope next to terrain along a catch line, where both share their vertices),
        // unless it lies on a step between slope and terrain (onStep: exactly on the toe, where the step's height goes to
        // 0 where the toe meets the terrain; terrainGroup the composite's terrain group)
        internal static string TJunction(DMesh3 c, Func<Vector2d, bool> onStep = null, int terrainGroup = int.MinValue)
        {
            var bb = c.GetBounds();
            Func<double, double> ulpOf = (x) => Math.Abs(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1) - x);
            double ulpZ = ulpOf(Math.Max(Math.Abs(bb.Min.z), Math.Abs(bb.Max.z)));
            var verts = c.VertexIndices().ToList();
            foreach (int e in c.BoundaryEdgeIndices()) {
                Index2i ev = c.GetEdgeV(e);
                Vector3d a = c.GetVertex(ev.a), b = c.GetVertex(ev.b);
                double x0 = Math.Min(a.x, b.x), x1 = Math.Max(a.x, b.x), y0 = Math.Min(a.y, b.y), y1 = Math.Max(a.y, b.y);
                int g = c.GetTriangleGroup(c.GetEdgeT(e).a);
                foreach (int v in verts) {
                    if (v == ev.a || v == ev.b) continue;
                    Vector3d q = c.GetVertex(v);
                    if (q.x < x0 || q.x > x1 || q.y < y0 || q.y > y1 || q.xy == a.xy || q.xy == b.xy) continue;
                    if ((q.xy - a.xy).Dot(b.xy - a.xy) <= 0 || (q.xy - b.xy).Dot(a.xy - b.xy) <= 0) continue;
                    if (ExactPredicates.Orient2d(a.xy, b.xy, q.xy) != 0) continue;
                    double t = (q.xy - a.xy).Length / (b.xy - a.xy).Length;
                    bool sameGroup = c.VtxTrianglesItr(v).Any(x => c.GetTriangleGroup(x) == g);
                    // (a step: one side terrain, the other slope)
                    bool step = onStep != null && onStep(q.xy) && (g == terrainGroup) != c.VtxTrianglesItr(v).Any(x => c.GetTriangleGroup(x) == terrainGroup);
                    if (sameGroup || (Math.Abs(q.z - (a.z + (b.z - a.z) * t)) <= 2 * ulpZ && step == false))
                        return $"vertex {v} ({q.x:R}, {q.y:R}, {q.z:R}) in boundary edge {e} ({a.x:R}, {a.y:R}, {a.z:R}) ({b.x:R}, {b.y:R}, {b.z:R}), groups {string.Join(",", c.VtxTrianglesItr(v).Select(x => c.GetTriangleGroup(x)))} / {g}";
                }
            }
            return "";
        }

        static bool cross(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            int o1 = ExactPredicates.Orient2d(a, b, c), o2 = ExactPredicates.Orient2d(a, b, d);
            int o3 = ExactPredicates.Orient2d(c, d, a), o4 = ExactPredicates.Orient2d(c, d, b);
            return o1 * o2 <= 0 && o3 * o4 <= 0 && !(o1 == 0 && o2 == 0 && o3 == 0 && o4 == 0 && disjoint_collinear(a, b, c, d));
        }

        static bool disjoint_collinear(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            Vector2d u = b - a;
            double t0 = (c - a).Dot(u), t1 = (d - a).Dot(u), l = u.Dot(u);
            return Math.Max(t0, t1) < 0 || Math.Min(t0, t1) > l;
        }

        static TerrainSlopeDesign Pit(DMesh3 terrain, List<Vector3d> toe, double ratio, double tol = 0.05)
        {
            return new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(toe, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(ratio)) },
                Terrain = terrain, Tolerance = tol, BuildComposite = true
            };
        }

        // ---- cases ----

        [Fact]
        public void Pit_FlatTerrain_ClosedCatchLineAtHOverS()
        {
            // square pit floor 20 x 20 at z = 0, terrain flat at z = 10, slope 1:2 (s = 0.5): the catch line is 20 out
            var terrain = Grid(new AxisAlignedBox2d(-60, -60, 60, 60), 24, 24, (x, y) => 10);
            var d = Pit(terrain, TerrainSlopeDesignTests.Square(10, 0), 2);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.True(d.IsComplete);
            Assert.Single(d.CatchLines);
            Assert.True(d.CatchLines[0].Closed);
            AssertCatchLines(d);
            AssertComposite(d);
            // the region: the square 20 x 20 offset by 20 minus the square (rounded corners), within the facets
            double area = XYArea(d.SlopeRegion), exact = 4 * 20 * 20 + Math.PI * 400;
            Assert.InRange(area, exact * 0.995, exact * 1.001);
            // distance from the toe square = h / s, within Tolerance / s (the facets of the cone are below it)
            foreach (Vector3d c in d.CatchLines[0].Points) {
                double dx = Math.Max(Math.Abs(c.x) - 10, 0), dy = Math.Max(Math.Abs(c.y) - 10, 0);
                double dist = Math.Sqrt(dx * dx + dy * dy);
                Assert.InRange(dist, 20 - 1e-6, 20 + d.Tolerance / 0.5 + 1e-6);
            }
            output.WriteLine($"{d.CatchLines[0].Points.Count} catch points, λ = {d.UsedToeLowering:G3}, phases " +
                string.Join(", ", d.PhaseSeconds.Select(kv => $"{kv.Key} {kv.Value:F3}")));
        }

        [Fact]
        public void Fill_FlatTerrain_ClosedCatchLine()
        {
            // a dump: toe at z = 0, terrain at z = -6, fill slope 1:3 down to the terrain: 18 out
            var terrain = Grid(new AxisAlignedBox2d(-50, -50, 50, 50), 20, 20, (x, y) => -6, flip: true);
            var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(3));
            toe.FillStart = toe.CutStart; toe.CutStart = null;
            var d = new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = 0.05, Direction = TerrainSlopeDesign.Directions.Fill, BuildComposite = true };
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Single(d.CatchLines);
            Assert.True(d.CatchLines[0].Closed);
            AssertCatchLines(d, fill: true);
            AssertComposite(d, fill: true);
            foreach (Vector3d c in d.CatchLines[0].Points) {
                double dx = Math.Max(Math.Abs(c.x) - 8, 0), dy = Math.Max(Math.Abs(c.y) - 8, 0);
                Assert.InRange(Math.Sqrt(dx * dx + dy * dy), 18 - 1e-6, 18 + d.Tolerance * 3 + 1e-6);
            }
        }

        [Fact]
        public void OpenToe_TiltedTerrain_EndsAtTheLateralLimits()
        {
            // a straight toe along x at z = 0, slope on the left (+y), terrain rising with y and x
            var terrain = Grid(new AxisAlignedBox2d(-40, -10, 40, 60), 16, 14, (x, y) => 2 + 0.1 * y + 0.02 * x);
            var toe = SlopeToe.Constant(new List<Vector3d> { new Vector3d(-20, 0, 0), new Vector3d(20, 0, 0) }, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(1));
            var d = new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = 0.02, BuildComposite = true };
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Single(d.CatchLines);
            CatchLine l = d.CatchLines[0];
            Assert.False(l.Closed);
            Assert.Equal(CatchLine.EndReasons.LateralLimit, l.StartReason);
            Assert.Equal(CatchLine.EndReasons.LateralLimit, l.EndReason);
            AssertCatchLines(d);
            AssertComposite(d);
            // analytic: S = y (strip, 1:1) meets T: y = 2 + 0.1 y + 0.02 x  ->  y = (2 + 0.02 x) / 0.9 (within Tolerance)
            foreach (Vector3d c in l.Points)
                Assert.InRange(c.y - (2 + 0.02 * c.x) / 0.9, -1e-6, d.Tolerance / 0.9 + 1e-6);
        }

        [Fact]
        public void HillBehindValley_IsNotExcavated()
        {
            // flat at 4 near the pit, a valley down to -10 around r = 30 (the cone passes over it), a hill up to 64 at
            // r = 50, where the terrain is again above the cone: that second component is not connected to the toe
            Func<double, double, double> f = (x, y) => {
                double r = Math.Sqrt(x * x + y * y);
                if (r < 20) return 4;
                if (r < 30) return 4 - 1.4 * (r - 20);
                if (r < 40) return -10 + 1.4 * (r - 30);
                if (r < 50) return 4 + 6 * (r - 40);
                return 64;
            };
            var terrain = Grid(new AxisAlignedBox2d(-70, -70, 70, 70), 56, 56, f);
            var d = Pit(terrain, TerrainSlopeDesignTests.Square(6, 0), 1, 0.05);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            AssertCatchLines(d);
            AssertComposite(d);
            Assert.Single(d.CatchLines);
            Assert.True(d.CatchLines[0].Closed);
            foreach (Vector3d c in d.CatchLines[0].Points) Assert.True(c.xy.Length < 25, $"{c}");
        }

        [Fact]
        public void NoDaylight_KeepsThePartialResult()
        {
            // the terrain ends before the slope reaches it
            var terrain = Grid(new AxisAlignedBox2d(-25, -25, 25, 25), 10, 10, (x, y) => 30);
            var d = Pit(terrain, TerrainSlopeDesignTests.Square(10, 0), 1);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.NoDaylight, d.Status);
            Assert.False(d.IsComplete);
            Assert.Equal(TerrainSlopeDesign.StatusKinds.None, d.StatusKind);    // partial, not a failure
            Assert.NotNull(d.SlopeSurface);
            Assert.Empty(d.CatchLines);       // the region covers the footprint, no line

            // a terrain that is high on one side only: the line ends at the footprint boundary there
            var terrain2 = Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 16, 16, (x, y) => x > 0 ? 3 + 2 * x : 3);
            var d2 = Pit(terrain2, TerrainSlopeDesignTests.Square(10, 0), 1);
            Assert.False(d2.Compute());
            Assert.True(d2.Status == TerrainSlopeDesign.Statuses.NoDaylight, d2.Status + " " + d2.Message);
            AssertCatchLines(d2);
            Assert.Contains(d2.CatchLines, l => l.StartReason == CatchLine.EndReasons.FootprintBoundary);
        }

        [Fact]
        public void TwoPits_WallsMeet_OneCatchLine()
        {
            // two pits whose slopes meet in a ridge below the terrain: one region, one catch line around both
            var terrain = Grid(new AxisAlignedBox2d(-70, -50, 70, 50), 28, 20, (x, y) => 12 + 0.02 * x);
            var a = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0, new Vector2d(-14, 0)), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var b = SlopeToe.Constant(Ngon(7, 9, 2, new Vector2d(16, 1)), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5));
            var d = new TerrainSlopeDesign() { Toes = { a, b }, Terrain = terrain, Tolerance = 0.03, BuildComposite = true };
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            AssertCatchLines(d);
            AssertComposite(d);
            Assert.Single(d.CatchLines);
            Assert.True(d.CatchLines[0].Closed);
            Assert.Contains(d.SlopeRegion.TriangleIndices(), t => d.Pieces[d.SlopeRegion.GetTriangleGroup(t)].Toe == 0);
            Assert.Contains(d.SlopeRegion.TriangleIndices(), t => d.Pieces[d.SlopeRegion.GetTriangleGroup(t)].Toe == 1);
        }

        [Fact]
        public void TerrainWithAHole_NoDaylightThere()
        {
            // a hole in the terrain across the catch line (at 18 from the centre): the region reaches the terrain's
            // boundary there, so the result is partial; the catch line ends at the hole on both sides, and the
            // composite keeps the hole
            var terrain = Grid(new AxisAlignedBox2d(-60, -60, 60, 60), 24, 24, (x, y) => 10);
            foreach (int t in terrain.TriangleIndices().ToList()) {
                Vector3d c = terrain.GetTriCentroid(t);
                if (Math.Abs(c.x - 17) < 4 && Math.Abs(c.y) < 4) terrain.RemoveTriangle(t, true, false);
            }
            var d = Pit(terrain, TerrainSlopeDesignTests.Square(8, 0), 1);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.NoDaylight, d.Status);
            Assert.False(d.IsComplete);
            AssertCatchLines(d);
            AssertComposite(d);
            Assert.Single(d.CatchLines);
            Assert.False(d.CatchLines[0].Closed);
            Assert.Equal(CatchLine.EndReasons.FootprintBoundary, d.CatchLines[0].StartReason);
            Assert.Equal(CatchLine.EndReasons.FootprintBoundary, d.CatchLines[0].EndReason);
        }

        [Fact]
        public void GivenHeightLimit_TheRegionEndsThere()
        {
            // a terrain rising with x, the slope built only up to 8: on the high side the slope does not daylight below
            // the limit, so the line around the low side ends there (HeightLimit) at both ends, and the result is partial
            var terrain = Grid(new AxisAlignedBox2d(-50, -50, 50, 50), 20, 20, (x, y) => 4 + 0.5 * x);
            var d = Pit(terrain, TerrainSlopeDesignTests.Square(8, 0), 1);
            d.HeightLimit = 8;
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.NoDaylight, d.Status);
            Assert.False(d.IsComplete);
            AssertCatchLines(d);
            AssertComposite(d);
            Assert.NotEmpty(d.CatchLines);
            Assert.All(d.CatchLines, l => Assert.False(l.Closed));
            Assert.Single(d.CatchLines);
            Assert.Equal(CatchLine.EndReasons.HeightLimit, d.CatchLines[0].StartReason);
            Assert.Equal(CatchLine.EndReasons.HeightLimit, d.CatchLines[0].EndReason);
            // the ends lie on the level line of the limit
            Assert.Equal(8, d.CatchLines[0].Points[0].z, 6);
            Assert.Equal(8, d.CatchLines[0].Points.Last().z, 6);
        }

        [Fact]
        public void LocalHeightLimit_IsRaised_AndMatchesTheGlobalLimit()
        {
            // a terrain rising with x: near the toe (its box) it is at most 8 m high, but the slope meets it on the high
            // side at about 16 m, so the local cap (8.05) is reached and raised. The result is that of the global limit
            // (the highest terrain + Tolerance) up to Tolerance: other facets and λ (they depend on the cap), the same
            // region; each catch point within 2 Tolerance / |grad D| (|grad D| >= 0.5 here) of the other line in XY
            var terrain = Grid(new AxisAlignedBox2d(-50, -50, 50, 50), 20, 20, (x, y) => 4 + 0.5 * x);
            var local = Pit(terrain, TerrainSlopeDesignTests.Square(8, 0), 1);
            local.CapEstimateRounds = 0;     // (the raise; the start estimate avoids it, below)
            Assert.True(local.Compute(), local.Message);
            Assert.True(local.CapRounds >= 2, $"cap rounds {local.CapRounds}: the local cap was not raised");
            // (the start estimate: one build; a vertex-only maximum missed the terrain triangles that reach into the toe
            // box from outside, 5 m grid, and needed the raise)
            var estimated = Pit(terrain, TerrainSlopeDesignTests.Square(8, 0), 1);
            Assert.True(estimated.Compute(), estimated.Message);
            Assert.Equal(1, estimated.CapRounds);
            Assert.Equal(1, estimated.Attempts);
            AssertCatchLines(estimated);
            AssertComposite(estimated);
            var global = Pit(terrain, TerrainSlopeDesignTests.Square(8, 0), 1);
            global.HeightLimit = 29 + global.Tolerance;
            Assert.True(global.Compute(), global.Message);
            Assert.Single(local.CatchLines);
            Assert.Single(global.CatchLines);
            Assert.True(local.CatchLines[0].Closed && global.CatchLines[0].Closed);
            AssertCatchLines(local);
            AssertComposite(local);
            double bound = 2 * local.Tolerance / 0.5;
            foreach (var pair in new[] { new[] { local, global }, new[] { global, local }, new[] { estimated, global }, new[] { global, estimated } }) {
                var line = pair[1].CatchLines[0].Points;
                foreach (Vector3d p in pair[0].CatchLines[0].Points) {
                    double best = double.MaxValue;
                    for (int k = 0; k < line.Count; ++k) {
                        var seg = new Segment2d(line[k].xy, line[(k + 1) % line.Count].xy);
                        best = Math.Min(best, seg.DistanceSquared(p.xy));
                    }
                    Assert.True(Math.Sqrt(best) <= bound, $"catch point {p} is {Math.Sqrt(best):R} from the other line");
                }
            }
        }

        [Fact]
        public void Reuse_AfterACancelledLocalLimitRun()
        {
            // a run with the local height limit, cancelled at any point, then the same instance with a given limit: the
            // result is that of a new instance (a cancel inside the local limit loop left 'the cap may grow' set, and the
            // second run then built no composite)
            var terrain = Grid(new AxisAlignedBox2d(-50, -50, 50, 50), 20, 20, (x, y) => 4 + 0.5 * x);
            var fresh = Pit(terrain, TerrainSlopeDesignTests.Square(8, 0), 1);
            fresh.HeightLimit = 8;
            fresh.Compute();
            Assert.NotNull(fresh.Composite);
            int cancelled = 0;
            for (int k = 1; k <= 60; ++k) {
                var d = Pit(terrain, TerrainSlopeDesignTests.Square(8, 0), 1);
                int calls = 0;
                d.CancelF = () => ++calls >= k;
                d.Compute();
                if (d.Status != TerrainSlopeDesign.Statuses.Cancelled) break;
                cancelled++;
                d.CancelF = () => false;
                d.HeightLimit = 8;
                d.Compute();
                Assert.Equal(fresh.Status, d.Status);
                Assert.NotNull(d.Composite);
                Assert.Equal(fresh.Composite.TriangleCount, d.Composite.TriangleCount);
                Assert.Equal(fresh.CatchLines.Count, d.CatchLines.Count);
                for (int i = 0; i < fresh.CatchLines.Count; ++i)
                    Assert.Equal(fresh.CatchLines[i].Points, d.CatchLines[i].Points);
            }
            Assert.True(cancelled > 0);
        }

        [Theory]
        [InlineData(3, 0.8)]
        [InlineData(12, 0.8)]
        [InlineData(20, 0.55)]
        [InlineData(7, 0.95)]
        public void CatchPointMerge_NoChains(int n, double spacing)
        {
            // points on a line, spaced below the merge radius: each may join a representative, but none may move by the
            // radius or more (a union-find closure moved the first point to the last: 1.67 radius for 3 points at 0.8, the
            // spacing times 1.044 along the line)
            double radius = 0.01;
            var keys = new List<long>();
            var at = new Dictionary<long, Vector3d>();
            for (int i = 0; i < n; ++i) {
                long k = SlopeTerrainOverlay.ekey(1, 100 + i, 7);
                keys.Add(k);
                at[k] = new Vector3d(5.6e6 + i * spacing * radius, 3.5e5 + 0.3 * i * spacing * radius, 100);
            }
            var rep = SlopeComposite.MergeNear(keys, at, k => 2, (a, b) => true, new HashSet<long>(), radius);
            int merged = 0;
            foreach (long k in keys) {
                double move = at[k].xy.Distance(at[rep[k]].xy);
                Assert.True(move < radius, $"point {keys.IndexOf(k)} moves by {move / radius:F3} radius");
                if (rep[k] != k) merged++;
            }
            Assert.True(merged > 0);
            // fixed points neither move nor take others
            var fixedPts = new HashSet<long>(keys);
            rep = SlopeComposite.MergeNear(keys, at, k => 2, (a, b) => true, fixedPts, radius);
            Assert.All(keys, k => Assert.Equal(k, rep[k]));
        }

        [Fact]
        public void Overlay_IsReusable()
        {
            // the overlay and the composite classes give the same result when run again on the same instance
            var terrain = Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 16, 16, (x, y) => 6 + 0.1 * x);
            var d0 = TerrainSlopeDesignTests.Design(9, 0.02, SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)));
            Assert.True(d0.Compute());
            var kinds = new Dictionary<int, int>();
            foreach (int e in d0.SlopeSurface.BoundaryEdgeIndices()) {
                Vector2d m = 0.5 * (d0.SlopeSurface.GetVertex(d0.SlopeSurface.GetEdgeV(e).a).xy + d0.SlopeSurface.GetVertex(d0.SlopeSurface.GetEdgeV(e).b).xy);
                kinds[e] = Math.Max(Math.Abs(m.x), Math.Abs(m.y)) <= 8 + 1e-9 ? SlopeTerrainOverlay.KindToe : SlopeTerrainOverlay.KindOther;
            }
            var o = new SlopeTerrainOverlay(d0.SlopeSurface, terrain, false, kinds, null);
            Assert.True(o.Compute(), o.Problem);
            var first = o.CatchLines.Select(l => l.Points.ToList()).ToList();
            int faces = o.Faces.Count;
            Assert.True(o.Compute(), o.Problem);
            Assert.Equal(faces, o.Faces.Count);
            Assert.Equal(first, o.CatchLines.Select(l => l.Points.ToList()).ToList());
            var c = new SlopeComposite(o, d0.Pieces.Count, 1e-9, null);
            Assert.True(c.Build(), c.Problem);
            DMesh3 comp = c.Composite;
            Assert.True(c.Build(), c.Problem);
            Assert.Equal("", Invariance.Difference(comp, c.Composite));
        }

        [Fact]
        public void TerrainNotAHeightField_IsRejected()
        {
            var terrain = Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 4, 4, (x, y) => 5);
            // a second copy of one triangle, shifted: overlaps in XY
            int a = terrain.AppendVertex(new Vector3d(-35, -35, 5)), b = terrain.AppendVertex(new Vector3d(-15, -35, 5)), c = terrain.AppendVertex(new Vector3d(-15, -15, 5));
            terrain.AppendTriangle(a, b, c);
            var d = Pit(terrain, TerrainSlopeDesignTests.Square(10, 0), 1);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.NotHeightField, d.Status);
            Assert.Equal(TerrainSlopeDesign.StatusKinds.InvalidInput, d.StatusKind);
        }

        [Fact]
        public void TerrainOverlap_FarFromTheSlope_IsTheTerrainAsGiven()
        {
            // the same defect far outside the slope's box (the slope reaches z 5 within 5 m of the toe): the terrain there
            // is not this operation's concern. The design succeeds, and the composite copies the terrain as given there:
            // its only overlaps are those of the input (before: CompositeFailed, from an overlap check over all of it)
            var terrain = Grid(new AxisAlignedBox2d(-200, -200, 200, 200), 20, 20, (x, y) => 5);
            int a = terrain.AppendVertex(new Vector3d(150, 150, 5)), b = terrain.AppendVertex(new Vector3d(170, 150, 5)), c = terrain.AppendVertex(new Vector3d(170, 170, 5));
            terrain.AppendTriangle(a, b, c);
            var d = Pit(terrain, TerrainSlopeDesignTests.Square(10, 0), 1);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.NotNull(d.Composite);
            var pairs = TerrainVolumeDifference.XYOverlapPairs(d.Composite);
            Assert.NotEmpty(pairs);
            foreach (Index2i pr in pairs)
                foreach (int t in new[] { pr.a, pr.b }) {
                    Index3i tv = d.Composite.GetTriangle(t);
                    foreach (int v in new[] { tv.a, tv.b, tv.c }) Assert.True(d.Composite.GetVertex(v).x >= 140 && d.Composite.GetVertex(v).y >= 140);
                }
            Assert.Equal(TerrainVolumeDifference.XYOverlapPairs(terrain).Count, pairs.Count);
        }

        [Fact]
        public void CompositeOverlapCheck_ConstructedAgainstCopied()
        {
            // the composite's check: a made triangle over a copied terrain triangle is found; two copied ones that
            // overlap far away are the terrain as given and not looked for
            Func<double, double, Vector3d> v = (x, y) => new Vector3d(x, y, 0);
            var made = new List<Vector3d[]> { new[] { v(2, 2), v(8, 2), v(2, 8) } };
            var others = new List<Vector3d[]> {
                new[] { v(0, 0), v(10, 0), v(0, 10) },
                new[] { v(100, 100), v(110, 100), v(100, 110) }, new[] { v(101, 101), v(109, 101), v(101, 109) } };
            DMesh3 near;
            var pairs = SlopeComposite.OverlapsOfMade(made, box => SlopeComposite.Within(others, box), out near);
            Assert.Equal(2, near.TriangleCount);
            Assert.Single(pairs);
            var m = new DMesh3(false, false, false, false);
            foreach (var t in made.Concat(others)) m.AppendTriangle(m.AppendVertex(t[0]), m.AppendVertex(t[1]), m.AppendVertex(t[2]));
            Assert.Equal(2, TerrainVolumeDifference.XYOverlapPairs(m).Count);
        }

        [Fact]
        public void ToeOnTerrainVertices_GridAligned_Consistent()
        {
            // the degenerate cases SoS decides: terrain vertices exactly on the toe vertices and edges, and on a grid
            // aligned with the toe; a terrain that crosses the toe height (cut on one side only)
            foreach (bool flip in new[] { false, true }) {
                var terrain = Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 16, 16, (x, y) => 0.25 * x, flip);
                var d = Pit(terrain, TerrainSlopeDesignTests.Square(10, 0), 1, 0.02);
                Assert.True(d.Compute(), d.Status + " " + d.Message);
                AssertCatchLines(d);
                AssertComposite(d);
                Assert.Single(d.CatchLines);
                Assert.False(d.CatchLines[0].Closed);
                Assert.Equal(CatchLine.EndReasons.Toe, d.CatchLines[0].StartReason);
                Assert.Equal(CatchLine.EndReasons.Toe, d.CatchLines[0].EndReason);
            }
        }

        [Fact]
        public void TerrainEqualToTheSlope_NoWork()
        {
            // the terrain is the slope surface itself: D = 0 everywhere counts as no work, so there is no region,
            // except where the toe lowering λ puts the slope below (along the toe, a strip of zero width in the limit)
            var d0 = TerrainSlopeDesignTests.Design(12, 0.05, SlopeToe.Constant(TerrainSlopeDesignTests.Square(10, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)));
            Assert.True(d0.Compute());
            var d = Pit(d0.SlopeSurface, TerrainSlopeDesignTests.Square(10, 0), 1);
            d.HeightLimit = 12;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Empty(d.CatchLines);
            AssertComposite(d);
        }

        internal static List<Vector3d> Ngon(int n, double r, double z, Vector2d c, double phase = 0.1) =>
            Enumerable.Range(0, n).Select(i => new Vector3d(c.x + r * Math.Cos(phase + 2 * Math.PI * i / n), c.y + r * Math.Sin(phase + 2 * Math.PI * i / n), z)).ToList();

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void RandomTerrains_CatchLinesAndComposite(int seed, bool utm)
        {
            int ok = 0;
            for (int it = 0; it < 6; ++it) {
                var d = RandomTerrainCase(seed, utm, it);
                bool fill = d.Direction == TerrainSlopeDesign.Directions.Fill;
                bool res = d.Compute();
                output.WriteLine($"{it}: fill {fill} {d.Status} {d.Message} lines {d.CatchLines.Count} attempts {d.Attempts} δ {d.UsedSlopeMargin:G2} λ {d.UsedToeLowering:G2}");
                Assert.True(res || d.Status == TerrainSlopeDesign.Statuses.NoDaylight, d.Status + " " + d.Message);
                if (res) ok++;
                AssertCatchLines(d, fill);
                AssertComposite(d, fill);
            }
            Assert.True(ok > 0);
        }

        // case 'it' of the random wavy terrains of seed 'seed' (the draws of the cases before it are made first)
        internal static TerrainSlopeDesign RandomTerrainCase(int seed, bool utm, int it)
        {
            var rnd = new Random(seed);
            TerrainSlopeDesign d = null;
            for (int k = 0; k <= it; ++k) {
                bool fill = rnd.Next(3) == 0;
                Vector2d c = utm ? new Vector2d(5.6e6 + rnd.Next(1000), 3.5e5 + rnd.Next(1000)) : Vector2d.Zero;
                double zc = utm ? 120 : 0;
                double amp = 1 + 4 * rnd.NextDouble(), kk = 0.05 + 0.1 * rnd.NextDouble(), h = 6 + 6 * rnd.NextDouble();
                double jitter = rnd.NextDouble() < 0.5 ? 0 : 0.3;
                var jr = new Random(seed * 100 + k);
                Func<double, double, double> f = (x, y) => zc + (fill ? -1 : 1) * (h + amp * Math.Sin(kk * (x - c.x)) * Math.Cos(0.7 * kk * (y - c.y))) + jitter * (jr.NextDouble() - 0.5);
                var terrain = Grid(new AxisAlignedBox2d(c.x - 80, c.y - 80, c.x + 80, c.y + 80), 20 + rnd.Next(20), 20 + rnd.Next(20), f, rnd.Next(2) == 0);
                var toe = SlopeToe.Constant(Ngon(5 + rnd.Next(6), 8 + 6 * rnd.NextDouble(), zc, c, rnd.NextDouble()), true, SlopeToe.Sides.Outside,
                    SlopeSpec.Ratio(1 + 2 * rnd.NextDouble()));
                if (fill) { toe.FillStart = toe.CutStart; toe.CutStart = null; }
                d = new TerrainSlopeDesign() {
                    Toes = { toe }, Terrain = terrain, Tolerance = 0.05, BuildComposite = true,
                    Direction = fill ? TerrainSlopeDesign.Directions.Fill : TerrainSlopeDesign.Directions.Cut
                };
            }
            return d;
        }

        // ---- the exact signs against rationals ----

        static BigRational R(double x) => BigRational.Of(x);

        // D at the crossing of A edge (a0, a1) and B edge (b0, b1), exactly: z_a - z_b
        static int ExactSignX(Vector3d a0, Vector3d a1, Vector3d b0, Vector3d b1)
        {
            BigRational ax = R(a1.x) - R(a0.x), ay = R(a1.y) - R(a0.y), bx = R(b1.x) - R(b0.x), by = R(b1.y) - R(b0.y);
            BigRational dx = R(b0.x) - R(a0.x), dy = R(b0.y) - R(a0.y);
            BigRational den = ax * by - ay * bx;
            BigRational ta = (dx * by - dy * bx) / den, tb = (dx * ay - dy * ax) / den;
            BigRational za = R(a0.z) + ta * (R(a1.z) - R(a0.z)), zb = R(b0.z) + tb * (R(b1.z) - R(b0.z));
            return (za - zb).Sign;
        }

        static int ExactSignPlane(Vector3d t0, Vector3d t1, Vector3d t2, Vector3d v)
        {
            // v.z - plane(v.xy)
            BigRational ux = R(t1.x) - R(t0.x), uy = R(t1.y) - R(t0.y), uz = R(t1.z) - R(t0.z);
            BigRational wx = R(t2.x) - R(t0.x), wy = R(t2.y) - R(t0.y), wz = R(t2.z) - R(t0.z);
            BigRational nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
            BigRational z = R(t0.z) - (nx * (R(v.x) - R(t0.x)) + ny * (R(v.y) - R(t0.y))) / nz;
            return (R(v.z) - z).Sign;
        }

        [Fact]
        public void Signs_AgreeWithRationals()
        {
            var rnd = new Random(7);
            int checkedX = 0, zeros = 0;
            for (int it = 0; it < 4000; ++it) {
                // small integer coordinates make exact zeros (coplanar, through vertices) frequent; UTM offsets for the rest
                double off = (it % 2 == 0) ? 0 : 5.6e6 + 0.5;
                Func<Vector3d> pt = () => new Vector3d(off + rnd.Next(-4, 5), off + rnd.Next(-4, 5), rnd.Next(-3, 4) * 0.5);
                Vector3d a0 = pt(), a1 = pt(), b0 = pt(), b1 = pt(), t2 = pt();
                if (a0.xy == a1.xy || b0.xy == b1.xy) continue;
                foreach (int dir in new[] { 1, -1 }) {
                    // crossing (proper, so the formula applies without SoS)
                    int o0 = ExactPredicates.Orient2d(a0.xy, a1.xy, b0.xy), o1 = ExactPredicates.Orient2d(a0.xy, a1.xy, b1.xy);
                    int o2 = ExactPredicates.Orient2d(b0.xy, b1.xy, a0.xy), o3 = ExactPredicates.Orient2d(b0.xy, b1.xy, a1.xy);
                    if (o0 * o1 < 0 && o2 * o3 < 0) {
                        int e = ExactSignX(a0, a1, b0, b1) * dir;
                        if (e == 0) zeros++;
                        Assert.Equal(e >= 0 ? 1 : -1, SlopeTerrainOverlay.SignX(a0, a1, b0, b1, dir));
                        checkedX++;
                    }
                    // vertex over triangle
                    int ot = ExactPredicates.Orient2d(b0.xy, b1.xy, t2.xy);
                    if (ot != 0) {
                        Vector3d q0 = b0, q1 = ot > 0 ? b1 : t2, q2 = ot > 0 ? t2 : b1;
                        int e = ExactSignPlane(q0, q1, q2, a0);
                        Assert.Equal(dir * e >= 0 ? 1 : -1, SlopeTerrainOverlay.SignAV(a0, q0, q1, q2, dir));
                        Assert.Equal(-dir * e >= 0 ? 1 : -1, SlopeTerrainOverlay.SignBV(a0, q0, q1, q2, dir));
                    }
                }
            }
            output.WriteLine($"{checkedX} crossings, {zeros} with D = 0");
            Assert.True(checkedX > 500 && zeros > 10);
        }

        [Fact]
        public void SoS_Orientations_AreConsistent()
        {
            // the perturbed orientation equals the orientation with B moved by a tiny (e, e^2) (here e = 2^-20 on
            // integer points, where the shift cannot meet another zero)
            var rnd = new Random(3);
            double e = Math.Pow(2, -20);
            for (int it = 0; it < 20000; ++it) {
                Vector2d a0 = new Vector2d(rnd.Next(-3, 4), rnd.Next(-3, 4)), a1 = new Vector2d(rnd.Next(-3, 4), rnd.Next(-3, 4));
                Vector2d b = new Vector2d(rnd.Next(-3, 4), rnd.Next(-3, 4));
                if (a0 == a1) continue;
                Vector2d bs = b + new Vector2d(e, e * e);
                Assert.Equal(ExactPredicates.Orient2d(a0, a1, bs), SlopeTerrainOverlay.OrientAAB(a0, a1, b));
                Assert.Equal(ExactPredicates.Orient2d(a0 + new Vector2d(e, e * e), a1 + new Vector2d(e, e * e), b),
                             SlopeTerrainOverlay.OrientBBA(a0, a1, b));
            }
        }
    }

    /// <summary> exact rationals for the test references </summary>
    internal readonly struct BigRational
    {
        public readonly BigInteger N, D;     // D > 0
        public BigRational(BigInteger n, BigInteger d)
        {
            if (d.Sign < 0) { n = -n; d = -d; }
            BigInteger g = BigInteger.GreatestCommonDivisor(n, d);
            if (g > 1) { n /= g; d /= g; }
            N = n; D = d;
        }
        public static BigRational Of(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            bool neg = bits < 0;
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp++; else man |= 1L << 52;
            exp -= 1075;
            BigInteger n = man, d = BigInteger.One;
            if (exp > 0) n <<= exp; else d <<= -exp;
            return new BigRational(neg ? -n : n, d);
        }
        public int Sign => N.Sign;
        public static BigRational operator +(BigRational a, BigRational b) => new BigRational(a.N * b.D + b.N * a.D, a.D * b.D);
        public static BigRational operator -(BigRational a, BigRational b) => new BigRational(a.N * b.D - b.N * a.D, a.D * b.D);
        public static BigRational operator *(BigRational a, BigRational b) => new BigRational(a.N * b.N, a.D * b.D);
        public static BigRational operator /(BigRational a, BigRational b) => new BigRational(a.N * b.D, a.D * b.N);
    }
}
