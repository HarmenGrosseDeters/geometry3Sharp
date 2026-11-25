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
    /// Optional for an IntervalImplicit3d: enclosures that reuse work between calls through a cache that the caller owns,
    /// so that the function itself keeps no state (requirement (iii) of IntervalImplicit3d). NewEncloseCache may be called
    /// concurrently from multiple threads; each returned cache is used by one thread at a time and with the function that
    /// made it. The result is the one of Enclose(x, y, z), bit for bit.
    /// NewEncloseCache may return null (no cache to offer): the caller then uses Enclose(x, y, z).
    /// </summary>
    internal interface IntervalImplicit3dCached : IntervalImplicit3d
    {
        object NewEncloseCache();
        IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z, object cache);
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
            SinCosReduced(x, Math.Round(x * 0.6366197723675814), out sin, out cos);
        }

        /// <summary>
        /// Upper bounds of a^15 / 15! (sin) and a^14 / 14! (cos) for a >= 0: the Lagrange remainders of the two
        /// polynomials for |r| &lt;= a. Every step is an interval operation, and the result is the upper end.
        /// </summary>
        internal static void RemainderBounds(double a, out double sinBound, out double cosBound)
        {
            ExactInterval a1 = ExactInterval.Of(a);
            ExactInterval a2 = a1 * a1, a4 = a2 * a2, a8 = a4 * a4, a14 = a8 * a4 * a2;
            cosBound = (a14 / ExactInterval.Of(Factorial14)).Hi; sinBound = (a14 * a1 / ExactInterval.Of(Factorial15)).Hi;
        }

        /// <summary>
        /// sin x and cos x from the reduction x = k pi / 2 + r with the given integer k (a double with an integer value).
        /// [-1, 1] if |r| may exceed MaxReduced: the remainder bounds hold for any k that passes this check.
        /// </summary>
        internal static void SinCosReduced(double x, double k, out ExactInterval sin, out ExactInterval cos)
        {
            sin = Unit; cos = Unit;
            ExactInterval r = ExactInterval.Of(x) - ExactInterval.Of(k) * PiHalf;
            if (!(Math.Max(Math.Abs(r.Lo), Math.Abs(r.Hi)) <= MaxReduced))
                return;
            ExactInterval r2 = ExactInterval.Square(r);
            double sinBound, cosBound;
            RemainderBounds(Math.Max(Math.Abs(r.Lo), Math.Abs(r.Hi)), out sinBound, out cosBound);
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
    internal sealed class IntervalTpms : IntervalImplicit3dCached
    {
        public readonly TpmsType Type;
        public readonly double K, Level;
        public readonly bool Negate;

        public IntervalTpms(TpmsType type, double k, double level, bool negate = false)
        {
            Type = type; K = k; Level = level; Negate = negate;
        }

        static ExactInterval c(double v) { return ExactInterval.Of(v); }

        internal struct SinCosEntry
        {
            public ExactInterval SinIv, CosIv;
            public Affine1 SinA, CosA;
            public double Mid, Half;
        }

        // Largest phase half-width for which IntervalTpms builds an Affine3 enclosure to intersect with the plain interval.
        // Above 0.6 rad the quadratic product remainder of Affine3 exceeds the plain interval hull, so coarse blocks skip Affine3.
        const double MaxAffinePhaseHalf = 0.6;

        /// <summary>
        /// The sines and cosines of phase intervals that were computed before, by the bits of the interval's ends. The
        /// function is a sum of products of sines and cosines of one coordinate each, and the boxes of a caller on a grid
        /// share their coordinate intervals: the 64 sub-boxes of a cube have 4 intervals per axis.
        /// </summary>
        internal sealed class SinCosCache
        {
            internal const int MaxEntries = 1 << 16;
            internal readonly bool WithAffine;
            internal readonly System.Collections.Generic.Dictionary<ValueTuple<long, long>, SinCosEntry> Map
                = new System.Collections.Generic.Dictionary<ValueTuple<long, long>, SinCosEntry>();
            /// <summary> the calls that found their interval, and those that computed it </summary>
            internal long Hits, Misses;

            internal SinCosCache(bool withAffine) { WithAffine = withAffine; }
        }

        public object NewEncloseCache() { return new SinCosCache(Type != TpmsType.SchwarzP); }

        public IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z) { return enclose(x, y, z, null); }

        public IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z, object cache)
        {
            var own = cache as SinCosCache;
            if (own == null || own.WithAffine != (Type != TpmsType.SchwarzP))
                throw new ArgumentException("not a cache of NewEncloseCache", nameof(cache));
            return enclose(x, y, z, own);
        }

        static SinCosEntry computeEntry(ExactInterval p, bool withAffine)
        {
            ExactInterval sinIv, cosIv;
            IntervalMath.SinCos(p, out sinIv, out cosIv);
            if (!withAffine)
                return new SinCosEntry { SinIv = sinIv, CosIv = cosIv };
            if (!Affine3.IsFiniteDouble(p.Lo) || !Affine3.IsFiniteDouble(p.Hi) || !(p.Lo <= p.Hi))
                return new SinCosEntry { SinIv = sinIv, CosIv = cosIv, SinA = Affine1.FromInterval(sinIv), CosA = Affine1.FromInterval(cosIv), Mid = double.NaN, Half = double.PositiveInfinity };
            double mid, half;
            Affine1.MidRad(p, out mid, out half);
            Affine1 sinA, cosA;
            if (half > 0 && half <= Affine1.MaxTaylorHalf)
                Affine1.SinCos(p, out sinA, out cosA, out mid, out half);
            else {
                sinA = Affine1.FromInterval(sinIv);
                cosA = Affine1.FromInterval(cosIv);
            }
            return new SinCosEntry { SinIv = sinIv, CosIv = cosIv, SinA = sinA, CosA = cosA, Mid = mid, Half = half };
        }

        // IntervalMath.SinCos (and Affine1.SinCos when withAffine), or the kept result of the same call
        static SinCosEntry lookupSinCos(ExactInterval p, bool withAffine, SinCosCache cache)
        {
            if (cache == null) return computeEntry(p, withAffine);
            var key = new ValueTuple<long, long>(BitConverter.DoubleToInt64Bits(p.Lo), BitConverter.DoubleToInt64Bits(p.Hi));
            SinCosEntry kept;
            if (cache.Map.TryGetValue(key, out kept)) { ++cache.Hits; return kept; }
            ++cache.Misses;
            kept = computeEntry(p, withAffine);
            if (cache.Map.Count >= SinCosCache.MaxEntries) cache.Map.Clear();
            cache.Map[key] = kept;
            return kept;
        }

        // Intersection of two sound enclosures of the same real set (falls back to Whole on NaN or Hull if disjoint).
        static ExactInterval intersect(ExactInterval a, ExactInterval b)
        {
            if (double.IsNaN(a.Lo) || double.IsNaN(a.Hi) || double.IsNaN(b.Lo) || double.IsNaN(b.Hi))
                return Affine3.Whole;
            double lo = Math.Max(a.Lo, b.Lo), hi = Math.Min(a.Hi, b.Hi);
            return lo <= hi ? new ExactInterval(lo, hi) : IntervalMath.Hull(a, b);
        }

        // Align 2*p's affine form to the parameter u = (theta - mid) / half of p.
        // Whenever 2*mid and 2*half match the doubled interval's parameterization (all normal finite boxes),
        // the linear correlation is exact; otherwise fold C1 into Rad so soundness holds unconditionally.
        static void alignDouble(SinCosEntry e2, double mid, double half, out Affine1 sin2, out Affine1 cos2)
        {
            sin2 = e2.SinA; cos2 = e2.CosA;
            if (!(e2.Mid == 2 * mid && e2.Half == 2 * half && Affine3.IsFiniteDouble(e2.Mid) && Affine3.IsFiniteDouble(e2.Half))) {
                sin2 = sin2.ConstantOnly();
                cos2 = cos2.ConstantOnly();
            }
        }

        IntervalEnclosure enclose(ExactInterval x, ExactInterval y, ExactInterval z, SinCosCache cache)
        {
            ExactInterval k = c(K);
            ExactInterval px = k * x, py = k * y, pz = k * z;
            bool withAffine = Type != TpmsType.SchwarzP;
            SinCosEntry ex = lookupSinCos(px, withAffine, cache);
            SinCosEntry ey = lookupSinCos(py, withAffine, cache);
            SinCosEntry ez = lookupSinCos(pz, withAffine, cache);
            ExactInterval isx = ex.SinIv, icx = ex.CosIv, isy = ey.SinIv, icy = ey.CosIv, isz = ez.SinIv, icz = ez.CosIv;
            SinCosEntry e2x = default(SinCosEntry), e2y = default(SinCosEntry), e2z = default(SinCosEntry);
            bool hasDouble = Type == TpmsType.IWP || Type == TpmsType.Lidinoid;
            if (hasDouble) {
                e2x = lookupSinCos(new ExactInterval(2 * px.Lo, 2 * px.Hi), withAffine, cache);
                e2y = lookupSinCos(new ExactInterval(2 * py.Lo, 2 * py.Hi), withAffine, cache);
                e2z = lookupSinCos(new ExactInterval(2 * pz.Lo, 2 * pz.Hi), withAffine, cache);
            }

            // Plain interval enclosure (optimal for SchwarzP, points, and coarse boxes; intersected with Affine3 on fine/medium boxes).
            ExactInterval g, gx, gy, gz;
            switch (Type) {
                case TpmsType.Gyroid:
                    g = isx * icy + isy * icz + isz * icx;
                    gx = icx * icy - isz * isx; gy = icy * icz - isx * isy; gz = icz * icx - isy * isz;
                    break;
                case TpmsType.SchwarzP:
                    g = icx + icy + icz;
                    gx = -isx; gy = -isy; gz = -isz;
                    break;
                case TpmsType.SchwarzD:
                    g = isx * isy * isz + isx * icy * icz + icx * isy * icz + icx * icy * isz;
                    gx = icx * isy * isz + icx * icy * icz - isx * isy * icz - isx * icy * isz;
                    gy = isx * icy * isz - isx * isy * icz + icx * icy * icz - icx * isy * isz;
                    gz = isx * isy * icz - isx * icy * isz - icx * isy * isz + icx * icy * icz;
                    break;
                case TpmsType.Neovius:
                    g = c(3) * (icx + icy + icz) + c(4) * (icx * icy * icz);
                    gx = -(c(3) * isx) - c(4) * (isx * icy * icz);
                    gy = -(c(3) * isy) - c(4) * (icx * isy * icz);
                    gz = -(c(3) * isz) - c(4) * (icx * icy * isz);
                    break;
                case TpmsType.IWP: {
                    ExactInterval is2x = e2x.SinIv, ic2x = e2x.CosIv, is2y = e2y.SinIv, ic2y = e2y.CosIv, is2z = e2z.SinIv, ic2z = e2z.CosIv;
                    g = c(2) * (icx * icy + icy * icz + icz * icx) - (ic2x + ic2y + ic2z);
                    gx = c(2) * is2x - c(2) * (isx * (icy + icz));
                    gy = c(2) * is2y - c(2) * (isy * (icx + icz));
                    gz = c(2) * is2z - c(2) * (isz * (icx + icy));
                    break;
                }
                case TpmsType.Lidinoid: {
                    ExactInterval is2x = e2x.SinIv, ic2x = e2x.CosIv, is2y = e2y.SinIv, ic2y = e2y.CosIv, is2z = e2z.SinIv, ic2z = e2z.CosIv;
                    ExactInterval h = c(0.5);
                    g = h * (is2x * icy * isz + is2y * icz * isx + is2z * icx * isy) - h * (ic2x * ic2y + ic2y * ic2z + ic2z * ic2x) + c(0.15);
                    gx = ic2x * icy * isz + h * (is2y * icz * icx) - h * (is2z * isx * isy) + is2x * (ic2y + ic2z);
                    gy = ic2y * icz * isx + h * (is2z * icx * icy) - h * (is2x * isy * isz) + is2y * (ic2x + ic2z);
                    gz = ic2z * icx * isy + h * (is2x * icy * icz) - h * (is2y * isz * isx) + is2z * (ic2x + ic2y);
                    break;
                }
                default: throw new ArgumentOutOfRangeException(nameof(Type));
            }

            // Intersect with Affine3 on non-point, non-coarse boxes to eliminate O(h) dependency bloat without ever widening coarse boxes.
            if (withAffine) {
                double maxHalf = Math.Max(ex.Half, Math.Max(ey.Half, ez.Half));
                if (maxHalf > 0 && maxHalf <= MaxAffinePhaseHalf) {
                    Affine3 sx = ex.SinA.OnAxis(0), cx = ex.CosA.OnAxis(0);
                    Affine3 sy = ey.SinA.OnAxis(1), cy = ey.CosA.OnAxis(1);
                    Affine3 sz = ez.SinA.OnAxis(2), cz = ez.CosA.OnAxis(2);
                    Affine3 ag, agx, agy, agz;
                    switch (Type) {
                        case TpmsType.Gyroid:
                            ag = sx * cy + sy * cz + sz * cx;
                            agx = cx * cy - sz * sx; agy = cy * cz - sx * sy; agz = cz * cx - sy * sz;
                            break;
                        case TpmsType.SchwarzD: {
                            Affine3 sxsy = sx * sy, cxcy = cx * cy, sxcy = sx * cy, cxsy = cx * sy;
                            Affine3 cosDiffXY = sxsy + cxcy;        // cos(x - y)
                            Affine3 cosSumXY = cxcy - sxsy;         // cos(x + y)
                            Affine3 sinSumXY = sxcy + cxsy;         // sin(x + y)
                            Affine3 sinDiffXY = sxcy - cxsy;        // sin(x - y)
                            ag = cosDiffXY * sz + sinSumXY * cz;
                            agx = cosSumXY * cz - sinDiffXY * sz;
                            agy = cosSumXY * cz + sinDiffXY * sz;
                            agz = cosDiffXY * cz - sinSumXY * sz;
                            break;
                        }
                        case TpmsType.Neovius: {
                            Affine3 cxcy = cx * cy, cycz = cy * cz, cxcz = cx * cz;
                            ag = (cx + cy + cz).Scale(3) + (cxcy * cz).Scale(4);
                            agx = -(sx * (cycz.Scale(4).AddConst(3)));
                            agy = -(sy * (cxcz.Scale(4).AddConst(3)));
                            agz = -(sz * (cxcy.Scale(4).AddConst(3)));
                            break;
                        }
                        case TpmsType.IWP: {
                            Affine1 s21x, c21x, s21y, c21y, s21z, c21z;
                            alignDouble(e2x, ex.Mid, ex.Half, out s21x, out c21x);
                            alignDouble(e2y, ey.Mid, ey.Half, out s21y, out c21y);
                            alignDouble(e2z, ez.Mid, ez.Half, out s21z, out c21z);
                            Affine3 s2x = s21x.OnAxis(0), c2x = c21x.OnAxis(0);
                            Affine3 s2y = s21y.OnAxis(1), c2y = c21y.OnAxis(1);
                            Affine3 s2z = s21z.OnAxis(2), c2z = c21z.OnAxis(2);
                            ag = (cx * cy + cy * cz + cz * cx).Scale(2) - (c2x + c2y + c2z);
                            agx = (s2x - sx * (cy + cz)).Scale(2);
                            agy = (s2y - sy * (cx + cz)).Scale(2);
                            agz = (s2z - sz * (cx + cy)).Scale(2);
                            break;
                        }
                        case TpmsType.Lidinoid: {
                            Affine1 s21x, c21x, s21y, c21y, s21z, c21z;
                            alignDouble(e2x, ex.Mid, ex.Half, out s21x, out c21x);
                            alignDouble(e2y, ey.Mid, ey.Half, out s21y, out c21y);
                            alignDouble(e2z, ez.Mid, ez.Half, out s21z, out c21z);
                            Affine3 s2x = s21x.OnAxis(0), c2x = c21x.OnAxis(0);
                            Affine3 s2y = s21y.OnAxis(1), c2y = c21y.OnAxis(1);
                            Affine3 s2z = s21z.OnAxis(2), c2z = c21z.OnAxis(2);
                            Affine3 cySz = cy * sz, czSx = cz * sx, cxSy = cx * sy;
                            ag = (s2x * cySz + s2y * czSx + s2z * cxSy).Scale(0.5)
                                - (c2x * c2y + c2y * c2z + c2z * c2x).Scale(0.5);
                            ag = ag.AddConst(0.15);
                            agx = c2x * cySz + (s2y * (cz * cx) - s2z * (sx * sy)).Scale(0.5) + s2x * (c2y + c2z);
                            agy = c2y * czSx + (s2z * (cx * cy) - s2x * (sy * sz)).Scale(0.5) + s2y * (c2x + c2z);
                            agz = c2z * cxSy + (s2x * (cy * cz) - s2y * (sz * sx)).Scale(0.5) + s2z * (c2x + c2y);
                            break;
                        }
                        default: throw new ArgumentOutOfRangeException(nameof(Type));
                    }
                    g = intersect(g, ag.ToInterval());
                    gx = intersect(gx, agx.ToInterval());
                    gy = intersect(gy, agy.ToInterval());
                    gz = intersect(gz, agz.ToInterval());
                }
            }
            if (Negate) { g = -g; gx = -gx; gy = -gy; gz = -gz; }
            return new IntervalEnclosure { F = g - c(Level), Gx = k * gx, Gy = k * gy, Gz = k * gz, Smooth = true };
        }
    }
}
