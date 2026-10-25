using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A triangle crossed by thousands of intersection segments (a plate with many holes): the segments are
    /// tested for crossings, and the points for lying on segments, only where their boxes overlap, not all pairs.
    /// </summary>
    public class ExactArrangementScalingTests
    {
        static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var box = new Box3d(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2));
            DMesh3 mesh = new TrivialBox3Generator() { Box = box }.Generate().MakeDMesh();
            foreach (int vid in mesh.VertexIndices().ToArray()) {
                Vector3d v = mesh.GetVertex(vid);
                mesh.SetVertex(vid, new Vector3d(v.x < box.Center.x ? x0 : x1, v.y < box.Center.y ? y0 : y1, v.z < box.Center.z ? z0 : z1));
            }
            return mesh;
        }

        // n x n square pins through a plate of n x n x 1, and the pins crossed by a slab (segments that cross)
        static void Plate(int n, out DMesh3 plate, out DMesh3 pins)
        {
            plate = Box(0, 0, 0, n, n, 1);
            pins = new DMesh3();
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j)
                    MeshEditor.Append(pins, Box(i + 0.25, j + 0.25, -1, i + 0.75, j + 0.75, 2));
        }

        [Fact]
        public void PlateWithManyHoles_CandidateTestsAreAboutLinear()
        {
            DMesh3 plate, pins;
            Plate(30, out plate, out pins);
            var arr = new ExactMeshArrangement(new List<DMesh3>() { plate, pins }) { SelfIntersections = true };
            arr.Compute();
            long segments = arr.Triangles.Sum(t => (long)t.Segments.Count);
            // each top and bottom triangle of the plate has thousands of segments; all pairs would be millions
            Assert.True(segments > 10000, segments.ToString());
            Assert.True(arr.CandidateTests < 30 * segments, arr.CandidateTests + " tests for " + segments + " segments");

            var boolean = new ExactMeshBoolean(plate, pins, Ops.Difference);
            Assert.True(boolean.Compute());
            Assert.True(boolean.Result.IsClosed());
            Assert.Equal(30 * 30 * 0.75, Volume(boolean.Result), 9);
        }

        [Fact]
        public void CrossingSegments_AllFound()
        {
            // a grid of thin bars in two directions lying on the plate's top face and crossing each other: their
            // intersection segments with the top face cross at n x n points, which must all be found
            int n = 20;
            DMesh3 plate = Box(0, 0, 0, n, n, 1);
            var bars = new DMesh3();
            for (int i = 0; i < n; ++i) {
                MeshEditor.Append(bars, Box(i + 0.4, -1, 0.5, i + 0.6, n + 1, 1.5));
                MeshEditor.Append(bars, Box(-1, i + 0.3, 0.75, n + 1, i + 0.7, 1.25));
            }
            var union = new ExactMeshBoolean(plate, bars, Ops.Union);
            Assert.True(union.Compute());
            Assert.True(union.Result.IsClosed());
            var inter = new ExactMeshBoolean(plate, bars, Ops.Intersection);
            Assert.True(inter.Compute());
            Assert.True(inter.Result.IsClosed());
            var barsOnly = ExactMeshBoolean.Resolve(bars);
            Assert.Equal(Volume(plate) + Volume(barsOnly), Volume(union.Result) + Volume(inter.Result), 9);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(union.Result));
        }
    
        [Fact]
        public void LongObliqueSegments_CandidateTestsStayBounded()
        {
            // thin bars at 45 degrees through a plate: their boxes on the top face cover almost the whole face, so a
            // fine grid would put every segment into almost every cell
            int m = 300;
            DMesh3 plate = Box(0, 0, 0, 100, 100, 1);
            var bars = new DMesh3();
            for (int k = 0; k < m; ++k) {
                DMesh3 bar = Box(-80, -0.05, 0.5, 80, 0.05, 1.5);
                MeshTransforms.Rotate(bar, Vector3d.Zero, Quaterniond.AxisAngleD(Vector3d.AxisZ, 45));
                MeshTransforms.Translate(bar, new Vector3d(50 + 0.5 * (k - m / 2), 50 - 0.5 * (k - m / 2), 0));
                MeshEditor.Append(bars, bar);
            }
            var arr = new ExactMeshArrangement(new List<DMesh3>() { plate, bars }) { SelfIntersections = true };
            arr.Compute();
            long segments = arr.Triangles.Sum(t => (long)t.Segments.Count), maxPerTriangle = arr.Triangles.Max(t => (long)t.Segments.Count);
            Assert.True(maxPerTriangle > 300, maxPerTriangle.ToString());
            // at most about all pairs of the segments of each triangle (the box tests without a grid)
            long allPairs = arr.Triangles.Sum(t => (long)t.Segments.Count * (t.Segments.Count - 1) / 2);
            Assert.True(arr.CandidateTests < allPairs + 30 * segments, arr.CandidateTests + " tests, all pairs " + allPairs);
        }
    }
}
