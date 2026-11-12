// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clipper2Lib;
using g3;
using g3.Clipper2;

namespace g3cli
{
    /// <summary> GeoJSON polygons in and out for polygon2d: Polygon and MultiPolygon (also in Features and FeatureCollections), x and y only </summary>
    public static class PolygonGeoJson
    {
        public const string FormatDescription =
            "GeoJSON (RFC 7946): a FeatureCollection, a Feature or a geometry of type Polygon or MultiPolygon (a polygon: an outer ring and its holes; " +
            "positions [x, y], a third coordinate is ignored; coordinates are taken as given, no reprojection; a \"crs\" member is passed through). " +
            "The orientation of the rings does not matter (the first ring of a polygon is its outer ring, the others are holes)";

        public class Result
        {
            public List<GeneralPolygon2d> Polygons = new List<GeneralPolygon2d>();
            public JsonNode Crs;
            public bool DroppedZ;
            public long Vertices;
            public int Rings;
            /// <summary> lines (LineString, MultiLineString), only in the lines mode of Parse </summary>
            public List<PolyLine2d> Lines = new List<PolyLine2d>();
            public bool LinesMode;
        }

        public static Result Parse(string json, string code, string what, bool lines = false)
        {
            if (json.Length > MaxTextBytes)
                throw new CommandException(code, what + " is longer than 256 million characters");
            JsonDocument doc;
            try {
                doc = JsonDocument.Parse(json, new JsonDocumentOptions() { MaxDepth = 64 });
            } catch (Exception e) when (e is JsonException || e is ArgumentException) {
                throw new CommandException(code, what + " is not valid JSON: " + e.Message);
            }
            using (doc) {
                var r = new Result() { LinesMode = lines };
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("crs", out JsonElement crs))
                    r.Crs = JsonNode.Parse(crs.GetRawText());
                Node(doc.RootElement, r, code, what, 0);
                return r;
            }
        }

        const long MaxTextBytes = 256L * 1024 * 1024;

        public static Result ReadFile(string path, bool lines = false)
        {
            string full;
            try { full = Path.GetFullPath(path); } catch (Exception e) { throw new CommandException(ResultCodes.ReadError, $"cannot use '{path}': {e.Message}"); }
            // a regular file only: devices and pipes (/dev/zero, /proc/..., FIFOs) have no length and would never end
            if (File.Exists(full) == false || full.StartsWith("/dev/") || full.StartsWith("/proc/") || full.StartsWith("/sys/"))
                throw new CommandException(ResultCodes.ReadError, $"file not found: {path}");
            string text;
            try {
                using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var ms = new MemoryStream()) {
                    var buf = new byte[1 << 16];
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0) {
                        ms.Write(buf, 0, n);
                        if (ms.Length > MaxTextBytes)
                            throw new CommandException(ResultCodes.ReadError, $"'{path}' is larger than {MaxTextBytes / (1024 * 1024)} MB");
                    }
                    text = new System.Text.UTF8Encoding(false, false).GetString(ms.GetBuffer(), 0, (int)ms.Length);
                }
            } catch (CommandException) {
                throw;
            } catch (Exception e) {
                throw new CommandException(ResultCodes.ReadError, $"cannot read '{path}': {e.Message}");
            }
            return Parse(text, ResultCodes.ReadError, "'" + path + "'", lines);
        }

        const int MaxPolygons = 1000000, MaxVertices = 20000000, MaxRings = 2000000;

        static void Node(JsonElement e, Result r, string code, string what, int depth)
        {
            if (e.ValueKind != JsonValueKind.Object || e.TryGetProperty("type", out JsonElement t) == false || t.ValueKind != JsonValueKind.String)
                throw new CommandException(code, what + ": expected a GeoJSON object with a \"type\"");
            switch (t.GetString()) {
                case "FeatureCollection":
                    if (e.TryGetProperty("features", out JsonElement fs) == false || fs.ValueKind != JsonValueKind.Array)
                        throw new CommandException(code, what + ": a FeatureCollection needs a \"features\" array");
                    foreach (JsonElement f in fs.EnumerateArray()) Node(f, r, code, what, depth + 1);
                    break;
                case "Feature":
                    if (e.TryGetProperty("geometry", out JsonElement g) == false || g.ValueKind != JsonValueKind.Object)
                        throw new CommandException(code, what + ": a Feature needs a \"geometry\" (null geometries are not supported)");
                    Node(g, r, code, what, depth + 1);
                    break;
                case "Polygon" when !r.LinesMode:
                    AddPolygon(Coordinates(e, code, what), r, code, what);
                    break;
                case "MultiPolygon" when !r.LinesMode:
                    foreach (JsonElement poly in Coordinates(e, code, what).EnumerateArray()) AddPolygon(poly, r, code, what);
                    break;
                case "LineString" when r.LinesMode:
                    AddLine(Coordinates(e, code, what), r, code, what);
                    break;
                case "MultiLineString" when r.LinesMode:
                    foreach (JsonElement line in Coordinates(e, code, what).EnumerateArray()) AddLine(line, r, code, what);
                    break;
                default:
                    throw new CommandException(code, what + $": geometry type '{t.GetString()}' is not supported " +
                        (r.LinesMode ? "here (LineString, MultiLineString, Feature, FeatureCollection)" : "(Polygon, MultiPolygon, Feature, FeatureCollection)"));
            }
        }

        static JsonElement Coordinates(JsonElement e, string code, string what)
        {
            if (e.TryGetProperty("coordinates", out JsonElement c) == false || c.ValueKind != JsonValueKind.Array)
                throw new CommandException(code, what + ": a geometry needs a \"coordinates\" array");
            return c;
        }

        static void AddPolygon(JsonElement rings, Result r, string code, string what)
        {
            if (rings.ValueKind != JsonValueKind.Array || rings.GetArrayLength() == 0)
                throw new CommandException(code, what + ": a polygon needs at least an outer ring");
            if (r.Polygons.Count >= MaxPolygons)
                throw new CommandException(code, what + $": more than {MaxPolygons} polygons");
            GeneralPolygon2d gp = null;
            int ringIndex = 0;
            foreach (JsonElement ring in rings.EnumerateArray()) {
                if (++r.Rings > MaxRings)
                    throw new CommandException(code, what + $": more than {MaxRings} rings");
                Polygon2d poly = Ring(ring, r, code, what, ringIndex);
                if (gp == null) gp = new GeneralPolygon2d(poly);
                else {
                    // holes run the other way than the outer ring, as GeneralPolygon2d.Area and the clipper expect (containment is the clipper's business)
                    if (poly.IsClockwise == gp.Outer.IsClockwise) poly.Reverse();
                    gp.AddHole(poly, false, false);
                }
                ringIndex++;
            }
            r.Polygons.Add(gp);
        }

        static void AddLine(JsonElement line, Result r, string code, string what)
        {
            if (line.ValueKind != JsonValueKind.Array)
                throw new CommandException(code, what + ": a line is an array of positions");
            if (r.Lines.Count >= MaxPolygons)
                throw new CommandException(code, what + $": more than {MaxPolygons} lines");
            if ((r.Vertices += line.GetArrayLength()) > MaxVertices)
                throw new CommandException(code, what + $": more than {MaxVertices} positions in all");
            var pl = new PolyLine2d();
            foreach (JsonElement pos in line.EnumerateArray()) {
                if (pos.ValueKind != JsonValueKind.Array || pos.GetArrayLength() < 2
                    || pos[0].ValueKind != JsonValueKind.Number || pos[1].ValueKind != JsonValueKind.Number
                    || pos[0].TryGetDouble(out double x) == false || pos[1].TryGetDouble(out double y) == false
                    || double.IsFinite(x) == false || double.IsFinite(y) == false)
                    throw new CommandException(code, what + ": a position is [x, y] with finite numbers");
                if (pos.GetArrayLength() > 2) r.DroppedZ = true;
                pl.AppendVertex(new Vector2d(x, y));
            }
            if (pl.VertexCount < 2)
                throw new CommandException(code, what + $": line {r.Lines.Count} has fewer than 2 positions");
            r.Lines.Add(pl);
        }

        /// <summary> a FeatureCollection with one MultiLineString Feature (an empty one for no lines) </summary>
        public static JsonObject WriteLines(IList<PolyLine2d> lines, JsonNode crs, JsonObject properties)
        {
            var multi = new JsonArray();
            foreach (PolyLine2d pl in lines) {
                var a = new JsonArray();
                foreach (Vector2d v in pl.Vertices) a.Add(new JsonArray(v.x, v.y));
                multi.Add(a);
            }
            var root = new JsonObject() { ["type"] = "FeatureCollection" };
            if (crs != null) root["crs"] = crs.DeepClone();
            root["features"] = new JsonArray(new JsonObject() {
                ["type"] = "Feature", ["properties"] = properties ?? new JsonObject(),
                ["geometry"] = new JsonObject() { ["type"] = "MultiLineString", ["coordinates"] = multi },
            });
            return root;
        }

        static Polygon2d Ring(JsonElement ring, Result r, string code, string what, int index)
        {
            if (ring.ValueKind != JsonValueKind.Array)
                throw new CommandException(code, what + ": a ring is an array of positions");
            int n = ring.GetArrayLength();
            if ((r.Vertices += n) > MaxVertices)
                throw new CommandException(code, what + $": more than {MaxVertices} positions in all");
            var pts = new List<Vector2d>(n);
            foreach (JsonElement pos in ring.EnumerateArray()) {
                if (pos.ValueKind != JsonValueKind.Array || pos.GetArrayLength() < 2)
                    throw new CommandException(code, what + ": a position is [x, y]");
                var xy = pos.EnumerateArray().Take(3).ToArray();
                if (xy[0].ValueKind != JsonValueKind.Number || xy[1].ValueKind != JsonValueKind.Number
                    || xy[0].TryGetDouble(out double x) == false || xy[1].TryGetDouble(out double y) == false
                    || double.IsFinite(x) == false || double.IsFinite(y) == false)
                    throw new CommandException(code, what + ": a coordinate is not a finite number");
                if (xy.Length > 2) r.DroppedZ = true;
                pts.Add(new Vector2d(x, y));
            }
            // (GeoJSON rings repeat the first position)
            if (pts.Count > 1 && pts[0] == pts[pts.Count - 1]) pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3)
                throw new CommandException(code, what + $": ring {index} has fewer than 3 distinct positions");
            return new Polygon2d(pts);
        }

        static JsonArray Ring(Polygon2d p)
        {
            var a = new JsonArray();
            foreach (Vector2d v in p.Vertices) a.Add(new JsonArray(v.x, v.y));
            a.Add(new JsonArray(p[0].x, p[0].y));
            return a;
        }

        /// <summary> a FeatureCollection with one MultiPolygon Feature (an empty one for no polygons); outer rings counterclockwise, holes clockwise </summary>
        public static JsonObject Write(IList<GeneralPolygon2d> polygons, JsonNode crs, JsonObject properties)
        {
            var multi = new JsonArray();
            foreach (GeneralPolygon2d gp in polygons) {
                var rings = new JsonArray { Ring(gp.Outer) };
                foreach (Polygon2d h in gp.Holes) rings.Add(Ring(h));
                multi.Add(rings);
            }
            var root = new JsonObject() { ["type"] = "FeatureCollection" };
            if (crs != null) root["crs"] = crs.DeepClone();
            root["features"] = new JsonArray(new JsonObject() {
                ["type"] = "Feature", ["properties"] = properties ?? new JsonObject(),
                ["geometry"] = new JsonObject() { ["type"] = "MultiPolygon", ["coordinates"] = multi },
            });
            return root;
        }
    }

    public static partial class Commands
    {
        static string PolyNum(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        static FillRule ParseFillRule(string s)
        {
            switch (s) {
                case "non_zero": return FillRule.NonZero;
                case "even_odd": return FillRule.EvenOdd;
                case "positive": return FillRule.Positive;
                case "negative": return FillRule.Negative;
            }
            throw new CommandException(ResultCodes.InvalidArguments, $"unknown fill_rule '{s}'");
        }

        static JoinType ParseJoin(string s)
        {
            switch (s) {
                case "round": return JoinType.Round;
                case "miter": return JoinType.Miter;
                case "square": return JoinType.Square;
                case "bevel": return JoinType.Bevel;
            }
            throw new CommandException(ResultCodes.InvalidArguments, $"unknown join '{s}'");
        }

        static Command Polygon2dCommand()
        {
            var c = new Command() {
                Name = "polygon2d",
                Description = "Robust 2D polygon booleans and offsets with the Clipper2 adapter (geometry3Sharp.Clipper2; integer grid fitted to the input, " +
                    "scale invariant for power-of-two scales, UTM coordinates fine). Operations: union (of the subject alone, or with the clip), " +
                    "intersection, difference (subject minus clip), xor, offset (grow by delta, shrink with a negative delta). Input: " + PolygonGeoJson.FormatDescription +
                    ". The result is a GeoJSON FeatureCollection with one MultiPolygon (outer rings counterclockwise, holes clockwise; written to 'output' " +
                    "or, with return_geojson, in the result as result_geojson), with its counts and area. Result vertices lie on the grid (spacing about 1e-12 of the extent): " +
                    "'frame' reports it. An empty result (eg disjoint polygons intersected) is ok with zero polygons.",
            };
            c.Params.Add(new Param("subject", ParamType.String, "subject polygons, a GeoJSON file; or give subject_geojson") { Positional = true });
            c.Params.Add(new Param("clip", ParamType.String, "clip polygons, a GeoJSON file (all operations but offset; optional for union); or give clip_geojson") { Positional = true });
            c.Params.Add(new Param("output", ParamType.String, "output GeoJSON file for the result (optional; give it as --output: the second positional argument is the clip)"));
            c.Params.Add(new Param("subject_geojson", ParamType.String, "the subject as inline GeoJSON text, instead of a file"));
            c.Params.Add(new Param("clip_geojson", ParamType.String, "the clip as inline GeoJSON text, instead of a file"));
            c.Params.Add(new Param("op", ParamType.String, "union, intersection, difference, xor, offset; simplify (polygons) and simplify_lines (LineString / MultiLineString subject); clip_lines (the lines of a LineString / MultiLineString subject clipped by the clip polygons); offset_lines (the band of width 2 delta around lines)") { Required = true, Enum = new[] { "union", "intersection", "difference", "xor", "offset", "simplify", "simplify_lines", "clip_lines", "offset_lines" } });
            c.Params.Add(new Param("epsilon", ParamType.Number, "simplify, simplify_lines: remove vertices closer than this (input units) to the line through their neighbours; 0 only merges overlaps"));
            c.Params.Add(new Param("clip_type", ParamType.String, "clip_lines: keep the pieces of the lines inside the clip polygons (intersection) or outside (difference)") { Default = "intersection", Enum = new[] { "intersection", "difference" } });
            c.Params.Add(new Param("end_type", ParamType.String, "offset_lines: end treatment (joined: a closed line is offset on both sides)") { Default = "round", Enum = new[] { "round", "square", "butt", "joined" } });
            c.Params.Add(new Param("delta", ParamType.Number, "offset: distance in coordinate units (positive grows, negative shrinks)"));
            c.Params.Add(new Param("join", ParamType.String, "offset: corner treatment") { Default = "round", Enum = new[] { "round", "miter", "square", "bevel" } });
            c.Params.Add(new Param("miter_limit", ParamType.Number, "offset with join miter: limit of the miter length in multiples of delta (library default 2)"));
            c.Params.Add(new Param("arc_tolerance", ParamType.Number, "offset with join round: largest distance of the arc's chords from the arc (0 = delta / 1000)"));
            c.Params.Add(new Param("fill_rule", ParamType.String, "how overlapping polygons of one operand combine (library default non_zero: their union)") { Enum = new[] { "non_zero", "even_odd", "positive", "negative" } });
            c.Params.Add(new Param("precision_bits", ParamType.Integer, "the integer grid has 2^bits units across the half extent (8..50, library default 40)"));
            c.Params.Add(new Param("preserve_collinear", ParamType.Boolean, "keep vertices in the middle of straight edges") { Default = false });
            c.Params.Add(new Param("return_geojson", ParamType.Boolean, "also return the result as 'result_geojson' in the JSON result") { Default = false });
            c.Run = args => {
                string op = args.String("op");
                if (op == "simplify" || op == "simplify_lines" || op == "clip_lines" || op == "offset_lines")
                    return Polygon2dMoreOps(args, op);
                if (args.Has("epsilon") || args.String("clip_type") != "intersection" || args.String("end_type") != "round")
                    throw new CommandException(ResultCodes.InvalidArguments, "epsilon, clip_type and end_type are options of simplify, simplify_lines, clip_lines and offset_lines");
                if (args.Has("subject") == args.Has("subject_geojson"))
                    throw new CommandException(ResultCodes.InvalidArguments, "give exactly one of subject (a GeoJSON file) and subject_geojson (inline GeoJSON)");
                if (args.Has("clip") && args.Has("clip_geojson"))
                    throw new CommandException(ResultCodes.InvalidArguments, "give clip or clip_geojson, not both");
                bool hasClip = args.Has("clip") || args.Has("clip_geojson");
                if (op == "offset") {
                    if (hasClip)
                        throw new CommandException(ResultCodes.InvalidArguments, "offset takes no clip");
                    if (args.Has("delta") == false || !double.IsFinite(args.Number("delta")))
                        throw new CommandException(ResultCodes.InvalidArguments, "offset needs a finite delta");
                } else {
                    if (args.Has("delta") || args.Has("miter_limit") || args.Has("arc_tolerance"))
                        throw new CommandException(ResultCodes.InvalidArguments, "delta, miter_limit and arc_tolerance are options of offset");
                    if (op != "union" && hasClip == false)
                        throw new CommandException(ResultCodes.InvalidArguments, op + " needs a clip");
                }
                if (op == "offset") {
                    if (args.Has("fill_rule"))
                        throw new CommandException(ResultCodes.InvalidArguments, "fill_rule is an option of the booleans");
                    if (args.Has("miter_limit") && args.String("join") != "miter")
                        throw new CommandException(ResultCodes.InvalidArguments, "miter_limit applies to join miter");
                    if (args.Has("arc_tolerance") && args.String("join") != "round")
                        throw new CommandException(ResultCodes.InvalidArguments, "arc_tolerance applies to join round");
                } else if (args.Has("miter_limit") || args.Has("arc_tolerance") || args.String("join") != "round")
                    throw new CommandException(ResultCodes.InvalidArguments, "join, miter_limit and arc_tolerance are options of offset");
                var options = new Clipper2Options() { PreserveCollinear = args.Bool("preserve_collinear"), JoinType = ParseJoin(args.String("join")) };
                if (args.Has("fill_rule")) options.FillRule = ParseFillRule(args.String("fill_rule"));
                if (args.Has("precision_bits")) {
                    int bits = args.Integer("precision_bits");
                    if (bits < ClipperFrame.MinPrecisionBits || bits > ClipperFrame.MaxPrecisionBits)
                        throw new CommandException(ResultCodes.InvalidArguments, $"'precision_bits' must be in {ClipperFrame.MinPrecisionBits}..{ClipperFrame.MaxPrecisionBits}");
                    options.PrecisionBits = bits;
                }
                if (args.Has("miter_limit")) {
                    double ml = args.Number("miter_limit");
                    if (!(ml >= 1 && ml <= Clipper2Options.MaxMiterLimit))
                        throw new CommandException(ResultCodes.InvalidArguments, $"'miter_limit' must be in 1..{Clipper2Options.MaxMiterLimit}");
                    options.MiterLimit = ml;
                }
                if (args.Has("arc_tolerance")) {
                    double at = args.Number("arc_tolerance");
                    if (!(at >= 0) || double.IsInfinity(at))
                        throw new CommandException(ResultCodes.InvalidArguments, "'arc_tolerance' must be a finite number >= 0");
                    options.ArcTolerance = at;
                }

                PolygonGeoJson.Result subject = args.Has("subject_geojson")
                    ? PolygonGeoJson.Parse(args.String("subject_geojson"), ResultCodes.InvalidArguments, "subject_geojson")
                    : PolygonGeoJson.ReadFile(args.String("subject"));
                PolygonGeoJson.Result clip = args.Has("clip_geojson") ? PolygonGeoJson.Parse(args.String("clip_geojson"), ResultCodes.InvalidArguments, "clip_geojson")
                    : args.Has("clip") ? PolygonGeoJson.ReadFile(args.String("clip")) : null;
                if (subject.Polygons.Count == 0)
                    throw new CommandException(ResultCodes.EmptyMesh, "the subject has no polygons");
                if (clip != null && clip.Polygons.Count == 0)
                    throw new CommandException(ResultCodes.EmptyMesh, "the clip has no polygons");

                if (op == "offset") {
                    // the grid is fitted to the input and the margin: a delta far beyond the input's size would round the input away
                    AxisAlignedBox2d box = subject.Polygons[0].Bounds;
                    foreach (var p in subject.Polygons.Skip(1)) box.Contain(p.Bounds);
                    double extent = Math.Max(box.Width, box.Height);
                    if (Math.Abs(args.Number("delta")) > 1000 * extent)
                        throw new CommandException(ResultCodes.InvalidArguments, "|delta| must be at most 1000 times the extent of the subject (" + PolyNum(1000 * extent) + "); a negative delta that large gives an empty result anyway");
                }
                Clipper2Result res = null;
                Exception failure = null;
                // on a thread with a large stack: the adapter builds nested polygons recursively, one level per nesting of rings
                var worker = new System.Threading.Thread(() => {
                    try {
                        switch (op) {
                            case "union": res = Clipper2Polygons.Union(subject.Polygons, clip?.Polygons, options); break;
                            case "intersection": res = Clipper2Polygons.Intersection(subject.Polygons, clip.Polygons, options); break;
                            case "difference": res = Clipper2Polygons.Difference(subject.Polygons, clip.Polygons, options); break;
                            case "xor": res = Clipper2Polygons.Xor(subject.Polygons, clip.Polygons, options); break;
                            default: res = Clipper2Polygons.Offset(subject.Polygons, args.Number("delta"), options); break;
                        }
                    } catch (Exception e) { failure = e; }
                }, 512 * 1024 * 1024);
                worker.Start(); worker.Join();
                if (failure is ArgumentException || failure is OverflowException)
                    throw new CommandException(ResultCodes.InvalidArguments, "polygon2d: " + failure.Message);
                if (failure != null)
                    throw new CommandException(ResultCodes.InternalError, failure.GetType().Name + ": " + failure.Message);
                string status = ToSnake(res.Status.ToString());
                if (res.Status == Clipper2Status.InvalidParameter)
                    throw new CommandException(ResultCodes.InvalidArguments, "polygon2d: " + res.Error, new JsonObject() { ["clipper_status"] = status });
                if (res.Ok == false)
                    throw new CommandException(ResultCodes.OperationFailed, "polygon2d: " + status + ": " + res.Error, new JsonObject() { ["clipper_status"] = status });

                JsonNode crs = subject.Crs ?? clip?.Crs;
                JsonObject geojson = PolygonGeoJson.Write(res.Polygons, crs, new JsonObject() { ["op"] = op });
                if (args.Has("output")) WriteText(args.String("output"), geojson.ToJsonString(new JsonSerializerOptions() { WriteIndented = false }));

                var o = new JsonObject() { ["op"] = op, ["clipper_status"] = status };
                if (args.Has("output")) o["output"] = args.String("output");
                o["subject"] = PolygonSummary(subject.Polygons);
                if (clip != null) o["clip"] = PolygonSummary(clip.Polygons);
                o["result"] = PolygonSummary(res.Polygons);
                o["empty_result"] = res.Polygons.Count == 0;
                o["frame"] = new JsonObject() { ["origin"] = new JsonArray(res.Frame.Origin.x, res.Frame.Origin.y), ["scale"] = Val(res.Frame.Scale), ["resolution"] = Val(res.Frame.Resolution) };
                if (args.Bool("return_geojson")) o["result_geojson"] = geojson;
                var warnings = new List<string>();
                if (subject.DroppedZ || (clip?.DroppedZ ?? false)) warnings.Add("third coordinates (z) of the positions are ignored");
                if (op == "offset" && args.Bool("preserve_collinear")) warnings.Add("preserve_collinear has no effect on offset");
                if (clip?.Crs != null && subject.Crs != null && clip.Crs.ToJsonString() != subject.Crs.ToJsonString())
                    warnings.Add("subject and clip name different crs; the subject's is used (coordinates are not reprojected)");
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }

        static JsonObject LineSummary(IList<PolyLine2d> lines)
        {
            var o = new JsonObject() { ["lines"] = lines.Count, ["vertices"] = lines.Sum(l => l.VertexCount), ["length"] = Val(lines.Sum(l => l.ArcLength)) };
            if (lines.Count > 0) {
                AxisAlignedBox2d b = lines[0].GetBounds();
                foreach (var l in lines.Skip(1)) b.Contain(l.GetBounds());
                o["bounds"] = new JsonObject() { ["min"] = new JsonArray(b.Min.x, b.Min.y), ["max"] = new JsonArray(b.Max.x, b.Max.y) };
            }
            return o;
        }

        /// <summary> simplify, simplify_lines, clip_lines and offset_lines of polygon2d </summary>
        static JsonObject Polygon2dMoreOps(Args args, string op)
        {
            bool lineSubject = op != "simplify";
            if (args.Has("subject") == args.Has("subject_geojson"))
                throw new CommandException(ResultCodes.InvalidArguments, "give exactly one of subject (a GeoJSON file) and subject_geojson (inline GeoJSON)");
            if (args.Has("clip") && args.Has("clip_geojson"))
                throw new CommandException(ResultCodes.InvalidArguments, "give clip or clip_geojson, not both");
            bool hasClip = args.Has("clip") || args.Has("clip_geojson");
            if (op == "clip_lines" && !hasClip)
                throw new CommandException(ResultCodes.InvalidArguments, "clip_lines needs a clip (polygons)");
            if (op != "clip_lines" && hasClip)
                throw new CommandException(ResultCodes.InvalidArguments, op + " takes no clip");
            bool offsetting = op == "offset_lines";
            bool simplifying = op == "simplify" || op == "simplify_lines";
            if (!offsetting && (args.Has("delta") || args.Has("miter_limit") || args.Has("arc_tolerance") || args.String("end_type") != "round" || args.String("join") != "round"))
                throw new CommandException(ResultCodes.InvalidArguments, "delta, join, miter_limit, arc_tolerance and end_type are options of offset and offset_lines");
            if (offsetting && args.Has("fill_rule"))
                throw new CommandException(ResultCodes.InvalidArguments, "fill_rule is an option of the booleans, simplify and clip_lines");
            if (op != "clip_lines" && args.String("clip_type") != "intersection")
                throw new CommandException(ResultCodes.InvalidArguments, "clip_type is an option of clip_lines");
            if (!simplifying && args.Has("epsilon"))
                throw new CommandException(ResultCodes.InvalidArguments, "epsilon is an option of simplify and simplify_lines");
            if (op == "simplify_lines" && args.Has("fill_rule"))
                throw new CommandException(ResultCodes.InvalidArguments, "fill_rule does not apply to lines");
            double epsilon = 0, delta = 0;
            if (simplifying) {
                if (args.Has("epsilon") == false || !(args.Number("epsilon") >= 0) || double.IsInfinity(args.Number("epsilon")))
                    throw new CommandException(ResultCodes.InvalidArguments, op + " needs a finite epsilon >= 0");
                epsilon = args.Number("epsilon");
            }
            if (offsetting) {
                if (args.Has("delta") == false || !double.IsFinite(args.Number("delta")))
                    throw new CommandException(ResultCodes.InvalidArguments, "offset_lines needs a finite delta >= 0 (a band of width 2 delta around the lines; the library refuses a negative one)");
                delta = args.Number("delta");
                if (args.Has("miter_limit") && args.String("join") != "miter")
                    throw new CommandException(ResultCodes.InvalidArguments, "miter_limit applies to join miter");
                if (args.Has("arc_tolerance") && args.String("join") != "round" && args.String("end_type") != "round")
                    throw new CommandException(ResultCodes.InvalidArguments, "arc_tolerance applies to round joins and ends");
            }
            var options = new Clipper2Options() { PreserveCollinear = args.Bool("preserve_collinear"), JoinType = ParseJoin(args.String("join")) };
            if (offsetting) {
                switch (args.String("end_type")) {
                    case "square": options.EndType = EndType.Square; break;
                    case "butt": options.EndType = EndType.Butt; break;
                    case "joined": options.EndType = EndType.Joined; break;
                    default: options.EndType = EndType.Round; break;
                }
            }
            if (args.Has("fill_rule")) options.FillRule = ParseFillRule(args.String("fill_rule"));
            if (args.Has("precision_bits")) {
                int bits = args.Integer("precision_bits");
                if (bits < ClipperFrame.MinPrecisionBits || bits > ClipperFrame.MaxPrecisionBits)
                    throw new CommandException(ResultCodes.InvalidArguments, $"'precision_bits' must be in {ClipperFrame.MinPrecisionBits}..{ClipperFrame.MaxPrecisionBits}");
                options.PrecisionBits = bits;
            }
            if (args.Has("miter_limit")) {
                double ml = args.Number("miter_limit");
                if (!(ml >= 1 && ml <= Clipper2Options.MaxMiterLimit))
                    throw new CommandException(ResultCodes.InvalidArguments, $"'miter_limit' must be in 1..{Clipper2Options.MaxMiterLimit}");
                options.MiterLimit = ml;
            }
            if (args.Has("arc_tolerance")) {
                double at = args.Number("arc_tolerance");
                if (!(at >= 0) || double.IsInfinity(at))
                    throw new CommandException(ResultCodes.InvalidArguments, "'arc_tolerance' must be a finite number >= 0");
                options.ArcTolerance = at;
            }

            PolygonGeoJson.Result subject = args.Has("subject_geojson")
                ? PolygonGeoJson.Parse(args.String("subject_geojson"), ResultCodes.InvalidArguments, "subject_geojson", lineSubject)
                : PolygonGeoJson.ReadFile(args.String("subject"), lineSubject);
            PolygonGeoJson.Result clip = args.Has("clip_geojson") ? PolygonGeoJson.Parse(args.String("clip_geojson"), ResultCodes.InvalidArguments, "clip_geojson")
                : args.Has("clip") ? PolygonGeoJson.ReadFile(args.String("clip")) : null;
            if (lineSubject ? subject.Lines.Count == 0 : subject.Polygons.Count == 0)
                throw new CommandException(ResultCodes.EmptyMesh, lineSubject ? "the subject has no lines" : "the subject has no polygons");
            if (clip != null && clip.Polygons.Count == 0)
                throw new CommandException(ResultCodes.EmptyMesh, "the clip has no polygons");
            if (offsetting) {
                AxisAlignedBox2d box = subject.Lines[0].GetBounds();
                foreach (var l in subject.Lines.Skip(1)) box.Contain(l.GetBounds());
                double extent = Math.Max(box.Width, box.Height);
                if (delta > 1000 * Math.Max(extent, 1e-300) && extent > 0)
                    throw new CommandException(ResultCodes.InvalidArguments, "delta must be at most 1000 times the extent of the lines (" + PolyNum(1000 * extent) + ")");
            }

            Clipper2Result res = null;
            Exception failure = null;
            var worker = new System.Threading.Thread(() => {
                try {
                    switch (op) {
                        case "simplify": res = Clipper2Polygons.Simplify(subject.Polygons, epsilon, options); break;
                        case "simplify_lines": res = Clipper2Polygons.SimplifyPolyLines(subject.Lines, epsilon, options); break;
                        case "clip_lines":
                            res = Clipper2Polygons.ClipPolyLines(args.String("clip_type") == "difference" ? ClipType.Difference : ClipType.Intersection, subject.Lines, clip.Polygons, options);
                            break;
                        default: res = Clipper2Polygons.OffsetPolyLines(subject.Lines, delta, options); break;
                    }
                } catch (Exception e) { failure = e; }
            }, 512 * 1024 * 1024);
            worker.Start(); worker.Join();
            if (failure is ArgumentException || failure is OverflowException)
                throw new CommandException(ResultCodes.InvalidArguments, "polygon2d: " + failure.Message);
            if (failure != null)
                throw new CommandException(ResultCodes.InternalError, failure.GetType().Name + ": " + failure.Message);
            string status = ToSnake(res.Status.ToString());
            if (res.Status == Clipper2Status.InvalidParameter)
                throw new CommandException(ResultCodes.InvalidArguments, "polygon2d: " + res.Error, new JsonObject() { ["clipper_status"] = status });
            if (res.Ok == false)
                throw new CommandException(ResultCodes.OperationFailed, "polygon2d: " + status + ": " + res.Error, new JsonObject() { ["clipper_status"] = status });

            bool linesOut = op == "simplify_lines" || op == "clip_lines";
            JsonNode crs = subject.Crs ?? clip?.Crs;
            JsonObject geojson = linesOut ? PolygonGeoJson.WriteLines(res.PolyLines, crs, new JsonObject() { ["op"] = op })
                : PolygonGeoJson.Write(res.Polygons, crs, new JsonObject() { ["op"] = op });
            if (args.Has("output")) WriteText(args.String("output"), geojson.ToJsonString(new JsonSerializerOptions() { WriteIndented = false }));

            var o = new JsonObject() { ["op"] = op, ["clipper_status"] = status };
            if (args.Has("output")) o["output"] = args.String("output");
            o["subject"] = lineSubject ? LineSummary(subject.Lines) : PolygonSummary(subject.Polygons);
            if (clip != null) o["clip"] = PolygonSummary(clip.Polygons);
            o["result"] = linesOut ? LineSummary(res.PolyLines) : PolygonSummary(res.Polygons);
            o["empty_result"] = linesOut ? res.PolyLines.Count == 0 : res.Polygons.Count == 0;
            o["frame"] = new JsonObject() { ["origin"] = new JsonArray(res.Frame.Origin.x, res.Frame.Origin.y), ["scale"] = Val(res.Frame.Scale), ["resolution"] = Val(res.Frame.Resolution) };
            if (args.Bool("return_geojson")) o["result_geojson"] = geojson;
            var warnings = new List<string>();
            if (subject.DroppedZ || (clip?.DroppedZ ?? false)) warnings.Add("third coordinates (z) of the positions are ignored");
            if (op == "offset_lines" && args.Bool("preserve_collinear")) warnings.Add("preserve_collinear has no effect on offset_lines");
            if (op == "clip_lines" && args.Bool("preserve_collinear")) warnings.Add("preserve_collinear has no effect on clip_lines");
            o["warnings"] = Warnings(warnings);
            return o;
        }

        static JsonObject PolygonSummary(IList<GeneralPolygon2d> polygons)
        {
            double area = polygons.Sum(p => p.Area);
            var o = new JsonObject() {
                ["polygons"] = polygons.Count, ["holes"] = polygons.Sum(p => p.Holes.Count), ["vertices"] = polygons.Sum(p => p.VertexCount),
                ["area"] = Val(area),
            };
            if (polygons.Count > 0) {
                AxisAlignedBox2d b = polygons[0].Bounds;
                foreach (var p in polygons.Skip(1)) b.Contain(p.Bounds);
                o["bounds"] = new JsonObject() { ["min"] = new JsonArray(b.Min.x, b.Min.y), ["max"] = new JsonArray(b.Max.x, b.Max.y) };
            }
            return o;
        }
    }
}
