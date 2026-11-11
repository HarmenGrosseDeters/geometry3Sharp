using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// DMesh3.CheckValidity required the squared length of every vertex to be finite: a valid mesh with a coordinate above about 1e154
    /// (all three above 7.7e153) was reported invalid (FINDINGS item 9). It checks the components now.
    /// </summary>
    public class CheckValidityRangeTests
    {
        static DMesh3 Sphere(double scale)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) m.SetVertex(v, scale * m.GetVertex(v));
            return m;
        }

        [Theory]
        [InlineData(1e-300)]
        [InlineData(1e-100)]
        [InlineData(1.0)]
        [InlineData(1e100)]
        [InlineData(1e150)]
        [InlineData(1e154)]
        [InlineData(1e160)]
        [InlineData(1e200)]
        [InlineData(1e300)]
        [InlineData(1.5e308)]
        public void AValidMesh_IsValid_AtAnyFiniteSize(double scale)
        {
            Assert.True(Sphere(scale).CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void ANonFiniteCoordinate_AtAHugeScale_AndMixedNaNAndInfinity_IsInvalid()
        {
            DMesh3 m = Sphere(1e200);
            m.SetVertex(3, new Vector3d(double.NaN, double.PositiveInfinity, 1e200));
            Assert.False(m.CheckValidity(false, FailMode.ReturnOnly));
            DMesh3 n = Sphere(1e200);
            n.SetVertex(3, new Vector3d(1e200, 1e200, double.NegativeInfinity));
            Assert.False(n.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void ANonFiniteCoordinate_IsStillInvalid(double bad)
        {
            for (int axis = 0; axis < 3; axis++)
                foreach (double scale in new[] { 1.0, 1e100 }) {
                    DMesh3 m = Sphere(scale);
                    Vector3d p = m.GetVertex(3);
                    p[axis] = bad;
                    m.SetVertex(3, p);
                    Assert.False(m.CheckValidity(false, FailMode.ReturnOnly), "axis " + axis + ", scale " + scale);
                }
        }
    }
}
