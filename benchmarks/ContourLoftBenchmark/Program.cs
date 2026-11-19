// Scaling of ContourLoft (docs/design/CONTOUR_LOFT.md section 6): the search is O(m n log n) steps and O(m n) triangle
// evaluations; with the argument stack, ContourLoftStack (section 10): slices with one and two contours alternating every 8 slices.
// Run: dotnet run --project benchmarks/ContourLoftBenchmark -c Release [-- quick | stack]
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

    static List<Vector3d> Ellipse(int n, double cx, double rx, double ry, double z)
    {
        var c = new List<Vector3d>(n);
        for (int i = 0; i < n; ++i) {
            double a = 2 * Math.PI * i / n;
            c.Add(new Vector3d(cx + rx * Math.Cos(a), ry * Math.Sin(a), z));
        }
        return c;
    }

    // a stack of count slices: one contour (n vertices) for 8 slices, then two (n vertices each), and so on; the pieces are
    // inside the single contour (contained) or stick out of it (cut: an intermediate plane per switch)
    static IList<IList<IList<Vector3d>>> Stack(int count, int n, bool cut)
    {
        var slices = new List<IList<IList<Vector3d>>>();
        for (int s = 0; s < count; ++s) {
            double z = s, w = 1 + 0.01 * (s % 8);
            var sl = new List<IList<Vector3d>>();
            if ((s / 8) % 2 == 0) sl.Add(Ellipse(n, 0, 4.5 * w, 1.5 * w, z));
            else {
                double c = cut ? 3 : 2.4, rx = cut ? 2.8 : 0.9, ry = cut ? 1.2 : 0.9;
                sl.Add(Ellipse(n, -c, rx * w, ry * w, z));
                sl.Add(Ellipse(n, c, rx * w, ry * w, z));
            }
            slices.Add(sl);
        }
        return slices;
    }

    static void StackBenchmark(bool quick)
    {
        int reps = quick ? 1 : 3;
        Console.WriteLine("| slices | n per contour | pieces | status | vertices | triangles | median ms | ms per slab |");
        Console.WriteLine("|---:|---:|---|---|---:|---:|---:|---:|");
        foreach (int n in quick ? new[] { 100 } : new[] { 100, 400, 1000 })
            foreach (bool cut in new[] { false, true }) {
                var slices = Stack(33, n, cut);
                var loft = new ContourLoftStack { CapEnds = true, CheckSelfIntersections = true };
                loft.Compute(slices);   // warm-up
                var times = new List<double>();
                ContourLoftStackResult r = null;
                for (int k = 0; k < reps; ++k) {
                    var sw = Stopwatch.StartNew();
                    r = loft.Compute(slices);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                double ms = Median(times);
                Console.WriteLine($"| 33 | {n} | {(cut ? "cut" : "contained")} | {r.Status} | {r.Mesh?.VertexCount} | {r.Mesh?.TriangleCount} | {ms:F1} | {ms / 32:F1} |");
            }
    }

    static void Main(string[] args)
    {
        if (args.Contains("stack")) { StackBenchmark(args.Contains("quick")); return; }
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
