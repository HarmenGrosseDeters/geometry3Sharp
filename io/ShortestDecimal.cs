using System;
using System.Numerics;

namespace g3
{
    /// <summary>
    /// The shortest decimal string that reads back to the same double or float under a correctly rounded parser, the same
    /// on every framework (.NET Framework's "R" and default formats keep 15 digits and its parser is not always correctly
    /// rounded, so MeshIOUtil used to search for the digits with it; the text could then differ from .NET Core's, and a
    /// subnormal lost digits). Layout of the text: that of .NET Core 3.0 and later "R" (plain digits for a decimal exponent in
    /// [-4, 17) for double and [-4, 9) for float, else scientific "1.5E+20", "5E-324" with at least two exponent digits), so that
    /// on those frameworks nothing changed.
    ///
    /// Method: Ryu, U. Adams, "Ryu: fast float-to-string conversion", PLDI 2018 (the shortest digits inside the rounding
    /// interval of the value, closest to the value, ties to even, by 128-bit multiplication with truncated powers of 5).
    /// Own implementation from the paper's description; nothing ported. The tables of the powers of 5 are computed from
    /// their definition with BigInteger at first use (the reference implementation stores them as constants). The result is
    /// the same digit string as the exact Steele-White / Dragon4 "shortest" ("How to print floating-point numbers
    /// accurately", PLDI 1990), which the tests use as the oracle. Integer arithmetic only, no platform formatting.
    /// </summary>
    internal static class ShortestDecimal
    {
        // bit counts of the table entries (as in the paper's implementation: 125 bit for double, 59 and 61 bit for float)
        const int DoublePow5InvBitCount = 125;
        const int DoublePow5BitCount = 125;
        const int FloatPow5InvBitCount = 59;
        const int FloatPow5BitCount = 61;

        const int DoublePow5InvTableSize = 342;
        const int DoublePow5TableSize = 326;
        const int FloatPow5InvTableSize = 32;
        const int FloatPow5TableSize = 48;

        // two ulongs (low, high) per entry
        static readonly ulong[] DoublePow5Inv = new ulong[2 * DoublePow5InvTableSize];
        static readonly ulong[] DoublePow5 = new ulong[2 * DoublePow5TableSize];
        static readonly ulong[] FloatPow5Inv = new ulong[FloatPow5InvTableSize];
        static readonly ulong[] FloatPow5 = new ulong[FloatPow5TableSize];

        static ShortestDecimal()
        {
            var mask = (BigInteger.One << 64) - BigInteger.One;
            BigInteger pow5 = BigInteger.One;
            int size = Math.Max(Math.Max(DoublePow5InvTableSize, DoublePow5TableSize), Math.Max(FloatPow5InvTableSize, FloatPow5TableSize));
            for (int i = 0; i < size; ++i) {
                int pb = Pow5Bits(i);
                if (i < DoublePow5InvTableSize) {
                    BigInteger inv = (BigInteger.One << (pb - 1 + DoublePow5InvBitCount)) / pow5 + BigInteger.One;
                    DoublePow5Inv[2 * i] = (ulong)(inv & mask);
                    DoublePow5Inv[2 * i + 1] = (ulong)(inv >> 64);
                }
                if (i < DoublePow5TableSize) {
                    int shift = pb - DoublePow5BitCount;
                    BigInteger p = shift >= 0 ? pow5 >> shift : pow5 << -shift;
                    DoublePow5[2 * i] = (ulong)(p & mask);
                    DoublePow5[2 * i + 1] = (ulong)(p >> 64);
                }
                if (i < FloatPow5InvTableSize)
                    FloatPow5Inv[i] = (ulong)((BigInteger.One << (pb - 1 + FloatPow5InvBitCount)) / pow5 + BigInteger.One);
                if (i < FloatPow5TableSize) {
                    int shift = pb - FloatPow5BitCount;
                    FloatPow5[i] = (ulong)(shift >= 0 ? pow5 >> shift : pow5 << -shift);
                }
                pow5 *= 5;
            }
        }

        // number of bits of 5^e (e in [0, 3528]; 1 for e = 0)
        static int Pow5Bits(int e) { return (int)(((uint)e * 1217359u) >> 19) + 1; }
        // floor(log10(2^e)), e in [0, 1650]
        static int Log10Pow2(int e) { return (int)(((uint)e * 78913u) >> 18); }
        // floor(log10(5^e)), e in [0, 2620]
        static int Log10Pow5(int e) { return (int)(((uint)e * 732923u) >> 20); }

        static bool MultipleOfPowerOf5(ulong value, int p)
        {
            // value has at least p factors 5
            int count = 0;
            while (value > 0) {
                ulong q = value / 5;
                if (value != q * 5) break;
                value = q;
                ++count;
                if (count >= p) return true;
            }
            return count >= p;
        }

        static bool MultipleOfPowerOf2(ulong value, int p) { return (value & ((1UL << p) - 1)) == 0; }

        // a * b as 128 bit
        static ulong Mul128(ulong a, ulong b, out ulong hi)
        {
            ulong aLo = (uint)a, aHi = a >> 32, bLo = (uint)b, bHi = b >> 32;
            ulong b00 = aLo * bLo, b01 = aLo * bHi, b10 = aHi * bLo, b11 = aHi * bHi;
            ulong mid1 = b10 + (b00 >> 32);
            ulong mid2 = b01 + (uint)mid1;
            hi = b11 + (mid1 >> 32) + (mid2 >> 32);
            return (mid2 << 32) | (uint)b00;
        }

        // (m * (mulHi:mulLo)) >> j, for 64 < j < 128 and a result below 2^64
        static ulong MulShift64(ulong m, ulong mulLo, ulong mulHi, int j)
        {
            ulong hi0;
            Mul128(m, mulLo, out hi0);                    // (low part of the first product is shifted out)
            ulong hi2;
            ulong lo2 = Mul128(m, mulHi, out hi2);
            ulong sumLo = lo2 + hi0;
            ulong sumHi = hi2 + (sumLo < lo2 ? 1UL : 0UL);
            int s = j - 64;
            return (sumHi << (64 - s)) | (sumLo >> s);
        }

        static ulong MulShiftAll(ulong m, ulong[] table, int index, int j, uint mmShift, out ulong vp, out ulong vm)
        {
            ulong lo = table[2 * index], hi = table[2 * index + 1];
            vp = MulShift64(4 * m + 2, lo, hi, j);
            vm = MulShift64(4 * m - 1 - mmShift, lo, hi, j);
            return MulShift64(4 * m, lo, hi, j);
        }

        static uint MulShift32(uint m, ulong factor, int shift)
        {
            uint factorLo = (uint)factor, factorHi = (uint)(factor >> 32);
            ulong bits0 = (ulong)m * factorLo, bits1 = (ulong)m * factorHi;
            ulong sum = (bits0 >> 32) + bits1;
            return (uint)(sum >> (shift - 32));
        }

        /// <summary>
        /// The shortest digits of a positive finite double: value = digits x 10^exp10, digits without trailing zeros removed
        /// only as far as the interval allows (it is the shortest, then the closest to the value)
        /// </summary>
        internal static void Shortest(double abs, out ulong digits, out int exp10)
        {
            ulong bits = (ulong)BitConverter.DoubleToInt64Bits(abs);
            ulong ieeeMantissa = bits & ((1UL << 52) - 1);
            int ieeeExponent = (int)((bits >> 52) & 0x7FF);
            int e2;
            ulong m2;
            if (ieeeExponent == 0) { e2 = 1 - 1023 - 52 - 2; m2 = ieeeMantissa; }
            else { e2 = ieeeExponent - 1023 - 52 - 2; m2 = (1UL << 52) | ieeeMantissa; }
            bool acceptBounds = (m2 & 1) == 0;
            ulong mv = 4 * m2;
            uint mmShift = (ieeeMantissa != 0 || ieeeExponent <= 1) ? 1u : 0u;

            ulong vr, vp, vm;
            int e10;
            bool vmIsTrailingZeros = false, vrIsTrailingZeros = false;
            if (e2 >= 0) {
                int q = Log10Pow2(e2) - (e2 > 3 ? 1 : 0);
                e10 = q;
                int k = DoublePow5InvBitCount + Pow5Bits(q) - 1;
                int i = -e2 + q + k;
                vr = MulShiftAll(m2, DoublePow5Inv, q, i, mmShift, out vp, out vm);
                if (q <= 21) {
                    // only one of mp, mv, mm can be a multiple of 5, if any
                    if (mv % 5 == 0) vrIsTrailingZeros = MultipleOfPowerOf5(mv, q);
                    else if (acceptBounds) vmIsTrailingZeros = MultipleOfPowerOf5(mv - 1 - mmShift, q);
                    else if (MultipleOfPowerOf5(mv + 2, q)) --vp;
                }
            } else {
                int q = Log10Pow5(-e2) - (-e2 > 1 ? 1 : 0);
                e10 = q + e2;
                int i = -e2 - q;
                int k = Pow5Bits(i) - DoublePow5BitCount;
                int j = q - k;
                vr = MulShiftAll(m2, DoublePow5, i, j, mmShift, out vp, out vm);
                if (q <= 1) {
                    // mv = 4 m2 has at least two trailing zero bits, so vr is exact
                    vrIsTrailingZeros = true;
                    if (acceptBounds) vmIsTrailingZeros = mmShift == 1;   // mm = mv - 1 - mmShift: its low bit is set iff mmShift is 1
                    else --vp;
                } else if (q < 63) {
                    vrIsTrailingZeros = MultipleOfPowerOf2(mv, q);
                }
            }
            Finish(vr, vp, vm, 0, vmIsTrailingZeros, vrIsTrailingZeros, acceptBounds, e10, out digits, out exp10);
        }

        /// <summary> as <see cref="Shortest(double, out ulong, out int)"/> for a positive finite float </summary>
        internal static void ShortestFloat(float abs, out ulong digits, out int exp10)
        {
            uint bits = (uint)BitConverter.ToInt32(BitConverter.GetBytes(abs), 0);
            uint ieeeMantissa = bits & ((1u << 23) - 1);
            int ieeeExponent = (int)((bits >> 23) & 0xFF);
            int e2;
            uint m2;
            if (ieeeExponent == 0) { e2 = 1 - 127 - 23 - 2; m2 = ieeeMantissa; }
            else { e2 = ieeeExponent - 127 - 23 - 2; m2 = (1u << 23) | ieeeMantissa; }
            bool acceptBounds = (m2 & 1) == 0;
            uint mv = 4 * m2, mp = 4 * m2 + 2;
            uint mmShift = (ieeeMantissa != 0 || ieeeExponent <= 1) ? 1u : 0u;
            uint mm = 4 * m2 - 1 - mmShift;

            uint vr, vp, vm;
            int e10;
            bool vmIsTrailingZeros = false, vrIsTrailingZeros = false;
            uint lastRemovedDigit = 0;
            if (e2 >= 0) {
                int q = Log10Pow2(e2);
                e10 = q;
                int k = FloatPow5InvBitCount + Pow5Bits(q) - 1;
                int i = -e2 + q + k;
                vr = MulShift32(mv, FloatPow5Inv[q], i);
                vp = MulShift32(mp, FloatPow5Inv[q], i);
                vm = MulShift32(mm, FloatPow5Inv[q], i);
                if (q != 0 && (vp - 1) / 10 <= vm / 10) {
                    // one removed digit is needed even if the loop below does not run
                    int l = FloatPow5InvBitCount + Pow5Bits(q - 1) - 1;
                    lastRemovedDigit = MulShift32(mv, FloatPow5Inv[q - 1], -e2 + q - 1 + l) % 10;
                }
                if (q <= 9) {
                    if (mv % 5 == 0) vrIsTrailingZeros = MultipleOfPowerOf5(mv, q);
                    else if (acceptBounds) vmIsTrailingZeros = MultipleOfPowerOf5(mm, q);
                    else if (MultipleOfPowerOf5(mp, q)) --vp;
                }
            } else {
                int q = Log10Pow5(-e2);
                e10 = q + e2;
                int i = -e2 - q;
                int k = Pow5Bits(i) - FloatPow5BitCount;
                int j = q - k;
                vr = MulShift32(mv, FloatPow5[i], j);
                vp = MulShift32(mp, FloatPow5[i], j);
                vm = MulShift32(mm, FloatPow5[i], j);
                if (q != 0 && (vp - 1) / 10 <= vm / 10) {
                    j = q - 1 - (Pow5Bits(i + 1) - FloatPow5BitCount);
                    lastRemovedDigit = MulShift32(mv, FloatPow5[i + 1], j) % 10;
                }
                if (q <= 1) {
                    vrIsTrailingZeros = true;
                    if (acceptBounds) vmIsTrailingZeros = mmShift == 1;
                    else --vp;
                } else if (q < 31) {
                    vrIsTrailingZeros = MultipleOfPowerOf2(mv, q - 1);
                }
            }
            Finish(vr, vp, vm, lastRemovedDigit, vmIsTrailingZeros, vrIsTrailingZeros, acceptBounds, e10, out digits, out exp10);
        }

        // the step from the scaled interval (vm, vr, vp) to the shortest digits, with the exact tie and bound rules
        static void Finish(ulong vr, ulong vp, ulong vm, uint lastRemovedDigit, bool vmIsTrailingZeros, bool vrIsTrailingZeros,
                           bool acceptBounds, int e10, out ulong digits, out int exp10)
        {
            int removed = 0;
            if (vmIsTrailingZeros || vrIsTrailingZeros) {
                // rare: the general case
                while (vp / 10 > vm / 10) {
                    vmIsTrailingZeros &= vm % 10 == 0;
                    vrIsTrailingZeros &= lastRemovedDigit == 0;
                    lastRemovedDigit = (uint)(vr % 10);
                    vr /= 10; vp /= 10; vm /= 10;
                    ++removed;
                }
                if (vmIsTrailingZeros) {
                    while (vm % 10 == 0) {
                        vrIsTrailingZeros &= lastRemovedDigit == 0;
                        lastRemovedDigit = (uint)(vr % 10);
                        vr /= 10; vp /= 10; vm /= 10;
                        ++removed;
                    }
                }
                // exactly halfway and the digit before is even: round down (to even)
                if (vrIsTrailingZeros && lastRemovedDigit == 5 && vr % 2 == 0) lastRemovedDigit = 4;
                bool up = (vr == vm && (!acceptBounds || !vmIsTrailingZeros)) || lastRemovedDigit >= 5;
                digits = vr + (up ? 1UL : 0UL);
            } else {
                bool roundUp = lastRemovedDigit >= 5;
                while (vp / 10 > vm / 10) {
                    roundUp = vr % 10 >= 5;
                    vr /= 10; vp /= 10; vm /= 10;
                    ++removed;
                }
                digits = vr + ((vr == vm || roundUp) ? 1UL : 0UL);
            }
            exp10 = e10 + removed;
        }

        static int DecimalLength(ulong v)
        {
            int n = 1;
            while (v >= 10) { v /= 10; ++n; }
            return n;
        }

        /// <summary>
        /// The text of a double: "NaN", "Infinity", "-Infinity", "0", "-0", else the shortest digits in the layout of .NET
        /// Core's "R"
        /// </summary>
        internal static string Format(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "Infinity";
            if (double.IsNegativeInfinity(v)) return "-Infinity";
            bool negative = BitConverter.DoubleToInt64Bits(v) < 0;
            if (v == 0) return negative ? "-0" : "0";
            ulong digits; int exp10;
            Shortest(Math.Abs(v), out digits, out exp10);
            return Layout(negative, digits, exp10, 17);
        }

        /// <summary> as <see cref="Format(double)"/>, for float </summary>
        internal static string Format(float v)
        {
            if (float.IsNaN(v)) return "NaN";
            if (float.IsPositiveInfinity(v)) return "Infinity";
            if (float.IsNegativeInfinity(v)) return "-Infinity";
            bool negative = BitConverter.ToInt32(BitConverter.GetBytes(v), 0) < 0;
            if (v == 0) return negative ? "-0" : "0";
            ulong digits; int exp10;
            ShortestFloat(Math.Abs(v), out digits, out exp10);
            return Layout(negative, digits, exp10, 9);
        }

        // digits x 10^exp10 as plain text when its decimal exponent is in [-4, sciLimit), else as d.dddE+XX
        static string Layout(bool negative, ulong digits, int exp10, int sciLimit)
        {
            int n = DecimalLength(digits);
            int sci = exp10 + n - 1;
            char[] buf = new char[40];
            int pos = 0;
            if (negative) buf[pos++] = '-';
            char[] d = new char[n];
            for (int i = n - 1; i >= 0; --i) { d[i] = (char)('0' + (int)(digits % 10)); digits /= 10; }
            if (sci < -4 || sci >= sciLimit) {
                buf[pos++] = d[0];
                if (n > 1) {
                    buf[pos++] = '.';
                    for (int i = 1; i < n; ++i) buf[pos++] = d[i];
                }
                buf[pos++] = 'E';
                buf[pos++] = sci < 0 ? '-' : '+';
                int a = Math.Abs(sci);
                if (a >= 100) buf[pos++] = (char)('0' + a / 100);
                buf[pos++] = (char)('0' + (a / 10) % 10);
                buf[pos++] = (char)('0' + a % 10);
            } else if (exp10 >= 0) {
                for (int i = 0; i < n; ++i) buf[pos++] = d[i];
                for (int i = 0; i < exp10; ++i) buf[pos++] = '0';
            } else if (sci >= 0) {
                int intDigits = n + exp10;
                for (int i = 0; i < intDigits; ++i) buf[pos++] = d[i];
                buf[pos++] = '.';
                for (int i = intDigits; i < n; ++i) buf[pos++] = d[i];
            } else {
                buf[pos++] = '0';
                buf[pos++] = '.';
                for (int i = 0; i < -sci - 1; ++i) buf[pos++] = '0';
                for (int i = 0; i < n; ++i) buf[pos++] = d[i];
            }
            return new string(buf, 0, pos);
        }
    }
}
