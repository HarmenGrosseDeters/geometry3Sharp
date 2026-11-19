// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using g3;
using gs;

static partial class Program
{
    static int Big(Dictionary<string, string> A)
    {
        string dir = A.ContainsKey("thingi") ? A["thingi"] : "benchmarks/RemeshBenchmark/data/meshes";
        string id = A.ContainsKey("id") ? A["id"] : "135363";
        DMesh3 input = StandardMeshReader.ReadMesh(Path.Combine(dir, id + ".stl"));
        double target = A.ContainsKey("target") ? double.Parse(A["target"], IC) : 11.6;
        Console.WriteLine($"# {id}: {input.TriangleCount} tris, volume {Shells.Volume(input):F1}, closed {input.IsClosed()}, bbox diag {input.CachedBounds.DiagonalLength:F2}, target {target}, input pairs {MeshRepairMetrics.SelfIntersections(input)}");

        // thickness and the feasibility numbers
        var wt = new MeshWallThickness(input) { Method = MeshWallThickness.Methods.InscribedSphere, ComputeVertices = false };
        wt.Compute();
        var tris = input.TriangleIndices().ToArray();
        double total = tris.Sum(t => input.GetTriArea(t));
        var ts = tris.Where(t => wt.TriangleThickness[t] > 0 && !double.IsInfinity(wt.TriangleThickness[t])).Select(t => (t: wt.TriangleThickness[t], a: input.GetTriArea(t))).OrderBy(p => p.t).ToList();
        double acc = 0; var q = new List<string>();
        foreach (double f in new[] { 0.05, 0.25, 0.5, 0.75, 0.95 }) { double a2 = 0; foreach (var p in ts) { a2 += p.a; if (a2 >= f * total) { q.Add($"{f}: {p.t:F3}"); break; } } }
        Console.WriteLine($"# area {total:F1}; thickness area quantiles {string.Join(", ", q)}");

        var rows = Pairing.Analyze(input, wt.TriangleThickness, new[] { 1.5, 2.0, 3.0 }, new[] { 0.1, 0.25, 0.5 });
        Console.WriteLine("\n| multiple of t | return tol (x d) | ray hits | + antiparallel (30 deg) | + d < m t | mutual (back within tol) | strict (returns to same triangle) |");
        Console.WriteLine("|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in rows) Console.WriteLine($"| {r.Multiple} | {r.Tol} | {r.Hit:P1} | {r.Anti:P1} | {r.Near:P1} | {r.Mutual:P1} | {r.MutualStrict:P1} |");

        // predicted counts
        var wtField = RemeshSizing.FromWallThickness(input, 1.0);
        double predWt = Fields.Predict(input, wtField, target);
        Console.WriteLine($"\n| field | smoothing rounds | r floor | predicted tris | pred. / (FromWallThickness pred.) |");
        Console.WriteLine("|---|---:|---|---:|---:|");
        Console.WriteLine($"| global target only | - | - | {Fields.Predict(input, null, target):F0} | {Fields.Predict(input, null, target) / predWt:F3} |");
        Console.WriteLine($"| FromWallThickness(1) | - | - | {predWt:F0} | 1 |");
        var cs = (A.ContainsKey("c") ? A["c"] : "1,2").Split(',').Select(x => double.Parse(x, IC)).ToArray();
        foreach (int rounds in new[] { 0, 2, 5 }) {
            var probe = new Fields.Probe(input, null, rounds);
            foreach (double rf in new[] { 1.0, 3.0 })
                foreach (double c in cs) {
                    double p = Fields.Predict(input, probe.Rule(c, target, rf), target);
                    Console.WriteLine($"| rule c={c} | {rounds} | r >= {rf} t | {p:F0} | {p / predWt:F3} |");
                }
        }
        if (A.ContainsKey("run")) {
            int rounds = A.ContainsKey("rounds") ? int.Parse(A["rounds"]) : 2;
            double rf = A.ContainsKey("rf") ? double.Parse(A["rf"], IC) : 1.0;
            foreach (double c in cs) {
                var probe = new Fields.Probe(input, null, rounds);
                var f = probe.Rule(c, target, rf);
                var sw = Stopwatch.StartNew();
                DMesh3 m = Remesh(input, target, false, f);
                double secs = sw.Elapsed.TotalSeconds;
                var r = Evaluate(input, m, secs, 0.75, false ? 0 : 0, thickness: false, hausdorff: A.ContainsKey("haus"));
                Console.WriteLine($"\nrun rule c={c}, rounds {rounds}, r >= {rf} t: {r.Tris} tris in {secs:F0} s, volume {r.VolRatio:F4}, new pairs {r.NewPairs}, poor {r.Poor}, flat {r.Flat}, haus/target {F(r.HausTarget, "F3")}, clean: {(r.NotClean == "" ? "yes" : r.NotClean)}");
            }
        }
        if (A.ContainsKey("wtfrun")) {
            foreach (double f in A["wtfrun"].Split(',').Select(x => double.Parse(x, IC))) {
                var field = RemeshSizing.FromWallThickness(input, f);
                double pred = Fields.Predict(input, field, target);
                var sw = Stopwatch.StartNew();
                DMesh3 m = Remesh(input, target, false, field);
                double secs = sw.Elapsed.TotalSeconds;
                var r = Evaluate(input, m, secs, 0.75, 0, thickness: false, hausdorff: true);
                Console.WriteLine($"\nrun FromWallThickness({f}): predicted {pred:F0}, {r.Tris} tris in {secs:F0} s, volume {r.VolRatio:F4}, new pairs {r.NewPairs}, poor {r.Poor}, flat {r.Flat}, haus/target {F(r.HausTarget, "F3")}, clean: {(r.NotClean == "" ? "yes" : r.NotClean)}");
            }
        }
        return 0;
    }
}
