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

        static Vector2d UV(Vector3d p)
        {
            double c = Math.Cos(Angle * MathUtil.Deg2Rad), s = Math.Sin(Angle * MathUtil.Deg2Rad);
            return new Vector2d(c * p.x + s * p.y, -s * p.x + c * p.y);
        }
    }
}
