using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshAlphaWrap.CornerPinchPoints: the Steiner points of the exception to the separation bound (pinch points for cells with a
    /// box corner, only on the retry after box corners on the result). It agrees with Counts["pinch_corner_points"], and a result
    /// without the retry has none
    /// </summary>
    public class AlphaWrapCornerPinchPointsTests
    {
        static double Unit(ref uint s) { s = s * 1664525u + 1013904223u; return ((s >> 8) & 0xFFFFFF) / (double)0x1000000; }

        /// <summary> 30 random points of the square z = 0 in [-1, 1]^2 (the flat scene of the alpha wrap campaign, kind 5), relative alpha 5 </summary>
        static MeshAlphaWrap Flat(int seed, bool refine)
        {
            uint state = (uint)seed * 2654435761u + 977u;
            var pts = new System.Collections.Generic.List<Vector3d>();
            for (int i = 0; i < 30; ++i) pts.Add(new Vector3d(2 * Unit(ref state) - 1, 2 * Unit(ref state) - 1, 0));
            return new MeshAlphaWrap(pts) { RelativeAlpha = 5, RelativeOffset = 600, RefineNonManifold = refine };
        }

        [Theory]
        [InlineData(129, false)]
        [InlineData(104, true)]
        public void FlatInput_RetryPlacesThePoints(int seed, bool refine)
        {
            MeshAlphaWrap w = Flat(seed, refine);
            Assert.True(w.Compute(), w.Status + " " + w.Error);
            Assert.Equal(1, w.Counts["box_retries"]);
            Assert.Equal(w.Counts.TryGetValue("pinch_corner_points", out long k) ? k : 0, w.CornerPinchPoints);
        }

        [Fact]
        public void NoRetry_NoPoints_AndComputeAgainResetsIt()
        {
            // a sphere-like point set wraps at the first attempt
            var pts = new System.Collections.Generic.List<Vector3d>();
            var rnd = new System.Random(3);
            for (int i = 0; i < 40; ++i) pts.Add(new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()));
            var w = new MeshAlphaWrap(pts) { RelativeAlpha = 10, RelativeOffset = 300 };
            Assert.True(w.Compute(), w.Status + " " + w.Error);
            Assert.False(w.Counts.ContainsKey("box_retries"));
            Assert.Equal(0, w.CornerPinchPoints);
        }

        [Fact]
        public void ReusedInstance_ReportsTheLastCompute()
        {
            MeshAlphaWrap w = Flat(129, false);
            Assert.True(w.Compute());
            int first = w.CornerPinchPoints;
            Assert.True(w.Compute());
            Assert.Equal(first, w.CornerPinchPoints);
        }
    }
}
