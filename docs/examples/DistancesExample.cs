using System.Collections.Generic;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Distances: Hausdorff distance between two meshes (a certified interval) and geodesic distance on a
    /// mesh (heat method). Both leave the meshes unchanged.
    /// </summary>
    public static class DistancesExample
    {
        /// <summary> Symmetric Hausdorff distance; the exact value lies in [lower, upper]. Returns false if it failed. </summary>
        public static bool Hausdorff(DMesh3 a, DMesh3 b, out double lower, out double upper)
        {
            var hausdorff = new MeshHausdorffDistance(a, b) { Tolerance = 1e-4 };   // absolute; default: 1e-4 of the bounding box diagonal
            bool ok = hausdorff.Compute();
            lower = hausdorff.LowerBound;
            upper = hausdorff.UpperBound;    // hausdorff.WorstPoint: where it is attained
            return ok && hausdorff.Converged;
        }

        /// <summary>
        /// Geodesic distance from vertex source to every vertex (by vertex id; NaN if unreachable), or null.
        /// Accuracy about 2% plus half an edge length; a few percent worse near open boundaries.
        /// </summary>
        public static double[] Geodesic(DMesh3 mesh, int source, out List<Vector3d> pathToFarthest)
        {
            var geodesic = new MeshGeodesicDistance(mesh);
            pathToFarthest = null;
            if (!geodesic.Compute(new[] { source }))
                return null;
            int farthest = source;
            foreach (int vid in mesh.VertexIndices())
                if (geodesic.Distance[vid] > geodesic.Distance[farthest])
                    farthest = vid;
            pathToFarthest = geodesic.TracePath(farthest);    // polyline back to the nearest source
            return geodesic.Distance;
        }
    }
}
