using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Oracle for the limit and extreme-scale paths of <c>boundary_edge_angle_x</c> (issue #1427, item 1): the exact angle of the edge
    /// (a, b) seen from the perturbed point p_eps = (p.x + eps3, p.y + eps1, p.z + eps2), 0 &lt; eps3 &lt;&lt; eps2 &lt;&lt; eps1 (the
    /// definition in the header of ExactWindingCache), computed in integers over the grid 2^-1074 with real, tiny eps. It covers
    /// every path of the function at once: the vertex limit (p == a, p == b), the open-edge limit, the points on the +x ray, the
    /// branch cut, and the rescaled paths (rescale_diff_2d, scaled_d_factor, rescale_pow2) for coordinates of any exponent, mixed
    /// exponents between x and yz, and differences that overflow. The perturbation is the definition, so the oracle needs no
    /// special cases and no zone where the function hands over to the exact predicates. The class name starts with
    /// ExactWindingCacheInternalTests so that the mutation shard's filter (exact-winding) runs it.
    /// </summary>
    public class ExactWindingCacheInternalTestsPerturbedOracle
    {
        static BigInteger Grid(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            bool neg = bits < 0;
            int e = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) e = 1; else m |= 1L << 52;
            BigInteger v = new BigInteger(m) << (e - 1);
            return neg ? -v : v;
        }

        static int BitsOf(BigInteger m)       // m >= 0
        {
            byte[] bytes = m.ToByteArray();
            int top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0) --top;
            int n = top * 8, t = bytes[top];
            while (t != 0) { ++n; t >>= 1; }
            return n;
        }

        static bool Finite(double x) { return (x - x) == 0.0; }

        static int BitLength(BigInteger v) { return v.IsZero ? 0 : BitsOf(BigInteger.Abs(v)); }

        static BigInteger ISqrt(BigInteger n)
        {
            if (n.Sign <= 0) return BigInteger.Zero;
            BigInteger x = BigInteger.One << ((BitLength(n) + 1) / 2);
            while (true) {
                BigInteger y = (x + n / x) >> 1;
                if (y >= x) return x;
                x = y;
            }
        }

        /// <summary>
        /// The exact perturbed angle; false when the perturbation cannot be made much smaller than the smallest nonzero difference
        /// at the spacing that the ratio of the largest to the smallest difference needs (eps1 &gt;&gt; eps2 &gt;&gt; eps3 must stay
        /// infinitesimal against every ratio of the differences, or the finite stand-in decides a case that the limit decides otherwise).
        /// </summary>
        internal static bool PerturbedAngle(Vector3d p, Vector3d a, Vector3d b, out double angle)
        {
            angle = 0.0;
            BigInteger[] d = {
                Grid(a.x) - Grid(p.x), Grid(a.y) - Grid(p.y), Grid(a.z) - Grid(p.z),
                Grid(b.x) - Grid(p.x), Grid(b.y) - Grid(p.y), Grid(b.z) - Grid(p.z) };
            int smallest = int.MaxValue, largest = 0;
            foreach (BigInteger v in d) {
                if (v.IsZero) continue;
                smallest = Math.Min(smallest, BitLength(v));
                largest = Math.Max(largest, BitLength(v));
            }
            if (smallest == int.MaxValue) return true;        // a == b == p: angle 0
            int gap = (largest - smallest) + 60;               // eps2 = eps1 2^-gap, eps3 = eps2 2^-(gap + 30)
            int e1Exp = smallest - 60, e2Exp = e1Exp - gap, e3Exp = e2Exp - gap - 30;
            if (e3Exp < 0) return false;
            BigInteger e1 = BigInteger.One << e1Exp, e2 = BigInteger.One << e2Exp, e3 = BigInteger.One << e3Exp;
            // a - p_eps = (ax - e3, ay - e1, az - e2)
            BigInteger ax = d[0] - e3, ay = d[1] - e1, az = d[2] - e2;
            BigInteger bx = d[3] - e3, by = d[4] - e1, bz = d[5] - e2;
            BigInteger yzA = ay * ay + az * az, yzB = by * by + bz * bz;
            BigInteger la = ISqrt(ax * ax + yzA), lb = ISqrt(bx * bx + yzB);
            BigInteger da = ax.Sign > 0 ? yzA / (la + ax) : la - ax;
            BigInteger db = bx.Sign > 0 ? yzB / (lb + bx) : lb - bx;
            BigInteger num = az * by - ay * bz;
            BigInteger den = da * db + ay * by + az * bz;
            int bits = Math.Max(Math.Max(BitLength(num), BitLength(den)), 1);
            int shift = Math.Max(0, bits - 800);
            double dn = (double)(num >> shift), dd = (double)(den >> shift);
            angle = Math.Atan2(dn, dd);
            return true;
        }

        static double Pow2(int k)
        {
            // exact for -1074 <= k <= 1023 (below 2^-1022 in two steps, the product is an exact subnormal)
            if (k >= -1022) return BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
            return BitConverter.Int64BitsToDouble((long)(k + 600 + 1023) << 52) * BitConverter.Int64BitsToDouble((long)(-600 + 1023) << 52);
        }

        static Vector3d Scaled(Vector3d v, double s) { return new Vector3d(v.x * s, v.y * s, v.z * s); }

        static Vector3d Scaled(Vector3d v, double sx, double syz) { return new Vector3d(v.x * sx, v.y * syz, v.z * syz); }

        // half-integers in [-3, 3]: every 2^k scaling between 2^-1071 and 2^1022 is exact and finite
        static IEnumerable<(Vector3d p, Vector3d a, Vector3d b, string family)> DegenerateConfigs(int perFamily, int seed)
        {
            var rnd = new Random(seed);
            double H() { return rnd.Next(-6, 7) / 2.0; }
            Vector3d V() { return new Vector3d(H(), H(), H()); }
            for (int i = 0; i < perFamily; ++i) {
                Vector3d a = V(), b = V();
                // p on vertex a, on vertex b
                yield return (a, a, b, "p == a");
                yield return (b, a, b, "p == b");
                // p on the open segment (a, b): the midpoint of two points with even-valued sums (half-integers: the midpoint is a multiple of 1/4, exact in a double)
                yield return (new Vector3d((a.x + b.x) / 2, (a.y + b.y) / 2, (a.z + b.z) / 2), a, b, "p on the open edge");
                // p on the open edge with a.z == b.z (the planar case of the open-edge limit), and with a.x == b.x
                {
                    Vector3d b2 = new Vector3d(b.x, b.y, a.z), b3 = new Vector3d(a.x, b.y, b.z);
                    yield return (new Vector3d((a.x + b2.x) / 2, (a.y + b2.y) / 2, a.z), a, b2, "p on the open edge, a.z == b.z");
                    yield return (new Vector3d(a.x, (a.y + b3.y) / 2, (a.z + b3.z) / 2), a, b3, "p on the open edge, a.x == b.x");
                }
                // p on the line of the edge, outside the segment (behind a, behind b): the branch cut with oyz == 0
                {
                    Vector3d p = new Vector3d(H(), 2 * a.y - b.y, 2 * a.z - b.z);
                    yield return (p, a, b, "p on the yz line, beyond a");
                    Vector3d q = new Vector3d(H(), 2 * b.y - a.y, 2 * b.z - a.z);
                    yield return (q, a, b, "p on the yz line, beyond b");
                }
                // a vertex on the +x ray of p, or behind
                {
                    Vector3d p = V();
                    yield return (p, new Vector3d(H(), p.y, p.z), b, "a on the x line of p");
                    yield return (p, a, new Vector3d(H(), p.y, p.z), "b on the x line of p");
                    // the other end at the same z, ahead in y (the rz == 0 paths of the vertex limit)
                    yield return (p, p, new Vector3d(H(), p.y + Math.Abs(H()) + 0.5, p.z), "p == a, b.z == p.z");
                    yield return (p, new Vector3d(H(), p.y + Math.Abs(H()) + 0.5, p.z), p, "p == b, a.z == p.z");
                    yield return (p, p, new Vector3d(H(), p.y, p.z + H()), "p == a, b.y == p.y");
                }
                // generic
                yield return (V(), a, b, "random");
            }
        }

        static bool AllFinite(Vector3d v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }

        static string F(Vector3d v) { return $"({v.x:R}, {v.y:R}, {v.z:R})"; }

        // the code documents 1.2e-13 for the rounded path (finish_edge_angle: the design note, "The accuracy of the edge angle") and 2^-61 for the exact one;
        // the largest error seen on these inputs is below 3e-15 (a few ulps of an angle up to pi), so 1e-14 is kept: three times the observed maximum
        const double Tolerance = 1e-14;

        static void AssertMatches(Vector3d p, Vector3d a, Vector3d b, double expected, string what)
        {
            double got = ExactWindingCache.boundary_edge_angle_x(p, a, b);
            Assert.True(Math.Abs(got - expected) <= Tolerance, $"{what}: p {F(p)} a {F(a)} b {F(b)}: got {got:R}, exact perturbed {expected:R}");
        }

        [Fact]
        public void DegenerateConfigs_MatchThePerturbedOracle_AndAreBitInvariantFromTheSubnormalsToTheOverflow()
        {
            // half-integer coordinates: the vertex limit, the open-edge limit, the +x ray and the branch cut all occur; the
            // function must agree with the perturbed exact angle and give the same bits at every 2^k scale, the rescaled paths
            // (|v| outside 2^+-100), the subnormal differences (2^-1070) and the overflowing differences (2^1022) included
            int[] ks = { -1070, -1000, -600, -300, -100, 100, 300, 600, 1000, 1022 };
            int tested = 0;
            var families = new Dictionary<string, int>();
            foreach (var c in DegenerateConfigs(60, 20261124)) {
                if (c.a.Equals(c.b)) continue;
                Assert.True(PerturbedAngle(c.p, c.a, c.b, out double exact));
                double baseAngle = ExactWindingCache.boundary_edge_angle_x(c.p, c.a, c.b);
                Assert.True(Math.Abs(baseAngle - exact) <= Tolerance, $"{c.family}: p {c.p} a {c.a} b {c.b}: got {baseAngle:R}, exact perturbed {exact:R}");
                double swapped = ExactWindingCache.boundary_edge_angle_x(c.p, c.b, c.a);
                Assert.Equal(-baseAngle, swapped);
                foreach (int k in ks) {
                    double s = Pow2(k);
                    if (!Finite(c.p.x * s) || !Finite(c.a.x * s) || !Finite(c.b.x * s) || Math.Abs(c.p.y * s) > double.MaxValue / 2 || Math.Abs(c.p.z * s) > double.MaxValue / 2 ||
                        Math.Abs(c.a.y * s) > double.MaxValue / 2 || Math.Abs(c.a.z * s) > double.MaxValue / 2 || Math.Abs(c.b.y * s) > double.MaxValue / 2 || Math.Abs(c.b.z * s) > double.MaxValue / 2 ||
                        Math.Abs(c.p.x * s) > double.MaxValue / 2 || Math.Abs(c.a.x * s) > double.MaxValue / 2 || Math.Abs(c.b.x * s) > double.MaxValue / 2) continue;     // (a coordinate that overflows)
                    double scaled = ExactWindingCache.boundary_edge_angle_x(Scaled(c.p, s), Scaled(c.a, s), Scaled(c.b, s));
                    Assert.True(scaled == baseAngle, $"{c.family}, k {k}: p {c.p} a {c.a} b {c.b}: {scaled:R} vs {baseAngle:R} at k 0");
                    Assert.Equal(-scaled, ExactWindingCache.boundary_edge_angle_x(Scaled(c.p, s), Scaled(c.b, s), Scaled(c.a, s)));
                    ++tested;
                }
                families.TryGetValue(c.family, out int n);
                families[c.family] = n + 1;
            }
            Assert.True(tested > 5000, $"only {tested} cases");
            Assert.True(families.Count >= 12, string.Join("; ", families));
        }

        [Fact]
        public void DegenerateConfigs_WithMixedExponentsOfXAndYz_MatchThePerturbedOracle()
        {
            // x at 2^kx and y, z at 2^kyz: |dx| >> |dyz| (the huge-d branches: dExp > 100, the quotient yz2 / (2 mx)) and
            // |dx| << |dyz| (relExp < -60, and below -1020 for the vertex limit); the oracle perturbs far below both
            int[] ks = { -130, -64, 0, 64, 130 };
            int tested = 0;
            foreach (var c in DegenerateConfigs(8, 77)) {
                if (c.a.Equals(c.b)) continue;
                foreach (int kx in ks) {
                    foreach (int kyz in ks) {
                        if (kx == kyz) continue;
                        Vector3d p = Scaled(c.p, Pow2(kx), Pow2(kyz)), a = Scaled(c.a, Pow2(kx), Pow2(kyz)), b = Scaled(c.b, Pow2(kx), Pow2(kyz));
                        if (!PerturbedAngle(p, a, b, out double exact)) continue;
                        AssertMatches(p, a, b, exact, $"{c.family}, kx {kx}, kyz {kyz}, unscaled p {c.p} a {c.a} b {c.b}");
                        Assert.Equal(-ExactWindingCache.boundary_edge_angle_x(p, a, b), ExactWindingCache.boundary_edge_angle_x(p, b, a));
                        ++tested;
                    }
                }
            }
            Assert.True(tested > 1000, $"only {tested} cases");
        }

        [Fact]
        public void RandomEdges_OfAnyExponent_MatchThePerturbedOracle()
        {
            // random mantissas, independent exponents of x, y and z (for the rescaled paths, the vertex of the edge near the
            // +x ray, and differences that overflow: coordinates up to 2^1023 of either sign)
            var rnd = new Random(1124);
            double R(int e) { return (rnd.NextDouble() + 0.5) * (rnd.Next(2) == 0 ? -1 : 1) * Pow2(e); }
            int tested = 0, overflow = 0;
            for (int i = 0; i < 1500; ++i) {
                int e0 = rnd.Next(-150, 1024 - 60);
                int ex = e0 + rnd.Next(0, 60), ey = e0 + rnd.Next(0, 60), ez = e0 + rnd.Next(0, 60);
                if (i % 3 == 0) { ey -= 130; ez -= 130; }                                         // yz << x
                if (i % 3 == 1) { ex -= 130; }                                                    // x << yz
                if (i % 4 == 3) { ex = 1023 - rnd.Next(2); ey = 1023 - rnd.Next(2); ez = 1023 - rnd.Next(2); }      // the differences of opposite signs overflow
                Vector3d V() { return new Vector3d(R(ex), R(ey), R(ez)); }
                Vector3d p = V(), a = V(), b = V();
                if (i % 5 == 0) a = p + new Vector3d(R(ex), R(ey - 70), R(ez - 70));          // a vertex close to the x line of p
                if (i % 7 == 0) b = new Vector3d(b.x, p.y + (b.y - p.y) * Pow2(-80), p.z + (b.z - p.z) * Pow2(-80));
                if (!AllFinite(p) || !AllFinite(a) || !AllFinite(b)) continue;
                if (!PerturbedAngle(p, a, b, out double exact)) continue;
                if (!Finite(a.x - p.x) || !Finite(a.y - p.y) || !Finite(a.z - p.z) ||
                    !Finite(b.x - p.x) || !Finite(b.y - p.y) || !Finite(b.z - p.z)) ++overflow;
                AssertMatches(p, a, b, exact, $"random {i}");
                ++tested;
            }
            Assert.True(tested > 800, $"only {tested} cases");
            Assert.True(overflow > 20, $"only {overflow} cases with an overflowing difference");
        }

        [Fact]
        public void NearThePlaneOfAnEdge_WithSmallX_IsWithinTheTightToleranceOfTheOracle()
        {
            // finding A1 (fixed in #1514): p near the plane of the edge and |x| << |yz|; the yz parts of a - p and b - p are nearly
            // antiparallel, den = da db + dotYZ cancels, and the rounded evaluation was off by 1e-16 / sin(theta): 5.9e-14 for
            // d = 1e-3, 1.5e-11 for d = 3e-6, 1.15e-9 for d = 1e-7 (the old code fails the tolerance from d = 1e-3 on)
            double[] ds = { 1e-3, 1e-4, 3e-6, 1e-7, 1e-9, 1e-12 };
            double[] xs = { 0.0, 1e-9, -1e-9, 1e-20 };
            int tested = 0;
            foreach (double d in ds) {
                foreach (double x in xs) {
                    foreach (double s in new[] { 1.0, Pow2(-300), Pow2(300) }) {
                        Vector3d p = new Vector3d(0, 0, 0), a = new Vector3d(x * s, s, 0), b = new Vector3d(x * s, -s, d * s);
                        Assert.True(PerturbedAngle(p, a, b, out double exact));
                        AssertMatches(p, a, b, exact, $"d {d:R}, x {x:R}, scale {s:R}");
                        Assert.Equal(-ExactWindingCache.boundary_edge_angle_x(p, a, b), ExactWindingCache.boundary_edge_angle_x(p, b, a));
                        ++tested;
                    }
                }
            }
            Assert.Equal(ds.Length * xs.Length * 3, tested);
        }

        [Fact]
        public void OpenEdge_WhereTheRescaledDzUnderflows_IsThePrincipalValueOfTheLimit()
        {
            // p on the open edge, |dz| of the edge below 2^-1075 of its length: after the rescaling uz is 0 and the sign of
            // -ux uy decides (pi, 0 or pi / 2 with the sign of dz). The oracle cannot reach this range (its grid is 2^-1074), the
            // expected value is the closed form of the limit atan2(0+, -ux uy).
            double tiny = Pow2(-1074);
            int tested = 0;
            foreach (double bigX in new[] { Pow2(120), Pow2(1000) }) {
                foreach (double c in new[] { 0.0, 1.0, -1.0, 3.0 * Pow2(500), -Pow2(-5) }) {
                    foreach (double dzSign in new[] { 1.0, -1.0 }) {
                        Vector3d p = new Vector3d(0, 0, tiny * dzSign);
                        Vector3d a = new Vector3d(-bigX, -c, 0), b = new Vector3d(bigX, c, 2 * tiny * dzSign);
                        double expected = c == 0.0 ? dzSign * Math.PI * 0.5 : (c > 0 ? dzSign * Math.PI : 0.0);   // ux > 0
                        double got = ExactWindingCache.boundary_edge_angle_x(p, a, b);
                        Assert.True(got == expected, $"bigX {bigX:R}, c {c:R}, dz sign {dzSign}: got {got:R}, expected {expected:R}");
                        Assert.Equal(-got, ExactWindingCache.boundary_edge_angle_x(p, b, a));
                        ++tested;
                    }
                }
            }
            Assert.Equal(20, tested);
        }

        [Fact]
        public void Vertex_WithXBelowTheYzRangeAndAnUnderflowingZ_IsThePrincipalValueOfTheLimit()
        {
            // p == a, |dx| / |dy| < 2^-1020 and dz at 2^-1074 against dx: after the rescaling uz is 0 and the sign of dx decides:
            // dx > 0 gives +-pi (the sign of dz), dx < 0 gives 0 (the limit of atan2(sign dz, -dx) with dz -> 0+)
            double tiny = Pow2(-1074);
            int tested = 0;
            foreach (double dx in new[] { 4.0, -4.0, 2.0 }) {
                foreach (double dzSign in new[] { 1.0, -1.0 }) {
                    Vector3d p = new Vector3d(0, 0, 0);
                    Vector3d other = new Vector3d(p.x + dx, p.y + Pow2(1023), p.z + dzSign * tiny);
                    
                    double expected = dx > 0 ? dzSign * Math.PI : 0.0;
                    double got = ExactWindingCache.boundary_edge_angle_x(p, p, other);
                    Assert.True(got == expected, $"dx {dx:R}, dz sign {dzSign}: got {got:R}, expected {expected:R}");
                    Assert.Equal(-got, ExactWindingCache.boundary_edge_angle_x(p, other, p));
                    ++tested;
                }
            }
            Assert.True(tested >= 6, $"only {tested} cases");
        }

        [Fact]
        public void Vertex_WithOverflowingDifferences_MatchesThePerturbedOracle()
        {
            // p == a (or p == b) with coordinates near 2^1023 of opposite signs: the differences overflow (dx and the yz parts)
            var rnd = new Random(77124);
            double R(int e) { return (rnd.NextDouble() + 0.5) * (rnd.Next(2) == 0 ? -1 : 1) * Pow2(e); }
            int tested = 0;
            for (int i = 0; i < 400; ++i) {
                int ex = 1022 - rnd.Next(0, 2), ey = ex - rnd.Next(0, 70), ez = ex - rnd.Next(0, 70);
                Vector3d p = new Vector3d(R(ex), R(ey), R(ez));
                Vector3d other = new Vector3d(-Math.Sign(p.x) * Math.Abs(R(ex)), R(ey), R(ez));
                if (!AllFinite(p) || !AllFinite(other)) continue;
                Vector3d a = i % 2 == 0 ? p : other, b = i % 2 == 0 ? other : p;
                if (!PerturbedAngle(p, a, b, out double exact)) continue;
                AssertMatches(p, a, b, exact, $"overflowing vertex limit {i}");
                ++tested;
            }
            Assert.True(tested > 300, $"only {tested} cases");
        }
    }
}
