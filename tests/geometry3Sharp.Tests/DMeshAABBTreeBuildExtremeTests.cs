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
        public void MidpointSplit_AdjacentCentroidsNextToOtherTriangles_EveryTriangleOnce()
        {
            // the same set inside a larger mesh (the scan of an inner set could run into its neighbour): unit triangles
            // at the origin first, then the adjacent set
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
