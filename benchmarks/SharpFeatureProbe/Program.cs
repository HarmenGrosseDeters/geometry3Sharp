// Sharp-feature probe for the alpha wrap (Alpha Wrap 3.0 lever 6, owner 2026-11-09): measures how well the wrap and three
// alternatives keep the input's sharp edges, with the guarantees and the time. No library change; see README.md.
//
//   dotnet run -c Release --project benchmarks/SharpFeatureProbe -- <out.jsonl> <input>... [-- variants A,B,C1,...]
//   input = a mesh file, or proc:box | lshape | octant | slot | vnotch | chamfer | proc:all
//
// Variants (one JSON line per input and variant; a rerun skips the pairs already in the file):
//   A    the current pipeline (MeshAlphaWrap 300 / 3000, SimplifyWithin 0.5 offset, as MeshRobustRepair runs it)
//   B    MeshWindingNumberSolidify (dual contouring on the winding number, adaptive), cell size searched for A's triangle count
//   C1   post-pass on A: vertices toward the QEF point of the input's planes shifted by one offset (the offset surface's miter)
//   C2   post-pass on A: planes through the input itself (the vertex may come down to 0.1 offset from it)
//   C3   C1 on the raw wrap, then the pipeline's simplification
//   R    the raw wrap (before the simplification), for the guarantees
//   D1   A with half the alpha (the same offset): an upper bound for a locally smaller alpha near creases (old option 4)
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
    static readonly Func<bool> never = () => false;

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: SharpFeatureProbe <out.jsonl> <input>... [-- variants]"); return 2; }
        string outFile = args[0];
        var inputs = new List<string>();
        string[] variants = { "A", "B", "C1", "C2", "C3", "D1" };
        for (int i = 1; i < args.Length; ++i) {
            if (args[i] == "--") { variants = args[i + 1].Split(','); break; }
            if (args[i] == "proc:all") inputs.AddRange(Procedural.Names.Select(n => "proc:" + n)); else inputs.Add(args[i]);
        }
        var done = new HashSet<string>();
        if (File.Exists(outFile))
            foreach (string line in File.ReadLines(outFile)) {
                using (var doc = JsonDocument.Parse(line)) done.Add(doc.RootElement.GetProperty("file").GetString() + "|" + doc.RootElement.GetProperty("variant").GetString());
            }
        // warm-up: the pipeline on a procedural input (JIT), not recorded
        Pipeline(Procedural.Make("lshape"), 300, 3000);
        foreach (string path in inputs) {
            string name = path.StartsWith("proc:") ? path : Path.GetFileName(path);
            if (variants.All(v => done.Contains(name + "|" + v))) continue;
            DMesh3 input = Load(path);
            if (input == null) { Console.Error.WriteLine("cannot load " + path); continue; }
            Wrapped aCached = null;
            foreach (string v in variants) {
                if (done.Contains(name + "|" + v)) continue;
                if (aCached == null) aCached = Pipeline(input, 300, 3000);
                var r = new Dictionary<string, object>();
                r["file"] = name; r["variant"] = v;
                try { Run(v, input, aCached, r); } catch (Exception e) { r["status"] = "exception"; r["error"] = e.GetType().Name + ": " + e.Message; }
                string json = JsonSerializer.Serialize(r, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
                File.AppendAllText(outFile, json + "\n");
                Console.WriteLine(name + " " + v + " " + (r.ContainsKey("status") ? r["status"] : "") + " tris=" + (r.ContainsKey("tris") ? r["tris"] : "-") + " ms=" + (r.ContainsKey("ms") ? r["ms"] : "-"));
            }
        }
        return 0;
    }

    static DMesh3 Load(string path)
    {
        if (path.StartsWith("proc:")) return Procedural.Make(path.Substring(5));
        var builder = new DMesh3Builder();
        IOReadResult rr = StandardMeshReader.ReadFile(path, new ReadOptions(), builder);
        if (rr.code != IOCode.Ok || builder.Meshes.Count == 0) return null;
        DMesh3 mesh = builder.Meshes[0];
        for (int i = 1; i < builder.Meshes.Count; ++i) MeshEditor.Append(mesh, builder.Meshes[i]);
        return mesh;
    }

    class Wrapped { public DMesh3 Raw, Final; public double Alpha, Delta; public long Ms, RawMs; public string Status; }

    // the pipeline as MeshRobustRepair runs it (benchmarks/MeshRepairBenchmark WrapPipeline, quality pass off)
    static Wrapped Pipeline(DMesh3 input, double relAlpha, double relOffset)
    {
        var sw = Stopwatch.StartNew();
        var wrap = new MeshAlphaWrap(input) { RelativeAlpha = relAlpha, RelativeOffset = relOffset };
        wrap.Compute();
        var w = new Wrapped { Raw = wrap.Result, Alpha = wrap.UsedAlpha, Delta = wrap.UsedOffset, Status = wrap.Status.ToString(), RawMs = sw.ElapsedMilliseconds };
        if (wrap.Result == null || wrap.Result.TriangleCount == 0 || (wrap.Status != MeshAlphaWrap.WrapStatus.Success && wrap.Status != MeshAlphaWrap.WrapStatus.PointLimitReached)) {
            w.Ms = sw.ElapsedMilliseconds; return w;
        }
        w.Final = Simplify(w.Raw, w.Delta);
        w.Ms = sw.ElapsedMilliseconds;
        return w;
    }

    static DMesh3 Simplify(DMesh3 raw, double delta)
    {
        return MeshRobustRepair.SimplifyWithin(raw, 0.5 * delta, 0.5 * delta, true, never, out int subdiv, new Dictionary<string, long>()) ?? raw;
    }

    static void Run(string variant, DMesh3 input, Wrapped a, Dictionary<string, object> r)
    {
        double diag = input.CachedBounds.DiagonalLength;
        r["input_tris"] = input.TriangleCount;
        DMesh3 oriented = WindingContouring.OrientedCopy(input);
        var sw = Stopwatch.StartNew();
        if (a.Final == null) { r["status"] = "wrap_" + a.Status; return; }
        double alpha = a.Alpha, delta = a.Delta;
        DMesh3 result = null;
        long ms = 0;
        var inputTree = new DMeshAABBTree3(oriented, true);
        switch (variant) {
            case "A": result = a.Final; ms = a.Ms; r["raw_tris"] = a.Raw.TriangleCount; r["raw_ms"] = a.RawMs; break;
            case "R": result = a.Raw; ms = a.RawMs; break;
            case "D1": {
                if (a.Raw.TriangleCount > int.Parse(Environment.GetEnvironmentVariable("PROBE_D1_MAX") ?? "120000")) { r["status"] = "skipped_large"; return; }
                Wrapped d = Pipeline(input, 600, 3000);
                if (d.Final == null) { r["status"] = "wrap_" + d.Status; return; }
                result = d.Final; ms = d.Ms; r["raw_tris"] = d.Raw.TriangleCount; alpha = d.Alpha; delta = d.Delta; r["variant_alpha"] = d.Alpha;
                break;
            }
            case "B": result = RunB(input, a.Final, r, out ms); if (result == null) { r["status"] = "solidify_failed"; return; } break;
            case "C1": case "C2": case "C3": {
                var o = variant == "C2" ? new SnapOptions { Shift = 0, MinDistance = 0.1 } : new SnapOptions();
                var st = new SnapStats();
                var inGrid = new TriangleHashGrid(oriented.CachedBounds.Min, Math.Max(alpha, 1e-12));
                foreach (int tid in oriented.TriangleIndices()) inGrid.Insert(tid, Snap.Box(Snap.Tri(oriented, tid)));
                DMesh3 snapped = Snap.Run(variant == "C3" ? a.Raw : a.Final, oriented, inputTree, inGrid, alpha, delta, o, st);
                long snapMs = (long)st.Ms;
                if (variant == "C3") {
                    var s2 = Stopwatch.StartNew();
                    result = Simplify(snapped, delta);
                    ms = a.RawMs + snapMs + s2.ElapsedMilliseconds;
                } else {
                    result = snapped; ms = a.Ms + snapMs;
                }
                r["snap_ms"] = snapMs; r["snap_attempts"] = st.Attempts; r["snap_moved"] = st.Moved; r["snap_rej_distance"] = st.RejDistance;
                r["snap_rej_normal"] = st.RejNormal; r["snap_rej_angle"] = st.RejAngle; r["snap_rej_input"] = st.RejInput; r["snap_rej_self"] = st.RejSelf;
                double tf = 1000.0 / Stopwatch.Frequency; r["snap_t_planes"] = st.TPlanes * tf; r["snap_t_dist"] = st.TDist * tf; r["snap_t_input"] = st.TInput * tf; r["snap_t_self"] = st.TSelf * tf; r["snap_t_qef"] = st.TOther * tf;
                r["snap_noplanes"] = st.NoPlanes; r["snap_tiny"] = st.Tiny;
                break;
            }
            default: throw new ArgumentException(variant);
        }
        r["ms"] = ms;
        r["alpha"] = alpha; r["delta"] = delta; r["diag"] = diag;
        Measure(input, oriented, inputTree, result, alpha, delta, r);
        r["status"] = "ok";
    }

    // dual contouring on the winding number at about the triangle count of the wrap
    static DMesh3 RunB(DMesh3 input, DMesh3 reference, Dictionary<string, object> r, out long ms)
    {
        int target = reference.TriangleCount;
        double area = 0;
        foreach (int tid in reference.TriangleIndices()) area += reference.GetTriArea(tid);
        double h = Math.Sqrt(3 * area / target);
        DMesh3 best = null; double bestErr = double.MaxValue, bestH = h; ms = 0;
        long search = 0;
        for (int it = 0; it < 4; ++it) {
            var sw = Stopwatch.StartNew();
            var s = new MeshWindingNumberSolidify(input) { Adaptive = true, CellSize = h };
            bool ok = s.Compute();
            long t = sw.ElapsedMilliseconds;
            search += t;
            if (!ok || s.Result == null || s.Result.TriangleCount == 0) { h *= 0.7; continue; }
            double ratio = (double)s.Result.TriangleCount / target;
            double err = Math.Abs(Math.Log(ratio));
            if (err < bestErr) { bestErr = err; best = s.Result; bestH = h; ms = t; }
            if (ratio > 0.8 && ratio < 1.25) break;
            h *= MathUtil.Clamp(Math.Sqrt(ratio), 0.5, 2);
        }
        r["b_cell"] = bestH; r["b_cell_rel"] = bestH / input.CachedBounds.DiagonalLength; r["b_search_ms"] = search;
        return best;
    }

    static void Measure(DMesh3 input, DMesh3 oriented, DMeshAABBTree3 inputTree, DMesh3 result, double alpha, double delta, Dictionary<string, object> r)
    {
        r["tris"] = result.TriangleCount; r["verts"] = result.VertexCount;
        r["not_clean"] = MeshRobustRepair.NotCleanReasons(result, true);
        r["clean"] = (string)r["not_clean"] == "";
        // angles
        var minA = new List<double>(); int lt05 = 0, gt170 = 0;
        foreach (int tid in result.TriangleIndices()) {
            Triangle3d t = Snap.Tri(result, tid);
            double mn = Snap.MinAngle(t.V0, t.V1, t.V2), mx = Snap.MaxAngle(t.V0, t.V1, t.V2);
            minA.Add(mn); if (mn < 0.5) lt05++; if (mx > 170) gt170++;
        }
        minA.Sort();
        r["angle_min"] = minA.Count > 0 ? minA[0] : double.NaN;
        r["angle_p01"] = minA.Count > 0 ? minA[(int)(0.01 * (minA.Count - 1))] : double.NaN;
        r["angle_lt05"] = lt05; r["angle_gt170"] = gt170;
        // offset band: vertex distances to the input, in offsets (lower) and in alpha + offset (upper)
        double dmin = double.MaxValue, dmax = 0;
        foreach (int vid in result.VertexIndices()) {
            double d2; inputTree.FindNearestTriangle(result.GetVertex(vid), out d2);
            double d = Math.Sqrt(d2);
            dmin = Math.Min(dmin, d); dmax = Math.Max(dmax, d);
        }
        r["vdist_min_over_offset"] = dmin / delta; r["vdist_max_over_band"] = dmax / (alpha + delta);
        // does the result cut the input (a wrap must enclose it)? triangles of the result intersecting the input
        var resTree = new DMeshAABBTree3(result, true);
        r["hits_input"] = resTree.TestIntersection(inputTree);
        if (Environment.GetEnvironmentVariable("PROBE_DEBUG") == "1" && (bool)r["hits_input"]) {
            int n = 0;
            foreach (int tid in result.TriangleIndices()) {
                Triangle3d t = Snap.Tri(result, tid);
                if (inputTree.TestIntersection(t) && n++ < 3) {
                    double d0, d1, d2; inputTree.FindNearestTriangle(t.V0, out d0); inputTree.FindNearestTriangle(t.V1, out d1); inputTree.FindNearestTriangle(t.V2, out d2);
                    Console.Error.WriteLine($"hit tri {tid}: {t.V0} {t.V1} {t.V2} dists/offset {Math.Sqrt(d0) / delta:F2} {Math.Sqrt(d1) / delta:F2} {Math.Sqrt(d2) / delta:F2}");
                }
            }
            Console.Error.WriteLine("triangles hitting: " + n);
        }
        FidelityFields.Add(input, result, r, withInputQuality: false);
        Crease.Add(oriented, result, alpha, delta, r);
    }
}
