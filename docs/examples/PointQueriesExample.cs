using System.Collections.Generic;
using g3;

namespace g3.Examples
{
    /// <summary>
    /// Nearest neighbours among points: PointHashGrid3d (hash grid, best for radius queries with a known radius,
    /// supports moving points) and PointAABBTree3 (bounding box tree over the vertices of a mesh / point set).
    /// </summary>
    public static class PointQueriesExample
    {
        /// <summary> Nearest of points to query within radius, or -1. </summary>
        public static int NearestInRadius(IList<Vector3d> points, Vector3d query, double radius)
        {
            var grid = new PointHashGrid3d<int>(radius, -1);    // cell size about the query radius; -1 = "none"
            for (int i = 0; i < points.Count; ++i)
                grid.InsertPoint(i, points[i]);
            KeyValuePair<int, double> nearest = grid.FindNearestInRadius(query, radius, i => points[i].Distance(query));
            return nearest.Key;      // nearest.Value: its distance
        }

        /// <summary> Nearest vertex of mesh to query (any distance). </summary>
        public static int NearestVertex(DMesh3 mesh, Vector3d query)
        {
            var tree = new PointAABBTree3(mesh, autoBuild: true);
            return tree.FindNearestPoint(query);
        }
    }
}
