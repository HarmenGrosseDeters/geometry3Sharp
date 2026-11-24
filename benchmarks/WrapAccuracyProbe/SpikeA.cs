// Spike A: wrap, naive snap back, validity-preserving snap back; accuracy, cost, validity per (Alpha, Offset).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;
using gs;

static class SpikeA
{
    public class WrapRun
    {
        public DMesh3 Result; public string Status; public double Ms; public double Alpha, Offset; public int Runs;
    }

    // the raw wrap (the offset surface as returned by MeshAlphaWrap.Compute()); best of 3 if one run takes less than 5 s
    public static WrapRun Wrap(DMesh3 input, double alpha, double offset, double limitSeconds, int maxRuns = 3)
    {
        var best = new WrapRun { Ms = double.MaxValue };
        for (int run = 0; run < maxRuns; ++run) {
            var sw = Stopwatch.StartNew();
            var w = new MeshAlphaWrap(input) { Alpha = alpha, Offset = offset };
            w.CancelF = () => sw.Elapsed.TotalSeconds > limitSeconds;
            bool ok = w.Compute();
            double ms = sw.Elapsed.TotalMilliseconds;
            if (!ok || (w.Status != MeshAlphaWrap.WrapStatus.Success && w.Status != MeshAlphaWrap.WrapStatus.PointLimitReached)) {
                return new WrapRun { Result = null, Status = w.Status.ToString(), Ms = ms, Alpha = alpha, Offset = offset, Runs = run + 1 };
            }
            if (ms < best.Ms) best = new WrapRun { Result = w.Result, Status = w.Status.ToString(), Ms = ms, Alpha = w.UsedAlpha, Offset = w.UsedOffset };
            best.Runs = run + 1;
            if (ms > 5000) break;
        }
        return best;
    }

    public static Dictionary<string, object> Describe(string variant, string name, double alphaDen, double offsetRel, DMesh3 res, DMesh3 input, DMeshAABBTree3 inTree,
        DMeshAABBTree3 resTree, double diag, double offset, double volIn, Vector3d origin, double hdSeconds)
    {
        var r = new Dictionary<string, object> { ["input"] = name, ["alpha_den"] = alphaDen, ["offset_rel"] = offsetRel, ["variant"] = variant, ["diag"] = diag };
        r["tris"] = res.TriangleCount;
        double vol = Metrics.SignedVolume(res, origin);
        r["vol_err"] = (vol - volIn) / volIn;
        r["area_ratio"] = Metrics.Area(res) / Metrics.Area(input);
        double tol = 0.05 * offset;
        Metrics.Hd a = Metrics.OneSided(res, input, inTree, tol, hdSeconds);
        r["h_res_in"] = a.Upper; r["h_res_in_lo"] = a.Lower; r["h_res_in_conv"] = a.Converged;
        Metrics.Hd b = Metrics.OneSided(input, res, resTree, tol, hdSeconds);
        r["h_in_res"] = b.Upper; r["h_in_res_lo"] = b.Lower; r["h_in_res_conv"] = b.Converged;
        r["hd_ms"] = a.Ms + b.Ms;
        double[] p = Metrics.DistPercentiles(res, inTree, 20000), q = Metrics.DistPercentiles(input, resTree, 20000);
        r["res_in_p50"] = p[0]; r["res_in_p90"] = p[1]; r["res_in_p99"] = p[2]; r["in_res_p50"] = q[0]; r["in_res_p90"] = q[1]; r["in_res_p99"] = q[2];
        r["verdict"] = Metrics.Verdict(res);
        r["closed"] = res.IsClosed();
        return r;
    }

    public static List<Dictionary<string, object>> Run(string spec, double alphaDen, double offsetRel, double limit)
    {
        string name = Inputs.NameOf(spec);
        DMesh3 input = Inputs.Load(spec);
        if (input == null) throw new Exception("cannot load " + spec);
        double diag = Inputs.Diag(input);
        double alpha = diag / alphaDen, offset = diag * offsetRel;
        var rows = new List<Dictionary<string, object>>();
        var inTree = new DMeshAABBTree3(input, true);
        Vector3d origin = input.GetBounds().Center;
        double volIn = Metrics.SignedVolume(input, origin);
        WrapRun wr = Wrap(input, alpha, offset, limit);
        if (wr.Result == null) {
            rows.Add(new Dictionary<string, object> { ["input"] = name, ["alpha_den"] = alphaDen, ["offset_rel"] = offsetRel, ["variant"] = "wrap", ["status"] = wr.Status, ["ms"] = wr.Ms, ["diag"] = diag, ["tris"] = 0 });
            return rows;
        }
        double hdSec = 40;
        var wTree = new DMeshAABBTree3(wr.Result, true);
        var row = Describe("wrap", name, alphaDen, offsetRel, wr.Result, input, inTree, wTree, diag, offset, volIn, origin, hdSec);
        row["status"] = wr.Status; row["ms"] = wr.Ms; row["runs"] = wr.Runs;
        rows.Add(row);

        // naive
        var sw = Stopwatch.StartNew();
        DMesh3 naive = SnapBack.Naive(wr.Result, input, inTree);
        double nms = sw.Elapsed.TotalMilliseconds;
        var nTree = new DMeshAABBTree3(naive, true);
        row = Describe("naive", name, alphaDen, offsetRel, naive, input, inTree, nTree, diag, offset, volIn, origin, hdSec);
        SnapBack.Damage(wr.Result, naive, diag, out int fl, out int dg, out double sh);
        row["flipped"] = fl; row["collapsed"] = dg; row["bad_area_share"] = sh; row["snap_ms"] = nms;
        row["selfint"] = SafeSelfInt(naive);
        rows.Add(row);

        // validity-preserving
        var st = new BackStats();
        DMesh3 safe = SnapBack.Safe(wr.Result, input, inTree, diag, new SnapBack.Options(), st);
        var sTree = new DMeshAABBTree3(safe, true);
        row = Describe("safe", name, alphaDen, offsetRel, safe, input, inTree, sTree, diag, offset, volIn, origin, hdSec);
        row["snap_ms"] = st.Ms; row["verts"] = st.Vertices;
        row["moved_full"] = (double)st.Full / st.Vertices; row["moved_partial"] = (double)st.Partial / st.Vertices; row["moved_none"] = (double)st.None / st.Vertices;
        row["rej_flip"] = st.RejFlip; row["rej_degenerate"] = st.RejDegenerate; row["rej_self"] = st.RejSelf; row["rounds"] = st.Rounds;
        row["selfint"] = SafeSelfInt(safe);
        rows.Add(row);
        return rows;
    }

    // independent check of the result (not the incremental one): number of self-intersecting triangle pairs, at most 1000 counted
    public static int SafeSelfInt(DMesh3 m)
    {
        try { return MeshRepairMetrics.SelfIntersectionsAtMost(m, 1000); } catch { return -1; }
    }
}
