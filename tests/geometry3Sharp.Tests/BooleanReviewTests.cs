using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Defects found in the quality review of the volumetric booleans: a stale index in the priority
    /// queue let the reducer collapse edges with outdated priorities (cut corners), non-compact inputs,
    /// inside-out parts.
    /// </summary>
    public class BooleanReviewTests
    {
        [Fact]
        public void IndexPriorityQueue_Contains_IsFalseAfterDequeue()
        {
            var q = new IndexPriorityQueue(10);
            q.Insert(0, 1); q.Insert(1, 2); q.Insert(2, 3); q.Insert(3, 4);
            Assert.Equal(0, q.Dequeue());
            q.Insert(4, 5);                     // reuses the slot that id 0 still points to
            Assert.False(q.Contains(0));
            Assert.True(q.Contains(4));
            q.Update(4, 0.5f);
            Assert.Equal(4, q.Dequeue());
            Assert.Equal(1, q.Dequeue());

        }

        static DMesh3 Box(Vector3d min, Vector3d max) =>
            new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Solidify_AxisAlignedBox_KeepsItsVolume(bool adaptive)
        {
            // [0,10]^3, default parameters except the resolution
            DMesh3 box = Box(Vector3d.Zero, 10 * Vector3d.One);
            var s = new MeshWindingNumberSolidify(box) { Adaptive = adaptive, GridResolution = 64 };
            Assert.True(s.Compute());
            double vol = MeshMeasurements.VolumeArea(s.Result, s.Result.TriangleIndices(), s.Result.GetVertex).x;
            Assert.InRange(vol, 999, 1001);
        }

        [Fact]
        public void Solidify_NonCompactMesh_DoesNotThrow()
        {
            // open hemisphere made by removing triangles: triangle ids are no longer compact
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 5, EdgeVertices = 20 }.Generate().MakeDMesh();
            foreach (int t in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(t).z > 0) m.RemoveTriangle(t, true, false);
            Assert.False(m.IsCompactT);
            var s = new MeshWindingNumberSolidify(m) { Adaptive = true, GridResolution = 64 };
            Assert.True(s.Compute());
        }

        [Fact]
        public void Solidify_DisjointInvertedPart_IsNotSilentlyDropped()
        {
            // a correct 10^3 box plus a disjoint inside-out 12^3 box (a common STL defect)
            DMesh3 m = Box(Vector3d.Zero, 10 * Vector3d.One);
            DMesh3 inv = Box(new Vector3d(20, 0, 0), new Vector3d(32, 12, 12));
            inv.ReverseOrientation(false);
            MeshEditor.Append(m, inv);
            var s = new MeshWindingNumberSolidify(m) { Adaptive = true, GridResolution = 64 };
            Assert.True(s.Compute());
            double vol = MeshMeasurements.VolumeArea(s.Result, s.Result.TriangleIndices(), s.Result.GetVertex).x;
            Assert.InRange(vol, 2700, 2760);
        }

        [Fact]
        public void Union_RotatedBoxes_ReducerKeepsInputCorner()
        {
            DMesh3 a = Box(Vector3d.Zero, 10 * Vector3d.One);
            DMesh3 b = Box(2.5 * Vector3d.One, 12.5 * Vector3d.One);
            MeshTransforms.Rotate(b, 7.5 * Vector3d.One, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
            AxisAlignedBox3d all = a.CachedBounds; all.Contain(b.CachedBounds);
            double cell = all.DiagonalLength / 128;
            var u = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { CellSize = cell };
            Assert.True(u.Compute());
            // (10,10,0) is a corner of A outside B: it must survive within ReduceTolerance * cell
            Vector3d corner = new Vector3d(10, 10, 0);
            double d = u.Result.VertexIndices().Min(v => u.Result.GetVertex(v).Distance(corner));
            Assert.True(d < 0.05 * cell, $"corner moved by {d / cell:F2} cells");
        }
    }
}
