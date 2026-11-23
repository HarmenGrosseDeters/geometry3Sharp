using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> SolidBetti (Harness) on procedural boxes whose solid Betti numbers follow from the construction </summary>
    public class SolidBettiTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var box = new Box3d(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2));
            DMesh3 mesh = new TrivialBox3Generator() { Box = box }.Generate().MakeDMesh();
            // exact corners (center +- extent can round)
            foreach (int vid in mesh.VertexIndices().ToArray()) {
                Vector3d v = mesh.GetVertex(vid);
                mesh.SetVertex(vid, new Vector3d(v.x < box.Center.x ? x0 : x1, v.y < box.Center.y ? y0 : y1, v.z < box.Center.z ? z0 : z1));
            }
            return mesh;
        }

        static DMesh3 Reversed(DMesh3 m)
        {
            var r = new DMesh3(m);
            r.ReverseOrientation();
            return r;
        }

        [Fact]
        public void CubeWithCavity_IsOnePartWithOneCavity()
        {
            DMesh3 mesh = ExactMeshBooleanTests.Merge(Box(0, 0, 0, 3, 3, 3), Reversed(Box(1, 1, 1, 2, 2, 2)));
            Assert.Equal((1, 0, 1), SolidBetti.Compute(mesh));
        }

        [Fact]
        public void TwoDisjointBoxes_AreTwoParts()
        {
            // the same surface topology (two spheres) as the cube with a cavity
            DMesh3 mesh = ExactMeshBooleanTests.Merge(Box(0, 0, 0, 3, 4, 2), Box(6, 0, 0, 8, 1, 1));
            Assert.Equal((2, 0, 0), SolidBetti.Compute(mesh));
        }

        [Fact]
        public void PartInsideACavity_IsASecondPart()
        {
            // depth 0, 1, 2: part, cavity, part
            DMesh3 mesh = ExactMeshBooleanTests.Merge(Box(0, 0, 0, 5, 5, 5), Reversed(Box(1, 1, 1, 4, 4, 4)), Box(2, 2, 2, 3, 3, 3));
            Assert.Equal((2, 0, 1), SolidBetti.Compute(mesh));
        }

        [Fact]
        public void CubeWithThroughTunnel_HasOneTunnel()
        {
            var boolean = new ExactMeshBoolean(Box(0, 0, 0, 3, 3, 3), Box(1, 1, -1, 2, 2, 4), ExactMeshBoolean.Operations.Difference);
            Assert.True(boolean.Compute(), boolean.Error);
            ResultChecks.AssertClean(boolean.Result);
            Assert.Equal((1, 1, 0), SolidBetti.Compute(boolean.Result));
        }
    }
}
