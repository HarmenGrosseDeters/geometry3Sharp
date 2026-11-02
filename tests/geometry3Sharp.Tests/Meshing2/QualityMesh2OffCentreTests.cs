using System;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Off-centres (Ungor 2004) in <see cref="QualityMesh2"/>: a triangle below the angle bound is split on the perpendicular bisector of
    /// its shortest edge pq, where the triangle of pq and the new point has the bound at the point, or at its circumcentre if that is
    /// closer. The meshes themselves run through the oracle in the catalogue, the fuzz and the similarity tests.
    /// </summary>
    public class QualityMesh2OffCentreTests
    {
        static double HalfCotOf(double degrees) { double s = Math.Sin(degrees * Math.PI / 180); return QualityMesh2.Refiner.HalfCot(s * s); }

        static double AngleAt(Vector2d p, Vector2d a, Vector2d b)
        {
            var u = a - p; var w = b - p;
            return Math.Atan2(Math.Abs(u.x * w.y - u.y * w.x), u.x * w.x + u.y * w.y) * 180 / Math.PI;
        }

        [Theory]
        [InlineData(20)]
        [InlineData(25)]
        [InlineData(30)]
        [InlineData(33)]
        public void OffCentre_HasTheAngleBoundAtThePoint(double degrees)
        {
            // a needle: shortest edge (0, 0)-(1, 0), apex angle 2.9 degrees, its circumcentre 10 from the edge
            Vector2d a = new Vector2d(0, 0), b = new Vector2d(1, 0), c = new Vector2d(0.5, 10);
            Assert.True(QualityMesh2.Refiner.OffCentre(a, b, c, HalfCotOf(degrees), out var p));
            Assert.Equal(0.5, p.x, 15);                                   // on the bisector of the shortest edge
            Assert.True(p.y > 0 && p.y < 10);                             // towards the circumcentre, closer than it
            Assert.Equal(degrees, AngleAt(p, a, b), 9);                   // the bound at the new point
            // the same from every corner and orientation: the shortest edge is found, whichever vertex comes first
            foreach (var (x, y, z) in new[] { (b, c, a), (c, a, b), (b, a, c) }) {
                Assert.True(QualityMesh2.Refiner.OffCentre(x, y, z, HalfCotOf(degrees), out var q));
                Assert.Equal(p.x, q.x, 12); Assert.Equal(p.y, q.y, 12);
            }
        }

        [Theory]
        [InlineData(0.0, 0.0, 1.0, 0.0, 0.5, 0.8660254037844386)]     // equilateral
        [InlineData(0.0, 0.0, 1.0, 0.0, 0.5, 1.8660254037844386)]     // smallest angle 30 degrees, above half the bound
        [InlineData(0.0, 0.0, 1.0, 0.0, 0.5, 2.9)]                    // smallest angle 19.4 degrees: below the bound of 20, above its half
        public void OffCentre_IsTheCircumcentre_WhenThatIsCloser(double ax, double ay, double bx, double by, double cx, double cy)
        {
            // the circumcentre lies |pq| cot(alpha) / 2 from the midpoint, alpha the smallest angle; the off-centre |pq| cot(theta / 2) / 2:
            // with alpha at least theta / 2 the circumcentre is not farther
            Assert.False(QualityMesh2.Refiner.OffCentre(new Vector2d(ax, ay), new Vector2d(bx, by), new Vector2d(cx, cy), HalfCotOf(20), out _));
        }

        [Fact]
        public void OffCentre_JustBelowHalfTheBound_IsUsed()
        {
            // smallest angle 9.9 degrees at a bound of 20: the circumcentre is a little farther than the off-centre
            double h = 0.5 / Math.Tan(9.9 / 2 * Math.PI / 180);
            Assert.True(QualityMesh2.Refiner.OffCentre(new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(0.5, h), HalfCotOf(20), out var p));
            Assert.Equal(20, AngleAt(p, new Vector2d(0, 0), new Vector2d(1, 0)), 9);
        }

        [Fact]
        public void OffCentre_ScalesBitForBitWithPowersOfTwo()
        {
            Vector2d a = new Vector2d(3.25, -1.5), b = new Vector2d(4.125, -1.375), c = new Vector2d(-17.5, 9.0625);
            double hc = HalfCotOf(25);
            Assert.True(QualityMesh2.Refiner.OffCentre(a, b, c, hc, out var p));
            foreach (int k in new[] { -900, -500, -10, 10, 500, 900 }) {
                double f = Math.Pow(2, k);
                Assert.True(QualityMesh2.Refiner.OffCentre(a * f, b * f, c * f, hc, out var q));
                Assert.Equal(p.x * f, q.x);
                Assert.Equal(p.y * f, q.y);
            }
        }

        [Fact]
        public void HalfCot_IsTheCotangentOfHalfTheAngle()
        {
            for (int d = 1; d < 60; ++d) {
                double expected = 1 / Math.Tan(d / 2.0 * Math.PI / 180);
                Assert.Equal(1, HalfCotOf(d) / expected, 14);
            }
            Assert.True(double.IsPositiveInfinity(QualityMesh2.Refiner.HalfCot(0)));
        }

        /// <summary> both ways of splitting pass the oracle on the fuzz inputs, and off-centres change the mesh (they are in use) </summary>
        [Fact]
        public void Fuzz_WithAndWithoutOffCentres_PassTheOracle_AndDiffer()
        {
            int differ = 0;
            for (int seed = 0; seed < 24; ++seed) {
                var r = new Random(seed);
                var input = new Mesh2Input { X = new double[] { 0, 10, 10, 0 }, Y = new double[] { 0, 0, 10, 10 } };
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                var x = input.X.ToList(); var y = input.Y.ToList();
                for (int i = 0; i < 3; ++i) {
                    x.Add(r.Next(1, 10)); y.Add(r.Next(1, 10));
                    x.Add(r.Next(1, 10) + r.NextDouble() * 0.5); y.Add(r.Next(1, 10));
                    input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
                }
                input.X = x.ToArray(); input.Y = y.ToArray();
                input.MinAngleDeg = new[] { 20.0, 25, 30 }[seed % 3];
                input.SteinerLimit = 20000;
                var outputs = new Mesh2Output[2];
                foreach (bool on in new[] { true, false }) {
                    QualityMesh2.Refiner.OffCentresForBenchmarks = on;
                    try { outputs[on ? 0 : 1] = new QualityMesh2Candidate().Run(input); }
                    finally { QualityMesh2.Refiner.OffCentresForBenchmarks = null; }
                    var report = Oracle.Check(input, outputs[on ? 0 : 1], new OracleExpect { Milestone = Milestone.M3 });
                    Assert.True(report.Ok, "seed " + seed + (on ? " with" : " without") + " off-centres: " + report);
                }
                if (outputs[0].VX.Length != outputs[1].VX.Length || !outputs[0].VX.SequenceEqual(outputs[1].VX)) ++differ;
            }
            Assert.True(differ > 0, "off-centres changed no mesh");
        }

        /// <summary>
        /// the same on the snap grid: off-centres are rounded to the grid, and a refused off-centre (its pixel holds a constraint, or it
        /// adds no vertex) falls back to the circumcentre. Both ways pass the oracle, and off-centres change some meshes.
        /// </summary>
        [Fact]
        public void GridFuzz_WithAndWithoutOffCentres_PassTheOracle_AndDiffer()
        {
            var random = new Random(322);
            int differ = 0;
            for (int iter = 0; iter < 36; ++iter) {
                var x = new System.Collections.Generic.List<double> { 0, 16, 16, 0 }; var y = new System.Collections.Generic.List<double> { 0, 0, 16, 16 };
                var input = new Mesh2Input();
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                int m = 4 + random.Next(12);
                for (int i = 0; i < m; ++i) {
                    for (int k = 0; k < 2; ++k) { x.Add(1 + Math.Round(random.NextDouble() * 14 * 64) / 64); y.Add(1 + Math.Round(random.NextDouble() * 14 * 64) / 64); }
                    input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
                }
                input.X = x.ToArray(); input.Y = y.ToArray();
                input.SnapTolerance = new[] { 0.25, 0.5, 0.3, 1.0 / 3, 0.125, 1.0 / 16 }[iter % 6];
                input.MinAngleDeg = new[] { 20.0, 25, 30 }[iter % 3];
                input.SteinerLimit = 50000;
                var outputs = new Mesh2Output[2];
                foreach (bool on in new[] { true, false }) {
                    QualityMesh2.Refiner.OffCentresForBenchmarks = on;
                    try { outputs[on ? 0 : 1] = new QualityMesh2Candidate().Run(input); }
                    finally { QualityMesh2.Refiner.OffCentresForBenchmarks = null; }
                    Assert.Equal(Mesh2Status.Success, outputs[on ? 0 : 1].Status);
                    var report = Oracle.Check(input, outputs[on ? 0 : 1], new OracleExpect { Milestone = Milestone.M3 });
                    Assert.True(report.Ok, "iteration " + iter + (on ? " with" : " without") + " off-centres: " + report);
                }
                if (outputs[0].VX.Length != outputs[1].VX.Length || !outputs[0].VX.SequenceEqual(outputs[1].VX)) ++differ;
            }
            Assert.True(differ > 0, "off-centres changed no mesh on the grid");
        }
            [Theory]
        [InlineData(0.3, 1.0, 0.002)]
        [InlineData(0.5, 2.0, 0.001)]
        [InlineData(2.0, 5.0, 0.01)]
        [InlineData(6.0, 20.0, 0.01)]
        public void SmallInputAngle_UnderAnAreaBound_TakesCircumcentres(double inputDeg, double boundDeg, double maxArea)
        {
            double a = inputDeg * Math.PI / 180;
            var input = new Mesh2Input { X = new double[] { 0, 4, 4 * Math.Cos(a) }, Y = new double[] { 0, 0, 4 * Math.Sin(a) } };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
            input.MinAngleDeg = boundDeg; input.MaxArea = maxArea; input.SteinerLimit = 20000;
            var outputs = new Mesh2Output[2];
            foreach (bool on in new[] { true, false }) {
                QualityMesh2.Refiner.OffCentresForBenchmarks = on;
                try { outputs[on ? 0 : 1] = new QualityMesh2Candidate().Run(input); }
                finally { QualityMesh2.Refiner.OffCentresForBenchmarks = null; }
                var report = Oracle.Check(input, outputs[on ? 0 : 1], new OracleExpect { Milestone = Milestone.M3 });
                Assert.True(report.Ok, (on ? "with" : "without") + " off-centres: " + report);
            }
            Assert.Equal(outputs[1].VX, outputs[0].VX);
            Assert.Equal(outputs[1].VY, outputs[0].VY);
        }
    }
}
