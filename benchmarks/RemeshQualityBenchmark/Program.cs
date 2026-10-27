// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Remesher.MinTriangleQuality (opt-in quality safeguards) on and off, with the settings of `g3cli remesh` (20 passes,
// smoothing 0.5, projection onto the input, boundary and 30 degree creases kept): per case the poor and flat
// triangles, self-intersecting triangle pairs, inside-out components, Hausdorff distance and time, and how many cases
// get worse. Procedural primitives, gears and thin-walled tubes (targets far above the wall thickness); optionally the
// Thingi10K selection of benchmarks/RemeshBenchmark (fetch_data.py).
//
//   dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- [thingi=<dir>] [only=<regex>] [quality=0.2] [runs=1]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using g3;
using gs;

static class Program
{
    class Result
    {
        public int Tris, Poor, Flat, SelfPairs, Inverted;
        public double Hausdorff, Seconds;
        public string NotClean;
    }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        double quality = A.ContainsKey("quality") ? double.Parse(A["quality"], CultureInfo.InvariantCulture) : 0.2;
        int runs = A.ContainsKey("runs") ? int.Parse(A["runs"]) : 1;

        var cases = new List<(string group, string name, DMesh3 input, double target)>();
        foreach (int s in new[] { 6, 8, 10, 12, 16, 24, 32 })
            foreach (var (kind, m) in new[] { ("cylinder", Cylinder(s, 1, 2)), ("cone", Cone(s)), ("taper", Cylinder(s, 0.3, 1)) })
                foreach (double f in Targets) cases.Add(("primitives", $"{kind}{s}_x{f}", m, f * MeanEdge(m)));
        foreach (int e in new[] { 2, 3, 4, 6 }) {
            DMesh3 m = Box(e);
            foreach (double f in Targets) cases.Add(("primitives", $"box{e}_x{f}", m, f * MeanEdge(m)));
        }
        foreach (var (teeth, h, sub) in new[] { (12, 0.3, 1), (24, 0.2, 1), (40, 0.25, 1), (12, 0.3, 3), (24, 0.2, 3), (16, 0.25, 4) }) {
            DMesh3 m = Gear(teeth, h, sub);
            foreach (double f in new[] { 0.5, 1.0, 2.0 }) cases.Add(("gears", $"gear{teeth}_{sub}_x{f}", m, f * MeanEdge(m)));
        }
        foreach (int s in new[] { 16, 32, 64 })
            foreach (double w in new[] { 0.05, 0.1 }) {
                DMesh3 m = Tube(s, 4, 1, w, 3);
                foreach (double t in new[] { 0.5, 0.8, 1.2, 1.6 }) cases.Add(("tubes", $"tube{s}_w{w}_t{t}", m, t));
            }
        if (A.ContainsKey("thingi"))
            foreach (string f in System.IO.Directory.GetFiles(A["thingi"]).OrderBy(f => f, StringComparer.Ordinal)) {
                DMesh3 m = StandardMeshReader.ReadMesh(f);
                if (m != null && m.TriangleCount > 0)
                    cases.Add(("thingi", System.IO.Path.GetFileNameWithoutExtension(f), m, MeanEdge(m)));
            }

        Remesh(cases[0].input, cases[0].target, quality);      // warm-up (JIT)
        var json = new JsonSerializerOptions() { IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var rows = new List<(string group, string name, Result off, Result on)>();
        foreach (var c in cases) {
            if (only != null && !only.IsMatch(c.name)) continue;
            Result off = Measure(c.input, c.target, 0, runs), on = Measure(c.input, c.target, quality, runs);
            rows.Add((c.group, c.name, off, on));
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object>() {
                ["group"] = c.group, ["case"] = c.name, ["input_tris"] = c.input.TriangleCount, ["input_self_pairs"] = MeshRepairMetrics.SelfIntersections(c.input),
                ["off"] = off, ["on"] = on }, json));
        }
        Summary(rows, quality);
        return 0;
    }

    static readonly double[] Targets = { 0.1, 0.15, 0.2, 0.3, 0.45, 0.6 };

    static Result Measure(DMesh3 input, double target, double quality, int runs)
    {
        var times = new List<double>();
        DMesh3 m = null;
        for (int k = 0; k < runs; ++k) {
            GC.Collect(); GC.WaitForPendingFinalizers();
            var sw = Stopwatch.StartNew();
            m = Remesh(input, target, quality);
            times.Add(sw.Elapsed.TotalSeconds);
        }
        times.Sort();
        var r = new Result() { Tris = m.TriangleCount, Seconds = times[times.Count / 2] };
        foreach (int t in m.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            double q = MeshRefinerBase.TriangleQuality(ref a, ref b, ref c);
            if (q < 0.1) r.Poor++;
            if (q < 1e-3) r.Flat++;
        }
        r.SelfPairs = MeshRepairMetrics.SelfIntersections(m);
        var hd = new MeshHausdorffDistance(input, m) { Tolerance = 0.01 * target };
        hd.Compute();
        r.Hausdorff = hd.UpperBound / target;
        r.NotClean = MeshRobustRepair.NotCleanReason(m, false);
        // directly: NotCleanReason stops at its first failed check (degenerate triangles come before inside-out)
        r.Inverted = MeshPostRepairCleanup.FindInvertedComponents(m).Count;
        return r;
    }

    // tools/g3cli/Commands.cs, remesh with its defaults
    static DMesh3 Remesh(DMesh3 input, double target, double quality)
    {
        DMesh3 mesh = new DMesh3(input);
        var r = new Remesher(mesh) { SmoothSpeedT = 0.5, MinTriangleQuality = quality };
        r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
        MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
        r.SetTargetEdgeLength(target);
        for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
        return mesh;
    }

    static void Summary(List<(string group, string name, Result off, Result on)> rows, double quality)
    {
        Console.WriteLine();
        Console.WriteLine($"| group | cases | q<0.1 off / on | q<1e-3 off / on | cases with q<1e-3 off / on | self-intersecting pairs off / on | cases with self-int. off / on | cases inside-out off / on | mean hausdorff/target off / on | time s off / on | worse: more pairs / more q<1e-3 / hausdorff / inside-out |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var g in rows.GroupBy(r => r.group)) {
            var l = g.ToList();
            int morePairs = l.Count(r => r.on.SelfPairs > r.off.SelfPairs), moreFlat = l.Count(r => r.on.Flat > r.off.Flat);
            int worseHd = l.Count(r => r.on.Hausdorff > r.off.Hausdorff + 0.02);       // above the two tolerances of 0.01 target
            int newInverted = l.Count(r => r.on.Inverted > 0 && r.off.Inverted == 0);
            Console.WriteLine($"| {g.Key} | {l.Count} | {l.Sum(r => r.off.Poor)} / {l.Sum(r => r.on.Poor)} | {l.Sum(r => r.off.Flat)} / {l.Sum(r => r.on.Flat)} | " +
                $"{l.Count(r => r.off.Flat > 0)} / {l.Count(r => r.on.Flat > 0)} | {l.Sum(r => r.off.SelfPairs)} / {l.Sum(r => r.on.SelfPairs)} | " +
                $"{l.Count(r => r.off.SelfPairs > 0)} / {l.Count(r => r.on.SelfPairs > 0)} | {l.Count(r => r.off.Inverted > 0)} / {l.Count(r => r.on.Inverted > 0)} | " +
                $"{l.Average(r => r.off.Hausdorff):F3} / {l.Average(r => r.on.Hausdorff):F3} | " +
                $"{l.Sum(r => r.off.Seconds):F1} / {l.Sum(r => r.on.Seconds):F1} | {morePairs} / {moreFlat} / {worseHd} / {newInverted} |");
        }
        Console.WriteLine();
        Console.WriteLine("Cases that get worse with the option (more self-intersecting pairs, more q<1e-3, a Hausdorff distance more than 0.02 target larger, or inside-out only with it):");
        foreach (var r in rows.Where(r => r.on.SelfPairs > r.off.SelfPairs || r.on.Flat > r.off.Flat || r.on.Hausdorff > r.off.Hausdorff + 0.02 || (r.on.Inverted > 0 && r.off.Inverted == 0)))
            Console.WriteLine($"- {r.name}: pairs {r.off.SelfPairs} -> {r.on.SelfPairs}, q<1e-3 {r.off.Flat} -> {r.on.Flat}, hausdorff/target {r.off.Hausdorff:F3} -> {r.on.Hausdorff:F3}, inside-out components {r.off.Inverted} -> {r.on.Inverted}");
    }

    // ---- inputs ----

    static double MeanEdge(DMesh3 m)
    {
        return m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });
    }

    // closed thin-walled tube (outer radius R, wall w): outer and inner wall joined by annular rims at both ends
    static DMesh3 Tube(int slices, int rings, double R, double w, double H)
    {
        var m = new DMesh3();
        int[,] o = new int[rings + 1, slices], i = new int[rings + 1, slices];
        for (int r = 0; r <= rings; ++r)
            for (int s = 0; s < slices; ++s) {
                double a = 2 * Math.PI * s / slices, z = H * r / rings;
                o[r, s] = m.AppendVertex(new Vector3d(R * Math.Cos(a), R * Math.Sin(a), z));
                i[r, s] = m.AppendVertex(new Vector3d((R - w) * Math.Cos(a), (R - w) * Math.Sin(a), z));
            }
        for (int s = 0; s < slices; ++s) {
            int s1 = (s + 1) % slices;
            for (int r = 0; r < rings; ++r) {
                m.AppendTriangle(o[r, s], o[r, s1], o[r + 1, s1]); m.AppendTriangle(o[r, s], o[r + 1, s1], o[r + 1, s]);
                m.AppendTriangle(i[r, s], i[r + 1, s1], i[r, s1]); m.AppendTriangle(i[r, s], i[r + 1, s], i[r + 1, s1]);
            }
            m.AppendTriangle(o[rings, s], o[rings, s1], i[rings, s1]); m.AppendTriangle(o[rings, s], i[rings, s1], i[rings, s]);
            m.AppendTriangle(o[0, s], i[0, s1], o[0, s1]); m.AppendTriangle(o[0, s], i[0, s], i[0, s1]);
        }
        return m;
    }

    static DMesh3 Cylinder(int slices, double topRadius, double height) =>
        new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = (float)topRadius, Height = (float)height, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
    static DMesh3 Cone(int slices) =>
        new ConeGenerator() { BaseRadius = 1f, Height = 2f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
    static DMesh3 Box(int edgeVertices) =>
        new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.6, 0.3)), EdgeVertices = edgeVertices, NoSharedVertices = false }.Generate().MakeDMesh();

    // an extruded gear: a CAD-like part with many feature curves and junctions, extra (collinear) points on the
    // flanks, and caps as fans around their centres (long thin triangles)
    static DMesh3 Gear(int teeth, double height, int pointsPerSide)
    {
        var poly = new List<Vector2d>();
        for (int k = 0; k < teeth; ++k) {
            double a0 = 2 * Math.PI * k / teeth, da = 2 * Math.PI / teeth;
            foreach (var (f, r) in new[] { (0.0, 0.8), (0.2, 1.0), (0.5, 1.0), (0.7, 0.8) })
                poly.Add(r * new Vector2d(Math.Cos(a0 + f * da), Math.Sin(a0 + f * da)));
        }
        var outline = new List<Vector2d>();
        for (int i = 0; i < poly.Count; ++i)
            for (int k = 0; k < pointsPerSide; ++k)
                outline.Add(Vector2d.Lerp(poly[i], poly[(i + 1) % poly.Count], (double)k / pointsPerSide));
        var m = new DMesh3();
        int n = outline.Count;
        int cb = m.AppendVertex(new Vector3d(0, 0, 0)), ct = m.AppendVertex(new Vector3d(0, 0, height));
        int[] b = outline.Select(p => m.AppendVertex(new Vector3d(p.x, p.y, 0))).ToArray();
        int[] t = outline.Select(p => m.AppendVertex(new Vector3d(p.x, p.y, height))).ToArray();
        for (int i = 0; i < n; ++i) {
            int j = (i + 1) % n;
            m.AppendTriangle(cb, b[j], b[i]);
            m.AppendTriangle(ct, t[i], t[j]);
            m.AppendTriangle(b[i], b[j], t[j]);
            m.AppendTriangle(b[i], t[j], t[i]);
        }
        return m;
    }
}
