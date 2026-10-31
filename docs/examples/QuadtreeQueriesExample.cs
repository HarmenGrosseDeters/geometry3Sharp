using System.Collections.Generic;
using g3;

namespace g3.Examples
{
    /// <summary>Build once from geometry snapshots and reuse caller-owned buffers for XY queries.</summary>
    public static class QuadtreeQueriesExample
    {
        public static double Run()
        {
            var triangles = new[] {
                new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(10, 0, 10), new Vector3d(0, 10, 20))
            };
            var tree = new TriangleQuadtree2d(triangles);
            var ids = new List<int>(16);
            tree.QueryRectangle(new AxisAlignedBox2d(0, 0, 2, 2), ids);
            if (ids.Count != 1 || ids[0] != 0) throw new System.InvalidOperationException("Missing triangle");
            tree.FindNearestK(new Vector2d(1, 1), 1, ids);
            if (ids.Count != 1 || ids[0] != 0) throw new System.InvalidOperationException("Missing nearest triangle");
            if (!tree.TryGetHeight(new Vector2d(1, 1), out double height, tolerance: 0))
                throw new System.InvalidOperationException("Expected an exact height hit");
            return height;
        }
    }
}
