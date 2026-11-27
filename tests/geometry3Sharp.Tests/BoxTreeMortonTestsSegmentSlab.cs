using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// (named for the box tree shards of tests/mutation, whose filter takes BoxTreeMortonTests*) TriangleBoxTree.QuerySegment
    /// is conservative (#1673): every box that the segment meets exactly (a rational oracle in
    /// BigInteger, closed boxes) is reported, also for near-parallel segments (|q - p| &lt; 1e-300 along an axis, the slab
    /// rule of the ends) and over the whole exponent range; under Similarity.All() and far 2^k scales the exact hits stay
    /// reported (bit-identical inputs give the same exact verdict).
    /// </summary>
    public class BoxTreeMortonTestsSegmentSlab
    {
        static Vector3d Scaled(Vector3d v, int k) => new Vector3d(ExactPoint3.ScaleB(v.x, k), ExactPoint3.ScaleB(v.y, k), ExactPoint3.ScaleB(v.z, k));

        // the coordinates as integers in units of the smallest exponent among them (a positive common scale)
        static BigInteger[][] Integers(params Vector3d[] pts)
        {
            var ms = new BigInteger[pts.Length * 3];
            var es = new int[pts.Length * 3];
            int emin = int.MaxValue;
            for (int i = 0; i < pts.Length * 3; ++i) {
                double v = pts[i / 3][i % 3];
                long bits = BitConverter.DoubleToInt64Bits(v);
                int ex = (int)((bits >> 52) & 0x7FF);
                long frac = bits & 0xFFFFFFFFFFFFFL;
                if (ex == 0) es[i] = -1074; else { frac |= 1L << 52; es[i] = ex - 1075; }
                ms[i] = bits < 0 ? -new BigInteger(frac) : new BigInteger(frac);
                if (ms[i].IsZero == false) emin = Math.Min(emin, es[i]);
            }
            var r = new BigInteger[pts.Length][];
            for (int i = 0; i < pts.Length; ++i) {
                r[i] = new BigInteger[3];
                for (int j = 0; j < 3; ++j)
                    r[i][j] = ms[3 * i + j].IsZero ? BigInteger.Zero : ms[3 * i + j] << (es[3 * i + j] - emin);
            }
            return r;
        }

        // does the segment p-q meet the closed box [lo, hi], exactly: the t-intervals of the three slabs and [0, 1] meet
        static bool Meets(Vector3d p, Vector3d q, Vector3d lo, Vector3d hi)
        {
            var I = Integers(p, q, lo, hi);
            // t = num / den, den > 0; the interval [a, b] as fractions
            BigInteger an = 0, ad = 1, bn = 1, bd = 1;
            for (int k = 0; k < 3; ++k) {
                BigInteger P = I[0][k], D = I[1][k] - I[0][k], L = I[2][k], H = I[3][k];
                if (D.IsZero) {
                    if (P < L || P > H) return false;
                    continue;
                }
                BigInteger n0 = L - P, n1 = H - P, den = D;
                if (den.Sign < 0) { n0 = -n0; n1 = -n1; den = -den; var tmp = n0; n0 = n1; n1 = tmp; }
                // max(a, n0 / den), min(b, n1 / den)
                if (n0 * ad > an * den) { an = n0; ad = den; }
                if (n1 * bd < bn * den) { bn = n1; bd = den; }
                if (an * bd > bn * ad) return false;
            }
            return true;
        }

        static double Value(Random r, int e)
        {
            double v = ExactPoint3.ScaleB(1 + r.Next(64) / 64.0, e);
            return r.Next(2) == 0 ? v : -v;
        }

        // boxes and segments: integer grids, near-parallel segments straddling a face, and random values over the range
        static IEnumerable<(Vector3d lo, Vector3d hi, Vector3d p, Vector3d q)> Cases(int seed, int count)
        {
            var r = new Random(seed);
            for (int it = 0; it < count; ++it) {
                Vector3d a, b, p, q;
                int kind = it % 3;
                if (kind == 0) {
                    Func<Vector3d> g = () => new Vector3d(r.Next(-4, 5), r.Next(-4, 5), r.Next(-4, 5));
                    a = g(); b = g(); p = g(); q = g();
                    int k = r.Next(-1070, 1000);
                    a = Scaled(a, k); b = Scaled(b, k); p = Scaled(p, k); q = Scaled(q, k);
                } else if (kind == 1) {
                    // a face at x = f, the segment's x from just below to just above (or both on one side), |dx| < 1e-300
                    int e = r.Next(-1074, -997);
                    double f = Value(r, e), lo = r.Next(2) == 0 ? f : f - 1, hi = lo == f ? f + 1 : f;
                    double px = f + Value(r, Math.Max(-1074, e - r.Next(3))), qx = f + Value(r, Math.Max(-1074, e - r.Next(3)));
                    a = new Vector3d(lo, -1, -1); b = new Vector3d(hi, 1, 1);
                    p = new Vector3d(px, r.NextDouble() * 4 - 2, r.NextDouble() * 4 - 2);
                    q = new Vector3d(qx, p.y + r.Next(-1, 2), p.z + r.Next(-1, 2));
                } else {
                    int e = r.Next(-1074, 1000);
                    Func<double> v = () => Value(r, Math.Max(-1074, Math.Min(1000, e - r.Next(-4, 5))));
                    a = new Vector3d(v(), v(), v()); b = new Vector3d(v(), v(), v()); p = new Vector3d(v(), v(), v()); q = new Vector3d(v(), v(), v());
                }
                var lo3 = new Vector3d(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z));
                var hi3 = new Vector3d(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
                if (lo3.IsFinite && hi3.IsFinite && p.IsFinite && q.IsFinite)
                    yield return (lo3, hi3, p, q);
            }
        }

        static bool Reported(Vector3d lo, Vector3d hi, Vector3d p, Vector3d q)
        {
            var tree = new TriangleBoxTree(new List<AxisAlignedBox3d> { new AxisAlignedBox3d(lo, hi) });
            bool found = false;
            tree.QuerySegment(p, q, 0, i => found = true);
            return found;
        }

        [Fact]
        public void QuerySegment_ReportsEveryBoxTheSegmentMeets()
        {
            int meets = 0, nearParallel = 0, n = 0;
            foreach (var (lo, hi, p, q) in Cases(1673, 9000)) {
                bool m = Meets(p, q, lo, hi);
                if (m) {
                    meets++;
                    Assert.True(Reported(lo, hi, p, q), $"box {lo:R} - {hi:R}, segment {p:R} - {q:R} meets it but is culled");
                    Assert.True(Reported(lo, hi, q, p), $"reversed: box {lo:R} - {hi:R}, segment {q:R} - {p:R}");
                    if (Math.Abs(q.x - p.x) < 1e-300 && (p.x < lo.x) != (q.x < lo.x)) nearParallel++;
                }
                n++;
            }
            Assert.True(n > 8500 && meets > 1500 && nearParallel > 200, $"{n}, {meets}, near parallel across a face {nearParallel}");
        }

        [Fact]
        public void QuerySegment_CullsANearParallelSegmentOutsideEitherSideOfTheSlab()
        {
            // the rule still culls: both ends strictly below the low face, or both strictly above the high face (x), and
            // the same with the other axes
            double f = 1e-310, g = 2e-310;
            for (int k = 0; k < 3; ++k) {
                Func<double, Vector3d> at = (x) => { var v = new Vector3d(0.5, 0.5, 0.5); v[k] = x; return v; };
                Vector3d lo = new Vector3d(-1, -1, -1), hi = new Vector3d(1, 1, 1);
                lo[k] = g; hi[k] = 1;
                Assert.False(Reported(lo, hi, at(0), at(f)), $"axis {k}: below the low face");
                Assert.True(Reported(lo, hi, at(f), at(3e-310)), $"axis {k}: across the low face");
                lo[k] = -1; hi[k] = f;
                Assert.False(Reported(lo, hi, at(g), at(3e-310)), $"axis {k}: above the high face");
                Assert.True(Reported(lo, hi, at(0), at(g)), $"axis {k}: across the high face");
            }
        }

        [Fact]
        public void QuerySegment_EndsAboutTwoTimesMaxValueApart_AreNotCulled()
        {
            // q - p overflows to infinity for finite ends: both quotients were 0 and the slab t in [0, 1e-12], so a
            // segment through the box was culled (review of #1673: p = (-1e308, -1, 0), q = (1e308, 1, 0) meets the box
            // [-1, 1] x [-0.1, 0.1] x [-1, 1] at t = 0.5)
            var lo = new Vector3d(-1, -0.1, -1); var hi = new Vector3d(1, 0.1, 1);
            var p = new Vector3d(-1e308, -1, 0); var q = new Vector3d(1e308, 1, 0);
            Assert.True(double.IsInfinity(q.x - p.x));
            Assert.True(Meets(p, q, lo, hi));
            Assert.True(Reported(lo, hi, p, q));
            Assert.True(Reported(lo, hi, q, p));
            // and still culled where it misses: x meets the slab near t = 0.5, y only for t in [0.3, 0.4]
            var lo2 = new Vector3d(-1, -0.4, -1); var hi2 = new Vector3d(1, -0.2, 1);
            Assert.False(Meets(p, q, lo2, hi2));
            Assert.False(Reported(lo2, hi2, p, q));
            Assert.False(Reported(lo2, hi2, q, p));
            // seeded: ends near both ends of the double range on one or more axes, against the oracle; a box well off the
            // segment's line is still culled
            var r = new Random(5);
            int hits = 0, culled = 0;
            for (int it = 0; it < 3000; ++it) {
                Func<double> big = () => (r.Next(2) == 0 ? 1 : -1) * double.MaxValue * (0.5 + 0.5 * r.NextDouble());
                var a = new Vector3d(r.NextDouble() * 8 - 4, r.NextDouble() * 8 - 4, r.NextDouble() * 8 - 4);
                var b = a + new Vector3d(r.NextDouble() * 4, r.NextDouble() * 4, r.NextDouble() * 4);
                var pp = new Vector3d(big(), r.Next(3) == 0 ? big() : r.NextDouble() * 8 - 4, r.NextDouble() * 8 - 4);
                var qq = new Vector3d(-Math.Sign(pp.x) * Math.Abs(big()), r.Next(3) == 0 ? -pp.y : r.NextDouble() * 8 - 4, r.NextDouble() * 8 - 4);
                bool m = Meets(pp, qq, a, b);
                if (m) {
                    hits++;
                    Assert.True(Reported(a, b, pp, qq), $"{it}: box {a:R} - {b:R}, segment {pp:R} - {qq:R}");
                } else if (Reported(a, b, pp, qq) == false)
                    culled++;
            }
            Assert.True(hits > 100 && culled > 100, $"{hits} hits, {culled} misses culled");
        }

        [Fact]
        public void QuerySegment_ManyBoxes_ReportsEveryBoxTheSegmentMeets()
        {
            // one tree of 300 boxes (node boxes too), near-parallel segments along x through tiny faces
            var r = new Random(7);
            var boxes = new List<AxisAlignedBox3d>();
            for (int i = 0; i < 300; ++i) {
                double x0 = Value(r, r.Next(-1074, -1000)), x1 = x0 + Math.Abs(Value(r, r.Next(-1074, -1000)));
                double y0 = r.Next(-8, 8), z0 = r.Next(-8, 8);
                boxes.Add(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y0 + r.Next(1, 4), z0 + r.Next(1, 4))));
            }
            var tree = new TriangleBoxTree(boxes);
            int hits = 0;
            for (int it = 0; it < 400; ++it) {
                var p = new Vector3d(Value(r, r.Next(-1074, -1000)), r.NextDouble() * 16 - 8, r.NextDouble() * 16 - 8);
                var q = new Vector3d(Value(r, r.Next(-1074, -1000)), p.y + r.NextDouble() * 4 - 2, p.z + r.NextDouble() * 4 - 2);
                var found = new HashSet<int>();
                tree.QuerySegment(p, q, 0, i => found.Add(i));
                for (int i = 0; i < boxes.Count; ++i)
                    if (Meets(p, q, boxes[i].Min, boxes[i].Max)) {
                        hits++;
                        Assert.True(found.Contains(i), $"query {it}: box {i} missed");
                    }
            }
            Assert.True(hits > 50, $"{hits}");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void QuerySegment_AllSimilarities_TheExactHitsStayReported(string name)
        {
            // 2^k (IsExact): the scaled input meets the scaled box iff the input does (bit-identical exact verdict), also at
            // 50 k and -50 k where the values stay exact, and the hit is reported at every one of these scales; the other
            // similarities round the input: the oracle on the transformed doubles, and those hits reported
            Similarity sim = Similarity.ByName(name);
            int compared = 0;
            foreach (var (lo, hi, p, q) in Cases(29, 1500)) {
                bool m = Meets(p, q, lo, hi);
                if (sim.IsExact) {
                    int k = (int)Math.Round(Math.Log(sim.Scale, 2));
                    foreach (int kk in new[] { k, 50 * k, -50 * k }) {
                        Vector3d sl = Scaled(lo, kk), sh = Scaled(hi, kk), sp = Scaled(p, kk), sq = Scaled(q, kk);
                        if (Scaled(sl, -kk) != lo || Scaled(sh, -kk) != hi || Scaled(sp, -kk) != p || Scaled(sq, -kk) != q)
                            continue;
                        Assert.Equal(m, Meets(sp, sq, sl, sh));
                        if (m) Assert.True(Reported(sl, sh, sp, sq), $"{name} 2^{kk}: box {lo:R} - {hi:R}, segment {p:R} - {q:R}");
                        compared++;
                    }
                    continue;
                }
                // (an axis-aligned box rotated is not a box: the box of the rotated corners, and the oracle on that)
                if (lo.MaxAbs > 1e100 || hi.MaxAbs > 1e100 || p.MaxAbs > 1e100 || q.MaxAbs > 1e100) continue;
                var box = new AxisAlignedBox3d(sim.Apply(lo));
                for (int c = 0; c < 8; ++c) box.Contain(sim.Apply(new Vector3d((c & 1) != 0 ? hi.x : lo.x, (c & 2) != 0 ? hi.y : lo.y, (c & 4) != 0 ? hi.z : lo.z)));
                Vector3d tp = sim.Apply(p), tq = sim.Apply(q);
                if (Meets(tp, tq, box.Min, box.Max)) Assert.True(Reported(box.Min, box.Max, tp, tq), $"{name}: {p:R} - {q:R}");
                compared++;
            }
            Assert.True(compared > 500, $"{name}: {compared}");
        }
    }
}
