// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

// MeshRobustRepair on small inputs that fall through to the alpha wrap fallback (#636, README.md): the 62-byte MeshFuzzPilot repro
// (an octahedron whose vertex 3 sits on vertex 2) and jittered octahedra / tetrahedra from a fixed seed. Each runs with
// SmallInputWrapTriangles = 0 (the diag / 300 wrap of every size: "old") and with the default ("new"). Distances are relative to the
// bounding box diagonal of the input: result vertices to the input, input vertices to the result.
static class Program
{
    static readonly double[][] Oct = { new[] { 1.0, 0, 0 }, new[] { -1.0, 0, 0 }, new[] { 0.0, 1, 0 }, new[] { 0.0, -1, 0 }, new[] { 0.0, 0, 1 }, new[] { 0.0, 0, -1 } };
    static readonly int[][] OctT = { new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };
    static readonly double[][] Tet = { new[] { 0.0, 0, 0 }, new[] { 1.0, 0, 0 }, new[] { 0.0, 1, 0 }, new[] { 0.0, 0, 1 } };
    static readonly int[][] TetT = { new[] { 0, 2, 1 }, new[] { 0, 1, 3 }, new[] { 0, 3, 2 }, new[] { 1, 2, 3 } };

    static DMesh3 Mk(double[][] v, int[][] t, double k = 1)
    {
        var m = new DMesh3();
        foreach (var p in v) m.AppendVertex(new Vector3d(p[0] * k, p[1] * k, p[2] * k));
        foreach (var x in t) m.AppendTriangle(x[0], x[1], x[2]);
        return m;
    }

    // the repro of the issue, and the jittered inputs: for index i of Random(5) one or two vertices of an octahedron or tetrahedron are
    // moved to a grid point of {-1, -0.5, ..., 1}^3; the six that reach the wrap in the first 300 are listed by name
    static IEnumerable<(string, DMesh3)> Inputs()
    {
        var repro = Oct.Select(p => p.ToArray()).ToArray();
        repro[3] = new[] { 0.0, 1, 0 };
        yield return ("62 B repro", Mk(repro, OctT, 2.407412430484045E-35));
        var rnd = new Random(5);
        var pick = new HashSet<string> { "tet-jitter-6", "oct-jitter-22", "tet-jitter-24", "tet-jitter-25", "tet-jitter-28", "tet-jitter-34" };
        for (int i = 0; i < 300; i++) {
            bool oct = rnd.Next(2) == 0;
            var v = (oct ? Oct : Tet).Select(p => p.ToArray()).ToArray();
            int k = 1 + rnd.Next(2);
            for (int j = 0; j < k; j++) {
                int vi = rnd.Next(v.Length);
                v[vi] = new[] { (rnd.Next(5) - 2) * 0.5, (rnd.Next(5) - 2) * 0.5, (rnd.Next(5) - 2) * 0.5 };
            }
            string name = (oct ? "oct" : "tet") + "-jitter-" + i;
            if (pick.Contains(name))
                yield return (name, Mk(v, oct ? OctT : TetT));
        }
    }

    static double MaxDistance(DMesh3 from, DMesh3 to)
    {
        var tree = new DMeshAABBTree3(to, true);
        tree.Build();
        return Math.Sqrt(from.VertexIndices().Max(v => MeshQueries.TriDistanceSqr(to, tree.FindNearestTriangle(from.GetVertex(v)), from.GetVertex(v))));
    }

    static void Main()
    {
        Console.WriteLine("input,mode,seconds,ok,wrap,wrap_triangles,result_triangles,alpha_rel,offset_rel,result_to_input_rel,input_to_result_rel");
        foreach (var (name, input) in Inputs()) {
            double diag = input.GetBounds().DiagonalLength;
            foreach (bool old in new[] { true, false }) {
                var r = new MeshRobustRepair(new DMesh3(input)) { SmallInputWrapTriangles = old ? 0 : 64 };
                var sw = Stopwatch.StartNew();
                bool ok = r.Apply();
                double s = sw.Elapsed.TotalSeconds;
                double a = double.NaN, b = double.NaN;
                if (r.Mesh != null && r.Mesh.TriangleCount > 0) { a = MaxDistance(r.Mesh, input) / diag; b = MaxDistance(input, r.Mesh) / diag; }
                Console.WriteLine($"{name},{(old ? "old" : "new")},{s:F2},{ok},{r.UsedAlphaWrap},{r.AlphaWrapTriangles},{r.Mesh?.TriangleCount},{(r.AlphaWrap != null ? r.AlphaWrap.UsedAlpha / diag : double.NaN):G3},{(r.AlphaWrap != null ? r.AlphaWrap.UsedOffset / diag : double.NaN):G3},{a:G3},{b:G3}");
            }
        }
    }
}
