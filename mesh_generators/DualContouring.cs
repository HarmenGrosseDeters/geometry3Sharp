// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
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

        /// <summary> sparse mode: cells to contour, in grid cell coordinates (see class comment) </summary>
        public IEnumerable<Vector3i> CandidateCells = null;
        /// <summary> sparse mode: field value at grid node (i,j,k). Called once per node. Must be thread-safe if ParallelCompute. </summary>
        public Func<Vector3i, double> NodeValueF = null;

        /// <summary>
        /// Sparse mode, optional: called for grid edges whose ends are both inside; return true if the surface
        /// leaves and re-enters along the edge (a gap narrower than a cell, invisible to the node signs).
        /// The cells there merge both sides of the gap, which can make the surfaces intersect.
        /// Counted in HiddenGapEdges; refining the grid until there are none avoids this.
        /// </summary>
        public Func<Vector3d, Vector3d, bool> HiddenGapF = null;

        public bool ParallelCompute = true;
        public Func<bool> CancelF = () => { return false; };

        // outputs
        public DMesh3 Mesh;
        public int NonManifoldTriangles;
        public int HiddenGapEdges;
        public Vector3i CornerDimensions;


        public void Generate()
        {
            origin = Bounds.Min;
            sparse = (CandidateCells != null);
            if (sparse) {
                if (NodeValueF == null)
                    throw new Exception("DualContouring: sparse mode requires NodeValueF");
                sparseValues = new System.Collections.Concurrent.ConcurrentDictionary<long, float>();
            } else {
                Vector3d size = Bounds.Diagonal;
                NI = (int)Math.Ceiling(size.x / CellSize) + 1; NJ = (int)Math.Ceiling(size.y / CellSize) + 1; NK = (int)Math.Ceiling(size.z / CellSize) + 1;
                CornerDimensions = new Vector3i(NI, NJ, NK);
                sample_grid();
            }
            if (CancelF()) return;

            List<GridEdge> crossings = find_crossings();
            if (CancelF()) return;

            // Hermite data per crossing
            int NC = crossings.Count;
            Vector3d[] hpoints = new Vector3d[NC];
            Vector3d[] hnormals = new Vector3d[NC];
            Action<int> compute_hermite = (ci) => {
                GridEdge e = crossings[ci];
                Vector3i nb = e.End;
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
            if (CancelF()) return;

            // gather crossings per cell, by local cube edge (each crossing edge touches 4 cells)
            Dictionary<long, int[]> cellCrossings = new Dictionary<long, int[]>();
            Dictionary<long, Vector3i> cellCoords = new Dictionary<long, Vector3i>();
            for (int ci = 0; ci < NC; ++ci) {
                GridEdge e = crossings[ci];
                for (int q = 0; q < 4; ++q) {
                    Vector3i cell;
                    if (edge_cell(e, q, out cell) == false) continue;
                    long key = node_key(cell);
                    int[] l;
                    if (cellCrossings.TryGetValue(key, out l) == false) {
                        l = new int[] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
                        cellCrossings[key] = l;
                        cellCoords[key] = cell;
                    }
                    l[edge_local_index(e.Axis, q)] = ci;
                }
            }

            // vertices: one per surface patch in each cell (manifold dual contouring). The patches are the
            // ones marching cubes would create in the cell; each gets its own QEF vertex, so thin walls
            // and gaps inside a cell do not collapse into one vertex and create non-manifold edges.
            long[] cellList = new long[cellCrossings.Count];
            cellCrossings.Keys.CopyTo(cellList, 0);
            Array.Sort(cellList);       // deterministic vertex order
            int[][] cellEdgeGroup = new int[cellList.Length][];
            Vector3d[][] cellGroupPos = new Vector3d[cellList.Length][];
            Action<int> place_vertices = (idx) => {
                Vector3i cell = cellCoords[cellList[idx]];
                int[] edgeCrossing = cellCrossings[cellList[idx]];
                int[] groups;
                int nGroups = cell_patches(cell, edgeCrossing, out groups);
                Vector3d[] pos = new Vector3d[nGroups];
                List<int> edges = new List<int>(12);
                for (int g = 0; g < nGroups; ++g) {
                    edges.Clear();
                    for (int le = 0; le < 12; ++le)
                        if (edgeCrossing[le] >= 0 && groups[le] == g)
                            edges.Add(edgeCrossing[le]);
                    pos[g] = solve_qef(cell, edges, hpoints, hnormals);
                }
                cellEdgeGroup[idx] = groups;
                cellGroupPos[idx] = pos;
            };
            if (ParallelCompute)
                gParallel.ForEach(Interval1i.Range(cellList.Length), place_vertices);
            else
                for (int idx = 0; idx < cellList.Length; ++idx) place_vertices(idx);
            if (CancelF()) return;

            Mesh = new DMesh3();
            Dictionary<long, int[]> cellEdgeVertex = new Dictionary<long, int[]>(cellList.Length);
            for (int idx = 0; idx < cellList.Length; ++idx) {
                int[] groupVertex = new int[cellGroupPos[idx].Length];
                for (int g = 0; g < groupVertex.Length; ++g)
                    groupVertex[g] = Mesh.AppendVertex(cellGroupPos[idx][g]);
                int[] edgeVertex = new int[12];
                for (int le = 0; le < 12; ++le)
                    edgeVertex[le] = (cellEdgeGroup[idx][le] >= 0) ? groupVertex[cellEdgeGroup[idx][le]] : -1;
                cellEdgeVertex[cellList[idx]] = edgeVertex;
            }

            // one quad per crossing edge, connecting the vertices of its patches in the 4 cells around it
            NonManifoldTriangles = 0;
            int[] quad = new int[4];
            for (int ci = 0; ci < NC; ++ci) {
                GridEdge e = crossings[ci];
                bool valid = true;
                for (int q = 0; q < 4 && valid; ++q) {
                    Vector3i cell;
                    if (edge_cell(e, q, out cell) == false) { valid = false; break; }
                    quad[q] = cellEdgeVertex[node_key(cell)][edge_local_index(e.Axis, q)];
                }
                if (valid == false)
                    continue;
                int v0 = quad[0], v1 = quad[1], v2 = quad[2], v3 = quad[3];
                // cells are ordered counter-clockwise around the +axis direction. The quad normal must point
                // from inside to outside, ie along +axis if the edge start corner is inside.
                if (is_inside(node_value(e.Start)) == false) {
                    int tmp = v1; v1 = v3; v3 = tmp;
                }
                append_quad(v0, v1, v2, v3);
            }

            // safety net: closed results must have positive volume
            if (Mesh.TriangleCount > 0 && MeshMeasurements.VolumeArea(Mesh, Mesh.TriangleIndices(), Mesh.GetVertex).x < 0)
                Mesh.ReverseOrientation(false);
        }


        // grid edge from node Start along +Axis
        struct GridEdge
        {
            public Vector3i Start;
            public int Axis;
            public GridEdge(Vector3i start, int axis) { Start = start; Axis = axis; }
            public Vector3i End { get { Vector3i e = Start; e[Axis] += 1; return e; } }
        }

        Vector3d origin;
        int NI, NJ, NK;
        float[] values;
        bool sparse;
        System.Collections.Concurrent.ConcurrentDictionary<long, float> sparseValues;

        // node keys support coordinates in [-2^20, 2^20)
        const int KeyBias = 1 << 20;
        static long node_key(Vector3i n) {
            return (long)(n.x + KeyBias) | ((long)(n.y + KeyBias) << 21) | ((long)(n.z + KeyBias) << 42);
        }

        Vector3d node_pos(Vector3i n) { return origin + CellSize * new Vector3d(n.x, n.y, n.z); }
        bool is_inside(double f) { return InsideIsLess ? f < IsoValue : f > IsoValue; }

        double node_value(Vector3i n)
        {
            if (sparse == false)
                return values[n.x + NI * (n.y + NJ * n.z)];
            long key = node_key(n);
            float value;
            if (sparseValues.TryGetValue(key, out value) == false) {
                value = (float)MathUtil.Clamp(NodeValueF(n), -float.MaxValue / 2, float.MaxValue / 2);
                sparseValues[key] = value;
            }
            return value;
        }

        // does cell (i,j,k) exist: dense mode inside the grid, sparse mode always
        bool cell_exists(Vector3i c) {
            if (sparse)
                return true;
            return c.x >= 0 && c.y >= 0 && c.z >= 0 && c.x < NI - 1 && c.y < NJ - 1 && c.z < NK - 1;
        }


        List<GridEdge> find_crossings()
        {
            List<GridEdge> crossings = new List<GridEdge>();
            if (sparse == false) {
                for (int k = 0; k < NK; ++k) {
                    for (int j = 0; j < NJ; ++j) {
                        for (int i = 0; i < NI; ++i) {
                            int c = i + NI * (j + NJ * k);
                            bool inside = is_inside(values[c]);
                            if (i + 1 < NI && inside != is_inside(values[c + 1])) crossings.Add(new GridEdge(new Vector3i(i, j, k), 0));
                            if (j + 1 < NJ && inside != is_inside(values[c + NI])) crossings.Add(new GridEdge(new Vector3i(i, j, k), 1));
                            if (k + 1 < NK && inside != is_inside(values[c + NI * NJ])) crossings.Add(new GridEdge(new Vector3i(i, j, k), 2));
                        }
                    }
                }
                return crossings;
            }

            // sparse: the 12 edges of each candidate cell. Evaluate the cell corners in parallel first.
            List<Vector3i> cells = new List<Vector3i>(CandidateCells);
            Action<int> eval_corners = (idx) => {
                for (int lc = 0; lc < 8; ++lc)
                    node_value(cells[idx] + new Vector3i(lc & 1, (lc >> 1) & 1, (lc >> 2) & 1));
            };
            if (ParallelCompute)
                gParallel.ForEach(Interval1i.Range(cells.Count), eval_corners);
            else
                for (int idx = 0; idx < cells.Count; ++idx) eval_corners(idx);

            HashSet<long> seen = new HashSet<long>();
            List<GridEdge> insideEdges = new List<GridEdge>();
            foreach (Vector3i cell in cells) {
                for (int le = 0; le < 12; ++le) {
                    Index2i ce = cube_edge_corners[le];
                    Vector3i a = cell + local_offset(ce.a);
                    int axis = le / 4;
                    if (seen.Add(node_key(a) * 3 + axis) == false)
                        continue;
                    bool aInside = is_inside(node_value(a));
                    if (aInside != is_inside(node_value(cell + local_offset(ce.b))))
                        crossings.Add(new GridEdge(a, axis));
                    else if (aInside && HiddenGapF != null)
                        insideEdges.Add(new GridEdge(a, axis));
                }
            }
            HiddenGapEdges = 0;
            if (insideEdges.Count > 0) {
                Action<int> test_gap = (i) => {
                    if (HiddenGapF(node_pos(insideEdges[i].Start), node_pos(insideEdges[i].End)))
                        System.Threading.Interlocked.Increment(ref HiddenGapEdges);
                };
                if (ParallelCompute)
                    gParallel.ForEach(Interval1i.Range(insideEdges.Count), test_gap);
                else
                    for (int i = 0; i < insideEdges.Count; ++i) test_gap(i);
            }
            // deterministic order
            crossings.Sort((x, y) => { int c = node_key(x.Start).CompareTo(node_key(y.Start)); return c != 0 ? c : x.Axis.CompareTo(y.Axis); });
            return crossings;
        }


        // the 4 cells around a grid edge, counter-clockwise around the +axis direction (q = 0..3)
        static readonly int[] edge_du = { -1, 0, 0, -1 }, edge_dv = { -1, -1, 0, 0 };
        bool edge_cell(GridEdge e, int q, out Vector3i cell) {
            cell = e.Start;
            int u = (e.Axis + 1) % 3, v = (e.Axis + 2) % 3;
            cell[u] += edge_du[q]; cell[v] += edge_dv[q];
            return cell_exists(cell);
        }
        // index (0..11) of the grid edge inside the q'th cell around it: axis*4 + lu + 2*lv, where (lu,lv)
        // are the edge's local coordinates along the other two axes (u = axis+1, v = axis+2)
        static int edge_local_index(int axis, int q) {
            return axis * 4 + (-edge_du[q]) + 2 * (-edge_dv[q]);
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
        // a pair (inside component, outside component). Returns the patch count; groups[le] is the patch of
        // local edge le, or -1 if it has no crossing.
        int cell_patches(Vector3i cell, int[] edgeCrossing, out int[] groups)
        {
            bool[] inside = new bool[8];
            int[] parent = new int[8];
            for (int lc = 0; lc < 8; ++lc) {
                inside[lc] = is_inside(node_value(cell + local_offset(lc)));
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
            int[] keys = new int[12];
            int nGroups = 0;
            for (int le = 0; le < 12; ++le) {
                groups[le] = -1;
                if (edgeCrossing[le] < 0)
                    continue;
                Index2i ce = cube_edge_corners[le];
                int cin = inside[ce.a] ? ce.a : ce.b, cout = inside[ce.a] ? ce.b : ce.a;
                int key = find(parent, cin) * 8 + find(parent, cout);
                int g = Array.IndexOf(keys, key, 0, nGroups);
                if (g < 0) {
                    g = nGroups++;
                    keys[g] = key;
                }
                groups[le] = g;
            }
            return nGroups;
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
        Vector3d solve_qef(Vector3i cell, List<int> edges, Vector3d[] points, Vector3d[] normals)
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
            Vector3d result = mass + x;

            // clamp to the cell, otherwise faces can fold over
            Vector3d cmin = node_pos(cell), cmax = cmin + CellSize * Vector3d.One;
            for (int c = 0; c < 3; ++c)
                result[c] = MathUtil.Clamp(result[c], cmin[c], cmax[c]);
            return result;
        }


        // split along the diagonal whose two triangles have the more similar normals; if that split
        // is rejected (would create a non-manifold edge), try the other diagonal
        void append_quad(int v0, int v1, int v2, int v3)
        {
            Vector3d p0 = Mesh.GetVertex(v0), p1 = Mesh.GetVertex(v1), p2 = Mesh.GetVertex(v2), p3 = Mesh.GetVertex(v3);
            double splitA = MathUtil.Normal(p0, p1, p2).Dot(MathUtil.Normal(p0, p2, p3));
            double splitB = MathUtil.Normal(p1, p2, p3).Dot(MathUtil.Normal(p1, p3, p0));
            bool preferA = splitA >= splitB;
            if (try_split(preferA ? v0 : v1, preferA ? v1 : v2, preferA ? v2 : v3, preferA ? v3 : v0))
                return;
            if (try_split(preferA ? v1 : v0, preferA ? v2 : v1, preferA ? v3 : v2, preferA ? v0 : v3))
                return;
            NonManifoldTriangles += 2;
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
