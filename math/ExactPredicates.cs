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


        // exact versions: with floating-point expansions (Shewchuk) when no product can overflow or underflow,
        // otherwise with all coordinates as integers with a common power-of-two scale

        static int incircle_exact(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            if (a == d || b == d || c == d || a == b || b == c || a == c)
                return 0;
            if (Expansion.InRange(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y) == false)
                return incircle_bigint(a, b, c, d);
            var t = Expansion.Terms.Get();
            int adx = t.Diff(a.x, d.x), ady = t.Diff(a.y, d.y), bdx = t.Diff(b.x, d.x), bdy = t.Diff(b.y, d.y);
            int cdx = t.Diff(c.x, d.x), cdy = t.Diff(c.y, d.y);
            int alift = t.Add(t.Mul(adx, adx), t.Mul(ady, ady));
            int blift = t.Add(t.Mul(bdx, bdx), t.Mul(bdy, bdy));
            int clift = t.Add(t.Mul(cdx, cdx), t.Mul(cdy, cdy));
            int bc = t.Add(t.Mul(bdx, cdy), t.Neg(t.Mul(cdx, bdy)));
            int ca = t.Add(t.Mul(cdx, ady), t.Neg(t.Mul(adx, cdy)));
            int ab = t.Add(t.Mul(adx, bdy), t.Neg(t.Mul(bdx, ady)));
            return t.Sign(t.Add(t.Add(t.Mul(alift, bc), t.Mul(blift, ca)), t.Mul(clift, ab)));
        }

        static int orient2d_exact(Vector2d a, Vector2d b, Vector2d c)
        {
            if (a == b || b == c || a == c)
                return 0;
            if (Expansion.InRange(a.x, a.y, b.x, b.y, c.x, c.y) == false)
                return orient2d_bigint(a, b, c);
            var t = Expansion.Terms.Get();
            int ux = t.Diff(b.x, a.x), uy = t.Diff(b.y, a.y), vx = t.Diff(c.x, a.x), vy = t.Diff(c.y, a.y);
            return t.Sign(t.Add(t.Mul(ux, vy), t.Neg(t.Mul(uy, vx))));
        }

        static int orient3d_exact(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            if (a == b || a == c || a == d || b == c || b == d || c == d)
                return 0;
            if (Expansion.InRange(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z) == false)
                return orient3d_bigint(a, b, c, d);
            var t = Expansion.Terms.Get();
            int ux = t.Diff(b.x, a.x), uy = t.Diff(b.y, a.y), uz = t.Diff(b.z, a.z);
            int vx = t.Diff(c.x, a.x), vy = t.Diff(c.y, a.y), vz = t.Diff(c.z, a.z);
            int wx = t.Diff(d.x, a.x), wy = t.Diff(d.y, a.y), wz = t.Diff(d.z, a.z);
            int cx = t.Add(t.Mul(uy, vz), t.Neg(t.Mul(uz, vy)));
            int cy = t.Add(t.Mul(uz, vx), t.Neg(t.Mul(ux, vz)));
            int cz = t.Add(t.Mul(ux, vy), t.Neg(t.Mul(uy, vx)));
            return t.Sign(t.Add(t.Add(t.Mul(wx, cx), t.Mul(wy, cy)), t.Mul(wz, cz)));
        }

        internal static int incircle_bigint(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            BigInteger[] v = to_integers(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y);
            BigInteger adx = v[0] - v[6], ady = v[1] - v[7], bdx = v[2] - v[6], bdy = v[3] - v[7], cdx = v[4] - v[6], cdy = v[5] - v[7];
            BigInteger det = (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy)
                + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
                + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
            return det.Sign;
        }

        internal static int orient2d_bigint(Vector2d a, Vector2d b, Vector2d c)
        {
            BigInteger[] v = to_integers(a.x, a.y, b.x, b.y, c.x, c.y);
            BigInteger ux = v[2] - v[0], uy = v[3] - v[1], vx = v[4] - v[0], vy = v[5] - v[1];
            return (ux * vy - uy * vx).Sign;
        }

        internal static int orient3d_bigint(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            BigInteger[] p = to_integers(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z);
            BigInteger ux = p[3] - p[0], uy = p[4] - p[1], uz = p[5] - p[2];
            BigInteger vx = p[6] - p[0], vy = p[7] - p[1], vz = p[8] - p[2];
            BigInteger wx = p[9] - p[0], wy = p[10] - p[1], wz = p[11] - p[2];
            return (wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx)).Sign;
        }

        // exact for the arguments of the public predicates (for tests)
        internal static int Orient3dExact(Vector3d a, Vector3d b, Vector3d c, Vector3d d) { return orient3d_exact(a, b, c, d); }
        internal static int Orient2dExact(Vector2d a, Vector2d b, Vector2d c) { return orient2d_exact(a, b, c); }
        internal static int InCircleExact(Vector2d a, Vector2d b, Vector2d c, Vector2d d) { return incircle_exact(a, b, c, d); }

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


    /// <summary>
    /// Exact sums and products of doubles with error-free transformations (Shewchuk, "Adaptive Precision
    /// Floating-Point Arithmetic and Fast Robust Geometric Predicates", 1997). A value is a list of doubles whose
    /// exact sum it is; products expand into the pairwise exact products (TwoProduct), the sign is taken after
    /// summing the list into a non-overlapping expansion (grow-expansion). Exact as long as nothing overflows
    /// or underflows (see InRange). Lists live in a per-thread buffer, no allocations per predicate.
    /// </summary>
    internal static class Expansion
    {
        const double Splitter = 134217729.0;       // 2^27 + 1
        const double MinMagnitude = 7.888609052210118e-31;     // 2^-100
        const double MaxMagnitude = 1.2676506002282294e30;     // 2^100

        /// <summary>
        /// All nonzero values have magnitudes in [2^-100, 2^100]: then the degree-4 products of the
        /// predicates (and their error terms) stay far from overflow and from subnormal numbers.
        /// </summary>
        static bool in_range(double x)
        {
            double ax = Math.Abs(x);
            if (double.IsNaN(x))
                throw new ArgumentException("ExactPredicates: coordinates must be finite");
            return ax == 0 || (ax >= MinMagnitude && ax <= MaxMagnitude);
        }
        public static bool InRange(double a, double b, double c, double d, double e, double f)
        {
            return in_range(a) && in_range(b) && in_range(c) && in_range(d) && in_range(e) && in_range(f);
        }
        public static bool InRange(double a, double b, double c, double d, double e, double f, double g, double h)
        {
            return InRange(a, b, c, d, e, f) && in_range(g) && in_range(h);
        }
        public static bool InRange(double a, double b, double c, double d, double e, double f, double g, double h, double i, double j, double k, double l)
        {
            return InRange(a, b, c, d, e, f) && InRange(g, h, i, j, k, l);
        }

        static void two_sum(double a, double b, out double x, out double y)
        {
            x = a + b;
            double bv = x - a, av = x - bv;
            y = (a - av) + (b - bv);
        }

        static void two_product(double a, double b, out double x, out double y)
        {
            x = a * b;
            double c = Splitter * a, ahi = c - (c - a), alo = a - ahi;
            double d = Splitter * b, bhi = d - (d - b), blo = b - bhi;
            double err = x - ahi * bhi - alo * bhi - ahi * blo;
            y = alo * blo - err;
        }

        /// <summary> lists of doubles in one growing buffer; a list is (start, count), referenced by an id </summary>
        public class Terms
        {
            [ThreadStatic] static Terms instance;
            double[] buf = new double[1024];
            int top;
            int[] start = new int[64], count = new int[64];
            int lists;

            public static Terms Get()
            {
                if (instance == null)
                    instance = new Terms();
                instance.top = 0;
                instance.lists = 0;
                return instance;
            }

            int begin()
            {
                if (lists == start.Length) {
                    Array.Resize(ref start, 2 * lists);
                    Array.Resize(ref count, 2 * lists);
                }
                start[lists] = top;
                count[lists] = 0;
                return lists++;
            }
            void push(int id, double v)
            {
                if (v == 0) return;
                if (top == buf.Length)
                    Array.Resize(ref buf, 2 * buf.Length);
                buf[top++] = v;
                count[id]++;
            }

            /// <summary> a - b, exact (two doubles) </summary>
            public int Diff(double a, double b)
            {
                int id = begin();
                double x = a - b;
                double bv = a - x, av = x + bv;
                double y = (a - av) + (bv - b);
                push(id, y);
                push(id, x);
                return id;
            }

            public int Neg(int e)
            {
                int id = begin();
                for (int i = 0; i < count[e]; ++i)
                    push(id, -buf[start[e] + i]);
                return id;
            }

            public int Add(int e, int f)
            {
                int id = begin();
                for (int i = 0; i < count[e]; ++i) push(id, buf[start[e] + i]);
                for (int i = 0; i < count[f]; ++i) push(id, buf[start[f] + i]);
                return id;
            }

            public int Mul(int e, int f)
            {
                int id = begin();
                for (int i = 0; i < count[e]; ++i) {
                    double ei = buf[start[e] + i];
                    for (int j = 0; j < count[f]; ++j) {
                        double x, y;
                        two_product(ei, buf[start[f] + j], out x, out y);
                        push(id, y);
                        push(id, x);
                    }
                }
                return id;
            }

            [ThreadStatic] static double[] scratch;

            /// <summary> exact sign of the sum of the list </summary>
            public int Sign(int e)
            {
                int n = count[e];
                if (scratch == null || scratch.Length < n + 1)
                    scratch = new double[Math.Max(256, 2 * n)];
                double[] h = scratch;
                int k = 0;
                for (int i = 0; i < n; ++i) {
                    // grow-expansion in place: add buf[...] to the expansion h[0..k)
                    double q = buf[start[e] + i];
                    int m = 0;
                    for (int j = 0; j < k; ++j) {
                        double qn, hh;
                        two_sum(q, h[j], out qn, out hh);
                        q = qn;
                        if (hh != 0)
                            h[m++] = hh;
                    }
                    if (q != 0)
                        h[m++] = q;
                    k = m;
                }
                return (k == 0) ? 0 : Math.Sign(h[k - 1]);
            }
        }
    }
}
