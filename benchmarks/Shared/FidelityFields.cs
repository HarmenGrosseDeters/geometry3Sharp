// JSON fields of MeshFidelityMetrics, TriangleQualityStatistics and MeshTopologySummary for the benchmarks
// (linked into MeshRepairBenchmark and UVAtlasBenchmark). Relative values are relative to MeshFidelityMetrics.Size.
using System.Collections.Generic;
using System.Diagnostics;
using g3;
using gs;

static class FidelityFields
{
    static readonly string[] tags = { "0001", "0005", "001" };

    /// <summary>
    /// fm_* fields: F-score, precision and recall at 0.1%, 0.5%, 1% of the size, the same against the input's outer
    /// surface (fm_of_*, fm_or_*, fm_outer_*), sampled max distances, Chamfer distance, normal
    /// consistency, reversed shares, sharp edge lengths, Edge Chamfer distance and edge F-scores, volume IoUs against
    /// the input's winding number solid and against the solid of its outer faces (fm_viou_*, fm_oviou_*), curvature
    /// errors and distribution distances (fm_h_*, fm_k_*), topology of both meshes and its changes, triangle quality
    /// of the result (and of the input if withInputQuality).
    /// The reference is used as given (orient it first if only the result's orientation is to be judged).
    /// </summary>
    public static void Add(DMesh3 reference, DMesh3 result, Dictionary<string, object> r, bool withInputQuality = true, int samples = 50000)
    {
        var sw = Stopwatch.StartNew();
        var f = new MeshFidelityMetrics(reference, result) { SurfaceSamples = samples, RelativeThresholds = new[] { 0.001, 0.005, 0.01 } };
        if (f.Compute() == false) {
            r["fm_ok"] = false;
            return;
        }
        r["fm_ok"] = true;
        r["fm_size"] = f.Size;
        r["fm_bbox_diag"] = reference.CachedBounds.DiagonalLength;     // the unit of hd_*_rel
        for (int k = 0; k < tags.Length; ++k) {
            r["fm_f_" + tags[k]] = f.FScore[k];
            r["fm_p_" + tags[k]] = f.Precision[k];
            r["fm_r_" + tags[k]] = f.Recall[k];
            r["fm_ef_" + tags[k]] = f.EdgeFScore[k];
            r["fm_ep_" + tags[k]] = f.EdgePrecision[k];
            r["fm_er_" + tags[k]] = f.EdgeRecall[k];
            r["fm_of_" + tags[k]] = f.OuterFScore[k];
            r["fm_or_" + tags[k]] = f.OuterRecall[k];
            r["fm_oer_" + tags[k]] = f.OuterEdgeRecall[k];
        }
        r["fm_max_fwd"] = f.MaxDistanceMeshToReference;
        r["fm_max_back"] = f.MaxDistanceReferenceToMesh;
        r["fm_outer_share"] = f.OuterShare;
        r["fm_outer_edge_share"] = f.OuterEdgeShare;
        r["fm_outer_mean"] = f.OuterDistanceReferenceToMesh;
        r["fm_outer_max"] = f.OuterMaxDistanceReferenceToMesh;
        r["fm_viou"] = f.VolumeIoU;
        r["fm_viou_missing"] = f.VolumeMissing;
        r["fm_viou_extra"] = f.VolumeExtra;
        r["fm_oviou"] = f.OuterVolumeIoU;
        r["fm_oviou_missing"] = f.OuterVolumeMissing;
        r["fm_oviou_extra"] = f.OuterVolumeExtra;
        r["fm_outer_face_share"] = f.OuterFaceShare;
        // curvature (MeshCurvature, Meyer et al. 2003): H * size, K * size^2 at corresponding points (mean of both
        // directions), 1-Wasserstein distances of the distributions, total absolute curvatures
        r["fm_h_err"] = f.MeanCurvatureError;
        r["fm_k_err"] = f.GaussianCurvatureError;
        r["fm_h_w1"] = f.MeanCurvatureDistance;
        r["fm_k_w1"] = f.GaussianCurvatureDistance;
        r["fm_h_abs_in"] = f.CurvatureReferenceToMesh.MeanAbs;
        r["fm_k_abs_in"] = f.CurvatureReferenceToMesh.GaussianAbs;
        r["fm_h_tot_in"] = f.TotalAbsMeanCurvatureReference;
        r["fm_h_tot_out"] = f.TotalAbsMeanCurvatureMesh;
        r["fm_k_tot_in"] = f.TotalAbsGaussianCurvatureReference;
        r["fm_k_tot_out"] = f.TotalAbsGaussianCurvatureMesh;
        r["fm_chamfer"] = f.Chamfer;
        r["fm_nc"] = f.NormalConsistency;
        r["fm_nangle_fwd"] = f.NormalsMeshToReference.MeanAngle;
        r["fm_nangle_back"] = f.NormalsReferenceToMesh.MeanAngle;
        r["fm_reversed_fwd"] = f.NormalsMeshToReference.ReversedFraction;
        r["fm_reversed_back"] = f.NormalsReferenceToMesh.ReversedFraction;
        r["fm_sharp_in"] = f.SharpEdgeLengthReference;
        r["fm_sharp_out"] = f.SharpEdgeLengthMesh;
        r["fm_ecd"] = f.EdgeChamfer;
        r["fm_ecd_fwd"] = f.EdgeDistanceMeshToReference;
        r["fm_ecd_back"] = f.EdgeDistanceReferenceToMesh;
        r["fm_topo_in"] = Topology(f.ReferenceTopology);
        r["fm_topo_out"] = Topology(f.MeshTopology);
        r["fm_topo_changes"] = f.TopologyChanges.ToString();
        if (withInputQuality)
            r["fm_q_in"] = Quality(f.ReferenceQuality);
        r["fm_q_out"] = Quality(f.MeshQuality);
        r["fm_ms"] = sw.ElapsedMilliseconds;
    }

    public static Dictionary<string, object> Topology(MeshTopologySummary t)
    {
        int discs = 0;
        foreach (var c in t.ComponentList)
            if (c.Genus == 0 && c.BoundaryLoops == 1) discs++;
        return new Dictionary<string, object> {
            ["components"] = t.Components, ["closed"] = t.ClosedComponents, ["genus"] = t.Genus, ["euler"] = t.EulerCharacteristic,
            ["loops"] = t.BoundaryLoops, ["bowties"] = t.BowtieVertices, ["discs"] = discs };
    }

    public static Dictionary<string, object> Quality(TriangleQualityStatistics q)
    {
        var d = new Dictionary<string, object> {
            ["tris"] = q.Triangles, ["degenerate"] = q.Degenerate,
            ["minangle_lt30"] = q.MinAngleBelow30, ["minangle_lt10"] = q.MinAngleBelow10, ["minangle_lt1"] = q.MinAngleBelow1,
            ["maxangle_gt90"] = q.MaxAngleAbove90, ["maxangle_gt160"] = q.MaxAngleAbove160,
            ["aspect_gt13"] = q.AspectRatioAbove13, ["sj_lt05"] = q.ScaledJacobianBelow05, ["cond_gt13"] = q.ConditionAbove13 };
        void dist(string name, QualityDistribution x) {
            d[name] = new[] { x.Worst, x.Worst1Percent, x.Worst5Percent, x.Median };
        }
        dist("minangle", q.MinAngle);
        dist("maxangle", q.MaxAngle);
        dist("aspect", q.AspectRatio);
        dist("sj", q.ScaledJacobian);
        dist("cond", q.Condition);
        return d;
    }
}
