using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The far-field expansion of PointAABBTree3.FastWindingNumber is used at every scale (#745), counted by the test hook
    /// FastWindingDirectPointCount (it shares the gate with the evaluation). Before, at 2^-50 every box near the cloud
    /// contained the queries (a fixed padding of 1.1e-14) and the points were evaluated one by one.
    /// </summary>
    public class PointFastWindingExpansionUsedTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-50)]
        [InlineData(20)]
        public void ExpansionUsed_FewPointsDirect(int exponent)
        {
            double s = BitConverter.Int64BitsToDouble((long)(1023 + exponent) << 52);
            double[] area;
            DMesh3 m = PointFastWindingScaleTests.Cloud(out area);
            MeshTransforms.Scale(m, s);
            PointAABBTree3 tree = PointFastWindingScaleTests.Tree(m, area, s);
            tree.FastWindingNumber(Vector3d.Zero);
            Vector3d[] queries = PointFastWindingScaleTests.Queries(200, 9);
            double perQuery = queries.Sum(q => (double)tree.FastWindingDirectPointCount(s * q)) / queries.Length;
            Assert.True(perQuery < 0.1 * m.VertexCount, $"2^{exponent}: {perQuery} of {m.VertexCount} points evaluated directly per query");
        }

    }
}
