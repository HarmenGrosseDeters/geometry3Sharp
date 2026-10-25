// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;

namespace g3
{
    /// <summary>
    /// Stages B, C and D of Shewchuk's adaptive predicates ("Adaptive Precision Floating-Point Arithmetic and
    /// Fast Robust Geometric Predicates", 1997), after the floating-point filter (stage A, in ExactPredicates):
    /// B evaluates the determinant exactly from the rounded coordinate differences (an expansion of a few
    /// dozen doubles), C adds the first-order terms of the differences' rounding errors; each decides the
    /// sign when its error bound allows. D adds all terms with rounding errors exactly to B's expansion: the
    /// exact determinant, so D always decides. Rounding errors that are zero are skipped, which keeps D's
    /// expansions short.
    ///
    /// Only valid without overflow and underflow: the caller checks that all coordinates are 0 or in
    /// [2^-100, 2^100] (Expansion.InRange). Then all coordinates are multiples of 2^-152, so are the
    /// differences' rounding errors, and every product of up to four of them (and its rounding error) is a
    /// normal double. Signs follow Shewchuk: orient2d > 0 for counter-clockwise,
    /// orient3d > 0 if d is below the plane of the counter-clockwise (seen from above) a, b, c,
    /// incircle > 0 if d is inside the circle of the counter-clockwise a, b, c.
    /// </summary>
    internal static class AdaptivePredicates
    {
        // how often stage D was needed on this thread (for tests)
        [ThreadStatic] internal static int StageDCount;

        const double Eps = 1.1102230246251565e-16;     // 2^-53
        const double ResultErrBound = (3.0 + 8.0 * Eps) * Eps;
        const double CcwErrBoundB = (2.0 + 12.0 * Eps) * Eps;
        const double CcwErrBoundC = (9.0 + 64.0 * Eps) * Eps * Eps;
        const double O3dErrBoundB = (3.0 + 28.0 * Eps) * Eps;
        const double O3dErrBoundC = (26.0 + 288.0 * Eps) * Eps * Eps;
        const double IccErrBoundB = (4.0 + 48.0 * Eps) * Eps;
        const double IccErrBoundC = (44.0 + 576.0 * Eps) * Eps * Eps;

        public static int Orient2d(double ax, double ay, double bx, double by, double cx, double cy)
        {
            return orient2d(ax, ay, bx, by, cx, cy, false);
        }

        /// <summary> stage B's expansion and stage D, without the error bounds of B and C (for tests) </summary>
        internal static int Orient2dStageD(double ax, double ay, double bx, double by, double cx, double cy)
        {
            return orient2d(ax, ay, bx, by, cx, cy, true);
        }

        static int orient2d(double ax, double ay, double bx, double by, double cx, double cy, bool onlyD)
        {
            double acx = ax - cx, bcx = bx - cx, acy = ay - cy, bcy = by - cy;
            double detsum = Math.Abs(acx * bcy) + Math.Abs(acy * bcx);
            var w = Workspace.Get();
            int n = two_products_diff(acx, bcy, acy, bcx, w.B, w);
            double det = estimate(w.B, n);
            double errbound = CcwErrBoundB * detsum;
            if (onlyD == false && (det >= errbound || -det >= errbound))
                return Math.Sign(det);

            double acxtail = diff_tail(ax, cx, acx), bcxtail = diff_tail(bx, cx, bcx);
            double acytail = diff_tail(ay, cy, acy), bcytail = diff_tail(by, cy, bcy);
            if (acxtail == 0 && acytail == 0 && bcxtail == 0 && bcytail == 0)
                return sign(w.B, n);        // the differences were exact: so is B

            errbound = CcwErrBoundC * detsum + ResultErrBound * Math.Abs(det);
            det += (acx * bcytail + bcy * acxtail) - (acy * bcxtail + bcx * acytail);
            if (onlyD == false && (det >= errbound || -det >= errbound))
                return Math.Sign(det);

            // D: B + the terms with rounding errors
            StageDCount++;
            cross_tail(acx, acxtail, bcy, bcytail, acy, acytail, bcx, bcxtail, w, w.T);
            w.D.Clear();
            w.D.Add(w.B, n);
            w.D.Add(w.T.X, w.T.N);
            return sign(w.D.X, w.D.N);
        }

        public static int Orient3d(double ax, double ay, double az, double bx, double by, double bz,
                                   double cx, double cy, double cz, double dx, double dy, double dz)
        {
            return orient3d(ax, ay, az, bx, by, bz, cx, cy, cz, dx, dy, dz, false);
        }

        /// <summary> stage B's expansion and stage D, without the error bounds of B and C (for tests) </summary>
        internal static int Orient3dStageD(double ax, double ay, double az, double bx, double by, double bz,
                                           double cx, double cy, double cz, double dx, double dy, double dz)
        {
            return orient3d(ax, ay, az, bx, by, bz, cx, cy, cz, dx, dy, dz, true);
        }

        static int orient3d(double ax, double ay, double az, double bx, double by, double bz,
                            double cx, double cy, double cz, double dx, double dy, double dz, bool onlyD)
        {
            double adx = ax - dx, bdx = bx - dx, cdx = cx - dx;
            double ady = ay - dy, bdy = by - dy, cdy = cy - dy;
            double adz = az - dz, bdz = bz - dz, cdz = cz - dz;
            double permanent = (Math.Abs(bdx * cdy) + Math.Abs(cdx * bdy)) * Math.Abs(adz)
                             + (Math.Abs(cdx * ady) + Math.Abs(adx * cdy)) * Math.Abs(bdz)
                             + (Math.Abs(adx * bdy) + Math.Abs(bdx * ady)) * Math.Abs(cdz);
            var w = Workspace.Get();
            int nbc = two_products_diff(bdx, cdy, cdx, bdy, w.Bc, w);
            int nca = two_products_diff(cdx, ady, adx, cdy, w.Ca, w);
            int nab = two_products_diff(adx, bdy, bdx, ady, w.Ab, w);
            int na = scale(w.Bc, nbc, adz, w.T8a);
            int nb = scale(w.Ca, nca, bdz, w.T8b);
            int nc = scale(w.Ab, nab, cdz, w.T8c);
            int nabd = sum(w.T8a, na, w.T8b, nb, w.T16);
            int n = sum(w.T16, nabd, w.T8c, nc, w.Fin1);
            double det = estimate(w.Fin1, n);
            double errbound = O3dErrBoundB * permanent;
            if (onlyD == false && (det >= errbound || -det >= errbound))
                return Math.Sign(det);

            double adxtail = diff_tail(ax, dx, adx), bdxtail = diff_tail(bx, dx, bdx), cdxtail = diff_tail(cx, dx, cdx);
            double adytail = diff_tail(ay, dy, ady), bdytail = diff_tail(by, dy, bdy), cdytail = diff_tail(cy, dy, cdy);
            double adztail = diff_tail(az, dz, adz), bdztail = diff_tail(bz, dz, bdz), cdztail = diff_tail(cz, dz, cdz);
            if (adxtail == 0 && bdxtail == 0 && cdxtail == 0 && adytail == 0 && bdytail == 0 && cdytail == 0
                && adztail == 0 && bdztail == 0 && cdztail == 0)
                return sign(w.Fin1, n);

            errbound = O3dErrBoundC * permanent + ResultErrBound * Math.Abs(det);
            det += (adz * ((bdx * cdytail + cdy * bdxtail) - (bdy * cdxtail + cdx * bdytail)) + adztail * (bdx * cdy - bdy * cdx))
                 + (bdz * ((cdx * adytail + ady * cdxtail) - (cdy * adxtail + adx * cdytail)) + bdztail * (cdx * ady - cdy * adx))
                 + (cdz * ((adx * bdytail + bdy * adxtail) - (ady * bdxtail + bdx * adytail)) + cdztail * (adx * bdy - ady * bdx));
            if (onlyD == false && (det >= errbound || -det >= errbound))
                return Math.Sign(det);

            // D: B + for each of the three terms z' M' - z M, with z' = z + ztail and the exact minor
            // M' = M + T of the differences with their rounding errors: z T + ztail M'
            StageDCount++;
            w.D.Clear();
            w.D.Add(w.Fin1, n);
            cross_tail(bdx, bdxtail, cdy, cdytail, cdx, cdxtail, bdy, bdytail, w, w.T);
            orient3d_term(adz, adztail, w.Bc, nbc, w);
            cross_tail(cdx, cdxtail, ady, adytail, adx, adxtail, cdy, cdytail, w, w.T);
            orient3d_term(bdz, bdztail, w.Ca, nca, w);
            cross_tail(adx, adxtail, bdy, bdytail, bdx, bdxtail, ady, adytail, w, w.T);
            orient3d_term(cdz, cdztail, w.Ab, nab, w);
            return sign(w.D.X, w.D.N);
        }

        // adds z T + ztail (M + T) to D (T in w.T)
        static void orient3d_term(double z, double ztail, double[] m, int mlen, Workspace w)
        {
            if (w.T.N > 0)
                w.D.Add(w.S1, scale(w.T.X, w.T.N, z, w.S1));
            if (ztail != 0) {
                w.M.Clear();
                w.M.Add(m, mlen);
                w.M.Add(w.T.X, w.T.N);
                w.D.Add(w.S1, scale(w.M.X, w.M.N, ztail, w.S1));
            }
        }

        public static int InCircle(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            return incircle(ax, ay, bx, by, cx, cy, dx, dy, false);
        }

        /// <summary> stage B's expansion and stage D, without the error bounds of B and C (for tests) </summary>
        internal static int InCircleStageD(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            return incircle(ax, ay, bx, by, cx, cy, dx, dy, true);
        }

        static int incircle(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy, bool onlyD)
        {
            double adx = ax - dx, bdx = bx - dx, cdx = cx - dx;
            double ady = ay - dy, bdy = by - dy, cdy = cy - dy;
            double alift = adx * adx + ady * ady, blift = bdx * bdx + bdy * bdy, clift = cdx * cdx + cdy * cdy;
            double permanent = (Math.Abs(bdx * cdy) + Math.Abs(cdx * bdy)) * alift
                             + (Math.Abs(cdx * ady) + Math.Abs(adx * cdy)) * blift
                             + (Math.Abs(adx * bdy) + Math.Abs(bdx * ady)) * clift;
            var w = Workspace.Get();
            int nbc = two_products_diff(bdx, cdy, cdx, bdy, w.Bc, w);
            int nca = two_products_diff(cdx, ady, adx, cdy, w.Ca, w);
            int nab = two_products_diff(adx, bdy, bdx, ady, w.Ab, w);
            int na = lifted(w.Bc, nbc, adx, ady, w, w.T32a);
            int nb = lifted(w.Ca, nca, bdx, bdy, w, w.T32b);
            int nabd = sum(w.T32a, na, w.T32b, nb, w.T64);
            int nc = lifted(w.Ab, nab, cdx, cdy, w, w.T32a);
            int n = sum(w.T64, nabd, w.T32a, nc, w.Fin1);
            double det = estimate(w.Fin1, n);
            double errbound = IccErrBoundB * permanent;
            if (onlyD == false && (det >= errbound || -det >= errbound))
                return Math.Sign(det);

            double adxtail = diff_tail(ax, dx, adx), bdxtail = diff_tail(bx, dx, bdx), cdxtail = diff_tail(cx, dx, cdx);
            double adytail = diff_tail(ay, dy, ady), bdytail = diff_tail(by, dy, bdy), cdytail = diff_tail(cy, dy, cdy);
            if (adxtail == 0 && bdxtail == 0 && cdxtail == 0 && adytail == 0 && bdytail == 0 && cdytail == 0)
                return sign(w.Fin1, n);

            errbound = IccErrBoundC * permanent + ResultErrBound * Math.Abs(det);
            det += ((adx * adx + ady * ady) * ((bdx * cdytail + cdy * bdxtail) - (bdy * cdxtail + cdx * bdytail))
                    + 2.0 * (adx * adxtail + ady * adytail) * (bdx * cdy - bdy * cdx))
                 + ((bdx * bdx + bdy * bdy) * ((cdx * adytail + ady * cdxtail) - (cdy * adxtail + adx * cdytail))
                    + 2.0 * (bdx * bdxtail + bdy * bdytail) * (cdx * ady - cdy * adx))
                 + ((cdx * cdx + cdy * cdy) * ((adx * bdytail + bdy * adxtail) - (ady * bdxtail + bdx * adytail))
                    + 2.0 * (cdx * cdxtail + cdy * cdytail) * (adx * bdy - ady * bdx));
            if (onlyD == false && (det >= errbound || -det >= errbound))
                return Math.Sign(det);

            // D: B + for each of the three terms L' M' - L M, with the exact lift L' = L + Lt of the
            // differences with their rounding errors (Lt = xtail (2 x + xtail) + ytail (2 y + ytail)) and the
            // exact minor M' = M + T: L T + Lt M'
            StageDCount++;
            w.D.Clear();
            w.D.Add(w.Fin1, n);
            cross_tail(bdx, bdxtail, cdy, cdytail, cdx, cdxtail, bdy, bdytail, w, w.T);
            incircle_term(adx, adxtail, ady, adytail, w.Bc, nbc, w);
            cross_tail(cdx, cdxtail, ady, adytail, adx, adxtail, cdy, cdytail, w, w.T);
            incircle_term(bdx, bdxtail, bdy, bdytail, w.Ca, nca, w);
            cross_tail(adx, adxtail, bdy, bdytail, bdx, bdxtail, ady, adytail, w, w.T);
            incircle_term(cdx, cdxtail, cdy, cdytail, w.Ab, nab, w);
            return sign(w.D.X, w.D.N);
        }

        // adds (x^2 + y^2) T + Lt (M + T) to D (T in w.T)
        static void incircle_term(double x, double xtail, double y, double ytail, double[] m, int mlen, Workspace w)
        {
            if (w.T.N > 0) {
                w.D.Add(w.S2, scale(w.S1, scale(w.T.X, w.T.N, x, w.S1), x, w.S2));
                w.D.Add(w.S2, scale(w.S1, scale(w.T.X, w.T.N, y, w.S1), y, w.S2));
            }
            if (xtail == 0 && ytail == 0)
                return;
            w.L.Clear();
            if (xtail != 0)
                w.L.Add(w.S1, scale(w.E2, two(2 * x, xtail, w.E2), xtail, w.S1));
            if (ytail != 0)
                w.L.Add(w.S1, scale(w.E2, two(2 * y, ytail, w.E2), ytail, w.S1));
            w.M.Clear();
            w.M.Add(m, mlen);
            w.M.Add(w.T.X, w.T.N);
            for (int i = 0; i < w.L.N; ++i)
                w.D.Add(w.S1, scale(w.M.X, w.M.N, w.L.X[i], w.S1));
        }

        // (x^2 + y^2) * e, exactly
        static int lifted(double[] e, int n, double x, double y, Workspace w, double[] h)
        {
            int n1 = scale(e, n, x, w.T8a);
            int n2 = scale(w.T8a, n1, x, w.T16);
            int n3 = scale(e, n, y, w.T8b);
            int n4 = scale(w.T8b, n3, y, w.T16b);
            return sum(w.T16, n2, w.T16b, n4, h);
        }


        // --- expansions: arrays of doubles, non-overlapping, in order of increasing magnitude, no zeros
        // (except a single 0 for the value 0); h must not alias the inputs

        static void two_sum(double a, double b, out double x, out double y)
        {
            x = a + b;
            double bv = x - a, av = x - bv;
            y = (a - av) + (b - bv);
        }

        // the rounding error of x = a - b
        static double diff_tail(double a, double b, double x)
        {
            double bv = a - x, av = x + bv;
            return (a - av) + (bv - b);
        }

        // a * b - c * d as an expansion of up to 4 components
        static int two_products_diff(double a, double b, double c, double d, double[] h, Workspace w)
        {
            double x1, x0, y1, y0;
            Expansion.TwoProduct(a, b, out x1, out x0);
            Expansion.TwoProduct(c, d, out y1, out y0);
            // (x1 + x0) - (y1 + y0): each pair is an expansion (|x0| <= ulp(x1) / 2)
            w.E2[0] = x0; w.E2[1] = x1;
            w.F2[0] = -y0; w.F2[1] = -y1;
            return sum(w.E2, 2, w.F2, 2, h);
        }

        // e + f: merge by magnitude, then accumulate with error-free sums (Shewchuk's fast-expansion-sum,
        // with Two-Sum in place of Fast-Two-Sum, which gives the same components)
        static int sum(double[] e, int elen, double[] f, int flen, double[] h)
        {
            int i = 0, j = 0, k = 0;
            double q;
            if (j >= flen || (i < elen && Math.Abs(e[i]) < Math.Abs(f[j]))) q = e[i++]; else q = f[j++];
            while (i < elen || j < flen) {
                double g;
                if (j >= flen || (i < elen && Math.Abs(e[i]) < Math.Abs(f[j]))) g = e[i++]; else g = f[j++];
                double qn, hh;
                two_sum(q, g, out qn, out hh);
                q = qn;
                if (hh != 0) h[k++] = hh;
            }
            if (q != 0 || k == 0) h[k++] = q;
            return k;
        }

        // e * b
        static int scale(double[] e, int elen, double b, double[] h)
        {
            int k = 0;
            double q, hh;
            Expansion.TwoProduct(e[0], b, out q, out hh);
            if (hh != 0) h[k++] = hh;
            for (int i = 1; i < elen; ++i) {
                double p1, p0, s;
                Expansion.TwoProduct(e[i], b, out p1, out p0);
                two_sum(q, p0, out s, out hh);
                if (hh != 0) h[k++] = hh;
                // fast two-sum (|p1| >= |s|)
                q = p1 + s;
                hh = s - (q - p1);
                if (hh != 0) h[k++] = hh;
            }
            if (q != 0 || k == 0) h[k++] = q;
            return k;
        }

        // x + xtail as an expansion, for a rounding error |xtail| below half an ulp of x (or a value 2 x
        // with such an error: the two do not overlap)
        static int two(double x, double xtail, double[] h)
        {
            int k = 0;
            if (xtail != 0) h[k++] = xtail;
            h[k++] = x;
            return k;
        }

        // a * b as an expansion
        static int product(double a, double b, double[] h)
        {
            double x, y;
            Expansion.TwoProduct(a, b, out x, out y);
            int k = 0;
            if (y != 0) h[k++] = y;
            h[k++] = x;
            return k;
        }

        /// <summary>
        /// The part of the 2x2 determinant (ax + axtail)(by + bytail) - (ay + aytail)(bx + bxtail) with rounding
        /// errors, ax bytail + axtail (by + bytail) - ay bxtail - aytail (bx + bxtail), into t (up to 12
        /// components; empty if the errors are zero)
        /// </summary>
        static void cross_tail(double ax, double axtail, double by, double bytail, double ay, double aytail, double bx, double bxtail,
                               Workspace w, GrowingExpansion t)
        {
            t.Clear();
            if (bytail != 0)
                t.Add(w.S1, product(ax, bytail, w.S1));
            if (axtail != 0)
                t.Add(w.S1, scale(w.F2, two(by, bytail, w.F2), axtail, w.S1));
            if (bxtail != 0)
                t.Add(w.S1, product(-ay, bxtail, w.S1));
            if (aytail != 0)
                t.Add(w.S1, scale(w.F2, two(bx, bxtail, w.F2), -aytail, w.S1));
        }

        /// <summary> an expansion that grows by sums (double buffered) </summary>
        sealed class GrowingExpansion
        {
            public double[] X, Y;
            public int N;
            public GrowingExpansion(int capacity) { X = new double[capacity]; Y = new double[capacity]; }

            public void Clear() { N = 0; }

            public void Add(double[] e, int n)
            {
                if (n == 0)
                    return;
                if (N == 0) {
                    Array.Copy(e, X, n);
                    N = n;
                    return;
                }
                N = sum(X, N, e, n, Y);
                double[] t = X; X = Y; Y = t;
            }
        }

        static double estimate(double[] e, int n)
        {
            double s = e[0];
            for (int i = 1; i < n; ++i) s += e[i];
            return s;
        }

        // the largest component decides
        static int sign(double[] e, int n)
        {
            return Math.Sign(e[n - 1]);
        }

        sealed class Workspace
        {
            [ThreadStatic] static Workspace current;
            public static Workspace Get() { return current ?? (current = new Workspace()); }

            public readonly double[] E2 = new double[2], F2 = new double[2];
            public readonly double[] B = new double[4], Bc = new double[4], Ca = new double[4], Ab = new double[4];
            public readonly double[] T8a = new double[8], T8b = new double[8], T8c = new double[8];
            public readonly double[] T16 = new double[16], T16b = new double[16];
            public readonly double[] T32a = new double[32], T32b = new double[32], T64 = new double[64];
            public readonly double[] Fin1 = new double[96];
            // stage D. Bounds: T 12 components; M = minor + T 16; incircle: L 8, D = B (96) + three times
            // (2 * 48 + 8 * 32) = 1152 (orient3d: 24 + 3 * (24 + 32) = 192); S1 and S2 hold scaled T and M
            public readonly double[] S1 = new double[32], S2 = new double[48];
            public readonly GrowingExpansion T = new GrowingExpansion(12), M = new GrowingExpansion(16), L = new GrowingExpansion(8), D = new GrowingExpansion(1152);
        }
    }
}
