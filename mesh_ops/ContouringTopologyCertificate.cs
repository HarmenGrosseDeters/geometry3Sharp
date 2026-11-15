// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Why a grid cell is not certified by the contouring's topology certificate (see
    /// MeshWindingNumberSolidify.ComputeTopologyCertificate).
    /// </summary>
    public enum ContouringCertificateReason
    {
        /// <summary> certified </summary>
        None = 0,
        /// <summary> a vertex of the level set lies on the cell's boundary, an edge of it meets a grid edge or lies in a face's plane, or a triangle contains a grid node </summary>
        GeneralPosition = 1,
        /// <summary> an edge of the cell is crossed by the level set more than once, or not as the contour crosses it </summary>
        Edge = 2,
        /// <summary> on a face of the cell the level set is not a set of arcs between crossings (a closed curve, or an arc with other ends) </summary>
        Face = 3,
        /// <summary> on a face with four crossings the level set joins them otherwise than the contour's face rule </summary>
        Pairing = 4,
        /// <summary> in the cell the level set is not a set of disks (a closed part, a handle, an annulus) </summary>
        Cell = 5,
        /// <summary> the contour is not known to meet the cell as the proof needs (a vertex not strictly inside its cell, face or edge, a left-out polygon, a self-intersection, or a polygon the proof does not cover) </summary>
        MeshSide = 6,
    }

    /// <summary> the outcome of the contouring's topology certificate (see MeshWindingNumberSolidify.ComputeTopologyCertificate) </summary>
    public enum ContouringCertificateStatus
    {
        /// <summary> not asked for (ComputeTopologyCertificate off), or the contouring failed </summary>
        NotComputed = 0,
        /// <summary> every cell certified: the contour is ambient isotopic to the level set </summary>
        Certified = 1,
        /// <summary> some cells are not certified (UncertifiedCells) </summary>
        CellsUncertified = 2,
        /// <summary> the contour is certified, but the result was cleaned up or reduced after it, which the proof does not cover </summary>
        PostProcessing = 3,
        /// <summary> not for this method (the dense grid, marching cubes), several inputs, or an iso value outside (0, 1) </summary>
        NotSupported = 4,
        /// <summary> the input is not clean (closed, manifold, consistently oriented, no self-intersections): its level set is not known exactly </summary>
        InputNotClean = 5,
        /// <summary> a component of the input could not be decided (no line through its vertex pierced it; not expected) </summary>
        LevelSetUndecided = 6,
    }

    /// <summary> a grid cell the contouring's topology certificate does not certify </summary>
    public struct ContouringUncertifiedCell
    {
        /// <summary> the cell (in the coordinates of the input) </summary>
        public AxisAlignedBox3d Box;
        /// <summary> the first reason found </summary>
        public ContouringCertificateReason Reason;
    }

    /// <summary> the grid a certificate tests on: node coordinates, cells, their faces and the faces' edges </summary>
    internal abstract class CertificateGrid
    {
        /// <summary> coordinate of grid node index i along axis (the same double the contouring compares with) </summary>
        public abstract double Coord(int axis, int i);
        /// <summary> the faces of a cell (min node, size in nodes), appended to faces </summary>
        public abstract void CellFaces(Vector3i min, int size, List<GridFace> faces);
        /// <summary> the edges on the boundary of a face, appended to edges </summary>
        public abstract void FaceEdges(GridFace f, List<GridSegment> edges);
        /// <summary> does the contour cross this grid edge (its pattern) </summary>
        public abstract bool Crossed(GridSegment e);
        /// <summary> the contour's sign (inside) at a corner of a face with four crossings (its face rule keeps the inside corners apart) </summary>
        public abstract bool NodeInside(Vector3i n);
        /// <summary> cells lie inside the bins of this size (in nodes, from node 0): the coarse cell size </summary>
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
        public bool Equals(GridSegment o) { return Start.x == o.Start.x && Start.y == o.Start.y && Start.z == o.Start.z && Axis == o.Axis && Length == o.Length; }
        public override bool Equals(object o) { return o is GridSegment && Equals((GridSegment)o); }
        public override int GetHashCode() { unchecked { return ((Start.x * 73856093) ^ (Start.y * 19349663) ^ (Start.z * 83492791)) * 31 + Axis * 7 + Length * 1031; } }
    }

    /// <summary> an axis-aligned grid face: normal Axis, Min corner, Size nodes along the two other axes </summary>
    internal struct GridFace : IEquatable<GridFace>
    {
        public Vector3i Min;
        public int Axis, Size;
        public GridFace(Vector3i min, int axis, int size) { Min = min; Axis = axis; Size = size; }
        public bool Equals(GridFace o) { return Min.x == o.Min.x && Min.y == o.Min.y && Min.z == o.Min.z && Axis == o.Axis && Size == o.Size; }
        public override bool Equals(object o) { return o is GridFace && Equals((GridFace)o); }
        public override int GetHashCode() { unchecked { return ((Min.x * 73856093) ^ (Min.y * 19349663) ^ (Min.z * 83492791)) * 31 + Axis * 7 + Size * 1031; } }
        /// <summary> corner k (0..3, cyclic: (0,0), (1,0), (1,1), (0,1) along the axes Axis+1, Axis+2) </summary>
        public Vector3i Corner(int k)
        {
            Vector3i c = Min;
            if (k == 1 || k == 2) c[(Axis + 1) % 3] += Size;
            if (k == 2 || k == 3) c[(Axis + 2) % 3] += Size;
            return c;
        }
    }

    /// <summary>
    /// The level-set side of the contouring's topology certificate (docs/design/CONTOURING_TOPOLOGY.md, section 7.4,
    /// Theorem 3): for a closed manifold triangle mesh L without self-intersections and a grid with the contour's
    /// pattern (the edges it crosses, the face rule), whether L meets a cell, its faces and their edges compatibly
    /// (each edge at most once, a face in arcs between edge crossings, the cell in disks) and with the same pattern.
    ///
    /// Counting: the Euler characteristic of the part of L in a closed face or cell is |triangles| - |mesh edges| +
    /// |mesh vertices| meeting it; a part of a face is an arc iff |T| - |M| = 1 (no vertex lies on a face), a part of a
    /// cell is a disk iff |T| - |M| + |V| = 1. Under general position (no mesh vertex on the cell's boundary, no mesh
    /// edge meeting a grid edge or lying in a face's plane on the face, no triangle through a grid node; a violation
    /// refuses the cell). Exact: ExactPredicates.Orient2d / Orient3d and comparisons of the mesh's and the grid's
    /// doubles only, no constructed point, no error bound; serial.
    ///
    /// A cell is tested together with all its faces and their edges (Theorem 1b, section 7.7: on the certified cells
    /// the contour and L differ by a deformation that keeps every cell, face and edge in place).
    /// </summary>
    internal sealed class ContouringTopologyCertificate
    {
        readonly CertificateGrid grid;
        // L, as arrays (ids of the mesh): vertex positions, triangle vertices and edges, edge vertices and triangles,
        // and for each vertex its triangles and edges
        readonly Vector3d[] P;
        readonly int[] triV, triE, edgeV, edgeT;
        readonly int[] vtxTriStart, vtxTris, vtxEdgeStart, vtxEdges;
        // per triangle its bounding box: min x, y, z, max x, y, z
        readonly double[] triBox;
        // triangles of L by bin (closed box overlap of their bounding boxes with the bin): the sorted bin keys, and
        // binTris[binStart[i] .. binStart[i + 1]) the triangles of bin binKeys[i]
        readonly long[] binKeys;
        readonly int[] binStart, binTris;

        /// <summary>
        /// (tests, benchmark) exact segment-triangle and segment-face tests after the box rejection; diagnostic only: on the
        /// parallel path the faces and edges on chunk borders are counted twice, so the counts depend on the chunking
        /// </summary>
        internal long SegmentTriangleTests, SegmentFaceTests;

        /// <param name="mesh"> closed, manifold, no self-intersections, no triangle of zero area (CleanLevelSetMesh) </param>
        /// <param name="levelSet"> per triangle id: part of L (a component of the level set); null: all triangles </param>
        public ContouringTopologyCertificate(DMesh3 mesh, bool[] levelSet, CertificateGrid grid)
        {
            this.grid = grid;
            int NV = mesh.MaxVertexID, NT = mesh.MaxTriangleID, NE = mesh.MaxEdgeID;
            P = new Vector3d[NV];
            foreach (int v in mesh.VertexIndices()) P[v] = mesh.GetVertex(v);
            triV = new int[3 * NT]; triE = new int[3 * NT];
            edgeV = new int[2 * NE]; edgeT = new int[2 * NE];
            bool[] inL = new bool[NT];
            foreach (int t in mesh.TriangleIndices()) {
                if (levelSet != null && !levelSet[t]) continue;
                inL[t] = true;
                Index3i tv = mesh.GetTriangle(t), te = mesh.GetTriEdges(t);
                for (int j = 0; j < 3; ++j) { triV[3 * t + j] = tv[j]; triE[3 * t + j] = te[j]; }
            }
            foreach (int e in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(e), et = mesh.GetEdgeT(e);
                edgeV[2 * e] = ev.a; edgeV[2 * e + 1] = ev.b; edgeT[2 * e] = et.a; edgeT[2 * e + 1] = et.b;
            }
            // vertex stars of L's vertices (counting sort over L's triangles and edges; every triangle of a vertex of L is
            // in L, components are whole)
            vtxTriStart = new int[NV + 1]; vtxEdgeStart = new int[NV + 1];
            bool[] edgeInL = new bool[NE];
            for (int t = 0; t < NT; ++t) {
                if (!inL[t]) continue;
                for (int j = 0; j < 3; ++j) { vtxTriStart[triV[3 * t + j] + 1]++; edgeInL[triE[3 * t + j]] = true; }
            }
            for (int e = 0; e < NE; ++e)
                if (edgeInL[e]) { vtxEdgeStart[edgeV[2 * e] + 1]++; vtxEdgeStart[edgeV[2 * e + 1] + 1]++; }
            for (int v = 0; v < NV; ++v) { vtxTriStart[v + 1] += vtxTriStart[v]; vtxEdgeStart[v + 1] += vtxEdgeStart[v]; }
            vtxTris = new int[vtxTriStart[NV]]; vtxEdges = new int[vtxEdgeStart[NV]];
            int[] fillT = new int[NV], fillE = new int[NV];
            for (int t = 0; t < NT; ++t) {
                if (!inL[t]) continue;
                for (int j = 0; j < 3; ++j) { int v = triV[3 * t + j]; vtxTris[vtxTriStart[v] + fillT[v]++] = t; }
            }
            for (int e = 0; e < NE; ++e) {
                if (!edgeInL[e]) continue;
                int va = edgeV[2 * e], vb = edgeV[2 * e + 1];
                vtxEdges[vtxEdgeStart[va] + fillE[va]++] = e;
                vtxEdges[vtxEdgeStart[vb] + fillE[vb]++] = e;
            }
            triMark = new int[NT]; triLocal = new int[NT]; edgeMark = new int[NE]; vtxMark = new int[NV];

            int B = grid.BinSize;
            var pairKeys = new List<long>(4 * NT); var pairTris = new List<int>(4 * NT);
            triBox = new double[6 * NT];
            for (int t = 0; t < NT; ++t) {
                if (!inL[t]) continue;
                Vector3d a = P[triV[3 * t]], b = P[triV[3 * t + 1]], c = P[triV[3 * t + 2]];
                Vector3i lo = Vector3i.Zero, hi = Vector3i.Zero;
                for (int k = 0; k < 3; ++k) {
                    triBox[6 * t + k] = Math.Min(a[k], Math.Min(b[k], c[k]));
                    triBox[6 * t + 3 + k] = Math.Max(a[k], Math.Max(b[k], c[k]));
                    lo[k] = first_bin(k, triBox[6 * t + k], B);
                    hi[k] = last_bin(k, triBox[6 * t + 3 + k], B);
                }
                // (a triangle over many bins: only the bins its plane may meet, may_meet_box; large slanted faces of CAD
                // solids span many bins with their boxes)
                // (for a triangle over a few bins the test costs more than the empty cells it saves)
                bool one = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1) * (hi.z - lo.z + 1) <= 8;
                for (int z = lo.z; z <= hi.z; ++z)
                    for (int y = lo.y; y <= hi.y; ++y)
                        for (int x = lo.x; x <= hi.x; ++x) {
                            if (!one) {
                                Vector3d blo = new Vector3d(grid.Coord(0, B * x), grid.Coord(1, B * y), grid.Coord(2, B * z));
                                Vector3d bhi = new Vector3d(grid.Coord(0, B * (x + 1)), grid.Coord(1, B * (y + 1)), grid.Coord(2, B * (z + 1)));
                                if (!may_meet_box(a, b, c, blo, bhi)) continue;
                            }
                            pairKeys.Add(bin_key(x, y, z));
                            pairTris.Add(t);
                        }
            }
            long[] keys = pairKeys.ToArray();
            int[] items = pairTris.ToArray();
            Array.Sort(keys, items);
            int nb = 0;
            for (int i = 0; i < keys.Length; ++i) if (i == 0 || keys[i] != keys[i - 1]) nb++;
            binKeys = new long[nb]; binStart = new int[nb + 1]; binTris = items;
            nb = 0;
            for (int i = 0; i < keys.Length; ++i)
                if (i == 0 || keys[i] != keys[i - 1]) { binKeys[nb] = keys[i]; binStart[nb++] = i; }
            binStart[nb] = keys.Length;
            expectedCells = (int)Math.Min(1 << 24, (long)binKeys.Length * B * B);
        }
        int expectedCells;

        // the tables of faces and edges, on first use (the parallel path's workers have their own): about three new faces
        // and edges per cell; presized, the growth of these tables cost a third of the time
        void ensure_tables()
        {
            if (edgeRecords != null) return;
            int cap = (int)Math.Min(1 << 26, 4L * expectedCells + 64);
            edgeRecords = new Dictionary<GridSegment, EdgeRecord>(cap);
            faceRecords = new Dictionary<GridFace, FaceRecord>(cap);
            faceT = new List<int>(2 * cap); faceM = new List<int>(cap);
        }

        // a worker of the parallel path (Cells): the same L, grid and bins, read only; its own marks and tables
        ContouringTopologyCertificate(ContouringTopologyCertificate shared, int expectedCells)
        {
            grid = shared.grid; P = shared.P;
            triV = shared.triV; triE = shared.triE; edgeV = shared.edgeV; edgeT = shared.edgeT;
            vtxTriStart = shared.vtxTriStart; vtxTris = shared.vtxTris; vtxEdgeStart = shared.vtxEdgeStart; vtxEdges = shared.vtxEdges;
            triBox = shared.triBox; binKeys = shared.binKeys; binStart = shared.binStart; binTris = shared.binTris;
            triMark = new int[shared.triMark.Length]; triLocal = new int[shared.triLocal.Length];
            edgeMark = new int[shared.edgeMark.Length]; vtxMark = new int[shared.vtxMark.Length];
            this.expectedCells = expectedCells;
        }

        /// <summary>
        /// The test of each cell (min node, size; contourCell as in Cell), in order. Parallel: the cells in contiguous
        /// chunks, each tested by a worker with its own tables (the faces and edges on the borders between chunks are
        /// tested twice). The same reasons as the serial loop, bit for bit: a face's or an edge's record, and so a cell's
        /// reason, is a function of its geometry alone (the triangles of L that meet it, from any superset; the first
        /// reason by a fixed priority), not of which cell tested it first. The grid's methods must be thread-safe for the
        /// parallel path: read only, or (DualContouring's node values, filled on first use in a concurrent table) a
        /// field evaluator that is itself thread-safe, as DualContouring with ParallelCompute already requires of
        /// NodeValueF (WindingContouring's winding-number trees are read only after their construction).
        /// </summary>
        /// <param name="minChunk"> the fewest cells per chunk (tests: small, to get many chunks from a small input) </param>
        public ContouringCertificateReason[] Cells(IList<KeyValuePair<Vector3i, int>> cells, IList<bool> contourCell, bool parallel, int minChunk = 2048)
        {
            var result = new ContouringCertificateReason[cells.Count];
            int nChunks = parallel ? Math.Min(4 * Environment.ProcessorCount, cells.Count / Math.Max(1, minChunk)) : 1;
            if (nChunks <= 1) {
                for (int i = 0; i < cells.Count; ++i) {
                    bool meets;
                    result[i] = Cell(cells[i].Key, cells[i].Value, contourCell[i], out meets);
                }
                return result;
            }
            var workers = new ContouringTopologyCertificate[nChunks];
            int n = cells.Count;
            gParallel.ForEach(Interval1i.Range(nChunks), (k) => {
                int i0 = (int)((long)n * k / nChunks), i1 = (int)((long)n * (k + 1) / nChunks);
                var w = new ContouringTopologyCertificate(this, i1 - i0);
                for (int i = i0; i < i1; ++i) {
                    bool meets;
                    result[i] = w.Cell(cells[i].Key, cells[i].Value, contourCell[i], out meets);
                }
                workers[k] = w;
            });
            // (integer counters, summed in chunk order)
            foreach (var w in workers) { SegmentTriangleTests += w.SegmentTriangleTests; SegmentFaceTests += w.SegmentFaceTests; }
            return result;
        }

        /// <summary> the bins (in bin units, bin m spans nodes BinSize m .. BinSize (m + 1)) that triangles of L overlap </summary>
        public IEnumerable<Vector3i> Bins
        {
            get {
                foreach (long k in binKeys)
                    yield return new Vector3i((int)(k & KeyMask) - KeyBias, (int)((k >> 21) & KeyMask) - KeyBias, (int)((k >> 42) & KeyMask) - KeyBias);
            }
        }
        // bin coordinates in [-2^20, 2^20)
        const int KeyBias = 1 << 20;
        const long KeyMask = (1L << 21) - 1;
        static long bin_key(int x, int y, int z)
        {
            if (x < -KeyBias || x >= KeyBias || y < -KeyBias || y >= KeyBias || z < -KeyBias || z >= KeyBias)
                throw new ArgumentException("ContouringTopologyCertificate: grid too large");
            return (long)(x + KeyBias) | ((long)(y + KeyBias) << 21) | ((long)(z + KeyBias) << 42);
        }

        // the smallest bin m with Coord(B (m + 1)) >= x, and the largest with Coord(B m) <= x (exact comparisons; the
        // estimate only starts the search; the coordinates increase with the index)
        int first_bin(int axis, double x, int B)
        {
            int m = estimate_bin(axis, x, B);
            while (grid.Coord(axis, B * (m + 1)) < x) m++;
            while (grid.Coord(axis, B * m) >= x) m--;
            return m;
        }
        int last_bin(int axis, double x, int B)
        {
            int m = estimate_bin(axis, x, B);
            while (grid.Coord(axis, B * m) > x) m--;
            while (grid.Coord(axis, B * (m + 1)) <= x) m++;
            return m;
        }
        int estimate_bin(int axis, double x, int B)
        {
            double o = grid.Coord(axis, 0), h = grid.Coord(axis, B) - o;
            double m = Math.Floor((x - o) / h);
            return (m > -1e9 && m < 1e9) ? (int)m : 0;
        }

        static int fdiv(int a, int b) { return a >= 0 ? a / b : -((-a + b - 1) / b); }

        // ---- the predicates ----

        const int None = 0, Cross = 1, Degenerate = -1;

        // the axis-parallel grid segment A-B against the triangle (a, b, c): Cross if it crosses the triangle's
        // interior at a point of its own interior, Degenerate if it meets the closed triangle otherwise (general
        // position fails: a node on the triangle, the segment through a mesh edge or vertex, or in the triangle's plane)
        internal int segment_triangle(Vector3d A, Vector3d B, int axis, Vector3d a, Vector3d b, Vector3d c)
        {
            if (!boxes_overlap(A, B, a, b, c)) return None;
            SegmentTriangleTests++;
            // A and B differ only along axis: the segment's line meets the triangle iff A's projection along the axis
            // lies in the projected triangle (Orient3d(A, B, x, y) is (B - A)_axis times Orient2d of the projections of
            // A, x, y). Strictly inside: the line crosses the triangle's interior, and the two Orient3d decide where.
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            Vector2d A2 = new Vector2d(A[u], A[v]), a2 = new Vector2d(a[u], a[v]), b2 = new Vector2d(b[u], b[v]), c2 = new Vector2d(c[u], c[v]);
            int p1 = ExactPredicates.Orient2d(a2, b2, A2), p2 = ExactPredicates.Orient2d(b2, c2, A2), p3 = ExactPredicates.Orient2d(c2, a2, A2);
            if ((p1 > 0 || p2 > 0 || p3 > 0) && (p1 < 0 || p2 < 0 || p3 < 0)) return None;
            if (p1 != 0 && p2 != 0 && p3 != 0) {
                int s1 = ExactPredicates.Orient3d(a, b, c, A), s2 = ExactPredicates.Orient3d(a, b, c, B);
                if (s1 == s2 && s1 != 0) return None;
                if (s1 == 0 || s2 == 0) return Degenerate;     // a node on the triangle's interior
                return Cross;
            }
            // the line through an edge or a vertex of the triangle, or parallel to its plane: the general test
            return segment_triangle_general(A, B, a, b, c);
        }

        // the same for any segment A-B (the closed boxes overlap)
        internal static int segment_triangle_general(Vector3d A, Vector3d B, Vector3d a, Vector3d b, Vector3d c)
        {
            int sA = ExactPredicates.Orient3d(a, b, c, A), sB = ExactPredicates.Orient3d(a, b, c, B);
            if (sA == sB && sA != 0) return None;
            if (sA == 0 && sB == 0) return Degenerate;      // (in the plane, the closed boxes overlap: refused, conservatively)
            if (sA == 0) return point_in_triangle(A, a, b, c) ? Degenerate : None;
            if (sB == 0) return point_in_triangle(B, a, b, c) ? Degenerate : None;
            // the segment crosses the plane in one point of its interior: inside the triangle iff the line passes all
            // three edges on the same side
            int o1 = ExactPredicates.Orient3d(A, B, a, b), o2 = ExactPredicates.Orient3d(A, B, b, c), o3 = ExactPredicates.Orient3d(A, B, c, a);
            if ((o1 > 0 || o2 > 0 || o3 > 0) && (o1 < 0 || o2 < 0 || o3 < 0)) return None;
            if (o1 == 0 || o2 == 0 || o3 == 0) return Degenerate;
            return Cross;
        }

        // the closed bounding boxes of A-B and of (a, b, c) overlap
        static bool boxes_overlap(Vector3d A, Vector3d B, Vector3d a, Vector3d b, Vector3d c)
        {
            for (int k = 0; k < 3; ++k) {
                double lo = Math.Min(A[k], B[k]), hi = Math.Max(A[k], B[k]);
                if (Math.Max(a[k], Math.Max(b[k], c[k])) < lo || Math.Min(a[k], Math.Min(b[k], c[k])) > hi) return false;
            }
            return true;
        }

        // False only if the triangle (a, b, c) certainly misses the closed box [lo, hi]: the closed bounding boxes are
        // apart (exact comparisons), or the box lies strictly on one side of the triangle's plane along n, the normal
        // as computed in double (any vector separates if the projections are apart; n's own rounding does not matter).
        // The projections n . x = t1 + t2 + t3 (t_k = n_k x_k) are summed in double, left to right, and compared after
        // adding / subtracting err; M is the largest single |t_k| of the points projected, so 3M bounds every sum
        // (Higham, Accuracy and Stability of Numerical Algorithms, 2002, 3.1; u = 2^-53, eta = 2^-1074):
        //   - each projection: the three products u (|t1| + |t2| + |t3|) + 3 eta, the two additions u |s2| + u |s3|,
        //     together at most 8 u M + 3 eta;
        //   - a comparison A + err < B - err (A a box extreme, B a triangle extreme, or the other way): the rounding of
        //     the two operands, u (|A| + |B|) <= 6 u M (plus O(u^2 M));
        //   so the exact projections are apart if the computed ones are by more than 22 u M + 6 eta + O(u^2 M), which
        //   2 err = 24 u M + 8 eta (err itself rounded: at least 24 u M (1 - 2u) + 8 eta) exceeds.
        // A projection that overflows (products finite, their sum not) keeps the triangle, as does a normal that does. (No
        // touching triangle was found that this guard saves, in tests and in the review's search at the overflow threshold;
        // it keeps the soundness independent of how the rounding of the computed sums falls there.)
        internal static bool may_meet_box(Vector3d a, Vector3d b, Vector3d c, Vector3d lo, Vector3d hi)
        {
            for (int k = 0; k < 3; ++k)
                if (Math.Max(a[k], Math.Max(b[k], c[k])) < lo[k] || Math.Min(a[k], Math.Min(b[k], c[k])) > hi[k]) return false;
            Vector3d n = (b - a).Cross(c - a);
            if (!(n.x == n.x && n.y == n.y && n.z == n.z) || double.IsInfinity(n.x) || double.IsInfinity(n.y) || double.IsInfinity(n.z))
                return true;
            double ta = n.x * a.x + n.y * a.y + n.z * a.z, tb = n.x * b.x + n.y * b.y + n.z * b.z, tc = n.x * c.x + n.y * c.y + n.z * c.z;
            double tmin = Math.Min(ta, Math.Min(tb, tc)), tmax = Math.Max(ta, Math.Max(tb, tc));
            // the box corners with the smallest and the largest projection
            double bmin = 0, bmax = 0, M = 0;
            for (int k = 0; k < 3; ++k) {
                double l = n[k] * lo[k], h = n[k] * hi[k];
                bmin += Math.Min(l, h); bmax += Math.Max(l, h);
                M = Math.Max(M, Math.Max(Math.Abs(l), Math.Abs(h)));
                M = Math.Max(M, Math.Max(Math.Abs(n[k] * a[k]), Math.Max(Math.Abs(n[k] * b[k]), Math.Abs(n[k] * c[k]))));
            }
            // (an infinite or NaN projection: no separation is known)
            if (!(Math.Abs(tmin) <= double.MaxValue && Math.Abs(tmax) <= double.MaxValue && Math.Abs(bmin) <= double.MaxValue && Math.Abs(bmax) <= double.MaxValue))
                return true;
            double err = 4 * Epsilon * 3 * M + 4 * double.Epsilon;
            if (!(err <= double.MaxValue)) return true;
            return !(bmax + err < tmin - err || bmin - err > tmax + err);
        }
        const double Epsilon = 1.1102230246251565e-16;     // u = 2^-53

        // p in the closed triangle (a, b, c), p in its plane: in a projection where the triangle has area
        static bool point_in_triangle(Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            for (int k = 0; k < 3; ++k) {
                Vector2d a2 = drop(a, k), b2 = drop(b, k), c2 = drop(c, k), p2 = drop(p, k);
                int s = ExactPredicates.Orient2d(a2, b2, c2);
                if (s == 0) continue;
                return ExactPredicates.Orient2d(a2, b2, p2) * s >= 0 && ExactPredicates.Orient2d(b2, c2, p2) * s >= 0
                    && ExactPredicates.Orient2d(c2, a2, p2) * s >= 0;
            }
            return true;    // (no area: excluded by the input check; refused)
        }
        static Vector2d drop(Vector3d p, int k) { return k == 0 ? new Vector2d(p.y, p.z) : k == 1 ? new Vector2d(p.z, p.x) : new Vector2d(p.x, p.y); }

        // the mesh edge p-q against the closed face: Cross if it crosses the face's interior at a point of its own
        // interior, Degenerate if it meets the closed face otherwise (an end on the face, the crossing on the face's
        // boundary, the edge in the face's plane)
        internal int segment_face(Vector3d p, Vector3d q, int a, double P, double U0, double U1, double V0, double V1)
        {
            int u = (a + 1) % 3, v = (a + 2) % 3;
            int sp = p[a].CompareTo(P), sq = q[a].CompareTo(P);
            if (sp == sq && sp != 0) return None;
            if (Math.Max(p[u], q[u]) < U0 || Math.Min(p[u], q[u]) > U1 || Math.Max(p[v], q[v]) < V0 || Math.Min(p[v], q[v]) > V1) return None;
            if (sp == 0 && sq == 0) return Degenerate;      // (in the plane, the closed boxes overlap: refused, conservatively)
            if (sp == 0) return (p[u] >= U0 && p[u] <= U1 && p[v] >= V0 && p[v] <= V1) ? Degenerate : None;
            if (sq == 0) return (q[u] >= U0 && q[u] <= U1 && q[v] >= V0 && q[v] <= V1) ? Degenerate : None;
            SegmentFaceTests++;
            if (sp > 0) { Vector3d t = p; p = q; q = t; }
            // the crossing point X (p_a < P < q_a): sign(X_u - r) = -Orient2d((p_a, p_u), (q_a, q_u), (P, r))
            Vector2d pu = new Vector2d(p[a], p[u]), qu = new Vector2d(q[a], q[u]);
            int su0 = -ExactPredicates.Orient2d(pu, qu, new Vector2d(P, U0));
            if (su0 < 0) return None;
            int su1 = -ExactPredicates.Orient2d(pu, qu, new Vector2d(P, U1));
            if (su1 > 0) return None;
            Vector2d pv = new Vector2d(p[a], p[v]), qv = new Vector2d(q[a], q[v]);
            int sv0 = -ExactPredicates.Orient2d(pv, qv, new Vector2d(P, V0));
            if (sv0 < 0) return None;
            int sv1 = -ExactPredicates.Orient2d(pv, qv, new Vector2d(P, V1));
            if (sv1 > 0) return None;
            if (su0 == 0 || su1 == 0 || sv0 == 0 || sv1 == 0) return Degenerate;
            return Cross;
        }

        // ---- edges ----

        struct EdgeRecord { public bool Degenerate, Crossed; public int Count, Triangle; }
        Dictionary<GridSegment, EdgeRecord> edgeRecords;

        EdgeRecord edge(GridSegment e, List<int> tris)
        {
            EdgeRecord r;
            if (edgeRecords.TryGetValue(e, out r)) return r;
            r.Triangle = -1;
            Vector3d A = grid.Node(e.Start), B = grid.Node(e.End);
            int ax = e.Axis, u = (ax + 1) % 3, v = (ax + 2) % 3;
            for (int i = 0; i < tris.Count; ++i) {
                int t = tris[i];
                // (closed boxes apart: no meeting; A and B differ only along the axis)
                int o = 6 * t;
                if (triBox[o + u] > A[u] || triBox[o + 3 + u] < A[u] || triBox[o + v] > A[v] || triBox[o + 3 + v] < A[v]
                    || triBox[o + ax] > B[ax] || triBox[o + 3 + ax] < A[ax]) continue;
                int s = segment_triangle(A, B, ax, P[triV[3 * t]], P[triV[3 * t + 1]], P[triV[3 * t + 2]]);
                if (s == Degenerate) r.Degenerate = true;
                else if (s == Cross) { r.Count++; r.Triangle = t; }
            }
            r.Crossed = grid.Crossed(e);
            edgeRecords[e] = r;
            return r;
        }

        // ---- faces ----

        struct FaceRecord { public ContouringCertificateReason Fail; public int TStart, TCount, MStart, MCount; }
        Dictionary<GridFace, FaceRecord> faceRecords;
        // T(f) and M(f) of all faces, by range
        List<int> faceT, faceM;
        readonly List<GridSegment> faceEdges = new List<GridSegment>();
        readonly List<int> endEdge = new List<int>(), endTri = new List<int>();

        // marks with a stamp per pass (no clearing): a triangle / edge / vertex is in the current set iff its mark is the stamp
        readonly int[] triMark, triLocal, edgeMark, vtxMark;
        int stamp;
        int[] parent = new int[64];
        int[] count = new int[64];

        FaceRecord face(GridFace f, List<int> tris)
        {
            FaceRecord r;
            if (faceRecords.TryGetValue(f, out r)) return r;
            r.Fail = face_test(f, tris, out r.TStart, out r.TCount, out r.MStart, out r.MCount);
            faceRecords[f] = r;
            return r;
        }

        ContouringCertificateReason face_test(GridFace f, List<int> tris, out int tStart, out int tCount, out int mStart, out int mCount)
        {
            tStart = faceT.Count; mStart = faceM.Count; tCount = mCount = 0;
            int a = f.Axis, u = (a + 1) % 3, v = (a + 2) % 3;
            double Pl = grid.Coord(a, f.Min[a]);
            double U0 = grid.Coord(u, f.Min[u]), U1 = grid.Coord(u, f.Min[u] + f.Size);
            double V0 = grid.Coord(v, f.Min[v]), V1 = grid.Coord(v, f.Min[v] + f.Size);
            int st = ++stamp;
            for (int i = 0; i < tris.Count; ++i) {
                int t = tris[i];
                // (a triangle whose closed box misses the closed face has no vertex on it and no edge meeting it)
                int o = 6 * t;
                if (triBox[o + a] > Pl || triBox[o + 3 + a] < Pl || triBox[o + u] > U1 || triBox[o + 3 + u] < U0
                    || triBox[o + v] > V1 || triBox[o + 3 + v] < V0) continue;
                // (a mesh vertex on the closed face is refused through its edges: segment_face, an end on the face)
                for (int j = 0; j < 3; ++j) {
                    int e = triE[3 * t + j];
                    if (edgeMark[e] == st) continue;
                    edgeMark[e] = st;
                    int s = segment_face(P[edgeV[2 * e]], P[edgeV[2 * e + 1]], a, Pl, U0, U1, V0, V1);
                    if (s == Degenerate) return ContouringCertificateReason.GeneralPosition;
                    if (s == Cross) {
                        faceM.Add(e);
                        add_triangle(edgeT[2 * e], st);
                        add_triangle(edgeT[2 * e + 1], st);
                    }
                }
            }
            // the crossings of the face's edges: the ends of the arcs
            faceEdges.Clear();
            grid.FaceEdges(f, faceEdges);
            endEdge.Clear(); endTri.Clear();
            for (int i = 0; i < faceEdges.Count; ++i) {
                EdgeRecord er = edge(faceEdges[i], tris);
                if (er.Degenerate) return ContouringCertificateReason.GeneralPosition;
                if (er.Count > 1 || (er.Count == 1) != er.Crossed) return ContouringCertificateReason.Edge;
                if (er.Count == 1) { endEdge.Add(i); endTri.Add(er.Triangle); add_triangle(er.Triangle, st); }
            }
            tCount = faceT.Count - tStart; mCount = faceM.Count - mStart;
            // parts: triangles joined across the mesh edges that cross the face; each an arc (|T| - |M| = 1) with two ends
            ensure(tCount);
            for (int i = 0; i < tCount; ++i) { parent[i] = i; count[i] = 0; triLocal[faceT[tStart + i]] = i; }
            for (int i = 0; i < mCount; ++i) {
                int e = faceM[mStart + i];
                union(triLocal[edgeT[2 * e]], triLocal[edgeT[2 * e + 1]]);
            }
            // count[root]: |T| - |M| per part, times 4, plus its ends (so a part is right iff it is 4 + 2 = 6)
            for (int i = 0; i < tCount; ++i) count[find(i)] += 4;
            for (int i = 0; i < mCount; ++i) count[find(triLocal[edgeT[2 * faceM[mStart + i]]])] -= 4;
            for (int i = 0; i < endTri.Count; ++i) count[find(triLocal[endTri[i]])] += 1;
            for (int i = 0; i < tCount; ++i)
                if (find(i) == i && count[i] != 6) return ContouringCertificateReason.Face;
            // the pairing on a face with four crossings: the contour's face rule joins the two crossings next to each
            // inside corner (the inside corners stay apart)
            if (endTri.Count == 4) {
                for (int i = 0; i < 4; ++i) {
                    for (int j = i + 1; j < 4; ++j) {
                        if (find(triLocal[endTri[i]]) != find(triLocal[endTri[j]])) continue;
                        int s0 = side_of(f, faceEdges[endEdge[i]]), s1 = side_of(f, faceEdges[endEdge[j]]);
                        // side k runs from corner k to corner k + 1
                        int corner = ((s0 + 1) % 4 == s1) ? s1 : ((s1 + 1) % 4 == s0) ? s0 : -1;
                        if (corner < 0 || !grid.NodeInside(f.Corner(corner))) return ContouringCertificateReason.Pairing;
                    }
                }
            }
            return ContouringCertificateReason.None;
        }

        void add_triangle(int t, int st)
        {
            if (triMark[t] == st) return;
            triMark[t] = st;
            faceT.Add(t);
        }

        // the side (0..3: corner k to k+1) of face f the edge lies on
        static int side_of(GridFace f, GridSegment e)
        {
            Vector3i end = e.End;
            for (int k = 0; k < 4; ++k) {
                Vector3i c0 = f.Corner(k), c1 = f.Corner((k + 1) % 4);
                if (on_segment(e.Start, c0, c1) && on_segment(end, c0, c1)) return k;
            }
            return -1;
        }
        static bool on_segment(Vector3i n, Vector3i a, Vector3i b)
        {
            for (int k = 0; k < 3; ++k)
                if (n[k] < Math.Min(a[k], b[k]) || n[k] > Math.Max(a[k], b[k])) return false;
            return true;
        }

        void ensure(int n)
        {
            if (parent.Length < n) { parent = new int[2 * n]; count = new int[2 * n]; }
        }
        int find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        void union(int a, int b) { a = find(a); b = find(b); if (a != b) parent[a] = b; }

        // ---- cells ----

        readonly List<GridFace> cellFaces = new List<GridFace>();
        readonly List<int> cellTris = new List<int>();
        FaceRecord[] cellFaceRecords = new FaceRecord[6];
        readonly List<int> cellT = new List<int>(), cellM = new List<int>(), cellV = new List<int>();

        /// <summary>
        /// The test of one cell (min node, size) with its faces and their edges: None if L meets it compatibly and with
        /// the contour's pattern, else the first reason found. meets: L meets the closed cell.
        /// </summary>
        /// <param name="contourCell"> the contour meets the cell (it has a crossed edge, or it may have: false only if none of its edges is crossed) </param>
        public ContouringCertificateReason Cell(Vector3i min, int size, bool contourCell, out bool meets)
        {
            meets = false;
            ensure_tables();
            int B = grid.BinSize;
            Vector3i bin = new Vector3i(fdiv(min.x, B), fdiv(min.y, B), fdiv(min.z, B));
            int bi = Array.BinarySearch(binKeys, bin_key(bin.x, bin.y, bin.z));
            int b0 = bi >= 0 ? binStart[bi] : 0, b1 = bi >= 0 ? binStart[bi + 1] : 0;
            Vector3i max = min + size * Vector3i.One;
            if (fdiv(max.x - 1, B) != bin.x || fdiv(max.y - 1, B) != bin.y || fdiv(max.z - 1, B) != bin.z)
                throw new ArgumentException("ContouringTopologyCertificate: a cell must lie inside one bin");
            Vector3d lo = grid.Node(min), hi = grid.Node(max);
            // the triangles that may meet the closed cell (a triangle that meets a face or an edge of the cell meets the
            // cell, so the faces and edges are tested with these too, whichever of their cells comes first)
            List<int> tris = cellTris;
            tris.Clear();
            for (int i = b0; i < b1; ++i) {
                int t = binTris[i];
                int o = 6 * t;
                if (triBox[o] > hi.x || triBox[o + 3] < lo.x || triBox[o + 1] > hi.y || triBox[o + 4] < lo.y || triBox[o + 2] > hi.z || triBox[o + 5] < lo.z) continue;
                if (may_meet_box(P[triV[3 * t]], P[triV[3 * t + 1]], P[triV[3 * t + 2]], lo, hi)) tris.Add(t);
            }
            // no triangle of L meets the cell, and no edge of it is crossed by the contour: nothing to test
            if (tris.Count == 0 && !contourCell)
                return ContouringCertificateReason.None;
            ContouringCertificateReason worst = ContouringCertificateReason.None;
            // the cell's vertices of L strictly inside (one on its boundary lies on a closed face: refused there)
            int st = ++stamp;
            cellV.Clear();
            for (int i = 0; i < tris.Count; ++i) {
                int t = tris[i];
                for (int j = 0; j < 3; ++j) {
                    int vid = triV[3 * t + j];
                    if (vtxMark[vid] == st) continue;
                    vtxMark[vid] = st;
                    Vector3d p = P[vid];
                    if (p.x > lo.x && p.x < hi.x && p.y > lo.y && p.y < hi.y && p.z > lo.z && p.z < hi.z) cellV.Add(vid);
                }
            }
            cellFaces.Clear();
            grid.CellFaces(min, size, cellFaces);
            int nFaces = cellFaces.Count;
            if (cellFaceRecords.Length < nFaces) cellFaceRecords = new FaceRecord[2 * nFaces];
            for (int i = 0; i < nFaces; ++i) {
                FaceRecord fr = face(cellFaces[i], tris);
                cellFaceRecords[i] = fr;
                if (fr.Fail != ContouringCertificateReason.None) worst = first_reason(worst, fr.Fail);
                if (fr.TCount > 0) meets = true;
            }
            if (cellV.Count > 0) meets = true;
            if (worst != ContouringCertificateReason.None)
                return worst;
            // T(c), M(c): those of the faces, and the stars of the vertices inside
            st = ++stamp;
            cellT.Clear(); cellM.Clear();
            for (int i = 0; i < nFaces; ++i) {
                FaceRecord fr = cellFaceRecords[i];
                for (int k = 0; k < fr.TCount; ++k) mark_triangle(faceT[fr.TStart + k], st);
                for (int k = 0; k < fr.MCount; ++k) mark_edge(faceM[fr.MStart + k], st);
            }
            for (int i = 0; i < cellV.Count; ++i) {
                int vid = cellV[i];
                for (int k = vtxTriStart[vid]; k < vtxTriStart[vid + 1]; ++k) mark_triangle(vtxTris[k], st);
                for (int k = vtxEdgeStart[vid]; k < vtxEdgeStart[vid + 1]; ++k) mark_edge(vtxEdges[k], st);
            }
            // parts: triangles joined across mesh edges that reach into the cell; each a disk (chi = 1)
            int n = cellT.Count;
            ensure(n);
            for (int i = 0; i < n; ++i) { parent[i] = i; count[i] = 0; triLocal[cellT[i]] = i; }
            for (int i = 0; i < cellM.Count; ++i) { int e = cellM[i]; union(triLocal[edgeT[2 * e]], triLocal[edgeT[2 * e + 1]]); }
            // (the triangles of a vertex inside are joined across its edges, which all reach into the cell: no union by vertex)
            for (int i = 0; i < n; ++i) count[find(i)] += 1;
            for (int i = 0; i < cellM.Count; ++i) count[find(triLocal[edgeT[2 * cellM[i]]])] -= 1;
            for (int i = 0; i < cellV.Count; ++i) count[find(triLocal[vtxTris[vtxTriStart[cellV[i]]]])] += 1;
            for (int i = 0; i < n; ++i)
                if (find(i) == i && count[i] != 1) return ContouringCertificateReason.Cell;
            return ContouringCertificateReason.None;
        }

        void mark_triangle(int t, int st) { if (triMark[t] != st) { triMark[t] = st; cellT.Add(t); } }
        void mark_edge(int e, int st) { if (edgeMark[e] != st) { edgeMark[e] = st; cellM.Add(e); } }

        static ContouringCertificateReason first_reason(ContouringCertificateReason a, ContouringCertificateReason b)
        {
            return (a == ContouringCertificateReason.None || (b != ContouringCertificateReason.None && b < a)) ? b : a;
        }
    }

    /// <summary>
    /// The level set of the exact winding number of a clean mesh at 1/2 (closed, manifold, no self-intersections): the
    /// components across which the winding number steps from 0 (the front side) to 1 (the back side).
    /// </summary>
    internal static class ContouringLevelSet
    {
        /// <summary>
        /// The mesh with the vertices of identical coordinates merged (the geometry and the winding number do not
        /// change), if the result is clean: closed, manifold (no bowtie), consistently oriented, no self-intersection
        /// (exact; so no triangle of zero area either). Null otherwise.
        /// </summary>
        public static DMesh3 CleanCopy(DMesh3 m)
        {
            var map = new Dictionary<Vector3d, int>();
            var outm = new DMesh3();
            var vmap = new int[m.MaxVertexID];
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                int id;
                if (!map.TryGetValue(p, out id)) { id = outm.AppendVertex(p); map[p] = id; }
                vmap[v] = id;
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                int a = vmap[tv.a], b = vmap[tv.b], c = vmap[tv.c];
                if (a == b || b == c || a == c || outm.AppendTriangle(a, b, c) < 0) return null;
            }
            if (outm.TriangleCount == 0 || !outm.IsClosed()) return null;
            foreach (int v in outm.VertexIndices()) {
                if (outm.GetVtxEdgeCount(v) == 0) { outm.RemoveVertex(v, false, false); continue; }
                if (outm.IsBowtieVertex(v)) return null;
            }
            if (gs.MeshRepairMetrics.InconsistentlyOrientedEdges(outm) > 0) return null;
            // (this also excludes a triangle of no area: in a closed manifold its middle vertex lies on the edge between the
            // other two, which the triangle across that edge has, so that triangle and the middle vertex's other triangles meet
            // beyond their shared vertices)
            if (gs.MeshRepairMetrics.HasSelfIntersections(outm)) return null;
            return outm;
        }

        /// <summary>
        /// Per triangle id: on the level set. Null if a component could not be decided (no line through its vertex
        /// pierced the surface in the attempts: not expected, one always exists).
        /// </summary>
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
                // the component's lexicographically largest vertex (any vertex would do; this one is canonical)
                int v = -1;
                foreach (int t in comp.Indices) {
                    Index3i tv = mesh.GetTriangle(t);
                    for (int j = 0; j < 3; ++j)
                        if (v < 0 || lex_greater(mesh.GetVertex(tv[j]), mesh.GetVertex(v))) v = tv[j];
                }
                int front;
                if (!FrontWinding(mesh, tree, v, root, out front))
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

        /// <summary>
        /// The winding number on the front side of the surface at vertex v (closed, embedded mesh), exactly: along a line
        /// from v to a far point q, the winding number just after v (the signed crossings of the triangles not at v,
        /// VolumeSampleCells.WindingAlong) and the jump across v's own triangles (the same perturbed line,
        /// ExactRayTriangle.LineCrossing; the line meets their planes only at v). A line that pierces the surface at v
        /// gives the values on both sides, front = the smaller (crossing a triangle against its normal adds 1). Such a
        /// line exists: otherwise the antipodal map would map the disk of the directions on one side into itself
        /// without a fixed point. False if none was found in the attempts.
        /// </summary>
        internal static bool FrontWinding(DMesh3 mesh, DMeshAABBTree3 tree, int v, AxisAlignedBox3d root, out int front)
        {
            front = 0;
            Vector3d p = mesh.GetVertex(v);
            Vector3d c = root.Center;
            double ext = 4 * Math.Max(root.MaxDim, Math.Max(Math.Max(Math.Abs(p.x - c.x), Math.Abs(p.y - c.y)), Math.Abs(p.z - c.z)));
            var rnd = new Random(31337);
            for (int attempt = 0; attempt < 64; ++attempt) {
                // a far point outside root (w = 0 there): one coordinate at +-ext from the centre, the others within
                int axis = rnd.Next(3);
                var dir = new Vector3d(rnd.Next(-1000, 1001), rnd.Next(-1000, 1001), rnd.Next(-1000, 1001)) / 1024.0;
                dir[axis] = rnd.Next(2) == 0 ? -1 : 1;
                Vector3d q = c + ext * dir;
                int jump = 0;
                bool inPlane = false;
                foreach (int t in mesh.VtxTrianglesItr(v)) {
                    Index3i tv = mesh.GetTriangle(t);
                    Vector3d a = mesh.GetVertex(tv.a), b = mesh.GetVertex(tv.b), cc = mesh.GetVertex(tv.c);
                    if (ExactPredicates.Orient3d(a, b, cc, q) == 0) { inPlane = true; break; }
                    jump += ExactRayTriangle.LineCrossing(p, q, a, b, cc);
                }
                if (inPlane || jump == 0) continue;
                if (jump != 1 && jump != -1) return false;
                int after = gs.VolumeSampleCells.WindingAlong(mesh, tree, p, q);
                // crossing a triangle along its normal (LineCrossing +1) lowers the winding number by 1
                front = Math.Min(after, after + jump);
                return true;
            }
            return false;
        }
    }
}
