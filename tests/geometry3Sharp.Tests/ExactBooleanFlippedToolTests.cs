using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1210, in terms of the API before the fix (so that it also runs against the earlier library): a closed tool with
    /// inconsistently oriented faces must not give Success with a wrong result. The earlier rule kept the orientation of the
    /// larger part by area, so a cube tool with three of its six faces flipped became inside-out, an empty tool, and
    /// [-1,2]^3 minus it returned the whole block (27 instead of 26) with Success.
    /// </summary>
    public class ExactBooleanFlippedToolTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void ACubeToolWithFlippedFaces_IsNotSilentlyEmpty(int k)
        {
            DMesh3 tool = Box(0, 0, 0, 1, 1, 1);
            var faces = tool.TriangleIndices().GroupBy(t => tool.GetTriNormal(t)).OrderBy(g => g.Key.x).ThenBy(g => g.Key.y).ThenBy(g => g.Key.z).ToList();
            foreach (int t in faces.Take(k).SelectMany(g => g).ToList())
                tool.ReverseTriOrientation(t);
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), tool, ExactMeshBoolean.Operations.Difference);
            // refused (no result), never Success with the tool taken as empty
            Assert.False(b.Compute(), "Success with " + (b.Result == null ? "no result" : b.Result.TriangleCount + " triangles"));
            Assert.Null(b.Result);
            Assert.Contains("inconsistently oriented", b.Error);
        }
    }
}
