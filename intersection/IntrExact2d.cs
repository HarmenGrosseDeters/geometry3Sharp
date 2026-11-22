using System;

namespace g3
{
    /// <summary>
    /// Exact decisions for the 2D line, segment and triangle intersection classes (IntrLine2Line2, IntrLine2Segment2,
    /// IntrSegment2Segment2, IntrLine2Triangle2, IntrSegment2Triangle2). Every yes/no, every Type and Quantity is an exact
    /// sign (ExactPredicates.Orient2d / Orient3d, each with a floating-point filter) or a comparison of coordinates, so
    /// nothing depends on a tolerance and nothing changes under scaling by 2^k. Contact points that are input points
    /// (endpoints, corners) are returned exactly; a crossing point is computed in floating point from the exactly decided
    /// crossing, on the edge or segment it crosses (a few ulp across it, and along it as well as the angle of the crossing
    /// allows: about 1e-16 / sin(angle) relative).
    /// <para>
    /// A line (Origin, Direction) is the line through Origin along Direction, exactly as given (a side test is the exact sign
    /// of cross(Direction, v - Origin)); a segment is the closed segment between Segment2d.P0 and P1 (the rounded
    /// Center -/+ Extent Direction); a triangle is the closed triangle of its corners. The signs use differences of the input
    /// doubles that are never rounded (the Orient3d of lifted points).
    /// </para>
    /// </summary>
    internal static class IntrExact2d
    {
        internal static bool IsFinite(Vector2d v)
        {
            return (v.x - v.x) == 0 && (v.y - v.y) == 0;
        }

        /// <summary> the sign of cross(q - p, s - r), exactly (Orient3d of the points lifted to z = 0 and z = 1) </summary>
        internal static int CrossSign(Vector2d p, Vector2d q, Vector2d r, Vector2d s)
        {
            return -ExactPredicates.Orient3d(new Vector3d(p.x, p.y, 0), new Vector3d(q.x, q.y, 0), new Vector3d(r.x, r.y, 1), new Vector3d(s.x, s.y, 1));
        }

        /// <summary> -1, 0, +1 as a &lt; b &lt;=&gt; (ordering of doubles) </summary>
        static int Cmp(double a, double b) { return a < b ? -1 : (a > b ? 1 : 0); }

        // ------------------------------------------------------------------------------------------------------------
        // a line: through an origin along a direction, or through two points (both exact)
        // ------------------------------------------------------------------------------------------------------------

        /// <summary> a line a + t d (the origin and the direction vector as given) or the line through the points a and b </summary>
        internal struct LineRef
        {
            public Vector2d A, B, D;
            public bool ByPoints;

            public static LineRef FromDirection(Vector2d origin, Vector2d direction)
            {
                return new LineRef { A = origin, D = direction, ByPoints = false };
            }
            public static LineRef FromPoints(Vector2d a, Vector2d b)
            {
                // (the direction as halves of the two points: finite for finite points; its sign pattern is exact, its size is not used exactly)
                return new LineRef { A = a, B = b, D = new Vector2d(0.5 * b.x - 0.5 * a.x, 0.5 * b.y - 0.5 * a.y), ByPoints = true };
            }

            /// <summary> the sign of cross(direction, v - origin): + if v is on the left of the line, exactly </summary>
            public int Side(Vector2d v)
            {
                return ByPoints ? ExactPredicates.Orient2d(A, B, v) : CrossSign(Vector2d.Zero, D, A, v);
            }

            /// <summary> the sign of cross(q - p, direction) (the slope of the edge p-q against the line), exactly </summary>
            public int Slope(Vector2d p, Vector2d q)
            {
                return ByPoints ? CrossSign(p, q, A, B) : CrossSign(p, q, Vector2d.Zero, D);
            }
        }

        /// <summary>
        /// Two lines (o1, d1) and (o2, d2) as given: Point (the lines cross), Line (the same line) or Empty (parallel, apart): the
        /// exact signs of cross(d1, d2) and cross(o2 - o1, d1)
        /// </summary>
        internal static IntersectionType LineLine(Vector2d o1, Vector2d d1, Vector2d o2, Vector2d d2)
        {
            if (CrossSign(Vector2d.Zero, d1, Vector2d.Zero, d2) != 0)
                return IntersectionType.Point;
            return CrossSign(o1, o2, Vector2d.Zero, d1) == 0 ? IntersectionType.Line : IntersectionType.Empty;
        }

        /// <summary>
        /// 2^-e for the largest coordinate magnitude of the points (a power of two, so that the scaling is exact), 1 for all zeros:
        /// the points scaled by it have a largest coordinate in [1, 2) (in [0.5, 1) for the top binade, 2^-1023 itself; subnormal coordinates are scaled up by 2^1023), so differences and products of scaled values cannot overflow
        /// </summary>
        internal static double ScaleFor(Vector2d a, Vector2d b, Vector2d c)
        {
            double m = Math.Max(Math.Max(Math.Abs(a.x), Math.Abs(a.y)), Math.Max(Math.Max(Math.Abs(b.x), Math.Abs(b.y)), Math.Max(Math.Abs(c.x), Math.Abs(c.y))));
            if ((m > 0 && m - m == 0) == false)
                return 1;
            int e = (int)((BitConverter.DoubleToInt64Bits(m) >> 52) & 0x7FF);
            if (e == 2046)
                return BitConverter.Int64BitsToDouble(1L << 51);          // 2^-1023 (the largest coordinates, in [2^1023, 2^1024): scaled into [0.5, 1))
            if (e == 0)
                return BitConverter.Int64BitsToDouble(0x7FE0000000000000L);   // 2^1023 (subnormal coordinates: scaling up is exact, the products do not underflow)
            return (e > 0 && e < 2046) ? BitConverter.Int64BitsToDouble((long)(2046 - e) << 52) : 1;
        }

        /// <summary>
        /// where the line (origin o, direction dir) crosses the segment p-q, as a fraction in [0, 1] of p to q, in floating point from the
        /// rounded distances of p and q to the line (see CrossingFraction); the points are scaled by a power of two first, so that no
        /// difference overflows
        /// </summary>
        internal static double FractionOnSegment(Vector2d o, Vector2d dir, Vector2d p, Vector2d q)
        {
            double sc = ScaleFor(o, p, q);
            Vector2d os = o * sc;
            // (the direction too, to a largest coordinate in [1, 2): the products cannot overflow; the fraction does not change)
            dir *= ScaleFor(dir, Vector2d.Zero, Vector2d.Zero);
            double dp = dir.DotPerp(p * sc - os), dq = dir.DotPerp(q * sc - os);
            return CrossingFraction(dp, dq);
        }

        /// <summary> (p - o) . dir, with the points scaled by a power of two first (the difference cannot overflow), for the parameter of a point along a line </summary>
        internal static double ParameterAlong(Vector2d o, Vector2d dir, Vector2d p)
        {
            Vector2d diff = p - o;
            if (diff.x - diff.x == 0 && diff.y - diff.y == 0)
                return diff.Dot(dir);       // (no scaling where the difference is finite: the scaled products could underflow)
            double sc = ScaleFor(o, p, Vector2d.Zero);
            return (p * sc - o * sc).Dot(dir) / sc;
        }

        /// <summary> a.x b.y - a.y b.x, to about one rounding of the result (the two products as exact sums of two doubles) </summary>
        internal static double AccurateCross(Vector2d a, Vector2d b)
        {
            double h1, l1, h2, l2;
            Expansion.TwoProduct(a.x, b.y, out h1, out l1);
            Expansion.TwoProduct(a.y, b.x, out h2, out l2);
            return (h1 - h2) + (l1 - l2);
        }

        /// <summary>
        /// p + w (q - p), p and q themselves for w = 0 and 1: computed on p and q scaled by a power of two (ScaleFor), so that the difference cannot
        /// overflow and the result does not depend on the scale (2^k invariant), then scaled back; the crossing of an axis-aligned edge stays on it
        /// (equal coordinates give the coordinate back)
        /// </summary>
        internal static Vector2d Lerp(Vector2d p, Vector2d q, double w)
        {
            if (w <= 0) return p;
            if (w >= 1) return q;
            double sc = ScaleFor(p, q, Vector2d.Zero);
            Vector2d ps = p * sc, qs = q * sc;
            return new Vector2d((ps.x + w * (qs.x - ps.x)) / sc, (ps.y + w * (qs.y - ps.y)) / sc);
        }

        /// <summary>
        /// num / (den sc), rounded once where the result is representable, for the scale sc of ScaleFor (a power of two): the power of two is applied
        /// where it is exact (scaling up is exact, a quotient that underflows or overflows is not rescued), the order chosen by sc
        /// </summary>
        internal static double QuotientScaled(double num, double den, double sc)
        {
            double r;
            if (sc <= 1) {
                double x = num / sc;                 // (exact unless it overflows)
                r = x / den;
                if (r - r == 0) return r;
                return num / den / sc;
            }
            double d2 = den * sc;                    // (exact unless it overflows)
            if (d2 - d2 == 0) {
                r = num / d2;
                if (r - r == 0) return r;
            }
            double y = num / sc;
            r = y / den;
            if (r - r == 0) return r;
            return num / den / sc;
        }

        // ------------------------------------------------------------------------------------------------------------
        // line / segment against triangle: the interval of the line in the closed triangle
        // ------------------------------------------------------------------------------------------------------------

        /// <summary> an end of the interval: a corner of the triangle, or the crossing of the line with an edge (k to k + 1) </summary>
        internal struct Cand
        {
            public bool Crossing;
            public int Index;
            public static Cand Vertex(int i) { return new Cand { Crossing = false, Index = i }; }
            public static Cand Edge(int k) { return new Cand { Crossing = true, Index = k }; }
        }

        /// <summary>
        /// The interval of the line in the closed triangle t, as its first and last candidate in the direction of the line
        /// (equal for a single point). Returns 1 for an interval, 0 if the line misses the triangle, and -1 if the triangle
        /// is degenerate (collinear corners: the caller takes another route).
        /// </summary>
        internal static int Interval(ref LineRef line, ref Triangle2d t, out Cand entry, out Cand exit)
        {
            entry = exit = default(Cand);
            int o = ExactPredicates.Orient2d(t.V0, t.V1, t.V2);
            if (o == 0)
                return -1;
            int s0 = line.Side(t.V0), s1 = line.Side(t.V1), s2 = line.Side(t.V2);
            if ((s0 > 0 && s1 > 0 && s2 > 0) || (s0 < 0 && s1 < 0 && s2 < 0))
                return 0;
            int[] s = { s0, s1, s2 };
            int zeros = (s0 == 0 ? 1 : 0) + (s1 == 0 ? 1 : 0) + (s2 == 0 ? 1 : 0);
            if (zeros == 0) {
                // two edges have corners on both sides: one is entered, the other left
                int crossed = 0;
                for (int k = 0; k < 3; ++k) {
                    if (s[k] * s[(k + 1) % 3] > 0)
                        continue;
                    if (Entering(ref line, ref t, k, o)) entry = Cand.Edge(k); else exit = Cand.Edge(k);
                    ++crossed;
                }
                return crossed == 2 ? 1 : 0;
            }
            if (zeros == 1) {
                int z = s0 == 0 ? 0 : (s1 == 0 ? 1 : 2);
                int i = (z + 1) % 3, j = (z + 2) % 3;
                if (s[i] * s[j] > 0) {
                    entry = exit = Cand.Vertex(z);          // grazes the corner
                    return 1;
                }
                // the edge i to j is crossed, the corner z is the other end
                if (Entering(ref line, ref t, i, o)) { entry = Cand.Edge(i); exit = Cand.Vertex(z); }
                else { entry = Cand.Vertex(z); exit = Cand.Edge(i); }
                return 1;
            }
            // two corners on the line (three is a degenerate triangle): the edge between them, ordered along the line
            int u, w;
            if (s0 != 0) { u = 1; w = 2; } else if (s1 != 0) { u = 0; w = 2; } else { u = 0; w = 1; }
            bool uFirst = Along(ref line, t[u], t[w]) <= 0;
            entry = Cand.Vertex(uFirst ? u : w);
            exit = Cand.Vertex(uFirst ? w : u);
            return 1;
        }

        // the line enters the triangle (interior on the left of CCW edges) at the crossing with the edge k to k + 1
        static bool Entering(ref LineRef line, ref Triangle2d t, int k, int orientation)
        {
            return line.Slope(t[k], t[(k + 1) % 3]) * orientation > 0;
        }

        /// <summary> the order of two points p, q on the line along its direction: -1 if p is before q </summary>
        static int Along(ref LineRef line, Vector2d p, Vector2d q)
        {
            double dx = line.D.x, dy = line.D.y;
            if (Math.Abs(dx) >= Math.Abs(dy))
                return Cmp(p.x, q.x) * (dx > 0 ? 1 : -1);
            return Cmp(p.y, q.y) * (dy > 0 ? 1 : -1);
        }

        /// <summary> p is strictly before q along a to b (p, q on the line a-b) </summary>
        internal static bool Before(Vector2d a, Vector2d b, Vector2d p, Vector2d q)
        {
            LineRef line = LineRef.FromPoints(a, b);
            return Along(ref line, p, q) < 0;
        }

        /// <summary>
        /// The order of the candidate c along the line through the two points a to b against the end e of the segment (e = a if atEnd
        /// is false, else b): -1 if c is before e, 0 if at e, +1 if after e
        /// </summary>
        internal static int Compare(Cand c, ref Triangle2d t, ref LineRef line, bool atEnd)
        {
            Vector2d e = atEnd ? line.B : line.A;
            if (c.Crossing == false)
                return Along(ref line, t[c.Index], e);
            // the crossing x = a + u (b - a) with the edge k to k + 1: with ra, rb the orientations of a, b against the
            // edge (the edge line value at a and b, linear in u): u = -ra / (rb - ra), u - 1 = -rb / (rb - ra)
            Vector2d p = t[c.Index], q = t[(c.Index + 1) % 3];
            int slope = line.Slope(p, q);      // the sign of rb - ra, not 0 for a crossed edge
            int r = ExactPredicates.Orient2d(p, q, e);
            return -r * slope;
        }

        /// <summary>
        /// where the linear function with the values dp at the start and dq at the end of an edge is 0, as a fraction in
        /// [0, 1], for values of exactly opposite signs that were rounded (they may have lost their signs, underflowed to 0,
        /// or overflowed): dp / (dp - dq) if the rounded values still have opposite signs and are finite, else the end that is
        /// nearer to the line
        /// </summary>
        internal static double CrossingFraction(double dp, double dq)
        {
            // (an overflowed value gives inf / inf = NaN, which fails the range test, or a quotient of 0, the nearer end)
            if ((dp < 0 && dq > 0) || (dp > 0 && dq < 0)) {
                double w = dp / (dp - dq);
                if (w >= 0 && w <= 1)
                    return w;
            }
            return Math.Abs(dp) <= Math.Abs(dq) ? 0 : 1;
        }

        /// <summary> the point of the line in the triangle (candidate c): the corner, or the crossing computed in floating point </summary>
        internal static Vector2d PointOf(Cand c, ref Triangle2d t, ref LineRef line)
        {
            if (c.Crossing == false)
                return t[c.Index];
            Vector2d p = t[c.Index], q = t[(c.Index + 1) % 3];
            return Lerp(p, q, FractionOnSegment(line.A, line.D, p, q));
        }
    }
}
