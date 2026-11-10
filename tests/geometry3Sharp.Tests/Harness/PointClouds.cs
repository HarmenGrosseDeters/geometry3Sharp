using System;
using System.Collections.Generic;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary> Procedural point samplings with known outward normals (no files). </summary>
    public static class PointClouds
    {
        public const double TorusR = 3, Torusr = 1;

        /// <summary>
        /// n points on a torus (genus 1), area-weighted, with their true outward normals (of the noise-free point).
        /// sigma: Gaussian noise; hole: a removed patch; nonUniform: a 10:1 density ratio along the ring
        /// </summary>
        public static List<Vector3d> Torus(int n, int seed, out List<Vector3d> normals, double sigma = 0, bool hole = false, bool nonUniform = false)
        {
            var rnd = new Random(seed);
            var pts = new List<Vector3d>();
            normals = new List<Vector3d>();
            double Gauss() { double a = 1 - rnd.NextDouble(), b = rnd.NextDouble(); return Math.Sqrt(-2 * Math.Log(a)) * Math.Cos(2 * Math.PI * b); }
            while (pts.Count < n) {
                double u = rnd.NextDouble() * 2 * Math.PI, v = rnd.NextDouble() * 2 * Math.PI;
                if (rnd.NextDouble() > (TorusR + Torusr * Math.Cos(v)) / (TorusR + Torusr)) continue;
                if (nonUniform && rnd.NextDouble() > 0.1 + 0.9 * (Math.Cos(u) + 1) / 2) continue;
                if (hole && Math.Abs(u - 1) < 0.5 && Math.Abs(v - 0.5) < 0.6) continue;
                var p = new Vector3d((TorusR + Torusr * Math.Cos(v)) * Math.Cos(u), (TorusR + Torusr * Math.Cos(v)) * Math.Sin(u), Torusr * Math.Sin(v));
                normals.Add(new Vector3d(Math.Cos(v) * Math.Cos(u), Math.Cos(v) * Math.Sin(u), Math.Sin(v)));
                pts.Add(p + new Vector3d(Gauss(), Gauss(), Gauss()) * sigma);
            }
            return pts;
        }

        /// <summary> n points on a sphere of the given radius around the origin, with the outward normals </summary>
        public static List<Vector3d> Sphere(int n, int seed, double radius, out List<Vector3d> normals)
        {
            var rnd = new Random(seed);
            var pts = new List<Vector3d>();
            normals = new List<Vector3d>();
            while (pts.Count < n) {
                var d = new Vector3d(2 * rnd.NextDouble() - 1, 2 * rnd.NextDouble() - 1, 2 * rnd.NextDouble() - 1);
                double l = d.Length;
                if (l > 1 || l < 1e-3) continue;
                d /= l;
                normals.Add(d);
                pts.Add(radius * d);
            }
            return pts;
        }
    }
}
