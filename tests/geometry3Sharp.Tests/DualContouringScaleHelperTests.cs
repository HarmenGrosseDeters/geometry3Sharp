using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The scale-free helpers of DualContouring (#1376): unit_normal, pow2_scaled and triangle_normal keep the bits of
    /// Vector3d.Normalized and MathUtil.Normal where those apply, and give the same direction at any magnitude.
    /// (Internal API of the fix, so this file does not compile against the library before it.)
    /// </summary>
    public class DualContouringScaleHelperTests
    {
        static double Pow2(int e) => ExactPoint3.ScaleB(1.0, e);

        [Fact]
        public void UnitNormal_OrdinaryGradient_HasTheBitsOfNormalized()
        {
            var r = new Random(11);
            for (int i = 0; i < 2000; ++i) {
                var g = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5) * Math.Pow(10, r.Next(-14, 100));
                Vector3d a = g.Normalized, b = DualContouring.unit_normal(g);
                Assert.True(a.x == b.x && a.y == b.y && a.z == b.z, g.ToString());
            }
        }

        [Theory]
        [InlineData(-60)]
        [InlineData(-300)]
        [InlineData(-900)]
        [InlineData(-1040)]     // subnormal components
        [InlineData(400)]
        [InlineData(520)]       // the squares overflow
        [InlineData(1000)]
        public void UnitNormal_AnyMagnitude_IsTheUnitVectorOfTheDirection(int e)
        {
            var g = new Vector3d(0.75, -0.5, 0.3125);
            Vector3d unit = g.Normalized;
            Vector3d n = DualContouring.unit_normal(new Vector3d(ExactPoint3.ScaleB(g.x, e), ExactPoint3.ScaleB(g.y, e), ExactPoint3.ScaleB(g.z, e)));
            Assert.True(unit.x == n.x && unit.y == n.y && unit.z == n.z, n.ToString());
            Assert.Equal(1.0, n.Length, 15);
        }

        [Fact]
        public void TriangleNormal_OrdinaryTriangle_HasTheBitsOfMathUtilNormal_AndAnySizeTheSameNormal()
        {
            var r = new Random(5);
            for (int i = 0; i < 500; ++i) {
                Vector3d a = new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble()), b = new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble()), c = new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble());
                if (i % 10 == 0) c = a + 0.5 * (b - a);         // collinear: the zero normal of MathUtil.Normal stays
                Vector3d n = MathUtil.Normal(a, b, c), m = DualContouring.triangle_normal(a, b, c);
                Assert.True(n.x == m.x && n.y == m.y && n.z == m.z);
                foreach (int e in new[] { -100, -600, 600 }) {
                    double s = Pow2(e);
                    Vector3d k = DualContouring.triangle_normal(a * s, b * s, c * s);
                    Assert.True(n.x == k.x && n.y == k.y && n.z == k.z, e.ToString());
                }
            }
        }

        [Fact]
        public void UnitNormal_ZeroNaNOrInfinite_IsZero()
        {
            foreach (Vector3d g in new[] { Vector3d.Zero, new Vector3d(double.NaN, 1, 0), new Vector3d(double.PositiveInfinity, 1, 0), new Vector3d(0, double.NegativeInfinity, 0), new Vector3d(1e-300, double.NaN, 0) })
                Assert.Equal(Vector3d.Zero, DualContouring.unit_normal(g));
        }

        [Fact]
        public void Pow2Scaled_LargestComponentInOneToTwo_SameDirectionBits()
        {
            foreach (int e in new[] { -1074, -1060, -1022, -700, -1, 0, 1, 511, 512, 1023 }) {
                var v = new Vector3d(ExactPoint3.ScaleB(-0.625, e - 3), ExactPoint3.ScaleB(1.5, e), ExactPoint3.ScaleB(1.0, e - 1));
                if (e < -1071) v = new Vector3d(0, ExactPoint3.ScaleB(1.0, e), 0);
                Vector3d w = DualContouring.pow2_scaled(v);
                double m = Math.Max(Math.Abs(w.x), Math.Max(Math.Abs(w.y), Math.Abs(w.z)));
                Assert.InRange(m, 1.0, 1.9999999999999998);
                if (e >= -1071) { Assert.Equal(1.5, w.y); Assert.Equal(0.5, w.z); Assert.Equal(-0.625 / 8, w.x); }
            }
            Assert.Equal(Vector3d.Zero, DualContouring.pow2_scaled(Vector3d.Zero));
            // the sign of a dot product of two huge normals survives
            var a = new Vector3d(1e200, -2e200, 3e200); var b = new Vector3d(3e200, 1e200, 1e200);
            Assert.True(double.IsNaN(a.Dot(b) - a.Dot(b)));       // the plain product is infinite
            Assert.True(DualContouring.pow2_scaled(a).Dot(DualContouring.pow2_scaled(b)) > 0);
            Assert.True(DualContouring.pow2_scaled(a).Dot(DualContouring.pow2_scaled(-b)) < 0);
        }
    }
}
