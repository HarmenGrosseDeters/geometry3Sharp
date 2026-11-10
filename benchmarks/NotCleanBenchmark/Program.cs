using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

// Time of the checks behind MeshRobustRepair.NotCleanReason on big procedural meshes, per check and in total, with the
// verdict and a hash of the self-intersection pair set (the pairs must not change between builds).
// Usage: NotCleanBenchmark [repeats]
static class Program
{
    static DMesh3 ThinPlates(int seed)
    {
        var rnd = new Random(seed);
        DMesh3 m = new DMesh3();
        for (int i = 0; i < 12; ++i) {
            var c = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
            var e = new Vector3d(0.3 + 0.2 * rnd.NextDouble(), 0.02 + 0.03 * rnd.NextDouble(), 0.3);
            var b = new TrivialBox3Generator() { Box = new Box3d(c, e) }.Generate().MakeDMesh();
            var axis = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
            MeshTransforms.Rotate(b, c, Quaterniond.AxisAngleD(axis, 360 * rnd.NextDouble()));
            MeshEditor.Append(m, b);
        }
        return m;
    }

    static string PairHash(List<Index2i> pairs)
    {
        var ms = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(ms);
        w.Write(pairs.Count);
        foreach (var p in pairs) { w.Write(p.a); w.Write(p.b); }
        w.Flush();
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", "").Substring(0, 12);
    }

    static long Median(Func<object> f, int repeats)
    {
        var t = new List<long>();
        for (int i = 0; i < repeats; ++i) { var sw = Stopwatch.StartNew(); f(); t.Add(sw.ElapsedMilliseconds); }
        t.Sort();
        return t[t.Count / 2];
    }

    static int Main(string[] args)
    {
        int repeats = args.Length > 0 ? int.Parse(args[0]) : 5;
        var wrap = new MeshAlphaWrap(ThinPlates(6)) { RelativeAlpha = 300, RelativeOffset = 3000 };
        wrap.Compute();
        // a copy of the wrap with a second, shifted wrap merged in: many real self-intersections
        DMesh3 crossing = new DMesh3(wrap.Result);
        DMesh3 shifted = new DMesh3(wrap.Result);
        MeshTransforms.Translate(shifted, new Vector3d(0.013, 0.007, 0.011));
        MeshEditor.Append(crossing, shifted);
        var cases = new List<KeyValuePair<string, DMesh3>> {
            new("sphere, 200k triangles (clean)", new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 130 }.Generate().MakeDMesh()),
            new("alpha wrap of thin plates, 185k triangles (clean)", wrap.Result),
            new("two shifted copies of that wrap (self-intersecting)", crossing),
        };
        Console.WriteLine("| mesh | verdict | pairs (hash) | closed+bowtie+degenerate ms | inverted ms | self-intersection search ms (all pairs) | HasSelfIntersections ms | NotCleanReason ms |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (var c in cases) {
            DMesh3 m = c.Value;
            string verdict = MeshRobustRepair.NotCleanReason(m);
            var pairs = MeshRepairMetrics.SelfIntersectionPairs(m);
            long basic = Median(() => MeshRobustRepair.NotCleanReason(m, false), repeats);
            long inv = Median(() => MeshPostRepairCleanup.FindInvertedComponents(m).Count, repeats);
            long all = Median(() => MeshRepairMetrics.SelfIntersectionPairs(m), repeats);
            long has = Median(() => MeshRepairMetrics.HasSelfIntersections(m), repeats);
            long total = Median(() => MeshRobustRepair.NotCleanReason(m), repeats);
            Console.WriteLine($"| {c.Key} | \"{verdict}\" | {pairs.Count} (`{PairHash(pairs)}`) | {basic} | {inv} | {all} | {has} | {total} |");
        }
        return 0;
    }
}
