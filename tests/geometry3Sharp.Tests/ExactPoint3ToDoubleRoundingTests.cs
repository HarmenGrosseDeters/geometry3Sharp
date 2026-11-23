using System;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactPoint3.ToDouble (and its internal twin ToDoubleRounded, which must give the same bits, issue #1410): num / den * 2^scaleExp correctly rounded to nearest, ties to even, in one rounding also in the
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

        // ToDouble and ToDoubleRounded (den > 0) are one function: every assertion below holds for both
        static double Conv(BigInteger num, BigInteger den, int scaleExp)
        {
            double a = ExactPoint3.ToDouble(num, den, scaleExp), b = ExactPoint3.ToDoubleRounded(num, den, scaleExp);
            Assert.Equal(BitConverter.DoubleToInt64Bits(b), BitConverter.DoubleToInt64Bits(a));
            return a;
        }

        static long Next62(Random r) { return ((long)r.Next() << 31) ^ r.Next(); }

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
                AssertCorrectlyRounded(num, den, sc, Conv(num, den, sc), $"case {i}");
            }
        }

        [Fact]
        public void Reproducer_TruncatedQuotientIsOneUlpOff()
        {
            // a quotient just below 1 whose 64-bit truncation rounds toward zero in (double)q: 1 - 2^-60 rounds to 1 (nearest), not to 1 - 2^-53
            BigInteger one = BigInteger.One;
            double got = Conv((one << 60) - 1, one << 60, 0);
            Assert.Equal(1.0, got);
            // 3 / 7 (inexact): the correctly rounded double
            Assert.Equal(3.0 / 7.0, Conv(3, 7, 0));
            Assert.Equal(1.0 / 3.0, Conv(1, 3, 0));
            Assert.Equal(2.0 / 3.0, Conv(2, 3, 0));
            Assert.Equal(-10.0 / 7.0, Conv(-10, 7, 0));
            Assert.Equal(0.1, Conv(1, 10, 0));
        }

        [Fact]
        public void Ties_GoToEven_AndAStickyBitBreaksThem()
        {
            BigInteger one = BigInteger.One;
            BigInteger p53 = one << 53;
            // 2^53 + 1 is the middle of 2^53 and 2^53 + 2: ties to the even mantissa 2^53; 2^53 + 3 goes to 2^53 + 4
            Assert.Equal(9007199254740992.0, Conv(p53 + 1, 1, 0));
            Assert.Equal(9007199254740996.0, Conv(p53 + 3, 1, 0));
            // the same ties as quotients and with exponents
            Assert.Equal(9007199254740992.0 * 1024, Conv((p53 + 1) * 5, 5, 10));
            // a tie plus a sticky bit goes up
            Assert.Equal(9007199254740994.0, Conv(((p53 + 1) << 100) + 1, one << 100, 0));
            // and a tie minus a sticky bit goes down
            Assert.Equal(9007199254740992.0, Conv(((p53 + 1) << 100) - 1, one << 100, 0));
        }

        [Fact]
        public void SubnormalBoundary_IsRoundedOnce()
        {
            BigInteger one = BigInteger.One;
            // the smallest subnormal 2^-1074 and its half: 2^-1075 is a tie to the even 0; just above it rounds up, just below to 0
            Assert.Equal(double.Epsilon, Conv(one, 1, -1074));
            Assert.Equal(0.0, Conv(one, 1, -1075));
            Assert.Equal(double.Epsilon, Conv((one << 10) + 1, one << 10, -1075));
            Assert.Equal(0.0, Conv((one << 10) - 1, one << 10, -1075));
            Assert.Equal(-0.0, Conv(-one, 1, -1075));
            Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(Conv(-one, 1, -1075)));
            // 1.5 * 2^-1074 ties to even: 2 * 2^-1074
            Assert.Equal(2 * double.Epsilon, Conv(3, 2, -1074));
            // the boundary between the largest subnormal and the smallest normal: 2^-1022 - 2^-1075 is a tie between them
            BigInteger tie = (one << 53) - 1;      // (2^53 - 1) * 2^-1075 = 2^-1022 - 2^-1075
            Assert.Equal(Math.Pow(2, -1022), Conv(tie, 1, -1075));       // the even one is the normal 2^-1022
            Assert.Equal(Math.Pow(2, -1022) - double.Epsilon, Conv(tie - 1, 1, -1075));
            // a value within the subnormals rounds at the grid, not first to 53 bits: (2^60 + 2^6 + 1) 2^-1130 is above the middle of two units
            BigInteger v = (one << 60) + (one << 6) + 1;
            AssertCorrectlyRounded(v, 1, -1130, Conv(v, 1, -1130), "double rounding");
            for (int k = 0; k < 200; k++)
                AssertCorrectlyRounded((one << 53) + k, 1, -1074 - 53 - 1 + (k % 5), Conv((one << 53) + k, 1, -1074 - 53 - 1 + (k % 5)), "subnormal " + k);
        }

        [Fact]
        public void OverflowEnd_AndUnderflowEnd()
        {
            BigInteger one = BigInteger.One;
            double max = double.MaxValue;
            Assert.Equal(max, Conv(MaxDouble, 1, 0));
            // the tie between the largest double and 2^1024 goes to the even 2^1024: an infinity; one below it is the largest double
            Assert.Equal(double.PositiveInfinity, Conv(OverflowTie, 1, 0));
            Assert.Equal(max, Conv(OverflowTie - 1, 1, 0));
            Assert.Equal(double.NegativeInfinity, Conv(-OverflowTie, 1, 0));
            Assert.Equal(max, Conv(OverflowTie * 3 - 3, 3, 0));
            Assert.Equal(double.PositiveInfinity, Conv(1, 1, 5000));
            Assert.Equal(0.0, Conv(1, 1, -5000));
            Assert.Equal(0.0, Conv(1, 1, -1000000000));
            Assert.Equal(double.PositiveInfinity, Conv(1, 1, 1000000000));
            // an exponent near the int limits must not wrap around (num much larger than den makes the shift negative)
            Assert.Equal(double.PositiveInfinity, Conv(one << 100, 1, int.MaxValue));
            Assert.Equal(double.PositiveInfinity, Conv(one << 100, 1, int.MaxValue - 10));
            Assert.Equal(double.NegativeInfinity, Conv(-(one << 100), 1, int.MaxValue - 1));
            Assert.Equal(double.PositiveInfinity, Conv(one << 5000, 1, int.MaxValue - 4000));
            Assert.Equal(0.0, Conv(one << 100, 1, int.MinValue + 60));
            Assert.Equal(0.0, Conv(1, one << 5000, int.MinValue));
            Assert.Equal(double.PositiveInfinity, Conv(1, 1, int.MaxValue));
            Assert.Equal(Math.Pow(2, -100), Conv(one << 5000, one << 3000, -2100));      // 2^2000 * 2^-2100
        }

        [Fact]
        public void PowerOfTwoScales_AreExact_AndBitForBit()
        {
            var r = new Random(77);
            for (int i = 0; i < 500; i++) {
                BigInteger num = BigInteger.Abs(Random(r, r.Next(1, 200))) + 1, den = BigInteger.Abs(Random(r, r.Next(1, 200))) + 1;
                double basis = Conv(num, den, 0);
                foreach (int k in new[] { -300, -37, 5, 100, 400 }) {
                    double scaled = Conv(num, den, k);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(basis * Math.Pow(2, k)), BitConverter.DoubleToInt64Bits(scaled));
                }
            }
        }

        [Fact]
        public void ToDouble_IsCorrectlyRounded_RandomIncludingNegativeDenominators()
        {
            var r = new Random(1411);
            for (int i = 0; i < 3000; i++) {
                BigInteger num = BigInteger.Abs(Random(r, r.Next(1, 200))) + 1, den = BigInteger.Abs(Random(r, r.Next(1, 200))) + 1;
                if (r.Next(2) == 0) num = -num;
                int sc = r.Next(-300, 300) + (r.Next(5) == 0 ? r.Next(-1100, 1100) : 0);
                double got = ExactPoint3.ToDouble(num, den, sc);
                AssertCorrectlyRounded(num, den, sc, got, $"case {i}");
                // a negative denominator: the same value (num / den = (-num) / (-den))
                Assert.Equal(BitConverter.DoubleToInt64Bits(got), BitConverter.DoubleToInt64Bits(ExactPoint3.ToDouble(-num, -den, sc)));
            }
            Assert.Equal(-1.0 / 3.0, ExactPoint3.ToDouble(1, -3, 0));
            Assert.Equal(1.0 / 3.0, ExactPoint3.ToDouble(-1, -3, 0));
            Assert.Equal(0.0, ExactPoint3.ToDouble(0, -3, 7));
            Assert.Throws<DivideByZeroException>(() => ExactPoint3.ToDouble(1, 0, 0));
        }

        // a value that is exactly a double (any magnitude, subnormals included) comes back unchanged, however num / den and the exponent split it
        [Fact]
        public void ExactDoubles_AreReturnedUnchanged()
        {
            var r = new Random(1412);
            for (int i = 0; i < 3000; i++) {
                long bits;
                switch (r.Next(3)) {
                    case 0: bits = ((long)r.Next(0, 0x7FF) << 52) | (Next62(r) & 0xFFFFFFFFFFFFFL); break;     // any finite
                    case 1: bits = Next62(r) & 0xFFFFFFFFFFFFFL; break;                                         // subnormal or zero
                    default: bits = ((long)r.Next(900, 1150) << 52) | (Next62(r) & 0xFFFFFFFFFFFFFL); break;
                }
                if (r.Next(2) == 0) bits |= long.MinValue;
                double x = BitConverter.Int64BitsToDouble(bits);
                var (n, d) = FromDouble(x);
                BigInteger k = BigInteger.Abs(Random(r, r.Next(1, 120))) + 1;
                int sc = r.Next(-200, 200);
                // x = (n k) / (d k): the exponent sc is moved into the fraction, x = (num / den) 2^sc
                BigInteger num = n * k, den = d * k;
                if (sc >= 0) den <<= sc; else num <<= -sc;
                double got = Conv(num, den, sc);
                if (x == 0) Assert.Equal(0.0, got);
                else Assert.Equal(bits, BitConverter.DoubleToInt64Bits(got));
            }
        }

        // the halfway values between neighbouring doubles (normal and subnormal), and values an epsilon above and below: the ones a missing
        // sticky bit or a missing tie rule gets wrong
        [Fact]
        public void HalfwayAndNeighbours_OfRandomDoubles_AreRoundedByTheRules()
        {
            var r = new Random(1413);
            for (int i = 0; i < 1500; i++) {
                int e = r.Next(2) == 0 ? -1074 : r.Next(-1074, 960);                          // the unit of the lower double is 2^e (e = -1074: a subnormal)
                long m = e == -1074 ? (Next62(r) & 0x1FFFFFFFFFFFFFL) : ((Next62(r) & 0xFFFFFFFFFFFFFL) | (1L << 52));
                if (e > -1074 && r.Next(5) == 0) m = (1L << 52) | (r.Next(2) == 0 ? 0 : 0xFFFFFFFFFFFFFL);   // binade boundaries
                // a halfway value is (2 m + 1) 2^(e - 1); as num / den 2^sc with den = k 2^p and num = (2 m + 1) k 2^p +- delta, a sticky bit far below
                BigInteger k = BigInteger.Abs(Random(r, r.Next(1, 100))) + 1;
                int p = r.Next(1, 150);
                BigInteger den = k << p, tie = (new BigInteger(2 * m + 1) * k) << p;
                int sc = e - 1;
                double lo = ExactPoint3.ScaleB((double)m, e), hi = ExactPoint3.ScaleB((double)(m + 1), e);
                foreach (int delta in new[] { -1, 0, 1 }) {
                    double got = Conv(tie + delta, den, sc);
                    AssertCorrectlyRounded(tie + delta, den, sc, got, $"case {i} delta {delta}");
                    double want = delta < 0 ? lo : delta > 0 ? hi : (m % 2 == 0 ? lo : hi);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(want), BitConverter.DoubleToInt64Bits(got));
                    // the same with a negative value
                    Assert.Equal(BitConverter.DoubleToInt64Bits(-want), BitConverter.DoubleToInt64Bits(Conv(-(tie + delta), den, sc)));
                }
            }
        }

        // the reproducer of #1410: the crossing of a segment with a triangle plane, exact on integer coordinates (every double is a multiple of
        // 2^-1074), converted per coordinate: z was 0.538797999888854, 1 ulp below the nearest double 0.5387979998888541
        [Fact]
        public void Issue1410_SegmentTriangleCrossing_IsTheNearestDouble()
        {
            double[] s = { 0.9796912223006092, 0.8392714871276503, 0.5432778380547081 }, t = { 0.1979652751227679, 0.038267011306372944, 0.5355382620056803 };
            double[][] tri = {
                new[] { 0.33469497591010056, 0.5815629843070931, 0.690371502279477 },
                new[] { 0.6716304929794885, -0.046980683946507396, 0.2060040019946191 },
                new[] { -0.10453685866880086, -0.03696404049962948, 0.1468926098416059 } };
            BigInteger[] G(double[] v)
            {
                var g = new BigInteger[3];
                for (int i = 0; i < 3; i++) { var (n, d) = FromDouble(v[i]); g[i] = n * (BigInteger.One << 1074) / d; }
                return g;
            }
            BigInteger[] P = G(s), Q = G(t), A = G(tri[0]), B = G(tri[1]), C = G(tri[2]);
            BigInteger[] Sub(BigInteger[] a, BigInteger[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
            BigInteger[] Cross(BigInteger[] a, BigInteger[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
            BigInteger Dot(BigInteger[] a, BigInteger[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
            BigInteger[] nrm = Cross(Sub(B, A), Sub(C, A));
            BigInteger dp = Dot(nrm, Sub(P, A)), dq = Dot(nrm, Sub(Q, A));
            Assert.True(dp.Sign * dq.Sign < 0, "the segment crosses the plane");
            BigInteger den = dp - dq;
            double[] got = new double[3];
            for (int k = 0; k < 3; k++) {
                BigInteger num = dp * Q[k] - dq * P[k];
                got[k] = ExactPoint3.ToDouble(num, den, -1074);
                AssertCorrectlyRounded(num, den, -1074, got[k], "coordinate " + k);
            }
            Assert.Equal(0.5387979998888541, got[2]);
        }

    }
}
