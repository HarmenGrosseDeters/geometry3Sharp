using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests.Spatial
{
    public class QuadtreeNumericRegressionTests
    {
        [Theory]
        [InlineData(-10, false)]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(-10, true)]
        [InlineData(0, true)]
        [InlineData(10, true)]
        public void SegmentProjectionDoesNotLoseTinyInteriorOffset(int exponent, bool translated)
        {
            double length = Math.Pow(2, 1000 + exponent), q = Math.Pow(2, -100 + exponent);
            double y = translated ? 8 * q : 0;
            var segment = new Segment3d(new Vector3d(length / 2, y, 0), Vector3d.AxisX, length / 2);
            var tree = new SegmentQuadtree2d(new[] { segment });
            var ids = new List<int>();
            var p = new Vector2d(q, y);
            for (int repeat = 0; repeat < 3; ++repeat) {
                tree.QueryPoint(p, q / 2, ids);
                Assert.Equal(new[] { 0 }, ids);
                Assert.Equal(0, tree.FindNearest(p, 0));
            }
        }

        [Theory]
        [InlineData(-10, false)]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(-10, true)]
        [InlineData(0, true)]
        [InlineData(10, true)]
        public void HeightDoesNotLoseTinyQueryOffset(int exponent, bool translated)
        {
            double length = Math.Pow(2, 1000 + exponent), q = Math.Pow(2, -100 + exponent);
            double offset = translated ? 8 * q : 0;
            var triangle = new Triangle3d(new Vector3d(offset, offset, q / 2),
                new Vector3d(length, offset, q / 2), new Vector3d(offset, length, length));
            var tree = new TriangleQuadtree2d(new[] { triangle });
            for (int repeat = 0; repeat < 3; ++repeat) {
                Assert.True(tree.TryGetHeight(new Vector2d(offset, offset + q), out double height, 0));
                Assert.False(double.IsNaN(height) || double.IsInfinity(height));
                Assert.Equal(1.5 * q, height);
            }
        }

        [Theory]
        [InlineData(-10)]
        [InlineData(0)]
        [InlineData(10)]
        public void HeightDoesNotLoseProductsOfRepresentableScaledComponents(int exponent)
        {
            double q = Math.Pow(2, -600), z = Math.Pow(2, 1000), scale = Math.Pow(2, exponent);
            var triangle = new Triangle3d(scale * new Vector3d(0, 0, q / 2),
                scale * new Vector3d(1, 0, z), scale * new Vector3d(q, 1, q / 2));
            var tree = new TriangleQuadtree2d(new[] { triangle });
            for (int repeat = 0; repeat < 3; ++repeat) {
                Assert.True(tree.TryGetHeight(new Vector2d(0, q * scale), out double height, q * scale));
                Assert.Equal(-Math.Pow(2, -200 + exponent), height);
            }
        }

        [Theory]
        [InlineData(1, 1, 1, 1)]
        [InlineData(1, 1, 2, 1)]
        [InlineData(1, 1, 3, 2)]
        [InlineData(3, 1, 1, 0)]
        public void SubnormalSegmentDistanceRoundsToNearestUnit(int dx, int dy, int units, int roundedUnits)
        {
            var direction = new Vector3d(dx, dy, 0).Normalized;
            double extent = Math.Pow(2, 999);
            var segment = new Segment3d(extent * direction, direction, extent);
            var tree = new SegmentQuadtree2d(new[] { segment });
            var p = new Vector2d(units * double.Epsilon, 0);
            var ids = new List<int>();
            for (int repeat = 0; repeat < 3; ++repeat) {
                Assert.Equal(0, tree.FindNearest(p, roundedUnits * double.Epsilon));
                tree.FindNearestK(p, 1, ids, roundedUnits * double.Epsilon);
                Assert.Equal(new[] { 0 }, ids);
                if (roundedUnits > 0) {
                    Assert.Equal(-1, tree.FindNearest(p, (roundedUnits - 1) * double.Epsilon));
                    tree.FindNearestK(p, 1, ids, (roundedUnits - 1) * double.Epsilon);
                    Assert.Empty(ids);
                }
            }
        }

        [Fact]
        public void HeightDoesNotRoundAwayCoefficientsBeforeMultiplyingHeights()
        {
            double baseline = Math.Pow(2, -100), z = Math.Pow(2, 1000);
            var triangle = new Triangle3d(new Vector3d(0, 0, baseline),
                new Vector3d(1, 1, z), new Vector3d(-1, 1, z));
            var tree = new TriangleQuadtree2d(new[] { triangle });
            for (int repeat = 0; repeat < 3; ++repeat) {
                Assert.True(tree.TryGetHeight(new Vector2d(0, double.Epsilon), out double height, 0));
                Assert.Equal(Math.Pow(2, -74) + baseline, height);
            }
        }
    }
}
