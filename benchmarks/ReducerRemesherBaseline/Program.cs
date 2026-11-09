// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Baseline of Reducer and Remesher (Reducer / Remesher 2.0, docs/design/REDUCER_REMESHER_20.md): per case and
// variant the time, the triangle quality (min angle, p01, slivers < 0.5 and > 170 degrees), the fidelity (Hausdorff
// both ways, F-score, volume IoU), the topology (genus kept, manifold, new self-intersections), the determinism
// (repeated runs, parallel = serial) and the scale invariance (the eight similarities of tests/.../Invariance.cs).
// No library change; procedural inputs plus the Thingi10K selection of benchmarks/RemeshBenchmark (fetch_data.py).
//
//   dotnet run -c Release --project benchmarks/ReducerRemesherBaseline -- [thingi=<dir>] [only=<regex>] [procedural=1]
//       [runs=2] [si_max=30000] [inv_max=30000] [invariance=1] [fidelity=1] [ops=reduce,remesh] [json=<file>]
//       [hd_tol=<f>] [profile=1] [reduce_fold=0] [reduce_worst=1] [reduce_q=<q>] [remesh_q=<q>] [remesh_crease=<deg>] [summary=<json file>]     (only the summary tables of an earlier run's JSON lines)
// One JSON line per case and variant on stdout (or json=<file>), then the summary tables as Markdown.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BaselineHarness;
using g3;
using gs;

static class Program
{
    class Case
    {
        public string Group, Name;
        public DMesh3 Input;
        public double Diagonal, MeanEdge;
        public bool Closed;
        public int SelfPairs;
        public AngleSummary Angles;
        public MeshTopologySummary Topology;
    }

    class Run
    {
        public string Op, Variant, Group, Case, TargetLabel;
        public Dictionary<string, object> Fields = new Dictionary<string, object>();
        public double D(string k) { return Fields.TryGetValue(k, out object v) ? (v is double d ? d : v is int i ? i : double.NaN) : double.NaN; }
        public int I(string k) { return Fields.TryGetValue(k, out object v) ? (v is int d ? d : v is double e ? (int)e : 0) : 0; }
        public bool B(string k) { return Fields.TryGetValue(k, out object v) && v is bool d && d; }
    }

    // an operation: input mesh, target (reduce: triangle count; remesh: edge length), self-intersection check, parallel
    delegate DMesh3 Operation(DMesh3 input, double target, bool si, bool parallel);

    static double ReduceQuality, RemeshQuality;      // reduce_q / remesh_q: Reducer.MinCollapseTriangleQuality / Remesher.MinTriangleQuality (0: library default, off)
    static double RemeshCrease = 30;      // remesh_crease=<deg>: the crease angle of PreserveBoundaryAndFeatures (0: the boundary only)
    static double HdTolerance = 0.001;      // hd_tol: tolerance of the certified Hausdorff bounds, relative to the box diagonal
    static bool ReduceFoldOver = true, ReduceAgainstWorst;      // reduce_fold=0: Reducer.PreventFoldOver off; reduce_worst=1: the internal CollapseQualityAgainstWorst (by reflection)
    static bool Profile;      // profile=1: Remesher.ENABLE_PROFILING (per pass phase times on stdout, for the upper bounds of the design note)

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        string get(string k, string d) { return A.ContainsKey(k) ? A[k] : d; }
        if (A.ContainsKey("summary")) {
            Summary(LoadRows(A["summary"]), null);
            return 0;
        }
        Profile = get("profile", "0") == "1";
        ReduceFoldOver = get("reduce_fold", "1") != "0"; ReduceAgainstWorst = get("reduce_worst", "0") == "1";
        RemeshCrease = double.Parse(get("remesh_crease", "30"), CultureInfo.InvariantCulture);
        HdTolerance = double.Parse(get("hd_tol", "0.001"), CultureInfo.InvariantCulture);
        ReduceQuality = double.Parse(get("reduce_q", "0"), CultureInfo.InvariantCulture); RemeshQuality = double.Parse(get("remesh_q", "0"), CultureInfo.InvariantCulture);
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        int runs = Math.Max(1, int.Parse(get("runs", "2")));
        int siMax = int.Parse(get("si_max", "30000")), invMax = int.Parse(get("inv_max", "30000"));
        bool invariance = get("invariance", "1") != "0", fidelity = get("fidelity", "1") != "0";
        var ops = get("ops", "reduce,remesh").Split(',');
        TextWriter json = A.ContainsKey("json") ? new StreamWriter(A["json"]) : Console.Out;

        var inputs = new List<(string group, string name, DMesh3 mesh)>();
        if (get("procedural", "1") != "0") {
            inputs.Add(("procedural", "bumpy_sphere", Inputs.BumpySphere(90)));
            inputs.Add(("procedural", "torus", Inputs.Torus(260, 130)));
            inputs.Add(("procedural", "terrain", Inputs.Terrain(220)));
            inputs.Add(("procedural", "box", Inputs.Box(70)));
        }
        if (A.ContainsKey("thingi"))
            foreach (string f in Directory.GetFiles(A["thingi"]).OrderBy(f => f, StringComparer.Ordinal)) {
                DMesh3 m = StandardMeshReader.ReadMesh(f);
                if (m != null && m.TriangleCount > 0) inputs.Add(("thingi", Path.GetFileNameWithoutExtension(f), m));
            }

        var cases = new List<Case>();
        foreach (var (group, name, mesh) in inputs) {
            if (only != null && !only.IsMatch(name)) continue;
            var c = new Case() { Group = group, Name = name, Input = mesh, Diagonal = mesh.CachedBounds.DiagonalLength, MeanEdge = Inputs.MeanEdge(mesh), Closed = mesh.IsClosed() };
            c.SelfPairs = MeshRepairMetrics.SelfIntersections(mesh);
            c.Angles = AngleSummary.Compute(mesh);
            c.Topology = MeshTopologySummary.Compute(mesh);
            cases.Add(c);
        }
        if (cases.Count == 0) { Console.Error.WriteLine("no cases"); return 1; }

        // JIT warm-up on the smallest case, so the first timing is not a cold one
        Case small = cases.OrderBy(c => c.Input.TriangleCount).First();
        Reduce(small.Input, Math.Max(4, small.Input.TriangleCount / 10), false, true);
        Remesh(small.Input, 2 * small.MeanEdge, false, true);

        var all = new List<Run>();
        var json_opts = new JsonSerializerOptions() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        foreach (var c in cases) {
            int tris = c.Input.TriangleCount;
            var work = new List<(string op, string label, double target, Operation f)>();
            if (ops.Contains("reduce"))
                foreach (double frac in new[] { 0.1, 0.01 })
                    work.Add(("reduce", frac.ToString("P0", CultureInfo.InvariantCulture).Replace(" ", ""), Math.Max(4, Math.Round(frac * tris)), Reduce));
            if (ops.Contains("remesh"))
                foreach (double f in new[] { 1.0, 2.0 })
                    work.Add(("remesh", "x" + f.ToString(CultureInfo.InvariantCulture), f * c.MeanEdge, Remesh));
            foreach (var w in work)
                foreach (string variant in new[] { "default", "si" }) {
                    if (variant == "si" && tris > siMax) continue;
                    Run r = Measure(c, w.op, variant, w.label, w.target, w.f, runs, fidelity, invariance && tris <= invMax);
                    all.Add(r);
                    var line = new Dictionary<string, object>() { ["op"] = r.Op, ["variant"] = r.Variant, ["target"] = r.TargetLabel, ["group"] = r.Group, ["case"] = r.Case };
                    foreach (var kv in r.Fields) line[kv.Key] = kv.Value;
                    json.WriteLine(JsonSerializer.Serialize(line, json_opts));
                    json.Flush();
                }
        }
        if (A.ContainsKey("json")) json.Dispose();
        Summary(all, cases);
        return 0;
    }

    // ---- the operations under test (library defaults, as a caller would use them) ----

    static DMesh3 Reduce(DMesh3 input, double targetTriangles, bool si, bool parallel)
    {
        var m = new DMesh3(input);
        var r = new Reducer(m) { PreventSelfIntersections = si, MinCollapseTriangleQuality = ReduceQuality, PreventFoldOver = ReduceFoldOver };
        if (ReduceAgainstWorst)
            typeof(MeshRefinerBase).GetField("CollapseQualityAgainstWorst", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(r, true);
        r.ReduceToTriangleCount((int)targetTriangles);
        return m;
    }

    // the settings of `g3cli remesh` (tools/g3cli/Commands.cs): 20 passes, smoothing 0.5, projection onto the input,
    // boundary and 30 degree creases kept; MinTriangleQuality at the library default
    static DMesh3 Remesh(DMesh3 input, double targetEdge, bool si, bool parallel)
    {
        var m = new DMesh3(input);
        var r = new Remesher(m) { SmoothSpeedT = 0.5, MinTriangleQuality = RemeshQuality, PreventSelfIntersections = si, EnableParallelSmooth = parallel, EnableParallelProjection = parallel, ENABLE_PROFILING = Profile };
        r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
        if (RemeshCrease > 0) MeshConstraintUtil.PreserveBoundaryAndFeatures(r, RemeshCrease); else MeshConstraintUtil.PreserveBoundaryLoops(r);
        r.SetTargetEdgeLength(targetEdge);
        for (int k = 0; k < 20; ++k)
            r.BasicRemeshPass();
        return m;
    }

    static Run Measure(Case c, string op, string variant, string label, double target, Operation f, int runs, bool fidelity, bool invariance)
    {
        bool si = variant == "si";
        var run = new Run() { Op = op, Variant = variant, Group = c.Group, Case = c.Name, TargetLabel = label };
        var F = run.Fields;
        var times = new List<double>();
        var hashes = new List<string>();
        DMesh3 result = null;
        string error = null;
        try {
            for (int k = 0; k < runs; ++k) {
                GC.Collect(); GC.WaitForPendingFinalizers();
                var sw = Stopwatch.StartNew();
                result = f(c.Input, target, si, true);
                times.Add(sw.Elapsed.TotalSeconds);
                hashes.Add(MeshHash.Of(result));
            }
        } catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
        F["in_tris"] = c.Input.TriangleCount;
        F["closed_in"] = c.Closed;
        if (error != null) { F["error"] = error; return run; }

        F["out_tris"] = result.TriangleCount;
        F["seconds"] = Stats.Median(times);
        F["us_per_in_tri"] = 1e6 * Stats.Median(times) / c.Input.TriangleCount;
        F["target_value"] = target;
        F["hash"] = hashes[^1];
        // determinism: all repeated runs equal; the Remesher also with its parallel smoothing / projection off
        F["repeat_same"] = hashes.Distinct().Count() == 1;
        if (op == "remesh")
            F["serial_same"] = MeshHash.Of(f(c.Input, target, si, false)) == hashes[^1];

        // quality
        AngleSummary q = AngleSummary.Compute(result);
        F["min_angle"] = q.MinAngle; F["p01"] = q.MinAngleP01; F["below05"] = q.Below05; F["above170"] = q.Above170;
        F["in_p01"] = c.Angles.MinAngleP01; F["in_below05"] = c.Angles.Below05; F["in_above170"] = c.Angles.Above170;

        // fidelity: certified one-sided Hausdorff upper bounds relative to the input's box diagonal
        foreach (bool forward in new[] { true, false }) {
            var hd = new MeshHausdorffDistance(forward ? c.Input : result, forward ? result : c.Input) { Symmetric = false, Tolerance = HdTolerance * c.Diagonal, MaxSubdivisions = 2000000 };
            hd.Compute();
            F[forward ? "hd_in_to_out" : "hd_out_to_in"] = hd.UpperBound / c.Diagonal;
            F[forward ? "hd_in_to_out_conv" : "hd_out_to_in_conv"] = hd.Converged;
        }
        if (fidelity) {
            bool volume = c.Closed && result.IsClosed();
            var fm = new MeshFidelityMetrics(c.Input, result) { SurfaceSamples = 20000, VolumeSamples = volume ? 50000 : 0, Curvature = false, OuterSurface = false, RelativeThresholds = new[] { 0.001, 0.005 } };
            if (fm.Compute()) {
                F["f_0001"] = fm.FScore[0]; F["f_0005"] = fm.FScore[1];
                F["nc"] = fm.NormalConsistency; F["chamfer_rel"] = fm.Chamfer / fm.Size;
                F["viou"] = volume ? fm.VolumeIoU : double.NaN;
                F["topo_changes"] = fm.TopologyChanges.ToString();
                F["genus_in"] = fm.ReferenceTopology.Genus; F["genus_out"] = fm.MeshTopology.Genus;
                F["bowties_out"] = fm.MeshTopology.BowtieVertices;
                F["components_in"] = fm.ReferenceTopology.Components; F["components_out"] = fm.MeshTopology.Components;
            }
        }

        // topology / cleanliness: more self-intersecting pairs than the input (a lower bound of the new ones: removing old pairs hides new ones;
        // exact for an input without pairs)
        int pairs = MeshRepairMetrics.SelfIntersections(result);
        F["self_pairs_in"] = c.SelfPairs; F["self_pairs_out"] = pairs; F["new_self_pairs"] = Math.Max(0, pairs - c.SelfPairs);
        F["not_clean"] = MeshRobustRepair.NotCleanReasons(result, false);
        F["closed_out"] = result.IsClosed();

        if (invariance) {
            // the similarities of Invariance.cs: a power-of-two scaling must give the same mesh bit for bit (mapped back);
            // the others only up to rounding, so their triangle counts are compared
            int bitFail = 0, bitTotal = 0; double maxTriDev = 0; var failed = new List<string>();
            foreach (Xform x in Xform.All()) {
                if (x.Name == "identity") continue;
                DMesh3 xr = x.MapBack(f(x.Transform(c.Input), op == "remesh" ? x.Scale * target : target, si, true));
                if (x.IsPowerOfTwoScaling) {
                    ++bitTotal;
                    if (MeshHash.Of(xr) != hashes[^1]) { ++bitFail; failed.Add(x.Name); }
                } else
                    maxTriDev = Math.Max(maxTriDev, Math.Abs(xr.TriangleCount - result.TriangleCount) / (double)Math.Max(1, result.TriangleCount));
            }
            F["inv_pow2_total"] = bitTotal; F["inv_pow2_not_identical"] = bitFail; F["inv_other_tri_dev_max"] = maxTriDev;
            F["inv_failed"] = string.Join(";", failed);
        }
        return run;
    }

    // ---- summary ----

    // the rows of an earlier run's JSON lines (numbers as double, which D() and I() accept)
    static List<Run> LoadRows(string file)
    {
        var rows = new List<Run>();
        foreach (string line in File.ReadLines(file)) {
            if (line.Trim().Length == 0) continue;
            using (JsonDocument doc = JsonDocument.Parse(line)) {
                var r = new Run();
                foreach (JsonProperty p in doc.RootElement.EnumerateObject()) {
                    switch (p.Name) {
                        case "op": r.Op = p.Value.GetString(); break;
                        case "variant": r.Variant = p.Value.GetString(); break;
                        case "target": r.TargetLabel = p.Value.GetString(); break;
                        case "group": r.Group = p.Value.GetString(); break;
                        case "case": r.Case = p.Value.GetString(); break;
                        default:
                            if (p.Value.ValueKind == JsonValueKind.Number) r.Fields[p.Name] = p.Value.GetDouble();
                            else if (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False) r.Fields[p.Name] = p.Value.GetBoolean();
                            else if (p.Value.ValueKind == JsonValueKind.String) {
                                string t = p.Value.GetString();
                                r.Fields[p.Name] = t == "NaN" ? double.NaN : t == "Infinity" ? double.PositiveInfinity : t == "-Infinity" ? double.NegativeInfinity : (object)t;
                            }
                            break;
                    }
                }
                rows.Add(r);
            }
        }
        return rows;
    }

    static void Summary(List<Run> all, List<Case> cases)
    {
        Console.WriteLine();
        if (cases != null) Console.WriteLine($"Inputs: {string.Join(", ", cases.Select(c => $"{c.Name} ({c.Input.TriangleCount} tris{(c.Closed ? "" : ", open")}, genus {c.Topology.Genus:F0}, {c.SelfPairs} self-int. pairs)"))}");
        string pct(IEnumerable<double> v) { var l = v.Where(x => double.IsFinite(x)).ToList(); return l.Count == 0 ? "-" : $"{Stats.Median(l):G3} ({Stats.Percentile(l, 0.1):G3}-{Stats.Percentile(l, 0.9):G3})"; }
        string max(IEnumerable<double> v) { var l = v.Where(x => double.IsFinite(x)).ToList(); return l.Count == 0 ? "-" : l.Max().ToString("G3", CultureInfo.InvariantCulture); }
        foreach (var g in all.Where(r => r.Fields.ContainsKey("error") == false).GroupBy(r => (r.Op, r.Variant, r.TargetLabel, r.Group)).OrderBy(g => g.Key.Op).ThenBy(g => g.Key.TargetLabel).ThenBy(g => g.Key.Variant).ThenBy(g => g.Key.Group)) {
            var l = g.ToList();
            Console.WriteLine();
            Console.WriteLine($"### {g.Key.Op} {g.Key.TargetLabel} {g.Key.Variant}, {g.Key.Group} ({l.Count} cases)");
            Console.WriteLine();
            Console.WriteLine("| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            Console.WriteLine($"| {pct(l.Select(r => r.D("seconds")))} | {pct(l.Select(r => r.D("us_per_in_tri")))} | {pct(l.Select(r => r.D("out_tris") / (r.Op == "reduce" ? r.D("target_value") : r.D("in_tris"))))} | " +
                $"{pct(l.Select(r => r.D("p01")))} ({pct(l.Select(r => r.D("in_p01")))}) | {pct(l.Select(r => r.D("min_angle")))} | " +
                $"{l.Sum(r => r.I("below05"))} / {l.Sum(r => r.I("above170"))} ({l.Count(r => r.I("below05") + r.I("above170") > 0)}) | " +
                $"{pct(l.Select(r => r.D("hd_in_to_out")))} ({max(l.Select(r => r.D("hd_in_to_out")))}) | {pct(l.Select(r => r.D("hd_out_to_in")))} ({max(l.Select(r => r.D("hd_out_to_in")))}) | " +
                $"{pct(l.Select(r => r.D("f_0001")))} | {pct(l.Select(r => r.D("f_0005")))} | {pct(l.Select(r => r.D("viou")))} | " +
                $"{l.Count(r => r.Fields.TryGetValue("topo_changes", out object t) && (string)t != "None")} | {l.Count(r => r.I("new_self_pairs") > 0)} | " +
                $"{l.Count(r => r.Fields.TryGetValue("not_clean", out object t) && (string)t != "")} | " +
                $"{l.Count(r => r.B("repeat_same") == false)} / {l.Count(r => r.Op == "remesh" && r.B("serial_same") == false)} | " +
                $"{l.Sum(r => r.I("inv_pow2_not_identical"))} of {l.Sum(r => r.I("inv_pow2_total"))} ({l.Count(r => r.I("inv_pow2_not_identical") > 0)}) |");
            int notConv = l.Count(r => r.Fields.TryGetValue("hd_in_to_out_conv", out object a) && r.Fields.TryGetValue("hd_out_to_in_conv", out object b) && (!(bool)a || !(bool)b));
            var reasons = l.Select(r => r.Fields.TryGetValue("not_clean", out object t) ? (string)t : "").Where(t => t != "").GroupBy(t => t).Select(t => $"{t.Key} x{t.Count()}").ToList();
            if (notConv > 0 || reasons.Count > 0)
                Console.WriteLine($"Hausdorff bound not converged (2M subdivisions): {notConv} cases. Not clean: {(reasons.Count > 0 ? string.Join("; ", reasons) : "none")}.");
        }
        var errs = all.Where(r => r.Fields.ContainsKey("error")).ToList();
        if (errs.Count > 0) {
            Console.WriteLine();
            Console.WriteLine("Errors:");
            foreach (var r in errs) Console.WriteLine($"- {r.Op} {r.TargetLabel} {r.Variant} {r.Case}: {r.Fields["error"]}");
        }
    }
}
