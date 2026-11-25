// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// A one-dimensional first-order Taylor model (affine form) over u in [-1, 1]: every enclosed value lies in
    /// [C0 + C1 * u - Rad, C0 + C1 * u + Rad] for the same parameter u in [-1, 1] tied to one coordinate axis.
    /// Outwardly rounded so every enclosure is sound across normal, subnormal, zero and non-finite inputs.
    /// </summary>
    internal readonly struct Affine1
    {
        public readonly double C0, C1, Rad;

        /// <summary> Largest phase half-width for which SinCos builds a linear Taylor model; wider boxes fall back to IntervalMath.SinCos. </summary>
        internal const double MaxTaylorHalf = 1.2;

        public static readonly Affine1 Unbounded = new Affine1(0, 0, double.PositiveInfinity);

        /// <summary> Precondition: either rad is +Infinity (Unbounded) or c0, c1, rad are all finite with rad >= 0. </summary>
        public Affine1(double c0, double c1, double rad)
        {
            C0 = c0; C1 = c1; Rad = rad;
        }

        public bool IsFinite
        {
            [MethodImpl(JitHints.Optimize)]
            get { return Rad >= 0 && !double.IsInfinity(Rad); }
        }

        /// <summary> Constant affine form enclosing iv (zero slope, independent of u). </summary>
        [MethodImpl(JitHints.Optimize)]
        public static Affine1 FromInterval(ExactInterval iv)
        {
            if (!Affine3.IsFiniteDouble(iv.Lo) || !Affine3.IsFiniteDouble(iv.Hi) || !(iv.Lo <= iv.Hi))
                return Unbounded;
            double m, r;
            MidRad(iv, out m, out r);
            if (!Affine3.IsFiniteDouble(m) || !Affine3.IsFiniteDouble(r))
                return Unbounded;
            return new Affine1(m, 0, r);
        }

        /// <summary> Collapse the linear term into Rad so the form holds for any u in [-1, 1]. </summary>
        [MethodImpl(JitHints.Optimize)]
        public Affine1 ConstantOnly()
        {
            if (!IsFinite) return Unbounded;
            if (C1 == 0) return this;
            double r = ExactInterval.Up(Rad + Math.Abs(C1));
            if (!Affine3.IsFiniteDouble(r)) return Unbounded;
            return new Affine1(C0, 0, r);
        }

        /// <summary> Lift this 1D form onto coordinate axis 0 (x), 1 (y) or 2 (z) of an Affine3. </summary>
        [MethodImpl(JitHints.Optimize)]
        public Affine3 OnAxis(int axis)
        {
            if (!IsFinite) return Affine3.Unbounded;
            return axis == 0 ? new Affine3(C0, C1, 0, 0, Rad)
                 : axis == 1 ? new Affine3(C0, 0, C1, 0, Rad)
                 : new Affine3(C0, 0, 0, C1, Rad);
        }

        /// <summary> Interval hull of this affine form over u in [-1, 1]. </summary>
        [MethodImpl(JitHints.Optimize)]
        public ExactInterval ToInterval()
        {
            if (!IsFinite || !Affine3.IsFiniteDouble(C0) || !Affine3.IsFiniteDouble(C1)) return Affine3.Whole;
            double ex = C1 == 0 ? Rad : ExactInterval.Up(Math.Abs(C1) + Rad);
            if (!Affine3.IsFiniteDouble(ex)) return Affine3.Whole;
            if (ex == 0) return new ExactInterval(C0, C0);
            return new ExactInterval(ExactInterval.Down(C0 - ex), ExactInterval.Up(C0 + ex));
        }

        // Exact-enclosing midpoint and half-width of a finite interval [lo, hi] with lo <= hi:
        // for every real t in [lo, hi], |t - mid| <= rad in exact reals.
        [MethodImpl(JitHints.Optimize)]
        internal static void MidRad(ExactInterval iv, out double mid, out double rad)
        {
            if (iv.Lo == iv.Hi) { mid = iv.Lo; rad = 0; return; }
            mid = 0.5 * iv.Lo + 0.5 * iv.Hi;
            rad = Math.Max(ExactInterval.Up(mid - iv.Lo), ExactInterval.Up(iv.Hi - mid));
        }

        /// <summary>
        /// Correlated affine enclosures of sin(theta) and cos(theta) for theta in p, parameterized by
        /// theta = mid + half * u with u in [-1, 1]. Uses one center evaluation of IntervalMath.SinCos(mid)
        /// plus a shifted second-order Taylor remainder (exact via ExactInterval) when half &lt;= MaxTaylorHalf,
        /// and falls back to constant enclosures from IntervalMath.SinCos(p) on wide or out-of-range boxes.
        /// </summary>
        public static void SinCos(ExactInterval p, out Affine1 sin, out Affine1 cos, out double mid, out double half)
        {
            if (!Affine3.IsFiniteDouble(p.Lo) || !Affine3.IsFiniteDouble(p.Hi) || !(p.Lo <= p.Hi)) {
                mid = double.NaN; half = double.PositiveInfinity;
                sin = new Affine1(0, 0, 1); cos = new Affine1(0, 0, 1);
                return;
            }
            MidRad(p, out mid, out half);
            if (half == 0) {
                ExactInterval sm, cm;
                IntervalMath.SinCos(mid, out sm, out cm);
                sin = FromInterval(sm); cos = FromInterval(cm);
                return;
            }
            if (!(half <= MaxTaylorHalf) || !(Math.Abs(mid) <= 1073741824.0)) {
                ExactInterval sIv, cIv;
                IntervalMath.SinCos(p, out sIv, out cIv);
                sin = FromInterval(sIv); cos = FromInterval(cIv);
                return;
            }
            ExactInterval sMid, cMid;
            IntervalMath.SinCos(mid, out sMid, out cMid);
            ExactInterval h = ExactInterval.Of(half);
            ExactInterval h2 = ExactInterval.Square(h);
            ExactInterval q = ExactInterval.Of(0.25) * h2;
            ExactInterval oneMinusQ = ExactInterval.Of(1.0) - q;
            double r3 = (h2 * h / ExactInterval.Of(6.0)).Hi;
            sin = makeTrigAffine(sMid, cMid, h, q, oneMinusQ, r3);
            cos = makeTrigAffine(cMid, -sMid, h, q, oneMinusQ, r3);
        }

        // For f in {sin, cos} with f'' = -f and |f'''| <= 1:
        //   f(m + h u) = f(m)(1 - h^2/4) + (f'(m) h) u - (f(m) h^2/4)(2 u^2 - 1) + (f'''(xi) h^3/6) u^3
        // where |2 u^2 - 1| <= 1 and |u| <= 1. Every intermediate bound is computed in ExactInterval.
        static Affine1 makeTrigAffine(ExactInterval f0, ExactInterval f1, ExactInterval h, ExactInterval q, ExactInterval oneMinusQ, double r3)
        {
            double c0, e0, c1, e1;
            MidRad(f0 * oneMinusQ, out c0, out e0);
            MidRad(f1 * h, out c1, out e1);
            double f0Max = Math.Max(Math.Abs(f0.Lo), Math.Abs(f0.Hi));
            double r2 = (ExactInterval.Of(f0Max) * q).Hi;
            double rad = (ExactInterval.Of(e0) + ExactInterval.Of(e1) + ExactInterval.Of(r2) + ExactInterval.Of(r3)).Hi;
            if (!Affine3.IsFiniteDouble(c0) || !Affine3.IsFiniteDouble(c1) || !Affine3.IsFiniteDouble(rad))
                return new Affine1(0, 0, 1);
            return new Affine1(c0, c1, rad);
        }
    }

    /// <summary>
    /// A three-dimensional first-order Taylor model (affine form) over (ux, uy, uz) in [-1, 1]^3:
    /// every enclosed value lies in C0 + Cx*ux + Cy*uy + Cz*uz + [-Rad, +Rad].
    /// Tracks linear dependencies across x, y and z so cancellations reduce enclosure error from O(h) to O(h^2),
    /// with outward rounding on every operation (Comba &amp; Stolfi 1993; de Figueiredo &amp; Stolfi 1997; Makino &amp; Berz 2003).
    /// </summary>
    internal readonly struct Affine3
    {
        public readonly double C0, Cx, Cy, Cz, Rad;

        internal static readonly ExactInterval Whole = new ExactInterval(double.NegativeInfinity, double.PositiveInfinity);
        public static readonly Affine3 Unbounded = new Affine3(0, 0, 0, 0, double.PositiveInfinity);

        // Single-operation rounding error upper bound: for any finite double v = fl(z),
        // |v - z| <= 2^-52 * |v| + 2^-1073 (covers normal, tiny-normal, subnormal, zero, and x87 double rounding).
        const double U2 = 2.2204460492503131e-16;          // 2^-52 (exact power of 2)
        const double SubErr = 9.881312916824931e-324;      // 2 * double.Epsilon = 2^-1073
        // Upper bound factor for summing up to k non-negative doubles in round-to-nearest:
        // exact sum <= Up(fl_sum * SafeSumK) for k <= 4, 8, 16 respectively.
        const double SafeSum4 = 1.0 + 4e-15;
        const double SafeSum8 = 1.0 + 4e-15;
        const double SafeSum16 = 1.0 + 8e-15;

        /// <summary> Precondition: either rad is +Infinity (Unbounded) or c0, cx, cy, cz, rad are all finite with rad >= 0. </summary>
        public Affine3(double c0, double cx, double cy, double cz, double rad)
        {
            C0 = c0; Cx = cx; Cy = cy; Cz = cz; Rad = rad;
        }

        public bool IsFinite
        {
            [MethodImpl(JitHints.Optimize)]
            get { return Rad >= 0 && !double.IsInfinity(Rad); }
        }

        [MethodImpl(JitHints.Optimize)]
        internal static bool IsFiniteDouble(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }

        [MethodImpl(JitHints.Optimize)]
        static double roundErr(double v) { return U2 * Math.Abs(v) + SubErr; }

        [MethodImpl(JitHints.Optimize)]
        static double mulUp(double a, double b)
        {
            return (a == 0 || b == 0) ? 0 : ExactInterval.Up(a * b);
        }

        /// <summary> Exact constant double. </summary>
        [MethodImpl(JitHints.Optimize)]
        public static Affine3 Of(double c)
        {
            return IsFiniteDouble(c) ? new Affine3(c, 0, 0, 0, 0) : Unbounded;
        }

        /// <summary> Constant affine form enclosing an ExactInterval (no linear correlation). </summary>
        [MethodImpl(JitHints.Optimize)]
        public static Affine3 FromInterval(ExactInterval iv)
        {
            if (!IsFiniteDouble(iv.Lo) || !IsFiniteDouble(iv.Hi) || !(iv.Lo <= iv.Hi))
                return Unbounded;
            double m, r;
            Affine1.MidRad(iv, out m, out r);
            if (!IsFiniteDouble(m) || !IsFiniteDouble(r))
                return Unbounded;
            return new Affine3(m, 0, 0, 0, r);
        }

        /// <summary> Exact affine parameterization of coordinate interval iv along axis 0 (x), 1 (y) or 2 (z). </summary>
        [MethodImpl(JitHints.Optimize)]
        public static Affine3 FromAxis(ExactInterval iv, int axis)
        {
            if (!IsFiniteDouble(iv.Lo) || !IsFiniteDouble(iv.Hi) || !(iv.Lo <= iv.Hi))
                return Unbounded;
            double m, h;
            Affine1.MidRad(iv, out m, out h);
            if (!IsFiniteDouble(m) || !IsFiniteDouble(h))
                return Unbounded;
            return axis == 0 ? new Affine3(m, h, 0, 0, 0)
                 : axis == 1 ? new Affine3(m, 0, h, 0, 0)
                 : new Affine3(m, 0, 0, h, 0);
        }

        /// <summary> Outwardly rounded upper bound on |Cx| + |Cy| + |Cz| + Rad. </summary>
        [MethodImpl(JitHints.Optimize)]
        public double Excursion()
        {
            if (!IsFinite) return double.PositiveInfinity;
            double s = Math.Abs(Cx) + Math.Abs(Cy) + Math.Abs(Cz) + Rad;
            return s == 0 ? 0 : ExactInterval.Up(s * SafeSum4);
        }

        /// <summary> Outwardly rounded interval hull over [-1, 1]^3. </summary>
        [MethodImpl(JitHints.Optimize)]
        public ExactInterval ToInterval()
        {
            if (!IsFinite || !IsFiniteDouble(C0) || !IsFiniteDouble(Cx) || !IsFiniteDouble(Cy) || !IsFiniteDouble(Cz))
                return Whole;
            double ex = Excursion();
            if (!IsFiniteDouble(ex)) return Whole;
            if (ex == 0) return new ExactInterval(C0, C0);
            return new ExactInterval(ExactInterval.Down(C0 - ex), ExactInterval.Up(C0 + ex));
        }

        [MethodImpl(JitHints.Optimize)]
        public static Affine3 operator -(Affine3 a)
        {
            return a.IsFinite ? new Affine3(-a.C0, -a.Cx, -a.Cy, -a.Cz, a.Rad) : Unbounded;
        }

        [MethodImpl(JitHints.Optimize)]
        public static Affine3 operator +(Affine3 a, Affine3 b)
        {
            if (!a.IsFinite || !b.IsFinite) return Unbounded;
            double c0 = a.C0 + b.C0, cx = a.Cx + b.Cx, cy = a.Cy + b.Cy, cz = a.Cz + b.Cz;
            if (!IsFiniteDouble(c0) || !IsFiniteDouble(cx) || !IsFiniteDouble(cy) || !IsFiniteDouble(cz))
                return Unbounded;
            double rSum = a.Rad + b.Rad + roundErr(c0) + roundErr(cx) + roundErr(cy) + roundErr(cz);
            double rad = rSum == 0 ? 0 : ExactInterval.Up(rSum * SafeSum8);
            return IsFiniteDouble(rad) ? new Affine3(c0, cx, cy, cz, rad) : Unbounded;
        }

        [MethodImpl(JitHints.Optimize)]
        public static Affine3 operator -(Affine3 a, Affine3 b)
        {
            return a + (-b);
        }

        /// <summary> Add an exact double constant d to this affine form. </summary>
        [MethodImpl(JitHints.Optimize)]
        public Affine3 AddConst(double d)
        {
            if (!IsFinite || !IsFiniteDouble(d)) return Unbounded;
            double c0 = C0 + d;
            if (!IsFiniteDouble(c0)) return Unbounded;
            double rSum = Rad + roundErr(c0);
            double rad = rSum == 0 ? 0 : ExactInterval.Up(rSum * SafeSum4);
            return IsFiniteDouble(rad) ? new Affine3(c0, Cx, Cy, Cz, rad) : Unbounded;
        }

        /// <summary> Multiply this affine form by an exact double scalar s. </summary>
        [MethodImpl(JitHints.Optimize)]
        public Affine3 Scale(double s)
        {
            if (!IsFinite || !IsFiniteDouble(s)) return Unbounded;
            double c0 = C0 * s, cx = Cx * s, cy = Cy * s, cz = Cz * s;
            if (!IsFiniteDouble(c0) || !IsFiniteDouble(cx) || !IsFiniteDouble(cy) || !IsFiniteDouble(cz))
                return Unbounded;
            double rScaled = mulUp(Rad, Math.Abs(s));
            double rSum = rScaled + roundErr(c0) + roundErr(cx) + roundErr(cy) + roundErr(cz);
            double rad = rSum == 0 ? 0 : ExactInterval.Up(rSum * SafeSum8);
            return IsFiniteDouble(rad) ? new Affine3(c0, cx, cy, cz, rad) : Unbounded;
        }

        [MethodImpl(JitHints.Optimize)]
        public static Affine3 operator *(Affine3 a, Affine3 b)
        {
            if (!a.IsFinite || !b.IsFinite) return Unbounded;
            double c0 = a.C0 * b.C0;
            double px1 = a.C0 * b.Cx, px2 = b.C0 * a.Cx, cx = px1 + px2;
            double py1 = a.C0 * b.Cy, py2 = b.C0 * a.Cy, cy = py1 + py2;
            double pz1 = a.C0 * b.Cz, pz2 = b.C0 * a.Cz, cz = pz1 + pz2;
            if (!IsFiniteDouble(c0) || !IsFiniteDouble(cx) || !IsFiniteDouble(cy) || !IsFiniteDouble(cz))
                return Unbounded;
            double la = a.Excursion(), lb = b.Excursion();
            double nl = mulUp(la, lb);
            double rA = mulUp(Math.Abs(b.C0), a.Rad);
            double rB = mulUp(Math.Abs(a.C0), b.Rad);
            double err = roundErr(c0)
                + (roundErr(px1) + roundErr(px2) + roundErr(cx))
                + (roundErr(py1) + roundErr(py2) + roundErr(cy))
                + (roundErr(pz1) + roundErr(pz2) + roundErr(cz));
            double rSum = nl + rA + rB + err;
            double rad = rSum == 0 ? 0 : ExactInterval.Up(rSum * SafeSum16);
            return IsFiniteDouble(rad) ? new Affine3(c0, cx, cy, cz, rad) : Unbounded;
        }

        /// <summary>
        /// Square a^2 with Chebyshev shift on the non-negative quadratic remainder delta^2 in [0, L^2],
        /// halving the quadratic bloat relative to a * a.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static Affine3 Square(Affine3 a)
        {
            if (!a.IsFinite) return Unbounded;
            double l = a.Excursion();
            double l2 = mulUp(l, l);
            double halfL2 = l2 == 0 ? 0 : ExactInterval.Up(0.5 * l2);
            double c0Sq = a.C0 * a.C0;
            double c0 = c0Sq + halfL2;
            double twoC0 = 2.0 * a.C0;
            double cx = twoC0 * a.Cx, cy = twoC0 * a.Cy, cz = twoC0 * a.Cz;
            if (!IsFiniteDouble(c0) || !IsFiniteDouble(cx) || !IsFiniteDouble(cy) || !IsFiniteDouble(cz))
                return Unbounded;
            double rA = mulUp(Math.Abs(twoC0), a.Rad);
            double err = roundErr(c0Sq) + roundErr(c0) + roundErr(cx) + roundErr(cy) + roundErr(cz);
            double rSum = halfL2 + rA + err;
            double rad = rSum == 0 ? 0 : ExactInterval.Up(rSum * SafeSum8);
            return IsFiniteDouble(rad) ? new Affine3(c0, cx, cy, cz, rad) : Unbounded;
        }
    }
}
