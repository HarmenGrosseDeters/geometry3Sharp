using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact 2D decisions through the public API only (no internal members), so the same tests compile against the base library:
    /// cases from the audit where the old absolute 1e-8 constants gave a false hit or a false miss.
    /// </summary>
    public class IntrExact2dPublicApiTests
    {
        [Fact]
        public void Regression_SegmentSegment_5e9Apart_IsAMiss()
        {
            var a = new Segment2d(new Vector2d(0, 0), new Vector2d(1, 0));
            var b = new Segment2d(new Vector2d(1 + 5e-9, -0.5), new Vector2d(1 + 5e-9, 0.5));
            Assert.False(new IntrSegment2Segment2(a, b).Find());
            Assert.False(new IntrSegment2Segment2(b, a).Find());
        }

        [Fact]
        public void Regression_LineSegment_5e9Beside_IsAMiss()
        {
            var line = new Line2d(new Vector2d(0, 0), new Vector2d(1, 0));
            var seg = new Segment2d(new Vector2d(0.5, 5e-9), new Vector2d(0.5, 1));
            Assert.False(new IntrLine2Segment2(line, seg).Find());
        }

        [Fact]
        public void SegmentTriangle_5e9Beside_IsAMiss()
        {
            var tri = new Triangle2d(new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(0, 1));
            var seg = new Segment2d(new Vector2d(-1, -5e-9), new Vector2d(2, -5e-9));
            Assert.False(new IntrSegment2Triangle2(seg, tri).Find());
            Assert.False(new IntrLine2Triangle2(new Line2d(new Vector2d(0, -5e-9), new Vector2d(1, 0)), tri).Find());
        }

        [Fact]
        public void TinyTriangle_AwayFromTheSegment_IsAMiss()
        {
            // a triangle of size 1e-9: the old absolute tolerances treated every segment near it as a hit or a miss by rounding
            var tri = new Triangle2d(new Vector2d(0.3, 0.3), new Vector2d(0.3 + 1e-9, 0.3), new Vector2d(0.3, 0.3 + 1e-9));
            var far = new Segment2d(new Vector2d(0, 1), new Vector2d(1, 1));
            Assert.False(new IntrSegment2Triangle2(far, tri).Find());
            var through = new Segment2d(new Vector2d(0, 0.3 + 2e-10), new Vector2d(1, 0.3 + 2e-10));
            Assert.True(new IntrSegment2Triangle2(through, tri).Find());
            var beside = new Segment2d(new Vector2d(0, 0.3 + 2e-9), new Vector2d(1, 0.3 + 2e-9));
            Assert.False(new IntrSegment2Triangle2(beside, tri).Find());
        }

        [Fact]
        public void NearlyParallelSegments_AreDecidedExactly()
        {
            // two segments that cross by a hair: slopes differ by 1e-12, they cross at x = 0.5
            var a = new Segment2d(new Vector2d(0, 0), new Vector2d(1, 0));
            var b = new Segment2d(new Vector2d(0, -0.5e-12), new Vector2d(1, 0.5e-12));
            var f = new IntrSegment2Segment2(a, b);
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Point, f.Type);
            Assert.Equal(0.5, f.Point0.x, 6);
            // and the same pair one hair apart does not cross
            var c = new Segment2d(new Vector2d(0, 1e-9), new Vector2d(1, 1e-9 + 1e-12));
            Assert.False(new IntrSegment2Segment2(a, c).Find());
        }

        [Fact]
        public void Regression_NonFiniteInput_IsNeverAHit()
        {
            var a = new Segment2d(new Vector2d(0, 0), new Vector2d(1, 1));
            var nan = new Segment2d(new Vector2d(double.NaN, 0), new Vector2d(1, 0));
            Assert.False(new IntrSegment2Segment2(a, nan).Find());
        }
    }
}
