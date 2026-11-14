// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using g3;

namespace g3cli
{
    /// <summary>
    /// tpms: triply periodic minimal surface lattices (TpmsLattice, docs/design/TPMS_LATTICE.md and TPMS_GRADED_CELL.md): gyroid, Schwarz P and D,
    /// Neovius, I-WP and Lidinoid, as a sheet or a skeletal solid, clipped to a box or to the inside of a closed mesh.
    /// </summary>
    public static partial class Commands
    {
        const long TpmsDefaultMaxCells = 16000000, TpmsMaxCellsLimit = 400000000;

        static double[] ParseNumbers(string text, int count, string name, string form)
        {
            string[] parts = text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var values = new double[parts.Length];
            for (int i = 0; i < parts.Length; ++i)
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || double.IsNaN(values[i]) || double.IsInfinity(values[i]))
                    throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': '{parts[i]}' is not a finite number (expected {form})");
            if (values.Length != count)
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}' needs {count} numbers ({form})");
            return values;
        }

        static Command Tpms()
        {
            var c = new Command() {
                Name = "tpms",
                Description = "Triply periodic minimal surface lattice for additive manufacturing (TpmsLattice): a closed, manifold, self-intersection-free mesh of a gyroid, Schwarz P / D, " +
                    "Neovius, I-WP or Lidinoid lattice, as a sheet (|f| <= t) or a skeletal solid (f <= c, or f >= -c), in a box (box_min, box_max; default two periods) or inside a closed mesh (domain). " +
                    "The solid fraction is volume_fraction of the infinite lattice (the exact quantile of f over a period) or, for a sheet, wall_thickness (a first-order approximation: the result reports " +
                    "mean_wall_thickness). divisions is the number of marching cubes cubes per period: a wall should be at least 2 cubes thick, the result has no topology guarantee for thinner walls. " +
                    "grade: a cell size that varies linearly along an axis (TPMS_GRADED_CELL.md; min_local_cell_size is reported). enclosure: the solid contains the exact lattice (never a wall thinner than designed; box domain, constant volume fraction, a dyadic grid: cell_size / divisions and the grid origin " +
                    "exact binary fractions, eg cell_size 1 and divisions 16) at the price of a larger volume. The result is always checked: tpms_status ok only for a closed, manifold, " +
                    "oriented mesh that passed the exact self-intersection check (unless verify_self_intersections is off: self_intersection_verified false and a warning); otherwise nothing is written (invalid_arguments for options and domain, " +
                    "operation_failed for a contouring or certificate failure, with tpms_status). Reports level, realized_volume_fraction, mean_wall_thickness, iso_value_used, grid_shifts_used and the metrics of the mesh.",
            };
            c.Params.Add(Output());
            c.Params.Add(new Param("type", ParamType.String, "the surface") { Default = "gyroid", Enum = new[] { "gyroid", "schwarz_p", "schwarz_d", "neovius", "iwp", "lidinoid" } });
            c.Params.Add(new Param("style", ParamType.String, "sheet: a wall around the surface; skeletal_low: f <= c; skeletal_high: f >= -c (the other network)") { Default = "sheet", Enum = new[] { "sheet", "skeletal_low", "skeletal_high" } });
            c.Params.Add(new Param("cell_size", ParamType.Number, "the period of the lattice along each axis (default 1)") { Default = 1.0 });
            c.Params.Add(new Param("divisions", ParamType.Integer, "marching cubes cubes per period, 4..512 (default 16)") { Default = 16 });
            c.Params.Add(new Param("volume_fraction", ParamType.Number, "target solid fraction of the infinite lattice in (0, 1) (default 0.3; not with wall_thickness)"));
            c.Params.Add(new Param("wall_thickness", ParamType.Number, "sheet only: wall thickness in mesh units, replaces volume_fraction (first-order approximation)"));
            c.Params.Add(new Param("box_min", ParamType.String, "domain box minimum 'x,y,z' in the lattice frame, which moves with origin (default 0,0,0)"));
            c.Params.Add(new Param("box_max", ParamType.String, "domain box maximum 'x,y,z' (default two periods: 2 cell_size in each axis)"));
            c.Params.Add(new Param("domain", ParamType.String, "instead of the box: a closed manifold mesh file; the lattice is clipped to its inside (" + string.Join(", ", MeshFiles.ReadFormats) + ")"));
            c.Params.Add(new Param("origin", ParamType.String, "world position of the lattice phase zero 'x,y,z' (default 0,0,0)"));
            c.Params.Add(new Param("grade", ParamType.String, "graded cell size along one axis 'axis,z0,period0,z1,period1,transverse_half_extent' (axis 0, 1 or 2; the period varies linearly from period0 at z0 to period1 at z1; set cell_size to the smaller period; the box must stay within transverse_half_extent of the origin in the other two axes; not with enclosure)"));
            c.Params.Add(new Param("enclosure", ParamType.Boolean, "the solid contains the exact lattice (needs a box domain, no wall_thickness and a dyadic grid)") { Default = false });
            c.Params.Add(new Param("verify_self_intersections", ParamType.Boolean, "run the exact self-intersection check inside the operation (cost O(T log T))") { Default = true });
            c.Params.Add(new Param("max_cells", ParamType.Integer, $"refuse a grid with more nodes than this, 1..{TpmsMaxCellsLimit} (default {TpmsDefaultMaxCells})") { Default = (int)TpmsDefaultMaxCells });
            c.Params.Add(new Param("parallel", ParamType.Boolean, "evaluate the field in parallel slabs (the same result)") { Default = true });
            c.Params.Add(SelfIntersections());
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                var warnings = new List<string>();
                var lattice = new TpmsLattice();
                string type = args.Has("type") ? args.String("type") : "gyroid";
                string style = args.Has("style") ? args.String("style") : "sheet";
                switch (type) {
                    case "schwarz_p": lattice.Type = TpmsType.SchwarzP; break;
                    case "schwarz_d": lattice.Type = TpmsType.SchwarzD; break;
                    case "neovius": lattice.Type = TpmsType.Neovius; break;
                    case "iwp": lattice.Type = TpmsType.IWP; break;
                    case "lidinoid": lattice.Type = TpmsType.Lidinoid; break;
                    default: lattice.Type = TpmsType.Gyroid; break;
                }
                switch (style) {
                    case "skeletal_low": lattice.Style = TpmsStyle.SkeletalLow; break;
                    case "skeletal_high": lattice.Style = TpmsStyle.SkeletalHigh; break;
                    default: lattice.Style = TpmsStyle.Sheet; break;
                }
                lattice.CellSize = args.Has("cell_size") ? Positive(args, "cell_size") : 1.0;
                if (args.Has("divisions")) {
                    int d = args.Integer("divisions");
                    if (d < 4 || d > 512) throw new CommandException(ResultCodes.InvalidArguments, "'divisions' must be in 4..512");
                    lattice.Divisions = d;
                }
                if (args.Has("volume_fraction")) {
                    double f = args.Number("volume_fraction");
                    if (!(f > 0 && f < 1)) throw new CommandException(ResultCodes.InvalidArguments, "'volume_fraction' must be in (0, 1)");
                    lattice.VolumeFraction = f;
                }
                if (args.Has("wall_thickness")) {
                    if (args.Has("volume_fraction")) throw new CommandException(ResultCodes.InvalidArguments, "give volume_fraction or wall_thickness, not both");
                    lattice.WallThickness = Positive(args, "wall_thickness");
                }
                long maxCells = TpmsDefaultMaxCells;
                if (args.Has("max_cells")) {
                    maxCells = args.Integer("max_cells");
                    if (maxCells < 1 || maxCells > TpmsMaxCellsLimit)
                        throw new CommandException(ResultCodes.InvalidArguments, $"'max_cells' must be in 1..{TpmsMaxCellsLimit}");
                }
                lattice.MaxCells = maxCells;
                if (args.Has("domain") && (args.Has("box_min") || args.Has("box_max")))
                    throw new CommandException(ResultCodes.InvalidArguments, "give a domain mesh or box_min / box_max, not both");
                var origin = Vector3d.Zero;
                if (args.Has("origin")) {
                    double[] o = ParseNumbers(args.String("origin"), 3, "origin", "'x,y,z'");
                    origin = new Vector3d(o[0], o[1], o[2]);
                }
                lattice.Origin = origin;
                if (args.Has("domain")) {
                    lattice.DomainMesh = MeshFiles.Read(args.String("domain"), warnings);
                } else {
                    var min = Vector3d.Zero;
                    var max = Vector3d.One * (2 * lattice.CellSize);
                    if (args.Has("box_min")) { double[] v = ParseNumbers(args.String("box_min"), 3, "box_min", "'x,y,z'"); min = new Vector3d(v[0], v[1], v[2]); }
                    if (args.Has("box_max")) { double[] v = ParseNumbers(args.String("box_max"), 3, "box_max", "'x,y,z'"); max = new Vector3d(v[0], v[1], v[2]); }
                    if (!(max.x > min.x && max.y > min.y && max.z > min.z))
                        throw new CommandException(ResultCodes.InvalidArguments, "box_max must be above box_min in every axis");
                    lattice.Box = new AxisAlignedBox3d(min, max);
                }
                if (args.Has("grade")) {
                    double[] g = ParseNumbers(args.String("grade"), 6, "grade", "'axis,z0,period0,z1,period1,transverse_half_extent'");
                    if (g[0] != 0 && g[0] != 1 && g[0] != 2) throw new CommandException(ResultCodes.InvalidArguments, "'grade': the axis must be 0, 1 or 2");
                    try { lattice.Grade = TpmsLattice.LinearGrade((int)g[0], g[1], g[2], g[3], g[4], g[5]); }
                    catch (ArgumentException e) { throw new CommandException(ResultCodes.InvalidArguments, "'grade': " + e.Message); }
                }
                lattice.Enclosure = args.Bool("enclosure");
                lattice.VerifySelfIntersections = args.Has("verify_self_intersections") ? args.Bool("verify_self_intersections") : true;
                lattice.ParallelCompute = args.Has("parallel") ? args.Bool("parallel") : true;

                try { lattice.Compute(); }
                catch (OutOfMemoryException) {
                    throw new CommandException(ResultCodes.OperationFailed, "out of memory for the grid (8 bytes per node): lower max_cells or divisions, or use a larger cell_size");
                }
                string status = ToSnake(lattice.Status.ToString());
                var failure = new JsonObject() { ["tpms_status"] = status };
                switch (lattice.Status) {
                    case TpmsStatus.Ok:
                        break;
                    case TpmsStatus.InvalidOptions: case TpmsStatus.InvalidDomain: case TpmsStatus.TooLarge:
                        throw new CommandException(ResultCodes.InvalidArguments, "tpms: " + lattice.Message, failure);
                    default:
                        throw new CommandException(ResultCodes.OperationFailed, "tpms failed (" + status + "): " + lattice.Message, failure);
                }
                MeshFiles.Write(args.String("output"), lattice.Mesh, args.Bool("binary"));
                var o2 = new JsonObject() { ["output"] = args.String("output") };
                o2["tpms_status"] = status;
                o2["type"] = type;
                o2["style"] = style;
                o2["level"] = MeshReport.Num(lattice.Level);
                o2["realized_volume_fraction"] = MeshReport.Num(lattice.RealizedVolumeFraction);
                o2["mean_wall_thickness"] = MeshReport.Num(lattice.MeanWallThickness);
                o2["iso_value_used"] = MeshReport.Num(lattice.IsoValueUsed);
                o2["grid_shifts_used"] = lattice.GridShiftsUsed;
                o2["grid_nodes"] = lattice.CellsEvaluated;
                o2["enclosure"] = lattice.Enclosure;
                o2["self_intersection_verified"] = lattice.VerifySelfIntersections;
                if (args.Has("grade")) o2["min_local_cell_size"] = MeshReport.Num(lattice.MinLocalCellSize);
                if (!lattice.VerifySelfIntersections) warnings.Add("verify_self_intersections is off: the mesh is closed and manifold, but was not checked for self-intersections");
                o2["mesh"] = MeshReport.Metrics(lattice.Mesh, args.Bool("self_intersections"));
                o2["warnings"] = Warnings(warnings);
                return o2;
            };
            return c;
        }
    }
}
