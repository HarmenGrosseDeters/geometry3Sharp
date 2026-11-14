// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using g3;

namespace g3cli
{
    /// <summary>
    /// offset: the offset surface of a closed mesh by marching cubes on a signed distance grid (docs/design/CLI_MCP_UPDATE.md section 1,
    /// owner decision Q1: LinearEdges as an option, enclosure_claim "none"), or, opt-in with method mesh_offset, by gs.MeshOffset
    /// (docs/design/MESH_OFFSET.md section 10: a one-sided band certificate, enclosure_claim "band"; coordinator decision 2026-11-13,
    /// the default stays sdf_mc).
    /// </summary>
    public static partial class Commands
    {
        const long OffsetDefaultMaxCells = 16000000, OffsetMaxCellsLimit = 400000000;

        static Command Offset()
        {
            var c = new Command() {
                Name = "offset",
                Description = "Offset surface of a closed mesh at a signed distance (positive outward, negative inward): MeshSignedDistanceGrid on a regular grid, then MarchingCubes at that " +
                    "iso value (the example of docs/examples/OffsetSurfaceExample.cs). The grid cell is cell_size (mesh units) or relative_cell_size (fraction of the bounding box diagonal, default 0.01); " +
                    "the result is as fine as the cell, and features thinner than the cell vanish. root_mode: how vertices are placed along cell edges; linear_edges is one linear interpolation of the " +
                    "original corner samples without the endpoint nudges (the mode of the conservative marching cubes audit): it requires finite samples, no sample exactly at the iso value and crossings " +
                    "strictly inside their edges and never repairs a violation (the command then fails with operation_failed and offset_status linear_edges_precondition, and writes nothing). " +
                    "The result states enclosure_claim \"none\": this command promises nothing about the offset surface enclosing the input; the signed distance of a mesh is sampled in float and is " +
                    "not the exact 1-Lipschitz field the enclosure recipe (docs/design/CONSERVATIVE_MC_AUDIT.md) assumes. Inputs that are not closed, or whose triangles face inward (negative volume), are refused (the sign comes from the inside test; a mesh with some components inside out is not detected). " +
                    "method mesh_offset (opt-in) uses MeshOffset instead: dual contouring of the exact distance field with sharp creases where offset sheets meet, the self-intersection test decided exactly " +
                    "(self_intersection_free), and a proven one-sided band (band_deviation: every point of the result has a signed distance within that of distance; band_certified: within half a cell); " +
                    "enclosure_claim is then \"band\". It needs a cell of at most half of |distance| (cell_size or relative_cell_size as for sdf_mc), max_cells bounds its grid nodes, root_mode other than " +
                    "single_lerp and root_steps are refused, and the input must also be free of self-intersections (not checked; the inside test assumes it). " +
                    "Its result reports band_deviation, band_certified, uncertified_triangles and self_intersection_free (with a warning when one of them is not clean; " +
                    "enclosure_claim is \"none\" if the band bound is not finite) instead of the sdf_mc fields cells, grid_cells, root_mode and root_steps; both report method.",
            };
            c.Params.Add(Input());
            c.Params.Add(Output());
            c.Params.Add(new Param("method", ParamType.String, "sdf_mc: signed distance grid + marching cubes (default); mesh_offset: MeshOffset (exact distance field, dual contouring, band certificate)") { Default = "sdf_mc", Enum = new[] { "sdf_mc", "mesh_offset" } });
            c.Params.Add(new Param("distance", ParamType.Number, "offset distance in mesh units, positive outward, negative inward") { Required = true });
            c.Params.Add(new Param("cell_size", ParamType.Number, "grid cell size in mesh units"));
            c.Params.Add(new Param("relative_cell_size", ParamType.Number, "grid cell size as a fraction of the bounding box diagonal of the input (default 0.01)"));
            c.Params.Add(new Param("root_mode", ParamType.String, "how vertices are placed along cell edges") { Default = "single_lerp", Enum = new[] { "single_lerp", "lerp_steps", "bisection", "linear_edges" } });
            c.Params.Add(new Param("root_steps", ParamType.Integer, "lerp_steps and bisection: number of steps, 1..64 (library default 5)"));
            c.Params.Add(new Param("max_cells", ParamType.Integer, "refuse a grid with more cells than this, counted for the marching cubes grid and the signed distance grid, which is wider by 4 times the band of the distance (default 16 million cells, 64 MB of float samples), at most 400 million; with method mesh_offset: refuse more grid nodes than this " +
                "(about 17 bytes each: 16 million nodes are about 270 MB; MeshOffset's own limit of 2^28 nodes applies above)"));
            c.Params.Add(new Param("parallel", ParamType.Boolean, "use all cores (the result does not depend on it)") { Default = true });
            c.Params.Add(SelfIntersections());
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                double distance = args.Number("distance");
                if (!double.IsFinite(distance))
                    throw new CommandException(ResultCodes.InvalidArguments, "'distance' must be a finite number");
                if (args.Has("cell_size") && args.Has("relative_cell_size"))
                    throw new CommandException(ResultCodes.InvalidArguments, "give cell_size or relative_cell_size, not both");
                if (args.Has("cell_size") && !(args.Number("cell_size") > 0 && double.IsFinite(args.Number("cell_size"))))
                    throw new CommandException(ResultCodes.InvalidArguments, "'cell_size' must be a finite number > 0");
                if (args.Has("relative_cell_size") && !(args.Number("relative_cell_size") > 0 && args.Number("relative_cell_size") <= 1))
                    throw new CommandException(ResultCodes.InvalidArguments, "'relative_cell_size' must be in (0, 1]");
                string rootMode = args.String("root_mode");
                bool meshOffset = args.String("method") == "mesh_offset";
                if (meshOffset && (rootMode != "single_lerp" || args.Has("root_steps")))
                    throw new CommandException(ResultCodes.InvalidArguments, "root_mode and root_steps are options of method sdf_mc (mesh_offset places its vertices by dual contouring)");
                if (args.Has("root_steps")) {
                    if (rootMode != "lerp_steps" && rootMode != "bisection")
                        throw new CommandException(ResultCodes.InvalidArguments, "'root_steps' is an option of root_mode lerp_steps and bisection");
                    if (args.Integer("root_steps") < 1 || args.Integer("root_steps") > 64)
                        throw new CommandException(ResultCodes.InvalidArguments, "'root_steps' must be in 1..64");
                }
                long maxCells = OffsetDefaultMaxCells;
                if (args.Has("max_cells")) {
                    maxCells = args.Integer("max_cells");
                    if (maxCells < 1 || maxCells > OffsetMaxCellsLimit)
                        throw new CommandException(ResultCodes.InvalidArguments, $"'max_cells' must be in 1..{OffsetMaxCellsLimit}");
                }
                var warnings = new List<string>();
                DMesh3 input = MeshFiles.Read(args.String("input"), warnings);
                if (MeshIterators.BoundaryEdges(input).Any())
                    throw new CommandException(ResultCodes.OperationFailed, "offset needs a closed mesh (it has boundary edges): the sign of the distance comes from the inside test; repair it first");
                bool selfInt = args.Bool("self_intersections");
                JsonObject inputMetrics = MeshReport.Metrics(input, selfInt);
                if (inputMetrics["volume"] != null && inputMetrics["volume"].GetValue<double>() < 0)
                    throw new CommandException(ResultCodes.OperationFailed, "the closed input has a negative volume (its triangles face inward): the inside test would take the outside for the inside; reorient it first (repair, or convert after flipping)",
                        new JsonObject() { ["offset_status"] = "inside_out_input", ["enclosure_claim"] = "none" });
                AxisAlignedBox3d box = input.CachedBounds;
                double diagonal = box.Diagonal.Length;
                if (!(diagonal > 0) || !double.IsFinite(diagonal))
                    throw new CommandException(ResultCodes.OperationFailed, "the input has no extent (or non-finite coordinates)");
                double cell = args.Has("cell_size") ? args.Number("cell_size") : (args.Has("relative_cell_size") ? args.Number("relative_cell_size") : 0.01) * diagonal;
                if (meshOffset)
                    return OffsetByMeshOffset(args, input, inputMetrics, distance, cell, maxCells, selfInt, warnings);
                // the grid covers the input, the offset and two cells of margin (as docs/examples/OffsetSurfaceExample.cs)
                double margin = Math.Max(distance, 0) + 2 * cell;
                var gridBox = new AxisAlignedBox3d(box.Min - margin * Vector3d.One, box.Max + margin * Vector3d.One);
                if (!(Math.Abs(distance) / cell < 1e6) || !(cell >= diagonal * 1e-9))
                    throw new CommandException(ResultCodes.InvalidArguments, "the offset distance is more than a million cells, or the cell is below 1e-9 of the diagonal");
                int band = (int)Math.Ceiling(Math.Abs(distance) / cell) + 2;
                // the cell budget counts both grids: the marching cubes grid, and the signed distance grid, which MeshSignedDistanceGrid pads by 2 * band cells on each side
                double cells = (gridBox.Width / cell + 1) * (gridBox.Height / cell + 1) * (gridBox.Depth / cell + 1);
                double sdfCells = (box.Width / cell + 4.0 * band + 1) * (box.Height / cell + 4.0 * band + 1) * (box.Depth / cell + 4.0 * band + 1);
                if (!(cells <= maxCells) || !(sdfCells <= maxCells))
                    throw new CommandException(ResultCodes.InvalidArguments,
                        $"the grids would have {cells:G4} (marching cubes) and {sdfCells:G4} (signed distance, 4 bytes each) cells (cell size {cell:G4}, distance {distance:G4}); " +
                        $"at most {maxCells} (max_cells): use a larger cell_size or relative_cell_size, or a smaller distance");

                bool parallel = args.Bool("parallel");
                var sdf = new MeshSignedDistanceGrid(input, cell) { ExactBandWidth = band, UseParallel = parallel };
                try {
                    sdf.Compute();
                } catch (ArgumentException e) {
                    throw new CommandException(ResultCodes.InvalidArguments, "the signed distance grid does not fit: " + e.Message);
                } catch (OutOfMemoryException) {
                    throw new CommandException(ResultCodes.OperationFailed, "out of memory for the signed distance grid: use a larger cell_size or a smaller distance");
                }
                var implicitF = new DenseGridTrilinearImplicit(sdf.Grid, sdf.GridOriginD, sdf.CellSize);
                var mc = new MarchingCubes() { Implicit = implicitF, IsoValue = distance, Bounds = gridBox, CubeSize = cell, ParallelCompute = parallel };
                switch (rootMode) {
                    case "lerp_steps": mc.RootMode = MarchingCubes.RootfindingModes.LerpSteps; break;
                    case "bisection": mc.RootMode = MarchingCubes.RootfindingModes.Bisection; break;
                    case "linear_edges": mc.RootMode = MarchingCubes.RootfindingModes.LinearEdges; break;
                    default: mc.RootMode = MarchingCubes.RootfindingModes.SingleLerp; break;
                }
                if (args.Has("root_steps")) mc.RootModeSteps = args.Integer("root_steps");
                try {
                    mc.Generate();
                } catch (Exception e) when (rootMode == "linear_edges" && (e is InvalidOperationException || e is ArgumentException)) {
                    // (LinearEdges never repairs a violation: the partial mesh must not be used)
                    throw new CommandException(ResultCodes.OperationFailed, "linear_edges: a precondition is violated: " + e.Message,
                        new JsonObject() { ["offset_status"] = "linear_edges_precondition", ["enclosure_claim"] = "none" });
                }
                DMesh3 result = mc.Mesh;
                if (result == null || result.TriangleCount == 0)
                    throw new CommandException(ResultCodes.OperationFailed, "the offset surface is empty (a negative distance beyond the inner half thickness, or a cell larger than the shape)",
                        new JsonObject() { ["offset_status"] = "empty", ["enclosure_claim"] = "none" });
                MeshFiles.Write(args.String("output"), result, args.Bool("binary"));
                var o = new JsonObject() {
                    ["input"] = args.String("input"), ["output"] = args.String("output"), ["offset_status"] = "ok", ["method"] = "sdf_mc",
                    ["distance"] = distance, ["cell_size"] = cell, ["cells"] = new JsonArray(mc.CellDimensions.x, mc.CellDimensions.y, mc.CellDimensions.z),
                    ["grid_cells"] = (long)mc.CellDimensions.x * mc.CellDimensions.y * mc.CellDimensions.z,
                    ["root_mode"] = rootMode, ["root_steps"] = (rootMode == "lerp_steps" || rootMode == "bisection") ? mc.RootModeSteps : (JsonNode)null,
                    ["enclosure_claim"] = "none",
                };
                o["input_mesh"] = inputMetrics;
                o["mesh"] = MeshReport.Metrics(result, selfInt);
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }

        // method mesh_offset: gs.MeshOffset with the command's cell and node budget
        static JsonObject OffsetByMeshOffset(Args args, DMesh3 input, JsonObject inputMetrics, double distance, double cell, long maxCells, bool selfInt, List<string> warnings)
        {
            if (distance == 0)
                throw new CommandException(ResultCodes.InvalidArguments, "method mesh_offset needs a nonzero distance");
            if (!(cell <= 0.5 * Math.Abs(distance)))
                throw new CommandException(ResultCodes.InvalidArguments,
                    $"method mesh_offset needs a cell of at most half of |distance| (cell {cell:G4}, distance {distance:G4}): use a smaller cell_size or relative_cell_size");
            long maxNodes = Math.Min(maxCells, 1L << 28);   // MeshOffset refuses grids above 2^28 nodes whatever MaxGridNodes says
            var o = new gs.MeshOffset(input, distance) { CellSize = cell, MaxGridNodes = maxNodes, UseParallel = args.Bool("parallel") };
            try {
                o.Compute();
            } catch (OutOfMemoryException) {
                throw new CommandException(ResultCodes.OperationFailed, "out of memory for the MeshOffset grid: use a larger cell_size or relative_cell_size, or a smaller max_cells",
                    new JsonObject() { ["offset_status"] = "failed", ["enclosure_claim"] = "none" });
            }
            switch (o.ResultStatus) {
                case gs.MeshOffset.Status.Success:
                    break;
                case gs.MeshOffset.Status.GridTooLarge:
                    throw new CommandException(ResultCodes.InvalidArguments, $"the grid would have more than {maxNodes} nodes (max_cells{(maxNodes < maxCells ? ", capped at MeshOffset's limit of 2^28" : "")}): use a larger cell_size or relative_cell_size",
                        new JsonObject() { ["offset_status"] = "grid_too_large", ["enclosure_claim"] = "none" });
                case gs.MeshOffset.Status.Empty:
                    throw new CommandException(ResultCodes.OperationFailed, "the offset surface is empty (a negative distance beyond the inner half thickness, or a cell larger than the shape)",
                        new JsonObject() { ["offset_status"] = "empty", ["enclosure_claim"] = "none" });
                case gs.MeshOffset.Status.InvalidParameters:
                case gs.MeshOffset.Status.InvalidInput:
                case gs.MeshOffset.Status.NotClosed:
                case gs.MeshOffset.Status.InsideOut:
                    throw new CommandException(ResultCodes.InvalidArguments, "MeshOffset refused the input or the parameters: " + o.ResultStatus,
                        new JsonObject() { ["offset_status"] = "invalid", ["enclosure_claim"] = "none" });
                case gs.MeshOffset.Status.Failed:
                    throw new CommandException(ResultCodes.OperationFailed, "MeshOffset failed: " + o.ResultStatus + " (the contour is not closed and manifold)",
                        new JsonObject() { ["offset_status"] = "failed", ["enclosure_claim"] = "none" });
                default:
                    throw new CommandException(ResultCodes.OperationFailed, "MeshOffset stopped: " + o.ResultStatus,
                        new JsonObject() { ["offset_status"] = "failed", ["enclosure_claim"] = "none" });
            }
            // the band bound is proven whenever it is finite; an infinite one (a non-finite triangle bound) claims nothing
            bool bandFinite = double.IsFinite(o.BandDeviation);
            if (!bandFinite)
                warnings.Add("the band bound is not finite (a triangle bound was not finite): enclosure_claim none");
            else if (!o.BandCertified)
                warnings.Add($"the band is not certified within half a cell: {o.UncertifiedTriangles} triangles exceed it, the proven bound is band_deviation");
            if (!o.SelfIntersectionFree)
                warnings.Add("the result has self-intersections that the repair could not remove");
            MeshFiles.Write(args.String("output"), o.Result, args.Bool("binary"));
            var r = new JsonObject() {
                ["input"] = args.String("input"), ["output"] = args.String("output"), ["offset_status"] = "ok", ["method"] = "mesh_offset",
                ["distance"] = distance, ["cell_size"] = o.UsedCellSize,
                ["band_deviation"] = MeshReport.Num(o.BandDeviation), ["band_certified"] = o.BandCertified, ["uncertified_triangles"] = o.UncertifiedTriangles,
                ["self_intersection_free"] = o.SelfIntersectionFree, ["enclosure_claim"] = bandFinite ? "band" : "none",
            };
            r["input_mesh"] = inputMetrics;
            r["mesh"] = MeshReport.Metrics(o.Result, selfInt);
            r["warnings"] = Warnings(warnings);
            return r;
        }
    }
}
