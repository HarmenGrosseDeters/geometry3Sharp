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
    /// A line (Origin, Direction) is the line through Origin and its second point (ExactRayTriangle.SecondPoint of the
    /// 3D classes: Origin + 2^m Direction, rounded), a segment the closed segment between Segment2d.P0 and P1 (the
    /// rounded Center -/+ Extent Direction), a triangle the closed triangle of its corners.
    /// </para>
    /// </summary>
    internal static class IntrExact2d
    {
        /// <summary> the second point of the line through origin along direction (origin if not finite or direction is 0) </summary>
        internal static Vector2d SecondPoint(Vector2d origin, Vector2d direction)
        {
            Vector3d p = ExactRayTriangle.SecondPoint(new Vector3d(origin.x, origin.y, 0), new Vector3d(direction.x, direction.y, 0));
            return new Vector2d(p.x, p.y);
        }

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
        // line / segment against line
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Two lines (o1, p1) and (o2, p2): Point (+1: crossing), Line (0, the same line) or Empty (-1: parallel apart)
        /// </summary>
        internal static IntersectionType LineLine(Vector2d o1, Vector2d p1, Vector2d o2, Vector2d p2)
        {
            if (CrossSign(o1, p1, o2, p2) != 0)
                return IntersectionType.Point;
            return ExactPredicates.Orient2d(o1, p1, o2) == 0 ? IntersectionType.Line : IntersectionType.Empty;
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
        /// The interval of the line a-b in the closed triangle t, as its first and last candidate in the direction a to b
        /// (equal for a single point). Returns 1 for an interval, 0 if the line misses the triangle, and -1 if the triangle
        /// is degenerate (collinear corners: the caller takes another route).
        /// </summary>
        internal static int Interval(Vector2d a, Vector2d b, ref Triangle2d t, out Cand entry, out Cand exit)
        {
            entry = exit = default(Cand);
            int o = ExactPredicates.Orient2d(t.V0, t.V1, t.V2);
            if (o == 0)
                return -1;
            int s0 = ExactPredicates.Orient2d(a, b, t.V0), s1 = ExactPredicates.Orient2d(a, b, t.V1), s2 = ExactPredicates.Orient2d(a, b, t.V2);
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
                    if (Entering(ref t, k, a, b, o)) entry = Cand.Edge(k); else exit = Cand.Edge(k);
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
                if (Entering(ref t, i, a, b, o)) { entry = Cand.Edge(i); exit = Cand.Vertex(z); }
                else { entry = Cand.Vertex(z); exit = Cand.Edge(i); }
                return 1;
            }
            // two corners on the line (three is a degenerate triangle): the edge between them, ordered along a to b
            int u, w;
            if (s0 != 0) { u = 1; w = 2; } else if (s1 != 0) { u = 0; w = 2; } else { u = 0; w = 1; }
            bool uFirst = Along(a, b, t[u], t[w]) <= 0;
            entry = Cand.Vertex(uFirst ? u : w);
            exit = Cand.Vertex(uFirst ? w : u);
            return 1;
        }

        // the line a-b enters the triangle (interior on the left of CCW edges) at the crossing with the edge k to k + 1
        static bool Entering(ref Triangle2d t, int k, Vector2d a, Vector2d b, int orientation)
        {
            return CrossSign(t[k], t[(k + 1) % 3], a, b) * orientation > 0;
        }

        /// <summary> the order of two points p, q on the line a-b along a to b: -1 if p is before q </summary>
        static int Along(Vector2d a, Vector2d b, Vector2d p, Vector2d q)
        {
            double dx = b.x - a.x, dy = b.y - a.y;
            if (Math.Abs(dx) >= Math.Abs(dy))
                return Cmp(p.x, q.x) * (dx > 0 ? 1 : -1);
            return Cmp(p.y, q.y) * (dy > 0 ? 1 : -1);
        }

        /// <summary> p is strictly before q along a to b (p, q on the line a-b) </summary>
        internal static bool Before(Vector2d a, Vector2d b, Vector2d p, Vector2d q)
        {
            return Along(a, b, p, q) < 0;
        }

        /// <summary>
        /// The order of the candidate c along a to b against the end e of the segment (e = a if atEnd is false, else b):
        /// -1 if c is before e, 0 if at e, +1 if after e
        /// </summary>
        internal static int Compare(Cand c, ref Triangle2d t, Vector2d a, Vector2d b, bool atEnd)
        {
            Vector2d e = atEnd ? b : a;
            if (c.Crossing == false)
                return Along(a, b, t[c.Index], e);
            // the crossing x = a + u (b - a) with the edge k to k + 1: with ra, rb the orientations of a, b against the
            // edge (the edge line value at a and b, linear in u): u = -ra / (rb - ra), u - 1 = -rb / (rb - ra)
            Vector2d p = t[c.Index], q = t[(c.Index + 1) % 3];
            int slope = CrossSign(p, q, a, b);      // the sign of rb - ra, not 0 for a crossed edge
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
            bool finite = (dp - dp) == 0 && (dq - dq) == 0;
            if (finite && ((dp < 0 && dq > 0) || (dp > 0 && dq < 0))) {
                double w = dp / (dp - dq);
                if (w >= 0 && w <= 1)
                    return w;
            }
            return Math.Abs(dp) <= Math.Abs(dq) ? 0 : 1;
        }

        /// <summary> the first point of the line a-b in the triangle (candidate c): the corner, or the crossing computed in floating point </summary>
        internal static Vector2d PointOf(Cand c, ref Triangle2d t, Vector2d a, Vector2d b)
        {
            if (c.Crossing == false)
                return t[c.Index];
            Vector2d p = t[c.Index], q = t[(c.Index + 1) % 3];
            // the distances of the edge ends to the line, with the exact signs (opposite): w = dp / (dp - dq)
            Vector2d d = b - a;
            // (d scaled by a power of two to a largest coordinate in [1, 2): the products below cannot overflow, and the
            // fraction does not change)
            double md = Math.Max(Math.Abs(d.x), Math.Abs(d.y));
            if (md > 0 && md - md == 0) {
                int e = (int)((BitConverter.DoubleToInt64Bits(md) >> 52) & 0x7FF);
                if (e > 0 && e < 2046)
                    d *= BitConverter.Int64BitsToDouble((long)(2046 - e) << 52);
            }
            double dp = d.DotPerp(p - a), dq = d.DotPerp(q - a);
            return p + CrossingFraction(dp, dq) * (q - p);
        }
    }
}
