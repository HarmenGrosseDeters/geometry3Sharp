// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Width of the certified geodesic enclosure of ExactMeshGeodesicDistance (internal) on procedural grids, its time and
// window count (#1058, docs/design/GEODESICS.md section 6b). No mesh files.
//   dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- reach      the reach meshes of GEODESICS.md 6b
//   dotnet run -c Release --project benchmarks/GeodesicOracleBenchmark -- sweep      domes and saddles over a curvature sweep
using System;
using System.Diagnostics;
using g3;

static class Program
{
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

    // one Compute: status, seconds, windows and the largest (Upper - Lower) / Lower over the vertices other than the source
    static void Row(string name, DMesh3 m, int src)
    {
        var g = new ExactMeshGeodesicDistance(m);
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
        } else if (mode == "sweep") {
            foreach (string kind in new[] { "dome", "saddle" })
                foreach (int n in new[] { 11, 21, 41 })
                    foreach (double k in new[] { 0, 1e-8, 1e-6, 1e-5, 1e-4, 1e-2, 0.3 })
                        foreach (string at in new[] { "centre", "corner", "edge" }) {
                            Func<double, double, double> h = kind == "dome" ? (x, y) => -k * (x * x + y * y) : (x, y) => k * (x * x - y * y);
                            int src = at == "centre" ? (n / 2) * n + n / 2 : at == "corner" ? 0 : n / 2;
                            Row($"{kind} k = {k:G2}, {n} x {n}, {at}", Grid(1, n, h), src);
                        }
        } else { Console.Error.WriteLine("modes: reach, sweep"); return 1; }
        return 0;
    }
}
