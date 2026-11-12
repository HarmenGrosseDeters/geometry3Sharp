// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The fused multiply-add of the vendored CORE-MATH port: the software version (used on .NET Framework / netstandard2.0), the
// helper CoreMath.FusedMultiplyAdd (Math.FusedMultiplyAdd on .NET Core 3.0 and later, hardware where it exists) and an exact
// BigInteger reference must give the same bits on random and on hard cases: cancellation, ties, subnormal and overflowing
// products, zeros, infinities, NaN.
using System;
using System.Collections.Generic;
using System.Text;
using g3.CoreMathPort;
using Xunit;

namespace g3.Tests
{
    public class CoreMathFmaTests
    {
        static ulong SplitMix(ref ulong s)
        {
            ulong z = (s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static double D(ulong bits) { return BitConverter.Int64BitsToDouble((long)bits); }

        // a double with the given unbiased exponent and random mantissa
        static double WithExponent(ref ulong s, int exp, bool sign)
        {
            ulong biased = (ulong)(exp + 1023);
            if (exp < -1022) return D((sign ? 1UL << 63 : 0) | (SplitMix(ref s) & ((1UL << (52 - Math.Min(52, -1022 - exp))) - 1)));   // subnormal
            return D((sign ? 1UL << 63 : 0) | (biased << 52) | (SplitMix(ref s) & ((1UL << 52) - 1)));
        }

        static List<double[]> Cases(int n, ulong seed)
        {
            var r = new List<double[]>();
            ulong s = seed;
            for (int i = 0; i < n; ++i) {
                bool sx = (SplitMix(ref s) & 1) != 0, sy = (SplitMix(ref s) & 1) != 0, sz = (SplitMix(ref s) & 1) != 0;
                switch (i % 8) {
                    case 0: // moderate exponents
                        r.Add(new[] { WithExponent(ref s, (int)(SplitMix(ref s) % 60) - 30, sx), WithExponent(ref s, (int)(SplitMix(ref s) % 60) - 30, sy), WithExponent(ref s, (int)(SplitMix(ref s) % 120) - 60, sz) });
                        break;
                    case 1: { // massive cancellation: z = -(x*y) rounded, plus a few ulps
                        double x = WithExponent(ref s, (int)(SplitMix(ref s) % 40) - 20, sx), y = WithExponent(ref s, (int)(SplitMix(ref s) % 40) - 20, sy);
                        double p = x * y;
                        long k = (long)(SplitMix(ref s) % 7) - 3;
                        double z = -D((ulong)((long)BitConverter.DoubleToInt64Bits(p) + k));
                        r.Add(new[] { x, y, z });
                        break;
                    }
                    case 2: // product in the subnormal range
                        r.Add(new[] { WithExponent(ref s, -600 - (int)(SplitMix(ref s) % 300), sx), WithExponent(ref s, -400 + (int)(SplitMix(ref s) % 200) - 100, sy), WithExponent(ref s, -1080 + (int)(SplitMix(ref s) % 60), sz) });
                        break;
                    case 3: // product near overflow
                        r.Add(new[] { WithExponent(ref s, 500 + (int)(SplitMix(ref s) % 23), sx), WithExponent(ref s, 500 + (int)(SplitMix(ref s) % 23), sy), WithExponent(ref s, 1000 + (int)(SplitMix(ref s) % 24), sz) });
                        break;
                    case 4: { // z half an ulp of the exact product: a tie (or just off it)
                        double x = WithExponent(ref s, (int)(SplitMix(ref s) % 20) - 10, sx), y = WithExponent(ref s, (int)(SplitMix(ref s) % 20) - 10, sy);
                        double p = x * y;
                        double ulp = Math.Abs(D((ulong)(BitConverter.DoubleToInt64Bits(Math.Abs(p)) + 1)) - Math.Abs(p));
                        double z = (SplitMix(ref s) & 1) == 0 ? ulp / 2 * (sz ? -1 : 1) : ulp / 4 * (sz ? -1 : 1);
                        r.Add(new[] { x, y, z });
                        break;
                    }
                    case 5: // tiny z against a large product (sticky bit)
                        r.Add(new[] { WithExponent(ref s, (int)(SplitMix(ref s) % 20), sx), WithExponent(ref s, (int)(SplitMix(ref s) % 20), sy), WithExponent(ref s, -200 + (int)(SplitMix(ref s) % 100), sz) });
                        break;
                    case 6: // z large against a tiny product
                        r.Add(new[] { WithExponent(ref s, -(int)(SplitMix(ref s) % 200) - 100, sx), WithExponent(ref s, -(int)(SplitMix(ref s) % 200) - 100, sy), WithExponent(ref s, (int)(SplitMix(ref s) % 50), sz) });
                        break;
                    default: // any bit pattern (infinities and NaN included)
                        r.Add(new[] { D(SplitMix(ref s)), D(SplitMix(ref s)), D(SplitMix(ref s)) });
                        break;
                }
            }
            return r;
        }

        static bool Same(double a, double b)
        {
            return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b) || (double.IsNaN(a) && double.IsNaN(b));
        }

        static string B(double d) { return BitConverter.DoubleToInt64Bits(d).ToString("X16"); }

        [Fact]
        public void SoftwareFma_EqualsTheExactReference()
        {
            var bad = new StringBuilder();
            int mismatches = 0, count = 0;
            foreach (double[] c in Cases(20000, 1)) {
                ++count;
                double got = CoreMath.SoftwareFusedMultiplyAdd(c[0], c[1], c[2]), want = CoreMathReference.Fma(c[0], c[1], c[2]);
                if (!Same(got, want)) { ++mismatches; if (mismatches <= 5) bad.AppendLine($"fma({B(c[0])}, {B(c[1])}, {B(c[2])}) = {B(got)}, want {B(want)}"); }
            }
            Assert.True(mismatches == 0, $"{mismatches} of {count}\n{bad}");
        }

        [Fact]
        public void FusedMultiplyAddHelper_EqualsTheSoftwareVersion()
        {
            // on .NET Core 3.0 and later the helper is Math.FusedMultiplyAdd (a hardware instruction where there is one):
            // a correctly rounded fma has one result, so hardware and software must agree
            var bad = new StringBuilder();
            int mismatches = 0, count = 0;
            foreach (double[] c in Cases(20000, 2)) {
                ++count;
                double got = CoreMath.FusedMultiplyAdd(c[0], c[1], c[2]), want = CoreMath.SoftwareFusedMultiplyAdd(c[0], c[1], c[2]);
                if (!Same(got, want)) { ++mismatches; if (mismatches <= 5) bad.AppendLine($"fma({B(c[0])}, {B(c[1])}, {B(c[2])}) = {B(got)}, software {B(want)}"); }
            }
            Assert.True(mismatches == 0, $"{mismatches} of {count}\n{bad}");
        }

        [Fact]
        public void Fma_SpecialValues()
        {
            double inf = double.PositiveInfinity, nan = double.NaN;
            var cases = new[] {
                new[] { 0.0, inf, 1.0 }, new[] { inf, 0.0, 1.0 }, new[] { inf, 1.0, -inf }, new[] { 1.0, 1.0, nan }, new[] { inf, inf, inf },
                new[] { -0.0, 1.0, -0.0 }, new[] { 0.0, 1.0, -0.0 }, new[] { -0.0, 1.0, 0.0 }, new[] { 1e308, 10.0, -1e308 }, new[] { 1e-320, 1e-320, 1e-320 },
                new[] { double.MaxValue, 1.0, double.MaxValue }, new[] { 1.0, double.Epsilon, -double.Epsilon }, new[] { 3.0, 1.0 / 3.0, -1.0 },
            };
            foreach (double[] c in cases) {
                double want = CoreMathReference.Fma(c[0], c[1], c[2]);
                Assert.True(Same(CoreMath.SoftwareFusedMultiplyAdd(c[0], c[1], c[2]), want), $"software fma({c[0]:R}, {c[1]:R}, {c[2]:R})");
                Assert.True(Same(CoreMath.FusedMultiplyAdd(c[0], c[1], c[2]), want), $"helper fma({c[0]:R}, {c[1]:R}, {c[2]:R})");
            }
        }
    }
}
