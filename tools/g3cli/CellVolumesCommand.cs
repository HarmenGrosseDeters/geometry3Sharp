// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using g3;
using gs;

namespace g3cli
{
    public static partial class Commands
    {
        static string Inv(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        static Command CellVolumes()
        {
            var c = new Command() {
                Name = "cell_volumes",
                Description = "Cut and fill volume per cell of a regular XY lattice between two terrain surfaces (TerrainCellVolumes; docs/design/TERRAIN.md 9.3): " +
                    "material removed (cut: 'after' below 'before') and added (fill) in every cell, exact up to rounding, from the overlay of both height fields " +
                    "(each a TIN mesh file; a height grid becomes one with the dgm command and --mesh). The sum over the cells equals the total of the whole footprint " +
                    "(total_cut_volume, total_fill_volume, as TerrainVolumeDifference); a part outside the lattice is reported as outside_*. Swapping the surfaces swaps cut and fill. " +
                    "Without x0, y0, nx and ny the lattice starts at the lower left corner of the bounds of both surfaces and covers them. 'output' is a CSV with one row per cell.",
            };
            c.Params.Add(new Param("before", ParamType.String, "the surface before (a TIN, a height field; " + string.Join(", ", MeshFiles.ReadFormats) + ")") { Required = true, Positional = true });
            c.Params.Add(new Param("after", ParamType.String, "the surface after (a TIN, a height field)") { Required = true, Positional = true });
            c.Params.Add(new Param("output", ParamType.String, "optional output CSV: ix, iy, x_min, y_min, cut_volume, fill_volume, net_volume, cut_area, fill_area, overlap_area per cell") { Positional = true });
            c.Params.Add(new Param("cell_size", ParamType.Number, "the cell width (x), positive") { Required = true });
            c.Params.Add(new Param("cell_size_y", ParamType.Number, "the cell height (y); default: cell_size"));
            c.Params.Add(new Param("x0", ParamType.Number, "lattice origin x (lower left corner); default: the bounds"));
            c.Params.Add(new Param("y0", ParamType.Number, "lattice origin y; default: the bounds"));
            c.Params.Add(new Param("nx", ParamType.Integer, "number of cells in x; default: covering the bounds"));
            c.Params.Add(new Param("ny", ParamType.Integer, "number of cells in y; default: covering the bounds"));
            c.Run = args => {
                double dx = args.Number("cell_size"), dy = args.Has("cell_size_y") ? args.Number("cell_size_y") : dx;
                if (!(dx > 0) || !(dy > 0) || double.IsInfinity(dx) || double.IsInfinity(dy))
                    throw new CommandException(ResultCodes.InvalidArguments, "'cell_size' and 'cell_size_y' must be positive and finite");
                if (args.Has("nx") && args.Integer("nx") < 1 || args.Has("ny") && args.Integer("ny") < 1)
                    throw new CommandException(ResultCodes.InvalidArguments, "'nx' and 'ny' must be at least 1");
                foreach (string k in new[] { "x0", "y0" })
                    if (args.Has(k) && !(Math.Abs(args.Number(k)) < double.PositiveInfinity)) throw new CommandException(ResultCodes.InvalidArguments, $"'{k}' must be finite");
                string outPath = args.String("output");
                if (outPath != null) CheckTextWritable(outPath);
                var warnings = new List<string>();
                DMesh3 before = MeshFiles.Read(args.String("before"), warnings), after = MeshFiles.Read(args.String("after"), warnings);
                AxisAlignedBox3d b = before.GetBounds(); b.Contain(after.GetBounds());
                double x0 = args.Has("x0") ? args.Number("x0") : b.Min.x, y0 = args.Has("y0") ? args.Number("y0") : b.Min.y;
                double nxd = args.Has("nx") ? args.Integer("nx") : Math.Max(1, Math.Ceiling((b.Max.x - x0) / dx)), nyd = args.Has("ny") ? args.Integer("ny") : Math.Max(1, Math.Ceiling((b.Max.y - y0) / dy));
                if (!(nxd * nyd <= TerrainCellVolumes.MaxCells)) throw new CommandException(ResultCodes.InvalidArguments, $"the lattice has too many cells ({nxd} x {nyd}, at most 2^20): enlarge cell_size");
                int nx = (int)nxd, ny = (int)nyd;
                var cv = new TerrainCellVolumes(before, after, x0, y0, dx, dy, nx, ny);
                if (cv.Compute() == false) {
                    if (cv.BeforeOverlaps > 0 || cv.AfterOverlaps > 0)
                        throw new CommandException(ResultCodes.OperationFailed, $"a surface is not a height field (triangles overlapping in XY: before {cv.BeforeOverlaps}, after {cv.AfterOverlaps} pairs)",
                            new JsonObject() { ["before_overlaps"] = cv.BeforeOverlaps, ["after_overlaps"] = cv.AfterOverlaps });
                    throw new CommandException(ResultCodes.OperationFailed, "the volumes could not be computed");
                }
                if (outPath != null) {
                    var sb = new StringBuilder("ix,iy,x_min,y_min,cut_volume,fill_volume,net_volume,cut_area,fill_area,overlap_area\n");
                    for (int j = 0; j < ny; ++j)
                        for (int i = 0; i < nx; ++i) {
                            int k = i + nx * j;
                            sb.Append(i).Append(',').Append(j).Append(',').Append(Inv(x0 + i * dx)).Append(',').Append(Inv(y0 + j * dy)).Append(',')
                              .Append(Inv(cv.CutVolume[k])).Append(',').Append(Inv(cv.FillVolume[k])).Append(',').Append(Inv(cv.FillVolume[k] - cv.CutVolume[k])).Append(',')
                              .Append(Inv(cv.CutArea[k])).Append(',').Append(Inv(cv.FillArea[k])).Append(',').Append(Inv(cv.OverlapArea[k])).Append('\n');
                        }
                    WriteText(outPath, sb.ToString());
                }
                int withData = 0;
                for (int k = 0; k < nx * ny; ++k) if (cv.OverlapArea[k] > 0) ++withData;
                return new JsonObject() {
                    ["before"] = args.String("before"), ["after"] = args.String("after"), ["output"] = outPath,
                    ["lattice"] = new JsonObject() { ["x0"] = MeshReport.Num(x0), ["y0"] = MeshReport.Num(y0), ["cell_size_x"] = MeshReport.Num(dx), ["cell_size_y"] = MeshReport.Num(dy), ["nx"] = nx, ["ny"] = ny },
                    ["cells"] = nx * ny, ["cells_with_overlap"] = withData,
                    ["cut_volume"] = MeshReport.Num(cv.CutVolume.Sum()), ["fill_volume"] = MeshReport.Num(cv.FillVolume.Sum()),
                    ["net_volume"] = MeshReport.Num(cv.FillVolume.Sum() - cv.CutVolume.Sum()),
                    ["cut_area"] = MeshReport.Num(cv.CutArea.Sum()), ["fill_area"] = MeshReport.Num(cv.FillArea.Sum()), ["overlap_area"] = MeshReport.Num(cv.OverlapArea.Sum()),
                    ["total_cut_volume"] = MeshReport.Num(cv.TotalCutVolume), ["total_fill_volume"] = MeshReport.Num(cv.TotalFillVolume), ["total_overlap_area"] = MeshReport.Num(cv.TotalOverlapArea),
                    ["outside_cut_volume"] = MeshReport.Num(cv.OutsideCutVolume), ["outside_fill_volume"] = MeshReport.Num(cv.OutsideFillVolume),
                    ["warnings"] = Warnings(warnings),
                };
            };
            return c;
        }
    }
}
