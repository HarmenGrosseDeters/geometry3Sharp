// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;

namespace g3
{
    /// <summary>
    /// The value of a 3x3 determinant of double rows, with a derived error bound: the exact determinant of the exact rows lies in
    /// [Det - E, Det + E]. The rows are rounded differences of input points (one rounding each) or inputs. Used by the certified
    /// patch bound (TriangleDeviationBounds.certified_patch_bound, #848). Derivation and constants: docs/design/CERTIFIED_PATCH_BOUND.md
    /// section 3. A result that is not finite is "undecided": Det = 0, E = +infinity.
    /// </summary>
    internal readonly struct CertifiedDet3
    {
        /// <summary> the computed determinant (0 if the evaluation overflowed) </summary>
        public readonly double Det;
        /// <summary> a bound of |Det - exact|, rounded up (+infinity if not certified) </summary>
        public readonly double E;

        CertifiedDet3(double det, double e) { Det = det; E = e; }

        /// <summary> 10 u, u = 2^-53: 5 2^-52, exact. The derived constant is 8.002 u (note, section 3, steps 1 to 4) </summary>
        internal const double TenU = 5.0 / 4503599627370496.0;
        /// <summary> 2^-1070 = 16 2^-1074: the absolute underflow term is this times (M + 1) (note, step 6) </summary>
        internal const double UnderflowUnit = 16 * double.Epsilon;

        /// <summary> 2^-980: perm >= this times (M + 1) puts A below 2^-39 of 10 u perm (the derivation, step 8) </summary>
        static readonly double AbsorbedAbove = BitConverter.Int64BitsToDouble(43L << 52);
        /// <summary> 1 + 2^-39 </summary>
        internal const double AbsorbFactor = 1.0 + 1.0 / 549755813888.0;

        static readonly CertifiedDet3 undecided = new CertifiedDet3(0, double.PositiveInfinity);

        /// <summary> +1 or -1 if the exact determinant has that sign for certain, else 0 (zero, undecided, rounding-sized) </summary>
        public int Sign => (Det > E) ? 1 : ((Det < -E) ? -1 : 0);

        /// <summary> a lower bound of the exact determinant (-infinity if undecided) </summary>
        public double Lo => (E < double.PositiveInfinity) ? ExactInterval.Down(Det - E) : double.NegativeInfinity;

        /// <summary> an upper bound of the exact determinant (+infinity if undecided) </summary>
        public double Hi => (E < double.PositiveInfinity) ? ExactInterval.Up(Det + E) : double.PositiveInfinity;

        /// <summary> an upper bound of the absolute value of the exact determinant (+infinity if undecided) </summary>
        public double AbsUpper => (E < double.PositiveInfinity) ? ExactInterval.Up(Math.Abs(Det) + E) : double.PositiveInfinity;

        /// <summary>
        /// The determinant of the rows r1, r2, r3 (Cramer's rule on the first row) and its error bound E = Up(Up(10 u perm) + A),
        /// perm the computed permanent, A = Up(2^-1070 Up(M + 1)), M the largest absolute entry; where perm >= 2^-980 (M + 1), E = Up(Up(10 u perm) (1 + 2^-39)), which is at least that sum and touches no subnormal. Valid if no operation overflows
        /// (otherwise undecided); no FMA, no extended precision (one rounding per operation)
        /// </summary>
        public static CertifiedDet3 Compute(Vector3d r1, Vector3d r2, Vector3d r3)
        {
            double m1 = r2.y * r3.z, m2 = r2.z * r3.y, m3 = r2.z * r3.x, m4 = r2.x * r3.z, m5 = r2.x * r3.y, m6 = r2.y * r3.x;
            double det = r1.x * (m1 - m2) + r1.y * (m3 - m4) + r1.z * (m5 - m6);
            double perm = Math.Abs(r1.x) * (Math.Abs(m1) + Math.Abs(m2)) + Math.Abs(r1.y) * (Math.Abs(m3) + Math.Abs(m4))
                + Math.Abs(r1.z) * (Math.Abs(m5) + Math.Abs(m6));
            // (an overflow in any operation ends as an infinity or a NaN in det and perm: the terms of perm are non-negative, and an
            // infinity in det cannot cancel to a finite value)
            if (!MathUtil.IsFinite(det) || !MathUtil.IsFinite(perm))
                return undecided;
            double mx = 0;
            for (int k = 0; k < 3; ++k)
                mx = Math.Max(mx, Math.Max(Math.Abs(r1[k]), Math.Max(Math.Abs(r2[k]), Math.Abs(r3[k]))));
            double mp1 = ExactInterval.Up(mx + 1);
            double t = ExactInterval.Up(TenU * perm);
            double e;
            if (perm >= AbsorbedAbove * mp1) {
                // A = 2^-1070 (M + 1) <= 2^-90 perm = 2^-40.3 (10 u perm) <= 2^-39 (10 u perm): E = Up(t (1 + 2^-39)) covers 10 u perm + A,
                // and no subnormal operand is touched (a subnormal addition costs about 100 ns on x86)
                e = ExactInterval.Up(t * AbsorbFactor);
            } else {
                double a = ExactInterval.Up(mp1 * UnderflowUnit);
                e = ExactInterval.Up(t + a);
            }
            if (!MathUtil.IsFinite(e))
                return undecided;
            return new CertifiedDet3(det, e);
        }
    }
}
