using System;

namespace g3
{
    /// <summary>
    /// Enclosures of an implicit function over an axis-aligned box: every value of f, and every gradient of f, at every
    /// point of the box lies in the intervals. Smooth is false if the box may hold a point where f is not smooth (two
    /// active pieces of a min / max, a square root or a division at 0); the gradient intervals then enclose the gradients
    /// of all pieces that can be active (or are the whole line), and a certificate must not use the box as a smooth one.
    /// </summary>
    internal struct IntervalEnclosure
    {
        public ExactInterval F, Gx, Gy, Gz;
        public bool Smooth;
    }

    /// <summary>
    /// An implicit function with enclosures over boxes, for the topology certificate of a contour of a smooth field
    /// (ContouringSmoothCertificate). Internal: the interface is not part of the public API.
    /// Requirements on an implementation: (i) inclusion as stated on IntervalEnclosure, for every box, with outward
    /// rounding (ExactInterval's operations and IntervalMath do this); (ii) which pieces of a min / max can be active is
    /// decided from enclosures over the box, never from a point value; (iii) no hidden state: Enclose is called from
    /// several threads.
    /// </summary>
    internal interface IntervalImplicit3d
    {
        IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z);
    }

    /// <summary>
    /// Interval functions beside ExactInterval's +, -, *, / and square root. No call into the runtime's math library
    /// (the error of Math.Sin is not specified): sin and cos are a Taylor polynomial with its Lagrange remainder,
    /// evaluated in ExactInterval arithmetic, so the result is an enclosure and the same on every runtime.
    /// </summary>
    internal static class IntervalMath
    {
        // pi / 2 = 1.57079632679489661923..., strictly between the double 0x3FF921FB54442D18 = 1.5707963267948965579989817...
        // and its successor (the spacing there is 2.2e-16, the double is 6.1e-17 below pi / 2)
        internal static readonly ExactInterval PiHalf = new ExactInterval(1.5707963267948966, ExactInterval.Up(1.5707963267948966));
        // scaling an interval by a power of two is exact
        static readonly ExactInterval Pi = new ExactInterval(2 * PiHalf.Lo, 2 * PiHalf.Hi);
        static readonly ExactInterval TwoPi = new ExactInterval(4 * PiHalf.Lo, 4 * PiHalf.Hi);
        static readonly ExactInterval One = ExactInterval.Of(1);
        static readonly ExactInterval Unit = new ExactInterval(-1, 1);
        /// <summary> the largest |r| of the reduced argument that is evaluated (beyond it the result is [-1, 1]) </summary>
        internal const double MaxReduced = 0.8;
        // The polynomials stop where every term and the remainder still matter at the precision of a double, so that the
        // exact tests see each of them:
        //   sin r = r - r^3/3! + ... + r^13/13! + R,  |R| <= |r|^15 / 15!   (Lagrange: the 15th derivative is +-cos)
        //   cos r = 1 - r^2/2! + ... + r^12/12! + R,  |R| <= |r|^14 / 14!   (the 14th derivative is +-cos)
        // For |r| <= 0.8 the remainders are at most 2.7e-14 and 5.1e-13; they are computed from r, so they vanish with it.
        internal const double Factorial15 = 1307674368000.0, Factorial14 = 87178291200.0;       // exact doubles
        // (2j)(2j + 1) for j = 6 .. 1, and (2j - 1)(2j) for j = 6 .. 1: small integers, exact
        static readonly double[] SinDivisors = { 156, 110, 72, 42, 20, 6 };
        static readonly double[] CosDivisors = { 132, 90, 56, 30, 12, 2 };

        public static ExactInterval Hull(ExactInterval a, ExactInterval b)
        {
            return new ExactInterval(Math.Min(a.Lo, b.Lo), Math.Max(a.Hi, b.Hi));
        }

        static ExactInterval clampUnit(ExactInterval a)
        {
            return new ExactInterval(Math.Max(a.Lo, -1), Math.Min(a.Hi, 1));
        }

        /// <summary> enclosures of sin x and cos x for a double x ([-1, 1] for NaN, infinities and |x| above 2^30) </summary>
        public static void SinCos(double x, out ExactInterval sin, out ExactInterval cos)
        {
            sin = Unit; cos = Unit;
            if (double.IsNaN(x) || !(Math.Abs(x) <= 1073741824.0))
                return;
            // x = k pi / 2 + r. Any integer k is sound (r is an enclosure of x - k pi / 2 for the real pi); this k makes
            // |r| about pi / 4 at most. The check below, not the choice of k, is what the remainder bounds rest on.
            double k = Math.Round(x * 0.6366197723675814);
            ExactInterval r = ExactInterval.Of(x) - ExactInterval.Of(k) * PiHalf;
            if (!(Math.Max(Math.Abs(r.Lo), Math.Abs(r.Hi)) <= MaxReduced))
                return;
            ExactInterval r2 = ExactInterval.Square(r);
            // upper bounds of |r|^14 / 14! and |r|^15 / 15!, from an upper bound a1 of |r|
            ExactInterval a1 = ExactInterval.Of(Math.Max(Math.Abs(r.Lo), Math.Abs(r.Hi)));
            ExactInterval a2 = a1 * a1, a4 = a2 * a2, a8 = a4 * a4, a14 = a8 * a4 * a2;
            double cosBound = (a14 / ExactInterval.Of(Factorial14)).Hi, sinBound = (a14 * a1 / ExactInterval.Of(Factorial15)).Hi;
            // sin r = r (1 - r^2/6 (1 - r^2/20 (... (1 - r^2/156)))) + R
            ExactInterval p = One;
            for (int i = 0; i < SinDivisors.Length; ++i)
                p = One - r2 / ExactInterval.Of(SinDivisors[i]) * p;
            ExactInterval sr = r * p + new ExactInterval(-sinBound, sinBound);
            // cos r = 1 - r^2/2 (1 - r^2/12 (... (1 - r^2/132))) + R
            ExactInterval q = One;
            for (int i = 0; i < CosDivisors.Length; ++i)
                q = One - r2 / ExactInterval.Of(CosDivisors[i]) * q;
            ExactInterval cr = q + new ExactInterval(-cosBound, cosBound);
            // sin(r + k pi/2), cos(r + k pi/2) by k mod 4
            int m = (int)(((long)k % 4 + 4) % 4);
            ExactInterval s = m == 0 ? sr : m == 1 ? cr : m == 2 ? -sr : -cr;
            ExactInterval c = m == 0 ? cr : m == 1 ? -sr : m == 2 ? -cr : sr;
            sin = clampUnit(s); cos = clampUnit(c);
        }

        // may [lo, hi] contain a point c + 2 pi n for an integer n? True whenever it does (it may be true when it does not):
        // such an n satisfies (lo - c) / 2 pi <= n <= (hi - c) / 2 pi, and the two quotients are enclosed.
        static bool mayContain(double lo, double hi, ExactInterval c)
        {
            ExactInterval qa = (ExactInterval.Of(lo) - c) / TwoPi, qb = (ExactInterval.Of(hi) - c) / TwoPi;
            return Math.Ceiling(qa.Lo) <= Math.Floor(qb.Hi);
        }

        /// <summary>
        /// enclosures of sin and cos over an interval: the hull of the two ends, widened to +1 / -1 wherever the interval
        /// may hold an extremum (between extrema the functions are monotone)
        /// </summary>
        public static void SinCos(ExactInterval a, out ExactInterval sin, out ExactInterval cos)
        {
            sin = Unit; cos = Unit;
            if (double.IsNaN(a.Lo) || double.IsNaN(a.Hi) || double.IsInfinity(a.Lo) || double.IsInfinity(a.Hi) || !(a.Hi - a.Lo <= 6))
                return;
            ExactInterval sa, ca, sb, cb;
            SinCos(a.Lo, out sa, out ca);
            if (a.Lo == a.Hi) { sin = sa; cos = ca; return; }
            SinCos(a.Hi, out sb, out cb);
            double slo = Math.Min(sa.Lo, sb.Lo), shi = Math.Max(sa.Hi, sb.Hi), clo = Math.Min(ca.Lo, cb.Lo), chi = Math.Max(ca.Hi, cb.Hi);
            if (mayContain(a.Lo, a.Hi, PiHalf)) shi = 1;                   // sin = 1 at pi/2 + 2 pi n
            if (mayContain(a.Lo, a.Hi, -PiHalf)) slo = -1;                 // sin = -1 at -pi/2 + 2 pi n
            if (mayContain(a.Lo, a.Hi, ExactInterval.Of(0))) chi = 1;      // cos = 1 at 2 pi n
            if (mayContain(a.Lo, a.Hi, Pi)) clo = -1;                      // cos = -1 at pi + 2 pi n
            sin = new ExactInterval(slo, shi); cos = new ExactInterval(clo, chi);
        }
    }

    /// <summary>
    /// A level of a triply periodic nodal function with enclosures: F = s g(K x, K y, K z) - Level, with g the function of
    /// TpmsLattice.Evaluate (as a function of real numbers, with K the given double) and s = -1 if Negate. One smooth
    /// function: the skeletal styles of TpmsLattice without the domain clip. The sheet style (|g|) and the clip are not
    /// smooth and are not offered here.
    /// </summary>
    internal sealed class IntervalTpms : IntervalImplicit3d
    {
        public readonly TpmsType Type;
        public readonly double K, Level;
        public readonly bool Negate;

        public IntervalTpms(TpmsType type, double k, double level, bool negate = false)
        {
            Type = type; K = k; Level = level; Negate = negate;
        }

        static ExactInterval c(double v) { return ExactInterval.Of(v); }

        public IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
        {
            ExactInterval k = c(K);
            ExactInterval px = k * x, py = k * y, pz = k * z;
            ExactInterval sx, cx, sy, cy, sz, cz;
            IntervalMath.SinCos(px, out sx, out cx); IntervalMath.SinCos(py, out sy, out cy); IntervalMath.SinCos(pz, out sz, out cz);
            ExactInterval g, gx, gy, gz;
            switch (Type) {
                case TpmsType.Gyroid:
                    g = sx * cy + sy * cz + sz * cx;
                    gx = cx * cy - sz * sx; gy = cy * cz - sx * sy; gz = cz * cx - sy * sz;
                    break;
                case TpmsType.SchwarzP:
                    g = cx + cy + cz;
                    gx = -sx; gy = -sy; gz = -sz;
                    break;
                case TpmsType.SchwarzD:
                    g = sx * sy * sz + sx * cy * cz + cx * sy * cz + cx * cy * sz;
                    gx = cx * sy * sz + cx * cy * cz - sx * sy * cz - sx * cy * sz;
                    gy = sx * cy * sz - sx * sy * cz + cx * cy * cz - cx * sy * sz;
                    gz = sx * sy * cz - sx * cy * sz - cx * sy * sz + cx * cy * cz;
                    break;
                case TpmsType.Neovius:
                    g = c(3) * (cx + cy + cz) + c(4) * (cx * cy * cz);
                    gx = -(c(3) * sx) - c(4) * (sx * cy * cz);
                    gy = -(c(3) * sy) - c(4) * (cx * sy * cz);
                    gz = -(c(3) * sz) - c(4) * (cx * cy * sz);
                    break;
                case TpmsType.IWP: {
                    ExactInterval s2x, c2x, s2y, c2y, s2z, c2z;
                    doubleAngle(px, py, pz, out s2x, out c2x, out s2y, out c2y, out s2z, out c2z);
                    g = c(2) * (cx * cy + cy * cz + cz * cx) - (c2x + c2y + c2z);
                    gx = c(2) * s2x - c(2) * (sx * (cy + cz));
                    gy = c(2) * s2y - c(2) * (sy * (cx + cz));
                    gz = c(2) * s2z - c(2) * (sz * (cx + cy));
                    break;
                }
                case TpmsType.Lidinoid: {
                    ExactInterval s2x, c2x, s2y, c2y, s2z, c2z;
                    doubleAngle(px, py, pz, out s2x, out c2x, out s2y, out c2y, out s2z, out c2z);
                    ExactInterval h = c(0.5);
                    g = h * (s2x * cy * sz + s2y * cz * sx + s2z * cx * sy) - h * (c2x * c2y + c2y * c2z + c2z * c2x) + c(0.15);
                    gx = c2x * cy * sz + h * (s2y * cz * cx) - h * (s2z * sx * sy) + s2x * (c2y + c2z);
                    gy = c2y * cz * sx + h * (s2z * cx * cy) - h * (s2x * sy * sz) + s2y * (c2x + c2z);
                    gz = c2z * cx * sy + h * (s2x * cy * cz) - h * (s2y * sz * sx) + s2z * (c2x + c2y);
                    break;
                }
                default: throw new ArgumentOutOfRangeException(nameof(Type));
            }
            if (Negate) { g = -g; gx = -gx; gy = -gy; gz = -gz; }
            return new IntervalEnclosure { F = g - c(Level), Gx = k * gx, Gy = k * gy, Gz = k * gz, Smooth = true };
        }

        // sin and cos of twice the phases, from their own enclosures (doubling an interval's ends is exact)
        static void doubleAngle(ExactInterval px, ExactInterval py, ExactInterval pz, out ExactInterval s2x, out ExactInterval c2x,
            out ExactInterval s2y, out ExactInterval c2y, out ExactInterval s2z, out ExactInterval c2z)
        {
            IntervalMath.SinCos(new ExactInterval(2 * px.Lo, 2 * px.Hi), out s2x, out c2x);
            IntervalMath.SinCos(new ExactInterval(2 * py.Lo, 2 * py.Hi), out s2y, out c2y);
            IntervalMath.SinCos(new ExactInterval(2 * pz.Lo, 2 * pz.Hi), out s2z, out c2z);
        }
    }
}
