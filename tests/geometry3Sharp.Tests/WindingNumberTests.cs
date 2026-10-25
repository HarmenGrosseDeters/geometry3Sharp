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

namespace geometry3Sharp.Tests
{
    /// <summary> spatial queries on meshes in large (eg UTM) coordinates </summary>
    public class LargeCoordinateTests
    {
        [Fact]
        public void RayCasts_NoMisses_AtUtmCoordinates()
        {
            Vector3d offset = new Vector3d(632000, 5712000, 100);
            // fine grid, 0.25 m triangles
            var gen = new TrivialRectGenerator() { Width = 50, Height = 50, IndicesMap = new Index2i(1, 2) };
            DMesh3 mesh = gen.Generate().MakeDMesh();
            for (int k = 0; k < 7; ++k)
                foreach (int eid in mesh.EdgeIndices().ToArray()) {
                    DMesh3.EdgeSplitInfo info;
                    mesh.SplitEdge(eid, out info);
                }
            MeshTransforms.Translate(mesh, offset);
            var spatial = new DMeshAABBTree3(mesh, true);
            var rnd = new Random(1);
            int misses = 0;
            for (int i = 0; i < 2000; ++i) {
                Vector3d p = offset + new Vector3d(rnd.NextDouble() * 48 - 24, rnd.NextDouble() * 48 - 24, 10);
                if (spatial.FindNearestHitTriangle(new Ray3d(p, -Vector3d.AxisZ)) == DMesh3.InvalidID)
                    misses++;
                Assert.True(spatial.FindNearestTriangle(p - 10 * Vector3d.AxisZ) >= 0);
            }
            Assert.Equal(0, misses);
            // every triangle is inside the root box
            AxisAlignedBox3d root = spatial.Bounds;
            foreach (int tid in mesh.TriangleIndices())
                Assert.True(root.Contains(mesh.GetTriBounds(tid)));
        }
    }
}
