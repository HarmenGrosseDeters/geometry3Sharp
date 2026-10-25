using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reduction that adds no self-intersections and no folds (checked exactly after reducing, regions
    /// with new defects pinned and reduced again).
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
        public void ThinSlab_PlainReducerIntersects_DefectFreeDoesNot()
        {
            DMesh3 input = ThinSlab();
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(input));
            DMesh3 plain = new DMesh3(input);
            new Reducer(plain).ReduceToTriangleCount(120);
            int plainDefects = MeshRepairMetrics.SelfIntersections(plain) + MeshRepairMetrics.FoldedEdges(plain).Count;
            Assert.True(plainDefects > 0, "test does not exercise the check");

            DMesh3 safe = new DMesh3(input);
            var reduce = new MeshDefectFreeReduce(safe, (r) => r.ReduceToTriangleCount(120));
            Assert.True(reduce.Apply());
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(safe));
            Assert.Empty(MeshRepairMetrics.FoldedEdges(safe));
            Assert.Equal(0, reduce.DefectsLeft);
            Assert.True(safe.IsClosed());
            Assert.True(safe.TriangleCount < 0.5 * input.TriangleCount, $"{safe.TriangleCount} of {input.TriangleCount} triangles");
        }

        [Fact]
        public void CleanReduction_OneRoundNoPins()
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
            var reduce = new MeshDefectFreeReduce(sphere, (r) => r.ReduceToTriangleCount(500));
            Assert.True(reduce.Apply());
            Assert.Equal(1, reduce.Rounds);
            Assert.Equal(0, reduce.PinnedVertices);
            Assert.InRange(sphere.TriangleCount, 450, 550);
        }

        [Fact]
        public void InputDefects_KeptButNoNewOnes()
        {
            // two overlapping spheres (an input self-intersection), reduced: the overlap stays, nothing new
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 16 }.Generate().MakeDMesh();
            DMesh3 b = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 16 }.Generate().MakeDMesh();
            MeshTransforms.Translate(b, new Vector3d(1.2, 0, 0));
            MeshEditor.Append(a, b);
            int before = MeshRepairMetrics.SelfIntersections(a);
            Assert.True(before > 0);
            var reduce = new MeshDefectFreeReduce(a, (r) => r.ReduceToTriangleCount(800));
            Assert.True(reduce.Apply());
            Assert.Equal(0, reduce.DefectsLeft);
            Assert.True(reduce.PinnedVertices > 0);
            Assert.True(a.TriangleCount < 2500);
        }
    }
}
