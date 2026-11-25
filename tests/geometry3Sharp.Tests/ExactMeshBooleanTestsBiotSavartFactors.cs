using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1350: the rounding of the Biot-Savart bounds of ExactMeshBoolean (the factors, the sum guard and carry_length):
    /// each computed bound is at least the real number it bounds, and not wildly larger. The references are exact:
    /// BigInteger arithmetic on the grid points (the endpoints of boundary segments are exact grid integers, converted to
    /// doubles as antipodal_grid does) and on dyadic doubles.
    /// </summary>
    public class ExactMeshBooleanTestsBiotSavartFactors
    {
        // floor(sqrt(n)) of a BigInteger
        static BigInteger Isqrt(BigInteger n)
        {
            if (n < 2) return n;
            BigInteger x = BigInteger.One << (int)((ExactPoint3.BitLength(n) + 1) / 2);
            while (true) {
                BigInteger y = (x + n / x) >> 1;
                if (y >= x) return x;
                x = y;
            }
        }

        [Theory]
        [InlineData(2)] [InlineData(4)] [InlineData(6)] [InlineData(8)] [InlineData(10)] [InlineData(14)]
        [InlineData(100)] [InlineData(1 << 26)]
        public void Factor_IsExactlyOnePlusKTimesTheUnitRoundoff(int k)
        {
            double f = ExactMeshBoolean.Factor(k);
            // (f - 1) is exact; f = 1 + j 2^-52 with 2 j = k (not below 1 + k 2^-53)
            double j = (f - 1) * 4503599627370496.0;
            Assert.Equal(Math.Floor(j), j);
            Assert.Equal((BigInteger)k, (BigInteger)j * 2);
        }

        [Theory]
        [InlineData(0L)] [InlineData(1L)] [InlineData(2L)] [InlineData(3L)] [InlineData(1000L)] [InlineData(1000001L)]
        [InlineData(1L << 26)]
        public void SumTermsFactor_IsAtLeastOnePlusMPlusThreeAndAHalfUnitRoundoffs_AndEven(long m)
        {
            double f = ExactMeshBoolean.SumTermsFactor(m);
            double j = (f - 1) * 4503599627370496.0;       // f = 1 + j 2^-52 = 1 + 2 j 2^-53, exact
            BigInteger k = (BigInteger)j * 2;
            Assert.True(k * 10000 >= (BigInteger)m * 10000 + 35002, $"m {m}: k {k}");   // k >= m + 3.5002
            Assert.True(k <= m + 5, $"m {m}: k {k} is not tight");
            Assert.True(k.IsEven);
        }

        [Fact]
        public void SumTermsFactor_AboveTheSumLemma_IsInfinite()
        {
            Assert.True(ExactMeshBoolean.SumTermsFactor(ExactMeshBoolean.MaxSumTerms) <= double.MaxValue);
            Assert.True(ExactMeshBoolean.SumTermsFactor(ExactMeshBoolean.MaxSumTerms + 1L) > double.MaxValue);
            Assert.True(ExactMeshBoolean.SumTermsFactor(long.MaxValue) > double.MaxValue);
        }

        [Fact]
        public void SumBound_IsInfinite_ForAnInfiniteOrUnsetFactor_AlsoForAZeroSum()
        {
            var b = new ExactMeshBoolean.BoundaryBound { SumFactor = double.PositiveInfinity };
            Assert.True(ExactMeshBoolean.sum_bound(0, b) > double.MaxValue);        // (not 0 * infinity = NaN)
            Assert.True(ExactMeshBoolean.sum_bound(3, b) > double.MaxValue);
            b.SumFactor = 0;                                                         // (a boundary that was not made by boundary_bound)
            Assert.True(ExactMeshBoolean.sum_bound(3, b) > double.MaxValue);
            b.SumFactor = double.NaN;
            Assert.True(ExactMeshBoolean.sum_bound(3, b) > double.MaxValue);
            b.SumFactor = 1.5;
            Assert.Equal(4.5, ExactMeshBoolean.sum_bound(3, b));
            b.SumFactor = 1;                                                         // (the least usable factor)
            Assert.Equal(3.0, ExactMeshBoolean.sum_bound(3, b));
            b.SumFactor = double.MaxValue;                                           // (the largest finite one)
            Assert.Equal(double.MaxValue, ExactMeshBoolean.sum_bound(1, b));
            Assert.Equal(0.0, ExactMeshBoolean.sum_bound(0, b));
            // the gradient bound of a boundary without a usable factor is infinite whatever its terms
            var one = ExactMeshBoolean.boundary_bound(new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0) });
            one.SumFactor = double.PositiveInfinity;
            Assert.True(ExactMeshBoolean.gradient_bound(new[] { 100.0, 100, 100, 101, 101, 101 }, one) > double.MaxValue);
            Assert.True(ExactMeshBoolean.gradient_bound(new[] { 100.0, 100, 100, 101, 101, 101 }, one, ExactMeshBoolean.cluster_bound(new[] { 99.0, 99, 99, 102, 102, 102 }, one)) > double.MaxValue);
        }

        // piece_cost's diameter: the longest computed edge plus 2 err, times 1 + 8 u (and at least the true diameter of the
        // piece whose corners are within e per coordinate: the exact longest edge plus sqrt(3) 2 e)
        [Fact]
        public void PieceDiameter_IsAtLeastTheTrueDiameter_AndTheFactorIsEightUnitRoundoffs()
        {
            var rnd = new Random(1353);
            double u = Math.Pow(2, -53);
            for (int trial = 0; trial < 2000; ++trial) {
                Func<double> v = () => rnd.Next(-1 << 20, 1 << 20) * Math.Pow(2, -10);
                Vector3d pa = new Vector3d(v(), v(), v()), pb = new Vector3d(v(), v(), v()), pc = new Vector3d(v(), v(), v());
                double ea = rnd.Next(0, 1000) * Math.Pow(2, -30), eb = rnd.Next(0, 1000) * Math.Pow(2, -30), ec = rnd.Next(0, 1000) * Math.Pow(2, -30);
                double err = ea + eb + ec;           // exact here (multiples of 2^-30 below 2^12)
                double diam = ExactMeshBoolean.piece_diameter(pa, pb, pc, err);
                double m = Math.Max(pa.Distance(pb), Math.Max(pb.Distance(pc), pc.Distance(pa)));
                Assert.True(diam >= (m + 2 * err) * (1 + 7 * u) && diam <= (m + 2 * err) * (1 + 9 * u), $"trial {trial}: factor");
                // the exact squared longest edge (dyadic, 2^-20 units) against diam, with the corner errors worst case
                Func<Vector3d, Vector3d, BigInteger> sq = (a, b) => {
                    BigInteger s = 0;
                    for (int c = 0; c < 3; ++c) { BigInteger d = (BigInteger)((a[c] - b[c]) * Math.Pow(2, 10)); s += d * d; }
                    return s;
                };
                BigInteger longest = BigInteger.Max(sq(pa, pb), BigInteger.Max(sq(pb, pc), sq(pc, pa)));
                // (diam + 0) >= sqrt(longest) 2^-10 + sqrt(3) (e_i + e_j) with e_i + e_j <= err: check diam >= edge + 1.7320508075688774 err
                double edge = (double)Isqrt(longest << 40) * Math.Pow(2, -30);
                Assert.True(diam >= edge + 1.7320508075688774 * err, $"trial {trial}");
            }
        }

        // far_term's overflow branch: the quotients one at a time times 1 + 6 u, plus 4 eps
        [Fact]
        public void FarTermOverflow_FactorIsSixUnitRoundoffs()
        {
            double u = Math.Pow(2, -53);
            int checkedPairs = 0;
            foreach (double d in new[] { 1e155, 3e200, 1e250 })
                foreach (double length in new[] { 1e100, 7e250, 1.0 }) {
                    Assert.True(4 * Math.PI * d * d > double.MaxValue);
                    double r = Math.Min(length / d / d / (4 * Math.PI), 1 / d / (2 * Math.PI));
                    if (r < 1e-290) continue;               // (an underflowed quotient tests nothing about the relative factor)
                    checkedPairs++;
                    double term = ExactMeshBoolean.far_term(length, 1, d);
                    Assert.True(term >= r * (1 + 5 * u) && term <= r * (1 + 7 * u) + 5 * double.Epsilon, $"{length} {d}: {term / r - 1}");
                }
            Assert.True(checkedPairs >= 4, $"only {checkedPairs} non-vacuous pairs");
        }

        // carry_length against exact arithmetic on dyadic points: at least |cp - cq| (the exact distance of the doubles plus
        // sqrt(3) (ep + eq)) and not much more
        [Fact]
        public void CarryLength_IsAtLeastTheExactDistanceBound_AndNotMuchMore()
        {
            var rnd = new Random(1351);
            // sqrt(3) to 40 digits, as a rational over 10^39
            BigInteger sqrt3Num = BigInteger.Parse("1732050807568877293527446341505872366943");
            BigInteger sqrt3Den = BigInteger.Pow(10, 39);
            for (int trial = 0; trial < 3000; ++trial) {
                int e = rnd.Next(-3, 22);
                double scale = Math.Pow(2, e);
                // multiples of 2^-20 below 2^21: the squares are exact in BigInteger over 2^40
                Func<double> v = () => (rnd.Next(-1 << 22, 1 << 22) * Math.Pow(2, -22)) * scale;
                var gp = new Vector3d(v(), v(), v()); var gq = new Vector3d(v(), v(), v());
                if (trial % 3 == 0) gq = gp + new Vector3d(rnd.Next(-8, 8) * Math.Pow(2, -22), 0, rnd.Next(-8, 8) * Math.Pow(2, -22));
                double ep = rnd.Next(0, 1 << 20) * Math.Pow(2, -40) * scale, eq = rnd.Next(0, 1 << 20) * Math.Pow(2, -40) * scale;
                // exact: everything in units of 2^-62 (e <= 21, 22 bits fraction)
                BigInteger U62(double a) { return (BigInteger)(a * Math.Pow(2, 62)); }
                BigInteger sq = 0;
                foreach (double dd in new[] { gp.x - gq.x, gp.y - gq.y, gp.z - gq.z }) sq += U62(dd) * U62(dd);
                // |gp - gq| in units of 2^-62, rounded up; sqrt(3) (ep + eq) the same, rounded up
                BigInteger fl = Isqrt(sq), dist = fl * fl == sq ? fl : fl + 1;
                BigInteger err = (sqrt3Num * (U62(ep) + U62(eq)) + sqrt3Den - 1) / sqrt3Den;
                BigInteger exact = dist + err;
                double len = ExactMeshBoolean.carry_length(gp, gq, ep, eq);
                BigInteger lenUnits = (BigInteger)Math.Ceiling(len * Math.Pow(2, 62));
                Assert.True(lenUnits >= exact, $"trial {trial}: {len} < exact");
                // the factor: 14 u over the computed sum (kills 12 u and 16 u), the sum as the code adds it
                double k = (Math.Abs(gp.x) + Math.Abs(gp.y) + Math.Abs(gp.z)) + (Math.Abs(gq.x) + Math.Abs(gq.y) + Math.Abs(gq.z));
                double sum = (gp.Distance(gq) + 1.7320508075688774 * (ep + eq)) + k * Math.Pow(2, -51);
                double u = Math.Pow(2, -53);
                Assert.True(len >= sum * (1 + 13 * u) && len <= sum * (1 + 15 * u), $"trial {trial}: factor");
            }
        }
    }
}
