// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Exact intersection tests for closed triangles and segments in 3D, built only on the exact
    /// ExactPredicates.Orient3d / Orient2d, so touching, coplanar and nearly touching configurations are
    /// decided correctly at any scale and position (IntrTriangle3Triangle3 uses a fixed epsilon).
    /// Triangle-triangle follows Devillers and Guigue, "Faster triangle-triangle intersection tests"
    /// (2002); degenerate (collinear) triangles are handled as segments.
    /// </summary>
    public static class ExactTriangleIntersection
    {
        /// <summary> do the closed triangles a and b share at least one point? </summary>
        [MethodImpl(JitHints.Optimize)]
        public static bool Intersects(ref Triangle3d a, ref Triangle3d b)
        {
            Vector3d p1 = a.V0, q1 = a.V1, r1 = a.V2, p2 = b.V0, q2 = b.V1, r2 = b.V2;
            bool degA = is_degenerate(p1, q1, r1), degB = is_degenerate(p2, q2, r2);
            if (degA || degB)
                return degenerate_case(ref a, ref b, degA, degB);

            int dp1 = ExactPredicates.Orient3d(p2, q2, r2, p1);
            int dq1 = ExactPredicates.Orient3d(p2, q2, r2, q1);
            int dr1 = ExactPredicates.Orient3d(p2, q2, r2, r1);
            if (dp1 * dq1 > 0 && dp1 * dr1 > 0)
                return false;
            int dp2 = ExactPredicates.Orient3d(p1, q1, r1, p2);
            int dq2 = ExactPredicates.Orient3d(p1, q1, r1, q2);
            int dr2 = ExactPredicates.Orient3d(p1, q1, r1, r2);
            if (dp2 * dq2 > 0 && dp2 * dr2 > 0)
                return false;

            // permute so that p1 is alone on its side of b's plane (or on it), then the same for b
            if (dp1 > 0) {
                if (dq1 > 0) return tri_tri(r1, p1, q1, p2, r2, q2, dp2, dr2, dq2);
                else if (dr1 > 0) return tri_tri(q1, r1, p1, p2, r2, q2, dp2, dr2, dq2);
                else return tri_tri(p1, q1, r1, p2, q2, r2, dp2, dq2, dr2);
            } else if (dp1 < 0) {
                if (dq1 < 0) return tri_tri(r1, p1, q1, p2, q2, r2, dp2, dq2, dr2);
                else if (dr1 < 0) return tri_tri(q1, r1, p1, p2, q2, r2, dp2, dq2, dr2);
                else return tri_tri(p1, q1, r1, p2, r2, q2, dp2, dr2, dq2);
            } else {
                if (dq1 < 0) {
                    if (dr1 >= 0) return tri_tri(q1, r1, p1, p2, r2, q2, dp2, dr2, dq2);
                    else return tri_tri(p1, q1, r1, p2, q2, r2, dp2, dq2, dr2);
                } else if (dq1 > 0) {
                    if (dr1 > 0) return tri_tri(p1, q1, r1, p2, r2, q2, dp2, dr2, dq2);
                    else return tri_tri(q1, r1, p1, p2, q2, r2, dp2, dq2, dr2);
                } else {
                    if (dr1 > 0) return tri_tri(r1, p1, q1, p2, q2, r2, dp2, dq2, dr2);
                    else if (dr1 < 0) return tri_tri(r1, p1, q1, p2, r2, q2, dp2, dr2, dq2);
                    else return coplanar(p1, q1, r1, p2, q2, r2);
                }
            }
        }

        [MethodImpl(JitHints.Optimize)]
        static bool tri_tri(Vector3d p1, Vector3d q1, Vector3d r1, Vector3d p2, Vector3d q2, Vector3d r2, int dp2, int dq2, int dr2)
        {
            if (dp2 > 0) {
                if (dq2 > 0) return check_min_max(p1, r1, q1, r2, p2, q2);
                else if (dr2 > 0) return check_min_max(p1, r1, q1, q2, r2, p2);
                else return check_min_max(p1, q1, r1, p2, q2, r2);
            } else if (dp2 < 0) {
                if (dq2 < 0) return check_min_max(p1, q1, r1, r2, p2, q2);
                else if (dr2 < 0) return check_min_max(p1, q1, r1, q2, r2, p2);
                else return check_min_max(p1, r1, q1, p2, q2, r2);
            } else {
                if (dq2 < 0) {
                    if (dr2 >= 0) return check_min_max(p1, r1, q1, q2, r2, p2);
                    else return check_min_max(p1, q1, r1, p2, q2, r2);
                } else if (dq2 > 0) {
                    if (dr2 > 0) return check_min_max(p1, r1, q1, p2, q2, r2);
                    else return check_min_max(p1, q1, r1, q2, r2, p2);
                } else {
                    if (dr2 > 0) return check_min_max(p1, q1, r1, r2, p2, q2);
                    else if (dr2 < 0) return check_min_max(p1, r1, q1, r2, p2, q2);
                    else return coplanar(p1, q1, r1, p2, q2, r2);
                }
            }
        }

        // the two intervals on the intersection line of the planes overlap
        [MethodImpl(JitHints.Optimize)]
        static bool check_min_max(Vector3d p1, Vector3d q1, Vector3d r1, Vector3d p2, Vector3d q2, Vector3d r2)
        {
            if (ExactPredicates.Orient3d(q1, p2, p1, q2) > 0)
                return false;
            if (ExactPredicates.Orient3d(p1, p2, r1, r2) > 0)
                return false;
            return true;
        }


        /// <summary>
        /// reference test (slower): closed triangles intersect iff an edge of one intersects the other triangle
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static bool IntersectsByEdges(ref Triangle3d a, ref Triangle3d b)
        {
            for (int k = 0; k < 3; ++k) {
                if (SegmentIntersectsTriangle(a[k], a[(k + 1) % 3], ref b)) return true;
                if (SegmentIntersectsTriangle(b[k], b[(k + 1) % 3], ref a)) return true;
            }
            return false;
        }

        /// <summary> does the closed segment s-t intersect the closed triangle? </summary>
        [MethodImpl(JitHints.Optimize)]
        public static bool SegmentIntersectsTriangle(Vector3d s, Vector3d t, ref Triangle3d tri)
        {
            Vector3d a = tri.V0, b = tri.V1, c = tri.V2;
            if (is_degenerate(a, b, c)) {
                for (int k = 0; k < 3; ++k)
                    if (SegmentsIntersect(s, t, tri[k], tri[(k + 1) % 3])) return true;
                return false;
            }
            int ds = ExactPredicates.Orient3d(a, b, c, s), dt = ExactPredicates.Orient3d(a, b, c, t);
            if (ds * dt > 0)
                return false;
            if (ds == 0 && dt == 0) {
                int axis = projection_axis(a, b, c);
                Vector2d s2 = drop(s, axis), t2 = drop(t, axis), a2 = drop(a, axis), b2 = drop(b, axis), c2 = drop(c, axis);
                if (point_in_triangle_2d(s2, a2, b2, c2) || point_in_triangle_2d(t2, a2, b2, c2)) return true;
                return segments_intersect_2d(s2, t2, a2, b2) || segments_intersect_2d(s2, t2, b2, c2) || segments_intersect_2d(s2, t2, c2, a2);
            }
            if (s == t) {
                // a point on the plane
                int axis = projection_axis(a, b, c);
                return point_in_triangle_2d(drop(s, axis), drop(a, axis), drop(b, axis), drop(c, axis));
            }
            // the line through s, t crosses the plane inside the segment: does it pass through the triangle?
            int o1 = ExactPredicates.Orient3d(s, t, a, b), o2 = ExactPredicates.Orient3d(s, t, b, c), o3 = ExactPredicates.Orient3d(s, t, c, a);
            bool pos = o1 > 0 || o2 > 0 || o3 > 0, neg = o1 < 0 || o2 < 0 || o3 < 0;
            return !(pos && neg);
        }

        /// <summary> do the closed segments p-q and r-s intersect? </summary>
        [MethodImpl(JitHints.Optimize)]
        public static bool SegmentsIntersect(Vector3d p, Vector3d q, Vector3d r, Vector3d s)
        {
            if (ExactPredicates.Orient3d(p, q, r, s) != 0)
                return false;
            // coplanar: projections along at least one axis are injective on their plane, and an
            // intersection shows in every projection
            for (int axis = 0; axis < 3; ++axis)
                if (segments_intersect_2d(drop(p, axis), drop(q, axis), drop(r, axis), drop(s, axis)) == false)
                    return false;
            return true;
        }


        [MethodImpl(JitHints.Optimize)]
        static bool coplanar(Vector3d p1, Vector3d q1, Vector3d r1, Vector3d p2, Vector3d q2, Vector3d r2)
        {
            int axis = projection_axis(p1, q1, r1);
            Vector2d a0 = drop(p1, axis), a1 = drop(q1, axis), a2 = drop(r1, axis);
            Vector2d b0 = drop(p2, axis), b1 = drop(q2, axis), b2 = drop(r2, axis);
            Vector2d[] A = { a0, a1, a2 }, B = { b0, b1, b2 };
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    if (segments_intersect_2d(A[i], A[(i + 1) % 3], B[j], B[(j + 1) % 3])) return true;
            return point_in_triangle_2d(a0, b0, b1, b2) || point_in_triangle_2d(b0, a0, a1, a2);
        }

        [MethodImpl(JitHints.Optimize)]
        static bool degenerate_case(ref Triangle3d a, ref Triangle3d b, bool degA, bool degB)
        {
            if (degA && degB) {
                for (int i = 0; i < 3; ++i)
                    for (int j = 0; j < 3; ++j)
                        if (SegmentsIntersect(a[i], a[(i + 1) % 3], b[j], b[(j + 1) % 3])) return true;
                return false;
            }
            if (degA) {
                for (int k = 0; k < 3; ++k)
                    if (SegmentIntersectsTriangle(a[k], a[(k + 1) % 3], ref b)) return true;
                return false;
            }
            for (int k = 0; k < 3; ++k)
                if (SegmentIntersectsTriangle(b[k], b[(k + 1) % 3], ref a)) return true;
            return false;
        }

        // collinear (or coincident) points: degenerate in every axis projection
        [MethodImpl(JitHints.Optimize)]
        static bool is_degenerate(Vector3d a, Vector3d b, Vector3d c)
        {
            for (int axis = 0; axis < 3; ++axis)
                if (ExactPredicates.Orient2d(drop(a, axis), drop(b, axis), drop(c, axis)) != 0)
                    return false;
            return true;
        }

        // an axis along which the (non-degenerate) triangle does not project to a segment
        [MethodImpl(JitHints.Optimize)]
        static int projection_axis(Vector3d a, Vector3d b, Vector3d c)
        {
            Vector3d n = (b - a).Cross(c - a);
            int best = (Math.Abs(n.x) >= Math.Abs(n.y) && Math.Abs(n.x) >= Math.Abs(n.z)) ? 0 : (Math.Abs(n.y) >= Math.Abs(n.z) ? 1 : 2);
            if (ExactPredicates.Orient2d(drop(a, best), drop(b, best), drop(c, best)) != 0)
                return best;
            for (int axis = 0; axis < 3; ++axis)
                if (ExactPredicates.Orient2d(drop(a, axis), drop(b, axis), drop(c, axis)) != 0)
                    return axis;
            return best;
        }

        static Vector2d drop(Vector3d v, int axis)
        {
            return (axis == 0) ? new Vector2d(v.y, v.z) : (axis == 1) ? new Vector2d(v.z, v.x) : new Vector2d(v.x, v.y);
        }

        [MethodImpl(JitHints.Optimize)]
        static bool point_in_triangle_2d(Vector2d p, Vector2d a, Vector2d b, Vector2d c)
        {
            int o1 = ExactPredicates.Orient2d(a, b, p), o2 = ExactPredicates.Orient2d(b, c, p), o3 = ExactPredicates.Orient2d(c, a, p);
            bool pos = o1 > 0 || o2 > 0 || o3 > 0, neg = o1 < 0 || o2 < 0 || o3 < 0;
            if (pos && neg)
                return false;
            if (pos || neg)
                return true;
            // degenerate triangle (all zero): p on its segment hull
            return on_segment(a, b, p) || on_segment(b, c, p) || on_segment(c, a, p);
        }

        [MethodImpl(JitHints.Optimize)]
        static bool segments_intersect_2d(Vector2d p, Vector2d q, Vector2d r, Vector2d s)
        {
            int o1 = ExactPredicates.Orient2d(p, q, r), o2 = ExactPredicates.Orient2d(p, q, s);
            int o3 = ExactPredicates.Orient2d(r, s, p), o4 = ExactPredicates.Orient2d(r, s, q);
            if (o1 * o2 < 0 && o3 * o4 < 0)
                return true;
            if (o1 == 0 && on_segment(p, q, r)) return true;
            if (o2 == 0 && on_segment(p, q, s)) return true;
            if (o3 == 0 && on_segment(r, s, p)) return true;
            if (o4 == 0 && on_segment(r, s, q)) return true;
            return false;
        }

        // x collinear with p-q: is it within the segment's bounding box (then on the segment)
        [MethodImpl(JitHints.Optimize)]
        static bool on_segment(Vector2d p, Vector2d q, Vector2d x)
        {
            if (ExactPredicates.Orient2d(p, q, x) != 0)
                return false;
            return Math.Min(p.x, q.x) <= x.x && x.x <= Math.Max(p.x, q.x) && Math.Min(p.y, q.y) <= x.y && x.y <= Math.Max(p.y, q.y);
        }

        /// <summary> zero area: the three points are collinear (exact) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static bool IsDegenerate(ref Triangle3d t)
        {
            Vector3d n = (t.V1 - t.V0).Cross(t.V2 - t.V0);
            // (s * s, not Math.Pow(s, 2): this runs for every candidate of the self-intersection checks, and Pow costs about
            // 4 times as much; the decision is the same, see IsDegenerate_PrefilterThreshold)
            double s = (t.V1 - t.V0).LengthSquared + (t.V2 - t.V0).LengthSquared;
            if (n.LengthSquared > 1e-24 * (s * s))
                return false;
            // collinear in 3D iff collinear in all three axis projections
            for (int k = 0; k < 3; ++k)
                if (ExactPredicates.Orient2d(project(t.V0, k), project(t.V1, k), project(t.V2, k)) != 0)
                    return false;
            return true;
        }

        [MethodImpl(JitHints.Optimize)]
        static int dominant_axis(Vector3d n)
        {
            double x = Math.Abs(n.x), y = Math.Abs(n.y), z = Math.Abs(n.z);
            return (x >= y && x >= z) ? 0 : (y >= z ? 1 : 2);
        }

        // drop coordinate ax
        static Vector2d project(Vector3d p, int ax)
        {
            return ax == 0 ? new Vector2d(p.y, p.z) : (ax == 1 ? new Vector2d(p.z, p.x) : new Vector2d(p.x, p.y));
        }

        /// <summary>
        /// For triangles of a mesh (ia, ib their vertex ids): do they intersect anywhere beyond the vertices or
        /// edge they share? Pairs without shared vertices: Intersects. A shared vertex: an edge opposite it
        /// meets the other triangle, or (coplanar) their corners overlap. A shared edge: coplanar with the far
        /// corners on the same side (a fold lying flat). Exact. A degenerate triangle (collinear corners, IsDegenerate) is
        /// the segment hull of its corners: with a shared vertex s it meets the other triangle beyond s iff one of its
        /// segments from s lies in the other's plane and leaves s into the other's closed corner (or, both degenerate,
        /// lies on a ray of the other from s); with a shared edge iff both are degenerate and extend beyond the same end
        /// of the edge (#1551).
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static bool IntersectsBeyondShared(Index3i ia, ref Triangle3d A, Index3i ib, ref Triangle3d B)
        {
            // shared vertices: corner indices in A and B
            int nShared = 0, sa0 = -1, sb0 = -1, sa1 = -1, sb1 = -1;
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    if (ia[i] == ib[j]) {
                        if (nShared == 0) { sa0 = i; sb0 = j; } else { sa1 = i; sb1 = j; }
                        nShared++;
                    }
            if (nShared == 0)
                return Intersects(ref A, ref B);
            // degenerate triangles (collinear corners): segments from the shared part, not planes (#1551)
            bool degA = is_degenerate(A.V0, A.V1, A.V2), degB = is_degenerate(B.V0, B.V1, B.V2);
            if (degA || degB)
                return degenerate_beyond_shared(nShared, sa0, sb0, sa1, sb1, ref A, ref B, degA, degB);
            if (nShared >= 2) {
                // shared edge: overlap only if coplanar with the far corners on the same side of the edge
                Vector3d a = A[sa0], b = A[sa1], c = A[3 - sa0 - sa1], d = B[3 - sb0 - sb1];
                if (ExactPredicates.Orient3d(a, b, c, d) != 0)
                    return false;
                int ax = plane_axis(a, b, c);
                Vector2d a2 = project(a, ax), b2 = project(b, ax);
                return ExactPredicates.Orient2d(a2, b2, project(c, ax)) * ExactPredicates.Orient2d(a2, b2, project(d, ax)) > 0;
            }
            // one shared vertex s: they intersect elsewhere iff the edge opposite s of one triangle meets
            // the other triangle, or (coplanar) their corners at s overlap
            Vector3d s = A[sa0];
            Vector3d p = A[(sa0 + 1) % 3], q = A[(sa0 + 2) % 3], u = B[(sb0 + 1) % 3], w = B[(sb0 + 2) % 3];
            if (SegmentIntersectsTriangle(p, q, ref B) || SegmentIntersectsTriangle(u, w, ref A))
                return true;
            if (ExactPredicates.Orient3d(s, p, q, u) != 0 || ExactPredicates.Orient3d(s, p, q, w) != 0)
                return false;
            int axis = plane_axis(s, p, q);
            Vector2d s2 = project(s, axis), p2 = project(p, axis), q2 = project(q, axis), u2 = project(u, axis), w2 = project(w, axis);
            if (ExactPredicates.Orient2d(s2, p2, q2) < 0) { var t = p2; p2 = q2; q2 = t; }
            if (ExactPredicates.Orient2d(s2, u2, w2) < 0) { var t = u2; u2 = w2; w2 = t; }
            return inside_corner(s2, p2, q2, u2) || inside_corner(s2, p2, q2, w2) || inside_corner(s2, u2, w2, p2) || inside_corner(s2, u2, w2, q2)
                || (same_ray(s2, p2, u2) && same_ray(s2, q2, w2));
        }

        // IntersectsBeyondShared where A or B is degenerate (collinear corners: the triangle is the segment hull of its
        // corners, the union of its edges). The part of A and B beyond the shared vertex s (or the shared edge a-b) is
        // convex and contains s (the edge); it is not empty iff it contains points arbitrarily close to s (the edge):
        // - one shared vertex s, A degenerate: A is the union of the segments s-p and s-q. A segment s-x meets the closed
        //   triangle B beyond s iff it lies in B's plane and leaves s into B's closed corner at s (B not degenerate), or
        //   (B degenerate, the union of s-u and s-w) iff x and u or w lie on one ray from s.
        // - a shared edge a-b with a != b, A degenerate: A lies on the line a-b. A non-degenerate B meets that line
        //   only in a-b, so the pair meets only in the edge; a degenerate B (on the line too) meets A beyond the edge iff
        //   both extend beyond the same end. With a = b the edge is a point: as one shared vertex.
        // Only exact predicates and coordinate comparisons.
        [MethodImpl(JitHints.Optimize)]
        static bool degenerate_beyond_shared(int nShared, int sa0, int sb0, int sa1, int sb1, ref Triangle3d A, ref Triangle3d B, bool degA, bool degB)
        {
            if (nShared >= 2) {
                Vector3d a = A[sa0], b = A[sa1], c = A[3 - sa0 - sa1], d = B[3 - sb0 - sb1];
                if (a == b)
                    return beyond_vertex(a, A[(sa0 + 1) % 3], A[(sa0 + 2) % 3], ref A, degA, B[(sb0 + 1) % 3], B[(sb0 + 2) % 3], ref B, degB);
                if (degA == false || degB == false)
                    return false;
                // both on the line a-b: the order along it is the order of a coordinate in which a and b differ
                int k = a.x != b.x ? 0 : (a.y != b.y ? 1 : 2);
                int dir = b[k] > a[k] ? 1 : -1;
                bool cb = dir * (c[k] - b[k]) > 0, ca = dir * (a[k] - c[k]) > 0;
                bool db = dir * (d[k] - b[k]) > 0, da = dir * (a[k] - d[k]) > 0;
                return (cb && db) || (ca && da);
            }
            Vector3d s = A[sa0];
            return beyond_vertex(s, A[(sa0 + 1) % 3], A[(sa0 + 2) % 3], ref A, degA, B[(sb0 + 1) % 3], B[(sb0 + 2) % 3], ref B, degB);
        }

        // triangles (s, p, q) = A and (s, u, w) = B sharing s, at least one degenerate: do they meet beyond s?
        static bool beyond_vertex(Vector3d s, Vector3d p, Vector3d q, ref Triangle3d A, bool degA, Vector3d u, Vector3d w, ref Triangle3d B, bool degB)
        {
            if (degA && degB)
                return same_ray_3d(s, p, u) || same_ray_3d(s, p, w) || same_ray_3d(s, q, u) || same_ray_3d(s, q, w);
            if (degB)
                return segment_into_corner(s, u, ref A) || segment_into_corner(s, w, ref A);
            return segment_into_corner(s, p, ref B) || segment_into_corner(s, q, ref B);
        }

        // does the segment s-x (x != s) meet the non-degenerate triangle T, of which s is a corner, beyond s? Iff x lies
        // in T's plane and the direction s->x is in T's closed corner at s (T convex: then a piece of the segment at s is
        // in T; otherwise the segment meets T only at s)
        [MethodImpl(JitHints.Optimize)]
        static bool segment_into_corner(Vector3d s, Vector3d x, ref Triangle3d T)
        {
            if (x == s || ExactPredicates.Orient3d(T.V0, T.V1, T.V2, x) != 0)
                return false;
            int i = T.V0 == s ? 0 : (T.V1 == s ? 1 : 2);
            Vector3d u = T[(i + 1) % 3], w = T[(i + 2) % 3];
            int axis = plane_axis(s, u, w);
            Vector2d s2 = project(s, axis), u2 = project(u, axis), w2 = project(w, axis), x2 = project(x, axis);
            if (ExactPredicates.Orient2d(s2, u2, w2) < 0) { var t = u2; u2 = w2; w2 = t; }
            return ExactPredicates.Orient2d(s2, u2, x2) >= 0 && ExactPredicates.Orient2d(s2, x2, w2) >= 0;
        }

        // x != s, y != s, and x, y on one ray from s: collinear (in all three axis projections) and every coordinate
        // difference with the same sign
        static bool same_ray_3d(Vector3d s, Vector3d x, Vector3d y)
        {
            if (x == s || y == s)
                return false;
            for (int k = 0; k < 3; ++k)
                if (ExactPredicates.Orient2d(project(s, k), project(x, k), project(y, k)) != 0)
                    return false;
            return x.x.CompareTo(s.x) == y.x.CompareTo(s.x) && x.y.CompareTo(s.y) == y.y.CompareTo(s.y) && x.z.CompareTo(s.z) == y.z.CompareTo(s.z);
        }

        /// <summary>
        /// IntersectsBeyondShared for two non-degenerate triangles whose corners are known to lie in one plane (the caller has the Orient3d of the far corners, all 0): the same result, without
        /// the Orient3d calls that are then 0 (the coplanar branches of IntersectsBeyondShared and of
        /// SegmentIntersectsTriangle, in the same order, with the same projections). ExactMeshArrangement's pair
        /// search calls it for coplanar neighbours (docs/design/CHEAPER_CONSTRUCTIONS.md, section 4.1).
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static bool IntersectsBeyondSharedCoplanar(Index3i ia, ref Triangle3d A, Index3i ib, ref Triangle3d B)
        {
            int nShared = 0, sa0 = -1, sb0 = -1, sa1 = -1, sb1 = -1;
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    if (ia[i] == ib[j]) {
                        if (nShared == 0) { sa0 = i; sb0 = j; } else { sa1 = i; sb1 = j; }
                        nShared++;
                    }
            if (nShared == 0)
                return Intersects(ref A, ref B);
            if (nShared >= 2) {
                Vector3d a = A[sa0], b = A[sa1], c = A[3 - sa0 - sa1], d = B[3 - sb0 - sb1];
                int ax = plane_axis(a, b, c);
                Vector2d a2 = project(a, ax), b2 = project(b, ax);
                return ExactPredicates.Orient2d(a2, b2, project(c, ax)) * ExactPredicates.Orient2d(a2, b2, project(d, ax)) > 0;
            }
            Vector3d s = A[sa0];
            Vector3d p = A[(sa0 + 1) % 3], q = A[(sa0 + 2) % 3], u = B[(sb0 + 1) % 3], w = B[(sb0 + 2) % 3];
            if (segment_triangle_coplanar(p, q, ref B) || segment_triangle_coplanar(u, w, ref A))
                return true;
            int axis = plane_axis(s, p, q);
            Vector2d s2 = project(s, axis), p2 = project(p, axis), q2 = project(q, axis), u2 = project(u, axis), w2 = project(w, axis);
            if (ExactPredicates.Orient2d(s2, p2, q2) < 0) { var t = p2; p2 = q2; q2 = t; }
            if (ExactPredicates.Orient2d(s2, u2, w2) < 0) { var t = u2; u2 = w2; w2 = t; }
            return inside_corner(s2, p2, q2, u2) || inside_corner(s2, p2, q2, w2) || inside_corner(s2, u2, w2, p2) || inside_corner(s2, u2, w2, q2)
                || (same_ray(s2, p2, u2) && same_ray(s2, q2, w2));
        }

        // SegmentIntersectsTriangle for a non-degenerate triangle and a segment in its plane (its branch ds = dt = 0)
        [MethodImpl(JitHints.Optimize)]
        static bool segment_triangle_coplanar(Vector3d s, Vector3d t, ref Triangle3d tri)
        {
            Vector3d a = tri.V0, b = tri.V1, c = tri.V2;
            int axis = projection_axis(a, b, c);
            Vector2d s2 = drop(s, axis), t2 = drop(t, axis), a2 = drop(a, axis), b2 = drop(b, axis), c2 = drop(c, axis);
            if (point_in_triangle_2d(s2, a2, b2, c2) || point_in_triangle_2d(t2, a2, b2, c2)) return true;
            return segments_intersect_2d(s2, t2, a2, b2) || segments_intersect_2d(s2, t2, b2, c2) || segments_intersect_2d(s2, t2, c2, a2);
        }

        // for points coplanar with the non-degenerate triangle (s, p, q): an axis whose projection is injective on
        // its plane, so that the 2D tests decide the 3D configuration. Chosen exactly: one along which s, p, q do not
        // project to collinear points (the largest component of the floating-point normal first, which is right unless
        // it rounds badly, eg to 0 for a sliver); for a degenerate triangle (no such axis) the guess
        [MethodImpl(JitHints.Optimize)]
        static int plane_axis(Vector3d s, Vector3d p, Vector3d q)
        {
            int guess = dominant_axis((p - s).Cross(q - s));
            if (ExactPredicates.Orient2d(project(s, guess), project(p, guess), project(q, guess)) != 0)
                return guess;
            for (int k = 0; k < 3; ++k)
                if (k != guess && ExactPredicates.Orient2d(project(s, k), project(p, k), project(q, k)) != 0)
                    return k;
            return guess;
        }

        // is the direction s->x strictly inside the (counter-clockwise, < 180 degree) corner p-s-q?
        static bool inside_corner(Vector2d s, Vector2d p, Vector2d q, Vector2d x)
        {
            return ExactPredicates.Orient2d(s, p, x) > 0 && ExactPredicates.Orient2d(s, x, q) > 0;
        }

        // do a and b lie on one ray from s (a != s, b != s, s, a, b collinear)? Orient2d is exact; for collinear s, a, b the
        // vectors a - s and b - s are parallel, so they point the same way iff the signs of their components agree, and
        // the sign of a coordinate difference is an exact comparison. (The sign of the double dot product (a - s) . (b - s)
        // is not: its products underflow to 0 near 1e-170 and overflow near 1e154, #1349.) For a = s there is no ray: false.
        internal static bool same_ray(Vector2d s, Vector2d a, Vector2d b)
        {
            if (ExactPredicates.Orient2d(s, a, b) != 0)
                return false;
            if (a.x == s.x && a.y == s.y)
                return false;
            return a.x.CompareTo(s.x) == b.x.CompareTo(s.x) && a.y.CompareTo(s.y) == b.y.CompareTo(s.y);
        }


    }
}
