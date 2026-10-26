// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;

namespace g3
{
    /// <summary>
    /// Removes the vertices of a boolean result that the shape does not need: new points (not input vertices)
    /// that lie inside a flat region, or on a straight edge between two flat regions, and retriangulates the
    /// region around each one. Such points come from the arrangement splitting coplanar overlapping triangles
    /// along each other's edges and diagonals (for example the faces of overlapping boxes); rounded to double,
    /// they lie slightly off the faces and edges, and repeated booleans split at them again and again.
    ///
    /// All decisions are exact: the triangles of a region lie in the same plane (their source triangles have the
    /// same plane, compared as normalized integer planes) with the same orientation, and the retriangulation uses
    /// the arrangement's exact orientation and incircle predicates in the plane's coordinate projection. A point
    /// is removed only if its triangles form one closed fan whose edges are all used by exactly two triangles,
    /// and either all of them are in one region, or exactly two of its edges separate two regions of different
    /// planes (then both edges lie on the line where the planes meet, on opposite sides of the point). The hole
    /// (a star-shaped polygon) is triangulated by ear clipping, then edges inside regions are flipped towards the
    /// Delaunay triangulation of the projection (Lawson), for better shaped triangles. The surface covers the same set, so it stays closed
    /// and keeps its exact volume; the remaining vertices keep their exact coordinates.
    ///
    /// Whether a point can be removed does not depend on the other removals (a removal keeps the regions and the
    /// lines between them), and the points are removed in a deterministic order (ComparePoints).
    /// </summary>
    internal class ExactPlanarSimplify
    {
        readonly ExactMeshArrangement arr;
        // triangles (point ids, counter-clockwise seen from the solid's outside) and their region (oriented plane)
        public readonly List<Index3i> Tris;
        readonly List<int> group;
        readonly List<bool> alive;
        // per group: the unoriented plane, an input triangle in the plane for the projection, the sign of the
        // group's orientation relative to that triangle
        readonly List<int> groupPlane = new List<int>();
        readonly List<ExactMeshArrangement.InputTriangle> groupTri = new List<ExactMeshArrangement.InputTriangle>();
        readonly List<int> groupSign = new List<int>();
        readonly Dictionary<int, List<int>> vt = new Dictionary<int, List<int>>();

        /// <summary> number of points removed </summary>
        public int Removed;
        /// <summary> number of edge flips </summary>
        public int Flips;

        readonly List<int> source;
        readonly List<bool> flipped;
        readonly Dictionary<int, int> srcGroup = new Dictionary<int, int>();
        readonly Dictionary<(BigInteger, BigInteger, BigInteger, BigInteger), int> groups = new Dictionary<(BigInteger, BigInteger, BigInteger, BigInteger), int>();
        readonly Dictionary<(BigInteger, BigInteger, BigInteger, BigInteger), int> planes = new Dictionary<(BigInteger, BigInteger, BigInteger, BigInteger), int>();

        /// <summary> tris: triangles of point ids; source: input triangle (arr.Triangles) of each; flipped: oriented opposite to it </summary>
        public ExactPlanarSimplify(ExactMeshArrangement arr, List<Index3i> tris, List<int> source, List<bool> flipped)
        {
            this.arr = arr;
            this.source = source;
            this.flipped = flipped;
            Tris = new List<Index3i>(tris);
            group = new List<int>(tris.Count);
            alive = new List<bool>(tris.Count);
            for (int i = 0; i < tris.Count; ++i) {
                group.Add(-1);
                alive.Add(true);
            }
        }

        // the group of triangle i (computed when first needed: most triangles are far from removable points)
        int group_of(int i)
        {
            int g = group[i];
            if (g >= 0)
                return g;
            int key = 2 * source[i] + (flipped[i] ? 1 : 0);
            if (srcGroup.TryGetValue(key, out g) == false) {
                var t = arr.Triangles[source[i]];
                var P = t.P;
                // normalized: divided by the gcd, first non-zero normal component positive (unoriented plane)
                BigInteger d = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(P.A, P.B), BigInteger.GreatestCommonDivisor(P.C, P.D));
                BigInteger a = P.A / d, b = P.B / d, c = P.C / d, e = P.D / d;
                int s = (a.Sign != 0) ? a.Sign : (b.Sign != 0) ? b.Sign : c.Sign;
                if (s < 0) { a = -a; b = -b; c = -c; e = -e; }
                var plane = (a, b, c, e);
                int pid;
                if (planes.TryGetValue(plane, out pid) == false) {
                    pid = planes.Count;
                    planes[plane] = pid;
                }
                int orient = s * (flipped[i] ? -1 : 1);
                var oriented = orient > 0 ? plane : (-a, -b, -c, -e);
                if (groups.TryGetValue(oriented, out g) == false) {
                    g = groupPlane.Count;
                    groups[oriented] = g;
                    groupPlane.Add(pid);
                    groupTri.Add(t);
                    groupSign.Add(flipped[i] ? -1 : 1);
                }
                srcGroup[key] = g;
            }
            group[i] = g;
            return g;
        }

        List<int> incident(int p)
        {
            List<int> l;
            if (vt.TryGetValue(p, out l) == false) {
                l = new List<int>(6);
                vt[p] = l;
            }
            return l;
        }

        // +1 if p, q, r are counter-clockwise in the oriented plane of group g
        int orient(int g, int p, int q, int r)
        {
            return groupSign[g] * arr.Orient(groupTri[g], p, q, r);
        }


        /// <summary> removes the points that are not input vertices where possible; the triangles are in Result() </summary>
        public void Run(HashSet<int> keep)
        {
            bool any = false;
            foreach (Index3i f in Tris)
                if (keep.Contains(f.a) == false || keep.Contains(f.b) == false || keep.Contains(f.c) == false) {
                    any = true;
                    break;
                }
            if (any == false)
                return;
            for (int i = 0; i < Tris.Count; ++i) {
                Index3i f = Tris[i];
                for (int k = 0; k < 3; ++k)
                    incident(f[k]).Add(i);
            }
            var candidates = new List<int>();
            foreach (var kv in vt)
                if (keep.Contains(kv.Key) == false && fan(kv.Key) != null)
                    candidates.Add(kv.Key);
            if (candidates.Count == 0)
                return;
            candidates.Sort(arr.ComparePoints);
            int first = Tris.Count;
            foreach (int v in candidates)
                remove(v);
            // the edges of the new triangles that remain (directed as in their first triangle: an EdgeKey puts the
            // smaller point id first, and point ids depend on the thread schedule)
            var flipQueue = new List<long>();
            var queued = new HashSet<long>();
            for (int i = first; i < Tris.Count; ++i)
                if (alive[i])
                    for (int k = 0; k < 3; ++k) {
                        int a = Tris[i][k], b = Tris[i][(k + 1) % 3];
                        if (queued.Add(ExactMeshArrangement.EdgeKey(a, b)))
                            flipQueue.Add(directed(a, b));
                    }
            delaunay(flipQueue);
        }

        /// <summary> the remaining triangles, in a deterministic order </summary>
        public List<Index3i> Result()
        {
            var r = new List<Index3i>();
            for (int i = 0; i < Tris.Count; ++i)
                if (alive[i])
                    r.Add(Tris[i]);
            return r;
        }

        // the triangle rotated to start at v: (v, p, q)
        Index3i at(int t, int v)
        {
            Index3i f = Tris[t];
            return (f.a == v) ? f : (f.b == v) ? new Index3i(f.b, f.c, f.a) : new Index3i(f.c, f.a, f.b);
        }

        /// <summary>
        /// The polygons around v to retriangulate (each counter-clockwise in its group's plane, with the group as
        /// the last element), or null if v cannot be removed.
        /// </summary>
        List<List<int>> fan(int v)
        {
            List<int> list = vt[v];
            int n = list.Count;
            if (n < 3)
                return null;
            // link edges p -> q of the triangles (v, p, q); every link vertex starts and ends one of them exactly
            // once (each edge v-x has two triangles, in opposite directions)
            var next = new Dictionary<int, int>(n);     // p -> triangle
            var ends = new HashSet<int>();
            foreach (int t in list) {
                Index3i f = at(t, v);
                if (next.ContainsKey(f.b) || ends.Add(f.c) == false)
                    return null;
                next[f.b] = t;
            }
            foreach (int q in ends)
                if (next.ContainsKey(q) == false)
                    return null;
            // one cycle; feature edges: where the group changes
            int start = at(list[0], v).b;
            var cycle = new List<int>(n);       // link vertices in order
            var tri = new List<int>(n);         // triangle (v, cycle[i], cycle[i+1])
            int x = start;
            do {
                int t = next[x];
                cycle.Add(x);
                tri.Add(t);
                x = at(t, v).c;
            } while (x != start && cycle.Count <= n);
            if (cycle.Count != n)
                return null;
            var features = new List<int>(2);    // i: edge v-cycle[i] separates tri[i-1] and tri[i]
            for (int i = 0; i < n; ++i)
                if (group_of(tri[i]) != group_of(tri[(i + n - 1) % n])) {
                    if (features.Count == 2)
                        return null;
                    features.Add(i);
                }
            var polys = new List<List<int>>();
            if (features.Count == 0) {
                cycle.Add(group_of(tri[0]));
                polys.Add(cycle);
                return polys;
            }
            if (features.Count != 2)
                return null;
            int i0 = features[0], i1 = features[1];
            int g0 = group_of(tri[i0]), g1 = group_of(tri[i1]);
            if (groupPlane[g0] == groupPlane[g1])
                return null;
            // the two edges lie on the line where the planes meet, on opposite sides of v (they do not overlap):
            // each side is a polygon cycle[i0..i1] (resp. cycle[i1..i0]) closed by a straight edge through v
            var p0 = new List<int>();
            for (int i = i0; ; i = (i + 1) % n) {
                p0.Add(cycle[i]);
                if (i == i1) break;
            }
            var p1 = new List<int>();
            for (int i = i1; ; i = (i + 1) % n) {
                p1.Add(cycle[i]);
                if (i == i0) break;
            }
            p0.Add(g0);
            p1.Add(g1);
            polys.Add(p0);
            polys.Add(p1);
            return polys;
        }

        bool has_edge(int a, int b)
        {
            List<int> l;
            if (vt.TryGetValue(a, out l) == false)
                return false;
            foreach (int t in l) {
                Index3i f = Tris[t];
                if (f.a == b || f.b == b || f.c == b)
                    return true;
            }
            return false;
        }

        void remove(int v)
        {
            List<List<int>> polys = fan(v);
            if (polys == null)
                return;
            var add = new List<Index3i>();
            var addGroup = new List<int>();
            foreach (List<int> poly in polys) {
                int g = poly[poly.Count - 1];
                poly.RemoveAt(poly.Count - 1);
                if (ear_clip(g, poly, add) == false)
                    return;
                while (addGroup.Count < add.Count)
                    addGroup.Add(g);
            }
            // the new edges must not exist elsewhere (they cannot in a valid arrangement; checked for safety)
            foreach (Index3i f in add)
                for (int k = 0; k < 3; ++k) {
                    int a = f[k], b = f[(k + 1) % 3];
                    if (has_edge(a, b) && is_link_edge(v, a, b) == false)
                        return;
                }
            foreach (int t in vt[v].ToArray()) {
                alive[t] = false;
                Index3i f = Tris[t];
                for (int k = 0; k < 3; ++k)
                    if (f[k] != v)
                        vt[f[k]].Remove(t);
            }
            vt.Remove(v);
            for (int i = 0; i < add.Count; ++i) {
                int t = Tris.Count;
                Tris.Add(add[i]);
                group.Add(addGroup[i]);
                alive.Add(true);
                for (int k = 0; k < 3; ++k)
                    incident(add[i][k]).Add(t);
            }
            Removed++;
        }

        // a-b is an edge of a triangle around v that is not incident to v (it stays)
        bool is_link_edge(int v, int a, int b)
        {
            foreach (int t in vt[v]) {
                Index3i f = at(t, v);
                if ((f.b == a && f.c == b) || (f.b == b && f.c == a))
                    return true;
            }
            return false;
        }

        // triangulates a simple counter-clockwise polygon (no repeated vertices) without new vertices
        bool ear_clip(int g, List<int> poly, List<Index3i> add)
        {
            var p = new List<int>(poly);
            while (p.Count > 3) {
                int n = p.Count;
                bool found = false;
                for (int i = 0; i < n && found == false; ++i) {
                    int a = p[(i + n - 1) % n], b = p[i], c = p[(i + 1) % n];
                    if (orient(g, a, b, c) <= 0)
                        continue;
                    bool empty = true;
                    for (int j = 0; j < n && empty; ++j) {
                        int x = p[j];
                        if (x == a || x == b || x == c)
                            continue;
                        if (orient(g, a, b, x) >= 0 && orient(g, b, c, x) >= 0 && orient(g, c, a, x) >= 0)
                            empty = false;
                    }
                    if (empty == false)
                        continue;
                    add.Add(new Index3i(a, b, c));
                    p.RemoveAt(i);
                    found = true;
                }
                if (found == false)
                    return false;
            }
            if (orient(g, p[0], p[1], p[2]) <= 0)
                return false;
            add.Add(new Index3i(p[0], p[1], p[2]));
            return true;
        }

        static long directed(int a, int b)
        {
            return ((long)a << 32) | (uint)b;
        }

        // Lawson flips of the edges inside regions, starting from the given edges, towards the Delaunay
        // triangulation of the projection. Only for the shape of the triangles: an edge is flipped only if the
        // floating-point filter shows that the opposite point is strictly inside the circumcircle (so it
        // terminates, and does not depend on DisableFilters); nearly cocircular cases are left as they are. The
        // new triangles are checked exactly (strictly convex quad). The queue holds directed edges (directed());
        // pending, their EdgeKeys.
        void delaunay(List<long> queue)
        {
            var pending = new HashSet<long>();
            foreach (long d in queue)
                pending.Add(ExactMeshArrangement.EdgeKey((int)(d >> 32), (int)(d & 0xFFFFFFFF)));
            var work = new Queue<long>(queue);
            var two = new List<int>(2);
            while (work.Count > 0) {
                long e = work.Dequeue();
                int x = (int)(e >> 32), y = (int)(e & 0xFFFFFFFF);
                pending.Remove(ExactMeshArrangement.EdgeKey(x, y));
                List<int> lx;
                if (vt.TryGetValue(x, out lx) == false)
                    continue;
                two.Clear();
                foreach (int t in lx) {
                    Index3i f = Tris[t];
                    if (f.a == y || f.b == y || f.c == y)
                        two.Add(t);
                }
                if (two.Count != 2 || group_of(two[0]) != group_of(two[1]))
                    continue;
                int g = group_of(two[0]);
                // t1 = (x, y, p), t2 = (y, x, q)
                Index3i t1 = at(two[0], x), t2 = at(two[1], x);
                if (t1.b != y) { Index3i tmp = t1; t1 = t2; t2 = tmp; }
                if (t1.b != y || t2.c != y)
                    continue;
                int p = t1.c, q = t2.b;
                if (p == q || groupSign[g] * arr.InCircleFiltered(groupTri[g], x, y, p, q) <= 0)
                    continue;
                // the quad x, q, y, p must be strictly convex at x and y
                if (orient(g, p, x, q) <= 0 || orient(g, q, y, p) <= 0 || has_edge(p, q))
                    continue;
                int ia = two[0], ib = two[1];
                foreach (int t in new[] { ia, ib }) {
                    Index3i f = Tris[t];
                    for (int k = 0; k < 3; ++k)
                        vt[f[k]].Remove(t);
                }
                Tris[ia] = new Index3i(p, x, q);
                Tris[ib] = new Index3i(q, y, p);
                foreach (int t in new[] { ia, ib }) {
                    Index3i f = Tris[t];
                    for (int k = 0; k < 3; ++k)
                        vt[f[k]].Add(t);
                }
                Flips++;
                foreach (long k in new[] { directed(x, q), directed(q, y), directed(y, p), directed(p, x) })
                    if (pending.Add(ExactMeshArrangement.EdgeKey((int)(k >> 32), (int)(k & 0xFFFFFFFF))))
                        work.Enqueue(k);
            }
        }
    }
}
