using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Diametral lenses (Shewchuk 2002, section 6.4) in <see cref="QualityMesh2"/> without <see cref="QualityMesh2.Conforming"/>, and the
    /// "constrained quality" mode of the acceptance harness that judges such meshes as constrained Delaunay (owner decision 2026-11-02).
    /// </summary>
    public class QualityMesh2LensTests
    {
        static Mesh2Output Run(Mesh2Input input, bool? lenses = null)
        {
            QualityMesh2.Refiner.LensesForBenchmarks = lenses;
            try { return new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.LensesForBenchmarks = null; }
        }

        static OracleReport Judge(Mesh2Input input, Mesh2Output output) => Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });

        // ------------------------------------------------------------ the predicate

        [Theory]
        [InlineData(0.5773, true)]     // 2 atan(1 / 0.5773) = 120.006 degrees: inside
        [InlineData(0.5774, false)]    // 119.997 degrees: outside
        [InlineData(0.0, true)]        // on the segment (180 degrees)
        [InlineData(0.9, false)]       // inside the diametral circle (96 degrees), outside the lens
        [InlineData(1.0, false)]       // on the diametral circle (90 degrees)
        [InlineData(-0.5773, true)]    // the lens is symmetric
        public void InLens_IsTheAngleOfAtLeast120Degrees(double h, bool inside)
        {
            // the boundary of the lens of (0, 0)-(2, 0) passes through (1, 1 / sqrt 3) = (1, 0.57735...)
            Assert.Equal(inside, QualityMesh2.Refiner.InLens(new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(1, h)));
            Assert.Equal(inside, QualityMesh2.Refiner.InLens(new Vector2d(2, 0), new Vector2d(0, 0), new Vector2d(1, h)));
        }

        [Fact]
        public void InLens_OutsideTheSpanOfTheSegment_IsOutside()
        {
            Assert.False(QualityMesh2.Refiner.InLens(new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(-0.01, 0)));
            Assert.False(QualityMesh2.Refiner.InLens(new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(2.5, 0.1)));
            Assert.False(QualityMesh2.Refiner.InLens(new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(0, 0)));   // an end
        }

        /// <summary> the filter agrees with integer arithmetic next to the boundary, and every 2^k scale gives the same answer (the integer
        /// path runs beyond the filter's range) </summary>
        [Fact]
        public void InLens_MatchesIntegers_NearTheBoundary_AtEveryScale()
        {
            var r = new Random(25);
            int checkedNear = 0;
            for (int i = 0; i < 4000; ++i) {
                // integer points: a segment and a point near its lens boundary
                long ax = r.Next(-1000, 1000), ay = r.Next(-1000, 1000), cx = ax + r.Next(1, 2000), cy = ay + r.Next(-1000, 1000);
                double mx = (ax + cx) / 2.0, my = (ay + cy) / 2.0, dx = cx - ax, dy = cy - ay;
                double t = (r.NextDouble() * 2 - 1) * 0.003 + (r.Next(2) == 0 ? 1 : -1) / (2 * Math.Sqrt(3));
                long px = (long)Math.Round(mx - dy * t), py = (long)Math.Round(my + dx * t);
                BigInteger ux = ax - px, uy = ay - py, wx = cx - px, wy = cy - py, dot = ux * wx + uy * wy;
                bool expected = dot < 0 && 4 * dot * dot >= (ux * ux + uy * uy) * (wx * wx + wy * wy);
                var a = new Vector2d(ax, ay); var c = new Vector2d(cx, cy); var p = new Vector2d(px, py);
                foreach (int k in new[] { 0, -1000, -600, -140, 140, 600, 900 }) {
                    double f = Math.Pow(2, k);
                    Assert.Equal(expected, QualityMesh2.Refiner.InLens(a * f, c * f, p * f));
                }
                if (Math.Abs((double)(4 * dot * dot - (ux * ux + uy * uy) * (wx * wx + wy * wy))) < 1e-2 * (double)((ux * ux + uy * uy) * (wx * wx + wy * wy))) ++checkedNear;
            }
            Assert.True(checkedNear > 100, "few points near the boundary: " + checkedNear);
        }

        // ------------------------------------------------------------ the rule: when lenses are used

        static Mesh2Input Fuzz(int seed, double angle, bool constrained)
        {
            var r = new Random(seed);
            var x = new List<double> { 0, 16, 16, 0, 6, 9, 7 }; var y = new List<double> { 0, 0, 16, 16, 6, 6.5, 9 };
            var input = new Mesh2Input();
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
            int m = 3 + r.Next(6);
            for (int i = 0; i < m; ++i) {
                for (int k = 0; k < 2; ++k) {
                    double px, py;
                    do { px = 1 + Math.Round(r.NextDouble() * 14 * 64) / 64; py = 1 + Math.Round(r.NextDouble() * 14 * 64) / 64; }
                    while (px > 5 && px < 10 && py > 5 && py < 10);
                    x.Add(px); y.Add(py);
                }
                input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
            }
            input.X = x.ToArray(); input.Y = y.ToArray();
            input.MinAngleDeg = angle; input.SteinerLimit = 50000; input.ConstrainedQuality = constrained;
            return input;
        }

        static bool Same(Mesh2Output a, Mesh2Output b) => a.VX.SequenceEqual(b.VX) && a.VY.SequenceEqual(b.VY) && a.Triangles.SelectMany(t => t).SequenceEqual(b.Triangles.SelectMany(t => t));

        /// <summary> lenses up to arcsin(sqrt 3 / 4) = 25.65 degrees without conforming; circles above it and with conforming </summary>
        [Theory]
        [InlineData(20.0, true, true)]
        [InlineData(25.0, true, true)]
        [InlineData(25.65, true, true)]      // just below the bound (25.6589...)
        [InlineData(25.66, true, false)]     // just above it
        [InlineData(30.0, true, false)]
        [InlineData(20.0, false, false)]     // conforming (O2): circles
        public void Lenses_FollowTheRule(double angle, bool constrained, bool lensesExpected)
        {
            int differ = 0;
            for (int seed = 0; seed < 12; ++seed) {
                var input = Fuzz(seed, angle, constrained);
                var byRule = Run(input);
                var lens = Run(input, true);
                var circle = Run(input, false);
                Assert.True(Same(byRule, lensesExpected ? lens : circle), "seed " + seed);
                if (!Same(lens, circle)) ++differ;
            }
            Assert.True(differ > 0, "lenses changed no mesh");   // the inputs tell the two apart
        }

        /// <summary> the constrained quality mode passes the oracle and needs fewer points with lenses than with circles </summary>
        [Theory]
        [InlineData(20.0)]
        [InlineData(25.0)]
        public void Fuzz_ConstrainedQuality_PassesTheOracle_WithFewerPoints(double angle)
        {
            long lensPoints = 0, circlePoints = 0;
            for (int seed = 0; seed < 40; ++seed) {
                var input = Fuzz(1000 + seed, angle, true);
                var o = Run(input);
                Assert.Equal(Mesh2Status.Success, o.Status);
                Assert.False(o.SteinerLimitReached);
                var report = Judge(input, o);
                Assert.True(report.Ok, "seed " + seed + ": " + report);
                lensPoints += o.VX.Length;
                circlePoints += Run(input, false).VX.Length;
            }
            Assert.True(lensPoints < circlePoints, lensPoints + " vs " + circlePoints);
        }

        // ------------------------------------------------------------ invariance, reuse, determinism

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Similarity_ConstrainedQuality(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Fuzz(7, 22, true);
            var moved = Fuzz(7, 22, true);
            for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            var b = Run(moved);
            var report = Judge(moved, b);
            Assert.True(report.Ok, name + ": " + report);
            if (s.IsExact) {
                var a = Run(plain);
                Assert.Equal(a.VX.Select(v => v * s.Scale), b.VX);
                Assert.Equal(a.VY.Select(v => v * s.Scale), b.VY);
                Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
            }
        }

        [Fact]
        public void Reuse_And_Parallel_RunsAreIdentical()
        {
            var mesher = new QualityMesh2 { MinAngle = 25, Conforming = false };
            var input = ConstrainedDelaunayMesh2Candidate.Convert(Fuzz(3, 25, true));
            var first = mesher.Compute(input).Mesh;
            var results = new ConstrainedMesh2[8];
            Parallel.For(0, results.Length, i => results[i] = mesher.Compute(input).Mesh);
            foreach (var m in results.Concat(new[] { mesher.Compute(input).Mesh })) {
                Assert.Equal(first.Vertices, m.Vertices);
                Assert.Equal(first.Triangles, m.Triangles);
                Assert.Equal(first.BadTriangles, m.BadTriangles);
            }
        }

        // ------------------------------------------------------------ the input key and the oracle mode

        [Fact]
        public void TextFormat_ModeLine_OnlyWhenSet_AndRoundTrips()
        {
            var input = Fuzz(1, 20, false);
            string text = TextFormat.WriteInput(input);
            Assert.DoesNotContain("mode", text);
            input.ConstrainedQuality = true;
            string withMode = TextFormat.WriteInput(input);
            Assert.EndsWith("conforming 0\nmode constrained\nend\n", withMode);
            Assert.NotEqual(TextFormat.InputSha256(Fuzz(1, 20, false)), TextFormat.InputSha256(input));
            var back = TextFormat.ReadInput(withMode);
            Assert.True(back.ConstrainedQuality);
            Assert.Equal(withMode, TextFormat.WriteInput(back));
            Assert.False(TextFormat.ReadInput(text).ConstrainedQuality);
            Assert.Throws<FormatException>(() => TextFormat.ReadInput(withMode.Replace("mode constrained", "mode conforming")));
            Assert.Throws<FormatException>(() => TextFormat.ReadInput(withMode.Replace("conforming 0", "conforming 1")));
        }

        /// <summary> the worked example of MESHING2D_GOLDEN_FORMAT.md keeps its hash: inputs without the mode are written as before </summary>
        [Fact]
        public void TextFormat_ExistingHash_Unchanged()
        {
            var input = new Mesh2Input { X = new double[] { 0, 1, 0 }, Y = new double[] { 0, 0, 1 } };
            Assert.Equal("404ba5d0db189586624422cb3610e0f32df60c39288536b41235e964b8d7e3db", TextFormat.InputSha256(input));
        }

        /// <summary>
        /// a constrained Delaunay mesh that is not conforming: a vertex in the diametral circle of a segment, across it. The constrained
        /// quality mode accepts it; the default quality mode (O2) does not.
        /// </summary>
        [Fact]
        public void Oracle_ConstrainedQuality_AcceptsWhatConformingRejects()
        {
            var input = new Mesh2Input { X = new double[] { 0, 10, 10, 0, 2, 8, 5 }, Y = new double[] { 0, 0, 10, 10, 5, 5, 4.5 } };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Segments.Add(new SegmentInput(4, 5));
            input.MinAngleDeg = 0;
            var cdt = new ConstrainedDelaunayMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, cdt.Status);
            cdt.BadTriangles = new int[0];
            var conforming = Judge(input, cdt);
            Assert.False(conforming.Ok);
            Assert.Contains("NotDelaunay", conforming.ToString());
            input.ConstrainedQuality = true;
            var constrained = Judge(input, cdt);
            Assert.True(constrained.Ok, constrained.ToString());
        }
    }
}
