using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1350: the rounding of the Biot-Savart bounds of ExactMeshBoolean (L1 gradient_bound / piece_cost, L2 carry_length):
    /// each computed bound is at least the real number it bounds, and not wildly larger. The references are exact:
    /// BigInteger arithmetic on the grid points (the endpoints of boundary segments are exact grid integers, converted to
    /// doubles as antipodal_grid does) and on dyadic doubles.
    /// </summary>
    public class ExactMeshBooleanTestsBiotSavartRounding
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

        // the endpoints are exact grid integers X, Y; x = (double)X, y = (double)Y, as antipodal_grid converts them: the
        // length of the segment XY is at most what boundary_bound stores, which is not much more
        // is the double v (finite, >= 0) at least the real sqrt(sq)? exactly: v^2 >= sq over BigInteger
        static bool AtLeastSqrt(double v, BigInteger sq)
        {
            BigInteger m; int e;
            ExactPoint3.Decompose(v, out m, out e);
            BigInteger a = m * m;
            return e >= 0 ? (a << (2 * e)) >= sq : a >= (sq << (-2 * e));
        }

        [Fact]
        public void BoundaryLengths_BoundTheExactSegment_OfGridPointsThatTheDoublesRound()
        {
            // the repro: X = 2^60 - 127 and Y = 2^60 + 127 both round to the multiples of 128 near them (2^60 - 128, 2^60): the
            // doubles are 128 apart, the exact points 254 (master: 128 (1 + 1e-15))
            var X = new BigInteger[] { (BigInteger.One << 60) - 127, 0, 0 };
            var Y = new BigInteger[] { (BigInteger.One << 60) + 127, 0, 0 };
            var x = new Vector3d((double)X[0], 0, 0);
            var y = new Vector3d((double)Y[0], 0, 0);
            Assert.Equal(128.0, y.x - x.x);
            var b = ExactMeshBoolean.boundary_bound(new[] { x, y });
            Assert.True(b.Lengths[0] >= 254, $"{b.Lengths[0]}");
            Assert.True(b.Total >= 254);
        }

        [Fact]
        public void BoundaryLengths_AreAtLeastTheExactLength_AndWithinTheEndpointTermOfIt()
        {
            var rnd = new Random(1350);
            int below = 0;
            for (int trial = 0; trial < 3000; ++trial) {
                int bits = rnd.Next(2, 180);
                var X = new BigInteger[3]; var Y = new BigInteger[3];
                for (int k = 0; k < 3; ++k) {
                    X[k] = Random(rnd, rnd.Next(0, bits)) * (rnd.Next(2) == 0 ? 1 : -1);
                    // (a short segment at a large position half of the time: the rounding error of the endpoints is large against it)
                    Y[k] = trial % 2 == 0 ? Random(rnd, rnd.Next(0, bits)) * (rnd.Next(2) == 0 ? 1 : -1) : X[k] + rnd.Next(-300, 300);
                }
                var x = new Vector3d((double)X[0], (double)X[1], (double)X[2]);
                var y = new Vector3d((double)Y[0], (double)Y[1], (double)Y[2]);
                BigInteger sq = 0;
                for (int k = 0; k < 3; ++k) sq += (X[k] - Y[k]) * (X[k] - Y[k]);
                                var b = ExactMeshBoolean.boundary_bound(new[] { x, y });
                double len = b.Lengths[0];
                // at least the exact length (exactly: len^2 >= the sum of squares over BigInteger)
                Assert.True(AtLeastSqrt(len, sq), $"trial {trial}: {len} < the exact length");
                // not much more: the doubles' length, the rounding of the endpoints (E |x|_1 + E |y|_1) and 12 u relative
                double r = Math.Abs(x.x) + Math.Abs(x.y) + Math.Abs(x.z) + Math.Abs(y.x) + Math.Abs(y.y) + Math.Abs(y.z);
                Assert.True(len <= (x.Distance(y) + Math.Pow(2, -52) * r) * (1 + 3e-15), $"trial {trial}: {len} too large");
                // and the factor itself: 10 u over the sum of the doubles' length and the endpoint term (kills 8 or 12 u)
                double t = x.Distance(y) + Math.Pow(2, -52) * r;
                Assert.True(len >= t * (1 + 9 * Math.Pow(2, -53)) && len <= t * (1 + 11 * Math.Pow(2, -53)), $"trial {trial}: factor");
                // the old length x.Distance(y) (1 + 1e-15) was too small for some of these
                if (!AtLeastSqrt(x.Distance(y) * (1 + 1e-15), sq)) below++;
            }
            Assert.True(below > 100, $"only {below} cases where the double endpoints' length is below the exact one");
        }

        // the bounds are built from scale-equivariant operations: under a similarity whose scale is a power of two the lengths
        // scale by it, and the gradient bound (a length over a squared distance, a count over a distance) by its inverse, bit
        // for bit (the others change the grid; nothing to compare)
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void BoundaryAndGradientBounds_ScaleExactlyUnderPowersOfTwo(string name)
        {
            Similarity sim = Similarity.ByName(name);
            if (sim.IsExact == false) return;
            var rnd = new Random(1352);
            var g = new Vector3d[40];
            for (int i = 0; i < g.Length; ++i)
                g[i] = new Vector3d(rnd.Next(-5000, 5000), rnd.Next(-5000, 5000), rnd.Next(-5000, 5000));
            double k = sim.Scale;
            var gs = new Vector3d[g.Length];
            for (int i = 0; i < g.Length; ++i) gs[i] = g[i] * k;
            var b = ExactMeshBoolean.boundary_bound(g);
            var bs = ExactMeshBoolean.boundary_bound(gs);
            for (int t = 0; t < b.Lengths.Length; ++t)
                Assert.Equal(BitConverter.DoubleToInt64Bits(b.Lengths[t] * k), BitConverter.DoubleToInt64Bits(bs.Lengths[t]));
            Assert.Equal(BitConverter.DoubleToInt64Bits(b.Total * k), BitConverter.DoubleToInt64Bits(bs.Total));
            int finite = 0;
            for (int q = 0; q < 50; ++q) {
                double ox = rnd.Next(-9000, 9000), oy = rnd.Next(-9000, 9000), oz = rnd.Next(-9000, 9000), w = 1 + rnd.Next(100);
                double[] box = { ox, oy, oz, ox + w, oy + w, oz + w };
                double[] bx = new double[6];
                for (int i = 0; i < 6; ++i) bx[i] = box[i] * k;
                double a = ExactMeshBoolean.gradient_bound(box, b), c = ExactMeshBoolean.gradient_bound(bx, bs);
                Assert.Equal(BitConverter.DoubleToInt64Bits(a / k), BitConverter.DoubleToInt64Bits(c));
                if (a <= double.MaxValue) finite++;
                // the cluster path: the box in a cluster box twice its size
                double[] cbox = { ox - w, oy - w, oz - w, ox + 2 * w, oy + 2 * w, oz + 2 * w }, cbx = new double[6];
                for (int i = 0; i < 6; ++i) cbx[i] = cbox[i] * k;
                double ac = ExactMeshBoolean.gradient_bound(box, b, ExactMeshBoolean.cluster_bound(cbox, b));
                double cc = ExactMeshBoolean.gradient_bound(bx, bs, ExactMeshBoolean.cluster_bound(cbx, bs));
                Assert.Equal(BitConverter.DoubleToInt64Bits(ac / k), BitConverter.DoubleToInt64Bits(cc));
            }
            Assert.True(finite > 10, $"only {finite} finite bounds: the comparison is mostly infinity = infinity");
        }

        // #1350: far_term with a subnormal result: the quotient is rounded to a multiple of 2^-1074 (an absolute error), which
        // the relative factors do not cover. length = 2^-33, d = 2^510: the true term 2^-33 / (pi 2^1022) = 2^19 / pi eps =
        // 166886.05 eps (eps = 2^-1074), which the plain quotient rounds down to 166886 eps
        [Fact]
        public void FarTerm_WithASubnormalResult_IsNotBelowTheTrueTerm()
        {
            double eps = double.Epsilon;
            double d = Math.Pow(2, 510), length = Math.Pow(2, -33);
            Assert.True(4 * Math.PI * d * d <= double.MaxValue);          // (the main branch)
            double term = ExactMeshBoolean.far_term(length, 1, d);
            Assert.True(term / eps >= 166886.05, $"{term / eps} eps");
            Assert.True(term / eps <= 166886.05 + 3, $"{term / eps} eps: not tight");
            // zero length or count: the true term is 0
            Assert.Equal(0.0, ExactMeshBoolean.far_term(0, 1, d));
            Assert.Equal(0.0, ExactMeshBoolean.far_term(length, 0, d));
            // a normal result is the plain quotient (bit-identical)
            Assert.Equal(1e10 / (4 * Math.PI * d * d), ExactMeshBoolean.far_term(1e10, 1, d));
        }

        // the branches of far_term at their edges (mutation survivors of #1350): q = 4 pi d d exactly double.MaxValue is the main
        // branch (plain quotients), a result of exactly the smallest normal number is not subnormal (no 2 eps), and the overflow
        // branch keeps its eps term where either the length or the count is positive
        [Fact]
        public void FarTerm_AtTheEdgesOfItsBranches()
        {
            double d = 3.782272786141309e+153;                              // fl(fl(4 pi d) d) == double.MaxValue
            Assert.Equal(double.MaxValue, 4 * Math.PI * d * d);
            Assert.Equal(1e300 / double.MaxValue, ExactMeshBoolean.far_term(1e300, 1e300, d));
            // length = fl(pi), d = 2^510: the quotient is exactly 2^-1022 (the smallest normal number): not rounded to a multiple of 2^-1074
            double minNormal = 2.2250738585072014E-308;
            Assert.Equal(minNormal, ExactMeshBoolean.far_term(Math.PI, 1, Math.Pow(2, 510)));
            // the overflow branch (q overflows): a positive length or a positive count gives a positive bound (4 eps at least)
            Assert.Equal(4 * double.Epsilon, ExactMeshBoolean.far_term(1, 0, 1e300));
            Assert.Equal(4 * double.Epsilon, ExactMeshBoolean.far_term(0, 1, 1e300));
            Assert.Equal(0.0, ExactMeshBoolean.far_term(0, 0, 1e300));
        }

        static BigInteger Random(Random rnd, int bits)
        {
            var bytes = new byte[bits / 8 + 2];
            rnd.NextBytes(bytes);
            bytes[bytes.Length - 1] = 0;
            BigInteger v = new BigInteger(bytes);
            return bits == 0 ? BigInteger.Zero : v & ((BigInteger.One << bits) - 1) | (BigInteger.One << (bits - 1));
        }

        // the sum of the lengths of 10^6 segments, each just below half an ulp of the first (which is 1): every addition
        // rounds to the first length, the exact sum is 1 + 10^-10 larger, which the final (1 + 1e-12) did not cover
        [Fact]
        public void GradientBound_CoversTheRoundingOfAMillionTermSum()
        {
            const int ns = 1000000;
            double h = 127.0 * Math.Pow(2, -60);                // 2^-53 - 2^-60: below half an ulp of 1
            var g = new Vector3d[2 * ns];
            g[0] = new Vector3d(0, 0, 0); g[1] = new Vector3d(1, 0, 0);
            for (int i = 1; i < ns; ++i) { g[2 * i] = new Vector3d(0, 0, 0); g[2 * i + 1] = new Vector3d(h, 0, 0); }
            var b = ExactMeshBoolean.boundary_bound(g);
            // the exact sum of the exact lengths 1 and (ns - 1) h, in units of 2^-60, rounded up to a double
            BigInteger units = (BigInteger.One << 60) + 127 * (ns - 1);
            double exactUp = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits((double)units / Math.Pow(2, 60)) + 1);
            Assert.True(exactUp > b.Total * (1 + 1e-11), "the sum does not round down here: the test is vacuous");
            double[] box = { 100, 100, 100, 101, 101, 101 };
            double s = Math.Max(b.Size, 3 * 101), slack = 1e-12 * s;
            double d = ExactMeshBoolean.box_distance(box, 0, b.All, 0) - slack;
            Assert.True(d >= 1e-3 * s);
            double reference = ExactMeshBoolean.far_term(exactUp, ns, d);       // the term of the exact total length at that distance
            double g1 = ExactMeshBoolean.gradient_bound(box, b);
            Assert.True(g1 >= reference, $"{g1} < {reference}");
            Assert.True(g1 <= reference * (1 + 1e-9), "not tight");
        }

    }
}
