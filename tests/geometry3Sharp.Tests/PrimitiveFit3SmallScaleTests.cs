using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> QA 2.0 T1 finding R26: the closed-form sphere and cylinder estimates failed for clouds of size below about 1e-7 </summary>
    public class PrimitiveFit3SmallScaleTests
    {
        [Theory]
        [InlineData(1.0)]
        [InlineData(1e-3)]
        [InlineData(5.9604644775390625e-08)]   // 2^-24
        [InlineData(1e-12)]
        public void SphereClosedForm_GivesTheSameFitAtEveryScale(double scale)
        {
            DMesh3 m = Defects.Sphere(new Vector3d(0.2, -0.1, 0.3), 1, 8);
            Vector3d[] pts = m.Vertices().Select(p => p * scale).ToArray();
            var fit = new PrimitiveFit3(pts) { Primitive = PrimitiveFit3.Primitives.Sphere, InlierDistance = 0.02 * scale, UseRansac = false };
            Assert.True(fit.Compute());
            Assert.Equal(PrimitiveFit3.Results.Success, fit.Result);
            Assert.Equal(1.0, fit.SphereRadius / scale, 6);
            Assert.True((fit.SphereCenter / scale - new Vector3d(0.2, -0.1, 0.3)).Length < 1e-6);
            Assert.Equal(pts.Length, fit.InlierCount);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(5.9604644775390625e-08)]
        public void CylinderClosedForm_GivesTheSameFitAtEveryScale(double scale)
        {
            var pts = new System.Collections.Generic.List<Vector3d>();
            var nrm = new System.Collections.Generic.List<Vector3d>();
            for (int k = 0; k < 24; ++k)
                for (int h = 0; h < 5; ++h) {
                    double a = 2 * Math.PI * k / 24;
                    pts.Add(new Vector3d(0.5 * Math.Cos(a), 0.5 * Math.Sin(a), 0.4 * h) * scale);
                    nrm.Add(new Vector3d(Math.Cos(a), Math.Sin(a), 0));
                }
            var fit = new PrimitiveFit3(pts, nrm) { Primitive = PrimitiveFit3.Primitives.Cylinder, InlierDistance = 0.01 * scale, UseRansac = false };
            Assert.True(fit.Compute());
            Assert.Equal(0.5, fit.Cylinder.Radius / scale, 6);
        }
    }
}
