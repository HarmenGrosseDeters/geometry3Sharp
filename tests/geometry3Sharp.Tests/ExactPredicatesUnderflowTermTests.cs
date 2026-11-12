using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The filters of Orient3d, InCircle and InSphere add their underflow term (sum times 2^-1066, a subnormal product)
    /// only where it can change the bound (ExactPredicates.with_underflow). The decisions must be those of the full
    /// expression bit for bit, and the predicates stay exact around the threshold.
    /// </summary>
    public class ExactPredicatesUnderflowTermTests
    {
        const double Epsilon = 1.1102230246251565e-16;
        const double Orient3dBound = (7.0 + 56.0 * Epsilon) * Epsilon;
        const double UnderflowUnit = double.Epsilon * 256;

        // the filter as it was before: the underflow term on every call
        static int Orient3dFilterFull(double ux, double uy, double uz, double vx, double vy, double vz, double wx, double wy, double wz)
        {
            double uyvz = uy * vz, uzvy = uz * vy, uzvx = uz * vx, uxvz = ux * vz, uxvy = ux * vy, uyvx = uy * vx;
            double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);
            double permanent = Math.Abs(wx) * (Math.Abs(uyvz) + Math.Abs(uzvy))
                + Math.Abs(wy) * (Math.Abs(uzvx) + Math.Abs(uxvz))
                + Math.Abs(wz) * (Math.Abs(uxvy) + Math.Abs(uyvx));
            double bound = Orient3dBound * permanent + (Math.Abs(wx) + Math.Abs(wy) + Math.Abs(wz) + 1) * UnderflowUnit;
            if (det > bound && permanent > 1e-200) return 1;
            if (-det > bound && permanent > 1e-200) return -1;
            return 0;
        }

        static double Coord(Random rnd, int lo, int hi)
        {
            int kind = rnd.Next(20);
            if (kind == 0) return 0;
            if (kind == 1) return (rnd.Next(2) == 0 ? 1 : -1) * double.Epsilon * rnd.Next(1, 1 << 20);
            if (kind == 2) return rnd.Next(3) == 0 ? double.NaN : (rnd.Next(2) == 0 ? double.PositiveInfinity : double.NegativeInfinity);
            return (rnd.NextDouble() - 0.5) * Math.Pow(2, rnd.Next(lo, hi));
        }

        [Fact]
        public void Orient3dFilter_DecidesAsTheFullUnderflowTerm()
        {
            // magnitudes over the whole range, and inputs around the two edges of the shortcut: |w| near 2^256 with
            // permanents near the underflow limit (u, v tiny), where the term is closest to half an ulp of the bound
            var rnd = new Random(20261112);
            int decided = 0, shortcut = 0;
            for (int i = 0; i < 400000; ++i) {
                double[] x = new double[9];
                int mode = i % 3;
                for (int k = 0; k < 9; ++k) {
                    if (mode == 0) x[k] = Coord(rnd, -1080, 1000);
                    else if (k < 6) x[k] = Coord(rnd, -480, -420);     // products near 2^-900, permanents near 1e-200 with |w| near 2^256
                    else x[k] = Coord(rnd, 250, 262);
                }
                if (rnd.Next(4) == 0)       // nearly coplanar: w close to a combination of u and v
                    for (int k = 0; k < 3; ++k)
                        x[6 + k] = 3 * x[k] - 5 * x[3 + k] + Coord(rnd, -1074, -1000);
                int full = Orient3dFilterFull(x[0], x[1], x[2], x[3], x[4], x[5], x[6], x[7], x[8]);
                int fast = ExactPredicates.Orient3dFilter(x[0], x[1], x[2], x[3], x[4], x[5], x[6], x[7], x[8]);
                Assert.Equal(full, fast);
                if (full != 0) decided++;
                if (Math.Abs(x[6]) + Math.Abs(x[7]) + Math.Abs(x[8]) + 1 < ExactPredicates.UnderflowSumNegligible) shortcut++;
            }
            // (both branches, and decisions in both, are exercised)
            Assert.True(decided > 100000 && shortcut > 100000 && shortcut < 390000, $"{decided} {shortcut}");
        }

        [Fact]
        public void Orient3dFilter_AtTheShortcutThreshold()
        {
            // w at 2^256 (full term) and just below (shortcut), u and v so that the permanent is just above 1e-200 and
            // det is within a few ulps of the bound: the same decisions as the full expression
            double t = ExactPredicates.UnderflowSumNegligible;
            var rnd = new Random(3);
            foreach (double w in new[] { t, t * (1 - Epsilon), t * (1 + 2 * Epsilon), t / 2, 2 * t, Math.Pow(2, 299), Math.Pow(2, 300) }) {
                for (int i = 0; i < 20000; ++i) {
                    double s = 1e-200 / w * (1 + rnd.NextDouble());
                    double ux = s, uy = s * (rnd.NextDouble() - 0.5), uz = 0, vx = 0, vy = s, vz = 0;
                    double wz = Orient3dBound * (1 + (rnd.NextDouble() - 0.5) * 1e-14) * w;    // det near the bound
                    double wx = (rnd.Next(2) == 0 ? 1 : -1) * w * Math.Sqrt(Math.Abs(uy) / s + 1e-300);
                    double[] x = { ux, uy, uz, vx, vy, vz, wx, w - Math.Abs(wx) / 2, (rnd.Next(2) == 0 ? 1 : -1) * wz * s * s };
                    Assert.Equal(Orient3dFilterFull(x[0], x[1], x[2], x[3], x[4], x[5], x[6], x[7], x[8]),
                        ExactPredicates.Orient3dFilter(x[0], x[1], x[2], x[3], x[4], x[5], x[6], x[7], x[8]));
                }
            }
        }

        [Fact]
        public void Predicates_Exact_AroundTheShortcutThreshold()
        {
            // the public predicates against the integer arithmetic with large coordinates (sums around 2^256) next to
            // tiny ones (subnormal partial products): Orient3d, InCircle, InSphere
            var rnd = new Random(11);
            Func<double> big = () => (rnd.NextDouble() - 0.5) * Math.Pow(2, rnd.Next(230, 290));
            Func<double> small = () => (rnd.NextDouble() - 0.5) * Math.Pow(2, rnd.Next(-560, -480));
            for (int i = 0; i < 20000; ++i) {
                Vector3d[] v = new Vector3d[5];
                for (int k = 0; k < 5; ++k)
                    v[k] = rnd.Next(2) == 0 ? new Vector3d(big(), small(), small()) : new Vector3d(small(), small(), rnd.Next(2) == 0 ? big() : small());
                Assert.Equal(ExactPredicates.orient3d_bigint(v[0], v[1], v[2], v[3]), ExactPredicates.Orient3d(v[0], v[1], v[2], v[3]));
                Assert.Equal(ExactPredicates.incircle_bigint(v[0].xy, v[1].xy, v[2].xy, v[3].xy), ExactPredicates.InCircle(v[0].xy, v[1].xy, v[2].xy, v[3].xy));
                if (i % 4 == 0) {
                    Vector3d[] e = new Vector3d[5];
                    for (int k = 0; k < 5; ++k)
                        e[k] = new Vector3d(small(), small(), small()) * Math.Pow(2, 140) + (rnd.Next(3) == 0 ? new Vector3d(big(), 0, 0) * Math.Pow(2, -200) : Vector3d.Zero);
                    Assert.Equal(ExactPredicates.insphere_bigint(e[0], e[1], e[2], e[3], e[4]), ExactPredicates.InSphere(e[0], e[1], e[2], e[3], e[4]));
                }
            }
        }

        [Fact]
        public void Predicates_InvariantUnderSimilarities()
        {
            // the signs of transformed inputs are those of the integer arithmetic on the transformed doubles; for the
            // exact similarities (2^k scales) they are the signs of the original, bit for bit
            var rnd = new Random(5);
            for (int i = 0; i < 3000; ++i) {
                Vector3d[] p = new Vector3d[5];
                for (int k = 0; k < 5; ++k)
                    p[k] = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                if (i % 2 == 0)     // nearly degenerate: d near the plane of a, b, c, e near the sphere
                    p[3] = p[0] + 0.3 * (p[1] - p[0]) + 0.6 * (p[2] - p[0]) + new Vector3d(1e-17, 0, 0);
                int o = ExactPredicates.Orient3d(p[0], p[1], p[2], p[3]);
                int c = ExactPredicates.InCircle(p[0].xy, p[1].xy, p[2].xy, p[3].xy);
                int s = ExactPredicates.InSphere(p[0], p[1], p[2], p[3], p[4]);
                foreach (Similarity sim in Similarity.All()) {
                    Vector3d[] q = new Vector3d[5];
                    for (int k = 0; k < 5; ++k)
                        q[k] = sim.Apply(p[k]);
                    int oq = ExactPredicates.Orient3d(q[0], q[1], q[2], q[3]);
                    int cq = ExactPredicates.InCircle(q[0].xy, q[1].xy, q[2].xy, q[3].xy);
                    int sq = ExactPredicates.InSphere(q[0], q[1], q[2], q[3], q[4]);
                    Assert.Equal(ExactPredicates.orient3d_bigint(q[0], q[1], q[2], q[3]), oq);
                    Assert.Equal(ExactPredicates.incircle_bigint(q[0].xy, q[1].xy, q[2].xy, q[3].xy), cq);
                    Assert.Equal(ExactPredicates.insphere_bigint(q[0], q[1], q[2], q[3], q[4]), sq);
                    if (sim.IsExact) {
                        Assert.Equal(o, oq);
                        Assert.Equal(c, cq);
                        Assert.Equal(s, sq);
                    }
                }
            }
        }
    }
}
