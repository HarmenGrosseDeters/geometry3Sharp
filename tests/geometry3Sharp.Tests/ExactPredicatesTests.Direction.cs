using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactPredicates.Orient3dDirection and Cross2Direction (#1672, the exact direction of ExactRayTriangle's rays)
    /// against the determinant in BigInteger, every stage; in ExactPredicatesTests, so that the predicates mutation
    /// shards run them.
    /// </summary>
    public partial class ExactPredicatesTests
    {
        [Fact]
        public void Orient3dDirection_EqualsTheDeterminant_AndItsStagesAgree()
        {
            // the filter, the 64-bit integer stage and BigInteger against the determinant in BigInteger
            int intStage = 0, n = 0;
            foreach (var q in ExactRayTriangleDirectionTests.Cases(7, 3000)) {
                Vector3d o = q[0], d = q[1], a = q[2], b = q[3];
                var I = ExactRayTriangleDirectionTests.Integers(o, d, a, b);
                int expected = ExactRayTriangleDirectionTests.Dot(ExactRayTriangleDirectionTests.Cross(I[1], ExactRayTriangleDirectionTests.Sub(I[2], I[0])), ExactRayTriangleDirectionTests.Sub(I[3], I[0])).Sign;
                Assert.Equal(expected, ExactPredicates.Orient3dDirection(o, d, a, b));
                Assert.Equal(expected, ExactPredicates.Orient3dDirectionExact(o, d, a, b));
                Assert.Equal(expected, ExactPredicates.orient3d_direction_bigint(o, d, a, b));
                if (ExactPredicates.orient3d_direction_int(o, d, a, b, out int si)) {
                    Assert.Equal(expected, si);
                    intStage++;
                }
                // Cross2Direction: (a1 - b1) d2 - (a2 - b2) d1
                var J = ExactRayTriangleDirectionTests.Integers(a, b, d);
                int e2 = ((J[0][1] - J[1][1]) * J[2][2] - (J[0][2] - J[1][2]) * J[2][1]).Sign;
                Assert.Equal(e2, ExactPredicates.Cross2Direction(a.y, a.z, b.y, b.z, d.y, d.z));
                n++;
            }
            Assert.True(intStage > 300 && n > 2500, $"{intStage} of {n}");
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Orient3dDirection_AndCross2Direction_ThrowOnANonFiniteCoordinate(double bad)
        {
            // as Orient3d: the exact stage refuses non-finite coordinates (the ray callers reject them before)
            var p = new Vector3d(1, 2, 3);
            Assert.Throws<System.ArgumentException>(() => ExactPredicates.Orient3dDirection(new Vector3d(bad, 0, 0), p, Vector3d.AxisX, Vector3d.AxisY));
            Assert.Throws<System.ArgumentException>(() => ExactPredicates.Orient3dDirection(Vector3d.Zero, new Vector3d(1, bad, 0), Vector3d.AxisX, Vector3d.AxisY));
            Assert.Throws<System.ArgumentException>(() => ExactPredicates.Orient3dDirection(Vector3d.Zero, p, new Vector3d(0, 0, bad), Vector3d.AxisY));
            Assert.Throws<System.ArgumentException>(() => ExactPredicates.Cross2Direction(bad, 0, 1, 2, 3, 4));
            Assert.Throws<System.ArgumentException>(() => ExactPredicates.Cross2Direction(0, 1, 2, 3, 4, bad));
        }
    }
}
