// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
#if NET8_0_OR_GREATER
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace g3
{
    [InlineArray(6)]
    internal struct Limbs6
    {
        ulong l;
    }

    /// <summary>
    /// Signed 384-bit integer (two's complement, six 64-bit limbs, least significant first) without allocation, for
    /// exact arithmetic whose operand sizes are bounded (the planes of triangles with integer coordinates below 2^88).
    /// The caller guarantees that no result leaves the range; nothing is checked. (Spike.)
    /// </summary>
    internal struct Int384
    {
        const int N = 6;
        Limbs6 L;

        public static Int384 FromLong(long v)
        {
            var r = new Int384();
            ulong ext = (v < 0) ? ulong.MaxValue : 0;
            r.L[0] = (ulong)v;
            for (int k = 1; k < N; ++k) r.L[k] = ext;
            return r;
        }

        /// <summary> v must be below 2^383 in magnitude </summary>
        public static Int384 FromBigInteger(BigInteger v)
        {
            var r = new Int384();
            bool neg = v.Sign < 0;
            BigInteger a = BigInteger.Abs(v);
            Span<byte> bytes = stackalloc byte[8 * N + 1];
            bytes.Clear();
            a.TryWriteBytes(bytes, out _, isUnsigned: true, isBigEndian: false);
            for (int k = 0; k < N; ++k) r.L[k] = BitConverter.ToUInt64(bytes.Slice(8 * k, 8));
            return neg ? -r : r;
        }

        public bool IsNegative { get { return (long)L[N - 1] < 0; } }
        public bool IsZero
        {
            get {
                ulong o = 0;
                for (int k = 0; k < N; ++k) o |= L[k];
                return o == 0;
            }
        }
        public int Sign { get { return IsNegative ? -1 : IsZero ? 0 : 1; } }

        public static Int384 operator +(Int384 a, Int384 b)
        {
            var r = new Int384();
            ulong c = 0;
            for (int k = 0; k < N; ++k) {
                ulong s = a.L[k] + b.L[k];
                ulong c1 = (s < a.L[k]) ? 1UL : 0;
                ulong t = s + c;
                c = c1 + ((t < s) ? 1UL : 0);
                r.L[k] = t;
            }
            return r;
        }

        public static Int384 operator -(Int384 a)
        {
            var r = new Int384();
            ulong c = 1;
            for (int k = 0; k < N; ++k) {
                ulong t = ~a.L[k] + c;
                c = (t < c) ? 1UL : 0;
                r.L[k] = t;
            }
            return r;
        }

        public static Int384 operator -(Int384 a, Int384 b) { return a + (-b); }

        /// <summary> product modulo 2^384 (the exact product if it is in range) </summary>
        public static Int384 operator *(Int384 a, Int384 b)
        {
            // schoolbook on the limbs, only the partial products below 2^384 (two's complement: the product modulo 2^384
            // of the unsigned representations is that of the signed values); the high limbs of a are mostly sign
            // extension, so the loops skip zero limbs of the magnitudes' representations only where exact
            var r = new Int384();
            for (int i = 0; i < N; ++i) {
                ulong ai = a.L[i];
                if (ai == 0) continue;
                ulong carry = 0;
                for (int j = 0; i + j < N; ++j) {
                    ulong lo, hi = Math.BigMul(ai, b.L[j], out lo);
                    ulong s = r.L[i + j] + lo;
                    hi += (s < lo) ? 1UL : 0;
                    ulong t = s + carry;
                    hi += (t < s) ? 1UL : 0;
                    r.L[i + j] = t;
                    carry = hi;
                }
            }
            return r;
        }

        public static Int384 operator *(Int384 a, long b) { return a * FromLong(b); }

        /// <summary> |a| compared with |b| </summary>
        public static int CompareAbs(Int384 a, Int384 b)
        {
            if (a.IsNegative) a = -a;
            if (b.IsNegative) b = -b;
            for (int k = N - 1; k >= 0; --k)
                if (a.L[k] != b.L[k])
                    return a.L[k].CompareTo(b.L[k]);
            return 0;
        }

        /// <summary>
        /// the double of the value as System.Numerics.BigInteger converts it: the 53 most significant bits, the others
        /// cut off (towards zero); exact up to 2^53
        /// </summary>
        public double ToDouble()
        {
            bool neg = IsNegative;
            Int384 a = neg ? -this : this;
            int top = N - 1;
            while (top > 0 && a.L[top] == 0) --top;
            ulong hi = a.L[top];
            if (top == 0 && hi < (1UL << 53))
                return neg ? -(double)hi : (double)hi;
            int lz = BitOperations.LeadingZeroCount(hi);
            ulong next = (top > 0) ? a.L[top - 1] : 0;
            ulong m64 = (lz == 0) ? hi : (hi << lz) | (next >> (64 - lz));
            ulong m53 = m64 >> 11;
            int exp = 64 * top + (64 - lz) - 53;      // value = m53 * 2^exp (cut)
            double d = Math.ScaleB((double)m53, exp);
            return neg ? -d : d;
        }

        public BigInteger ToBigInteger()
        {
            bool neg = IsNegative;
            Int384 a = neg ? -this : this;
            Span<byte> bytes = stackalloc byte[8 * N];
            for (int k = 0; k < N; ++k)
                BitConverter.TryWriteBytes(bytes.Slice(8 * k, 8), a.L[k]);
            var v = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
            return neg ? -v : v;
        }
    }
}
#endif
