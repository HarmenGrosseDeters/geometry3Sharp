// Runs MeshAutoRepair on one mesh file and prints one JSON line with the result
// (mesh statistics and quality metrics before and after the repair).
// Usage: MeshRepairBenchmark <meshfile> [std|soup|manual] [none|meters|geo] [rel|tolerance factor]
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
            var swStats = Stopwatch.StartNew();
            Stats(mesh, r, "before");
            r["stats_before_ms"] = swStats.ElapsedMilliseconds;
            if (Environment.GetEnvironmentVariable("BENCH_REPAIR") == "analysis") {
                // analysis building blocks on the input instead of a repair (AnalysisBench.cs)
                stage = "analysis";
                r["repair_mode"] = "analysis";
                sw.Restart();
                AnalysisBench.Run(mesh, r);
                r["analysis_ms"] = sw.ElapsedMilliseconds;
                r["status"] = "ok";
                Emit(r);
                return 0;
            }
            // AutoRepair modifies the mesh in place; keep the input for solidify and deviation
            DMesh3 original = new DMesh3(mesh);

            stage = "repair";
            sw.Restart();
            // BENCH_REPAIR: default = MeshAutoRepair, cleanup = MeshAutoRepair with PostRepairCleanup,
            // smoothfill = cleanup + HoleFillMode.Smoothed, interior = cleanup + RemoveMode.Interior (fast winding number),
            // solidify_dc / solidify_dc_adaptive / solidify_mc = MeshWindingNumberSolidify (dual contouring on a grid /
            // on an adaptive octree / marching cubes) on the input,
            // hybrid = cleanup; if the result is not clean (MeshRobustRepair.NotCleanReason, including exact
            // self-intersections), solidify_dc_adaptive of the repaired mesh; hybrid_topo = the same without
            // the self-intersection check,
            // robust = MeshRobustRepair (hybrid plus the volume check against the input),
            // wrap = MeshAlphaWrap of the input (BENCH_WRAP_ALPHA / BENCH_WRAP_OFFSET: diagonal divided by alpha / offset),
            // cgalwrap = the same with CGAL's alpha wrap (external program, CGAL_WRAP; for comparison only),
            // instr = InstrumentedAutoRepair, records cheap quality metrics after every pipeline step,
            // steps = MeshAutoRepair (cleanup) with OnStage: quality, fidelity and winding closedness after every step,
            // analysis = no repair: MeshWallThickness, MeshDeviation and PrimitiveFit3 on the input (see above)
            string repairMode = Environment.GetEnvironmentVariable("BENCH_REPAIR") ?? "default";
            r["repair_mode"] = repairMode;
            // optional 4th arg: absolute tolerances, the 1.0.324 defaults (RepairTolerance 1e-6, MinEdgeLengthTol 1e-4)
            // times this factor (e.g. 0.001 when the data is in meters); "rel" or missing: the library default,
            // tolerances relative to the size of the mesh
            double tolFactor = double.NaN;
            if (args.Length > 3 && args[3] != "rel") {
                tolFactor = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
                r["tol_factor"] = tolFactor;
            }
            bool absTol = double.IsNaN(tolFactor) == false;
            const double absRepairTol = MathUtil.ZeroTolerancef, absMinEdge = 1e-4;
            DMesh3 result;
            bool ok;
            if (repairMode == "exact") {
                result = RunExactBooleans(original, r);
                ok = true;
            } else if (repairMode == "boolean") {
                result = RunBooleans(original, r);
                ok = true;
            } else if (repairMode == "instr") {
                var repair = new InstrumentedAutoRepair(mesh);
                if (absTol) {
                    repair.ToleranceMode = InstrumentedAutoRepair.ToleranceModes.Absolute;
                    repair.RepairTolerance = absRepairTol * tolFactor;
                    repair.MinEdgeLengthTol = absMinEdge * tolFactor;
                }
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
            } else if (repairMode == "steps") {
                // the library pipeline (MeshAutoRepair with PostRepairCleanup) with its OnStage callback: cheap
                // quality, distance to the input (certified Hausdorff bounds), volume IoU and closedness in the
                // winding number sense after every step
                var repair = new MeshAutoRepair(mesh) { PostRepairCleanup = true };
                if (absTol) {
                    repair.ToleranceMode = MeshAutoRepair.ToleranceModes.Absolute;
                    repair.RepairTolerance = absRepairTol * tolFactor;
                    repair.MinEdgeLengthTol = absMinEdge * tolFactor;
                }
                var stages = new System.Collections.Generic.List<object>();
                double diag = original.CachedBounds.DiagonalLength;
                repair.OnStage = (name, sm) => {
                    var q = new System.Collections.Generic.Dictionary<string, object>();
                    q["stage"] = name;
                    DMesh3 c = new DMesh3(sm, true);
                    Quality(c, q, "q", false);
                    q["tris"] = c.TriangleCount;
                    q["boundary_edges"] = MeshIterators.BoundaryEdges(c).Count();
                    q["bowties"] = c.VertexIndices().Count(v => c.IsBowtieVertex(v));
                    foreach (int[] comp in MeshPostRepairCleanup.FindInvertedComponents(c))
                        foreach (int tid in comp) c.ReverseTriOrientation(tid);
                    q["wclosed"] = ExactMeshBoolean.IsWindingClosed(c);
                    if (c.TriangleCount > 0) {
                        foreach (bool fwd in new[] { true, false }) {
                            var h = new MeshHausdorffDistance(fwd ? c : original, fwd ? original : c) {
                                Symmetric = false, RelativeTolerance = 1e-3, MaxSubdivisions = 500000 };
                            if (h.Compute())
                                q[fwd ? "hd_fwd_hi_rel" : "hd_back_hi_rel"] = h.UpperBound / diag;
                        }
                        double missing, extra;
                        q["iou"] = MeshRepairMetrics.VolumeIoU(original, c, out missing, out extra, 30000);
                    }
                    stages.Add(q);
                };
                ok = repair.Apply();
                result = repair.Mesh;
                r["stages"] = stages;
            } else if (repairMode == "robust") {
                // MeshRobustRepair: hybrid plus the volume check against the input's winding number solid
                var robust = new MeshRobustRepair(mesh) { ExactResolve = Environment.GetEnvironmentVariable("BENCH_EXACT") != "0", ConfigureRepair = (rep) => {
                    if (absTol) { rep.ToleranceMode = MeshAutoRepair.ToleranceModes.Absolute; rep.RepairTolerance = absRepairTol * tolFactor; rep.MinEdgeLengthTol = absMinEdge * tolFactor; } } };
                int res = int.Parse(Environment.GetEnvironmentVariable("BENCH_SOLID_RES") ?? "128");
                robust.SolidifyResolution = res;
                robust.ExactResolveRounds = int.Parse(Environment.GetEnvironmentVariable("BENCH_EXACT_ROUNDS") ?? "4");
                robust.AlphaWrapFallback = Environment.GetEnvironmentVariable("BENCH_WRAP_FALLBACK") != "0";
                robust.SimplifyAlphaWrap = Environment.GetEnvironmentVariable("BENCH_WRAP_SIMPLIFY") != "0";
                robust.SafeRounding = Environment.GetEnvironmentVariable("BENCH_SAFE_ROUNDING") != "0";
                robust.CheckLostComponents = Environment.GetEnvironmentVariable("BENCH_LOST_COMPONENTS") == "1";
                ok = robust.Apply();
                result = robust.Mesh;
                r["hybrid_fallback"] = robust.FallbackReason != "";
                r["hybrid_reason"] = robust.FallbackReason;
                r["robust_used_solidify"] = robust.UsedSolidify;
                r["robust_repair_iou"] = robust.RepairVolumeIoU;
                r["robust_used_exact"] = robust.UsedExactResolve;
                r["robust_exact_reason"] = robust.ExactResolveReason;
                r["robust_wrap_reason"] = robust.WrapReason;
                r["robust_exact_rounds"] = robust.ExactResolveRoundsUsed;
                r["robust_exact_selfint"] = robust.ExactResolveSelfIntersections;
                r["robust_exact_rounding_edits"] = robust.ExactRoundingEdits.ConvertAll(e => new[] { e.a, e.b });
                r["robust_solidify_reason"] = robust.SolidifyReason;
                r["robust_used_wrap"] = robust.UsedAlphaWrap;
                r["robust_wrap_tris"] = robust.AlphaWrapTriangles;
                r["robust_phases"] = robust.PhaseMilliseconds;
            } else if (repairMode == "cgalwrap") {
                result = CgalWrap(args[0], r);
                ok = result.TriangleCount > 0;
            } else if (repairMode == "wrap") {
                result = Wrap(original, r);
                ok = r["wrap_status"].ToString() == "Success";
            } else if (repairMode.StartsWith("solidify_")) {
                result = Solidify(original, repairMode == "solidify_mc" ? MeshWindingNumberSolidify.Methods.MarchingCubes
                    : MeshWindingNumberSolidify.Methods.DualContouring, repairMode == "solidify_dc_adaptive", r);
                ok = true;
            } else {
                var repair = new MeshAutoRepair(mesh);
                if (absTol) {
                    repair.ToleranceMode = MeshAutoRepair.ToleranceModes.Absolute;
                    repair.RepairTolerance = absRepairTol * tolFactor;
                    repair.MinEdgeLengthTol = absMinEdge * tolFactor;
                }
                repair.PostRepairCleanup = repairMode != "default";
                if (repairMode == "interior")
                    repair.RemoveMode = MeshAutoRepair.RemoveModes.Interior;
                if (repairMode == "smoothfill")
                    repair.HoleFillMode = MeshAutoRepair.HoleFillModes.Smoothed;
                ok = repair.Apply();
                result = repair.Mesh;
                if (repairMode == "hybrid" || repairMode == "hybrid_topo") {
                    // as MeshRobustRepair: solidify the repaired mesh unless it is clean; hybrid also requires
                    // no self-intersections, hybrid_topo (the earlier variant) only topological cleanness
                    string reason = MeshRobustRepair.NotCleanReason(result, repairMode == "hybrid");
                    r["hybrid_fallback"] = reason != "";
                    r["hybrid_reason"] = reason;
                    if (reason != "")
                        result = Solidify(result, MeshWindingNumberSolidify.Methods.DualContouring, true, r);
                }
            }
            r["repair_ms"] = (repairMode == "cgalwrap" && r.ContainsKey("wrap_ms")) ? r["wrap_ms"] : sw.ElapsedMilliseconds;
            r["apply_returned"] = ok;

            stage = "stats_after";
            swStats.Restart();
            Stats(result, r, "after");
            r["stats_after_ms"] = swStats.ElapsedMilliseconds;
            Deviation(original, result, r);
            if (repairMode != "boolean" && repairMode != "exact")
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
            r["trace"] = string.Join(" | ", (e.StackTrace ?? "").Split('\n').Take(3).Select(l => l.Trim()));
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
    // ExactMeshBoolean on the same input pair as RunBooleans (A and a rotated, shifted copy B). Exact results
    // satisfy V(A u B) + V(A n B) = V(A) + V(B) up to the rounding of the output vertices.
    static DMesh3 RunExactBooleans(DMesh3 a, System.Collections.Generic.Dictionary<string, object> r)
    {
        DMesh3 b = new DMesh3(a);
        AxisAlignedBox3d bounds = a.CachedBounds;
        Vector3d c = bounds.Center;
        MeshTransforms.Rotate(b, c, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
        MeshTransforms.Translate(b, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
        // the solids of A and B (self-intersections resolved), for the volume identities
        var swr = Stopwatch.StartNew();
        DMesh3 ra = ExactMeshBoolean.Resolve(a), rb = ExactMeshBoolean.Resolve(b);
        r["exact_resolve_ms"] = swr.ElapsedMilliseconds;
        double vA = ra != null ? Vol(ra) : Vol(a), vB = rb != null ? Vol(rb) : Vol(b);
        r["exact_vA"] = vA; r["exact_vB"] = vB;
        r["exact_vA_signed"] = Vol(a);
        r["exact_wclosed_input"] = ExactMeshBoolean.IsWindingClosed(a);
        var results = new System.Collections.Generic.Dictionary<string, DMesh3>();
        long total = 0;
        foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection, ExactMeshBoolean.Operations.Difference }) {
            string key = op.ToString().ToLower();
            var sw = Stopwatch.StartNew();
            // BENCH_SAFE_ROUNDING=0: output vertices only rounded (no MeshRoundingRepair)
            var boolean = new ExactMeshBoolean(a, b, op) { SafeRounding = Environment.GetEnvironmentVariable("BENCH_SAFE_ROUNDING") != "0" };
            bool ok = boolean.Compute();
            total += sw.ElapsedMilliseconds;
            r["exact_" + key + "_ms"] = sw.ElapsedMilliseconds;
            if (!ok) {
                r["exact_error"] = boolean.Error;
                return a;
            }
            DMesh3 m = boolean.Result;
            results[key] = m;
            r["exact_phases_" + key] = boolean.PhaseMilliseconds;
            r["exact_pairs"] = boolean.IntersectingPairs;
            r["exact_" + key + "_tris"] = m.TriangleCount;
            r["exact_" + key + "_volume"] = Vol(m);
            r["exact_" + key + "_closed"] = m.TriangleCount == 0 || m.IsClosed();
            r["exact_" + key + "_wclosed"] = ExactMeshBoolean.IsWindingClosed(m);
            r["exact_windings_exact"] = boolean.ExactWindingA && boolean.ExactWindingB;
            r["exact_selfpairs"] = boolean.SelfIntersectingPairs;
            r["exact_" + key + "_nonmanifold"] = boolean.NonManifoldTriangles;
            // orientation of the result (interior edges whose two triangles run the same way) and its open edges
            r["exact_" + key + "_inconsistent"] = m.EdgeIndices().Count(e => {
                Index2i et = m.GetEdgeT(e), ev = m.GetEdgeV(e);
                return et.b != DMesh3.InvalidID && edge_dir(m.GetTriangle(et.a), ev.a, ev.b) == edge_dir(m.GetTriangle(et.b), ev.a, ev.b);
            });
            r["exact_" + key + "_boundary"] = m.EdgeIndices().Count(e => m.IsBoundaryEdge(e));
            r["exact_" + key + "_selfint"] = m.TriangleCount <= 300000 ? MeshRepairMetrics.SelfIntersections(m) : -1;
            double diag = m.TriangleCount > 0 ? m.CachedBounds.DiagonalLength : 0;
            r["exact_" + key + "_degenerate"] = m.TriangleIndices().Count(t => m.GetTriArea(t) < 1e-12 * diag * diag);
            if (boolean.Rounding != null) {
                r["exact_" + key + "_rounding_clean"] = boolean.RoundingClean;
                r["exact_" + key + "_rounding_initial"] = new[] { boolean.Rounding.InitialIntersections, boolean.Rounding.InitialDegenerate };
                r["exact_" + key + "_rounding_remaining"] = new[] { boolean.Rounding.RemainingIntersections, boolean.Rounding.RemainingDegenerate };
                r["exact_" + key + "_rounding_removed"] = boolean.Rounding.RemovedTriangles;
            }
        }
        r["exact_ms"] = total;
        double vU = Vol(results["union"]), vI = Vol(results["intersection"]), vD = Vol(results["difference"]);
        r["exact_ie_error"] = (vA + vB) > 0 ? Math.Abs(vU + vI - vA - vB) / (vA + vB) : 0;
        r["exact_diff_error"] = vA > 0 ? Math.Abs(vD - (vA - vI)) / vA : 0;
        return results["union"];
    }

    static double Vol(DMesh3 m) { return m.TriangleCount == 0 ? 0 : MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }

    // alpha wrap of the input (MeshAlphaWrap): alpha = diagonal / BENCH_WRAP_ALPHA, offset = diagonal / BENCH_WRAP_OFFSET
    static DMesh3 Wrap(DMesh3 input, System.Collections.Generic.Dictionary<string, object> r)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double relAlpha = double.Parse(Environment.GetEnvironmentVariable("BENCH_WRAP_ALPHA") ?? "100", inv);
        double relOffset = double.Parse(Environment.GetEnvironmentVariable("BENCH_WRAP_OFFSET") ?? "1000", inv);
        var sw = Stopwatch.StartNew();
        var wrap = new MeshAlphaWrap(input) { RelativeAlpha = relAlpha, RelativeOffset = relOffset };
        wrap.Compute();
        r["wrap_ms"] = sw.ElapsedMilliseconds;
        r["wrap_status"] = wrap.Status.ToString();
        if (wrap.Error != null) r["wrap_error"] = wrap.Error;
        r["wrap_rel_alpha"] = relAlpha;
        r["wrap_rel_offset"] = relOffset;
        r["wrap_alpha"] = wrap.UsedAlpha;
        r["wrap_offset"] = wrap.UsedOffset;
        r["wrap_counts"] = wrap.Counts;
        r["wrap_phases"] = wrap.PhaseMilliseconds;
        return wrap.Result ?? new DMesh3();
    }

    // CGAL's alpha wrap (external/cgal_wrap.cpp, path in CGAL_WRAP; GPL, for comparison only) on the input file,
    // with the same relative alpha and offset as Wrap; repair_ms is CGAL's own time (without process start and I/O)
    static DMesh3 CgalWrap(string file, System.Collections.Generic.Dictionary<string, object> r)
    {
        string exe = Environment.GetEnvironmentVariable("CGAL_WRAP") ?? "cgal_wrap";
        string relAlpha = Environment.GetEnvironmentVariable("BENCH_WRAP_ALPHA") ?? "100";
        string relOffset = Environment.GetEnvironmentVariable("BENCH_WRAP_OFFSET") ?? "1000";
        string off = System.IO.Path.GetTempFileName() + ".off";
        var psi = new ProcessStartInfo(exe, $"\"{file}\" {relAlpha} {relOffset} \"{off}\"") { RedirectStandardOutput = true, UseShellExecute = false };
        using (var p = Process.Start(psi)) {
            string line = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            var j = JsonDocument.Parse(line.Split('\n').Last()).RootElement;
            r["wrap_status"] = j.GetProperty("ok").GetBoolean() ? "Success" : "Failed";
            if (j.TryGetProperty("ms", out var ms)) r["wrap_ms"] = ms.GetInt64();
        }
        r["wrap_rel_alpha"] = relAlpha;
        r["wrap_rel_offset"] = relOffset;
        DMesh3Builder builder = new DMesh3Builder();
        IOReadResult rr = StandardMeshReader.ReadFile(off, new ReadOptions(), builder);
        System.IO.File.Delete(off);
        if (rr.code != IOCode.Ok || builder.Meshes.Count == 0) {
            r["wrap_error"] = "could not read the CGAL result: " + rr.message;
            return new DMesh3();
        }
        return builder.Meshes[0];
    }

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
