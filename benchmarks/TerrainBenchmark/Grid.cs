// Height grids and point clouds as written by fetch_data.py, and the regular grid TIN built from a grid.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using g3;

namespace TerrainBenchmark
{
    /// <summary>
    /// Regular height grid: node (i, j) at (X0 + i Dx, Y0 + j Dx), row j = 0 is the south row, NaN = no data.
    /// Its TIN splits each cell along the diagonal from node (i, j) to (i+1, j+1); Height() interpolates
    /// on that TIN, so it is the exact surface of BuildMesh().
    /// </summary>
    public class Grid
    {
        public double X0, Y0, Dx;
        public int NX, NY;
        public float[] Z;
        public string Name;

        public double this[int i, int j] { get { return Z[i + NX * j]; } }
        public double X(int i) { return X0 + i * Dx; }
        public double Y(int j) { return Y0 + j * Dx; }
        public double X1 { get { return X(NX - 1); } }
        public double Y1 { get { return Y(NY - 1); } }

        public static Grid Load(string path)
        {
            string stem = path.EndsWith(".json") ? path.Substring(0, path.Length - 5) : path;
            var meta = JsonDocument.Parse(File.ReadAllText(stem + ".json")).RootElement;
            var g = new Grid() {
                X0 = meta.GetProperty("x0").GetDouble(), Y0 = meta.GetProperty("y0").GetDouble(),
                Dx = meta.GetProperty("dx").GetDouble(), NX = meta.GetProperty("nx").GetInt32(),
                NY = meta.GetProperty("ny").GetInt32(), Name = Path.GetFileName(stem)
            };
            byte[] bytes = File.ReadAllBytes(stem + ".f32");
            g.Z = new float[g.NX * g.NY];
            Buffer.BlockCopy(bytes, 0, g.Z, 0, bytes.Length);
            return g;
        }

        /// <summary> the nodes inside the box [x0,x1] x [y0,y1] </summary>
        public Grid Window(double x0, double y0, double x1, double y1)
        {
            int i0 = Math.Max(0, (int)Math.Ceiling((x0 - X0) / Dx - 1e-9)), j0 = Math.Max(0, (int)Math.Ceiling((y0 - Y0) / Dx - 1e-9));
            int i1 = Math.Min(NX - 1, (int)Math.Floor((x1 - X0) / Dx + 1e-9)), j1 = Math.Min(NY - 1, (int)Math.Floor((y1 - Y0) / Dx + 1e-9));
            var g = new Grid() { X0 = X(i0), Y0 = Y(j0), Dx = Dx, NX = i1 - i0 + 1, NY = j1 - j0 + 1, Name = Name };
            g.Z = new float[g.NX * g.NY];
            for (int j = 0; j < g.NY; ++j)
                Array.Copy(Z, i0 + NX * (j0 + j), g.Z, g.NX * j, g.NX);
            return g;
        }

        /// <summary> height on the TIN at (x, y); NaN outside or in a cell with no data </summary>
        public double Height(double x, double y)
        {
            double fx = (x - X0) / Dx, fy = (y - Y0) / Dx;
            int i = (int)Math.Floor(fx), j = (int)Math.Floor(fy);
            if (i == NX - 1 && fx <= NX - 1 + 1e-9) i--;
            if (j == NY - 1 && fy <= NY - 1 + 1e-9) j--;
            if (i < 0 || j < 0 || i >= NX - 1 || j >= NY - 1)
                return double.NaN;
            fx -= i; fy -= j;
            double z00 = this[i, j], z10 = this[i + 1, j], z01 = this[i, j + 1], z11 = this[i + 1, j + 1];
            if (fx >= fy)
                return z00 + fx * (z10 - z00) + fy * (z11 - z10);
            return z00 + fy * (z01 - z00) + fx * (z11 - z01);
        }

        /// <summary>
        /// The grid TIN (two triangles per cell, counter-clockwise from above), translated by -origin.
        /// Cells with a no-data corner are left out. Optionally only the cells [ci0, ci1) x [cj0, cj1).
        /// </summary>
        public DMesh3 BuildMesh(Vector3d origin, int ci0 = 0, int cj0 = 0, int ci1 = -1, int cj1 = -1)
        {
            if (ci1 < 0) ci1 = NX - 1;
            if (cj1 < 0) cj1 = NY - 1;
            DMesh3 mesh = new DMesh3(MeshComponents.None);
            int w = ci1 - ci0 + 1;
            int[] vid = new int[w * (cj1 - cj0 + 1)];
            for (int j = cj0; j <= cj1; ++j)
                for (int i = ci0; i <= ci1; ++i) {
                    double z = this[i, j];
                    vid[(i - ci0) + w * (j - cj0)] = double.IsNaN(z) ? -1 : mesh.AppendVertex(new Vector3d(X(i), Y(j), z) - origin);
                }
            for (int j = cj0; j < cj1; ++j)
                for (int i = ci0; i < ci1; ++i) {
                    int k = (i - ci0) + w * (j - cj0);
                    int a = vid[k], b = vid[k + 1], c = vid[k + 1 + w], d = vid[k + w];
                    if (a < 0 || b < 0 || c < 0 || d < 0) continue;
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            return mesh;
        }
    }


    /// <summary> point cloud with LAS classes (fetch_data.py: .xyz.f64 + .cls.u8) </summary>
    public class PointCloud
    {
        public Vector3d[] P;
        public byte[] Class;

        public static PointCloud Load(string stem)
        {
            byte[] xyz = File.ReadAllBytes(stem + ".xyz.f64");
            byte[] cls = File.ReadAllBytes(stem + ".cls.u8");
            var pc = new PointCloud() { P = new Vector3d[cls.Length], Class = cls };
            double[] tmp = new double[3 * cls.Length];
            Buffer.BlockCopy(xyz, 0, tmp, 0, xyz.Length);
            for (int k = 0; k < cls.Length; ++k)
                pc.P[k] = new Vector3d(tmp[3 * k], tmp[3 * k + 1], tmp[3 * k + 2]);
            return pc;
        }

        public PointCloud Window(double x0, double y0, double x1, double y1)
        {
            var p = new List<Vector3d>(); var c = new List<byte>();
            for (int k = 0; k < P.Length; ++k)
                if (P[k].x >= x0 && P[k].x <= x1 && P[k].y >= y0 && P[k].y <= y1) { p.Add(P[k]); c.Add(Class[k]); }
            return new PointCloud() { P = p.ToArray(), Class = c.ToArray() };
        }
    }
}
