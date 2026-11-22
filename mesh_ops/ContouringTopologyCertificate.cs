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
        /// <param name="grid"> integer certificate grid on which cells are evaluated </param>
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
        /// <param name="cells"> list of (cell minimum corner, cell side length in grid units) pairs to certify </param>
        /// <param name="contourCell"> per-cell flag indicating whether the contour meets the cell </param>
        /// <param name="parallel"> if true, evaluate chunks in parallel </param>
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
        /// <param name="min"> minimum corner of the cell in integer grid coordinates </param>
        /// <param name="size"> cell side length in integer grid units </param>
        /// <param name="contourCell"> the contour meets the cell (it has a crossed edge, or it may have: false only if none of its edges is crossed) </param>
        /// <param name="meets"> set to true if the level set L meets the closed cell </param>
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

        // ---- blocks (Theorem 6, section 7.10 of docs/design/CONTOURING_TOPOLOGY.md) ----

        /// <summary>
        /// The level-set side of the block test (Lemmas 6.2 and 6.3): the block R is the union of the cells (min node) of
        /// size s, all of one size and on one lattice (no refinement transition in or next to the block: the caller checks
        /// that). True if L meets R in disjoint disks properly embedded in R: every component of L in R meets the boundary of
        /// R, and the Euler characteristic of L in R equals the number of components. The first version asks general
        /// position (G) of every face, edge and node of every cell of the block, inside ones included, and answers false on
        /// any degeneracy; so every predicate below is the stage-1 one and none is a closed-set test.
        ///
        /// chi(L in R) = sum over the cells c of chi(L in c) - sum over the faces f inside R of chi(L in f) + sum over the
        /// edges e inside R of chi(L in e) (inclusion and exclusion over the cells; the coefficient of a face, edge or node on
        /// the boundary of a manifold block is 0, and no node meets L), with chi(L in c) = |T(c)| - |M(c)| + |V(c)|,
        /// chi(L in f) = |T(f)| - |M(f)| and chi(L in e) = the triangles crossing e (the counting lemma, section 7.4). The
        /// components: the pieces (t, c), t in T(c), joined in a cell across the mesh edges of M(c) and across an inside face
        /// f by the triangles of T(f).
        /// </summary>
        public bool Block(IList<Vector3i> cells, int s)
        {
            int n = cells.Count;
            var index = new Dictionary<Vector3i, int>(n);
            for (int i = 0; i < n; ++i) index[cells[i]] = i;
            int B = grid.BinSize;
            // per cell: the triangles that may meet it (as Cell), T(c), M(c), V(c)
            var cellTris = new List<int>[n];
            var Tc = new List<int>[n];
            long chi = 0;
            var insideFaceT = new Dictionary<GridFace, List<int>>();
            var cellM_ = new HashSet<int>[n];
            var edgeCount = new Dictionary<GridSegment, List<int>>();
            for (int i = 0; i < n; ++i) {
                Vector3i min = cells[i];
                Vector3i bin = new Vector3i(fdiv(min.x, B), fdiv(min.y, B), fdiv(min.z, B));
                Vector3i max = min + s * Vector3i.One;
                if (fdiv(max.x - 1, B) != bin.x || fdiv(max.y - 1, B) != bin.y || fdiv(max.z - 1, B) != bin.z) return false;
                int bi = Array.BinarySearch(binKeys, bin_key(bin.x, bin.y, bin.z));
                int b0 = bi >= 0 ? binStart[bi] : 0, b1 = bi >= 0 ? binStart[bi + 1] : 0;
                Vector3d lo = grid.Node(min), hi = grid.Node(max);
                var tris = new List<int>();
                for (int k = b0; k < b1; ++k) {
                    int t = binTris[k], o = 6 * t;
                    if (triBox[o] > hi.x || triBox[o + 3] < lo.x || triBox[o + 1] > hi.y || triBox[o + 4] < lo.y || triBox[o + 2] > hi.z || triBox[o + 5] < lo.z) continue;
                    if (may_meet_box(P[triV[3 * t]], P[triV[3 * t + 1]], P[triV[3 * t + 2]], lo, hi)) tris.Add(t);
                }
                cellTris[i] = tris;
                // V(c): vertices strictly inside (one on the boundary of a face is a degeneracy, found by the face's edges)
                int st = ++stamp;
                var V = new List<int>();
                foreach (int t in tris)
                    for (int j = 0; j < 3; ++j) {
                        int vid = triV[3 * t + j];
                        if (vtxMark[vid] == st) continue;
                        vtxMark[vid] = st;
                        Vector3d p = P[vid];
                        if (p.x > lo.x && p.x < hi.x && p.y > lo.y && p.y < hi.y && p.z > lo.z && p.z < hi.z) V.Add(vid);
                    }
                // the faces of c (with their T and M) and their edges
                var Tset = new HashSet<int>(); var Mset = new HashSet<int>();
                for (int a = 0; a < 3; ++a)
                    for (int side = 0; side < 2; ++side) {
                        Vector3i fmin = min; fmin[a] += side * s;
                        GridFace f = new GridFace(fmin, a, s);
                        List<int> fT, fM;
                        if (!block_face(f, tris, edgeCount, out fT, out fM)) return false;
                        foreach (int t in fT) Tset.Add(t);
                        foreach (int e in fM) Mset.Add(e);
                        // an inside face (the cell across is in the block) is subtracted once, from the lower cell's side
                        Vector3i nb = min; nb[a] += side == 0 ? -s : s;
                        if (side == 1 && index.ContainsKey(nb)) {
                            insideFaceT[f] = fT;
                            chi -= fT.Count - fM.Count;
                        }
                    }
                foreach (int vid in V) {
                    for (int k = vtxTriStart[vid]; k < vtxTriStart[vid + 1]; ++k) Tset.Add(vtxTris[k]);
                    for (int k = vtxEdgeStart[vid]; k < vtxEdgeStart[vid + 1]; ++k) Mset.Add(vtxEdges[k]);
                }
                chi += Tset.Count - Mset.Count + V.Count;
                Tc[i] = new List<int>(Tset);
                Tc[i].Sort();
                // the mesh edges of M(c) join the pieces of c
                cellM_[i] = Mset;
            }
            // edges inside R: the four cells around them in the block
            foreach (var kv in edgeCount) {
                GridSegment e = kv.Key;
                int u = (e.Axis + 1) % 3, v = (e.Axis + 2) % 3;
                bool inside = true;
                for (int q = 0; q < 4 && inside; ++q) {
                    Vector3i c = e.Start;
                    if ((q & 1) != 0) c[u] -= s;
                    if ((q & 2) != 0) c[v] -= s;
                    inside = index.ContainsKey(c);
                }
                if (inside) chi += kv.Value.Count;
            }
            // the pieces and their components
            var piece = new Dictionary<long, int>();
            int np = 0;
            long NT = triMark.Length;
            for (int i = 0; i < n; ++i) foreach (int t in Tc[i]) piece[i * NT + t] = np++;
            var par = new int[np];
            for (int i = 0; i < np; ++i) par[i] = i;
            Func<int, int> root = null;
            root = (x) => { while (par[x] != x) x = par[x] = par[par[x]]; return x; };
            Action<int, int> join = (x, y) => { x = root(x); y = root(y); if (x != y) par[x] = y; };
            for (int i = 0; i < n; ++i)
                foreach (int e in cellM_[i]) {
                    int ta = edgeT[2 * e], tb = edgeT[2 * e + 1];
                    int pa, pb;
                    if (piece.TryGetValue(i * NT + ta, out pa) && piece.TryGetValue(i * NT + tb, out pb)) join(pa, pb);
                    else return false;      // (both triangles of a mesh edge meeting c meet c: not expected)
                }
            foreach (var kv in insideFaceT) {
                GridFace f = kv.Key;
                Vector3i below = f.Min; below[f.Axis] -= s;
                int ia = index[below], ib = index[f.Min];
                foreach (int t in kv.Value) {
                    int pa, pb;
                    if (piece.TryGetValue(ia * NT + t, out pa) && piece.TryGetValue(ib * NT + t, out pb)) join(pa, pb);
                    else return false;
                }
            }
            // a component meets the boundary of R if a piece's triangle meets a face of its cell on the boundary
            var touches = new bool[np];
            for (int i = 0; i < n; ++i) {
                Vector3i min = cells[i];
                for (int a = 0; a < 3; ++a)
                    for (int side = 0; side < 2; ++side) {
                        Vector3i nb = min; nb[a] += side == 0 ? -s : s;
                        if (index.ContainsKey(nb)) continue;
                        Vector3i fmin = min; fmin[a] += side * s;
                        List<int> fT, fM;
                        block_face(new GridFace(fmin, a, s), cellTris[i], edgeCount, out fT, out fM);
                        foreach (int t in fT) touches[root(piece[i * NT + t])] = true;
                    }
            }
            int components = 0;
            for (int i = 0; i < np; ++i)
                if (root(i) == i) {
                    if (!touches[i]) return false;      // a component inside R (closed): not a disk
                    components++;
                }
            return chi == components;
        }

        // T(f) and M(f) of a face (the triangles of the mesh edges crossing it and those crossing its edges); false on
        // any degeneracy of the face or its edges ((G): a mesh edge meeting it otherwise than by crossing its interior,
        // a triangle meeting one of its edges otherwise than by a crossing)
        bool block_face(GridFace f, List<int> tris, Dictionary<GridSegment, List<int>> edgeTris, out List<int> T, out List<int> M)
        {
            T = new List<int>(); M = new List<int>();
            int a = f.Axis, u = (a + 1) % 3, v = (a + 2) % 3;
            double Pl = grid.Coord(a, f.Min[a]);
            double U0 = grid.Coord(u, f.Min[u]), U1 = grid.Coord(u, f.Min[u] + f.Size);
            double V0 = grid.Coord(v, f.Min[v]), V1 = grid.Coord(v, f.Min[v] + f.Size);
            var Ts = new HashSet<int>(); var Ms = new HashSet<int>();
            foreach (int t in tris) {
                int o = 6 * t;
                if (triBox[o + a] > Pl || triBox[o + 3 + a] < Pl || triBox[o + u] > U1 || triBox[o + 3 + u] < U0
                    || triBox[o + v] > V1 || triBox[o + 3 + v] < V0) continue;
                for (int j = 0; j < 3; ++j) {
                    int e = triE[3 * t + j];
                    if (Ms.Contains(e)) continue;
                    int sc = segment_face(P[edgeV[2 * e]], P[edgeV[2 * e + 1]], a, Pl, U0, U1, V0, V1);
                    if (sc == Degenerate) return false;
                    if (sc == Cross) { Ms.Add(e); Ts.Add(edgeT[2 * e]); Ts.Add(edgeT[2 * e + 1]); }
                }
            }
            for (int k = 0; k < 4; ++k) {
                Vector3i c0 = f.Corner(k), c1 = f.Corner((k + 1) % 4), d = c1 - c0;
                int axis = d.x != 0 ? 0 : d.y != 0 ? 1 : 2;
                GridSegment seg = new GridSegment(d[axis] > 0 ? c0 : c1, axis, f.Size);
                List<int> et;
                if (!edgeTris.TryGetValue(seg, out et)) {
                    et = new List<int>();
                    Vector3d A = grid.Node(seg.Start), Bn = grid.Node(seg.End);
                    bool degenerate = false;
                    foreach (int t in tris) {
                        int sc = segment_triangle(A, Bn, axis, P[triV[3 * t]], P[triV[3 * t + 1]], P[triV[3 * t + 2]]);
                        if (sc == Degenerate) { degenerate = true; break; }
                        if (sc == Cross) et.Add(t);
                    }
                    if (degenerate) return false;
                    edgeTris[seg] = et;
                }
                foreach (int t in et) Ts.Add(t);
            }
            T.AddRange(Ts); T.Sort();
            M.AddRange(Ms); M.Sort();
            return true;
        }

        /// <summary>
        /// The contour side of the block test (Lemma 6.4), from the pattern only: the patches of the cells (the
        /// marching-cubes patches by the node signs and the face rule, as DualContouring builds them), joined across the
        /// faces inside the block by the crossed edges they share. True if every component reaches the boundary of R and
        /// P - A + Z (patches, arcs on inside faces, crossed inside edges) equals their number. False if an edge is crossed
        /// where its node signs agree or the reverse (the pattern is not the one of the signs).
        /// </summary>
        public static bool ContourBlock(CertificateGrid grid, IList<Vector3i> cells, int s)
        {
            int n = cells.Count;
            var index = new Dictionary<Vector3i, int>(n);
            for (int i = 0; i < n; ++i) index[cells[i]] = i;
            // nodes: (cell, local edge) of crossed edges; patch roots per cell
            var parList = new List<int>();
            var touchList = new List<bool>();
            long P = 0;
            var patchOfEdge = new int[n * 12];
            for (int i = 0; i < n; ++i) {
                Vector3i min = cells[i];
                int[] local = new int[12];
                bool ambiguous;
                int np = cell_patches(grid, min, s, local, out ambiguous);
                if (np < 0) return false;
                int base0 = parList.Count;
                for (int g = 0; g < np; ++g) { parList.Add(base0 + g); touchList.Add(false); }
                for (int le = 0; le < 12; ++le) patchOfEdge[12 * i + le] = local[le] < 0 ? -1 : base0 + local[le];
                P += np;
                // a patch reaches the boundary if one of its edges lies on a face of the cell on the boundary of R
                for (int le = 0; le < 12; ++le) {
                    int g = patchOfEdge[12 * i + le];
                    if (g < 0) continue;
                    int ea = le / 4, bu = le & 1, bv = (le >> 1) & 1;
                    int u = (ea + 1) % 3, v = (ea + 2) % 3;
                    Vector3i nbu = min; nbu[u] += bu == 0 ? -s : s;
                    Vector3i nbv = min; nbv[v] += bv == 0 ? -s : s;
                    if (!index.ContainsKey(nbu) || !index.ContainsKey(nbv)) touchList[g] = true;
                }
            }
            int[] gpar = parList.ToArray();
            long A = 0, Z = 0;
            for (int i = 0; i < n; ++i) {
                Vector3i min = cells[i];
                // the faces to the +a neighbour in the block: each crossed edge of the face joins the two cells' patches
                for (int a = 0; a < 3; ++a) {
                    Vector3i nb = min; nb[a] += s;
                    int j;
                    if (!index.TryGetValue(nb, out j)) continue;
                    int crossedOnFace = 0;
                    for (int le = 0; le < 12; ++le) {
                        int ea = le / 4;
                        if (ea == a) continue;
                        // local edges of cell i on its +a face, and the same edge of cell j on its -a face
                        int bu = le & 1, bv = (le >> 1) & 1;
                        int u = (ea + 1) % 3;
                        int bitA = (u == a) ? bu : bv;
                        if (bitA != 1) continue;
                        int leJ = (u == a) ? (le & ~1) : (le & ~2);
                        int gi = patchOfEdge[12 * i + le], gj = patchOfEdge[12 * j + leJ];
                        if ((gi < 0) != (gj < 0)) return false;
                        if (gi < 0) continue;
                        crossedOnFace++;
                        uf_union(gpar, gi, gj);
                    }
                    A += crossedOnFace / 2;
                }
                // crossed edges inside R, counted at the cell where the edge is the local edge with both bits 1 (the cell
                // below and behind it along the two other axes)
                for (int ea = 0; ea < 3; ++ea) {
                    int le = 4 * ea + 3;
                    if (patchOfEdge[12 * i + le] < 0) continue;
                    int u = (ea + 1) % 3, v = (ea + 2) % 3;
                    bool inside = true;
                    for (int q = 1; q < 4 && inside; ++q) {
                        Vector3i c = min;
                        if ((q & 1) != 0) c[u] += s;
                        if ((q & 2) != 0) c[v] += s;
                        inside = index.ContainsKey(c);
                    }
                    if (inside) Z++;
                }
            }
            var rootTouches = new Dictionary<int, bool>();
            for (int g = 0; g < gpar.Length; ++g) {
                int r = uf_find(gpar, g);
                bool t;
                rootTouches.TryGetValue(r, out t);
                rootTouches[r] = t || touchList[g];
            }
            foreach (var kv in rootTouches) if (!kv.Value) return false;
            return P - A + Z == rootTouches.Count;
        }

        /// <summary> (tests) the number of patches ContourBlock reads for one cell (as DualContouring builds them), -1 if the pattern is not the signs' </summary>
        internal static int CellPatchCount(CertificateGrid grid, Vector3i min, int s, out bool ambiguous)
        {
            int[] patchOfEdge = new int[12];
            return cell_patches(grid, min, s, patchOfEdge, out ambiguous);
        }

        // the marching-cubes patches of a cell from the node signs and the face rule (inside corners joined along cube edges,
        // outside corners also across an ambiguous face; as DualContouring.cell_patches): the patch count, and per local
        // edge its patch or -1; -1 if an edge is crossed where its node signs agree or the reverse
        static int cell_patches(CertificateGrid grid, Vector3i min, int s, int[] patchOfEdge, out bool ambiguous)
        {
            bool[] inside = new bool[8];
            int[] par = new int[8];
            for (int lc = 0; lc < 8; ++lc) { inside[lc] = grid.NodeInside(min + s * new Vector3i(lc & 1, (lc >> 1) & 1, (lc >> 2) & 1)); par[lc] = lc; }
            ambiguous = false;
            for (int le = 0; le < 12; ++le) { int c0, c1; edge_corners(le, out c0, out c1); if (inside[c0] == inside[c1]) uf_union(par, c0, c1); }
            for (int a = 0; a < 3; ++a)
                for (int side = 0; side < 2; ++side) {
                    int[] fc = face_corners(a, side);
                    if (inside[fc[0]] == inside[fc[2]] && inside[fc[1]] == inside[fc[3]] && inside[fc[0]] != inside[fc[1]]) {
                        ambiguous = true;
                        if (inside[fc[0]]) uf_union(par, fc[1], fc[3]); else uf_union(par, fc[0], fc[2]);
                    }
                }
            var keys = new List<int>(4);
            for (int le = 0; le < 12; ++le) {
                patchOfEdge[le] = -1;
                int c0, c1; edge_corners(le, out c0, out c1);
                bool crossed = grid.Crossed(local_segment(min, s, le));
                if (crossed != (inside[c0] != inside[c1])) return -1;
                if (!crossed) continue;
                int cin = inside[c0] ? c0 : c1, cout = inside[c0] ? c1 : c0;
                int key = uf_find(par, cin) * 8 + uf_find(par, cout);
                int g = keys.IndexOf(key);
                if (g < 0) { g = keys.Count; keys.Add(key); }
                patchOfEdge[le] = g;
            }
            return keys.Count;
        }

        // local corner lc = (x, y, z) bits; local edge le = 4 axis + (bit of axis + 1) + 2 (bit of axis + 2)
        static void edge_corners(int le, out int c0, out int c1)
        {
            int ea = le / 4, bu = le & 1, bv = (le >> 1) & 1, u = (ea + 1) % 3, v = (ea + 2) % 3;
            c0 = (bu << u) | (bv << v);
            c1 = c0 | (1 << ea);
        }
        // the corners of the face normal to a on side 'side', in cyclic order
        static int[] face_corners(int a, int side)
        {
            int u = (a + 1) % 3, v = (a + 2) % 3, b = side << a;
            return new int[] { b, b | (1 << u), b | (1 << u) | (1 << v), b | (1 << v) };
        }
        static GridSegment local_segment(Vector3i min, int s, int le)
        {
            int ea = le / 4, bu = le & 1, bv = (le >> 1) & 1, u = (ea + 1) % 3, v = (ea + 2) % 3;
            Vector3i st = min; st[u] += bu * s; st[v] += bv * s;
            return new GridSegment(st, ea, s);
        }
        static int uf_find(int[] par, int i) { while (par[i] != i) i = par[i] = par[par[i]]; return i; }
        static void uf_union(int[] par, int a, int b) { a = uf_find(par, a); b = uf_find(par, b); if (a != b) par[a] = b; }

        /// <summary>
        /// The ball test of remark 2 of Theorem 6 for a union of lattice cubes (cell coordinates): every node's link (its
        /// octants in the union) is a disk or the whole sphere, the boundary is connected, and the Euler characteristic of
        /// the union is 1.
        /// </summary>
        public static bool IsBall(ICollection<Vector3i> cubes)
        {
            var set = cubes as HashSet<Vector3i> ?? new HashSet<Vector3i>(cubes);
            if (set.Count == 0) return false;
            var nodes = new HashSet<Vector3i>();
            var elems = new HashSet<Vector3i>();
            long chi = 0;
            foreach (var c in set)
                for (int dz = 0; dz <= 2; ++dz) for (int dy = 0; dy <= 2; ++dy) for (int dx = 0; dx <= 2; ++dx) {
                    var key = new Vector3i(2 * c.x + dx, 2 * c.y + dy, 2 * c.z + dz);
                    if (elems.Add(key)) chi += ((dx & 1) + (dy & 1) + (dz & 1)) % 2 == 0 ? 1 : -1;
                    if ((dx & 1) == 0 && (dy & 1) == 0 && (dz & 1) == 0) nodes.Add(new Vector3i(c.x + dx / 2, c.y + dy / 2, c.z + dz / 2));
                }
            if (chi != 1) return false;
            foreach (var v in nodes) {
                int mask = 0;
                for (int o = 0; o < 8; ++o)
                    if (set.Contains(new Vector3i(v.x - 1 + (o & 1), v.y - 1 + ((o >> 1) & 1), v.z - 1 + ((o >> 2) & 1)))) mask |= 1 << o;
                if (!link_disk_or_sphere(mask)) return false;
            }
            // boundary faces (a cube of the union on one side only), connected through their edges
            // (keys as value tuples: KeyValuePair has no hash of its own, a dictionary of them is quadratic)
            var faces = new List<(int, Vector3i)>();
            foreach (var c in set)
                for (int a = 0; a < 3; ++a)
                    for (int side = 0; side < 2; ++side) {
                        var nb = c; nb[a] += side == 0 ? -1 : 1;
                        if (set.Contains(nb)) continue;
                        var m = c; m[a] += side;
                        faces.Add((a, m));
                    }
            var owner = new Dictionary<(int, Vector3i), int>();
            var par = new int[faces.Count];
            for (int i = 0; i < par.Length; ++i) par[i] = i;
            for (int i = 0; i < faces.Count; ++i) {
                int a = faces[i].Item1, u = (a + 1) % 3, w = (a + 2) % 3;
                Vector3i m = faces[i].Item2, du = Vector3i.Zero, dw = Vector3i.Zero;
                du[u] = 1; dw[w] = 1;
                var es = new[] { (u, m), (u, m + dw), (w, m), (w, m + du) };
                foreach (var e in es) {
                    int j;
                    if (owner.TryGetValue(e, out j)) uf_union(par, i, j); else owner[e] = i;
                }
            }
            int roots = 0;
            for (int i = 0; i < par.Length; ++i) if (uf_find(par, i) == i) roots++;
            return roots == 1;
        }

        // the octants of a node (bit o = the octant with offsets (o & 1, o >> 1 & 1, o >> 2 & 1)) on the octahedral sphere
        // around it: a disk or the whole sphere
        static bool link_disk_or_sphere(int mask)
        {
            if (mask == 255) return true;
            if (mask == 0) return false;
            // around each of the six axis directions the four octants touching it must be one arc (or all or none)
            for (int a = 0; a < 3; ++a)
                for (int sgn = 0; sgn < 2; ++sgn) {
                    int u = (a + 1) % 3, w = (a + 2) % 3;
                    int[] ring = { (sgn << a), (sgn << a) | (1 << u), (sgn << a) | (1 << u) | (1 << w), (sgn << a) | (1 << w) };
                    int occ = 0, runs = 0;
                    for (int k = 0; k < 4; ++k) {
                        bool cur = ((mask >> ring[k]) & 1) != 0, prev = ((mask >> ring[(k + 3) % 4]) & 1) != 0;
                        if (cur) occ++;
                        if (cur && !prev) runs++;
                    }
                    if (occ > 0 && occ < 4 && runs != 1) return false;
                }
            // connected through shared sides (octants that differ in one bit), and V - E + F = 1
            int first = -1;
            for (int o = 0; o < 8; ++o) if (((mask >> o) & 1) != 0) { first = o; break; }
            int seen = 1 << first;
            var stack = new Stack<int>(); stack.Push(first);
            while (stack.Count > 0) {
                int o = stack.Pop();
                for (int b = 0; b < 3; ++b) {
                    int q = o ^ (1 << b);
                    if (((mask >> q) & 1) != 0 && ((seen >> q) & 1) == 0) { seen |= 1 << q; stack.Push(q); }
                }
            }
            if (seen != mask) return false;
            // the octants as spherical triangles: vertices (axis, sign) = 2 axis + sign, edges the pairs of them
            int F = 0, vmask = 0;
            var edges = new HashSet<int>();
            for (int o = 0; o < 8; ++o) {
                if (((mask >> o) & 1) == 0) continue;
                F++;
                int[] vs = new int[3];
                for (int a = 0; a < 3; ++a) { vs[a] = 2 * a + ((o >> a) & 1); vmask |= 1 << vs[a]; }
                for (int i = 0; i < 3; ++i) for (int j = i + 1; j < 3; ++j) edges.Add(vs[i] * 8 + vs[j]);
            }
            int V = 0;
            for (int k = 0; k < 6; ++k) if (((vmask >> k) & 1) != 0) V++;
            return V - edges.Count + F == 1;
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
