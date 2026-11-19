using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> The opt-in work cap of the improvement (MaxSplitAttemptsPerVertex, #1154): a count of split attempts, never wall-clock; off = bit for bit as before. </summary>
    public class TetImproveWorkCapTests
    {
        static MeshTetrahedralization Make(DMesh3 input, double cap) => new MeshTetrahedralization(input) { RelativeAlpha = TetMeshingStage2SplitTests.MechanismAlpha, MaxSplitAttemptsPerVertex = cap };
        static DMesh3 Thin() => TetMeshingStage2Tests.Input("torus thin");
        const double Tight = 0.02;      // a few dozen attempts on the thin torus: far below what it uses

        [Fact]
        public void Off_IsBitForBit_AndAGenerousCapDoesNotAct()
        {
            var a = Make(Thin(), 0); Assert.True(a.Compute());
            var generous = Make(Thin(), 1e6); Assert.True(generous.Compute());
            Assert.Equal(a.Result.Vertices, generous.Result.Vertices);
            Assert.Equal(a.Result.Tets, generous.Result.Tets);
            Assert.Equal(a.Counts, generous.Counts);
            Assert.False(a.Counts.ContainsKey("improve_work_cap_reached"));
            Assert.True(a.Counts["improve_insertions"] > 0);
            foreach (double off in new[] { -1, double.NaN, double.PositiveInfinity }) {
                var b = Make(Thin(), off); Assert.True(b.Compute());
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                Assert.Equal(a.Counts, b.Counts);
            }
        }

        [Fact]
        public void TheCap_Acts_IsReported_AndTheRunStaysValid()
        {
            var off = Make(Thin(), 0); Assert.True(off.Compute());
            var capped = Make(Thin(), Tight);
            Assert.True(capped.Compute(), capped.Status.ToString());
            Assert.Equal(1, capped.Counts["improve_work_cap_reached"]);
            TetMeshingStage2Tests.Invariants(capped);
            Assert.True(capped.Counts["improve_insertions"] < off.Counts["improve_insertions"], capped.Counts["improve_insertions"] + " vs " + off.Counts["improve_insertions"]);
            Assert.NotEqual(off.Result.Tets, capped.Result.Tets);
        }

        // an improver on the stage-1 mesh, as the mesher sets it up (splits on, no contraction), with the given cap in attempts
        static TetMeshImprover Improver(long cap)
        {
            var tm = new MeshTetrahedralization(Thin()) { Improve = false, RelativeAlpha = TetMeshingStage2SplitTests.MechanismAlpha };
            Assert.True(tm.Compute());
            var imp = new TetMeshImprover(tm.Result) { TargetDegrees = 10, MaxPasses = 50, RescuePasses = 150, Insertion = true, Contraction = true, MaxSplitAttempts = cap,
                InputTree = new DMeshAABBTree3(Thin(), true), BoundarySlideLimit = tm.BoundarySlideFactor * tm.UsedOffset };
            imp.Run();
            return imp;
        }

        [Fact]
        public void TheBudget_IsExact()
        {
            TetMeshImprover free = Improver(0);
            long used = free.SplitAttempts;
            Assert.True(used > 10, used.ToString());
            Assert.False(free.Counts.ContainsKey("work_cap_reached"));
            // exactly k attempts are made, then the cap is reported (the runs are identical up to the k-th attempt)
            foreach (long k in new[] { 1L, 5L, used / 2 }) {
                TetMeshImprover c = Improver(k);
                Assert.Equal(k, c.SplitAttempts);
                Assert.Equal(1, c.Counts["work_cap_reached"]);
            }
            // a budget above what the run uses changes nothing
            TetMeshImprover roomy = Improver(used + 1);
            Assert.False(roomy.Counts.ContainsKey("work_cap_reached"));
            Assert.Equal(used, roomy.SplitAttempts);
            Assert.Equal(free.Result().Tets, roomy.Result().Tets);
            Assert.Equal(free.Result().Vertices, roomy.Result().Vertices);
        }

        [Fact]
        public void TheOption_OverflowAndTinyValues()
        {
            var off = Make(Thin(), 0); Assert.True(off.Compute());
            foreach (double huge in new[] { 1e308, double.MaxValue }) {                   // the product overflows: no cap in effect
                var tm = Make(Thin(), huge); Assert.True(tm.Compute());
                Assert.Equal(off.Result.Tets, tm.Result.Tets);
                Assert.False(tm.Counts.ContainsKey("improve_work_cap_reached"));
            }
            var tiny = Make(Thin(), 1e-300); Assert.True(tiny.Compute());                  // ceil gives one attempt: the cap is on
            Assert.Equal(1, tiny.Counts["improve_work_cap_reached"]);
            var neg = Make(Thin(), double.NegativeInfinity); Assert.True(neg.Compute());
            Assert.Equal(off.Result.Tets, neg.Result.Tets);
        }

        [Fact]
        public void ReusingTheInstance_GivesTheSameResult()
        {
            var tm = Make(Thin(), Tight);
            Assert.True(tm.Compute());
            TetMesh3 first = tm.Result; var counts = new Dictionary<string, long>(tm.Counts);
            Assert.True(tm.Compute());
            Assert.Equal(first.Vertices, tm.Result.Vertices);
            Assert.Equal(first.Tets, tm.Result.Tets);
            Assert.Equal(counts, tm.Counts);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void TheCap_IsInvariantUnderEverySimilarity(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = Thin();
            var a = Make(input, Tight); Assert.True(a.Compute());
            var b = Make(Invariance.Transformed(input, s), Tight); Assert.True(b.Compute());
            Assert.Equal(1, a.Counts["improve_work_cap_reached"]);
            TetMeshingStage2Tests.Invariants(b);
            if (s.IsExact) {                                                             // 2^k scales: the same attempts, the same result, bit for bit
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            }
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();
    }
}
