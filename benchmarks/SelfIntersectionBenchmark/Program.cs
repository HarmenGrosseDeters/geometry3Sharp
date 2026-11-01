// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Cost of the exact self-intersection checks: Reducer.PreventSelfIntersections and Remesher.PreventSelfIntersections,
// on generic, tilted and axis-flat meshes (procedural), and optionally a Thingi10K mesh fetched at run time (never
// committed). Prints time and a fingerprint of the result (a hash of every vertex coordinate bit and triangle), so
// runs of two builds can be checked for bit-identical results. See README.md.
//
//   dotnet run -c Release --project benchmarks/SelfIntersectionBenchmark -- [reducer] [remesher] [runs=3] [only=<regex>]
//       [thingi=<file>] [thingi_passes=5] [thingi_runs=1] [thingi_factor=1] (target edge = factor x mean edge)
//   (build with -p:G3PredicateStats=true to also print the predicate calls and their exact stages)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using gs;

static class Program
{
    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        int runs = A.ContainsKey("runs") ? int.Parse(A["runs"]) : 3;
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        bool all = !A.ContainsKey("reducer") && !A.ContainsKey("remesher");
        Console.WriteLine($"| case | triangles in | out | rejected | time ms (min of {runs}) | all runs ms | fingerprint |");
        Console.WriteLine("|---|---:|---:|---:|---:|---|---|");
        if (all || A.ContainsKey("reducer")) {
            // the four cases of the review harness of #239, as given (HarnessPlate, HarnessTilt; target triangles / 10)
            Reduce("reducer_harness_plate50_jitter0.5", HarnessPlate(50, 0.5), 500, runs, only);
            Reduce("reducer_harness_tilt_plate50_jitter0.5", HarnessTilt(HarnessPlate(50, 0.5)), 500, runs, only);
            Reduce("reducer_harness_tilt_plate50", HarnessTilt(HarnessPlate(50)), 500, runs, only);
            Reduce("reducer_harness_plate100", HarnessPlate(100), 2000, runs, only);
            Reduce("reducer_jitter_plate_z0", JitterPlate(50, 0.3, 1), 500, runs, only);
            Reduce("reducer_jitter_plate_z0_to50", JitterPlate(50, 0.3, 1), 50, runs, only);
            Reduce("reducer_jitter_plate_tilted_to50", Tilted(JitterPlate(50, 0.3, 1)), 50, runs, only);
            Reduce("reducer_jitter_plate_tilted", Tilted(JitterPlate(50, 0.3, 1)), 500, runs, only);
            Reduce("reducer_integer_plate", Plate(100), 2000, runs, only);
            Reduce("reducer_bumpy_sphere", BumpySphere(40), 1000, runs, only);
            Reduce("reducer_box_grid", GridBox(30), 600, runs, only);
        }
        if (all || A.ContainsKey("remesher")) {
            foreach (bool prevent in new[] { false, true }) {
                string s = prevent ? "_si" : "";
                Remesh("remesher_bumpy_sphere" + s, BumpySphere(40), 1.0, 10, prevent, runs, only);
                Remesh("remesher_jitter_plate_z0" + s, JitterPlate(40, 0.3, 1), 1.0, 10, prevent, runs, only);
                Remesh("remesher_jitter_plate_tilted" + s, Tilted(JitterPlate(40, 0.3, 1)), 1.0, 10, prevent, runs, only);
                Remesh("remesher_box_grid" + s, GridBox(30), 1.0, 10, prevent, runs, only);
            }
        }
        if (A.ContainsKey("thingi")) {
            DMesh3 m = StandardMeshReader.ReadMesh(A["thingi"]);
            int passes = A.ContainsKey("thingi_passes") ? int.Parse(A["thingi_passes"]) : 5;
            string name = "thingi_" + System.IO.Path.GetFileNameWithoutExtension(A["thingi"]);
            double factor = A.ContainsKey("thingi_factor") ? double.Parse(A["thingi_factor"], System.Globalization.CultureInfo.InvariantCulture) : 1.0;
            foreach (bool prevent in new[] { false, true })
                Remesh(name + (prevent ? "_si" : ""), m, factor, passes, prevent, A.ContainsKey("thingi_runs") ? int.Parse(A["thingi_runs"]) : 1, only);
        }
        return 0;
    }

    static void Reduce(string name, DMesh3 input, int target, int runs, Regex only)
    {
        if (only != null && !only.IsMatch(name)) return;
        Run(name, input, runs, (mesh) => {
            var r = new Reducer(mesh) { PreventSelfIntersections = true };
            r.ReduceToTriangleCount(target);
            return -1;
        });
    }

    // the settings of `g3cli remesh` (tools/g3cli/Commands.cs): smoothing 0.5, projection onto the input, boundary and
    // 30 degree creases kept; target edge length: factor times the mean edge length of the input
    static void Remesh(string name, DMesh3 input, double factor, int passes, bool prevent, int runs, Regex only)
    {
        if (only != null && !only.IsMatch(name)) return;
        double target = factor * MeanEdge(input);
        Run(name, input, runs, (mesh) => {
            var r = new Remesher(mesh) { SmoothSpeedT = 0.5, PreventSelfIntersections = prevent };
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(target);
            for (int k = 0; k < passes; ++k)
                r.BasicRemeshPass();
            return r.SelfIntersectionsRejected;
        });
    }

    static void Run(string name, DMesh3 input, int runs, Func<DMesh3, int> op)
    {
        var times = new List<double>();
        string print = null; int tris = 0, rejected = 0;
        for (int k = 0; k < runs; ++k) {
            DMesh3 mesh = new DMesh3(input);
            ResetStats();
            var sw = Stopwatch.StartNew();
            System.Array.Clear(SelfIntersectionPatch.DBG, 0, 16);
            rejected = op(mesh);
            Console.WriteLine("DBG " + name + ": " + string.Join(" ", SelfIntersectionPatch.DBG));
            times.Add(sw.Elapsed.TotalMilliseconds);
            string p = Fingerprint(mesh);
            if (print != null && p != print)
                throw new Exception($"{name}: run {k} differs from run 0");
            print = p; tris = mesh.TriangleCount;
        }
        Console.WriteLine($"| {name} | {input.TriangleCount} | {tris} | {(rejected < 0 ? "" : rejected.ToString())} | {times.Min():F0} | " +
            $"{string.Join(", ", times.Select(t => t.ToString("F0")))} | {print} |");
        string stats = Stats();
        if (stats != null)
            Console.WriteLine($"|   {name} predicates (last run) | {stats} | | | | | |");
    }

    static void ResetStats() { PredicateStats.Reset(); }

    static string Stats()
    {
        if (PredicateStats.Enabled == false) return null;
        var s = new List<string>();
        foreach (var k in new[] { PredicateStats.Kind.Orient2d, PredicateStats.Kind.Orient3d }) {
            long calls = PredicateStats.Calls((int)k), exact = PredicateStats.ExactStages((int)k);
            s.Add($"{PredicateStats.Names[(int)k]} {calls} calls, {exact} exact ({(calls > 0 ? 100.0 * exact / calls : 0):F1} %)");
        }
        return string.Join("; ", s);
    }

    // FNV-1a over the bits of every vertex coordinate and every triangle, in index order (compact meshes only differ
    // if the operations differ)
    static string Fingerprint(DMesh3 mesh)
    {
        ulong h = 14695981039346656037UL;
        void add(long v) { for (int i = 0; i < 8; ++i) { h ^= (ulong)((v >> (8 * i)) & 0xff); h *= 1099511628211UL; } }
        foreach (int vid in mesh.VertexIndices()) {
            Vector3d p = mesh.GetVertex(vid);
            add(vid); add(BitConverter.DoubleToInt64Bits(p.x)); add(BitConverter.DoubleToInt64Bits(p.y)); add(BitConverter.DoubleToInt64Bits(p.z));
        }
        foreach (int tid in mesh.TriangleIndices()) {
            Index3i t = mesh.GetTriangle(tid);
            add(tid); add(t.a); add(t.b); add(t.c);
        }
        return h.ToString("x16");
    }

    static double MeanEdge(DMesh3 m)
    {
        double sum = 0; int n = 0;
        foreach (int eid in m.EdgeIndices()) {
            Index2i ev = m.GetEdgeV(eid);
            sum += m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); n++;
        }
        return sum / n;
    }

    // ---- inputs (procedural) ----

    // the plate of the review harness of #239: spacing 1, z = 0, the jitter is the full width (0.5: +-0.25 per axis),
    // boundary vertices not moved in the direction across the boundary (exactly as given, including the order of the
    // random numbers)
    static DMesh3 HarnessPlate(int n, double jitter = 0, int seed = 1)
    {
        var r = new Random(seed); var m = new DMesh3();
        for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++) {
            double dx = (i > 0 && i < n) ? jitter * (r.NextDouble() - .5) : 0;
            double dy = (j > 0 && j < n) ? jitter * (r.NextDouble() - .5) : 0;
            m.AppendVertex(new Vector3d(i + dx, j + dy, 0));
        }
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) {
            int a = i + j * (n + 1), b = a + 1, c = a + n + 2, d = a + n + 1;
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
        }
        return m;
    }

    static DMesh3 HarnessTilt(DMesh3 m)
    {
        var q = new Quaterniond(new Vector3d(1, 2, 3).Normalized, 37.0);
        var c = new DMesh3(m);
        foreach (int v in c.VertexIndices()) c.SetVertex(v, q * c.GetVertex(v));
        return c;
    }

    // n x n cells of size 1 in the plane z = 0, integer coordinates, two triangles per cell
    static DMesh3 Plate(int n)
    {
        return JitterPlate(n, 0, 0);
    }

    // n x n cells of size 1 in the plane z = 0, interior vertices moved by up to +-jitter in x and y
    static DMesh3 JitterPlate(int n, double jitter, int seed)
    {
        var rand = new Random(seed);
        var mesh = new DMesh3();
        for (int j = 0; j <= n; ++j)
            for (int i = 0; i <= n; ++i) {
                double x = i, y = j;
                if (jitter > 0 && i > 0 && j > 0 && i < n && j < n) {
                    x += jitter * (2 * rand.NextDouble() - 1);
                    y += jitter * (2 * rand.NextDouble() - 1);
                }
                mesh.AppendVertex(new Vector3d(x, y, 0));
            }
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                int a = i + j * (n + 1), b = a + 1, c = a + n + 2, d = a + n + 1;
                mesh.AppendTriangle(a, b, c);
                mesh.AppendTriangle(a, c, d);
            }
        return mesh;
    }

    // rotated out of every axis plane (no coordinate stays constant over a triangle)
    static DMesh3 Tilted(DMesh3 m)
    {
        var q = new Quaterniond(new Vector3d(1, 2, 3).Normalized, 37);
        DMesh3 r = new DMesh3(m);
        foreach (int vid in r.VertexIndices())
            r.SetVertex(vid, q * r.GetVertex(vid));
        return r;
    }

    static DMesh3 BumpySphere(int edgeVertices)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        foreach (int vid in m.VertexIndices()) {
            Vector3d p = m.GetVertex(vid);
            m.SetVertex(vid, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5) + 0.05 * Math.Sin(9 * p.z)));
        }
        return m;
    }

    // a closed box with regular grids on its faces: every triangle lies in an axis plane
    static DMesh3 GridBox(int edgeVertices)
    {
        return new GridBox3Generator() { EdgeVertices = edgeVertices, Box = new Box3d(Vector3d.Zero, new Vector3d(10, 10, 10)) }.Generate().MakeDMesh();
    }
}
