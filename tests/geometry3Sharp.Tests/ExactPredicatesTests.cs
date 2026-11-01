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


        [Fact]
        public void Orient3d_SubnormalProducts()
        {
            // products of three coordinates around 1e-106 are subnormal: the relative error bound of the filter
            // does not hold there
            Vector3d a = Vector3d.Zero;
            Vector3d b = new Vector3d(5.410462796616011e-106, 7.923489689955754e-107, 7.205795578410991e-106);
            Vector3d c = new Vector3d(-5.356477438739708e-106, 2.754332637527401e-107, 9.04934776536539e-106);
            Vector3d d = new Vector3d(8.646047251538728e-107, 5.74104474308333e-107, 7.984152883833546e-106);
            Assert.Equal(1, ExactPredicates.Orient3d(a, b, c, d));
        }

        [Fact]
        public void Orient2d_SubnormalProducts()
        {
            // exactly collinear (multiples 2^k u of one vector, scaled by 1e-160): the products are subnormal, the
            // filter's relative error bound rounds to 0 while the rounding errors of the differences leave a
            // determinant of one subnormal unit; the filter must not decide
            Vector2d a = new Vector2d(1.0773023667125954E-154, 3.2828707925443306E-155);
            Vector2d b = new Vector2d(6.421222488359185E-162, 1.9567434743310992E-162);
            Vector2d c = new Vector2d(9.568367136059978E-170, 2.9157749926017212E-170);
            Assert.Equal(0, ExactPredicates.orient2d_bigint(a, b, c));
            Assert.Equal(0, ExactPredicates.Orient2d(a, b, c));

            var rnd = new Random(31);
            for (int i = 0; i < 20000; ++i) {
                Vector2d u = new Vector2d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                Vector2d[] p = new Vector2d[3];
                for (int k = 0; k < 3; ++k)
                    p[k] = (Math.Pow(2, rnd.Next(-40, 41)) * u) * 1e-160;
                Assert.Equal(ExactPredicates.orient2d_bigint(p[0], p[1], p[2]), ExactPredicates.Orient2d(p[0], p[1], p[2]));
            }
        }

        [Fact]
        public void Predicates_SubnormalPartialProducts_TimesLargeFactors()
        {
            // the permanents are far above the underflow limit, but 2x2 products are subnormal (absolute rounding
            // errors) and multiplied by large coordinates or lifts: 1.6 and 1.4 units of 2^-1074 round to 2 and 1,
            // the computed minor is 1 unit instead of 0.2, and a term of -0.5 units flips the sign
            double s = Math.Pow(2, -537);
            Vector3d a = Vector3d.Zero, b = new Vector3d(0, s, s), c = new Vector3d(1, 1.4 * s, 1.6 * s), d = new Vector3d(Math.Pow(2, 600), -Math.Pow(2, 62), 0);
            Assert.Equal(-1, ExactPredicates.orient3d_bigint(a, b, c, d));
            Assert.Equal(-1, ExactPredicates.Orient3d(a, b, c, d));

            double X = Math.Pow(2, 210), unit = double.Epsilon;
            Vector2d p = new Vector2d(X, -8.93 * X * X * unit), q = new Vector2d(0.8, 2 * unit), r = new Vector2d(0.7, 2 * unit);
            Assert.Equal(-1, ExactPredicates.incircle_bigint(p, q, r, Vector2d.Zero));
            Assert.Equal(-1, ExactPredicates.InCircle(p, q, r, Vector2d.Zero));

            Vector3d[] e = {
                new Vector3d(-1.533029625205798E-241, -2.6234477870025E-113, -1.11634E-319),
                new Vector3d(5.42385483877849E-239, 5.359945562533891E-39, -3.323151427154333E+113),
                new Vector3d(1.4195398889734518E-169, 8.85220279967162E-187, 3.050922758200515E-157),
                new Vector3d(5.677733035144346E-209, -0.001472248891108203, 3.664814583832122E-210),
                new Vector3d(5.443689989319026E-232, -2.1100996564666743E-113, -2.8027266704634148E-273) };
            Assert.Equal(1, ExactPredicates.insphere_bigint(e[0], e[1], e[2], e[3], e[4]));
            Assert.Equal(1, ExactPredicates.InSphere(e[0], e[1], e[2], e[3], e[4]));

            // random coordinates of independent magnitudes (2^-1080 to 2^400), against the integer arithmetic
            var rnd = new Random(5);
            Func<double> coord = () => (rnd.NextDouble() - 0.5) * Math.Pow(2, rnd.Next(-1080, 400));
            for (int i = 0; i < 100000; ++i) {
                Vector3d[] v = new Vector3d[5];
                for (int k = 0; k < 5; ++k)
                    v[k] = new Vector3d(coord(), coord(), coord());
                Assert.Equal(ExactPredicates.orient3d_bigint(v[0], v[1], v[2], v[3]), ExactPredicates.Orient3d(v[0], v[1], v[2], v[3]));
                Assert.Equal(ExactPredicates.orient2d_bigint(v[0].xy, v[1].xy, v[2].xy), ExactPredicates.Orient2d(v[0].xy, v[1].xy, v[2].xy));
                Assert.Equal(ExactPredicates.incircle_bigint(v[0].xy, v[1].xy, v[2].xy, v[3].xy), ExactPredicates.InCircle(v[0].xy, v[1].xy, v[2].xy, v[3].xy));
                if (i % 10 == 0)
                    Assert.Equal(ExactPredicates.insphere_bigint(v[0], v[1], v[2], v[3], v[4]), ExactPredicates.InSphere(v[0], v[1], v[2], v[3], v[4]));
            }
        }

        [Fact]
        public void Predicates_NearDegenerate_ScaledIntoTheSubnormalRange()
        {
            // the integer near-degenerate cases, scaled by powers of two (exact): the signs must not change
            var rnd = new Random(7);
            double s3 = Math.Pow(2, -390), s2 = Math.Pow(2, -300);
            for (int i = 0; i < 5000; ++i) {
                long[] a = { rnd.Next(1 << 20), rnd.Next(1 << 20), rnd.Next(1 << 20) };
                long[] dir = { rnd.Next(1, 1 << 20), rnd.Next(1, 1 << 20), rnd.Next(1, 1 << 20) };
                long[] b = { a[0] + dir[0], a[1] + dir[1], a[2] + dir[2] };
                long[] c = { a[0] + rnd.Next(-1 << 20, 1 << 20), a[1] + rnd.Next(-1 << 20, 1 << 20), a[2] + rnd.Next(-1 << 20, 1 << 20) };
                // d nearly in the plane a, b, c
                long[] d = { b[0] + c[0] - a[0] + rnd.Next(-1, 2), b[1] + c[1] - a[1] + rnd.Next(-1, 2), b[2] + c[2] - a[2] + rnd.Next(-1, 2) };
                Assert.Equal(Orient3dInt(a, b, c, d), ExactPredicates.Orient3d(s3 * V(a), s3 * V(b), s3 * V(c), s3 * V(d)));
                // in-circle: d next to the circle through a, b, c (a, b, c, d on a circle of integer points)
                long r = rnd.Next(1, 1 << 15);
                long[][] p = { new long[] { r, 0 }, new long[] { 0, r }, new long[] { -r, 0 }, new long[] { 0, -r + rnd.Next(-1, 2) } };
                Vector2d[] q = p.Select(x => s2 * new Vector2d(x[0], x[1])).ToArray();
                Assert.Equal(InCircleInt(p[0], p[1], p[2], p[3]), ExactPredicates.InCircle(q[0], q[1], q[2], q[3]));
            }
        }

        [Fact]
        public void Orient_ExpansionPath_CoplanarAndNearCoplanar_AtManyScales()
        {
            // integer points (exact as doubles) on / next to a plane, scaled by powers of two from 2^-250 to
            // 2^250: the expansion arithmetic (used within [1e-90, 1e90]) and the BigInteger path (outside)
            // must both give the integer sign; coplanar points must give exactly 0
            var rnd = new Random(11);
            for (int i = 0; i < 4000; ++i) {
                long[] a = { rnd.Next(-1 << 25, 1 << 25), rnd.Next(-1 << 25, 1 << 25), rnd.Next(-1 << 25, 1 << 25) };
                long[] u = { rnd.Next(-1 << 12, 1 << 12), rnd.Next(-1 << 12, 1 << 12), rnd.Next(-1 << 12, 1 << 12) };
                long[] v = { rnd.Next(-1 << 12, 1 << 12), rnd.Next(-1 << 12, 1 << 12), rnd.Next(-1 << 12, 1 << 12) };
                long s1 = rnd.Next(-1000, 1000), t1 = rnd.Next(-1000, 1000), s2 = rnd.Next(-1000, 1000), t2 = rnd.Next(-1000, 1000);
                long[] b = { a[0] + u[0], a[1] + u[1], a[2] + u[2] };
                long[] c = { a[0] + v[0], a[1] + v[1], a[2] + v[2] };
                int off = (i % 3) - 1;      // -1, 0 (coplanar), +1
                long[] d = { a[0] + s1 * u[0] + t1 * v[0], a[1] + s1 * u[1] + t1 * v[1] + off, a[2] + s1 * u[2] + t1 * v[2] };
                int expected = Orient3dInt(a, b, c, d);
                if (off == 0) Assert.Equal(0, expected);
                double scale = Math.Pow(2, rnd.Next(-250, 250));
                Assert.Equal(expected, ExactPredicates.Orient3d(scale * V(a), scale * V(b), scale * V(c), scale * V(d)));
                Assert.Equal(Orient2dInt(a, b, d), ExactPredicates.Orient2d(scale * V(a).xy, scale * V(b).xy, scale * V(d).xy));
            }
        }

        [Fact]
        public void TwoProduct_IsExact()
        {
            // Dekker's split (no FMA, so the same on every target framework): a * b == x + y exactly
            var rnd = new Random(5);
            for (int i = 0; i < 100000; ++i) {
                double a = (rnd.NextDouble() - 0.5) * Math.Pow(10, rnd.Next(-90, 90));
                double b = (rnd.NextDouble() - 0.5) * Math.Pow(10, rnd.Next(-90, 90));
                double x, y;
                Expansion.TwoProduct(a, b, out x, out y);
                Assert.Equal(a * b, x);
                Assert.True(ExactEquals(x, y, a, b), $"{a} * {b}: {x} + {y}");
            }
        }

        // x + y == a * b exactly (doubles as mantissa * 2^exponent in BigInteger; net48 has no FMA)
        static bool ExactEquals(double x, double y, double a, double b)
        {
            int ex, ey, ea, eb;
            BigInteger mx = Decompose(x, out ex), my = Decompose(y, out ey), ma = Decompose(a, out ea), mb = Decompose(b, out eb);
            int e = Math.Min(Math.Min(ex, ey), ea + eb);
            return (mx << (ex - e)) + (my << (ey - e)) == (ma * mb) << (ea + eb - e);
        }

        static BigInteger Decompose(double d, out int exponent)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            exponent = exp - 1075;
            return (bits < 0) ? -new BigInteger(mant) : new BigInteger(mant);
        }

        // a few ulps away from x
        static double Ulps(double x, Random rnd, int k)
        {
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + rnd.Next(-k, k + 1));
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1e6)]
        [InlineData(1e-12)]
        public void AdaptiveStages_DecideNearlyDegenerateCasesCorrectly(double scale)
        {
            // points a few ulps off a line / plane / circle: the floating-point filter cannot decide them; the
            // adaptive stages give the exact sign, stages B and C already for most of them (D is rarely needed),
            // and stage D alone (without the error bounds of B and C) as well
            var rnd = new Random(17);
            int n = 20000;
            int[] reachedD = new int[3];
            for (int i = 0; i < n; ++i) {
                Vector2d a = scale * new Vector2d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                Vector2d b = scale * new Vector2d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                double t = rnd.NextDouble();
                Vector2d c = new Vector2d(Ulps(a.x + t * (b.x - a.x), rnd, 4), Ulps(a.y + t * (b.y - a.y), rnd, 4));
                int e2 = ExactPredicates.orient2d_bigint(a, b, c);
                int before = AdaptivePredicates.StageDCount;
                Assert.Equal(e2, AdaptivePredicates.Orient2d(a.x, a.y, b.x, b.y, c.x, c.y));
                reachedD[0] += AdaptivePredicates.StageDCount - before;
                Assert.Equal(e2, AdaptivePredicates.Orient2dStageD(a.x, a.y, b.x, b.y, c.x, c.y));

                Vector3d p = scale * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                Vector3d q = scale * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                Vector3d r = scale * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                double u = rnd.NextDouble(), v = rnd.NextDouble();
                Vector3d w = p + u * (q - p) + v * (r - p);
                w = new Vector3d(Ulps(w.x, rnd, 4), Ulps(w.y, rnd, 4), Ulps(w.z, rnd, 4));
                int e3 = -ExactPredicates.orient3d_bigint(p, q, r, w);     // (Shewchuk's sign)
                before = AdaptivePredicates.StageDCount;
                Assert.Equal(e3, AdaptivePredicates.Orient3d(p.x, p.y, p.z, q.x, q.y, q.z, r.x, r.y, r.z, w.x, w.y, w.z));
                reachedD[1] += AdaptivePredicates.StageDCount - before;
                Assert.Equal(e3, AdaptivePredicates.Orient3dStageD(p.x, p.y, p.z, q.x, q.y, q.z, r.x, r.y, r.z, w.x, w.y, w.z));

                Vector2d center = scale * new Vector2d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                double rad = scale * (0.1 + rnd.NextDouble());
                Vector2d[] cp = new Vector2d[4];
                for (int k = 0; k < 4; ++k) {
                    double th = rnd.NextDouble() * 2 * Math.PI;
                    cp[k] = new Vector2d(Ulps(center.x + rad * Math.Cos(th), rnd, 4), Ulps(center.y + rad * Math.Sin(th), rnd, 4));
                }
                int ec = ExactPredicates.incircle_bigint(cp[0], cp[1], cp[2], cp[3]);
                before = AdaptivePredicates.StageDCount;
                Assert.Equal(ec, AdaptivePredicates.InCircle(cp[0].x, cp[0].y, cp[1].x, cp[1].y, cp[2].x, cp[2].y, cp[3].x, cp[3].y));
                reachedD[2] += AdaptivePredicates.StageDCount - before;
                Assert.Equal(ec, AdaptivePredicates.InCircleStageD(cp[0].x, cp[0].y, cp[1].x, cp[1].y, cp[2].x, cp[2].y, cp[3].x, cp[3].y));
            }
            for (int k = 0; k < 3; ++k)
                Assert.True(reachedD[k] < 0.1 * n, $"predicate {k}: stage D needed {reachedD[k]} times");
        }

        // u scaled by a power of two (exact), within the range of the expansion arithmetic
        static Vector3d Pow2(Random rnd, Vector3d u) { return Math.Pow(2, rnd.Next(-40, 41)) * u; }

        [Fact]
        public void StageD_ExactlyDegenerate_WithInexactDifferences()
        {
            // multiples 2^k u of one vector are exactly collinear (with 0), multiples of u and v exactly coplanar,
            // and collinear points exactly cocircular (the lifted points lie on a parabola in a vertical plane).
            // The differences of such points are not exact doubles: the rounding errors and their products
            // decide, which only stage D evaluates. Exactly 0, and the exact sign one ulp away.
            var rnd = new Random(23);
            int reachedD = 0;
            for (int i = 0; i < 5000; ++i) {
                Vector3d u = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                Vector3d v = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                Vector3d a = Pow2(rnd, u), b = Pow2(rnd, v), c = Pow2(rnd, u), d = Pow2(rnd, v);
                Vector2d a2 = a.xy, b2 = Pow2(rnd, u).xy, c2 = c.xy, d2 = Pow2(rnd, u).xy;
                int before = AdaptivePredicates.StageDCount;

                Assert.Equal(0, ExactPredicates.Orient2d(a2, b2, c2));
                Assert.Equal(0, AdaptivePredicates.Orient2dStageD(a2.x, a2.y, b2.x, b2.y, c2.x, c2.y));
                Assert.Equal(0, ExactPredicates.Orient3d(a, b, c, d));
                Assert.Equal(0, AdaptivePredicates.Orient3dStageD(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z));
                Assert.Equal(0, ExactPredicates.InCircle(a2, b2, c2, d2));
                Assert.Equal(0, AdaptivePredicates.InCircleStageD(a2.x, a2.y, b2.x, b2.y, c2.x, c2.y, d2.x, d2.y));

                // one coordinate one ulp away
                c2 = new Vector2d(Ulps(c2.x, rnd, 1), c2.y);
                d = new Vector3d(d.x, d.y, Ulps(d.z, rnd, 1));
                d2 = new Vector2d(d2.x, Ulps(d2.y, rnd, 1));
                int e2 = ExactPredicates.orient2d_bigint(a2, b2, c2);
                Assert.Equal(e2, ExactPredicates.Orient2d(a2, b2, c2));
                Assert.Equal(e2, AdaptivePredicates.Orient2dStageD(a2.x, a2.y, b2.x, b2.y, c2.x, c2.y));
                int e3 = ExactPredicates.orient3d_bigint(a, b, c, d);
                Assert.Equal(e3, ExactPredicates.Orient3d(a, b, c, d));
                Assert.Equal(-e3, AdaptivePredicates.Orient3dStageD(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z));
                int ec = ExactPredicates.incircle_bigint(a2, b2, c2, d2);
                Assert.Equal(ec, ExactPredicates.InCircle(a2, b2, c2, d2));
                Assert.Equal(ec, AdaptivePredicates.InCircleStageD(a2.x, a2.y, b2.x, b2.y, c2.x, c2.y, d2.x, d2.y));
                reachedD += AdaptivePredicates.StageDCount - before;
            }
            Assert.True(reachedD > 5000, $"stage D reached {reachedD} times");
        }

        // Gaussian integers a + b i with prime norms 1 mod 4 (and 13 two more times): their products have the norm
        // N in [2^105, 2^106), so the integer points with x^2 + y^2 = N have coordinates below 2^53 (exact doubles),
        // up to 2^53 apart: many of their differences are not exact doubles
        static readonly int[,] GaussianPrimes = { { 1, 2 }, { 2, 3 }, { 1, 4 }, { 2, 5 }, { 1, 6 }, { 4, 5 }, { 2, 7 }, { 5, 6 }, { 3, 8 },
            { 5, 8 }, { 4, 9 }, { 1, 10 }, { 3, 10 }, { 7, 8 }, { 4, 11 }, { 7, 10 }, { 6, 11 }, { 2, 3 }, { 2, 3 } };

        // the rounding error of a - b
        static double DiffTail(double a, double b)
        {
            double x = a - b, bv = a - x, av = x + bv;
            return (a - av) + (bv - b);
        }

        static Vector2d PointOnBigCircle(Random rnd)
        {
            BigInteger x = BigInteger.One, y = BigInteger.Zero;
            for (int k = 0; k < GaussianPrimes.GetLength(0); ++k) {
                int a = GaussianPrimes[k, 0], b = (rnd.Next(2) == 0) ? GaussianPrimes[k, 1] : -GaussianPrimes[k, 1];
                BigInteger nx = x * a - y * b, ny = x * b + y * a;
                x = nx; y = ny;
            }
            Vector2d p = new Vector2d((double)x, (double)y);
            if (rnd.Next(2) == 0) p = new Vector2d(p.y, p.x);
            return new Vector2d(rnd.Next(2) == 0 ? p.x : -p.x, rnd.Next(2) == 0 ? p.y : -p.y);
        }

        [Fact]
        public void StageD_Cocircular_WithInexactDifferences()
        {
            // four integer points on one circle (not collinear): exactly cocircular, but the rounding errors of the
            // differences, also their squares in the lifts, decide; exactly 0, and the exact sign one ulp away
            var rnd = new Random(29);
            int inexact = 0;
            for (int i = 0; i < 3000; ++i) {
                Vector2d[] q = { PointOnBigCircle(rnd), PointOnBigCircle(rnd), PointOnBigCircle(rnd), PointOnBigCircle(rnd) };
                Assert.Equal(0, ExactPredicates.incircle_bigint(q[0], q[1], q[2], q[3]));
                for (int k = 0; k < 3; ++k)
                    if (DiffTail(q[k].x, q[3].x) != 0 || DiffTail(q[k].y, q[3].y) != 0) inexact++;
                Assert.Equal(0, ExactPredicates.InCircle(q[0], q[1], q[2], q[3]));
                Assert.Equal(0, AdaptivePredicates.InCircleStageD(q[0].x, q[0].y, q[1].x, q[1].y, q[2].x, q[2].y, q[3].x, q[3].y));

                int j = rnd.Next(4);
                q[j] = new Vector2d(Ulps(q[j].x, rnd, 1), q[j].y);
                int e = ExactPredicates.incircle_bigint(q[0], q[1], q[2], q[3]);
                Assert.Equal(e, ExactPredicates.InCircle(q[0], q[1], q[2], q[3]));
                Assert.Equal(e, AdaptivePredicates.InCircleStageD(q[0].x, q[0].y, q[1].x, q[1].y, q[2].x, q[2].y, q[3].x, q[3].y));
            }
            Assert.True(inexact > 1000, $"{inexact} inexact differences");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void StageD_MatchesBigInteger_HardValues(int kind)
        {
            // values of very different magnitudes, few mantissa bits, and exactly degenerate combinations: stage D
            // alone gives the exact sign whenever the coordinates are in its range
            var rand = new Random(2000 + kind);
            int checkedCases = 0;
            for (int i = 0; i < 4000; ++i) {
                Func<double> h = () => Hard(rand, kind);
                Vector3d a = new Vector3d(h(), h(), h()), b = new Vector3d(h(), h(), h()), c = new Vector3d(h(), h(), h()), d = new Vector3d(h(), h(), h());
                if (i % 4 == 1) d = a + (b - a) * 2 + (c - a) * 3;
                if (i % 4 == 2) c = a;
                if (Expansion.InRange(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z) == false)
                    continue;
                checkedCases++;
                Assert.Equal(-ExactPredicates.orient3d_bigint(a, b, c, d),
                    AdaptivePredicates.Orient3dStageD(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z));
                Assert.Equal(ExactPredicates.orient2d_bigint(a.xy, b.xy, c.xy), AdaptivePredicates.Orient2dStageD(a.x, a.y, b.x, b.y, c.x, c.y));
                Assert.Equal(ExactPredicates.incircle_bigint(a.xy, b.xy, c.xy, d.xy),
                    AdaptivePredicates.InCircleStageD(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y));
            }
            Assert.True(checkedCases > 100, $"{checkedCases} cases in range");
        }

        [Fact]
        public void AdaptiveStages_ExactDifferences_DecideDegenerateCases()
        {
            // on an integer grid the coordinate differences are exact: stage B is the exact value, also 0
            var rnd = new Random(3);
            for (int i = 0; i < 5000; ++i) {
                int x0 = rnd.Next(-1000, 1000), y0 = rnd.Next(-1000, 1000), z0 = rnd.Next(-1000, 1000);
                int dx = rnd.Next(-9, 10), dy = rnd.Next(-9, 10), dz = rnd.Next(-9, 10), t1 = rnd.Next(1, 5), t2 = rnd.Next(-5, 9);
                Assert.Equal(0, AdaptivePredicates.Orient2d(x0, y0, x0 + t1 * dx, y0 + t1 * dy, x0 + t2 * dx, y0 + t2 * dy));
                // the plane z = z0 + x + 2y
                Func<int, int, Vector3d> pl = (x, y) => new Vector3d(x, y, z0 + x + 2 * y);
                Vector3d p = pl(x0, y0), q = pl(x0 + dx, y0 + dy), r = pl(x0 + dy, y0 - dx), w = pl(x0 + t2, y0 - t1);
                Assert.Equal(0, AdaptivePredicates.Orient3d(p.x, p.y, p.z, q.x, q.y, q.z, r.x, r.y, r.z, w.x, w.y, w.z));
                // the circle of radius 5 around (x0, y0)
                Assert.Equal(0, AdaptivePredicates.InCircle(x0 + 5, y0, x0 + 3, y0 + 4, x0 - 4, y0 + 3, x0, y0 - 5));
                // just off the circle
                Assert.Equal(-1, AdaptivePredicates.InCircle(x0 + 5, y0, x0 + 3, y0 + 4, x0 - 4, y0 + 3, x0, y0 - 5.000000000001));
            }
        }

        static int InCircleInt(long[] a, long[] b, long[] c, long[] d)
        {
            BigInteger adx = a[0] - d[0], ady = a[1] - d[1], bdx = b[0] - d[0], bdy = b[1] - d[1], cdx = c[0] - d[0], cdy = c[1] - d[1];
            BigInteger det = (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy) + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
                + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
            return det.Sign;
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


        // hard inputs: grid points (exactly collinear / coplanar / cocircular), the same perturbed by a few ulps,
        // large coordinates with small differences, mixed magnitudes, and values outside the expansion range
        static double Hard(Random rand, int kind)
        {
            switch (kind) {
                case 0: return rand.Next(-4, 5);
                case 1: { double v = rand.Next(-4, 5) * 0.25; for (int k = rand.Next(4); k > 0; --k) v = (rand.Next(2) == 0) ? NextUp(v) : NextDown(v); return v; }
                case 2: return 5e6 + rand.Next(-8, 9) * 0.125 + ((rand.Next(3) == 0) ? 1e-9 * rand.Next(-3, 4) : 0);
                case 3: return rand.NextDouble() * Math.Pow(10, rand.Next(-20, 21)) * (rand.Next(2) == 0 ? -1 : 1);
                default: return rand.Next(-3, 4) * Math.Pow(2, rand.Next(-140, 140));
            }
        }

        static double NextDown(double v) { return -NextUp(-v); }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void ExpansionArithmetic_MatchesBigInteger(int kind)
        {
            var rand = new Random(1000 + kind);
            int nonzero = 0;
            for (int i = 0; i < 4000; ++i) {
                Func<double> h = () => Hard(rand, kind);
                Vector3d a = new Vector3d(h(), h(), h()), b = new Vector3d(h(), h(), h()), c = new Vector3d(h(), h(), h()), d = new Vector3d(h(), h(), h());
                // also exactly degenerate configurations from the same values
                if (i % 4 == 1) d = a + (b - a) * 2 + (c - a) * 3;
                if (i % 4 == 2) c = a;
                int o3 = ExactPredicates.orient3d_bigint(a, b, c, d);
                Assert.Equal(o3, ExactPredicates.Orient3dExact(a, b, c, d));
                Assert.Equal(o3, ExactPredicates.Orient3d(a, b, c, d));
                Assert.Equal(ExactPredicates.orient2d_bigint(a.xy, b.xy, c.xy), ExactPredicates.Orient2dExact(a.xy, b.xy, c.xy));
                int ic = ExactPredicates.incircle_bigint(a.xy, b.xy, c.xy, d.xy);
                Assert.Equal(ic, ExactPredicates.InCircleExact(a.xy, b.xy, c.xy, d.xy));
                Assert.Equal(ic, ExactPredicates.InCircle(a.xy, b.xy, c.xy, d.xy));
                if (o3 != 0) nonzero++;
            }
            Assert.True(nonzero > 100);
        }

        [Fact]
        public void ExpansionArithmetic_Cocircular()
        {
            // points on the circle x^2 + y^2 = 25 (integer and scaled): incircle exactly 0
            var circle = new[] { new Vector2d(5, 0), new Vector2d(3, 4), new Vector2d(0, 5), new Vector2d(-4, 3), new Vector2d(-5, 0), new Vector2d(4, -3) };
            foreach (double scale in new[] { 1.0, 0.1, 1e-20, 1e20, 3.0 / 7.0 }) {
                for (int k = 0; k + 3 < circle.Length; ++k) {
                    Vector2d a = circle[k] * scale, b = circle[k + 1] * scale, c = circle[k + 2] * scale, d = circle[k + 3] * scale;
                    Assert.Equal(ExactPredicates.incircle_bigint(a, b, c, d), ExactPredicates.InCircleExact(a, b, c, d));
                }
            }
            Assert.Equal(0, ExactPredicates.InCircleExact(circle[0], circle[1], circle[2], circle[3]));
        }
    }
}
