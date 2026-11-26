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

        [Fact]
        public void Interval_Division_AndSqrt_AreTight()
        {
            // an exact quotient or root gives the one-spacing widening, not the whole line
            ExactInterval q = new ExactInterval(1, 2) / new ExactInterval(4, 8);
            Assert.Equal(0.125, q.Lo, 1e-15); Assert.Equal(0.5, q.Hi, 1e-15);
            Assert.True(q.Lo < 0.125 && q.Hi > 0.5);
            ExactInterval n = new ExactInterval(1, 2) / new ExactInterval(-8, -4);
            Assert.Equal(-0.5, n.Lo, 1e-15); Assert.Equal(-0.125, n.Hi, 1e-15);
            ExactInterval r = ExactInterval.Sqrt(new ExactInterval(4, 9));
            Assert.Equal(2, r.Lo, 1e-15); Assert.Equal(3, r.Hi, 1e-15);
            var rnd = new Random(3);
            for (int i = 0; i < 2000; ++i) {
                double a = Rand(rnd), b = Rand(rnd);
                if (b == 0 || !(Math.Abs(a / b) < 1e300) || a / b == 0) continue;   // below MaxValue, where the widening reaches infinity
                ExactInterval qi = ExactInterval.Of(a) / ExactInterval.Of(b);
                double m = Math.Abs(a / b);
                Assert.True(qi.Hi - qi.Lo <= 8 * Math.Max(m * 2.3e-16, double.Epsilon), $"{a:R} / {b:R} -> [{qi.Lo:R}, {qi.Hi:R}]");
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

        // a random interval: wide, negative, mixed sign, a point, or with an infinite end
        static ExactInterval RandInterval(Random rnd)
        {
            double a = Rand(rnd), b = Rand(rnd);
            switch (rnd.Next(6)) {
                case 0: return new ExactInterval(double.NegativeInfinity, Math.Max(a, b));
                case 1: return new ExactInterval(Math.Min(a, b), double.PositiveInfinity);
                case 2: return new ExactInterval(-Math.Abs(a), Math.Abs(b));                 // mixed sign
                case 3: return new ExactInterval(-Math.Max(Math.Abs(a), Math.Abs(b)), -Math.Min(Math.Abs(a), Math.Abs(b)));   // negative
                case 4: return ExactInterval.Of(a);
                default: return new ExactInterval(Math.Min(a, b), Math.Max(a, b));
            }
        }

        // members of iv for a brute force: both (finite) ends, 0 if inside, and points spread over it; an infinite end is
        // represented by +-MaxValue and its neighbours
        static double[] Members(ExactInterval iv, Random rnd)
        {
            double lo = double.IsNegativeInfinity(iv.Lo) ? -double.MaxValue : iv.Lo, hi = double.IsPositiveInfinity(iv.Hi) ? double.MaxValue : iv.Hi;
            var m = new System.Collections.Generic.List<double> { lo, hi, ExactInterval.Up(lo), ExactInterval.Down(hi) };
            if (lo <= 0 && hi >= 0) { m.Add(0); m.Add(double.Epsilon); m.Add(-double.Epsilon); }
            for (int k = 0; k < 6; ++k) { double t = rnd.NextDouble(); m.Add(lo * t + hi * (1 - t)); m.Add(0.5 * lo + 0.5 * hi); }
            return m.FindAll(x => x >= lo && x <= hi && !double.IsNaN(x)).ToArray();
        }

        [Fact]
        public void Interval_Division_AndSqrt_OfWideMixedNegativeAndInfiniteIntervals_EncloseEveryMember()
        {
            var rnd = new Random(3);
            for (int i = 0; i < 3000; ++i) {
                ExactInterval A = RandInterval(rnd), B = RandInterval(rnd);
                ExactInterval q = A / B, r = ExactInterval.Sqrt(A);
                Assert.True(q.Lo <= q.Hi && r.Lo <= r.Hi);
                foreach (double y in Members(B, rnd)) {
                    if (y == 0) continue;
                    foreach (double x in Members(A, rnd))
                        Assert.True(EnclosesQuotient(q, x, y), $"[{A.Lo:R}, {A.Hi:R}] / [{B.Lo:R}, {B.Hi:R}] -> [{q.Lo:R}, {q.Hi:R}] misses {x:R} / {y:R}");
                }
                foreach (double x in Members(A, rnd))
                    if (x >= 0) Assert.True(EnclosesRoot(r, x), $"sqrt [{A.Lo:R}, {A.Hi:R}] -> [{r.Lo:R}, {r.Hi:R}] misses sqrt {x:R}");
            }
            // infinite ends stay infinite
            Assert.True(double.IsPositiveInfinity((new ExactInterval(1, double.PositiveInfinity) / new ExactInterval(2, 3)).Hi));
            Assert.True(double.IsNegativeInfinity((new ExactInterval(double.NegativeInfinity, -1) / new ExactInterval(2, 3)).Lo));
            Assert.True(double.IsNegativeInfinity((new ExactInterval(1, double.PositiveInfinity) / new ExactInterval(-3, -2)).Lo));
            Assert.True(double.IsPositiveInfinity(ExactInterval.Sqrt(new ExactInterval(-5, double.PositiveInfinity)).Hi));
            // infinity / infinity is NaN at a corner: the whole line, never a NaN end
            ExactInterval n = new ExactInterval(1, double.PositiveInfinity) / new ExactInterval(1, double.PositiveInfinity);
            Assert.True(double.IsNegativeInfinity(n.Lo) && double.IsPositiveInfinity(n.Hi));
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

        // survivors of the terrain-arith shard (mutation run 535, 2026-11-14)

        // a NaN in only one bound (infinity minus infinity at the low end) gives the whole line, not a NaN bound
        [Fact]
        public void OneNaNBound_IsTheWholeLine()
        {
            var a = new ExactInterval(double.PositiveInfinity, double.PositiveInfinity);
            var b = new ExactInterval(1, double.PositiveInfinity);
            ExactInterval d = a - b;
            Assert.Equal(double.NegativeInfinity, d.Lo);
            Assert.Equal(double.PositiveInfinity, d.Hi);
            Assert.Equal(2, d.Sign());
        }

        // the product of mixed-sign intervals: the bounds come from either pair of the four products
        [Fact]
        public void Product_MixedSigns_BoundsFromEitherPair()
        {
            // (lo and hi from p3, p4: a.Hi times b)
            ExactInterval p = new ExactInterval(-2, 3) * new ExactInterval(-5, 7);
            Assert.Equal(ExactInterval.Down(-15), p.Lo);
            Assert.Equal(ExactInterval.Up(21), p.Hi);
            // (lo and hi from p1, p2: a.Lo times b)
            ExactInterval q = new ExactInterval(-3, 2) * new ExactInterval(-7, 5);
            Assert.Equal(ExactInterval.Down(-15), q.Lo);
            Assert.Equal(ExactInterval.Up(21), q.Hi);
        }

        [Fact]
        public void Quotient_OfZero_IsZero()
        {
            double z = ExactDyadic.Quotient(ExactDyadic.Zero, ExactDyadic.Of(3));
            Assert.Equal(0.0, z);
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(z));     // (+0, not -0)
            Assert.Equal(0.0, ExactDyadic.Quotient(ExactDyadic.Zero, ExactDyadic.Of(-3)));
        }

        // Independent bit-manipulation oracle for nextafter(x, +infinity) / nextafter(x, -infinity)
        static double RefUp(double x)
        {
            if (double.IsNaN(x) || x == double.PositiveInfinity) return x;
            if (x == 0) return double.Epsilon;
            long b = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(x > 0 ? b + 1 : b - 1);
        }

        static double RefDown(double x) => -RefUp(-x);

        static void AssertSameDouble(double expected, double actual, double input)
        {
            if (double.IsNaN(expected)) { Assert.True(double.IsNaN(actual), $"input={input:R}"); return; }
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
        }

        [Fact]
        public void UpAndDown_MatchBitExactReferenceAcrossAllExponentsAndSpecials()
        {
            // Explicit specials and critical Rump (2009) boundary values:
            // +-MinNormal (2^-1022), +-2^-1000 (tiny normal scaled by 2^54), +-2^-970, +-2^-969 (RumpMin), +-1e300, +-MaxValue, +-Inf, NaN
            double minNormal = BitConverter.Int64BitsToDouble(0x0010000000000000L);
            double tinyNormal = BitConverter.Int64BitsToDouble(0x0170000000000000L); // 2^-1000
            double belowRump = BitConverter.Int64BitsToDouble(0x0350000000000000L);  // 2^-970
            double atRump = BitConverter.Int64BitsToDouble(0x0360000000000000L);     // 2^-969
            double[] specialsExact = {
                0.0, -0.0, double.Epsilon, -double.Epsilon, 2 * double.Epsilon, -2 * double.Epsilon,
                minNormal, -minNormal, tinyNormal, -tinyNormal, belowRump, -belowRump, atRump, -atRump,
                1e-300, -1e-300, 1.0, -1.0, 1e300, -1e300, double.MaxValue, -double.MaxValue,
                double.PositiveInfinity, double.NegativeInfinity, double.NaN
            };
            foreach (double v in specialsExact) {
                AssertSameDouble(RefUp(v), ExactInterval.Up(v), v);
                AssertSameDouble(RefDown(v), ExactInterval.Down(v), v);
            }
            Assert.Equal(unchecked((long)0x8000000000000000UL), BitConverter.DoubleToInt64Bits(ExactInterval.Up(-double.Epsilon)));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(ExactInterval.Down(double.Epsilon)));
            Assert.Equal(-double.MaxValue, ExactInterval.Up(double.NegativeInfinity));
            Assert.Equal(double.MaxValue, ExactInterval.Down(double.PositiveInfinity));
            Assert.True(ExactInterval.Up(1e300) < double.PositiveInfinity);
            Assert.True(ExactInterval.Down(-1e300) > double.NegativeInfinity);

            // Every binary64 exponent from 2^-1074 to 2^1023, both signs, and +-0..3 ulps around each power of two
            for (int exp = -1074; exp <= 1023; ++exp) {
                long bits = exp < -1022 ? (1L << (exp + 1074)) : ((long)(exp + 1023) << 52);
                foreach (long signBit in new[] { 0L, unchecked((long)0x8000000000000000UL) }) {
                    double baseVal = BitConverter.Int64BitsToDouble(bits | signBit);
                    double v = baseVal;
                    for (int s = 0; s <= 3; ++s) {
                        AssertSameDouble(RefUp(v), ExactInterval.Up(v), v);
                        AssertSameDouble(RefDown(v), ExactInterval.Down(v), v);
                        v = RefUp(v);
                    }
                    v = baseVal;
                    for (int s = 1; s <= 3; ++s) {
                        v = RefDown(v);
                        AssertSameDouble(RefUp(v), ExactInterval.Up(v), v);
                        AssertSameDouble(RefDown(v), ExactInterval.Down(v), v);
                    }
                }
            }

            // 20,000 random finite doubles across the full IEEE 754 bit range
            var rnd = new Random(2009);
            for (int i = 0; i < 20000; ++i) {
                double v = Rand(rnd);
                AssertSameDouble(RefUp(v), ExactInterval.Up(v), v);
                AssertSameDouble(RefDown(v), ExactInterval.Down(v), v);
            }
        }

        [Theory]
        [InlineData(-980)]
        [InlineData(-500)]
        [InlineData(-10)]
        [InlineData(10)]
        [InlineData(500)]
        public void ExactInterval_PowerOfTwoScaleInvariance(int k)
        {
            double s = Math.Pow(2, k);
            double s2 = s * s;
            var a = new ExactInterval(-1.3, 2.7);
            var b = new ExactInterval(0.4, 3.1);
            var sa = new ExactInterval(a.Lo * s, a.Hi * s);
            var sb = new ExactInterval(b.Lo * s, b.Hi * s);

            Assert.Equal(BitConverter.DoubleToInt64Bits(ExactInterval.Up(a.Hi) * s), BitConverter.DoubleToInt64Bits(ExactInterval.Up(sa.Hi)));
            Assert.Equal(BitConverter.DoubleToInt64Bits(ExactInterval.Down(a.Lo) * s), BitConverter.DoubleToInt64Bits(ExactInterval.Down(sa.Lo)));

            ExactInterval sum = a + b, ssum = sa + sb;
            Assert.Equal(BitConverter.DoubleToInt64Bits(sum.Lo * s), BitConverter.DoubleToInt64Bits(ssum.Lo));
            Assert.Equal(BitConverter.DoubleToInt64Bits(sum.Hi * s), BitConverter.DoubleToInt64Bits(ssum.Hi));

            ExactInterval diff = a - b, sdiff = sa - sb;
            Assert.Equal(BitConverter.DoubleToInt64Bits(diff.Lo * s), BitConverter.DoubleToInt64Bits(sdiff.Lo));
            Assert.Equal(BitConverter.DoubleToInt64Bits(diff.Hi * s), BitConverter.DoubleToInt64Bits(sdiff.Hi));

            if (Math.Abs(k) <= 200) {
                ExactInterval prod = a * b, sprod = sa * sb;
                Assert.Equal(BitConverter.DoubleToInt64Bits(prod.Lo * s2), BitConverter.DoubleToInt64Bits(sprod.Lo));
                Assert.Equal(BitConverter.DoubleToInt64Bits(prod.Hi * s2), BitConverter.DoubleToInt64Bits(sprod.Hi));
                ExactInterval sq = ExactInterval.Square(a), ssq = ExactInterval.Square(sa);
                Assert.Equal(BitConverter.DoubleToInt64Bits(sq.Lo * s2), BitConverter.DoubleToInt64Bits(ssq.Lo));
                Assert.Equal(BitConverter.DoubleToInt64Bits(sq.Hi * s2), BitConverter.DoubleToInt64Bits(ssq.Hi));
            }
            ExactInterval quot = a / b, squot = sa / sb;
            Assert.Equal(BitConverter.DoubleToInt64Bits(quot.Lo), BitConverter.DoubleToInt64Bits(squot.Lo));
            Assert.Equal(BitConverter.DoubleToInt64Bits(quot.Hi), BitConverter.DoubleToInt64Bits(squot.Hi));
        }
    }
}
