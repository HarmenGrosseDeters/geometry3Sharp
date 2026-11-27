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
        public void ShallowRay_OnATiltedPlane_CancellationInDdotN_ParameterIsTheHit()
        {
            // a ray within about 1e-16 of a tilted triangle's plane (review of #1702): D . N cancels, its rounding is noise
            // (1.1e-16 against terms of 0.79), and the double quotients gave t = 3 and barycentrics (0.25, 0) for the exact
            // hit at t = 0.0043038, barycentrics (0.26529, 0.45417)
            var tri = new Triangle3d(new Vector3d(0.1, 0.2, 0.3), new Vector3d(1.3, 0.7, 0.9), new Vector3d(0.4, 1.6, 1.1));
            var o = new Vector3d(0.5555242140606036, 0.9701310428816298, 0.8236162533557757);
            var d = new Vector3d(-0.2153209081718545, -0.38334886138368773, -0.2573551055391454);
            var q = new IntrRay3Triangle3(new Ray3d(o, d, true), tri);
            Assert.True(q.Find());
            Assert.True(Math.Abs(q.RayParameter - 0.0043038) < 1e-6, q.RayParameter.ToString("R"));
            Assert.True(Math.Abs(q.TriangleBaryCoords.y - 0.26529) < 1e-5 && Math.Abs(q.TriangleBaryCoords.z - 0.45417) < 1e-5, q.TriangleBaryCoords.ToString());
            // the hit point is on the triangle: within rounding of the point of its barycentrics
            Vector3d p = new Ray3d(o, d, true).PointAt(q.RayParameter);
            Vector3d w = q.TriangleBaryCoords;
            Assert.True((p - (w.x * tri.V0 + w.y * tri.V1 + w.z * tri.V2)).MaxAbs < 1e-12);
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
