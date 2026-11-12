// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// exp at the smallest arguments (#718): the accurate stage nudges the low part of the result when it is a power of two
// (CORE-MATH: `(ix << 12) == 0`); the port tested `(ix & ~0ul) >> 12`, so exp(2^-53) = 1 + 2^-53 + 2^-107 was rounded to 1.
// The expected values come from CoreMathReference (BigInteger, no shared code).
using System;
using g3.CoreMathPort;
using Xunit;

namespace g3.Tests
{
    public class CoreMathExpSmallArgumentTests
    {
        static long Bits(double d) { return BitConverter.DoubleToInt64Bits(d); }
        static double FromBits(long b) { return BitConverter.Int64BitsToDouble(b); }

        [Fact]
        public void TwoToTheMinus53_IsRoundedUp()
        {
            double x = Math.Pow(2, -53);
            Assert.Equal(Bits(1.0 + Math.Pow(2, -52)), Bits(CoreMathReference.Exp(x)));
            Assert.Equal(Bits(1.0 + Math.Pow(2, -52)), Bits(CoreMath.Exp(x)));
        }

        [Fact]
        public void SweepAroundTheRoundingThresholds_IsCorrectlyRounded()
        {
            // exp(x) = 1 + x + x^2/2 is a tie of 1 + 2^-52 / 2 at x = 2^-53: sweep 200 ulps on both sides of the threshold and of
            // the neighbouring powers of two (also negative), and every power of two from 2^-1 to 2^-60 with its neighbours
            int count = 0;
            foreach (int k in new[] { 52, 53, 54 })
                for (int d = -100; d <= 100; ++d)
                    foreach (double sgn in new[] { 1.0, -1.0 }) {
                        double x = sgn * FromBits(Bits(Math.Pow(2, -k)) + d);
                        Check(x);
                        ++count;
                    }
            for (int k = 1; k <= 60; ++k)
                foreach (double sgn in new[] { 1.0, -1.0 })
                    for (int d = -3; d <= 3; ++d) {
                        Check(sgn * FromBits(Bits(Math.Pow(2, -k)) + d));
                        ++count;
                    }
            Assert.True(count > 1000);
        }

        static void Check(double x)
        {
            double got = CoreMath.Exp(x), want = CoreMathReference.Exp(x);
            Assert.True(Bits(got) == Bits(want), $"exp({x:R}) = {got:R}, want {want:R}");
        }
    }
}
