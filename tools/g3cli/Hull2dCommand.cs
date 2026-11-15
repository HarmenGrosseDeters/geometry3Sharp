// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using g3;

namespace g3cli
{
    /// <summary>
    /// hull2d: the convex hull of 2D points (ConvexHull2; docs/design/CLI_MCP_UPDATE.md section 8, owner decision Q3: the query types double and int64;
    /// ConvexHull2 has no exact mode).
    /// </summary>
    public static partial class Commands
    {
        static Command Hull2d()
        {
            var c = new Command() {
                Name = "hull2d",
                Description = "Convex hull of points in the plane (ConvexHull2). Give 'points' ('x,y;x,y;...', '@file', or JSON [[x,y],...]) or a mesh file as 'input' (the x and y of its vertices, " +
                    "z ignored; vertex ids as read). query double (default) uses floating point predicates, which are not robust in every case; int64 rounds the points to a 2^20 grid of their " +
                    "bounding square first (the grid follows the extent: huge extents drop more) (fast, loses precision: the hull is the hull of the rounded points, so indices can differ from double near collinear points). There is no exact mode. " +
                    "The result has the dimension of the hull (2: a polygon, 1: all points on a line, 0: one point or an extent below epsilon), its vertices counterclockwise with their " +
                    "indices into the input, area and perimeter (a hull of dimension 1 has area 0 and the perimeter twice its length). 'epsilon' (default 1e-8) only decides " +
                    "whether the points are on a line or at one point: the extent below epsilon is an absolute length, the deviation from a line is judged relative to the extent. At most 50000 points (the library takes quadratic time when most points are on the hull, eg on a circle). Lists in the result are cut at max_results; the counts are not.",
            };
            c.Params.Add(new Param("input", ParamType.String, "mesh file whose vertices (x, y) are the points (" + string.Join(", ", MeshFiles.ReadFormats) + "); or give points") { Positional = true });
            c.Params.Add(new Param("points", ParamType.String, "points 'x,y;x,y;...', '@file' (one point per line) or JSON [[x,y],...]"));
            c.Params.Add(new Param("query", ParamType.String, "predicates: double (floating point) or int64 (points rounded to a 2^20 grid)") { Default = "double", Enum = new[] { "double", "int64" } });
            c.Params.Add(new Param("epsilon", ParamType.Number, "an extent below this absolute length counts as one point; points that deviate from a line by less than epsilon times the extent count as on a line; must be > 0 (the library takes every point set as one point with 0), default 1e-8") { Default = 1e-8 });
            c.Params.Add(new Param("output", ParamType.String, "write the hull as GeoJSON (a Polygon, a LineString or a Point) to this file"));
            c.Params.Add(new Param("return_geojson", ParamType.Boolean, "also return the hull as 'hull_geojson' in the result") { Default = false });
            c.Params.Add(new Param("max_results", ParamType.Integer, "at most this many hull vertices in the lists") { Default = 1000 });
            c.Run = args => {
                if (args.Has("input") == args.Has("points"))
                    throw new CommandException(ResultCodes.InvalidArguments, "give exactly one of input (a mesh file) and points");
                double epsilon = args.Number("epsilon");
                if (!(epsilon > 0) || double.IsInfinity(epsilon))
                    throw new CommandException(ResultCodes.InvalidArguments, "'epsilon' must be a finite number > 0");
                int maxResults = args.Integer("max_results");
                if (maxResults < 0 || maxResults > 100000)
                    throw new CommandException(ResultCodes.InvalidArguments, "'max_results' must be in 0..100000");
                var warnings = new List<string>();
                var pts = new List<Vector2d>();
                const int MaxHullPoints = 50000;
                if (args.Has("points")) {
                    var list = ParseTuples(args.String("points"), "points", 2);
                    if (list.Count > MaxHullPoints)
                        throw new CommandException(ResultCodes.InvalidArguments, $"'points': at most {MaxHullPoints} points (the hull takes quadratic time when most points are on it)");
                    foreach (double[] p in list) pts.Add(new Vector2d(p[0], p[1]));
                } else {
                    DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings, allowEmpty: true);
                    foreach (int v in mesh.VertexIndices()) { Vector3d p = mesh.GetVertex(v); pts.Add(new Vector2d(p.x, p.y)); }
                    if (pts.Count == 0)
                        throw new CommandException(ResultCodes.EmptyMesh, "the input has no vertices");
                    if (pts.Count > MaxHullPoints)
                        throw new CommandException(ResultCodes.InvalidArguments, $"the input has {pts.Count} vertices, at most {MaxHullPoints} (the hull takes quadratic time when most points are on it)");
                    if (pts.Any(p => !p.IsFinite))
                        throw new CommandException(ResultCodes.InvalidArguments, "the input has a vertex that is not finite");
                }
                string query = args.String("query");
                ConvexHull2 hull;
                try {
                    hull = new ConvexHull2(pts, epsilon, query == "int64" ? QueryNumberType.QT_INT64 : QueryNumberType.QT_DOUBLE);
                } catch (ArgumentException e) {
                    throw new CommandException(ResultCodes.InvalidArguments, "hull2d: " + e.Message);
                }
                int dim = hull.Dimension;
                var indices = new List<int>();
                if (dim == 2) {
                    indices.AddRange(hull.HullIndices);
                    // (ConvexHull2 returns the edges counterclockwise; the tests check it on random sets)
                } else if (dim == 1) {
                    // the two extreme points along the line
                    hull.Get1DHullInfo(out Vector2d origin, out Vector2d direction);
                    int lo = 0, hi = 0;
                    double dlo = double.MaxValue, dhi = double.MinValue;
                    for (int i = 0; i < pts.Count; i++) {
                        double t = (pts[i] - origin).Dot(direction);
                        if (t < dlo) { dlo = t; lo = i; }
                        if (t > dhi) { dhi = t; hi = i; }
                    }
                    indices.Add(lo); indices.Add(hi);
                } else
                    indices.Add(0);
                var polygon = indices.Select(i => pts[i]).ToList();
                double area = 0, perimeter = 0;
                if (dim == 2) {
                    var poly = new Polygon2d(polygon);
                    area = Math.Abs(poly.SignedArea);
                    perimeter = poly.ArcLength;
                } else if (dim == 1)
                    perimeter = 2 * polygon[0].Distance(polygon[1]);

                var o = new JsonObject() {
                    ["source"] = args.Has("points") ? "points" : "mesh", ["points"] = pts.Count, ["query"] = query, ["epsilon"] = epsilon,
                    ["dimension"] = dim, ["hull_vertices"] = indices.Count, ["area"] = Val(area), ["perimeter"] = Val(perimeter),
                    ["indices"] = new JsonArray(indices.Take(maxResults).Select(i => (JsonNode)i).ToArray()),
                    ["polygon"] = new JsonArray(polygon.Take(maxResults).Select(p => (JsonNode)new JsonArray(p.x, p.y)).ToArray()),
                    ["truncated"] = indices.Count > maxResults,
                };
                if (args.Has("points") == false) o["input"] = args.String("input");
                if (args.Has("output") || args.Bool("return_geojson")) {
                    JsonObject geometry;
                    var coords = new JsonArray(polygon.Select(p => (JsonNode)new JsonArray(p.x, p.y)).ToArray());
                    if (dim == 2) {
                        var ring = new JsonArray(polygon.Select(p => (JsonNode)new JsonArray(p.x, p.y)).ToArray()) { new JsonArray(polygon[0].x, polygon[0].y) };
                        geometry = new JsonObject() { ["type"] = "Polygon", ["coordinates"] = new JsonArray(ring) };
                    } else if (dim == 1)
                        geometry = new JsonObject() { ["type"] = "LineString", ["coordinates"] = coords };
                    else
                        geometry = new JsonObject() { ["type"] = "Point", ["coordinates"] = new JsonArray(polygon[0].x, polygon[0].y) };
                    var feature = new JsonObject() { ["type"] = "Feature", ["properties"] = new JsonObject() { ["dimension"] = dim, ["query"] = query }, ["geometry"] = geometry };
                    if (args.Has("output")) {
                        WriteText(args.String("output"), feature.ToJsonString(new JsonSerializerOptions() { WriteIndented = false }));
                        o["output"] = args.String("output");
                    }
                    if (args.Bool("return_geojson")) o["hull_geojson"] = feature;
                }
                if (!double.IsFinite(area) || !double.IsFinite(perimeter))
                    warnings.Add("the area or the perimeter exceeds the double range (the value is \"infinity\"); the hull itself is valid");
                if (query == "int64" && dim == 2)
                    warnings.Add("query int64 rounds the points to a 2^20 grid of their bounding square: the hull is that of the rounded points");
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }
    }
}
