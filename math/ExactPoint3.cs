// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Exact 3D point with rational coordinates, in homogeneous integer form (X/W, Y/W, Z/W).
    /// Canonical: W > 0 and gcd(X, Y, Z, W) = 1, so two points are equal iff their components are equal.
    /// Used by ExactMeshBoolean, where input coordinates are scaled to an integer grid.
    /// </summary>
    public struct ExactPoint3 : IEquatable<ExactPoint3>
    {
        public readonly BigInteger X, Y, Z, W;

        ExactPoint3(BigInteger x, BigInteger y, BigInteger z, BigInteger w)
        {
            X = x; Y = y; Z = z; W = w;
        }

        /// <summary> integer point (W = 1) </summary>
        public static ExactPoint3 FromInteger(BigInteger x, BigInteger y, BigInteger z)
        {
            return new ExactPoint3(x, y, z, BigInteger.One);
        }

        /// <summary> canonical point (x/w, y/w, z/w); w must not be 0 </summary>
        [MethodImpl(JitHints.Optimize)]
        public static ExactPoint3 Create(BigInteger x, BigInteger y, BigInteger z, BigInteger w)
        {
            if (w.IsZero)
                throw new ArgumentException("ExactPoint3: W is zero");
            if (w.Sign < 0) {
                x = -x; y = -y; z = -z; w = -w;
            }
            if (w.IsOne)
                return new ExactPoint3(x, y, z, w);
            BigInteger g = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(x, y), BigInteger.GreatestCommonDivisor(z, w));
            if (g.IsOne == false && g.IsZero == false) {
                x /= g; y /= g; z /= g; w /= g;
            }
            return new ExactPoint3(x, y, z, w);
        }

        /// <summary> the point (x/w, y/w, z/w) with W > 0 but without reducing by the gcd (not canonical: not for comparisons by value) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static ExactPoint3 CreateUnreduced(BigInteger x, BigInteger y, BigInteger z, BigInteger w)
        {
            if (w.IsZero)
                throw new ArgumentException("ExactPoint3: W is zero");
            return (w.Sign < 0) ? new ExactPoint3(-x, -y, -z, -w) : new ExactPoint3(x, y, z, w);
        }

        /// <summary> coordinate i of the numerator: X, Y, Z for i = 0, 1, 2 </summary>
        public BigInteger this[int i] {
            get {
                switch (i) {
                    case 0: return X;
                    case 1: return Y;
                    case 2: return Z;
                    default: throw new ArgumentOutOfRangeException(nameof(i), "ExactPoint3: coordinate index must be 0, 1 or 2");
                }
            }
        }

        public bool Equals(ExactPoint3 o)
        {
            return W == o.W && X == o.X && Y == o.Y && Z == o.Z;
        }
        public override bool Equals(object obj)
        {
            return obj is ExactPoint3 && Equals((ExactPoint3)obj);
        }
        /// <summary>
        /// Bits of the components for choosing a shard of a lookup structure (ExactMeshArrangement.AddPoint): the low 31 bits of each component
        /// (the point is canonical, so equal points give equal bits), unseeded; unlike GetHashCode it is a function of the value on every runtime
        /// </summary>
        public int ShardBits()
        {
            unchecked {
                int h = (int)(X & int.MaxValue);
                h = h * 31 + (int)(Y & int.MaxValue);
                h = h * 31 + (int)(Z & int.MaxValue);
                return h * 31 + (int)(W & int.MaxValue);
            }
        }

        [MethodImpl(JitHints.Optimize)]
        public override int GetHashCode()
        {
            unchecked {
                int h = X.GetHashCode();
                h = h * 31 + Y.GetHashCode();
                h = h * 31 + Z.GetHashCode();
                return h * 31 + W.GetHashCode();
            }
        }

        /// <summary> coordinates as doubles, multiplied by 2^scaleExp (each coordinate correctly rounded, see ToDouble) </summary>
        public Vector3d ToVector3d(int scaleExp = 0)
        {
            return new Vector3d(ToDouble(X, W, scaleExp), ToDouble(Y, W, scaleExp), ToDouble(Z, W, scaleExp));
        }

        public override string ToString()
        {
            return ToVector3d().ToString();
        }


        /// <summary>
        /// Fast approximation of the coordinates (relative error below 4 * 2^-52, plus an absolute 2^-1074 where a
        /// coordinate is subnormal or below the double range): for floating-point filters
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public Vector3d ApproxVector3d()
        {
            double w = (double)W;
            double x = (double)X / w, y = (double)Y / w, z = (double)Z / w;
            if (is_safe(w) && is_safe_or_zero(x) && is_safe_or_zero(y) && is_safe_or_zero(z))
                return new Vector3d(x, y, z);
            return ToVector3d(0);
        }
        static bool is_safe(double v) { return double.IsInfinity(v) == false && double.IsNaN(v) == false && Math.Abs(v) > 1e-290; }
        static bool is_safe_or_zero(double v) { return v == 0 || is_safe(v); }

        /// <summary>
        /// num / den * 2^scaleExp as a double, correctly rounded (issue #1410): the double nearest to the exact value, ties to even, in one
        /// rounding also in the subnormal range; a result of at most 2^-1075 in magnitude (the exact value 2^-1075 is a tie, to even) is a zero of the sign of num, one at or above the middle between the largest
        /// double and 2^1024 an infinity. A negative den is allowed (the quotient has the sign of num * den); den = 0 always throws
        /// <see cref="DivideByZeroException"/>, also for num = 0 and for a scaleExp that would underflow. See <see cref="ToDoubleRounded"/> for the method
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static double ToDouble(BigInteger num, BigInteger den, int scaleExp)
        {
            if (den.IsZero)
                throw new DivideByZeroException();
            if (den.Sign < 0) {
                num = -num; den = -den;
            }
            return ToDoubleRounded(num, den, scaleExp);
        }

        /// <summary>
        /// num / den * 2^scaleExp as a double, den &gt; 0, correctly rounded: the double nearest to the exact value, ties to even, in one
        /// rounding also in the subnormal range (a result of at most 2^-1075 in magnitude, the tie included, is a zero of the sign of num, one at or above overflows to an infinity).
        /// Method (the standard one: enough quotient bits plus a sticky bit, one round to nearest even): a = |num| is shifted so that the
        /// integer quotient q = floor(n2 / d2) with n2 = a 2^shift, d2 = den (shift &gt;= 0) or n2 = a, d2 = den 2^-shift has 55 or 56 bits
        /// (2^54 &lt;= q &lt; 2^56). The exact value is
        /// (q + f) 2^(scaleExp - shift) with 0 &lt;= f &lt; 1, f &gt; 0 iff q d2 != n2 (the sticky bit; a division without remainder is cheaper, the product is only formed on a tie). The result grid is 2^g with
        /// g = max(bitlength(q) - 53, -1074 - (scaleExp - shift)) quotient bits dropped (53 significant bits, or fewer in the subnormal range,
        /// so the grid is the subnormal grid 2^-1074 and nothing is rounded twice). With mant = q &gt;&gt; g and low = q mod 2^g, the value lies
        /// above the midpoint iff low &gt; 2^(g-1) or (low = 2^(g-1) and f &gt; 0), on it iff low = 2^(g-1) and f = 0 (then the even mant wins);
        /// below otherwise. mant + 1 can be 2^53, a power of two, which converts exactly (the next binade, or an overflow in ScaleB).
        /// g &gt;= 57 means q is below half a unit (q &lt; 2^56 &lt;= 2^(g-1)), so the result is zero. The shifts and the masks are exact. The final
        /// ScaleB scales an integer mant &lt;= 2^53 by 2^e2 with e2 &gt;= -1074 (the grid), so mant 2^e2 is a multiple of the smallest subnormal and
        /// ScaleB, whose only rounding is in a subnormal result, does not round: no second rounding.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static double ToDoubleRounded(BigInteger num, BigInteger den, int scaleExp)
        {
            if (num.IsZero)
                return 0;
            bool negative = num.Sign < 0;
            BigInteger a = negative ? -num : num;
            long nb = BitLength(a), db = BitLength(den);
            int shift = (int)(55 - (nb - db));      // the quotient has 55 or 56 bits
            long top = (long)scaleExp - shift + 56;     // the value is below 2^top
            if (top <= -1076)
                return negative ? -0.0 : 0.0;        // (below half the smallest subnormal; also avoids a huge shift below)
            // (q < 2^56 fits a ulong: the rounding below is integer arithmetic on 64 bits)
            BigInteger n2 = shift >= 0 ? a << shift : a, d2 = shift >= 0 ? den : den << -shift, qb = n2 / d2;
            ulong q = (ulong)qb;
            long extra = ((q >> 55) != 0 ? 56 : 55) - 53;
            // a result below 2^-1022 has fewer than 53 significant bits: round once, to the grid 2^-1074 (rounding to 53 bits first
            // and then to the grid would round twice)
            long unit = (long)scaleExp - shift + extra;       // (long: a scaleExp near int.MaxValue must not wrap)
            if (unit < -1074)
                extra += -1074 - unit;
            if (extra >= 57)     // (defensive: g = 57 and 58 give mant = 0 below as well; the guard keeps every shift count under 64)
                return negative ? -0.0 : 0.0;       // (q < 2^56 is below half a unit; underflow to zero keeps the sign, as IEEE division does)
            int g = (int)extra;
            ulong mant = q >> g, half = 1UL << (g - 1), low = q & ((1UL << g) - 1);
            // above the midpoint, or on it with a nonzero remainder (the quotient was truncated: q d2 != n2, only computed on a tie), or a tie with an odd mantissa
            if (low > half || (low == half && ((mant & 1) != 0 || (qb * d2 == n2) == false)))
                mant += 1;
            if (mant == 0)
                return negative ? -0.0 : 0.0;
            // (mant <= 2^53, the unit is a double: exact; an exponent beyond +-3000 gives the same infinity or zero as 3000)
            long e2 = (long)scaleExp - shift + g;
            double r = ScaleB((double)mant, (int)Math.Max(-3000L, Math.Min(3000L, e2)));
            return negative ? -r : r;
        }

        /// <summary> x * 2^n, correctly rounded (one rounding, only for a subnormal result) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static double ScaleB(double x, int n)
        {
            if (x == 0 || double.IsNaN(x) || double.IsInfinity(x))
                return x;
            long bits = BitConverter.DoubleToInt64Bits(x), e = n;
            if (((bits >> 52) & 0x7FF) == 0) {
                // subnormal: normalize (exact)
                bits = BitConverter.DoubleToInt64Bits(x * 18446744073709551616.0);    // 2^64
                e -= 64;
            }
            // x = m 2^k with m in [1, 2): the result is m 2^e
            e += ((bits >> 52) & 0x7FF) - 1023;
            long sign = bits & long.MinValue, frac = bits & 0xFFFFFFFFFFFFFL;
            if (e > 1023)
                return (sign != 0) ? double.NegativeInfinity : double.PositiveInfinity;
            if (e >= -1022)
                return BitConverter.Int64BitsToDouble(sign | ((e + 1023) << 52) | frac);
            if (e < -1080)
                return (sign != 0) ? -0.0 : 0.0;       // below half the smallest subnormal
            // subnormal result: m 2^-1022 (exact) times 2^(e + 1022) >= 2^-58, a single rounding
            double m = BitConverter.Int64BitsToDouble(sign | (1L << 52) | frac);
            return m * BitConverter.Int64BitsToDouble((e + 2045) << 52);
        }

        [MethodImpl(JitHints.Optimize)]
        public static long BitLength(BigInteger v)
        {
            if (v.Sign < 0)
                v = -v;
#if NET
            // (no allocation; the bit length of v >= 0)
            return v.GetBitLength();
#else
            return BitLengthPortable(v);
#endif
        }

        /// <summary> BitLength of v >= 0 for netstandard2.0 (one allocation; compiled everywhere, so that the tests on .NET check it) </summary>
        internal static long BitLengthPortable(BigInteger v)
        {
            byte[] bytes = v.ToByteArray();
            int top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0)
                top--;
            int b = bytes[top], bits = 0;
            while (b != 0) { bits++; b >>= 1; }
            return 8L * top + bits;
        }


        /// <summary>
        /// Decompose finite v into m * 2^e with m odd (or m = 0, e = 0). Exact.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static void Decompose(double v, out BigInteger m, out int e)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                throw new ArgumentException("ExactPoint3.Decompose: value is not finite");
            long bits = BitConverter.DoubleToInt64Bits(v);
            bool neg = bits < 0;
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0)
                exp = 1;
            else
                man |= 1L << 52;
            if (man == 0) {
                m = BigInteger.Zero; e = 0;
                return;
            }
            e = exp - 1075;
            while ((man & 1) == 0) {
                man >>= 1; e++;
            }
            m = neg ? -new BigInteger(man) : new BigInteger(man);
        }


        public static BigInteger Det3(BigInteger a, BigInteger b, BigInteger c,
                                      BigInteger d, BigInteger e, BigInteger f,
                                      BigInteger g, BigInteger h, BigInteger i)
        {
            return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        }
    }
}
