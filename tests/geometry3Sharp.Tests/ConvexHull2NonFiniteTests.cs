using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> QA 2.0 T1 finding R24: a point that is not finite gave an ArithmeticException from Math.Sign deep inside </summary>
    public class ConvexHull2NonFiniteTests
    {
        static List<Vector2d> Square() => new List<Vector2d> { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(1, 1), new Vector2d(0, 1), new Vector2d(0.5, 0.4) };

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void NonFinitePoint_ThrowsArgumentException(double bad)
        {
            foreach (int index in new[] { 0, 2, 4 })
                foreach (int axis in new[] { 0, 1 }) {
                    List<Vector2d> pts = Square();
                    Vector2d p = pts[index]; p[axis] = bad; pts[index] = p;
                    var e = Assert.Throws<ArgumentException>(() => new ConvexHull2(pts, 1e-9, QueryNumberType.QT_DOUBLE));
                    Assert.Contains("ConvexHull2", e.Message);
                }
        }

        [Fact]
        public void FinitePoints_StillGiveTheHull()
        {
            var hull = new ConvexHull2(Square(), 1e-9, QueryNumberType.QT_DOUBLE);
            Assert.Equal(2, hull.Dimension);
            Assert.Equal(4, hull.NumSimplices);
            Assert.Equal(1.0, hull.GetHullPolygon().Area, 12);
        }
    }
}
