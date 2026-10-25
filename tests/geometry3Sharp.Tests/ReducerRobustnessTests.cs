using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The Reducer must not depend on where the mesh is (UTM coordinates) or on its units, and must stay
    /// stable on noisy, nearly flat data. Deviation is measured with the certified Hausdorff distance.
    /// </summary>
    public class ReducerRobustnessTests
    {
        static DMesh3 Terrain(int n = 101, double noise = 0, int seed = 1)
        {
            var rnd = new Random(seed);
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[n, n];
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double x = -50 + 100.0 * i / (n - 1), y = -50 + 100.0 * j / (n - 1);
                    double z = 3 * Math.Sin(0.1 * x) * Math.Cos(0.13 * y) + noise * (2 * rnd.NextDouble() - 1);
                    vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, z));
                }
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return mesh;
        }

        static DMesh3 Reduce(DMesh3 mesh, int triangles)
        {
            DMesh3 r = new DMesh3(mesh);
            new Reducer(r).ReduceToTriangleCount(triangles);
            return r;
        }

        static double RelativeHausdorff(DMesh3 a, DMesh3 b)
        {
            var h = new MeshHausdorffDistance(a, b) { RelativeTolerance = 1e-5 };
            Assert.True(h.Compute());
            return h.UpperBound / a.CachedBounds.DiagonalLength;
        }

        [Fact]
        public void UtmCoordinates_SameQualityAsAtOrigin()
        {
            DMesh3 local = Terrain();
            DMesh3 utm = new DMesh3(local);
            MeshTransforms.Translate(utm, new Vector3d(632000, 5712000, 100));
            double hLocal = RelativeHausdorff(local, Reduce(local, 2000));
            double hUtm = RelativeHausdorff(utm, Reduce(utm, 2000));
            Assert.True(Math.Abs(hUtm - hLocal) <= 0.05 * hLocal, $"deviation {hUtm:E3} at UTM vs {hLocal:E3} at the origin");
        }

        [Theory]
        [InlineData(1e-3)]
        [InlineData(1e3)]
        public void Units_SameQuality(double scale)
        {
            DMesh3 mesh = Terrain();
            DMesh3 scaled = new DMesh3(mesh);
            MeshTransforms.Scale(scaled, scale);
            double h = RelativeHausdorff(mesh, Reduce(mesh, 2000));
            double hs = RelativeHausdorff(scaled, Reduce(scaled, 2000));
            Assert.True(Math.Abs(hs - h) <= 0.05 * h, $"deviation {hs:E3} at scale {scale} vs {h:E3}");
        }

        [Fact]
        public void NoisyFlatSurface_NoSpikes()
        {
            // nearly flat: plane quadrics are nearly singular in the plane directions
            DMesh3 mesh = Terrain(81, 0.001);
            foreach (int vid in mesh.VertexIndices().ToArray()) {
                Vector3d v = mesh.GetVertex(vid);
                mesh.SetVertex(vid, new Vector3d(v.x, v.y, 0.001 * v.z));
            }
            DMesh3 reduced = Reduce(mesh, 400);
            AxisAlignedBox3d b = mesh.CachedBounds;
            foreach (int vid in reduced.VertexIndices()) {
                Vector3d v = reduced.GetVertex(vid);
                Assert.True(b.Contains(v) || b.Distance(v) < 1e-3, $"vertex {v} left the bounds");
            }
            Assert.True(RelativeHausdorff(mesh, reduced) < 1e-4);
        }
    }
}
