// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// A canonical ExactPoint3 in fixed width, for the exact points of ExactMeshArrangement
    /// (docs/design/FIXED_WIDTH_EXACT_POINTS.md): X, Y, Z, W as sign and magnitude, the magnitudes in four 64-bit
    /// limbs each. Admitted iff every component has |v| &lt;= 2^255 - 1. No reference fields, so arrays of it are not
    /// traced by the GC: the point costs no objects, unlike four BigIntegers with their digit arrays.
    /// The representation is a function of the value (unused limbs are 0), so equal points have equal limbs.
    /// </summary>
    internal unsafe struct FixedPoint3
    {
        public const int Limbs = 4;
        /// <summary> magnitude of component k (X, Y, Z, W) in L[4k .. 4k + 3], little-endian; unused limbs are 0 </summary>
        public fixed ulong L[16];
        /// <summary> component k: the number of used limbs (0..4, 0 iff the component is 0) in bits 0-2, 0x80 if negative </summary>
        public fixed byte M[4];
        /// <summary> 1: an integer point whose coordinates are doubles, D exactly (as ExactMeshArrangement.ExactBox) </summary>
        public byte Flags;
        public Vector3d D;

        public bool IsDouble { get { return (Flags & 1) != 0; } }
        public bool WIsOne { get { return M[3] == 1 && L[12] == 1; } }

        /// <summary> p in fixed width, false if a component is beyond 2^255 - 1 in magnitude </summary>
        [MethodImpl(JitHints.Optimize)]
        public static bool TryCreate(ExactPoint3 p, out FixedPoint3 f)
        {
            if (TryCreateLimbs(p, out f) == false)
                return false;
            f.SetDouble(p);
            return true;
        }

        /// <summary> TryCreate without IsDouble and D (for lookups: SameValue and Hash read only the limbs); SetDouble adds them </summary>
        [MethodImpl(JitHints.Optimize)]
        public static bool TryCreateLimbs(ExactPoint3 p, out FixedPoint3 f)
        {
            f = default(FixedPoint3);
            fixed (ulong* l = f.L)
            fixed (byte* m = f.M) {
                if (Load(p.X, l, out m[0]) == false || Load(p.Y, l + 4, out m[1]) == false
                    || Load(p.Z, l + 8, out m[2]) == false || Load(p.W, l + 12, out m[3]) == false)
                    return false;
            }
            return true;
        }

        /// <summary> IsDouble and D of the point p that these limbs hold </summary>
        public void SetDouble(ExactPoint3 p)
        {
            Vector3d d;
            if (DoubleOf(p, out d)) {
                Flags = 1;
                D = d;
            }
        }

        /// <summary> the point is an integer point whose coordinates are doubles (d, exactly) </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static bool DoubleOf(ExactPoint3 p, out Vector3d d)
        {
            d = Vector3d.Zero;
            if (p.W.IsOne == false)
                return false;
            // (exact iff the conversion goes back to the same integer, whatever its rounding)
            double x = (double)p.X, y = (double)p.Y, z = (double)p.Z;
            if (exact_integer(x, p.X) && exact_integer(y, p.Y) && exact_integer(z, p.Z)) {
                d = new Vector3d(x, y, z);
                return true;
            }
            return false;
        }
        static bool exact_integer(double d, BigInteger v)
        {
            return double.IsInfinity(d) == false && double.IsNaN(d) == false && new BigInteger(d) == v;
        }

        /// <summary> the magnitude of v into 4 limbs at dst (which must be 0) and its length / sign byte; false if |v| &gt; 2^255 - 1 </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static bool Load(BigInteger v, ulong* dst, out byte m)
        {
            int len;
            bool neg;
            m = 0;
            if (LoadN(v, dst, 4, out len, out neg) == false)
                return false;
            m = (byte)(len | (neg ? 0x80 : 0));
            return true;
        }

        /// <summary> the magnitude of v into `limbs` limbs at dst (1..8; unused limbs are not written); false if |v| &gt; 2^(64 limbs - 1) - 1 </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static bool LoadN(BigInteger v, ulong* dst, int limbs, out int len, out bool neg)
        {
            len = 0;
            neg = false;
            if (v.IsZero)
                return true;
            // little-endian two's complement into limbs + 1 limbs
            ulong* t = stackalloc ulong[9];
            byte* b = (byte*)t;
            int size = 8 * (limbs + 1);
#if NET
            int n;
            if (v.TryWriteBytes(new Span<byte>(b, size), out n, false, false) == false)
                return false;
#else
            byte[] bytes = v.ToByteArray();
            int n = bytes.Length;
            if (n > size)
                return false;
            for (int k = 0; k < n; ++k) b[k] = bytes[k];
#endif
            return FromTwos(b, n, size, v.Sign < 0, dst, limbs, out len, out neg);
        }

        /// <summary> Load without the .NET API (netstandard2.0; compiled everywhere, so that the tests on .NET check it) </summary>
        internal static bool LoadPortable(BigInteger v, ulong* dst, out byte m)
        {
            m = 0;
            if (v.IsZero)
                return true;
            byte[] bytes = v.ToByteArray();
            int n = bytes.Length;
            if (n > 40)
                return false;
            ulong* t = stackalloc ulong[5];
            byte* b = (byte*)t;
            for (int k = 0; k < n; ++k) b[k] = bytes[k];
            int len;
            bool neg;
            if (FromTwos(b, n, 40, v.Sign < 0, dst, 4, out len, out neg) == false)
                return false;
            m = (byte)(len | (neg ? 0x80 : 0));
            return true;
        }

        // the n bytes at b (little-endian two's complement of a nonzero value, n <= size = 8 (limbs + 1), in a buffer of
        // size bytes) as magnitude limbs
        static bool FromTwos(byte* b, int n, int size, bool neg, ulong* dst, int limbs, out int len, out bool isNeg)
        {
            len = 0;
            isNeg = neg;
            byte fill = neg ? (byte)0xFF : (byte)0;
            for (int k = n; k < size; ++k) b[k] = fill;
            ulong* t = (ulong*)b;
            if (neg) {
                // magnitude = ~t + 1 (t != 0)
                ulong carry = 1;
                for (int k = 0; k <= limbs; ++k) {
                    ulong s = ~t[k] + carry;
                    carry = (carry != 0 && s == 0) ? 1UL : 0UL;
                    t[k] = s;
                }
            }
            // |v| <= 2^(64 limbs - 1) - 1: the top limb and the top bit of the one below are 0
            if (t[limbs] != 0 || (t[limbs - 1] >> 63) != 0)
                return false;
            len = limbs;
            while (len > 0 && t[len - 1] == 0) len--;
            for (int k = 0; k < len; ++k) dst[k] = t[k];
            return true;
        }

        /// <summary> the point as an ExactPoint3 (canonical, since only canonical points are stored) </summary>
        [MethodImpl(JitHints.Optimize)]
        public ExactPoint3 ToExact()
        {
            fixed (ulong* l = L)
            fixed (byte* m = M)
                return ExactPoint3.CreateUnreduced(ToBig(l, m[0]), ToBig(l + 4, m[1]), ToBig(l + 8, m[2]), ToBig(l + 12, m[3]));
        }

        [MethodImpl(JitHints.Optimize)]
        internal static BigInteger ToBig(ulong* l, byte m)
        {
            int len = m & 7;
            BigInteger v;
            if (len == 0)
                return BigInteger.Zero;
            if (len == 1)
                v = new BigInteger(l[0]);
            else {
#if NET
                v = new BigInteger(new ReadOnlySpan<byte>(l, 8 * len), true, false);
#else
                v = ToBigPortable(l, len);
#endif
            }
            return ((m & 0x80) != 0) ? -v : v;
        }

        /// <summary> the magnitude l[0 .. len - 1] as BigInteger without the .NET API (netstandard2.0; tested everywhere) </summary>
        internal static BigInteger ToBigPortable(ulong* l, int len)
        {
            byte[] bytes = new byte[8 * len + 1];     // with a 0 byte on top: positive
            byte* b = (byte*)l;
            for (int k = 0; k < 8 * len; ++k) bytes[k] = b[k];
            return new BigInteger(bytes);
        }

        /// <summary> the same point (equal limbs and signs: the representation is unique) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static bool SameValue(ref FixedPoint3 a, ref FixedPoint3 b)
        {
            for (int k = 0; k < 4; ++k)
                if (a.M[k] != b.M[k]) return false;
            for (int k = 0; k < 16; ++k)
                if (a.L[k] != b.L[k]) return false;
            return true;
        }

        [MethodImpl(JitHints.Optimize)]
        public int Hash()
        {
            unchecked {
                ulong h = 0x9E3779B97F4A7C15UL;
                for (int k = 0; k < 16; ++k)
                    h = (h ^ L[k]) * 0x100000001B3UL;
                for (int k = 0; k < 4; ++k)
                    h = (h ^ M[k]) * 0x100000001B3UL;
                // (a final mix, so that the low bits, which pick the index shard, depend on all bits of all limbs)
                h ^= h >> 33;
                h *= 0xFF51AFD7ED558CCDUL;
                h ^= h >> 33;
                return (int)(h ^ (h >> 32));
            }
        }

        /// <summary> component k (0..3) as a number in fixed-width arithmetic, its limbs copied to dst (4 limbs) </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedInt Get(int k, ulong* dst)
        {
            byte m = M[k];
            int len = m & 7;
            for (int i = 0; i < len; ++i) dst[i] = L[4 * k + i];
            return new FixedInt(dst, len, (m & 0x80) != 0);
        }
    }


    /// <summary> a signed integer as sign and magnitude: Len used limbs at P (little-endian, P[Len - 1] != 0; 0 iff Len = 0) </summary>
    internal unsafe struct FixedInt
    {
        public readonly ulong* P;
        public readonly int Len;
        public readonly bool Neg;
        public FixedInt(ulong* p, int len, bool neg) { P = p; Len = len; Neg = neg && len > 0; }
        public int Sign { get { return (Len == 0) ? 0 : Neg ? -1 : 1; } }
        public FixedInt Negated { get { return new FixedInt(P, Len, !Neg); } }
    }

    /// <summary> scratch space on the stack for FixedArith: results are taken from it in order (no reuse) </summary>
    internal unsafe struct FixedArena
    {
        ulong* cur, end;
        public FixedArena(ulong* buf, int limbs) { cur = buf; end = buf + limbs; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong* Take(int n)
        {
            ulong* p = cur;
            if (n > end - cur)
                throw new InvalidOperationException("FixedArith: scratch space exhausted");
            cur += n;
            return p;
        }
    }

    /// <summary>
    /// Exact integer arithmetic on FixedInt for the predicates of ExactMeshArrangement on fixed-width points. Every
    /// result gets the limbs that its operands' lengths allow (a product la + lb, a sum max(la, lb) + 1), so no
    /// operation can overflow; the buffers come from a FixedArena on the caller's stack. No allocation.
    /// </summary>
    internal static unsafe class FixedArith
    {
        /// <summary> the 128-bit product a b: high limb returned, low limb in lo </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong MulHigh(ulong a, ulong b, out ulong lo)
        {
#if NET
            return Math.BigMul(a, b, out lo);
#else
            return MulHighPortable(a, b, out lo);
#endif
        }

        /// <summary> MulHigh from 32-bit halves (netstandard2.0; compiled everywhere, so that the tests on .NET check it) </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong MulHighPortable(ulong a, ulong b, out ulong lo)
        {
            ulong a0 = (uint)a, a1 = a >> 32, b0 = (uint)b, b1 = b >> 32;
            ulong p00 = a0 * b0, p01 = a0 * b1, p10 = a1 * b0, p11 = a1 * b1;
            // middle column: at most 3 (2^32 - 1) < 2^34, no overflow
            ulong mid = (p00 >> 32) + (uint)p01 + (uint)p10;
            lo = (mid << 32) | (uint)p00;
            return p11 + (p01 >> 32) + (p10 >> 32) + (mid >> 32);
        }

        [MethodImpl(JitHints.Optimize)]
        public static FixedInt Mul(FixedInt a, FixedInt b, ref FixedArena ar)
        {
            if (a.Len == 0 || b.Len == 0)
                return new FixedInt(null, 0, false);
            int n = a.Len + b.Len;
            ulong* r = ar.Take(n);
            for (int k = 0; k < n; ++k) r[k] = 0;
            for (int i = 0; i < a.Len; ++i) {
                ulong ai = a.P[i], carry = 0;
                for (int j = 0; j < b.Len; ++j) {
                    ulong lo, hi = MulHigh(ai, b.P[j], out lo);
                    // r[i + j] + lo + carry < 2^128: hi absorbs both carries (hi <= 2^64 - 2)
                    ulong s = r[i + j] + lo;
                    hi += (s < lo) ? 1UL : 0UL;
                    ulong s2 = s + carry;
                    hi += (s2 < carry) ? 1UL : 0UL;
                    r[i + j] = s2;
                    carry = hi;
                }
                r[i + b.Len] = carry;
            }
            while (n > 0 && r[n - 1] == 0) n--;
            return new FixedInt(r, n, a.Neg != b.Neg);
        }

        public static FixedInt Sub(FixedInt a, FixedInt b, ref FixedArena ar)
        {
            return Add(a, b.Negated, ref ar);
        }

        [MethodImpl(JitHints.Optimize)]
        public static FixedInt Add(FixedInt a, FixedInt b, ref FixedArena ar)
        {
            if (b.Len == 0) return a;
            if (a.Len == 0) return b;
            if (a.Neg == b.Neg)
                return add_mag(a, b, a.Neg, ref ar);
            int c = cmp_mag(a, b);
            if (c == 0)
                return new FixedInt(null, 0, false);
            return (c > 0) ? sub_mag(a, b, a.Neg, ref ar) : sub_mag(b, a, b.Neg, ref ar);
        }

        // |a| + |b|
        static FixedInt add_mag(FixedInt a, FixedInt b, bool neg, ref FixedArena ar)
        {
            if (a.Len < b.Len) { FixedInt t = a; a = b; b = t; }
            ulong* r = ar.Take(a.Len + 1);
            ulong carry = 0;
            for (int k = 0; k < a.Len; ++k) {
                ulong x = a.P[k], y = (k < b.Len) ? b.P[k] : 0;
                ulong s = x + y;
                ulong c1 = (s < x) ? 1UL : 0UL;
                ulong s2 = s + carry;
                carry = c1 + ((s2 < carry) ? 1UL : 0UL);
                r[k] = s2;
            }
            r[a.Len] = carry;
            return new FixedInt(r, a.Len + (carry != 0 ? 1 : 0), neg);
        }

        // |a| - |b| for |a| > |b|
        static FixedInt sub_mag(FixedInt a, FixedInt b, bool neg, ref FixedArena ar)
        {
            ulong* r = ar.Take(a.Len);
            ulong borrow = 0;
            for (int k = 0; k < a.Len; ++k) {
                ulong x = a.P[k], y = (k < b.Len) ? b.P[k] : 0;
                ulong d = x - y;
                ulong b1 = (x < y) ? 1UL : 0UL;
                ulong d2 = d - borrow;
                borrow = b1 + ((d < borrow) ? 1UL : 0UL);
                r[k] = d2;
            }
            if (borrow != 0)
                throw new InvalidOperationException("FixedArith: subtraction of a larger magnitude");
            int n = a.Len;
            while (n > 0 && r[n - 1] == 0) n--;
            return new FixedInt(r, n, neg);
        }

        // compare |a| and |b|
        [MethodImpl(JitHints.Optimize)]
        static int cmp_mag(FixedInt a, FixedInt b)
        {
            if (a.Len != b.Len) return (a.Len > b.Len) ? 1 : -1;
            for (int k = a.Len - 1; k >= 0; --k)
                if (a.P[k] != b.P[k]) return (a.P[k] > b.P[k]) ? 1 : -1;
            return 0;
        }

        /// <summary> sign of a - b </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int Compare(FixedInt a, FixedInt b)
        {
            int sa = a.Sign, sb = b.Sign;
            if (sa != sb) return (sa > sb) ? 1 : -1;
            if (sa == 0) return 0;
            int c = cmp_mag(a, b);
            return (sa > 0) ? c : -c;
        }

        /// <summary> sign of the 3x3 determinant a (e i - f h) - b (d i - f g) + c (d h - e g), as ExactPoint3.Det3 </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int Det3Sign(FixedInt a, FixedInt b, FixedInt c, FixedInt d, FixedInt e, FixedInt f,
                                   FixedInt g, FixedInt h, FixedInt i, ref FixedArena ar)
        {
            FixedInt t1 = Mul(a, Sub(Mul(e, i, ref ar), Mul(f, h, ref ar), ref ar), ref ar);
            FixedInt t2 = Mul(b, Sub(Mul(d, i, ref ar), Mul(f, g, ref ar), ref ar), ref ar);
            FixedInt t3 = Mul(c, Sub(Mul(d, h, ref ar), Mul(e, g, ref ar), ref ar), ref ar);
            return Add(Sub(t1, t2, ref ar), t3, ref ar).Sign;
        }

        // ---- the predicates of ExactMeshArrangement on fixed-width points (each the same determinant as its
        // BigInteger code, so the same sign) ----

        // Scratch sizes (each operation takes the sum of its operands' limbs, a sum one more than the larger; the
        // components have at most 4 limbs). Orient: 6 products of 8, 3 minors of 9, 3 terms of 13, sums of 14 and 15:
        // 143. InCircle, per row (general case; the W = 1 case takes less): 4 products of 8, A and B of 9, s of 8, A s
        // and B s of 17, A^2 and B^2 of 18, l of 19: 147, three rows 441; the determinant of entries with at most 19
        // limbs: 6 products of 38, 3 minors of 39, 3 terms of 58, sums of 59 and 60: 637; together 1078.
        // ComparePoints: per side 3 products of 5, sums of 6 and 7, times W 11: 39; the coordinates 6 products of 8:
        // 48; together 126. (Exceeding them throws: FixedArena.Take.)
        const int OrientScratch = 144;
        const int InCircleScratch = 1080;
        const int CompareScratch = 128;
        // CompareAlong (direction components of at most 8 limbs): per side 3 products of 12, sums of 13 and 14, times W
        // 18: 81; together 162.
        const int AlongScratch = 168;

        /// <summary> sign of Det3 of the rows (x_i, x_j, W) of a, b, c (ExactMeshArrangement.Orient) </summary>
#if NET
        [SkipLocalsInit]
#endif
        [MethodImpl(JitHints.Optimize)]
        public static int Orient(ref FixedPoint3 a, ref FixedPoint3 b, ref FixedPoint3 c, int i, int j)
        {
            ulong* v = stackalloc ulong[36];
            ulong* s = stackalloc ulong[OrientScratch];
            var ar = new FixedArena(s, OrientScratch);
            return Det3Sign(a.Get(i, v), a.Get(j, v + 4), a.Get(3, v + 8),
                b.Get(i, v + 12), b.Get(j, v + 16), b.Get(3, v + 20),
                c.Get(i, v + 24), c.Get(j, v + 28), c.Get(3, v + 32), ref ar);
        }

        /// <summary>
        /// sign of the InCircle determinant of ExactMeshArrangement.InCircle: Det3 of the rows (A s, B s, A^2 + B^2)
        /// of a, b, c relative to d, with A = x w_d - x_d w, B likewise in j, s = w w_d (A = x - x_d, s = 1 if both W = 1)
        /// </summary>
#if NET
        [SkipLocalsInit]
#endif
        [MethodImpl(JitHints.Optimize)]
        public static int InCircle(ref FixedPoint3 a, ref FixedPoint3 b, ref FixedPoint3 c, ref FixedPoint3 d, int i, int j)
        {
            ulong* v = stackalloc ulong[48];
            ulong* s = stackalloc ulong[InCircleScratch];
            var ar = new FixedArena(s, InCircleScratch);
            FixedInt dx = d.Get(i, v), dy = d.Get(j, v + 4), dw = d.Get(3, v + 8);
            bool dOne = d.WIsOne;
            FixedInt ax, ay, al, bx, by, bl, cx, cy, cl;
            incircle_row(ref a, dx, dy, dw, dOne, i, j, v + 12, ref ar, out ax, out ay, out al);
            incircle_row(ref b, dx, dy, dw, dOne, i, j, v + 24, ref ar, out bx, out by, out bl);
            incircle_row(ref c, dx, dy, dw, dOne, i, j, v + 36, ref ar, out cx, out cy, out cl);
            return Det3Sign(ax, ay, al, bx, by, bl, cx, cy, cl, ref ar);
        }

        [MethodImpl(JitHints.Optimize)]
        static void incircle_row(ref FixedPoint3 p, FixedInt dx, FixedInt dy, FixedInt dw, bool dOne, int i, int j, ulong* v,
                                 ref FixedArena ar, out FixedInt x, out FixedInt y, out FixedInt l)
        {
            FixedInt px = p.Get(i, v), py = p.Get(j, v + 4), pw = p.Get(3, v + 8);
            FixedInt A, B;
            if (dOne && p.WIsOne) {
                A = Sub(px, dx, ref ar); B = Sub(py, dy, ref ar);
                x = A; y = B;
            } else {
                A = Sub(Mul(px, dw, ref ar), Mul(dx, pw, ref ar), ref ar);
                B = Sub(Mul(py, dw, ref ar), Mul(dy, pw, ref ar), ref ar);
                FixedInt s = Mul(pw, dw, ref ar);
                x = Mul(A, s, ref ar); y = Mul(B, s, ref ar);
            }
            l = Add(Mul(A, A, ref ar), Mul(B, B, ref ar), ref ar);
        }

        /// <summary>
        /// ExactMeshArrangement.compare_along: (d . a) W_b against (d . b) W_a, for a direction d with components of at
        /// most 8 limbs
        /// </summary>
#if NET
        [SkipLocalsInit]
#endif
        [MethodImpl(JitHints.Optimize)]
        public static int CompareAlong(ref FixedPoint3 a, ref FixedPoint3 b, FixedInt dx, FixedInt dy, FixedInt dz)
        {
            ulong* v = stackalloc ulong[32];
            ulong* s = stackalloc ulong[AlongScratch];
            var ar = new FixedArena(s, AlongScratch);
            FixedInt ax = a.Get(0, v), ay = a.Get(1, v + 4), az = a.Get(2, v + 8), aw = a.Get(3, v + 12);
            FixedInt bx = b.Get(0, v + 16), by = b.Get(1, v + 20), bz = b.Get(2, v + 24), bw = b.Get(3, v + 28);
            FixedInt ka = Add(Add(Mul(dx, ax, ref ar), Mul(dy, ay, ref ar), ref ar), Mul(dz, az, ref ar), ref ar);
            FixedInt kb = Add(Add(Mul(dx, bx, ref ar), Mul(dy, by, ref ar), ref ar), Mul(dz, bz, ref ar), ref ar);
            return Compare(Mul(ka, bw, ref ar), Mul(kb, aw, ref ar));
        }

        /// <summary>
        /// ExactMeshArrangement.ComparePoints of two different points: (alpha . a) W_b against (alpha . b) W_a, then
        /// the coordinates x_a W_b against x_b W_a; alpha: three nonnegative one-limb integers
        /// </summary>
#if NET
        [SkipLocalsInit]
#endif
        [MethodImpl(JitHints.Optimize)]
        public static int ComparePoints(ref FixedPoint3 a, ref FixedPoint3 b, ulong alpha0, ulong alpha1, ulong alpha2)
        {
            ulong* v = stackalloc ulong[35];
            ulong* s = stackalloc ulong[CompareScratch];
            var ar = new FixedArena(s, CompareScratch);
            v[32] = alpha0; v[33] = alpha1; v[34] = alpha2;
            FixedInt al0 = new FixedInt(v + 32, alpha0 != 0 ? 1 : 0, false), al1 = new FixedInt(v + 33, alpha1 != 0 ? 1 : 0, false),
                al2 = new FixedInt(v + 34, alpha2 != 0 ? 1 : 0, false);
            FixedInt ax = a.Get(0, v), ay = a.Get(1, v + 4), az = a.Get(2, v + 8), aw = a.Get(3, v + 12);
            FixedInt bx = b.Get(0, v + 16), by = b.Get(1, v + 20), bz = b.Get(2, v + 24), bw = b.Get(3, v + 28);
            FixedInt ka = Add(Add(Mul(al0, ax, ref ar), Mul(al1, ay, ref ar), ref ar), Mul(al2, az, ref ar), ref ar);
            FixedInt kb = Add(Add(Mul(al0, bx, ref ar), Mul(al1, by, ref ar), ref ar), Mul(al2, bz, ref ar), ref ar);
            int c = Compare(Mul(ka, bw, ref ar), Mul(kb, aw, ref ar));
            if (c != 0) return c;
            c = Compare(Mul(ax, bw, ref ar), Mul(bx, aw, ref ar));
            if (c != 0) return c;
            c = Compare(Mul(ay, bw, ref ar), Mul(by, aw, ref ar));
            if (c != 0) return c;
            return Compare(Mul(az, bw, ref ar), Mul(bz, aw, ref ar));
        }

        // ---- the triangle plane and its edge planes (ExactMeshArrangement.InputTriangle.compute_planes; docs/design/
        // CHEAPER_CONSTRUCTIONS.md, section 4.5) ----
        // Scratch by the length rule for inputs of 4 limbs, in this order of operations: 996 limbs (the generator's
        // "lengths only" of the triangle plane with its three edge planes, each edge with its own difference q - p);
        // 1-limb grid inputs take about 120.
        const int PlanesScratch = 1024;

        /// <summary>
        /// The plane N . x = D of the triangle (a, b, c) of integer points (W = 1), N = (b - a) x (c - a), D = N . a, as
        /// the BigInteger code computes it, with the projection axis (the largest |N_k|, the first of equal ones) and
        /// the sign of N on it; approx gets the 16 doubles of N, D and the three edge planes E_k = (q - p) x N,
        /// F_k = E_k . p (p, q = corners k, k + 1), each as (double)BigInteger converts it (truncation to 53 bits)
        /// </summary>
#if NET
        [SkipLocalsInit]
#endif
        [MethodImpl(JitHints.Optimize)]
        public static void TrianglePlanes(ref FixedPoint3 a, ref FixedPoint3 b, ref FixedPoint3 c, double[] approx,
            out BigInteger nx, out BigInteger ny, out BigInteger nz, out BigInteger d, out int axis, out int sign)
        {
            ulong* v = stackalloc ulong[36];
            ulong* s = stackalloc ulong[PlanesScratch];
            var ar = new FixedArena(s, PlanesScratch);
            FixedInt* p = stackalloc FixedInt[9];
            p[0] = a.Get(0, v); p[1] = a.Get(1, v + 4); p[2] = a.Get(2, v + 8);
            p[3] = b.Get(0, v + 12); p[4] = b.Get(1, v + 16); p[5] = b.Get(2, v + 20);
            p[6] = c.Get(0, v + 24); p[7] = c.Get(1, v + 28); p[8] = c.Get(2, v + 32);
            FixedInt ux = Sub(p[3], p[0], ref ar), uy = Sub(p[4], p[1], ref ar), uz = Sub(p[5], p[2], ref ar);
            FixedInt vx = Sub(p[6], p[0], ref ar), vy = Sub(p[7], p[1], ref ar), vz = Sub(p[8], p[2], ref ar);
            FixedInt Nx = Sub(Mul(uy, vz, ref ar), Mul(uz, vy, ref ar), ref ar);
            FixedInt Ny = Sub(Mul(uz, vx, ref ar), Mul(ux, vz, ref ar), ref ar);
            FixedInt Nz = Sub(Mul(ux, vy, ref ar), Mul(uy, vx, ref ar), ref ar);
            FixedInt D = Add(Add(Mul(Nx, p[0], ref ar), Mul(Ny, p[1], ref ar), ref ar), Mul(Nz, p[2], ref ar), ref ar);
            approx[0] = ToDouble(Nx); approx[1] = ToDouble(Ny); approx[2] = ToDouble(Nz); approx[3] = ToDouble(D);
            for (int k = 0; k < 3; ++k) {
                FixedInt* P = p + 3 * k, Q = p + 3 * ((k + 1) % 3);
                FixedInt dx = Sub(Q[0], P[0], ref ar), dy = Sub(Q[1], P[1], ref ar), dz = Sub(Q[2], P[2], ref ar);
                FixedInt ex = Sub(Mul(dy, Nz, ref ar), Mul(dz, Ny, ref ar), ref ar);
                FixedInt ey = Sub(Mul(dz, Nx, ref ar), Mul(dx, Nz, ref ar), ref ar);
                FixedInt ez = Sub(Mul(dx, Ny, ref ar), Mul(dy, Nx, ref ar), ref ar);
                FixedInt f = Add(Add(Mul(ex, P[0], ref ar), Mul(ey, P[1], ref ar), ref ar), Mul(ez, P[2], ref ar), ref ar);
                approx[4 * k + 4] = ToDouble(ex); approx[4 * k + 5] = ToDouble(ey); approx[4 * k + 6] = ToDouble(ez); approx[4 * k + 7] = ToDouble(f);
            }
            axis = (cmp_mag(Nx, Ny) >= 0 && cmp_mag(Nx, Nz) >= 0) ? 0 : (cmp_mag(Ny, Nz) >= 0) ? 1 : 2;
            sign = ((axis == 0) ? Nx : (axis == 1) ? Ny : Nz).Sign;
            nx = ToBig(Nx); ny = ToBig(Ny); nz = ToBig(Nz); d = ToBig(D);
        }

        /// <summary> v as a double as (double)BigInteger converts on .NET: the magnitude truncated to 53 significant
        /// bits (toward zero), infinite beyond 2^1024 </summary>
        [MethodImpl(JitHints.Optimize)]
        public static double ToDouble(FixedInt v)
        {
            if (v.Len == 0)
                return 0;
            ulong hi = v.P[v.Len - 1];
            int lz = leading_zeros(hi);
            long bits = 64L * v.Len - lz;
            double r;
            if (bits <= 53) {
                r = (double)v.P[0];          // (exact: below 2^53)
            } else if (bits > 1024) {
                r = double.PositiveInfinity;
            } else {
                // the top 64 bits, then the top 53 of them (truncated): m 2^(bits - 53) with 2^52 <= m < 2^53
                ulong top = hi << lz;
                if (lz > 0 && v.Len > 1)
                    top |= v.P[v.Len - 2] >> (64 - lz);
                ulong m = top >> 11;
                r = BitConverter.Int64BitsToDouble(((long)(bits - 1 + 1023) << 52) | (long)(m & 0xFFFFFFFFFFFFFUL));
            }
            return v.Neg ? -r : r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int leading_zeros(ulong x)
        {
#if NET
            return System.Numerics.BitOperations.LeadingZeroCount(x);
#else
            return LeadingZerosPortable(x);
#endif
        }

        /// <summary> leading zero bits of x != 0 for netstandard2.0 (compiled everywhere, so that the tests on .NET check it) </summary>
        internal static int LeadingZerosPortable(ulong x)
        {
            int n = 0;
            while ((x & 0x8000000000000000UL) == 0) { x <<= 1; n++; }
            return n;
        }

        /// <summary> v as a BigInteger (any length) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static BigInteger ToBig(FixedInt v)
        {
            if (v.Len == 0)
                return BigInteger.Zero;
            BigInteger r;
            if (v.Len == 1)
                r = new BigInteger(v.P[0]);
            else {
#if NET
                r = new BigInteger(new ReadOnlySpan<byte>(v.P, 8 * v.Len), true, false);
#else
                r = FixedPoint3.ToBigPortable(v.P, v.Len);
#endif
            }
            return v.Neg ? -r : r;
        }
    }


    /// <summary>
    /// Append-only store of FixedPoint3 in chunks that are never moved (256 per chunk: 40 KB, below the large object
    /// heap threshold). Add is thread-safe; an element is read through the slot it returned, after the slot was
    /// published (ExactMeshArrangement publishes it with Volatile.Write after Add).
    /// </summary>
    internal sealed class FixedPointStore
    {
        const int Shift = 8, Size = 1 << Shift, Mask = Size - 1;
        volatile FixedPoint3[][] chunks = new FixedPoint3[64][];
        int count;
        readonly object growLock = new object();

        public int Count { get { return System.Threading.Volatile.Read(ref count); } }

        [MethodImpl(JitHints.Optimize)]
        public int Add(ref FixedPoint3 v)
        {
            int i = System.Threading.Interlocked.Increment(ref count) - 1;
            int ci = i >> Shift;
            FixedPoint3[][] ch = chunks;
            if (ci >= ch.Length || ch[ci] == null) {
                lock (growLock) {
                    ch = chunks;
                    if (ci >= ch.Length) {
                        int n = ch.Length;
                        while (n <= ci) n *= 2;
                        var grown = new FixedPoint3[n][];
                        Array.Copy(ch, grown, ch.Length);
                        chunks = ch = grown;
                    }
                    if (ch[ci] == null)
                        ch[ci] = new FixedPoint3[Size];
                }
            }
            ch[ci][i & Mask] = v;
            return i;
        }

        public ref FixedPoint3 this[int i] { get { return ref chunks[i >> Shift][i & Mask]; } }
    }
}
