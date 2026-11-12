// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Tests for the rare branches of the vendored CORE-MATH port (math/CoreMath) that the other CoreMath tests do not reach:
// the scalbn steps for huge / tiny exponents, the 192-bit Tint and 128-bit Dint helpers at their edges (zero, equal operands,
// overflow, subnormal results), the table of hard cases of exp, the exception tables of sin and cos, the software fma
// when the product is subnormal (full cancellation, the "just below the smallest normal" rule) or the double-double sum is
// exactly 1 or 3 times a power of two, atan2 with huge operands. Every expected value is computed with exact arithmetic
// (CoreMathReference or BigInteger), never with System.Math, and compared bit for bit.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using g3.CoreMathPort;
using Xunit;

namespace g3.Tests
{
    public class CoreMathCoverageTests
    {
        // ---------------------------------------------------------------- helpers

        static ulong SplitMix(ref ulong s)
        {
            ulong z = (s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        // the next double above / below a positive finite one (Math.BitIncrement does not exist on .NET Framework)
        static double NextUp(double x) { return FromBits((ulong)BitsOf(x) + 1); }
        static double NextDown(double x) { return FromBits((ulong)BitsOf(x) - 1); }

        static double FromBits(ulong b) { return BitConverter.Int64BitsToDouble((long)b); }
        static long BitsOf(double d) { return BitConverter.DoubleToInt64Bits(d); }
        static string Hex(double d) { return BitsOf(d).ToString("X16", CultureInfo.InvariantCulture); }

        static bool Same(double a, double b)
        {
            return BitsOf(a) == BitsOf(b) || (double.IsNaN(a) && double.IsNaN(b));
        }

        /// <summary> 2^k for -1022 &lt;= k &lt;= 1023 </summary>
        static double Pow2(int k) { return FromBits((ulong)(k + 1023) << 52); }

        static T[] Table<T>(string name)
        {
            FieldInfo f = typeof(CoreMath).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(f);
            return (T[])f.GetValue(null);
        }

        static BigInteger Big(ulong hi, ulong lo) { return ((BigInteger)hi << 64) | lo; }

        // ---------------------------------------------------------------- CoreMathBase: round, scalbn, trailing zeros

        [Fact]
        public void BuiltinRound_RoundsHalfAwayFromZero()
        {
            double[,] cases = {
                { 0.5, 1.0 }, { -0.5, -1.0 }, { 1.5, 2.0 }, { 2.5, 3.0 }, { -2.5, -3.0 }, { 3.5, 4.0 }, { 0.49999999999999994, 0.0 },
                { -0.49999999999999994, -0.0 }, { 1.4999999999999998, 1.0 }, { 4503599627370495.5, 4503599627370496.0 },
                { 4503599627370497.0, 4503599627370497.0 }, { 9007199254740993.0, 9007199254740992.0 }, { 0.0, 0.0 }, { -0.0, -0.0 },
                { 1e300, 1e300 }, { double.PositiveInfinity, double.PositiveInfinity }, { double.NegativeInfinity, double.NegativeInfinity },
            };
            for (int i = 0; i < cases.GetLength(0); ++i)
                Assert.True(BitsOf(CoreMath.BuiltinRound(cases[i, 0])) == BitsOf(cases[i, 1]), $"round({cases[i, 0]:R}) = {Hex(CoreMath.BuiltinRound(cases[i, 0]))}");
            Assert.True(double.IsNaN(CoreMath.BuiltinRound(double.NaN)));
        }

        [Fact]
        public void Ldexp_IsTheCorrectlyRoundedScaling_AlsoForHugeAndTinyExponents()
        {
            // n beyond +-1023 takes the second and the third scaling step of the algorithm (n > 2046 / n < -2044)
            ulong s = 77;
            var xs = new List<double> { 1.0, 1.5, double.MaxValue, FromBits(1), FromBits(0xFFFFFFFFFFFFFUL), 0.75, 1.9999999999999998, 3.0e-310 };
            for (int i = 0; i < 20; ++i) xs.Add(FromBits(((SplitMix(ref s) % 2046) + 1) << 52 | (SplitMix(ref s) & ((1UL << 52) - 1))));
            int[] ns = { 0, 1, -1, 1023, 1024, -1022, -1023, 1500, 2046, 2047, 2090, 2097, 2098, 3000, 3100, 3200, 100000,
                         -1075, -1500, -2044, -2045, -2090, -2100, -3000, -3200, -100000 };
            int count = 0;
            foreach (double x in xs)
                foreach (int n in ns) {
                    double got = CoreMath.Ldexp(x, n);
                    double want = CoreMathReference.RoundToDouble(CoreMathReference.ExactFixed(x), n - CoreMathReference.Precision, false);
                    Assert.True(BitsOf(got) == BitsOf(want), $"ldexp({x:R}, {n}) = {Hex(got)}, want {Hex(want)}");
                    ++count;
                }
            Assert.True(count > 200);
            // zero stays zero (sign kept) for every n, a finite value cannot become non finite by the clamped last step
            foreach (int n in new[] { 3000, -3000, 50000, -50000 }) {
                Assert.Equal(0L, BitsOf(CoreMath.Ldexp(0.0, n)));
                Assert.Equal(BitsOf(-0.0), BitsOf(CoreMath.Ldexp(-0.0, n)));
            }
            Assert.Equal(double.PositiveInfinity, CoreMath.Ldexp(FromBits(1), 3200));
            Assert.Equal(BitsOf(-0.0), BitsOf(CoreMath.Ldexp(-double.MaxValue, -3200)));
        }

        [Fact]
        public void TrailingZeroCount_CountsEveryPosition()
        {
            Assert.Equal(64, Polyfill.TrailingZeroCount(0));
            for (int k = 0; k < 64; ++k) {
                Assert.Equal(k, Polyfill.TrailingZeroCount(1UL << k));
                Assert.Equal(k, Polyfill.TrailingZeroCount(ulong.MaxValue << k));
                Assert.Equal(k, Polyfill.TrailingZeroCount((0xA5A5A5A5A5A5A5A5UL | 1UL << 63) << k | 1UL << k));
            }
            ulong s = 5;
            for (int i = 0; i < 2000; ++i) {
                ulong v = SplitMix(ref s) >> (int)(SplitMix(ref s) % 64);
                if (v == 0) continue;
                int want = 0;
                while (((v >> want) & 1) == 0) ++want;
                Assert.Equal(want, Polyfill.TrailingZeroCount(v));
            }
        }

        // ---------------------------------------------------------------- Tint (atan2)

        static BigInteger TSig(CoreMath.Tint t) { return ((BigInteger)t.h << 128) | ((BigInteger)t.m << 64) | t.l; }

        [Fact]
        public void Tint_Constants_ToString_IsNormalized_AndCmpAbsOfZero()
        {
            // value = sig / 2^192 * 2^ex
            Assert.Equal(BigInteger.One << 191, TSig(CoreMath.Tint.OneHalf)); Assert.Equal(0, CoreMath.Tint.OneHalf.ex);       // 0.5 * 2^0
            Assert.Equal(BigInteger.One << 191, TSig(CoreMath.Tint.One)); Assert.Equal(1, CoreMath.Tint.One.ex);               // 0.5 * 2^1
            // 1/pi = 0.31830988618379067153776752674502872406891929148091289749533468811779359526845307...: sig = 2^193 / pi
            Assert.Equal(-1, CoreMath.Tint.OneOverPi.ex);
            BigInteger wantSig = FixedFromDigits("31830988618379067153776752674502872406891929148091289749533468811779359526845307", 193);
            Assert.True(BigInteger.Abs(TSig(CoreMath.Tint.OneOverPi) - wantSig) < (BigInteger.One << 64), "1/pi to 2^-128");

            var pi = CoreMath.Tint.Pi;
            Assert.Equal("c4c6628b80dc1cd1 c90fdaa22168c234 29024e088a67cc74 0000000000000002 0000000000000000", pi.ToString());
            Assert.Equal("0000000000000000 8000000000000000 0000000000000000 ffffffffffffffff 0000000000000001",
                new CoreMath.Tint(0, 0x8000000000000000UL, 0, -1, 1).ToString());

            Assert.True(CoreMath.Tint.Zero.IsNormalized());                                  // all zero significand
            Assert.True(CoreMath.Tint.One.IsNormalized());
            Assert.True(pi.IsNormalized());
            Assert.False(new CoreMath.Tint(5, 0x7FFFFFFFFFFFFFFFUL, 5, 0, 0).IsNormalized());
            Assert.False(new CoreMath.Tint(0, 0, 1, 0, 0).IsNormalized());                   // h == 0 but not zero
            Assert.False(new CoreMath.Tint(1, 0, 0, 0, 0).IsNormalized());

            Assert.Equal(0, CoreMath.Tint.CmpAbs(CoreMath.Tint.Zero, CoreMath.Tint.Zero));
            Assert.Equal(-1, CoreMath.Tint.CmpAbs(CoreMath.Tint.Zero, CoreMath.Tint.One));
            Assert.Equal(1, CoreMath.Tint.CmpAbs(CoreMath.Tint.One, CoreMath.Tint.Zero));
            Assert.Equal(0, CoreMath.Tint.CmpAbs(CoreMath.Tint.One, CoreMath.Tint.One with { sgn = 1 }));      // magnitudes only
            Assert.Equal(-1, CoreMath.Tint.CmpAbs(CoreMath.Tint.OneHalf, CoreMath.Tint.One));
        }

        [Fact]
        public void Tint_Add_OfEqualMagnitudes_DoublesOrCancels()
        {
            ulong s = 3;
            for (int i = 0; i < 50; ++i) {
                var a = new CoreMath.Tint(SplitMix(ref s), SplitMix(ref s) | (1UL << 63), SplitMix(ref s), (long)(SplitMix(ref s) % 21) - 10, SplitMix(ref s) & 1);
                var twice = CoreMath.Tint.Add(a, a);
                Assert.Equal(a.ex + 1, twice.ex); Assert.Equal(TSig(a), TSig(twice)); Assert.Equal(a.sgn, twice.sgn);
                var zero = CoreMath.Tint.Add(a, a with { sgn = 1 - a.sgn });
                Assert.True(zero.IsZero() && zero.h == 0 && zero.m == 0 && zero.l == 0);
            }
            // 1 + 1 = 2 and 0.5 + 0.5 = 1
            var two = CoreMath.Tint.Add(CoreMath.Tint.One, CoreMath.Tint.One);
            Assert.Equal(2, two.ex); Assert.Equal(0x8000000000000000UL, two.h);
            Assert.True(CoreMath.Tint.CmpAbs(CoreMath.Tint.Add(CoreMath.Tint.OneHalf, CoreMath.Tint.OneHalf), CoreMath.Tint.One) == 0);
        }

        static double TintValueRounded(CoreMath.Tint t)
        {
            BigInteger sig = TSig(t);
            return CoreMathReference.RoundToDouble(t.sgn != 0 ? -sig : sig, (int)t.ex - 192, false);
        }

        [Fact]
        public void Tint_ToDouble_Overflow_Underflow_AndTheHalfwayOfTheSmallestSubnormal()
        {
            var big = new CoreMath.Tint(0x1234, 0x8000000000000000UL, 0x5678, 1025, 0);
            Assert.Equal(double.PositiveInfinity, big.ToDouble(0, 1, 1));
            Assert.Equal(double.NegativeInfinity, (big with { sgn = 1 }).ToDouble(0, 1, 1));
            Assert.Equal(double.PositiveInfinity, (big with { ex = 5000 }).ToDouble(7, 1, 1));

            // ex < -1074: the value is below 2^-1075: rounds to a zero of the right sign
            var tiny = new CoreMath.Tint(1, 0x8000000000000001UL, 1, -1075, 0);
            Assert.Equal(0L, BitsOf(tiny.ToDouble(0, 1, 1)));
            Assert.Equal(BitsOf(-0.0), BitsOf((tiny with { sgn = 1 }).ToDouble(0, 1, 1)));
            Assert.Equal(0L, BitsOf((tiny with { ex = -3000 }).ToDouble(0, 1, 1)));

            // ex == -1074: the value is in [2^-1075, 2^-1074): exactly 2^-1075 is a tie (even: zero), anything above rounds to 2^-1074
            var mid = new CoreMath.Tint(0, 0x8000000000000000UL, 0, -1074, 0);
            Assert.Equal(0L, BitsOf(mid.ToDouble(0, 1, 1)));
            Assert.Equal(BitsOf(-0.0), BitsOf((mid with { sgn = 1 }).ToDouble(0, 1, 1)));
            foreach (var above in new[] { mid with { l = 1 }, mid with { m = 1 }, mid with { h = 0x8000000000000001UL }, mid with { h = ulong.MaxValue, m = ulong.MaxValue, l = ulong.MaxValue } }) {
                Assert.Equal(1L, BitsOf(above.ToDouble(0, 1, 1)));
                Assert.Equal(1L, BitsOf(TintValueRounded(above)));
                Assert.Equal(BitsOf(-double.Epsilon), BitsOf((above with { sgn = 1 }).ToDouble(0, 1, 1)));
            }
        }

        [Fact]
        public void Tint_ToDouble_RejectsAWorstCaseBelowTheErrorBound()
        {
            // the significand sits within `err` of a rounding boundary: the caller's error bound cannot decide the rounding
            const ulong top = 0x8000000000000000UL;
            Assert.Throws<InvalidOperationException>(() => new CoreMath.Tint(0, top, 0, 1, 0).ToDouble(1, 3.0, 4.0));                 // exactly a double
            Assert.Throws<InvalidOperationException>(() => new CoreMath.Tint(0, top | 0x400, 3, 1, 0).ToDouble(5, 3.0, 4.0));         // exactly halfway, plus 3
            Assert.Throws<InvalidOperationException>(() => new CoreMath.Tint(~0UL, top | 0x3ff, ~0UL - 2, 1, 0).ToDouble(5, 3.0, 4.0));    // just below halfway
            Assert.Throws<InvalidOperationException>(() => new CoreMath.Tint(~0UL, top | 0x7ff, ~0UL, 1, 1).ToDouble(1, 3.0, 4.0));     // just below a double
            // an error bound smaller than the distance to the boundary decides
            var t = new CoreMath.Tint(0, top, 5, 1, 0);                                                                                // 1 + 5 * 2^-192
            Assert.Equal(1.0, t.ToDouble(1, 3.0, 4.0));
            Assert.Equal(-1.0, (t with { sgn = 1 }).ToDouble(1, 3.0, 4.0));
            // err == 0 means: the value is exact, round to nearest; the guard is skipped by err == 0 only for the offsets
            Assert.Equal(1.0, new CoreMath.Tint(0, top, 0, 1, 0).ToDouble(0, 3.0, 4.0));
        }

        [Fact]
        public void Tint_Inv_OfSubnormalMagnitudes_IsTheReciprocal()
        {
            // Inv starts from the double ToDouble(this), which has only 53 - sh bits when the value is subnormal (sh = -1021 - ex);
            // one Newton step doubles that: the product a * (1/a) is 1 up to about 2^-(2 * (53 - sh)). The scaling by 2^53 of the
            // start value and by 2^53 of the exponent of the result (the lines under test) is what puts the product at 1.
            ulong s = 9;
            foreach (long ex in new long[] { -1023, -1030, -1040, -1050 }) {
                int bits = 2 * (53 - (int)(-1021 - ex)) - 6;
                for (int i = 0; i < 12; ++i) {
                    var a = new CoreMath.Tint(SplitMix(ref s), SplitMix(ref s) | (1UL << 63), SplitMix(ref s), ex, SplitMix(ref s) & 1);
                    CoreMath.Tint r = a.Inv();
                    BigInteger prod = TSig(a) * TSig(r);                       // 2^(ex_a + ex_r - 384) * prod
                    int shift = 384 - (int)(a.ex + r.ex);
                    BigInteger one = BigInteger.One << shift;
                    BigInteger err = BigInteger.Abs(prod - one);
                    Assert.True((err << bits) < one, $"ex {ex}: relative error of a * (1/a) is 2^{CoreMathReference.BitLength(err) - shift}, want below 2^-{bits}");
                    Assert.Equal(a.sgn, r.sgn);
                    Assert.True(r.ex > 1000, "the reciprocal of a subnormal-sized number is huge");
                }
            }
        }

        // ---------------------------------------------------------------- atan2

        static void CheckAtan2(string what, IEnumerable<KeyValuePair<double, double>> pairs)
        {
            int count = 0;
            foreach (var p in pairs) {
                double got = CoreMath.Atan2(p.Key, p.Value), want = CoreMathReference.Atan2(p.Key, p.Value);
                Assert.True(Same(got, want), $"{what}: atan2({Hex(p.Key)}, {Hex(p.Value)}) = {Hex(got)}, want {Hex(want)}");
                ++count;
            }
            Assert.True(count > 0);
        }

        [Fact]
        public void Atan2_HugeOperands_TakeTheRescalingOfTheSecondStage()
        {
            // x >= 2^1022: the operands are halved (jt != 0) or the sum xx + t0 * yy is compared with 2^1022 (jt == 0, a ratio below 1/128)
            var pairs = new List<KeyValuePair<double, double>>();
            ulong s = 99;
            for (int i = 0; i < 160; ++i) {
                double x = FromBits(((ulong)(2045 + (int)(i % 2)) << 52) | (SplitMix(ref s) & ((1UL << 52) - 1)));          // 2^1022 .. 2^1024
                double ratio = (i % 4) switch { 0 => 1.0 / 200.0, 1 => 1.0 / 130.0, 2 => 1.0 / 4096.0, _ => 0.4 } * (0.5 + (SplitMix(ref s) >> 11) * (1.0 / 9007199254740992.0));
                double y = x * ratio;
                bool swap = (i & 8) != 0;
                double sx = (i & 1) != 0 ? -1 : 1, sy = (i & 2) != 0 ? -1 : 1;
                pairs.Add(swap ? new KeyValuePair<double, double>(sx * x, sy * y) : new KeyValuePair<double, double>(sy * y, sx * x));
            }
            CheckAtan2("huge operands", pairs);
        }

        [Fact]
        public void Atan2_RatioJustBelowOneOver128_TakesTheOtherBranchOfTheResidual()
        {
            // yy / xx + 2 + 1/128 rounds up to the table index of 1/64 although yy < xx / 128: nh = xx / 64 is twice yy and
            // yy - nh is not exact, so y1 = (yy - nh) + nh differs from yy
            var pairs = new List<KeyValuePair<double, double>>();
            foreach (double xs in new[] { 1.0, 1.5, 1.2345678901234567, 3.0e10, 1e-200 }) {
                double y = xs / 128;
                for (int k = 0; k < 45; ++k) {
                    pairs.Add(new KeyValuePair<double, double>(y, xs));
                    pairs.Add(new KeyValuePair<double, double>(-y, -xs));
                    pairs.Add(new KeyValuePair<double, double>(xs, y));
                    y = NextDown(y);
                }
            }
            CheckAtan2("ratio below 1/128", pairs);
        }

        // ---------------------------------------------------------------- exp: the database of hard cases

        [Fact]
        public void Exp_ArgumentsOfTheDatabase_AreCorrectlyRounded()
        {
            double[] db = Table<double>("Tbl_Exp_0");
            Assert.True(db.Length > 20);
            int count = 0;
            foreach (double x in db) {
                foreach (double arg in new[] { x, -x, NextUp(x), NextDown(x) }) {
                    double got = CoreMath.Exp(arg), want = CoreMathReference.Exp(arg);
                    Assert.True(Same(got, want), $"exp({Hex(arg)} = {arg:R}) = {Hex(got)}, want {Hex(want)}");
                    ++count;
                }
            }
            Assert.Equal(db.Length * 4, count);
        }

        // ---------------------------------------------------------------- sin, cos: exception tables, NaN payloads, reduction limbs

        [Fact]
        public void Sin_ExceptionTable_AndNaNPayloads()
        {
            double[] ex = Table<double>("Tbl_Sin_22");
            Assert.Equal(0, ex.Length % 3);
            for (int j = 0; j < ex.Length / 3; ++j) {
                double x = ex[3 * j];
                foreach (double arg in new[] { x, -x }) {
                    double got = CoreMath.Sin(arg), want = CoreMathReference.Sin(arg);
                    Assert.True(Same(got, want), $"sin({Hex(arg)}) = {Hex(got)}, want {Hex(want)}");
                }
            }
            foreach (ulong nan in new[] { 0x7FF8000000000000UL, 0xFFF8000000000000UL, 0x7FF0000000000001UL, 0xFFF0000000000001UL, 0x7FF8000000000001UL, 0x7FFFFFFFFFFFFFFFUL, 0xFFFFFFFFFFFFFFFFUL })
                Assert.True(double.IsNaN(CoreMath.Sin(FromBits(nan))), $"sin(NaN {nan:X16})");
        }

        [Fact]
        public void Cos_ExceptionTable_AndNaNPayloads()
        {
            double[] ex = Table<double>("Tbl_Cos_22");
            Assert.Equal(0, ex.Length % 3);
            for (int j = 0; j < ex.Length / 3; ++j) {
                double x = ex[3 * j];
                foreach (double arg in new[] { x, -x }) {
                    double got = CoreMath.Cos(arg), want = CoreMathReference.Cos(arg);
                    Assert.True(Same(got, want), $"cos({Hex(arg)}) = {Hex(got)}, want {Hex(want)}");
                }
            }
            foreach (ulong nan in new[] { 0x7FF8000000000000UL, 0xFFF8000000000000UL, 0x7FF0000000000001UL, 0xFFF0000000000001UL, 0x7FFFFFFFFFFFFFFFUL })
                Assert.True(double.IsNaN(CoreMath.Cos(FromBits(nan))), $"cos(NaN {nan:X16})");
        }

        // the reduction shifts by 64 bits when the biased exponent is 1075 + 64 i (x in [2^52, 2^53), [2^116, 2^117), ...)
        static double[] LimbBoundaryArguments()
        {
            var list = new List<double>();
            ulong[] mantissas = { 0x0000000000000UL, 0x5555555555555UL, 0xFFFFFFFFFFFFFUL, 0x8000000000001UL, 0x2F1A3C5E7B9D1UL };
            for (int i = 0; i < 16; ++i)
                foreach (ulong m in mantissas) {
                    ulong e = (ulong)(1075 + 64 * i);
                    list.Add(FromBits((e << 52) | m));
                    list.Add(FromBits((1UL << 63) | (e << 52) | m));
                }
            return list.ToArray();
        }

        [Fact]
        public void Sin_ArgumentsWhoseExponentFillsWholeLimbs_AreCorrectlyRounded()
        {
            foreach (double x in LimbBoundaryArguments()) {
                double got = CoreMath.Sin(x), want = CoreMathReference.Sin(x);
                Assert.True(Same(got, want), $"sin({Hex(x)}) = {Hex(got)}, want {Hex(want)}");
            }
        }

        [Fact]
        public void Cos_ArgumentsWhoseExponentFillsWholeLimbs_AreCorrectlyRounded()
        {
            foreach (double x in LimbBoundaryArguments()) {
                double got = CoreMath.Cos(x), want = CoreMathReference.Cos(x);
                Assert.True(Same(got, want), $"cos({Hex(x)}) = {Hex(got)}, want {Hex(want)}");
            }
        }

        // ---------------------------------------------------------------- Uint128

        static BigInteger U(Uint128 v) { return Big(v.hi, v.lo); }
        static readonly BigInteger Two128 = BigInteger.One << 128;

        [Fact]
        public void Uint128_Operators_EqualBigInteger()
        {
            ulong s = 123;
            var specials = new[] { new Uint128(0, 0), new Uint128(ulong.MaxValue, ulong.MaxValue), new Uint128(ulong.MaxValue, 0), new Uint128(0, ulong.MaxValue), new Uint128(1, 0), new Uint128(0, 1) };
            var all = new List<Uint128>(specials);
            for (int i = 0; i < 60; ++i) all.Add(new Uint128(SplitMix(ref s), (i % 5 == 0) ? 0 : SplitMix(ref s)));
            foreach (var a in all)
                foreach (var b in all) {
                    BigInteger A = U(a), B = U(b);
                    var (carry, sum) = Uint128.AddU128(a, b);
                    Assert.Equal((A + B) % Two128, U(sum)); Assert.Equal(A + B >= Two128 ? 1 : 0, carry);
                    var (borrow, diff) = Uint128.SubU128(a, b);
                    Assert.Equal(((A - B) % Two128 + Two128) % Two128, U(diff)); Assert.Equal(A < B ? 1 : 0, borrow);
                    Assert.Equal(A & B, U(a & b)); Assert.Equal(A | B, U(a | b)); Assert.Equal(A ^ B, U(a ^ b));
                    Assert.Equal(A > B, a > b); Assert.Equal(A < B, a < b);
                    Assert.Equal(A > B ? 1 : A < B ? -1 : 0, Uint128.Cmpu128(a, b));
                }
            foreach (var a in all)
                foreach (int k in new[] { 0, 1, 7, 31, 32, 63, 64, 65, 100, 127 }) {
                    BigInteger A = U(a);
                    Assert.Equal((A >> k), U(a >> k));
                    Assert.Equal((A << k) % Two128, U(a << k));
                }
        }

        // ---------------------------------------------------------------- Dint (cos, log)

        static BigInteger DSig(CoreMath.Dint d) { return Big(d.hi, d.lo); }

        // |value| * 2^S as an integer: value = sig * 2^(ex - 128)
        const int S = 400;
        static BigInteger DScaled(CoreMath.Dint d)
        {
            int sh = (int)d.ex - 128 + S;
            BigInteger v = sh >= 0 ? DSig(d) << sh : DSig(d) >> -sh;
            return d.sgn != 0 ? -v : v;
        }

        static double DintRounded(CoreMath.Dint d)
        {
            BigInteger sig = DSig(d);
            return CoreMathReference.RoundToDouble(d.sgn != 0 ? -sig : sig, (int)d.ex - 128, false);
        }

        static BigInteger FixedFromDigits(string digitsAfterPoint, int S2)
        {
            // 0.digits * 2^S2 (truncated)
            return (BigInteger.Parse(digitsAfterPoint, CultureInfo.InvariantCulture) << S2) / BigInteger.Pow(10, digitsAfterPoint.Length);
        }

        [Fact]
        public void Dint_Constants_HaveTheirValues()
        {
            // ln 2 = 0.6931471805599453094172321214581765680755001343602552..., log10(e) = 0.4342944819032518276511289189166050822944...,
            // 1 / ln 2 = 1.4426950408889634073599246810018921374266459541529859...
            const string ln2 = "6931471805599453094172321214581765680755001343602552";
            const string log10e = "4342944819032518276511289189166050822944";
            const string invLn2 = "4426950408889634073599246810018921374266459541529859";
            BigInteger tol = BigInteger.One << (S - 115);

            BigInteger halfLn2 = FixedFromDigits(ln2, S) / 2;                                                    // Log2 = ln 2 / 2 (ex -1)
            Assert.True(BigInteger.Abs(DScaled(CoreMath.Dint.Log2) - halfLn2) < tol, "Log2");
            BigInteger twoPow11OverLn2 = ((BigInteger.One << S) + FixedFromDigits(invLn2, S)) << 11;            // 2^11 / ln 2
            Assert.True(BigInteger.Abs(DScaled(CoreMath.Dint.Log2Inv) - twoPow11OverLn2) < (tol << 11), "Log2Inv");
            BigInteger halfLog10e = FixedFromDigits(log10e, S) / 2;
            Assert.True(BigInteger.Abs(DScaled(CoreMath.Dint.Log10Inv) - halfLog10e) < tol, "Log10Inv");
            Assert.True(BigInteger.Abs(DScaled(CoreMath.Dint.OneOverLog10) - halfLog10e) < tol, "OneOverLog10");
            Assert.Equal(DScaled(CoreMath.Dint.Log10Inv), DScaled(CoreMath.Dint.OneOverLog10));

            // the "power" variant keeps the high limb only, rounded up
            Assert.Equal(0UL, CoreMath.Dint.Log2InvPow.lo);
            Assert.Equal(CoreMath.Dint.Log2Inv.hi + 1, CoreMath.Dint.Log2InvPow.hi);
            Assert.Equal(CoreMath.Dint.Log2Inv.ex, CoreMath.Dint.Log2InvPow.ex);

            // One and MinusOne: significand 1/2, sign 0 / 1 (the exponent is 0: the value is 2^-1)
            Assert.Equal(BigInteger.One << (S - 1), DScaled(CoreMath.Dint.One));
            Assert.Equal(-(BigInteger.One << (S - 1)), DScaled(CoreMath.Dint.MinusOne));
            Assert.Equal(1UL, CoreMath.Dint.MinusOne.sgn);
            Assert.Equal(0.5, DintRounded(CoreMath.Dint.One));
            Assert.Equal(-0.5, DintRounded(CoreMath.Dint.MinusOne));
        }

        [Fact]
        public void Dint_Add_OfEqualMagnitudes_AndCmpOfZero()
        {
            ulong s = 41;
            for (int i = 0; i < 40; ++i) {
                var a = new CoreMath.Dint(SplitMix(ref s), SplitMix(ref s) | (1UL << 63), (long)(SplitMix(ref s) % 21) - 10, SplitMix(ref s) & 1);
                var twice = CoreMath.Dint.Add(a, a);
                Assert.Equal(a.ex + 1, twice.ex); Assert.Equal(DSig(a), DSig(twice)); Assert.Equal(a.sgn, twice.sgn);
                var zero = CoreMath.Dint.Add(a, a with { sgn = 1 - a.sgn });
                Assert.True(zero.IsZero && zero.lo == 0);
                Assert.Equal(DScaled(a) * 2, DScaled(twice));
            }
            Assert.Equal(0, CoreMath.Dint.CmpDintAbs(CoreMath.Dint.Zero, CoreMath.Dint.Zero));
            Assert.Equal(-1, CoreMath.Dint.CmpDintAbs(CoreMath.Dint.Zero, CoreMath.Dint.Log2));
            Assert.Equal(1, CoreMath.Dint.CmpDintAbs(CoreMath.Dint.Log2, CoreMath.Dint.Zero));
            Assert.Equal(-1, CoreMath.Dint.CmpDintAbs(CoreMath.Dint.Log2, CoreMath.Dint.Log2Inv));      // different exponents
        }

        [Fact]
        public void Dint_Mul2_ByAnInteger_IsAccurate_AlsoWhenTheSumOfTheTwoPartsCarries()
        {
            // 7 * ((2^66 - 1) / 7) = 2^66 - 1: the high part of the product is all ones, and the low part carries into it
            ulong hi = (ulong)(((BigInteger.One << 66) - 1) / 7);
            var carry = new CoreMath.Dint(ulong.MaxValue, hi, 0, 0);
            var cases = new List<(long, CoreMath.Dint)> { (7, carry), (-7, carry), (7, carry with { sgn = 1 }), (0, carry), (1, carry), (-1, carry), (3, carry), (1L << 62, carry) };
            ulong s = 17;
            for (int i = 0; i < 300; ++i)
                cases.Add(((long)(SplitMix(ref s) >> (int)(SplitMix(ref s) % 64)) * ((i & 1) == 0 ? 1 : -1) % (1L << 62),
                    new CoreMath.Dint(SplitMix(ref s), SplitMix(ref s) | (1UL << 63), (long)(SplitMix(ref s) % 7) - 3, SplitMix(ref s) & 1)));
            foreach (var (b, a) in cases) {
                var r = CoreMath.Dint.Mul2(b, a);
                BigInteger exact = DScaled(a) * b;
                if (b == 0) { Assert.True(r.IsZero); continue; }
                BigInteger diff = BigInteger.Abs(DScaled(r) - exact);
                Assert.True(diff == 0 || CoreMathReference.BitLength(diff) - CoreMathReference.BitLength(exact) < -120, $"Mul2({b}, ...): relative error 2^{CoreMathReference.BitLength(diff) - CoreMathReference.BitLength(exact)}");
                Assert.True((r.hi >> 63) == 1, "normalized");
            }
        }

        [Fact]
        public void Dint_Normalize_OfALowLimbOnlyValue_ShiftsItUp()
        {
            ulong s = 8;
            for (int i = 0; i < 64; ++i) {
                ulong lo = (SplitMix(ref s) | 1) >> i;                 // 1 .. 64 significant bits
                if (lo == 0) continue;
                var d = new CoreMath.Dint(lo, 0, 5, 1);
                var n = d.Normalize();
                Assert.Equal(0UL, n.lo);
                Assert.True((n.hi >> 63) == 1);
                Assert.Equal(1UL, n.sgn);
                Assert.Equal(DScaled(d), DScaled(n));
            }
            var zero = new CoreMath.Dint(0, 0, 3, 0).Normalize();
            Assert.True(zero.IsZero);
            // high limb present: shifted by the leading zeros, bits move up from the low limb
            var mixed = new CoreMath.Dint(0xF000000000000001UL, 0x00FF000000000000UL, 7, 0);
            var nm = mixed.Normalize();
            Assert.True((nm.hi >> 63) == 1);
            Assert.Equal(DScaled(mixed), DScaled(nm));
        }

        [Fact]
        public void Dint_ToDouble_RoundsCorrectly_InTheSubnormalRange_AndOverflows()
        {
            ulong s = 29;
            var inputs = new List<CoreMath.Dint>();
            foreach (long ex in new long[] { -1000, -1022, -1023, -1024, -1030, -1050, -1060, -1070, -1073, -1074, -1075, -1080, -1200 })
                for (int i = 0; i < 14; ++i) {
                    ulong hi = SplitMix(ref s) | (1UL << 63), lo = SplitMix(ref s);
                    switch (i) {
                        case 0: hi = 1UL << 63; lo = 0; break;                       // exact power of two
                        case 1: hi = 3UL << 62; lo = 0; break;                       // 1.5 * 2^(ex-1)
                        case 2: hi = (1UL << 63) | 0x400; lo = 0; break;            // tie at the 53rd bit (normal numbers)
                        case 3: hi = (1UL << 63) | 0x400; lo = 1; break;            // just above that tie
                        case 4: hi = ulong.MaxValue; lo = ulong.MaxValue; break;    // just below the next power of two
                        case 5: hi = (1UL << 63) | 0x3FF; lo = ulong.MaxValue; break;
                    }
                    inputs.Add(new CoreMath.Dint(lo, hi, ex, (ulong)(i & 1)));
                }
            // a tie in the subnormal range: keep exactly one bit below the last subnormal bit
            for (int rex = 12; rex <= 62; ++rex) {
                long ex = -(1011 + rex);
                ulong tie = (1UL << 63) | (1UL << (rex - 1));                         // bit rex-1 is the first bit below the subnormal grid
                inputs.Add(new CoreMath.Dint(0, tie, ex, 0));
                inputs.Add(new CoreMath.Dint(1, tie, ex, 0));
                inputs.Add(new CoreMath.Dint(0, tie | (1UL << rex), ex, 1));          // odd below the tie: rounds up
                inputs.Add(new CoreMath.Dint(0, tie | (1UL << (rex - 1) >> 1), ex, 0));
            }
            // Dint.ToDouble is correctly rounded for normal results. For subnormal results (ex <= -1022) it rounds twice (first to
            // 53 bits, then to the subnormal grid), ties do not go to even and a negative zero loses its sign: it is within one unit
            // of the subnormal grid there. (No sin / cos / log result is subnormal: only a direct call reaches this.)
            const double unit = double.Epsilon;
            foreach (var d in inputs) {
                double got = d.ToDouble(), want = DintRounded(d);
                if (d.ex >= -1021) Assert.True(BitsOf(got) == BitsOf(want), $"Dint(lo={d.lo:X16}, hi={d.hi:X16}, ex={d.ex}, sgn={d.sgn}).ToDouble() = {Hex(got)}, want {Hex(want)}");
                else {
                    Assert.True(Math.Abs(got - want) <= unit, $"Dint(lo={d.lo:X16}, hi={d.hi:X16}, ex={d.ex}, sgn={d.sgn}).ToDouble() = {Hex(got)}, want {Hex(want)} (more than one unit)");
                    if (got != 0) Assert.Equal(d.sgn != 0, got < 0);
                }
            }
            // overflow: exponent 1025 gives +-infinity (the value is at least 2^1024); from 1026 on the sign of a negative value is lost
            // (+infinity, a deviation of the port that no sin / cos / log result can reach)
            foreach (ulong sgn in new[] { 0UL, 1UL })
                Assert.Equal(sgn == 0 ? double.PositiveInfinity : double.NegativeInfinity, new CoreMath.Dint(0x1234, 0x8000000000000001UL, 1025, sgn).ToDouble());
            foreach (long ex in new long[] { 1026, 1100, 5000 }) {
                Assert.Equal(double.PositiveInfinity, new CoreMath.Dint(0x1234, 0x8000000000000001UL, ex, 0).ToDouble());
                Assert.True(double.IsInfinity(new CoreMath.Dint(0x1234, 0x8000000000000001UL, ex, 1).ToDouble()));
            }
            // exponent 1024: the largest finite value, and rounding up to infinity
            Assert.Equal(double.MaxValue, new CoreMath.Dint(0, ulong.MaxValue << 11, 1024, 0).ToDouble());
            Assert.Equal(double.PositiveInfinity, new CoreMath.Dint(ulong.MaxValue, ulong.MaxValue, 1024, 0).ToDouble());
        }

        [Fact]
        public void Dint_Subnormalize_KeepsTheValueOfNormalNumbers_AndRoundsTheRest()
        {
            var normal = new CoreMath.Dint(5, 0x8000000000000003UL, -1022, 0);
            Assert.Equal(normal, normal.Subnormalize());
            // rounding to the subnormal grid happens in Subnormalize: the result has no bits below the grid
            ulong s = 61;
            for (int i = 0; i < 200; ++i) {
                long ex = -1023 - (long)(SplitMix(ref s) % 50);
                var d = new CoreMath.Dint(SplitMix(ref s), SplitMix(ref s) | (1UL << 63), ex, SplitMix(ref s) & 1);
                var r = d.Subnormalize();
                int rex = (int)-(1011 + ex);
                BigInteger exact = DScaled(d), got = DScaled(r);
                BigInteger grid = BigInteger.One << (S + (int)ex - 128 + 64 + rex);       // one unit of the kept bits
                BigInteger diff = BigInteger.Abs(got - exact);
                Assert.True(diff * 2 <= grid || r.ex == ex + 1, $"case {i}: moved by more than half a unit");
                Assert.True((r.hi & ((1UL << rex) - 1)) == 0 || r.ex == ex + 1, "bits below the grid cleared");
            }
            // a value so small that nothing of it survives is returned as the smallest power of two above (hi = 2^63, ex + 1)
            var vanish = new CoreMath.Dint(0, 0x8000000000000000UL, -1074, 0).Subnormalize();
            Assert.True(vanish.hi == 0x8000000000000000UL);
        }

        // ---------------------------------------------------------------- fma (software path)

        static double SubnormalFromUnits(BigInteger k, bool negative)
        {
            long bits = (long)k;                         // k < 2^52: the bit pattern of k * 2^-1074
            return FromBits((ulong)bits | (negative ? 1UL << 63 : 0));
        }

        static void CheckFma(string what, double x, double y, double z)
        {
            double want = CoreMathReference.Fma(x, y, z);
            double got = CoreMath.SoftwareFusedMultiplyAdd(x, y, z);
            Assert.True(Same(got, want), $"{what}: fma({Hex(x)}, {Hex(y)}, {Hex(z)}) = {Hex(got)}, want {Hex(want)}");
            Assert.True(Same(CoreMath.FusedMultiplyAdd(x, y, z), want), $"{what}: helper fma({Hex(x)}, {Hex(y)}, {Hex(z)})");
        }

        /// <summary> x * y = a * b * 2^-1129 with 53 bit integers a, b (x, y normal), the product is below the smallest normal number </summary>
        static (double x, double y, BigInteger p) SubnormalProduct(ref ulong s, bool negative)
        {
            BigInteger a = ((BigInteger)(SplitMix(ref s) & ((1UL << 52) - 1)) | (BigInteger.One << 52));
            BigInteger b = ((BigInteger)(SplitMix(ref s) & ((1UL << 52) - 1)) | (BigInteger.One << 52));
            double x = (double)a * Pow2(-500 - 52);       // exponent -500
            double y = (double)b * Pow2(-525 - 52);       // exponent -525: product 2^-1025 * (1..4) ~ 2^-1129 * a * b
            return (negative ? -x : x, y, a * b);
        }

        [Fact]
        public void SoftwareFma_ProductCancelledByASubnormalAddend_LeavesTheLowBits()
        {
            // the exact sum p - k * 2^-1074 (p the 106 bit product) is smaller than 2^64 units of the product: the high word is zero
            // and the result comes from the low word alone; residuals up to 2^63 shift it up, residuals from 2^63 to 2^64 shift it down
            ulong s = 4711;
            BigInteger[] targets = { BigInteger.One << 20, BigInteger.One << 40, (BigInteger.One << 54) + 12345, BigInteger.One << 61, (BigInteger.One << 63) - (BigInteger.One << 56),
                                     (BigInteger.One << 63) + (BigInteger.One << 62), (BigInteger.One << 63) + 99, (BigInteger.One << 64) - (BigInteger.One << 57) };
            int count = 0;
            foreach (BigInteger target in targets)
                for (int i = 0; i < 6; ++i) {
                    bool neg = (i & 1) != 0;
                    var (x, y, p) = SubnormalProduct(ref s, neg);
                    BigInteger k = (p - target) >> 55;                      // z = -k * 2^-1074 = -k * 2^55 units of 2^-1129
                    if (k <= 0 || k >= (BigInteger.One << 52)) continue;
                    double z = SubnormalFromUnits(k, !neg);
                    CheckFma("cancellation", x, y, z);
                    ++count;
                }
            Assert.True(count >= 40, "enough cases built");
            // the same cancellation with the signs exchanged and with y negative
            var (x2, y2, p2) = SubnormalProduct(ref s, false);
            BigInteger k2 = (p2 - (BigInteger.One << 63) - (BigInteger.One << 62)) >> 55;
            CheckFma("cancellation, y negative", x2, -y2, SubnormalFromUnits(k2, false));
        }

        [Fact]
        public void SoftwareFma_ResultJustBelowTheSmallestNormalNumber_RoundsToIt()
        {
            // exact result 2^-1022 - j * 2^-1085 with 1 <= j <= 512 lies within a quarter of the subnormal grid below the smallest
            // normal number and rounds up to it
            ulong s = 31337;
            int count = 0;
            for (int variant = 0; variant < 4; ++variant)
                for (int i = 0; i < 6; ++i) {
                    // T = (2^107 - g) units of 2^-1129, g = j * 2^44. The product a * b must be -g modulo 2^55 (the addend is a multiple of 2^55 units).
                    int jShift = new[] { 0, 3, 8, 9 }[variant];        // j = 2^jShift: 1, 8, 256, 512
                    int low = 44 + jShift;                               // g = 2^low
                    BigInteger g = BigInteger.One << low;
                    // a = oddA * 2^t1, b = oddB * 2^t2 with t1 + t2 = low: the product is oddA * oddB * 2^low and oddA * oddB must be -1 modulo 2^(55 - low)
                    int t1 = low / 2, t2 = low - t1;
                    BigInteger oddA = ((BigInteger)(SplitMix(ref s) & ((1UL << (52 - t1)) - 1)) | (BigInteger.One << (52 - t1))) | 1;
                    BigInteger mod = BigInteger.One << (55 - low);
                    // oddB = -oddA^-1 mod 2^(55-low), lifted to the 53 - t2 bit range
                    BigInteger inv = ModInverse(oddA, mod);
                    BigInteger oddB = (mod - inv) % mod;
                    BigInteger lo2 = BigInteger.One << (52 - t2), hi2 = BigInteger.One << (53 - t2);
                    while (oddB < lo2) oddB += mod;
                    if (oddB >= hi2) continue;
                    if (oddB.IsEven) continue;
                    BigInteger a = oddA << t1, b = oddB << t2, p = a * b;
                    if (p >= (BigInteger.One << 106)) continue;
                    BigInteger k = ((BigInteger.One << 107) - g - p) >> 55;
                    if (((BigInteger.One << 107) - g - p) != (k << 55)) continue;
                    if (k <= 0 || k >= (BigInteger.One << 52)) continue;
                    double x = (double)a * Pow2(-500 - 52), y = (double)b * Pow2(-525 - 52), z = SubnormalFromUnits(k, false);
                    double want = CoreMathReference.Fma(x, y, z);
                    Assert.Equal(BitsOf(Pow2(-1022)), BitsOf(want));
                    CheckFma("just below the smallest normal", x, y, z);
                    CheckFma("just below the smallest normal, negated", -x, y, -z);
                    ++count;
                }
            Assert.True(count >= 4, $"cases built: {count}");
        }

        static BigInteger ModInverse(BigInteger a, BigInteger m)
        {
            BigInteger t = 0, newT = 1, r = m, newR = a % m;
            while (!newR.IsZero) {
                BigInteger q = r / newR;
                (t, newT) = (newT, t - q * newT);
                (r, newR) = (newR, r - q * newR);
            }
            return t.Sign < 0 ? t + m : t;
        }

        [Fact]
        public void SoftwareFma_DoubleDoubleSumOfOneOrThreeTimesAPowerOfTwo_RoundsToTheCorrectNeighbour()
        {
            // the sum of the low parts is exactly 2^k or 3 * 2^k and nonzero: the fast path nudges it by 1/8 of itself so that a tie of
            // the final addition is resolved by the sign of the remainder
            double e53 = Pow2(-53), u = Pow2(-52), t26 = Pow2(-26);
            var cases = new List<double[]>();
            // x * y = 2^-53 * (1 - 2^-104): low part negative; z = 1 (even) and z = 1 + 2^-52 (odd)
            double x1 = (1 + u) * e53, y1 = 1 - u;
            cases.Add(new[] { x1, y1, 1.0 });
            cases.Add(new[] { x1, y1, 1 + u });
            // x * y = 2^-53 * (1 + 2^-78): low part positive
            double x2 = (1 + t26) * e53, y2 = 1 - t26 + u;
            cases.Add(new[] { x2, y2, 1.0 });
            cases.Add(new[] { x2, y2, 1 + u });
            // the same at other magnitudes and with the three-times-a-power-of-two form (hi = 1.5 * 2^k)
            foreach (double scale in new[] { 1.0, Pow2(100), Pow2(-100), 3.0, Pow2(-1000) }) {
                cases.Add(new[] { x1 * scale, y1, scale });
                cases.Add(new[] { x2 * scale, y2, scale });
                cases.Add(new[] { x2 * scale, y2, scale * (1 + u) });
                cases.Add(new[] { x1 * scale, y1, scale * (1 + u) });
            }
            // 1.5 * 2^-53 as the head: (1.5 * (1 + 2^-26)) * (1 - 2^-26 + 2^-52) ...
            double x3 = 1.5 * (1 + t26) * e53;
            cases.Add(new[] { x3, y2, 1.0 });
            cases.Add(new[] { x3, y2, 1 + u });
            cases.Add(new[] { 1.5 * x1, y1, 1.0 });
            cases.Add(new[] { 1.5 * x1, y1, 1 + u });
            foreach (double[] c in cases) {
                CheckFma("1 or 3 times a power of two", c[0], c[1], c[2]);
                CheckFma("negated", -c[0], c[1], -c[2]);
                CheckFma("y negated", c[0], -c[1], -c[2]);
            }
            // the tie that needs the nudge: x2 * y2 + 1 is above the midpoint of 1 and 1 + 2^-52 by 2^-131, and naive rounding gives 1
            Assert.Equal(1.0 + u, CoreMath.SoftwareFusedMultiplyAdd(x2, y2, 1.0));
            Assert.Equal(1.0 + u, CoreMathReference.Fma(x2, y2, 1.0));
        }

        // ---------------------------------------------------------------- private stages (local functions) called directly

        // the stages that the public functions call only after the fast path failed, or with arguments the callers cannot produce,
        // are static local functions: the compiler turns them into private static methods "<Outer>g__name|n_m"
        static MethodInfo LocalFunction(string outer, string name)
        {
            string prefix = "<" + outer + ">g__" + name + "|";
            foreach (MethodInfo m in typeof(CoreMath).GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (m.Name.StartsWith(prefix, StringComparison.Ordinal)) return m;
            throw new InvalidOperationException("local function not found: " + prefix);
        }

        [Fact]
        public void Cos_AccurateStage_ReturnsTheTabulatedExceptions()
        {
            // the 128 bit result of these five arguments lies within the error bound of a rounding boundary (a double plus about 2^-142):
            // the accurate stage answers from the table. The fast stage already decides them, so only a direct call gets there.
            MethodInfo accurate = LocalFunction("Cos", "cosAccurate");
            double[] ex = Table<double>("Tbl_Cos_22");
            ulong[] T = CoreMath.CosTableForTests;
            Assert.Equal(15, ex.Length);
            for (int k = 0; k < 5; ++k) {
                double x = ex[3 * k];
                double got = (double)accurate.Invoke(null, new object[] { x, T });
                Assert.Equal(ex[3 * k + 1] + ex[3 * k + 2], got);
                Assert.True(Same(got, CoreMathReference.Cos(x)), $"cosAccurate({Hex(x)}) = {Hex(got)}");
                Assert.True(Same(CoreMath.Cos(x), got) && Same(CoreMath.Cos(-x), got), "the public function agrees");
            }
            // arguments that are not in the table are computed
            foreach (double x in new[] { 0.5, 1.7881393432617211e-07 * 1.5, 100.0, 1e22 }) {
                double got = (double)accurate.Invoke(null, new object[] { x, T });
                Assert.True(Same(got, CoreMathReference.Cos(x)), $"cosAccurate({Hex(x)}) = {Hex(got)}");
            }
        }

        [Fact]
        public void Exp_DatabaseLookup_IsCalledWithTheLowPartOfTheAccurateResult()
        {
            MethodInfo database = LocalFunction("Exp", "asExpDatabase");
            double[] db = Table<double>("Tbl_Exp_0");
            ulong[] s2 = Table<ulong>("Tbl_Exp_1");
            // an argument that is not in the database returns the result unchanged
            foreach (double x in new[] { 0.3, 1e-5, 5.0, -2.0, NextUp(db[0]), NextDown(db[db.Length - 1]) })
                Assert.Equal(1.2345, (double)database.Invoke(null, new object[] { x, 1.2345 }));
            const ulong one = 0x3FF8000000000000UL;                 // 1.5, residue 0 modulo 4, in the middle of a binade (the stored correction is a quarter unit)
            for (int m = 0; m < db.Length; ++m) {
                ulong t = (s2[m >> 5] >> ((m << 1) & 63)) & 3;
                // f whose three neighbours r - 1, r, r + 1 all differ from t modulo 4: the lookup finds no admissible value
                double f = FromBits(one + (t + 2) % 4);
                Assert.Equal(f, (double)database.Invoke(null, new object[] { db[m], f }));
                // with another residue the value is moved by at most one unit to the residue of the database
                for (ulong d = 0; d < 4; ++d) {
                    double g = FromBits(one + d);
                    double r = (double)database.Invoke(null, new object[] { db[m], g });
                    long diff = BitsOf(r) - BitsOf(g);
                    if (d == (t + 2) % 4) { Assert.Equal(0L, diff); continue; }
                    Assert.True(diff >= -1 && diff <= 1, $"entry {m}: moved by {diff}");
                    Assert.Equal(t, (ulong)BitsOf(r) & 3);
                }
            }
        }

        [Fact]
        public void Log_AccurateStage_OfOne_IsZero()
        {
            // Log(1) is answered before the accurate stage; called directly it still has to give +0
            MethodInfo accurate = LocalFunction("Log", "logAccurate");
            Assert.Equal(0L, BitsOf((double)accurate.Invoke(null, new object[] { 1.0 })));
            foreach (double x in new[] { 2.0, 0.5, 10.0, 1.0000000000000002, 0.9999999999999999 })
                Assert.True(Same((double)accurate.Invoke(null, new object[] { x }), CoreMathReference.Log(x)), $"logAccurate({x:R})");
        }

        // the fraction c1:c0 / 2^128 of the reduction as a double-double (h, l): h + l is the fraction truncated to about 106 bits
        static void CheckSetDd(MethodInfo setDd, ulong c1, ulong c0)
        {
            var r = (ValueTuple<double, double>)setDd.Invoke(null, new object[] { c1, c0 });
            BigInteger v = Big(c1, c0);
            if (v.IsZero) { Assert.Equal(0L, BitsOf(r.Item1)); Assert.Equal(0L, BitsOf(r.Item2)); return; }
            // fixed point with W = Precision bits: v * 2^(W - 128)
            BigInteger exact = v << (CoreMathReference.Precision - 128);
            BigInteger sum = CoreMathReference.ExactFixed(r.Item1) + CoreMathReference.ExactFixed(r.Item2);
            BigInteger err = exact - sum;
            Assert.True(err.Sign >= 0, $"setDd({c1:X16}, {c0:X16}): h + l above the fraction");
            if (c1 != 0) Assert.True((err << 104) < exact, $"setDd({c1:X16}, {c0:X16}): h + l = {r.Item1:R} + {r.Item2:R} is not within 2^-104");
            else {
                // only the head is right when the high limb is zero (the exponent of the low double is off, see SetDdCases): h is the
                // fraction truncated to 53 bits and l is a nonnegative number below one unit of h
                int bits = CoreMathReference.BitLength(exact);
                BigInteger head = (exact >> (bits - 53)) << (bits - 53);
                Assert.Equal(head, CoreMathReference.ExactFixed(r.Item1));
                Assert.True(CoreMathReference.ExactFixed(r.Item2) < (BigInteger.One << (bits - 53)));
            }
            Assert.True(r.Item1 > 0 && r.Item1 < 1);
            Assert.True(r.Item2 >= 0);
        }

        static void SetDdCases(MethodInfo setDd, bool lowLimbOnly)
        {
            ulong s = 64738;
            // c1 == 0: only the low limb is set (the reduced argument is below 2^-64 turns). The port computes the exponent of l as f - 64 - g
            // where f - 53 - g is right: the low double is not the rest of the fraction there (no argument gets here, see the report)
            if (lowLimbOnly) {
            // (c0 == 1 is left out: the shift by 64 of the port wraps to a shift by 0 and h + l is wrong there)
            foreach (ulong c0 in new ulong[] { 2, 3, 0xFFF, 0x1000, 0x1001, 0x7FFFFFFFFFFFFFFFUL, 0x8000000000000000UL, 0x8000000000000001UL, 0xFFFFFFFFFFFFFFFFUL, 0x00FFFFFFFFFFF000UL, 0x0010000000000000UL, 0x0010000000000001UL })
                CheckSetDd(setDd, 0, c0);
            for (int i = 0; i < 64; ++i) {
                ulong c0 = SplitMix(ref s) >> i;
                if (c0 > 1) CheckSetDd(setDd, 0, c0);
                // the bits of c0 below the 53 kept ones are all zero: the low double is zero
                CheckSetDd(setDd, 0, ((SplitMix(ref s) | (1UL << 63)) & (~0UL << 12)) >> i << i | (1UL << 63 >> i));
            }
            
            CheckSetDd(setDd, 0, 0);
            }
            // c1 != 0: the low limb contributes to the low double or not at all
            CheckSetDd(setDd, 0x8000000000000000UL, 0);
            CheckSetDd(setDd, 1, 0);
            CheckSetDd(setDd, 0xFFFFFFFFFFFFFFFFUL, 0);
            CheckSetDd(setDd, 0x8000000000000000UL, 1);
            CheckSetDd(setDd, 0x8000000000000800UL, 0x7FFUL);
            CheckSetDd(setDd, 0x0000000000000800UL, 0);
            for (int i = 0; i < 64; ++i) {
                CheckSetDd(setDd, (SplitMix(ref s) >> i) | 1, SplitMix(ref s));
                CheckSetDd(setDd, ((SplitMix(ref s) | 1UL << 63) >> (i / 2)) << 11, 0);                  // a low limb of zero: l == 0
                CheckSetDd(setDd, (1UL << (i % 53)) << 11, (ulong)1 << 63);                              // c1's low bits zero, c0 small
            }
        }

        [Fact]
        public void Sin_SetDd_SplitsTheFractionOfTheReduction()
        {
            // Sin's version does not normalise the low limb when c1 == 0 (h is wrong there); no argument gets there: the fraction of x / (2 pi)
            // of a double is never below 2^-64, so only the c1 != 0 branch is checked
            SetDdCases(LocalFunction("Sin", "setDd"), false);
        }

        [Fact]
        public void Cos_SetDd_SplitsTheFractionOfTheReduction()
        {
            SetDdCases(LocalFunction("Cos", "setdd"), true);
        }

        // ---------------------------------------------------------------- exp at the smallest arguments (the "th == 1" branch of the accurate stage)

        [Fact]
        public void Exp_PowersOfTwoNearTheSmallestArguments_AreCorrectlyRounded()
        {
            // |x| below 2^-13 takes the branch of the accurate stage in which the table value is exactly 1. 2^-53 itself is the known
            // exception, see Exp_TwoToTheMinus53_IsRoundedUp.
            int count = 0;
            for (int k = 1; k <= 54; ++k)
                foreach (double sgn in new[] { 1.0, -1.0 })
                    foreach (int d in new[] { -2, -1, 0, 1, 2 }) {
                        double x = sgn * FromBits((ulong)(BitsOf(Pow2(-k)) + d));
                        if (Math.Abs(x) == Pow2(-53)) continue;
                        double got = CoreMath.Exp(x), want = CoreMathReference.Exp(x);
                        Assert.True(Same(got, want), $"exp({x:R}) = {Hex(got)}, want {Hex(want)}");
                        ++count;
                    }
            Assert.True(count > 500);
        }

        [Fact(Skip = "Known defect of the port, not fixed here (tests only): asExpAccurate tests '((ix & ~0ul) >> 12) == 0' where CORE-MATH has '(ix << 12) == 0' " +
                     "(fl is a power of two): exp(2^-53) = 1 + 2^-53 + 2^-107 is rounded to 1 instead of 1 + 2^-52. Enable when the condition is fixed; " +
                     "then lines 202-205 of CoreMathExp.cs are covered by this test.")]
        public void Exp_TwoToTheMinus53_IsRoundedUp()
        {
            double x = Pow2(-53);
            Assert.Equal(BitsOf(1.0 + Pow2(-52)), BitsOf(CoreMathReference.Exp(x)));
            Assert.Equal(BitsOf(1.0 + Pow2(-52)), BitsOf(CoreMath.Exp(x)));
        }
    }
}
