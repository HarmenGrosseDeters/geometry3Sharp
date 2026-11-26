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
    /// through integer arithmetic (every double is m * 2^e): the generated kernels of DoublePredicates for
    /// Orient2d, Orient3d and InCircle (the monomials of the determinant, added exactly on the stack; no
    /// allocation), BigInteger for InSphere. The sign is always right, also for nearly
    /// degenerate input and coordinates far from the origin, unlike tests with a fixed epsilon.
    /// </summary>
    public static class ExactPredicates
    {
        internal const double Epsilon = 1.1102230246251565e-16;     // 2^-53
        const double Orient2dBound = (3.0 + 16.0 * Epsilon) * Epsilon;
        internal const double Orient3dBound = (7.0 + 56.0 * Epsilon) * Epsilon;
        internal const double InCircleBound = (10.0 + 96.0 * Epsilon) * Epsilon;
        internal const double InSphereBound = (16.0 + 224.0 * Epsilon) * Epsilon;
        // below this the products may be subnormal (absolute, not relative rounding error). with_underflow relies on
        // UnderflowLimit > 2^-706 and on every bound constant being at least 2^-51 (then B * permanent >= 2^-757 and
        // half an ulp of it exceeds 2^-810, the largest term it skips); ExactPredicatesTests checks this coupling.
        internal const double UnderflowLimit = 1e-200;
        // A larger permanent does not exclude subnormal partial products either: a subnormal 2x2 product (absolute
        // error up to 2^-1075) times a large coordinate or lift. With fl(x * y) = x y (1 + d) + n, |n| <= 2^-1075
        // (sums that underflow are exact), these errors add at most 4 * 2^-1075 times the sum of the factors they
        // are multiplied by (plus 1); the filters add that sum times 2^-1066.
        internal const double UnderflowUnit = double.Epsilon * 256;     // 2^-1066
        // below this sum the underflow term adds nothing to the bound (with_underflow)
        internal const double MinNormal = 2.2250738585072014e-308;     // 2^-1022
        internal const double UnderflowSumNegligible = 1.157920892373162e77;    // 2^256

        /// <summary>
        /// The bound of a filter: relative (its bound constant times the permanent) plus sum times 2^-1066, the same
        /// double as relative + sum * UnderflowUnit wherever the filter can decide, but without that product where it
        /// adds nothing. The product is subnormal for every sum below 2^44, and subnormal arithmetic is slow on
        /// x86 (a microcode assist of about 50 ns per filter call, benchmarks/PredicateComparison/README.md, "Filter underflow term").
        /// Why the result is the same: the filters decide only where permanent > UnderflowLimit (1e-200 > 2^-665),
        /// and every bound constant is at least 2^-51, so relative >= 2^-716 (rounding is monotone and 2^-716 is a
        /// double) and half an ulp of it is at least 2^-769. For sum &lt; 2^256, sum * 2^-1066 &lt; 2^-810 is below that
        /// half ulp and non-negative, so relative + sum * 2^-1066 rounds to relative. NaN and infinite sums take the
        /// full expression (the comparison is false), as before.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double with_underflow(double relative, double sum)
        {
            return sum < UnderflowSumNegligible ? relative : relative + sum * UnderflowUnit;
        }

        /// <summary> sign of (b - a) x (c - a): +1 if a, b, c are counter-clockwise, -1 clockwise, 0 collinear </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int Orient2d(Vector2d a, Vector2d b, Vector2d c)
        {
            PredicateStats.Call(PredicateStats.Kind.Orient2d);
            int s = Orient2dFilter(b.x - a.x, b.y - a.y, c.x - a.x, c.y - a.y);
            if (s != 0) return s;
            PredicateStats.ExactStage(PredicateStats.Kind.Orient2d);
            return orient2d_exact(a, b, c);
        }

        /// <summary>
        /// The floating-point filter of Orient2d(a, b, c), given the differences u = b - a, v = c - a (each rounded once,
        /// as Orient2d computes them): the sign of u x v where the error bound proves it, 0 where it does not decide
        /// (then Orient2d(a, b, c) decides). The only implementation of the filter, so a caller that keeps u for many c
        /// (TriangleTriangleFilter.ProjectedEdges, an edge against many points) gets Orient2d's decisions bit for bit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Orient2dFilter(double ux, double uy, double vx, double vy)
        {
            double l = ux * vy, r = uy * vx;
            double det = l - r;
            double sum = Math.Abs(l) + Math.Abs(r);
            double bound = Orient2dBound * sum;
            // the relative error bound does not hold when products underflow (subnormal): undecided then
            if (det > bound && sum > UnderflowLimit) return 1;
            if (-det > bound && sum > UnderflowLimit) return -1;
            return 0;
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
            double bound = with_underflow(Orient3dBound * permanent, Math.Abs(wx) + Math.Abs(wy) + Math.Abs(wz) + 1);
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
            double bound = with_underflow(InCircleBound * permanent, alift + blift + clift + Math.Abs(bdxcdy) + Math.Abs(cdxbdy)
                + Math.Abs(cdxady) + Math.Abs(adxcdy) + Math.Abs(adxbdy) + Math.Abs(bdxady) + 1);
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
            double bound = with_underflow(InSphereBound * permanent, (alift + blift + clift + dlift) * (aezp + bezp + cezp + dezp + 1)
                + Math.Abs(abc) + Math.Abs(bcd) + Math.Abs(cda) + Math.Abs(dab) + 1);
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
        // otherwise integer arithmetic on the mantissas and exponents (DoublePredicates; docs/design/
        // EXACT_FALLBACK_MONOMIALS.md). The *_bigint functions below evaluate the same determinants with all coordinates
        // as BigInteger on a common power-of-two scale: InSphere's exact stage, and the reference of the tests

        [MethodImpl(JitHints.Optimize)]
        static int incircle_exact(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            if (a == d || b == d || c == d || a == b || b == c || a == c)
                return 0;
            if (Expansion.InRange(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y) == false)
                return DoublePredicates.InCircle(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y);
            return AdaptivePredicates.InCircle(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y);
        }

        [MethodImpl(JitHints.Optimize)]
        static int orient2d_exact(Vector2d a, Vector2d b, Vector2d c)
        {
            if (a == b || b == c || a == c)
                return 0;
            if (Expansion.InRange(a.x, a.y, b.x, b.y, c.x, c.y) == false)
                return DoublePredicates.Orient2d(a.x, a.y, b.x, b.y, c.x, c.y);
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
                return DoublePredicates.Orient3d(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z);
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
        internal static int trailing_zeros(ulong v)
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

        /// <summary>
        /// The sign of a signed volume, exactly: six times it is the sum over the triangles t of det(p_a - o, p_b - o,
        /// p_c - o), where the triangles are the triples corners[3 t .. 3 t + 2] of indices of the points xyz[3 i .. 3 i + 2]
        /// and o = p_corners[0]. +1, -1, or 0 where it is zero. For a closed, consistently oriented surface that is the
        /// sign of its enclosed volume. 0 also where a coordinate is not finite (no sign: as the floating-point
        /// comparison V &lt; 0 of NaN, nothing is inside out).
        ///
        /// Filter (docs/design/EXACT_VOLUME_SIGN.md): each term is evaluated as Orient3dFilter evaluates its
        /// determinant, from differences rounded once, so its error is at most Orient3dBound * permanent_t (Shewchuk's
        /// errboundA, which covers the rounded differences but not the term's last rounding) plus u * |d_t| (that last
        /// rounding) plus UnderflowUnit * (|w_t|_1 + 1) (subnormal products). The recursive sum of the n terms adds at most
        /// gamma_(n-1) * sum |d_t| (Higham, Accuracy and Stability of Numerical Algorithms, 2nd ed., section 4.2). The
        /// sums of the bounds are of non-negative numbers, computed with a relative error of at most gamma_(n-1) each, and
        /// the bound has six roundings on each path: the term u * |d_t|, those errors and roundings are covered by
        /// g = gamma_(n+2) in place of gamma_(n-1) and the factor (1 + 3 g) (with g &lt; 1/4: below about 2^50
        /// triangles). Where |S|
        /// exceeds the bound, S has the sign of the exact sum; elsewhere (and where a value is not finite) the sum is
        /// evaluated exactly, in integers (the coordinates scaled to a common exponent, the differences exact).
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static int VolumeSign(double[] xyz, int[] corners)
        {
            // Stryker disable once Equality: one triangle (3 corners): its first corner is o, so u = 0 and S = 0 exactly; the sign is 0 whether this returns or the filter and the exact sum run
            if (corners == null || corners.Length < 3)
                return 0;
            for (int k = 0; k < xyz.Length; ++k)
                if (double.IsNaN(xyz[k]) || double.IsInfinity(xyz[k]))
                    return 0;
            int s = VolumeSignFilter(xyz, corners);
            return s != 0 ? s : VolumeSignExact(xyz, corners);
        }

        /// <summary>
        /// the filter of VolumeSign: +1 or -1 where the bound proves the sign of the floating-point sum, 0 where it does not
        /// decide (then VolumeSignExact decides); finite coordinates and at least one triangle
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static int VolumeSignFilter(double[] xyz, int[] corners)
        {
            return VolumeSumWithBound(xyz, corners, out double sum, out double bound) ? SignOfBoundedSum(sum, bound) : 0;
        }

        /// <summary> +1 where sum > bound, -1 where -sum > bound, else 0 (the exact sum S has |S_fp - S| &lt;= bound, so then it has the sign of S_fp) </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static int SignOfBoundedSum(double sum, double bound)
        {
            if (sum > bound) return 1;
            if (-sum > bound) return -1;
            return 0;
        }

        /// <summary>
        /// the floating-point sum S_fp of the determinants of VolumeSign and the bound E of the filter (see VolumeSignFilter):
        /// |S_fp - S| &lt;= E, S the exact sum. False where the bound does not hold or is not computable (a value not finite,
        /// g >= 1/4); then sum and bound mean nothing. Finite coordinates and at least one triangle
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static bool VolumeSumWithBound(double[] xyz, int[] corners, out double sum, out double bound)
        {
            int io = 3 * corners[0];
            double ox = xyz[io], oy = xyz[io + 1], oz = xyz[io + 2];
            sum = 0;
            double perm = 0, under = 0, abs = 0;
            int n = corners.Length / 3;
            for (int t = 0; t < n; ++t) {
                int ia = 3 * corners[3 * t], ib = 3 * corners[3 * t + 1], ic = 3 * corners[3 * t + 2];
                // (u, v, w) = (a - o, b - o, c - o); det(u, v, w) = w . (u x v), the form of Orient3dFilter
                double ux = xyz[ia] - ox, uy = xyz[ia + 1] - oy, uz = xyz[ia + 2] - oz;
                double vx = xyz[ib] - ox, vy = xyz[ib + 1] - oy, vz = xyz[ib + 2] - oz;
                double wx = xyz[ic] - ox, wy = xyz[ic + 1] - oy, wz = xyz[ic + 2] - oz;
                double uyvz = uy * vz, uzvy = uz * vy, uzvx = uz * vx, uxvz = ux * vz, uxvy = ux * vy, uyvx = uy * vx;
                double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);
                sum += det;
                abs += Math.Abs(det);
                perm += Math.Abs(wx) * (Math.Abs(uyvz) + Math.Abs(uzvy)) + Math.Abs(wy) * (Math.Abs(uzvx) + Math.Abs(uxvz))
                    + Math.Abs(wz) * (Math.Abs(uxvy) + Math.Abs(uyvx));
                under += Math.Abs(wx) + Math.Abs(wy) + Math.Abs(wz) + 1;
            }
            double g = VolumeBoundG(n);
            bound = VolumeBound(perm, under, abs, g);
            return VolumeBoundUsable(sum, bound, g);
        }

        /// <summary> g = gamma_(n+2) = (n + 2) u / (1 - (n + 2) u) of the bound of VolumeSumWithBound, n the number of triangles </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static double VolumeBoundG(int n)
        {
            return (n + 2) * Epsilon / (1 - (n + 2) * Epsilon);
        }

        /// <summary> the bound E of VolumeSumWithBound from the sums perm, under, abs of its terms: (Orient3dBound perm + UnderflowUnit under + g abs) (1 + 3 g) </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static double VolumeBound(double perm, double under, double abs, double g)
        {
            return (Orient3dBound * perm + UnderflowUnit * under + g * abs) * (1 + 3 * g);
        }

        /// <summary>
        /// whether sum and bound of VolumeSumWithBound are usable: both finite and g &lt; 1/4 (the proof of the bound needs it). A sum
        /// that is not finite has a bound that is not finite (abs, the sum of the |det_t|, is at least |sum|, and a NaN det makes both NaN),
        /// so the tests of the sum are a second guard; a finite sum with a bound that overflows (perm, the sum of the |w| (|uyvz| + |uzvy|)
        /// terms, can overflow where det does not) is the case that only the test of the bound catches
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static bool VolumeBoundUsable(double sum, double bound, double g)
        {
            bool finite = !(double.IsNaN(sum) || double.IsInfinity(sum) || double.IsNaN(bound) || double.IsInfinity(bound));
            return finite && g < 0.25;
        }

        /// <summary>
        /// the filter of TryCompareAbsVolume6: from sum = S_fp and bound = E of VolumeSumWithBound, +1 where |S| > 3 t is proven, -1 where |S| &lt; 3 t,
        /// else false (sign 0, undecided); false too where 3 t overflows
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static bool VolumeCompareFilter(double sum, double bound, double t, out int sign)
        {
            sign = 0;
            double t3 = 3 * t;
            if (!double.IsInfinity(t3)) {
                double a = Math.Abs(sum);
                // (the exact S has |S| in [a - E, a + E]; the slack 4 u per side)
                if ((a - bound) * (1 - 4 * Epsilon) > t3 * (1 + 4 * Epsilon)) { sign = 1; return true; }
                if ((a + bound) * (1 + 4 * Epsilon) < t3 * (1 - 4 * Epsilon)) { sign = -1; return true; }
            }
            return false;
        }

        /// <summary>
        /// Compares |S| = 6 |V| of the triangles with 3 t, exactly (#1191): sign = -1, 0 or +1 for |S| &lt;, =, &gt; 3 t, where
        /// S is the sum of VolumeSign and t is a double (the rule 2 |V| &lt; t of the numerically zero volume: t = ulp(largest
        /// coordinate) * area). False, and nothing decided, where a coordinate is not finite, t is not a positive normal
        /// number, or S_fp overflows (the callers keep). 3 t is formed as the integer 3 * mantissa(t) at the exponent of t, never
        /// as the double 3 * t, which may round. The filter is the proven bound of VolumeSign (docs/design/EXACT_VOLUME_SIGN.md):
        /// |S_fp| - E and |S_fp| + E against 3 t, each side with a relative slack of 4 u that covers the roundings of the
        /// subtraction or addition (u each, E and |S_fp| are non-negative so the addition has one rounding, the subtraction
        /// one), of the product 3 t (u), and of the bound's own evaluation; the bound is E (1 + 3g) already inflated, so the slack
        /// is for the comparison only. Where the filter does not decide, S is evaluated exactly in integers
        /// (VolumeSignExact) and compared as BigInteger: |total| 2^(3 emin) against 3 m_t 2^e_t, shifted to a common exponent.
        /// </summary>
        internal static bool TryCompareAbsVolume6(double[] xyz, int[] corners, double t, out int sign)
        {
            sign = 0;
            if (corners == null || corners.Length < 3 || !(t >= MinNormal) || double.IsInfinity(t))
                return false;
            for (int k = 0; k < xyz.Length; ++k)
                if (double.IsNaN(xyz[k]) || double.IsInfinity(xyz[k]))
                    return false;
            if (VolumeSumWithBound(xyz, corners, out double sum, out double bound)) {
                if (VolumeCompareFilter(sum, bound, t, out sign))
                    return true;
            } else if (double.IsNaN(sum) || double.IsInfinity(sum))
                return false;
            BigInteger[] q = to_integers(xyz, out int emin);
            int io = 3 * corners[0];
            BigInteger total = BigInteger.Zero;
            for (int i = 0; i < corners.Length / 3; ++i) {
                int ia = 3 * corners[3 * i], ib = 3 * corners[3 * i + 1], ic = 3 * corners[3 * i + 2];
                BigInteger ux = q[ia] - q[io], uy = q[ia + 1] - q[io + 1], uz = q[ia + 2] - q[io + 2];
                BigInteger vx = q[ib] - q[io], vy = q[ib + 1] - q[io + 1], vz = q[ib + 2] - q[io + 2];
                BigInteger wx = q[ic] - q[io], wy = q[ic + 1] - q[io + 1], wz = q[ic + 2] - q[io + 2];
                // Stryker disable once Assignment: only |total| is used below
                total += wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx);
            }
            decompose(t, out long mt, out int et);
            // (|S| = |total| 2^(3 emin), 3 t = 3 m_t 2^et: both shifted to the smaller exponent, so that neither shift is negative)
            int low = Math.Min(3 * emin, et);
            BigInteger lhs = BigInteger.Abs(total) << (3 * emin - low), rhs = (3 * new BigInteger(mt)) << (et - low);
            sign = lhs.CompareTo(rhs);
            return true;
        }

        /// <summary>
        /// The sum S of VolumeSign (six times the signed volume of the triangles, relative to their first corner), exactly:
        /// S = total 2^exponent, in integers, without a filter (the comparison of two such sums, #1216: whether a repair changed
        /// the volume of a closed component by more than a factor). False, and nothing computed, without a triangle or where a
        /// coordinate is not finite. For a closed, consistently oriented surface S does not depend on the corner.
        /// </summary>
        internal static bool TryVolumeSum6Exact(double[] xyz, int[] corners, out BigInteger total, out int exponent)
        {
            total = BigInteger.Zero;
            exponent = 0;
            if (corners == null || corners.Length < 3)
                return false;
            for (int k = 0; k < xyz.Length; ++k)
                if (double.IsNaN(xyz[k]) || double.IsInfinity(xyz[k]))
                    return false;
            BigInteger[] q = to_integers(xyz, out int emin);
            int io = 3 * corners[0];
            for (int t = 0; t < corners.Length / 3; ++t) {
                int ia = 3 * corners[3 * t], ib = 3 * corners[3 * t + 1], ic = 3 * corners[3 * t + 2];
                BigInteger ux = q[ia] - q[io], uy = q[ia + 1] - q[io + 1], uz = q[ia + 2] - q[io + 2];
                BigInteger vx = q[ib] - q[io], vy = q[ib + 1] - q[io + 1], vz = q[ib + 2] - q[io + 2];
                BigInteger wx = q[ic] - q[io], wy = q[ic + 1] - q[io + 1], wz = q[ic + 2] - q[io + 2];
                total += wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx);
            }
            exponent = 3 * emin;
            return true;
        }

        /// <summary>
        /// Whether |S1| and |S0| of two exact sums (TryVolumeSum6Exact: S = total 2^exponent) differ by more than the factor
        /// 2^shift, or the sums have different signs: |S1| &gt; 2^shift |S0|, or 2^shift |S1| &lt; |S0|, or sign(S1) != sign(S0).
        /// Exact (integers shifted to the smaller exponent); exactly the factor is not more than it.
        /// </summary>
        internal static bool VolumeSumsDifferByMoreThan(BigInteger total0, int exponent0, BigInteger total1, int exponent1, int shift)
        {
            if (total0.Sign != total1.Sign)
                return true;
            int low = Math.Min(exponent0, exponent1);
            BigInteger a0 = BigInteger.Abs(total0) << (exponent0 - low), a1 = BigInteger.Abs(total1) << (exponent1 - low);
            return a1 > (a0 << shift) || (a1 << shift) < a0;
        }

        /// <summary> VolumeSign without the filter: the sum of the determinants in integers (exact, slow) </summary>
        internal static int VolumeSignExact(double[] xyz, int[] corners)
        {
            if (corners == null || corners.Length < 3)
                return 0;
            for (int k = 0; k < xyz.Length; ++k)
                if (double.IsNaN(xyz[k]) || double.IsInfinity(xyz[k]))
                    return 0;
            BigInteger[] q = to_integers(xyz);
            int io = 3 * corners[0];
            BigInteger total = BigInteger.Zero;
            for (int t = 0; t < corners.Length / 3; ++t) {
                int ia = 3 * corners[3 * t], ib = 3 * corners[3 * t + 1], ic = 3 * corners[3 * t + 2];
                BigInteger ux = q[ia] - q[io], uy = q[ia + 1] - q[io + 1], uz = q[ia + 2] - q[io + 2];
                BigInteger vx = q[ib] - q[io], vy = q[ib + 1] - q[io + 1], vz = q[ib + 2] - q[io + 2];
                BigInteger wx = q[ic] - q[io], wy = q[ic + 1] - q[io + 1], wz = q[ic + 2] - q[io + 2];
                total += wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx);
            }
            return total.Sign;
        }

        // exact for the arguments of the public predicates (for tests)
        internal static int Orient3dExact(Vector3d a, Vector3d b, Vector3d c, Vector3d d) { return orient3d_exact(a, b, c, d); }
        internal static int Orient2dExact(Vector2d a, Vector2d b, Vector2d c) { return orient2d_exact(a, b, c); }
        internal static int InCircleExact(Vector2d a, Vector2d b, Vector2d c, Vector2d d) { return incircle_exact(a, b, c, d); }
        internal static int InSphereExact(Vector3d a, Vector3d b, Vector3d c, Vector3d d, Vector3d e) { return insphere_exact(a, b, c, d, e); }

        [MethodImpl(JitHints.Optimize)]
        static BigInteger[] to_integers(params double[] values)
        {
            return to_integers(values, out int _);
        }

        // the values as integers at the common exponent emin (value = result * 2^emin)
        [MethodImpl(JitHints.Optimize)]
        static BigInteger[] to_integers(double[] values, out int emin)
        {
            int n = values.Length;
            long[] mant = new long[n];
            int[] exp = new int[n];
            emin = int.MaxValue;
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
