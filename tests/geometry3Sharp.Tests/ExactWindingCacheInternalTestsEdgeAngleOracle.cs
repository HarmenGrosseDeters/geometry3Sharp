using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Assumption A1 of the BoundaryTree bound (docs/design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md, "Which tree"), as far as these
    /// tests check it: the computed angle T of an edge, boundary_edge_angle_x(p, a, b), differs from its exact value by at most
    /// 1e-12 on the inputs of EdgeAngle_ErrorIsBelowTheAssumedBound_OnAdversarialEdges, which keep p away from the edge by
    /// construction (no family puts p within about 1e-6 rad of the edge seen from p, except by chance) and outside the exact
    /// zone (num and den both within numErr and denErr of 0, where the exact predicates decide the sign). The claim is NOT
    /// general: near an open edge den = da db + dotYZ cancels and the error grows like 1e-16 / theta (the angular distance
    /// of p from the edge); the tree uses A1 only for the edges of an accepted node (1e-14, proved, under the fast-path certificate:
    /// EdgesOfAcceptedNodes_HaveAnErrorBelowTheProvedBound). Tests of the general claim belong to the change that corrects the
    /// edge angle near an open edge (#1427). The oracle takes the exact num = a.z b.y - a.y b.z and den = da db + a.y b.y + a.z b.z of
    /// the exact differences a - p and b - p in integers over the grid 2^-1074 (da = |a - p| - (a.x - p.x) by the cancellation-free
    /// quotient), and the angle atan2(num, den) of their double conversion: its own error is a few 1e-16, far below the 1e-12 under
    /// test. The class name starts with ExactWindingCacheInternalTests so that the mutation shard's filter runs it.
    /// </summary>
    public class ExactWindingCacheInternalTestsEdgeAngleOracle
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

        // an upper bound of the bit length (the bytes of the magnitude): enough for a starting value and a scale
        static int Bits(BigInteger v) { return BigInteger.Abs(v).ToByteArray().Length * 8; }

        static BigInteger ISqrt(BigInteger n)
        {
            if (n.Sign <= 0) return BigInteger.Zero;
            BigInteger x = BigInteger.One << ((Bits(n) + 1) / 2);
            while (true) {
                BigInteger y = (x + n / x) >> 1;
                if (y >= x) return x;
                x = y;
            }
        }

        static double ToDouble(BigInteger v, int shift)
        {
            return (double)(v >> shift) ;
        }

        // the exact angle atan2(num, den) (double conversion of the exact integers, then one atan2); false when the exact
        // num and den are in the zone where the function leaves the decision to the exact predicates
        internal static bool ExactAngle(Vector3d p, Vector3d a, Vector3d b, out double angle, out bool inExactZone)
        {
            BigInteger ax = Grid(a.x) - Grid(p.x), ay = Grid(a.y) - Grid(p.y), az = Grid(a.z) - Grid(p.z);
            BigInteger bx = Grid(b.x) - Grid(p.x), by = Grid(b.y) - Grid(p.y), bz = Grid(b.z) - Grid(p.z);
            BigInteger yzA = ay * ay + az * az, yzB = by * by + bz * bz;
            BigInteger la = ISqrt(ax * ax + yzA), lb = ISqrt(bx * bx + yzB);
            // da = la - ax without cancellation: yz2 / (la + ax) for ax > 0 (all over the grid, so the quotient stays on it)
            BigInteger da = ax.Sign > 0 ? yzA / (la + ax) : la - ax;
            BigInteger db = bx.Sign > 0 ? yzB / (lb + bx) : lb - bx;
            BigInteger num = az * by - ay * bz;
            BigInteger den = da * db + ay * by + az * bz;
            // the sizes of the terms, for the exact zone
            BigInteger s1 = BigInteger.Abs(ay * bz) + BigInteger.Abs(az * by);
            BigInteger s2 = da * db + BigInteger.Abs(ay * by) + BigInteger.Abs(az * bz);
            double d1 = 1e-12 * 1.01;
            // |num| <= numErr and den <= denErr, with a margin of 1 % (the zone of the exact fallback)
            inExactZone = RatioLE(BigInteger.Abs(num), s1, d1) && RatioLE(den, s2, d1);
            int bits = Math.Max(Math.Max(Bits(num), Bits(den)), 1);
            int shift = Math.Max(0, bits - 900);
            angle = Math.Atan2(ToDouble(num, shift), ToDouble(den, shift));
            return true;
        }

        // x <= y * f for non-negative integers (f a double): compared as x * 2^40 <= y * round(f * 2^40 ...) by exact rational f
        static bool RatioLE(BigInteger x, BigInteger y, double f)
        {
            // f = 1.01e-12: x <= y * 1.01e-12  <=>  x * 10^14 <= y * 101 (1.01e-12 = 101 / 10^14)
            return x * BigInteger.Pow(10, 14) <= y * 101;
        }

        static IEnumerable<(Vector3d p, Vector3d a, Vector3d b, string family)> Cases(int perFamily, int seed)
        {
            var rnd = new Random(seed);
            double R() { return rnd.NextDouble() * 2 - 1; }
            Vector3d V(double s) { return new Vector3d(R() * s, R() * s, R() * s); }
            for (int i = 0; i < perFamily; ++i) {
                double s = Math.Pow(2, rnd.Next(-90, 90));
                // generic
                yield return (V(s), V(s), V(s), "generic");
                // a vertex near the +x ray of p
                {
                    Vector3d p = V(s); double t = s * (0.1 + rnd.NextDouble());
                    double eps = Math.Pow(10, -rnd.Next(9, 14));
                    Vector3d a = p + new Vector3d(t, R() * t * eps, R() * t * eps);
                    yield return (p, a, V(s) + p, "vertex near the ray");
                    yield return (p, V(s) + p, a, "vertex near the ray (b)");
                }
                // p near the yz line of the edge (ahead and behind)
                {
                    Vector3d a = V(s), b = V(s);
                    double u = rnd.NextDouble();
                    double delta = Math.Pow(10, -rnd.Next(6, 12)) * s;
                    var p = new Vector3d(R() * s, a.y + u * (b.y - a.y) + R() * delta, a.z + u * (b.z - a.z) + R() * delta);
                    yield return (p, a, b, "p near the yz line");
                }
                // p near a vertex
                {
                    Vector3d p = V(s), dir = V(1);
                    double r = s * Math.Pow(10, -rnd.Next(6, 12));
                    Vector3d a = p + r * dir;
                    yield return (p, a, V(s), "p near a vertex");
                }
                // yz much smaller than x: fast path just inside and the rescaled path beyond
                {
                    double ratio = Math.Pow(10, -rnd.Next(5, 45));
                    Vector3d p = V(s);
                    Vector3d a = p + new Vector3d((rnd.NextDouble() + 0.2) * s, R() * s * ratio, R() * s * ratio);
                    Vector3d b = p + new Vector3d((R() + 0.01) * s, R() * s * ratio, R() * s * ratio);
                    yield return (p, a, b, "yz much smaller than x");
                }
                // large offset: the differences a - p and b - p are rounded
                {
                    Vector3d off = new Vector3d(R(), R(), R()) * 1e6;
                    Vector3d p = off + V(1), a = off + V(1), b = off + V(1);
                    yield return (p, a, b, "large offset");
                }
            }
        }

        [Fact]
        public void EdgeAngle_ErrorIsBelowTheAssumedBound_OnAdversarialEdges()
        {
            double max = 0.0;
            string where = "";
            int tested = 0, zone = 0;
            var perFamily = new Dictionary<string, double>();
            foreach (var c in Cases(900, 13)) {
                if (c.a.Equals(c.p) || c.b.Equals(c.p) || c.a.Equals(c.b)) continue;
                if ((c.a.y == c.p.y && c.a.z == c.p.z) || (c.b.y == c.p.y && c.b.z == c.p.z)) continue;      // (the special-case paths)
                ExactAngle(c.p, c.a, c.b, out double exact, out bool inZone);
                if (inZone) { ++zone; continue; }
                double got = ExactWindingCache.boundary_edge_angle_x(c.p, c.a, c.b);
                double err = Math.Abs(got - exact);
                // (an angle near +-pi may come out with the other sign: the same angle modulo 2 pi)
                err = Math.Min(err, Math.Abs(Math.Abs(got - exact) - 2 * Math.PI));
                ++tested;
                perFamily.TryGetValue(c.family, out double m);
                if (err > m) perFamily[c.family] = err;
                if (err > max) { max = err; where = $"{c.family}: p {c.p} a {c.a} b {c.b}: got {got:R}, exact {exact:R}"; }
            }
            Assert.True(tested > 4000, $"only {tested} edges tested ({zone} in the exact zone)");
            string report = string.Join("; ", perFamily);
            Assert.True(max <= 1e-12, $"the largest error {max:E3} exceeds the assumed 1e-12 ({where}); per family: {report}");
        }

        [Fact]
        public void EdgesOfAcceptedNodes_HaveAnErrorBelowTheProvedBound()
        {
            // the proved bound of A1 (design note, "The decision margin"): every edge of an accepted node, which the certificate
            // keeps on the fast path, is within 1e-14 of its exact angle; the chord is such an edge
            DMesh3 mesh = ExactWindingAdoptionTests.LargeBoundaryOpenGrid(160);
            foreach (int v in mesh.VertexIndices()) {          // (an offset and a jitter: rounded differences)
                Vector3d q = mesh.GetVertex(v);
                mesh.SetVertex(v, q + new Vector3d(1e3, -2e3, 5e2) + new Vector3d(1e-4 * Math.Sin(7 * v), 1e-4 * Math.Cos(3 * v), 1e-4 * Math.Sin(11 * v)));
            }
            ExactWindingCache cache = new DMeshAABBTree3(mesh, true).GetExactWindingCache();
            cache.PrepareThresholdQueries();
            Assert.True(cache.HasBoundaryTree);
            var rnd = new Random(5);
            double max = 0.0;
            int edges = 0, nodes = 0;
            for (int k = 0; k < cache.BoundaryNodeCount; k += 3) {
                ExactWindingCache.NodeInfo node = cache.DescribeBoundaryNode(k, Vector3d.Zero);
                for (int t = 0; t < 6; ++t) {
                    Vector3d dir = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                    Vector3d p = node.Center + (6 + 100 * rnd.NextDouble()) * Math.Max(node.R, 1e-3) * dir.Normalized;
                    ExactWindingCache.NodeInfo info = cache.DescribeBoundaryNode(k, p);
                    if (info.Status != 1) continue;
                    ++nodes;
                    var pairs = new List<(Vector3d, Vector3d)> { (info.Vertices[0], info.Vertices[info.Vertices.Length - 1]) };
                    for (int s = 0; s + 1 < info.Vertices.Length; s += Math.Max(1, info.Vertices.Length / 6)) pairs.Add((info.Vertices[s], info.Vertices[s + 1]));
                    foreach (var (a, b) in pairs) {
                        if (a.Equals(b)) continue;
                        ExactAngle(p, a, b, out double exact, out bool inZone);
                        Assert.False(inZone, "an edge of an accepted node is never in the exact zone");
                        double err = Math.Abs(ExactWindingCache.boundary_edge_angle_x(p, a, b) - exact);
                        ++edges;
                        if (err > max) max = err;
                    }
                }
            }
            Assert.True(edges > 300 && nodes > 100, $"{edges} edges of {nodes} nodes");
            Assert.True(max <= 1e-14, $"the largest error {max:E3} of an accepted node's edge exceeds the proved 1e-14");
        }

        // ---- Math.Atan2 against an exact arctangent (the proof of A1 assumes at most 2 ulp of the result) ----

        const int F = 220;

        static BigInteger AtanFixed(BigInteger r)           // r over 2^F, |r| <= 1; atan(r) over 2^F
        {
            BigInteger one = BigInteger.One << F;
            for (int i = 0; i < 3; ++i)                      // atan(r) = 2 atan(r / (1 + sqrt(1 + r^2)))
                r = (r << F) / (one + ISqrt((one << F) + r * r));
            BigInteger sum = r, term = r, r2 = (r * r) >> F;
            for (int k = 1; k < 80; ++k) {
                term = -((term * r2) >> F);
                sum += term / (2 * k + 1);
            }
            return sum << 3;
        }

        internal static double ExactAtan2(double y, double x, out double ulp)
        {
            BigInteger Y = Grid(y), X = Grid(x);
            if (BitConverter.DoubleToInt64Bits(y) < 0) Y = -Grid(-y);
            BigInteger ay = BigInteger.Abs(Y), ax = BigInteger.Abs(X);
            BigInteger pi = AtanFixed(BigInteger.One << F) << 2;
            BigInteger a;
            if (ax.IsZero && ay.IsZero) { ulp = 0; return 0.0; }
            a = ax >= ay ? AtanFixed((ay << F) / ax) : (pi >> 1) - AtanFixed((ax << F) / ay);
            if (x < 0) a = pi - a;
            if (y < 0) a = -a;
            // (the integer a over 2^F to a double: 56 to 64 leading bits, rounded to nearest by the conversion of an ulong)
            BigInteger magnitude = BigInteger.Abs(a);
            int sh = Math.Max(0, Bits(magnitude) - 64);
            double result = (double)(ulong)(magnitude >> sh) * Math.Pow(2, sh - F);
            if (a.Sign < 0) result = -result;
            double next = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(Math.Abs(result)) + 1);
            ulp = next - Math.Abs(result);
            return result;
        }

        [Fact]
        public void MathAtan2_IsWithinTwoUlpOfTheExactValue_OnTheArgumentsOfTheFastPath()
        {
            // num and den of the fast path are products and sums of doubles of any exponent between 2^-200 and 2^200, of either
            // sign, with ratios from 1e-15 to 1e15 and near +-pi (den < 0, num tiny)
            var rnd = new Random(31);
            double worst = 0.0;
            int n = 0;
            for (int i = 0; i < 6000; ++i) {
                double e1 = Math.Pow(2, rnd.Next(-200, 200));
                double ratio = Math.Pow(10, rnd.Next(-15, 16));
                double y = (rnd.NextDouble() * 2 - 1) * e1;
                double x = (rnd.NextDouble() * 2 - 1) * e1 * (i % 3 == 0 ? 1.0 : ratio);
                if (i % 7 == 0) { x = -Math.Abs(x); y = y * 1e-14; }          // near the branch cut
                if (x == 0.0 && y == 0.0) continue;
                double got = Math.Atan2(y, x);
                double exact = ExactAtan2(y, x, out double ulp);
                double ulps = Math.Abs(got - exact) / ulp;
                ++n;
                if (ulps > worst) worst = ulps;
                Assert.True(ulps <= 2.0, $"Math.Atan2({y:R}, {x:R}) = {got:R} is {ulps:F2} ulp from {exact:R}");
            }
            Assert.True(n > 5000);
        }
    }
}
