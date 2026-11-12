// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The large verification of the vendored CORE-MATH port against the BigInteger reference: N random arguments per function
// (environment variable CORE_MATH_N, default 20000; 1000000 for the campaign of the PR) and, if CORE_MATH_WC names a
// directory with the CORE-MATH worst-case lists (<dir>/<function>/<function>.wc), up to CORE_MATH_WC_N (default 20000)
// evenly spread hard cases per function. Nightly tier: it takes minutes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g3.CoreMathPort;
using Xunit;

namespace g3.Tests
{
    public class CoreMathCampaignTests
    {
        static ulong SplitMix(ulong x)
        {
            ulong z = x + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static int EnvInt(string name, int dflt)
        {
            string s = Environment.GetEnvironmentVariable(name);
            return int.TryParse(s, out int v) ? v : dflt;
        }

        static double U(ulong h, double lo, double hi) { return lo + (hi - lo) * ((h >> 11) * (1.0 / 9007199254740992.0)); }

        static double AnyFinite(ulong seed, ref int k)
        {
            while (true) {
                double d = BitConverter.Int64BitsToDouble((long)SplitMix(seed + (ulong)(k++)));
                if (!double.IsNaN(d) && !double.IsInfinity(d)) return d;
            }
        }

        /// <summary> C99 hexadecimal floating point, as written in the .wc files: [-]0x1.8p+3 </summary>
        static bool TryHexFloat(string t, out double v)
        {
            v = 0;
            bool neg = false;
            if (t.StartsWith("-")) { neg = true; t = t.Substring(1); }
            else if (t.StartsWith("+")) t = t.Substring(1);
            if (!t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
            t = t.Substring(2);
            int p = t.IndexOfAny(new[] { 'p', 'P' });
            if (p < 0) return false;
            if (!int.TryParse(t.Substring(p + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int exp)) return false;
            string mant = t.Substring(0, p);
            int dot = mant.IndexOf('.');
            string ip = dot < 0 ? mant : mant.Substring(0, dot), fp = dot < 0 ? "" : mant.Substring(dot + 1);
            if (ip.Length + fp.Length == 0 || ip.Length + fp.Length > 16) return false;
            if (!ulong.TryParse(ip + fp, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong m)) return false;
            // value = m * 2^(exp - 4 * fp.Length): exact when m < 2^53 (the lists hold doubles)
            double r = m;
            int e2 = exp - 4 * fp.Length;
            if (m >= (1UL << 53)) return false;
            // scale in two steps to stay in range
            while (e2 > 1000) { r *= Math.Pow(2, 1000); e2 -= 1000; }
            while (e2 < -1000) { r *= Math.Pow(2, -1000); e2 += 1000; if (r == 0) break; }
            r *= Math.Pow(2, e2);
            v = neg ? -r : r;
            return true;
        }

        static List<string[]> LoadWc(string dir, string fn, int maxCount)
        {
            string path = Path.Combine(dir, fn, fn + ".wc");
            var all = new List<string[]>();
            if (!File.Exists(path)) return all;
            foreach (string line in File.ReadLines(path)) {
                if (line.Length == 0 || line[0] == '#') continue;
                all.Add(line.Split(','));
            }
            if (all.Count <= maxCount) return all;
            var r = new List<string[]>();
            for (int i = 0; i < maxCount; ++i) r.Add(all[(int)((long)i * all.Count / maxCount)]);
            return r;
        }

        static void Run(string name, Func<double, double> f, Func<double, double> reference, List<double> args)
        {
            long mismatches = 0, skipped = 0;
            var bad = new System.Collections.Concurrent.ConcurrentQueue<string>();
            Parallel.For(0, args.Count, i => {
                double x = args[i];
                double got = f(x), want;
                try { want = reference(x); }
                catch (InvalidOperationException) { Interlocked.Increment(ref skipped); return; }       // too close to a rounding boundary for the reference: skip (counted)
                if (BitConverter.DoubleToInt64Bits(got) != BitConverter.DoubleToInt64Bits(want) && !(double.IsNaN(got) && double.IsNaN(want))) {
                    Interlocked.Increment(ref mismatches);
                    if (bad.Count < 8) bad.Enqueue($"{name}({BitConverter.DoubleToInt64Bits(x):X16} = {x:R}): got {got:R}, want {want:R}");
                }
            });
            Assert.True(skipped <= args.Count / 1000, $"{name}: {skipped} of {args.Count} arguments skipped by the reference");
            Assert.True(mismatches == 0, $"{name}: {mismatches} of {args.Count} results not correctly rounded\n" + string.Join("\n", bad));
        }

        static List<double> RandomArgs(string fn, int n, ulong seed)
        {
            var r = new List<double>(n);
            int k = 0;
            for (int i = 0; i < n; ++i) {
                ulong h = SplitMix(seed ^ (ulong)i * 0x1000193UL);
                switch (fn) {
                    case "exp":
                        r.Add(i % 4 == 0 ? U(h, -745.2, 709.8) : i % 4 == 1 ? U(h, -2, 2) : i % 4 == 2 ? U(h, -1e-3, 1e-3) : U(h, -746, -700)); break;
                    case "log":
                        r.Add(i % 4 == 0 ? Math.Abs(AnyFinite(seed + h, ref k)) : i % 4 == 1 ? U(h, 0.5, 2) : i % 4 == 2 ? 1.0 + (double)(long)(h % 4001 - 2000) * Math.Pow(2, -52) : U(h, 1e-3, 1e3)); break;
                    default:        // sin, cos
                        r.Add(i % 4 == 0 ? U(h, -50, 50) : i % 4 == 1 ? U(h, -1e-3, 1e-3) : i % 4 == 2 ? AnyFinite(seed + h, ref k) : U(h, -1e6, 1e6)); break;
                }
            }
            return r;
        }

        [Trait("Category", "Nightly")]
        [Theory]
        [InlineData("exp")]
        [InlineData("log")]
        [InlineData("sin")]
        [InlineData("cos")]
        public void Unary_RandomAndWorstCaseArguments_AreCorrectlyRounded(string fn)
        {
            Func<double, double> f = fn == "exp" ? (Func<double, double>)CoreMath.Exp : fn == "log" ? CoreMath.Log : fn == "sin" ? (Func<double, double>)CoreMath.Sin : CoreMath.Cos;
            Func<double, double> reference = fn == "exp" ? (Func<double, double>)CoreMathReference.Exp : fn == "log" ? CoreMathReference.Log : fn == "sin" ? (Func<double, double>)CoreMathReference.Sin : CoreMathReference.Cos;
            Run(fn, f, reference, RandomArgs(fn, EnvInt("CORE_MATH_N", 20000), 0xC0DEUL + (ulong)fn.Length));
            string dir = Environment.GetEnvironmentVariable("CORE_MATH_WC");
            if (!string.IsNullOrEmpty(dir)) {
                var hard = new List<double>();
                foreach (string[] cols in LoadWc(dir, fn, EnvInt("CORE_MATH_WC_N", 20000)))
                    if (TryHexFloat(cols[0].Trim(), out double v) && !double.IsInfinity(v) && !double.IsNaN(v)) hard.Add(v);
                Run(fn + " worst cases", f, reference, hard);
            }
        }

        [Trait("Category", "Nightly")]
        [Fact]
        public void Atan2_RandomAndWorstCaseArguments_AreCorrectlyRounded()
        {
            int n = EnvInt("CORE_MATH_N", 20000);
            var pairs = new List<double[]>(n);
            int k = 0;
            for (int i = 0; i < n; ++i) {
                ulong h = SplitMix(0xA7A2UL ^ (ulong)i * 0x1000193UL);
                switch (i % 4) {
                    case 0: pairs.Add(new[] { U(h, -10, 10), U(SplitMix(h), -10, 10) }); break;
                    case 1: pairs.Add(new[] { AnyFinite(h, ref k), AnyFinite(h + 7, ref k) }); break;
                    case 2: pairs.Add(new[] { U(h, -1e-5, 1e-5), U(SplitMix(h), 0.5, 2) }); break;
                    default: pairs.Add(new[] { U(h, 1, 100), U(SplitMix(h), -1e-3, 1e-3) }); break;
                }
            }
            string dir = Environment.GetEnvironmentVariable("CORE_MATH_WC");
            if (!string.IsNullOrEmpty(dir))
                foreach (string[] cols in LoadWc(dir, "atan2", EnvInt("CORE_MATH_WC_N", 20000)))
                    if (cols.Length == 2 && TryHexFloat(cols[0].Trim(), out double y) && TryHexFloat(cols[1].Trim(), out double x)) pairs.Add(new[] { y, x });
            long mismatches = 0, skipped = 0;
            var bad = new System.Collections.Concurrent.ConcurrentQueue<string>();
            Parallel.For(0, pairs.Count, i => {
                double y = pairs[i][0], x = pairs[i][1];
                double got = CoreMath.Atan2(y, x), want;
                try { want = CoreMathReference.Atan2(y, x); }
                catch (InvalidOperationException) { Interlocked.Increment(ref skipped); return; }
                if (BitConverter.DoubleToInt64Bits(got) != BitConverter.DoubleToInt64Bits(want) && !(double.IsNaN(got) && double.IsNaN(want))) {
                    Interlocked.Increment(ref mismatches);
                    if (bad.Count < 8) bad.Enqueue($"atan2({y:R}, {x:R}): got {got:R}, want {want:R}");
                }
            });
            Assert.True(skipped <= pairs.Count / 1000, $"atan2: {skipped} of {pairs.Count} arguments skipped by the reference");
            Assert.True(mismatches == 0, $"atan2: {mismatches} of {pairs.Count} results not correctly rounded\n" + string.Join("\n", bad));
        }
    }
}
