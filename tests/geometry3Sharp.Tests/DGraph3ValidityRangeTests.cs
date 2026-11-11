using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> DGraph3.CheckValidity required the squared length of a vertex to be finite: a valid graph with a coordinate above about 1e154 was reported invalid (FINDINGS item 9) </summary>
    public class DGraph3ValidityRangeTests
    {
        static DGraph3 Triangle(double scale)
        {
            var g = new DGraph3();
            int a = g.AppendVertex(new Vector3d(0, 0, 0)), b = g.AppendVertex(new Vector3d(scale, 0, 0)), c = g.AppendVertex(new Vector3d(0, scale, scale));
            g.AppendEdge(a, b); g.AppendEdge(b, c); g.AppendEdge(c, a);
            return g;
        }

        [Theory]
        [InlineData(1e-300)]
        [InlineData(1.0)]
        [InlineData(1e150)]
        [InlineData(1e160)]
        [InlineData(1e300)]
        [InlineData(1.5e308)]
        public void AValidGraph_IsValid_AtAnyFiniteSize(double scale)
        {
            Assert.True(Triangle(scale).CheckValidity(DGraph.FailMode.ReturnOnly));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void ANonFiniteCoordinate_IsStillInvalid(double bad)
        {
            for (int axis = 0; axis < 3; axis++)
                foreach (double scale in new[] { 1.0, 1e200 }) {
                    DGraph3 g = Triangle(scale);
                    Vector3d p = g.GetVertex(1);
                    p[axis] = bad;
                    g.SetVertex(1, p);
                    Assert.False(g.CheckValidity(DGraph.FailMode.ReturnOnly), "axis " + axis + ", scale " + scale);
                }
        }
    }
}
