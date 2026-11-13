using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The retry of SimplifyWithin in two stages (issue #747): first with Reducer.SampleDeviation (cheap: a few nearest-point
    /// queries per collapse), then, if that is not certified either, with Reducer.MaxDeviation (certified per collapse, 10 to
    /// 30 times slower). On the 5 uncertified wraps of the 800 sample the first stage certified all.
    /// </summary>
    public class SimplifyWithinSampledRetryTests
    {
        static DMesh3 Simplify(DMesh3 m, double q, double d, bool sampled, int slabs, int threads, Dictionary<string, long> phases = null)
        {
            return MeshRobustRepair.SimplifyWithin(m, q, d, true, () => false, out int sub, MeshRobustRepair.WrapSimplifyMinQuality, true, slabs, threads,
                phases, null, true, sampled);
        }

        [Fact]
        public void SampledStage_Certifies_TheBoundedStageOnlyWithoutIt()
        {
            DMesh3 sphere = SimplifyWithinRetryTests.BumpySphere(16);
            foreach (int slabs in new[] { 0, MeshRobustRepair.WrapSimplifySlabs }) {
                var phases = new Dictionary<string, long>();
                DMesh3 m = Simplify(sphere, 0.01, 1e-3, true, slabs, 0, phases);
                SimplifyWithinRetryTests.AssertCertified(m, sphere, 1e-3, $"sampled, slabs {slabs}", 0.99);
                Assert.True(phases.ContainsKey("retry") && !phases.ContainsKey("retry2"), $"slabs {slabs}: " + string.Join(",", phases.Keys));
                // without the sampled stage: the bounded one, certified too
                phases.Clear();
                DMesh3 b = Simplify(sphere, 0.01, 1e-3, false, slabs, 0, phases);
                SimplifyWithinRetryTests.AssertCertified(b, sphere, 1e-3, $"bounded, slabs {slabs}", 0.99);
                Assert.True(phases.ContainsKey("retry2") && !phases.ContainsKey("retry"), $"slabs {slabs}: " + string.Join(",", phases.Keys));
            }
        }

        // the sampled stage on slabs: parallel equals serial (one thread), and repeated runs are equal
        [Fact]
        public void SampledStage_Deterministic()
        {
            DMesh3 sphere = SimplifyWithinRetryTests.BumpySphere(16);
            DMesh3 first = Simplify(sphere, 0.01, 1e-3, true, 8, 0);
            SimplifyWithinRetryTests.AssertCertified(first, sphere, 1e-3, "sampled, 8 slabs", 0.99);
            string h = AlphaWrapManifoldStepTests.Hash(first);
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(Simplify(sphere, 0.01, 1e-3, true, 8, 1)));
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(Simplify(sphere, 0.01, 1e-3, true, 8, 0)));
        }

        // Reducer.SampleDeviation itself: every triangle of the result was made by a collapse that checked its sample points, or
        // is an input triangle, so all sample points of the result lie within the distance of the input; and it reduces less
        // than without. Invariance over Similarity.All(): the 2^k scales give the same result, scaled, bit for bit
        [Fact]
        public void Reducer_SampleDeviation_KeepsTheSamplesNear_Invariance()
        {
            DMesh3 sphere = SimplifyWithinRetryTests.BumpySphere(16);
            Func<Similarity, bool, DMesh3> reduce = (s, guarded) => {
                DMesh3 m = Invariance.Transformed(sphere, s);
                var r = new Reducer(m) { AreaWeightedQuadrics = false, PreventSelfIntersections = true };
                if (guarded) { r.SampleDeviation = s.ApplyLength(2e-3); r.SampleReference = new DMeshAABBTree3(Invariance.Transformed(sphere, s), true); }
                r.ReduceToMaxQuadricError(s.ApplyLength(0.05) * s.ApplyLength(0.05));
                return m;
            };
            DMesh3 b = reduce(Similarity.Identity, true);
            int unguarded = reduce(Similarity.Identity, false).TriangleCount;
            // it reduces (a guard that refuses every collapse fails here), but less than without the guard
            Assert.True(b.TriangleCount < sphere.TriangleCount && b.TriangleCount > unguarded,
                $"guarded reduces less: input {sphere.TriangleCount}, guarded {b.TriangleCount}, unguarded {unguarded}");
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = reduce(s, true);
                var tree = new DMeshAABBTree3(Invariance.Transformed(sphere, s), true);
                double lim = s.ApplyLength(2e-3) * (1 + 1e-9);
                Vector3d a = Vector3d.Zero, bb = Vector3d.Zero, c = Vector3d.Zero;
                foreach (int t in m.TriangleIndices()) {
                    m.GetTriVertices(t, ref a, ref bb, ref c);
                    foreach (Vector3d p in new[] { (a + bb + c) / 3, 0.5 * (a + bb), 0.5 * (bb + c), 0.5 * (c + a) }) {
                        tree.FindNearestTriangle(p, out double d2);
                        Assert.True(Math.Sqrt(d2) <= lim, $"{s}: sample {Math.Sqrt(d2) / s.Scale} from the input");
                    }
                }
                if (s.IsExact) {
                    Assert.Equal(b.TriangleCount, m.TriangleCount);
                    var bv = b.VertexIndices().ToList(); var mv = m.VertexIndices().ToList();
                    for (int i = 0; i < bv.Count; ++i) Assert.Equal(s.Apply(b.GetVertex(bv[i])), m.GetVertex(mv[i]));
                }
            }
        }
    }
}
