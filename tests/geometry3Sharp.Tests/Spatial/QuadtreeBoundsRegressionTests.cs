using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests.Spatial
{
    public class QuadtreeBoundsRegressionTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(-1)]
        public void SegmentClippingRetainsClosedCrossings(int exponent)
        {
            double scale = Math.Pow(2, exponent);
            var point = new Vector2d(1e16 * scale, 2e16 * scale);
            var tree = new LooseQuadtree2d(new[] { new AxisAlignedBox2d(point) });
            var ids = new List<int>();
            var a = new Vector2d(0, 3e16 * scale);
            var b = new Vector2d(3e16 * scale, 0);
            tree.QuerySegment(a, b, ids);
            Assert.Equal(new[] { 0 }, ids);
            tree.QuerySegment(b, a, ids);
            Assert.Equal(new[] { 0 }, ids);
            tree.QuerySegment(a, new Vector2d(b.x, -16 * scale), ids);
            Assert.Empty(ids);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OverflowTiesUseStableIds(bool parallel)
        {
            var boxes = new[] {
                new AxisAlignedBox2d(new Vector2d(-1e308, 0)),
                new AxisAlignedBox2d(new Vector2d(-9e307, 0)),
                new AxisAlignedBox2d(new Vector2d(-9e307, 0)) };
            var tree = new LooseQuadtree2d(boxes, parallel);
            var p = new Vector2d(1e308, 0);
            var ids = new List<int>();
            for (int repeat = 0; repeat < 2; ++repeat) {
                Assert.Equal(1, tree.FindNearest(p));
                tree.FindNearestK(p, 3, ids);
                Assert.Equal(new[] { 1, 2, 0 }, ids);
                tree.FindNearestK(p, 2, ids);
                Assert.Equal(new[] { 1, 2 }, ids);
            }
        }

        [Fact]
        public void OverflowDistancesRemainOrderedAcrossCoreAndAdapters()
        {
            var p = new Vector2d(1e308, 0);
            var points = new[] { new Vector3d(-1e308, 0, 0), new Vector3d(-9e307, 0, 0) };
            var boxes = new[] { new AxisAlignedBox2d(new Vector2d(points[0].x, 0)),
                new AxisAlignedBox2d(new Vector2d(points[1].x, 0)) };
            var core = new LooseQuadtree2d(boxes);
            var pointTree = new PointQuadtree2d(points);
            var segmentTree = new SegmentQuadtree2d(new[] {
                new Segment3d(points[0], Vector3d.AxisY, 1),
                new Segment3d(points[1], Vector3d.AxisY, 1) });
            var triangleTree = new TriangleQuadtree2d(new[] {
                new Triangle3d(points[0], points[0], points[0]),
                new Triangle3d(points[1], points[1], points[1]) });
            var ids = new List<int>();
            for (int repeat = 0; repeat < 2; ++repeat) {
                Assert.Equal(1, core.FindNearest(p));
                core.FindNearestK(p, 2, ids);
                Assert.Equal(new[] { 1, 0 }, ids);
                core.FindNearestK(p, 1, ids);
                Assert.Equal(new[] { 1 }, ids);
                Assert.Equal(-1, core.FindNearest(p, double.MaxValue));
                Assert.Equal(1, pointTree.FindNearest(p));
                pointTree.FindNearestK(p, 2, ids);
                Assert.Equal(new[] { 1, 0 }, ids);
                Assert.Equal(1, segmentTree.FindNearest(p));
                segmentTree.FindNearestK(p, 2, ids);
                Assert.Equal(new[] { 1, 0 }, ids);
                Assert.Equal(1, triangleTree.FindNearest(p));
                triangleTree.FindNearestK(p, 2, ids);
                Assert.Equal(new[] { 1, 0 }, ids);
            }
        }
    }
}
