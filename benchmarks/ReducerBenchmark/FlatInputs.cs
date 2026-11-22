// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Issue 1317: Reducer.MaxDeviation on flat inputs. The check with the patch bound (Reducer.DeviationPatchBound, the default)
// against the check as before (false), alternating rounds (and their order), medians; the reduced meshes must be the same, bit for bit (SHA-256).
// Flat cases (a welded cube, a tilted plate with a boundary) and the curved cases of the other modes.
//
//   dotnet run -c Release --project benchmarks/ReducerBenchmark -- flat [rounds] [case filter]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static class FlatInputs
{
    static DMesh3 Cube(int edgeVertices)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        foreach (int vid in m.VertexIndices()) {
            Vector3d p = m.GetVertex(vid);
            m.SetVertex(vid, p / Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))));
        }
        return m;
    }

    // n x n cells over the unit square in the plane z = 0.25 x
    static DMesh3 Plate(int n)
    {
        var m = new DMesh3();
        for (int j = 0; j <= n; ++j)
            for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d((double)i / n, (double)j / n, 0.25 * i / n));
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                int a = (n + 1) * j + i, b = a + n + 1;
                m.AppendTriangle(a, a + 1, b + 1);
                m.AppendTriangle(a, b + 1, b);
            }
        return m;
    }

    internal static void Run(Func<int, DMesh3> bumpy, Func<int, DMesh3> terrain, string[] args)
    {
        int rounds = args.Length > 1 && int.TryParse(args[1], out int r) ? r : 3;
        string filter = args.Length > 2 ? args[2] : "";
        // (name, mesh, tolerances, rounds without the patch bound: the slow flat cases run it once)
        var cases = new List<(string name, DMesh3 mesh, double[] rel, bool once)> {
            ("cube 10", Cube(10), new[] { 1e-3, 1e-2 }, false), ("cube 20", Cube(20), new[] { 1e-3 }, true),
            ("plate 16", Plate(16), new[] { 1e-3, 1e-2 }, false), ("plate 32", Plate(32), new[] { 1e-3 }, true),
            ("bumpy sphere 40", bumpy(40), new[] { 1e-3, 3e-3, 1e-2 }, false), ("terrain 80", terrain(80), new[] { 1e-3, 3e-3, 1e-2 }, false),
            ("bumpy sphere 130", bumpy(130), new[] { 1e-3, 3e-3, 1e-2 }, false), ("terrain 300", terrain(300), new[] { 1e-3, 3e-3, 1e-2 }, false) };
        Console.WriteLine($"rounds: {rounds}, processors: {Environment.ProcessorCount}");
        Console.WriteLine("| mesh | triangles | tolerance | result | before (ms) | with the patch bound (ms) | speedup | meshes |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (var (name, input, rels, once) in cases.Where(c => c.name.Contains(filter)))
            foreach (double rel in rels) {
                double eps = rel * input.CachedBounds.DiagonalLength;
                new Reducer(new DMesh3(input)) { MaxDeviation = eps }.ReduceToTriangleCount(input.TriangleCount / 2);    // warm-up
                var t = new[] { new List<double>(), new List<double>() };
                var res = new DMesh3[2];
                for (int k = 0; k < rounds; ++k)
                    for (int o = 0; o < 2; ++o) {
                        int v = (k % 2 == 0) ? o : 1 - o;       // the order alternates between the rounds
                        if (v == 0 && once && k > 0) continue;
                        GC.Collect();
                        var m = new DMesh3(input);
                        var sw = Stopwatch.StartNew();
                        new Reducer(m) { MaxDeviation = eps, DeviationPatchBound = v == 1 }.ReduceToTriangleCount(4);
                        t[v].Add(sw.Elapsed.TotalMilliseconds);
                        res[v] = m;
                    }
                t[0].Sort(); t[1].Sort();
                double t0 = t[0][t[0].Count / 2], t1 = t[1][t[1].Count / 2];
                bool same = ReducerHashes.Hash(res[0]) == ReducerHashes.Hash(res[1]);
                Console.WriteLine($"| {name} | {input.TriangleCount} | {rel} | {res[1].TriangleCount} | {t0:F0}{(once ? " (1 run)" : "")} | {t1:F0} | {t0 / t1:F1}x | {(same ? "same" : "DIFFERENT")} {ReducerHashes.Hash(res[1])} |");
            }
    }
}
