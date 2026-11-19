using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The ramp builder's refusals below the smallest feature that can be realized (TerrainSlopeRamps.build_ramps): a ramp
    /// Width, a path segment and a bend's kite far below the coordinates' resolution (about 1e-10 here) are reported as
    /// InvalidInput with the ramp (and segment) named, not built into a degenerate road.
    /// </summary>
    public class CoverageGate2CRampTests
    {
        static void AssertRefused(TerrainSlopeDesign d, string text, Index2i problem)
        {
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            Assert.Contains(text, d.Message);
            Assert.Contains(problem, d.RampProblems);
            Assert.Null(d.SlopeSurface);
        }

        [Fact]
        public void WidthBelowTheSmallestFeature_IsRefused()
        {
            var d = TerrainSlopeRampTests.Straight(10, 1e-12);
            AssertRefused(d, "Width is below the smallest feature that can be realized", new Index2i(0, -1));
            Assert.StartsWith("ramp 0: ", d.Message);
        }

        [Fact]
        public void PathSegmentBelowTheSmallestFeature_IsRefused()
        {
            var d = TerrainSlopeRampTests.Straight(10, 4, new Vector2d(-10 + 1e-12, -20 + 1e-13));
            AssertRefused(d, "the path segment is shorter than the smallest feature that can be realized", new Index2i(0, 0));
            Assert.StartsWith("ramp 0 at 0: ", d.Message);
        }

        [Fact]
        public void NearlyCollinearBend_IsRefusedWithItsKite()
        {
            // a left turn of 1e-13 rad (away from a road on the right): the kite's edges are Width tan(turn / 2), far below
            // the smallest feature
            var d = TerrainSlopeRampTests.Straight(10, 4);
            d.Ramps[0].Path = new List<Vector2d>() { new Vector2d(-10, -20), new Vector2d(0, -20), new Vector2d(10, -20 + 1e-12) };
            AssertRefused(d, "the bend is too slight for its kite", new Index2i(0, 1));
            Assert.Contains("remove the nearly collinear path point", d.Message);
            Assert.StartsWith("ramp 0 at 1: ", d.Message);
        }

        [Fact]
        public void BendAboveTheSmallestFeature_IsBuilt()
        {
            // a bend with a visible turn is realized: the guard refuses only a kite below the smallest feature
            var d = TerrainSlopeRampTests.AroundCorner();
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Empty(d.RampProblems);
        }
    }
}
