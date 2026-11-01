using System;
using System.Linq;
using System.Numerics;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The "never steeper" oracle (docs/design/TERRAIN.md 5.4, 7 "oracle tests"): Slope.IsNotSteeper and
    /// Slope.IsSupportedBy against hand-computed cases and against an independent rational reference.
    /// </summary>
    public class TerrainSlopeOracleTests
    {
        // ---- independent reference: exact rationals as (numerator, denominator) BigIntegers ----

        struct Q
        {
            public BigInteger N, D;    // D > 0
            public Q(BigInteger n, BigInteger d) { if (d.Sign < 0) { n = -n; d = -d; } N = n; D = d; }
            public static Q Of(double x)
            {
                if (x == 0) return new Q(0, 1);
                long bits = BitConverter.DoubleToInt64Bits(x);
                int exp = (int)((bits >> 52) & 0x7FF);
                long frac = bits & 0xFFFFFFFFFFFFFL;
                if (exp == 0) exp = 1; else frac |= 1L << 52;
                BigInteger m = bits < 0 ? -frac : frac;
                int e = exp - 1075;
                return e >= 0 ? new Q(m << e, 1) : new Q(m, BigInteger.One << -e);
            }
            public static Q operator +(Q a, Q b) => new Q(a.N * b.D + b.N * a.D, a.D * b.D);
            public static Q operator -(Q a, Q b) => new Q(a.N * b.D - b.N * a.D, a.D * b.D);
            public static Q operator *(Q a, Q b) => new Q(a.N * b.N, a.D * b.D);
            public int Sign => N.Sign;
        }

        // reference: slope^2 <= limit^2, limit = num / den given as rationals
        static bool RefNotSteeper(Vector3d a, Vector3d b, Vector3d c, Q limit)
        {
            Q ux = Q.Of(b.x) - Q.Of(a.x), uy = Q.Of(b.y) - Q.Of(a.y), uz = Q.Of(b.z) - Q.Of(a.z);
            Q vx = Q.Of(c.x) - Q.Of(a.x), vy = Q.Of(c.y) - Q.Of(a.y), vz = Q.Of(c.z) - Q.Of(a.z);
            Q nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (nz.Sign == 0) return false;
            return (limit * limit * nz * nz - (nx * nx + ny * ny)).Sign >= 0;
        }

        static Q RefLimit(SlopeSpec spec)
        {
            switch (spec.Kind) {
                case SlopeSpec.Kinds.Ratio: { Q n = Q.Of(spec.Value); return new Q(n.D, n.N); }
                case SlopeSpec.Kinds.Percent: { Q p = Q.Of(spec.Value); return new Q(p.N, p.D * 100); }
                default: return Q.Of(spec.S);
            }
        }

        static double NextUp(double x)
        {
            if (x == 0) return double.Epsilon;
            long b = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(x > 0 ? b + 1 : b - 1);
        }
        static double NextDown(double x) => -NextUp(-x);
        static Vector3d Up(Vector3d p) => new Vector3d(p.x, p.y, NextUp(p.z));
        static Vector3d Down(Vector3d p) => new Vector3d(p.x, p.y, NextDown(p.z));
        static void Tri(DMesh3 m, int t, out Vector3d a, out Vector3d b, out Vector3d c)
        {
            a = b = c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
        }

        // ---- hand-computed cases ----

        [Fact]
        public void Ratio_ExactlyAtTheSlope_AndOneUlpSteeper()
        {
            // rises 1 over a run of 3: exactly 1:3
            Vector3d a = new Vector3d(0, 0, 0), b = new Vector3d(3, 0, 0), c = new Vector3d(0, 3, 1);
            Assert.True(Slope.IsNotSteeper(a, b, c, SlopeSpec.Ratio(3)));
            Assert.False(Slope.IsNotSteeper(a, b, Up(c), SlopeSpec.Ratio(3)));
            Assert.True(Slope.IsNotSteeper(a, b, Down(c), SlopeSpec.Ratio(3)));
            // the double S of 1:3 is below 1/3, so Rise(S) rejects the exact 1:3 triangle: the ratio is checked as entered
            double s = SlopeSpec.Ratio(3).S;
            Assert.True((RefLimit(SlopeSpec.Ratio(3)) - Q.Of(s)).Sign > 0);   // 1/3 is not a double
            Assert.False(Slope.IsNotSteeper(a, b, c, SlopeSpec.Rise(s)));
            // orientation does not matter
            Assert.True(Slope.IsNotSteeper(a, c, b, SlopeSpec.Ratio(3)));
            Assert.True(Slope.IsNotSteeper(c, b, a, SlopeSpec.Ratio(3)));
        }

        [Fact]
        public void Percent_ExactlyAtTheSlope()
        {
            Vector3d a = new Vector3d(10, 20, 5), b = new Vector3d(12, 20, 5), c = new Vector3d(10, 22, 6);   // 50 %
            Assert.True(Slope.IsNotSteeper(a, b, c, SlopeSpec.Percent(50)));
            Assert.False(Slope.IsNotSteeper(a, b, Up(c), SlopeSpec.Percent(50)));
            Assert.False(Slope.IsNotSteeper(a, b, c, SlopeSpec.Percent(49.999999999)));
            // 0.1 % is not a double ratio: the check is against p as entered
            Vector3d d = new Vector3d(0, 0, 0), e = new Vector3d(1000, 0, 0), f = new Vector3d(0, 1000, 1);
            Assert.True(Slope.IsNotSteeper(d, e, f, SlopeSpec.Percent(0.1)));
            Assert.Equal(RefNotSteeper(d, e, Up(f), RefLimit(SlopeSpec.Percent(0.1))), Slope.IsNotSteeper(d, e, Up(f), SlopeSpec.Percent(0.1)));
        }

        [Fact]
        public void Degrees_AgainstTheDoubleTangent()
        {
            Assert.False(SlopeSpec.Degrees(45).ExactlyCheckable);
            Assert.True(SlopeSpec.Ratio(1).ExactlyCheckable && SlopeSpec.Percent(100).ExactlyCheckable);
            // slope exactly 1: tan(45 deg) in doubles is 0.9999999999999999, so the check against the double fails
            Vector3d a = new Vector3d(0, 0, 0), b = new Vector3d(1, 0, 0), c = new Vector3d(0, 1, 1);
            Assert.Equal(SlopeSpec.Degrees(45).S >= 1, Slope.IsNotSteeper(a, b, c, SlopeSpec.Degrees(45)));
            Assert.True(Slope.IsNotSteeper(a, b, c, SlopeSpec.Ratio(1)));
        }

        [Fact]
        public void Gradient_IsTheFallLine_InAnyDirection()
        {
            // plane z = 0.3 x + 0.4 y has slope 0.5 whatever the triangle
            Func<double, double, Vector3d> p = (x, y) => new Vector3d(x, y, 0.3 * x + 0.4 * y);
            Vector3d a = p(0, 0), b = p(10, 0), c = p(0, 10);
            Assert.True(Slope.IsNotSteeper(a, b, c, SlopeSpec.Rise(0.5000001)));
            Assert.False(Slope.IsNotSteeper(a, b, c, SlopeSpec.Rise(0.4999999)));
            Assert.False(Slope.IsNotSteeper(a, b, c, SlopeSpec.Rise(0.4)));     // steeper than its steepest axis component
        }

        [Fact]
        public void Degenerate_And_Invalid_AreRejected()
        {
            Vector3d a = new Vector3d(0, 0, 0), b = new Vector3d(1, 1, 0), c = new Vector3d(2, 2, 0);
            Assert.False(Slope.IsNotSteeper(a, b, c, SlopeSpec.Ratio(1)));      // collinear in XY, even if flat
            Assert.False(Slope.IsNotSteeper(a, a, a, SlopeSpec.Ratio(1)));
            Vector3d d = new Vector3d(1, 0, 0), e = new Vector3d(0, 1, 0);
            Assert.True(Slope.IsNotSteeper(a, d, e, SlopeSpec.Ratio(1)));        // flat
            foreach (var bad in new[] { SlopeSpec.Ratio(0), SlopeSpec.Ratio(-1), SlopeSpec.Ratio(double.NaN), SlopeSpec.Rise(double.PositiveInfinity), SlopeSpec.Degrees(90) }) {
                Assert.False(bad.IsValid);
                Assert.True(double.IsNaN(bad.S));
                Assert.False(Slope.IsNotSteeper(a, d, e, bad));
            }
            Assert.False(Slope.IsNotSteeper(a, d, new Vector3d(0, 1, double.NaN), SlopeSpec.Ratio(1)));
        }

        [Fact]
        public void S_IsNeverSteeperThanTheSpec()
        {
            var rnd = new Random(5);
            for (int i = 0; i < 2000; ++i) {
                double n = Math.Exp(rnd.NextDouble() * 10 - 5), p = Math.Exp(rnd.NextDouble() * 10 - 3);
                Assert.True(new Q(1, 1).Sign > 0);
                Q sr = Q.Of(SlopeSpec.Ratio(n).S), lr = RefLimit(SlopeSpec.Ratio(n));
                Assert.True((lr - sr).Sign >= 0);
                Assert.True((lr - Q.Of(NextUp(SlopeSpec.Ratio(n).S))).Sign < 0);    // the largest such double
                Q sp = Q.Of(SlopeSpec.Percent(p).S), lp = RefLimit(SlopeSpec.Percent(p));
                Assert.True((lp - sp).Sign >= 0);
                Assert.True((lp - Q.Of(NextUp(SlopeSpec.Percent(p).S))).Sign < 0);
            }
        }

        // ---- against the rational reference ----

        // triangles near their limit (a plane with gradient of magnitude about s), perturbed by a few ulp, at many
        // magnitudes: the oracle agrees with the reference on every one
        [Theory]
        [InlineData(1.0, 0.0)]
        [InlineData(1.0, 632117.25)]
        [InlineData(1e-3, 5712043.5)]
        [InlineData(1e-150, 0)]
        [InlineData(1e150, 1e160)]
        [InlineData(1e-300, 0)]       // subnormal differences
        [InlineData(1e100, 0)]        // products beyond the double range
        public void RandomNearTheLimit_AgreesWithRationals(double scale, double offset)
        {
            var rnd = new Random(11);
            var specs = new[] { SlopeSpec.Ratio(3), SlopeSpec.Ratio(1.5), SlopeSpec.Percent(70), SlopeSpec.Rise(0.731), SlopeSpec.Degrees(35) };
            int agree = 0, notSteeper = 0, steeper = 0;
            for (int i = 0; i < 3000; ++i) {
                SlopeSpec spec = specs[i % specs.Length];
                double s = spec.S, phi = rnd.NextDouble() * 2 * Math.PI;
                double gx = s * Math.Cos(phi), gy = s * Math.Sin(phi);
                Func<double, double, Vector3d> p = (x, y) => new Vector3d(offset + scale * x, offset + scale * y, scale * (gx * x + gy * y));
                Vector3d a = p(rnd.NextDouble(), rnd.NextDouble()), b = p(rnd.NextDouble(), rnd.NextDouble()), c = p(rnd.NextDouble(), rnd.NextDouble());
                int k = rnd.Next(-3, 4);
                for (int j = 0; j < Math.Abs(k); ++j) c = k > 0 ? Up(c) : Down(c);
                bool r = RefNotSteeper(a, b, c, RefLimit(spec));
                Assert.Equal(r, Slope.IsNotSteeper(a, b, c, spec));
                agree++;
                if (r) notSteeper++; else steeper++;
            }
            // both answers occur, so the cases are near the limit
            Assert.True(notSteeper > 100 && steeper > 100, $"{notSteeper} / {steeper}");
        }

        [Fact]
        public void IsSupportedBy_HandCases()
        {
            // toe from (0,0,10) to (10,0,10), slope 1:2 rising in +y: the plane z = 10 + y / 2
            Vector3d p0 = new Vector3d(0, 0, 10), p1 = new Vector3d(10, 0, 10);
            Vector3d a = new Vector3d(0, 4, 12), b = new Vector3d(10, 4, 12), c = new Vector3d(5, 8, 14);
            Assert.True(Slope.IsSupportedBy(a, b, c, p0, p1, SlopeSpec.Ratio(2)));
            // the same triangle 1 ulp higher: its plane passes above the toe
            Assert.False(Slope.IsSupportedBy(Up(a), Up(b), Up(c), p0, p1, SlopeSpec.Ratio(2)));
            // lowered: still supported (below the cones)
            Assert.True(Slope.IsSupportedBy(Down(a), Down(b), Down(c), p0, p1, SlopeSpec.Ratio(2)));
            // not steeper, but far above the toe: not supported
            Vector3d off = new Vector3d(0, 0, 1);
            Assert.True(Slope.IsNotSteeper(a + off, b + off, c + off, SlopeSpec.Ratio(2)));
            Assert.False(Slope.IsSupportedBy(a + off, b + off, c + off, p0, p1, SlopeSpec.Ratio(2)));
            // on the toe: the toe triangle itself
            Assert.True(Slope.IsSupportedBy(p0, p1, c, p0, p1, SlopeSpec.Ratio(2)));
            // a single apex (p0 = p1): a cone tangent plane through (0,0,10) in direction (1,1)/sqrt 2
            Vector3d q = new Vector3d(0, 0, 10);
            Assert.True(Slope.IsSupportedBy(q, new Vector3d(-5, 5, 10), new Vector3d(4, 4, 10 + 0.5 * Math.Sqrt(32) * 0.999), q, q, SlopeSpec.Ratio(2)));
        }

        [Fact]
        public void IsSupportedBy_MatchesTheDefinition_Random()
        {
            // the reference: slope by rationals, plane at the toe points by rationals
            var rnd = new Random(17);
            int both = 0;
            for (int i = 0; i < 2000; ++i) {
                Vector3d p0 = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                Vector3d p1 = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                Func<Vector3d> r = () => new Vector3d(rnd.NextDouble() * 3, rnd.NextDouble() * 3, rnd.NextDouble() * 3);
                Vector3d a = r(), b = r(), c = r();
                var spec = SlopeSpec.Rise(1 + rnd.NextDouble() * 3);
                bool slope = RefNotSteeper(a, b, c, RefLimit(spec));
                bool below = RefPlaneBelow(a, b, c, p0) && RefPlaneBelow(a, b, c, p1);
                Assert.Equal(slope && below, Slope.IsSupportedBy(a, b, c, p0, p1, spec));
                if (slope && below) both++;
            }
            Assert.True(both > 50);
        }

        // plane of (a, b, c) at p's XY <= p.z, by rationals: z = a.z - (nx (x - ax) + ny (y - ay)) / nz
        static bool RefPlaneBelow(Vector3d a, Vector3d b, Vector3d c, Vector3d p)
        {
            Q ux = Q.Of(b.x) - Q.Of(a.x), uy = Q.Of(b.y) - Q.Of(a.y), uz = Q.Of(b.z) - Q.Of(a.z);
            Q vx = Q.Of(c.x) - Q.Of(a.x), vy = Q.Of(c.y) - Q.Of(a.y), vz = Q.Of(c.z) - Q.Of(a.z);
            Q nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (nz.Sign == 0) return false;
            // p.z - plane(p) = ((p.z - a.z) nz + nx (px - ax) + ny (py - ay)) / nz
            Q t = (Q.Of(p.z) - Q.Of(a.z)) * nz + nx * (Q.Of(p.x) - Q.Of(a.x)) + ny * (Q.Of(p.y) - Q.Of(a.y));
            return t.Sign * nz.Sign >= 0;
        }

        // ---- on existing results: TerrainGreedyTIN ----

        // On every triangle of a greedy TIN of a wavy terrain at UTM, the oracle at the triangle's own slope (as a
        // double, rounded up and down) agrees with the rational reference, and the steepest triangle is found.
        [Fact]
        public void GreedyTIN_EveryTriangle_AgreesWithRationals()
        {
            int nx = 61, ny = 47;
            var z = new double[nx * ny];
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) z[i + nx * j] = 8 * Math.Sin(i / 7.0) * Math.Cos(j / 5.0) + 0.3 * i;
            var g = new TerrainGreedyTIN(z, nx, ny, 326000.5, 5641000.5, 1) { MaxError = 0.05 };
            Assert.True(g.Compute());
            DMesh3 tin = g.Result;
            double steepest = 0;
            int checks = 0;
            foreach (int tid in tin.TriangleIndices()) {
                Vector3d a, b, c;
                Tri(tin, tid, out a, out b, out c);
                Vector3d n = (b - a).Cross(c - a);
                double s = Math.Sqrt(n.x * n.x + n.y * n.y) / Math.Abs(n.z);
                steepest = Math.Max(steepest, s);
                foreach (double lim in new[] { s, NextUp(s), NextDown(s), s * (1 + 1e-12), s * (1 - 1e-12) }) {
                    Assert.Equal(RefNotSteeper(a, b, c, Q.Of(lim)), Slope.IsNotSteeper(a, b, c, SlopeSpec.Rise(lim)));
                    checks++;
                }
                Assert.True(Slope.IsNotSteeper(a, b, c, SlopeSpec.Rise(s * (1 + 1e-12))));
                Assert.False(Slope.IsNotSteeper(a, b, c, SlopeSpec.Rise(s * (1 - 1e-12))));
            }
            // the whole TIN against its steepest triangle: not steeper than that, steeper than slightly less
            Assert.True(tin.TriangleIndices().All(t => { Vector3d a, b, c; Tri(tin, t, out a, out b, out c); return Slope.IsNotSteeper(a, b, c, SlopeSpec.Rise(steepest * (1 + 1e-12))); }));
            Assert.False(tin.TriangleIndices().All(t => { Vector3d a, b, c; Tri(tin, t, out a, out b, out c); return Slope.IsNotSteeper(a, b, c, SlopeSpec.Rise(steepest * (1 - 1e-12))); }));
            Assert.True(checks > 1000);
        }
    }
}
