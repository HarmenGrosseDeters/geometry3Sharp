// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Four BigInteger operands of a plane side (the plane A, B, C, D of a triangle, or the point X, Y, Z, W of a piece
    /// centroid) loaded into sign-magnitude limbs once (#1047): ExactLimbSigns.PlaneSide loads its eight operands on every
    /// call (limbs_of and load, about two thirds of the call's time on nearly coincident layers), and the same plane meets many
    /// centroids and the same centroid many planes. The limbs are those that ExactLimbSigns.PlaneSide would load, each value
    /// at the same width (limbs_of), and PreparedLimbs.PlaneSide runs the same FixedArith operations in the same order on them,
    /// so the value is the same integer and the sign the same. Immutable once built, so one object is shared by all threads.
    /// </summary>
    internal sealed unsafe class PreparedLimbs
    {
        /// <summary> the operands as BigIntegers (for a side whose limbs do not fit the stack, and netstandard2.0) </summary>
        internal readonly BigInteger V0, V1, V2, V3;
#if NET
        // the limbs of the four values, one after the other; nk: the limbs k takes in the stack total (limbs_of), lk: its used limbs
        readonly ulong[] limbs;
        readonly int n0, n1, n2, n3, l0, l1, l2, l3;
        readonly byte negative;

        /// <summary> the limbs the four operands take in the stack budget of ExactLimbSigns.MaxOperandLimbs </summary>
        internal int Total { get { return n0 + n1 + n2 + n3; } }
#endif

        public PreparedLimbs(BigInteger a, BigInteger b, BigInteger c, BigInteger d)
        {
            V0 = a; V1 = b; V2 = c; V3 = d;
#if NET
            n0 = ExactLimbSigns.limbs_of(a); n1 = ExactLimbSigns.limbs_of(b); n2 = ExactLimbSigns.limbs_of(c); n3 = ExactLimbSigns.limbs_of(d);
            // (no limbs for an operand set that the stack budget refuses: the side then takes the BigInteger expression)
            if (n0 + n1 + n2 + n3 > ExactLimbSigns.MaxOperandLimbs) {
                limbs = null;
                return;
            }
            ulong[] own = new ulong[n0 + n1 + n2 + n3];
            limbs = own;
            fixed (ulong* p = own) {
                FixedInt f0 = ExactLimbSigns.load(a, p, n0), f1 = ExactLimbSigns.load(b, p + n0, n1);
                FixedInt f2 = ExactLimbSigns.load(c, p + n0 + n1, n2), f3 = ExactLimbSigns.load(d, p + n0 + n1 + n2, n3);
                l0 = f0.Len; l1 = f1.Len; l2 = f2.Len; l3 = f3.Len;
                negative = (byte)((f0.Neg ? 1 : 0) | (f1.Neg ? 2 : 0) | (f2.Neg ? 4 : 0) | (f3.Neg ? 8 : 0));
            }
#endif
        }

        /// <summary>
        /// the sign of a x + b y + c z - d w for the plane (a, b, c, d) and the point (x, y, z, w): the value of
        /// ExactLimbSigns.PlaneSide on the same eight integers
        /// </summary>
#if G3_PREDICATE_TIME
        internal static int PlaneSide(PreparedLimbs plane, PreparedLimbs point) { long tk = PredicateTime.EBegin((int)PredicateStats.Kind.LimbPlaneSide); int wr_ = PlaneSide_t(plane, point); PredicateTime.EEnd((int)PredicateStats.Kind.LimbPlaneSide, tk); return wr_; }
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        private static int PlaneSide_t(PreparedLimbs plane, PreparedLimbs point)
#else
        [MethodImpl(JitHints.Optimize)]
#if NET
        [SkipLocalsInit]
#endif
        internal static int PlaneSide(PreparedLimbs plane, PreparedLimbs point)
#endif
        {
#if NET
            int total = plane.Total + point.Total;
            if (total <= ExactLimbSigns.MaxOperandLimbs) {
                int scratch = ExactLimbSigns.SideScratch(total);
                ulong* sc = stackalloc ulong[scratch];
                var ar = new FixedArena(sc, scratch);
                ulong[] planeLimbs = plane.limbs, pointLimbs = point.limbs;
                fixed (ulong* pp = planeLimbs, qp = pointLimbs) {
                    FixedInt fa = new FixedInt(pp, plane.l0, (plane.negative & 1) != 0);
                    FixedInt fb = new FixedInt(pp + plane.n0, plane.l1, (plane.negative & 2) != 0);
                    FixedInt fc = new FixedInt(pp + plane.n0 + plane.n1, plane.l2, (plane.negative & 4) != 0);
                    FixedInt fd = new FixedInt(pp + plane.n0 + plane.n1 + plane.n2, plane.l3, (plane.negative & 8) != 0);
                    FixedInt fx = new FixedInt(qp, point.l0, (point.negative & 1) != 0);
                    FixedInt fy = new FixedInt(qp + point.n0, point.l1, (point.negative & 2) != 0);
                    FixedInt fz = new FixedInt(qp + point.n0 + point.n1, point.l2, (point.negative & 4) != 0);
                    FixedInt fw = new FixedInt(qp + point.n0 + point.n1 + point.n2, point.l3, (point.negative & 8) != 0);
                    FixedInt s = FixedArith.Add(FixedArith.Mul(fa, fx, ref ar), FixedArith.Mul(fb, fy, ref ar), ref ar);
                    s = FixedArith.Add(s, FixedArith.Mul(fc, fz, ref ar), ref ar);
                    return FixedArith.Sub(s, FixedArith.Mul(fd, fw, ref ar), ref ar).Sign;
                }
            }
#endif
            return ExactLimbSigns.PlaneSide(plane.V0, plane.V1, plane.V2, plane.V3, point.V0, point.V1, point.V2, point.V3);
        }
    }
}
