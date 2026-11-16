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
}
