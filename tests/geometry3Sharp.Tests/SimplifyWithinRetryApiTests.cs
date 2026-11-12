using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // (its own file: it needs the new retryWithDeviation parameter, so SimplifyWithinRetryTests still compiles against the base library)
    public class SimplifyWithinRetryApiTests
    {
        static DMesh3 Simplify(DMesh3 m, double q, double d, bool retry, int slabs, int threads, System.Collections.Generic.Dictionary<string, long> phases = null)
        {
            return MeshRobustRepair.SimplifyWithin(m, q, d, true, () => false, out int sub, MeshRobustRepair.WrapSimplifyMinQuality, true, slabs, threads,
                phases, null, retry);
        }

        [Fact]
        public void Retry_OnlyWhenNotCertified_AndOffGivesTheOldResult()
        {
            DMesh3 sphere = SimplifyWithinRetryTests.BumpySphere(16);
            Assert.Null(Simplify(sphere, 0.01, 1e-3, false, 0, 0));
            var phases = new System.Collections.Generic.Dictionary<string, long>();
            Assert.NotNull(Simplify(sphere, 0.01, 1e-3, true, 0, 0, phases));
            Assert.True(phases.ContainsKey("retry"), "retried");
            // certified the first time: no retry, the same mesh with and without it
            phases.Clear();
            DMesh3 a = Simplify(sphere, 0.01, 0.01, true, 0, 0, phases), b = Simplify(sphere, 0.01, 0.01, false, 0, 0);
            Assert.False(phases.ContainsKey("retry"));
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(b), AlphaWrapManifoldStepTests.Hash(a));
        }

        // the retry on slabs: parallel equals serial (one thread), and repeated runs are equal
        [Fact]
        public void Retry_Deterministic()
        {
            DMesh3 sphere = SimplifyWithinRetryTests.BumpySphere(16);
            string h = AlphaWrapManifoldStepTests.Hash(Simplify(sphere, 0.01, 1e-3, true, 8, 0));
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(Simplify(sphere, 0.01, 1e-3, true, 8, 1)));
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(Simplify(sphere, 0.01, 1e-3, true, 8, 0)));
        }

        // invariance over Similarity.All(): certified on every transformed input; the 2^k scales give the same result, scaled, bit for bit
        [Fact]
        public void Retry_Invariance()
        {
            DMesh3 sphere = SimplifyWithinRetryTests.BumpySphere(16);
            DMesh3 b = Simplify(sphere, 0.01, 1e-3, true, 0, 0);
            Assert.NotNull(b);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 t = Invariance.Transformed(sphere, s);
                DMesh3 m = Simplify(t, s.ApplyLength(0.01), s.ApplyLength(1e-3), true, 0, 0);
                SimplifyWithinRetryTests.AssertCertified(m, t, s.ApplyLength(1e-3), s.Name, 0.99);
                if (s.IsExact) {
                    Assert.Equal(b.TriangleCount, m.TriangleCount);
                    var bv = b.VertexIndices().ToList(); var mv = m.VertexIndices().ToList();
                    Assert.Equal(bv.Count, mv.Count);
                    for (int i = 0; i < bv.Count; ++i) Assert.Equal(s.Apply(b.GetVertex(bv[i])), m.GetVertex(mv[i]));
                }
            }
        }
    }
}
