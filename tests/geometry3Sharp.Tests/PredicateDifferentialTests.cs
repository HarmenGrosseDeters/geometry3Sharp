using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Differential tests of the filtered predicates against exact integer evaluation (docs/QA.md 2.4), aimed at the
    /// floating-point filters: a filter decides from the double determinant when it exceeds an error bound, so a bound
    /// that is too small gives a wrong sign exactly when the rounding error is large and the true determinant is small.
    /// The inputs are therefore nearly degenerate (a point a few ulps off a line, plane, circle or sphere) with
    /// differences of very different magnitudes per point and far from the origin, where the rounding errors of the
    /// products are large compared with the result. Mutation testing (tests/mutation) showed that the earlier tests,
    /// with coordinates of similar size, let bounds shrink without a failure.
    /// </summary>
    public class PredicateDifferentialTests
    {
        const int Cases = 20000;

        static double Ulps(double x, Random rnd, int k)
        {
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + rnd.Next(-k, k + 1));
        }

        // a power of two in [2^lo, 2^hi]: exact scaling, independent magnitudes per point
        static double Pow2(Random rnd, int lo, int hi) { return Math.Pow(2, rnd.Next(lo, hi + 1)); }

        static double Signed(Random rnd) { return rnd.NextDouble() - 0.5; }

        static Vector3d Vec3(Random rnd, double scale) { return scale * new Vector3d(Signed(rnd), Signed(rnd), Signed(rnd)); }
        static Vector2d Vec2(Random rnd, double scale) { return scale * new Vector2d(Signed(rnd), Signed(rnd)); }

        static Vector3d Perturb(Vector3d v, Random rnd, int k) { return new Vector3d(Ulps(v.x, rnd, k), Ulps(v.y, rnd, k), Ulps(v.z, rnd, k)); }
        static Vector2d Perturb(Vector2d v, Random rnd, int k) { return new Vector2d(Ulps(v.x, rnd, k), Ulps(v.y, rnd, k)); }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Orient2d_NearlyCollinear_AnisotropicMagnitudes(int seed)
        {
            var rnd = new Random(seed);
            for (int i = 0; i < Cases; ++i) {
                Vector2d origin = Vec2(rnd, Pow2(rnd, -10, 30));
                Vector2d a = origin + Vec2(rnd, Pow2(rnd, -20, 20));
                Vector2d dir = Vec2(rnd, Pow2(rnd, -20, 20));
                Vector2d b = a + dir;
                // c on the line, before, between or far beyond a and b
                double t = Signed(rnd) * Pow2(rnd, -10, 20);
                Vector2d c = Perturb(a + t * dir, rnd, 3);
                int exact = ExactPredicates.orient2d_bigint(a, b, c);
                Assert.True(exact == ExactPredicates.Orient2d(a, b, c), $"case {i}: {a} {b} {c}");
                Assert.True(-exact == ExactPredicates.Orient2d(b, a, c), $"case {i} swapped");
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Orient3d_NearlyCoplanar_AnisotropicMagnitudes(int seed)
        {
            var rnd = new Random(seed);
            for (int i = 0; i < Cases; ++i) {
                Vector3d origin = Vec3(rnd, Pow2(rnd, -10, 30));
                Vector3d a = origin + Vec3(rnd, Pow2(rnd, -20, 20));
                Vector3d u = Vec3(rnd, Pow2(rnd, -20, 20)), v = Vec3(rnd, Pow2(rnd, -20, 20));
                Vector3d b = a + u, c = a + v;
                double s = Signed(rnd) * Pow2(rnd, -10, 20), t = Signed(rnd) * Pow2(rnd, -10, 20);
                Vector3d d = Perturb(a + s * u + t * v, rnd, 3);
                int exact = ExactPredicates.orient3d_bigint(a, b, c, d);
                Assert.True(exact == ExactPredicates.Orient3d(a, b, c, d), $"case {i}: {a} {b} {c} {d}");
                Assert.True(-exact == ExactPredicates.Orient3d(b, a, c, d), $"case {i} swapped");
                // the point that is nearly in the plane as the first argument (other products are large)
                Assert.True(-exact == ExactPredicates.Orient3d(d, b, c, a), $"case {i} rotated");
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void InCircle_NearlyCocircular_AnisotropicMagnitudes(int seed)
        {
            var rnd = new Random(seed);
            Vector2d[] p = new Vector2d[4];
            for (int i = 0; i < Cases; ++i) {
                Vector2d center = Vec2(rnd, Pow2(rnd, -10, 30));
                double radius = Pow2(rnd, -20, 20) * (0.5 + rnd.NextDouble());
                // angles clustered or spread: clustered points make the 2x2 minors small and the lifts large
                double spread = (rnd.Next(2) == 0) ? 2 * Math.PI : Pow2(rnd, -20, -1);
                double start = rnd.NextDouble() * 2 * Math.PI;
                for (int k = 0; k < 4; ++k) {
                    double th = start + spread * rnd.NextDouble();
                    p[k] = Perturb(center + radius * new Vector2d(Math.Cos(th), Math.Sin(th)), rnd, 3);
                }
                int exact = ExactPredicates.incircle_bigint(p[0], p[1], p[2], p[3]);
                Assert.True(exact == ExactPredicates.InCircle(p[0], p[1], p[2], p[3]), $"case {i}: {p[0]} {p[1]} {p[2]} {p[3]}");
                Assert.True(-exact == ExactPredicates.InCircle(p[1], p[0], p[2], p[3]), $"case {i} swapped");
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void InSphere_NearlyCospherical_AnisotropicMagnitudes(int seed)
        {
            var rnd = new Random(seed);
            Vector3d[] p = new Vector3d[5];
            for (int i = 0; i < Cases; ++i) {
                Vector3d center = Vec3(rnd, Pow2(rnd, -10, 30));
                double radius = Pow2(rnd, -20, 20) * (0.5 + rnd.NextDouble());
                double spread = (rnd.Next(2) == 0) ? 1 : Pow2(rnd, -20, -1);
                Vector3d axis = Vec3(rnd, 1).Normalized;
                for (int k = 0; k < 5; ++k) {
                    // points in a cap around axis (spread 1: the whole sphere)
                    Vector3d dir = (axis + spread * 2 * Vec3(rnd, 1)).Normalized;
                    p[k] = Perturb(center + radius * dir, rnd, 3);
                }
                int exact = ExactPredicates.insphere_bigint(p[0], p[1], p[2], p[3], p[4]);
                Assert.True(exact == ExactPredicates.InSphere(p[0], p[1], p[2], p[3], p[4]), $"case {i}: {p[0]} {p[1]} {p[2]} {p[3]} {p[4]}");
                Assert.True(-exact == ExactPredicates.InSphere(p[1], p[0], p[2], p[3], p[4]), $"case {i} swapped");
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void InSphere_OnePointFarOutOfPlane_OthersNearlyCoplanar(int far)
        {
            // e = 0, one point far along x and one far along z, the other two nearly on a line through e in the plane
            // z = 0: the z differences of three points are 0 and of one is large, so the permanent's products of
            // 2x2 minors and z differences are of very different sizes. A bound computed with a wrong combination of
            // them (eg cdp / bezp instead of cdp * bezp) is too small here; the filter then returns wrong signs.
            var rnd = new Random(41 + far);
            Vector3d[] p = new Vector3d[4];
            for (int i = 0; i < Cases; ++i) {
                Vector2d c = new Vector2d(0.5 + rnd.NextDouble(), 0.5 + rnd.NextDouble());
                double t = 1 + 4 * rnd.NextDouble();
                Vector3d cc = new Vector3d(c.x, c.y, 0), dd = new Vector3d(Ulps(t * c.x, rnd, 3), Ulps(t * c.y, rnd, 3), 0);
                Vector3d ax = new Vector3d(Math.Pow(2, 60), 0, 0), bz = new Vector3d(0, 0, Math.Pow(2, 20));
                // the far-z point at position far, the far-x point at each of the other positions in turn (the term
                // with the far-x point's lift dominates the permanent, so every term needs a case where it does not)
                int farX = (far + 1 + i % 3) % 4;
                int j = 0;
                Vector3d[] near = { cc, dd };
                for (int k = 0; k < 4; ++k)
                    p[k] = (k == far) ? bz : (k == farX) ? ax : near[j++];
                Vector3d e = Vector3d.Zero;
                int exact = ExactPredicates.insphere_bigint(p[0], p[1], p[2], p[3], e);
                Assert.True(exact == ExactPredicates.InSphere(p[0], p[1], p[2], p[3], e), $"case {i}: {p[0]} {p[1]} {p[2]} {p[3]}");
            }
        }
    }
}
