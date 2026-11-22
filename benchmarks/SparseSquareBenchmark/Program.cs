// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// SymmetricSparseMatrix.Square / Multiply (parallel) and PackedSparseMatrix.Square on the weighted Laplacian of a sphere mesh.
// Prints the best time of `reps` runs and a hash of the product applied to a vector (the same for every run and schedule
// since the entries are inserted in row order; before that change the hash of the symmetric ones varied with the schedule).
//
//   dotnet run -c Release --project benchmarks/SparseSquareBenchmark -- [reps=5] [res=24]
using System;
using System.Diagnostics;
using System.Linq;
using g3;

static class Program
{
    static ulong Hash(double[] x)
    {
        ulong h = 1469598103934665603UL;
        foreach (double v in x) { h ^= (ulong)BitConverter.DoubleToInt64Bits(v); h *= 1099511628211UL; }
        return h;
    }

    static double Best(int reps, Action a)
    {
        double best = double.MaxValue;
        for (int k = 0; k < reps; ++k) {
            var sw = Stopwatch.StartNew(); a(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        int reps = A.ContainsKey("reps") ? int.Parse(A["reps"]) : 5, res = A.ContainsKey("res") ? int.Parse(A["res"]) : 24;
        var mesh = new Sphere3Generator_NormalizedCube() { EdgeVertices = res }.Generate().MakeDMesh();
        var m = new SymmetricSparseMatrix();
        foreach (int v in mesh.VertexIndices()) {
            double sum = 0;
            foreach (int n in mesh.VtxVerticesItr(v)) {
                double w = 1.0 / (1 + (v + n) % 5) + 1e-3 * ((v * 7 + n * 13) % 11);
                if (n > v) m.Set(v, n, -w);
                sum += w;
            }
            m.Set(v, v, sum + 0.25);
        }
        int N = m.Rows;
        var x = new double[N]; for (int i = 0; i < N; ++i) x[i] = Math.Sin(i * 0.7) + 1.0 / (1 + i % 7);
        var y = new double[N];
        Console.WriteLine($"N={N} cores={Environment.ProcessorCount}");

        SymmetricSparseMatrix r = null;
        double t = Best(reps, () => r = m.Square(true));
        ulong h = 0; var hs = new System.Collections.Generic.HashSet<ulong>();
        for (int k = 0; k < 8; ++k) { m.Square(true).Multiply(x, y); hs.Add(Hash(y)); }
        Console.WriteLine($"SymmetricSparseMatrix.Square(parallel)  {t,8:F1} ms   distinct product hashes in 8 runs: {hs.Count}");
        hs.Clear();
        t = Best(reps, () => { SymmetricSparseMatrix q = null; m.Multiply(m, ref q, true); r = q; });
        for (int k = 0; k < 8; ++k) { SymmetricSparseMatrix q = null; m.Multiply(m, ref q, true); q.Multiply(x, y); hs.Add(Hash(y)); }
        Console.WriteLine($"SymmetricSparseMatrix.Multiply(parallel){t,8:F1} ms   distinct product hashes in 8 runs: {hs.Count}");
        t = Best(reps, () => m.Square(false));
        Console.WriteLine($"SymmetricSparseMatrix.Square(serial)    {t,8:F1} ms");
        t = Best(reps, () => m.SquarePackedParallel());
        Console.WriteLine($"SquarePackedParallel                    {t,8:F1} ms");
        return hs.Count == 1 ? 0 : 1;
    }
}
