// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Dual Contouring (Ju et al. 2002) of an implicit function on a uniform grid.
    /// One vertex per cell, placed by minimizing a QEF built from the surface points and normals
    /// ("Hermite data") on the cell's sign-changing edges, which reproduces sharp edges and corners.
    /// Produces far fewer triangles than MarchingCubes at the same feature quality.
    ///
    /// Hermite data comes from HermiteF if it is set (eg project the edge crossing onto an input mesh),
    /// otherwise from linear interpolation of the field and its central-difference gradient.
    ///
    /// Each cell gets one vertex per surface patch (the patches marching cubes would create in it), as in
    /// manifold dual contouring (Schaefer et al. 2007), so thin walls and gaps do not create non-manifold edges.
    ///
    /// Sparse mode: if CandidateCells is set, only those cells (and the cells around their sign-changing
    /// edges) are contoured, and grid node values come from NodeValueF instead of sampling Implicit on the
    /// whole grid. Cell (i,j,k) spans Bounds.Min + CellSize * [i,i+1] x [j,j+1] x [k,k+1].
    ///
    /// Local refinement (sparse mode, see SetRefinement): a set of cells is replaced by cells RefineFactor
    /// times smaller. Where refined cells meet unrefined ones, the fine grid nodes on the shared faces and
    /// edges get signs topologically equivalent to the coarse face (see edge_decision / face_decision): the
    /// real field signs if they are, otherwise imposed ones. Each fine sign change on such a face then
    /// belongs to one surface patch of the coarse cell, and the polygons of fine edges on the faces of
    /// coarse cells (triangles for edges inside a coarse face) connect to that patch's vertex, as in octree
    /// dual contouring. The result stays closed and manifold.
    ///
    /// Notes:
    ///   - inside is where the field is greater than IsoValue (eg winding number); set InsideIsLess for SDFs
    ///   - quads that would still create a non-manifold edge (an ambiguous face where both cells have a
    ///     single patch) are left out and counted in NonManifoldTriangles; the result then has small holes
    /// </summary>
    public class DualContouring
    {
        public ImplicitFunction3d Implicit;
        public double IsoValue = 0;
        public bool InsideIsLess = false;

        /// <summary> region to contour, should be large enough that the surface does not touch its border </summary>
        public AxisAlignedBox3d Bounds;
        public double CellSize = 0.1;

        /// <summary>
        /// Optional Hermite data for a sign-changing grid edge from edgeA to edgeB (edgeA is inside), with
        /// the interpolated crossing point. Return true and the surface point and normal to use, or false
        /// to fall back to the interpolated point and field gradient.
        /// Must be thread-safe if ParallelCompute is true.
        /// </summary>
        public delegate bool HermiteFunc(Vector3d edgeA, Vector3d edgeB, Vector3d crossing, out Vector3d point, out Vector3d normal);
        public HermiteFunc HermiteF = null;

        /// <summary> QEF eigenvalues below this fraction of the largest are ignored (feature sensitivity) </summary>
        public double QEFEigenTolerance = 0.1;
        /// <summary> vertices stay this far (relative to the cell size) inside their cell </summary>
        public double QEFCellMargin = 0.01;

        /// <summary> sparse mode: cells to contour, in grid cell coordinates (see class comment) </summary>
        public IEnumerable<Vector3i> CandidateCells = null;
        /// <summary> sparse mode: field value at grid node (i,j,k). Called once per node. Must be thread-safe if ParallelCompute. </summary>
        public Func<Vector3i, double> NodeValueF = null;

        /// <summary>
        /// Sparse mode, optional: called for grid edges whose ends are both inside; return true if the surface
        /// leaves and re-enters along the edge (a gap narrower than a cell, invisible to the node signs).
        /// The cells there merge both sides of the gap, which can make the surfaces intersect.
        /// Counted in HiddenGapEdges; refining the grid until there are none avoids this.
        /// Called at most once per grid edge (results are kept across FindCrossings calls).
        /// </summary>
        public Func<Vector3d, Vector3d, bool> HiddenGapF = null;
        /// <summary>
        /// Sparse mode, optional: the same for grid edges whose ends are both outside: true if the edge
        /// crosses material thinner than a cell (a thin wall, lost or broken up by the contour). Counted in
        /// HiddenWallEdges.
        /// </summary>
        public Func<Vector3d, Vector3d, bool> HiddenWallF = null;

        public bool ParallelCompute = true;
        public Func<bool> CancelF = () => { return false; };

        // outputs
        public DMesh3 Mesh;
        public int NonManifoldTriangles;
        public int HiddenGapEdges;
        public int HiddenWallEdges;
        /// <summary> the hidden gap / wall edges of the last FindCrossings (end points as passed to HiddenGapF / HiddenWallF) </summary>
        public List<HiddenEdge> HiddenEdges = new List<HiddenEdge>();
        public Vector3i CornerDimensions;
        /// <summary> time per phase of Generate (ms): sample, crossings, gaps, hermite, vertices, intersections, mesh </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();

        /// <summary> local refinement diagnostics: interface edges / faces whose fine node signs were imposed </summary>
        public int ImposedEdges, ImposedFaces;
        /// <summary>
        /// local refinement: the unrefined cells at imposed edges / faces (grid cell coordinates). The coarse
        /// grid misses something of the field there (eg a gap or wall continues), refining them as well avoids
        /// the imposed signs.
        /// </summary>
        public List<Vector3i> ImposedCells = new List<Vector3i>();
        /// <summary>
        /// unrefined cells (grid cell coordinates) around polygons left out because they would create a
        /// non-manifold edge (see NonManifoldTriangles): at an ambiguous face, both cells have one patch through
        /// both of its surface segments. Refining there (SetRefinement) resolves it where the field separates the
        /// segments at the finer sampling (as in Zint, Grosso, Guertler, "Resolving non-manifoldness on meshes
        /// from dual marching cubes", 2022, who refine on the trilinear interpolant).
        /// </summary>
        public List<Vector3i> NonManifoldCells = new List<Vector3i>();
        /// <summary> local refinement: fine crossings that could not be assigned to a patch of a coarse cell (should be 0) </summary>
        public int TransitionErrors;

        public struct HiddenEdge
        {
            public Vector3d A, B;
            public bool Wall;
        }


        /// <summary> largest dense grid (nodes); larger ones throw an ArgumentException </summary>
        public static long MaxGridNodes = 1L << 30;


        /// <summary>
        /// Sparse mode: replace the given cells (grid cell coordinates) by factor^3 cells each (factor a power of
        /// two). fineCandidateCells are cells of the fine grid (cell (i,j,k) spans Bounds.Min + CellSize / factor
        /// * [i,i+1] x ...) inside the refined cells where the surface may pass, fineNodeValueF the field at
        /// fine grid nodes (it is not called at nodes of the coarse grid, those come from NodeValueF). Call
        /// FindCrossings or Generate afterwards. Replaces an earlier refinement.
        /// </summary>
        public void SetRefinement(int factor, IEnumerable<Vector3i> refinedCells, IEnumerable<Vector3i> fineCandidateCells, Func<Vector3i, double> fineNodeValueF)
        {
            if (CandidateCells == null)
                throw new Exception("DualContouring: refinement requires sparse mode");
            if (factor < 2 || (factor & (factor - 1)) != 0)
                throw new ArgumentException("DualContouring: refine factor must be a power of two");
            F = factor;
            refined = new HashSet<long>();
            foreach (Vector3i c in refinedCells)
                refined.Add(node_key(c));
            fineCandidates = new List<Vector3i>(fineCandidateCells);
            FineNodeValueF = fineNodeValueF;
            crossings = null;
        }
        Func<Vector3i, double> FineNodeValueF;
        List<Vector3i> fineCandidates;


        /// <summary>
        /// Finds the sign-changing grid edges (and in sparse mode the hidden gap / wall edges), the first part
        /// of Generate. Can be called again after SetRefinement.
        /// </summary>
        public void FindCrossings()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            origin = Bounds.Min;
            h = CellSize / F;
            sparse = (CandidateCells != null);
            if (sparse) {
                if (NodeValueF == null)
                    throw new Exception("DualContouring: sparse mode requires NodeValueF");
                sparseValues = new ConcurrentDictionary<long, float>();
                edgeDecisions = new ConcurrentDictionary<EdgeKey, EdgeDecision>();
                faceDecisions = new ConcurrentDictionary<EdgeKey, FaceDecision>();
                ImposedEdges = ImposedFaces = 0;
                imposedCells = new ConcurrentDictionary<long, Vector3i>();
            } else {
                Vector3d size = Bounds.Diagonal;
                double ni = Math.Ceiling(size.x / CellSize) + 1, nj = Math.Ceiling(size.y / CellSize) + 1, nk = Math.Ceiling(size.z / CellSize) + 1;
                if (!(ni * nj * nk <= MaxGridNodes) || ni < 1 || nj < 1 || nk < 1)
                    throw new ArgumentException("DualContouring: grid of " + ni + " x " + nj + " x " + nk + " nodes is too large (or invalid)");
                NI = (int)ni; NJ = (int)nj; NK = (int)nk;
                CornerDimensions = new Vector3i(NI, NJ, NK);
                sample_grid();
            }
            add_phase("sample", timer.ElapsedMilliseconds); timer.Restart();
            if (CancelF()) return;

            gapMilliseconds = 0;
            crossings = find_crossings();
            if (sparse) {
                var ic = new List<KeyValuePair<long, Vector3i>>(imposedCells);
                ic.Sort((x, y) => x.Key.CompareTo(y.Key));
                ImposedCells = new List<Vector3i>();
                foreach (var kv in ic) ImposedCells.Add(kv.Value);
            }
            add_phase("crossings", timer.ElapsedMilliseconds - gapMilliseconds);
            add_phase("gaps", gapMilliseconds);
        }

        void add_phase(string name, long ms)
        {
            long cur;
            PhaseMilliseconds[name] = PhaseMilliseconds.TryGetValue(name, out cur) ? cur + ms : ms;
        }


        public void Generate()
        {
            if (crossings == null)
                FindCrossings();
            if (CancelF() || crossings == null) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Action<string> phase = (name) => { add_phase(name, timer.ElapsedMilliseconds); timer.Restart(); };

            // Hermite data per crossing
            int NC = crossings.Count;
            Vector3d[] hpoints = new Vector3d[NC];
            Vector3d[] hnormals = new Vector3d[NC];
            Action<int> compute_hermite = (ci) => {
                GridEdge e = crossings[ci];
                Vector3i nb = e.End(F);
                Vector3d pa = node_pos(e.Start), pb = node_pos(nb);
                double fa = node_value(e.Start), fb = node_value(nb);
                double t = (Math.Abs(fb - fa) > MathUtil.Epsilon) ? MathUtil.Clamp((IsoValue - fa) / (fb - fa), 0, 1) : 0.5;
                Vector3d p = Vector3d.Lerp(pa, pb, t);
                Vector3d hp, hn;
                bool aInside = is_inside(fa);
                if (HermiteF != null && HermiteF(aInside ? pa : pb, aInside ? pb : pa, p, out hp, out hn)) {
                    hpoints[ci] = hp; hnormals[ci] = hn.Normalized;
                } else {
                    hpoints[ci] = p; hnormals[ci] = gradient(p).Normalized;
                }
            };
            if (ParallelCompute)
                gParallel.ForEach(Interval1i.Range(NC), compute_hermite);
            else
                for (int ci = 0; ci < NC; ++ci) compute_hermite(ci);
            phase("hermite");
            if (CancelF()) return;

            // gather crossings per cell, by local cube edge (each crossing edge touches 4 cells). Fine
            // crossings inside a face of a coarse cell are kept in a separate list of that cell.
            Dictionary<long, CellCrossings> cellCrossings = new Dictionary<long, CellCrossings>();
            TransitionErrors = 0;
            for (int ci = 0; ci < NC; ++ci) {
                GridEdge e = crossings[ci];
                for (int q = 0; q < 4; ++q) {
                    Cell cell;
                    if (quadrant_cell(e, q, out cell) == false) continue;
                    long key = cell_key(cell);
                    CellCrossings cc;
                    if (cellCrossings.TryGetValue(key, out cc) == false) {
                        cc = new CellCrossings() { Cell = cell, Edges = new int[] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 } };
                        cellCrossings[key] = cc;
                    }
                    int le = cell_edge_slot(cell, e, q);
                    if (le >= 0) {
                        if (cc.Edges[le] >= 0 && cc.Edges[le] != ci)
                            TransitionErrors++;
                        else
                            cc.Edges[le] = ci;
                    } else {
                        if (cc.FaceCrossings == null)
                            cc.FaceCrossings = new List<int>();
                        if (cc.FaceCrossings.Count == 0 || cc.FaceCrossings[cc.FaceCrossings.Count - 1] != ci)
                            cc.FaceCrossings.Add(ci);
                    }
                }
            }

            // vertices: one per surface patch in each cell (manifold dual contouring). The patches are the
            // ones marching cubes would create in the cell; each gets its own QEF vertex, so thin walls
            // and gaps inside a cell do not collapse into one vertex and create non-manifold edges.
            long[] cellList = new long[cellCrossings.Count];
            cellCrossings.Keys.CopyTo(cellList, 0);
            Array.Sort(cellList);       // deterministic vertex order
            int[][] cellEdgeGroup = new int[cellList.Length][];
            int[][] cellFaceGroup = new int[cellList.Length][];
            Vector3d[][] cellGroupPos = new Vector3d[cellList.Length][];
            Vector3d[][] cellGroupMass = new Vector3d[cellList.Length][];
            Vector3d[][] cellGroupPlane = new Vector3d[cellList.Length][];
            Vector3d[][] cellGroupCentroid = new Vector3d[cellList.Length][];
            bool[][] cellGroupFeature = new bool[cellList.Length][];
            int transitionErrors = 0;
            Action<int> place_vertices = (idx) => {
                CellCrossings cc = cellCrossings[cellList[idx]];
                int[] groups, parent;
                List<int> keys;
                int nGroups = cell_patches(cc.Cell, cc.Edges, out groups, out parent, out keys);
                int[] faceGroups = null;
                if (cc.FaceCrossings != null) {
                    faceGroups = new int[cc.FaceCrossings.Count];
                    for (int i = 0; i < faceGroups.Length; ++i) {
                        int key = face_crossing_key(cc.Cell, crossings[cc.FaceCrossings[i]], parent);
                        int g = keys.IndexOf(key);
                        if (g < 0) {
                            // should not happen (the fine signs on the face are equivalent to the coarse ones);
                            // keep the mesh closed with a vertex of its own
                            System.Threading.Interlocked.Increment(ref transitionErrors);
                            g = nGroups++;
                            keys.Add(key);
                        }
                        faceGroups[i] = g;
                    }
                }
                Vector3d cmin = node_pos(cc.Cell.Min);
                double csize = h * cell_size(cc.Cell);
                Vector3d[] pos = new Vector3d[nGroups], mass = new Vector3d[nGroups], plane = new Vector3d[nGroups], centroid = new Vector3d[nGroups];
                bool[] feature = new bool[nGroups];
                List<int> edges = new List<int>(12);
                for (int g = 0; g < nGroups; ++g) {
                    edges.Clear();
                    for (int le = 0; le < 12; ++le)
                        if (cc.Edges[le] >= 0 && groups[le] == g)
                            edges.Add(cc.Edges[le]);
                    if (faceGroups != null)
                        for (int i = 0; i < faceGroups.Length; ++i)
                            if (faceGroups[i] == g)
                                edges.Add(cc.FaceCrossings[i]);
                    pos[g] = solve_qef(cmin, csize, edges, hpoints, hnormals, out mass[g], out plane[g], out feature[g]);
                    foreach (int ci in edges)
                        centroid[g] += hpoints[ci];
                    centroid[g] /= Math.Max(edges.Count, 1);
                }
                cellEdgeGroup[idx] = groups;
                cellFaceGroup[idx] = faceGroups;
                cellGroupPos[idx] = pos;
                cellGroupMass[idx] = mass;
                cellGroupPlane[idx] = plane;
                cellGroupCentroid[idx] = centroid;
                cellGroupFeature[idx] = feature;
            };
            if (ParallelCompute)
                gParallel.ForEach(Interval1i.Range(cellList.Length), place_vertices);
            else
                for (int idx = 0; idx < cellList.Length; ++idx) place_vertices(idx);
            TransitionErrors += transitionErrors;
            phase("vertices");
            if (CancelF()) return;

            Mesh = new DMesh3();
            var vertexMass = new List<Vector3d>();
            var vertexPlane = new List<Vector3d>();
            var vertexCell = new List<Cell>();
            var vertexLast = new List<Vector3d>();
            Dictionary<long, int[]> cellEdgeVertex = new Dictionary<long, int[]>(cellList.Length);
            Dictionary<long, Dictionary<int, int>> cellFaceVertex = new Dictionary<long, Dictionary<int, int>>();
            for (int idx = 0; idx < cellList.Length; ++idx) {
                int[] groupVertex = new int[cellGroupPos[idx].Length];
                for (int g = 0; g < groupVertex.Length; ++g) {
                    groupVertex[g] = Mesh.AppendVertex(cellGroupPos[idx][g]);
                    vertexMass.Add(cellGroupMass[idx][g]);
                    vertexPlane.Add(cellGroupPlane[idx][g]);
                    vertexCell.Add(cellCrossings[cellList[idx]].Cell);
                    // last resort for resolving self-intersections: the cell center if it is a fine cell with
                    // one patch (the polygons are then squares between cell centers), else halfway between the
                    // patch's crossing centroid and the cell center; in a coarse cell (its center can be far
                    // from the surface) the crossing centroid
                    Cell vc = cellCrossings[cellList[idx]].Cell;
                    Vector3d vcenter = node_pos(vc.Min) + 0.5 * h * cell_size(vc) * Vector3d.One;
                    vertexLast.Add(vc.Coarse ? cellGroupCentroid[idx][g]
                        : groupVertex.Length == 1 ? vcenter : Vector3d.Lerp(cellGroupCentroid[idx][g], vcenter, 0.5));
                }
                int[] edgeVertex = new int[12];
                for (int le = 0; le < 12; ++le)
                    edgeVertex[le] = (cellEdgeGroup[idx][le] >= 0) ? groupVertex[cellEdgeGroup[idx][le]] : -1;
                cellEdgeVertex[cellList[idx]] = edgeVertex;
                if (cellFaceGroup[idx] != null) {
                    List<int> fc = cellCrossings[cellList[idx]].FaceCrossings;
                    var map = new Dictionary<int, int>();
                    for (int i = 0; i < fc.Count; ++i)
                        map[fc[i]] = groupVertex[cellFaceGroup[idx][i]];
                    cellFaceVertex[cellList[idx]] = map;
                }
            }

            // one polygon per crossing edge, connecting the vertices of its patches in the cells around it:
            // a quad, or a triangle for a fine edge inside a face of a coarse cell
            NonManifoldTriangles = 0;
            var nonManifoldCells = new Dictionary<long, Vector3i>();
            Cell[] polyCells = new Cell[4];
            int[] poly = new int[4];
            HashSet<int> transitionVertices = new HashSet<int>();
            for (int ci = 0; ci < NC; ++ci) {
                GridEdge e = crossings[ci];
                bool valid = true, anyFine = false, anyCoarse = false;
                for (int q = 0; q < 4 && valid; ++q) {
                    Cell cell;
                    if (quadrant_cell(e, q, out cell) == false) { valid = false; break; }
                    polyCells[q] = cell;
                    long key = cell_key(cell);
                    int le = cell_edge_slot(cell, e, q);
                    poly[q] = (le >= 0) ? cellEdgeVertex[key][le] : cellFaceVertex[key][ci];
                    if (poly[q] < 0) valid = false;
                    if (cell.Coarse) anyCoarse = true; else anyFine = true;
                }
                if (valid == false)
                    continue;
                // cells are ordered counter-clockwise around the +axis direction. The polygon normal must point
                // from inside to outside, ie along +axis if the edge start corner is inside.
                if (is_inside(node_value(e.Start)) == false) {
                    int tmp = poly[1]; poly[1] = poly[3]; poly[3] = tmp;
                }
                if (anyFine && anyCoarse) {
                    for (int q = 0; q < 4; ++q)
                        transitionVertices.Add(poly[q]);
                    // two cells around the edge can be the same coarse cell: a triangle
                    int n = 0;
                    int[] tri = new int[4];
                    for (int q = 0; q < 4; ++q)
                        if (poly[q] != poly[(q + 3) % 4])
                            tri[n++] = poly[q];
                    if (n == 3) {
                        if (Mesh.AppendTriangle(tri[0], tri[1], tri[2]) < 0) {
                            NonManifoldTriangles++;
                            add_non_manifold_cells(nonManifoldCells, polyCells);
                        }
                        continue;
                    } else if (n != 4)
                        continue;
                }
                if (append_quad(poly[0], poly[1], poly[2], poly[3]) == false)
                    add_non_manifold_cells(nonManifoldCells, polyCells);
            }
            var nmc = new List<KeyValuePair<long, Vector3i>>(nonManifoldCells);
            nmc.Sort((x, y) => x.Key.CompareTo(y.Key));
            NonManifoldCells = new List<Vector3i>();
            foreach (var kv in nmc) NonManifoldCells.Add(kv.Value);

            SelfIntersectionsFound = SelfIntersectionsLeft = 0;
            Func<int, Vector3d> fallback = (v) => vertexMass[v];
            if (ResolveSelfIntersections) {
                // Polygons where fine and coarse cells meet can fold (the coarse cell spans more than a quadrant
                // around a fine edge inside its face). Fallback for their vertices: the centroid of their
                // neighbours (recomputed in each round), on their dominant plane (so they stay on the
                // surface), in their cell.
                Func<int, Vector3d> ring_target = (v) => {
                    Vector3d c = Vector3d.Zero;
                    int n = 0;
                    foreach (int nb in Mesh.VtxVerticesItr(v)) { c += Mesh.GetVertex(nb); n++; }
                    if (n == 0)
                        return vertexMass[v];
                    c /= n;
                    Vector3d pn = vertexPlane[v];
                    c -= (c - vertexMass[v]).Dot(pn) * pn;
                    Cell cell = vertexCell[v];
                    double size = h * cell_size(cell);
                    Vector3d cmin = node_pos(cell.Min) + QEFCellMargin * size * Vector3d.One, cmax = node_pos(cell.Min) + (1 - QEFCellMargin) * size * Vector3d.One;
                    for (int k = 0; k < 3; ++k)
                        c[k] = MathUtil.Clamp(c[k], cmin[k], cmax[k]);
                    return c;
                };
                fallback = (v) => transitionVertices.Contains(v) ? ring_target(v) : vertexMass[v];
                resolve_self_intersections(fallback, (v) => vertexLast[v]);
            }
            phase("intersections");

            // safety net: closed results must have positive volume (relative to the center: results with
            // holes are not closed)
            Vector3d center = Bounds.Center;
            if (Mesh.TriangleCount > 0 && MeshMeasurements.VolumeArea(Mesh, Mesh.TriangleIndices(), (vid) => Mesh.GetVertex(vid) - center).x < 0)
                Mesh.ReverseOrientation(false);
            phase("mesh");
        }
        long gapMilliseconds;

        class CellCrossings
        {
            public Cell Cell;
            public int[] Edges;
            public List<int> FaceCrossings;
        }


        // Grid nodes and cells are in fine grid coordinates (node n is at Bounds.Min + h * n, h = CellSize / F).
        // Without refinement F = 1. A coarse cell spans F x F x F fine cells from Min.
        struct Cell
        {
            public Vector3i Min;
            public bool Coarse;
            public Cell(Vector3i min, bool coarse) { Min = min; Coarse = coarse; }
        }
        int cell_size(Cell c) { return c.Coarse ? F : 1; }
        static long cell_key(Cell c) { return c.Coarse ? (node_key(c.Min) | long.MinValue) : node_key(c.Min); }

        // grid edge from node Start along +Axis, of length F (an edge of the coarse grid) or 1 (fine grid)
        struct GridEdge
        {
            public Vector3i Start;
            public int Axis;
            public bool Coarse;
            public GridEdge(Vector3i start, int axis, bool coarse) { Start = start; Axis = axis; Coarse = coarse; }
            public Vector3i End(int F) { Vector3i e = Start; e[Axis] += Coarse ? F : 1; return e; }
        }
        struct EdgeKey : IEquatable<EdgeKey>
        {
            public long Node;
            public int Code;
            public bool Equals(EdgeKey o) { return Node == o.Node && Code == o.Code; }
            public override bool Equals(object o) { return o is EdgeKey && Equals((EdgeKey)o); }
            public override int GetHashCode() { unchecked { return Node.GetHashCode() * 31 + Code; } }
        }
        EdgeKey edge_key(GridEdge e) { return new EdgeKey() { Node = node_key(e.Start), Code = e.Axis * 2 + (e.Coarse ? 1 : 0) }; }
        // key that stays the same for a coarse edge when F changes (for the hidden gap results)
        EdgeKey stable_edge_key(GridEdge e) {
            if (e.Coarse)
                return new EdgeKey() { Node = node_key(e.Start / F), Code = e.Axis };
            return new EdgeKey() { Node = node_key(e.Start), Code = e.Axis + 9 * F };
        }

        Vector3d origin;
        double h;
        int F = 1;
        HashSet<long> refined = null;
        int NI, NJ, NK;
        float[] values;
        bool sparse;
        ConcurrentDictionary<long, float> sparseValues;
        List<GridEdge> crossings;
        ConcurrentDictionary<EdgeKey, HiddenEdge?> hiddenResults = new ConcurrentDictionary<EdgeKey, HiddenEdge?>();

        // node keys support coordinates in [-2^20, 2^20)
        const int KeyBias = 1 << 20;
        static long node_key(Vector3i n) {
            return (long)(n.x + KeyBias) | ((long)(n.y + KeyBias) << 21) | ((long)(n.z + KeyBias) << 42);
        }

        Vector3d node_pos(Vector3i n) { return origin + h * new Vector3d(n.x, n.y, n.z); }
        bool is_inside(double f) { return InsideIsLess ? f < IsoValue : f > IsoValue; }

        static int fdiv(int a, int b) { return a >= 0 ? a / b : -((-a + b - 1) / b); }
        static int fmod(int a, int b) { int m = a % b; return m < 0 ? m + b : m; }
        static Vector3i fdiv(Vector3i a, int b) { return new Vector3i(fdiv(a.x, b), fdiv(a.y, b), fdiv(a.z, b)); }
        static Vector3i axis_vec(int axis, int len) { Vector3i v = Vector3i.Zero; v[axis] = len; return v; }

        // is the coarse cell (coarse grid coordinates) refined
        bool is_refined(Vector3i coarseCell) { return refined != null && refined.Contains(node_key(coarseCell)); }

        double node_value(Vector3i n)
        {
            if (sparse == false)
                return values[n.x + NI * (n.y + NJ * n.z)];
            long key = node_key(n);
            float value;
            if (sparseValues.TryGetValue(key, out value) == false) {
                value = (F == 1) ? clamp_value(NodeValueF(n)) : mixed_node_value(n);
                sparseValues[key] = value;
            }
            return value;
        }
        static float clamp_value(double f) { return (float)MathUtil.Clamp(f, -float.MaxValue / 2, float.MaxValue / 2); }

        // with refinement: coarse nodes from NodeValueF; fine nodes on faces / edges between refined and
        // unrefined cells from the edge / face decisions; all others from FineNodeValueF
        float mixed_node_value(Vector3i n)
        {
            int nDiv = 0, freeAxis = -1, divAxis = -1;
            for (int a = 0; a < 3; ++a) {
                if (fmod(n[a], F) == 0) { nDiv++; divAxis = a; } else freeAxis = a;
            }
            if (nDiv == 3)
                return clamp_value(NodeValueF(n / F));
            if (nDiv == 2) {
                // on the coarse edge along freeAxis
                Vector3i E = fdiv(n, F);
                if (is_interface_edge(E, freeAxis))
                    return edge_decision(E, freeAxis).Values[fmod(n[freeAxis], F)];
            } else if (nDiv == 1) {
                // on the coarse face normal to divAxis, between the coarse cells P and P - e_divAxis
                Vector3i P = fdiv(n, F);
                Vector3i below = P; below[divAxis] -= 1;
                if (is_refined(P) != is_refined(below)) {
                    int fu = (divAxis + 1) % 3, fv = (divAxis + 2) % 3;
                    return face_decision(P, divAxis).Values[fmod(n[fu], F) + (F + 1) * fmod(n[fv], F)];
                }
            }
            return real_fine_value(n);
        }
        float real_fine_value(Vector3i n) { return clamp_value(FineNodeValueF(n)); }

        // the 4 coarse cells around the coarse edge from coarse node E along axis
        bool is_interface_edge(Vector3i E, int axis)
        {
            int nRefined = 0;
            for (int q = 0; q < 4; ++q)
                if (is_refined(coarse_edge_cell(E, axis, q))) nRefined++;
            return nRefined > 0 && nRefined < 4;
        }
        static Vector3i coarse_edge_cell(Vector3i E, int axis, int q)
        {
            Vector3i c = E;
            c[(axis + 1) % 3] += edge_du[q]; c[(axis + 2) % 3] += edge_dv[q];
            return c;
        }
        bool is_subdivided_edge(Vector3i E, int axis)
        {
            for (int q = 0; q < 4; ++q)
                if (is_refined(coarse_edge_cell(E, axis, q))) return true;
            return false;
        }


        // Signs of the fine nodes along a coarse edge between refined and unrefined cells (F + 1 nodes, the
        // ends are the coarse nodes): the real ones if they change sign exactly as often as the coarse edge
        // does (0 or 1 times), otherwise one change at the first real one.
        class EdgeDecision
        {
            public float[] Values;
            public bool[] Inside;
        }
        ConcurrentDictionary<EdgeKey, EdgeDecision> edgeDecisions;

        EdgeDecision edge_decision(Vector3i E, int axis)
        {
            EdgeKey key = new EdgeKey() { Node = node_key(E), Code = axis };
            EdgeDecision d;
            if (edgeDecisions.TryGetValue(key, out d))
                return d;
            d = new EdgeDecision() { Values = new float[F + 1], Inside = new bool[F + 1] };
            Vector3i n0 = F * E;
            for (int i = 0; i <= F; ++i) {
                Vector3i n = n0 + axis_vec(axis, i);
                d.Values[i] = (i == 0 || i == F) ? (float)node_value(n) : real_fine_value(n);
                d.Inside[i] = is_inside(d.Values[i]);
            }
            bool s0 = d.Inside[0], sF = d.Inside[F];
            int changes = 0, first = -1;
            for (int i = 1; i <= F; ++i) {
                if (d.Inside[i] != d.Inside[i - 1]) changes++;
                if (first < 0 && d.Inside[i] != s0) first = i;
            }
            if (changes != (s0 != sF ? 1 : 0)) {
                for (int i = 1; i < F; ++i)
                    set_sign(d.Values, d.Inside, i, (s0 == sF || i < first) ? s0 : sF);
                System.Threading.Interlocked.Increment(ref ImposedEdges);
                for (int q = 0; q < 4; ++q)
                    add_imposed_cell(coarse_edge_cell(E, axis, q));
            }
            return edgeDecisions.GetOrAdd(key, d);
        }

        ConcurrentDictionary<long, Vector3i> imposedCells;
        void add_imposed_cell(Vector3i c)
        {
            if (is_refined(c) == false)
                imposedCells[node_key(c)] = c;
        }

        void set_sign(float[] values, bool[] inside, int i, bool sign)
        {
            if (inside[i] == sign)
                return;
            inside[i] = sign;
            // a value just on the requested side (only used for interpolating the crossing point)
            double eps = 1e-3;
            values[i] = (float)((sign != InsideIsLess) ? IsoValue + eps : IsoValue - eps);
        }


        // Signs of the fine nodes on a coarse face between a refined and an unrefined cell ((F + 1)^2 nodes,
        // the border from the edge decisions): the real ones if the fine face is topologically equivalent to
        // the coarse face, otherwise imposed (inside nodes only along the border, see impose). Equivalent
        // means: each connected component of the fine nodes (inside nodes connected along fine edges, outside
        // nodes also across the diagonal of ambiguous fine faces, as in cell_patches) contains a coarse
        // corner, and two corners are in the same fine component iff they are in the same coarse one. As every
        // component reaches the border, where the signs change once per coarse sign change, the fine contour
        // curves on the face then correspond one to one to the coarse ones. Corner[node] is a coarse corner
        // (cu + 2 cv) in the fine component of each node.
        class FaceDecision
        {
            public float[] Values;
            public bool[] Inside;
            public int[] Corner;
        }
        ConcurrentDictionary<EdgeKey, FaceDecision> faceDecisions;

        FaceDecision face_decision(Vector3i P, int axis)
        {
            EdgeKey key = new EdgeKey() { Node = node_key(P), Code = axis };
            FaceDecision d;
            if (faceDecisions.TryGetValue(key, out d))
                return d;
            int fu = (axis + 1) % 3, fv = (axis + 2) % 3, N = F + 1;
            d = new FaceDecision() { Values = new float[N * N], Inside = new bool[N * N], Corner = new int[N * N] };
            EdgeDecision e0 = edge_decision(P, fu), e1 = edge_decision(P + axis_vec(fv, 1), fu);
            EdgeDecision e2 = edge_decision(P, fv), e3 = edge_decision(P + axis_vec(fu, 1), fv);
            for (int i = 0; i <= F; ++i) {
                d.Values[i] = e0.Values[i]; d.Values[i + N * F] = e1.Values[i];
                d.Values[N * i] = e2.Values[i]; d.Values[F + N * i] = e3.Values[i];
            }
            Vector3i n0 = F * P;
            for (int j = 1; j < F; ++j)
                for (int i = 1; i < F; ++i)
                    d.Values[i + N * j] = real_fine_value(n0 + axis_vec(fu, i) + axis_vec(fv, j));
            for (int k = 0; k < N * N; ++k)
                d.Inside[k] = is_inside(d.Values[k]);

            int[] corners = { 0, F, N * F, F + N * F };     // (cu, cv) = (0,0), (1,0), (0,1), (1,1)
            bool[] cs = new bool[4];
            for (int c = 0; c < 4; ++c) cs[c] = d.Inside[corners[c]];
            int[] cp = { 0, 1, 2, 3 };
            if (cs[0] == cs[1]) union(cp, 0, 1);
            if (cs[2] == cs[3]) union(cp, 2, 3);
            if (cs[0] == cs[2]) union(cp, 0, 2);
            if (cs[1] == cs[3]) union(cp, 1, 3);
            if (cs[0] == cs[3] && cs[1] == cs[2] && cs[0] != cs[1]) {
                if (cs[0]) union(cp, 1, 2); else union(cp, 0, 3);
            }

            int[] parent = label_face(d.Inside, N);
            bool equivalent = true;
            for (int a = 0; a < 4 && equivalent; ++a)
                for (int b = a + 1; b < 4 && equivalent; ++b)
                    if ((find(parent, corners[a]) == find(parent, corners[b])) != (find(cp, a) == find(cp, b)))
                        equivalent = false;
            if (equivalent) {
                var cornerRoots = new HashSet<int>();
                for (int c = 0; c < 4; ++c) cornerRoots.Add(find(parent, corners[c]));
                for (int k = 0; k < N * N && equivalent; ++k)
                    if (cornerRoots.Contains(find(parent, k)) == false)
                        equivalent = false;
            }
            if (equivalent == false) {
                // interior nodes outside, unless the whole coarse face is inside: the inside components are
                // then the runs along the border around the inside corners, the outside one component
                bool allInside = cs[0] && cs[1] && cs[2] && cs[3];
                for (int j = 1; j < F; ++j)
                    for (int i = 1; i < F; ++i)
                        set_sign(d.Values, d.Inside, i + N * j, allInside);
                parent = label_face(d.Inside, N);
                System.Threading.Interlocked.Increment(ref ImposedFaces);
                Vector3i below = P; below[axis] -= 1;
                add_imposed_cell(P); add_imposed_cell(below);
            }
            int[] rootCorner = new int[N * N];
            for (int k = 0; k < N * N; ++k) rootCorner[k] = -1;
            for (int c = 3; c >= 0; --c) rootCorner[find(parent, corners[c])] = c;
            for (int k = 0; k < N * N; ++k)
                d.Corner[k] = rootCorner[find(parent, k)];
            return faceDecisions.GetOrAdd(key, d);
        }

        // components of the fine nodes of a face (see face_decision)
        static int[] label_face(bool[] inside, int N)
        {
            int[] parent = new int[N * N];
            for (int k = 0; k < N * N; ++k) parent[k] = k;
            for (int j = 0; j < N; ++j) {
                for (int i = 0; i < N; ++i) {
                    int k = i + N * j;
                    if (i + 1 < N && inside[k] == inside[k + 1]) union(parent, k, k + 1);
                    if (j + 1 < N && inside[k] == inside[k + N]) union(parent, k, k + N);
                    if (i + 1 < N && j + 1 < N) {
                        int a = k, b = k + 1, c = k + 1 + N, dd = k + N;
                        if (inside[a] == inside[c] && inside[b] == inside[dd] && inside[a] != inside[b]) {
                            if (inside[a]) union(parent, b, dd); else union(parent, a, c);
                        }
                    }
                }
            }
            return parent;
        }

        // patch key (as in cell_patches) of a fine crossing inside a face of the coarse cell
        int face_crossing_key(Cell cell, GridEdge e, int[] parent)
        {
            Vector3i a = e.Start, b = e.End(F);
            bool aInside = is_inside(node_value(a));
            Vector3i nin = aInside ? a : b, nout = aInside ? b : a;
            Vector3i rel = e.Start - cell.Min;
            int u = (e.Axis + 1) % 3, v = (e.Axis + 2) % 3;
            int axis = (fmod(rel[u], F) == 0) ? u : v;
            Vector3i P = cell.Min / F;
            P[axis] += rel[axis] / F;
            FaceDecision d = face_decision(P, axis);
            int fu = (axis + 1) % 3, fv = (axis + 2) % 3, N = F + 1;
            Vector3i n0 = F * P;
            int cin = d.Corner[(nin[fu] - n0[fu]) + N * (nin[fv] - n0[fv])];
            int cout = d.Corner[(nout[fu] - n0[fu]) + N * (nout[fv] - n0[fv])];
            if (cin < 0 || cout < 0)
                return -1;
            return find(parent, face_corner_local(P, axis, cin, cell)) * 8 + find(parent, face_corner_local(P, axis, cout, cell));
        }
        // local corner index (x + 2y + 4z) in the cell of face corner c (cu + 2 cv) of the face at P normal to axis
        int face_corner_local(Vector3i P, int axis, int c, Cell cell)
        {
            Vector3i corner = P;
            corner[(axis + 1) % 3] += c & 1;
            corner[(axis + 2) % 3] += c >> 1;
            Vector3i l = corner - cell.Min / F;
            return l.x + 2 * l.y + 4 * l.z;
        }


        // does cell (i,j,k) exist: dense mode inside the grid, sparse mode always
        bool cell_exists(Vector3i c) {
            if (sparse)
                return true;
            return c.x >= 0 && c.y >= 0 && c.z >= 0 && c.x < NI - 1 && c.y < NJ - 1 && c.z < NK - 1;
        }

        // the edges a cell owns for the crossing search: its 12 edges; with refinement, the edges of a coarse
        // cell along refined cells are split into fine edges, and its faces shared with refined cells add the
        // fine edges inside them
        void cell_edges(Cell cell, List<GridEdge> edges)
        {
            edges.Clear();
            int s = cell_size(cell);
            for (int le = 0; le < 12; ++le) {
                Index2i ce = cube_edge_corners[le];
                Vector3i a = cell.Min + s * local_offset(ce.a);
                int axis = le / 4;
                if (cell.Coarse == false)
                    edges.Add(new GridEdge(a, axis, false));
                else if (F == 1 || is_subdivided_edge(a / F, axis) == false)
                    edges.Add(new GridEdge(a, axis, true));
                else
                    for (int t = 0; t < F; ++t)
                        edges.Add(new GridEdge(a + axis_vec(axis, t), axis, false));
            }
            if (cell.Coarse == false || F == 1)
                return;
            Vector3i C = cell.Min / F;
            for (int axis = 0; axis < 3; ++axis) {
                for (int side = 0; side < 2; ++side) {
                    Vector3i nb = C; nb[axis] += side == 0 ? -1 : 1;
                    if (is_refined(nb) == false)
                        continue;
                    Vector3i fmin = cell.Min + axis_vec(axis, side * F);
                    int fu = (axis + 1) % 3, fv = (axis + 2) % 3;
                    for (int j = 1; j < F; ++j) {
                        for (int t = 0; t < F; ++t) {
                            edges.Add(new GridEdge(fmin + axis_vec(fv, j) + axis_vec(fu, t), fu, false));
                            edges.Add(new GridEdge(fmin + axis_vec(fu, j) + axis_vec(fv, t), fv, false));
                        }
                    }
                }
            }
        }


        List<GridEdge> find_crossings()
        {
            List<GridEdge> crossings = new List<GridEdge>();
            HiddenEdges = new List<HiddenEdge>();
            HiddenGapEdges = HiddenWallEdges = 0;
            if (sparse == false) {
                for (int k = 0; k < NK; ++k) {
                    for (int j = 0; j < NJ; ++j) {
                        for (int i = 0; i < NI; ++i) {
                            int c = i + NI * (j + NJ * k);
                            bool inside = is_inside(values[c]);
                            if (i + 1 < NI && inside != is_inside(values[c + 1])) crossings.Add(new GridEdge(new Vector3i(i, j, k), 0, true));
                            if (j + 1 < NJ && inside != is_inside(values[c + NI])) crossings.Add(new GridEdge(new Vector3i(i, j, k), 1, true));
                            if (k + 1 < NK && inside != is_inside(values[c + NI * NJ])) crossings.Add(new GridEdge(new Vector3i(i, j, k), 2, true));
                        }
                    }
                }
                return crossings;
            }

            // sparse: the edges of each candidate cell, then of every cell around a crossing edge that was
            // not a candidate (its other edges can cross too; without them the surface would have holes),
            // until no new cells come up. Corners of each batch of cells are evaluated in parallel first.
            // With refinement, candidates inside refined cells are replaced by the fine candidates.
            List<Cell> cells = new List<Cell>();
            HashSet<long> visited = new HashSet<long>();
            foreach (Vector3i c in CandidateCells) {
                if (is_refined(c))
                    continue;
                Cell cell = new Cell(F * c, true);
                if (visited.Add(cell_key(cell)))
                    cells.Add(cell);
            }
            if (F > 1 && fineCandidates != null) {
                foreach (Vector3i c in fineCandidates) {
                    if (is_refined(fdiv(c, F)) == false)
                        continue;
                    Cell cell = new Cell(c, false);
                    if (visited.Add(cell_key(cell)))
                        cells.Add(cell);
                }
            }
            HashSet<EdgeKey> seen = new HashSet<EdgeKey>();
            List<GridEdge> insideEdges = new List<GridEdge>(), outsideEdges = new List<GridEdge>();
            List<Cell> allCells = new List<Cell>(cells);
            List<GridEdge> edges = new List<GridEdge>(12);
            while (cells.Count > 0) {
                List<Cell> batch = cells;
                Action<int> eval_corners = (idx) => {
                    Cell cell = batch[idx];
                    int s = cell_size(cell);
                    for (int lc = 0; lc < 8; ++lc)
                        node_value(cell.Min + s * local_offset(lc));
                    if (cell.Coarse && F > 1) {
                        List<GridEdge> ce = new List<GridEdge>();
                        cell_edges(cell, ce);
                        foreach (GridEdge e in ce) {
                            node_value(e.Start); node_value(e.End(F));
                        }
                    }
                };
                if (ParallelCompute)
                    gParallel.ForEach(Interval1i.Range(batch.Count), eval_corners);
                else
                    for (int idx = 0; idx < batch.Count; ++idx) eval_corners(idx);

                cells = new List<Cell>();
                foreach (Cell cell in batch) {
                    cell_edges(cell, edges);
                    foreach (GridEdge e in edges) {
                        if (seen.Add(edge_key(e)) == false)
                            continue;
                        bool aInside = is_inside(node_value(e.Start));
                        if (aInside != is_inside(node_value(e.End(F)))) {
                            crossings.Add(e);
                            for (int q = 0; q < 4; ++q) {
                                Cell nc;
                                if (quadrant_cell(e, q, out nc) && visited.Add(cell_key(nc))) {
                                    cells.Add(nc);
                                    allCells.Add(nc);
                                }
                            }
                        } else if (aInside && HiddenGapF != null)
                            insideEdges.Add(e);
                        else if (!aInside && HiddenWallF != null)
                            outsideEdges.Add(e);
                    }
                }
            }
            var gapTimer = System.Diagnostics.Stopwatch.StartNew();
            // the outside diagonal of ambiguous faces (diagonal sign pattern): the face rule connects the
            // outside corners and separates the inside ones; material crossing the diagonal (a wall along the
            // other diagonal) is a hidden wall too
            List<GridEdge> diagonals = new List<GridEdge>();
            List<Vector3i> diagonalEnds = new List<Vector3i>();
            if (HiddenWallF != null) {
                HashSet<EdgeKey> seenFaces = new HashSet<EdgeKey>();
                foreach (Cell cell in allCells) {
                    int sz = cell_size(cell);
                    for (int f = 0; f < 6; ++f) {
                        int axis = f / 2, side = f % 2;
                        if (cell.Coarse && F > 1) {
                            Vector3i nb = cell.Min / F; nb[axis] += side == 0 ? -1 : 1;
                            if (is_refined(nb)) continue;       // split into fine faces
                        }
                        Vector3i fmin = cell.Min + axis_vec(axis, side * sz);
                        if (seenFaces.Add(new EdgeKey() { Node = node_key(fmin), Code = axis * 2 + (cell.Coarse ? 1 : 0) }) == false)
                            continue;
                        Index4i fc = cube_faces[f];
                        Vector3i[] corner = { cell.Min + sz * local_offset(fc.a), cell.Min + sz * local_offset(fc.b),
                                              cell.Min + sz * local_offset(fc.c), cell.Min + sz * local_offset(fc.d) };
                        bool ia = is_inside(node_value(corner[0])), ib = is_inside(node_value(corner[1]));
                        if (ia != is_inside(node_value(corner[2])) || ib != is_inside(node_value(corner[3])) || ia == ib)
                            continue;
                        int d0 = ia ? 1 : 0;        // outside diagonal: corners d0, d0 + 2
                        // as an edge: Axis encodes the face axis and diagonal, for the result cache
                        diagonals.Add(new GridEdge(corner[d0], 3 + axis * 2 + d0, cell.Coarse));
                        diagonalEnds.Add(corner[d0 + 2]);
                    }
                }
            }
            var hidden = new ConcurrentBag<KeyValuePair<long, HiddenEdge>>();
            for (int pass = 0; pass < 3; ++pass) {
                bool wall = pass >= 1;
                List<GridEdge> list = (pass == 0) ? insideEdges : (pass == 1) ? outsideEdges : diagonals;
                if (list.Count == 0)
                    continue;
                Action<int> test = (i) => {
                    GridEdge e = list[i];
                    // results (with the end points they were computed for) are kept for later searches
                    EdgeKey key = stable_edge_key(e);
                    HiddenEdge? result;
                    if (hiddenResults.TryGetValue(key, out result) == false) {
                        Vector3d a = node_pos(e.Start), b = node_pos(pass == 2 ? diagonalEnds[i] : e.End(F));
                        bool isHidden = wall ? HiddenWallF(a, b) : HiddenGapF(a, b);
                        result = isHidden ? new HiddenEdge() { A = a, B = b, Wall = wall } : (HiddenEdge?)null;
                        hiddenResults[key] = result;
                    }
                    if (result.HasValue) {
                        if (wall) System.Threading.Interlocked.Increment(ref HiddenWallEdges);
                        else System.Threading.Interlocked.Increment(ref HiddenGapEdges);
                        hidden.Add(new KeyValuePair<long, HiddenEdge>(node_key(e.Start) * 20 + e.Axis * 2 + (wall ? 1 : 0), result.Value));
                    }
                };
                if (ParallelCompute)
                    gParallel.ForEach(Interval1i.Range(list.Count), test);
                else
                    for (int i = 0; i < list.Count; ++i) test(i);
            }
            var hiddenList = new List<KeyValuePair<long, HiddenEdge>>(hidden);
            hiddenList.Sort((x, y) => x.Key.CompareTo(y.Key));
            foreach (var kv in hiddenList)
                HiddenEdges.Add(kv.Value);
            gapMilliseconds = gapTimer.ElapsedMilliseconds;
            // deterministic order
            crossings.Sort((x, y) => {
                int c = node_key(x.Start).CompareTo(node_key(y.Start));
                if (c != 0) return c;
                c = x.Axis.CompareTo(y.Axis);
                return c != 0 ? c : x.Coarse.CompareTo(y.Coarse);
            });
            return crossings;
        }


        // the 4 cells around a grid edge, counter-clockwise around the +axis direction (q = 0..3)
        static readonly int[] edge_du = { -1, 0, 0, -1 }, edge_dv = { -1, -1, 0, 0 };
        bool quadrant_cell(GridEdge e, int q, out Cell cell) {
            int u = (e.Axis + 1) % 3, v = (e.Axis + 2) % 3;
            Vector3i p = e.Start;
            if (e.Coarse) {
                p[u] += F * edge_du[q]; p[v] += F * edge_dv[q];
                cell = new Cell(p, true);
                return cell_exists(p / F);
            }
            p[u] += edge_du[q]; p[v] += edge_dv[q];
            Vector3i blk = fdiv(p, F);
            cell = is_refined(blk) ? new Cell(p, false) : new Cell(F * blk, true);
            return true;
        }
        // index (0..11) of the grid edge inside the q'th cell around it: axis*4 + lu + 2*lv, where (lu,lv)
        // are the edge's local coordinates along the other two axes (u = axis+1, v = axis+2)
        static int edge_local_index(int axis, int q) {
            return axis * 4 + (-edge_du[q]) + 2 * (-edge_dv[q]);
        }
        // the local edge (0..11) of the cell that the crossing edge e (q'th cell around it) lies on, or -1 for
        // a fine edge inside a face of a coarse cell
        int cell_edge_slot(Cell cell, GridEdge e, int q) {
            if (cell.Coarse == false || e.Coarse)
                return edge_local_index(e.Axis, q);
            Vector3i rel = e.Start - cell.Min;
            int u = (e.Axis + 1) % 3, v = (e.Axis + 2) % 3;
            if (fmod(rel[u], F) == 0 && fmod(rel[v], F) == 0)
                return edge_local_index(e.Axis, q);
            return -1;
        }
        static Vector3i local_offset(int lc) {
            return new Vector3i(lc & 1, (lc >> 1) & 1, (lc >> 2) & 1);
        }

        // local cube corners are numbered x + 2y + 4z
        static int local_corner(int axis, int ca, int cu, int cv) {
            int[] c = new int[3];
            c[axis] = ca; c[(axis + 1) % 3] = cu; c[(axis + 2) % 3] = cv;
            return c[0] + 2 * c[1] + 4 * c[2];
        }
        static readonly Index2i[] cube_edge_corners = make_cube_edges();
        static Index2i[] make_cube_edges() {
            Index2i[] edges = new Index2i[12];
            for (int axis = 0; axis < 3; ++axis)
                for (int lv = 0; lv < 2; ++lv)
                    for (int lu = 0; lu < 2; ++lu)
                        edges[axis * 4 + lu + 2 * lv] = new Index2i(local_corner(axis, 0, lu, lv), local_corner(axis, 1, lu, lv));
            return edges;
        }
        // 6 faces, corners in cyclic order
        static readonly Index4i[] cube_faces = make_cube_faces();
        static Index4i[] make_cube_faces() {
            Index4i[] faces = new Index4i[6];
            for (int axis = 0; axis < 3; ++axis)
                for (int side = 0; side < 2; ++side)
                    faces[2 * axis + side] = new Index4i(local_corner(axis, side, 0, 0), local_corner(axis, side, 1, 0),
                                                         local_corner(axis, side, 1, 1), local_corner(axis, side, 0, 1));
            return faces;
        }

        // Group the crossing edges of a cell into surface patches. Inside corners are connected along cube
        // edges, outside corners along cube edges and across ambiguous faces (diagonal sign pattern), which
        // is the standard face rule and only depends on the face, so neighbouring cells agree. A patch is
        // a pair (inside component, outside component), keys[g] = root(inside) * 8 + root(outside). Returns
        // the patch count; groups[le] is the patch of local edge le, or -1 if it has no crossing.
        int cell_patches(Cell cell, int[] edgeCrossing, out int[] groups, out int[] parent, out List<int> keys)
        {
            bool[] inside = new bool[8];
            parent = new int[8];
            int s = cell_size(cell);
            for (int lc = 0; lc < 8; ++lc) {
                inside[lc] = is_inside(node_value(cell.Min + s * local_offset(lc)));
                parent[lc] = lc;
            }
            for (int le = 0; le < 12; ++le) {
                Index2i ce = cube_edge_corners[le];
                if (inside[ce.a] == inside[ce.b])
                    union(parent, ce.a, ce.b);
            }
            for (int f = 0; f < 6; ++f) {
                Index4i fc = cube_faces[f];
                if (inside[fc.a] == inside[fc.c] && inside[fc.b] == inside[fc.d] && inside[fc.a] != inside[fc.b]) {
                    if (inside[fc.a]) union(parent, fc.b, fc.d);
                    else union(parent, fc.a, fc.c);
                }
            }
            groups = new int[12];
            keys = new List<int>(4);
            for (int le = 0; le < 12; ++le) {
                groups[le] = -1;
                if (edgeCrossing[le] < 0)
                    continue;
                Index2i ce = cube_edge_corners[le];
                int cin = inside[ce.a] ? ce.a : ce.b, cout = inside[ce.a] ? ce.b : ce.a;
                int key = find(parent, cin) * 8 + find(parent, cout);
                int g = keys.IndexOf(key);
                if (g < 0) {
                    g = keys.Count;
                    keys.Add(key);
                }
                groups[le] = g;
            }
            return keys.Count;
        }
        static int find(int[] parent, int i) {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }
        static void union(int[] parent, int a, int b) {
            parent[find(parent, a)] = find(parent, b);
        }


        void sample_grid()
        {
            values = new float[NI * NJ * NK];
            Action<int> sample_slab = (k) => {
                for (int j = 0; j < NJ; ++j) {
                    for (int i = 0; i < NI; ++i) {
                        Vector3d pos = origin + CellSize * new Vector3d(i, j, k);
                        double f = Implicit.Value(ref pos);
                        // keep values finite, a float overflow would turn edge interpolation into NaN
                        values[i + NI * (j + NJ * k)] = (float)MathUtil.Clamp(f, -float.MaxValue / 2, float.MaxValue / 2);
                    }
                }
            };
            if (ParallelCompute)
                gParallel.ForEach(Interval1i.Range(NK), sample_slab);
            else
                for (int k = 0; k < NK; ++k) sample_slab(k);
        }


        Vector3d gradient(Vector3d p)
        {
            double h = CellSize * 0.25;
            Vector3d g = Vector3d.Zero;
            for (int c = 0; c < 3; ++c) {
                Vector3d d = Vector3d.Zero; d[c] = h;
                Vector3d p0 = p + d, p1 = p - d;
                g[c] = Implicit.Value(ref p0) - Implicit.Value(ref p1);
            }
            return g;
        }


        // minimize sum_i (n_i . (x - p_i))^2 around the mass point, with truncated pseudo-inverse,
        // and keep the result inside the cell
        /// <summary>
        /// Neighbouring polygons can fold through each other: around vertices on sharp features (QEF of
        /// rank 2 or 3: edges, corners), where fine and coarse cells meet, and where several thin sheets pass
        /// through few cells (eg a winding number field of a non-orientable input). If set, intersecting
        /// triangles are found with exact predicates and their vertices moved until none are left: halfway
        /// to a fallback position four times, then onto it (the QEF solution on the dominant plane only, which
        /// stays on one input surface), then onto a last resort (the center of a fine cell with one patch,
        /// where polygons between such cells become non-intersecting squares between cell centers; else
        /// halfway between the patch's crossing centroid and the cell center, or in a coarse cell the
        /// crossing centroid).
        /// </summary>
        public bool ResolveSelfIntersections = true;
        /// <summary> intersecting triangle pairs found before / left after resolving </summary>
        public int SelfIntersectionsFound, SelfIntersectionsLeft;

        void resolve_self_intersections(Func<int, Vector3d> fallback, Func<int, Vector3d> lastResort)
        {
            var tree = new DMeshAABBTree3(Mesh, true);
            HashSet<int> candidates = new HashSet<int>(Mesh.TriangleIndices());
            var moves = new Dictionary<int, int>();
            for (int round = 0; round < 10; ++round) {
                var moving = new HashSet<int>();
                int pairs = 0;
                object lockObj = new object();
                int[] cand = new int[candidates.Count];
                candidates.CopyTo(cand);
                gParallel.ForEach(Interval1i.Range(cand.Length), (k) => {
                    int ta = cand[k];
                    Index3i ia = Mesh.GetTriangle(ta);
                    Triangle3d A = new Triangle3d(Mesh.GetVertex(ia.a), Mesh.GetVertex(ia.b), Mesh.GetVertex(ia.c));
                    if (ExactTriangleIntersection.IsDegenerate(ref A)) return;
                    AxisAlignedBox3d box = new AxisAlignedBox3d(A.V0); box.Contain(A.V1); box.Contain(A.V2);
                    var hits = new List<int>();
                    tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                        NextBoxDF = (b, depth) => b.Intersects(box),
                        NextTriangleF = (tb) => {
                            if (tb == ta || (tb < ta && candidates.Contains(tb))) return;
                            Index3i ib = Mesh.GetTriangle(tb);
                            Triangle3d B = new Triangle3d(Mesh.GetVertex(ib.a), Mesh.GetVertex(ib.b), Mesh.GetVertex(ib.c));
                            if (ExactTriangleIntersection.IsDegenerate(ref B) == false && ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B))
                                hits.Add(tb);
                        }
                    });
                    if (hits.Count > 0) {
                        lock (lockObj) {
                            pairs += hits.Count;
                            moving.Add(ia.a); moving.Add(ia.b); moving.Add(ia.c);
                            foreach (int tb in hits) {
                                Index3i ib = Mesh.GetTriangle(tb);
                                moving.Add(ib.a); moving.Add(ib.b); moving.Add(ib.c);
                            }
                        }
                    }
                });
                if (round == 0) SelfIntersectionsFound = pairs;
                SelfIntersectionsLeft = pairs;
                if (pairs == 0)
                    return;
                var targets = new List<KeyValuePair<int, Vector3d>>();
                foreach (int v in moving) {
                    int n;
                    moves.TryGetValue(v, out n);
                    moves[v] = n + 1;
                    Vector3d p = Mesh.GetVertex(v);
                    targets.Add(new KeyValuePair<int, Vector3d>(v,
                        (n < 4) ? Vector3d.Lerp(p, fallback(v), 0.5) : (n < 6) ? fallback(v) : lastResort(v)));
                }
                foreach (var kv in targets)
                    Mesh.SetVertex(kv.Key, kv.Value);
                tree = new DMeshAABBTree3(Mesh, true);
                candidates.Clear();
                foreach (int v in moving)
                    foreach (int tt in Mesh.VtxTrianglesItr(v))
                        candidates.Add(tt);
            }
        }

        Vector3d solve_qef(Vector3d cellMin, double cellSize, List<int> edges, Vector3d[] points, Vector3d[] normals, out Vector3d massPoint, out Vector3d planeNormal, out bool isFeature)
        {
            Vector3d mass = Vector3d.Zero;
            foreach (int ci in edges)
                mass += points[ci];
            mass /= edges.Count;

            double[] A = new double[9];
            Vector3d b = Vector3d.Zero;
            foreach (int ci in edges) {
                Vector3d n = normals[ci];
                double d = n.Dot(points[ci] - mass);
                for (int r = 0; r < 3; ++r)
                    for (int c = 0; c < 3; ++c)
                        A[3 * r + c] += n[r] * n[c];
                b += d * n;
            }

            SymmetricEigenSolver solver = new SymmetricEigenSolver(3, 64);
            solver.Solve(A, SymmetricEigenSolver.SortType.Decreasing);
            double[] evals = solver.GetEigenvalues();
            Vector3d x = Vector3d.Zero;
            double maxEval = Math.Max(evals[0], MathUtil.Epsilon);
            for (int c = 0; c < 3; ++c) {
                if (evals[c] < QEFEigenTolerance * maxEval)
                    continue;
                double[] ev = solver.GetEigenvector(c);
                Vector3d e = new Vector3d(ev[0], ev[1], ev[2]);
                x += (e.Dot(b) / evals[c]) * e;
            }
            // the fallback for resolving self-intersections: on the dominant plane only (rank 1), so the
            // vertex stays on one of the input surfaces
            {
                double[] ev0 = solver.GetEigenvector(0);
                Vector3d e0 = new Vector3d(ev0[0], ev0[1], ev0[2]);
                Vector3d x1 = (evals[0] > MathUtil.Epsilon) ? (e0.Dot(b) / evals[0]) * e0 : Vector3d.Zero;
                Vector3d cmin = cellMin + QEFCellMargin * cellSize * Vector3d.One, cmax = cellMin + (1 - QEFCellMargin) * cellSize * Vector3d.One;
                massPoint = mass + x1;
                planeNormal = (evals[0] > MathUtil.Epsilon) ? e0.Normalized : Vector3d.Zero;
                for (int c = 0; c < 3; ++c)
                    massPoint[c] = MathUtil.Clamp(massPoint[c], cmin[c], cmax[c]);
            }
            int rank = 0;
            for (int c = 0; c < 3; ++c)
                if (evals[c] >= QEFEigenTolerance * maxEval) rank++;
            isFeature = rank >= 2;

            // the vertex must stay in the cell, otherwise faces can fold over. Clamping the solution per
            // coordinate moves it off the planes (a corner of the input outside the cell would pull the
            // vertex off the edge leading to it): minimize the QEF over the cell instead. It is convex, so
            // the minimum is the best feasible one of the unconstrained solution and the solutions with
            // 1, 2 or 3 coordinates fixed at the cell bounds (faces, edges, corners of the cell).
            // (slightly inside: vertices of neighbouring cells on their common face would coincide)
            double margin = QEFCellMargin * cellSize;
            Vector3d lo = cellMin - mass + margin * Vector3d.One, hi = lo + (cellSize - 2 * margin) * Vector3d.One;
            double eps = 1e-9 * cellSize;
            if (inside(x, lo, hi, eps))
                return mass + x;
            double[] Am = A;
            double evalTol = QEFEigenTolerance * maxEval;
            Vector3d best = Vector3d.Zero;
            double bestErr = double.MaxValue;
            // fixed[c]: -1 free, 0 at lo, 1 at hi
            int[] fixedState = new int[3];
            for (int code = 1; code < 27; ++code) {
                int t = code;
                for (int c = 0; c < 3; ++c) { fixedState[c] = (t % 3) - 1; t /= 3; }
                Vector3d y;
                if (solve_restricted(Am, b, fixedState, lo, hi, evalTol, out y) == false || inside(y, lo, hi, eps) == false)
                    continue;
                double err = qef_error(Am, b, y);
                if (err < bestErr - 1e-15 * (1 + Math.Abs(err)) || (Math.Abs(err - bestErr) <= 1e-15 * (1 + Math.Abs(err)) && y.LengthSquared < best.LengthSquared)) {
                    bestErr = err; best = y;
                }
            }
            if (bestErr == double.MaxValue) {
                for (int c = 0; c < 3; ++c)
                    best[c] = MathUtil.Clamp(x[c], lo[c], hi[c]);
            }
            return mass + best;
        }

        static bool inside(Vector3d y, Vector3d lo, Vector3d hi, double eps)
        {
            return y.x >= lo.x - eps && y.x <= hi.x + eps && y.y >= lo.y - eps && y.y <= hi.y + eps && y.z >= lo.z - eps && y.z <= hi.z + eps;
        }

        // y^T A y - 2 b.y (the QEF up to a constant)
        static double qef_error(double[] A, Vector3d b, Vector3d y)
        {
            double e = 0;
            for (int r = 0; r < 3; ++r)
                for (int c = 0; c < 3; ++c)
                    e += y[r] * A[3 * r + c] * y[c];
            return e - 2 * b.Dot(y);
        }

        // minimize the QEF with the coordinates in fixedState (0 = lo, 1 = hi) fixed: A_FF y_F = b_F - A_FX y_X,
        // solved with a truncated eigen decomposition (directions the planes do not constrain stay at the
        // mass point, ie 0)
        static bool solve_restricted(double[] A, Vector3d b, int[] fixedState, Vector3d lo, Vector3d hi, double evalTol, out Vector3d y)
        {
            y = Vector3d.Zero;
            int[] free = new int[3];
            int nf = 0;
            for (int c = 0; c < 3; ++c) {
                if (fixedState[c] < 0) free[nf++] = c;
                else y[c] = (fixedState[c] == 0) ? lo[c] : hi[c];
            }
            if (nf == 0)
                return true;
            double[] r = new double[nf];
            for (int i = 0; i < nf; ++i) {
                int fi = free[i];
                double v = b[fi];
                for (int c = 0; c < 3; ++c)
                    if (fixedState[c] >= 0) v -= A[3 * fi + c] * y[c];
                r[i] = v;
            }
            if (nf == 1) {
                double a = A[3 * free[0] + free[0]];
                y[free[0]] = (a > evalTol) ? r[0] / a : 0;
                return true;
            }
            if (nf == 2) {
                double a = A[3 * free[0] + free[0]], c2 = A[3 * free[1] + free[1]], bb = A[3 * free[0] + free[1]];
                // eigen decomposition of [[a, bb], [bb, c2]]
                double tr = 0.5 * (a + c2), det = Math.Sqrt(Math.Max(0, 0.25 * (a - c2) * (a - c2) + bb * bb));
                double l1 = tr + det, l2 = tr - det;
                Vector2d e1 = (Math.Abs(bb) > 1e-300) ? new Vector2d(l1 - c2, bb).Normalized : (a >= c2 ? Vector2d.AxisX : Vector2d.AxisY);
                Vector2d e2 = new Vector2d(-e1.y, e1.x);
                Vector2d rv = new Vector2d(r[0], r[1]), sol = Vector2d.Zero;
                if (l1 > evalTol) sol += (e1.Dot(rv) / l1) * e1;
                if (l2 > evalTol) sol += (e2.Dot(rv) / l2) * e2;
                y[free[0]] = sol.x; y[free[1]] = sol.y;
                return true;
            }
            return false;       // all free: the unconstrained solution, handled by the caller
        }


        // split along the diagonal whose two triangles have the more similar normals; if that split
        // is rejected (would create a non-manifold edge), try the other diagonal
        void add_non_manifold_cells(Dictionary<long, Vector3i> cells, Cell[] polyCells)
        {
            foreach (Cell c in polyCells) {
                Vector3i coarse = fdiv(c.Min, F);
                if (F == 1 || c.Coarse)
                    cells[node_key(coarse)] = coarse;
            }
        }

        bool append_quad(int v0, int v1, int v2, int v3)
        {
            Vector3d p0 = Mesh.GetVertex(v0), p1 = Mesh.GetVertex(v1), p2 = Mesh.GetVertex(v2), p3 = Mesh.GetVertex(v3);
            double splitA = MathUtil.Normal(p0, p1, p2).Dot(MathUtil.Normal(p0, p2, p3));
            double splitB = MathUtil.Normal(p1, p2, p3).Dot(MathUtil.Normal(p1, p3, p0));
            bool preferA = splitA >= splitB;
            if (try_split(preferA ? v0 : v1, preferA ? v1 : v2, preferA ? v2 : v3, preferA ? v3 : v0))
                return true;
            if (try_split(preferA ? v1 : v0, preferA ? v2 : v1, preferA ? v3 : v2, preferA ? v0 : v3))
                return true;
            NonManifoldTriangles += 2;
            return false;
        }
        bool try_split(int a, int b, int c, int d)
        {
            int t0 = Mesh.AppendTriangle(a, b, c);
            if (t0 < 0)
                return false;
            int t1 = Mesh.AppendTriangle(a, c, d);
            if (t1 < 0) {
                Mesh.RemoveTriangle(t0, false, false);
                return false;
            }
            return true;
        }
    }
}
