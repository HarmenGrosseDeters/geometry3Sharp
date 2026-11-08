using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRobustRepair.ScaleDisparity: the exact resolve of components far apart is clean but for the degenerate area
    /// of NotCleanReason, relative to the diagonal of all triangles; Apply() then returns false with the resolved mesh
    /// and this reason, and does not run the solidify or the wrap (RobustRepairFarPartTests for the scene).
    /// </summary>
    public class RobustRepairScaleDisparityTests
    {
        [Fact]
        public void FarPart_TheReasonAndTheResolvedMesh()
        {
            var r = new MeshRobustRepair(RobustRepairFarPartTests.Scene(1e8, 10));
            Assert.False(r.Apply());
            Assert.Equal(MeshRobustRepair.ScaleDisparity, r.ResultReason);
            Assert.True(r.UsedExactResolve);
            Assert.False(r.UsedSolidify || r.UsedAlphaWrap);
            Assert.EndsWith(MeshRobustRepair.ScaleDisparity, r.ExactResolveReason);
            Assert.Equal("degenerate triangles", MeshRobustRepair.NotCleanReason(r.Mesh));
        }

        [Fact]
        public void FarPart_Similarities_Pow2BitIdentical()
        {
            DMesh3 scene = RobustRepairFarPartTests.Scene(1e8, 10);
            var r0 = new MeshRobustRepair(new DMesh3(scene));
            Assert.False(r0.Apply());
            foreach (Similarity sim in Similarity.All()) {
                var r = new MeshRobustRepair(Invariance.Transformed(scene, sim));
                r.Apply();
                Assert.True(r.Mesh.IsClosed() && Invariance.AllFinite(r.Mesh), sim.ToString());
                Assert.False(r.UsedAlphaWrap, sim.ToString());
                if (sim.IsExact) {
                    Assert.Equal(r0.ResultReason, r.ResultReason);
                    Assert.Equal("", Invariance.Difference(r0.Mesh, Invariance.MappedBack(r.Mesh, sim)));
                }
            }
        }

        [Fact]
        public void FarPart_SerialEqualsParallel_ApplyTwice()
        {
            DMesh3 scene = RobustRepairFarPartTests.Scene(1e8, 10);
            var parallel = new MeshRobustRepair(new DMesh3(scene));
            parallel.Apply();
            gParallel.SerialOnThisThread = true;
            MeshRobustRepair serial;
            try {
                serial = new MeshRobustRepair(new DMesh3(scene));
                serial.Apply();
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            Assert.Equal("", Invariance.Difference(serial.Mesh, parallel.Mesh));
            // the same instance again, on a clean mesh after the far scene: the reason does not stay
            var r = new MeshRobustRepair(new DMesh3(scene));
            Assert.False(r.Apply());
            r.Mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            Assert.True(r.Apply());
            Assert.Equal("", r.ResultReason);
            Assert.Equal("", r.ExactResolveReason);
            // and the far scene again: the same as the first
            r.Mesh = new DMesh3(scene);
            Assert.False(r.Apply());
            Assert.Equal("", Invariance.Difference(parallel.Mesh, r.Mesh));
            // then two crossing open triangles, whose exact resolve fails (not closed): the far scene's resolve must not
            // come back
            DMesh3 open = new DMesh3();
            int a0 = open.AppendVertex(new Vector3d(0, 0, 0)), a1 = open.AppendVertex(new Vector3d(1, 0, 0)), a2 = open.AppendVertex(new Vector3d(0, 1, 0));
            int b0 = open.AppendVertex(new Vector3d(0.2, 0.2, -0.5)), b1 = open.AppendVertex(new Vector3d(0.3, 0.2, 0.5)), b2 = open.AppendVertex(new Vector3d(0.2, 0.3, 0.5));
            open.AppendTriangle(a0, a1, a2);
            open.AppendTriangle(b0, b1, b2);
            r.Mesh = open;
            r.Apply();
            Assert.NotEqual(MeshRobustRepair.ScaleDisparity, r.ResultReason);
            Assert.True(r.Mesh.Vertices().All(v => v.MaxAbs < 10), "the far scene came back");
        }
    }
}
