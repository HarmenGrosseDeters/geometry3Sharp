// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using g3;
using gs;

namespace g3cli
{
    public static partial class Commands
    {
        // ---- quad ----

        static string QuadFormat(string format, string output)
        {
            if (format == "obj" || format == "ply") return format;
            string ext = Path.GetExtension(output ?? "").ToLowerInvariant();
            if (ext == ".obj") return "obj";
            if (ext == ".ply") return "ply";
            throw new CommandException(ResultCodes.UnknownFormat,
                $"cannot write '{output}': a quad mesh is written as OBJ or PLY (the polygon writers); the extension must be .obj or .ply, or give --format obj or ply");
        }

        static Command Quad()
        {
            var c = new Command() {
                Name = "quad",
                Description = "Quad remeshing of a manifold triangle mesh (QuadRemesher: cross field, position field, extraction from the integer lattice offsets; docs/design/QUAD_REMESHING.md): " +
                    "a polygon mesh of mostly quads, with a few triangles, pentagons and hexagons near the singularities (quad-dominant, the library default), " +
                    "written with the faces kept whole as OBJ ('f' lines with 4 vertices) or PLY (ascii, or binary with 'binary'); the other mesh formats cannot hold quads. " +
                    "With 'pure_quads' the opt-in pure-quad repair runs after the extraction (closed meshes only; it succeeds on some shapes only): if every check passes the file has quads only " +
                    "(pure_quad_status success), otherwise the file has the quad-dominant mesh and pure_quad_status names the first failed check " +
                    "(orientation_folds, flipped_faces, not_manifold, flow_infeasible (also any open mesh), budget_exceeded), with 'pure_quads_fell_back' true and a warning: never silent, and not an error. " +
                    "Result: quad_status, pure_quad_status, size_adaptation / size_adaptation_applied (with curl_before, curl_after, min_spacing, max_spacing), the face mix (quads, non-quads, triangles, pentagons, hexagons, larger), irregular vertices, closed / manifold flags, " +
                    "corner angle and aspect statistics of the quads (median, p95, p99, max), the stage timings and the pure-quad counters. " +
                    "Failures of the remeshing (field_failed, position_field_failed, no_faces) end with operation_failed and quad_status; nothing is written then. " +
                    "The vertices of the quad-dominant mesh lie on the tangent planes of the input, not on its surface (a successful pure_quads result is projected onto the input; 'deviation' reports the Hausdorff distance).",
            };
            c.Params.Add(Input());
            c.Params.Add(new Param("output", ParamType.String, "output polygon mesh file (.obj or .ply; see 'format')") { Required = true, Positional = true });
            c.Params.Add(new Param("format", ParamType.String, "auto: by the extension; obj; ply") { Enum = new[] { "auto", "obj", "ply" }, Default = "auto" });
            c.Params.Add(new Param("edge_length", ParamType.Number, "target edge length of the quads in mesh units (the library has no default: if omitted, the larger of 1/30 of the bounding box diagonal and twice the mean edge length of the input; the value used is reported as used_edge_length)"));
            c.Params.Add(new Param("size_adaptation", ParamType.String, "opt-in size field (IntegrableFrames, library default off): off (uniform spacing), scale (log spacing only), or scale_and_rotation (spacing and small field rotation); intended for closed smooth shapes, applied only when curl drops by at least 20 % (see size_adaptation_applied); can be combined with pure_quads (local spacing in the pure-quad flow)") { Enum = new[] { "off", "scale", "scale_and_rotation" }, Default = "off" });
            c.Params.Add(new Param("curvature_alignment", ParamType.Number, "weight for aligning the cross field with principal curvature directions where anisotropic (0 = smoothest field, library default; about 1 follows principal directions)") { Default = 0.0 });
            c.Params.Add(new Param("align_creases", ParamType.Boolean, "align the cross field with crease edges whose dihedral angle exceeds crease_angle (library default false)") { Default = false });
            c.Params.Add(new Param("crease_angle", ParamType.Number, "dihedral angle threshold in degrees (0..180) for align_creases (library default 30)") { Default = 30.0 });
            c.Params.Add(new Param("corner_angle", ParamType.Number, "boundary turn angle in degrees (0..180) above which a boundary vertex is pinned as a lattice corner (library default 30)") { Default = 30.0 });
            c.Params.Add(new Param("pure_quads", ParamType.Boolean, "try the pure-quad repair after the extraction (library default false; closed meshes only; falls back to the quad-dominant mesh with a status, see pure_quad_status)") { Default = false });
            c.Params.Add(new Param("parallel", ParamType.Boolean, "use all cores (the result does not depend on it)") { Default = true });
            c.Params.Add(new Param("binary", ParamType.Boolean, "write PLY as binary_little_endian (false: ASCII; OBJ is always text)") { Default = true });
            c.Params.Add(Deviation());
            c.Params.Add(SelfIntersections());
            c.Run = args => {
                string output = args.String("output");
                string format = QuadFormat(args.String("format"), output);
                var warnings = new List<string>();
                double edge = 0;
                if (args.Has("edge_length")) edge = Positive(args, "edge_length");
                string sizeAdaptStr = args.String("size_adaptation");
                SizeAdaptationMode sizeAdapt;
                if (sizeAdaptStr == "off") sizeAdapt = SizeAdaptationMode.Off;
                else if (sizeAdaptStr == "scale") sizeAdapt = SizeAdaptationMode.Scale;
                else if (sizeAdaptStr == "scale_and_rotation") sizeAdapt = SizeAdaptationMode.ScaleAndRotation;
                else throw new CommandException(ResultCodes.InvalidArguments, "'size_adaptation' must be off, scale or scale_and_rotation");
                bool pure = args.Bool("pure_quads");
                double curvatureAlignment = args.Number("curvature_alignment");
                if (!(curvatureAlignment >= 0) || double.IsInfinity(curvatureAlignment))
                    throw new CommandException(ResultCodes.InvalidArguments, "'curvature_alignment' must be >= 0 and finite");
                bool alignCreases = args.Bool("align_creases");
                double creaseAngle = args.Number("crease_angle");
                if (!(creaseAngle >= 0 && creaseAngle <= 180))
                    throw new CommandException(ResultCodes.InvalidArguments, "'crease_angle' must be in 0..180");
                double cornerAngle = args.Number("corner_angle");
                if (!(cornerAngle >= 0 && cornerAngle <= 180))
                    throw new CommandException(ResultCodes.InvalidArguments, "'corner_angle' must be in 0..180");
                DMesh3 input = MeshFiles.Read(args.String("input"), warnings);
                double diagonal = input.GetBounds().DiagonalLength;
                bool defaulted = !args.Has("edge_length");
                if (defaulted) {
                    // no library default: 1/30 of the diagonal, but not below twice the mean edge of the input (a target far below the edges extracts no face)
                    double sum = 0; int n = 0;
                    foreach (int eid in input.EdgeIndices()) { Index2i ev = input.GetEdgeV(eid); sum += input.GetVertex(ev.a).Distance(input.GetVertex(ev.b)); n++; }
                    edge = Math.Max(diagonal / 30, n > 0 ? 2 * sum / n : 0);
                    if (!(edge > 0) || double.IsInfinity(edge))
                        throw new CommandException(ResultCodes.InvalidArguments, "the bounding box of the input is degenerate or not finite: give 'edge_length' (non-finite coordinates cannot be remeshed)");
                }
                var qr = new QuadRemesher(input) {
                    TargetEdgeLength = edge,
                    SizeAdaptation = sizeAdapt,
                    CurvatureAlignment = curvatureAlignment,
                    AlignCreases = alignCreases,
                    CreaseAngleDegrees = creaseAngle,
                    CornerAngleDegrees = cornerAngle,
                    PureQuads = pure,
                    Parallel = args.Bool("parallel")
                };
                qr.Compute();
                string status = ToSnake(qr.Status.ToString());
                if (qr.Status == QuadRemeshStatus.InvalidInput)
                    throw new CommandException(ResultCodes.InvalidArguments, "quad remeshing: " + qr.Error, new JsonObject() { ["quad_status"] = status });
                if (qr.Status != QuadRemeshStatus.Success || qr.Result == null || qr.Result.FaceCount == 0) {
                    var failure = new JsonObject() { ["quad_status"] = status, ["used_edge_length"] = Val(edge) };
                    if (qr.Status == QuadRemeshStatus.FieldFailed) failure["field_status"] = ToSnake(qr.FieldStatus.ToString());
                    throw new CommandException(ResultCodes.OperationFailed, "quad remeshing failed: " + status + (qr.Error != null ? " (" + qr.Error + ")" : ""), failure);
                }
                DPolyMesh result = qr.Result;
                string pureStatus = ToSnake(qr.PureQuadStatus.ToString());
                bool fellBack = pure && qr.PureQuadStatus != PureQuadStatus.Success;
                if (fellBack)
                    warnings.Add("pure_quads fell back to the quad-dominant mesh: " + pureStatus + " (the file has triangles, pentagons or hexagons; see pure_quad_status)");

                PolyMeshMetrics pm = PolyMeshMetrics.Compute(result);
                JsonObject deviation = null;
                if (args.Bool("deviation")) {
                    DMesh3 tri = result.ToDMesh3(out int[] unusedMap);
                    JsonObject h = Hausdorff(input, tri);
                    if (h == null)
                        throw new CommandException(ResultCodes.OperationFailed, "the Hausdorff distance could not be computed; nothing is written", new JsonObject() { ["quad_status"] = status });
                    deviation = new JsonObject() { ["hausdorff"] = h, ["measured_on"] = "the result triangulated (DPolyMesh.ToDMesh3 ear-clipping)" };
                }
                try {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(output));
                    if (Directory.Exists(dir) == false)
                        throw new CommandException(ResultCodes.WriteError, $"cannot write '{output}': directory does not exist");
                    string temp = Path.Combine(dir, ".g3cli-quad-" + Guid.NewGuid().ToString("N") + ".tmp");
                    try {
                        IOWriteResult w;
                        if (format == "obj") {
                            using (var sw = new StreamWriter(temp, false, new System.Text.UTF8Encoding(false)))
                                w = PolyMeshIO.WriteOBJ(sw, result);
                        } else if (args.Bool("binary")) {
                            using (var bw = new BinaryWriter(File.Open(temp, FileMode.Create)))
                                w = PolyMeshIO.WritePLY(bw, result);
                        } else {
                            using (var sw = new StreamWriter(temp, false, new System.Text.UTF8Encoding(false)))
                                w = PolyMeshIO.WritePLY(sw, result);
                        }
                        if (w.code != IOCode.Ok)
                            throw new CommandException(ResultCodes.WriteError, $"cannot write '{output}': {w.code}: {w.message}");
                        File.Move(temp, output, true);   // an existing output is replaced only by a complete file
                    } finally {
                        try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                    }
                } catch (CommandException) {
                    throw;
                } catch (Exception e) {
                    throw new CommandException(ResultCodes.WriteError, $"cannot write '{output}': {e.Message}");
                }

                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = output };
                o["format"] = format;
                o["quad_status"] = status;
                o["size_adaptation"] = sizeAdaptStr;
                o["size_adaptation_applied"] = qr.SizeAdaptationApplied;
                o["curl_before"] = Val(qr.CurlBefore);
                o["curl_after"] = Val(qr.CurlAfter);
                o["min_spacing"] = Val(qr.MinSpacing);
                o["max_spacing"] = Val(qr.MaxSpacing);
                o["pure_quads_requested"] = pure;
                o["pure_quad_status"] = pureStatus;
                o["pure_quads_fell_back"] = fellBack;
                o["used_edge_length"] = Val(edge);
                o["edge_length_defaulted"] = defaulted;
                int nonQuads = pm.Faces - pm.Quads;
                o["faces"] = new JsonObject() {
                    ["total"] = pm.Faces, ["quads"] = pm.Quads, ["non_quads"] = nonQuads, ["quad_fraction"] = Val(pm.QuadFraction),
                    ["triangles"] = pm.Triangles, ["pentagons"] = pm.Pentagons, ["hexagons"] = pm.Hexagons, ["larger"] = pm.LargerFaces, ["max_sides"] = pm.MaxSides,
                    ["pure_quads"] = pm.Faces > 0 && nonQuads == 0,
                };
                o["vertices"] = pm.Vertices;
                o["edges"] = pm.Edges;
                o["irregular_vertices"] = pm.IrregularVertices;
                o["boundary_vertices"] = pm.BoundaryVertices;
                o["euler_characteristic"] = pm.EulerCharacteristic;
                o["manifold"] = pm.IsManifold;
                o["closed"] = pm.IsManifold && pm.BoundaryVertices == 0;
                o["quad_quality"] = new JsonObject() {
                    ["corner_deviation_degrees"] = new JsonObject() {
                        ["median"] = Val(PolyMeshMetrics.Percentile(pm.CornerDeviations, 0.5)), ["p95"] = Val(PolyMeshMetrics.Percentile(pm.CornerDeviations, 0.95)),
                        ["p99"] = Val(PolyMeshMetrics.Percentile(pm.CornerDeviations, 0.99)), ["max"] = Val(PolyMeshMetrics.Percentile(pm.CornerDeviations, 1)) },
                    ["aspect"] = new JsonObject() {
                        ["median"] = Val(PolyMeshMetrics.Percentile(pm.Aspects, 0.5)), ["p95"] = Val(PolyMeshMetrics.Percentile(pm.Aspects, 0.95)),
                        ["p99"] = Val(PolyMeshMetrics.Percentile(pm.Aspects, 0.99)), ["max"] = Val(PolyMeshMetrics.Percentile(pm.Aspects, 1)) },
                };
                o["timings_seconds"] = new JsonObject() {
                    ["cross_field"] = Val(qr.FieldSeconds), ["size_adaptation"] = Val(qr.SizeSeconds), ["position_field"] = Val(qr.PositionSeconds), ["extraction"] = Val(qr.ExtractSeconds),
                    ["pure_quad_flow"] = Val(qr.FlowSeconds), ["pure_quad_repair"] = Val(qr.RepairSeconds),
                };
                o["pure_quad_counters"] = new JsonObject() { ["inverted_triangles"] = qr.InvertedTriangles, ["flipped_quads"] = qr.FlippedQuads };
                o["cleanup"] = new JsonObject() { ["collapsed_edges"] = qr.CollapsedEdges, ["removed_valence_2"] = qr.RemovedValence2, ["dropped_faces"] = qr.DroppedFaces };
                if (deviation != null) o["deviation"] = deviation;
                o["input_mesh"] = MeshReport.Metrics(input, args.Bool("self_intersections"));
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }
    }
}
