using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// LaplacianMeshSmoother (docs/ROBUSTNESS_SWEEP.md R2): the system is solved relative to the bounds center, so the
    /// CG tolerance (relative to the right-hand side) does not grow with the distance from the coordinate origin. At
    /// UTM coordinates upstream changed the smoothed area by 7%, and SmoothedHoleFill (which uses it) by 1.4%.
    /// </summary>
    public class LaplacianSmootherOriginTests
    {
        /// <summary> a sphere with a hole, smoothed with soft interior and hard boundary constraints </summary>
        static DMesh3 Smoothed(Similarity s, bool multipleRHS = false, bool twice = false)
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6);
            int top = m.VertexIndices().OrderByDescending(v => m.GetVertex(v).z).ThenBy(v => v).First();
            foreach (int t in m.VtxTrianglesItr(top).ToList()) m.RemoveTriangle(t, true, false);
            m = Invariance.Transformed(m, s);
            var sm = new LaplacianMeshSmoother(m);
            Func<Vector3d[], bool> solve = multipleRHS ? (Func<Vector3d[], bool>)sm.SolveMultipleRHS : sm.SolveMultipleCG;
            foreach (int v in m.VertexIndices())
                sm.SetConstraint(v, m.GetVertex(v), m.IsBoundaryVertex(v) ? 100 : 0.5, m.IsBoundaryVertex(v));
            Vector3d[] result = new Vector3d[m.MaxVertexID];
            Assert.True(solve(result));
            if (twice) {
                Vector3d[] again = new Vector3d[m.MaxVertexID];
                Assert.True(solve(again));
                Assert.True(result.SequenceEqual(again), "second solve on the same instance differs");
            }
            foreach (int v in m.VertexIndices()) m.SetVertex(v, result[v]);
            return Invariance.MappedBack(m, s);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Smooth_SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (bool rhs in new[] { false, true }) {
                DMesh3 a = Smoothed(Similarity.Identity, rhs), b = Smoothed(s, rhs);
                double max = a.VertexIndices().Max(v => a.GetVertex(v).Distance(b.GetVertex(v)));
                if (s.IsExact)
                    Assert.True(max == 0, $"{name}: not bit identical ({max:E2})");
                // (CG stops at 1e-8 of the right-hand side of each axis; with the condition number of the smoothing system
                // that leaves about 5e-5 of the size, which a rotation redistributes between the axes: an upstream
                // limit of the solver, not of the placement. Translations and scales give 1e-8 plus the rounding.)
                double tol = (s.IsRotated ? 2e-4 : 1e-6) + 100 * s.Resolution(1);
                Assert.True(max < tol, $"{name}, multipleRHS={rhs}: vertices move by {max:E2} (tolerance {tol:E2})");
            }
        }

        [Fact]
        public void Smooth_ReuseAndRepeatedRuns_BitIdentical()
        {
            DMesh3 a = Smoothed(Similarity.Utm, twice: true);
            for (int run = 0; run < 3; ++run)
                Assert.Equal("", Invariance.Difference(a, Smoothed(Similarity.Utm)));
        }

        [Fact]
        public void SmoothedHoleFill_SameAreaAtUtm()
        {
            double Area(Similarity s)
            {
                DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6);
                int top = m.VertexIndices().OrderByDescending(v => m.GetVertex(v).z).ThenBy(v => v).First();
                foreach (int t in m.VtxTrianglesItr(top).ToList()) m.RemoveTriangle(t, true, false);
                m = Invariance.Transformed(m, s);
                var fill = new SmoothedHoleFill(m, new MeshBoundaryLoops(m).Loops[0]) { TargetEdgeLength = 0.15 };
                Assert.True(fill.Apply());
                ResultChecks.AssertClosed(m);
                return Invariance.Area(m);
            }
            double a0 = Area(Similarity.Identity), a1 = Area(Similarity.Utm);
            Assert.True(Math.Abs(a0 - a1) < 1e-5 * a0, $"area {a0} at the origin, {a1} at UTM");
        }
    }
}
