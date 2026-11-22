using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The internal tolerance route of IntrSegment2Triangle2 (kept for the old floating-point branch of
    /// IntrTriangle3Triangle3.Find). In a file of its own: it uses a member the base library does not have.
    /// </summary>
    public class IntrSegment2Triangle2ToleranceRouteTests
    {
        [Fact]
        public void UseToleranceRoute_KeepsTheOldTolerance_ForTheTriangleTriangleBranch()
        {
            var tri = new Triangle2d(new Vector2d(0, 0), new Vector2d(4, 0), new Vector2d(0, 4));
            var seg = new Segment2d(new Vector2d(1, -5e-9), new Vector2d(2, -5e-9));       // 5e-9 below the edge y = 0
            Assert.False(new IntrSegment2Triangle2(seg, tri).Find());
            Assert.True(new IntrSegment2Triangle2(seg, tri) { UseToleranceRoute = true }.Find());
        }
    }
}
