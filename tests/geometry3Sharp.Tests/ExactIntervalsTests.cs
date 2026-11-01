using System;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The filter and exact arithmetic of the terrain slope code: ExactInterval encloses the exact result of every
    /// operation, ExactDyadic is exact, and ExactDyadic.Quotient rounds to nearest (ties to even), also subnormal.
    /// </summary>
    public class ExactIntervalsTests
    {
        static readonly double[] specials = { 0, double.Epsilon, 3 * double.Epsilon, 2.2250738585072014E-308, 1e-300, 1e-170,
            0.1, 1, 3, 1e17, 1e160, 1e300, double.MaxValue };

        static double Rand(Random rnd)
        {
            double x;
            switch (rnd.Next(4)) {
                case 0: x = specials[rnd.Next(specials.Length)]; break;
                case 1: x = rnd.NextDouble(); break;
                case 2: x = Math.Exp(rnd.NextDouble() * 1400 - 700); break;
                default: x = BitConverter.Int64BitsToDouble((long)(rnd.NextDouble() * 0x7FEFFFFFFFFFFFFFL)); break;
            }
            return rnd.Next(2) == 0 ? -x : x;
        }

        // the exact value of a dyadic compared with a double (both exact)
        static int Cmp(ExactDyadic a, double x) => ExactDyadic.Compare(a, ExactDyadic.Of(x));

        static bool Encloses(ExactInterval iv, ExactDyadic exact)
        {
            bool lo = double.IsNegativeInfinity(iv.Lo) || Cmp(exact, iv.Lo) >= 0;
            bool hi = double.IsPositiveInfinity(iv.Hi) || Cmp(exact, iv.Hi) <= 0;
            return lo && hi;
        }

        [Fact]
        public void Interval_EnclosesTheExactResult()
        {
            var rnd = new Random(1);
            for (int i = 0; i < 20000; ++i) {
                double a = Rand(rnd), b = Rand(rnd), c = Rand(rnd);
                ExactInterval ia = ExactInterval.Of(a), ib = ExactInterval.Of(b), ic = ExactInterval.Of(c);
                ExactDyadic da = ExactDyadic.Of(a), db = ExactDyadic.Of(b), dc = ExactDyadic.Of(c);
                Assert.True(Encloses(ia + ib, da + db));
                Assert.True(Encloses(ia - ib, da - db));
                Assert.True(Encloses(ia * ib, da * db));
                Assert.True(Encloses((ia * ib) - ic, da * db - dc));
                Assert.True(Encloses(ExactInterval.Square(ia - ib) + ic * ic, (da - db) * (da - db) + dc * dc));
                // a decided sign is the exact sign
                int s = ((ia * ib) - (ic * ia)).Sign();
                if (s != 2) Assert.Equal((da * db - dc * da).Sign, s);
            }
        }

        [Fact]
        public void Dyadic_RoundTripsDoubles()
        {
            var rnd = new Random(2);
            for (int i = 0; i < 5000; ++i) {
                double a = Rand(rnd);
                Assert.Equal(a, ExactDyadic.Quotient(ExactDyadic.Of(a), ExactDyadic.Of(1)));
                Assert.Equal(0, Cmp(ExactDyadic.Of(a), a));
            }
        }

        // reference: the double nearest to n / d (ties to even) by comparing with the neighbours of a candidate
        [Fact]
        public void Quotient_IsCorrectlyRounded()
        {
            var rnd = new Random(3);
            for (int i = 0; i < 20000; ++i) {
                double a = Rand(rnd), b = Rand(rnd);
                if (b == 0) continue;
                ExactDyadic n = ExactDyadic.Of(a), d = ExactDyadic.Of(b);
                if (i % 3 == 0) { n = n * ExactDyadic.Of(Rand(rnd)) + ExactDyadic.Of(Rand(rnd)); }
                double q = ExactDyadic.Quotient(n, d);
                if (double.IsInfinity(q)) continue;
                // |n/d - q| <= |n/d - q'| for the neighbours q' of q: compare (n - q d) against (n - q' d) in exact arithmetic
                ExactDyadic err = Abs(n - ExactDyadic.Of(q) * d);
                foreach (double nb in new[] { ExactInterval.Up(q), ExactInterval.Down(q) }) {
                    if (double.IsInfinity(nb)) continue;
                    ExactDyadic e2 = Abs(n - ExactDyadic.Of(nb) * d);
                    int c = ExactDyadic.Compare(err, e2);
                    Assert.True(c <= 0, $"{a} {b}: {q} vs {nb}");
                    if (c == 0) Assert.Equal(0L, BitConverter.DoubleToInt64Bits(q) & 1);     // tie: even
                }
            }
        }

        [Fact]
        public void Quotient_Ties_AndSubnormals()
        {
            // 1 + 2^-53 is half way between 1 and the next double: ties to even gives 1
            ExactDyadic half = new ExactDyadic((BigInteger.One << 53) + 1, -53);
            Assert.Equal(1.0, ExactDyadic.Quotient(half, ExactDyadic.Of(1)));
            ExactDyadic above = new ExactDyadic((BigInteger.One << 54) + 3, -54);     // 1 + 0.75 * 2^-52: nearest is 1 + 2^-52
            Assert.Equal(1.0 + Math.Pow(2, -52), ExactDyadic.Quotient(above, ExactDyadic.Of(1)));
            Assert.Equal(double.Epsilon, ExactDyadic.Quotient(ExactDyadic.Of(double.Epsilon), ExactDyadic.Of(1)));
            Assert.Equal(0.0, ExactDyadic.Quotient(ExactDyadic.Of(double.Epsilon), ExactDyadic.Of(4)));      // below half
            Assert.Equal(double.Epsilon, ExactDyadic.Quotient(ExactDyadic.Of(3 * double.Epsilon), ExactDyadic.Of(4)));
            Assert.Equal(2 * double.Epsilon, ExactDyadic.Quotient(ExactDyadic.Of(3 * double.Epsilon), ExactDyadic.Of(2)));  // tie to even
            Assert.Equal(-1.0 / 3, ExactDyadic.Quotient(ExactDyadic.Of(-1), ExactDyadic.Of(3)));
            Assert.Equal(double.PositiveInfinity, ExactDyadic.Quotient(ExactDyadic.Of(double.MaxValue), ExactDyadic.Of(0.5)));
        }

        static ExactDyadic Abs(ExactDyadic x) => x.Sign < 0 ? -x : x;
    }
}
