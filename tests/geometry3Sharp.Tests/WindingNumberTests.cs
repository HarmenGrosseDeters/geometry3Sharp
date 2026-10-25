using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// DMeshAABBTree3.WindingNumber must match the plain sum of triangle solid angles, also for meshes that
    /// are closed but inconsistently oriented (common in scanned / exported data).
    /// </summary>
    public class WindingNumberTests
    {
        static double BruteForce(DMesh3 mesh, Vector3d p)
        {
            double sum = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in mesh.TriangleIndices()) {
                mesh.GetTriVertices(tid, ref a, ref b, ref c);
                sum += MathUtil.TriSolidAngle(a, b, c, ref p);
            }
            return sum / MathUtil.FourPI;
        }

        static DMesh3 Sphere()
        {
            return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void WindingNumber_MatchesSolidAngleSum(bool flipHalf)
        {
            DMesh3 mesh = Sphere();
            if (flipHalf)
                foreach (int tid in mesh.TriangleIndices().ToArray())
                    if (mesh.GetTriCentroid(tid).z > 0.3)
                        mesh.ReverseTriOrientation(tid);
            Assert.True(mesh.IsClosed());
            var spatial = new DMeshAABBTree3(mesh, true);
            var rnd = new Random(7);
            for (int i = 0; i < 300; ++i) {
                Vector3d p = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()) * 3 - 1.5 * Vector3d.One;
                double expected = BruteForce(mesh, p);
                Assert.True(Math.Abs(spatial.WindingNumber(p) - expected) < 1e-6, $"exact at {p}: {spatial.WindingNumber(p)} vs {expected}");
                Assert.True(Math.Abs(spatial.FastWindingNumber(p) - expected) < 0.1, $"fast at {p}: {spatial.FastWindingNumber(p)} vs {expected}");
            }
        }
    }
}
