using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The value of an imposed sign (sparse mode with refinement) is 1e-3 of the largest distance from the iso value among the finite values of its
    /// edge or face (#1376, item 3), so it follows the size of the field's values. (Uses internal members of the change: the regressions that
    /// compile against the library before it are in DualContouringSparseScaleTests.)
    /// </summary>
    public class DualContouringImposeStepTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-300)]
        [InlineData(-140)]
        [InlineData(200)]
        public void ImposeStep_IsAThousandthOfTheLargestDistance_AtAnyScale(int k)
        {
            var dc = new DualContouring { IsoValue = 0 };
            double c = ExactPoint3.ScaleB(1.0, k);
            double[] values = { 0.25 * c, -c, double.PositiveInfinity, 0.5 * c, double.NaN };
            Assert.Equal(1e-3 * c, dc.impose_step(values));
            // an iso value: the distance from it (exact in the scale of the values)
            var shifted = new DualContouring { IsoValue = 4 * c };
            Assert.Equal(1e-3 * 5 * c, shifted.impose_step(new[] { 4 * c, -c, 6 * c }));
        }

        [Fact]
        public void ImposeStep_NoDistance_Is1e3Absolute()
        {
            var dc = new DualContouring { IsoValue = 0 };
            Assert.Equal(1e-3, dc.impose_step(new[] { 0.0, 0.0, 0.0 }));
            Assert.Equal(1e-3, dc.impose_step(new[] { double.PositiveInfinity, double.NaN }));
        }

        // the values stored for a node: a float where a float holds it (the bits of before), the same 24 bit mantissa at its own exponent outside
        [Theory]
        [InlineData(1.0)]
        [InlineData(-0.3)]
        [InlineData(1e-30)]
        [InlineData(3e37)]
        public void StoredValue_InTheFloatRange_IsTheFloat(double v)
        {
            Assert.Equal((double)(float)v, DualContouring.store_value(v));
        }

        [Theory]
        [InlineData(-140)]
        [InlineData(-300)]
        [InlineData(130)]
        [InlineData(300)]
        public void StoredValue_OutsideTheFloatRange_IsTheMantissaOfTheFloatAtItsExponent(int k)
        {
            foreach (double v in new[] { 1.0, -0.3, 0.7 }) {
                double stored = DualContouring.store_value(ExactPoint3.ScaleB(v, k));
                Assert.Equal(ExactPoint3.ScaleB((double)(float)v, k), stored);
            }
            Assert.Equal(0.0, DualContouring.store_value(0.0));
            Assert.Equal((double)(float.MaxValue / 2), DualContouring.store_value(double.PositiveInfinity));
            Assert.Equal(-(double)(float.MaxValue / 2), DualContouring.store_value(double.NegativeInfinity));
        }
    }
}
