using g3;

namespace g3.Examples
{
    /// <summary>
    /// Cut a closed workpiece with an open sheet that may end inside it (an extruded cut): the sheet is swept along a stated
    /// direction to a closed tool that ends beyond the workpiece, and the tool is subtracted with the exact boolean. Everything
    /// under the sheet along the direction is removed: a sheet that crosses fully cuts the workpiece in two, a sheet that ends
    /// inside leaves a step, a sheet with a hole leaves a pillar. For a sheet that crosses fully, the role Sheet
    /// (ExactMeshBoolean.Roles) is the exact cut and needs no direction.
    /// </summary>
    public static class SweptCutExample
    {
        /// <summary>
        /// direction: where the removed material is, seen from the sheet (the side the sheet's normals point away from).
        /// Returns null if a triangle of the sheet does not face away from the direction (the swept tool would fold over; also
        /// for a zero direction), or if the sheet's boundary could not be stitched; otherwise the computed boolean (check Status).
        /// </summary>
        public static ExactMeshBoolean Run(DMesh3 workpiece, DMesh3 sheet, Vector3d direction)
        {
            Vector3d d = direction.Normalized;
            // 1) every triangle must face away from the sweep. Necessary, not sufficient: see the check after Compute()
            foreach (int tid in sheet.TriangleIndices())
                if (sheet.GetTriNormal(tid).Dot(d) >= 0)
                    return null;

            // 2) a sweep longer than the common box is along d, so the tool's far end is outside the workpiece
            AxisAlignedBox3d box = workpiece.GetBounds();
            box.Contain(sheet.GetBounds());
            double length = 2 * (System.Math.Abs(d.x) * box.Width + System.Math.Abs(d.y) * box.Height + System.Math.Abs(d.z) * box.Depth);

            // 3) the closed tool: the sheet (its outward top), a moved copy, and walls on the sheet's boundary loops
            var tool = new DMesh3(sheet, false, MeshComponents.None);
            var extrude = new MeshExtrudeMesh(tool) {
                ExtrudedPositionF = (position, normal, vid) => position + length * d,
                IsPositiveOffset = false        // the sweep runs against the normals: the sheet keeps its orientation
            };
            if (extrude.Extrude() == false)
                return null;

            // 4) the exact Difference. AllowOpenMeshes = false: a tool that did not close is refused (Status OpenInput), not guessed
            var op = new ExactMeshBoolean(workpiece, tool, ExactMeshBoolean.Operations.Difference) { AllowOpenMeshes = false };
            op.Compute();
            // op.SelfIntersectingPairs > 0: parts of the sheet lie above each other along the direction (or the workpiece
            // self-intersects); the result is then the Difference with the union of their swept prisms
            return op;
        }
    }
}
