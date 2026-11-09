using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Very large and very small extents. Before, MeshRobustRepair threw an ArgumentException from ExactPredicates
    /// ("coordinates must be finite") for finite coordinates near +-1e308: the exact winding of the volume check took a
    /// far point at four times the extent from the centre, which was infinite. Outside about [1e-103, 1e102] the volume
    /// check's IoU was NaN (volumes are cubes of the extent), which passed as "no solid": a result that lost most of the
    /// solid was accepted. And MeshAlphaWrap threw from Delaunay3 when the margin of its box was below the units in the
    /// last place of the coordinates. Now the volume checks scale by a power of two into [2^-300, 2^300] (exact), the
    /// wrap reports InvalidInput, and MeshRobustRepair fails with ScaleNotFinite where the diagonal overflows
    /// (HugeCoordinatesRepairApiTests).
    /// </summary>
    public class HugeCoordinatesRepairTests
    {
        [Fact]
        public void RobustRepair_LargeFiniteDiagonal_Repaired()
        {
            // an extent of about 2e120: the diagonal is finite, the volume check's volumes are not (it scales by a power
            // of two); a sphere with a duplicated triangle is repaired, and the result has the sphere's volume
            DMesh3 m = UnitSphere(Math.Pow(2, 400));
            int t0 = m.TriangleIndices().First();
            Index3i f = m.GetTriangle(t0);
            int va = m.AppendVertex(m.GetVertex(f.a)), vb = m.AppendVertex(m.GetVertex(f.b)), vc = m.AppendVertex(m.GetVertex(f.c));
            m.AppendTriangle(va, vb, vc);
            // (volumes measured at unit size: the cube of 2^400 overflows)
            DMesh3 unit = UnitSphere(1);
            double v0 = MeshMeasurements.VolumeArea(unit, unit.TriangleIndices(), unit.GetVertex).x;
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
            Assert.Equal("", r.ResultReason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            Assert.True(r.Mesh.IsClosed());
            var back = new DMesh3(r.Mesh);
            foreach (int v_ in back.VertexIndices())
                back.SetVertex(v_, Math.Pow(2, -400) * back.GetVertex(v_));
            double v = MeshMeasurements.VolumeArea(back, back.TriangleIndices(), back.GetVertex).x;
            Assert.InRange(v / v0, 0.99, 1.01);
        }

        static DMesh3 UnitSphere(double scale)
        {
            var g = new Sphere3Generator_NormalizedCube() { EdgeVertices = 5, Radius = 1 };
            g.Generate();
            DMesh3 m = g.MakeDMesh();
            foreach (int v in m.VertexIndices())
                m.SetVertex(v, scale * m.GetVertex(v));
            return m;
        }

        public static TheoryData<int, bool> Exponents()
        {
            var d = new TheoryData<int, bool>();
            foreach (int k in new[] { -900, -600, -350, 350, 600, 900, 1020 })
                foreach (bool cells in new[] { true, false })
                    d.Add(k, cells);
            return d;
        }

        [Theory]
        [MemberData(nameof(Exponents))]
        public void VolumeCheck_Pow2Scales_SameIoUAsAtUnitSize(int k, bool cells)
        {
            // the IoU of a sphere and of the sphere at half the radius is bit for bit the one at unit size, for scales
            // 2^k far outside [2^-300, 2^300]. Before: NaN, which passed as "no solid" (true) above about 1e102, and a
            // throw from the exact predicates near 1e308
            double s = Math.Pow(2, k);
            Assert.Equal(Math.Pow(2, k), s);
            foreach (double f in new[] { 1, 0.5 }) {
                bool unitOk = MeshRepairMetrics.VolumeIoUAtLeast(new MeshRepairMetrics.SolidReference(UnitSphere(1)),
                    MeshRepairMetrics.SolidReference.Unoriented(UnitSphere(f)), 0.99, out double unitIoU, useCells: cells);
                bool ok = MeshRepairMetrics.VolumeIoUAtLeast(new MeshRepairMetrics.SolidReference(UnitSphere(s)),
                    MeshRepairMetrics.SolidReference.Unoriented(UnitSphere(f * s)), 0.99, out double iou, useCells: cells);
                Assert.Equal(f == 1, unitOk);
                Assert.Equal(unitOk, ok);
                Assert.Equal(unitIoU, iou);
                double plain = MeshRepairMetrics.VolumeIoU(UnitSphere(s), UnitSphere(f * s), out double missing, out double extra);
                Assert.Equal(MeshRepairMetrics.VolumeIoU(UnitSphere(1), UnitSphere(f), out double m1, out double e1), plain);
                Assert.Equal(m1, missing);
                Assert.Equal(e1, extra);
            }
        }

        [Fact]
        public void AlphaWrap_MarginBelowTheUlp_InvalidInputNotAThrow()
        {
            // a flat triangle of size 1e100 at z = 1e120, alpha and offset 1e99: the margin of the box (about 4e99) is below
            // half the unit in the last place of z (about 7e103), so the box would have no extent in z
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 1e120)), b = m.AppendVertex(new Vector3d(1e100, 0, 1e120));
            int c = m.AppendVertex(new Vector3d(0, 1e100, 1e120));
            m.AppendTriangle(a, b, c);
            var w = new MeshAlphaWrap(m) { Alpha = 1e99, Offset = 1e99 };
            Assert.False(w.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidInput, w.Status);
            Assert.Null(w.Result);
            Assert.False(string.IsNullOrEmpty(w.Error));
        }

        [Fact]
        public void AlphaWrap_MarginAboveTheUlp_NotRejected()
        {
            // the same triangle with alpha and offset 1e105, a margin above the unit in the last place of z: the guard
            // does not reject it (it only rejects boxes that do not fit doubles)
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 1e120)), b = m.AppendVertex(new Vector3d(1e100, 0, 1e120));
            int c = m.AppendVertex(new Vector3d(0, 1e100, 1e120));
            m.AppendTriangle(a, b, c);
            var w = new MeshAlphaWrap(m) { Alpha = 1e105, Offset = 1e105 };
            w.Compute();
            Assert.NotEqual(MeshAlphaWrap.WrapStatus.InvalidInput, w.Status);
        }
    }
}
