using System;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The conversions of ExactPoint3 to and from doubles, checked exactly in integer arithmetic over the whole double
    /// range (subnormal results, overflow, negative values, quotients of very different sizes). Mutation testing
    /// (tests/mutation) showed that the earlier tests reached ToDouble, ScaleB and ApproxVector3d only with values
    /// near 1.
    /// </summary>
    public class ExactPoint3ConversionTests
    {
        // |r - num / den * 2^s| <= 1 ulp of r (and r has the sign of num), checked exactly
        static void AssertWithinOneUlp(double r, BigInteger num, BigInteger den, int s, string what)
        {
            Assert.False(double.IsNaN(r), what);
            if (double.IsInfinity(r)) {
                // only beyond the double range: |num / den * 2^s| >= 2^1024 - 2^970 (the largest double plus half an
                // ulp, (2^53 - 1) 2^971 + 2^970 = (2^54 - 1) 2^970)
                Assert.Equal(num.Sign, Math.Sign(r));
                Assert.True(Compare(BigInteger.Abs(num), den, s, (BigInteger.One << 54) - 1, 970) >= 0, what + ": infinity below the double range");
                return;
            }
            Assert.True(r == 0 || Math.Sign(r) == num.Sign, what + ": sign");
            BigInteger m; int e;
            ExactPoint3.Decompose(r, out m, out e);
            // ulp of r: 2^(exponent - 52), at least 2^-1074
            int biased = (int)((BitConverter.DoubleToInt64Bits(r) >> 52) & 0x7FF);
            int ulpExp = (biased == 0) ? -1074 : biased - 1023 - 52;
            // |m 2^e den - num 2^s| <= 2^ulpExp den, all at the common exponent k
            int k = Math.Min(Math.Min(e, s), ulpExp);
            BigInteger lhs = BigInteger.Abs((m << (e - k)) * den - (num << (s - k)));
            BigInteger rhs = den << (ulpExp - k);
            Assert.True(lhs <= rhs, what + $": {r:R} is more than 1 ulp from the exact value");
        }

        // sign of |num| / den * 2^s - a * 2^b
        static int Compare(BigInteger num, BigInteger den, int s, BigInteger a, int b)
        {
            int k = Math.Min(s, b);
            return BigInteger.Compare(num << (s - k), (a * den) << (b - k));
        }

        static BigInteger RandomBig(Random rnd, int bits)
        {
            byte[] bytes = new byte[bits / 8 + 2];
            rnd.NextBytes(bytes);
            bytes[bytes.Length - 1] = 0;        // positive
            BigInteger v = new BigInteger(bytes) >> (8 * bytes.Length - 8 - bits);
            return v.IsZero ? BigInteger.One : v;
        }

        [Fact]
        public void ToDouble_WithinOneUlp_OverTheWholeRange()
        {
            var rnd = new Random(5);
            for (int i = 0; i < 20000; ++i) {
                BigInteger num = RandomBig(rnd, 1 + rnd.Next(400));
                BigInteger den = RandomBig(rnd, 1 + rnd.Next(400));
                if (rnd.Next(2) == 0) num = -num;
                // scales that put the result anywhere from below the subnormal range to beyond the double range
                int s = rnd.Next(-1500, 1500);
                double r = ExactPoint3.ToDouble(num, den, s);
                AssertWithinOneUlp(r, num, den, s, $"case {i}: {num} / {den} * 2^{s}");
            }
        }

        [Fact]
        public void ToDouble_SmallAndExactQuotients()
        {
            Assert.Equal(0.0, ExactPoint3.ToDouble(BigInteger.Zero, 7, 100));
            Assert.Equal(0.375, ExactPoint3.ToDouble(3, 8, 0));
            Assert.Equal(-0.375, ExactPoint3.ToDouble(-3, 8, 0));
            Assert.Equal(1.5 * Math.Pow(2, 900), ExactPoint3.ToDouble(3, 2, 900));
            Assert.Equal(double.Epsilon, ExactPoint3.ToDouble(BigInteger.One, BigInteger.One, -1074));
            Assert.Equal(-double.Epsilon, ExactPoint3.ToDouble(BigInteger.MinusOne, BigInteger.One, -1074));
            // the numerator much longer than the denominator (negative shift)
            BigInteger big = BigInteger.Pow(3, 300);
            AssertWithinOneUlp(ExactPoint3.ToDouble(big, 7, -400), big, 7, -400, "3^300 / 7");
        }

        [Fact]
        public void ScaleB_MatchesExactScaling()
        {
            var rnd = new Random(8);
            for (int i = 0; i < 20000; ++i) {
                // normal and subnormal inputs, both signs
                double x = (rnd.NextDouble() + 0.5) * Math.Pow(2, rnd.Next(-1074, 1000));
                if (rnd.Next(2) == 0) x = -x;
                int n = rnd.Next(-2200, 2200);
                double r = ExactPoint3.ScaleB(x, n);
                BigInteger m; int e;
                ExactPoint3.Decompose(x, out m, out e);
                AssertWithinOneUlp(r, m, BigInteger.One, e + n, $"case {i}: {x:R} * 2^{n}");
#if NETCOREAPP
                Assert.Equal(Math.ScaleB(x, n), r);       // correctly rounded, like the runtime
#endif
            }
        }

        [Fact]
        public void ScaleB_SpecialValuesAndSignOfZero()
        {
            Assert.Equal(0.0, ExactPoint3.ScaleB(0.0, 50));
            Assert.True(double.IsNaN(ExactPoint3.ScaleB(double.NaN, 3)));
            Assert.Equal(double.PositiveInfinity, ExactPoint3.ScaleB(double.PositiveInfinity, -3));
            Assert.Equal(double.PositiveInfinity, ExactPoint3.ScaleB(1.5, 1024));
            Assert.Equal(double.NegativeInfinity, ExactPoint3.ScaleB(-1.5, 1024));
            // below half the smallest subnormal: zero with the sign of x
            double negZero = ExactPoint3.ScaleB(-1.0, -1100);
            Assert.Equal(0.0, negZero);
            Assert.True(BitConverter.DoubleToInt64Bits(negZero) < 0, "-0.0 expected");
            Assert.True(BitConverter.DoubleToInt64Bits(ExactPoint3.ScaleB(1.0, -1100)) == 0, "+0.0 expected");
            // a subnormal input scaled up, and down again
            Assert.Equal(1.0, ExactPoint3.ScaleB(double.Epsilon, 1074));
            Assert.Equal(double.Epsilon, ExactPoint3.ScaleB(1.0, -1074));
            Assert.Equal(-4 * double.Epsilon, ExactPoint3.ScaleB(-double.Epsilon, 2));
        }

        [Fact]
        public void BitLength_MatchesTheDefinition()
        {
            var rnd = new Random(9);
            Assert.Equal(0, ExactPoint3.BitLength(BigInteger.Zero));
            Assert.Equal(1, ExactPoint3.BitLength(BigInteger.One));
            Assert.Equal(1, ExactPoint3.BitLength(BigInteger.MinusOne));
            for (int bits = 1; bits < 300; ++bits) {
                BigInteger lo = BigInteger.One << (bits - 1), hi = (BigInteger.One << bits) - 1;
                Assert.Equal(bits, ExactPoint3.BitLength(lo));
                Assert.Equal(bits, ExactPoint3.BitLength(hi));
                Assert.Equal(bits, ExactPoint3.BitLength(-hi));
                Assert.Equal(bits, ExactPoint3.BitLength(RandomBig(rnd, bits) | lo));
            }
        }

        [Fact]
        public void Decompose_IsExactWithAnOddMantissa()
        {
            var rnd = new Random(10);
            for (int i = 0; i < 5000; ++i) {
                double v = (rnd.NextDouble() - 0.5) * Math.Pow(2, rnd.Next(-1074, 1020));
                BigInteger m; int e;
                ExactPoint3.Decompose(v, out m, out e);
                if (v == 0) { Assert.True(m.IsZero && e == 0); continue; }
                Assert.False(m.IsEven, $"{v:R}: mantissa {m} is even");
                Assert.Equal(Math.Sign(v), m.Sign);
                Assert.Equal(v, ExactPoint3.ScaleB((double)m, e));       // m has at most 53 bits: exact
            }
            BigInteger m0; int e0;
            ExactPoint3.Decompose(-0.0, out m0, out e0);
            Assert.True(m0.IsZero && e0 == 0);
            Assert.Throws<ArgumentException>(() => ExactPoint3.Decompose(double.NaN, out m0, out e0));
            Assert.Throws<ArgumentException>(() => ExactPoint3.Decompose(double.NegativeInfinity, out m0, out e0));
        }

        [Fact]
        public void Create_NormalizesTheSignOfW_AndRejectsZero()
        {
            ExactPoint3 p = ExactPoint3.Create(2, -4, 6, -8);
            Assert.Equal(new BigInteger(-1), p.X);
            Assert.Equal(new BigInteger(2), p.Y);
            Assert.Equal(new BigInteger(-3), p.Z);
            Assert.Equal(new BigInteger(4), p.W);
            ExactPoint3 q = ExactPoint3.CreateUnreduced(2, -4, 6, -8);
            Assert.Equal(new BigInteger(-2), q.X);
            Assert.Equal(new BigInteger(8), q.W);
            Assert.Throws<ArgumentException>(() => ExactPoint3.Create(1, 2, 3, BigInteger.Zero));
            Assert.Throws<ArgumentException>(() => ExactPoint3.CreateUnreduced(1, 2, 3, BigInteger.Zero));
            Assert.Equal(ExactPoint3.Create(2, -4, 6, -8), ExactPoint3.Create(-1, 2, -3, 4));
            Assert.Equal(ExactPoint3.Create(2, -4, 6, -8).GetHashCode(), ExactPoint3.Create(-1, 2, -3, 4).GetHashCode());
            Assert.False(ExactPoint3.Create(1, 2, 3, 1).Equals((object)"(1, 2, 3)"));
        }

        [Fact]
        public void ApproxVector3d_BeyondTheDoubleRange_FallsBackToTheExactConversion()
        {
            // W (and X) beyond the double range: the double quotient is inf / inf or x / inf; the coordinates are not
            BigInteger w = BigInteger.One << 1100;
            ExactPoint3 p = ExactPoint3.Create(BigInteger.One << 1090, 3 * (BigInteger.One << 1095), BigInteger.One, w);
            Vector3d v = p.ApproxVector3d();
            Assert.Equal(Math.Pow(2, -10), v.x);
            Assert.Equal(3 * Math.Pow(2, -5), v.y);
            Assert.Equal(0.0, v.z);         // 2^-1100: below the subnormal range
            // quotients in the subnormal range, with W beyond the double range as well: the fallback is taken (the
            // plain division would give 0 here)
            ExactPoint3 s = ExactPoint3.Create(3, 5, 7, BigInteger.One << 1060);
            Vector3d a = s.ApproxVector3d(), e = s.ToVector3d();
            Assert.Equal(e, a);
            // in the normal range both agree within 4 ulps
            ExactPoint3 n = ExactPoint3.Create(1, 2, 3, 7);
            Assert.True(Math.Abs(n.ApproxVector3d().x - 1.0 / 7) <= 4 * Math.Pow(2, -52) / 7);
        }
    }
}
