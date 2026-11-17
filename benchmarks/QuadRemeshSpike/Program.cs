// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Quad remeshing feasibility spike (docs/design/QUAD_REMESHING.md, section 3). NOT library code: a throwaway
// prototype that measures what a first quad pipeline built from the library's blocks would deliver.
//
//   1. input: implicit shape -> MarchingCubes -> Remesher (isotropic, edge h = s * ratio)
//   2. cross field: Knöppel, Crane, Pinkall, Schröder 2013 (smoothest 4-RoSy field, vertex based, cotan weights,
//      smallest eigenvector of the connection Laplacian by inverse iteration with SparseCholesky)
//   3. position field after Jakob, Tarini, Panozzo, Sorkine-Hornung 2015 (Instant Meshes), derived from the paper
//      only (the derivation and the own choices are in QUAD_REMESHING.md section 3): extrinsic 4-RoSy compatibility,
//      lattice rounding, Gauss-Seidel smoothing on a coarsening hierarchy
//   4. extraction: union-find of edges with zero lattice offset, unit-offset edges form the graph, faces traced in
//      the tangent planes (a simplified version of the paper's extraction, no cleanup)
//   5. baseline: greedy triangle pairing of an isotropic remesh at edge s (quad-dominant)
//
//   dotnet run -c Release --project benchmarks/QuadRemeshSpike -- [only=<regex>] [ratio=0.5] [sweeps=40] [scale=1] [eps=0.05]
//
// Output: one line per shape and method (Markdown table rows). Single-threaded and deterministic.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using gs;

static class Program
{
    class Fn : BoundedImplicitFunction3d
    {
        public Func<Vector3d, double> F; public AxisAlignedBox3d B;
        public double Value(ref Vector3d p) { return F(p); }
        public AxisAlignedBox3d Bounds() { return B; }
    }

    class Shape { public string Name; public Func<Vector3d, double> F; public AxisAlignedBox3d Box; public int Chi; public double S; public double Noise; }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        double ratio = A.ContainsKey("ratio") ? double.Parse(A["ratio"], CultureInfo.InvariantCulture) : 0.5;
        int sweeps = A.ContainsKey("sweeps") ? int.Parse(A["sweeps"]) : 40;
        if (A.ContainsKey("eps")) MiddleEps = double.Parse(A["eps"], CultureInfo.InvariantCulture);
        double scaleMul = A.ContainsKey("scale") ? double.Parse(A["scale"], CultureInfo.InvariantCulture) : 1.0;

        Func<Vector3d, double> sphere = p => p.Length - 1;
        Func<Vector3d, double> torus = p => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 1; return Math.Sqrt(q * q + p.z * p.z) - 0.35; };
        Func<Vector3d, double> bumpy = p => p.Length - 1 - 0.08 * Math.Sin(4 * p.x) * Math.Sin(4 * p.y) * Math.Sin(4 * p.z);
        Vector3d ext = new Vector3d(1, 0.7, 0.5); double fr = 0.15;
        Func<Vector3d, double> rbox = p => {
            Vector3d q = new Vector3d(Math.Abs(p.x) - (ext.x - fr), Math.Abs(p.y) - (ext.y - fr), Math.Abs(p.z) - (ext.z - fr));
            Vector3d qp = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return qp.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0) - fr;
        };
        var B2 = new AxisAlignedBox3d(new Vector3d(-1.6, -1.6, -1.6), new Vector3d(1.6, 1.6, 1.6));
        var shapes = new List<Shape> {
            new Shape { Name = "sphere", F = sphere, Box = B2, Chi = 2, S = 0.1 },
            new Shape { Name = "torus", F = torus, Box = B2, Chi = 0, S = 0.1 },
            new Shape { Name = "bumpy", F = bumpy, Box = B2, Chi = 2, S = 0.06 },
            new Shape { Name = "filleted-box", F = rbox, Box = B2, Chi = 2, S = 0.08 },
            new Shape { Name = "noisy-sphere", F = sphere, Box = B2, Chi = 2, S = 0.1, Noise = 0.2 },
        };

        Console.WriteLine("| shape | method | input V | singular faces (index x count); field eigenvalue | sum index | faces | quads % | tris | 5-6 | defects (<3 or >6 sides) | non-manifold tris | irreg. V | irreg. after 1 CC | angle dev p50 / p95 (deg) | corners in [70,110] % | aspect p50 / p95 | Hausdorff sym. / diag | Hausdorff out->ref / diag | t field | t pos | t extract (s) |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        {   // JIT warm-up (not reported): the whole pipeline once on a coarse sphere
            var w0 = MakeInput(shapes[0], 0.2, 0); var f0 = new Frames(w0);
            var z0 = SmoothestField(w0, f0, 4, out _, out _); var q0 = new Vector3d[f0.N.Length];
            for (int i = 0; i < q0.Length; ++i) { double phi = Math.Atan2(z0[i].im, z0[i].re) / 4; q0[i] = Math.Cos(phi) * f0.E[i] + Math.Sin(phi) * f0.Bt[i]; }
            Extract(w0, f0, q0, PositionField(w0, f0, q0, 0.4, 5), 0.4); TrianglePairing(w0);
        }
        foreach (var sh in shapes) {
            if (only != null && !only.IsMatch(sh.Name)) continue;
            double s = sh.S * scaleMul;
            DMesh3 input = MakeInput(sh, s * ratio, sh.Noise);
            DMesh3 reference = MakeInput(sh, s * ratio * 0.5, 0);

            // field + position field + extraction
            var sw = Stopwatch.StartNew();
            var fr0 = new Frames(input);
            Complex[] z = SmoothestField(input, fr0, 4, out double lambda, out double lambdaChange);
            double tField = sw.Elapsed.TotalSeconds; sw.Restart();
            Vector3d[] Q = new Vector3d[fr0.N.Length];
            for (int i = 0; i < Q.Length; ++i) { double phi = Math.Atan2(z[i].im, z[i].re) / 4; Q[i] = Math.Cos(phi) * fr0.E[i] + Math.Sin(phi) * fr0.Bt[i]; }
            var sing = Singularities(input, fr0, z, 4, out double sumIndex);
            Vector3d[] O = PositionField(input, fr0, Q, s, sweeps);
            double tPos = sw.Elapsed.TotalSeconds; sw.Restart();
            var poly = Extract(input, fr0, Q, O, s);
            double tExt = sw.Elapsed.TotalSeconds;
            Report(sh.Name, "field+pos (IM-style)", input.VertexCount, sing + "; lambda " + lambda.ToString("G3", CultureInfo.InvariantCulture) + ", last change " + lambdaChange.ToString("G2", CultureInfo.InvariantCulture), sumIndex.ToString("F2", CultureInfo.InvariantCulture), poly, reference, tField, tPos, tExt);

            // baseline: greedy triangle pairing of an isotropic remesh at edge s
            sw.Restart();
            DMesh3 coarse = MakeInput(sh, s, sh.Noise);
            var pairs = TrianglePairing(coarse);
            double tPair = sw.Elapsed.TotalSeconds;
            Report(sh.Name, "tri-pairing (s)", coarse.VertexCount, "-", "-", pairs, reference, 0, 0, tPair);
        }
        return 0;
    }

    // ---------------------------------------------------------------- input
    static DMesh3 MakeInput(Shape sh, double h, double noise)
    {
        var mc = new MarchingCubes { Implicit = new Fn { F = sh.F, B = sh.Box }, Bounds = sh.Box, CubeSize = h * 0.5, ParallelCompute = false, RootMode = MarchingCubes.RootfindingModes.Bisection };
        mc.Generate();
        DMesh3 m = mc.Mesh;
        DMesh3 src = new DMesh3(m);
        var r = new Remesher(m) { SmoothSpeedT = 0.5 };
        r.SetProjectionTarget(MeshProjectionTarget.Auto(src));
        r.SetTargetEdgeLength(h);
        for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
        m = new DMesh3(m, true);
        if (noise > 0) {
            var rnd = new Random(1);
            MeshNormals.QuickCompute(m);
            foreach (int v in m.VertexIndices()) {
                Vector3d n = (Vector3d)m.GetVertexNormal(v);
                m.SetVertex(v, m.GetVertex(v) + n * ((rnd.NextDouble() * 2 - 1) * noise * h));
            }
        }
        return m;
    }

    // ---------------------------------------------------------------- frames, field
    struct Complex { public double re, im; public Complex(double r, double i) { re = r; im = i; } }

    class Frames
    {
        public Vector3d[] P, N, E, Bt; public double[] Area;
        public Frames(DMesh3 m)
        {
            int n = m.MaxVertexID;
            P = new Vector3d[n]; N = new Vector3d[n]; E = new Vector3d[n]; Bt = new Vector3d[n]; Area = new double[n];
            foreach (int v in m.VertexIndices()) P[v] = m.GetVertex(v);
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                Vector3d c = (P[tv.b] - P[tv.a]).Cross(P[tv.c] - P[tv.a]);     // 2 * area * normal
                for (int k = 0; k < 3; ++k) { N[tv[k]] += c; Area[tv[k]] += c.Length / 6; }
            }
            foreach (int v in m.VertexIndices()) {
                N[v] = N[v].Normalized;
                int nb = m.VtxVerticesItr(v).First();
                Vector3d d = P[nb] - P[v]; d -= d.Dot(N[v]) * N[v];
                E[v] = d.Normalized; Bt[v] = N[v].Cross(E[v]);
            }
        }
        public double Angle(int i, Vector3d d) { return Math.Atan2(d.Dot(Bt[i]), d.Dot(E[i])); }
        // transport of an angle in i's frame to j's frame: + rho_ij
        public double Rho(int i, int j) { return Angle(j, P[i] - P[j]) + Math.PI - Angle(i, P[j] - P[i]); }
    }

    static Dictionary<int, double> CotanWeights(DMesh3 m)
    {
        var w = new Dictionary<int, double>();
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            for (int k = 0; k < 3; ++k) {
                int a = tv[k], b = tv[(k + 1) % 3], c = tv[(k + 2) % 3];
                Vector3d u = m.GetVertex(a) - m.GetVertex(c), v = m.GetVertex(b) - m.GetVertex(c);
                double cot = u.Dot(v) / Math.Max(u.Cross(v).Length, 1e-300);
                int e = m.FindEdge(a, b);
                w.TryGetValue(e, out double o); w[e] = o + 0.5 * cot;
            }
        }
        foreach (int e in w.Keys.ToList()) w[e] = Math.Max(w[e], 1e-3);      // spike: clamp negative cotans
        return w;
    }

    static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a <= -Math.PI) a += 2 * Math.PI; return a; }

    // smallest eigenvector of A x = lambda M x, A the n-RoSy connection Laplacian (Hermitian, as 2n x 2n real)
    static Complex[] SmoothestField(DMesh3 m, Frames f, int nsym, out double lambda, out double lambdaChange)
    {
        int n = m.MaxVertexID;
        var w = CotanWeights(m);
        var chol = new SparseCholesky(2 * n);
        double eps = 0;
        foreach (int v in m.VertexIndices()) eps += f.Area[v];
        eps = 1e-6 / (eps / n);     // small mass shift (A is PSD)
        foreach (int e in m.EdgeIndices()) {
            Index2i ev = m.GetEdgeV(e); int i = ev.a, j = ev.b; double we = w[e];
            double r = nsym * f.Rho(i, j), cr = Math.Cos(r), sr = Math.Sin(r);
            chol.Add(i, i, we); chol.Add(j, j, we); chol.Add(n + i, n + i, we); chol.Add(n + j, n + j, we);
            chol.Add(j, i, -we * cr); chol.Add(n + j, n + i, -we * cr);
            chol.Add(j, n + i, we * sr); chol.Add(i, n + j, -we * sr);
        }
        foreach (int v in m.VertexIndices()) { chol.Add(v, v, eps * f.Area[v]); chol.Add(n + v, n + v, eps * f.Area[v]); }
        if (!chol.Factorize()) throw new Exception("factorization failed");
        double[] x = new double[2 * n], b = new double[2 * n];
        var rnd = new Random(7);
        for (int k = 0; k < 2 * n; ++k) x[k] = rnd.NextDouble() - 0.5;
        // ||(A + eps M)^-1 M x||_M -> 1 / (lambda + eps) for the M-normalized iterate: its inverse estimates the eigenvalue
        lambda = double.NaN; lambdaChange = double.NaN;
        for (int it = 0; it < 30; ++it) {
            for (int k = 0; k < n; ++k) { b[k] = f.Area[k] * x[k]; b[n + k] = f.Area[k] * x[n + k]; }
            chol.Solve(b, x);
            double nm = 0; for (int k = 0; k < n; ++k) nm += f.Area[k] * (x[k] * x[k] + x[n + k] * x[n + k]);
            nm = 1 / Math.Sqrt(nm); for (int k = 0; k < 2 * n; ++k) x[k] *= nm;
            double l = nm - eps;
            lambdaChange = Math.Abs(l - lambda) / Math.Abs(l); lambda = l;
        }
        var z = new Complex[n];
        for (int k = 0; k < n; ++k) z[k] = new Complex(x[k], x[n + k]);
        return z;
    }

    static string Singularities(DMesh3 m, Frames f, Complex[] z, int nsym, out double sumIndex)
    {
        int count = 0; sumIndex = 0;
        var hist = new SortedDictionary<int, int>();
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            double sd = 0, sr = 0;
            for (int k = 0; k < 3; ++k) {
                int i = tv[k], j = tv[(k + 1) % 3];
                double rho = f.Rho(i, j);
                sr += rho;
                sd += Wrap(Math.Atan2(z[j].im, z[j].re) - Math.Atan2(z[i].im, z[i].re) - nsym * rho);
            }
            double idx = Math.Round((sd + nsym * Wrap(sr)) / (2 * Math.PI));
            if (idx != 0) { count++; sumIndex += idx / nsym; hist.TryGetValue((int)idx, out int c); hist[(int)idx] = c + 1; }
        }
        // the field-dependent part: the index of each singular face (in units of 1 / nsym); the total alone is fixed by the connection
        return count + " (" + string.Join(", ", hist.Select(kv => (kv.Key > 0 ? "+" : "") + kv.Key + "/" + nsym + " x" + kv.Value)) + ")";
    }

    // ---------------------------------------------------------------- position field (Jakob et al. 2015, derived from the paper; QUAD_REMESHING.md section 3)
    // The 4-RoSy at a vertex is {+-q, +-t}, t = n x q. Compatible representative of vertex 1's field for vertex 0:
    // keep q0 and pick the one of the four that maximizes the dot product with it (the paper's rotation that
    // best aligns the two fields).
    static void CompatOrient(Vector3d q0, Vector3d n0, Vector3d q1, Vector3d n1, out Vector3d a, out Vector3d b)
    {
        Vector3d t1 = n1.Cross(q1);
        double dq = q0.Dot(q1), dt = q0.Dot(t1);
        a = q0;
        if (Math.Abs(dq) >= Math.Abs(dt)) b = dq >= 0 ? q1 : -q1;
        else b = dt >= 0 ? t1 : -t1;
    }

    // The point x nearest to the edge midpoint m that lies on both tangent planes n0.(x - p0) = 0 and
    // n1.(x - p1) = 0. With x = m + alpha n0 + beta n1 the constraints give the 2 x 2 Gram system
    //   [1 c; c 1] (alpha, beta) = (r0, r1),  c = n0.n1,  r_k = n_k.(p_k - m),
    // which is singular for parallel planes. Own choice (the paper gives no formula): Tikhonov regularization,
    // the diagonal + EPS, which bounds |alpha|, |beta| by about |r| / EPS. EPS = 0.05: with |r| <= h sin(theta) / 2
    // the shift stays below 10 h even for nearly parallel planes; exactly parallel planes give x = m.
    static double MiddleEps = 0.05;     // eps=<x> on the command line, for the sensitivity run
    static Vector3d MiddlePoint(Vector3d p0, Vector3d n0, Vector3d p1, Vector3d n1)
    {
        Vector3d m = 0.5 * (p0 + p1);
        double c = n0.Dot(n1), r0 = n0.Dot(p0 - m), r1 = n1.Dot(p1 - m);
        double d0 = 1 + MiddleEps, det = d0 * d0 - c * c;
        double alpha = (d0 * r0 - c * r1) / det, beta = (d0 * r1 - c * r0) / det;
        return m + alpha * n0 + beta * n1;
    }

    // Integer lattice coordinates of the point of lattice 0 = {o0 + s (i q0 + j t0)} nearest to the middle point
    // x, and of the point of lattice 1 nearest to that one (two nearest-point roundings; own choice). Returns the
    // two points; the integer offset between the lattice origins is (kq0 - kq1, kt0 - kt1).
    static double CompatPos(Vector3d v0, Vector3d n0, Vector3d q0, Vector3d o0, Vector3d v1, Vector3d n1, Vector3d q1, Vector3d o1, double s,
        out Vector3d a, out Vector3d b, out int kq0, out int kt0, out int kq1, out int kt1)
    {
        Vector3d t0 = n0.Cross(q0), t1 = n1.Cross(q1);
        Vector3d x = MiddlePoint(v0, n0, v1, n1);
        kq0 = (int)Math.Round(q0.Dot(x - o0) / s); kt0 = (int)Math.Round(t0.Dot(x - o0) / s);
        a = o0 + s * (kq0 * q0 + kt0 * t0);
        kq1 = (int)Math.Round(q1.Dot(a - o1) / s); kt1 = (int)Math.Round(t1.Dot(a - o1) / s);
        b = o1 + s * (kq1 * q1 + kt1 * t1);
        return (a - b).Length;
    }

    // the representative of the lattice through o nearest to the vertex v (the paper keeps o near its vertex)
    static Vector3d RoundPos(Vector3d o, Vector3d q, Vector3d n, Vector3d v, double s)
    {
        Vector3d t = n.Cross(q), d = v - o;
        return o + s * (Math.Round(q.Dot(d) / s) * q + Math.Round(t.Dot(d) / s) * t);
    }

    class Level { public Vector3d[] V, N, Q; public double[] A; public List<int>[] Adj; public int[] ToCoarse; }

    static Level Coarsen(Level f)
    {
        int n = f.V.Length;
        var edges = new List<(double, int, int)>();
        for (int i = 0; i < n; ++i)
            foreach (int j in f.Adj[i])
                if (i < j) edges.Add(((f.V[i] - f.V[j]).LengthSquared, i, j));      // own choice: merge the closest pairs first
        edges.Sort();
        int[] map = Enumerable.Repeat(-1, n).ToArray();
        var members = new List<(int, int)>();
        foreach (var (_, i, j) in edges)
            if (map[i] < 0 && map[j] < 0) { map[i] = map[j] = members.Count; members.Add((i, j)); }
        for (int i = 0; i < n; ++i) if (map[i] < 0) { map[i] = members.Count; members.Add((i, -1)); }
        int nc = members.Count;
        var c = new Level { V = new Vector3d[nc], N = new Vector3d[nc], Q = new Vector3d[nc], A = new double[nc], Adj = new List<int>[nc] };
        for (int k = 0; k < nc; ++k) {
            var (i, j) = members[k];
            if (j < 0) { c.V[k] = f.V[i]; c.N[k] = f.N[i]; c.Q[k] = f.Q[i]; c.A[k] = f.A[i]; }
            else {
                double a = f.A[i] + f.A[j];
                c.A[k] = a; c.V[k] = (f.A[i] * f.V[i] + f.A[j] * f.V[j]) / a;
                c.N[k] = (f.A[i] * f.N[i] + f.A[j] * f.N[j]).Normalized;
                CompatOrient(f.Q[i], f.N[i], f.Q[j], f.N[j], out Vector3d qa, out Vector3d qb);
                Vector3d q = qa * f.A[i] + qb * f.A[j]; q -= c.N[k].Dot(q) * c.N[k];
                c.Q[k] = q.Normalized;
            }
            c.Adj[k] = new List<int>();
        }
        for (int i = 0; i < n; ++i)
            foreach (int j in f.Adj[i]) { int a = map[i], b = map[j]; if (a != b && !c.Adj[a].Contains(b)) c.Adj[a].Add(b); }
        f.ToCoarse = map;
        return c;
    }

    // Gauss-Seidel on E_i = sum_j |o_i + delta - (its lattice match of o_j)|^2: shifting o_i by delta moves its
    // lattice points by delta, so the minimizer is the mean misfit delta = mean_j (b_j - a_j) (uniform weights,
    // own choice); then back into the tangent plane and to the representative nearest the vertex.
    static void Smooth(Level L, Vector3d[] O, double s, int sweeps)
    {
        int n = L.V.Length;
        for (int it = 0; it < sweeps; ++it)
            for (int i = 0; i < n; ++i) {
                if (L.Adj[i].Count == 0) continue;
                Vector3d delta = Vector3d.Zero;
                foreach (int j in L.Adj[i]) {
                    CompatOrient(L.Q[i], L.N[i], L.Q[j], L.N[j], out Vector3d qi, out Vector3d qj);
                    CompatPos(L.V[i], L.N[i], qi, O[i], L.V[j], L.N[j], qj, O[j], s, out Vector3d a, out Vector3d b, out _, out _, out _, out _);
                    delta += b - a;
                }
                Vector3d o = O[i] + delta / L.Adj[i].Count;
                o -= L.N[i].Dot(o - L.V[i]) * L.N[i];
                O[i] = RoundPos(o, L.Q[i], L.N[i], L.V[i], s);
            }
    }

    static Vector3d[] PositionField(DMesh3 m, Frames f, Vector3d[] Q, double s, int sweeps)
    {
        int n = m.MaxVertexID;
        var L0 = new Level { V = f.P, N = f.N, Q = Q, A = f.Area, Adj = new List<int>[n] };
        for (int i = 0; i < n; ++i) L0.Adj[i] = m.VtxVerticesItr(i).ToList();
        var levels = new List<Level> { L0 };
        while (levels[levels.Count - 1].V.Length > 50 && levels.Count < 20) levels.Add(Coarsen(levels[levels.Count - 1]));
        Level top = levels[levels.Count - 1];
        Vector3d[] O = (Vector3d[])top.V.Clone();
        Smooth(top, O, s, sweeps);
        for (int l = levels.Count - 2; l >= 0; --l) {
            Level L = levels[l];
            var Of = new Vector3d[L.V.Length];
            for (int i = 0; i < Of.Length; ++i) {
                Vector3d o = O[L.ToCoarse[i]];
                o -= L.N[i].Dot(o - L.V[i]) * L.N[i];
                Of[i] = RoundPos(o, L.Q[i], L.N[i], L.V[i], s);
            }
            O = Of;
            Smooth(L, O, s, sweeps);
        }
        return O;
    }

    // ---------------------------------------------------------------- extraction
    class Poly { public Vector3d[] V; public Vector3d[] N; public List<int[]> Faces = new List<int[]>(); }

    static int Find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }

    static Poly Extract(DMesh3 m, Frames f, Vector3d[] Q, Vector3d[] O, double s)
    {
        int n = m.MaxVertexID;
        int[] uf = Enumerable.Range(0, n).ToArray();
        var unit = new List<(int, int)>();
        foreach (int e in m.EdgeIndices()) {
            Index2i ev = m.GetEdgeV(e); int i = ev.a, j = ev.b;
            CompatOrient(Q[i], f.N[i], Q[j], f.N[j], out Vector3d qi, out Vector3d qj);
            CompatPos(f.P[i], f.N[i], qi, O[i], f.P[j], f.N[j], qj, O[j], s, out _, out _, out int a0, out int a1, out int b0, out int b1);
            int dq = a0 - b0, dt = a1 - b1;
            if (dq == 0 && dt == 0) { int ri = Find(uf, i), rj = Find(uf, j); if (ri != rj) uf[Math.Max(ri, rj)] = Math.Min(ri, rj); }
            else if (Math.Abs(dq) + Math.Abs(dt) == 1) unit.Add((i, j));
        }
        var id = new Dictionary<int, int>(); var pos = new List<Vector3d>(); var nrm = new List<Vector3d>(); var cnt = new List<int>();
        for (int i = 0; i < n; ++i) {
            int r = Find(uf, i);
            if (!id.TryGetValue(r, out int k)) { k = pos.Count; id[r] = k; pos.Add(Vector3d.Zero); nrm.Add(Vector3d.Zero); cnt.Add(0); }
            pos[k] += O[i]; nrm[k] += f.N[i]; cnt[k]++;
        }
        int nv = pos.Count;
        var adj = new HashSet<int>[nv];
        for (int k = 0; k < nv; ++k) { pos[k] /= cnt[k]; nrm[k] = nrm[k].Normalized; adj[k] = new HashSet<int>(); }
        foreach (var (i, j) in unit) { int a = id[Find(uf, i)], b = id[Find(uf, j)]; if (a != b) { adj[a].Add(b); adj[b].Add(a); } }
        // drop dangling vertices
        bool changed = true;
        while (changed) {
            changed = false;
            for (int k = 0; k < nv; ++k)
                if (adj[k].Count == 1) { int o = adj[k].First(); adj[o].Remove(k); adj[k].Clear(); changed = true; }
        }
        var poly = new Poly { V = pos.ToArray(), N = nrm.ToArray() };
        TraceFaces(poly, adj);
        return poly;
    }

    static void TraceFaces(Poly poly, HashSet<int>[] adj)
    {
        int nv = poly.V.Length;
        var order = new List<int>[nv];
        for (int k = 0; k < nv; ++k) {
            Vector3d nk = poly.N[k], e = Vector3d.Zero;
            if (adj[k].Count == 0) { order[k] = new List<int>(); continue; }
            Vector3d d0 = poly.V[adj[k].First()] - poly.V[k]; e = (d0 - d0.Dot(nk) * nk).Normalized; Vector3d b = nk.Cross(e);
            order[k] = adj[k].OrderBy(j => { Vector3d d = poly.V[j] - poly.V[k]; return Math.Atan2(d.Dot(b), d.Dot(e)); }).ThenBy(j => j).ToList();
        }
        var used = new HashSet<(int, int)>();
        for (int u = 0; u < nv; ++u)
            foreach (int v0 in order[u]) {
                if (used.Contains((u, v0))) continue;
                var face = new List<int>(); int a = u, b = v0;
                while (!used.Contains((a, b)) && face.Count < 1000) {
                    used.Add((a, b)); face.Add(a);
                    var ob = order[b]; int k = ob.IndexOf(a);
                    int c = ob[(k - 1 + ob.Count) % ob.Count];
                    a = b; b = c;
                }
                poly.Faces.Add(face.ToArray());
            }
    }

    // ---------------------------------------------------------------- baseline
    static double QuadAngleDev(Vector3d[] p)
    {
        double worst = 0;
        for (int k = 0; k < 4; ++k) {
            Vector3d a = p[(k + 3) % 4] - p[k], b = p[(k + 1) % 4] - p[k];
            double ang = Vector3d.AngleD(a.Normalized, b.Normalized);
            worst = Math.Max(worst, Math.Abs(ang - 90));
        }
        return worst;
    }

    static Poly TrianglePairing(DMesh3 m)
    {
        var cand = new List<(double, int)>();
        foreach (int e in m.EdgeIndices()) {
            Index4i q = QuadOf(m, e); if (q.a < 0) continue;
            double dev = QuadAngleDev(new[] { m.GetVertex(q.a), m.GetVertex(q.b), m.GetVertex(q.c), m.GetVertex(q.d) });
            if (dev < 60) cand.Add((dev, e));
        }
        cand.Sort();
        var usedT = new HashSet<int>();
        var poly = new Poly { V = new Vector3d[m.MaxVertexID], N = new Vector3d[m.MaxVertexID] };
        foreach (int v in m.VertexIndices()) poly.V[v] = m.GetVertex(v);
        foreach (var (_, e) in cand) {
            Index2i et = m.GetEdgeT(e);
            if (usedT.Contains(et.a) || usedT.Contains(et.b)) continue;
            usedT.Add(et.a); usedT.Add(et.b);
            Index4i q = QuadOf(m, e);
            poly.Faces.Add(new[] { q.a, q.b, q.c, q.d });
        }
        foreach (int t in m.TriangleIndices()) if (!usedT.Contains(t)) { Index3i tv = m.GetTriangle(t); poly.Faces.Add(new[] { tv.a, tv.b, tv.c }); }
        return poly;
    }

    // the quad of the two triangles at an interior edge, in the orientation of the first triangle
    static Index4i QuadOf(DMesh3 m, int e)
    {
        Index2i et = m.GetEdgeT(e); if (et.b < 0) return new Index4i(-1, -1, -1, -1);
        Index3i t0 = m.GetTriangle(et.a); Index2i ev = m.GetEdgeV(e);
        int k = 0; while (t0[k] == ev.a || t0[k] == ev.b) k++;      // the corner opposite the edge
        int c = t0[k], a = t0[(k + 1) % 3], b = t0[(k + 2) % 3];       // edge a-b in t0's orientation
        Index2i op = m.GetEdgeOpposingV(e); int d = op.a == c ? op.b : op.a;
        return new Index4i(c, a, d, b);
    }

    // ---------------------------------------------------------------- metrics
    static void Report(string shape, string method, int inputV, string sing, string sumIdx, Poly p, DMesh3 reference, double tField, double tPos, double tExt)
    {
        int quads = 0, tris = 0, five6 = 0, big = 0;
        var dev = new List<double>(); var asp = new List<double>();
        var valence = new Dictionary<int, int>();
        foreach (var f in p.Faces) {
            if (f.Length == 4) quads++; else if (f.Length == 3) tris++; else if (f.Length >= 5 && f.Length <= 6) five6++; else big++;
            if (f.Length >= 3 && f.Length <= 6) foreach (int v in f) { valence.TryGetValue(v, out int c); valence[v] = c + 1; }
            if (f.Length == 4) {
                double mn = double.MaxValue, mx = 0;
                for (int k = 0; k < 4; ++k) {
                    Vector3d a = p.V[f[(k + 3) % 4]] - p.V[f[k]], b = p.V[f[(k + 1) % 4]] - p.V[f[k]];
                    dev.Add(Math.Abs(Vector3d.AngleD(a.Normalized, b.Normalized) - 90));
                    double l = b.Length; mn = Math.Min(mn, l); mx = Math.Max(mx, l);
                }
                asp.Add(mx / Math.Max(mn, 1e-300));
            }
        }
        int faces = p.Faces.Count;
        int irregular = valence.Values.Count(c => c != 4);
        int nonquad = faces - quads;
        dev.Sort(); asp.Sort();
        Func<List<double>, double, double> pct = (l, q) => l.Count == 0 ? double.NaN : l[Math.Min(l.Count - 1, (int)(q * l.Count))];
        double in70 = dev.Count == 0 ? double.NaN : 100.0 * dev.Count(d => d <= 20) / dev.Count;

        // Hausdorff of the fan-triangulated polygons (faces <= 6 sides) to the finer reference remesh
        var tm = new DMesh3();
        foreach (var v in p.V) tm.AppendVertex(v);
        int skipped = 0;
        foreach (var f in p.Faces) {
            if (f.Length < 3 || f.Length > 6) continue;
            for (int k = 1; k + 1 < f.Length; ++k) if (tm.AppendTriangle(f[0], f[k], f[k + 1]) < 0) skipped++;
        }
        // the symmetric distance includes the input's distance to the holes (defect faces are not triangulated);
        // the one-sided distance from the output to the reference measures the fidelity of the faces that exist.
        // Irregular vertices: valence from the faces with 3-6 sides, so the vertices on a defect's border count as irregular
        double haus = double.NaN, hausOut = double.NaN;
        if (tm.TriangleCount > 0) {
            var hd = new MeshHausdorffDistance(tm, reference) { Symmetric = true, RelativeTolerance = 1e-3 };
            hd.Compute();
            haus = hd.UpperBound / reference.CachedBounds.DiagonalLength;
            var ho = new MeshHausdorffDistance(tm, reference) { Symmetric = false, RelativeTolerance = 1e-3 };
            ho.Compute();
            hausOut = ho.UpperBound / reference.CachedBounds.DiagonalLength;
        }
        string F(double x, string fmt) { return x.ToString(fmt, CultureInfo.InvariantCulture); }
        Console.WriteLine($"| {shape} | {method} | {inputV} | {sing} | {sumIdx} | {faces} | {F(100.0 * quads / Math.Max(faces, 1), "F1")} | {tris} | {five6} | {big} | {skipped} | {irregular} | {irregular + nonquad} | {F(pct(dev, 0.5), "F1")} / {F(pct(dev, 0.95), "F1")} | {F(in70, "F1")} | {F(pct(asp, 0.5), "F2")} / {F(pct(asp, 0.95), "F2")} | {F(haus, "F4")} | {F(hausOut, "F4")} | {F(tField, "F2")} | {F(tPos, "F2")} | {F(tExt, "F2")} |");
    }
}
