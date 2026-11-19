// MeshTetrahedralization.MaxDihedralDegrees (stage 2, opt-in objective on both ends): per input, stage 1 once (Improve = false), then the
// improver of stage 2 from that result for three configurations, one CSV row each (columns of benchmarks/TetMeshBenchmark/compare/maxdih_summary.py):
//   master  the objective off (the improver as it is without the option)
//   p4      MaxDihedralDegrees = G without the guard
//   p4g     MaxDihedralDegrees = G with the guard
//   p4s     p4g after a first phase on the smallest dihedral alone (TetMeshImprover.MaxDihedralStaged)
// Usage: ... -- maxdih <mesh|proc> <relativeAlpha> <floor> <G> [nohdr]
// With G3_CHECK=1 each row also gets the boundary's self-intersection and the number of input vertices outside the result.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

static class MaxDihedralSample
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string F(double v) => v.ToString("G5", Inv);
    public const string Header = "name,alpha,floor,G,cfg,status,tets,imp_s,stage1_s,imp_us_per_tet,total_us_per_tet,min,p01,max,p99,min_lt_F,max_gt_G,fail_either,valid,counts";

    public static int Run(string[] args, IEnumerable<KeyValuePair<string, DMesh3>> procedural)
    {
        string src = args[1];
        double alpha = double.Parse(args[2], Inv), floor = double.Parse(args[3], Inv), G = double.Parse(args[4], Inv);
        var inputs = src == "proc" ? procedural.ToList()
            : new List<KeyValuePair<string, DMesh3>> { new KeyValuePair<string, DMesh3>(System.IO.Path.GetFileName(src), StandardMeshReader.ReadMesh(src)) };
        if (args.Length <= 5 || args[5] != "nohdr") Console.WriteLine(Header);
        foreach (var kv in inputs) foreach (string line in Rows(kv.Key, kv.Value, alpha, floor, G)) Console.WriteLine(line);
        return 0;
    }

    static IEnumerable<string> Rows(string name, DMesh3 input, double alpha, double floor, double G)
    {
        var sw0 = Stopwatch.StartNew();
        var tm = new MeshTetrahedralization(input) { RelativeAlpha = alpha, RelativeOffset = 600, SliverDihedralDegrees = floor, Improve = false };
        tm.Compute();
        double stage1 = sw0.Elapsed.TotalSeconds;
        foreach (string cfg in new[] { "master", "p4", "p4g", "p4s" }) {
            string row;
            try { row = One(name, input, alpha, floor, G, cfg, tm, stage1); }
            catch (Exception e) { row = name + "," + F(alpha) + "," + F(floor) + "," + F(G) + "," + cfg + ",EXCEPTION_" + e.GetType().Name; }
            yield return row;
        }
    }

    static string One(string name, DMesh3 input, double alpha, double floor, double G, string cfg, MeshTetrahedralization tm, double stage1)
    {
        string head = name + "," + F(alpha) + "," + F(floor) + "," + F(G) + "," + cfg;
        if (tm.Result == null) return head + "," + tm.Status;
        TetMesh3 r0 = tm.Result;
        var sw = Stopwatch.StartNew();
        var imp = new TetMeshImprover(r0) { TargetDegrees = floor };
        imp.InputTree = new DMeshAABBTree3(input, true);
        imp.BoundarySlideLimit = tm.BoundarySlideFactor * tm.UsedOffset;
        imp.TangentOnly = tm.BoundaryMovesTangentOnly;
        imp.MaxPasses = tm.ImprovePasses; imp.RescuePasses = tm.RescuePasses;
        imp.Contraction = tm.ContractEdges; imp.Insertion = tm.SplitEdges;
        if (cfg != "master") { imp.MaxDihedralDegrees = G; imp.MaxDihedralGuard = cfg != "p4"; imp.MaxDihedralStaged = cfg == "p4s"; }
        imp.Run();
        TetMesh3 r = imp.Result();
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
            chk = " check_selfint=" + (selfInt ? 1 : 0) + " check_input_verts_outside=" + outside + "/" + tested + " ";
        }
        int s0F = 0, s0G = 0;       // the stage-1 result: tets below F / above G before the improver
        foreach (Index4i t in r0.Tets) {
            Dihedrals(r0.Vertices[t.a], r0.Vertices[t.b], r0.Vertices[t.c], r0.Vertices[t.d], out double a0, out double b0);
            if (a0 < floor) s0F++; if (b0 > G) s0G++;
        }
        chk += "start_min_lt_F=" + s0F + " start_max_gt_G=" + s0G + " ";
        string counts = chk + string.Join(" ", imp.Counts.Where(c => c.Value != 0 && !c.Key.StartsWith("left_")).Select(c => c.Key + "=" + c.Value));
        return string.Join(",", head, tm.Status, n, F(impS), F(stage1), F(impS * 1e6 / Math.Max(1, n)), F((impS + stage1) * 1e6 / Math.Max(1, n)),
            F(mins[0]), F(mins[(int)(0.01 * (n - 1))]), F(maxs[n - 1]), F(maxs[(int)(0.99 * (n - 1))]), lF, gG, either, valid, counts);
    }

    // per-tet dihedral extremes in degrees; -1 / 181 for a non-positive tet
    static void Dihedrals(Vector3d a, Vector3d b, Vector3d c, Vector3d d, out double mn, out double mx)
    {
        mn = 180; mx = 0;
        if (ExactPredicates.Orient3d(a, b, c, d) <= 0) { mn = -1; mx = 181; return; }
        Vector3d[] n = { (c - b).Cross(d - b), (d - a).Cross(c - a), (b - a).Cross(d - a), (c - a).Cross(b - a) };
        for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) {
            double x = 180 - Math.Atan2(n[i].Cross(n[j]).Length, n[i].Dot(n[j])) * (180 / Math.PI);
            if (x < mn) mn = x; if (x > mx) mx = x;
        }
    }
}
