// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Diagnostics;
using g3;

// Time per ray query (README.md): DMeshAABBTree3.FindNearestHitTriangle and IsInside on a sphere and a terrain,
// and the single-triangle IntrRay3Triangle3.Intersects on random triangles. Procedural inputs, fixed seeds; the
// checksums (hit ids, inside counts, hit counts and the sum of t) must agree between two builds whose decisions
// agree.
static class Program
{
    static DMesh3 Sphere()
    {
        var gen = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 130 };
        return gen.Generate().MakeDMesh();
    }

    static DMesh3 Terrain()
    {
        int n = 224; var m = new DMesh3();
        var rnd = new Random(3);
        for (int j = 0; j <= n; ++j)
            for (int i = 0; i <= n; ++i)
                m.AppendVertex(new Vector3d(i, j, 3 * Math.Sin(i * 0.1) * Math.Cos(j * 0.13) + rnd.NextDouble() * 0.2));
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
            }
        return m;
    }

    static Vector3d RandDir(Random r)
    {
        Vector3d d;
        do d = new Vector3d(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);
        while (d.LengthSquared > 1 || d.LengthSquared < 1e-3);
        return d.Normalized;
    }

    static void Tree(string name, DMesh3 mesh, Func<Random, Ray3d> ray, Func<Random, Vector3d> point, int n, int rounds)
    {
        var tree = new DMeshAABBTree3(mesh, true);
        var rays = new Ray3d[n]; var pts = new Vector3d[n];
        var rnd = new Random(11);
        for (int i = 0; i < n; ++i) { rays[i] = ray(rnd); pts[i] = point(rnd); }
        var tn = new double[rounds]; var ti = new double[rounds];
        long chk = 0; int inside = 0;
        for (int rep = 0; rep <= rounds; ++rep) {      // round 0: warm-up
            chk = 0; inside = 0;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; ++i) chk += tree.FindNearestHitTriangle(rays[i]);
            double a = sw.Elapsed.TotalMilliseconds * 1e6 / n;
            sw.Restart();
            for (int i = 0; i < n; ++i) if (tree.IsInside(pts[i])) inside++;
            double b = sw.Elapsed.TotalMilliseconds * 1e6 / n;
            if (rep > 0) { tn[rep - 1] = a; ti[rep - 1] = b; }
        }
        Console.WriteLine($"{name},{mesh.TriangleCount},FindNearestHitTriangle,{Median(tn):F0},{chk}");
        Console.WriteLine($"{name},{mesh.TriangleCount},IsInside,{Median(ti):F0},{inside}");
    }

    static void Single(int n, int rounds)
    {
        // random triangles in the unit cube, origins in [-2,2]^3 aimed at a random point of the cube
        var rnd = new Random(5);
        var rays = new Ray3d[n]; var tris = new Vector3d[3 * n];
        Func<Vector3d> rp = () => new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
        for (int i = 0; i < n; ++i) {
            tris[3 * i] = rp(); tris[3 * i + 1] = rp(); tris[3 * i + 2] = rp();
            var o = 4 * rp() - 2 * Vector3d.One;
            rays[i] = new Ray3d(o, (rp() - o).Normalized);
        }
        var ts = new double[rounds]; int hits = 0; double sum = 0;
        for (int rep = 0; rep <= rounds; ++rep) {
            hits = 0; sum = 0;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; ++i)
                if (IntrRay3Triangle3.Intersects(ref rays[i], ref tris[3 * i], ref tris[3 * i + 1], ref tris[3 * i + 2], out double t)) {
                    hits++; sum += t;
                }
            if (rep > 0) ts[rep - 1] = sw.Elapsed.TotalMilliseconds * 1e6 / n;
        }
        Console.WriteLine($"random triangles,{n},Intersects,{Median(ts):F1},{hits}/{sum:R}");
    }

    static double Median(double[] v)
    {
        var s = (double[])v.Clone(); Array.Sort(s);
        return s[s.Length / 2];
    }

    static void Main(string[] args)
    {
        int n = args.Length > 0 ? int.Parse(args[0]) : 200000;
        int rounds = args.Length > 1 ? int.Parse(args[1]) : 5;
        Console.WriteLine("mesh,triangles,query,ns per query (median),checksum");
        Tree("sphere", Sphere(), r => new Ray3d(3 * RandDir(r), (0.5 * RandDir(r) - 3 * RandDir(r)).Normalized),
            r => 1.2 * RandDir(r) * Math.Pow(r.NextDouble(), 1 / 3.0), n, rounds);
        Tree("terrain", Terrain(), r => new Ray3d(new Vector3d(r.NextDouble() * 224, r.NextDouble() * 224, 20),
            new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, -1).Normalized),
            r => new Vector3d(r.NextDouble() * 224, r.NextDouble() * 224, r.NextDouble() * 8 - 4), n, rounds);
        Single(5 * n, rounds);
    }
}
