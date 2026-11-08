using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The corner of the smallest angle of a triangle is the one opposite its exactly shortest edge. The oracle takes the exactly shortest
    /// edge (SpansSmallInputAngle), so a corner chosen from rounded products of squared lengths disagreed at near-180 degree triangles
    /// whose two short edges differ by less than a rounding error.
    /// </summary>
    public class QualityMesh2CornerChoiceTests
    {
        static readonly double Eps = Math.Pow(2, -30);

        [Fact]
        public void ShortestEdgeCorner_TwoShortEdgesDifferingBelowARounding_TakesTheExactlyShorterOne()
        {
            // |ab|^2 = 1, |bc|^2 = 1 + 2^-60 (rounds to 1), |ca|^2 = 4 + 2^-60: ab is the shortest, opposite corner c = 2.
            // In doubles the products at a and at c tie, and the first (a, opposite bc) used to win
            var a = new Vector2d(0, 0); var b = new Vector2d(1, 0); var c = new Vector2d(2, Eps);
            Assert.Equal(2, QualityMesh2.Refiner.ShortestEdgeCorner(a, b, c));
            // the order of the vertices reversed: c, b, a: the corner of c is now 0
            Assert.Equal(0, QualityMesh2.Refiner.ShortestEdgeCorner(c, b, a));
        }

        [Fact]
        public void ShortestEdgeCorner_ExactTie_TakesTheFirstCorner()
        {
            Assert.Equal(0, QualityMesh2.Refiner.ShortestEdgeCorner(new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(1, 2)));
            Assert.Equal(0, QualityMesh2.Refiner.ShortestEdgeCorner(new Vector2d(0, 1), new Vector2d(1, 0), new Vector2d(-1, 0)));
        }

        [Fact]
        public void ShortestEdgeCorner_MatchesIntegers_OnNearTies()
        {
            var rng = new Random(11);
            int near = 0;
            for (int i = 0; i < 20000; ++i) {
                long R() => rng.Next(-(1 << 26), 1 << 26);
                long ax = R(), ay = R(), bx = R(), by = R();
                // c near a point at the same distance from b as a is from b... a long thin triangle: c = b + (b - a) + a small offset
                long cx = 2 * bx - ax + rng.Next(-3, 4), cy = 2 * by - ay + rng.Next(-3, 4);
                if (Math.Abs(cx) >= (1 << 26) || Math.Abs(cy) >= (1 << 26)) continue;
                var l = new System.Numerics.BigInteger[3];
                long[] x = { ax, bx, cx }, y = { ay, by, cy };
                for (int k = 0; k < 3; ++k) {
                    long dx = x[(k + 1) % 3] - x[(k + 2) % 3], dy = y[(k + 1) % 3] - y[(k + 2) % 3];
                    l[k] = (System.Numerics.BigInteger)dx * dx + (System.Numerics.BigInteger)dy * dy;
                }
                int want = 0; if (l[1] < l[want]) want = 1; if (l[2] < l[want]) want = 2;
                if (l[0] == l[1] || l[1] == l[2] || l[0] == l[2]) ++near;
                int got = QualityMesh2.Refiner.ShortestEdgeCorner(new Vector2d(ax, ay), new Vector2d(bx, by), new Vector2d(cx, cy));
                Assert.Equal(want, got);
            }
            Assert.True(near > 0);
        }

        [Theory]
        [InlineData(-10)] [InlineData(-1)] [InlineData(1)] [InlineData(10)] [InlineData(100)] [InlineData(-100)]
        public void ShortestEdgeCorner_IsInvariantUnderPowerOfTwoScales(int e)
        {
            double s = Math.Pow(2, e);
            Vector2d[][] cases = {
                new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(2, Eps) },
                new[] { new Vector2d(2, Eps), new Vector2d(1, 0), new Vector2d(0, 0) },
                new[] { new Vector2d(0.1, 0.3), new Vector2d(0.7, 0.2), new Vector2d(1.3, 0.1) },
            };
            foreach (var t in cases) {
                int want = QualityMesh2.Refiner.ShortestEdgeCorner(t[0], t[1], t[2]);
                Assert.Equal(want, QualityMesh2.Refiner.ShortestEdgeCorner(t[0] * s, t[1] * s, t[2] * s));
            }
        }
    }
}
