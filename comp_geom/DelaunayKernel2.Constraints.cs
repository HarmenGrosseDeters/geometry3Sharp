// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace g3
{
    /// <summary>
    /// The constrained phase of the kernel: points inserted one at a time by splitting and Lawson flips that never cross a
    /// constrained edge (Lawson 1977; the constrained Delaunay property of Lee and Lin 1986, Chew 1989), segments recovered by
    /// a walk and flips (Sloan 1993). Ghost triangles take part like any other (Cheng, Dey, Shewchuk 2012), so a point outside
    /// the hull extends it.
    /// </summary>
    internal sealed partial class DelaunayKernel2
    {
        /// <summary> per half-edge: the constraint it belongs to, or -1 (both half-edges of an edge carry the same id) </summary>
        internal int[] Seg;
        /// <summary> per vertex: a half-edge leaving it, or -1 </summary>
        int[] leaving;
        /// <summary> number of vertex ids in use; ids N + 1 and above are vertices made by the constrained phase </summary>
        internal int VertexCount;
        /// <summary> per triangle slot: the caller's tag (domain and region), inherited by the triangles a split or flip makes </summary>
        internal int[] Tag;
        /// <summary> if not null, every slot that a split or flip rewrites is appended </summary>
        internal List<int> Dirty;
        /// <summary> per constraint id: its sources (indices into the caller's list, with repetition) </summary>
        internal readonly List<List<int>> Sources = new List<List<int>>();

        /// <summary> Switches to the constrained phase after <see cref="Build"/>. </summary>
        internal void BeginConstraints()
        {
            Seg = new int[V.Length];
            for (int i = 0; i < Seg.Length; ++i) Seg[i] = -1;
            Tag = new int[V.Length / 3];
            VertexCount = N + 1;
            leaving = new int[Math.Max(16, X.Length + 1)];
            for (int i = 0; i < leaving.Length; ++i) leaving[i] = -1;
            for (int t = 0; t < TriangleSlots; ++t) Touch(t);
        }

        void Touch(int t)
        {
            for (int k = 0; k < 3; ++k) {
                int v = V[3 * t + k];
                if (v != Infinite) leaving[v] = 3 * t + k;
            }
        }

        static int Prev(int e) => e % 3 == 0 ? e + 2 : e - 1;

        int NewVertex(double x, double y)
        {
            int id = VertexCount++;
            if (id >= X.Length) {
                int capacity = Math.Max(16, 2 * X.Length);
                Array.Resize(ref X, capacity);
                Array.Resize(ref Y, capacity);
            }
            if (id >= leaving.Length) {
                int old = leaving.Length;
                Array.Resize(ref leaving, Math.Max(id + 1, 2 * old));
                for (int i = old; i < leaving.Length; ++i) leaving[i] = -1;
            }
            X[id] = x; Y[id] = y;
            return id;
        }

        int NewSlots(int count)
        {
            int s = TriangleSlots;
            while (s + count > V.Length / 3) {
                Grow();
                int old = Seg.Length;
                Array.Resize(ref Seg, V.Length);
                for (int i = old; i < Seg.Length; ++i) Seg[i] = -1;
                Array.Resize(ref Tag, V.Length / 3);
            }
            TriangleSlots = s + count;
            return s;
        }

        /// <summary> sets a half-edge's twin and constraint, both ways </summary>
        void Join(int e, int f, int seg) { H[e] = f; H[f] = e; Seg[e] = seg; Seg[f] = seg; }

        // ---------------------------------------------------------------- location

        /// <summary>
        /// Walks to the triangle that contains (x, y) (closed), or to a ghost whose hull edge sees it strictly. Sets vertex to the
        /// vertex with exactly these coordinates, if one exists.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal int LocatePoint(double px, double py, out int vertex)
        {
            vertex = -1;
            int t = hint;
            if (t < 0 || IsGhost(t)) {
                for (t = 0; t < TriangleSlots && IsGhost(t); ++t) { }
            }
            int entry = -1;
            long limit = 16L * TriangleSlots + 64;
            for (long step = 0; step < limit; ++step) {
                walkState ^= walkState << 13; walkState ^= walkState >> 17; walkState ^= walkState << 5;
                int start = (int)(walkState % 3);
                int moved = -1;
                for (int j = 0; j < 3; ++j) {
                    int e = 3 * t + (start + j) % 3;
                    if (e == entry) continue;
                    int u = V[e], v = V[Next(e)];
                    if (Orient(X[u], Y[u], X[v], Y[v], px, py) < 0) { moved = e; break; }
                }
                if (moved < 0) {
                    for (int k = 0; k < 3; ++k) {
                        int u = V[3 * t + k];
                        if (X[u] == px && Y[u] == py) { vertex = u; break; }
                    }
                    return t;
                }
                entry = H[moved];
                t = entry / 3;
                if (IsGhost(t)) return t;
            }
            for (t = 0; t < TriangleSlots; ++t) {
                if (IsGhost(t)) continue;
                bool inside = true;
                for (int k = 0; k < 3 && inside; ++k) {
                    int u = V[3 * t + k], v = V[3 * t + (k + 1) % 3];
                    if (Orient(X[u], Y[u], X[v], Y[v], px, py) < 0) inside = false;
                }
                if (!inside) continue;
                for (int k = 0; k < 3; ++k) {
                    int u = V[3 * t + k];
                    if (X[u] == px && Y[u] == py) { vertex = u; break; }
                }
                return t;
            }
            var p = new Vector2d(px, py);
            for (t = 0; t < TriangleSlots; ++t) {
                if (!IsGhost(t)) continue;
                int i = 3 * t, k = V[i] == Infinite ? 0 : V[i + 1] == Infinite ? 1 : 2;
                if (GhostConflict(V[i + (k + 1) % 3], V[i + (k + 2) % 3], p)) return t;
            }
            throw new InvalidOperationException("DelaunayKernel2: a point is in no triangle");
        }

        // ---------------------------------------------------------------- point insertion

        /// <summary>
        /// Inserts (x, y) and restores the constrained Delaunay property by flips. Returns the new vertex, or the existing one
        /// with these coordinates. A point on a constrained edge splits it; both halves keep the constraint (as copies).
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal int InsertPoint(double x, double y)
        {
            int t = LocatePoint(x, y, out int existing);
            if (existing >= 0) return existing;
            int p = NewVertex(x, y);
            var stack = flipStack;
            stack.Clear();
            int onEdge = -1;
            if (!IsGhost(t)) {
                for (int k = 0; k < 3; ++k) {
                    int e = 3 * t + k, u = V[e], v = V[Next(e)];
                    if (Orient(X[u], Y[u], X[v], Y[v], x, y) == 0) { onEdge = e; break; }
                }
            }
            if (onEdge >= 0) SplitEdge(onEdge, p, stack);
            else SplitTriangle(t, p, stack);
            Legalize(p, stack);
            return p;
        }

        readonly List<int> flipStack = new List<int>();

        void SplitTriangle(int t, int p, List<int> stack)
        {
            int i = 3 * t;
            int a = V[i], b = V[i + 1], c = V[i + 2];
            int hab = H[i], hbc = H[i + 1], hca = H[i + 2];
            int sab = Seg[i], sbc = Seg[i + 1], sca = Seg[i + 2];
            int t1 = NewSlots(2), t2 = t1 + 1;
            SetTriangle(t, a, b, p);
            SetTriangle(t1, b, c, p);
            SetTriangle(t2, c, a, p);
            Join(3 * t, hab, sab);
            Join(3 * t1, hbc, sbc);
            Join(3 * t2, hca, sca);
            Join(3 * t + 1, 3 * t1 + 2, -1);
            Join(3 * t1 + 1, 3 * t2 + 2, -1);
            Join(3 * t2 + 1, 3 * t + 2, -1);
            Tag[t1] = Tag[t2] = Tag[t];
            Touch(t); Touch(t1); Touch(t2);
            if (Dirty != null) { Dirty.Add(t); Dirty.Add(t1); Dirty.Add(t2); }
            stack.Add(3 * t); stack.Add(3 * t1); stack.Add(3 * t2);
            hint = IsGhost(t) ? (IsGhost(t1) ? t2 : t1) : t;
        }

        /// <summary> splits half-edge e (in triangle a, b, c, from a to b) and its twin at p </summary>
        void SplitEdge(int e, int p, List<int> stack)
        {
            int t = e / 3, f = H[e], t2 = f / 3;
            int a = V[e], b = V[Next(e)], c = V[Next(Next(e))];
            int d = V[Next(Next(f))];
            int hbc = H[Next(e)], hca = H[Next(Next(e))], had = H[Next(f)], hdb = H[Next(Next(f))];
            int sbc = Seg[Next(e)], sca = Seg[Next(Next(e))], sad = Seg[Next(f)], sdb = Seg[Next(Next(f))];
            int seg = Seg[e];
            int seg2 = seg < 0 ? -1 : CopyConstraint(seg);
            int t1 = NewSlots(2), t3 = t1 + 1;
            // t = (a, p, c), t1 = (p, b, c), t2 = (b, p, d), t3 = (p, a, d)
            SetTriangle(t, a, p, c);
            SetTriangle(t1, p, b, c);
            SetTriangle(t2, b, p, d);
            SetTriangle(t3, p, a, d);
            Join(3 * t, 3 * t3, seg);          // a-p
            Join(3 * t1, 3 * t2, seg2);        // p-b
            Join(3 * t + 1, 3 * t1 + 2, -1);   // p-c
            Join(3 * t2 + 1, 3 * t3 + 2, -1);  // p-d
            Join(3 * t + 2, hca, sca);         // c-a
            Join(3 * t1 + 1, hbc, sbc);        // b-c
            Join(3 * t2 + 2, hdb, sdb);        // d-b
            Join(3 * t3 + 1, had, sad);        // a-d
            Tag[t1] = Tag[t]; Tag[t3] = Tag[t2];
            Touch(t); Touch(t1); Touch(t2); Touch(t3);
            if (Dirty != null) { Dirty.Add(t); Dirty.Add(t1); Dirty.Add(t2); Dirty.Add(t3); }
            stack.Add(3 * t + 2); stack.Add(3 * t1 + 1); stack.Add(3 * t2 + 2); stack.Add(3 * t3 + 1);
            hint = !IsGhost(t) ? t : t2;
        }

        int CopyConstraint(int seg)
        {
            Sources.Add(new List<int>(Sources[seg]));
            return Sources.Count - 1;
        }

        /// <summary>
        /// Lawson's flips around the new vertex p: stack holds the half-edges opposite p. An edge is flipped if it is not
        /// constrained and p lies in the circumcircle of the triangle across it (for a ghost: beyond its hull edge).
        /// </summary>
        void Legalize(int p, List<int> stack)
        {
            while (stack.Count > 0) {
                int e = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                if (Seg[e] >= 0) continue;
                int f = H[e];
                if (!Conflict(f / 3, p)) continue;
                // the triangle of e is (u, w, p) with e = u -> w; after the flip p sees the two outer edges of the other triangle
                Flip(e, out int n1, out int n2);
                stack.Add(n1); stack.Add(n2);
            }
        }

        /// <summary>
        /// Flips the edge of half-edge e: triangles (a, b, c) and (b, a, d), e = a -> b, become (c, a, d) and (d, b, c). n1 and n2
        /// are the half-edges a -> d and d -> b, the former outer edges of the second triangle (opposite c).
        /// </summary>
        void Flip(int e, out int n1, out int n2)
        {
            int t = e / 3, f = H[e], t2 = f / 3;
            int a = V[e], b = V[Next(e)], c = V[Next(Next(e))], d = V[Next(Next(f))];
            int hbc = H[Next(e)], hca = H[Next(Next(e))], had = H[Next(f)], hdb = H[Next(Next(f))];
            int sbc = Seg[Next(e)], sca = Seg[Next(Next(e))], sad = Seg[Next(f)], sdb = Seg[Next(Next(f))];
            SetTriangle(t, c, a, d);
            SetTriangle(t2, d, b, c);
            Join(3 * t, hca, sca);          // c-a
            Join(3 * t + 1, had, sad);      // a-d
            Join(3 * t + 2, 3 * t2 + 2, -1);// d-c with c-d
            Join(3 * t2, hdb, sdb);         // d-b
            Join(3 * t2 + 1, hbc, sbc);     // b-c
            // the flipped edge is unconstrained, so the tags of the two triangles stay as they were; while a split has lifted a
            // constraint, a flip may cross the boundary and leave a wrong tag, which the refiner's Collect assigns anew
            Touch(t); Touch(t2);
            if (Dirty != null) { Dirty.Add(t); Dirty.Add(t2); }
            n1 = 3 * t + 1; n2 = 3 * t2;
        }

        // ---------------------------------------------------------------- edges and segments

        /// <summary> the half-edge from u to w, or -1 </summary>
        internal int FindEdge(int u, int w)
        {
            int start = leaving[u];
            if (start < 0) return -1;
            int e = start;
            int guard = 0;
            do {
                if (V[Next(e)] == w) return e;
                e = H[Prev(e)];
                if (++guard > TriangleSlots * 3) throw new InvalidOperationException("DelaunayKernel2: the star of a vertex is not closed");
            } while (e != start);
            return -1;
        }

        /// <summary> the half-edges leaving u, counter-clockwise </summary>
        internal IEnumerable<int> Star(int u)
        {
            int start = leaving[u];
            if (start < 0) yield break;
            int e = start;
            do {
                yield return e;
                e = H[Prev(e)];
            } while (e != start);
        }

        internal Vector2d Point(int v) => new Vector2d(X[v], Y[v]);
        internal int OrientV(int a, int b, int c) => Orient(a, b, c);

        /// <summary>
        /// Makes u-w an edge by Sloan's flips, given that every edge it crosses is unconstrained, then restores the constrained
        /// Delaunay property of the new edges by Lawson's flips. Returns the half-edge u -> w.
        /// </summary>
        internal int RecoverEdge(int u, int w, List<long> crossed)
        {
            var queue = new Queue<long>(crossed);
            var created = new List<long>();
            long guard = 0, limit = 64L * (crossed.Count + 4) * (crossed.Count + 4);
            while (queue.Count > 0) {
                if (++guard > limit) throw new InvalidOperationException("DelaunayKernel2: segment recovery does not terminate");
                long key = queue.Dequeue();
                int e = FindEdge((int)(key >> 32), (int)(key & 0xffffffff));
                if (e < 0) continue;   // flipped away meanwhile
                int f = H[e];
                int a = V[e], b = V[Next(e)], c = V[Next(Next(e))], d = V[Next(Next(f))];
                // the quadrilateral a, d, b, c is strictly convex iff c and d lie on opposite sides of a-b (given) and a, b on
                // opposite sides of c-d
                if (Orient(c, d, a) >= 0 || Orient(c, d, b) <= 0) { queue.Enqueue(key); continue; }
                Flip(e, out _, out _);
                // the new edge c-d
                if (c == u && d == w || c == w && d == u) continue;
                if (c != u && c != w && d != u && d != w && Orient(u, w, c) * Orient(u, w, d) < 0) queue.Enqueue(Key(c, d));
                else created.Add(Key(c, d));
            }
            int result = FindEdge(u, w);
            if (result < 0) throw new InvalidOperationException("DelaunayKernel2: segment recovery failed");
            RestoreDelaunay(created, u, w);
            return FindEdge(u, w);
        }

        /// <summary>
        /// Lawson's flips over the given edges and whatever they touch, until every unconstrained real edge among them is locally
        /// Delaunay; the edge skipU-skipW (about to become constrained) is never flipped. Terminates (Lawson 1977).
        /// </summary>
        internal void RestoreDelaunay(IEnumerable<long> edges, int skipU = -1, int skipW = -1)
        {
            var pending = new Stack<long>(edges);
            long flips = 0, flipLimit = 64L * (TriangleSlots + 16);
            while (pending.Count > 0) {
                long key = pending.Pop();
                int a0 = (int)(key >> 32), b0 = (int)(key & 0xffffffff);
                if (a0 == skipU && b0 == skipW || a0 == skipW && b0 == skipU) continue;
                int e = FindEdge(a0, b0);
                if (e < 0 || Seg[e] >= 0) continue;
                int f = H[e];
                if (IsGhost(e / 3) || IsGhost(f / 3)) continue;
                int c = V[Next(Next(e))], d = V[Next(Next(f))];
                if (ExactPredicates.InCircle(Point(a0), Point(b0), Point(c), Point(d)) <= 0) continue;
                if (++flips > flipLimit) throw new InvalidOperationException("DelaunayKernel2: flips do not terminate");
                Flip(e, out _, out _);
                pending.Push(Key(c, a0)); pending.Push(Key(a0, d)); pending.Push(Key(d, b0)); pending.Push(Key(b0, c));
            }
        }

        /// <summary> every unconstrained real edge that is not locally Delaunay, as a key </summary>
        internal List<long> NonDelaunayEdges()
        {
            var result = new List<long>();
            for (int e = 0; e < 3 * TriangleSlots; ++e) {
                int f = H[e];
                if (f < e || Seg[e] >= 0 || IsGhost(e / 3) || IsGhost(f / 3)) continue;
                int a0 = V[e], b0 = V[Next(e)], c = V[Next(Next(e))], d = V[Next(Next(f))];
                if (ExactPredicates.InCircle(Point(a0), Point(b0), Point(c), Point(d)) > 0) result.Add(Key(a0, b0));
            }
            return result;
        }

        static long Key(int a, int b) => ((long)a << 32) | (uint)b;

        internal void SetConstraint(int e, int seg) { Seg[e] = seg; Seg[H[e]] = seg; }

        internal int NewConstraint(List<int> sources) { Sources.Add(sources); return Sources.Count - 1; }

        /// <summary> removes the constraint of an edge (before a crossing splits it), returning its sources </summary>
        internal List<int> Unconstrain(int e)
        {
            int s = Seg[e];
            Seg[e] = -1; Seg[H[e]] = -1;
            return s < 0 ? null : Sources[s];
        }
    }
}
