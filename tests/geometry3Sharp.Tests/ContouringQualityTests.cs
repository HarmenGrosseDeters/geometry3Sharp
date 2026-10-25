using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Geometric quality of the dual contouring results: sharp features off the grid axes, triangle
    /// count, self-intersections (from the quality review).
    /// </summary>
    public class ContouringQualityTests
    {
        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            return new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
        }

        static double Volume(DMesh3 m)
        {
            Vector3d c = m.CachedBounds.Center;
            return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), (v) => m.GetVertex(v) - c).x;
        }

        static DMesh3 RotatedBox(out Vector3d[] corners)
        {
            DMesh3 box = Box(Vector3d.Zero, new Vector3d(10, 6, 4));
            var q = Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 27);
            MeshTransforms.Rotate(box, new Vector3d(5, 3, 2), q);
            corners = new Vector3d[8];
            for (int k = 0; k < 8; ++k)
                corners[k] = new Vector3d(5, 3, 2) + q * (new Vector3d((k & 1) * 10, ((k >> 1) & 1) * 6, ((k >> 2) & 1) * 4) - new Vector3d(5, 3, 2));
            return box;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RotatedBox_SharpCornersAndExactVolume(bool adaptive)
        {
            Vector3d[] corners;
            DMesh3 box = RotatedBox(out corners);
            double cell = 0.25;
            var s = new MeshWindingNumberSolidify(box) { CellSize = cell, Adaptive = adaptive };
            Assert.True(s.Compute());
            DMesh3 r = s.Result;
            foreach (Vector3d c in corners) {
                double d = r.VertexIndices().Min(v => r.GetVertex(v).Distance(c));
                Assert.True(d < 0.02 * cell, $"corner {c} missed by {d / cell:F3} cells");
            }
            Assert.InRange(Volume(r), 240 * 0.999, 240 * 1.001);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(r));
            // the vertices on the input surface (edges and corners reproduced): all but the few moved to
            // resolve folds at the edges (were 0.12 cells off at about 200 vertices with clamping)
            var tree = new DMeshAABBTree3(box, true);
            var dev = r.VertexIndices().Select(v => Math.Sqrt(MeshQueries.TriDistanceSqr(box, tree.FindNearestTriangle(r.GetVertex(v)), r.GetVertex(v)))).ToList();
            Assert.True(dev.Max() < 0.2 * cell, $"vertex {dev.Max() / cell:F3} cells off the input");
            Assert.True(dev.Count(d => d > 0.02 * cell) <= 20, $"{dev.Count(d => d > 0.02 * cell)} of {dev.Count} vertices off the input");
            Assert.True(r.TriangleCount < 1000, $"{r.TriangleCount} triangles");
        }
    }
}
