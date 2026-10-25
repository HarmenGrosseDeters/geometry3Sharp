// Runs MeshAutoRepair on one mesh file and prints one JSON line with the result
// (mesh statistics and quality metrics before and after the repair).
// Usage: MeshRepairBenchmark <meshfile> [std|soup|manual] [none|meters|geo] [tolerance factor]
//   std    = StandardMeshReader defaults (STL vertex welding, DMesh3Builder)
//   soup   = STL triangle soup, no vertex welding
//   manual = weld by exact position ourselves and call DMesh3.AppendTriangle directly
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using g3;
using gs;
using MeshRepairBenchmark;

class Program
{
    static int Main(string[] args)
    {
        var r = new System.Collections.Generic.Dictionary<string, object>();
        r["file"] = System.IO.Path.GetFileName(args[0]);
        string stage = "load";
        var sw = Stopwatch.StartNew();
        try {
            string mode = args.Length > 1 ? args[1] : "std";
            r["mode"] = mode;
            ReadOptions opts = new ReadOptions();
            if (mode != "std")
                opts.CustomFlags.Integers[STLReader.StrategyFlag] = (int)STLReader.Strategy.NoProcessing;
            DMesh3Builder builder = new DMesh3Builder();
            IOReadResult rr = StandardMeshReader.ReadFile(args[0], opts, builder);
            if (rr.code != IOCode.Ok || builder.Meshes.Count == 0) {
                r["status"] = "load_failed"; r["error"] = rr.message;
                Emit(r); return 0;
            }
            DMesh3 mesh = builder.Meshes[0];
            for (int i = 1; i < builder.Meshes.Count; ++i)
                MeshEditor.Append(mesh, builder.Meshes[i]);
            if (mode == "manual")
                mesh = ManualBuild(mesh, r);
            // optional transform: "meters" = scale 0.001, "geo" = translate far from origin
            string xform = args.Length > 2 ? args[2] : "none";
            r["xform"] = xform;
            if (xform == "meters")
                MeshTransforms.Scale(mesh, 0.001);
            else if (xform == "geo")
                MeshTransforms.Translate(mesh, new Vector3d(500000, 5000000, 0));
            r["load_ms"] = sw.ElapsedMilliseconds;
            Stats(mesh, r, "before");
            // AutoRepair modifies the mesh in place; keep the input for solidify and deviation
            DMesh3 original = new DMesh3(mesh);

            stage = "repair";
            sw.Restart();
            // BENCH_REPAIR: default = MeshAutoRepair, cleanup = MeshAutoRepair with PostRepairCleanup,
            // smoothfill = cleanup + HoleFillMode.Smoothed, interior = cleanup + RemoveMode.Interior (fast winding number),
            // solidify_dc / solidify_dc_adaptive / solidify_mc = MeshWindingNumberSolidify (dual contouring on a grid /
            // on an adaptive octree / marching cubes) on the input,
            // hybrid = cleanup; if the result is not topologically clean, solidify_dc_adaptive of the repaired mesh,
            // instr = InstrumentedAutoRepair, records cheap quality metrics after every pipeline step
            string repairMode = Environment.GetEnvironmentVariable("BENCH_REPAIR") ?? "default";
            r["repair_mode"] = repairMode;
            double tolFactor = 1;
            // optional 4th arg: multiply default tolerances (e.g. 0.001 when the data is in meters)
            if (args.Length > 3) {
                tolFactor = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
                r["tol_factor"] = tolFactor;
            }
            DMesh3 result;
            bool ok;
            if (repairMode == "boolean") {
                result = RunBooleans(original, r);
                ok = true;
            } else if (repairMode == "instr") {
                var repair = new InstrumentedAutoRepair(mesh);
                repair.RepairTolerance *= tolFactor;
                repair.MinEdgeLengthTol *= tolFactor;
                var stages = new System.Collections.Generic.List<object>();
                repair.OnStage = (name, sm) => {
                    var q = new System.Collections.Generic.Dictionary<string, object>();
                    q["stage"] = name;
                    Quality(sm, q, "q", false);
                    q["tris"] = sm.TriangleCount;
                    q["boundary_edges"] = MeshIterators.BoundaryEdges(sm).Count();
                    q["bowties"] = sm.VertexIndices().Count(v => sm.IsBowtieVertex(v));
                    stages.Add(q);
                };
                ok = repair.Apply();
                result = repair.Mesh;
                r["stages"] = stages;
            } else if (repairMode.StartsWith("solidify_")) {
                result = Solidify(original, repairMode == "solidify_mc" ? MeshWindingNumberSolidify.Methods.MarchingCubes
                    : MeshWindingNumberSolidify.Methods.DualContouring, repairMode == "solidify_dc_adaptive", r);
                ok = true;
            } else {
                var repair = new MeshAutoRepair(mesh);
                repair.RepairTolerance *= tolFactor;
                repair.MinEdgeLengthTol *= tolFactor;
                repair.PostRepairCleanup = repairMode != "default";
                if (repairMode == "interior")
                    repair.RemoveMode = MeshAutoRepair.RemoveModes.Interior;
                if (repairMode == "smoothfill")
                    repair.HoleFillMode = MeshAutoRepair.HoleFillModes.Smoothed;
                ok = repair.Apply();
                result = repair.Mesh;
                if (repairMode == "hybrid") {
                    double areaEps = 1e-12 * result.CachedBounds.DiagonalLength * result.CachedBounds.DiagonalLength;
                    bool clean = result.IsClosed() && !MeshIterators.BowtieVertices(result).Any()
                        && MeshPostRepairCleanup.FindInvertedComponents(result).Count == 0
                        && !result.TriangleIndices().Any(t => result.GetTriArea(t) < areaEps);
                    r["hybrid_fallback"] = !clean;
                    if (!clean)
                        result = Solidify(result, MeshWindingNumberSolidify.Methods.DualContouring, true, r);
                }
            }
            r["repair_ms"] = sw.ElapsedMilliseconds;
            r["apply_returned"] = ok;

            stage = "stats_after";
            Stats(result, r, "after");
            Deviation(original, result, r);
            if (repairMode != "boolean")
                Fidelity(original, result, r);
            bool valid = true;
            try { result.CheckValidity(false, FailMode.ReturnOnly); } catch { valid = false; }
            r["after_valid"] = valid;
            r["status"] = result.TriangleCount == 0 ? "emptied"
                        : result.IsClosed() ? "closed" : "not_closed";
        } catch (Exception e) {
            r["status"] = "exception";
            r["stage"] = stage;
            r["ms"] = sw.ElapsedMilliseconds;
            r["error"] = e.GetType().Name + ": " + e.Message;
            r["trace"] = string.Join(" | ", (e.StackTrace ?? "").Split('\n')
                .Select(l => l.Trim()).Where(l => l.StartsWith("at g3.")).Take(4)
                .Select(l => l.Substring(3).Split(" in ")[0]));
        }
        Emit(r);
        return 0;
    }

    // Booleans of the input A with a rotated, shifted copy B that overlaps it. Checks that hold for any shape:
    // all results closed, V(A u B) + V(A n B) = V(A) + V(B), V(A \ B) = V(A) - V(A n B).
    // Volumes of A and B are those of their solidified versions, at the same cell size.
    static DMesh3 RunBooleans(DMesh3 a, System.Collections.Generic.Dictionary<string, object> r)
    {
        int res = int.Parse(Environment.GetEnvironmentVariable("BENCH_SOLID_RES") ?? "128");
        DMesh3 b = new DMesh3(a);
        AxisAlignedBox3d bounds = a.CachedBounds;
        Vector3d c = bounds.Center;
        MeshTransforms.Rotate(b, c, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
        MeshTransforms.Translate(b, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
        AxisAlignedBox3d all = bounds; all.Contain(b.CachedBounds);
        double cellSize = all.DiagonalLength / res;
        r["bool_cell"] = cellSize;

        var sw = Stopwatch.StartNew();
        Func<DMesh3, DMesh3> solid = (m) => {
            var s = new MeshWindingNumberSolidify(m) { CellSize = cellSize, Adaptive = true };
            s.Compute();
            return s.Result;
        };
        double vA = Vol(solid(a)), vB = Vol(solid(b));
        var results = new System.Collections.Generic.Dictionary<string, DMesh3>();
        var ta = new DMeshAABBTree3(a, true); var tb = new DMeshAABBTree3(b, true);
        ta.FastWindingNumber(Vector3d.Zero); tb.FastWindingNumber(Vector3d.Zero);
        foreach (var op in new[] { MeshWindingNumberBoolean.Operations.Union, MeshWindingNumberBoolean.Operations.Intersection,
                                   MeshWindingNumberBoolean.Operations.Difference }) {
            var boolean = new MeshWindingNumberBoolean(a, b, op) { CellSize = cellSize };
            boolean.Compute();
            DMesh3 m = boolean.Result ?? new DMesh3();
            string key = op.ToString().ToLower();
            results[key] = m;
            r["bool_" + key + "_volume"] = Vol(m);
            r["bool_" + key + "_closed"] = m.IsClosed();
            r["bool_" + key + "_bowties"] = MeshIterators.BowtieVertices(m).Count();
            r["bool_" + key + "_tris"] = m.TriangleCount;
            // against the boolean of the inputs' winding number solids (the exact result of the method);
            // not part of bool_ms
            sw.Stop();
            Func<double[], double> combine = MeshWindingNumberBoolean.CombineF(op);
            double missing, extra;
            r["bool_" + key + "_iou"] = MeshRepairMetrics.VolumeIoU(
                (p) => combine(new[] { ta.FastWindingNumber(p), tb.FastWindingNumber(p) }) > 0.5, all, m, out missing, out extra);
            r["bool_" + key + "_missing"] = missing;
            r["bool_" + key + "_extra"] = extra;
            r["bool_" + key + "_selfint_exact"] = m.TriangleCount <= 300000 ? MeshRepairMetrics.SelfIntersections(m) : -1;
            sw.Start();
        }
        r["bool_ms"] = sw.ElapsedMilliseconds;
        double vU = Vol(results["union"]), vI = Vol(results["intersection"]), vD = Vol(results["difference"]);
        r["bool_vA"] = vA; r["bool_vB"] = vB;
        r["bool_ie_error"] = (vA + vB) > 0 ? Math.Abs(vU + vI - vA - vB) / (vA + vB) : 0;
        r["bool_diff_error"] = vA > 0 ? Math.Abs(vD - (vA - vI)) / vA : 0;
        return results["union"];
    }
    static double Vol(DMesh3 m) { return m.TriangleCount == 0 ? 0 : MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }

    // winding number solidify of the input (library class), BENCH_SOLID_RES cells along the diagonal
    static DMesh3 Solidify(DMesh3 input, MeshWindingNumberSolidify.Methods method, bool adaptive, System.Collections.Generic.Dictionary<string, object> r)
    {
        int res = int.Parse(Environment.GetEnvironmentVariable("BENCH_SOLID_RES") ?? "128");
        var sw = Stopwatch.StartNew();
        var solid = new MeshWindingNumberSolidify(input) { GridResolution = res, Method = method, Adaptive = adaptive };
        string red = Environment.GetEnvironmentVariable("BENCH_SOLID_REDUCE");
        if (red != null) solid.ReduceTolerance = double.Parse(red, System.Globalization.CultureInfo.InvariantCulture);
        string clean = Environment.GetEnvironmentVariable("BENCH_SOLID_CLEANUP");
        if (clean != null) solid.CleanupResult = clean == "1";
        solid.Compute();
        r["solidify_res"] = res;
        r["solidify_method"] = method.ToString() + (adaptive ? " adaptive" : "");
        r["solidify_evaluations"] = solid.WindingEvaluations;
        r["solidify_phases"] = solid.PhaseMilliseconds;
        r["solidify_nonmanifold_tris"] = solid.NonManifoldTriangles;
        r["solidify_hidden_gap_edges"] = solid.HiddenGapEdges;
        r["solidify_used_cell_size"] = solid.UsedCellSize;
        r["solidify_ms"] = sw.ElapsedMilliseconds;
        return solid.Result ?? new DMesh3();
    }

    // distance between input and result surfaces (vertex samples, both directions), relative to the bbox diagonal:
    // dev_* = result vertices to input (added geometry), back_* = input vertices to result (lost geometry)
    static void Deviation(DMesh3 input, DMesh3 result, System.Collections.Generic.Dictionary<string, object> r)
    {
        if (input.TriangleCount == 0 || result.TriangleCount == 0)
            return;
        double diag = input.CachedBounds.DiagonalLength;
        Vector2d fwd = one_sided(result, input), back = one_sided(input, result);
        r["dev_mean_rel"] = fwd.x / diag; r["dev_max_rel"] = fwd.y / diag;
        r["back_mean_rel"] = back.x / diag; r["back_max_rel"] = back.y / diag;
    }
    static Vector2d one_sided(DMesh3 from, DMesh3 to)
    {
        var spatial = new DMeshAABBTree3(to, true);
        double sum = 0, max = 0;
        int n = 0;
        foreach (int vid in from.VertexIndices()) {
            if (from.GetVtxEdgeCount(vid) == 0) continue;
            Vector3d v = from.GetVertex(vid);
            int tid = spatial.FindNearestTriangle(v);
            if (tid < 0) continue;
            double d = Math.Sqrt(MeshQueries.TriDistanceSqr(to, tid, v));
            sum += d; max = Math.Max(max, d); n++;
        }
        return new Vector2d(n > 0 ? sum / n : 0, max);
    }

    // Fidelity to the input without a reference result: certified Hausdorff distances in both directions
    // (relative to the bbox diagonal; hd_fwd = result to input, hd_back = input to result, lower and upper
    // bound), and agreement with the input's winding number solid (IoU, missing and extra volume)
    static void Fidelity(DMesh3 input, DMesh3 result, System.Collections.Generic.Dictionary<string, object> r)
    {
        if (input.TriangleCount == 0 || result.TriangleCount == 0)
            return;
        double diag = input.CachedBounds.DiagonalLength;
        var sw = Stopwatch.StartNew();
        foreach (bool fwd in new[] { true, false }) {
            var h = new MeshHausdorffDistance(fwd ? result : input, fwd ? input : result) {
                Symmetric = false, RelativeTolerance = 1e-3, MaxSubdivisions = 2000000
            };
            if (h.Compute()) {
                string k = fwd ? "hd_fwd" : "hd_back";
                r[k + "_lo_rel"] = h.LowerBound / diag;
                r[k + "_hi_rel"] = h.UpperBound / diag;
                r[k + "_converged"] = h.Converged;
            }
        }
        double missing, extra;
        r["iou"] = MeshRepairMetrics.VolumeIoU(input, result, out missing, out extra);
        r["iou_missing"] = missing;
        r["iou_extra"] = extra;
        r["fidelity_ms"] = sw.ElapsedMilliseconds;
    }

    // naive wrapper-style construction: exact-position weld, AppendTriangle, skip failures
    static DMesh3 ManualBuild(DMesh3 soup, System.Collections.Generic.Dictionary<string, object> r)
    {
        var result = new DMesh3();
        var map = new System.Collections.Generic.Dictionary<Vector3d, int>();
        int nNonManifold = 0, nInvalid = 0;
        foreach (int tid in soup.TriangleIndices()) {
            Index3i t = soup.GetTriangle(tid);
            Index3i nt = Index3i.Zero;
            for (int j = 0; j < 3; ++j) {
                Vector3d v = soup.GetVertex(t[j]);
                if (!map.TryGetValue(v, out int vid)) { vid = result.AppendVertex(v); map[v] = vid; }
                nt[j] = vid;
            }
            int res = result.AppendTriangle(nt);
            if (res == DMesh3.NonManifoldID) nNonManifold++;
            else if (res < 0) nInvalid++;
        }
        r["build_skipped_nonmanifold"] = nNonManifold;
        r["build_skipped_invalid"] = nInvalid;
        return result;
    }

    // topological and geometric quality checks
    static void Quality(DMesh3 m, System.Collections.Generic.Dictionary<string, object> r, string p, bool bExpensive = true)
    {
        double diag = Math.Max(m.CachedBounds.DiagonalLength, MathUtil.Epsilon);
        double areaEps = 1e-12 * diag * diag;

        // degenerate (near-zero area) and sliver triangles
        int nDegen = 0, nSliver = 0;
        foreach (int tid in m.TriangleIndices()) {
            m.GetTriVertices(tid, ref vA, ref vB, ref vC);
            double area = MathUtil.Area(vA, vB, vC);
            if (area < areaEps) { nDegen++; continue; }
            double maxEdge = Math.Max(vA.Distance(vB), Math.Max(vB.Distance(vC), vC.Distance(vA)));
            // ratio of longest edge to height over it; equilateral ~ 1.15
            if (maxEdge * maxEdge / (2 * area) > 1000) nSliver++;
        }
        r[p + "_degenerate_tris"] = nDegen;
        r[p + "_sliver_tris"] = nSliver;

        // orientation consistency and fold-overs on interior edges
        int nFlippedEdges = 0, nFolds = 0;
        double foldCos = Math.Cos(175.0 * MathUtil.Deg2Rad);
        foreach (int eid in m.EdgeIndices()) {
            Index2i et = m.GetEdgeT(eid);
            if (et.b == DMesh3.InvalidID) continue;
            Index2i ev = m.GetEdgeV(eid);
            bool dirA = edge_dir(m.GetTriangle(et.a), ev.a, ev.b);
            bool dirB = edge_dir(m.GetTriangle(et.b), ev.a, ev.b);
            if (dirA == dirB) { nFlippedEdges++; continue; }
            if (m.GetTriArea(et.a) < areaEps || m.GetTriArea(et.b) < areaEps) continue;
            if (m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b)) < foldCos) nFolds++;
        }
        r[p + "_inconsistent_orient_edges"] = nFlippedEdges;
        r[p + "_folded_edges_gt175deg"] = nFolds;

        // duplicate triangles (same vertex set, any orientation)
        var seen = new System.Collections.Generic.HashSet<(int, int, int)>();
        int nDup = 0;
        foreach (int tid in m.TriangleIndices()) {
            Index3i t = m.GetTriangle(tid);
            int[] s = { t.a, t.b, t.c }; Array.Sort(s);
            if (!seen.Add((s[0], s[1], s[2]))) nDup++;
        }
        r[p + "_duplicate_tris"] = nDup;

        // self-intersections between triangles that share no vertex (coplanar overlaps not detected)
        int nSelf = 0;
        if (bExpensive && m.TriangleCount > 0 && m.TriangleCount <= 200000) {
            // g3's tri-tri intersection throws on (near-)degenerate triangles; those are counted
            // separately above, so test the remaining triangles only (vertex ids are preserved)
            var copy = new DMesh3(m);
            foreach (int tid in copy.TriangleIndices().ToArray()) {
                copy.GetTriVertices(tid, ref vA, ref vB, ref vC);
                double maxEdge = Math.Max(vA.Distance(vB), Math.Max(vB.Distance(vC), vC.Distance(vA)));
                double area = MathUtil.Area(vA, vB, vC);
                if (area < areaEps || maxEdge * maxEdge / (2 * area) > 1e6)
                    copy.RemoveTriangle(tid, false, false);
            }
            var tree = new DMeshAABBTree3(copy, true);
            var res = tree.FindAllIntersections(tree);
            var pairs = new System.Collections.Generic.HashSet<(int, int)>();
            foreach (var x in res.Points.Select(q => (q.t0, q.t1)).Concat(res.Segments.Select(q => (q.t0, q.t1)))) {
                if (x.t0 == x.t1) continue;
                Index3i ta = copy.GetTriangle(x.t0), tb = copy.GetTriangle(x.t1);
                if (ta.a == tb.a || ta.a == tb.b || ta.a == tb.c || ta.b == tb.a || ta.b == tb.b || ta.b == tb.c
                    || ta.c == tb.a || ta.c == tb.b || ta.c == tb.c) continue;
                pairs.Add((Math.Min(x.t0, x.t1), Math.Max(x.t0, x.t1)));
            }
            nSelf = pairs.Count;
        } else nSelf = -1;
        r[p + "_self_intersecting_pairs"] = nSelf;
        // exact, including neighbours that overlap (MeshRepairMetrics)
        r[p + "_selfint_exact"] = (bExpensive && m.TriangleCount <= 300000) ? MeshRepairMetrics.SelfIntersections(m) : -1;

        // inside-out closed components (negative volume and not a cavity inside another component)
        int nInverted = MeshPostRepairCleanup.FindInvertedComponents(m).Count;
        r[p + "_inverted_components"] = nInverted;
    }
    static Vector3d vA, vB, vC;

    // true if triangle t traverses a->b
    static bool edge_dir(Index3i t, int a, int b)
    {
        for (int j = 0; j < 3; ++j)
            if (t[j] == a) return t[(j + 1) % 3] == b;
        return false;
    }

    static void Stats(DMesh3 m, System.Collections.Generic.Dictionary<string, object> r, string p)
    {
        Quality(m, r, p);
        r[p + "_tris"] = m.TriangleCount;
        r[p + "_verts"] = m.VertexCount;
        r[p + "_boundary_edges"] = MeshIterators.BoundaryEdges(m).Count();
        r[p + "_bowties"] = m.VertexIndices().Count(v => m.IsBowtieVertex(v));
        r[p + "_components"] = new MeshConnectedComponents(m).Let(c => { c.FindConnectedT(); return c.Count; });
        r[p + "_closed"] = m.IsClosed();
        r[p + "_bbox_diag"] = Math.Round(m.CachedBounds.DiagonalLength, 4);
        r[p + "_edges_below_1e-4"] = m.EdgeIndices().Count(e => {
            Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)) < 0.0001; });
    }

    // NaN / infinity (eg IoU without a reference solid) are written as strings ("NaN"), not rejected
    static void Emit(object o) => Console.WriteLine(JsonSerializer.Serialize(o, new JsonSerializerOptions {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
}

static class Ext
{
    public static R Let<T, R>(this T t, Func<T, R> f) => f(t);
}
