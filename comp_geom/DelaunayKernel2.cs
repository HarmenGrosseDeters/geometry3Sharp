// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace g3
{
    /// <summary>
    /// The incremental Delaunay kernel of the 2D mesher: Bowyer-Watson insertion (Bowyer 1981, Watson 1981) into a
    /// triangulation closed by ghost triangles with one vertex at infinity (Cheng, Dey, Shewchuk, "Delaunay Mesh
    /// Generation", 2012, chapter 3), points ordered by biased randomized insertion rounds (Amenta, Choi, Rote 2003)
    /// along a Hilbert curve, located by a visibility walk (Devillers, Pion, Teillaud 2002) from the last insertion.
    /// All decisions use the exact predicates; no coordinate is invented (no supertriangle).
    /// </summary>
    /// <remarks>
    /// Storage: triangle t has vertices V[3t..3t+2], counter-clockwise for a real triangle. Half-edge 3t+k runs from
    /// V[3t+k] to V[3t+(k+1)%3]; H[e] is its twin. Ghost triangles carry the vertex <see cref="Infinite"/>; a ghost
    /// (a, b, inf) lies on the outer side of the hull edge from a to b (the interior is to its right). The number of
    /// triangles, ghosts included, is 2 (vertices) - 2 at all times, so a cavity of k triangles is replaced by k + 2 and
    /// no slot is ever freed.
    /// </remarks>
    internal sealed partial class DelaunayKernel2
    {
        /// <summary> coordinates per vertex; vertices are numbered in insertion order </summary>
        internal double[] X, Y;
        /// <summary> per vertex: its input index </summary>
        internal readonly int[] Input;
        internal readonly int N;
        /// <summary> the vertex at infinity: N </summary>
        internal readonly int Infinite;
        internal int[] V, H;
        internal int TriangleSlots;
        /// <summary> per vertex: the vertex it became (itself, or the first inserted one with equal coordinates); -1 if not inserted </summary>
        internal readonly int[] Vertex;
        /// <summary> per vertex: the smallest input index with its coordinates (the identity reported to the caller) </summary>
        internal readonly int[] Representative;

        int[] mark, reject, startAt, stack, cavity, boundary;
        int stamp, hint = -1;
        /// <summary> on a failure: a vertex where it was found (the point being inserted, or a vertex of the defect) </summary>
        internal int FailureVertex = -1;
        uint walkState = 2463534242;
        /// <summary> triangles visited by the location walks of the insertion (a measure of the insertion order's locality) </summary>
        internal long WalkSteps;

        /// <param name="x">coordinates in insertion order</param>
        /// <param name="y">coordinates in insertion order</param>
        /// <param name="input">the input index of each point</param>
        internal DelaunayKernel2(double[] x, double[] y, int[] input)
        {
            X = x; Y = y; Input = input; N = x.Length; Infinite = N;
            Vertex = new int[N];
            Representative = new int[N];
            for (int i = 0; i < N; ++i) { Vertex[i] = -1; Representative[i] = input[i]; }
            int capacity = Math.Max(4, 2 * N);
            V = new int[3 * capacity];
            H = new int[3 * capacity];
            mark = new int[capacity];
            reject = new int[capacity];
            startAt = new int[N + 1];
            stack = new int[64];
            cavity = new int[64];
            boundary = new int[64];
        }

        internal bool IsGhost(int t) => V[3 * t] == Infinite || V[3 * t + 1] == Infinite || V[3 * t + 2] == Infinite;

        int Orient(int a, int b, int c) => ExactPredicates.Orient2d(new Vector2d(X[a], Y[a]), new Vector2d(X[b], Y[b]), new Vector2d(X[c], Y[c]));

        static int Orient(double ax, double ay, double bx, double by, double px, double py)
            => ExactPredicates.Orient2d(new Vector2d(ax, ay), new Vector2d(bx, by), new Vector2d(px, py));

        // ---------------------------------------------------------------- insertion order

        /// <summary>
        /// The insertion order: rounds of geometrically growing size chosen by a hash of the index (BRIO), each round sorted
        /// along a Hilbert curve on a 2^16 grid over the coordinate ranks. The order only affects speed, never the result
        /// (ties are canonicalized afterwards), so floating-point rounding in the keys decides nothing.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static int[] InsertionOrder(double[] x, double[] y)
        {
            int n = x.Length;
            var order = new int[n];
            if (n == 0) return order;
            // The curve runs over the ranks of the coordinates, not the coordinates: every column and row of the grid then holds
            // about as many points, whatever the distribution (clusters, outliers, many scales), and no floating-point division
            // can overflow. Equal coordinates get equal ranks, so the keys do not depend on how a sort breaks ties.
            var gx = GridRanks(x);
            var gy = GridRanks(y);
            int levels = 0;
            while ((1 << levels) < n && levels < 30) ++levels;
            var keys = new long[n];
            for (int i = 0; i < n; ++i) {
                long hilbert = HilbertIndex(gx[i], gy[i]);
                // round: the number of trailing zeros of a hash, so round r holds about n / 2^(r+1) points; larger rounds go last
                ulong h = Mix((ulong)i);
                int r = 0;
                while (r < levels && (h & 1) == 0) { h >>= 1; ++r; }
                keys[i] = ((long)(levels - r) << 32) | hilbert;
                order[i] = i;
            }
            Array.Sort(keys, order);
            SortTies(keys, order);
            return order;
        }

        /// <summary> per value: its rank among the values (equal values share the smallest), scaled to [0, 65535] </summary>
        static int[] GridRanks(double[] v)
        {
            int n = v.Length;
            var sorted = (double[])v.Clone();
            var index = new int[n];
            for (int i = 0; i < n; ++i) index[i] = i;
            Array.Sort(sorted, index);
            var grid = new int[n];
            int first = 0;
            for (int i = 0; i < n; ++i) {
                if (i > 0 && sorted[i] != sorted[i - 1]) first = i;
                grid[index[i]] = (int)((long)first * 65536 / n);
            }
            return grid;
        }

        /// <summary> orders each run of equal keys by index: Array.Sort is not stable, and runtimes may break ties differently </summary>
        static void SortTies(long[] keys, int[] items)
        {
            int n = keys.Length;
            for (int i = 0; i < n; ) {
                int j = i + 1;
                while (j < n && keys[j] == keys[i]) ++j;
                if (j - i > 1) Array.Sort(items, i, j - i);
                i = j;
            }
        }

        static ulong Mix(ulong z)
        {
            // splitmix64 finalizer
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary> position of (x, y) on the Hilbert curve of the 2^16 x 2^16 grid </summary>
        static long HilbertIndex(int x, int y)
        {
            long d = 0;
            for (int s = 1 << 15; s > 0; s >>= 1) {
                int rx = (x & s) > 0 ? 1 : 0, ry = (y & s) > 0 ? 1 : 0;
                d += (long)s * s * ((3 * rx) ^ ry);
                if (ry == 0) {
                    if (rx == 1) { x = s - 1 - x; y = s - 1 - y; }
                    int t = x; x = y; y = t;
                }
            }
            return d;
        }

        // ---------------------------------------------------------------- construction

        /// <summary>
        /// Inserts all points in their order. Returns false if they span no area (fewer than three distinct points, or
        /// all collinear): no triangle exists then.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal bool Build(CancellationToken cancellationToken)
        {
            int n = N;
            if (n < 3) return false;
            // the first triangle: the first point, the first one distinct from it, the first one off their line
            int a = 0, b = -1, c = -1, ib = -1, ic = -1;
            for (int p = 1; p < n; ++p) {
                if (X[p] != X[a] || Y[p] != Y[a]) { b = p; ib = p; break; }
            }
            if (b < 0) return false;
            for (int p = ib + 1; p < n; ++p) {
                if (Orient(a, b, p) != 0) { c = p; ic = p; break; }
            }
            if (c < 0) return false;
            if (Orient(a, b, c) < 0) { int t = b; b = c; c = t; }
            CreateFirst(a, b, c);
            for (int i = 1; i < n; ++i) {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (i == ib || i == ic) continue;
                Insert(i);
            }
            return true;
        }

        void CreateFirst(int a, int b, int c)
        {
            // triangle 0 = (a, b, c); ghosts 1, 2, 3 on edges a-b, b-c, c-a
            int inf = Infinite;
            SetTriangle(0, a, b, c);
            SetTriangle(1, b, a, inf);
            SetTriangle(2, c, b, inf);
            SetTriangle(3, a, c, inf);
            Link(0, 3);     // a->b with b->a
            Link(1, 6);     // b->c with c->b
            Link(2, 9);     // c->a with a->c
            Link(4, 11);    // ghost1 a->inf with ghost3 inf->a
            Link(5, 7);     // ghost1 inf->b with ghost2 b->inf
            Link(8, 10);    // ghost2 inf->c with ghost3 c->inf
            TriangleSlots = 4;
            Vertex[a] = a; Vertex[b] = b; Vertex[c] = c;
            hint = 0;
        }

        void SetTriangle(int t, int a, int b, int c) { V[3 * t] = a; V[3 * t + 1] = b; V[3 * t + 2] = c; }
        void Link(int e, int f) { H[e] = f; H[f] = e; }

        static int Next(int e) => e % 3 == 2 ? e - 2 : e + 1;

        /// <summary>
        /// Inserts point p. An exact duplicate of a vertex is mapped to it (and the vertex's representative becomes the
        /// smaller input index).
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal void Insert(int p)
        {
            FailureVertex = p;
            int t = Locate(p, out int duplicate);
            if (duplicate >= 0) {
                Vertex[p] = duplicate;
                if (Input[p] < Representative[duplicate]) Representative[duplicate] = Input[p];
                return;
            }
            Vertex[p] = p;
            Cavity(t, p);
        }

        /// <summary>
        /// A triangle in conflict with p: the real triangle that contains it (closed), or the ghost of a hull edge that sees
        /// it strictly. Sets duplicate to the vertex with p's coordinates if there is one.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        int Locate(int p, out int duplicate)
        {
            duplicate = -1;
            double px = X[p], py = Y[p];
            int t = hint, entry = -1;
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
                        if (X[u] == px && Y[u] == py) { duplicate = u; return t; }
                    }
                    return t;
                }
                ++WalkSteps;
                entry = H[moved];
                t = entry / 3;
                if (IsGhost(t)) return t;   // stepped over a hull edge that sees p strictly
            }
            // not reached in a Delaunay triangulation; a linear search is still correct
            return LocateByScan(p, out duplicate);
        }

        int LocateByScan(int p, out int duplicate)
        {
            duplicate = -1;
            for (int t = 0; t < TriangleSlots; ++t) {
                if (IsGhost(t)) continue;
                bool inside = true;
                for (int k = 0; k < 3 && inside; ++k)
                    if (Orient(V[3 * t + k], V[3 * t + (k + 1) % 3], p) < 0) inside = false;
                if (!inside) continue;
                for (int k = 0; k < 3; ++k) {
                    int u = V[3 * t + k];
                    if (X[u] == X[p] && Y[u] == Y[p]) { duplicate = u; break; }
                }
                return t;
            }
            for (int t = 0; t < TriangleSlots; ++t)
                if (IsGhost(t) && Conflict(t, p)) return t;
            throw new InvalidOperationException("DelaunayKernel2: no triangle in conflict with a point");
        }

        /// <summary>
        /// p lies strictly inside the circumcircle of real triangle t; for a ghost (a, b, inf): p lies strictly on the outer
        /// side of a-b, or in the open segment a-b (the limit of the circles through a, b).
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        bool Conflict(int t, int p)
        {
            int i = 3 * t;
            int a = V[i], b = V[i + 1], c = V[i + 2];
            var pp = new Vector2d(X[p], Y[p]);
            if (c == Infinite) return GhostConflict(a, b, pp);
            if (a == Infinite) return GhostConflict(b, c, pp);
            if (b == Infinite) return GhostConflict(c, a, pp);
            return ExactPredicates.InCircle(new Vector2d(X[a], Y[a]), new Vector2d(X[b], Y[b]), new Vector2d(X[c], Y[c]), pp) > 0;
        }

        bool GhostConflict(int a, int b, Vector2d p)
        {
            int o = Orient(X[a], Y[a], X[b], Y[b], p.x, p.y);
            if (o != 0) return o > 0;
            return X[a] != X[b] ? Between(X[a], X[b], p.x) : Between(Y[a], Y[b], p.y);
        }

        static bool Between(double a, double b, double v) => a < b ? a < v && v < b : b < v && v < a;

        /// <summary> Removes the triangles in conflict with p (a star-shaped region around it) and connects p to its boundary. </summary>
        [MethodImpl(JitHints.Optimize)]
        void Cavity(int t0, int p)
        {
            if (++stamp == int.MaxValue) { Array.Clear(mark, 0, mark.Length); Array.Clear(reject, 0, reject.Length); stamp = 1; }
            int sp = 0, nc = 0, nb = 0;
            mark[t0] = stamp;
            stack[sp++] = t0;
            while (sp > 0) {
                int t = stack[--sp];
                if (nc == cavity.Length) Array.Resize(ref cavity, 2 * nc);
                cavity[nc++] = t;
                for (int k = 0; k < 3; ++k) {
                    int e = 3 * t + k, t2 = H[e] / 3;
                    if (mark[t2] == stamp) continue;
                    if (reject[t2] != stamp) {
                        if (Conflict(t2, p)) {
                            mark[t2] = stamp;
                            if (sp == stack.Length) Array.Resize(ref stack, 2 * sp);
                            stack[sp++] = t2;
                            continue;
                        }
                        reject[t2] = stamp;
                    }
                    if (nb == boundary.Length) Array.Resize(ref boundary, 2 * nb);
                    boundary[nb++] = e;
                }
            }
            if (nb != nc + 2) throw new InvalidOperationException("DelaunayKernel2: the cavity is not a disk");
            EnsureTemp(nb);
            // new triangles: the cavity's slots, then two new ones
            int slots = TriangleSlots;
            if (slots + 2 > V.Length / 3) Grow();
            int real = -1;
            for (int i = 0; i < nb; ++i) {
                int e = boundary[i];
                int u = V[e], v = V[Next(e)], outside = H[e];
                int s = i < nc ? cavity[i] : slots + (i - nc);
                // (boundary[] holds half-edges of cavity triangles: all are read here before any slot is rewritten)
                startAt[u] = s;
                tmpU[i] = u; tmpV[i] = v; tmpOut[i] = outside;
                if (u != Infinite && v != Infinite) real = s;
            }
            for (int i = 0; i < nb; ++i) {
                int s = i < nc ? cavity[i] : slots + (i - nc);
                SetTriangle(s, tmpU[i], tmpV[i], p);
                Link(3 * s, tmpOut[i]);
            }
            for (int i = 0; i < nb; ++i) {
                int s = i < nc ? cavity[i] : slots + (i - nc);
                int s2 = startAt[tmpV[i]];
                Link(3 * s + 1, 3 * s2 + 2);
            }
            TriangleSlots = slots + 2;
            if (real >= 0) hint = real;
        }

        int[] tmpU = new int[64], tmpV = new int[64], tmpOut = new int[64];

        void Grow()
        {
            int capacity = 2 * (V.Length / 3);
            Array.Resize(ref V, 3 * capacity);
            Array.Resize(ref H, 3 * capacity);
            Array.Resize(ref mark, capacity);
            Array.Resize(ref reject, capacity);
        }

        // keeps tmp arrays as large as the boundary array
        void EnsureTemp(int size)
        {
            if (tmpU.Length >= size) return;
            Array.Resize(ref tmpU, size); Array.Resize(ref tmpV, size); Array.Resize(ref tmpOut, size);
        }

        // ---------------------------------------------------------------- ties

        /// <summary>
        /// Makes the result independent of the insertion order (decision D18): every Delaunay cell with more than three
        /// vertices (cocircular points, the triangles joined by edges whose four points are cocircular) is re-triangulated as
        /// a fan from its vertex with the smallest representative index. Also verifies the local Delaunay property of every
        /// interior edge and the orientation of every triangle. Returns the number of edges with four cocircular points
        /// before the fan, or -1 if the verification failed.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal int CanonicalizeTies(out string failure)
        {
            failure = null;
            FailureVertex = -1;
            int slots = TriangleSlots;
            int[] parent = null;
            int ties = 0;
            for (int t = 0; t < slots; ++t) {
                if (IsGhost(t)) continue;
                int i = 3 * t;
                var a = P(V[i]); var b = P(V[i + 1]); var c = P(V[i + 2]);
                if (ExactPredicates.Orient2d(a, b, c) <= 0) { failure = "triangle " + t + " is not counter-clockwise"; FailureVertex = V[i]; return -1; }
                for (int k = 0; k < 3; ++k) {
                    int e = i + k, f = H[e];
                    if (H[f] != e || V[f] != V[Next(e)] || V[Next(f)] != V[e]) { failure = "half-edge " + e + " has no matching twin"; FailureVertex = V[e]; return -1; }
                    if (f < e) continue;
                    int t2 = f / 3;
                    if (IsGhost(t2)) continue;
                    var d = P(V[Next(Next(f))]);
                    int s = ExactPredicates.InCircle(a, b, c, d);
                    if (s > 0) { failure = "edge " + V[e] + "-" + V[Next(e)] + " is not locally Delaunay"; FailureVertex = V[e]; return -1; }
                    if (s == 0) {
                        if (parent == null) { parent = new int[slots]; for (int j = 0; j < slots; ++j) parent[j] = j; }
                        Union(parent, t, t2);
                        ++ties;
                    }
                }
            }
            if (!HullIsConvex(out failure)) return -1;
            if (ties == 0) return 0;
            // group the triangles of each cell
            var count = new int[slots + 1];
            for (int t = 0; t < slots; ++t) if (!IsGhost(t)) count[Find(parent, t) + 1]++;
            for (int t = 0; t < slots; ++t) count[t + 1] += count[t];
            var members = new int[count[slots]];
            var fill = (int[])count.Clone();
            for (int t = 0; t < slots; ++t) if (!IsGhost(t)) members[fill[Find(parent, t)]++] = t;
            var nextVertex = new int[N + 1];
            var nextOutside = new int[N + 1];
            for (int root = 0; root < slots; ++root) {
                int from = count[root], to = count[root + 1];
                if (to - from < 2) continue;
                Fan(parent, members, from, to, nextVertex, nextOutside);
            }
            return ties;
        }

        Vector2d P(int v) => new Vector2d(X[v], Y[v]);

        static int Find(int[] parent, int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        static void Union(int[] parent, int a, int b)
        {
            a = Find(parent, a); b = Find(parent, b);
            if (a == b) return;
            if (a < b) parent[b] = a; else parent[a] = b;
        }

        void Fan(int[] parent, int[] members, int from, int to, int[] nextVertex, int[] nextOutside)
        {
            int root = Find(parent, members[from]);
            int start = -1, nb = 0;
            for (int m = from; m < to; ++m) {
                int t = members[m];
                for (int k = 0; k < 3; ++k) {
                    int e = 3 * t + k, f = H[e];
                    int t2 = f / 3;
                    if (!IsGhost(t2) && Find(parent, t2) == root) continue;
                    int u = V[e];
                    nextVertex[u] = V[Next(e)];
                    nextOutside[u] = f;
                    if (start < 0 || Representative[u] < Representative[start]) start = u;
                    ++nb;
                }
            }
            int cells = to - from;
            if (nb != cells + 2) throw new InvalidOperationException("DelaunayKernel2: a cocircular cell is not a disk");
            // polygon w[0] = start, w[1], ... counter-clockwise; triangles (start, w[i], w[i+1]) for i = 1 .. nb-2
            int prevSlot = -1;
            int w1 = nextVertex[start];
            int firstOutside = nextOutside[start];
            int cur = w1;
            for (int i = 0; i < cells; ++i) {
                int s = members[from + i];
                int nxt = nextVertex[cur];
                SetTriangle(s, start, cur, nxt);
                Link(3 * s + 1, nextOutside[cur]);
                if (i == 0) Link(3 * s, firstOutside);
                else Link(3 * s, 3 * prevSlot + 2);
                if (i == cells - 1) Link(3 * s + 2, nextOutside[nxt]);
                prevSlot = s;
                cur = nxt;
            }
        }

        /// <summary>
        /// The ghosts form one cycle around the vertex at infinity, the hull turns left or goes straight at every vertex,
        /// and it winds once (exactly one lexicographically smallest local minimum).
        /// </summary>
        bool HullIsConvex(out string failure)
        {
            failure = null;
            int g = -1;
            for (int t = 0; t < TriangleSlots; ++t) if (IsGhost(t)) { g = t; break; }
            if (g < 0) { failure = "no hull"; return false; }
            // walk the hull: from ghost (a, b, inf), the next ghost is across the edge b -> inf... rotate around inf
            int count = 0, minima = 0, total = 0;
            for (int t = 0; t < TriangleSlots; ++t) if (IsGhost(t)) ++total;
            int cur = g;
            do {
                HullEdge(cur, out int a, out int b, out int eInfToA);
                // next ghost: the one sharing edge b - inf
                int next = NextGhost(cur);
                HullEdge(next, out int b2, out int c, out _);
                if (b2 != b) { failure = "hull ghosts are not linked"; FailureVertex = b; return false; }
                if (Orient(a, b, c) > 0) { failure = "hull is not convex at vertex " + b; FailureVertex = b; return false; }
                if (Less(b, a) && Less(b, c)) ++minima;
                cur = next;
                if (++count > total) { failure = "hull cycle too long"; return false; }
            } while (cur != g);
            if (count != total) { failure = "hull ghosts form several cycles"; return false; }
            if (minima != 1) { failure = "hull winds " + minima + " times"; return false; }
            return true;
        }

        bool Less(int u, int v) => X[u] < X[v] || (X[u] == X[v] && Y[u] < Y[v]);

        /// <summary> the hull edge (a, b) of a ghost, in the ghost's order (interior to the right of a -> b) </summary>
        void HullEdge(int g, out int a, out int b, out int infEdge)
        {
            int i = 3 * g;
            int k = V[i] == Infinite ? 0 : V[i + 1] == Infinite ? 1 : 2;
            a = V[i + (k + 1) % 3]; b = V[i + (k + 2) % 3];
            infEdge = i + k;   // inf -> a
        }

        /// <summary> the ghost after g along the hull: across g's half-edge b -> inf </summary>
        int NextGhost(int g)
        {
            int i = 3 * g;
            int k = V[i] == Infinite ? 0 : V[i + 1] == Infinite ? 1 : 2;
            int bToInf = i + (k + 2) % 3;
            return H[bToInf] / 3;
        }
    }
}
