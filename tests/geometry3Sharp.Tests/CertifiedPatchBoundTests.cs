using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #848: the certified patch bound (TriangleDeviationBounds.certified_patch_bound, with CertifiedDet3), checked against exact
    /// arithmetic: the determinant's error bound against the exact determinant of the exact difference rows (ExactDyadic), the patch
    /// bound against the exact gap along the normal over horizontal triangles and the exact distance of dyadic samples
    /// (CertifiedPointMeshDistance.AtLeastDistance). docs/design/CERTIFIED_PATCH_BOUND.md
    /// </summary>
    public class CertifiedPatchBoundTests
    {
        static ExactDyadic D(double x) => ExactDyadic.Of(x);
        static ExactDyadic Abs(ExactDyadic x) => x.Sign < 0 ? -x : x;
        static double U = Math.Pow(2, -53);

        // ---------------------------------------------------------------- CertifiedDet3

        // the exact determinant of the exact rows q_i - p_i (the rounded rows are fl(q_i - p_i))
        static ExactDyadic ExactDet(Vector3d[] p, Vector3d[] q)
        {
            ExactDyadic[,] r = new ExactDyadic[3, 3];
            for (int i = 0; i < 3; ++i)
                for (int k = 0; k < 3; ++k)
                    r[i, k] = D(q[i][k]) - D(p[i][k]);
            return r[0, 0] * (r[1, 1] * r[2, 2] - r[1, 2] * r[2, 1]) - r[0, 1] * (r[1, 0] * r[2, 2] - r[1, 2] * r[2, 0])
                + r[0, 2] * (r[1, 0] * r[2, 1] - r[1, 1] * r[2, 0]);
        }

        // |det_c - exact| <= E, exactly; and a decided sign is the exact sign. Returns |det_c - exact| / (U perm_c) (0 if perm_c = 0)
        static double CheckDet(Vector3d[] p, Vector3d[] q, string what)
        {
            Vector3d r1 = q[0] - p[0], r2 = q[1] - p[1], r3 = q[2] - p[2];
            CertifiedDet3 d = CertifiedDet3.Compute(r1, r2, r3);
            if (double.IsPositiveInfinity(d.E)) {
                Assert.Equal(0, d.Sign);
                Assert.Equal(double.NegativeInfinity, d.Lo);
                Assert.Equal(double.PositiveInfinity, d.Hi);
                return 0;
            }
            ExactDyadic exact = ExactDet(p, q);
            ExactDyadic err = Abs(D(d.Det) - exact);
            Assert.True(ExactDyadic.Compare(err, D(d.E)) <= 0, $"{what}: |det_c - exact| exceeds E = {d.E:R} (det_c {d.Det:R})");
            if (d.Sign != 0) Assert.Equal(exact.Sign, d.Sign);
            // (the outward interval and the absolute bound hold too)
            Assert.True(ExactDyadic.Compare(D(d.Lo), exact) <= 0 && ExactDyadic.Compare(exact, D(d.Hi)) <= 0, what + ": [Lo, Hi]");
            Assert.True(ExactDyadic.Compare(Abs(exact), D(d.AbsUpper)) <= 0, what + ": AbsUpper");
            double perm = Math.Abs(r1.x) * (Math.Abs(r2.y * r3.z) + Math.Abs(r2.z * r3.y)) + Math.Abs(r1.y) * (Math.Abs(r2.z * r3.x) + Math.Abs(r2.x * r3.z))
                + Math.Abs(r1.z) * (Math.Abs(r2.x * r3.y) + Math.Abs(r2.y * r3.x));
            if (perm == 0) return 0;
            return (U * perm > 0) ? Quotient(err, D(U * perm)) : 0;
        }

        static double Quotient(ExactDyadic a, ExactDyadic b) => a.Sign == 0 ? 0 : ExactDyadic.Quotient(a, b);

        static double R(Random rnd, int e) => (rnd.NextDouble() + 0.5) * Math.Pow(2, e) * (rnd.Next(2) == 0 ? 1 : -1);

        static Vector3d V(Random rnd, int e) => new Vector3d(R(rnd, e), R(rnd, e), R(rnd, e));

        /// <summary> random rows at many exponents (each row a difference of two input points): the bound holds against the exact
        /// determinant, a decided sign is right. The largest observed error is a few U perm (the derived constant is 8.002 U, the
        /// one used 10 U): the cases have the power to catch a constant far below it </summary>
        [Fact]
        public void Det3_ErrorBound_HoldsAgainstExactDeterminant()
        {
            var rnd = new Random(848);
            double worst = 0; int decided = 0, n = 0;
            for (int it = 0; it < 2500; ++it) {
                int e = rnd.Next(-40, 40);
                Vector3d[] p = new Vector3d[3], q = new Vector3d[3];
                for (int i = 0; i < 3; ++i) { p[i] = V(rnd, e + rnd.Next(-3, 4)); q[i] = V(rnd, e + rnd.Next(-3, 4)); }
                double ratio = CheckDet(p, q, "random " + it);
                worst = Math.Max(worst, ratio);
                n++;
                if (CertifiedDet3.Compute(q[0] - p[0], q[1] - p[1], q[2] - p[2]).Sign != 0) decided++;
            }
            Assert.True(decided > n * 0.95, decided + " of " + n + " decided");
            Assert.True(worst <= 8.002, "observed " + worst);
        }

        /// <summary> near-dependent rows: the third is a combination of the first two plus a perturbation of a few ulps, rounded
        /// differences of nearly equal points: cancellation makes the determinant tiny against the permanent. The adversarial case of the
        /// bound: the largest |det_c - exact| / (U perm_c) over these is the observed constant </summary>
        [Fact]
        public void Det3_ErrorBound_NearDependentRows()
        {
            var rnd = new Random(849);
            double worst = 0; int undecided = 0, n = 0;
            for (int it = 0; it < 2500; ++it) {
                int e = rnd.Next(-30, 30);
                Vector3d b = V(rnd, e), a1 = V(rnd, e), a2 = V(rnd, e);
                double s = R(rnd, 0), t = R(rnd, 0);
                Vector3d[] p = { b, b, b }, q = { b + a1, b + a2, Vector3d.Zero };
                Vector3d comb = s * (q[0] - p[0]) + t * (q[1] - p[1]);
                // (the third row: the combination, perturbed by 0 to 8 ulps per component: these are offsets of the end point)
                q[2] = b + new Vector3d(Perturb(comb.x, rnd), Perturb(comb.y, rnd), Perturb(comb.z, rnd));
                worst = Math.Max(worst, CheckDet(p, q, "near-dependent " + it));
                n++;
                if (CertifiedDet3.Compute(q[0] - p[0], q[1] - p[1], q[2] - p[2]).Sign == 0) undecided++;
            }
            Assert.True(undecided > n / 4, undecided + " of " + n + " undecided: the cases are not near-dependent");
            Assert.True(worst <= 8.002 && worst > 0.5, "observed " + worst);
        }

        static double Perturb(double x, Random rnd)
        {
            for (int k = rnd.Next(0, 9); k > 0; --k) x = ExactInterval.Up(x);
            return x;
        }

        /// <summary> exactly dependent rows (the third is the sum of the others, exact): the determinant is 0 and the sign is never
        /// decided; and rows with a zero row, a zero column </summary>
        [Fact]
        public void Det3_ExactlyZero_IsNeverDecided()
        {
            var rnd = new Random(850);
            for (int it = 0; it < 300; ++it) {
                // dyadic rows with few bits: the sum is exact
                Vector3d a = new Vector3d(rnd.Next(-64, 64), rnd.Next(-64, 64), rnd.Next(-64, 64)) / 8, b = new Vector3d(rnd.Next(-64, 64), rnd.Next(-64, 64), rnd.Next(-64, 64)) / 8;
                var d = CertifiedDet3.Compute(a, b, a + b);
                Assert.Equal(0, d.Sign);
                Assert.True(d.Lo <= 0 && d.Hi >= 0);
                Assert.Equal(0, CertifiedDet3.Compute(a, Vector3d.Zero, b).Sign);
                Assert.Equal(0, CertifiedDet3.Compute(a, b, 3 * a - 2 * b).Sign);
            }
        }

        /// <summary> subnormal and near-underflow rows: the products lose bits (the absolute term A of the bound: without it a determinant
        /// of 2^-1620 computed as 0 would be outside E = 0), differences of subnormals are exact; large rows near overflow </summary>
        [Fact]
        public void Det3_Underflow_AndOverflow()
        {
            var rnd = new Random(851);
            for (int it = 0; it < 600; ++it) {
                int e = rnd.Next(0, 4) switch { 0 => -1074 + rnd.Next(0, 40), 1 => -540 + rnd.Next(-30, 30), 2 => -340 + rnd.Next(-20, 20), _ => 330 + rnd.Next(-20, 20) };
                Vector3d[] p = new Vector3d[3], q = new Vector3d[3];
                for (int i = 0; i < 3; ++i) { p[i] = V(rnd, e); q[i] = V(rnd, e); }
                CheckDet(p, q, $"exponent {e}, it {it}");
            }
            // a large first row against two tiny ones: the products of the tiny rows (2^-1080 and below) lose up to 2^-1075 each, times the
            // first row's entries (2^500): an error of 2^-575 against a permanent of 2^-580, which only the absolute term A (about
            // 2^-570) covers; without A (or with a sixteenth of it) the bound is below the exact error
            for (int it = 0; it < 300; ++it) {
                Vector3d[] bp = { Vector3d.Zero, Vector3d.Zero, Vector3d.Zero };
                Vector3d[] bq = { V(rnd, 500 + rnd.Next(-3, 4)), V(rnd, -541 + rnd.Next(-3, 4)), V(rnd, -541 + rnd.Next(-3, 4)) };
                CheckDet(bp, bq, "big first row " + it);
            }
            // mixed magnitudes (the case where A matters): r1 = (2^40, 0, 0), r2 = (0, 2^-537, 0), r3 = (0, 0, 1.5 2^-537): exact 0.75 2^-1033,
            // computed 2^-1033 (the products 2^-1074 and 1.5 2^-1074 round): an error of 2^-1035, far above 10 u perm (2^-1083), so a
            // bound that always takes the fast formula, or compares the permanent without the factor (M + 1), is below it
            {
                Vector3d[] zp = { Vector3d.Zero, Vector3d.Zero, Vector3d.Zero };
                CheckDet(zp, new[] { new Vector3d(Math.Pow(2, 40), 0, 0), new Vector3d(0, Math.Pow(2, -537), 0), new Vector3d(0, 0, 1.5 * Math.Pow(2, -537)) }, "mixed 2^40");
                for (int it = 0; it < 300; ++it) {
                    int e1 = rnd.Next(20, 120), e2 = -537 + rnd.Next(-6, 7);
                    CheckDet(zp, new[] { new Vector3d(R(rnd, e1), 0, 0), new Vector3d(0, R(rnd, e2), 0), new Vector3d(0, 0, R(rnd, e2)) }, "mixed " + it);
                    CheckDet(zp, new[] { V(rnd, e1), V(rnd, e2), V(rnd, e2 - 3) }, "mixed full " + it);
                }
            }
            // 2^-540: the products are 2^-1080 and below, the determinant 2^-1620: computed 0, exact nonzero, within A
            Vector3d t = new Vector3d(Math.Pow(2, -540), 0, 0);
            Vector3d[] z = { Vector3d.Zero, Vector3d.Zero, Vector3d.Zero };
            Vector3d[] rows = { t, new Vector3d(0, Math.Pow(2, -540), 0), new Vector3d(0, 0, Math.Pow(2, -540)) };
            var d3 = CertifiedDet3.Compute(rows[0], rows[1], rows[2]);
            Assert.Equal(0, d3.Det);
            Assert.True(d3.E > 0 && d3.E < 1e-300, d3.E.ToString("R"));
            CheckDet(z, rows, "2^-1620");
            Assert.Equal(0, d3.Sign);
            // overflow, NaN, infinity: undecided (E infinite), never a sign
            foreach (var rr in new[] {
                new[] { new Vector3d(1e200, 0, 0), new Vector3d(0, 1e200, 0), new Vector3d(0, 0, 1e200) },
                new[] { new Vector3d(double.NaN, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1) },
                new[] { new Vector3d(double.PositiveInfinity, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1) },
                new[] { new Vector3d(1e308, 1e308, 0), new Vector3d(1, 1, 0), new Vector3d(0, 0, 1) }, }) {
                var d = CertifiedDet3.Compute(rr[0], rr[1], rr[2]);
                Assert.Equal(0, d.Sign);
                Assert.True(double.IsPositiveInfinity(d.E));
            }
            // an infinity in a difference of finite points: 1.7e308 - (-1.7e308)
            Assert.Equal(0, CertifiedDet3.Compute(new Vector3d(1.7e308, 0, 0) - new Vector3d(-1.7e308, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1)).Sign);
        }

        /// <summary> the determinant of rows scaled by 2^k is the determinant scaled by 2^3k, and E too, bit for bit, while the absolute
        /// term A is absorbed by the rounding of 10 U perm (A does not scale: it is 2^-1070 (M + 1)). Where it is not absorbed (a
        /// permanent near 2^-1000) the bounds of the scaled rows differ from the scaled bound, and both hold against the exact
        /// determinant </summary>
        [Fact]
        public void Det3_ScalesBitForBit_WhereTheUnderflowTermIsAbsorbed()
        {
            var rnd = new Random(852);
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                for (int it = 0; it < 200; ++it) {
                    Vector3d[] p = new Vector3d[3], q = new Vector3d[3];
                    for (int i = 0; i < 3; ++i) { p[i] = V(rnd, 0); q[i] = V(rnd, 0); }
                    var d0 = CertifiedDet3.Compute(q[0] - p[0], q[1] - p[1], q[2] - p[2]);
                    var d1 = CertifiedDet3.Compute(s.Scale * q[0] - s.Scale * p[0], s.Scale * q[1] - s.Scale * p[1], s.Scale * q[2] - s.Scale * p[2]);
                    double f = s.Scale * s.Scale * s.Scale;
                    Assert.Equal(f * d0.Det, d1.Det);
                    Assert.Equal(f * d0.E, d1.E);
                }
            }
            // not absorbed: E is dominated by A = 2^-1070 (M + 1) (rows near 2^-340, products 2^-1020, determinant 2^-1360)
            Vector3d[] pp = { Vector3d.Zero, Vector3d.Zero, Vector3d.Zero };
            double h = Math.Pow(2, -340);
            Vector3d[] qq = { new Vector3d(h, 0.5 * h, 0), new Vector3d(0, h, 0.25 * h), new Vector3d(0.125 * h, 0, h) };
            var a = CertifiedDet3.Compute(qq[0], qq[1], qq[2]);
            Vector3d[] qs = qq.Select(v => 1024 * v).ToArray();
            var b = CertifiedDet3.Compute(qs[0], qs[1], qs[2]);
            Assert.NotEqual(a.E * Math.Pow(2, 30), b.E);
            CheckDet(pp, qq, "not absorbed"); CheckDet(pp, qs, "not absorbed, scaled");
        }

        // ---------------------------------------------------------------- the patch bound

        // a heightfield over [-n/2, n/2]^2, one unit cells, two triangles each (counter-clockwise seen from +z), z = k / 32
        static DMesh3 Heightfield(int n, Random rnd, double amplitude, double scale = 1, Vector3d? offset = null)
        {
            var m = new DMesh3();
            int[,] id = new int[n + 1, n + 1];
            Vector3d off = offset ?? Vector3d.Zero;
            for (int i = 0; i <= n; ++i)
                for (int j = 0; j <= n; ++j)
                    id[i, j] = m.AppendVertex(scale * new Vector3d(i - n / 2, j - n / 2, Math.Round(amplitude * (rnd.NextDouble() - 0.5) * 32) / 32) + off);
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    if ((i + j) % 2 == 0) {
                        m.AppendTriangle(id[i, j], id[i + 1, j], id[i + 1, j + 1]); m.AppendTriangle(id[i, j], id[i + 1, j + 1], id[i, j + 1]);
                    } else {
                        m.AppendTriangle(id[i, j], id[i + 1, j], id[i, j + 1]); m.AppendTriangle(id[i + 1, j], id[i + 1, j + 1], id[i, j + 1]);
                    }
                }
            return m;
        }

        // a triangle with xy corners at multiples of 1/64 in (-1.25, 1.25)^2, counter-clockwise, above the heightfield at z0 + ax + by
        static (Vector3d, Vector3d, Vector3d) RandomQ(Random rnd, double z0, double sx, double sy)
        {
            while (true) {
                Vector2d[] c = new Vector2d[3];
                for (int i = 0; i < 3; ++i) c[i] = new Vector2d(rnd.Next(-80, 81) / 64.0, rnd.Next(-80, 81) / 64.0);
                double area2 = (c[1].x - c[0].x) * (c[2].y - c[0].y) - (c[1].y - c[0].y) * (c[2].x - c[0].x);
                if (area2 < 0.25) continue;
                Vector3d P(Vector2d v) => new Vector3d(v.x, v.y, z0 + sx * v.x + sy * v.y);
                return (P(c[0]), P(c[1]), P(c[2]));
            }
        }

        static int Nearest(DMeshAABBTree3 tree, Vector3d p) => tree.FindNearestTriangle(p);

        static double Patch(DMesh3 target, Vector3d p0, Vector3d p1, Vector3d p2)
        {
            var tree = new DMeshAABBTree3(target, true);
            var bounds = new TriangleDeviationBounds(target);
            return bounds.certified_patch_bound(p0, p1, p2, Nearest(tree, p0), Nearest(tree, p1), Nearest(tree, p2));
        }

        // the exact gap along z over the horizontal-above heightfield at (x, y), z of the triangle Q there zq, is at most u at every
        // closed target triangle that contains (x, y): |(zq - a.z) Nz + Nx dx + Ny dy| <= u Nz (Nz > 0, counter-clockwise from +z)
        static void AssertGapWithin(DMesh3 target, double x, double y, double zq, double u, string what)
        {
            int hits = 0;
            foreach (int tid in target.TriangleIndices()) {
                Index3i tv = target.GetTriangle(tid);
                Vector3d a = target.GetVertex(tv.a), b = target.GetVertex(tv.b), c = target.GetVertex(tv.c);
                if (!InsideXY(a, b, c, x, y)) continue;
                ExactDyadic bx = D(b.x) - D(a.x), by = D(b.y) - D(a.y), bz = D(b.z) - D(a.z), cx = D(c.x) - D(a.x), cy = D(c.y) - D(a.y), cz = D(c.z) - D(a.z);
                ExactDyadic nx = by * cz - bz * cy, ny = bz * cx - bx * cz, nz = bx * cy - by * cx;
                Assert.True(nz.Sign > 0);
                ExactDyadic g = (D(zq) - D(a.z)) * nz + nx * (D(x) - D(a.x)) + ny * (D(y) - D(a.y));
                Assert.True(ExactDyadic.Compare(Abs(g), D(u) * nz) <= 0, $"{what}: the exact gap at ({x}, {y}) exceeds the bound {u:R}");
                hits++;
            }
            Assert.True(hits > 0, $"{what}: ({x}, {y}) is over no target triangle");
        }

        static bool InsideXY(Vector3d a, Vector3d b, Vector3d c, double x, double y)
        {
            Vector3d[] v = { a, b, c };
            for (int i = 0; i < 3; ++i) {
                Vector3d s = v[i], e = v[(i + 1) % 3];
                if (((D(e.x) - D(s.x)) * (D(y) - D(s.y)) - (D(e.y) - D(s.y)) * (D(x) - D(s.x))).Sign < 0) return false;
            }
            return true;
        }

        static bool InsideQ(Vector3d p0, Vector3d p1, Vector3d p2, double x, double y) => InsideXY(p0, p1, p2, x, y);

        /// <summary> a horizontal triangle over a bumpy heightfield: the bound is finite, not below the exact gap along z at dyadic
        /// samples of the triangle (nor the exact distance), and within 1e-9 of the uncertified patch bound (which is the largest
        /// gap at the clipped corners, in doubles, plus its estimated slack): the candidates are a tight superset </summary>
        [Fact]
        public void PatchBound_Horizontal_NotBelowTheExactGap_AndTight()
        {
            var rnd = new Random(8480);
            for (int it = 0; it < 24; ++it) {
                DMesh3 target = Heightfield(6, rnd, 0.9);
                var (p0, p1, p2) = RandomQ(rnd, 0.75, 0, 0);
                double u = Patch(target, p0, p1, p2);
                Assert.True(MathUtil.IsFinite(u), $"it {it}: {u}");
                var tree = new DMeshAABBTree3(target, true);
                double unc = new TriangleDeviationBounds(target).patch_bound(p0, p1, p2, Nearest(tree, p0), Nearest(tree, p1), Nearest(tree, p2));
                Assert.True(Math.Abs(u - unc) <= 1e-9, $"it {it}: certified {u:R}, uncertified {unc:R}");
                for (int i = -80; i <= 80; i += 7)
                    for (int j = -80; j <= 80; j += 5) {
                        double x = i / 64.0, y = j / 64.0;
                        if (InsideQ(p0, p1, p2, x, y)) AssertGapWithin(target, x, y, 0.75, u, "it " + it);
                    }
                // (the corners themselves)
                foreach (Vector3d c in new[] { p0, p1, p2 }) AssertGapWithin(target, c.x, c.y, 0.75, u, "corner");
            }
        }


        /// <summary> the same with doubles that are not dyadic: a jittered heightfield (x, y, z random doubles) and a horizontal triangle
        /// with random double corners, so every difference rounds and the bound's rounding steps are live. Not below the exact gap at
        /// random double samples (exact arithmetic), and within 1e-9 of the uncertified patch bound </summary>
        [Fact]
        public void PatchBound_NonDyadic_NotBelowTheExactGap_AndTight()
        {
            var rnd = new Random(8487);
            int done = 0;
            for (int it = 0; it < 30; ++it) {
                DMesh3 target = Heightfield(6, rnd, 0.9);
                foreach (int v in target.VertexIndices().ToList()) {
                    Vector3d p = target.GetVertex(v);
                    bool border = Math.Abs(p.x) >= 3 || Math.Abs(p.y) >= 3;
                    target.SetVertex(v, new Vector3d(p.x + (border ? 0 : 0.3 * (rnd.NextDouble() - 0.5)), p.y + (border ? 0 : 0.3 * (rnd.NextDouble() - 0.5)), p.z + 0.01 * rnd.NextDouble()));
                }
                double z0 = 0.7 + 0.1 * rnd.NextDouble();
                Vector3d[] c = new Vector3d[3];
                for (int i = 0; i < 3; ++i) c[i] = new Vector3d(2.4 * rnd.NextDouble() - 1.2, 2.4 * rnd.NextDouble() - 1.2, z0);
                double area2 = (c[1].x - c[0].x) * (c[2].y - c[0].y) - (c[1].y - c[0].y) * (c[2].x - c[0].x);
                if (Math.Abs(area2) < 0.3) continue;
                if (area2 < 0) { var s = c[1]; c[1] = c[2]; c[2] = s; }
                double u = Patch(target, c[0], c[1], c[2]);
                Assert.True(MathUtil.IsFinite(u), $"it {it}: {u}");
                var tree = new DMeshAABBTree3(target, true);
                double unc = new TriangleDeviationBounds(target).patch_bound(c[0], c[1], c[2], Nearest(tree, c[0]), Nearest(tree, c[1]), Nearest(tree, c[2]));
                Assert.True(Math.Abs(u - unc) <= 1e-9, $"it {it}: certified {u:R}, uncertified {unc:R}");
                for (int k = 0; k < 60; ++k) {
                    double a = rnd.NextDouble(), b = rnd.NextDouble();
                    if (a + b > 1) { a = 1 - a; b = 1 - b; }
                    double x = c[0].x + a * (c[1].x - c[0].x) + b * (c[2].x - c[0].x), y = c[0].y + a * (c[1].y - c[0].y) + b * (c[2].y - c[0].y);
                    if (InsideQ(c[0], c[1], c[2], x, y)) AssertGapWithin(target, x, y, z0, u, "non-dyadic " + it);
                }
                done++;
            }
            Assert.True(done >= 20, done.ToString());
        }

        /// <summary> tilted triangles over the heightfield: not below the exact distance of dyadic samples of the triangle </summary>
        [Fact]
        public void PatchBound_Tilted_NotBelowTheExactDistance()
        {
            var rnd = new Random(8481);
            for (int it = 0; it < 10; ++it) {
                DMesh3 target = Heightfield(6, rnd, 0.6);
                double sx = rnd.Next(-4, 5) / 8.0, sy = rnd.Next(-4, 5) / 8.0;
                var (p0, p1, p2) = RandomQ(rnd, 0.5 + rnd.Next(0, 8) / 16.0, sx, sy);
                double u = Patch(target, p0, p1, p2);
                Assert.True(MathUtil.IsFinite(u), $"it {it}: {u}");
                var ttree = new DMeshAABBTree3(target, true);
                double unc = new TriangleDeviationBounds(target).patch_bound(p0, p1, p2, Nearest(ttree, p0), Nearest(ttree, p1), Nearest(ttree, p2));
                Assert.True(Math.Abs(u - unc) <= 1e-9, $"it {it}: certified {u:R}, uncertified {unc:R}");
                int tested = 0;
                for (int i = -80; i <= 80; i += 3)
                    for (int j = -80; j <= 80; j += 3) {
                        double x = i / 64.0, y = j / 64.0;
                        if (!InsideQ(p0, p1, p2, x, y) || (i + j) % 4 != 0) continue;
                        Vector3d s = new Vector3d(x, y, p0.z + sx * (x - p0.x) + sy * (y - p0.y));
                        Assert.True(CertifiedPointMeshDistance.AtLeastDistance(target, s, u), $"it {it}: {u:R} below the distance of {s}");
                        tested++;
                    }
                Assert.True(tested > 3, "samples: " + tested);
            }
        }

        /// <summary> the patch gives up (+infinity) when it would take more target triangles than MaxPatchTriangles; with exactly the
        /// number it needs the bound is the finite one, which does not depend on the limit </summary>
        [Fact]
        public void PatchBound_MoreTrianglesThanTheLimit_IsInfinite()
        {
            DMesh3 field = Heightfield(6, new Random(8482), 0.5);
            Vector3d p0 = new Vector3d(-0.75, -0.5, 0.75), p1 = new Vector3d(0.5, -0.625, 0.75), p2 = new Vector3d(0.125, 0.75, 0.75);
            var tree = new DMeshAABBTree3(field, true);
            int n0 = Nearest(tree, p0), n1 = Nearest(tree, p1), n2 = Nearest(tree, p2);
            double unlimited = new TriangleDeviationBounds(field).certified_patch_bound(p0, p1, p2, n0, n1, n2);
            Assert.True(MathUtil.IsFinite(unlimited));
            int needed = -1;
            for (int limit = 1; limit <= 256 && needed < 0; ++limit)
                if (MathUtil.IsFinite(new TriangleDeviationBounds(field) { MaxPatchTriangles = limit }.certified_patch_bound(p0, p1, p2, n0, n1, n2)))
                    needed = limit;
            Assert.True(needed > 1, "the triangle lies over several target triangles");
            // the patch is the target triangles whose xy box meets the xy box of Q (the field is all positively oriented and connected)
            double xlo = Math.Min(p0.x, Math.Min(p1.x, p2.x)), xhi = Math.Max(p0.x, Math.Max(p1.x, p2.x));
            double ylo = Math.Min(p0.y, Math.Min(p1.y, p2.y)), yhi = Math.Max(p0.y, Math.Max(p1.y, p2.y));
            int inBox = 0;
            foreach (int tid in field.TriangleIndices()) {
                Index3i tv = field.GetTriangle(tid);
                Vector3d a = field.GetVertex(tv.a), b = field.GetVertex(tv.b), c = field.GetVertex(tv.c);
                if (Math.Max(a.x, Math.Max(b.x, c.x)) >= xlo && Math.Min(a.x, Math.Min(b.x, c.x)) <= xhi
                    && Math.Max(a.y, Math.Max(b.y, c.y)) >= ylo && Math.Min(a.y, Math.Min(b.y, c.y)) <= yhi)
                    ++inBox;
            }
            Assert.Equal(inBox, needed);
            Assert.True(double.IsPositiveInfinity(new TriangleDeviationBounds(field) { MaxPatchTriangles = needed - 1 }.certified_patch_bound(p0, p1, p2, n0, n1, n2)));
            Assert.Equal(unlimited, new TriangleDeviationBounds(field) { MaxPatchTriangles = needed }.certified_patch_bound(p0, p1, p2, n0, n1, n2));
        }

        /// <summary> where the coverage fails the bound is +infinity: a hole under the triangle, a triangle that sticks out of the
        /// mesh, a flipped (folded) triangle, a triangle soup (no shared edges), a triangle that is edge-on or degenerate. Never a
        /// finite value; and the same triangle over the closed field is finite </summary>
        [Fact]
        public void PatchBound_CoverageFails_IsInfinite()
        {
            var rnd = new Random(8482);
            DMesh3 field = Heightfield(6, rnd, 0.5);
            Vector3d p0 = new Vector3d(-0.75, -0.5, 0.75), p1 = new Vector3d(0.5, -0.625, 0.75), p2 = new Vector3d(0.125, 0.75, 0.75);
            Assert.True(MathUtil.IsFinite(Patch(field, p0, p1, p2)));
            // a hole: remove the triangles under the corner p1 and its centroid
            foreach (Vector3d at in new[] { p1, (p0 + p1 + p2) / 3 }) {
                DMesh3 holed = new DMesh3(field);
                var tree = new DMeshAABBTree3(holed, true);
                int t = tree.FindNearestTriangle(new Vector3d(at.x, at.y, 0));
                holed.RemoveTriangle(t);
                Assert.True(double.IsPositiveInfinity(Patch(holed, p0, p1, p2)), "hole under " + at);
            }
            // sticks out of the mesh (the field covers [-3, 3]^2)
            Assert.True(double.IsPositiveInfinity(Patch(field, new Vector3d(2.5, 0, 0.5), new Vector3d(3.5, 0, 0.5), new Vector3d(3, 1, 0.5))));
            // a flipped triangle under the middle: the winding is not 1 there (the orientation is not consistent)
            DMesh3 folded = new DMesh3(field);
            var ft = new DMeshAABBTree3(folded, true);
            int under = ft.FindNearestTriangle(new Vector3d(p0.x * 0.2 + p1.x * 0.3 + p2.x * 0.5, p0.y * 0.2 + p1.y * 0.3 + p2.y * 0.5, 0));
            Index3i tv = folded.GetTriangle(under);
            folded.SetTriangle(under, new Index3i(tv.a, tv.c, tv.b), false);
            Assert.True(double.IsPositiveInfinity(Patch(folded, p0, p1, p2)), "flipped triangle");
            // a soup: the same triangles with separate vertices
            var soup = new DMesh3();
            foreach (int tid in field.TriangleIndices()) {
                Index3i v = field.GetTriangle(tid);
                soup.AppendTriangle(soup.AppendVertex(field.GetVertex(v.a)), soup.AppendVertex(field.GetVertex(v.b)), soup.AppendVertex(field.GetVertex(v.c)));
            }
            Assert.True(double.IsPositiveInfinity(Patch(soup, p0, p1, p2)), "soup");
            // one target triangle in the bounding box of Q but beyond its hypotenuse (x + y > 1): nothing underneath, no boundary edge meets Q'
            var lone = new DMesh3();
            lone.AppendTriangle(lone.AppendVertex(new Vector3d(0.8, 0.8, 0)), lone.AppendVertex(new Vector3d(0.9, 0.8, 0)), lone.AppendVertex(new Vector3d(0.8, 0.9, 0)));
            Assert.True(double.IsPositiveInfinity(new TriangleDeviationBounds(lone).certified_patch_bound(
                new Vector3d(0, 0, 1), new Vector3d(1, 0, 1), new Vector3d(0, 1, 1), 0, 0, 0)), "no witness");
            // degenerate and edge-on triangles (T(Q) = 0 exactly), and clockwise as seen along its own normal cannot occur: the opposite order
            Assert.True(double.IsPositiveInfinity(Patch(field, p0, p1, (p0 + p1) / 2)), "collinear");
            Assert.True(double.IsPositiveInfinity(Patch(field, p0, p0, p1)), "repeated corner");
            // the opposite order of the corners: the normal points down and the field is seen from below, with the opposite orientation
            // of every target triangle: T(t) < 0 for all of them, so none joins S
            Assert.True(double.IsPositiveInfinity(Patch(field, p0, p2, p1)), "reversed corners");
        }

        /// <summary> the corners of the triangle (and an edge midpoint, and a crossing) on target vertices and edges, as a reduced mesh
        /// against its original has them: signs are exactly 0 there, the candidates must stay (an undecided sign is not "outside"):
        /// the exact gap at the corners, 1.15 or more, is not below the bound </summary>
        [Fact]
        public void PatchBound_CornersOnTargetVertices_KeepTheirGap()
        {
            var rnd = new Random(8488);
            int finite = 0;
            for (int it = 0; it < 12; ++it) {
                DMesh3 target = Heightfield(6, rnd, 1.0);
                // corners on grid vertices, z0 above all
                Vector3d p0 = new Vector3d(-1, -1, 0.75), p1 = new Vector3d(2, -1, 0.75), p2 = new Vector3d(0, 2, 0.75);
                double u = Patch(target, p0, p1, p2);
                if (!MathUtil.IsFinite(u)) continue;     // (touching boundary edges of S may refuse; never wrong)
                finite++;
                foreach (Vector3d c in new[] { p0, p1, p2, (p0 + p1) / 2, (p1 + p2) / 2, (p0 + p2) / 2, (p0 + p1 + p2) / 3 })
                    AssertGapWithin(target, c.x, c.y, 0.75, u, "grid corner " + c);
            }
            Assert.True(finite >= 6, finite.ToString());
        }


        /// <summary> the enclosure of the cut parameter against the exact lambda = P_a / (P_a - P_b), for determinants that are certified
        /// opposite but whose enclosures are wide (rows with cancellation, |P| a few times E): lo and hi are on the right sides of the
        /// exact value, both orders of the signs, and the enclosure is wide in some cases (a swapped lo / hi, or a one-sided enclosure,
        /// is outside the exact value there) </summary>
        [Fact]
        public void LambdaEnclosure_ContainsTheExactParameter_WhenWide()
        {
            var rnd = new Random(8489);
            var pos = new List<(CertifiedDet3, ExactDyadic)>(); var neg = new List<(CertifiedDet3, ExactDyadic)>();
            for (int it = 0; it < 6000 && (pos.Count < 150 || neg.Count < 150); ++it) {
                Vector3d b = V(rnd, 0), a1 = V(rnd, 0), a2 = V(rnd, 0);
                double s = R(rnd, 0), t = R(rnd, 0);
                Vector3d[] p = { b, b, b }, q = { b + a1, b + a2, Vector3d.Zero };
                Vector3d comb = s * (q[0] - p[0]) + t * (q[1] - p[1]);
                double rel = Math.Pow(10, -14 + 2 * rnd.NextDouble());
                q[2] = b + new Vector3d(comb.x * (1 + rel * rnd.NextDouble()), comb.y * (1 + rel * rnd.NextDouble()), comb.z * (1 + rel * rnd.NextDouble()));
                var d = CertifiedDet3.Compute(q[0] - p[0], q[1] - p[1], q[2] - p[2]);
                if (d.Sign > 0) pos.Add((d, ExactDet(p, q))); else if (d.Sign < 0) neg.Add((d, ExactDet(p, q)));
            }
            Assert.True(pos.Count >= 50 && neg.Count >= 50, pos.Count + " / " + neg.Count);
            int wide = 0, n = 0;
            for (int i = 0; i < Math.Min(pos.Count, neg.Count); ++i)
                for (int k = 0; k < 8; ++k) {
                    var (dp, ep) = pos[i]; var (dn, en) = neg[rnd.Next(neg.Count)];
                    foreach (bool swap in new[] { false, true }) {
                        // lambda = P_a / (P_a - P_b); the order of the arguments is the order of the signs
                        var (da, ea, db, eb) = swap ? (dn, en, dp, ep) : (dp, ep, dn, en);
                        Assert.True(TriangleDeviationBounds.lambda_enclosure(da, db, out double lo, out double hi));
                        ExactDyadic den = ea - eb;                          // P_a - P_b, nonzero, the sign of P_a
                        // lo <= P_a / den <= hi, multiplied by den (its sign flips the comparisons)
                        int sgn = den.Sign;
                        Assert.True(sgn * ExactDyadic.Compare(D(lo) * den, ea) <= 0, $"lo {lo:R} above the exact lambda");
                        Assert.True(sgn * ExactDyadic.Compare(ea, D(hi) * den) <= 0, $"hi {hi:R} below the exact lambda");
                        Assert.True(lo >= 0 && hi <= 1 && lo <= hi);
                        n++; if (hi - lo > 1e-3) wide++;
                    }
                }
            Assert.True(wide > n / 20, wide + " wide of " + n + ": the cases are not ill-conditioned");
            // undecided or equal signs: no enclosure
            Assert.False(TriangleDeviationBounds.lambda_enclosure(pos[0].Item1, pos[1].Item1, out _, out _));
            Assert.False(TriangleDeviationBounds.lambda_enclosure(pos[0].Item1, CertifiedDet3.Compute(Vector3d.Zero, Vector3d.Zero, Vector3d.Zero), out _, out _));
        }

        /// <summary> CertifiedUpper with the patch bound under every similarity: bit for bit at the 2^k scalings </summary>
        [Fact]
        public void CertifiedUpper_WithPatchBound_UnderEverySimilarity()
        {
            var rnd = new Random(8490);
            DMesh3 big = Heightfield(10, rnd, 0.6), red = new DMesh3(big);
            new Reducer(red).ReduceToTriangleCount(big.TriangleCount / 6);
            var fi = typeof(MeshHausdorffDistance).GetField("PatchBound", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            MeshHausdorffDistance run(DMesh3 a, DMesh3 b, double tol)
            {
                var h = new MeshHausdorffDistance(a, b) { Symmetric = false, CertifiedUpper = true, Tolerance = tol };
                fi.SetValue(h, true);
                Assert.True(h.Compute());
                return h;
            }
            var h0 = run(red, big, 1e-3);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 a = new DMesh3(red), b = new DMesh3(big);
                MeshTransforms.PerVertexTransform(a, s.Apply); MeshTransforms.PerVertexTransform(b, s.Apply);
                var h = run(a, b, 1e-3 * s.Scale);
                if (s.IsExact) {
                    Assert.Equal(h0.LowerBound, h.LowerBound / s.Scale);
                    Assert.Equal(h0.UpperBound, h.UpperBound / s.Scale);
                    Assert.Equal(h0.Subdivisions, h.Subdivisions);
                } else {
                    double eps = 100 * s.Resolution(6);
                    Assert.True(h.LowerBound / s.Scale <= h0.UpperBound + eps && h0.LowerBound <= h.UpperBound / s.Scale + eps, s.Name);
                }
            }
        }

        /// <summary> no witness: a triangle in the bounding box of the only target triangle, beyond its hypotenuse. No boundary edge meets it
        /// (the hypotenuse's line separates), but no vertex lies inside it and no corner inside the target triangle: nothing is under it,
        /// +infinity (without the witness test the coverage would be taken for granted) </summary>
        [Fact]
        public void PatchBound_NothingUnderneath_IsInfinite()
        {
            var target = new DMesh3();
            target.AppendTriangle(target.AppendVertex(new Vector3d(0, 0, 0)), target.AppendVertex(new Vector3d(4, 0, 0)), target.AppendVertex(new Vector3d(0, 4, 0)));
            Vector3d p0 = new Vector3d(3, 3, 0.5), p1 = new Vector3d(3.75, 3, 0.5), p2 = new Vector3d(3, 3.75, 0.5);
            Assert.True(double.IsPositiveInfinity(Patch(target, p0, p1, p2)));
            // (the same triangle moved inside the target triangle is covered: finite)
            Assert.True(MathUtil.IsFinite(Patch(target, new Vector3d(0.5, 0.5, 0.5), new Vector3d(1.25, 0.5, 0.5), new Vector3d(0.5, 1.25, 0.5))));
        }

        /// <summary> large coordinates (UTM offsets, 2^20): the differences are exact there, the permanent small; and the exact gap
        /// is still never above the bound </summary>
        [Fact]
        public void PatchBound_FarFromTheOrigin()
        {
            foreach (double off in new[] { 1e3, 632117.25, Math.Pow(2, 20) + 0.5 }) {
                var rnd = new Random(8483);
                DMesh3 target = Heightfield(6, rnd, 0.9, 1, new Vector3d(off, off + 1024, 0));
                var (q0, q1, q2) = RandomQ(rnd, 0.75, 0, 0);
                Vector3d sh = new Vector3d(off, off + 1024, 0);
                Vector3d p0 = q0 + sh, p1 = q1 + sh, p2 = q2 + sh;
                double u = Patch(target, p0, p1, p2);
                Assert.True(MathUtil.IsFinite(u), $"offset {off}: {u}");
                DMesh3 local = Heightfield(6, new Random(8483), 0.9);
                double u0 = Patch(local, q0, q1, q2);
                Assert.True(Math.Abs(u - u0) <= 1e-9, $"offset {off}: {u:R} vs {u0:R}");
                for (int i = -80; i <= 80; i += 9)
                    for (int j = -80; j <= 80; j += 9)
                        if (InsideQ(q0, q1, q2, i / 64.0, j / 64.0)) AssertGapWithin(local, i / 64.0, j / 64.0, 0.75, u, "offset " + off);
            }
        }

        /// <summary> scaled by 2^k, k from -330 to +330 (near underflow of the products of three entries, near overflow): the result
        /// is either +infinity (refused: the determinants are not decided) or not below the exact gap (scaled exactly). Finite in the
        /// middle of the range, where the products are normal; at 2^k the finite results are the 2^k multiple of the unscaled one
        /// where nothing is refused </summary>
        [Fact]
        public void PatchBound_ScaleExtremes_RefusedOrValid()
        {
            var rnd = new Random(8484);
            DMesh3 field = Heightfield(6, rnd, 0.9);
            var (q0, q1, q2) = RandomQ(rnd, 0.75, 0, 0);
            double u1 = Patch(field, q0, q1, q2);
            Assert.True(MathUtil.IsFinite(u1));
            int finite = 0, refused = 0;
            foreach (int k in new[] { -340, -300, -250, -200, -150, -100, -50, 0, 50, 100, 150, 200, 250, 300, 330 }) {
                double f = Math.Pow(2, k);
                DMesh3 scaled = Heightfield(6, new Random(8484), 0.9, f);
                Vector3d p0 = f * q0, p1 = f * q1, p2 = f * q2;
                double u = Patch(scaled, p0, p1, p2);
                if (double.IsPositiveInfinity(u)) { refused++; continue; }
                finite++;
                Assert.True(MathUtil.IsFinite(u), $"2^{k}: {u}");
                // (x, y, zq of the unscaled sample, scaled: exact; the gap formula is homogeneous)
                for (int i = -80; i <= 80; i += 16)
                    for (int j = -80; j <= 80; j += 16) {
                        double x = i / 64.0, y = j / 64.0;
                        if (InsideQ(q0, q1, q2, x, y)) AssertGapWithin(scaled, f * x, f * y, f * 0.75, u, $"2^{k}");
                    }
                if (k >= -100 && k <= 100) Assert.Equal(f * u1, u);          // (bit for bit where the underflow term is absorbed)
            }
            Assert.True(finite >= 5, finite + " finite, " + refused + " refused");
            Assert.True(refused >= 2, "the extremes are refused: " + refused);
        }

        /// <summary> the 2^k similarities scale the result bit for bit; the other similarities give the same bound up to the rounding of
        /// the transformed coordinates, and a finite bound </summary>
        [Fact]
        public void PatchBound_UnderEverySimilarity()
        {
            var rnd = new Random(8485);
            DMesh3 field = Heightfield(6, rnd, 0.9);
            var (q0, q1, q2) = RandomQ(rnd, 0.75, 0.25, -0.125);
            double u0 = Patch(field, q0, q1, q2);
            Assert.True(MathUtil.IsFinite(u0));
            foreach (Similarity s in Similarity.All()) {
                DMesh3 t = new DMesh3(field);
                MeshTransforms.PerVertexTransform(t, s.Apply);
                double u = Patch(t, s.Apply(q0), s.Apply(q1), s.Apply(q2));
                if (s.IsExact) {
                    Assert.Equal(s.Scale * u0, u);
                } else {
                    Assert.True(MathUtil.IsFinite(u), s.Name + ": " + u);
                    Assert.True(Math.Abs(u / s.Scale - u0) <= 1e-6, $"{s.Name}: {u / s.Scale:R} vs {u0:R}");
                }
            }
        }

        /// <summary> one instance computes the same value twice, and from several threads (no state in the instance: the dictionaries
        /// are local) </summary>
        [Fact]
        public void PatchBound_Reuse_AndParallel_Same()
        {
            var rnd = new Random(8486);
            DMesh3 field = Heightfield(8, rnd, 0.9);
            var tree = new DMeshAABBTree3(field, true);
            var bounds = new TriangleDeviationBounds(field);
            var qs = new List<(Vector3d, Vector3d, Vector3d)>();
            for (int i = 0; i < 40; ++i) qs.Add(RandomQ(rnd, 0.75, 0, 0));
            double[] serial = qs.Select(q => bounds.certified_patch_bound(q.Item1, q.Item2, q.Item3, Nearest(tree, q.Item1), Nearest(tree, q.Item2), Nearest(tree, q.Item3))).ToArray();
            double[] again = qs.Select(q => bounds.certified_patch_bound(q.Item1, q.Item2, q.Item3, Nearest(tree, q.Item1), Nearest(tree, q.Item2), Nearest(tree, q.Item3))).ToArray();
            Assert.Equal(serial, again);
            var par = new double[qs.Count];
            System.Threading.Tasks.Parallel.For(0, qs.Count, i => {
                var q = qs[i];
                par[i] = bounds.certified_patch_bound(q.Item1, q.Item2, q.Item3, tree.FindNearestTriangle(q.Item1), tree.FindNearestTriangle(q.Item2), tree.FindNearestTriangle(q.Item3));
            });
            Assert.Equal(serial, par);
            Assert.True(serial.Count(MathUtil.IsFinite) > 30);
        }
    }
}
