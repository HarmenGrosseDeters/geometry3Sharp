using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static partial class Spike
{
    // proto <proc|mesh> <alpha> <floor> <maxdeg> <cfg> [nohdr]    cfg: comma list of master, p1, p2, p3, p4 (p4 sets the max-angle objective to <maxdeg>)
    const string ProtoHeader = "name,alpha,floor,G,cfg,status,tets,imp_s,stage1_s,imp_us_per_tet,total_us_per_tet,min,p01,max,p99,min_lt_F,max_gt_G,fail_either,valid,counts";

    static IEnumerable<string> ProtoAll(string name, DMesh3 input, double alpha, double floor, double G, string[] cfgs)
    {
        // stage 1 once per distinct refinement floor (flag rNN), then every configuration's improver from that result
        var stage = new Dictionary<double, (MeshTetrahedralization tm, double secs)>();
        foreach (string cfg in cfgs) {
            var flags = new HashSet<string>(cfg.Split(',', StringSplitOptions.RemoveEmptyEntries));
            double rf = flags.FirstOrDefault(x => x.Length > 1 && x[0] == 'r' && char.IsDigit(x[1])) is string rr ? double.Parse(rr.Substring(1), Inv) : floor;
            if (!stage.TryGetValue(rf, out var st)) {
                var sw0 = Stopwatch.StartNew();
                var tm0 = new MeshTetrahedralization(input) { RelativeAlpha = alpha, RelativeOffset = 600, SliverDihedralDegrees = rf, Improve = false };
                tm0.Compute();
                stage[rf] = st = (tm0, sw0.Elapsed.TotalSeconds);
            }
            string row;
            try { row = Improve(name, input, alpha, floor, G, cfg, flags, st.tm, st.secs); }
            catch (Exception e) { row = name + "," + F(alpha) + "," + F(floor) + "," + F(G) + "," + cfg.Replace(',', '+') + ",EXCEPTION_" + e.GetType().Name; }
            yield return row;
        }
    }

    static string Improve(string name, DMesh3 input, double alpha, double floor, double G, string cfg, HashSet<string> flags, MeshTetrahedralization tm, double stage1)
    {
        if (tm.Result == null) return name + "," + F(alpha) + "," + F(floor) + "," + F(G) + "," + cfg + "," + tm.Status;
        var r0 = tm.Result;
        var sw = Stopwatch.StartNew();
        Imp2.QF = floor; Imp2.QG = flags.Contains("p4") ? G : 0; Imp2.QW = flags.FirstOrDefault(x => x.Length > 1 && x[0] == 'w' && char.IsDigit(x[1])) is string ww ? double.Parse(ww.Substring(1), Inv) / 100 : 1;
        var imp = new Imp2(r0) { TargetDegrees = floor };
        imp.InputTree = new DMeshAABBTree3(input, true);
        imp.BoundarySlideLimit = tm.BoundarySlideFactor * tm.UsedOffset;
        imp.TangentOnly = tm.BoundaryMovesTangentOnly;
        imp.MaxPasses = tm.ImprovePasses; imp.RescuePasses = tm.RescuePasses;
        imp.Contraction = tm.ContractEdges; imp.Insertion = tm.SplitEdges;
        imp.ActiveAlways = flags.Contains("p1");
        imp.MultiFace = flags.Contains("p2");
        imp.CavityInsertion = flags.Contains("p3");
        imp.Run();
        var r = imp.Result();
        double impS = sw.Elapsed.TotalSeconds;
        int n = r.Tets.Count;
        var mins = new double[n]; var maxs = new double[n]; int lF = 0, gG = 0, either = 0;
        for (int i = 0; i < n; ++i) {
            Index4i t = r.Tets[i];
            Dihedrals(r.Vertices[t.a], r.Vertices[t.b], r.Vertices[t.c], r.Vertices[t.d], out mins[i], out maxs[i]);
            bool a = mins[i] < floor, b = maxs[i] > G; if (a) lF++; if (b) gG++; if (a || b) either++;
        }
        Array.Sort(mins); Array.Sort(maxs);
        string valid = TetMeshQuality.NotValidReason(r) == null ? "yes" : "NO";
        string chk = "";
        if (Environment.GetEnvironmentVariable("G3_CHECK") == "1") {
            DMesh3 bm = r.ToSurfaceMesh();
            bool selfInt = MeshRepairMetrics.HasSelfIntersections(bm);
            var tree = new DMeshAABBTree3(bm, true); tree.WindingNumber(Vector3d.Zero);
            int outside = 0, tested = 0; int step = Math.Max(1, input.VertexCount / 3000);
            for (int v = 0; v < input.MaxVertexID; v += step) { if (!input.IsVertex(v)) continue; tested++; if (!(tree.WindingNumber(input.GetVertex(v)) > 0.5)) outside++; }
            chk = " check_selfint=" + (selfInt ? 1 : 0) + " check_input_verts_outside=" + outside + "/" + tested;
        }
        string counts = chk + string.Join(" ", imp.Counts.Where(c => c.Value != 0 && !c.Key.StartsWith("left_")).Select(c => c.Key + "=" + c.Value));
        return string.Join(",", name, F(alpha), F(floor), F(G), cfg.Replace(',', '+'), tm.Status, n, F(impS), F(stage1), F(impS * 1e6 / Math.Max(1, n)), F((impS + stage1) * 1e6 / Math.Max(1, n)),
            F(mins[0]), F(mins[(int)(0.01 * (n - 1))]), F(maxs[n - 1]), F(maxs[(int)(0.99 * (n - 1))]), lF, gG, either, valid, counts);
    }
}
