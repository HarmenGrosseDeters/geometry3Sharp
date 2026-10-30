using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class WrapCurvatureHypothesisTests
    {
        [Theory]
        [InlineData(0.0, 1.0, 0.25, 0.125, 0.125, false)]
        [InlineData(0.25, 0.25, 0.03125, 0.03125, 0.0625, false)]
        [InlineData(0.0, 0.0, 0.0, 0.0, 0.0, true)]
        [InlineData(0.5, 1e-14, 0.0, 0.0, 0.0, true)]
        public void MixedArea_LocalRightObtuseAndDegenerateTriangles_MatchAnalyticAllocation(
            double x, double y, double area0, double area1, double area2, bool degenerate)
        {
            foreach (int exponent in new[] { -20, 0, 20 }) {
                double scale = Math.Pow(2, exponent);
                var mesh = new DMesh3();
                mesh.AppendVertex(Vector3d.Zero);
                mesh.AppendVertex(new Vector3d(scale, 0, 0));
                mesh.AppendVertex(new Vector3d(x * scale, y * scale, 0));
                Assert.True(mesh.AppendTriangle(0, 1, 2) >= 0);
                Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
                var curvature = MeshCurvature.Compute(mesh);
                double areaScale = scale * scale;
                var expected = new[] { area0, area1, area2 };
                for (int i = 0; i < 3; ++i) {
                    Assert.Equal(expected[i] * areaScale, curvature.MixedArea[i]);
                    Assert.True(MathUtil.IsFinite(curvature.Mean[i]));
                    Assert.True(MathUtil.IsFinite(curvature.Gaussian[i]));
                    Assert.True(curvature.MeanCurvatureNormal[i].IsFinite);
                    Assert.False(curvature.Valid[i]);
                }
                Assert.Equal(degenerate ? 1 : 0, curvature.DegenerateTriangles);
                Assert.Equal(0, curvature.ValidVertices);
                Assert.Equal(2 * Math.PI, curvature.AngleDeficit.Sum(), 12);
            }
        }

        [Fact]
        public void AcuteEquilateralTriangle_MixedAreasPartitionItsAnalyticArea()
        {
            var mesh = new DMesh3();
            mesh.AppendVertex(Vector3d.Zero);
            mesh.AppendVertex(new Vector3d(1, 0, 0));
            mesh.AppendVertex(new Vector3d(0.5, Math.Sqrt(3) / 2, 0));
            Assert.True(mesh.AppendTriangle(0, 1, 2) >= 0);
            var c = MeshCurvature.Compute(mesh);
            double expectedArea = Math.Sqrt(3) / 4;
            Assert.All(c.MixedArea, area => Assert.InRange(Math.Abs(area - expectedArea / 3), 0, 1e-15));
            Assert.InRange(Math.Abs(c.MixedArea.Sum() - expectedArea), 0, 1e-15);
            Assert.Equal(0, c.DegenerateTriangles);
            Assert.Equal(0, c.ValidVertices);
        }
    }
}
