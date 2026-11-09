using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // The wrap's promise is "the input is strictly inside" (MeshAlphaWrap). The simplification of the wrap
    // (MeshRobustRepair.SimplifyAlphaWrap) is certified within half the offset of the wrap, not of the input, so before
    // it knew the input a collapse could put a triangle across a convex crease of the input. The oracle here is not the
    // pipeline's certification: exact triangle-triangle tests of every result triangle against every input triangle.
    // (public API only: the "New tests on base" job compiles this file against the base branch's library)
    public class AlphaWrapNoContactTests
    {
        static DMesh3 Plates() { return MeshRobustRepairTests.ThinPlates(6); }

        static DMesh3 Wrapped(DMesh3 input, out MeshRobustRepair r)
        {
            r = new MeshRobustRepair(input) { RepairF = (m) => new DMesh3(m), ExactResolve = false, SolidifyResolution = 64, WrapRelativeAlpha = 100, WrapRelativeOffset = 1000 };
            Assert.True(r.Apply());
            Assert.True(r.UsedAlphaWrap && r.AlphaWrapSimplified);
            return r.Mesh;
        }

        /// <summary> result triangles that touch or cross an input triangle (exact predicates), candidates from the input's tree </summary>
        internal static int Crossings(DMesh3 result, DMesh3 input)
        {
            var tree = new DMeshAABBTree3(input, true);
            int n = 0;
            foreach (int tid in result.TriangleIndices()) {
                Triangle3d A = new Triangle3d(result.GetVertex(result.GetTriangle(tid).a), result.GetVertex(result.GetTriangle(tid).b), result.GetVertex(result.GetTriangle(tid).c));
                var ab = new AxisAlignedBox3d(A.V0); ab.Contain(A.V1); ab.Contain(A.V2);
                bool hit = false;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (bx, d) => !hit && bx.Intersects(ab),
                    NextTriangleF = (it) => {
                        if (hit) return;
                        Triangle3d B = new Triangle3d(input.GetVertex(input.GetTriangle(it).a), input.GetVertex(input.GetTriangle(it).b), input.GetVertex(input.GetTriangle(it).c));
                        if (!ExactTriangleIntersection.IsDegenerate(ref B) && ExactTriangleIntersection.Intersects(ref A, ref B)) hit = true;
                    }
                });
                if (hit) n++;
            }
            return n;
        }

        // Slow: a wrap and the exact clean check of its result, with the JIT of the first test of the class
        [Fact, Trait("Category", "Slow")]
        public void SimplifiedWrap_DoesNotCrossTheInput()
        {
            DMesh3 input = Plates();
            DMesh3 simplified = Wrapped(input, out MeshRobustRepair r);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(simplified, true));
            // (the raw wrap keeps the input strictly inside, the class guarantee; the simplification must too)
            Assert.Equal(0, Crossings(simplified, input));
        }

        [Theory]
        [InlineData("scale 2^-10")]
        [InlineData("scale 2^10")]
        [InlineData("rotated, scale 1e-3, utm")]
        public void SimplifiedWrap_DoesNotCrossTheInput_UnderSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Invariance.Transformed(Plates(), s);
            DMesh3 simplified = Wrapped(input, out MeshRobustRepair r);
            Assert.Equal(0, Crossings(simplified, input));
        }

        [Fact]
        public void QualityPass_DoesNotCrossTheInput()
        {
            // the pass flips edges and moves vertices of the simplified wrap: its triangles keep the input strictly inside as well
            DMesh3 input = Plates();
            var r = new MeshRobustRepair(input) { RepairF = (m) => new DMesh3(m), ExactResolve = false, SolidifyResolution = 64, AlphaWrapQualityPass = true, WrapRelativeAlpha = 100, WrapRelativeOffset = 1000 };
            Assert.True(r.Apply());
            Assert.True(r.AlphaWrapQualityImproved);
            Assert.Equal(0, Crossings(r.Mesh, input));
        }

        // Slow: two wraps (about 3.5 s each)
        [Fact, Trait("Category", "Slow")]
        public void SimplifiedWrap_Pow2Scale_GivesTheSameResultBitForBit()
        {
            DMesh3 a = Wrapped(Plates(), out MeshRobustRepair ra);
            Similarity s = Similarity.Pow2Large;
            DMesh3 b = Wrapped(Invariance.Transformed(Plates(), s), out MeshRobustRepair rb);
            Assert.Equal("", Invariance.Difference(a, Invariance.MappedBack(b, s)));
        }
    }
}
