// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;

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
        public static bool IntersectsByEdges(ref Triangle3d a, ref Triangle3d b)
        {
            for (int k = 0; k < 3; ++k) {
                if (SegmentIntersectsTriangle(a[k], a[(k + 1) % 3], ref b)) return true;
                if (SegmentIntersectsTriangle(b[k], b[(k + 1) % 3], ref a)) return true;
            }
            return false;
        }

        /// <summary> does the closed segment s-t intersect the closed triangle? </summary>
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
        static bool is_degenerate(Vector3d a, Vector3d b, Vector3d c)
        {
            for (int axis = 0; axis < 3; ++axis)
                if (ExactPredicates.Orient2d(drop(a, axis), drop(b, axis), drop(c, axis)) != 0)
                    return false;
            return true;
        }

        // an axis along which the (non-degenerate) triangle does not project to a segment
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
        static bool on_segment(Vector2d p, Vector2d q, Vector2d x)
        {
            if (ExactPredicates.Orient2d(p, q, x) != 0)
                return false;
            return Math.Min(p.x, q.x) <= x.x && x.x <= Math.Max(p.x, q.x) && Math.Min(p.y, q.y) <= x.y && x.y <= Math.Max(p.y, q.y);
        }
    }
}
