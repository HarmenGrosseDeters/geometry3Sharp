using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshBoolean (the mesh-cut union of two closed meshes): the result must come back welded and closed.
    /// (MeshBooleanTests covers MeshWindingNumberBoolean.)
    /// </summary>
    public class MeshBooleanSeamTests
    {
        static DMesh3 Sphere(Vector3d center, int edgeVertices)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(mesh, center);
            return mesh;
        }

        static void AssertClosedUnion(int edgeVertices, Vector3d toolCenter)
        {
            var op = new MeshBoolean() { Target = Sphere(Vector3d.Zero, edgeVertices), Tool = Sphere(toolCenter, edgeVertices) };
            bool ok = op.Compute();
            int boundary = MeshIterators.BoundaryEdges(op.Result).Count();
            Assert.True(ok && boundary == 0, $"Compute() = {ok}, {boundary} boundary edges");
            Assert.True(op.Result.CheckValidity(false, FailMode.ReturnOnly));
            var comps = new MeshConnectedComponents(op.Result);
            comps.FindConnectedT();
            Assert.Equal(1, comps.Count);
        }

        // Inputs that left a small seam hole after welding: three with a tiny boundary edge between two seam
        // vertices less than the match tolerance apart, one with a single missing seam triangle.
        [Theory]
        [InlineData(8, 0.7060438090963492, 0.1422092237706339, 0.1314813208912878)]
        [InlineData(8, 0.48356576663608, 0.48208275226973124, 0.04767185824349143)]
        [InlineData(20, 1.4666749242538004, 0.12680896330941885, 0.2533013062799821)]
        [InlineData(32, 0.8737664778594703, 0.05047010423171804, 0.49147923383465003)]
        public void Union_SeamArtifacts_AreClosed(int edgeVertices, double x, double y, double z)
        {
            AssertClosedUnion(edgeVertices, new Vector3d(x, y, z));
        }

        [Fact]
        public void Union_RandomSpherePairs_AreClosed()
        {
            var rand = new Random(17);
            var failures = new System.Collections.Generic.List<string>();
            foreach (int ev in new[] { 8, 12, 20 }) {
                for (int t = 0; t < 10; ++t) {
                    var c = new Vector3d(0.3 + rand.NextDouble() * 1.2, rand.NextDouble() * 0.5, rand.NextDouble() * 0.5);
                    try {
                        AssertClosedUnion(ev, c);
                    } catch (Exception e) {
                        failures.Add($"ev {ev} c ({c.x:R},{c.y:R},{c.z:R}): {e.Message.Split('\n')[0]}");
                    }
                }
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
    }
}
