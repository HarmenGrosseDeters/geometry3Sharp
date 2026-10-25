using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Mesh repair. MeshAutoRepair keeps the input triangles where it can (welds, removes degenerate and
    /// duplicate parts, fills holes, orients). MeshRobustRepair runs it; when the result is not clean (open,
    /// non-manifold or self-intersecting) it resolves the self-intersections exactly (keeps the geometry) and
    /// falls back to winding number solidify only when that is not clean either.
    /// </summary>
    public static class MeshRepairExample
    {
        /// <summary> Repairs a copy of input with MeshAutoRepair. Always read the result from repair.Mesh. </summary>
        public static DMesh3 AutoRepair(DMesh3 input)
        {
            var repair = new MeshAutoRepair(new DMesh3(input)) {   // modifies the mesh it gets
                PostRepairCleanup = true                         // also remove duplicates, bowties, folds, inside-out parts
            };
            repair.Apply();     // false only if cancelled; true does not mean closed, so check the result
            return repair.Mesh; // a compacted copy, with vertex normals
        }

        /// <summary>
        /// Repair with a guaranteed clean result. usedExactResolve / usedSolidify say which fallback was needed
        /// (neither: the repair result was clean).
        /// </summary>
        public static DMesh3 RobustRepair(DMesh3 input, out bool usedExactResolve, out bool usedSolidify, out string fallbackReason)
        {
            var repair = new MeshRobustRepair(new DMesh3(input)) { SolidifyResolution = 96 };
            if (!repair.Apply()) {
                usedExactResolve = usedSolidify = false;
                fallbackReason = "failed";
                return null;
            }
            usedExactResolve = repair.UsedExactResolve;   // why not: repair.ExactResolveReason
            usedSolidify = repair.UsedSolidify;
            fallbackReason = repair.FallbackReason;       // "" if the repair result was clean, eg "self-intersections"
            return repair.Mesh;                           // MeshRobustRepair.NotCleanReason(repair.Mesh) == ""
        }
    }
}
