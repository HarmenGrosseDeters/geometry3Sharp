// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;

/// <summary>
/// Measurement spike (docs/research/GEOGRAM_MEASUREMENTS_2026-11.md, item 1): the pin-free spectral conformal
/// parameterization of Mullen, Tong, Alliez, Desbrun, "Spectral Conformal Parameterization", SGP 2008, written from
/// the paper. Not library code.
///
/// E_C(x) = E_D(x) - A(x) = x^T A x over x = (u_0, v_0, u_1, v_1, ...), with plain cotan weights (no mollification:
/// the spike's inputs have no degenerate triangles) and the signed UV area as a sum over the boundary edges (the same
/// energy as LSCMParameterization). Instead of two pins, the paper minimizes E_C subject to x^T B x = 1 and
/// x B-orthogonal to the two translations, with B the lumped boundary mass (each boundary vertex: half the length of
/// its two boundary edges, for u and v). The minimizer is the eigenvector of A x = lambda B x of the smallest nonzero
/// eigenvalue (a pair: x and its rotation by 90 degrees, so any vector of the pair's space is the same map up to a
/// rotation). Computed by inverse power iteration on SparseCholesky: A is only semidefinite (the translations are its
/// null space), so A + sigma B is factorized once, with sigma a small fraction of the trace ratio, and each iterate is
/// projected B-orthogonally off the translations; stop when the B-norm of the change of the (B-normalized) iterate is below Tol.
/// </summary>
public sealed class SpectralConformal
{
    public double Tol = 1e-10;
    public int MaxIterations = 200;
    public double ShiftFraction = 1e-9;

    public bool SecondPair;
    /// <summary> start from the projection on the xy plane instead of the least-squares plane (to test the start dependence) </summary>
    public bool StartXY;
    /// <summary> the first pair's iteration met Tol before MaxIterations </summary>
    public bool Converged;
    public int Iterations, Iterations2;
    public double Lambda2;
    public double Lambda, Residual, FactorMs, IterateMs;
    public string Error;

    public Vector2d[] Compute(DMesh3 m)
    {
        Error = null;
        int[] idx = new int[m.MaxVertexID];
        int n = 0;
        for (int v = 0; v < m.MaxVertexID; ++v) idx[v] = m.IsVertex(v) && m.GetVtxTriangleCount(v) > 0 ? n++ : -1;
        int N = 2 * n;
        var entries = new List<(int, int, double)>();
        void add(int r, int c, double val) => entries.Add((r, c, val));
        // Dirichlet: per triangle corner k (opposite edge i, j): 1/4 cot(k) |x_i - x_j|^2, for u and v
        foreach (int t in m.TriangleIndices()) {
            Index3i tri = m.GetTriangle(t);
            for (int k = 0; k < 3; ++k) {
                int vk = tri[k], vi = tri[(k + 1) % 3], vj = tri[(k + 2) % 3];
                Vector3d a = m.GetVertex(vi) - m.GetVertex(vk), b = m.GetVertex(vj) - m.GetVertex(vk);
                double c = 0.25 * a.Dot(b) / a.Cross(b).Length;
                int i = idx[vi], j = idx[vj];
                for (int s = 0; s < 2; ++s) {
                    add(2 * i + s, 2 * i + s, c); add(2 * j + s, 2 * j + s, c); add(2 * i + s, 2 * j + s, -c);
                }
            }
        }
        // minus the signed area: -1/2 sum over boundary edges (i -> j, interior on the left) of (u_i v_j - u_j v_i);
        // SparseCholesky.Add puts an off-diagonal value in both triangles, so each term gets half of it
        double[] mass = new double[n];
        var loops = new MeshBoundaryLoops(m);
        if (loops.Count == 0) { Error = "no boundary"; return null; }
        foreach (EdgeLoop loop in loops) {
            int[] lv = loop.Vertices;
            // orient the loop so that the interior is on the left: the edge (a, b) must appear as a -> b in its triangle
            Index2i ev = m.GetOrientedBoundaryEdgeV(m.FindEdge(lv[0], lv[1]));
            bool forward = ev.a == lv[0];
            for (int k = 0; k < lv.Length; ++k) {
                int a = lv[k], b = lv[(k + 1) % lv.Length];
                if (!forward) (a, b) = (b, a);
                int i = idx[a], j = idx[b];
                add(2 * i, 2 * j + 1, -0.25);
                add(2 * j, 2 * i + 1, 0.25);
                double len = m.GetVertex(a).Distance(m.GetVertex(b));
                mass[i] += 0.5 * len; mass[j] += 0.5 * len;
            }
        }
        double trA = 0, trB = 0;
        foreach (var (r, c, val) in entries) if (r == c) trA += val;
        for (int i = 0; i < n; ++i) trB += 2 * mass[i];
        double sigma = ShiftFraction * trA / trB;
        var sw = Stopwatch.StartNew();
        var chol = new SparseCholesky(N, 2);
        foreach (var (r, c, val) in entries) chol.Add(r, c, val);
        for (int i = 0; i < n; ++i) { chol.Add(2 * i, 2 * i, sigma * mass[i]); chol.Add(2 * i + 1, 2 * i + 1, sigma * mass[i]); }
        if (!chol.Factorize()) { Error = "factorization: " + chol.Error; return null; }
        FactorMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        // A as a matrix for the Rayleigh quotient and the residual
        var rows = new Dictionary<long, double>();
        foreach (var (r, c, val) in entries) {
            long key = (long)r * N + c; rows[key] = rows.GetValueOrDefault(key) + val;
            if (r != c) { key = (long)c * N + r; rows[key] = rows.GetValueOrDefault(key) + val; }
        }
        var Ar = rows.Select(kv => ((int)(kv.Key / N), (int)(kv.Key % N), kv.Value)).ToArray();
        double[] mulA(double[] x) { var y = new double[N]; foreach (var (r, c, val) in Ar) y[r] += val * x[c]; return y; }
        double[] mulB(double[] x) { var y = new double[N]; for (int i = 0; i < n; ++i) { y[2 * i] = mass[i] * x[2 * i]; y[2 * i + 1] = mass[i] * x[2 * i + 1]; } return y; }
        double dot(double[] a, double[] b) { double s = 0; for (int i = 0; i < a.Length; ++i) s += a[i] * b[i]; return s; }
        double totalMass = mass.Sum();
        // B-orthogonal projection off the translations and off the vectors in 'extra' (B-orthonormal), then B-normalize
        void deflate(double[] x, List<double[]> extra)
        {
            for (int s = 0; s < 2; ++s) {
                double mean = 0;
                for (int i = 0; i < n; ++i) mean += mass[i] * x[2 * i + s];
                mean /= totalMass;
                for (int i = 0; i < n; ++i) x[2 * i + s] -= mean;
            }
            foreach (var e in extra) {
                double c = dot(x, mulB(e));
                for (int i = 0; i < N; ++i) x[i] -= c * e[i];
            }
            double norm = Math.Sqrt(dot(x, mulB(x)));
            for (int i = 0; i < N; ++i) x[i] /= norm;
        }
        // inverse power iteration; stops when the B-norm of the change of the iterate is below Tol (after the sign is
        // matched; within the eigenvalue pair the iteration does not rotate: both have the same eigenvalue)
        bool converged = false;
        double iterate(double[] x, List<double[]> extra, out int its)
        {
            deflate(x, extra);
            var yv = new double[N];
            converged = false;
            for (its = 0; its < MaxIterations;) {
                chol.Solve(mulB(x), yv);
                deflate(yv, extra);
                ++its;
                double sgn = dot(yv, mulB(x)) < 0 ? -1 : 1, ch = 0;
                for (int i = 0; i < N; ++i) { double d = sgn * yv[i] - x[i]; ch += mass[i / 2] * d * d; x[i] = sgn * yv[i]; }
                if (Math.Sqrt(ch) <= Tol) { converged = true; break; }
            }
            return dot(x, mulA(x));
        }
        // start: the positions projected on the least-squares plane of the vertices (deterministic, no pins)
        var xk = new double[N];
        {
            var pts = new List<Vector3d>();
            for (int v = 0; v < m.MaxVertexID; ++v) if (idx[v] >= 0) pts.Add(m.GetVertex(v));
            var fit = new OrthogonalPlaneFit3(pts);
            Vector3d nrm = fit.Normal, e1 = Math.Abs(nrm.x) < 0.9 ? nrm.Cross(Vector3d.AxisX).Normalized : nrm.Cross(Vector3d.AxisY).Normalized;
            Vector3d e2 = nrm.Cross(e1);
            for (int v = 0; v < m.MaxVertexID; ++v) {
                if (idx[v] < 0) continue;
                Vector3d d = m.GetVertex(v) - fit.Origin;
                xk[2 * idx[v]] = d.Dot(e1); xk[2 * idx[v] + 1] = d.Dot(e2);
            }
        }
        var start = (double[])xk.Clone();
        if (StartXY)
            for (int v = 0; v < m.MaxVertexID; ++v)
                if (idx[v] >= 0) { xk[2 * idx[v]] = m.GetVertex(v).x; xk[2 * idx[v] + 1] = m.GetVertex(v).y; }
        double lambda = iterate(xk, new List<double[]>(), out Iterations);
        Converged = converged;
        if (SecondPair) {
            // the next eigenvalue pair: deflate x and its rotation J x = (-v, u) and iterate again from the same start
            var jx = new double[N];
            for (int i = 0; i < n; ++i) { jx[2 * i] = -xk[2 * i + 1]; jx[2 * i + 1] = xk[2 * i]; }
            var ex = new List<double[]> { (double[])xk.Clone() };
            double c = dot(jx, mulB(xk)); for (int i = 0; i < N; ++i) jx[i] -= c * xk[i];
            double nj = Math.Sqrt(dot(jx, mulB(jx))); for (int i = 0; i < N; ++i) jx[i] /= nj;
            ex.Add(jx);
            // a start with all modes in it: the plane projection plus a deterministic perturbation
            var x2 = (double[])start.Clone();
            for (int i = 0; i < N; ++i) x2[i] += 0.1 * Math.Sin(12.9898 * i);
            Lambda2 = iterate(x2, ex, out Iterations2);
        }
        Lambda = lambda;
        var ax = mulA(xk); var bx = mulB(xk);
        double rn = 0; for (int i = 0; i < N; ++i) rn += (ax[i] - lambda * bx[i]) * (ax[i] - lambda * bx[i]);
        Residual = Math.Sqrt(rn) / Math.Sqrt(dot(ax, ax));
        IterateMs = sw.Elapsed.TotalMilliseconds;
        // to UVs by vertex id, centred and scaled to the surface area (as LSCM with automatic pins)
        var uv = new Vector2d[m.MaxVertexID];
        for (int v = 0; v < m.MaxVertexID; ++v) if (idx[v] >= 0) uv[v] = new Vector2d(xk[2 * idx[v]], xk[2 * idx[v] + 1]);
        double areaUV = 0, area3 = 0;
        foreach (int t in m.TriangleIndices()) {
            Index3i tri = m.GetTriangle(t);
            areaUV += 0.5 * (uv[tri.b] - uv[tri.a]).Cross(uv[tri.c] - uv[tri.a]);
            area3 += m.GetTriArea(t);
        }
        double scale = Math.Abs(areaUV) > 0 ? Math.Sqrt(area3 / Math.Abs(areaUV)) : 1;
        for (int v = 0; v < m.MaxVertexID; ++v) if (idx[v] >= 0) uv[v] *= scale;
        return uv;
    }
}

/// <summary> The spectral spike against LSCM: the P1 cylinders, the P3 plate, the benchmark meshes. </summary>
static class SpectralComparison
{
    static string F(double x) => x.ToString("G4", CultureInfo.InvariantCulture);

    // the open cylinder of finding P1: sides x rings, radius 1, ring spacing dz
    static DMesh3 OpenCylinder(int sides, int rings, double dz, double topRadius = 1, double jitter = 0)
    {
        var rnd = new Random(7);
        var m = new DMesh3();
        for (int j = 0; j <= rings; ++j)
            for (int i = 0; i < sides; ++i) {
                double a = 2 * Math.PI * i / sides, r = 1 + (topRadius - 1) * j / rings;
                double jz = jitter * dz * (rnd.NextDouble() - 0.5);
                m.AppendVertex(new Vector3d(r * Math.Cos(a), r * Math.Sin(a), dz * j + jz));
            }
        for (int j = 0; j < rings; ++j)
            for (int i = 0; i < sides; ++i) {
                int a = j * sides + i, b = j * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
            }
        return m;
    }

    // the plate of finding P3 (ParameterizationIssueCasesTests.TwoIdenticalRectangularHoles_SymmetricResult)
    static DMesh3 Plate(out List<(int, int)> pairs, out int[,] ids, int nx = 18, int ny = 12)
    {
        double h = 3.0 / nx;
        Func<int, int, bool> keep = (i, j) => !((i >= 3 && i < 5 || i >= 13 && i < 15) && j >= 4 && j < 7);
        var m = new DMesh3();
        ids = new int[nx + 1, ny + 1];
        for (int j = 0; j <= ny; ++j)
            for (int i = 0; i <= nx; ++i) {
                double x = i * h, y = j * h;
                ids[i, j] = m.AppendVertex(new Vector3d(x, y, 0.3 * Math.Sin(y) + 0.1 * (x - 1.5) * (x - 1.5)));
            }
        for (int j = 0; j < ny; ++j)
            for (int i = 0; i < nx; ++i) {
                if (!keep(i, j)) continue;
                int a = ids[i, j], b = ids[i + 1, j], c = ids[i + 1, j + 1], d = ids[i, j + 1];
                bool left = 2 * i + 1 < nx;
                if (left == ((j % 2) == 0)) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                else { m.AppendTriangle(a, b, d); m.AppendTriangle(b, c, d); }
            }
        foreach (int v in m.VertexIndices().ToList())
            if (m.GetVtxEdgeCount(v) == 0) m.RemoveVertex(v);
        pairs = new List<(int, int)>();
        for (int j = 0; j <= ny; ++j)
            for (int i = 0; i <= nx; ++i)
                if (m.IsVertex(ids[i, j]) && m.IsVertex(ids[nx - i, j])) pairs.Add((ids[i, j], ids[nx - i, j]));
        return m;
    }

    // the metric of the P3 test: the largest difference of UV distances of mirrored pairs (400 random pairs of pairs,
    // seed 5), relative to the UV bounding box diagonal; and the same over all pairs of pairs
    static (double sampled, double all) Asymmetry(DMesh3 m, Vector2d[] uv, List<(int, int)> pairs)
    {
        var box = new AxisAlignedBox2d(uv[m.VertexIndices().First()]);
        foreach (int a in m.VertexIndices()) box.Contain(uv[a]);
        var rnd = new Random(5);
        double worst = 0;
        for (int k = 0; k < 400; ++k) {
            var (a, ma) = pairs[rnd.Next(pairs.Count)];
            var (b, mb) = pairs[rnd.Next(pairs.Count)];
            worst = Math.Max(worst, Math.Abs(uv[a].Distance(uv[b]) - uv[ma].Distance(uv[mb])));
        }
        double all = 0;
        foreach (var (a, ma) in pairs) foreach (var (b, mb) in pairs) all = Math.Max(all, Math.Abs(uv[a].Distance(uv[b]) - uv[ma].Distance(uv[mb])));
        return (worst / box.DiagonalLength, all / box.DiagonalLength);
    }

    // the UV size of a ring of the cylinder: the mean distance of its vertices from the UV centroid of the bottom ring
    // (for an annulus map the rings are near-concentric circles); and the smallest UV extent of the ring over its largest
    static (double radius, double roundness) Ring(Vector2d[] uv, int sides, int ring, Vector2d centre)
    {
        double sum = 0, rmin = double.MaxValue, rmax = 0;
        for (int i = 0; i < sides; ++i) {
            double r = uv[ring * sides + i].Distance(centre);
            sum += r; rmin = Math.Min(rmin, r); rmax = Math.Max(rmax, r);
        }
        return (sum / sides, rmax > 0 ? rmin / rmax : 0);
    }

    static string Dist(ParameterizationDistortion d) =>
        $"flips={d.FlippedTriangles} degenerate={d.DegenerateUVTriangles} conf={F(d.MeanConformal)}/{F(d.MaxConformal)} area={F(d.MeanAreaDistortion)}";

    public static void Run(int maxRings)
    {
        // JIT warm-up
        { var w = OpenCylinder(8, 3, 0.6); new SpectralConformal().Compute(w); new LSCMParameterization(w).Compute(); }

        Console.WriteLine("## P1: open cylinders (radius 1). Exact conformal annulus: inner/outer radius = exp(-height)");
        var cyl = new List<(int, int, double, double, double)>();
        foreach (var (sides, rings) in new[] { (8, 3), (16, 4), (24, 6), (48, 12) })
            foreach (double height in new[] { 0.9, 1.8, 3.6 }) cyl.Add((sides, rings, height, 1, 0));
        // symmetry broken: a frustum (top radius 0.9) and z jitter of 10 % of the ring spacing
        foreach (var (sides, rings) in new[] { (8, 3), (24, 6) })
            foreach (double height in new[] { 1.8, 3.6 }) { cyl.Add((sides, rings, height, 0.9, 0)); cyl.Add((sides, rings, height, 1, 0.1)); }
        foreach (var (sides, rings, height, top, jit) in cyl) {
                DMesh3 m = OpenCylinder(sides, rings, height / rings, top, jit);
                var lscm = new LSCMParameterization(m).Compute();
                // variants: the default; an xy-plane start on the symmetric tall cylinders (the start dependence); 5000 iterations on
                // the jittered ones (their gap is tiny)
                var variants = new List<(string, SpectralConformal)> { ("spectral", new SpectralConformal { SecondPair = true }) };
                if (top == 1 && jit == 0 && height > 3) variants.Add(("spec-xy", new SpectralConformal { SecondPair = true, StartXY = true }));
                if (jit > 0) variants.Add(("spec-5k", new SpectralConformal { SecondPair = true, MaxIterations = 5000 }));
                var runs = new List<(string, Vector2d[], string, SpectralConformal, double)> { ("LSCM", lscm.UV, lscm.Status.ToString(), null, 0) };
                foreach (var (vname, sc) in variants) {
                    var sw = Stopwatch.StartNew();
                    Vector2d[] uv = sc.Compute(m);
                    runs.Add((vname, uv, sc.Error ?? (sc.Converged ? "ok" : "not converged"), sc, sw.Elapsed.TotalMilliseconds));
                }
                foreach (var (name, map, status, sc, ms) in runs) {
                    if (map == null) { Console.WriteLine($"{name,-8} {sides}x{rings} h={height} top={top} jitter={jit}: {status}"); continue; }
                    var d = ParameterizationDistortion.Compute(m, map);
                    // centre: the UV centroid of ring 0
                    Vector2d c0 = Vector2d.Zero;
                    for (int i = 0; i < sides; ++i) c0 += map[i];
                    c0 /= sides;
                    var r0 = Ring(map, sides, 0, c0); var r1 = Ring(map, sides, rings, c0);
                    double inner = Math.Min(r0.radius, r1.radius), outer = Math.Max(r0.radius, r1.radius);
                    var rIn = r0.radius < r1.radius ? r0 : r1;
                    Console.WriteLine($"{name,-8} {sides}x{rings} h={height} top={top} jitter={jit}: {status} {Dist(d)} inner/outer={F(inner / outer)} (exact {F(top == 1 ? Math.Exp(-height) : double.NaN)}) inner roundness={F(rIn.roundness)}" +
                        (sc != null ? $" it={sc.Iterations} lambda={F(sc.Lambda)} next pair={F(sc.Lambda2)} (it={sc.Iterations2}) residual={F(sc.Residual)} {F(ms)} ms (with the next pair)" : ""));
                }
            }

        Console.WriteLine("## P3: plate with two mirrored holes (asymmetry: mirrored-pair distance difference / UV diagonal; 400 sampled, all)");
        {
            DMesh3 m = Plate(out var pairs, out int[,] ids);
            var lscm = new LSCMParameterization(m).Compute();
            var a0 = Asymmetry(m, lscm.UV, pairs);
            Console.WriteLine($"LSCM auto pins: {lscm.Status} {Dist(lscm.Distortion)} asym={F(a0.sampled)} all={F(a0.all)}");
            var pins = new Dictionary<int, Vector2d> { [ids[0, 0]] = Vector2d.Zero, [ids[18, 0]] = new Vector2d(3, 0) };
            var lp = new LSCMParameterization(m) { PinnedVertices = pins }.Compute();
            var a1 = Asymmetry(m, lp.UV, pairs);
            Console.WriteLine($"LSCM mirrored pins: {lp.Status} {Dist(lp.Distortion)} asym={F(a1.sampled)} all={F(a1.all)}");
            foreach (double tol in new[] { 1e-6, 1e-10 }) {
                var sc = new SpectralConformal { Tol = tol };
                var uv = sc.Compute(m);
                var a2 = Asymmetry(m, uv, pairs);
                Console.WriteLine($"spectral tol={tol}: {Dist(ParameterizationDistortion.Compute(m, uv))} asym={F(a2.sampled)} all={F(a2.all)} it={sc.Iterations} residual={F(sc.Residual)}");
            }
        }

        Console.WriteLine("## ParameterizationBenchmark meshes (time: LSCM Compute; spectral: factorization + iterations)");
        for (int rings = 30; rings <= maxRings; rings *= 2) {
            var meshes = new (string, DMesh3)[] {
                ("hemi", Program.Cap(Math.PI / 2, rings)), ("cap0.8", Program.Cap(0.8 * Math.PI, rings)),
                ("ellipsoid", Program.Cap(Math.PI / 2, rings, 2)), ("cylinder", Program.Cylinder(rings)) };
            foreach (var (name, m) in meshes) {
                var sw = Stopwatch.StartNew();
                var lscm = new LSCMParameterization(m).Compute();
                double lms = sw.Elapsed.TotalMilliseconds;
                string line = $"{name,-9} V={m.VertexCount,6} LSCM {F(lms)} ms {Dist(lscm.Distortion)}";
                foreach (double shift in new[] { 1e-6, 1e-9 }) {
                    var sc = new SpectralConformal { ShiftFraction = shift };
                    sw.Restart();
                    var uv = sc.Compute(m);
                    double sms = sw.Elapsed.TotalMilliseconds;
                    line += $" | spectral shift {shift}: {F(sms)} ms (factor {F(sc.FactorMs)}, it={sc.Iterations} {F(sc.IterateMs)}) " +
                        (uv == null ? sc.Error : Dist(ParameterizationDistortion.Compute(m, uv)) + $" residual={F(sc.Residual)}");
                }
                Console.WriteLine(line);
            }
        }
    }
}
