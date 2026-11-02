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
    /// The 2D mesher input as JSON (the "mesh2d" command): points, segments, rings, hole seeds, regions and heights of a
    /// ConstrainedMesh2Input. Failures are CommandExceptions with the code given to the parser (read_error for a file,
    /// invalid_arguments for inline JSON); never another exception.
    /// </summary>
    public static class Mesh2dInput
    {
        public const string FormatDescription =
            "{\"points\": [[x, y], ...], \"segments\": [[a, b] or {\"a\", \"b\", \"marker\", \"locked\"}, ...], " +
            "\"rings\": [[i, j, k, ...] or {\"vertices\": [...], \"hole\": false, \"marker\": 0}, ...], \"hole_seeds\": [[x, y], ...], " +
            "\"regions\": [{\"ring\": [...] or \"seed\": [x, y], \"label\": 1, \"max_area\": a}, ...], \"heights\": [z per point]}; " +
            "only points is required, indices refer to points (0-based)";

        /// <summary> Parses the JSON text; code is the result code of every failure. </summary>
        public static ConstrainedMesh2Input Parse(string json, string code, string what)
        {
            JsonDocument doc;
            try {
                doc = JsonDocument.Parse(json, new JsonDocumentOptions() { MaxDepth = 16 });
            } catch (JsonException e) {
                throw new CommandException(code, what + " is not valid JSON: " + e.Message);
            }
            using (doc) {
                var p = new Parser() { Code = code, What = what };
                return p.Input(doc.RootElement);
            }
        }

        public static ConstrainedMesh2Input ReadFile(string path)
        {
            if (File.Exists(path) == false)
                throw new CommandException(ResultCodes.ReadError, $"file not found: {path}");
            string text;
            try {
                text = File.ReadAllText(path);
            } catch (Exception e) {
                throw new CommandException(ResultCodes.ReadError, $"cannot read '{path}': {e.Message}");
            }
            return Parse(text, ResultCodes.ReadError, "'" + path + "'");
        }

        class Parser
        {
            public string Code, What;

            CommandException Fail(string where, string message) => new CommandException(Code, $"{What}: {where} {message}");

            public ConstrainedMesh2Input Input(JsonElement root)
            {
                if (root.ValueKind != JsonValueKind.Object)
                    throw Fail("the top level", "must be an object with 'points'");
                var input = new ConstrainedMesh2Input();
                bool hasPoints = false;
                var seen = new HashSet<string>();
                foreach (JsonProperty prop in root.EnumerateObject()) {
                    if (seen.Add(prop.Name) == false)
                        throw Fail("the top level", $"has the key '{prop.Name}' twice");
                    switch (prop.Name) {
                        case "points":
                            hasPoints = true;
                            foreach (var (e, i) in Array(prop.Value, "points"))
                                input.Points.Add(Point(e, $"points[{i}]"));
                            break;
                        case "segments":
                            foreach (var (e, i) in Array(prop.Value, "segments"))
                                input.Segments.Add(Segment(e, $"segments[{i}]"));
                            break;
                        case "rings":
                            foreach (var (e, i) in Array(prop.Value, "rings"))
                                input.Rings.Add(Ring(e, $"rings[{i}]"));
                            break;
                        case "hole_seeds":
                            foreach (var (e, i) in Array(prop.Value, "hole_seeds"))
                                input.HoleSeeds.Add(Point(e, $"hole_seeds[{i}]"));
                            break;
                        case "regions":
                            foreach (var (e, i) in Array(prop.Value, "regions"))
                                input.Regions.Add(Region(e, $"regions[{i}]"));
                            break;
                        case "heights":
                            input.Heights = new List<double>();
                            foreach (var (e, i) in Array(prop.Value, "heights"))
                                input.Heights.Add(Number(e, $"heights[{i}]"));
                            break;
                        default:
                            throw Fail("the top level", $"has an unknown key '{prop.Name}' (known: points, segments, rings, hole_seeds, regions, heights)");
                    }
                }
                if (hasPoints == false)
                    throw Fail("the top level", "has no 'points'");
                return input;
            }

            IEnumerable<(JsonElement, int)> Array(JsonElement e, string where)
            {
                if (e.ValueKind != JsonValueKind.Array)
                    throw Fail(where, "must be an array");
                return e.EnumerateArray().Select((x, i) => (x, i)).ToList();
            }

            double Number(JsonElement e, string where)
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double d) && double.IsFinite(d))
                    return d;
                throw Fail(where, "must be a finite number, got " + Raw(e));
            }

            int Index(JsonElement e, string where)
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int i))
                    return i;
                throw Fail(where, "must be an integer, got " + Raw(e));
            }

            bool Bool(JsonElement e, string where)
            {
                if (e.ValueKind == JsonValueKind.True) return true;
                if (e.ValueKind == JsonValueKind.False) return false;
                throw Fail(where, "must be true or false, got " + Raw(e));
            }

            static string Raw(JsonElement e)
            {
                string s = e.GetRawText();
                return s.Length > 40 ? s.Substring(0, 40) + "..." : s;
            }

            Vector2d Point(JsonElement e, string where)
            {
                if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2)
                    throw Fail(where, "must be [x, y], got " + Raw(e));
                return new Vector2d(Number(e[0], where + "[0]"), Number(e[1], where + "[1]"));
            }

            int[] Indices(JsonElement e, string where)
            {
                return Array(e, where).Select(x => Index(x.Item1, $"{where}[{x.Item2}]")).ToArray();
            }

            Mesh2Segment Segment(JsonElement e, string where)
            {
                if (e.ValueKind == JsonValueKind.Array) {
                    if (e.GetArrayLength() != 2)
                        throw Fail(where, "must be [a, b] or an object, got " + Raw(e));
                    return new Mesh2Segment(Index(e[0], where + "[0]"), Index(e[1], where + "[1]"));
                }
                if (e.ValueKind != JsonValueKind.Object)
                    throw Fail(where, "must be [a, b] or {\"a\", \"b\", \"marker\", \"locked\"}, got " + Raw(e));
                int? a = null, b = null; int marker = 0; bool locked = false;
                foreach (JsonProperty p in e.EnumerateObject()) {
                    switch (p.Name) {
                        case "a": a = Index(p.Value, where + ".a"); break;
                        case "b": b = Index(p.Value, where + ".b"); break;
                        case "marker": marker = Index(p.Value, where + ".marker"); break;
                        case "locked": locked = Bool(p.Value, where + ".locked"); break;
                        default: throw Fail(where, $"has an unknown key '{p.Name}' (known: a, b, marker, locked)");
                    }
                }
                if (a == null || b == null)
                    throw Fail(where, "needs 'a' and 'b'");
                return new Mesh2Segment(a.Value, b.Value, marker, locked);
            }

            Mesh2Ring Ring(JsonElement e, string where)
            {
                if (e.ValueKind == JsonValueKind.Array)
                    return new Mesh2Ring(Indices(e, where));
                if (e.ValueKind != JsonValueKind.Object)
                    throw Fail(where, "must be an array of point indices or {\"vertices\", \"hole\", \"marker\"}, got " + Raw(e));
                int[] vertices = null; bool hole = false; int marker = 0;
                foreach (JsonProperty p in e.EnumerateObject()) {
                    switch (p.Name) {
                        case "vertices": vertices = Indices(p.Value, where + ".vertices"); break;
                        case "hole": hole = Bool(p.Value, where + ".hole"); break;
                        case "marker": marker = Index(p.Value, where + ".marker"); break;
                        default: throw Fail(where, $"has an unknown key '{p.Name}' (known: vertices, hole, marker)");
                    }
                }
                if (vertices == null)
                    throw Fail(where, "needs 'vertices'");
                return new Mesh2Ring(vertices, hole, marker);
            }

            Mesh2Region Region(JsonElement e, string where)
            {
                if (e.ValueKind != JsonValueKind.Object)
                    throw Fail(where, "must be {\"ring\" or \"seed\", \"label\", \"max_area\"}, got " + Raw(e));
                int[] ring = null; Vector2d? seed = null; int label = 0; double? maxArea = null;
                foreach (JsonProperty p in e.EnumerateObject()) {
                    switch (p.Name) {
                        case "ring": ring = Indices(p.Value, where + ".ring"); break;
                        case "seed": seed = Point(p.Value, where + ".seed"); break;
                        case "label": label = Index(p.Value, where + ".label"); break;
                        case "max_area": maxArea = Number(p.Value, where + ".max_area"); break;
                        default: throw Fail(where, $"has an unknown key '{p.Name}' (known: ring, seed, label, max_area)");
                    }
                }
                if ((ring == null) == (seed == null))
                    throw Fail(where, "needs exactly one of 'ring' and 'seed'");
                return ring != null ? Mesh2Region.ByRing(ring, label, maxArea) : Mesh2Region.BySeed(seed.Value, label, maxArea);
            }
        }
    }


    public static partial class Commands
    {
        // ---- mesh2d ----

        static double SmallestAngleDegrees(Vector2d a, Vector2d b, Vector2d c)
        {
            // the edge vectors scaled by a power of two (exact) to about 1, so that tiny triangles do not underflow
            Vector2d[] d = { b - a, c - b, a - c };
            double max = d.Max(e => Math.Max(Math.Abs(e.x), Math.Abs(e.y)));
            if (max > 0) {
                int exp = Math.ILogB(max);
                for (int k = 0; k < 3; ++k) d[k] = new Vector2d(Math.ScaleB(d[k].x, -exp), Math.ScaleB(d[k].y, -exp));
            }
            double best = double.MaxValue;
            for (int k = 0; k < 3; ++k) {
                Vector2d u = d[k], v = -d[(k + 2) % 3];
                double ang = Math.Atan2(Math.Abs(u.x * v.y - u.y * v.x), u.x * v.x + u.y * v.y);
                best = Math.Min(best, ang * 180 / Math.PI);
            }
            return best;
        }

        static string Extension(string path) => System.IO.Path.GetExtension(path ?? "").ToLowerInvariant();

        static bool IsTriangleInput(string path) => Extension(path) == ".poly" || Extension(path) == ".node";

        /// <summary> the path without its extension if the output is a Triangle file (.node, .ele, .poly), else null </summary>
        static string TriangleOutputBase(string path)
        {
            string ext = Extension(path);
            return ext == ".node" || ext == ".ele" || ext == ".poly" ? path.Substring(0, path.Length - ext.Length) : null;
        }

        static string ReadText(string path)
        {
            if (System.IO.File.Exists(path) == false)
                throw new CommandException(ResultCodes.ReadError, $"file not found: {path}");
            try {
                return System.IO.File.ReadAllText(path);
            } catch (Exception e) {
                throw new CommandException(ResultCodes.ReadError, $"cannot read '{path}': {e.Message}");
            }
        }

        static void WriteText(string path, string text)
        {
            try {
                string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
                if (System.IO.Directory.Exists(dir) == false)
                    throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': directory does not exist");
                System.IO.File.WriteAllText(path, text);
            } catch (CommandException) {
                throw;
            } catch (Exception e) {
                throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': {e.Message}");
            }
        }

        /// <summary> A Triangle .poly (with its .node file if it lists no vertices) or .node file. </summary>
        static ConstrainedMesh2Input ReadTriangleInput(string path, bool heights, List<string> notes, out int loops)
        {
            string text = ReadText(path);
            TrianglePolyFormat.PolyInfo info;
            ConstrainedMesh2Input input;
            try {
                if (Extension(path) == ".node") {
                    input = TrianglePolyFormat.ReadNode(text, heights, out info);
                } else {
                    string node = System.IO.Path.ChangeExtension(path, ".node");
                    input = TrianglePolyFormat.ReadPoly(text, () => System.IO.File.Exists(node) ? ReadText(node) : null, heights, out info);
                }
            } catch (FormatException e) {
                throw new CommandException(ResultCodes.ReadError, $"cannot read '{path}': {e.Message}");
            }
            if (heights && input.Heights == null)
                notes.Add("poly_heights: the vertices have no attribute, the heights are 0");
            notes.AddRange(info.Notes);
            loops = info.Loops;
            return input;
        }

        static Command Mesh2d()
        {
            var c = new Command() {
                Name = "mesh2d",
                Description = "2D constrained Delaunay triangulation and quality mesh of points, segments, rings (outer boundaries " +
                    "and holes), hole seeds and labelled regions (ConstrainedDelaunayMesh2, QualityMesh2: Ruppert / Shewchuk refinement " +
                    "to a minimum angle, maximum area and Steiner point limit). Exact predicates; crossing constraints are split. " +
                    "Input: a JSON file (input) or inline JSON (geometry): " + Mesh2dInput.FormatDescription + ". " +
                    "Or a Triangle .poly or .node file (the standard 2D meshing format; the domain is what segments enclose, as with Triangle -p, " +
                    "hole points remove holes, regional attributes are integer labels with optional maximum areas). " +
                    "Without rings the domain is the convex hull minus the holes. The output mesh lies in the XY plane (z = heights, " +
                    "or 0), oriented counterclockwise (normals +z); region labels are its triangle groups (OBJ: 'g' lines). " +
                    "An output ending in .node, .ele or .poly writes the Triangle files <name>.node, <name>.ele and <name>.poly (subsegments with markers).",
            };
            c.Params.Add(new Param("input", ParamType.String, "input file: JSON (see the description), or Triangle .poly / .node; or give geometry") { Positional = true });
            c.Params.Add(new Param("output", ParamType.String, "output file: a mesh file by extension (" + string.Join(", ", MeshFiles.WriteFormats) +
                "), or .node / .ele / .poly for the Triangle files <name>.node, <name>.ele, <name>.poly (subsegments, hole points, seed regions)") { Required = true, Positional = true });
            c.Params.Add(new Param("geometry", ParamType.String, "the input as inline JSON text, instead of an input file"));
            c.Params.Add(new Param("quality", ParamType.Boolean, "refine to the quality bounds (QualityMesh2); false: the constrained Delaunay triangulation only (ConstrainedDelaunayMesh2), min_angle, max_area, steiner_limit and conforming must not be given") { Default = true });
            c.Params.Add(new Param("min_angle", ParamType.Number, "quality: minimum angle in degrees, in [0, 60) (0 = no angle bound; up to about 33 converges on ordinary input)"));
            c.Params.Add(new Param("max_area", ParamType.Number, "quality: maximum triangle area (regions can set their own)"));
            c.Params.Add(new Param("steiner_limit", ParamType.Integer, "quality: most Steiner points to insert (default 10 x (points + segments) + 100000)"));
            c.Params.Add(new Param("conforming", ParamType.Boolean, "quality: conforming Delaunay, also without bounds (split segments until none is encroached)"));
            c.Params.Add(new Param("resolve_crossings", ParamType.Boolean, "split crossing constraints; false: a crossing fails with operation_failed") { Default = true });
            c.Params.Add(new Param("poly_heights", ParamType.Boolean, "Triangle input: the first vertex attribute is the height of the point (z of the output)") { Default = false });
            c.Params.Add(Binary());
            // TODO(m2.snap, #277): expose the snap tolerance (QualityMesh2 / ConstrainedDelaunayMesh2 SnapTolerance) once #277 is merged.
            c.Run = args => {
                string outputBase = TriangleOutputBase(args.String("output"));
                if (outputBase == null)
                    MeshFiles.CheckWritable(args.String("output"));
                if (args.Has("input") == args.Has("geometry"))
                    throw new CommandException(ResultCodes.InvalidArguments, "give exactly one of input (a JSON file) and geometry (inline JSON)");
                bool quality = args.Bool("quality");
                string[] qualityOnly = new[] { "min_angle", "max_area", "steiner_limit", "conforming" }.Where(args.Has).ToArray();
                if (quality == false && qualityOnly.Length > 0)
                    throw new CommandException(ResultCodes.InvalidArguments, string.Join(", ", qualityOnly) + " need quality (true)");
                double minAngle = args.Has("min_angle") ? args.Number("min_angle") : 20;
                if (!(minAngle >= 0 && minAngle < 60))
                    throw new CommandException(ResultCodes.InvalidArguments, "'min_angle' must be in [0, 60)");
                if (args.Has("max_area") && !(args.Number("max_area") > 0))
                    throw new CommandException(ResultCodes.InvalidArguments, "'max_area' must be > 0");
                if (args.Has("steiner_limit") && args.Integer("steiner_limit") < 0)
                    throw new CommandException(ResultCodes.InvalidArguments, "'steiner_limit' must be >= 0");

                var notes = new List<string>();
                int loops = -1;
                ConstrainedMesh2Input input;
                if (args.Has("geometry"))
                    input = Mesh2dInput.Parse(args.String("geometry"), ResultCodes.InvalidArguments, "geometry");
                else if (IsTriangleInput(args.String("input")))
                    input = ReadTriangleInput(args.String("input"), args.Bool("poly_heights"), notes, out loops);
                else {
                    if (args.Bool("poly_heights"))
                        throw new CommandException(ResultCodes.InvalidArguments, "'poly_heights' is for Triangle .poly / .node input");
                    input = Mesh2dInput.ReadFile(args.String("input"));
                }

                ConstrainedTriangulation2Result result;
                try {
                    if (quality) {
                        var q = new QualityMesh2() { MinAngle = minAngle, ResolveCrossings = args.Bool("resolve_crossings") };
                        if (args.Has("max_area")) q.MaxArea = args.Number("max_area");
                        if (args.Has("steiner_limit")) q.SteinerLimit = args.Integer("steiner_limit");
                        if (args.Has("conforming")) q.Conforming = args.Bool("conforming");
                        result = q.Compute(input);
                    } else {
                        result = new ConstrainedDelaunayMesh2() { ResolveCrossings = args.Bool("resolve_crossings") }.Compute(input);
                    }
                } catch (ArgumentException e) {
                    // indices out of range, non-finite or too large coordinates, a region's area, ...
                    throw new CommandException(args.Has("input") ? ResultCodes.ReadError : ResultCodes.InvalidArguments, "invalid input: " + e.Message);
                }
                var diagnostics = result.Diagnostics ?? new string[0];
                if (result.Status == Triangulation2Status.Failed) {
                    string where = result.ProblemCoordinate.HasValue
                        ? $" near ({result.ProblemCoordinate.Value.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}, {result.ProblemCoordinate.Value.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)})" : "";
                    throw new CommandException(ResultCodes.OperationFailed, $"triangulation failed ({result.FailureKind}){where}: {result.FailureDetail}");
                }
                ConstrainedMesh2 m2 = result.Mesh;
                if (result.Status == Triangulation2Status.Empty || m2 == null || m2.TriangleCount == 0)
                    throw new CommandException(ResultCodes.OperationFailed, "no triangles: fewer than three distinct points, all points collinear, or everything inside holes");

                // only the vertices of triangles, in id order (not duplicate input points, nor points in holes or outside the rings)
                var mesh = new DMesh3(MeshComponents.None);
                mesh.EnableTriangleGroups();
                var map = new int[m2.Vertices.Count];
                for (int t = 0; t < m2.TriangleCount; ++t)
                    for (int j = 0; j < 3; ++j)
                        map[m2.Triangles[t][j]] = 1;
                int added = 0;
                for (int i = 0; i < m2.Vertices.Count; ++i) {
                    if (map[i] == 0) { map[i] = -1; continue; }
                    map[i] = mesh.AppendVertex(new Vector3d(m2.Vertices[i].x, m2.Vertices[i].y, m2.Heights != null ? m2.Heights[i] : 0));
                    if (i >= m2.InputCount) added++;    // made by the mesher: Steiner points, crossings
                }
                double minAng = double.MaxValue, maxArea = 0, area = 0;
                var labels = new SortedDictionary<int, int>();
                for (int t = 0; t < m2.TriangleCount; ++t) {
                    Index3i tri = m2.Triangles[t];
                    int label = m2.TriangleLabels[t];
                    if (mesh.AppendTriangle(new Index3i(map[tri.a], map[tri.b], map[tri.c]), label) < 0)
                        throw new CommandException(ResultCodes.InternalError, $"triangle {t} could not be added to the output mesh");
                    Vector2d a = m2.Vertices[tri.a], b = m2.Vertices[tri.b], cc = m2.Vertices[tri.c];
                    double ta = 0.5 * ((b.x - a.x) * (cc.y - a.y) - (b.y - a.y) * (cc.x - a.x));
                    area += ta;
                    maxArea = Math.Max(maxArea, ta);
                    minAng = Math.Min(minAng, SmallestAngleDegrees(a, b, cc));
                    labels[label] = labels.TryGetValue(label, out int n) ? n + 1 : 1;
                }
                JsonArray files = null;
                if (outputBase != null) {
                    // the Triangle files, of the written vertices (numbered from 1, as Triangle does by default)
                    var vertices = mesh.VertexIndices().Select(v => mesh.GetVertex(v).xy).ToList();
                    var heights = m2.Heights != null ? mesh.VertexIndices().Select(v => mesh.GetVertex(v).z).ToList() : null;
                    var triangles = mesh.TriangleIndices().Select(mesh.GetTriangle).ToList();
                    var triLabels = input.Regions.Count > 0 ? mesh.TriangleIndices().Select(mesh.GetTriangleGroup).ToList() : null;
                    var subsegments = m2.Subsegments.Select(sg => (map[sg.A], map[sg.B], sg.Marker)).ToList();
                    files = new JsonArray();
                    if (input.Regions.Any(g => g.Ring != null))
                        notes.Add("regions given by ring have no .poly form (a region is a seed point there): the written .poly has only the seed regions; the labels of all triangles are in the .ele");
                    foreach (var (ext, text) in new[] {
                        (".node", TrianglePolyFormat.WriteNode(vertices, heights, subsegments)),
                        (".ele", TrianglePolyFormat.WriteEle(triangles, triLabels)),
                        (".poly", TrianglePolyFormat.WritePolyOfSubsegments(subsegments, input.HoleSeeds, input.Regions)) }) {
                        string path = outputBase + ext;
                        WriteText(path, text);
                        files.Add(path);
                    }
                } else {
                    MeshFiles.Write(args.String("output"), mesh, args.Bool("binary"), groups: true);
                }

                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = args.String("output") };
                if (files != null) o["files"] = files;
                if (loops >= 0) o["rings_from_segments"] = loops;
                o["method"] = quality ? "quality" : "constrained_delaunay";
                if (quality) {
                    o["min_angle"] = minAngle;
                    o["max_area"] = args.Has("max_area") ? MeshReport.Num(args.Number("max_area")) : null;
                }
                o["input_points"] = input.Points.Count;
                o["input_segments"] = input.Segments.Count;
                o["vertices"] = mesh.VertexCount;
                o["triangles"] = m2.TriangleCount;
                o["added_vertices"] = added;
                o["subsegments"] = m2.Subsegments.Count;
                o["smallest_angle"] = MeshReport.Num(minAng);
                o["largest_area"] = MeshReport.Num(maxArea);
                o["area"] = MeshReport.Num(area);
                o["bad_triangles"] = m2.BadTriangles.Count;
                o["steiner_limit_reached"] = m2.SteinerLimitReached;
                o["labels"] = new JsonObject(labels.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), kv.Value)));
                o["mesh"] = MeshReport.Metrics(mesh, false);
                var warnings = notes.Concat(diagnostics).ToList();
                if (m2.SteinerLimitReached)
                    warnings.Add("the Steiner point limit was reached: some triangles may miss the quality bounds (bad_triangles)");
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }
    }
}
