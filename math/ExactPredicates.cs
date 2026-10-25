// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;

namespace g3
{
    /// <summary>
    /// Exact orientation predicates for double coordinates. A floating-point filter with Shewchuk's error
    /// bounds ("Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates", 1997)
    /// decides almost all cases; the uncertain ones are recomputed exactly with integers (every double is
    /// m * 2^e). The sign is always right, also for nearly degenerate input and coordinates far from the
    /// origin, unlike tests with a fixed epsilon.
    /// </summary>
    public static class ExactPredicates
    {
        const double Epsilon = 1.1102230246251565e-16;     // 2^-53
        const double Orient2dBound = (3.0 + 16.0 * Epsilon) * Epsilon;
        const double Orient3dBound = (7.0 + 56.0 * Epsilon) * Epsilon;
        const double InCircleBound = (10.0 + 96.0 * Epsilon) * Epsilon;
        // below this the products may be subnormal (absolute, not relative rounding error)
        const double UnderflowLimit = 1e-200;

        /// <summary> sign of (b - a) x (c - a): +1 if a, b, c are counter-clockwise, -1 clockwise, 0 collinear </summary>
        public static int Orient2d(Vector2d a, Vector2d b, Vector2d c)
        {
            double ux = b.x - a.x, uy = b.y - a.y, vx = c.x - a.x, vy = c.y - a.y;
            double l = ux * vy, r = uy * vx;
            double det = l - r;
            double bound = Orient2dBound * (Math.Abs(l) + Math.Abs(r));
            if (det > bound) return 1;
            if (-det > bound) return -1;
            return orient2d_exact(a, b, c);
        }

        /// <summary>
        /// sign of ((b - a) x (c - a)) . (d - a): +1 if d is on the side the normal of the counter-clockwise
        /// triangle (a, b, c) points to, -1 on the other side, 0 if the four points are coplanar
        /// </summary>
        public static int Orient3d(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
            double vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
            double wx = d.x - a.x, wy = d.y - a.y, wz = d.z - a.z;
            double uyvz = uy * vz, uzvy = uz * vy, uzvx = uz * vx, uxvz = ux * vz, uxvy = ux * vy, uyvx = uy * vx;
            double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);
            double permanent = Math.Abs(wx) * (Math.Abs(uyvz) + Math.Abs(uzvy))
                + Math.Abs(wy) * (Math.Abs(uzvx) + Math.Abs(uxvz))
                + Math.Abs(wz) * (Math.Abs(uxvy) + Math.Abs(uyvx));
            double bound = Orient3dBound * permanent;
            // the relative error bound does not hold when products underflow (subnormal): exact then
            if (det > bound && permanent > UnderflowLimit) return 1;
            if (-det > bound && permanent > UnderflowLimit) return -1;
            return orient3d_exact(a, b, c, d);
        }


        /// <summary>
        /// +1 if d lies inside the circle through a, b, c (given counter-clockwise; the sign is reversed for
        /// clockwise input), -1 outside, 0 if the four points are cocircular
        /// </summary>
        public static int InCircle(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            double adx = a.x - d.x, bdx = b.x - d.x, cdx = c.x - d.x;
            double ady = a.y - d.y, bdy = b.y - d.y, cdy = c.y - d.y;
            double bdxcdy = bdx * cdy, cdxbdy = cdx * bdy, alift = adx * adx + ady * ady;
            double cdxady = cdx * ady, adxcdy = adx * cdy, blift = bdx * bdx + bdy * bdy;
            double adxbdy = adx * bdy, bdxady = bdx * ady, clift = cdx * cdx + cdy * cdy;
            double det = alift * (bdxcdy - cdxbdy) + blift * (cdxady - adxcdy) + clift * (adxbdy - bdxady);
            double permanent = (Math.Abs(bdxcdy) + Math.Abs(cdxbdy)) * alift
                + (Math.Abs(cdxady) + Math.Abs(adxcdy)) * blift
                + (Math.Abs(adxbdy) + Math.Abs(bdxady)) * clift;
            double bound = InCircleBound * permanent;
            if (det > bound && permanent > UnderflowLimit) return 1;
            if (-det > bound && permanent > UnderflowLimit) return -1;
            return incircle_exact(a, b, c, d);
        }


        // exact versions: all coordinates as integers with a common power-of-two scale

        static int incircle_exact(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            BigInteger[] v = to_integers(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y);
            BigInteger adx = v[0] - v[6], ady = v[1] - v[7], bdx = v[2] - v[6], bdy = v[3] - v[7], cdx = v[4] - v[6], cdy = v[5] - v[7];
            BigInteger det = (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy)
                + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
                + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
            return det.Sign;
        }


        static int orient2d_exact(Vector2d a, Vector2d b, Vector2d c)
        {
            if (expansion_safe(a.x, a.y, b.x, b.y, c.x, c.y))
                return orient2d_expansion(a, b, c);
            BigInteger[] v = to_integers(a.x, a.y, b.x, b.y, c.x, c.y);
            BigInteger ux = v[2] - v[0], uy = v[3] - v[1], vx = v[4] - v[0], vy = v[5] - v[1];
            return (ux * vy - uy * vx).Sign;
        }

        static int orient3d_exact(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            if (expansion_safe(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z))
                return orient3d_expansion(a, b, c, d);
            BigInteger[] p = to_integers(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z);
            BigInteger ux = p[3] - p[0], uy = p[4] - p[1], uz = p[5] - p[2];
            BigInteger vx = p[6] - p[0], vy = p[7] - p[1], vz = p[8] - p[2];
            BigInteger wx = p[9] - p[0], wy = p[10] - p[1], wz = p[11] - p[2];
            return (wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx)).Sign;
        }

        // --- exact floating point expansions (Shewchuk 1997): sums of non-overlapping doubles, in order of
        // increasing magnitude; the sign is that of the largest component. Exact as long as no product
        // underflows or overflows, so only used for coordinates in [1e-90, 1e90] (else BigInteger).

        static bool expansion_safe(params double[] values)
        {
            foreach (double v in values) {
                double m = Math.Abs(v);
                if (double.IsNaN(v) || double.IsInfinity(v))
                    throw new ArgumentException("ExactPredicates: coordinates must be finite");
                if (m != 0 && (m < 1e-90 || m > 1e90))
                    return false;
            }
            return true;
        }

        static void two_sum(double a, double b, out double x, out double y)
        {
            x = a + b;
            double bv = x - a, av = x - bv;
            y = (a - av) + (b - bv);
        }

        static void two_product(double a, double b, out double x, out double y)
        {
#if NETCOREAPP3_0_OR_GREATER
            x = a * b;
            y = Math.FusedMultiplyAdd(a, b, -x);
#else
            TwoProductDekker(a, b, out x, out y);
#endif
        }

        /// <summary> a * b = x + y exactly, without FMA (Dekker); used where FMA is not available (netstandard2.0) </summary>
        internal static void TwoProductDekker(double a, double b, out double x, out double y)
        {
            x = a * b;
            // split both factors into 26-bit halves, whose products are exact
            double ahi, alo, bhi, blo;
            split(a, out ahi, out alo);
            split(b, out bhi, out blo);
            double err1 = x - (ahi * bhi);
            double err2 = err1 - (alo * bhi);
            double err3 = err2 - (ahi * blo);
            y = (alo * blo) - err3;
        }

        static void split(double a, out double hi, out double lo)
        {
            const double splitter = 134217729.0;        // 2^27 + 1
            double c = splitter * a;
            double abig = c - a;
            hi = c - abig;
            lo = a - hi;
        }

        // e + b (e an expansion of n components), zero components eliminated
        static List<double> grow(List<double> e, double b)
        {
            var h = new List<double>(e.Count + 1);
            double q = b;
            foreach (double ei in e) {
                double qn, hh;
                two_sum(q, ei, out qn, out hh);
                q = qn;
                if (hh != 0) h.Add(hh);
            }
            if (q != 0 || h.Count == 0) h.Add(q);
            return h;
        }

        static List<double> sum(List<double> e, List<double> f)
        {
            var h = e;
            foreach (double fi in f)
                if (fi != 0) h = grow(h, fi);
            return h;
        }

        // e * b, zero components eliminated
        static List<double> scale(List<double> e, double b)
        {
            var h = new List<double>(2 * e.Count);
            if (e.Count == 0 || b == 0) { h.Add(0); return h; }
            double q, hh;
            two_product(e[0], b, out q, out hh);
            if (hh != 0) h.Add(hh);
            for (int i = 1; i < e.Count; ++i) {
                double p1, p0, s, t;
                two_product(e[i], b, out p1, out p0);
                two_sum(q, p0, out s, out hh);
                if (hh != 0) h.Add(hh);
                // fast two-sum: |p1| >= |s|
                t = p1 + s;
                hh = s - (t - p1);
                q = t;
                if (hh != 0) h.Add(hh);
            }
            if (q != 0 || h.Count == 0) h.Add(q);
            return h;
        }

        static List<double> negate(List<double> e)
        {
            var h = new List<double>(e.Count);
            foreach (double x in e) h.Add(-x);
            return h;
        }

        static int sign(List<double> e)
        {
            for (int i = e.Count - 1; i >= 0; --i)
                if (e[i] != 0) return Math.Sign(e[i]);
            return 0;
        }

        // a1 * b1 - a2 * b2, exactly
        static List<double> cross_term(double a1, double b1, double a2, double b2)
        {
            double x1, y1, x2, y2;
            two_product(a1, b1, out x1, out y1);
            two_product(a2, b2, out x2, out y2);
            var e = grow(new List<double>() { y1 }, x1);
            e = grow(e, -y2);
            return grow(e, -x2);
        }

        static int orient2d_expansion(Vector2d a, Vector2d b, Vector2d c)
        {
            // (b - a) x (c - a) = (bx cy - by cx) + (ax by - ay bx) + (cx ay - cy ax), from the coordinates
            var det = sum(sum(cross_term(b.x, c.y, b.y, c.x), cross_term(a.x, b.y, a.y, b.x)), cross_term(c.x, a.y, c.y, a.x));
            return sign(det);
        }

        static int orient3d_expansion(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            // Shewchuk's orient3dexact (2x2 minors of x, y, then the z combinations); this combination has
            // our sign convention
            var ab = cross_term(a.x, b.y, b.x, a.y);
            var bc = cross_term(b.x, c.y, c.x, b.y);
            var cd = cross_term(c.x, d.y, d.x, c.y);
            var da = cross_term(d.x, a.y, a.x, d.y);
            var ac = cross_term(a.x, c.y, c.x, a.y);
            var bd = cross_term(b.x, d.y, d.x, b.y);
            var abc = sum(sum(scale(bc, a.z), scale(ac, -b.z)), scale(ab, c.z));
            var bcd = sum(sum(scale(cd, b.z), scale(bd, -c.z)), scale(bc, d.z));
            var cda = sum(sum(scale(da, c.z), scale(ac, d.z)), scale(cd, a.z));
            var dab = sum(sum(scale(ab, d.z), scale(bd, a.z)), scale(da, b.z));
            var det = sum(sum(bcd, negate(cda)), sum(dab, negate(abc)));
            return sign(det);
        }

        static BigInteger[] to_integers(params double[] values)
        {
            int n = values.Length;
            long[] mant = new long[n];
            int[] exp = new int[n];
            int emin = int.MaxValue;
            for (int i = 0; i < n; ++i) {
                double v = values[i];
                if (double.IsNaN(v) || double.IsInfinity(v))
                    throw new ArgumentException("ExactPredicates: coordinates must be finite");
                decompose(v, out mant[i], out exp[i]);
                if (mant[i] != 0)
                    emin = Math.Min(emin, exp[i]);
            }
            if (emin == int.MaxValue)
                emin = 0;
            BigInteger[] result = new BigInteger[n];
            for (int i = 0; i < n; ++i)
                result[i] = (mant[i] == 0) ? BigInteger.Zero : new BigInteger(mant[i]) << (exp[i] - emin);
            return result;
        }

        // v = mantissa * 2^exponent exactly
        static void decompose(double v, out long mantissa, out int exponent)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            bool negative = bits < 0;
            int e = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) {
                exponent = -1074;           // zero or subnormal
            } else {
                frac |= 1L << 52;
                exponent = e - 1075;
            }
            mantissa = negative ? -frac : frac;
        }
    }
}
