// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary> The outcome of <see cref="PolyhedralGeodesicDistance.Compute"/>. </summary>
    /// <summary> PartiallyReached: the run finished, but a vertex of a source's connected component (reachable over the edges) has no distance
    /// (MaxDistance infinite); the reached values are in Distance, UnreachedVertices counts the rest. Seen on triangles of aspect ratio beyond about 1e5. </summary>
    public enum PolyhedralGeodesicStatus { NotComputed, Ok, NoSource, NonManifoldInput, DegenerateTriangles, WindowLimit, Cancelled, PartiallyReached }

    /// <summary>
    /// The geodesic distance along the surface of a triangle mesh (the shortest path on the piecewise flat surface, the
    /// "polyhedral" distance) from one or more source vertices to every vertex, by window propagation (Mitchell, Mount and
    /// Papadimitriou 1987; Surazhsky et al. 2005) in plain double arithmetic. Meant for meshes of 10^4 to 10^5 vertices.
    /// <para>Accuracy: <b>measured on the tested families, not a certified bound</b> (docs/design/GEODESICS.md 6e, A.7). The
    /// tests enforce |Distance - d| &lt;= 5e-15 d + 5e-16 extent against the closed forms and the certified enclosure of the internal
    /// oracle <c>ExactMeshGeodesicDistance</c> (a distance outside [Lower, Upper] by more than that fails), and Distance never below the
    /// straight 3D distance by more than that. Measured: on 24 runs (12 families of 31 x 31 and 41 x 41 vertices, planes, a terrain, a saddle,
    /// domes, ridges, a valley, an L-shape, two sources) the result lies inside the enclosure, which is up to 2e-8 relative wide on
    /// the terrain, saddle and plane families, so the agreement claimed there is that of the enclosure; the largest deviation from a
    /// closed form or from the straight line was 2.4e-15 relative. The sliver test (a height of 1e-7 to 1e-12 against a length of 1) enforces 1e-8 against the
    /// straight distance. For a certified enclosure use the oracle (up to about 10^4 vertices, serial), for a smooth
    /// approximation on any mesh <see cref="gs.MeshGeodesicDistance"/> (the heat method).</para>
    /// <para><b>Limits.</b> (1) Windows per vertex grow with the grid width on meshes with many saddle vertices: on the saddle 0.3 (x^2 - y^2) the final windows are
        /// essentially all needed (each is the minimum of its edge somewhere; GEODESICS.md A.7 note 11), about 0.8 to 0.9 per vertex and grid width (64 at about 78 x 78 vertices, about 120 at 141 x 141,
        /// about 270 at 317 x 317). Such a mesh needs an explicit <see cref="MaxWindows"/> beyond about 78 x 78 vertices; at the default budget of 64 per vertex the run ends with
        /// <see cref="PolyhedralGeodesicStatus.WindowLimit"/> (after about 13 s at 317 x 317). (2) On meshes whose triangles have an aspect ratio beyond about 10^5 (a strip of height 1e-6 against a length of 1 with several rows)
    /// a vertex can stay unreached; the status is then <see cref="PolyhedralGeodesicStatus.PartiallyReached"/> (owner decision 2026-11-19), the reached
    /// values are kept and <see cref="UnreachedVertices"/> counts the others. Ok guarantees a distance for every vertex of the sources' components
    /// when MaxDistance is infinite.</para>
    /// <para><b>Paths.</b> <see cref="TracePath(int)"/> returns the shortest path from the nearest source to a vertex (or, with a barycentric point, to a point in a triangle) as a polyline on the
    /// surface, by backtracing through the unfolded origin images (GEODESICS.md A.8); its length equals the distance of the run to 1e-14 relative on the tested families (measured at most 6.0e-15, growing with the number of triangles crossed).</para>
    /// <para>Memory: a window is a 64-byte struct; a run holds at most about 120 bytes x (MaxWindows + 2 x the largest vertex
    /// valence) plus about 89 bytes per vertex (see the design note; 32 of them are the record of how a vertex's distance arose, for <see cref="TracePath(int)"/>). Serial (one Compute at a time per instance) and deterministic: every operation is IEEE
    /// double without FMA, and 2^k scalings of the input give the same bits (away from overflow and the subnormal range).</para>
    /// </summary>
    public sealed partial class PolyhedralGeodesicDistance
    {
        /// <summary> The wording of the accuracy claim; the numbers are in the class remarks and are the constants of the tests </summary>
        public const string AccuracyNote = "measured on the tested families, not a certified bound";

        /// <summary> the mesh (read only during Compute) </summary>
        public DMesh3 Mesh { get; private set; }
        /// <summary> no window is created beyond this distance; vertices farther away stay NaN. NaN or negative throws in Compute; 0 computes the sources only </summary>
        public double MaxDistance = double.PositiveInfinity;
        /// <summary> the limit on the windows created; 0 means 64 x the vertex count. Checked between steps, so Windows can end up above it by the windows of one step. Passing it ends Compute with WindowLimit and NaN arrays </summary>
        public long MaxWindows = 0;
        /// <summary> optional, polled between steps; returning true ends Compute with Cancelled and NaN arrays </summary>
        public Func<bool> CancelRequested;
        public PolyhedralGeodesicStatus Status { get; private set; }
        /// <summary> per vertex id: the distance to the nearest source; NaN for unused ids, unreached vertices, and every vertex unless Status is Ok or PartiallyReached </summary>
        public double[] Distance { get; private set; }
        /// <summary> per vertex id: the source that realises Distance (the lowest id at an exact tie), -1 where Distance is NaN </summary>
        public int[] NearestSource { get; private set; }
        /// <summary> the windows created by the last Compute </summary>
        public long Windows { get; private set; }
        /// <summary> with MaxDistance infinite: the vertices of the sources' connected components that got no distance (0 unless Status is PartiallyReached) </summary>
        public int UnreachedVertices { get; private set; }
        /// <summary> merges of windows in the last Compute (diagnostics) </summary>
        internal long Merges { get; private set; }

        public PolyhedralGeodesicDistance(DMesh3 mesh) { Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh)); Status = PolyhedralGeodesicStatus.NotComputed; }

        // ------------------------------------------------------------------ data

        // 64 bytes: the sign bit of Side is the "propagated" flag (Side = ~side), a window with B1 <= B0 is dead
        struct Window
        {
            public int Side, Origin;
            public double B0, B1;       // outer interval along the edge from its first vertex
            public double Sx, Sy;       // the unfolded origin in the frame of the edge (a at 0, b at +x), Sy <= 0
            public double Sigma;        // distance of the origin
            public double Xi;           // accumulated distance error
            public double E;            // angular error radius (radians) of the rays of the window; the error of an end on the edge is E x R^2 / h (EP)
        }

        // work lists of Add (it is not re-entrant): no allocation per call
        List<(double l, double r)> segA = new List<(double l, double r)>(), segB = new List<(double l, double r)>(), remM = new List<(double l, double r)>(), minusA = new List<(double l, double r)>(), minusB = new List<(double l, double r)>();

        const int ChunkBits = 16, ChunkSize = 1 << ChunkBits;
        Window[][] wchunks;
        int[][] nchunks;                // next window of the same side list
        int[][] pchunks;                // heap position of a window, -1 if not queued
        int wcount;
        ref Window W(int i) => ref wchunks[i >> ChunkBits][i & (ChunkSize - 1)];

        int[] head;                     // per edge side: first window
        // heap (indexed): item >= 0 is a window, item < 0 is the emission event of vertex ~item
        int[] hid = new int[64]; double[] hkey = new double[64]; int hn;
        int[] vpos;

        double[] dist, xiV, emitted; int[] root; byte[] kind; bool[] flat;
        double[] vx, vy, vz;
        double[] elen;
        double extent, cEps, delta, xiCap;
        // Unit scale (#1377, MAGNITUDE_ROBUSTNESS_2026-11.md option (a)): the positions vx, vy, vz, the lengths and the windows are
        // in lengths times 2^-scaleExp (the exponent of the longest edge), so that a mesh scaled by 2^j runs bit for bit the same at every
        // scale and the products of four lengths in Place stay in the double range. Distance, the paths and MaxDistance are in the
        // units of the mesh; maxDistU is MaxDistance in unit scale.
        int scaleExp;
        double maxDistU;
        double Down(double x) => scaleExp == 0 ? x : ExactPoint3.ScaleB(x, -scaleExp);
        double Up(double x) => scaleExp == 0 ? x : ExactPoint3.ScaleB(x, scaleExp);
        Vector3d Down(Vector3d v) => scaleExp == 0 ? v : new Vector3d(Down(v.x), Down(v.y), Down(v.z));
        const double DisparityLimit = 6.223015277861142e-61;      // 2^-200 of the longest edge, see IntrinsicTriangulation.HasLengthDisparity
        long maxWindows;
        int maxValence;

        const double Eps = 1.1102230246251565E-16;        // 2^-53

        // ------------------------------------------------------------------ compute

        public PolyhedralGeodesicStatus Compute(IEnumerable<int> sourceVertices)
        {
            if (double.IsNaN(MaxDistance) || MaxDistance < 0) throw new ArgumentOutOfRangeException(nameof(MaxDistance));
            int n = Mesh.MaxVertexID;
            Status = PolyhedralGeodesicStatus.NotComputed;
            Distance = new double[n]; NearestSource = new int[n];
            for (int v = 0; v < n; ++v) { Distance[v] = double.NaN; NearestSource[v] = -1; }
            Windows = 0; Merges = 0; UnreachedVertices = 0;
            Status = Run(sourceVertices);
            return Status;
        }

        PolyhedralGeodesicStatus Run(IEnumerable<int> sourceVertices)
        {
            int n = Mesh.MaxVertexID;
            var src = new List<int>();
            if (sourceVertices != null) foreach (int s in sourceVertices) if (s >= 0 && s < n && Mesh.IsVertex(s) && !src.Contains(s)) src.Add(s);
            if (src.Count == 0) return PolyhedralGeodesicStatus.NoSource;
            foreach (int v in Mesh.VertexIndices()) if (Mesh.IsBowtieVertex(v)) return PolyhedralGeodesicStatus.NonManifoldInput;
            scaleExp = gs.IntrinsicTriangulation.EdgeExponent(Mesh);
            maxDistU = Down(MaxDistance);
            foreach (int t in Mesh.TriangleIndices()) {
                Index3i tri = Mesh.GetTriangle(t);
                Vector3d a = Down(Mesh.GetVertex(tri.a)), b = Down(Mesh.GetVertex(tri.b)), c = Down(Mesh.GetVertex(tri.c));       // (unit scale: the squares of the cross products stay in the double range)
                ExactInterval ux = ExactInterval.Of(b.x) - ExactInterval.Of(a.x), uy = ExactInterval.Of(b.y) - ExactInterval.Of(a.y), uz = ExactInterval.Of(b.z) - ExactInterval.Of(a.z);
                ExactInterval wx = ExactInterval.Of(c.x) - ExactInterval.Of(a.x), wy = ExactInterval.Of(c.y) - ExactInterval.Of(a.y), wz = ExactInterval.Of(c.z) - ExactInterval.Of(a.z);
                ExactInterval area2 = ExactInterval.Square(uy * wz - uz * wy) + ExactInterval.Square(uz * wx - ux * wz) + ExactInterval.Square(ux * wy - uy * wx);
                if (!(area2.Lo > 0)) return PolyhedralGeodesicStatus.DegenerateTriangles;
            }
            AxisAlignedBox3d bounds = Mesh.CachedBounds;
            extent = Down(Math.Max(bounds.Max.x - bounds.Min.x, Math.Max(bounds.Max.y - bounds.Min.y, bounds.Max.z - bounds.Min.z)));
            cEps = 16 * Eps * extent; delta = 1e-12 * extent; xiCap = 1e-6 * extent;
            maxWindows = Math.Min(MaxWindows > 0 ? MaxWindows : 64L * Mesh.VertexCount, 1L << 30);   // window ids and the heap are int
            maxValence = 0;
            vx = new double[n]; vy = new double[n]; vz = new double[n];
            foreach (int v in Mesh.VertexIndices()) { Vector3d p = Mesh.GetVertex(v); vx[v] = Down(p.x); vy[v] = Down(p.y); vz[v] = Down(p.z); maxValence = Math.Max(maxValence, Mesh.GetVtxEdgeCount(v)); }
            int me = Mesh.MaxEdgeID;
            elen = new double[me];
            foreach (int e in Mesh.EdgeIndices()) { Index2i ev = Mesh.GetEdgeV(e); elen[e] = Len(ev.a, ev.b); }
            foreach (int e in Mesh.EdgeIndices()) if (elen[e] > 0 && elen[e] < DisparityLimit) return PolyhedralGeodesicStatus.DegenerateTriangles;     // edge lengths more than 2^200 apart
            vrec = new VRec[n];
            dist = new double[n]; xiV = new double[n]; emitted = new double[n]; root = new int[n]; kind = new byte[n]; vpos = new int[n];
            for (int v = 0; v < n; ++v) { dist[v] = emitted[v] = double.PositiveInfinity; root[v] = -1; vpos[v] = -1; }
            var oracle = new ExactMeshGeodesicDistance(Mesh);
            flat = new bool[n];
            foreach (int v in Mesh.VertexIndices()) { kind[v] = Mesh.GetVtxTriangleCount(v) == 0 ? (byte)0 : (byte)oracle.VertexKind(v); flat[v] = kind[v] == 0 && oracle.IsFlatVertex(v); }
            head = new int[2 * me]; for (int i = 0; i < head.Length; ++i) head[i] = -1;
            int firstChunk = (int)Math.Min(ChunkSize, maxWindows + 2L * maxValence + 2);
            wchunks = new Window[1][] { new Window[firstChunk] }; nchunks = new int[1][] { new int[firstChunk] }; pchunks = new int[1][] { new int[firstChunk] };
            wcount = 0; hn = 0;

            foreach (int s in src) { kind[s] = 1; root[s] = s; dist[s] = 0; vrec[s].Kind = 1; Push(~s, 0); }
            long pops = 0;
            while (hn > 0) {
                if (Windows > maxWindows) return PolyhedralGeodesicStatus.WindowLimit;
                if ((++pops & 1023) == 0 && CancelRequested != null && CancelRequested()) return PolyhedralGeodesicStatus.Cancelled;
                int id = PopMin();
                if (id < 0) { int v = ~id; if (dist[v] < emitted[v]) Emit(v); continue; }
                ref Window w = ref W(id);
                if (w.Side < 0 || !(w.B1 > w.B0)) continue;
                int side = w.Side; w.Side = ~side;
                Propagate(id, side);
            }
            for (int v = 0; v < n; ++v)
                if (Mesh.IsVertex(v) && dist[v] <= maxDistU && dist[v] < double.PositiveInfinity) { Distance[v] = Up(dist[v]); NearestSource[v] = root[v]; }
            if (double.IsPositiveInfinity(MaxDistance)) {
                // Ok guarantees a distance for every vertex of the sources' components: a breadth-first walk over the edges from the sources
                var seen = new bool[n]; var queue = new Queue<int>();
                foreach (int s0 in src) { seen[s0] = true; queue.Enqueue(s0); }
                int unreached = 0;
                while (queue.Count > 0) {
                    int v = queue.Dequeue();
                    if (!(dist[v] < double.PositiveInfinity)) ++unreached;
                    foreach (int nb in Mesh.VtxVerticesItr(v)) if (!seen[nb]) { seen[nb] = true; queue.Enqueue(nb); }
                }
                if (unreached > 0) { UnreachedVertices = unreached; return PolyhedralGeodesicStatus.PartiallyReached; }
            }
            return PolyhedralGeodesicStatus.Ok;
        }

        // ------------------------------------------------------------------ geometry

        double Len(int a, int b) { double dx = vx[a] - vx[b], dy = vy[a] - vy[b], dz = vz[a] - vz[b]; return Math.Sqrt(dx * dx + dy * dy + dz * dz); }
        static double Hyp(double x, double y) => Math.Sqrt(x * x + y * y);

        // the position of c in the frame of the edge (a at the origin, b on +x), on the side y >= 0
        void Place(int a, int b, int c, double L, out double x, out double y)
        {
            double ux = vx[b] - vx[a], uy = vy[b] - vy[a], uz = vz[b] - vz[a], wx = vx[c] - vx[a], wy = vy[c] - vy[a], wz = vz[c] - vz[a];
            double dot = ux * wx + uy * wy + uz * wz;
            double cx = uy * wz - uz * wy, cy = uz * wx - ux * wz, cz = ux * wy - uy * wx;
            x = dot / L; y = Math.Sqrt(cx * cx + cy * cy + cz * cz) / L;
        }

        int Third(int f, int a, int b) { Index3i t = Mesh.GetTriangle(f); for (int k = 0; k < 3; ++k) if (t[k] != a && t[k] != b) return t[k]; return -1; }
        int Other(int e, int f) { Index2i et = Mesh.GetEdgeT(e); return et.a == f ? et.b : et.a; }
        int SideOf(int e, int f) => 2 * e + (Mesh.GetEdgeT(e).a == f ? 0 : 1);
        int FaceOfSide(int side) { Index2i et = Mesh.GetEdgeT(side >> 1); return (side & 1) == 0 ? et.a : et.b; }

        // the window's distance function over [l, r] of its edge: minimum at the point nearest to the origin, maximum at an end
        static double FMin(in Window w, double l, double r) { double x = Math.Min(Math.Max(w.Sx, l), r); return w.Sigma + Hyp(x - w.Sx, w.Sy); }
        static double FMax(in Window w, double l, double r) => w.Sigma + Math.Max(Hyp(l - w.Sx, w.Sy), Hyp(r - w.Sx, w.Sy));
        static double FAt(in Window w, double x) => w.Sigma + Hyp(x - w.Sx, w.Sy);
        // the error of the parameter t of a ray of the window on its edge: angular error x R^2 / h, R the distance of the origin from
        // the point t, h its distance from the edge's line (rays nearly along the edge are badly conditioned: a large error, which only widens the clips)
        double EP(in Window w, double t, double cap)
        {
            double h = Math.Max(Math.Abs(w.Sy), cEps), rx = t - w.Sx, rr = rx * rx + w.Sy * w.Sy;
            return Math.Min(cap, w.E * rr / h + cEps);
        }

        // ------------------------------------------------------------------ distances and the queue

        // how dist[v] arose, for TracePath (PolyhedralGeodesicPath.cs): kind 1 a source, 2 an edge from the pseudo-source Org, 3 a window (edge E,
        // the side entered Side (-1: a terminal window on a boundary edge), the unfolded origin Org at (X, Y) in the frame of the edge)
        struct VRec { public double X, Y; public int E, Side, Org; public byte Kind; }      // 32 bytes
        VRec[] vrec;

        void SetDist(int v, double d, int rootSource, double xi, byte how = 0, int e = -1, int side = -1, int origin = -1, double ox = 0, double oy = 0)
        {
            if (d < dist[v] || (d == dist[v] && rootSource >= 0 && rootSource < root[v])) {
                bool lower = d < dist[v];
                dist[v] = d; root[v] = rootSource; xiV[v] = xi;
                ref VRec vr = ref vrec[v]; vr.Kind = how; vr.E = e; vr.Side = side; vr.Org = origin; vr.X = ox; vr.Y = oy;
                if (lower && kind[v] == 1 && d <= maxDistU) Push(~v, d);
            }
        }

        void Emit(int v)
        {
            double sl = dist[v]; emitted[v] = sl;
            foreach (int nb in Mesh.VtxVerticesItr(v)) SetDist(nb, sl + Len(v, nb), root[v], xiV[v] + cEps, 2, -1, -1, v);
            foreach (int t in Mesh.VtxTrianglesItr(v)) {
                Index3i tri = Mesh.GetTriangle(t);
                int a = -1, b = -1; for (int k = 0; k < 3; ++k) if (tri[k] != v) { if (a < 0) a = tri[k]; else b = tri[k]; }
                int e = Mesh.FindEdge(a, b); int f2 = Other(e, t);
                if (f2 < 0) continue;
                Index2i ev = Mesh.GetEdgeV(e); double L = elen[e];
                Place(ev.a, ev.b, v, L, out double x, out double y);
                var w = new Window { Side = SideOf(e, f2), Origin = v, B0 = 0, B1 = L, Sx = x, Sy = -y, Sigma = sl, Xi = xiV[v] + cEps, E = 8 * Eps };
                Add(ref w);
            }
        }

        // ---- the indexed heap

        int GetPos(int id) => id < 0 ? vpos[~id] : pchunks[id >> ChunkBits][id & (ChunkSize - 1)];
        void SetPos(int id, int p) { if (id < 0) vpos[~id] = p; else pchunks[id >> ChunkBits][id & (ChunkSize - 1)] = p; }

        void Push(int id, double key)
        {
            int p = GetPos(id);
            if (p >= 0) { if (key < hkey[p]) { hkey[p] = key; SiftUp(p); } return; }
            if (hn == hid.Length) { Array.Resize(ref hid, hn * 2); Array.Resize(ref hkey, hn * 2); }
            hid[hn] = id; hkey[hn] = key; SetPos(id, hn); SiftUp(hn); ++hn;
        }
        static bool Less(double ka, int ia, double kb, int ib) => ka < kb || (ka == kb && ia < ib);
        void Swap(int i, int j) { int ti = hid[i]; double tk = hkey[i]; hid[i] = hid[j]; hkey[i] = hkey[j]; hid[j] = ti; hkey[j] = tk; SetPos(hid[i], i); SetPos(hid[j], j); }
        void SiftUp(int i) { while (i > 0) { int q = (i - 1) >> 1; if (!Less(hkey[i], hid[i], hkey[q], hid[q])) break; Swap(i, q); i = q; } }
        int PopMin()
        {
            int id = hid[0]; SetPos(id, -1); --hn;
            if (hn > 0) {
                hid[0] = hid[hn]; hkey[0] = hkey[hn]; SetPos(hid[0], 0);
                int i = 0;
                while (true) {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < hn && Less(hkey[l], hid[l], hkey[m], hid[m])) m = l;
                    if (r < hn && Less(hkey[r], hid[r], hkey[m], hid[m])) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
            }
            return id;
        }

        // ------------------------------------------------------------------ adding windows: merging and pruning (A.7)

        int NewWindow(in Window w)
        {
            int ci = wcount >> ChunkBits;
            if (ci >= wchunks.Length) {
                Array.Resize(ref wchunks, ci + 1); Array.Resize(ref nchunks, ci + 1); Array.Resize(ref pchunks, ci + 1);
                wchunks[ci] = new Window[ChunkSize]; nchunks[ci] = new int[ChunkSize]; pchunks[ci] = new int[ChunkSize];
            }
            // the first chunk is sized for MaxWindows; a step can overshoot the limit by more than its estimate (each Add splits into pieces)
            if (ci == 0 && (wcount & (ChunkSize - 1)) >= wchunks[0].Length) {
                int nl = (int)Math.Min(ChunkSize, 2L * wchunks[0].Length);
                Array.Resize(ref wchunks[0], nl); Array.Resize(ref nchunks[0], nl); Array.Resize(ref pchunks[0], nl);
            }
            int id = wcount++;
            W(id) = w; pchunks[ci][id & (ChunkSize - 1)] = -1;
            Windows = wcount;
            return id;
        }
        int Next(int id) => nchunks[id >> ChunkBits][id & (ChunkSize - 1)];
        void SetNext(int id, int nx) => nchunks[id >> ChunkBits][id & (ChunkSize - 1)] = nx;

        double LowerKey(in Window w) => FMin(w, w.B0, w.B1);

        void Add(ref Window win)
        {
            int side = win.Side, e = side >> 1;
            if (!(win.B1 > win.B0) || LowerKey(win) > maxDistU) return;
            double L = elen[e];
            double xiM = win.Xi, margin = 4 * cEps;
            // segments of the new window that survive; the first version keeps one, cuts happen at the ends
            double l0 = Math.Max(0, win.B0), r0 = Math.Min(L, win.B1);
            if (!(r0 > l0)) return;
            var segs = segA; segs.Clear(); segs.Add((l0, r0));
            // the vertex test (A.7): a piece that costs more than reaching a vertex of the entered face and walking from it is dropped
            {
                Index2i ev = Mesh.GetEdgeV(e); int f = FaceOfSide(side); int c = Third(f, ev.a, ev.b);
                Place(ev.a, ev.b, c, L, out double cx, out double cy);
                TrimByVertex(in win, segs, 0, 0, ev.a, margin);
                TrimByVertex(in win, segs, L, 0, ev.b, margin);
                TrimByVertex(in win, segs, cx, cy, c, margin);
                segs.RemoveAll(t => !(t.r > t.l)); if (segs.Count == 0) return;
            }
            int prev = -1, cur = head[side];
            while (cur >= 0) {
                int nx = Next(cur);
                ref Window b = ref W(cur);
                if (!(b.B1 > b.B0)) { if (prev < 0) head[side] = nx; else SetNext(prev, nx); cur = nx; continue; }   // unlink the dead
                double segL = segs[0].l, segR = segs[segs.Count - 1].r;
                if (b.B1 < segL || b.B0 > segR) { prev = cur; cur = nx; continue; }
                // merging (A.7): one origin, nearly the same position and sigma, pending partner, touching intervals
                if (b.Origin == win.Origin && b.Side >= 0) {
                    double dp = Hyp(b.Sx - win.Sx, b.Sy - win.Sy), ds = Math.Abs(b.Sigma - win.Sigma);
                    double nxi = Math.Max(b.Xi, win.Xi + dp + ds);
                    if (dp <= delta && ds <= delta && nxi <= xiCap) {
                        var keep = segB; keep.Clear(); bool merged = false;
                        foreach (var sg in segs) {
                            if (sg.l <= b.B1 && sg.r >= b.B0) { b.B0 = Math.Min(b.B0, sg.l); b.B1 = Math.Max(b.B1, sg.r); merged = true; }
                            else keep.Add(sg);
                        }
                        if (merged) {
                            ++Merges; b.Xi = nxi; b.E = Math.Max(b.E, win.E) + dp / Math.Max(Math.Abs(b.Sy), cEps);
                            Push(cur, LowerKey(b));
                            RecordEnds(ref b, e, side);
                        }
                        segB = segs; segA = keep; segs = keep; if (segs.Count == 0) return;
                        prev = cur; cur = nx; continue;
                    }
                }
                // w certainly worse than b over a piece: drop it (the margins make a near tie stay)
                {
                    var rem = remM; rem.Clear();
                    foreach (var sg in segs) { double l = Math.Max(sg.l, b.B0), r = Math.Min(sg.r, b.B1); if (r > l) Dominated(in win, in b, l, r, rem, xiM + b.Xi + margin); }
                    if (rem.Count > 0) { var res = segB; Minus(segs, rem, res); segB = segs; segA = res; segs = res; if (segs.Count == 0) return; }
                }
                // b certainly worse than w over a piece: cut b's ends (a piece in the middle of b stays: splitting b makes more windows than it saves)
                {
                    var rb = remM; rb.Clear();
                    foreach (var sg in segs) { double l = Math.Max(sg.l, b.B0), r = Math.Min(sg.r, b.B1); if (r > l) Dominated(in b, in win, l, r, rb, xiM + b.Xi + margin); }
                    if (rb.Count > 0) { CutEnds(rb, b.B0, b.B1, out double lo, out double hi); b.B0 = lo; b.B1 = hi; }
                }
                prev = cur; cur = nx;
            }
            for (int si = 0; si < segs.Count; ++si) {
                var sg = segs[si];
                var c = win; c.B0 = sg.l; c.B1 = sg.r;
                int id = NewWindow(c);
                SetNext(id, head[side]); head[side] = id;
                RecordEnds(ref W(id), e, side);
                Push(id, LowerKey(W(id)));
            }
        }

        // a vertex with a known distance reaches every point of [l, r] more cheaply than w (by the margins: the errors of w and of the vertex)
        bool VertexBeats(in Window w, double vx0, double vy0, int v, double l, double r, double margin)
        {
            if (!(dist[v] < double.PositiveInfinity)) return false;
            double far = Math.Max(Hyp(vx0 - l, vy0), Hyp(vx0 - r, vy0));
            return FMin(w, l, r) - w.Xi > dist[v] + xiV[v] + far + margin;
        }

        void TrimByVertex(in Window w, List<(double l, double r)> segs, double vx0, double vy0, int v, double margin)
        {
            for (int k = 0; k < segs.Count; ++k) {
                var (l, r) = segs[k];
                if (VertexBeats(in w, vx0, vy0, v, l, r, margin)) { segs[k] = (r, r); continue; }
                double a = l, b = r;
                for (int it = 0; it < 8; ++it) { double m = 0.5 * a + 0.5 * b; if (VertexBeats(in w, vx0, vy0, v, l, m, margin)) a = m; else b = m; }
                if (a > l) l = a;
                a = l; b = r;
                for (int it = 0; it < 8; ++it) { double m = 0.5 * a + 0.5 * b; if (VertexBeats(in w, vx0, vy0, v, m, r, margin)) b = m; else a = m; }
                if (b < r) r = b;
                segs[k] = (l, r);
            }
        }

        // The part of [l, r] where a is certainly worse than b: FAt(a, x) > FAt(b, x) + m (A.7, "Exact trimming"). The difference of the two
        // functions is |x - sa| - |x - sb| + const, and the derivative cos(angle at a) - cos(angle at b) vanishes only where x, sa and sb are
        // collinear: at one point xs at most (the line through the two origins meets the line of the edge once). So the difference is monotone on
        // [l, xs] and on [xs, r], and on each the set where the test holds is a prefix or a suffix of the piece, which a bisection finds from
        // the end at which the test holds (so the reported set is always inside the true one, whatever the rounding of the last step).
        static void Dominated(in Window a, in Window b, double l, double r, List<(double l, double r)> rem, double m)
        {
            if (!(r > l)) return;
            // no point of [l, r] can pass unless FMax(a) > FMin(b) + m (Hyp is monotone in |x - sx| also in floating point)
            if (!(FMax(a, l, r) > FMin(b, l, r) + m)) return;
            if (FMin(a, l, r) > FMax(b, l, r) + m) { rem.Add((l, r)); return; }
            double xs = double.NaN;
            if (a.Sy != b.Sy) xs = a.Sx + a.Sy / (a.Sy - b.Sy) * (b.Sx - a.Sx);
            if (xs > l && xs < r) { DominatedMonotone(in a, in b, l, xs, rem, m); DominatedMonotone(in a, in b, xs, r, rem, m); }
            else DominatedMonotone(in a, in b, l, r, rem, m);
        }

        /// <summary> the part of [l, r] where the window of the origin (ax, ay) (ay &lt;= 0, the edge on the x axis) at distance sigmaA is worse than that of (bx, by) at sigmaB by more than m; for the tests </summary>
        internal static List<(double l, double r)> DominatedPieces(double ax, double ay, double sigmaA, double bx, double by, double sigmaB, double l, double r, double m)
        {
            var a = new Window { Sx = ax, Sy = ay, Sigma = sigmaA }; var b = new Window { Sx = bx, Sy = by, Sigma = sigmaB };
            var rem = new List<(double l, double r)>(); Dominated(in a, in b, l, r, rem, m); return rem;
        }

        /// <summary> the interval [b0, b1] of a window without the pieces at its ends that are in the ascending list rem (a piece in the middle stays); lo >= hi: nothing is left </summary>
        internal static void CutEnds(List<(double l, double r)> rem, double b0, double b1, out double lo, out double hi)
        {
            lo = b0; hi = b1;
            for (int k = 0; k < rem.Count && rem[k].l <= lo; ++k) lo = Math.Max(lo, rem[k].r);
            for (int k = rem.Count - 1; k >= 0 && rem[k].r >= hi; --k) hi = Math.Min(hi, rem[k].l);
        }

        static bool Worse(in Window a, in Window b, double x, double m) => FAt(a, x) > FAt(b, x) + m;

        // [l, r] is a piece on which FAt(a) - FAt(b) is monotone: the set where it exceeds m is a prefix or a suffix (or all, or nothing)
        static void DominatedMonotone(in Window a, in Window b, double l, double r, List<(double l, double r)> rem, double m)
        {
            bool wl = Worse(in a, in b, l, m), wr = Worse(in a, in b, r, m);
            if (!wl && !wr) return;
            if (wl && wr) { AddPiece(rem, l, r); return; }
            double good = wl ? l : r, bad = wl ? r : l;
            for (int it = 0; it < 48; ++it) { double mid = 0.5 * good + 0.5 * bad; if (Worse(in a, in b, mid, m)) good = mid; else bad = mid; }
            if (wl) { if (good > l) AddPiece(rem, l, good); } else if (good < r) AddPiece(rem, good, r);
        }

        // pieces arrive in ascending order; touching ones are joined
        static void AddPiece(List<(double l, double r)> rem, double l, double r)
        {
            int k = rem.Count - 1;
            if (k >= 0 && rem[k].r >= l) rem[k] = (rem[k].l, Math.Max(rem[k].r, r)); else rem.Add((l, r));
        }

        // dst = the parts of segs not covered by rem (empty pieces dropped); no allocation: the lists are members
        void Minus(List<(double l, double r)> segs, List<(double l, double r)> rem, List<(double l, double r)> dst)
        {
            dst.Clear();
            foreach (var sg in segs) {
                var ca = minusA; var cb = minusB; ca.Clear(); ca.Add(sg);
                foreach (var x in rem) {
                    cb.Clear();
                    foreach (var c in ca) {
                        if (x.r <= c.l || x.l >= c.r) { cb.Add(c); continue; }
                        if (x.l > c.l) cb.Add((c.l, x.l));
                        if (x.r < c.r) cb.Add((x.r, c.r));
                    }
                    var t = ca; ca = cb; cb = t;
                }
                foreach (var c in ca) if (c.r > c.l) dst.Add(c);
            }
        }

        // the distance a window gives the end vertices of its edge when its interval reaches them
        void RecordEnds(ref Window w, int e, int side)
        {
            Index2i ev = Mesh.GetEdgeV(e); double L = elen[e];
            int r = root[w.Origin];
            if (w.B0 <= EP(w, 0, L)) SetDist(ev.a, FAt(w, 0), r, w.Xi, 3, e, side, w.Origin, w.Sx, w.Sy);
            if (w.B1 >= L - EP(w, L, L)) SetDist(ev.b, FAt(w, L), r, w.Xi, 3, e, side, w.Origin, w.Sx, w.Sy);
        }

        // ------------------------------------------------------------------ propagation

        void Propagate(int id, int side)
        {
            Window w = W(id);
            int e = side >> 1, f = FaceOfSide(side);
            Index2i ev = Mesh.GetEdgeV(e); double L = elen[e];
            int c = Third(f, ev.a, ev.b);
            Place(ev.a, ev.b, c, L, out double cx, out double cy);
            double sx = w.Sx, sy = Math.Min(w.Sy, 0);
            double xc = sx + (cx - sx) * (-sy) / (cy - sy);
            double E = EP(w, xc, L);
            int r = root[w.Origin];
            if (xc >= w.B0 - E && xc <= w.B1 + E) SetDist(c, w.Sigma + Hyp(cx - sx, cy - sy), r, w.Xi, 3, e, side, w.Origin, w.Sx, w.Sy);
            // at a flat vertex the clips are closed (they carry the ray through it, A.6); elsewhere they are the plain clips
            double Es = flat[c] ? E : 0;
            if (flat[c] ? w.B0 <= xc + Es : w.B0 < xc) MakeChild(in w, f, ev.a, c, 0, 0, cx, cy, xc, true, Es);
            if (flat[c] ? w.B1 >= xc - Es : w.B1 > xc) MakeChild(in w, f, c, ev.b, cx, cy, L, 0, xc, false, Es);
        }

        // the child on the edge (pv, qv) of the face f, p to q as met in the frame of the parent edge (coordinates Px.. in that frame);
        // left: the clip is [B0, xc] and its end at xc maps to q = c exactly (snapping); right: the clip is [xc, B1], xc maps to p = c
        void MakeChild(in Window w, int f, int pv, int qv, double Px, double Py, double Qx, double Qy, double xc, bool left, double Es)
        {
            double L = 0; { int pe = w.Side < 0 ? (~w.Side) >> 1 : w.Side >> 1; L = elen[pe]; }
            int e2 = Mesh.FindEdge(pv, qv); int f2 = Other(e2, f);
            double len = Hyp(Qx - Px, Qy - Py);
            double sx = w.Sx, sy = Math.Min(w.Sy, 0);
            double t0, t1; bool snap0 = false, snap1 = false;
            if (left) { t0 = w.B0; if (w.B1 >= xc - Es) { t1 = xc; snap1 = true; } else t1 = w.B1; }
            else { t1 = w.B1; if (w.B0 <= xc + Es) { t0 = xc; snap0 = true; } else t0 = w.B0; }
            double ex = Qx - Px, ey = Qy - Py;
            double dx = sx - Px, dy = sy - Py;
            // the child's frame: canonical a at the origin
            Index2i ev2 = Mesh.GetEdgeV(e2);
            bool aIsP = ev2.a == pv;
            double Ax = aIsP ? Px : Qx, Ay = aIsP ? Py : Qy, Bx = aIsP ? Qx : Px, By = aIsP ? Qy : Py;
            double ux = (Bx - Ax) / len, uy = (By - Ay) / len;
            double rx = sx - Ax, ry = sy - Ay;
            double csx = ux * rx + uy * ry, csy = -Math.Abs(ux * ry - uy * rx);
            double tolS = w.Xi + 8 * cEps;
            double u0, u1, errU;
            if (Math.Abs(csy) <= tolS) {
                // the origin is on the line of the child's edge: only the ray along the edge, from the nearer end vertex q0
                double pr = (dx * ex + dy * ey) / len;
                bool atP = pr <= 0.5 * len;
                double xq = atP ? (left ? 0 : xc) : (left ? xc : L);
                double Eq = EP(w, xq, L);
                if (!(xq >= t0 - Eq && xq <= t1 + Eq)) return;
                double qxp = atP ? Px : Qx, qyp = atP ? Py : Qy;
                bool qIsA = atP == aIsP;
                var cw = new Window { Side = f2 < 0 ? 0 : SideOf(e2, f2), Origin = w.Origin, Sx = qIsA ? 0 : len, Sy = 0, Sigma = w.Sigma + Hyp(sx - qxp, sy - qyp), Xi = w.Xi + 8 * cEps, E = w.E + 16 * Eps };
                double ww = Math.Min(len, Math.Max(8 * cEps, Math.Min(tolS, 1e-9 * extent)));
                if (qIsA) { cw.B0 = len - ww; cw.B1 = len; } else { cw.B0 = 0; cw.B1 = ww; }
                Finish(ref cw, e2, f2);
                return;
            }
            // hull of the crossing parameters of the two clip ends with the line of (P, Q): raw values (not clamped to the edge: a ray
            // that misses the edge must give an empty child), each non-snapped end opened by the error
            double us0 = 0, us1 = 0, ds0 = 0, ds1 = 0; bool ok0 = true, ok1 = true;
            for (int k = 0; k < 2; ++k) {
                double t = k == 0 ? t0 : t1; bool snapped = k == 0 ? snap0 : snap1;
                double uk = 0, dk = 0; bool okk = true;
                if (snapped) uk = left ? len : 0;
                else {
                    double rrx = t - sx, rry = -sy;
                    double N = dx * rry - dy * rrx, D = ex * rry - ey * rrx;
                    double dscale = Math.Abs(ex * rry) + Math.Abs(ey * rrx);
                    dk = D;
                    // D uncertain: within the rounding of D (a ray parallel to the edge, the conditioning of the opening is the angular E x R^2 / h below)
                    if (Math.Abs(D) <= 64 * Eps * dscale || dscale == 0) okk = false;
                    else uk = N / D * len;
                }
                if (k == 0) { us0 = uk; ds0 = dk; ok0 = okk; } else { us1 = uk; ds1 = dk; ok1 = okk; }
            }
            // the angular error of the child: the parent's, the rounding of the hop (a position error of 4 cEps seen from the clip's nearest point)
            double Rp = Math.Min(Hyp(t0 - sx, sy), Hyp(t1 - sx, sy));
            double Ec = w.E + 16 * Eps + 4 * cEps / Math.Max(Rp, 8 * cEps);
            double lo, hi;
            if (!ok0 && !ok1) { lo = 0; hi = len; errU = Ec * len; }
            else {
                if (!ok0) { double rrx = t0 - sx, rry = -sy; double N = dx * rry - dy * rrx; us0 = (N * ds1 > 0) ? double.MaxValue : -double.MaxValue; }
                if (!ok1) { double rrx = t1 - sx, rry = -sy; double N = dx * rry - dy * rrx; us1 = (N * ds0 > 0) ? double.MaxValue : -double.MaxValue; }
                // the opening of an end: the angular error of the child x R^2 / h at that end (R: the origin's distance from it)
                double hc = Math.Max(Math.Abs(csy), cEps);
                double R0 = Hyp(Math.Min(Math.Max(us0, 0), len) - csx, csy), R1 = Hyp(Math.Min(Math.Max(us1, 0), len) - csx, csy);
                errU = Math.Min(len, Ec * Math.Max(R0 * R0, R1 * R1) / hc + 4 * cEps);
                double a0 = snap0 ? us0 : us0 - errU, a1 = snap0 ? us0 : us0 + errU, b0 = snap1 ? us1 : us1 - errU, b1 = snap1 ? us1 : us1 + errU;
                lo = Math.Min(Math.Min(a0, a1), Math.Min(b0, b1)); hi = Math.Max(Math.Max(a0, a1), Math.Max(b0, b1));
            }
            if (hi < 0 || lo > len) return;
            u0 = Math.Max(0, lo); u1 = Math.Min(len, hi);
            if (!(u1 > u0)) return;
            double o0, o1;
            if (aIsP) { o0 = u0; o1 = u1; } else { o0 = len - u1; o1 = len - u0; }
            var cw2 = new Window { Side = f2 < 0 ? 0 : SideOf(e2, f2), Origin = w.Origin, B0 = o0, B1 = o1, Sx = csx, Sy = csy, Sigma = w.Sigma, Xi = w.Xi + 2 * errU + cEps, E = Ec };
            Finish(ref cw2, e2, f2);
        }

        void Finish(ref Window cw, int e2, int f2)
        {
            if (f2 < 0) {                                   // a boundary edge: terminal, only the end vertices get a distance
                if (cw.B1 > cw.B0) RecordEnds(ref cw, e2, -1);
                return;
            }
            Add(ref cw);
        }
    }
}
