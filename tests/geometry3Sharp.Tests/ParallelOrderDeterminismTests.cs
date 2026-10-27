using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Results that followed the thread schedule (docs/ROBUSTNESS_SWEEP.md S29, S30, S32, R19): parallel loops collected
    /// triangle pairs or triangles to remove in thread order, and the order decided which triangle was dropped
    /// (TerrainDualContouring.RemoveOverlaps), which leaves were split first, or the free-list order of the mesh and so
    /// the ids of later insertions. The lists are sorted now: repeated runs give the same result, bit for bit.
    /// </summary>
    public class ParallelOrderDeterminismTests
    {
        /// <summary> two overlapping grids in XY (many overlapping triangle pairs) </summary>
        static DMesh3 OverlappingGrids()
        {
            var m = new DMesh3();
            foreach (double shift in new[] { 0.0, 0.37 }) {
                int n = 30, b = m.MaxVertexID;
                for (int j = 0; j <= n; ++j)
                    for (int i = 0; i <= n; ++i)
                        m.AppendVertex(new Vector3d(i + shift, j + 0.5 * shift, shift));
                for (int j = 0; j < n; ++j)
                    for (int i = 0; i < n; ++i) {
                        int a = b + j * (n + 1) + i;
                        m.AppendTriangle(a, a + 1, a + n + 2);
                        m.AppendTriangle(a, a + n + 2, a + n + 1);
                    }
            }
            return m;
        }

        [Fact]
        public void XYOverlapPairs_Sorted()
        {
            List<Index2i> pairs = TerrainVolumeDifference.XYOverlapPairs(OverlappingGrids());
            Assert.True(pairs.Count > 1000);
            for (int k = 1; k < pairs.Count; ++k) {
                Index2i p = pairs[k - 1], q = pairs[k];
                Assert.True(p.a < q.a || p.a == q.a && p.b < q.b, $"pair {k}: {p} before {q}");
            }
        }

        static DMesh3 RoughTerrain()
        {
            // the input of TerrainDualContouringTests.MergedCells_NoOverlappingTrianglesInXY: merged cells overlap in
            // XY, so leaves are split and pairs are removed
            var rnd = new Random(167);
            var planes2 = Enumerable.Range(0, 15).Select(_ => new Vector3d(rnd.NextDouble() * 1.6 - 0.8, rnd.NextDouble() * 1.6 - 0.8, rnd.NextDouble() * 40)).ToList();
            var planes = Enumerable.Range(0, 25).Select(_ => new Vector3d(rnd.NextDouble() * 1.2 - 0.6, rnd.NextDouble() * 1.2 - 0.6, rnd.NextDouble() * 40)).ToList();
            double noise = rnd.NextDouble() * 0.4;
            int n = 121;
            var mesh = new DMesh3();
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double x = 120.0 * i / (n - 1), y = 120.0 * j / (n - 1);
                    mesh.AppendVertex(new Vector3d(x, y, planes.Max(p => p.x * (x - 60) + p.y * (y - 60) + p.z)
                        + planes2.Min(p => p.x * (x - 60) + p.y * (y - 60) + p.z) + noise * (rnd.NextDouble() - 0.5)));
                }
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    int a = j * n + i;
                    mesh.AppendTriangle(a, a + 1, a + n + 1);
                    mesh.AppendTriangle(a, a + n + 1, a + n);
                }
            return mesh;
        }

        [Fact]
        public void TerrainDualContouring_RepeatedRuns_BitIdentical()
        {
            DMesh3 input = RoughTerrain();
            DMesh3 first = null;
            for (int run = 0; run < 4; ++run) {
                var tdc = new TerrainDualContouring(input) { CellSize = 1, Tolerance = 0.2 };
                Assert.True(tdc.Compute());
                Assert.True(tdc.SplitLeaves > 0);
                if (first == null) first = tdc.Result;
                else Assert.Equal("", Invariance.Difference(first, tdc.Result));
            }
        }

        [Fact]
        public void RemoveOccludedTriangles_RemovedInIdOrder_RepeatedRunsBitIdentical()
        {
            // a sphere inside a box: the sphere is occluded; the removal order sets the ids of later insertions
            DMesh3 Make()
            {
                DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, 2 * Vector3d.One) }.Generate().MakeDMesh();
                MeshEditor.Append(m, Defects.Sphere(Vector3d.Zero, 1, 10));
                var r = new RemoveOccludedTriangles(m) { InsideMode = RemoveOccludedTriangles.CalculationMode.RayParity };
                r.Apply();
                for (int k = 1; k < r.RemovedT.Count; ++k) Assert.True(r.RemovedT[k - 1] < r.RemovedT[k]);
                Assert.True(r.RemovedT.Count > 100);
                // later insertions take the freed ids
                int a = m.AppendVertex(new Vector3d(5, 0, 0)), b = m.AppendVertex(new Vector3d(5, 1, 0)), c = m.AppendVertex(new Vector3d(5, 0, 1));
                m.AppendTriangle(a, b, c);
                return m;
            }
            DMesh3 first = Make();
            for (int run = 0; run < 3; ++run)
                Assert.Equal("", Invariance.Difference(first, Make()));
        }
    }
}
