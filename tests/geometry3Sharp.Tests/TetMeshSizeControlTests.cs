using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Size control of the tet meshing (docs/design/TET_MESHING.md 17.12, stage P3, target T7): a uniform target edge length, a user size
    /// function and an automatic one graded with the distance to the boundary, as an extra bad-tet criterion of the refinement. Interior only:
    /// the boundary keeps the wrap's spacing. Every run has its own stopwatch as CancelF.
    /// </summary>
    public class TetMeshSizeControlTests
    {
        static double Diagonal(DMesh3 m) => m.GetBounds().DiagonalLength;

        static MeshTetrahedralization Make(string shape, double alpha = 12)
            => new MeshTetrahedralization(TetMeshingStage2Tests.Input(shape)) { RelativeAlpha = alpha, RescuePasses = 0 };

        static bool Go(MeshTetrahedralization tm)
        {
            var sw = Stopwatch.StartNew();
            tm.CancelF = () => sw.Elapsed.TotalSeconds > 15;
            return tm.Compute();
        }

        static void AssertGood(MeshTetrahedralization tm)
        {
            Assert.NotNull(tm.Result);
            Assert.Null(TetMeshQuality.NotValidReason(tm.Result));
            Assert.Null(TetOracle.Check(tm.Result, null));
        }

        static double MeanEdge(TetMesh3 t, Func<Vector3d, bool> where)
        {
            double sum = 0; int n = 0;
            foreach (Index4i q in t.Tets) {
                Vector3d a = t.Vertices[q.a], b = t.Vertices[q.b], c = t.Vertices[q.c], d = t.Vertices[q.d];
                if (!where((a + b + c + d) * 0.25)) continue;
                sum += a.Distance(b) + a.Distance(c) + a.Distance(d) + b.Distance(c) + b.Distance(d) + c.Distance(d); n += 6;
            }
            return n == 0 ? 0 : sum / n;
        }

        // ---- no size set: nothing changes (the fingerprints are pinned in TetRefinementWorkBoundTests; here the "off" values of every option)
        [Theory]
        [InlineData("box"), InlineData("sphere")]
        public void OffValuesOfEveryOption_GiveTheDefaultResultBitForBit(string shape)
        {
            MeshTetrahedralization a = Make(shape); Assert.True(Go(a));
            MeshTetrahedralization b = Make(shape);
            b.TargetEdgeLength = double.NaN; b.GradingFactor = -1; b.SizeFunction = null; b.MinSizeFactor = 5;
            Assert.True(Go(b));
            MeshTetrahedralization c = Make(shape);
            c.TargetEdgeLength = double.PositiveInfinity; c.GradingFactor = double.NaN;
            Assert.True(Go(c));
            ulong f = TetRefinementWorkBoundTests.Fingerprint(a);
            Assert.Equal(f, TetRefinementWorkBoundTests.Fingerprint(b));
            Assert.Equal(f, TetRefinementWorkBoundTests.Fingerprint(c));
            Assert.DoesNotContain(a.Counts.Keys, k => k.StartsWith("size_", StringComparison.Ordinal));
        }

        // ---- T7: tet count within a factor 2 of volume / (0.12 h^3), uniform h; the dihedral floor and the targets report hold
        [Theory]
        [InlineData("box", 0.5), InlineData("box", 0.35), InlineData("sphere", 0.5), InlineData("sphere", 0.35)]
        // h below the boundary spacing (alpha): coarser sizes are met by the wrap's own tets, which a size cannot coarsen (17.12)
        public void UniformSize_TetCountIsWithinAFactorTwoOfTheVolumeEstimate(string shape, double hOverAlpha)
        {
            DMesh3 input = TetMeshingStage2Tests.Input(shape);
            double h = hOverAlpha * Diagonal(input) / 12, volume = Invariance.Volume(input);
            MeshTetrahedralization tm = Make(shape);
            tm.TargetEdgeLength = h;
            Assert.True(Go(tm));
            AssertGood(tm);
            double expected = volume / (0.12 * h * h * h);
            double count = tm.Result.Tets.Count;
            Assert.InRange(count, expected / 2, expected * 2);
            Assert.True(tm.Targets.MinDihedralMet, "worst dihedral " + tm.Targets.WorstMinDihedral);
            Assert.Equal(0, Clamped(tm));
            // a finer h gives more tets than the run without a size
            MeshTetrahedralization plain = Make(shape); Assert.True(Go(plain));
            Assert.True(count > plain.Result.Tets.Count);
        }

        [Fact]
        public void UniformSize_FinerSizeGivesMoreTets_AndTheMeanEdgeFollowsH()
        {
            DMesh3 input = TetMeshingStage2Tests.Input("sphere");
            double alpha = Diagonal(input) / 12;
            int last = 0; double lastEdge = double.MaxValue;
            foreach (double f in new[] { 2.0, 1.0, 0.5 }) {
                MeshTetrahedralization tm = Make("sphere"); tm.TargetEdgeLength = f * alpha;
                Assert.True(Go(tm)); AssertGood(tm);
                Assert.True(tm.Result.Tets.Count > last); last = tm.Result.Tets.Count;
                double e = MeanEdge(tm.Result, p => p.Length < 0.6);          // the core, away from the surface's own spacing
                Assert.True(e < lastEdge); lastEdge = e;
                Assert.InRange(e, 0.6 * f * alpha, 1.3 * f * alpha);
            }
        }

        // ---- automatic grading
        [Fact]
        public void Grading_CountsAreMonotoneInTheFactor_AndTheSizeGrowsWithTheDistance()
        {
            var counts = new List<int>();
            MeshTetrahedralization fine = null;
            foreach (double g in new[] { 0.25, 0.5, 1.0, 2.0 }) {
                MeshTetrahedralization tm = Make("sphere");
                tm.GradingFactor = g;
                Assert.True(Go(tm)); AssertGood(tm);
                Assert.True(tm.Targets.MinDihedralMet);
                counts.Add(tm.Result.Tets.Count);
                if (g == 0.25) fine = tm;
            }
            for (int i = 1; i < counts.Count; ++i) Assert.True(counts[i] <= counts[i - 1], string.Join(", ", counts));
            Assert.True(counts[0] > counts[counts.Count - 1], string.Join(", ", counts));
            // the tets grow towards the core
            double core = MeanEdge(fine.Result, p => p.Length < 0.35), shell = MeanEdge(fine.Result, p => p.Length > 0.8);
            Assert.True(core > 1.4 * shell, core + " vs " + shell);
        }

        // ---- user function
        [Fact]
        public void UserFunction_ConstantEqualsTheUniformSize_BitForBit_AndAGradientIsFollowed()
        {
            double h = Diagonal(TetMeshingStage2Tests.Input("box")) / 12;
            MeshTetrahedralization uni = Make("box"); uni.TargetEdgeLength = h; Assert.True(Go(uni));
            MeshTetrahedralization fn = Make("box"); fn.SizeFunction = p => h; Assert.True(Go(fn));
            Assert.Equal(uni.Result.Vertices, fn.Result.Vertices); Assert.Equal(uni.Result.Tets, fn.Result.Tets);
            Assert.Equal(uni.Counts, fn.Counts);

            // fine for x < 0, coarse for x > 0 (box [-0.5, 0.5]^3 or alike)
            MeshTetrahedralization g = Make("box"); g.SizeFunction = p => p.x < 0 ? 0.5 * h : 2 * h;
            Assert.True(Go(g)); AssertGood(g);
            double left = MeanEdge(g.Result, p => p.x < -0.15), right = MeanEdge(g.Result, p => p.x > 0.15);
            Assert.True(left < 0.7 * right, left + " vs " + right);
        }

        [Fact]
        public void SeveralSizes_TheSmallestWins()
        {
            double h = Diagonal(TetMeshingStage2Tests.Input("sphere")) / 12;
            MeshTetrahedralization a = Make("sphere"); a.TargetEdgeLength = h; Assert.True(Go(a));
            MeshTetrahedralization b = Make("sphere"); b.TargetEdgeLength = h; b.SizeFunction = p => 3 * h; Assert.True(Go(b));
            Assert.Equal(a.Result.Tets, b.Result.Tets);
            MeshTetrahedralization c = Make("sphere"); c.TargetEdgeLength = 3 * h; c.SizeFunction = p => h; Assert.True(Go(c));
            Assert.Equal(a.Result.Tets, c.Result.Tets);
        }

        // ---- clamp and termination with any size function
        public static IEnumerable<object[]> Adversarial() => new[] {
            new object[] { "zero" }, new object[] { "negative" }, new object[] { "nan" }, new object[] { "minus infinity" }, new object[] { "plus infinity" },
            new object[] { "denormal" }, new object[] { "tiny" }, new object[] { "mixed" } };

        static Func<Vector3d, double> Function(string kind)
        {
            switch (kind) {
                case "zero": return p => 0;
                case "negative": return p => -1;
                case "nan": return p => double.NaN;
                case "minus infinity": return p => double.NegativeInfinity;
                case "plus infinity": return p => double.PositiveInfinity;
                case "denormal": return p => double.Epsilon;
                case "tiny": return p => 1e-9;
                default: return p => Math.Sin(40 * p.x) > 0.3 ? double.NaN : Math.Sin(40 * p.y) > 0 ? 1e-12 : 0.2;     // oscillating, partly invalid
            }
        }

        static long Clamped(MeshTetrahedralization tm) => tm.Counts.TryGetValue("size_clamped_tets", out long n) ? n : 0;

        [Theory]
        [InlineData("zero"), InlineData("negative"), InlineData("nan"), InlineData("minus infinity"), InlineData("plus infinity")]
        public void InvalidSizeFunction_MeansNoSizeHere_DefaultOutputBitForBit_AndIsCounted(string kind)
        {
            MeshTetrahedralization plain = Make("box"); Assert.True(Go(plain));
            MeshTetrahedralization tm = Make("box");                    // the default alpha of these tests, not a coarse one: no run of minutes
            tm.SizeFunction = Function(kind);
            var sw = Stopwatch.StartNew();
            Assert.True(Go(tm), tm.Status.ToString());
            Assert.True(sw.Elapsed.TotalSeconds < 5);
            Assert.Equal(TetRefinementWorkBoundTests.Fingerprint(plain), TetRefinementWorkBoundTests.Fingerprint(tm));    // vertices, tets, boundary, counts
            Assert.Equal(plain.Result.Tets, tm.Result.Tets);
            Assert.True(tm.InvalidSizeSamples > 0);
            Assert.True(tm.InvalidSizeSamples >= 0.9 * plain.Counts["tet_splits"]);
            Assert.Equal(0, plain.InvalidSizeSamples);
            Assert.Equal(0, Clamped(tm));
            // the counter is reset by the next Compute
            tm.SizeFunction = null; Assert.True(Go(tm));
            Assert.Equal(0, tm.InvalidSizeSamples);
        }

        [Fact]
        public void InvalidSizeFunction_NextToAValidSource_LeavesThatSourceInForce()
        {
            double h = Diagonal(TetMeshingStage2Tests.Input("sphere")) / 12;
            MeshTetrahedralization a = Make("sphere"); a.TargetEdgeLength = h; Assert.True(Go(a));
            MeshTetrahedralization b = Make("sphere"); b.TargetEdgeLength = h; b.SizeFunction = p => double.NaN; Assert.True(Go(b));
            Assert.Equal(a.Result.Tets, b.Result.Tets);
            Assert.True(b.InvalidSizeSamples > 0); Assert.Equal(0, a.InvalidSizeSamples);
        }

        [Theory]
        [InlineData("denormal"), InlineData("tiny"), InlineData("mixed")]
        public void TinyValidSizes_AreClamped_Terminate_AndAreCounted(string kind)
        {
            MeshTetrahedralization tm = Make("box", 4);
            tm.SizeFunction = Function(kind);
            var sw = Stopwatch.StartNew();
            Assert.True(Go(tm), tm.Status.ToString());
            Assert.True(sw.Elapsed.TotalSeconds < 5, sw.Elapsed.TotalSeconds + " s");
            AssertGood(tm);
            Assert.True(tm.Targets.MinDihedralMet);
            Assert.True(Clamped(tm) > 0);
            double hmin = tm.MinSizeFactor * tm.UsedAlpha;
            Assert.True(tm.Result.Tets.Count < 3 * Invariance.Volume(tm.Mesh) / (0.12 * hmin * hmin * hmin), tm.Result.Tets.Count + " tets");
            if (kind != "mixed") {
                MeshTetrahedralization u = Make("box", 4); u.TargetEdgeLength = hmin; Assert.True(Go(u));
                Assert.Equal(u.Result.Tets, tm.Result.Tets);                   // the same as asking for the clamp itself
                Assert.True(Clamped(tm) > 0.9 * tm.Counts["tet_splits"]);
                Assert.Equal(0, tm.InvalidSizeSamples);
            } else Assert.True(tm.InvalidSizeSamples > 0);                      // the NaN part
        }

        [Fact]
        public void SizeBelowTheClamp_IsClampedAndReported_NotAnError_AndTheWorkBoundDoesNotCutIt()
        {
            double alpha = Diagonal(TetMeshingStage2Tests.Input("sphere")) / 12;
            MeshTetrahedralization tm = Make("sphere");
            tm.Improve = false;                                                    // the count is of the refinement's tets, before stage 2
            tm.TargetEdgeLength = 0.05 * alpha;                                    // far below the boundary spacing
            Assert.True(Go(tm)); AssertGood(tm);
            Assert.Equal(tm.Result.Tets.Count, tm.Counts["size_clamped_tets"]);   // every tet had its size clamped
            // the size makes the trials follow the volume: far more than the bound for the wrap's own vertices, and not cut
            Assert.False(tm.Counts.ContainsKey("work_limit_reached"));
            Assert.True(tm.Counts["trial_insertions"] > 48 * tm.Wrap.Triangulation.VertexCount + 2000, tm.Counts["trial_insertions"] + " trials");
        }

        [Fact]
        public void MinSizeFactor_IsTheClamp()
        {
            MeshTetrahedralization tm = Make("sphere", 6);
            tm.TargetEdgeLength = 1e-3; tm.MinSizeFactor = 0.5;
            Assert.True(Go(tm));
            MeshTetrahedralization u = Make("sphere", 6); u.TargetEdgeLength = 0.5 * tm.UsedAlpha; Assert.True(Go(u));
            Assert.Equal(u.Result.Tets, tm.Result.Tets);
            Assert.Equal(0, Clamped(u)); Assert.True(Clamped(tm) > 0);
        }

        [Fact]
        public void TooManyPoints_StopsAtMaxSteinerPoints_Valid()
        {
            MeshTetrahedralization tm = Make("box", 4);
            tm.SizeFunction = p => 1e-9; tm.MinSizeFactor = 1e-3; tm.MaxSteinerPoints = 3000;
            Assert.True(!Go(tm) && tm.Status == MeshTetrahedralization.TetStatus.PointLimitReached);
            AssertGood(tm);
            Assert.Equal(3000, tm.Counts["tet_splits"]);
        }

        [Fact]
        public void MaxCircumradiusWithASize_BothApply()
        {
            double h = Diagonal(TetMeshingStage2Tests.Input("sphere")) / 12;
            MeshTetrahedralization a = Make("sphere"); a.TargetEdgeLength = h; Assert.True(Go(a));
            MeshTetrahedralization b = Make("sphere"); b.TargetEdgeLength = h; b.MaxCircumradius = 0.3 * h; Assert.True(Go(b)); AssertGood(b);
            Assert.True(b.Result.Tets.Count > a.Result.Tets.Count);
            Assert.False(b.Counts.ContainsKey("work_limit_reached"));
        }

        // a tet that is only too large is never peeled away (peeling is for slivers): with no shape criterion left, nothing is even tried
        [Theory]
        [InlineData("box", 16, 0.5), InlineData("torus thin", 16, 0.5)]
        public void TetsThatAreOnlyTooLarge_AreNotPeeled(string shape, double alpha, double hOverAlpha)
        {
            MeshTetrahedralization tm = Make(shape, alpha);
            tm.MaxRadiusEdgeRatio = 1e9; tm.SliverDihedralDegrees = 0; tm.Improve = false;
            tm.TargetEdgeLength = hOverAlpha * Diagonal(tm.Mesh) / alpha;
            Assert.True(Go(tm)); AssertGood(tm);
            Assert.True(tm.Counts["unrefined_bad_tets"] > 0);                       // the large tets at the surface stay
            Assert.Equal(0, tm.Counts["peeled"] + tm.Counts["peeled_in_sweep"] + tm.Counts["peel_refused_input"] + tm.Counts["peel_refused_edge"]);
        }

        [Fact]
        public void SizeAndAShapeRunaway_TheWorkBoundStillStopsIt()
        {
            // radius-edge 1.0 does not end on the sphere (17.10); a size on top must not hide the shape trials from the bound
            double alpha = Diagonal(TetMeshingStage2Tests.Input("sphere")) / 12;
            MeshTetrahedralization tm = Make("sphere"); tm.MaxRadiusEdgeRatio = 1.0; tm.TargetEdgeLength = alpha;
            Go(tm);
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, tm.Status);
            Assert.Equal(1, tm.Counts["work_limit_reached"]);
            AssertGood(tm);
        }

        // ---- reuse, determinism, similarity
        [Fact]
        public void Reuse_ComputingTwiceOnOneInstance_GivesTheSameResult()
        {
            MeshTetrahedralization tm = Make("sphere"); tm.GradingFactor = 0.5; tm.SizeFunction = p => 0.5 + p.Length;
            Assert.True(Go(tm));
            List<Vector3d> v = tm.Result.Vertices.ToList(); List<Index4i> t = tm.Result.Tets.ToList();
            var counts = new Dictionary<string, long>(tm.Counts);
            Assert.True(Go(tm));
            Assert.Equal(v, tm.Result.Vertices); Assert.Equal(t, tm.Result.Tets); Assert.Equal(counts, tm.Counts);
            // switching the size off on the same instance leaves nothing behind
            tm.GradingFactor = 0; tm.SizeFunction = null; Assert.True(Go(tm));
            Assert.DoesNotContain(tm.Counts.Keys, k => k.StartsWith("size_", StringComparison.Ordinal));
        }

        [Fact]
        public void EveryMaxThreads_GivesTheSameSizedResult()
        {
            MeshTetrahedralization serial = Make("box"); serial.MaxThreads = 1; serial.GradingFactor = 0.5; Assert.True(Go(serial));
            foreach (int threads in new[] { 0, 2, 4 }) {
                MeshTetrahedralization tm = Make("box"); tm.MaxThreads = threads; tm.GradingFactor = 0.5; Assert.True(Go(tm));
                Assert.Equal(serial.Result.Vertices, tm.Result.Vertices); Assert.Equal(serial.Result.Tets, tm.Result.Tets); Assert.Equal(serial.Counts, tm.Counts);
            }
            // repeated runs
            MeshTetrahedralization again = Make("box"); again.MaxThreads = 1; again.GradingFactor = 0.5; Assert.True(Go(again));
            Assert.Equal(serial.Result.Tets, again.Result.Tets);
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Similarity_SizesScaleWithTheInput(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = TetMeshingStage2Tests.Input("sphere");
            double h = Diagonal(input) / 12;
            Func<Vector3d, double> f = p => h * (1 + 0.5 * p.Length);
            MeshTetrahedralization a = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, TargetEdgeLength = 1.5 * h, GradingFactor = 0.5, SizeFunction = f };
            Assert.True(Go(a));
            // the same options in the transformed frame: the length scales, the function is the same function of the pulled-back point
            MeshTetrahedralization b = new MeshTetrahedralization(Invariance.Transformed(input, s)) { RelativeAlpha = 12, RescuePasses = 0,
                TargetEdgeLength = s.Scale * 1.5 * h, GradingFactor = 0.5, SizeFunction = p => s.Scale * f(s.Inverse(p)) };
            Assert.True(Go(b));
            AssertGood(b);
            Assert.True(b.Targets.MinDihedralMet);
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            } else {
                Assert.InRange((double)b.Result.Tets.Count, 0.8 * a.Result.Tets.Count, 1.25 * a.Result.Tets.Count);
            }
        }
    }
}
