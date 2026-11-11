using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> NTMesh3.CheckValidity required the squared length of a vertex to be finite: a valid mesh with a coordinate above about 1e154 was reported invalid (as DMesh3, FINDINGS item 9) </summary>
    public class NTMesh3ValidityRangeTests
    {
        static NTMesh3 Tetra(double scale)
        {
            var m = new NTMesh3(false, false, false);
            m.AppendVertex(new Vector3d(0, 0, 0));
            m.AppendVertex(new Vector3d(scale, 0, 0));
            m.AppendVertex(new Vector3d(0, scale, 0));
            m.AppendVertex(new Vector3d(0, 0, scale));
            m.AppendTriangle(0, 2, 1); m.AppendTriangle(0, 1, 3); m.AppendTriangle(0, 3, 2); m.AppendTriangle(1, 2, 3);
            return m;
        }

        [Theory]
        [InlineData(1e-300)]
        [InlineData(1.0)]
        [InlineData(1e150)]
        [InlineData(1e160)]
        [InlineData(1e300)]
        [InlineData(1.5e308)]
        public void AValidMesh_IsValid_AtAnyFiniteSize(double scale)
        {
            Assert.True(Tetra(scale).CheckValidity(FailMode.ReturnOnly));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void ANonFiniteCoordinate_IsStillInvalid(double bad)
        {
            for (int axis = 0; axis < 3; axis++)
                foreach (double scale in new[] { 1.0, 1e200 }) {
                    NTMesh3 m = Tetra(scale);
                    Vector3d p = m.GetVertex(1);
                    p[axis] = bad;
                    m.SetVertex(1, p);
                    Assert.False(m.CheckValidity(FailMode.ReturnOnly), "axis " + axis + ", scale " + scale);
                }
        }
    }
}
