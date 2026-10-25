// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3;

namespace gs
{
    /// <summary>
    /// Offset surface of a terrain (TIN / height field) at a perpendicular distance: Distance > 0 above,
    /// &lt; 0 below the terrain (eg a safety layer or minimum cover below the surface). The offset is the
    /// boundary of the band of points within |Distance| of the terrain on that side: concave break lines
    /// stay sharp, convex ones become the round arcs of the true offset.
    ///
    /// On each vertical line, starting |Distance| above/below the terrain (where the distance is at most
    /// |Distance|), the first point farther than |Distance| is found by stepping and bisection; the
    /// direction from its closest terrain point is the exact normal of the offset. The surface is built
    /// with TerrainDualContouring, so the normals are Hermite data and it has few triangles.
    /// </summary>
    public class TerrainOffset
    {
        public DMesh3 Terrain;
        public double Distance;

        /// <summary> cell size of the result (0 = larger XY extent / GridResolution) </summary>
        public double CellSize = 0;
        public int GridResolution = 256;
        /// <summary> RMS merge tolerance of the quadtree (0 = 0.05 * cell size), see TerrainDualContouring </summary>
        public double Tolerance = 0;

        public Func<bool> CancelF = () => { return false; };

        // output
        public DMesh3 Result;


        public TerrainOffset(DMesh3 terrain, double distance)
        {
            Terrain = terrain;
            Distance = distance;
        }


        public bool Compute()
        {
            if (Terrain.TriangleCount == 0 || Distance == 0)
                return false;
            spatial = new DMeshAABBTree3(Terrain, true);
            AxisAlignedBox3d b = Terrain.CachedBounds;
            top = b.Max.z + 1 + Math.Abs(Distance);
            depth = b.Depth + 2 + 2 * Math.Abs(Distance);
            AxisAlignedBox3d bounds = b;
            bounds.Min.z -= Math.Abs(Distance);
            bounds.Max.z += Math.Abs(Distance);
            var tdc = new TerrainDualContouring() {
                SampleF = sample, SampleBounds = bounds, CellSize = CellSize, GridResolution = GridResolution,
                Tolerance = Tolerance, CancelF = CancelF
            };
            if (tdc.Compute() == false)
                return false;
            Result = tdc.Result;
            return true;
        }

        DMeshAABBTree3 spatial;
        double top, depth;

        // distance to the terrain and its closest point
        double distance(Vector3d p, out Vector3d closest)
        {
            // only distances up to about 2 |Distance| matter (farther = outside the band)
            int tid = spatial.FindNearestTriangle(p, 2 * Math.Abs(Distance));
            if (tid < 0) {
                closest = p;
                return double.MaxValue;
            }
            DistPoint3Triangle3 q = MeshQueries.TriangleDistance(Terrain, tid, p);
            closest = q.TriangleClosest;
            return Math.Sqrt(q.DistanceSquared);
        }

        bool sample(double x, double y, out double z, out Vector3d normal)
        {
            z = 0; normal = Vector3d.AxisZ;
            // terrain height (top surface)
            Ray3d ray = new Ray3d(new Vector3d(x, y, top), -Vector3d.AxisZ);
            int tid = spatial.FindNearestHitTriangle(ray, depth);
            if (tid < 0)
                return false;
            IntrRay3Triangle3 intr = MeshQueries.TriangleIntersection(Terrain, tid, ray);
            if (intr.Find() == false)
                return false;
            double zs = ray.PointAt(intr.RayParameter).z;

            double d = Math.Abs(Distance), dir = Math.Sign(Distance);
            Vector3d c;
            // inside the band at the vertical distance d; step outwards until farther than d
            double zin = zs + dir * d, zout = double.NaN;
            double step = 0.25 * d;
            for (int k = 1; k <= 400; ++k) {
                double zk = zin + dir * step;
                if (distance(new Vector3d(x, y, zk), out c) > d) { zout = zk; break; }
                zin = zk;
            }
            if (double.IsNaN(zout))
                return false;
            for (int k = 0; k < 50; ++k) {
                double zm = 0.5 * (zin + zout);
                if (distance(new Vector3d(x, y, zm), out c) > d) zout = zm; else zin = zm;
                if (Math.Abs(zout - zin) < 1e-6 * (1 + d)) break;
            }
            z = 0.5 * (zin + zout);
            Vector3d p = new Vector3d(x, y, z);
            distance(p, out c);
            normal = (p - c).Normalized;
            return true;
        }
    }
}
