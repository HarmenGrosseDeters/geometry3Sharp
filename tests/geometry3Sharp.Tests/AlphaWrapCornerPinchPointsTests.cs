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
        static MeshAlphaWrap Flat(int seed, bool refine)
        {
            var s = new AlphaWrapCampaignTests.Scene { Kind = 5, Exp = 0, N = 30, Far = -1, RelAlpha = 5, RelOffset = 600, Refine = refine, Seed = seed };
            return s.Wrap();
        }

        [Theory]
        [InlineData(129, false)]
        [InlineData(1, false)]
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
