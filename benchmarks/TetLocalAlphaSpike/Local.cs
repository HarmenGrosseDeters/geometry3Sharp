// The local-alpha prototype (spike, runs/tet-local-alpha): MeshTetrahedralization with MeshAlphaWrap.AlphaField (patched, not on master),
// against the uniform alpha / 40, / 80, / 160. Variants of the alpha field:
//   oracle  levels from the uniform wraps' own distances (the upper bound made real: needs all three uniform runs, not a method)
//   post    two passes: the uniform alpha / 40 wrap measures d(x) = distance of the input samples to its boundary (an a posteriori error
//           indicator, as in adaptive refinement), alpha_j = alpha / 40 * T / d_j clamped to [alpha / 160, alpha / 40], graded
//   post2   a third pass on the post result's distances (same rule against the post result's d)
//   lfs     one pass, a priori: alpha_j = g_j / 3 (clamped), g_j = the free distance along the outward normal (a cheap local feature
//           size of the free space, as a ray-based estimate: Amenta and Bern 1999 for lfs; Shapira, Shamir, Cohen-Or 2008 for the ray idea)
// The field is a graded size function, alpha(x) = min_j (alpha_j + L |x - p_j|) over the samples with a fine alpha (Persson 2006,
// gradient-limited mesh size functions), L = 0.25 (G3_L): the field at the middle of a gap of width g must stay below g / 2 for the ball to enter, so the slope cannot be 1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using g3;
using gs;

sealed class AlphaGridField
{
    readonly S.Grid grid; readonly Vector3d[] p; readonly double[] aj; readonly double a0, L, rmax;
    [ThreadStatic] static List<int> buf;
    public AlphaGridField(Vector3d[] pos, double[] alphaJ, double alpha0, double alphaMin, double slope)
    {
        var idx = Enumerable.Range(0, pos.Length).Where(i => alphaJ[i] < alpha0 * 0.999).ToArray();
        p = idx.Select(i => pos[i]).ToArray(); aj = idx.Select(i => alphaJ[i]).ToArray();
        a0 = alpha0; L = slope; rmax = (alpha0 - alphaMin) / slope;
        grid = new S.Grid(p, Math.Max(rmax, 1e-12));
    }
    public int FineSamples => p.Length;
    public long Calls, Ticks;
    public double AtTimed(Vector3d x)
    {
        long t0 = Stopwatch.GetTimestamp();
        double v = At(x);
        System.Threading.Interlocked.Add(ref Ticks, Stopwatch.GetTimestamp() - t0);
        System.Threading.Interlocked.Increment(ref Calls);
        return v;
    }
    public double At(Vector3d x)
    {
        if (p.Length == 0) return a0;
        var b = buf ??= new List<int>();
        grid.InBall(x, rmax, b);
        double best = a0;
        foreach (int j in b) { double v = aj[j] + L * (p[j] - x).Length; if (v < best) best = v; }
        return best;
    }
}

static class Local
{
    public const string Header = "name,variant,status,tets,vertices,time_s,pass1_s,field_calls,field_cpu_s,touches_input,valid,selfint,worst_dihedral,frac_tets_lt10,p95,p50,max,dmin_over_offset,out_max,fine_samples,counts";

    sealed class Run
    {
        public MeshTetrahedralization Tm; public double Secs; public TetMesh3 Result; public DMesh3 Boundary; public double[] D; public string Row;
    }

    static Run Exec(string name, string variant, DMesh3 input, Vector3d[] pos, double diag, double pass1Secs, double ra, Func<Vector3d, double> field, int fineSamples, AlphaGridField fobj = null)
    {
        var sw = Stopwatch.StartNew();
        var tm = new MeshTetrahedralization(input) { RelativeAlpha = ra, RelativeOffset = 600, AlphaField = field };
        tm.Compute();
        double secs = sw.Elapsed.TotalSeconds;
        var r = new Run { Tm = tm, Secs = secs };
        if (tm.Result == null) { r.Row = string.Join(",", name, variant, tm.Status, "", "", S.F(secs + pass1Secs), S.F(pass1Secs), "", "", "", "", "", "", "", "", "", "", "", "", fineSamples, ""); return r; }
        r.Result = tm.Result;
        r.Boundary = tm.Result.ToSurfaceMesh();
        string bad = TetMeshQuality.NotValidReason(tm.Result);
        bool selfInt = MeshRepairMetrics.HasSelfIntersections(r.Boundary);
        var q = TetMeshQuality.Measure(tm.Result);
        // the boundary against the input (double precision test of the AABB trees; the wrap's own guarantee is exact): no triangle of the boundary may meet the input
        bool touches = new DMeshAABBTree3(r.Boundary, true).TestIntersection(new DMeshAABBTree3(input, true));
        r.D = S.Dist(r.Boundary, pos);
        for (int i = 0; i < r.D.Length; ++i) r.D[i] /= diag;
        var inTree = new DMeshAABBTree3(input, true);
        double outMax = 0; foreach (int v in r.Boundary.VertexIndices()) { inTree.FindNearestTriangle(r.Boundary.GetVertex(v), out double d2); outMax = Math.Max(outMax, Math.Sqrt(d2)); }
        double offRel = 1.0 / 600;
        r.Row = string.Join(",", name, variant, tm.Status, tm.Result.Tets.Count, tm.Result.Vertices.Count, S.F(secs + pass1Secs), S.F(pass1Secs), fobj == null ? "0" : fobj.Calls.ToString(), fobj == null ? "0" : S.F(fobj.Ticks / (double)Stopwatch.Frequency), touches ? "yes" : "no",
            bad == null ? "yes" : bad.Replace(",", ";"), selfInt ? "yes" : "no", S.F(q.MinDihedral.Worst), S.F(q.MinDihedralBelow10 / (double)Math.Max(1, tm.Result.Tets.Count)),
            S.F(S.Quantile(r.D, .95)), S.F(S.Quantile(r.D, .5)), S.F(r.D.Max()), S.F(r.D.Min() / offRel), S.F(outMax / diag), fineSamples,
            string.Join(" ", tm.Counts.Where(c => c.Key.StartsWith("steiner") || c.Key.Contains("refused") || c.Key == "work_limit_reached").Select(c => c.Key + "=" + c.Value)));
        return r;
    }

    static double[] FreeDistance(DMesh3 input, DMeshAABBTree3 tree, Vector3d[] pos, Vector3d[] nrm, int[] tid, double diag)
    {
        // orientation vote: the signed volume (outward inputs positive)
        double vol = 0; foreach (int t in input.TriangleIndices()) { Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero; input.GetTriVertices(t, ref a, ref b, ref c); vol += a.Dot(b.Cross(c)) / 6; }
        double sgn = vol >= 0 ? 1 : -1;
        var g = new double[pos.Length];
        System.Threading.Tasks.Parallel.For(0, pos.Length, i => {
            var dir = sgn * nrm[i];
            var ray = new Ray3d(pos[i] + dir * 1e-7 * diag, dir);
            int t = tree.FindNearestHitTriangle(ray);
            g[i] = double.PositiveInfinity;
            if (t < 0 || t == tid[i]) return;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero; input.GetTriVertices(t, ref a, ref b, ref c);
            var it = new IntrRay3Triangle3(ray, new Triangle3d(a, b, c)); if (it.Find()) g[i] = it.RayParameter / diag;
        });
        return g;
    }

    public static List<string> Go(string name, DMesh3 input, int nSamples = 20000, double slope = 0)
    {
        var rows = new List<string>();
        if (slope <= 0) slope = double.Parse(Environment.GetEnvironmentVariable("G3_L") ?? "0.25", S.Inv);
        double diag = input.CachedBounds.DiagonalLength;
        S.Samples(input, nSamples, out var pos, out var nrm, out var tid);
        var tree = new DMeshAABBTree3(input, true);
        double a40 = diag / 40, a160 = diag / 160, offRel = 1.0 / 600;

        // uniform references (the first pass of post is the /40 run)
        var u = new Dictionary<int, Run>();
        foreach (int ra in new[] { 40, 80, 160 }) { u[ra] = Exec(name, "uniform" + ra, input, pos, diag, 0, ra, null, 0); rows.Add(u[ra].Row); if (u[ra].D == null) return rows; }
        double T = Math.Max(S.Quantile(u[80].D, .95), 2 * offRel);                // the target deviation: the uniform alpha / 80's p95
        double p1 = u[40].Secs;

        // oracle
        {
            var aj = new double[nSamples];
            for (int i = 0; i < nSamples; ++i) aj[i] = u[40].D[i] <= T ? a40 : u[80].D[i] <= T ? diag / 80 : u[160].D[i] <= T ? a160 : a40;
            var f = new AlphaGridField(pos, aj, a40, a160, slope);
            var r = Exec(name, "oracle_p95_80", input, pos, diag, 0, 40, f.AtTimed, f.FineSamples, f); rows.Add(r.Row);
        }
        // post: alpha_j = alpha40 * T / d_j (one more pass), post2: the same rule on the post result's distances (a third pass)
        Func<string, double, double, double[], Run> postRun = null;
        Run PostPair(string tag, double Tt, double sl)
        {
            var aj = new double[nSamples];
            for (int i = 0; i < nSamples; ++i) aj[i] = Math.Min(a40, Math.Max(a160, a40 * Tt / Math.Max(u[40].D[i], 1e-12)));
            var f = new AlphaGridField(pos, aj, a40, a160, sl);
            var post = Exec(name, "post" + tag, input, pos, diag, p1, 40, f.AtTimed, f.FineSamples, f); rows.Add(post.Row);
            if (post.D == null) return post;
            var aj2 = new double[nSamples];
            for (int i = 0; i < nSamples; ++i) aj2[i] = Math.Min(a40, Math.Max(a160, aj[i] * Tt / Math.Max(post.D[i], 1e-12)));
            // only where it is finer: a sample that met the target is not relaxed back (that would undo the first refinement near it)
            for (int i = 0; i < nSamples; ++i) aj2[i] = Math.Min(aj[i], aj2[i]);
            var f2 = new AlphaGridField(pos, aj2, a40, a160, sl);
            var r2 = Exec(name, "post2" + tag, input, pos, diag, p1 + post.Secs, 40, f2.AtTimed, f2.FineSamples, f2); rows.Add(r2.Row);
            return post;
        }
        PostPair("_p95_80", T, slope);
        double T160 = Math.Max(S.Quantile(u[160].D, .95), 2 * offRel);
        PostPair("_p95_160", T160, slope);
        // the gradation slope: L / 2 on the first target
        {
            double Tq = T;
            var aj = new double[nSamples];
            for (int i = 0; i < nSamples; ++i) aj[i] = Math.Min(a40, Math.Max(a160, a40 * Tq / Math.Max(u[40].D[i], 1e-12)));
            var f = new AlphaGridField(pos, aj, a40, a160, slope / 2);
            var rr = Exec(name, "postLhalf_p95_80", input, pos, diag, p1, 40, f.AtTimed, f.FineSamples, f); rows.Add(rr.Row);
        }
        // lfs: a priori, one pass
        {
            double ts = Stopwatch.GetTimestamp();
            var g = FreeDistance(input, tree, pos, nrm, tid, diag);
            var aj = new double[nSamples];
            for (int i = 0; i < nSamples; ++i) aj[i] = Math.Min(a40, Math.Max(a160, g[i] * diag / 3));
            var f = new AlphaGridField(pos, aj, a40, a160, slope);
            double est = (Stopwatch.GetTimestamp() - ts) / Stopwatch.Frequency;
            var r = Exec(name, "lfs_g3", input, pos, diag, est, 40, f.AtTimed, f.FineSamples, f); rows.Add(r.Row);
        }
        return rows;
    }
}
