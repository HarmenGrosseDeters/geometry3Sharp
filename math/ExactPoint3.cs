// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Numerics;

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
        public static ExactPoint3 CreateUnreduced(BigInteger x, BigInteger y, BigInteger z, BigInteger w)
        {
            if (w.IsZero)
                throw new ArgumentException("ExactPoint3: W is zero");
            return (w.Sign < 0) ? new ExactPoint3(-x, -y, -z, -w) : new ExactPoint3(x, y, z, w);
        }

        public BigInteger this[int i] {
            get { return (i == 0) ? X : (i == 1) ? Y : Z; }
        }

        public bool Equals(ExactPoint3 o)
        {
            return W == o.W && X == o.X && Y == o.Y && Z == o.Z;
        }
        public override bool Equals(object obj)
        {
            return obj is ExactPoint3 && Equals((ExactPoint3)obj);
        }
        public override int GetHashCode()
        {
            unchecked {
                int h = X.GetHashCode();
                h = h * 31 + Y.GetHashCode();
                h = h * 31 + Z.GetHashCode();
                return h * 31 + W.GetHashCode();
            }
        }

        /// <summary> coordinates as doubles, multiplied by 2^scaleExp (nearly correctly rounded: error below 1 ulp) </summary>
        public Vector3d ToVector3d(int scaleExp = 0)
        {
            return new Vector3d(ToDouble(X, W, scaleExp), ToDouble(Y, W, scaleExp), ToDouble(Z, W, scaleExp));
        }

        public override string ToString()
        {
            return ToVector3d().ToString();
        }


        /// <summary>
        /// Fast approximation of the coordinates (relative error below 4 * 2^-52): for floating-point filters
        /// </summary>
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

        /// <summary> num / den * 2^scaleExp as double, den > 0 </summary>
        public static double ToDouble(BigInteger num, BigInteger den, int scaleExp)
        {
            if (num.IsZero)
                return 0;
            // quotient with 64 significant bits, then scale
            long nb = BitLength(num), db = BitLength(den);
            int shift = (int)(64 - (nb - db));
            BigInteger q = (shift >= 0) ? (num << shift) / den : num / (den << -shift);
            return ScaleB((double)q, scaleExp - shift);
        }

        /// <summary> x * 2^n without intermediate overflow or underflow </summary>
        public static double ScaleB(double x, int n)
        {
            while (n > 1000) { x *= 8.98846567431158e307; n -= 1023; }       // 2^1023
            while (n < -1000) { x *= 2.2250738585072014e-308; n += 1022; }   // 2^-1022
            return x * BitConverter.Int64BitsToDouble((long)(n + 1023) << 52);
        }

        public static long BitLength(BigInteger v)
        {
            if (v.Sign < 0)
                v = -v;
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
