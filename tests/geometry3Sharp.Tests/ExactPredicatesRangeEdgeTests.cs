using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The range of the expansion arithmetic, <c>Expansion.InRange</c> ([2^-100, 2^100], math/ExactPredicates.cs): its
    /// edges, and nearly degenerate inputs with full mantissas on both sides of them and far outside, against the
    /// BigInteger determinants. Inside the range AdaptivePredicates decides; outside it the generated kernels do. A range
    /// widened for speed sends inputs whose stage D products or tails underflow to the expansions: under the mutant H6 the
    /// cocircular rectangles at 2^-220 fail (InCircle +1 for exactly cocircular points); the other rows are regressions of
    /// the two paths at the edges, which that mutant does not break (docs/qa/exact-bound-inventory.md, gap G2: the hand mutant H6, MinMagnitude 2^-100 to 2^-250, survived
    /// every predicate test; tools/Z3Pilot/adaptive_range.py proves the range the expansions need).
    /// </summary>
    public class ExactPredicatesRangeEdgeTests
    {
        static double Pow2(int e) { return BitConverter.Int64BitsToDouble((long)(e + 1023) << 52); }

        // the next double away from (up) or towards (down) zero, for finite nonzero x (Math.BitIncrement is not in
        // .NET Framework)
        static double Next(double x, bool up)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(up ? bits + 1 : bits - 1);
        }

        static double Ulps(double x, int k) { return Next(x, k > 0); }

        static bool InRange1(double x) { return Expansion.InRange(x, 0, 0, 0, 0, 0); }

        [Fact]
        public void InRange_IsClosedAtTwoToTheMinusAndPlus100()
        {
            double lo = Pow2(-100), hi = Pow2(100);
            foreach (double s in new[] { 1.0, -1.0 }) {
                Assert.True(InRange1(s * lo));
                Assert.True(InRange1(s * hi));
                Assert.False(InRange1(s * Next(lo, false)));
                Assert.False(InRange1(s * Next(hi, true)));
            }
            Assert.True(InRange1(0.0));
            Assert.True(InRange1(-0.0));
            Assert.False(InRange1(double.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => InRange1(double.NaN));
        }

        // Exactly degenerate configurations whose differences are not exact doubles (the generator of
        // ExactPredicatesTests.StageD_ExactlyDegenerate_WithInexactDifferences), scaled by 2^scale: multiples 2^k u of one
        // vector are collinear with 0, multiples of u and v coplanar, collinear points cocircular. Exactly 0, and the
        // exact sign one ulp away. Near and beyond the edges of the range the rounding errors of the differences and their
        // degree-4 products are what an underflowing or overflowing expansion loses.
        [Theory]
        [InlineData(-255)]
        [InlineData(-250)]
        [InlineData(-210)]
        [InlineData(-190)]
        [InlineData(-150)]
        [InlineData(-101)]
        [InlineData(-100)]
        [InlineData(-99)]
        [InlineData(97)]
        [InlineData(100)]
        [InlineData(101)]
        [InlineData(150)]
        [InlineData(190)]
        [InlineData(250)]
        public void NearlyDegenerate_AtAndBeyondTheRangeEdges_MatchBigInteger(int scale)
        {
            var rnd = new Random(1000 + scale);
            double s = Pow2(scale);
            int inRange = 0;
            Func<Vector3d, int, Vector3d> at = (w, k) => (s * Pow2(k)) * w;
            for (int i = 0; i < 400; ++i) {
                var u = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                var v = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                Vector3d a = at(u, rnd.Next(-2, 3)), b = at(v, rnd.Next(-2, 3)), c = at(u, rnd.Next(-2, 3)), d = at(v, rnd.Next(-2, 3));
                Vector2d a2 = a.xy, b2 = at(u, rnd.Next(-2, 3)).xy, c2 = c.xy, d2 = at(u, rnd.Next(-2, 3)).xy;
                if (Expansion.InRange(a.x, a.y, a.z, b.x, b.y, b.z) && Expansion.InRange(c.x, c.y, c.z, d.x, d.y, d.z))
                    ++inRange;

                Assert.Equal(ExactPredicates.orient2d_bigint(a2, b2, c2), ExactPredicates.Orient2d(a2, b2, c2));
                Assert.Equal(ExactPredicates.orient3d_bigint(a, b, c, d), ExactPredicates.Orient3d(a, b, c, d));
                Assert.Equal(ExactPredicates.incircle_bigint(a2, b2, c2, d2), ExactPredicates.InCircle(a2, b2, c2, d2));

                int k = rnd.Next(2) == 0 ? 1 : -1;
                c2 = new Vector2d(Ulps(c2.x, k), c2.y);
                d = new Vector3d(d.x, d.y, Ulps(d.z, k));
                d2 = new Vector2d(d2.x, Ulps(d2.y, k));
                Assert.Equal(ExactPredicates.orient2d_bigint(a2, b2, c2), ExactPredicates.Orient2d(a2, b2, c2));
                Assert.Equal(ExactPredicates.orient3d_bigint(a, b, c, d), ExactPredicates.Orient3d(a, b, c, d));
                Assert.Equal(ExactPredicates.incircle_bigint(a2, b2, c2, d2), ExactPredicates.InCircle(a2, b2, c2, d2));
            }
            // the cases inside the range reach the expansions (the generator is meant to straddle the edges)
            if (scale >= -97 && scale <= 97)
                Assert.Equal(400, inRange);
        }

        // The corners of an axis-parallel rectangle are exactly cocircular, also after rounding (two x and two y values).
        // With coordinates of both signs and full mantissas the differences are not exact doubles, and the degree-4
        // products of their rounding errors are what InCircle's stage D needs: at 2^-250 they lie below 2^-1074, at 2^250
        // the lifted products above 2^1023. Exactly 0, and the exact sign with one corner moved by one ulp.
        [Theory]
        [InlineData(-255)]
        [InlineData(-250)]
        [InlineData(-220)]
        [InlineData(-190)]
        [InlineData(-150)]
        [InlineData(-100)]
        [InlineData(0)]
        [InlineData(100)]
        [InlineData(150)]
        [InlineData(190)]
        [InlineData(220)]
        [InlineData(250)]
        public void CocircularRectangles_AtAndBeyondTheRangeEdges_MatchBigInteger(int scale)
        {
            var rnd = new Random(2000 + scale);
            double s = Pow2(scale);
            Func<double> pos = () => s * Pow2(rnd.Next(-3, 1)) * (1 + rnd.NextDouble());
            for (int i = 0; i < 300; ++i) {
                double x1 = pos(), x2 = -pos(), y1 = pos(), y2 = -pos();
                var p = new[] { new Vector2d(x1, y1), new Vector2d(x2, y1), new Vector2d(x2, y2), new Vector2d(x1, y2) };
                // a random order of the corners, so that every corner is the reference point d of InCircle
                for (int k = 3; k > 0; --k) { int j = rnd.Next(k + 1); var t = p[k]; p[k] = p[j]; p[j] = t; }
                Assert.Equal(0, ExactPredicates.incircle_bigint(p[0], p[1], p[2], p[3]));
                Assert.Equal(0, ExactPredicates.InCircle(p[0], p[1], p[2], p[3]));

                int c = rnd.Next(4);
                p[c] = rnd.Next(2) == 0 ? new Vector2d(Ulps(p[c].x, rnd.Next(2) * 2 - 1), p[c].y)
                                        : new Vector2d(p[c].x, Ulps(p[c].y, rnd.Next(2) * 2 - 1));
                Assert.Equal(ExactPredicates.incircle_bigint(p[0], p[1], p[2], p[3]), ExactPredicates.InCircle(p[0], p[1], p[2], p[3]));
                Assert.Equal(ExactPredicates.orient2d_bigint(p[0], p[1], p[2]), ExactPredicates.Orient2d(p[0], p[1], p[2]));
            }
        }
    }
}
