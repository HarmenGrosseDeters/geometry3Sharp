using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;

namespace Meshing2Acceptance
{
    /// <summary>
    /// Timing of the M1 kernel alone (spec 3.1): procedural inputs, the median of several runs, allocated bytes per run.
    /// One sample per size is validated by <see cref="TriangulationMesh2.IsValid"/>; the oracle is far too slow for these sizes.
    /// </summary>
    static class Perf
    {
        static Vector2d[] Points(string kind, int n)
        {
            var p = new Vector2d[n];
            ulong s = 0x2545F4914F6CDD1DUL ^ (ulong)n;
            double Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s >> 11) * (1.0 / (1UL << 53)); }
            switch (kind) {
                case "uniform":
                    for (int i = 0; i < n; ++i) p[i] = new Vector2d(Next(), Next());
                    break;
                case "utm":   // uniform in a 1 km tile at UTM northing, cm resolution
                    for (int i = 0; i < n; ++i) p[i] = new Vector2d(500000 + Math.Floor(Next() * 100000) / 100, 5700000 + Math.Floor(Next() * 100000) / 100);
                    break;
                case "grid": { // cocircular everywhere: the tie rule on every cell
                    int side = (int)Math.Ceiling(Math.Sqrt(n));
                    for (int i = 0; i < n; ++i) p[i] = new Vector2d(i % side, i / side);
                    break;
                }
                case "clustered": // 1% of the points as dense clusters
                    for (int i = 0; i < n; ++i) {
                        if (i % 100 < 50) p[i] = new Vector2d(Next(), Next());
                        else { double cx = (i / 100 % 17) / 17.0, cy = (i / 100 % 13) / 13.0; p[i] = new Vector2d(cx + Next() * 1e-6, cy + Next() * 1e-6); }
                    }
                    break;
                default: throw new FormatException("unknown kind " + kind);
            }
            return p;
        }

        internal static int Run(string[] args, Func<string[], string, string> arg)
        {
            var sizes = (arg(args, "--sizes") ?? "1000,4096,65536,1000000").Split(',').Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            var kinds = (arg(args, "--kinds") ?? "uniform,grid,utm,clustered").Split(',');
            int repeats = int.Parse(arg(args, "--repeats") ?? "5", CultureInfo.InvariantCulture);
            Console.WriteLine("runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ", " + Environment.ProcessorCount + " cores, " + (Environment.Is64BitProcess ? "x64" : "x86"));
            Console.WriteLine("producer,kind,n,triangles,median_ms,min_ms,allocated_mb,valid");
            foreach (var kind in kinds) {
                foreach (int n in sizes) {
                    var pts = Points(kind, n);
                    var times = new List<double>();
                    long allocated = 0;
                    int triangles = 0;
                    string valid = "-";
                    for (int r = 0; r < repeats + 1; ++r) {
                        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                        long before = GC.GetAllocatedBytesForCurrentThread();
                        var sw = Stopwatch.StartNew();
                        var result = new DelaunayMesh2().Compute(pts);
                        sw.Stop();
                        triangles = result.Mesh.TriangleCount;
                        if (r == 0) valid = result.Status == Triangulation2Status.Success && result.Mesh.IsValid(out _) ? "yes" : "NO";
                        long after = GC.GetAllocatedBytesForCurrentThread();
                        if (r == 0) continue;   // warm-up
                        times.Add(sw.Elapsed.TotalMilliseconds);
                        allocated = after - before;
                    }
                    times.Sort();
                    Console.WriteLine(string.Join(",", "g3-delaunay", kind, n.ToString(CultureInfo.InvariantCulture), triangles.ToString(CultureInfo.InvariantCulture),
                        times[times.Count / 2].ToString("F1", CultureInfo.InvariantCulture), times[0].ToString("F1", CultureInfo.InvariantCulture),
                        (allocated / 1048576.0).ToString("F1", CultureInfo.InvariantCulture), valid));
                }
            }
            return 0;
        }
    }
}
