// LOCAL PROTOTYPE of the exact-number study (runs/exact-numbers, candidate D); not library code.
// The arrangement's BigInteger stages of InCircle and Orient (points beyond the fixed width) on stack limbs with
// FixedArith: the operands are copied from the points' BigIntegers per call. EXACT_PROTO contains "incircle" /
// "orient"; with "check" every sign is compared with the BigInteger stage.
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace g3
{
    internal static unsafe class LimbProtoD
    {
        static readonly string env = Environment.GetEnvironmentVariable("EXACT_PROTO") ?? "";
        internal static readonly bool InCircleOn = env.Contains("incircle");
        internal static readonly bool OrientOn = env.Contains("orient");
        internal static readonly bool Check = env.Contains("check");
        internal static long Mismatch, InCircleCalls, InCircleFallback, OrientCalls, OrientFallback, MaxLimbs;
        const int MaxScratch = 8192;        // limbs: 64 KB of stack

#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int cap(BigInteger v) { return (v.GetByteCount(false) + 7) >> 3; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static FixedInt load(BigInteger v, ulong* dst, int n)
        {
            int sign = v.Sign;
            if (sign == 0)
                return new FixedInt(null, 0, false);
            var bytes = new Span<byte>(dst, 8 * n);
            bytes.Fill(sign < 0 ? (byte)0xFF : (byte)0);
            int written;
            v.TryWriteBytes(bytes, out written, false, false);
            if (sign < 0) {
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

        static FixedInt det3(FixedInt a, FixedInt b, FixedInt c, FixedInt d, FixedInt e, FixedInt f, FixedInt g, FixedInt h, FixedInt i, ref FixedArena ar)
        {
            FixedInt t1 = FixedArith.Mul(a, FixedArith.Sub(FixedArith.Mul(e, i, ref ar), FixedArith.Mul(f, h, ref ar), ref ar), ref ar);
            FixedInt t2 = FixedArith.Mul(b, FixedArith.Sub(FixedArith.Mul(d, i, ref ar), FixedArith.Mul(f, g, ref ar), ref ar), ref ar);
            FixedInt t3 = FixedArith.Mul(c, FixedArith.Sub(FixedArith.Mul(d, h, ref ar), FixedArith.Mul(e, g, ref ar), ref ar), ref ar);
            return FixedArith.Add(FixedArith.Sub(t1, t2, ref ar), t3, ref ar);
        }

        // the row (A s, B s, A^2 + B^2) of v against d: A = v_i d.W - d_i v.W, B likewise, s = v.W d.W
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void row(FixedInt vi, FixedInt vj, FixedInt vw, FixedInt di, FixedInt dj, FixedInt dw, ref FixedArena ar, out FixedInt x, out FixedInt y, out FixedInt l)
        {
            FixedInt A = FixedArith.Sub(FixedArith.Mul(vi, dw, ref ar), FixedArith.Mul(di, vw, ref ar), ref ar);
            FixedInt B = FixedArith.Sub(FixedArith.Mul(vj, dw, ref ar), FixedArith.Mul(dj, vw, ref ar), ref ar);
            FixedInt s = FixedArith.Mul(vw, dw, ref ar);
            x = FixedArith.Mul(A, s, ref ar);
            y = FixedArith.Mul(B, s, ref ar);
            l = FixedArith.Add(FixedArith.Mul(A, A, ref ar), FixedArith.Mul(B, B, ref ar), ref ar);
        }

        /// <summary> the sign of the InCircle determinant of p, q, r against d in the axes i, j; 2 if beyond the stack limit </summary>
        [MethodImpl(JitHints.Optimize)]
        [SkipLocalsInit]
        internal static int InCircle(ExactPoint3 p, ExactPoint3 q, ExactPoint3 r, ExactPoint3 d, int i, int j)
        {
            System.Threading.Interlocked.Increment(ref InCircleCalls);
            int* n = stackalloc int[12];
            n[0] = cap(p[i]); n[1] = cap(p[j]); n[2] = cap(p.W);
            n[3] = cap(q[i]); n[4] = cap(q[j]); n[5] = cap(q.W);
            n[6] = cap(r[i]); n[7] = cap(r[j]); n[8] = cap(r.W);
            n[9] = cap(d[i]); n[10] = cap(d[j]); n[11] = cap(d.W);
            int total = 0, wv = 0, wd = Math.Max(n[9], Math.Max(n[10], n[11]));
            for (int k = 0; k < 12; ++k) total += n[k];
            for (int k = 0; k < 9; ++k) wv = Math.Max(wv, n[k]);
            if (wv + wd > MaxLimbs) MaxLimbs = wv + wd;
            int scratch = 120 * (wv + wd) + 200;
            if (scratch > MaxScratch) {
                System.Threading.Interlocked.Increment(ref InCircleFallback);
                return 2;
            }
            ulong* buf = stackalloc ulong[total];
            ulong* sc = stackalloc ulong[scratch];
            var ar = new FixedArena(sc, scratch);
            FixedInt* m = stackalloc FixedInt[12];
            ulong* b = buf;
            m[0] = load(p[i], b, n[0]); b += n[0]; m[1] = load(p[j], b, n[1]); b += n[1]; m[2] = load(p.W, b, n[2]); b += n[2];
            m[3] = load(q[i], b, n[3]); b += n[3]; m[4] = load(q[j], b, n[4]); b += n[4]; m[5] = load(q.W, b, n[5]); b += n[5];
            m[6] = load(r[i], b, n[6]); b += n[6]; m[7] = load(r[j], b, n[7]); b += n[7]; m[8] = load(r.W, b, n[8]); b += n[8];
            m[9] = load(d[i], b, n[9]); b += n[9]; m[10] = load(d[j], b, n[10]); b += n[10]; m[11] = load(d.W, b, n[11]);
            FixedInt ax, ay, al, bx, by, bl, cx, cy, cl;
            row(m[0], m[1], m[2], m[9], m[10], m[11], ref ar, out ax, out ay, out al);
            row(m[3], m[4], m[5], m[9], m[10], m[11], ref ar, out bx, out by, out bl);
            row(m[6], m[7], m[8], m[9], m[10], m[11], ref ar, out cx, out cy, out cl);
            return det3(ax, ay, al, bx, by, bl, cx, cy, cl, ref ar).Sign;
        }

        /// <summary> the sign of det3 of the rows (v_i, v_j, v.W) of a, b, c; 2 if beyond the stack limit </summary>
        [MethodImpl(JitHints.Optimize)]
        [SkipLocalsInit]
        internal static int Orient(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int i, int j)
        {
            System.Threading.Interlocked.Increment(ref OrientCalls);
            int* n = stackalloc int[9];
            n[0] = cap(a[i]); n[1] = cap(a[j]); n[2] = cap(a.W);
            n[3] = cap(b[i]); n[4] = cap(b[j]); n[5] = cap(b.W);
            n[6] = cap(c[i]); n[7] = cap(c[j]); n[8] = cap(c.W);
            int total = 0, w = 0;
            for (int k = 0; k < 9; ++k) { total += n[k]; w = Math.Max(w, n[k]); }
            int scratch = 34 * w + 32;
            if (scratch > MaxScratch) {
                System.Threading.Interlocked.Increment(ref OrientFallback);
                return 2;
            }
            ulong* buf = stackalloc ulong[total];
            ulong* sc = stackalloc ulong[scratch];
            var ar = new FixedArena(sc, scratch);
            FixedInt* m = stackalloc FixedInt[9];
            ulong* q = buf;
            for (int k = 0; k < 9; ++k) {
                ExactPoint3 v = (k < 3) ? a : (k < 6) ? b : c;
                int e = k % 3;
                m[k] = load(e == 0 ? v[i] : e == 1 ? v[j] : v.W, q, n[k]);
                q += n[k];
            }
            return det3(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], ref ar).Sign;
        }
#else
        internal static int InCircle(ExactPoint3 p, ExactPoint3 q, ExactPoint3 r, ExactPoint3 d, int i, int j) { return 2; }
        internal static int Orient(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int i, int j) { return 2; }
#endif
    }
}
