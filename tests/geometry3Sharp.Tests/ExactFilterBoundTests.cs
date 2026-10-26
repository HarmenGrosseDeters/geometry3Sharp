using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The error bounds of the floating-point filters of ExactMeshArrangement, checked against exact values on
    /// adversarial inputs: nearly degenerate configurations (where the filters decide at their bound), large
    /// offsets (cancellation in the differences), huge and subnormal magnitudes, and exact points at the worst
    /// corners of the error boxes of their approximations. Every comparison is exact (scaled integers).
    /// </summary>
    public class ExactFilterBoundTests
    {
        // doubles as integers: v * 2^S (exact for every finite double, the smallest is 2^-1074)
        const int S = 1100;
        const double U = 2.220446049250313e-16;

        static BigInteger Q(double v, int scale = S)
        {
            BigInteger m; int e;
            ExactPoint3.Decompose(v, out m, out e);
            return m.IsZero ? BigInteger.Zero : (m << (e + scale));
        }

        // |approx - num / (den 2^scale)| <= err, exactly (den > 0)
        static bool Within(double approx, double err, BigInteger num, BigInteger den, int scale)
        {
            return BigInteger.Abs(Q(approx, scale) * den - num) <= Q(err, scale) * den;
        }

        static void AssertWithin(double approx, double err, BigInteger num, BigInteger den, int scale, string what)
        {
            Assert.True(err >= 0, what + ": negative bound");
            Assert.True(Within(approx, err, num, den, scale),
                $"{what}: approximation {approx:R}, exact {ExactPoint3.ToDouble(num, den << scale, 0):R}, bound {err:R}");
        }

        // the corners of the error box: c + s e for the signs in the bits of mask
        static BigInteger Corner(double c, double e, int mask, int bit)
        {
            return ((mask >> bit) & 1) == 0 ? Q(c) - Q(e) : Q(c) + Q(e);
        }

        // a random double of magnitude about 2^k
        static double Rnd(Random r, int k)
        {
            return (r.NextDouble() - 0.5) * Math.Pow(2, k + 1);
        }

        // the scales of the cases: coordinates around 2^k, with offsets around 2^off (cancellation in the differences)
        static IEnumerable<(int k, int off)> Scales()
        {
            foreach (int k in new[] { -1070, -1060, -900, -40, -3, 0, 10, 52, 60, 300 })
                foreach (int off in new[] { int.MinValue, k + 20, k + 50 })
                    yield return (k, off);
        }

        // an error bound for coordinates of magnitude about 2^k: none, of the order of rounding, larger, subnormal
        static double ErrorOf(Random r, int k, int kind)
        {
            switch (kind) {
                case 0: return 0;
                case 1: return 4 * U * Math.Pow(2, k) * r.NextDouble();
                case 2: return Math.Pow(2, k - 30) * r.NextDouble();
                default: return double.Epsilon * r.Next(1, 8);
            }
        }

        [Fact]
        public void Orient2d_BoundHolds_AtWorstCorners()
        {
            var r = new Random(1);
            int cases = 0;
            foreach (var (k, off) in Scales())
                for (int iter = 0; iter < 30; ++iter) {
                    double o = (off == int.MinValue) ? 0 : Math.Pow(2, off);
                    var a = new Vector3d(o + Rnd(r, k), o + Rnd(r, k), 0);
                    var b = new Vector3d(o + Rnd(r, k), o + Rnd(r, k), 0);
                    // c nearly on the line a-b (on it before rounding, or a few ulps off)
                    double t = r.NextDouble() * 3 - 1;
                    var c = new Vector3d(a.x + t * (b.x - a.x), a.y + t * (b.y - a.y), 0);
                    if (iter % 3 == 1)
                        c.y += Math.Pow(2, k - 50) * (r.NextDouble() - 0.5);
                    if (iter % 5 == 4)
                        c = a;      // repeated point
                    double e = ErrorOf(r, k, iter % 4);
                    double err;
                    double f = ExactMeshArrangement.orient2d_approx(a, b, c, 0, 1, e, out err);
                    for (int mask = 0; mask < 64; ++mask) {
                        BigInteger ax = Corner(a.x, e, mask, 0), ay = Corner(a.y, e, mask, 1), bx = Corner(b.x, e, mask, 2),
                            by = Corner(b.y, e, mask, 3), cx = Corner(c.x, e, mask, 4), cy = Corner(c.y, e, mask, 5);
                        BigInteger det = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                        AssertWithin(f, err, det, BigInteger.One, 2 * S, $"orient2d k={k} off={off} iter={iter} mask={mask}");
                    }
                    cases++;
                }
            Assert.True(cases > 0);
        }

        [Fact]
        public void InCircle_BoundHolds_AtWorstCorners()
        {
            var r = new Random(2);
            foreach (var (k, off) in Scales()) {
                if (k > 200 || k < -1000)
                    continue;       // (degree 4: beyond the double range, the filter has no bound)
                for (int iter = 0; iter < 12; ++iter) {
                    double o = (off == int.MinValue) ? 0 : Math.Pow(2, off);
                    // four points nearly on a circle (rounded), d sometimes exactly a, b or c
                    double rad = Math.Pow(2, k), cx0 = o + Rnd(r, k), cy0 = o + Rnd(r, k);
                    var p = new Vector3d[4];
                    for (int m = 0; m < 4; ++m) {
                        double ang = r.NextDouble() * 2 * Math.PI;
                        p[m] = new Vector3d(cx0 + rad * Math.Cos(ang), cy0 + rad * Math.Sin(ang), 0);
                    }
                    if (iter % 4 == 3)
                        p[3] = p[1];
                    double e = ErrorOf(r, k, iter % 4);
                    double bound;
                    double f = ExactMeshArrangement.incircle_approx(p[0], p[1], p[2], p[3], 0, 1, e, out bound);
                    for (int mask = 0; mask < 256; ++mask) {
                        var X = new BigInteger[4];
                        var Y = new BigInteger[4];
                        for (int m = 0; m < 4; ++m) {
                            X[m] = Corner(p[m].x, e, mask, 2 * m);
                            Y[m] = Corner(p[m].y, e, mask, 2 * m + 1);
                        }
                        BigInteger adx = X[0] - X[3], ady = Y[0] - Y[3], bdx = X[1] - X[3], bdy = Y[1] - Y[3], cdx = X[2] - X[3], cdy = Y[2] - Y[3];
                        BigInteger det = (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy) + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
                            + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
                        AssertWithin(f, bound, det, BigInteger.One, 4 * S, $"incircle k={k} off={off} iter={iter} mask={mask}");
                    }
                }
            }
        }

        [Fact]
        public void CompareAlong_BoundHolds_WithExactDirection()
        {
            // the direction is an exact integer (rounded to double by the filter), the points differ nearly
            // perpendicular to it
            var r = new Random(3);
            foreach (var (k, off) in Scales())
                for (int iter = 0; iter < 30; ++iter) {
                    BigInteger dx = new BigInteger(Rnd(r, 62)) << r.Next(0, 40), dy = new BigInteger(Rnd(r, 62)), dz = new BigInteger(Rnd(r, 20));
                    double fdx = (double)dx, fdy = (double)dy, fdz = (double)dz;
                    double o = (off == int.MinValue) ? 0 : Math.Pow(2, off);
                    var a = new Vector3d(o + Rnd(r, k), o + Rnd(r, k), o + Rnd(r, k));
                    // b - a nearly perpendicular to d
                    var perp = new Vector3d(fdy, -fdx, 0).Normalized * Math.Pow(2, k) * r.NextDouble();
                    var b = a + perp + new Vector3d(Rnd(r, k - 45), Rnd(r, k - 45), Rnd(r, k - 45)) * ((iter % 2 == 0) ? 1 : 0);
                    double e = ErrorOf(r, k, iter % 4);
                    double bound;
                    double f = ExactMeshArrangement.along_approx(fdx, fdy, fdz, a, b, e, out bound);
                    for (int mask = 0; mask < 64; ++mask) {
                        BigInteger v = dx * (Corner(a.x, e, mask, 0) - Corner(b.x, e, mask, 1)) + dy * (Corner(a.y, e, mask, 2) - Corner(b.y, e, mask, 3))
                            + dz * (Corner(a.z, e, mask, 4) - Corner(b.z, e, mask, 5));
                        AssertWithin(f, bound, v, BigInteger.One, S, $"along k={k} off={off} iter={iter} mask={mask}");
                    }
                }
        }

        [Fact]
        public void OrderKey_BoundHolds_AtWorstCorners()
        {
            // the weights are exact doubles; their exact value as integers over 2^53
            double[] alpha = { 1.0, 0.7548776662466927, 0.5698402909980532 };
            var r = new Random(4);
            foreach (var (k, off) in Scales())
                for (int iter = 0; iter < 30; ++iter) {
                    double o = (off == int.MinValue) ? 0 : Math.Pow(2, off);
                    var a = new Vector3d(o + Rnd(r, k), Rnd(r, k), -o + Rnd(r, k));
                    double e = ErrorOf(r, k, iter % 4);
                    double err;
                    double f = ExactMeshArrangement.order_key(a, e, out err);
                    for (int mask = 0; mask < 8; ++mask) {
                        BigInteger v = Q(alpha[0], 53) * Corner(a.x, e, mask, 0) + Q(alpha[1], 53) * Corner(a.y, e, mask, 1) + Q(alpha[2], 53) * Corner(a.z, e, mask, 2);
                        AssertWithin(f, err, v, BigInteger.One, S + 53, $"order key k={k} off={off} iter={iter} mask={mask}");
                    }
                }
        }

        [Fact]
        public void PlaneValue_BoundHolds_ForRoundedCoefficients()
        {
            // integer plane coefficients beyond 2^53 (rounded to double), points nearly on the plane
            var r = new Random(5);
            foreach (int bits in new[] { 10, 53, 54, 80, 200 })
                for (int iter = 0; iter < 200; ++iter) {
                    BigInteger A = new BigInteger(Rnd(r, 50)) << (bits - 50 > 0 ? bits - 50 : 0), B = new BigInteger(Rnd(r, 50)) << r.Next(0, Math.Max(1, bits - 50)),
                        C = new BigInteger(Rnd(r, 30)) + 1;
                    double x = Rnd(r, 20), y = Rnd(r, 20), z = Rnd(r, 20);
                    // D so that the point is on the plane (up to the rounding of x, y, z), sometimes exactly
                    BigInteger D = (A * Q(x) + B * Q(y) + C * Q(z)) >> S;
                    double[] pa = { (double)A, (double)B, (double)C, (double)D };
                    var p = new Vector3d(x, y, z);
                    double e = ErrorOf(r, 20, iter % 3);
                    double err;
                    double f = ExactMeshArrangement.plane_value(pa, 0, p, e, out err);
                    for (int mask = 0; mask < 8; ++mask) {
                        BigInteger v = A * Corner(x, e, mask, 0) + B * Corner(y, e, mask, 1) + C * Corner(z, e, mask, 2) - (D << S);
                        AssertWithin(f, err, v, BigInteger.One, S, $"plane value bits={bits} iter={iter} mask={mask}");
                    }
                }
        }

        // the exact point P + t (Q - P), t = sp / (sp - sq), for exact P, Q (scaled by 2^S) and values sp, sq of
        // opposite signs: coordinate k as num / (den 2^S)
        static void SegmentExact(BigInteger[] P, BigInteger[] Qe, BigInteger sp, BigInteger sq, int k, out BigInteger num, out BigInteger den)
        {
            den = sp - sq;
            num = P[k] * den + sp * (Qe[k] - P[k]);
            if (den.Sign < 0) { den = -den; num = -num; }
        }

        [Fact]
        public void SegmentPoint_BoundHolds_AtWorstCorners()
        {
            var r = new Random(6);
            int checkedCorners = 0;
            foreach (var (k, off) in Scales())
                for (int iter = 0; iter < 30; ++iter) {
                    double o = (off == int.MinValue) ? 0 : Math.Pow(2, off);
                    var P = new Vector3d(o + Rnd(r, k), o + Rnd(r, k), Rnd(r, k));
                    var Qv = new Vector3d(o + Rnd(r, k), Rnd(r, k), o + Rnd(r, k));
                    double ep = ErrorOf(r, k, iter % 4), eq = ErrorOf(r, k, (iter + 1) % 4);
                    // values of opposite signs: both large, one tiny (the crossing at an end), with errors up to
                    // their size (a poor approximation of t)
                    int sk = r.Next(-300, 300);
                    double sp = Math.Pow(2, sk) * (0.5 + r.NextDouble()), sq = -Math.Pow(2, sk + ((iter % 3 == 0) ? -60 : r.Next(-5, 5))) * (0.5 + r.NextDouble());
                    double esp = Math.Abs(sp) * ((iter % 2 == 0) ? U : 1e-3 * r.NextDouble()), esq = Math.Abs(sq) * ((iter % 5 == 0) ? 0.9 : U);
                    double err;
                    Vector3d a = ExactMeshArrangement.segment_point(P, ep, Qv, eq, sp, esp, sq, esq, out err);
                    for (int mask = 0; mask < 256; ++mask) {
                        var Pe = new[] { Corner(P.x, ep, mask, 0), Corner(P.y, ep, mask, 1), Corner(P.z, ep, mask, 2) };
                        var Qe = new[] { Corner(Qv.x, eq, mask, 3), Corner(Qv.y, eq, mask, 4), Corner(Qv.z, eq, mask, 5) };
                        BigInteger spe = Corner(sp, esp, mask, 6), sqe = Corner(sq, esq, mask, 7);
                        if (spe.Sign * sqe.Sign >= 0)
                            continue;       // (the contract: exactly of opposite signs)
                        for (int c = 0; c < 3; ++c) {
                            BigInteger num, den;
                            SegmentExact(Pe, Qe, spe, sqe, c, out num, out den);
                            AssertWithin(a[c], err, num, den, S, $"segment point k={k} off={off} iter={iter} mask={mask} coordinate {c}");
                        }
                        checkedCorners++;
                    }
                }
            Assert.True(checkedCorners > 10000);
        }

        [Fact]
        public void SegmentPoint_SubnormalResult_BoundIsNotZero()
        {
            // the crossing at 1.5 * 2^-1074 rounds to 2 * 2^-1074; the relative terms of the bound underflow to 0
            var P = Vector3d.Zero;
            var Qv = new Vector3d(3 * double.Epsilon, 0, 0);
            double err;
            Vector3d a = ExactMeshArrangement.segment_point(P, 0, Qv, 0, 1, 0, -1, 0, out err);
            AssertWithin(a.x, err, 3, 2, 1074, "subnormal segment point");
        }

        // the point common to three planes, exactly: Cramer with the coefficients as integers scaled by 2^S
        static bool PlanesExact(double[][] pl, out BigInteger[] num, out BigInteger den)
        {
            BigInteger[][] p = new BigInteger[3][];
            for (int m = 0; m < 3; ++m)
                p[m] = new[] { Q(pl[m][0]), Q(pl[m][1]), Q(pl[m][2]), Q(pl[m][3]) };
            BigInteger c23x = p[1][1] * p[2][2] - p[1][2] * p[2][1], c23y = p[1][2] * p[2][0] - p[1][0] * p[2][2], c23z = p[1][0] * p[2][1] - p[1][1] * p[2][0];
            BigInteger c31x = p[2][1] * p[0][2] - p[2][2] * p[0][1], c31y = p[2][2] * p[0][0] - p[2][0] * p[0][2], c31z = p[2][0] * p[0][1] - p[2][1] * p[0][0];
            BigInteger c12x = p[0][1] * p[1][2] - p[0][2] * p[1][1], c12y = p[0][2] * p[1][0] - p[0][0] * p[1][2], c12z = p[0][0] * p[1][1] - p[0][1] * p[1][0];
            den = p[0][0] * c23x + p[0][1] * c23y + p[0][2] * c23z;
            num = new[] { p[0][3] * c23x + p[1][3] * c31x + p[2][3] * c12x, p[0][3] * c23y + p[1][3] * c31y + p[2][3] * c12y,
                p[0][3] * c23z + p[1][3] * c31z + p[2][3] * c12z };
            if (den.Sign < 0) {
                den = -den;
                for (int k = 0; k < 3; ++k) num[k] = -num[k];
            }
            // num / den is the point: the 2^S of the coefficients cancel; scale num by 2^S for the comparison
            for (int k = 0; k < 3; ++k) num[k] <<= S;
            return den.IsZero == false;
        }

        static void AssertPlanes(double[][] pl, string what)
        {
            double err;
            Vector3d x = ExactMeshArrangement.planes_approx(pl[0], 0, pl[1], 0, pl[2], 0, out err);
            BigInteger[] num; BigInteger den;
            if (PlanesExact(pl, out num, out den) == false || double.IsInfinity(err))
                return;     // (dependent or nearly dependent: computed exactly, no bound)
            for (int k = 0; k < 3; ++k)
                AssertWithin(x[k], err, num[k], den, S, $"{what} coordinate {k}");
        }

        [Fact]
        public void PlanesPoint_BoundHolds_NearlyDependentAndExtreme()
        {
            var r = new Random(7);
            int bounded = 0;
            foreach (int k in new[] { 0, 20, 52, 100, 300 })
                for (int iter = 0; iter < 200; ++iter) {
                    // three planes through about the same line (nearly dependent), integer coefficients of about 2^k
                    var n0 = new Vector3d(Rnd(r, 0), Rnd(r, 0), Rnd(r, 0)).Normalized;
                    var pl = new double[3][];
                    double eps = Math.Pow(2, -r.Next(1, 50));
                    for (int m = 0; m < 3; ++m) {
                        Vector3d n = (m == 0) ? n0 : (n0 + new Vector3d(Rnd(r, 0), Rnd(r, 0), Rnd(r, 0)) * eps);
                        double s = Math.Pow(2, k);
                        pl[m] = new[] { Math.Round(n.x * s), Math.Round(n.y * s), Math.Round(n.z * s), Math.Round(Rnd(r, k + 10)) };
                    }
                    AssertPlanes(pl, $"planes k={k} iter={iter}");
                    double err;
                    ExactMeshArrangement.planes_approx(pl[0], 0, pl[1], 0, pl[2], 0, out err);
                    if (double.IsInfinity(err) == false)
                        bounded++;
                }
            Assert.True(bounded > 100);
        }

        [Fact]
        public void PlanesPoint_SubnormalResult_BoundIsNotZero()
        {
            // x = 2^-1074 / 3 rounds to 0, the relative terms of the bound underflow (coefficients that are not
            // integers: the contract of the function, not the planes of the arrangement)
            var pl = new[] { new[] { 3.0, 0, 0, double.Epsilon }, new[] { 0, 1.0, 0, 0 }, new[] { 0, 0, 1.0, 0 } };
            AssertPlanes(pl, "subnormal planes point");
            double err;
            ExactMeshArrangement.planes_approx(pl[0], 0, pl[1], 0, pl[2], 0, out err);
            Assert.True(err > 0);
        }

        [Fact]
        public void AddPoint_TinyRational_BoundCoversTheApproximation()
        {
            // rational points whose coordinates are subnormal or below the double range: the approximation is
            // rounded absolutely (or 0), not relatively
            var arr = new ExactMeshArrangement(new List<DMesh3>());
            var cases = new[] {
                ExactPoint3.Create(1, 0, 0, BigInteger.One << 1100),
                ExactPoint3.Create(1, -5, 7, 3 * (BigInteger.One << 1072)),
                ExactPoint3.Create(BigInteger.One << 2000, 1, 0, 3 * (BigInteger.One << 1070)),
            };
            foreach (ExactPoint3 p in cases) {
                int id = arr.AddPoint(p);
                Vector3d a = arr.Approx(id);
                double e = arr.ApproxError(id);
                for (int k = 0; k < 3; ++k)
                    if (double.IsInfinity(a[k]) == false)
                        AssertWithin(a[k], e, p[k] << S, p.W, S, $"rational point {p} coordinate {k}");
            }
        }

        [Fact]
        public void ExactPoint3_Indexer_RejectsIndicesOutsideXYZ()
        {
            ExactPoint3 p = ExactPoint3.Create(1, 2, 3, 5);
            Assert.Equal(new BigInteger(1), p[0]);
            Assert.Equal(new BigInteger(2), p[1]);
            Assert.Equal(new BigInteger(3), p[2]);
            Assert.Throws<ArgumentOutOfRangeException>(() => p[3]);
            Assert.Throws<ArgumentOutOfRangeException>(() => p[-1]);
        }
    }
}
