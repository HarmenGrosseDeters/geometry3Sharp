// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The 128-bit helper type of the vendored sin / cos / log (Dint: sign, exponent, hi:lo significand) against BigInteger.
using System;
using System.Numerics;
using g3.CoreMathPort;
using Xunit;
using Xunit.Abstractions;

namespace g3.Tests
{
    public class CoreMathDintTests
    {
        readonly ITestOutputHelper output;
        public CoreMathDintTests(ITestOutputHelper output) { this.output = output; }

        static ulong SplitMix(ref ulong s)
        {
            ulong z = (s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        // value = (-1)^sgn * (hi:lo) * 2^(ex - 128), as an exact rational scaled by 2^S
        const int S = 1500;
        static BigInteger Scaled(CoreMath.Dint d)
        {
            BigInteger m = ((BigInteger)d.hi << 64) | d.lo;
            int sh = (int)d.ex - 128 + S;
            BigInteger v = sh >= 0 ? m << sh : m >> -sh;
            return d.sgn != 0 ? -v : v;
        }

        [Fact]
        public void FromDouble_IsExact()
        {
            foreach (double x in new[] { 1.0, 0.75, 3.141592653589793, 1e300, 1e-300, -2.5, 123456.789 }) {
                BigInteger got = Scaled(CoreMath.Dint.FromDouble(x));          // x * 2^S
                BigInteger want = CoreMathReference.ExactFixed(x) << (S - CoreMathReference.Precision);
                if (S < CoreMathReference.Precision) want = CoreMathReference.ExactFixed(x) >> (CoreMathReference.Precision - S);
                Assert.True(got == want, $"FromDouble({x:R})");
            }
        }

        [Fact]
        public void Reduce_OfLargeArguments_IsTheFractionalPartOfXOverTwoPi()
        {
            ulong s = 21;
            ulong[] T = CoreMath.CosTableForTests;
            BigInteger K = CoreMathReference.InverseTwoPi;              // 1/(2 pi) * 2^W
            int W = CoreMathReference.Precision;
            int bad = 0;
            for (int i = 0; i < 400; ++i) {
                // exponent from 2^2 to 2^1023
                int e = 1023 + 2 + (int)(SplitMix(ref s) % 1020);
                long bits = ((long)e << 52) | (long)(SplitMix(ref s) & ((1UL << 52) - 1));
                double x = BitConverter.Int64BitsToDouble(bits);
                CoreMath.Dint X = CoreMath.Dint.FromDouble(x);
                CoreMath.Dint R = X.Reduce(T);
                // exact fractional part of x / (2 pi), in units 2^-W
                BigInteger prod = CoreMathReference.ExactFixed(x) * K;      // scale 2^(2W)
                BigInteger frac = prod & ((BigInteger.One << (2 * W)) - 1);  // fractional part, scale 2^(2W)
                // R as a value in [0,1) (sign handled: the reduction returns |frac| up to the sign flag)
                BigInteger got = Scaled(R);                                  // scale 2^S
                // convert frac (scale 2^(2W)=2^3200) to scale 2^S (S=1500): shift right 1700
                BigInteger want = frac >> (2 * W - S);
                BigInteger diff = BigInteger.Abs(got - want);
                // tolerance: 2^-120 absolute (S = 1500 -> 2^(1500-120))
                if (diff > (BigInteger.One << (S - 120))) {
                    ++bad;
                    if (bad <= 5) output.WriteLine($"x = {x:R} (e = {e}): Reduce differs by 2^{CoreMathReference.BitLength(diff) - S}");
                }
            }
            Assert.True(bad == 0, $"{bad} of 400 reductions are not the fractional part of x / (2 pi)");
        }

        [Fact]
        public void Mul21_IsTheProductWithTheHighLimbOfTheSecondOperand()
        {
            // the port had lost a "- 1" in the exponent: the product was twice too large, which doubled the highest
            // polynomial term of the accurate sin / cos paths (an error of 2^-105 for arguments with X near 2^-11)
            ulong s = 31;
            for (int i = 0; i < 2000; ++i) {
                var a = new CoreMath.Dint(SplitMix(ref s), SplitMix(ref s) | (1UL << 63), (long)(SplitMix(ref s) % 7) - 3, SplitMix(ref s) & 1);
                var b = new CoreMath.Dint(0, SplitMix(ref s) | (1UL << 63), (long)(SplitMix(ref s) % 7) - 3, SplitMix(ref s) & 1);
                BigInteger exact = (Scaled(a) * Scaled(b)) >> S;
                BigInteger got = Scaled(CoreMath.Dint.Mul21(a, b));
                BigInteger diff = BigInteger.Abs(got - exact);
                Assert.True(diff == 0 || CoreMathReference.BitLength(diff) - CoreMathReference.BitLength(exact) < -120, $"Mul21 case {i}");
            }
        }

        [Fact]
        public void Mul_And_Add_AreAccurateTo2PowMinus120()
        {
            ulong s = 32;
            for (int i = 0; i < 3000; ++i) {
                var a = new CoreMath.Dint(SplitMix(ref s), SplitMix(ref s) | (1UL << 63), (long)(SplitMix(ref s) % 7) - 3, SplitMix(ref s) & 1);
                var b = new CoreMath.Dint(SplitMix(ref s), SplitMix(ref s) | (1UL << 63), (long)(SplitMix(ref s) % 7) - 3, SplitMix(ref s) & 1);
                BigInteger exactMul = (Scaled(a) * Scaled(b)) >> S, exactAdd = Scaled(a) + Scaled(b);
                BigInteger dm = BigInteger.Abs(Scaled(CoreMath.Dint.Mul(a, b)) - exactMul), da = BigInteger.Abs(Scaled(CoreMath.Dint.Add(a, b)) - exactAdd);
                Assert.True(dm == 0 || CoreMathReference.BitLength(dm) - CoreMathReference.BitLength(exactMul) < -120, $"Mul case {i}");
                if (!exactAdd.IsZero)
                    Assert.True(da == 0 || CoreMathReference.BitLength(da) - CoreMathReference.BitLength(exactAdd) < -120, $"Add case {i}");
            }
        }
    }
}
