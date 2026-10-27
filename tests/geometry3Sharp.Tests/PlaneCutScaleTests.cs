using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshPlaneCut (docs/ROBUSTNESS_SWEEP.md R5): the degenerate-edge collapse and the on-plane test are relative to
    /// the mesh. Upstream collapsed cut edges shorter than an absolute 1e-6, so a small mesh lost its cut loop.
    /// </summary>
    public class PlaneCutScaleTests
    {
        static readonly Vector3d Normal = new Vector3d(1, 2, 3).Normalized;

        static MeshPlaneCut Cut(Similarity s, out DMesh3 mesh, int n = 6, double offset = 0.1)
        {
            mesh = Invariance.Transformed(Defects.Sphere(Vector3d.Zero, 1, n), s);
            var cut = new MeshPlaneCut(mesh, s.Apply(new Vector3d(offset, 0.05, 0.02)), s.ApplyDirection(Normal));
            Assert.True(cut.Cut());
            Assert.True(cut.FillHoles());
            return cut;
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Cut_SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            MeshPlaneCut c0 = Cut(Similarity.Identity, out DMesh3 m0), c1 = Cut(s, out DMesh3 m1);
            Assert.Equal(c0.CutLoops.Count, c1.CutLoops.Count);
            Assert.Equal(m0.TriangleCount, m1.TriangleCount);
            ResultChecks.AssertClosed(m1);
            DMesh3 back = Invariance.MappedBack(m1, s);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(m0, back));
            else
                Assert.True(Invariance.VertexDistance(m0, back) <= s.DistanceResolution(2), name);
        }

        [Theory]
        [InlineData(-24)]
        [InlineData(-40)]
        [InlineData(30)]
        public void Cut_ExtremePowerOfTwoScales_SameMesh(int exponent)
        {
            var s = new Similarity("2^" + exponent, Math.Pow(2, exponent), Matrix3d.Identity, Vector3d.Zero);
            MeshPlaneCut c0 = Cut(Similarity.Identity, out DMesh3 m0), c1 = Cut(s, out DMesh3 m1);
            Assert.Equal(1, c1.CutLoops.Count);
            ResultChecks.AssertClosed(m1);
            Assert.Equal("", Invariance.Difference(m0, Invariance.MappedBack(m1, s)));
            Assert.Equal(c0.EffectiveDegenerateEdgeTol * s.Scale, c1.EffectiveDegenerateEdgeTol);
        }

        [Fact]
        public void Cut_ExplicitAbsoluteTolerance_IsUsedAsGiven()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6);
            var cut = new MeshPlaneCut(m, Vector3d.Zero, Normal) { DegenerateEdgeTol = 1e-3 };
            cut.Cut();
            Assert.Equal(1e-3, cut.EffectiveDegenerateEdgeTol);
        }

        [Fact]
        public void Cut_PlaneThroughVertices_AtUtm_NoSlivers()
        {
            // a plane through vertices of the mesh at UTM coordinates: the signed distances of those vertices are
            // rounding noise (about 1e-10), larger than MathUtil.Epsilon; they must count as on the plane, not split
            // edges at t ~ 1e-10 into slivers
            foreach (Similarity s in Similarity.All()) {
                DMesh3 box = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, 0.5 * Vector3d.One), EdgeVertices = 5 }.Generate().MakeDMesh();
                DMesh3 m = Invariance.Transformed(box, s);
                var cut = new MeshPlaneCut(m, s.Apply(Vector3d.Zero), s.ApplyDirection(Vector3d.AxisX)) { CollapseDegenerateEdgesOnCut = false };
                cut.Cut();
                MeshQueries.EdgeLengthStats(m, out double min, out double max, out double avg);
                // (the plane runs along grid lines: no edge crosses it, so no edge gets shorter than the grid's)
                Assert.True(min > 0.5 * max, $"{s}: shortest edge {min:E2}, longest {max:E2}");
            }
        }
    }
}
