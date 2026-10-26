// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Upper bounds of the distance from the points of a triangle to a target mesh, given the distances
    /// of its corners and their nearest target triangles (Sacht and Jacobson, "Cascading upper bounds for
    /// triangle soup Pompeiu-Hausdorff distance", SGP 2024, with plane splits for adjacent target
    /// triangles). Used by MeshHausdorffDistance, and by IsWithin() to decide whether a triangle lies
    /// within a tolerance of the target (eg in the Reducer).
    /// </summary>
    public class TriangleDeviationBounds
    {
        public DMesh3 Target;

        public TriangleDeviationBounds(DMesh3 target)
        {
            Target = target;
        }

        /// <summary>
        /// upper bound of the distance to Target over the triangle (p0, p1, p2); d_i are the corner distances
        /// and n_i their nearest target triangles. Refinement stops early once the bound is below threshold.
        /// </summary>
        public double UpperBound(Vector3d p0, Vector3d p1, Vector3d p2, double d0, double d1, double d2,
            int n0, int n1, int n2, double threshold)
        {
            double dmax = Math.Max(d0, Math.Max(d1, d2));
            // the distance to a single target triangle is convex: its max over the triangle is at a corner
            if (n0 == n1 && n1 == n2)
                return dmax;
            // ... also on any convex piece of the triangle. Try single nearest triangles, and splits of the
            // triangle by a plane between two of them (each side bounded by its own triangle)
            Corners corners = new Corners() { P0 = p0, P1 = p1, P2 = p2, D0 = d0, D1 = d1, D2 = d2, N0 = n0, N1 = n1, N2 = n2 };
            List<int> cand = new List<int>(3) { n0 };
            if (n1 != n0) cand.Add(n1);
            if (n2 != n0 && n2 != n1) cand.Add(n2);
            List<Vector3d> poly = new List<Vector3d>(3) { p0, p1, p2 };
            double u0 = polygon_bound(poly, cand, 0, ref corners, threshold);

            // triangle inequality from a corner
            double e01 = p0.Distance(p1), e12 = p1.Distance(p2), e20 = p2.Distance(p0);
            double u1 = Math.Min(d0 + Math.Max(e01, e20), Math.Min(d1 + Math.Max(e01, e12), d2 + Math.Max(e12, e20)));

            // every point is within the covering radius of some corner
            double u2 = dmax + covering_radius(e01, e12, e20);

            return Math.Min(u0, Math.Min(u1, u2));
        }


        /// <summary>
        /// Certified interval [lower, upper] of the largest distance to Target over the triangle (a, b, c):
        /// lower is the distance of a point of the triangle (a corner or a subdivision point, returned in
        /// worst), and no point of the triangle is farther than upper. Branch and bound as in
        /// MeshHausdorffDistance, restricted to this triangle: the piece with the largest upper bound is split
        /// into 4 until upper - lower &lt;= tol or after maxSubdivisions splits (then false, with a wider but
        /// still valid interval). d_i and n_i are the corner distances and nearest triangles in spatial, whose
        /// mesh must be Target. Sequential and deterministic, so it can run in parallel over triangles.
        /// Non-finite corners give NaN. A piece whose bound is NaN (rounding in a degenerate piece) counts as
        /// unbounded, so upper can be +Infinity if it cannot be resolved within the budget.
        /// </summary>
        public bool MaxDistance(DMeshAABBTree3 spatial, Vector3d a, Vector3d b, Vector3d c, double da, double db, double dc,
            int na, int nb, int nc, double tol, int maxSubdivisions, out double lower, out double upper, out Vector3d worst)
        {
            if (spatial.Mesh != Target)
                throw new ArgumentException("TriangleDeviationBounds.MaxDistance: spatial is not built on Target");
            worst = a;
            lower = upper = double.NaN;
            if (!(finite(a) && finite(b) && finite(c)) || double.IsNaN(da + db + dc) || double.IsInfinity(da + db + dc))
                return false;
            lower = da;
            if (db > lower) { lower = db; worst = b; }
            if (dc > lower) { lower = dc; worst = c; }

            var heap = new DeviationMaxHeap<Item>();
            Item root = new Item() { P0 = a, P1 = b, P2 = c, D0 = da, D1 = db, D2 = dc, N0 = na, N1 = nb, N2 = nc };
            push(heap, root, lower);
            int splits = 0;
            while (heap.Count > 0 && heap.TopKey > lower + tol && splits < maxSubdivisions) {
                Item t = heap.Pop();
                splits++;
                Vector3d m01 = 0.5 * (t.P0 + t.P1), m12 = 0.5 * (t.P1 + t.P2), m20 = 0.5 * (t.P2 + t.P0);
                double d01, d12, d20; int n01, n12, n20;
                query(spatial, m01, double.MaxValue, out d01, out n01);
                query(spatial, m12, double.MaxValue, out d12, out n12);
                query(spatial, m20, double.MaxValue, out d20, out n20);
                if (d01 > lower) { lower = d01; worst = m01; }
                if (d12 > lower) { lower = d12; worst = m12; }
                if (d20 > lower) { lower = d20; worst = m20; }
                push(heap, new Item() { P0 = t.P0, P1 = m01, P2 = m20, D0 = t.D0, D1 = d01, D2 = d20, N0 = t.N0, N1 = n01, N2 = n20 }, lower);
                push(heap, new Item() { P0 = m01, P1 = t.P1, P2 = m12, D0 = d01, D1 = t.D1, D2 = d12, N0 = n01, N1 = t.N1, N2 = n12 }, lower);
                push(heap, new Item() { P0 = m20, P1 = m12, P2 = t.P2, D0 = d20, D1 = d12, D2 = t.D2, N0 = n20, N1 = n12, N2 = t.N2 }, lower);
                push(heap, new Item() { P0 = m01, P1 = m12, P2 = m20, D0 = d01, D1 = d12, D2 = d20, N0 = n01, N1 = n12, N2 = n20 }, lower);
            }
            // pieces dropped or still in the heap below lower cannot exceed it (lower only grows)
            upper = Math.Max(lower, (heap.Count > 0) ? heap.TopKey : 0);
            // the same test as the loop, so that a stop by it always counts as converged
            return !(upper > lower + tol);
        }

        // push a piece unless its upper bound shows that it cannot exceed lower
        void push(DeviationMaxHeap<Item> heap, Item t, double lower)
        {
            double ub = UpperBound(t.P0, t.P1, t.P2, t.D0, t.D1, t.D2, t.N0, t.N1, t.N2, lower);
            if (!(ub <= lower))
                heap.Push(double.IsNaN(ub) ? double.PositiveInfinity : ub, t);
        }

        static bool finite(Vector3d v)
        {
            double s = v.x + v.y + v.z;
            return !(double.IsNaN(s) || double.IsInfinity(s));
        }


        /// <summary>
        /// Is every point of the triangle (a, b, c) within eps of Target? Conservative: answers false if
        /// that cannot be shown with maxDepth levels of subdivision.
        /// </summary>
        public bool IsWithin(DMeshAABBTree3 spatial, Vector3d a, Vector3d b, Vector3d c, double eps, int maxDepth = 10)
        {
            Stack<Item> stack = new Stack<Item>();
            Item root = new Item() { P0 = a, P1 = b, P2 = c, Depth = 0 };
            if (query(spatial, a, eps, out root.D0, out root.N0) > eps) return false;
            if (query(spatial, b, eps, out root.D1, out root.N1) > eps) return false;
            if (query(spatial, c, eps, out root.D2, out root.N2) > eps) return false;
            stack.Push(root);
            while (stack.Count > 0) {
                Item t = stack.Pop();
                if (UpperBound(t.P0, t.P1, t.P2, t.D0, t.D1, t.D2, t.N0, t.N1, t.N2, eps) <= eps)
                    continue;
                if (t.Depth >= maxDepth)
                    return false;
                Vector3d m01 = 0.5 * (t.P0 + t.P1), m12 = 0.5 * (t.P1 + t.P2), m20 = 0.5 * (t.P2 + t.P0);
                double d01, d12, d20; int n01, n12, n20;
                if (query(spatial, m01, eps, out d01, out n01) > eps) return false;
                if (query(spatial, m12, eps, out d12, out n12) > eps) return false;
                if (query(spatial, m20, eps, out d20, out n20) > eps) return false;
                int depth = t.Depth + 1;
                stack.Push(new Item() { P0 = t.P0, P1 = m01, P2 = m20, D0 = t.D0, D1 = d01, D2 = d20, N0 = t.N0, N1 = n01, N2 = n20, Depth = depth });
                stack.Push(new Item() { P0 = m01, P1 = t.P1, P2 = m12, D0 = d01, D1 = t.D1, D2 = d12, N0 = n01, N1 = t.N1, N2 = n12, Depth = depth });
                stack.Push(new Item() { P0 = m20, P1 = m12, P2 = t.P2, D0 = d20, D1 = d12, D2 = t.D2, N0 = n20, N1 = n12, N2 = t.N2, Depth = depth });
                stack.Push(new Item() { P0 = m01, P1 = m12, P2 = m20, D0 = d01, D1 = d12, D2 = d20, N0 = n01, N1 = n12, N2 = n20, Depth = depth });
            }
            return true;
        }

        struct Item
        {
            public Vector3d P0, P1, P2;
            public double D0, D1, D2;
            public int N0, N1, N2;
            public int Depth;
        }

        // nearest triangle within maxDist (the tree search prunes everything farther); MaxValue if none
        double query(DMeshAABBTree3 spatial, Vector3d p, double maxDist, out double d, out int tid)
        {
            double distSqr;
            tid = spatial.FindNearestTriangle(p, out distSqr, maxDist);
            d = (tid >= 0) ? Math.Sqrt(distSqr) : double.MaxValue;
            return d;
        }


        struct Corners
        {
            public Vector3d P0, P1, P2;
            public double D0, D1, D2;
            public int N0, N1, N2;
        }

        // upper bound of the distance over a convex polygon (a piece of the triangle), using only the
        // candidate triangles: the best single candidate, or a split by a plane between two candidates
        double polygon_bound(List<Vector3d> poly, List<int> cand, int depth, ref Corners corners, double threshold)
        {
            double best = double.MaxValue;
            foreach (int tid in cand)
                best = Math.Min(best, max_distance(poly, tid, ref corners));
            if (cand.Count < 2 || depth >= 2 || best <= threshold)
                return best;
            for (int i = 0; i < cand.Count; ++i)
                for (int j = i + 1; j < cand.Count; ++j) {
                    Vector3d origin, normal;
                    if (separating_plane(cand[i], cand[j], out origin, out normal) == false)
                        continue;
                    List<Vector3d> pi, pj;
                    clip(poly, origin, normal, out pi, out pj);
                    List<int> ci = new List<int>(), cj = new List<int>();
                    foreach (int c in cand)
                        ((c == cand[i] || (c != cand[j] && normal.Dot(Target.GetTriCentroid(c) - origin) >= 0)) ? ci : cj).Add(c);
                    double bi = (pi.Count >= 3) ? polygon_bound(pi, ci, depth + 1, ref corners, threshold) : 0;
                    if (bi >= best) continue;
                    double bj = (pj.Count >= 3) ? polygon_bound(pj, cj, depth + 1, ref corners, threshold) : 0;
                    best = Math.Min(best, Math.Max(bi, bj));
                }
            return best;
        }

        // max over the polygon corners of the distance to triangle tid (known corner distances are reused)
        double max_distance(List<Vector3d> poly, int tid, ref Corners k)
        {
            double m = 0;
            foreach (Vector3d p in poly) {
                double d;
                if (p == k.P0 && k.N0 == tid) d = k.D0;
                else if (p == k.P1 && k.N1 == tid) d = k.D1;
                else if (p == k.P2 && k.N2 == tid) d = k.D2;
                else d = Math.Sqrt(MeshQueries.TriDistanceSqr(Target, tid, p));
                m = Math.Max(m, d);
            }
            return m;
        }

        // plane between target triangles ti and tj, normal towards ti. Adjacent: the plane through the shared
        // edge that bisects the angle between them (points on ti's side are nearer to ti); otherwise the
        // bisector of their centroids. Any plane gives a valid bound, this one gives a tight one.
        bool separating_plane(int ti, int tj, out Vector3d origin, out Vector3d normal)
        {
            Index3i a = Target.GetTriangle(ti), b = Target.GetTriangle(tj);
            int s0 = -1, s1 = -1, oi = -1, oj = -1;
            for (int k = 0; k < 3; ++k) {
                if (a[k] == b.a || a[k] == b.b || a[k] == b.c) { if (s0 < 0) s0 = a[k]; else s1 = a[k]; } else oi = a[k];
                if (b[k] != a.a && b[k] != a.b && b[k] != a.c) oj = b[k];
            }
            if (s1 >= 0 && oi >= 0 && oj >= 0) {
                Vector3d e0 = Target.GetVertex(s0), e = (Target.GetVertex(s1) - e0).Normalized;
                Vector3d wi = Target.GetVertex(oi) - e0, wj = Target.GetVertex(oj) - e0;
                wi = (wi - wi.Dot(e) * e).Normalized; wj = (wj - wj.Dot(e) * e).Normalized;
                normal = wi - wj;
                origin = e0;
                if (normal.Length > 1e-6) {
                    normal.Normalize();
                    return true;
                }
            }
            Vector3d ci = Target.GetTriCentroid(ti), cj = Target.GetTriCentroid(tj);
            normal = ci - cj;
            origin = 0.5 * (ci + cj);
            if (normal.Length < 1e-12)
                return false;
            normal.Normalize();
            return true;
        }

        // split a convex polygon by the plane: positive side and negative side (with the cut points)
        static void clip(List<Vector3d> poly, Vector3d origin, Vector3d normal, out List<Vector3d> pos, out List<Vector3d> neg)
        {
            pos = new List<Vector3d>(); neg = new List<Vector3d>();
            int n = poly.Count;
            for (int k = 0; k < n; ++k) {
                Vector3d a = poly[k], b = poly[(k + 1) % n];
                double da = normal.Dot(a - origin), db = normal.Dot(b - origin);
                if (da >= 0) pos.Add(a);
                if (da <= 0) neg.Add(a);
                if ((da > 0 && db < 0) || (da < 0 && db > 0)) {
                    Vector3d x = a + (da / (da - db)) * (b - a);
                    pos.Add(x); neg.Add(x);
                }
            }
        }

        // largest distance from a point of the triangle to its nearest corner: the circumradius for an
        // acute triangle, otherwise half the longest edge
        static double covering_radius(double a, double b, double c)
        {
            double l = Math.Max(a, Math.Max(b, c));
            double a2 = a * a, b2 = b * b, c2 = c * c, l2 = l * l;
            bool acute = (a2 + b2 + c2 - l2) > l2;
            if (acute) {
                double s = 0.5 * (a + b + c);
                double area2 = s * (s - a) * (s - b) * (s - c);
                if (area2 > 0) {
                    double R = a * b * c / (4 * Math.Sqrt(area2));
                    return Math.Max(R, 0.5 * l);
                }
            }
            return 0.5 * l;
        }
    }


    /// <summary> binary max-heap of (key, item); ties pop in a fixed order, so the use is deterministic </summary>
    internal class DeviationMaxHeap<T>
    {
        List<double> keys = new List<double>();
        List<T> items = new List<T>();
        public int Count { get { return keys.Count; } }
        public double TopKey { get { return keys[0]; } }

        public void Push(double key, T item)
        {
            keys.Add(key); items.Add(item);
            int i = keys.Count - 1;
            while (i > 0) {
                int p = (i - 1) / 2;
                if (keys[p] >= keys[i]) break;
                swap(i, p); i = p;
            }
        }

        public T Pop()
        {
            T top = items[0];
            int last = keys.Count - 1;
            swap(0, last);
            keys.RemoveAt(last); items.RemoveAt(last);
            int i = 0, n = keys.Count;
            while (true) {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < n && keys[l] > keys[m]) m = l;
                if (r < n && keys[r] > keys[m]) m = r;
                if (m == i) break;
                swap(i, m); i = m;
            }
            return top;
        }

        void swap(int i, int j)
        {
            double k = keys[i]; keys[i] = keys[j]; keys[j] = k;
            T t = items[i]; items[i] = items[j]; items[j] = t;
        }
    }
}
