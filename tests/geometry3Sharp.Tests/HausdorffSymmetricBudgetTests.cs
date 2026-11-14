using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #740: MaxSubdivisions counted both directions of the symmetric distance together. A first direction that used it up left the
    /// second none: it reported the bound of its initial triangles, and the symmetric upper bound was looser than both one-sided
    /// ones (0.3136 against 0.2255 and 0.2267 on the folded spheres of #738). The first direction now gets half of the budget, the
    /// second the rest.
    /// </summary>
    public class HausdorffSymmetricBudgetTests
    {
        static DMesh3 Bumpy(int seed, int ev)
        {
            var rnd = new Random(seed);
            DMesh3 m = new Sphere3Generator_NormalizedCube { EdgeVertices = ev, Radius = 1 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, m.GetVertex(v) * (1 + 0.05 * rnd.NextDouble()));
            return m;
        }

        [Theory]
        [InlineData(400)] [InlineData(2001)] [InlineData(8000)]
        public void SymmetricUpper_NotLooserThanTheOneSidedRunsWithHalfTheBudget(int budget)
        {
            DMesh3 a = Bumpy(5, 12), b = Bumpy(6, 8);
            var sym = new MeshHausdorffDistance(a, b) { Tolerance = 1e-9, MaxSubdivisions = budget };
            var ab = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = 1e-9, MaxSubdivisions = budget / 2 };
            var ba = new MeshHausdorffDistance(b, a) { Symmetric = false, Tolerance = 1e-9, MaxSubdivisions = budget / 2 };
            Assert.True(sym.Compute() && ab.Compute() && ba.Compute());
            double oneSided = Math.Max(ab.UpperBound, ba.UpperBound);
            Assert.True(sym.UpperBound <= oneSided * (1 + 1e-12), $"budget {budget}: symmetric upper {sym.UpperBound:R} above the one-sided {ab.UpperBound:R}, {ba.UpperBound:R}");
            Assert.True(sym.LowerBound >= Math.Max(ab.LowerBound, ba.LowerBound) * (1 - 1e-12) - 1e-15);
            Assert.True(sym.Subdivisions <= budget + 2 * 255);
        }

        /// <summary> under every similarity, with the budget split: the result of the plain meshes mapped by it, bit for bit at the
        /// exact similarities (2^k scales); otherwise the intervals overlap up to the coordinates' rounding </summary>
        [Fact]
        public void SplitBudget_UnderEverySimilarity()
        {
            DMesh3 a = Bumpy(5, 12), b = Bumpy(6, 8);
            var h0 = new MeshHausdorffDistance(a, b) { Tolerance = 1e-9, MaxSubdivisions = 400 };
            Assert.True(h0.Compute());
            foreach (Similarity s in Similarity.All()) {
                DMesh3 ta = new DMesh3(a), tb = new DMesh3(b);
                MeshTransforms.PerVertexTransform(ta, s.Apply);
                MeshTransforms.PerVertexTransform(tb, s.Apply);
                var h = new MeshHausdorffDistance(ta, tb) { Tolerance = 1e-9 * s.Scale, MaxSubdivisions = 400 };
                Assert.True(h.Compute(), s.Name);
                if (s.IsExact) {
                    Assert.Equal(h0.LowerBound, h.LowerBound / s.Scale);
                    Assert.Equal(h0.UpperBound, h.UpperBound / s.Scale);
                    Assert.Equal(h0.Subdivisions, h.Subdivisions);
                } else {
                    double eps = 100 * s.Resolution(2);
                    Assert.True(h.LowerBound / s.Scale <= h0.UpperBound + eps && h0.LowerBound <= h.UpperBound / s.Scale + eps, s.Name);
                }
            }
        }

        /// <summary> Compute twice on one instance: the split budget starts again from 0, the same result bit for bit </summary>
        [Fact]
        public void SplitBudget_Reuse()
        {
            DMesh3 a = Bumpy(5, 12), b = Bumpy(6, 8);
            var h = new MeshHausdorffDistance(a, b) { Tolerance = 1e-9, MaxSubdivisions = 400 };
            Assert.True(h.Compute());
            double lo = h.LowerBound, hi = h.UpperBound; int n = h.Subdivisions;
            Assert.False(h.Converged);
            Assert.True(lo > 0 && lo <= hi && !double.IsInfinity(hi));
            Assert.True(h.Compute());
            Assert.Equal(lo, h.LowerBound);
            Assert.Equal(hi, h.UpperBound);
            Assert.Equal(n, h.Subdivisions);
        }

        /// <summary> a converging run is unchanged: the first direction needs less than half of the budget </summary>
        [Fact]
        public void ConvergingRun_Unchanged()
        {
            DMesh3 a = Bumpy(5, 12), b = Bumpy(6, 8);
            var h = new MeshHausdorffDistance(a, b);
            Assert.True(h.Compute() && h.Converged);
            var capped = new MeshHausdorffDistance(a, b) { MaxSubdivisions = 2 * h.Subdivisions + 600 };
            Assert.True(capped.Compute() && capped.Converged);
            Assert.Equal(h.LowerBound, capped.LowerBound);
            Assert.Equal(h.UpperBound, capped.UpperBound);
            Assert.Equal(h.Subdivisions, capped.Subdivisions);
        }
    }
}
