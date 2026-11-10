// Upper bound for intersection-free dual contouring (Phase 5 item 4, docs/design/DC_INTERSECTION_FREE.md): counts the
// self-intersections of today's DualContouring / WindingContouring and how many dual polygons would need more than a
// diagonal choice under Wang's envelope criterion. Measurement only: uses the internal DualContouring diagnostics hook
// (off by default, no effect on the result).
//
//   dotnet run -c Release --project benchmarks/DualContouringIntersections -- <out.jsonl> <input>... [-- res1,res2]
//   input = a mesh file (WindingContouring via MeshWindingNumberSolidify, adaptive, cell = diagonal / res),
//           mesh:<procedural CAD solid> | mesh:all (benchmarks/SharpFeatureProbe/Procedural.cs, same path),
//           imp:<implicit> | imp:all (plain DualContouring, dense grid, res cells across the box)
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
    static readonly string[] Implicits = { "rotbox", "boxminussphere", "twoboxes", "slab", "cone", "gyroid", "sphere" };

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: DualContouringIntersections <out.jsonl> <input>... [-- res,...]"); return 2; }
        string outFile = args[0];
        var inputs = new List<string>();
        int[] resolutions = { 64, 128 };
        bool diagnostics = true;
        for (int i = 1; i < args.Length; ++i) {
            if (args[i] == "--timing") { diagnostics = false; continue; }   // hook off: only times and counts

            if (args[i] == "--") { resolutions = args[i + 1].Split(',').Select(int.Parse).ToArray(); break; }
            if (args[i] == "mesh:all") inputs.AddRange(Procedural.Names.Select(n => "mesh:" + n));
            else if (args[i] == "imp:all") inputs.AddRange(Implicits.Select(n => "imp:" + n));
            else inputs.Add(args[i]);
        }
        var done = new HashSet<string>();
        if (File.Exists(outFile))
            foreach (string line in File.ReadLines(outFile))
                using (var doc = JsonDocument.Parse(line))
                    done.Add(doc.RootElement.GetProperty("input").GetString() + "|" + doc.RootElement.GetProperty("res").GetInt32());

        DualContouring.CollectDiagnostics = diagnostics;
        DualContouring last = null;
        int generates = 0;
        DualContouring.GenerateFinished = (dc) => { last = dc; generates++; };
        RunImplicit("sphere", 16, new Dictionary<string, object>());   // warm-up (JIT), not recorded

        foreach (string input in inputs) {
            string name = input.StartsWith("mesh:") || input.StartsWith("imp:") ? input : Path.GetFileName(input);
            foreach (int res in resolutions) {
                if (done.Contains(name + "|" + res)) continue;
                var r = new Dictionary<string, object>();
                r["input"] = name; r["res"] = res; r["diagnostics"] = diagnostics;
                last = null; generates = 0;
                try {
                    if (input.StartsWith("imp:")) RunImplicit(input.Substring(4), res, r);
                    else RunWinding(input, res, r);
                    if (last != null) Record(last, r);
                    r["generates"] = generates;
                } catch (Exception e) { r["status"] = "exception"; r["error"] = e.GetType().Name + ": " + e.Message; }
                string json = JsonSerializer.Serialize(r, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
                File.AppendAllText(outFile, json + "\n");
                Console.WriteLine(json);
            }
        }
        return 0;
    }

    static void Record(DualContouring dc, Dictionary<string, object> r)
    {
        r["dc_tris"] = dc.Mesh?.TriangleCount ?? 0;
        r["si_found"] = dc.SelfIntersectionsFound;
        r["si_left"] = dc.SelfIntersectionsLeft;
        r["si_free"] = dc.SelfIntersectionFree;
        r["nonmanifold_tris"] = dc.NonManifoldTriangles;
        r["resolve_ms"] = dc.ResolveMilliseconds;
        foreach (var kv in dc.PhaseMilliseconds) r["dc_ms_" + kv.Key] = kv.Value;
        var d = dc.Diagnostics;
        if (d == null) return;
        r["si_pairs_diag"] = d.Pairs;
        r["polys"] = d.Polygons; r["transition_polys"] = d.TransitionPolygons; r["multipatch_polys"] = d.MultiPatchPolygons;
        r["degenerate_polys"] = d.DegeneratePolygons;
        r["concave0"] = d.Concave[0]; r["concave1"] = d.Concave[1]; r["concave2"] = d.Concave[2]; r["concave3"] = d.Concave[3]; r["concave4"] = d.Concave[4];
        r["onplane"] = d.OnPlane;
        r["verts"] = d.Vertices; r["multipatch_verts"] = d.MultiPatchVertices; r["verts_not_in_cell"] = d.VerticesNotStrictlyInCell;
        r["resolve_moved"] = d.ResolveMovedVertices;
        r["covered_pairs"] = d.CoveredPairs;
        string[] cls = { "unknown", "envelope_in", "envelope_out", "multipatch", "transition", "degenerate" };
        for (int c = 0; c < 6; ++c) r["si_tris_" + cls[c]] = d.IntersectingByClass[c];
    }

    static void RunWinding(string input, int res, Dictionary<string, object> r)
    {
        DMesh3 mesh = input.StartsWith("mesh:") ? Procedural.Make(input.Substring(5)) : StandardMeshReader.ReadMesh(input);
        if (mesh == null || mesh.TriangleCount == 0) { r["status"] = "load_failed"; return; }
        r["in_tris"] = mesh.TriangleCount;
        double cell = mesh.CachedBounds.DiagonalLength / res;
        var sw = Stopwatch.StartNew();
        var s = new MeshWindingNumberSolidify(mesh) { Adaptive = true, CellSize = cell };
        bool ok = s.Compute();
        r["ms"] = sw.ElapsedMilliseconds;
        r["status"] = ok ? "ok" : "failed";
        r["cell_rel"] = 1.0 / res;
        if (s.Result != null) {
            r["tris"] = s.Result.TriangleCount;
            r["final_si"] = MeshRepairMetrics.SelfIntersections(s.Result);
            r["final_clean"] = s.Result.IsClosed() && MeshRepairMetrics.SelfIntersections(s.Result) == 0;
        }
        if (s.Result != null && (int)r["final_si"] > 0) {
            // which stage: the contour itself (hole fills included), after the cleanup, after the reduction
            var raw = new MeshWindingNumberSolidify(mesh) { Adaptive = true, CellSize = cell, CleanupResult = false, ReduceTolerance = 0 };
            raw.Compute();
            r["stage_si_contour"] = raw.Result == null ? -1 : MeshRepairMetrics.SelfIntersections(raw.Result);
            r["stage_nonmanifold_tris"] = raw.NonManifoldTriangles;
            var cl = new MeshWindingNumberSolidify(mesh) { Adaptive = true, CellSize = cell, CleanupResult = true, ReduceTolerance = 0 };
            cl.Compute();
            r["stage_si_cleanup"] = cl.Result == null ? -1 : MeshRepairMetrics.SelfIntersections(cl.Result);
        }
        r["refined_cells"] = s.RefinedCells; r["hidden_gaps"] = s.HiddenGapEdges; r["hidden_walls"] = s.HiddenWallEdges;
    }

    class Fn : ImplicitFunction3d
    {
        public Func<Vector3d, double> F;
        public double Value(ref Vector3d p) => F(p);
    }

    // signed distance-like fields, inside where < 0 (InsideIsLess); all inside [-1, 1]^3
    static Func<Vector3d, double> MakeImplicit(string name)
    {
        Func<Vector3d, Vector3d, double> box = (p, hs) => {
            Vector3d q = new Vector3d(Math.Abs(p.x) - hs.x, Math.Abs(p.y) - hs.y, Math.Abs(p.z) - hs.z);
            Vector3d m = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return m.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0);
        };
        // irrational rotations, so faces and edges do not align with the grid
        Matrix3d R1 = Rot(Vector3d.AxisX, 0.37) * Rot(Vector3d.AxisY, 0.61) * Rot(Vector3d.AxisZ, 0.23);
        Matrix3d R2 = Rot(Vector3d.AxisZ, 0.71) * Rot(Vector3d.AxisX, 0.29);
        switch (name) {
            case "sphere": return p => p.Length - 0.7;
            case "rotbox": return p => box(R1 * p, new Vector3d(0.6, 0.4, 0.3));
            case "boxminussphere": return p => Math.Max(box(R1 * p, new Vector3d(0.55, 0.55, 0.55)), 0.75 - (p - new Vector3d(0.5, 0.45, 0.4)).Length);
            // union of two rotated boxes: concave creases where they meet
            case "twoboxes": return p => Math.Min(box(R1 * p, new Vector3d(0.6, 0.25, 0.25)), box(R2 * (p - new Vector3d(0.1, 0.1, 0)), new Vector3d(0.2, 0.6, 0.3)));
            // two rotated plates 0.03 apart (a gap narrower than a cell at res 64), each 0.05 thick
            case "slab": return p => { Vector3d q = R1 * p; return Math.Min(box(q - new Vector3d(0, 0, 0.04), new Vector3d(0.7, 0.6, 0.025)), box(q + new Vector3d(0, 0, 0.04), new Vector3d(0.7, 0.6, 0.025))); };
            // a cone with a sharp tip (half angle 20 degrees), capped
            case "cone": return p => { Vector3d q = R2 * p; double rr = Math.Sqrt(q.x * q.x + q.y * q.y); double s = Math.Sin(20 * Math.PI / 180), c = Math.Cos(20 * Math.PI / 180);
                return Math.Max(c * rr - s * (0.7 - q.z), -0.7 - q.z); };
            // gyroid TPMS sheet (thickness 0.3 of the level band) clipped to a sphere
            case "gyroid": return p => { double k = 2 * Math.PI * 1.5; double g = Math.Sin(k * p.x) * Math.Cos(k * p.y) + Math.Sin(k * p.y) * Math.Cos(k * p.z) + Math.Sin(k * p.z) * Math.Cos(k * p.x);
                return Math.Max(Math.Abs(g) / k - 0.03, p.Length - 0.8); };
        }
        throw new ArgumentException(name);
    }

    static Matrix3d Rot(Vector3d axis, double radians) => Matrix3d.AxisAngleD(axis, radians * 180 / Math.PI);

    // imp:name@seed: the field turned by a random rotation and the grid shifted by a random fraction of a cell
    static void RunImplicit(string name, int res, Dictionary<string, object> r)
    {
        int seed = -1;
        int at = name.IndexOf('@');
        if (at >= 0) { seed = int.Parse(name.Substring(at + 1)); name = name.Substring(0, at); }
        var f0 = MakeImplicit(name);
        Func<Vector3d, double> f = f0;
        Vector3d shift = new Vector3d(0.1371, 0.2713, 0.3891);
        if (seed >= 0) {
            var rnd = new Random(seed);
            Matrix3d R = Rot(Vector3d.AxisX, rnd.NextDouble() * 6.28) * Rot(Vector3d.AxisY, rnd.NextDouble() * 6.28) * Rot(Vector3d.AxisZ, rnd.NextDouble() * 6.28);
            f = p => f0(R * p);
            shift = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
        }
        // grid offset by an odd fraction, so faces do not sit on grid nodes
        double cell = 2.2 / res;
        Vector3d o = new Vector3d(-1.1, -1.1, -1.1) + cell * shift;
        var dc = new DualContouring() {
            Implicit = new Fn() { F = f }, InsideIsLess = true, IsoValue = 0, CellSize = cell,
            Bounds = new AxisAlignedBox3d(o, o + 2.2 * Vector3d.One)
        };
        var sw = Stopwatch.StartNew();
        dc.Generate();
        r["ms"] = sw.ElapsedMilliseconds;
        r["status"] = "ok";
        r["tris"] = dc.Mesh.TriangleCount;
        r["final_si"] = MeshRepairMetrics.SelfIntersections(dc.Mesh);
        r["final_clean"] = dc.Mesh.IsClosed() && (int)r["final_si"] == 0;
    }
}
