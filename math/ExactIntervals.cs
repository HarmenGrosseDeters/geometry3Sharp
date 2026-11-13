// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// A closed interval of doubles that contains an exact real value, for floating-point filters. Every operation
    /// rounds to nearest and then widens the result by one unit in the last place on both sides, so the exact result
    /// of the operation on any values in the operand intervals lies inside: round to nearest is off by at most half the
    /// spacing to the next double on the side of the exact value, and the widening moves by a whole spacing. This holds
    /// for subnormal results too (the spacing there is 2^-1074). A NaN (eg infinity minus infinity) gives the whole line.
    /// So a sign decided by Sign() is a proof, without a hand-derived error constant.
    /// </summary>
    internal struct ExactInterval
    {
        public readonly double Lo, Hi;

        public ExactInterval(double lo, double hi) { Lo = lo; Hi = hi; }

        /// <summary> the exact value x (a double) </summary>
        public static ExactInterval Of(double x) { return new ExactInterval(x, x); }

        static readonly ExactInterval whole = new ExactInterval(double.NegativeInfinity, double.PositiveInfinity);

        /// <summary> the next double above x (x for +infinity and NaN) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static double Up(double x)
        {
            if (double.IsNaN(x) || x == double.PositiveInfinity)
                return x;
            if (x == 0)
                return double.Epsilon;
            long b = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(x > 0 ? b + 1 : b - 1);
        }

        /// <summary> the next double below x </summary>
        public static double Down(double x) { return -Up(-x); }

        static ExactInterval make(double lo, double hi)
        {
            if (double.IsNaN(lo) || double.IsNaN(hi))
                return whole;
            return new ExactInterval(Down(lo), Up(hi));
        }

        [MethodImpl(JitHints.Optimize)]
        public static ExactInterval operator +(ExactInterval a, ExactInterval b) { return make(a.Lo + b.Lo, a.Hi + b.Hi); }

        [MethodImpl(JitHints.Optimize)]
        public static ExactInterval operator -(ExactInterval a, ExactInterval b) { return make(a.Lo - b.Hi, a.Hi - b.Lo); }

        public static ExactInterval operator -(ExactInterval a) { return new ExactInterval(-a.Hi, -a.Lo); }

        [MethodImpl(JitHints.Optimize)]
        public static ExactInterval operator *(ExactInterval a, ExactInterval b)
        {
            double p1 = a.Lo * b.Lo, p2 = a.Lo * b.Hi, p3 = a.Hi * b.Lo, p4 = a.Hi * b.Hi;
            if (double.IsNaN(p1) || double.IsNaN(p2) || double.IsNaN(p3) || double.IsNaN(p4))
                return whole;
            return make(Math.Min(Math.Min(p1, p2), Math.Min(p3, p4)), Math.Max(Math.Max(p1, p2), Math.Max(p3, p4)));
        }

        /// <summary>
        /// a / b; the whole line if b contains 0. Division is correctly rounded in IEEE 754 binary64 on every target
        /// (net10.0, netstandard2.0, net48 64-bit with SSE2), so a quotient is within half a spacing of the exact value
        /// and the one-spacing widening of make() encloses it, also for subnormal and overflowing quotients (an
        /// overflow to infinity widens to [MaxValue, infinity], and the exact quotient is at least MaxValue). With the
        /// x87 unit of 32-bit net48 the quotient is first rounded to 64 bits and then to 53: the error stays below
        /// half a spacing plus 2^-11 of it, below the one-spacing widening.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static ExactInterval operator /(ExactInterval a, ExactInterval b)
        {
            if (!(b.Lo > 0 || b.Hi < 0))
                return whole;                   // b contains 0 (or is NaN)
            double q1 = a.Lo / b.Lo, q2 = a.Lo / b.Hi, q3 = a.Hi / b.Lo, q4 = a.Hi / b.Hi;
            if (double.IsNaN(q1) || double.IsNaN(q2) || double.IsNaN(q3) || double.IsNaN(q4))
                return whole;
            return make(Math.Min(Math.Min(q1, q2), Math.Min(q3, q4)), Math.Max(Math.Max(q1, q2), Math.Max(q3, q4)));
        }

        /// <summary>
        /// the square root of the non-negative part of a (sqrt is monotone); the whole line if a is entirely negative or
        /// NaN. Math.Sqrt is correctly rounded in IEEE 754 on every target (the same argument as the division above),
        /// so the one-spacing widening encloses it; the lower end is clamped to 0.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static ExactInterval Sqrt(ExactInterval a)
        {
            if (!(a.Hi >= 0))
                return whole;
            ExactInterval r = make(Math.Sqrt(Math.Max(0, a.Lo)), Math.Sqrt(a.Hi));
            return new ExactInterval(Math.Max(0, r.Lo), r.Hi);
        }

        /// <summary> a * a, never negative </summary>
        [MethodImpl(JitHints.Optimize)]
        public static ExactInterval Square(ExactInterval a)
        {
            if (a.Lo >= 0)
                return make(a.Lo * a.Lo, a.Hi * a.Hi);
            if (a.Hi <= 0)
                return make(a.Hi * a.Hi, a.Lo * a.Lo);
            double m = Math.Max(-a.Lo, a.Hi);
            ExactInterval r = make(0, m * m);
            return new ExactInterval(0, r.Hi);
        }

        /// <summary> +1 or -1 if the sign of every value inside is that, 0 if the interval is exactly [0, 0], else 2 </summary>
        public int Sign()
        {
            if (Lo > 0) return 1;
            if (Hi < 0) return -1;
            if (Lo == 0 && Hi == 0) return 0;
            return 2;
        }
    }


    /// <summary>
    /// An exact dyadic number M * 2^E (every finite double is one), with exact sum, difference and product, for
    /// the exact stage of the predicates.
    /// </summary>
    internal struct ExactDyadic
    {
        public readonly BigInteger M;
        public readonly int E;

        public ExactDyadic(BigInteger m, int e) { M = m; E = e; }

        public static readonly ExactDyadic Zero = new ExactDyadic(BigInteger.Zero, 0);

        /// <summary> the finite double x exactly </summary>
        [MethodImpl(JitHints.Optimize)]
        public static ExactDyadic Of(double x)
        {
            if (x == 0)
                return Zero;
            long bits = BitConverter.DoubleToInt64Bits(x);
            bool negative = bits < 0;
            int exp = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0)
                exp = 1;            // subnormal
            else
                frac |= 1L << 52;
            return new ExactDyadic(negative ? -frac : frac, exp - 1075);
        }

        public int Sign { get { return M.Sign; } }

        [MethodImpl(JitHints.Optimize)]
        public static ExactDyadic operator +(ExactDyadic a, ExactDyadic b)
        {
            if (a.M.IsZero) return b;
            if (b.M.IsZero) return a;
            if (a.E == b.E) return new ExactDyadic(a.M + b.M, a.E);
            if (a.E < b.E) return new ExactDyadic(a.M + (b.M << (b.E - a.E)), a.E);
            return new ExactDyadic((a.M << (a.E - b.E)) + b.M, b.E);
        }

        public static ExactDyadic operator -(ExactDyadic a) { return new ExactDyadic(-a.M, a.E); }

        public static ExactDyadic operator -(ExactDyadic a, ExactDyadic b) { return a + (-b); }

        [MethodImpl(JitHints.Optimize)]
        public static ExactDyadic operator *(ExactDyadic a, ExactDyadic b)
        {
            if (a.M.IsZero || b.M.IsZero) return Zero;
            return new ExactDyadic(a.M * b.M, a.E + b.E);
        }

        /// <summary> sign of a - b </summary>
        public static int Compare(ExactDyadic a, ExactDyadic b) { return (a - b).Sign; }

        /// <summary> num / den rounded to the nearest double (ties to even), den != 0; overflow gives infinity </summary>
        [MethodImpl(JitHints.Optimize)]
        public static double Quotient(ExactDyadic num, ExactDyadic den)
        {
            if (den.M.IsZero)
                throw new DivideByZeroException();
            if (num.M.IsZero)
                return 0;
            bool negative = (num.M.Sign < 0) != (den.M.Sign < 0);
            BigInteger n = BigInteger.Abs(num.M), d = BigInteger.Abs(den.M);
            long e = (long)num.E - den.E;
            // q = floor(n 2^shift / d) with 56 or 57 significant bits, plus a sticky bit for the remainder
            long shift = 56 - (ExactPoint3.BitLength(n) - ExactPoint3.BitLength(d));
            BigInteger rem;
            BigInteger q = (shift >= 0) ? BigInteger.DivRem(n << (int)shift, d, out rem)
                                        : BigInteger.DivRem(n, d << (int)(-shift), out rem);
            e -= shift;
            long qb = ExactPoint3.BitLength(q);
            // value = (q + sticky) * 2^e with q < 2^qb, normalized: 2^(qb - 1) <= q
            long topExp = e + qb - 1;              // exponent of the leading bit
            long keep;                             // significant bits the double holds at this exponent
            if (topExp >= -1022) keep = 53;
            else keep = 53 - (-1022 - topExp);     // subnormal
            if (topExp > 1023)
                return negative ? double.NegativeInfinity : double.PositiveInfinity;
            if (keep < 0)
                return negative ? -0.0 : 0.0;      // below half the smallest subnormal (qb >= 55 bits, so keep < 0 is far below)
            long drop = qb - keep;                 // >= 3
            BigInteger mant = q >> (int)drop;
            BigInteger low = q - (mant << (int)drop);
            BigInteger half = BigInteger.One << (int)(drop - 1);
            int cmp = low.CompareTo(half);
            bool roundUp = cmp > 0 || (cmp == 0 && (rem.IsZero == false || mant.IsEven == false));
            if (roundUp)
                mant += 1;
            long me = e + drop;                    // value = mant * 2^me
            double m = (double)mant;               // exact: at most 54 bits, and 2^53 + 1 does not occur after rounding up
            double r = ExactPoint3.ScaleB(m, (int)me);
            return negative ? -r : r;
        }
    }
}
