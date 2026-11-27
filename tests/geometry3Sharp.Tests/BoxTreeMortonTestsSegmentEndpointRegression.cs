using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// (named for the box tree shards of tests/mutation, whose filter takes BoxTreeMortonTests*) Regression tests of #1673
    /// on the API of master (TriangleBoxTree.QuerySegment, internal): the slab test treated a
    /// segment with |q - p| &lt; 1e-300 along an axis as parallel and tested only its start p, so a segment whose end q lies
    /// in the box was culled (and its copy scaled by 2^600, which takes the division branch, was not).
    /// </summary>
    public class BoxTreeMortonTestsSegmentEndpointRegression
    {
        static Vector3d Scaled(Vector3d v, int k) => new Vector3d(ExactPoint3.ScaleB(v.x, k), ExactPoint3.ScaleB(v.y, k), ExactPoint3.ScaleB(v.z, k));

        static List<int> Query(List<AxisAlignedBox3d> boxes, Vector3d p, Vector3d q, double pad = 0)
        {
            var tree = new TriangleBoxTree(boxes);
            var found = new List<int>();
            tree.QuerySegment(p, q, pad, found.Add);
            return found;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(600)]
        [InlineData(1000)]
        [InlineData(1023)]
        [InlineData(-40)]
        public void NearParallelSegment_WithItsEndInTheBox_IsReported_AtEveryScale(int k)
        {
            // the issue's repro: box (1e-310, -1, -1)-(1, 1, 1), segment (0,0,0)-(2e-310,0,0): q lies in the box
            var box = new AxisAlignedBox3d(Scaled(new Vector3d(1e-310, -1, -1), k), Scaled(new Vector3d(1, 1, 1), k));
            var p = Scaled(Vector3d.Zero, k);
            var q = Scaled(new Vector3d(2e-310, 0, 0), k);
            Assert.True(box.Contains(q));
            Assert.Equal(new List<int> { 0 }, Query(new List<AxisAlignedBox3d> { box }, p, q));
            // the reverse segment (the start in the box) and the segment from q alone
            Assert.Equal(new List<int> { 0 }, Query(new List<AxisAlignedBox3d> { box }, q, p));
        }

        [Fact]
        public void NearParallelSegment_ThroughATreeOfManyBoxes_FindsTheBoxOfItsEnd()
        {
            // the README's variant ([1e-310, 1] x [0, 1]^2, ends (0, .5, .5) and (2e-310, .5, .5)) among boxes on a grid,
            // so that the node boxes are tested by the same rule; the boxes left of x = 0 are not reported
            var boxes = new List<AxisAlignedBox3d>();
            for (int i = 0; i < 40; ++i)
                boxes.Add(new AxisAlignedBox3d(new Vector3d(-2 - i, 0, 0), new Vector3d(-1 - i, 1, 1)));
            boxes.Add(new AxisAlignedBox3d(new Vector3d(1e-310, 0, 0), new Vector3d(1, 1, 1)));
            var found = Query(boxes, new Vector3d(0, 0.5, 0.5), new Vector3d(2e-310, 0.5, 0.5));
            Assert.Equal(new List<int> { 40 }, found);
        }
    }
}
