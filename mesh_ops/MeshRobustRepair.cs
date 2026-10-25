// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// Repair that ends with a clean solid wherever possible: MeshAutoRepair (with the post-repair
    /// cleanup) first, which keeps the input triangles; if its result is not clean, its self-intersections
    /// are resolved exactly (ExactResolve: ExactMeshBoolean.Resolve, which keeps the geometry); if that is not
    /// clean either, the repaired mesh is solidified (winding number, adaptive dual contouring), which gives a
    /// closed, manifold surface without self-intersections at the cost of remeshing it (deviation about a
    /// cell, see SolidifyResolution).
    ///
    /// Clean means: closed, no bowties, no degenerate triangles, no inside-out components and, if
    /// CheckSelfIntersections, no self-intersections (exact, MeshRepairMetrics).
    ///
    /// A clean result can still have lost volume (eg a self-overlapping input made non-overlapping by
    /// dropping a layer): if its solid agrees with the input's winding number solid less than MinVolumeIoU,
    /// the input itself is solidified instead (its winding number solid is the reference).
    /// </summary>
    public class MeshRobustRepair
    {
        public DMesh3 Mesh;
        /// <summary> cells along the bounding box diagonal for the solidify fallback </summary>
        public int SolidifyResolution = 128;
        public bool CheckSelfIntersections = true;
        /// <summary> minimum volume IoU of the repaired mesh with the input's winding number solid (0 = no check) </summary>
        public double MinVolumeIoU = 0.99;
        /// <summary> configures the first stage (eg tolerances) </summary>
        public Action<MeshAutoRepair> ConfigureRepair = null;
        /// <summary> replaces the first stage (input -> repaired mesh); null = MeshAutoRepair </summary>
        public Func<DMesh3, DMesh3> RepairF = null;
        public Func<bool> CancelF = () => { return false; };
        /// <summary>
        /// before solidifying: resolve the self-intersections exactly (the solid where the winding number is
        /// positive, ExactMeshBoolean.Resolve); used if the result is clean and its volume agrees with the input.
        /// Only for meshes that are closed in the winding number sense (exact winding numbers).
        /// </summary>
        public bool ExactResolve = true;
        /// <summary> rounding the new vertices to double can create tiny self-intersections: resolve again, up to this often </summary>
        public int ExactResolveRounds = 4;

        // outputs
        public bool UsedSolidify;
        /// <summary> why the repair result was not used ("" if it was) </summary>
        public string FallbackReason = "";
        /// <summary> volume IoU of the repair result with the input (NaN if the input has no solid or not checked) </summary>
        public double RepairVolumeIoU = double.NaN;
        public MeshWindingNumberSolidify Solidify;
        public bool UsedExactResolve;
        /// <summary> why the exact resolve was not used ("" if it was or was not tried) </summary>
        public string ExactResolveReason = "";
        /// <summary> resolves done by the exact stage (0 if not tried) </summary>
        public int ExactResolveRoundsUsed;
        /// <summary> per resolve round: self-intersecting triangle pairs of its result (diagnostics) </summary>
        public List<int> ExactResolveSelfIntersections = new List<int>();

        public MeshRobustRepair(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public bool Apply()
        {
            UsedSolidify = false;
            UsedExactResolve = false;
            ExactResolveReason = "";
            ExactResolveRoundsUsed = 0;
            ExactResolveSelfIntersections.Clear();
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
            if (ExactResolve) {
                DMesh3 exact = resolve_exactly(solidifyFrom, input);
                if (CancelF())
                    return false;
                if (exact != null) {
                    Mesh = exact;
                    UsedExactResolve = true;
                    return true;
                }
            }
            Solidify = new MeshWindingNumberSolidify(solidifyFrom) { GridResolution = SolidifyResolution, Adaptive = true, CancelF = CancelF };
            if (Solidify.Compute() == false || Solidify.Result == null || Solidify.Result.TriangleCount == 0)
                return false;
            Mesh = Solidify.Result;
            UsedSolidify = true;
            return true;
        }

        // the solid of the mesh with its self-intersections resolved exactly, or null (ExactResolveReason says why)
        DMesh3 resolve_exactly(DMesh3 from, DMesh3 input)
        {
            // inside-out parts flipped, so that the solid is where the winding number is positive
            DMesh3 m = WindingContouring.OrientedCopy(from);
            if (ExactMeshBoolean.IsWindingClosed(m) == false) {
                ExactResolveReason = "not closed (winding number)";
                return null;
            }
            for (int round = 0; round < ExactResolveRounds; ++round) {
                if (CancelF())
                    return null;
                DMesh3 res = ExactMeshBoolean.Resolve(m);
                ExactResolveRoundsUsed++;
                if (res == null || res.TriangleCount == 0) {
                    ExactResolveReason = "resolve failed";
                    return null;
                }
                // slivers where intersection curves nearly meet: collapsed or flipped by the cleanup
                string reason = NotCleanReason(res, false);
                if (reason == "degenerate triangles") {
                    new MeshPostRepairCleanup(res).Apply();
                    res = new DMesh3(res, true);
                }
                // where solids touch (at a vertex or along an edge) Resolve gives manifold sheets with duplicated
                // vertices, which touch: separate them minimally
                SeparateTouchingVertices(res, SeparateFraction);
                reason = NotCleanReason(res, false);
                if (reason == "" && CheckSelfIntersections) {
                    int si = MeshRepairMetrics.SelfIntersections(res);
                    ExactResolveSelfIntersections.Add(si);
                    if (si > 0)
                        reason = "self-intersections";
                }
                if (reason == "self-intersections") {
                    // from rounding the new vertices (or the cleanup): tiny, resolved by the next round
                    ExactResolveReason = reason;
                    m = res;
                    continue;
                }
                if (reason != "") {
                    ExactResolveReason = reason;
                    return null;
                }
                if (MinVolumeIoU > 0) {
                    double missing, extra;
                    double iou = MeshRepairMetrics.VolumeIoU(input, res, out missing, out extra);
                    if (iou < MinVolumeIoU) {        // (NaN: the input has no solid to compare with)
                        ExactResolveReason = "volume differs from the input";
                        return null;
                    }
                    RepairVolumeIoU = iou;
                }
                ExactResolveReason = "";
                return res;
            }
            return null;
        }

        /// <summary> how far SeparateTouchingVertices moves a vertex, relative to the distance to the centroid of its neighbours </summary>
        public double SeparateFraction = 1e-4;

        /// <summary>
        /// Vertices at the same position (eg from splitting a non-manifold vertex or edge into manifold sheets)
        /// make their sheets touch there. Each is moved by fraction of the way towards the centroid of its
        /// neighbours, so the sheets separate. Returns the number of vertices moved.
        /// </summary>
        public static int SeparateTouchingVertices(DMesh3 mesh, double fraction)
        {
            var at = new Dictionary<Vector3d, List<int>>();
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                List<int> list;
                if (at.TryGetValue(v, out list) == false)
                    at[v] = list = new List<int>();
                list.Add(vid);
            }
            var moves = new List<KeyValuePair<int, Vector3d>>();
            foreach (var kv in at) {
                if (kv.Value.Count < 2)
                    continue;
                foreach (int vid in kv.Value) {
                    Vector3d c = Vector3d.Zero;
                    int n = 0;
                    foreach (int nb in mesh.VtxVerticesItr(vid)) {
                        c += mesh.GetVertex(nb);
                        n++;
                    }
                    if (n > 0)
                        moves.Add(new KeyValuePair<int, Vector3d>(vid, kv.Key + fraction * (c / n - kv.Key)));
                }
            }
            foreach (var m in moves)
                mesh.SetVertex(m.Key, m.Value);
            return moves.Count;
        }

        /// <summary> "" if the mesh is clean (see class comment), otherwise the first problem found </summary>
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
