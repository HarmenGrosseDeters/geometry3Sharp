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

            double u = Math.Min(u0, Math.Min(u1, u2));
            if (PatchBound && !(u <= threshold))
                u = Math.Min(u, patch_bound(p0, p1, p2, n0, n1, n2));
            return u;
        }

        /// <summary>
        /// also try the patch bound (patch_bound): for triangles that span many target triangles of a nearly flat
        /// region (eg a coarse mesh against the fine one it was reduced from). Off by default; MeshHausdorffDistance.PatchBound
        /// </summary>
        internal bool PatchBound = false;
        /// <summary> the patch bound gives up when the patch has more target triangles </summary>
        internal int MaxPatchTriangles = 256;

        // Upper bound from the target triangles under the triangle (p0, p1, p2), seen along its normal: if the
        // projections of a connected set S of target triangles onto the triangle's plane cover the triangle,
        // every point x of the triangle lies straight above (or below) a point z of S, so
        // d(x, Target) <= |x - z| = |height of z|, where the height over the plane is linear on each target
        // triangle: its max over the part of the projection inside the triangle is at a corner of that clipped
        // polygon. S grows from the corners' nearest triangles over shared edges, through target triangles whose
        // projection overlaps the triangle's box and is positively oriented. Coverage: then the projections of
        // two triangles of S across an edge lie on opposite sides of it, so the boundary of their union lies
        // on edges of S whose neighbour is not in S; if none of those meets the triangle, it lies wholly inside
        // the union or wholly outside, and some part of it over S (a clipped polygon) decides. +Infinity if that
        // cannot be shown.
        internal double patch_bound(Vector3d p0, Vector3d p1, Vector3d p2, int n0, int n1, int n2)
        {
            Vector3d e1 = p1 - p0, e2 = p2 - p0;
            Vector3d nrm = e1.Cross(e2);
            double len1 = e1.Length, nlen = nrm.Length;
            double scale = Math.Max(len1, Math.Max(e2.Length, (p2 - p1).Length));
            if (!(nlen > 1e-12 * scale * scale) || !(len1 > 0))
                return double.PositiveInfinity;
            nrm /= nlen;
            Vector3d ux = e1 / len1, uy = nrm.Cross(ux);
            Vector2d q0 = Vector2d.Zero, q1 = new Vector2d(len1, 0), q2 = new Vector2d(e2.Dot(ux), e2.Dot(uy));
            double margin = 1e-9 * scale;
            AxisAlignedBox2d box = new AxisAlignedBox2d(q0); box.Contain(q1); box.Contain(q2);
            box.Expand(margin);

            // state of the target triangles seen: 1 = in S, 2 = rejected
            var state = new Dictionary<int, byte>();
            var inS = new List<int>();
            var stack = new Stack<int>();
            foreach (int s in new int[] { n0, n1, n2 })
                if (s >= 0 && Target.IsTriangle(s) && state.ContainsKey(s) == false) { state[s] = 0; stack.Push(s); }
            double hmax = 0;
            bool hasHeight = false;
            var clipped = new List<Vector3d>(9);
            var tmp = new List<Vector3d>(9);
            while (stack.Count > 0) {
                int tid = stack.Pop();
                Index3i tv = Target.GetTriangle(tid);
                Vector3d a = Target.GetVertex(tv.a) - p0, b = Target.GetVertex(tv.b) - p0, c = Target.GetVertex(tv.c) - p0;
                // (x, y) in the plane, z = height
                Vector3d ta = new Vector3d(a.Dot(ux), a.Dot(uy), a.Dot(nrm)), tb = new Vector3d(b.Dot(ux), b.Dot(uy), b.Dot(nrm)),
                    tc = new Vector3d(c.Dot(ux), c.Dot(uy), c.Dot(nrm));
                AxisAlignedBox2d tbox = new AxisAlignedBox2d(ta.xy); tbox.Contain(tb.xy); tbox.Contain(tc.xy);
                double area2 = (tb.x - ta.x) * (tc.y - ta.y) - (tb.y - ta.y) * (tc.x - ta.x);
                double tscale = Math.Max(tbox.Width, tbox.Height);
                if (tbox.Intersects(box) == false || !(area2 > 1e-9 * tscale * tscale)) {
                    state[tid] = 2;
                    continue;
                }
                state[tid] = 1;
                inS.Add(tid);
                if (inS.Count > MaxPatchTriangles)
                    return double.PositiveInfinity;
                // heights over the part of the projection inside the triangle
                clipped.Clear(); clipped.Add(ta); clipped.Add(tb); clipped.Add(tc);
                clip2(clipped, tmp, q0, q1); clip2(tmp, clipped, q1, q2); clip2(clipped, tmp, q2, q0);
                foreach (Vector3d v in tmp) {
                    hmax = Math.Max(hmax, Math.Abs(v.z));
                    hasHeight = true;
                }
                Index3i nb = Target.GetTriNeighbourTris(tid);
                for (int k = 0; k < 3; ++k)
                    if (nb[k] >= 0 && state.ContainsKey(nb[k]) == false) { state[nb[k]] = 0; stack.Push(nb[k]); }
            }
            // (no part of the triangle over S: not covered)
            if (hasHeight == false)
                return double.PositiveInfinity;
            // no boundary edge of S may meet the triangle
            foreach (int tid in inS) {
                Index3i tv = Target.GetTriangle(tid);
                Index3i nb = Target.GetTriNeighbourTris(tid);
                for (int k = 0; k < 3; ++k) {
                    byte st;
                    // (an edge between triangles of S of inconsistent orientation counts as a boundary edge: both
                    // can lie on the same side of it)
                    if (nb[k] >= 0 && state.TryGetValue(nb[k], out st) && st == 1 && traverses(Target.GetTriangle(nb[k]), tv[(k + 1) % 3], tv[k]))
                        continue;
                    Vector3d a = Target.GetVertex(tv[k]) - p0, b = Target.GetVertex(tv[(k + 1) % 3]) - p0;
                    Vector2d sa = new Vector2d(a.Dot(ux), a.Dot(uy)), sb = new Vector2d(b.Dot(ux), b.Dot(uy));
                    if (segment_meets_triangle(sa, sb, q0, q1, q2, margin))
                        return double.PositiveInfinity;
                }
            }
            return hmax;
        }

        // does the triangle t have the edge a -> b in its order?
        static bool traverses(Index3i t, int a, int b)
        {
            return (t.a == a && t.b == b) || (t.b == a && t.c == b) || (t.c == a && t.a == b);
        }

        // doubled signed area of (a, b, p): positive if p is left of a -> b
        static double edge_side(Vector2d a, Vector2d b, Vector2d p)
        {
            return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        }

        // clip the polygon (x, y, height) to the left side of the line a -> b (Sutherland-Hodgman); the height is
        // interpolated linearly
        static void clip2(List<Vector3d> poly, List<Vector3d> outPoly, Vector2d a, Vector2d b)
        {
            outPoly.Clear();
            int n = poly.Count;
            for (int k = 0; k < n; ++k) {
                Vector3d p = poly[k], r = poly[(k + 1) % n];
                double dp = edge_side(a, b, p.xy), dr = edge_side(a, b, r.xy);
                if (dp >= 0) outPoly.Add(p);
                if ((dp > 0 && dr < 0) || (dp < 0 && dr > 0))
                    outPoly.Add(p + (dp / (dp - dr)) * (r - p));
            }
        }

        // does the segment (a, b) come within margin of the closed triangle (q0, q1, q2) (counter-clockwise)?
        static bool segment_meets_triangle(Vector2d a, Vector2d b, Vector2d q0, Vector2d q1, Vector2d q2, double margin)
        {
            // an end point inside, or the segment within margin of an edge
            if (edge_side(q0, q1, a) >= 0 && edge_side(q1, q2, a) >= 0 && edge_side(q2, q0, a) >= 0) return true;
            return segment_distance(a, b, q0, q1) <= margin || segment_distance(a, b, q1, q2) <= margin
                || segment_distance(a, b, q2, q0) <= margin;
        }

        static double segment_distance(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            double s1 = edge_side(a, b, c), s2 = edge_side(a, b, d), s3 = edge_side(c, d, a), s4 = edge_side(c, d, b);
            if (((s1 > 0 && s2 < 0) || (s1 < 0 && s2 > 0)) && ((s3 > 0 && s4 < 0) || (s3 < 0 && s4 > 0)))
                return 0;
            return Math.Min(Math.Min(point_segment(c, a, b), point_segment(d, a, b)), Math.Min(point_segment(a, c, d), point_segment(b, c, d)));
        }

        static double point_segment(Vector2d p, Vector2d a, Vector2d b)
        {
            Vector2d ab = b - a;
            double l2 = ab.LengthSquared;
            double t = (l2 > 0) ? MathUtil.Clamp((p - a).Dot(ab) / l2, 0, 1) : 0;
            return p.Distance(a + t * ab);
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
            return IsWithin(spatial, a, b, c, eps, maxDepth, null);
        }

        /// <summary>
        /// Nearest-triangle results within one distance (MaxDist) in one tree, shared by the queries of several
        /// IsWithin calls and their callers' samples, eg the triangles of one Reducer collapse: they share corners and
        /// edge midpoints. Thread-safe; a result is the one the tree search gives for the same point, distance and
        /// tree, so the answers are the same as without the cache (a cache of another distance or tree is not used).
        /// </summary>
        internal sealed class QueryCache
        {
            internal readonly double MaxDist;
            internal readonly DMeshAABBTree3 Tree;
            internal readonly System.Collections.Concurrent.ConcurrentDictionary<Vector3d, (double d, int tid)> Results =
                new System.Collections.Concurrent.ConcurrentDictionary<Vector3d, (double d, int tid)>(4, 64);
            /// <summary> (tests) lookups and hits, and the hits of IsWithin calls </summary>
            internal int Lookups, Hits, IsWithinHits;
            internal QueryCache(DMeshAABBTree3 tree, double maxDist) { Tree = tree; MaxDist = maxDist; }

            /// <summary> stores the result of a tree search the caller made (FindNearestTriangle(p, out distSqr, MaxDist)) </summary>
            internal void Put(Vector3d p, int tid, double distSqr)
            {
                System.Threading.Interlocked.Increment(ref Lookups);
                Results.TryAdd(p, (distance(tid, distSqr), tid));
            }
        }

        // the distance as query_tree reports it
        static double distance(int tid, double distSqr) { return (tid >= 0) ? Math.Sqrt(distSqr) : double.MaxValue; }

        /// <summary> the nearest triangle of p within cache.MaxDist (-1 if none) and its distance (MaxValue if none) </summary>
        internal int NearestWithin(DMeshAABBTree3 spatial, Vector3d p, QueryCache cache, out double d)
        {
            int tid;
            query(spatial, p, cache.MaxDist, out d, out tid, cache.Tree == spatial ? cache : null);
            return tid;
        }

        /// <summary> IsWithin with the tree queries shared through cache (null: none; a cache of another distance is not used) </summary>
        internal bool IsWithin(DMeshAABBTree3 spatial, Vector3d a, Vector3d b, Vector3d c, double eps, int maxDepth, QueryCache cache)
        {
            if (cache != null && (cache.MaxDist != eps || cache.Tree != spatial)) cache = null;
            Stack<Item> stack = new Stack<Item>();
            Item root = new Item() { P0 = a, P1 = b, P2 = c, Depth = 0, OnEdge = 7 };
            if (query(spatial, a, eps, out root.D0, out root.N0, cache, true) > eps) return false;
            if (query(spatial, b, eps, out root.D1, out root.N1, cache, true) > eps) return false;
            if (query(spatial, c, eps, out root.D2, out root.N2, cache, true) > eps) return false;
            stack.Push(root);
            while (stack.Count > 0) {
                Item t = stack.Pop();
                if (UpperBound(t.P0, t.P1, t.P2, t.D0, t.D1, t.D2, t.N0, t.N1, t.N2, eps) <= eps)
                    continue;
                if (t.Depth >= maxDepth)
                    return false;
                Vector3d m01 = 0.5 * (t.P0 + t.P1), m12 = 0.5 * (t.P1 + t.P2), m20 = 0.5 * (t.P2 + t.P0);
                double d01, d12, d20; int n01, n12, n20;
                int e = t.OnEdge;
                if (query(spatial, m01, eps, out d01, out n01, (e & 1) != 0 ? cache : null, true) > eps) return false;
                if (query(spatial, m12, eps, out d12, out n12, (e & 2) != 0 ? cache : null, true) > eps) return false;
                if (query(spatial, m20, eps, out d20, out n20, (e & 4) != 0 ? cache : null, true) > eps) return false;
                int depth = t.Depth + 1;
                stack.Push(new Item() { P0 = t.P0, P1 = m01, P2 = m20, D0 = t.D0, D1 = d01, D2 = d20, N0 = t.N0, N1 = n01, N2 = n20, Depth = depth, OnEdge = e & 5 });
                stack.Push(new Item() { P0 = m01, P1 = t.P1, P2 = m12, D0 = d01, D1 = t.D1, D2 = d12, N0 = n01, N1 = t.N1, N2 = n12, Depth = depth, OnEdge = e & 3 });
                stack.Push(new Item() { P0 = m20, P1 = m12, P2 = t.P2, D0 = d20, D1 = d12, D2 = t.D2, N0 = n20, N1 = n12, N2 = t.N2, Depth = depth, OnEdge = e & 6 });
                stack.Push(new Item() { P0 = m01, P1 = m12, P2 = m20, D0 = d01, D1 = d12, D2 = d20, N0 = n01, N1 = n12, N2 = n20, Depth = depth, OnEdge = 0 });
            }
            return true;
        }

        struct Item
        {
            public Vector3d P0, P1, P2;
            public double D0, D1, D2;
            public int N0, N1, N2;
            public int Depth;
            // (IsWithin with a cache) which edges lie on the original triangle's edges: 1 = P0P1, 2 = P1P2, 4 = P2P0.
            // Only points there can be shared with another triangle; interior points skip the cache
            public int OnEdge;
        }

        // nearest triangle within maxDist (the tree search prunes everything farther); MaxValue if none
        double query(DMeshAABBTree3 spatial, Vector3d p, double maxDist, out double d, out int tid)
        {
            return query(spatial, p, maxDist, out d, out tid, null);
        }

        double query(DMeshAABBTree3 spatial, Vector3d p, double maxDist, out double d, out int tid, QueryCache cache, bool fromIsWithin = false)
        {
            if (cache != null) {
                (double d, int tid) r;
                System.Threading.Interlocked.Increment(ref cache.Lookups);
                if (cache.Results.TryGetValue(p, out r)) {
                    System.Threading.Interlocked.Increment(ref cache.Hits);
                    if (fromIsWithin) System.Threading.Interlocked.Increment(ref cache.IsWithinHits);
                    d = r.d; tid = r.tid; return d;
                }
                query_tree(spatial, p, maxDist, out d, out tid);
                cache.Results.TryAdd(p, (d, tid));
                return d;
            }
            return query_tree(spatial, p, maxDist, out d, out tid);
        }

        double query_tree(DMeshAABBTree3 spatial, Vector3d p, double maxDist, out double d, out int tid)
        {
            double distSqr;
            tid = spatial.FindNearestTriangle(p, out distSqr, maxDist);
            d = distance(tid, distSqr);
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
