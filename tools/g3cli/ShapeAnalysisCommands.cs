// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using g3;
using gs;

namespace g3cli
{
    /// <summary>
    /// Analysis commands on shape: deviation (MeshDeviation), thickness (MeshWallThickness) and fit (PrimitiveFit3).
    /// They report statistics only (per-vertex values are not written). null is not available (NaN), the string
    /// "infinity" is infinite, as in fidelity.
    /// </summary>
    public static partial class Commands
    {
        /// <summary> DeviationStatistics as JSON (null if there are no values, or if all weights are zero) </summary>
        static JsonObject StatsReport(DeviationStatistics s)
        {
            if (s == null || s.Count == 0) return null;
            return new JsonObject() {
                ["count"] = s.Count, ["total_weight"] = Val(s.TotalWeight),
                ["min"] = Val(s.Min), ["max"] = Val(s.Max), ["mean"] = Val(s.Mean), ["std_dev"] = Val(s.StdDev), ["rms"] = Val(s.RMS),
                ["mean_absolute"] = Val(s.MeanAbsolute), ["max_absolute"] = Val(s.MaxAbsolute),
                ["median"] = Val(s.Median),
                ["absolute_p90"] = Val(s.AbsolutePercentile(0.9)), ["absolute_p95"] = Val(s.AbsolutePercentile(0.95)),
                ["absolute_p99"] = Val(s.AbsolutePercentile(0.99)),
            };
        }

        static string[] EnumNames<T>() =>
            Enum.GetNames(typeof(T)).Select(n => ToSnake(n)).ToArray();

        static T ParseEnum<T>(string snake) where T : struct
        {
            foreach (T v in Enum.GetValues(typeof(T)))
                if (ToSnake(v.ToString()) == snake) return v;
            throw new CommandException(ResultCodes.InvalidArguments, $"unknown value '{snake}'");
        }

        static double NonNegative(Args args, string name)
        {
            double v = args.Number(name);
            if (!(v >= 0) || double.IsInfinity(v))
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}' must be a finite number >= 0");
            return v;
        }


        // ---- deviation ----

        static Command DeviationCommand()
        {
            var c = new Command() {
                Name = "deviation",
                Description = "Deviation map of a mesh against a reference (MeshDeviation): the distance of every vertex of 'mesh' to the " +
                    "surface of 'reference', signed (positive outside the reference's solid; sign = pseudo_normal or winding_number) or " +
                    "unsigned, as statistics (count, min, max, mean, std_dev, rms, median, absolute percentiles; vertex_statistics are " +
                    "unweighted, 'statistics' are weighted by vertex area), and with 'certified' the certified one-sided Hausdorff " +
                    "distance from mesh to reference as lower and upper bound. The per-vertex values are not written. " +
                    "Distances are in mesh units.",
            };
            c.Params.Add(Input("mesh", "mesh file whose deviation is measured"));
            c.Params.Add(Input("reference", "reference mesh file"));
            c.Params.Add(new Param("sign", ParamType.String, "sign of the distance: pseudo_normal (angle weighted pseudo normals, needs a closed reference), winding_number, unsigned") { Default = "pseudo_normal", Enum = EnumNames<MeshDeviation.Signs>() });
            c.Params.Add(new Param("certified", ParamType.Boolean, "also the certified one-sided Hausdorff distance from mesh to reference (hausdorff_lower_bound, hausdorff_upper_bound)") { Default = false });
            c.Params.Add(new Param("certified_tolerance", ParamType.Number, "absolute tolerance of the certified bound (0 = 1e-4 of the bounding box diagonal)") { Default = 0.0 });
            c.Run = args => {
                double tol = NonNegative(args, "certified_tolerance");
                var sign = ParseEnum<MeshDeviation.Signs>(args.String("sign"));
                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("mesh"), warnings);
                DMesh3 reference = MeshFiles.Read(args.String("reference"), warnings);
                var d = new MeshDeviation(mesh, reference) {
                    Sign = sign, ComputeCertifiedBound = args.Bool("certified"), CertifiedTolerance = tol,
                };
                if (d.Compute() == false)
                    throw new CommandException(ResultCodes.OperationFailed, "deviation failed (a mesh without triangles, or cancelled)");
                var o = new JsonObject() { ["mesh_file"] = args.String("mesh"), ["reference"] = args.String("reference"), ["sign"] = args.String("sign") };
                o["statistics"] = StatsReport(d.Statistics);
                o["vertex_statistics"] = StatsReport(d.VertexStatistics);
                if (args.Bool("certified")) {
                    o["hausdorff_lower_bound"] = Val(d.HausdorffLowerBound);
                    o["hausdorff_upper_bound"] = Val(d.HausdorffUpperBound);
                }
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }


        // ---- thickness ----

        static Command Thickness()
        {
            var c = new Command() {
                Name = "thickness",
                Description = "Wall thickness of a closed mesh (MeshWallThickness) by method: ray (distance along the inward normal), " +
                    "shape_diameter (median over a cone of rays, robust, the default) or inscribed_sphere (diameter of the ball that " +
                    "touches the surface, medial axis). Statistics over the triangles (area weighted; thickness at the centroid), and with " +
                    "'min_thickness' the number and area share of the triangles and the number of vertices thinner than it " +
                    "(thin_triangle_ids lists the first ones). Thickness is in mesh units; a value is null where no ray hit.",
            };
            c.Params.Add(Input());
            c.Params.Add(new Param("method", ParamType.String, "ray, shape_diameter or inscribed_sphere") { Default = "shape_diameter", Enum = EnumNames<MeshWallThickness.Methods>() });
            c.Params.Add(new Param("cone_angle", ParamType.Number, "shape_diameter: full opening angle of the ray cone in degrees (library default 120)"));
            c.Params.Add(new Param("ray_count", ParamType.Integer, "shape_diameter: rays per point (library default 30)"));
            c.Params.Add(new Param("max_distance", ParamType.Number, "rays longer than this count as missing (0 = bounding box diagonal)") { Default = 0.0 });
            c.Params.Add(new Param("min_thickness", ParamType.Number, "report the triangles and vertices thinner than this (0 = off)") { Default = 0.0 });
            c.Params.Add(new Param("max_results", ParamType.Integer, "at most this many ids in thin_triangle_ids") { Default = 100 });
            c.Run = args => {
                double maxDistance = NonNegative(args, "max_distance"), minThickness = NonNegative(args, "min_thickness");
                int maxResults = args.Integer("max_results");
                if (maxResults < 0 || maxResults > 100000)
                    throw new CommandException(ResultCodes.InvalidArguments, "'max_results' must be in 0..100000");
                var t = new MeshWallThickness(null) { Method = ParseEnum<MeshWallThickness.Methods>(args.String("method")), MaxDistance = maxDistance };
                if (args.Has("cone_angle")) {
                    double a = args.Number("cone_angle");
                    if (!(a > 0 && a <= 179))
                        throw new CommandException(ResultCodes.InvalidArguments, "'cone_angle' must be in (0, 179] degrees");
                    t.ConeAngle = a;
                }
                if (args.Has("ray_count")) {
                    int n = args.Integer("ray_count");
                    if (n < 1 || n > 1000)
                        throw new CommandException(ResultCodes.InvalidArguments, "'ray_count' must be in 1..1000");
                    t.RayCount = n;
                }
                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                t.Mesh = mesh;
                // work: rays per point times the points (triangle centroids, and vertices if they are wanted)
                long points = mesh.TriangleCount + (minThickness > 0 ? mesh.VertexCount : 0);
                if ((long)t.RayCount * points > MaxRayCasts)
                    throw new CommandException(ResultCodes.InvalidArguments,
                        $"ray_count {t.RayCount} times {points} points is more than {MaxRayCasts} ray casts; lower ray_count");
                t.ComputeVertices = minThickness > 0;
                if (MeshRepairMetrics_OpenEdges(mesh) > 0)
                    warnings.Add("the mesh is not closed: wall thickness is only defined for a closed, consistently oriented mesh; triangles without a hit have no value");
                if (t.Compute() == false)
                    throw new CommandException(ResultCodes.OperationFailed, "thickness failed (a mesh without triangles, or cancelled)");
                var o = new JsonObject() { ["input"] = args.String("input"), ["method"] = args.String("method") };
                o["statistics"] = StatsReport(t.Statistics);
                int missing = t.TriangleIndicesWithoutValue(mesh);
                o["triangles_without_value"] = missing;
                if (minThickness > 0) {
                    List<int> thinT = t.ThinTriangles(minThickness);
                    double thinArea = thinT.Sum(tid => mesh.GetTriArea(tid)), area = mesh.TriangleIndices().Sum(tid => mesh.GetTriArea(tid));
                    o["min_thickness"] = minThickness;
                    o["thin_triangles"] = thinT.Count;
                    o["thin_area_share"] = Val(area > 0 ? thinArea / area : double.NaN);
                    o["thin_vertices"] = t.ThinVertices(minThickness).Count;
                    o["thin_triangle_ids"] = new JsonArray(thinT.Take(maxResults).Select(i => (JsonNode)i).ToArray());
                    o["thin_triangle_ids_truncated"] = thinT.Count > maxResults;
                }
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }

        const long MaxRayCasts = 2000000000L, MaxFitWork = 1000000000L;

        static int MeshRepairMetrics_OpenEdges(DMesh3 mesh) => MeshIterators.BoundaryEdges(mesh).Count();

        static int TriangleIndicesWithoutValue(this MeshWallThickness t, DMesh3 mesh)
        {
            return t.TriangleThickness == null ? 0 : mesh.TriangleIndices().Count(tid => double.IsNaN(t.TriangleThickness[tid]));
        }


        // ---- fit ----

        static Command Fit()
        {
            var c = new Command() {
                Name = "fit",
                Description = "Fit a plane, sphere, cylinder or cone to the vertices of a mesh (PrimitiveFit3: RANSAC with local optimization, " +
                    "then least squares on the inliers; deterministic for a seed). Reports the parameters, the inliers and the RMS and maximum " +
                    "distance of the inliers to the surface. With 'all', every primitive is fitted and reported in 'fits'. " +
                    "A failed fit (too few points, no model within the limits) is operation_failed with 'fit_result'.",
            };
            c.Params.Add(Input());
            var prim = EnumNames<PrimitiveFit3.Primitives>().Concat(new[] { "all" }).ToArray();
            c.Params.Add(new Param("primitive", ParamType.String, "plane, sphere, cylinder, cone or all") { Default = "plane", Enum = prim });
            c.Params.Add(new Param("inlier_distance", ParamType.Number, "inlier threshold on the distance to the surface in mesh units (0 = 1 % of the bounding box diagonal)") { Default = 0.0 });
            c.Params.Add(new Param("max_normal_angle", ParamType.Number, "inlier threshold on the angle between vertex normal and surface normal, degrees (library default 30)"));
            c.Params.Add(new Param("max_iterations", ParamType.Integer, "RANSAC samples at most (library default 10000)"));
            c.Params.Add(new Param("seed", ParamType.Integer, "seed of the sampling (library default 1)"));
            c.Params.Add(new Param("max_radius", ParamType.Number, "largest sphere / cylinder radius (0 = 10 times the bounding box diagonal)") { Default = 0.0 });
            c.Params.Add(new Param("ransac", ParamType.Boolean, "RANSAC (false: least squares on all vertices)") { Default = true });
            c.Run = args => {
                double inlier = NonNegative(args, "inlier_distance"), maxRadius = NonNegative(args, "max_radius");
                var opts = new Action<PrimitiveFit3>(f => {
                    f.InlierDistance = inlier; f.MaxRadius = maxRadius; f.UseRansac = args.Bool("ransac");
                    if (args.Has("max_normal_angle")) {
                        double a = args.Number("max_normal_angle");
                        if (!(a > 0 && a <= 90))
                            throw new CommandException(ResultCodes.InvalidArguments, "'max_normal_angle' must be in (0, 90]");
                        f.MaxNormalAngle = a;
                    }
                    if (args.Has("max_iterations")) {
                        int n = args.Integer("max_iterations");
                        if (n < 1 || n > 1000000)
                            throw new CommandException(ResultCodes.InvalidArguments, "'max_iterations' must be in 1..1000000");
                        f.MaxIterations = n;
                    }
                    if (args.Has("seed")) f.Seed = args.Integer("seed");
                });
                string primitive = args.String("primitive");
                var which = primitive == "all" ? Enum.GetValues(typeof(PrimitiveFit3.Primitives)).Cast<PrimitiveFit3.Primitives>().ToArray()
                    : new[] { ParseEnum<PrimitiveFit3.Primitives>(primitive) };
                opts(new PrimitiveFit3(new Vector3d[0])); // validate the options before reading
                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                var o = new JsonObject() { ["input"] = args.String("input"), ["vertices"] = mesh.VertexCount };
                var probe = new PrimitiveFit3(new Vector3d[0]);
                opts(probe);
                // only a max_iterations given by the caller is bounded by the work; the default stops early (Confidence)
                if (args.Has("max_iterations") && (double)probe.MaxIterations * mesh.VertexCount * which.Length > MaxFitWork)
                    throw new CommandException(ResultCodes.InvalidArguments,
                        $"max_iterations {probe.MaxIterations} times {mesh.VertexCount} vertices is too much work; lower max_iterations");
                var fits = new JsonArray();
                foreach (var p in which) {
                    var f = new PrimitiveFit3(mesh) { Primitive = p };
                    opts(f);
                    f.Compute();
                    JsonObject r = FitReport(f);
                    if (which.Length == 1) {
                        if (f.Result != PrimitiveFit3.Results.Success)
                            throw new CommandException(ResultCodes.OperationFailed, "fit failed: " + r["fit_result"],
                                new JsonObject() { ["fit_result"] = r["fit_result"].DeepClone() });
                        foreach (var kv in r.ToList()) { r.Remove(kv.Key); o[kv.Key] = kv.Value; }
                    } else
                        fits.Add(r);
                }
                if (which.Length > 1) {
                    o["fits"] = fits;
                    o["successful_fits"] = fits.Count(f => (string)f["fit_result"] == "success");
                }
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }

        static JsonObject FitReport(PrimitiveFit3 f)
        {
            var o = new JsonObject() { ["primitive"] = ToSnake(f.Primitive.ToString()), ["fit_result"] = ToSnake(f.Result.ToString()) };
            if (f.Result != PrimitiveFit3.Results.Success) {
                o["rejected_by_limits"] = f.RejectedByLimits;
                return o;
            }
            o["inliers"] = f.InlierCount; o["points"] = f.Points.Length;
            o["inlier_share"] = Val(f.Points.Length > 0 ? (double)f.InlierCount / f.Points.Length : double.NaN);
            o["rms_error"] = Val(f.RMSError); o["max_error"] = Val(f.MaxError);
            o["iterations"] = f.Iterations; o["rejected_by_limits"] = f.RejectedByLimits;
            switch (f.Primitive) {
                case PrimitiveFit3.Primitives.Plane:
                    o["plane"] = new JsonObject() { ["origin"] = MeshReport.Vec(f.PlaneOrigin), ["normal"] = MeshReport.Vec(f.Plane.Normal) };
                    break;
                case PrimitiveFit3.Primitives.Sphere:
                    o["sphere"] = new JsonObject() { ["center"] = MeshReport.Vec(f.SphereCenter), ["radius"] = Val(f.SphereRadius) };
                    break;
                case PrimitiveFit3.Primitives.Cylinder:
                    o["cylinder"] = new JsonObject() {
                        ["center"] = MeshReport.Vec(f.Cylinder.Axis.Origin), ["axis_direction"] = MeshReport.Vec(f.Cylinder.Axis.Direction),
                        ["radius"] = Val(f.Cylinder.Radius), ["height"] = Val(f.Cylinder.Height),
                    };
                    break;
                case PrimitiveFit3.Primitives.Cone:
                    o["cone"] = new JsonObject() {
                        ["apex"] = MeshReport.Vec(f.Cone.Apex), ["axis"] = MeshReport.Vec(f.Cone.Axis),
                        ["half_angle_degrees"] = Val(f.Cone.Angle * MathUtil.Rad2Deg), ["height"] = Val(f.Cone.Height),
                    };
                    break;
            }
            return o;
        }
    }
}
