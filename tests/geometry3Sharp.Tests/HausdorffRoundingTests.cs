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
            // the largest double below 2^-1021 against 0: its half is a tie that rounds up to the normal 2^-1022 (the halving rounded,
            // though the midpoint is normal; numerics review of #819, F1)
            { 4.4501477170144023E-308, 0 },
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

        /// <summary> the upper bound of one rounded child (TriangleUpper, docs c3) in a quantized frame: at Y = 2^52 (an ulp is 1) the
        /// exact child (0, Y, 0), (1, Y, 0), (0.5, Y + 0.5, 0) is the rounded (0, Y, 0), (1, Y, 0), (0.5, Y, 0), its third corner off by
        /// 0.5. The target is one tilted triangle, farther from the exact corner (1.078) than from its rounded image (0.98). The bound
        /// of the rounded triangle alone (all corners nearest the same triangle: their largest distance) is below the exact maximum
        /// over the exact child; with the corners' allowance it is above it (numerics review of #819) </summary>
        [Fact]
        public void TriangleUpper_CoversTheExactChild_WhichTheRoundedBoundAloneDoesNot()
        {
            double Y = Math.Pow(2, 52);
            var b = new DMesh3();
            b.AppendTriangle(b.AppendVertex(new Vector3d(-5, Y - 10, -1)), b.AppendVertex(new Vector3d(5, Y - 10, -1)), b.AppendVertex(new Vector3d(0, Y + 10, 3)));
            var bounds = new TriangleDeviationBounds(b);
            Vector3d c0 = new Vector3d(0, Y, 0), c1 = new Vector3d(1, Y, 0), c2 = HausdorffRounding.Midpoint(new Vector3d(0, Y, 0), new Vector3d(1, Y + 1, 0), out double err);
            Assert.Equal(new Vector3d(0.5, Y, 0), c2);        // (the midpoint (0.5, Y + 0.5) rounds to y = Y)
            Assert.True(err >= 0.5, "allowance " + err);
            double D(Vector3d p) => Math.Sqrt(MeshQueries.TriangleDistance(b, 0, p).DistanceSquared);
            double withOff = HausdorffRounding.TriangleUpper(bounds, c0, c1, c2, D(c0), D(c1), D(c2), 0, 0, 0, 0, 0, err, 0);
            double alone = HausdorffRounding.TriangleUpper(bounds, c0, c1, c2, D(c0), D(c1), D(c2), 0, 0, 0, 0, 0, 0, 0);
            // the exact child, measured exactly shifted by Y (exact: Sterbenz), on a dense barycentric grid
            var sb = new DMesh3();
            sb.AppendTriangle(sb.AppendVertex(new Vector3d(-5, -10, -1)), sb.AppendVertex(new Vector3d(5, -10, -1)), sb.AppendVertex(new Vector3d(0, 10, 3)));
            Vector3d e0 = new Vector3d(0, 0, 0), e1 = new Vector3d(1, 0, 0), e2 = new Vector3d(0.5, 0.5, 0);
            double exact = 0;
            const int n = 40;
            for (int i = 0; i <= n; ++i)
                for (int j = 0; i + j <= n; ++j) {
                    Vector3d q = e0 + (i / (double)n) * (e1 - e0) + (j / (double)n) * (e2 - e0);
                    exact = Math.Max(exact, Math.Sqrt(MeshQueries.TriangleDistance(sb, 0, q).DistanceSquared));
                }
            Assert.True(alone < exact, $"the rounded triangle's bound {alone:R} is not below the exact child's maximum {exact:R}: the test has no power");
            Assert.True(withOff >= exact, $"the bound with the allowance {withOff:R} is below the exact child's maximum {exact:R}");
        }

        [Fact]
        public void MidOff_IsAtLeastTheMeanPlusTheError()
        {
            Assert.True(HausdorffRounding.MidOff(1, 1, 0) >= 1);
            Assert.True(HausdorffRounding.MidOff(0.5, 0.25, 0.125) >= 0.5);
            Assert.True(HausdorffRounding.MidOff(3 * U, 0, 0) >= 1.5 * U);       // (a subnormal halving)
            Assert.True(HausdorffRounding.MidOff(1e-300, 3e-300, 1e-310) > 2e-300);
            Assert.Equal(0, HausdorffRounding.MidOff(0, 0, 0));
            Assert.True(HausdorffRounding.MidOff(U, 0, 0) > 0);                    // (half of 2^-1074 rounds to 0; N3)
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
