// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// `Ldexp` follows the scalbn algorithm of musl libc (MIT, Copyright (c) 2005-2020 Rich Felker, et al.).
// Helpers of the vendored CORE-MATH port (see CoreMathExp.cs for the sources): bit casts, exact rounding helpers.
// Everything here is exact (no libm): bit operations, Math.Floor / Math.Truncate / Math.Round (correctly defined),
// and the leading / trailing zero counts.
using System;
using System.Runtime.CompilerServices;

namespace g3.CoreMathPort
{
    internal static partial class CoreMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double Abs(double x)
        {
            return Polyfill.UInt64BitsToDouble(Polyfill.DoubleToUInt64Bits(x) & ~(1ul << 63));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double CopySign(double magnitude, double sign)
        {
            ulong m = Polyfill.DoubleToUInt64Bits(magnitude), s = Polyfill.DoubleToUInt64Bits(sign);
            return Polyfill.UInt64BitsToDouble((m & ((1ul << 63) - 1)) | (s & (1ul << 63)));
        }

        // Math.Min / Math.Max are exact (the callers pass non-NaN values)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double Min(double x, double y) { return Math.Min(x, y); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double Max(double x, double y) { return Math.Max(x, y); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double BuiltinFloor(double x)
        {
            return Math.Floor(x);       // exact on every runtime
        }

        /// <summary> round half away from zero (C round()), exact for |x| below 2^52, identity above </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double BuiltinRound(double x)
        {
            return Math.Round(x, MidpointRounding.AwayFromZero);
        }

        /// <summary> x * 2^exp for finite x with an exact result or the correctly rounded subnormal / overflowing one
        /// (the algorithm of musl's scalbn) </summary>
        internal static double Ldexp(double x, int n)
        {
            const double x1p1023 = 8.98846567431158E+307;           // 2^1023
            const double x1p_1022 = 2.2250738585072014E-308;        // 2^-1022
            const double x1p53 = 9007199254740992;                  // 2^53
            if (n > 1023) {
                x *= x1p1023;
                n -= 1023;
                if (n > 1023) {
                    x *= x1p1023;
                    n -= 1023;
                    if (n > 1023) n = 1023;
                }
            } else if (n < -1022) {
                // the multiplier is 2^-1022 * 2^53 (exact); the final scaling below rounds once
                x *= x1p_1022 * x1p53;
                n += 1022 - 53;
                if (n < -1022) {
                    x *= x1p_1022 * x1p53;
                    n += 1022 - 53;
                    if (n < -1022) n = -1022;
                }
            }
            return x * Polyfill.UInt64BitsToDouble((ulong)(n + 1023) << 52);
        }
    }

    internal static class Polyfill
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong DoubleToUInt64Bits(double x) { return (ulong)BitConverter.DoubleToInt64Bits(x); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double UInt64BitsToDouble(ulong x) { return BitConverter.Int64BitsToDouble((long)x); }

        internal static int TrailingZeroCount(ulong x)
        {
            if (x == 0) return 64;
            int n = 0;
            if ((x & 0xFFFFFFFFul) == 0) { n += 32; x >>= 32; }
            if ((x & 0xFFFFul) == 0) { n += 16; x >>= 16; }
            if ((x & 0xFFul) == 0) { n += 8; x >>= 8; }
            if ((x & 0xFul) == 0) { n += 4; x >>= 4; }
            if ((x & 0x3ul) == 0) { n += 2; x >>= 2; }
            if ((x & 0x1ul) == 0) n += 1;
            return n;
        }

        internal static int LeadingZeroCount(ulong x)
        {
            if (x == 0) return 64;
            int n = 0;
            if ((x >> 32) == 0) { n += 32; x <<= 32; }
            if ((x >> 48) == 0) { n += 16; x <<= 16; }
            if ((x >> 56) == 0) { n += 8; x <<= 8; }
            if ((x >> 60) == 0) { n += 4; x <<= 4; }
            if ((x >> 62) == 0) { n += 2; x <<= 2; }
            if ((x >> 63) == 0) n += 1;
            return n;
        }

        /// <summary> high 64 bits of the 128-bit product, the low 64 in lo </summary>
        internal static ulong BigMul(ulong x, ulong y, out ulong lo)
        {
            uint xlo = (uint)x, xhi = (uint)(x >> 32), ylo = (uint)y, yhi = (uint)(y >> 32);
            ulong lolo = (ulong)xlo * ylo;
            ulong mid1 = (ulong)xhi * ylo + (lolo >> 32);
            ulong mid2 = (ulong)xlo * yhi + (uint)mid1;
            ulong hihi = (ulong)xhi * yhi;
            lo = mid2 << 32 | (uint)lolo;
            return hihi + (mid1 >> 32) + (mid2 >> 32);
        }
    }
}

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    // lets the record structs of the port compile for netstandard2.0 (the BCL has it from .NET 5)
    internal static class IsExternalInit { }
}
#endif
