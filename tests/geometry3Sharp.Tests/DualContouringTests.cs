using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class DualContouringTests
    {
        // signed distance to a sphere, negative inside
        class SphereSDF : ImplicitFunction3d
        {
            public double Radius = 1;
            public double Value(ref Vector3d pt) { return pt.Length - Radius; }
        }

        static DMesh3 Box(double size)
        {
            var gen = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, size * 0.5 * Vector3d.One) };
            return gen.Generate().MakeDMesh();
        }

        [Fact]
        public void Sphere_IsClosedManifoldAndOrientedOutwards()
        {
            var dc = new DualContouring() {
                Implicit = new SphereSDF(), IsoValue = 0, InsideIsLess = true, CellSize = 0.1,
                Bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.3)
            };
            dc.Generate();
            DMesh3 mesh = dc.Mesh;

            Assert.Equal(0, dc.NonManifoldTriangles);
            Assert.True(mesh.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(mesh));
            // every triangle faces away from the center (checks the per-quad orientation, not just the global flip)
            foreach (int tid in mesh.TriangleIndices())
                Assert.True(mesh.GetTriNormal(tid).Dot(mesh.GetTriCentroid(tid)) > 0);
            foreach (int vid in mesh.VertexIndices())
                Assert.InRange(mesh.GetVertex(vid).Length, 0.98, 1.02);
        }

        [Fact]
        public void Solidify_Box_KeepsSharpCorners_MarchingCubesDoesNot()
        {
            DMesh3 box = Box(10);
            Vector3d[] corners = new Box3d(Vector3d.Zero, 5 * Vector3d.One).ComputeVertices();
            Func<DMesh3, double> maxCornerError = (m) =>
                corners.Max(c => m.VertexIndices().Min(v => m.GetVertex(v).Distance(c)));

            var dc = new MeshWindingNumberSolidify(box) { GridResolution = 32, Method = MeshWindingNumberSolidify.Methods.DualContouring };
            Assert.True(dc.Compute());
            var mc = new MeshWindingNumberSolidify(box) { GridResolution = 32, Method = MeshWindingNumberSolidify.Methods.MarchingCubes };
            Assert.True(mc.Compute());

            Assert.True(dc.Result.IsClosed());
            Assert.Equal(0, dc.NonManifoldTriangles);
            double volume = MeshMeasurements.VolumeArea(dc.Result, dc.Result.TriangleIndices(), dc.Result.GetVertex).x;
            Assert.InRange(volume, 990, 1010);

            double cell = box.CachedBounds.DiagonalLength / 32;
            Assert.True(maxCornerError(dc.Result) < 0.01 * cell, "dual contouring should reproduce the corners");
            Assert.True(maxCornerError(mc.Result) > 0.1 * cell, "marching cubes rounds the corners");
            Assert.True(dc.Result.TriangleCount < mc.Result.TriangleCount / 3);
        }

        // two boxes almost touching along an edge: cells there have diagonal inside corners (ambiguous faces).
        // Plain dual contouring puts one vertex in such a cell and pinches the boxes together with
        // non-manifold edges; manifold dual contouring gives each box its own vertex.
        [Fact]
        public void Solidify_BoxesTouchingDiagonally_StayTwoClosedParts()
        {
            DMesh3 mesh = Box(10);
            DMesh3 second = Box(10);
            double cell = 0.5;
            MeshTransforms.Translate(second, new Vector3d(10 + 0.3 * cell, 10 + 0.3 * cell, 0));
            MeshEditor.Append(mesh, second);

            var solid = new MeshWindingNumberSolidify(mesh) { CellSize = cell };
            Assert.True(solid.Compute());
            DMesh3 result = solid.Result;

            Assert.Equal(0, solid.NonManifoldTriangles);
            Assert.True(result.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(result));
            var comps = new MeshConnectedComponents(result);
            comps.FindConnectedT();
            Assert.Equal(2, comps.Count);
            double volume = MeshMeasurements.VolumeArea(result, result.TriangleIndices(), result.GetVertex).x;
            Assert.InRange(volume, 1990, 2010);
        }

        [Fact]
        public void Solidify_BrokenBox_IsClosedAndRemovesInteriorShell()
        {
            // box with a missing triangle and a smaller box inside it
            DMesh3 mesh = Box(10);
            mesh.RemoveTriangle(mesh.TriangleIndices().First(), false, false);
            MeshEditor.Append(mesh, Box(4));
            Assert.False(mesh.IsClosed());

            var solid = new MeshWindingNumberSolidify(mesh) { GridResolution = 48 };
            Assert.True(solid.Compute());
            DMesh3 result = solid.Result;

            Assert.True(result.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(result));
            var comps = new MeshConnectedComponents(result);
            comps.FindConnectedT();
            Assert.Equal(1, comps.Count);
            double volume = MeshMeasurements.VolumeArea(result, result.TriangleIndices(), result.GetVertex).x;
            Assert.InRange(volume, 980, 1020);
        }
    }
}
