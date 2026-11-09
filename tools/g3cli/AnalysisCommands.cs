// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using g3;
using gs;

namespace g3cli
{
    /// <summary>
    /// Analysis commands: fidelity (MeshFidelityMetrics) and quality (TriangleQualityStatistics, MeshTopologySummary).
    /// They only read meshes and report numbers; non-finite values are reported as null.
    /// </summary>
    public static partial class Commands
    {
        /// <summary> like MeshReport.Num, but infinity (eg the aspect ratio of a zero-area triangle, the edge Chamfer distance when only one mesh has sharp edges) is the string "infinity" / "-infinity" instead of null; null is NaN: no value </summary>
        static JsonNode Val(double d)
        {
            if (double.IsPositiveInfinity(d)) return "infinity";
            if (double.IsNegativeInfinity(d)) return "-infinity";
            return MeshReport.Num(d);
        }

        static JsonArray NumArray(IEnumerable<double> values)
        {
            return values == null ? null : new JsonArray(values.Select(v => Val(v)).ToArray());
        }

        const int MaxSamples = 20000000, MaxThresholds = 16;

        static double[] ParseThresholds(string text)
        {
            double[] v = ParseNumbers(text, "relative_thresholds");
            if (v.Length == 0 || v.Length > MaxThresholds || v.Any(d => !(d > 0)))
                throw new CommandException(ResultCodes.InvalidArguments, $"'relative_thresholds' must be a list of 1 to {MaxThresholds} positive numbers");
            return v;
        }

        static JsonObject Distribution(QualityDistribution d)
        {
            return new JsonObject() {
                ["worst"] = Val(d.Worst), ["worst_1_percent"] = Val(d.Worst1Percent),
                ["worst_5_percent"] = Val(d.Worst5Percent), ["median"] = Val(d.Median),
            };
        }

        /// <summary> TriangleQualityStatistics as JSON (angles in degrees) </summary>
        public static JsonObject QualityReport(TriangleQualityStatistics q)
        {
            return new JsonObject() {
                ["triangles"] = q.Triangles, ["degenerate"] = q.Degenerate,
                ["min_angle"] = Distribution(q.MinAngle), ["max_angle"] = Distribution(q.MaxAngle),
                ["aspect_ratio"] = Distribution(q.AspectRatio), ["scaled_jacobian"] = Distribution(q.ScaledJacobian),
                ["condition"] = Distribution(q.Condition),
                ["min_angle_below_30"] = q.MinAngleBelow30, ["min_angle_below_10"] = q.MinAngleBelow10,
                ["min_angle_below_1"] = q.MinAngleBelow1,
                ["max_angle_above_90"] = q.MaxAngleAbove90, ["max_angle_above_160"] = q.MaxAngleAbove160,
                ["aspect_ratio_above_1_3"] = q.AspectRatioAbove13, ["scaled_jacobian_below_0_5"] = q.ScaledJacobianBelow05,
                ["condition_above_1_3"] = q.ConditionAbove13,
            };
        }

        /// <summary> MeshTopologySummary as JSON; the components (largest first) are listed up to maxComponents </summary>
        public static JsonObject TopologyReport(MeshTopologySummary t, int maxComponents = 20)
        {
            var o = new JsonObject() {
                ["components"] = t.Components, ["closed_components"] = t.ClosedComponents,
                ["vertices"] = t.Vertices, ["edges"] = t.Edges, ["triangles"] = t.Triangles,
                ["boundary_edges"] = t.BoundaryEdges, ["boundary_loops"] = t.BoundaryLoops, ["bowtie_vertices"] = t.BowtieVertices,
                ["euler_characteristic"] = t.EulerCharacteristic, ["genus"] = MeshReport.Num(t.Genus),
            };
            o["component_list"] = new JsonArray(t.ComponentList.Take(maxComponents).Select(c => (JsonNode)new JsonObject() {
                ["vertices"] = c.Vertices, ["edges"] = c.Edges, ["triangles"] = c.Triangles,
                ["boundary_loops"] = c.BoundaryLoops, ["euler_characteristic"] = c.EulerCharacteristic,
                ["genus"] = MeshReport.Num(c.Genus),
            }).ToArray());
            o["component_list_truncated"] = t.ComponentList.Count > maxComponents;
            return o;
        }

        static JsonArray TopologyChanges(MeshTopologySummary.Changes c)
        {
            var names = new JsonArray();
            foreach (MeshTopologySummary.Changes flag in Enum.GetValues(typeof(MeshTopologySummary.Changes)))
                if (flag != MeshTopologySummary.Changes.None && c.HasFlag(flag))
                    names.Add(ToSnake(flag.ToString()));
            return names;
        }

        static string ToSnake(string pascal)
        {
            return string.Concat(pascal.Select((ch, i) => i > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
        }

        static JsonObject Agreement(NormalAgreement a)
        {
            if (a == null) return null;
            return new JsonObject() {
                ["samples"] = a.Samples, ["matched_samples"] = a.MatchedSamples, ["mean_abs_cos"] = MeshReport.Num(a.MeanAbsCos),
                ["mean_angle"] = MeshReport.Num(a.MeanAngle), ["reversed_fraction"] = MeshReport.Num(a.ReversedFraction),
            };
        }

        static JsonObject CurvAgreement(CurvatureAgreement a)
        {
            if (a == null) return null;
            return new JsonObject() {
                ["samples"] = a.Samples, ["mean_error"] = MeshReport.Num(a.MeanError), ["gaussian_error"] = MeshReport.Num(a.GaussianError),
                ["mean_abs"] = MeshReport.Num(a.MeanAbs), ["gaussian_abs"] = MeshReport.Num(a.GaussianAbs),
            };
        }


        // ---- fidelity ----

        static Command Fidelity()
        {
            var c = new Command() {
                Name = "fidelity",
                Description = "Compare a mesh with a reference by the evaluation metrics of MeshFidelityMetrics, which see what the Hausdorff " +
                    "distance and the volume IoU miss: precision, recall and F-score at relative distance thresholds, Chamfer distance, normal " +
                    "consistency and the share of reversed normals, edge Chamfer / precision / recall on sharp edges, volume IoU (also of the outer " +
                    "surface), curvature agreement, the triangle quality of both meshes and the topology changes. 'size' is the reference's size (bounding box diagonal) in mesh units; " +
                    "every distance and length (chamfer, distance_*, max_distance_*, edge_*distance*, sharp_edge_length_*) is relative to it, " +
                    "curvatures are H*size and K*size^2, angles are in degrees, volume_missing / volume_extra fractions of the reference solid. " +
                    "A value null is not available (NaN), the string \"infinity\" is infinite (eg edge_chamfer if only one mesh has sharp edges). Sampled with a fixed seed: the result is reproducible.",
            };
            c.Params.Add(Input("reference", "reference mesh file"));
            c.Params.Add(Input("mesh", "mesh file compared with the reference"));
            c.Params.Add(new Param("relative_thresholds", ParamType.String, "distance thresholds as fractions of the reference size, 'a,b,c' (library default 0.001,0.005,0.01)"));
            c.Params.Add(new Param("surface_samples", ParamType.Integer, "surface samples per mesh (library default 50000)"));
            c.Params.Add(new Param("edge_samples", ParamType.Integer, "samples on the sharp edges per mesh (library default 10000)"));
            c.Params.Add(new Param("sharp_edge_angle", ParamType.Number, "dihedral angle in degrees above which an edge is sharp (library default 30)"));
            c.Params.Add(new Param("outer_surface", ParamType.Boolean, "also the metrics restricted to the outer surface of the reference (library default true)"));
            c.Params.Add(new Param("volume_samples", ParamType.Integer, "samples of the volume IoU (library default 100000)"));
            c.Params.Add(new Param("curvature", ParamType.Boolean, "also the curvature agreement (library default true)"));
            c.Params.Add(new Param("seed", ParamType.Integer, "seed of the sampling (library default 1)"));
            c.Params.Add(new Param("parallel", ParamType.Boolean, "use all cores (the result does not depend on it)") { Default = true });
            c.Run = args => {
                var f = new MeshFidelityMetrics(null, null) { Parallel = args.Bool("parallel") };
                if (args.Has("relative_thresholds")) f.RelativeThresholds = ParseThresholds(args.String("relative_thresholds"));
                if (args.Has("surface_samples")) f.SurfaceSamples = PositiveInt(args, "surface_samples");
                if (args.Has("edge_samples")) f.EdgeSamples = PositiveInt(args, "edge_samples");
                if (args.Has("volume_samples")) f.VolumeSamples = PositiveInt(args, "volume_samples");
                if (args.Has("sharp_edge_angle")) {
                    double a = args.Number("sharp_edge_angle");
                    if (!(a >= 0 && a <= 180))
                        throw new CommandException(ResultCodes.InvalidArguments, "'sharp_edge_angle' must be in 0..180");
                    f.SharpEdgeAngle = a;
                }
                if (args.Has("outer_surface")) f.OuterSurface = args.Bool("outer_surface");
                if (args.Has("curvature")) f.Curvature = args.Bool("curvature");
                if (args.Has("seed")) f.Seed = args.Integer("seed");
                var warnings = new List<string>();
                DMesh3 reference = MeshFiles.Read(args.String("reference"), warnings);
                DMesh3 mesh = MeshFiles.Read(args.String("mesh"), warnings);
                f.Reference = reference; f.Mesh = mesh;
                if (f.Compute() == false)
                    throw new CommandException(ResultCodes.OperationFailed,
                        "fidelity metrics failed (a mesh without usable triangles or with non-finite coordinates)");

                var o = new JsonObject() { ["reference"] = args.String("reference"), ["mesh_file"] = args.String("mesh") };
                o["size"] = MeshReport.Num(f.Size);
                o["relative_thresholds"] = NumArray(f.RelativeThresholds);
                o["precision"] = NumArray(f.Precision); o["recall"] = NumArray(f.Recall); o["f_score"] = NumArray(f.FScore);
                o["chamfer"] = MeshReport.Num(f.Chamfer);
                o["distance_mesh_to_reference"] = MeshReport.Num(f.DistanceMeshToReference);
                o["distance_reference_to_mesh"] = MeshReport.Num(f.DistanceReferenceToMesh);
                o["max_distance_mesh_to_reference"] = MeshReport.Num(f.MaxDistanceMeshToReference);
                o["max_distance_reference_to_mesh"] = MeshReport.Num(f.MaxDistanceReferenceToMesh);
                o["normal_consistency"] = MeshReport.Num(f.NormalConsistency);
                o["normals_mesh_to_reference"] = Agreement(f.NormalsMeshToReference);
                o["normals_reference_to_mesh"] = Agreement(f.NormalsReferenceToMesh);
                o["sharp_edge_length_reference"] = MeshReport.Num(f.SharpEdgeLengthReference);
                o["sharp_edge_length_mesh"] = MeshReport.Num(f.SharpEdgeLengthMesh);
                o["edge_chamfer"] = Val(f.EdgeChamfer);
                o["edge_distance_mesh_to_reference"] = Val(f.EdgeDistanceMeshToReference);
                o["edge_distance_reference_to_mesh"] = Val(f.EdgeDistanceReferenceToMesh);
                o["edge_precision"] = NumArray(f.EdgePrecision); o["edge_recall"] = NumArray(f.EdgeRecall); o["edge_f_score"] = NumArray(f.EdgeFScore);
                o["volume_iou"] = MeshReport.Num(f.VolumeIoU);
                o["volume_missing"] = MeshReport.Num(f.VolumeMissing); o["volume_extra"] = MeshReport.Num(f.VolumeExtra);
                if (f.OuterSurface) {
                    o["outer"] = new JsonObject() {
                        ["share"] = MeshReport.Num(f.OuterShare), ["recall"] = NumArray(f.OuterRecall), ["f_score"] = NumArray(f.OuterFScore),
                        ["edge_recall"] = NumArray(f.OuterEdgeRecall), ["edge_share"] = MeshReport.Num(f.OuterEdgeShare),
                        ["volume_iou"] = MeshReport.Num(f.OuterVolumeIoU),
                        ["volume_missing"] = MeshReport.Num(f.OuterVolumeMissing), ["volume_extra"] = MeshReport.Num(f.OuterVolumeExtra),
                        ["distance_reference_to_mesh"] = Val(f.OuterDistanceReferenceToMesh), ["max_distance_reference_to_mesh"] = Val(f.OuterMaxDistanceReferenceToMesh),
                        ["face_share"] = MeshReport.Num(f.OuterFaceShare), ["face_rounds"] = f.OuterFaceRounds,
                        ["faces_converged"] = f.OuterFacesConverged,
                    };
                }
                if (f.Curvature) {
                    o["curvature"] = new JsonObject() {
                        ["mesh_to_reference"] = CurvAgreement(f.CurvatureMeshToReference),
                        ["reference_to_mesh"] = CurvAgreement(f.CurvatureReferenceToMesh),
                        ["mean_error"] = MeshReport.Num(f.MeanCurvatureError), ["gaussian_error"] = MeshReport.Num(f.GaussianCurvatureError),
                        ["mean_distance"] = MeshReport.Num(f.MeanCurvatureDistance), ["gaussian_distance"] = MeshReport.Num(f.GaussianCurvatureDistance),
                        ["total_abs_mean_reference"] = MeshReport.Num(f.TotalAbsMeanCurvatureReference), ["total_abs_mean_mesh"] = MeshReport.Num(f.TotalAbsMeanCurvatureMesh),
                        ["total_abs_gaussian_reference"] = MeshReport.Num(f.TotalAbsGaussianCurvatureReference), ["total_abs_gaussian_mesh"] = MeshReport.Num(f.TotalAbsGaussianCurvatureMesh),
                    };
                }
                o["reference_quality"] = QualityReport(f.ReferenceQuality);
                o["mesh_quality"] = QualityReport(f.MeshQuality);
                o["reference_topology"] = TopologyReport(f.ReferenceTopology, 5);
                o["mesh_topology"] = TopologyReport(f.MeshTopology, 5);
                o["topology_changes"] = TopologyChanges(f.TopologyChanges);
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }

        static int PositiveInt(Args args, string name)
        {
            int v = args.Integer(name);
            if (v <= 0 || v > MaxSamples)
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}' must be in 1..{MaxSamples}");
            return v;
        }


        // ---- quality ----

        static Command Quality()
        {
            var c = new Command() {
                Name = "quality",
                Description = "Triangle quality and topology of one mesh: distributions (worst, worst 1 % and 5 %, median) of the minimum and " +
                    "maximum angle in degrees, aspect ratio, scaled Jacobian and condition (TriangleQualityStatistics), counts over common limits, " +
                    "and components, boundary loops, Euler characteristic and genus (MeshTopologySummary).",
            };
            c.Params.Add(Input());
            c.Params.Add(new Param("max_components", ParamType.Integer, "list at most this many components (largest first) in topology.component_list") { Default = 20 });
            c.Run = args => {
                int maxComponents = args.Integer("max_components");
                if (maxComponents < 0)
                    throw new CommandException(ResultCodes.InvalidArguments, "'max_components' must be >= 0");
                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                if (mesh.VertexIndices().Any(v => !mesh.GetVertex(v).IsFinite))
                    throw new CommandException(ResultCodes.OperationFailed, "the input has non-finite coordinates");
                var o = new JsonObject() { ["input"] = args.String("input") };
                o["quality"] = QualityReport(TriangleQualityStatistics.Compute(mesh));
                o["topology"] = TopologyReport(MeshTopologySummary.Compute(mesh), maxComponents);
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }
    }
}
