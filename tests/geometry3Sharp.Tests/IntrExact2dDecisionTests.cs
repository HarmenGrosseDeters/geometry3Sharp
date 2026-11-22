using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact decisions of IntrLine2Line2, IntrLine2Segment2, IntrSegment2Segment2, IntrLine2Triangle2 and
    /// IntrSegment2Triangle2 against an independent rational oracle (BigInteger fractions of the exact doubles): the
    /// yes/no, the Type and the Quantity agree on engineered contacts (touching ends, T junctions, collinear overlaps,
    /// parallel and nearly parallel input, 1-ulp and 1e-9 offsets), under every similarity of the plane, with no absolute
    /// epsilon (the old 1e-8 constants decided about 4 % of such pairs wrongly and every pair of a triangle of size
    /// 1e-9). Input points are returned exactly; the result is the same bit for bit at 2^k scales; non-finite input is an
    /// InvalidQuery; the tolerance route stays available with an IntervalThreshold above 0.
    /// </summary>
    public class IntrExact2dDecisionTests
    {
        // ---- the oracle: dyadic rationals ----
        readonly struct Q : IComparable<Q>
        {
            public readonly BigInteger N, D;
            public Q(BigInteger n, BigInteger d) { if (d.Sign < 0) { n = -n; d = -d; } N = n; D = d; }
            public static Q From(double x)
            {
                if (x == 0) return new Q(0, 1);
                long bits = BitConverter.DoubleToInt64Bits(x);
                bool neg = bits < 0; int e = (int)((bits >> 52) & 0x7FF); long m = bits & 0xFFFFFFFFFFFFFL;
                if (e == 0) e = 1; else m |= 1L << 52;
                e -= 1075;
                BigInteger n = m; if (neg) n = -n;
                return e >= 0 ? new Q(n << e, 1) : new Q(n, BigInteger.One << -e);
            }
            public static Q operator +(Q a, Q b) => new Q(a.N * b.D + b.N * a.D, a.D * b.D);
            public static Q operator -(Q a, Q b) => new Q(a.N * b.D - b.N * a.D, a.D * b.D);
            public static Q operator *(Q a, Q b) => new Q(a.N * b.N, a.D * b.D);
            public static Q operator /(Q a, Q b) => new Q(a.N * b.D, a.D * b.N);
            public int Sign => N.Sign;
            public int CompareTo(Q o) => (N * o.D).CompareTo(o.N * D);
            public static bool operator <(Q a, Q b) => a.CompareTo(b) < 0;
            public static bool operator >(Q a, Q b) => a.CompareTo(b) > 0;
            public static bool operator <=(Q a, Q b) => a.CompareTo(b) <= 0;
            public static bool operator >=(Q a, Q b) => a.CompareTo(b) >= 0;
            public static Q Min(Q a, Q b) => a <= b ? a : b;
            public static Q Max(Q a, Q b) => a >= b ? a : b;
            public static readonly Q Zero = new Q(0, 1), One = new Q(1, 1);
            // (BigInteger.GetBitLength is missing on net48)
            static int BitLength(BigInteger x)
            {
                byte[] b = BigInteger.Abs(x).ToByteArray();
                int top = b.Length - 1;
                while (top > 0 && b[top] == 0) top--;
                int bits = top * 8;
                for (int v = b[top]; v > 0; v >>= 1) bits++;
                return bits;
            }
            // the value as a double (to about 20 digits of the quotient, enough to measure an error)
            public double Approx()
            {
                if (N.IsZero) return 0;
                BigInteger a = BigInteger.Abs(N);
                int shift = 70 + BitLength(D) - BitLength(a);
                BigInteger q = shift >= 0 ? (a << shift) / D : a / (D << -shift);
                double r = Math.Pow(2, -shift) * (double)q;
                return N.Sign < 0 ? -r : r;
            }
            // the double if the value is exactly one, else NaN
            public double ExactDouble()
            {
                if (N.IsZero) return 0;
                double approx = Approx();
                return (approx - approx == 0 && From(approx).CompareTo(this) == 0) ? approx : double.NaN;
            }
        }

        readonly struct P2
        {
            public readonly Q X, Y;
            public P2(Q x, Q y) { X = x; Y = y; }
            public P2(Vector2d v) { X = Q.From(v.x); Y = Q.From(v.y); }
            public static P2 operator -(P2 a, P2 b) => new P2(a.X - b.X, a.Y - b.Y);
            public static P2 operator +(P2 a, P2 b) => new P2(a.X + b.X, a.Y + b.Y);
            public static P2 operator *(Q s, P2 a) => new P2(s * a.X, s * a.Y);
            public static Q Cross(P2 a, P2 b) => a.X * b.Y - a.Y * b.X;
            public static Q Dot(P2 a, P2 b) => a.X * b.X + a.Y * b.Y;
        }

        /// <summary> the expected contact: kind 0 none, 1 a point, 2 a segment (or a line), and the exact end points </summary>
        struct Expect { public int Kind; public P2 P0, P1; }

        static Expect SegSeg(Vector2d A, Vector2d B, Vector2d C, Vector2d D)
        {
            P2 a = new P2(A), b = new P2(B), c = new P2(C), d = new P2(D);
            P2 ab = b - a, cd = d - c;
            Q den = P2.Cross(ab, cd);
            Q o1 = P2.Cross(ab, c - a), o2 = P2.Cross(ab, d - a), o3 = P2.Cross(cd, a - c), o4 = P2.Cross(cd, b - c);
            var r = new Expect();
            if (den.Sign != 0) {
                if (o1.Sign * o2.Sign <= 0 && o3.Sign * o4.Sign <= 0) { r.Kind = 1; r.P0 = a + (P2.Cross(c - a, cd) / den) * ab; }
                return r;
            }
            if (o1.Sign != 0) return r;
            Q l2 = P2.Dot(ab, ab);
            Q tc = P2.Dot(c - a, ab) / l2, td = P2.Dot(d - a, ab) / l2;
            Q lo = Q.Max(Q.Zero, Q.Min(tc, td)), hi = Q.Min(Q.One, Q.Max(tc, td));
            if (lo > hi) return r;
            r.P0 = a + lo * ab; r.P1 = a + hi * ab; r.Kind = lo.CompareTo(hi) == 0 ? 1 : 2;
            return r;
        }

        static Expect LineLine(Vector2d O1, Vector2d S1, Vector2d O2, Vector2d S2)
        {
            P2 o1 = new P2(O1), d1 = new P2(S1) - o1, o2 = new P2(O2), d2 = new P2(S2) - o2;
            Q den = P2.Cross(d1, d2);
            var r = new Expect();
            if (den.Sign != 0) { r.Kind = 1; r.P0 = o1 + (P2.Cross(o2 - o1, d2) / den) * d1; return r; }
            if (P2.Cross(o2 - o1, d1).Sign == 0) r.Kind = 2;
            return r;
        }

        static Expect LineSeg(Vector2d O, Vector2d S, Vector2d A, Vector2d B)
        {
            P2 o = new P2(O), d = new P2(S) - o, a = new P2(A), b = new P2(B);
            Q sa = P2.Cross(d, a - o), sb = P2.Cross(d, b - o);
            var r = new Expect();
            if (sa.Sign == 0 && sb.Sign == 0) { r.Kind = 2; return r; }
            if (sa.Sign * sb.Sign > 0) return r;
            r.Kind = 1; r.P0 = a + (sa / (sa - sb)) * (b - a);
            return r;
        }

        // the interval of triangle with the line a + u (b - a): parameters lo, hi; false if empty
        static bool TriInterval(P2 a, P2 b, P2[] v, out Q lo, out Q hi)
        {
            P2 ab = b - a; Q l2 = P2.Dot(ab, ab);
            var c = v.Select(p => P2.Cross(ab, p - a)).ToArray();
            Q mn = Q.Zero, mx = Q.Zero; bool any = false;
            lo = hi = Q.Zero;
            if (c.All(x => x.Sign > 0) || c.All(x => x.Sign < 0)) return false;
            void add(Q u) { if (!any) { mn = u; mx = u; any = true; } else { mn = Q.Min(mn, u); mx = Q.Max(mx, u); } }
            for (int i = 0; i < 3; i++) {
                int j = (i + 1) % 3;
                if (c[i].Sign == 0) add(P2.Dot(v[i] - a, ab) / l2);
                if (c[i].Sign * c[j].Sign < 0) add(P2.Dot(v[i] + (c[i] / (c[i] - c[j])) * (v[j] - v[i]) - a, ab) / l2);
            }
            lo = mn; hi = mx;
            return any;
        }

        static bool Degenerate(Vector2d[] t) => P2.Cross(new P2(t[1]) - new P2(t[0]), new P2(t[2]) - new P2(t[0])).Sign == 0;

        static Expect SegTri(Vector2d A, Vector2d B, Vector2d[] t)
        {
            P2 a = new P2(A), b = new P2(B), ab = b - a;
            var r = new Expect();
            if (!TriInterval(a, b, t.Select(p => new P2(p)).ToArray(), out Q lo, out Q hi)) return r;
            lo = Q.Max(lo, Q.Zero); hi = Q.Min(hi, Q.One);
            if (lo > hi) return r;
            r.P0 = a + lo * ab; r.P1 = a + hi * ab; r.Kind = lo.CompareTo(hi) == 0 ? 1 : 2;
            return r;
        }

        static Expect LineTri(Vector2d O, Vector2d S, Vector2d[] t)
        {
            P2 a = new P2(O), b = new P2(S), ab = b - a;
            var r = new Expect();
            if (!TriInterval(a, b, t.Select(p => new P2(p)).ToArray(), out Q lo, out Q hi)) return r;
            r.P0 = a + lo * ab; r.P1 = a + hi * ab; r.Kind = lo.CompareTo(hi) == 0 ? 1 : 2;
            return r;
        }

        // ---- the generator ----
        static double Perturb(Random r, double v)
        {
            if (v == 0) return (r.Next(2) == 0 ? 1 : -1) * Math.Pow(2, -r.Next(30, 53));      // (not subnormal: a 2^k scale would lose it)
            switch (r.Next(1, 6)) {
                case 1: return BitIncrement(v);
                case 2: return BitDecrement(v);
                case 3: return v + (r.NextDouble() - 0.5) * 1e-9;
                case 4: return v + (r.NextDouble() - 0.5) * 1e-12;
                default: return v + (r.NextDouble() - 0.5) * 1e-6;
            }
        }
        // the next double up / down, for finite x (Math.BitIncrement / BitDecrement are missing on net48)
        static double BitIncrement(double x)
        {
            if (x == 0) return double.Epsilon;
            long b = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(x > 0 ? b + 1 : b - 1);
        }
        static double BitDecrement(double x) => -BitIncrement(-x);

        /// <summary> n points on the lattice [-3, 3]^2, with structure (a point at a dyadic position on the first two, or equal to one of them) and perturbations </summary>
        static Vector2d[] Points(Random r, int n)
        {
            var p = new Vector2d[n];
            for (int i = 0; i < n; i++) p[i] = new Vector2d(r.Next(-3, 4), r.Next(-3, 4));
            if (n >= 4) {
                if (r.Next(2) == 0) p[2] = p[0] + (r.Next(0, 5) / 4.0) * (p[1] - p[0]);
                if (r.Next(3) == 0) p[3] = p[1] + (r.Next(-2, 3) / 2.0) * (p[1] - p[0]);
                if (r.Next(4) == 0) p[3] = p[0];
                if (r.Next(5) == 0) p[3] = p[2];
            }
            for (int i = 0; i < n; i++)
                if (r.Next(3) == 0) {
                    p[i].x = Perturb(r, p[i].x);
                    if (r.Next(2) == 0) p[i].y = Perturb(r, p[i].y);
                }
            return p;
        }

        static Vector2d Map(Vector2d v, Similarity s) => s.Apply2d(v);

        static Vector2d Unit(Vector2d a, Vector2d b) { Vector2d d = b - a; d.Normalize(); return d; }

        // the points of the line (origin, second point) as the classes define it
        static Vector2d Second(Line2d l)
        {
            Vector3d p = ExactRayTriangle.SecondPoint(new Vector3d(l.Origin.x, l.Origin.y, 0), new Vector3d(l.Direction.x, l.Direction.y, 0));
            return new Vector2d(p.x, p.y);
        }

        static string Fmt(IEnumerable<Vector2d> pts) => string.Join(" ", pts.Select(v => v.x.ToString("R") + "," + v.y.ToString("R")));

        public static IEnumerable<object[]> Names() => Similarity.Names2D();

        // a result Point0 (and Point1) must be the exact point where that is a double, else close to it
        static void CheckPoints(Expect e, int quantity, Vector2d p0, Vector2d p1, string what, double tol = 1e-9)
        {
            if (e.Kind == 1) {
                CheckPoint(e.P0, p0, what, tol);
            } else if (e.Kind == 2 && quantity == 2) {
                // (the two ends in either order)
                bool direct = Near(e.P0, p0, tol) && Near(e.P1, p1, tol);
                bool swapped = Near(e.P0, p1, tol) && Near(e.P1, p0, tol);
                Assert.True(direct || swapped, "segment ends " + what);
            }
        }
        // (a crossing computed in floating point is as good as the angle of the crossing allows: relative error 1e-14 / sin)
        static bool Near(P2 e, Vector2d p, double tol = 1e-9) => Math.Abs((Q.From(p.x) - e.X).Approx()) <= tol && Math.Abs((Q.From(p.y) - e.Y).Approx()) <= tol;
        static double Tol(Vector2d[] pts, double sin) => Math.Max(1e-9, 1e-14 * pts.Max(v => Math.Max(Math.Abs(v.x), Math.Abs(v.y))) / Math.Max(sin, 1e-300));
        // the points of the input of the current case (a contact at one of them is returned exactly)
        [ThreadStatic] static Vector2d[] currentInput;
        static void CheckPoint(P2 e, Vector2d p, string what, double tol = 1e-9)
        {
            double ex = e.X.ExactDouble(), ey = e.Y.ExactDouble();
            if (double.IsNaN(ex) == false && double.IsNaN(ey) == false && currentInput != null && currentInput.Any(v => v.x == ex && v.y == ey))
                Assert.True(ex == p.x && ey == p.y, "an input point is returned exactly " + what);
            else
                Assert.True(Near(e, p, tol), "crossing point " + what);
        }


        // contact points of a result against a line a-b and a set of input points (the corners of a triangle): an exact end
        // that is an input point is returned exactly; any point is on the line and in the box of the input to within rounding
        // (a crossing with an edge nearly parallel to the line is poorly determined along the edge, not across it)
        static void CheckOnLine(Expect e, int quantity, Vector2d p0, Vector2d p1, Vector2d a, Vector2d b, Vector2d[] box, string what)
        {
            var got = quantity == 2 ? new[] { p0, p1 } : new[] { p0 };
            foreach (P2 end in e.Kind == 1 ? new[] { e.P0 } : new[] { e.P0, e.P1 }) {
                double ex = end.X.ExactDouble(), ey = end.Y.ExactDouble();
                if (double.IsNaN(ex) == false && double.IsNaN(ey) == false && currentInput.Any(v => v.x == ex && v.y == ey))
                    Assert.True(got.Any(g => g.x == ex && g.y == ey), "an end that is an input point is returned exactly " + what);
            }
            P2 pa = new P2(a), ab = new P2(b) - pa;
            double len = Math.Sqrt(P2.Dot(ab, ab).Approx());
            double size = box.Max(v => Math.Max(Math.Abs(v.x), Math.Abs(v.y))) + 1e-300;
            foreach (Vector2d g in got) {
                double dist = Math.Abs(P2.Cross(ab, new P2(g) - pa).Approx()) / len;
                Assert.True(dist <= 1e-9 * Math.Max(size, 1e-3 * len), "on the line " + what);
                Assert.True(g.x >= box.Min(v => v.x) - 1e-9 * size && g.x <= box.Max(v => v.x) + 1e-9 * size
                    && g.y >= box.Min(v => v.y) - 1e-9 * size && g.y <= box.Max(v => v.y) + 1e-9 * size, "in the box " + what);
            }
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void SegmentSegment_AgreesWithTheOracle(string name)
        {
            Similarity s = Similarity.ByName(name);
            var r = new Random(1);
            int hits = 0, touches = 0, overlaps = 0, n = 0;
            for (int it = 0; it < 700; it++) {
                var p = Points(r, 4).Select(v => Map(v, s)).ToArray();
                var s1 = new Segment2d(p[0], p[1]); var s2 = new Segment2d(p[2], p[3]);
                if (s1.Direction.IsNormalized == false || s2.Direction.IsNormalized == false || s1.P0 == s1.P1 || s2.P0 == s2.P1) continue;
                currentInput = new[] { s1.P0, s1.P1, s2.P0, s2.P1 };
                Expect e = SegSeg(s1.P0, s1.P1, s2.P0, s2.P1);
                var f = new IntrSegment2Segment2(s1, s2);
                bool hit = f.Find();
                string what = name + ": " + Fmt(p);
                Assert.True(hit == (e.Kind != 0), "hit " + what);
                if (e.Kind != 0) {
                    Assert.Equal(e.Kind == 1 ? IntersectionType.Point : IntersectionType.Segment, f.Type);
                    Assert.Equal(e.Kind, f.Quantity);
                    CheckPoints(e, f.Quantity, f.Point0, f.Point1, what, Tol(p, Math.Abs(s1.Direction.DotPerp(s2.Direction))));
                    hits++; if (e.Kind == 2) overlaps++;
                    if (e.Kind == 1 && e.P0.X.ExactDouble() == e.P0.X.ExactDouble()) touches++;
                } else {
                    Assert.Equal(IntersectionType.Empty, f.Type);
                    Assert.Equal(0, f.Quantity);
                }
                n++;
            }
            Assert.True(hits > 150 && hits < n - 100 && overlaps > 5 && touches > 20, $"hits {hits}/{n}, overlaps {overlaps}, touches {touches}");
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void LineSegment_AgreesWithTheOracle(string name)
        {
            Similarity s = Similarity.ByName(name);
            var r = new Random(2);
            int hits = 0, n = 0;
            for (int it = 0; it < 700; it++) {
                var p = Points(r, 4).Select(v => Map(v, s)).ToArray();
                Vector2d d = Unit(p[0], p[1]);
                if (d.IsNormalized == false) continue;
                var line = new Line2d(p[0], d); var seg = new Segment2d(p[2], p[3]);
                if (seg.Direction.IsNormalized == false || seg.P0 == seg.P1) continue;
                currentInput = new[] { seg.P0, seg.P1 };
                Expect e = LineSeg(line.Origin, Second(line), seg.P0, seg.P1);
                var f = new IntrLine2Segment2(line, seg);
                string what = name + ": " + Fmt(p);
                Assert.True(f.Find() == (e.Kind != 0), "hit " + what);
                if (e.Kind == 1) { Assert.Equal(IntersectionType.Point, f.Type); Assert.Equal(1, f.Quantity); CheckPoint(e.P0, f.Point, what, Tol(p, Math.Abs(line.Direction.DotPerp(seg.Direction)))); hits++; }
                else if (e.Kind == 2) { Assert.Equal(IntersectionType.Segment, f.Type); Assert.Equal(int.MaxValue, f.Quantity); hits++; }
                else Assert.Equal(IntersectionType.Empty, f.Type);
                n++;
            }
            Assert.True(hits > 150 && hits < n - 100, $"hits {hits}/{n}");
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void LineLine_AgreesWithTheOracle(string name)
        {
            Similarity s = Similarity.ByName(name);
            var r = new Random(3);
            int points = 0, lines = 0, empty = 0;
            for (int it = 0; it < 700; it++) {
                var p = Points(r, 4).Select(v => Map(v, s)).ToArray();
                Vector2d d1 = Unit(p[0], p[1]), d2 = Unit(p[2], p[3]);
                if (d1.IsNormalized == false || d2.IsNormalized == false) continue;
                var l1 = new Line2d(p[0], d1); var l2 = new Line2d(p[2], d2);
                Expect e = LineLine(l1.Origin, Second(l1), l2.Origin, Second(l2));
                var f = new IntrLine2Line2(l1, l2);
                string what = name + ": " + Fmt(p);
                Assert.True(f.Find() == (e.Kind != 0), "hit " + what);
                if (e.Kind == 1) { Assert.Equal(IntersectionType.Point, f.Type); points++; }
                else if (e.Kind == 2) { Assert.Equal(IntersectionType.Line, f.Type); Assert.Equal(int.MaxValue, f.Quantity); lines++; }
                else { Assert.Equal(IntersectionType.Empty, f.Type); empty++; }
            }
            Assert.True(points > 100 && lines + empty >= 1, $"points {points}, lines {lines}, empty {empty}");
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void SegmentTriangle_AgreesWithTheOracle(string name)
        {
            Similarity s = Similarity.ByName(name);
            var r = new Random(4);
            int points = 0, segments = 0, n = 0;
            for (int it = 0; it < 700; it++) {
                var p = Points(r, 5).Select(v => Map(v, s)).ToArray();
                var seg = new Segment2d(p[0], p[1]); var t = new[] { p[2], p[3], p[4] };
                if (seg.Direction.IsNormalized == false || seg.P0 == seg.P1 || Degenerate(t)) continue;
                currentInput = new[] { seg.P0, seg.P1, t[0], t[1], t[2] };
                Expect e = SegTri(seg.P0, seg.P1, t);
                var f = new IntrSegment2Triangle2(seg, new Triangle2d(t[0], t[1], t[2]));
                string what = name + ": " + Fmt(p);
                Assert.True(f.Find() == (e.Kind != 0), "hit " + what);
                if (e.Kind != 0) {
                    Assert.Equal(e.Kind == 1 ? IntersectionType.Point : IntersectionType.Segment, f.Type);
                    Assert.Equal(e.Kind, f.Quantity);
                    CheckOnLine(e, f.Quantity, f.Point0, f.Point1, seg.P0, seg.P1, new[] { seg.P0, seg.P1, t[0], t[1], t[2] }, what);
                    if (e.Kind == 1) points++; else segments++;
                } else Assert.Equal(IntersectionType.Empty, f.Type);
                n++;
            }
            Assert.True(points > 10 && segments > 100 && n - points - segments > 100, $"points {points}, segments {segments} of {n}");
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void LineTriangle_AgreesWithTheOracle(string name)
        {
            Similarity s = Similarity.ByName(name);
            var r = new Random(5);
            int points = 0, segments = 0, n = 0;
            for (int it = 0; it < 700; it++) {
                var p = Points(r, 5).Select(v => Map(v, s)).ToArray();
                Vector2d d = Unit(p[0], p[1]);
                var t = new[] { p[2], p[3], p[4] };
                if (d.IsNormalized == false || Degenerate(t)) continue;
                var line = new Line2d(p[0], d);
                currentInput = new[] { t[0], t[1], t[2] };
                Expect e = LineTri(line.Origin, Second(line), t);
                var f = new IntrLine2Triangle2(line, new Triangle2d(t[0], t[1], t[2]));
                string what = name + ": " + Fmt(p);
                Assert.True(f.Find() == (e.Kind != 0), "hit " + what);
                if (e.Kind != 0) {
                    Assert.Equal(e.Kind == 1 ? IntersectionType.Point : IntersectionType.Segment, f.Type);
                    Assert.Equal(e.Kind, f.Quantity);
                    CheckOnLine(e, f.Quantity, f.Point0, f.Point1, line.Origin, Second(line), new[] { t[0], t[1], t[2] }, what);
                    if (e.Kind == 1) points++; else segments++;
                } else Assert.Equal(IntersectionType.Empty, f.Type);
                n++;
            }
            Assert.True(points > 10 && segments > 100 && n - points - segments > 50, $"points {points}, segments {segments} of {n}");
        }

        // ---- scale: nothing absolute ----
        [Fact]
        public void SmallTriangle_IsNotEverywhere()
        {
            // old: dist within 1e-8 of the line counted as on it, so every line near a triangle of size 1e-9 hit it
            double s = 1e-9;
            var tri = new Triangle2d(new Vector2d(0, 0), new Vector2d(s, 0), new Vector2d(0, s));
            var far = new Line2d(new Vector2d(0, 1e-3), new Vector2d(1, 0));       // 1e-3 above, parallel to an edge
            Assert.False(new IntrLine2Triangle2(far, tri).Find());
            Assert.False(new IntrSegment2Triangle2(new Segment2d(new Vector2d(-1, 5e-9), new Vector2d(1, 5e-9)), tri).Find());
            var through = new Line2d(new Vector2d(0, 0.25 * s), new Vector2d(1, 0));
            var f = new IntrLine2Triangle2(through, tri);
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Segment, f.Type);
        }

        [Fact]
        public void NearlyParallelSegments_AreNotParallel()
        {
            // two segments crossing at a very small angle (8e-10 rad): old DotThreshold 1e-8 called them parallel
            var a = new Segment2d(new Vector2d(0, 0), new Vector2d(1, 0));
            var b = new Segment2d(new Vector2d(0, -4e-10), new Vector2d(1, 4e-10));
            var f = new IntrSegment2Segment2(a, b);
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Point, f.Type);
            Assert.InRange(f.Point0.x, 0.499, 0.501);
            var apart = new Segment2d(new Vector2d(0, 5e-9), new Vector2d(1, 5e-9));        // parallel, 5e-9 apart: not collinear
            Assert.False(new IntrSegment2Segment2(a, apart).Find());
            var l = new IntrLine2Line2(new Line2d(Vector2d.Zero, Vector2d.AxisX), new Line2d(new Vector2d(0, 5e-9), Vector2d.AxisX));
            Assert.False(l.Find());
            Assert.Equal(IntersectionType.Empty, l.Type);
        }

        [Fact]
        public void SharedEnd_Touches_AndReturnsTheEndExactly()
        {
            var a = new Segment2d(new Vector2d(0, 0), new Vector2d(1, 1));
            var b = new Segment2d(new Vector2d(1, 1), new Vector2d(3, 0));
            var f = new IntrSegment2Segment2(a, b);
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Point, f.Type);
            Assert.Equal(new Vector2d(1, 1), f.Point0);
            // collinear, touching at one end
            var c = new IntrSegment2Segment2(new Segment2d(new Vector2d(0, 0), new Vector2d(1, 0)), new Segment2d(new Vector2d(1, 0), new Vector2d(2, 0)));
            Assert.True(c.Find());
            Assert.Equal(IntersectionType.Point, c.Type);
            Assert.Equal(1, c.Quantity);
            Assert.Equal(new Vector2d(1, 0), c.Point0);
            // collinear, overlapping and disjoint
            var o = new IntrSegment2Segment2(new Segment2d(new Vector2d(0, 0), new Vector2d(2, 0)), new Segment2d(new Vector2d(1, 0), new Vector2d(3, 0)));
            Assert.True(o.Find());
            Assert.Equal(IntersectionType.Segment, o.Type);
            Assert.Equal(2, o.Quantity);
            Assert.Equal(1.0, Math.Min(o.Point0.x, o.Point1.x));
            Assert.Equal(2.0, Math.Max(o.Point0.x, o.Point1.x));
            Assert.False(new IntrSegment2Segment2(new Segment2d(new Vector2d(0, 0), new Vector2d(1, 0)), new Segment2d(new Vector2d(1.000000001, 0), new Vector2d(2, 0))).Find());
        }

        [Fact]
        public void SegmentEnd_OnATriangleEdge_IsReturnedExactly()
        {
            // vertical segments (their ends are exact: Direction (0, 1)) from a point on the edge y = x / 2 up into the triangle:
            // the crossing of the line with the edge is not exact in floating point (7 w), the end must be returned as it is
            var tri = new Triangle2d(new Vector2d(0, 0), new Vector2d(7, 3.5), new Vector2d(0, 6));
            for (int k = 1; k <= 40; k++) {
                double x = k / 8.0;
                Vector2d onEdge = new Vector2d(x, x / 2), top = new Vector2d(x, 3);
                foreach (var (p, q) in new[] { (onEdge, top), (top, onEdge) }) {
                    var seg = new Segment2d(p, q);
                    Assert.True(seg.P0 == p && seg.P1 == q, "exact ends");
                    var f = new IntrSegment2Triangle2(seg, tri);
                    Assert.True(f.Find());
                    Assert.Equal(IntersectionType.Segment, f.Type);
                    Assert.Equal(2, f.Quantity);
                    Assert.True((f.Point0 == p && f.Point1 == q) || (f.Point0 == q && f.Point1 == p), "ends at x = " + x + ": " + f.Point0 + " | " + f.Point1);
                }
            }
            // the segment ends at the edge from below, touching it there: one point, exactly the end
            for (int k = 1; k <= 40; k++) {
                double x = k / 8.0;
                var touch = new Segment2d(new Vector2d(x, x / 2 - 3), new Vector2d(x, x / 2));
                var g = new IntrSegment2Triangle2(touch, tri);
                Assert.True(g.Find());
                Assert.Equal(1, g.Quantity);
                Assert.Equal(new Vector2d(x, x / 2), g.Point0);
            }
        }

        [Theory]
        [InlineData(-530)]
        [InlineData(-300)]
        [InlineData(300)]
        [InlineData(520)]
        public void Crossing_AtExtremeScales_IsTheCrossing(int exponent)
        {
            // the rounded distances to the line underflow (2^-530) or overflow (2^520): the crossing must still be found
            double s = Math.Pow(2, exponent);
            var line = new Line2d(new Vector2d(-s, 0), Vector2d.AxisX);
            var seg = new Segment2d(Vector2d.Zero, Vector2d.AxisY, s);        // (not from two points: Normalize zeroes a vector below 2.2e-16 and overflows above 1e154)
            var ls = new IntrLine2Segment2(line, seg);
            Assert.True(ls.Find(), "line-segment find " + ls.Result);
            Assert.Equal(IntersectionType.Point, ls.Type);
            Assert.True(Math.Abs(ls.Point.x) <= 1e-15 * s && Math.Abs(ls.Point.y) <= 1e-15 * s, "line-segment " + ls.Point);
            var ss = new IntrSegment2Segment2(new Segment2d(Vector2d.Zero, Vector2d.AxisX, s), seg);
            Assert.True(ss.Find(), "segment-segment find " + ss.Result);
            Assert.Equal(IntersectionType.Point, ss.Type);
            Assert.True(Math.Abs(ss.Point0.x) <= 1e-15 * s && Math.Abs(ss.Point0.y) <= 1e-15 * s, "segment-segment " + ss.Point0);
            var tri = new Triangle2d(new Vector2d(0, -s), new Vector2d(s, s), new Vector2d(-s, s));
            var lt = new IntrLine2Triangle2(new Line2d(new Vector2d(-s, 0), Vector2d.AxisX), tri);
            Assert.True(lt.Find(), "line-triangle find " + lt.Result);
            Assert.Equal(2, lt.Quantity);
            Assert.True(Math.Abs(lt.Point0.y) <= 1e-15 * s && Math.Abs(lt.Point1.y) <= 1e-15 * s);
        }

        [Fact]
        public void LineWhoseSecondPointOverflows_IsInvalid()
        {
            var line = new Line2d(new Vector2d(1.7e308, 0), Vector2d.AxisX);
            var seg = new Segment2d(new Vector2d(1, -1), new Vector2d(1, 1));
            var tri = new Triangle2d(new Vector2d(0, -1), new Vector2d(2, -1), new Vector2d(1, 2));
            Assert.False(new IntrLine2Line2(line, line).Find());
            Assert.False(new IntrLine2Segment2(line, seg).Find());
            Assert.False(new IntrLine2Triangle2(line, tri).Find());
            Assert.Equal(IntersectionResult.InvalidQuery, new IntrLine2Segment2(line, seg).Compute().Result);
        }

        // ---- tolerance route ----
        [Fact]
        public void IntervalThreshold_SelectsTheToleranceRoute()
        {
            var a = new Segment2d(new Vector2d(0, 0), new Vector2d(1, 0));
            var b = new Segment2d(new Vector2d(1 + 5e-9, 0.5), new Vector2d(1 + 5e-9, -0.5));       // crosses the line of a 5e-9 beyond its end
            Assert.False(new IntrSegment2Segment2(a, b).Find());
            Assert.True(new IntrSegment2Segment2(a, b) { IntervalThreshold = 1e-8 }.Find());
            var l = new IntrLine2Segment2(new Line2d(Vector2d.Zero, Vector2d.AxisX), new Segment2d(new Vector2d(0.5, 5e-9), new Vector2d(0.5, 1)));
            Assert.False(l.Find());
            Assert.True(new IntrLine2Segment2(new Line2d(Vector2d.Zero, Vector2d.AxisX), new Segment2d(new Vector2d(0.5, 5e-9), new Vector2d(0.5, 1))) { IntervalThreshold = 1e-8 }.Find());
        }

        // ---- non-finite input ----
        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void NonFiniteInput_NeverHits(double bad)
        {
            var good = new Segment2d(new Vector2d(0, 0), new Vector2d(2, 0));
            var cross = new Segment2d(new Vector2d(1, -1), new Vector2d(1, 1));
            var badSeg = new Segment2d(new Vector2d(1, -1), new Vector2d(1, bad));
            var tri = new Triangle2d(new Vector2d(0, -1), new Vector2d(2, -1), new Vector2d(1, 2));
            var badTri = new Triangle2d(new Vector2d(0, -1), new Vector2d(bad, -1), new Vector2d(1, 2));
            var line = new Line2d(Vector2d.Zero, Vector2d.AxisX);
            var badLine = new Line2d(new Vector2d(bad, 0), Vector2d.AxisX);
            Assert.False(new IntrSegment2Segment2(good, badSeg).Find());
            Assert.False(new IntrLine2Segment2(line, badSeg).Find());
            Assert.False(new IntrLine2Segment2(badLine, cross).Find());
            Assert.False(new IntrLine2Line2(badLine, line).Find());
            Assert.False(new IntrLine2Line2(line, badLine).Find());
            Assert.False(new IntrLine2Triangle2(line, badTri).Find());
            Assert.False(new IntrLine2Triangle2(badLine, tri).Find());
            Assert.False(new IntrSegment2Triangle2(good, badTri).Find());
            Assert.False(new IntrSegment2Triangle2(badSeg, tri).Find());
            Assert.Equal(IntersectionResult.InvalidQuery, new IntrLine2Line2(badLine, line).Compute().Result);
            Assert.Equal(IntersectionResult.InvalidQuery, new IntrSegment2Triangle2(good, badTri).Compute().Result);
        }

        // ---- 2^k invariance, bit for bit ----
        static IEnumerable<(Vector2d[] pts, int kind)> Cases(int seed)
        {
            var r = new Random(seed);
            for (int i = 0; i < 400; i++) yield return (Points(r, 5), i);
        }

        public static IEnumerable<object[]> Pow2Names()
        {
            foreach (Similarity s in Similarity.All2D())
                if (s.IsExact && s != Similarity.Identity)
                    yield return new object[] { s.Name };
        }

        [Theory]
        [MemberData(nameof(Pow2Names))]
        public void Results_AreTheSameBitForBit_AtPowerOfTwoScales(string name)
        {
            Similarity s = Similarity.ByName(name);
            double k = s.Scale;
            int compared = 0;
            foreach (var (p0, _) in Cases(11)) {
                var p1 = p0.Select(v => v * k).ToArray();
                var a1 = new Segment2d(p0[0], p0[1]); var b1 = new Segment2d(p0[2], p0[3]);
                var a2 = new Segment2d(p1[0], p1[1]); var b2 = new Segment2d(p1[2], p1[3]);
                if (a1.Direction.IsNormalized == false || b1.Direction.IsNormalized == false || a1.P0 == a1.P1 || b1.P0 == b1.P1
                    || a2.Direction.IsNormalized == false || b2.Direction.IsNormalized == false) continue;      // (Normalize zeroes a vector below 2.2e-16: not at both scales)
                if (Degenerate(new[] { p0[2], p0[3], p0[4] })) continue;        // (a degenerate triangle takes the tolerance route)
                var tri0 = new Triangle2d(p0[2], p0[3], p0[4]); var tri1 = new Triangle2d(p1[2], p1[3], p1[4]);
                var l0 = new Line2d(p0[0], Unit(p0[0], p0[1])); var l1 = new Line2d(p1[0], Unit(p1[0], p1[1]));
                var ss0 = new IntrSegment2Segment2(a1, b1); var ss1 = new IntrSegment2Segment2(a2, b2);
                Assert.True(ss0.Find() == ss1.Find(), "SegSeg " + Fmt(p0));
                Assert.Equal(ss0.Type, ss1.Type); Assert.Equal(ss0.Quantity, ss1.Quantity);
                Same(ss0.Point0, ss1.Point0, k, ss0.Quantity >= 1); if (ss0.Quantity == 2) Same(ss0.Point1, ss1.Point1, k, true);
                var st0 = new IntrSegment2Triangle2(a1, tri0); var st1 = new IntrSegment2Triangle2(a2, tri1);
                Assert.True(st0.Find() == st1.Find(), "SegTri " + Fmt(p0));
                Assert.Equal(st0.Type, st1.Type); Assert.Equal(st0.Quantity, st1.Quantity);
                Same(st0.Point0, st1.Point0, k, st0.Quantity >= 1); if (st0.Quantity == 2) Same(st0.Point1, st1.Point1, k, true);
                if (l0.Direction.IsNormalized && l1.Direction.IsNormalized) {      // (Normalize zeroes a vector below 2.2e-16: not at both scales)
                    var lt0 = new IntrLine2Triangle2(l0, tri0); var lt1 = new IntrLine2Triangle2(l1, tri1);
                    Assert.True(lt0.Find() == lt1.Find(), "LineTri " + Fmt(p0));
                    Assert.Equal(lt0.Type, lt1.Type); Assert.Equal(lt0.Quantity, lt1.Quantity);
                    Same(lt0.Point0, lt1.Point0, k, lt0.Quantity >= 1);
                    var ls0 = new IntrLine2Segment2(l0, b1); var ls1 = new IntrLine2Segment2(l1, b2);
                    Assert.True(ls0.Find() == ls1.Find(), "LineSeg " + Fmt(p0));
                    Assert.Equal(ls0.Type, ls1.Type); Assert.Equal(ls0.Quantity, ls1.Quantity);
                    if (ls0.Quantity == 1) Same(ls0.Point, ls1.Point, k, true);
                    var m0 = new Line2d(p0[2], Unit(p0[2], p0[3])); var m1 = new Line2d(p1[2], Unit(p1[2], p1[3]));
                    if (m0.Direction.IsNormalized && m1.Direction.IsNormalized) {
                        var ll0 = new IntrLine2Line2(l0, m0); var ll1 = new IntrLine2Line2(l1, m1);
                        Assert.True(ll0.Find() == ll1.Find(), "LineLine " + Fmt(p0));
                        Assert.Equal(ll0.Type, ll1.Type);
                        if (ll0.Type == IntersectionType.Point) Same(ll0.Point, ll1.Point, k, true);
                    }
                }
                compared++;
            }
            Assert.True(compared > 300, "compared " + compared);
        }

        static void Same(Vector2d a, Vector2d b, double k, bool check)
        {
            if (check == false) return;
            if (double.IsNaN(a.x) && double.IsNaN(b.x)) return;
            Assert.True(a.x * k == b.x && a.y * k == b.y, "point " + a.x.ToString("R") + "," + a.y.ToString("R") + " scaled by " + k + " is " + b.x.ToString("R") + "," + b.y.ToString("R"));
        }

        // ---- reuse ----
        [Fact]
        public void Find_IsCached_AndResetByTheSetters()
        {
            var a = new Segment2d(new Vector2d(0, 0), new Vector2d(2, 0));
            var b = new Segment2d(new Vector2d(1, -1), new Vector2d(1, 1));
            var far = new Segment2d(new Vector2d(5, -1), new Vector2d(5, 1));
            var f = new IntrSegment2Segment2(a, b);
            Assert.True(f.Compute().Result == IntersectionResult.Intersects);
            Assert.True(f.Find());
            Assert.Equal(new Vector2d(1, 0), f.Point0);
            f.Segment2 = far;
            Assert.Equal(IntersectionResult.NotComputed, f.Result);
            Assert.False(f.Find());
            Assert.Equal(0, f.Quantity);
            f.Segment2 = new Segment2d(new Vector2d(1, 0), new Vector2d(3, 0));       // collinear overlap after a miss
            Assert.True(f.Find());
            Assert.Equal(2, f.Quantity);
            f.Segment2 = b;                                                            // a point after a segment
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Point, f.Type);
            Assert.Equal(1, f.Quantity);

            var tri = new Triangle2d(new Vector2d(0, 0), new Vector2d(4, 0), new Vector2d(0, 4));
            var g = new IntrSegment2Triangle2(new Segment2d(new Vector2d(-1, 1), new Vector2d(5, 1)), tri);
            Assert.True(g.Find());
            Assert.Equal(2, g.Quantity);
            g.Segment = new Segment2d(new Vector2d(-1, 5), new Vector2d(5, 5));
            Assert.False(g.Find());
            g.Segment = new Segment2d(new Vector2d(-2, -2), new Vector2d(0, 0));          // touches the corner (0, 0)
            Assert.True(g.Find());
            Assert.Equal(IntersectionType.Point, g.Type);
            Assert.Equal(new Vector2d(0, 0), g.Point0);
            g.Triangle = new Triangle2d(new Vector2d(10, 10), new Vector2d(11, 10), new Vector2d(10, 11));
            Assert.False(g.Find());

            var lt = new IntrLine2Triangle2(new Line2d(new Vector2d(0, 1), Vector2d.AxisX), tri);
            Assert.True(lt.Find()); Assert.Equal(2, lt.Quantity);
            lt.Line = new Line2d(new Vector2d(-1, -1), Vector2d.AxisX);
            Assert.False(lt.Find());
            lt.Line = new Line2d(new Vector2d(-1, 4), Vector2d.AxisX);       // along the top corner
            Assert.True(lt.Find());
            Assert.Equal(IntersectionType.Point, lt.Type);
            Assert.Equal(new Vector2d(0, 4), lt.Point0);
        }
    }

}
