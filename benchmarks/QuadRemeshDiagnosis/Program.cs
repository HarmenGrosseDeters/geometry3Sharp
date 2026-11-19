// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Stage 2 quad remeshing diagnosis and lever measurement (task of 2026-11-19, benchmark code only; no library change).
// Runs lab copies of PositionField and QuadRemesher (Lab/, same algorithm, plus options for the levers) on the 8 shapes of
// QuadRemeshBenchmark and
//   mode=diag   classifies every irregular vertex and every non-quad face of the output by its cause (see Classify),
//   mode=levers runs the levers one at a time and prints one row per shape and lever.
//
//   dotnet run -c Release --project benchmarks/QuadRemeshDiagnosis -- mode=diag|levers [only=<regex>] [lever=<regex>] [big=<n>]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using gs;
using gs.Lab;

static class Program
{
    class Fn : BoundedImplicitFunction3d
    {
        public Func<Vector3d, double> F; public AxisAlignedBox3d B;
        public double Value(ref Vector3d p) { return F(p); }
        public AxisAlignedBox3d Bounds() { return B; }
    }
    class Shape { public string Name; public Func<Vector3d, double> F; public double S; public double Noise; public Func<DMesh3, DMesh3> Post; }
    class Lever
    {
        public string Name; public Action<QuadRemesherLab> Apply; public double Ratio = 0; public double Scale = 0;
    }

    static string F(double x, string fmt) { return x.ToString(fmt, CultureInfo.InvariantCulture); }

    static List<Shape> Shapes()
    {
        Func<Vector3d, double> sphere = p => p.Length - 1;
        Func<Vector3d, double> torus = p => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 1; return Math.Sqrt(q * q + p.z * p.z) - 0.35; };
        Func<Vector3d, double> bumpy = p => p.Length - 1 - 0.08 * Math.Sin(4 * p.x) * Math.Sin(4 * p.y) * Math.Sin(4 * p.z);
        Vector3d ext = new Vector3d(1, 0.7, 0.5); double fr = 0.15;
        Func<Vector3d, double> rbox = p => {
            Vector3d q = new Vector3d(Math.Abs(p.x) - (ext.x - fr), Math.Abs(p.y) - (ext.y - fr), Math.Abs(p.z) - (ext.z - fr));
            Vector3d qp = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return qp.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0) - fr;
        };
        Func<DMesh3, DMesh3> cap = m => {
            var remove = new List<int>();
            foreach (int t in m.TriangleIndices()) if (m.GetTriCentroid(t).z > 0.5) remove.Add(t);
            MeshEditor.RemoveTriangles(m, remove, true);
            return m;
        };
        return new List<Shape> {
            new Shape { Name = "sphere", F = sphere, S = 0.1 },
            new Shape { Name = "torus", F = torus, S = 0.1 },
            new Shape { Name = "bumpy", F = bumpy, S = 0.06 },
            new Shape { Name = "filleted-box", F = rbox, S = 0.08 },
            new Shape { Name = "noisy-sphere", F = sphere, S = 0.1, Noise = 0.2 },
            new Shape { Name = "sphere-cap", F = sphere, S = 0.1, Post = cap },
            new Shape { Name = "open-patch", F = null, S = 0.04 },
        };
    }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        string mode = A.ContainsKey("mode") ? A["mode"] : "diag";
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        var leverRx = A.ContainsKey("lever") ? new Regex(A["lever"]) : null;
        double ratio = 0.5;
        var shapes = Shapes();
        if (A.ContainsKey("big")) {
            double n = double.Parse(A["big"], CultureInfo.InvariantCulture);
            double h = Math.Sqrt(2 * Math.PI / (0.866 * n) * 2);
            shapes.Add(new Shape { Name = "big-sphere", F = shapes[0].F, S = 2 * h });
        }
        var levers = Levers();
        if (mode == "diag") levers = levers.Where(l => leverRx != null ? l.Name == A["lever"] : l.Name == "base").ToList();
        else if (leverRx != null) levers = levers.Where(l => l.Name == "base" || leverRx.IsMatch(l.Name)).ToList();

        if (mode == "solver") {
            foreach (var sh in shapes) {
                if (only != null && !only.IsMatch(sh.Name)) continue;
                double s = sh.S;
                DMesh3 input = sh.Name == "open-patch" ? OpenPatch((int)Math.Round(1.0 / (s * ratio))) : MakeInput(sh, s * ratio, sh.Noise);
                var cf = new CrossField(input); cf.Compute();
                foreach (var (us, ur, gn, lam, mx, mr) in new[] { (true, false, 10, 1e-2, 3.0, 1.0), (true, false, 30, 1e-2, 3.0, 1.0), (true, false, 30, 1e-3, 3.0, 1.0), (true, true, 30, 1e-2, 3.0, 90.0), (true, true, 60, 1e-3, 3.0, 90.0) }) {
                    Vector3d[] qo; double[] sc; string rep;
                    IntegrableField.Solve(input, cf, s, us, ur, gn, lam, mx, mr, out qo, out sc, out rep);
                    Console.WriteLine($"{sh.Name} scale={us} rot={ur} gn={gn} lam={lam} max={mx}: {rep}");
                }
            }
            return 0;
        }
        if (mode == "levers") {
            Console.WriteLine("| shape | lever | faces | quads % | tris | 5-6 | irreg. V | irreg / sing. | boundary V | manifold | corner p95 | Hausdorff out->in / s | t position (s) | t extract (s) | collapsed / val2 / dropped |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        }
        if (mode == "diag") {
            Console.WriteLine("| shape | irreg. V | sing. faces | irregular by cause F / P / B / M / X | non-quad faces (quads %) | non-quad by cause F / P / B / M / X | defect tris F (clusters) / P (clusters) / M | irregular left if P, M, X fixed (gate) | irregular left if only P fixed | quads % if P, M, X fixed | quads % if only P fixed | valences by cause |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        }
        foreach (var sh in shapes) {
            if (only != null && !only.IsMatch(sh.Name)) continue;
            double s = sh.S;
            DMesh3 input0 = sh.Name == "open-patch" ? OpenPatch((int)Math.Round(1.0 / (s * ratio))) : MakeInput(sh, s * ratio, sh.Noise);
            var cf0 = new CrossField(input0); cf0.Compute();
            if (!cf0.Success) { Console.WriteLine("| " + sh.Name + " | cross field failed |"); continue; }
            foreach (var lv in levers) {
                DMesh3 input = input0; CrossField cf = cf0; double sUse = s;
                if (lv.Scale > 0) sUse = s * lv.Scale;
                if (lv.Ratio > 0 || lv.Scale > 0) {
                    double rr = lv.Ratio > 0 ? lv.Ratio : ratio;
                    input = sh.Name == "open-patch" ? OpenPatch((int)Math.Round(1.0 / (sUse * rr))) : MakeInput(sh, sUse * rr, sh.Noise);
                    cf = new CrossField(input); cf.Compute();
                }
                var qr = new QuadRemesherLab(input) { Field = cf, TargetEdgeLength = sUse };
                lv.Apply?.Invoke(qr);
                qr.Compute();
                if (!qr.Success) { Console.WriteLine("| " + sh.Name + " | " + lv.Name + " | " + qr.Status + " " + qr.Error + " |"); continue; }
                PolyMeshMetrics m = PolyMeshMetrics.Compute(qr.Result);
                double haus = double.NaN;
                int[] map; var skipped = new List<int>();
                DMesh3 tri = qr.Result.ToDMesh3(out map, skipped);
                if (tri.TriangleCount > 0) { var hd = new MeshHausdorffDistance(tri, input) { Symmetric = false, RelativeTolerance = 1e-3 }; hd.Compute(); haus = hd.UpperBound / sUse; }
                string sing = cf.SingularFaces.Count == 0 ? "-" : F((double)m.IrregularVertices / cf.SingularFaces.Count, "F1");
                if (mode == "levers") {
                    Console.WriteLine($"| {sh.Name} | {lv.Name} | {m.Faces} | {F(100.0 * m.QuadFraction, "F1")} | {m.Triangles} | {m.Pentagons + m.Hexagons} | {m.IrregularVertices} | {sing} ({cf.SingularFaces.Count}) | {m.BoundaryVertices} | {(m.IsManifold ? "yes" : "NO")} | {F(PolyMeshMetrics.Percentile(m.CornerDeviations, 0.95), "F1")} | {F(haus, "F2")} | {F(qr.PositionSeconds, "F2")} | {F(qr.ExtractSeconds, "F2")} | {qr.CollapsedEdges} / {qr.RemovedValence2} / {qr.DroppedFaces} |");
                } else {
                    Diagnose.Run(sh.Name, input, cf, qr, m, sUse);
                }
            }
        }
        return 0;
    }

    static List<Lever> Levers()
    {
        var l = new List<Lever>();
        l.Add(new Lever { Name = "base", Apply = q => { } });
        l.Add(new Lever { Name = "fix-offsets", Apply = q => q.FixOffsets = true });
        l.Add(new Lever { Name = "no-cleanup", Apply = q => q.Cleanup = false });
        l.Add(new Lever { Name = "sweeps-5", Apply = q => q.Sweeps = 5 });
        l.Add(new Lever { Name = "sweeps-100", Apply = q => q.Sweeps = 100 });
        l.Add(new Lever { Name = "finest-sweeps-100", Apply = q => q.FinestSweeps = 100 });
        l.Add(new Lever { Name = "weight-1/len", Apply = q => q.Weighting = 1 });
        l.Add(new Lever { Name = "weight-len", Apply = q => q.Weighting = 2 });
        l.Add(new Lever { Name = "round-at-end", Apply = q => q.RoundEachSweep = false });
        foreach (double c in new[] { 0.3, 0.5, 0.6, 0.7 }) { double cc = c; l.Add(new Lever { Name = "collapse-" + F(cc, "0.0"), Apply = q => q.CollapseLength = cc }); }
        foreach (double mm in new[] { 0.3, 0.75, 1.0 }) { double m = mm; l.Add(new Lever { Name = "mismatch-" + F(m, "0.00"), Apply = q => q.MismatchLimit = m }); }
        foreach (int r in new[] { 1, 3 }) { int rr = r; l.Add(new Lever { Name = "couple-" + rr, Apply = q => { q.CoupleRounds = rr; q.FixOffsets = true; } }); }
        l.Add(new Lever { Name = "couple-3-dev45", Apply = q => { q.CoupleRounds = 3; q.CoupleMaxDev = 45; q.FixOffsets = true; } });
        foreach (var (rn, eta, mx) in new[] { (5, 0.5, 0.2), (10, 0.5, 0.4) }) { int rr = rn; double ee = eta, xx = mx; l.Add(new Lever { Name = $"free-scale-{rr}x-max{xx:0.0}", Apply = q => { q.ScaleRounds = rr; q.ScaleEta = ee; q.ScaleMax = xx; q.FixOffsets = true; } }); }
        foreach (var (nm, us, ur, mx, mr, gn, lam) in new[] {
            ("int2-scale-10", true, false, 3.0, 0.0, 10, 1e-2), ("int2-scale-30", true, false, 3.0, 0.0, 30, 1e-2), ("int2-both-30", true, true, 3.0, 45.0, 30, 1e-2),
            ("int2-scale-30-max0.5", true, false, 0.5, 0.0, 30, 1e-2), ("int2-both-30-max0.5-rot15", true, true, 0.5, 15.0, 30, 1e-2) }) {
            string nn = nm; bool a = us, b = ur; double m1 = mx, m2 = mr, ll = lam; int g = gn;
            l.Add(new Lever { Name = nn, Apply = q => {
                Vector3d[] qo; double[] sc; string rep;
                IntegrableField.Solve(q.Mesh, q.Field, q.TargetEdgeLength, a, b, g, ll, m1 > 0 ? m1 : 1, m2 > 0 ? m2 : 1, out qo, out sc, out rep);
                if (b) q.QOverride = qo; if (a) q.ScaleField = sc; q.FixOffsets = true;
                Console.Error.WriteLine("  " + nn + ": " + rep);
            } });
        }
        l.Add(new Lever { Name = "ratio-0.33", Ratio = 0.33, Apply = q => { } });
        l.Add(new Lever { Name = "scale-0.5", Scale = 0.5, Apply = q => { } });
        l.Add(new Lever { Name = "scale-2", Scale = 2, Apply = q => { } });
        return l;
    }

    static DMesh3 OpenPatch(int n)
    {
        var m = new DMesh3();
        int[,] ids = new int[n + 1, n + 1];
        for (int j = 0; j <= n; ++j)
            for (int i = 0; i <= n; ++i) {
                double x = (double)i / n, y = (double)j / n;
                ids[i, j] = m.AppendVertex(new Vector3d(x, y, 0.15 * Math.Sin(3 * x) * Math.Cos(2 * y)));
            }
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
            }
        return m;
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
        if (sh.Post != null) m = sh.Post(m);
        return m;
    }
}
