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
    /// fm_* fields: F-score, precision and recall at 0.1%, 0.5%, 1% of the size, Chamfer distance, normal
    /// consistency, reversed shares, sharp edge lengths, Edge Chamfer distance and edge F-scores, topology of
    /// both meshes and its changes, triangle quality of the result (and of the input if withInputQuality).
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
        for (int k = 0; k < tags.Length; ++k) {
            r["fm_f_" + tags[k]] = f.FScore[k];
            r["fm_p_" + tags[k]] = f.Precision[k];
            r["fm_r_" + tags[k]] = f.Recall[k];
            r["fm_ef_" + tags[k]] = f.EdgeFScore[k];
            r["fm_ep_" + tags[k]] = f.EdgePrecision[k];
            r["fm_er_" + tags[k]] = f.EdgeRecall[k];
        }
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
