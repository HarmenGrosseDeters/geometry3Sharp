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

        static double ShortestLoopEdge(DMesh3 m, MeshPlaneCut cut) =>
            cut.CutLoops.SelectMany(l => l.Edges).Min(e => m.GetVertex(m.GetEdgeV(e).a).Distance(m.GetVertex(m.GetEdgeV(e).b)));

        [Fact]
        public void Cut_ExplicitAbsoluteTolerance_IsUsedAsGiven()
        {
            // a plane 2e-5 from a vertex: the short cut edges near it stay with the relative default (3.5e-6 here) and
            // are collapsed with an explicit DegenerateEdgeTol of 1e-3
            DMesh3 probe = Defects.Sphere(Vector3d.Zero, 1, 6);
            Vector3d v = probe.GetVertex(probe.VertexIndices().First(x => Math.Abs(probe.GetVertex(x).Dot(Normal)) < 0.5));
            Vector3d origin = v + 2e-5 * Normal;
            foreach (double tol in new[] { MathUtil.ZeroTolerancef, 1e-3 }) {
                DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6);
                var cut = new MeshPlaneCut(m, origin, Normal) { DegenerateEdgeTol = tol };
                cut.Cut();
                double shortest = ShortestLoopEdge(m, cut);
                if (tol == MathUtil.ZeroTolerancef) {
                    Assert.Equal(1e-6 * probe.CachedBounds.DiagonalLength, cut.EffectiveDegenerateEdgeTol, 15);
                    Assert.True(shortest < 1e-3, $"default: shortest cut edge {shortest:E2}");
                } else {
                    Assert.Equal(1e-3, cut.EffectiveDegenerateEdgeTol);
                    Assert.True(shortest >= 1e-3, $"explicit: shortest cut edge {shortest:E2}");
                }
            }
        }

        [Fact]
        public void Cut_UnusedFarVertex_DoesNotWidenTheTolerances()
        {
            // a millimetre mesh with an unused vertex a kilometre away: the tolerances come from the vertices with edges
            Similarity s = Similarity.Millimetre;
            MeshPlaneCut c0 = Cut(s, out DMesh3 m0);
            DMesh3 m1 = Invariance.Transformed(Defects.Sphere(Vector3d.Zero, 1, 6), s);
            m1.AppendVertex(new Vector3d(1e3, 0, 0));
            var c1 = new MeshPlaneCut(m1, s.Apply(new Vector3d(0.1, 0.05, 0.02)), s.ApplyDirection(Normal));
            c1.Cut();
            Assert.Equal(c0.EffectiveDegenerateEdgeTol, c1.EffectiveDegenerateEdgeTol);
            Assert.Equal(1, c1.CutLoops.Count);
        }

        [Fact]
        public void Cut_PlaneThroughVertices_AnySimilarity_SameCut()
        {
            // a plane through a column of grid vertices: in UTM or rotated placements their signed distances are rounding
            // noise (up to about 1e-9), above MathUtil.Epsilon. They must count as on the plane: no slivers at t ~ 1e-10,
            // and no on-plane vertex with a positive noise sign removed with its whole one-ring
            DMesh3 box0 = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, 0.5 * Vector3d.One), EdgeVertices = 5 }.Generate().MakeDMesh();
            int triangles0 = -1; double area0 = 0;
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = Invariance.Transformed(box0, s);
                var cut = new MeshPlaneCut(m, s.Apply(Vector3d.Zero), s.ApplyDirection(Vector3d.AxisX)) { CollapseDegenerateEdgesOnCut = false };
                cut.Cut();
                Assert.Equal(1, cut.CutLoops.Count);
                Assert.False(cut.FoundOpenSpans, s.Name);
                MeshQueries.EdgeLengthStats(m, out double min, out double max, out double avg);
                // (the plane runs along grid lines: no edge crosses it, so no edge gets shorter than the grid's)
                Assert.True(min > 0.5 * max, $"{s}: shortest edge {min:E2}, longest {max:E2}");
                Assert.True(cut.FillHoles());
                ResultChecks.AssertClosed(m, s.Name);
                double area = Invariance.Area(m) / (s.Scale * s.Scale);
                if (triangles0 < 0) { triangles0 = m.TriangleCount; area0 = area; }
                Assert.Equal(triangles0, m.TriangleCount);
                Assert.True(Math.Abs(area - area0) < 1e-6 * area0, $"{s}: area {area} vs {area0}");
            }
        }
    }
}
