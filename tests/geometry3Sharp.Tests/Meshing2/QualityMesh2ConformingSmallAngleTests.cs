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
    /// subsegment only while the subsegment is a locally Delaunay edge, so O2 holds; on the snap grid too.
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

        /// <summary> pinned triangle counts of rule C (they change only with the rule, and with the pair threshold, which became 60 degrees on 2026-11-09: the ghost side counts as Delaunay, a tie of the in-circle test too) </summary>
        [Theory]
        [InlineData("wedge", 2.6, 20, 24)]
        [InlineData("wedge", 10.0, 20, 6)]
        [InlineData("wedge", 1.0, 30, 36)]
        [InlineData("pair", 1, 20, 17)]
        [InlineData("pair", 3, 30, 27)]
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

        /// <summary> one mesher, two computes, on the grid with rule C: the same mesh </summary>
        [Fact]
        public void OnTheGrid_Reuse()
        {
            var input = Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(20, 2.6, 1.0 / 64));
            var c = ConstrainedDelaunayMesh2Candidate.Convert(input);
            var mesher = new QualityMesh2 { MinAngle = 20, SnapTolerance = 1.0 / 64, Conforming = true };
            var a = mesher.Compute(c); var b = mesher.Compute(c);
            Assert.Equal(a.Mesh.Vertices, b.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, b.Mesh.Triangles);
        }

        /// <summary> with rule C on, the chain rule still matters: without it star seed 15 shows a coincidence (review of the grid change) </summary>
        [Fact]
        public void ChainRule_StillMattersWithRuleC()
        {
            var input = StarPolygonFuzz.Make(15);
            QualityMesh2.Refiner.KeepChainsApartForTests = false;
            try {
                var r = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
                Assert.Contains(r.Violations, v => v.Kind == Violation.RefinementCoincidence);
            }
            finally { QualityMesh2.Refiner.KeepChainsApartForTests = null; }
            var ok = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
            Assert.DoesNotContain(ok.Violations, v => v.Kind == Violation.RefinementCoincidence);
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
        /// on the snap grid rule C fires as well: the oracle accepts the mesh (a forced split the mesher's pinch rule refuses is excused by
        /// the oracle's chain identity, Oracle.OwnStretch), with fewer triangles than without the rule and exemptions counted
        /// </summary>
        [Theory]
        [InlineData(20)]
        [InlineData(30)]
        public void OnTheGrid_TheRuleFires(double angle)
        {
            var input = Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(angle, 2.6, 1.0 / 256));
            var on = Run(input); var off = Run(input, false);
            Assert.True(off.Report.Ok, off.Report.ToString());
            Assert.True(on.Report.Ok, on.Report.ToString());
            Assert.True(on.Output.Triangles.Length < off.Output.Triangles.Length, on.Output.Triangles.Length + " against " + off.Output.Triangles.Length);
            Assert.True(on.Counts[8] + on.Counts[9] > 0);
        }

        /// <summary>
        /// the zigzag of Q17 (a domain 7 mm wide on a grid of 1 cm) with rule C: the late forced splits that the mesher's pinch rule refuses
        /// are exactly the subsegments the oracle excuses (it ended INVALID while the oracle judged chain identity by envelope: two chains
        /// within a pixel of each other along the piece counted as one chain). VALID, the rule fires, fewer triangles than without it
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void Zigzag_OnTheGrid_IsValid_WithTheRule(int which)
        {
            var input = Conforming(QualityMesh2CollapsedPartsTests.Zigzag(which, 20));
            var on = Run(input); var off = Run(input, false);
            Assert.True(on.Report.Ok, on.Report.ToString());
            Assert.True(on.Counts[8] + on.Counts[9] > 0);
            Assert.True(on.Output.Triangles.Length < off.Output.Triangles.Length, on.Output.Triangles.Length + " against " + off.Output.Triangles.Length);
        }


        /// <summary>
        /// rule C on the grid under every similarity: accepted in every frame, and at the 2^k scales the mesh is the same bit for bit (the
        /// grid scales with the input), repeated runs give the same mesh
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void OnTheGrid_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(20, 2.6, 1.0 / 16));
            var moved = Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(20, 2.6, 1.0 / 16));
            for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            moved.SnapTolerance = plain.SnapTolerance * s.Scale;
            var a = Run(plain); var b = Run(moved);
            Assert.True(b.Report.Ok, name + ": " + b.Report);
            Assert.True(b.Counts[8] + b.Counts[9] > 0, name);
            if (!s.IsExact) return;
            Assert.Equal(a.Output.VX.Select(v => v * s.Scale), b.Output.VX);
            Assert.Equal(a.Output.VY.Select(v => v * s.Scale), b.Output.VY);
            Assert.Equal(a.Output.Triangles.SelectMany(t => t), b.Output.Triangles.SelectMany(t => t));
            Assert.Equal(b.Output.Triangles.SelectMany(t => t), Run(moved).Output.Triangles.SelectMany(t => t));
        }

        /// <summary>
        /// the oracle's chain identity against its former envelope approximation (Oracle.OwnStretch): over the same meshes (the star and
        /// segment fuzz, the small-angle wedges and pairs on the grid, the zigzags; rule C on and off) every mesh the envelope oracle
        /// accepted is accepted, and the one verdict that changes is the zigzag with rule C: INVALID before, VALID now
        /// </summary>
        [Fact]
        public void OracleChainIdentity_AcceptsWhatTheEnvelopeAccepted()
        {
            var changed = new List<string>();
            int judged = 0;
            var inputs = new List<(string, Func<Mesh2Input>)>();
            for (int seed = 0; seed < 6; ++seed) { int sd = seed; inputs.Add(("star " + sd, () => StarPolygonFuzz.Make(sd))); inputs.Add(("seg " + sd, () => QualityMesh2PinchFreeTests.Segments(sd, 20))); }
            foreach (double deg in new[] { 2.6, 10.0 }) { double dg = deg; inputs.Add(("wedge " + dg, () => Conforming(QualityMesh2SmallAngleRuleTests.HullWedge(20, dg, 1.0 / 64)))); }
            foreach (int kind in new[] { 0, 2 }) { int kd = kind; inputs.Add(("pair " + kd, () => { var i = Conforming(QualityMesh2ApexExemptionTests.Pair(kd, 20)); i.SnapTolerance = 1.0 / 64; return i; })); }
            inputs.Add(("zigzag 0", () => Conforming(QualityMesh2CollapsedPartsTests.Zigzag(0, 20))));
            foreach (var (name, make) in inputs)
                foreach (bool rule in new[] { true, false }) {
                    var input = make();
                    QualityMesh2.Refiner.SmallAngleExemptionForTests = rule ? (bool?)null : false;
                    Mesh2Output output;
                    try { output = new QualityMesh2Candidate().Run(input); }
                    finally { QualityMesh2.Refiner.SmallAngleExemptionForTests = null; }
                    if (output.Status != Mesh2Status.Success) continue;
                    Oracle.LegacyEnvelopeChainForDifferential = true;
                    bool before;
                    try { before = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 }).Ok; }
                    finally { Oracle.LegacyEnvelopeChainForDifferential = false; }
                    bool now = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 }).Ok;
                    ++judged;
                    if (before && !now) Assert.Fail(name + (rule ? " (rule C)" : " (no rule)") + ": accepted by the envelope oracle, rejected by chain identity");
                    if (before != now) changed.Add(name + (rule ? " (rule C)" : " (no rule)"));
                }
            Assert.True(judged > 30, judged.ToString());
            Assert.Equal(new[] { "zigzag 0 (rule C)" }, changed);
        }

        /// <summary> the in-circle test of rule C: a cocircular pair of apexes is Delaunay (the exemption stays), strictly inside is not </summary>
        [Fact]
        public void IsLocallyDelaunay_TreatsACocircularPairAsDelaunay()
        {
            var a = new Vector2d(0, 0); var b = new Vector2d(2, 0); var c = new Vector2d(1, 1);
            Assert.True(QualityMesh2.Refiner.IsLocallyDelaunay(a, b, c, new Vector2d(1, -1)));      // on the circle of abc: 0
            Assert.False(QualityMesh2.Refiner.IsLocallyDelaunay(a, b, c, new Vector2d(1, -0.5)));   // inside
            Assert.True(QualityMesh2.Refiner.IsLocallyDelaunay(a, b, c, new Vector2d(1, -1.5)));    // outside
            // the same at a scale and a shift that keep it exact (a lattice circle, radius 5: 3-4-5)
            Assert.True(QualityMesh2.Refiner.IsLocallyDelaunay(new Vector2d(-4 + 100, -3), new Vector2d(4 + 100, 3), new Vector2d(3 + 100, -4), new Vector2d(-3 + 100, 4)));
        }

        internal static Mesh2Input HoleBesideTheWedge(double rx, double ry)
        {
            // a hole triangle P Q R under the edge PQ, a segment from P at 2.6 degrees above it: the subsegments of PQ have a hole triangle on one side
            double t = Math.Round(8 * Math.Tan(2.6 * Math.PI / 180), 12);
            var input = Generators.Literal(-10, -10, 10, -10, 10, 10, -10, 10, 0, 0, 8, 0, rx, ry, 8, t);
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true, 2));
            input.Segments.Add(new SegmentInput(4, 7));
            input.MinAngleDeg = 25; input.SteinerLimit = 20000;
            return input;
        }

        /// <summary>
        /// the neighbour across an exempt subsegment is a hole triangle (the hole's edge PQ meets a segment from P at 2.6 degrees; R moves):
        /// the conforming mode counts hole triangles (O2 across holes), so rule C revokes exemptions there. A check that skipped hole triangles
        /// (a mutant) leaves subsegments that are not Delaunay edges: the oracle rejects those meshes (found by exploring R). Pinned
        /// triangle and revocation counts
        /// </summary>
        [Theory]
        [InlineData(1.0, -0.05, 285, 72)]
        [InlineData(1.0, -0.2, 137, 29)]
        [InlineData(1.0, -0.6, 68, 13)]
        [InlineData(2.0, -0.6, 55, 9)]
        [InlineData(6.0, -0.2, 54, 8)]
        public void HoleTriangleAcrossTheExemptSubsegment_RevokesIt(double rx, double ry, int triangles, long revoked)
        {
            var r = Run(HoleBesideTheWedge(rx, ry));
            Assert.True(r.Report.Ok, r.Report.ToString());
            Assert.Equal(triangles, r.Output.Triangles.Length);
            Assert.Equal(revoked, r.Counts[16]);
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
