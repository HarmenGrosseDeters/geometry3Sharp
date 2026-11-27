// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;

namespace g3
{
    /// <summary>
    /// The bisector predicates of the restricted Voronoi diagram under a symbolic perturbation of the site weights
    /// (docs/design/BISECTOR_PREDICATES.md section 3; docs/design/CERTIFIED_REMESHING.md section 14.8).
    ///
    /// Every site s (a point with an integer id, eg its Delaunay3 vertex id) gets an infinitesimal weight w_s, and the
    /// distance of x to s is the power distance |x - s|^2 - w_s. A smaller id has the dominant weight: w_i / w_j is
    /// infinitely large for i &lt; j, and every weight is infinitely small against any nonzero value of the input. The
    /// bisectors become the power bisectors (parallel to the plain ones), the cells the power cells (convex), and the
    /// regular triangulation of the weighted sites is a Delaunay triangulation of the plain ones. Each predicate's value
    /// is linear in the weights; where the plain value is 0, the sign of the coefficient of the most dominant weight
    /// with a nonzero coefficient decides. So no predicate returns 0 for a point that exists, and ties go to the site
    /// with the smaller id (the larger weight, so the smaller power distance).
    ///
    /// The sides: -1 where the point is closer (in power distance) to the site a, +1 where it is closer to the tested
    /// site. Sites passed to one call must have distinct ids. Sources: H. Edelsbrunner, E. P. Muecke, "Simulation of
    /// Simplicity", ACM TOG 9(1), 1990; O. Devillers, M. Teillaud, "Perturbations for Delaunay and weighted Delaunay 3D
    /// triangulations", CGTA 44(3), 2011; B. Levy, "Robustness and efficiency of geometric programs: the Predicate
    /// Construction Kit (PCK)", CAD 72, 2016 (papers only).
    /// </summary>
    internal static class ExactBisectorSoS
    {
        /// <summary>
        /// the side of the power bisector of (a, e) at the mesh vertex v: -1 closer to a, +1 closer to e; never 0
        /// </summary>
        internal static int Side1(Vector3d v, Vector3d a, int ia, Vector3d e, int ie)
        {
            check_ids(ia, ie);
            int s = BisectorPredicates.BisectorSide1(v, a, e);
            if (s != 0) return s;
            // f = f0 + w_e - w_a: the dominant of the two weights decides
            return ie < ia ? 1 : -1;
        }

        /// <summary>
        /// the side of the power bisector of (a, c) at the point y where the mesh edge q0 q1 crosses the power bisector
        /// of (a, b): -1 closer to a, +1 closer to c; 0 only if the edge is parallel to the bisectors (then y does not
        /// exist, and an edge whose ends are on two sides of it is never parallel)
        /// </summary>
        internal static int Side2(Vector3d q0, Vector3d q1, Vector3d a, int ia, Vector3d b, int ib, Vector3d c, int ic)
        {
            check_ids(ia, ib, ic);
            int s = BisectorPredicates.BisectorSide2(q0, q1, a, b, c);
            if (s != 0) return s;
            // d = 2 (b - a).(q1 - q0); f = f0 + w_a 2 (c - b).dq - w_b 2 (c - a).dq + w_c d
            int sd = BisectorPredicates.DotSites(a, b, q0, q1);
            if (sd == 0) return 0;
            for (int k = 0; k < 3; ++k) {
                int which = nth_smallest(k, ia, ib, ic);
                int coef = which == 0 ? BisectorPredicates.DotSites(b, c, q0, q1)
                    : which == 1 ? -BisectorPredicates.DotSites(a, c, q0, q1)
                    : sd;
                if (coef != 0) return coef * sd;
            }
            throw new InvalidOperationException("ExactBisectorSoS.Side2: the coefficient of w_c is d != 0");
        }

        /// <summary>
        /// the side of the power bisector of (a, e) at the point x of the power bisectors of (a, b) and (a, c) and the
        /// plane of the triangle t0 t1 t2 (a point of the Voronoi edge of a, b, c): -1 closer to a, +1 closer to e; 0
        /// only if the bisector line is parallel to the plane (then x does not exist)
        /// </summary>
        internal static int Side3(Vector3d a, int ia, Vector3d b, int ib, Vector3d c, int ic,
            Vector3d t0, Vector3d t1, Vector3d t2, Vector3d e, int ie)
        {
            check_ids(ia, ib, ic, ie);
            int s = BisectorPredicates.BisectorSide3(a, b, c, t0, t1, t2, e);
            if (s != 0) return s;
            // with D(p, q) = det(p, q, n): f = f0 - w_a D(b - e, c - e) - w_b D(e - a, c - a) - w_c D(b - a, e - a)
            // + w_e D(b - a, c - a), and det = D(b - a, c - a)
            int sdet = BisectorPredicates.DetSitesNormal(a, b, c, t0, t1, t2);
            if (sdet == 0) return 0;
            for (int k = 0; k < 4; ++k) {
                int which = nth_smallest(k, ia, ib, ic, ie);
                int coef = which == 0 ? -BisectorPredicates.DetSitesNormal(e, b, c, t0, t1, t2)
                    : which == 1 ? -BisectorPredicates.DetSitesNormal(a, e, c, t0, t1, t2)
                    : which == 2 ? -BisectorPredicates.DetSitesNormal(a, b, e, t0, t1, t2)
                    : sdet;
                if (coef != 0) return coef * sdet;
            }
            throw new InvalidOperationException("ExactBisectorSoS.Side3: the coefficient of w_e is det != 0");
        }

        /// <summary>
        /// ExactPredicates.InSphere(a, b, c, d, e) for the weighted sites (the power test of the regular triangulation):
        /// +1 if e is in conflict with the orthosphere of a, b, c, d given positively oriented (Orient3d(a, b, c, d) &gt; 0;
        /// reversed for the negative orientation), -1 if not; 0 only if a, b, c, d are coplanar
        /// </summary>
        internal static int InSphere(Vector3d a, int ia, Vector3d b, int ib, Vector3d c, int ic, Vector3d d, int id,
            Vector3d e, int ie)
        {
            check_ids(ia, ib, ic, id, ie);
            int s = ExactPredicates.InSphere(a, b, c, d, e);
            if (s != 0) return s;
            // the determinant of the rows (p, |p|^2 - w_p, 1) is antisymmetric in its rows and linear in the weights; the
            // coefficient of w_e is Orient3d(a, b, c, d) (a larger weight of e puts it further inside), and row swaps give
            // the others: w_a -Orient3d(e, b, c, d), w_b -Orient3d(a, e, c, d), w_c -Orient3d(a, b, e, d), w_d
            // -Orient3d(a, b, c, e)
            for (int k = 0; k < 5; ++k) {
                int which = nth_smallest(k, ia, ib, ic, id, ie);
                int coef = which == 0 ? -ExactPredicates.Orient3d(e, b, c, d)
                    : which == 1 ? -ExactPredicates.Orient3d(a, e, c, d)
                    : which == 2 ? -ExactPredicates.Orient3d(a, b, e, d)
                    : which == 3 ? -ExactPredicates.Orient3d(a, b, c, e)
                    : ExactPredicates.Orient3d(a, b, c, d);
                if (coef != 0) return coef;
            }
            return 0;
        }

        static void check_ids(int a, int b)
        {
            if (a == b) throw new ArgumentException("ExactBisectorSoS: the sites need distinct ids");
        }

        static void check_ids(int a, int b, int c)
        {
            if (a == b || a == c || b == c) throw new ArgumentException("ExactBisectorSoS: the sites need distinct ids");
        }

        static void check_ids(int a, int b, int c, int d)
        {
            if (a == b || a == c || a == d || b == c || b == d || c == d)
                throw new ArgumentException("ExactBisectorSoS: the sites need distinct ids");
        }

        static void check_ids(int a, int b, int c, int d, int e)
        {
            check_ids(a, b, c, d);
            if (e == a || e == b || e == c || e == d) throw new ArgumentException("ExactBisectorSoS: the sites need distinct ids");
        }

        // the position (0 = first argument) of the k-th smallest of the distinct ids: the k-th most dominant weight
        static int nth_smallest(int k, int i0, int i1, int i2) { return nth_smallest(k, i0, i1, i2, int.MaxValue, int.MaxValue); }

        static int nth_smallest(int k, int i0, int i1, int i2, int i3) { return nth_smallest(k, i0, i1, i2, i3, int.MaxValue); }

        static int nth_smallest(int k, int i0, int i1, int i2, int i3, int i4)
        {
            // the rank of each argument is the number of smaller ids (the ids are distinct; int.MaxValue pads)
            int r0 = (i1 < i0 ? 1 : 0) + (i2 < i0 ? 1 : 0) + (i3 < i0 ? 1 : 0) + (i4 < i0 ? 1 : 0);
            if (r0 == k) return 0;
            int r1 = (i0 < i1 ? 1 : 0) + (i2 < i1 ? 1 : 0) + (i3 < i1 ? 1 : 0) + (i4 < i1 ? 1 : 0);
            if (r1 == k) return 1;
            int r2 = (i0 < i2 ? 1 : 0) + (i1 < i2 ? 1 : 0) + (i3 < i2 ? 1 : 0) + (i4 < i2 ? 1 : 0);
            if (r2 == k) return 2;
            int r3 = (i0 < i3 ? 1 : 0) + (i1 < i3 ? 1 : 0) + (i2 < i3 ? 1 : 0) + (i4 < i3 ? 1 : 0);
            if (r3 == k) return 3;
            return 4;
        }
    }
}
