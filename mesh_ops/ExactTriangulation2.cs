// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

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
        readonly int[] outer;

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
            return ti;
        }

        void remove_tri(int ti)
        {
            int[] t = tris[ti];
            edgeTri.Remove(dkey(t[0], t[1])); edgeTri.Remove(dkey(t[1], t[2])); edgeTri.Remove(dkey(t[2], t[0]));
            tris[ti] = null;
            free.Add(ti);
        }

        // vertex of triangle ti opposite to directed edge (a, b)
        int opposite(int ti, int a, int b)
        {
            int[] t = tris[ti];
            for (int k = 0; k < 3; ++k)
                if (t[k] != a && t[k] != b)
                    return t[k];
            throw new InvalidOperationException("ExactTriangulation2: degenerate triangle");
        }


        /// <summary> insert a point inside or on the boundary of the outer triangle </summary>
        public void InsertPoint(int p)
        {
            if (vertices.Contains(p))
                return;
            for (int ti = 0; ti < tris.Count; ++ti) {
                int[] t = tris[ti];
                if (t == null)
                    continue;
                int o0 = orient(t[0], t[1], p);
                if (o0 < 0) continue;
                int o1 = orient(t[1], t[2], p);
                if (o1 < 0) continue;
                int o2 = orient(t[2], t[0], p);
                if (o2 < 0) continue;
                int zeros = (o0 == 0 ? 1 : 0) + (o1 == 0 ? 1 : 0) + (o2 == 0 ? 1 : 0);
                if (zeros >= 2)
                    throw new InvalidOperationException("ExactTriangulation2: point coincides with a vertex");
                vertices.Add(p);
                if (zeros == 0) {
                    int a = t[0], b = t[1], c = t[2];
                    remove_tri(ti);
                    add_tri(a, b, p); add_tri(b, c, p); add_tri(c, a, p);
                } else if (o0 == 0) {
                    split_edge(t[0], t[1], p);
                } else if (o1 == 0) {
                    split_edge(t[1], t[2], p);
                } else {
                    split_edge(t[2], t[0], p);
                }
                return;
            }
            throw new InvalidOperationException("ExactTriangulation2: point outside of the outer triangle");
        }

        void split_edge(int a, int b, int p)
        {
            bool wasConstrained = constrained.Remove(ukey(a, b));
            int t1;
            if (edgeTri.TryGetValue(dkey(a, b), out t1)) {
                int c = opposite(t1, a, b);
                remove_tri(t1);
                add_tri(a, p, c); add_tri(p, b, c);
            }
            int t2;
            if (edgeTri.TryGetValue(dkey(b, a), out t2)) {
                int d = opposite(t2, b, a);
                remove_tri(t2);
                add_tri(b, p, d); add_tri(p, a, d);
            }
            if (wasConstrained) {
                constrained.Add(ukey(a, p));
                constrained.Add(ukey(p, b));
            }
        }

        // flip edge (a, b) shared by triangles (a, b, c) and (b, a, d) to (c, d); returns false if not possible
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
            var seen = new HashSet<long>(EdgeKeyComparer.Instance);
            foreach (int[] t in tris) {
                if (t == null) continue;
                for (int k = 0; k < 3; ++k) {
                    int x = t[k], y = t[(k + 1) % 3];
                    long key = ukey(x, y);
                    if (seen.Contains(key)) continue;
                    seen.Add(key);
                    if (crosses(x, y, u, v)) {
                        if (constrained.Contains(key))
                            throw new InvalidOperationException("ExactTriangulation2: constraint segments cross");
                        queue.Enqueue(dkey(x, y));
                    }
                }
            }
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


        /// <summary> flip non-constrained edges until the triangulation is constrained Delaunay </summary>
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
