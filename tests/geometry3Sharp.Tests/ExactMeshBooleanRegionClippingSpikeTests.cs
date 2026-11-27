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

        // ---- stage 1: the regions

        static int[] Regions(params DMesh3[] meshes)
        {
            var arr = new ExactMeshArrangement(new List<DMesh3>(meshes)) { SelfIntersections = true, SpikeFlatSkip = true };
            arr.Compute();
            return arr.RegionOf;
        }

        static DMesh3 Far() { var m = new DMesh3(); m.AppendVertex(new Vector3d(100, 100, 100)); m.AppendVertex(new Vector3d(101, 100, 100)); m.AppendVertex(new Vector3d(100, 101, 100)); m.AppendTriangle(0, 1, 2); return m; }

        static int Count(int[] r, int from, int to)
        {
            var roots = new HashSet<int>();
            for (int i = from; i < to; ++i) roots.Add(r[i]);
            return roots.Count;
        }

        static DMesh3 Grid(int n, double z = 0, double bendFrom = double.MaxValue)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d(i, j, i >= bendFrom ? z + (i - bendFrom) : z));
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
            }
            return m;
        }

        [Fact]
        public void AGridInOnePlane_IsOneRegion_AndABentGridTwo()
        {
            int[] r = Regions(Grid(4), Far());
            Assert.Equal(1, Count(r, 0, 32));
            Assert.Equal(0, r[31]);
            Assert.Equal(32, r[32]);        // the far triangle: a region of its own
            Assert.Equal(2, Count(Regions(Grid(4, 0, 2), Far()), 0, 32));
        }

        [Fact]
        public void ABox_HasSixRegionsOfTwoTriangles_AndTwoMeshesNeverShareOne()
        {
            var box = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1))) }.Generate().MakeDMesh();
            int[] r = Regions(box, Far());
            Assert.Equal(6, Count(r, 0, 12));
            foreach (int root in new HashSet<int>(r[0..12])) Assert.Equal(2, System.Linq.Enumerable.Count(r[0..12], x => x == root));
            int[] two = Regions(Grid(2), Grid(2));
            Assert.Equal(1, Count(two, 0, 8));
            Assert.Equal(1, Count(two, 8, 16));
            Assert.Equal(8, two[8]);
        }

        [Fact]
        public void AFlippedNeighbour_IsNotFlat()
        {
            var flipped = new DMesh3();
            flipped.AppendVertex(new Vector3d(0, 0, 0)); flipped.AppendVertex(new Vector3d(1, 0, 0)); flipped.AppendVertex(new Vector3d(0, 1, 0)); flipped.AppendVertex(new Vector3d(1, 1, 0));
            flipped.AppendTriangle(0, 1, 2);
            flipped.AppendTriangle(1, 3, 2);                      // 1->2 in the first, 2->1 in the second: consistent
            int[] ok = Regions(flipped, Far());
            Assert.Equal(1, Count(ok, 0, 2));                   // consistent: one region
            var bad = new DMesh3();
            bad.AppendVertex(new Vector3d(0, 0, 0)); bad.AppendVertex(new Vector3d(1, 0, 0)); bad.AppendVertex(new Vector3d(0, 1, 0)); bad.AppendVertex(new Vector3d(1, 1, 0));
            bad.AppendTriangle(0, 1, 2);
            bad.AppendTriangle(1, 2, 3);                          // 1->2 in both: a flipped neighbour
            Assert.Equal(2, Count(Regions(bad, Far()), 0, 2));
        }

        [Fact]
        public void ACylinder_HasTwoCapsAndOneRegionPerSideQuad()
        {
            DMesh3 c = ExactMeshBooleanCoplanarWalkTests.Cylinder(8);
            int n = (c.VertexCount - 2) / 2;
            int[] r = Regions(c, Far());
            Assert.Equal(2 + n, Count(r, 0, c.TriangleCount));
            // the regions are a function of the mesh alone: again, and with the other order of the meshes' triangles
            Assert.Equal(r, Regions(c, Far()));
        }

        [Fact]
        public void TheRegions_AreNullWithTheSwitchOff()
        {
            var arr = new ExactMeshArrangement(new List<DMesh3> { Grid(2), Far() }) { SelfIntersections = true };
            arr.Compute();
            Assert.Null(arr.RegionOf);
        }
    }
}
