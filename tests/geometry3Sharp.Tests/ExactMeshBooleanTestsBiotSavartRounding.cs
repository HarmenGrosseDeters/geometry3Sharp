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
            BigInteger x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
            while (true) {
                BigInteger y = (x + n / x) >> 1;
                if (y >= x) return x;
                x = y;
            }
        }

        // the endpoints are exact grid integers X, Y; x = (double)X, y = (double)Y, as antipodal_grid converts them: the
        // length of the segment XY is at most what boundary_bound stores, which is not much more
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
                BigInteger floor = Isqrt(sq), exactCeil = floor * floor == sq ? floor : floor + 1;
                var b = ExactMeshBoolean.boundary_bound(new[] { x, y });
                double len = b.Lengths[0];
                // at least the exact length: ceil(len) >= ceil(exact), and for an integer-valued len strictly the real length
                Assert.True((BigInteger)Math.Ceiling(len) >= exactCeil, $"trial {trial}: {len} < {exactCeil}");
                // not much more: the doubles' length, the rounding of the endpoints (E |x|_1 + E |y|_1) and 12 u relative
                double r = Math.Abs(x.x) + Math.Abs(x.y) + Math.Abs(x.z) + Math.Abs(y.x) + Math.Abs(y.y) + Math.Abs(y.z);
                Assert.True(len <= (x.Distance(y) + Math.Pow(2, -52) * r) * (1 + 3e-15), $"trial {trial}: {len} too large");
                // the old length x.Distance(y) (1 + 1e-15) was too small for some of these
                if (x.Distance(y) * (1 + 1e-15) < (double)exactCeil - 0.5) below++;
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
            Assert.Equal(b.SumFactor, bs.SumFactor);
            for (int q = 0; q < 50; ++q) {
                double ox = rnd.Next(-9000, 9000), oy = rnd.Next(-9000, 9000), oz = rnd.Next(-9000, 9000), w = 1 + rnd.Next(100);
                double[] box = { ox, oy, oz, ox + w, oy + w, oz + w };
                double[] bx = new double[6];
                for (int i = 0; i < 6; ++i) bx[i] = box[i] * k;
                double a = ExactMeshBoolean.gradient_bound(box, b), c = ExactMeshBoolean.gradient_bound(bx, bs);
                Assert.Equal(BitConverter.DoubleToInt64Bits(a / k), BitConverter.DoubleToInt64Bits(c));
            }
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
