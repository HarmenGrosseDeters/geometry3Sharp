using System;
using System.Linq;
using System.Numerics;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The oracle on input whose coordinates span many binary exponents (#947). The exact tube squared of such an input is a quotient of
    /// two numbers beyond the range of a double (an extent of 1 and a vertex at 2^-920 give a denominator of about 2^1900), though its
    /// value is an ordinary double. <see cref="Q.Bounds(out double, out double)"/> then said "does not fit", every double filter that
    /// needs the tube was off, and each of the tens of thousands of tests "within the tube" ran in BigInteger: 6 to 17 s for a mesh of
    /// 1500 vertices that the mesher builds in 0.3 s (the slow units of the mesh fuzz campaign, group (f)).
    /// </summary>
    public class Meshing2OracleWideRangeTests
    {
        /// <summary> the value of a Q lies inside [lo, hi] of Bounds, compared exactly </summary>
        static void AssertContains(Q value, double lo, double hi, string what)
        {
            Assert.True(!double.IsNaN(lo) && !double.IsNaN(hi) && lo <= hi, what + ": interval " + lo + " .. " + hi);
            if (double.IsInfinity(lo) || double.IsInfinity(hi)) return;       // (within 1e-13 of the largest double: no exact value to compare)
            Assert.True(Q.FromDouble(lo) <= value && value <= Q.FromDouble(hi), what + ": " + value + " not in " + lo + " .. " + hi);
        }

        /// <summary> a random odd number of exactly the given number of bits (so that a quotient of two does not reduce) </summary>
        static BigInteger Odd(Random rnd, int bits)
        {
            var bytes = new byte[(bits + 7) / 8 + 1];
            rnd.NextBytes(bytes);
            bytes[bytes.Length - 1] = 0;
            var x = new BigInteger(bytes);
            x &= (BigInteger.One << bits) - 1;
            return x | (BigInteger.One << (bits - 1)) | 1;
        }

        [Fact]
        public void Bounds_OfAQuotientWhosePartsOverflowADouble_ContainTheValue()
        {
            var rnd = new Random(20261116);
            int decided = 0, overflowing = 0;
            for (int n = 0; n < 400; ++n) {
                // odd numerator and denominator of up to 3500 bits (they do not reduce); the value is about 2^(bits of N - bits of D)
                int dBits = rnd.Next(1100, 3500), exponent = rnd.Next(-1100, 1100);
                int nBits = Math.Max(2, dBits + exponent);
                BigInteger num = Odd(rnd, nBits), den = Odd(rnd, dBits);
                if (rnd.Next(2) == 0) num = -num;
                Q q = new Q(num, den);
                bool ok = q.Bounds(out double lo, out double hi);
                if (nBits > 1030 || dBits > 1030) ++overflowing;
                if (Math.Abs(exponent) < 1000) {
                    Assert.True(ok, "about 2^" + exponent + " (" + nBits + " / " + dBits + " bits) did not fit");
                    AssertContains(q, lo, hi, "about 2^" + exponent);
                    ++decided;
                } else if (ok) {
                    // (near the ends of the range, or a tiny value that the absolute padding makes an interval around 0): still contained
                    AssertContains(q, lo, hi, "about 2^" + exponent);
                }
                if (exponent > 1030) Assert.False(ok, "about 2^" + exponent + " is no double");
            }
            Assert.True(decided > 300 && overflowing > 300, "tested " + decided + " in range, " + overflowing + " with parts beyond a double");
        }

        [Fact]
        public void Bounds_InTheOrdinaryRange_IsUnchanged()
        {
            // parts that fit a double take the old path: the interval is the quotient padded by 1e-13 relative and 1e-300
            Q q = new Q(3, 7);
            Assert.True(q.Bounds(out double lo, out double hi));
            double v = 3.0 / 7;
            Assert.Equal(v - (Math.Abs(v) * 1e-13 + 1e-300), lo);
            Assert.Equal(v + (Math.Abs(v) * 1e-13 + 1e-300), hi);
        }

        /// <summary>
        /// A mesh of extent 1 with one input point at y = -2^-920 (the extent in y, and so the tube, is then 0.9375 + 2^-920): the exact tube squared has a denominator of 2^1900 and more. The double
        /// filter of the tube tests decides most of them (old code: none, all exact), and the verdicts of the fast path and the
        /// reference path are the same.
        /// </summary>
        [Fact]
        public void TubeFilter_DecidesWhenTheTubeSquaredHasPartsBeyondADouble_AndTheVerdictsAreTheReferences()
        {
            // eight points, the segments of a near-concurrent fan and a triangle: crossings at small angles make the mesh fine (about 1000
            // vertices) and many triangles hang at the small input angles
            var input = Generators.Literal(new[] {
                0.25, 0.25,   0.75, 0.3125,   0.5, 0.9375,   0.3125, -Math.Pow(2, -920),   0.875, 0.625,   0.125, 0.6875,   0.6875, 0.0625,   0.1875, 0.8125 });
            foreach (var (a, b) in new[] { (0, 1), (1, 2), (2, 0), (3, 2), (4, 5), (6, 7), (3, 4), (5, 6), (0, 4), (7, 1) })
                input.Segments.Add(new SegmentInput(a, b));
            input.MinAngleDeg = 25;
            input.ConstrainedQuality = true;
            input.SteinerLimit = 1500;
            var output = new QualityMesh2Candidate().Run(input);
            Assert.NotEqual(Mesh2Status.Failed, output.Status);
            var e = new OracleExpect { Milestone = Milestone.M3 };
            var fast = Oracle.Check(input, output, e);
            var reference = Oracle.Check(input, output, e.WithReference());
            Assert.Equal(reference.Ok, fast.Ok);
            Assert.Equal(string.Join("|", reference.Violations.Select(v => v.ToString())), string.Join("|", fast.Violations.Select(v => v.ToString())));
            Assert.Equal(reference.BelowAngle, fast.BelowAngle);
            Assert.True(fast.TubeTests > 200, "the input does not exercise the tube tests: " + fast.TubeTests);
            // (a vertex inside or at the tube is decided exactly; the old code, with no filter at all, ran every test exact)
            Assert.True(fast.TubeTestsExact * 3 < fast.TubeTests, fast.TubeTestsExact + " of " + fast.TubeTests + " tube tests ran in exact arithmetic");
        }
    }
}
