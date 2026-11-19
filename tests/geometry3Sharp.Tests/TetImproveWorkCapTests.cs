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

        [Fact]
        public void TheCap_IsMonotone_InTheSplitsItAllows()
        {
            long prev = -1;
            foreach (double c in new[] { 0.005, Tight, 0.1, 0.5 }) {
                var tm = Make(Thin(), c); Assert.True(tm.Compute());
                long ins = tm.Counts["improve_insertions"];
                Assert.True(ins >= prev, c + ": " + ins + " < " + prev);
                prev = ins;
            }
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
