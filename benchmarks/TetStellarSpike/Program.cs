// Throwaway spike (branch runs/tet-stellar): which operations of Klingner and Shewchuk 2007 that TetMeshImprover lacks would raise tet quality?
//   base <proc|mesh> <alpha> <floor> [maxdeg]    MeshTetrahedralization at the floor, per-tet min/max dihedral distribution, one CSV row
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

static partial class Spike
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string F(double v) => v.ToString("G5", Inv);

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "base";
        string src = args.Length > 1 ? args[1] : "proc";
        double alpha = args.Length > 2 ? double.Parse(args[2], Inv) : 40;
        double floor = args.Length > 3 ? double.Parse(args[3], Inv) : 10;
        var inputs = src == "proc" ? Procedural().ToList()
            : new List<KeyValuePair<string, DMesh3>> { new KeyValuePair<string, DMesh3>(System.IO.Path.GetFileName(src), StandardMeshReader.ReadMesh(src)) };
        if (mode == "base") {
            if (args.Length <= 4 || args[4] != "nohdr") Console.WriteLine(BaseHeader);
            foreach (var kv in inputs) Console.WriteLine(Base(kv.Key, kv.Value, alpha, floor));
            return 0;
        }
        if (mode == "proto") {
            double gG = double.Parse(args[4], Inv); string cfg = args[5];
            if (args.Length <= 6 || args[6] != "nohdr") Console.WriteLine(ProtoHeader);
            foreach (var kv in inputs) foreach (string line in ProtoAll(kv.Key, kv.Value, alpha, floor, gG, cfg.Split(';'))) Console.WriteLine(line);
            return 0;
        }
        if (mode == "bounds") {
            // bounds <proc|mesh> <alpha> <floor> <maxdeg> [maxViol] : the pipeline runs at <floor>; the target is (floor, maxdeg)
            F_ = floor; var Gs = args[4].Split(',').Select(x => double.Parse(x, Inv)).ToList(); int maxViol = args.Length > 5 ? int.Parse(args[5], Inv) : 60;
            foreach (var kv in inputs) {
                var tm = new MeshTetrahedralization(kv.Value) { RelativeAlpha = alpha, RelativeOffset = 600, SliverDihedralDegrees = floor };
                tm.Compute();
                if (tm.Result == null) { Console.WriteLine(kv.Key + ",FAIL"); continue; }
                foreach (double gg in Gs) {
                G_ = gg;
                var row = Bounds(tm.Result, tm.UsedOffset, maxViol, new Random(1));
                string[] keys = { "touchF", "touchE", "touchV", "interior", "shape_needle", "shape_wedge", "shape_sliver", "shape_cap", "op1_prog", "op1_clear", "op2n3_clear", "op2_prog", "op2_clear", "op3_prog", "op3_clear", "op3i_clear", "op3b_clear", "any_prog", "any_clear", "op3_nocand" };
                Console.WriteLine(kv.Key + "," + F(alpha) + "," + F(floor) + "," + F(G_) + "," + tm.Result.Tets.Count + "," + row.Viol + "," + string.Join(",", keys.Select(k => row.Shape.TryGetValue(k, out int v) ? v : 0)));
                }
            }
            return 0;
        }
        return 1;
    }

    // per-tet dihedral extremes in degrees; -1 for a non-positive tet
    internal static void Dihedrals(Vector3d a, Vector3d b, Vector3d c, Vector3d d, out double mn, out double mx)
    {
        mn = 180; mx = 0;
        if (ExactPredicates.Orient3d(a, b, c, d) <= 0) { mn = -1; mx = 181; return; }
        Vector3d n0 = (c - b).Cross(d - b), n1 = (d - a).Cross(c - a), n2 = (b - a).Cross(d - a), n3 = (c - a).Cross(b - a);
        Vector3d[] n = { n0, n1, n2, n3 };
        for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) {
            double x = 180 - Math.Atan2(n[i].Cross(n[j]).Length, n[i].Dot(n[j])) * (180 / Math.PI);
            if (x < mn) mn = x; if (x > mx) mx = x;
        }
    }

    const string BaseHeader = "name,alpha,floor,status,tets,boundary_tris,total_s,us_per_tet,min,p01,max,p99,min_lt10,min_lt20,min_lt30,max_gt140,max_gt150,max_gt165,either20_150,either30_140,valid";

    static string Base(string name, DMesh3 input, double alpha, double floor)
    {
        var sw = Stopwatch.StartNew();
        var tm = new MeshTetrahedralization(input) { RelativeAlpha = alpha, RelativeOffset = 600, SliverDihedralDegrees = floor };
        if (Environment.GetEnvironmentVariable("G3_TET_RESCUE") is string rp) tm.RescuePasses = int.Parse(rp, Inv);
        tm.Compute();
        double secs = sw.Elapsed.TotalSeconds;
        if (tm.Result == null) return name + "," + F(alpha) + "," + F(floor) + "," + tm.Status;
        var r = tm.Result;
        int n = r.Tets.Count;
        var mins = new double[n]; var maxs = new double[n];
        int l10 = 0, l20 = 0, l30 = 0, g140 = 0, g150 = 0, g165 = 0, e20 = 0, e30 = 0;
        for (int i = 0; i < n; ++i) {
            Index4i t = r.Tets[i];
            Dihedrals(r.Vertices[t.a], r.Vertices[t.b], r.Vertices[t.c], r.Vertices[t.d], out mins[i], out maxs[i]);
            if (mins[i] < 10) l10++; if (mins[i] < 20) l20++; if (mins[i] < 30) l30++;
            if (maxs[i] > 140) g140++; if (maxs[i] > 150) g150++; if (maxs[i] > 165) g165++;
            if (mins[i] < 20 || maxs[i] > 150) e20++; if (mins[i] < 30 || maxs[i] > 140) e30++;
        }
        Array.Sort(mins); Array.Sort(maxs);
        string valid = TetMeshQuality.NotValidReason(r) == null ? "yes" : "NO";
        return string.Join(",", name, F(alpha), F(floor), tm.Status, n, r.BoundaryTriangles.Count, F(secs), F(secs * 1e6 / Math.Max(1, n)),
            F(mins[0]), F(mins[(int)(0.01 * (n - 1))]), F(maxs[n - 1]), F(maxs[(int)(0.99 * (n - 1))]), l10, l20, l30, g140, g150, g165, e20, e30, valid);
    }

    static DMesh3 Sphere(int n) => new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
    static DMesh3 Torus(int major, int minor, double R, double r)
    {
        var m = new DMesh3();
        for (int i = 0; i < major; ++i) for (int j = 0; j < minor; ++j) {
            double u = 2 * Math.PI * i / major, v = 2 * Math.PI * j / minor;
            m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
        }
        for (int i = 0; i < major; ++i) for (int j = 0; j < minor; ++j) {
            int a = i * minor + j, b = ((i + 1) % major) * minor + j, c = ((i + 1) % major) * minor + (j + 1) % minor, d = i * minor + (j + 1) % minor;
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
        }
        return m;
    }
    static IEnumerable<KeyValuePair<string, DMesh3>> Procedural()
    {
        yield return new KeyValuePair<string, DMesh3>("sphere n=16", Sphere(16));
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
