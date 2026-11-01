// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace g3
{
    /// <summary>
    /// Constrained Delaunay triangulation of points, segments, rings, holes and regions, with exact predicates; the output
    /// coordinates are doubles and the mesh is valid on them (decision D1).
    /// </summary>
    /// <remarks>
    /// <para>Crossing, touching and overlapping constraints are split at their intersections (noding, spec O10). A crossing is
    /// computed exactly and rounded to the nearest double; the pieces then run through it, at most a few units in the last
    /// place off their input segment. A vertex within the automatic tolerance (2^-41 of the larger of the largest coordinate
    /// and the extent, about 2.6 um at UTM northings; spec O11, Q14) of a segment becomes a vertex of it, so every piece stays
    /// within that tolerance of each of its input segments. With <see cref="ResolveCrossings"/> off, a crossing gives
    /// <see cref="Triangulation2FailureKind.CrossingConstraints"/>.</para>
    /// <para>The domain is the convex hull without outer rings, else the union of the outer rings minus the union of the hole
    /// rings, each ring by the even-odd rule (decision D5); a hole removes everything inside it, constraints included (Q7).
    /// Hole seeds remove the bounded cell of the constraints around them (D6). Regions by ring or seed label the triangles,
    /// the last one wins (I6, D16).</para>
    /// <para>Vertex ids: input point i is vertex i; crossings get ids from the input count on (Q8). Deterministic; independent
    /// instances may run concurrently.</para>
    /// </remarks>
    public sealed class ConstrainedDelaunayMesh2
    {
        /// <summary> Split crossing and overlapping constraints (default). Off: a crossing is a failure, never an invalid mesh. </summary>
        public bool ResolveCrossings = true;

        /// <summary> The automatic tolerance as a fraction of the larger of the largest coordinate and the extent. </summary>
        public const double AutoToleranceFactor = 1.0 / (1L << 41);

        public ConstrainedTriangulation2Result Compute(ConstrainedMesh2Input input, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var builder = new Builder(input, ResolveCrossings, cancellationToken);
            return builder.Run();
        }

        internal static Vector2d IntersectionForTests(Vector2d a, Vector2d b, Vector2d c, Vector2d d) => Builder.Intersection(a, b, c, d);

        /// <summary> an input piece of a constraint and where it came from </summary>
        internal struct Source
        {
            internal int Ring;        // ring id (outer and hole rings, then region rings), or -1
            internal int Marker;
            internal bool Locked;
            internal double AX, AY, BX, BY;
        }

        sealed class Builder
        {
            readonly ConstrainedMesh2Input input;
            readonly bool resolve;
            readonly CancellationToken cancel;
            readonly List<string> diagnostics = new List<string>();
            readonly List<Source> sources = new List<Source>();
            DelaunayKernel2 k;
            int n;
            int[] order, rank;
            double tolerance;
            double[] heights;     // per kernel vertex (ranks, then new vertices), if any
            int outerRings, holeRings, regionRings;
            bool[] ringIsHole;

            internal Builder(ConstrainedMesh2Input input, bool resolve, CancellationToken cancel)
            {
                this.input = input; this.resolve = resolve; this.cancel = cancel;
            }

            ConstrainedTriangulation2Result Fail(Triangulation2FailureKind kind, Vector2d? where, string detail)
                => new ConstrainedTriangulation2Result(Triangulation2Status.Failed, kind, null, diagnostics, where, detail);

            internal ConstrainedTriangulation2Result Run()
            {
                Validate();
                n = input.Points.Count;
                var x = new double[n];
                var y = new double[n];
                var vertices = new Vector2d[n];
                double maxAbs = 0, minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
                for (int i = 0; i < n; ++i) {
                    var p = input.Points[i];
                    x[i] = p.x == 0 ? 0.0 : p.x; y[i] = p.y == 0 ? 0.0 : p.y;
                    vertices[i] = new Vector2d(x[i], y[i]);
                    maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(x[i]), Math.Abs(y[i])));
                    minX = Math.Min(minX, x[i]); maxX = Math.Max(maxX, x[i]); minY = Math.Min(minY, y[i]); maxY = Math.Max(maxY, y[i]);
                }
                tolerance = n == 0 ? 0 : Math.Max(maxAbs, Math.Max(maxX - minX, maxY - minY)) * AutoToleranceFactor;
                cancel.ThrowIfCancellationRequested();
                order = DelaunayKernel2.InsertionOrder(x, y);
                rank = new int[n];
                var xs = new double[n]; var ys = new double[n];
                for (int r = 0; r < n; ++r) { xs[r] = x[order[r]]; ys[r] = y[order[r]]; rank[order[r]] = r; }
                k = new DelaunayKernel2(xs, ys, order);
                bool area;
                try {
                    area = k.Build(cancel);
                } catch (InvalidOperationException e) {
                    return Fail(Triangulation2FailureKind.InvalidTopology, null, e.Message);
                }
                var mapping = new int[n];
                if (!area) {
                    var first = new Dictionary<Vector2d, int>();
                    for (int i = 0; i < n; ++i) {
                        if (!first.TryGetValue(vertices[i], out int f)) first.Add(vertices[i], f = i);
                        mapping[i] = f;
                    }
                    var empty = new ConstrainedMesh2(vertices, n, mapping, new Index3i[0], new Index3i[0], new int[0], new Mesh2Subsegment[0],
                        input.Heights == null ? null : input.Heights.ToArray(), new int[0], new int[0], new int[0], new Vector2d[0], 0);
                    return new ConstrainedTriangulation2Result(Triangulation2Status.Empty, Triangulation2FailureKind.None, empty, diagnostics);
                }
                for (int i = 0; i < n; ++i) mapping[i] = k.Representative[k.Vertex[rank[i]]];
                k.BeginConstraints();
                InitHeights();
                try {
                    var failure = InsertConstraints();
                    if (failure != null) return failure;
                } catch (InvalidOperationException e) {
                    return Fail(Triangulation2FailureKind.InvalidTopology, null, e.Message);
                }
                cancel.ThrowIfCancellationRequested();
                return Output(vertices, mapping);
            }

            void Validate()
            {
                var pts = input.Points ?? throw new ArgumentException("input.Points is null");
                for (int i = 0; i < pts.Count; ++i) DelaunayMesh2.CheckCoordinate(pts[i].x, pts[i].y, "points[" + i + "]");
                void Index(int v, string what)
                {
                    if (v < 0 || v >= pts.Count) throw new ArgumentException(what + " refers to point " + v + ", which does not exist (" + pts.Count + " points)");
                }
                if (input.Segments != null)
                    for (int i = 0; i < input.Segments.Count; ++i) {
                        var s = input.Segments[i] ?? throw new ArgumentException("segments[" + i + "] is null");
                        Index(s.A, "segments[" + i + "].A"); Index(s.B, "segments[" + i + "].B");
                    }
                if (input.Rings != null)
                    for (int i = 0; i < input.Rings.Count; ++i) {
                        var r = input.Rings[i] ?? throw new ArgumentException("rings[" + i + "] is null");
                        if (r.Vertices == null) throw new ArgumentException("rings[" + i + "] has no vertices");
                        foreach (int v in r.Vertices) Index(v, "rings[" + i + "]");
                    }
                if (input.HoleSeeds != null)
                    for (int i = 0; i < input.HoleSeeds.Count; ++i) DelaunayMesh2.CheckCoordinate(input.HoleSeeds[i].x, input.HoleSeeds[i].y, "holeSeeds[" + i + "]");
                if (input.Regions != null)
                    for (int i = 0; i < input.Regions.Count; ++i) {
                        var r = input.Regions[i] ?? throw new ArgumentException("regions[" + i + "] is null");
                        if (r.Ring != null) foreach (int v in r.Ring) Index(v, "regions[" + i + "]");
                        else DelaunayMesh2.CheckCoordinate(r.Seed.x, r.Seed.y, "regions[" + i + "].Seed");
                    }
                if (input.Heights != null) {
                    if (input.Heights.Count != pts.Count) throw new ArgumentException("heights: one per point is needed");
                    for (int i = 0; i < input.Heights.Count; ++i)
                        if (double.IsNaN(input.Heights[i]) || double.IsInfinity(input.Heights[i])) throw new ArgumentException("heights[" + i + "] is not finite");
                }
            }

            int KV(int inputIndex) => k.Vertex[rank[inputIndex]];

            void InitHeights()
            {
                if (input.Heights == null) return;
                heights = new double[Math.Max(16, n + 1)];
                var set = new bool[n];
                for (int i = 0; i < n; ++i) {
                    int v = KV(i);
                    double z = input.Heights[i];
                    // the lowest index wins (decision D11): Representative is the lowest index of the vertex
                    if (k.Representative[v] == i) { heights[v] = z; set[v] = true; }
                }
                for (int i = 0; i < n; ++i) {
                    int v = KV(i);
                    if (k.Representative[v] != i && input.Heights[i] != heights[v])
                        diagnostics.Add("point " + i + " coincides with point " + k.Representative[v] + " but has another height; the lower index wins");
                }
            }

            void SetHeight(int v, int u, int w)
            {
                if (heights == null) return;
                if (v >= heights.Length) Array.Resize(ref heights, Math.Max(v + 1, 2 * heights.Length));
                // linear along the piece u-w that v splits
                double dx = k.X[w] - k.X[u], dy = k.Y[w] - k.Y[u], l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? ((k.X[v] - k.X[u]) * dx + (k.Y[v] - k.Y[u]) * dy) / l2 : 0;
                t = Math.Max(0, Math.Min(1, t));
                heights[v] = heights[u] + t * (heights[w] - heights[u]);
            }

            struct Piece
            {
                internal int U, W;
                internal List<int> Sources;
            }

            ConstrainedTriangulation2Result InsertConstraints()
            {
                var work = new Stack<Piece>();
                void Add(int a, int b, Source s)
                {
                    int u = KV(a), w = KV(b);
                    sources.Add(s);
                    if (u != w) work.Push(new Piece { U = u, W = w, Sources = new List<int> { sources.Count - 1 } });
                }
                Source Make(int a, int b, int ring, int marker, bool locked)
                    => new Source { Ring = ring, Marker = marker, Locked = locked, AX = k.X[KV(a)], AY = k.Y[KV(a)], BX = k.X[KV(b)], BY = k.Y[KV(b)] };
                // rings: outer and hole rings, then region rings, each with an id
                var rings = input.Rings ?? new List<Mesh2Ring>();
                var regions = input.Regions ?? new List<Mesh2Region>();
                ringIsHole = new bool[rings.Count + regions.Count];
                for (int r = 0; r < rings.Count; ++r) {
                    var ring = rings[r];
                    if (ring.Hole) { ringIsHole[r] = true; ++holeRings; } else ++outerRings;
                    var vs = ring.Vertices;
                    for (int i = 0; i < vs.Length; ++i) {
                        int a = vs[i], b = vs[(i + 1) % vs.Length];
                        Add(a, b, Make(a, b, r, ring.Marker, false));
                    }
                }
                for (int g = 0; g < regions.Count; ++g) {
                    var vs = regions[g].Ring;
                    if (vs == null) continue;
                    ++regionRings;
                    for (int i = 0; i < vs.Length; ++i) {
                        int a = vs[i], b = vs[(i + 1) % vs.Length];
                        Add(a, b, Make(a, b, rings.Count + g, 0, false));
                    }
                }
                if (input.Segments != null)
                    foreach (var s in input.Segments) Add(s.A, s.B, Make(s.A, s.B, -1, s.Marker, s.Locked));
                // in input order: reverse the stack once
                var pending = new Stack<Piece>(work);
                // Each crossing of two input segments makes at most one vertex, so more new vertices than pairs of sources mean the
                // rounded crossings feed on each other; and the steps per vertex are bounded. Both limits only stop a runaway.
                long m = sources.Count, vertexLimit = m * (m - 1) / 2 + 2 * m + 1000, steps = 0;
                while (pending.Count > 0) {
                    if ((++steps & 255) == 0) cancel.ThrowIfCancellationRequested();
                    long made = k.VertexCount - (k.N + 1);
                    if (made > vertexLimit || steps > 64L * (m + n + made) + 100000)
                        return Fail(Triangulation2FailureKind.NodingLimit, null, "noding did not settle: " + made + " vertices made in " + steps + " steps");
                    var piece = pending.Pop();
                    var failure = InsertPiece(piece, pending);
                    if (failure != null) return failure;
                }
                return null;
            }

            /// <summary> v is within the tolerance of the open segment u-w and of every source of the piece </summary>
            bool Near(int v, int u, int w, List<int> src)
            {
                if (tolerance <= 0) return false;
                double px = k.X[v], py = k.Y[v];
                if (!WithinTolerance(px, py, k.X[u], k.Y[u], k.X[w], k.Y[w])) return false;
                foreach (int s in src) {
                    var q = sources[s];
                    if (!WithinTolerance(px, py, q.AX, q.AY, q.BX, q.BY)) return false;
                }
                return true;
            }

            bool WithinTolerance(double px, double py, double ax, double ay, double bx, double by)
            {
                double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
                if (!(l2 > 0)) return false;
                double t = ((px - ax) * dx + (py - ay) * dy) / l2;
                if (!(t > 0 && t < 1)) return false;
                double cx = ax + t * dx - px, cy = ay + t * dy - py;
                return cx * cx + cy * cy <= tolerance * tolerance;
            }

            void Split(int v, Piece piece, Stack<Piece> pending)
            {
                pending.Push(new Piece { U = v, W = piece.W, Sources = piece.Sources });
                pending.Push(new Piece { U = piece.U, W = v, Sources = new List<int>(piece.Sources) });
            }

            ConstrainedTriangulation2Result InsertPiece(Piece piece, Stack<Piece> pending)
            {
                int u = piece.U, w = piece.W;
                if (u == w) return null;
                int existing = k.FindEdge(u, w);
                if (existing >= 0) {
                    if (k.Seg[existing] < 0) k.SetConstraint(existing, k.NewConstraint(piece.Sources));
                    else k.Sources[k.Seg[existing]].AddRange(piece.Sources);
                    return null;
                }
                // the triangle at u whose wedge holds the direction to w
                int h = -1;
                foreach (int e in k.Star(u)) {
                    if (k.IsGhost(e / 3)) continue;
                    int a = k.V[Next(e)], b = k.V[Next(Next(e))];
                    int oa = k.OrientV(u, w, a), ob = k.OrientV(u, w, b);
                    if (oa == 0 && Ahead(u, w, a)) { Split(a, piece, pending); return null; }
                    if (ob == 0 && Ahead(u, w, b)) { Split(b, piece, pending); return null; }
                    if (oa < 0 && ob > 0) { h = Next(e); break; }
                }
                if (h < 0) throw new InvalidOperationException("ConstrainedDelaunayMesh2: no triangle at a segment's end point");
                {
                    int a = k.V[h], b = k.V[Next(h)];
                    if (Near(a, u, w, piece.Sources)) { Split(a, piece, pending); return null; }
                    if (Near(b, u, w, piece.Sources)) { Split(b, piece, pending); return null; }
                }
                var crossed = new List<long>();
                while (true) {
                    int right = k.V[h], left = k.V[Next(h)];
                    if (k.Seg[h] >= 0) return Cross(piece, h, pending);
                    crossed.Add(((long)right << 32) | (uint)left);
                    int g = k.H[h];
                    int v = k.V[Next(Next(g))];
                    if (v == w) break;
                    int ov = k.OrientV(u, w, v);
                    if (ov == 0 || Near(v, u, w, piece.Sources)) { Split(v, piece, pending); return null; }
                    h = ov > 0 ? Next(g) : Next(Next(g));
                }
                int edge = k.RecoverEdge(u, w, crossed);
                k.SetConstraint(edge, k.NewConstraint(piece.Sources));
                return null;
            }

            static int Next(int e) => e % 3 == 2 ? e - 2 : e + 1;

            bool Ahead(int u, int w, int a)
            {
                double dx = k.X[w] - k.X[u], dy = k.Y[w] - k.Y[u];
                return (k.X[a] - k.X[u]) * dx + (k.Y[a] - k.Y[u]) * dy > 0;
            }

            /// <summary> the piece crosses the constrained edge of half-edge h: split both at the rounded crossing </summary>
            ConstrainedTriangulation2Result Cross(Piece piece, int h, Stack<Piece> pending)
            {
                int u = piece.U, w = piece.W, r = k.V[h], l = k.V[Next(h)];
                var x = Intersection(k.Point(u), k.Point(w), k.Point(r), k.Point(l));
                if (!resolve) return Fail(Triangulation2FailureKind.CrossingConstraints, x, "constraints cross and ResolveCrossings is off");
                var other = k.Unconstrain(h);
                int p = k.InsertPoint(x.x, x.y);
                if (p > k.N) SetHeight(p, u, w);
                pending.Push(new Piece { U = r, W = p, Sources = other });
                pending.Push(new Piece { U = p, W = l, Sources = new List<int>(other) });
                pending.Push(new Piece { U = p, W = w, Sources = piece.Sources });
                pending.Push(new Piece { U = u, W = p, Sources = new List<int>(piece.Sources) });
                return null;
            }

            /// <summary>
            /// The crossing of segments a-b and c-d (not parallel), computed exactly and rounded to a double point: every double is
            /// an integer times a power of two, so with a common exponent the crossing is a ratio of integers.
            /// </summary>
            internal static Vector2d Intersection(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
            {
                var m = new BigInteger[8];
                var ex = new int[8];
                double[] v = { a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y };
                int emin = int.MaxValue;
                for (int i = 0; i < 8; ++i) {
                    ExactPoint3.Decompose(v[i], out m[i], out ex[i]);
                    if (!m[i].IsZero) emin = Math.Min(emin, ex[i]);
                }
                if (emin == int.MaxValue) emin = 0;
                var q = new BigInteger[8];
                for (int i = 0; i < 8; ++i) q[i] = m[i].IsZero ? BigInteger.Zero : m[i] << (ex[i] - emin);
                BigInteger ax = q[0], ay = q[1], bx = q[2], by = q[3], cx = q[4], cy = q[5], dx = q[6], dy = q[7];
                // t = cross(c - a, d - c) / cross(b - a, d - c)
                BigInteger den = (bx - ax) * (dy - cy) - (by - ay) * (dx - cx);
                BigInteger num = (cx - ax) * (dy - cy) - (cy - ay) * (dx - cx);
                if (den.Sign < 0) { den = -den; num = -num; }
                BigInteger xn = ax * den + num * (bx - ax), yn = ay * den + num * (by - ay);
                return new Vector2d(RatioToDouble(xn, den, emin), RatioToDouble(yn, den, emin));
            }

            /// <summary>
            /// num / den * 2^scale (den > 0) rounded to nearest, ties to even, in integer arithmetic only: no conversion of a large
            /// BigInteger to double, whose rounding may differ between runtimes. (Below the normal range a second rounding to the
            /// subnormal grid follows, still within one unit in the last place.)
            /// </summary>
            internal static double RatioToDouble(BigInteger num, BigInteger den, int scale)
            {
                if (num.IsZero) return 0;
                bool negative = num.Sign < 0;
                if (negative) num = -num;
                long shift = 55 - (ExactPoint3.BitLength(num) - ExactPoint3.BitLength(den));
                BigInteger q = shift >= 0 ? BigInteger.DivRem(num << (int)shift, den, out BigInteger rem) : BigInteger.DivRem(num, den << (int)-shift, out rem);
                // q has 55 or 56 bits; keep 53, round with the dropped bits and the remainder as sticky
                int drop = (int)ExactPoint3.BitLength(q) - 53;
                BigInteger kept = q >> drop, dropped = q - (kept << drop), half = BigInteger.One << (drop - 1);
                int c = dropped.CompareTo(half);
                if (c > 0 || (c == 0 && !rem.IsZero) || (c == 0 && !kept.IsEven)) kept += 1;
                double mantissa = (double)(long)kept;   // exact: at most 2^53
                double r = ExactPoint3.ScaleB(mantissa, (int)(scale - shift + drop));
                return negative ? -r : r;
            }

            // ------------------------------------------------------------ domain, labels, output

            ConstrainedTriangulation2Result Output(Vector2d[] inputVertices, int[] mapping)
            {
                int slots = k.TriangleSlots;
                // per constraint: the rings it toggles (odd multiplicity), as a sorted array
                var toggles = new int[k.Sources.Count][];
                var count = new Dictionary<int, int>();
                for (int c = 0; c < toggles.Length; ++c) {
                    count.Clear();
                    foreach (int s in k.Sources[c]) {
                        int ring = sources[s].Ring;
                        if (ring < 0) continue;
                        count.TryGetValue(ring, out int m);
                        count[ring] = m + 1;
                    }
                    toggles[c] = count.Where(p => (p.Value & 1) == 1).Select(p => p.Key).OrderBy(z => z).ToArray();
                }
                // the rings each triangle lies inside of (odd parity), by a breadth-first search from the outside; sets are interned
                var sets = new List<int[]> { new int[0] };
                var index = new Dictionary<string, int> { { "", 0 } };
                int Intern(int[] set)
                {
                    string key = string.Join(",", set);
                    if (!index.TryGetValue(key, out int id)) { id = sets.Count; sets.Add(set); index.Add(key, id); }
                    return id;
                }
                var state = new int[slots];
                for (int t = 0; t < slots; ++t) state[t] = -1;
                var queue = new Queue<int>();
                for (int t = 0; t < slots; ++t) if (k.IsGhost(t)) { state[t] = 0; queue.Enqueue(t); }
                var xorCache = new Dictionary<long, int>();
                while (queue.Count > 0) {
                    int t = queue.Dequeue();
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j, t2 = k.H[e] / 3;
                        if (state[t2] >= 0) continue;
                        int c = k.Seg[e];
                        if (c < 0 || toggles[c].Length == 0) state[t2] = state[t];
                        else {
                            long key = ((long)state[t] << 32) | (uint)c;
                            if (!xorCache.TryGetValue(key, out int s2)) xorCache.Add(key, s2 = Intern(Xor(sets[state[t]], toggles[c])));
                            state[t2] = s2;
                        }
                        queue.Enqueue(t2);
                    }
                }
                int ringCount = ringIsHole.Length;
                int baseRings = (input.Rings ?? new List<Mesh2Ring>()).Count;
                var inside = new bool[sets.Count];
                for (int s = 0; s < sets.Count; ++s) {
                    bool outer = outerRings == 0, hole = false;
                    foreach (int ring in sets[s]) {
                        if (ring >= baseRings) continue;
                        if (ringIsHole[ring]) hole = true; else outer = true;
                    }
                    inside[s] = outer && !hole;
                }
                var keep = new bool[slots];
                for (int t = 0; t < slots; ++t) keep[t] = !k.IsGhost(t) && inside[state[t]];
                // D5: an outer ring entirely inside holes is removed with them; say so. One pass over the distinct sets.
                var setInside = new bool[sets.Count];   // the set occurs in some real triangle, and that triangle is kept
                var setUsed = new bool[sets.Count];
                for (int t = 0; t < slots; ++t) {
                    if (k.IsGhost(t)) continue;
                    setUsed[state[t]] = true;
                    if (keep[t]) setInside[state[t]] = true;
                }
                var ringAny = new bool[ringCount];
                var ringKept = new bool[ringCount];
                for (int si = 0; si < sets.Count; ++si) {
                    if (!setUsed[si]) continue;
                    foreach (int ring in sets[si]) { ringAny[ring] = true; if (setInside[si]) ringKept[ring] = true; }
                }
                for (int ring = 0; ring < baseRings; ++ring)
                    if (!ringIsHole[ring] && ringAny[ring] && !ringKept[ring]) diagnostics.Add("outer ring " + ring + " lies inside a hole and is removed with it");
                // hole seeds (D6): the bounded cell of the constraints around the seed
                var holeSeeds = input.HoleSeeds ?? new List<Vector2d>();
                for (int i = 0; i < holeSeeds.Count; ++i) {
                    var cell = SeedCell(holeSeeds[i], keep, "hole seed " + i, requireBounded: true);
                    if (cell != null) foreach (int t in cell) keep[t] = false;
                }
                // regions, the last wins
                var label = new int[slots];
                var seedRegionOf = new int[slots];   // per triangle: the index of the last seed region that labelled it, or -1
                for (int t = 0; t < slots; ++t) seedRegionOf[t] = -1;
                var regions = input.Regions ?? new List<Mesh2Region>();
                for (int g = 0; g < regions.Count; ++g) {
                    var region = regions[g];
                    if (region.Ring != null) {
                        // by ring: the latest region ring of each set, found once per set below
                        continue;
                    } else {
                        var cell = SeedCell(region.Seed, keep, "region seed " + g, requireBounded: false);
                        if (cell != null) foreach (int t in cell) if (keep[t]) { label[t] = region.Label; seedRegionOf[t] = g; }
                    }
                }
                // region rings: per distinct set, the region ring with the highest index wins over seeds of lower index
                var regionOfSet = new int[sets.Count];
                for (int si = 0; si < sets.Count; ++si) {
                    regionOfSet[si] = -1;
                    foreach (int ring in sets[si]) if (ring >= baseRings) regionOfSet[si] = Math.Max(regionOfSet[si], ring - baseRings);
                }
                if (regionRings > 0) {
                    // seeds were applied in order above; a ring region overrides a seed region of a lower index only
                    for (int t = 0; t < slots; ++t) {
                        if (!keep[t]) continue;
                        int g = regionOfSet[state[t]];
                        if (g >= 0 && g >= seedRegionOf[t]) label[t] = regions[g].Label;
                    }
                }
                return Build(inputVertices, mapping, keep, label);
            }

            static int[] Xor(int[] a, int[] b)
            {
                var r = new List<int>(a.Length + b.Length);
                int i = 0, j = 0;
                while (i < a.Length || j < b.Length) {
                    if (j == b.Length || (i < a.Length && a[i] < b[j])) r.Add(a[i++]);
                    else if (i == a.Length || b[j] < a[i]) r.Add(b[j++]);
                    else { ++i; ++j; }
                }
                return r.ToArray();
            }

            /// <summary>
            /// The triangles of the cell of the constraints that contains the seed strictly inside a kept triangle; null (with a
            /// diagnostic) if the seed is outside, on a constraint or vertex, in a removed area, or (for holes) the cell is unbounded.
            /// </summary>
            List<int> SeedCell(Vector2d seed, bool[] keep, string what, bool requireBounded)
            {
                int t = k.LocatePoint(seed.x, seed.y, out int vertex);
                if (k.IsGhost(t) || !keep[t]) { diagnostics.Add(what + " is outside the domain or in a removed area and is ignored"); return null; }
                if (vertex >= 0) { diagnostics.Add(what + " is on a vertex and is ignored"); return null; }
                for (int j = 0; j < 3; ++j) {
                    int e = 3 * t + j;
                    if (ExactPredicates.Orient2d(k.Point(k.V[e]), k.Point(k.V[Next(e)]), seed) == 0) {
                        if (k.Seg[e] >= 0) { diagnostics.Add(what + " is on a constraint and is ignored"); return null; }
                    }
                }
                var cell = new List<int> { t };
                var seen = new HashSet<int> { t };
                for (int i = 0; i < cell.Count; ++i) {
                    int c = cell[i];
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * c + j;
                        if (k.Seg[e] >= 0) continue;
                        int t2 = k.H[e] / 3;
                        if (k.IsGhost(t2)) {
                            if (requireBounded) { diagnostics.Add(what + " lies in an unbounded cell and removes nothing"); return null; }
                            continue;
                        }
                        if (seen.Add(t2)) cell.Add(t2);
                    }
                }
                return cell;
            }

            ConstrainedTriangulation2Result Build(Vector2d[] inputVertices, int[] mapping, bool[] keep, int[] label)
            {
                int slots = k.TriangleSlots;
                // vertex ids: input vertices by representative; new vertices numbered from n in order of first use
                var outId = new int[k.VertexCount];
                for (int v = 0; v < outId.Length; ++v) outId[v] = -1;
                for (int r = 0; r < n; ++r) if (k.Vertex[r] == r) outId[r] = k.Representative[r];
                var extra = new List<Vector2d>();
                var extraHeights = new List<double>();
                var id = new int[slots];
                int count = 0;
                for (int t = 0; t < slots; ++t) {
                    id[t] = keep[t] ? count++ : -1;
                    if (!keep[t]) continue;
                    for (int j = 0; j < 3; ++j) {
                        int v = k.V[3 * t + j];
                        if (outId[v] >= 0) continue;
                        outId[v] = n + extra.Count;
                        extra.Add(k.Point(v));
                        if (heights != null) extraHeights.Add(heights[v]);
                    }
                }
                var triangles = new Index3i[count];
                var neighbours = new Index3i[count];
                var labels = new int[count];
                var subsegments = new List<Mesh2Subsegment>();
                for (int t = 0; t < slots; ++t) {
                    int i = id[t];
                    if (i < 0) continue;
                    int a = outId[k.V[3 * t]], b = outId[k.V[3 * t + 1]], c = outId[k.V[3 * t + 2]];
                    int rot = a < b && a < c ? 0 : b < c ? 1 : 2;
                    int e0 = 3 * t + rot, e1 = 3 * t + (rot + 1) % 3, e2 = 3 * t + (rot + 2) % 3;
                    triangles[i] = new Index3i(outId[k.V[e0]], outId[k.V[e1]], outId[k.V[e2]]);
                    neighbours[i] = new Index3i(id[k.H[e1] / 3], id[k.H[e2] / 3], id[k.H[e0] / 3]);
                    labels[i] = label[t];
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j, c2 = k.Seg[e];
                        if (c2 < 0) continue;
                        int nb = id[k.H[e] / 3];
                        if (nb >= 0 && nb < i) continue;
                        subsegments.Add(Subsegment(outId[k.V[e]], outId[k.V[Next(e)]], Covering(k.V[e], k.V[Next(e)], k.Sources[c2])));
                    }
                }
                var vertices = inputVertices.Concat(extra).ToArray();
                double[] outHeights = null;
                if (heights != null) {
                    outHeights = new double[vertices.Length];
                    for (int i = 0; i < n; ++i) outHeights[i] = input.Heights[i];
                    for (int i = 0; i < n; ++i) outHeights[i] = outHeights[mapping[i]];
                    for (int j = 0; j < extra.Count; ++j) outHeights[n + j] = extraHeights[j];
                }
                // the full triangulation behind the mesh, for point location
                var fullP = new Vector2d[k.VertexCount];
                for (int v = 0; v < fullP.Length; ++v) if (v != k.Infinite) fullP[v] = k.Point(v);
                var fullV = new int[3 * slots]; Array.Copy(k.V, fullV, fullV.Length);
                var fullH = new int[3 * slots]; Array.Copy(k.H, fullH, fullH.Length);
                var mesh = new ConstrainedMesh2(vertices, n, mapping, triangles, neighbours, labels, subsegments.ToArray(), outHeights, fullV, fullH, id, fullP, k.Infinite);
                var status = count == 0 ? Triangulation2Status.Empty : Triangulation2Status.Success;
                return new ConstrainedTriangulation2Result(status, Triangulation2FailureKind.None, mesh, diagnostics);
            }

            /// <summary>
            /// The sources that cover the edge u-w: its own, and those of constraints at its end points whose input segment passes
            /// within the automatic tolerance (twice the snap tolerance, 2^-40 relative) of both ends. Nearly coincident lines count as
            /// one there (spec O11, R8), so a short piece between two crossings of nearly parallel lines carries both markers.
            /// </summary>
            List<int> Covering(int u, int w, List<int> own)
            {
                var result = new List<int>(own);
                double tube = 2 * tolerance;
                // a constraint that passes this close to u (or w) is an edge of a triangle at u, unless the star of u is thinner than the
                // tube: the edges of all triangles at both ends are the candidates
                foreach (int v in new[] { u, w })
                    foreach (int e0 in k.Star(v)) {
                        int t = e0 / 3;
                        for (int j = 0; j < 3; ++j) {
                            int c = k.Seg[3 * t + j];
                            if (c < 0) continue;
                            foreach (int s in k.Sources[c]) {
                                if (result.Contains(s)) continue;
                                var q = sources[s];
                                if (Dist2(k.X[u], k.Y[u], q) <= tube * tube && Dist2(k.X[w], k.Y[w], q) <= tube * tube) result.Add(s);
                            }
                        }
                    }
                return result;
            }

            static double Dist2(double px, double py, Source q)
            {
                double dx = q.BX - q.AX, dy = q.BY - q.AY, l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? Math.Max(0, Math.Min(1, ((px - q.AX) * dx + (py - q.AY) * dy) / l2)) : 0;
                double cx = q.AX + t * dx - px, cy = q.AY + t * dy - py;
                return cx * cx + cy * cy;
            }

            Mesh2Subsegment Subsegment(int a, int b, List<int> src)
            {
                var markers = new SortedSet<int>();
                bool locked = false;
                foreach (int s in src) {
                    if (sources[s].Marker != 0) markers.Add(sources[s].Marker);
                    locked |= sources[s].Locked;
                }
                return new Mesh2Subsegment { A = a, B = b, Markers = markers.ToArray(), Marker = markers.Count == 1 ? markers.Min : 0, Locked = locked };
            }
        }
    }
}
