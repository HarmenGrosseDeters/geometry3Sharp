using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ConvexHull2.ExtentExponent (QA 2.0, #644): the binary exponent of the extent that the constructor scales by. In a file of
    /// its own because it uses an internal member (a test file that does not compile against the base library counts as
    /// "does not compile" for every test in it in tools/new_tests_on_base.py).
    /// </summary>
    public class ConvexHull2ExtentExponentTests
    {
        [Fact]
        public void ExtentExponent_IsTheBinaryExponent_AlsoForSubnormals()
        {
            // 2^k <= x < 2^(k+1), exact. (An end-to-end test cannot tell a wrong exponent for a subnormal extent: the scaled extent
            // stays above the 2.2e-16 of Normalize.)
            var values = new List<double> { 5e-324, 1e-323, 3e-320, 1e-310, 2.2250738585072009e-308, 2.2250738585072014e-308, 1e-300, 0.001, 0.5, 0.99999, 1, 1.5, 2, 3, 1000, 1e300, 1.7976931348623157e308 };
            foreach (double x in values) {
                int k = ConvexHull2.ExtentExponent(x);
                Assert.True(ExactPoint3.ScaleB(1, k) <= x && (k == 1023 || x < ExactPoint3.ScaleB(1, k + 1)), $"{x}: exponent {k}");
            }
            Assert.Equal(0, ConvexHull2.ExtentExponent(0));
            Assert.Equal(0, ConvexHull2.ExtentExponent(double.PositiveInfinity));
        }
    }
}
