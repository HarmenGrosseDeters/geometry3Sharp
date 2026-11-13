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


        [Fact]
        public void BetaBelowSqrt3_GateByThePaddedBox()
        {
            // below sqrt(3) the box test still matters and keeps the padded box of before: at 2^-50 (padding 1.1e-14 above
            // the cloud's boxes) every point is evaluated directly, as on the old code; from sqrt(3) on the tight box
            double s = BitConverter.Int64BitsToDouble((long)(1023 - 50) << 52);
            double[] area;
            DMesh3 m = PointFastWindingScaleTests.Cloud(out area);
            MeshTransforms.Scale(m, s);
            PointAABBTree3 low = PointFastWindingScaleTests.Tree(m, area, s), high = PointFastWindingScaleTests.Tree(m, area, s);
            low.FWNBeta = 1.7;
            high.FWNBeta = Math.Sqrt(3);
            low.FastWindingNumber(Vector3d.Zero);
            high.FastWindingNumber(Vector3d.Zero);
            Vector3d[] queries = PointFastWindingScaleTests.Queries(50, 13);
            double perLow = queries.Sum(q => (double)low.FastWindingDirectPointCount(s * q)) / queries.Length;
            double perHigh = queries.Sum(q => (double)high.FastWindingDirectPointCount(s * q)) / queries.Length;
            Assert.True(perLow > 0.5 * m.VertexCount, $"beta 1.7: {perLow} of {m.VertexCount} points direct per query");
            Assert.True(perHigh < 0.1 * m.VertexCount, $"beta sqrt(3): {perHigh} of {m.VertexCount} points direct per query");
        }
    }
}
