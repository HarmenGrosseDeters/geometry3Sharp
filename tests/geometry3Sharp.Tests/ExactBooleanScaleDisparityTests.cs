using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SafeRounding's safety net (MeshRoundingRepair.ScaleDisparity): a closed component that the bound takes as thin but
    /// that holds real volume for its area is never removed; it is kept, counted, and the repair is not clean. The far
    /// parts of ExactBooleanFarComponentTests count none.
    /// </summary>
    public class ExactBooleanScaleDisparityTests
    {
        [Fact]
        public void RealVolume_NotRemovedAsThin_Counted()
        {
            // a sphere and a cube of edge 1e-3 with a needle, with a bound so wide (MaxDisplacement 0.01) that the cube
            // counts as thin (thickness edge / 3 below 0.01 diag) though it is not tiny: it holds real volume for its
            // area, so it is kept and counted, and the repair is not clean (before: removed silently)
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            int n = m.TriangleCount;
            DMesh3 cube = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(3, 0, 0), 0.5e-3 * Vector3d.One) }.Generate().MakeDMesh();
            int eid = cube.EdgeIndices().First();
            Index2i ev = cube.GetEdgeV(eid);
            Vector3d p = cube.GetVertex(ev.a), q = cube.GetVertex(ev.b);
            DMesh3.EdgeSplitInfo split;
            Assert.Equal(MeshResult.Ok, cube.SplitEdge(eid, out split));
            cube.SetVertex(split.vNew, p + 1e-12 * (q - p).Normalized);
            new MeshEditor(m).AppendMesh(cube);
            int total = m.TriangleCount;
            var repair = new MeshRoundingRepair(m) { MaxDisplacement = 0.01 };
            Assert.False(repair.Apply());
            Assert.Equal(1, repair.ScaleDisparity);
            Assert.Equal(0, repair.RemovedComponents);
            Assert.True(m.TriangleCount > n, "the cube was removed");
            Assert.True(m.IsClosed());
        }

        [Fact]
        public void FarParts_NoScaleDisparity()
        {
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Translate(b, new Vector3d(0.5, 0.3, 0.2));
            DMesh3 far = new Sphere3Generator_NormalizedCube() { Radius = 0.1, EdgeVertices = 8 }.Generate().MakeDMesh();
            MeshTransforms.Translate(far, new Vector3d(1e8, 1e8, 1e8));
            new MeshEditor(a).AppendMesh(far);
            var boolean = new ExactMeshBoolean(a, b, g3.ExactMeshBoolean.Operations.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(0, boolean.Rounding.ScaleDisparity);
            Assert.Equal(0, boolean.Counts["rounding_scale_disparity"]);
            Assert.True(boolean.RoundingClean);
        }
    }
}
