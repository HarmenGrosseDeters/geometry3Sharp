using System.Collections.Generic;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Intrinsic triangulations: the same surface and vertices as the mesh, but Delaunay (and refined by
    /// inserting vertices), so the cotan Laplacian has no negative weights however poor the triangles are.
    /// The mesh is not modified.
    /// </summary>
    public static class IntrinsicTriangulationExample
    {
        /// <summary>
        /// Cotan Laplacian of the refined intrinsic Delaunay triangulation, in its vertex indices: the mesh
        /// vertices first (T.VertexID(i) is the mesh vertex id), then the inserted ones (VertexID -1).
        /// </summary>
        public static SymmetricSparseMatrix RefinedLaplacian(DMesh3 mesh, out IntrinsicTriangulation T, out IntrinsicRefinementResult result)
        {
            T = IntrinsicTriangulation.BuildDelaunay(mesh, true, out result, 25);   // min angle 25 degrees
            // result.Status: Success, InsertionLimitReached or Incomplete; result.SkippedTriangles: at small input angles
            return T.CotanLaplacian();
        }

        /// <summary> geodesic distance (heat method) on the refined intrinsic triangulation, at the mesh vertices; null if it failed </summary>
        public static double[] RefinedGeodesic(DMesh3 mesh, int source, out IntrinsicRefinementResult result)
        {
            var geodesic = new MeshGeodesicDistance(mesh) { IntrinsicRefine = true };
            bool ok = geodesic.Compute(new[] { source });
            result = geodesic.Refinement;
            return ok ? geodesic.Distance : null;
        }

        /// <summary> the overlay of the intrinsic and the input triangles, as a mesh (triangle groups: intrinsic triangle) </summary>
        public static DMesh3 CommonSubdivision(IntrinsicTriangulation T, out CommonSubdivisionStatus status)
        {
            IntrinsicCommonSubdivision overlay = T.ExtractCommonSubdivision();
            status = overlay.Status;           // Inconsistent (with overlay.Error) instead of wrong faces
            return overlay.ToMesh();
        }
    }
}
