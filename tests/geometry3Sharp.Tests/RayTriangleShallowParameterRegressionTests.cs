using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Regression tests of #1686 on the public API of master: for a shallow ray whose D . N is subnormal, IntrRay3Triangle3
    /// hit (rightly) but returned t from the centroid's projection, because 1 / (D . N) overflowed although the quotient is
    /// fine: t = 0.333 instead of 1, and ray.PointAt(t) off the triangle.
    /// </summary>
    public class RayTriangleShallowParameterRegressionTests
    {
        static readonly Triangle3d Tri = new Triangle3d(new Vector3d(0, -1, 0), new Vector3d(4, -1, 0), new Vector3d(0, 4, 0));

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(7)]
        [InlineData(1000)]
        public void ShallowRay_SubnormalDdotN_ParameterAndBarycentricsAreTheHit(int k)
        {
            // the issue's ray (k = 1), and steeper ones still with a subnormal D . N: o + t d meets z = 0 at t = 1, in (2, 0, 0)
            double h = double.Epsilon * k;
            var ray = new Ray3d(new Vector3d(1, 0, h), new Vector3d(1, 0, -h), true);
            var q = new IntrRay3Triangle3(ray, Tri);
            Assert.True(q.Find());
            Assert.Equal(1.0, q.RayParameter);
            Assert.True((q.TriangleBaryCoords - new Vector3d(0.3, 0.5, 0.2)).MaxAbs < 1e-15, q.TriangleBaryCoords.ToString());
            Assert.True((ray.PointAt(q.RayParameter) - new Vector3d(2, 0, 0)).MaxAbs < 1e-15);
            // the static form, and the same instance computed again
            Vector3d a = Tri.V0, b = Tri.V1, c = Tri.V2;
            Assert.True(IntrRay3Triangle3.Intersects(ref ray, ref a, ref b, ref c, out double t));
            Assert.Equal(1.0, t);
            // reuse: a new ray resets the query, computing again gives the same
            q.Ray = ray;
            Assert.True(q.Compute().Find());
            Assert.Equal(1.0, q.RayParameter);
        }

        [Fact]
        public void ShallowRay_ThroughTheTreeAndMeshQueries_HitsAtTheRightParameter()
        {
            double h = double.Epsilon;
            var mesh = new DMesh3();
            mesh.AppendVertex(Tri.V0); mesh.AppendVertex(Tri.V1); mesh.AppendVertex(Tri.V2);
            mesh.AppendTriangle(0, 1, 2);
            var tree = new DMeshAABBTree3(mesh, true);
            var ray = new Ray3d(new Vector3d(1, 0, h), new Vector3d(1, 0, -h), true);
            Assert.Equal(0, tree.FindNearestHitTriangle(ray));
            var hit = MeshQueries.TriangleIntersection(mesh, 0, ray);
            Assert.Equal(1.0, hit.RayParameter);
        }
    }
}
