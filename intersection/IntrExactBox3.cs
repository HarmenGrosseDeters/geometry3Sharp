using System;
using System.Numerics;

namespace g3
{
    /// <summary>
    /// The exact decision shared by IntrLine3Box3, IntrRay3Box3, IntrSegment3Box3, IntrLine3AxisAlignedBox3 and IntrRay3AxisAlignedBox3:
    /// the closed solid box against a line, ray or segment, decided on the doubles as given, without a tolerance.
    /// <para>
    /// The box is the intersection of three closed slabs. For an axis-aligned box <c>lo_i &lt;= x_i &lt;= hi_i</c> (the fields Min and Max). For an
    /// oriented box <c>|dot(x - Center, Axis_i)| &lt;= Extent_i</c> with the exact dot products of the given doubles: this is the box when the axes are
    /// orthonormal (the same assumption the WildMagic5 code made), and the intersection of the slabs otherwise. A segment is the closed segment
    /// between its rounded ends P0 and P1 (a Segment3d is stored as center, direction and extent).
    /// </para>
    /// <para>
    /// A point of the component is origin + t d with t in [tmin, tmax] and d = D1 - D0 (exact), each slab gives <c>L &lt;= P + t Q &lt;= H</c>. The
    /// double route computes P, Q, L, H with certified error bounds and decides when the intervals of t are separated by them; otherwise
    /// (a touching contact, a direction within its error of parallel to a slab, a value near the range limits) the exact route decides with
    /// integers (<see cref="Dy"/>). The end points of the contact interval come out of the active slab: on its face exactly for an axis-aligned
    /// box; the other coordinates of a point from the double route are within a few ulp, from the exact route they are rounded once.
    /// </para>
    /// </summary>
    internal static class IntrExactBox3
    {
        internal const double U = 1.1102230246251565e-16;        // 2^-53
        internal const double Tiny = 1e-290, Huge = 1e290;        // below / above: the relative bounds do not hold (underflow, overflow), the exact route decides
        internal const double AbsErr = 1e-320;                    // absolute error of the underflow of one operation

        // ------------------------------------------------------------------------------------------------------------
        // the exact numbers: a BigInteger mantissa and a power of two
        // ------------------------------------------------------------------------------------------------------------

        internal struct Dy
        {
            public BigInteger M;
            public int E;
            public Dy(BigInteger m, int e) { M = m; E = e; }

            public static Dy From(double x)
            {
                if (x == 0) return new Dy(BigInteger.Zero, 0);
                long bits = BitConverter.DoubleToInt64Bits(x);
                bool neg = bits < 0; int e = (int)((bits >> 52) & 0x7FF); long m = bits & 0xFFFFFFFFFFFFFL;
                if (e == 0) e = 1; else m |= 1L << 52;
                BigInteger b = m; if (neg) b = -b;
                return new Dy(b, e - 1075);
            }
            public int Sign { get { return M.Sign; } }
            public static Dy operator -(Dy a) { return new Dy(-a.M, a.E); }
            public static Dy operator +(Dy a, Dy b)
            {
                if (a.M.IsZero) return b;
                if (b.M.IsZero) return a;
                if (a.E >= b.E) return new Dy((a.M << (a.E - b.E)) + b.M, b.E);
                return new Dy(a.M + (b.M << (b.E - a.E)), a.E);
            }
            public static Dy operator -(Dy a, Dy b) { return a + (-b); }
            public static Dy operator *(Dy a, Dy b) { return new Dy(a.M * b.M, a.E + b.E); }
            public static int Compare(Dy a, Dy b) { return (a - b).Sign; }
        }

        /// <summary> num / den with den > 0 </summary>
        internal struct Frac
        {
            public Dy N, D;
            public Frac(Dy n, Dy d) { if (d.Sign < 0) { n = -n; d = -d; } N = n; D = d; }
            /// <summary> sign of a - b </summary>
            public static int Compare(Frac a, Frac b) { return (a.N * b.D - b.N * a.D).Sign; }
            /// <summary> the double nearest to the value (ties to even, a single rounding also in the subnormal range) </summary>
            public double ToDouble() { return ExactPoint3.ToDoubleRounded(N.M, D.M, N.E - D.E); }
        }

        // ------------------------------------------------------------------------------------------------------------
        // the slabs
        // ------------------------------------------------------------------------------------------------------------

        /// <summary> where a contact ends: kind 0 none, 1 point, 2 segment, with the end points and parameters </summary>
        internal struct Hit
        {
            public int Kind;
            public bool Exact;          // the ends come from the exact route (rounded once); else the double filter (within 6 u and 1e-300)
            public double T0, T1;
            public Vector3d P0, P1;
        }

        /// <summary> the box as it is given </summary>
        internal struct BoxSpec
        {
            public bool Aabb;
            public AxisAlignedBox3d A;
            public Box3d B;
            public double Expand;
            public static BoxSpec Of(ref AxisAlignedBox3d a, double expand) { return new BoxSpec { Aabb = true, A = a, Expand = expand }; }
            public static BoxSpec Of(ref Box3d b, double expand) { return new BoxSpec { Aabb = false, B = b, Expand = expand }; }
            public bool IsFinite()
            {
                // (the faces moved by Expand must be finite too)
                if (Aabb)
                    return fin(A.Min) && fin(A.Max) && fin(Expand) && fin(A.Min.x - Expand) && fin(A.Min.y - Expand) && fin(A.Min.z - Expand)
                        && fin(A.Max.x + Expand) && fin(A.Max.y + Expand) && fin(A.Max.z + Expand);
                return fin(B.Center) && fin(B.AxisX) && fin(B.AxisY) && fin(B.AxisZ) && fin(B.Extent) && fin(Expand)
                    && fin(B.Extent.x + Expand) && fin(B.Extent.y + Expand) && fin(B.Extent.z + Expand);
            }
        }

        static bool fin(double d) { return d - d == 0; }
        static bool fin(Vector3d v) { return fin(v.x) && fin(v.y) && fin(v.z); }

        /// <summary>
        /// The closed solid box against the component origin + t (d1 - d0), t in [tmin, tmax] (hasMin / hasMax say which bounds exist; d0 is
        /// the zero vector for a line or ray). False for a non-finite input. solid false: a segment fully inside the open box does not touch it
        /// (only the surface of the box counts).
        /// </summary>
        internal static bool Find(ref BoxSpec box, ref Vector3d o, ref Vector3d d0, ref Vector3d d1, bool hasMin, double tmin, bool hasMax, double tmax, bool solid, out Hit hit)
        {
            hit = new Hit();
            if (box.IsFinite() == false || fin(o) == false || fin(d0) == false || fin(d1) == false)
                return false;
            Vector3d dd = d1 - d0;
            // (a difference that overflows: the filter's direction would be wrong, the pair goes to the exact route)
            bool ddOverflow = fin(dd) == false;
            if (ddOverflow)
                dd = d1 * 0.5 - d0 * 0.5;       // (only its signs are used: the snap to a face)
            bool d0Zero = d0.x == 0 && d0.y == 0 && d0.z == 0;
            Acc acc = default(Acc);
            acc.Init(hasMin, tmin, hasMax, tmax);
            // (a limit of the caller beyond Huge is a number, not the infinite bound of the filter: exact route)
            bool bigLimit = (hasMin && Math.Abs(tmin) > Huge) || (hasMax && Math.Abs(tmax) > Huge);
            int verdict = (ddOverflow || bigLimit) ? 3 : FilterAll(ref box, ref o, ref d0, ref dd, d0Zero, hasMin && tmin == 0 && hasMax == false && d0Zero, false, ref acc);
            if (verdict == 0) verdict = acc.Verdict();
            else if (verdict == 1) verdict = 0;
            int act0 = acc.Act0, act1 = acc.Act1;
            hit.T0 = acc.LoBest; hit.T1 = acc.UpBest;
            if (verdict == 0) {
                hit.Kind = 0;
                return true;
            }
            bool exact = verdict == 3;
            hit.Exact = exact;
            Frac f0 = default(Frac), f1 = default(Frac);
            if (exact) {
                verdict = ExactInterval(ref box, ref o, ref d0, ref d1, hasMin, tmin, hasMax, tmax, out f0, out f1, out act0, out act1);
                if (verdict == 0) {
                    hit.Kind = 0;
                    return true;
                }
                hit.T0 = f0.ToDouble();
                hit.T1 = verdict == 2 ? f1.ToDouble() : hit.T0;
            }
            hit.Kind = verdict;
            if (verdict == 2 && d0.x == d1.x && d0.y == d1.y && d0.z == d1.z) {
                // (a segment of zero length is a point; a box contact of positive parameter length)
                verdict = hit.Kind = 1;
                hit.T1 = hit.T0;
            }
            if (verdict == 1)
                act1 = act0;
            if (solid == false && Inside(ref box, ref o, ref d0, ref d1, hasMin, tmin, hasMax, tmax)) {
                hit.Kind = 0;
                return true;
            }
            hit.P0 = Point(ref o, ref dd, ref d0, ref d1, hit.T0, exact ? f0 : default(Frac), exact);
            hit.P1 = verdict == 2 ? Point(ref o, ref dd, ref d0, ref d1, hit.T1, exact ? f1 : default(Frac), exact) : hit.P0;
            if (exact == false && o == d0 && hasMax && tmax == 1) {
                // a segment (origin = d0, end d1): the end of the component is its end point itself
                if (act0 < 0 && hit.T0 == 1) hit.P0 = d1;
                if (act1 < 0 && hit.T1 == 1) hit.P1 = d1;
            }
            if (box.Aabb) {
                // an end that the face of a slab bounds lies on that face exactly
                // (the sign of the direction from the two ends, not of dd: d1 - d0 may round to 0 or overflow where the ends differ)
                if (act0 >= 0) hit.P0[act0] = d1[act0] > d0[act0] ? Lo(ref box, act0) : Hi(ref box, act0);
                if (act1 >= 0) hit.P1[act1] = d1[act1] > d0[act1] ? Hi(ref box, act1) : Lo(ref box, act1);
                if (verdict == 1 && act0 >= 0) hit.P1 = hit.P0;
            }
            return true;
        }

        /// <summary>
        /// The shape of the old DoClipping of the line, ray and segment classes: the component origin + t direction with t in [t0, t1] (a bound
        /// of +-double.MaxValue means none), on return t0 and t1 are the ends of the contact, the points are origin + t direction, rounded
        /// (see the class summary). False for a non-finite input or no contact (type Empty, quantity 0).
        /// </summary>
        internal static bool DoClip(ref double t0, ref double t1, ref Vector3d origin, ref Vector3d direction, ref BoxSpec box, bool solid,
            ref int quantity, ref Vector3d point0, ref Vector3d point1, ref IntersectionType type)
        {
            Vector3d zero = Vector3d.Zero;
            Hit hit;
            bool hasMin = t0 > -double.MaxValue, hasMax = t1 < double.MaxValue;
            if (Find(ref box, ref origin, ref zero, ref direction, hasMin, t0, hasMax, t1, solid, out hit) == false || hit.Kind == 0) {
                quantity = 0; type = IntersectionType.Empty;
                return false;
            }
            t0 = hit.T0; t1 = hit.T1;
            point0 = hit.P0;
            if (hit.Kind == 2) {
                type = IntersectionType.Segment; quantity = 2; point1 = hit.P1;
            } else {
                type = IntersectionType.Point; quantity = 1;
            }
            return true;
        }

        /// <summary> the exact closed contact of the component with the box, no points (Test, Intersects); false for a non-finite input </summary>
        internal static bool Touches(ref BoxSpec box, ref Vector3d origin, ref Vector3d direction, bool hasMin, double tmin, bool hasMax, double tmax)
        {
            if (box.IsFinite() == false || fin(origin) == false || fin(direction) == false || IsZero(ref direction))
                return false;
            if (box.Aabb && hasMin && tmin == 0 && hasMax == false) {
                double tt;
                int r = RayAabb(ref origin, ref direction, ref box.A, box.Expand, out tt);
                if (r >= 0) return r == 1;
            }
            Vector3d zero = Vector3d.Zero;
            bool d0Zero = true;
            Acc acc = default(Acc);
            acc.Init(hasMin, tmin, hasMax, tmax);
            int v = FilterAll(ref box, ref origin, ref zero, ref direction, d0Zero, hasMin && tmin == 0 && hasMax == false, true, ref acc);
            if (v == 1) return false;
            if (v == 4) return true;
            if (v == 0) {
                v = acc.Verdict(false);         // (only the decision: the accuracy of the ends does not matter)
                if (v == 0) return false;
                if (v == 2) return true;
            }
            Hit hit;
            return Find(ref box, ref origin, ref zero, ref direction, hasMin, tmin, hasMax, tmax, true, out hit) && hit.Kind != 0;
        }

        /// <summary>
        /// The ray against an axis-aligned box, certified in a few operations: 1 a contact with a positive length, t the entry (0 for an
        /// origin inside), 0 a miss, -1 undecided (a touching contact, a value near the range limits). The origin and the faces are exact, so each
        /// t = (face - origin) / direction is within 2.5 u of its exact value (one rounding for the difference, one for the quotient).
        /// No losing-bound check of the entry t is needed (unlike <c>Acc.Verdict</c>): every candidate for the entry has a relative error of at most 2.5 u, so the true
        /// maximum of the candidates is within 2.5 u |tn| of the computed maximum whichever candidate wins; the candidate 0 (origin inside the slab) is exact.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static int RayAabb(ref Vector3d o, ref Vector3d d, ref AxisAlignedBox3d b, double ex, out double t)
        {
            t = 0;
            double tn = 0, tf = double.PositiveInfinity;
            for (int i = 0; i < 3; i++) {
                double di = d[i], oi = o[i], lo = b.Min[i] - ex, hi = b.Max[i] + ex;
                if (di > 0) {
                    if (oi > hi) return 0;                                  // (leaving the slab or parallel to it outside: exact comparisons)
                    double nl = lo - oi, nh = hi - oi;
                    if ((nl - nl != 0 || nh - nh != 0) && di > 1) return -1;        // (an overflowed difference of a quotient that is finite: the exact route)
                    if (oi < lo) { double lt = nl / di; if (lt > tn) tn = lt; }      // (an origin inside the slab enters it at t <= 0)
                    double ht = nh / di; if (ht < tf) tf = ht;
                } else if (di < 0) {
                    if (oi < lo) return 0;
                    double nh = hi - oi, nl = lo - oi;
                    if ((nl - nl != 0 || nh - nh != 0) && di < -1) return -1;
                    if (oi > hi) { double lt = nh / di; if (lt > tn) tn = lt; }
                    double ht = nl / di; if (ht < tf) tf = ht;
                } else if (oi < lo || oi > hi) {
                    return 0;
                }
            }
            // (NaN, an infinite or a subnormal value: the exact route)
            if (tf == double.PositiveInfinity || (tn != 0 && !(tn >= Tiny && tn <= Huge)) || (tf != 0 && !(Math.Abs(tf) >= Tiny && Math.Abs(tf) <= Huge)))
                return -1;
            double m = 3 * U * (Math.Abs(tn) + Math.Abs(tf)) + AbsErr;
            if (tn > tf + m) return 0;
            if (tf - tn > m) { t = tn; return 1; }
            return -1;
        }

        /// <summary> the closed ray against an oriented box, without copying the box: the decision of IntrRay3Box3.Intersects </summary>
        internal static bool RayTouchesBox3(ref Vector3d origin, ref Vector3d direction, ref Box3d b, double expand)
        {
            if (IsZero(ref direction))
                return false;       // (no direction: not a ray)
            {
                Vector3d zero = Vector3d.Zero;
                Acc acc = default(Acc);
                acc.Init(true, 0, false, 0);
                int v = FilterOriented(ref b, expand, ref origin, ref direction, true, true, ref acc);
                if (v == 4) return true;
                if (v == 1) return false;
                if (v == 0) {
                    v = acc.Verdict(false);
                    if (v == 0) return false;
                    if (v == 2) return true;
                }
            }
            BoxSpec spec = BoxSpec.Of(ref b, expand);
            return Touches(ref spec, ref origin, ref direction, true, 0, false, 0);
        }

        /// <summary>
        /// The conservative form for culling (the tree traversals): false only for a certified miss; otherwise true ("may hit"), with t a
        /// lower bound of the entry parameter of the ray (outward by the bound of its error, at least 0). It never misses a box that the
        /// exact test hits: the values t = (face - origin) / direction are within 2.5 u of their exact values (one rounding of the difference, one of
        /// the quotient) plus 2^-1070 of underflow, so tn &gt; tf + 3 u (|tn| + |tf|) + 1e-320 cannot hold for an exact hit; an overflowed
        /// quotient is an infinity of the right sign. Where nothing is certified (touching, parallel on a face, subnormal values) the answer is
        /// true and no exact computation is made. A difference that overflows needs no guard: the infinity it gives makes the margin `m` infinite or NaN, so `tn > tf + m` is false ("may hit"); the limits of the caller do not occur here (the ray starts at 0).
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static bool RayAabbMay(ref Vector3d o, ref Vector3d d, ref AxisAlignedBox3d b, double ex, out double t)
        {
            double tn = 0, tf = double.PositiveInfinity;
            for (int i = 0; i < 3; i++) {
                double di = d[i], oi = o[i], lo = b.Min[i] - ex, hi = b.Max[i] + ex;
                if (di > 0) {
                    if (oi > hi) { t = 0; return false; }
                    if (oi < lo) { double lt = (lo - oi) / di; if (lt > tn) tn = lt; }
                    double ht = (hi - oi) / di; if (ht < tf) tf = ht;
                } else if (di < 0) {
                    if (oi < lo) { t = 0; return false; }
                    if (oi > hi) { double lt = (hi - oi) / di; if (lt > tn) tn = lt; }
                    double ht = (lo - oi) / di; if (ht < tf) tf = ht;
                } else if (oi < lo || oi > hi) {
                    t = 0; return false;
                }
            }
            double m = 3 * U * (Math.Abs(tn) + Math.Abs(tf)) + AbsErr;
            if (tn > tf + m) { t = 0; return false; }
            t = tn - 3 * U * tn - AbsErr;
            if (!(t >= 0)) t = 0;       // (also for an infinite tn: inf - inf is NaN, and a lower bound of 0 is always valid)
            return true;
        }

        /// <summary> the parameter of the entry of the ray into the box (0 for an origin inside); false for a miss or a non-finite input </summary>
        internal static bool RayEntry(ref BoxSpec box, ref Vector3d origin, ref Vector3d direction, out double t)
        {
            t = 0;
            if (box.IsFinite() == false || fin(origin) == false || fin(direction) == false || IsZero(ref direction))
                return false;
            if (box.Aabb) {
                int r = RayAabb(ref origin, ref direction, ref box.A, box.Expand, out t);
                if (r >= 0) return r == 1;
            }
            Vector3d zero = Vector3d.Zero;
            Acc acc = default(Acc);
            acc.Init(true, 0, false, 0);
            int v = FilterAll(ref box, ref origin, ref zero, ref direction, true, true, false, ref acc);
            if (v == 1) return false;
            if (v == 0) {
                v = acc.Verdict();
                if (v == 0) return false;
                if (v == 2) { t = acc.LoBest; return true; }
            }
            Hit hit;
            if (Find(ref box, ref origin, ref zero, ref direction, true, 0, false, 0, true, out hit) == false || hit.Kind == 0)
                return false;
            t = hit.T0;
            return true;
        }

        static bool IsZero(ref Vector3d d) { return d.x == 0 && d.y == 0 && d.z == 0; }

        internal static bool IsFinite(ref Vector3d o, ref Vector3d d) { return fin(o) && fin(d); }

        /// <summary> the bounds of slab i as doubles: the faces of an axis-aligned box (expanded), or -e and e of an oriented one </summary>
        static double Lo(ref BoxSpec box, int i) { return box.Aabb ? (box.Expand != 0 ? box.A.Min[i] - box.Expand : box.A.Min[i]) : -(box.B.Extent[i] + box.Expand); }
        static double Hi(ref BoxSpec box, int i) { return box.Aabb ? (box.Expand != 0 ? box.A.Max[i] + box.Expand : box.A.Max[i]) : box.B.Extent[i] + box.Expand; }

        /// <summary> the error bound of t = num / q, num with its own error </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static double TErr(double errNum, double errQ, double ri, double t)
        {
            // ri = 1.0000001 / (|q| - errQ)
            return (errNum + Math.Abs(t) * errQ) * ri + U * Math.Abs(t) * 2 + AbsErr;
        }

        /// <summary> the intervals of the ends of t that the slabs give, with the bounds of their error </summary>
        internal struct Acc
        {
            public double LoMinus, LoPlus, UpPlus, UpMinus, LoBest, UpBest, LoBestErr, UpBestErr;
            public int Act0, Act1;
            public void Init(bool hasMin, double tmin, bool hasMax, double tmax)
            {
                LoMinus = LoPlus = LoBest = double.NegativeInfinity; UpPlus = UpMinus = UpBest = double.PositiveInfinity;
                LoBestErr = UpBestErr = 0; Act0 = Act1 = -1;
                if (hasMin) { LoMinus = LoPlus = LoBest = tmin; }
                if (hasMax) { UpPlus = UpMinus = UpBest = tmax; }
            }
            /// <summary> 0 certified miss, 2 certified contact with a positive length, 3 undecided </summary>
            public int Verdict(bool accurateEnds = true)
            {
                if (LoMinus > UpPlus) return 0;
                if (LoPlus < UpMinus) {
                    if (accurateEnds && (LoBest == double.NegativeInfinity || UpBest == double.PositiveInfinity)) return 3;
                    // the ends are returned from this route only when they are accurate to a few ulp (a slab with a cancelling P is not). The two checks of the winner's own
                    // error are implied by the checks of the intervals below (LoPlus >= LoBest + LoBestErr, UpMinus <= UpBest - UpBestErr): kept as the cheap first test
                    if (accurateEnds && (LoBestErr > 6 * U * Math.Abs(LoBest) + 1e-300 || UpBestErr > 6 * U * Math.Abs(UpBest) + 1e-300)) return 3;
                    // (and the losing bounds: the exact end is the largest lower bound, in [LoMinus, LoPlus], and the smallest upper bound, in [UpMinus, UpPlus];
                    // a losing bound whose interval reaches past the winner can be the true end, eg a slab whose P cancels)
                    if (accurateEnds && (LoPlus - LoBest > 6 * U * Math.Abs(LoBest) + 1e-300 || UpBest - UpMinus > 6 * U * Math.Abs(UpBest) + 1e-300)) return 3;
                    return 2;
                }
                return 3;
            }
        }

        /// <summary>
        /// The kind of a computed slab bound v = n / Q with the bound ev of its error: 0 a number (|v| + ev &lt;= Huge: its exact value is within
        /// +-Huge and the relative error bounds hold), 1 an infinity (the exact value is certainly beyond 2 Huge), 2 undecided. For an infinity:
        /// the exact numerator is at least |n| (1 - 2u) - eP in magnitude (n rounded once, or beyond 1.7e308 where n overflowed to an infinity),
        /// the exact |Q| at most aq + eQ, so |exact v| &gt; 2 Huge whenever that quotient is. NaN or an infinite error give 2.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static int Kind(double v, double ev, double n, double eP, double aq, double eQ)
        {
            if (Math.Abs(v) + ev <= Huge)
                return 0;
            double mag = (n - n == 0) ? Math.Abs(n) * (1 - 2 * U) : 1.7e308;
            return (mag - eP - AbsErr > 2 * Huge * (aq + eQ)) ? 1 : 2;
        }

        /// <summary> one slab: 0 continue, 1 certified miss, 3 undecided (P, eP, Q and eQ finite: FilterOriented checks them, the axis-aligned box has finite inputs) </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static int Step(int i, double P, double eP, double Q, double eQ, double L, double H, bool ray, ref Acc c)
        {
            if (Q == 0 && eQ == 0) {
                // parallel to the slab, exactly: P itself against L and H
                if (P + eP < L || P - eP > H) return 1;
                if (L < P - eP && P + eP < H) return 0;
                if (eP == 0 && P >= L && P <= H) return 0;      // (a touching contact of a point on a face: exact for an axis-aligned box)
                return 3;
            }
            double aq = Math.Abs(Q);
            if (aq <= 2 * eQ || aq < Tiny || aq > Huge)
                return 3;
            double n1 = L - P, n2 = H - P;
            double en1 = eP + U * Math.Abs(n1) + AbsErr, en2 = eP + U * Math.Abs(n2) + AbsErr;
            bool exactPQ = eP == 0 && eQ == 0;
            double ri = 1.0000001 / (aq - eQ);
            if (ray && ((Q > 0 && n1 + en1 < 0) || (Q < 0 && n2 - en2 > 0))) {
                // a ray whose origin is certainly inside the slab on the entering side: the lower bound of t is negative, below the limit 0 of
                // the ray, so only the upper bound is needed (one division)
                double nu = Q > 0 ? n2 : n1, enu = Q > 0 ? en2 : en1;
                double uv0 = nu / Q;
                double ue0 = exactPQ ? 2.5 * U * Math.Abs(uv0) + AbsErr : TErr(enu, eQ, ri, uv0);
                int ku = Kind(uv0, ue0, nu, eP, aq, eQ);
                if (ku == 2)
                    return 3;
                if (ku == 1) { uv0 = ((nu > 0) == (Q > 0)) ? double.PositiveInfinity : double.NegativeInfinity; ue0 = 0; }
                else if (uv0 != 0 && Math.Abs(uv0) < Tiny)
                    return 3;
                if (uv0 + ue0 < c.UpPlus) c.UpPlus = uv0 + ue0;
                if (uv0 - ue0 < c.UpMinus) c.UpMinus = uv0 - ue0;
                if (uv0 < c.UpBest) { c.UpBest = uv0; c.UpBestErr = ue0; c.Act1 = i; }
                return 0;
            }
            double a = n1 / Q, b = n2 / Q;
            double ea = exactPQ ? 2.5 * U * Math.Abs(a) + AbsErr : TErr(en1, eQ, ri, a);
            double eb = exactPQ ? 2.5 * U * Math.Abs(b) + AbsErr : TErr(en2, eQ, ri, b);
            // (a bound is a number (|a| + ea <= 1e290, the relative bounds hold), or certainly beyond every number that the other bounds and the
            // limits of the caller can reach, with their errors: an infinity, compared with the other bounds; anything between is undecided.
            // docs/design/BOX3_EXACT_FILTER.md "The infinite bounds")
            int ka = Kind(a, ea, n1, eP, aq, eQ), kb = Kind(b, eb, n2, eP, aq, eQ);
            if (ka == 2 || kb == 2)
                return 3;
            if (ka == 0 && a != 0 && Math.Abs(a) < Tiny || kb == 0 && b != 0 && Math.Abs(b) < Tiny)
                return 3;
            if (ka == 1) { a = ((n1 > 0) == (Q > 0)) ? double.PositiveInfinity : double.NegativeInfinity; ea = 0; }
            if (kb == 1) { b = ((n2 > 0) == (Q > 0)) ? double.PositiveInfinity : double.NegativeInfinity; eb = 0; }
            // (both bounds certainly beyond reach on opposite sides: the slab is wider than any t, no constraint; on the same side: not decided)
            if (ka == 1 && kb == 1)
                return (a < 0) != (b < 0) ? 0 : 3;
            double lv, le, uv, ue;
            if (Q > 0) { lv = a; le = ea; uv = b; ue = eb; }
            else { lv = b; le = eb; uv = a; ue = ea; }
            if (lv - le > c.LoMinus) c.LoMinus = lv - le;
            if (lv + le > c.LoPlus) c.LoPlus = lv + le;
            if (lv > c.LoBest) { c.LoBest = lv; c.LoBestErr = le; c.Act0 = i; }
            if (uv + ue < c.UpPlus) c.UpPlus = uv + ue;
            if (uv - ue < c.UpMinus) c.UpMinus = uv - ue;
            if (uv < c.UpBest) { c.UpBest = uv; c.UpBestErr = ue; c.Act1 = i; }
            return 0;
        }

        /// <summary> the three slabs from the box and the component (no allocation) </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        static int FilterAll(ref BoxSpec box, ref Vector3d o, ref Vector3d d0, ref Vector3d dd, bool d0Zero, bool ray, bool quick, ref Acc c)
        {
            if (box.Aabb) {
                double ex = box.Expand;
                for (int i = 0; i < 3; i++) {
                    double lo = box.A.Min[i], hi = box.A.Max[i];
                    if (ex != 0) { lo -= ex; hi += ex; }
                    double q = dd[i];
                    // (q = d1 - d0 is exactly 0 only for d1 == d0 (the subtraction is exact where it gives zero or a subnormal): then no error, and an axis-parallel
                    // segment is decided in the slab by comparisons, not by the exact route)
                    int v = Step(i, o[i], 0, q, (d0Zero || q == 0) ? 0 : U * Math.Abs(q) + AbsErr, lo, hi, ray, ref c);
                    if (v != 0) return v;
                    if (c.LoMinus > c.UpPlus) return 1;
                }
                return 0;
            }
            return FilterOriented(ref box.B, box.Expand, ref o, ref dd, ray, quick, ref c);
        }

        /// <summary> P, Q of a slab of an oriented box (o - Center and the direction on the axis u) with the bounds of their error </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static void Prep(double ux, double uy, double uz, double cx, double cy, double cz, ref Vector3d o, ref Vector3d dd, out double P, out double eP, out double Q, out double eQ)
        {
            double x = (o.x - cx) * ux, y = (o.y - cy) * uy, z = (o.z - cz) * uz;
            double a = dd.x * ux, b = dd.y * uy, w = dd.z * uz;
            P = (x + y) + z; eP = 6 * U * (Math.Abs(x) + Math.Abs(y) + Math.Abs(z)) + AbsErr;
            Q = (a + b) + w;
            // (a direction component that is exactly zero, or a zero axis component, gives an exact zero product: no error)
            eQ = ((dd.x == 0 || ux == 0) && (dd.y == 0 || uy == 0) && (dd.z == 0 || uz == 0)) ? 0 : 6 * U * (Math.Abs(a) + Math.Abs(b) + Math.Abs(w)) + AbsErr;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static bool Away(double P, double eP, double Q, double eQ, double e)
        {
            // a ray that starts beyond a face of the slab and does not move toward it (the sign of Q certified) misses
            return (P - eP > e && Q >= eQ && Q >= 0) || (P + eP < -e && -Q >= eQ && Q <= 0);
        }

        /// <summary>
        /// the three slabs of an oriented box, no allocation, no copy of the box. 1: certified miss; 3: undecided; 0: the bounds are in c;
        /// for a ray and quick: 4 when the origin is certainly inside all three slabs (a hit at t = 0, no bounds computed)
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        static int FilterOriented(ref Box3d bx, double expand, ref Vector3d o, ref Vector3d dd, bool ray, bool quick, ref Acc c)
        {
            double cx = bx.Center.x, cy = bx.Center.y, cz = bx.Center.z;
            double e0 = bx.Extent.x + expand, e1 = bx.Extent.y + expand, e2 = bx.Extent.z + expand;
            double P0, eP0, Q0, eQ0, P1, eP1, Q1, eQ1, P2, eP2, Q2, eQ2;
            Prep(bx.AxisX.x, bx.AxisX.y, bx.AxisX.z, cx, cy, cz, ref o, ref dd, out P0, out eP0, out Q0, out eQ0);
            Prep(bx.AxisY.x, bx.AxisY.y, bx.AxisY.z, cx, cy, cz, ref o, ref dd, out P1, out eP1, out Q1, out eQ1);
            Prep(bx.AxisZ.x, bx.AxisZ.y, bx.AxisZ.z, cx, cy, cz, ref o, ref dd, out P2, out eP2, out Q2, out eQ2);
            // (a NaN or an infinity in an input, or an overflow, eg (origin - center) * 0 with an overflowed difference = inf * 0 = NaN: every comparison
            // below would be false, the slab would constrain nothing and a miss could be certified as a hit; one test of the sum of the values of
            // all slabs, one of the extents, the exact route decides)
            // (the extents each: an expand of double.MaxValue, as DCurve3BoxTree starts with, is finite, only the sum of three of them overflows)
            double fs = ((P0 + eP0) + (Q0 + eQ0)) + ((P1 + eP1) + (Q1 + eQ1)) + ((P2 + eP2) + (Q2 + eQ2));
            if ((fs - fs) + ((e0 - e0) + (e1 - e1) + (e2 - e2)) != 0)
                return 3;
            if (ray) {
                // the cheap tests of all slabs first: a miss, or an origin strictly inside all slabs (in the box)
                if (Away(P0, eP0, Q0, eQ0, e0) || Away(P1, eP1, Q1, eQ1, e1) || Away(P2, eP2, Q2, eQ2, e2))
                    return 1;
                if (quick && P0 - eP0 > -e0 && P0 + eP0 < e0 && P1 - eP1 > -e1 && P1 + eP1 < e1 && P2 - eP2 > -e2 && P2 + eP2 < e2)
                    return 4;
            }
            int v = Step(0, P0, eP0, Q0, eQ0, -e0, e0, ray, ref c);
            if (v != 0) return v;
            if (c.LoMinus > c.UpPlus) return 1;
            v = Step(1, P1, eP1, Q1, eQ1, -e1, e1, ray, ref c);
            if (v != 0) return v;
            if (c.LoMinus > c.UpPlus) return 1;
            v = Step(2, P2, eP2, Q2, eQ2, -e2, e2, ray, ref c);
            if (v != 0) return v;
            if (c.LoMinus > c.UpPlus) return 1;
            return 0;
        }

        // ------------------------------------------------------------------------------------------------------------
        // the exact route
        // ------------------------------------------------------------------------------------------------------------

        static Dy DotExact(Vector3d u, Vector3d a, Vector3d b)
        {
            // sum (a_k - b_k) u_k
            Dy r = Dy.From(0);
            for (int k = 0; k < 3; k++)
                r = r + (Dy.From(a[k]) - Dy.From(b[k])) * Dy.From(u[k]);
            return r;
        }

        /// <summary> the exact P, Q, L, H of slab i: L &lt;= P + t Q &lt;= H </summary>
        static void Exact(ref BoxSpec box, int i, ref Vector3d o, ref Vector3d d0, ref Vector3d d1, out Dy P, out Dy Q, out Dy L, out Dy H)
        {
            if (box.Aabb) {
                P = Dy.From(o[i]);
                Q = Dy.From(d1[i]) - Dy.From(d0[i]);
            } else {
                Vector3d u = box.B.Axis(i);
                P = DotExact(u, o, box.B.Center);
                Q = DotExact(u, d1, d0);
            }
            L = Dy.From(Lo(ref box, i)); H = Dy.From(Hi(ref box, i));
        }

        /// <summary> 0 miss, 1 point (t0 == t1), 2 segment; the end fractions and the slabs that bound them (-1: the limits of t) </summary>
        static int ExactInterval(ref BoxSpec box, ref Vector3d o, ref Vector3d d0, ref Vector3d d1, bool hasMin, double tmin, bool hasMax, double tmax, out Frac lo, out Frac hi, out int act0, out int act1)
        {
            bool haveLo = false, haveHi = false;
            lo = default(Frac); hi = default(Frac); act0 = -1; act1 = -1;
            if (hasMin) { lo = new Frac(Dy.From(tmin), Dy.From(1)); haveLo = true; }
            if (hasMax) { hi = new Frac(Dy.From(tmax), Dy.From(1)); haveHi = true; }
            for (int i = 0; i < 3; i++) {
                Dy P, Q, L, H;
                Exact(ref box, i, ref o, ref d0, ref d1, out P, out Q, out L, out H);
                if (Q.Sign == 0) {
                    if ((L - P).Sign > 0 || (P - H).Sign > 0)
                        return 0;
                    continue;
                }
                Frac a = new Frac(L - P, Q), b = new Frac(H - P, Q);
                Frac fl = Q.Sign > 0 ? a : b, fu = Q.Sign > 0 ? b : a;
                if (haveLo == false || Frac.Compare(fl, lo) > 0) { lo = fl; haveLo = true; act0 = i; }
                if (haveHi == false || Frac.Compare(fu, hi) < 0) { hi = fu; haveHi = true; act1 = i; }
            }
            if (haveLo == false || haveHi == false) {
                // (no direction: not a component)
                return 0;
            }
            int c = Frac.Compare(lo, hi);
            if (c > 0) return 0;
            return c == 0 ? 1 : 2;
        }

        /// <summary> true if both ends of the (bounded) component are strictly inside all slabs: the whole segment is in the open box </summary>
        static bool Inside(ref BoxSpec box, ref Vector3d o, ref Vector3d d0, ref Vector3d d1, bool hasMin, double tmin, bool hasMax, double tmax)
        {
            if (hasMin == false || hasMax == false)
                return false;
            for (int i = 0; i < 3; i++) {
                Dy P, Q, L, H;
                Exact(ref box, i, ref o, ref d0, ref d1, out P, out Q, out L, out H);
                Dy v0 = P + Q * Dy.From(tmin), v1 = P + Q * Dy.From(tmax);
                if ((v0 - L).Sign <= 0 || (H - v0).Sign <= 0 || (v1 - L).Sign <= 0 || (H - v1).Sign <= 0)
                    return false;
            }
            return true;
        }

        /// <summary> o + t d at an end of the contact: on the face of the active slab exactly (axis-aligned box); from the exact fraction rounded once on the exact route </summary>
        static Vector3d Point(ref Vector3d o, ref Vector3d dd, ref Vector3d d0, ref Vector3d d1, double t, Frac f, bool exact)
        {
            Vector3d p = new Vector3d();
            if (exact) {
                for (int k = 0; k < 3; k++) {
                    // o_k + (N / D) (d1_k - d0_k) = (o_k D + N d_k) / D
                    Dy dk = Dy.From(d1[k]) - Dy.From(d0[k]);
                    Dy num = Dy.From(o[k]) * f.D + f.N * dk;
                    p[k] = ExactPoint3.ToDoubleRounded(num.M, f.D.M, num.E - f.D.E);
                }
            } else {
                p = new Vector3d(o.x + t * dd.x, o.y + t * dd.y, o.z + t * dd.z);
            }
            return p;
        }
    }
}
