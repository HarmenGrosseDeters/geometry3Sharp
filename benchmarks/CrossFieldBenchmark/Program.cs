// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Cross field benchmark (docs/design/QUAD_REMESHING.md stage 1): gs.CrossField on procedural closed shapes (MarchingCubes, then
// 20 Remesher passes at edge h), the smoothest and the curvature-aligned field. One Markdown table row per shape, field and size.
//
//   dotnet run -c Release --project benchmarks/CrossFieldBenchmark -- [only=<regex>] [h=<edge>] [s=<alignment>] [reps=3] [tol=1e-4] [maxit=200]
//
// h overrides the edge length of every shape (default: per shape, about 6k vertices); s is CurvatureAlignment of the aligned rows.
// The time is the best of reps runs of Compute() (the input mesh is built once), single process, after one untimed warm-up.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using gs;

static class Program
{
    class Fn : BoundedImplicitFunction3d
    {
        public Func<Vector3d, double> F; public AxisAlignedBox3d B;
        public double Value(ref Vector3d p) { return F(p); }
        public AxisAlignedBox3d Bounds() { return B; }
    }
    class Shape { public string Name; public Func<Vector3d, double> F; public double H; public double Noise; public int Chi; public int Ideal; }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        double hOverride = A.ContainsKey("h") ? double.Parse(A["h"], CultureInfo.InvariantCulture) : 0;
        double sAlign = A.ContainsKey("s") ? double.Parse(A["s"], CultureInfo.InvariantCulture) : 1;
        int reps = A.ContainsKey("reps") ? int.Parse(A["reps"]) : 3;
        double tol = A.ContainsKey("tol") ? double.Parse(A["tol"], CultureInfo.InvariantCulture) : 1e-4;
        int maxit = A.ContainsKey("maxit") ? int.Parse(A["maxit"]) : 200;
        Func<Vector3d, double> sphere = p => p.Length - 1;
        Func<Vector3d, double> torus = p => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 1; return Math.Sqrt(q * q + p.z * p.z) - 0.35; };
        Func<Vector3d, double> bumpy = p => p.Length - 1 - 0.08 * Math.Sin(4 * p.x) * Math.Sin(4 * p.y) * Math.Sin(4 * p.z);
        Vector3d ext = new Vector3d(1, 0.7, 0.5); double fr = 0.15;
        Func<Vector3d, double> rbox = p => {
            Vector3d q = new Vector3d(Math.Abs(p.x) - (ext.x - fr), Math.Abs(p.y) - (ext.y - fr), Math.Abs(p.z) - (ext.z - fr));
            Vector3d qp = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return qp.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0) - fr;
        };
        var shapes = new List<Shape> {
            new Shape { Name = "sphere", F = sphere, H = 0.05, Chi = 2, Ideal = 8 },
            new Shape { Name = "torus", F = torus, H = 0.05, Chi = 0, Ideal = 0 },
            new Shape { Name = "bumpy", F = bumpy, H = 0.03, Chi = 2, Ideal = 8 },
            new Shape { Name = "filleted-box", F = rbox, H = 0.04, Chi = 2, Ideal = 8 },
            new Shape { Name = "noisy-sphere", F = sphere, H = 0.05, Noise = 0.2, Chi = 2, Ideal = 8 },
            new Shape { Name = "noisy-torus", F = torus, H = 0.05, Noise = 0.2, Chi = 0, Ideal = 0 },
        };
        Console.WriteLine("| shape | field | V | status | iterations | converged | eigenvalue | singular faces (index x count) | ideal | sum index (chi) | time (s) |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        {
            var w = MakeInput(shapes[0], 0.2, 0);
            new CrossField(w).Compute();
        }
        foreach (var sh in shapes) {
            if (only != null && !only.IsMatch(sh.Name)) continue;
            DMesh3 m = MakeInput(sh, hOverride > 0 ? hOverride : sh.H, sh.Noise);
            foreach (double s in new[] { 0.0, sAlign }) {
                var cf = new CrossField(m) { CurvatureAlignment = s, Tolerance = tol, MaxIterations = maxit };
                double best = double.MaxValue;
                for (int r = 0; r < reps; ++r) {
                    var sw = Stopwatch.StartNew();
                    cf.Compute();
                    best = Math.Min(best, sw.Elapsed.TotalSeconds);
                }
                string hist = cf.SingularFaces.Count + " (" + string.Join(", ", cf.IndexHistogram.Select(kv => (kv.Key > 0 ? "+" : "") + kv.Key + "/4 x" + kv.Value)) + ")";
                Console.WriteLine("| " + sh.Name + " | " + (s == 0 ? "smoothest" : "aligned s=" + s.ToString(CultureInfo.InvariantCulture)) + " | " + m.VertexCount + " | " + cf.Status + " | " + cf.Iterations + " | " + cf.Converged
                    + " | " + cf.Eigenvalue.ToString("G3", CultureInfo.InvariantCulture) + " | " + hist + " | " + sh.Ideal + " | " + cf.IndexSum.ToString("F2", CultureInfo.InvariantCulture) + " (" + sh.Chi + ") | " + best.ToString("F2", CultureInfo.InvariantCulture) + " |");
            }
        }
        return 0;
    }

    static DMesh3 MakeInput(Shape sh, double h, double noise)
    {
        var B = new AxisAlignedBox3d(new Vector3d(-1.6, -1.6, -1.6), new Vector3d(1.6, 1.6, 1.6));
        var mc = new MarchingCubes { Implicit = new Fn { F = sh.F, B = B }, Bounds = B, CubeSize = h * 0.5, ParallelCompute = false, RootMode = MarchingCubes.RootfindingModes.Bisection };
        mc.Generate();
        DMesh3 m = mc.Mesh;
        DMesh3 src = new DMesh3(m);
        var r = new Remesher(m) { SmoothSpeedT = 0.5 };
        r.SetProjectionTarget(MeshProjectionTarget.Auto(src));
        r.SetTargetEdgeLength(h);
        for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
        m = new DMesh3(m, true);
        if (noise > 0) {
            var rnd = new Random(1);
            MeshNormals.QuickCompute(m);
            foreach (int v in m.VertexIndices()) {
                Vector3d n = (Vector3d)m.GetVertexNormal(v);
                m.SetVertex(v, m.GetVertex(v) + n * ((rnd.NextDouble() * 2 - 1) * noise * h));
            }
        }
        return m;
    }
}
