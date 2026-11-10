using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> the supported input range of MeshRobustRepair (docs/QA.md 2.5a): outside, a documented status, no hang, no false success </summary>
    public class RobustRepairInputRangeTests
    {
        static DMesh3 Scene(double scale)
        {
            var a = DegenerateFamilies.Sphere(4); var b = DegenerateFamilies.Sphere(4);
            foreach (int v in b.VertexIndices()) b.SetVertex(v, b.GetVertex(v) + new Vector3d(0.7, 0.2, 0.1));
            new MeshEditor(a).AppendMesh(b);
            foreach (int v in a.VertexIndices()) a.SetVertex(v, scale * a.GetVertex(v));
            return a;
        }

        // a small scene far from the origin: the extent is small, so the earlier scale check passes (this hung beyond 1e176)
        [Theory]
        [InlineData(1.9e176)]
        [InlineData(1e100)]
        [InlineData(1e300)]
        public void FarFromTheOrigin_Reported(double offset)
        {
            DMesh3 m = Scene(1);
            foreach (int v in m.VertexIndices()) m.SetVertex(v, m.GetVertex(v) + new Vector3d(0, offset, offset));
            var r = new MeshRobustRepair(m);
            bool ok = true;
            Assert.True(Timed.Finishes(() => ok = r.Apply(), TimeSpan.FromSeconds(20)));
            Assert.False(ok);
            Assert.StartsWith("outside the supported range", r.ResultReason);
        }

        [Theory]
        [InlineData(1e130)]
        [InlineData(1e-100)]
        [InlineData(1e-300)]
        [InlineData(1e-320)]          // subnormal
        public void OutsideRange_ReportedNotRepaired(double scale)
        {
            DMesh3 m = Scene(scale);
            var r = new MeshRobustRepair(m);
            bool ok = true;
            Assert.True(Timed.Finishes(() => ok = r.Apply(), TimeSpan.FromSeconds(20)));
            Assert.False(ok);
            Assert.StartsWith("outside the supported range", r.ResultReason);
            Assert.Equal(Scene(scale).TriangleCount, r.Mesh.TriangleCount);   // not replaced
        }

        [Theory]
        [InlineData(-190)]
        [InlineData(-100)]
        [InlineData(0)]
        [InlineData(100)]
        [InlineData(190)]
        [InlineData(390)]
        public void InsideRange_Repaired_Pow2BitIdentical(int e)
        {
            double s = Math.Pow(2, e);
            var r0 = new MeshRobustRepair(Scene(1));
            Assert.True(r0.Apply());
            var r = new MeshRobustRepair(Scene(s));
            Assert.True(r.Apply());
            Assert.Equal("", r.ResultReason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            var back = new DMesh3(r.Mesh);
            foreach (int v in back.VertexIndices()) back.SetVertex(v, back.GetVertex(v) / s);
            Assert.Equal("", Invariance.Difference(r0.Mesh, back));
        }

        [Fact]
        public void ZeroCoordinates_AreInRange()
        {
            DMesh3 m = DegenerateFamilies.Sphere(3);   // has coordinates that are exactly 0 on the axes
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
        }
    }
}
