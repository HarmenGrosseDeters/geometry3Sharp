// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

// The size estimate of the alpha wrap on open input and the budget WrapMaxEstimatedTriangles (issue #1113, README.md): the reproducer
// (two overlapping unwelded spheres, 12 edge vertices per cube side, 1.2 apart in x, the second shifted 0.3 in z, the axis caps whose
// centroid direction is within 0.5 rad of an axis removed), the same two spheres closed, and a flat open sheet. First the wrap alone, then through
// MeshRobustRepair at the budgets of the table. Columns: seconds of Apply(), AlphaWrap.EstimatedTriangles (before the refinement),
// raw wrap triangles, the ratio raw / budget (raw / estimate without a budget), the alpha used relative to the diagonal, the result triangles.
//     dotnet run --project benchmarks/OpenSoupWrapBenchmark -c Release [-- --quick]   (--quick: the first two budgets of each input only)
static class Program
{
    static DMesh3 Sphere(Vector3d center)
    {
        var g = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 12 };
        g.Generate();
        DMesh3 m = g.MakeDMesh();
        foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, m.GetVertex(v) + center);
        return m;
    }

    // a soup (own vertices per triangle) of the triangles of the meshes, without the caps if cut
    static DMesh3 Soup(IEnumerable<DMesh3> meshes, bool cut)
    {
        var soup = new DMesh3();
        double lim = Math.Cos(0.5);
        foreach (DMesh3 m in meshes)
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
                if (cut) {
                    Vector3d d = ((a + b + c) / 3 - m.GetBounds().Center).Normalized;
                    if (Math.Max(Math.Abs(d.x), Math.Max(Math.Abs(d.y), Math.Abs(d.z))) > lim) continue;
                }
                soup.AppendTriangle(soup.AppendVertex(a), soup.AppendVertex(b), soup.AppendVertex(c));
            }
        return soup;
    }

    static DMesh3 Sheet(int n, double side)
    {
        var m = new DMesh3();
        for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d(i * side / n, j * side / n, 0));
        for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
            int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
            m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
        }
        return m;
    }

    static void Main(string[] args)
    {
        bool quick = args.Contains("--quick");
        Vector3d shift = new Vector3d(1.2, 0, 0.3);
        var inputs = new List<(string, DMesh3)> {
            ("two open spheres (#1113)", Soup(new[] { Sphere(Vector3d.Zero), Sphere(shift) }, true)),
            ("two closed spheres", Soup(new[] { Sphere(Vector3d.Zero), Sphere(shift) }, false)),
            ("open sheet 4 x 4", Sheet(60, 4)),
        };
        // 1. the wrap alone (MeshAlphaWrap, relative alpha 100, offset 1/1000), no budget: the estimate against the raw wrap
        Console.WriteLine("input,estimate,raw_triangles,raw_over_estimate,seconds");
        foreach (var (name, input) in inputs) {
            var w = new MeshAlphaWrap(input) { RelativeAlpha = 100, RelativeOffset = 1000 };
            var sw = Stopwatch.StartNew();
            bool ok = w.Compute();
            Console.WriteLine($"{name},{w.EstimatedTriangles:F0},{w.Result?.TriangleCount},{(w.Result?.TriangleCount / w.EstimatedTriangles):F2},{sw.Elapsed.TotalSeconds:F1}");
        }
        // 2. MeshRobustRepair at library defaults (alpha diagonal / 300) with WrapMaxEstimatedTriangles: only the open inputs reach the wrap
        Console.WriteLine("input,budget,seconds,estimate,raw_triangles,raw_over_budget_or_estimate,alpha_rel,result_triangles");
        foreach (var (name, input) in inputs.Where(i => i.Item1 != "two closed spheres"))
            foreach (int budget in new[] { 0, 200000, 100000, 50000 }.Take(quick ? 2 : 4)) {
                var r = new MeshRobustRepair(new DMesh3(input)) { WrapMaxEstimatedTriangles = budget };
                var sw = Stopwatch.StartNew();
                bool ok = r.Apply();
                double s = sw.Elapsed.TotalSeconds;
                double diag = input.GetBounds().DiagonalLength;
                double est = r.AlphaWrap != null ? r.AlphaWrap.EstimatedTriangles : double.NaN;
                Console.WriteLine($"{name},{budget},{s:F1},{est:F0},{r.AlphaWrapTriangles},{(r.AlphaWrapTriangles / (budget > 0 ? budget : est)):F2},{(r.AlphaWrap != null ? r.AlphaWrap.UsedAlpha / diag : double.NaN):G3},{r.Mesh?.TriangleCount}");
            }
    }
}
