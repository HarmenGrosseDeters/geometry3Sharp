using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The unit scale and the disparity flag of IntrinsicTriangulation (#1377): members that a library without the prescaling does not
    /// have, so this file does not compile against it (the sweeps over the public API are in MagnitudeRobustnessTests).
    /// </summary>
    public class MagnitudeRobustnessInternalsTests
    {
        static DMesh3 ShearedGrid(int n = 3)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d(i + 0.375 * j, j, 0));
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 2, d = a + n + 1;
                m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            }
            return m;
        }

        static DMesh3 Scaled(DMesh3 mesh, int e) => MagnitudeRobustnessTests.Scaled(mesh, e);
        static DMesh3 HalfSliver() => MagnitudeRobustnessTests.HalfSliver();

        [Fact]
        public void IntrinsicTriangulation_UnitScaleExponent_FollowsTheLongestEdge()
        {
            DMesh3 mesh = ShearedGrid();
            Assert.Equal(IntrinsicTriangulation.EdgeExponent(mesh) + 37, IntrinsicTriangulation.EdgeExponent(Scaled(mesh, 37)));
            Assert.Equal(new IntrinsicTriangulation(mesh).ScaleExponent + 37, new IntrinsicTriangulation(Scaled(mesh, 37)).ScaleExponent);
        }

        [Fact]
        public void IntrinsicTriangulation_LengthDisparity_IsDetected()
        {
            // edges 2^-260 of the longest one: the fourth powers of their lengths leave the double range
            Assert.False(new IntrinsicTriangulation(ShearedGrid()).HasLengthDisparity);
            Assert.True(new IntrinsicTriangulation(HalfSliver()).HasLengthDisparity);
        }

        [Fact]
        public void LengthDisparity_BelowTheSquareRangeOfTheLongestEdge_IsStillDetected()
        {
            // an edge 2^-600 of the longest: its square underflows to 0 (the length would read as 0), the disparity must still be seen
            var m = new DMesh3();
            double s = Math.Pow(2, -600);
            m.AppendVertex(Vector3d.Zero); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(1, s, 0)); m.AppendVertex(new Vector3d(1, -s, 0));
            m.AppendTriangle(0, 1, 2); m.AppendTriangle(0, 3, 1);
            Assert.True(new IntrinsicTriangulation(m).HasLengthDisparity);
            Assert.True(IntrinsicTriangulation.HasEdgeDisparity(m));
            Assert.Equal(PolyhedralGeodesicStatus.DegenerateTriangles, new PolyhedralGeodesicDistance(m).Compute(new[] { 0 }));
            Assert.False(new MeshGeodesicDistance(m).Compute(new[] { 0 }));
        }
    }
}
