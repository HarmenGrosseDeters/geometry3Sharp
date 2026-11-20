// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Reducer.FastCollapseEndpointFallback: the variants of the endpoint retry of FastCollapsePass(0.05 * diagonal, 5) with
// MaxDeviation and MaxDeviationTwoSided (off = today's two-sided pass), at 0.003 and 0.01 of the bounding-box diagonal:
//   feature end only / both ends, retry in every round / in the first round only. Rounds alternate the variants; times are
//   medians. The Hausdorff distances are certified upper bounds of MeshHausdorffDistance (tolerance 0.01 eps), in eps.
//   `fallback pit`: the same with PreventSelfIntersections (a subset of the cases).
//
//   dotnet run -c Release --project benchmarks/ReducerBenchmark -- fallback [rounds] [mesh files...]
//   (environment variable FB_ONLY=text: only the cases whose name contains the text; FB_PIT=1: PreventSelfIntersections table)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static class EndpointFallback
{
    static (double fwd, double back) Bounds(DMesh3 input, DMesh3 result, double eps)
    {
        var f = new MeshHausdorffDistance(input, result) { Symmetric = false, Tolerance = 0.01 * eps }; f.Compute();
        var b = new MeshHausdorffDistance(result, input) { Symmetric = false, Tolerance = 0.01 * eps }; b.Compute();
        return (f.UpperBound / eps, b.UpperBound / eps);
    }

    // off, feature end only, first round only (both ends), feature end only and first round only, both ends every round
    static readonly (string name, bool on, bool featureOnly, bool firstRound)[] Variants = {
        ("off", false, false, false), ("feature end", true, true, false), ("both ends, round 1", true, false, true),
        ("feature end, round 1", true, true, true), ("both ends", true, false, false) };

    static Reducer Make(DMesh3 m, double eps, int v, bool pit)
    {
        var d = Variants[v];
        return new Reducer(m) { MaxDeviation = eps, MaxDeviationTwoSided = true, PreventSelfIntersections = pit,
            FastCollapseEndpointFallback = d.on, FastCollapseFallbackBothEnds = !d.featureOnly, FastCollapseFallbackFirstRoundOnly = d.firstRound };
    }

    internal static void Run(Func<int, DMesh3> bumpy, Func<int, DMesh3> terrain, string[] args)
    {
        int rounds = args.Length > 1 && int.TryParse(args[1], out int r) ? r : 3;
        bool pit = Environment.GetEnvironmentVariable("FB_PIT") == "1";
        string only = Environment.GetEnvironmentVariable("FB_ONLY");
        var cases = new List<(string, DMesh3)> { ("bumpy sphere 40", bumpy(40)), ("spiky sphere 60", TwoSided.SpikySphere(60)), ("terrain 80", terrain(80)) };
        foreach (string f in args.Skip(1).Where(x => !int.TryParse(x, out int _))) {
            DMesh3 m = StandardMeshReader.ReadMesh(f);
            if (m != null && m.TriangleCount > 0) cases.Add((System.IO.Path.GetFileNameWithoutExtension(f), m));
        }
        if (only != null) cases = cases.Where(c => only.Split(',').Any(o => c.Item1.Contains(o))).ToList();
        Console.WriteLine($"rounds: {rounds}, processors: {Environment.ProcessorCount}, PreventSelfIntersections: {pit}");
        { var wm = bumpy(20); double e = 0.003 * wm.CachedBounds.DiagonalLength; for (int v = 0; v < Variants.Length; ++v) Make(new DMesh3(wm), e, v, pit).FastCollapsePass(0.05 * wm.CachedBounds.DiagonalLength, 5); }

        Console.WriteLine("| mesh | triangles | eps / diag | " + string.Join(" | ", Variants.Select(v => v.name + ": triangles, ms")) + " | h(M,S) / h(S,M) / eps, both ends |");
        Console.WriteLine("|---|---|---|" + string.Concat(Variants.Select(_ => "---|")) + "---|");
        var saved = new double[Variants.Length]; var extra = new double[Variants.Length];
        var ratios = new List<double>[Variants.Length]; for (int v = 0; v < ratios.Length; ++v) ratios[v] = new List<double>();
        foreach (var (name, input) in cases)
            foreach (double rel in new[] { 3e-3, 1e-2 }) {
                double diag = input.CachedBounds.DiagonalLength, eps = rel * diag, len = 0.05 * diag;
                var times = Variants.Select(_ => new List<double>()).ToArray(); var res = new DMesh3[Variants.Length];
                for (int k = 0; k < rounds; ++k)
                    for (int v = 0; v < Variants.Length; ++v) {
                        res[v] = new DMesh3(input);
                        Reducer red = Make(res[v], eps, v, pit);
                        var sw = Stopwatch.StartNew();
                        red.FastCollapsePass(len, 5);
                        times[v].Add(sw.Elapsed.TotalMilliseconds);
                    }
                var ms = times.Select(t => { t.Sort(); return t[t.Count / 2]; }).ToArray();
                for (int v = 1; v < Variants.Length; ++v) {
                    saved[v] += res[0].TriangleCount - res[v].TriangleCount; extra[v] += ms[v] - ms[0];
                    ratios[v].Add(ms[v] / ms[0]);
                }
                var (fb, bb) = Bounds(input, res[Variants.Length - 1], eps);
                Console.WriteLine($"| {name} | {input.TriangleCount} | {rel:0.###} | " + string.Join(" | ", Enumerable.Range(0, Variants.Length).Select(v => $"{res[v].TriangleCount}, {ms[v]:F0}")) + $" | {fb:F3} / {bb:F3} |");
            }
        Console.WriteLine();
        Console.WriteLine("| variant | triangles removed against off (sum over the rows) | extra time (sum, s) | triangles per extra second | share of the both-ends reduction | time against off (median of the rows) |");
        Console.WriteLine("|---|---|---|---|---|---|");
        for (int v = 1; v < Variants.Length; ++v) {
            ratios[v].Sort();
            Console.WriteLine($"| {Variants[v].name} | {saved[v]:F0} | {extra[v] / 1000:F1} | {saved[v] / (extra[v] / 1000):F0} | {100 * saved[v] / saved[Variants.Length - 1]:F0} % | {ratios[v][ratios[v].Count / 2]:F2}x |");
        }
    }
}
