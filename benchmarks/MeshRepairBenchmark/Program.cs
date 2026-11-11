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
            // robust = MeshRobustRepair (hybrid plus the volume check against the input; BENCH_REJECT_TOPOLOGY=1: RejectTopologyChange),
            // wrap = MeshAlphaWrap of the input (BENCH_WRAP_ALPHA / BENCH_WRAP_OFFSET: diagonal divided by alpha / offset),
            // wrappipe = the wrap fallback's pipeline as MeshRobustRepair runs it: MeshAlphaWrap (BENCH_WRAP_ALPHA / _OFFSET,
            // default 300 / 3000 as the fallback), SimplifyAlphaWrap and AlphaWrapQualityPass, with the time per phase and
            // the quality of the raw wrap (raw_*), after a warm-up (BENCH_WRAP_WARMUP); BENCH_WRAP_SIMPLIFY=0 leaves the
            // simplification out, BENCH_WRAP_QUALITY=1 adds the quality pass (off by default, as in the library),
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
            DMesh3 hybridUnclean = null;     // the hybrid repair result that was not clean (its defects: after the timing)
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
                // BENCH_PASSTHROUGH=0: without stage 0 of the part handling (A/B)
                robust.PassThroughCleanParts = Environment.GetEnvironmentVariable("BENCH_PASSTHROUGH") != "0";
                robust.ExactResolveRounds = int.Parse(Environment.GetEnvironmentVariable("BENCH_EXACT_ROUNDS") ?? "4");
                robust.AlphaWrapFallback = Environment.GetEnvironmentVariable("BENCH_WRAP_FALLBACK") != "0";
                robust.SimplifyAlphaWrap = Environment.GetEnvironmentVariable("BENCH_WRAP_SIMPLIFY") != "0";
                // BENCH_WRAP_QUALITY=1: with the quality pass (off by default in the library since Alpha Wrap 2.0)
                robust.AlphaWrapQualityPass = Environment.GetEnvironmentVariable("BENCH_WRAP_QUALITY") == "1";
                // BENCH_WQ: variant switches of the quality pass, eg "turn=10,follow=1,crease=20" (README, wrap quality pass)
                string wq = Environment.GetEnvironmentVariable("BENCH_WQ");
                // (the input as given: the repair may change mesh in place)
                DMesh3 wqInput = string.IsNullOrEmpty(wq) ? null : new DMesh3(mesh);
                if (string.IsNullOrEmpty(wq) == false) {
                    r["wq_variant"] = wq;
                    robust.ConfigureWrapQuality = (pass) => {
                        foreach (string kv in wq.Split(',')) {
                            string[] p = kv.Split('=');
                            double v = double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                            switch (p[0]) {
                                // (the switches apply on top of the library's default settings, MeshRobustRepair.DefaultWrapQuality;
                                // "default=0" first resets them to the pass's own, as for the variants measured before the default)
                                case "default":
                                    if (v == 0) {
                                        pass.KeepNormalDeviation = false; pass.NormalDeviationSlackDegrees = 0;
                                        pass.CapAngleDegrees = 180; pass.SplitCapsAboveDegrees = 180;
                                    }
                                    break;
                                case "turn": pass.MaxNormalTurnDegrees = v; break;
                                case "follow": pass.FollowReference = v != 0; break;
                                case "crease": pass.MaxFlipDihedralDegrees = v; break;
                                case "worst": pass.WorstFirst = v != 0; break;
                                case "normals": pass.KeepNormalDeviation = v != 0; break;
                                case "nslack": pass.NormalDeviationSlackDegrees = v; break;
                                case "split": pass.SplitCapsAboveDegrees = v; break;
                                case "capflip": pass.CapAngleDegrees = v; break;
                                case "nsamples": pass.NormalSamples = (int)v; break;
                                case "pin": pass.PinCreaseVerticesDegrees = v; break;
                                case "protect":
                                    // the input's sharp edges (dihedral above 20 degrees), kept v wrap offsets away from
                                    // (the pass's Tolerance is 0.95 of half the offset)
                                    foreach (int e in wqInput.EdgeIndices()) {
                                        Index2i et = wqInput.GetEdgeT(e);
                                        if (et.b < 0) continue;
                                        if (wqInput.GetTriNormal(et.a).Dot(wqInput.GetTriNormal(et.b)) < Math.Cos(20 * MathUtil.Deg2Rad)) {
                                            Index2i ev = wqInput.GetEdgeV(e);
                                            pass.ProtectedEdges.Add(new Segment3d(wqInput.GetVertex(ev.a), wqInput.GetVertex(ev.b)));
                                        }
                                    }
                                    pass.ProtectedDistance = v * pass.Tolerance / 0.475;
                                    break;
                                case "protectflipsonly": pass.ProtectFlipsOnly = v != 0; break;
                                case "flips": pass.EnableFlips = v != 0; break;
                                case "moves": pass.EnableMoves = v != 0; break;
                                case "collapse": pass.EnableCollapses = v != 0; break;
                                case "growth": pass.MaxTriangleGrowth = v; break;
                                default: throw new ArgumentException("BENCH_WQ: unknown switch " + p[0]);
                            }
                        }
                    };
                }
                // BENCH_ROBUST_WRAP_ALPHA / BENCH_ROBUST_WRAP_OFFSET: WrapRelativeAlpha / WrapRelativeOffset (diagonal / value)
                string wa = Environment.GetEnvironmentVariable("BENCH_ROBUST_WRAP_ALPHA"), wo = Environment.GetEnvironmentVariable("BENCH_ROBUST_WRAP_OFFSET");
                if (wa != null) robust.WrapRelativeAlpha = double.Parse(wa, System.Globalization.CultureInfo.InvariantCulture);
                if (wo != null) robust.WrapRelativeOffset = double.Parse(wo, System.Globalization.CultureInfo.InvariantCulture);
                robust.SafeRounding = Environment.GetEnvironmentVariable("BENCH_SAFE_ROUNDING") != "0";
                robust.CheckLostComponents = Environment.GetEnvironmentVariable("BENCH_LOST_COMPONENTS") == "1";
                robust.RejectTopologyChange = Environment.GetEnvironmentVariable("BENCH_REJECT_TOPOLOGY") == "1";
                // BENCH_IOU_CELLS=0: the volume check samples in the region of sample_region always (A/B of the cells)
                robust.SampleCellsInVolumeCheck = Environment.GetEnvironmentVariable("BENCH_IOU_CELLS") != "0";
                string cellBudget = Environment.GetEnvironmentVariable("BENCH_IOU_CELL_BUDGET");
                if (cellBudget != null) robust.SampleCellsChildBudget = int.Parse(cellBudget);
                ok = robust.Apply();
                result = robust.Mesh;
                r["hybrid_fallback"] = robust.FallbackReason != "";
                r["hybrid_reason"] = robust.FallbackReason;
                r["robust_used_solidify"] = robust.UsedSolidify;
                r["robust_repair_iou"] = robust.RepairVolumeIoU;
                r["robust_used_exact"] = robust.UsedExactResolve;
                r["robust_topology_changed"] = robust.TopologyChanged;
                r["robust_genus_before"] = robust.GenusBefore;
                r["robust_genus_after"] = robust.GenusAfter;
                r["robust_exact_reason"] = robust.ExactResolveReason;
                r["robust_wrap_reason"] = robust.WrapReason;
                r["robust_passed_through_tris"] = robust.PassedThroughTriangles;
                r["robust_exact_rounds"] = robust.ExactResolveRoundsUsed;
                r["robust_exact_selfint"] = robust.ExactResolveSelfIntersections;
                r["robust_exact_rounding_edits"] = robust.ExactRoundingEdits.ConvertAll(e => new[] { e.a, e.b });
                r["robust_solidify_reason"] = robust.SolidifyReason;
                r["robust_used_wrap"] = robust.UsedAlphaWrap;
                r["robust_wrap_tris"] = robust.AlphaWrapTriangles;
                r["robust_wrap_quality_improved"] = robust.AlphaWrapQualityImproved;
                // (the exact resolve once more relative to the largest component, after the others failed, and the
                // largest share of a component below the degenerate area of all triangles that triggered it)
                r["robust_scale_retry"] = robust.ScaleRetry;
                r["robust_scale_erosion_share"] = robust.MaxScaleErosionShare;
                r["robust_result_reason"] = robust.ResultReason;
                r["robust_phases"] = robust.PhaseMilliseconds;
                // per volume check: cells used, samples, samples in the union, the IoU's interval at the stop, the
                // sampled and the known volume (relative to the estimated union), the cells, and the times
                r["robust_volume_checks"] = robust.VolumeCheckStats.ConvertAll(v => new System.Collections.Generic.Dictionary<string, object>() {
                    ["cells"] = v.Cells, ["samples"] = v.Samples, ["union"] = v.UnionSamples, ["iou_lo"] = v.IoULow, ["iou_hi"] = v.IoUHigh,
                    ["sampled_cells"] = v.SampledCells, ["child_tests"] = v.ChildTests, ["depth"] = v.Depth, ["unclassified"] = v.Unclassified, ["windings"] = v.WindingEvaluations,
                    ["sampled_volume"] = v.SampledVolume, ["known_union"] = v.KnownUnion, ["known_both"] = v.KnownBoth,
                    ["build_ms"] = v.BuildTicks * 1000.0 / Stopwatch.Frequency, ["sample_ms"] = v.SampleTicks * 1000.0 / Stopwatch.Frequency });
            } else if (repairMode == "cgalwrap") {
                result = CgalWrap(args[0], r);
                ok = result.TriangleCount > 0;
            } else if (repairMode == "wrappipe") {
                // the time budget is for a warm process (owner 2026-11-08): BENCH_WRAP_WARMUP = soup (default): the pipeline
                // on a procedural input first (warmup_ms; JIT and tiering), then on the mesh; same: the mesh twice, the first
                // time is pipe_ms_cold; none: a cold process (pipe_ms includes the JIT). BENCH_WRAP_REPEAT=n: n measured
                // runs (pipe_ms_runs), the row keeps the last
                string warmup = Environment.GetEnvironmentVariable("BENCH_WRAP_WARMUP") ?? "soup";
                r["warmup"] = warmup;
                if (warmup == "soup") {
                    var wsw = Stopwatch.StartNew();
                    // (alpha = diagonal / 60: about 1 s; as warm as a first run of the same mesh, measured on 3 meshes)
                    var wr = new System.Collections.Generic.Dictionary<string, object>();
                    WrapPipeline(WarmupInput(), wr, 60);
                    r["warmup_ms"] = wsw.ElapsedMilliseconds;
                    // (whether the warm-up reached the simplification and the quality pass: else their JIT lands in pipe_ms)
                    r["warmup_simplified"] = wr.ContainsKey("pipe_simplified") ? wr["pipe_simplified"] : false;
                    r["warmup_quality"] = wr.ContainsKey("pipe_quality_improved") ? wr["pipe_quality_improved"] : false;
                } else if (warmup == "same") {
                    WrapPipeline(original, r);
                    r["pipe_ms_cold"] = r["pipe_ms"];
                }
                int repeat = int.Parse(Environment.GetEnvironmentVariable("BENCH_WRAP_REPEAT") ?? "1");
                var runs = new System.Collections.Generic.List<object>();
                result = null;
                for (int k = 0; k < Math.Max(1, repeat); ++k) {
                    result = WrapPipeline(original, r);
                    runs.Add(r["pipe_ms"]);
                }
                if (repeat > 1) r["pipe_ms_runs"] = runs;
                ok = r["wrap_status"].ToString() == "Success" || r["wrap_status"].ToString() == "PointLimitReached";
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
                    if (reason != "") {
                        hybridUnclean = result;     // (the solidify does not change its input)
                        result = Solidify(result, MeshWindingNumberSolidify.Methods.DualContouring, true, r);
                    }
                }
            }
            r["repair_ms"] = (repairMode == "cgalwrap" && r.ContainsKey("wrap_ms")) ? r["wrap_ms"] : sw.ElapsedMilliseconds;
            r["apply_returned"] = ok;
            // every failed check of the unclean hybrid repair result (hybrid_reason is the first, which decides); a
            // diagnostic, so its failure does not fail the run. "self-intersections" first means every other check
            // passed: not checked again
            if (repairMode == "hybrid" || repairMode == "hybrid_topo") {
                stage = "hybrid_defects";
                string first = (string)r["hybrid_reason"];
                try {
                    r["hybrid_defects"] = hybridUnclean == null || first == "self-intersections" ? first
                        : MeshRobustRepair.NotCleanReasons(hybridUnclean, repairMode == "hybrid");
                } catch (Exception e) {
                    r["hybrid_defects"] = "error: " + e.GetType().Name + ": " + e.Message;
                }
            }

            // BENCH_SAVE_DIR: the result as <dir>/<input name>.obj (for diagnostics; never committed)
            string saveDir = Environment.GetEnvironmentVariable("BENCH_SAVE_DIR");
            if (string.IsNullOrEmpty(saveDir) == false && result != null && result.TriangleCount > 0) {
                System.IO.Directory.CreateDirectory(saveDir);
                StandardMeshWriter.WriteMesh(System.IO.Path.Combine(saveDir, System.IO.Path.GetFileNameWithoutExtension(args[0]) + ".obj"), result, WriteOptions.Defaults);
            }
            stage = "stats_after";
            swStats.Restart();
            Stats(result, r, "after");
            r["stats_after_ms"] = swStats.ElapsedMilliseconds;
            // (for before/after comparisons of optimizations: the same hash, the same result bit for bit)
            r["after_hash"] = Hash(result);
            Deviation(original, result, r);
            if (repairMode != "boolean" && repairMode != "exact")
                Fidelity(original, result, r);
            bool valid = true;
            try { valid = result.CheckValidity(false, FailMode.ReturnOnly); } catch { valid = false; }
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
        bool safe = Environment.GetEnvironmentVariable("BENCH_SAFE_ROUNDING") != "0";
        DMesh3 ra = ExactMeshBoolean.Resolve(a, safe), rb = ExactMeshBoolean.Resolve(b, safe);
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

    // the warm-up input of wrappipe: a bumpy sphere and a few random triangles through it (pinches, slivers: every phase
    // of the pipeline runs), seeded
    static DMesh3 WarmupInput()
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 24 }.Generate().MakeDMesh();
        foreach (int vid in m.VertexIndices().ToArray()) {
            Vector3d p = m.GetVertex(vid);
            m.SetVertex(vid, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5)));
        }
        var rnd = new Random(7);
        for (int k = 0; k < 20; ++k) {
            Func<Vector3d> q = () => new Vector3d(2.4 * rnd.NextDouble() - 1.2, 2.4 * rnd.NextDouble() - 1.2, 2.4 * rnd.NextDouble() - 1.2);
            m.AppendTriangle(m.AppendVertex(q()), m.AppendVertex(q()), m.AppendVertex(q()));
        }
        return m;
    }

    // the wrap fallback pipeline of MeshRobustRepair (raw wrap, simplification within half the offset, quality pass), as
    // MeshRobustRepair.Apply runs it after a failed solidify; pipe_ms is the time of the three steps, pipe_phases their
    // sub-steps (wrap.* = MeshAlphaWrap.PhaseMilliseconds; simplify.reduce / .check / .certify; quality.pass / .check / .certify)
    static DMesh3 WrapPipeline(DMesh3 input, System.Collections.Generic.Dictionary<string, object> r, double alphaOverride = 0)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double relAlpha = double.Parse(Environment.GetEnvironmentVariable("BENCH_WRAP_ALPHA") ?? "300", inv);
        double relOffset = double.Parse(Environment.GetEnvironmentVariable("BENCH_WRAP_OFFSET") ?? "3000", inv);
        if (alphaOverride > 0) {
            relOffset *= alphaOverride / relAlpha;
            relAlpha = alphaOverride;
        }
        Func<bool> never = () => false;
        var phases = new System.Collections.Generic.Dictionary<string, long>();
        var sw = Stopwatch.StartNew();
        var wrap = new MeshAlphaWrap(input) { RelativeAlpha = relAlpha, RelativeOffset = relOffset };
        wrap.Compute();
        long wrapMs = sw.ElapsedMilliseconds;
        r["wrap_ms"] = wrapMs;
        r["wrap_status"] = wrap.Status.ToString();
        if (wrap.Error != null) r["wrap_error"] = wrap.Error;
        r["wrap_rel_alpha"] = relAlpha;
        r["wrap_rel_offset"] = relOffset;
        r["wrap_alpha"] = wrap.UsedAlpha;
        r["wrap_offset"] = wrap.UsedOffset;
        r["wrap_counts"] = wrap.Counts;
        foreach (var kv in wrap.PhaseMilliseconds) phases[kv.Key.StartsWith("wrap") ? kv.Key : "wrap." + kv.Key] = kv.Value;
        DMesh3 raw = wrap.Result;
        if (raw == null || raw.TriangleCount == 0 || (wrap.Status != MeshAlphaWrap.WrapStatus.Success && wrap.Status != MeshAlphaWrap.WrapStatus.PointLimitReached)) {
            r["pipe_ms"] = sw.ElapsedMilliseconds;
            r["pipe_phases"] = phases;
            return raw ?? new DMesh3();
        }
        DMesh3 result = raw;
        if (Environment.GetEnvironmentVariable("BENCH_WRAP_SIMPLIFY") != "0") {
            var sp = new System.Collections.Generic.Dictionary<string, long>();
            long t0 = sw.ElapsedMilliseconds;
            result = MeshRobustRepair.SimplifyWithin(raw, 0.5 * wrap.UsedOffset, 0.5 * wrap.UsedOffset, true, never, out int subdiv, sp) ?? raw;
            phases["simplify"] = sw.ElapsedMilliseconds - t0;
            foreach (var kv in sp) phases["simplify." + kv.Key] = kv.Value;
            r["pipe_simplified"] = result != raw;
        }
        // (the quality pass only with BENCH_WRAP_QUALITY=1, as in the library since Alpha Wrap 2.0, #413)
        if (result != raw && Environment.GetEnvironmentVariable("BENCH_WRAP_QUALITY") == "1") {
            var qp = new System.Collections.Generic.Dictionary<string, long>();
            long t0 = sw.ElapsedMilliseconds;
            DMesh3 improved = MeshRobustRepair.ImproveWrapQuality(result, raw, 0.5 * wrap.UsedOffset, true, never, MeshRobustRepair.DefaultWrapQuality, qp);
            phases["quality"] = sw.ElapsedMilliseconds - t0;
            foreach (var kv in qp) phases["quality." + kv.Key] = kv.Value;
            r["pipe_quality_improved"] = improved != null;
            if (improved != null) result = improved;
        }
        r["pipe_ms"] = sw.ElapsedMilliseconds;
        r["pipe_phases"] = phases;
        // the raw wrap's triangle quality (angles only; raw_tris for the size)
        r["raw_tris"] = raw.TriangleCount;
        Quality(raw, r, "raw", false);
        return result;
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
            if (j.TryGetProperty("alpha", out var a)) r["wrap_alpha"] = a.GetDouble();
            if (j.TryGetProperty("offset", out var o)) r["wrap_offset"] = o.GetDouble();
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
    // bound), agreement with the input's winding number solid (IoU, missing and extra volume), and the
    // MeshFidelityMetrics fields (fm_*, FidelityFields.cs)
    static void Fidelity(DMesh3 input, DMesh3 result, System.Collections.Generic.Dictionary<string, object> r)
    {
        if (input.TriangleCount == 0 || result.TriangleCount == 0)
            return;
        // angle extremes of the result (wrap quality acceptance, owner decision 2026-11-02): triangles with a smallest
        // angle below 0.5 degrees, share with a largest angle above 170 degrees, global min / max angle
        {
            int lt05 = 0, lt10 = 0, lt20 = 0, gt150 = 0, gt170 = 0, n = 0; double gmin = 180, gmax = 0;
            foreach (int t in result.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                result.GetTriVertices(t, ref a, ref b, ref c);
                double A = Vector3d.AngleD((b - a).Normalized, (c - a).Normalized), B = Vector3d.AngleD((a - b).Normalized, (c - b).Normalized), C = 180 - A - B;
                double mn = Math.Min(A, Math.Min(B, C)), mx = Math.Max(A, Math.Max(B, C));
                if (mn < 0.5) lt05++;
                if (mn < 10) lt10++;
                if (mn < 20) lt20++;
                if (mx > 150) gt150++;
                if (mx > 170) gt170++;
                gmin = Math.Min(gmin, mn); gmax = Math.Max(gmax, mx); n++;
            }
            r["q_min_angle"] = gmin; r["q_max_angle"] = gmax; r["q_lt05"] = lt05;
            r["q_lt10_share"] = (double)lt10 / n; r["q_lt20_share"] = (double)lt20 / n;
            r["q_gt150_share"] = (double)gt150 / n; r["q_gt170_share"] = (double)gt170 / n;
        }
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
        // BENCH_FIDELITY=hd: the certified Hausdorff distances only (quick comparisons of the triangle quality)
        if (Environment.GetEnvironmentVariable("BENCH_FIDELITY") == "hd") {
            r["fidelity_ms"] = sw.ElapsedMilliseconds;
            return;
        }
        double missing, extra;
        // BENCH_FM_SEED: seed of the sampled measures (IoU, fm_*), to measure their sampling noise (noise_floor.py)
        int seed = int.Parse(Environment.GetEnvironmentVariable("BENCH_FM_SEED") ?? "1");
        if (seed != 1) r["fm_seed"] = seed;
        r["iou"] = MeshRepairMetrics.VolumeIoU(input, result, out missing, out extra, 100000, seed);
        r["iou_missing"] = missing;
        r["iou_extra"] = extra;
        r["fidelity_ms"] = sw.ElapsedMilliseconds;
        // further measures (MeshFidelityMetrics; fm_* fields, not part of the "repaired" criterion), against the
        // input with inside-out closed components reversed (as VolumeIoU sees it), and reversed as a whole if its
        // signed volume is negative; BENCH_FIDELITY=0 skips them
        if (Environment.GetEnvironmentVariable("BENCH_FIDELITY") != "0") {
            DMesh3 oriented = new DMesh3(input);
            foreach (int[] comp in MeshPostRepairCleanup.FindInvertedComponents(oriented))
                foreach (int tid in comp) oriented.ReverseTriOrientation(tid);
            // an open input that is inside-out as a whole (negative signed volume; FindInvertedComponents judges
            // closed components only): the reversed share would only report that the result is outward
            if (Vol(oriented) < 0)
                foreach (int tid in oriented.TriangleIndices().ToArray()) oriented.ReverseTriOrientation(tid);
            FidelityFields.Add(oriented, result, r, seed: seed);
            // BENCH_FM_SEEDS=2,3,4: the sampled measures (IoU, fm_*) again with these seeds, under fm_seeds (noise_floor.py)
            string more = Environment.GetEnvironmentVariable("BENCH_FM_SEEDS");
            if (string.IsNullOrEmpty(more) == false) {
                var bySeed = new System.Collections.Generic.Dictionary<string, object>();
                foreach (string sd in more.Split(',')) {
                    int s2 = int.Parse(sd);
                    var q = new System.Collections.Generic.Dictionary<string, object>();
                    double mi, ex;
                    q["iou"] = MeshRepairMetrics.VolumeIoU(input, result, out mi, out ex, 100000, s2);
                    q["iou_missing"] = mi;
                    q["iou_extra"] = ex;
                    FidelityFields.Add(oriented, result, q, seed: s2);
                    bySeed[s2.ToString()] = q;
                }
                r["fm_seeds"] = bySeed;
            }
        }
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

        // triangle angle distribution (degrees): global min / max, triangles with a min angle below 0.5 / 1 / 10
        // and with a max angle above 150 / 170 (analyze_wrap.py)
        double minAngle = 180, maxAngle = 0;
        int lt05 = 0, lt1 = 0, lt10 = 0, gt150 = 0, gt170 = 0;
        var minAngles = new System.Collections.Generic.List<double>(m.TriangleCount);
        foreach (int tid in m.TriangleIndices()) {
            m.GetTriVertices(tid, ref vA, ref vB, ref vC);
            double a0 = angle_deg(vB - vA, vC - vA), a1 = angle_deg(vA - vB, vC - vB), a2 = angle_deg(vA - vC, vB - vC);
            double lo = Math.Min(a0, Math.Min(a1, a2)), hi = Math.Max(a0, Math.Max(a1, a2));
            minAngle = Math.Min(minAngle, lo); maxAngle = Math.Max(maxAngle, hi);
            minAngles.Add(lo);
            if (lo < 0.5) lt05++;
            if (lo < 1) lt1++;
            if (lo < 10) lt10++;
            if (hi > 150) gt150++;
            if (hi > 170) gt170++;
        }
        // (a zero-length edge gives angles of 0; no triangles: no global min / max)
        if (m.TriangleCount > 0) {
            r[p + "_angle_min"] = Math.Round(minAngle, 4);
            r[p + "_angle_max"] = Math.Round(maxAngle, 4);
            // the 1 % worst min angle (the min angle of the triangle at the 1st percentile)
            minAngles.Sort();
            r[p + "_angle_p01"] = Math.Round(minAngles[(int)(0.01 * (minAngles.Count - 1))], 4);
        }
        r[p + "_angle_lt05"] = lt05;
        r[p + "_angle_lt1"] = lt1;
        r[p + "_angle_lt10"] = lt10;
        r[p + "_angle_gt150"] = gt150;
        r[p + "_angle_gt170"] = gt170;

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

    // angle between u and v in degrees (atan2: accurate near 0 and 180)
    static double angle_deg(Vector3d u, Vector3d v) => Math.Atan2(u.Cross(v).Length, u.Dot(v)) * 180 / Math.PI;

    // vertex ids and coordinate bits, triangle ids and corners (as BooleanComparison profile)
    static string Hash(DMesh3 mesh)
    {
        var sb = new System.Text.StringBuilder();
        foreach (int vid in mesh.VertexIndices()) {
            Vector3d v = mesh.GetVertex(vid);
            sb.Append(vid).Append(':').Append(BitConverter.DoubleToInt64Bits(v.x)).Append(',')
                .Append(BitConverter.DoubleToInt64Bits(v.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(v.z)).Append(';');
        }
        foreach (int tid in mesh.TriangleIndices()) {
            Index3i t = mesh.GetTriangle(tid);
            sb.Append(tid).Append(':').Append(t.a).Append(',').Append(t.b).Append(',').Append(t.c).Append(';');
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))).Substring(0, 16);
    }

    // NaN / infinity (eg IoU without a reference solid) are written as strings ("NaN"), not rejected
    static void Emit(object o) => Console.WriteLine(JsonSerializer.Serialize(o, new JsonSerializerOptions {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
}

static class Ext
{
    public static R Let<T, R>(this T t, Func<T, R> f) => f(t);
}
