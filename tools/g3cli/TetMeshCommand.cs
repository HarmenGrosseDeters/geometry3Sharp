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
        // ---- tetmesh ----

        static TetMeshWriter.Format TetFormat(string format, string output)
        {
            switch (format) {
                case "medit": return TetMeshWriter.Format.Medit;
                case "msh41": return TetMeshWriter.Format.Msh41;
                case "msh22": return TetMeshWriter.Format.Msh22;
                case "vtk": return TetMeshWriter.Format.Vtk;
            }
            TetMeshWriter.Format? f = TetMeshWriter.FormatOfExtension(output);
            if (f == null)
                throw new CommandException(ResultCodes.UnknownFormat,
                    $"cannot write '{output}': the extension must be .mesh (Medit), .msh (Gmsh 4.1) or .vtk (VTK), or give --format medit, msh41, msh22 or vtk");
            return f.Value;
        }

        static Command TetMesh()
        {
            var c = new Command() {
                Name = "tetmesh",
                Description = "Tetrahedral mesh of any input (MeshTetrahedralization: alpha wrap, Delaunay refinement, flips and smoothing; docs/design/TET_MESHING.md): " +
                    "a valid tet mesh of a closed, 2-manifold boundary that encloses the input at about 'offset' and follows its detail down to about 'alpha'. " +
                    "Writes Medit .mesh, Gmsh .msh (4.1, or 2.2 with --format msh22) or VTK legacy .vtk (ASCII); no reader exists. Every boundary triangle has a marker " +
                    "(the group id of the nearest input triangle if the input has triangle groups, else its triangle id) and every tet a region id " +
                    "(connected component); the file refs are marker + 1 and region + 1 (0 = unmarked). Result: tet_status (success, or " +
                    "point_limit_reached: a valid mesh, less refined, with a warning), counts, 'valid' (exact check), quality statistics and 'targets' " +
                    "(the min_dihedral and max_radius_edge targets checked on the output: counts and 'met' flags, not a proof), the marker distances " +
                    "(boundary centroid to nearest input triangle; with --deviation also the Hausdorff distance). Other statuses fail with operation_failed " +
                    "and wrap_status or tet_status; nothing is written then.",
            };
            c.Params.Add(Input());
            c.Params.Add(new Param("output", ParamType.String, "output tet mesh file (.mesh Medit, .msh Gmsh, .vtk VTK; see 'format')") { Required = true, Positional = true });
            c.Params.Add(new Param("format", ParamType.String, "auto: by the extension (.msh is Gmsh 4.1); medit; msh41; msh22 (Gmsh 2.2); vtk") { Enum = new[] { "auto", "medit", "msh41", "msh22", "vtk" }, Default = "auto" });
            c.Params.Add(new Param("relative_alpha", ParamType.Number, "bounding box diagonal divided by this gives the wrap's alpha (library default 40; larger follows the input more closely at more tets)"));
            c.Params.Add(new Param("relative_offset", ParamType.Number, "bounding box diagonal divided by this gives the wrap's offset (library default 600)"));
            c.Params.Add(new Param("min_dihedral", ParamType.Number, "target for the smallest dihedral angle in degrees (0 = off; library default 10): tets below it are worked on by the refinement and improvement, and counted in targets"));
            c.Params.Add(new Param("max_radius_edge", ParamType.Number, "target for the radius-edge ratio (library default 2): tets above it are refined where the fixed surface allows, and counted in targets"));
            c.Params.Add(new Param("max_circumradius", ParamType.Number, "size bound in mesh units: tets with a larger circumradius are refined (0 = none, the library default; at least 1/100 of the bounding box diagonal)"));
            c.Params.Add(new Param("max_steiner_points", ParamType.Integer, "stop refining after this many inserted points (status point_limit_reached; library default 2000000)"));
            c.Params.Add(new Param("improve", ParamType.Boolean, "flips, smoothing, splits and contractions after the refinement (library default true)"));
            c.Params.Add(new Param("markers", ParamType.Boolean, "compute the boundary markers and region ids and write them as refs (false: every ref is 0)") { Default = true });
            c.Params.Add(Deviation());
            c.Params.Add(SelfIntersections());
            c.Run = args => {
                string output = args.String("output");
                TetMeshWriter.Format format = TetFormat(args.String("format"), output);
                var tm = new MeshTetrahedralization((DMesh3)null);
                if (args.Has("relative_alpha")) tm.RelativeAlpha = AtMost(Positive(args, "relative_alpha"), 1000, "relative_alpha");
                if (args.Has("relative_offset")) tm.RelativeOffset = AtMost(Positive(args, "relative_offset"), 100000, "relative_offset");
                if (args.Has("min_dihedral")) {
                    double a = args.Number("min_dihedral");
                    if (!(a >= 0 && a <= 70))
                        throw new CommandException(ResultCodes.InvalidArguments, "'min_dihedral' must be in [0, 70] degrees");
                    tm.SliverDihedralDegrees = a;
                }
                if (args.Has("max_radius_edge")) {
                    double r = args.Number("max_radius_edge");
                    if (!(r >= 1.5 && r <= 1000))
                        throw new CommandException(ResultCodes.InvalidArguments, "'max_radius_edge' must be in [1.5, 1000] (the refinement's theory needs more than 2; below 1.5 it can take minutes)");
                    tm.MaxRadiusEdgeRatio = r;
                }
                if (args.Has("max_steiner_points")) {
                    int n = args.Integer("max_steiner_points");
                    if (n < 1 || n > 2000000)
                        throw new CommandException(ResultCodes.InvalidArguments, "'max_steiner_points' must be in 1..2000000 (the library default)");
                    tm.MaxSteinerPoints = n;
                }
                if (args.Has("improve")) tm.Improve = args.Bool("improve");
                var warnings = new List<string>();
                DMesh3 input = MeshFiles.Read(args.String("input"), warnings);
                tm.Mesh = input;
                double diagonal = input.GetBounds().DiagonalLength;
                if (args.Has("max_circumradius")) {
                    double r = args.Number("max_circumradius");
                    if (!(r >= 0) || (r > 0 && diagonal / r > 100))
                        throw new CommandException(ResultCodes.InvalidArguments, "'max_circumradius' must be 0 (none) or at least 1/100 of the bounding box diagonal (" + diagonal / 100 + ")");
                    tm.MaxCircumradius = r;
                }
                tm.Compute();
                string tetStatus = ToSnake(tm.Status.ToString()), wrapStatus = ToSnake(tm.WrapStatus.ToString());
                var failure = new JsonObject() { ["tet_status"] = tetStatus, ["wrap_status"] = wrapStatus };
                if (tm.Status == MeshTetrahedralization.TetStatus.WrapFailed && tm.WrapStatus == MeshAlphaWrap.WrapStatus.InvalidParameters)
                    throw new CommandException(ResultCodes.InvalidArguments, "tet mesh: the alpha wrap has invalid parameters" + (tm.Wrap?.Error != null ? ": " + tm.Wrap.Error : ""), failure);
                bool usable = tm.Status == MeshTetrahedralization.TetStatus.Success || tm.Status == MeshTetrahedralization.TetStatus.PointLimitReached;
                if (!usable || tm.Result == null || tm.Result.Tets.Count == 0)
                    throw new CommandException(ResultCodes.OperationFailed, "tet mesh failed: " + tetStatus + (tm.Status == MeshTetrahedralization.TetStatus.WrapFailed ? " (" + wrapStatus + ")" : ""), failure);
                if (tm.Status == MeshTetrahedralization.TetStatus.PointLimitReached)
                    warnings.Add("max_steiner_points reached: the tet mesh is valid but not fully refined");

                TetMesh3 t = tm.Result;
                TetMeshMarkers markers = args.Bool("markers") ? TetMeshMarkers.Compute(t, input) : null;
                try {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(output));
                    if (Directory.Exists(dir) == false)
                        throw new CommandException(ResultCodes.WriteError, $"cannot write '{output}': directory does not exist");
                    TetMeshWriter.Write(output, t, markers, format);
                } catch (CommandException) {
                    throw;
                } catch (Exception e) {
                    throw new CommandException(ResultCodes.WriteError, $"cannot write '{output}': {e.Message}");
                }

                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = output };
                o["tet_status"] = tetStatus;
                o["wrap_status"] = wrapStatus;
                o["format"] = ToSnake(format.ToString());
                o["used_alpha"] = Val(tm.UsedAlpha);
                o["used_offset"] = Val(tm.UsedOffset);
                o["counts"] = new JsonObject() {
                    ["vertices"] = t.Vertices.Count, ["tets"] = t.Tets.Count, ["boundary_triangles"] = t.BoundaryTriangles.Count,
                    ["regions"] = markers?.RegionCount
                };
                var steps = new JsonObject();
                foreach (var kv in tm.Counts.OrderBy(k => k.Key, StringComparer.Ordinal)) steps[kv.Key] = kv.Value;
                o["steps"] = steps;
                string notValid = TetMeshQuality.NotValidReason(t);
                o["valid"] = notValid == null;
                o["not_valid_reason"] = notValid;

                TetQualityStatistics q = TetMeshQuality.Measure(t);
                o["quality"] = new JsonObject() {
                    ["min_dihedral_degrees"] = new JsonObject() { ["worst"] = Val(q.MinDihedral.Worst), ["worst_1_percent"] = Val(q.MinDihedral.Worst1Percent), ["median"] = Val(q.MinDihedral.Median) },
                    ["max_dihedral_degrees"] = new JsonObject() { ["worst"] = Val(q.MaxDihedral.Worst), ["median"] = Val(q.MaxDihedral.Median) },
                    ["radius_edge"] = new JsonObject() { ["worst"] = Val(q.RadiusEdge.Worst), ["worst_1_percent"] = Val(q.RadiusEdge.Worst1Percent), ["median"] = Val(q.RadiusEdge.Median) },
                    ["tets_below_10_degrees"] = q.MinDihedralBelow10,
                    ["tets_radius_edge_above_2"] = q.RadiusEdgeAbove2,
                    ["total_volume"] = Val(q.TotalVolume),
                };
                TetTargetReport tr = tm.Targets;
                o["targets"] = new JsonObject() {
                    ["min_dihedral_degrees"] = Val(tr.MinDihedralDegrees),
                    ["max_radius_edge"] = Val(tr.MaxRadiusEdgeRatio),
                    ["tets_below_min_dihedral"] = tr.TetsBelowMinDihedral,
                    ["tets_above_max_radius_edge"] = tr.TetsAboveRadiusEdge,
                    ["worst_min_dihedral_degrees"] = Val(tr.WorstMinDihedral),
                    ["worst_radius_edge"] = Val(tr.WorstRadiusEdge),
                    ["min_dihedral_met"] = tr.MinDihedralMet,
                    ["radius_edge_met"] = tr.RadiusEdgeMet,
                    ["met"] = tr.Met,
                };

                var dev = new JsonObject();
                if (markers != null) {
                    int distinct = markers.BoundaryMarker.Where(x => x >= 0).Distinct().Count();
                    o["markers"] = new JsonObject() {
                        ["kind"] = markers.MarkersAreGroups ? "group_id" : "triangle_id",
                        ["distinct"] = distinct,
                        ["ref_offset"] = 1,
                    };
                    dev["boundary_to_input_max"] = Val(markers.MaxBoundaryDistance);
                    dev["boundary_to_input_mean"] = Val(markers.MeanBoundaryDistance);
                    dev["boundary_to_input_max_relative"] = Val(diagonal > 0 ? markers.MaxBoundaryDistance / diagonal : 0);
                    dev["sampled_at"] = "boundary triangle centroids";
                }
                DMesh3 boundary = t.ToSurfaceMesh();
                if (args.Bool("deviation"))
                    dev["hausdorff"] = Hausdorff(input, boundary);
                o["deviation"] = dev;
                o["input_mesh"] = MeshReport.Metrics(input, false);
                o["boundary_mesh"] = MeshReport.Metrics(boundary, args.Bool("self_intersections"));
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }
    }
}
