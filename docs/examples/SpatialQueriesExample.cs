using g3;

namespace g3.Examples
{
    /// <summary>
    /// Spatial queries with DMeshAABBTree3 (bounding box tree over the triangles): nearest point, ray hits,
    /// inside test and winding number. Build the tree once and reuse it; rebuild it after editing the mesh.
    /// </summary>
    public static class SpatialQueriesExample
    {
        public class Result
        {
            public Vector3d NearestPoint;
            public double Distance;
            public Vector3d RayHit;
            public bool CenterInside, FarPointInside;
            public double CenterWinding;
        }

        public static Result Run(DMesh3 mesh, Vector3d query)
        {
            var tree = new DMeshAABBTree3(mesh, autoBuild: true);   // or new DMeshAABBTree3(mesh) and tree.Build()
            var result = new Result();

            // nearest point on the surface
            int tid = tree.FindNearestTriangle(query);               // DMesh3.InvalidID if the mesh is empty
            DistPoint3Triangle3 dist = MeshQueries.TriangleDistance(mesh, tid, query);
            result.NearestPoint = dist.TriangleClosest;
            result.Distance = dist.Get();

            // first hit along a ray
            var ray = new Ray3d(query, (mesh.GetBounds().Center - query).Normalized);
            int hitTid = tree.FindNearestHitTriangle(ray);
            if (hitTid != DMesh3.InvalidID) {
                IntrRay3Triangle3 hit = MeshQueries.TriangleIntersection(mesh, hitTid, ray);
                result.RayHit = ray.PointAt(hit.RayParameter);
            }

            // inside tests: IsInside counts ray crossings (closed meshes); the winding number is ~1 inside, ~0 outside
            // and also works for open or broken meshes (FastWindingNumber is the approximate, fast version)
            Vector3d center = mesh.GetBounds().Center;
            result.CenterInside = tree.IsInside(center);
            result.FarPointInside = tree.IsInside(center + 100 * mesh.GetBounds().Diagonal);
            result.CenterWinding = tree.WindingNumber(center);
            return result;
        }
    }
}
