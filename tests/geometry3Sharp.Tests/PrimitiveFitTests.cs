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

            // the same in a tiny unit: the vertex normals must survive (no absolute degeneracy threshold)
            const double s = 1e-5;
            MeshTransforms.Scale(mesh, s);
            var small = new PrimitiveFit3(mesh) { Primitive = PrimitiveFit3.Primitives.Cylinder, InlierDistance = 0.01 * s };
            Assert.True(small.Normals.All(n => n.Length > 0.99));
            Assert.True(small.Compute());
            Assert.Equal(fit.Cylinder.Radius, small.Cylinder.Radius / s, 6);
        }

        [Theory]
        [InlineData(0.0)]       // default limit: 10 x bounding box diagonal
        public void Sphere_OnPlaneDominatedInput_IsNotAHugePlaneImitation(double maxRadius)
        {
            // a large plate (most points) with a small dome on it: the sphere fit must find the dome, not a
            // huge sphere that imitates the plate within the threshold (found on Thingi10K parts)
            var rnd = new Random(7);
            var pts = new List<Vector3d>();
            for (int i = 0; i < 3000; ++i)
                pts.Add(new Vector3d(rnd.NextDouble() * 20 - 10, rnd.NextDouble() * 20 - 10, Noise * Gauss(rnd)));
            Vector3d c = new Vector3d(3, -2, 0);
            for (int i = 0; i < 400; ++i) {
                Vector3d d = GaussVec(rnd).Normalized;
                if (d.z < 0) d.z = -d.z;
                pts.Add(c + (1 + Noise * Gauss(rnd)) * d);
            }
            var fit = new PrimitiveFit3(pts) { Primitive = PrimitiveFit3.Primitives.Sphere, InlierDistance = 4 * Noise, MaxRadius = maxRadius };
            Assert.True(fit.Compute());
            Assert.True(fit.SphereCenter.Distance(c) < 0.01, "center " + fit.SphereCenter + " radius " + fit.SphereRadius);
            Assert.True(Math.Abs(fit.SphereRadius - 1) < 0.01);
        }

        [Fact]
        public void Fit_IsInvariantUnderRotation()
        {
            // same points rotated (same order and seed): the same inliers (a guard; on real parts the local
            // optimization is what makes this hold, see the analysis mode of benchmarks/MeshRepairBenchmark)
            Vector3d axis = new Vector3d(0.2, 1, -0.3).Normalized, a = new Vector3d(1, 0, 0);
            Vector3d b1, b2;
            Frame(axis, out b1, out b2);
            Vector3d[] pts, nrm; bool[] outlier;
            Sample((rnd) => {
                double phi = 2 * Math.PI * rnd.NextDouble(), h = (rnd.NextDouble() - 0.5) * 2;
                Vector3d radial = Math.Cos(phi) * b1 + Math.Sin(phi) * b2;
                return new[] { a + h * axis + 0.5 * radial, radial };
            }, 1000, 9, out pts, out nrm, out outlier);
            Quaterniond rot = Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 37);
            // (a cone is left out: on a cylinder it is ill-posed, its apex runs off to the limit)
            foreach (var prim in new[] { PrimitiveFit3.Primitives.Plane, PrimitiveFit3.Primitives.Sphere, PrimitiveFit3.Primitives.Cylinder }) {
                var f1 = new PrimitiveFit3(pts, nrm) { Primitive = prim, InlierDistance = 4 * Noise };
                var f2 = new PrimitiveFit3(pts.Select(p => rot * p), nrm.Select(n => rot * n)) { Primitive = prim, InlierDistance = 4 * Noise };
                Assert.True(f1.Compute() && f2.Compute(), prim.ToString());
                Assert.True(Math.Abs(f1.InlierCount - f2.InlierCount) <= 0.01 * pts.Length, prim + ": " + f1.InlierCount + " vs " + f2.InlierCount);
            }
        }

        [Fact]
        public void FlatCap_NeedsLargerMaxRadius()
        {
            // a lens-like cap: sphere of radius 100 over a disc of radius 2 (sag 0.02, far above the noise),
            // beyond the default limit of 10 bounding box diagonals
            var rnd = new Random(11);
            const double R = 100;
            var pts = new List<Vector3d>();
            var nrm = new List<Vector3d>();
            for (int i = 0; i < 1000; ++i) {
                double r = 2 * Math.Sqrt(rnd.NextDouble()), phi = 2 * Math.PI * rnd.NextDouble();
                Vector3d d = new Vector3d(r * Math.Cos(phi), r * Math.Sin(phi), Math.Sqrt(R * R - r * r)).Normalized;
                pts.Add(R * d + Noise * Gauss(rnd) * d);
                nrm.Add(d);
            }
            // default: the full cap is rejected by the limit (counted), at best a smaller sphere fits part of it
            var fit = new PrimitiveFit3(pts, nrm) { Primitive = PrimitiveFit3.Primitives.Sphere, InlierDistance = 4 * Noise };
            bool ok = fit.Compute();
            Assert.True(fit.RejectedByLimits > 0);
            Assert.True(ok == false ? fit.Result == PrimitiveFit3.Results.RejectedByLimits : fit.InlierCount < 0.9 * pts.Count,
                fit.Result + " inliers " + fit.InlierCount + " radius " + fit.SphereRadius);
            var wide = new PrimitiveFit3(pts, nrm) { Primitive = PrimitiveFit3.Primitives.Sphere, InlierDistance = 4 * Noise, MaxRadius = 1000 };
            Assert.True(wide.Compute());
            Assert.InRange(wide.SphereRadius, 0.98 * R, 1.02 * R);
            Assert.True(wide.InlierCount > 0.98 * pts.Count);
        }

        [Fact]
        public void LocalOptimization_CanBeTurnedOff()
        {
            Vector3d c = new Vector3d(1, -2, 3);
            Vector3d[] pts, nrm; bool[] outlier;
            Sample((rnd) => {
                Vector3d d = GaussVec(rnd).Normalized;
                return new[] { c + 2 * d, d };
            }, 1000, 12, out pts, out nrm, out outlier);
            foreach (bool lo in new[] { true, false }) {
                var fit = new PrimitiveFit3(pts, nrm) { Primitive = PrimitiveFit3.Primitives.Sphere, InlierDistance = 4 * Noise, LocalOptimization = lo };
                Assert.True(fit.Compute());
                Assert.True(fit.SphereCenter.Distance(c) < 0.005);
                AssertInliers(fit, outlier);
            }
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

        [Fact]
        public void Ransac_LargeCloud_DoesNotStopAtTheFirstPoorModel()
        {
            // 200k points, 20% on a sphere, a tight inlier distance: a first model through 4 random points has a
            // handful of inliers, w^4 is below 1e-16 and log(1 - w^4) rounds to 0; the iteration count from the
            // confidence was -infinity cast to long, which stopped RANSAC after that model (6 inliers)
            var rnd = new Random(5);
            int n = 200000;
            var pts = new Vector3d[n];
            for (int i = 0; i < n; ++i)
                pts[i] = (i % 5 == 0) ? 2 * GaussVec(rnd).Normalized + new Vector3d(1, 2, 3)
                    : new Vector3d(rnd.NextDouble() * 6 - 2, rnd.NextDouble() * 6 - 1, rnd.NextDouble() * 6);
            var fit = new PrimitiveFit3(pts) { Primitive = PrimitiveFit3.Primitives.Sphere, InlierDistance = 1e-5 };
            Assert.True(fit.Compute());
            Assert.True(fit.Iterations > 1);
            Assert.Equal(2, fit.SphereRadius, 6);
            Assert.True(fit.SphereCenter.Distance(new Vector3d(1, 2, 3)) < 1e-6);
            Assert.True(fit.InlierCount >= n / 5);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        [InlineData(double.NaN)]
        public void Ransac_ConfidenceOne_RunsAllIterations(double confidence)
        {
            // never "confident enough": all MaxIterations. (long)(+infinity) is long.MinValue on .NET Framework,
            // which stopped RANSAC right after the first improving model there
            Vector3d[] pts, nrm; bool[] outlier;
            Sample((rnd) => { Vector3d d = GaussVec(rnd).Normalized; return new[] { 2 * d, d }; }, 500, 2, out pts, out nrm, out outlier);
            var fit = new PrimitiveFit3(pts) { Primitive = PrimitiveFit3.Primitives.Sphere, Confidence = confidence, MaxIterations = 300 };
            Assert.True(fit.Compute());
            Assert.Equal(300, fit.Iterations);
        }
    }
}
