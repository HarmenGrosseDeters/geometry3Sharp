using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The top-down build of DMeshAABBTree3 on extreme coordinates (found by tools/MeshFuzzPilot: MeshRobustRepair's
    /// solidify fallback, DualContouring's tree over a contour at about 1e80, died with a stack overflow).
    /// - Midpoint split: when a set's extreme centroids on the axis are adjacent doubles, the midpoint can round to the
    ///   larger one, so no centroid is above it. The scan ran past the set: out of the array (IndexOutOfRange) at the
    ///   end, into the next set inside (a corrupt tree), or the same set recursed without end (a stack overflow).
    /// - Valid centroids: the test used the squared length, which overflows above about 1e154, so every triangle of
    ///   such a mesh was skipped, and the skipped slots were split as triangle 0.
    /// </summary>
    public class DMeshAABBTreeBuildExtremeTests
    {
        static List<int> Leaves(DMeshAABBTree3 tree)
        {
            var seen = new List<int>();
            tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() { NextTriangleF = t => seen.Add(t) });
            return seen;
        }

        // point triangles whose centroids (GetTriCentroid: the sum times 1/3) are c or the next double on every axis;
        // c = 1.0000000000000003e80: the midpoint of the two rounds to the larger one, and they differ by more than
        // MathUtil.ZeroTolerance (so the split sorts instead of halving)
        static DMesh3 AdjacentCentroids(int copies)
        {
            double c = 1.0000000000000003e80, c1 = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(c) + 1);
            var m = new DMesh3();
            foreach (double x in new[] { c, c1 })
                foreach (double y in new[] { c, c1 })
                    foreach (double z in new[] { c, c1 })
                        for (int k = 0; k < copies; ++k) {
                            var p = new Vector3d(x, y, z);
                            m.AppendTriangle(m.AppendVertex(p), m.AppendVertex(p), m.AppendVertex(p));
                        }
            return m;
        }

        [Fact]
        public void MidpointSplit_AdjacentCentroids_EveryTriangleOnce()
        {
            DMesh3 m = AdjacentCentroids(4);
            // the setup: two distinct centroid values per axis, adjacent, whose midpoint rounds to the larger
            var xs = m.TriangleIndices().Select(t => m.GetTriCentroid(t).x).Distinct().OrderBy(v => v).ToArray();
            Assert.Equal(2, xs.Length);
            Assert.Equal(xs[1], 0.5 * (xs[0] + xs[1]));
            Assert.True(xs[1] - xs[0] > MathUtil.ZeroTolerance);
            var tree = new DMeshAABBTree3(m, true);
            List<int> leaves = Leaves(tree);
            Assert.Equal(m.TriangleCount, leaves.Count);
            Assert.Equal(m.TriangleIndices().OrderBy(t => t), leaves.OrderBy(t => t));
        }

        [Fact]
        public void MidpointSplit_AdjacentCentroidsAfterOtherTriangles_EveryTriangleOnce()
        {
            // the same set after other triangles (it becomes the right child, at the end of the array: the scan ran out
            // of the array there too): unit triangles at the origin first, then the adjacent set
            var m = new DMesh3();
            for (int k = 0; k < 40; ++k)
                m.AppendTriangle(m.AppendVertex(new Vector3d(k, 0, 0)), m.AppendVertex(new Vector3d(k + 1, 0, 0)), m.AppendVertex(new Vector3d(k, 1, 0)));
            MeshEditor.Append(m, AdjacentCentroids(4));
            var tree = new DMeshAABBTree3(m, true);
            List<int> leaves = Leaves(tree);
            Assert.Equal(m.TriangleIndices().OrderBy(t => t), leaves.OrderBy(t => t));
            // a query still finds the unit triangles
            var hit = tree.FindNearestTriangle(new Vector3d(10.2, 0.2, 0));
            Assert.Equal(10, hit);
        }

        [Theory]
        [InlineData(DMeshAABBTree3.BuildStrategy.TopDownMidpoint)]
        [InlineData(DMeshAABBTree3.BuildStrategy.TopDownMedian)]
        public void MidpointSplit_AdjacentCentroidsBeforeOtherTriangles_EveryTriangleOnce(DMeshAABBTree3.BuildStrategy strategy)
        {
            // the fuzz case's shape: the adjacent set becomes the left child, and its neighbour (40 triangles at about
            // 1e82, larger on every axis) follows in the array. Before, the scan of the left set ran into the neighbour
            // and the set recursed without end: a stack overflow (the test host died)
            DMesh3 m = AdjacentCentroids(4);
            double far = 1e82;
            for (int k = 0; k < 40; ++k) {
                var p = new Vector3d(far + k * 1e66, far + k * 1e66, far + k * 1e66);
                m.AppendTriangle(m.AppendVertex(p), m.AppendVertex(p + new Vector3d(1e67, 0, 0)), m.AppendVertex(p + new Vector3d(0, 1e67, 0)));
            }
            var tree = new DMeshAABBTree3(m, false);
            tree.Build(strategy);
            List<int> leaves = Leaves(tree);
            Assert.Equal(m.TriangleIndices().OrderBy(t => t), leaves.OrderBy(t => t));
        }

        [Theory]
        [InlineData(1e160)]
        [InlineData(1e200)]
        [InlineData(1e300)]
        public void Build_HugeCoordinates_EveryTriangleOnce(double radius)
        {
            var g = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6, Radius = radius };
            g.Generate();
            DMesh3 m = g.MakeDMesh();
            List<int> leaves = Leaves(new DMeshAABBTree3(m, true));
            Assert.Equal(m.TriangleIndices().OrderBy(t => t), leaves.OrderBy(t => t));
        }

        // (queries, found by tools/MeshFuzzPilot's tree target) a ray with no distance limit used float.MaxValue as the
        // limit, so every hit farther than 3.4e38 was lost: FindNearestHitTriangle -1, FindAllHitTriangles 0, and
        // IsInside false for the centre of any mesh of that size
        [Theory]
        [InlineData(1e30)]
        [InlineData(1e39)]
        [InlineData(1e100)]
        [InlineData(1e150)]
        [InlineData(1e300)]
        public void RayQueries_HugeSphere_HitAndInside(double radius)
        {
            var g = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6, Radius = radius };
            g.Generate();
            DMesh3 m = g.MakeDMesh();
            var tree = new DMeshAABBTree3(m, true);
            var ray = new Ray3d(Vector3d.Zero, new Vector3d(0.6, 0, 0.8));
            int hit = tree.FindNearestHitTriangle(ray);
            Assert.NotEqual(DMesh3.InvalidID, hit);
            var all = new List<int>();
            Assert.True(tree.FindAllHitTriangles(ray, all) >= 1);
            Assert.Contains(hit, all);
            Assert.True(tree.IsInside(Vector3d.Zero));
            Assert.False(tree.IsInside(new Vector3d(0.5 * radius, 0, 0) * 3));
            // a limit is still a limit
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestHitTriangle(ray, 0.5 * radius));
            Assert.Equal(hit, tree.FindNearestHitTriangle(ray, 2 * radius));
        }

        [Fact]
        public void Build_NonFiniteCentroid_Skipped_OthersOnce()
        {
            // a triangle with an infinite corner is left out (as before); the others are each in the tree once (before,
            // its slot was split as triangle 0, so triangle 0 appeared twice)
            var m = new DMesh3();
            for (int k = 0; k < 20; ++k)
                m.AppendTriangle(m.AppendVertex(new Vector3d(k, 0, 0)), m.AppendVertex(new Vector3d(k + 1, 0, 0)), m.AppendVertex(new Vector3d(k, 1, 0)));
            int bad = m.AppendTriangle(m.AppendVertex(new Vector3d(double.PositiveInfinity, 0, 0)), m.AppendVertex(Vector3d.AxisY), m.AppendVertex(Vector3d.AxisZ));
            List<int> leaves = Leaves(new DMeshAABBTree3(m, true));
            Assert.Equal(m.TriangleIndices().Where(t => t != bad).OrderBy(t => t), leaves.OrderBy(t => t));
        }
    }
}
