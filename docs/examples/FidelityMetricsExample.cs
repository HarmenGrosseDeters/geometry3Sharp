using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Comparing a result (repair, remesh, solidify, wrap) with its input beyond Hausdorff distance and volume IoU:
    /// F-score at a threshold, normals (reversed patches), sharp edges (lost features), triangle quality and
    /// topology. MeshFidelityMetrics is read-only and deterministic (seeded samples); all distances are relative to
    /// the input's size.
    /// </summary>
    public static class FidelityMetricsExample
    {
        public class Report
        {
            /// <summary> F-score at 1% of the size: 1 = every part of either surface is that close to the other </summary>
            public double FScore;
            /// <summary> share of the result's surface whose normals point against the input's </summary>
            public double Reversed;
            /// <summary> Edge Chamfer distance (relative), and the share of the input's sharp edges kept within 0.5% </summary>
            public double EdgeChamfer, EdgeRecall;
            /// <summary> what changed: components, handles, boundary loops </summary>
            public MeshTopologySummary.Changes TopologyChanges;
            /// <summary> the min angle that 1% of the result's triangles are below (degrees) </summary>
            public double MinAngle1Percent;
        }

        public static Report Compare(DMesh3 input, DMesh3 result)
        {
            var f = new MeshFidelityMetrics(input, result) { RelativeThresholds = new[] { 0.005, 0.01 } };
            if (f.Compute() == false)
                return null;        // an empty or non-finite mesh
            return new Report() {
                FScore = f.FScore[1],
                Reversed = f.NormalsMeshToReference.ReversedFraction,
                EdgeChamfer = f.EdgeChamfer,
                EdgeRecall = f.EdgeRecall[0],
                TopologyChanges = f.TopologyChanges,
                MinAngle1Percent = f.MeshQuality.MinAngle.Worst1Percent
            };
        }
    }
}
