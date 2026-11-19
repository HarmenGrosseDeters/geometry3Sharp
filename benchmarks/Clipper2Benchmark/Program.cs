using System;
using System.Diagnostics;
using System.Linq;

namespace Clipper2Benchmark
{
    /// <summary>
    /// dotnet run -c Release -- [reps] [n ...]: the median time of each offset of Ops over reps runs (default 3) and the digest of
    /// its result; the ring sizes n default to 1000 20000 100000. Run it on the base and on the branch and compare the times and the
    /// digests (README.md).
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            int reps = args.Length > 0 ? int.Parse(args[0]) : 3;
            int[] sizes = args.Length > 1 ? args.Skip(1).Select(int.Parse).ToArray() : new[] { 1000, 20000, 100000 };
            Console.WriteLine($"# runtime {Environment.Version} cores={Environment.ProcessorCount} reps={reps}");
            Console.WriteLine("op\tmedian_ms\tdigest");
            foreach (var (name, run) in Ops.All(sizes)) {
                var times = new double[reps];
                double digest = 0;
                run();   // warm-up (the largest ring is slow on the base: one run only)
                for (int i = 0; i < reps; i++) {
                    var sw = Stopwatch.StartNew();
                    digest = run().Digest();
                    times[i] = sw.Elapsed.TotalMilliseconds;
                }
                Array.Sort(times);
                Console.WriteLine($"{name}\t{times[reps / 2]:F1}\t{digest:F0}");
            }
            return 0;
        }
    }
}
