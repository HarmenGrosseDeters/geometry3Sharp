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
    /// Notes:
    ///   - inside is where the field is greater than IsoValue (eg winding number); set InsideIsLess for SDFs
    ///   - plain DC is not guaranteed to be manifold: cells that the surface passes through more than once
    ///     get a single vertex, which can create non-manifold edges. Triangles that DMesh3 rejects for this
    ///     reason are counted in NonManifoldTriangles and left out (the result then has small holes).
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

        public bool ParallelCompute = true;
        public Func<bool> CancelF = () => { return false; };

        // outputs
        public DMesh3 Mesh;
        public int NonManifoldTriangles;
        public Vector3i CornerDimensions;


        public void Generate()
        {
            Vector3d size = Bounds.Diagonal;
            int ni = (int)Math.Ceiling(size.x / CellSize) + 1, nj = (int)Math.Ceiling(size.y / CellSize) + 1, nk = (int)Math.Ceiling(size.z / CellSize) + 1;
            CornerDimensions = new Vector3i(ni, nj, nk);
            origin = Bounds.Min;
            NI = ni; NJ = nj; NK = nk;

            sample_grid();
            if (CancelF()) return;

            // collect sign-changing edges; a crossing edge is (corner index, axis)
            List<Index2i> crossings = new List<Index2i>();
            for (int k = 0; k < nk; ++k) {
                for (int j = 0; j < nj; ++j) {
                    for (int i = 0; i < ni; ++i) {
                        int c = corner(i, j, k);
                        bool inside = is_inside(values[c]);
                        if (i + 1 < ni && inside != is_inside(values[c + 1])) crossings.Add(new Index2i(c, 0));
                        if (j + 1 < nj && inside != is_inside(values[c + ni])) crossings.Add(new Index2i(c, 1));
                        if (k + 1 < nk && inside != is_inside(values[c + ni * nj])) crossings.Add(new Index2i(c, 2));
                    }
                }
            }

            // Hermite data per crossing
            int NC = crossings.Count;
            Vector3d[] hpoints = new Vector3d[NC];
            Vector3d[] hnormals = new Vector3d[NC];
            Action<int> compute_hermite = (ci) => {
                Index2i e = crossings[ci];
                Vector3d pa = corner_pos(e.a), pb = corner_pos(e.a + axis_offset(e.b));
                double fa = values[e.a], fb = values[e.a + axis_offset(e.b)];
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
            Dictionary<int, int[]> cellCrossings = new Dictionary<int, int[]>();
            for (int ci = 0; ci < NC; ++ci) {
                Index2i e = crossings[ci];
                for (int q = 0; q < 4; ++q) {
                    int cell = edge_cell(e, q);
                    if (cell < 0) continue;
                    int[] l;
                    if (cellCrossings.TryGetValue(cell, out l) == false) {
                        l = new int[] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
                        cellCrossings[cell] = l;
                    }
                    l[edge_local_index(e.b, q)] = ci;
                }
            }

            // vertices: one per surface patch in each cell (manifold dual contouring). The patches are the
            // ones marching cubes would create in the cell; each gets its own QEF vertex, so thin walls
            // and gaps inside a cell do not collapse into one vertex and create non-manifold edges.
            int[] cellList = new int[cellCrossings.Count];
            cellCrossings.Keys.CopyTo(cellList, 0);
            int[][] cellEdgeGroup = new int[cellList.Length][];
            Vector3d[][] cellGroupPos = new Vector3d[cellList.Length][];
            Action<int> place_vertices = (idx) => {
                int cell = cellList[idx];
                int[] edgeCrossing = cellCrossings[cell];
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
            Dictionary<int, int[]> cellEdgeVertex = new Dictionary<int, int[]>(cellList.Length);
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
                Index2i e = crossings[ci];
                bool valid = true;
                for (int q = 0; q < 4 && valid; ++q) {
                    int cell = edge_cell(e, q);
                    if (cell < 0) { valid = false; break; }
                    quad[q] = cellEdgeVertex[cell][edge_local_index(e.b, q)];
                }
                if (valid == false)
                    continue;
                int v0 = quad[0], v1 = quad[1], v2 = quad[2], v3 = quad[3];
                // cells are ordered counter-clockwise around the +axis direction. The quad normal must point
                // from inside to outside, ie along +axis if the edge start corner is inside.
                if (is_inside(values[e.a]) == false) {
                    int tmp = v1; v1 = v3; v3 = tmp;
                }
                append_quad(v0, v1, v2, v3);
            }

            // safety net: closed results must have positive volume
            if (Mesh.TriangleCount > 0 && MeshMeasurements.VolumeArea(Mesh, Mesh.TriangleIndices(), Mesh.GetVertex).x < 0)
                Mesh.ReverseOrientation(false);
        }


        Vector3d origin;
        int NI, NJ, NK;
        float[] values;

        int corner(int i, int j, int k) { return i + NI * (j + NJ * k); }
        Vector3i corner_ijk(int c) { return new Vector3i(c % NI, (c / NI) % NJ, c / (NI * NJ)); }
        Vector3d corner_pos(int c) {
            Vector3i ijk = corner_ijk(c);
            return origin + CellSize * new Vector3d(ijk.x, ijk.y, ijk.z);
        }
        int axis_offset(int axis) { return axis == 0 ? 1 : (axis == 1 ? NI : NI * NJ); }
        bool is_inside(double f) { return InsideIsLess ? f < IsoValue : f > IsoValue; }

        // cell index = index of its min corner; -1 if the cell does not exist
        int cell_index(int i, int j, int k) {
            if (i < 0 || j < 0 || k < 0 || i >= NI - 1 || j >= NJ - 1 || k >= NK - 1)
                return -1;
            return corner(i, j, k);
        }

        // the 4 cells around a grid edge, counter-clockwise around the +axis direction (q = 0..3)
        static readonly int[] edge_du = { -1, 0, 0, -1 }, edge_dv = { -1, -1, 0, 0 };
        int edge_cell(Index2i e, int q) {
            Vector3i c = corner_ijk(e.a);
            int axis = e.b, u = (axis + 1) % 3, v = (axis + 2) % 3;
            c[u] += edge_du[q]; c[v] += edge_dv[q];
            return cell_index(c.x, c.y, c.z);
        }
        // index (0..11) of the grid edge inside the q'th cell around it: axis*4 + lu + 2*lv, where (lu,lv)
        // are the edge's local coordinates along the other two axes (u = axis+1, v = axis+2)
        static int edge_local_index(int axis, int q) {
            return axis * 4 + (-edge_du[q]) + 2 * (-edge_dv[q]);
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
        int cell_patches(int cell, int[] edgeCrossing, out int[] groups)
        {
            Vector3i c0 = corner_ijk(cell);
            bool[] inside = new bool[8];
            int[] parent = new int[8];
            for (int lc = 0; lc < 8; ++lc) {
                inside[lc] = is_inside(values[corner(c0.x + (lc & 1), c0.y + ((lc >> 1) & 1), c0.z + ((lc >> 2) & 1))]);
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
                        values[corner(i, j, k)] = (float)MathUtil.Clamp(f, -float.MaxValue / 2, float.MaxValue / 2);
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
        Vector3d solve_qef(int cell, List<int> edges, Vector3d[] points, Vector3d[] normals)
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
            Vector3d cmin = corner_pos(cell), cmax = cmin + CellSize * Vector3d.One;
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
