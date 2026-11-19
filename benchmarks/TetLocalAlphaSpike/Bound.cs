// Upper bound of a local alpha (TET_MESHING.md 17.2, P4(a)): per input, the uniform wraps at alpha / 40, / 80, / 160 give the input-to-boundary
// distance d_l(x) at area-weighted input samples x and the tets (by centroid) of each level. A perfect local alpha (an oracle that knows d_l)
// uses, at x, the coarsest level whose d_l(x) is within the target T, and the tets of that level around x: the tets are attributed to the
// level of the nearest sample (strict), or of the nearest sample after dilating the levels by a transition band of alpha / 40 (one coarse alpha) (dilated).
// Ignores the grading and the wrap's neighbour effects, so it is a bound, not a prediction.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using g3;
using gs;

static class Bound
{
    static readonly int[] Levels = { 40, 80, 160 };
    public const string Header = "name,tris,samples,"
        + "tets40,tets80,tets160,t40,t80,t160,"
        + "max40,max80,max160,p99_40,p99_80,p99_160,p95_40,p95_80,p95_160,p50_40,p50_80,p50_160,"
        + "lfs_lt_2a,area_gap,area_thin_only,gap_share_of_d40_gt_half_alpha,thin_share_of_d40_gt_half_alpha,gap_share_of_worst5pct,thin_share_of_worst5pct,share_lfs_lt_2a_of_d40_gt_half_alpha,share_lfs_lt_2a_of_worst5pct,irreducible_share_of_gt_half_alpha,frac_d40_gt_half_alpha,frac_d40_gt_alpha,"
        + "target,T,frac_fine_strict,frac_fine_dilated,tets_local_strict,tets_local_dilated,ratio_vs80_strict,ratio_vs80_dilated,ratio_vs160_dilated,ach_p95_over_T,ach_max_over_T";

    public static List<string> Run(string name, DMesh3 input, int nSamples = 20000)
    {
        var rows = new List<string>();
        double diag = input.CachedBounds.DiagonalLength;
        S.Samples(input, nSamples, out var pos, out var nrm, out var tid);
        var inTree = new DMeshAABBTree3(input, true);

        var d = new double[3][]; var tets = new int[3]; var secs = new double[3]; var cent = new Vector3d[3][];
        for (int l = 0; l < 3; ++l) {
            var sw = Stopwatch.StartNew();
            var tm = new MeshTetrahedralization(input) { RelativeAlpha = Levels[l], RelativeOffset = 600 };
            tm.Compute();
            secs[l] = sw.Elapsed.TotalSeconds;
            if (tm.Result == null || tm.Status != MeshTetrahedralization.TetStatus.Success) { Console.Error.WriteLine(name + " level " + Levels[l] + ": " + tm.Status); return rows; }
            tets[l] = tm.Result.Tets.Count;
            d[l] = S.Dist(tm.Result.ToSurfaceMesh(), pos);
            for (int i = 0; i < d[l].Length; ++i) d[l][i] /= diag;
            cent[l] = new Vector3d[tets[l]];
            for (int k = 0; k < tets[l]; ++k) { var q = tm.Result.Tets[k]; var V = tm.Result.Vertices; cent[l][k] = 0.25 * (V[q.a] + V[q.b] + V[q.c] + V[q.d]); }
        }
        double a40 = 1.0 / 40;      // alpha / 40 relative to the diagonal
        // feature size estimate, with the input's orientation voted by its signed volume (outward = positive): the free distance along the
        // outward normal (gap: a ball outside the input has to fit into it) and the distance along the inward normal (thickness)
        double vol = 0; foreach (int t in input.TriangleIndices()) { Vector3d va = Vector3d.Zero, vb = Vector3d.Zero, vc = Vector3d.Zero; input.GetTriVertices(t, ref va, ref vb, ref vc); vol += va.Dot(vb.Cross(vc)) / 6; }
        double osgn = vol >= 0 ? 1 : -1;
        var gap = new double[nSamples]; var thick = new double[nSamples];
        System.Threading.Tasks.Parallel.For(0, nSamples, i => {
            for (int k = 0; k < 2; ++k) {
                var dir = (k == 0 ? osgn : -osgn) * nrm[i];
                var ray = new Ray3d(pos[i] + dir * 1e-7 * diag, dir);
                double best = double.PositiveInfinity;
                int t = inTree.FindNearestHitTriangle(ray);
                if (t >= 0 && t != tid[i]) { m_tri(input, t, out var tri); var it = new IntrRay3Triangle3(ray, tri); if (it.Find()) best = it.RayParameter / diag; }
                if (k == 0) gap[i] = best; else thick[i] = best;
            }
        });
        var lfs = new double[nSamples];
        for (int i = 0; i < nSamples; ++i) lfs[i] = Math.Min(gap[i], thick[i]);
        int nGap = 0, nThin = 0; for (int i = 0; i < nSamples; ++i) { if (gap[i] < 2 * a40) nGap++; else if (thick[i] < 2 * a40) nThin++; }
        int feat = nGap + nThin;
        var big = Enumerable.Range(0, nSamples).Where(i => d[0][i] > 0.5 * a40).ToArray();
        double q95 = S.Quantile(d[0], 0.95);
        var worst = Enumerable.Range(0, nSamples).Where(i => d[0][i] >= q95).ToArray();
        double shareBig = big.Length == 0 ? 0 : big.Count(i => lfs[i] < 2 * a40) / (double)big.Length;
        double gapBig = big.Length == 0 ? 0 : big.Count(i => gap[i] < 2 * a40) / (double)big.Length, thinBig = big.Length == 0 ? 0 : big.Count(i => gap[i] >= 2 * a40 && thick[i] < 2 * a40) / (double)big.Length;
        double gapWorst = worst.Length == 0 ? 0 : worst.Count(i => gap[i] < 2 * a40) / (double)worst.Length, thinWorst = worst.Length == 0 ? 0 : worst.Count(i => gap[i] >= 2 * a40 && thick[i] < 2 * a40) / (double)worst.Length;
        double shareWorst = worst.Length == 0 ? 0 : worst.Count(i => lfs[i] < 2 * a40) / (double)worst.Length;
        double irr = big.Length == 0 ? 0 : big.Count(i => d[2][i] > 0.7 * d[0][i]) / (double)big.Length;
        string stats = string.Join(",", name, input.TriangleCount, nSamples, tets[0], tets[1], tets[2], S.F(secs[0]), S.F(secs[1]), S.F(secs[2]),
            S.F(d[0].Max()), S.F(d[1].Max()), S.F(d[2].Max()), S.F(S.Quantile(d[0], .99)), S.F(S.Quantile(d[1], .99)), S.F(S.Quantile(d[2], .99)),
            S.F(S.Quantile(d[0], .95)), S.F(S.Quantile(d[1], .95)), S.F(S.Quantile(d[2], .95)), S.F(S.Quantile(d[0], .5)), S.F(S.Quantile(d[1], .5)), S.F(S.Quantile(d[2], .5)),
            S.F(feat / (double)nSamples), S.F(nGap / (double)nSamples), S.F(nThin / (double)nSamples), S.F(gapBig), S.F(thinBig), S.F(gapWorst), S.F(thinWorst), S.F(shareBig), S.F(shareWorst), S.F(irr), S.F(big.Length / (double)nSamples), S.F(d[0].Count(x => x > a40) / (double)nSamples));

        // the oracle
        var grid = new S.Grid(pos, a40 * diag);
        var targets = new (string, double)[] {
            ("p95_80", S.Quantile(d[1], .95)), ("max_80", d[1].Max()), ("p95_160", S.Quantile(d[2], .95)), ("max_160", d[2].Max()) };
        foreach (var (tn, T0) in targets) {
            double T = Math.Max(T0, 2.0 / 600);          // the offset (diagonal / 600) is the floor of every deviation: a target below it is not one
            var lev = new int[nSamples];
            for (int i = 0; i < nSamples; ++i) lev[i] = d[0][i] <= T ? 0 : d[1][i] <= T ? 1 : 2;
            var ach = new double[nSamples]; for (int i = 0; i < nSamples; ++i) ach[i] = d[lev[i]][i];
            // the unfixable (d_160 > T) stay at the coarsest level: they are not worth tets
            for (int i = 0; i < nSamples; ++i) if (d[2][i] > T) { lev[i] = 0; ach[i] = d[0][i]; }
            // dilation: a sample takes the finest level within 2 * alpha / 40
            var dil = (int[])lev.Clone(); var buf = new List<int>();
            for (int i = 0; i < nSamples; ++i) { grid.InBall(pos[i], a40 * diag, buf); foreach (int j in buf) if (lev[j] > dil[i]) dil[i] = lev[j]; }
            double nS = 0, nD = 0; long tl = 0, tdl = 0;
            for (int l = 0; l < 3; ++l) {
                int cs = 0, cd = 0;
                for (int k = 0; k < cent[l].Length; ++k) {
                    int s = grid.Nearest(cent[l][k], 10 * diag); if (s < 0) { if (l == 0) { cs++; cd++; } continue; }
                    if (lev[s] == l) cs++; if (dil[s] == l) cd++;
                }
                tl += cs; tdl += cd;
            }
            nS = lev.Count(x => x > 0) / (double)nSamples; nD = dil.Count(x => x > 0) / (double)nSamples;
            rows.Add(stats + "," + string.Join(",", tn, S.F(T), S.F(nS), S.F(nD), tl, tdl, S.F(tl / (double)tets[1]), S.F(tdl / (double)tets[1]), S.F(tdl / (double)tets[2]),
                S.F(S.Quantile(ach, .95) / T), S.F(ach.Max() / T)));
        }
        return rows;
    }

    static void m_tri(DMesh3 m, int t, out Triangle3d tri) { Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero; m.GetTriVertices(t, ref a, ref b, ref c); tri = new Triangle3d(a, b, c); }
}
