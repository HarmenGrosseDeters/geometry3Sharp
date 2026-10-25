using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Mesh repair. MeshAutoRepair keeps the input triangles where it can (welds, removes degenerate and
    /// duplicate parts, fills holes, orients). MeshRobustRepair runs it and falls back to winding number
    /// solidify when the result is not clean (open, non-manifold or self-intersecting).
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

        /// <summary> Repair with a guaranteed clean result. usedSolidify says whether the fallback was needed. </summary>
        public static DMesh3 RobustRepair(DMesh3 input, out bool usedSolidify, out string fallbackReason)
        {
            var repair = new MeshRobustRepair(new DMesh3(input)) { SolidifyResolution = 96 };
            if (!repair.Apply()) {
                usedSolidify = false;
                fallbackReason = "failed";
                return null;
            }
            usedSolidify = repair.UsedSolidify;
            fallbackReason = repair.FallbackReason;       // "" if the repair result was clean, eg "self-intersections"
            return repair.Mesh;                           // MeshRobustRepair.NotCleanReason(repair.Mesh) == ""
        }
    }
}
