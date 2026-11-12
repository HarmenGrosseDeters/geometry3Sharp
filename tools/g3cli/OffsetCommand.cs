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
    /// owner decision Q1: LinearEdges as an option, enclosure_claim "none").
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
                    "not the exact 1-Lipschitz field the enclosure recipe (docs/design/CONSERVATIVE_MC_AUDIT.md) assumes. Inputs that are not closed are refused (the sign comes from the inside test).",
            };
            c.Params.Add(Input());
            c.Params.Add(Output());
            c.Params.Add(new Param("distance", ParamType.Number, "offset distance in mesh units, positive outward, negative inward") { Required = true });
            c.Params.Add(new Param("cell_size", ParamType.Number, "grid cell size in mesh units"));
            c.Params.Add(new Param("relative_cell_size", ParamType.Number, "grid cell size as a fraction of the bounding box diagonal of the input (default 0.01)"));
            c.Params.Add(new Param("root_mode", ParamType.String, "how vertices are placed along cell edges") { Default = "single_lerp", Enum = new[] { "single_lerp", "lerp_steps", "bisection", "linear_edges" } });
            c.Params.Add(new Param("root_steps", ParamType.Integer, "lerp_steps and bisection: number of steps, 1..64 (library default 5)"));
            c.Params.Add(new Param("max_cells", ParamType.Integer, "refuse a grid with more cells than this (default 16 million = 64 MB of float samples), at most 400 million"));
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
                AxisAlignedBox3d box = input.CachedBounds;
                double diagonal = box.Diagonal.Length;
                if (!(diagonal > 0) || !double.IsFinite(diagonal))
                    throw new CommandException(ResultCodes.OperationFailed, "the input has no extent (or non-finite coordinates)");
                double cell = args.Has("cell_size") ? args.Number("cell_size") : (args.Has("relative_cell_size") ? args.Number("relative_cell_size") : 0.01) * diagonal;
                // the grid covers the input, the offset and two cells of margin (as docs/examples/OffsetSurfaceExample.cs)
                double margin = Math.Max(distance, 0) + 2 * cell;
                var gridBox = new AxisAlignedBox3d(box.Min - margin * Vector3d.One, box.Max + margin * Vector3d.One);
                double cx = gridBox.Width / cell + 1, cy = gridBox.Height / cell + 1, cz = gridBox.Depth / cell + 1;
                double cells = cx * cy * cz;
                if (!(cells <= maxCells))
                    throw new CommandException(ResultCodes.InvalidArguments,
                        $"the grid would have {cells:G4} cells (cell size {cell:G4}); at most {maxCells} (max_cells): use a larger cell_size or relative_cell_size");
                if (!(Math.Abs(distance) / cell < 1e6) || !(cell >= diagonal * 1e-9))
                    throw new CommandException(ResultCodes.InvalidArguments, "the offset distance is more than a million cells, or the cell is below 1e-9 of the diagonal");
                int band = (int)Math.Ceiling(Math.Abs(distance) / cell) + 2;

                bool parallel = args.Bool("parallel");
                var sdf = new MeshSignedDistanceGrid(input, cell) { ExactBandWidth = band, UseParallel = parallel };
                sdf.Compute();
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
                bool selfInt = args.Bool("self_intersections");
                var o = new JsonObject() {
                    ["input"] = args.String("input"), ["output"] = args.String("output"), ["offset_status"] = "ok",
                    ["distance"] = distance, ["cell_size"] = cell, ["cells"] = new JsonArray(mc.CellDimensions.x, mc.CellDimensions.y, mc.CellDimensions.z),
                    ["grid_cells"] = (long)mc.CellDimensions.x * mc.CellDimensions.y * mc.CellDimensions.z,
                    ["root_mode"] = rootMode, ["root_steps"] = (rootMode == "lerp_steps" || rootMode == "bisection") ? mc.RootModeSteps : (JsonNode)null,
                    ["enclosure_claim"] = "none",
                };
                o["input_mesh"] = MeshReport.Metrics(input, selfInt);
                o["mesh"] = MeshReport.Metrics(result, selfInt);
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }
    }
}
