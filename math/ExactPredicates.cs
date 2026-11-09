// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Exact orientation predicates for double coordinates. A floating-point filter with Shewchuk's error
    /// bounds ("Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates", 1997)
    /// decides almost all cases; the uncertain ones go through Shewchuk's adaptive stages B to D
    /// (AdaptivePredicates, floating-point expansions, exact), or, where products could overflow or underflow,
    /// through integer arithmetic (every double is m * 2^e). The sign is always right, also for nearly
    /// degenerate input and coordinates far from the origin, unlike tests with a fixed epsilon.
    /// </summary>
    public static class ExactPredicates
    {
        const double Epsilon = 1.1102230246251565e-16;     // 2^-53
        const double Orient2dBound = (3.0 + 16.0 * Epsilon) * Epsilon;
        const double Orient3dBound = (7.0 + 56.0 * Epsilon) * Epsilon;
        const double InCircleBound = (10.0 + 96.0 * Epsilon) * Epsilon;
        const double InSphereBound = (16.0 + 224.0 * Epsilon) * Epsilon;
        // below this the products may be subnormal (absolute, not relative rounding error)
        const double UnderflowLimit = 1e-200;
        // A larger permanent does not exclude subnormal partial products either: a subnormal 2x2 product (absolute
        // error up to 2^-1075) times a large coordinate or lift. With fl(x * y) = x y (1 + d) + n, |n| <= 2^-1075
        // (sums that underflow are exact), these errors add at most 4 * 2^-1075 times the sum of the factors they
        // are multiplied by (plus 1); the filters add that sum times 2^-1066.
        const double UnderflowUnit = double.Epsilon * 256;     // 2^-1066

        /// <summary> sign of (b - a) x (c - a): +1 if a, b, c are counter-clockwise, -1 clockwise, 0 collinear </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int Orient2d(Vector2d a, Vector2d b, Vector2d c)
        {
            PredicateStats.Call(PredicateStats.Kind.Orient2d);
            double ux = b.x - a.x, uy = b.y - a.y, vx = c.x - a.x, vy = c.y - a.y;
            double l = ux * vy, r = uy * vx;
            double det = l - r;
            double sum = Math.Abs(l) + Math.Abs(r);
            double bound = Orient2dBound * sum;
            // the relative error bound does not hold when products underflow (subnormal): exact then
            if (det > bound && sum > UnderflowLimit) return 1;
            if (-det > bound && sum > UnderflowLimit) return -1;
            PredicateStats.ExactStage(PredicateStats.Kind.Orient2d);
            return orient2d_exact(a, b, c);
        }

        /// <summary>
        /// sign of ((b - a) x (c - a)) . (d - a): +1 if d is on the side the normal of the counter-clockwise
        /// triangle (a, b, c) points to, -1 on the other side, 0 if the four points are coplanar
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int Orient3d(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            PredicateStats.Call(PredicateStats.Kind.Orient3d);
            int s = Orient3dFilter(b.x - a.x, b.y - a.y, b.z - a.z, c.x - a.x, c.y - a.y, c.z - a.z, d.x - a.x, d.y - a.y, d.z - a.z);
            if (s != 0) return s;
            PredicateStats.ExactStage(PredicateStats.Kind.Orient3d);
            return orient3d_exact(a, b, c, d);
        }

        /// <summary>
        /// The floating-point filter of Orient3d(a, b, c, d), given the differences u = b - a, v = c - a, w = d - a
        /// (each rounded once, as Orient3d computes them): the sign of ((b - a) x (c - a)) . (d - a) where the error
        /// bound proves it, 0 where it does not decide (then Orient3d(a, b, c, d) decides). The only implementation of
        /// the filter, so a caller that shares the differences between several predicates (ExactRayTriangle) gets
        /// Orient3d's decisions bit for bit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Orient3dFilter(double ux, double uy, double uz, double vx, double vy, double vz,
            double wx, double wy, double wz)
        {
            double uyvz = uy * vz, uzvy = uz * vy, uzvx = uz * vx, uxvz = ux * vz, uxvy = ux * vy, uyvx = uy * vx;
            double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);
            double permanent = Math.Abs(wx) * (Math.Abs(uyvz) + Math.Abs(uzvy))
                + Math.Abs(wy) * (Math.Abs(uzvx) + Math.Abs(uxvz))
                + Math.Abs(wz) * (Math.Abs(uxvy) + Math.Abs(uyvx));
            // (the 2x2 products have absolute errors of their own, multiplied by |w|)
            double bound = Orient3dBound * permanent + (Math.Abs(wx) + Math.Abs(wy) + Math.Abs(wz) + 1) * UnderflowUnit;
            if (det > bound && permanent > UnderflowLimit) return 1;
            if (-det > bound && permanent > UnderflowLimit) return -1;
            return 0;
        }


        /// <summary>
        /// +1 if d lies inside the circle through a, b, c (given counter-clockwise; the sign is reversed for
        /// clockwise input), -1 outside, 0 if the four points are cocircular
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int InCircle(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            PredicateStats.Call(PredicateStats.Kind.InCircle);
            double adx = a.x - d.x, bdx = b.x - d.x, cdx = c.x - d.x;
            double ady = a.y - d.y, bdy = b.y - d.y, cdy = c.y - d.y;
            double bdxcdy = bdx * cdy, cdxbdy = cdx * bdy, alift = adx * adx + ady * ady;
            double cdxady = cdx * ady, adxcdy = adx * cdy, blift = bdx * bdx + bdy * bdy;
            double adxbdy = adx * bdy, bdxady = bdx * ady, clift = cdx * cdx + cdy * cdy;
            double det = alift * (bdxcdy - cdxbdy) + blift * (cdxady - adxcdy) + clift * (adxbdy - bdxady);
            double permanent = (Math.Abs(bdxcdy) + Math.Abs(cdxbdy)) * alift
                + (Math.Abs(cdxady) + Math.Abs(adxcdy)) * blift
                + (Math.Abs(adxbdy) + Math.Abs(bdxady)) * clift;
            // (the 2x2 products and the lifts have absolute errors of their own, multiplied by each other)
            double bound = InCircleBound * permanent + (alift + blift + clift + Math.Abs(bdxcdy) + Math.Abs(cdxbdy)
                + Math.Abs(cdxady) + Math.Abs(adxcdy) + Math.Abs(adxbdy) + Math.Abs(bdxady) + 1) * UnderflowUnit;
            if (det > bound && permanent > UnderflowLimit) return 1;
            if (-det > bound && permanent > UnderflowLimit) return -1;
            PredicateStats.ExactStage(PredicateStats.Kind.InCircle);
            return incircle_exact(a, b, c, d);
        }


        /// <summary>
        /// +1 if e lies inside the sphere through a, b, c, d (given positively oriented, Orient3d(a, b, c, d) > 0;
        /// the sign is reversed for negative orientation), -1 outside, 0 if the five points are cospherical.
        /// Exact (floating-point filter, integer arithmetic for the uncertain cases).
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int InSphere(Vector3d a, Vector3d b, Vector3d c, Vector3d d, Vector3d e)
        {
            double aex = a.x - e.x, bex = b.x - e.x, cex = c.x - e.x, dex = d.x - e.x;
            double aey = a.y - e.y, bey = b.y - e.y, cey = c.y - e.y, dey = d.y - e.y;
            double aez = a.z - e.z, bez = b.z - e.z, cez = c.z - e.z, dez = d.z - e.z;
            double aexbey = aex * bey, bexaey = bex * aey, bexcey = bex * cey, cexbey = cex * bey;
            double cexdey = cex * dey, dexcey = dex * cey, dexaey = dex * aey, aexdey = aex * dey;
            double aexcey = aex * cey, cexaey = cex * aey, bexdey = bex * dey, dexbey = dex * bey;
            double ab = aexbey - bexaey, bc = bexcey - cexbey, cd = cexdey - dexcey, da = dexaey - aexdey;
            double ac = aexcey - cexaey, bd = bexdey - dexbey;
            double abc = aez * bc - bez * ac + cez * ab;
            double bcd = bez * cd - cez * bd + dez * bc;
            double cda = cez * da + dez * ac + aez * cd;
            double dab = dez * ab + aez * bd + bez * da;
            double alift = aex * aex + aey * aey + aez * aez, blift = bex * bex + bey * bey + bez * bez;
            double clift = cex * cex + cey * cey + cez * cez, dlift = dex * dex + dey * dey + dez * dez;
            // (Shewchuk's insphere determinant; positive inside for his orientation, which is the opposite of Orient3d)
            double det = (dlift * abc - clift * dab) + (blift * cda - alift * bcd);
            double aezp = Math.Abs(aez), bezp = Math.Abs(bez), cezp = Math.Abs(cez), dezp = Math.Abs(dez);
            double abp = Math.Abs(aexbey) + Math.Abs(bexaey), bcp = Math.Abs(bexcey) + Math.Abs(cexbey);
            double cdp = Math.Abs(cexdey) + Math.Abs(dexcey), dap = Math.Abs(dexaey) + Math.Abs(aexdey);
            double acp = Math.Abs(aexcey) + Math.Abs(cexaey), bdp = Math.Abs(bexdey) + Math.Abs(dexbey);
            double permanent = (cdp * bezp + bdp * cezp + bcp * dezp) * alift
                + (dap * cezp + acp * dezp + cdp * aezp) * blift
                + (abp * dezp + bdp * aezp + dap * bezp) * clift
                + (bcp * aezp + acp * bezp + abp * cezp) * dlift;
            // (the 2x2 products have absolute errors, multiplied by z and the lifts; the lifts', multiplied by
            // the 3x3 minors)
            double bound = InSphereBound * permanent + ((alift + blift + clift + dlift) * (aezp + bezp + cezp + dezp + 1)
                + Math.Abs(abc) + Math.Abs(bcd) + Math.Abs(cda) + Math.Abs(dab) + 1) * UnderflowUnit;
            if (det > bound && permanent > UnderflowLimit) return -1;
            if (-det > bound && permanent > UnderflowLimit) return 1;
            return insphere_exact(a, b, c, d, e);
        }

        [MethodImpl(JitHints.Optimize)]
        static int insphere_exact(Vector3d a, Vector3d b, Vector3d c, Vector3d d, Vector3d e)
        {
            if (a == e || b == e || c == e || d == e)
                return 0;
            return insphere_bigint(a, b, c, d, e);
        }

        [MethodImpl(JitHints.Optimize)]
        internal static int insphere_bigint(Vector3d a, Vector3d b, Vector3d c, Vector3d d, Vector3d e)
        {
            BigInteger[] v = to_integers(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z, e.x, e.y, e.z);
            BigInteger aex = v[0] - v[12], aey = v[1] - v[13], aez = v[2] - v[14];
            BigInteger bex = v[3] - v[12], bey = v[4] - v[13], bez = v[5] - v[14];
            BigInteger cex = v[6] - v[12], cey = v[7] - v[13], cez = v[8] - v[14];
            BigInteger dex = v[9] - v[12], dey = v[10] - v[13], dez = v[11] - v[14];
            BigInteger ab = aex * bey - bex * aey, bc = bex * cey - cex * bey, cd = cex * dey - dex * cey;
            BigInteger da = dex * aey - aex * dey, ac = aex * cey - cex * aey, bd = bex * dey - dex * bey;
            BigInteger abc = aez * bc - bez * ac + cez * ab;
            BigInteger bcd = bez * cd - cez * bd + dez * bc;
            BigInteger cda = cez * da + dez * ac + aez * cd;
            BigInteger dab = dez * ab + aez * bd + bez * da;
            BigInteger alift = aex * aex + aey * aey + aez * aez, blift = bex * bex + bey * bey + bez * bez;
            BigInteger clift = cex * cex + cey * cey + cez * cez, dlift = dex * dex + dey * dey + dez * dez;
            return -((dlift * abc - clift * dab) + (blift * cda - alift * bcd)).Sign;
        }


        // exact versions: when no product can overflow or underflow, Shewchuk's adaptive stages B to D;
        // otherwise all coordinates as integers with a common power-of-two scale

        [MethodImpl(JitHints.Optimize)]
        static int incircle_exact(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            if (a == d || b == d || c == d || a == b || b == c || a == c)
                return 0;
            if (Expansion.InRange(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y) == false)
                return incircle_bigint(a, b, c, d);
            return AdaptivePredicates.InCircle(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y);
        }

        [MethodImpl(JitHints.Optimize)]
        static int orient2d_exact(Vector2d a, Vector2d b, Vector2d c)
        {
            if (a == b || b == c || a == c)
                return 0;
            if (Expansion.InRange(a.x, a.y, b.x, b.y, c.x, c.y) == false)
                return orient2d_bigint(a, b, c);
            return AdaptivePredicates.Orient2d(a.x, a.y, b.x, b.y, c.x, c.y);
        }

        [MethodImpl(JitHints.Optimize)]
        static int orient3d_exact(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            if (a == b || a == c || a == d || b == c || b == d || c == d)
                return 0;
            int sign;
            if (orient3d_int(a, b, c, d, out sign))
                return sign;
            if (Expansion.InRange(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z) == false)
                return orient3d_bigint(a, b, c, d);
            // (Shewchuk's orient3d has the opposite sign)
            return -AdaptivePredicates.Orient3d(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z);
        }

        // ---- the exact stage of Orient3d in 64-bit integers (docs/design/CHEAPER_CONSTRUCTIONS.md, section 4.2) ----
        // Every finite double is an integer multiple of 2^e, e the exponent of its lowest set bit, so with e the
        // smallest of these over the twelve coordinates every coordinate difference is an integer multiple of 2^e.
        // TwoDiff gives each difference exactly as hi + lo (both multiples of 2^e), and if |hi 2^-e| < 2^62 (so at
        // most 2^62 - 2^9, with |lo 2^-e| <= 2^8) the difference is an integer of magnitude at most 2^62 - 2^8 in units of 2^e. On CAD inputs (float coordinates)
        // the differences have 48-62 bits, and 90 % of the exact stages are exactly coplanar neighbours.
        // The determinant w . (u x v) is then computed exactly: minors |u_y v_z - u_z v_y| < 2 (2^62)^2 = 2^125
        // (two's complement in 128 bits), terms and their sum |.| < 6 (2^62)^3 < 2^189 (two's complement in 192
        // bits); bounds derived by tools/PredicateCompiler (entry Orient3dInputDiffs of the design's spec) and
        // reviewed. It is the determinant of orient3d_bigint times the positive factor 2^(3 (e' - e)) (e' <= e its
        // scale), so the sign is the same.

        const double Pow62 = 4611686018427387904.0;        // 2^62

        /// <summary> the sign of orient3d(a, b, c, d) by 64-bit integer arithmetic; false if the differences are not
        /// admitted (then nothing is decided) </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static bool orient3d_int(Vector3d a, Vector3d b, Vector3d c, Vector3d d, out int sign)
        {
            sign = 0;
            int e = Math.Min(Math.Min(Math.Min(low_exp(a.x), low_exp(a.y)), Math.Min(low_exp(a.z), low_exp(b.x))),
                Math.Min(Math.Min(low_exp(b.y), low_exp(b.z)), Math.Min(low_exp(c.x), low_exp(c.y))));
            e = Math.Min(e, Math.Min(Math.Min(low_exp(c.z), low_exp(d.x)), Math.Min(low_exp(d.y), low_exp(d.z))));
            // 2^-e must be a normal double (e = int.MaxValue: all coordinates zero, excluded by the caller)
            if (e < -1022 || e > 1022)
                return false;
            double scale = BitConverter.Int64BitsToDouble((long)(1023 - e) << 52);
            long ux, uy, uz, vx, vy, vz, wx, wy, wz;
            if (diff(b.x, a.x, scale, out ux) == false || diff(b.y, a.y, scale, out uy) == false || diff(b.z, a.z, scale, out uz) == false
                || diff(c.x, a.x, scale, out vx) == false || diff(c.y, a.y, scale, out vy) == false || diff(c.z, a.z, scale, out vz) == false
                || diff(d.x, a.x, scale, out wx) == false || diff(d.y, a.y, scale, out wy) == false || diff(d.z, a.z, scale, out wz) == false)
                return false;
            ulong m1h, m1l, m2h, m2l, m3h, m3l;
            minor(uy, vz, uz, vy, out m1h, out m1l);
            minor(uz, vx, ux, vz, out m2h, out m2l);
            minor(ux, vy, uy, vx, out m3h, out m3l);
            ulong s0, s1, s2, t0, t1, t2;
            term(m1h, m1l, wx, out s2, out s1, out s0);
            term(m2h, m2l, wy, out t2, out t1, out t0);
            add192(ref s2, ref s1, ref s0, t2, t1, t0);
            term(m3h, m3l, wz, out t2, out t1, out t0);
            add192(ref s2, ref s1, ref s0, t2, t1, t0);
            sign = ((long)s2 < 0) ? -1 : ((s2 | s1 | s0) == 0) ? 0 : 1;
            return true;
        }

        // the exponent of the lowest set bit of x (int.MaxValue for 0)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int low_exp(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int ex = (int)((bits >> 52) & 0x7FF);
            ulong man = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            if (ex != 0)
                man |= 1UL << 52;
            else
                ex = 1;
            if (man == 0)
                return int.MaxValue;
            return ex - 1075 + trailing_zeros(man);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int trailing_zeros(ulong v)
        {
#if NET
            return System.Numerics.BitOperations.TrailingZeroCount(v);
#else
            return TrailingZerosPortable(v);
#endif
        }

        /// <summary> trailing zero bits of v != 0 for netstandard2.0 (compiled everywhere, so that the tests on .NET check it) </summary>
        internal static int TrailingZerosPortable(ulong v)
        {
            int n = 0;
            while ((v & 1) == 0) { v >>= 1; n++; }
            return n;
        }

        // x - y exactly, in units of 2^e (scale = 2^-e), if its magnitude is below 2^62
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool diff(double x, double y, double scale, out long r)
        {
            // TwoDiff (Shewchuk): hi + lo = x - y exactly (no overflow: an infinite hi fails the test below)
            double hi = x - y;
            double bv = x - hi, av = hi + bv, br = bv - y, ar = x - av;
            double lo = ar + br;
            double h = hi * scale;
            // (NaN fails too)
            if ((Math.Abs(h) < Pow62) == false) {
                r = 0;
                return false;
            }
            // h and lo * scale are integers (multiples of 2^e times 2^-e, exact), |lo| <= ulp(hi) / 2
            r = (long)h + (long)(lo * scale);
            return true;
        }

        // p q - s t in 128-bit two's complement (|p|, |q|, |s|, |t| < 2^62)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void minor(long p, long q, long s, long t, out ulong hi, out ulong lo)
        {
            ulong ah, al, bh, bl;
            product(p, q, out ah, out al);
            product(s, t, out bh, out bl);
            lo = al - bl;
            hi = ah - bh - ((al < bl) ? 1UL : 0UL);
        }

        // p q in 128-bit two's complement
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void product(long p, long q, out ulong hi, out ulong lo)
        {
            ulong ap = (ulong)((p < 0) ? -p : p), aq = (ulong)((q < 0) ? -q : q);
            hi = FixedArith.MulHigh(ap, aq, out lo);
            if ((p < 0) != (q < 0))
                negate128(ref hi, ref lo);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void negate128(ref ulong hi, ref ulong lo)
        {
            lo = ~lo + 1;
            hi = ~hi + ((lo == 0) ? 1UL : 0UL);
        }

        // (mh, ml) * w in 192-bit two's complement (|m| < 2^125, |w| < 2^62)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void term(ulong mh, ulong ml, long w, out ulong r2, out ulong r1, out ulong r0)
        {
            bool neg = (long)mh < 0;
            if (neg)
                negate128(ref mh, ref ml);
            if (w < 0) {
                neg = !neg;
                w = -w;
            }
            ulong aw = (ulong)w, c0, c1;
            c0 = FixedArith.MulHigh(ml, aw, out r0);
            r2 = FixedArith.MulHigh(mh, aw, out c1);
            r1 = c1 + c0;
            r2 += (r1 < c0) ? 1UL : 0UL;
            if (neg) {
                r0 = ~r0; r1 = ~r1; r2 = ~r2;
                r0 += 1;
                ulong k = (r0 == 0) ? 1UL : 0UL;
                r1 += k;
                r2 += (k != 0 && r1 == 0) ? 1UL : 0UL;
            }
        }

        // s += t in 192-bit two's complement (the sums of the terms stay below 2^189 in magnitude)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void add192(ref ulong s2, ref ulong s1, ref ulong s0, ulong t2, ulong t1, ulong t0)
        {
            ulong r0 = s0 + t0;
            ulong k0 = (r0 < s0) ? 1UL : 0UL;
            ulong r1 = s1 + t1;
            ulong k1 = (r1 < s1) ? 1UL : 0UL;
            ulong r1b = r1 + k0;
            k1 += (r1b < r1) ? 1UL : 0UL;
            s0 = r0; s1 = r1b; s2 = s2 + t2 + k1;
        }

        [MethodImpl(JitHints.Optimize)]
        internal static int incircle_bigint(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            BigInteger[] v = to_integers(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y);
            BigInteger adx = v[0] - v[6], ady = v[1] - v[7], bdx = v[2] - v[6], bdy = v[3] - v[7], cdx = v[4] - v[6], cdy = v[5] - v[7];
            BigInteger det = (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy)
                + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
                + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
            return det.Sign;
        }

        [MethodImpl(JitHints.Optimize)]
        internal static int orient2d_bigint(Vector2d a, Vector2d b, Vector2d c)
        {
            BigInteger[] v = to_integers(a.x, a.y, b.x, b.y, c.x, c.y);
            BigInteger ux = v[2] - v[0], uy = v[3] - v[1], vx = v[4] - v[0], vy = v[5] - v[1];
            return (ux * vy - uy * vx).Sign;
        }

        [MethodImpl(JitHints.Optimize)]
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
        internal static int InSphereExact(Vector3d a, Vector3d b, Vector3d c, Vector3d d, Vector3d e) { return insphere_exact(a, b, c, d, e); }

        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
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
    /// Range check and exact product for the expansion arithmetic of AdaptivePredicates (Shewchuk, "Adaptive
    /// Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates", 1997).
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
        [MethodImpl(JitHints.Optimize)]
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

        [MethodImpl(JitHints.Optimize)]
        internal static void TwoProduct(double a, double b, out double x, out double y)
        {
            x = a * b;
            double c = Splitter * a, ahi = c - (c - a), alo = a - ahi;
            double d = Splitter * b, bhi = d - (d - b), blo = b - bhi;
            double err = x - ahi * bhi - alo * bhi - ahi * blo;
            y = alo * blo - err;
        }
    }
}
