using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace g3
{
    /// <summary> why a cell is not certified by ContouringSmoothCertificate </summary>
    internal enum SmoothCertificateReason
    {
        None = 0,
        /// <summary> the cell is not proven zero-free and the gradient test did not pass at the tightening limit </summary>
        GradientTest = 1,
        /// <summary> the cell may hold a zero and a point where the field is not smooth </summary>
        NotSmooth = 2,
        /// <summary> the sign of the field at a node of the cell is not certified, or is not the sign the contouring used </summary>
        NodeSign = 3,
        /// <summary> the corner signs are not one of the nine configurations the published argument admits </summary>
        Configuration = 4,
        /// <summary> the contour around the cell is not what Theorem 2 of the design note proves (DualContouring's record) </summary>
        MeshSide = 5,
        /// <summary> the cell is in the outermost layer of the grid and is not proven zero-free </summary>
        OuterLayer = 6
    }

    /// <summary> why the whole run is not certified, whatever the cells say (flags) </summary>
    [Flags]
    internal enum SmoothCertificateRunFailure
    {
        None = 0,
        /// <summary> the contour was not generated with RecordCertificateData, or has no grid </summary>
        NoRecord = 1,
        /// <summary> the grid is refined (two cell sizes): the published statement used here is for one cell size </summary>
        Refined = 2,
        /// <summary> the node spacings are not equal within the tolerance: the cells are not cubes </summary>
        CellShape = 4,
        /// <summary> the contour fails the mesh side as a whole (a vertex outside its cell, no area, left out, self-intersections) </summary>
        MeshSide = 8,
        /// <summary> a cell of the outermost layer may hold a zero: the level set may leave the grid </summary>
        OuterLayer = 16
    }

    internal struct SmoothUncertifiedCell
    {
        public Vector3i Cell;
        public SmoothCertificateReason Reason;
    }

    /// <summary>
    /// A topology certificate for the DualContouring mesh of a smooth implicit field on a uniform grid: if Certified, the
    /// level set {f = iso} is a closed surface inside the grid and the mesh is isotopic to it. Internal, report only: it
    /// changes nothing in the contour.
    ///
    /// The proof is a composition (docs/design/CONTOURING_TOPOLOGY.md, section 7.13): Plantinga and Vegter's statement for
    /// a regular grid of cubes (every cell is zero-free, or an axis-aligned box that encloses the gradients over the cell has
    /// a positive interval inner product with itself) gives level set ~ a marching-cubes-type mesh on the grid, with either
    /// pairing on a face with alternating corner signs; Theorems 1 and 2 of the design note give that mesh ~ this contour.
    /// The published statement is the premise; it is not re-proved here.
    ///
    /// What is tested, per cell (X), per node (N) and per run (R). Every test is the sign of a bound of an
    /// outward-rounded interval, or an exact comparison of doubles; a test that cannot decide refuses.
    ///   (X) zero-free: the enclosure of f - iso over the cell, or over each of its 2^t sub-boxes per axis, excludes 0;
    ///       else the gradient test in its published form: f smooth on the cell, and the hull G of the gradient enclosures
    ///       of the sub-boxes (an enclosure of the gradient over the cell) has lower bound of Gx*Gx + Gy*Gy + Gz*Gz above 0,
    ///       where each product is of two independent copies (an interval through 0 contributes lo * hi, not 0);
    ///   (X) the eight corner signs form one of the nine admitted configurations (a guard: the published argument excludes
    ///       the other five under the gradient test, by an argument this code does not rely on);
    ///   (N) the sign of f - iso at every node of a cell that is not zero-free is certified by a point enclosure, and at
    ///       every node of a zero-free box it is the box's sign; both must equal the inside flag the contouring used;
    ///   (R) one cell size; node spacings equal within CellShapeTolerance (cubes); no possible zero in the outermost layer;
    ///       the mesh side of Theorem 2 as recorded by DualContouring.
    /// Never certified: a cell on a crease of a min / max, a cell with two sheets, a node on the level set, a singular
    /// point of f, a surface that leaves the grid.
    ///
    /// Deterministic: the parallel path works on blocks of cells and merges their results in block order, so it equals
    /// the serial path.
    /// </summary>
    internal sealed class ContouringSmoothCertificate
    {
        /// <summary> the field, the same function the contour's Implicit evaluates (only the node signs are compared) </summary>
        public IntervalImplicit3d Field;
        /// <summary> the contour, after Generate() with RecordCertificateData </summary>
        public DualContouring Contour;
        /// <summary> a cell that fails as one box is cut into 2^t sub-boxes per axis, t up to this, to tighten the enclosures </summary>
        public int MaxTightening = 2;
        /// <summary> the largest node spacing may exceed the smallest by this factor minus one (cubes up to rounding) </summary>
        public double CellShapeTolerance = 1.0 / 1048576;
        public bool ParallelCompute = true;

        public bool Certified;
        public SmoothCertificateRunFailure RunFailure;
        /// <summary> the cells that are not certified, sorted by (z, y, x) </summary>
        public List<SmoothUncertifiedCell> UncertifiedCells = new List<SmoothUncertifiedCell>();
        /// <summary> cells not proven zero-free, and those of them that passed the gradient test at t = 0, 1, 2, ... </summary>
        public int SurfaceCells;
        public int[] PassedAtTightening = new int[0];

        const int Block = 8;
        int nx, ny, nz;                 // cells per axis
        double[] X, Y, Z;               // node coordinates, as the contouring computes them
        bool[] inside;                  // the contouring's flag per node
        sbyte[] nodeSign;               // the certified sign of f - iso per node: 0 not computed, +1, -1, 2 not decided
        double iso;
        Vector3d origin; double h;

        sealed class BlockResult
        {
            public List<SmoothUncertifiedCell> Cells = new List<SmoothUncertifiedCell>();
            public int Surface;
            public int[] Passed;
            public bool Outer;
        }

        public void Compute()
        {
            Certified = false; RunFailure = SmoothCertificateRunFailure.None;
            UncertifiedCells = new List<SmoothUncertifiedCell>(); SurfaceCells = 0;
            PassedAtTightening = new int[Math.Max(0, MaxTightening) + 1];
            if (Field == null) throw new ArgumentNullException(nameof(Field));
            if (Contour == null) throw new ArgumentNullException(nameof(Contour));
            if (MaxTightening < 0 || MaxTightening > 4) throw new ArgumentOutOfRangeException(nameof(MaxTightening));

            Vector3i dims = Contour.CornerDimensions;
            if (!Contour.RecordCertificateData || Contour.Mesh == null || dims.x < 2 || dims.y < 2 || dims.z < 2) {
                RunFailure = SmoothCertificateRunFailure.NoRecord; return;
            }
            CertificateGrid grid = Contour.ContourGrid();
            if (grid.BinSize != 1) { RunFailure = SmoothCertificateRunFailure.Refined; return; }
            nx = dims.x - 1; ny = dims.y - 1; nz = dims.z - 1;
            iso = Contour.IsoValue; origin = Contour.Bounds.Min; h = Contour.CellSize;
            X = new double[nx + 1]; Y = new double[ny + 1]; Z = new double[nz + 1];
            for (int i = 0; i <= nx; ++i) X[i] = grid.Coord(0, i);
            for (int i = 0; i <= ny; ++i) Y[i] = grid.Coord(1, i);
            for (int i = 0; i <= nz; ++i) Z[i] = grid.Coord(2, i);
            if (!EqualSpacing(X, Y, Z, CellShapeTolerance)) RunFailure |= SmoothCertificateRunFailure.CellShape;
            if (Contour.CertificateMeshFails) RunFailure |= SmoothCertificateRunFailure.MeshSide;

            inside = new bool[(nx + 1) * (ny + 1) * (nz + 1)];
            nodeSign = new sbyte[inside.Length];
            for (int k = 0; k <= nz; ++k)
                for (int j = 0; j <= ny; ++j)
                    for (int i = 0; i <= nx; ++i)
                        inside[i + (nx + 1) * (j + (ny + 1) * k)] = grid.NodeInside(new Vector3i(i, j, k));

            // blocks of Block^3 cells, each with its own result; merged in block order
            int bx = (nx + Block - 1) / Block, by = (ny + Block - 1) / Block, bz = (nz + Block - 1) / Block;
            var results = new BlockResult[bx * by * bz];
            Action<int> work = b => {
                var r = new BlockResult { Passed = new int[MaxTightening + 1] };
                int i0 = (b % bx) * Block, j0 = (b / bx % by) * Block, k0 = (b / (bx * by)) * Block;
                descend(i0, j0, k0, Block, r);
                results[b] = r;
            };
            if (ParallelCompute) Parallel.For(0, results.Length, work);
            else for (int b = 0; b < results.Length; ++b) work(b);

            bool outer = false;
            foreach (BlockResult r in results) {
                UncertifiedCells.AddRange(r.Cells);
                SurfaceCells += r.Surface;
                for (int t = 0; t <= MaxTightening; ++t) PassedAtTightening[t] += r.Passed[t];
                outer |= r.Outer;
            }
            if (outer) RunFailure |= SmoothCertificateRunFailure.OuterLayer;
            // the mesh side per cell: a cell DualContouring refuses is listed unless it is listed already
            if (Contour.CertificateMeshCells.Count > 0) {
                var listed = new HashSet<Vector3i>();
                foreach (SmoothUncertifiedCell c in UncertifiedCells) listed.Add(c.Cell);
                foreach (KeyValuePair<Vector3i, int> c in Contour.CertificateMeshCells)
                    if (listed.Add(c.Key)) UncertifiedCells.Add(new SmoothUncertifiedCell { Cell = c.Key, Reason = SmoothCertificateReason.MeshSide });
            }
            UncertifiedCells.Sort((a, b) => a.Cell.z != b.Cell.z ? a.Cell.z.CompareTo(b.Cell.z) : a.Cell.y != b.Cell.y ? a.Cell.y.CompareTo(b.Cell.y) : a.Cell.x.CompareTo(b.Cell.x));
            Certified = RunFailure == SmoothCertificateRunFailure.None && UncertifiedCells.Count == 0;
        }

        /// <summary>
        /// Are all node spacings of the three axes equal within the factor 1 + tolerance (max at most min (1 + tolerance), min positive)?
        /// Exact: the spacings are differences of neighbouring doubles (exact by Sterbenz's lemma for a cell size far above
        /// the rounding unit; a difference that is not exact is of spacings that differ grossly, which fails either way), and
        /// the comparison is evaluated with the product rounded down by one unit.
        /// </summary>
        internal static bool EqualSpacing(double[] x, double[] y, double[] z, double tolerance)
        {
            double min = double.MaxValue, max = 0;
            foreach (double[] a in new[] { x, y, z })
                for (int i = 0; i + 1 < a.Length; ++i) {
                    double d = a[i + 1] - a[i];
                    if (!(d > 0) || double.IsInfinity(d)) return false;
                    min = Math.Min(min, d); max = Math.Max(max, d);
                }
            if (!(max > 0)) return false;
            return max <= ExactInterval.Down(min * (1 + tolerance));
        }

        /// <summary>
        /// Is the corner-sign configuration (bit c set = corner c inside, c = x + 2 y + 4 z) one of the nine that the
        /// published argument admits? Of the 14 configurations up to rotation, mirroring and exchange of the signs, five
        /// are excluded: two corners of one sign on a body diagonal, and the four with more than one face of alternating
        /// signs. So: at most one face with alternating signs, and the minority sign is not exactly a body diagonal.
        /// </summary>
        internal static bool AdmittedConfiguration(int insideMask)
        {
            int ambiguous = 0;
            for (int f = 0; f < 6; ++f) {
                bool a = (insideMask >> faceCorners[f, 0] & 1) != 0, b = (insideMask >> faceCorners[f, 1] & 1) != 0;
                bool c = (insideMask >> faceCorners[f, 2] & 1) != 0, d = (insideMask >> faceCorners[f, 3] & 1) != 0;
                if (a == c && b == d && a != b) ++ambiguous;
            }
            if (ambiguous > 1) return false;
            int count = 0;
            for (int c = 0; c < 8; ++c) count += insideMask >> c & 1;
            int minority = count <= 4 ? insideMask & 255 : ~insideMask & 255;
            // a body diagonal: corners c and 7 - c
            return !(minority == 0x81 || minority == 0x42 || minority == 0x24 || minority == 0x18);
        }

        // the four corners of each face in cyclic order (faces x = 0, x = 1, y = 0, y = 1, z = 0, z = 1)
        static readonly int[,] faceCorners = { { 0, 2, 6, 4 }, { 1, 3, 7, 5 }, { 0, 1, 5, 4 }, { 2, 3, 7, 6 }, { 0, 1, 3, 2 }, { 4, 5, 7, 6 } };

        // node coordinate j of axis a on the grid refined t times; for j a multiple of 2^t this is the grid's own double,
        // because (h / 2^t) * (i * 2^t) = h * i exactly and the sum is the one the contouring computes
        double coord(int axis, long j, int t) { return origin[axis] + (h / (1 << t)) * j; }

        ExactInterval span(int axis, int i, int cells)
        {
            double[] a = axis == 0 ? X : axis == 1 ? Y : Z;
            return new ExactInterval(a[i], a[i + cells]);
        }

        IntervalEnclosure enclose(ExactInterval x, ExactInterval y, ExactInterval z)
        {
            IntervalEnclosure e = Field.Enclose(x, y, z);
            e.F = e.F - ExactInterval.Of(iso);
            return e;
        }

        /// <summary> +1 / -1: the sign of every value of the interval; 0: not decided (the interval holds 0, or a bound is 0 or NaN) </summary>
        internal static int CertifiedSign(ExactInterval f) { return f.Lo > 0 ? 1 : f.Hi < 0 ? -1 : 0; }

        // The certified sign of f - iso at a node by a point enclosure (0: not decided), computed once per node. Two threads
        // may compute the same node; both store the same value (the enclosure is a function of the node), so the result
        // does not depend on the schedule.
        int pointSign(int i, int j, int k)
        {
            int idx = i + (nx + 1) * (j + (ny + 1) * k);
            sbyte v = nodeSign[idx];
            if (v == 0) {
                int s = CertifiedSign(enclose(ExactInterval.Of(X[i]), ExactInterval.Of(Y[j]), ExactInterval.Of(Z[k])).F);
                v = (sbyte)(s == 0 ? 2 : s);
                nodeSign[idx] = v;
            }
            return v == 2 ? 0 : v;
        }

        // the contouring's inside flag as a sign of f - iso
        int flagSign(int i, int j, int k)
        {
            bool isInside = inside[i + (nx + 1) * (j + (ny + 1) * k)];
            return isInside == Contour.InsideIsLess ? -1 : 1;
        }

        void refuse(BlockResult r, int i, int j, int k, SmoothCertificateReason reason)
        {
            r.Cells.Add(new SmoothUncertifiedCell { Cell = new Vector3i(i, j, k), Reason = reason });
        }

        // a box of cells [i, i + size) x ..., clipped to the grid: proven zero-free as a whole, or split down to cells
        void descend(int i, int j, int k, int size, BlockResult r)
        {
            if (i >= nx || j >= ny || k >= nz) return;
            int si = Math.Min(size, nx - i), sj = Math.Min(size, ny - j), sk = Math.Min(size, nz - k);
            IntervalEnclosure e = enclose(span(0, i, si), span(1, j, sj), span(2, k, sk));
            int s = CertifiedSign(e.F);
            if (s != 0) {
                // zero-free: every node of the box has this sign, and the contouring must have used it
                for (int c = k; c <= k + sk; ++c)
                    for (int b = j; b <= j + sj; ++b)
                        for (int a = i; a <= i + si; ++a)
                            if (flagSign(a, b, c) != s) { refuseAround(r, a, b, c, i, j, k, si, sj, sk); }
                return;
            }
            if (size > 1) {
                int half = size / 2;
                for (int c = 0; c < 8; ++c)
                    descend(i + ((c & 1) != 0 ? half : 0), j + ((c & 2) != 0 ? half : 0), k + ((c & 4) != 0 ? half : 0), half, r);
                return;
            }
            cell(i, j, k, e, r);
        }

        // a node of a zero-free box whose flag is not the box's sign: the cells of the box at that node are refused
        void refuseAround(BlockResult r, int a, int b, int c, int i, int j, int k, int si, int sj, int sk)
        {
            for (int dc = -1; dc <= 0; ++dc)
                for (int db = -1; db <= 0; ++db)
                    for (int da = -1; da <= 0; ++da) {
                        int ci = a + da, cj = b + db, ck = c + dc;
                        if (ci < i || cj < j || ck < k || ci >= i + si || cj >= j + sj || ck >= k + sk) continue;
                        bool listed = false;
                        foreach (SmoothUncertifiedCell u in r.Cells) if (u.Cell.x == ci && u.Cell.y == cj && u.Cell.z == ck) { listed = true; break; }
                        if (!listed) refuse(r, ci, cj, ck, SmoothCertificateReason.NodeSign);
                    }
        }

        void cell(int i, int j, int k, IntervalEnclosure whole, BlockResult r)
        {
            // tighten: zero-free on all sub-boxes, or the gradient test on the hull of their gradient enclosures
            int passed = -1; bool zeroFree = false, smooth = true; int boxSign = 0;
            for (int t = 0; t <= MaxTightening && passed < 0 && !zeroFree; ++t) {
                int m = 1 << t;
                bool allFree = true; smooth = true; boxSign = 0;
                ExactInterval gx = default(ExactInterval), gy = default(ExactInterval), gz = default(ExactInterval);
                bool first = true;
                for (int c = 0; c < m; ++c)
                    for (int b = 0; b < m; ++b)
                        for (int a = 0; a < m; ++a) {
                            IntervalEnclosure e = t == 0 ? whole : enclose(
                                new ExactInterval(coord(0, (long)i * m + a, t), coord(0, (long)i * m + a + 1, t)),
                                new ExactInterval(coord(1, (long)j * m + b, t), coord(1, (long)j * m + b + 1, t)),
                                new ExactInterval(coord(2, (long)k * m + c, t), coord(2, (long)k * m + c + 1, t)));
                            int s = CertifiedSign(e.F);
                            if (s == 0) allFree = false;
                            else if (boxSign == 0) boxSign = s;
                            else if (boxSign != s) allFree = false;        // cannot happen for a continuous f; refuses if it does
                            if (!e.Smooth) smooth = false;
                            if (first) { gx = e.Gx; gy = e.Gy; gz = e.Gz; first = false; }
                            else { gx = IntervalMath.Hull(gx, e.Gx); gy = IntervalMath.Hull(gy, e.Gy); gz = IntervalMath.Hull(gz, e.Gz); }
                        }
                if (allFree) { zeroFree = true; break; }
                if (smooth && GradientTest(gx, gy, gz)) passed = t;
            }

            // the corner signs: certified by point enclosures and equal to the contouring's flags
            int mask = 0; bool nodesOk = true;
            for (int c = 0; c < 8; ++c) {
                int a = i + (c & 1), b = j + (c >> 1 & 1), d = k + (c >> 2 & 1);
                int s = zeroFree ? boxSign : pointSign(a, b, d);
                if (s == 0 || s != flagSign(a, b, d)) nodesOk = false;
                if (inside[a + (nx + 1) * (b + (ny + 1) * d)]) mask |= 1 << c;
            }
            if (zeroFree) {
                if (!nodesOk) refuse(r, i, j, k, SmoothCertificateReason.NodeSign);
                return;
            }
            ++r.Surface;
            if (i == 0 || j == 0 || k == 0 || i == nx - 1 || j == ny - 1 || k == nz - 1) {
                r.Outer = true;
                refuse(r, i, j, k, SmoothCertificateReason.OuterLayer);
                return;
            }
            if (!nodesOk) { refuse(r, i, j, k, SmoothCertificateReason.NodeSign); return; }
            if (passed < 0) { refuse(r, i, j, k, smooth ? SmoothCertificateReason.GradientTest : SmoothCertificateReason.NotSmooth); return; }
            if (!AdmittedConfiguration(mask)) { refuse(r, i, j, k, SmoothCertificateReason.Configuration); return; }
            ++r.Passed[passed];
        }

        /// <summary>
        /// The published gradient test on one box of gradient enclosures: the lower bound of the interval inner product of
        /// the box with itself is positive. Each product is of two independent copies of the interval (ExactInterval's
        /// product, not its square), so a component through 0 contributes lo * hi &lt; 0. A pass proves that any two
        /// gradients in the box have a positive inner product and that one component keeps its sign.
        /// </summary>
        internal static bool GradientTest(ExactInterval gx, ExactInterval gy, ExactInterval gz)
        {
            ExactInterval p = gx * gx + gy * gy + gz * gz;
            return p.Lo > 0;
        }
    }
}
