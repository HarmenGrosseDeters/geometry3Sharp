// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The per-collapse loop of the Reducer's MaxDeviation check (Reducer.DeviationParallelMinTriangles): the triangles of a
// collapse (about 10) certified in parallel (gParallel.ForRange, the default), in a loop on the calling thread, or in
// parallel only from a fan size on. The results must be the same, bit for bit. The reductions run on a dedicated thread
// (as a test runner's), optionally while `load` other work items keep the thread pool busy with parallel loops (as the
// other test classes of a full test run do).
//
//   dotnet run -c Release --project benchmarks/ReducerBenchmark -- loop [rounds] [load]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g3;
using gs;

static class DeviationLoop
{
    static readonly (string name, int min)[] Variants = { ("parallel", 0), ("serial", int.MaxValue), ("parallel from 16", 16) };

    public static void Run(Func<int, DMesh3> bumpy, Func<int, DMesh3> terrain, string[] args)
    {
        int rounds = args.Length > 1 ? int.Parse(args[1]) : 3;
        int load = args.Length > 2 ? int.Parse(args[2]) : 0;
        var meshes = new (string, DMesh3)[] { ("bumpy sphere 60", bumpy(60)), ("bumpy sphere 130", bumpy(130)), ("terrain 300", terrain(300)) };
        Console.WriteLine($"cores {Environment.ProcessorCount}, rounds {rounds}, load {load}");
        Console.WriteLine("| mesh | triangles | tolerance | result | " + string.Join(" | ", Variants.Select(v => v.name + " (ms, median)")) + " | " + string.Join(" | ", Variants.Skip(1).Select(v => v.name + " / parallel")) + " |");
        Console.WriteLine("|---|---|---|---|" + string.Concat(Enumerable.Repeat("---|", 2 * Variants.Length - 1)));
        using var stop = new CancellationTokenSource();
        var busy = Enumerable.Range(0, load).Select(_ => Task.Run(() => Busy(stop.Token))).ToArray();
        try {
            foreach (var (name, input) in meshes)
                foreach (double rel in new[] { 1e-3, 3e-3 }) {
                    double tol = rel * input.CachedBounds.DiagonalLength;
                    var times = Variants.Select(_ => new List<double>()).ToArray();
                    var results = new DMesh3[Variants.Length];
                    OnThread(() => Reduce(input, tol, 0, new Stopwatch()));    // warm-up (JIT)
                    for (int r = 0; r < rounds; ++r)
                        for (int v = 0; v < Variants.Length; ++v) {
                            int vi = (v + r) % Variants.Length;   // rotate the order
                            var sw = new Stopwatch();
                            DMesh3 m = null;
                            OnThread(() => m = Reduce(input, tol, Variants[vi].min, sw));
                            times[vi].Add(sw.Elapsed.TotalMilliseconds);
                            results[vi] = m;
                        }
                    for (int v = 1; v < Variants.Length; ++v)
                        if (!Same(results[0], results[v])) throw new Exception($"{name} {rel}: {Variants[v].name} differs from parallel");
                    double[] med = times.Select(t => { t.Sort(); return t[t.Count / 2]; }).ToArray();
                    Console.WriteLine($"| {name} | {input.TriangleCount} | {rel} | {results[0].TriangleCount} | " + string.Join(" | ", med.Select(x => x.ToString("F0")))
                        + " | " + string.Join(" | ", med.Skip(1).Select(x => (x / med[0]).ToString("F2"))) + " |");
                }
        } finally {
            stop.Cancel();
            Task.WaitAll(busy);
        }
    }

    static DMesh3 Reduce(DMesh3 input, double tol, int min, Stopwatch sw)
    {
        var m = new DMesh3(input);
        sw.Start();
        new Reducer(m) { MaxDeviation = tol, DeviationParallelMinTriangles = min }.ReduceToTriangleCount(4);
        sw.Stop();
        return m;
    }

    // runs f on a new thread that is not a thread pool worker (a test runner's thread) and waits for it
    static void OnThread(Action f)
    {
        var t = new Thread(() => f());
        t.Start();
        t.Join();
    }

    // other work: parallel loops of short items, as other tests' library calls
    static void Busy(CancellationToken stop)
    {
        double sink = 0;
        while (!stop.IsCancellationRequested)
            Parallel.For(0, 64, i => {
                double x = i;
                for (int k = 0; k < 20000; ++k) x = Math.Sqrt(x + k);
                Volatile.Write(ref sink, x);
            });
    }

    static bool Same(DMesh3 a, DMesh3 b)
    {
        if (a.TriangleCount != b.TriangleCount || a.MaxVertexID != b.MaxVertexID) return false;
        foreach (int v in a.VertexIndices()) if (!b.IsVertex(v) || a.GetVertex(v) != b.GetVertex(v)) return false;
        foreach (int t in a.TriangleIndices()) if (!b.IsTriangle(t) || a.GetTriangle(t) != b.GetTriangle(t)) return false;
        return true;
    }
}
