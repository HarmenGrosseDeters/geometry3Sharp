using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary> ExactMeshBoolean.Compute() called again after the meshes or the operation changed </summary>
    public class ExactBooleanReuseTests
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

        static void AssertSameAsNew(ExactMeshBoolean reused)
        {
            var fresh = new ExactMeshBoolean(reused.MeshA, reused.MeshB, reused.Operation);
            Assert.True(fresh.Compute());
            Assert.Equal(Volume(fresh.Result), Volume(reused.Result), 12);
            Assert.Equal(fresh.Result.TriangleCount, reused.Result.TriangleCount);
            Assert.Equal(fresh.Patches, reused.Patches);
            Assert.Equal(fresh.IntersectingPairs, reused.IntersectingPairs);
            Assert.Equal(fresh.Counts["pieces"], reused.Counts["pieces"]);
        }

        [Fact]
        public void ClosedMeshes_OtherMeshB()
        {
            DMesh3 a = Box(0, 0, 0, 1, 1, 1);
            var boolean = new ExactMeshBoolean(a, Box(0.5, 0.5, 0.5, 1.5, 1.5, 1.5), Ops.Intersection);
            Assert.True(boolean.Compute());
            Assert.Equal(0.125, Volume(boolean.Result), 12);
            // a larger mesh B: its bounds (outside of them the winding number is 0) must not be the old ones
            boolean.MeshB = Box(-10, -10, -10, 10, 10, 0.5);
            Assert.True(boolean.Compute());
            Assert.Equal(0.5, Volume(boolean.Result), 12);
            AssertSameAsNew(boolean);
            boolean.Operation = Ops.Difference;
            Assert.True(boolean.Compute());
            Assert.Equal(0.5, Volume(boolean.Result), 12);
            AssertSameAsNew(boolean);
        }

        [Fact]
        public void OpenMeshes_OtherMeshB()
        {
            // open boxes (without a top triangle): generalized winding numbers, from a tree of the mesh
            DMesh3 open1 = Box(0, 0, 0, 2, 2, 2), open2 = Box(5, 5, 5, 7, 7, 7);
            open1.RemoveTriangle(open1.TriangleIndices().First(t => open1.GetTriCentroid(t).z == 2), true, false);
            open2.RemoveTriangle(open2.TriangleIndices().First(t => open2.GetTriCentroid(t).z == 7), true, false);
            DMesh3 inner = Box(0.5, 0.5, 0.25, 1.5, 1.5, 0.75);
            var boolean = new ExactMeshBoolean(inner, open1, Ops.Intersection);
            Assert.True(boolean.Compute());
            Assert.False(boolean.ExactWindingB);
            Assert.Equal(0.5, Volume(boolean.Result), 12);
            boolean.MeshB = open2;
            Assert.True(boolean.Compute());
            Assert.Equal(0, boolean.Result.TriangleCount);
            AssertSameAsNew(boolean);
        }
    
        [Fact]
        public void FailedCompute_LeavesNoOldDiagnostics()
        {
            var boolean = new ExactMeshBoolean(Box(0, 0, 0, 1, 1, 1), Box(0.5, 0.5, 0.5, 2, 2, 2), Ops.Union);
            Assert.True(boolean.Compute());
            ResultChecks.AssertClosed(boolean.Result);
            // 1 + 1.5^3 - 0.5^3
            Assert.Equal(1 + 3.375 - 0.125, ResultChecks.Volume(boolean.Result), 12);
            var counts = boolean.Counts;
            Assert.True(boolean.ExactWindingA && boolean.PhaseMilliseconds.Count > 0 && counts.Count > 0);
            boolean.MeshB = null;
            Assert.False(boolean.Compute());
            Assert.False(boolean.ExactWindingA || boolean.ExactWindingB);
            Assert.Empty(boolean.PhaseMilliseconds);
            Assert.Empty(boolean.Counts);
            // the dictionaries of the earlier call stay as they were
            Assert.NotEmpty(counts);
        }
    }
}
