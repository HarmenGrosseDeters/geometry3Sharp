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
    public static class Commands
    {
        public static readonly List<Command> All = new List<Command>() {
            Measure(), Repair(), Boolean(), Resolve(), Remesh(), Simplify(), Uv(), Convert()
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
            c.Params.Add(new Param("tolerance", ParamType.Number, "weld / crack-closing tolerance of MeshAutoRepair (default 1e-6)"));
            c.Params.Add(new Param("solidify_resolution", ParamType.Integer, "robust: grid cells along the bounding box diagonal of the solidify fallback, 8..2048") { Default = 128 });
            c.Params.Add(new Param("min_volume_iou", ParamType.Number, "robust: minimum volume IoU of the repair with the input before falling back (0 = no check)") { Default = 0.99 });
            c.Params.Add(new Param("exact_resolve", ParamType.Boolean, "robust: before the solidify, resolve the self-intersections exactly (ExactMeshBoolean.Resolve)") { Default = true });
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
                    var repair = new MeshRobustRepair(new DMesh3(mesh)) {
                        SolidifyResolution = args.Integer("solidify_resolution"),
                        MinVolumeIoU = args.Number("min_volume_iou"),
                        CheckSelfIntersections = selfInt,
                        ExactResolve = args.Bool("exact_resolve"),
                        ConfigureRepair = configure,
                    };
                    if (repair.Apply() == false)
                        throw new CommandException(ResultCodes.OperationFailed,
                            "MeshRobustRepair failed" + (repair.FallbackReason != "" ? " (repair result: " + repair.FallbackReason + ", solidify gave no surface)" : ""));
                    result = repair.Mesh;
                    o["used_exact_resolve"] = repair.UsedExactResolve;
                    o["exact_resolve_reason"] = repair.ExactResolveReason == "" ? null : repair.ExactResolveReason;
                    o["used_solidify"] = repair.UsedSolidify;
                    o["fallback_reason"] = repair.FallbackReason == "" ? null : repair.FallbackReason;
                    o["volume_iou"] = MeshReport.Num(repair.RepairVolumeIoU);
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
            };
        }

        static Command Boolean()
        {
            var c = new Command() {
                Name = "boolean",
                Description = "Exact mesh boolean (ExactMeshBoolean) of two or more meshes in one arrangement: union, intersection, " +
                    "difference (a minus all others) or at_least (inside at least k of the meshes). " +
                    "The solid of a mesh is where its winding number is positive, so self-intersecting, nested or overlapping inputs work; " +
                    "open inputs use the generalized winding number (result then generally not closed).",
            };
            c.Params.Add(Input("a", "first mesh file"));
            c.Params.Add(Input("b", "second mesh file"));
            c.Params.Add(Output());
            c.Params.Add(new Param("op", ParamType.String, "operation") { Required = true, Enum = new[] { "union", "intersection", "difference", "at_least" } });
            c.Params.Add(new Param("more", ParamType.String, "further input mesh files, separated by ';' (all inputs in one operation, in the order a, b, these)"));
            c.Params.Add(new Param("k", ParamType.Integer, "for op at_least: the number of meshes a point must be inside (1 = union, number of inputs = intersection)"));
            c.Params.Add(new Param("allow_open", ParamType.Boolean, "accept inputs that are not closed (winding number sense); false: fail with operation_failed") { Default = true });
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
            c.Params.Add(SelfIntersections());
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                var op = new ExactMeshBoolean(mesh, new DMesh3(), ExactMeshBoolean.Operations.Union);
                if (op.Compute() == false)
                    throw new CommandException(ResultCodes.OperationFailed, "resolve failed: " + op.Error);
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

        static Command Remesh()
        {
            var c = new Command() {
                Name = "remesh",
                Description = "Isotropic remeshing (Remesher: splits, collapses, flips, smoothing) towards a target edge length, " +
                    "projected back onto the input surface. Boundary and sharp edges can be kept.",
            };
            c.Params.Add(Input());
            c.Params.Add(Output());
            c.Params.Add(new Param("edge_length", ParamType.Number, "target edge length (default: mean edge length of the input)"));
            c.Params.Add(new Param("iterations", ParamType.Integer, "remesh passes") { Default = 20 });
            c.Params.Add(new Param("preserve_boundary", ParamType.Boolean, "keep boundary edges fixed") { Default = true });
            c.Params.Add(new Param("crease_angle", ParamType.Number, "with preserve_boundary: also keep edges whose dihedral angle exceeds this (degrees, 0 = off)") { Default = 30.0 });
            c.Params.Add(new Param("smooth", ParamType.Number, "smoothing speed per pass, 0..1") { Default = 0.5 });
            c.Params.Add(new Param("project", ParamType.Boolean, "project the vertices onto the input surface") { Default = true });
            c.Params.Add(Deviation());
            c.Params.Add(SelfIntersections());
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                var warnings = new List<string>();
                DMesh3 input = MeshFiles.Read(args.String("input"), warnings);
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

                DMesh3 mesh = new DMesh3(input);
                var r = new Remesher(mesh) { SmoothSpeedT = smooth, EnableSmoothing = smooth > 0 };
                if (args.Bool("project"))
                    r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
                if (args.Bool("preserve_boundary")) {
                    if (args.Number("crease_angle") > 0)
                        MeshConstraintUtil.PreserveBoundaryAndFeatures(r, args.Number("crease_angle"));
                    else
                        MeshConstraintUtil.FixAllBoundaryEdges(r);
                }
                r.SetTargetEdgeLength(len);
                for (int k = 0; k < iterations; ++k)
                    r.BasicRemeshPass();
                if (mesh.TriangleCount == 0)
                    throw new CommandException(ResultCodes.OperationFailed, "remeshing removed all triangles");
                MeshFiles.Write(args.String("output"), mesh, args.Bool("binary"));

                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = args.String("output") };
                o["edge_length"] = len;
                o["iterations"] = iterations;
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
