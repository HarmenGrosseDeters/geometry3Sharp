using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer.PreventSelfIntersections: every collapse is checked exactly against the nearby triangles,
    /// so the reduction cannot push the surface through itself.
    /// </summary>
    public class DefectFreeReduceTests
    {
        // a thin wavy slab, finely tessellated: aggressive reduction pushes the two sides through each other
        static DMesh3 ThinSlab()
        {
            DMesh3 slab = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(5, 5, 0.02)), EdgeVertices = 40 }.Generate().MakeDMesh();
            foreach (int vid in slab.VertexIndices().ToArray()) {
                Vector3d v = slab.GetVertex(vid);
                slab.SetVertex(vid, new Vector3d(v.x, v.y, v.z + 0.3 * Math.Sin(1.3 * v.x) * Math.Cos(1.1 * v.y)));
            }
            return slab;
        }

        [Fact]
        public void ThinSlab_PlainReducerIntersects_CheckedOneDoesNot()
        {
            DMesh3 input = ThinSlab();
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(input));
            DMesh3 plain = new DMesh3(input);
            new Reducer(plain).ReduceToTriangleCount(120);
            Assert.True(MeshRepairMetrics.SelfIntersections(plain) > 0, "test does not exercise the check");

            DMesh3 safe = new DMesh3(input);
            new Reducer(safe) { PreventSelfIntersections = true }.ReduceToTriangleCount(120);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(safe));
            Assert.True(safe.IsClosed());
            Assert.True(safe.TriangleCount < 0.2 * input.TriangleCount, $"{safe.TriangleCount} of {input.TriangleCount} triangles");
        }

        [Fact]
        public void CleanMesh_SameReductionAsWithoutTheCheck()
        {
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            new Reducer(a).ReduceToTriangleCount(500);
            new Reducer(b) { PreventSelfIntersections = true }.ReduceToTriangleCount(500);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(b));
        }

        [Fact]
        public void InputIntersections_NoNewOnes()
        {
            // two overlapping spheres (an input self-intersection), reduced: the overlap stays, no new ones
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 16 }.Generate().MakeDMesh();
            DMesh3 b = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 16 }.Generate().MakeDMesh();
            MeshTransforms.Translate(b, new Vector3d(1.2, 0, 0));
            MeshEditor.Append(a, b);
            int before = MeshRepairMetrics.SelfIntersections(a);
            Assert.True(before > 0);
            new Reducer(a) { PreventSelfIntersections = true }.ReduceToTriangleCount(800);
            Assert.True(a.TriangleCount < 1000);
            // collapses near the overlap only happen if they create no new intersecting pair; the remaining
            // pairs are the (coarser) overlap curve
            Assert.True(MeshRepairMetrics.SelfIntersections(a) <= before);
        }
    }
}
