// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Hand-written runtime of the generated integer predicates (GeneratedIntegerPredicates.cs, from
    /// tools/PredicateCompiler, "ipredicate"): fixed-width two's complement integers on stack buffers, as little-endian
    /// runs of 64-bit limbs. The generator sizes every buffer from proven bit bounds, so no operation overflows its
    /// result width; the operations still check that the result fits (Debug.Assert would hide a wrong bound in Release,
    /// so they throw).
    ///
    /// Experimental (docs/design/PREDICATE_COMPILER.md). On .NET without allocation (Math.BigMul,
    /// BigInteger.TryWriteBytes); on netstandard2.0 the same arithmetic with portable replacements (a 64 x 64 bit
    /// product from 32-bit halves; reading an input through BigInteger.ToByteArray, one allocation per input).
    /// </summary>
    internal static unsafe class IntegerRuntime
    {
        /// <summary> |v| < 2^bits (bits >= 1) </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Fits(BigInteger v, int bits)
        {
#if NET
            // GetBitLength: the bits of the shortest two's complement form without the sign bit. For v >= 0 it is the
            // bit length, so v < 2^bits iff it is <= bits. For v < 0 it is <= bits iff v >= -2^bits, and v = -2^bits
            // (the only such value with |v| = 2^bits) has exactly bits trailing zeros
            long len = v.GetBitLength();
            if (len > bits) return false;
            return v.Sign >= 0 || len < bits || BigInteger.TrailingZeroCount(v) < bits;
#else
            return FitsPortable(v, bits);
#endif
        }

        /// <summary> Fits for netstandard2.0 (one allocation; compiled everywhere, so that the tests on .NET check it) </summary>
        internal static bool FitsPortable(BigInteger v, int bits)
        {
            if (v.IsZero) return true;
            byte[] b = BigInteger.Abs(v).ToByteArray();     // little-endian, with a 0 byte on top if needed
            int top = b.Length - 1;
            while (top > 0 && b[top] == 0) --top;
            int n = 0;
            for (int x = b[top]; x != 0; x >>= 1) ++n;
            return 8L * top + n <= bits;
        }

        /// <summary> v into k limbs (two's complement); v must fit (Fits with 64 k - 1 bits) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static void Load(BigInteger v, ulong* r, int k)
        {
#if NET
            int written;
            if (!v.TryWriteBytes(new Span<byte>(r, 8 * k), out written, false, false))
                throw new InvalidOperationException("IntegerRuntime: value does not fit");
            byte fill = v.Sign < 0 ? (byte)0xFF : (byte)0;
            byte* p = (byte*)r;
            for (int i = written; i < 8 * k; ++i) p[i] = fill;
#else
            LoadPortable(v, r, k);
#endif
        }

        /// <summary> Load for netstandard2.0 (one allocation; compiled everywhere, so that the tests on .NET check it) </summary>
        internal static void LoadPortable(BigInteger v, ulong* r, int k)
        {
            byte* p = (byte*)r;
            byte[] bytes = v.ToByteArray();     // little-endian two's complement, shortest form
            byte fill = v.Sign < 0 ? (byte)0xFF : (byte)0;
            int written = bytes.Length;
            if (written > 8 * k) {
                // (the shortest form may carry a sign byte that the limbs do not need)
                for (int i = 8 * k; i < written; ++i)
                    if (bytes[i] != fill) throw new InvalidOperationException("IntegerRuntime: value does not fit");
                written = 8 * k;
            }
            for (int i = 0; i < written; ++i) p[i] = bytes[i];
            for (int i = written; i < 8 * k; ++i) p[i] = fill;
        }

        /// <summary> the 128-bit product x y: high half returned, low half in lo </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong big_mul(ulong x, ulong y, out ulong lo)
        {
#if NET
            return Math.BigMul(x, y, out lo);
#else
            return BigMulPortable(x, y, out lo);
#endif
        }

        /// <summary> the 128-bit product from 32-bit halves (netstandard2.0; compiled everywhere, so that the tests check it) </summary>
        internal static ulong BigMulPortable(ulong x, ulong y, out ulong lo)
        {
            ulong x0 = (uint)x, x1 = x >> 32, y0 = (uint)y, y1 = y >> 32;
            ulong p00 = x0 * y0, p01 = x0 * y1, p10 = x1 * y0, p11 = x1 * y1;
            // the middle column with the carries of the 32-bit parts (at most 3 (2^32 - 1))
            ulong mid = (p00 >> 32) + (uint)p01 + (uint)p10;
            lo = (mid << 32) | (uint)p00;
            return p11 + (p01 >> 32) + (p10 >> 32) + (mid >> 32);
        }

        // limb i of a (ka limbs), sign-extended
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong limb(ulong* a, int ka, int i)
        {
            return i < ka ? a[i] : ((long)a[ka - 1] < 0 ? ulong.MaxValue : 0);
        }

        /// <summary> r = a + b (sub: a - b), r of kr limbs, kr >= max(ka, kb) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static void Add(ulong* a, int ka, ulong* b, int kb, ulong* r, int kr, bool sub)
        {
            ulong carry = sub ? 1UL : 0UL;
            for (int i = 0; i < kr; ++i) {
                ulong x = limb(a, ka, i), y = limb(b, kb, i);
                if (sub) y = ~y;
                ulong s = x + y;
                ulong c1 = s < x ? 1UL : 0UL;
                ulong t = s + carry;
                ulong c2 = t < s ? 1UL : 0UL;
                r[i] = t;
                carry = c1 | c2;
            }
            // the exact result has one more limb: its sign extension must agree with r (no overflow)
            ulong top = limb(a, ka, kr) + (sub ? ~limb(b, kb, kr) : limb(b, kb, kr)) + carry;
            if (top != ((long)r[kr - 1] < 0 ? ulong.MaxValue : 0))
                throw new InvalidOperationException("IntegerRuntime: sum overflows its bound");
        }

        // |a| into m (ka limbs, unsigned), returns true if a < 0
        [MethodImpl(JitHints.Optimize)]
        static bool magnitude(ulong* a, int ka, ulong* m)
        {
            bool neg = (long)a[ka - 1] < 0;
            if (!neg) {
                for (int i = 0; i < ka; ++i) m[i] = a[i];
                return false;
            }
            ulong carry = 1;
            for (int i = 0; i < ka; ++i) {
                ulong t = ~a[i] + carry;
                carry = (carry == 1 && t == 0) ? 1UL : 0UL;
                m[i] = t;
            }
            return true;
        }

        /// <summary> r = a b, r of kr limbs; |a b| must be below 2^(64 kr - 1) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static void Mul(ulong* a, int ka, ulong* b, int kb, ulong* r, int kr)
        {
            ulong* ma = stackalloc ulong[ka];
            ulong* mb = stackalloc ulong[kb];
            bool neg = magnitude(a, ka, ma) ^ magnitude(b, kb, mb);
            // significant limbs
            int na = ka, nb = kb;
            while (na > 0 && ma[na - 1] == 0) --na;
            while (nb > 0 && mb[nb - 1] == 0) --nb;
            for (int i = 0; i < kr; ++i) r[i] = 0;
            if (na == 0 || nb == 0)
                return;
            if (na + nb > kr + 1)
                throw new InvalidOperationException("IntegerRuntime: product overflows its bound");
            for (int i = 0; i < na; ++i) {
                ulong carry = 0;
                ulong x = ma[i];
                int j = 0;
                for (; j < nb && i + j < kr; ++j) {
                    ulong lo;
                    ulong hi = big_mul(x, mb[j], out lo);
                    ulong s = r[i + j] + lo;
                    hi += s < lo ? 1UL : 0UL;
                    s += carry;
                    hi += s < carry ? 1UL : 0UL;
                    r[i + j] = s;
                    carry = hi;
                }
                if (i + j < kr) r[i + j] = carry;
                else if (carry != 0 || j < nb)
                    throw new InvalidOperationException("IntegerRuntime: product overflows its bound");
            }
            if ((long)r[kr - 1] < 0)
                throw new InvalidOperationException("IntegerRuntime: product overflows its bound");
            if (neg) {
                ulong carry = 1;
                for (int i = 0; i < kr; ++i) {
                    ulong t = ~r[i] + carry;
                    carry = (carry == 1 && t == 0) ? 1UL : 0UL;
                    r[i] = t;
                }
            }
        }

        /// <summary> the sign of a (k limbs) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int Sign(ulong* a, int k)
        {
            if ((long)a[k - 1] < 0) return -1;
            for (int i = 0; i < k; ++i)
                if (a[i] != 0) return 1;
            return 0;
        }
    }
}
