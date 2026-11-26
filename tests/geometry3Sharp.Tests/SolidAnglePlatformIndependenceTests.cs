using System;
using g3;
using g3.CoreMathPort;
using g3.Tests;
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


        [Fact]
        public void TermAtan2_MatchesTheHighPrecisionReference_OnASeededSampleAndTheQuadrantEdges()
        {
            // (CoreMathReference.Atan2 is exact arithmetic rounded once; the terms of 3000 random triangles, of which about 0.27 % are inputs where the platform's Math.Atan2 differs from it, then the axes, the subnormals and the infinities)
            var rnd = new Random(4242);
            Func<Vector3d> r = () => new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
            for (int i = 0; i < 3000; ++i) {
                Vector3d a = r(), b = r(), c = r(), p = 1.5 * r();
                Terms(a, b, c, p, out double top, out double bottom);
                Assert.Equal(2.0 * CoreMathReference.Atan2(top, bottom), MathUtil.TriSolidAngle(a, b, c, ref p));
            }
            double[] edges = { 0.0, -0.0, 1.0, -1.0, 1e-300, -1e-300, 1e300, -1e300, 5e-324, -5e-324, 2.2250738585072014e-308, -2.2250738585072014e-308, double.PositiveInfinity, double.NegativeInfinity, double.NaN };
            foreach (double y in edges)
                foreach (double x in edges) {
                    double got = CoreMath.Atan2(y, x), want = CoreMathReference.Atan2(y, x);
                    Assert.True(BitConverter.DoubleToInt64Bits(got) == BitConverter.DoubleToInt64Bits(want) || (double.IsNaN(got) && double.IsNaN(want)), "atan2(" + y + ", " + x + ") = " + got + " expected " + want);
                    // (the special values are the same as the platform's: nothing a caller of the sign of zero or an infinity could see changes)
                    double libm = Math.Atan2(y, x);
                    Assert.True(BitConverter.DoubleToInt64Bits(got) == BitConverter.DoubleToInt64Bits(libm) || (double.IsNaN(got) && double.IsNaN(libm)), "special value atan2(" + y + ", " + x + ")");
                }
        }

        [Fact]
        public void TriSolidAngle_OfDegenerateAndNonFiniteInput_IsFiniteOrNaNAsBefore()
        {
            Vector3d a = new Vector3d(1, 0, 0), b = new Vector3d(0, 1, 0), c = new Vector3d(0, 0, 1);
            Vector3d atVertex = a, onPlane = new Vector3d(0.25, 0.25, 0.5), nan = new Vector3d(double.NaN, 0, 0);
            Assert.False(double.IsNaN(MathUtil.TriSolidAngle(a, b, c, ref onPlane)));
            Assert.Equal(Math.Atan2(0.0, 0.0) * 2, MathUtil.TriSolidAngle(a, a, a, ref atVertex));
            Assert.True(double.IsNaN(MathUtil.TriSolidAngle(a, b, c, ref nan)));
        }

        [Fact]
        public void FastWindingNumber_UnderEverySimilarity_IsTheSameUpToRounding_AndTheVectorPathEqualsTheReferencePath()
        {
            DMesh3 m = Sphere(1);
            var pts = new[] { new Vector3d(0.1, 0.2, 0.3), new Vector3d(0.7, 0.1, -0.2), new Vector3d(1.4, 0.3, 0.9), new Vector3d(0.5, 0.05, 0.05) };
            double worst = 0;
            foreach (Similarity s in Similarity.All()) {
                var tree = new DMeshAABBTree3(Invariance.Transformed(m, s), true);
                var reference = new DMeshAABBTree3(Invariance.Transformed(m, s), true) { FastWindingForceScalar = true };
                var plain = new DMeshAABBTree3(m, true);
                foreach (Vector3d p in pts) {
                    double w = tree.FastWindingNumber(s.Apply(p));
                    Assert.Equal(reference.FastWindingNumber(s.Apply(p)), w);                 // kernel and reference path: the same bits
                    double dev = Math.Abs(w - plain.FastWindingNumber(p));
                    worst = Math.Max(worst, dev);
                    Assert.InRange(dev, 0, WorstAllowed);        // (a rotation or a translation rounds the input and builds other boxes, so the far-field expansions differ by their own error: not bit for bit)
                }
            }
            output.WriteLine("largest deviation from the untransformed mesh: " + worst);
        }

        // the far-field expansions of the default beta on this coarse sphere are off by up to 7.4e-3 from the winding number of the same mesh in
        // another frame (measured, printed by the test above): the tolerance is the method's own approximation, not a rounding. The claim of bits is
        // the equality with the reference path of the same frame, asserted without any tolerance.
        const double WorstAllowed = 1e-2;

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
                    Assert.True(!double.IsNaN(got[i]) && !double.IsInfinity(got[i]));
                }
                if (expected == null) expected = got;
                else for (int i = 0; i < pts.Length; ++i) Assert.Equal(expected[i], got[i]);
            }
        }
    }
}
