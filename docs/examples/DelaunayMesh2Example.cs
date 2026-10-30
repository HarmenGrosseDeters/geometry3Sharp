using System;
using System.Collections.Generic;
using System.Threading;

namespace g3.Examples
{
    /// <summary>Point-set Delaunay triangulation over the convex hull, without constraints or snapping.</summary>
    public static class DelaunayMesh2Example
    {
        /// <summary>Returns an owned mesh, including an empty mesh for collinear input; failures throw.</summary>
        public static TriangulationMesh2 Triangulate(IReadOnlyList<Vector2d> points,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new DelaunayMesh2().Compute(points, cancellationToken);
            if (result.Status == Triangulation2Status.Failed)
                throw new InvalidOperationException("Triangulation failed: " + result.FailureKind);
            return result.Mesh;
        }
    }
}
