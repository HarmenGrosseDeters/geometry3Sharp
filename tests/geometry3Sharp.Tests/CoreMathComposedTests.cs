// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3.CoreMathPort;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// CoreMath.AcosByAtan2 (issue 802): the arc cosine composed from the correctly rounded Atan2, the same bits on
    /// every platform, with the domain of Math.Acos and a measured distance to it.
    /// </summary>
    public class CoreMathComposedTests
    {
        readonly ITestOutputHelper output;
        public CoreMathComposedTests(ITestOutputHelper output) { this.output = output; }

        static long Ulps(double a, double b)
        {
            return Math.Abs(BitConverter.DoubleToInt64Bits(a) - BitConverter.DoubleToInt64Bits(b));
        }

        static ulong Next(ref ulong s)
        {
            ulong z = (s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        [Fact]
        public void Domain_AsMathAcos()
        {
            Assert.Equal(0.0, CoreMath.AcosByAtan2(1));
            Assert.Equal(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(CoreMath.AcosByAtan2(1)));     // +0, not -0
            Assert.Equal(Math.PI, CoreMath.AcosByAtan2(-1));
            Assert.Equal(Math.PI / 2, CoreMath.AcosByAtan2(0));
            Assert.Equal(Math.PI / 2, CoreMath.AcosByAtan2(-0.0));
            foreach (double c in new[] { double.NaN, 1.0000000000000002, -1.0000000000000002, 2, -7, 1e300, double.PositiveInfinity, double.NegativeInfinity }) {
                Assert.True(double.IsNaN(CoreMath.AcosByAtan2(c)), c.ToString("R"));
                Assert.True(double.IsNaN(Math.Acos(c)), c.ToString("R"));
            }
            // the neighbours of 1 and -1: finite, positive, below pi, and monotone
            double below1 = 0.99999999999999989, aboveM1 = -0.99999999999999989;
            Assert.True(CoreMath.AcosByAtan2(below1) > 0 && CoreMath.AcosByAtan2(below1) < 2e-8);
            Assert.True(CoreMath.AcosByAtan2(aboveM1) < Math.PI && CoreMath.AcosByAtan2(aboveM1) > Math.PI - 2e-8);
            // tiny arguments: pi / 2 minus less than an ulp
            Assert.Equal(Math.PI / 2, CoreMath.AcosByAtan2(double.Epsilon));
            Assert.Equal(Math.PI / 2, CoreMath.AcosByAtan2(-1e-300));
        }

        // the same bits everywhere: these values are what linux x64 gives; the net48 run on Windows must give them too
        [Theory]
        [InlineData(0.5, "3ff0c152382d7365")]
        [InlineData(-0.5, "4000c152382d7366")]
        [InlineData(0.1, "3ff787b22ce3f590")]
        [InlineData(0.9999, "3f8cf69d216bd74c")]
        [InlineData(-0.9999, "40090504b722c141")]
        [InlineData(0.99999999999, "3ed2c1eb6bf6e971")]
        [InlineData(0.7071067811865476, "3fe921fb54442d18")]
        [InlineData(0.3333333333333333, "3ff3b2028082e8d4")]
        public void PinnedBits(double c, string expected)
        {
            Assert.Equal(expected, BitConverter.DoubleToInt64Bits(CoreMath.AcosByAtan2(c)).ToString("x16"));
        }

        [Fact]
        public void WithinThreeUlpsOfMathAcos_UniformAndNearTheEnds()
        {
            // the distance to the platform's Math.Acos. AcosByAtan2 is within about 1.6 ulps of the true value by derivation
            // and a platform's acos within 1, so 3 is the bound that holds everywhere; measured on glibc x64: 1 ulp at
            // worst, 60501 of the 816000 arguments differ. Sweeps: uniform in [-1, 1], and at distances 2^-k from -1, 0, 1
            ulong s = 12345;
            long worst = 0, differ = 0, count = 0;
            double worstAt = 0;
            Action<double> check = (c) => {
                double a = CoreMath.AcosByAtan2(c), m = Math.Acos(c);
                long u = Ulps(a, m);
                count++;
                if (u != 0) differ++;
                if (u > worst) { worst = u; worstAt = c; }
            };
            for (int i = 0; i < 400000; ++i)
                check((Next(ref s) >> 11) * (1.0 / 9007199254740992.0) * 2 - 1);
            for (int k = 1; k <= 52; ++k)
                for (int i = 0; i < 2000; ++i) {
                    double d = ((Next(ref s) >> 11) * (1.0 / 9007199254740992.0) + 1) * Math.Pow(2, -k - 1);     // in [2^-(k+1), 2^-k)
                    check(1 - d); check(-1 + d); check(d); check(-d);
                }
            output.WriteLine($"{count} arguments, {differ} differ from Math.Acos, worst {worst} ulp at {worstAt:R}");
            Assert.True(worst <= 3, $"{worst} ulps from Math.Acos at {worstAt:R}");
            Assert.True(differ > 0 && differ < count / 4, $"{differ} of {count} differ");      // (composed, so not Math.Acos itself; and close to it)
        }

        [Fact]
        public void DecreasingWithinTwoUlps_AndNotMonotoneInTheLastUlp()
        {
            // over neighbouring doubles the result never rises by more than 2 ulps, and over steps of 1e-5 never at all
            ulong s = 99;
            for (int i = 0; i < 200000; ++i) {
                double c = (Next(ref s) >> 11) * (1.0 / 9007199254740992.0) * 2 - 1;
                double up = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(c) + (c >= 0 ? 1 : -1));     // the next double above c
                if (!(up <= 1)) continue;
                double a = CoreMath.AcosByAtan2(c), b = CoreMath.AcosByAtan2(up);
                Assert.True(b <= a || Ulps(a, b) <= 2, $"acos rises by {Ulps(a, b)} ulps at {c:R}");
            }
            double prev = double.PositiveInfinity;
            for (int i = 0; i <= 200000; ++i) {
                double c = Math.Min(1, -1 + i * (2.0 / 200000));
                double a = CoreMath.AcosByAtan2(c);
                Assert.True(a <= prev, $"acos increases at {c:R}");
                prev = a;
            }
            // the documented pair: one ulp up in c, one ulp up in the result (1 + c and 1 - c round at different arguments)
            double c0 = 0.23144537602828275, c1 = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(c0) + 1);
            Assert.Equal(1.3372331895174001, CoreMath.AcosByAtan2(c0));
            Assert.Equal(1.3372331895174003, CoreMath.AcosByAtan2(c1));
        }
    }
}
