// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
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
            BigInteger[] v = to_integers(a.x, a.y, b.x, b.y, c.x, c.y);
            BigInteger ux = v[2] - v[0], uy = v[3] - v[1], vx = v[4] - v[0], vy = v[5] - v[1];
            return (ux * vy - uy * vx).Sign;
        }

        static int orient3d_exact(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            BigInteger[] p = to_integers(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z);
            BigInteger ux = p[3] - p[0], uy = p[4] - p[1], uz = p[5] - p[2];
            BigInteger vx = p[6] - p[0], vy = p[7] - p[1], vz = p[8] - p[2];
            BigInteger wx = p[9] - p[0], wy = p[10] - p[1], wz = p[11] - p[2];
            return (wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx)).Sign;
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
