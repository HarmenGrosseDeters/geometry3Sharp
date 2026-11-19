using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Harness/PortableMath (#1151): the platform-independent test inputs are the library's, up to the rounding of the
    /// trigonometry.
    /// </summary>
    public class PortableMathTests
    {
        // within a few units in the last place of the radius (an absolute bound: near a zero of the cosine the platform's
        // value can be many ulps of the tiny result off, eg on net48, and still be within 1e-16 of the radius)
        static bool Close(double a, double b, double radius) { return Math.Abs(a - b) <= 4 * 2.2204460492503131e-16 * radius; }

        [Theory]
        [InlineData(0.5f, 0.5f, 1.5f, 32, false)]
        [InlineData(0.5f, 0.5f, 1.5f, 24, true)]
        [InlineData(1.0f, 0.25f, 2.0f, 7, false)]
        [InlineData(0.3f, 0.7f, 0.5f, 16, true)]
        public void CappedCylinder_IsTheGeneratorsMesh_UpToTheRoundingOfTheTrigonometry(float rb, float rt, float h, int slices, bool noShared)
        {
            DMesh3 lib = new CappedCylinderGenerator() { BaseRadius = rb, TopRadius = rt, Height = h, Slices = slices, NoSharedVertices = noShared }.Generate().MakeDMesh();
            DMesh3 port = PortableMath.CappedCylinder(rb, rt, h, slices, noShared);
            Assert.Equal(lib.VertexCount, port.VertexCount);
            Assert.Equal(lib.TriangleCount, port.TriangleCount);
            foreach (int t in lib.TriangleIndices())
                Assert.Equal(lib.GetTriangle(t), port.GetTriangle(t));
            // the generator's angles, to tell where the platform's trigonometry is the correctly rounded one
            float delta = 360.0f * MathUtil.Deg2Radf / slices;
            int exact = 0;
            foreach (int v in lib.VertexIndices()) {
                Vector3d a = lib.GetVertex(v), b = port.GetVertex(v);
                Assert.Equal(a.y, b.y);
                double radius = a.y == 0 ? rb : rt;
                Assert.True(Close(a.x, b.x, radius) && Close(a.z, b.z, radius), $"vertex {v}: {a.x:R} {a.z:R} vs {b.x:R} {b.z:R}");
                if (a == b)
                    exact++;
            }
            // where Math.Cos / Math.Sin agree with CoreMath at every ring angle, the meshes are the same bits
            bool platformExact = true;
            for (int k = 0; k <= slices; ++k) {
                float angle = (float)k * delta;
                platformExact &= Math.Cos(angle) == PortableMath.Cos(angle) && Math.Sin(angle) == PortableMath.Sin(angle);
            }
            if (platformExact)
                Assert.Equal(lib.VertexCount, exact);
        }

        [Fact]
        public void AxisAngleD_IsQuaterniondsUpToTheRoundingOfTheTrigonometry()
        {
            var rnd = new Random(5);
            for (int i = 0; i < 200; ++i) {
                Vector3d axis = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
                double deg = 720 * rnd.NextDouble() - 360;
                Quaterniond q = Quaterniond.AxisAngleD(axis, deg), p = PortableMath.AxisAngleD(axis, deg);
                double half = 0.5 * (MathUtil.Deg2Rad * deg);
                if (Math.Sin(half) == PortableMath.Sin(half) && Math.Cos(half) == PortableMath.Cos(half)) {
                    Assert.Equal(q.x, p.x); Assert.Equal(q.y, p.y); Assert.Equal(q.z, p.z); Assert.Equal(q.w, p.w);
                } else
                    Assert.True(Math.Abs(q.w - p.w) <= 4e-16 && Math.Abs(q.x - p.x) <= 4e-16 && Math.Abs(q.y - p.y) <= 4e-16 && Math.Abs(q.z - p.z) <= 4e-16);
            }
        }
    }
}
