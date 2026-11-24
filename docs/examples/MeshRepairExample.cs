using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Mesh repair. MeshAutoRepair keeps the input triangles where it can (welds, removes degenerate and
    /// duplicate parts, fills holes, orients). MeshRobustRepair runs it; when the result is not clean (open,
    /// non-manifold or self-intersecting) it resolves the self-intersections exactly (keeps the geometry),
    /// falls back to winding number solidify when that is not clean either, and to an alpha wrap as the
    /// last resort (AlphaWrapFallback).
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

        /// <summary>
        /// The lost-area report (MeasureLostArea, opt-in): the share of the input's surface that is farther than the stage's tolerance from the
        /// result. An interior wall of a solid is lost by design (the result is the solid), so a large share is not damage by itself.
        /// </summary>
        public static double LostAreaShare(DMesh3 input, out int lostTriangles, out double maxDistance)
        {
            var repair = new MeshRobustRepair(new DMesh3(input)) { MeasureLostArea = true };
            if (!repair.Apply()) {
                lostTriangles = 0;
                maxDistance = double.NaN;
                return double.NaN;
            }
            lostTriangles = repair.LostTriangleIds.Count;       // ids of input's triangles, to locate the loss
            maxDistance = repair.LostAreaMaxDistance;           // the largest distance of any sample of the input to the result
            return repair.LostAreaShare;                        // repair.LostArea is the area; both sides of a sheet count
        }
    }
}
