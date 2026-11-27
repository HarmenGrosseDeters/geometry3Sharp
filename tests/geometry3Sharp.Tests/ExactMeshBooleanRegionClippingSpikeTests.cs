using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Stage 0 of docs/design/COPLANAR_REGION_CLIPPING.md (#1630): with the internal switch SpikeFlatSkip the edges of a triangle that are flat edges of its
    /// region (the neighbour in the same exact plane on the other side) add no constraint to the coplanar triangles of the other meshes. Only the number of
    /// pieces is meaningful at this stage (the stacks are not region aware yet, so the classification is not).
    /// </summary>
    public class ExactMeshBooleanRegionClippingSpikeTests
    {
        static int Pieces(int K, double dx, bool skip, out int skipped)
        {
            DMesh3 a = ExactMeshBooleanCoplanarWalkTests.Cylinder(K);
            DMesh3 b = ExactMeshBooleanCoplanarWalkTests.Shifted(a, dx);
            var bo = new ExactMeshBoolean(new[] { a, b }, ExactMeshBoolean.Operations.Union) { SpikeFlatSkip = skip };
            Assert.True(bo.Compute(), bo.Error);
            skipped = bo.Arrangement.SpikeSkipped;
            return bo.Arrangement.Output.Count;
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.25)]
        public void SkippingFlatEdges_MakesThePiecesGrowLinearly_OnShiftedFans(double dx)
        {
            int s;
            int p8 = Pieces(8, dx, true, out s), p16 = Pieces(16, dx, true, out s), p32 = Pieces(32, dx, true, out s);
            Assert.True(s > 0, "no flat edge was skipped");
            Assert.True(p16 <= 2.3 * p8 && p32 <= 2.3 * p16, "pieces " + p8 + ", " + p16 + ", " + p32);
            int q16 = Pieces(16, dx, false, out s);
            Assert.True(p16 * 4 < q16, "pieces with the switch " + p16 + ", without " + q16);
            Assert.Equal(0, s);
        }

        [Fact]
        public void TheSwitchIsOffByDefault()
        {
            DMesh3 a = ExactMeshBooleanCoplanarWalkTests.Cylinder(8);
            var bo = new ExactMeshBoolean(new[] { a, ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0) }, ExactMeshBoolean.Operations.Union);
            Assert.False(bo.SpikeFlatSkip);
            Assert.True(bo.Compute(), bo.Error);
            Assert.Equal(0, bo.Arrangement.SpikeSkipped);
        }

        static int Skipped(DMesh3 m1)
        {
            var m2 = new DMesh3(); m2.AppendVertex(new Vector3d(0.5, 0.2, 0)); m2.AppendVertex(new Vector3d(1.5, 0.2, 0)); m2.AppendVertex(new Vector3d(0.9, 0.9, 0));
            m2.AppendTriangle(0, 1, 2);
            var arr = new ExactMeshArrangement(new List<DMesh3> { m1, m2 }) { SelfIntersections = true, SpikeFlatSkip = true };
            arr.Compute();
            return arr.SpikeSkipped;
        }

        static DMesh3 Pair(bool fold)
        {
            // a-b = (0,0)-(2,0), c above; d below (a flat edge a-b) or above too (a fold: both on the same side)
            var m = new DMesh3(); m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(2, 0, 0)); m.AppendVertex(new Vector3d(0, 2, 0));
            m.AppendVertex(fold ? new Vector3d(2, 2, 0) : new Vector3d(1, -1, 0));
            m.AppendTriangle(0, 1, 2); m.AppendTriangle(1, 0, 3);
            return m;
        }

        [Fact]
        public void ASharedEdgeWithTheNeighbourOnTheOtherSide_IsSkipped_AFoldIsNot()
        {
            Assert.Equal(1, Skipped(Pair(false)));      // (only the triangle above the line meets the small one)
            Assert.Equal(0, Skipped(Pair(true)));
        }
    }
}
