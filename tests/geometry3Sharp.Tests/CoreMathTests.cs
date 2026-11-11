// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Tests of the vendored CORE-MATH port (math/CoreMath): exp, log, sin, cos, atan2 must be correctly rounded, i.e. equal
// to the exact result rounded to nearest even, on every runtime. The expected values come from CoreMathReference
// (BigInteger arithmetic, no shared code), the hard cases from the worst-case lists of CORE-MATH (CoreMathHardCases).
using System;
using System.Collections.Generic;
using System.Text;
using g3.CoreMathPort;
using Xunit;

namespace g3.Tests
{
    public class CoreMathTests
    {
        // ---------------------------------------------------------------- inputs

        static ulong SplitMix(ref ulong s)
        {
            ulong z = (s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static double Uniform(ref ulong s, double lo, double hi)
        {
            return lo + (hi - lo) * ((SplitMix(ref s) >> 11) * (1.0 / 9007199254740992.0));
        }

        static double RandomFinite(ref ulong s)
        {
            while (true) {
                double d = BitConverter.Int64BitsToDouble((long)SplitMix(ref s));
                if (!double.IsNaN(d) && !double.IsInfinity(d)) return d;
            }
        }

        static double FromBits(ulong b) { return BitConverter.Int64BitsToDouble((long)b); }

        static string Bits(double d) { return BitConverter.DoubleToInt64Bits(d).ToString("X16"); }

        static double[] ExpArgs(int n, ulong seed)
        {
            var r = new List<double>();
            ulong s = seed;
            for (int i = 0; i < n; ++i) {
                switch (i % 4) {
                    case 0: r.Add(Uniform(ref s, -745.2, 709.8)); break;
                    case 1: r.Add(Uniform(ref s, -2, 2)); break;
                    case 2: r.Add(Uniform(ref s, -1e-3, 1e-3)); break;
                    default: r.Add(Uniform(ref s, -746, -700)); break;      // subnormal results
                }
            }
            return r.ToArray();
        }

        static double[] LogArgs(int n, ulong seed)
        {
            var r = new List<double>();
            ulong s = seed;
            for (int i = 0; i < n; ++i) {
                switch (i % 4) {
                    case 0: r.Add(Math.Abs(RandomFinite(ref s))); break;                       // any exponent, subnormals
                    case 1: r.Add(Uniform(ref s, 0.5, 2.0)); break;
                    case 2: r.Add(1.0 + (double)(long)(SplitMix(ref s) % 4001 - 2000) * Math.Pow(2, -52)); break;   // around 1
                    default: r.Add(Uniform(ref s, 1e-3, 1e3)); break;
                }
            }
            return r.ToArray();
        }

        static double[] TrigArgs(int n, ulong seed)
        {
            var r = new List<double>();
            ulong s = seed;
            for (int i = 0; i < n; ++i) {
                switch (i % 4) {
                    case 0: r.Add(Uniform(ref s, -50, 50)); break;
                    case 1: r.Add(Uniform(ref s, -1e-3, 1e-3)); break;
                    case 2: r.Add(RandomFinite(ref s)); break;                                 // any exponent up to 2^1023
                    default: r.Add(Uniform(ref s, -1e6, 1e6)); break;
                }
            }
            return r.ToArray();
        }

        // ---------------------------------------------------------------- check helpers

        static void CheckAll(string name, Func<double, double> f, Func<double, double> reference, IEnumerable<double> args)
        {
            var bad = new StringBuilder();
            int count = 0, mismatches = 0;
            foreach (double x in args) {
                ++count;
                double got = f(x), want = reference(x);
                bool same = BitConverter.DoubleToInt64Bits(got) == BitConverter.DoubleToInt64Bits(want) || (double.IsNaN(got) && double.IsNaN(want));
                if (!same) {
                    ++mismatches;
                    if (mismatches <= 5) bad.AppendLine($"{name}({Bits(x)} = {x:R}): got {Bits(got)} ({got:R}), want {Bits(want)} ({want:R})");
                }
            }
            Assert.True(mismatches == 0, $"{name}: {mismatches} of {count} results not correctly rounded\n{bad}");
        }

        static IEnumerable<double> FromBitList(ulong[] bits)
        {
            foreach (ulong b in bits) yield return FromBits(b);
        }

        // ---------------------------------------------------------------- exp, log, sin, cos

        [Fact]
        public void Exp_IsCorrectlyRounded()
        {
            CheckAll("exp", CoreMath.Exp, CoreMathReference.Exp, ExpArgs(600, 1));
        }

        [Fact]
        public void Exp_HardCases_AreCorrectlyRounded()
        {
            CheckAll("exp", CoreMath.Exp, CoreMathReference.Exp, FromBitList(CoreMathHardCases.Exp));
        }

        [Fact]
        public void Log_IsCorrectlyRounded()
        {
            CheckAll("log", CoreMath.Log, CoreMathReference.Log, LogArgs(600, 2));
        }

        [Fact]
        public void Log_HardCases_AreCorrectlyRounded()
        {
            CheckAll("log", CoreMath.Log, CoreMathReference.Log, FromBitList(CoreMathHardCases.Log));
        }

        [Fact]
        public void Sin_IsCorrectlyRounded()
        {
            CheckAll("sin", CoreMath.Sin, CoreMathReference.Sin, TrigArgs(600, 3));
        }

        [Fact]
        public void Sin_HardCases_AreCorrectlyRounded()
        {
            CheckAll("sin", CoreMath.Sin, CoreMathReference.Sin, FromBitList(CoreMathHardCases.Sin));
        }

        [Fact]
        public void Cos_IsCorrectlyRounded()
        {
            CheckAll("cos", CoreMath.Cos, CoreMathReference.Cos, TrigArgs(600, 4));
        }

        [Fact]
        public void Cos_HardCases_AreCorrectlyRounded()
        {
            CheckAll("cos", CoreMath.Cos, CoreMathReference.Cos, FromBitList(CoreMathHardCases.Cos));
        }

        [Fact]
        public void Cos_LargeArguments_AccuratePath_IsCorrectlyRounded()
        {
            // two worst-case arguments of the CORE-MATH list whose result lies only 2^-54 ulp from a rounding boundary: the
            // accurate path of the port was off by 2^-105 (a doubled polynomial term, see Mul21_IsTheProduct...)
            CheckAll("cos", CoreMath.Cos, CoreMathReference.Cos, FromBitList(new ulong[] { 0x7A46B7B71E8AE7C4UL, 0x4901111B0BEB4E46UL }));
        }

        // the reduction of large arguments has a branch per alignment of the exponent with the 64-bit limbs of 1/(2 pi) (shifts
        // by 0, 1, 63, 64, 65 bits, carries across the limbs): sweep the exponents around every multiple of 64
        static double[] ExponentSweep()
        {
            var list = new List<double>();
            ulong[] mantissas = { 0x0000000000000UL, 0x5555555555555UL, 0xFFFFFFFFFFFFFUL, 0x8000000000001UL };
            var exps = new SortedSet<int>();
            for (int e = 1023; e <= 1060; ++e) exps.Add(e);                       // 1 .. 2^37, around the switch to the large path at 2^31
            for (int k = 1; k <= 16; ++k)
                for (int d = -2; d <= 2; ++d) { int e = 1022 + 64 * k + d; if (e >= 1023 && e <= 2046) exps.Add(e); }
            exps.Add(2046);
            foreach (int e in exps)
                foreach (ulong m in mantissas) {
                    list.Add(FromBits(((ulong)e << 52) | m));
                    list.Add(FromBits((1UL << 63) | ((ulong)e << 52) | m));
                }
            return list.ToArray();
        }

        [Fact]
        public void Sin_ExponentSweep_IsCorrectlyRounded()
        {
            CheckAll("sin", CoreMath.Sin, CoreMathReference.Sin, ExponentSweep());
        }

        [Fact]
        public void Cos_ExponentSweep_IsCorrectlyRounded()
        {
            CheckAll("cos", CoreMath.Cos, CoreMathReference.Cos, ExponentSweep());
        }

        // ---------------------------------------------------------------- atan2

        static void CheckAtan2(IEnumerable<KeyValuePair<double, double>> pairs)
        {
            var bad = new StringBuilder();
            int count = 0, mismatches = 0;
            foreach (var p in pairs) {
                ++count;
                double got = CoreMath.Atan2(p.Key, p.Value), want = CoreMathReference.Atan2(p.Key, p.Value);
                bool same = BitConverter.DoubleToInt64Bits(got) == BitConverter.DoubleToInt64Bits(want) || (double.IsNaN(got) && double.IsNaN(want));
                if (!same) {
                    ++mismatches;
                    if (mismatches <= 5) bad.AppendLine($"atan2({Bits(p.Key)}, {Bits(p.Value)}): got {Bits(got)} ({got:R}), want {Bits(want)} ({want:R})");
                }
            }
            Assert.True(mismatches == 0, $"atan2: {mismatches} of {count} results not correctly rounded\n{bad}");
        }

        [Fact]
        public void Atan2_IsCorrectlyRounded()
        {
            var pairs = new List<KeyValuePair<double, double>>();
            ulong s = 5;
            for (int i = 0; i < 600; ++i) {
                switch (i % 4) {
                    case 0: pairs.Add(new KeyValuePair<double, double>(Uniform(ref s, -10, 10), Uniform(ref s, -10, 10))); break;
                    case 1: pairs.Add(new KeyValuePair<double, double>(RandomFinite(ref s), RandomFinite(ref s))); break;     // any exponents
                    case 2: pairs.Add(new KeyValuePair<double, double>(Uniform(ref s, -1e-5, 1e-5), Uniform(ref s, 0.5, 2))); break;
                    default: pairs.Add(new KeyValuePair<double, double>(Uniform(ref s, 1, 100), Uniform(ref s, -1e-3, 1e-3))); break;
                }
            }
            CheckAtan2(pairs);
        }

        [Fact]
        public void Atan2_HardCases_AreCorrectlyRounded()
        {
            var pairs = new List<KeyValuePair<double, double>>();
            ulong[] a = CoreMathHardCases.Atan2;
            for (int i = 0; i < a.Length; i += 2) pairs.Add(new KeyValuePair<double, double>(FromBits(a[i]), FromBits(a[i + 1])));
            CheckAtan2(pairs);
        }

        [Fact]
        public void Atan2_ExponentDifferenceSweep_IsCorrectlyRounded()
        {
            // the 192-bit significand shifts by the exponent difference of the operands: 0, 1, 63, 64, 65, 127, 128, 129, 191, 192, 193
            var pairs = new List<KeyValuePair<double, double>>();
            ulong[] mantissas = { 0x0000000000000UL, 0x5555555555555UL, 0xFFFFFFFFFFFFFUL };
            int[] diffs = { 0, 1, 2, 53, 54, 63, 64, 65, 127, 128, 129, 191, 192, 193 };
            foreach (int baseExp in new[] { -300, -30, 0, 20, 400 })
                foreach (int d in diffs)
                    foreach (int sign in new[] { 1, -1 })
                        foreach (ulong m in mantissas)
                            foreach (int sy in new[] { 0, 1 })
                                foreach (int sx in new[] { 0, 1 }) {
                                    int ex = baseExp, ey = baseExp + sign * d;
                                    double x = FromBits(((ulong)sx << 63) | ((ulong)(ex + 1023) << 52) | m);
                                    double y = FromBits(((ulong)sy << 63) | ((ulong)(ey + 1023) << 52) | (0xFFFFFFFFFFFFFUL - m));
                                    pairs.Add(new KeyValuePair<double, double>(y, x));
                                }
            CheckAtan2(pairs);
        }

        [Fact]
        public void Atan2_SpecialValues_FollowC99()
        {
            double inf = double.PositiveInfinity, nan = double.NaN;
            double pi = Math.PI, pi2 = Math.PI / 2, pi4 = Math.PI / 4, pi34 = FromBits(0x4002D97C7F3321D2UL);
            var cases = new[] {
                new[] { 0.0, 1.0, 0.0 }, new[] { -0.0, 1.0, -0.0 }, new[] { 0.0, -1.0, pi }, new[] { -0.0, -1.0, -pi },
                new[] { 0.0, 0.0, 0.0 }, new[] { -0.0, 0.0, -0.0 }, new[] { 0.0, -0.0, pi }, new[] { -0.0, -0.0, -pi },
                new[] { 1.0, 0.0, pi2 }, new[] { -1.0, 0.0, -pi2 }, new[] { 1.0, -0.0, pi2 }, new[] { -1.0, -0.0, -pi2 },
                new[] { 1.0, inf, 0.0 }, new[] { -1.0, inf, -0.0 }, new[] { 1.0, -inf, pi }, new[] { -1.0, -inf, -pi },
                new[] { inf, 1.0, pi2 }, new[] { -inf, 1.0, -pi2 }, new[] { inf, -1.0, pi2 }, new[] { -inf, -1.0, -pi2 },
                new[] { inf, inf, pi4 }, new[] { -inf, inf, -pi4 }, new[] { inf, -inf, pi34 }, new[] { -inf, -inf, -pi34 },
            };
            foreach (var c in cases) {
                double got = CoreMath.Atan2(c[0], c[1]);
                Assert.True(BitConverter.DoubleToInt64Bits(got) == BitConverter.DoubleToInt64Bits(c[2]), $"atan2({c[0]}, {c[1]}) = {Bits(got)}, want {Bits(c[2])}");
                Assert.True(BitConverter.DoubleToInt64Bits(CoreMathReference.Atan2(c[0], c[1])) == BitConverter.DoubleToInt64Bits(c[2]), "reference");
            }
            Assert.True(double.IsNaN(CoreMath.Atan2(nan, 1)));
            Assert.True(double.IsNaN(CoreMath.Atan2(1, nan)));
            Assert.True(double.IsNaN(CoreMath.Atan2(nan, nan)));
        }

        [Fact]
        public void Atan2_Subnormals_AndExtremeRatios_AreCorrectlyRounded()
        {
            double min = FromBits(1), max = double.MaxValue;
            var pairs = new List<KeyValuePair<double, double>>();
            foreach (double y in new[] { min, -min, 3 * min, max, -max, 1e-310, 1.0 })
                foreach (double x in new[] { min, -min, 1.0, -1.0, max, -max, 1e-310, 7 * min })
                    pairs.Add(new KeyValuePair<double, double>(y, x));
            CheckAtan2(pairs);
        }

        // ---------------------------------------------------------------- special values of the unary functions

        [Fact]
        public void Exp_Log_Sin_Cos_SpecialValues()
        {
            double inf = double.PositiveInfinity, nan = double.NaN;
            Assert.Equal(1.0, CoreMath.Exp(0.0)); Assert.Equal(1.0, CoreMath.Exp(-0.0));
            Assert.Equal(inf, CoreMath.Exp(inf)); Assert.Equal(0.0, CoreMath.Exp(-inf)); Assert.True(double.IsNaN(CoreMath.Exp(nan)));
            Assert.Equal(inf, CoreMath.Exp(710)); Assert.Equal(0.0, CoreMath.Exp(-746)); Assert.Equal(0.0, CoreMath.Exp(-1e300));
            Assert.Equal(double.NegativeInfinity, CoreMath.Log(0.0)); Assert.Equal(double.NegativeInfinity, CoreMath.Log(-0.0));
            Assert.Equal(inf, CoreMath.Log(inf)); Assert.True(double.IsNaN(CoreMath.Log(-1.0))); Assert.True(double.IsNaN(CoreMath.Log(nan)));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(CoreMath.Log(1.0)));
            Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(CoreMath.Sin(-0.0)));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(CoreMath.Sin(0.0)));
            Assert.Equal(1.0, CoreMath.Cos(0.0)); Assert.Equal(1.0, CoreMath.Cos(-0.0));
            Assert.True(double.IsNaN(CoreMath.Sin(inf))); Assert.True(double.IsNaN(CoreMath.Sin(-inf))); Assert.True(double.IsNaN(CoreMath.Sin(nan)));
            Assert.True(double.IsNaN(CoreMath.Cos(inf))); Assert.True(double.IsNaN(CoreMath.Cos(nan)));
            double sub = FromBits(5);
            Assert.Equal(Bits(sub), Bits(CoreMath.Sin(sub)));       // sin(x) = x for tiny x
            Assert.Equal(Bits(-sub), Bits(CoreMath.Sin(-sub)));
            Assert.Equal(1.0, CoreMath.Cos(sub));
            Assert.Equal(Bits(CoreMathReference.Log(sub)), Bits(CoreMath.Log(sub)));
            Assert.Equal(Bits(CoreMathReference.Exp(-745.13)), Bits(CoreMath.Exp(-745.13)));
            Assert.Equal(Bits(CoreMathReference.Exp(709.782712893384)), Bits(CoreMath.Exp(709.782712893384)));   // the overflow threshold
        }
    }
}
