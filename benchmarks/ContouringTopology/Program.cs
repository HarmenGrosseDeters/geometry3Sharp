// Upper bound for a topology guarantee of the contouring (docs/design/CONTOURING_TOPOLOGY.md): how often does today's
// DualContouring / WindingContouring output differ in topology (components, genus per component) from the level set?
// Measurement only, public API only.
//
//   dotnet run -c Release --project benchmarks/ContouringTopology -- <out.jsonl> <input>... [-- res1,res2]
//   input = imp:<shape>[:<k>][@seed]  dense DualContouring on an implicit shape; k = the shape's feature width in
//                                     cells (shapes with a feature parameter); seed = random rotation and grid shift
//           mesh:<procedural CAD solid> | mesh:all | <mesh file>   MeshWindingNumberSolidify (adaptive,
//                                     cell = diagonal / res): the default result and the raw contour
//           probe:mesh:<solid> | probe:<mesh file>   cost probe of a per-cell certificate (Probe.cs)
// One JSON line per input and resolution; a rerun skips the pairs already in the file.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;
using gs;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: ContouringTopology <out.jsonl> <input>... [-- res,...]"); return 2; }
        string outFile = args[0];
        var inputs = new List<string>();
        int[] resolutions = { 64, 128 };
        for (int i = 1; i < args.Length; ++i) {
            if (args[i] == "--") { if (i + 1 < args.Length) resolutions = args[i + 1].Split(',').Select(int.Parse).ToArray(); break; }
            if (args[i] == "mesh:all") inputs.AddRange(Procedural.Names.Select(n => "mesh:" + n));
            else inputs.Add(args[i]);
        }
        var done = new HashSet<string>();
        if (File.Exists(outFile))
            foreach (string line in File.ReadLines(outFile))
                using (var doc = JsonDocument.Parse(line))
                    done.Add(doc.RootElement.GetProperty("input").GetString() + "|" + doc.RootElement.GetProperty("res").GetInt32());
        RunImplicit("sphere", 16, new Dictionary<string, object>());   // warm-up (JIT), not recorded

        foreach (string input in inputs) {
            string name = input.StartsWith("mesh:") || input.StartsWith("imp:") || input.StartsWith("probe:mesh:") ? input
                : input.StartsWith("probe:") ? "probe:" + Path.GetFileName(input.Substring(6)) : Path.GetFileName(input);
            foreach (int res in resolutions) {
                if (done.Contains(name + "|" + res)) continue;
                var r = new Dictionary<string, object>();
                r["input"] = name; r["res"] = res;
                try {
                    if (input.StartsWith("probe:")) {
                        string src = input.Substring(6);
                        DMesh3 m = src.StartsWith("mesh:") ? Procedural.Make(src.Substring(5)) : StandardMeshReader.ReadMesh(src);
                        r["in_tris"] = m.TriangleCount;
                        InputTruth(m, r);
                        var psw = Stopwatch.StartNew();
                        Probe.Run(m, res, r);
                        r["probe_ms"] = psw.ElapsedMilliseconds;
                        r["status"] = "ok";
                    } else if (input.StartsWith("imp:")) RunImplicit(input.Substring(4), res, r);
                    else RunWinding(input, res, r);
                } catch (Exception e) { r["status"] = "exception"; r["error"] = e.GetType().Name + ": " + e.Message; }
                string json = JsonSerializer.Serialize(r);
                File.AppendAllText(outFile, json + "\n");
                Console.WriteLine(json);
            }
        }
        return 0;
    }

    // components and the genus of each (sorted): "c=2;g=0,1". Boundary loops and bowties are separate fields, not
    // part of the signature: analyze.py counts a result that is not closed as wrong whatever its genus.
    static string Topology(DMesh3 mesh, Dictionary<string, object> r, string prefix)
    {
        var t = MeshTopologySummary.Compute(mesh);
        var genera = t.ComponentList.Select(c => c.Genus).OrderBy(g => g).ToList();
        string sig = "c=" + t.Components + ";g=" + string.Join(",", genera.Select(g => g.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        r[prefix + "components"] = t.Components; r[prefix + "genus"] = t.Genus;
        r[prefix + "boundary_loops"] = t.BoundaryLoops; r[prefix + "bowties"] = t.BowtieVertices;
        r[prefix + "sig"] = sig;
        return sig;
    }

    static void RunWinding(string input, int res, Dictionary<string, object> r)
    {
        DMesh3 mesh = input.StartsWith("mesh:") ? Procedural.Make(input.Substring(5)) : StandardMeshReader.ReadMesh(input);
        if (mesh == null || mesh.TriangleCount == 0) { r["status"] = "load_failed"; return; }
        r["in_tris"] = mesh.TriangleCount;
        InputTruth(mesh, r);
        double cell = mesh.CachedBounds.DiagonalLength / res;
        // the default result (cleanup and reduction), then the raw contour
        for (int pass = 0; pass < 2; ++pass) {
            string p = pass == 0 ? "final_" : "raw_";
            var sw = Stopwatch.StartNew();
            var s = new MeshWindingNumberSolidify(mesh) { Adaptive = true, CellSize = cell };
            if (pass == 1) { s.CleanupResult = false; s.ReduceTolerance = 0; }
            bool ok = s.Compute();
            r[p + "ms"] = sw.ElapsedMilliseconds;
            r[p + "status"] = ok ? "ok" : "failed";
            if (s.Result == null) continue;
            r[p + "tris"] = s.Result.TriangleCount;
            Topology(s.Result, r, p);
            r[p + "closed"] = s.Result.IsClosed();
            if (res <= 256) r[p + "si"] = MeshRepairMetrics.SelfIntersections(s.Result);
            r[p + "hidden_gaps"] = s.HiddenGapEdges; r[p + "hidden_walls"] = s.HiddenWallEdges;
            r[p + "refined_cells"] = s.RefinedCells; r[p + "nm_tris"] = s.NonManifoldTriangles;
            r[p + "used_cell_rel"] = s.UsedCellSize / mesh.CachedBounds.DiagonalLength;
        }
        r["status"] = "ok";
    }

    // Exact topology of the level set for a clean input (closed, oriented, no self-intersections): the surface of
    // { winding number > 0.5 } is the union of the input components across which the winding number steps between
    // 0 and 1 (a component nested in material steps 1 -> 2 and is not part of it).
    static void InputTruth(DMesh3 input, Dictionary<string, object> r)
    {
        DMesh3 mesh = new DMesh3(input, true);
        var weld = new MergeCoincidentEdges(mesh); weld.Apply();
        bool closed = mesh.IsClosed();
        int si = MeshRepairMetrics.SelfIntersections(mesh);
        int badEdges = MeshRepairMetrics.InconsistentlyOrientedEdges(mesh);
        var top = MeshTopologySummary.Compute(mesh);
        r["in_closed"] = closed; r["in_si"] = si; r["in_bad_edges"] = badEdges;
        r["in_components"] = top.Components; r["in_boundary_loops"] = top.BoundaryLoops; r["in_bowties"] = top.BowtieVertices;
        bool clean = closed && si == 0 && badEdges == 0 && top.BowtieVertices == 0;
        r["in_clean"] = clean;
        if (!clean) return;
        var tree = new DMeshAABBTree3(mesh, true);
        var cc = new MeshConnectedComponents(mesh); cc.FindConnectedT();
        var genera = new List<double>();
        int hidden = 0;
        double eps = 1e-6 * mesh.CachedBounds.DiagonalLength;
        foreach (var comp in cc.Components) {
            // the largest triangle of the component: its centroid moved off the surface to both sides
            int best = comp.Indices[0]; double area = -1;
            foreach (int tid in comp.Indices) { double a = mesh.GetTriArea(tid); if (a > area) { area = a; best = tid; } }
            Vector3d c = mesh.GetTriCentroid(best), n = mesh.GetTriNormal(best);
            double w0 = Math.Round(tree.WindingNumber(c + eps * n)), w1 = Math.Round(tree.WindingNumber(c - eps * n));
            bool onLevelSet = (w0 > 0.5) != (w1 > 0.5);
            if (!onLevelSet) { hidden++; continue; }
            var subMesh = new DSubmesh3(mesh, comp.Indices);
            genera.Add(MeshTopologySummary.Compute(subMesh.SubMesh).Genus);
        }
        genera.Sort();
        r["truth_hidden_components"] = hidden;
        r["truth_sig"] = "c=" + genera.Count + ";g=" + string.Join(",", genera.Select(g => g.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    class Fn : ImplicitFunction3d
    {
        public Func<Vector3d, double> F;
        public double Value(ref Vector3d p) => F(p);
    }

    static double Box(Vector3d p, Vector3d hs)
    {
        Vector3d q = new Vector3d(Math.Abs(p.x) - hs.x, Math.Abs(p.y) - hs.y, Math.Abs(p.z) - hs.z);
        Vector3d m = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
        return m.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0);
    }

    // Distance-like fields, inside where < 0, all inside [-1, 1]^3. w = the feature width (0 for the shapes without
    // a parameter); truth = the topology of the zero set, known by construction (null: from a fine reference run).
    static Func<Vector3d, double> MakeImplicit(string name, double w, out string truth)
    {
        Matrix3d R1 = Rot(Vector3d.AxisX, 0.37) * Rot(Vector3d.AxisY, 0.61) * Rot(Vector3d.AxisZ, 0.23);
        Matrix3d R2 = Rot(Vector3d.AxisZ, 0.71) * Rot(Vector3d.AxisX, 0.29);
        switch (name) {
            // --- feature-width families ---
            // torus with tube diameter w
            case "torus": truth = "c=1;g=1"; return p => { double a = Math.Sqrt(p.x * p.x + p.y * p.y) - 0.6; return Math.Sqrt(a * a + p.z * p.z) - w / 2; };
            // ball with a cylindrical tunnel of diameter w
            case "tunnel": truth = "c=1;g=1"; return p => Math.Max(p.Length - 0.8, w / 2 - Math.Sqrt(p.x * p.x + p.y * p.y));
            // two balls with a gap w
            case "gap": truth = "c=2;g=0,0"; return p => Math.Min((p - new Vector3d(0.45 + w / 2, 0, 0)).Length, (p + new Vector3d(0.45 + w / 2, 0, 0)).Length) - 0.45;
            // two balls joined by a rod of diameter w
            case "rod": truth = "c=1;g=0"; return p => {
                double balls = Math.Min((p - new Vector3d(0.6, 0, 0)).Length, (p + new Vector3d(0.6, 0, 0)).Length) - 0.3;
                double x = Math.Max(Math.Abs(p.x) - 0.6, 0);
                return Math.Min(balls, Math.Sqrt(x * x + p.y * p.y + p.z * p.z) - w / 2); };
            // hollow ball with wall thickness w
            case "shell": truth = "c=2;g=0,0"; return p => Math.Max(p.Length - 0.8, 0.8 - w - p.Length);
            // plate with 3 x 3 round holes of diameter w
            case "plate": truth = "c=1;g=9"; return p => {
                double d = Box(p, new Vector3d(0.7, 0.7, 0.08));
                for (int i = -1; i <= 1; ++i) for (int j = -1; j <= 1; ++j) {
                    double dx = p.x - 0.4 * i, dy = p.y - 0.4 * j;
                    d = Math.Max(d, w / 2 - Math.Sqrt(dx * dx + dy * dy));
                }
                return d; };
            // --- the shapes of benchmarks/DualContouringIntersections ---
            case "sphere": truth = "c=1;g=0"; return p => p.Length - 0.7;
            case "rotbox": truth = "c=1;g=0"; return p => Box(R1 * p, new Vector3d(0.6, 0.4, 0.3));
            case "boxminussphere": truth = null; return p => Math.Max(Box(R1 * p, new Vector3d(0.55, 0.55, 0.55)), 0.75 - (p - new Vector3d(0.5, 0.45, 0.4)).Length);
            case "twoboxes": truth = "c=1;g=0"; return p => Math.Min(Box(R1 * p, new Vector3d(0.6, 0.25, 0.25)), Box(R2 * (p - new Vector3d(0.1, 0.1, 0)), new Vector3d(0.2, 0.6, 0.3)));
            // two plates 0.05 thick, 0.03 apart
            case "slab": truth = "c=2;g=0,0"; return p => { Vector3d q = R1 * p; return Math.Min(Box(q - new Vector3d(0, 0, 0.04), new Vector3d(0.7, 0.6, 0.025)), Box(q + new Vector3d(0, 0, 0.04), new Vector3d(0.7, 0.6, 0.025))); };
            case "cone": truth = "c=1;g=0"; return p => { Vector3d q = R2 * p; double rr = Math.Sqrt(q.x * q.x + q.y * q.y); double s = Math.Sin(20 * Math.PI / 180), c = Math.Cos(20 * Math.PI / 180);
                return Math.Max(c * rr - s * (0.7 - q.z), -0.7 - q.z); };
            // gyroid sheet (about 0.05 thick) clipped to a ball
            case "gyroid": truth = null; return p => { double k = 2 * Math.PI * 1.5; double g = Math.Sin(k * p.x) * Math.Cos(k * p.y) + Math.Sin(k * p.y) * Math.Cos(k * p.z) + Math.Sin(k * p.z) * Math.Cos(k * p.x);
                return Math.Max(Math.Abs(g) / k - 0.03, p.Length - 0.8); };
        }
        throw new ArgumentException(name);
    }

    static Matrix3d Rot(Vector3d axis, double radians) => Matrix3d.AxisAngleD(axis, radians * 180 / Math.PI);

    // imp:name[:k][@seed]
    static void RunImplicit(string spec, int res, Dictionary<string, object> r)
    {
        int seed = -1;
        int at = spec.IndexOf('@');
        if (at >= 0) { seed = int.Parse(spec.Substring(at + 1)); spec = spec.Substring(0, at); }
        double k = 0;
        int colon = spec.IndexOf(':');
        if (colon >= 0) { k = double.Parse(spec.Substring(colon + 1), System.Globalization.CultureInfo.InvariantCulture); spec = spec.Substring(0, colon); }
        double cell = 2.2 / res;
        var f0 = MakeImplicit(spec, k * cell, out string truth);
        Func<Vector3d, double> f = f0;
        Vector3d shift = new Vector3d(0.1371, 0.2713, 0.3891);
        if (seed >= 0) {
            var rnd = new Random(seed);
            Matrix3d R = Rot(Vector3d.AxisX, rnd.NextDouble() * 6.28) * Rot(Vector3d.AxisY, rnd.NextDouble() * 6.28) * Rot(Vector3d.AxisZ, rnd.NextDouble() * 6.28);
            f = p => f0(R * p);
            shift = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
        }
        Vector3d o = new Vector3d(-1.1, -1.1, -1.1) + cell * shift;
        var dc = new DualContouring() {
            Implicit = new Fn() { F = f }, InsideIsLess = true, IsoValue = 0, CellSize = cell,
            Bounds = new AxisAlignedBox3d(o, o + 2.2 * Vector3d.One)
        };
        var sw = Stopwatch.StartNew();
        dc.Generate();
        r["ms"] = sw.ElapsedMilliseconds;
        r["status"] = "ok";
        r["shape"] = spec; r["k"] = k; r["seed"] = seed;
        r["tris"] = dc.Mesh.TriangleCount;
        Topology(dc.Mesh, r, "");
        r["closed"] = dc.Mesh.IsClosed();
        r["si_free"] = dc.SelfIntersectionFree;
        r["nm_tris"] = dc.NonManifoldTriangles;
        r["checked_tris"] = dc.CheckedTriangles; r["covered_tris"] = dc.CoveredTriangles;
        if (truth != null) r["truth_sig"] = truth;
    }
}
