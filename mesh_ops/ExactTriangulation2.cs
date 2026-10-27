// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Constrained Delaunay triangulation of a triangle with points and segments inside, on exact points
    /// given by index. Only the orientation and incircle predicates are used, so the result is exact
    /// if they are. Points are inserted into the triangle that contains them, constraint segments by
    /// edge flips (Sloan 1993), then non-constrained edges are flipped to Delaunay (Lawson).
    /// Points must be distinct, inside the closed outer triangle, and constraint segments must not cross
    /// each other or pass through points (split them first).
    /// </summary>
    internal class ExactTriangulation2
    {
        // orient(a, b, c): +1 counter-clockwise, -1 clockwise, 0 collinear. The outer triangle is counter-clockwise.
        readonly Func<int, int, int, int> orient;
        // incircle(a, b, c, d): +1 if d is inside the circle through counter-clockwise a, b, c
        readonly Func<int, int, int, int, int> incircle;

        readonly List<int[]> tris = new List<int[]>();
        readonly List<int> free = new List<int>();
        readonly Dictionary<long, int> edgeTri = new Dictionary<long, int>(EdgeKeyComparer.Instance);     // directed edge -> triangle
        readonly HashSet<long> constrained = new HashSet<long>(EdgeKeyComparer.Instance);                 // undirected
        readonly HashSet<int> vertices = new HashSet<int>();
        readonly Dictionary<int, int> vertexTri = new Dictionary<int, int>();       // vertex -> a triangle that had it (check before use)
        readonly int[] outer;

        [MethodImpl(JitHints.Optimize)]
        public ExactTriangulation2(int c0, int c1, int c2, Func<int, int, int, int> orient, Func<int, int, int, int, int> incircle)
        {
            this.orient = orient;
            this.incircle = incircle;
            outer = new[] { c0, c1, c2 };
            add_tri(c0, c1, c2);
            vertices.Add(c0); vertices.Add(c1); vertices.Add(c2);
            constrained.Add(ukey(c0, c1)); constrained.Add(ukey(c1, c2)); constrained.Add(ukey(c2, c0));
        }

        public IEnumerable<Index3i> Triangles()
        {
            foreach (int[] t in tris)
                if (t != null)
                    yield return new Index3i(t[0], t[1], t[2]);
        }

        static long dkey(int a, int b) { return ((long)a << 32) | (uint)b; }
        static long ukey(int a, int b) { return (a < b) ? dkey(a, b) : dkey(b, a); }

        [MethodImpl(JitHints.Optimize)]
        int add_tri(int a, int b, int c)
        {
            int ti;
            int[] t = new[] { a, b, c };
            if (free.Count > 0) {
                ti = free[free.Count - 1];
                free.RemoveAt(free.Count - 1);
                tris[ti] = t;
            } else {
                ti = tris.Count;
                tris.Add(t);
            }
            edgeTri[dkey(a, b)] = ti; edgeTri[dkey(b, c)] = ti; edgeTri[dkey(c, a)] = ti;
            vertexTri[a] = ti; vertexTri[b] = ti; vertexTri[c] = ti;
            return ti;
        }

        [MethodImpl(JitHints.Optimize)]
        void remove_tri(int ti)
        {
            int[] t = tris[ti];
            edgeTri.Remove(dkey(t[0], t[1])); edgeTri.Remove(dkey(t[1], t[2])); edgeTri.Remove(dkey(t[2], t[0]));
            tris[ti] = null;
            free.Add(ti);
        }

        // vertex of triangle ti opposite to directed edge (a, b)
        [MethodImpl(JitHints.Optimize)]
        int opposite(int ti, int a, int b)
        {
            int[] t = tris[ti];
            for (int k = 0; k < 3; ++k)
                if (t[k] != a && t[k] != b)
                    return t[k];
            throw new InvalidOperationException("ExactTriangulation2: degenerate triangle");
        }


        /// <summary> insert a point inside or on the boundary of the outer triangle </summary>
        [MethodImpl(JitHints.Optimize)]
        public void InsertPoint(int p)
        {
            if (vertices.Contains(p))
                return;
            int o0, o1, o2;
            int ti = locate(p, out o0, out o1, out o2);
            if (ti < 0)
                throw new InvalidOperationException("ExactTriangulation2: point outside of the outer triangle");
            int[] t = tris[ti];
            int zeros = (o0 == 0 ? 1 : 0) + (o1 == 0 ? 1 : 0) + (o2 == 0 ? 1 : 0);
            if (zeros >= 2)
                throw new InvalidOperationException("ExactTriangulation2: point coincides with a vertex");
            vertices.Add(p);
            if (zeros == 0) {
                int a = t[0], b = t[1], c = t[2];
                remove_tri(ti);
                last = add_tri(a, b, p); add_tri(b, c, p); add_tri(c, a, p);
                legalize(a, b); legalize(b, c); legalize(c, a);
            } else if (o0 == 0) {
                split_edge(t[0], t[1], p);
            } else if (o1 == 0) {
                split_edge(t[1], t[2], p);
            } else {
                split_edge(t[2], t[0], p);
            }
        }

        // incremental Delaunay: flip the edges around a new point while they are not locally Delaunay (constrained
        // edges stay), so that the triangulation stays well shaped for walking; MakeDelaunay finishes
        readonly Stack<long> legalStack = new Stack<long>();
        [MethodImpl(JitHints.Optimize)]
        void legalize(int a0, int b0)
        {
            legalStack.Push(dkey(a0, b0));
            int guard = 0;
            while (legalStack.Count > 0 && ++guard < 10000) {
                long e = legalStack.Pop();
                int a = (int)(e >> 32), b = (int)(e & 0xFFFFFFFF);
                if (constrained.Contains(ukey(a, b)))
                    continue;
                int t1, t2;
                if (edgeTri.TryGetValue(dkey(a, b), out t1) == false || edgeTri.TryGetValue(dkey(b, a), out t2) == false)
                    continue;
                int c = opposite(t1, a, b), d = opposite(t2, b, a);
                if (incircle(a, b, c, d) <= 0)
                    continue;
                flip(a, b, out c, out d);
                legalStack.Push(dkey(a, d)); legalStack.Push(dkey(d, b)); legalStack.Push(dkey(b, c)); legalStack.Push(dkey(c, a));
            }
            legalStack.Clear();
        }

        int last = 0;
        /// <summary> point locations that fell back to the linear search (for tests: the walk should find almost all) </summary>
        internal int LinearSearches;
        readonly int[] o = new int[3];
        uint rng = 0x9E3779B9;

        // a triangle that contains p (closed), with the orientations of p to its edges: walk from the last inserted
        // triangle across edges that p is behind (the first one from a pseudo-random edge, so that the walk does not
        // cycle), with a linear search if the walk is long
        [MethodImpl(JitHints.Optimize)]
        int locate(int p, out int o0, out int o1, out int o2)
        {
            int ti = (last < tris.Count && tris[last] != null) ? last : -1;
            for (int k = 0; ti < 0 && k < tris.Count; ++k)
                if (tris[k] != null) ti = k;
            int limit = 64 + 4 * (int)Math.Sqrt(tris.Count);
            for (int step = 0; ti >= 0 && step < limit; ++step) {
                int[] t = tris[ti];
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                int start = (int)(rng % 3), next = -1;
                for (int j = 0; j < 3 && next < 0; ++j) {
                    int k = (start + j) % 3;
                    o[k] = orient(t[k], t[(k + 1) % 3], p);
                    if (o[k] < 0) {
                        int nb;
                        next = edgeTri.TryGetValue(dkey(t[(k + 1) % 3], t[k]), out nb) ? nb : -2;
                    }
                }
                if (next == -2)
                    break;      // behind the outer boundary
                if (next < 0) {
                    o0 = o[0]; o1 = o[1]; o2 = o[2];
                    return ti;
                }
                ti = next;
            }
            LinearSearches++;
            for (ti = 0; ti < tris.Count; ++ti) {
                int[] t = tris[ti];
                if (t == null)
                    continue;
                o0 = orient(t[0], t[1], p);
                if (o0 < 0) continue;
                o1 = orient(t[1], t[2], p);
                if (o1 < 0) continue;
                o2 = orient(t[2], t[0], p);
                if (o2 < 0) continue;
                return ti;
            }
            o0 = o1 = o2 = 0;
            return -1;
        }

        [MethodImpl(JitHints.Optimize)]
        void split_edge(int a, int b, int p)
        {
            bool wasConstrained = constrained.Remove(ukey(a, b));
            int t1, c = -1, d = -1;
            if (edgeTri.TryGetValue(dkey(a, b), out t1)) {
                c = opposite(t1, a, b);
                remove_tri(t1);
                last = add_tri(a, p, c); add_tri(p, b, c);
            }
            int t2;
            if (edgeTri.TryGetValue(dkey(b, a), out t2)) {
                d = opposite(t2, b, a);
                remove_tri(t2);
                add_tri(b, p, d); add_tri(p, a, d);
            }
            if (wasConstrained) {
                constrained.Add(ukey(a, p));
                constrained.Add(ukey(p, b));
            }
            // the edges opposite to p
            if (c >= 0) { legalize(c, a); legalize(b, c); }
            if (d >= 0) { legalize(d, b); legalize(a, d); }
        }

        // flip edge (a, b) shared by triangles (a, b, c) and (b, a, d) to (c, d); returns false if not possible
        [MethodImpl(JitHints.Optimize)]
        bool flip(int a, int b, out int c, out int d)
        {
            c = d = -1;
            int t1, t2;
            if (edgeTri.TryGetValue(dkey(a, b), out t1) == false || edgeTri.TryGetValue(dkey(b, a), out t2) == false)
                return false;
            c = opposite(t1, a, b);
            d = opposite(t2, b, a);
            remove_tri(t1);
            remove_tri(t2);
            add_tri(c, a, d);
            add_tri(d, b, c);
            return true;
        }

        bool is_edge(int a, int b)
        {
            return edgeTri.ContainsKey(dkey(a, b)) || edgeTri.ContainsKey(dkey(b, a));
        }

        // does edge (x, y) cross segment (u, v) in its interior?
        [MethodImpl(JitHints.Optimize)]
        bool crosses(int x, int y, int u, int v)
        {
            if (x == u || x == v || y == u || y == v)
                return false;
            int o1 = orient(u, v, x), o2 = orient(u, v, y);
            if (o1 * o2 >= 0)
                return false;
            int o3 = orient(x, y, u), o4 = orient(x, y, v);
            return o3 * o4 < 0;
        }


        /// <summary> make the segment (u, v) between two inserted points an edge of the triangulation </summary>
        [MethodImpl(JitHints.Optimize)]
        public void InsertConstraint(int u, int v)
        {
            if (u == v)
                return;
            if (vertices.Contains(u) == false || vertices.Contains(v) == false)
                throw new InvalidOperationException("ExactTriangulation2: constraint endpoint is not a vertex");
            if (is_edge(u, v)) {
                constrained.Add(ukey(u, v));
                return;
            }
            var queue = new Queue<long>();
            if (walk_crossings(u, v, queue) == false) {
                // (the walk only fails if the input breaks the preconditions: the scan finds the reason)
                queue.Clear();
                LinearSearches++;
                var seen = new HashSet<long>(EdgeKeyComparer.Instance);
                foreach (int[] t in tris) {
                    if (t == null) continue;
                    for (int k = 0; k < 3; ++k) {
                        int x = t[k], y = t[(k + 1) % 3];
                        long key = ukey(x, y);
                        if (seen.Contains(key)) continue;
                        seen.Add(key);
                        if (crosses(x, y, u, v))
                            queue.Enqueue(dkey(x, y));
                    }
                }
            }
            foreach (long e in queue)
                if (constrained.Contains(ukey((int)(e >> 32), (int)(e & 0xFFFFFFFF))))
                    throw new InvalidOperationException("ExactTriangulation2: constraint segments cross");
            int guard = 0, limit = 100 + 50 * queue.Count * queue.Count;
            while (queue.Count > 0) {
                if (++guard > limit)
                    throw new InvalidOperationException("ExactTriangulation2: constraint insertion does not terminate");
                long e = queue.Dequeue();
                int a = (int)(e >> 32), b = (int)(e & 0xFFFFFFFF);
                int t1, t2;
                if (edgeTri.TryGetValue(dkey(a, b), out t1) == false || edgeTri.TryGetValue(dkey(b, a), out t2) == false)
                    throw new InvalidOperationException("ExactTriangulation2: crossing edge disappeared");
                int c = opposite(t1, a, b), d = opposite(t2, b, a);
                // the quad a, d, b, c is strictly convex iff a and b are on opposite sides of c-d
                if (orient(c, d, a) * orient(c, d, b) < 0) {
                    flip(a, b, out c, out d);
                    if (crosses(c, d, u, v))
                        queue.Enqueue(dkey(c, d));
                } else {
                    queue.Enqueue(e);
                }
            }
            if (is_edge(u, v) == false) {
                int remaining = 0, through = 0;
                foreach (int[] t in tris) {
                    if (t == null) continue;
                    for (int k = 0; k < 3; ++k) {
                        int x = t[k], y = t[(k + 1) % 3];
                        if (crosses(x, y, u, v)) remaining++;
                    }
                    foreach (int x in t)
                        if (x != u && x != v && orient(u, v, x) == 0) through++;
                }
                throw new InvalidOperationException("ExactTriangulation2: constraint was not inserted (" + remaining + " crossing edges, " + through + " collinear vertex uses)");
            }
            constrained.Add(ukey(u, v));
        }


        // the edges crossed by the segment u-v, in order: rotate around u to the triangle that the segment leaves
        // through its opposite edge, then march across the crossed edges to v. False if that is not possible (the
        // segment passes through a vertex or leaves the triangulation)
        [MethodImpl(JitHints.Optimize)]
        bool walk_crossings(int u, int v, Queue<long> queue)
        {
            int ti;
            if (vertexTri.TryGetValue(u, out ti) == false || tris[ti] == null || Array.IndexOf(tris[ti], u) < 0)
                return false;
            // around u: counter-clockwise, triangle (u, p, q) is followed by the one with directed edge (u, q);
            // clockwise (after reaching the boundary) it is preceded by the one with directed edge (p, u)
            int p = -1, q = -1, start = ti;
            bool found = false, clockwise = false;
            for (int steps = 0; steps < 2 * tris.Count + 2; ++steps) {
                int[] t = tris[ti];
                int i = Array.IndexOf(t, u);
                p = t[(i + 1) % 3]; q = t[(i + 2) % 3];
                // v strictly inside the wedge at u: the segment leaves through (p, q)
                if (orient(u, p, v) > 0 && orient(u, v, q) > 0) {
                    found = true;
                    break;
                }
                int next;
                if (edgeTri.TryGetValue(clockwise ? dkey(p, u) : dkey(u, q), out next) == false) {
                    if (clockwise)
                        return false;
                    clockwise = true;
                    int[] s0 = tris[start];
                    int i0 = Array.IndexOf(s0, u);
                    if (edgeTri.TryGetValue(dkey(s0[(i0 + 1) % 3], u), out next) == false)
                        return false;
                }
                ti = next;
                if (ti == start)
                    return false;
            }
            if (found == false)
                return false;
            // march: (x, y) is the crossed edge, x right of u -> v, y left; the next triangle is across it
            int x = p, y = q;
            for (int steps = 0; steps < tris.Count + 1; ++steps) {
                queue.Enqueue(dkey(x, y));
                int far;
                if (edgeTri.TryGetValue(dkey(y, x), out far) == false)
                    return false;
                int c = opposite(far, y, x);
                if (c == v)
                    return true;
                int s = orient(u, v, c);
                if (s == 0)
                    return false;       // through a vertex
                if (s < 0) x = c; else y = c;
            }
            return false;
        }

        /// <summary> flip non-constrained edges until the triangulation is constrained Delaunay </summary>
        [MethodImpl(JitHints.Optimize)]
        public void MakeDelaunay()
        {
            var stack = new Stack<long>();
            foreach (int[] t in tris) {
                if (t == null) continue;
                for (int k = 0; k < 3; ++k)
                    stack.Push(dkey(t[k], t[(k + 1) % 3]));
            }
            int guard = 0, limit = 1000 + 100 * stack.Count * Math.Max(1, (int)Math.Sqrt(stack.Count));
            while (stack.Count > 0) {
                if (++guard > limit)
                    throw new InvalidOperationException("ExactTriangulation2: Delaunay flips do not terminate");
                long e = stack.Pop();
                int a = (int)(e >> 32), b = (int)(e & 0xFFFFFFFF);
                if (constrained.Contains(ukey(a, b)))
                    continue;
                int t1, t2;
                if (edgeTri.TryGetValue(dkey(a, b), out t1) == false || edgeTri.TryGetValue(dkey(b, a), out t2) == false)
                    continue;
                int c = opposite(t1, a, b), d = opposite(t2, b, a);
                if (incircle(a, b, c, d) <= 0)
                    continue;
                flip(a, b, out c, out d);
                stack.Push(dkey(a, d)); stack.Push(dkey(d, b)); stack.Push(dkey(b, c)); stack.Push(dkey(c, a));
            }
        }
    }
}
