using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Parallel smoothing in the Remesher buffers the new positions, so it must give exactly the serial
    /// result. It did not: the modified-vertex flags were a BitArray, whose concurrent writes to different
    /// vertices in one 32-bit word lose flags, and with them some smoothed positions, at random (the
    /// AlongSurface safety line test failed now and then, mostly under load).
    /// </summary>
    public class RemesherParallelTests
    {
        static DMesh3 Remeshed(bool parallel)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 10, EdgeVertices = 80 }.Generate().MakeDMesh();
            var r = new Remesher(mesh) { EnableParallelSmooth = parallel, SmoothSpeedT = 0.5 };
            r.SetTargetEdgeLength(0.15);
            r.SetProjectionTarget(MeshProjectionTarget.Auto(new DMesh3(mesh)));
            for (int k = 0; k < 3; ++k)
                r.BasicRemeshPass();
            return mesh;
        }

        [Fact]
        public void ParallelSmoothing_SameResultAsSerial()
        {
            DMesh3 serial = Remeshed(false);
            for (int run = 0; run < 3; ++run) {
                DMesh3 parallel = Remeshed(true);
                Assert.Equal(serial.MaxVertexID, parallel.MaxVertexID);
                Assert.Equal(serial.TriangleCount, parallel.TriangleCount);
                foreach (int vid in serial.VertexIndices())
                    Assert.True(serial.GetVertex(vid) == parallel.GetVertex(vid), $"run {run}: vertex {vid} differs");
            }
        }
    }
}
