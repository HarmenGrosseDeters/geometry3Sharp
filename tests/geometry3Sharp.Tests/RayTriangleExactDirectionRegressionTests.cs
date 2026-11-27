using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Regression tests of #1672 on the public API of master (IntersectionUtil.Intersects, IntrRay3Triangle3): the ray
    /// hit was decided for the line through the origin and a rounded second point origin + 2^m direction, so a
    /// subnormal ray missed a triangle it crosses in the interior (and its 2^k-scaled copies hit), and a ray whose
    /// second point overflows had no hits at all. The decision is now that of the supplied ray (exact direction).
    /// </summary>
    public class RayTriangleExactDirectionRegressionTests
    {
        static Vector3d Scaled(Vector3d v, int k)
        {
            return new Vector3d(ExactPoint3.ScaleB(v.x, k), ExactPoint3.ScaleB(v.y, k), ExactPoint3.ScaleB(v.z, k));
        }

        [Fact]
        public void SubnormalRay_HitsTheTriangleItCrossesInTheInterior_AtEveryPowerOfTwoScale()
        {
            // the issue's repro: o + t d crosses y = 8e at t = 2, in (3e, 8e, 0), barycentrics (1/6, 1/3, 1/2) (strict
            // interior); the rounded second point (e + e/2 -> e, 2e) made the line x = e, which misses
            double e = double.Epsilon;
            var o = new Vector3d(e, 0, 0);
            var d = new Vector3d(e, 4 * e, 0);
            var v0 = new Vector3d(2 * e, 8 * e, -2 * e);
            var v1 = new Vector3d(5 * e, 8 * e, -2 * e);
            var v2 = new Vector3d(2 * e, 8 * e, 2 * e);
            foreach (int k in new[] { 0, 1, 52, 200, 1000, 2000, 2094 }) {
                Vector3d so = Scaled(o, k), sd = Scaled(d, k), a = Scaled(v0, k), b = Scaled(v1, k), c = Scaled(v2, k);
                Assert.True(IntersectionUtil.Intersects(so, sd, a, b, c), $"scale 2^{k}: missed");
                // the reverse ray starts in front of the triangle and points away from it
                Assert.False(IntersectionUtil.Intersects(so, -sd, a, b, c), $"scale 2^{k}: reverse ray hit");
                // the direction's length does not matter (it is the same ray), also 2^j times it (exact)
                foreach (int j in new[] { 3, 60 })
                    if (k <= 1000) Assert.True(IntersectionUtil.Intersects(so, Scaled(sd, j), a, b, c), $"scale 2^{k}, direction 2^{j}: missed");
                var intr = new IntrRay3Triangle3(new Ray3d(so, sd, true), new Triangle3d(a, b, c));
                Assert.True(intr.Find(), $"scale 2^{k}: IntrRay3Triangle3 missed");
                Assert.Equal(IntersectionResult.Intersects, intr.Result);
                // the hit is o + 2 d (the direction is kept: Ray3d(.., true)), and its barycentrics (1/6, 1/3, 1/2)
                Assert.True(Math.Abs(intr.RayParameter - 2) < 1e-12, $"scale 2^{k}: t {intr.RayParameter:R}");
                Assert.True((intr.TriangleBaryCoords - new Vector3d(1.0 / 6, 1.0 / 3, 0.5)).MaxAbs < 1e-12, $"scale 2^{k}: {intr.TriangleBaryCoords}");
                // reuse: computing again on the same instance gives the same result
                double t = intr.RayParameter;
                Assert.True(intr.Compute().Find());
                Assert.Equal(t, intr.RayParameter);
            }
        }

        [Fact]
        public void RayFromNearTheTopOfTheDoubleRange_HitsTheTriangleAhead()
        {
            // the second point origin + 2^1024 direction overflows; SecondPoint returned the origin (no hits). The
            // triangle in the plane x = double.MaxValue is ahead of the origin along +x, around the ray's line y = z = 0.
            double big = double.MaxValue / 2;
            var o = new Vector3d(big, 0, 0);
            var d = new Vector3d(1, 0, 0);
            double m = double.MaxValue;
            var a = new Vector3d(m, -1e300, -1e300);
            var b = new Vector3d(m, 1e300, -1e300);
            var c = new Vector3d(m, 0, 1e300);
            Assert.True(IntersectionUtil.Intersects(o, d, a, b, c));
            Assert.False(IntersectionUtil.Intersects(o, -d, a, b, c));
            var intr = new IntrRay3Triangle3(new Ray3d(o, d), new Triangle3d(a, b, c));
            Assert.True(intr.Find());
            Assert.True(intr.RayParameter > 0);
        }
    }
}
