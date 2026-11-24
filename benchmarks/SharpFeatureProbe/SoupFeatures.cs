// Crease detection and curvature without connectivity, from the unoriented varifold of a soup or a point cloud
// (docs/research/VARIFOLD_FEATURES_CURVATURE_2026-11.md). No library change. Written from the papers (no code read):
//   - B. Buet, G. P. Leonardi, S. Masnou, "A varifold approach to surface approximation", ARMA 226, 2017, and "Weak and
//     approximate curvatures of a measure: a varifold perspective", Nonlinear Analysis 222, 2022: the regularised first
//     variation  H_e(x) = -(dV * rho'_e)(x) / (|V| * xi_e)(x)  with the kernel pair xi(r) = -r rho'(r) / d;
//   - J. E. Hutchinson, "Second fundamental form for varifolds and the existence of surfaces minimising curvature", Indiana
//     Univ. Math. J. 35, 1986: the curvature of a varifold as the (weak) derivative of its tangent projector P;
//   - baselines: M. Pauly, M. Gross, L. Kobbelt, "Efficient simplification of point-sampled surfaces", IEEE Vis 2002 (surface
//     variation of the position covariance), F. Cazals, M. Pouget, "Estimating differential quantities using polynomial fitting
//     of osculating jets", CAGD 22, 2005 (2-jet fit), and the library's dihedral angles / MeshPrincipalCurvature after a weld.
//
// The measure: points y with mass m_y and unoriented tangent projector P_y = I - n_y n_y^T (soup: the edge-midpoint quadrature
// of VarifoldMeasure with the triangle normals, sign ignored; point cloud: equal masses and a local PCA). Kernel
// rho(r) = (1 - r^2 / e^2)^4 on r < e.
//
// Curvature at x (the projector derivative, Hutchinson's identity made discrete with the BLM kernel pair):
//   d_l P(x) ~ sum_y m_y rho'(r)/r (y - x)_l (P_y - P_x) / ((1/2) sum_y m_y rho'(r) r)   (exact for a linear P on a plane),
//   and with (P_y - P_x) n = n - (n_y . n) n_y the shape operator S_ab = -e_b . (D_a P) n in the tangent frame (e_1, e_2, n)
//   of P_x (the kernel mean of P_y). |K1| >= |K2| are the eigenvalues of S's symmetric part, by magnitude (no orientation).
// Mean curvature (BLM, the literal first variation): |H| with H = sum_y m_y rho'(r)/r P_y (y - x) / ((1/2) sum m_y rho'(r) r).
//
// Crease at a soup edge (p, q) of a triangle with normal n_t (a two-plane decomposition of the varifold in the ball of radius e
// around the edge's midpoint): the mass whose line n_y is within 20 degrees of n_t is plane A, the rest plane B (each needs
// at least 20 % of the mass); each plane is the mass-weighted centroid and the main eigenvector of sum m n n^T; their line L;
// the dihedral is 180 degrees minus the angle between the half-planes (from L towards each centroid), so it needs no
// orientation. Crease: dihedral >= 30 degrees and both ends of the edge within 0.3 h + 3 rms(plane residuals) of L.
//
// Rows (queries) are computed in parallel, each in a fixed order of cells and points: the result does not depend on the
// threads. All lengths are relative to the soup's median edge length h: a scale by 2^k gives the scaled result bit for bit.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using gs;

sealed class PointSet
{
    public double[] X, Y, Z, NX, NY, NZ, A;
    public int Count;
    public static PointSet From(VarifoldMeasure m) => new PointSet { X = m.X, Y = m.Y, Z = m.Z, NX = m.NX, NY = m.NY, NZ = m.NZ, A = m.A, Count = m.Count };
}

/// <summary> the points sorted by cell of size e; a query visits the 27 cells around a point in a fixed order </summary>
sealed class SortedGrid
{
    readonly PointSet s; readonly double cell; readonly Vector3d o; readonly long nx, ny, nz;
    readonly long[] keys; readonly int[] order;
    public readonly double R;
    public SortedGrid(PointSet ps, double radius)
    {
        s = ps; R = radius; cell = radius;
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        for (int i = 0; i < ps.Count; ++i) {
            x0 = Math.Min(x0, ps.X[i]); y0 = Math.Min(y0, ps.Y[i]); z0 = Math.Min(z0, ps.Z[i]);
            x1 = Math.Max(x1, ps.X[i]); y1 = Math.Max(y1, ps.Y[i]); z1 = Math.Max(z1, ps.Z[i]);
        }
        o = new Vector3d(x0, y0, z0);
        nx = (long)Math.Floor((x1 - x0) / cell) + 1; ny = (long)Math.Floor((y1 - y0) / cell) + 1; nz = (long)Math.Floor((z1 - z0) / cell) + 1;
        keys = new long[ps.Count]; order = new int[ps.Count];
        for (int i = 0; i < ps.Count; ++i) { keys[i] = Key(C(ps.X[i], o.x, nx), C(ps.Y[i], o.y, ny), C(ps.Z[i], o.z, nz)); order[i] = i; }
        Array.Sort(keys, order);
        for (int j = 0; j < keys.Length;) { int e = j; while (e < keys.Length && keys[e] == keys[j]) ++e; Array.Sort(order, j, e - j); j = e; }
    }
    long C(double v, double o0, long n) { long c = (long)Math.Floor((v - o0) / cell); return c < 0 ? 0 : c >= n ? n - 1 : c; }
    long Key(long x, long y, long z) => (x * ny + y) * nz + z;

    /// <summary> the indices of the points within R of p, in a fixed order (cleared first) </summary>
    public void Query(Vector3d p, List<int> outIdx)
    {
        outIdx.Clear();
        long cx = (long)Math.Floor((p.x - o.x) / cell), cy = (long)Math.Floor((p.y - o.y) / cell), cz = (long)Math.Floor((p.z - o.z) / cell);
        double r2 = R * R;
        for (long x = cx - 1; x <= cx + 1; ++x) {
            if (x < 0 || x >= nx) continue;
            for (long y = cy - 1; y <= cy + 1; ++y) {
                if (y < 0 || y >= ny) continue;
                for (long z = cz - 1; z <= cz + 1; ++z) {
                    if (z < 0 || z >= nz) continue;
                    long k = Key(x, y, z);
                    for (int j = LowerBound(k); j < keys.Length && keys[j] == k; ++j) {
                        int i = order[j];
                        double dx = s.X[i] - p.x, dy = s.Y[i] - p.y, dz = s.Z[i] - p.z;
                        if (dx * dx + dy * dy + dz * dz < r2) outIdx.Add(i);
                    }
                }
            }
        }
    }
    int LowerBound(long key)
    {
        int lo = 0, hi = keys.Length;
        while (lo < hi) { int mid = (lo + hi) >> 1; if (keys[mid] < key) lo = mid + 1; else hi = mid; }
        return lo;
    }
}

static class Sym3
{
    /// <summary> eigen decomposition of a symmetric 3 x 3 (xx, xy, xz, yy, yz, zz) by cyclic Jacobi; values descending </summary>
    public static void Eigen(double[] m6, double[] val, Vector3d[] vec)
    {
        double[,] a = { { m6[0], m6[1], m6[2] }, { m6[1], m6[3], m6[4] }, { m6[2], m6[4], m6[5] } };
        double[,] v = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 50; ++sweep) {
            double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            double sc = Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]);
            if (off <= 1e-15 * sc || off == 0) break;
            for (int p = 0; p < 2; ++p)
                for (int q = p + 1; q < 3; ++q) {
                    if (a[p, q] == 0) continue;
                    double th = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(th) / (Math.Abs(th) + Math.Sqrt(th * th + 1));
                    if (th == 0) t = 1;
                    double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                    for (int k = 0; k < 3; ++k) { double akp = a[k, p], akq = a[k, q]; a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq; }
                    for (int k = 0; k < 3; ++k) { double apk = a[p, k], aqk = a[q, k]; a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk; }
                    for (int k = 0; k < 3; ++k) { double vkp = v[k, p], vkq = v[k, q]; v[k, p] = c * vkp - s * vkq; v[k, q] = s * vkp + c * vkq; }
                }
        }
        int[] idx = { 0, 1, 2 };
        Array.Sort(idx, (i, j) => a[j, j].CompareTo(a[i, i]) != 0 ? a[j, j].CompareTo(a[i, i]) : i.CompareTo(j));
        for (int k = 0; k < 3; ++k) { int i = idx[k]; val[k] = a[i, i]; vec[k] = new Vector3d(v[0, i], v[1, i], v[2, i]); }
    }
    public static void AddOuter(double[] m6, Vector3d n, double w)
    {
        m6[0] += w * n.x * n.x; m6[1] += w * n.x * n.y; m6[2] += w * n.x * n.z; m6[3] += w * n.y * n.y; m6[4] += w * n.y * n.z; m6[5] += w * n.z * n.z;
    }
}

static class SoupFeatures
{
    public static int Threads = Environment.ProcessorCount;
    static ParallelOptions Opt => new ParallelOptions { MaxDegreeOfParallelism = Threads };

    public static double MedianEdge(Soup s)
    {
        var l = new double[3 * s.Count];
        for (int t = 0; t < s.Count; ++t) for (int k = 0; k < 3; ++k) l[3 * t + k] = s.V[3 * t + k].Distance(s.V[3 * t + (k + 1) % 3]);
        Array.Sort(l);
        return l.Length == 0 ? 0 : l[l.Length / 2];
    }

    static Vector3d P(PointSet s, int i) => new Vector3d(s.X[i], s.Y[i], s.Z[i]);
    static Vector3d Nrm(PointSet s, int i) => new Vector3d(s.NX[i], s.NY[i], s.NZ[i]);

    // ---------------- creases ----------------

    public sealed class CreaseResult { public bool[] Flag; public double[] Dih; public double Seconds; }

    /// <summary> per soup edge (3 t + k: from corner k to k + 1): the varifold two-plane crease test at radius c h </summary>
    public static CreaseResult VarifoldCreases(Soup s, double h, double c = 2.0, double qf = 2.0, double thetaDeg = 30)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double eps = c * h;
        var ps = PointSet.From(VarifoldMeasure.Sample(s.ToMesh(), eps / qf));
        var grid = new SortedGrid(ps, eps);
        int ne = 3 * s.Count;
        var res = new CreaseResult { Flag = new bool[ne], Dih = new double[ne] };
        double cosA = Math.Cos(20 * MathUtil.Deg2Rad), sinMin = Math.Sin(10 * MathUtil.Deg2Rad);
        Parallel.For(0, s.Count, Opt, () => new List<int>(), (t, _, idx) => {
            Vector3d a = s.V[3 * t], b = s.V[3 * t + 1], cc = s.V[3 * t + 2];
            Vector3d nt = (b - a).Cross(cc - a);
            if (!(nt.Length > 0)) return idx;
            nt = nt.Normalized;
            for (int k = 0; k < 3; ++k) {
                Vector3d p = s.V[3 * t + k], q = s.V[3 * t + (k + 1) % 3];
                Vector3d m = 0.5 * (p + q);
                grid.Query(m, idx);
                double WA = 0, WB = 0; var MA = new double[6]; var MB = new double[6]; var QA = new double[6]; var QB = new double[6];
                Vector3d CA = Vector3d.Zero, CB = Vector3d.Zero;
                foreach (int i in idx) {
                    Vector3d y = P(ps, i) - m, n = Nrm(ps, i);
                    double tt = y.LengthSquared / (eps * eps), w = ps.A[i] * Math.Pow(1 - tt, 4);
                    if (Math.Abs(n.Dot(nt)) >= cosA) { WA += w; Sym3.AddOuter(MA, n, w); CA += w * y; Sym3.AddOuter(QA, y, w); }
                    else { WB += w; Sym3.AddOuter(MB, n, w); CB += w * y; Sym3.AddOuter(QB, y, w); }
                }
                double W = WA + WB;
                if (!(WB >= 0.2 * W && WA >= 0.2 * W)) continue;
                var val = new double[3]; var vec = new Vector3d[3];
                Sym3.Eigen(MA, val, vec); Vector3d nA = vec[0];
                Sym3.Eigen(MB, val, vec); Vector3d nB0 = vec[0];
                // plane B again: only the mass within 20 degrees of its main line (near a corner the rest holds a third plane)
                WB = 0; Array.Clear(MB, 0, 6); Array.Clear(QB, 0, 6); CB = Vector3d.Zero;
                foreach (int i in idx) {
                    Vector3d y = P(ps, i) - m, n = Nrm(ps, i);
                    if (Math.Abs(n.Dot(nt)) >= cosA || Math.Abs(n.Dot(nB0)) < cosA) continue;
                    double w = ps.A[i] * Math.Pow(1 - y.LengthSquared / (eps * eps), 4);
                    WB += w; Sym3.AddOuter(MB, n, w); CB += w * y; Sym3.AddOuter(QB, y, w);
                }
                if (!(WB >= 0.2 * W)) continue;
                Sym3.Eigen(MB, val, vec); Vector3d nB = vec[0];
                CA /= WA; CB /= WB;
                Vector3d d = nA.Cross(nB);
                if (!(d.Length > sinMin)) continue;
                d = d.Normalized;
                double rmsA = Math.Sqrt(Math.Max(0, Quad(QA, nA) / WA - Sq(nA.Dot(CA)))), rmsB = Math.Sqrt(Math.Max(0, Quad(QB, nB) / WB - Sq(nB.Dot(CB))));
                // the point of L nearest the midpoint (local coordinates: the midpoint is the origin)
                var M = new Matrix3d(nA, nB, d, true);
                Vector3d x0 = M.Inverse() * new Vector3d(nA.Dot(CA), nB.Dot(CB), 0);
                Vector3d uA = d.Cross(nA), uB = d.Cross(nB);
                if (uA.Dot(CA - x0) < 0) uA = -uA;
                if (uB.Dot(CB - x0) < 0) uB = -uB;
                double dih = 180 - Vector3d.AngleD(uA, uB);
                double tol = 0.3 * h + 3 * Math.Max(rmsA, rmsB);
                double dp = LineDist(p - m, x0, d), dq = LineDist(q - m, x0, d);
                res.Dih[3 * t + k] = dih;
                double along = Math.Abs(d.Dot((q - p).Normalized));
                res.Flag[3 * t + k] = dih >= thetaDeg && dp <= tol && dq <= tol && along >= Math.Cos(30 * MathUtil.Deg2Rad);
            }
            return idx;
        }, _ => { });
        res.Seconds = sw.Elapsed.TotalSeconds;
        return res;
    }

    static double Sq(double x) => x * x;
    static double Quad(double[] m6, Vector3d n) => m6[0] * n.x * n.x + m6[3] * n.y * n.y + m6[5] * n.z * n.z + 2 * (m6[1] * n.x * n.y + m6[2] * n.x * n.z + m6[4] * n.y * n.z);
    static double LineDist(Vector3d p, Vector3d x0, Vector3d d) { Vector3d w = p - x0; return (w - w.Dot(d) * d).Length; }

    /// <summary> the baseline: dihedral angles of the welded copy (weld within tol); edges without two triangles are not creases </summary>
    public static CreaseResult WeldDihedral(Soup s, double tol, double thetaDeg = 30)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        DMesh3 m = SoupWeld.Weld(s, tol, out int[] triOf, out int[] vOf);
        int ne = 3 * s.Count;
        var res = new CreaseResult { Flag = new bool[ne], Dih = new double[ne] };
        for (int t = 0; t < s.Count; ++t) {
            if (triOf[t] < 0) continue;
            for (int k = 0; k < 3; ++k) {
                int eid = m.FindEdge(vOf[3 * t + k], vOf[3 * t + (k + 1) % 3]);
                if (eid < 0) continue;
                Index2i et = m.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID) continue;
                double dih = Vector3d.AngleD(m.GetTriNormal(et.a), m.GetTriNormal(et.b));
                res.Dih[3 * t + k] = dih; res.Flag[3 * t + k] = dih >= thetaDeg;
            }
        }
        res.Seconds = sw.Elapsed.TotalSeconds;
        return res;
    }

    /// <summary>
    /// the connectivity-free baseline: surface variation lambda_min / trace of the kernel-weighted position covariance at the
    /// edge midpoint (Pauly et al.), above its value at an ideal 30 degree crease (computed for the same kernel)
    /// </summary>
    public static CreaseResult PcaCreases(Soup s, double h, double c = 2.0, double qf = 2.0, double thetaDeg = 30)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double eps = c * h, thr = IdealVariation(thetaDeg);
        var ps = PointSet.From(VarifoldMeasure.Sample(s.ToMesh(), eps / qf));
        var grid = new SortedGrid(ps, eps);
        var res = new CreaseResult { Flag = new bool[3 * s.Count], Dih = new double[3 * s.Count] };
        Parallel.For(0, s.Count, Opt, () => new List<int>(), (t, _, idx) => {
            for (int k = 0; k < 3; ++k) {
                Vector3d m = 0.5 * (s.V[3 * t + k] + s.V[3 * t + (k + 1) % 3]);
                grid.Query(m, idx);
                double W = 0; var Q = new double[6]; Vector3d C = Vector3d.Zero;
                foreach (int i in idx) {
                    Vector3d y = P(ps, i) - m;
                    double w = ps.A[i] * Math.Pow(1 - y.LengthSquared / (eps * eps), 4);
                    W += w; C += w * y; Sym3.AddOuter(Q, y, w);
                }
                if (!(W > 0)) continue;
                C /= W;
                var cov = new double[6];
                for (int j = 0; j < 6; ++j) cov[j] = Q[j] / W;
                cov[0] -= C.x * C.x; cov[1] -= C.x * C.y; cov[2] -= C.x * C.z; cov[3] -= C.y * C.y; cov[4] -= C.y * C.z; cov[5] -= C.z * C.z;
                var val = new double[3]; var vec = new Vector3d[3];
                Sym3.Eigen(cov, val, vec);
                double sv = val[2] / (val[0] + val[1] + val[2]);
                res.Flag[3 * t + k] = sv >= thr;
            }
            return idx;
        }, _ => { });
        res.Seconds = sw.Elapsed.TotalSeconds;
        return res;
    }

    /// <summary> the surface variation at a point of an ideal crease (two half-planes, turn theta), kernel (1 - r^2)^4, unit radius </summary>
    public static double IdealVariation(double thetaDeg)
    {
        double th = thetaDeg * MathUtil.Deg2Rad; int n = 400;
        double W = 0; var Q = new double[6]; Vector3d C = Vector3d.Zero;
        for (int side = 0; side < 2; ++side)
            for (int i = 0; i < 2 * n; ++i)
                for (int j = 0; j < n; ++j) {
                    double x = -1 + (i + 0.5) / n, sv = (j + 0.5) / n, r2 = x * x + sv * sv;
                    if (r2 >= 1) continue;
                    var y = side == 0 ? new Vector3d(x, -sv, 0) : new Vector3d(x, sv * Math.Cos(th), sv * Math.Sin(th));
                    double w = Math.Pow(1 - r2, 4);
                    W += w; C += w * y; Sym3.AddOuter(Q, y, w);
                }
        C /= W;
        var cov = new double[6];
        for (int j = 0; j < 6; ++j) cov[j] = Q[j] / W;
        cov[0] -= C.x * C.x; cov[1] -= C.x * C.y; cov[2] -= C.x * C.z; cov[3] -= C.y * C.y; cov[4] -= C.y * C.z; cov[5] -= C.z * C.z;
        var val = new double[3]; var vec = new Vector3d[3];
        Sym3.Eigen(cov, val, vec);
        return val[2] / (val[0] + val[1] + val[2]);
    }

    // ---------------- curvature ----------------

    public sealed class CurvResult { public double[] K1, K2, H; public bool[] Valid; public double Seconds; }

    /// <summary> the measure of a soup (quadrature at spacing qh, the triangle normals) </summary>
    public static PointSet SoupMeasure(Soup s, double qh) => PointSet.From(VarifoldMeasure.Sample(s.ToMesh(), qh));

    /// <summary>
    /// a point cloud as a varifold: equal masses (area / n) and unoriented normals from a kernel PCA of the positions within
    /// pcaR (the third eigenvector)
    /// </summary>
    public static PointSet CloudMeasure(List<Vector3d> pts, double area, double pcaR)
    {
        int n = pts.Count;
        var ps = new PointSet { Count = n, X = new double[n], Y = new double[n], Z = new double[n], NX = new double[n], NY = new double[n], NZ = new double[n], A = new double[n] };
        for (int i = 0; i < n; ++i) { ps.X[i] = pts[i].x; ps.Y[i] = pts[i].y; ps.Z[i] = pts[i].z; ps.A[i] = area / n; }
        var grid = new SortedGrid(ps, pcaR);
        Parallel.For(0, n, Opt, () => new List<int>(), (i, _, idx) => {
            Vector3d x = pts[i];
            grid.Query(x, idx);
            double W = 0; var Q = new double[6]; Vector3d C = Vector3d.Zero;
            foreach (int j in idx) { Vector3d y = P(ps, j) - x; double w = Math.Pow(1 - y.LengthSquared / (pcaR * pcaR), 4); W += w; C += w * y; Sym3.AddOuter(Q, y, w); }
            C /= W;
            var cov = new double[6];
            for (int j = 0; j < 6; ++j) cov[j] = Q[j] / W;
            cov[0] -= C.x * C.x; cov[1] -= C.x * C.y; cov[2] -= C.x * C.z; cov[3] -= C.y * C.y; cov[4] -= C.y * C.z; cov[5] -= C.z * C.z;
            var val = new double[3]; var vec = new Vector3d[3];
            Sym3.Eigen(cov, val, vec);
            ps.NX[i] = vec[2].x; ps.NY[i] = vec[2].y; ps.NZ[i] = vec[2].z;
            return idx;
        }, _ => { });
        return ps;
    }

    /// <summary> the varifold curvature (projector derivative) and the BLM mean curvature at the query points, radius eps </summary>
    public static CurvResult VarifoldCurvature(PointSet ps, IList<Vector3d> query, double eps, bool leastSquares = false)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var grid = new SortedGrid(ps, eps);
        int nq = query.Count;
        var r = new CurvResult { K1 = new double[nq], K2 = new double[nq], H = new double[nq], Valid = new bool[nq] };
        Parallel.For(0, nq, Opt, () => new List<int>(), (qi, _, idx) => {
            Vector3d x = query[qi];
            grid.Query(x, idx);
            if (idx.Count < 6) return idx;
            // P_x: the kernel mean of the projectors (the normal: the main eigenvector of sum w n n^T)
            var M = new double[6];
            foreach (int i in idx) { Vector3d y = P(ps, i) - x; Sym3.AddOuter(M, Nrm(ps, i), ps.A[i] * Math.Pow(1 - y.LengthSquared / (eps * eps), 4)); }
            var val = new double[3]; var vec = new Vector3d[3];
            Sym3.Eigen(M, val, vec);
            Vector3d n = vec[0], e1 = vec[1], e2 = vec[2];
            double D = 0; Vector3d g1 = Vector3d.Zero, g2 = Vector3d.Zero, Hn = Vector3d.Zero;
            // least squares variant: (P_y - P_x) n ~ u g1 + v g2 (u, v the tangent coordinates of y - x), weights rho
            double Suu = 0, Suv = 0, Svv = 0; Vector3d Bu = Vector3d.Zero, Bv = Vector3d.Zero;
            foreach (int i in idx) {
                Vector3d y = P(ps, i) - x, ny = Nrm(ps, i);
                double t = y.LengthSquared / (eps * eps), om = 1 - t, om3 = om * om * om;
                double rpOverR = -8 / (eps * eps) * om3;   // rho'(r) / r
                double rpR = -8 * t * om3;                   // rho'(r) r
                double c = ps.A[i] * rpOverR;
                D += 0.5 * ps.A[i] * rpR;
                Vector3d dPn = n - ny.Dot(n) * ny;          // (P_y - P_x) n
                g1 += c * e1.Dot(y) * dPn; g2 += c * e2.Dot(y) * dPn;
                Hn += c * (y - ny.Dot(y) * ny);             // P_y (y - x)
                if (leastSquares) {
                    double w = ps.A[i] * om * om * om * om, u = e1.Dot(y), v = e2.Dot(y);
                    Suu += w * u * u; Suv += w * u * v; Svv += w * v * v; Bu += w * u * dPn; Bv += w * v * dPn;
                }
            }
            if (leastSquares) {
                double det2 = Suu * Svv - Suv * Suv;
                if (!(det2 > 1e-12 * Suu * Svv)) return idx;
                // g = [Suu Suv; Suv Svv]^-1 [Bu; Bv], times D so that the division below undoes it
                Vector3d l1v = (Svv * Bu - Suv * Bv) / det2, l2v = (Suu * Bv - Suv * Bu) / det2;
                g1 = l1v * D; g2 = l2v * D;
            }
            if (!(D < 0)) return idx;
            g1 /= D; g2 /= D; Hn /= D;
            // S_ab = -e_b . (D_a P) n
            double s11 = -e1.Dot(g1), s12 = -e2.Dot(g1), s21 = -e1.Dot(g2), s22 = -e2.Dot(g2), sym = 0.5 * (s12 + s21);
            double tr = 0.5 * (s11 + s22), det = Math.Sqrt(0.25 * Sq(s11 - s22) + sym * sym);
            double l1 = tr + det, l2 = tr - det;
            if (Math.Abs(l2) > Math.Abs(l1)) { var tmp = l1; l1 = l2; l2 = tmp; }
            r.K1[qi] = Math.Abs(l1); r.K2[qi] = Math.Abs(l2); r.H[qi] = Hn.Length; r.Valid[qi] = true;
            return idx;
        }, _ => { });
        r.Seconds = sw.Elapsed.TotalSeconds;
        return r;
    }

    /// <summary>
    /// the 2-jet baseline (Cazals-Pouget): in the PCA frame of the positions within eps, a weighted least squares fit
    /// z = a u^2 + b u v + c v^2 + d u + e v + f, curvatures from its first and second fundamental forms at the origin
    /// </summary>
    public static CurvResult JetCurvature(PointSet ps, IList<Vector3d> query, double eps)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var grid = new SortedGrid(ps, eps);
        int nq = query.Count;
        var r = new CurvResult { K1 = new double[nq], K2 = new double[nq], H = new double[nq], Valid = new bool[nq] };
        Parallel.For(0, nq, Opt, () => new List<int>(), (qi, _, idx) => {
            Vector3d x = query[qi];
            grid.Query(x, idx);
            if (idx.Count < 10) return idx;
            double W = 0; var Q = new double[6]; Vector3d C = Vector3d.Zero;
            foreach (int i in idx) { Vector3d y = P(ps, i) - x; double w = ps.A[i] * Math.Pow(1 - y.LengthSquared / (eps * eps), 4); W += w; C += w * y; Sym3.AddOuter(Q, y, w); }
            C /= W;
            var cov = new double[6];
            for (int j = 0; j < 6; ++j) cov[j] = Q[j] / W;
            cov[0] -= C.x * C.x; cov[1] -= C.x * C.y; cov[2] -= C.x * C.z; cov[3] -= C.y * C.y; cov[4] -= C.y * C.z; cov[5] -= C.z * C.z;
            var val = new double[3]; var vec = new Vector3d[3];
            Sym3.Eigen(cov, val, vec);
            Vector3d e1 = vec[0], e2 = vec[1], n = vec[2];
            var A = new double[6, 6]; var bb = new double[6];
            foreach (int i in idx) {
                Vector3d y = P(ps, i) - x;
                double w = ps.A[i] * Math.Pow(1 - y.LengthSquared / (eps * eps), 4);
                double u = e1.Dot(y) / eps, v = e2.Dot(y) / eps, z = n.Dot(y) / eps;   // scaled: a well-conditioned system
                double[] f = { u * u, u * v, v * v, u, v, 1 };
                for (int a = 0; a < 6; ++a) { bb[a] += w * f[a] * z; for (int b = 0; b < 6; ++b) A[a, b] += w * f[a] * f[b]; }
            }
            if (!Solve6(A, bb)) return idx;
            double ca = bb[0] / eps, cb = bb[1] / eps, cc = bb[2] / eps, cd = bb[3], ce = bb[4];
            double E = 1 + cd * cd, F = cd * ce, G = 1 + ce * ce, Wn = Math.Sqrt(1 + cd * cd + ce * ce);
            double L = 2 * ca / Wn, Mm = cb / Wn, N = 2 * cc / Wn;
            // shape operator I^-1 II; its eigenvalues are real
            double det1 = E * G - F * F;
            double s11 = (G * L - F * Mm) / det1, s12 = (G * Mm - F * N) / det1, s21 = (E * Mm - F * L) / det1, s22 = (E * N - F * Mm) / det1;
            double tr = 0.5 * (s11 + s22), disc = Math.Sqrt(Math.Max(0, Sq(0.5 * (s11 - s22)) + s12 * s21));
            double l1 = tr + disc, l2 = tr - disc;
            if (Math.Abs(l2) > Math.Abs(l1)) { var tmp = l1; l1 = l2; l2 = tmp; }
            r.K1[qi] = Math.Abs(l1); r.K2[qi] = Math.Abs(l2); r.H[qi] = Math.Abs(l1 + l2); r.Valid[qi] = true;
            return idx;
        }, _ => { });
        r.Seconds = sw.Elapsed.TotalSeconds;
        return r;
    }

    static bool Solve6(double[,] A, double[] b)
    {
        int n = 6;
        for (int c = 0; c < n; ++c) {
            int piv = c;
            for (int r = c + 1; r < n; ++r) if (Math.Abs(A[r, c]) > Math.Abs(A[piv, c])) piv = r;
            if (!(Math.Abs(A[piv, c]) > 1e-14 * Math.Abs(A[5, 5]))) return false;
            if (piv != c) { for (int k = 0; k < n; ++k) { var t = A[c, k]; A[c, k] = A[piv, k]; A[piv, k] = t; } var tb = b[c]; b[c] = b[piv]; b[piv] = tb; }
            for (int r = c + 1; r < n; ++r) {
                double f = A[r, c] / A[c, c];
                for (int k = c; k < n; ++k) A[r, k] -= f * A[c, k];
                b[r] -= f * b[c];
            }
        }
        for (int c = n - 1; c >= 0; --c) { double s = b[c]; for (int k = c + 1; k < n; ++k) s -= A[c, k] * b[k]; b[c] = s / A[c, c]; }
        return true;
    }

    /// <summary> the library baseline: MeshPrincipalCurvature on the welded copy, at the welded vertex of each query corner </summary>
    public static CurvResult WeldCurvature(Soup s, double tol, int[] queryCorners, int rounds)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        DMesh3 m = SoupWeld.Weld(s, tol, out _, out int[] vOf);
        var pc = MeshPrincipalCurvature.Compute(m, rounds);
        int nq = queryCorners.Length;
        var r = new CurvResult { K1 = new double[nq], K2 = new double[nq], H = new double[nq], Valid = new bool[nq] };
        for (int qi = 0; qi < nq; ++qi) {
            int v = vOf[queryCorners[qi]];
            if (!m.IsVertex(v) || !pc.Valid[v] || m.GetVtxEdgeCount(v) == 0) continue;
            double a = Math.Abs(pc.K1[v]), b = Math.Abs(pc.K2[v]);
            r.K1[qi] = Math.Max(a, b); r.K2[qi] = Math.Min(a, b); r.H[qi] = Math.Abs(pc.K1[v] + pc.K2[v]); r.Valid[qi] = true;
        }
        r.Seconds = sw.Elapsed.TotalSeconds;
        return r;
    }
}
