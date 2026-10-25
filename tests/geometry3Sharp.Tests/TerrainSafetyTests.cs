using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Break lines (crests and toes of the benches) and safety lines at a distance from them along the
    /// terrain, on an open pit built with TerrainDualContouring (break lines on mesh edges).
    /// </summary>
    public class TerrainSafetyTests
    {
        const double Angle = 17;

        // pit as in the other terrain tests; r = distance out of the bottom rectangle (30 x 15 half sizes)
        static double R(double x, double y)
        {
            double c = Math.Cos(Angle * MathUtil.Deg2Rad), s = Math.Sin(Angle * MathUtil.Deg2Rad);
            double u = c * x + s * y, v = -s * x + c * y;
            return Math.Max(Math.Abs(u) - 30, Math.Abs(v) - 15);
        }
        static double Pit(double x, double y)
        {
            double r = R(x, y), z = -40;
            for (int bench = 0; bench < 4; ++bench) {
                if (r <= 0) return z;
                z += 10 * Math.Min(r, 8) / 8;
                r -= 8;
                if (r <= 0) return z;
                r -= 6;
            }
            return z;
        }

        static DMesh3 PitTerrain()
        {
            int n = 401;
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[n, n];
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double x = -100 + 200.0 * i / (n - 1), y = -100 + 200.0 * j / (n - 1);
                    vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, Pit(x, y)));
                }
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            var tdc = new TerrainDualContouring(mesh) { GridResolution = 200 };
            Assert.True(tdc.Compute());
            return tdc.Result;
        }

        static double Length(List<Vector3d> l) { double s = 0; for (int i = 1; i < l.Count; ++i) s += l[i].Distance(l[i - 1]); return s; }

        static double RectanglePerimeter(double r) { return 2 * (2 * (30 + r) + 2 * (15 + r)); }

        [Fact]
        public void BreakLines_CrestsAndToesOfTheBenches()
        {
            DMesh3 terrain = PitTerrain();
            var bl = new TerrainBreakLines(terrain);
            Assert.True(bl.Compute());
            // crests at r = 8, 22, 36, 50; toes at r = 0, 14, 28, 42 (plus the concave corner diagonals)
            double[] crests = { 8, 22, 36, 50 }, toes = { 0, 14, 28, 42 };
            foreach (var line in bl.Crests)
                foreach (Vector3d p in line)
                    // within a cell (1 m) of the true crest: where a crest only clips a cell, its vertex lies
                    // on one plane and the crest runs through a triangle
                    Assert.True(crests.Min(c => Math.Abs(R(p.x, p.y) - c)) < 1.0, $"crest point {p} at r = {R(p.x, p.y)}");
            double crestLength = bl.Crests.Sum(Length);
            double expected = crests.Sum(c => RectanglePerimeter(c));
            Assert.InRange(crestLength, 0.8 * expected, 1.1 * expected);
            Assert.True(bl.Toes.Sum(Length) > 0.8 * toes.Sum(c => RectanglePerimeter(c)));
        }

        [Fact]
        public void BreakLines_VerticalRiser_CrestAndToe()
        {
            // a 1 m step with a vertical riser (duplicate xy, as surveyed walls are): x = 1 at z = 0 and 1
            DMesh3 m = new DMesh3();
            double[] xs = { 0, 1, 1, 2 }, zs = { 0, 0, 1, 1 };
            int ny = 11;
            int[,] vid = new int[4, ny];
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < 4; ++i) vid[i, j] = m.AppendVertex(new Vector3d(xs[i], j, zs[i]));
            for (int j = 0; j + 1 < ny; ++j)
                for (int i = 0; i < 3; ++i) {
                    m.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    m.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            Assert.True(m.GetTriNormal(0).z > 0, "test mesh faces up");
            var bl = new TerrainBreakLines(m) { MinLineLength = 2 };
            Assert.True(bl.Compute());
            Assert.Single(bl.Crests);
            Assert.Single(bl.Toes);
            Assert.All(bl.Crests[0], p => Assert.True(p.x == 1 && p.z == 1, $"crest {p}"));
            Assert.All(bl.Toes[0], p => Assert.True(p.x == 1 && p.z == 0, $"toe {p}"));
            Assert.Equal(10, Length(bl.Crests[0]), 9);
            // the same mesh facing down (reversed orientation) gives the same lines
            m.ReverseOrientation();
            var bl2 = new TerrainBreakLines(m) { MinLineLength = 2 };
            Assert.True(bl2.Compute());
            Assert.All(bl2.Crests.SelectMany(l => l), p => Assert.True(p.z == 1));
            Assert.All(bl2.Toes.SelectMany(l => l), p => Assert.True(p.z == 0));
        }

        // the top crest: the rectangle r = 50 (u = +-80, v = +-65 in the rotated frame)
        static List<Vector3d> TopCrest()
        {
            double c = Math.Cos(Angle * MathUtil.Deg2Rad), s = Math.Sin(Angle * MathUtil.Deg2Rad);
            Func<double, double, Vector3d> P = (u, v) => new Vector3d(c * u - s * v, s * u + c * v, 0);
            var l = new List<Vector3d>() { P(-80, -65), P(80, -65), P(80, 65), P(-80, 65), P(-80, -65) };
            return l;
        }

        // horizontal: exact (bisection on the terrain edges); along the surface: on flat ground the same,
        // within about 2% plus half an edge (edges 1 m: 0.6 m, plus margin). Sources: the surveyed (exact)
        // crest polyline; detected break lines can have gaps and are not meant as sources for safety lines
        [Theory]
        [InlineData(TerrainSafetyLines.Modes.Horizontal, 0.01)]
        [InlineData(TerrainSafetyLines.Modes.AlongSurface, 0.7)]
        public void SafetyLine_FiveMetresBehindTheTopCrest(TerrainSafetyLines.Modes mode, double tolerance)
        {
            DMesh3 terrain = PitTerrain();
            var safety = new TerrainSafetyLines(terrain, new[] { TopCrest() }, 5.0) { Mode = mode };
            Assert.True(safety.Compute());
            Assert.NotEmpty(safety.Lines);
            // the line on the ground outside the pit (r > 50): 5 m from the crest rectangle
            int checkedPoints = 0;
            foreach (var line in safety.Lines)
                foreach (Vector3d p in line) {
                    if (R(p.x, p.y) < 50) continue;
                    double d = R(p.x, p.y) - 50;    // Chebyshev-like: exact away from the corners
                    if (Math.Abs(Math.Abs(UV(p).x) - 30 - 50) < 6 && Math.Abs(Math.Abs(UV(p).y) - 15 - 50) < 6) continue;   // round corners
                    Assert.True(Math.Abs(d - 5) < tolerance, $"{p}: {d} m from the crest");
                    checkedPoints++;
                }
            Assert.True(checkedPoints > 50, $"{checkedPoints} points checked");
        }

        // coarse survey TIN: a 100 x 100 square of two triangles, no vertex near the source
        static DMesh3 CoarseSquare()
        {
            DMesh3 m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(100, 0, 0));
            int c = m.AppendVertex(new Vector3d(100, 100, 0)), d = m.AppendVertex(new Vector3d(0, 100, 0));
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            return m;
        }

        [Theory]
        [InlineData(TerrainSafetyLines.Modes.Horizontal, 0.02)]
        [InlineData(TerrainSafetyLines.Modes.AlongSurface, 1.2)]
        public void SafetyLine_CoarseTin_CircleAroundAPoint(TerrainSafetyLines.Modes mode, double tolerance)
        {
            Vector3d src = new Vector3d(50, 50, 0);
            var safety = new TerrainSafetyLines(CoarseSquare(), new[] { new List<Vector3d>() { src } }, 10) { Mode = mode };
            Assert.True(safety.Compute());
            Assert.Single(safety.Lines);
            var line = safety.Lines[0];
            Assert.True(line[0].Distance(line[line.Count - 1]) < 1e-9, "not closed");
            // along the surface the line is drawn with a safety margin (outwards), the heat distance error
            // is within the tolerance of that
            double offset = (mode == TerrainSafetyLines.Modes.Horizontal) ? 0 : 0.35 * 10.0 / 8;
            foreach (Vector3d p in line)
                Assert.True(Math.Abs(p.Distance(src) - 10 - offset) < tolerance, $"{p}: {p.Distance(src)}");
            // along the surface the iso line of the heat distance wiggles a little
            double r = 10 + offset;
            Assert.InRange(Length(line), 0.98 * 2 * Math.PI * r, (mode == TerrainSafetyLines.Modes.Horizontal ? 1.01 : 1.05) * 2 * Math.PI * (r + 1));
        }

        [Fact]
        public void SafetyLine_Horizontal_NeverInsideTheDistanceByMoreThanTheTolerance()
        {
            // a crest with a convex corner: the line around the corner is an arc, approximated by chords
            var crest = new List<Vector3d>() { new Vector3d(20, 20, 0), new Vector3d(60, 20, 0), new Vector3d(60, 70, 0) };
            var safety = new TerrainSafetyLines(CoarseSquare(), new[] { crest }, 8) { Tolerance = 0.005 };
            Assert.True(safety.Compute());
            var segs = new List<Segment2d>() { new Segment2d(crest[0].xy, crest[1].xy), new Segment2d(crest[1].xy, crest[2].xy) };
            Func<Vector2d, double> dist = (q) => segs.Min(s => Math.Sqrt(s.DistanceSquared(q)));
            int n = 0;
            foreach (var line in safety.Lines)
                for (int i = 0; i + 1 < line.Count; ++i)
                    for (double t = 0; t <= 1; t += 0.25) {
                        Vector2d q = Vector2d.Lerp(line[i].xy, line[i + 1].xy, t);
                        // inside the arcs at most the tolerance; across the corner on the concave side the chords
                        // lie farther away (on the safe side)
                        Assert.True(dist(q) > 8 - 0.005 - 1e-6 && dist(q) < 8 + 0.5, $"{q}: {dist(q)}");
                        n++;
                    }
            Assert.True(n > 100);
        }

        [Theory]
        [InlineData(TerrainSafetyLines.Modes.Horizontal)]
        [InlineData(TerrainSafetyLines.Modes.AlongSurface)]
        [InlineData(TerrainSafetyLines.Modes.Guaranteed)]
        public void SafetyLine_SourcesOutsideTheTerrain_NoLines(TerrainSafetyLines.Modes mode)
        {
            var outside = new List<Vector3d>() { new Vector3d(-30, 0, 0), new Vector3d(-30, 100, 0) };
            var safety = new TerrainSafetyLines(CoarseSquare(), new[] { outside }, 10) { Mode = mode };
            Assert.True(safety.Compute());
            Assert.Empty(safety.Lines);
        }

        [Theory]
        [InlineData(TerrainSafetyLines.Modes.Horizontal, 0.0)]
        [InlineData(TerrainSafetyLines.Modes.Horizontal, -5.0)]
        [InlineData(TerrainSafetyLines.Modes.AlongSurface, 0.0)]
        [InlineData(TerrainSafetyLines.Modes.AlongSurface, -5.0)]
        [InlineData(TerrainSafetyLines.Modes.Guaranteed, 0.0)]
        public void SafetyLine_InvalidDistance_Fails(TerrainSafetyLines.Modes mode, double d)
        {
            var safety = new TerrainSafetyLines(CoarseSquare(), new[] { new List<Vector3d>() { new Vector3d(50, 50, 0) } }, d) { Mode = mode };
            Assert.False(safety.Compute());
        }

        static double DistanceToPolyline(List<Vector3d> l, Vector3d p)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < l.Count; ++i) best = Math.Min(best, Math.Sqrt(new Segment3d(l[i], l[i + 1]).DistanceSquared(p)));
            return best;
        }

        [Fact]
        public void SafetyLine_Guaranteed_OutsideTheDistanceAndCertified()
        {
            // 12 m from the top crest: outside on flat ground, inside down the bench face and onto the berm
            DMesh3 terrain = PitTerrain();
            var crest = TopCrest();
            var safety = new TerrainSafetyLines(terrain, new[] { crest }, 12) { Mode = TerrainSafetyLines.Modes.Guaranteed };
            Assert.True(safety.Compute());
            Assert.True(safety.Lines.Count >= 2);
            int n = 0;
            for (int li = 0; li < safety.Lines.Count; ++li)
                for (int k = 0; k < safety.Lines[li].Count; ++k) {
                    Vector3d p = safety.Lines[li][k];
                    // the straight distance (a lower bound of the distance along the terrain): at 12 + Tolerance
                    Assert.True(Math.Abs(DistanceToPolyline(crest, p) - 12.01) < 1e-6, $"{p}: {DistanceToPolyline(crest, p)}");
                    // the certified upper bound is consistent with it
                    Assert.True(safety.Excess[li][k] >= 0.01 - 1e-6, $"excess {safety.Excess[li][k]}");
                    n++;
                }
            Assert.True(n > 100);
            // the whole lines (segments, not only their points): outside 12 m, and certified within 0.2 m of it
            Assert.True(safety.MinLineDistance >= 12, $"{safety.MinLineDistance}");
            Assert.True(safety.Certified, $"max excess {safety.MaxExcess}, {safety.UncertifiedSegments} segments");
            // with 3 points per edge the bound is 0.4 m too long on flat ground: certified with more
            Assert.True(safety.UsedSteinerPoints > 3);
        }

        [Fact]
        public void SafetyLine_Guaranteed_GapNotCertified()
        {
            // a 30 m deep trench, 8 m wide at the top: across it the straight distance is short, along the
            // terrain (down and up again) long. The line on the far rim is safe but far too conservative
            DMesh3 m = new DMesh3();
            int nx = 81, ny = 41;
            int[,] vid = new int[nx, ny];
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    double x = i - 40, y = j;
                    vid[i, j] = m.AppendVertex(new Vector3d(x, y, -30 * Math.Max(0, 1 - Math.Abs(x) / 4)));
                }
            for (int j = 0; j + 1 < ny; ++j)
                for (int i = 0; i + 1 < nx; ++i) {
                    m.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    m.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            var rim = new List<Vector3d>() { new Vector3d(-4, 0, 0), new Vector3d(-4, 40, 0) };
            var safety = new TerrainSafetyLines(m, new[] { rim }, 10) { Mode = TerrainSafetyLines.Modes.Guaranteed };
            Assert.True(safety.Compute());
            Assert.False(safety.Certified);
            Assert.True(safety.UncertifiedSegments > 0);
            // the line behind the rim (x = -14) is certified, the one beyond the trench (x = +2) is not
            for (int li = 0; li < safety.Lines.Count; ++li)
                for (int k = 0; k < safety.Lines[li].Count; ++k) {
                    Vector3d p = safety.Lines[li][k];
                    if (p.y < 2 || p.y > 38) continue;
                    if (p.x < -10) Assert.True(safety.Excess[li][k] < 0.2, $"{p}: {safety.Excess[li][k]}");
                    if (p.x > 0) Assert.True(safety.Excess[li][k] > 5, $"{p}: {safety.Excess[li][k]}");
                }
            safety.RequireCertified = true;
            Assert.False(safety.Compute());
        }

        [Fact]
        public void BreakLines_GapClosing_ManyPieces_FastAndClosestFirst()
        {
            // 1200 pieces of one zigzag line, 0.5 m gaps, and a square ring of 4 pieces: the gap closing
            // searched all end pairs again for every join (cubic: 105 s for 1500 lines of real crests)
            var lines = new List<List<Vector3d>>();
            for (int k = 0; k < 1200; ++k) {
                double x = 2.5 * k;
                lines.Add(new List<Vector3d>() { new Vector3d(x, k % 2, 0), new Vector3d(x + 2, k % 2, 0) });
            }
            // shuffled, so the pieces are not in order
            var rnd = new Random(5);
            lines = lines.OrderBy(l => rnd.Next()).ToList();
            var square = new[] { new Vector3d(0, 100, 0), new Vector3d(10, 100, 0), new Vector3d(10, 110, 0), new Vector3d(0, 110, 0) };
            for (int k = 0; k < 4; ++k)
                lines.Add(new List<Vector3d>() { square[k], Vector3d.Lerp(square[k], square[(k + 1) % 4], 0.95) });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            TerrainBreakLines.close_gaps(lines, 1.5);
            Assert.True(sw.Elapsed.TotalSeconds < 2, $"{sw.Elapsed.TotalSeconds} s");
            // the zigzag pieces: each end is 0.5 m (straight) or 1.1 m (diagonal) from the next piece's
            // start: one line; the square: one closed ring
            Assert.Equal(2, lines.Count);
            var zig = lines.Single(l => l[0].y < 50);
            Assert.Equal(2400, zig.Count);
            var ring = lines.Single(l => l[0].y >= 50);
            Assert.True(ring[0].Distance(ring[ring.Count - 1]) < 1e-9);
            Assert.Equal(9, ring.Count);
        }

        static Vector2d UV(Vector3d p)
        {
            double c = Math.Cos(Angle * MathUtil.Deg2Rad), s = Math.Sin(Angle * MathUtil.Deg2Rad);
            return new Vector2d(c * p.x + s * p.y, -s * p.x + c * p.y);
        }
    }
}
