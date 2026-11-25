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
                if (i % 10 == 0) c = a + 0.5 * (b - a);         // nearly collinear: the same rounding noise as MathUtil.Normal
                if (i % 10 == 5) c = b;                         // a repeated point: the zero normal stays
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

        [Fact]
        public void ValuesDiffer_RelativeIsAnyDifference_AbsoluteIsTheOldThreshold_NaNNever()
        {
            Assert.True(DualContouring.values_differ(1e-300, 2e-300, true));
            Assert.True(DualContouring.values_differ(0, double.Epsilon, true));
            Assert.False(DualContouring.values_differ(1e-300, 2e-300, false));
            Assert.False(DualContouring.values_differ(0, MathUtil.Epsilon, false));
            Assert.True(DualContouring.values_differ(0, 2 * MathUtil.Epsilon, false));
            Assert.True(DualContouring.values_differ(-1, 1, true));
            Assert.True(DualContouring.values_differ(1, -1, false));
            foreach (bool relative in new[] { true, false }) {
                Assert.False(DualContouring.values_differ(0.5, 0.5, relative));
                Assert.False(DualContouring.values_differ(double.NaN, 1, relative));
                Assert.False(DualContouring.values_differ(1, double.NaN, relative));
                Assert.False(DualContouring.values_differ(double.PositiveInfinity, double.PositiveInfinity, relative));
            }
        }

        // (#1376, item 2) triangles for degenerate(): an equilateral one, a right one, a needle (area 1e-13 of the squared edge),
        // a sliver just above and just below the 2e-12 threshold, and a repeated point
        static readonly (Vector3d a, Vector3d b, Vector3d c, bool degenerate)[] Triangles = {
            (new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, 0.8660254037844386, 0), false),
            (new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), false),
            (new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, 1e-13, 0), true),
            (new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, 1e-11, 0), false),
            (new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, 3e-12, 0), false),
            (new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, 1e-12, 0), true),
            (new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 0, 0), true),
        };

        [Theory]
        [InlineData(0)]
        [InlineData(-100)]
        [InlineData(-170)]      // 2^-170: the squares of the cross product are below 2^-512
        [InlineData(-300)]
        [InlineData(-500)]
        [InlineData(100)]
        [InlineData(200)]
        [InlineData(300)]       // the fourth power is infinite
        [InlineData(500)]
        public void Degenerate_AnySizeOfTriangle_GivesTheDecisionOfTheUnitTriangle(int e)
        {
            double s = Pow2(e);
            foreach (var t in Triangles)
                Assert.Equal(t.degenerate, DualContouring.degenerate(t.a * s, t.b * s, t.c * s));
            // the same triangles tilted and moved off the origin (the edges are differences of large coordinates)
            Vector3d o = new Vector3d(3, -5, 7) * s;
            foreach (var t in Triangles)
                Assert.Equal(t.degenerate, DualContouring.degenerate(o + new Vector3d(t.a.z, t.a.x, t.a.y) * s, o + new Vector3d(t.b.z, t.b.x, t.b.y) * s, o + new Vector3d(t.c.z, t.c.x, t.c.y) * s));
        }

        [Fact]
        public void Degenerate_NaNAndInfiniteTriangles_AreDegenerate()
        {
            Vector3d nan = new Vector3d(double.NaN, 0, 0), inf = new Vector3d(double.PositiveInfinity, 0, 0);
            Assert.True(DualContouring.degenerate(Vector3d.Zero, nan, Vector3d.AxisY));
            Assert.True(DualContouring.degenerate(Vector3d.Zero, inf, Vector3d.AxisY));
        }

        [Fact]
        public void CrossExceeds_InTheOrdinaryRange_IsTheUnscaledComparison()
        {
            var r = new Random(3);
            for (int i = 0; i < 3000; ++i) {
                Vector3d u = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5), v = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5);
                double scale = Math.Pow(10, r.Next(-40, 40)), f = Math.Pow(10, -r.Next(0, 3));
                u *= scale; v *= scale;
                Vector3d ref1 = u.LengthSquared >= v.LengthSquared ? u : v;
                Assert.Equal(u.Cross(v).Length > f * ref1.LengthSquared, DualContouring.cross_exceeds(u, v, ref1, f));
            }
        }
    }
}
