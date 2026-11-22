// Kernel varifold / current inner products of triangle meshes (docs/research/VARIFOLD_WRAP_QUALITY_2026-11.md).
// Implemented from the papers: Vaillant, Glaunes, "Surface matching via currents", IPMI 2005; Charon, Trouve, "The varifold
// representation of nonoriented shapes for diffeomorphic registration", SIAM J. Imaging Sci. 6(4), 2013; Kaltenmark, Charlier,
// Charon, "A general framework for curve and surface comparison and registration with oriented varifolds", CVPR 2017.
//
// A surface S is the measure sum_f a_f delta_(c_f, n_f) over (position, unit normal). With a Gaussian in space,
//   <S, T>_p = sum_f sum_g a_f a_g exp(-|c_f - c_g|^2 / sigma^2) (n_f . n_g)^p,   p = 0, 1, 2,
// p = 0 is the mass (area measure), p = 1 the current, p = 2 the unoriented varifold (Binet kernel). The oriented varifolds
// use ((1 + u) / 2)^m in place of u^p (1 for equal normals, 0 for opposite ones; m = 2 is wide, m = 8 falls to 0.28 at 45
// degrees and to 0.004 at 90 degrees).
//
// Quadrature: a triangle (slivers halved at the longest edge first) is cut into k^2 congruent triangles, k = ceil(longest
// edge / h), each with the edge-midpoint
// rule (exact for degree 2; the centroid rule needs four times the points per digit near creases and borders). Midpoints
// inside a triangle are shared by two pieces: 3 k (k + 1) / 2 points.
// The sum runs over the pairs within Cut sigma (a cell grid). Rows are summed in parallel and added in index order, so the
// result does not depend on the thread count.
using System;
using System.Collections.Generic;
using g3;

sealed class VarifoldMeasure
{
    public const double Cut = 3.5;     // pairs beyond Cut sigma are dropped: exp(-12.25) = 4.8e-6 of a flat sheet's sum, on both sides alike

    public double[] X, Y, Z, NX, NY, NZ, A;
    public int Count;
    public double Area;

    /// <summary>
    /// the quadrature points of a mesh at spacing h; degenerate triangles (no normal) are skipped. centroids: one point per
    /// piece at its centroid instead (for point-to-surface measures: no point on an edge, where the nearest triangle is not unique)
    /// </summary>
    public static VarifoldMeasure Sample(DMesh3 mesh, double h, long maxPoints = 60_000_000, bool centroids = false)
    {
        if (!(h > 0)) throw new ArgumentException("varifold: the spacing must be positive");
        var pts = new List<double>();
        var stack = new Stack<(Vector3d, Vector3d, Vector3d)>();
        double area = 0;
        foreach (int tid in mesh.TriangleIndices()) {
            Vector3d v0 = Vector3d.Zero, v1 = Vector3d.Zero, v2 = Vector3d.Zero;
            mesh.GetTriVertices(tid, ref v0, ref v1, ref v2);
            Vector3d cr = (v1 - v0).Cross(v2 - v0);
            double len = cr.Length;
            if (!(len > 0) || double.IsInfinity(len)) continue;
            Vector3d nrm = cr / len;
            area += 0.5 * len;
            stack.Push((v0, v1, v2));
            while (stack.Count > 0) {
                var (a, b, c) = stack.Pop();
                double ab = a.DistanceSquared(b), bc = b.DistanceSquared(c), ca = c.DistanceSquared(a);
                double longest = Math.Max(ab, Math.Max(bc, ca)), twice = (b - a).Cross(c - a).Length;
                // a sliver (longest^2 > 8 area) longer than h is halved at its longest edge first: the lattice below would
                // fill it with (longest / h)^2 points whatever its area
                if (longest > h * h && longest > 4 * twice) {
                    if (ab == longest) { Vector3d m = 0.5 * (a + b); stack.Push((a, m, c)); stack.Push((m, b, c)); }
                    else if (bc == longest) { Vector3d m = 0.5 * (b + c); stack.Push((a, b, m)); stack.Push((a, m, c)); }
                    else { Vector3d m = 0.5 * (c + a); stack.Push((a, b, m)); stack.Push((m, b, c)); }
                    continue;
                }
                double kk = Math.Ceiling(Math.Sqrt(longest) / h);
                if (!(kk >= 1 && kk < 1e6)) throw new InvalidOperationException("varifold: the spacing " + h + " does not fit the triangles");
                int k = (int)kk;
                if (pts.Count / 7 + 3L * k * (k + 1) / 2 > maxPoints) throw new InvalidOperationException("varifold: more than " + maxPoints + " quadrature points");
                double w = 0.5 * twice / ((double)k * k) / 3;
                Vector3d e1 = (b - a) / k, e2 = (c - a) / k;
                for (int i = 0; i < k; ++i)
                    for (int j = 0; i + j < k; ++j) {
                        if (centroids) {
                            Add(pts, a + (i + 1.0 / 3) * e1 + (j + 1.0 / 3) * e2, nrm, 3 * w);
                            if (i + j < k - 1) Add(pts, a + (i + 2.0 / 3) * e1 + (j + 2.0 / 3) * e2, nrm, 3 * w);
                            continue;
                        }
                        // the three edges of the upright piece with its corner at (i, j); an edge inside the triangle is shared
                        // with the inverted piece on its other side and carries both thirds
                        Add(pts, a + (i + 0.5) * e1 + j * e2, nrm, j > 0 ? 2 * w : w);
                        Add(pts, a + i * e1 + (j + 0.5) * e2, nrm, i > 0 ? 2 * w : w);
                        Add(pts, a + (i + 0.5) * e1 + (j + 0.5) * e2, nrm, i + j < k - 1 ? 2 * w : w);
                    }
            }
        }
        int n = pts.Count / 7;
        var r = new VarifoldMeasure { Count = n, Area = area, X = new double[n], Y = new double[n], Z = new double[n], NX = new double[n], NY = new double[n], NZ = new double[n], A = new double[n] };
        for (int i = 0; i < n; ++i) {
            int o = 7 * i;
            r.X[i] = pts[o]; r.Y[i] = pts[o + 1]; r.Z[i] = pts[o + 2]; r.NX[i] = pts[o + 3]; r.NY[i] = pts[o + 4]; r.NZ[i] = pts[o + 5]; r.A[i] = pts[o + 6];
        }
        return r;
    }

    static void Add(List<double> pts, Vector3d p, Vector3d n, double a)
    {
        pts.Add(p.x); pts.Add(p.y); pts.Add(p.z); pts.Add(n.x); pts.Add(n.y); pts.Add(n.z); pts.Add(a);
    }

    public const int Kernels = 5;
    public static readonly string[] KernelNames = { "mass", "current", "varifold", "oriented2", "oriented8" };

    /// <summary>
    /// sum_f sum_g a_f a_g exp(-|c_f - c_g|^2 / sigma^2) k(n_f . n_g) for the normal kernels k(u) = 1 (mass), u (current),
    /// u^2 (unoriented varifold), ((1 + u) / 2)^2 and ((1 + u) / 2)^8 (oriented varifolds, wide and narrow)
    /// </summary>
    public static double[] Inner(VarifoldMeasure s, VarifoldMeasure t, double sigma)
    {
        var sum = new double[Kernels];
        if (s.Count == 0 || t.Count == 0) return sum;
        // cells of half the cut-off radius over t's box; a point sees the cells within 2 of its own
        double cell = 0.5 * Cut * sigma, inv = 1 / (sigma * sigma), r2max = Cut * Cut * sigma * sigma;
        double ox = double.MaxValue, oy = double.MaxValue, oz = double.MaxValue, mx = double.MinValue, my = double.MinValue, mz = double.MinValue;
        for (int j = 0; j < t.Count; ++j) {
            ox = Math.Min(ox, t.X[j]); oy = Math.Min(oy, t.Y[j]); oz = Math.Min(oz, t.Z[j]);
            mx = Math.Max(mx, t.X[j]); my = Math.Max(my, t.Y[j]); mz = Math.Max(mz, t.Z[j]);
        }
        long nx = (long)Math.Floor((mx - ox) / cell) + 1, ny = (long)Math.Floor((my - oy) / cell) + 1, nz = (long)Math.Floor((mz - oz) / cell) + 1;
        if (nx > 2000000 || ny > 2000000 || nz > 2000000) throw new InvalidOperationException("varifold: sigma is too small for the box");
        int nt = t.Count;
        var tkeys = new long[nt]; var torder = new int[nt];
        for (int j = 0; j < nt; ++j) {
            tkeys[j] = (Coord(t.X[j], ox, cell, nx) * ny + Coord(t.Y[j], oy, cell, ny)) * nz + Coord(t.Z[j], oz, cell, nz);
            torder[j] = j;
        }
        SortByKeyThenIndex(tkeys, torder);
        var td = new double[7 * nt];
        for (int j = 0; j < nt; ++j) {
            int o = torder[j], b = 7 * j;
            td[b] = t.X[o]; td[b + 1] = t.Y[o]; td[b + 2] = t.Z[o]; td[b + 3] = t.NX[o]; td[b + 4] = t.NY[o]; td[b + 5] = t.NZ[o]; td[b + 6] = t.A[o];
        }
        // s's points in the order of the same cells (points more than two cells outside t's box have no partner)
        int ns = s.Count;
        var skeys = new long[ns]; var sorder = new int[ns];
        long span = (nx + 4) * (ny + 4) * (nz + 4);
        for (int i = 0; i < ns; ++i) {
            double cx = Math.Floor((s.X[i] - ox) / cell), cy = Math.Floor((s.Y[i] - oy) / cell), cz = Math.Floor((s.Z[i] - oz) / cell);
            bool outside = !(cx >= -2 && cy >= -2 && cz >= -2 && cx < nx + 2 && cy < ny + 2 && cz < nz + 2);
            skeys[i] = outside ? span : (((long)cx + 2) * (ny + 4) + ((long)cy + 2)) * (nz + 4) + ((long)cz + 2);
            sorder[i] = i;
        }
        SortByKeyThenIndex(skeys, sorder);
        var rows = new double[Kernels * ns];
        const int block = 4096;
        int blocks = (ns + block - 1) / block;
        gParallel.ForEach(Interval1i.Range(blocks), (b) => {
            int end = Math.Min(ns, (b + 1) * block);
            long last = -1;
            var lo = new int[25]; var hi = new int[25];
            int ranges = 0;
            for (int i = b * block; i < end; ++i) {
                long key = skeys[i];
                if (key == span) break;         // (sorted: the rest of the block is outside, too)
                if (key != last) {
                    last = key;
                    long cz = key % (nz + 4) - 2, cy = (key / (nz + 4)) % (ny + 4) - 2, cx = key / ((nz + 4) * (ny + 4)) - 2;
                    long z0 = Math.Max(0, cz - 2), z1 = Math.Min(nz - 1, cz + 2);
                    ranges = 0;
                    if (z0 <= z1)
                        for (long ix = Math.Max(0, cx - 2); ix <= Math.Min(nx - 1, cx + 2); ++ix)
                            for (long iy = Math.Max(0, cy - 2); iy <= Math.Min(ny - 1, cy + 2); ++iy) {
                                long k0 = (ix * ny + iy) * nz;
                                int from = LowerBound(tkeys, k0 + z0), to = LowerBound(tkeys, k0 + z1 + 1);
                                if (to > from) { lo[ranges] = from; hi[ranges] = to; ranges++; }
                            }
                }
                int o = sorder[i];
                double x = s.X[o], y = s.Y[o], z = s.Z[o], pnx = s.NX[o], pny = s.NY[o], pnz = s.NZ[o];
                double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
                for (int r = 0; r < ranges; ++r)
                    for (int j = 7 * lo[r], e = 7 * hi[r]; j < e; j += 7) {
                        double dx = td[j] - x, dy = td[j + 1] - y, dz = td[j + 2] - z;
                        double d2 = dx * dx + dy * dy + dz * dz;
                        if (d2 > r2max) continue;
                        double w = td[j + 6] * Math.Exp(-d2 * inv), u = pnx * td[j + 3] + pny * td[j + 4] + pnz * td[j + 5];
                        double h = 0.5 * (1 + u), h2 = h * h, h4 = h2 * h2;
                        s0 += w; s1 += w * u; s2 += w * u * u; s3 += w * h2; s4 += w * h4 * h4;
                    }
                double a = s.A[o];
                int row = Kernels * i;
                rows[row] = a * s0; rows[row + 1] = a * s1; rows[row + 2] = a * s2; rows[row + 3] = a * s3; rows[row + 4] = a * s4;
            }
        });
        // (in the order of the sorted points, whatever the threads did)
        for (int i = 0; i < ns; ++i) for (int k = 0; k < Kernels; ++k) sum[k] += rows[Kernels * i + k];
        return sum;
    }

    static long Coord(double v, double o, double cell, long n)
    {
        long c = (long)Math.Floor((v - o) / cell);
        return c < 0 ? 0 : c >= n ? n - 1 : c;
    }

    // ascending by key, equal keys by index (Array.Sort is not stable)
    static void SortByKeyThenIndex(long[] keys, int[] order)
    {
        Array.Sort(keys, order);
        for (int j = 0; j < keys.Length;) { int e = j; while (e < keys.Length && keys[e] == keys[j]) ++e; Array.Sort(order, j, e - j); j = e; }
    }

    static int LowerBound(long[] keys, long key)
    {
        int lo = 0, hi = keys.Length;
        while (lo < hi) { int mid = (lo + hi) >> 1; if (keys[mid] < key) lo = mid + 1; else hi = mid; }
        return lo;
    }
}
