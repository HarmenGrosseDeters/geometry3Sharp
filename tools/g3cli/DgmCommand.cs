// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using g3;
using gs;

namespace g3cli
{
    /// <summary>
    /// Breaklines for "dgm_tin" as GeoJSON (RFC 7946): a FeatureCollection, a Feature or a geometry; LineString and MultiLineString
    /// are breaklines, a Polygon contributes its rings as closed breaklines. Positions are [x, y] or [x, y, z] in the coordinates of
    /// the grid (taken as given, no reprojection); z is needed (and used) only with breakline_heights input.
    /// </summary>
    public static class DgmBreaklinesGeoJson
    {
        public const string FormatDescription =
            "GeoJSON (RFC 7946): a FeatureCollection, a Feature or a geometry; LineString and MultiLineString are breaklines, a Polygon " +
            "contributes its rings as closed breaklines; positions [x, y] or [x, y, z] in the coordinates of the grid (no reprojection)";

        public static List<TerrainBreakline> Parse(string json, string code, string what, bool heightsFromInput)
        {
            JsonDocument doc;
            try {
                doc = JsonDocument.Parse(json, new JsonDocumentOptions() { MaxDepth = 32 });
            } catch (JsonException e) {
                throw new CommandException(code, what + " is not valid JSON: " + e.Message);
            }
            using (doc) {
                var lines = new List<TerrainBreakline>();
                Node(doc.RootElement, lines, code, what, heightsFromInput, 0);
                if (lines.Count == 0) throw new CommandException(code, what + " has no breakline (LineString, MultiLineString or Polygon)");
                return lines;
            }
        }

        public static List<TerrainBreakline> ReadFile(string path, bool heightsFromInput)
        {
            if (File.Exists(path) == false) throw new CommandException(ResultCodes.ReadError, $"file not found: {path}");
            string text;
            try {
                text = File.ReadAllText(path);
            } catch (Exception e) {
                throw new CommandException(ResultCodes.ReadError, $"cannot read '{path}': {e.Message}");
            }
            return Parse(text, ResultCodes.ReadError, "'" + path + "'", heightsFromInput);
        }

        static void Node(JsonElement e, List<TerrainBreakline> lines, string code, string what, bool fromInput, int depth)
        {
            if (e.ValueKind != JsonValueKind.Object || e.TryGetProperty("type", out JsonElement t) == false || t.ValueKind != JsonValueKind.String)
                throw new CommandException(code, what + ": every object needs a \"type\" string");
            switch (t.GetString()) {
                case "FeatureCollection":
                    if (e.TryGetProperty("features", out JsonElement fs) == false || fs.ValueKind != JsonValueKind.Array)
                        throw new CommandException(code, what + ": the FeatureCollection needs a \"features\" array");
                    foreach (JsonElement f in fs.EnumerateArray()) Node(f, lines, code, what, fromInput, depth + 1);
                    break;
                case "Feature":
                    if (e.TryGetProperty("geometry", out JsonElement g) == false || g.ValueKind != JsonValueKind.Object)
                        throw new CommandException(code, what + ": a Feature needs a \"geometry\" object");
                    Node(g, lines, code, what, fromInput, depth + 1);
                    break;
                case "LineString":
                    lines.Add(Line(Coordinates(e, code, what), code, what, fromInput));
                    break;
                case "MultiLineString":
                case "Polygon":
                    JsonElement c = Coordinates(e, code, what);
                    foreach (JsonElement part in c.EnumerateArray()) lines.Add(Line(part, code, what, fromInput));
                    break;
                default:
                    throw new CommandException(code, what + ": geometry type '" + t.GetString() + "' is not a breakline (LineString, MultiLineString, Polygon)");
            }
        }

        static JsonElement Coordinates(JsonElement e, string code, string what)
        {
            if (e.TryGetProperty("coordinates", out JsonElement c) == false || c.ValueKind != JsonValueKind.Array)
                throw new CommandException(code, what + ": a geometry needs a \"coordinates\" array");
            return c;
        }

        static TerrainBreakline Line(JsonElement positions, string code, string what, bool fromInput)
        {
            if (positions.ValueKind != JsonValueKind.Array || positions.GetArrayLength() < 2)
                throw new CommandException(code, what + ": a breakline needs at least 2 positions");
            var pts = new List<Vector3d>();
            foreach (JsonElement p in positions.EnumerateArray()) {
                int n = p.ValueKind == JsonValueKind.Array ? p.GetArrayLength() : 0;
                if (n < 2) throw new CommandException(code, what + ": a position must be [x, y] or [x, y, z]");
                var v = new double[3];
                for (int k = 0; k < Math.Min(n, 3); ++k)
                    if (p[k].ValueKind != JsonValueKind.Number || p[k].TryGetDouble(out v[k]) == false || double.IsFinite(v[k]) == false)
                        throw new CommandException(code, what + ": a position has a coordinate that is not a finite number");
                if (fromInput && n < 3) throw new CommandException(code, what + ": breakline_heights input needs [x, y, z] positions");
                pts.Add(new Vector3d(v[0], v[1], v[2]));
            }
            return new TerrainBreakline(pts, fromInput ? TerrainBreaklineHeights.FromInput : TerrainBreaklineHeights.FromGrid);
        }
    }

    public static partial class Commands
    {
        static readonly string[] DgmFormats = { "auto", "xyz", "esri" };

        static Param DgmInput() =>
            new Param("input", ParamType.String, "height grid file (DGM): XYZ ASCII (x y z per line, regular square lattice, gaps allowed; .xyz, .txt, .csv) or ESRI ASCII Grid (.asc)") { Required = true, Positional = true };
        static Param DgmFormat() =>
            new Param("format", ParamType.String, "input format: auto (.asc is esri, anything else xyz), xyz or esri") { Default = "auto", Enum = DgmFormats };
        static Param DgmNoData() =>
            new Param("no_data", ParamType.Number, "a height equal to this is no data (ESRI ASCII: used if the header has no NODATA_value)");

        static HeightGrid ReadDgm(Args args)
        {
            string path = args.String("input"), format = args.String("format");
            if (format == "auto") format = Path.GetExtension(path).Equals(".asc", StringComparison.OrdinalIgnoreCase) ? "esri" : "xyz";
            if (File.Exists(path) == false) throw new CommandException(ResultCodes.ReadError, $"file not found: {path}");
            var options = new HeightGridReadOptions();
            if (args.Has("no_data")) options.NoDataValue = args.Number("no_data");
            HeightGridReadResult r = format == "esri" ? HeightGridReader.ReadEsriAsciiFile(path, options) : HeightGridReader.ReadXyzFile(path, options);
            if (r.Ok == false)
                throw new CommandException(ResultCodes.ReadError, $"cannot read '{path}' as {format}: {r.Status}: {r.Message}",
                    new JsonObject() { ["grid_status"] = Snake(r.Status.ToString()) });
            return r.Grid;
        }

        static JsonObject DgmReport(HeightGrid g)
        {
            double zmin = double.PositiveInfinity, zmax = double.NegativeInfinity;
            int valid = 0;
            foreach (double z in g.Z)
                if (HeightGrid.IsData(z)) { ++valid; zmin = Math.Min(zmin, z); zmax = Math.Max(zmax, z); }
            long cells = 0;
            for (int j = 0; j + 1 < g.NY; ++j)
                for (int i = 0; i + 1 < g.NX; ++i)
                    if (HeightGrid.IsData(g[i, j]) && HeightGrid.IsData(g[i + 1, j]) && HeightGrid.IsData(g[i, j + 1]) && HeightGrid.IsData(g[i + 1, j + 1])) ++cells;
            var o = new JsonObject() {
                ["grid_cells_with_data"] = cells, ["grid_triangles"] = 2 * cells,
                ["nx"] = g.NX, ["ny"] = g.NY, ["cell_size"] = MeshReport.Num(g.CellSize),
                ["x0"] = MeshReport.Num(g.X0), ["y0"] = MeshReport.Num(g.Y0), ["x1"] = MeshReport.Num(g.X1), ["y1"] = MeshReport.Num(g.Y1),
                ["nodes"] = (long)g.NX * g.NY, ["valid_nodes"] = valid, ["no_data_nodes"] = (long)g.NX * g.NY - valid,
            };
            if (valid > 0) { o["z_min"] = MeshReport.Num(zmin); o["z_max"] = MeshReport.Num(zmax); }
            return o;
        }

        // true if the first or last row or column holds no data at all (an XYZ file then loses the extent)
        static bool BorderHasNoData(HeightGrid g)
        {
            bool row0 = false, row1 = false, col0 = false, col1 = false;
            for (int i = 0; i < g.NX; ++i) { row0 |= HeightGrid.IsData(g[i, 0]); row1 |= HeightGrid.IsData(g[i, g.NY - 1]); }
            for (int j = 0; j < g.NY; ++j) { col0 |= HeightGrid.IsData(g[0, j]); col1 |= HeightGrid.IsData(g[g.NX - 1, j]); }
            return !(row0 && row1 && col0 && col1);
        }

        static Command Dgm()
        {
            var c = new Command() {
                Name = "dgm",
                Description = "Read a height grid (DGM / DTM raster: XYZ ASCII or ESRI ASCII Grid), report it (size, cell size, extent, nodes with data, " +
                    "height range) and optionally write it again as XYZ or ESRI ASCII Grid ('output') and as the grid TIN, two triangles per cell, cells with a " +
                    "no-data corner left out ('mesh'; " + string.Join(", ", MeshFiles.WriteFormats) + "). Heights are node values; XYZ nodes without a point are no data.",
            };
            c.Params.Add(DgmInput());
            c.Params.Add(new Param("output", ParamType.String, "optional output height grid; .asc is written as ESRI ASCII Grid, anything else as XYZ (unless output_format)") { Positional = true });
            c.Params.Add(DgmFormat());
            c.Params.Add(DgmNoData());
            c.Params.Add(new Param("output_format", ParamType.String, "format of 'output': auto (by extension), xyz or esri") { Default = "auto", Enum = DgmFormats });
            c.Params.Add(new Param("output_no_data", ParamType.Number, "NODATA_value written to an ESRI ASCII Grid") { Default = -9999.0 });
            c.Params.Add(new Param("mesh", ParamType.String, "optional output mesh file of the grid TIN (HeightGrid.BuildMesh)"));
            c.Params.Add(Binary());
            c.Run = args => {
                if (args.Has("mesh")) MeshFiles.CheckWritable(args.String("mesh"));
                HeightGrid g = ReadDgm(args);
                var o = new JsonObject() { ["input"] = args.String("input") };
                JsonObject report = DgmReport(g);
                foreach (var kv in report.ToList()) { report.Remove(kv.Key); o[kv.Key] = kv.Value; }
                var warnings = new List<string>();
                // everything that can fail is checked before a file is written: the mesh, the output path, the serialized grid
                DMesh3 mesh = null;
                if (args.Has("mesh")) {
                    mesh = g.BuildMesh();
                    if (mesh.TriangleCount == 0) throw new CommandException(ResultCodes.EmptyMesh, "the grid has no cell with data");
                }
                string text = null, outPath = null, fmt = null;
                if (args.Has("output")) {
                    outPath = args.String("output");
                    fmt = args.String("output_format");
                    if (fmt == "auto") fmt = Path.GetExtension(outPath).Equals(".asc", StringComparison.OrdinalIgnoreCase) ? "esri" : "xyz";
                    string full, dir;
                    try {
                        full = Path.GetFullPath(outPath); dir = Path.GetDirectoryName(full);
                    } catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) {
                        throw new CommandException(ResultCodes.InvalidArguments, $"'output' is not a valid path: {e.Message}");
                    }
                    if (string.Equals(full, Path.GetFullPath(args.String("input")), StringComparison.Ordinal))
                        throw new CommandException(ResultCodes.InvalidArguments, "'output' is the input file");
                    if (Directory.Exists(dir) == false) throw new CommandException(ResultCodes.WriteError, $"cannot write '{outPath}': directory does not exist");
                    try {
                        var sw = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
                        if (fmt == "esri") HeightGridWriter.WriteEsriAscii(sw, g, args.Number("output_no_data"));
                        else HeightGridWriter.WriteXyz(sw, g);
                        text = sw.ToString();
                    } catch (ArgumentException e) {
                        throw new CommandException(ResultCodes.InvalidArguments, "cannot write the grid: " + e.Message);
                    }
                    if (fmt == "xyz" && BorderHasNoData(g))
                        warnings.Add("xyz_border_no_data: a border row or column of the grid has no data, so the XYZ file (which has no header) reads back as a smaller grid; write ESRI ASCII Grid to keep the extent");
                }
                if (text != null) {
                    try {
                        File.WriteAllText(outPath, text, new System.Text.UTF8Encoding(false));
                    } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                        throw new CommandException(ResultCodes.WriteError, $"cannot write '{outPath}': {e.Message}");
                    }
                    o["output"] = outPath; o["output_format"] = fmt;
                }
                if (mesh != null) {
                    MeshFiles.Write(args.String("mesh"), mesh, args.Bool("binary"));
                    o["mesh_file"] = args.String("mesh");
                    o["mesh"] = MeshReport.Metrics(mesh, false);
                }
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }

        static Command DgmTin()
        {
            var c = new Command() {
                Name = "dgm_tin",
                Description = "TIN of a height grid (DGM) with a guaranteed vertical error (TerrainGridTin; docs/design/TERRAIN_DGM_TO_TIN.md): far fewer triangles than the " +
                    "two per cell of the grid TIN, the maximum of |TIN - grid surface| over the whole TIN at most 'epsilon' (exact scan, reported as surface_error), " +
                    "breaklines kept as edge chains, cells without data left out. The TIN is a height field the slope command accepts. Breaklines: " +
                    DgmBreaklinesGeoJson.FormatDescription + ". breakline_heights grid (default) takes their heights from the grid, input from the z of the positions " +
                    "(survey lines: followed exactly, they must be within epsilon of the grid, else breakline_height_deviates).",
            };
            c.Params.Add(DgmInput());
            c.Params.Add(new Param("output", ParamType.String, "output mesh file of the TIN; the format follows the extension (" + string.Join(", ", MeshFiles.WriteFormats) + ")") { Required = true, Positional = true });
            c.Params.Add(new Param("epsilon", ParamType.Number, "the guaranteed vertical error (the unit of the heights), positive") { Required = true });
            c.Params.Add(DgmFormat());
            c.Params.Add(DgmNoData());
            c.Params.Add(new Param("breaklines", ParamType.String, "breaklines, a GeoJSON file (see the description); or give breaklines_geojson"));
            c.Params.Add(new Param("breaklines_geojson", ParamType.String, "the breaklines as inline GeoJSON text, instead of a file"));
            c.Params.Add(new Param("breakline_heights", ParamType.String, "grid: heights of the breaklines from the grid (the bound holds along them); input: from the z of the positions") { Default = "grid", Enum = new[] { "grid", "input" } });
            c.Params.Add(new Param("max_rounds", ParamType.Integer, "the most refinement rounds (a TIN usually needs 10-20)") { Default = 200 });
            c.Params.Add(Binary());
            c.Run = args => {
                double eps = args.Number("epsilon");
                if (!(eps > 0) || double.IsInfinity(eps)) throw new CommandException(ResultCodes.InvalidArguments, "'epsilon' must be positive and finite");
                if (args.Integer("max_rounds") < 1) throw new CommandException(ResultCodes.InvalidArguments, "'max_rounds' must be at least 1");
                if (args.Has("breaklines") && args.Has("breaklines_geojson"))
                    throw new CommandException(ResultCodes.InvalidArguments, "give at most one of breaklines (a GeoJSON file) and breaklines_geojson (inline GeoJSON)");
                bool fromInput = args.String("breakline_heights") == "input";
                MeshFiles.CheckWritable(args.String("output"));
                List<TerrainBreakline> lines = args.Has("breaklines_geojson") ? DgmBreaklinesGeoJson.Parse(args.String("breaklines_geojson"), ResultCodes.InvalidArguments, "breaklines_geojson", fromInput)
                    : args.Has("breaklines") ? DgmBreaklinesGeoJson.ReadFile(args.String("breaklines"), fromInput) : new List<TerrainBreakline>();
                HeightGrid g = ReadDgm(args);
                var tin = new TerrainGridTin() { Grid = g, Epsilon = eps, Breaklines = lines, MaxRounds = args.Integer("max_rounds") };
                bool ok = tin.Compute();
                var status = new JsonObject() { ["tin_status"] = Snake(tin.Status.ToString()), ["tin_message"] = tin.Message };
                if (double.IsNaN(tin.SurfaceError) == false) {
                    status["surface_error"] = MeshReport.Num(tin.SurfaceError);
                    status["node_error"] = MeshReport.Num(tin.NodeError);
                    status["breakline_error"] = MeshReport.Num(tin.BreaklineError);
                    status["rounds"] = tin.Rounds;
                }
                if (ok == false) {
                    string code = tin.Status == TerrainGridTinStatus.InvalidInput ? ResultCodes.InvalidArguments
                        : tin.Status == TerrainGridTinStatus.NoData ? ResultCodes.EmptyMesh : ResultCodes.OperationFailed;
                    throw new CommandException(code, $"dgm_tin: {tin.Status}: {tin.Message}", status);
                }
                MeshFiles.Write(args.String("output"), tin.Result, args.Bool("binary"));
                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = args.String("output"), ["epsilon"] = MeshReport.Num(eps) };
                foreach (var kv in status.ToList()) { status.Remove(kv.Key); o[kv.Key] = kv.Value; }
                o["grid"] = DgmReport(g);
                o["breaklines"] = lines.Count;
                o["breakline_edges"] = tin.BreaklineEdges.Count;
                o["dropped_breakline_segments"] = tin.DroppedBreaklineSegments;
                o["locked_creases"] = tin.LockedCreases;
                                o["mesh"] = MeshReport.Metrics(tin.Result, false);
                return o;
            };
            return c;
        }
    }
}
