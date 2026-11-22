// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// SparseCholesky benchmark: Factorize() (ordering, symbolic part and numeric part) and Solve() on procedural symmetric positive
// definite matrices with the structure of mesh systems (a triangulated grid, 1 to 3 unknowns per node, eg the 2n x 2n
// interleaved CrossField system) and of a 3D grid. Prints the best time of `reps` runs and a hash of the solution, which must be
// the same on every machine and framework (the kernels give the same bits as the scalar code); a different hash makes the run exit with 1.
//
//   dotnet run -c Release --project benchmarks/SparseCholeskyBenchmark -- [only=<regex>] [reps=3]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using g3;

static class Program
{
    static ulong Next(ref ulong s) { s = s * 6364136223846793005UL + 1442695040888963407UL; return s >> 11; }

    static SparseCholesky Grid2(int nx, int ny, int bs)
    {
        ulong s = 1; var c = new SparseCholesky(nx * ny * bs, bs);
        for (int y = 0; y < ny; ++y)
            for (int x = 0; x < nx; ++x) {
                int a = (y * nx + x) * bs;
                for (int u = 0; u < bs; ++u) {
                    c.Add(a + u, a + u, 8.0 + (Next(ref s) % 64) / 64.0);
                    for (int v = 0; v < u; ++v) c.Add(a + u, a + v, -((Next(ref s) % 32) / 64.0));
                }
                foreach (var q in new[] { new[] { x + 1, y }, new[] { x, y + 1 }, new[] { x + 1, y + 1 } }) {
                    if (q[0] >= nx || q[1] >= ny) continue;
                    int b = (q[1] * nx + q[0]) * bs;
                    for (int u = 0; u < bs; ++u)
                        for (int v = 0; v < bs; ++v) c.Add(b + v, a + u, -((Next(ref s) % 128) / 128.0 + 0.25) / bs);
                }
            }
        return c;
    }

    static SparseCholesky Grid3(int k)
    {
        ulong s = 1; var c = new SparseCholesky(k * k * k);
        for (int z = 0; z < k; ++z) for (int y = 0; y < k; ++y) for (int x = 0; x < k; ++x) {
            int a = (z * k + y) * k + x; c.Add(a, a, 9.0 + (Next(ref s) % 16) / 16.0);
            if (x + 1 < k) c.Add(a + 1, a, -(double)(1 + Next(ref s) % 8) / 8.0);
            if (y + 1 < k) c.Add(a + k, a, -(double)(1 + Next(ref s) % 8) / 8.0);
            if (z + 1 < k) c.Add(a + k * k, a, -(double)(1 + Next(ref s) % 8) / 8.0);
        }
        return c;
    }

    // complex Hermitian matrix with the pattern of Grid2 (bs=1), the structure of the CrossField system of CrossField
    static SparseHermitianCholesky HermitianGrid(int nx, int ny)
    {
        ulong s = 1; var c = new SparseHermitianCholesky(nx * ny);
        for (int y = 0; y < ny; ++y)
            for (int x = 0; x < nx; ++x) {
                int a = y * nx + x; c.Add(a, a, 8.0 + (Next(ref s) % 64) / 64.0, 0);
                foreach (var q in new[] { new[] { x + 1, y }, new[] { x, y + 1 }, new[] { x + 1, y + 1 } }) {
                    if (q[0] >= nx || q[1] >= ny) continue;
                    int b = q[1] * nx + q[0];
                    c.Add(b, a, -((Next(ref s) % 128) / 128.0 + 0.25), (Next(ref s) % 64) / 64.0 - 0.5);
                }
            }
        return c;
    }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        string only = A.ContainsKey("only") ? A["only"] : ".*";
        int reps = A.ContainsKey("reps") ? int.Parse(A["reps"]) : 3;
        if (A.ContainsKey("compare") && A["compare"] == "nd")
            return RunCompareND(only, reps);
        var cases = new (string name, Func<SparseCholesky> make, string hash)[] {
            ("grid2d bs=1 n=40000", () => Grid2(200, 200, 1), "5af59d2818b8f21d"),
            ("grid2d bs=2 n=80000 (CrossField-like)", () => Grid2(200, 200, 2), "ce7f838b19a99e97"),
            ("grid2d bs=2 n=120000 (CrossField-like)", () => Grid2(245, 245, 2), "34e470467305cff4"),
            ("grid3d n=27000", () => Grid3(30), "641b48317c4b9c92"),
        };
        Console.WriteLine("vector kernels: " + (Vector256Accelerated() ? "yes" : "no"));
        Console.WriteLine("| matrix | nnz(L) | Factorize (s) | Solve parallel / serial (ms) | solution hash |");
        Console.WriteLine("|---|---|---|---|---|");
        bool bad = false;
        foreach (var cs in cases) {
            if (!Regex.IsMatch(cs.name, only)) continue;
            double bf = double.MaxValue, bs = double.MaxValue, bss = double.MaxValue; long nnz = 0; ulong h = 0;
            for (int r = 0; r < reps; ++r) {
                var c = cs.make();
                var sw = Stopwatch.StartNew();
                if (!c.Factorize()) throw new Exception(c.Error);
                bf = Math.Min(bf, sw.Elapsed.TotalSeconds);
                nnz = c.FactorNonZeros;
                var b = new double[c.N]; for (int i = 0; i < b.Length; ++i) b[i] = 1 + (i % 7) / 7.0;
                var x = new double[c.N];
                bs = Math.Min(bs, TimeSolves(() => c.Solve(b, x), 3));
                h = 1469598103934665603UL; foreach (double v in x) { h ^= (ulong)BitConverter.DoubleToInt64Bits(v); h *= 1099511628211UL; }
                SetField(c, "ForceSerialSolve", true); bss = Math.Min(bss, TimeSolves(() => c.Solve(b, x), 3));
            }
            Console.WriteLine($"| {cs.name} | {nnz / 1e6:F1}M | {bf:F2} | {bs:F1} / {bss:F1} | {h:x16} |");
            if (h.ToString("x16") != cs.hash) { Console.Error.WriteLine("solution hash differs from the scalar code: expected " + cs.hash); bad = true; }
        }
        foreach (var (name, nx, hash) in new[] { ("hermitian grid2d n=40000", 200, "dc5d10357e466a03"), ("hermitian grid2d n=60000", 245, "5d513cbc4bee6470") }) {
            if (!Regex.IsMatch(name, only)) continue;
            double bf = double.MaxValue, bs = double.MaxValue, bss = double.MaxValue; long nnz = 0; ulong h = 0;
            for (int r = 0; r < reps; ++r) {
                var c = HermitianGrid(nx, nx);
                var sw = Stopwatch.StartNew();
                if (!c.Factorize()) throw new Exception(c.Error);
                bf = Math.Min(bf, sw.Elapsed.TotalSeconds);
                nnz = c.FactorNonZeros;
                var b = new double[2 * c.N]; for (int i = 0; i < b.Length; ++i) b[i] = 1 + (i % 7) / 7.0;
                var x = new double[2 * c.N];
                bs = Math.Min(bs, TimeSolves(() => c.Solve(b, x), 3));
                h = 1469598103934665603UL; foreach (double v in x) { h ^= (ulong)BitConverter.DoubleToInt64Bits(v); h *= 1099511628211UL; }
                SetField(c, "ForceSerialSolve", true); bss = Math.Min(bss, TimeSolves(() => c.Solve(b, x), 3));
            }
            Console.WriteLine($"| {name} | {nnz / 1e6:F1}M | {bf:F2} | {bs:F1} / {bss:F1} | {h:x16} |");
            if (h.ToString("x16") != hash) { Console.Error.WriteLine("solution hash differs from the previous code: expected " + hash); bad = true; }
        }
        return bad ? 1 : 0;
    }

    static SparseCholesky MeshLaplacian(DMesh3 mesh)
    {
        int n = mesh.MaxVertexID;
        var c = new SparseCholesky(n, 1);
        var deg = new int[n];
        foreach (int eid in mesh.EdgeIndices()) {
            Index2i ev = mesh.GetEdgeV(eid);
            c.Add(ev.a, ev.b, -1.0);
            deg[ev.a]++; deg[ev.b]++;
        }
        for (int i = 0; i < n; ++i) c.Add(i, i, deg[i] + 1.0);
        return c;
    }

    static DMesh3 MakeSphere(int slices)
    {
        var gen = new Sphere3Generator_NormalizedCube { Radius = 1.0, EdgeVertices = slices };
        gen.Generate();
        return gen.MakeDMesh();
    }

    static (long nnz, long flops, int maxSnWidth, double totalS, double numS, double solveMs, double relRes) MeasureReal(
        Func<SparseCholesky> make, CholeskyOrdering ord, int reps)
    {
        double bestTot = double.MaxValue, bestNum = double.MaxValue, bestSolve = double.MaxValue;
        long nnz = 0, flops = 0; int maxW = 0; double relRes = 0;
        for (int r = 0; r < reps; ++r) {
            var c = make();
            c.Ordering = ord;
            var sw = Stopwatch.StartNew();
            if (!c.Factorize()) throw new Exception(c.Error);
            double tot = sw.Elapsed.TotalSeconds;
            sw.Restart();
            if (!c.Factorize()) throw new Exception(c.Error);
            double num = sw.Elapsed.TotalSeconds;
            bestTot = Math.Min(bestTot, tot);
            bestNum = Math.Min(bestNum, num);
            nnz = c.FactorNonZeros;
            GetStats(c, out flops, out maxW);
            var b = new double[c.N]; for (int i = 0; i < b.Length; ++i) b[i] = 1 + (i % 7) / 7.0;
            var x = new double[c.N];
            bestSolve = Math.Min(bestSolve, TimeSolves(() => c.Solve(b, x), 2));
            if (r == 0) relRes = RelativeResidualReal(c, b, x);
        }
        return (nnz, flops, maxW, bestTot, bestNum, bestSolve, relRes);
    }

    static (long nnz, long flops, int maxSnWidth, double totalS, double solveMs, double relRes) MeasureHerm(
        Func<SparseHermitianCholesky> make, CholeskyOrdering ord, int reps)
    {
        double bestTot = double.MaxValue, bestSolve = double.MaxValue;
        long nnz = 0, flops = 0; int maxW = 0; double relRes = 0;
        for (int r = 0; r < reps; ++r) {
            var c = make();
            c.Ordering = ord;
            var sw = Stopwatch.StartNew();
            if (!c.Factorize()) throw new Exception(c.Error);
            bestTot = Math.Min(bestTot, sw.Elapsed.TotalSeconds);
            nnz = c.FactorNonZeros;
            GetStats(c, out flops, out maxW);
            var b = new double[2 * c.N]; for (int i = 0; i < b.Length; ++i) b[i] = 1 + (i % 7) / 7.0;
            var x = new double[2 * c.N];
            bestSolve = Math.Min(bestSolve, TimeSolves(() => c.Solve(b, x), 2));
            if (r == 0) relRes = RelativeResidualHerm(c, b, x);
        }
        return (nnz, flops, maxW, bestTot, bestSolve, relRes);
    }

    static void GetStats(object solver, out long flops, out int maxW)
    {
        var arr = (int[][])solver.GetType().GetProperty("AnalysisArrays",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(solver);
        int[] Lp = arr[1], snFirst = arr[3];
        flops = 0;
        for (int j = 0; j < Lp.Length - 1; ++j) {
            long c = Lp[j + 1] - Lp[j];
            flops += c * c;
        }
        maxW = 0;
        for (int s = 0; s < snFirst.Length - 1; ++s) {
            int w = snFirst[s + 1] - snFirst[s];
            if (w > maxW) maxW = w;
        }
    }

    static double RelativeResidualReal(SparseCholesky c, double[] b, double[] x)
    {
        var colPtr = (int[])c.GetType().GetField("aColPtr", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(c);
        var row = (int[])c.GetType().GetField("aRow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(c);
        var val = (double[])c.GetType().GetField("aVal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(c);
        var ax = new double[c.N];
        for (int j = 0; j < c.N; ++j) {
            for (int q = colPtr[j]; q < colPtr[j + 1]; ++q) {
                int r = row[q]; double v = val[q];
                ax[r] += v * x[j];
                if (r != j) ax[j] += v * x[r];
            }
        }
        double rn = 0, bn = 0;
        for (int i = 0; i < c.N; ++i) { double d = ax[i] - b[i]; rn += d * d; bn += b[i] * b[i]; }
        return Math.Sqrt(rn / bn);
    }

    static double RelativeResidualHerm(SparseHermitianCholesky c, double[] b, double[] x)
    {
        var colPtr = (int[])c.GetType().GetField("aColPtr", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(c);
        var row = (int[])c.GetType().GetField("aRow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(c);
        var aRe = (double[])c.GetType().GetField("aRe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(c);
        var aIm = (double[])c.GetType().GetField("aIm", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(c);
        var ax = new double[2 * c.N];
        for (int j = 0; j < c.N; ++j) {
            double xjr = x[2 * j], xji = x[2 * j + 1];
            for (int q = colPtr[j]; q < colPtr[j + 1]; ++q) {
                int r = row[q]; double vr = aRe[q], vi = aIm[q];
                ax[2 * r] += vr * xjr - vi * xji;
                ax[2 * r + 1] += vr * xji + vi * xjr;
                if (r != j) {
                    double xrr = x[2 * r], xri = x[2 * r + 1];
                    // conj(v) = (vr, -vi)
                    ax[2 * j] += vr * xrr + vi * xri;
                    ax[2 * j + 1] += vr * xri - vi * xrr;
                }
            }
        }
        double rn = 0, bn = 0;
        for (int i = 0; i < 2 * c.N; ++i) { double d = ax[i] - b[i]; rn += d * d; bn += b[i] * b[i]; }
        return Math.Sqrt(rn / bn);
    }

    static SparseCholesky BiharmonicGrid2(int nx, int ny)
    {
        // 2-ring stencil (distance <= 2 in Chebyshev/triangulated metric), matching BiharmonicMeshDeformer sparsity.
        ulong s = 7;
        int n = nx * ny;
        var c = new SparseCholesky(n, 1);
        for (int y = 0; y < ny; ++y)
            for (int x = 0; x < nx; ++x) {
                int a = y * nx + x;
                double diag = 24.0 + (Next(ref s) % 32) / 32.0;
                c.Add(a, a, diag);
                for (int dy = -2; dy <= 2; ++dy)
                    for (int dx = -2; dx <= 2; ++dx) {
                        if (dx == 0 && dy == 0) continue;
                        if (Math.Abs(dx) + Math.Abs(dy) > 3) continue;
                        int nx2 = x + dx, ny2 = y + dy;
                        if (nx2 < 0 || nx2 >= nx || ny2 < 0 || ny2 >= ny) continue;
                        int b = ny2 * nx + nx2;
                        if (b <= a) continue;
                        double w = (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) ? -1.0 : 0.25;
                        c.Add(b, a, w);
                    }
            }
        return c;
    }

    static int RunCompareND(string only, int reps)
    {
        // Untimed warmup so JIT compilation does not skew the first benchmark row.
        {
            var wAmd = Grid2(40, 40, 1); wAmd.Factorize();
            var wNd = Grid2(40, 40, 1); wNd.Ordering = CholeskyOrdering.NestedDissection; wNd.Factorize();
        }
        Console.WriteLine($"vector kernels: {(Vector256Accelerated() ? "yes" : "no")}, CPUs: {Environment.ProcessorCount}");
        Console.WriteLine("| matrix | nnz(L) AMD / ND | flops AMD / ND | maxSnW AMD / ND | Factorize AMD / ND (s) | Anal+Num ND (ms) | Solve AMD / ND (ms) | relRes ND |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        bool bad = false;
        var cases = new (string name, Func<SparseCholesky> make)[] {
            ("grid2d bs=1 n=40000", () => Grid2(200, 200, 1)),
            ("grid2d bs=2 n=80000", () => Grid2(200, 200, 2)),
            ("grid2d bs=2 n=120000", () => Grid2(245, 245, 2)),
            ("grid3d n=27000", () => Grid3(30)),
            ("sphere V=54152", () => MeshLaplacian(MakeSphere(96))),
            ("biharmonic2d n=40000", () => BiharmonicGrid2(200, 200)),
        };
        foreach (var cs in cases) {
            if (!Regex.IsMatch(cs.name, only)) continue;
            var a = MeasureReal(cs.make, CholeskyOrdering.MinimumDegree, reps);
            GC.Collect();
            var nd = MeasureReal(cs.make, CholeskyOrdering.NestedDissection, reps);
            GC.Collect();
            double nnzPct = 100.0 * (1.0 - (double)nd.nnz / a.nnz);
            double flopPct = 100.0 * (1.0 - (double)nd.flops / a.flops);
            double facPct = 100.0 * (1.0 - nd.totalS / a.totalS);
            double analNdMs = Math.Max(0, (nd.totalS - nd.numS) * 1000.0);
            Console.WriteLine($"| {cs.name} | {a.nnz / 1e6:F2}M / {nd.nnz / 1e6:F2}M ({nnzPct:+0.0;-0.0}%) | {a.flops / 1e6:F0}M / {nd.flops / 1e6:F0}M ({flopPct:+0.0;-0.0}%) | {a.maxSnWidth} / {nd.maxSnWidth} | {a.totalS:F3} / {nd.totalS:F3} ({facPct:+0.0;-0.0}%) | {analNdMs:F0} + {nd.numS * 1000:F0} | {a.solveMs:F1} / {nd.solveMs:F1} | {nd.relRes:E2} |");
            if (!(nd.relRes <= 1e-12)) bad = true;
        }
        foreach (var (name, nx) in new[] { ("hermitian grid2d n=40000", 200), ("hermitian grid2d n=60000", 245) }) {
            if (!Regex.IsMatch(name, only)) continue;
            var a = MeasureHerm(() => HermitianGrid(nx, nx), CholeskyOrdering.MinimumDegree, reps);
            GC.Collect();
            var nd = MeasureHerm(() => HermitianGrid(nx, nx), CholeskyOrdering.NestedDissection, reps);
            GC.Collect();
            double nnzPct = 100.0 * (1.0 - (double)nd.nnz / a.nnz);
            double flopPct = 100.0 * (1.0 - (double)nd.flops / a.flops);
            double facPct = 100.0 * (1.0 - nd.totalS / a.totalS);
            Console.WriteLine($"| {name} | {a.nnz / 1e6:F2}M / {nd.nnz / 1e6:F2}M ({nnzPct:+0.0;-0.0}%) | {a.flops / 1e6:F0}M / {nd.flops / 1e6:F0}M ({flopPct:+0.0;-0.0}%) | {a.maxSnWidth} / {nd.maxSnWidth} | {a.totalS:F3} / {nd.totalS:F3} ({facPct:+0.0;-0.0}%) | - | {a.solveMs:F1} / {nd.solveMs:F1} | {nd.relRes:E2} |");
            if (!(nd.relRes <= 1e-12)) bad = true;
        }
        return bad ? 1 : 0;
    }

    // internal test switches of the solvers (reflection: the benchmark project has no InternalsVisibleTo)
    static void SetField(object o, string name, object v)
    {
        o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(o, v);
    }

    static double TimeSolves(Action solve, int reps)
    {
        double best = double.MaxValue;
        var sw = new Stopwatch();
        for (int r = 0; r < reps; ++r) { sw.Restart(); for (int k = 0; k < 5; ++k) solve(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds / 5); }
        return best;
    }

    static bool Vector256Accelerated()
    {
#if NET7_0_OR_GREATER
        return System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated;
#else
        return false;
#endif
    }
}
