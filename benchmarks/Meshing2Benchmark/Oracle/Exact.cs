using System;
using System.Globalization;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Meshing2Acceptance
{
    /// <summary>Exact rational number: reduced, denominator positive. All oracle decisions are made on these or on <see cref="Pt"/>.</summary>
    public readonly struct Q : IComparable<Q>, IEquatable<Q>
    {
        readonly BigInteger n, d; // default(Q) has d = 0 and reads as 0/1
        public BigInteger N => d.IsZero ? BigInteger.Zero : n;
        public BigInteger D => d.IsZero ? BigInteger.One : d;

        public Q(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero) throw new DivideByZeroException();
            if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
            if (denominator.IsOne) { n = numerator; d = denominator; return; }
            if (denominator.IsPowerOfTwo) {
                // (every value built from doubles by + - * has a power of two as its denominator: the common factor is a power of two too,
                // 2^min(trailing zeros of the numerator, the exponent of the denominator), taken by shifts instead of a gcd; exact, so the
                // reduced form is the same)
                if (numerator.IsZero) { n = BigInteger.Zero; d = BigInteger.One; return; }
                int shift = Math.Min(TrailingZeros(numerator), BitLength(denominator) - 1);
                n = numerator >> shift; d = denominator >> shift;
                return;
            }
            var g = BigInteger.GreatestCommonDivisor(numerator, denominator);
            if (!g.IsOne && !g.IsZero) { numerator /= g; denominator /= g; }
            n = numerator; d = denominator;
        }

        public static readonly Q Zero = new Q(0, 1);
        public static readonly Q One = new Q(1, 1);
        public static implicit operator Q(int v) => new Q(v, 1);

        /// <summary>The exact value of a finite double, from its bits. Throws on NaN and infinities.</summary>
        /// <summary> an interval of doubles around n / d (d > 0): the conversions and the division round by at most a few units in
        /// the last place, the interval is wider by far; false if the quotient overflows a double. A quotient whose parts overflow a
        /// double (a tube squared of 1000 bits and more) can still be a double: it is taken from the leading bits of both and the
        /// exponent difference (#947; before, the filters were off there and every test ran in BigInteger) </summary>
        public static bool Bounds(BigInteger n, BigInteger d, out double lo, out double hi)
        {
            double dn = (double)n, dd = (double)d;
            lo = hi = 0;
            if (d.Sign <= 0) return false;
            double v = double.IsInfinity(dn) || double.IsInfinity(dd) ? Quotient(n, d) : dn / dd, pad;
            if (double.IsInfinity(v) || double.IsNaN(v)) return false;
            pad = Math.Abs(v) * 1e-13 + 1e-300;
            lo = v - pad; hi = v + pad;
            return true;
        }

        /// <summary> n / d (d > 0) as a double to a few units in the last place (the leading 62 bits of both, scaled by the difference of
        /// the shifts, in steps that cannot overflow); infinity when the quotient is too large for a double, 0 when it is too small </summary>
        static double Quotient(BigInteger n, BigInteger d)
        {
            int sn = Math.Max(0, BitLength(n) - 62), sd = Math.Max(0, BitLength(d) - 62);
            double r = (double)(n >> sn) / (double)(d >> sd);   // (an arithmetic shift of a negative n rounds toward minus infinity: within the error)
            int k = sn - sd;
            while (k > 0 && !double.IsInfinity(r) && r != 0) { int step = Math.Min(k, 1000); r *= Math.Pow(2, step); k -= step; }
            while (k < 0 && r != 0) { int step = Math.Min(-k, 1000); r *= Math.Pow(2, -step); k += step; }
            return r;
        }

        /// <summary> the number of trailing zero bits of a nonzero x (of |x|: the bytes of the two's complement hold the same count) </summary>
        static int TrailingZeros(BigInteger x)
        {
            byte[] bytes = x.ToByteArray();
            int i = 0;
            while (bytes[i] == 0) ++i;
            int b = bytes[i], z = 0;
            while ((b & 1) == 0) { ++z; b >>= 1; }
            return i * 8 + z;
        }

        /// <summary> the number of bits of |x| </summary>
        static int BitLength(BigInteger x)
        {
            byte[] bytes = BigInteger.Abs(x).ToByteArray();
            int last = bytes.Length - 1;
            while (last > 0 && bytes[last] == 0) --last;
            int top = bytes[last], bits = 0;
            while (top != 0) { ++bits; top >>= 1; }
            return last * 8 + bits;
        }

        public bool Bounds(out double lo, out double hi) => Bounds(N, D, out lo, out hi);

        public static Q FromDouble(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("not finite: " + v);
            long bits = BitConverter.DoubleToInt64Bits(v);
            bool negative = bits < 0;
            int exponent = (int)((bits >> 52) & 0x7ff);
            long mantissa = bits & 0xfffffffffffffL;
            if (exponent == 0) exponent = 1; else mantissa |= 1L << 52;
            exponent -= 1075;
            BigInteger num = mantissa;
            if (negative) num = -num;
            return exponent >= 0 ? new Q(num << exponent, 1) : new Q(num, BigInteger.One << -exponent);
        }

        public static Q operator +(Q a, Q b) => new Q(a.N * b.D + b.N * a.D, a.D * b.D);
        public static Q operator -(Q a, Q b) => new Q(a.N * b.D - b.N * a.D, a.D * b.D);
        public static Q operator -(Q a) => new Q(-a.N, a.D);
        public static Q operator *(Q a, Q b) => new Q(a.N * b.N, a.D * b.D);
        public static Q operator /(Q a, Q b) => new Q(a.N * b.D, a.D * b.N);
        public int CompareTo(Q o) => (N * o.D).CompareTo(o.N * D);
        public static bool operator <(Q a, Q b) => a.CompareTo(b) < 0;
        public static bool operator >(Q a, Q b) => a.CompareTo(b) > 0;
        public static bool operator <=(Q a, Q b) => a.CompareTo(b) <= 0;
        public static bool operator >=(Q a, Q b) => a.CompareTo(b) >= 0;
        public static bool operator ==(Q a, Q b) => a.N == b.N && a.D == b.D;
        public static bool operator !=(Q a, Q b) => !(a == b);
        public bool Equals(Q o) => this == o;
        public override bool Equals(object obj) => obj is Q q && this == q;
        public override int GetHashCode() => N.GetHashCode() ^ (D.GetHashCode() * 31);
        public int Sign => N.Sign;
        public static Q Abs(Q a) => a.N.Sign < 0 ? -a : a;
        public static Q Min(Q a, Q b) => a <= b ? a : b;
        public static Q Max(Q a, Q b) => a >= b ? a : b;

        /// <summary>Exact form "n" or "n/d": the canonical text of an area.</summary>
        public override string ToString() => D.IsOne ? N.ToString(CultureInfo.InvariantCulture) : N.ToString(CultureInfo.InvariantCulture) + "/" + D.ToString(CultureInfo.InvariantCulture);

        /// <summary>Decimal text with the given digits after the point, truncated toward zero. No floating point involved.</summary>
        public string ToDecimal(int digits)
        {
            var sb = new StringBuilder();
            BigInteger whole = BigInteger.DivRem(BigInteger.Abs(N), D, out BigInteger rest);
            if (N.Sign < 0) sb.Append('-');
            sb.Append(whole.ToString(CultureInfo.InvariantCulture)).Append('.');
            for (int i = 0; i < digits; ++i) {
                rest *= 10;
                var digit = BigInteger.DivRem(rest, D, out rest);
                sb.Append(digit.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }

    /// <summary>Exact point in homogeneous form (X/W, Y/W), W positive. Output vertices may be rational, inputs are dyadic.</summary>
    public readonly struct Pt : IEquatable<Pt>
    {
        public readonly BigInteger X, Y, W;
        public Pt(BigInteger x, BigInteger y, BigInteger w)
        {
            if (w.Sign <= 0) throw new ArgumentException("W must be positive");
            X = x; Y = y; W = w;
        }
        public Pt(Q x, Q y)
        {
            W = x.D * y.D / BigInteger.GreatestCommonDivisor(x.D, y.D);
            X = x.N * (W / x.D);
            Y = y.N * (W / y.D);
        }
        public static Pt FromDoubles(double x, double y) => new Pt(Q.FromDouble(x), Q.FromDouble(y));

        /// <summary>
        /// An interval of doubles that contains the exact x (or y): for floating-point filters in front of the exact tests. False
        /// when the parts do not fit in doubles (then the caller must not filter).
        /// </summary>
        public bool Bounds(bool y, out double lo, out double hi) => Q.Bounds(y ? Y : X, W, out lo, out hi);
        public Q Qx => new Q(X, W);
        public Q Qy => new Q(Y, W);
        public bool Equals(Pt o) => X * o.W == o.X * W && Y * o.W == o.Y * W;
        public override bool Equals(object obj) => obj is Pt p && Equals(p);
        public override int GetHashCode() { var x = Qx; var y = Qy; return x.GetHashCode() ^ (y.GetHashCode() * 397); }
        public int CompareXY(Pt o)
        {
            int c = (X * o.W).CompareTo(o.X * W);
            return c != 0 ? c : (Y * o.W).CompareTo(o.Y * W);
        }
        public override string ToString() => "(" + Qx + ", " + Qy + ")";
    }

    /// <summary>
    /// The oracle's own predicates: BigInteger determinants by definition, no filters, written from the textbook
    /// formulas. They share nothing with math/ExactPredicates.cs, on purpose.
    /// </summary>
    public static class Exact
    {
        /// <summary>+1 if a, b, c are counter-clockwise, -1 clockwise, 0 collinear.</summary>
        public static int Orient(Pt a, Pt b, Pt c) => Det3(a, b, c).Sign;

        // det | ax ay aw ; bx by bw ; cx cy cw |, whose sign is the orientation because every w is positive
        static BigInteger Det3(Pt a, Pt b, Pt c) =>
            a.X * (b.Y * c.W - c.Y * b.W) - a.Y * (b.X * c.W - c.X * b.W) + a.W * (b.X * c.Y - c.X * b.Y);

        /// <summary>Twice the signed area of a, b, c, as an exact rational.</summary>
        public static Q Area2(Pt a, Pt b, Pt c) => new Q(Det3(a, b, c), a.W * b.W * c.W);

        /// <summary>+1 if d is strictly inside the circle through the counter-clockwise a, b, c; -1 outside; 0 on it.</summary>
        public static int InCircle(Pt a, Pt b, Pt c, Pt d)
        {
            // each row (x w, y w, x^2 + y^2, w^2) is the row (x/w, y/w, (x^2+y^2)/w^2, 1) times w^2 > 0
            return Det4(Row(a), Row(b), Row(c), Row(d)).Sign;
        }

        static BigInteger[] Row(Pt p) => new[] { p.X * p.W, p.Y * p.W, p.X * p.X + p.Y * p.Y, p.W * p.W };

        static BigInteger Det4(BigInteger[] a, BigInteger[] b, BigInteger[] c, BigInteger[] d)
        {
            // Laplace expansion by the 2x2 minors of the first two rows and of the last two rows
            BigInteger s01 = a[0] * b[1] - a[1] * b[0], s02 = a[0] * b[2] - a[2] * b[0], s03 = a[0] * b[3] - a[3] * b[0];
            BigInteger s12 = a[1] * b[2] - a[2] * b[1], s13 = a[1] * b[3] - a[3] * b[1], s23 = a[2] * b[3] - a[3] * b[2];
            BigInteger c23 = c[2] * d[3] - c[3] * d[2], c13 = c[1] * d[3] - c[3] * d[1], c12 = c[1] * d[2] - c[2] * d[1];
            BigInteger c03 = c[0] * d[3] - c[3] * d[0], c02 = c[0] * d[2] - c[2] * d[0], c01 = c[0] * d[1] - c[1] * d[0];
            return s01 * c23 - s02 * c13 + s03 * c12 + s12 * c03 - s13 * c02 + s23 * c01;
        }

        /// <summary>The point is on the closed segment a-b (a and b may coincide).</summary>
        public static bool OnClosedSegment(Pt a, Pt b, Pt p)
        {
            if (Orient(a, b, p) != 0) return false;
            Q px = p.Qx, py = p.Qy, ax = a.Qx, ay = a.Qy, bx = b.Qx, by = b.Qy;
            return Q.Min(ax, bx) <= px && px <= Q.Max(ax, bx) && Q.Min(ay, by) <= py && py <= Q.Max(ay, by);
        }

        /// <summary>The open segments a-b and c-d cross at one point interior to both.</summary>
        public static bool ProperCross(Pt a, Pt b, Pt c, Pt d) =>
            Orient(a, b, c) * Orient(a, b, d) < 0 && Orient(c, d, a) * Orient(c, d, b) < 0;

        /// <summary>
        /// The sign of Dist2ToSegment(a, b, p) - limit (limit of either sign), without the rationals: with D = a.W * b.W * p.W every
        /// coordinate of the vectors u = b - a and v = p - a is an integer over D, and each case of Dist2ToSegment is a quotient of
        /// integers with a positive denominator (v.v over D^2 beyond a, w.w over D^2 beyond b, cross^2 over u.u * D^2 between), compared
        /// with the limit by cross-multiplication. No gcd; the same sign as the rational computation, exactly (the equivalence is argued in
        /// docs/design/MESHING2D_FAST_ORACLE.md, section 7).
        /// </summary>
        public static int Dist2ToSegmentCompare(Pt a, Pt b, Pt p, Q limit)
        {
            BigInteger d = a.W * b.W * p.W;
            BigInteger ux = (b.X * a.W - a.X * b.W) * p.W, uy = (b.Y * a.W - a.Y * b.W) * p.W;
            BigInteger vx = (p.X * a.W - a.X * p.W) * b.W, vy = (p.Y * a.W - a.Y * p.W) * b.W;
            BigInteger uu = ux * ux + uy * uy, dd = d * d, ln = limit.N, ld = limit.D;
            BigInteger num, den;
            if (uu.IsZero) { num = vx * vx + vy * vy; den = dd; }
            else {
                BigInteger uv = ux * vx + uy * vy;
                if (uv.Sign <= 0) { num = vx * vx + vy * vy; den = dd; }
                else if (uv >= uu) { BigInteger wx = vx - ux, wy = vy - uy; num = wx * wx + wy * wy; den = dd; }
                else { BigInteger cross = ux * vy - uy * vx; num = cross * cross; den = uu * dd; }
            }
            return (num * ld).CompareTo(ln * den);
        }

        /// <summary> Dist2ToSegment(a, b, p) &lt;= limit </summary>
        public static bool Dist2ToSegmentAtMost(Pt a, Pt b, Pt p, Q limit) => Dist2ToSegmentCompare(a, b, p, limit) <= 0;


        /// <summary>Squared distance from p to the closed segment a-b.</summary>
        public static Q Dist2ToSegment(Pt a, Pt b, Pt p)
        {
            Q ax = a.Qx, ay = a.Qy, ux = b.Qx - ax, uy = b.Qy - ay, vx = p.Qx - ax, vy = p.Qy - ay;
            Q uu = ux * ux + uy * uy;
            if (uu.Sign == 0) return vx * vx + vy * vy;
            Q t = (ux * vx + uy * vy) / uu;
            if (t.Sign <= 0) return vx * vx + vy * vy;
            if (t >= Q.One) { Q wx = vx - ux, wy = vy - uy; return wx * wx + wy * wy; }
            Q cross = ux * vy - uy * vx;
            return cross * cross / uu;
        }

        /// <summary>Convex hull of the points by monotone chain, counter-clockwise, strictly convex corners only.</summary>
        public static List<Pt> ConvexHull(IEnumerable<Pt> points)
        {
            var sorted = new List<Pt>(points);
            sorted.Sort((p, q) => p.CompareXY(q));
            var unique = new List<Pt>();
            foreach (var p in sorted) if (unique.Count == 0 || !unique[unique.Count - 1].Equals(p)) unique.Add(p);
            if (unique.Count < 3) return unique;
            var hull = new List<Pt>();
            for (int pass = 0; pass < 2; ++pass) {
                int start = hull.Count;
                for (int i = 0; i < unique.Count; ++i) {
                    var p = unique[pass == 0 ? i : unique.Count - 1 - i];
                    while (hull.Count >= start + 2 && Orient(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        /// <summary>
        /// sin^2 of the smallest angle of a triangle (exact). Over the three corners the minimum of sin^2 is at the smallest
        /// angle: every other angle b satisfies a &lt;= b &lt; 180 - a, so sin b &gt;= sin a.
        /// </summary>
        public static Q MinAngleSin2(Pt a, Pt b, Pt c)
        {
            Q ab = Len2(a, b), bc = Len2(b, c), ca = Len2(c, a);
            Q area2 = Area2(a, b, c);
            Q cross2 = area2 * area2; // the same cross product at every corner
            return Q.Min(cross2 / (ab * ca), Q.Min(cross2 / (ab * bc), cross2 / (bc * ca)));
        }

        public static Q Len2(Pt a, Pt b)
        {
            Q dx = a.Qx - b.Qx, dy = a.Qy - b.Qy;
            return dx * dx + dy * dy;
        }
    }
}
