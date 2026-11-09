// Stage 0 of docs/design/TET_MESHING.md: the insertion rate of Delaunay3 and the tetrahedral mesh that the inside cells of
// MeshAlphaWrap give (time, size, radius-edge ratio, dihedral angles). Usage:
//   dotnet run -c Release --project benchmarks/TetMeshBenchmark -- insert [n ...]
//   dotnet run -c Release --project benchmarks/TetMeshBenchmark -- wrap [relativeAlpha] [relativeOffset]   (procedural inputs)
//   dotnet run -c Release --project benchmarks/TetMeshBenchmark -- file <mesh.obj|stl|off> [relativeAlpha] [relativeOffset]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

static class Program
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "wrap";
        if (mode == "insert") {
            foreach (int n in args.Length > 1 ? args.Skip(1).Select(int.Parse) : new[] { 20000, 100000, 400000 })
                Insert(n);
        } else if (mode == "wrap") {
            double ra = args.Length > 1 ? double.Parse(args[1], Inv) : 20, ro = args.Length > 2 ? double.Parse(args[2], Inv) : 600;
            Header();
            foreach (var kv in Procedural()) Wrap(kv.Key, kv.Value, ra, ro);
        } else if (mode == "file") {
            double ra = args.Length > 2 ? double.Parse(args[2], Inv) : 20, ro = args.Length > 3 ? double.Parse(args[3], Inv) : 600;
            var mesh = StandardMeshReader.ReadMesh(args[1]);
            Header();
            Wrap(System.IO.Path.GetFileName(args[1]), mesh, ra, ro);
        } else if (mode == "export") {          // export <mesh> <out.mesh|out.msh> [relativeAlpha] [relativeOffset]: the wrap's inside cells as a tet file
            double ra = args.Length > 3 ? double.Parse(args[3], Inv) : 20, ro = args.Length > 4 ? double.Parse(args[4], Inv) : 600;
            var w = new MeshAlphaWrap(StandardMeshReader.ReadMesh(args[1])) { RelativeAlpha = ra, RelativeOffset = ro };
            if (!w.Compute()) { Console.Error.WriteLine("wrap failed: " + w.Status); return 2; }
            TetFiles.Write(w.ExtractTetMesh(), args[2]);
        } else if (mode == "eval") {            // eval <tet file .mesh|.msh> [name] [time_s] [threads]: quality of a tet mesh of another mesher
            Console.WriteLine(TetFiles.Header);
            Console.WriteLine(TetFiles.Evaluate(args.Length > 2 ? args[2] : System.IO.Path.GetFileName(args[1]), args[1], args.Length > 3 ? args[3] : "", args.Length > 4 ? args[4] : ""));
        } else return 1;
        return 0;
    }

    static void Insert(int n)
    {
        var rng = new Random(12345);
        var pts = new Vector3d[n];
        for (int i = 0; i < n; ++i) pts[i] = new Vector3d(rng.NextDouble() * 0.98 + 0.01, rng.NextDouble() * 0.98 + 0.01, rng.NextDouble() * 0.98 + 0.01);
        // insertion in a spatially coherent order (the wrap's points come in coherent order, too): sort by a coarse grid cell
        var sorted = pts.OrderBy(p => (long)(p.x * 32) * 1024 * 1024 + (long)(p.y * 32) * 1024 + (long)(p.z * 32)).ToArray();
        foreach (var (name, order) in new[] { ("random order", pts), ("grid-sorted", sorted) }) {
            var dt = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
            var sw = Stopwatch.StartNew();
            int ok = 0;
            foreach (var p in order) {
                if (dt.Insert(p, -1, 0, out int v) == Delaunay3.InsertResult.Inserted) ok++;   // Locate starts from the last cell by itself
            }
            sw.Stop();
            Console.WriteLine($"insert,{name},{n},{ok},{dt.CellCount},{sw.Elapsed.TotalMilliseconds.ToString("F1", Inv)} ms,{(sw.Elapsed.TotalMilliseconds * 1000 / n).ToString("F2", Inv)} us/point,{(sw.Elapsed.TotalMilliseconds * 1000 / dt.CellCount).ToString("F3", Inv)} us/cell");
        }
    }

    static void Header()
    {
        Console.WriteLine("input,status,input_tris,steiner_points,wrap_ms,extract_ms,validity_ms,measure_ms,tets,boundary_tris,wrap_us_per_tet,extract_us_per_tet," +
            "re_worst,re_p1,re_p5,re_median,re_gt2,mindih_worst,mindih_p1,mindih_p5,mindih_median,maxdih_worst,dih_lt10,dih_lt5,dih_lt1,maxdih_gt170,valid," +
            "surf_edges,surf_dih_min,surf_edges_lt90,surf_edges_lt45,surf_edges_lt20,surf_edges_gt270,surf_tri_minangle_worst,surf_tri_minangle_p1");
    }

    static void Wrap(string name, DMesh3 mesh, double ra, double ro)
    {
        var w = new MeshAlphaWrap(mesh) { RelativeAlpha = ra, RelativeOffset = ro };
        var sw = Stopwatch.StartNew();
        bool ok = w.Compute();
        double wrapMs = sw.Elapsed.TotalMilliseconds;
        if (!ok) { Console.WriteLine($"{name},{w.Status},{mesh.TriangleCount},,{wrapMs.ToString("F1", Inv)}"); return; }
        sw.Restart();
        TetMesh3 t = w.ExtractTetMesh();
        double extractMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        string bad = TetMeshQuality.NotValidReason(t);
        double validMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        TetQualityStatistics q = TetMeshQuality.Measure(t);
        double measureMs = sw.Elapsed.TotalMilliseconds;
        string F(double x) => x.ToString("G6", Inv);
        string SurfaceStats()
        {
            DMesh3 sm = t.ToSurfaceMesh();
            int edges = 0, lt90 = 0, lt45 = 0, lt20 = 0, gt270 = 0; double mind = 360;
            foreach (int eid in sm.EdgeIndices()) {
                Index2i et = sm.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID) continue;
                Index4i ev = sm.GetEdge(eid);
                Vector3d n1 = sm.GetTriNormal(et.a), n2 = sm.GetTriNormal(et.b);
                double theta = Math.Atan2(n1.Cross(n2).Length, n1.Dot(n2)) * 180 / Math.PI;
                Index3i t2 = sm.GetTriangle(et.b);
                int opp = t2.a != ev.a && t2.a != ev.b ? t2.a : t2.b != ev.a && t2.b != ev.b ? t2.b : t2.c;
                bool convex = n1.Dot(sm.GetVertex(opp) - sm.GetVertex(ev.a)) < 0;       // the other triangle bends inwards
                double dih = convex ? 180 - theta : 180 + theta;
                edges++; mind = Math.Min(mind, dih);
                if (dih < 90) lt90++; if (dih < 45) lt45++; if (dih < 20) lt20++; if (dih > 270) gt270++;
            }
            TriangleQualityStatistics ts = TriangleQualityStatistics.Compute(sm);
            return string.Join(",", edges, F(mind), lt90, lt45, lt20, gt270, F(ts.MinAngle.Worst), F(ts.MinAngle.Worst1Percent));
        }
        long steiner = w.Counts.TryGetValue("steiner_points", out long sp) ? sp : -1;
        Console.WriteLine(string.Join(",", name, w.Status, mesh.TriangleCount, steiner, F(wrapMs), F(extractMs), F(validMs), F(measureMs), t.Tets.Count, t.BoundaryTriangles.Count,
            F(wrapMs * 1000 / t.Tets.Count), F(extractMs * 1000 / t.Tets.Count),
            F(q.RadiusEdge.Worst), F(q.RadiusEdge.Worst1Percent), F(q.RadiusEdge.Worst5Percent), F(q.RadiusEdge.Median), q.RadiusEdgeAbove2,
            F(q.MinDihedral.Worst), F(q.MinDihedral.Worst1Percent), F(q.MinDihedral.Worst5Percent), F(q.MinDihedral.Median), F(q.MaxDihedral.Worst),
            q.MinDihedralBelow10, q.MinDihedralBelow5, q.MinDihedralBelow1, q.MaxDihedralAbove170, bad == null ? "yes" : bad.Replace(",", ";"), SurfaceStats()));
    }

    static DMesh3 Sphere(int n) => new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();

    static DMesh3 Torus(int major, int minor, double R, double r)
    {
        var m = new DMesh3();
        for (int i = 0; i < major; ++i)
            for (int j = 0; j < minor; ++j) {
                double u = 2 * Math.PI * i / major, v = 2 * Math.PI * j / minor;
                m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
            }
        for (int i = 0; i < major; ++i)
            for (int j = 0; j < minor; ++j) {
                int a = i * minor + j, b = ((i + 1) % major) * minor + j, c = ((i + 1) % major) * minor + (j + 1) % minor, d = i * minor + (j + 1) % minor;
                m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            }
        return m;
    }

    static IEnumerable<KeyValuePair<string, DMesh3>> Procedural()
    {
        yield return new KeyValuePair<string, DMesh3>("sphere n=6", Sphere(6));
        yield return new KeyValuePair<string, DMesh3>("sphere n=16", Sphere(16));
        yield return new KeyValuePair<string, DMesh3>("sphere n=40", Sphere(40));
        yield return new KeyValuePair<string, DMesh3>("box 1x0.5x0.25", new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.25, 0.125), new Vector3d(0.5, 0.25, 0.125)) }.Generate().MakeDMesh());
        yield return new KeyValuePair<string, DMesh3>("cylinder", new CappedCylinderGenerator() { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1.5f, Slices = 32 }.Generate().MakeDMesh());
        yield return new KeyValuePair<string, DMesh3>("torus", Torus(48, 24, 1, 0.35));
        yield return new KeyValuePair<string, DMesh3>("torus thin", Torus(64, 16, 1, 0.1));
        var a = Sphere(12); var b = Sphere(12);
        foreach (int v in b.VertexIndices()) b.SetVertex(v, b.GetVertex(v) + new Vector3d(1.2, 0.3, 0));
        new MeshEditor(a).AppendMesh(b);
        yield return new KeyValuePair<string, DMesh3>("two spheres (soup)", a);
    }
}
