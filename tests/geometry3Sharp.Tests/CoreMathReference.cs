// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// High-precision reference for exp, log, sin, cos, atan2 and the fused multiply-add, built from System.Numerics.BigInteger
// only (fixed point with W = 1600 fractional bits); it shares no code with the vendored CORE-MATH port. The values are
// rounded to nearest-even double from the exact / 2^-500 accurate result; a result that lies closer than 2^-200
// quantum to a rounding boundary makes the reference throw instead of guessing.
using System;
using System.Numerics;

namespace g3.Tests
{
    static class CoreMathReference
    {
        const int W = 1600;
        static readonly BigInteger One = BigInteger.One << W;
        static readonly BigInteger Pi = ComputePi();
        static readonly BigInteger HalfPi = Pi >> 1;
        static readonly BigInteger Ln2 = ComputeLn2();

        // truncating towards zero (a floor would keep a negative series term at -1 forever)
        static BigInteger Mul(BigInteger a, BigInteger b) { BigInteger p = a * b; return p.Sign >= 0 ? p >> W : -((-p) >> W); }
        static BigInteger Div(BigInteger a, BigInteger b) { return (a << W) / b; }

        public static int BitLength(BigInteger a)
        {
            if (a.Sign < 0) a = -a;
            if (a.IsZero) return 0;
            byte[] b = a.ToByteArray();
            int top = b.Length - 1;
            while (top > 0 && b[top] == 0) --top;
            int bits = 0;
            for (int v = b[top]; v != 0; v >>= 1) ++bits;
            return top * 8 + bits;
        }

        static BigInteger ArcTanInv(int n, bool hyperbolic)
        {
            BigInteger term = One / n, sum = term;
            BigInteger n2 = (BigInteger)n * n;
            for (int k = 1; !term.IsZero; ++k) {
                term /= n2;
                BigInteger t = term / (2 * k + 1);
                sum += (hyperbolic || (k & 1) == 0) ? t : -t;
            }
            return sum;
        }

        static BigInteger ComputePi() { return 16 * ArcTanInv(5, false) - 4 * ArcTanInv(239, false); }
        static BigInteger ComputeLn2() { return 2 * ArcTanInv(3, true); }

        /// <summary> the double as an exact fixed point number (x * 2^W) </summary>
        static BigInteger FromDouble(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            bool neg = bits < 0;
            int e = (int)((bits >> 52) & 0x7ff);
            long m = bits & ((1L << 52) - 1);
            if (e == 0) e = 1; else m |= 1L << 52;
            BigInteger v = new BigInteger(m) << (e - 1075 + W);
            return neg ? -v : v;
        }

        static BigInteger RoundDiv(BigInteger a, BigInteger b)
        {
            // nearest integer to a / b for b > 0 (floor((2a + b) / 2b))
            BigInteger num = 2 * a + b, den = 2 * b;
            BigInteger q = BigInteger.DivRem(num, den, out BigInteger r);
            if (r.Sign < 0) q -= 1;
            return q;
        }

        static double Pow2(int k)
        {
            if (k >= -1022) return BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
            return BitConverter.Int64BitsToDouble(1L << (k + 1074));
        }

        /// <summary> v * 2^exp2 rounded to nearest, ties to even. checkMargin: throw if v is within 2^-200 quantum of a
        /// rounding boundary (the value is an approximation then). </summary>
        public static double RoundToDouble(BigInteger v, int exp2, bool checkMargin)
        {
            if (v.IsZero) return 0.0;
            bool neg = v.Sign < 0;
            BigInteger a = neg ? -v : v;
            int n = BitLength(a);
            int e = n - 1 + exp2;
            if (e > 1023) return neg ? double.NegativeInfinity : double.PositiveInfinity;
            int qe = Math.Max(e - 52, -1074);
            int s = qe - exp2;
            BigInteger m;
            if (s <= 0) m = a << -s;
            else {
                m = a >> s;
                BigInteger rem = a - (m << s), half = BigInteger.One << (s - 1);
                BigInteger diff = BigInteger.Abs(rem - half);
                if (checkMargin && diff <= (half >> 200))
                    throw new InvalidOperationException("reference too close to a rounding boundary");
                if (rem > half || (rem == half && !m.IsEven)) m += 1;
            }
            double d = (double)m;       // m <= 2^53: exact
            double r = d * Pow2(qe);    // exact, or overflow to infinity at 2^1024
            return neg ? -r : r;
        }

        // ------------------------------------------------------------------------------------------ exp

        public static double Exp(double x)
        {
            if (double.IsNaN(x)) return double.NaN;
            if (double.IsPositiveInfinity(x)) return double.PositiveInfinity;
            if (double.IsNegativeInfinity(x)) return 0.0;
            if (x == 0) return 1.0;
            if (x > 710) return double.PositiveInfinity;
            if (x < -746) return 0.0;
            BigInteger fx = FromDouble(x);
            BigInteger k = RoundDiv(fx, Ln2);
            BigInteger r = (fx - k * Ln2) >> 10;
            BigInteger term = One, sum = One;
            for (int n = 1; !term.IsZero; ++n) { term = Mul(term, r) / n; sum += term; }
            for (int i = 0; i < 10; ++i) sum = Mul(sum, sum);
            return RoundToDouble(sum, (int)k - W, true);
        }

        // ------------------------------------------------------------------------------------------ log

        public static double Log(double x)
        {
            if (double.IsNaN(x) || x < 0) return double.NaN;
            if (x == 0) return double.NegativeInfinity;
            if (double.IsPositiveInfinity(x)) return double.PositiveInfinity;
            if (x == 1) return 0.0;
            BigInteger fx = FromDouble(x);
            int e = BitLength(fx) - 1 - W;            // 2^e <= x < 2^(e+1)
            BigInteger m = e >= 0 ? fx >> e : fx << -e;
            if (m * 2 >= 3 * One) { ++e; m = e > 0 ? fx >> e : fx << -e; }   // m in [0.75, 1.5)
            BigInteger z = Div(m - One, m + One), z2 = Mul(z, z);
            BigInteger term = z, sum = z;
            for (int k = 1; !term.IsZero; ++k) { term = Mul(term, z2); sum += term / (2 * k + 1); }
            BigInteger result = e * Ln2 + 2 * sum;
            return RoundToDouble(result, -W, true);
        }

        // ------------------------------------------------------------------------------------------ sin, cos

        static void SinCosFixed(BigInteger x, out BigInteger sin, out BigInteger cos)
        {
            BigInteger q = RoundDiv(x, HalfPi);
            BigInteger r = x - q * HalfPi, r2 = Mul(r, r);
            BigInteger st = r, ss = r;
            for (int k = 1; !st.IsZero; ++k) { st = -Mul(st, r2) / ((2 * k) * (2 * k + 1)); ss += st; }
            BigInteger ct = One, cs = One;
            for (int k = 1; !ct.IsZero; ++k) { ct = -Mul(ct, r2) / ((2 * k - 1) * (2 * k)); cs += ct; }
            int quad = (int)(((q % 4) + 4) % 4);
            switch (quad) {
                case 0: sin = ss; cos = cs; break;
                case 1: sin = cs; cos = -ss; break;
                case 2: sin = -ss; cos = -cs; break;
                default: sin = -cs; cos = ss; break;
            }
        }

        public static double Sin(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return double.NaN;
            if (x == 0) return x;
            SinCosFixed(FromDouble(x), out BigInteger s, out BigInteger c);
            return RoundToDouble(s, -W, true);
        }

        public static double Cos(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return double.NaN;
            if (x == 0) return 1.0;
            SinCosFixed(FromDouble(x), out BigInteger s, out BigInteger c);
            return RoundToDouble(c, -W, true);
        }

        // ------------------------------------------------------------------------------------------ atan2

        static BigInteger ISqrt(BigInteger n)
        {
            if (n.IsZero) return n;
            BigInteger x = BigInteger.One << ((BitLength(n) + 1) / 2);
            while (true) {
                BigInteger y = (x + n / x) >> 1;
                if (y >= x) return x;
                x = y;
            }
        }

        static BigInteger AtanFixed(BigInteger t)      // t >= 0
        {
            if (t > One) return HalfPi - AtanFixed(t.IsZero ? t : (One << W) / t);
            for (int i = 0; i < 3; ++i) {
                BigInteger root = ISqrt((One + Mul(t, t)) << W);
                t = Div(t, One + root);
            }
            BigInteger t2 = Mul(t, t), term = t, sum = t;
            for (int k = 1; !term.IsZero; ++k) {
                term = -Mul(term, t2);
                sum += term / (2 * k + 1);
            }
            return sum * 8;
        }

        public static double Atan2(double y, double x)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
            bool negY = BitConverter.DoubleToInt64Bits(y) < 0, negX = BitConverter.DoubleToInt64Bits(x) < 0;
            double r;
            if (y == 0) r = negX ? Math.PI : 0.0;
            else if (x == 0) r = Math.PI / 2;                       // (the double nearest pi / 2 is exactly Math.PI / 2)
            else if (double.IsInfinity(y) && double.IsInfinity(x)) r = negX ? BitConverter.Int64BitsToDouble(0x4002D97C7F3321D2L) : Math.PI / 4;
            else if (double.IsInfinity(y)) r = Math.PI / 2;
            else if (double.IsInfinity(x)) r = negX ? Math.PI : 0.0;
            else {
                BigInteger t = Div(BigInteger.Abs(FromDouble(y)), BigInteger.Abs(FromDouble(x)));
                BigInteger angle = AtanFixed(t);
                if (negX) angle = Pi - angle;
                r = RoundToDouble(angle, -W, true);
                if (r == 0) r = 0.0;
            }
            return negY ? -r : r;
        }

        // ------------------------------------------------------------------------------------------ fma

        static void Decompose(double x, out BigInteger m, out int e)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int ex = (int)((bits >> 52) & 0x7ff);
            long mant = bits & ((1L << 52) - 1);
            if (ex == 0) ex = 1; else mant |= 1L << 52;
            m = bits < 0 ? -mant : mant;
            e = ex - 1075;
        }

        /// <summary> x * y + z rounded once to nearest even, exact (ties included) </summary>
        public static double Fma(double x, double y, double z)
        {
            bool special = double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z) || double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(z);
            if (special) return x * y + z;                    // the IEEE result of the product is exact for these
            if (x == 0 || y == 0) return x * y + z;           // the product is an exact zero
            Decompose(x, out BigInteger mx, out int ex);
            Decompose(y, out BigInteger my, out int ey);
            BigInteger p = mx * my;
            int ep = ex + ey;
            BigInteger sum;
            int e;
            if (z == 0) { sum = p; e = ep; }
            else {
                Decompose(z, out BigInteger mz, out int ez);
                e = Math.Min(ep, ez);
                sum = (p << (ep - e)) + (mz << (ez - e));
            }
            if (sum.IsZero) return 0.0;                       // exact cancellation: +0 in round to nearest
            return RoundToDouble(sum, e, false);
        }
    }
}
