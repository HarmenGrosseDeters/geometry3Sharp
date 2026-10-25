using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Checking a mesh: closed (DMesh3 edges are always manifold), self-intersections, folded edges, and how much of the solid a repair
    /// kept (volume IoU). MeshRepairMetrics is read-only and deterministic.
    /// </summary>
    public static class RepairMetricsExample
    {
        public class Report
        {
            public bool Closed;
            public int BoundaryEdges, SelfIntersections, FoldedEdges;
            public string NotCleanReason;
        }

        public static Report Check(DMesh3 mesh)
        {
            var report = new Report();
            report.Closed = mesh.IsClosed();
            foreach (int eid in mesh.EdgeIndices())
                if (mesh.IsBoundaryEdge(eid))
                    report.BoundaryEdges++;
            report.SelfIntersections = MeshRepairMetrics.SelfIntersections(mesh);
            report.FoldedEdges = MeshRepairMetrics.FoldedEdges(mesh).Count;    // dihedral angle above 175 degrees
            report.NotCleanReason = MeshRobustRepair.NotCleanReason(mesh);     // "" = closed, manifold, no self-intersections
            return report;
        }

        /// <summary> Volume IoU of the solids of input and result (1 = same solid), by sampling winding numbers. </summary>
        public static double VolumeIoU(DMesh3 input, DMesh3 result)
        {
            double missing, extra;
            return MeshRepairMetrics.VolumeIoU(input, result, out missing, out extra);
        }
    }
}
