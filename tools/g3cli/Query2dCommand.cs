// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using g3;

namespace g3cli
{
    public static partial class Commands
    {
        // ---- query2d ----

        /// <summary> "x,y;x,y;..." (also "x y"), finite numbers </summary>
        static List<Vector2d> ParsePoints2(string text, string name)
        {
            var points = new List<Vector2d>();
            foreach (string part in text.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0)) {
                double[] v = ParseNumbers(part, name);
                if (v.Length != 2)
                    throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': '{part}' is not a point x,y");
                points.Add(new Vector2d(v[0], v[1]));
            }
            return points;
        }

        static double[] ParseNumbers(string text, string name)
        {
            return text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => {
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d))
                    return d;
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}': '{s}' is not a finite number");
            }).ToArray();
        }

        static Command Query2d()
        {
            var c = new Command() {
                Name = "query2d",
                Description = "Query the triangles of a mesh in the XY plane (a terrain or other height field) with a 2D loose quadtree " +
                    "(TriangleQuadtree2d): the height at points (exact containment, z interpolated in the triangle of the lowest id that " +
                    "contains the point; else, within 'tolerance', the height at the nearest point of the nearest triangle), the triangles whose bounding boxes meet a " +
                    "rectangle, and the k nearest triangles of each point (XY distance). Triangle ids are those of the input mesh.",
            };
            c.Params.Add(Input());
            c.Params.Add(new Param("points", ParamType.String, "query points 'x,y;x,y;...': the height and triangle at each"));
            c.Params.Add(new Param("rect", ParamType.String, "rectangle 'xmin,ymin,xmax,ymax': the triangles whose XY bounding boxes meet it (inclusive)"));
            c.Params.Add(new Param("tolerance", ParamType.Number, "points: absolute XY distance within which a point outside every triangle still gets the height of the nearest one (0 = exact hits only)") { Default = 0.0 });
            c.Params.Add(new Param("nearest", ParamType.Integer, "points: also the ids of the k nearest triangles (XY distance, ties by id; 0 = off)") { Default = 0 });
            c.Params.Add(new Param("max_results", ParamType.Integer, "at most this many triangle ids per list (rect, hits); more are counted (*_count) and the list is marked truncated (*_truncated)") { Default = 1000 });
            c.Run = args => {
                if (args.Has("points") == false && args.Has("rect") == false)
                    throw new CommandException(ResultCodes.InvalidArguments, "give points or rect (or both)");
                double tolerance = args.Number("tolerance");
                if (!(tolerance >= 0))
                    throw new CommandException(ResultCodes.InvalidArguments, "'tolerance' must be >= 0");
                int k = args.Integer("nearest"), maxResults = args.Integer("max_results");
                if (k < 0 || k > 1000)
                    throw new CommandException(ResultCodes.InvalidArguments, "'nearest' must be in 0..1000");
                if (maxResults < 1)
                    throw new CommandException(ResultCodes.InvalidArguments, "'max_results' must be >= 1");
                List<Vector2d> points = args.Has("points") ? ParsePoints2(args.String("points"), "points") : null;
                if (points != null && points.Count == 0)
                    throw new CommandException(ResultCodes.InvalidArguments, "'points' has no point");
                AxisAlignedBox2d rect = AxisAlignedBox2d.Empty;
                if (args.Has("rect")) {
                    double[] r = ParseNumbers(args.String("rect"), "rect");
                    if (r.Length != 4 || r[0] > r[2] || r[1] > r[3])
                        throw new CommandException(ResultCodes.InvalidArguments, "'rect' must be xmin,ymin,xmax,ymax with xmin <= xmax and ymin <= ymax");
                    rect = new AxisAlignedBox2d(r[0], r[1], r[2], r[3]);
                }

                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                int[] tids = mesh.TriangleIndices().ToArray();
                var triangles = new Triangle3d[tids.Length];
                for (int i = 0; i < tids.Length; ++i) {
                    mesh.GetTriVertices(tids[i], ref triangles[i].V0, ref triangles[i].V1, ref triangles[i].V2);
                    if (!(triangles[i].V0.IsFinite && triangles[i].V1.IsFinite && triangles[i].V2.IsFinite))
                        throw new CommandException(ResultCodes.OperationFailed, $"triangle {tids[i]} has non-finite coordinates");
                }
                var tree = TriangleQuadtree2d.Over(triangles);

                JsonArray Ids(List<int> ids, JsonObject owner, string name)
                {
                    var sorted = ids.Select(i => tids[i]).ToList();
                    owner[name + "_count"] = sorted.Count;
                    owner[name + "_truncated"] = sorted.Count > maxResults;
                    return new JsonArray(sorted.Take(maxResults).Select(t => (JsonNode)t).ToArray());
                }

                var o = new JsonObject() { ["input"] = args.String("input"), ["triangles"] = tids.Length };
                AxisAlignedBox2d extent = tree.Extent;
                o["extent"] = tids.Length == 0 ? null : new JsonObject() {
                    ["min"] = new JsonArray(MeshReport.Num(extent.Min.x), MeshReport.Num(extent.Min.y)),
                    ["max"] = new JsonArray(MeshReport.Num(extent.Max.x), MeshReport.Num(extent.Max.y)),
                };
                if (points != null) {
                    var hits = new List<QuadtreeHeightHit2d>();
                    var nearestHit = new List<QuadtreeHeightHit2d>();
                    var ids = new List<int>();
                    var results = new JsonArray();
                    int found = 0;
                    foreach (Vector2d p in points) {
                        var pr = new JsonObject() { ["x"] = p.x, ["y"] = p.y };
                        bool exact;
                        QuadtreeHeightHit2d? best = null;
                        try {
                            // the triangles that contain the point (exact); without one, all within the tolerance, of which the
                            // nearest (lowest id on a tie) gives the height
                            tree.QueryHeights(p, hits, 0);
                            exact = hits.Count > 0;
                            if (exact == false && tolerance > 0) {
                                tree.QueryHeights(p, nearestHit, tolerance, stopAfterFirst: true);
                                if (nearestHit.Count > 0) {
                                    best = nearestHit[0];
                                    tree.QueryHeights(p, hits, tolerance);
                                }
                            }
                        } catch (ArithmeticException e) {
                            throw new CommandException(ResultCodes.OperationFailed, $"height at ({p.x}, {p.y}): {e.Message}");
                        }
                        hits.Sort((a, b) => a.Id.CompareTo(b.Id));
                        if (exact)
                            best = hits[0];     // overlapping layers: the lowest triangle id; all of them are in "hits"
                        pr["height"] = best.HasValue ? MeshReport.Num(best.Value.Height) : null;
                        pr["triangle"] = best.HasValue ? tids[best.Value.Id] : (JsonNode)null;
                        pr["exact"] = best.HasValue ? exact : (JsonNode)null;
                        if (best.HasValue) found++;
                        pr["hits"] = new JsonArray(hits.Take(maxResults).Select(h =>
                            (JsonNode)new JsonObject() { ["triangle"] = tids[h.Id], ["height"] = MeshReport.Num(h.Height) }).ToArray());
                        pr["hits_count"] = hits.Count;
                        pr["hits_truncated"] = hits.Count > maxResults;
                        if (k > 0) {
                            tree.FindNearestK(p, k, ids);
                            pr["nearest"] = new JsonArray(ids.Select(i => (JsonNode)tids[i]).ToArray());
                        }
                        results.Add(pr);
                    }
                    o["points"] = results;
                    o["points_with_height"] = found;
                }
                if (args.Has("rect")) {
                    var ids = new List<int>();
                    tree.QueryRectangle(rect, ids);
                    ids.Sort();
                    var ro = new JsonObject() {
                        ["min"] = new JsonArray(rect.Min.x, rect.Min.y), ["max"] = new JsonArray(rect.Max.x, rect.Max.y),
                    };
                    ro["triangles"] = Ids(ids, ro, "triangles");
                    o["rect"] = ro;
                }
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }
    }
}
