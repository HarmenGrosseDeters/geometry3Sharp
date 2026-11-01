// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3;

namespace gs
{
    /// <summary>
    /// A slope as the user specified it (docs/design/TERRAIN.md section 1). The kind of input is kept, so that the
    /// check runs against what was entered: a ratio 1:n and a percentage are checked exactly, an angle against the
    /// double S = tan(angle), which is only as exact as Math.Tan (ExactlyCheckable is false).
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

        /// <summary> positive and finite (an angle below 90 degrees) </summary>
        public bool IsValid {
            get {
                if (!(Value > 0) || double.IsInfinity(Value)) return false;
                return Kind != Kinds.Degrees || Value < 90;
            }
        }

        /// <summary> false for Degrees: checked against the double S, not against the angle as entered </summary>
        public bool ExactlyCheckable { get { return Kind != Kinds.Degrees; } }

        /// <summary>
        /// rise / run as a double, never steeper than the spec for Ratio, Percent and Rise (the largest double that
        /// is not above it; checked exactly); tan of the angle for Degrees; NaN if not IsValid
        /// </summary>
        public double S {
            get {
                if (IsValid == false) return double.NaN;
                switch (Kind) {
                    case Kinds.Ratio: {
                        // largest s with s * n <= 1
                        double s = 1.0 / Value;
                        while (ExactSlopePredicates.ProductMinus(s, Value, 1) > 0) s = ExactInterval.Down(s);
                        return s;
                    }
                    case Kinds.Percent: {
                        double s = Value / 100;
                        while (ExactSlopePredicates.ProductMinus(s, 100, Value) > 0) s = ExactInterval.Down(s);
                        return s;
                    }
                    case Kinds.Degrees:
                        return Math.Tan(Value * Math.PI / 180);
                    default:
                        return Value;
                }
            }
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
