using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The small-angle rule in the conforming (default) mode, "rule C" (MESHING2D_SMALL_ANGLES_CONFORMING.md): a partner vertex exempts a
    /// subsegment only while the subsegment is a locally Delaunay edge, so O2 holds; off the snap grid only.
    /// </summary>
    public class QualityMesh2ConformingSmallAngleTests
    {
        readonly ITestOutputHelper o; public QualityMesh2ConformingSmallAngleTests(ITestOutputHelper o) { this.o = o; }

        static Mesh2Input Conforming(Mesh2Input input) { input.ConstrainedQuality = false; return input; }

        internal static IEnumerable<(string Name, Mesh2Input Input)> Inputs()
        {
            foreach (double deg in new[] { 1.0, 2.6, 5.0, 10.0 })
                foreach (double angle in new[] { 20.0, 30.0 })
                    yield return ($"wedge {deg} q{angle}", Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(angle, deg)));
            foreach (int kind in new[] { 0, 1, 2, 3 })
                foreach (double angle in new[] { 20.0, 30.0 })
                    yield return ($"pair {kind} q{angle}", Conforming(QualityMesh2ApexExemptionTests.Pair(kind, angle)));
        }

        static (Mesh2Output Output, OracleReport Report, long[] Counts) Run(Mesh2Input input, bool? exemption = null, bool? deleteCheck = null)
        {
            var counts = new long[17];
            QualityMesh2.Refiner.SmallAngleExemptionForTests = exemption;
            QualityMesh2.Refiner.ConformingDelaunayCheckForTests = deleteCheck;
            QualityMesh2.Refiner.CountsForBenchmarks = counts;
            try {
                var output = new QualityMesh2Candidate().Run(input);
                Assert.True(output.Status == Mesh2Status.Success, output.Status + " " + output.FailureKind);
                var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
                return (output, report, counts);
            }
            finally { QualityMesh2.Refiner.SmallAngleExemptionForTests = null; QualityMesh2.Refiner.ConformingDelaunayCheckForTests = null; QualityMesh2.Refiner.CountsForBenchmarks = null; }
        }

        /// <summary> rule C: the oracle accepts every mesh (O2 included, judged as conforming), with fewer triangles than without the rule </summary>
        [Fact]
        public void RuleC_IsOracleValid_AndSavesTriangles()
        {
            foreach (var (name, input) in Inputs()) {
                var off = Run(input, false); var on = Run(input);
                Assert.True(off.Report.Ok, name + " (rule off): " + off.Report);
                Assert.True(on.Report.Ok, name + ": " + on.Report);
                Assert.False(on.Output.SteinerLimitReached, name);
                Assert.True(on.Output.Triangles.Length < off.Output.Triangles.Length, name + ": " + on.Output.Triangles.Length + " against " + off.Output.Triangles.Length);
                Assert.True(on.Counts[8] + on.Counts[9] > 0, name + ": no exemption");
            }
        }

        /// <summary>
        /// the Delaunay check is what keeps O2: the same rule without it leaves subsegments that are not Delaunay edges (found with the
        /// catalogue rows F-4, F-5, F-26, F-42 and others: NotDelaunayLocal), on all but one of these inputs
        /// </summary>
        [Fact]
        public void WithoutTheDelaunayCheck_O2Breaks()
        {
            int broken = 0, all = 0;
            foreach (var (name, input) in Inputs()) {
                ++all;
                var r = Run(input, null, false);
                if (!r.Report.Ok) { ++broken; Assert.Contains("NotDelaunayLocal", r.Report.ToString()); }
            }
            Assert.True(broken >= all - 2, broken + " of " + all);
            // a named one
            Assert.False(Run(Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(20, 2.6)), null, false).Report.Ok);
        }

        /// <summary> pinned triangle counts of rule C (they change only with the rule: the ghost side counts as Delaunay, a tie of the in-circle test too) </summary>
        [Theory]
        [InlineData("wedge", 2.6, 20, 24)]
        [InlineData("wedge", 10.0, 20, 7)]
        [InlineData("wedge", 1.0, 30, 37)]
        [InlineData("pair", 1, 20, 22)]
        [InlineData("pair", 3, 30, 28)]
        public void RuleC_PinnedCounts(string kind, double what, double angle, int triangles)
        {
            var input = kind == "wedge" ? Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(angle, what)) : Conforming(QualityMesh2ApexExemptionTests.Pair((int)what, angle));
            Assert.Equal(triangles, Run(input).Output.Triangles.Length);
        }

        /// <summary> exemptions that were granted and then revoked (the subsegment was not locally Delaunay): counted in slot 16 </summary>
        [Fact]
        public void SomeExemptionsAreRevoked_AndTheirSplitsAreForced()
        {
            var r = Run(Conforming(QualityMesh2ApexExemptionTests.Pair(1, 20)));
            Assert.True(r.Counts[16] > 0);
            Assert.True(r.Counts[16] < r.Counts[8] + r.Counts[9], r.Counts[16] + " revoked of " + (r.Counts[8] + r.Counts[9]) + " granted");
            Assert.Equal(0, Run(Conforming(QualityMesh2ApexExemptionTests.Pair(1, 20)), false).Counts[16]);   // the rule off: nothing granted, nothing revoked
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary> under every similarity the oracle accepts it, and the 2^k scales give the same mesh bit for bit </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void RuleC_UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(25, 2.6));
            var moved = Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(25, 2.6));
            for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            var b = Run(moved);
            Assert.True(b.Report.Ok, name + ": " + b.Report);
            if (s.IsExact) {
                var a = Run(plain);
                Assert.Equal(a.Output.VX.Select(v => v * s.Scale), b.Output.VX);
                Assert.Equal(a.Output.VY.Select(v => v * s.Scale), b.Output.VY);
                Assert.Equal(a.Output.Triangles.SelectMany(t => t), b.Output.Triangles.SelectMany(t => t));
            }
        }

        /// <summary> one mesher, two computes: the same mesh </summary>
        [Fact]
        public void Reuse_SameInstance()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(25, 2.6)));
            var mesher = new QualityMesh2 { MinAngle = 25, Conforming = true };
            var a = mesher.Compute(c); var b = mesher.Compute(c);
            Assert.Equal(a.Mesh.Vertices, b.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, b.Mesh.Triangles);
        }

        /// <summary>
        /// on the snap grid the rule stays off in the conforming mode (a split that rule C forces late can be refused there where the
        /// oracle still demands it: the zigzag of QualityMesh2CollapsedPartsTests ended INVALID with it): the mesh is the one without the rule
        /// </summary>
        [Theory]
        [InlineData(20)]
        [InlineData(30)]
        public void OnTheGrid_TheRuleStaysOff(double angle)
        {
            var input = Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(angle, 2.6, 1.0 / 256));
            var on = Run(input); var off = Run(input, false);
            Assert.True(on.Report.Ok, on.Report.ToString());
            Assert.Equal(off.Output.VX, on.Output.VX);
            Assert.Equal(off.Output.VY, on.Output.VY);
            Assert.Equal(off.Output.Triangles.SelectMany(t => t), on.Output.Triangles.SelectMany(t => t));
            Assert.Equal(0, on.Counts[8] + on.Counts[9] + on.Counts[16]);
        }

        /// <summary> with lenses forced on (benchmarks), the conforming mode has no rule: no exemption, the mesh of the rule off </summary>
        [Fact]
        public void WithLensesForcedOn_TheRuleIsOff()
        {
            var input = Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(20, 2.6));
            QualityMesh2.Refiner.LensesForBenchmarks = true;
            try {
                var on = Run(input); var off = Run(input, false);
                Assert.Equal(0, on.Counts[8] + on.Counts[9] + on.Counts[16]);
                Assert.Equal(off.Output.Triangles.SelectMany(t => t), on.Output.Triangles.SelectMany(t => t));
            }
            finally { QualityMesh2.Refiner.LensesForBenchmarks = null; }
        }

        /// <summary>
        /// a hole and the wedge: the conforming mode counts the vertices of hole triangles as encroaching too (O2 across holes), and so does
        /// the Delaunay check; valid, with no more triangles than without the rule (a smoke test: whether the hole triangle is ever the
        /// neighbour across the subsegment is not asserted)
        /// </summary>
        [Fact]
        public void WedgeWithAHole_IsValid()
        {
            double t = Math.Round(8 * Math.Tan(2.6 * Math.PI / 180), 12);
            var input = Generators.Literal(0, 0, 10, 0, 10, 6, 0, 6, 3, 0.8, 5, 0.8, 5, 1.6, 3, 1.6, 8, t);
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true, 2));
            input.Segments.Add(new SegmentInput(0, 8));
            input.MinAngleDeg = 25; input.SteinerLimit = 20000;
            var on = Run(input); var off = Run(input, false);
            Assert.True(on.Report.Ok, on.Report.ToString());
            Assert.False(on.Output.SteinerLimitReached);
            Assert.True(on.Output.Triangles.Length <= off.Output.Triangles.Length);
        }

        /// <summary> without an angle bound (an area bound only) the rule is off </summary>
        [Fact]
        public void WithoutAnAngleBound_TheRuleIsOff()
        {
            var input = Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(0, 2.6));
            input.MaxArea = 1;
            var on = Run(input); var off = Run(input, false);
            Assert.Equal(off.Output.VX, on.Output.VX);
            Assert.Equal(off.Output.Triangles.SelectMany(t => t), on.Output.Triangles.SelectMany(t => t));
            Assert.Equal(0, on.Counts[8] + on.Counts[9] + on.Counts[16]);
        }

        /// <summary> a seeded sweep of fans of segments at 0.5 to 10 degrees in the conforming mode: valid, no Steiner limit, rule C not more than the rule off </summary>
        [Fact]
        public void Fans_FromHalfADegreeToTen_AreValid()
        {
            var rng = new Random(3);
            for (int i = 0; i < 40; ++i) {
                double deg = 0.5 + 9.5 * rng.NextDouble(); double angle = rng.Next(0, 2) == 0 ? 20 : 30;
                int rays = rng.Next(2, 5);
                var xy = new List<double> { 0, 0 };
                var segs = new List<SegmentInput>();
                for (int r = 0; r < rays; ++r) {
                    double th = deg * Math.PI / 180 * r * (1 + 0.3 * rng.NextDouble()), len = 4 + 4 * rng.NextDouble();
                    xy.Add(Math.Round(len * Math.Cos(th), 6)); xy.Add(Math.Round(len * Math.Sin(th), 6));
                    segs.Add(new SegmentInput(0, r + 1));
                }
                var input = Generators.Literal(xy.ToArray());
                input.Segments.AddRange(segs);
                input.MinAngleDeg = angle; input.SteinerLimit = 20000;
                var on = Run(input); var off = Run(input, false);
                Assert.True(on.Report.Ok, "fan " + i + " (" + deg + "°, q" + angle + "): " + on.Report);
                Assert.False(on.Output.SteinerLimitReached, "fan " + i);
                Assert.True(on.Output.Triangles.Length <= off.Output.Triangles.Length, "fan " + i + ": " + on.Output.Triangles.Length + " against " + off.Output.Triangles.Length);
            }
        }
    }
}
