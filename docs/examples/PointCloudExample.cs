using System;
using System.Collections.Generic;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Outward normal estimation (PointCloudNormals) and surface reconstruction (PointCloudSurface)
    /// from an unoriented 3D point cloud.
    /// </summary>
    public static class PointCloudExample
    {
        /// <summary> Generates a deterministic Fibonacci-sphere point cloud of radius 1. </summary>
        public static List<Vector3d> SampleUnitSphere(int count)
        {
            var pts = new List<Vector3d>(count);
            double golden = Math.PI * (3.0 - Math.Sqrt(5.0));
            for (int i = 0; i < count; ++i) {
                double y = 1.0 - (2.0 * i + 1.0) / count;
                double r = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
                double theta = golden * i;
                pts.Add(new Vector3d(Math.Cos(theta) * r, y, Math.Sin(theta) * r));
            }
            return pts;
        }

        /// <summary> Estimates consistently outward-oriented unit normals on an unoriented point cloud. </summary>
        public static PointCloudNormals EstimateNormals(IList<Vector3d> points)
        {
            var est = new PointCloudNormals(points) { Orient = true };
            est.Compute();
            return est;
        }

        /// <summary> Reconstructs a closed manifold surface from an unoriented point cloud. </summary>
        public static PointCloudSurface ReconstructSurface(IList<Vector3d> points)
        {
            var surf = new PointCloudSurface(points);
            surf.Compute();
            return surf;
        }
    }
}
