// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Width of the certified geodesic enclosure of ExactMeshGeodesicDistance (internal) on procedural grids, its time and
// window count (#1058, docs/design/GEODESICS.md sections 6b, 6d). No mesh files. Set G3_FLAT_EMITS=1 for the S1 behaviour
// (flat vertices emit) to compare with A7'.
//   dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- reach      the reach meshes of GEODESICS.md 6b
//   dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- sweep      domes and saddles over a curvature sweep
//   dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- path 101,201   TracePath of the public class: the exact paths to about 2000 vertices (time, points, length against distance)
//   dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- poly 101,201,317   the public PolyhedralGeodesicDistance (docs/design/GEODESICS.md 6e): time, windows, memory; no oracle
using System;
using System.Linq;
using System.Diagnostics;
using g3;

static class Program
{
    // FNV-1a over the bits of Distance, NearestSource and the window count: equal digests mean bit for bit equal results
    static ulong Digest(PolyhedralGeodesicDistance g)
    {
        ulong h = 14695981039346656037UL;
        void Mix(ulong x) { h ^= x; h *= 1099511628211UL; }
        if (g.Distance != null) for (int i = 0; i < g.Distance.Length; ++i) { Mix((ulong)BitConverter.DoubleToInt64Bits(g.Distance[i])); Mix((ulong)g.NearestSource[i]); }
        Mix((ulong)g.Windows);
        return h;
    }
    static DMesh3 Grid(double half, int n, Func<double, double, double> h)
    {
        // the alternating diagonals of the tests (ExactMeshGeodesicDistanceTests.Grid)
        var m = new DMesh3();
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                double x = -half + 2 * half * i / (n - 1), y = -half + 2 * half * j / (n - 1);
                m.AppendVertex(new Vector3d(x, y, h(x, y)));
            }
        for (int j = 0; j < n - 1; ++j)
            for (int i = 0; i < n - 1; ++i) {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                if (((i + j) & 1) == 0) { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); }
                else { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
            }
        return m;
    }

    static double Terrain(double x, double y) => 3 * Math.Sin(x / 7 + 1) * Math.Sin(y / 5) + 0.8 * Math.Sin(x * 1.3) * Math.Cos(y * 1.1);
    static readonly double RidgeSlope = Math.Tan(75 * Math.PI / 180);
    static double Ridge(double x) => RidgeSlope * Math.Max(0, 2.5 - Math.Abs(x));
    // slope 4: the vertices of the slope planes and of the folds are exactly flat in doubles (A7', GEODESICS.md 6d)
    static double Ridge4(double x) => 4 * Math.Max(0, 2.5 - Math.Abs(x));

    // one Compute: status, seconds, windows and the largest (Upper - Lower) / Lower over the vertices other than the source
    static void Row(string name, DMesh3 m, int src)
    {
        var g = new ExactMeshGeodesicDistance(m) { MaxWindows = int.Parse(Environment.GetEnvironmentVariable("G3_MAXW") ?? "300000"), FlatVerticesEmit = Environment.GetEnvironmentVariable("G3_FLAT_EMITS") == "1" };
        var sw = Stopwatch.StartNew();
        var r = g.Compute(new[] { src });
        double secs = sw.Elapsed.TotalSeconds;
        double worst = 0;
        if (r == ExactMeshGeodesicDistance.Result.Ok)
            foreach (int v in m.VertexIndices())
                if (v != src && g.Lower[v] > 0) worst = Math.Max(worst, (g.Upper[v] - g.Lower[v]) / g.Lower[v]);
        string gap = r == ExactMeshGeodesicDistance.Result.Ok ? worst.ToString("E2") : "";
        Console.WriteLine($"| {name} | {m.VertexCount} | {r} | {secs:F2} | {g.Windows} | {gap} |");
    }

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "reach";
        Console.WriteLine("| mesh | vertices | status | seconds | windows | largest (Upper - Lower) / Lower |");
        Console.WriteLine("|---|---|---|---|---|---|");
        if (mode == "reach") {
            foreach (int n in new[] { 41, 101 }) {
                Row($"plane {n} x {n}, centre", Grid(1, n, (x, y) => 0), (n / 2) * n + n / 2);
                Row($"terrain {n} x {n}, centre", Grid(n / 2, n, Terrain), (n / 2) * n + n / 2);
                Row($"saddle 0.3 (x^2 - y^2) {n} x {n}, centre", Grid(1, n, (x, y) => 0.3 * (x * x - y * y)), (n / 2) * n + n / 2);
            }
            foreach (int n in new[] { 21, 31, 41 }) {
                double half = (n - 1) * 0.25 / 2;
                Row($"aligned ridge {n} x {n}", Grid(half, n, (x, y) => Ridge(x)), (n / 2) * n + 3);
            }
            foreach (int n in new[] { 41, 101 }) {
                double half = (n - 1) * 0.25 / 2;
                Row($"aligned ridge, slope 4 (exactly flat) {n} x {n}", Grid(half, n, (x, y) => Ridge4(x)), (n / 2) * n + 3);
            }
        } else if (mode == "sweep") {
            foreach (string kind in new[] { "dome", "saddle" })
                foreach (int n in new[] { 11, 21, 41 })
                    foreach (double k in new[] { 0, 1e-8, 1e-6, 1e-5, 1e-4, 1e-2, 0.3 })
                        foreach (string at in new[] { "centre", "corner", "edge" }) {
                            Func<double, double, double> h = kind == "dome" ? (x, y) => -k * (x * x + y * y) : (x, y) => k * (x * x - y * y);
                            int src = at == "centre" ? (n / 2) * n + n / 2 : at == "corner" ? 0 : n / 2;
                            Row($"{kind} k = {k:G2}, {n} x {n}, {at}", Grid(1, n, h), src);
                        }
        } else if (mode == "poly") {
            Console.WriteLine("| mesh | vertices | status | seconds | windows | windows per vertex | allocated MB | peak working set MB | result digest |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
            foreach (int n in (args.Length > 1 ? args[1] : "101,201,317").Split(',').Select(int.Parse)) {
                foreach (string fam in (Environment.GetEnvironmentVariable("G3_FAMS") ?? "terrain,plane,ridge4,ridge75,saddle").Split(',')) {
                    double half = (n - 1) * 0.25 / 2;
                    DMesh3 m = fam == "terrain" ? Grid(n / 2, n, Terrain) : fam == "plane" ? Grid(1, n, (x, y) => 0) : fam == "saddle" ? Grid(1, n, (x, y) => 0.3 * (x * x - y * y))
                        : fam == "ridge4" ? Grid(half, n, (x, y) => Ridge4(x)) : Grid(half, n, (x, y) => Ridge(x));
                    int src = fam.StartsWith("ridge") ? (n / 2) * n + 3 : (n / 2) * n + n / 2;
                    var g = new PolyhedralGeodesicDistance(m) { MaxWindows = long.Parse(Environment.GetEnvironmentVariable("G3_MAXW") ?? "0") };
                    long a0 = GC.GetAllocatedBytesForCurrentThread(); var sw = Stopwatch.StartNew();
                    var st = g.Compute(new[] { src });
                    double secs = sw.Elapsed.TotalSeconds; long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                    long peak = Process.GetCurrentProcess().PeakWorkingSet64;
                    Console.WriteLine($"| {fam} {n} x {n} | {m.VertexCount} | {st} | {secs:F2} | {g.Windows} | {(double)g.Windows / m.VertexCount:F1} | {alloc / 1048576} | {peak / 1048576} | digest {Digest(g):X16} |");
                }
            }
        } else if (mode == "path") {
            Console.WriteLine("| mesh | vertices | Compute seconds | traces | trace seconds (total) | microseconds per trace | longest path (points) | largest |Length - Distance| / Distance |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|");
            foreach (int n in (args.Length > 1 ? args[1] : "101,201").Split(',').Select(int.Parse)) {
                foreach (string fam in (Environment.GetEnvironmentVariable("G3_FAMS") ?? "terrain,plane,saddle").Split(',')) {
                    double half = (n - 1) * 0.25 / 2;
                    DMesh3 m = fam == "terrain" ? Grid(n / 2, n, Terrain) : fam == "plane" ? Grid(1, n, (x, y) => 0) : Grid(1, n, (x, y) => 0.3 * (x * x - y * y));
                    int src = (n / 2) * n + n / 2;
                    var g = new PolyhedralGeodesicDistance(m) { MaxWindows = long.Parse(Environment.GetEnvironmentVariable("G3_MAXW") ?? "0") };
                    var sw = Stopwatch.StartNew(); var st = g.Compute(new[] { src }); double cs = sw.Elapsed.TotalSeconds;
                    if (st != PolyhedralGeodesicStatus.Ok) { Console.WriteLine($"| {fam} {n} x {n} | {m.VertexCount} | {cs:F2} ({st}) | | | | | |"); continue; }
                    int step = Math.Max(1, m.VertexCount / 2000), traces = 0, maxPts = 0; double worst = 0; sw.Restart();
                    for (int v = 0; v < m.MaxVertexID; v += step) {
                        var r = g.TracePath(v); if (r.Status != PolyhedralGeodesicPathStatus.Ok) { Console.WriteLine($"trace {v}: {r.Status}"); continue; }
                        ++traces; maxPts = Math.Max(maxPts, r.Points.Count); if (r.Distance > 0) worst = Math.Max(worst, Math.Abs(r.Length - r.Distance) / r.Distance);
                    }
                    double ts = sw.Elapsed.TotalSeconds;
                    Console.WriteLine($"| {fam} {n} x {n} | {m.VertexCount} | {cs:F2} | {traces} | {ts:F3} | {ts / traces * 1e6:F0} | {maxPts} | {worst:E1} |");
                }
            }
        } else { Console.Error.WriteLine("modes: reach, sweep, poly, path"); return 1; }
        return 0;
    }
}
