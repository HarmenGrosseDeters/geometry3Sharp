// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary> why a cube is not certified for a level by TwoLevelSheetCertificate </summary>
    internal enum SheetCertificateReason
    {
        None = 0,
        /// <summary> the sign of the level at a node is not certified, or is not the sign the mesher used </summary>
        NodeSign = 1,
        /// <summary> an edge may be crossed twice: no certified sign of the derivative along it where the level may be zero (S1) </summary>
        Edge = 2,
        /// <summary> a face has no direction of strict growth in its plane, or on a face with alternating corner signs the level set may cross the split's diagonal (S2) </summary>
        Face = 3,
        /// <summary> no direction of strict growth on the cube or block (S3, B3) </summary>
        Direction = 4,
        /// <summary> more than one closed curve of the level set on the boundary of the cube or block </summary>
        Circles = 5,
        /// <summary> the mesh in the block is not one disk per closed curve on its boundary </summary>
        MeshInBlock = 6,
        /// <summary> the block that would hold the cube exceeds MaxBlockSide or reaches the outermost layer </summary>
        BlockLimit = 7,
        /// <summary> a cube of the outermost layer that the level set may meet </summary>
        OuterLayer = 8,
        /// <summary> the cube was not reached by the tests (no run of Compute leaves one; a cube is not zero-free by default) </summary>
        Unvisited = 9
    }

    /// <summary> why the whole run is not certified </summary>
    [Flags]
    internal enum SheetCertificateRunFailure
    {
        None = 0,
        /// <summary> the mesher has no record (no run with RecordCertificateData, a Domain, a cancelled run), or its mesh is not the recorded one </summary>
        NoRecord = 1,
        /// <summary> the half width was a function of the position: the levels are not g - w and -g - w </summary>
        VariableHalfWidth = 2,
        /// <summary> the mesh side of Lemma 9 fails as a whole: a node value of exactly 0, a cut polygon, a refused triangle, a vertex that is not strictly inside its edge </summary>
        MeshSide = 4,
        /// <summary> the mesh has self-intersections (exact test) </summary>
        SelfIntersections = 8,
        /// <summary> the level set may meet the outermost layer of cubes </summary>
        OuterLayer = 16
    }

    internal struct SheetUncertifiedCell
    {
        public int Level;
        public Vector3i Cell;
        public SheetCertificateReason Reason;
    }

    /// <summary>
    /// A topology certificate for the two sides of a wall made by TwoLevelMarchingTetrahedra without a domain: if Certified,
    /// each of the two level sets g = +w and g = -w of the field is a closed surface inside the grid, and the part of the
    /// mesh that belongs to it is isotopic to it, by a map that keeps every certified cube and every block in place. The two
    /// statements are per level: the pair of surfaces together is not covered. Internal, report only.
    ///
    /// The proof is Theorem 8 of docs/design/CONTOURING_BLOCK_CERTIFICATE.md (Theorem 5 of the contouring note on a complex
    /// with box-shaped blocks) with Lemma 9 as its mesh side. Nothing here is an estimate: every decision is the sign of a
    /// bound of an outward-rounded interval (ExactInterval), an exact comparison of doubles, or a count on the mesher's record.
    ///
    /// Per level and cube: zero-free (the enclosure of the level over the cube or over all its sub-boxes excludes 0, and the
    /// mesher's signs at its nodes are that sign), or (S0) the signs at its eight nodes certified and equal to the mesher's,
    /// (S1) on every edge that may hold a zero one sign of the derivative along it, (S2) on every face that may hold a zero a
    /// direction of strict growth in its plane, and on a face with alternating signs no zero on the diagonal the mesher's
    /// split gives it, (S3) a direction of strict growth on the sub-boxes that may hold a zero and one closed curve on the
    /// cube's boundary. A cube that fails is the seed of a box-shaped block that grows by one layer on the sides that carry
    /// a failing node, edge or face until (B0) to (B3) hold on its boundary and the mesh in it is one disk per closed curve
    /// (at most one); cubes that end in no such block are listed. The mesh side of Lemma 9, (T1) to (T4), is tested for the
    /// run as a whole.
    ///
    /// Never certified: a wall cut by a domain, a half width that varies, a level set through a node, a tangency or a second
    /// sheet that no block of the allowed size absorbs, a level set that leaves the grid.
    /// Serial but for the exact self-intersection test of the mesh (MeshRepairMetrics, whose answer does not depend on the
    /// threads). Not thread-safe. A box that Field.Enclose does not report as Smooth has no usable gradient here: it passes
    /// only if the level has no zero in it.
    /// </summary>
    internal sealed class TwoLevelSheetCertificate
    {
        /// <summary> the field g of the mesher, with enclosures of its value and gradient over a box </summary>
        public IntervalImplicit3d Field;
        /// <summary> the mesher, after a Generate with RecordCertificateData and without a Domain </summary>
        public TwoLevelMarchingTetrahedra Mesher;
        /// <summary>
        /// a cube, edge or face that fails as one box is cut into 2^t pieces per axis, t up to this. Memory: up to 8^t enclosures
        /// of 72 bytes per cube are kept, for at most max(MaxBlockSide^3, MaxCachedEnclosures / 8^t) cubes: 76 MB with the
        /// defaults and at t = 4; 1.2 GB at a block side of 64 with t = 2.
        /// </summary>
        public int MaxTightening = 2;
        /// <summary> the largest side of a block, in cubes </summary>
        public int MaxBlockSide = 6;

        public bool Certified;
        public SheetCertificateRunFailure RunFailure;
        /// <summary> the cubes that are not certified, sorted by (level, z, y, x) </summary>
        public List<SheetUncertifiedCell> UncertifiedCells = new List<SheetUncertifiedCell>();
        /// <summary> per level: cubes not proven zero-free; those certified on their own; those certified in a block; the blocks </summary>
        public int[] SurfaceCells = new int[2], CellsCertifiedAlone = new int[2], CellsInBlocks = new int[2], Blocks = new int[2];
        /// <summary> per level: the certified blocks as ranges of cubes (lo x, y, z, hi x, y, z; hi exclusive), with disjoint interiors, in the order they were made </summary>
        public List<int[]>[] BlockBoxes = { new List<int[]>(), new List<int[]>() };
        /// <summary> per level and cube (x + nx (y + ny z)): 0 zero-free, 1 certified on its own, 2 not certified, 3 in a certified block, 255 not reached (not certified); null before Compute </summary>
        public byte[][] CellStates = new byte[2][];

        TwoLevelMarchingTetrahedra.CertificateRecord rec;
        int nx, ny, nz;                 // cubes per axis
        int n1x, n1y;                   // nodes per axis (x, y)
        double[][] X;                   // node coordinates per axis, as the mesher computes them
        ExactInterval W;
        int level;                      // the level in work: 0 for g - w, 1 for -g - w
        sbyte[] nodeSign;               // per node: 0 not known, 1 / -1 certified, 2 not certifiable
        Dictionary<long, bool> edgeOk, faceOk;
        Dictionary<int, List<IntervalEnclosure>> cellPieces;
        byte[] cellState;               // 0 zero-free, 1 certified alone, 2 refused (a seed), 3 in a block, 255 not reached
        internal const byte UnvisitedState = 255;
        /// <summary> for a test of the unvisited state: cubes that the next Compute leaves out; null in every other use, reset by Compute </summary>
        internal Predicate<Vector3i> SkipCubeForTest;
        Predicate<Vector3i> skipCube;
        /// <summary> the budget of kept enclosures (the kept cubes are emptied when it is reached; one block's cubes are always kept) </summary>
        internal int MaxCachedEnclosures = 1 << 20;
        /// <summary> the largest number of cubes whose enclosures were kept at one time in the last Compute </summary>
        internal int PeakCachedCubes;
        SheetCertificateReason[] cellReason;
        int[] owner;
        List<int[]> boxes;

        public void Compute()
        {
            Certified = false; RunFailure = SheetCertificateRunFailure.None;
            UncertifiedCells = new List<SheetUncertifiedCell>();
            SurfaceCells = new int[2]; CellsCertifiedAlone = new int[2]; CellsInBlocks = new int[2]; Blocks = new int[2];
            CellStates = new byte[2][];
            BlockBoxes = new[] { new List<int[]>(), new List<int[]>() };
            PeakCachedCubes = 0;
            skipCube = SkipCubeForTest; SkipCubeForTest = null;
            if (Field == null) throw new ArgumentNullException(nameof(Field));
            if (Mesher == null) throw new ArgumentNullException(nameof(Mesher));
            if (MaxTightening < 0 || MaxTightening > 4) throw new ArgumentOutOfRangeException(nameof(MaxTightening));
            if (MaxBlockSide < 1 || MaxBlockSide > 64) throw new ArgumentOutOfRangeException(nameof(MaxBlockSide));

            rec = Mesher.Certificate;
            DMesh3 mesh = Mesher.Mesh;
            if (rec == null || mesh == null || rec.VertexA.Count != mesh.MaxVertexID || rec.VertexB.Count != mesh.MaxVertexID
                || rec.VertexLevel.Count != mesh.MaxVertexID || rec.TriangleLevel.Count != mesh.MaxTriangleID
                || mesh.VertexCount != mesh.MaxVertexID || mesh.TriangleCount != mesh.MaxTriangleID
                || rec.NodeFlags == null || rec.Nodes.x < 2 || rec.Nodes.y < 2 || rec.Nodes.z < 2
                || rec.NodeFlags.Length != (long)rec.Nodes.x * rec.Nodes.y * rec.Nodes.z) {
                RunFailure = SheetCertificateRunFailure.NoRecord; return;
            }
            // a half width that was a function is recorded as NaN
            if (!(rec.HalfWidth > 0) || double.IsInfinity(rec.HalfWidth)) { RunFailure = SheetCertificateRunFailure.VariableHalfWidth; return; }
            nx = rec.Nodes.x - 1; ny = rec.Nodes.y - 1; nz = rec.Nodes.z - 1;
            n1x = rec.Nodes.x; n1y = rec.Nodes.y;
            X = new double[3][];
            for (int a = 0; a < 3; ++a) {
                X[a] = new double[rec.Nodes[a]];
                for (int i = 0; i < X[a].Length; ++i) X[a][i] = rec.Origin[a] + rec.CellSize * i;
                for (int i = 0; i + 1 < X[a].Length; ++i) if (!(X[a][i + 1] > X[a][i])) { RunFailure = SheetCertificateRunFailure.NoRecord; return; }
                // finite: the first is below the second, so only the last can be infinite
                if (double.IsInfinity(X[a][X[a].Length - 1])) { RunFailure = SheetCertificateRunFailure.NoRecord; return; }
            }
            W = ExactInterval.Of(rec.HalfWidth);

            // the mesh side of Lemma 9 for the run: (T3), (T1), (T2), (T4)
            if (rec.ClippedPolygonVertices != 0 || rec.NonManifoldTriangles != 0) RunFailure |= SheetCertificateRunFailure.MeshSide;
            // no node value of exactly 0, and one level negative at every node (the mesher skips a tetrahedron where a level is
            // negative at no node, with whatever the other level has there; g - w and -g - w are never both non-negative)
            foreach (byte flags in rec.NodeFlags) if ((flags & 48) != 0 || (flags & 3) == 0) { RunFailure |= SheetCertificateRunFailure.MeshSide; break; }
            for (int vid = 0; vid < mesh.MaxVertexID; ++vid)
                if (!VertexStrictlyInsideItsEdge(mesh.GetVertex(vid), rec.VertexA[vid], rec.VertexB[vid], rec.VertexLevel[vid])) { RunFailure |= SheetCertificateRunFailure.MeshSide; break; }
            // every triangle has the level of its three vertices (which is 0 or 1 by the vertex test)
            for (int tid = 0; tid < mesh.MaxTriangleID; ++tid) {
                byte l = rec.TriangleLevel[tid];
                Index3i t = mesh.GetTriangle(tid);
                if (rec.VertexLevel[t.a] != l || rec.VertexLevel[t.b] != l || rec.VertexLevel[t.c] != l) { RunFailure |= SheetCertificateRunFailure.MeshSide; break; }
            }
            if (gs.MeshRepairMetrics.HasSelfIntersections(mesh)) RunFailure |= SheetCertificateRunFailure.SelfIntersections;

            // a field that offers a cache is asked through one cache for this run (no cache: asked plainly)
            cachedField = Field as IntervalImplicit3dCached;
            encloseCache = cachedField != null ? cachedField.NewEncloseCache() : null;
            try {
                for (level = 0; level < 2; ++level) {
                    nodeSign = new sbyte[rec.NodeFlags.Length];
                    edgeOk = new Dictionary<long, bool>(); faceOk = new Dictionary<long, bool>();
                    cellPieces = new Dictionary<int, List<IntervalEnclosure>>();
                    cellState = new byte[nx * ny * nz];
                    for (int c = 0; c < cellState.Length; ++c) cellState[c] = UnvisitedState;
                    cellReason = new SheetCertificateReason[cellState.Length];
                    owner = new int[cellState.Length];
                    for (int c = 0; c < owner.Length; ++c) owner[c] = -1;
                    boxes = new List<int[]>();
                    descend(0, 0, 0, nx, ny, nz);
                    growBlocks();
                    for (int k = 0; k < nz; ++k) for (int j = 0; j < ny; ++j) for (int i = 0; i < nx; ++i) {
                        int c = i + nx * (j + ny * k);
                        if (cellState[c] == 1) ++CellsCertifiedAlone[level];
                        else if (cellState[c] == 3) ++CellsInBlocks[level];
                        else if (cellState[c] == 2) UncertifiedCells.Add(new SheetUncertifiedCell { Level = level, Cell = new Vector3i(i, j, k), Reason = cellReason[c] });
                        else if (cellState[c] != 0) UncertifiedCells.Add(new SheetUncertifiedCell { Level = level, Cell = new Vector3i(i, j, k), Reason = SheetCertificateReason.Unvisited });
                    }
                    foreach (int[] b in boxes) if (b != null) BlockBoxes[level].Add(b);
                    Blocks[level] = BlockBoxes[level].Count;
                    CellStates[level] = cellState;
                }
            } finally { cachedField = null; encloseCache = null; }  // also when the field throws
            foreach (SheetUncertifiedCell u in UncertifiedCells) if (u.Reason == SheetCertificateReason.OuterLayer) RunFailure |= SheetCertificateRunFailure.OuterLayer;
            Certified = RunFailure == SheetCertificateRunFailure.None && UncertifiedCells.Count == 0;
        }

        /// <summary>
        /// (T2) of Lemma 9 for one vertex: on a tetrahedron edge between the nodes a and b of the grid, with the coordinates the
        /// two nodes share exactly and the others strictly between, and the vertex's level has one node of the edge inside
        /// and one outside (the mesher's signs). False for a vertex without an edge or a level.
        /// </summary>
        internal bool VertexStrictlyInsideItsEdge(Vector3d v, int a, int b, int vertexLevel)
        {
            if (a < 0 || b <= a || b >= rec.NodeFlags.Length || vertexLevel > 1) return false;
            if (((rec.NodeFlags[a] ^ rec.NodeFlags[b]) >> vertexLevel & 1) == 0) return false;
            int moving = 0;
            for (int ax = 0; ax < 3; ++ax) {
                int ia = ax == 0 ? a % n1x : ax == 1 ? a / n1x % n1y : a / (n1x * n1y), ib = ax == 0 ? b % n1x : ax == 1 ? b / n1x % n1y : b / (n1x * n1y);
                if (Math.Abs(ia - ib) > 1) return false;
                double pa = X[ax][ia], pb = X[ax][ib];
                if (ia == ib) { if (v[ax] != pa) return false; }
                else { ++moving; if (!(v[ax] > Math.Min(pa, pb) && v[ax] < Math.Max(pa, pb))) return false; }
            }
            return moving == 1 || moving == 2;
        }

        // ---- enclosures of the level in work ----

        // The level over a box: g - w or -g - w, with the gradient of g. The gradient of the second level is the negative of
        // it, which no test below needs: each asks for one sign of a derivative or for some direction d with d . g > 0, and
        // -d does for -g what d does for g.
        // a field that reuses work between its enclosures does so through a cache of this run
        IntervalImplicit3dCached cachedField; object encloseCache;
        internal bool HoldsAnEncloseCache { get { return encloseCache != null; } }

        IntervalEnclosure enclose(ExactInterval x, ExactInterval y, ExactInterval z)
        {
            IntervalEnclosure e = encloseCache != null ? cachedField.Enclose(x, y, z, encloseCache) : Field.Enclose(x, y, z);
            e.F = level == 0 ? e.F - W : -e.F - W;
            // a box that may hold a point where g is not smooth has no gradient to test: every test of a derivative fails on it
            if (!e.Smooth) e.Gx = e.Gy = e.Gz = new ExactInterval(double.NegativeInfinity, double.PositiveInfinity);
            return e;
        }

        /// <summary> can the interval hold 0? True unless a bound certifies a sign (so also for NaN bounds and for a bound of exactly 0) </summary>
        internal static bool MayBeZero(ExactInterval f) { return !(f.Lo > 0) && !(f.Hi < 0); }

        // breakpoint j of m between the nodes i and i + 1 of an axis: non-decreasing in j, the ends are the node coordinates
        double breakpoint(int axis, int i, int j, int m)
        {
            double[] a = X[axis];
            if (j <= 0) return a[i];
            if (j >= m) return a[i + 1];
            double p = a[i] + (a[i + 1] - a[i]) * ((double)j / m);
            return p < a[i] ? a[i] : p > a[i + 1] ? a[i + 1] : p;
        }

        ExactInterval span(int axis, int i, int j, int m) { return new ExactInterval(breakpoint(axis, i, j, m), breakpoint(axis, i, j + 1, m)); }
        ExactInterval point(int axis, int i) { return ExactInterval.Of(X[axis][i]); }

        int nodeId(int i, int j, int k) { return i + n1x * (j + n1y * k); }

        /// <summary> the certified sign of an interval: 1, -1, or 0 if it may hold 0 </summary>
        internal static int CertifiedSign(ExactInterval f) { return f.Lo > 0 ? 1 : f.Hi < 0 ? -1 : 0; }

        // the certified sign of the level at a node (1 outside, -1 inside), 2 if the enclosure at the point cannot decide
        int pointSign(int i, int j, int k)
        {
            int id = nodeId(i, j, k);
            if (nodeSign[id] != 0) return nodeSign[id];
            int s = CertifiedSign(enclose(point(0, i), point(1, j), point(2, k)).F);
            nodeSign[id] = (sbyte)(s == 0 ? 2 : s);
            return nodeSign[id];
        }

        // the mesher's sign at a node for the level in work: -1 inside
        int flagSign(int i, int j, int k) { return (rec.NodeFlags[nodeId(i, j, k)] >> level & 1) != 0 ? -1 : 1; }

        // (S0) / (B0) at a node: the sign is certified and is the mesher's
        bool nodeOk(int i, int j, int k) { return pointSign(i, j, k) == flagSign(i, j, k); }

        /// <summary> (S1) for one grid edge and (S2) for one grid face of the run of the last Compute: for tests of the two conditions on their own </summary>
        internal bool EdgePasses(int forLevel, int axis, int i, int j, int k) { level = forLevel; edgeOk = new Dictionary<long, bool>(); return edge(axis, i, j, k); }
        internal bool FacePasses(int forLevel, int axis, int i, int j, int k) { level = forLevel; faceOk = new Dictionary<long, bool>(); return face(axis, i, j, k); }

        // ---- cubes ----

        // the cubes [i, i + si) x ...: zero-free as one box (then its nodes get the box's sign), or split, or tested one by one
        void descend(int i, int j, int k, int si, int sj, int sk)
        {
            if (si <= 0 || sj <= 0 || sk <= 0) return;
            IntervalEnclosure e = enclose(new ExactInterval(X[0][i], X[0][i + si]), new ExactInterval(X[1][j], X[1][j + sj]), new ExactInterval(X[2][k], X[2][k + sk]));
            int s = CertifiedSign(e.F);
            if (s != 0) { zeroFree(i, j, k, si, sj, sk, s); return; }
            if (si == 1 && sj == 1 && sk == 1) { if (skipCube == null || !skipCube(new Vector3i(i, j, k))) testCube(i, j, k); return; }
            int hi = (si + 1) / 2, hj = (sj + 1) / 2, hk = (sk + 1) / 2;
            for (int c = 0; c < 8; ++c)
                descend((c & 1) == 0 ? i : i + hi, (c & 2) == 0 ? j : j + hj, (c & 4) == 0 ? k : k + hk,
                    (c & 1) == 0 ? hi : si - hi, (c & 2) == 0 ? hj : sj - hj, (c & 4) == 0 ? hk : sk - hk);
        }

        // a box of cubes without a zero, of sign s: a cube with a node where the mesher has the other sign is refused (the mesh
        // has a crossing where the level set has none)
        void zeroFree(int i, int j, int k, int si, int sj, int sk, int s)
        {
            for (int c = k; c < k + sk; ++c) for (int b = j; b < j + sj; ++b) for (int a = i; a < i + si; ++a) {
                bool same = true;
                for (int q = 0; q < 8; ++q) if (flagSign(a + (q & 1), b + (q >> 1 & 1), c + (q >> 2 & 1)) != s) same = false;
                if (!same) refuse(a, b, c, SheetCertificateReason.NodeSign);
                else cellState[a + nx * (b + ny * c)] = 0;
            }
        }

        void refuse(int i, int j, int k, SheetCertificateReason reason)
        {
            int c = i + nx * (j + ny * k);
            cellState[c] = 2; cellReason[c] = reason;
        }

        bool outer(int i, int j, int k) { return i == 0 || j == 0 || k == 0 || i == nx - 1 || j == ny - 1 || k == nz - 1; }

        void testCube(int i, int j, int k)
        {
            // zero-free on sub-boxes
            for (int t = 1; t <= MaxTightening; ++t) {
                // a continuous level without a zero in the cube has one sign on it: the sign of any sub-box
                int m = 1 << t, sign = 0; bool free = true;
                for (int a = 0; a < m && free; ++a) for (int b = 0; b < m && free; ++b) for (int c = 0; c < m && free; ++c) {
                    sign = CertifiedSign(enclose(span(0, i, a, m), span(1, j, b, m), span(2, k, c, m)).F);
                    if (sign == 0) free = false;
                }
                if (free) { zeroFree(i, j, k, 1, 1, 1, sign); return; }
            }
            ++SurfaceCells[level];
            if (outer(i, j, k)) { refuse(i, j, k, SheetCertificateReason.OuterLayer); return; }
            SheetCertificateReason reason = testBox(new[] { i, j, k, i + 1, j + 1, k + 1 }, null);
            if (reason == SheetCertificateReason.None) cellState[i + nx * (j + ny * k)] = 1;
            else refuse(i, j, k, reason);
        }

        // ---- a box of cubes (a cube, or a block): (B0) to (B3), at most one closed curve, the mesh in it ----

        // lo x, y, z, hi x, y, z (hi exclusive). sides: if not null, the sides of the box that carry the failing node, edge or face.
        SheetCertificateReason testBox(int[] b, bool[] sides)
        {
            for (int k = b[2]; k <= b[5]; ++k) for (int j = b[1]; j <= b[4]; ++j) for (int i = b[0]; i <= b[3]; ++i) {
                int[] p = { i, j, k };
                if (!onBoundary(b, p, -1)) continue;
                if (!nodeOk(i, j, k)) { mark(b, p, -1, sides); return SheetCertificateReason.NodeSign; }
            }
            for (int axis = 0; axis < 3; ++axis)
                for (int k = b[2]; k <= b[5] - (axis == 2 ? 1 : 0); ++k) for (int j = b[1]; j <= b[4] - (axis == 1 ? 1 : 0); ++j) for (int i = b[0]; i <= b[3] - (axis == 0 ? 1 : 0); ++i) {
                    int[] p = { i, j, k };
                    if (!onBoundary(b, p, axis)) continue;
                    if (!edge(axis, i, j, k)) { mark(b, p, axis, sides); return SheetCertificateReason.Edge; }
                }
            for (int axis = 0; axis < 3; ++axis) {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int side = 0; side < 2; ++side) {
                    int[] p = new int[3];
                    p[axis] = side == 0 ? b[axis] : b[axis + 3];
                    for (p[u] = b[u]; p[u] < b[u + 3]; ++p[u]) for (p[v] = b[v]; p[v] < b[v + 3]; ++p[v])
                        if (!face(axis, p[0], p[1], p[2])) { if (sides != null) sides[axis + 3 * side] = true; return SheetCertificateReason.Face; }
                }
            }
            var list = new List<IntervalEnclosure>();
            for (int k = b[2]; k < b[5]; ++k) for (int j = b[1]; j < b[4]; ++j) for (int i = b[0]; i < b[3]; ++i) list.AddRange(pieces(i, j, k));
            if (list.Count > 0 && !HasDirection(list, -1)) return SheetCertificateReason.Direction;
            int m = Circles(rec, level, b);
            if (m > 1) return SheetCertificateReason.Circles;
            if (!MeshIsDisks(rec, level, b, m)) return SheetCertificateReason.MeshInBlock;
            return SheetCertificateReason.None;
        }

        // is the node p (axis < 0), or the edge from node p along axis, on the boundary of the box?
        static bool onBoundary(int[] b, int[] p, int axis)
        {
            for (int a = 0; a < 3; ++a) if (a != axis && (p[a] == b[a] || p[a] == b[a + 3])) return true;
            return false;
        }

        static void mark(int[] b, int[] p, int axis, bool[] sides)
        {
            if (sides == null) return;
            for (int a = 0; a < 3; ++a) {
                if (a == axis) continue;
                if (p[a] == b[a]) sides[a] = true;
                if (p[a] == b[a + 3]) sides[a + 3] = true;
            }
        }

        // an id of the edge or face of an axis at a node: unique for every grid (a node id is below the mesher's limit of nodes)
        internal static long Key(TwoLevelMarchingTetrahedra.CertificateRecord rec, int axis, int i, int j, int k) { return axis + 4L * (i + (long)rec.Nodes.x * (j + (long)rec.Nodes.y * k)); }

        /// <summary>
        /// d . g > 0 for every gradient of every enclosure: the lower bound of the interval product is positive. A d that is
        /// zero or not finite fails. A lower bound of exactly 0 fails too.
        /// </summary>
        internal static bool OneSign(List<IntervalEnclosure> list, Vector3d d)
        {
            // a zero d fails by the arithmetic below (0 * g is an interval around 0), a d that is not a number too
            if (double.IsInfinity(d.LengthSquared)) return false;
            ExactInterval dx = ExactInterval.Of(d.x), dy = ExactInterval.Of(d.y), dz = ExactInterval.Of(d.z);
            foreach (IntervalEnclosure e in list)
                if (!((dx * e.Gx + dy * e.Gy + dz * e.Gz).Lo > 0)) return false;
            return true;
        }

        // a direction of strict growth for all the enclosures: the mean of their midpoints, or an axis; in the plane with
        // normal `normal` if that is 0, 1 or 2 (the component along it is exactly 0)
        internal static bool HasDirection(List<IntervalEnclosure> list, int normal)
        {
            Vector3d mean = Vector3d.Zero;
            foreach (IntervalEnclosure e in list) mean += new Vector3d(0.5 * (e.Gx.Lo + e.Gx.Hi), 0.5 * (e.Gy.Lo + e.Gy.Hi), 0.5 * (e.Gz.Lo + e.Gz.Hi));
            if (normal >= 0) mean[normal] = 0;
            if (OneSign(list, mean)) return true;
            for (int a = 0; a < 3; ++a) {
                if (a == normal) continue;
                Vector3d d = Vector3d.Zero;
                d[a] = 1; if (OneSign(list, d)) return true;
                d[a] = -1; if (OneSign(list, d)) return true;
            }
            return false;
        }

        // (S1) for the grid edge from node (i, j, k) along axis: on all the pieces that may hold a zero the derivative along the
        // edge has one certified sign. Then the level has one zero at most on the edge: it is strictly monotone on each such
        // piece, all in one direction, and keeps its sign on the pieces between them.
        bool edge(int axis, int i, int j, int k)
        {
            bool r;
            long id = Key(rec, axis, i, j, k);
            if (edgeOk.TryGetValue(id, out r)) return r;
            r = false;
            int[] n = { i, j, k };
            for (int t = 0; t <= MaxTightening && !r; ++t) {
                int m = 1 << t, sign = 0;
                bool ok = true;
                for (int a = 0; a < m && ok; ++a) {
                    ExactInterval[] x = new ExactInterval[3];
                    for (int ax = 0; ax < 3; ++ax) x[ax] = ax == axis ? span(ax, n[ax], a, m) : point(ax, n[ax]);
                    IntervalEnclosure e = enclose(x[0], x[1], x[2]);
                    if (!MayBeZero(e.F)) continue;
                    int s = CertifiedSign(axis == 0 ? e.Gx : axis == 1 ? e.Gy : e.Gz);
                    if (s == 0 || (sign != 0 && s != sign)) ok = false;
                    sign = s;
                }
                r = ok;
            }
            edgeOk[id] = r;
            return r;
        }

        // (S2) for the grid face with normal axis and lowest node (i, j, k): a direction of strict growth in its plane on the
        // pieces that may hold a zero, and, if the mesher's signs alternate around it, no zero on the diagonal that the
        // mesher's split gives the face (the one between its two nodes of even index sum). The diagonal's chain of boxes
        // separates the two other corners; that this gives the pairing of (S2) also needs the certified signs at the face's
        // corners (B0) and one zero at most on each of its edges (S1). testBox has both before it comes here: it tests the
        // nodes, then the edges, then the faces, and returns at the first failure. Not to be called before them.
        bool face(int axis, int i, int j, int k)
        {
            bool r;
            long id = Key(rec, axis, i, j, k);
            if (faceOk.TryGetValue(id, out r)) return r;
            r = false;
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            int[] n = { i, j, k };
            int[] q = { i, j, k };
            int c0 = flagSign(q[0], q[1], q[2]); q[u]++; int c1 = flagSign(q[0], q[1], q[2]); q[v]++; int c2 = flagSign(q[0], q[1], q[2]); q[u]--; int c3 = flagSign(q[0], q[1], q[2]);
            bool alternating = c0 == c2 && c1 == c3 && c0 != c1;
            bool lowIsEven = ((i + j + k) & 1) == 0;
            for (int t = 0; t <= MaxTightening && !r; ++t) {
                int m = 1 << t;
                var list = new List<IntervalEnclosure>();
                for (int a = 0; a < m; ++a) for (int c = 0; c < m; ++c) {
                    ExactInterval[] x = new ExactInterval[3];
                    x[axis] = point(axis, n[axis]); x[u] = span(u, n[u], a, m); x[v] = span(v, n[v], c, m);
                    IntervalEnclosure e = enclose(x[0], x[1], x[2]);
                    if (MayBeZero(e.F)) list.Add(e);
                }
                bool ok = list.Count == 0 || HasDirection(list, axis);
                if (ok && alternating) {
                    int pieces = 8 * m;
                    for (int p = 0; p < pieces && ok; ++p) {
                        ExactInterval[] x = new ExactInterval[3];
                        x[axis] = point(axis, n[axis]);
                        x[u] = span(u, n[u], p, pieces);
                        x[v] = span(v, n[v], lowIsEven ? p : pieces - 1 - p, pieces);
                        if (MayBeZero(enclose(x[0], x[1], x[2]).F)) ok = false;
                    }
                }
                r = ok;
            }
            faceOk[id] = r;
            return r;
        }

        // the enclosures over the 2^MaxTightening sub-boxes per axis of a cube that may hold a zero; kept for the boxes that take
        // the cube in later, within a budget
        List<IntervalEnclosure> pieces(int i, int j, int k)
        {
            List<IntervalEnclosure> list;
            int c = i + nx * (j + ny * k);
            if (cellPieces.TryGetValue(c, out list)) return list;
            // the budget: all kept cubes go when it is reached (a box in test has its enclosures in its own list already)
            long cap = Math.Max((long)MaxBlockSide * MaxBlockSide * MaxBlockSide, (long)MaxCachedEnclosures >> (3 * MaxTightening));
            if (cellPieces.Count >= cap) cellPieces.Clear();
            list = new List<IntervalEnclosure>();
            int m = 1 << MaxTightening;
            for (int a = 0; a < m; ++a) for (int b = 0; b < m; ++b) for (int d = 0; d < m; ++d) {
                IntervalEnclosure e = enclose(span(0, i, a, m), span(1, j, b, m), span(2, k, d, m));
                if (MayBeZero(e.F)) list.Add(e);
            }
            cellPieces[c] = list;
            if (cellPieces.Count > PeakCachedCubes) PeakCachedCubes = cellPieces.Count;
            return list;
        }

        // ---- counts on the mesher's record (no geometry) ----

        static int flag(TwoLevelMarchingTetrahedra.CertificateRecord rec, int level, int i, int j, int k)
        {
            return rec.NodeFlags[i + rec.Nodes.x * (j + rec.Nodes.y * k)] >> level & 1;
        }

        /// <summary>
        /// The closed curves of a level's pattern on the boundary of a box of cubes: the classes of the crossed cube edges on
        /// it, joined across every boundary face as the mesh joins them (two crossed edges: joined; four: the two edges at each
        /// of the two nodes that are not on the face's diagonal, which joins its nodes of even index sum).
        /// </summary>
        internal static int Circles(TwoLevelMarchingTetrahedra.CertificateRecord rec, int level, int[] b)
        {
            var parent = new Dictionary<long, long>();
            Func<long, long> find = null;
            find = a => { long p; if (!parent.TryGetValue(a, out p)) { parent[a] = a; return a; } return p == a ? a : (parent[a] = find(p)); };
            for (int axis = 0; axis < 3; ++axis) {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int side = 0; side < 2; ++side) {
                    int[] p = new int[3];
                    p[axis] = side == 0 ? b[axis] : b[axis + 3];
                    for (p[u] = b[u]; p[u] < b[u + 3]; ++p[u]) for (p[v] = b[v]; p[v] < b[v + 3]; ++p[v]) {
                        int[] q = (int[])p.Clone();
                        int[] c = new int[4]; long[] e = new long[4];
                        // corners in cyclic order 0 (lowest), 1 (+u), 2 (+u +v), 3 (+v); edge a joins the corners a and a + 1
                        c[0] = flag(rec, level, q[0], q[1], q[2]); e[0] = Key(rec, u, q[0], q[1], q[2]); e[3] = Key(rec, v, q[0], q[1], q[2]);
                        q[u]++; c[1] = flag(rec, level, q[0], q[1], q[2]); e[1] = Key(rec, v, q[0], q[1], q[2]);
                        q[v]++; c[2] = flag(rec, level, q[0], q[1], q[2]);
                        q[u]--; c[3] = flag(rec, level, q[0], q[1], q[2]); e[2] = Key(rec, u, q[0], q[1], q[2]);
                        int crossed = 0, first = -1, second = -1;
                        for (int a = 0; a < 4; ++a) if (c[a] != c[(a + 1) % 4]) { ++crossed; if (first < 0) first = a; else second = a; }
                        if (crossed == 2) parent[find(e[first])] = find(e[second]);
                        else if (crossed == 4) {
                            // corner a has the edges a - 1 and a; the corners off the diagonal are 1 and 3 if the lowest node is even
                            int off = ((p[0] + p[1] + p[2]) & 1) == 0 ? 1 : 0;
                            parent[find(e[(off + 3) % 4])] = find(e[off]);
                            parent[find(e[(off + 1) % 4])] = find(e[(off + 2) % 4]);
                        }
                    }
                }
            }
            var roots = new HashSet<long>();
            foreach (long a in new List<long>(parent.Keys)) roots.Add(find(a));
            return roots.Count;
        }

        static readonly int[][] evenTets = { new[] { 0, 3, 5, 6 }, new[] { 1, 0, 3, 5 }, new[] { 2, 0, 3, 6 }, new[] { 4, 0, 5, 6 }, new[] { 7, 3, 5, 6 } };

        /// <summary>
        /// Is the level's mesh in a box of cubes a set of `circles` disks (0 or 1)? By Lemma 9 the mesh in the box is the cell
        /// complex of the five tetrahedra of each cube: one polygon per tetrahedron with both signs, one segment per
        /// tetrahedron face with both signs, one vertex per crossed tetrahedron edge. For 0: no polygon. For 1: one connected
        /// component of Euler characteristic 1 (Lemma 6.2 of the contouring note).
        /// </summary>
        internal static bool MeshIsDisks(TwoLevelMarchingTetrahedra.CertificateRecord rec, int level, int[] b, int circles)
        {
            var vertices = new HashSet<long>(); var segments = new Dictionary<ValueTuple<int, int, int>, int>();
            var parent = new List<int>();
            Func<int, int> find = null;
            find = a => parent[a] == a ? a : (parent[a] = find(parent[a]));
            long nodes = (long)rec.NodeFlags.Length;
            int polygons = 0;
            int[] id = new int[4], sign = new int[4];
            for (int k = b[2]; k < b[5]; ++k) for (int j = b[1]; j < b[4]; ++j) for (int i = b[0]; i < b[3]; ++i) {
                bool odd = ((i + j + k) & 1) != 0;
                foreach (int[] tet in evenTets) {
                    int inside = 0;
                    for (int q = 0; q < 4; ++q) {
                        int c = odd ? tet[q] ^ 1 : tet[q];        // the split of an odd cube is that of an even one mirrored in x
                        int a = i + (c & 1), d = j + (c >> 1 & 1), e = k + (c >> 2 & 1);
                        id[q] = a + rec.Nodes.x * (d + rec.Nodes.y * e);
                        sign[q] = rec.NodeFlags[id[q]] >> level & 1;
                        inside += sign[q];
                    }
                    if (inside == 0 || inside == 4) continue;
                    int polygon = polygons++;
                    parent.Add(polygon);
                    for (int x = 0; x < 4; ++x) for (int y = x + 1; y < 4; ++y)
                        if (sign[x] != sign[y]) vertices.Add(Math.Min(id[x], id[y]) * nodes + Math.Max(id[x], id[y]));
                    for (int skip = 0; skip < 4; ++skip) {
                        // the face without the node `skip`
                        int f0 = id[skip == 0 ? 1 : 0], f1 = id[skip <= 1 ? 2 : 1], f2 = id[skip == 3 ? 2 : 3];
                        int s0 = sign[skip == 0 ? 1 : 0], s1 = sign[skip <= 1 ? 2 : 1], s2 = sign[skip == 3 ? 2 : 3];
                        if (s0 == s1 && s1 == s2) continue;
                        int lo = Math.Min(f0, Math.Min(f1, f2)), hi = Math.Max(f0, Math.Max(f1, f2)), mid = f0 + f1 + f2 - lo - hi;
                        var faceKey = new ValueTuple<int, int, int>(lo, mid, hi);
                        int other;
                        if (segments.TryGetValue(faceKey, out other)) parent[find(polygon)] = find(other);
                        else segments[faceKey] = polygon;
                    }
                }
            }
            if (circles == 0) return polygons == 0;
            int components = 0;
            for (int p = 0; p < polygons; ++p) if (find(p) == p) ++components;
            return components == 1 && vertices.Count - segments.Count + polygons == 1;
        }

        // ---- blocks ----

        // every refused cube is the seed of a box that grows on the sides that carry a failing element, and takes in the blocks
        // it overlaps, until it passes or exceeds the limit or reaches the outermost layer
        void growBlocks()
        {
            for (int k = 0; k < nz; ++k) for (int j = 0; j < ny; ++j) for (int i = 0; i < nx; ++i) {
                int c = i + nx * (j + ny * k);
                if (cellState[c] != 2 || cellReason[c] == SheetCertificateReason.OuterLayer) continue;
                SheetCertificateReason left = grow(new[] { i, j, k, i + 1, j + 1, k + 1 });
                if (left != SheetCertificateReason.None && cellState[c] == 2) cellReason[c] = left;
            }
        }

        SheetCertificateReason grow(int[] b)
        {
            var absorbed = new List<int>();
            SheetCertificateReason last = SheetCertificateReason.BlockLimit;
            for (int step = 0; step < 4 * MaxBlockSide + 8; ++step) {
                bool changed = true;
                while (changed) {
                    changed = false;
                    for (int k = b[2]; k < b[5]; ++k) for (int j = b[1]; j < b[4]; ++j) for (int i = b[0]; i < b[3]; ++i) {
                        int o = owner[i + nx * (j + ny * k)];
                        if (o < 0 || absorbed.Contains(o)) continue;
                        absorbed.Add(o);
                        int[] other = boxes[o];
                        for (int a = 0; a < 3; ++a) { b[a] = Math.Min(b[a], other[a]); b[a + 3] = Math.Max(b[a + 3], other[a + 3]); }
                        changed = true;
                    }
                }
                int[] cubes = { nx, ny, nz };
                for (int a = 0; a < 3; ++a)
                    if (b[a] < 1 || b[a + 3] > cubes[a] - 1 || b[a + 3] - b[a] > MaxBlockSide) return SheetCertificateReason.BlockLimit;
                bool[] sides = new bool[6];
                SheetCertificateReason fail = testBox(b, sides);
                if (fail == SheetCertificateReason.None) {
                    foreach (int o in absorbed) boxes[o] = null;
                    int id = boxes.Count; boxes.Add((int[])b.Clone());
                    for (int k = b[2]; k < b[5]; ++k) for (int j = b[1]; j < b[4]; ++j) for (int i = b[0]; i < b[3]; ++i) {
                        int c = i + nx * (j + ny * k);
                        owner[c] = id; cellState[c] = 3;
                    }
                    return SheetCertificateReason.None;
                }
                last = fail;
                bool any = false;
                for (int a = 0; a < 3; ++a) { if (sides[a]) { --b[a]; any = true; } if (sides[a + 3]) { ++b[a + 3]; any = true; } }
                if (!any) return fail;         // no side to grow on: the direction, the curves or the mesh in the block
            }
            return last;
        }
    }
}
