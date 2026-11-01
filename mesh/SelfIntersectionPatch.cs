// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Certificate that a patch of triangles cannot intersect itself, after the normal-cone and contour criterion of
    /// Volino and Magnenat-Thalmann (1994) with the star-shaped contour test of Schvartzman, Pérez and Otaduy (2010),
    /// made exact for a triangle mesh (docs/design/SELF_INTERSECTION_PATCH_CERTIFICATE.md). The patch is certified if
    /// <para>
    /// (1) it is a consistently oriented triangulated disk (edge- and vertex-manifold, edge-connected, one boundary
    ///     loop, Euler characteristic 1; integer checks on the vertex ids);
    /// </para><para>
    /// (2) every triangle projects along the axis with the same strict orientation (exact Orient2d);
    /// </para><para>
    /// (3) the boundary loop is strictly counter-clockwise about the projected center point (relative to that
    ///     orientation) in every edge, and winds once (exact Orient2d and comparisons).
    /// </para>
    /// Then the projection is one to one on the patch, so two of its triangles meet exactly in their shared vertices
    /// and edge: ExactTriangleIntersection.IntersectsBeyondShared is false for every pair. Any failed condition, a
    /// degenerate projection, or a non-finite coordinate gives no certificate (false); it never claims an intersection.
    /// </summary>
    internal static class SelfIntersectionPatch
    {
        public static long[] DBG = new long[16];
        static bool F(int r) { DBG[r]++; return false; }
        /// <summary>
        /// is the patch (triangles as vertex ids, wound as in the mesh; positions by vertex id) certified to be free of
        /// self-intersections, projected along axis (dropped coordinate), with the contour star-shaped about the
        /// position of vertex center?
        /// </summary>
        internal static bool Certify(List<Index3i> tris, Func<int, Vector3d> position, int axis, int center)
        {
            if (tris.Count == 0)
                return F(1);
            var pos = new Dictionary<int, Vector2d>();
            Func<int, Vector2d> p2 = (v) => {
                Vector2d q;
                if (pos.TryGetValue(v, out q) == false) {
                    Vector3d p = position(v);
                    q = drop(p, axis);
                    pos[v] = q;
                }
                return q;
            };
            // (2) orientation, and the finite check
            int sign = 0;
            foreach (Index3i t in tris) {
                Vector2d a = p2(t.a), b = p2(t.b), c = p2(t.c);
                if (finite(a) == false || finite(b) == false || finite(c) == false)
                    return F(2);
                int o = ExactPredicates.Orient2d(a, b, c);
                if (o == 0 || (sign != 0 && o != sign))
                    return F(3);
                sign = o;
            }

            // (1) the disk: directed edges, each at most once (consistent orientation, edge-manifold)
            var directed = new Dictionary<long, int>();      // directed edge (u, w) -> triangle index
            for (int i = 0; i < tris.Count; ++i) {
                Index3i t = tris[i];
                if (t.a == t.b || t.b == t.c || t.c == t.a)
                    return F(4);
                for (int k = 0; k < 3; ++k) {
                    long key = edge_key(t[k], t[(k + 1) % 3]);
                    if (directed.ContainsKey(key))
                        return F(5);           // the same directed edge twice: inconsistent or non-manifold
                    directed[key] = i;
                }
            }
            // boundary edges: directed (u, w) without (w, u). Each boundary vertex has exactly one outgoing one.
            var nextOnBoundary = new Dictionary<int, int>();
            int undirected = 0;
            var vertices = new HashSet<int>();
            foreach (var kv in directed) {
                int u = (int)(kv.Key >> 32), w = (int)(kv.Key & 0xffffffff);
                vertices.Add(u);
                if (directed.ContainsKey(edge_key(w, u))) {
                    if (u < w) undirected++;
                    continue;
                }
                undirected++;
                if (nextOnBoundary.ContainsKey(u))
                    return F(6);               // two boundary edges leave u: not a disk at u
                nextOnBoundary[u] = w;
            }
            // Euler characteristic of a disk
            if (vertices.Count - undirected + tris.Count != 1)
                return F(7);
            // one boundary loop through all boundary edges
            if (nextOnBoundary.Count < 3)
                return F(8);
            var loop = new List<int>();
            int start = -1;
            foreach (int u in nextOnBoundary.Keys) { start = u; break; }
            int cur = start;
            do {
                loop.Add(cur);
                int nxt;
                if (nextOnBoundary.TryGetValue(cur, out nxt) == false || loop.Count > nextOnBoundary.Count)
                    return F(9);
                cur = nxt;
            } while (cur != start);
            if (loop.Count != nextOnBoundary.Count)
                return F(10);                   // more than one boundary loop
            // vertex-manifold: the triangles at every vertex form one fan (walked through the directed edges)
            if (vertex_fans_connected(tris, directed) == false)
                return F(11);
            // edge-connected
            if (edge_connected(tris, directed) == false)
                return F(12);

            // (3) the contour about the center, in the direction of the orientation (with sign < 0 the induced
            // boundary runs clockwise: walk it backwards)
            if (vertices.Contains(center) == false)
                return F(13);
            Vector2d z = p2(center);
            int m = loop.Count;
            var q = new Vector2d[m];
            int at = -1;
            for (int i = 0; i < m; ++i) {
                int v = sign > 0 ? loop[i] : loop[(m - i) % m];
                q[i] = p2(v);
                if (v == center) at = i;
            }
            if (at < 0) {
                // the center inside: every edge strictly counter-clockwise about it, and the loop winds once
                int crossings = 0;
                for (int i = 0; i < m; ++i) {
                    Vector2d q0 = q[i], q1 = q[(i + 1) % m];
                    if (ExactPredicates.Orient2d(z, q0, q1) <= 0)
                        return F(14);
                    // counter-clockwise about z: an edge from below to at or above the line y = z.y crosses the ray +x
                    if (q0.y < z.y && q1.y >= z.y)
                        crossings++;
                }
                if (crossings != 1) return F(15); DBG[0]++; return true;
            }
            // the center on the contour (eg a boundary vertex of the mesh): the chain from its successor to its
            // predecessor turns strictly counter-clockwise about it, and less than a full turn: it never comes back to
            // the ray from z through the successor (a chain that did would cross that ray from its right to its left)
            Vector2d first = q[(at + 1) % m];
            for (int k = 1; k < m - 1; ++k) {
                Vector2d q0 = q[(at + k) % m], q1 = q[(at + k + 1) % m];
                if (ExactPredicates.Orient2d(z, q0, q1) <= 0)
                    return F(15);
                if (ExactPredicates.Orient2d(z, first, q0) < 0 && ExactPredicates.Orient2d(z, first, q1) >= 0)
                    return F(16);
            }
            DBG[0]++; return true;
        }

        // the triangles at each vertex form one fan: from one triangle at v, rotate across the edges at v (both ways)
        // and count the triangles reached
        static bool vertex_fans_connected(List<Index3i> tris, Dictionary<long, int> directed)
        {
            var count = new Dictionary<int, int>();
            var first = new Dictionary<int, int>();
            for (int i = 0; i < tris.Count; ++i)
                for (int k = 0; k < 3; ++k) {
                    int v = tris[i][k];
                    int c;
                    count[v] = count.TryGetValue(v, out c) ? c + 1 : 1;
                    if (first.ContainsKey(v) == false) first[v] = i;
                }
            foreach (var kv in count) {
                int v = kv.Key, reached = 1, t0 = first[v];
                // forward: triangle (v, x, y) holds the directed edge (y, v); its neighbour across that edge, wound
                // consistently, holds (v, y)
                int t = t0;
                bool closed = false;
                while (true) {
                    int y = after(tris[t], after(tris[t], v));
                    int nt;
                    if (directed.TryGetValue(edge_key(v, y), out nt) == false) break;
                    if (nt == t0) { closed = true; break; }
                    t = nt; reached++;
                    if (reached > kv.Value) return false;
                }
                if (closed == false) {
                    // backward: triangle (v, x, y) holds (v, x); its neighbour across that edge holds (x, v)
                    t = t0;
                    while (true) {
                        int x = after(tris[t], v);
                        int pt;
                        if (directed.TryGetValue(edge_key(x, v), out pt) == false) break;
                        t = pt; reached++;
                        if (reached > kv.Value) return false;
                    }
                }
                if (reached != kv.Value)
                    return false;
            }
            return true;
        }

        static bool edge_connected(List<Index3i> tris, Dictionary<long, int> directed)
        {
            var seen = new bool[tris.Count];
            var stack = new Stack<int>();
            stack.Push(0); seen[0] = true;
            int n = 1;
            while (stack.Count > 0) {
                Index3i t = tris[stack.Pop()];
                for (int k = 0; k < 3; ++k) {
                    int nb;
                    if (directed.TryGetValue(edge_key(t[(k + 1) % 3], t[k]), out nb) && seen[nb] == false) {
                        seen[nb] = true; n++; stack.Push(nb);
                    }
                }
            }
            return n == tris.Count;
        }

        // the vertex after v in the cyclic order of t
        static int after(Index3i t, int v)
        {
            return t.a == v ? t.b : (t.b == v ? t.c : t.a);
        }

        static long edge_key(int u, int w)
        {
            return ((long)u << 32) | (uint)w;
        }

        static bool finite(Vector2d p)
        {
            return (p.x - p.x) == 0 && (p.y - p.y) == 0;
        }

        // drop coordinate axis
        static Vector2d drop(Vector3d v, int axis)
        {
            return (axis == 0) ? new Vector2d(v.y, v.z) : (axis == 1) ? new Vector2d(v.z, v.x) : new Vector2d(v.x, v.y);
        }
    }
}
