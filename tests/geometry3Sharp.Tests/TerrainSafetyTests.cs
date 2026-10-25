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
            foreach (Vector3d p in line)
                Assert.True(Math.Abs(p.Distance(src) - 10) < tolerance, $"{p}: {p.Distance(src)}");
            // along the surface the iso line of the heat distance wiggles a little
            Assert.InRange(Length(line), 0.98 * 2 * Math.PI * 10, (mode == TerrainSafetyLines.Modes.Horizontal ? 1.01 : 1.05) * 2 * Math.PI * 10);
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
        public void SafetyLine_InvalidDistance_Fails(TerrainSafetyLines.Modes mode, double d)
        {
            var safety = new TerrainSafetyLines(CoarseSquare(), new[] { new List<Vector3d>() { new Vector3d(50, 50, 0) } }, d) { Mode = mode };
            Assert.False(safety.Compute());
        }

        static Vector2d UV(Vector3d p)
        {
            double c = Math.Cos(Angle * MathUtil.Deg2Rad), s = Math.Sin(Angle * MathUtil.Deg2Rad);
            return new Vector2d(c * p.x + s * p.y, -s * p.x + c * p.y);
        }
    }
}
