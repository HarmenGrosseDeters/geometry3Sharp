// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Measurement study (no library change): (A) does a curvature-aware size rule L = min(target, c sqrt(r t)) keep thin walls, and how
// much surface has a unique mutual opposite side; (B) offline repair of the pinned crease slivers of REMESHER_CREASE_SLIVERS.md.
//
//   dotnet run -c Release --project benchmarks/RemeshThinWallSliverStudy -- mode=thin   [only=<regex>]
//   dotnet run -c Release --project benchmarks/RemeshThinWallSliverStudy -- mode=big    [thingi=<dir>] [id=135363] [run=1] [c=1,2]
//   dotnet run -c Release --project benchmarks/RemeshThinWallSliverStudy -- mode=slivers [thingi=<dir>] [only=<regex>]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using gs;

static partial class Program
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        string mode = A.ContainsKey("mode") ? A["mode"] : "thin";
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        switch (mode) {
            case "thin": return Thin(only, A);
            case "big": return Big(A);
            case "slivers": return Slivers(A, only);
        }
        Console.Error.WriteLine("unknown mode");
        return 1;
    }

    // ---- the remesh of g3cli with the options of the study ----
    // q = 0.2 always (g3cli's default); si = PreventSelfIntersections; field = TargetEdgeLengthF
    public static DMesh3 Remesh(DMesh3 input, double target, bool si, Func<Vector3d, double> field)
    {
        DMesh3 mesh = new DMesh3(input);
        var r = new Remesher(mesh);
        r.SmoothSpeedT = 0.5; r.MinTriangleQuality = 0.2;
        r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
        MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
        if (field != null) r.TargetEdgeLengthF = field;
        r.SetTargetEdgeLength(target);
        for (int k = 0; k < 20; ++k) {
            r.PreventSelfIntersections = si;
            r.BasicRemeshPass();
        }
        return mesh;
    }

    class Metrics
    {
        public int Tris; public double Seconds, VolRatio = double.NaN, HausTarget = double.NaN, NewPairs, ThickP5, ThickP50, ThickP95, ThickInBand, Poor, Flat;
        public string NotClean;
    }

    static Metrics Evaluate(DMesh3 input, DMesh3 m, double secs, double target, double t, bool thickness = true, bool hausdorff = true)
    {
        var r = new Metrics() { Tris = m.TriangleCount, Seconds = secs };
        double v0 = Shells.Volume(input);
        if (input.IsClosed() && v0 != 0) r.VolRatio = Shells.Volume(m) / v0;
        r.NewPairs = MeshRepairMetrics.SelfIntersections(m) - MeshRepairMetrics.SelfIntersections(input);
        if (hausdorff) {
            var hd = new MeshHausdorffDistance(input, m) { Tolerance = 0.01 * target };
            hd.Compute();
            r.HausTarget = hd.UpperBound / target;
        }
        foreach (int tid in m.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(tid, ref a, ref b, ref c);
            double q = MeshRefinerBase.TriangleQuality(ref a, ref b, ref c);
            if (q < 0.1) r.Poor++;
            if (q < 1e-3) r.Flat++;
        }
        r.NotClean = MeshRobustRepair.NotCleanReasons(m, true);
        r.ThickP5 = r.ThickP50 = r.ThickP95 = r.ThickInBand = double.NaN;
        if (thickness && t > 0) {
            var wt = new MeshWallThickness(m) { Method = MeshWallThickness.Methods.InscribedSphere, ComputeVertices = false };
            if (wt.Compute()) {
                var list = new List<(double ratio, double area)>();
                foreach (int tid in m.TriangleIndices()) {
                    double x = wt.TriangleThickness[tid];
                    if (!(x > 0) || double.IsInfinity(x)) continue;
                    list.Add((x / t, m.GetTriArea(tid)));
                }
                list.Sort((p, q) => p.ratio.CompareTo(q.ratio));
                double tot = list.Sum(p => p.area), acc = 0;
                double P(double f) { double a2 = 0; foreach (var p in list) { a2 += p.area; if (a2 >= f * tot) return p.ratio; } return double.NaN; }
                r.ThickP5 = P(0.05); r.ThickP50 = P(0.5); r.ThickP95 = P(0.95);
                foreach (var p in list) if (p.ratio >= 0.75 && p.ratio <= 1.33) acc += p.area;
                r.ThickInBand = acc / tot;
            }
        }
        return r;
    }

    static string F(double x, string f = "F2") => double.IsNaN(x) ? "-" : x.ToString(f, IC);

    static int Thin(Regex only, Dictionary<string, string> A)
    {
        // extra=1: thinner walls (r / t = 100, 400), no FromWallThickness / si run (too many triangles / not needed), k = 10, 20, 30
        bool extra = A.ContainsKey("extra");
        var cs = (A.ContainsKey("c") ? A["c"] : "1,2").Split(',').Select(x => double.Parse(x, IC)).ToArray();
        double[] wtfs = A.ContainsKey("wtf") ? A["wtf"].Split(',').Select(x => double.Parse(x, IC)).ToArray() : new double[0];
        bool noBase = A.ContainsKey("nobase");       // only the rule and wtf configs
        var bodies = new List<Shells.Body>();
        if (!extra) {
            foreach (double t in new[] { 0.05, 0.1 }) bodies.Add(Shells.Plate(3.0, t, 4));
            foreach (double t in new[] { 0.05, 0.1 }) bodies.Add(Shells.Tube(1.0, 3.0, t, 48, 4));
            foreach (double t in new[] { 0.025, 0.05 }) bodies.Add(Shells.Bend(0.75, 0.5, t, 32, 12));
        } else {
            foreach (double t in new[] { 0.01, 0.0025 }) bodies.Add(Shells.Tube(1.0, 3.0, t, 48, 4));
            bodies.Add(Shells.Bend(0.75, 0.5, 0.005, 32, 12));
        }
        Console.WriteLine("| body | t | in tris | k=target/t | config | tris | pred. tris | vol ratio | haus/target | new pairs | t_res/t p5 / p50 / p95 | area in 0.75..1.33 t | q<0.1 | q<1e-3 | clean | s |");
        Console.WriteLine("|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---|---:|---:|---:|---|---:|");
        // warm-up
        Remesh(bodies[0].Mesh, 1.0, false, null);
        foreach (var b in bodies) {
            if (only != null && !only.IsMatch(b.Name)) continue;
            if (!b.Mesh.IsClosed() || Shells.Volume(b.Mesh) <= 0) { Console.WriteLine($"# {b.Name}: input not closed / positive"); continue; }
            int pin = MeshRepairMetrics.SelfIntersections(b.Mesh);
            var probe = new Fields.Probe(b.Mesh, b.R, 0);
            var wtField = extra ? null : RemeshSizing.FromWallThickness(b.Mesh, 1.0);
            foreach (double k in extra ? new[] { 10.0, 20, 30 } : new[] { 5.0, 10, 20, 30 }) {
                double target = k * b.T;
                var configs = new List<(string name, bool si, Func<Vector3d, double> f)>();
                if (!noBase) configs.Add(("default", false, null));
                if (!extra && !noBase) { configs.Add(("si", true, null)); configs.Add(("FromWallThickness(1)", false, wtField)); }
                foreach (double c in cs) configs.Add(($"rule c={c.ToString(IC)}", false, probe.Rule(c, target)));
                foreach (double f in wtfs) configs.Add(($"FromWallThickness({f.ToString(IC)})", false, RemeshSizing.FromWallThickness(b.Mesh, f)));
                foreach (var (name, si, f) in configs) {
                    var sw = Stopwatch.StartNew();
                    DMesh3 m = Remesh(b.Mesh, target, si, f);
                    double secs = sw.Elapsed.TotalSeconds;
                    var r = Evaluate(b.Mesh, m, secs, target, b.T);
                    double pred = Fields.Predict(b.Mesh, f, target);
                    Console.WriteLine($"| {b.Name.Split('_')[0]} | {b.T.ToString(IC)} | {b.Mesh.TriangleCount} | {k} | {name} | {r.Tris} | {pred:F0} | {F(r.VolRatio, "F3")} | {F(r.HausTarget)} | {r.NewPairs} | " +
                        $"{F(r.ThickP5)} / {F(r.ThickP50)} / {F(r.ThickP95)} | {F(r.ThickInBand)} | {r.Poor} | {r.Flat} | {(r.NotClean == "" ? "yes" : "no")} | {r.Seconds:F1} |");
                }
            }
            Console.Error.WriteLine($"# done {b.Name} (input pairs {pin})");
        }
        return 0;
    }
}
