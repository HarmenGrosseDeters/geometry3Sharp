// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace g3
{
    /// <summary>
    /// Correctly rounded conversion of a decimal string to double and float (round to nearest, ties to even) in exact
    /// integer arithmetic. .NET Framework's double.Parse is not correctly rounded for long digit strings (a 20 digit string
    /// can be one ulp off) and reads "-0" as +0; .NET Core 3.0 and later are correct. MeshIOUtil uses this class only where
    /// a start-up probe finds the platform parser wrong, so the readers give the same vertices on every framework
    /// (docs/design/DETERMINISM.md, C2).
    ///
    /// Method (from the description of the problem in W. D. Clinger, "How to Read Floating Point Numbers Accurately",
    /// PLDI 1990, and D. M. Gay, "Correctly rounded binary-decimal and decimal-binary conversions", 1990): the value is
    /// N x 10^e with N an integer; a quotient of two big integers scaled by a power of two gives the p-bit significand
    /// and the remainder decides the rounding. A string of at most 15 digits with |e| &lt;= 22 is exact in one double
    /// multiplication or division (Clinger's fast path). Digits beyond 800 are replaced by a sticky digit: no decision
    /// needs more (a halfway case has at most 767 significant digits). Own implementation, nothing ported.
    /// </summary>
    internal static class ExactDecimal
    {
        const int MaxDigits = 800;

        static readonly double[] ExactPowers = {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22
        };

        /// <summary>
        /// False if the text is not a plain decimal number: optional white space, a sign, digits with an optional point
        /// (at least one digit), an optional exponent. (NaN, Infinity, hexadecimal and the like are the platform's.)
        /// </summary>
        static bool Scan(string s, out bool negative, out string digits, out int exp10)
        {
            negative = false; digits = ""; exp10 = 0;
            if (s == null) return false;
            int i = 0, n = s.Length;
            while (i < n && IsFloatWhiteSpace(s[i])) ++i;
            while (n > i && IsFloatWhiteSpace(s[n - 1])) --n;
            if (i < n && (s[i] == '+' || s[i] == '-')) { negative = s[i] == '-'; ++i; }
            var sb = new StringBuilder();
            int fraction = 0, any = 0;
            bool point = false;
            for (; i < n; ++i) {
                char c = s[i];
                if (c >= '0' && c <= '9') { sb.Append(c); ++any; if (point) ++fraction; }
                else if (c == '.' && !point) point = true;
                else break;
            }
            if (any == 0) return false;
            long e = 0;
            if (i < n) {
                if (s[i] != 'e' && s[i] != 'E') return false;
                ++i;
                bool eneg = false;
                if (i < n && (s[i] == '+' || s[i] == '-')) { eneg = s[i] == '-'; ++i; }
                if (i >= n) return false;
                for (; i < n; ++i) {
                    char c = s[i];
                    if (c < '0' || c > '9') return false;
                    if (e < 100000000) e = e * 10 + (c - '0');
                }
                if (eneg) e = -e;
            }
            string all = sb.ToString();
            int start = 0;
            while (start < all.Length && all[start] == '0') ++start;
            int end = all.Length;
            int trailing = 0;
            while (end > start && all[end - 1] == '0') { --end; ++trailing; }
            digits = all.Substring(start, end - start);
            long exp = e - fraction + trailing;
            if (digits.Length > MaxDigits) {
                // the digits beyond MaxDigits only matter as "something is left": a sticky 1 after the kept ones
                string kept = digits.Substring(0, MaxDigits), dropped = digits.Substring(MaxDigits);
                bool rest = dropped.Trim('0').Length > 0;
                exp += dropped.Length - (rest ? 1 : 0);
                digits = rest ? kept + "1" : kept;
            }
            exp10 = (int)Math.Max(-1000000000L, Math.Min(1000000000L, exp));
            return true;
        }

        // the white space NumberStyles.Float allows (U+0009 to U+000D and U+0020), not every Unicode space
        static bool IsFloatWhiteSpace(char c) { return c == ' ' || (c >= '\t' && c <= '\r'); }

        static readonly BigInteger[] Pow10Cache = new BigInteger[1200];

        static BigInteger Pow10(int n)
        {
            if (n >= Pow10Cache.Length) return BigInteger.Pow(10, n);
            BigInteger p = Pow10Cache[n];
            if (p.IsZero) Pow10Cache[n] = p = BigInteger.Pow(10, n);      // (a racing thread computes the same value)
            return p;
        }

        static int BitLength(BigInteger n)
        {
            byte[] b = n.ToByteArray();
            int top = b.Length - 1;
            while (top > 0 && b[top] == 0) --top;
            int bits = top * 8;
            for (int v = b[top]; v != 0; v >>= 1) ++bits;
            return bits;
        }

        // value = n x 10^e10 rounded to q x 2^k with q of p bits (fewer at k = kMin, the subnormals); false if it overflows
        static bool Round(BigInteger n, int e10, int p, int kMin, int kMax, out ulong q, out int k)
        {
            BigInteger num = n, den = BigInteger.One;
            if (e10 >= 0) num *= Pow10(e10); else den = Pow10(-e10);
            k = BitLength(num) - BitLength(den) - p;
            BigInteger top = BigInteger.One << p, half = BigInteger.One << (p - 1);
            while (true) {
                if (k < kMin) k = kMin;
                BigInteger a = num, b = den;
                if (k >= 0) b <<= k; else a <<= -k;
                BigInteger rem;
                BigInteger qq = BigInteger.DivRem(a, b, out rem);
                if (qq >= top) { ++k; continue; }
                if (qq < half && k > kMin) { --k; continue; }
                int c = (rem * 2).CompareTo(b);
                if (c > 0 || (c == 0 && !qq.IsEven)) qq += 1;
                q = (ulong)qq;
                if (q == (1UL << p)) { q >>= 1; ++k; }
                return k <= kMax;
            }
        }

        /// <summary> the correctly rounded double of a plain decimal string; false if the text is not one </summary>
        public static bool TryParseDouble(string s, out double value)
        {
            value = 0;
            bool neg; string digits; int e;
            if (!Scan(s, out neg, out digits, out e)) return false;
            double v;
            if (digits.Length == 0) v = 0;
            else if (digits.Length <= 15 && e >= -22 && e <= 22) {
                double w = (double)ulong.Parse(digits, CultureInfo.InvariantCulture);
                v = e >= 0 ? (double)(w * ExactPowers[e]) : (double)(w / ExactPowers[-e]);     // (the casts round to double on an x87 FPU too)
            } else {
                long lead = (long)digits.Length + e - 1;       // the decimal exponent of the leading digit
                if (lead > 310) v = double.PositiveInfinity;
                else if (lead < -330) v = 0;
                else {
                    ulong q; int k;
                    if (!Round(BigInteger.Parse(digits, CultureInfo.InvariantCulture), e, 53, -1074, 971, out q, out k)) v = double.PositiveInfinity;
                    else {
                        long bits = q < (1UL << 52) ? (long)q : ((long)(k + 1075) << 52) | (long)(q - (1UL << 52));
                        v = BitConverter.Int64BitsToDouble(bits);
                    }
                }
            }
            value = neg ? -v : v;
            return true;
        }

        /// <summary> the correctly rounded float of a plain decimal string (rounded once, not through double); false if the text is not one </summary>
        public static bool TryParseFloat(string s, out float value)
        {
            value = 0;
            bool neg; string digits; int e;
            if (!Scan(s, out neg, out digits, out e)) return false;
            float v;
            if (digits.Length == 0) v = 0;
            else {
                long lead = (long)digits.Length + e - 1;
                if (lead > 40) v = float.PositiveInfinity;
                else if (lead < -50) v = 0;
                else {
                    ulong q; int k;
                    if (!Round(BigInteger.Parse(digits, CultureInfo.InvariantCulture), e, 24, -149, 104, out q, out k)) v = float.PositiveInfinity;
                    else {
                        uint bits = q < (1UL << 23) ? (uint)q : ((uint)(k + 150) << 23) | (uint)(q - (1UL << 23));
                        v = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
                    }
                }
            }
            value = neg ? -v : v;
            return true;
        }
    }
}
