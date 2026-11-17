#pragma warning disable CS0618 // the benchmark measures the deprecated float MeshMeshCut
// Cost of MeshMeshCut.RefuseContact (the exact look for contact before the cut): Compute() of a sphere cut by a second sphere (a transversal cut,
// so the look finds nothing and the cut runs), with the look off (RefuseContact = false: the code path of the base library) and on, in ms, median of
// the runs. Usage: MeshMeshCutContactBenchmark [resolution] [runs]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

static class Program
{
    static DMesh3 Sphere(int n, Vector3d center, double radius)
    {
        var gen = new Sphere3Generator_NormalizedCube() { EdgeVertices = n, Radius = radius };
        DMesh3 m = new DMesh3(gen.Generate().MakeDMesh());
        foreach (int v in m.VertexIndices())
            m.SetVertex(v, m.GetVertex(v) + center);
        return m;
    }

    static double Time(DMesh3 target, DMesh3 cut, bool refuse, out int triangles, out MeshMeshCut.CutStatus status)
    {
        DMesh3 t = new DMesh3(target);
        var op = new MeshMeshCut() { Target = t, CutMesh = cut, RefuseContact = refuse };
        var sw = Stopwatch.StartNew();
        op.Compute();
        sw.Stop();
        triangles = t.TriangleCount;
        status = op.Status;
        return sw.Elapsed.TotalMilliseconds;
    }

    static void Main(string[] args)
    {
        int n = args.Length > 0 ? int.Parse(args[0]) : 40, runs = args.Length > 1 ? int.Parse(args[1]) : 9;
        DMesh3 target = Sphere(n, Vector3d.Zero, 1.0), cut = Sphere(n, new Vector3d(0.31, 0.17, 0.23), 0.9);
        Console.WriteLine("runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        Console.WriteLine($"target {target.TriangleCount} triangles, cut mesh {cut.TriangleCount} triangles, {runs} runs");
        Console.WriteLine();
        Console.WriteLine("| RefuseContact | Compute() median (ms) | min (ms) | result triangles | status |");
        Console.WriteLine("|---|---|---|---|---|");
        Time(target, cut, false, out _, out _); Time(target, cut, true, out _, out _);       // warm up
        foreach (bool refuse in new[] { false, true }) {
            var ms = new List<double>();
            int tri = 0; var st = MeshMeshCut.CutStatus.Ok;
            for (int i = 0; i < runs; ++i)
                ms.Add(Time(target, cut, refuse, out tri, out st));
            ms.Sort();
            Console.WriteLine($"| {(refuse ? "true" : "false (base path)")} | {ms[ms.Count / 2]:F1} | {ms[0]:F1} | {tri} | {st} |");
        }
    }
}
