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
    /// </summary>
    public class MeshRobustRepair
    {
        public DMesh3 Mesh;
        /// <summary> cells along the bounding box diagonal for the solidify fallback </summary>
        public int SolidifyResolution = 128;
        public bool CheckSelfIntersections = true;
        /// <summary> configures the first stage (eg tolerances) </summary>
        public Action<MeshAutoRepair> ConfigureRepair = null;
        public Func<bool> CancelF = () => { return false; };

        // outputs
        public bool UsedSolidify;
        /// <summary> why the repair result was not clean ("" if it was) </summary>
        public string FallbackReason = "";
        public MeshWindingNumberSolidify Solidify;

        public MeshRobustRepair(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public bool Apply()
        {
            UsedSolidify = false;
            FallbackReason = "";
            var repair = new MeshAutoRepair(Mesh) { PostRepairCleanup = true };
            ConfigureRepair?.Invoke(repair);
            repair.Apply();
            DMesh3 result = repair.Mesh;
            if (CancelF())
                return false;
            FallbackReason = NotCleanReason(result, CheckSelfIntersections);
            if (FallbackReason == "") {
                Mesh = result;
                return true;
            }
            if (result.TriangleCount == 0)
                return false;
            Solidify = new MeshWindingNumberSolidify(result) { GridResolution = SolidifyResolution, Adaptive = true, CancelF = CancelF };
            if (Solidify.Compute() == false || Solidify.Result == null || Solidify.Result.TriangleCount == 0)
                return false;
            Mesh = Solidify.Result;
            UsedSolidify = true;
            return true;
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
