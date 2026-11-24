// PDD research probe (candidate #11 of docs/research/UNTAPPED_SCIENCE_GEOMETRY_2026-11.md). Research only: no library code.
// Pointwise Distance Distributions after Widdowson & Kurlin (arXiv:2108.04798, NeurIPS 2022 / SIAM J. Appl. Math.),
// implemented from the paper's definitions; no reference code read. See docs/research/PDD_PROBE_2026-11.md.
//
//   dotnet run --project benchmarks/PddProbe -c Release -- disc <N>        discrimination: rigid copies vs deformed copies
//   dotnet run --project benchmarks/PddProbe -c Release -- retrieval      "same shape?" pre-check on a small corpus
//   dotnet run --project benchmarks/PddProbe -c Release -- time           cost per shape at 10k-200k points
//   dotnet run --project benchmarks/PddProbe -c Release -- determinism    threads, runs, permutation, similarities
//   dotnet run --project benchmarks/PddProbe -c Release -- period         period / symmetry on TpmsLattice vertices
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using g3;

static class Program
{
    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string mode = args.Length > 0 ? args[0] : "disc";
        switch (mode) {
            case "disc": Experiments.Discrimination(args.Length > 1 ? int.Parse(args[1]) : 300); break;
            case "retrieval": Experiments.Retrieval(args.Length > 1 ? int.Parse(args[1]) : 2000); break;
            case "time": Experiments.Timing(); break;
            case "determinism": Experiments.Determinism(); break;
            case "period": Experiments.Period(); break;
            case "moments": Experiments.Moments(); break;
            default: Console.WriteLine("modes: disc [N] | retrieval [N] | time | determinism | period"); return 1;
        }
        return 0;
    }
}

/// <summary> SplitMix64: a fixed, platform-independent generator (System.Random's algorithm is not part of its contract). </summary>
struct Rng
{
    ulong s;
    public Rng(ulong seed) { s = seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL; }
    public ulong Next() { ulong z = (s += 0x9E3779B97F4A7C15UL); z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }
    public double U() => (Next() >> 11) * (1.0 / (1UL << 53));
    public double Gauss() { double u1 = 1.0 - U(), u2 = U(); return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }
}

static class Pdd
{
    /// <summary>
    /// The k nearest-neighbour distances of every point (row i ascending), N*k row-major. A uniform grid, rings of cells
    /// searched outwards until the k-th distance is inside the searched radius. Each row depends only on the multiset of
    /// distances from p_i (computed as sqrt((qx-px)^2+(qy-py)^2+(qz-pz)^2), fixed order), not on the search order or the
    /// thread, so the result is bit-identical for any thread count and any input order.
    /// </summary>
    public static double[] Knn(Vector3d[] P, int k, int threads)
    {
        int n = P.Length;
        if (k >= n) throw new ArgumentException("k >= n");
        var D = new double[(long)n * k];
        if (k * 4 >= n) {   // brute force for k near n (the full distance matrix)
            Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = threads }, i => {
                var row = new double[n - 1]; int c = 0;
                for (int j = 0; j < n; ++j) if (j != i) row[c++] = D2(P[i], P[j]);
                Array.Sort(row);
                for (int t = 0; t < k; ++t) D[(long)i * k + t] = Math.Sqrt(row[t]);
            });
            return D;
        }
        var box = new AxisAlignedBox3d(P[0], P[0]);
        foreach (var p in P) box.Contain(p);
        double ext = Math.Max(box.Width, Math.Max(box.Height, box.Depth));
        double ex = Math.Max(box.Width, ext * 1e-3), ey = Math.Max(box.Height, ext * 1e-3), ez = Math.Max(box.Depth, ext * 1e-3);
        double cell = Math.Cbrt(ex * ey * ez / Math.Max(1, n / 2.0));
        int nx = Math.Max(1, (int)Math.Ceiling(ex / cell)), ny = Math.Max(1, (int)Math.Ceiling(ey / cell)), nz = Math.Max(1, (int)Math.Ceiling(ez / cell));
        Vector3d mn = box.Min;
        int Cx(double v) => Math.Min(nx - 1, Math.Max(0, (int)((v - mn.x) / cell)));
        int Cy(double v) => Math.Min(ny - 1, Math.Max(0, (int)((v - mn.y) / cell)));
        int Cz(double v) => Math.Min(nz - 1, Math.Max(0, (int)((v - mn.z) / cell)));
        int ncell = nx * ny * nz;
        var start = new int[ncell + 1]; var cid = new int[n];
        for (int i = 0; i < n; ++i) { cid[i] = (Cz(P[i].z) * ny + Cy(P[i].y)) * nx + Cx(P[i].x); start[cid[i] + 1]++; }
        for (int c = 0; c < ncell; ++c) start[c + 1] += start[c];
        var fill = (int[])start.Clone(); var idx = new int[n];
        for (int i = 0; i < n; ++i) idx[fill[cid[i]]++] = i;
        int maxr = Math.Max(nx, Math.Max(ny, nz));
        Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = threads }, () => new double[k], (i, _, best) => {
            int cnt = 0; Vector3d p = P[i];
            int ix = Cx(p.x), iy = Cy(p.y), iz = Cz(p.z);
            for (int r = 0; r <= maxr; ++r) {
                for (int dz = -r; dz <= r; ++dz) {
                    int z = iz + dz; if (z < 0 || z >= nz) continue;
                    for (int dy = -r; dy <= r; ++dy) {
                        int y = iy + dy; if (y < 0 || y >= ny) continue;
                        bool edge = Math.Abs(dz) == r || Math.Abs(dy) == r;
                        for (int dx = -r; dx <= r; dx += (edge || r == 0) ? 1 : 2 * r) {
                            int x = ix + dx; if (x < 0 || x >= nx) continue;
                            int c = (z * ny + y) * nx + x;
                            for (int s = start[c]; s < start[c + 1]; ++s) {
                                int j = idx[s]; if (j == i) continue;
                                double d = D2(p, P[j]);
                                if (cnt < k) { int t = cnt++; while (t > 0 && best[t - 1] > d) { best[t] = best[t - 1]; --t; } best[t] = d; }
                                else if (d < best[k - 1]) { int t = k - 1; while (t > 0 && best[t - 1] > d) { best[t] = best[t - 1]; --t; } best[t] = d; }
                            }
                            if (r == 0) break;
                        }
                    }
                }
                double rr = r * cell;
                if (cnt == k && best[k - 1] <= rr * rr) break;
            }
            for (int t = 0; t < k; ++t) D[(long)i * k + t] = Math.Sqrt(best[t]);
            return best;
        }, _ => { });
        return D;
    }

    static double D2(Vector3d a, Vector3d b) { double dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z; return dx * dx + dy * dy + dz * dz; }

    /// <summary> sum of the values in ascending order: independent of the input order, exact under 2^k scaling </summary>
    public static double SortedSum(double[] v) { var c = (double[])v.Clone(); Array.Sort(c); double s = 0; foreach (var x in c) s += x; return s; }

    /// <summary> RMS radius about the centroid, order independent (sorted sums), exactly covariant under 2^k scaling </summary>
    public static double RmsRadius(Vector3d[] P, out Vector3d centroid)
    {
        int n = P.Length;
        centroid = new Vector3d(SortedSum(P.Select(p => p.x).ToArray()) / n, SortedSum(P.Select(p => p.y).ToArray()) / n, SortedSum(P.Select(p => p.z).ToArray()) / n);
        var c = centroid;
        return Math.Sqrt(SortedSum(P.Select(p => (p - c).LengthSquared).ToArray()) / n);
    }

    public static double[] Normalize(double[] D, double s) { var r = new double[D.Length]; for (long i = 0; i < D.Length; ++i) r[i] = D[i] / s; return r; }

    /// <summary> AMD: the column means (sorted sums) </summary>
    public static double[] Amd(double[] D, int n, int k)
    {
        var a = new double[k]; var col = new double[n];
        for (int j = 0; j < k; ++j) { for (int i = 0; i < n; ++i) col[i] = D[(long)i * k + j]; a[j] = SortedSum(col) / n; }
        return a;
    }

    /// <summary> columns sorted ascending, column-major: the 1D marginals for the column-wise W1 bound </summary>
    public static double[][] SortedColumns(double[] D, int n, int k)
    {
        var cols = new double[k][];
        for (int j = 0; j < k; ++j) { var c = new double[n]; for (int i = 0; i < n; ++i) c[i] = D[(long)i * k + j]; Array.Sort(c); cols[j] = c; }
        return cols;
    }

    /// <summary> AMD from the sorted columns: the same sorted sums as Amd, bit for bit, without sorting again </summary>
    public static double[] AmdFromCols(double[][] cols) { var a = new double[cols.Length]; for (int j = 0; j < cols.Length; ++j) { double s = 0; foreach (var x in cols[j]) s += x; a[j] = s / cols[j].Length; } return a; }

    public static double AmdDist(double[] a, double[] b) { double m = 0; for (int j = 0; j < a.Length; ++j) m = Math.Max(m, Math.Abs(a[j] - b[j])); return m; }

    /// <summary> max over the columns of the 1D Wasserstein-1 distance: a lower bound of EMD with the L_inf ground metric (equal row counts, uniform weights) </summary>
    public static double ColW1(double[][] A, double[][] B)
    {
        double m = 0;
        for (int j = 0; j < A.Length; ++j) { double s = 0; var a = A[j]; var b = B[j]; for (int i = 0; i < a.Length; ++i) s += Math.Abs(a[i] - b[i]); m = Math.Max(m, s / a.Length); }
        return m;
    }

    /// <summary>
    /// Exact EMD between two PDDs with n rows each and weights 1/n (no collapsed rows, the generic case), ground metric
    /// L_inf between rows. With uniform weights and equal counts an optimal plan is a permutation (Birkhoff), so this is the
    /// assignment problem: Hungarian algorithm with potentials, O(n^3) (Kuhn 1955 / Munkres 1957; the shortest
    /// augmenting path form of Jonker & Volgenant 1987).
    /// </summary>
    public static double Emd(double[] A, double[] B, int n, int k)
    {
        var cost = new double[n, n];
        for (int a = 0; a < n; ++a) for (int b = 0; b < n; ++b) {
            double m = 0; for (int t = 0; t < k; ++t) m = Math.Max(m, Math.Abs(A[(long)a * k + t] - B[(long)b * k + t])); cost[a, b] = m;
        }
        return Hungarian(cost, n) / n;
    }

    static double Hungarian(double[,] c, int n)
    {
        var u = new double[n + 1]; var v = new double[n + 1]; var p = new int[n + 1]; var way = new int[n + 1];
        var minv = new double[n + 1]; var used = new bool[n + 1];
        for (int i = 1; i <= n; ++i) {
            p[0] = i; int j0 = 0;
            for (int j = 0; j <= n; ++j) { minv[j] = double.PositiveInfinity; used[j] = false; }
            do {
                used[j0] = true; int i0 = p[j0], j1 = 0; double delta = double.PositiveInfinity;
                for (int j = 1; j <= n; ++j) if (!used[j]) {
                    double cur = c[i0 - 1, j - 1] - u[i0] - v[j];
                    if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                    if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                }
                for (int j = 0; j <= n; ++j) if (used[j]) { u[p[j]] += delta; v[j] -= delta; } else minv[j] -= delta;
                j0 = j1;
            } while (p[j0] != 0);
            do { int j1 = way[j0]; p[j0] = p[j1]; j0 = j1; } while (j0 != 0);
        }
        double total = 0; for (int j = 1; j <= n; ++j) total += c[p[j] - 1, j - 1];
        return total;
    }

    /// <summary> row indices in lexicographic order of the rows (the PDD's row order; ties by index) </summary>
    public static int[] LexOrder(double[] D, int n, int k)
    {
        var o = Enumerable.Range(0, n).ToArray();
        Array.Sort(o, (a, b) => {
            for (int t = 0; t < k; ++t) { int c = D[(long)a * k + t].CompareTo(D[(long)b * k + t]); if (c != 0) return c; }
            return a.CompareTo(b);
        });
        return o;
    }

    public static ulong HashRows(double[] D, int n, int k, int[] order)
    {
        ulong h = 1469598103934665603UL;
        foreach (int i in order) for (int t = 0; t < k; ++t) { h ^= (ulong)BitConverter.DoubleToInt64Bits(D[(long)i * k + t]); h *= 1099511628211UL; }
        return h;
    }
}

static class Baselines
{
    /// <summary> D2 (Osada et al. 2002) as the sorted multiset of normalized pairwise distances: all pairs, or m pairs drawn with a fixed seed </summary>
    public static double[] D2(Vector3d[] P, double scale, int maxPairs)
    {
        int n = P.Length; long all = (long)n * (n - 1) / 2;
        double[] d;
        if (all <= maxPairs) {
            d = new double[all]; long c = 0;
            for (int i = 0; i < n; ++i) for (int j = i + 1; j < n; ++j) d[c++] = P[i].Distance(P[j]) / scale;
        } else {
            d = new double[maxPairs]; var r = new Rng(777);
            for (int t = 0; t < maxPairs; ++t) { int i = (int)(r.Next() % (ulong)n), j = (int)(r.Next() % (ulong)(n - 1)); if (j >= i) ++j; d[t] = P[i].Distance(P[j]) / scale; }
        }
        Array.Sort(d);
        return d;
    }

    public static double W1Sorted(double[] a, double[] b) { double s = 0; for (int i = 0; i < a.Length; ++i) s += Math.Abs(a[i] - b[i]); return s / a.Length; }

    /// <summary> PCA moments: sqrt of the covariance eigenvalues / RMS radius, ascending (3 numbers) </summary>
    public static double[] Pca(Vector3d[] P, Vector3d c, double scale)
    {
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var q in P) { var p = q - c; xx += p.x * p.x; xy += p.x * p.y; xz += p.x * p.z; yy += p.y * p.y; yz += p.y * p.z; zz += p.z * p.z; }
        int n = P.Length; xx /= n; xy /= n; xz /= n; yy /= n; yz /= n; zz /= n;
        var e = EigenSqrt(xx, xy, xz, yy, yz, zz);
        return e;
    }

    /// <summary> sqrt of the eigenvalues of a symmetric 3x3 matrix divided by sqrt(trace), ascending </summary>
    public static double[] EigenSqrt(double xx, double xy, double xz, double yy, double yz, double zz)
    {
        // eigenvalues of a symmetric 3x3 matrix, trigonometric method (Smith 1961)
        double p1 = xy * xy + xz * xz + yz * yz, q0 = (xx + yy + zz) / 3;
        double[] ev;
        if (p1 == 0) ev = new[] { xx, yy, zz };
        else {
            double p2 = (xx - q0) * (xx - q0) + (yy - q0) * (yy - q0) + (zz - q0) * (zz - q0) + 2 * p1, pp = Math.Sqrt(p2 / 6);
            double b11 = (xx - q0) / pp, b22 = (yy - q0) / pp, b33 = (zz - q0) / pp, b12 = xy / pp, b13 = xz / pp, b23 = yz / pp;
            double detB = b11 * (b22 * b33 - b23 * b23) - b12 * (b12 * b33 - b23 * b13) + b13 * (b12 * b23 - b22 * b13);
            double r = Math.Max(-1, Math.Min(1, detB / 2)), phi = Math.Acos(r) / 3;
            double e1 = q0 + 2 * pp * Math.Cos(phi), e3 = q0 + 2 * pp * Math.Cos(phi + 2 * Math.PI / 3);
            ev = new[] { e1, 3 * q0 - e1 - e3, e3 };
        }
        double tr = Math.Sqrt(xx + yy + zz);
        var s = ev.Select(e => Math.Sqrt(Math.Max(0, e)) / tr).ToArray(); Array.Sort(s); return s;
    }
}

static class Shapes
{
    static readonly Dictionary<string, (Vector3d[] V, Index3i[] T, double[] Cdf)> cache = new Dictionary<string, (Vector3d[], Index3i[], double[])>();

    public static readonly string[] Base = { "sphere", "ellipsoid", "torus", "box", "cylinder", "gyroid", "schwarzP", "blob" };

    static DMesh3 Build(string name)
    {
        switch (name) {
            // other tessellations of the same shapes, "shape@t<i>", i = 0..5, for the moments candidate (Experiments.Moments)
        }
        int at = name.IndexOf("@t");
        if (at > 0) {
            string b = name.Substring(0, at); int i = int.Parse(name.Substring(at + 2));
            switch (b) {
                case "sphere": return new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 18 + i }.Generate().MakeDMesh();
                case "ellipsoid": return Scaled(Build("sphere@t" + i), 1, 0.85, 0.7);
                case "torus": return Torus(1, 0.35, 56 + i, 20 + i);
                case "box": return new GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.8, 0.6)), EdgeVertices = 7 + i }.Generate().MakeDMesh();
                case "cylinder": return new CappedCylinderGenerator { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1.5f, Slices = 40 + i }.Generate().MakeDMesh();
                case "gyroid": return Tpms(TpmsType.Gyroid, TpmsStyle.Sheet, 2, new[] { 8, 9, 11, 12, 13, 14 }[i], Matrix3d.Identity);
                case "schwarzP": return Tpms(TpmsType.SchwarzP, TpmsStyle.SkeletalLow, 2, new[] { 8, 9, 11, 12, 13, 14 }[i], Matrix3d.Identity);
            }
        }
        switch (name) {
            case "sphere": return new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 24 }.Generate().MakeDMesh();
            case "ellipsoid": return Scaled(Build("sphere"), 1, 0.85, 0.7);
            case "ellipsoid3": return Scaled(Build("sphere"), 1, 0.97, 0.94);
            case "torus": return Torus(1, 0.35);
            case "torus33": return Torus(1, 0.33);
            case "box": return new GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.8, 0.6)), EdgeVertices = 12 }.Generate().MakeDMesh();
            case "box62": return new GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.8, 0.62)), EdgeVertices = 12 }.Generate().MakeDMesh();
            case "cylinder": return new CappedCylinderGenerator { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1.5f, Slices = 48 }.Generate().MakeDMesh();
            case "cylinder145": return new CappedCylinderGenerator { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1.45f, Slices = 48 }.Generate().MakeDMesh();
            case "gyroid": return Tpms(TpmsType.Gyroid, TpmsStyle.Sheet, 2, 10, Matrix3d.Identity);
            case "schwarzP": return Tpms(TpmsType.SchwarzP, TpmsStyle.SkeletalLow, 2, 10, Matrix3d.Identity);
            case "schwarzD": return Tpms(TpmsType.SchwarzD, TpmsStyle.SkeletalLow, 2, 10, Matrix3d.Identity);
        }
        throw new ArgumentException(name);
    }

    public static DMesh3 Tpms(TpmsType type, TpmsStyle style, int cells, int div, Matrix3d rot)
    {
        var t = new TpmsLattice { Type = type, Style = style, CellSize = 1.0, Divisions = div, VolumeFraction = 0.3, Rotation = rot,
            Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(cells, cells, cells)), VerifySelfIntersections = false };
        t.Compute();
        if (t.Status != TpmsStatus.Ok) throw new Exception("TpmsLattice " + t.Status + " " + t.Message);
        return t.Mesh;
    }

    static DMesh3 Scaled(DMesh3 m, double sx, double sy, double sz)
    {
        foreach (int v in m.VertexIndices()) { var p = m.GetVertex(v); m.SetVertex(v, new Vector3d(p.x * sx, p.y * sy, p.z * sz)); }
        return m;
    }

    static DMesh3 Torus(double R, double r, int nu = 64, int nv = 24)
    {
        var m = new DMesh3();
        for (int i = 0; i < nu; ++i) for (int j = 0; j < nv; ++j) {
            double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
            m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
        }
        for (int i = 0; i < nu; ++i) for (int j = 0; j < nv; ++j) {
            int a = i * nv + j, b = ((i + 1) % nu) * nv + j, c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
        }
        return m;
    }

    static (Vector3d[] V, Index3i[] T, double[] Cdf) Get(string name)
    {
        lock (cache) {
            if (cache.TryGetValue(name, out var e)) return e;
            var m = Build(name);
            var map = new Dictionary<int, int>(); var V = new List<Vector3d>();
            foreach (int v in m.VertexIndices()) { map[v] = V.Count; V.Add(m.GetVertex(v)); }
            // centre the mesh at its vertex centroid so the deformations below act about the shape
            var c = Vector3d.Zero; foreach (var p in V) c += p; c /= V.Count;
            for (int i = 0; i < V.Count; ++i) V[i] -= c;
            var T = new List<Index3i>(); var cdf = new List<double>(); double acc = 0;
            foreach (int t in m.TriangleIndices()) {
                var tri = m.GetTriangle(t); var it = new Index3i(map[tri.a], map[tri.b], map[tri.c]);
                acc += MathUtil.Area(V[it.a], V[it.b], V[it.c]); T.Add(it); cdf.Add(acc);
            }
            e = (V.ToArray(), T.ToArray(), cdf.ToArray()); cache[name] = e; return e;
        }
    }

    public static int VertexCount(string name) => Get(name).V.Length;

    public static (Vector3d[] V, Index3i[] T) Mesh(string name) { var e = Get(name); return ((Vector3d[])e.V.Clone(), e.T); }

    /// <summary>
    /// Sampling-free descriptor (the candidate after the stop): sqrt of the eigenvalues of the exact second-moment tensor of the
    /// surface (triangle integrals: int_T x x^T dA = A/12 (sum_v v v^T + s s^T), s = a+b+c), divided by its RMS radius. A
    /// classical moment invariant, not PDD.
    /// </summary>
    public static double[] SurfaceMoments(Vector3d[] V, Index3i[] T)
    {
        double A = 0; Vector3d c = Vector3d.Zero; var M = new double[6];
        foreach (var t in T) {
            Vector3d a = V[t.a], b = V[t.b], d = V[t.c]; double area = MathUtil.Area(a, b, d); var s = a + b + d;
            A += area; c += area / 3 * s;
            double[] u = { a.x, b.x, d.x }, v = { a.y, b.y, d.y }, w = { a.z, b.z, d.z };
            double XX = 0, XY = 0, XZ = 0, YY = 0, YZ = 0, ZZ = 0;
            for (int i = 0; i < 3; ++i) { XX += u[i] * u[i]; XY += u[i] * v[i]; XZ += u[i] * w[i]; YY += v[i] * v[i]; YZ += v[i] * w[i]; ZZ += w[i] * w[i]; }
            double f = area / 12;
            M[0] += f * (XX + s.x * s.x); M[1] += f * (XY + s.x * s.y); M[2] += f * (XZ + s.x * s.z);
            M[3] += f * (YY + s.y * s.y); M[4] += f * (YZ + s.y * s.z); M[5] += f * (ZZ + s.z * s.z);
        }
        c /= A;
        // the covariance of the surface measure, as a point set of 3 weighted "points" is not needed: use the PCA routine on the tensor
        double xx = M[0] / A - c.x * c.x, xy = M[1] / A - c.x * c.y, xz = M[2] / A - c.x * c.z, yy = M[3] / A - c.y * c.y, yz = M[4] / A - c.y * c.z, zz = M[5] / A - c.z * c.z;
        return Baselines.EigenSqrt(xx, xy, xz, yy, yz, zz);
    }

    /// <summary> n points: area-uniform samples of the shape's surface (seeded), or for "blob" an anisotropic Gaussian cloud </summary>
    public static Vector3d[] Sample(string name, int n, ulong seed)
    {
        var r = new Rng(seed); var P = new Vector3d[n];
        if (name == "blob") { for (int i = 0; i < n; ++i) P[i] = new Vector3d(r.Gauss(), 0.7 * r.Gauss(), 0.5 * r.Gauss()); return P; }
        var (V, T, cdf) = Get(name); double total = cdf[cdf.Length - 1];
        for (int i = 0; i < n; ++i) {
            double x = r.U() * total; int lo = Array.BinarySearch(cdf, x); if (lo < 0) lo = ~lo; if (lo >= T.Length) lo = T.Length - 1;
            double s = Math.Sqrt(r.U()), t = r.U(); var tri = T[lo];
            P[i] = (1 - s) * V[tri.a] + s * (1 - t) * V[tri.b] + s * t * V[tri.c];
        }
        return P;
    }

    public static Vector3d[] Vertices(string name) => (Vector3d[])Get(name).V.Clone();

    public static Vector3d[] Deform(Vector3d[] P, string kind, double eps)
    {
        var Q = new Vector3d[P.Length];
        // bump centre: the point of the set farthest along (1, 0.6, 0.3) (independent of the order)
        Vector3d dir = new Vector3d(1, 0.6, 0.3).Normalized, cb = P[0];
        foreach (var p in P) if (p.Dot(dir) > cb.Dot(dir)) cb = p;
        for (int i = 0; i < P.Length; ++i) {
            var p = P[i];
            switch (kind) {
                case "stretch": Q[i] = new Vector3d(p.x * (1 + eps), p.y, p.z); break;
                case "twist": { double a = eps * p.z, ca = Math.Cos(a), sa = Math.Sin(a); Q[i] = new Vector3d(ca * p.x - sa * p.y, sa * p.x + ca * p.y, p.z); break; }
                case "bump": { double w = Math.Exp(-(p - cb).LengthSquared / (0.3 * 0.3)); Q[i] = p + eps * w * dir; break; }
                default: throw new ArgumentException(kind);
            }
        }
        return Q;
    }

    /// <summary> Gaussian noise of sigma (shape units, size ~1), then a random rotation, scale in [1/2, 2] and translation in [-10, 10]^3 </summary>
    public static Vector3d[] RigidCopy(Vector3d[] P, ulong seed, double noise, bool identity = false)
    {
        var r = new Rng(seed ^ 0xABCDEF);
        double q0 = r.Gauss(), q1 = r.Gauss(), q2 = r.Gauss(), q3 = r.Gauss();
        var q = new Quaterniond(q1, q2, q3, q0); q.Normalize();
        var R = q.ToRotationMatrix();
        double s = Math.Pow(2, 2 * r.U() - 1); var t = new Vector3d(20 * r.U() - 10, 20 * r.U() - 10, 20 * r.U() - 10);
        var Q = new Vector3d[P.Length];
        for (int i = 0; i < P.Length; ++i) {
            var p = P[i];
            if (noise > 0) p += noise * new Vector3d(r.Gauss(), r.Gauss(), r.Gauss());
            Q[i] = identity ? p : s * (R * p) + t;
        }
        return Q;
    }
}

/// <summary> all descriptors of one point set </summary>
sealed class Desc
{
    public int N; public double Scale;
    public Dictionary<int, double[]> Pdd = new Dictionary<int, double[]>();
    public Dictionary<int, double[]> Amd = new Dictionary<int, double[]>();
    public Dictionary<int, double[][]> Cols = new Dictionary<int, double[][]>();
    public double[] D2, Pca;

    public static Desc Of(Vector3d[] P, int[] ks, bool keepRows, int maxPairs = 200000, int threads = 1)
    {
        var d = new Desc { N = P.Length };
        d.Scale = global::Pdd.RmsRadius(P, out var c);
        foreach (int k in ks) {
            int kk = Math.Min(k, P.Length - 1);
            var D = global::Pdd.Normalize(global::Pdd.Knn(P, kk, threads), d.Scale);
            if (keepRows) d.Pdd[k] = D;
            d.Cols[k] = global::Pdd.SortedColumns(D, P.Length, kk);
            d.Amd[k] = global::Pdd.AmdFromCols(d.Cols[k]);
        }
        d.D2 = Baselines.D2(P, d.Scale, maxPairs);
        d.Pca = Baselines.Pca(P, c, d.Scale);
        return d;
    }
}

static class Experiments
{
    static readonly double[] Ladder = { 1e-5, 3e-5, 1e-4, 3e-4, 1e-3, 3e-3, 1e-2, 3e-2, 1e-1, 3e-1 };

    sealed class Metric { public string Name; public bool IsPdd; public Func<Desc, Desc, double> F; }

    static List<Metric> Metrics(int n, bool withEmd)
    {
        var m = new List<Metric>();
        foreach (int k in new[] { 8, 32, 128 }) {
            int kk = k;
            m.Add(new Metric { Name = "AMD" + k, IsPdd = true, F = (a, b) => Pdd.AmdDist(a.Amd[kk], b.Amd[kk]) });
            m.Add(new Metric { Name = "colW1_" + k, IsPdd = true, F = (a, b) => Pdd.ColW1(a.Cols[kk], b.Cols[kk]) });
        }
        if (withEmd) {
            m.Add(new Metric { Name = "EMD32", IsPdd = true, F = (a, b) => Pdd.Emd(a.Pdd[32], b.Pdd[32], a.N, 32) });
            m.Add(new Metric { Name = "EMDall", IsPdd = true, F = (a, b) => Pdd.Emd(a.Pdd[100000], b.Pdd[100000], a.N, a.N - 1) });
            m.Add(new Metric { Name = "colW1all", IsPdd = true, F = (a, b) => Pdd.ColW1(a.Cols[100000], b.Cols[100000]) });
        }
        m.Add(new Metric { Name = "D2", IsPdd = false, F = (a, b) => Baselines.W1Sorted(a.D2, b.D2) });
        m.Add(new Metric { Name = "PCA", IsPdd = false, F = (a, b) => Pdd.AmdDist(a.Pca, b.Pca) });
        return m;
    }

    /// <summary>
    /// (a) For each shape, sampling mode (same point set / resampled), noise and deformation: the reference set, 6 rigid copies
    /// (random rotation, scale, translation; fresh noise; fresh samples in the resampled mode) and 3 deformed copies per eps of the
    /// ladder. eps* = smallest eps from which on every deformed copy is farther from the reference than every rigid copy.
    /// </summary>
    public static void Discrimination(int n)
    {
        bool emd = n <= 400;
        int[] ks = emd ? new[] { 8, 32, 128, 100000 } : new[] { 8, 32, 128 };
        var metrics = Metrics(n, emd);
        Console.WriteLine($"# disc N={n} metrics: {string.Join(" ", metrics.Select(m => m.Name))}; eps ladder {string.Join(",", Ladder)}");
        Console.WriteLine("# eps* per metric ('-' = not separated up to 0.3); best PDD vs best baseline (D2, PCA)");
        Console.WriteLine($"mode     noise  deform  shape     | {string.Join(" ", metrics.Select(m => m.Name.PadLeft(8)))} | PDDbest  BASEbest verdict");
        int pddWins = 0, baseWins = 0, ties = 0;
        var sw = Stopwatch.StartNew();
        foreach (string mode in new[] { "same", "resample" })
        foreach (double noise in new[] { 0.0, 1e-3, 1e-2 })
        foreach (string deform in new[] { "stretch", "bump", "twist" })
        foreach (string shape in Shapes.Base) {
            if (mode == "same" && noise == 0 && deform != "stretch") { }
            var basePts = Shapes.Sample(shape, n, 1);
            Vector3d[] Pts(ulong seed) => mode == "same" ? basePts : Shapes.Sample(shape, n, seed);
            var refD = Desc.Of(Shapes.RigidCopy(basePts, 999, noise, identity: true), ks, emd);
            var pos = new List<Vector3d[]>();
            for (int i = 0; i < 6; ++i) pos.Add(Shapes.RigidCopy(Pts(100 + (ulong)i), 10 + (ulong)i, noise));
            var neg = new List<(int e, Vector3d[] P)>();
            for (int e = 0; e < Ladder.Length; ++e) for (int j = 0; j < 3; ++j)
                neg.Add((e, Shapes.RigidCopy(Shapes.Deform(Pts(200 + (ulong)j), deform, Ladder[e]), 50 + (ulong)(e * 3 + j), noise)));
            var posD = new Desc[pos.Count]; var negD = new Desc[neg.Count];
            Parallel.For(0, pos.Count + neg.Count, i => { if (i < pos.Count) posD[i] = Desc.Of(pos[i], ks, emd); else negD[i - pos.Count] = Desc.Of(neg[i - pos.Count].P, ks, emd); });
            var star = new double[metrics.Count];
            Parallel.For(0, metrics.Count, mi => {
                var M = metrics[mi];
                double maxPos = posD.Max(d => M.F(refD, d));
                var minNeg = new double[Ladder.Length]; for (int e = 0; e < Ladder.Length; ++e) minNeg[e] = double.PositiveInfinity;
                for (int i = 0; i < neg.Count; ++i) minNeg[neg[i].e] = Math.Min(minNeg[neg[i].e], M.F(refD, negD[i]));
                double s = double.NaN;
                for (int e = Ladder.Length - 1; e >= 0; --e) { if (minNeg[e] > maxPos) s = Ladder[e]; else break; }
                star[mi] = s;
            });
            double Best(bool isPdd) { double b = double.NaN; for (int i = 0; i < metrics.Count; ++i) if (metrics[i].IsPdd == isPdd && !double.IsNaN(star[i]) && (double.IsNaN(b) || star[i] < b)) b = star[i]; return b; }
            double bp = Best(true), bb = Best(false);
            double vp = double.IsNaN(bp) ? 1 : bp, vb = double.IsNaN(bb) ? 1 : bb;
            string verdict = vp * 2 <= vb ? "PDD" : vb * 2 <= vp ? "BASE" : "tie";
            if (verdict == "PDD") pddWins++; else if (verdict == "BASE") baseWins++; else ties++;
            Console.WriteLine($"{mode,-8} {noise,-6} {deform,-7} {shape,-9} | {string.Join(" ", star.Select(F))} | {F(bp)} {F(bb)} {verdict}");
        }
        Console.WriteLine($"# summary N={n}: PDD clearly better (eps* <= half) {pddWins}, baseline clearly better {baseWins}, ties {ties}; {sw.Elapsed.TotalSeconds:F0} s");
    }

    static string F(double v) => (double.IsNaN(v) ? "-" : v.ToString("0e0")).PadLeft(8);

    /// <summary>
    /// (c) "same shape?": one reference per shape (seed 1), 4 queries per shape (fresh samples, rigid copy, noise 1e-3).
    /// top-1: the nearest reference is the own shape. Pre-check: tau = the largest query-to-own-reference distance (no false
    /// negative by construction); FP = query-to-other-reference pairs at or below tau.
    /// </summary>
    public static void Retrieval(int n)
    {
        string[] shapes = { "sphere", "ellipsoid", "ellipsoid3", "torus", "torus33", "box", "box62", "cylinder", "cylinder145", "gyroid", "schwarzP", "schwarzD", "blob" };
        bool full = n <= 400;   // the full PDD (k = N-1) and exact EMD only at small N (O(N^2) / O(N^3))
        int[] ks = full ? new[] { 8, 32, 128, 100000 } : new[] { 8, 32, 128 };
        var metrics = Metrics(n, full);
        var refs = new Desc[shapes.Length];
        Parallel.For(0, shapes.Length, s => refs[s] = Desc.Of(Shapes.Sample(shapes[s], n, 1), ks, full));
        int Q = 4; var qs = new Desc[shapes.Length * Q];
        Parallel.For(0, qs.Length, i => qs[i] = Desc.Of(Shapes.RigidCopy(Shapes.Sample(shapes[i / Q], n, 300 + (ulong)i), 70 + (ulong)i, 1e-3), ks, full));
        Console.WriteLine($"# retrieval N={n}, {shapes.Length} shapes ({string.Join(",", shapes)}), {Q} queries each, noise 1e-3, resampled");
        Console.WriteLine("metric    top1   FP@tau(of " + (qs.Length * (shapes.Length - 1)) + ")  confused pairs at tau");
        foreach (var M in metrics) {
            int top1 = 0; double tau = 0;
            var dist = new double[qs.Length, shapes.Length];
            for (int i = 0; i < qs.Length; ++i) for (int s = 0; s < shapes.Length; ++s) dist[i, s] = M.F(qs[i], refs[s]);
            for (int i = 0; i < qs.Length; ++i) {
                int own = i / Q, best = 0; for (int s = 1; s < shapes.Length; ++s) if (dist[i, s] < dist[i, best]) best = s;
                if (best == own) top1++; tau = Math.Max(tau, dist[i, own]);
            }
            int fp = 0; var conf = new SortedSet<string>();
            for (int i = 0; i < qs.Length; ++i) for (int s = 0; s < shapes.Length; ++s) if (s != i / Q && dist[i, s] <= tau) { fp++; conf.Add(shapes[i / Q] + "~" + shapes[s]); }
            Console.WriteLine($"{M.Name,-9} {top1,3}/{qs.Length}  {fp,5}            {string.Join(" ", conf.Take(8))}{(conf.Count > 8 ? " ..." : "")}");
        }
    }

    /// <summary>
    /// Candidate after the stop: is the limit of the resampled stratum the sampling noise? The same shape at a second
    /// tessellation (other grid, other contour) stands in for "resampled"; positives = 6 rigid copies of the second
    /// tessellation, negatives = 3 deformed rigid copies per eps. Exact surface moments (no samples) against D2 and PDD on
    /// 2000 area samples of the second tessellation.
    /// </summary>
    public static void Moments()
    {
        string[] shapes = { "sphere", "ellipsoid", "torus", "box", "cylinder", "gyroid", "schwarzP" };
        int n = 2000; int[] ks = { 32 };
        Console.WriteLine($"# moments: reference = tessellation 0, positives = 6 other tessellations (shape@t0..5), negatives = deformed t1/t3/t5; rigid, no noise; the sphere twist is a control (no shape change); samples N={n}; eps* as in disc");
        Console.WriteLine("deform  shape     | Moments       D2    AMD32 colW1_32  PCAsmp");
        foreach (string deform in new[] { "stretch", "bump", "twist" })
        foreach (string shape in shapes) {
            var (V0, T0) = Shapes.Mesh(shape);
            var refM = Shapes.SurfaceMoments(V0, T0); var refD = Desc.Of(Shapes.Sample(shape, n, 1), ks, false);
            var pos = new List<(double[] m, Desc d)>(); var neg = new List<(int e, double[] m, Desc d)>();
            for (int i = 0; i < 6; ++i) {   // six other tessellations, rigid copies
                string alt = shape + "@t" + i; var (V1, T1) = Shapes.Mesh(alt);
                var V = Shapes.RigidCopy(V1, 10 + (ulong)i, 0);
                pos.Add((Shapes.SurfaceMoments(V, T1), Desc.Of(Shapes.RigidCopy(Shapes.Sample(alt, n, 100 + (ulong)i), 10 + (ulong)i, 0), ks, false)));
            }
            for (int e = 0; e < Ladder.Length; ++e) for (int j = 0; j < 3; ++j) {   // deformed copies of three of them
                ulong sd = 50 + (ulong)(e * 3 + j); string alt = shape + "@t" + (2 * j + 1); var (V1, T1) = Shapes.Mesh(alt);
                var V = Shapes.RigidCopy(Shapes.Deform(V1, deform, Ladder[e]), sd, 0);
                neg.Add((e, Shapes.SurfaceMoments(V, T1), Desc.Of(Shapes.RigidCopy(Shapes.Deform(Shapes.Sample(alt, n, 200 + (ulong)j), deform, Ladder[e]), sd, 0), ks, false)));
            }
            var fs = new Func<double[], Desc, double>[] {
                (m, d) => Pdd.AmdDist(refM, m), (m, d) => Baselines.W1Sorted(refD.D2, d.D2), (m, d) => Pdd.AmdDist(refD.Amd[32], d.Amd[32]),
                (m, d) => Pdd.ColW1(refD.Cols[32], d.Cols[32]), (m, d) => Pdd.AmdDist(refD.Pca, d.Pca) };
            var star = fs.Select(f => {
                double maxPos = pos.Max(p => f(p.m, p.d)); double s = double.NaN;
                for (int e = Ladder.Length - 1; e >= 0; --e) { double mn = neg.Where(x => x.e == e).Min(x => f(x.m, x.d)); if (mn > maxPos) s = Ladder[e]; else break; }
                return s; }).ToArray();
            Console.WriteLine($"{deform,-7} {shape,-9} | {string.Join(" ", star.Select(F))}");
        }
    }

    public static void Timing()
    {
        Console.WriteLine($"# time: gyroid surface samples; {Environment.ProcessorCount} logical cores; min of 3 runs; ms");
        Console.WriteLine("N       k   kNN-1t   kNN-4t  scale  AMD+cols  compare(AMD+colW1)  D2(1M pairs)+W1  PCA");
        foreach (int n in new[] { 10000, 50000, 100000, 200000 }) {
            var P = Shapes.Sample("gyroid", n, 5); var P2 = Shapes.Sample("gyroid", n, 6);
            foreach (int k in new[] { 16, 32, 64 }) {
                double t1 = Min3(() => Pdd.Knn(P, k, 1)), t4 = Min3(() => Pdd.Knn(P, k, 4));
                double sc = 0; double tsc = Min3(() => sc = Pdd.RmsRadius(P, out _));
                var D = Pdd.Normalize(Pdd.Knn(P, k, 4), sc); var Db = Pdd.Normalize(Pdd.Knn(P2, k, 4), sc);
                double[] a = null; double[][] cols = null;
                double tprep = Min3(() => { cols = Pdd.SortedColumns(D, n, k); a = Pdd.AmdFromCols(cols); });
                var ab = Pdd.Amd(Db, n, k); var cb = Pdd.SortedColumns(Db, n, k);
                double tcmp = Min3(() => { Pdd.AmdDist(a, ab); Pdd.ColW1(cols, cb); });
                double td2 = k == 16 ? Min3(() => { var x = Baselines.D2(P, sc, 1000000); var y = Baselines.D2(P2, sc, 1000000); Baselines.W1Sorted(x, y); }) : double.NaN;
                double tpca = k == 16 ? Min3(() => Baselines.Pca(P, Vector3d.Zero, sc)) : double.NaN;
                Console.WriteLine($"{n,-7} {k,-3} {t1,7:F0} {t4,8:F0} {tsc,6:F0} {tprep,9:F0} {tcmp,19:F1} {(double.IsNaN(td2) ? "" : td2.ToString("F0")),16} {(double.IsNaN(tpca) ? "" : tpca.ToString("F1")),5}");
            }
        }
        // exact EMD (assignment) cost, for the record
        foreach (int n in new[] { 200, 400, 800 }) {
            var A = Pdd.Knn(Shapes.Sample("gyroid", n, 5), 32, 1); var B = Pdd.Knn(Shapes.Sample("gyroid", n, 6), 32, 1);
            Console.WriteLine($"exact EMD (Hungarian) N={n} k=32: {Min3(() => Pdd.Emd(A, B, n, 32)):F0} ms");
        }
    }

    static double Min3(Action a) { double best = double.MaxValue; for (int r = 0; r < 3; ++r) { var sw = Stopwatch.StartNew(); a(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds); } return best; }

    /// <summary> 4. determinism: runs and thread counts, input order, and the eight similarities of the test harness's Similarity.All() </summary>
    public static void Determinism()
    {
        int n = 50000, k = 32;
        var P = Shapes.Sample("gyroid", n, 5);
        ulong Hash(Vector3d[] X, int threads, out double[] amd)
        {
            double s = Pdd.RmsRadius(X, out _); var D = Pdd.Normalize(Pdd.Knn(X, k, threads), s);
            amd = Pdd.Amd(D, X.Length, k);
            return Pdd.HashRows(D, X.Length, k, Pdd.LexOrder(D, X.Length, k));
        }
        Console.WriteLine($"# determinism: gyroid samples N={n} k={k}; hash = FNV-1a over the bits of the normalized PDD rows in lexicographic order");
        ulong h0 = Hash(P, 1, out var amd0);
        foreach (int th in new[] { 1, 2, 4 }) for (int run = 0; run < 2; ++run) Console.WriteLine($"threads {th} run {run}: {Hash(P, th, out _):X16}");
        var perm = (Vector3d[])P.Clone(); var r = new Rng(42);
        for (int i = n - 1; i > 0; --i) { int j = (int)(r.Next() % (ulong)(i + 1)); var t = perm[i]; perm[i] = perm[j]; perm[j] = t; }
        Console.WriteLine($"permuted input:     {Hash(perm, 4, out _):X16}");
        // the members of tests/geometry3Sharp.Tests/Harness/Invariance.cs Similarity.All(), copied (the probe does not reference the test project)
        var utm = new Vector3d(632117.25, 5712043.5, 113.75);
        var sims = new (string name, double s, Matrix3d R, Vector3d t)[] {
            ("identity", 1, Matrix3d.Identity, Vector3d.Zero), ("utm", 1, Matrix3d.Identity, utm),
            ("scale 2^-10", 1.0 / 1024, Matrix3d.Identity, Vector3d.Zero), ("scale 2^10", 1024, Matrix3d.Identity, Vector3d.Zero),
            ("scale 1e-3", 1e-3, Matrix3d.Identity, Vector3d.Zero), ("scale 1e3", 1e3, Matrix3d.Identity, Vector3d.Zero),
            ("rotated", 1, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17), Vector3d.Zero),
            ("rotated, scale 1e-3, utm", 1e-3, Matrix3d.AxisAngleD(Vector3d.AxisY, 23), utm) };
        foreach (var (name, s, R, t) in sims) {
            var X = P.Select(p => s * (R * p) + t).ToArray();
            ulong h = Hash(X, 4, out var amd);
            double dev = 0; for (int j = 0; j < k; ++j) dev = Math.Max(dev, Math.Abs(amd[j] - amd0[j]) / amd0[j]);
            Console.WriteLine($"{name,-26} {h:X16} {(h == h0 ? "bit-identical" : "differs"),-14} max rel. AMD deviation {dev:0.0e0}");
        }
    }

    /// <summary>
    /// (b) period / symmetry on TpmsLattice vertices (gyroid sheet, cell size 1, n^3 cells): distinct PDD rows (orbits up to
    /// tolerance), and lattice-basis recovery from a centre vertex: candidates = vertices with the anchor's PDD row (PDD route)
    /// or all vertices (baseline), shortest first, each verified by translating all vertices (hash lookup).
    /// </summary>
    public static void Period()
    {
        Console.WriteLine("# period: gyroid sheet, CellSize 1, Divisions 10, VF 0.3; PDD k=16 on the vertices; row tolerance 1e-9, match tolerance 1e-6");
        Console.WriteLine("cells rot  V      distinct  V/distinct | PDD: cand verif  ms  basis |  all-vertex baseline: verif  ms | known-period check ms");
        foreach (var (cells, rot) in new[] { (1, false), (2, false), (3, false), (2, true) }) {
            var R = rot ? Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 37) : Matrix3d.Identity;
            var m = Shapes.Tpms(TpmsType.Gyroid, TpmsStyle.Sheet, cells, 10, R);
            var V = m.VertexIndices().Select(v => m.GetVertex(v)).ToArray(); int n = V.Length, k = 16;
            var D = Pdd.Knn(V, k, 4);
            var order = Pdd.LexOrder(D, n, k);
            double Linf(int a, int b) { double x = 0; for (int t = 0; t < k; ++t) x = Math.Max(x, Math.Abs(D[(long)a * k + t] - D[(long)b * k + t])); return x; }
            int distinct = 1; for (int i = 1; i < n; ++i) if (Linf(order[i], order[i - 1]) > 1e-9) distinct++;
            // match hash on a 1e-3 grid, neighbours searched
            var hash = new Dictionary<(long, long, long), List<int>>();
            (long, long, long) Key(Vector3d p) => ((long)Math.Floor(p.x * 1e3), (long)Math.Floor(p.y * 1e3), (long)Math.Floor(p.z * 1e3));
            for (int i = 0; i < n; ++i) { var key = Key(V[i]); if (!hash.TryGetValue(key, out var l)) hash[key] = l = new List<int>(); l.Add(i); }
            bool Has(Vector3d p) {
                var (a, b, c) = Key(p);
                for (long x = a - 1; x <= a + 1; ++x) for (long y = b - 1; y <= b + 1; ++y) for (long z = c - 1; z <= c + 1; ++z)
                    if (hash.TryGetValue((x, y, z), out var l)) foreach (int j in l) if (V[j].Distance(p) <= 1e-6) return true;
                return false;
            }
            var RT = R.Transpose();
            bool Inside(Vector3d p, double margin) { var q = RT * p; return q.x > margin && q.y > margin && q.z > margin && q.x < cells - margin && q.y < cells - margin && q.z < cells - margin; }
            bool Verify(Vector3d t) {
                int den = 0, hit = 0;
                for (int i = 0; i < n; ++i) { if (!Inside(V[i], 0.15) || !Inside(V[i] + t, 0.15)) continue; den++; if (Has(V[i] + t)) hit++; }
                return den >= 50 && hit >= 0.99 * den;
            }
            var cen = Vector3d.Zero; foreach (var p in V) cen += p; cen /= n;
            int anchor = 0; for (int i = 1; i < n; ++i) if (V[i].Distance(cen) < V[anchor].Distance(cen)) anchor = i;
            (int verif, List<Vector3d> basis) Recover(IEnumerable<int> cand) {
                var ts = cand.Where(j => j != anchor).Select(j => V[j] - V[anchor]).OrderBy(t => t.LengthSquared).ToList();
                var basis = new List<Vector3d>(); int verif = 0;
                foreach (var t in ts) {
                    if (basis.Count == 1 && basis[0].Normalized.Cross(t.Normalized).Length < 1e-3) continue;
                    if (basis.Count == 2 && Math.Abs(basis[0].Cross(basis[1]).Normalized.Dot(t.Normalized)) < 1e-3) continue;
                    verif++; if (Verify(t)) { basis.Add(t); if (basis.Count == 3) break; }
                    if (verif > 400) break;   // cap: the baseline on 3^3 cells otherwise runs for minutes
                }
                return (verif, basis);
            }
            var sw = Stopwatch.StartNew();
            var candPdd = Enumerable.Range(0, n).Where(j => Linf(j, anchor) <= 1e-9).ToList();
            var (vp, bp) = Recover(candPdd); double tp = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); var (vb, bb) = Recover(Enumerable.Range(0, n)); double tb = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); bool known = cells > 1 && Verify(R * Vector3d.AxisX) && Verify(R * Vector3d.AxisY) && Verify(R * Vector3d.AxisZ); double tk = sw.Elapsed.TotalMilliseconds;
            string B(List<Vector3d> b) => b.Count == 3 ? string.Join("/", b.Select(t => t.Length.ToString("F6"))) : $"{b.Count} found";
            Console.WriteLine($"{cells,-5} {(rot ? "yes" : "no"),-4} {n,-6} {distinct,-9} {n / (double)distinct,10:F2} | {candPdd.Count,4} {vp,5} {tp,5:F0} {B(bp)} | {vb,5} {tb,6:F0} {B(bb)} | {(cells > 1 ? (known ? "ok " : "FAIL ") + tk.ToString("F0") : "n/a")}");
        }
    }
}
