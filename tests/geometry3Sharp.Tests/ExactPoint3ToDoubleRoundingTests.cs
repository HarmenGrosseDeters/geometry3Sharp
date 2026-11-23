using System;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactPoint3.ToDoubleRounded: num / den * 2^scaleExp correctly rounded to nearest, ties to even, in one rounding also in the
    /// subnormal range, compared with an exact rational check (the result is no farther from the value than either neighbouring double, and
    /// the even one of two equally near ones) that does not use the library's arithmetic.
    /// </summary>
    public class ExactPoint3ToDoubleRoundingTests
    {
        // value = num / den * 2^scaleExp as a fraction n / d
        static void Fraction(BigInteger num, BigInteger den, int scaleExp, out BigInteger n, out BigInteger d)
        {
            n = num; d = den;
            if (scaleExp >= 0) n <<= scaleExp; else d <<= -scaleExp;
        }

        static (BigInteger n, BigInteger d) FromDouble(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            bool neg = bits < 0; int e = (int)((bits >> 52) & 0x7FF); long m = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) e = 1; else m |= 1L << 52;
            e -= 1075;
            BigInteger n = m; if (neg) n = -n;
            return e >= 0 ? (n << e, BigInteger.One) : (n, BigInteger.One << -e);
        }

        // |a/b - c/d|, as a fraction
        static (BigInteger n, BigInteger d) AbsDiff((BigInteger n, BigInteger d) x, (BigInteger n, BigInteger d) y)
        {
            BigInteger n = x.n * y.d - y.n * x.d;
            return (BigInteger.Abs(n), x.d * y.d);
        }
        static int Cmp((BigInteger n, BigInteger d) x, (BigInteger n, BigInteger d) y) => (x.n * y.d).CompareTo(y.n * x.d);

        static double Up(double x) => x == 0 ? double.Epsilon : BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x > 0 ? 1 : -1));
        static double Down(double x) => x == 0 ? -double.Epsilon : BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x > 0 ? -1 : 1));
        static bool IsEven(double x) => (BitConverter.DoubleToInt64Bits(x) & 1) == 0;

        static readonly BigInteger MaxDouble = ((BigInteger.One << 53) - 1) << 971;                 // 2^1024 - 2^971
        static readonly BigInteger OverflowTie = (BigInteger.One << 1024) - (BigInteger.One << 970);  // max + half an ulp

        static void AssertCorrectlyRounded(BigInteger num, BigInteger den, int scaleExp, double got, string what)
        {
            BigInteger fn, fd; Fraction(num, den, scaleExp, out fn, out fd);
            if (fd.Sign < 0) { fn = -fn; fd = -fd; }
            var v = (n: fn, d: fd);
            var av = (n: BigInteger.Abs(fn), d: fd);
            if (double.IsInfinity(got)) {
                // the value is at or above the middle between the largest double and 2^1024 (a tie goes to the even 2^1024)
                Assert.True(Cmp(av, (OverflowTie, BigInteger.One)) >= 0, what + ": infinity for a value below the overflow tie");
                Assert.Equal(fn.Sign < 0, got < 0);
                return;
            }
            Assert.False(double.IsNaN(got), what);
            if (got == 0) {
                // at most half the smallest subnormal (2^-1075; a tie goes to the even 0)
                Assert.True(Cmp(av, (BigInteger.One, BigInteger.One << 1075)) <= 0, what + ": zero for a value above 2^-1075");
                if (fn.Sign != 0) Assert.Equal(fn.Sign < 0, BitConverter.DoubleToInt64Bits(got) < 0);
                return;
            }
            var r = FromDouble(got);
            var err = AbsDiff(v, r);
            foreach (double nb in new[] { Up(got), Down(got) }) {
                if (double.IsInfinity(nb)) {
                    // the neighbour beyond the largest double is 2^1024
                    var nbf = (n: (got > 0 ? BigInteger.One : -BigInteger.One) << 1024, d: BigInteger.One);
                    int c0 = Cmp(err, AbsDiff(v, nbf));
                    Assert.True(c0 <= 0, what + ": a nearer double (2^1024)");
                    continue;
                }
                int c = Cmp(err, AbsDiff(v, FromDouble(nb)));
                Assert.True(c <= 0, what + ": a nearer double " + nb.ToString("R") + " than " + got.ToString("R"));
                if (c == 0) Assert.True(IsEven(got), what + ": a tie not rounded to even " + got.ToString("R"));
            }
        }

        static BigInteger Random(Random r, int bits)
        {
            var b = new byte[bits / 8 + 2];
            r.NextBytes(b);
            b[b.Length - 1] &= 0x7F;
            BigInteger v = new BigInteger(b);
            v >>= (b.Length * 8 - 8) - bits;
            return v;
        }

        [Fact]
        public void RandomFractions_AreCorrectlyRounded()
        {
            var r = new Random(1410);
            for (int i = 0; i < 4000; i++) {
                BigInteger num = BigInteger.Abs(Random(r, r.Next(1, 300))) + 1, den = BigInteger.Abs(Random(r, r.Next(1, 300))) + 1;
                if (r.Next(3) == 0) num = -num;
                int sc = r.Next(-60, 60) + (r.Next(4) == 0 ? r.Next(-1100, 1100) : 0);
                AssertCorrectlyRounded(num, den, sc, ExactPoint3.ToDoubleRounded(num, den, sc), $"case {i}");
            }
        }

        [Fact]
        public void Reproducer_TruncatedQuotientIsOneUlpOff()
        {
            // a quotient just below 1 whose 64-bit truncation rounds toward zero in (double)q: 1 - 2^-60 rounds to 1 (nearest), not to 1 - 2^-53
            BigInteger one = BigInteger.One;
            double got = ExactPoint3.ToDoubleRounded((one << 60) - 1, one << 60, 0);
            Assert.Equal(1.0, got);
            // 3 / 7 (inexact): the correctly rounded double
            Assert.Equal(3.0 / 7.0, ExactPoint3.ToDoubleRounded(3, 7, 0));
            Assert.Equal(1.0 / 3.0, ExactPoint3.ToDoubleRounded(1, 3, 0));
            Assert.Equal(2.0 / 3.0, ExactPoint3.ToDoubleRounded(2, 3, 0));
            Assert.Equal(-10.0 / 7.0, ExactPoint3.ToDoubleRounded(-10, 7, 0));
            Assert.Equal(0.1, ExactPoint3.ToDoubleRounded(1, 10, 0));
        }

        [Fact]
        public void Ties_GoToEven_AndAStickyBitBreaksThem()
        {
            BigInteger one = BigInteger.One;
            BigInteger p53 = one << 53;
            // 2^53 + 1 is the middle of 2^53 and 2^53 + 2: ties to the even mantissa 2^53; 2^53 + 3 goes to 2^53 + 4
            Assert.Equal(9007199254740992.0, ExactPoint3.ToDoubleRounded(p53 + 1, 1, 0));
            Assert.Equal(9007199254740996.0, ExactPoint3.ToDoubleRounded(p53 + 3, 1, 0));
            // the same ties as quotients and with exponents
            Assert.Equal(9007199254740992.0 * 1024, ExactPoint3.ToDoubleRounded((p53 + 1) * 5, 5, 10));
            // a tie plus a sticky bit goes up
            Assert.Equal(9007199254740994.0, ExactPoint3.ToDoubleRounded(((p53 + 1) << 100) + 1, one << 100, 0));
            // and a tie minus a sticky bit goes down
            Assert.Equal(9007199254740992.0, ExactPoint3.ToDoubleRounded(((p53 + 1) << 100) - 1, one << 100, 0));
        }

        [Fact]
        public void SubnormalBoundary_IsRoundedOnce()
        {
            BigInteger one = BigInteger.One;
            // the smallest subnormal 2^-1074 and its half: 2^-1075 is a tie to the even 0; just above it rounds up, just below to 0
            Assert.Equal(double.Epsilon, ExactPoint3.ToDoubleRounded(one, 1, -1074));
            Assert.Equal(0.0, ExactPoint3.ToDoubleRounded(one, 1, -1075));
            Assert.Equal(double.Epsilon, ExactPoint3.ToDoubleRounded((one << 10) + 1, one << 10, -1075));
            Assert.Equal(0.0, ExactPoint3.ToDoubleRounded((one << 10) - 1, one << 10, -1075));
            Assert.Equal(-0.0, ExactPoint3.ToDoubleRounded(-one, 1, -1075));
            Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(ExactPoint3.ToDoubleRounded(-one, 1, -1075)));
            // 1.5 * 2^-1074 ties to even: 2 * 2^-1074
            Assert.Equal(2 * double.Epsilon, ExactPoint3.ToDoubleRounded(3, 2, -1074));
            // the boundary between the largest subnormal and the smallest normal: 2^-1022 - 2^-1075 is a tie between them
            BigInteger tie = (one << 53) - 1;      // (2^53 - 1) * 2^-1075 = 2^-1022 - 2^-1075
            Assert.Equal(Math.Pow(2, -1022), ExactPoint3.ToDoubleRounded(tie, 1, -1075));       // the even one is the normal 2^-1022
            Assert.Equal(Math.Pow(2, -1022) - double.Epsilon, ExactPoint3.ToDoubleRounded(tie - 1, 1, -1075));
            // a value within the subnormals rounds at the grid, not first to 53 bits: (2^60 + 2^6 + 1) 2^-1130 is above the middle of two units
            BigInteger v = (one << 60) + (one << 6) + 1;
            AssertCorrectlyRounded(v, 1, -1130, ExactPoint3.ToDoubleRounded(v, 1, -1130), "double rounding");
            for (int k = 0; k < 200; k++)
                AssertCorrectlyRounded((one << 53) + k, 1, -1074 - 53 - 1 + (k % 5), ExactPoint3.ToDoubleRounded((one << 53) + k, 1, -1074 - 53 - 1 + (k % 5)), "subnormal " + k);
        }

        [Fact]
        public void OverflowEnd_AndUnderflowEnd()
        {
            BigInteger one = BigInteger.One;
            double max = double.MaxValue;
            Assert.Equal(max, ExactPoint3.ToDoubleRounded(MaxDouble, 1, 0));
            // the tie between the largest double and 2^1024 goes to the even 2^1024: an infinity; one below it is the largest double
            Assert.Equal(double.PositiveInfinity, ExactPoint3.ToDoubleRounded(OverflowTie, 1, 0));
            Assert.Equal(max, ExactPoint3.ToDoubleRounded(OverflowTie - 1, 1, 0));
            Assert.Equal(double.NegativeInfinity, ExactPoint3.ToDoubleRounded(-OverflowTie, 1, 0));
            Assert.Equal(max, ExactPoint3.ToDoubleRounded(OverflowTie * 3 - 3, 3, 0));
            Assert.Equal(double.PositiveInfinity, ExactPoint3.ToDoubleRounded(1, 1, 5000));
            Assert.Equal(0.0, ExactPoint3.ToDoubleRounded(1, 1, -5000));
            Assert.Equal(0.0, ExactPoint3.ToDoubleRounded(1, 1, -1000000000));
            Assert.Equal(double.PositiveInfinity, ExactPoint3.ToDoubleRounded(1, 1, 1000000000));
            // an exponent near the int limits must not wrap around (num much larger than den makes the shift negative)
            Assert.Equal(double.PositiveInfinity, ExactPoint3.ToDoubleRounded(one << 100, 1, int.MaxValue));
            Assert.Equal(double.PositiveInfinity, ExactPoint3.ToDoubleRounded(one << 100, 1, int.MaxValue - 10));
            Assert.Equal(double.NegativeInfinity, ExactPoint3.ToDoubleRounded(-(one << 100), 1, int.MaxValue - 1));
            Assert.Equal(double.PositiveInfinity, ExactPoint3.ToDoubleRounded(one << 5000, 1, int.MaxValue - 4000));
            Assert.Equal(0.0, ExactPoint3.ToDoubleRounded(one << 100, 1, int.MinValue + 60));
            Assert.Equal(0.0, ExactPoint3.ToDoubleRounded(1, one << 5000, int.MinValue));
            Assert.Equal(double.PositiveInfinity, ExactPoint3.ToDoubleRounded(1, 1, int.MaxValue));
            Assert.Equal(Math.Pow(2, -100), ExactPoint3.ToDoubleRounded(one << 5000, one << 3000, -2100));      // 2^2000 * 2^-2100
        }

        [Fact]
        public void PowerOfTwoScales_AreExact_AndBitForBit()
        {
            var r = new Random(77);
            for (int i = 0; i < 500; i++) {
                BigInteger num = BigInteger.Abs(Random(r, r.Next(1, 200))) + 1, den = BigInteger.Abs(Random(r, r.Next(1, 200))) + 1;
                double basis = ExactPoint3.ToDoubleRounded(num, den, 0);
                foreach (int k in new[] { -300, -37, 5, 100, 400 }) {
                    double scaled = ExactPoint3.ToDoubleRounded(num, den, k);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(basis * Math.Pow(2, k)), BitConverter.DoubleToInt64Bits(scaled));
                }
            }
        }

        [Fact]
        public void ToDouble_IsWithinOneUlp_AsDocumented()
        {
            // the conversion of the exact pipeline is not correctly rounded by design: within 1 ulp of the correctly rounded value
            var r = new Random(1411);
            for (int i = 0; i < 2000; i++) {
                BigInteger num = BigInteger.Abs(Random(r, r.Next(1, 200))) + 1, den = BigInteger.Abs(Random(r, r.Next(1, 200))) + 1;
                int sc = r.Next(-300, 300);
                double exact = ExactPoint3.ToDoubleRounded(num, den, sc), got = ExactPoint3.ToDouble(num, den, sc);
                Assert.True(got == exact || got == Up(exact) || got == Down(exact), $"case {i}: {got:R} against {exact:R}");
            }
        }

    }
}
