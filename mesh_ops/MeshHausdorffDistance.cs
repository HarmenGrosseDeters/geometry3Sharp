// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Certified (Pompeiu-)Hausdorff distance between two triangle meshes (or soups): computes an interval
    /// [LowerBound, UpperBound] that contains the exact distance, with UpperBound - LowerBound below the
    /// tolerance. Sampling vertices or edge points only gives a lower bound, which can be far too small
    /// when the farthest point lies inside a triangle (eg over a hole or dip of the other mesh).
    ///
    /// Branch and bound after Sacht and Jacobson, "Cascading upper bounds for triangle soup
    /// Pompeiu-Hausdorff distance" (SGP 2024):
    ///   - lower bound: distances of points of A (vertices, then subdivision points) to B;
    ///   - upper bound per triangle of A, the smallest of
    ///       * for any triangle t of B, max over the corners of d(corner, t) (the distance to one triangle
    ///         is convex, so its maximum over a triangle is at a corner). Tried for the nearest triangles
    ///         of the corners, it is exact when all corners project to the same triangle;
    ///       * d(corner) + longest edge at that corner (triangle inequality);
    ///       * max d(corner) + covering radius (every point is that close to some corner);
    ///   - the triangle with the largest upper bound is split into 4 until the largest upper bound is
    ///     within the tolerance of the lower bound; triangles whose upper bound is below the lower bound
    ///     are dropped.
    /// The symmetric distance is the max of both directions; the second direction is pruned against the
    /// first one's lower bound.
    /// </summary>
    public class MeshHausdorffDistance
    {
        public DMesh3 MeshA, MeshB;
        /// <summary> optional spatial data structures (built if null) </summary>
        public DMeshAABBTree3 SpatialA, SpatialB;

        /// <summary> symmetric distance max(h(A,B), h(B,A)); false = one-sided h(A,B) (A to B) </summary>
        public bool Symmetric = true;
        /// <summary> absolute tolerance for UpperBound - LowerBound; if 0, RelativeTolerance * bounding box diagonal </summary>
        public double Tolerance = 0;
        public double RelativeTolerance = 1e-4;
        /// <summary> stop (Converged = false) after this many triangle subdivisions </summary>
        public int MaxSubdivisions = 20000000;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        public double LowerBound, UpperBound;
        public bool Converged;
        /// <summary> point where the lower bound is attained (on A, or on B if WorstPointOnB), and its closest point on the other mesh </summary>
        public Vector3d WorstPoint, WorstPointClosest;
        public bool WorstPointOnB;
        public int Subdivisions;


        public MeshHausdorffDistance(DMesh3 a, DMesh3 b)
        {
            MeshA = a; MeshB = b;
        }


        public bool Compute()
        {
            if (MeshA.TriangleCount == 0 || MeshB.TriangleCount == 0)
                return false;
            if (SpatialA == null && Symmetric) SpatialA = new DMeshAABBTree3(MeshA, true);
            if (SpatialB == null) SpatialB = new DMeshAABBTree3(MeshB, true);
            AxisAlignedBox3d bounds = MeshA.CachedBounds;
            bounds.Contain(MeshB.CachedBounds);
            double tol = (Tolerance > 0) ? Tolerance : RelativeTolerance * bounds.DiagonalLength;

            Subdivisions = 0;
            var ab = new OneSided(MeshA, SpatialB, tol, 0, this);
            ab.Run();
            LowerBound = ab.Lower; UpperBound = ab.Upper; Converged = ab.Converged;
            WorstPoint = ab.Worst; WorstPointClosest = ab.WorstClosest; WorstPointOnB = false;
            if (Symmetric && CancelF() == false) {
                var ba = new OneSided(MeshB, SpatialA, tol, ab.Lower, this);
                ba.Run();
                Converged = Converged && ba.Converged;
                if (ba.Lower > LowerBound) {
                    LowerBound = ba.Lower;
                    WorstPoint = ba.Worst; WorstPointClosest = ba.WorstClosest; WorstPointOnB = true;
                }
                UpperBound = Math.Max(UpperBound, ba.Upper);
            }
            UpperBound = Math.Max(UpperBound, LowerBound);
            return CancelF() == false;
        }


        // one-sided h(From, To), pruned against an external lower bound (for the symmetric distance)
        class OneSided
        {
            DMesh3 from, to;
            DMeshAABBTree3 spatial;
            double tol, prune;
            MeshHausdorffDistance owner;

            // points of From (input vertices, then subdivision points): position, distance to To, nearest triangle
            List<Vector3d> pos = new List<Vector3d>();
            List<double> dist = new List<double>();
            List<int> near = new List<int>();
            List<Vector3d> closest = new List<Vector3d>();

            public double Lower, Upper;
            public bool Converged;
            public Vector3d Worst, WorstClosest;

            public OneSided(DMesh3 from, DMeshAABBTree3 spatialTo, double tol, double prune, MeshHausdorffDistance owner)
            {
                this.from = from; this.to = spatialTo.Mesh; spatial = spatialTo;
                this.tol = tol; this.prune = prune; this.owner = owner;
            }

            void query(Vector3d p, out double d, out int t, out Vector3d c)
            {
                t = spatial.FindNearestTriangle(p);
                DistPoint3Triangle3 q = MeshQueries.TriangleDistance(to, t, p);
                d = Math.Sqrt(q.DistanceSquared);
                c = q.TriangleClosest;
            }

            int add_point(Vector3d p)
            {
                double d; int t; Vector3d c;
                query(p, out d, out t, out c);
                return add_point(p, d, t, c);
            }
            int add_point(Vector3d p, double d, int t, Vector3d c)
            {
                pos.Add(p); dist.Add(d); near.Add(t); closest.Add(c);
                int i = pos.Count - 1;
                if (d > Lower) {
                    Lower = d; Worst = p; WorstClosest = c;
                }
                return i;
            }

            public void Run()
            {
                // input vertices (in parallel)
                int[] vmap = new int[from.MaxVertexID];
                int nv = 0;
                foreach (int vid in from.VertexIndices())
                    vmap[vid] = nv++;
                Vector3d[] vp = new Vector3d[nv];
                double[] vd = new double[nv];
                int[] vt = new int[nv];
                Vector3d[] vc = new Vector3d[nv];
                foreach (int vid in from.VertexIndices())
                    vp[vmap[vid]] = from.GetVertex(vid);
                gParallel.ForEach(Interval1i.Range(nv), (i) => query(vp[i], out vd[i], out vt[i], out vc[i]));
                Lower = 0;
                Worst = (nv > 0) ? vp[0] : Vector3d.Zero;
                WorstClosest = (nv > 0) ? vc[0] : Vector3d.Zero;
                for (int i = 0; i < nv; ++i)
                    add_point(vp[i], vd[i], vt[i], vc[i]);

                // initial upper bounds (in parallel)
                List<Index3i> tris = new List<Index3i>();
                foreach (int tid in from.TriangleIndices()) {
                    Index3i t = from.GetTriangle(tid);
                    tris.Add(new Index3i(vmap[t.a], vmap[t.b], vmap[t.c]));
                }
                double[] ub = new double[tris.Count];
                gParallel.ForEach(Interval1i.Range(tris.Count), (k) => ub[k] = upper_bound(tris[k]));

                MaxHeap heap = new MaxHeap();
                for (int k = 0; k < tris.Count; ++k)
                    if (ub[k] > threshold())
                        heap.Push(ub[k], tris[k]);

                // refine the triangles with the largest upper bounds, in batches: the distance queries of
                // the new points and the bounds of the children are computed in parallel
                Converged = true;
                const int BatchSize = 256;
                List<Index3i> batch = new List<Index3i>(BatchSize);
                while (heap.Count > 0 && heap.TopKey > threshold() + tol) {
                    if (owner.Subdivisions >= owner.MaxSubdivisions || owner.CancelF()) {
                        Converged = false;
                        break;
                    }
                    batch.Clear();
                    while (batch.Count < BatchSize && heap.Count > 0 && heap.TopKey > threshold() + tol)
                        batch.Add(heap.Pop());
                    owner.Subdivisions += batch.Count;

                    int nb = batch.Count;
                    Vector3d[] mp = new Vector3d[3 * nb], mc = new Vector3d[3 * nb];
                    double[] md = new double[3 * nb];
                    int[] mt = new int[3 * nb];
                    for (int b = 0; b < nb; ++b) {
                        Index3i t = batch[b];
                        mp[3 * b] = 0.5 * (pos[t.a] + pos[t.b]);
                        mp[3 * b + 1] = 0.5 * (pos[t.b] + pos[t.c]);
                        mp[3 * b + 2] = 0.5 * (pos[t.c] + pos[t.a]);
                    }
                    gParallel.ForEach(Interval1i.Range(3 * nb), (k) => query(mp[k], out md[k], out mt[k], out mc[k]));

                    Index3i[] children = new Index3i[4 * nb];
                    for (int b = 0; b < nb; ++b) {
                        Index3i t = batch[b];
                        int m01 = add_point(mp[3 * b], md[3 * b], mt[3 * b], mc[3 * b]);
                        int m12 = add_point(mp[3 * b + 1], md[3 * b + 1], mt[3 * b + 1], mc[3 * b + 1]);
                        int m20 = add_point(mp[3 * b + 2], md[3 * b + 2], mt[3 * b + 2], mc[3 * b + 2]);
                        children[4 * b] = new Index3i(t.a, m01, m20);
                        children[4 * b + 1] = new Index3i(m01, t.b, m12);
                        children[4 * b + 2] = new Index3i(m20, m12, t.c);
                        children[4 * b + 3] = new Index3i(m01, m12, m20);
                    }
                    double[] cu = new double[4 * nb];
                    gParallel.ForEach(Interval1i.Range(4 * nb), (k) => cu[k] = upper_bound(children[k]));
                    for (int k = 0; k < 4 * nb; ++k)
                        if (cu[k] > threshold())
                            heap.Push(cu[k], children[k]);
                }
                // dropped triangles were below max(Lower, prune), so for the pruned (second) direction this is
                // only an upper bound where it is above prune, which is all the symmetric distance needs
                Upper = Math.Max(Lower, (heap.Count > 0) ? heap.TopKey : 0);
            }

            double threshold() { return Math.Max(Lower, prune); }

            // upper bound of the distance to To over the triangle (i, j, k)
            double upper_bound(Index3i t)
            {
                Vector3d p0 = pos[t.a], p1 = pos[t.b], p2 = pos[t.c];
                double d0 = dist[t.a], d1 = dist[t.b], d2 = dist[t.c];
                double dmax = Math.Max(d0, Math.Max(d1, d2));

                // distance to a single triangle of To is convex: its max over the triangle is at a corner
                int n0 = near[t.a], n1 = near[t.b], n2 = near[t.c];
                if (n0 == n1 && n1 == n2)
                    return dmax;
                // the distance to a single triangle of To is convex: on any convex piece of the triangle its
                // max is at a corner of the piece. Try single nearest triangles, and splits of the triangle
                // by a plane between two of them (each side bounded by its own triangle)
                List<int> cand = new List<int>(3) { n0 };
                if (n1 != n0) cand.Add(n1);
                if (n2 != n0 && n2 != n1) cand.Add(n2);
                List<Vector3d> poly = new List<Vector3d>(3) { p0, p1, p2 };
                double u0 = polygon_bound(poly, cand, 0, t);

                // triangle inequality from a corner
                double e01 = p0.Distance(p1), e12 = p1.Distance(p2), e20 = p2.Distance(p0);
                double u1 = Math.Min(d0 + Math.Max(e01, e20), Math.Min(d1 + Math.Max(e01, e12), d2 + Math.Max(e12, e20)));

                // every point is within the covering radius of some corner
                double u2 = dmax + covering_radius(e01, e12, e20);

                return Math.Min(u0, Math.Min(u1, u2));
            }

            // upper bound of the distance to To over a convex polygon (a piece of triangle t), using only the
            // candidate triangles: the best single candidate, or a split by a plane between two candidates
            double polygon_bound(List<Vector3d> poly, List<int> cand, int depth, Index3i t)
            {
                double best = double.MaxValue;
                foreach (int tid in cand)
                    best = Math.Min(best, max_distance(poly, tid, t));
                if (cand.Count < 2 || depth >= 2 || best <= threshold())
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
                            ((c == cand[i] || (c != cand[j] && normal.Dot(to.GetTriCentroid(c) - origin) >= 0)) ? ci : cj).Add(c);
                        double bi = (pi.Count >= 3) ? polygon_bound(pi, ci, depth + 1, t) : 0;
                        if (bi >= best) continue;
                        double bj = (pj.Count >= 3) ? polygon_bound(pj, cj, depth + 1, t) : 0;
                        best = Math.Min(best, Math.Max(bi, bj));
                    }
                return best;
            }

            // max over the polygon corners of the distance to triangle tid (distances of t's corners are known)
            double max_distance(List<Vector3d> poly, int tid, Index3i t)
            {
                double m = 0;
                foreach (Vector3d p in poly) {
                    double d;
                    if (p == pos[t.a] && near[t.a] == tid) d = dist[t.a];
                    else if (p == pos[t.b] && near[t.b] == tid) d = dist[t.b];
                    else if (p == pos[t.c] && near[t.c] == tid) d = dist[t.c];
                    else d = Math.Sqrt(MeshQueries.TriDistanceSqr(to, tid, p));
                    m = Math.Max(m, d);
                }
                return m;
            }

            // plane between triangles ti and tj, normal towards ti. Adjacent: the plane through the shared
            // edge that bisects the angle between them (points on ti's side are nearer to ti); otherwise the
            // bisector of their centroids. Any plane gives a valid bound, this one gives a tight one.
            bool separating_plane(int ti, int tj, out Vector3d origin, out Vector3d normal)
            {
                Index3i a = to.GetTriangle(ti), b = to.GetTriangle(tj);
                int s0 = -1, s1 = -1, oi = -1, oj = -1;
                for (int k = 0; k < 3; ++k) {
                    if (a[k] == b.a || a[k] == b.b || a[k] == b.c) { if (s0 < 0) s0 = a[k]; else s1 = a[k]; } else oi = a[k];
                    if (b[k] != a.a && b[k] != a.b && b[k] != a.c) oj = b[k];
                }
                if (s1 >= 0 && oi >= 0 && oj >= 0) {
                    Vector3d e0 = to.GetVertex(s0), e = (to.GetVertex(s1) - e0).Normalized;
                    Vector3d wi = to.GetVertex(oi) - e0, wj = to.GetVertex(oj) - e0;
                    wi = (wi - wi.Dot(e) * e).Normalized; wj = (wj - wj.Dot(e) * e).Normalized;
                    normal = wi - wj;
                    origin = e0;
                    if (normal.Length > 1e-6) {
                        normal.Normalize();
                        return true;
                    }
                }
                Vector3d ci = to.GetTriCentroid(ti), cj = to.GetTriCentroid(tj);
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


        // binary max-heap of (key, triangle)
        class MaxHeap
        {
            List<double> keys = new List<double>();
            List<Index3i> items = new List<Index3i>();
            public int Count { get { return keys.Count; } }
            public double TopKey { get { return keys[0]; } }

            public void Push(double key, Index3i item)
            {
                keys.Add(key); items.Add(item);
                int i = keys.Count - 1;
                while (i > 0) {
                    int p = (i - 1) / 2;
                    if (keys[p] >= keys[i]) break;
                    swap(i, p); i = p;
                }
            }

            public Index3i Pop()
            {
                Index3i top = items[0];
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
                Index3i t = items[i]; items[i] = items[j]; items[j] = t;
            }
        }
    }
}
