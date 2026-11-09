using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 T1 finding R27: a NaN or infinite vertex made Apply throw an ArgumentException from ExactPredicates (an
    /// AggregateException of it from the parallel pair search), where the class documents ScaleNotFinite
    /// </summary>
    public class MeshRoundingRepairNonFiniteTests
    {
        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void NonFiniteVertex_IsReportedAsScaleNotFinite(double bad)
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6);
            int v = m.VertexIndices().Skip(m.VertexCount / 3).First();
            m.SetVertex(v, new Vector3d(bad, 0.5, 0.5));
            int triangles = m.TriangleCount;
            var r = new MeshRoundingRepair(m);
            Assert.False(r.Apply());
            Assert.True(r.ScaleNotFinite);
            Assert.False(r.IsClean);
            Assert.Equal(triangles, m.TriangleCount);
        }

        [Fact]
        public void NonFiniteVertexOfNoTriangle_DoesNotMatter()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6);
            int v = m.AppendVertex(new Vector3d(double.NaN, 0, 0));
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            Assert.False(r.ScaleNotFinite);
        }
    }
}
