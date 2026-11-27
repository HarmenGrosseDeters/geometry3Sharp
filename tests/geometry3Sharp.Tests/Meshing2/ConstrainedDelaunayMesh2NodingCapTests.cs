using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1400: verifies that both NodingLimit runaway guards (consecutive splits and crossings) still stop
    /// a livelock in Builder.InsertConstraints. Kept separate from ConstrainedDelaunayMesh2OverlapTests so
    /// the regression tests compile against master for CI's New tests on base check.
    /// </summary>
    public class ConstrainedDelaunayMesh2NodingCapTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void RunawayNoding_IsStoppedByNodingLimit(int stallMode)
        {
            var input = new ConstrainedMesh2Input();
            input.Points.Add(new Vector2d(0, 0));
            input.Points.Add(new Vector2d(10, 0));
            input.Points.Add(new Vector2d(10, 10));
            input.Points.Add(new Vector2d(0, 10));
            input.Segments.Add(new Mesh2Segment(0, 2));

            ConstrainedDelaunayMesh2.Builder.StallNodingModeForTests = stallMode;
            try {
                var result = new ConstrainedDelaunayMesh2().Compute(input);
                Assert.Equal(Triangulation2Status.Failed, result.Status);
                Assert.Equal(Triangulation2FailureKind.NodingLimit, result.FailureKind);
            } finally {
                ConstrainedDelaunayMesh2.Builder.StallNodingModeForTests = 0;
            }

            // Without the stall hook, the same input succeeds cleanly.
            var ok = new ConstrainedDelaunayMesh2().Compute(input);
            Assert.Equal(Triangulation2Status.Success, ok.Status);
        }
    }
}
