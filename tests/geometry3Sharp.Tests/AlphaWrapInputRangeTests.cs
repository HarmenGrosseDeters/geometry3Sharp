using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> the supported input range of MeshAlphaWrap (docs/QA.md 2.5a): outside, InvalidInput, no hang, no false success </summary>
    public class AlphaWrapInputRangeTests
    {
        static DMesh3 Sphere(double scale)
        {
            DMesh3 m = DegenerateFamilies.Sphere(3);
            foreach (int v in m.VertexIndices()) m.SetVertex(v, scale * m.GetVertex(v));
            return m;
        }

        [Theory]
        [InlineData(-200)]
        [InlineData(-180)]
        [InlineData(180)]
        [InlineData(200)]
        [InlineData(-700)]
        [InlineData(700)]
        public void OutsideRange_InvalidInput_NoHang(int e)
        {
            var w = new MeshAlphaWrap(Sphere(Math.Pow(2, e))) { RelativeAlpha = 5, RelativeOffset = 100 };
            bool ok = true;
            Assert.True(Timed.Finishes(() => ok = w.Compute(), TimeSpan.FromSeconds(20)));
            Assert.False(ok);
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidInput, w.Status);
            Assert.StartsWith("outside the supported range", w.Error);
        }

        // FINDINGS 4: coordinates of 1e-276 next to an extent of 3e17 were wrapped with Success and an input vertex outside
        [Fact]
        public void TinyCoordinatesNextToLargeExtent_NoFalseSuccess()
        {
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(9.477270473290355E-276, 3.610388751729659E-276, 9.477270473290355E-276));
            m.AppendVertex(new Vector3d(2.8823037615171174E+17, 3.610388751729659E-276, 9.477270473290355E-276));
            m.AppendVertex(new Vector3d(9.477270473290355E-276, 2.8823037615171174E+17, 9.477270473290355E-276));
            m.AppendVertex(new Vector3d(9.477270473290355E-276, 3.610388751729659E-276, 2.8823037615171174E+17));
            m.AppendTriangle(0, 2, 1); m.AppendTriangle(0, 1, 3); m.AppendTriangle(0, 3, 2); m.AppendTriangle(1, 2, 3);
            var w = new MeshAlphaWrap(m) { RelativeAlpha = 5, RelativeOffset = 100 };
            Assert.False(w.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidInput, w.Status);
        }

        // a point cluster far from the origin compared to its extent (FINDINGS 8) is reported, not failed late
        [Fact]
        public void ClusterFarFromTheOrigin_InvalidInput()
        {
            var pts = new System.Collections.Generic.List<Vector3d>();
            var rnd = new Random(1);
            for (int i = 0; i < 20; ++i) pts.Add(new Vector3d(-26.5 + 5e-14 * rnd.NextDouble(), -26.5 + 5e-14 * rnd.NextDouble(), -26.5 + 5e-14 * rnd.NextDouble()));
            var w = new MeshAlphaWrap(pts) { RelativeAlpha = 5, RelativeOffset = 100 };
            Assert.False(w.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidInput, w.Status);
        }

        [Theory]
        [InlineData(-120)]
        [InlineData(-60)]
        [InlineData(0)]
        [InlineData(60)]
        [InlineData(120)]
        public void InsideRange_Success_Pow2BitIdentical(int e)
        {
            double s = Math.Pow(2, e);
            var w0 = new MeshAlphaWrap(Sphere(1)) { RelativeAlpha = 5, RelativeOffset = 100 };
            Assert.True(w0.Compute());
            var w = new MeshAlphaWrap(Sphere(s)) { RelativeAlpha = 5, RelativeOffset = 100 };
            Assert.True(Timed.Finishes(() => w.Compute(), TimeSpan.FromSeconds(20)));
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, w.Status);
            var back = new DMesh3(w.Result);
            foreach (int v in back.VertexIndices()) back.SetVertex(v, back.GetVertex(v) / s);
            Assert.Equal("", Invariance.Difference(w0.Result, back));
            Assert.True(w.Result.IsClosed());
        }
    }
}
