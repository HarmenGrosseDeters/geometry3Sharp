using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// (named for the box tree shards of tests/mutation, whose filter takes BoxTreeMortonTests*) Regression test of #1691 on
    /// the API of master (TriangleBoxTree.QuerySegment, internal): the padded face fl(b - pad) rounded up to half an ulp
    /// of b inside b - pad, so a segment that meets only the part of the pad below one ulp of b was culled.
    /// </summary>
    public class BoxTreeMortonTestsPaddedSlabRegression
    {
        [Fact]
        public void SegmentInTheSubUlpPad_IsReported()
        {
            // box x in [1, 1], y in [0.998, 0.9995], pad 2^-60 (below half an ulp of 1, so fl(1 - pad) = 1); the segment
            // from (1 - 2^-50, -1, 0) to (1, 1, 0) at t = 0.9995 is at x = 1 - 4.4e-19 >= 1 - 2^-60 and y = 0.999: in the
            // padded box (review of #1690, F2)
            double pad = Math.Pow(2, -60);
            var box = new AxisAlignedBox3d(new Vector3d(1, 0.998, -1), new Vector3d(1, 0.9995, 1));
            var tree = new TriangleBoxTree(new List<AxisAlignedBox3d> { box });
            var found = new List<int>();
            tree.QuerySegment(new Vector3d(1 - Math.Pow(2, -50), -1, 0), new Vector3d(1, 1, 0), pad, found.Add);
            Assert.Equal(new List<int> { 0 }, found);
            // the same on the high face: box x in [1, 1] seen from above
            found.Clear();
            tree.QuerySegment(new Vector3d(1 + Math.Pow(2, -50), -1, 0), new Vector3d(1, 1, 0), pad, found.Add);
            Assert.Equal(new List<int> { 0 }, found);
        }
    }
}
