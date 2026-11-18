// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Signs of small integer expressions on BigInteger operands, evaluated on stack limbs with FixedArith instead of
    /// BigInteger temporaries: the plane side A X + B Y + C Z - D W (and with W = 1), the dot of two integer vectors and
    /// the 4 x 4 determinant of four homogeneous points (the exact stages of ExactMeshBoolean's classification), and
    /// the planar orientation and in-circle determinants of homogeneous points (the last stages of
    /// ExactMeshArrangement.Orient and InCircle, for points beyond the fixed width). The
    /// operands are copied into sign-magnitude limbs per call (no allocation), the arithmetic is the library's
    /// FixedArith, so the value is the same integer and the sign the same as the BigInteger expression's.
    /// A call whose stack need is above a limit uses the BigInteger expression: for the sides and the dot the sum of
    /// the operands' limbs above MaxOperandLimbs (one wide operand beside small ones stays on the stack), for the
    /// three determinants the operands' limbs and the scratch space above MaxDeterminantLimbs; so does netstandard2.0
    /// (no BigInteger.TryWriteBytes). The limits bound the stack of one call, which calls nothing but FixedArith: 13 KB
    /// for a side or a dot, 36 KB for a determinant (docs/design/EXACT_LIMB_SIGNS.md, which derives the scratch space).
    /// </summary>
    internal static unsafe class ExactLimbSigns
    {
        /// <summary> the sides and the dot run on the stack while their operands take at most this many 64-bit limbs in all </summary>
        internal const int MaxOperandLimbs = 320;
        /// <summary> a determinant (Det4, Orient2, InCircle) runs on the stack while its operands and its scratch space take at most this many limbs (36 KB) </summary>
        internal const int MaxDeterminantLimbs = 4608;

        /// <summary> the scratch limbs of a side or a dot whose operands take `total` limbs (the need is at most 4 total + 6) </summary>
        internal static int SideScratch(int total) { return 4 * total + 16; }

        /// <summary>
        /// the scratch limbs of the determinant for rows whose widest entries take a, b, c, d limbs (the need is at most
        /// 7 a + 27 b + 63 (c + d) + 71: the expansion is along the first row, so the minors are built from the others)
        /// </summary>
        internal static long DeterminantScratch(int a, int b, int c, int d) { return 8L * a + 28L * b + 64L * ((long)c + d) + 96; }

#if NET
        // the limbs v needs: its two's complement bytes (one bit more than its magnitude), rounded up
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int limbs_of(BigInteger v) { return (v.GetByteCount(false) + 7) >> 3; }

        // the magnitude of v in dst (n limbs, n >= limbs_of(v)) and its sign
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FixedInt load(BigInteger v, ulong* dst, int n)
        {
            int sign = v.Sign;
            if (sign == 0)
                return new FixedInt(null, 0, false);
            var bytes = new Span<byte>(dst, 8 * n);
            // (two's complement, sign-extended to n limbs)
            bytes.Fill(sign < 0 ? (byte)0xFF : (byte)0);
            int written;
            if (v.TryWriteBytes(bytes, out written, false, false) == false)
                throw new InvalidOperationException("ExactLimbSigns: an operand wider than its buffer");
            if (sign < 0) {
                // the magnitude: invert and add one
                ulong carry = 1;
                for (int k = 0; k < n; ++k) {
                    ulong t = ~dst[k] + carry;
                    carry = (carry != 0 && t == 0) ? 1UL : 0UL;
                    dst[k] = t;
                }
            }
            int len = n;
            while (len > 0 && dst[len - 1] == 0) len--;
            return new FixedInt(dst, len, sign < 0);
        }
#endif

        /// <summary> the sign of a x + b y + c z - d w </summary>
        internal static int PlaneSide(BigInteger a, BigInteger b, BigInteger c, BigInteger d, BigInteger x, BigInteger y, BigInteger z, BigInteger w)
        {
            int used;
            return PlaneSide(a, b, c, d, x, y, z, w, out used);
        }

        /// <summary> PlaneSide, and the scratch limbs it used (-1: the BigInteger expression; for the tests) </summary>
#if G3_PREDICATE_TIME
        internal static int PlaneSide(BigInteger a, BigInteger b, BigInteger c, BigInteger d, BigInteger x, BigInteger y, BigInteger z, BigInteger w, out int used) { long tk = PredicateTime.EBegin((int)PredicateStats.Kind.LimbPlaneSide); int wr_ = PlaneSide_t(a, b, c, d, x, y, z, w, out used); PredicateTime.EEnd((int)PredicateStats.Kind.LimbPlaneSide, tk); return wr_; }
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        private static int PlaneSide_t(BigInteger a, BigInteger b, BigInteger c, BigInteger d, BigInteger x, BigInteger y, BigInteger z, BigInteger w, out int used)
#else
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        internal static int PlaneSide(BigInteger a, BigInteger b, BigInteger c, BigInteger d, BigInteger x, BigInteger y, BigInteger z, BigInteger w, out int used)
#endif
        {
#if NET
            int na = limbs_of(a), nb = limbs_of(b), nc = limbs_of(c), nd = limbs_of(d), nx = limbs_of(x), ny = limbs_of(y), nz = limbs_of(z), nw = limbs_of(w);
            int total = na + nb + nc + nd + nx + ny + nz + nw;
            if (total <= MaxOperandLimbs) {
                ulong* buf = stackalloc ulong[total];
                int scratch = SideScratch(total);
                ulong* sc = stackalloc ulong[scratch];
                var ar = new FixedArena(sc, scratch);
                ulong* p = buf;
                FixedInt fa = load(a, p, na); p += na;
                FixedInt fb = load(b, p, nb); p += nb;
                FixedInt fc = load(c, p, nc); p += nc;
                FixedInt fd = load(d, p, nd); p += nd;
                FixedInt fx = load(x, p, nx); p += nx;
                FixedInt fy = load(y, p, ny); p += ny;
                FixedInt fz = load(z, p, nz); p += nz;
                FixedInt fw = load(w, p, nw);
                FixedInt s = FixedArith.Add(FixedArith.Mul(fa, fx, ref ar), FixedArith.Mul(fb, fy, ref ar), ref ar);
                s = FixedArith.Add(s, FixedArith.Mul(fc, fz, ref ar), ref ar);
                int sign = FixedArith.Sub(s, FixedArith.Mul(fd, fw, ref ar), ref ar).Sign;
                used = scratch - ar.Remaining;
                return sign;
            }
#endif
            used = -1;
            return (a * x + b * y + c * z - d * w).Sign;
        }

        /// <summary> the sign of a x + b y + c z - d: the plane side of the integer point (x, y, z), W = 1 </summary>
        internal static int PlaneSide(BigInteger a, BigInteger b, BigInteger c, BigInteger d, BigInteger x, BigInteger y, BigInteger z)
        {
            int used;
            return PlaneSide(a, b, c, d, x, y, z, out used);
        }

        /// <summary> PlaneSide of an integer point, and the scratch limbs it used (-1: the BigInteger expression) </summary>
#if G3_PREDICATE_TIME
        internal static int PlaneSide(BigInteger a, BigInteger b, BigInteger c, BigInteger d, BigInteger x, BigInteger y, BigInteger z, out int used) { long tk = PredicateTime.EBegin((int)PredicateStats.Kind.LimbPlaneSide); int wr_ = PlaneSide_t(a, b, c, d, x, y, z, out used); PredicateTime.EEnd((int)PredicateStats.Kind.LimbPlaneSide, tk); return wr_; }
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        private static int PlaneSide_t(BigInteger a, BigInteger b, BigInteger c, BigInteger d, BigInteger x, BigInteger y, BigInteger z, out int used)
#else
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        internal static int PlaneSide(BigInteger a, BigInteger b, BigInteger c, BigInteger d, BigInteger x, BigInteger y, BigInteger z, out int used)
#endif
        {
#if NET
            int na = limbs_of(a), nb = limbs_of(b), nc = limbs_of(c), nd = limbs_of(d), nx = limbs_of(x), ny = limbs_of(y), nz = limbs_of(z);
            int total = na + nb + nc + nd + nx + ny + nz;
            if (total <= MaxOperandLimbs) {
                ulong* buf = stackalloc ulong[total];
                int scratch = SideScratch(total);
                ulong* sc = stackalloc ulong[scratch];
                var ar = new FixedArena(sc, scratch);
                ulong* p = buf;
                FixedInt fa = load(a, p, na); p += na;
                FixedInt fb = load(b, p, nb); p += nb;
                FixedInt fc = load(c, p, nc); p += nc;
                FixedInt fd = load(d, p, nd); p += nd;
                FixedInt fx = load(x, p, nx); p += nx;
                FixedInt fy = load(y, p, ny); p += ny;
                FixedInt fz = load(z, p, nz);
                FixedInt s = FixedArith.Add(FixedArith.Mul(fa, fx, ref ar), FixedArith.Mul(fb, fy, ref ar), ref ar);
                s = FixedArith.Add(s, FixedArith.Mul(fc, fz, ref ar), ref ar);
                int sign = FixedArith.Sub(s, fd, ref ar).Sign;
                used = scratch - ar.Remaining;
                return sign;
            }
#endif
            used = -1;
            return (a * x + b * y + c * z - d).Sign;
        }

        /// <summary> the sign of a x + b y + c z </summary>
        internal static int Dot(BigInteger a, BigInteger b, BigInteger c, BigInteger x, BigInteger y, BigInteger z)
        {
            int used;
            return Dot(a, b, c, x, y, z, out used);
        }

        /// <summary> Dot, and the scratch limbs it used (-1: the BigInteger expression) </summary>
#if G3_PREDICATE_TIME
        internal static int Dot(BigInteger a, BigInteger b, BigInteger c, BigInteger x, BigInteger y, BigInteger z, out int used) { long tk = PredicateTime.EBegin((int)PredicateStats.Kind.LimbOther); int wr_ = Dot_t(a, b, c, x, y, z, out used); PredicateTime.EEnd((int)PredicateStats.Kind.LimbOther, tk); return wr_; }
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        private static int Dot_t(BigInteger a, BigInteger b, BigInteger c, BigInteger x, BigInteger y, BigInteger z, out int used)
#else
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        internal static int Dot(BigInteger a, BigInteger b, BigInteger c, BigInteger x, BigInteger y, BigInteger z, out int used)
#endif
        {
#if NET
            int na = limbs_of(a), nb = limbs_of(b), nc = limbs_of(c), nx = limbs_of(x), ny = limbs_of(y), nz = limbs_of(z);
            int total = na + nb + nc + nx + ny + nz;
            if (total <= MaxOperandLimbs) {
                ulong* buf = stackalloc ulong[total];
                int scratch = SideScratch(total);
                ulong* sc = stackalloc ulong[scratch];
                var ar = new FixedArena(sc, scratch);
                ulong* p = buf;
                FixedInt fa = load(a, p, na); p += na;
                FixedInt fb = load(b, p, nb); p += nb;
                FixedInt fc = load(c, p, nc); p += nc;
                FixedInt fx = load(x, p, nx); p += nx;
                FixedInt fy = load(y, p, ny); p += ny;
                FixedInt fz = load(z, p, nz);
                FixedInt s = FixedArith.Add(FixedArith.Mul(fa, fx, ref ar), FixedArith.Mul(fb, fy, ref ar), ref ar);
                int sign = FixedArith.Add(s, FixedArith.Mul(fc, fz, ref ar), ref ar).Sign;
                used = scratch - ar.Remaining;
                return sign;
            }
#endif
            used = -1;
            return (a * x + b * y + c * z).Sign;
        }

#if NET
        // a (e i - f h) - b (d i - f g) + c (d h - e g), as ExactPoint3.Det3
        static FixedInt det3(FixedInt a, FixedInt b, FixedInt c, FixedInt d, FixedInt e, FixedInt f, FixedInt g, FixedInt h, FixedInt i, ref FixedArena ar)
        {
            FixedInt t1 = FixedArith.Mul(a, FixedArith.Sub(FixedArith.Mul(e, i, ref ar), FixedArith.Mul(f, h, ref ar), ref ar), ref ar);
            FixedInt t2 = FixedArith.Mul(b, FixedArith.Sub(FixedArith.Mul(d, i, ref ar), FixedArith.Mul(f, g, ref ar), ref ar), ref ar);
            FixedInt t3 = FixedArith.Mul(c, FixedArith.Sub(FixedArith.Mul(d, h, ref ar), FixedArith.Mul(e, g, ref ar), ref ar), ref ar);
            return FixedArith.Add(FixedArith.Sub(t1, t2, ref ar), t3, ref ar);
        }
#endif

        /// <summary>
        /// the sign of the 4 x 4 determinant of the rows (X, Y, Z, W) of a, b, c, d, expanded along the first row as
        /// ExactMeshArrangement.Det4
        /// </summary>
        internal static int Det4(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d)
        {
            int used;
            return Det4(a, b, c, d, out used);
        }

        /// <summary> Det4, and the scratch limbs it used (-1: the BigInteger form) </summary>
#if G3_PREDICATE_TIME
        internal static int Det4(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d, out int used) { long tk = PredicateTime.EBegin((int)PredicateStats.Kind.LimbOther); int r = Det4_t(a, b, c, d, out used); PredicateTime.EEnd((int)PredicateStats.Kind.LimbOther, tk); return wr_; }
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        private static int Det4_t(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d, out int used)
#else
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        internal static int Det4(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d, out int used)
#endif
        {
#if NET
            int* n = stackalloc int[16];
            n[0] = limbs_of(a.X); n[1] = limbs_of(a.Y); n[2] = limbs_of(a.Z); n[3] = limbs_of(a.W);
            n[4] = limbs_of(b.X); n[5] = limbs_of(b.Y); n[6] = limbs_of(b.Z); n[7] = limbs_of(b.W);
            n[8] = limbs_of(c.X); n[9] = limbs_of(c.Y); n[10] = limbs_of(c.Z); n[11] = limbs_of(c.W);
            n[12] = limbs_of(d.X); n[13] = limbs_of(d.Y); n[14] = limbs_of(d.Z); n[15] = limbs_of(d.W);
            int total = 0;
            for (int k = 0; k < 16; ++k) total += n[k];
            // the widest entry of each row
            int wa = Math.Max(Math.Max(n[0], n[1]), Math.Max(n[2], n[3])), wb = Math.Max(Math.Max(n[4], n[5]), Math.Max(n[6], n[7]));
            int wc = Math.Max(Math.Max(n[8], n[9]), Math.Max(n[10], n[11])), wd = Math.Max(Math.Max(n[12], n[13]), Math.Max(n[14], n[15]));
            // (in long: no overflow for operands of any size)
            long need = DeterminantScratch(wa, wb, wc, wd);
            if (total + need <= MaxDeterminantLimbs) {
                int scratch = (int)need;
                ulong* buf = stackalloc ulong[total];
                ulong* sc = stackalloc ulong[scratch];
                var ar = new FixedArena(sc, scratch);
                FixedInt* m = stackalloc FixedInt[16];
                ulong* p = buf;
                m[0] = load(a.X, p, n[0]); p += n[0]; m[1] = load(a.Y, p, n[1]); p += n[1]; m[2] = load(a.Z, p, n[2]); p += n[2]; m[3] = load(a.W, p, n[3]); p += n[3];
                m[4] = load(b.X, p, n[4]); p += n[4]; m[5] = load(b.Y, p, n[5]); p += n[5]; m[6] = load(b.Z, p, n[6]); p += n[6]; m[7] = load(b.W, p, n[7]); p += n[7];
                m[8] = load(c.X, p, n[8]); p += n[8]; m[9] = load(c.Y, p, n[9]); p += n[9]; m[10] = load(c.Z, p, n[10]); p += n[10]; m[11] = load(c.W, p, n[11]); p += n[11];
                m[12] = load(d.X, p, n[12]); p += n[12]; m[13] = load(d.Y, p, n[13]); p += n[13]; m[14] = load(d.Z, p, n[14]); p += n[14]; m[15] = load(d.W, p, n[15]);
                FixedInt det = new FixedInt(null, 0, false);
                for (int col = 0; col < 4; ++col) {
                    int c0 = (col == 0) ? 1 : 0, c1 = (col <= 1) ? 2 : 1, c2 = (col <= 2) ? 3 : 2;
                    FixedInt minor = det3(m[4 + c0], m[4 + c1], m[4 + c2], m[8 + c0], m[8 + c1], m[8 + c2], m[12 + c0], m[12 + c1], m[12 + c2], ref ar);
                    FixedInt term = FixedArith.Mul(m[col], minor, ref ar);
                    det = (col % 2 == 0) ? FixedArith.Add(det, term, ref ar) : FixedArith.Sub(det, term, ref ar);
                }
                used = scratch - ar.Remaining;
                return det.Sign;
            }
#endif
            used = -1;
            return Det4Big(a, b, c, d);
        }

        /// <summary>
        /// the scratch limbs of the planar orientation for rows whose widest entries take a, b, c limbs (the need is at
        /// most 5 a + 14 (b + c) + 11: one 3 x 3 determinant, as a minor of Det4)
        /// </summary>
        internal static long OrientScratch(int a, int b, int c) { return 6L * a + 15L * ((long)b + c) + 32; }

        /// <summary>
        /// the scratch limbs of the in-circle determinant; p, q, r: for each of the three points the limbs of its widest
        /// entry plus those of the fourth point's widest entry (the need is at most 27 p + 45 (q + r) + 99)
        /// </summary>
        internal static long InCircleScratch(long p, long q, long r) { return 28L * p + 46L * (q + r) + 128; }

        /// <summary>
        /// The sign of the determinant with the rows (v[i], v[j], v.W) of a, b, c: the orientation of the three points
        /// in the plane of the axes i, j (W > 0).
        /// </summary>
        internal static int Orient2(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int i, int j)
        {
            int used;
            return Orient2(a, b, c, i, j, out used);
        }

        /// <summary> Orient2, and the scratch limbs it used (-1: the BigInteger form) </summary>
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        internal static int Orient2(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int i, int j, out int used)
        {
#if NET
            BigInteger ai = a[i], aj = a[j], bi = b[i], bj = b[j], ci = c[i], cj = c[j];
            int* n = stackalloc int[9];
            n[0] = limbs_of(ai); n[1] = limbs_of(aj); n[2] = limbs_of(a.W);
            n[3] = limbs_of(bi); n[4] = limbs_of(bj); n[5] = limbs_of(b.W);
            n[6] = limbs_of(ci); n[7] = limbs_of(cj); n[8] = limbs_of(c.W);
            // (the sizes in long: a BigInteger has fewer than 2^26 limbs, so neither sum overflows)
            long total = 0;
            for (int k = 0; k < 9; ++k) total += n[k];
            long need = OrientScratch(Math.Max(n[0], Math.Max(n[1], n[2])), Math.Max(n[3], Math.Max(n[4], n[5])), Math.Max(n[6], Math.Max(n[7], n[8])));
            if (total + need <= MaxDeterminantLimbs) {
                int scratch = (int)need;
                ulong* buf = stackalloc ulong[(int)total];
                ulong* sc = stackalloc ulong[scratch];
                var ar = new FixedArena(sc, scratch);
                ulong* p = buf;
                FixedInt f0 = load(ai, p, n[0]); p += n[0];
                FixedInt f1 = load(aj, p, n[1]); p += n[1];
                FixedInt f2 = load(a.W, p, n[2]); p += n[2];
                FixedInt f3 = load(bi, p, n[3]); p += n[3];
                FixedInt f4 = load(bj, p, n[4]); p += n[4];
                FixedInt f5 = load(b.W, p, n[5]); p += n[5];
                FixedInt f6 = load(ci, p, n[6]); p += n[6];
                FixedInt f7 = load(cj, p, n[7]); p += n[7];
                FixedInt f8 = load(c.W, p, n[8]);
                int sign = det3(f0, f1, f2, f3, f4, f5, f6, f7, f8, ref ar).Sign;
                used = scratch - ar.Remaining;
                return sign;
            }
#endif
            used = -1;
            return Orient2Big(a, b, c, i, j);
        }

        /// <summary> the BigInteger form of Orient2 (wide operands, netstandard2.0, and the tests' reference) </summary>
        internal static int Orient2Big(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int i, int j)
        {
            return ExactPoint3.Det3(a[i], a[j], a.W, b[i], b[j], b.W, c[i], c[j], c.W).Sign;
        }

#if NET
        // the in-circle row of v against d: (A s, B s, A^2 + B^2) with A = v_i d_w - d_i v_w, B likewise, s = v_w d_w
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void incircle_row(FixedInt vi, FixedInt vj, FixedInt vw, FixedInt di, FixedInt dj, FixedInt dw, ref FixedArena ar, out FixedInt x, out FixedInt y, out FixedInt l)
        {
            FixedInt A = FixedArith.Sub(FixedArith.Mul(vi, dw, ref ar), FixedArith.Mul(di, vw, ref ar), ref ar);
            FixedInt B = FixedArith.Sub(FixedArith.Mul(vj, dw, ref ar), FixedArith.Mul(dj, vw, ref ar), ref ar);
            FixedInt s = FixedArith.Mul(vw, dw, ref ar);
            x = FixedArith.Mul(A, s, ref ar);
            y = FixedArith.Mul(B, s, ref ar);
            l = FixedArith.Add(FixedArith.Mul(A, A, ref ar), FixedArith.Mul(B, B, ref ar), ref ar);
        }
#endif

        /// <summary>
        /// The sign of the in-circle determinant of p, q, r against d in the plane of the axes i, j: the 3 x 3
        /// determinant with a row (A s, B s, A^2 + B^2) per point v of p, q, r, A = v[i] d.W - d[i] v.W, B likewise
        /// with j, s = v.W d.W (ExactMeshArrangement.InCircle derives it; W > 0).
        /// </summary>
        internal static int InCircle(ExactPoint3 p, ExactPoint3 q, ExactPoint3 r, ExactPoint3 d, int i, int j)
        {
            int used;
            return InCircle(p, q, r, d, i, j, out used);
        }

        /// <summary> InCircle, and the scratch limbs it used (-1: the BigInteger form) </summary>
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        internal static int InCircle(ExactPoint3 p, ExactPoint3 q, ExactPoint3 r, ExactPoint3 d, int i, int j, out int used)
        {
#if NET
            BigInteger pi = p[i], pj = p[j], qi = q[i], qj = q[j], ri = r[i], rj = r[j], di = d[i], dj = d[j];
            int* n = stackalloc int[12];
            n[0] = limbs_of(pi); n[1] = limbs_of(pj); n[2] = limbs_of(p.W);
            n[3] = limbs_of(qi); n[4] = limbs_of(qj); n[5] = limbs_of(q.W);
            n[6] = limbs_of(ri); n[7] = limbs_of(rj); n[8] = limbs_of(r.W);
            n[9] = limbs_of(di); n[10] = limbs_of(dj); n[11] = limbs_of(d.W);
            // (the sizes in long, as in Orient2)
            long total = 0;
            for (int k = 0; k < 12; ++k) total += n[k];
            // the widest entry of the fourth point, and with it of each of the three
            long wd = Math.Max(n[9], Math.Max(n[10], n[11]));
            long need = InCircleScratch(Math.Max(n[0], Math.Max(n[1], n[2])) + wd, Math.Max(n[3], Math.Max(n[4], n[5])) + wd, Math.Max(n[6], Math.Max(n[7], n[8])) + wd);
            if (total + need <= MaxDeterminantLimbs) {
                int scratch = (int)need;
                ulong* buf = stackalloc ulong[(int)total];
                ulong* sc = stackalloc ulong[scratch];
                var ar = new FixedArena(sc, scratch);
                ulong* b = buf;
                FixedInt f0 = load(pi, b, n[0]); b += n[0];
                FixedInt f1 = load(pj, b, n[1]); b += n[1];
                FixedInt f2 = load(p.W, b, n[2]); b += n[2];
                FixedInt f3 = load(qi, b, n[3]); b += n[3];
                FixedInt f4 = load(qj, b, n[4]); b += n[4];
                FixedInt f5 = load(q.W, b, n[5]); b += n[5];
                FixedInt f6 = load(ri, b, n[6]); b += n[6];
                FixedInt f7 = load(rj, b, n[7]); b += n[7];
                FixedInt f8 = load(r.W, b, n[8]); b += n[8];
                FixedInt f9 = load(di, b, n[9]); b += n[9];
                FixedInt f10 = load(dj, b, n[10]); b += n[10];
                FixedInt f11 = load(d.W, b, n[11]);
                FixedInt ax, ay, al, bx, by, bl, cx, cy, cl;
                incircle_row(f0, f1, f2, f9, f10, f11, ref ar, out ax, out ay, out al);
                incircle_row(f3, f4, f5, f9, f10, f11, ref ar, out bx, out by, out bl);
                incircle_row(f6, f7, f8, f9, f10, f11, ref ar, out cx, out cy, out cl);
                int sign = det3(ax, ay, al, bx, by, bl, cx, cy, cl, ref ar).Sign;
                used = scratch - ar.Remaining;
                return sign;
            }
#endif
            used = -1;
            return InCircleBig(p, q, r, d, i, j);
        }

        /// <summary> the BigInteger form of InCircle (wide operands, netstandard2.0, and the tests' reference) </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static int InCircleBig(ExactPoint3 p, ExactPoint3 q, ExactPoint3 r, ExactPoint3 d, int i, int j)
        {
            BigInteger ax, ay, al, bx, by, bl, cx, cy, cl;
            incircle_row_big(p, d, i, j, out ax, out ay, out al);
            incircle_row_big(q, d, i, j, out bx, out by, out bl);
            incircle_row_big(r, d, i, j, out cx, out cy, out cl);
            return ExactPoint3.Det3(ax, ay, al, bx, by, bl, cx, cy, cl).Sign;
        }

        // the row of incircle_row in BigIntegers; with both W = 1 the factor s is 1
        [MethodImpl(JitHints.Optimize)]
        static void incircle_row_big(ExactPoint3 v, ExactPoint3 d, int i, int j, out BigInteger x, out BigInteger y, out BigInteger l)
        {
            BigInteger A, B, s;
            if (v.W.IsOne && d.W.IsOne) {
                A = v[i] - d[i]; B = v[j] - d[j];
                x = A; y = B;
            } else {
                A = v[i] * d.W - d[i] * v.W; B = v[j] * d.W - d[j] * v.W; s = v.W * d.W;
                x = A * s; y = B * s;
            }
            l = A * A + B * B;
        }

        /// <summary> the BigInteger form of Det4 (wide operands, netstandard2.0, and the tests' reference) </summary>
        internal static int Det4Big(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d)
        {
            BigInteger[,] m = new BigInteger[4, 4];
            m[0, 0] = a.X; m[0, 1] = a.Y; m[0, 2] = a.Z; m[0, 3] = a.W;
            m[1, 0] = b.X; m[1, 1] = b.Y; m[1, 2] = b.Z; m[1, 3] = b.W;
            m[2, 0] = c.X; m[2, 1] = c.Y; m[2, 2] = c.Z; m[2, 3] = c.W;
            m[3, 0] = d.X; m[3, 1] = d.Y; m[3, 2] = d.Z; m[3, 3] = d.W;
            return ExactMeshArrangement.Det4(m).Sign;
        }
    }
}
