// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// A filter in front of the exact triangle-triangle test ExactTriangleIntersection.IntersectsBeyondShared, for
    /// the self-intersection checks (Reducer, Remesher). It decides the pairs a projection onto an axis plane settles,
    /// with the exact ExactPredicates.Orient2d only, so it adds no error bound of its own:
    /// <para>
    /// - both triangles in one axis plane (all six coordinates on that axis equal): the problem is 2D, and the
    ///   projection decides both ways. The exact 3D test would spend its Orient3d calls (all exactly 0, so all in
    ///   the exact stage) to find that out.
    /// </para><para>
    /// - otherwise, projected along the dominant axis of A's normal: a projection that separates them separates the
    ///   triangles in 3D (the projection is linear). Without a shared vertex, an edge line of one projected triangle
    ///   with the other strictly outside (for triangles in 2D such a line exists iff they are disjoint). With a shared
    ///   vertex s, the projected corners at s overlap only at s; with a shared edge, the far corners lie strictly on
    ///   opposite sides of it. Both need A's projection to be one to one (its projected corners not collinear), then
    ///   the triangles meet only at s, or only on the edge. Nearly coplanar neighbours, which the exact test decides
    ///   in its slow stage, are decided here.
    /// </para>
    /// Anything else is left to the exact test (Undecided). The filter only answers where the exact answer is known,
    /// so it never changes a decision of the exact test (checked against it, also on coplanar slivers whose
    /// floating-point normal is 0, in TriangleTriangleFilterTests).
    /// </summary>
    internal static class TriangleTriangleFilter
    {
        internal const int Undecided = -1, Separate = 0, Intersecting = 1;

        /// <summary>
        /// ExactTriangleIntersection.IntersectsBeyondShared (the same answer), with the filter first
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static bool IntersectsBeyondShared(Index3i ia, ref Triangle3d A, Index3i ib, ref Triangle3d B)
        {
            int r = BeyondShared(ia, ref A, ib, ref B);
            if (r == Undecided)
                return ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B);
            return r == Intersecting;
        }

        /// <summary>
        /// ExactTriangleIntersection.Intersects for closed triangles (the same answer), with the filter first
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static bool Intersects(ref Triangle3d A, ref Triangle3d B)
        {
            int r = BeyondShared(SeparateA, ref A, SeparateB, ref B);
            if (r == Undecided)
                return ExactTriangleIntersection.Intersects(ref A, ref B);
            return r == Intersecting;
        }

        // vertex ids of two triangles without shared vertices: BeyondShared is then the closed intersection test
        static readonly Index3i SeparateA = new Index3i(0, 1, 2), SeparateB = new Index3i(3, 4, 5);

        /// <summary>
        /// For triangles of a mesh (ia, ib their vertex ids, as in ExactTriangleIntersection.IntersectsBeyondShared):
        /// Separate if they certainly meet at most in their shared vertices or edge, Intersecting if they certainly
        /// intersect beyond them, else Undecided
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static int BeyondShared(Index3i ia, ref Triangle3d A, Index3i ib, ref Triangle3d B)
        {
            if (all_finite(ref A, ref B) == false)
                return Undecided;
            int r = by_projection(ia, ref A, ib, ref B);
            if (r != Undecided)
                return r;
            r = in_axis_plane_of(ia, ref A, ib, ref B);
            return r != Undecided ? r : in_axis_plane_of(ib, ref B, ia, ref A);
        }

        // the projection along the common axis plane, or along the dominant axis of A's normal (finite input)
        [MethodImpl(JitHints.Optimize)]
        static int by_projection(Index3i ia, ref Triangle3d A, Index3i ib, ref Triangle3d B)
        {
            // shared corners: as in ExactTriangleIntersection.IntersectsBeyondShared
            int nShared = 0, sa0 = -1, sb0 = -1, sa1 = -1, sb1 = -1;
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    if (ia[i] == ib[j]) {
                        if (nShared == 0) { sa0 = i; sb0 = j; } else { sa1 = i; sb1 = j; }
                        nShared++;
                    }
            if (nShared >= 3)
                return Undecided;

            int axis = common_axis_plane(ref A, ref B);
            bool planar = axis >= 0;     // both in the plane: the projection decides both ways
            if (planar == false)
                axis = dominant_axis((A.V1 - A.V0).Cross(A.V2 - A.V0));

            if (nShared == 0) {
                Vector2d a0 = drop(A.V0, axis), a1 = drop(A.V1, axis), a2 = drop(A.V2, axis);
                Vector2d b0 = drop(B.V0, axis), b1 = drop(B.V1, axis), b2 = drop(B.V2, axis);
                int oA = ExactPredicates.Orient2d(a0, a1, a2), oB = ExactPredicates.Orient2d(b0, b1, b2);
                if (edge_separates(a0, a1, a2, oA, b0, b1, b2) || edge_separates(b0, b1, b2, oB, a0, a1, a2))
                    return Separate;
                // two non-degenerate triangles in a plane that no edge line separates intersect
                return (planar && oA != 0 && oB != 0) ? Intersecting : Undecided;
            }

            if (nShared == 2) {
                Vector2d a = drop(A[sa0], axis), b = drop(A[sa1], axis);
                Vector2d c = drop(A[3 - sa0 - sa1], axis), d = drop(B[3 - sb0 - sb1], axis);
                int oc = ExactPredicates.Orient2d(a, b, c), od = ExactPredicates.Orient2d(a, b, d);
                if (oc * od < 0)
                    return Separate;
                // in the plane: a fold lying flat
                return (planar && oc * od > 0) ? Intersecting : Undecided;
            }

            // one shared vertex s: corners p-s-q of A and u-s-w of B
            Vector2d s = drop(A[sa0], axis);
            Vector2d p = drop(A[(sa0 + 1) % 3], axis), q = drop(A[(sa0 + 2) % 3], axis);
            Vector2d u = drop(B[(sb0 + 1) % 3], axis), w = drop(B[(sb0 + 2) % 3], axis);
            int ocA = ExactPredicates.Orient2d(s, p, q), ocB = ExactPredicates.Orient2d(s, u, w);
            if (ocA == 0 || ocB == 0)
                return Undecided;
            if (ocA < 0) { var t = p; p = q; q = t; }
            if (ocB < 0) { var t = u; u = w; w = t; }
            // two convex corners (below 180 degrees) at s share more than s iff a side of one lies in the other
            bool overlap = in_corner(s, p, q, u) || in_corner(s, p, q, w) || in_corner(s, u, w, p) || in_corner(s, u, w, q);
            if (overlap == false)
                return Separate;
            return planar ? Intersecting : Undecided;
        }

        // P in an axis plane x_k = h, Q not in it (finite input). In the determinant of Orient3d(p0, p1, p2, x) the
        // column k is h - x_k in every row, and subtracting the first row from the others leaves the projections
        // of P's edge vectors, so
        //     Orient3d(p0, p1, p2, x) = sign(h - x_k) * Orient2d(drop(p0, k), drop(p1, k), drop(p2, k))
        // exactly, with drop keeping the other two coordinates in cyclic order (k+1, k+2): a cyclic order of the
        // columns is an even permutation, so the sign convention is the same for k = 0, 1, 2
        // (AxisPlaneFactorization_SameSignAsOrient3d checks it against Orient3d). Only the partition matters here:
        // with P not degenerate in the projection, the side of a corner of Q is the comparison of its coordinate k with
        // h, exact without a filter (where Orient3d is exactly 0 its exact stage decides slowly: CAD parts, caps and
        // walls in axis planes that touch along a rim). If no two corners of Q lie strictly on opposite sides, Q meets
        // the plane in the hull of its corners on it (none, a point or a segment), and the 2D tests along k decide how
        // that meets P, both ways. Undecided if Q crosses the plane, P is degenerate or the case is not covered.
        [MethodImpl(JitHints.Optimize)]
        static int in_axis_plane_of(Index3i ip, ref Triangle3d P, Index3i iq, ref Triangle3d Q)
        {
            int k = axis_plane(ref P);
            if (k < 0)
                return Undecided;
            double h = P.V0[k];
            int s0 = side(Q.V0[k], h), s1 = side(Q.V1[k], h), s2 = side(Q.V2[k], h);
            bool above = s0 > 0 || s1 > 0 || s2 > 0, below = s0 < 0 || s1 < 0 || s2 < 0;
            if ((above && below) || (above == false && below == false))
                return Undecided;       // Q crosses the plane, or lies in it (the planar case: not reached)
            Vector2d p0 = drop(P.V0, k), p1 = drop(P.V1, k), p2 = drop(P.V2, k);
            int o = ExactPredicates.Orient2d(p0, p1, p2);
            if (o == 0)
                return Undecided;
            int nShared = 0, sp = -1, sq = -1;
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    if (ip[i] == iq[j]) { nShared++; sp = i; sq = j; }
            int nZero = (s0 == 0 ? 1 : 0) + (s1 == 0 ? 1 : 0) + (s2 == 0 ? 1 : 0);
            if (nShared == 0) {
                if (nZero == 0)
                    return Separate;    // Q strictly on one side
                // the corners of Q on the plane (one or two): a point or a segment, against the closed P
                int z0 = s0 == 0 ? 0 : (s1 == 0 ? 1 : 2), z1 = s2 == 0 ? 2 : (s1 == 0 ? 1 : 0);
                Vector2d u = drop(Q[z0], k), w = drop(Q[z1], k);
                if (edge_separates(p0, p1, p2, o, u, w, w))
                    return Separate;
                if (nZero == 2) {
                    // the segment's line with all of P strictly on one side (the separating axes of a segment and
                    // a triangle in 2D: the edge normals of both)
                    int a = ExactPredicates.Orient2d(u, w, p0), b = ExactPredicates.Orient2d(u, w, p1), c = ExactPredicates.Orient2d(u, w, p2);
                    if ((a > 0 && b > 0 && c > 0) || (a < 0 && b < 0 && c < 0))
                        return Separate;
                }
                return Intersecting;
            }
            if (nShared == 2)
                return Separate;        // the far corner of Q is off the plane: Q meets it only in the shared edge
            if (nShared != 1)
                return Undecided;
            // one shared vertex s (on the plane): the other corners u, w of Q
            if (nZero == 1)
                return Separate;        // u and w strictly on one side: Q meets the plane only at s
            // u on the plane, w off it: Q meets the plane in the segment s-u, which meets P beyond s iff it leaves s
            // into P's closed corner at s
            Vector3d u3 = Q[(sq + 1) % 3][k] == h ? Q[(sq + 1) % 3] : Q[(sq + 2) % 3];
            Vector2d s2d = drop(P[sp], k), pp = drop(P[(sp + 1) % 3], k), pq = drop(P[(sp + 2) % 3], k), u2 = drop(u3, k);
            if (u2 == s2d)
                return Undecided;       // a degenerate Q (u at s)
            if (o < 0) { var t = pp; pp = pq; pq = t; }
            return in_corner(s2d, pp, pq, u2) ? Intersecting : Separate;
        }

        // the axis k on which the three corners of T have equal coordinates, else -1
        static int axis_plane(ref Triangle3d T)
        {
            for (int k = 0; k < 3; ++k) {
                double v = T.V0[k];
                if (T.V1[k] == v && T.V2[k] == v)
                    return k;
            }
            return -1;
        }

        static int side(double x, double h)
        {
            return x > h ? 1 : (x < h ? -1 : 0);
        }

        // all three points of the other triangle strictly outside one edge line of the (non-degenerate, orientation
        // o) triangle t0, t1, t2
        [MethodImpl(JitHints.Optimize)]
        static bool edge_separates(Vector2d t0, Vector2d t1, Vector2d t2, int o, Vector2d r0, Vector2d r1, Vector2d r2)
        {
            if (o == 0)
                return false;
            return outside(t0, t1, o, r0, r1, r2) || outside(t1, t2, o, r0, r1, r2) || outside(t2, t0, o, r0, r1, r2);
        }

        [MethodImpl(JitHints.Optimize)]
        static bool outside(Vector2d e0, Vector2d e1, int o, Vector2d r0, Vector2d r1, Vector2d r2)
        {
            return ExactPredicates.Orient2d(e0, e1, r0) * o < 0 && ExactPredicates.Orient2d(e0, e1, r1) * o < 0
                && ExactPredicates.Orient2d(e0, e1, r2) * o < 0;
        }

        // is the direction s->x in the closed (counter-clockwise, below 180 degree) corner p-s-q?
        [MethodImpl(JitHints.Optimize)]
        static bool in_corner(Vector2d s, Vector2d p, Vector2d q, Vector2d x)
        {
            return ExactPredicates.Orient2d(s, p, x) >= 0 && ExactPredicates.Orient2d(s, x, q) >= 0;
        }

        // an axis on which all six coordinates are equal, else -1
        static int common_axis_plane(ref Triangle3d A, ref Triangle3d B)
        {
            for (int k = 0; k < 3; ++k) {
                double v = A.V0[k];
                if (A.V1[k] == v && A.V2[k] == v && B.V0[k] == v && B.V1[k] == v && B.V2[k] == v)
                    return k;
            }
            return -1;
        }

        static bool all_finite(ref Triangle3d A, ref Triangle3d B)
        {
            // (x - x is 0 for finite x, NaN otherwise)
            double z = (A.V0.x - A.V0.x) + (A.V0.y - A.V0.y) + (A.V0.z - A.V0.z) + (A.V1.x - A.V1.x) + (A.V1.y - A.V1.y)
                + (A.V1.z - A.V1.z) + (A.V2.x - A.V2.x) + (A.V2.y - A.V2.y) + (A.V2.z - A.V2.z) + (B.V0.x - B.V0.x)
                + (B.V0.y - B.V0.y) + (B.V0.z - B.V0.z) + (B.V1.x - B.V1.x) + (B.V1.y - B.V1.y) + (B.V1.z - B.V1.z)
                + (B.V2.x - B.V2.x) + (B.V2.y - B.V2.y) + (B.V2.z - B.V2.z);
            return z == 0;
        }

        static int dominant_axis(Vector3d n)
        {
            double x = Math.Abs(n.x), y = Math.Abs(n.y), z = Math.Abs(n.z);
            return (x >= y && x >= z) ? 0 : (y >= z ? 1 : 2);
        }

        // drop coordinate axis
        static Vector2d drop(Vector3d v, int axis)
        {
            return (axis == 0) ? new Vector2d(v.y, v.z) : (axis == 1) ? new Vector2d(v.z, v.x) : new Vector2d(v.x, v.y);
        }
    }
}
