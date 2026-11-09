using System;

namespace g3
{
    /// <summary>
    /// Regular height grid (a DGM / DTM raster): node (i, j) lies at (X0 + i CellSize, Y0 + j CellSize), row j = 0 is
    /// the south row, <c>Z[i + NX j]</c> is its height, NaN = no data. The grid's TIN splits each cell along the
    /// diagonal from node (i, j) to node (i+1, j+1) (<see cref="BuildMesh"/>); <see cref="Height"/> interpolates on
    /// that TIN, so it is exactly the surface of the mesh. A cell with a no-data corner (NaN or infinite height) has no surface.
    /// </summary>
    public class HeightGrid
    {
        public double X0, Y0, CellSize;
        public int NX, NY;
        public double[] Z;

        public HeightGrid() { }

        public HeightGrid(double[] z, int nx, int ny, double x0, double y0, double cellSize)
        {
            if (z == null) throw new ArgumentNullException(nameof(z));
            if (nx < 0 || ny < 0 || (long)nx * ny != z.Length) throw new ArgumentException("z must have nx * ny elements");
            if (!(cellSize > 0) || double.IsInfinity(cellSize)) throw new ArgumentException("cellSize must be positive and finite");
            Z = z; NX = nx; NY = ny; X0 = x0; Y0 = y0; CellSize = cellSize;
        }

        public double this[int i, int j] { get { return Z[i + NX * j]; } set { Z[i + NX * j] = value; } }
        public double X(int i) { return X0 + i * CellSize; }
        public double Y(int j) { return Y0 + j * CellSize; }
        public double X1 { get { return X(NX - 1); } }
        public double Y1 { get { return Y(NY - 1); } }

        /// <summary> true if z is a height (finite); NaN and infinite values are no data </summary>
        public static bool IsData(double z) { return !double.IsNaN(z) && !double.IsInfinity(z); }

        /// <summary> number of nodes with data </summary>
        public int ValidNodeCount()
        {
            int n = 0;
            foreach (double z in Z) if (IsData(z)) ++n;
            return n;
        }

        /// <summary> the nodes inside the box [x0,x1] x [y0,y1] as a new grid </summary>
        public HeightGrid Window(double x0, double y0, double x1, double y1)
        {
            // (clamped as doubles before the cast: NaN or huge box coordinates give an empty window, not an undefined cast)
            double fi0 = Math.Ceiling((x0 - X0) / CellSize - 1e-9), fj0 = Math.Ceiling((y0 - Y0) / CellSize - 1e-9);
            double fi1 = Math.Floor((x1 - X0) / CellSize + 1e-9), fj1 = Math.Floor((y1 - Y0) / CellSize + 1e-9);
            if (double.IsNaN(fi0) || double.IsNaN(fj0) || double.IsNaN(fi1) || double.IsNaN(fj1) || fi0 > NX - 1 || fj0 > NY - 1 || fi1 < 0 || fj1 < 0)
                return new HeightGrid() { X0 = X0, Y0 = Y0, CellSize = CellSize, NX = 0, NY = 0, Z = new double[0] };
            int i0 = (int)Math.Max(0, fi0), j0 = (int)Math.Max(0, fj0);
            int i1 = (int)Math.Min(NX - 1, fi1), j1 = (int)Math.Min(NY - 1, fj1);
            int nx = Math.Max(0, i1 - i0 + 1), ny = Math.Max(0, j1 - j0 + 1);
            if (nx == 0 || ny == 0)
                return new HeightGrid() { X0 = X(i0), Y0 = Y(j0), CellSize = CellSize, NX = 0, NY = 0, Z = new double[0] };
            var g = new HeightGrid() { X0 = X(i0), Y0 = Y(j0), CellSize = CellSize, NX = nx, NY = ny, Z = new double[nx * ny] };
            for (int j = 0; j < ny; ++j)
                Array.Copy(Z, i0 + NX * (j0 + j), g.Z, nx * j, nx);
            return g;
        }

        /// <summary>
        /// Height of the grid TIN at (x, y): linear on the triangle (i,j) (i+1,j) (i+1,j+1) where fx >= fy and on
        /// (i,j) (i+1,j+1) (i,j+1) otherwise (fx, fy: the position in the cell). The boundary of the grid belongs to
        /// the last cells. NaN outside the grid or in a cell with a no-data corner.
        /// </summary>
        public double Height(double x, double y)
        {
            double fx = (x - X0) / CellSize, fy = (y - Y0) / CellSize;
            if (double.IsNaN(fx) || double.IsNaN(fy)) return double.NaN;
            if (fx < 0 || fy < 0 || fx > NX - 1 || fy > NY - 1 || NX < 2 || NY < 2) return double.NaN;
            int i = (int)Math.Floor(fx), j = (int)Math.Floor(fy);
            if (i == NX - 1) i--;
            if (j == NY - 1) j--;
            fx -= i; fy -= j;
            double z00 = this[i, j], z10 = this[i + 1, j], z01 = this[i, j + 1], z11 = this[i + 1, j + 1];
            if (!IsData(z00) || !IsData(z10) || !IsData(z01) || !IsData(z11))
                return double.NaN;
            if (fx >= fy)
                return z00 + fx * (z10 - z00) + fy * (z11 - z10);
            return z00 + fy * (z01 - z00) + fx * (z11 - z01);
        }

        /// <summary>
        /// The grid TIN: two triangles per cell (counter-clockwise seen from above, fixed diagonal from node (i, j) to
        /// (i+1, j+1)), vertices at (X(i), Y(j), Z) translated by -origin. Cells with a no-data corner are left out,
        /// and nodes that no remaining cell uses get no vertex. Optionally only the cells [ci0, ci1) x [cj0, cj1)
        /// (negative ci1 / cj1: up to the last cell). The mesh surface equals <see cref="Height"/> (up to rounding of
        /// the translation).
        /// </summary>
        public DMesh3 BuildMesh(Vector3d origin = default(Vector3d), int ci0 = 0, int cj0 = 0, int ci1 = -1, int cj1 = -1)
        {
            if (ci1 < 0) ci1 = NX - 1;
            if (cj1 < 0) cj1 = NY - 1;
            ci0 = Math.Max(ci0, 0); cj0 = Math.Max(cj0, 0);
            ci1 = Math.Min(ci1, NX - 1); cj1 = Math.Min(cj1, NY - 1);
            DMesh3 mesh = new DMesh3(MeshComponents.None);
            if (ci1 <= ci0 || cj1 <= cj0) return mesh;
            int w = ci1 - ci0 + 1;
            int[] vid = new int[w * (cj1 - cj0 + 1)];
            for (int k = 0; k < vid.Length; ++k) vid[k] = -1;
            for (int j = cj0; j < cj1; ++j)
                for (int i = ci0; i < ci1; ++i) {
                    if (!IsData(this[i, j]) || !IsData(this[i + 1, j]) || !IsData(this[i + 1, j + 1]) || !IsData(this[i, j + 1]))
                        continue;
                    int a = Node(mesh, vid, i, j, ci0, cj0, w, origin), b = Node(mesh, vid, i + 1, j, ci0, cj0, w, origin);
                    int c = Node(mesh, vid, i + 1, j + 1, ci0, cj0, w, origin), d = Node(mesh, vid, i, j + 1, ci0, cj0, w, origin);
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            return mesh;
        }

        int Node(DMesh3 mesh, int[] vid, int i, int j, int ci0, int cj0, int w, Vector3d origin)
        {
            int k = (i - ci0) + w * (j - cj0);
            if (vid[k] < 0)
                vid[k] = mesh.AppendVertex(new Vector3d(X(i), Y(j), this[i, j]) - origin);
            return vid[k];
        }
    }
}
