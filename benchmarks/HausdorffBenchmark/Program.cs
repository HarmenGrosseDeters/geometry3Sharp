// MeshHausdorffDistance: the default upper bound against CertifiedUpper (#742). Procedural meshes, fixed seeds; markdown table:
// bounds, subdivisions and the median time of N runs after a warm-up, default -> certified.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static class Program
{
    static DMesh3 Sphere(int ev, double r, Vector3d off)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube { EdgeVertices = ev, Radius = 1 }.Generate().MakeDMesh();
        foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, r * m.GetVertex(v) + off);
        return m;
    }

    static DMesh3 Bumpy(int seed, int ev)
    {
        var rnd = new Random(seed);
        DMesh3 m = Sphere(ev, 1, Vector3d.Zero);
        foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, m.GetVertex(v) * (1 + 0.05 * rnd.NextDouble()));
        return m;
    }

    static DMesh3 Tri(Vector3d a, Vector3d b, Vector3d c)
    {
        var m = new DMesh3();
        m.AppendTriangle(m.AppendVertex(a), m.AppendVertex(b), m.AppendVertex(c));
        return m;
    }

    static int Runs = 7;

    static (MeshHausdorffDistance h, double ms) Measure(DMesh3 a, DMesh3 b, bool sym, double tol, bool certified)
    {
        var times = new List<double>();
        MeshHausdorffDistance h = null;
        for (int r = 0; r <= Runs; ++r) {
            var sw = Stopwatch.StartNew();
            h = new MeshHausdorffDistance(a, b) { Symmetric = sym, Tolerance = tol, CertifiedUpper = certified };
            h.Compute();
            if (r > 0) times.Add(sw.Elapsed.TotalMilliseconds);     // (run 0: warm-up)
        }
        times.Sort();
        return (h, times[times.Count / 2]);
    }

    static void Row(string name, DMesh3 a, DMesh3 b, bool sym, double tol = 0)
    {
        var d = Measure(a, b, sym, tol, false);
        var c = Measure(a, b, sym, tol, true);
        Console.WriteLine($"| {name} | {(sym ? "yes" : "no")} | {d.h.LowerBound:R} | {d.h.UpperBound:R} → {c.h.UpperBound:R} | " +
            $"{d.h.Subdivisions} → {c.h.Subdivisions} | {d.ms:F1} → {c.ms:F1} | {100 * (c.ms / d.ms - 1):+0;-0}% |");
    }

    static void Main(string[] args)
    {
        if (args.Length > 0) Runs = int.Parse(args[0]);
        Console.WriteLine($"| case | sym | lower (default) | upper default → certified | subdivisions | ms, median of {Runs} | time |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        foreach (bool sym in new[] { true, false }) {
            Row("spheres6", Sphere(6, 1, Vector3d.Zero), Sphere(6, 1.01, Vector3d.Zero), sym);
            Row("spheres6-vs-20", Sphere(6, 1, Vector3d.Zero), Sphere(20, 1, Vector3d.Zero), sym);
            Row("bumpy5-6", Bumpy(5, 12), Bumpy(6, 8), sym);
            Row("bumpy7-8 tol1e-5", Bumpy(7, 10), Bumpy(8, 14), sym, 1e-5);
            Row("utm spheres", Sphere(8, 50, new Vector3d(632117.25, 5712043.5, 113.75)), Sphere(10, 50.3, new Vector3d(632117.25, 5712043.5, 113.75)), sym);
            var big = Bumpy(9, 30); var red = new DMesh3(big); new Reducer(red).ReduceToTriangleCount(big.TriangleCount / 8);
            Row("reduced 1/8", red, big, sym);
            var big2 = Bumpy(11, 60); var red2 = new DMesh3(big2); new Reducer(red2).ReduceToTriangleCount(big2.TriangleCount / 8);
            Row("reduced 1/8 of 21k", red2, big2, sym);
            Row("cube-vs-sphere", new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.7, 0.7, 0.7)) }.Generate().MakeDMesh(), Sphere(10, 1, Vector3d.Zero), sym);
            Row("offset 1e3 spheres", Sphere(6, 1, new Vector3d(1000, 0, 0)), Sphere(6, 1.01, new Vector3d(1000, 0, 0)), sym);
        }
        // the case the option is for: a small triangle 1e-9 above a tilted unit triangle (the default upper bound can be below the
        // exact distance there)
        var rnd = new Random(1);
        Vector3d v0 = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()), v1 = v0 + new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()),
            v2 = v0 + new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
        Vector3d n = (v1 - v0).Cross(v2 - v0).Normalized;
        Vector3d q(double s, double t) => v0 + s * (v1 - v0) + t * (v2 - v0) + 1e-9 * n;
        Row("1e-9 above a triangle", Tri(q(0.3, 0.3), q(0.32, 0.3), q(0.3, 0.32)), Tri(v0, v1, v2), false);
    }
}
