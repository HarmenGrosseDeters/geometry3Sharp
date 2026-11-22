// A closed interval of doubles with the semantics of the library's internal ExactInterval (math/ExactIntervals.cs): every
// operation is rounded to nearest and then widened by one unit in the last place on both sides, so the exact result on
// any values of the operands lies inside. No directed rounding mode, no FMA, no libm: +, -, *, / and sqrt are correctly
// rounded in IEEE 754, and sin / cos are enclosed by a Taylor polynomial with its remainder, evaluated in this arithmetic
// (Math.Sin is not used: its error is not specified).
using System;

struct Iv
{
    public readonly double Lo, Hi;
    public Iv(double lo, double hi) { Lo = lo; Hi = hi; }
    public static Iv Of(double x) { return new Iv(x, x); }
    public static readonly Iv Whole = new Iv(double.NegativeInfinity, double.PositiveInfinity);
    public static readonly Iv Unit = new Iv(-1, 1);

    static Iv make(double lo, double hi)
    {
        if (double.IsNaN(lo) || double.IsNaN(hi)) return Whole;
        return new Iv(Math.BitDecrement(lo), Math.BitIncrement(hi));
    }

    public bool HasZero { get { return !(Lo > 0) && !(Hi < 0); } }
    public double Mid { get { return 0.5 * Lo + 0.5 * Hi; } }
    public double MaxAbs { get { return Math.Max(Math.Abs(Lo), Math.Abs(Hi)); } }
    public bool IsFinite { get { return !double.IsInfinity(Lo) && !double.IsInfinity(Hi); } }

    public static Iv operator +(Iv a, Iv b) { return make(a.Lo + b.Lo, a.Hi + b.Hi); }
    public static Iv operator -(Iv a, Iv b) { return make(a.Lo - b.Hi, a.Hi - b.Lo); }
    public static Iv operator -(Iv a) { return new Iv(-a.Hi, -a.Lo); }
    public static Iv operator *(Iv a, Iv b)
    {
        double p1 = a.Lo * b.Lo, p2 = a.Lo * b.Hi, p3 = a.Hi * b.Lo, p4 = a.Hi * b.Hi;
        if (double.IsNaN(p1) || double.IsNaN(p2) || double.IsNaN(p3) || double.IsNaN(p4)) return Whole;
        return make(Math.Min(Math.Min(p1, p2), Math.Min(p3, p4)), Math.Max(Math.Max(p1, p2), Math.Max(p3, p4)));
    }
    public static Iv operator *(double a, Iv b) { return Of(a) * b; }
    public static Iv operator /(Iv a, Iv b)
    {
        if (b.HasZero) return Whole;
        double p1 = a.Lo / b.Lo, p2 = a.Lo / b.Hi, p3 = a.Hi / b.Lo, p4 = a.Hi / b.Hi;
        if (double.IsNaN(p1) || double.IsNaN(p2) || double.IsNaN(p3) || double.IsNaN(p4)) return Whole;
        return make(Math.Min(Math.Min(p1, p2), Math.Min(p3, p4)), Math.Max(Math.Max(p1, p2), Math.Max(p3, p4)));
    }
    public static Iv Sqr(Iv a)
    {
        if (a.Lo >= 0) return make(a.Lo * a.Lo, a.Hi * a.Hi);
        if (a.Hi <= 0) return make(a.Hi * a.Hi, a.Lo * a.Lo);
        double m = Math.Max(-a.Lo, a.Hi);
        return new Iv(0, make(0, m * m).Hi);
    }
    /// <summary> the square root of the non-negative part; the whole line if a is entirely negative </summary>
    public static Iv Sqrt(Iv a)
    {
        if (!(a.Hi >= 0)) return Whole;
        Iv r = make(Math.Sqrt(Math.Max(a.Lo, 0)), Math.Sqrt(a.Hi));
        return new Iv(Math.Max(r.Lo, 0), r.Hi);
    }
    public static Iv Hull(Iv a, Iv b) { return new Iv(Math.Min(a.Lo, b.Lo), Math.Max(a.Hi, b.Hi)); }
    public static Iv Min(Iv a, Iv b) { return new Iv(Math.Min(a.Lo, b.Lo), Math.Min(a.Hi, b.Hi)); }
    public static Iv Max(Iv a, Iv b) { return new Iv(Math.Max(a.Lo, b.Lo), Math.Max(a.Hi, b.Hi)); }
    public static Iv Abs(Iv a)
    {
        if (a.Lo >= 0) return a;
        if (a.Hi <= 0) return -a;
        return new Iv(0, Math.Max(-a.Lo, a.Hi));
    }

    // pi / 2 lies strictly between the double 0x3FF921FB54442D18 = 1.5707963267948965579989817... and its successor
    static readonly Iv PiHalf = new Iv(1.5707963267948966, Math.BitIncrement(1.5707963267948966));
    static readonly Iv Pi = new Iv(2 * PiHalf.Lo, 2 * PiHalf.Hi);            // scaling by 2 is exact
    static readonly Iv TwoPi = new Iv(4 * PiHalf.Lo, 4 * PiHalf.Hi);
    static readonly Iv One = Of(1);

    /// <summary> enclosures of sin x and cos x for a double x </summary>
    public static void SinCosPoint(double x, out Iv s, out Iv c)
    {
        s = Unit; c = Unit;
        if (double.IsNaN(x) || Math.Abs(x) > 1e6) return;
        double k = Math.Round(x * 0.6366197723675814);       // any integer is sound; this one makes r small
        Iv r = Of(x) - Of(k) * PiHalf;
        if (r.MaxAbs > 0.8) return;
        Iv r2 = Sqr(r);
        // sin r = r (1 - r^2/6 (1 - r^2/20 (... (1 - r^2/272)))) up to r^17, remainder |r|^19 / 19! < 1e-18 for |r| <= 0.8
        Iv p = One;
        foreach (double d in new double[] { 272, 210, 156, 110, 72, 42, 20, 6 }) p = One - r2 / Of(d) * p;
        Iv sr = r * p + new Iv(-1e-18, 1e-18);
        // cos r = 1 - r^2/2 (1 - r^2/12 (... (1 - r^2/240))) up to r^16, remainder |r|^18 / 18! < 1e-17 for |r| <= 0.8
        Iv q = One;
        foreach (double d in new double[] { 240, 182, 132, 90, 56, 30, 12, 2 }) q = One - r2 / Of(d) * q;
        Iv cr = q + new Iv(-1e-17, 1e-17);
        int m = (int)(((long)k % 4 + 4) % 4);
        Iv ss = m == 0 ? sr : m == 1 ? cr : m == 2 ? -sr : -cr;
        Iv cc = m == 0 ? cr : m == 1 ? -sr : m == 2 ? -cr : sr;
        s = new Iv(Math.Max(ss.Lo, -1), Math.Min(ss.Hi, 1));
        c = new Iv(Math.Max(cc.Lo, -1), Math.Min(cc.Hi, 1));
    }

    // may [lo, hi] contain a point c + 2 pi n ? (true whenever it does; may be true when it does not)
    static bool mayContain(double lo, double hi, Iv c)
    {
        Iv qa = (Of(lo) - c) / TwoPi, qb = (Of(hi) - c) / TwoPi;
        return Math.Ceiling(qa.Lo) <= Math.Floor(qb.Hi);
    }

    public static void SinCos(Iv a, out Iv s, out Iv c)
    {
        if (!a.IsFinite || a.Hi - a.Lo > 6) { s = Unit; c = Unit; return; }
        Iv sa, ca, sb, cb;
        SinCosPoint(a.Lo, out sa, out ca);
        if (a.Lo == a.Hi) { s = sa; c = ca; return; }
        SinCosPoint(a.Hi, out sb, out cb);
        double slo = Math.Min(sa.Lo, sb.Lo), shi = Math.Max(sa.Hi, sb.Hi), clo = Math.Min(ca.Lo, cb.Lo), chi = Math.Max(ca.Hi, cb.Hi);
        if (mayContain(a.Lo, a.Hi, PiHalf)) shi = 1;
        if (mayContain(a.Lo, a.Hi, -PiHalf)) slo = -1;
        if (mayContain(a.Lo, a.Hi, Of(0))) chi = 1;
        if (mayContain(a.Lo, a.Hi, Pi)) clo = -1;
        s = new Iv(slo, shi); c = new Iv(clo, chi);
    }
}
