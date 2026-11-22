using System;
using System.Numerics;

namespace g3
{
    /// <summary>
    /// 3D triangle-triangle intersection (originally ported from WildMagic5; every decision is now exact).
    /// </summary>
    /// <remarks>
    /// <para>Call <see cref="Test"/> for a fast boolean hit test without computing contact geometry, or
    /// <see cref="Find"/> / <see cref="Compute"/> for full contact output (<see cref="Type"/>, <see cref="Points"/>,
    /// <see cref="PolygonPoints"/>). By default <see cref="ReportCoplanarIntersection"/> is <c>false</c>, so
    /// <see cref="Find"/> / <see cref="Compute"/> do <b>not</b> report coplanar intersections unless it is set to
    /// <c>true</c> (<see cref="Test"/> and <see cref="Intersects"/> always report intersecting coplanar triangles).</para>
    /// <para>Whether the triangles intersect is decided exactly, for every pair, as closed triangles (touching at a
    /// vertex or an edge counts): <see cref="Test"/>, <see cref="Intersects"/> and <see cref="Find"/> (with
    /// <see cref="ReportCoplanarIntersection"/> enabled for coplanar pairs) agree with
    /// <see cref="ExactTriangleIntersection"/>, with no epsilon: a triangle 1e-300 above another does not touch it.
    /// The answer is the same at every power of two scale. Non-finite coordinates give no intersection.</para>
    /// <para>A degenerate triangle (exactly collinear or coincident corners) is a segment or a point; for such input
    /// <see cref="Find"/> reports only whether the triangles touch (<see cref="IntersectionType.Unknown"/>, no contact
    /// points), and <see cref="Test"/> sets <see cref="Type"/> to <see cref="IntersectionType.Unknown"/> for a hit.</para>
    /// <para>The contact of a crossing pair is computed from exactly decided candidates. In the general crossing (no
    /// corner of one triangle in the plane of the other, no crossing edge of one coplanar with a crossing edge of the
    /// other, planes not nearly parallel) the two end points are computed in floating point from the crossing edges, a
    /// few ulp for well conditioned input; <see cref="Type"/> and <see cref="Quantity"/> are exact. All other pairs
    /// (touching contacts, coplanar and nearly parallel pairs) get their crossing coordinates from exact
    /// determinants, rounded once to double.</para>
    /// </remarks>
    public class IntrTriangle3Triangle3
    {
        Triangle3d triangle0;
        public Triangle3d Triangle0
        {
            get { return triangle0; }
            set { triangle0 = value; Result = IntersectionResult.NotComputed; }
        }

        Triangle3d triangle1;
        public Triangle3d Triangle1
        {
            get { return triangle1; }
            set { triangle1 = value; Result = IntersectionResult.NotComputed; }
        }

        /// <summary>
        /// If <c>true</c>, <see cref="Find"/> / <see cref="Compute"/> detect and return intersection polygons for
        /// coplanar triangles (more expensive). Default is <c>false</c>: when <c>false</c>, <see cref="Find"/> does
        /// <b>not</b> report coplanar intersections (<see cref="Test"/> and <see cref="Intersects"/> still do).
        /// </summary>
        public bool ReportCoplanarIntersection = false;

        // result flags
		public int Quantity = 0;
		public IntersectionResult Result = IntersectionResult.NotComputed;
		public IntersectionType Type = IntersectionType.Empty;

        // intersection points (for point, line)
        // only first Quantity elements are relevant
        public Vector3dTuple3 Points;

        // only valid if intersection type is Polygon
        public Vector3d[] PolygonPoints;


		public IntrTriangle3Triangle3(Triangle3d t0, Triangle3d t1)
		{
			triangle0 = t0;
            triangle1 = t1;
		}


        public IntrTriangle3Triangle3 Compute()
        {
            Find();
            return this;
        }


        public bool Find()
        {
            if (Result != IntersectionResult.NotComputed)
                return (Result != g3.IntersectionResult.NoIntersection);


            // in this code the results get initialized in subroutines, so we
            // set the defautl value here...
            Result = IntersectionResult.NoIntersection;
            Type = IntersectionType.Empty;
            Quantity = 0;

            // non-finite coordinates: no contact is defined
            if (finite(ref triangle0) == false || finite(ref triangle1) == false)
                return false;

            // the yes/no is the exact closed-triangle test (ExactTriangleIntersection, with the projection filter in
            // front where the pair is nearly coplanar), for every pair, so Find() agrees with Test() and Intersects()
            if (exact_intersects(ref triangle0, ref triangle1) == false)
                return false;

            // a degenerate triangle (collinear corners) has no plane: a segment or a point, for which only whether
            // the triangles touch is reported (Type Unknown, no points)
            if (ExactTriangleIntersection.IsDegenerate(ref triangle0) || ExactTriangleIntersection.IsDegenerate(ref triangle1)) {
                Type = IntersectionType.Unknown;
                Result = IntersectionResult.Intersects;
                return true;
            }

            // exactly coplanar pairs are reported only with ReportCoplanarIntersection
            bool isCoplanar = exactly_coplanar(ref triangle0, ref triangle1);
            if (isCoplanar && ReportCoplanarIntersection == false)
                return false;
            if (isCoplanar == false && find_transversal())
                return true;
            return find_exact(isCoplanar);
        }


        public bool Test()
        {
            return test_exact();
        }


        public static bool Intersects(ref Triangle3d triangle0, ref Triangle3d triangle1)
        {
            // (false for non-finite coordinates: no contact is defined)
            return exact_intersects(ref triangle0, ref triangle1);
        }


        // Test() decided exactly (coplanar, nearly parallel, slivers)
        bool test_exact()
        {
            if (exact_intersects(ref triangle0, ref triangle1) == false) {
                Type = IntersectionType.Empty;
                return false;
            }
            // The test query does not know the intersection set.
            bool plane = ExactTriangleIntersection.IsDegenerate(ref triangle0) == false && ExactTriangleIntersection.IsDegenerate(ref triangle1) == false
                && exactly_coplanar(ref triangle0, ref triangle1);
            Type = plane ? IntersectionType.Plane : IntersectionType.Unknown;
            return true;
        }

        static bool finite(ref Triangle3d t)
        {
            return t.V0.IsFinite && t.V1.IsFinite && t.V2.IsFinite;
        }

        // ExactTriangleIntersection.Intersects, the exact closed yes/no; false for non-finite input (no contact is
        // defined there). Pairs that are nearly coplanar go through TriangleTriangleFilter first (the same answer, but
        // the exact test is slow there: its Orient3d calls all need the exact stage). nearly_planar only chooses
        // between two routes with the same answer, so it needs no error bound.
        static bool exact_intersects(ref Triangle3d a, ref Triangle3d b)
        {
            if (finite(ref a) == false || finite(ref b) == false)
                return false;
            return nearly_planar(ref a, ref b) ? TriangleTriangleFilter.Intersects(ref a, ref b) : ExactTriangleIntersection.Intersects(ref a, ref b);
        }

        // b within about 1e-9 (relative) of the plane of a, or a without a plane (heuristic, see exact_intersects)
        static bool nearly_planar(ref Triangle3d a, ref Triangle3d b)
        {
            Vector3d n = (a.V1 - a.V0).Cross(a.V2 - a.V0);
            double nl = Math.Abs(n.x) + Math.Abs(n.y) + Math.Abs(n.z);
            if (nl == 0)
                return true;
            for (int i = 0; i < 3; ++i) {
                Vector3d d = b[i] - a.V0;
                if (Math.Abs(n.Dot(d)) > 1e-9 * nl * (Math.Abs(d.x) + Math.Abs(d.y) + Math.Abs(d.z)))
                    return false;
            }
            return true;
        }

        // Find() for the generic crossing of two triangles, decided exactly by signs: no corner of one lies in the
        // plane of the other, and no crossing edge of one is coplanar with a crossing edge of the other (the exact
        // Orient3d). Then the contact is a segment of positive length (a zero length overlap needs the end of an
        // edge of one to meet an edge of the other, on the line of the two planes, hence a coplanar pair), its ends
        // are crossing points of those edges, and the points are computed in floating point from them (a few ulp for
        // well conditioned input; Quantity, Type and the pair of edges per end are exact). Returns false, leaving the
        // exact route (find_exact) to decide, for anything else, also where the rounded values disagree with the
        // exact signs or the segment degenerates in floating point.
        bool find_transversal()
        {
            Triangle3d A = triangle0, B = triangle1;
            // the crossing points are computed from the floating-point normals and distances: only for well
            // conditioned triangles (no sliver: |n|^2 above 1e-12 of the squared edge lengths) whose planes are not nearly
            // parallel (sin^2 of the angle above 1e-8: an error of 1e-16 in a distance is then 1e-12 of the edge). A
            // heuristic choice of the route; the exact one decides anything it does not take.
            Vector3d nA = (A.V1 - A.V0).Cross(A.V2 - A.V0), nB = (B.V1 - B.V0).Cross(B.V2 - B.V0);
            double lA = nA.LengthSquared, lB = nB.LengthSquared;
            if ((lA > 1e-12 * (A.V1 - A.V0).LengthSquared * (A.V2 - A.V0).LengthSquared
                && lB > 1e-12 * (B.V1 - B.V0).LengthSquared * (B.V2 - B.V0).LengthSquared
                && nA.Cross(nB).LengthSquared >= 1e-8 * lA * lB) == false)
                return false;       // (also for overflow or NaN: the comparisons are false)
            int oa = odd_vertex(ref A, ref B), ob = odd_vertex(ref B, ref A);
            if (oa < 0 || ob < 0)
                return false;
            for (int i = 1; i <= 2; ++i)
                for (int j = 1; j <= 2; ++j)
                    if (ExactPredicates.Orient3d(A[oa], A[(oa + i) % 3], B[ob], B[(ob + j) % 3]) == 0)
                        return false;
            Vector3d a1, a2, b1, b2;
            if (crossing(ref A, oa, ref B, out a1, out a2) == false || crossing(ref B, ob, ref A, out b1, out b2) == false)
                return false;
            Vector3d D = a2 - a1;
            double dd = D.Dot(D);
            double u1 = D.Dot(b1 - a1), u2 = D.Dot(b2 - a1);
            Vector3d bLo = b1, bHi = b2;
            double uLo = u1, uHi = u2;
            if (u1 > u2) { bLo = b2; bHi = b1; uLo = u2; uHi = u1; }
            Vector3d lo = uLo > 0 ? bLo : a1, hi = uHi < dd ? bHi : a2;
            double ulo = Math.Max(0, uLo), uhi = Math.Min(dd, uHi);
            // (ulo < uhi also excludes equal points: the parameter of a point is computed from its coordinates alone, so equal
            // points have equal parameters; a1 and a2 have 0 and dd exactly)
            if ((ulo < uhi) == false || lo.IsFinite == false || hi.IsFinite == false)
                return false;
            Points[0] = lo;
            Points[1] = hi;
            Quantity = 2;
            Type = IntersectionType.Segment;
            Result = IntersectionResult.Intersects;
            return true;
        }

        // the corner of t that lies on one side of the plane of other alone (the other two on the other side), -1 if a
        // corner is in the plane or all three are on one side (exact)
        static int odd_vertex(ref Triangle3d t, ref Triangle3d other)
        {
            int s0 = ExactPredicates.Orient3d(other.V0, other.V1, other.V2, t.V0);
            int s1 = ExactPredicates.Orient3d(other.V0, other.V1, other.V2, t.V1);
            int s2 = ExactPredicates.Orient3d(other.V0, other.V1, other.V2, t.V2);
            if (s0 == 0 || s1 == 0 || s2 == 0)
                return -1;
            if (s1 == s2 && s0 != s1) return 0;
            if (s0 == s2 && s1 != s0) return 1;
            if (s0 == s1 && s2 != s0) return 2;
            return -1;
        }

        // where the two edges of t at its corner o cross the plane of other, in floating point (false if the rounded
        // distances do not have the exact signs: o on one side, the others on the other)
        static bool crossing(ref Triangle3d t, int o, ref Triangle3d other, out Vector3d p1, out Vector3d p2)
        {
            Vector3d n = (other.V1 - other.V0).Cross(other.V2 - other.V0);
            double d0 = n.Dot(t[o] - other.V0), d1 = n.Dot(t[(o + 1) % 3] - other.V0), d2 = n.Dot(t[(o + 2) % 3] - other.V0);
            p1 = p2 = Vector3d.Zero;
            if ((d0 > 0 && d1 < 0 && d2 < 0 || d0 < 0 && d1 > 0 && d2 > 0) == false)
                return false;
            p1 = t[o] + (d0 / (d0 - d1)) * (t[(o + 1) % 3] - t[o]);
            p2 = t[o] + (d0 / (d0 - d2)) * (t[(o + 2) % 3] - t[o]);
            return true;
        }

        static bool exactly_coplanar(ref Triangle3d a, ref Triangle3d b)
        {
            if (finite(ref a) == false || finite(ref b) == false)
                return false;
            for (int i = 0; i < 3; ++i)
                if (ExactPredicates.Orient3d(a.V0, a.V1, a.V2, b[i]) != 0)
                    return false;
            return true;
        }

        static Vector2d drop(Vector3d v, int axis)
        {
            return (axis == 0) ? new Vector2d(v.y, v.z) : (axis == 1) ? new Vector2d(v.z, v.x) : new Vector2d(v.x, v.y);
        }

        // Find() for coplanar or nearly parallel triangles: the exact yes/no of ExactTriangleIntersection, and the
        // contact from exactly decided candidates (see the class comment)
        bool find_exact(bool coplanar)
        {
            Result = IntersectionResult.NoIntersection;
            Type = IntersectionType.Empty;
            Quantity = 0;
            if (exact_intersects(ref triangle0, ref triangle1) == false)
                return false;
            var pts = new System.Collections.Generic.List<Vector3d>();
            contact_candidates(ref triangle0, ref triangle1, pts);
            contact_candidates(ref triangle1, ref triangle0, pts);
            // (pairs of edges are tried from both sides: the same crossing twice, maybe rounded differently)
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    add_crossing(triangle0[i], triangle0[(i + 1) % 3], triangle1[j], triangle1[(j + 1) % 3], pts);
            if (pts.Count == 0) {
                // (not expected: an intersection has a vertex among the candidates) the nearest point of triangle0
                Vector3d c = triangle1.V0;
                DistPoint3Triangle3.DistanceSqr(ref c, ref triangle0, out Vector3d q, out Vector3d bary);
                pts.Add(q);
            }
            Result = IntersectionResult.Intersects;
            if (coplanar) {
                // A n B is convex and every candidate is one of its vertices: in order around their centroid
                // (in a projection that is injective on the plane; rounded angles only order the output)
                int axis = 2;
                for (int k = 0; k < 3; ++k)
                    if (ExactPredicates.Orient2d(drop(triangle0.V0, k), drop(triangle0.V1, k), drop(triangle0.V2, k)) != 0) { axis = k; break; }
                Vector2d ctr = Vector2d.Zero;
                foreach (Vector3d p in pts) ctr += drop(p, axis);
                ctr /= pts.Count;
                var angles = new double[pts.Count];
                for (int k = 0; k < pts.Count; ++k) {
                    Vector2d d = drop(pts[k], axis) - ctr;
                    angles[k] = Math.Atan2(d.y, d.x);
                }
                Vector3d[] ordered = pts.ToArray();
                Array.Sort(angles, ordered);
                PolygonPoints = ordered;
                Quantity = ordered.Length;
                Type = IntersectionType.Polygon;
                return true;
            }
            // not coplanar: the candidates lie on the line of the two planes; its extreme two
            int a = 0, b = 0;
            double best = -1;
            for (int i = 0; i < pts.Count; ++i)
                for (int j = i + 1; j < pts.Count; ++j) {
                    // (the largest coordinate difference, not the squared distance: that underflows below 2^-512 and
                    // would make every pair equal; the candidates are collinear, so the order is the same)
                    Vector3d diff = pts[i] - pts[j];
                    double d = Math.Max(Math.Abs(diff.x), Math.Max(Math.Abs(diff.y), Math.Abs(diff.z)));
                    if (d > best) { best = d; a = i; b = j; }
                }
            Points[0] = pts[a];
            if (best > 0) {
                Points[1] = pts[b];
                Quantity = 2;
                Type = IntersectionType.Segment;
            } else {
                Quantity = 1;
                Type = IntersectionType.Point;
            }
            return true;
        }

        static void add_unique(Vector3d p, System.Collections.Generic.List<Vector3d> pts)
        {
            foreach (Vector3d q in pts)
                if (q == p)
                    return;
            pts.Add(p);
        }

        // candidates of tri n other: corners of tri in other (closed, exact), and where an edge of tri crosses the
        // plane of other strictly between its ends inside other (exact; the point rounded)
        static void contact_candidates(ref Triangle3d tri, ref Triangle3d other, System.Collections.Generic.List<Vector3d> pts)
        {
            Vector3d o0 = other.V0, o1 = other.V1, o2 = other.V2;
            for (int i = 0; i < 3; ++i)
                if (ExactTriangleIntersection.SegmentIntersectsTriangle(tri[i], tri[i], ref other))
                    add_unique(tri[i], pts);
            for (int i = 0; i < 3; ++i) {
                Vector3d s = tri[i], t = tri[(i + 1) % 3];
                int ds = ExactPredicates.Orient3d(o0, o1, o2, s), dt = ExactPredicates.Orient3d(o0, o1, o2, t);
                if (ds * dt >= 0 || ExactTriangleIntersection.SegmentIntersectsTriangle(s, t, ref other) == false)
                    continue;
                // the point s + Ds / (Ds - Dt) (t - s) where the line meets the plane, with the exact determinants
                // Ds, Dt (nearly parallel edges make a rounded parameter useless), rounded at the end
                BigInteger[] g = grid(out int e0, o0, o1, o2, s, t);
                BigInteger dS = det(g, 0, 3, 6, 9), dT = det(g, 0, 3, 6, 12);
                add_unique(lerp(g, 9, 12, dS, dT, e0), pts);
            }
        }

        // the coordinates of the points as integers X with value X 2^e0 (exact; e0 the smallest exponent)
        static BigInteger[] grid(out int e0, params Vector3d[] pts)
        {
            int n = 3 * pts.Length;
            var m = new BigInteger[n];
            var e = new int[n];
            e0 = int.MaxValue;
            for (int i = 0; i < n; ++i) {
                ExactPoint3.Decompose(pts[i / 3][i % 3], out m[i], out e[i]);
                if (m[i].IsZero == false)
                    e0 = Math.Min(e0, e[i]);
            }
            if (e0 == int.MaxValue)
                e0 = 0;
            for (int i = 0; i < n; ++i)
                if (m[i].IsZero == false)
                    m[i] <<= e[i] - e0;
            return m;
        }

        // the orientation determinant of the points at offsets a, b, c, d of g (3 coordinates each), exact
        static BigInteger det(BigInteger[] g, int a, int b, int c, int d)
        {
            return ExactPoint3.Det3(g[b] - g[a], g[b + 1] - g[a + 1], g[b + 2] - g[a + 2],
                                    g[c] - g[a], g[c + 1] - g[a + 1], g[c + 2] - g[a + 2],
                                    g[d] - g[a], g[d + 1] - g[a + 1], g[d + 2] - g[a + 2]);
        }

        // the 2D orientation of r, s, x (offsets into g) in the coordinates a0, a1, exact
        static BigInteger det2(BigInteger[] g, int r, int s, int x, int a0, int a1)
        {
            return (g[s + a0] - g[r + a0]) * (g[x + a1] - g[r + a1]) - (g[s + a1] - g[r + a1]) * (g[x + a0] - g[r + a0]);
        }

        // (dp Q - dq P) / (dp - dq) for the points P, Q at offsets p, q of g (the point where a quantity linear along
        // P-Q with values dp, dq of opposite signs is 0), rounded
        static Vector3d lerp(BigInteger[] g, int p, int q, BigInteger dp, BigInteger dq, int e0)
        {
            BigInteger den = dp - dq;
            if (den.Sign < 0) { dp = -dp; dq = -dq; den = -den; }
            var r = new Vector3d();
            for (int k = 0; k < 3; ++k)
                r[k] = ExactPoint3.ToDouble(dp * g[q + k] - dq * g[p + k], den, e0);
            return r;
        }

        // a proper crossing of the coplanar edges p-q and r-s (their ends strictly on both sides of the other edge,
        // exact): the crossing point, rounded
        static void add_crossing(Vector3d p, Vector3d q, Vector3d r, Vector3d s, System.Collections.Generic.List<Vector3d> pts)
        {
            if (ExactPredicates.Orient3d(p, q, r, s) != 0)
                return;
            for (int k = 0; k < 3; ++k) {
                Vector2d p2 = drop(p, k), q2 = drop(q, k), r2 = drop(r, k), s2 = drop(s, k);
                int o1 = ExactPredicates.Orient2d(p2, q2, r2), o2 = ExactPredicates.Orient2d(p2, q2, s2);
                int o3 = ExactPredicates.Orient2d(r2, s2, p2), o4 = ExactPredicates.Orient2d(r2, s2, q2);
                if (o1 == 0 && o2 == 0 && o3 == 0 && o4 == 0)
                    continue;       // (this projection maps their plane to a line; or all four are collinear)
                if (o1 * o2 < 0 && o3 * o4 < 0) {
                    // p + Dp / (Dp - Dq) (q - p) with the exact 2D determinants of p, q against r-s
                    BigInteger[] g = grid(out int e0, p, q, r, s);
                    int a0 = (k + 1) % 3, a1 = (k + 2) % 3;
                    BigInteger dP = det2(g, 6, 9, 0, a0, a1), dQ = det2(g, 6, 9, 3, a0, a1);
                    add_unique(lerp(g, 0, 3, dP, dQ, e0), pts);
                }
                return;
            }
        }

        static public void ProjectOntoAxis ( ref Triangle3d triangle, ref Vector3d axis, out double fmin, out double fmax)
        {
            double dot0 = axis.Dot(triangle.V0);
            double dot1 = axis.Dot(triangle.V1);
            double dot2 = axis.Dot(triangle.V2);

            fmin = dot0;
            fmax = fmin;

            if (dot1 < fmin)
            {
                fmin = dot1;
            }
            else if (dot1 > fmax)
            {
                fmax = dot1;
            }

            if (dot2 < fmin)
            {
                fmin = dot2;
            }
            else if (dot2 > fmax)
            {
                fmax = dot2;
            }
        }



        static public void TrianglePlaneRelations ( ref Triangle3d triangle, ref Plane3d plane,
            out Vector3d distance, out Index3i sign, out int positive, out int negative, out int zero)
        {
            // Compute the signed distances of triangle vertices to the plane.  Use
            // an epsilon-thick plane test.
            positive = 0;
            negative = 0;
            zero = 0;
            distance = Vector3d.Zero;
            sign = Index3i.Zero;
            for (int i = 0; i < 3; ++i) {
                distance[i] = plane.DistanceTo(triangle[i]);
                if (distance[i] > MathUtil.ZeroTolerance) {
                    sign[i] = 1;
                    positive++;
                } else if (distance[i] < -MathUtil.ZeroTolerance) {
                    sign[i] = -1;
                    negative++;
                } else {
                    distance[i] = (double)0;
                    sign[i] = 0;
                    zero++;
                }
            }
        }
    }
}
