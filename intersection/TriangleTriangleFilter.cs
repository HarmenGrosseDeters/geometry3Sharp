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
    /// so it never changes a decision of a correct exact test. (ExactTriangleIntersection before the fix of its
    /// coplanar projection axis could report a false intersection for coplanar slivers whose floating-point normal is
    /// 0, or miss a fold lying flat; the filter gives the correct answer there.)
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
        /// For triangles of a mesh (ia, ib their vertex ids, as in ExactTriangleIntersection.IntersectsBeyondShared):
        /// Separate if they certainly meet at most in their shared vertices or edge, Intersecting if they certainly
        /// intersect beyond them, else Undecided
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static int BeyondShared(Index3i ia, ref Triangle3d A, Index3i ib, ref Triangle3d B)
        {
            if (all_finite(ref A, ref B) == false)
                return Undecided;
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
