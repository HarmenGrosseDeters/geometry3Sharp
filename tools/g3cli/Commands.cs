// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using g3;
using gs;

namespace g3cli
{
    /// <summary>
    /// The operations on mesh files, shared by the CLI (subcommands) and the MCP server (tools). Each one
    /// reads its inputs, runs the operation and returns a JSON object; Run() turns every failure into a
    /// result with an error code, it never throws.
    /// </summary>
    public static partial class Commands
    {
        public static readonly List<Command> All = new List<Command>() {
            Measure(), Repair(), Boolean(), Resolve(), Remesh(), Simplify(), Uv(), Convert(), Mesh2d(), Query2d(), Slope()
        };

        public static Command Find(string name)
        {
            return All.FirstOrDefault(c => c.Name == name);
        }

        public static string Version {
            get {
                var v = typeof(Commands).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
                int plus = v.IndexOf('+');
                return plus >= 0 ? v.Substring(0, plus) : v;
            }
        }


        /// <summary>
        /// Runs a command on already validated arguments: { "command", "status", "code", ... }, with
        /// "message" if status is "error". Never throws.
        /// </summary>
        public static JsonObject Run(Command cmd, Args args)
        {
            var sw = Stopwatch.StartNew();
            JsonObject result;
            try {
                result = cmd.Run(args);
                result = Ok(cmd.Name, result);
            } catch (CommandException e) {
                result = Error(cmd.Name, e.Code, e.Message);
                if (e.Data != null)
                    foreach (var kv in e.Data.ToList()) {
                        e.Data.Remove(kv.Key);
                        if (result.ContainsKey(kv.Key) == false) result[kv.Key] = kv.Value;
                    }
            } catch (Exception e) {
                result = Error(cmd.Name, ResultCodes.InternalError, e.GetType().Name + ": " + e.Message);
            }
            result["elapsed_ms"] = sw.ElapsedMilliseconds;
            return result;
        }

        public static JsonObject Ok(string command, JsonObject data)
        {
            var o = new JsonObject() { ["command"] = command, ["status"] = "ok", ["code"] = ResultCodes.Ok };
            foreach (var kv in data.ToList()) {
                data.Remove(kv.Key);
                o[kv.Key] = kv.Value;
            }
            return o;
        }

        public static JsonObject Error(string command, string code, string message)
        {
            return new JsonObject() { ["command"] = command, ["status"] = "error", ["code"] = code, ["message"] = message };
        }


        // ---- parameters used by several commands ----

        static Param Input(string name = "input", string what = "input mesh file") =>
            new Param(name, ParamType.String, what + " (" + string.Join(", ", MeshFiles.ReadFormats) + ")") { Required = true, Positional = true };
        static Param Output() =>
            new Param("output", ParamType.String, "output mesh file, the format follows the extension (" + string.Join(", ", MeshFiles.WriteFormats) + ")") { Required = true, Positional = true };
        static Param Binary() =>
            new Param("binary", ParamType.Boolean, "write STL and PLY as binary (false: ASCII)") { Default = true };
        static Param SelfIntersections() =>
            new Param("self_intersections", ParamType.Boolean, "count self-intersecting triangle pairs (exact) in the reported metrics") { Default = true };
        static Param Deviation() =>
            new Param("deviation", ParamType.Boolean, "report the Hausdorff distance between input and result") { Default = false };


        static JsonObject Hausdorff(DMesh3 a, DMesh3 b, double tolerance = 0)
        {
            var h = new MeshHausdorffDistance(a, b) { Tolerance = tolerance };
            bool ok = h.Compute();
            if (ok == false)
                return null;
            return new JsonObject() {
                ["distance"] = MeshReport.Num(h.UpperBound),
                ["lower_bound"] = MeshReport.Num(h.LowerBound),
                ["upper_bound"] = MeshReport.Num(h.UpperBound),
                ["converged"] = h.Converged,
                ["worst_point"] = MeshReport.Vec(h.WorstPoint),
                ["worst_point_on"] = h.WorstPointOnB ? "b" : "a",
            };
        }

        static JsonArray Warnings(List<string> warnings)
        {
            return new JsonArray(warnings.Distinct().Take(20).Select(w => (JsonNode)w).ToArray());
        }


        // ---- measure ----

        static Command Measure()
        {
            var c = new Command() {
                Name = "measure",
                Description = "Measure a mesh: counts, components, closedness, volume, area, bounds, genus, " +
                    "degenerate triangles, bowties, inconsistently oriented edges, inside-out components, self-intersections (exact) and whether it is a clean solid. " +
                    "With 'compare', also the certified Hausdorff distance to a second mesh.",
            };
            c.Params.Add(Input());
            c.Params.Add(new Param("compare", ParamType.String, "second mesh file: report the symmetric Hausdorff distance to it"));
            c.Params.Add(new Param("tolerance", ParamType.Number, "absolute tolerance of the Hausdorff distance (default 1e-4 of the bounding box diagonal)"));
            c.Params.Add(SelfIntersections());
            c.Run = args => {
                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                DMesh3 other = args.Has("compare") ? MeshFiles.Read(args.String("compare"), warnings) : null;
                var o = new JsonObject() { ["input"] = args.String("input") };
                o["mesh"] = MeshReport.Metrics(mesh, args.Bool("self_intersections"));
                if (other != null) {
                    double tol = args.Has("tolerance") ? args.Number("tolerance") : 0;
                    if (tol < 0)
                        throw new CommandException(ResultCodes.InvalidArguments, "'tolerance' must be >= 0");
                    o["compare"] = args.String("compare");
                    o["hausdorff"] = Hausdorff(mesh, other, tol)
                        ?? throw new CommandException(ResultCodes.OperationFailed, "Hausdorff distance failed (non-finite coordinates)");
                }
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }


        // ---- repair ----

        static Command Repair()
        {
            var c = new Command() {
                Name = "repair",
                Description = "Repair a mesh into a closed solid. 'robust' (default, MeshRobustRepair): MeshAutoRepair with post-repair cleanup, " +
                    "and if the result is still not clean (closed, manifold, no degenerate or inside-out parts, no self-intersections) or lost volume, " +
                    "its self-intersections resolved exactly (keeps the geometry), and if that is not clean either a winding-number solidify " +
                    "fallback (remeshes). 'auto' (MeshAutoRepair): keeps the input triangles, may leave problems.",
            };
            c.Params.Add(Input());
            c.Params.Add(Output());
            c.Params.Add(new Param("method", ParamType.String, "repair method") { Default = "robust", Enum = new[] { "robust", "auto" } });
            c.Params.Add(new Param("tolerance", ParamType.Number, "absolute weld / crack-closing tolerance of MeshAutoRepair (default 1e-8 of the bounding box diagonal of the triangles; exactly 1e-6 counts as the default)"));
            c.Params.Add(new Param("solidify_resolution", ParamType.Integer, "robust: grid cells along the bounding box diagonal of the solidify fallback, 8..2048") { Default = 128 });
            c.Params.Add(new Param("min_volume_iou", ParamType.Number, "robust: minimum volume IoU of the repair with the input before falling back (0 = no check)") { Default = 0.99 });
            c.Params.Add(new Param("exact_resolve", ParamType.Boolean, "robust: before the solidify, resolve the self-intersections exactly (ExactMeshBoolean.Resolve)") { Default = true });
            c.Params.Add(new Param("exact_resolve_max_pairs", ParamType.Integer, "robust: skip the exact resolve for meshes with more self-intersecting triangle pairs than this (0 = no limit)") { Default = 200000 });
            c.Params.Add(new Param("safe_rounding", ParamType.Boolean, "robust: the exact resolve repairs the self-intersections and slivers that rounding leaves (MeshRobustRepair.SafeRounding); " +
                "false: the cleanup collapses slivers and the resolve is repeated") { Default = true });
            c.Params.Add(new Param("alpha_wrap", ParamType.Boolean, "robust: last resort, alpha wrap the mesh if the solidify fails or is not clean (MeshRobustRepair.AlphaWrapFallback)") { Default = true });
            c.Params.Add(new Param("simplify_alpha_wrap", ParamType.Boolean, "robust: simplify the alpha wrap, kept only if clean and certified within half the wrap offset of the unsimplified wrap") { Default = true });
            AddRoundingTolerances(c, repair: true);
            c.Params.Add(new Param("check_lost_components", ParamType.Boolean, "robust: a result that lost a connected component of the input (eg a small part of an assembly) falls back like one that lost volume; " +
                "needs min_volume_iou > 0") { Default = false });
            c.Params.Add(new Param("reject_topology_change", ParamType.Boolean, "robust: reject an exact resolve whose genus differs from the mesh it resolved (the fallbacks run instead)") { Default = false });
            c.Params.Add(SelfIntersections());
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                bool selfInt = args.Bool("self_intersections");
                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = args.String("output"), ["method"] = args.String("method") };
                o["input_mesh"] = MeshReport.Metrics(mesh, selfInt);

                Action<MeshAutoRepair> configure = r => {
                    if (args.Has("tolerance")) {
                        if (args.Number("tolerance") <= 0)
                            throw new CommandException(ResultCodes.InvalidArguments, "'tolerance' must be > 0");
                        r.RepairTolerance = args.Number("tolerance");
                    }
                };
                DMesh3 result;
                if (args.String("method") == "auto") {
                    var repair = new MeshAutoRepair(new DMesh3(mesh));
                    configure(repair);
                    if (repair.Apply() == false)
                        throw new CommandException(ResultCodes.OperationFailed, "MeshAutoRepair failed");
                    result = repair.Mesh;
                } else {
                    if (args.Integer("solidify_resolution") < 8 || args.Integer("solidify_resolution") > 2048)
                        throw new CommandException(ResultCodes.InvalidArguments, "'solidify_resolution' must be in 8..2048");
                    var rounding = RoundingTolerances(args);
                    if (args.Integer("exact_resolve_max_pairs") < 0)
                        throw new CommandException(ResultCodes.InvalidArguments, "'exact_resolve_max_pairs' must be >= 0");
                    if (args.Bool("check_lost_components") && args.Number("min_volume_iou") <= 0)
                        throw new CommandException(ResultCodes.InvalidArguments, "'check_lost_components' needs min_volume_iou > 0");
                    var repair = new MeshRobustRepair(new DMesh3(mesh)) {
                        SolidifyResolution = args.Integer("solidify_resolution"),
                        MinVolumeIoU = args.Number("min_volume_iou"),
                        CheckSelfIntersections = selfInt,
                        ExactResolve = args.Bool("exact_resolve"),
                        ExactResolveMaxPairs = args.Integer("exact_resolve_max_pairs"),
                        SafeRounding = args.Bool("safe_rounding"),
                        AlphaWrapFallback = args.Bool("alpha_wrap"),
                        SimplifyAlphaWrap = args.Bool("simplify_alpha_wrap"),
                        CheckLostComponents = args.Bool("check_lost_components"),
                        RejectTopologyChange = args.Bool("reject_topology_change"),
                        RoundingMaxDisplacement = rounding.MaxDisplacement, RoundingUlpFactor = rounding.UlpFactor,
                        RoundingAbsoluteTolerance = rounding.AbsoluteTolerance, StrictRounding = rounding.Strict,
                        RoundingMaxPairTests = rounding.MaxPairTests,
                        ConfigureRepair = configure,
                    };
                    if (repair.Apply() == false)
                        throw new CommandException(ResultCodes.OperationFailed,
                            "MeshRobustRepair failed" + (repair.FallbackReason != "" ? " (repair result: " + repair.FallbackReason + ", solidify gave no surface)" : ""));
                    result = repair.Mesh;
                    o["used_exact_resolve"] = repair.UsedExactResolve;
                    // genus of the mesh the exact stage resolved and of its result (null without the exact stage or where
                    // the genus is not defined); a warning where a component gained or lost handles
                    o["genus_before"] = MeshReport.Num(repair.GenusBefore);
                    o["genus_after"] = MeshReport.Num(repair.GenusAfter);
                    if (repair.TopologyChanged)
                        warnings.Add("the exact resolve changed the genus from " + repair.GenusBefore.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            " to " + repair.GenusAfter.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (self-overlapping parts form handles or tunnels)");
                    o["exact_resolve_reason"] = repair.ExactResolveReason == "" ? null : repair.ExactResolveReason;
                    o["used_solidify"] = repair.UsedSolidify;
                    o["fallback_reason"] = repair.FallbackReason == "" ? null : repair.FallbackReason;
                    o["volume_iou"] = MeshReport.Num(repair.RepairVolumeIoU);
                    o["used_input"] = repair.UsedInput;
                    o["solidify_reason"] = repair.SolidifyReason == "" ? null : repair.SolidifyReason;
                    o["used_alpha_wrap"] = repair.UsedAlphaWrap;
                    o["alpha_wrap_triangles"] = repair.AlphaWrapTriangles;
                    o["alpha_wrap_simplified"] = repair.AlphaWrapSimplified;
                    o["wrap_reason"] = repair.WrapReason == "" ? null : repair.WrapReason;
                    o["result_reason"] = repair.ResultReason == "" ? null : repair.ResultReason;
                    // the SafeRounding repairs of the exact stage's last round (null if not run)
                    o["strict_rounding"] = repair.StrictRounding;
                    o["exact_resolve_rounding"] = RoundingInfo(repair.ExactResolveRounding);
                    o["separated_rounding"] = RoundingInfo(repair.SeparatedRounding);
                }
                if (result.TriangleCount == 0)
                    throw new CommandException(ResultCodes.OperationFailed, "repair removed all triangles");
                MeshFiles.Write(args.String("output"), result, args.Bool("binary"));
                o["mesh"] = MeshReport.Metrics(result, selfInt);
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }


        // ---- boolean, resolve ----

        static JsonObject BooleanInfo(ExactMeshBoolean op)
        {
            return new JsonObject() {
                ["intersecting_pairs"] = op.IntersectingPairs,
                ["self_intersecting_pairs"] = op.SelfIntersectingPairs,
                ["patches"] = op.Patches,
                ["non_manifold_edges"] = op.NonManifoldEdges,
                ["non_manifold_triangles"] = op.NonManifoldTriangles,
                // (SafeRounding is on by default: rounding_clean = verified free of self-intersections and slivers)
                ["safe_rounding"] = op.SafeRounding,
                ["rounding_clean"] = op.RoundingClean,
                ["simplify_flat_regions"] = op.SimplifyFlatRegions,
                ["strict_rounding"] = op.StrictRounding,
                ["rounding_max_displacement"] = op.RoundingMaxDisplacement,
                // (the effective values: strict rounding uses neither term)
                ["rounding_ulp_factor"] = op.StrictRounding ? 0 : op.RoundingUlpFactor,
                ["rounding_absolute_tolerance"] = op.StrictRounding ? 0 : op.RoundingAbsoluteTolerance,
                ["rounding_max_pair_tests"] = op.RoundingMaxPairTests,
                ["removed_vertices"] = op.RemovedVertices,
                ["rounding"] = RoundingInfo(op.Rounding),
            };
        }

        /// <summary> the counts of a SafeRounding repair (MeshRoundingRepair), null if it did not run </summary>
        static JsonObject RoundingInfo(MeshRoundingRepair r)
        {
            if (r == null)
                return null;
            return new JsonObject() {
                ["clean"] = r.IsClean,
                ["initial_intersections"] = r.InitialIntersections,
                ["initial_degenerate"] = r.InitialDegenerate,
                ["remaining_intersections"] = r.RemainingIntersections,
                ["remaining_degenerate"] = r.RemainingDegenerate,
                ["collapses"] = r.Collapses,
                ["flips"] = r.Flips,
                ["moves"] = r.Moves,
                ["zips"] = r.Zips,
                ["strip_zips"] = r.StripZips,
                ["removed_components"] = r.RemovedComponents,
                ["scale_disparity"] = r.ScaleDisparity,
                ["scale_not_finite"] = r.ScaleNotFinite,
                ["removed_triangles"] = r.RemovedTriangles,
                ["max_drift"] = MeshReport.Num(r.MaxDrift),
                ["max_length"] = MeshReport.Num(r.MaxLength),
                ["max_displacement"] = r.MaxDisplacement,
                ["ulp_factor"] = r.UlpFactor,
                ["absolute_tolerance"] = r.AbsoluteTolerance,
                ["max_pair_tests"] = r.MaxPairTests,
                ["evaluation_limit_reached"] = r.EvaluationLimitReached,
            };
        }

        static Param SafeRounding() =>
            new Param("safe_rounding", ParamType.Boolean, "repair the self-intersections and slivers that rounding the new vertices to double leaves " +
                "(ExactMeshBoolean.SafeRounding, MeshRoundingRepair: local edits checked exactly, each surface point moves at most 1e-5 of the " +
                "bounding box diagonal, or the larger bound of rounding_ulp_factor / rounding_absolute_tolerance); false: plain rounding, faster") { Default = true };
        static void AddRoundingTolerances(Command c, bool repair = false)
        {
            string prefix = repair ? "robust, exact stage: " : "";
            c.Params.Add(new Param("rounding_max_displacement", ParamType.Number, prefix + "safe_rounding's relative bound: the longest edge it contracts and " +
                "the farthest it moves the surface, as a fraction of the bounding box diagonal, in [1e-16, 1e-2] (larger bounds let the repair remove " +
                "whole components) (RoundingMaxDisplacement; MeshRoundingRepair.MaxDisplacement)") { Default = 1e-5 });
            c.Params.Add(new Param("rounding_ulp_factor", ParamType.Number, prefix + "safe_rounding far from the origin (eg UTM coordinates): beyond the relative " +
                "bound, edits may move the surface up to this many units in the last place of the largest coordinate " +
                "(RoundingUlpFactor; no upper limit: a large factor allows large moves)") { Default = 256.0 });
            c.Params.Add(new Param("rounding_absolute_tolerance", ParamType.Number, prefix + "safe_rounding: edits may also move the surface up to this absolute " +
                "distance (RoundingAbsoluteTolerance, 0 = none; no upper limit)") { Default = 0.0 });
            c.Params.Add(new Param("rounding_max_pair_tests", ParamType.Integer, prefix + "safe_rounding's effort budget: once the exact triangle pair " +
                "tests of its evaluations exceed this, the repair stops editing (rounding.evaluation_limit_reached) and the defects left are counted; " +
                "0 stops once one pair test was made (there is no 'no limit' value), at most 2147483647 (RoundingMaxPairTests; MeshRoundingRepair.MaxPairTests)") { Default = 5000000 });
            c.Params.Add(new Param("strict_rounding", ParamType.Boolean, repair
                ? "robust, exact stage: safe_rounding within the relative bound only (no ulp or absolute term, so it cannot be combined with them): an " +
                  "exact resolve whose rounding is not clean is rejected (exact_resolve_reason 'rounding not clean (StrictRounding)') and the other stages " +
                  "run; the repair after the separation of touching vertices uses the relative bound only and does not fail (MeshRobustRepair.StrictRounding)"
                : "safe_rounding within the relative bound only (no ulp or absolute term, so it cannot be combined with them); a result that is not clean " +
                  "fails with operation_failed (status RoundingNotClean) instead of being written with defects (ExactMeshBoolean.StrictRounding)") { Default = false });
        }

        struct RoundingSettings { public double MaxDisplacement, UlpFactor, AbsoluteTolerance; public bool Strict; public long MaxPairTests; }

        static RoundingSettings RoundingTolerances(Args args)
        {
            var r = new RoundingSettings {
                MaxDisplacement = args.Number("rounding_max_displacement"), UlpFactor = args.Number("rounding_ulp_factor"),
                AbsoluteTolerance = args.Number("rounding_absolute_tolerance"), Strict = args.Bool("strict_rounding"),
                MaxPairTests = args.Integer("rounding_max_pair_tests"),
            };
            // above 1e-2 of the diagonal the repair may contract whole components away; below 1e-16 it is a no-op (the ulp term dominates)
            if (!(r.MaxDisplacement >= 1e-16 && r.MaxDisplacement <= 1e-2))
                throw new CommandException(ResultCodes.InvalidArguments, "'rounding_max_displacement' must be in [1e-16, 1e-2]");
            if (!(r.UlpFactor >= 0))
                throw new CommandException(ResultCodes.InvalidArguments, "'rounding_ulp_factor' must be >= 0");
            if (!(r.AbsoluteTolerance >= 0))
                throw new CommandException(ResultCodes.InvalidArguments, "'rounding_absolute_tolerance' must be >= 0");
            if (r.MaxPairTests < 0)
                throw new CommandException(ResultCodes.InvalidArguments, "'rounding_max_pair_tests' must be >= 0");
            if (r.Strict && args.Bool("safe_rounding") == false)
                throw new CommandException(ResultCodes.InvalidArguments, "'strict_rounding' needs safe_rounding");
            // (the defaults are filled in, so an explicit value equal to the default cannot be told apart; it changes nothing)
            if (r.Strict && (r.UlpFactor != 256 || r.AbsoluteTolerance != 0))
                throw new CommandException(ResultCodes.InvalidArguments, "'strict_rounding' uses the relative bound only: it cannot be combined with 'rounding_ulp_factor' or 'rounding_absolute_tolerance'");
            return r;
        }

        static void ApplyRoundingTolerances(ExactMeshBoolean op, Args args)
        {
            var r = RoundingTolerances(args);
            op.RoundingMaxDisplacement = r.MaxDisplacement;
            op.RoundingUlpFactor = r.UlpFactor;
            op.RoundingAbsoluteTolerance = r.AbsoluteTolerance;
            op.StrictRounding = r.Strict;
            op.RoundingMaxPairTests = r.MaxPairTests;
        }

        static Param SimplifyFlatRegions() =>
            new Param("simplify_flat_regions", ParamType.Boolean, "remove new vertices inside flat regions and on straight edges between them " +
                "(ExactMeshBoolean.SimplifyFlatRegions); false keeps every crossing of coplanar faces") { Default = true };

        static Command Boolean()
        {
            var c = new Command() {
                Name = "boolean",
                Description = "Exact mesh boolean (ExactMeshBoolean) of two or more meshes in one arrangement: union, intersection, " +
                    "difference (a minus all others), at_least (inside at least k of the meshes) or xor (inside an odd number of them). " +
                    "The solid of a mesh is where its winding number is positive, so self-intersecting, nested or overlapping inputs work; " +
                    "open inputs use the generalized winding number (result then generally not closed).",
            };
            c.Params.Add(Input("a", "first mesh file"));
            c.Params.Add(Input("b", "second mesh file"));
            c.Params.Add(Output());
            c.Params.Add(new Param("op", ParamType.String, "operation") { Required = true, Enum = new[] { "union", "intersection", "difference", "at_least", "xor" } });
            c.Params.Add(new Param("more", ParamType.String, "further input mesh files, separated by ';' (all inputs in one operation, in the order a, b, these)"));
            c.Params.Add(new Param("k", ParamType.Integer, "for op at_least: the number of meshes a point must be inside (1 = union, number of inputs = intersection)"));
            c.Params.Add(new Param("allow_open", ParamType.Boolean, "accept inputs that are not closed (winding number sense); false: fail with operation_failed") { Default = true });
            c.Params.Add(SafeRounding());
            AddRoundingTolerances(c);
            c.Params.Add(SimplifyFlatRegions());
            c.Params.Add(SelfIntersections());
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                string opName = args.String("op");
                if (opName == "at_least" && args.Has("k") == false)
                    throw new CommandException(ResultCodes.InvalidArguments, "op at_least needs k");
                if (args.Has("k") && opName != "at_least")
                    throw new CommandException(ResultCodes.InvalidArguments, "k is only used with op at_least");
                if (args.Has("k") && args.Integer("k") < 1)
                    throw new CommandException(ResultCodes.InvalidArguments, "k must be at least 1");
                var files = new List<string>() { args.String("a"), args.String("b") };
                if (args.Has("more"))
                    files.AddRange(args.String("more").Split(';').Select(f => f.Trim()).Where(f => f.Length > 0));
                var warnings = new List<string>();
                var meshes = files.Select(f => MeshFiles.Read(f, warnings)).ToList();
                if (opName == "at_least" && args.Integer("k") > meshes.Count)
                    warnings.Add($"k = {args.Integer("k")} is more than the {meshes.Count} inputs: the result is empty");
                ExactMeshBoolean op;
                if (opName == "at_least") {
                    op = new ExactMeshBoolean(meshes, ExactMeshBoolean.AtLeast(args.Integer("k")));
                } else if (opName == "xor") {
                    // inside an odd number of the inputs (symmetric difference; for two: a xor b)
                    op = new ExactMeshBoolean(meshes, w => w.Count(x => x > 0) % 2 == 1);
                } else {
                    var ops = new Dictionary<string, ExactMeshBoolean.Operations>() {
                        ["union"] = ExactMeshBoolean.Operations.Union,
                        ["intersection"] = ExactMeshBoolean.Operations.Intersection,
                        ["difference"] = ExactMeshBoolean.Operations.Difference,
                    };
                    // (two inputs: the two-mesh boolean, as before)
                    op = (meshes.Count == 2) ? new ExactMeshBoolean(meshes[0], meshes[1], ops[opName]) : new ExactMeshBoolean(meshes, ops[opName]);
                }
                op.AllowOpenMeshes = args.Bool("allow_open");
                op.SafeRounding = args.Bool("safe_rounding");
                ApplyRoundingTolerances(op, args);
                op.SimplifyFlatRegions = args.Bool("simplify_flat_regions");
                if (op.Compute() == false)
                    throw new CommandException(ResultCodes.OperationFailed, "boolean failed (" + op.Status + "): " + op.Error);
                MeshFiles.Write(args.String("output"), op.Result, args.Bool("binary"));
                var o = new JsonObject() { ["a"] = args.String("a"), ["b"] = args.String("b"), ["output"] = args.String("output"), ["op"] = opName };
                o["inputs"] = new JsonArray(files.Select(f => (JsonNode)f).ToArray());
                if (opName == "at_least")
                    o["k"] = args.Integer("k");
                o["exact_winding_a"] = op.ExactWindingA;
                o["exact_winding_b"] = op.ExactWindingB;
                o["exact_winding"] = new JsonArray(op.ExactWinding.Select(e => (JsonNode)e).ToArray());
                o["boolean"] = BooleanInfo(op);
                o["mesh"] = MeshReport.Metrics(op.Result, args.Bool("self_intersections"));
                o["empty_result"] = op.Result.TriangleCount == 0;
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }

        static Command Resolve()
        {
            var c = new Command() {
                Name = "resolve",
                Description = "Resolve one mesh exactly (ExactMeshBoolean.Resolve): the boundary of its solid (winding number > 0), " +
                    "with self-intersections, nested shells and duplicated faces removed.",
            };
            c.Params.Add(Input());
            c.Params.Add(Output());
            c.Params.Add(SafeRounding());
            AddRoundingTolerances(c);
            c.Params.Add(SimplifyFlatRegions());
            c.Params.Add(SelfIntersections());
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                var op = new ExactMeshBoolean(mesh, new DMesh3(), ExactMeshBoolean.Operations.Union) {
                    SafeRounding = args.Bool("safe_rounding"), SimplifyFlatRegions = args.Bool("simplify_flat_regions"),
                };
                ApplyRoundingTolerances(op, args);
                if (op.Compute() == false)
                    throw new CommandException(ResultCodes.OperationFailed, "resolve failed (" + op.Status + "): " + op.Error);
                MeshFiles.Write(args.String("output"), op.Result, args.Bool("binary"));
                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = args.String("output") };
                o["exact_winding"] = op.ExactWindingA;
                o["boolean"] = BooleanInfo(op);
                o["input_mesh"] = MeshReport.Metrics(mesh, args.Bool("self_intersections"));
                o["mesh"] = MeshReport.Metrics(op.Result, args.Bool("self_intersections"));
                o["empty_result"] = op.Result.TriangleCount == 0;
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }


        // ---- remesh, simplify ----

        static double MeanEdgeLength(DMesh3 mesh)
        {
            double sum = 0; int n = 0;
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(eid);
                sum += mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b));
                n++;
            }
            return n > 0 ? sum / n : 0;
        }

        // the wall thickness of thin parts: the 10th percentile of the distances from triangle centroids inward along
        // the normal to the next surface, over up to 2000 triangles; NaN if no ray hits (eg an open sheet)
        static double WallThickness(DMesh3 mesh)
        {
            var tree = new DMeshAABBTree3(mesh, true);
            int step = Math.Max(1, mesh.TriangleCount / 2000), k = 0;
            var dist = new List<double>();
            foreach (int tid in mesh.TriangleIndices()) {
                if (k++ % step != 0)
                    continue;
                Vector3d n = mesh.GetTriNormal(tid);
                if (n.LengthSquared < 0.5)      // degenerate
                    continue;
                var ray = new Ray3d(mesh.GetTriCentroid(tid), -n);
                tree.TriangleFilterF = t => t != tid;
                int hit = tree.FindNearestHitTriangle(ray);
                if (hit == DMesh3.InvalidID)
                    continue;
                IntrRay3Triangle3 intr = MeshQueries.TriangleIntersection(mesh, hit, ray);
                if (intr.Result == IntersectionResult.Intersects && intr.RayParameter > 0)     // (not a coincident face)
                    dist.Add(intr.RayParameter);
            }
            if (dist.Count == 0)
                return double.NaN;
            dist.Sort();
            return dist[(dist.Count - 1) / 10];
        }

        static Command Remesh()
        {
            var c = new Command() {
                Name = "remesh",
                Description = "Isotropic remeshing (Remesher: splits, collapses, flips, smoothing) towards a target edge length, " +
                    "projected back onto the input surface. Boundary and sharp edges can be kept. Passes that would turn a closed " +
                    "part inside out are rolled back (rejected_passes); an edge length far above the wall thickness gives a warning.",
            };
            c.Params.Add(Input());
            c.Params.Add(Output());
            c.Params.Add(new Param("edge_length", ParamType.Number, "target edge length (default: mean edge length of the input)"));
            c.Params.Add(new Param("iterations", ParamType.Integer, "remesh passes") { Default = 20 });
            c.Params.Add(new Param("preserve_boundary", ParamType.Boolean, "keep boundary edges fixed") { Default = true });
            c.Params.Add(new Param("crease_angle", ParamType.Number, "with preserve_boundary: also keep edges whose dihedral angle exceeds this (degrees, 0 = off)") { Default = 30.0 });
            c.Params.Add(new Param("smooth", ParamType.Number, "smoothing speed per pass, 0..1") { Default = 0.5 });
            c.Params.Add(new Param("project", ParamType.Boolean, "project the vertices onto the input surface") { Default = true });
            c.Params.Add(new Param("min_quality", ParamType.Number, "guard against poor and flat triangles (Remesher.MinTriangleQuality, " +
                "0..1, 0 = off, the library's default; 0.2 here: fewer flat triangles and self-intersections at equal or better " +
                "mean fidelity on Thingi10K and CAD-like parts, about 1.5x the time, benchmarks/RemeshQualityBenchmark; 0 for thin walls far below the edge length)") { Default = 0.2 });
            c.Params.Add(new Param("sharp_vertex_angle", ParamType.Number, "also pin vertices where the surface comes to a point (absolute " +
                "angle deficit at least this, degrees; 0 = off), such as the apex of a cone with many slices, which is no crease and " +
                "is rounded off otherwise (MeshConstraintUtil.PinSharpVertices, off in the library); vertices on kept boundaries and creases " +
                "are left to them") { Default = 75.0 });
            c.Params.Add(new Param("thickness_sizing", ParamType.Number, "target edge length at most this fraction of the local wall " +
                "thickness (Remesher.TargetEdgeLengthF, RemeshSizing.FromWallThickness; eg 1; 0 = off): thin walls are refined instead of " +
                "folded through each other, the rest is remeshed to edge_length. Costs the triangles the walls need (Thingi10K 135363, " +
                "walls 0.75 at edge length 11.6: 1.1M triangles instead of 500)") { Default = 0.0 });
            c.Params.Add(Deviation());
            c.Params.Add(SelfIntersections());
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                var warnings = new List<string>();
                DMesh3 input = MeshFiles.Read(args.String("input"), warnings);
                double thicknessSizing = args.Number("thickness_sizing");
                if (!(thicknessSizing >= 0) || double.IsInfinity(thicknessSizing))
                    throw new CommandException(ResultCodes.InvalidArguments, "'thickness_sizing' must be a finite number >= 0");
                double sharpAngle = args.Number("sharp_vertex_angle");
                if (!(sharpAngle >= 0) || double.IsInfinity(sharpAngle))
                    throw new CommandException(ResultCodes.InvalidArguments, "'sharp_vertex_angle' must be a finite number >= 0");
                double len = args.Has("edge_length") ? args.Number("edge_length") : MeanEdgeLength(input);
                if (len <= 0)
                    throw new CommandException(ResultCodes.InvalidArguments, "'edge_length' must be > 0");
                double diag = input.GetBounds().DiagonalLength;
                if (len < diag * 1e-4)
                    throw new CommandException(ResultCodes.InvalidArguments, $"'edge_length' {len} is too small for a mesh of size {diag} (minimum 1e-4 of the diagonal)");
                int iterations = args.Integer("iterations");
                if (iterations < 1 || iterations > 1000)
                    throw new CommandException(ResultCodes.InvalidArguments, "'iterations' must be in 1..1000");
                double smooth = args.Number("smooth");
                if (smooth < 0 || smooth > 1)
                    throw new CommandException(ResultCodes.InvalidArguments, "'smooth' must be in 0..1");
                double minQuality = args.Number("min_quality");
                if (!(minQuality >= 0 && minQuality < 1))
                    throw new CommandException(ResultCodes.InvalidArguments, "'min_quality' must be in 0..1 (below 1)");

                DMesh3 mesh = new DMesh3(input);
                int pinnedSharp = 0;
                var r = new Remesher(mesh) { SmoothSpeedT = smooth, EnableSmoothing = smooth > 0, MinTriangleQuality = minQuality };
                if (args.Bool("project"))
                    r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
                if (args.Bool("preserve_boundary")) {
                    if (args.Number("crease_angle") > 0) {
                        MeshConstraintUtil.PreserveBoundaryAndFeatures(r, args.Number("crease_angle"));
                    } else
                        MeshConstraintUtil.FixAllBoundaryEdges(r);
                }
                if (sharpAngle > 0)
                    pinnedSharp = MeshConstraintUtil.PinSharpVertices(r, sharpAngle);
                if (thicknessSizing > 0)
                    r.TargetEdgeLengthF = RemeshSizing.FromWallThickness(input, thicknessSizing);
                double thickness = WallThickness(input);
                // the edge length at the thinnest walls: the global one, or with the sizing about fraction times the wall
                double wallEdge = thicknessSizing > 0 ? Math.Min(len, thicknessSizing * thickness) : len;
                if (wallEdge > 4 * thickness)
                    warnings.Add($"the edge length {wallEdge:G4} is {wallEdge / thickness:F1} times the estimated wall thickness {thickness:G4} " +
                        "(10% of the inward distances are smaller): thinner walls are folded through each other (self-intersections, " +
                        "inside-out parts); use a smaller edge length or --thickness-sizing 1" +
                        (minQuality > 0 ? ", or --min-quality 0, which keeps such walls closer to the input" : ""));
                r.SetTargetEdgeLength(len);
                for (int k = 0; k < iterations; ++k)
                    r.BasicRemeshPass();
                if (mesh.TriangleCount == 0)
                    throw new CommandException(ResultCodes.OperationFailed, "remeshing removed all triangles");
                if (r.InvertingPassesRejected > 0)
                    warnings.Add($"{r.InvertingPassesRejected} of {iterations} passes were rolled back because they turned a closed part " +
                        "inside out (edge length far above the wall thickness?): the result is the state after the last pass that kept the orientation");
                MeshFiles.Write(args.String("output"), mesh, args.Bool("binary"));

                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = args.String("output") };
                o["edge_length"] = len;
                o["iterations"] = iterations;
                o["min_quality"] = minQuality;
                o["sharp_vertices_pinned"] = pinnedSharp;
                o["thickness_sizing"] = thicknessSizing;
                o["rejected_passes"] = r.InvertingPassesRejected;
                o["input_mesh"] = MeshReport.Metrics(input, false);
                o["mesh"] = MeshReport.Metrics(mesh, args.Bool("self_intersections"));
                if (args.Bool("deviation"))
                    o["hausdorff"] = Hausdorff(input, mesh);
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }

        static Command Simplify()
        {
            var c = new Command() {
                Name = "simplify",
                Description = "Simplify a mesh by quadric edge collapses (Reducer). Give exactly one target: triangles, ratio, max_error or edge_length.",
            };
            c.Params.Add(Input());
            c.Params.Add(Output());
            c.Params.Add(new Param("triangles", ParamType.Integer, "target triangle count"));
            c.Params.Add(new Param("ratio", ParamType.Number, "target triangle count as a fraction of the input, in (0, 1]"));
            c.Params.Add(new Param("max_error", ParamType.Number, "collapse while the quadric error stays below this"));
            c.Params.Add(new Param("edge_length", ParamType.Number, "collapse edges shorter than this"));
            c.Params.Add(new Param("preserve_boundary", ParamType.Boolean, "keep the shape of boundary loops") { Default = true });
            c.Params.Add(new Param("crease_angle", ParamType.Number, "keep edges whose dihedral angle exceeds this (degrees, 0 = off)") { Default = 0.0 });
            c.Params.Add(Deviation());
            c.Params.Add(SelfIntersections());
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                string[] targets = new[] { "triangles", "ratio", "max_error", "edge_length" }.Where(args.Has).ToArray();
                if (targets.Length != 1)
                    throw new CommandException(ResultCodes.InvalidArguments,
                        "give exactly one of triangles, ratio, max_error, edge_length" + (targets.Length > 1 ? " (got " + string.Join(", ", targets) + ")" : ""));
                var warnings = new List<string>();
                DMesh3 input = MeshFiles.Read(args.String("input"), warnings);
                DMesh3 mesh = new DMesh3(input);
                var r = new Reducer(mesh) {
                    PreserveBoundaryShape = args.Bool("preserve_boundary"),
                    FeatureAngle = Math.Max(0, args.Number("crease_angle")),
                };
                switch (targets[0]) {
                    case "triangles":
                        if (args.Integer("triangles") < 1)
                            throw new CommandException(ResultCodes.InvalidArguments, "'triangles' must be >= 1");
                        r.ReduceToTriangleCount(args.Integer("triangles"));
                        break;
                    case "ratio":
                        double ratio = args.Number("ratio");
                        if (ratio <= 0 || ratio > 1)
                            throw new CommandException(ResultCodes.InvalidArguments, "'ratio' must be in (0, 1]");
                        r.ReduceToTriangleCount(Math.Max(1, (int)Math.Round(ratio * input.TriangleCount)));
                        break;
                    case "max_error":
                        if (args.Number("max_error") <= 0)
                            throw new CommandException(ResultCodes.InvalidArguments, "'max_error' must be > 0");
                        r.ReduceToMaxQuadricError(args.Number("max_error"));
                        break;
                    case "edge_length":
                        if (args.Number("edge_length") <= 0)
                            throw new CommandException(ResultCodes.InvalidArguments, "'edge_length' must be > 0");
                        r.ReduceToEdgeLength(args.Number("edge_length"));
                        break;
                }
                if (mesh.TriangleCount == 0)
                    throw new CommandException(ResultCodes.OperationFailed, "simplification removed all triangles");
                MeshFiles.Write(args.String("output"), mesh, args.Bool("binary"));

                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = args.String("output"), ["target"] = targets[0] };
                o["input_mesh"] = MeshReport.Metrics(input, false);
                o["mesh"] = MeshReport.Metrics(mesh, args.Bool("self_intersections"));
                if (args.Bool("deviation"))
                    o["hausdorff"] = Hausdorff(input, mesh);
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }


        // ---- uv ----

        static JsonObject DistortionInfo(ParameterizationDistortion d)
        {
            return new JsonObject() {
                ["mean_area"] = MeshReport.Num(d.MeanAreaDistortion),
                ["max_area"] = MeshReport.Num(d.MaxAreaDistortion),
                ["mean_conformal"] = MeshReport.Num(d.MeanConformal),
                ["max_conformal"] = MeshReport.Num(d.MaxConformal),
                ["mean_angle_error"] = MeshReport.Num(d.MeanAngleError),
                ["flipped_triangles"] = d.FlippedTriangles,
            };
        }

        static Command Uv()
        {
            var c = new Command() {
                Name = "uv",
                Description = "UV atlas (UVAtlasGenerator) of any mesh, closed or open, of any genus: seams through automatically placed cones " +
                    "(BFF with cone singularities) where that gives one overlap-free chart, otherwise charts grown by normal and flattened with BFF, " +
                    "split while a chart exceeds the distortion limits; packed into the unit square and checked for overlaps with exact predicates. " +
                    "The output has the vertices split along the seams, with per-vertex UVs (OBJ, PLY, glTF/GLB, g3mesh keep them). " +
                    "atlas_status 'distortion_limit_exceeded' means overlap-free, but max_charts was not enough for the limits.",
            };
            c.Params.Add(Input());
            c.Params.Add(Output());
            c.Params.Add(new Param("method", ParamType.String, "auto: cones where they work, else charts; cones; charts; single: one chart per component") {
                Default = "auto", Enum = new[] { "auto", "cones", "charts", "single" } });
            c.Params.Add(new Param("max_area_distortion", ParamType.Number, "per chart: mean |log(area ratio)| (0 = area preserving)") { Default = 0.25 });
            c.Params.Add(new Param("max_conformal_distortion", ParamType.Number, "per chart: mean conformal distortion s1/s2 (1 = conformal)") { Default = 1.2 });
            c.Params.Add(new Param("max_charts", ParamType.Integer, "at most this many charts") { Default = 128 });
            c.Params.Add(new Param("max_normal_angle", ParamType.Number, "charts: largest angle between a triangle normal and its chart normal (degrees)") { Default = 50.0 });
            c.Params.Add(new Param("feature_weight", ParamType.Number, "seams prefer sharp edges (0 = shortest seams)") { Default = 1.0 });
            c.Params.Add(new Param("padding", ParamType.Number, "gap between charts in the unit square, in [0, 0.25)") { Default = 0.005 });
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                double maxArea = args.Number("max_area_distortion"), maxConf = args.Number("max_conformal_distortion");
                double padding = args.Number("padding"), angle = args.Number("max_normal_angle");
                if (!(maxArea > 0))
                    throw new CommandException(ResultCodes.InvalidArguments, "'max_area_distortion' must be > 0");
                if (!(maxConf >= 1))
                    throw new CommandException(ResultCodes.InvalidArguments, "'max_conformal_distortion' must be >= 1");
                if (args.Integer("max_charts") < 1)
                    throw new CommandException(ResultCodes.InvalidArguments, "'max_charts' must be >= 1");
                if (!(padding >= 0 && padding < 0.25))
                    throw new CommandException(ResultCodes.InvalidArguments, "'padding' must be in [0, 0.25)");
                if (!(angle > 0 && angle <= 180))
                    throw new CommandException(ResultCodes.InvalidArguments, "'max_normal_angle' must be in (0, 180]");
                if (!(args.Number("feature_weight") >= 0))
                    throw new CommandException(ResultCodes.InvalidArguments, "'feature_weight' must be >= 0");
                var warnings = new List<string>();
                if (!MeshFiles.StoresUVs(args.String("output")))
                    warnings.Add("the output format does not store UVs (use " + string.Join(", ", MeshFiles.UVFormats) + ")");
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                var methods = new Dictionary<string, UVAtlasGenerator.Methods>() {
                    ["auto"] = UVAtlasGenerator.Methods.Auto, ["cones"] = UVAtlasGenerator.Methods.Cones,
                    ["charts"] = UVAtlasGenerator.Methods.Charts, ["single"] = UVAtlasGenerator.Methods.SingleChart,
                };
                var atlas = new UVAtlasGenerator(mesh) {
                    Method = methods[args.String("method")],
                    MaxAreaDistortion = maxArea, MaxConformalDistortion = maxConf,
                    MaxCharts = args.Integer("max_charts"), MaxNormalAngle = angle,
                    FeatureWeight = args.Number("feature_weight"), Padding = padding,
                };
                atlas.Compute();
                if (atlas.Status == UVAtlasStatus.InvalidMesh || atlas.Status == UVAtlasStatus.Failed || atlas.AtlasMesh == null)
                    throw new CommandException(ResultCodes.OperationFailed, "uv atlas failed: " + atlas.Error);
                if (atlas.Status == UVAtlasStatus.DistortionLimitExceeded)
                    warnings.Add(atlas.Error);
                MeshFiles.Write(args.String("output"), atlas.AtlasMesh, args.Bool("binary"), uvs: true);

                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = args.String("output"), ["method"] = args.String("method") };
                o["atlas_status"] = atlas.Status == UVAtlasStatus.Success ? "success" : "distortion_limit_exceeded";
                o["charts"] = atlas.ChartCount;
                o["charts_over_limit"] = atlas.ChartsOverLimit;
                o["chart_methods"] = new JsonObject(atlas.Charts.GroupBy(ch => ch.Method).OrderBy(g => g.Key)
                    .Select(g => new KeyValuePair<string, JsonNode>(g.Key, g.Count())));
                o["cones"] = atlas.ConeCount;
                o["cone_components"] = atlas.ConeComponents;
                o["seam_length"] = MeshReport.Num(atlas.SeamLength);
                o["coverage"] = MeshReport.Num(atlas.Coverage);
                o["overlapping_pairs"] = atlas.Overlap.OverlappingPairs;
                o["inverted_triangles"] = atlas.Overlap.InvertedTriangles;
                o["float_rounding_defects"] = atlas.FloatRoundingDefects;
                o["distortion"] = DistortionInfo(atlas.Distortion);
                o["input_mesh"] = MeshReport.Metrics(mesh, false);
                o["vertices"] = atlas.AtlasMesh.VertexCount;
                o["triangles"] = atlas.AtlasMesh.TriangleCount;
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }


        // ---- convert ----

        static Command Convert()
        {
            var c = new Command() {
                Name = "convert",
                Description = "Convert a mesh file to another format (by extension): STL, OBJ, OFF, PLY, 3MF, glTF/GLB, g3mesh. " +
                    "All meshes of the input are merged into one.",
            };
            c.Params.Add(Input());
            c.Params.Add(Output());
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                MeshFiles.Write(args.String("output"), mesh, args.Bool("binary"));
                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = args.String("output") };
                o["vertices"] = mesh.VertexCount;
                o["triangles"] = mesh.TriangleCount;
                o["bytes"] = new FileInfo(args.String("output")).Length;
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }
    }
}
