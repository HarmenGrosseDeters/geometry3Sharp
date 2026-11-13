// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Spike prototype (stage 1, commit 0) of the level-set side of the contouring topology certificate,
// docs/design/CONTOURING_TOPOLOGY.md section 7.4 (Theorem 3). Exact: ExactPredicates.Orient2d / Orient3d and
// coordinate comparisons on the mesh's and the grid's doubles only; no constructed point. Serial.
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary> the grid the certificate tests on: node coordinates, cells, their faces and the faces' edges </summary>
    internal abstract class CertificateGrid
    {
        /// <summary> coordinate of grid node index i along axis (the same double the contouring uses) </summary>
        public abstract double Coord(int axis, int i);
        /// <summary> the faces of a cell (min node, size in nodes) </summary>
        public abstract void CellFaces(Vector3i min, int size, List<GridFace> faces);
        /// <summary> the edges on the boundary of a face, in order around it </summary>
        public abstract void FaceEdges(GridFace f, List<GridSegment> edges);
        /// <summary> does the contour cross this grid edge (its pattern) </summary>
        public abstract bool Crossed(GridSegment e);
        /// <summary> the contour's sign at a node (inside), for the face rule </summary>
        public abstract bool NodeInside(Vector3i n);
        /// <summary> the size (in nodes) of the bins the triangles are sorted into (the coarse cell size) </summary>
        public abstract int BinSize { get; }

        public Vector3d Node(Vector3i n) { return new Vector3d(Coord(0, n.x), Coord(1, n.y), Coord(2, n.z)); }
    }

    /// <summary> an axis-aligned grid edge: from Start along +Axis, Length nodes </summary>
    internal struct GridSegment : IEquatable<GridSegment>
    {
        public Vector3i Start;
        public int Axis, Length;
        public GridSegment(Vector3i start, int axis, int length) { Start = start; Axis = axis; Length = length; }
        public Vector3i End { get { Vector3i e = Start; e[Axis] += Length; return e; } }
        public bool Equals(GridSegment o) { return Start == o.Start && Axis == o.Axis && Length == o.Length; }
        public override bool Equals(object o) { return o is GridSegment && Equals((GridSegment)o); }
        public override int GetHashCode() { unchecked { return (Start.GetHashCode() * 31 + Axis) * 31 + Length; } }
    }

    /// <summary> an axis-aligned grid face: normal Axis, Min corner, Size nodes along the two other axes </summary>
    internal struct GridFace : IEquatable<GridFace>
    {
        public Vector3i Min;
        public int Axis, Size;
        public GridFace(Vector3i min, int axis, int size) { Min = min; Axis = axis; Size = size; }
        public bool Equals(GridFace o) { return Min == o.Min && Axis == o.Axis && Size == o.Size; }
        public override bool Equals(object o) { return o is GridFace && Equals((GridFace)o); }
        public override int GetHashCode() { unchecked { return (Min.GetHashCode() * 31 + Axis) * 31 + Size; } }
        /// <summary> corner k (0..3, cyclic: (0,0), (1,0), (1,1), (0,1) in the axes Axis+1, Axis+2) </summary>
        public Vector3i Corner(int k)
        {
            Vector3i c = Min;
            if (k == 1 || k == 2) c[(Axis + 1) % 3] += Size;
            if (k == 2 || k == 3) c[(Axis + 2) % 3] += Size;
            return c;
        }
    }

    internal class LevelSetCertificate
    {
        public enum Reason { None = 0, GeneralPosition = 1, Edge = 2, Sign = 3, Face = 4, Pairing = 5, Cell = 6 }

        readonly DMesh3 mesh;
        readonly bool[] inL;
        readonly CertificateGrid grid;
        // triangles of L by bin (closed box overlap of their bounding boxes)
        readonly Dictionary<Vector3i, List<int>> bins = new Dictionary<Vector3i, List<int>>();

        public LevelSetCertificate(DMesh3 mesh, bool[] levelSetTriangles, CertificateGrid grid)
        {
            this.mesh = mesh; inL = levelSetTriangles; this.grid = grid;
            int B = grid.BinSize;
            foreach (int t in mesh.TriangleIndices()) {
                if (!inL[t]) continue;
                Vector3d a, b, c;
                tri(t, out a, out b, out c);
                Vector3i lo = Vector3i.Zero, hi = Vector3i.Zero;
                for (int k = 0; k < 3; ++k) {
                    double mn = Math.Min(a[k], Math.Min(b[k], c[k])), mx = Math.Max(a[k], Math.Max(b[k], c[k]));
                    lo[k] = first_bin(k, mn, B);
                    hi[k] = last_bin(k, mx, B);
                }
                for (int z = lo.z; z <= hi.z; ++z)
                    for (int y = lo.y; y <= hi.y; ++y)
                        for (int x = lo.x; x <= hi.x; ++x) {
                            Vector3i key = new Vector3i(x, y, z);
                            List<int> l;
                            if (!bins.TryGetValue(key, out l)) { l = new List<int>(); bins[key] = l; }
                            l.Add(t);
                        }
            }
        }

        /// <summary> the bins (in bin units) that triangles of L overlap </summary>
        public IEnumerable<Vector3i> Bins { get { return bins.Keys; } }

        // the smallest bin m with Coord(B (m + 1)) >= x, and the largest with Coord(B m) <= x (exact comparisons;
        // the estimate only starts the search)
        int first_bin(int axis, double x, int B)
        {
            double h = grid.Coord(axis, B) - grid.Coord(axis, 0);
            int m = (int)Math.Floor((x - grid.Coord(axis, 0)) / h);
            while (grid.Coord(axis, B * (m + 1)) < x) m++;
            while (grid.Coord(axis, B * m) >= x) m--;
            return m;
        }
        int last_bin(int axis, double x, int B)
        {
            double h = grid.Coord(axis, B) - grid.Coord(axis, 0);
            int m = (int)Math.Floor((x - grid.Coord(axis, 0)) / h);
            while (grid.Coord(axis, B * m) > x) m--;
            while (grid.Coord(axis, B * (m + 1)) <= x) m++;
            return m;
        }

        void tri(int t, out Vector3d a, out Vector3d b, out Vector3d c)
        {
            Index3i tv = mesh.GetTriangle(t);
            a = mesh.GetVertex(tv.a); b = mesh.GetVertex(tv.b); c = mesh.GetVertex(tv.c);
        }

        static readonly List<int> empty = new List<int>();
        // the triangles of L whose boxes overlap the closed box [lo, hi] (nodes)
        List<int> near(Vector3i lo, Vector3i hi)
        {
            int B = grid.BinSize;
            Vector3i blo = new Vector3i(fdiv(lo.x, B), fdiv(lo.y, B), fdiv(lo.z, B));
            Vector3i bhi = new Vector3i(fdiv(hi.x - 1, B), fdiv(hi.y - 1, B), fdiv(hi.z - 1, B));
            // (a box within one bin: that bin's list; on the boundary between bins either side has the triangles that meet it)
            if (blo == bhi || (bhi.x <= blo.x && bhi.y <= blo.y && bhi.z <= blo.z)) {
                List<int> l;
                return bins.TryGetValue(blo, out l) ? l : empty;
            }
            var set = new HashSet<int>();
            for (int z = blo.z; z <= Math.Max(blo.z, bhi.z); ++z)
                for (int y = blo.y; y <= Math.Max(blo.y, bhi.y); ++y)
                    for (int x = blo.x; x <= Math.Max(blo.x, bhi.x); ++x) {
                        List<int> l;
                        if (bins.TryGetValue(new Vector3i(x, y, z), out l)) foreach (int t in l) set.Add(t);
                    }
            return new List<int>(set);
        }
        static int fdiv(int a, int b) { return a >= 0 ? a / b : -((-a + b - 1) / b); }

        // ---- the predicates ----

        const int None = 0, Cross = 1, Degenerate = -1;

        // the grid segment A-B (axis-parallel) against the triangle (a, b, c): Cross if it crosses the triangle's
        // interior at a point of its own interior, Degenerate if it meets it otherwise (general position fails)
        static int segment_triangle(Vector3d A, Vector3d B, Vector3d a, Vector3d b, Vector3d c)
        {
            // (closed boxes apart: they do not meet; exact comparisons)
            if (!boxes_overlap(A, B, a, b, c)) return None;
            SegTri++;
            int sA = ExactPredicates.Orient3d(a, b, c, A), sB = ExactPredicates.Orient3d(a, b, c, B);
            if (sA == sB && sA != 0) return None;
            if (sA == 0 && sB == 0)
                return boxes_overlap(A, B, a, b, c) ? Degenerate : None;
            if (sA == 0) return point_in_triangle(A, a, b, c) ? Degenerate : None;
            if (sB == 0) return point_in_triangle(B, a, b, c) ? Degenerate : None;
            int o1 = ExactPredicates.Orient3d(A, B, a, b), o2 = ExactPredicates.Orient3d(A, B, b, c), o3 = ExactPredicates.Orient3d(A, B, c, a);
            if ((o1 > 0 || o2 > 0 || o3 > 0) && (o1 < 0 || o2 < 0 || o3 < 0)) return None;
            if (o1 == 0 || o2 == 0 || o3 == 0) return Degenerate;
            return Cross;
        }

        static bool boxes_overlap(Vector3d A, Vector3d B, Vector3d a, Vector3d b, Vector3d c)
        {
            for (int k = 0; k < 3; ++k) {
                double lo = Math.Min(A[k], B[k]), hi = Math.Max(A[k], B[k]);
                if (Math.Max(a[k], Math.Max(b[k], c[k])) < lo || Math.Min(a[k], Math.Min(b[k], c[k])) > hi) return false;
            }
            return true;
        }

        // p in the closed triangle (a, b, c), p in its plane (exact, in a projection where the triangle has area)
        static bool point_in_triangle(Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            for (int k = 0; k < 3; ++k) {
                Vector2d a2 = drop(a, k), b2 = drop(b, k), c2 = drop(c, k), p2 = drop(p, k);
                int s = ExactPredicates.Orient2d(a2, b2, c2);
                if (s == 0) continue;
                return ExactPredicates.Orient2d(a2, b2, p2) * s >= 0 && ExactPredicates.Orient2d(b2, c2, p2) * s >= 0
                    && ExactPredicates.Orient2d(c2, a2, p2) * s >= 0;
            }
            return true;    // (a triangle of no area: not expected, the input is checked; general position fails)
        }
        static Vector2d drop(Vector3d p, int k) { return k == 0 ? new Vector2d(p.y, p.z) : k == 1 ? new Vector2d(p.z, p.x) : new Vector2d(p.x, p.y); }

        // the mesh edge p-q against the face f (closed rectangle): Cross if it crosses the face's interior at a point of
        // its own interior, Degenerate if it meets the closed face otherwise
        int segment_face(Vector3d p, Vector3d q, GridFace f)
        {
            SegFace++;
            int a = f.Axis, u = (a + 1) % 3, v = (a + 2) % 3;
            double P = grid.Coord(a, f.Min[a]);
            double U0 = grid.Coord(u, f.Min[u]), U1 = grid.Coord(u, f.Min[u] + f.Size);
            double V0 = grid.Coord(v, f.Min[v]), V1 = grid.Coord(v, f.Min[v] + f.Size);
            int sp = p[a].CompareTo(P), sq = q[a].CompareTo(P);
            if (sp == sq && sp != 0) return None;
            if (Math.Max(p[u], q[u]) < U0 || Math.Min(p[u], q[u]) > U1 || Math.Max(p[v], q[v]) < V0 || Math.Min(p[v], q[v]) > V1) return None;
            if (sp == 0 && sq == 0) {
                bool overlap = Math.Max(p[u], q[u]) >= U0 && Math.Min(p[u], q[u]) <= U1 && Math.Max(p[v], q[v]) >= V0 && Math.Min(p[v], q[v]) <= V1;
                return overlap ? Degenerate : None;
            }
            if (sp == 0) return (p[u] >= U0 && p[u] <= U1 && p[v] >= V0 && p[v] <= V1) ? Degenerate : None;
            if (sq == 0) return (q[u] >= U0 && q[u] <= U1 && q[v] >= V0 && q[v] <= V1) ? Degenerate : None;
            if (sp > 0) { Vector3d t = p; p = q; q = t; }
            // sign(X_u - r) = -Orient2d((p_a, p_u), (q_a, q_u), (P, r)) for the crossing point X (p_a < P < q_a)
            Vector2d pu = new Vector2d(p[a], p[u]), qu = new Vector2d(q[a], q[u]), pv = new Vector2d(p[a], p[v]), qv = new Vector2d(q[a], q[v]);
            int su0 = -ExactPredicates.Orient2d(pu, qu, new Vector2d(P, U0));
            if (su0 < 0) return None;
            int su1 = -ExactPredicates.Orient2d(pu, qu, new Vector2d(P, U1));
            if (su1 > 0) return None;
            int sv0 = -ExactPredicates.Orient2d(pv, qv, new Vector2d(P, V0));
            if (sv0 < 0) return None;
            int sv1 = -ExactPredicates.Orient2d(pv, qv, new Vector2d(P, V1));
            if (sv1 > 0) return None;
            if (su0 == 0 || su1 == 0 || sv0 == 0 || sv1 == 0) return Degenerate;
            return Cross;
        }

        // ---- edges, faces, cells ----

        class EdgeResult { public bool Degenerate; public int Count; public int Triangle = -1; }
        readonly Dictionary<GridSegment, EdgeResult> edgeResults = new Dictionary<GridSegment, EdgeResult>();

        EdgeResult edge(GridSegment e, List<int> tris)
        {
            EdgeResult r;
            if (edgeResults.TryGetValue(e, out r)) return r;
            r = new EdgeResult();
            Vector3d A = grid.Node(e.Start), B = grid.Node(e.End);
            foreach (int t in tris) {
                Vector3d a, b, c;
                tri(t, out a, out b, out c);
                int s = segment_triangle(A, B, a, b, c);
                if (s == Degenerate) r.Degenerate = true;
                else if (s == Cross) { r.Count++; r.Triangle = t; }
            }
            edgeResults[e] = r;
            return r;
        }

        class FaceResult
        {
            public Reason Fail;
            public HashSet<int> T = new HashSet<int>(), M = new HashSet<int>();
        }
        readonly Dictionary<GridFace, FaceResult> faceResults = new Dictionary<GridFace, FaceResult>();
        readonly List<GridSegment> faceEdges = new List<GridSegment>();

        FaceResult face(GridFace f, List<int> tris)
        {
            FaceResult r;
            if (faceResults.TryGetValue(f, out r)) return r;
            r = new FaceResult();
            faceResults[f] = r;
            int a = f.Axis, u = (a + 1) % 3, v = (a + 2) % 3;
            double P = grid.Coord(a, f.Min[a]);
            double U0 = grid.Coord(u, f.Min[u]), U1 = grid.Coord(u, f.Min[u] + f.Size);
            double V0 = grid.Coord(v, f.Min[v]), V1 = grid.Coord(v, f.Min[v] + f.Size);
            var seenEdges = new HashSet<int>();
            foreach (int t in tris) {
                Index3i tv = mesh.GetTriangle(t);
                for (int j = 0; j < 3; ++j) {
                    Vector3d p = mesh.GetVertex(tv[j]);
                    if (p[a] == P && p[u] >= U0 && p[u] <= U1 && p[v] >= V0 && p[v] <= V1) { r.Fail = Reason.GeneralPosition; return r; }
                }
                Index3i te = mesh.GetTriEdges(t);
                for (int j = 0; j < 3; ++j) {
                    if (!seenEdges.Add(te[j])) continue;
                    Index2i ev = mesh.GetEdgeV(te[j]);
                    int s = segment_face(mesh.GetVertex(ev.a), mesh.GetVertex(ev.b), f);
                    if (s == Degenerate) { r.Fail = Reason.GeneralPosition; return r; }
                    if (s == Cross) {
                        r.M.Add(te[j]);
                        Index2i et = mesh.GetEdgeT(te[j]);
                        r.T.Add(et.a); r.T.Add(et.b);
                    }
                }
            }
            // the crossings of the face's edges (end points of the arcs)
            faceEdges.Clear();
            grid.FaceEdges(f, faceEdges);
            var ends = new List<KeyValuePair<int, int>>();     // (edge index in faceEdges, triangle)
            var fe = new List<GridSegment>(faceEdges);
            for (int i = 0; i < fe.Count; ++i) {
                EdgeResult er = edge(fe[i], tris);
                if (er.Degenerate) { r.Fail = Reason.GeneralPosition; return r; }
                if (er.Count > 1) { r.Fail = Reason.Edge; return r; }
                if ((er.Count == 1) != grid.Crossed(fe[i])) { r.Fail = Reason.Sign; return r; }
                if (er.Count == 1) { ends.Add(new KeyValuePair<int, int>(i, er.Triangle)); r.T.Add(er.Triangle); }
            }
            // parts: triangles joined across the mesh edges that cross the face; each an arc (|T| - |M| = 1) with two ends
            var tlist = new List<int>(r.T);
            var index = new Dictionary<int, int>();
            for (int i = 0; i < tlist.Count; ++i) index[tlist[i]] = i;
            int[] parent = new int[tlist.Count];
            for (int i = 0; i < parent.Length; ++i) parent[i] = i;
            foreach (int eid in r.M) {
                Index2i et = mesh.GetEdgeT(eid);
                union(parent, index[et.a], index[et.b]);
            }
            var nT = new Dictionary<int, int>(); var nM = new Dictionary<int, int>(); var partEnds = new Dictionary<int, List<int>>();
            foreach (int t in tlist) { int root = find(parent, index[t]); nT[root] = (nT.TryGetValue(root, out int x) ? x : 0) + 1; }
            foreach (int eid in r.M) { int root = find(parent, index[mesh.GetEdgeT(eid).a]); nM[root] = (nM.TryGetValue(root, out int x) ? x : 0) + 1; }
            foreach (var kv in ends) {
                int root = find(parent, index[kv.Value]);
                List<int> l;
                if (!partEnds.TryGetValue(root, out l)) { l = new List<int>(); partEnds[root] = l; }
                l.Add(kv.Key);
            }
            foreach (var kv in nT) {
                int m = nM.TryGetValue(kv.Key, out int x) ? x : 0;
                List<int> pe;
                int ne = partEnds.TryGetValue(kv.Key, out pe) ? pe.Count : 0;
                if (kv.Value - m != 1 || ne != 2) { r.Fail = Reason.Face; return r; }
            }
            // the pairing on a face with four crossings: the contour's face rule joins the two crossings next to each
            // inside corner (inside corners stay apart)
            if (ends.Count == 4) {
                int[] side = new int[4];
                for (int k = 0; k < 4; ++k) side[k] = side_of(f, fe[ends[k].Key]);
                foreach (var kv in partEnds) {
                    int s0 = side[ends.FindIndex(e => e.Key == kv.Value[0])], s1 = side[ends.FindIndex(e => e.Key == kv.Value[1])];
                    // sides s and s+1 (mod 4) share corner s+1
                    int corner = ((s0 + 1) % 4 == s1) ? s1 : ((s1 + 1) % 4 == s0) ? s0 : -1;
                    if (corner < 0 || !grid.NodeInside(f.Corner(corner))) { r.Fail = Reason.Pairing; return r; }
                }
            }
            return r;
        }

        // the side (0..3: corner k to k+1) of face f that the edge lies on
        static int side_of(GridFace f, GridSegment e)
        {
            for (int k = 0; k < 4; ++k) {
                Vector3i c0 = f.Corner(k), c1 = f.Corner((k + 1) % 4);
                if (on_segment(e.Start, c0, c1) && on_segment(e.End, c0, c1)) return k;
            }
            return -1;
        }
        static bool on_segment(Vector3i n, Vector3i a, Vector3i b)
        {
            for (int k = 0; k < 3; ++k)
                if (n[k] < Math.Min(a[k], b[k]) || n[k] > Math.Max(a[k], b[k])) return false;
            return true;
        }

        readonly List<GridFace> cellFaces = new List<GridFace>();
        public long NearTriangles;
        public static long SegTri, SegFace;
        public long Orient3dCalls { get { return SegTri; } }

        /// <summary>
        /// the test of one cell with its faces and their edges (Theorem 3): None if L meets it compatibly with the
        /// contour's pattern; meets = whether L meets the closed cell at all
        /// </summary>
        public Reason Cell(Vector3i min, int size, out bool meets)
        {
            meets = false;
            Vector3i max = min + size * Vector3i.One;
            List<int> tris = near(min, max);
            NearTriangles += tris.Count;
            cellFaces.Clear();
            grid.CellFaces(min, size, cellFaces);
            var faces = new List<GridFace>(cellFaces);
            Reason worst = Reason.None;
            var T = new HashSet<int>(); var M = new HashSet<int>(); var V = new HashSet<int>();
            Vector3d lo = grid.Node(min), hi = grid.Node(max);
            foreach (int t in tris) {
                Index3i tv = mesh.GetTriangle(t);
                for (int j = 0; j < 3; ++j) {
                    Vector3d p = mesh.GetVertex(tv[j]);
                    bool inClosed = true, inOpen = true;
                    for (int k = 0; k < 3; ++k) {
                        if (p[k] < lo[k] || p[k] > hi[k]) inClosed = false;
                        if (!(p[k] > lo[k] && p[k] < hi[k])) inOpen = false;
                    }
                    if (inClosed && !inOpen) worst = max_reason(worst, Reason.GeneralPosition);
                    if (inOpen) V.Add(tv[j]);
                }
            }
            foreach (GridFace f in faces) {
                FaceResult fr = face(f, tris);
                if (fr.Fail != Reason.None) worst = max_reason(worst, fr.Fail);
                foreach (int t in fr.T) T.Add(t);
                foreach (int e in fr.M) M.Add(e);
            }
            foreach (int vid in V) {
                foreach (int t in mesh.VtxTrianglesItr(vid)) T.Add(t);
                foreach (int e in mesh.VtxEdgesItr(vid)) M.Add(e);
            }
            meets = T.Count > 0;
            if (worst != Reason.None)
                return worst;
            // parts: triangles joined across mesh edges and vertices that reach into the cell; each a disk
            var tlist = new List<int>(T);
            var index = new Dictionary<int, int>();
            for (int i = 0; i < tlist.Count; ++i) index[tlist[i]] = i;
            int[] parent = new int[tlist.Count];
            for (int i = 0; i < parent.Length; ++i) parent[i] = i;
            foreach (int eid in M) { Index2i et = mesh.GetEdgeT(eid); union(parent, index[et.a], index[et.b]); }
            foreach (int vid in V) {
                int first = -1;
                foreach (int t in mesh.VtxTrianglesItr(vid)) { if (first < 0) first = t; else union(parent, index[first], index[t]); }
            }
            var chi = new Dictionary<int, int>();
            foreach (int t in tlist) add(chi, find(parent, index[t]), 1);
            foreach (int eid in M) add(chi, find(parent, index[mesh.GetEdgeT(eid).a]), -1);
            foreach (int vid in V) { foreach (int t in mesh.VtxTrianglesItr(vid)) { add(chi, find(parent, index[t]), 1); break; } }
            foreach (var kv in chi)
                if (kv.Value != 1) return Reason.Cell;
            return Reason.None;
        }

        static Reason max_reason(Reason a, Reason b) { return (a == Reason.None || (b != Reason.None && b < a)) ? b : a; }
        static void add(Dictionary<int, int> d, int k, int v) { d[k] = (d.TryGetValue(k, out int x) ? x : 0) + v; }
        static int find(int[] parent, int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        static void union(int[] parent, int a, int b) { parent[find(parent, a)] = find(parent, b); }
    }

    /// <summary>
    /// The level set of the exact winding number of a clean mesh (closed, manifold, no self-intersections): the
    /// components across which it steps from 0 (front) to 1 (back).
    /// </summary>
    internal static class LevelSetComponents
    {
        /// <summary> per triangle: on the level set; false if a component could not be decided (then null) </summary>
        public static bool[] Classify(DMesh3 mesh, out int components, out int levelSetComponents)
        {
            var tree = new DMeshAABBTree3(mesh, true);
            var cc = new MeshConnectedComponents(mesh);
            cc.FindConnectedT();
            components = cc.Components.Count;
            levelSetComponents = 0;
            bool[] inL = new bool[mesh.MaxTriangleID];
            AxisAlignedBox3d root = mesh.CachedBounds;
            foreach (var comp in cc.Components) {
                // the component's lexicographically largest vertex (any vertex would do)
                int v = -1;
                foreach (int t in comp.Indices) {
                    Index3i tv = mesh.GetTriangle(t);
                    for (int j = 0; j < 3; ++j)
                        if (v < 0 || lex_greater(mesh.GetVertex(tv[j]), mesh.GetVertex(v))) v = tv[j];
                }
                int front;
                if (!front_winding(mesh, tree, v, root, out front))
                    return null;
                if (front == 0) {
                    levelSetComponents++;
                    foreach (int t in comp.Indices) inL[t] = true;
                }
            }
            return inL;
        }

        static bool lex_greater(Vector3d a, Vector3d b)
        {
            if (a.x != b.x) return a.x > b.x;
            if (a.y != b.y) return a.y > b.y;
            return a.z > b.z;
        }

        // the winding number on the front side of the surface at vertex v: along a line from v to a far point q, the
        // exact winding number just after v (the signed crossings of the other triangles, VolumeSampleCells.WindingAlong)
        // and the jump across v's own triangles (the same perturbed line, ExactRayTriangle.LineCrossing); a line that
        // pierces the surface at v gives both sides (one always exists: the antipodal map has no fixed disk)
        static bool front_winding(DMesh3 mesh, DMeshAABBTree3 tree, int v, AxisAlignedBox3d root, out int front)
        {
            front = 0;
            Vector3d p = mesh.GetVertex(v);
            Vector3d c = root.Center;
            double ext = 4 * Math.Max(root.MaxDim, Math.Max(Math.Max(Math.Abs(p.x - c.x), Math.Abs(p.y - c.y)), Math.Abs(p.z - c.z)));
            var rnd = new Random(31337);
            for (int attempt = 0; attempt < 64; ++attempt) {
                int axis = rnd.Next(3);
                var dir = new Vector3d(rnd.Next(-1000, 1001), rnd.Next(-1000, 1001), rnd.Next(-1000, 1001)) / 1024.0;
                dir[axis] = rnd.Next(2) == 0 ? -1 : 1;
                Vector3d q = c + ext * dir;
                int jump = 0;
                bool coplanar = false;
                foreach (int t in mesh.VtxTrianglesItr(v)) {
                    Index3i tv = mesh.GetTriangle(t);
                    Vector3d a = mesh.GetVertex(tv.a), b = mesh.GetVertex(tv.b), cc = mesh.GetVertex(tv.c);
                    if (ExactPredicates.Orient3d(a, b, cc, q) == 0) { coplanar = true; break; }
                    jump += ExactRayTriangle.LineCrossing(p, q, a, b, cc);
                }
                if (coplanar || jump == 0) continue;
                if (jump != 1 && jump != -1) return false;
                int after = gs.VolumeSampleCells.WindingAlong(mesh, tree, p, q);
                int before = after + jump;
                front = Math.Min(after, before);
                return true;
            }
            return false;
        }
    }
}
