// Upper bound for the exact imprint (docs/design/EXACT_IMPRINT.md section 6): the phases of ExactMeshBoolean on procedural
// closed pairs (two offset spheres, a sphere and a cube-like box), 1 run per size after a warm-up. The imprint skips the
// classification, the simplification and the cutter's triangulation, so "bound" = (total - classification) is its upper limit.
// Usage: dotnet run -c Release --project benchmarks/ExactImprint -- bound [n1 n2 ...]
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
        if (args.Length == 0 || args[0] != "bound") { Console.WriteLine("usage: bound [n ...]"); return 1; }
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
}
