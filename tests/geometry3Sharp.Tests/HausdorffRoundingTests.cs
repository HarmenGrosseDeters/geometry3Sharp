using System;
using System.Linq;
using System.Numerics;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #738: the rounding allowance of subdivision points (HausdorffRounding; docs/design/HAUSDORFF_CERTIFIED_BOUNDS.md c1, c2),
    /// against the exact error of each midpoint computed in integers, including subnormal sums and remainders
    /// </summary>
    public class HausdorffRoundingTests
    {
        // sum over the components of |m - (a + b) / 2|, exactly, in units of 2^-1076 (every double is a multiple of 2^-1074)
        static BigInteger ExactError(Vector3d a, Vector3d b, Vector3d m)
        {
            BigInteger total = BigInteger.Zero;
            for (int k = 0; k < 3; ++k)
                total += BigInteger.Abs(2 * Units(m[k]) - Units(a[k]) - Units(b[k]));    // 2 |m - (a+b)/2| in units of 2^-1075
            return total;
        }

        // v in units of 2^-1075, or 2^-1076 for the allowance (exact)
        static BigInteger Units(double v, int unitExp = -1075)
        {
            if (v == 0) return BigInteger.Zero;
            ExactPoint3.Decompose(v, out BigInteger mm, out int e);
            return mm << (e - unitExp);
        }

        static bool Covers(double err, BigInteger exact) => Units(err, -1076) >= exact;

        static readonly double U = double.Epsilon, MinNormal = 2.2250738585072014E-308;

        public static TheoryData<double, double> Pairs() => new TheoryData<double, double> {
            { U, 2 * U },                       // a subnormal sum 3 2^-1074: its half rounds
            { 3 * U, 0 },                       // the same, against 0
            { -U, 0 },                          // half of the smallest subnormal rounds to 0
            { 2 * MinNormal, 3 * U },           // a remainder of the sum of 2^-1074 (subnormal): its half rounds
            { 0.1, 0.3 },                       // an ordinary rounded sum
            { 1.3 * Math.Pow(2, 52), 1.3 * Math.Pow(2, 52) + 1 },   // ulp 1: the midpoint rounds by 0.5
            { 1e308, 0.7e308 },
            { 1, 1e-300 },
        };

        [Theory]
        [MemberData(nameof(Pairs))]
        public void Midpoint_ErrorBoundsTheExactError(double x, double y)
        {
            foreach (var (a, b) in new[] { (new Vector3d(x, 0, 0), new Vector3d(y, 0, 0)), (new Vector3d(x, y, -x), new Vector3d(y, x, -y)) }) {
                Vector3d m = HausdorffRounding.Midpoint(a, b, out double err);
                BigInteger exact = ExactError(a, b, m);
                Assert.True(Covers(err, exact), $"({x:R}, {y:R}): allowance {err:R} below the exact error ({exact} 2^-1076)");
                Assert.Equal(0.5 * (a + b), m);
            }
        }

        /// <summary> random pairs at all exponents: the allowance is never below the exact error, and at most a few times it (or
        /// a few subnormals) </summary>
        [Fact]
        public void Midpoint_RandomPairs()
        {
            var rnd = new Random(738);
            for (int i = 0; i < 4000; ++i) {
                double R() => (rnd.NextDouble() - 0.5) * Math.Pow(2, rnd.Next(-1080, 1020));
                Vector3d a = new Vector3d(R(), R(), R()), b = new Vector3d(R(), R(), R());
                if (rnd.Next(4) == 0) b = a + new Vector3d(R(), 0, 0) * 1e-16;
                Vector3d m = HausdorffRounding.Midpoint(a, b, out double err);
                BigInteger exact = ExactError(a, b, m);
                Assert.True(Covers(err, exact), $"pair {i}: allowance {err:R} below the exact error ({exact} 2^-1076)");
                // (and not wildly above: at most 4 times it plus a few subnormals)
                Assert.True(Units(err, -1076) <= 4 * exact + 8 * 4, $"pair {i}: allowance {err:R} far above ({exact} 2^-1076)");
            }
        }

        [Fact]
        public void MidOff_IsAtLeastTheMeanPlusTheError()
        {
            Assert.True(HausdorffRounding.MidOff(1, 1, 0) >= 1);
            Assert.True(HausdorffRounding.MidOff(0.5, 0.25, 0.125) >= 0.5);
            Assert.True(HausdorffRounding.MidOff(3 * U, 0, 0) >= 1.5 * U);       // (a subnormal halving)
            Assert.True(HausdorffRounding.MidOff(1e-300, 3e-300, 1e-310) > 2e-300);
            Assert.Equal(0, HausdorffRounding.MidOff(0, 0, 0));
        }

        [Fact]
        public void UpAndDown_CoverTheRounding()
        {
            foreach (double x in new[] { U, 3 * U, MinNormal, 1, 0.1, 1e308, 1.0000000000000002 }) {
                Assert.True(HausdorffRounding.Up(x) > x);
                Assert.True(HausdorffRounding.Down(x) < x);
                Assert.True(HausdorffRounding.Down(x) >= 0);
            }
            Assert.Equal(0, HausdorffRounding.Down(0));
            Assert.Equal(0, HausdorffRounding.Down(-1));
        }
    }
}
