using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> Stage 1 of docs/design/COPLANAR_REGION_CLIPPING.md (#1630): the regions of the input triangles (ExactMeshArrangement.RegionOf) </summary>
    public class ExactMeshArrangementRegionTests
    {
        static int[] Regions(params DMesh3[] meshes)
        {
            var arr = new ExactMeshArrangement(new List<DMesh3>(meshes)) { SelfIntersections = true, BuildRegions = true };
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
            foreach (int root in new HashSet<int>(r.Take(12))) Assert.Equal(2, System.Linq.Enumerable.Count(r.Take(12), x => x == root));
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
            var fold = new DMesh3();                              // consistent edge direction (1->2, 2->1) but both third corners on the same side
            fold.AppendVertex(new Vector3d(0, 0, 0)); fold.AppendVertex(new Vector3d(2, 0, 0)); fold.AppendVertex(new Vector3d(0, 2, 0)); fold.AppendVertex(new Vector3d(2, 2, 0));
            fold.AppendTriangle(0, 1, 2);
            fold.AppendTriangle(2, 1, 3);                         // the edge 1-2 is 1->2 in the first and 2->1 here, the corners 0 and 3 are on opposite sides: flat
            Assert.Equal(1, Count(Regions(fold, Far()), 0, 2));
            var over = new DMesh3();                              // the edge 0-1 as 0->1 and 1->0, the third corners (0,2) and (1,1) both above
            over.AppendVertex(new Vector3d(0, 0, 0)); over.AppendVertex(new Vector3d(2, 0, 0)); over.AppendVertex(new Vector3d(0, 2, 0)); over.AppendVertex(new Vector3d(1, 1, 0));
            over.AppendTriangle(0, 1, 2);
            over.AppendTriangle(1, 0, 3);
            Assert.Equal(2, Count(Regions(over, Far()), 0, 2));
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

        static string Outputs(bool regions, params DMesh3[] meshes)
        {
            var arr = new ExactMeshArrangement(new List<DMesh3>(meshes)) { SelfIntersections = true, BuildRegions = regions };
            gParallel.SerialOnThisThread = true;       // (point ids follow the thread order otherwise)
            try { arr.Compute(); } finally { gParallel.SerialOnThisThread = false; }
            return string.Join(";", arr.Output.Select(o => o.Mesh + "," + o.Source + "," + o.A + "," + o.B + "," + o.C));
        }

        [Fact]
        public void BuildingTheRegions_ChangesNoOutput()
        {
            DMesh3 a = ExactMeshBooleanCoplanarWalkTests.Cylinder(6), b = ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0.25);
            Assert.Equal(Outputs(false, a, b), Outputs(true, a, b));
            DMesh3 c = ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0);
            Assert.Equal(Outputs(false, a, c), Outputs(true, a, c));
        }

        [Fact]
        public void TheRegions_AreTheSameForEveryPowerOfTwoScale_AndWhenComputedTwice()
        {
            DMesh3 a = ExactMeshBooleanCoplanarWalkTests.Cylinder(6), b = ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0.25);
            int[] r = Regions(a, b);
            foreach (double f in new[] { 0.25, 2, 1024 }) {
                DMesh3 sa = new DMesh3(a), sb = new DMesh3(b);
                foreach (DMesh3 m in new[] { sa, sb })
                    foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, m.GetVertex(v) * f);
                Assert.Equal(r, Regions(sa, sb));
            }
            var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true, BuildRegions = true };
            arr.Compute();
            int[] first = arr.RegionOf;
            arr.Compute();
            Assert.Equal(first, arr.RegionOf);
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
