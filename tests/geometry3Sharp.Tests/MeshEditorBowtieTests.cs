using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class MeshEditorBowtieTests
    {
        [Fact]
        public void DoubleCone_IsClosedWithOneBowtie()
        {
            DMesh3 mesh = MeshTestUtil.DoubleConeBowtie(8, 0);
            Assert.Equal(0, MeshTestUtil.BoundaryEdgeCount(mesh));
            Assert.Single(MeshIterators.BowtieVertices(mesh));
        }

        // the old DisconnectBowtie grouped one-ring triangles in a single order-dependent pass and
        // could split a fan into several pieces, which cut cracks (boundary edges) into the mesh
        [Fact]
        public void DisconnectAllBowties_NeverOpensCracks()
        {
            for (int seed = 0; seed < 200; ++seed) {
                DMesh3 mesh = MeshTestUtil.DoubleConeBowtie(8, seed);
                int nRemaining = new MeshEditor(mesh).DisconnectAllBowties();

                Assert.Equal(0, nRemaining);
                Assert.True(MeshTestUtil.BoundaryEdgeCount(mesh) == 0, $"seed {seed}: boundary edges after disconnect");
                Assert.Equal(2 * 8 + 2 + 2, mesh.VertexCount);   // apex was duplicated exactly once
                var comps = new MeshConnectedComponents(mesh);
                comps.FindConnectedT();
                Assert.Equal(2, comps.Count);
                Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            }
        }
    }
}
