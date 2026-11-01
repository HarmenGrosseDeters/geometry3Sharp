using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactTriangleIntersection.IsDegenerate: exactly collinear corners, decided exactly. Its floating-point prefilter
    /// (n^2 > 1e-24 s^2) only shortcuts clearly non-degenerate triangles, so near its threshold the answer is the exact one.
    /// </summary>
    public class ExactTriangleIntersectionDegenerateTests
    {
        // collinear in 3D iff collinear in all three axis projections (exact Orient2d), the reference
        static bool Collinear(Vector3d a, Vector3d b, Vector3d c)
        {
            for (int k = 0; k < 3; ++k) {
                Func<Vector3d, Vector2d> drop = v => k == 0 ? new Vector2d(v.y, v.z) : k == 1 ? new Vector2d(v.z, v.x) : new Vector2d(v.x, v.y);
                if (ExactPredicates.Orient2d(drop(a), drop(b), drop(c)) != 0)
                    return false;
            }
            return true;
        }

        static Vector3d RandomUnit(Random r)
        {
            Vector3d v = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5);
            return v.Normalized;
        }

        [Fact]
        public void IsDegenerate_PrefilterThreshold()
        {
            // slivers whose height puts n^2 / s^2 within a factor 4 of the prefilter's 1e-24, at scales 2^-40..2^40, and
            // exactly collinear triples (dyadic multiples of one direction) at the same scales
            var rand = new Random(41);
            int nearThreshold = 0, below = 0, above = 0, collinear = 0;
            for (int n = 0; n < 40000; ++n) {
                double scale = Math.Pow(2, rand.Next(-40, 41));
                Vector3d p = scale * new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                Vector3d d = RandomUnit(rand), perp = d.Cross(RandomUnit(rand)).Normalized;
                Triangle3d t;
                if (n % 4 == 0) {
                    // exactly collinear: p, p + 2^i d', p + 2^j d' with d' a dyadic direction (exact products and sums)
                    Vector3d q = new Vector3d(rand.Next(0, 64), rand.Next(0, 64), rand.Next(0, 64)) * (scale / 64);
                    Vector3d dd = new Vector3d(rand.Next(-8, 9), rand.Next(-8, 9), rand.Next(1, 9)) * (scale / 8);
                    t = new Triangle3d(q, q + dd, q + 2 * dd);
                } else {
                    double len = scale * (0.5 + rand.NextDouble());
                    // n^2 / s^2 = h^2 len^2 / (len^2 + |q - p|^2)^2: about (h / len)^2 / (1 + u^2)^2
                    double u = rand.NextDouble();
                    double ratio = 1e-12 * Math.Pow(2, 4 * rand.NextDouble() - 2) * (1 + u * u);
                    t = new Triangle3d(p, p + len * d, p + u * len * d + ratio * len * perp);
                }
                Vector3d e1 = t.V1 - t.V0, e2 = t.V2 - t.V0;
                double s = e1.LengthSquared + e2.LengthSquared, nn = e1.Cross(e2).LengthSquared;
                bool expected = Collinear(t.V0, t.V1, t.V2);
                Assert.True(expected == ExactTriangleIntersection.IsDegenerate(ref t), $"{t.V0} {t.V1} {t.V2}: expected {expected}");
                // the 2^k scales give the same answer
                Triangle3d t2 = new Triangle3d(t.V0 * 1024, t.V1 * 1024, t.V2 * 1024);
                Assert.Equal(expected, ExactTriangleIntersection.IsDegenerate(ref t2));
                if (expected) collinear++;
                else {
                    double r = nn / (s * s);
                    if (r > 0.25e-24 && r < 4e-24) nearThreshold++;
                    if (nn > 1e-24 * (s * s)) above++; else below++;
                }
            }
            // the family is what it claims: collinear triples, and slivers on both sides of the prefilter's threshold
            Assert.True(collinear > 5000, $"collinear {collinear}");
            Assert.True(nearThreshold > 10000 && below > 5000 && above > 5000, $"near {nearThreshold}, below {below}, above {above}");
        }
    }
}
