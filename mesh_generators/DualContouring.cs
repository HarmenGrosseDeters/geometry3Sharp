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

            // gather Hermite data per cell (each crossing edge touches 4 cells)
            Dictionary<int, List<int>> cellEdges = new Dictionary<int, List<int>>();
            for (int ci = 0; ci < NC; ++ci) {
                foreach (int cell in edge_cells(crossings[ci])) {
                    if (cell < 0) continue;
                    List<int> l;
                    if (cellEdges.TryGetValue(cell, out l) == false) {
                        l = new List<int>();
                        cellEdges[cell] = l;
                    }
                    l.Add(ci);
                }
            }

            // one vertex per cell from the QEF
            Mesh = new DMesh3();
            Dictionary<int, int> cellVertex = new Dictionary<int, int>();
            foreach (var pair in cellEdges)
                cellVertex[pair.Key] = Mesh.AppendVertex(solve_qef(pair.Key, pair.Value, hpoints, hnormals));
            if (CancelF()) return;

            // one quad per crossing edge, connecting the vertices of its 4 cells
            NonManifoldTriangles = 0;
            int[] cells = new int[4];
            for (int ci = 0; ci < NC; ++ci) {
                Index2i e = crossings[ci];
                int n = 0;
                foreach (int cell in edge_cells(e))
                    cells[n++] = cell;
                if (cells[0] < 0 || cells[1] < 0 || cells[2] < 0 || cells[3] < 0)
                    continue;
                int v0 = cellVertex[cells[0]], v1 = cellVertex[cells[1]], v2 = cellVertex[cells[2]], v3 = cellVertex[cells[3]];
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

        // the 4 cells around a grid edge, counter-clockwise around the +axis direction
        IEnumerable<int> edge_cells(Index2i e) {
            Vector3i c = corner_ijk(e.a);
            int axis = e.b, u = (axis + 1) % 3, v = (axis + 2) % 3;
            int[] du = { -1, 0, 0, -1 }, dv = { -1, -1, 0, 0 };
            for (int q = 0; q < 4; ++q) {
                Vector3i cc = c;
                cc[u] += du[q]; cc[v] += dv[q];
                yield return cell_index(cc.x, cc.y, cc.z);
            }
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


        // split along the diagonal whose two triangles have the more similar normals
        void append_quad(int v0, int v1, int v2, int v3)
        {
            Vector3d p0 = Mesh.GetVertex(v0), p1 = Mesh.GetVertex(v1), p2 = Mesh.GetVertex(v2), p3 = Mesh.GetVertex(v3);
            double splitA = MathUtil.Normal(p0, p1, p2).Dot(MathUtil.Normal(p0, p2, p3));
            double splitB = MathUtil.Normal(p1, p2, p3).Dot(MathUtil.Normal(p1, p3, p0));
            if (splitA >= splitB) {
                append_tri(v0, v1, v2); append_tri(v0, v2, v3);
            } else {
                append_tri(v1, v2, v3); append_tri(v1, v3, v0);
            }
        }
        void append_tri(int a, int b, int c)
        {
            int tid = Mesh.AppendTriangle(a, b, c);
            if (tid < 0)
                NonManifoldTriangles++;
        }
    }
}
