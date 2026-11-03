// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using g3;
using gs;

namespace g3cli
{
    /// <summary>
    /// The toe lines of the "slope" command as GeoJSON (RFC 7946): a FeatureCollection, a Feature or a bare geometry, each
    /// geometry a LineString (an open toe) or a Polygon (a closed toe: its outer ring; no holes), with x, y, z per position.
    /// The coordinates are taken as given (projected, metres; no reprojection); a "crs" member is passed through.
    /// Failures are CommandExceptions with the code given to the parser (read_error for a file, invalid_arguments for
    /// inline JSON); never another exception.
    /// </summary>
    public static class SlopeToesGeoJson
    {
        public const string FormatDescription =
            "GeoJSON (RFC 7946): a FeatureCollection, a Feature or a geometry; each toe a LineString (open) or a Polygon (closed: " +
            "its outer ring, no holes) with [x, y, z] positions, taken as given (projected coordinates, no reprojection; a \"crs\" " +
            "member is passed through to the report). Feature properties: \"side\" (left, right, inside, outside; the slope's side " +
            "of the toe line in its direction; inside / outside for a closed toe; required), \"slope\" (cut and fill), or \"cut_slope\" " +
            "and \"fill_slope\", optional \"segment_slopes\" (one per segment, overriding \"slope\"; cut and fill alike). A slope is a " +
            "number n (1:n, n horizontal per 1 vertical) or {\"ratio\": n}, {\"percent\": p}, {\"degrees\": a}, {\"rise\": s}. " +
            "A bare geometry needs the side and slope options";

        public class Result
        {
            public List<SlopeToe> Toes = new List<SlopeToe>();
            /// <summary> the "crs" member of the root, as given (null if none) </summary>
            public JsonNode Crs;
        }

        /// <summary> Parses the JSON text; code is the result code of every failure; side and slope are the defaults for features without them </summary>
        public static Result Parse(string json, string code, string what, string side = null, SlopeSpec? slope = null)
        {
            JsonDocument doc;
            try {
                doc = JsonDocument.Parse(json, new JsonDocumentOptions() { MaxDepth = 32 });
            } catch (JsonException e) {
                throw new CommandException(code, what + " is not valid JSON: " + e.Message);
            }
            using (doc) {
                var p = new Parser() { Code = code, What = what, DefaultSide = side, DefaultSlope = slope };
                return p.Root(doc.RootElement);
            }
        }

        public static Result ReadFile(string path, string side = null, SlopeSpec? slope = null)
        {
            if (File.Exists(path) == false)
                throw new CommandException(ResultCodes.ReadError, $"file not found: {path}");
            string text;
            try {
                text = File.ReadAllText(path);
            } catch (Exception e) {
                throw new CommandException(ResultCodes.ReadError, $"cannot read '{path}': {e.Message}");
            }
            return Parse(text, ResultCodes.ReadError, "'" + path + "'", side, slope);
        }

        /// <summary> a slope given as text on the command line: n (1:n), or ratio:n, percent:p, degrees:a, rise:s </summary>
        public static SlopeSpec ParseSlopeText(string s, string what)
        {
            string kind = "ratio", v = s.Trim();
            int colon = v.IndexOf(':');
            if (colon >= 0) {
                kind = v.Substring(0, colon).Trim().ToLowerInvariant(); v = v.Substring(colon + 1).Trim();
                // (the conventional 1:n)
                if (kind == "1") kind = "ratio";
            }
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) == false || double.IsFinite(d) == false)
                throw new CommandException(ResultCodes.InvalidArguments, $"'{what}': not a number: '{s}'");
            SlopeSpec spec;
            switch (kind) {
                case "ratio": spec = SlopeSpec.Ratio(d); break;
                case "percent": spec = SlopeSpec.Percent(d); break;
                case "degrees": spec = SlopeSpec.Degrees(d); break;
                case "rise": spec = SlopeSpec.Rise(d); break;
                default: throw new CommandException(ResultCodes.InvalidArguments, $"'{what}': the kind must be ratio, percent, degrees or rise, got '{kind}'");
            }
            if (spec.IsValid == false)
                throw new CommandException(ResultCodes.InvalidArguments, $"'{what}': not a valid slope: '{s}' (positive, finite; an angle below 90 degrees)");
            return spec;
        }

        class Parser
        {
            public string Code, What;
            public string DefaultSide;
            public SlopeSpec? DefaultSlope;

            CommandException Fail(string where, string message) => new CommandException(Code, $"{What}: {where} {message}");

            // (System.Text.Json reads a repeated key without complaint; an input that says two things is refused)
            void Unique(JsonElement o, string where)
            {
                var seen = new HashSet<string>();
                foreach (JsonProperty p in o.EnumerateObject())
                    if (seen.Add(p.Name) == false) throw Fail(where, $"has the key \"{p.Name}\" twice");
            }

            public Result Root(JsonElement root)
            {
                if (root.ValueKind != JsonValueKind.Object) throw Fail("the root", "must be an object");
                Unique(root, "the root");
                var r = new Result();
                if (root.TryGetProperty("crs", out JsonElement crs)) r.Crs = JsonNode.Parse(crs.GetRawText());
                string type = Type(root, "the root");
                if (type == "FeatureCollection") {
                    if (root.TryGetProperty("features", out JsonElement fs) == false || fs.ValueKind != JsonValueKind.Array)
                        throw Fail("the FeatureCollection", "needs a \"features\" array");
                    int i = 0;
                    foreach (JsonElement f in fs.EnumerateArray()) Feature(f, $"feature {i++}", r.Toes);
                } else if (type == "Feature") {
                    Feature(root, "the feature", r.Toes);
                } else {
                    Geometry(root, "the geometry", default(JsonElement), false, r.Toes);
                }
                if (r.Toes.Count == 0) throw Fail("the input", "has no toe (a LineString or Polygon)");
                return r;
            }

            string Type(JsonElement e, string where)
            {
                if (e.TryGetProperty("type", out JsonElement t) == false || t.ValueKind != JsonValueKind.String)
                    throw Fail(where, "needs a \"type\" string");
                return t.GetString();
            }

            void Feature(JsonElement f, string where, List<SlopeToe> toes)
            {
                if (f.ValueKind != JsonValueKind.Object || Type(f, where) != "Feature") throw Fail(where, "must be a Feature");
                Unique(f, where);
                if (f.TryGetProperty("geometry", out JsonElement g) == false || g.ValueKind != JsonValueKind.Object)
                    throw Fail(where, "needs a \"geometry\" object");
                bool hasProps = f.TryGetProperty("properties", out JsonElement props) && props.ValueKind != JsonValueKind.Null;
                if (hasProps && props.ValueKind != JsonValueKind.Object) throw Fail(where, "has \"properties\" that are not an object");
                if (hasProps) Unique(props, where + " properties");
                Geometry(g, where, props, hasProps, toes);
            }

            void Geometry(JsonElement g, string where, JsonElement props, bool hasProps, List<SlopeToe> toes)
            {
                string type = Type(g, where);
                Unique(g, where + " geometry");
                if (g.TryGetProperty("coordinates", out JsonElement c) == false || c.ValueKind != JsonValueKind.Array)
                    throw Fail(where, "needs a \"coordinates\" array");
                List<Vector3d> pts;
                bool closed;
                if (type == "LineString") {
                    pts = Positions(c, where);
                    closed = false;
                    if (pts.Count < 2) throw Fail(where, "a LineString needs at least 2 positions");
                } else if (type == "Polygon") {
                    int rings = c.GetArrayLength();
                    if (rings != 1) throw Fail(where, rings == 0 ? "a Polygon needs its ring" : "a Polygon with holes is not a toe (one ring only)");
                    pts = Positions(c[0], where);
                    if (pts.Count < 4 || pts[0] != pts[pts.Count - 1]) throw Fail(where, "a Polygon ring needs at least 4 positions, the last equal to the first");
                    pts.RemoveAt(pts.Count - 1);
                    closed = true;
                } else {
                    throw Fail(where, $"has geometry type '{type}': a toe is a LineString or a Polygon");
                }
                var toe = new SlopeToe() { Points = pts, Closed = closed };
                string side = hasProps && props.TryGetProperty("side", out JsonElement s) ? (s.ValueKind == JsonValueKind.String ? s.GetString() : throw Fail(where, "\"side\" must be a string")) : DefaultSide;
                if (side == null) throw Fail(where, "needs a \"side\" (left, right, inside, outside)");
                switch (side) {
                    case "left": toe.Side = SlopeToe.Sides.Left; break;
                    case "right": toe.Side = SlopeToe.Sides.Right; break;
                    case "inside": toe.Side = SlopeToe.Sides.Inside; break;
                    case "outside": toe.Side = SlopeToe.Sides.Outside; break;
                    default: throw Fail(where, $"has side '{side}': left, right, inside or outside");
                }
                if (closed == false && (toe.Side == SlopeToe.Sides.Inside || toe.Side == SlopeToe.Sides.Outside))
                    throw Fail(where, $"is an open toe (a LineString): its side is left or right, not {side}");
                int n = toe.SegmentCount;
                SlopeSpec? slope = hasProps && props.TryGetProperty("slope", out JsonElement sl) ? Slope(sl, where + " \"slope\"") : DefaultSlope;
                SlopeSpec? cut = hasProps && props.TryGetProperty("cut_slope", out JsonElement cs) ? Slope(cs, where + " \"cut_slope\"") : slope;
                SlopeSpec? fill = hasProps && props.TryGetProperty("fill_slope", out JsonElement fsl) ? Slope(fsl, where + " \"fill_slope\"") : slope;
                SlopeSpec[] perSegment = null;
                if (hasProps && props.TryGetProperty("segment_slopes", out JsonElement ss)) {
                    if (props.TryGetProperty("slope", out _) || props.TryGetProperty("cut_slope", out _) || props.TryGetProperty("fill_slope", out _))
                        throw Fail(where, "has \"segment_slopes\" and \"slope\" / \"cut_slope\" / \"fill_slope\": give one of them");
                    if (ss.ValueKind != JsonValueKind.Array || ss.GetArrayLength() != n)
                        throw Fail(where, $"\"segment_slopes\" must be an array of {n} slopes (one per segment)");
                    perSegment = ss.EnumerateArray().Select((e, i) => Slope(e, $"{where} \"segment_slopes\"[{i}]")).ToArray();
                }
                if (perSegment == null && (cut == null || fill == null))
                    throw Fail(where, "needs a \"slope\" (or \"cut_slope\" and \"fill_slope\", or \"segment_slopes\")");
                toe.CutStart = perSegment ?? Enumerable.Repeat(cut.Value, n).ToArray();
                toe.FillStart = perSegment ?? Enumerable.Repeat(fill.Value, n).ToArray();
                toe.CutEnd = (SlopeSpec[])toe.CutStart.Clone();
                toe.FillEnd = (SlopeSpec[])toe.FillStart.Clone();
                toes.Add(toe);
            }

            List<Vector3d> Positions(JsonElement c, string where)
            {
                if (c.ValueKind != JsonValueKind.Array) throw Fail(where, "positions must be an array");
                var pts = new List<Vector3d>();
                int i = 0;
                foreach (JsonElement p in c.EnumerateArray()) {
                    if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() != 3)
                        throw Fail(where, $"position {i} must be [x, y, z] (the toe's height is needed)");
                    var v = new double[3];
                    for (int k = 0; k < 3; ++k) {
                        JsonElement e = p[k];
                        if (e.ValueKind != JsonValueKind.Number || e.TryGetDouble(out v[k]) == false || double.IsFinite(v[k]) == false)
                            throw Fail(where, $"position {i} has a coordinate that is not a finite number");
                    }
                    pts.Add(new Vector3d(v[0], v[1], v[2]));
                    i++;
                }
                return pts;
            }

            SlopeSpec Slope(JsonElement e, string where)
            {
                SlopeSpec spec;
                if (e.ValueKind == JsonValueKind.Number) {
                    if (e.TryGetDouble(out double n) == false) throw Fail(where, "is not a number");
                    spec = SlopeSpec.Ratio(n);
                } else if (e.ValueKind == JsonValueKind.Object) {
                    var props = e.EnumerateObject().ToList();
                    if (props.Count != 1 || props[0].Value.ValueKind != JsonValueKind.Number || props[0].Value.TryGetDouble(out double v) == false)
                        throw Fail(where, "must be one of {\"ratio\": n}, {\"percent\": p}, {\"degrees\": a}, {\"rise\": s}");
                    switch (props[0].Name) {
                        case "ratio": spec = SlopeSpec.Ratio(v); break;
                        case "percent": spec = SlopeSpec.Percent(v); break;
                        case "degrees": spec = SlopeSpec.Degrees(v); break;
                        case "rise": spec = SlopeSpec.Rise(v); break;
                        default: throw Fail(where, $"has kind '{props[0].Name}': ratio, percent, degrees or rise");
                    }
                } else {
                    throw Fail(where, "must be a number (1:n) or an object such as {\"degrees\": 35}");
                }
                if (spec.IsValid == false) throw Fail(where, "is not a valid slope (positive, finite; an angle below 90 degrees)");
                return spec;
            }
        }

        /// <summary> lines as a GeoJSON FeatureCollection (closed lines repeat their first position), the crs passed through </summary>
        public static string Lines(IEnumerable<(List<Vector3d> points, bool closed, JsonObject props)> lines, JsonNode crs, JsonObject extra)
        {
            var features = new JsonArray();
            foreach (var (points, closed, props) in lines) {
                var coords = new JsonArray();
                foreach (Vector3d p in points) coords.Add(new JsonArray(p.x, p.y, p.z));
                if (closed && points.Count > 0) coords.Add(new JsonArray(points[0].x, points[0].y, points[0].z));
                features.Add(new JsonObject() {
                    ["type"] = "Feature",
                    ["geometry"] = new JsonObject() { ["type"] = "LineString", ["coordinates"] = coords },
                    ["properties"] = props
                });
            }
            var root = new JsonObject() { ["type"] = "FeatureCollection" };
            if (crs != null) root["crs"] = crs.DeepClone();
            if (extra != null)
                foreach (var kv in extra.ToList()) { extra.Remove(kv.Key); root[kv.Key] = kv.Value; }
            root["features"] = features;
            return root.ToJsonString(new JsonSerializerOptions() { WriteIndented = false });
        }
    }

    public static partial class Commands
    {
        static string Snake(string camel)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < camel.Length; ++i) {
                if (char.IsUpper(camel[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(camel[i]));
            }
            return sb.ToString();
        }

        // a GeoJSON output: its directory must exist (checked before anything is written)
        static void CheckTextWritable(string path)
        {
            string dir;
            try {
                dir = Path.GetDirectoryName(Path.GetFullPath(path));
            } catch (Exception e) {
                throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': {e.Message}");
            }
            if (Directory.Exists(dir) == false)
                throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': directory does not exist");
        }

        static Command Slope()
        {
            var c = new Command() {
                Name = "slope",
                Description = "Slope design from toe lines to a terrain (TerrainSlopeDesign; docs/design/TERRAIN.md): open-pit benches, dumps, " +
                    "embankments in cut, fill or both (direction auto), never steeper than the given slopes (every output triangle checked " +
                    "exactly), the catch lines where the slope meets the terrain, the composite terrain (or only the replacement of the changed " +
                    "triangles), cut and fill volumes and areas, level lines at target heights. Terrain: a TIN mesh file (a height field). " +
                    "Toes: " + SlopeToesGeoJson.FormatDescription + ". A design that cannot be certified fails with operation_failed and its " +
                    "design_status, status_kind and message; it never returns a violating result. With local_frame the design is computed on " +
                    "the inputs translated to a local origin (finer guaranteed features at UTM coordinates): every output, the meshes and the " +
                    "GeoJSON lines, is then in the local frame, and used_origin must be added to x and y for the input's frame (translating back " +
                    "rounds by up to half an ulp and is not covered by the exact checks).",
            };
            c.Params.Add(new Param("terrain", ParamType.String, "terrain mesh file, a TIN (" + string.Join(", ", MeshFiles.ReadFormats) + ")") { Required = true, Positional = true });
            c.Params.Add(new Param("toes", ParamType.String, "toe lines, a GeoJSON file (see the description); or give toes_geojson") { Positional = true });
            c.Params.Add(new Param("output", ParamType.String, "output mesh file of the composite (the whole terrain with the slope region) or, with composite " +
                "replacement, of the replacement only; the format follows the extension (" + string.Join(", ", MeshFiles.WriteFormats) + ")") { Positional = true });
            c.Params.Add(new Param("toes_geojson", ParamType.String, "the toes as inline GeoJSON text, instead of a file"));
            c.Params.Add(new Param("tolerance", ParamType.Number, "the professional accuracy: the largest vertical deviation of the design from the ideal slope surface, in the input's units (eg 0.05 m); no default") { Required = true });
            c.Params.Add(new Param("direction", ParamType.String, "cut (down into the terrain from the toe), fill (up from it), or auto (mixed: each part where it is needed)") { Default = "cut", Enum = new[] { "cut", "fill", "auto" } });
            c.Params.Add(new Param("composite", ParamType.String, "full: the whole terrain with the slope region; replacement: only the part that replaces terrain triangles (with their ids); none: no composite (catch lines only)") { Default = "full", Enum = new[] { "full", "replacement", "none" } });
            c.Params.Add(new Param("side", ParamType.String, "the side of toes without a \"side\" property (or a bare geometry)") { Enum = new[] { "left", "right", "inside", "outside" } });
            c.Params.Add(new Param("slope", ParamType.String, "the slope of toes without one: n or 1:n (n horizontal per 1 vertical), or ratio:n, percent:p, degrees:a, rise:s"));
            c.Params.Add(new Param("height_limit", ParamType.Number, "the height up to which the slope is built (cut; down to it for fill); default: up to the terrain near the toe"));
            c.Params.Add(new Param("target_heights", ParamType.String, "comma separated heights: the level lines of the slope region at these heights (bench levels), written to target_lines"));
            c.Params.Add(new Param("slope_margin", ParamType.Number, "the relative margin below the slope (δ), raised automatically if needed") { Default = 1e-6 });
            c.Params.Add(new Param("terrain_vertical_uncertainty", ParamType.Number, "the terrain's vertical uncertainty, for the catch lines' horizontal uncertainty") { Default = 0.0 });
            c.Params.Add(new Param("required_catch_line_accuracy", ParamType.Number, "fail (catch_line_accuracy_not_reached) unless every catch-line segment is located this well horizontally"));
            c.Params.Add(new Param("local_frame", ParamType.Boolean, "compute in a local frame at the toes (finer guaranteed features at UTM); the outputs are then local: add used_origin to x and y") { Default = false });
            c.Params.Add(new Param("surface", ParamType.String, "optional output mesh file of the slope surface (all slope triangles, groups = the toe pieces)"));
            c.Params.Add(new Param("region", ParamType.String, "optional output mesh file of the slope region (the slope surface clipped at the catch lines)"));
            c.Params.Add(new Param("catch_lines", ParamType.String, "optional output GeoJSON file of the catch lines (LineStrings with their end reasons and accuracy)"));
            c.Params.Add(new Param("target_lines", ParamType.String, "optional output GeoJSON file of the target-height level lines"));
            c.Params.Add(Binary());
            c.Run = args => {
                var warnings = new List<string>();
                double tol = args.Number("tolerance");
                if (!(tol > 0)) throw new CommandException(ResultCodes.InvalidArguments, "'tolerance' must be > 0");
                if (!(args.Number("slope_margin") > 0 && args.Number("slope_margin") <= 0.01)) throw new CommandException(ResultCodes.InvalidArguments, "'slope_margin' must be in (0, 0.01]");
                if (args.Has("height_limit") && args.String("direction") == "auto")
                    throw new CommandException(ResultCodes.InvalidArguments, "'height_limit' is not for direction auto (the cut and the fill run end at the terrain)");
                foreach (string g in new[] { "catch_lines", "target_lines" })
                    if (args.Has(g)) CheckTextWritable(args.String(g));
                if (!(args.Number("terrain_vertical_uncertainty") >= 0)) throw new CommandException(ResultCodes.InvalidArguments, "'terrain_vertical_uncertainty' must be >= 0");
                if (args.Has("required_catch_line_accuracy") && !(args.Number("required_catch_line_accuracy") > 0))
                    throw new CommandException(ResultCodes.InvalidArguments, "'required_catch_line_accuracy' must be > 0");
                if (args.Has("toes") == args.Has("toes_geojson"))
                    throw new CommandException(ResultCodes.InvalidArguments, "give exactly one of toes (a GeoJSON file) and toes_geojson (inline GeoJSON)");
                string composite = args.String("composite");
                if (composite == "none" && args.Has("output"))
                    throw new CommandException(ResultCodes.InvalidArguments, "'output' needs a composite (full or replacement)");
                if (composite != "none" && args.Has("output") == false)
                    throw new CommandException(ResultCodes.InvalidArguments, "'output' is needed for the composite (or give composite none)");
                foreach (string m in new[] { "output", "surface", "region" })
                    if (args.Has(m)) MeshFiles.CheckWritable(args.String(m));
                if (args.Has("region") && composite == "none")
                    throw new CommandException(ResultCodes.InvalidArguments, "'region' needs a composite (full or replacement)");
                var heights = new List<double>();
                if (args.Has("target_heights")) {
                    foreach (string h in args.String("target_heights").Split(',')) {
                        if (double.TryParse(h.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double z) == false || double.IsFinite(z) == false)
                            throw new CommandException(ResultCodes.InvalidArguments, $"'target_heights': not a number: '{h}'");
                        heights.Add(z);
                    }
                } else if (args.Has("target_lines")) {
                    throw new CommandException(ResultCodes.InvalidArguments, "'target_lines' needs target_heights");
                }
                SlopeSpec? slope = args.Has("slope") ? SlopeToesGeoJson.ParseSlopeText(args.String("slope"), "slope") : (SlopeSpec?)null;
                var toes = args.Has("toes_geojson")
                    ? SlopeToesGeoJson.Parse(args.String("toes_geojson"), ResultCodes.InvalidArguments, "toes_geojson", args.String("side"), slope)
                    : SlopeToesGeoJson.ReadFile(args.String("toes"), args.String("side"), slope);
                DMesh3 terrain = MeshFiles.Read(args.String("terrain"), warnings);

                var d = new TerrainSlopeDesign() {
                    Toes = toes.Toes, Terrain = terrain, Tolerance = tol, SlopeMargin = args.Number("slope_margin"),
                    TerrainVerticalUncertainty = args.Number("terrain_vertical_uncertainty"),
                    BuildComposite = composite != "none",
                    CompositeKind = composite == "replacement" ? TerrainSlopeDesign.CompositeKinds.Replacement : TerrainSlopeDesign.CompositeKinds.Full,
                    Direction = args.String("direction") == "fill" ? TerrainSlopeDesign.Directions.Fill
                        : args.String("direction") == "auto" ? TerrainSlopeDesign.Directions.Auto : TerrainSlopeDesign.Directions.Cut,
                    LocalFrame = args.Bool("local_frame"),
                };
                if (args.Has("height_limit")) d.HeightLimit = args.Number("height_limit");
                if (args.Has("required_catch_line_accuracy")) d.RequiredCatchLineAccuracy = args.Number("required_catch_line_accuracy");
                if (heights.Count > 0) d.TargetHeights = heights;
                d.Compute();

                var status = new JsonObject() {
                    ["design_status"] = Snake(d.Status.ToString()),
                    ["status_kind"] = Snake(d.StatusKind.ToString()),
                    ["design_message"] = d.Message,
                };
                if (d.ToeProblems.Count > 0)
                    status["toe_problems"] = new JsonArray(d.ToeProblems.Select(p => (JsonNode)new JsonArray(p.a, p.b)).ToArray());
                if (d.ProblemTriangles.Count > 0) status["problem_triangles"] = d.ProblemTriangles.Count;
                status["attempts"] = d.Attempts;
                if (d.Status != TerrainSlopeDesign.Statuses.Success && d.Status != TerrainSlopeDesign.Statuses.NoDaylight) {
                    // (the terrain is not a height field: the terrain file; a toe problem: the toes, a file or inline; the
                    // other invalid inputs are parameters, eg a tolerance below the numerical margins)
                    string code = d.Status == TerrainSlopeDesign.Statuses.NotHeightField ? ResultCodes.ReadError
                        : d.Status == TerrainSlopeDesign.Statuses.InvalidInput ? (d.ToeProblems.Count > 0 ? (args.Has("toes") ? ResultCodes.ReadError : ResultCodes.InvalidArguments) : ResultCodes.InvalidArguments)
                        : ResultCodes.OperationFailed;
                    throw new CommandException(code, $"slope design: {d.Status} ({d.StatusKind}): {d.Message}", status);
                }
                if (d.Status == TerrainSlopeDesign.Statuses.NoDaylight)
                    warnings.Add("no_daylight: the slope does not reach the terrain everywhere within its footprint (or below height_limit); the results are partial (complete false), and the catch lines that end there say so");

                // the outputs
                var lf = d.LocalFrame && d.LocalFrameFallback == false;
                JsonObject frame = lf ? new JsonObject() { ["used_origin"] = new JsonArray(d.UsedOrigin.x, d.UsedOrigin.y) } : null;
                if (args.Has("output")) {
                    DMesh3 outMesh = composite == "replacement" ? d.Replacement : d.Composite;
                    if (outMesh == null) throw new CommandException(ResultCodes.InternalError, "the composite is missing");
                    MeshFiles.Write(args.String("output"), outMesh, args.Bool("binary"), groups: true);
                }
                if (args.Has("surface")) MeshFiles.Write(args.String("surface"), d.SlopeSurface, args.Bool("binary"), groups: true);
                if (args.Has("region")) MeshFiles.Write(args.String("region"), d.SlopeRegion, args.Bool("binary"), groups: true);
                var catchLines = d.CatchLines.Select(l => (l.Points, l.Closed, new JsonObject() {
                    ["closed"] = l.Closed, ["start_reason"] = Snake(l.StartReason.ToString()), ["end_reason"] = Snake(l.EndReason.ToString()),
                    ["max_uncertainty"] = MeshReport.Num(l.Uncertainty.Count > 0 ? l.Uncertainty.Max() : 0),
                    ["tangential_segments"] = l.Tangential.Count(t => t),
                })).ToList();
                if (args.Has("catch_lines"))
                    WriteText(args.String("catch_lines"), SlopeToesGeoJson.Lines(catchLines, toes.Crs, frame?.DeepClone() as JsonObject));
                if (args.Has("target_lines"))
                    WriteText(args.String("target_lines"), SlopeToesGeoJson.Lines(d.TargetHeightLines.Select(l => (l.Points, l.Closed,
                        new JsonObject() { ["height"] = l.Height, ["closed"] = l.Closed })), toes.Crs, frame?.DeepClone() as JsonObject));

                var o = new JsonObject() {
                    ["terrain"] = args.String("terrain"), ["toes"] = args.String("toes"), ["output"] = args.String("output"),
                };
                foreach (var kv in status.ToList()) { status.Remove(kv.Key); o[kv.Key] = kv.Value; }
                o["complete"] = d.IsComplete;
                o["direction"] = args.String("direction");
                o["composite"] = composite;
                o["toe_count"] = toes.Toes.Count;
                o["pieces"] = d.Pieces.Count;
                o["volume"] = MeshReport.Num(d.Volume);
                o["plan_area"] = MeshReport.Num(d.PlanArea);
                o["slope_area"] = MeshReport.Num(d.SlopeArea);
                o["cut_volume"] = MeshReport.Num(d.CutVolume);
                o["fill_volume"] = MeshReport.Num(d.FillVolume);
                o["cut_area"] = MeshReport.Num(d.CutArea);
                o["fill_area"] = MeshReport.Num(d.FillArea);
                o["catch_lines"] = new JsonArray(catchLines.Select(l => (JsonNode)new JsonObject() {
                    ["points"] = l.Points.Count, ["closed"] = l.Closed, ["start_reason"] = l.Item3["start_reason"].DeepClone(),
                    ["end_reason"] = l.Item3["end_reason"].DeepClone(), ["max_uncertainty"] = l.Item3["max_uncertainty"]?.DeepClone(),
                    ["tangential_segments"] = l.Item3["tangential_segments"].DeepClone() }).ToArray());
                o["ill_conditioned_segments"] = d.IllConditionedCatchLine.Count;
                o["tangential_contacts"] = d.TangentialContact.Count;
                o["target_height_lines"] = d.TargetHeightLines.Count;
                if (composite == "replacement") o["replaced_terrain_triangles"] = d.ReplacedTerrainTriangles.Count;
                o["max_slope_ratio"] = MeshReport.Num(d.MaxSlopeRatio);
                o["used_tolerance"] = MeshReport.Num(d.UsedTolerance);
                o["slope_margin"] = MeshReport.Num(d.UsedSlopeMargin);
                o["toe_lowering"] = MeshReport.Num(d.UsedToeLowering);
                o["min_feature_size"] = MeshReport.Num(d.UsedMinFeatureSize);
                o["local_frame"] = d.LocalFrame;
                if (d.LocalFrame) {
                    o["local_frame_fallback"] = d.LocalFrameFallback;
                    o["used_origin"] = new JsonArray(d.UsedOrigin.x, d.UsedOrigin.y);
                    if (lf) warnings.Add("local_frame: every output is in the local frame; add used_origin to x and y for the input's frame");
                    else warnings.Add("local_frame_fallback: an input coordinate could not be translated exactly, the design ran in the input's frame (used_origin 0, 0)");
                }
                if (toes.Crs != null) o["crs"] = toes.Crs.DeepClone();
                o["realizations"] = d.Realizations;
                o["phase_seconds"] = new JsonObject(d.PhaseSeconds.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, MeshReport.Num(kv.Value))));
                DMesh3 reportMesh = composite == "replacement" ? d.Replacement : composite == "full" ? d.Composite : null;
                if (reportMesh != null) o["mesh"] = MeshReport.Metrics(reportMesh, false);
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }
    }
}
