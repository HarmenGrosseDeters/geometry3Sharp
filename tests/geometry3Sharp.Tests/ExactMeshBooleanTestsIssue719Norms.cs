using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #719: the overflow-safe norms and far terms of the Antipodal classification and L1 and L2's gradient bounds
    /// (ExactMeshBoolean.scaled_norm, far_term): exact over the whole exponent range, bit-identical to the old arithmetic in range, scale-invariant out of it. (A separate file: it uses internals
    /// the base does not have, so the regression tests of ExactMeshBooleanTestsIssue719 still run on it.)
    /// </summary>
    public class ExactMeshBooleanTestsIssue719Norms
    {
        [Fact]
        public void ScaledNorm_ExactOverTheWholeExponentRange()
        {
            // ((3, 4, 12) has norm 13; scaled by its largest component's power of two the norm is exact)
            for (int k = -1072; k <= 1019; ++k) {
                double s = ExactPoint3.ScaleB(1, k);
                Assert.Equal(13 * s, ExactMeshBoolean.scaled_norm(3 * s, -4 * s, 12 * s));
                Assert.Equal(13 * s, ExactMeshBoolean.scaled_norm(-12 * s, 3 * s, 4 * s));
            }
            Assert.Equal(0, ExactMeshBoolean.scaled_norm(0, 0, 0));
            Assert.Equal(double.PositiveInfinity, ExactMeshBoolean.scaled_norm(1, double.NegativeInfinity, 0));
            Assert.True(double.IsNaN(ExactMeshBoolean.scaled_norm(1, double.NaN, 0)));
            // (where the squares are in range: within an ulp or two of the plain norm)
            var rand = new Random(719);
            for (int i = 0; i < 1000; ++i) {
                Vector3d v = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5) * Math.Pow(10, rand.Next(-90, 90));
                Assert.True(Math.Abs(ExactMeshBoolean.scaled_norm(v.x, v.y, v.z) - v.Length) <= 4.5e-16 * v.Length);
            }
        }

        [Fact]
        public void AntipodalValue_SameAtEveryPowerOfTwoScale()
        {
            // (a closed square loop around the point and a ray along +z: the boundary sum is -1/2 of a turn at any scale; the
            // differences squared overflow above 2^511 and lose their precision below 2^-511, without the guard of the
            // norms the value is wrong or no value at all)
            Vector3d[] loop = { new Vector3d(-1, -1, 0.25), new Vector3d(1, -1, 0.25), new Vector3d(1, -1, 0.25), new Vector3d(1, 1, 0.25),
                new Vector3d(1, 1, 0.25), new Vector3d(-1, 1, 0.25), new Vector3d(-1, 1, 0.25), new Vector3d(-1, -1, 0.25) };
            Vector3d p = new Vector3d(0.125, -0.0625, 0), x1 = new Vector3d(0, 0, -1);
            double w0, b0, r0;
            int s0 = ExactMeshBoolean.antipodal_value(p, p.x + Math.Abs(p.y), x1, 0, loop, out w0, out b0, out r0, 0, 0, 1e-6);
            Assert.Equal(0, s0);
            foreach (int k in new[] { -900, -700, -560, 560, 700, 900 }) {
                double sc = ExactPoint3.ScaleB(1, k);
                Vector3d[] g = loop.Select(v => v * sc).ToArray();
                Vector3d ps = p * sc;
                double w, b, r;
                int st = ExactMeshBoolean.antipodal_value(ps, (p.x + Math.Abs(p.y)) * sc, x1, 0, g, out w, out b, out r, 0, 0, 1e-6);
                Assert.Equal(0, st);
                Assert.True(Math.Abs(w - w0) <= 1e-14, $"2^{k}: {w} for {w0}");
                Assert.True(b <= 2 * b0 + 1e-15, $"2^{k}: bound {b} for {b0}");
            }
        }

        [Fact]
        public void BoxDistance_SameAtEveryPowerOfTwoScale()
        {
            // (boxes min x, y, z, max x, y, z with gaps 3, 4, 12 on the axes: distance 13 at any scale)
            double[] x = { 0, 0, 0, 1, 1, 1 }, y = { 4, -5, 13, 6, -4, 14 };
            Assert.Equal(13, ExactMeshBoolean.box_distance(x, 0, y, 0));
            for (int k = -1000; k <= 1000; k += 50) {
                double sc = ExactPoint3.ScaleB(1, k);
                double[] xs = x.Select(v => v * sc).ToArray(), ys = y.Select(v => v * sc).ToArray();
                double d = ExactMeshBoolean.box_distance(xs, 0, ys, 0);
                // (where the squares underflow the distance may be smaller: a lower bound, which only weakens far_term's)
                if (k >= -450)
                    Assert.Equal(13 * sc, d);
                else
                    Assert.True(d <= 13 * sc, $"2^{k}: {d}");
            }
            Assert.Equal(0, ExactMeshBoolean.box_distance(x, 0, x, 0));
        }

        [Fact]
        public void FarTerm_ScalesAsTheBiotSavartBound()
        {
            // (length and distance scaled by s: the bound scales by 1/s; where 4 pi d^2 is finite the old formula, bit for bit)
            double[] lengths = { 0.5, 3, 40 }, counts = { 1, 7 }, distances = { 0.25, 2, 100 };
            foreach (double length in lengths)
                foreach (double count in counts)
                    foreach (double d in distances) {
                        double g = ExactMeshBoolean.far_term(length, count, d);
                        Assert.Equal(Math.Min(length / (4 * Math.PI * d * d), count / (2 * Math.PI * d)), g);
                        foreach (int k in new[] { -900, -600, -340, 340, 600, 900 }) {
                            double s = ExactPoint3.ScaleB(1, k);
                            double gs = ExactMeshBoolean.far_term(length * s, count, d * s) * s;
                            // (large: the same bound; small, where 4 pi d^2 underflows to 0, the old expression's
                            // length term is infinite and the count term remains: larger, still a bound)
                            if (k > 0)
                                Assert.True(Math.Abs(gs - g) <= 1e-14 * g, $"2^{k}: {gs} for {g}");
                            else
                                Assert.True(gs >= g * (1 - 1e-14), $"2^{k}: {gs} for {g}");
                        }
                    }
            // (a term that underflows stays positive, an undefined one is unbounded)
            Assert.Equal(double.Epsilon, ExactMeshBoolean.far_term(1e-300, 1, 1e300));
            Assert.Equal(double.Epsilon, ExactMeshBoolean.far_term(1, 1, double.PositiveInfinity));
            Assert.Equal(0, ExactMeshBoolean.far_term(0, 0, 1e300));
            Assert.Equal(double.PositiveInfinity, ExactMeshBoolean.far_term(double.PositiveInfinity, 1, double.PositiveInfinity));
            Assert.Equal(double.PositiveInfinity, ExactMeshBoolean.far_term(1, 1, 0));
            Assert.Equal(double.PositiveInfinity, ExactMeshBoolean.far_term(1, 1, double.NaN));
        }
    }
}
