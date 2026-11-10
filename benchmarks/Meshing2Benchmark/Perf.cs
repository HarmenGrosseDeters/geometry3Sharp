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

        /// <summary> constrained inputs: a coastline ring, crossing segments, a parcel grid of rings </summary>
        static ConstrainedMesh2Input Constrained(string kind, int n)
        {
            var input = new ConstrainedMesh2Input();
            ulong s = 0x9E3779B97F4A7C15UL ^ (ulong)n;
            double Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s >> 11) * (1.0 / (1UL << 53)); }
            switch (kind) {
                case "coastline": {   // a star-shaped ring of n points at UTM coordinates, cm grid, radius 10 km with 1 % noise
                    var ring = new int[n];
                    for (int i = 0; i < n; ++i) {
                        double a = 2 * Math.PI * i / n, r = 10000 * (1 + 0.01 * (Next() - 0.5));
                        input.Points.Add(new Vector2d(Math.Round(500000 + r * Math.Cos(a), 2), Math.Round(5700000 + r * Math.Sin(a), 2)));
                        ring[i] = i;
                    }
                    input.Rings.Add(new Mesh2Ring(ring));
                    break;
                }
                case "crossings": {   // n/2 random segments in a unit square: about n^2/40 crossings, so n stays small
                    for (int i = 0; i < n / 2; ++i) {
                        input.Points.Add(new Vector2d(Next(), Next())); input.Points.Add(new Vector2d(Next(), Next()));
                        input.Segments.Add(new Mesh2Segment(2 * i, 2 * i + 1, i % 7));
                    }
                    break;
                }
                default: {   // parcels: a grid of square rings sharing their borders, plus one random point inside each
                    int side = Math.Max(1, (int)Math.Sqrt(n / 2.0));
                    for (int j = 0; j <= side; ++j) for (int i = 0; i <= side; ++i) input.Points.Add(new Vector2d(300000 + 20 * i, 5600000 + 20 * j));
                    for (int j = 0; j < side; ++j)
                        for (int i = 0; i < side; ++i) {
                            int a = j * (side + 1) + i;
                            input.Rings.Add(new Mesh2Ring(new[] { a, a + 1, a + side + 2, a + side + 1 }, false, j * side + i));
                            input.Points.Add(new Vector2d(300000 + 20 * i + 20 * Next(), 5600000 + 20 * j + 20 * Next()));
                        }
                    break;
                }
            }
            return input;
        }

        static int RunConstrained(string[] args, Func<string[], string, string> arg)
        {
            var sizes = (arg(args, "--sizes") ?? "1000,10000,100000,400000").Split(',').Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            var kinds = (arg(args, "--kinds") ?? "coastline,parcels,crossings").Split(',');
            int repeats = int.Parse(arg(args, "--repeats") ?? "3", CultureInfo.InvariantCulture);
            Console.WriteLine("runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ", " + Environment.ProcessorCount + " cores");
            Console.WriteLine("producer,kind,n,vertices,triangles,subsegments,median_ms,min_ms,allocated_mb,status");
            foreach (var kind in kinds)
                foreach (int n in sizes) {
                    if (kind == "crossings" && n > 4000) continue;   // quadratic output by construction
                    var input = Constrained(kind, n);
                    var times = new List<double>();
                    long allocated = 0;
                    ConstrainedTriangulation2Result result = null;
                    for (int r = 0; r < repeats + 1; ++r) {
                        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                        long before = GC.GetAllocatedBytesForCurrentThread();
                        var sw = Stopwatch.StartNew();
                        result = new ConstrainedDelaunayMesh2().Compute(input);
                        sw.Stop();
                        long after = GC.GetAllocatedBytesForCurrentThread();
                        if (r == 0) continue;
                        times.Add(sw.Elapsed.TotalMilliseconds);
                        allocated = after - before;
                    }
                    times.Sort();
                    var m = result.Mesh;
                    Console.WriteLine(string.Join(",", "g3-constrained", kind, input.Points.Count.ToString(CultureInfo.InvariantCulture),
                        (m?.Vertices.Count ?? 0).ToString(CultureInfo.InvariantCulture), (m?.TriangleCount ?? 0).ToString(CultureInfo.InvariantCulture),
                        (m?.Subsegments.Count ?? 0).ToString(CultureInfo.InvariantCulture),
                        times[times.Count / 2].ToString("F1", CultureInfo.InvariantCulture), times[0].ToString("F1", CultureInfo.InvariantCulture),
                        (allocated / 1048576.0).ToString("F1", CultureInfo.InvariantCulture), result.Status));
                }
            return 0;
        }

        /// <summary> a side x side grid of square rings sharing their borders, the inner corners jittered by a quarter of a cell (a fixed seed):
        /// the parcels of a cadastre, the proxy for the Herne tiles (which are not in the repository) </summary>
        static ConstrainedMesh2Input JitteredParcels(int side)
        {
            var rnd = new Random(7);
            var input = new ConstrainedMesh2Input();
            for (int i = 0; i <= side; ++i)
                for (int j = 0; j <= side; ++j) {
                    bool edge = i == 0 || j == 0 || i == side || j == side;
                    input.Points.Add(new Vector2d(i + (edge ? 0 : (rnd.NextDouble() - 0.5) * 0.5), j + (edge ? 0 : (rnd.NextDouble() - 0.5) * 0.5)));
                }
            for (int i = 0; i < side; ++i)
                for (int j = 0; j < side; ++j) {
                    int a = i * (side + 1) + j;
                    input.Rings.Add(new Mesh2Ring(new[] { a, a + side + 1, a + side + 2, a + 1 }, false, i * side + j));
                }
            return input;
        }

        /// <summary> the quality mesher on jittered parcels, with and without a snap pixel of 1/64 cell (--quality; sides in --sizes) </summary>
        static int RunQuality(string[] args, Func<string[], string, string> arg)
        {
            var sides = (arg(args, "--sizes") ?? "30,60,120").Split(',').Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            int repeats = int.Parse(arg(args, "--repeats") ?? "1", CultureInfo.InvariantCulture);
            double angle = double.Parse(arg(args, "--angle") ?? "30", CultureInfo.InvariantCulture);
            Console.WriteLine("runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ", " + Environment.ProcessorCount + " cores");
            Console.WriteLine("producer,kind,side,triangles,median_ms,min_ms,status");
            foreach (var snap in new[] { 1.0 / 64, 0.0 })
                foreach (int side in sides) {
                    var input = JitteredParcels(side);
                    var times = new List<double>();
                    ConstrainedTriangulation2Result result = null;
                    for (int r = 0; r < repeats + 1; ++r) {
                        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                        var sw = Stopwatch.StartNew();
                        result = new QualityMesh2 { MinAngle = angle, SnapTolerance = snap, SteinerLimit = 3000000 }.Compute(input);
                        sw.Stop();
                        if (r > 0) times.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    times.Sort();
                    Console.WriteLine(string.Join(",", "g3-quality", snap > 0 ? "parcels-snap" : "parcels", side.ToString(CultureInfo.InvariantCulture),
                        (result.Mesh?.TriangleCount ?? 0).ToString(CultureInfo.InvariantCulture),
                        times[times.Count / 2].ToString("F0", CultureInfo.InvariantCulture), times[0].ToString("F0", CultureInfo.InvariantCulture), result.Status));
                }
            return 0;
        }

        internal static int Run(string[] args, Func<string[], string, string> arg)
        {
            if (args.Contains("--quality")) return RunQuality(args, arg);
            if (args.Contains("--m2")) return RunConstrained(args, arg);
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
