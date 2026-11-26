using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1612: Math.Floor / Ceiling(Math.Log(x, 2)) as a binary exponent is off by one at exact powers of two (a platform libm value);
    /// MathUtil.FloorLog2 / CeilLog2 read the exponent bits instead.
    /// </summary>
    public class BinaryExponentTests
    {
        [Fact]
        public void FloorAndCeilLog2_AreExactForEveryPowerOfTwo_AlsoSubnormal()
        {
            for (int k = -1074; k <= 1023; ++k) {
                double p = Math.Pow(2, k);
                if (k == -1074) p = BitConverter.Int64BitsToDouble(1L);   // Math.Pow(2, -1074) is exact too, but spell the smallest subnormal by its bits
                Assert.Equal(k, MathUtil.FloorLog2(p));
                Assert.Equal(k, MathUtil.CeilLog2(p));
                // one ulp above: floor stays, ceil goes up (the largest finite double has no ulp above)
                if (k >= -1022 && k < 1023) {   // below 2^-1022 the ulp is 2^-1074, so the neighbour is another binade: covered by Subnormals_AllMantissaLengths
                    double up = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(p) + 1);
                    Assert.Equal(k, MathUtil.FloorLog2(up));
                    Assert.Equal(k + 1, MathUtil.CeilLog2(up));
                }
                // one ulp below (for 2^-1074 there is none)
                if (k >= -1022) {
                    double down = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(p) - 1);
                    Assert.Equal(k - 1, MathUtil.FloorLog2(down));
                    Assert.Equal(k, MathUtil.CeilLog2(down));
                }
            }
        }

        [Fact]
        public void Subnormals_AllMantissaLengths()
        {
            for (int len = 1; len <= 52; ++len) {
                long m = (1L << len) - 1;   // all ones: not a power of two (except len 1)
                double x = BitConverter.Int64BitsToDouble(m);
                Assert.Equal(len - 1075, MathUtil.FloorLog2(x));
                Assert.Equal(len == 1 ? -1074 : len - 1074, MathUtil.CeilLog2(x));
            }
            Assert.Equal(1023, MathUtil.FloorLog2(double.MaxValue));
            Assert.Equal(1024, MathUtil.CeilLog2(double.MaxValue));
        }

        [Fact]
        public void LogOfPowerOfTwo_WasOffByOne_OnThisPlatformOrNot_TheHelperIsNot()
        {
            // the k that the issue lists for glibc: -59 -58 -55 -51 -47 -39 -31 -29 (floor) and 29 31 39 47 51 55 58 59 (ceiling)
            foreach (int k in new[] { -59, -58, -55, -51, -47, -39, -31, -29, 29, 31, 39, 47, 51, 55, 58, 59 }) {
                Assert.Equal(k, MathUtil.FloorLog2(Math.Pow(2, k)));
                Assert.Equal(k, MathUtil.CeilLog2(Math.Pow(2, k)));
            }
        }

        [Fact]
        public void InvalidInput_IsDefined()
        {
            foreach (double bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                Assert.Throws<ArgumentOutOfRangeException>(() => MathUtil.FloorLog2(bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => MathUtil.CeilLog2(bad));
            }
            Assert.Equal(0, MathUtil.CeilLog2Saturating(double.NaN));
            Assert.Equal(int.MaxValue, MathUtil.CeilLog2Saturating(double.PositiveInfinity));
            Assert.Equal(int.MinValue, MathUtil.CeilLog2Saturating(0));
            Assert.Equal(int.MinValue, MathUtil.CeilLog2Saturating(-3));
            Assert.Equal(30, MathUtil.CeilLog2Saturating(Math.Pow(2, 30)));
            Assert.Equal(31, MathUtil.CeilLog2Saturating(Math.Pow(2, 30) + 1));
        }
    }
}
