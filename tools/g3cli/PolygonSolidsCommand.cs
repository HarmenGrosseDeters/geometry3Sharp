// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using g3;

namespace g3cli
{
    /// <summary>
    /// GeoJSON rings as loops for polygon_solids: every ring of a Polygon or MultiPolygon (outer rings and holes alike, with their orientation
    /// as given) and every closed LineString or MultiLineString line is one loop; the nesting is found by PlanarComplex.FindSolidRegions.
    /// </summary>
    static class LoopGeoJson
    {
        public class Result
        {
            public List<Polygon2d> Loops = new List<Polygon2d>();
            public int OpenLines;
            public JsonNode Crs;
            public bool DroppedZ;
            public PolygonGeoJson.Result Counter = new PolygonGeoJson.Result();
        }

        public static Result Parse(string json, string code, string what)
        {
            if (json.Length > PolygonGeoJson.MaxTextBytes)
                throw new CommandException(code, what + " is longer than 256 million characters");
            JsonDocument doc;
            try {
                doc = JsonDocument.Parse(json, new JsonDocumentOptions() { MaxDepth = 64 });
            } catch (Exception e) when (e is JsonException || e is ArgumentException) {
                throw new CommandException(code, what + " is not valid JSON: " + e.Message);
            }
            using (doc) {
                var r = new Result();
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("crs", out JsonElement crs))
                    r.Crs = JsonNode.Parse(crs.GetRawText());
                Node(doc.RootElement, r, code, what);
                r.DroppedZ = r.Counter.DroppedZ;
                return r;
            }
        }

        public static Result ReadFile(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); } catch (Exception e) { throw new CommandException(ResultCodes.ReadError, $"cannot use '{path}': {e.Message}"); }
            if (File.Exists(full) == false || full.StartsWith("/dev/") || full.StartsWith("/proc/") || full.StartsWith("/sys/"))
                throw new CommandException(ResultCodes.ReadError, $"file not found: {path}");
            string text;
            try {
                if (new FileInfo(full).Length > PolygonGeoJson.MaxTextBytes)
                    throw new CommandException(ResultCodes.ReadError, $"'{path}' is larger than {PolygonGeoJson.MaxTextBytes / (1024 * 1024)} MB");
                text = File.ReadAllText(full);
            } catch (CommandException) {
                throw;
            } catch (Exception e) {
                throw new CommandException(ResultCodes.ReadError, $"cannot read '{path}': {e.Message}");
            }
            return Parse(text, ResultCodes.ReadError, "'" + path + "'");
        }

        static void Node(JsonElement e, Result r, string code, string what)
        {
            if (e.ValueKind != JsonValueKind.Object || e.TryGetProperty("type", out JsonElement t) == false || t.ValueKind != JsonValueKind.String)
                throw new CommandException(code, what + ": expected a GeoJSON object with a \"type\"");
            switch (t.GetString()) {
                case "FeatureCollection":
                    if (e.TryGetProperty("features", out JsonElement fs) == false || fs.ValueKind != JsonValueKind.Array)
                        throw new CommandException(code, what + ": a FeatureCollection needs a \"features\" array");
                    foreach (JsonElement f in fs.EnumerateArray()) Node(f, r, code, what);
                    break;
                case "Feature":
                    if (e.TryGetProperty("geometry", out JsonElement g) == false || g.ValueKind != JsonValueKind.Object)
                        throw new CommandException(code, what + ": a Feature needs a \"geometry\" (null geometries are not supported)");
                    Node(g, r, code, what);
                    break;
                case "Polygon":
                    foreach (JsonElement ring in Coordinates(e, code, what).EnumerateArray()) AddRing(ring, r, code, what);
                    break;
                case "MultiPolygon":
                    foreach (JsonElement poly in Coordinates(e, code, what).EnumerateArray()) {
                        if (poly.ValueKind != JsonValueKind.Array)
                            throw new CommandException(code, what + ": a polygon is an array of rings");
                        foreach (JsonElement ring in poly.EnumerateArray()) AddRing(ring, r, code, what);
                    }
                    break;
                case "LineString":
                    AddLine(Coordinates(e, code, what), r, code, what);
                    break;
                case "MultiLineString":
                    foreach (JsonElement line in Coordinates(e, code, what).EnumerateArray()) AddLine(line, r, code, what);
                    break;
                default:
                    throw new CommandException(code, what + $": geometry type '{t.GetString()}' is not supported (Polygon, MultiPolygon, LineString, MultiLineString, Feature, FeatureCollection)");
            }
        }

        static JsonElement Coordinates(JsonElement e, string code, string what)
        {
            if (e.TryGetProperty("coordinates", out JsonElement c) == false || c.ValueKind != JsonValueKind.Array)
                throw new CommandException(code, what + ": a geometry needs a \"coordinates\" array");
            return c;
        }

        static void AddRing(JsonElement ring, Result r, string code, string what)
        {
            CheckCount(r, code, what);
            r.Counter.Rings++;
            r.Loops.Add(PolygonGeoJson.Ring(ring, r.Counter, code, what, r.Loops.Count));
        }

        // a line is a loop if it is closed (first position = last, at least 4 positions); any other is counted and not used
        static void AddLine(JsonElement line, Result r, string code, string what)
        {
            if (line.ValueKind != JsonValueKind.Array)
                throw new CommandException(code, what + ": a line is an array of positions");
            int n = line.GetArrayLength();
            if ((r.Counter.Vertices += n) > PolygonGeoJson.MaxVertices)
                throw new CommandException(code, what + $": more than {PolygonGeoJson.MaxVertices} positions in all");
            // (every position is checked, also those of a line that is not used)
            var xs = new double[n]; var ys = new double[n];
            int k = 0;
            foreach (JsonElement pos in line.EnumerateArray()) {
                if (pos.ValueKind != JsonValueKind.Array || pos.GetArrayLength() < 2
                    || pos[0].ValueKind != JsonValueKind.Number || pos[1].ValueKind != JsonValueKind.Number
                    || pos[0].TryGetDouble(out xs[k]) == false || pos[1].TryGetDouble(out ys[k]) == false
                    || double.IsFinite(xs[k]) == false || double.IsFinite(ys[k]) == false)
                    throw new CommandException(code, what + ": a position is [x, y] with finite numbers");
                k++;
            }
            bool closed = n >= 4 && xs[0] == xs[n - 1] && ys[0] == ys[n - 1];
            if (closed) AddRing(line, r, code, what);
            else r.OpenLines++;
        }

        static void CheckCount(Result r, string code, string what)
        {
            if (r.Loops.Count >= SolidsMaxLoops)
                throw new CommandException(code, what + $": more than {SolidsMaxLoops} loops (the nesting search is quadratic in the number of loops: 4000 nested loops take about 10 s)");
        }

        public const int SolidsMaxLoops = 4000;
    }

    public static partial class Commands
    {
        /// <summary> the loop's vertices from its smallest vertex on, in the direction that gives the smaller sequence: equal loops have equal keys </summary>
        static string CanonicalLoop(Polygon2d loop)
        {
            int n = loop.VertexCount;
            string Key(bool reverse)
            {
                Func<int, Vector2d> at = i => loop[reverse ? (n - 1 - i) : i];
                int best = 0;
                for (int i = 1; i < n; i++) {
                    Vector2d a = at(i), b = at(best);
                    if (a.x < b.x || (a.x == b.x && a.y < b.y)) best = i;
                }
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n; i++) {
                    Vector2d v = at((best + i) % n);
                    sb.Append(PolyNum(v.x)).Append(',').Append(PolyNum(v.y)).Append(';');
                }
                return sb.ToString();
            }
            string f = Key(false), r = Key(true);
            return string.CompareOrdinal(f, r) <= 0 ? f : r;
        }

        static Command PolygonSolidsCommand()
        {
            var c = new Command() {
                Name = "polygon_solids",
                Description = "Solid regions (polygons with holes) from loops: PlanarComplex.FindSolidRegions. Every ring of the input is a loop, whether it was written as an outer " +
                    "ring or as a hole: the nesting is found from the containment of the loops (a loop inside one other loop is its hole, inside two a new solid, and so on), " +
                    "so loops from different sources, with any orientation, give the polygons with holes. Input: GeoJSON (a FeatureCollection, a Feature or a geometry) of type Polygon, " +
                    "MultiPolygon (all their rings), LineString or MultiLineString (closed lines only: first position = last, 4 or more positions; open lines are counted in " +
                    "'loops.open_lines_ignored' and not used). Coordinates are taken as given. Loops are simplified before the nesting is decided: simplify_tolerance is an " +
                    "ABSOLUTE deviation in the units of the input (library default 0.1, so loops closer together than about that distance can lose their nesting: " +
                    "choose a smaller one for small or close loops, 0 for none) and relative_tolerance one relative to each loop's own bounding box diagonal (0 = off; " +
                    "if both are given the smaller applies to each loop). The result is a GeoJSON FeatureCollection with one MultiPolygon (outer rings counterclockwise, holes clockwise), " +
                    "written to 'output' or, with return_geojson, in the result as result_geojson.",
            };
            c.Params.Add(new Param("input", ParamType.String, "loops as a GeoJSON file; or give input_geojson") { Positional = true });
            c.Params.Add(new Param("output", ParamType.String, "output GeoJSON file for the solids (optional)") { Positional = true });
            c.Params.Add(new Param("input_geojson", ParamType.String, "the loops as inline GeoJSON text, instead of a file"));
            c.Params.Add(new Param("simplify_tolerance", ParamType.Number, "absolute deviation tolerance of the simplification of each loop, in input units (0 = none)") { Default = 0.1 });
            c.Params.Add(new Param("relative_tolerance", ParamType.Number, "deviation tolerance relative to the diagonal of each loop's bounding box (0 = off)") { Default = 0.0 });
            c.Params.Add(new Param("trust_orientations", ParamType.Boolean, "a loop inside another counts as its hole only if it runs the other way (else by containment alone)") { Default = false });
            c.Params.Add(new Param("allow_overlapping_holes", ParamType.Boolean, "keep a hole that overlaps another hole of its solid (the polygon is then not simple; by default such a hole is dropped, with a warning)") { Default = false });
            c.Params.Add(new Param("return_geojson", ParamType.Boolean, "also return the result as 'result_geojson' in the JSON result") { Default = false });
            c.Run = args => {
                if (args.Has("input") == args.Has("input_geojson"))
                    throw new CommandException(ResultCodes.InvalidArguments, "give exactly one of input (a GeoJSON file) and input_geojson (inline GeoJSON)");
                double abs = args.Number("simplify_tolerance"), rel = args.Number("relative_tolerance");
                if (!(abs >= 0) || double.IsInfinity(abs))
                    throw new CommandException(ResultCodes.InvalidArguments, "'simplify_tolerance' must be a finite number >= 0");
                if (!(rel >= 0) || double.IsInfinity(rel))
                    throw new CommandException(ResultCodes.InvalidArguments, "'relative_tolerance' must be a finite number >= 0");
                LoopGeoJson.Result input = args.Has("input_geojson")
                    ? LoopGeoJson.Parse(args.String("input_geojson"), ResultCodes.InvalidArguments, "input_geojson")
                    : LoopGeoJson.ReadFile(args.String("input"));
                if (input.Loops.Count == 0)
                    throw new CommandException(ResultCodes.OperationFailed, input.OpenLines > 0
                        ? "the input has no closed loop (" + input.OpenLines + " open lines are not used)" : "the input has no loop");
                // identical loops (the same vertices from the same start, in either direction) would be two overlapping solids of the same area: one is kept
                var seen = new HashSet<string>();
                var loops = new List<Polygon2d>();
                foreach (Polygon2d loop in input.Loops)
                    if (seen.Add(CanonicalLoop(loop))) loops.Add(loop);
                int duplicates = input.Loops.Count - loops.Count;
                var complex = new PlanarComplex();
                foreach (Polygon2d loop in loops) complex.Add(loop);
                PlanarComplex.SolidRegionInfo info = complex.FindSolidRegions(new PlanarComplex.FindSolidsOptions() {
                    SimplifyDeviationTolerance = abs,
                    SimplifyRelativeTolerance = rel,
                    WantCurveSolids = false,
                    TrustOrientations = args.Bool("trust_orientations"),
                    AllowOverlappingHoles = args.Bool("allow_overlapping_holes"),
                });
                // (FindSolidRegions returns clockwise outer loops and counterclockwise holes; written the other way round, as polygon2d does)
                var solids = new List<GeneralPolygon2d>();
                foreach (GeneralPolygon2d gp in info.Polygons ?? new List<GeneralPolygon2d>()) {
                    var outer = new Polygon2d(gp.Outer);
                    if (outer.IsClockwise) outer.Reverse();
                    var ngp = new GeneralPolygon2d(outer);
                    foreach (Polygon2d hole in gp.Holes) {
                        var h = new Polygon2d(hole);
                        if (h.IsClockwise == false) h.Reverse();
                        ngp.AddHole(h, false, false);
                    }
                    solids.Add(ngp);
                }
                JsonObject geojson = PolygonGeoJson.Write(solids, input.Crs, new JsonObject() { ["source"] = "polygon_solids" });
                if (args.Has("output")) WriteText(args.String("output"), geojson.ToJsonString(new JsonSerializerOptions() { WriteIndented = false }));
                var o = new JsonObject();
                if (args.Has("input")) o["input"] = args.String("input");
                if (args.Has("output")) o["output"] = args.String("output");
                o["loops"] = new JsonObject() { ["input"] = input.Loops.Count, ["open_lines_ignored"] = input.OpenLines, ["duplicates_removed"] = duplicates };
                o["solids"] = solids.Count;
                o["holes"] = solids.Sum(p => p.Holes.Count);
                o["area"] = Val(solids.Sum(p => p.Area));
                o["holes_area"] = Val(solids.Sum(p => p.Holes.Sum(h => Math.Abs(h.SignedArea))));
                o["used_tolerance"] = new JsonObject() { ["absolute"] = abs, ["relative"] = rel };
                o["result"] = PolygonSummary(solids);
                if (args.Bool("return_geojson")) o["result_geojson"] = geojson;
                var warnings = new List<string>();
                if (input.DroppedZ) warnings.Add("third coordinates (z) of the positions are ignored");
                if (input.OpenLines > 0) warnings.Add($"{input.OpenLines} open lines are not loops and were ignored");
                if (duplicates > 0) warnings.Add($"{duplicates} loops were identical to another one and were removed");
                int lost = loops.Count - (solids.Count + o["holes"].GetValue<int>());
                if (lost > 0) warnings.Add($"{lost} of {loops.Count} loops are not part of any solid (dropped by the simplification or as degenerate, or an island rejected as a hole)");
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }
    }
}
