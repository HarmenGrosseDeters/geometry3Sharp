// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// A value as an unevaluated sum of two doubles, Hi + Lo (|Lo| &lt;= 2^-53 |Hi|), with a certified bound Err of its
    /// distance to the exact value it stands for: the exact value lies in [Hi + Lo - Err, Hi + Lo + Err]. Used to certify
    /// the correct rounding of an intersection point (IntrTriangle3Triangle3.Find) with a fallback to exact arithmetic
    /// where the bound cannot decide. The derivation of every bound is in docs/design/TRI3TRI3_DOUBLE_WORD.md
    /// (section numbers in the comments). No FMA, no float: only error-free transforms (TwoSum, Dekker's TwoProduct)
    /// and plain double operations.
    /// <para>
    /// Range rule: every Hi is 0 or in [2^-480, 2^480], else Err is +infinity (not certified): the transforms are exact
    /// without underflow or overflow (the Dekker split needs |x| &lt; 2^996, products of two such values stay above 2^-960),
    /// and the relative bounds of the operations assume no underflow. An infinite Err propagates through every operation.
    /// </para>
    /// </summary>
    internal readonly struct DoubleWord
    {
        public readonly double Hi, Lo, Err;

        /// <summary> 2^-53 </summary>
        internal const double U = 1.1102230246251565e-16;
        /// <summary>
        /// 2^-100 = 64 u^2: covers the derived error of the operations (section 3: at most 3.4 u^2 S for a sum, 10 u^2 P for a
        /// product) 6.4 times or more, which also absorbs the rounding of the arithmetic on the bounds (section 2: at most 12
        /// operations of non-negative numbers, a factor of at most 1 - 12 u below the real value, and 6.4 (1 - 12 u) &gt; 1)
        /// </summary>
        internal const double Eps2 = 7.888609052210118e-31;
        /// <summary>
        /// 1 + 2^-48 = 1 + 32 u: applied to the final Err of every operation, so that the rounding of the arithmetic that computes
        /// Err cannot make it smaller than the real bound, for any of its terms (the operand errors too). The roundings on non-negative
        /// numbers, each by a factor within 1 +- u: a sum 5 (|xh| + |yh|, Eps2 times it, the sum of the three terms), a product 11
        /// (Mag twice, four products of an error with a magnitude, Eps2 times |xh yh| with the product, the sums), a quotient 6 (res.Mag,
        /// + res.Err, the denominator twice, the division, ErrUp; res.Err carries its own factor). At most 12, so Err is rounded
        /// down by at most a factor 1 - 12 u
        /// </summary>
        /// <summary>
        /// 2^-1000: a bound that is not 0 but below this (an underflow, with a subnormal's large relative error) is raised to it when an operand
        /// error is not 0: it stays far below any gap that matters (2^-1000 times the largest scale 2^960 of the range rule is 2^-40 absolute)
        /// and keeps the relative error of the bound valid in the later operations
        /// </summary>
        const double ErrFloor = 9.332636185032189e-302;
        const double ErrUp = 1.0000000000000035527136788005;
        const double MinMagBits = 3.2033329522929615e-145;     // 2^-480
        const double MaxMagBits = 3.1217485503159922e144;      // 2^480

        public DoubleWord(double hi, double lo, double err) { Hi = hi; Lo = lo; Err = err; }

        /// <summary> the exact value x </summary>
        public static DoubleWord Of(double x) { return Checked(x, 0, 0); }

        /// <summary> the exact difference a - b (TwoSum, exact) </summary>
        public static DoubleWord Diff(double a, double b)
        {
            double s = a - b, bb = s - a, e = (a - (s - bb)) + (-b - bb);
            return Checked(s, e, 0);
        }

        /// <summary> not certified at all (infinite error) </summary>
        public static readonly DoubleWord Invalid = new DoubleWord(0, 0, double.PositiveInfinity);

        public bool IsValid { get { return Err < double.PositiveInfinity; } }

        /// <summary> a bound of |value| </summary>
        public double Mag { get { return Math.Abs(Hi) + Math.Abs(Lo); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static DoubleWord Checked(double hi, double lo, double err)
        {
            double a = Math.Abs(hi);
            // (NaN and infinities fail both comparisons)
            if ((a == 0 || (a >= MinMagBits && a <= MaxMagBits)) && err < double.PositiveInfinity)
                return new DoubleWord(hi, lo, err);
            return Invalid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void TwoSum(double a, double b, out double s, out double e)
        {
            s = a + b;
            double bb = s - a;
            e = (a - (s - bb)) + (b - bb);
        }

        public static DoubleWord operator -(DoubleWord x) { return new DoubleWord(-x.Hi, -x.Lo, x.Err); }

        /// <summary>
        /// x + y. Section 3.1: sh + sl = xh + yh, th + tl = xl + yl (exact), c = fl(sl + th), vh + vl = sh + c (exact),
        /// w = fl(tl + vl), zh + zl = vh + w (exact). The error is that of c and w, at most 3.4 u^2 (|xh| + |yh|) for
        /// normalized inputs; 2^-100 (|xh| + |yh|) is more than 18 times that.
        /// </summary>
        public static DoubleWord operator +(DoubleWord x, DoubleWord y)
        {
            double sh, sl, th, tl, vh, vl, zh, zl;
            TwoSum(x.Hi, y.Hi, out sh, out sl);
            TwoSum(x.Lo, y.Lo, out th, out tl);
            double c = sl + th;
            TwoSum(sh, c, out vh, out vl);
            double w = tl + vl;
            TwoSum(vh, w, out zh, out zl);
            double err = (x.Err + y.Err + Eps2 * (Math.Abs(x.Hi) + Math.Abs(y.Hi))) * ErrUp;
            return Checked(zh, zl, err);
        }

        public static DoubleWord operator -(DoubleWord x, DoubleWord y) { return x + (-y); }

        /// <summary>
        /// x y. Section 3.2: ch + cl1 = xh yh (exact, Dekker), t = fl(fl(fl(xh yl) + fl(xl yh)) + fl(xl yl)),
        /// cl3 = fl(cl1 + t), zh + zl = ch + cl3 (exact). The error of the rounded cross terms is at most 12 u^2 |xh yh|;
        /// 2^-100 |xh yh| is more than 5 times that. The operand errors propagate as |x| err(y) + |y| err(x) + err(x) err(y).
        /// </summary>
        public static DoubleWord operator *(DoubleWord x, DoubleWord y)
        {
            double ch, cl1;
            Expansion.TwoProduct(x.Hi, y.Hi, out ch, out cl1);
            double t = x.Hi * y.Lo + x.Lo * y.Hi;
            t += x.Lo * y.Lo;
            double cl3 = cl1 + t;
            double zh, zl;
            TwoSum(ch, cl3, out zh, out zl);
            double ax = x.Mag, ay = y.Mag;
            double err = (ax * y.Err + ay * x.Err + x.Err * y.Err + Eps2 * Math.Abs(x.Hi * y.Hi)) * ErrUp;
            // (an operand error that is not 0 must not vanish, or shrink into the subnormals, into a bound that a later operation scales up
            // to something far too small: the bound is at least ErrFloor)
            if (err < ErrFloor && (x.Err > 0 || y.Err > 0))
                err = ErrFloor;
            // (the product of the heads may be below the range even if both are in it: Checked)
            return Checked(zh, zl, err);
        }

        /// <summary>
        /// x / y by two corrections, certified by the residual (section 3.3): q1 = fl(xh / yh), q = q1 + fl(rh / yh) with
        /// r = x - q1 y, then the exact identity x'/y' - q = (x' - q y') / y' gives the error from a certified residual
        /// x - q y: |x'/y' - q| &lt;= (|x - q y| + err(x - q y)) / (|yh| - |yl| - err(y)), where err(x - q y) already contains err(x) and |q| err(y). Requires
        /// that denominator to be positive, else Invalid. The quotient's own rounding is not assumed to be small: it only costs a larger bound.
        /// </summary>
        public static DoubleWord Div(DoubleWord x, DoubleWord y)
        {
            if (y.IsValid == false || x.IsValid == false)
                return Invalid;
            double q1 = x.Hi / y.Hi;
            DoubleWord r = x - y * Of(q1);
            if (r.IsValid == false)
                return Invalid;
            double q2 = r.Hi / y.Hi;
            double qh, ql;
            TwoSum(q1, q2, out qh, out ql);
            DoubleWord q = Checked(qh, ql, 0);
            if (q.IsValid == false)
                return Invalid;
            DoubleWord res = x - y * q;
            if (res.IsValid == false)
                return Invalid;
            // (y.Err up to a quarter of |yh|: then the denominator |yh| - |yl| - err(y) is at least 0.7 |yh| and its rounding, at most 2.5 u
            // relative (u + u / 0.7), is covered by ErrUp; a larger error is not certified)
            if (y.Err > 0.25 * Math.Abs(y.Hi))
                return Invalid;
            double den = Math.Abs(y.Hi) - Math.Abs(y.Lo) - y.Err;
            if (den <= 0)
                return Invalid;
            // (res.Err already carries the errors of x and of y q, so x.Err and |q| y.Err are not added again)
            double err = (res.Mag + res.Err) / den * ErrUp;
            // (the quotient of a tiny error by a large denominator can underflow too: the same floor)
            // (res.Err is not 0 exactly when an operand error is not 0: x.Err is part of it, and a y.Err that underflowed in the product is floored there)
            if (err < ErrFloor && res.Err > 0)
                err = ErrFloor;
            return Checked(qh, ql, err);
        }

        /// <summary>
        /// True if Hi is the correctly rounded (to nearest) double of every value within Err of Hi + Lo: |Lo| + Err &lt; half the
        /// spacing of the doubles just below |Hi|, or the value is an exact 0 (section 4). A tie, or a value within the error of
        /// a midpoint, is not certified.
        /// </summary>
        public bool RoundsTo(out double rounded)
        {
            rounded = Hi;
            if (IsValid == false)
                return false;
            // an exact zero (no error, as the product of an exact zero, eg the difference of equal coordinates): certified,
            // as +0 (a product of an exact zero and a negative value is -0)
            if (Hi == 0) {
                rounded = 0.0;
                return Lo == 0 && Err == 0;
            }
            double a = Math.Abs(Hi);
            long bits = BitConverter.DoubleToInt64Bits(a);
            // spacing above a: 2^(e - 52); just below a: the same, or half of it if a is a power of two
            double above = BitConverter.Int64BitsToDouble(bits + 1) - a;
            double below = ((bits & 0xFFFFFFFFFFFFFL) == 0) ? above * 0.5 : above;
            return Math.Abs(Lo) + Err < 0.5 * below;
        }
    }
}
