using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

// Triangle count vs error and time, uniform dual contouring (Tolerance 0) vs AdaptiveDualContouring.
// dotnet run --project benchmarks/DualContouringSimplification -c Release -- [--res 32,64,128,256] [--shapes csg,bracket,sphere,torus] [--tol 0.1,0.25,0.5] [--ref 320]
static class Program
{
    static void Main(string[] args)
    {
        string Arg(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
        if (args.Contains("--phases")) { Phases(); return; }
        if (args.Contains("--quality")) { Quality(); return; }
        int[] res = Arg("--res", "32,64,128,256").Split(',').Select(int.Parse).ToArray();
        string[] shapes = Arg("--shapes", string.Join(",", Shapes.Names)).Split(',');
        double[] tols = Arg("--tol", "0.1,0.25,0.5").Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        int refN = int.Parse(Arg("--ref", "320"));
        Console.WriteLine("| shape | N | tolerance | triangles | vs uniform | mean err | max err | mean abs f | max abs f | ms total | ms simplify | clusters (by level) | rounds / rejected |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (string shape in shapes) {
            var f = Shapes.Get(shape);
            double ext = Shapes.Extent(shape);
            Vector3d[] points = ReferencePoints(f, ext, refN);
            foreach (int N in res) {
                int baseTris = 0;
                foreach (double tol in new[] { 0.0 }.Concat(tols)) {
                    double cell = 2 * ext / N;
                    var ad = new AdaptiveDualContouring() {
                        Implicit = new ImplicitFunctionDelegate(f), InsideIsLess = true, CellSize = cell, Tolerance = tol,
                        Bounds = new AxisAlignedBox3d(new Vector3d(-ext, -ext, -ext), new Vector3d(ext, ext, ext))
                    };
                    var sw = Stopwatch.StartNew();
                    ad.Generate();
                    long ms = sw.ElapsedMilliseconds;
                    if (N <= 128) { sw.Restart(); ad.Generate(); ms = Math.Min(ms, sw.ElapsedMilliseconds); }
                    DMesh3 m = ad.Mesh;
                    if (tol == 0) baseTris = m.TriangleCount;
                    string clean = MeshRobustRepair.NotCleanReason(m, true);
                    var tree = new DMeshAABBTree3(m, true);
                    double[] d = new double[points.Length];
                    System.Threading.Tasks.Parallel.For(0, points.Length, i => {
                        double dsq; tree.FindNearestTriangle(points[i], out dsq); d[i] = Math.Sqrt(dsq);
                    });
                    double fm = 0, fx = 0; int nv = 0;
                    foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); double a = Math.Abs(f(p)); fm += a; fx = Math.Max(fx, a); nv++; }
                    string lv = ad.ClustersByLevel.Length > 0 ? string.Join("/", ad.ClustersByLevel.Skip(1)) : "-";
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "| {0} | {1} | {2} | {3} | {4:F2}x | {5:F3} | {6:F3} | {7:F3} | {8:F3} | {9} | {10} | {11} | {12} / {13} |{14}",
                        shape, N, tol, m.TriangleCount, (double)baseTris / m.TriangleCount, d.Average() / cell, d.Max() / cell, fm / nv / cell, fx / cell,
                        ms, ad.PhaseMilliseconds.ContainsKey("simplify") ? ad.PhaseMilliseconds["simplify"] : 0, lv, ad.CheckRounds, ad.RejectedClusters,
                        clean == "" ? "" : " NOT CLEAN: " + clean));
                    Console.Out.Flush();
                }
            }
        }
    }

    // vertices of a fine bisection marching cubes contour: dense points on the true surface
    static Vector3d[] ReferencePoints(Func<Vector3d, double> f, double ext, int n)
    {
        var mc = new MarchingCubes() {
            Implicit = new ImplicitFunctionDelegate(f), RootMode = MarchingCubes.RootfindingModes.Bisection, CubeSize = 2 * ext / n,
            Bounds = new AxisAlignedBox3d(new Vector3d(-ext, -ext, -ext), new Vector3d(ext, ext, ext))
        };
        mc.Generate();
        return mc.Mesh.Vertices().ToArray();
    }

    class ImplicitFunctionDelegate : ImplicitFunction3d
    {
        readonly Func<Vector3d, double> f;
        public ImplicitFunctionDelegate(Func<Vector3d, double> f) { this.f = f; }
        public double Value(ref Vector3d pt) { return f(pt); }
        public AxisAlignedBox3d Bounds() { return AxisAlignedBox3d.Empty; }
    }

    // time per phase of the uniform contour and the simplification (upper bound for a sparse construction)
    static void Phases()
    {
        Console.WriteLine("| shape | N | tol | sample | crossings | hermite | vertices | intersections | mesh | simplify | total ms |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (string shape in new[] { "csg", "bracket", "sphere" })
            foreach (int N in new[] { 64, 128, 256 }) {
                double ext = Shapes.Extent(shape);
                var f = Shapes.Get(shape);
                var ad = new AdaptiveDualContouring() { Implicit = new ImplicitFunctionDelegate(f), InsideIsLess = true, CellSize = 2 * ext / N, Tolerance = 0.25,
                    Bounds = new AxisAlignedBox3d(new Vector3d(-ext, -ext, -ext), new Vector3d(ext, ext, ext)) };
                ad.Generate();
                var sw = Stopwatch.StartNew(); ad.Generate(); long total = sw.ElapsedMilliseconds;
                var ph = ad.BasePhaseMilliseconds;
                Console.WriteLine($"| {shape} | {N} | 0.25 | {ph["sample"]} | {ph["crossings"]} | {ph["hermite"]} | {ph["vertices"]} | {ph["intersections"]} | {ph["mesh"]} | {ad.PhaseMilliseconds["simplify"]} | {total} |");
            }
    }

    // triangle quality: smallest angle (degrees) of each triangle; share below 5 and 10 degrees, 1st percentile, mean
    static void Quality()
    {
        Console.WriteLine("| shape | N | tolerance | triangles | min angle p01 | min angle mean | below 5 deg | below 10 deg | max angle above 150 deg |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
        foreach (string shape in new[] { "csg", "bracket", "sphere", "torus" })
            foreach (int N in new[] { 64, 128 }) {
                double ext = Shapes.Extent(shape);
                var f = Shapes.Get(shape);
                foreach (double tol in new[] { 0.0, 0.25, 0.5 }) {
                    var ad = new AdaptiveDualContouring() { Implicit = new ImplicitFunctionDelegate(f), InsideIsLess = true, CellSize = 2 * ext / N, Tolerance = tol,
                        Bounds = new AxisAlignedBox3d(new Vector3d(-ext, -ext, -ext), new Vector3d(ext, ext, ext)) };
                    ad.Generate();
                    var mins = new List<double>(); int obtuse = 0;
                    foreach (int t in ad.Mesh.TriangleIndices()) {
                        Index3i it = ad.Mesh.GetTriangle(t);
                        Vector3d a = ad.Mesh.GetVertex(it.a), b = ad.Mesh.GetVertex(it.b), c = ad.Mesh.GetVertex(it.c);
                        double mn = 180, mx = 0;
                        foreach (var ang in new[] { Ang(a, b, c), Ang(b, c, a), Ang(c, a, b) }) { mn = Math.Min(mn, ang); mx = Math.Max(mx, ang); }
                        mins.Add(mn); if (mx > 150) obtuse++;
                    }
                    mins.Sort();
                    int n = mins.Count;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "| {0} | {1} | {2} | {3} | {4:F1} | {5:F1} | {6:P1} | {7:P1} | {8:P1} |",
                        shape, N, tol, n, mins[n / 100], mins.Average(), (double)mins.Count(x => x < 5) / n, (double)mins.Count(x => x < 10) / n, (double)obtuse / n));
                }
            }
    }
    static double Ang(Vector3d p, Vector3d q, Vector3d r)
    {
        Vector3d u = q - p, v = r - p;
        double d = u.Length * v.Length;
        return d == 0 ? 0 : Math.Acos(MathUtil.Clamp(u.Dot(v) / d, -1, 1)) * 180 / Math.PI;
    }
}
