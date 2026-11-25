using System;
using g3;
using g3.CoreMathPort;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1577: the browser (WebAssembly) gave other meshes than native for MeshRobustRepair's Solidify path, because the
    /// per-triangle Math.Atan2 of MathUtil.TriSolidAngle (the term of DMeshAABBTree3.FastWindingNumber) is the platform's
    /// libm, which can differ in the last bit between runtimes, and the contour of the winding number reaches the result
    /// (moving the term by one ulp changes the mesh of 5 of 37 demo repair cases). TriSolidAngle now takes the correctly
    /// rounded CoreMath.Atan2 (integer and basic floating-point operations only), the same double on every runtime.
    /// </summary>
    public class SolidAnglePlatformIndependenceTests
    {
        readonly ITestOutputHelper output;
        public SolidAnglePlatformIndependenceTests(ITestOutputHelper output) { this.output = output; }

        // the arguments of the atan2 of TriSolidAngle for a, b, c relative to p (no rescaling: the inputs are moderate)
        static void Terms(Vector3d a, Vector3d b, Vector3d c, Vector3d p, out double top, out double bottom)
        {
            a -= p; b -= p; c -= p;
            double la = a.Length, lb = b.Length, lc = c.Length;
            bottom = (la * lb * lc) + a.Dot(ref b) * lc + b.Dot(ref c) * la + c.Dot(ref a) * lb;
            top = a.x * (b.y * c.z - c.y * b.z) - a.y * (b.x * c.z - c.x * b.z) + a.z * (b.x * c.y - c.x * b.y);
        }

        [Fact]
        public void TriSolidAngle_IsTwiceTheCorrectlyRoundedAtan2OfItsTerms()
        {
            var rnd = new Random(1577);
            Func<Vector3d> r = () => new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
            int n = 400000, libmDiffers = 0;
            for (int i = 0; i < n; ++i) {
                Vector3d a = r(), b = r(), c = r(), p = 1.5 * r();
                Terms(a, b, c, p, out double top, out double bottom);
                double got = MathUtil.TriSolidAngle(a, b, c, ref p);
                Assert.Equal(2.0 * CoreMath.Atan2(top, bottom), got);
                if (Math.Atan2(top, bottom) != CoreMath.Atan2(top, bottom)) ++libmDiffers;
            }
            // (information: how many of the inputs this machine's libm would have rounded differently)
            output.WriteLine("Math.Atan2 differs from the correctly rounded value in " + libmDiffers + " of " + n);
        }

        static DMesh3 Sphere(double scale)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
            MeshTransforms.Scale(m, scale);
            return m;
        }

        [Fact]
        public void FastWindingNumber_IsTheSameBitsAtEveryPowerOfTwoScale()
        {
            // (a scale by 2^k is exact, so the winding number of the scaled mesh at the scaled point is the same double; the
            // exact terms then differ only if an operation is not a function of the ratios, which a libm call is not guaranteed to be)
            var pts = new[] { new Vector3d(0.1, 0.2, 0.3), new Vector3d(0.7, 0.1, -0.2), new Vector3d(1.4, 0.3, 0.9), new Vector3d(-0.3, -0.45, 0.05), new Vector3d(0.97, 0.1, 0.1) };
            double[] expected = null;
            foreach (double k in new[] { 1.0, 1.0 / 1024, 1024.0, 1.0 / 1048576 }) {
                DMesh3 m = Sphere(k);
                var tree = new DMeshAABBTree3(m, true);
                tree.FastWindingNumber(Vector3d.Zero);
                var got = new double[pts.Length];
                for (int i = 0; i < pts.Length; ++i) {
                    got[i] = tree.FastWindingNumber(k * pts[i]);
                    Assert.True(double.IsFinite(got[i]));
                }
                if (expected == null) expected = got;
                else for (int i = 0; i < pts.Length; ++i) Assert.Equal(expected[i], got[i]);
            }
        }
    }
}
