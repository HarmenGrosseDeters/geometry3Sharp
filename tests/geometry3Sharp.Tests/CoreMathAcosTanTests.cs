// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Tests of CoreMath.Acos and CoreMath.Tan (the vendored CORE-MATH port, see docs/SOURCES.md): correctly rounded, i.e. equal
// to the exact result rounded to nearest even, on every runtime. The expected values come from CoreMathReference (BigInteger
// fixed point, no shared code with the port), never from System.Math. Also the special values, monotonicity (a correctly
// rounded function of a monotone function is monotone over neighbouring doubles: the composed AcosByAtan2 is not), the
// symmetry and the bits pinned for the determinism harness.
using System;
using System.Collections.Generic;
using System.Text;
using g3.CoreMathPort;
using Xunit;
using Xunit.Abstractions;

namespace g3.Tests
{
    public class CoreMathAcosTanTests
    {
        readonly ITestOutputHelper output;
        public CoreMathAcosTanTests(ITestOutputHelper output) { this.output = output; }

        // ---------------------------------------------------------------- inputs and helpers

        static ulong SplitMix(ref ulong s)
        {
            ulong z = (s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static double Unit(ref ulong s) { return (SplitMix(ref s) >> 11) * (1.0 / 9007199254740992.0); }

        static long Bits(double d) { return BitConverter.DoubleToInt64Bits(d); }
        static double FromBits(long b) { return BitConverter.Int64BitsToDouble(b); }
        static string Hex(double d) { return Bits(d).ToString("X16"); }

        // the neighbouring double of a finite one, in the direction of larger values (up) or smaller ones
        static double Step(double x, bool up)
        {
            if (x == 0) return up ? double.Epsilon : -double.Epsilon;
            return FromBits(Bits(x) + ((x > 0) == up ? 1 : -1));
        }

        static IEnumerable<double> FromBitList(ulong[] bits)
        {
            foreach (ulong b in bits) yield return FromBits((long)b);
        }

        static void CheckAll(string name, Func<double, double> f, Func<double, double> reference, IEnumerable<double> args)
        {
            var bad = new StringBuilder();
            int count = 0, mismatches = 0;
            foreach (double x in args) {
                ++count;
                double got = f(x), want = reference(x);
                bool same = Bits(got) == Bits(want) || (double.IsNaN(got) && double.IsNaN(want));
                if (!same) {
                    ++mismatches;
                    if (mismatches <= 5) bad.AppendLine($"{name}({Hex(x)} = {x:R}): got {Hex(got)} ({got:R}), want {Hex(want)} ({want:R})");
                }
            }
            Assert.True(count > 0);
            Assert.True(mismatches == 0, $"{name}: {mismatches} of {count} results not correctly rounded\n{bad}");
        }

        static List<double> AcosArgs(int n, ulong seed)
        {
            var r = new List<double>();
            ulong s = seed;
            for (int i = 0; i < n; ++i) {
                switch (i % 6) {
                    case 0: r.Add(-1 + 2 * Unit(ref s)); break;                                                   // the whole domain
                    case 1: r.Add(1 - Math.Pow(2, -(int)(SplitMix(ref s) % 53)) * Unit(ref s)); break;           // 2^-k below 1
                    case 2: r.Add(-1 + Math.Pow(2, -(int)(SplitMix(ref s) % 53)) * Unit(ref s)); break;          // 2^-k above -1
                    case 3: r.Add((Unit(ref s) - 0.5) * Math.Pow(2, -(int)(SplitMix(ref s) % 60))); break;       // around 0, down to tiny
                    case 4: r.Add(0.5 * (Unit(ref s) - 0.5)); break;                                              // |x| < 1/4, where the composed version is not monotone
                    default: r.Add(Math.Sqrt(Unit(ref s)) * (Unit(ref s) < 0.5 ? -1 : 1)); break;
                }
            }
            return r;
        }

        static List<double> TanArgs(int n, ulong seed)
        {
            var r = new List<double>();
            ulong s = seed;
            for (int i = 0; i < n; ++i) {
                switch (i % 6) {
                    case 0: r.Add(-50 + 100 * Unit(ref s)); break;
                    case 1: r.Add((Unit(ref s) - 0.5) * 2e-3); break;
                    case 2: r.Add(FromBits((long)SplitMix(ref s) & 0x7FFFFFFFFFFFFFFF) is double d && !double.IsNaN(d) && !double.IsInfinity(d)
                                  ? d : 1.0); break;                                                              // any exponent up to 2^1023
                    case 3: r.Add(-1e6 + 2e6 * Unit(ref s)); break;
                    case 4: r.Add(Math.PI / 2 * (1 + 2 * (int)(SplitMix(ref s) % 50)) * (1 + (Unit(ref s) - 0.5) * 1e-12)); break;   // close to odd multiples of pi / 2
                    default: r.Add((Unit(ref s) - 0.5) * Math.Pow(2, -(int)(SplitMix(ref s) % 70))); break;      // tiny, down to subnormal results
                }
            }
            return r;
        }

        // ---------------------------------------------------------------- acos

        [Fact]
        public void Acos_IsCorrectlyRounded()
        {
            CheckAll("acos", CoreMath.Acos, CoreMathReference.Acos, AcosArgs(480, 11));
        }

        [Fact]
        public void Acos_HardCases_AreCorrectlyRounded()
        {
            // includes the seven arguments the port special-cases (the worst cases of CORE-MATH's fast path) and the
            // arguments for which the fast path cannot decide, so the accurate path of the port is exercised
            CheckAll("acos", CoreMath.Acos, CoreMathReference.Acos, FromBitList(CoreMathHardCasesAcosTan.Acos));
        }

        [Fact]
        public void Acos_SpecialValues()
        {
            Assert.Equal(0L, Bits(CoreMath.Acos(1)));                                   // +0
            Assert.Equal(Bits(Math.PI), Bits(CoreMath.Acos(-1)));
            Assert.Equal(Bits(Math.PI / 2), Bits(CoreMath.Acos(0.0)));
            Assert.Equal(Bits(Math.PI / 2), Bits(CoreMath.Acos(-0.0)));
            foreach (double x in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.0000000000000002, -1.0000000000000002, 2, -7, 1e300, double.MinValue })
                Assert.True(double.IsNaN(CoreMath.Acos(x)), x.ToString("R"));
            // tiny arguments: pi / 2 minus less than half an ulp, so the double nearest pi / 2 (checked against the reference)
            foreach (double x in new[] { double.Epsilon, -double.Epsilon, 1e-300, -1e-300, 1e-20, -1e-20 })
                Assert.Equal(Bits(CoreMathReference.Acos(x)), Bits(CoreMath.Acos(x)));
            // the neighbours of the ends: tiny and just below pi
            double below1 = 0.99999999999999989, aboveM1 = -0.99999999999999989;
            Assert.Equal(Bits(CoreMathReference.Acos(below1)), Bits(CoreMath.Acos(below1)));
            Assert.Equal(Bits(CoreMathReference.Acos(aboveM1)), Bits(CoreMath.Acos(aboveM1)));
            Assert.True(CoreMath.Acos(below1) > 0 && CoreMath.Acos(below1) < 2e-8);
            Assert.True(CoreMath.Acos(aboveM1) < Math.PI && CoreMath.Acos(aboveM1) > Math.PI - 2e-8);
        }

        [Fact]
        public void Acos_AroundTheTinyArgumentThreshold_IsCorrectlyRounded()
        {
            // the port returns pi / 2 without a computation up to |x| = 0x3C8CB3B399D747F2 (about 6.1e-17, half an ulp of pi / 2 minus
            // the rounding); 60 doubles at each side of it, both signs, and the same for the 1/2 (table) boundary of the branches
            var args = new List<double>();
            foreach (long centre in new[] { 0x3C8CB3B399D747F2L, 0x3FE0000000000000L, 0x3FD0000000000000L })
                for (long d = -30; d <= 30; ++d) { args.Add(FromBits(centre + d)); args.Add(-FromBits(centre + d)); }
            CheckAll("acos", CoreMath.Acos, CoreMathReference.Acos, args);
        }

        [Fact]
        public void Acos_IsMonotoneOverNeighbouringDoubles()
        {
            // the composed AcosByAtan2 is not (0.23144537602828275 and its successor: results 1.3372331895174001 and
            // 1.3372331895174003 for an argument that grows); a correctly rounded acos never rises. Windows of 3000
            // consecutive doubles around 1, -1, 0, the documented kink and a few interior points
            foreach (double centre in new[] { 1.0, -1.0, 0.0, 0.23144537602828275, -0.23144537602828275, 0.5, -0.5, 0.7071067811865476, 0.999, -0.999, 1e-8, 0.1 }) {
                double lo = centre;
                for (int i = 0; i < 1500; ++i) lo = Step(lo, false);
                double x = lo, prev = double.PositiveInfinity;
                for (int i = 0; i < 3000; ++i, x = Step(x, true)) {
                    if (!(x >= -1 && x <= 1)) continue;
                    double a = CoreMath.Acos(x);
                    Assert.True(a <= prev, $"acos rises at {Hex(x)} = {x:R}: {a:R} after {prev:R}");
                    prev = a;
                }
            }
            // the composed version really does rise there: the property is not vacuous
            double c0 = 0.23144537602828275, c1 = Step(c0, true);
            Assert.True(CoreMath.AcosByAtan2(c1) > CoreMath.AcosByAtan2(c0));
            Assert.True(CoreMath.Acos(c1) <= CoreMath.Acos(c0));
        }

        [Fact]
        public void Acos_ReflectionFormula_HoldsToTheRounding()
        {
            // acos(-x) = pi - acos(x) exactly, so the correctly rounded values of acos(x) and acos(-x) differ
            // in total (their sum is within one ulp of pi); a swapped branch for negative arguments would not
            ulong s = 77;
            for (int i = 0; i < 20000; ++i) {
                double x = -1 + 2 * Unit(ref s);
                double sum = CoreMath.Acos(x) + CoreMath.Acos(-x);
                Assert.True(Math.Abs(sum - Math.PI) <= 4.5e-16, $"acos({x:R}) + acos({-x:R}) = {sum:R}");
            }
        }

        [Fact]
        public void Acos_DiffersFromTheComposedVersionByAtMostTwoUlps()
        {
            // what the switch of the terrain slope design from AcosByAtan2 to Acos (2026-11-17) changed:
            // by derivation at most 1.6 ulps + 0.5 apart, and in a few percent of the arguments
            ulong s = 4242;
            long differ = 0, count = 0, worst = 0;
            for (int i = 0; i < 100000; ++i) {
                double x = i % 3 == 0 ? -1 + 2 * Unit(ref s) : i % 3 == 1 ? 1 - Math.Pow(2, -(int)(SplitMix(ref s) % 52)) * Unit(ref s) : (Unit(ref s) - 0.5) * 0.5;
                double a = CoreMath.Acos(x), b = CoreMath.AcosByAtan2(x);
                long u = Math.Abs(Bits(a) - Bits(b));
                ++count;
                if (u != 0) ++differ;
                worst = Math.Max(worst, u);
            }
            output.WriteLine($"{differ} of {count} arguments differ from AcosByAtan2, worst {worst} ulps");
            Assert.True(worst <= 2, $"{worst} ulps");
            Assert.True(differ > count / 100 && differ < count / 4, $"{differ} of {count} differ");
        }

        // ---------------------------------------------------------------- tan

        [Fact]
        public void Tan_IsCorrectlyRounded()
        {
            CheckAll("tan", CoreMath.Tan, CoreMathReference.Tan, TanArgs(480, 13));
        }

        [Fact]
        public void Tan_HardCases_AreCorrectlyRounded()
        {
            // the two arguments the port special-cases, and those for which the fast path cannot decide
            CheckAll("tan", CoreMath.Tan, CoreMathReference.Tan, FromBitList(CoreMathHardCasesAcosTan.Tan));
        }

        [Fact]
        public void Tan_NearThePoles_AreCorrectlyRounded()
        {
            // 60 doubles on each side of the doubles nearest to odd multiples of pi / 2: the largest results (cos below 2^-50
            // after the reduction) and the sign change; and the classical worst case for the argument reduction
            var args = new List<double>();
            foreach (double centre in new[] { Math.PI / 2, -Math.PI / 2, 3 * (Math.PI / 2), 11 * (Math.PI / 2), 1e5 * (Math.PI / 2), 6381956970095103.0 * Math.Pow(2, 797), 5.0e15, 1.5707963267948966e22 }) {
                double d = centre;
                for (int i = 0; i < 30; ++i) d = Step(d, false);
                for (int i = 0; i < 61; ++i, d = Step(d, true)) args.Add(d);
            }
            CheckAll("tan", CoreMath.Tan, CoreMathReference.Tan, args);
        }

        [Fact]
        public void Tan_AroundTheTinyArgumentThreshold_IsCorrectlyRounded()
        {
            // the port returns x + x * 2^-54 (one fused multiply-add) up to |x| = 0x3E4D12ED0AF1A27E (about 1.49e-8); 60 doubles at
            // each side of it, both signs, and around the first table entries of the fast path
            var args = new List<double>();
            foreach (long centre in new[] { 0x3E4D12ED0AF1A27EL, 0x3F50000000000000L, 0x3FD0000000000000L })
                for (long d = -30; d <= 30; ++d) { args.Add(FromBits(centre + d)); args.Add(-FromBits(centre + d)); }
            CheckAll("tan", CoreMath.Tan, CoreMathReference.Tan, args);
        }

        [Fact]
        public void Tan_SpecialValues()
        {
            Assert.Equal(0L, Bits(CoreMath.Tan(0.0)));
            Assert.Equal(Bits(-0.0), Bits(CoreMath.Tan(-0.0)));
            foreach (double x in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.True(double.IsNaN(CoreMath.Tan(x)), x.ToString("R"));
            // subnormal and tiny arguments: tan(x) = x (+ x^3 / 3 below half an ulp) with the sign of x
            foreach (double x in new[] { double.Epsilon, -double.Epsilon, 3 * double.Epsilon, 2.2250738585072014E-308, 1e-200, -1e-100, 1e-9, -3e-9 })
                Assert.Equal(Bits(CoreMathReference.Tan(x)), Bits(CoreMath.Tan(x)));
            Assert.Equal(Bits(double.Epsilon), Bits(CoreMath.Tan(double.Epsilon)));
            Assert.Equal(Bits(-1e-100), Bits(CoreMath.Tan(-1e-100)));
            // the largest finite argument
            Assert.Equal(Bits(CoreMathReference.Tan(double.MaxValue)), Bits(CoreMath.Tan(double.MaxValue)));
            Assert.Equal(Bits(CoreMathReference.Tan(-double.MaxValue)), Bits(CoreMath.Tan(-double.MaxValue)));
            // the double nearest pi / 2: the largest result, about 1.633e16, and its successor is negative
            double p = Math.PI / 2;
            Assert.Equal(Bits(CoreMathReference.Tan(p)), Bits(CoreMath.Tan(p)));
            Assert.True(CoreMath.Tan(p) > 1.6e16 && CoreMath.Tan(p) < 1.7e16);
            Assert.True(CoreMath.Tan(Step(p, true)) < -2e15);
        }

        [Fact]
        public void Tan_IsOddAndMonotoneOverNeighbouringDoubles()
        {
            ulong s = 31;
            for (int i = 0; i < 20000; ++i) {
                double x = -20 + 40 * Unit(ref s);
                Assert.Equal(Bits(-CoreMath.Tan(x)), Bits(CoreMath.Tan(-x)));
            }
            // tan increases between its poles; a correctly rounded one never decreases over neighbouring doubles. Windows of
            // 3000 doubles at both sides of the poles, at 0 and at the interior points (not for huge arguments: there neighbouring
            // doubles are many periods apart)
            foreach (double centre in new[] { Math.PI / 2, -Math.PI / 2, 3 * (Math.PI / 2), Math.PI / 4, 1.0, -1.0, 1e-8, 0.0, 100.0 }) {
                double d = centre;
                for (int i = 0; i < 1500; ++i) d = Step(d, false);
                double prev = double.NegativeInfinity;
                bool poleSeen = false;
                for (int i = 0; i < 3000; ++i, d = Step(d, true)) {
                    double t = CoreMath.Tan(d);
                    if (t < prev) {
                        // only the pole itself (positive huge, then negative huge) may fall, once
                        Assert.False(poleSeen, $"tan falls twice around {centre:R}, at {Hex(d)}");
                        Assert.True(prev > 1e8 && t < -1e8, $"tan falls at {Hex(d)} = {d:R}: {t:R} after {prev:R}");
                        poleSeen = true;
                    }
                    prev = t;
                }
            }
        }

        [Fact]
        public void Tan_OfAcos_IsSqrtOverX()
        {
            // tan(acos(x)) = sqrt(1 - x^2) / x: the two functions agree on the relation to a few ulps (an end-to-end check
            // that does not use the reference: a wrong table entry in either port would break it)
            ulong s = 5;
            for (int i = 0; i < 5000; ++i) {
                double x = 0.05 + 0.9 * Unit(ref s);
                double t = CoreMath.Tan(CoreMath.Acos(x));
                double want = Math.Sqrt((1 - x) * (1 + x)) / x;
                Assert.True(Math.Abs(t - want) <= 4e-15 * want, $"x = {x:R}: tan(acos(x)) = {t:R}, sqrt(1 - x^2) / x = {want:R}");
            }
        }

        // ---------------------------------------------------------------- the bits the determinism harness traces (docs/design/DETERMINISM.md, C1)

        [Theory]
        [InlineData(0.5, "3ff0c152382d7366")]
        [InlineData(-0.5, "4000c152382d7366")]
        [InlineData(0.1, "3ff787b22ce3f590")]
        [InlineData(0.9999, "3f8cf69d216bd74b")]
        [InlineData(0.3333333333333333, "3ff3b2028082e8d4")]
        public void Acos_PinnedBits(double x, string expected)
        {
            // the bits of the correctly rounded value, equal to the reference on every platform
            Assert.Equal(expected, Hex(CoreMath.Acos(x)).ToLowerInvariant());
            Assert.Equal(expected, Hex(CoreMathReference.Acos(x)).ToLowerInvariant());
        }

        [Theory]
        [InlineData(0.5, "3fe17b4f5bf3474a")]
        [InlineData(1.0, "3ff8eb245cbee3a6")]
        [InlineData(-2.0, "40017af62e0950f8")]
        [InlineData(1e22, "bffa0f79c1b6b257")]
        [InlineData(1.5707963267948966, "434d02967c31cdb5")]
        public void Tan_PinnedBits(double x, string expected)
        {
            Assert.Equal(expected, Hex(CoreMath.Tan(x)).ToLowerInvariant());
            Assert.Equal(expected, Hex(CoreMathReference.Tan(x)).ToLowerInvariant());
        }
    }
}
