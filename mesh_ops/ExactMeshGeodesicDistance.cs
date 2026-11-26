// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3.CoreMathPort;

namespace g3
{
    /// <summary>
    /// Certified enclosure [Lower, Upper] of the polyhedral geodesic distance from source vertices, per vertex: the
    /// shortest path along the piecewise flat surface of the mesh lies, for every vertex, between Lower and Upper. A test
    /// oracle (docs/design/GEODESICS.md, option A; owner and coordinator 2026-11-13: internal, for meshes up to about
    /// 10^4 vertices, serial). Window propagation after Mitchell, Mount and Papadimitriou 1987 and Surazhsky et al. 2005,
    /// in outward-rounded interval arithmetic (ExactInterval); the claims are A1-A7 of the design note, Appendix A:
    ///  - every window keeps an outer interval (the lower side) and an inner interval (the upper side, realised by a real
    ///    surface path), and the unfolded source as a disk (centre and radius) carried by rigid frame changes (A.2);
    ///  - a piece is removed from a window only when a real path is certainly shorter over the whole piece (A4), when
    ///    another window subsumes it (its disk, sigma interval and outer interval contain it), or by merging two pending
    ///    windows of the same origin and nearly the same position, at most one of them with a nonempty inner interval (A6);
    ///  - windows on boundary edges are terminal: they record the bounds of their edge ends (M1);
    ///  - boundary vertices and vertices that are neither certainly convex nor exactly flat are pseudo-sources (A5, review
    ///    S1); an exactly flat interior vertex (angle sum exactly 2 pi, an exact test without tolerance) emits nothing: a
    ///    shortest path through it is straight and the ordinary windows carry the straight continuation (A7', Appendix A.6;
    ///    the continuation windows of A7 would duplicate them).
    /// Reach (measured, net10 Release, benchmarks/GeodesicOracleBenchmark, docs/design/GEODESICS.md 6d): terrains of 41 x 41
    /// vertices in half a second and of 101 x 101 in about 3 s; exactly flat input (planes, developable ridges) of 101 x 101 in
    /// 3-6 s since A7' (it reached the window limit before); a saddle of 101 x 101 (no flat vertices) still needs about 75 s.
    /// Width (measured, #1058): (Upper - Lower) / Lower is at most about 2e-8 on planes, on domes and saddles from exactly
    /// flat to curved (k (x^2 +- y^2), k from 1e-8 to 0.3), on terrains and on the aligned ridge, from a centre, edge or corner
    /// source; it comes from Lower's re-emission margins (1e-9 per emission, A5; 1e-12 where no vertex emits), and Upper is as
    /// tight. A measured figure on procedural grids, not an a-priori bound: Upper may fall back to edge paths where windows
    /// keep no inner interval (eg the degenerate projections of S2). A limit ends with WindowLimit and NaN arrays.
    /// The result is deterministic (serial, every operation IEEE-rounded and widened) and scales exactly with 2^k scalings
    /// of the input (away from overflow and the subnormal range).
    /// </summary>
    internal sealed class ExactMeshGeodesicDistance
    {
        public enum Result { NotComputed, Ok, NoSource, NonManifoldInput, DegenerateTriangles, WindowLimit, PositionTooWide, LowerBoundMissing }

        public readonly DMesh3 Mesh;
        /// <summary> limit on the windows created; passing it ends Compute with WindowLimit (never with a wrong bound). The count
        /// is checked between propagation steps, so Windows can end somewhat above the limit (by the windows of one step). The default
        /// keeps one Compute below about 20 s (a flat plane of 101 x 101 vertices reaches it; a terrain of that size needs
        /// about 125000 windows, 3 s; benchmarks/GeodesicOracleBenchmark) </summary>
        public int MaxWindows = 300_000;

        // the compaction of the dead (emptied) windows of a side list: speed only, the scan skips dead windows anyway.
        // Internal so a test can force it on every dead window (terrain 41^2 reaches at most 7 dead windows on one side).
        internal bool ForceCompaction;
        internal int Compactions;

        /// <summary> per vertex id: a lower and an upper bound of the polyhedral distance to the nearest source. NaN for
        /// unused ids, vertices without a path to a source, and every vertex when Compute did not return Ok </summary>
        public double[] Lower, Upper;
        public Result Status { get; private set; }
        /// <summary> windows created by the last Compute </summary>
        public long Windows { get; private set; }

        public ExactMeshGeodesicDistance(DMesh3 mesh) { Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh)); }

        // ------------------------------------------------------------------ data

        sealed class Window
        {
            public int E, F, Origin;            // edge, the face it propagates into, origin vertex
            public double SigLo, SigHi;          // the origin's distance
            public double Cx, Cy, R;             // the unfolded origin: within R of (Cx, Cy) in the frame of E (Appendix A.2)
            public double O0, O1, I0, I1;        // outer and inner parameter intervals along E from its first vertex (inner empty if I0 > I1)
            public bool Done;
        }

        internal struct P2 { public ExactInterval X, Y; public P2(ExactInterval x, ExactInterval y) { X = x; Y = y; } }

        // a binary min-heap with a total order: the key, then the insertion number (deterministic ties)
        sealed class Heap
        {
            readonly List<(double key, long seq, long id)> h = new List<(double key, long seq, long id)>();
            long next;
            public int Count => h.Count;
            static bool Less((double key, long seq, long id) a, (double key, long seq, long id) b) => a.key < b.key || (a.key == b.key && a.seq < b.seq);
            public void Enqueue(long id, double key)
            {
                h.Add((key, next++, id)); int i = h.Count - 1;
                while (i > 0) { int q = (i - 1) / 2; if (!Less(h[i], h[q])) break; var t = h[q]; h[q] = h[i]; h[i] = t; i = q; }
            }
            public bool TryDequeue(out long id, out double key)
            {
                if (h.Count == 0) { id = 0; key = 0; return false; }
                id = h[0].id; key = h[0].key;
                var last = h[h.Count - 1]; h.RemoveAt(h.Count - 1);
                if (h.Count > 0) {
                    h[0] = last; int i = 0;
                    while (true) {
                        int l = 2 * i + 1, r = l + 1, m = i;
                        if (l < h.Count && Less(h[l], h[m])) m = l;
                        if (r < h.Count && Less(h[r], h[m])) m = r;
                        if (m == i) break;
                        var t = h[m]; h[m] = h[i]; h[i] = t; i = m;
                    }
                }
                return true;
            }
        }

        List<Window> windows;
        Dictionary<int, List<int>> bySide;
        internal long Emissions, Merges, Iterations, DominatedCalls;
        Heap queue;
        double[] lo, up, emitted;
        ExactInterval[] edgeLen;
        P2[] sidePlace;
        byte[] kind;                // 0 needs no emission (certainly convex, or exactly flat: A7'), 1 pseudo-source
        bool failed;
        double scale;               // a length scale of the mesh (its largest coordinate extent): scales exactly with 2^k
        Result failure;

        // ------------------------------------------------------------------ interval helpers

        static ExactInterval I(double x) => ExactInterval.Of(x);
        static ExactInterval Sq(ExactInterval a) => ExactInterval.Square(a);
        static ExactInterval Sqrt(ExactInterval a) => ExactInterval.Sqrt(a);
        static double Mid(ExactInterval a) => 0.5 * a.Lo + 0.5 * a.Hi;
        static double HalfWidth(ExactInterval a) => ExactInterval.Up(0.5 * a.Hi - 0.5 * a.Lo);
        static double Ulp(double x) { double a = Math.Abs(x); return ExactInterval.Up(a) - a; }
        static double RoundUpSum(double a, double b) => ExactInterval.Up(a + b);

        ExactInterval Len(int a, int b)
        {
            Vector3d p = Mesh.GetVertex(a), q = Mesh.GetVertex(b);
            return Sqrt(Sq(I(p.x) - I(q.x)) + Sq(I(p.y) - I(q.y)) + Sq(I(p.z) - I(q.z)));
        }

        // position of c in the frame of the edge (a at the origin, b on +x), on the side y > 0
        P2 Place(int a, int b, int c, ExactInterval L)
        {
            ExactInterval la = Len(a, c), lb = Len(b, c);
            ExactInterval x = (Sq(la) - Sq(lb) + Sq(L)) / (I(2) * L);
            ExactInterval y = Sqrt(Sq(la) - Sq(x));
            return new P2(x, y);
        }

        static ExactInterval Dist(P2 s, ExactInterval x, ExactInterval y) => Sqrt(Sq(x - s.X) + Sq(y - s.Y));

        // the box of a window's disk; y <= 0 (the origin lies on or behind the edge's line, Appendix A.3)
        static P2 Box(Window w)
        {
            ExactInterval x = I(w.Cx) + new ExactInterval(-w.R, w.R);
            ExactInterval y = I(w.Cy) + new ExactInterval(-w.R, w.R);
            return new P2(x, new ExactInterval(y.Lo, Math.Min(0, y.Hi)));
        }

        // the window's distance function over the parameter interval [l, r] of its edge
        static ExactInterval Fn(Window w, P2 s, double l, double r) => Dist(s, new ExactInterval(l, r), I(0)) + new ExactInterval(w.SigLo, w.SigHi);

        int Third(int f, int a, int b) { Index3i t = Mesh.GetTriangle(f); for (int k = 0; k < 3; ++k) if (t[k] != a && t[k] != b) return t[k]; return -1; }
        int Other(int e, int f) { Index2i et = Mesh.GetEdgeT(e); return et.a == f ? et.b : et.a; }
        int Side(int e, int f) => 2 * e + (Mesh.GetEdgeT(e).a == f ? 0 : 1);

        void SetLower(int v, double d) { if (d < lo[v]) { lo[v] = d; if (kind[v] == 1) queue.Enqueue(-(long)v - 1, d); } }
        void SetUpper(int v, double d) { if (d < up[v]) up[v] = d; }

        // ------------------------------------------------------------------ vertex classification (A5, A7)

        // the vertices around v in fan order (v interior and manifold)
        List<int> Fan(int v)
        {
            var tris = new List<int>(); foreach (int t in Mesh.VtxTrianglesItr(v)) tris.Add(t);
            var order = new List<int>();
            int t0 = tris[0]; Index3i tri = Mesh.GetTriangle(t0);
            int k = tri.a == v ? 0 : tri.b == v ? 1 : 2;
            int first = tri[(k + 1) % 3], cur = tri[(k + 2) % 3], curT = t0;
            order.Add(first);
            for (int guard = 0; guard <= tris.Count && cur != first; ++guard) {
                order.Add(cur);
                int e = Mesh.FindEdge(v, cur); int next = Other(e, curT);
                if (next < 0) break;
                int nb = Third(next, v, cur); curT = next; cur = nb;
            }
            return order;
        }

        // the corner angle at p between u = a - p and w = b - p, enclosed: atan2(|u x w|, u . w) by CoreMath.Atan2 at the
        // corners of the argument box (the box lies in y > 0, so its directions form an arc whose ends are corners), widened
        // by two spacings on each side. An assumption, not a proof: CoreMath.Atan2 is the CORE-MATH port, and its correct
        // rounding (error at most half a spacing) is an empirical claim backed by bit-exact tests against a BigInteger
        // reference, random and on the hard-to-round cases (docs/SOURCES.md, "Correctly rounded sin, cos, atan2, exp, log"),
        // not re-derived for the port. The widening by two spacings leaves a margin of 1.5 spacings beyond that claim.
        ExactInterval CornerAngle(int p, int a, int b) => CornerAngle(Mesh.GetVertex(p), Mesh.GetVertex(a), Mesh.GetVertex(b));

        internal static ExactInterval CornerAngle(Vector3d P, Vector3d A, Vector3d B)
        {
            ExactInterval ux = I(A.x) - I(P.x), uy = I(A.y) - I(P.y), uz = I(A.z) - I(P.z);
            ExactInterval wx = I(B.x) - I(P.x), wy = I(B.y) - I(P.y), wz = I(B.z) - I(P.z);
            ExactInterval cx = uy * wz - uz * wy, cy = uz * wx - ux * wz, cz = ux * wy - uy * wx;
            ExactInterval y = Sqrt(Sq(cx) + Sq(cy) + Sq(cz)), x = ux * wx + uy * wy + uz * wz;
            if (!(y.Lo > 0)) return new ExactInterval(0, Math.PI * 2);
            double mn = double.MaxValue, mx = double.MinValue;
            foreach (double yy in new[] { y.Lo, y.Hi })
                foreach (double xx in new[] { x.Lo, x.Hi }) {
                    double t = CoreMath.Atan2(yy, xx); mn = Math.Min(mn, t); mx = Math.Max(mx, t);
                }
            return new ExactInterval(ExactInterval.Down(ExactInterval.Down(mn)), ExactInterval.Up(ExactInterval.Up(mx)));
        }

        void Classify()
        {
            // a vertex without triangles carries no surface: no path passes through it (it stays unreachable, NaN)
            // FlatVertices counts exactly flat vertices before a source is forced to kind 1 (a flat source still emits)
            FlatVertices = 0;
            foreach (int v in Mesh.VertexIndices()) {
                lastWasFlat = false;
                kind[v] = Mesh.GetVtxTriangleCount(v) == 0 ? (byte)0 : (byte)VertexKind(v);
                if (lastWasFlat && kind[v] == 0) ++FlatVertices;
            }
        }

        /// <summary> 0: the vertex needs no emission: certainly convex (no shortest path passes through it, Fact F), or exactly
        /// flat (an interior vertex of angle sum exactly 2 pi, decided by exact predicates without a tolerance: a shortest path
        /// through it is straight, and the ordinary windows already carry the straight continuation, A7' of the design note,
        /// Appendix A.6). 1: a pseudo-source (a boundary vertex, or neither certainly convex nor exactly flat: a saddle, a
        /// nearly flat vertex whose sum may exceed 2 pi by rounding noise). For tests. </summary>
        internal int VertexKind(int v)
        {
            lastWasFlat = false;
            if (Mesh.IsBoundaryVertex(v)) return 1;
            var fan = Fan(v);
            // a walk that stopped early (or wrapped twice) does not see every corner: never certainly convex
            if (fan.Count != Mesh.GetVtxTriangleCount(v)) return 1;
            ExactInterval sum = I(0);
            for (int i = 0; i < fan.Count; ++i) sum = sum + CornerAngle(v, fan[i], fan[(i + 1) % fan.Count]);
            // Math.PI is the double below pi, so 2 * Math.PI (an exact doubling) is below 2 pi
            if (sum.Hi < 2 * Math.PI) return 0;
            if (!FlatVerticesEmit && IsExactlyFlat(v, fan)) { lastWasFlat = true; return 0; }
            return 1;
        }

        /// <summary> whether the vertex is an interior vertex of angle sum exactly 2 pi (the exact flatness test of A.5), whatever the
        /// run's settings; used by <see cref="PolyhedralGeodesicDistance"/>, whose clips are closed (carry the ray) at these vertices only </summary>
        internal bool IsFlatVertex(int v)
        {
            if (Mesh.GetVtxTriangleCount(v) == 0 || Mesh.IsBoundaryVertex(v)) return false;
            var fan = Fan(v);
            return fan.Count == Mesh.GetVtxTriangleCount(v) && IsExactlyFlat(v, fan);
        }

        /// <summary> the pre-A7' behaviour (S1): every vertex that is not certainly convex is a pseudo-source, flat ones included.
        /// For tests, which compare the two. </summary>
        bool lastWasFlat;           // the last VertexKind call returned 0 because the vertex is exactly flat
        internal bool FlatVerticesEmit;
        /// <summary> the number of vertices of the last run that are exactly flat and so of kind 0 without being certainly
        /// convex. For tests </summary>
        internal int FlatVertices;
        /// <summary> whether the vertex has emitted in the last run. For tests </summary>
        internal bool Emitted(int v) => emitted != null && emitted[v] < double.PositiveInfinity;
        internal int Kind(int v) => kind[v];

        // ---- the exact flatness test of A.5 (all predicates exact, on the double coordinates)

        static Vector2d Project(int axis, Vector3d q) => axis == 0 ? new Vector2d(q.y, q.z) : axis == 1 ? new Vector2d(q.z, q.x) : new Vector2d(q.x, q.y);
        static int Orient2(int axis, Vector3d p, Vector3d a, Vector3d b) => ExactPredicates.Orient2d(Project(axis, p), Project(axis, a), Project(axis, b));
        // a coordinate plane on which the triangle (p, a, b) does not project to a segment; 2 if it is degenerate (all three
        // projections are segments), and then every Orient2 of the callers is 0, so their sign tests reject
        static int RegularAxis(Vector3d p, Vector3d a, Vector3d b)
        {
            for (int ax = 0; ax < 2; ++ax) if (Orient2(ax, p, a, b) != 0) return ax;
            return 2;
        }

        /// <summary> the angle sum at the interior vertex v (neighbours in fan order) is exactly 2 pi: (i) a coplanar fan of
        /// sectors that all turn the same way and wind once, or (ii) a straight fold: two opposite neighbours on a line
        /// through v and each half a coplanar fan on one side of that line (Appendix A.5) </summary>
        internal bool IsExactlyFlat(int v, List<int> fan)
        {
            int k = fan.Count;
            Vector3d P = Mesh.GetVertex(v); var N = new Vector3d[k];
            for (int i = 0; i < k; ++i) N[i] = Mesh.GetVertex(fan[i]);
            return CoplanarFan(P, N) || StraightFold(P, N);
        }

        internal static bool CoplanarFan(Vector3d P, Vector3d[] N)
        {
            int k = N.Length, ax = RegularAxis(P, N[0], N[1]);
            for (int i = 2; i < k; ++i) if (ExactPredicates.Orient3d(P, N[0], N[1], N[i]) != 0) return false;
            int s = Orient2(ax, P, N[0], N[1]), winding = 0;
            for (int i = 0; i < k; ++i) {
                int j = (i + 1) % k;
                if (s * Orient2(ax, P, N[i], N[j]) <= 0) return false;          // every sector turns the same way, by less than pi
                if (s * Orient2(ax, P, N[i], N[0]) > 0) {                        // the sector starts on the far side of the reference ray: it reaches or passes it?
                    int o = s * Orient2(ax, P, N[0], N[j]);
                    // o == 0: n_j lies on the reference line, and in the same direction as n_0 (the sector starts strictly behind n_0 and
                    // turns by less than pi, so it cannot end opposite to it): it ends exactly on the ray
                    if (o >= 0) ++winding;
                }
            }
            return winding == 1;                                                 // 2 pi times the winding number
        }

        internal static bool StraightFold(Vector3d P, Vector3d[] N)
        {
            int k = N.Length;
            for (int i = 0; i < k; ++i)
                for (int j = i + 1; j < k; ++j) {
                    Vector3d a = N[i], b = N[j];
                    if (Orient2(0, P, a, b) != 0 || Orient2(1, P, a, b) != 0 || Orient2(2, P, a, b) != 0) continue;     // collinear
                    // that a and b are on opposite sides of P needs no test of its own: a half whose last sector (from its last
                    // intermediate neighbour, strictly on one side of the line, to b) turns the same way as its first one ends on the
                    // ray opposite to a
                    if (FoldHalf(P, N, i, j) && FoldHalf(P, N, j, i)) return true;
                }
            return false;
        }

        // the neighbours from index `from` forward to `to` (cyclic): coplanar, all strictly on one side of the fold line, and
        // consecutive sectors turning the same way, so the half spans exactly pi
        internal static bool FoldHalf(Vector3d P, Vector3d[] N, int from, int to)
        {
            int k = N.Length, count = ((to - from + k) % k) - 1;     // intermediate neighbours; none: a and b adjacent, sg = 0 below rejects
            Vector3d a = N[from], m1 = N[(from + 1) % k];
            int ax = RegularAxis(P, a, m1);
            int sg = Orient2(ax, P, a, m1);
            Vector3d prev = a;
            for (int t = 1; t <= count + 1; ++t) {
                Vector3d cur = N[(from + t) % k];
                if (sg * Orient2(ax, P, prev, cur) <= 0) return false;                                  // turns the same way, by less than pi
                if (t <= count) {
                    if (ExactPredicates.Orient3d(P, a, m1, cur) != 0) return false;                    // in the half's plane
                    if (Orient2(ax, P, a, cur) != sg) return false;                                     // strictly on one side of the fold line
                }
                prev = cur;
            }
            return true;
        }

        // ------------------------------------------------------------------ compute

        public Result Compute(IEnumerable<int> sources)
        {
            int n = Mesh.MaxVertexID;
            // NaN until a successful run copies its bounds in, so a failure (or an exception) never leaves values that look valid
            Lower = new double[n]; Upper = new double[n];
            for (int v = 0; v < n; ++v) { Lower[v] = double.NaN; Upper[v] = double.NaN; }
            Status = Result.NotComputed;        // also if run throws: never the old Ok next to NaN arrays
            Status = run(sources);
            return Status;
        }

        Result run(IEnumerable<int> sources)
        {
            int n = Mesh.MaxVertexID;
            windows = new List<Window>(); bySide = new Dictionary<int, List<int>>(); queue = new Heap();
            lo = new double[n]; up = new double[n]; emitted = new double[n]; kind = new byte[n]; failed = false; Windows = 0; Compactions = 0;
            Emissions = Merges = Iterations = DominatedCalls = 0;
            for (int v = 0; v < n; ++v) { lo[v] = up[v] = emitted[v] = double.PositiveInfinity; }
            var src = new List<int>();
            if (sources != null) foreach (int s in sources) if (s >= 0 && s < n && Mesh.IsVertex(s) && !src.Contains(s)) src.Add(s);
            if (src.Count == 0) return Result.NoSource;
            foreach (int v in Mesh.VertexIndices()) if (Mesh.IsBowtieVertex(v)) return Result.NonManifoldInput;
            foreach (int t in Mesh.TriangleIndices()) {
                Index3i tri = Mesh.GetTriangle(t);
                Vector3d a = Mesh.GetVertex(tri.a), b = Mesh.GetVertex(tri.b), c = Mesh.GetVertex(tri.c);
                ExactInterval ux = I(b.x) - I(a.x), uy = I(b.y) - I(a.y), uz = I(b.z) - I(a.z), wx = I(c.x) - I(a.x), wy = I(c.y) - I(a.y), wz = I(c.z) - I(a.z);
                ExactInterval area2 = Sq(uy * wz - uz * wy) + Sq(uz * wx - ux * wz) + Sq(ux * wy - uy * wx);
                if (!(area2.Lo > 0)) return Result.DegenerateTriangles;
            }
            int ne = Mesh.MaxEdgeID;
            edgeLen = new ExactInterval[ne];
            sidePlace = new P2[2 * ne];
            foreach (int e in Mesh.EdgeIndices()) {
                Index2i ev = Mesh.GetEdgeV(e), et = Mesh.GetEdgeT(e);
                ExactInterval el = Len(ev.a, ev.b);
                edgeLen[e] = el;
                sidePlace[2 * e] = Place(ev.a, ev.b, Third(et.a, ev.a, ev.b), el);
                if (et.b != DMesh3.InvalidID) sidePlace[2 * e + 1] = Place(ev.a, ev.b, Third(et.b, ev.a, ev.b), el);
            }
            AxisAlignedBox3d bounds = Mesh.CachedBounds;
            scale = Math.Max(bounds.Max.x - bounds.Min.x, Math.Max(bounds.Max.y - bounds.Min.y, bounds.Max.z - bounds.Min.z));
            Classify();
            foreach (int s in src) { lo[s] = 0; up[s] = 0; queue.Enqueue(-(long)s - 1, 0); kind[s] = 1; }
            while (queue.Count > 0) {
                if (failed) return failure;
                if (Windows > MaxWindows || ++Iterations > 20L * MaxWindows) return Result.WindowLimit;
                queue.TryDequeue(out long id, out _);
                if (id < 0) { int v = (int)(-id - 1); if (lo[v] < emitted[v]) Emit(v); continue; }
                Window w = windows[(int)id];
                if (w.Done || !(w.O1 > w.O0)) continue;
                w.Done = true; Propagate(w);
            }
            if (failed) return failure;
            // Upper: an edge path is a real path
            var pq = new Heap();
            foreach (int v in Mesh.VertexIndices()) if (up[v] < double.PositiveInfinity) pq.Enqueue(v, up[v]);
            while (pq.TryDequeue(out long lv, out double key)) {
                int v = (int)lv;
                if (key > up[v]) continue;
                foreach (int u in Mesh.VtxVerticesItr(v)) { double c = (I(up[v]) + Len(v, u)).Hi; if (c < up[u]) { up[u] = c; pq.Enqueue(u, c); } }
            }
            // a vertex with a path (finite Upper) but no lower bound is a gap in the propagation, not an unreachable vertex
            for (int v = 0; v < n; ++v)
                if (Mesh.IsVertex(v) && up[v] < double.PositiveInfinity && !(lo[v] < double.PositiveInfinity)) return Result.LowerBoundMissing;
            for (int v = 0; v < n; ++v)
                if (Mesh.IsVertex(v) && lo[v] < double.PositiveInfinity && up[v] < double.PositiveInfinity) { Lower[v] = lo[v]; Upper[v] = up[v]; }
            return Result.Ok;
        }

        // a pseudo-source (re-)emits: windows on the opposite edges of its faces, sigma below its current lower bound (A5)
        void Emit(int v)
        {
            ++Emissions;
            double sl = Math.Max(0, ExactInterval.Down(lo[v] - lo[v] * 1e-9));
            emitted[v] = sl;
            foreach (int nb in Mesh.VtxVerticesItr(v)) {
                ExactInterval l = Len(v, nb);
                SetLower(nb, (I(sl) + l).Lo);
                if (up[v] < double.PositiveInfinity) SetUpper(nb, (I(up[v]) + l).Hi);
            }
            foreach (int t in Mesh.VtxTrianglesItr(v)) {
                Index3i tri = Mesh.GetTriangle(t);
                int a = -1, b = -1; for (int k = 0; k < 3; ++k) if (tri[k] != v) { if (a < 0) a = tri[k]; else b = tri[k]; }
                int e = Mesh.FindEdge(a, b); int f2 = Other(e, t);
                if (f2 < 0) continue;               // the opposite edge is a boundary edge: its ends are neighbours of v (recorded above)
                ExactInterval L = edgeLen[e];
                P2 V = sidePlace[Side(e, t)];
                double r0 = DiskOfBox(V, out double vx, out double vy);
                Add(new Window { E = e, F = f2, Origin = v, SigLo = sl, SigHi = up[v], Cx = vx, Cy = -vy, R = r0, O0 = 0, O1 = L.Hi, I0 = 0, I1 = L.Lo });
            }
        }

        /// <summary> a disk (centre, returned radius) containing the box: the centre is the box's computed midpoint, the radius
        /// the sum of the half widths (at least the half diagonal, and no underflow of squares) plus the midpoint's rounding </summary>
        internal static double DiskOfBox(P2 box, out double cx, out double cy)
        {
            cx = Mid(box.X); cy = Mid(box.Y);
            double hx = HalfWidth(box.X), hy = HalfWidth(box.Y);
            return ExactInterval.Up(ExactInterval.Up(hx + hy) + 2 * (Ulp(cx) + Ulp(cy)));
        }

        // ------------------------------------------------------------------ pruning (A4), subsumption, merging (A6)

        // a vertex at (vx, vy) with a certified upper bound reaches every point of [l, r] more cheaply than w
        bool VertexBeats(Window w, P2 s, ExactInterval vx, ExactInterval vy, int v, double l, double r)
        {
            if (!(up[v] < double.PositiveInfinity)) return false;
            double sup = Math.Max(Dist(new P2(vx, vy), I(l), I(0)).Hi, Dist(new P2(vx, vy), I(r), I(0)).Hi);
            return Fn(w, s, l, r).Lo > (I(up[v]) + I(sup)).Hi;
        }

        void TrimByVertex(Window w, P2 s, ExactInterval vx, ExactInterval vy, int v, List<(double l, double r)> segs)
        {
            for (int k = 0; k < segs.Count; ++k) {
                var (l, r) = segs[k];
                if (VertexBeats(w, s, vx, vy, v, l, r)) { segs[k] = (r, r); continue; }
                double a = l, b = r;
                for (int it = 0; it < 8; ++it) { double m = 0.5 * a + 0.5 * b; if (VertexBeats(w, s, vx, vy, v, l, m)) a = m; else b = m; }
                if (a > l) l = a;
                a = l; b = r;
                for (int it = 0; it < 8; ++it) { double m = 0.5 * a + 0.5 * b; if (VertexBeats(w, s, vx, vy, v, m, r)) b = m; else a = m; }
                if (b < r) r = b;
                segs[k] = (l, r);
            }
        }

        // outward-rounded enclosure of phi(u, v) = u / sqrt(u^2 + v^2) (v >= 0): monotone increasing in u;
        // decreasing in v for u > 0, increasing in v for u < 0; ExactInterval division maps underflow/NaN to [-Inf, +Inf], clamped to [-1, 1]
        static double SlopeBound(double u, double v, bool isLo)
        {
            if (u == 0) return v > 0 ? 0 : (isLo ? -1 : 1);
            if (v == 0) return u > 0 ? 1 : -1;
            ExactInterval q = I(u) / Sqrt(Sq(I(u)) + Sq(I(v)));
            return Math.Max(-1, Math.Min(1, isLo ? q.Lo : q.Hi));
        }

        /// <summary> outward-rounded enclosure of d/dt sqrt((t - x)^2 + y^2) over t in [l, r], (x, y) in s (s.Y.Hi &lt;= 0) </summary>
        internal static ExactInterval Slope(P2 s, double l, double r)
        {
            double vLo = -s.Y.Hi, vHi = -s.Y.Lo;
            double uLo = (I(l) - s.X).Lo, uHi = (I(r) - s.X).Hi;
            double slo = SlopeBound(uLo, uLo > 0 ? vHi : vLo, true);
            double shi = SlopeBound(uHi, uHi < 0 ? vHi : vLo, false);
            return new ExactInterval(slo, shi);
        }

        // the parts of [l, r] (inside b's inner interval) where a is certainly worse than b, to the given depth
        // (Hansen-Walster monotonicity + 1D bisection and Mean-Value Form in ExactInterval)
        void Dominated(Window a, P2 sa, Window b, P2 sb, double l, double r, int depth, List<(double l, double r)> rem)
        {
            if (!(r > l)) return;
            ++DominatedCalls;
            ExactInterval fa = Fn(a, sa, l, r), fb = Fn(b, sb, l, r);
            if (fa.Lo > fb.Hi) { rem.Add((l, r)); return; }
            if (fa.Hi <= fb.Lo) return;
            ExactInterval dg = Slope(sa, l, r) - Slope(sb, l, r);
            if (dg.Lo >= 0) {
                if (Fn(a, sa, l, l).Lo > Fn(b, sb, l, l).Hi) { rem.Add((l, r)); return; }
                if (Fn(a, sa, r, r).Lo > Fn(b, sb, r, r).Hi) {
                    double lo = l, hi = r;
                    for (int it = 0; it < depth; ++it) {
                        double mid = 0.5 * lo + 0.5 * hi;
                        if (Fn(a, sa, mid, mid).Lo > Fn(b, sb, mid, mid).Hi) hi = mid; else lo = mid;
                    }
                    if (r > hi) rem.Add((hi, r));
                }
                return;
            }
            if (dg.Hi <= 0) {
                if (Fn(a, sa, r, r).Lo > Fn(b, sb, r, r).Hi) { rem.Add((l, r)); return; }
                if (Fn(a, sa, l, l).Lo > Fn(b, sb, l, l).Hi) {
                    double lo = l, hi = r;
                    for (int it = 0; it < depth; ++it) {
                        double mid = 0.5 * lo + 0.5 * hi;
                        if (Fn(a, sa, mid, mid).Lo > Fn(b, sb, mid, mid).Hi) lo = mid; else hi = mid;
                    }
                    if (lo > l) rem.Add((l, lo));
                }
                return;
            }
            double m = 0.5 * l + 0.5 * r;
            ExactInterval mv = (Fn(a, sa, m, m) - Fn(b, sb, m, m)) + dg * (new ExactInterval(l, r) - I(m));
            if (mv.Lo > 0) { rem.Add((l, r)); return; }
            if (mv.Hi <= 0) return;
            if (depth == 0) return;
            Dominated(a, sa, b, sb, l, m, depth - 1, rem); Dominated(a, sa, b, sb, m, r, depth - 1, rem);
        }

        static List<(double l, double r)> Minus(List<(double l, double r)> segs, List<(double l, double r)> rem)
        {
            var result = new List<(double l, double r)>();
            foreach (var sg in segs) {
                var cur = new List<(double l, double r)> { sg };
                foreach (var x in rem) {
                    var next = new List<(double l, double r)>();
                    foreach (var c in cur) {
                        if (x.r <= c.l || x.l >= c.r) { next.Add(c); continue; }
                        if (x.l > c.l) next.Add((c.l, x.l));
                        if (x.r < c.r) next.Add((x.r, c.r));
                    }
                    cur = next;
                }
                result.AddRange(cur);
            }
            return result;
        }

        static double CentreDistanceUp(Window a, Window b)
        {
            ExactInterval d = Sqrt(Sq(I(a.Cx) - I(b.Cx)) + Sq(I(a.Cy) - I(b.Cy)));
            return d.Hi;
        }

        void Add(Window w)
        {
            Index2i ev = Mesh.GetEdgeV(w.E); ExactInterval L = edgeLen[w.E]; P2 s = Box(w);
            double tol = 1e-12 * L.Hi;
            var segs = new List<(double l, double r)> { (w.O0, w.O1) };
            int z = Third(w.F, ev.a, ev.b); int side = Side(w.E, w.F); P2 Z = sidePlace[side];
            TrimByVertex(w, s, I(0), I(0), ev.a, segs); TrimByVertex(w, s, L, I(0), ev.b, segs); TrimByVertex(w, s, Z.X, Z.Y, z, segs);
            segs.RemoveAll(t => !(t.r > t.l)); if (segs.Count == 0) return;      // only empty pieces: a short piece is no certified drop
            if (!bySide.TryGetValue(side, out var list)) { list = new List<int>(); bySide[side] = list; }
            int dead = 0; foreach (int oi in list) if (!(windows[oi].O1 > windows[oi].O0)) ++dead;
            if (dead > 0 && (ForceCompaction || dead > 8 && 2 * dead > list.Count)) {
                list.RemoveAll(oi => !(windows[oi].O1 > windows[oi].O0));
                ++Compactions;
            }
            double segL = double.MaxValue, segR = double.MinValue; foreach (var t in segs) { segL = Math.Min(segL, t.l); segR = Math.Max(segR, t.r); }
            foreach (int oi in list) {
                Window b = windows[oi];
                if (!(b.O1 > b.O0) || b.O1 < segL - tol || b.O0 > segR + tol) continue;
                P2 sb = Box(b);
                if (b.Origin == w.Origin) {
                    double dc = CentreDistanceUp(b, w);
                    // subsumption: b's disk, sigma and outer interval contain w's (certified: upward rounding, exact comparisons)
                    if (RoundUpSum(dc, w.R) <= b.R && b.SigLo <= w.SigLo && w.SigHi <= b.SigHi) {
                        segs.RemoveAll(t => t.l >= b.O0 && t.r <= b.O1);
                        if (segs.Count == 0) return;
                    }
                    // merging (A6): both pending, nearly the same position and sigma, outer intervals touching
                    double mr = Math.Max(b.R, w.R), nr = RoundUpSum(dc, w.R) * (1 + 1e-15);
                    // relative to the mesh's length scale (no absolute constants: 2^k scalings give the same decisions)
                    bool near = !b.Done && nr <= 2 * mr && Math.Max(b.R, nr) <= 1e-11 * (scale + Math.Abs(b.Cx) + Math.Abs(b.Cy))
                        && Math.Abs(b.SigHi - w.SigHi) <= 1e-12 * (scale + Math.Abs(b.SigHi)) && Math.Abs(b.SigLo - w.SigLo) <= 1e-12 * (scale + Math.Abs(b.SigHi));
                    // A6 keeps the inner interval of only one partner, so it merges only when at most one has a nonempty
                    // inner interval. Two (eg the two halves of a fan split at a vertex, whose rays meet again) stay two
                    // windows: skipping a merge removes nothing, so it is always sound. #1058: dropping the second half left
                    // Upper on edge paths, up to 8% above the distance on planes and nearly flat domes
                    if (near && b.I1 > b.I0 && w.I1 > w.I0) near = false;
                    if (near) {
                        var keep = new List<(double l, double r)>(); bool merged = false;
                        foreach (var sg in segs) {
                            if (sg.l <= b.O1 + tol && sg.r >= b.O0 - tol) { b.O0 = Math.Min(b.O0, sg.l); b.O1 = Math.Max(b.O1, sg.r); merged = true; }
                            else keep.Add(sg);
                        }
                        if (merged) {
                            ++Merges; b.R = Math.Max(b.R, nr); b.SigLo = Math.Min(b.SigLo, w.SigLo); b.SigHi = Math.Max(b.SigHi, w.SigHi);
                            // the inner interval of only one of them (A6): the nonempty one. w's is clipped to the merged
                            // outer interval (a subset of a realised set is realised, and w's witness lies in the merged disk);
                            // an empty interval (I0 > I1) stays empty under the clip
                            if (!(b.I1 > b.I0)) { b.I0 = Math.Max(w.I0, b.O0); b.I1 = Math.Min(w.I1, b.O1); }
                            Record(b, L, ev.a, ev.b);
                            queue.Enqueue(oi, Fn(b, Box(b), b.O0, b.O1).Lo);
                        }
                        segs = keep; if (segs.Count == 0) return;
                        continue;
                    }
                }
                // w certainly worse than b where b is inner-valid (A4)
                if (b.I1 > b.I0) {
                    var rem = new List<(double l, double r)>();
                    foreach (var sg in segs) { double l = Math.Max(sg.l, b.I0), r = Math.Min(sg.r, b.I1); if (r > l) Dominated(w, s, b, sb, l, r, 6, rem); }
                    if (rem.Count > 0) { segs = Minus(segs, rem); segs.RemoveAll(t => !(t.r > t.l)); if (segs.Count == 0) return; }
                }
                // b certainly worse than w where w is inner-valid: cut b's ends only
                if (w.I1 > w.I0) {
                    double l = Math.Max(b.O0, w.I0), r = Math.Min(b.O1, w.I1);
                    if (r > l && Fn(b, sb, l, r).Lo > Fn(w, s, l, r).Hi) {
                        if (l <= b.O0) { b.O0 = r; b.I0 = Math.Max(b.I0, r); }
                        else if (r >= b.O1) { b.O1 = l; b.I1 = Math.Min(b.I1, l); }
                    }
                }
            }
            foreach (var sg in segs) {
                var c = new Window { E = w.E, F = w.F, Origin = w.Origin, SigLo = w.SigLo, SigHi = w.SigHi, Cx = w.Cx, Cy = w.Cy, R = w.R,
                    O0 = sg.l, O1 = sg.r, I0 = Math.Max(w.I0, sg.l), I1 = Math.Min(w.I1, sg.r) };
                windows.Add(c); list.Add(windows.Count - 1); ++Windows;
                Record(c, L, ev.a, ev.b);
                queue.Enqueue(windows.Count - 1, Fn(c, s, c.O0, c.O1).Lo);
            }
        }

        // the bounds a window gives its edge ends
        void Record(Window c, ExactInterval L, int a, int b)
        {
            P2 s = Box(c);
            ExactInterval sig = new ExactInterval(c.SigLo, c.SigHi);
            ExactInterval da = Dist(s, I(0), I(0)) + sig, db = Dist(s, L, I(0)) + sig;
            if (c.O0 <= 0) SetLower(a, da.Lo);
            if (c.O1 >= L.Lo) SetLower(b, db.Lo);
            if (c.I1 > c.I0) {
                if (c.I0 <= 0) SetUpper(a, da.Hi);
                // no counterpart for b: inner intervals are clipped to at most L.Lo < L.Hi, and I1 >= L.Lo would not
                // prove that the inner part reaches b (reviewed); b's bound is the path below
                // a real path: to the nearest inner point, then along the edge
                SetUpper(a, (Fn(c, s, c.I0, c.I0) + I(c.I0)).Hi);
                SetUpper(b, (Fn(c, s, c.I1, c.I1) + (L - I(c.I1))).Hi);
            }
        }

        // ------------------------------------------------------------------ propagation

        void Propagate(Window w)
        {
            Index2i ev = Mesh.GetEdgeV(w.E); ExactInterval L = edgeLen[w.E];
            int c = Third(w.F, ev.a, ev.b); P2 C = sidePlace[Side(w.E, w.F)]; P2 s = Box(w);
            P2 A = new P2(I(0), I(0)), B = new P2(L, I(0));
            ExactInterval sig = new ExactInterval(w.SigLo, w.SigHi);
            // the ray through C: x where the line from the source through C meets the edge
            ExactInterval xc = s.X + (C.X - s.X) * (-s.Y) / (C.Y - s.Y);
            bool cPossible = xc.Hi >= w.O0 && xc.Lo <= w.O1;
            if (cPossible) SetLower(c, (Dist(s, C.X, C.Y) + sig).Lo);
            if (w.I1 > w.I0) {
                if (xc.Lo >= w.I0 && xc.Hi <= w.I1) SetUpper(c, (Dist(s, C.X, C.Y) + sig).Hi);
                else { double x = Math.Min(Math.Max(Mid(xc), w.I0), w.I1); SetUpper(c, (Fn(w, s, x, x) + Dist(new P2(I(x), I(0)), C.X, C.Y)).Hi); }
            }
            if (w.O0 < xc.Hi) Child(w, s, ev.a, c, A, C, w.O0, Math.Min(w.O1, xc.Hi), w.I0, Math.Min(w.I1, xc.Lo));
            if (w.O1 > xc.Lo) Child(w, s, c, ev.b, C, B, Math.Max(w.O0, xc.Lo), w.O1, Math.Max(w.I0, xc.Hi), w.I1);
        }

        // the parameter (0..1 along P->Q) where the line from s through (t, 0) meets PQ
        static ExactInterval Mu(P2 s, double t, P2 P, P2 Q)
        {
            ExactInterval rx = I(t) - s.X, ry = -s.Y, ex = Q.X - P.X, ey = Q.Y - P.Y, dx = s.X - P.X, dy = s.Y - P.Y;
            return (dx * ry - dy * rx) / (ex * ry - ey * rx);
        }

        // the denominator of Mu, ex * ry - ey * rx, certainly nonzero for every source in the box s and every t in [t0, t1]
        static bool DenominatorCertain(P2 s, double t0, double t1, P2 P, P2 Q)
        {
            ExactInterval rx = new ExactInterval(t0, t1) - s.X, ry = -s.Y, ex = Q.X - P.X, ey = Q.Y - P.Y;
            ExactInterval d = ex * ry - ey * rx;
            return d.Lo > 0 || d.Hi < 0;
        }

        // the window w's disk carried into the frame of the edge (pa, pb) whose ends are at the boxes PA, PB in w's frame;
        // the face the new window propagates into lies on y > 0 there (Appendix A.2, with the review's S3 error term)
        static bool Transform(Window w, P2 PA, P2 PB, out double cx, out double cy, out double r) => TransformDisk(w.Cx, w.Cy, w.R, PA, PB, out cx, out cy, out r);

        /// <summary> the disk (wcx, wcy, wr) of a frame carried into the frame of the edge from PA to PB (boxes in the old frame):
        /// PA at the origin, PB on +x, the old origin side on y &lt;= 0. Returns false if the boxes are too wide for the bound. </summary>
        internal static bool TransformDisk(double wcx, double wcy, double wr, P2 PA, P2 PB, out double cx, out double cy, out double r)
        {
            // dP, dQ: the distance of the true ends from the computed midpoints (half widths summed: at least the half
            // diagonal, and no underflow of their squares), plus the midpoints' rounding
            double px = Mid(PA.X), py = Mid(PA.Y), qx = Mid(PB.X), qy = Mid(PB.Y);
            double dP = (HalfWidth(PA.X) + HalfWidth(PA.Y)) * (1 + 1e-15) + 2 * (Ulp(px) + Ulp(py));
            double dQ = (HalfWidth(PB.X) + HalfWidth(PB.Y)) * (1 + 1e-15) + 2 * (Ulp(qx) + Ulp(qy));
            double wx = qx - px, wy = qy - py, len = Hypot(wx, wy);
            cx = cy = r = 0;
            double lenLo = len * (1 - 4e-16) - dP - dQ;
            if (!(lenLo > 0)) return false;
            double dx = wx / len, dy = wy / len;
            double ux = wcx - px, uy = wcy - py;
            cx = ux * dx + uy * dy; cy = -ux * dy + uy * dx;
            if (cy > 0) cy = -cy;              // reflection when the orientation flips (an isometry)
            // 2 asin(z) <= 2 z / sqrt(1 - z^2) for 0 <= z < 1 (asin z <= tan(asin z)): no libm, so the bits are the same on every platform
            double z = (dP + dQ) / (2 * lenLo);
            if (!(z < 0.5)) return false;
            double alpha = 2 * z / Math.Sqrt(1 - z * z) * (1 + 1e-14) + 8 * Ulp(len) / len;
            double cn = Hypot(ux, uy);
            double rot = (cn + dP + wr) * alpha;
            double rounding = 16 * (Ulp(cn) + Ulp(cx) + Ulp(cy) + Ulp(px) + Ulp(py) + Ulp(wcx) + Ulp(wcy));
            r = (wr + dP + rot + rounding) * (1 + 1e-15);
            return true;
        }

        static readonly double TwoP600 = BitConverter.Int64BitsToDouble((long)(1023 + 600) << 52), TwoM600 = BitConverter.Int64BitsToDouble((long)(1023 - 600) << 52);

        /// <summary> sqrt(x^2 + y^2) without underflow or overflow of the squares: the arguments are scaled by powers of two
        /// (exact) so that the larger lies in [1, 2), then the result is scaled back (exact unless it is subnormal, where
        /// the error is at most half the smallest spacing). Relative error below 3 units of 2^-53 otherwise, as for the
        /// plain formula without underflow; reviewed: the plain formula lost the length of edges near 1e-154 </summary>
        internal static double Hypot(double x, double y)
        {
            double ax = Math.Abs(x), ay = Math.Abs(y), m = Math.Max(ax, ay);
            if (!(m > 0) || double.IsInfinity(m)) return m;
            double pre = 1;
            if (m < 1e-300) pre = TwoP600; else if (m > 1e300) pre = TwoM600;
            ax *= pre; ay *= pre; m *= pre;
            int e = (int)((BitConverter.DoubleToInt64Bits(m) >> 52) & 0x7FF);
            double sc = BitConverter.Int64BitsToDouble((long)(2046 - e) << 52);      // 2^(1023 - unbiased): m * sc in [1, 2)
            ax *= sc; ay *= sc;
            return Math.Sqrt(ax * ax + ay * ay) / sc / pre;
        }

        void Child(Window w, P2 s, int p, int q, P2 P, P2 Q, double t0, double t1, double ti0, double ti1)
        {
            int e = Mesh.FindEdge(p, q); int f2 = Other(e, w.F);
            Index2i ev = Mesh.GetEdgeV(e); ExactInterval L2 = edgeLen[e];
            double u0, u1, v0 = 1, v1 = 0;      // outer and inner parameters from p (inner empty if v0 > v1)
            ExactInterval m0 = Mu(s, t0, P, Q), m1 = Mu(s, t1, P, Q);
            // Mu(s, t) is a ratio of two functions affine in t, so for a fixed s it is monotone in t wherever its
            // denominator D keeps its sign; only then do the end values bound the crossings of the whole clip [t0, t1].
            // D is evaluated with t as the interval [t0, t1] (it is affine in t: the interval encloses it over the box
            // and the clip). Reviewed: with t1 = xc.Hi past the ray parallel to PQ the end values gave [0, ~0]
            if (!DenominatorCertain(s, t0, t1, P, Q) ||
                double.IsInfinity(m0.Lo) || double.IsInfinity(m0.Hi) || double.IsInfinity(m1.Lo) || double.IsInfinity(m1.Hi)) {
                // D may be 0 (a ray parallel to the edge within the clip, or only the interval's widening on a thin
                // sliver) or an end crossing overflowed: the whole edge, nothing certain (precision lost, not soundness)
                u0 = 0; u1 = L2.Hi;
            } else {
                // the hull of both ends: the crossing of every clip point lies between its two end crossings
                u0 = Math.Max(0, (I(Math.Min(m0.Lo, m1.Lo)) * L2).Lo); u1 = Math.Min(L2.Hi, (I(Math.Max(m0.Hi, m1.Hi)) * L2).Hi);
                if (ti1 > ti0) {
                    ExactInterval n0 = Mu(s, ti0, P, Q), n1 = Mu(s, ti1, P, Q);
                    if (!double.IsInfinity(n0.Hi) && !double.IsInfinity(n1.Lo)) { v0 = Math.Max(0, (n0 * L2).Hi); v1 = Math.Min(L2.Lo, (n1 * L2).Lo); }
                }
            }
            if (!(u1 > u0)) return;
            double o0, o1, i0, i1;
            if (ev.a == p) { o0 = u0; o1 = u1; i0 = v0; i1 = v1; }
            else {
                o0 = Math.Max(0, (L2 - I(u1)).Lo); o1 = Math.Min(L2.Hi, (L2 - I(u0)).Hi);
                if (v1 > v0) { i0 = (L2 - I(v1)).Hi; i1 = (L2 - I(v0)).Lo; } else { i0 = 1; i1 = 0; }
            }
            P2 PA = ev.a == p ? P : Q, PB = ev.a == p ? Q : P;
            if (!Transform(w, PA, PB, out double cx, out double cy, out double r)) { failed = true; failure = Result.PositionTooWide; return; }
            var cw = new Window { E = e, F = f2, Origin = w.Origin, SigLo = w.SigLo, SigHi = w.SigHi, Cx = cx, Cy = cy, R = r, O0 = o0, O1 = o1, I0 = i0, I1 = i1 };
            if (f2 < 0) { Record(cw, L2, ev.a, ev.b); return; }   // terminal window on a boundary edge (M1)
            Add(cw);
        }
    }
}
