using System;
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
            var g = BigInteger.GreatestCommonDivisor(numerator, denominator);
            if (!g.IsOne && !g.IsZero) { numerator /= g; denominator /= g; }
            n = numerator; d = denominator;
        }

        public static readonly Q Zero = new Q(0, 1);
        public static readonly Q One = new Q(1, 1);
        public static implicit operator Q(int v) => new Q(v, 1);

        /// <summary>The exact value of a finite double, from its bits. Throws on NaN and infinities.</summary>
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
        public override string ToString() => D.IsOne ? N.ToString() : N + "/" + D;

        /// <summary>Decimal text with the given digits after the point, truncated toward zero. No floating point involved.</summary>
        public string ToDecimal(int digits)
        {
            var sb = new StringBuilder();
            BigInteger whole = BigInteger.DivRem(BigInteger.Abs(N), D, out BigInteger rest);
            if (N.Sign < 0) sb.Append('-');
            sb.Append(whole.ToString()).Append('.');
            for (int i = 0; i < digits; ++i) {
                rest *= 10;
                var digit = BigInteger.DivRem(rest, D, out rest);
                sb.Append(digit.ToString());
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
