// Scaling of ContourLoft (docs/design/CONTOUR_LOFT.md section 6): the search is O(m n log n) steps and O(m n) triangle
// evaluations. Run: dotnet run --project benchmarks/ContourLoftBenchmark -c Release [-- quick]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static class Program
{
    // a wavy closed curve of n vertices at height z (radius r, a few lobes), counter-clockwise
    static List<Vector3d> Contour(int n, double r, double z, double phase)
    {
        var c = new List<Vector3d>(n);
        for (int i = 0; i < n; ++i) {
            double a = 2 * Math.PI * i / n + phase;
            double rr = r * (1 + 0.15 * Math.Sin(5 * a) + 0.05 * Math.Cos(11 * a));
            c.Add(new Vector3d(rr * Math.Cos(a), rr * Math.Sin(a), z));
        }
        return c;
    }

    static double Median(List<double> x) { x.Sort(); return x[x.Count / 2]; }

    static void Main(string[] args)
    {
        bool quick = args.Contains("quick");
        int reps = quick ? 1 : 5;
        var sizes = quick ? new[] { (256, 256), (1024, 256) } : new[] { (256, 256), (512, 512), (1024, 1024), (2048, 2048), (4096, 1024), (16384, 256) };
        Console.WriteLine("| m | n | m n | cost | median ms | ns per m n log2 n | status |");
        Console.WriteLine("|---:|---:|---:|---|---:|---:|---|");
        foreach (var (m, n) in sizes) {
            var lower = Contour(m, 10, 0, 0);
            var upper = Contour(n, 8, 1, 0.01);
            var variants = new (string, ContourLoft)[] {
                ("area", new ContourLoft { CheckSelfIntersections = false }),
                ("constant (search only)", new ContourLoft { Cost = ContourLoftCost.Custom, CustomCost = t => 1, CheckSelfIntersections = false }),
                ("area + slope 1:1", new ContourLoft { IsAdmissible = ContourLoft.NotSteeperThan(SlopeSpec.Ratio(1)), CheckSelfIntersections = false }),
                ("area + self-int. check", new ContourLoft()),
            };
            foreach (var (name, loft) in variants) {
                var times = new List<double>();
                ContourLoftResult r = null;
                loft.Compute(lower, upper);   // warm-up
                for (int k = 0; k < reps; ++k) {
                    var sw = Stopwatch.StartNew();
                    r = loft.Compute(lower, upper);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                double ms = Median(times);
                double per = ms * 1e6 / ((double)m * n * Math.Log2(n));
                Console.WriteLine($"| {m} | {n} | {(long)m * n} | {name} | {ms:F1} | {per:F2} | {r.Status} |");
            }
        }
    }
}
