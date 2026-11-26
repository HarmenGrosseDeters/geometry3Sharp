using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1596, only the API of master (the new-tests gate): a TpmsLattice that is not the lattice asked for has no Ok status and no mesh. Master
    /// returned Ok with the solid box (Enclosure on a Gyroid sheet, realized fraction 1.17) or with 1-10 % of the requested material (a sheet
    /// wall under about two cubes). The success paths next to them stay Ok, and clean.
    /// </summary>
    public class TpmsLatticeUnresolvedTests
    {
        static TpmsLattice Lattice(TpmsStyle style, double fraction, int divisions, bool enclosure = false, bool twoLevel = false, bool parallel = true, double scale = 1)
        {
            return new TpmsLattice {
                Type = TpmsType.Gyroid, Style = style, CellSize = scale, Divisions = divisions, VolumeFraction = fraction,
                Box = new AxisAlignedBox3d(Vector3d.Zero, scale), Enclosure = enclosure, TwoLevelSheet = twoLevel, ParallelCompute = parallel
            };
        }

        static void AssertRefused(TpmsLattice t)
        {
            Assert.NotEqual(TpmsStatus.Ok, t.Status);
            Assert.Null(t.Mesh);
            Assert.NotEqual("", t.Message);
        }

        static void AssertCleanResult(TpmsLattice t)
        {
            Assert.True(t.Status == TpmsStatus.Ok, t.Status + ": " + t.Message);
            Assert.NotNull(t.Mesh);
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(t.Mesh));
            Assert.True(Invariance.AllFinite(t.Mesh));
            Assert.True(t.Mesh.IsClosed());
        }

        [Fact]
        public void EnclosureOfAThinSheet_IsRefused()
        {
            // the repro of the issue: the realized fraction is 1.168, the box enlarged by the margin
            var t = Lattice(TpmsStyle.Sheet, 0.3, 16, enclosure: true);
            t.Compute();
            AssertRefused(t);
            Assert.True(t.RealizedVolumeFraction > 1, "fraction " + t.RealizedVolumeFraction);
        }

        [Fact]
        public void SkeletalEnclosureThatIsTheBox_IsRefused()
        {
            var t = Lattice(TpmsStyle.SkeletalLow, 0.5, 16, enclosure: true);   // realized 1.055
            t.Compute();
            AssertRefused(t);
        }

        [Theory]
        [InlineData(0.05, 16)]
        [InlineData(0.1, 16)]
        [InlineData(0.05, 6)]
        [InlineData(0.3, 8)]
        public void SheetThatLostItsWall_IsRefused(double fraction, int divisions)
        {
            var t = Lattice(TpmsStyle.Sheet, fraction, divisions);
            t.Compute();
            AssertRefused(t);
            Assert.True(t.RealizedVolumeFraction < 0.5 * fraction, "realized " + t.RealizedVolumeFraction);
        }

        [Fact]
        public void ResolvedLattices_StayOk()
        {
            foreach (var t in new[] {
                Lattice(TpmsStyle.Sheet, 0.3, 16),                          // wall about 4.5 cubes
                Lattice(TpmsStyle.Sheet, 0.1, 16, twoLevel: true),          // the thin wall with the option made for it
                Lattice(TpmsStyle.SkeletalLow, 0.3, 16),
                Lattice(TpmsStyle.SkeletalLow, 0.3, 16, enclosure: true),   // realized 0.80: the voids stay
                Lattice(TpmsStyle.Sheet, 0.3, 32, enclosure: true),         // realized 0.76
            }) {
                t.Compute();
                AssertCleanResult(t);
                Assert.Equal("", t.Message);
                Assert.InRange(t.RealizedVolumeFraction, 0.05, 1.0);
            }
        }

        [Fact]
        public void Refusal_IsReusable_AndSerialEqualsParallel()
        {
            var t = Lattice(TpmsStyle.Sheet, 0.1, 12);
            t.Compute();
            AssertRefused(t);
            string message = t.Message;
            double realized = t.RealizedVolumeFraction;
            t.Compute();                                                    // twice on one instance
            AssertRefused(t);
            Assert.Equal(message, t.Message);
            Assert.Equal(realized, t.RealizedVolumeFraction);
            t.VolumeFraction = 0.5;                                         // the state of the refusal must not stay
            t.Compute();
            AssertCleanResult(t);
            Assert.Equal("", t.Message);
            DMesh3 good = new DMesh3(t.Mesh);
            t.VolumeFraction = 0.1;
            t.Compute();
            AssertRefused(t);
            Assert.Equal(realized, t.RealizedVolumeFraction);
            t.VolumeFraction = 0.5;
            t.Compute();
            Assert.Equal("", Invariance.Difference(good, t.Mesh));          // and the instance gives the same mesh as before

            var serial = Lattice(TpmsStyle.Sheet, 0.1, 12, parallel: false);
            serial.Compute();
            AssertRefused(serial);
            Assert.Equal(message, serial.Message);
            Assert.Equal(realized, serial.RealizedVolumeFraction);
        }

        [Fact]
        public void Refusals_UnderSimilarities()
        {
            // the status does not depend on the frame; the exact ones (2^k scale) give the same realized fraction bit for bit
            double reference = -1;
            foreach (Similarity s in Similarity.All()) {
                var t = new TpmsLattice {
                    Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, VolumeFraction = 0.1, CellSize = s.Scale, Divisions = 12,
                    Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1) * s.Scale),
                    Origin = s.Apply(Vector3d.Zero), Rotation = s.Rotation
                };
                t.Compute();
                AssertRefused(t);
                if (s.IsExact) {
                    if (reference < 0) reference = t.RealizedVolumeFraction;
                    Assert.Equal(reference, t.RealizedVolumeFraction);
                }
            }
        }

        [Fact]
        public void EnclosureRefusal_AndSuccess_UnderTheExactScalings()
        {
            DMesh3 reference = null;
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                var refused = Lattice(TpmsStyle.Sheet, 0.3, 16, enclosure: true, scale: s.Scale);
                refused.Compute();
                AssertRefused(refused);
                var ok = Lattice(TpmsStyle.SkeletalLow, 0.3, 16, enclosure: true, scale: s.Scale);
                ok.Compute();
                AssertCleanResult(ok);
                DMesh3 back = Invariance.MappedBack(ok.Mesh, s);
                if (reference == null) reference = back;
                else Assert.Equal("", Invariance.Difference(reference, back));   // the 2^k scalings are exact: the same mesh bit for bit
            }
        }
    }
}
