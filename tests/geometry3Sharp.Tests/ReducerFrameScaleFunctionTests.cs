using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> the scale of the quadric frame of the Reducer (an internal function, in its own file: the other Reducer scale tests compile against the base) </summary>
    public class ReducerFrameScaleFunctionTests
    {
        [Fact]
        public void FrameScale_IsAPowerOfTwoWithANormalReciprocal_AndOneInTheBand()
        {
            Func<double, double> scaleOf = ext => Reducer.frame_scale_for(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(ext, 0.25 * ext, 0.5 * ext)));
            foreach (int e in new[] { -32, -20, 0, 20, 32 })
                Assert.Equal(1.0, scaleOf(Math.Pow(2, e)));                // the band: unchanged arithmetic
            foreach (int e in new[] { -1000, -500, -33, 33, 500, 1000 }) {
                double ext = Math.Pow(2, e), f = scaleOf(ext);
                Assert.Equal(Math.Pow(2, -e), f);                           // the extent becomes [1, 2)
                Assert.True(f > 0 && !double.IsInfinity(f) && 1.0 / f > 0 && !double.IsInfinity(1.0 / f));
            }
            foreach (double ext in new[] { Math.Pow(2, -1001), Math.Pow(2, -1067), 5e-324, Math.Pow(2, 1001), 1.7e308, 0.0, double.NaN })
                Assert.Equal(1.0, scaleOf(ext));                            // out of the range of a usable scale: unscaled
        }
    }
}
