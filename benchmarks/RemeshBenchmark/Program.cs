// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Remesher vs RemesherPro with the settings of the library's internal callers: `g3cli remesh` (20 passes, smoothing
// 0.5, projection onto the input, boundary and 30 degree creases kept) and the band remesh of
// TerrainSafetyLines.AlongSurface (8 passes, smoothing 0.5, boundary and 20 degree break lines kept, a last pass
// without smoothing, no projection). Procedurally generated meshes; optionally Thingi10K meshes (fetch_data.py).
//
//   dotnet run -c Release --project benchmarks/RemeshBenchmark -- [runs=5] [thingi=data/meshes] [only=<regex>]
//
// Prints one JSON line per case and configuration, then a markdown table (see README.md).
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
    enum Kind { G3cli, TerrainBand }

    class Case
    {
        public string Name;
        public DMesh3 Input;
        public Kind Kind;
        public double Target;
        // TerrainBand on the planar pit: the exact distance to the crest along the surface, the crest and its x range
        public Func<Vector3d, double> Distance;
        public List<Vector3d> Crest;
        public double X0, X1;
    }

    static readonly string[] Configs = { "remesher", "pro_fastest", "pro_iter" };
    const int G3cliPasses = 20;
    const double SafetyD = 5;
    const double H = SafetyD / 8;             // TerrainSafetyLines.EdgeLength default
    static readonly double SlopeAngle = 35 * MathUtil.Deg2Rad;

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        int runs = A.ContainsKey("runs") ? int.Parse(A["runs"]) : 5;
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;

        var cases = new List<Case>();
        cases.Add(G3(  "sphere_refine", Sphere(24), 0.5));
        cases.Add(G3(  "bumpy_uniform", Bumpy(Sphere(80)), 1.0));
        cases.Add(G3(  "noisy_coarsen", Noisy(Sphere(160), 0.002), 3.0));
        cases.Add(G3(  "open_stretched", Heightfield(64, 40, 4.0, (x, y) => 0.15 * Math.Sin(3 * x) * Math.Cos(2 * y)), 1.0));
        cases.Add(TerrainBand("terrain_band", 0));
        cases.Add(TerrainBand("terrain_band_bumpy", 0.3));
        if (A.ContainsKey("thingi"))
            foreach (string f in System.IO.Directory.GetFiles(A["thingi"]).OrderBy(f => f, StringComparer.Ordinal)) {
                DMesh3 m = StandardMeshReader.ReadMesh(f);
                if (m == null || m.TriangleCount == 0) continue;
                cases.Add(G3("thingi_" + System.IO.Path.GetFileNameWithoutExtension(f), m, 1.0));
            }

        var rows = new List<Dictionary<string, object>>();
        foreach (Case c in cases) {
            if (only != null && !only.IsMatch(c.Name)) continue;
            foreach (string cfg in Configs) {
                var R = Measure(c, cfg, runs);
                rows.Add(R);
                Console.WriteLine(JsonSerializer.Serialize(R, new JsonSerializerOptions() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
            }
        }
        if (only == null || only.IsMatch("terrain_safety")) {
            var R = TerrainSafetyEndToEnd(runs);
            Console.WriteLine(JsonSerializer.Serialize(R, new JsonSerializerOptions() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        }
        PrintTable(rows);
        return 0;
    }

    // ---- inputs ----

    static Case G3(string name, DMesh3 m, double targetFactor)
    {
        return new Case() { Name = name, Input = m, Kind = Kind.G3cli, Target = targetFactor * MeanEdgeLength(m) };
    }

    static DMesh3 Sphere(int edgeVertices)
    {
        return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
    }

    static DMesh3 Bumpy(DMesh3 m)
    {
        foreach (int vid in m.VertexIndices()) {
            Vector3d p = m.GetVertex(vid);
            m.SetVertex(vid, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5) + 0.05 * Math.Sin(9 * p.z)));
        }
        return m;
    }

    static DMesh3 Noisy(DMesh3 m, double amount)
    {
        var rand = new Random(31);
        foreach (int vid in m.VertexIndices())
            m.SetVertex(vid, m.GetVertex(vid) + amount * new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5));
        return m;
    }

    // nx x ny cells on [0, sx] x [0, 1]: anisotropic triangles if sx / nx != 1 / ny (64 x 40 cells on 4 x 1: aspect 2.5)
    static DMesh3 Heightfield(int nx, int ny, double sx, Func<double, double, double> z)
    {
        return Grid(nx, ny, (i, j) => { double x = sx * i / nx, y = (double)j / ny; return new Vector3d(x, y, z(x, y)); });
    }

    static DMesh3 Grid(int nx, int ny, Func<int, int, Vector3d> pos)
    {
        var m = new DMesh3();
        int[,] ids = new int[nx + 1, ny + 1];
        for (int j = 0; j <= ny; ++j)
            for (int i = 0; i <= nx; ++i)
                ids[i, j] = m.AppendVertex(pos(i, j));
        for (int j = 0; j < ny; ++j)
            for (int i = 0; i < nx; ++i) {
                m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
            }
        return m;
    }

    // pit edge: plateau z = 0 for y <= 40, a 35 degree slope down for y > 40, crest along y = 40. bumps > 0 adds
    // smooth bumps of that height (then there is no exact distance).
    static double PitHeight(double x, double y, double bumps)
    {
        double z = (y > 40) ? -(y - 40) * Math.Tan(SlopeAngle) : 0;
        return z + bumps * Math.Sin(0.9 * x) * Math.Cos(0.7 * y);
    }

    static double PitDistance(Vector3d p)
    {
        return (p.y <= 40) ? 40 - p.y : (p.y - 40) / Math.Cos(SlopeAngle);
    }

    // the 80 x 80 m pit on a 1 m grid, crest from (0, 40) to (80, 40)
    static DMesh3 Pit(double bumps)
    {
        return Grid(80, 80, (i, j) => new Vector3d(i, j, PitHeight(i, j, bumps)));
    }

    // the band TerrainSafetyLines.AlongSurface remeshes, built as there: edges longer than 2 h near the crest split
    // (refine_near_sources, horizontal distance), then the triangles within 3 d + 2 h of the crest (3D distance of
    // the centroid, plus its radius) cut out
    static Case TerrainBand(string name, double bumps)
    {
        double band = 3 * SafetyD + 2 * H;
        DMesh3 mesh = Pit(bumps);
        Func<Vector3d, double> crestDist3 = (p) => {
            double x = Math.Max(0, Math.Min(80, p.x));
            return p.Distance(new Vector3d(x, 40, 0));
        };
        var queue = new Queue<int>(mesh.EdgeIndices());
        while (queue.Count > 0) {
            int eid = queue.Dequeue();
            if (!mesh.IsEdge(eid)) continue;
            Index2i ev = mesh.GetEdgeV(eid);
            Vector3d a = mesh.GetVertex(ev.a), b = mesh.GetVertex(ev.b);
            if (a.Distance(b) <= 2 * H) continue;
            Vector2d mid = 0.5 * (a.xy + b.xy);
            double r = band + 0.5 * a.xy.Distance(b.xy);
            double hdist = new Vector2d(Math.Max(0, Math.Min(80, mid.x)), 40).Distance(mid);
            if (hdist > r) continue;
            DMesh3.EdgeSplitInfo split;
            if (mesh.SplitEdge(eid, out split) != MeshResult.Ok) continue;
            queue.Enqueue(eid); queue.Enqueue(split.eNewBN); queue.Enqueue(split.eNewCN);
            if (split.eNewDN >= 0) queue.Enqueue(split.eNewDN);
        }
        var sel = new MeshFaceSelection(mesh);
        foreach (int tid in mesh.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            mesh.GetTriVertices(tid, ref a, ref b, ref c);
            Vector3d m = (a + b + c) / 3;
            double rad = Math.Max(m.Distance(a), Math.Max(m.Distance(b), m.Distance(c)));
            if (crestDist3(m) <= band + rad) sel.Select(tid);
        }
        DMesh3 m3 = new DSubmesh3(mesh, sel).SubMesh;
        var crest = new List<Vector3d>();
        for (double x = 0; x <= 80 + 1e-9; x += 0.5 * H)
            crest.Add(new Vector3d(x, 40, 0));
        return new Case() {
            Name = name, Input = m3, Kind = Kind.TerrainBand, Target = H, Crest = crest, X0 = 0, X1 = 80,
            Distance = (bumps == 0) ? PitDistance : (Func<Vector3d, double>)null
        };
    }

    // ---- remeshing as the callers do it ----

    static DMesh3 Remesh(Case c, string cfg, double scale, bool parallel, out double seconds)
    {
        DMesh3 input = c.Input;
        if (scale != 1) {
            input = new DMesh3(c.Input);
            MeshTransforms.Scale(input, scale);
        }
        double target = scale * c.Target;
        DMesh3 mesh = new DMesh3(input);
        GC.Collect(); GC.WaitForPendingFinalizers();
        var sw = Stopwatch.StartNew();
        bool pro = cfg != "remesher";
        Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
        RemesherPro rp = r as RemesherPro;
        r.SmoothSpeedT = 0.5;
        r.EnableParallelSmooth = r.EnableParallelProjection = parallel;
        if (c.Kind == Kind.G3cli) {
            // tools/g3cli/Commands.cs, remesh with the defaults
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(target);
            if (cfg == "remesher")
                for (int k = 0; k < G3cliPasses; ++k) r.BasicRemeshPass();
            else if (cfg == "pro_fastest")
                rp.FastestRemesh(G3cliPasses);
            else
                for (int k = 0; k < G3cliPasses; ++k) rp.RemeshIteration();
        } else {
            // mesh_ops/TerrainSafety.cs, compute_along_surface
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 20);
            r.SetTargetEdgeLength(target);
            if (cfg == "remesher") {
                for (int k = 0; k < 8; ++k) r.BasicRemeshPass();
                r.EnableSmoothing = false;
                r.BasicRemeshPass();
            } else {
                if (cfg == "pro_fastest")
                    rp.FastestRemesh(8);
                else
                    for (int k = 0; k < 8; ++k) rp.RemeshIteration();
                // the last pass without smoothing over all edges, as BasicRemeshPass
                r.EnableSmoothing = false;
                rp.ResetQueue();
                rp.RemeshIteration();
            }
        }
        seconds = sw.Elapsed.TotalSeconds;
        if (scale != 1)
            MeshTransforms.Scale(mesh, 1 / scale);
        return mesh;
    }

    static Dictionary<string, object> Measure(Case c, string cfg, int runs)
    {
        var R = new Dictionary<string, object>();
        R["case"] = c.Name; R["config"] = cfg;
        R["input_tris"] = c.Input.TriangleCount;
        R["target"] = c.Target;
        Remesh(c, cfg, 1, true, out _);      // warm-up (JIT)
        var times = new List<double>();
        var hashes = new HashSet<ulong>();
        DMesh3 result = null;
        for (int k = 0; k < runs; ++k) {
            result = Remesh(c, cfg, 1, true, out double s);
            times.Add(s);
            hashes.Add(Hash(result));
        }
        times.Sort();
        R["time_s"] = times[times.Count / 2];
        R["time_min_s"] = times[0];
        R["deterministic"] = hashes.Count == 1;
        ulong h0 = Hash(result);
        R["serial_equal"] = Hash(Remesh(c, cfg, 1, false, out _)) == h0;
        R["scale_2^-10_equal"] = Hash(Remesh(c, cfg, 1.0 / 1024, true, out _)) == h0;
        R["scale_2^10_equal"] = Hash(Remesh(c, cfg, 1024, true, out _)) == h0;
        Metrics(c, result, R);
        return R;
    }

    // ---- metrics ----

    static void Metrics(Case c, DMesh3 m, Dictionary<string, object> R)
    {
        R["tris"] = m.TriangleCount;
        var len = new List<double>();
        foreach (int eid in m.EdgeIndices()) {
            Index2i ev = m.GetEdgeV(eid);
            len.Add(m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)) / c.Target);
        }
        len.Sort();
        R["len_mean"] = len.Average(); R["len_p5"] = Pct(len, 0.05); R["len_p95"] = Pct(len, 0.95);
        R["len_min"] = len[0]; R["len_max"] = len[len.Count - 1];
        // triangle quality 4 sqrt(3) area / sum of squared edge lengths: 1 equilateral, 0 degenerate
        var q = new List<double>();
        foreach (int tid in m.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, cc = Vector3d.Zero;
            m.GetTriVertices(tid, ref a, ref b, ref cc);
            double l2 = a.DistanceSquared(b) + b.DistanceSquared(cc) + cc.DistanceSquared(a);
            q.Add(l2 > 0 ? 4 * Math.Sqrt(3) * 0.5 * (b - a).Cross(cc - a).Length / l2 : 0);
        }
        q.Sort();
        R["q_min"] = q[0]; R["q_p5"] = Pct(q, 0.05); R["q_mean"] = q.Average();
        R["q_below_0.1"] = q.Count(x => x < 0.1);
        var hd = new MeshHausdorffDistance(c.Input, m) { Tolerance = 0.01 * c.Target };
        R["hausdorff_converged"] = hd.Compute() && hd.Converged;
        R["hausdorff_rel"] = hd.UpperBound / c.Target;
        R["hausdorff_lower_rel"] = hd.LowerBound / c.Target;
        R["boundary_edges"] = m.BoundaryEdgeIndices().Count();
        R["input_boundary_edges"] = c.Input.BoundaryEdgeIndices().Count();
        // every failed check (NotCleanReason stops at the first: degenerate triangles hid self-intersections)
        R["not_clean"] = c.Input.IsClosed() ? MeshRobustRepair.NotCleanReasons(m, true) : "";
        if (c.Kind == Kind.TerrainBand && c.Distance != null)
            SafetyCheck(m, c.Crest, c.Distance, c.X0 + 2 * SafetyD, c.X1 - 2 * SafetyD, R, "band_");
        // MeshFidelityMetrics against the input (fm_* fields, benchmarks/Shared/FidelityFields.cs): F-score, normals,
        // sharp edges kept (the remeshers keep 20 / 30 degree creases), Verdict triangle quality, topology
        FidelityFields.Add(c.Input, m, R, false, 20000);
    }

    static double Pct(List<double> sorted, double p) { return sorted[(int)(p * (sorted.Count - 1))]; }

    // the AlongSurface line on the remeshed band as TerrainSafetyLines computes it (heat method geodesic from the
    // vertices nearest the crest, iso line at d + 0.35 h), checked against the exact distance along the surface
    static void SafetyCheck(DMesh3 mesh, List<Vector3d> crest, Func<Vector3d, double> dist, double x0, double x1, Dictionary<string, object> R, string prefix)
    {
        var spatial = new DMeshAABBTree3(mesh, true);
        var sources = new HashSet<int>();
        foreach (Vector3d p in crest) {
            int tid = spatial.FindNearestTriangle(p, H);
            if (tid < 0) continue;
            Index3i t = mesh.GetTriangle(tid);
            int best = t.a;
            foreach (int v in new[] { t.b, t.c })
                if (mesh.GetVertex(v).DistanceSquared(p) < mesh.GetVertex(best).DistanceSquared(p)) best = v;
            sources.Add(best);
        }
        var geo = new MeshGeodesicDistance(mesh) { TimeFactor = 0.25 };
        if (!geo.Compute(sources)) { R[prefix + "geodesic_ok"] = false; return; }
        double[] d = geo.Distance;
        double level = SafetyD + 0.35 * H;
        var iso = new MeshIsoCurves(mesh, (v) => 0) {
            VertexValueF = (vid) => double.IsNaN(d[vid]) ? SafetyD : d[vid] - level,
            RootMode = MeshIsoCurves.RootfindingModes.SingleLerp
        };
        iso.Compute();
        var pts = iso.Graph.Vertices().Where(p => p.x > x0 && p.x < x1).ToList();
        LineCheck(pts, dist, R, prefix);
    }

    static void LineCheck(List<Vector3d> pts, Func<Vector3d, double> dist, Dictionary<string, object> R, string prefix)
    {
        R[prefix + "line_pts"] = pts.Count;
        if (pts.Count == 0) return;
        var err = pts.Select(p => dist(p) - SafetyD).ToList();
        R[prefix + "min_excess"] = err.Min();         // < 0: a line point closer than d (unsafe)
        R[prefix + "max_excess"] = err.Max();
        R[prefix + "mean_excess"] = err.Average();
        R[prefix + "too_close"] = err.Count(e => e < 0);
    }

    // TerrainSafetyLines AlongSurface end to end on the full pit (1 m grid) with whatever remesher the library uses
    static Dictionary<string, object> TerrainSafetyEndToEnd(int runs)
    {
        var R = new Dictionary<string, object>();
        R["case"] = "terrain_safety"; R["config"] = "library";
        DMesh3 terrain = Pit(0);
        var crest = new List<Vector3d>() { new Vector3d(0, 40, 0), new Vector3d(80, 40, 0) };
        Func<TerrainSafetyLines> run = () => {
            var s = new TerrainSafetyLines(terrain, new[] { crest }, SafetyD) { Mode = TerrainSafetyLines.Modes.AlongSurface };
            if (!s.Compute()) throw new Exception("TerrainSafetyLines failed");
            return s;
        };
        run();
        var times = new List<double>();
        var hashes = new HashSet<ulong>();
        TerrainSafetyLines sl = null;
        for (int k = 0; k < runs; ++k) {
            GC.Collect(); GC.WaitForPendingFinalizers();
            var sw = Stopwatch.StartNew();
            sl = run();
            times.Add(sw.Elapsed.TotalSeconds);
            hashes.Add(Hash(sl.RemeshedTerrain) ^ HashLines(sl.Lines));
        }
        times.Sort();
        R["time_s"] = times[times.Count / 2];
        R["deterministic"] = hashes.Count == 1;
        R["remeshed_tris"] = sl.RemeshedTerrain.TriangleCount;
        R["lines"] = sl.Lines.Count;
        LineCheck(sl.Lines.SelectMany(l => l).Where(p => p.x > 2 * SafetyD && p.x < 80 - 2 * SafetyD).ToList(), PitDistance, R, "");
        return R;
    }

    // ---- helpers ----

    static double MeanEdgeLength(DMesh3 m)
    {
        double sum = 0; int n = 0;
        foreach (int eid in m.EdgeIndices()) {
            Index2i ev = m.GetEdgeV(eid);
            sum += m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); n++;
        }
        return sum / n;
    }

    // FNV-1a over the vertex coordinate bits and the triangles
    static ulong Hash(DMesh3 m)
    {
        ulong h = 14695981039346656037;
        Action<long> add = (x) => { h ^= (ulong)x; h *= 1099511628211; };
        foreach (int vid in m.VertexIndices()) {
            Vector3d p = m.GetVertex(vid);
            add(vid); add(BitConverter.DoubleToInt64Bits(p.x)); add(BitConverter.DoubleToInt64Bits(p.y)); add(BitConverter.DoubleToInt64Bits(p.z));
        }
        foreach (int tid in m.TriangleIndices()) {
            Index3i t = m.GetTriangle(tid);
            add(tid); add(t.a); add(t.b); add(t.c);
        }
        return h;
    }

    static ulong HashLines(List<List<Vector3d>> lines)
    {
        ulong h = 14695981039346656037;
        foreach (var l in lines) {
            h ^= (ulong)l.Count; h *= 1099511628211;
            foreach (Vector3d p in l)
                foreach (double x in new[] { p.x, p.y, p.z }) { h ^= (ulong)BitConverter.DoubleToInt64Bits(x); h *= 1099511628211; }
        }
        return h;
    }

    static string F(object o, string fmt = "0.###")
    {
        return o is double d ? d.ToString(fmt, CultureInfo.InvariantCulture) : o?.ToString() ?? "";
    }

    static void PrintTable(List<Dictionary<string, object>> rows)
    {
        Console.WriteLine();
        Console.WriteLine("| case | config | time s | tris | len mean | len p5 | len p95 | q min | q p5 | q<0.1 | hausdorff/target | det. | serial= | 2^-10= | 2^10= | band min/max excess |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---|---|---|");
        foreach (var r in rows) {
            object v;
            string band = r.TryGetValue("band_min_excess", out v) ? F(v, "0.000") + " / " + F(r["band_max_excess"], "0.000") : "";
            Console.WriteLine($"| {r["case"]} | {r["config"]} | {F(r["time_s"], "0.000")} | {r["tris"]} | {F(r["len_mean"])} | {F(r["len_p5"])} | {F(r["len_p95"])} | " +
                $"{F(r["q_min"])} | {F(r["q_p5"])} | {r["q_below_0.1"]} | {F(r["hausdorff_rel"])} | {r["deterministic"]} | {r["serial_equal"]} | {r["scale_2^-10_equal"]} | {r["scale_2^10_equal"]} | {band} |");
        }
        // the further fidelity measures (relative to the input's size, see MeshFidelityMetrics)
        Console.WriteLine();
        Console.WriteLine("| case | config | F 0.1% | normal consistency | reversed | Edge Chamfer | edge recall 0.5% | edge precision 0.5% | min angle 1% worst | scaled Jacobian 1% worst | topology changes |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var r in rows) {
            if (r.ContainsKey("fm_ok") == false || (bool)r["fm_ok"] == false)
                continue;
            var q = (Dictionary<string, object>)r["fm_q_out"];
            Console.WriteLine($"| {r["case"]} | {r["config"]} | {F(r["fm_f_0001"], "0.0000")} | {F(r["fm_nc"], "0.0000")} | {F(r["fm_reversed_fwd"], "0.0000")} | " +
                $"{F(r["fm_ecd"], "0.00000")} | {F(r["fm_er_0005"], "0.000")} | {F(r["fm_ep_0005"], "0.000")} | {F(((double[])q["minangle"])[1], "0.0")} | " +
                $"{F(((double[])q["sj"])[1], "0.000")} | {r["fm_topo_changes"]} |");
        }
    }
}
