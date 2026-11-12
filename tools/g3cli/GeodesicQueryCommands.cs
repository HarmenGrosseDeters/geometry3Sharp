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
    /// <summary>
    /// geodesic (MeshGeodesicDistance, MeshGeodesicPath) and query3d (MeshBatchQueries). Vertex and triangle ids are
    /// those of the mesh as g3cli reads it (0-based, in file order for OBJ and PLY; several meshes in one file are appended).
    /// </summary>
    public static partial class Commands
    {
        const int MaxIds = 100000, MaxQueries = 1000000;

        const int MaxTextChars = 32 * 1024 * 1024;

        static List<int> ParseIds(string text, string name)
        {
            if (text.Length > 20 * MaxIds)
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': at most {MaxIds} ids");
            var ids = new List<int>();
            foreach (string part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
                if (int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id) == false)
                    throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': '{part}' is not a vertex id (0, 1, 2, ...)");
                ids.Add(id);
                if (ids.Count > MaxIds)
                    throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': at most {MaxIds} ids");
            }
            if (ids.Count == 0)
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}' is empty");
            return ids;
        }

        static void CheckVertexIds(DMesh3 mesh, IEnumerable<int> ids, string name)
        {
            foreach (int id in ids)
                if (mesh.IsVertex(id) == false)
                    throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': {id} is not a vertex of the mesh (0..{mesh.MaxVertexID - 1})");
        }


        // ---- geodesic ----

        static JsonObject DistanceStats(List<double> values)
        {
            if (values.Count == 0) return null;
            DeviationStatistics s = DeviationStatistics.Compute(values);
            return new JsonObject() {
                ["count"] = s.Count, ["min"] = Val(s.Min), ["max"] = Val(s.Max), ["mean"] = Val(s.Mean),
                ["median"] = Val(s.Median), ["std_dev"] = Val(s.StdDev),
            };
        }

        static Command Geodesic()
        {
            var c = new Command() {
                Name = "geodesic",
                Description = "Geodesics on a triangle mesh. Two modes. Distance (give 'sources'): geodesic distance from the nearest of the " +
                    "source vertices to every vertex by the heat method (MeshGeodesicDistance, about 2 % plus half an edge length accurate), as " +
                    "statistics, and at the vertices in 'targets'. Path (give 'from' and 'to', or 'through' for waypoints, optionally 'closed'): the " +
                    "geodesic path by edge flips (MeshGeodesicPath, FlipOut), its length, the vertices it passes and its polyline on the surface. " +
                    "Vertex ids are 0-based in the order of the mesh as read. The per-vertex values are not written.",
            };
            c.Params.Add(Input());
            c.Params.Add(new Param("sources", ParamType.String, "distance mode: source vertex ids 'a,b,c'"));
            c.Params.Add(new Param("targets", ParamType.String, "distance mode: vertex ids whose distance is listed 'a,b,c'"));
            c.Params.Add(new Param("from", ParamType.Integer, "path mode: start vertex id (with 'to')"));
            c.Params.Add(new Param("to", ParamType.Integer, "path mode: end vertex id (with 'from')"));
            c.Params.Add(new Param("through", ParamType.String, "path mode: waypoint vertex ids 'a,b,c' (kept fixed), instead of from / to"));
            c.Params.Add(new Param("closed", ParamType.Boolean, "path mode: also back from the last vertex to the first (with 'through' or 'from' / 'to')") { Default = false });
            c.Params.Add(new Param("intrinsic_delaunay", ParamType.Boolean, "distance mode: compute on the intrinsic Delaunay triangulation (library default true; robust for needles and caps)"));
            c.Params.Add(new Param("time_factor", ParamType.Number, "distance mode: heat time as a multiple of the squared mean edge length (library default 1)"));
            c.Params.Add(new Param("max_results", ParamType.Integer, "path mode: at most this many points / vertices in the lists (the counts are always given)") { Default = 1000 });
            c.Run = args => {
                bool distanceMode = args.Has("sources");
                bool pathMode = args.Has("from") || args.Has("to") || args.Has("through");
                if (distanceMode == pathMode)
                    throw new CommandException(ResultCodes.InvalidArguments, "give 'sources' (distance), or 'from' and 'to' / 'through' (path)");
                int maxResults = args.Integer("max_results");
                if (maxResults < 0 || maxResults > 100000)
                    throw new CommandException(ResultCodes.InvalidArguments, "'max_results' must be in 0..100000");
                if (distanceMode && (args.Has("closed") && args.Bool("closed")))
                    throw new CommandException(ResultCodes.InvalidArguments, "'closed' is an option of the path mode");
                if (pathMode && (args.Has("targets") || args.Has("intrinsic_delaunay") || args.Has("time_factor")))
                    throw new CommandException(ResultCodes.InvalidArguments, "'targets', 'intrinsic_delaunay' and 'time_factor' are options of the distance mode");
                List<int> sources = null, targets = null, waypoints = null;
                if (distanceMode) {
                    sources = ParseIds(args.String("sources"), "sources");
                    if (args.Has("targets")) targets = ParseIds(args.String("targets"), "targets");
                    if (args.Has("time_factor") && !(args.Number("time_factor") > 0 && args.Number("time_factor") <= 1e6))
                        throw new CommandException(ResultCodes.InvalidArguments, "'time_factor' must be in (0, 1e6]");
                } else {
                    if (args.Has("through") == (args.Has("from") || args.Has("to")))
                        throw new CommandException(ResultCodes.InvalidArguments, "give 'from' and 'to', or 'through'");
                    if (args.Has("through")) waypoints = ParseIds(args.String("through"), "through");
                    else if (args.Has("from") && args.Has("to")) waypoints = new List<int> { args.Integer("from"), args.Integer("to") };
                    else throw new CommandException(ResultCodes.InvalidArguments, "give both 'from' and 'to'");
                    if (waypoints.Count > 1000)
                        throw new CommandException(ResultCodes.InvalidArguments, "a path has at most 1000 waypoints (each leg is a search over the mesh)");
                    if (waypoints.Count < 2)
                        throw new CommandException(ResultCodes.InvalidArguments, "a path needs at least two vertices");
                    if (waypoints.Any(w => w < 0))
                        throw new CommandException(ResultCodes.InvalidArguments, "vertex ids are >= 0");
                }
                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                CheckVertexIds(mesh, (sources ?? waypoints).Concat(targets ?? new List<int>()), distanceMode ? "sources / targets" : "path");
                var o = new JsonObject() { ["input"] = args.String("input"), ["mode"] = distanceMode ? "distance" : "path" };
                if (distanceMode) {
                    var geo = new MeshGeodesicDistance(mesh);
                    if (args.Has("intrinsic_delaunay")) geo.IntrinsicDelaunay = args.Bool("intrinsic_delaunay");
                    if (args.Has("time_factor")) geo.TimeFactor = args.Number("time_factor");
                    if (geo.Compute(sources) == false)
                        throw new CommandException(ResultCodes.OperationFailed, "geodesic distance failed (no valid source, or the solver failed)");
                    double[] d = geo.Distance;
                    var values = mesh.VertexIndices().Select(v => d[v]).ToList();
                    int unreached = values.Count(double.IsNaN);
                    o["sources"] = new JsonArray(sources.Distinct().Select(s => (JsonNode)s).ToArray());
                    o["distance_statistics"] = DistanceStats(values.Where(v => !double.IsNaN(v)).ToList());
                    o["vertices_without_distance"] = unreached;
                    if (unreached > 0)
                        warnings.Add($"{unreached} vertices have no distance (components without a source, isolated vertices): null");
                    o["intrinsic_flips"] = geo.IntrinsicFlips;
                    if (targets != null)
                        o["targets"] = new JsonArray(targets.Select(t => (JsonNode)new JsonObject() { ["vertex"] = t, ["distance"] = Val(d[t]) }).ToArray());
                } else {
                    var gp = new MeshGeodesicPath(mesh);
                    GeodesicPathStatus status = gp.ComputeThrough(waypoints, args.Bool("closed"));
                    string name = ToSnake(status.ToString());
                    bool usable = status == GeodesicPathStatus.Success || status == GeodesicPathStatus.Collapsed;
                    if (status == GeodesicPathStatus.InvalidInput || status == GeodesicPathStatus.NoPath)
                        throw new CommandException(ResultCodes.OperationFailed, "geodesic path failed: " + name, new JsonObject() { ["path_status"] = name });
                    o["path_status"] = name;
                    if (usable == false)
                        warnings.Add($"path status {name}: the path is valid but " + (status == GeodesicPathStatus.NotConverged ? "not yet a geodesic"
                            : status == GeodesicPathStatus.Stuck ? "a vertex with an angle below pi could not be shortened" : "may cut across the surface"));
                    o["length"] = Val(gp.Length);
                    o["initial_length"] = Val(gp.InitialLength);
                    o["iterations"] = gp.Iterations; o["flips"] = gp.Flips;
                    o["closed"] = args.Bool("closed");
                    o["path_vertex_count"] = gp.PathVertices.Count;
                    o["path_vertices"] = new JsonArray(gp.PathVertices.Take(maxResults).Select(v => (JsonNode)v).ToArray());
                    o["path_point_count"] = gp.Path.Count;
                    o["path"] = new JsonArray(gp.Path.Take(maxResults).Select(p => (JsonNode)MeshReport.Vec(p)).ToArray());
                    o["path_truncated"] = gp.Path.Count > maxResults || gp.PathVertices.Count > maxResults;
                }
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }


        // ---- query3d ----

        /// <summary> numbers 'a,b,c;d,e,f' (';' or a new line between the tuples, ',' or a blank within); '@file' reads the text from a file </summary>
        static List<double[]> ParseTuples(string text, string name, int size)
        {
            if (text.StartsWith("@")) {
                string file = text.Substring(1);
                // a regular file of bounded size only: not a device, a pipe or stdin (they would block or never end)
                string full;
                try { full = Path.GetFullPath(file); } catch (Exception e) { throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': cannot use '{file}': {e.Message}"); }
                if (full.StartsWith("/dev/") || full.StartsWith("/proc/") || full.StartsWith("/sys/") || File.Exists(full) == false)
                    throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': file not found: {file}");
                try {
                    if (new FileInfo(full).Length > MaxTextChars)
                        throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': the file is larger than {MaxTextChars / (1024 * 1024)} MB");
                    text = File.ReadAllText(full);
                } catch (IOException e) { throw new CommandException(ResultCodes.ReadError, $"'{name}': cannot read '{file}': {e.Message}"); }
                catch (UnauthorizedAccessException e) { throw new CommandException(ResultCodes.ReadError, $"'{name}': cannot read '{file}': {e.Message}"); }
            } else if (text.Length > MaxTextChars)
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': longer than {MaxTextChars} characters");
            if (text.TrimStart().StartsWith("["))
                return ParseJsonTuples(text, name, size);
            var list = new List<double[]>();
            foreach (string part in text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0)) {
                double[] v;
                try { v = ParseNumbers(part, name); }
                catch (CommandException) { throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': entry {list.Count + 1} is not a list of finite numbers"); }
                if (v.Length != size)
                    throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': entry {list.Count + 1} needs {size} numbers");
                list.Add(v);
                if (list.Count > MaxQueries)
                    throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': at most {MaxQueries} entries");
            }
            if (list.Count == 0)
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}' is empty");
            return list;
        }

        /// <summary>
        /// the JSON form of a list: [[x, y, z], ...] for points, [[ox, oy, oz, dx, dy, dz], ...] or
        /// [{"origin": [x, y, z], "direction": [x, y, z]}, ...] for rays
        /// </summary>
        static List<double[]> ParseJsonTuples(string text, string name, int size)
        {
            JsonArray array;
            try { array = JsonNode.Parse(text) as JsonArray; }
            catch (Exception e) when (e is System.Text.Json.JsonException || e is ArgumentException) { throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': not valid JSON: {e.Message}"); }
            if (array == null)
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': a JSON list was expected");
            if (array.Count > MaxQueries)
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': at most {MaxQueries} entries");
            if (array.Count == 0)
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}' is empty");
            var list = new List<double[]>(array.Count);
            // (a duplicate key in an object throws ArgumentException when the object is first read)
            try {
            for (int i = 0; i < array.Count; i++) {
                string bad = $"'{name}': entry {i + 1} is not a list of {size} finite numbers" + (size == 6 ? " or an object with 'origin' and 'direction'" : "");
                JsonNode e = array[i];
                var numbers = new List<double>();
                if (size == 6 && e is JsonObject obj) {
                    foreach (var kv in obj)
                        if (kv.Key != "origin" && kv.Key != "direction")
                            throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': entry {i + 1} has an unknown key '{kv.Key}' (known: origin, direction)");
                    foreach (string key in new[] { "origin", "direction" }) {
                        if (!(obj[key] is JsonArray part) || part.Count != 3) throw new CommandException(ResultCodes.InvalidArguments, bad);
                        AddJsonNumbers(part, numbers, bad);
                    }
                } else if (e is JsonArray arr) {
                    AddJsonNumbers(arr, numbers, bad);
                } else throw new CommandException(ResultCodes.InvalidArguments, bad);
                if (numbers.Count != size)
                    throw new CommandException(ResultCodes.InvalidArguments, bad);
                list.Add(numbers.ToArray());
            }
            } catch (ArgumentException e) {
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': invalid JSON object: {e.Message}");
            }
            return list;
        }

        static void AddJsonNumbers(JsonArray array, List<double> into, string message)
        {
            foreach (JsonNode n in array) {
                double d;
                try { d = n is JsonValue v ? v.GetValue<double>() : double.NaN; }
                catch (Exception e) when (e is InvalidOperationException || e is FormatException) { d = double.NaN; }
                if (!double.IsFinite(d)) throw new CommandException(ResultCodes.InvalidArguments, message);
                into.Add(d);
            }
        }

        // ---- the per-item results of query3d as a CSV or JSON file ----

        static readonly HashSet<string> Vec3Columns = new HashSet<string> { "point", "closest_point", "barycentric" };

        static void FlattenEntry(JsonObject entry, string prefix, List<KeyValuePair<string, string>> into)
        {
            foreach (var kv in entry) {
                string key = prefix + kv.Key;
                if (kv.Value is JsonObject inner) FlattenEntry(inner, key + "_", into);
                else if (kv.Value is JsonArray arr || (kv.Value == null && Vec3Columns.Contains(kv.Key))) {
                    string[] names = { "x", "y", "z" };
                    for (int i = 0; i < 3; i++)
                        into.Add(new KeyValuePair<string, string>(key + "_" + names[i], kv.Value == null ? "" : kv.Value[i].ToJsonString()));
                } else if (kv.Value == null) into.Add(new KeyValuePair<string, string>(key, ""));
                else if (kv.Value is JsonValue val && val.TryGetValue(out string str)) into.Add(new KeyValuePair<string, string>(key, str));
                else into.Add(new KeyValuePair<string, string>(key, kv.Value.ToJsonString()));
            }
        }

        /// <summary> the entry of a result list with its index in the input list first (the files hold every entry, so they need it) </summary>
        static JsonObject WithIndex(int index, JsonObject entry)
        {
            var o = new JsonObject() { ["index"] = index };
            foreach (var kv in entry.ToList()) { entry.Remove(kv.Key); o[kv.Key] = kv.Value; }
            return o;
        }

        /// <summary> checks the extension of the query output file (.csv or .json) before any work </summary>
        static string QueryOutputFormat(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".csv" && ext != ".json")
                throw new CommandException(ResultCodes.UnknownFormat, $"cannot write '{path}': the query results are written as .csv or .json");
            return ext.Substring(1);
        }

        static void WriteQueryOutput(string path, string format, IEnumerable<JsonObject> entries)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (Directory.Exists(dir) == false)
                throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': directory does not exist");
            // (written next to the target and moved over it: a failure leaves neither a partial file nor a destroyed old one)
            string temp = path + ".g3tmp";
            try {
                using (var w = new StreamWriter(temp, false, new System.Text.UTF8Encoding(false))) {
                    if (format == "json") {
                        w.Write('[');
                        bool first = true;
                        foreach (JsonObject e in entries) {
                            w.Write(first ? "\n" : ",\n");
                            w.Write(e.ToJsonString());
                            first = false;
                        }
                        w.Write("\n]\n");
                    } else {
                        bool header = true;
                        var cells = new List<KeyValuePair<string, string>>();
                        foreach (JsonObject e in entries) {
                            cells.Clear();
                            FlattenEntry(e, "", cells);
                            if (header) { w.Write(string.Join(",", cells.Select(c => c.Key)) + "\n"); header = false; }
                            w.Write(string.Join(",", cells.Select(c => c.Value)) + "\n");
                        }
                    }
                }
                File.Move(temp, path, true);
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                try { File.Delete(temp); } catch (Exception) { }
                throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': {e.Message}");
            }
        }

        static Command Query3d()
        {
            var c = new Command() {
                Name = "query3d",
                Description = "Batch queries against a triangle mesh (MeshBatchQueries over a DMeshAABBTree3; each result equals the single query's, " +
                    "and does not depend on the order of the queries or on 'parallel'). 'rays' (origin and direction 'ox,oy,oz,dx,dy,dz;...'; " +
                    "the direction is normalized here): the nearest hit (triangle, ray parameter t in mesh units, point), front and back faces, " +
                    "or only whether there is a hit (any_hit). 'points' ('x,y,z;...'): with 'nearest' the closest triangle, point, barycentric " +
                    "coordinates and distance; with 'inside' whether the point is inside (crossings: exact parity, closed meshes without " +
                    "self-intersections; winding_number: any mesh, approximate near the surface). A list may be '@file' (entries separated by ';' or lines). " +
                    "Triangle ids are those of the mesh as read. Lists in the result are cut at max_results; the counts are not.",
            };
            c.Params.Add(Input());
            c.Params.Add(new Param("rays", ParamType.String, "rays 'ox,oy,oz,dx,dy,dz;...' (or @file)"));
            c.Params.Add(new Param("points", ParamType.String, "points 'x,y,z;...' (or @file)"));
            c.Params.Add(new Param("nearest", ParamType.Boolean, "points: the nearest triangle, closest point and distance") { Default = false });
            c.Params.Add(new Param("inside", ParamType.String, "points: inside / outside test by this method; auto: crossings if the mesh is closed and has no boundary, else winding_number (the result's method_used says which)") { Enum = new[] { "crossings", "winding_number", "auto" } });
            c.Params.Add(new Param("any_hit", ParamType.Boolean, "rays: only whether each ray hits (no triangle, t or point)") { Default = false });
            c.Params.Add(new Param("max_distance", ParamType.Number, "only hits / triangles strictly closer than this count (default: no limit)"));
            c.Params.Add(new Param("parallel", ParamType.Boolean, "use all cores (the result does not depend on it)") { Default = true });
            c.Params.Add(new Param("max_results", ParamType.Integer, "at most this many entries in each result list") { Default = 1000 });
            c.Params.Add(new Param("normalize", ParamType.Boolean, "rays: normalize the directions (false: a direction that is not a unit vector gets the status not_normalized and no hit)") { Default = true });
            c.Params.Add(new Param("output", ParamType.String, "write every result (not cut at max_results) to this .csv or .json file; for rays or for points, not both"));
            c.Run = args => {
                if (args.Has("rays") == false && args.Has("points") == false)
                    throw new CommandException(ResultCodes.InvalidArguments, "give rays or points (or both)");
                if (args.Bool("any_hit") && args.Has("rays") == false)
                    throw new CommandException(ResultCodes.InvalidArguments, "'any_hit' needs 'rays'");
                if (args.Has("points") && args.Bool("nearest") == false && args.Has("inside") == false)
                    throw new CommandException(ResultCodes.InvalidArguments, "points need 'nearest' or 'inside'");
                if ((args.Bool("nearest") || args.Has("inside")) && args.Has("points") == false)
                    throw new CommandException(ResultCodes.InvalidArguments, "'nearest' and 'inside' need 'points'");
                int maxResults = args.Integer("max_results");
                if (maxResults < 0 || maxResults > 100000)
                    throw new CommandException(ResultCodes.InvalidArguments, "'max_results' must be in 0..100000");
                if (args.Has("normalize") && args.Bool("normalize") == false && args.Has("rays") == false)
                    throw new CommandException(ResultCodes.InvalidArguments, "'normalize' is an option of 'rays'");
                string outputFormat = null;
                if (args.Has("output")) {
                    if (args.Has("rays") && args.Has("points"))
                        throw new CommandException(ResultCodes.InvalidArguments, "'output' is for rays or for points, not both: run the command twice");
                    outputFormat = QueryOutputFormat(args.String("output"));
                    string outFull = Path.GetFullPath(args.String("output"));
                    foreach (string list in new[] { "rays", "points" })
                        if (args.Has(list) && args.String(list).StartsWith("@") && string.Equals(Path.GetFullPath(args.String(list).Substring(1)), outFull, StringComparison.Ordinal))
                            throw new CommandException(ResultCodes.InvalidArguments, $"'output' is the '{list}' file");
                }
                double maxDistance = double.MaxValue;
                if (args.Has("max_distance")) {
                    maxDistance = args.Number("max_distance");
                    if (!(maxDistance > 0) || double.IsInfinity(maxDistance))
                        throw new CommandException(ResultCodes.InvalidArguments, "'max_distance' must be a finite number > 0");
                }
                List<double[]> rayList = args.Has("rays") ? ParseTuples(args.String("rays"), "rays", 6) : null;
                List<double[]> pointList = args.Has("points") ? ParseTuples(args.String("points"), "points", 3) : null;
                Ray3d[] rays = null;
                if (rayList != null) {
                    rays = new Ray3d[rayList.Count];
                    for (int i = 0; i < rays.Length; i++) {
                        double scale = Math.Max(Math.Abs(rayList[i][3]), Math.Max(Math.Abs(rayList[i][4]), Math.Abs(rayList[i][5])));
                        if (!(scale > 0))
                            throw new CommandException(ResultCodes.InvalidArguments, $"'rays': ray {i + 1} has a zero direction");
                        // (scaled by the largest component first: a direction of 1e-200 or 1e200 normalizes as any other)
                        var dir = new Vector3d(rayList[i][3] / scale, rayList[i][4] / scale, rayList[i][5] / scale);
                        // (normalize false: the direction as given, a non-unit one is not_normalized)
                        rays[i] = new Ray3d(new Vector3d(rayList[i][0], rayList[i][1], rayList[i][2]),
                            args.Bool("normalize") ? dir.Normalized : new Vector3d(rayList[i][3], rayList[i][4], rayList[i][5]),
                            bIsNormalized: !args.Bool("normalize"));
                    }
                }
                Vector3d[] points = pointList?.Select(p => new Vector3d(p[0], p[1], p[2])).ToArray();

                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                var tree = new DMeshAABBTree3(mesh, true);
                var options = new BatchOptions() { Parallel = args.Bool("parallel"), MaxDistance = maxDistance };
                var o = new JsonObject() { ["input"] = args.String("input"), ["triangles"] = mesh.TriangleCount };

                if (rays != null) {
                    var status = new RayStatus[rays.Length];
                    BatchStatus bs;
                    var rr = new JsonObject() { ["count"] = rays.Length };
                    Func<int, JsonObject> rayEntry;
                    if (args.Bool("any_hit")) {
                        var hit = new bool[rays.Length];
                        bs = MeshBatchQueries.AnyHitBatch(tree, rays, hit, status, options);
                        BatchOk(bs);
                        rr["hits"] = hit.Count(h => h);
                        rayEntry = i => new JsonObject() { ["hit"] = hit[i], ["status"] = ToSnake(status[i].ToString()) };
                    } else {
                        var hits = new RayHit[rays.Length];
                        bs = MeshBatchQueries.IntersectBatch(tree, rays, hits, status, options);
                        BatchOk(bs);
                        rr["hits"] = hits.Count(h => h.Triangle != DMesh3.InvalidID);
                        rayEntry = i => {
                            bool h = hits[i].Triangle != DMesh3.InvalidID;
                            return new JsonObject() {
                                ["triangle"] = h ? hits[i].Triangle : (JsonNode)null,
                                ["t"] = h ? Val(hits[i].T) : null,
                                ["point"] = h ? MeshReport.Vec(rays[i].PointAt(hits[i].T)) : null,
                                ["status"] = ToSnake(status[i].ToString()),
                            };
                        };
                    }
                    rr["results"] = new JsonArray(Enumerable.Range(0, rays.Length).Take(maxResults).Select(i => (JsonNode)rayEntry(i)).ToArray());
                    if (!args.Has("points") && outputFormat != null) {
                        WriteQueryOutput(args.String("output"), outputFormat, Enumerable.Range(0, rays.Length).Select(i => WithIndex(i, rayEntry(i))));
                        o["output"] = args.String("output"); o["output_format"] = outputFormat; o["output_rows"] = rays.Length;
                    }
                    rr["truncated"] = rays.Length > maxResults;
                    int badRays = status.Count(x => x != RayStatus.Ok);
                    rr["not_ok"] = badRays;
                    if (badRays > 0) warnings.Add($"{badRays} rays could not be answered (status not ok: origin or direction not finite or not normalized); they have no hit");
                    o["rays"] = rr;
                }
                if (points != null) {
                    var pr = new JsonObject() { ["count"] = points.Length };
                    NearestPointResult[] res = null;
                    PointStatus[] ps = null;
                    InsideStatus[] inside = null;
                    if (args.Bool("nearest")) {
                        res = new NearestPointResult[points.Length];
                        ps = new PointStatus[points.Length];
                        BatchOk(MeshBatchQueries.NearestPointBatch(tree, points, res, ps, options));
                        pr["nearest_found"] = res.Count(r => r.Triangle != DMesh3.InvalidID);
                    }
                    if (args.Has("inside")) {
                        string insideMode = args.String("inside");
                        // auto: the exact crossings test needs a closed mesh without boundary; any other mesh gets the winding number
                        bool closed = insideMode != "winding_number" && MeshIterators.BoundaryEdges(mesh).Any() == false && (mesh.TriangleCount == 0 || ExactMeshBoolean.IsWindingClosed(mesh));
                        string used = insideMode == "auto" ? (closed ? "crossings" : "winding_number") : insideMode;
                        if (insideMode == "crossings" && !closed)
                            warnings.Add("inside by crossings is exact only for a closed mesh without self-intersections; this mesh is not closed: use inside=winding_number (or auto)");
                        if (insideMode == "auto" && !closed)
                            warnings.Add("the mesh is not closed: inside uses the winding number (approximate near the surface)");
                        inside = new InsideStatus[points.Length];
                        var method = used == "crossings" ? InsideMethod.Crossings : InsideMethod.WindingNumber;
                        BatchOk(MeshBatchQueries.IsInsideBatch(tree, points, inside, method, options));
                        pr["inside_count"] = inside.Count(s => s == InsideStatus.Inside);
                        pr["method_used"] = used;
                    }
                    Func<int, JsonObject> pointEntry = i => {
                        var e = new JsonObject() { ["point"] = MeshReport.Vec(points[i]) };
                        if (res != null) {
                            bool f = res[i].Triangle != DMesh3.InvalidID;
                            e["nearest"] = new JsonObject() {
                                ["status"] = ToSnake(ps[i].ToString()),
                                ["triangle"] = f ? res[i].Triangle : (JsonNode)null,
                                ["closest_point"] = f ? MeshReport.Vec(res[i].Point) : null,
                                ["barycentric"] = f ? MeshReport.Vec(res[i].Bary) : null,
                                ["distance"] = f ? Val(res[i].Distance) : null,
                            };
                        }
                        if (inside != null)
                            e["inside"] = ToSnake(inside[i].ToString());
                        return e;
                    };
                    pr["results"] = new JsonArray(Enumerable.Range(0, points.Length).Take(maxResults).Select(i => (JsonNode)pointEntry(i)).ToArray());
                    pr["truncated"] = points.Length > maxResults;
                    o["points"] = pr;
                    if (outputFormat != null) {
                        WriteQueryOutput(args.String("output"), outputFormat, Enumerable.Range(0, points.Length).Select(i => WithIndex(i, pointEntry(i))));
                        o["output"] = args.String("output"); o["output_format"] = outputFormat; o["output_rows"] = points.Length;
                    }
                }
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }

        static void BatchOk(BatchStatus status)
        {
            if (status != BatchStatus.Ok)
                throw new CommandException(ResultCodes.OperationFailed, "batch query failed: " + ToSnake(status.ToString()));
        }
    }
}
