// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3;

namespace gs
{
    /// <summary>
    /// A slope as the user specified it (docs/design/TERRAIN.md section 1). The kind of input is kept, so that the
    /// check runs against what was entered: a ratio 1:n and a percentage are checked exactly, an angle against the
    /// double S, a proven lower bound of tan(angle) within a few ulps (ExactlyCheckable is false: a slope between S and
    /// the true tangent is rejected, never one above it).
    /// The slope is rise / run: the magnitude of the gradient over XY, measured against the horizontal.
    /// </summary>
    public struct SlopeSpec
    {
        public enum Kinds
        {
            /// <summary> 1:n, n metres horizontal per metre vertical (the reference convention): s = 1 / n </summary>
            Ratio,
            /// <summary> p percent: s = p / 100 </summary>
            Percent,
            /// <summary> an angle against the horizontal, in degrees: s = tan(a) </summary>
            Degrees,
            /// <summary> s itself (rise / run) </summary>
            Rise
        }

        public Kinds Kind;
        public double Value;

        public SlopeSpec(Kinds kind, double value) { Kind = kind; Value = value; }

        public static SlopeSpec Ratio(double n) { return new SlopeSpec(Kinds.Ratio, n); }
        public static SlopeSpec Percent(double p) { return new SlopeSpec(Kinds.Percent, p); }
        public static SlopeSpec Degrees(double a) { return new SlopeSpec(Kinds.Degrees, a); }
        public static SlopeSpec Rise(double s) { return new SlopeSpec(Kinds.Rise, s); }

        /// <summary> positive and finite (an angle below 90 degrees), with S finite and not below the normal doubles </summary>
        public bool IsValid {
            get {
                if (!(Value > 0) || double.IsInfinity(Value)) return false;
                if (Kind == Kinds.Degrees && !(Value < 90)) return false;
                double s = raw_s();
                return s >= 2.2250738585072014E-308 && double.IsInfinity(s) == false;
            }
        }

        /// <summary>
        /// false for Degrees: checked against the double S, a proven lower bound of the tangent of the angle as entered,
        /// so the check is conservative there (never steeper than the angle), not exact
        /// </summary>
        public bool ExactlyCheckable { get { return Kind != Kinds.Degrees; } }

        /// <summary>
        /// rise / run as a double, never steeper than the spec: for Ratio and Percent the largest double that is not above
        /// it (checked exactly); for Degrees a double not above tan of the angle as entered, proven (TanLowerBound), within
        /// a few ulps of it; NaN if not IsValid
        /// </summary>
        public double S {
            get { return IsValid ? raw_s() : double.NaN; }
        }

        double raw_s()
        {
            switch (Kind) {
                case Kinds.Ratio: {
                    // largest s with s * n <= 1
                    double s = 1.0 / Value;
                    while (s > 0 && ExactSlopePredicates.ProductMinus(s, Value, 1) > 0) s = ExactInterval.Down(s);
                    return s;
                }
                case Kinds.Percent: {
                    double s = Value / 100;
                    while (s > 0 && ExactSlopePredicates.ProductMinus(s, 100, Value) > 0) s = ExactInterval.Down(s);
                    return s;
                }
                case Kinds.Degrees:
                    return TanLowerBound(Value);
                default:
                    return Value;
            }
        }

        static readonly System.Collections.Concurrent.ConcurrentDictionary<long, double> tanCache =
            new System.Collections.Concurrent.ConcurrentDictionary<long, double>();

        /// <summary>
        /// a double t with 0 &lt;= t &lt;= tan(a pi / 180), proven, for 0 &lt; a &lt; 90 (0 only where tan is below the smallest
        /// positive double, a below about 2.8e-322): the largest double not above tan, or the one just below it (docs/design/TERRAIN.md section 1): the
        /// largest double t for which sin_lo(x) &gt;= t cos_hi(x) holds exactly, with x = a pi_lo / 180
        /// &lt;= a pi / 180 (pi_lo = Math.PI plus a double below pi - Math.PI, exactly), and sin_lo, cos_hi the Taylor sums of
        /// sin and cos up to x^47 and x^48, which end with a negative and a positive term of alternating series whose
        /// omitted terms fall (x &lt; pi / 2): a lower bound of sin x and an upper bound of cos x &gt; 0. Then tan(a pi / 180) &gt;= tan x =
        /// sin x / cos x &gt;= sin_lo / cos_hi &gt;= t. The comparison is made exactly on integers, the sums times 180^48 48!.
        /// The same on every platform: Math.Tan only gives the start of the search. NaN outside (0, 90)
        /// </summary>
        internal static double TanLowerBound(double a)
        {
            if (!(a > 0 && a < 90)) return double.NaN;
            // (45 degrees, the one angle in (0, 90) whose tangent is a double: tan = 1 exactly, so 1 is the largest double not
            // above it; the bound below would give the double just below 1, as x = 45 pi_lo / 180 is below pi / 4)
            if (a == 45) return 1.0;
            long key = BitConverter.DoubleToInt64Bits(a);
            double t;
            if (tanCache.TryGetValue(key, out t)) return t;
            t = tan_lower_bound(a);
            // (bounded: a host that sweeps angles does not grow it for ever; a cleared entry is computed again, the same)
            if (tanCache.Count >= 4096) tanCache.Clear();
            tanCache[key] = t;
            return t;
        }

        // pi - Math.PI = 1.2246467991473531772e-16; one double lower is below it
        static readonly double piTail = ExactInterval.Down(1.2246467991473532e-16);

        /// <summary>
        /// the Taylor sums of sin x and cos x for x = a pi_lo / 180, up to the terms x^(N-1) and x^N, times 180^N N! (exact);
        /// for N = 0 mod 4 the last sin term (m = N - 1 = 3 mod 4) is negative and the last cos term (m = N) positive, so
        /// sinL / L &lt;= sin x and cosH / L &gt;= cos x (Leibniz: the omitted terms fall)
        /// </summary>
        internal static void TaylorSums(double a, int N, out ExactDyadic sinL, out ExactDyadic cosH)
        {
            // y = a pi_lo exactly, x = y / 180; the terms times L = 180^N N!: x^m / m! L = y^m 180^(N - m) N! / m!
            ExactDyadic y = ExactDyadic.Of(a) * PiLo();
            sinL = new ExactDyadic(System.Numerics.BigInteger.Zero, 0); cosH = sinL;
            ExactDyadic yp = new ExactDyadic(System.Numerics.BigInteger.One, 0);
            for (int m = 0; m <= N; ++m) {
                // 180^(N - m) N! / m!
                System.Numerics.BigInteger f = System.Numerics.BigInteger.Pow(180, N - m);
                for (int j = m + 1; j <= N; ++j) f *= j;
                ExactDyadic term = yp * new ExactDyadic(f, 0);
                bool negative = (m / 2) % 2 == 1;
                if (m % 2 == 0) cosH = negative ? cosH - term : cosH + term;
                else sinL = negative ? sinL - term : sinL + term;
                yp = yp * y;
            }
        }

        /// <summary> N of the Taylor sums: 0 mod 4, so the sums end with a negative sin and a positive cos term </summary>
        internal const int TaylorTerms = 48;

        /// <summary> pi_lo = Math.PI + a double below pi - Math.PI, exactly (below pi) </summary>
        internal static ExactDyadic PiLo() { return ExactDyadic.Of(Math.PI) + ExactDyadic.Of(piTail); }

        static double tan_lower_bound(double a)
        {
            ExactDyadic sinL, cosH;
            TaylorSums(a, TaylorTerms, out sinL, out cosH);
            // (sin ends at m = 47, negative: a lower bound; cos at m = 48, positive: an upper bound)
            Func<double, bool> proven = (c) => c > 0 && ExactDyadic.Compare(sinL, ExactDyadic.Of(c) * cosH) >= 0;
            // the largest proven double, which depends on a only: proven is monotone (true up to the bound's sin_lo / cos_hi,
            // false above), so a bracket [lo proven, hi not proven] from Math.Tan's value, widened in growing steps, is
            // bisected on the bit patterns (positive doubles are ordered as their bits) to adjacent doubles
            double start = Math.Tan(a * Math.PI / 180);
            if (!(start > 0) || double.IsInfinity(start)) start = double.MaxValue;
            double lo, hi;
            if (proven(start)) {
                lo = start; hi = double.PositiveInfinity;
                for (int i = 0; ; ++i) {
                    double up = lo + (ExactInterval.Up(lo) - lo) * (double)(1L << Math.Min(i, 60));
                    if (double.IsInfinity(up) || up <= lo) up = ExactInterval.Up(lo);
                    if (double.IsInfinity(up)) return lo;
                    if (proven(up)) lo = up; else { hi = up; break; }
                }
            } else {
                hi = start; lo = 0;
                for (int i = 0; ; ++i) {
                    double down = hi - (ExactInterval.Up(hi) - hi) * (double)(1L << Math.Min(i, 60));
                    if (!(down > 0)) down = double.Epsilon;
                    if (proven(down)) { lo = down; break; }
                    if (down == double.Epsilon) return 0;     // (none proven: tan of the angle is below the smallest double)
                    hi = down;
                }
            }
            long bl = BitConverter.DoubleToInt64Bits(lo), bh = BitConverter.DoubleToInt64Bits(hi);
            while (bh - bl > 1) {
                long mid = bl + (bh - bl) / 2;
                if (proven(BitConverter.Int64BitsToDouble(mid))) bl = mid; else bh = mid;
            }
            return BitConverter.Int64BitsToDouble(bl);
        }

        // slope^2 * k1 <= k2, k1 = k1a * k1b, k2 = k2a * k2b
        internal void Factors(out double k1a, out double k1b, out double k2a, out double k2b)
        {
            switch (Kind) {
                case Kinds.Ratio: k1a = Value; k1b = Value; k2a = 1; k2b = 1; return;       // s^2 n^2 <= 1
                case Kinds.Percent: k1a = 100; k1b = 100; k2a = Value; k2b = Value; return; // s^2 10^4 <= p^2
                default: { double s = S; k1a = 1; k1b = 1; k2a = s; k2b = s; return; }       // s^2 <= S^2
            }
        }

        public override string ToString()
        {
            switch (Kind) {
                case Kinds.Ratio: return "1:" + Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                case Kinds.Percent: return Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " %";
                case Kinds.Degrees: return Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " deg";
                default: return "s = " + Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }


    /// <summary>
    /// The "never steeper than specified" oracle (docs/design/TERRAIN.md 5.4), exact on the stored doubles, usable
    /// on any TIN (also results of other tools).
    /// </summary>
    public static class Slope
    {
        /// <summary>
        /// exact: the slope of triangle (a, b, c), sqrt(nx^2 + ny^2) / |nz| with n = (b - a) x (c - a), is at most the
        /// spec: exactly against the spec as entered for Ratio and Percent, against the double S for Rise and Degrees.
        /// For Degrees S is a proven lower bound of tan of the angle (since 2026-11-12; before, Math.Tan's double, which can
        /// be above it), so a true result proves the triangle not steeper than the angle as entered, and a slope within a
        /// few ulps below the angle's tangent may be rejected.
        /// The orientation does not matter. False for a triangle that is degenerate in XY (nz = 0), for an invalid
        /// spec and for coordinates that are not finite.
        /// </summary>
        public static bool IsNotSteeper(Vector3d a, Vector3d b, Vector3d c, SlopeSpec spec)
        {
            if (spec.IsValid == false || finite(a) == false || finite(b) == false || finite(c) == false)
                return false;
            double k1a, k1b, k2a, k2b;
            spec.Factors(out k1a, out k1b, out k2a, out k2b);
            bool degenerate;
            int sign = ExactSlopePredicates.SlopeSign(a, b, c, k1a, k1b, k2a, k2b, out degenerate);
            return degenerate == false && sign >= 0;
        }

        /// <summary>
        /// exact: IsNotSteeper, and the plane of (a, b, c) lies on or below both toe points p0 and p1 (the end points
        /// of the toe piece the triangle is attributed to). Together they prove that the triangle lies below every
        /// slope cone z(q) + s |x - q| of the toe segment from p0 to p1 (5.4 (i) and (ii)): the plane L of the
        /// triangle has L(q) &lt;= z(q) on the whole segment (both are linear along it), so L(x) &lt;= L(q) + s |x - q|.
        /// p0 = p1 for a single toe point (a cone apex).
        /// </summary>
        public static bool IsSupportedBy(Vector3d a, Vector3d b, Vector3d c, Vector3d p0, Vector3d p1, SlopeSpec spec)
        {
            if (IsNotSteeper(a, b, c, spec) == false || finite(p0) == false || finite(p1) == false)
                return false;
            return IsPlaneOnOrBelow(a, b, c, p0) && IsPlaneOnOrBelow(a, b, c, p1);
        }

        /// <summary> exact: the plane of (a, b, c), not vertical, is at most p.z at p's XY </summary>
        public static bool IsPlaneOnOrBelow(Vector3d a, Vector3d b, Vector3d c, Vector3d p)
        {
            int o2 = ExactPredicates.Orient2d(a.xy, b.xy, c.xy);
            if (o2 == 0)
                return false;
            // Orient3d is +1 for p above the plane of a counter-clockwise triangle
            return ExactPredicates.Orient3d(a, b, c, p) * o2 >= 0;
        }

        static bool finite(Vector3d p)
        {
            return MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z);
        }
    }
}
