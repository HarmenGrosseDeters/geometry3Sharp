using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// RANSAC + least squares fits of planes, spheres, cylinders and cones to sampled primitives with
    /// Gaussian noise and uniform outliers.
    /// </summary>
    public class PrimitiveFitTests
    {
        const double Noise = 0.002;
        const double OutlierFraction = 0.3;

        static double Gauss(Random rnd)
        {
            double u1 = 1.0 - rnd.NextDouble(), u2 = rnd.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        static Vector3d GaussVec(Random rnd)
        {
            return new Vector3d(Gauss(rnd), Gauss(rnd), Gauss(rnd));
        }

        // surface samples (with noise along the normal and noisy normals) plus uniform outliers in the
        // bounds of the samples (with random normals); returns whether each point is an outlier
        static void Sample(Func<Random, Vector3d[]> pointAndNormal, int n, int seed,
            out Vector3d[] points, out Vector3d[] normals, out bool[] outlier)
        {
            var rnd = new Random(seed);
            var pts = new List<Vector3d>();
            var nrm = new List<Vector3d>();
            for (int i = 0; i < n; ++i) {
                Vector3d[] pn = pointAndNormal(rnd);
                pts.Add(pn[0] + Noise * Gauss(rnd) * pn[1]);
                nrm.Add((pn[1] + 0.02 * GaussVec(rnd)).Normalized);
            }
            AxisAlignedBox3d box = BoundsUtil.Bounds(pts);
            int nOut = (int)(n * OutlierFraction / (1 - OutlierFraction));
            var flags = new List<bool>(Enumerable.Repeat(false, n));
            for (int i = 0; i < nOut; ++i) {
                pts.Add(box.Min + new Vector3d(rnd.NextDouble() * box.Width, rnd.NextDouble() * box.Height, rnd.NextDouble() * box.Depth));
                nrm.Add(GaussVec(rnd).Normalized);
                flags.Add(true);
            }
            // shuffle
            int[] order = Enumerable.Range(0, pts.Count).OrderBy(i => rnd.Next()).ToArray();
            points = order.Select(i => pts[i]).ToArray();
            normals = order.Select(i => nrm[i]).ToArray();
            outlier = order.Select(i => flags[i]).ToArray();
        }

        static void Frame(Vector3d axis, out Vector3d b1, out Vector3d b2)
        {
            Vector3d.MakePerpVectors(ref axis, out b1, out b2);
        }

        static void AssertInliers(PrimitiveFit3 fit, bool[] outlier, double minFound = 0.98)
        {
            int truePos = 0, falsePos = 0, inliers = outlier.Count(o => !o);
            for (int i = 0; i < outlier.Length; ++i) {
                if (fit.IsInlier[i] && !outlier[i]) truePos++;
                if (fit.IsInlier[i] && outlier[i]) falsePos++;
            }
            Assert.True(truePos > minFound * inliers, "found " + truePos + " of " + inliers + " inliers, rms " + fit.RMSError + " iterations " + fit.Iterations);
            // uniform outliers that happen to lie on the surface are inliers too, but few
            Assert.True(falsePos < 0.05 * outlier.Count(o => o), falsePos + " outliers accepted");
            Assert.True(fit.RMSError < 2 * Noise);
        }

        [Fact]
        public void Plane_WithNoiseAndOutliers()
        {
            Vector3d n = new Vector3d(0.3, -0.5, 0.8).Normalized, o = new Vector3d(1, 2, -1);
            Vector3d b1, b2;
            Frame(n, out b1, out b2);
            Vector3d[] pts, nrm; bool[] outlier;
            Sample((rnd) => new[] { o + (rnd.NextDouble() * 4 - 2) * b1 + (rnd.NextDouble() * 3 - 1.5) * b2, n }, 1000, 1, out pts, out nrm, out outlier);
            foreach (bool withNormals in new[] { false, true }) {
                var fit = new PrimitiveFit3(pts, withNormals ? nrm : null) { Primitive = PrimitiveFit3.Primitives.Plane, InlierDistance = 4 * Noise };
                Assert.True(fit.Compute());
                Assert.Equal(PrimitiveFit3.Results.Success, fit.Result);
                Assert.True(Math.Abs(fit.Plane.Normal.Dot(n)) > Math.Cos(0.002));
                Assert.True(Math.Abs(fit.SignedDistance(o)) < 0.001);
                AssertInliers(fit, outlier);
            }

            // plain least squares is pulled away by the outliers
            var ls = new PrimitiveFit3(pts) { Primitive = PrimitiveFit3.Primitives.Plane, UseRansac = false };
            Assert.True(ls.Compute());
            Assert.True(Math.Abs(ls.Plane.Normal.Dot(n)) < Math.Cos(0.01));
        }

        [Fact]
        public void Sphere_WithNoiseAndOutliers()
        {
            Vector3d c = new Vector3d(1, -2, 3);
            const double R = 2.5;
            Vector3d[] pts, nrm; bool[] outlier;
            // a cap of the sphere only (polar angle up to 100 degrees)
            Sample((rnd) => {
                double z = 1 - rnd.NextDouble() * (1 - Math.Cos(100 * MathUtil.Deg2Rad)), phi = 2 * Math.PI * rnd.NextDouble();
                double s = Math.Sqrt(1 - z * z);
                Vector3d d = new Vector3d(s * Math.Cos(phi), s * Math.Sin(phi), z);
                return new[] { c + R * d, d };
            }, 1500, 2, out pts, out nrm, out outlier);
            var fit = new PrimitiveFit3(pts, nrm) { Primitive = PrimitiveFit3.Primitives.Sphere, InlierDistance = 4 * Noise };
            Assert.True(fit.Compute());
            Assert.True(fit.SphereCenter.Distance(c) < 0.005, "center " + fit.SphereCenter);
            Assert.Equal(R, fit.SphereRadius, 2);
            Assert.True(Math.Abs(fit.SphereRadius - R) < 0.005);
            AssertInliers(fit, outlier);

            // without outliers, plain least squares from the algebraic fit
            var clean = pts.Where((p, i) => !outlier[i]).ToArray();
            var ls = new PrimitiveFit3(clean) { Primitive = PrimitiveFit3.Primitives.Sphere, UseRansac = false };
            Assert.True(ls.Compute());
            Assert.True(ls.SphereCenter.Distance(c) < 0.005);
            Assert.True(Math.Abs(ls.SphereRadius - R) < 0.005);
        }

        [Fact]
        public void Cylinder_WithNoiseAndOutliers()
        {
            Vector3d axis = new Vector3d(1, 2, 0.5).Normalized, a = new Vector3d(0.5, -1, 2);
            const double R = 0.7, H = 3;
            Vector3d b1, b2;
            Frame(axis, out b1, out b2);
            Vector3d[] pts, nrm; bool[] outlier;
            // three quarters of the mantle
            Sample((rnd) => {
                double phi = 1.5 * Math.PI * rnd.NextDouble(), h = (rnd.NextDouble() - 0.5) * H;
                Vector3d radial = Math.Cos(phi) * b1 + Math.Sin(phi) * b2;
                return new[] { a + h * axis + R * radial, radial };
            }, 1500, 3, out pts, out nrm, out outlier);
            foreach (bool withNormals in new[] { true, false }) {
                var fit = new PrimitiveFit3(pts, withNormals ? nrm : null) { Primitive = PrimitiveFit3.Primitives.Cylinder, InlierDistance = 4 * Noise };
                Assert.True(fit.Compute(), "normals given: " + withNormals);
                Cylinder3d cyl = fit.Cylinder;
                Assert.True(Math.Abs(cyl.Axis.Direction.Dot(axis)) > Math.Cos(0.005), "axis " + cyl.Axis.Direction);
                Assert.True(Math.Abs(cyl.Radius - R) < 0.005, "radius " + cyl.Radius);
                Assert.True(cyl.Axis.DistanceSquared(a) < 0.005 * 0.005);
                Assert.InRange(cyl.Height, H - 0.05, H + 0.05);
                AssertInliers(fit, outlier);
            }
        }

        [Fact]
        public void Cone_WithNoiseAndOutliers()
        {
            Vector3d axis = new Vector3d(-0.4, 0.2, 1).Normalized, apex = new Vector3d(1, 1, -1);
            double angle = 25 * MathUtil.Deg2Rad;
            Vector3d b1, b2;
            Frame(axis, out b1, out b2);
            Vector3d[] pts, nrm; bool[] outlier;
            // truncated cone between 0.5 and 2.5 along the axis
            Sample((rnd) => {
                double phi = 2 * Math.PI * rnd.NextDouble(), h = 0.5 + 2 * rnd.NextDouble();
                Vector3d radial = Math.Cos(phi) * b1 + Math.Sin(phi) * b2;
                Vector3d n = Math.Cos(angle) * radial - Math.Sin(angle) * axis;
                return new[] { apex + h * axis + h * Math.Tan(angle) * radial, n };
            }, 1500, 4, out pts, out nrm, out outlier);
            foreach (bool withNormals in new[] { true, false }) {
                var fit = new PrimitiveFit3(pts, withNormals ? nrm : null) { Primitive = PrimitiveFit3.Primitives.Cone, InlierDistance = 4 * Noise };
                Assert.True(fit.Compute(), "normals given: " + withNormals);
                Cone3d cone = fit.Cone;
                Assert.True(cone.Axis.Dot(axis) > Math.Cos(0.01), "axis " + cone.Axis);
                Assert.True(cone.Apex.Distance(apex) < 0.02, "apex " + cone.Apex);
                Assert.True(Math.Abs(cone.Angle - angle) < 0.005, "angle " + cone.Angle * MathUtil.Rad2Deg);
                // outliers that happen to lie on the cone beyond the samples may extend it
                Assert.True(cone.Height > 2.45);
                AssertInliers(fit, outlier);
            }

            // without outliers, least squares from the closed form estimate of apex and axis
            var cleanP = pts.Where((p, i) => !outlier[i]).ToArray();
            var cleanN = nrm.Where((p, i) => !outlier[i]).ToArray();
            var ls = new PrimitiveFit3(cleanP, cleanN) { Primitive = PrimitiveFit3.Primitives.Cone, UseRansac = false };
            Assert.True(ls.Compute());
            Assert.True(ls.Cone.Apex.Distance(apex) < 0.02);
            Assert.True(Math.Abs(ls.Cone.Angle - angle) < 0.005);
        }

        [Fact]
        public void Cylinder_FromMeshVertices()
        {
            DMesh3 mesh = new CappedCylinderGenerator() { BaseRadius = 1.5f, TopRadius = 1.5f, Height = 4, Slices = 48 }.Generate().MakeDMesh();
            // the cap vertices are outliers of the mantle
            var fit = new PrimitiveFit3(mesh) { Primitive = PrimitiveFit3.Primitives.Cylinder, InlierDistance = 0.01 };
            Assert.True(fit.Compute());
            Assert.True(Math.Abs(fit.Cylinder.Axis.Direction.Dot(Vector3d.AxisY)) > 0.9999);
            // vertices lie on the radius, the polygon's sides inside: the fit is between
            Assert.InRange(fit.Cylinder.Radius, 1.5 * Math.Cos(Math.PI / 48) - 1e-3, 1.5 + 1e-6);
        }

        [Fact]
        public void TooFewPoints_AndDeterminism()
        {
            var few = new PrimitiveFit3(new[] { Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY }) { Primitive = PrimitiveFit3.Primitives.Sphere };
            Assert.False(few.Compute());
            Assert.Equal(PrimitiveFit3.Results.TooFewPoints, few.Result);

            Vector3d[] pts, nrm; bool[] outlier;
            Sample((rnd) => {
                Vector3d d = GaussVec(rnd).Normalized;
                return new[] { d, d };
            }, 500, 5, out pts, out nrm, out outlier);
            var f1 = new PrimitiveFit3(pts) { Primitive = PrimitiveFit3.Primitives.Sphere, InlierDistance = 0.01 };
            var f2 = new PrimitiveFit3(pts) { Primitive = PrimitiveFit3.Primitives.Sphere, InlierDistance = 0.01 };
            Assert.True(f1.Compute() && f2.Compute());
            Assert.Equal(f1.SphereCenter, f2.SphereCenter);
            Assert.Equal(f1.SphereRadius, f2.SphereRadius);
            Assert.Equal(f1.Iterations, f2.Iterations);
        }
    }
}
