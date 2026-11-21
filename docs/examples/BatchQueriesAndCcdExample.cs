using g3;

namespace g3.Examples
{
    /// <summary>
    /// Deterministic batch ray/point/inside queries (MeshBatchQueries)
    /// and continuous collision detection for moving triangles (ContinuousCollision).
    /// </summary>
    public static class BatchQueriesAndCcdExample
    {
        /// <summary> Runs batch ray-intersection and inside queries against a unit cube. </summary>
        public static BatchStatus RunBatchQueries(out RayHit[] hits, out InsideStatus[] inside)
        {
            DMesh3 cube = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
            var tree = new DMeshAABBTree3(cube, autoBuild: true);

            var rays = new[] {
                new Ray3d(new Vector3d(0, 0, -5), Vector3d.AxisZ),     // hits the z = -1 face at t = 4
                new Ray3d(new Vector3d(5, 5, -5), Vector3d.AxisZ)      // misses
            };
            hits = new RayHit[rays.Length];
            BatchStatus rs = MeshBatchQueries.IntersectBatch(tree, rays, hits);
            if (rs != BatchStatus.Ok) {
                inside = new InsideStatus[0];
                return rs;
            }

            var points = new[] { new Vector3d(0, 0, 0), new Vector3d(5, 0, 0) };
            inside = new InsideStatus[points.Length];
            return MeshBatchQueries.IsInsideBatch(tree, points, inside, InsideMethod.Crossings);
        }

        /// <summary>
        /// Detects the first time of impact of a point moving linearly from z = +1 to z = -1
        /// across a stationary triangle in the z = 0 plane (exact impact at t = 0.5).
        /// </summary>
        public static CcdResult VertexCrossingTriangle()
        {
            Vector3d a = new Vector3d(-1, -1, 0), b = new Vector3d(2, -1, 0), c = new Vector3d(-1, 2, 0);
            Vector3d p0 = new Vector3d(0, 0, 1), p1 = new Vector3d(0, 0, -1);
            return ContinuousCollision.VertexTriangle(p0, a, b, c, p1, a, b, c);
        }
    }
}
