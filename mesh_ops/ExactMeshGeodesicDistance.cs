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
    ///    windows of the same origin and nearly the same position (A6);
    ///  - windows on boundary edges are terminal: they record the bounds of their edge ends (M1);
    ///  - boundary vertices and vertices that are not certainly convex, flat vertices included, are pseudo-sources (A5,
    ///    review S1); the continuation windows of A7 are not used (they duplicate the ordinary windows on flat regions, an
    ///    open item of the design note).
    /// Reach (measured, net10 Release): terrains up to 41 x 41 well under a second, a plane 41 x 41 in about 4 s, the
    /// exactly aligned ridge of gate 3 only up to about 21 x 21 (at 41 x 41 it reaches the window limit). On flat regions
    /// Upper is up to about 1.3% above the distance; Lower is tight. A limit ends with WindowLimit and NaN arrays.
    /// The result is deterministic (serial, every operation IEEE-rounded and widened) and scales exactly with 2^k scalings
    /// of the input (away from overflow and the subnormal range).
    /// </summary>
    internal sealed class ExactMeshGeodesicDistance
    {
        public enum Result { Ok, NoSource, NonManifoldInput, DegenerateTriangles, WindowLimit, PositionTooWide }

        public readonly DMesh3 Mesh;
        /// <summary> limit on the windows created; reaching it ends Compute with WindowLimit (never with a wrong bound). The default
        /// keeps one Compute below about 20 s (a flat plane of 101 x 101 vertices reaches it; a terrain of that size needs
        /// about 130000 windows, 4 s) </summary>
        public int MaxWindows = 300_000;
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

        struct P2 { public ExactInterval X, Y; public P2(ExactInterval x, ExactInterval y) { X = x; Y = y; } }

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
        internal long Emissions, Merges, Iterations;
        Heap queue;
        double[] lo, up, emitted;
        byte[] kind;                // 0 convex (geodesics do not pass), 1 pseudo-source
        bool failed;
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
        // by two spacings on each side. CoreMath.Atan2 is the CORE-MATH port, documented as correctly rounded (error at most
        // half a spacing; docs/SOURCES.md, "Correctly rounded sin, cos, atan2, exp, log"); the widening by two spacings
        // leaves a margin of 1.5 spacings beyond that bound.
        ExactInterval CornerAngle(int p, int a, int b)
        {
            Vector3d P = Mesh.GetVertex(p), A = Mesh.GetVertex(a), B = Mesh.GetVertex(b);
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
            foreach (int v in Mesh.VertexIndices()) kind[v] = (byte)VertexKind(v);
        }

        /// <summary> 0: certainly convex, so no shortest path passes through it (Fact F, and the local argument of A.5 for a
        /// flat vertex), 1: a pseudo-source (a boundary vertex, or not certainly convex, flat vertices included: review S1).
        /// For tests. </summary>
        internal int VertexKind(int v)
        {
            if (Mesh.IsBoundaryVertex(v)) return 1;
            var fan = Fan(v);
            ExactInterval sum = I(0);
            for (int i = 0; i < fan.Count; ++i) sum = sum + CornerAngle(v, fan[i], fan[(i + 1) % fan.Count]);
            // Math.PI is the double below pi, so 2 * Math.PI (an exact doubling) is below 2 pi
            if (sum.Hi < 2 * Math.PI) return 0;
            return 1;
        }

        // ------------------------------------------------------------------ compute

        public Result Compute(IEnumerable<int> sources)
        {
            int n = Mesh.MaxVertexID;
            Lower = new double[n]; Upper = new double[n];
            Status = run(sources);
            if (Status != Result.Ok)
                for (int v = 0; v < n; ++v) { Lower[v] = double.NaN; Upper[v] = double.NaN; }
            return Status;
        }

        Result run(IEnumerable<int> sources)
        {
            int n = Mesh.MaxVertexID;
            windows = new List<Window>(); bySide = new Dictionary<int, List<int>>(); queue = new Heap();
            lo = new double[n]; up = new double[n]; emitted = new double[n]; kind = new byte[n]; failed = false; Windows = 0;
            Emissions = Merges = Iterations = 0;
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
            for (int v = 0; v < n; ++v) {
                bool ok = Mesh.IsVertex(v) && lo[v] < double.PositiveInfinity && up[v] < double.PositiveInfinity;
                Lower[v] = ok ? lo[v] : double.NaN; Upper[v] = ok ? up[v] : double.NaN;
            }
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
                Index2i ev = Mesh.GetEdgeV(e); ExactInterval L = Len(ev.a, ev.b);
                P2 V = Place(ev.a, ev.b, v, L);
                double r0 = RoundUpSum(Math.Sqrt(HalfWidth(V.X) * HalfWidth(V.X) + HalfWidth(V.Y) * HalfWidth(V.Y)) * 1.0000001, 4 * (Ulp(V.X.Hi) + Ulp(V.Y.Hi)));
                Add(new Window { E = e, F = f2, Origin = v, SigLo = sl, SigHi = up[v], Cx = Mid(V.X), Cy = -Mid(V.Y), R = r0, O0 = 0, O1 = L.Hi, I0 = 0, I1 = L.Lo });
            }
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

        // the parts of [l, r] (inside b's inner interval) where a is certainly worse than b, to the given depth
        void Dominated(Window a, P2 sa, Window b, P2 sb, double l, double r, int depth, List<(double l, double r)> rem)
        {
            if (!(r > l)) return;
            if (Fn(a, sa, l, r).Lo > Fn(b, sb, l, r).Hi) { rem.Add((l, r)); return; }
            if (depth == 0) return;
            double m = 0.5 * l + 0.5 * r;
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
            Index2i ev = Mesh.GetEdgeV(w.E); ExactInterval L = Len(ev.a, ev.b); P2 s = Box(w);
            double tol = 1e-12 * L.Hi;
            var segs = new List<(double l, double r)> { (w.O0, w.O1) };
            int z = Third(w.F, ev.a, ev.b); P2 Z = Place(ev.a, ev.b, z, L);
            TrimByVertex(w, s, I(0), I(0), ev.a, segs); TrimByVertex(w, s, L, I(0), ev.b, segs); TrimByVertex(w, s, Z.X, Z.Y, z, segs);
            segs.RemoveAll(t => !(t.r - t.l > tol)); if (segs.Count == 0) return;
            int side = Side(w.E, w.F);
            if (!bySide.TryGetValue(side, out var list)) { list = new List<int>(); bySide[side] = list; }
            int dead = 0; foreach (int oi in list) if (!(windows[oi].O1 > windows[oi].O0)) ++dead;
            if (dead > 8 && 2 * dead > list.Count) list.RemoveAll(oi => !(windows[oi].O1 > windows[oi].O0));
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
                    bool near = !b.Done && nr <= 2 * mr && Math.Max(b.R, nr) <= 1e-11 * (1 + Math.Abs(b.Cx) + Math.Abs(b.Cy))
                        && Math.Abs(b.SigHi - w.SigHi) <= 1e-12 * (1 + Math.Abs(b.SigHi)) && Math.Abs(b.SigLo - w.SigLo) <= 1e-12 * (1 + Math.Abs(b.SigHi));
                    if (near) {
                        var keep = new List<(double l, double r)>(); bool merged = false;
                        foreach (var sg in segs) {
                            if (sg.l <= b.O1 + tol && sg.r >= b.O0 - tol) { b.O0 = Math.Min(b.O0, sg.l); b.O1 = Math.Max(b.O1, sg.r); merged = true; }
                            else keep.Add(sg);
                        }
                        if (merged) {
                            ++Merges; b.R = Math.Max(b.R, nr); b.SigLo = Math.Min(b.SigLo, w.SigLo); b.SigHi = Math.Max(b.SigHi, w.SigHi);
                            // the inner interval of only one of them (A6)
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
                    if (rem.Count > 0) { segs = Minus(segs, rem); segs.RemoveAll(t => !(t.r - t.l > tol)); if (segs.Count == 0) return; }
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

        // the bounds a window gives its edge ends; the ray through a flat edge end continues (A7)
        void Record(Window c, ExactInterval L, int a, int b)
        {
            P2 s = Box(c);
            ExactInterval sig = new ExactInterval(c.SigLo, c.SigHi);
            ExactInterval da = Dist(s, I(0), I(0)) + sig, db = Dist(s, L, I(0)) + sig;
            if (c.O0 <= 0) SetLower(a, da.Lo);
            if (c.O1 >= L.Lo) SetLower(b, db.Lo);
            if (c.I1 > c.I0) {
                if (c.I0 <= 0) SetUpper(a, da.Hi);
                if (c.I1 >= L.Hi) SetUpper(b, db.Hi);
                // a real path: to the nearest inner point, then along the edge
                SetUpper(a, (Fn(c, s, c.I0, c.I0) + I(c.I0)).Hi);
                SetUpper(b, (Fn(c, s, c.I1, c.I1) + (L - I(c.I1))).Hi);
            }
        }

        // ------------------------------------------------------------------ propagation

        void Propagate(Window w)
        {
            Index2i ev = Mesh.GetEdgeV(w.E); ExactInterval L = Len(ev.a, ev.b);
            int c = Third(w.F, ev.a, ev.b); P2 C = Place(ev.a, ev.b, c, L); P2 s = Box(w);
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

        // the window w's disk carried into the frame of the edge (pa, pb) whose ends are at the boxes PA, PB in w's frame;
        // the face the new window propagates into lies on y > 0 there (Appendix A.2, with the review's S3 error term)
        bool Transform(Window w, P2 PA, P2 PB, out double cx, out double cy, out double r)
        {
            double px = Mid(PA.X), py = Mid(PA.Y), qx = Mid(PB.X), qy = Mid(PB.Y);
            double dP = Math.Sqrt(HalfWidth(PA.X) * HalfWidth(PA.X) + HalfWidth(PA.Y) * HalfWidth(PA.Y)) * (1 + 1e-15) + Ulp(px) + Ulp(py);
            double dQ = Math.Sqrt(HalfWidth(PB.X) * HalfWidth(PB.X) + HalfWidth(PB.Y) * HalfWidth(PB.Y)) * (1 + 1e-15) + Ulp(qx) + Ulp(qy);
            double wx = qx - px, wy = qy - py, len = Math.Sqrt(wx * wx + wy * wy);
            cx = cy = r = 0;
            double lenLo = len * (1 - 4e-16) - dP - dQ;
            if (!(lenLo > 0)) return false;
            double dx = wx / len, dy = wy / len;
            double ux = w.Cx - px, uy = w.Cy - py;
            cx = ux * dx + uy * dy; cy = -ux * dy + uy * dx;
            if (cy > 0) cy = -cy;              // reflection when the orientation flips (an isometry)
            // 2 asin(z) <= 2 z / sqrt(1 - z^2) for 0 <= z < 1 (asin z <= tan(asin z)): no libm, so the bits are the same on every platform
            double z = (dP + dQ) / (2 * lenLo);
            if (!(z < 0.5)) return false;
            double alpha = 2 * z / Math.Sqrt(1 - z * z) * (1 + 1e-14) + 8 * Ulp(len) / len;
            double cn = Math.Sqrt(ux * ux + uy * uy);
            double rot = (cn + dP + w.R) * alpha;
            double rounding = 16 * (Ulp(cn) + Ulp(cx) + Ulp(cy) + Ulp(px) + Ulp(py) + Ulp(w.Cx) + Ulp(w.Cy));
            r = (w.R + dP + rot + rounding) * (1 + 1e-15);
            return true;
        }

        void Child(Window w, P2 s, int p, int q, P2 P, P2 Q, double t0, double t1, double ti0, double ti1)
        {
            int e = Mesh.FindEdge(p, q); int f2 = Other(e, w.F);
            Index2i ev = Mesh.GetEdgeV(e); ExactInterval L2 = Len(ev.a, ev.b);
            double u0, u1, v0 = 1, v1 = 0;      // outer and inner parameters from p (inner empty if v0 > v1)
            ExactInterval m0 = Mu(s, t0, P, Q), m1 = Mu(s, t1, P, Q);
            if (double.IsInfinity(m0.Lo) || double.IsInfinity(m0.Hi) || double.IsInfinity(m1.Lo) || double.IsInfinity(m1.Hi)) {
                u0 = 0; u1 = L2.Hi;             // a ray parallel to the edge is possible: the whole edge, nothing certain
            } else {
                u0 = Math.Max(0, (m0 * L2).Lo); u1 = Math.Min(L2.Hi, (m1 * L2).Hi);
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
