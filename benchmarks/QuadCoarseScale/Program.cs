// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Prototype (measurement only, no library change): the scalar log spacing sigma of IntegrableFrames (Scale) solved on a coarse level of the
// position field's hierarchy and prolonged to the fine vertices. The cross field, the position field and the extraction stay on the input mesh.
// docs/design/QUAD_REMESHING.md 4.5 has the full solve this is compared with.
//
// Method (own, written for this study): the fine curl energy 1/2 sum_t |w_t|^2 of IntegrableFrames is minimized over a coarse unknown c per
// cluster of level k, sigma_i = c[map_i] (the hierarchy's own prolongation: the composition of the greedy matchings, piecewise constant),
// i.e. the Galerkin reduction J P of the fine Jacobian, so no coarse triangles are needed. Damped Gauss-Newton with a sparse Cholesky of the
// k-level normal matrix (1 unknown per cluster), the mean of sigma 0, the same clamp and stop rules as the library. Optionally a few fine
// Gauss-Seidel sweeps (one scalar Gauss-Newton step per vertex) after the prolongation. Level 0 is the full scalar solve on the fine mesh.
//
//   dotnet run -c Release --project benchmarks/QuadCoarseScale -- [only=<regex>] [big=<n>] [gs=<sweeps>] [minN=<coarse vertices>] [det=1]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
    class Shape { public string Name; public Func<Vector3d, double> F; public double S; public double Noise; public Func<DMesh3, DMesh3> Post; }

    static string F(double x, string fmt) { return x.ToString(fmt, CultureInfo.InvariantCulture); }

    // ------------------------------------------------------------------------------------------------ the fine problem (as IntegrableFrames builds it)

    class Prob
    {
        public int N, M, NUsed;
        public int[] ToVertex, ToC;
        public int[][] Tri; public Vector2d[][] A; public bool[] Used;
        public int[] VS, VT, VK;       // CSR vertex -> (triangle, slot)
    }

    static Prob Build(DMesh3 mesh, CrossField field, double s)
    {
        var P = new Prob();
        int maxId = mesh.MaxVertexID;
        var toVertex = new List<int>(); var toC = new int[maxId];
        for (int v = 0; v < maxId; ++v) { toC[v] = -1; if (mesh.IsVertex(v) && field.HasField(v)) { toC[v] = toVertex.Count; toVertex.Add(v); } }
        int n = toVertex.Count;
        var V = new Vector3d[n]; var N = new Vector3d[n]; var Q = new Vector3d[n];
        for (int i = 0; i < n; ++i) { int v = toVertex[i]; V[i] = mesh.GetVertex(v); N[i] = field.VertexNormal(v); Q[i] = field.VertexDirection(v, 0); }
        var singular = new HashSet<int>(field.SingularFaces);
        var tri = new List<int[]>(); var edgeVec = new List<Vector2d[]>();
        foreach (int t in mesh.TriangleIndices()) {
            if (singular.Contains(t)) continue;
            Index3i tv = mesh.GetTriangle(t);
            int[] c = { toC[tv.a], toC[tv.b], toC[tv.c] };
            if (c[0] < 0 || c[1] < 0 || c[2] < 0) continue;
            Vector3d qa = Q[c[0]], na = N[c[0]];
            var a = new Vector2d[3]; bool ok = true;
            for (int e = 0; e < 3 && ok; ++e) {
                int i = c[e], j = c[(e + 1) % 3];
                Vector3d q0, q1, u0, u1;
                CompatOrient(qa, na, Q[i], N[i], out q0, out q1);
                CompatOrient(qa, na, Q[j], N[j], out u0, out u1);
                Vector3d qe = q1 + u1; double ql = qe.Length;
                if (!(ql > 1e-9)) { ok = false; break; }
                qe /= ql;
                Vector3d ev = (V[j] - V[i]) / s;
                a[e] = new Vector2d(ev.Dot(qe), ev.Dot(na.Cross(qe)));
                if (double.IsNaN(a[e].x) || double.IsNaN(a[e].y) || double.IsInfinity(a[e].x) || double.IsInfinity(a[e].y)) ok = false;
            }
            if (!ok) continue;
            tri.Add(c); edgeVec.Add(a);
        }
        P.N = n; P.M = tri.Count; P.ToVertex = toVertex.ToArray(); P.ToC = toC; P.Tri = tri.ToArray(); P.A = edgeVec.ToArray();
        P.Used = new bool[n];
        foreach (int[] c in P.Tri) foreach (int i in c) if (!P.Used[i]) { P.Used[i] = true; P.NUsed++; }
        var cnt = new int[n + 1];
        foreach (int[] c in P.Tri) foreach (int i in c) cnt[i + 1]++;
        for (int i = 0; i < n; ++i) cnt[i + 1] += cnt[i];
        P.VS = cnt; P.VT = new int[cnt[n]]; P.VK = new int[cnt[n]];
        var fill = (int[])cnt.Clone();
        for (int t = 0; t < P.M; ++t) for (int k = 0; k < 3; ++k) { int i = P.Tri[t][k]; P.VT[fill[i]] = t; P.VK[fill[i]] = k; fill[i]++; }
        return P;
    }

    static void CompatOrient(Vector3d q0, Vector3d n0, Vector3d q1, Vector3d n1, out Vector3d a, out Vector3d b)
    {
        Vector3d t1 = n1.Cross(q1);
        double dq = q0.Dot(q1), dt = q0.Dot(t1);
        a = q0;
        if (Math.Abs(dq) >= Math.Abs(dt)) b = dq >= 0 ? q1 : -q1;
        else b = dt >= 0 ? t1 : -t1;
    }

    static Vector2d Term(Vector2d a, double[] sig, int i, int j) { double g = Math.Exp(-0.5 * (sig[i] + sig[j])); return new Vector2d(g * a.x, g * a.y); }

    static double Cost(Prob P, double[] sig, out double curl)
    {
        double cost = 0; curl = 0;
        for (int t = 0; t < P.M; ++t) {
            int[] c = P.Tri[t]; Vector2d ws = Vector2d.Zero;
            for (int e = 0; e < 3; ++e) ws += Term(P.A[t][e], sig, c[e], c[(e + 1) % 3]);
            cost += 0.5 * ws.LengthSquared; curl += ws.Length;
        }
        return cost;
    }

    // residual w and the Jacobian column per slot (dw/dsigma_slot, 2 components) of triangle t
    static Vector2d Lin(Prob P, double[] sig, int t, Vector2d[] b)
    {
        int[] c = P.Tri[t]; var ce = new Vector2d[3]; Vector2d ws = Vector2d.Zero;
        for (int e = 0; e < 3; ++e) { ce[e] = Term(P.A[t][e], sig, c[e], c[(e + 1) % 3]); ws += ce[e]; }
        for (int k = 0; k < 3; ++k) { Vector2d e1 = ce[k], e2 = ce[(k + 2) % 3]; b[k] = new Vector2d(-0.5 * (e1.x + e2.x), -0.5 * (e1.y + e2.y)); }
        return ws;
    }

    // ------------------------------------------------------------------------------------------------ the solve

    class SolveStats { public int Steps, Unknowns; public double Seconds, CurlBefore, CurlAfter, SecondsPrep, SecondsGs; public int GsSweeps; }

    // prolongation as CSR rows (fine compact vertex -> (cluster, weight), weights sum to 1): P0 = the hierarchy's injection (piecewise constant),
    // P_m = S^m P0 with S the average over the vertex and its triangle neighbours (smoothed aggregation)
    class Prolong { public int[] S, J; public double[] W; public int Nc; }

    static Prolong MakeP(Prob P, int[] map, int nc, int passes)
    {
        int n = P.N;
        var rows = new List<KeyValuePair<int, double>>[n];
        for (int i = 0; i < n; ++i) rows[i] = new List<KeyValuePair<int, double>> { new KeyValuePair<int, double>(map == null ? i : map[i], 1.0) };
        for (int pass = 0; pass < passes; ++pass) {
            var nxt = new List<KeyValuePair<int, double>>[n];
            for (int i = 0; i < n; ++i) {
                var acc = new Dictionary<int, double>(); var order = new List<int>();
                var nb = new List<int> { i };
                if (P.Used[i]) for (int q = P.VS[i]; q < P.VS[i + 1]; ++q) { int[] tv = P.Tri[P.VT[q]]; nb.Add(tv[(P.VK[q] + 1) % 3]); }
                double wgt = 1.0 / nb.Count;
                foreach (int j in nb) foreach (var kv in rows[j]) { if (!acc.ContainsKey(kv.Key)) { acc[kv.Key] = 0; order.Add(kv.Key); } acc[kv.Key] += wgt * kv.Value; }
                order.Sort();
                nxt[i] = order.Select(k => new KeyValuePair<int, double>(k, acc[k])).ToList();
            }
            rows = nxt;
        }
        var pr = new Prolong { Nc = map == null ? n : nc, S = new int[n + 1] };
        for (int i = 0; i < n; ++i) pr.S[i + 1] = pr.S[i] + rows[i].Count;
        pr.J = new int[pr.S[n]]; pr.W = new double[pr.S[n]];
        for (int i = 0; i < n; ++i) { int at = pr.S[i]; foreach (var kv in rows[i]) { pr.J[at] = kv.Key; pr.W[at] = kv.Value; at++; } }
        return pr;
    }

    static void Apply(Prolong R, int n, double[] c, double[] outF)
    {
        for (int i = 0; i < n; ++i) { double v = 0; for (int q = R.S[i]; q < R.S[i + 1]; ++q) v += R.W[q] * c[R.J[q]]; outF[i] = v; }
    }

    // Galerkin Gauss-Newton on the coarse unknowns c, sigma = R c. Returns the fine sigma (compact, mean 0 over used vertices).
    static double[] Solve(Prob P, Prolong R, int gsSweeps, SolveStats st)
    {
        var sw = Stopwatch.StartNew();
        int n = P.N, nc = R.Nc;
        double maxSig = Math.Log(2.5), damping = 0.01; const int refactorEvery = 8; const int iterations = 40; const double stopTol = 0.001;
        var clUsed = new bool[nc];
        for (int i = 0; i < n; ++i) if (P.Used[i]) for (int q = R.S[i]; q < R.S[i + 1]; ++q) clUsed[R.J[q]] = true;
        var c = new double[nc]; var sigF = new double[n];
        double curl0; double cost = Cost(P, sigF, out curl0); st.CurlBefore = st.CurlAfter = curl0; st.Unknowns = nc;
        SparseCholesky chol = null; bool refactor = true;
        var w = new Vector2d[P.M]; var bs = new Vector2d[P.M][];
        for (int t = 0; t < P.M; ++t) bs[t] = new Vector2d[3];
        var cl = new int[64]; var jv = new Vector2d[64];
        st.SecondsPrep = sw.Elapsed.TotalSeconds;
        for (int step = 0; step < iterations; ++step) {
            var gF = new Vector2d[0]; var gFine = new double[n];
            for (int t = 0; t < P.M; ++t) {
                w[t] = Lin(P, sigF, t, bs[t]);
                for (int k = 0; k < 3; ++k) gFine[P.Tri[t][k]] -= bs[t][k].Dot(w[t]);
            }
            var rhs = new double[nc];
            for (int i = 0; i < n; ++i) if (gFine[i] != 0) for (int q = R.S[i]; q < R.S[i + 1]; ++q) rhs[R.J[q]] += R.W[q] * gFine[i];
            bool fresh = false;
            if (chol == null || refactor) {
                fresh = true;
                if (chol == null) chol = new SparseCholesky(nc, 1); else chol.Clear();
                for (int t = 0; t < P.M; ++t) {
                    int cnt = 0;
                    for (int k = 0; k < 3; ++k) {
                        int i = P.Tri[t][k];
                        for (int q = R.S[i]; q < R.S[i + 1]; ++q) {
                            int m = R.J[q]; int at = -1;
                            for (int z = 0; z < cnt; ++z) if (cl[z] == m) { at = z; break; }
                            Vector2d add = R.W[q] * bs[t][k];
                            if (at < 0) { cl[cnt] = m; jv[cnt] = add; cnt++; } else jv[at] += add;
                        }
                    }
                    for (int p = 0; p < cnt; ++p) for (int q = p; q < cnt; ++q) chol.Add(cl[p], cl[q], jv[p].Dot(jv[q]));
                }
                for (int i = 0; i < nc; ++i) chol.Add(i, i, damping);
                if (!chol.Factorize()) break;
                refactor = false;
            }
            var d = new double[nc];
            chol.Solve(rhs, d);
            var dF = new double[n]; Apply(R, n, d, dF);
            double dm = 0; for (int i = 0; i < n; ++i) if (P.Used[i]) dm += dF[i]; dm /= P.NUsed;
            for (int i = 0; i < nc; ++i) d[i] = clUsed[i] ? d[i] - dm : 0;
            double alpha = 1, newCost = cost, newCurl = st.CurlAfter; bool accepted = false;
            var c2 = new double[nc]; var s2 = new double[n];
            for (int tries = 0; tries < 8; ++tries) {
                for (int i = 0; i < nc; ++i) c2[i] = Math.Max(-maxSig, Math.Min(maxSig, c[i] + alpha * d[i]));
                Apply(R, n, c2, s2);
                newCost = Cost(P, s2, out newCurl);
                if (newCost < cost) { accepted = true; break; }
                alpha *= 0.5;
            }
            if (!accepted) { if (fresh) break; refactor = true; continue; }
            double gain = (cost - newCost) / cost;
            Array.Copy(c2, c, nc); Array.Copy(s2, sigF, n);
            if (alpha != 1 || (step + 1) % refactorEvery == 0) refactor = true;
            cost = newCost; st.CurlAfter = newCurl; st.Steps++;
            if (gain < stopTol) break;
        }
        // optional fine Gauss-Seidel: one scalar damped Gauss-Newton step per vertex, in vertex order (serial, deterministic);
        // a sweep that does not lower the cost is undone and ends the smoothing
        var sw2 = Stopwatch.StartNew();
        if (gsSweeps > 0) {
            var b = new Vector2d[3];
            for (int sweep = 0; sweep < gsSweeps; ++sweep) {
                var backup = (double[])sigF.Clone(); double before = cost;
                for (int i = 0; i < n; ++i) {
                    if (!P.Used[i]) continue;
                    double g = 0, h = damping;
                    for (int q = P.VS[i]; q < P.VS[i + 1]; ++q) {
                        Vector2d wt = Lin(P, sigF, P.VT[q], b); Vector2d bi = b[P.VK[q]];
                        g += bi.Dot(wt); h += bi.LengthSquared;
                    }
                    sigF[i] = Math.Max(-maxSig, Math.Min(maxSig, sigF[i] - g / h));
                }
                double cv; double after = Cost(P, sigF, out cv);
                if (!(after < before)) { Array.Copy(backup, sigF, n); break; }
                cost = after; st.CurlAfter = cv; st.GsSweeps++;
            }
        }
        st.SecondsGs = sw2.Elapsed.TotalSeconds;
        double mean = 0; for (int i = 0; i < n; ++i) if (P.Used[i]) mean += sigF[i]; mean /= P.NUsed;
        for (int i = 0; i < n; ++i) if (P.Used[i]) sigF[i] -= mean;
        Cost(P, sigF, out st.CurlAfter);
        st.Seconds = sw.Elapsed.TotalSeconds;
        return sigF;
    }

    // ------------------------------------------------------------------------------------------------ pipeline

    static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static List<int[]> HierarchyMaps(DMesh3 mesh, CrossField cf, double s, out double seconds, out int[] sizes)
    {
        var sw = Stopwatch.StartNew();
        var pf = new PositionField(mesh, cf) { TargetEdgeLength = s, Sweeps = 0 };
        pf.Compute();
        seconds = sw.Elapsed.TotalSeconds;
        var list = (System.Collections.IList)typeof(PositionField).GetField("LevelList", NP).GetValue(pf);
        int nl = list.Count;
        sizes = new int[nl];
        var maps = new List<int[]>();            // maps[l]: fine compact -> level l
        int[] cur = null;
        for (int l = 0; l < nl; ++l) {
            object lev = list[l];
            sizes[l] = (int)lev.GetType().GetField("N").GetValue(lev);
            if (l == 0) { cur = null; maps.Add(null); continue; }
            var prev = list[l - 1]; var tc = (int[])prev.GetType().GetField("ToCoarse").GetValue(prev);
            int[] nxt = new int[sizes[0]];
            for (int i = 0; i < nxt.Length; ++i) nxt[i] = cur == null ? tc[i] : tc[cur[i]];
            cur = nxt; maps.Add(cur);
        }
        return maps;
    }

    class Outcome
    {
        public bool Applied; public int Irregular; public double QuadPct, CornerP95, Haus = double.NaN, MinSp = 1, MaxSp = 1;
        public double TPos, TExt; public long Hash; public string Err;
    }

    static Outcome Finish(DMesh3 input, CrossField cf, double s, double[] spacing, double mm, bool hausdorff, int[] toVertex = null)
    {
        var o = new Outcome();
        var qr = new QuadRemesher(input) { TargetEdgeLength = s, Cleanup = true, MismatchLimit = mm };
        var pf = new PositionField(input, cf) { TargetEdgeLength = s, Sweeps = 20, Parallel = true, MiddleRegularization = 0.05, SpacingFactors = spacing };
        var sw = Stopwatch.StartNew();
        if (pf.Compute() != PositionFieldStatus.Success) { o.Err = "position " + pf.Error; return o; }
        o.TPos = sw.Elapsed.TotalSeconds; sw.Restart();
        var extract = typeof(QuadRemesher).GetMethod("Extract", NP);
        var res = (DPolyMesh)extract.Invoke(qr, new object[] { pf });
        o.TExt = sw.Elapsed.TotalSeconds;
        if (res == null || res.FaceCount == 0) { o.Err = "no faces"; return o; }
        PolyMeshMetrics m = PolyMeshMetrics.Compute(res);
        o.Irregular = m.IrregularVertices; o.QuadPct = 100.0 * m.QuadFraction; o.CornerP95 = PolyMeshMetrics.Percentile(m.CornerDeviations, 0.95);
        if (spacing != null && toVertex != null) { o.MinSp = double.MaxValue; o.MaxSp = double.MinValue; foreach (int v in toVertex) { o.MinSp = Math.Min(o.MinSp, spacing[v]); o.MaxSp = Math.Max(o.MaxSp, spacing[v]); } }
        long h = 1469598103934665603L;
        for (int v = 0; v < res.VertexCount; ++v) { Vector3d p = res.GetVertex(v); h = (h ^ BitConverter.DoubleToInt64Bits(p.x)) * 1099511628211L; h = (h ^ BitConverter.DoubleToInt64Bits(p.y)) * 1099511628211L; h = (h ^ BitConverter.DoubleToInt64Bits(p.z)) * 1099511628211L; }
        for (int f = 0; f < res.FaceCount; ++f) foreach (int v in res.GetFace(f)) h = (h ^ v) * 1099511628211L;
        o.Hash = h;
        if (hausdorff) {
            int[] map; var skipped = new List<int>();
            DMesh3 tri = res.ToDMesh3(out map, skipped);
            if (tri.TriangleCount > 0) {
                var hd = new MeshHausdorffDistance(tri, input) { Symmetric = false, RelativeTolerance = 1e-3 };
                hd.Compute(); o.Haus = hd.UpperBound / s;
            }
        }
        return o;
    }

    static double[] ToVertexArray(Prob P, double[] sigF, int maxId)
    {
        var sp = new double[maxId]; for (int v = 0; v < maxId; ++v) sp[v] = 1;
        for (int i = 0; i < P.N; ++i) sp[P.ToVertex[i]] = Math.Exp(sigF[i]);
        return sp;
    }

    static long HashArr(double[] a) { long h = 1469598103934665603L; foreach (double x in a) h = (h ^ BitConverter.DoubleToInt64Bits(x)) * 1099511628211L; return h; }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        double ratio = 0.5, mm = 0.5, minGain = A.ContainsKey("gate") ? double.Parse(A["gate"], CultureInfo.InvariantCulture) : 0.2;
        int gs = A.ContainsKey("gs") ? int.Parse(A["gs"]) : 5;
        int minN = A.ContainsKey("minN") ? int.Parse(A["minN"]) : 300;
        bool det = A.ContainsKey("det") && A["det"] == "1";
        bool quick = A.ContainsKey("quick") && A["quick"] == "1";
        bool fine = !(A.ContainsKey("fine") && A["fine"] == "0");

        Func<Vector3d, double> sphere = p => p.Length - 1;
        Func<Vector3d, double> torus = p => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 1; return Math.Sqrt(q * q + p.z * p.z) - 0.35; };
        Func<Vector3d, double> bumpy = p => p.Length - 1 - 0.08 * Math.Sin(4 * p.x) * Math.Sin(4 * p.y) * Math.Sin(4 * p.z);
        Vector3d ext = new Vector3d(1, 0.7, 0.5); double fr = 0.15;
        Func<Vector3d, double> rbox = p => {
            Vector3d q = new Vector3d(Math.Abs(p.x) - (ext.x - fr), Math.Abs(p.y) - (ext.y - fr), Math.Abs(p.z) - (ext.z - fr));
            Vector3d qp = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return qp.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0) - fr;
        };
        var shapes = new List<Shape> {
            new Shape { Name = "sphere", F = sphere, S = 0.1 },
            new Shape { Name = "torus", F = torus, S = 0.1 },
            new Shape { Name = "bumpy", F = bumpy, S = 0.06 },
            new Shape { Name = "filleted-box", F = rbox, S = 0.08 },
            new Shape { Name = "noisy-sphere", F = sphere, S = 0.1, Noise = 0.2 },
        };
        if (A.ContainsKey("big")) {
            double n = double.Parse(A["big"], CultureInfo.InvariantCulture);
            double h = Math.Sqrt(2 * Math.PI / (0.866 * n) * 2);
            shapes.Add(new Shape { Name = "big-sphere", F = sphere, S = 2 * h });
        }
        { var w = MakeInput(shapes[0], 0.2, 0); var wf = new CrossField(w); wf.Compute(); new QuadRemesher(w) { Field = wf, TargetEdgeLength = 0.4 }.Compute(); }

        foreach (var sh in shapes) {
            if (only != null && !only.IsMatch(sh.Name)) continue;
            double s = sh.S;
            DMesh3 input = MakeInput(sh, s * ratio, sh.Noise);
            var cf = new CrossField(input); cf.Compute();
            if (!cf.Success) { Console.WriteLine("| " + sh.Name + " | cross field failed |"); continue; }
            Console.WriteLine();
            Console.WriteLine($"### {sh.Name} (input V {input.VertexCount}, s {F(s, "G3")}, singular faces {cf.SingularFaces.Count})");
            Console.WriteLine();
            Console.WriteLine("| variant | unknowns | applied | curl before -> after | irregular V | quads % | corner p95 | Hausdorff / s | spacing range | solve (s) | position (s) | extract (s) | total (s) | repeat bits |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            // Off through my path, and the library's Scale for the reference
            var off = Finish(input, cf, s, null, mm, true);
            Row(sh.Name, "Off", 0, false, 0, 0, off, 0, "-");
            var lib = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, SizeAdaptation = SizeAdaptationMode.Scale, MismatchLimit = mm };
            lib.Compute();
            PolyMeshMetrics lm = PolyMeshMetrics.Compute(lib.Result);
            int[] lmap; var lsk = new List<int>(); DMesh3 ltri = lib.Result.ToDMesh3(out lmap, lsk);
            var lhd = new MeshHausdorffDistance(ltri, input) { Symmetric = false, RelativeTolerance = 1e-3 }; lhd.Compute();
            var lo = new Outcome { Applied = lib.SizeAdaptationApplied, Irregular = lm.IrregularVertices, QuadPct = 100.0 * lm.QuadFraction, CornerP95 = PolyMeshMetrics.Percentile(lm.CornerDeviations, 0.95), Haus = lhd.UpperBound / s, TPos = lib.PositionSeconds, TExt = lib.ExtractSeconds, MinSp = lib.MinSpacing, MaxSp = lib.MaxSpacing };
            Row(sh.Name, "library Scale (full, 2 unknowns / vertex)", 2 * input.VertexCount, lib.SizeAdaptationApplied, lib.CurlBefore, lib.CurlAfter, lo, lib.SizeSeconds, "-");

            var P = Build(input, cf, s);
            double tH; int[] sizes;
            var maps = HierarchyMaps(input, cf, s, out tH, out sizes);
            Console.WriteLine($"<!-- {sh.Name}: hierarchy build {F(tH, "F2")} s (levels, colouring), level sizes {string.Join(" ", sizes)} -->");
            for (int l = 0; l < sizes.Length; ++l) {
                if (l == 0 && !fine) continue;
                if (l > 0 && sizes[l] < minN) continue;
                foreach (var gj in quick ? new[] { (0, 0), (gs, 0), (gs, 1), (gs, 2) } : new[] { (0, 0), (gs, 0), (0, 1), (gs, 1), (gs, 2) }) {
                    int g = gj.Item1, jac = gj.Item2;
                    if (l == 0 && (g > 0 || jac > 0)) continue;
                    var R = MakeP(P, maps[l], sizes[l], jac);
                    var st = new SolveStats();
                    double[] sig = Solve(P, R, g, st);
                    var st2 = new SolveStats(); double[] sigB = Solve(P, R, g, st2);
                    bool same = HashArr(sig) == HashArr(sigB);
                    bool applied = st.CurlBefore > 0 && (minGain <= 0 ? st.CurlAfter < st.CurlBefore : st.CurlAfter <= (1 - minGain) * st.CurlBefore);
                    double[] sp = applied ? ToVertexArray(P, sig, input.MaxVertexID) : null;
                    var o = Finish(input, cf, s, sp, mm, true, P.ToVertex);
                    o.Applied = applied;
                    string rep = same ? "sigma same" : "SIGMA DIFFERS";
                    if (det) { var o2 = Finish(input, cf, s, sp, mm, false); rep += o2.Hash == o.Hash ? ", mesh same" : ", MESH DIFFERS"; }
                    Row(sh.Name, (l == 0 ? "level 0 (fine, scalar full solve)" : $"level {l} ({sizes[l]} coarse)") + (jac > 0 ? $" smoothed P x{jac}" : "") + (g > 0 ? $" + {st.GsSweeps} GS" : ""), st.Unknowns, applied, st.CurlBefore, st.CurlAfter, o, st.Seconds, rep);
                }
            }
        }
        return 0;
    }

    static void Row(string shape, string variant, int unknowns, bool applied, double curl0, double curl1, Outcome o, double solve, string rep)
    {
        if (o.Err != null) { Console.WriteLine($"| {variant} | {unknowns} | {o.Err} |"); return; }
        Console.WriteLine($"| {variant} | {unknowns} | {(variant == "Off" ? "-" : applied ? "yes" : "no")} | {F(curl0, "F1")} -> {F(curl1, "F1")} | {o.Irregular} | {F(o.QuadPct, "F1")} | {F(o.CornerP95, "F1")} | {F(o.Haus, "F2")} | {F(o.MinSp, "F2")}-{F(o.MaxSp, "F2")} | {F(solve, "F2")} | {F(o.TPos, "F2")} | {F(o.TExt, "F2")} | {F(solve + o.TPos + o.TExt, "F2")} | {rep} |");
    }

    static DMesh3 MakeInput(Shape sh, double h, double noise)
    {
        var B = new AxisAlignedBox3d(new Vector3d(-1.6, -1.6, -1.6), new Vector3d(1.6, 1.6, 1.6));
        var mc = new MarchingCubes { Implicit = new Fn { F = sh.F, B = B }, Bounds = B, CubeSize = h * 0.5, ParallelCompute = false, RootMode = MarchingCubes.RootfindingModes.Bisection };
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
}
