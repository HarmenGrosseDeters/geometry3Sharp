// Upper bound for the exact imprint (docs/design/EXACT_IMPRINT.md section 6): the phases of ExactMeshBoolean on procedural
// closed pairs (two offset spheres, a sphere and a cube-like box), 1 run per size after a warm-up. The imprint skips the
// classification, the simplification and the cutter's triangulation, so "bound" = (total - classification) is its upper limit.
// Usage: dotnet run -c Release --project benchmarks/ExactImprint -- bound [n1 n2 ...]
//        dotnet run -c Release --project benchmarks/ExactImprint -- compare [runs] [n1 n2 ...]   (imprint against the boolean, medians)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

static class Program
{
    static DMesh3 Sphere(Vector3d c, double r, int n)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
        MeshTransforms.Translate(m, c);
        return m;
    }

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "compare")
            return Compare(args.Skip(1).ToArray());
        if (args.Length == 0 || args[0] != "bound") { Console.WriteLine("usage: bound [n ...] | compare [runs] [n ...]"); return 1; }
        int[] sizes = args.Length > 1 ? args.Skip(1).Select(int.Parse).ToArray() : new[] { 16, 32, 64 };
        Console.WriteLine("n tris | total ms | setup..merge | triangulation | classification | simplify | rounding | other");
        foreach (int n in sizes.Prepend(sizes[0])) {
            DMesh3 a = Sphere(new Vector3d(0, 0, 0), 1, n), b = Sphere(new Vector3d(0.7, 0.3, 0.2), 0.9, n);
            var bool_ = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            var sw = Stopwatch.StartNew();
            bool ok = bool_.Compute();
            sw.Stop();
            var p = bool_.PhaseMilliseconds;
            long G(string k) { return p.TryGetValue(k, out long v) ? v : 0; }
            long pre = G("setup") + G("intersections") + G("merge");
            Console.WriteLine("{0} | {1} | {2} | {3} | {4} | {5} | {6} | {7}  (ok={8}, keys: {9})", a.TriangleCount + b.TriangleCount, sw.ElapsedMilliseconds,
                pre, G("triangulation"), G("classification"), G("simplify"), G("rounding"),
                sw.ElapsedMilliseconds - pre - G("triangulation") - G("classification") - G("simplify") - G("rounding"), ok, string.Join(",", p.Keys));
        }
        return 0;
    }

    // imprint of a sphere by an offset sphere (the curve touches a part of the target), and of a small sphere (a small curve), against the
    // boolean Union of the same pair: wall milliseconds, median of the runs, and the imprint's phases
    static int Compare(string[] args)
    {
        int runs = args.Length > 0 ? int.Parse(args[0]) : 5;
        int[] sizes = args.Length > 1 ? args.Skip(1).Select(int.Parse).ToArray() : new[] { 32, 64, 128 };
        Console.WriteLine("case | tris | boolean ms | imprint ms | imprint / boolean | imprint no-rounding ms | pieces | curve edges | rounding edits");
        foreach (var cutter in new[] { ("offset sphere", new Vector3d(0.7, 0.3, 0.2), 0.9), ("small sphere", new Vector3d(0.9, 0.2, 0.1), 0.25) }) {
            foreach (int n in sizes) {
                DMesh3 a = Sphere(Vector3d.Zero, 1, n), b = Sphere(cutter.Item2, cutter.Item3, n);
                var tb = new List<double>(); var ti = new List<double>(); var tn = new List<double>();
                ExactMeshImprint last = null;
                for (int r = 0; r < runs + 1; ++r) {
                    var sw = Stopwatch.StartNew();
                    var bo = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
                    bo.Compute();
                    double ms1 = sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    var im = new ExactMeshImprint() { Target = a, Cutter = b };
                    im.Compute();
                    double ms2 = sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    new ExactMeshImprint() { Target = a, Cutter = b, SafeRounding = false }.Compute();
                    double ms3 = sw.Elapsed.TotalMilliseconds;
                    last = im;
                    if (r == 0) continue;     // warm-up
                    tb.Add(ms1); ti.Add(ms2); tn.Add(ms3);
                }
                double Med(List<double> l) { l.Sort(); return l[l.Count / 2]; }
                Console.WriteLine("{0} | {1} | {2:F0} | {3:F0} | {4:F2} | {5:F0} | {6} | {7} | {8}", cutter.Item1, a.TriangleCount + b.TriangleCount, Med(tb), Med(ti), Med(ti) / Med(tb), Med(tn),
                    last.Counts["pieces"], last.Counts["curve_edges"], last.Counts.ContainsKey("rounding_edits") ? last.Counts["rounding_edits"] : 0);
            }
        }
        return 0;
    }
}
