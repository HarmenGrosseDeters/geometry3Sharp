using System;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Exact orientation predicates and the exact triangle-triangle intersection test built on them.
    /// </summary>
    public class ExactPredicatesTests
    {
        // reference: exact determinant of integer coordinates
        static int Orient2dInt(long[] a, long[] b, long[] c)
        {
            BigInteger d = (new BigInteger(b[0]) - a[0]) * (new BigInteger(c[1]) - a[1]) - (new BigInteger(b[1]) - a[1]) * (new BigInteger(c[0]) - a[0]);
            return d.Sign;
        }
        static int Orient3dInt(long[] a, long[] b, long[] c, long[] d)
        {
            BigInteger[] u = Enumerable.Range(0, 3).Select(k => new BigInteger(b[k]) - a[k]).ToArray();
            BigInteger[] v = Enumerable.Range(0, 3).Select(k => new BigInteger(c[k]) - a[k]).ToArray();
            BigInteger[] w = Enumerable.Range(0, 3).Select(k => new BigInteger(d[k]) - a[k]).ToArray();
            BigInteger det = (u[1] * v[2] - u[2] * v[1]) * w[0] + (u[2] * v[0] - u[0] * v[2]) * w[1] + (u[0] * v[1] - u[1] * v[0]) * w[2];
            return det.Sign;
        }
        static Vector3d V(long[] p) { return new Vector3d(p[0], p[1], p[2]); }

        [Fact]
        public void Orient_MatchesExactIntegerArithmetic_NearDegenerate()
        {
            // points on or next to a line / plane, far from the origin: naive floating point gets signs wrong
            var rnd = new Random(1);
            long off = 1L << 40;
            int naiveWrong = 0;
            for (int i = 0; i < 20000; ++i) {
                long[] a = { off + rnd.Next(1000), off + rnd.Next(1000), off + rnd.Next(1000) };
                long[] dir = { rnd.Next(1, 1 << 26), rnd.Next(1, 1 << 26), rnd.Next(1, 1 << 26) };
                long[] b = { a[0] + dir[0], a[1] + dir[1], a[2] + dir[2] };
                long t = rnd.Next(1, 1 << 20);     // c nearly on the line a-b: products beyond 2^53
                long[] c = { a[0] + t * dir[0] + rnd.Next(-1, 2), a[1] + t * dir[1] + rnd.Next(-1, 2), a[2] + t * dir[2] + rnd.Next(-1, 2) };
                long[] d = { a[0] + 3 * dir[0] + c[0] - a[0] + rnd.Next(-1, 2), a[1] + 3 * dir[1] + c[1] - a[1], a[2] + 3 * dir[2] + c[2] - a[2] + rnd.Next(-1, 2) };

                int e2 = Orient2dInt(a, b, c);
                Assert.Equal(e2, ExactPredicates.Orient2d(V(a).xy, V(b).xy, V(c).xy));
                int e3 = Orient3dInt(a, b, c, d);
                Assert.Equal(e3, ExactPredicates.Orient3d(V(a), V(b), V(c), V(d)));

                Vector3d n = (V(b) - V(a)).Cross(V(c) - V(a));
                if (Math.Sign(n.Dot(V(d) - V(a))) != e3) naiveWrong++;
            }
            Assert.True(naiveWrong > 0, "test does not exercise the exact path");
        }

        [Fact]
        public void Orient_TinyCoordinates()
        {
            Vector3d a = new Vector3d(1e-300, 0, 0), b = new Vector3d(0, 1e-300, 0), c = new Vector3d(0, 0, 1e-300);
            Assert.Equal(1, ExactPredicates.Orient3d(a, b, c, Vector3d.Zero) * ExactPredicates.Orient3d(a, b, c, Vector3d.Zero));
            Assert.Equal(0, ExactPredicates.Orient3d(a, b, c, new Vector3d(1e-300, 0, 0)));
            Assert.NotEqual(ExactPredicates.Orient3d(a, b, c, Vector3d.Zero), ExactPredicates.Orient3d(a, b, c, new Vector3d(1e-300, 1e-300, 1e-300)));
        }


        // --- ported from the predicate tests of the Triangle.NET fork (RobustPredicatesTest, Robustness/PredicatesTest) ---

        [Fact]
        public void Orient2d_Basic()
        {
            Vector2d a = new Vector2d(-1, 0), b = new Vector2d(0, 1);
            Assert.True(ExactPredicates.Orient2d(a, b, new Vector2d(1, 0)) < 0);
            Assert.True(ExactPredicates.Orient2d(a, b, new Vector2d(0, 2)) > 0);
            Assert.Equal(0, ExactPredicates.Orient2d(a, b, new Vector2d(1, 2)));
        }

        [Fact]
        public void InCircle_Basic()
        {
            Vector2d a = new Vector2d(-1, 0), b = new Vector2d(0, 1), c = new Vector2d(1, 0);
            // a, b, c are clockwise here: as in Triangle.NET, the sign is reversed for clockwise input
            Assert.True(ExactPredicates.InCircle(a, b, c, new Vector2d(0, 0.5)) < 0);
            Assert.True(ExactPredicates.InCircle(a, b, c, new Vector2d(0, 1.5)) > 0);
            Assert.Equal(0, ExactPredicates.InCircle(a, b, c, new Vector2d(0, 1.0)));
            Assert.True(ExactPredicates.InCircle(c, b, a, new Vector2d(0, 0.5)) > 0);
        }

        [Fact]
        public void NearlyCollinearPointsOfDifferentMagnitude_NoErrorsAndExact()
        {
            var random = new Random(1);
            for (int i = 0; i < 100000; i++) {
                // points on a line with tiny perturbations and coordinates of very different magnitude
                double scale = Math.Pow(2, random.Next(-60, 60));
                double ax = (random.NextDouble() - 0.5) * scale, ay = (random.NextDouble() - 0.5) * scale;
                double dx = random.NextDouble() - 0.5, dy = random.NextDouble() - 0.5;
                double t1 = random.NextDouble() * Math.Pow(2, random.Next(-40, 40));
                double t2 = random.NextDouble() * Math.Pow(2, random.Next(-40, 40));
                Vector2d a = new Vector2d(ax, ay), b = new Vector2d(ax + t1 * dx, ay + t1 * dy);
                Vector2d c = new Vector2d(ax + t2 * dx + Math.Pow(2, random.Next(-80, 0)), ay + t2 * dy);
                Vector2d d = new Vector2d(ax - t2 * dy, ay + t1 * dx);
                Assert.Equal(Reference.Orient2d(a, b, c), ExactPredicates.Orient2d(a, b, c));
                Assert.Equal(Reference.InCircle(a, b, c, d), ExactPredicates.InCircle(a, b, c, d));
            }
        }

        [Fact]
        public void InfiniteCoordinates_Throw()
        {
            Vector2d inf = new Vector2d(double.PositiveInfinity, double.PositiveInfinity);
            Assert.Throws<ArgumentException>(() => ExactPredicates.Orient2d(inf, new Vector2d(-1, 1), new Vector2d(1, 1)));
            Assert.Throws<ArgumentException>(() => ExactPredicates.Orient3d(new Vector3d(double.NaN, 0, 0), Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ));
        }

        // independent exact reference: rational arithmetic on the exact values of the doubles
        static class Reference
        {
            // v = num / 2^1074 exactly
            static BigInteger Exact(double v)
            {
                long bits = BitConverter.DoubleToInt64Bits(v);
                int e = (int)((bits >> 52) & 0x7FF);
                long m = bits & 0xFFFFFFFFFFFFFL;
                BigInteger r = (e == 0) ? new BigInteger(m) : new BigInteger(m | (1L << 52)) << (e - 1);
                return bits < 0 ? -r : r;
            }
            public static int Orient2d(Vector2d a, Vector2d b, Vector2d c)
            {
                BigInteger ax = Exact(a.x), ay = Exact(a.y);
                return ((Exact(b.x) - ax) * (Exact(c.y) - ay) - (Exact(b.y) - ay) * (Exact(c.x) - ax)).Sign;
            }
            public static int InCircle(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
            {
                BigInteger dx = Exact(d.x), dy = Exact(d.y);
                BigInteger adx = Exact(a.x) - dx, ady = Exact(a.y) - dy, bdx = Exact(b.x) - dx, bdy = Exact(b.y) - dy, cdx = Exact(c.x) - dx, cdy = Exact(c.y) - dy;
                BigInteger det = (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy) + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy) + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
                return det.Sign;
            }
        }

        static Triangle3d T(Vector3d a, Vector3d b, Vector3d c) { return new Triangle3d(a, b, c); }

        // next representable double above v
        static double NextUp(double v)
        {
            if (v == 0) return double.Epsilon;
            long bits = BitConverter.DoubleToInt64Bits(v);
            return BitConverter.Int64BitsToDouble(v > 0 ? bits + 1 : bits - 1);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(632000, 5712000, 100)]
        public void TriTri_TouchingAndSeparatedByOneUlp(double ox, double oy, double oz)
        {
            Vector3d o = new Vector3d(ox, oy, oz);
            Triangle3d a = T(o + new Vector3d(0, 0, 0), o + new Vector3d(10, 0, 0), o + new Vector3d(0, 10, 0));
            // vertex of b exactly on a's face
            Triangle3d touch = T(o + new Vector3d(2, 2, 0), o + new Vector3d(3, 2, 5), o + new Vector3d(2, 3, 5));
            Assert.True(ExactTriangleIntersection.Intersects(ref a, ref touch));
            // the same, lifted by one ulp
            double z = NextUp(oz);
            Triangle3d above = T(new Vector3d(ox + 2, oy + 2, z), o + new Vector3d(3, 2, 5), o + new Vector3d(2, 3, 5));
            Assert.False(ExactTriangleIntersection.Intersects(ref a, ref above));
            // sharing an edge, folded
            Triangle3d edge = T(o + new Vector3d(10, 0, 0), o + new Vector3d(0, 10, 0), o + new Vector3d(5, 5, 7));
            Assert.True(ExactTriangleIntersection.Intersects(ref a, ref edge));
            // coplanar, overlapping
            Triangle3d cop = T(o + new Vector3d(1, 1, 0), o + new Vector3d(20, 1, 0), o + new Vector3d(1, 20, 0));
            Assert.True(ExactTriangleIntersection.Intersects(ref a, ref cop));
            // coplanar, touching along the hypotenuse
            Triangle3d copTouch = T(o + new Vector3d(10, 0, 0), o + new Vector3d(10, 10, 0), o + new Vector3d(0, 10, 0));
            Assert.True(ExactTriangleIntersection.Intersects(ref a, ref copTouch));
            // coplanar, one ulp away from the hypotenuse
            double x = NextUp(ox + 10);
            Triangle3d copApart = T(new Vector3d(x, oy, oz), new Vector3d(x, oy + 10, oz), new Vector3d(NextUp(ox), oy + 10, oz));
            Assert.False(ExactTriangleIntersection.Intersects(ref a, ref copApart));
        }

        [Fact]
        public void TriTri_RandomDegenerateConfigurations_AgreeWithEdgeBasedTest()
        {
            // small integer coordinates: many coplanar, collinear, touching and degenerate cases
            var rnd = new Random(7);
            Func<Vector3d> P = () => new Vector3d(rnd.Next(4), rnd.Next(4), rnd.Next(3));
            int hits = 0;
            for (int i = 0; i < 20000; ++i) {
                Triangle3d a = T(P(), P(), P()), b = T(P(), P(), P());
                bool fast = ExactTriangleIntersection.Intersects(ref a, ref b);
                bool reference = ExactTriangleIntersection.IntersectsByEdges(ref a, ref b);
                Assert.True(fast == reference, $"{a.V0} {a.V1} {a.V2} / {b.V0} {b.V1} {b.V2}: {fast} vs {reference}");
                if (fast) hits++;
            }
            Assert.InRange(hits, 2000, 18000);
        }

        [Fact]
        public void TriTri_GeneralPosition_AgreesWithFloatingPointTest()
        {
            var rnd = new Random(3);
            Func<Vector3d> P = () => new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
            for (int i = 0; i < 20000; ++i) {
                Triangle3d a = T(P(), P(), P()), b = T(P(), P(), P());
                Assert.Equal(IntrTriangle3Triangle3.Intersects(ref a, ref b), ExactTriangleIntersection.Intersects(ref a, ref b));
            }
        }
    }
}
