// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using gs;

// Upper bound of what a better inside/outside decision for open input could gain (spike 2026-11-19, hotspots item 6).
//   dotnet run --project benchmarks/OpenInsideOutsideBound -c Release -- proc            (procedural cases with analytic truth)
//   dotnet run --project benchmarks/OpenInsideOutsideBound -c Release -- thingi DIR [max_open] [max_tris]
// Output: CSV lines. proc: classifier error of GWN > 0.5 against the true solid + the MeshRobustRepair result against the same truth.
static class Program
{
    static CultureInfo C = CultureInfo.InvariantCulture;

    static DMesh3 Sphere(Vector3d c, double r = 1, int ev = 20)
    {
        var g = new Sphere3Generator_NormalizedCube { Radius = r, EdgeVertices = ev };
        g.Generate();
        DMesh3 m = g.MakeDMesh();
        foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, m.GetVertex(v) + c);
        return m;
    }

    static DMesh3 Torus(double R, double r, int nu, int nv)
    {
        var m = new DMesh3();
        for (int i = 0; i < nu; ++i) for (int j = 0; j < nv; ++j) {
            double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
            m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
        }
        Func<int, int, int> id = (i, j) => (i % nu) * nv + (j % nv);
        for (int i = 0; i < nu; ++i) for (int j = 0; j < nv; ++j) {
            m.AppendTriangle(id(i, j), id(i + 1, j), id(i + 1, j + 1));
            m.AppendTriangle(id(i, j), id(i + 1, j + 1), id(i, j + 1));
        }
        return m;
    }

    // a copy without the triangles whose centroid satisfies drop; unwelded (soup) if soup
    static DMesh3 Cut(DMesh3 m, Func<Vector3d, bool> drop, bool soup)
    {
        var o = new DMesh3();
        if (!soup) { foreach (int v in m.VertexIndices()) o.AppendVertex(m.GetVertex(v)); }
        foreach (int t in m.TriangleIndices()) {
            Index3i tr = m.GetTriangle(t);
            Vector3d a = m.GetVertex(tr.a), b = m.GetVertex(tr.b), c = m.GetVertex(tr.c);
            if (drop((a + b + c) / 3)) continue;
            if (soup) o.AppendTriangle(o.AppendVertex(a), o.AppendVertex(b), o.AppendVertex(c));
            else o.AppendTriangle(tr.a, tr.b, tr.c);
        }
        return o;
    }

    static DMesh3 Join(params DMesh3[] ms)
    {
        var o = new DMesh3();
        foreach (var m in ms) foreach (int t in m.TriangleIndices()) {
            Index3i tr = m.GetTriangle(t);
            o.AppendTriangle(o.AppendVertex(m.GetVertex(tr.a)), o.AppendVertex(m.GetVertex(tr.b)), o.AppendVertex(m.GetVertex(tr.c)));
        }
        return o;
    }

    static bool Near(Vector3d p, Vector3d c, Vector3d axis, double theta)
    {
        Vector3d d = (p - c).Normalized;
        return d.Dot(axis) > Math.Cos(theta);
    }

    record Case(string Name, DMesh3 Mesh, Func<Vector3d, bool> Truth, bool Open);

    static List<Case> Cases()
    {
        var L = new List<Case>();
        Vector3d z = new Vector3d(0, 0, 1), o = Vector3d.Zero;
        Func<Vector3d, bool> ball = p => p.Length < 1;
        L.Add(new Case("sphere_closed", Sphere(o), ball, false));
        foreach (double th in new[] { 0.3, 0.6, 1.0, 1.4 })
            L.Add(new Case($"sphere_cap{th:F1}", Cut(Sphere(o), p => Near(p, o, z, th), false), ball, true));
        L.Add(new Case("sphere_2caps1.0", Cut(Sphere(o), p => Near(p, o, z, 1.0) || Near(p, o, -z, 1.0), false), ball, true));
        // eight small holes at the cube corners
        var corners = new List<Vector3d>(); for (int i = 0; i < 8; ++i) corners.Add(new Vector3d((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1).Normalized);
        L.Add(new Case("sphere_8holes0.3", Cut(Sphere(o), p => corners.Any(c => Near(p, o, c, 0.3)), false), ball, true));
        L.Add(new Case("sphere_8holes0.6", Cut(Sphere(o), p => corners.Any(c => Near(p, o, c, 0.6)), false), ball, true));
        // a band (slit) around the equator
        L.Add(new Case("sphere_band0.15", Cut(Sphere(o), p => Math.Abs(p.z) < Math.Sin(0.15), false), ball, true));
        Func<Vector3d, bool> torus = p => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 1; return q * q + p.z * p.z < 0.16; };
        L.Add(new Case("torus_closed", Torus(1, 0.4, 48, 20), torus, false));
        foreach (double ang in new[] { 0.5, 1.0 })
            L.Add(new Case($"torus_window{ang:F1}", Cut(Torus(1, 0.4, 48, 20), p => Math.Atan2(p.y, p.x) > 0 && Math.Atan2(p.y, p.x) < ang * 2 && p.z > 0, false), torus, true));
        // overlapping patches: two spheres (union is the solid), unwelded soups
        Vector3d s2 = new Vector3d(1.2, 0, 0.3);
        Func<Vector3d, bool> two = p => p.Length < 1 || (p - s2).Length < 1;
        L.Add(new Case("two_spheres_closed", Join(Sphere(o), Sphere(s2)), two, false));
        foreach (double th in new[] { 0.5, 1.0 })
            L.Add(new Case($"two_spheres_cut{th:F1}", Join(Cut(Sphere(o), p => Near(p, o, z, th), false), Cut(Sphere(s2), p => Near(p, s2, z, th), false)), two, true));
        // facing caps: the cut of each sphere faces the other (holes inside the other's solid)
        L.Add(new Case("two_spheres_facing0.6", Join(Cut(Sphere(o), p => Near(p, o, new Vector3d(1, 0, 0.3).Normalized, 0.6), false),
            Cut(Sphere(s2), p => Near(p, s2, new Vector3d(-1, 0, -0.3).Normalized, 0.6), false)), two, true));
        return L;
    }

    static IEnumerable<Vector3d> Samples(AxisAlignedBox3d b, int n, int seed)
    {
        var rnd = new Random(seed);
        double e = 0.1 * b.Diagonal.Length;
        for (int i = 0; i < n; ++i)
            yield return new Vector3d(b.Min.x - e + rnd.NextDouble() * (b.Width + 2 * e), b.Min.y - e + rnd.NextDouble() * (b.Height + 2 * e), b.Min.z - e + rnd.NextDouble() * (b.Depth + 2 * e));
    }

    static string Short(string s) { s = (s ?? "").Replace(',', ';').Replace('\n', ' '); return s.Length > 70 ? s.Substring(0, 70) : s; }

    static void Proc()
    {
        Console.WriteLine("case,open,tris,gwn_missing,gwn_extra,gwn_iou,ambig01_share,ambig_of_err,path,clean,result_iou_truth,seconds,exact_reason");
        foreach (var cs in Cases()) {
            DMesh3 m = cs.Mesh;
            var tree = new DMeshAABBTree3(m, true); tree.FastWindingNumber(Vector3d.Zero);
            var pts = Samples(m.GetBounds(), 30000, 7).ToList();
            int tin = 0, miss = 0, extra = 0, amb = 0, ambErr = 0, err = 0;
            foreach (var p in pts) {
                double w = tree.FastWindingNumber(p); bool t = cs.Truth(p), pr = w > 0.5, a = Math.Abs(w - 0.5) < 0.1;
                if (t) tin++; if (a) amb++;
                if (t && !pr) miss++; if (!t && pr) extra++;
                if (t != pr) { err++; if (a) ambErr++; }
            }
            int inter = tin - miss;
            double iou = (double)inter / (tin + extra);
            var rep = new MeshRobustRepair(new DMesh3(m, false, false, false));
            var sw = Stopwatch.StartNew();
            bool ok = rep.Apply();
            double secs = sw.Elapsed.TotalSeconds;
            double rIou = double.NaN;
            if (ok && rep.Mesh.TriangleCount > 0) {
                var rt = new DMeshAABBTree3(rep.Mesh, true); rt.FastWindingNumber(Vector3d.Zero);
                int i2 = 0, u2 = 0;
                foreach (var p in pts) { bool a = cs.Truth(p), b = rt.FastWindingNumber(p) > 0.5; if (a && b) i2++; if (a || b) u2++; }
                rIou = (double)i2 / u2;
            }
            Console.WriteLine(string.Join(",", cs.Name, cs.Open, m.TriangleCount, ((double)miss / tin).ToString("F4", C), ((double)extra / tin).ToString("F4", C), iou.ToString("F4", C),
                ((double)amb / pts.Count).ToString("F4", C), err == 0 ? "NA" : ((double)ambErr / err).ToString("F3", C), rep.ResultPath, rep.ResultReason == "" && ok, rIou.ToString("F4", C), secs.ToString("F1", C), Short(rep.ExactResolveReason)));
        }
    }

    static void Thingi(string dir, int maxOpen, int maxTris)
    {
        Console.WriteLine("file,tris,boundary_edges,ambig01_share,inside_share,path,clean,result_iou_input_gwn,seconds,exact_reason");
        int openDone = 0, total = 0, closed = 0;
        foreach (string f in Directory.GetFiles(dir).OrderBy(x => x, StringComparer.Ordinal)) {
            if (openDone >= maxOpen) break;
            var b = new DMesh3Builder();
            var rr = StandardMeshReader.ReadFile(f, new ReadOptions(), b);
            if (rr.code != IOCode.Ok || b.Meshes.Count == 0) continue;
            DMesh3 m = b.Meshes[0]; for (int i = 1; i < b.Meshes.Count; ++i) MeshEditor.Append(m, b.Meshes[i]);
            total++;
            int be = m.BoundaryEdgeIndices().Count();
            if (be == 0) { closed++; continue; }
            if (m.TriangleCount > maxTris) continue;
            var tree = new DMeshAABBTree3(m, true); tree.FastWindingNumber(Vector3d.Zero);
            int amb = 0, inn = 0, n = 20000;
            foreach (var p in Samples(m.GetBounds(), n, 7)) { double w = tree.FastWindingNumber(p); if (Math.Abs(w - 0.5) < 0.1) amb++; if (w > 0.5) inn++; }
            var rep = new MeshRobustRepair(m) { MeasureResultVolumeIoU = true };
            var sw = Stopwatch.StartNew();
            bool ok = rep.Apply();
            Console.WriteLine(string.Join(",", Path.GetFileName(f), m.TriangleCount, be, ((double)amb / n).ToString("F4", C), ((double)inn / n).ToString("F4", C), rep.ResultPath, rep.ResultReason == "" && ok,
                rep.ResultVolumeIoU.ToString("F4", C), sw.Elapsed.TotalSeconds.ToString("F1", C), Short(rep.ExactResolveReason)));
            Console.Out.Flush();
            openDone++;
        }
        Console.Error.WriteLine($"read {total}, closed {closed}, open run {openDone}");
    }


    static DMesh3 FlipSome(DMesh3 m, double frac, int seed)
    {
        var o = new DMesh3(m, false, false, false); var rnd = new Random(seed);
        foreach (int t in o.TriangleIndices().ToList()) if (rnd.NextDouble() < frac) { Index3i tr = o.GetTriangle(t); o.SetTriangle(t, new Index3i(tr.a, tr.c, tr.b), false); }
        return o;
    }

    static void Heat(int n, bool cg)
    {
        Console.WriteLine("case,variant,method,missing,extra,iou,seconds,iters");
        var list = Cases();
        var extra = new List<Case>();
        foreach (var c in list) if (c.Name == "sphere_cap1.0" || c.Name == "two_spheres_cut1.0") foreach (double f in new[] { 0.1, 0.3 }) extra.Add(new Case(c.Name + "_flip" + f, FlipSome(c.Mesh, f, 3), c.Truth, true));
        foreach (var cs in list.Concat(extra)) {
            DMesh3 m = cs.Mesh;
            var pts = Samples(m.GetBounds(), 30000, 7).ToList();
            var tree = new DMeshAABBTree3(m, true); tree.FastWindingNumber(Vector3d.Zero);
            Report(cs, pts, "gwn", p => tree.FastWindingNumber(p) > 0.5, 0, 0);
            var h = new SignedHeatGrid { UseCG = cg };
            var sw = Stopwatch.StartNew();
            h.Compute(m, n);
            Report(cs, pts, "heat" + n + (cg ? "cg" : "chol"), p => h.Eval(p) < 0, sw.Elapsed.TotalSeconds, h.Iterations);
        }
    }

    static void Report(Case cs, List<Vector3d> pts, string method, Func<Vector3d, bool> inside, double secs, int iters)
    {
        int tin = 0, miss = 0, extra = 0;
        foreach (var p in pts) { bool t = cs.Truth(p), pr = inside(p); if (t) tin++; if (t && !pr) miss++; if (!t && pr) extra++; }
        Console.WriteLine(string.Join(",", cs.Name, "", method, ((double)miss / tin).ToString("F4", C), ((double)extra / tin).ToString("F4", C),
            ((double)(tin - miss) / (tin + extra)).ToString("F4", C), secs.ToString("F2", C), iters));
        Console.Out.Flush();
    }


    static void ThingiHeat(string dir, int maxOpen, int maxTris, int n)
    {
        Console.WriteLine("file,tris,boundary_edges,gwn_inside,heat_inside,disagree,path,result_iou_gwn,result_iou_heat,secs_heat");
        int done = 0;
        foreach (string f in Directory.GetFiles(dir).OrderBy(x => x, StringComparer.Ordinal)) {
            if (done >= maxOpen) break;
            var b = new DMesh3Builder();
            var rr = StandardMeshReader.ReadFile(f, new ReadOptions(), b);
            if (rr.code != IOCode.Ok || b.Meshes.Count == 0) continue;
            DMesh3 m = b.Meshes[0]; for (int i = 1; i < b.Meshes.Count; ++i) MeshEditor.Append(m, b.Meshes[i]);
            int be = m.BoundaryEdgeIndices().Count();
            if (be == 0 || m.TriangleCount > maxTris) continue;
            var pts = Samples(m.GetBounds(), 20000, 7).ToList();
            var tree = new DMeshAABBTree3(m, true); tree.FastWindingNumber(Vector3d.Zero);
            var h = new SignedHeatGrid { UseCG = true };
            var sw = Stopwatch.StartNew(); h.Compute(m, n); double hs = sw.Elapsed.TotalSeconds;
            int gi = 0, hi = 0, dis = 0;
            foreach (var p in pts) { bool g = tree.FastWindingNumber(p) > 0.5, hh = h.Eval(p) < 0; if (g) gi++; if (hh) hi++; if (g != hh) dis++; }
            var rep = new MeshRobustRepair(new DMesh3(m, false, false, false));
            bool ok = rep.Apply();
            double ig = double.NaN, ih = double.NaN;
            if (ok && rep.Mesh.TriangleCount > 0) {
                var rt = new DMeshAABBTree3(rep.Mesh, true); rt.FastWindingNumber(Vector3d.Zero);
                int a1 = 0, u1 = 0, a2 = 0, u2 = 0;
                foreach (var p in pts) { bool r = rt.FastWindingNumber(p) > 0.5, g = tree.FastWindingNumber(p) > 0.5, hh = h.Eval(p) < 0;
                    if (r && g) a1++; if (r || g) u1++; if (r && hh) a2++; if (r || hh) u2++; }
                ig = (double)a1 / u1; ih = (double)a2 / u2;
            }
            Console.WriteLine(string.Join(",", Path.GetFileName(f), m.TriangleCount, be, ((double)gi / pts.Count).ToString("F4", C), ((double)hi / pts.Count).ToString("F4", C),
                ((double)dis / pts.Count).ToString("F4", C), rep.ResultPath, ig.ToString("F4", C), ih.ToString("F4", C), hs.ToString("F2", C)));
            Console.Out.Flush();
            done++;
        }
    }

    static void Determinism()
    {
        var m = Cases().First(c => c.Name == "sphere_cap1.0").Mesh;
        foreach (bool cg in new[] { true, false }) {
            ulong h1 = 0, h2 = 0;
            for (int run = 0; run < 3; ++run) {
                var h = new SignedHeatGrid { UseCG = cg }; h.Compute(m, cg ? 48 : 24);
                ulong x = 14695981039346656037UL; foreach (double d in h.Phi) { x ^= (ulong)BitConverter.DoubleToInt64Bits(d); x *= 1099511628211UL; }
                Console.WriteLine($"{(cg ? "cg" : "chol")} run {run} hash {x:x16}");
            }
        }
    }

    static void Main(string[] a)
    {
        if (a[0] == "proc") Proc();
        else if (a[0] == "det") Determinism();
        else if (a[0] == "thingiheat") ThingiHeat(a[1], int.Parse(a[2]), 30000, int.Parse(a[3]));
        else if (a[0] == "heat") Heat(int.Parse(a[1]), a.Length > 2 && a[2] == "cg");
        else Thingi(a[1], a.Length > 2 ? int.Parse(a[2]) : 40, a.Length > 3 ? int.Parse(a[3]) : 30000);
    }
}
