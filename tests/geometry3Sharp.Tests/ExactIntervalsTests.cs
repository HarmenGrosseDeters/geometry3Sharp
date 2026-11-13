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

        // q encloses a / b (b != 0): checked exactly as lo * b <= a <= hi * b for b > 0 (reversed for b < 0)
        static bool EnclosesQuotient(ExactInterval q, double a, double b)
        {
            ExactDyadic da = ExactDyadic.Of(a), db = ExactDyadic.Of(b);
            int sb = b > 0 ? 1 : -1;
            bool lo = double.IsNegativeInfinity(q.Lo) || sb * ExactDyadic.Compare(ExactDyadic.Of(q.Lo) * db, da) <= 0;
            bool hi = double.IsPositiveInfinity(q.Hi) || sb * ExactDyadic.Compare(ExactDyadic.Of(q.Hi) * db, da) >= 0;
            return lo && hi;
        }

        // r encloses sqrt(a) (a >= 0): lo >= 0 and lo^2 <= a <= hi^2, exactly
        static bool EnclosesRoot(ExactInterval r, double a)
        {
            ExactDyadic da = ExactDyadic.Of(a), lo = ExactDyadic.Of(r.Lo);
            bool hi = double.IsPositiveInfinity(r.Hi) || ExactDyadic.Compare(ExactDyadic.Of(r.Hi) * ExactDyadic.Of(r.Hi), da) >= 0;
            return r.Lo >= 0 && ExactDyadic.Compare(lo * lo, da) <= 0 && hi;
        }

        [Fact]
        public void Interval_Division_AndSqrt_EncloseTheExactResult()
        {
            var rnd = new Random(2);
            for (int i = 0; i < 20000; ++i) {
                double a = Rand(rnd), b = Rand(rnd);
                if (b == 0) continue;
                ExactInterval q = ExactInterval.Of(a) / ExactInterval.Of(b);
                Assert.True(EnclosesQuotient(q, a, b), $"{a:R} / {b:R} -> [{q.Lo:R}, {q.Hi:R}]");
                double m = Math.Abs(a);
                ExactInterval r = ExactInterval.Sqrt(ExactInterval.Of(m));
                Assert.True(EnclosesRoot(r, m), $"sqrt {m:R} -> [{r.Lo:R}, {r.Hi:R}]");
            }
        }

        [Theory]
        [InlineData(double.Epsilon, 3.0)]          // subnormal quotient
        [InlineData(double.Epsilon, 0.5)]          // twice the smallest subnormal
        [InlineData(2.2250738585072014E-308, 7.0)] // from the smallest normal into the subnormals
        [InlineData(1e308, 1e-308)]                // overflows to infinity
        [InlineData(double.MaxValue, 0.5)]
        [InlineData(-double.MaxValue, 0.25)]
        [InlineData(1.0, double.MaxValue)]         // underflows to a subnormal
        [InlineData(1e-300, 1e300)]                // underflows to 0
        public void Interval_Division_AtSubnormalAndOverflowEdges(double a, double b)
        {
            ExactInterval q = ExactInterval.Of(a) / ExactInterval.Of(b);
            Assert.True(EnclosesQuotient(q, a, b), $"[{q.Lo:R}, {q.Hi:R}]");
            Assert.True(q.Lo <= q.Hi);
        }

        [Theory]
        [InlineData(double.Epsilon)]
        [InlineData(3 * double.Epsilon)]
        [InlineData(2.2250738585072014E-308)]
        [InlineData(double.MaxValue)]
        [InlineData(2.0)]
        [InlineData(0.0)]
        public void Interval_Sqrt_AtSubnormalAndOverflowEdges(double a)
        {
            ExactInterval r = ExactInterval.Sqrt(ExactInterval.Of(a));
            Assert.True(EnclosesRoot(r, a), $"[{r.Lo:R}, {r.Hi:R}]");
            Assert.True(r.Hi - r.Lo <= 4 * (ExactInterval.Up(r.Hi) - r.Hi) + double.Epsilon, "tight: at most a few spacings");
        }

        [Fact]
        public void Interval_Division_ByAnIntervalContainingZero_IsTheWholeLine_AndSqrtOfNegativeToo()
        {
            ExactInterval q = ExactInterval.Of(1) / new ExactInterval(-1, 1);
            Assert.True(double.IsNegativeInfinity(q.Lo) && double.IsPositiveInfinity(q.Hi));
            ExactInterval q0 = ExactInterval.Of(1) / ExactInterval.Of(0);
            Assert.True(double.IsNegativeInfinity(q0.Lo) && double.IsPositiveInfinity(q0.Hi));
            ExactInterval r = ExactInterval.Sqrt(new ExactInterval(-2, -1));
            Assert.True(double.IsNegativeInfinity(r.Lo) && double.IsPositiveInfinity(r.Hi));
            // a partly negative argument: the root of its non-negative part
            ExactInterval r2 = ExactInterval.Sqrt(new ExactInterval(-1, 4));
            Assert.Equal(0, r2.Lo);
            Assert.True(r2.Hi >= 2 && r2.Hi <= ExactInterval.Up(2));
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

        // tight, not only enclosing: on exact inputs each operation is at most one ulp wider on each side than the
        // rounded result (wider intervals would still enclose, but decide fewer signs)
        [Fact]
        public void Interval_IsTight()
        {
            var rnd = new Random(4);
            for (int i = 0; i < 20000; ++i) {
                double a = (rnd.NextDouble() - 0.5) * Math.Exp(rnd.NextDouble() * 40 - 20), b = (rnd.NextDouble() - 0.5) * Math.Exp(rnd.NextDouble() * 40 - 20);
                ExactInterval ia = ExactInterval.Of(a), ib = ExactInterval.Of(b);
                Tight(ia * ib, a * b);
                Tight(ia + ib, a + b);
                Tight(ia - ib, a - b);
                Tight(ExactInterval.Square(ia), a * a);
                // a product of intervals with both signs: [-|a|, |a|] x [b, b]
                ExactInterval sym = new ExactInterval(-Math.Abs(a), Math.Abs(a));
                ExactInterval p = sym * ib;
                Assert.True(p.Lo <= -Math.Abs(a * b) && p.Hi >= Math.Abs(a * b));
                Assert.True(p.Hi - p.Lo <= 2 * Math.Abs(a * b) * (1 + 1e-15) + 4 * double.Epsilon);
                // squares of intervals that straddle 0 start at 0
                ExactInterval sq = ExactInterval.Square(new ExactInterval(-Math.Abs(a), 0.5 * Math.Abs(a)));
                Assert.Equal(0, sq.Lo);
                Assert.True(sq.Hi >= a * a && sq.Hi <= ExactInterval.Up(ExactInterval.Up(a * a)));
                ExactInterval sqn = ExactInterval.Square(new ExactInterval(-Math.Abs(a), -0.5 * Math.Abs(a)));
                Assert.True(sqn.Lo <= 0.25 * a * a && sqn.Lo >= ExactInterval.Down(ExactInterval.Down(0.25 * a * a)));
            }
        }

        static void Tight(ExactInterval iv, double rounded)
        {
            Assert.True(iv.Lo >= ExactInterval.Down(rounded) && iv.Hi <= ExactInterval.Up(rounded), $"{iv.Lo} {iv.Hi} {rounded}");
        }

        [Fact]
        public void Sign_DecidesOnlyStrictIntervals()
        {
            Assert.Equal(2, new ExactInterval(0, 1).Sign());
            Assert.Equal(2, new ExactInterval(-1, 0).Sign());
            Assert.Equal(1, new ExactInterval(double.Epsilon, 1).Sign());
            Assert.Equal(-1, new ExactInterval(-1, -double.Epsilon).Sign());
            Assert.Equal(0, ExactInterval.Of(0).Sign());
            // NaN in a product (0 x infinity) or a sum (infinity - infinity) gives the whole line
            Assert.Equal(2, (ExactInterval.Of(0) * ExactInterval.Of(double.PositiveInfinity)).Sign());
            Assert.Equal(2, (ExactInterval.Of(double.PositiveInfinity) - ExactInterval.Of(double.PositiveInfinity)).Sign());
            Assert.Equal(2, (new ExactInterval(1, 2) * new ExactInterval(0, double.PositiveInfinity) * ExactInterval.Of(0)).Sign());
            ExactInterval neg = -new ExactInterval(1, 2);
            Assert.Equal(-2, neg.Lo); Assert.Equal(-1, neg.Hi);
        }

        [Fact]
        public void Quotient_SignsOfOverflowAndUnderflow()
        {
            Assert.Equal(double.NegativeInfinity, ExactDyadic.Quotient(ExactDyadic.Of(-double.MaxValue), ExactDyadic.Of(0.5)));
            Assert.Equal(double.NegativeInfinity, ExactDyadic.Quotient(ExactDyadic.Of(double.MaxValue), ExactDyadic.Of(-0.5)));
            double nz = ExactDyadic.Quotient(ExactDyadic.Of(-double.Epsilon), ExactDyadic.Of(1e300));
            Assert.Equal(0.0, nz);
            Assert.True(BitConverter.DoubleToInt64Bits(nz) < 0);       // -0.0
            double pz = ExactDyadic.Quotient(ExactDyadic.Of(double.Epsilon), ExactDyadic.Of(1e300));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(pz));      // +0.0
            Assert.Equal(-0.5, ExactDyadic.Quotient(ExactDyadic.Of(1), ExactDyadic.Of(-2)));
            Assert.Equal(0.25, ExactDyadic.Quotient(ExactDyadic.Of(-1), ExactDyadic.Of(-4)));
            Assert.Throws<DivideByZeroException>(() => ExactDyadic.Quotient(ExactDyadic.Of(1), ExactDyadic.Zero));
        }
    }
}
