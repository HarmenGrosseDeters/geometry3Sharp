// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// Repair that ends with a clean solid wherever possible: MeshAutoRepair (with the post-repair
    /// cleanup) first, which keeps the input triangles; if its result is not clean, the repaired mesh is
    /// solidified (winding number, adaptive dual contouring), which gives a closed, manifold surface without
    /// self-intersections at the cost of remeshing it (deviation about a cell, see SolidifyResolution).
    ///
    /// Clean means: closed, no bowties, no degenerate triangles, no inside-out components and, if
    /// CheckSelfIntersections, no self-intersections (exact, MeshRepairMetrics).
    ///
    /// A clean result can still have lost volume (eg a self-overlapping input made non-overlapping by
    /// dropping a layer): if its solid agrees with the input's winding number solid less than MinVolumeIoU,
    /// the input itself is solidified instead (its winding number solid is the reference).
    ///
    /// Behaviour: the result is in <see cref="Mesh"/> after Apply(), a new mesh object. The mesh passed to the
    /// constructor is used as the working mesh of MeshAutoRepair and is modified by it, so pass a copy if
    /// you need the input afterwards. Check UsedSolidify / FallbackReason to see which path was taken.
    /// Cost: the self-intersection check and the volume comparison are cheap compared to the solidify
    /// fallback, whose cost grows with SolidifyResolution.
    ///
    /// Use MeshAutoRepair alone if the input triangles must be kept (no remeshing), MeshWindingNumberSolidify
    /// to always remesh, ExactMeshBoolean.Resolve to resolve self-intersections exactly without remeshing.
    /// </summary>
    public class MeshRobustRepair
    {
        /// <summary> input mesh; after a successful Apply() the repaired (or solidified) result </summary>
        public DMesh3 Mesh;
        /// <summary> cells along the bounding box diagonal for the solidify fallback </summary>
        public int SolidifyResolution = 128;
        /// <summary> if true, self-intersections make the repair result unclean (triggers the solidify fallback) </summary>
        public bool CheckSelfIntersections = true;
        /// <summary> minimum volume IoU of the repaired mesh with the input's winding number solid (0 = no check) </summary>
        public double MinVolumeIoU = 0.99;
        /// <summary> configures the first stage (eg tolerances) </summary>
        public Action<MeshAutoRepair> ConfigureRepair = null;
        /// <summary> replaces the first stage (input -> repaired mesh); null = MeshAutoRepair </summary>
        public Func<DMesh3, DMesh3> RepairF = null;
        /// <summary> return true to cancel (checked after the repair stage and during solidify) </summary>
        public Func<bool> CancelF = () => { return false; };

        // outputs
        /// <summary> true if the result was produced by the solidify fallback (remeshed), false if it is the repair result </summary>
        public bool UsedSolidify;
        /// <summary> why the repair result was not used ("" if it was) </summary>
        public string FallbackReason = "";
        /// <summary> volume IoU of the repair result with the input (NaN if the input has no solid or not checked) </summary>
        public double RepairVolumeIoU = double.NaN;
        /// <summary> the solidify fallback, if it ran (for its statistics); null otherwise </summary>
        public MeshWindingNumberSolidify Solidify;

        /// <summary> the mesh is modified by the repair stage (see class comment) </summary>
        public MeshRobustRepair(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        /// <summary>
        /// Repair, check, and solidify if needed (see class comment). On success Mesh is the result.
        /// </summary>
        /// <returns>
        /// false if cancelled, if the mesh to solidify is empty, or if the solidify fallback failed (eg the
        /// grid would be too large); Mesh is then not replaced. true otherwise.
        /// </returns>
        public bool Apply()
        {
            UsedSolidify = false;
            FallbackReason = "";
            RepairVolumeIoU = double.NaN;
            DMesh3 input = new DMesh3(Mesh);
            DMesh3 result;
            if (RepairF != null) {
                result = RepairF(Mesh);
            } else {
                var repair = new MeshAutoRepair(Mesh) { PostRepairCleanup = true };
                ConfigureRepair?.Invoke(repair);
                repair.Apply();
                result = repair.Mesh;
            }
            if (CancelF())
                return false;
            FallbackReason = NotCleanReason(result, CheckSelfIntersections);
            DMesh3 solidifyFrom = result;
            if (FallbackReason == "" && MinVolumeIoU > 0) {
                double missing, extra;
                RepairVolumeIoU = MeshRepairMetrics.VolumeIoU(input, result, out missing, out extra);
                if (RepairVolumeIoU < MinVolumeIoU) {        // (NaN: the input has no solid to compare with)
                    FallbackReason = "volume differs from the input";
                    solidifyFrom = input;
                }
            }
            if (FallbackReason == "") {
                Mesh = result;
                return true;
            }
            if (solidifyFrom.TriangleCount == 0)
                return false;
            Solidify = new MeshWindingNumberSolidify(solidifyFrom) { GridResolution = SolidifyResolution, Adaptive = true, CancelF = CancelF };
            if (Solidify.Compute() == false || Solidify.Result == null || Solidify.Result.TriangleCount == 0)
                return false;
            Mesh = Solidify.Result;
            UsedSolidify = true;
            return true;
        }

        /// <summary>
        /// "" if the mesh is clean (see class comment), otherwise the first problem found, one of "empty",
        /// "not closed", "bowties", "degenerate triangles" (area &lt; 1e-12 * diagonal^2), "inside-out components",
        /// "self-intersections". Useful as a test assertion: NotCleanReason(mesh) == "".
        /// </summary>
        public static string NotCleanReason(DMesh3 mesh, bool checkSelfIntersections = true)
        {
            if (mesh.TriangleCount == 0) return "empty";
            if (mesh.IsClosed() == false) return "not closed";
            if (MeshIterators.BowtieVertices(mesh).Any()) return "bowties";
            double diag = mesh.CachedBounds.DiagonalLength;
            double areaEps = 1e-12 * diag * diag;
            if (mesh.TriangleIndices().Any(t => mesh.GetTriArea(t) < areaEps)) return "degenerate triangles";
            if (MeshPostRepairCleanup.FindInvertedComponents(mesh).Count > 0) return "inside-out components";
            if (checkSelfIntersections && MeshRepairMetrics.SelfIntersections(mesh) > 0) return "self-intersections";
            return "";
        }
    }
}
