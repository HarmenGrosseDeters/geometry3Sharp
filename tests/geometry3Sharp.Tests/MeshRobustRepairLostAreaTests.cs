using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRobustRepair.MeasureLostArea (#1479): the area of the input that the result does not represent. Procedural inputs with a known answer: a
    /// closed box (nothing lost), a box with an interior wall (the wall's two faces), a box with a fin (an open sheet that the exact resolve drops), a
    /// sphere with a hole that the repair closes, two overlapping spheres (the caps inside each other), and the solidify and the alpha wrap paths.
    /// </summary>
    public class MeshRobustRepairLostAreaTests
    {
        static DMesh3 Box(Vector3d c, Vector3d half) { return new TrivialBox3Generator() { Box = new Box3d(c, half) }.Generate().MakeDMesh(); }

        static DMesh3 Sphere(double r, Vector3d c, int n = 12)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 UnitBox() { return Box(new Vector3d(0.5, 0.5, 0.5), new Vector3d(.5, .5, .5)); }

        // the unit box (area 6) and a slab through it, y in [0.45, 0.55], that fills it from wall to wall: the slab's end faces lie on the
        // box's walls, its two large faces (area 1 each) are interior walls of the solid. Triangle ids 12 to 23 are the slab's
        static DMesh3 BoxWithWall()
        {
            DMesh3 m = UnitBox();
            MeshEditor.Append(m, Box(new Vector3d(0.5, 0.5, 0.5), new Vector3d(.5, .05, .5)));
            return m;
        }

        // the slab's two large faces: 4 triangles, 2 each
        static bool IsWallFace(DMesh3 m, int t)
        {
            Vector3d c = m.GetTriCentroid(t);
            return t >= 12 && Math.Abs(m.GetTriNormal(t).y) > 0.99 && (Math.Abs(c.y - 0.45) < 1e-9 || Math.Abs(c.y - 0.55) < 1e-9);
        }

        // the unit box and an open sheet of 32 triangles (area 1) standing on its face x = 1, from x = 1 to 2: a fin. Triangle ids 12 to 43 are the sheet's
        static DMesh3 BoxWithFin(double gap = 0)
        {
            DMesh3 m = UnitBox();
            DMesh3 sheet = new GriddedRectGenerator() { Width = 1, Height = 1, EdgeVertices = 5 }.Generate().MakeDMesh();
            MeshTransforms.Translate(sheet, new Vector3d(1 + gap + 0.5, 0.5, 0.5));
            MeshEditor.Append(m, sheet);
            return m;
        }

        static DMesh3 Holed(DMesh3 m, double cos)
        {
            foreach (int t in m.TriangleIndices().ToList())
                if (m.GetTriCentroid(t).Normalized.Dot(Vector3d.AxisX) > cos) m.RemoveTriangle(t);
            return m;
        }

        static DMesh3 OverlappingSpheres(int n = 16)
        {
            DMesh3 m = Sphere(1, Vector3d.Zero, n);
            MeshEditor.Append(m, Sphere(1, new Vector3d(1.2, 0, 0), n));
            return m;
        }

        static readonly Action<MeshRobustRepair> SolidifyOnly = r => { r.ExactResolve = false; r.AlphaWrapFallback = false; r.SolidifyResolution = 48; };

        static MeshRobustRepair Run(DMesh3 mesh, Action<MeshRobustRepair> configure = null, bool measure = true)
        {
            var r = new MeshRobustRepair(mesh) { MeasureLostArea = measure };
            configure?.Invoke(r);
            Assert.True(r.Apply(), r.ResultReason);
            return r;
        }

        // every output that exists without the report, as a hash (the mesh bit for bit)
        static string Fingerprint(MeshRobustRepair r, bool ok)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(ok).Append('|').Append(r.ResultReason).Append('|').Append(r.FallbackReason).Append('|').Append(r.ExactResolveReason).Append('|')
              .Append(r.SolidifyReason).Append('|').Append(r.WrapReason).Append('|').Append(r.UsedExactResolve).Append(r.UsedSolidify).Append(r.UsedAlphaWrap)
              .Append(r.UsedInput).Append('|').Append(r.PassedThroughTriangles).Append('|').Append(r.AlphaWrapTriangles).Append('|')
              .Append(BitConverter.DoubleToInt64Bits(r.RepairVolumeIoU)).Append('|').Append(r.VolumeChecksRun).Append('|').Append(r.ResultPath).Append('|')
              .Append(BitConverter.DoubleToInt64Bits(r.ResultVolumeIoU)).Append('|');
            foreach (int v in r.Mesh.VertexIndices()) {
                Vector3d p = r.Mesh.GetVertex(v);
                sb.Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in r.Mesh.TriangleIndices()) {
                Index3i tri = r.Mesh.GetTriangle(t);
                sb.Append(tri.a).Append(',').Append(tri.b).Append(',').Append(tri.c).Append(';');
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
        }

        [Fact]
        public void ClosedBox_NothingIsLost()
        {
            var r = Run(UnitBox());
            Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, r.ResultPath);
            Assert.Equal(0.0, r.LostArea);
            Assert.Equal(0.0, r.LostAreaShare);
            Assert.Empty(r.LostTriangleIds);
            Assert.True(r.LostAreaMaxDistance <= r.UsedLostAreaTolerance, "the result is the input");
            // no stage resolution: the exact resolve's rounding bound, RoundingMaxDisplacement times the input's diagonal
            Assert.Equal(r.RoundingMaxDisplacement * Math.Sqrt(3), r.UsedLostAreaTolerance, 12);
        }

        [Fact]
        public void BoxWithInteriorWall_LostAreaIsTheWallsTwoFaces()
        {
            DMesh3 given = BoxWithWall();
            Assert.Equal(8.4, MeshMeasurements.AreaT(given, given.TriangleIndices()), 9);
            var r = Run(new DMesh3(given));
            Assert.Equal(MeshRobustRepair.ResultPaths.ExactResolve, r.ResultPath);
            Assert.Equal(6.0, MeshMeasurements.AreaT(r.Mesh, r.Mesh.TriangleIndices()), 9);       // the solid is the box
            // both faces of the wall, as the input has them (the slab's end faces lie on the box's walls: not lost)
            Assert.Equal(2.0, r.LostArea, 9);
            Assert.Equal(2.0 / 8.4, r.LostAreaShare, 9);
            Assert.Equal(4, r.LostTriangleIds.Count);
            Assert.All(r.LostTriangleIds, t => Assert.True(IsWallFace(given, t), "triangle " + t));
            Assert.InRange(r.LostAreaMaxDistance, 0.4, 0.4501);        // (the wall's samples are up to half the slab's gap from the faces they are farthest from)
        }

        [Fact]
        public void SphereWithHole_TheRepairCloses_LosesNothing()
        {
            var r = Run(Holed(Sphere(1, Vector3d.Zero, 12), 0.97));
            Assert.Equal(MeshRobustRepair.ResultPaths.Repair, r.ResultPath);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            Assert.Equal(0.0, r.LostArea);
            Assert.Equal(0.0, r.LostAreaShare);
            Assert.Empty(r.LostTriangleIds);
        }

        [Fact]
        public void OpenSheetOnASolid_TheExactResolveDrops_LostAreaIsTheSheets()
        {
            // the fin of the diagnosis note: an open sheet attached to a solid's surface is no part of the solid
            var r = Run(BoxWithFin());
            Assert.Equal(MeshRobustRepair.ResultPaths.ExactResolve, r.ResultPath);
            Assert.Equal(1.0, r.LostArea, 9);
            Assert.Equal(1.0 / 7.0, r.LostAreaShare, 9);
            Assert.Equal(Enumerable.Range(12, 32), r.LostTriangleIds);
        }

        [Fact]
        public void Off_ByDefault_NothingIsMeasured_AndNoOtherOutputChanges()
        {
            foreach (Func<DMesh3> make in new Func<DMesh3>[] { UnitBox, BoxWithWall, () => BoxWithFin(), () => Holed(Sphere(1, Vector3d.Zero, 12), 0.97), () => OverlappingSpheres(8) }) {
                var off = new MeshRobustRepair(make());
                Assert.False(off.MeasureLostArea);
                bool okOff = off.Apply();
                Assert.True(double.IsNaN(off.LostArea) && double.IsNaN(off.LostAreaShare) && double.IsNaN(off.LostAreaMaxDistance) && double.IsNaN(off.UsedLostAreaTolerance));
                Assert.Empty(off.LostTriangleIds);
                var on = new MeshRobustRepair(make()) { MeasureLostArea = true };
                bool okOn = on.Apply();
                Assert.False(double.IsNaN(on.LostArea));
                Assert.Equal(Fingerprint(off, okOff), Fingerprint(on, okOn));
            }
        }

        [Fact]
        public void EmptyInput_IsNotMeasured()
        {
            var r = new MeshRobustRepair(new DMesh3()) { MeasureLostArea = true };
            Assert.False(r.Apply());
            Assert.True(double.IsNaN(r.LostArea) && double.IsNaN(r.LostAreaShare));
            Assert.Empty(r.LostTriangleIds);
        }

        [Fact]
        public void Reuse_TheSameInstanceTwice_GivesTheSameReport_AndResetsIt()
        {
            var r = new MeshRobustRepair(BoxWithWall()) { MeasureLostArea = true };
            Assert.True(r.Apply());
            double area = r.LostArea, share = r.LostAreaShare, maxd = r.LostAreaMaxDistance;
            List<int> ids = r.LostTriangleIds;
            Assert.Equal(2.0, area, 9);
            r.Mesh = BoxWithWall();
            Assert.True(r.Apply());
            Assert.Equal(area, r.LostArea);
            Assert.Equal(share, r.LostAreaShare);
            Assert.Equal(maxd, r.LostAreaMaxDistance);
            Assert.Equal(ids, r.LostTriangleIds);
            Assert.NotSame(ids, r.LostTriangleIds);
            // a third run, without the report: nothing of the last one is left
            r.MeasureLostArea = false;
            r.Mesh = BoxWithWall();
            Assert.True(r.Apply());
            Assert.True(double.IsNaN(r.LostArea) && double.IsNaN(r.LostAreaShare) && double.IsNaN(r.LostAreaMaxDistance) && double.IsNaN(r.UsedLostAreaTolerance));
            Assert.Empty(r.LostTriangleIds);
        }

        // --- the measurement itself (MeshRobustRepairLostArea.Measure) ---

        static DMesh3 Sheet(double y, int n = 5)
        {
            DMesh3 m = new GriddedRectGenerator() { Width = 1, Height = 1, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, new Vector3d(0, y, 0));
            return m;
        }

        [Fact]
        public void Tolerance_IsTheLimit_OfTheDistanceToTheResult()
        {
            DMesh3 input = Sheet(0), shifted = Sheet(0.01);
            double area = MeshMeasurements.AreaT(input, input.TriangleIndices());
            var within = MeshRobustRepairLostArea.Measure(input, shifted, 0.0101);
            Assert.Equal(0.0, within.LostArea);
            Assert.Empty(within.LostTriangles);
            Assert.Equal(0.01, within.MaxDistance, 12);
            var beyond = MeshRobustRepairLostArea.Measure(input, shifted, 0.0099);
            Assert.Equal(area, beyond.LostArea, 12);
            Assert.Equal(area, beyond.InputArea, 12);
            Assert.Equal(input.TriangleCount, beyond.LostTriangles.Count);
            // an empty result represents nothing
            var none = MeshRobustRepairLostArea.Measure(input, new DMesh3(), 1);
            Assert.Equal(area, none.LostArea, 12);
            Assert.True(double.IsPositiveInfinity(none.MaxDistance));
        }

        [Fact]
        public void PartlyLostTriangle_CountsInTheArea_ButNotAsLostTriangle_UntilHalfIsLost()
        {
            // two triangles; the result covers the sheet except a strip of a third of its width on the right
            DMesh3 input = Sheet(0, 2), result = Sheet(0, 2);
            MeshTransforms.Translate(result, new Vector3d(-0.3, 0, 0));
            var r = MeshRobustRepairLostArea.Measure(input, result, 1e-6);
            Assert.InRange(r.LostArea / r.InputArea, 0.05, 0.3);
            Assert.Empty(r.LostTriangles);
            // moved by 0.7: a good half of each triangle is not covered
            MeshTransforms.Translate(result, new Vector3d(-0.4, 0, 0));
            var more = MeshRobustRepairLostArea.Measure(input, result, 1e-6);
            Assert.InRange(more.LostArea / more.InputArea, 0.5, 0.9);
            Assert.NotEmpty(more.LostTriangles);
        }

        [Fact]
        public void BoxWithInteriorWall_Solidify_LostAreaIsTheWallsToo()
        {
            var r = Run(BoxWithWall(), SolidifyOnly);
            Assert.Equal(MeshRobustRepair.ResultPaths.Solidify, r.ResultPath);
            // (the unrefined contour: the cell the tolerance uses is the one of the unrefined regions)
            Assert.Equal(2 * r.Solidify.UsedCellSize * r.Solidify.RefineFactor, r.UsedLostAreaTolerance, 12);
            // the thin wall makes the solidify refine locally; its tolerance is the coarse cell (0.5 at resolution 48 on this input, about 4 cells of the wall's
            // thickness): a lower bound, 1.28 of the 2.0 here. The kept faces, and the wall's rim within the band of them, read as present
            Assert.InRange(r.LostArea, 1.0, 2.0 + 1e-9);
            Assert.InRange(r.LostAreaShare, 1.0 / 8.4, 2.0 / 8.4 + 1e-9);
        }

        [Fact]
        public void OverlappingSpheres_LostAreaIsTheCapsInsideEachOther_AndALowerBoundByTheBlindBand()
        {
            // two unit spheres 1.2 apart: each loses the cap inside the other, of height 1 - 0.6, area 2 pi r h
            double caps = 2 * 2 * Math.PI * 1 * 0.4;
            var r = Run(OverlappingSpheres());
            Assert.Equal(MeshRobustRepair.ResultPaths.ExactResolve, r.ResultPath);
            Assert.InRange(r.LostArea, 0.98 * caps, 1.02 * caps);
            // The solidify reads LESS, by design: its tolerance is two grid cells (2 / SolidifyResolution of the diagonal, 0.083 here at resolution 48 on a
            // diagonal of 4.2), so the part of a cap within that of the kept surface counts as present. The report is a lower bound of the lost area by that
            // band: measured on this input 2.6 (52 %) at resolution 32, 3.3 (66 %) at 48 and 4.3 (86 %) at 128 of the 5.03 of the caps. What this test
            // accepts: more than half of the exact path's value and never more than it (the band only removes area).
            var s = Run(OverlappingSpheres(), SolidifyOnly);
            Assert.Equal(MeshRobustRepair.ResultPaths.Solidify, s.ResultPath);
            Assert.InRange(s.LostArea, 0.5 * r.LostArea, r.LostArea + 1e-9);
            Assert.True(s.LostArea < 0.95 * r.LostArea, "the blind band of two cells hides part of the caps");
        }

        [Fact]
        public void AlphaWrap_Path_ReportsWithTheOffsetsTolerance()
        {
            // a sheet apart from a box: the wrap covers both, nothing is lost within twice its offset
            var r = Run(BoxWithFin(3));
            Assert.Equal(MeshRobustRepair.ResultPaths.AlphaWrap, r.ResultPath);
            Assert.Equal(2 * r.AlphaWrap.UsedOffset, r.UsedLostAreaTolerance, 12);
            Assert.Equal(0.0, r.LostArea);
            Assert.Empty(r.LostTriangleIds);
            Assert.True(r.LostAreaMaxDistance <= r.UsedLostAreaTolerance);
        }

        [Fact]
        public void AlphaWrap_WithATarget_ToleranceFollowsWrapTargetMaxDistance()
        {
            // the reduction to a target is certified within WrapTargetMaxDistance offsets of the wrap, which lies an offset from the input: (1 + 3) offsets
            var r = Run(BoxWithFin(3), c => { c.WrapTargetTriangles = 20; c.WrapTargetMaxDistance = 3; });
            Assert.Equal(MeshRobustRepair.ResultPaths.AlphaWrap, r.ResultPath);
            Assert.Equal(20, r.AlphaWrapTarget);
            Assert.Equal(4 * r.AlphaWrap.UsedOffset, r.UsedLostAreaTolerance, 12);
            // the default distance 0.5 (or a smaller one) keeps the two offsets
            var d = Run(BoxWithFin(3), c => { c.WrapTargetTriangles = 20; c.WrapTargetMaxDistance = 0.1; });
            Assert.Equal(2 * d.AlphaWrap.UsedOffset, d.UsedLostAreaTolerance, 12);
        }

        [Fact]
        public void GivenTolerance_ReplacesTheDerivedOne()
        {
            // the wall's samples are at most 0.4167 from the result: a tolerance above that loses none, a tiny one all of the wall
            var big = Run(BoxWithWall(), r => r.LostAreaTolerance = 0.5);
            Assert.Equal(0.5, big.UsedLostAreaTolerance);
            Assert.Equal(0.0, big.LostArea);
            var mid = Run(BoxWithWall(), r => r.LostAreaTolerance = 0.2);
            Assert.InRange(mid.LostArea, 0.1, 1.9);
            var small = Run(BoxWithWall(), r => r.LostAreaTolerance = 1e-3);
            Assert.Equal(2.0, small.LostArea, 9);
        }

        [Theory]
        [InlineData(1024.0)]        // a power of two: the report equals the unscaled one, scaled, bit for bit
        [InlineData(1000.0)]
        [InlineData(0.001)]
        public void GivenTolerance_IsInTheCallersUnits_AtAnyScale(double scale)
        {
            // the tolerance the caller gives is a length of the caller's input, however the repair's working frame scales it
            var b = Run(BoxWithWall(), r => r.LostAreaTolerance = 0.2);
            DMesh3 scaled = BoxWithWall();
            MeshTransforms.Scale(scaled, new Vector3d(scale, scale, scale), Vector3d.Zero);
            var r2 = Run(scaled, r => r.LostAreaTolerance = 0.2 * scale);
            Assert.Equal(0.2 * scale, r2.UsedLostAreaTolerance);
            Assert.Equal(b.LostTriangleIds, r2.LostTriangleIds);
            Assert.Equal(b.LostAreaShare, r2.LostAreaShare, 12);
            Assert.Equal(b.LostArea * scale * scale, r2.LostArea, 9);
            Assert.Equal(b.LostAreaMaxDistance * scale, r2.LostAreaMaxDistance, 9);
            if (scale == 1024.0)
                Assert.Equal(BitConverter.DoubleToInt64Bits(b.LostArea * scale * scale), BitConverter.DoubleToInt64Bits(r2.LostArea));
        }

        [Fact]
        public void GivenToleranceThatMakesNoSense_CountsAsDerived()
        {
            double derived = Run(BoxWithWall()).UsedLostAreaTolerance;
            foreach (double bad in new double[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                var r = Run(BoxWithWall(), c => c.LostAreaTolerance = bad);
                Assert.Equal(derived, r.UsedLostAreaTolerance);
                Assert.Equal(2.0, r.LostArea, 9);
            }
        }

        // the sphere of 12 edge vertices, scaled to the given extent and moved to (1e6, 1e6, 1e6)
        static DMesh3 FarSphere(double extent)
        {
            DMesh3 m = Sphere(1, Vector3d.Zero, 12);
            foreach (int v in m.VertexIndices().ToList())
                m.SetVertex(v, m.GetVertex(v) * (extent / 2) + new Vector3d(1e6, 1e6, 1e6));
            return m;
        }

        [Theory]
        [InlineData(1e-6)]
        [InlineData(1e-5)]
        [InlineData(1e-2)]
        public void FarFromTheOrigin_AResultThatIsTheInput_ReadsExactlyZero(double extent)
        {
            // the rounding bound's ulp term and the query's own error: at 1e6 a unit in the last place is 1.2e-10, more than the relative bound of the sphere
            var r = Run(FarSphere(extent));
            Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, r.ResultPath);
            Assert.Equal(0.0, r.LostArea);
            Assert.Equal(0.0, r.LostAreaShare);
            Assert.Empty(r.LostTriangleIds);
            Assert.True(r.LostAreaMaxDistance <= r.UsedLostAreaTolerance);
            double ulp = 1.0 / (1L << 33);          // the unit in the last place of 1e6 (2^-33)
            Assert.True(r.UsedLostAreaTolerance >= r.RoundingUlpFactor * ulp);
            Assert.True(r.UsedLostAreaTolerance >= MeshRobustRepair.LostAreaQueryUlps * ulp);
        }

        [Fact]
        public void FarFromTheOrigin_WithoutTheUlpTerm_TheQueryErrorAloneKeepsItAtZero()
        {
            // RoundingUlpFactor 0 turns the rounding's ulp term off: what is left of the tolerance at extent 1e-6 is 1.7e-11 of relative bound, below the
            // sample's rounding (2^-33 = 1.2e-10 per unit): the query term of 8 units
            var r = Run(FarSphere(1e-6), c => c.RoundingUlpFactor = 0);
            Assert.Equal(MeshRobustRepair.ResultPaths.PassThrough, r.ResultPath);
            Assert.Equal(0.0, r.LostArea);
            Assert.Empty(r.LostTriangleIds);
            double queryTerm = MeshRobustRepair.LostAreaQueryUlps / (1L << 33);
            Assert.InRange(r.UsedLostAreaTolerance, queryTerm, queryTerm + 2e-11);     // (plus the relative bound, 1.7e-11)
            Assert.True(r.LostAreaMaxDistance > 2e-11, "the samples are off the input by more than the relative bound: " + r.LostAreaMaxDistance);
        }

        [Fact]
        public void RoundingAbsoluteTolerance_IsPartOfTheDerivedTolerance_InTheCallersUnits()
        {
            // a caller who lets the rounding move vertices by 0.2 gets a tolerance of 0.2: the wall (up to 0.4167 from the result) is partly within it
            var b = Run(BoxWithWall(), r => r.RoundingAbsoluteTolerance = 0.2);
            Assert.Equal(0.2, b.UsedLostAreaTolerance, 12);
            Assert.InRange(b.LostArea, 0.1, 1.9);
            DMesh3 scaled = BoxWithWall();
            MeshTransforms.Scale(scaled, new Vector3d(1024, 1024, 1024), Vector3d.Zero);
            var r = Run(scaled, c => c.RoundingAbsoluteTolerance = 0.2 * 1024);
            Assert.Equal(BitConverter.DoubleToInt64Bits(b.UsedLostAreaTolerance * 1024), BitConverter.DoubleToInt64Bits(r.UsedLostAreaTolerance));
            Assert.Equal(BitConverter.DoubleToInt64Bits(b.LostArea * 1024 * 1024), BitConverter.DoubleToInt64Bits(r.LostArea));
            Assert.Equal(0.2 * 1024, r.RoundingAbsoluteTolerance);       // (the caller's setting is given back as it was)
        }

        [Fact]
        public void FarDegenerateTriangle_DoesNotWidenTheTolerance_AndIsNotListed()
        {
            // the rounding's bound is relative to the largest component, not to the extent of all triangles: a zero area triangle far away is a component of its own
            DMesh3 input = BoxWithWall();
            int a = input.AppendVertex(new Vector3d(50, 0, 0)), b = input.AppendVertex(new Vector3d(50.001, 0, 0)), c = input.AppendVertex(new Vector3d(50.002, 0, 0));
            int degenerate = input.AppendTriangle(a, b, c);
            Assert.True(degenerate >= 0);
            double without = Run(BoxWithWall()).UsedLostAreaTolerance;
            var r = Run(input);
            Assert.Equal(without, r.UsedLostAreaTolerance, 11);
            Assert.DoesNotContain(degenerate, r.LostTriangleIds);
            Assert.Equal(2.0, r.LostArea, 9);
        }

        static DMesh3 PlateWithGapsAndOverlaps()
        {
            // a plate, four pairs of boxes beside it with a gap of a fraction of a cell (hidden gaps: the solidify refines the cells around them), and two
            // boxes that overlap the plate, so that only a solidify makes a solid of it. Triangle ids 0 to 107: the plate and the pairs (kept)
            Func<double, double, double, double, double, double, DMesh3> box = (x0, y0, z0, x1, y1, z1) => Box(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2));
            DMesh3 m = box(-10, -5, -2, 10, 5, 2);
            for (int i = 0; i < 4; ++i) {
                double x = 12 + 4 * i;
                MeshEditor.Append(m, box(x, -1, -1, x + 2, 0, 1));
                MeshEditor.Append(m, box(x, 0.225, -1, x + 2, 1.225, 1));
            }
            MeshEditor.Append(m, box(-8, -3, -1, -2, 3, 1));
            MeshEditor.Append(m, box(-9, -4, -1, -1, 4, 3));
            MeshTransforms.Rotate(m, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 27));
            return m;
        }

        [Fact]
        public void Solidify_WithLocalRefinement_ToleranceIsTheCoarseCell_AndTheKeptSurfaceReadsZero()
        {
            var r = Run(PlateWithGapsAndOverlaps(), c => { c.ExactResolve = false; c.AlphaWrapFallback = false; c.SolidifyResolution = 48; });
            Assert.Equal(MeshRobustRepair.ResultPaths.Solidify, r.ResultPath);
            Assert.True(r.Solidify.RefineFactor > 1, "the hidden gaps are refined");
            // UsedCellSize is the finest cell; the contour of the unrefined regions is on the coarse one
            Assert.Equal(2 * r.Solidify.UsedCellSize * r.Solidify.RefineFactor, r.UsedLostAreaTolerance, 12);
            Assert.True(r.LostArea > 0, "the boxes inside the plate are interior surface");
            Assert.All(r.LostTriangleIds, t => Assert.True(t >= 108, "triangle " + t + " of the kept surface"));
        }

        public static IEnumerable<object[]> Names() { return Similarity.Names(); }

        static void AssertRelative(double expected, double actual, double relative, string what)
        {
            double scale = Math.Max(Math.Abs(expected), Math.Abs(actual));
            Assert.True(scale == 0 || Math.Abs(expected - actual) <= relative * scale, what + ": " + expected + " against " + actual);
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void Invariance_ScaledInputGivesTheSameReport(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (Func<DMesh3> make in new Func<DMesh3>[] { BoxWithWall, () => BoxWithFin() }) {
                var b = Run(make());
                var r = Run(Invariance.Transformed(make(), s));
                Assert.Equal(b.ResultPath, r.ResultPath);
                Assert.Equal(b.LostTriangleIds, r.LostTriangleIds);
                if (s.IsExact) {
                    // a power of two: every length, area and distance scales exactly, the share does not change at all
                    Assert.Equal(BitConverter.DoubleToInt64Bits(b.LostAreaShare), BitConverter.DoubleToInt64Bits(r.LostAreaShare));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(b.LostArea * s.Scale * s.Scale), BitConverter.DoubleToInt64Bits(r.LostArea));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(b.LostAreaMaxDistance * s.Scale), BitConverter.DoubleToInt64Bits(r.LostAreaMaxDistance));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(b.UsedLostAreaTolerance * s.Scale), BitConverter.DoubleToInt64Bits(r.UsedLostAreaTolerance));
                } else {
                    // relative (the lost area at scale 1e-3 is 2e-6): measured differences are 1.8e-10 and 4.4e-8 of the value
                    AssertRelative(b.LostAreaShare, r.LostAreaShare, 1e-6, "share");
                    AssertRelative(b.LostArea * s.Scale * s.Scale, r.LostArea, 1e-6, "area");
                    AssertRelative(b.LostAreaMaxDistance * s.Scale, r.LostAreaMaxDistance, 1e-6, "distance");
                    // (the tolerance follows the axis aligned boxes of the components and the coordinates' size, so it differs under a rotation and a translation, by
                    // design: the rounding bound it takes is the same; equal for a pure scale, not smaller than half of the scaled one otherwise)
                    if (s.IsRotated == false && s.Translation == Vector3d.Zero)
                        AssertRelative(b.UsedLostAreaTolerance * s.Scale, r.UsedLostAreaTolerance, 1e-6, "tolerance");
                    else
                        Assert.True(r.UsedLostAreaTolerance >= 0.5 * b.UsedLostAreaTolerance * s.Scale);
                }
            }
        }

        [Fact]
        public void LostAreaOfAFalseApply_IsReportedWhereAResultRemains()
        {
            // a repair that drops a small separate sphere: Apply() is false (a lost part) with the big sphere in Mesh; the report is the small sphere's area
            DMesh3 big = Sphere(1, Vector3d.Zero), small = Sphere(0.15, new Vector3d(3, 0, 0));
            DMesh3 input = new DMesh3(big);
            MeshEditor.Append(input, small);
            double bigArea = MeshMeasurements.AreaT(big, big.TriangleIndices()), smallArea = MeshMeasurements.AreaT(small, small.TriangleIndices());
            var r = new MeshRobustRepair(input) { RepairF = (m) => new DMesh3(big), MeasureLostArea = true };
            Assert.False(r.Apply());
            Assert.Equal(MeshRobustRepair.LostSeparatePart, r.ResultReason);
            Assert.NotEqual(MeshRobustRepair.ResultPaths.None, r.ResultPath);
            Assert.Equal(smallArea, r.LostArea, 9);
            Assert.Equal(smallArea / (bigArea + smallArea), r.LostAreaShare, 9);
            Assert.Equal(Enumerable.Range(big.TriangleCount, small.TriangleCount), r.LostTriangleIds);
            Assert.True(r.LostAreaMaxDistance > 1.8);
            // a false Apply() with no result (an empty input, a failure before the stages) is not measured: EmptyInput_IsNotMeasured
        }

        [Fact]
        public void CancelDuringTheReport_IsACancel_ApplyIsFalse_AndTheReportIsNaN()
        {
            var r = new MeshRobustRepair(BoxWithWall()) { MeasureLostArea = true };
            // (ResultPath is set just before the report; every stage before it runs to its end)
            r.CancelF = () => r.ResultPath != MeshRobustRepair.ResultPaths.None;
            Assert.False(r.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.ExactResolve, r.ResultPath);
            Assert.Equal(6.0, MeshMeasurements.AreaT(r.Mesh, r.Mesh.TriangleIndices()), 9);       // the result is in Mesh
            Assert.True(double.IsNaN(r.LostArea) && double.IsNaN(r.LostAreaShare) && double.IsNaN(r.UsedLostAreaTolerance));
            Assert.Empty(r.LostTriangleIds);
            // not measured, not cancelled: true as without the report
            var off = new MeshRobustRepair(BoxWithWall());
            off.CancelF = () => off.ResultPath != MeshRobustRepair.ResultPaths.None;
            Assert.True(off.Apply());
        }

        // a triangle of the plane z = 0 and, for the boundary tests, tiny triangles around chosen samples of it
        static DMesh3 ResultAroundSamples(Vector3d[] samples, int[] present)
        {
            var m = new DMesh3();
            double e = 1e-3;
            foreach (int i in present) {
                Vector3d s = samples[i];
                m.AppendTriangle(m.AppendVertex(s + new Vector3d(-e, -e, 0)), m.AppendVertex(s + new Vector3d(e, -e, 0)), m.AppendVertex(s + new Vector3d(0, 2 * e, 0)));
            }
            return m;
        }

        static DMesh3 UnitTriangle()
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, 0)), m.AppendVertex(new Vector3d(1, 0, 0)), m.AppendVertex(new Vector3d(0, 1, 0)));
            return m;
        }

        [Fact]
        public void Boundary_ASampleAtExactlyTheTolerance_IsNotLost()
        {
            DMesh3 input = Sheet(0, 3), result = Sheet(0.01, 3);
            double d = MeshRobustRepairLostArea.Measure(input, result, 1).MaxDistance;
            Assert.Equal(0.01, d, 9);
            var at = MeshRobustRepairLostArea.Measure(input, result, d);
            Assert.Equal(0.0, at.LostArea);
            Assert.Empty(at.LostTriangles);
            var below = MeshRobustRepairLostArea.Measure(input, result, d * (1 - 1e-9));
            Assert.True(below.LostArea > 0);
        }

        [Fact]
        public void Boundary_ATriangleWithExactlyHalfItsSamplesLost_IsNotListed_MoreThanHalfIs()
        {
            DMesh3 input = UnitTriangle();
            Vector3d[] samples = MeshRobustRepairLostArea.SamplePoints(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), MeshRobustRepairLostArea.SubdivisionsFor(1));
            Assert.Equal(16, samples.Length);
            var half = MeshRobustRepairLostArea.Measure(input, ResultAroundSamples(samples, Enumerable.Range(0, 8).ToArray()), 0.01);
            Assert.Equal(0.25, half.LostArea, 12);            // 8 of 16 samples of an area of 0.5
            Assert.Empty(half.LostTriangles);
            var more = MeshRobustRepairLostArea.Measure(input, ResultAroundSamples(samples, Enumerable.Range(0, 7).ToArray()), 0.01);
            Assert.Equal(0.5 * 9 / 16, more.LostArea, 12);
            Assert.Equal(new List<int> { 0 }, more.LostTriangles);
            var fewer = MeshRobustRepairLostArea.Measure(input, ResultAroundSamples(samples, Enumerable.Range(0, 9).ToArray()), 0.01);
            Assert.Empty(fewer.LostTriangles);
        }

        [Fact]
        public void HalfTheSurface_IsHalfTheArea_ParallelEqualsSerial_AcrossBatches()
        {
            // a flat sheet of 2 * 99^2 = 19602 triangles (4 subdivisions: 313632 samples, two batches of at most 262144 and a triangle); the result is its half x <= 0,
            // flat, so that the queries are cheap
            DMesh3 input = Sheet(0, 100);
            DMesh3 half = new DMesh3(input);
            foreach (int t in input.TriangleIndices())
                if (input.GetTriCentroid(t).x > 0) half.RemoveTriangle(t);
            var par = MeshRobustRepairLostArea.Measure(input, half, 1e-3, true);
            var ser = MeshRobustRepairLostArea.Measure(input, half, 1e-3, false);
            var again = MeshRobustRepairLostArea.Measure(input, half, 1e-3, true);
            Assert.True(par.Samples > par.PeakBatchSamples, "more than one batch");
            Assert.Equal(BitConverter.DoubleToInt64Bits(par.LostArea), BitConverter.DoubleToInt64Bits(ser.LostArea));
            Assert.Equal(BitConverter.DoubleToInt64Bits(par.LostArea), BitConverter.DoubleToInt64Bits(again.LostArea));
            Assert.Equal(BitConverter.DoubleToInt64Bits(par.MaxDistance), BitConverter.DoubleToInt64Bits(ser.MaxDistance));
            Assert.Equal(par.LostTriangles, ser.LostTriangles);
            Assert.Equal(par.LostTriangles, again.LostTriangles);
            Assert.InRange(par.LostArea / par.InputArea, 0.49, 0.5);
            Assert.All(par.LostTriangles, t => Assert.True(input.GetTriCentroid(t).x > -0.02));
            Assert.True(par.LostTriangles.Count > 0.45 * input.TriangleCount);
        }

        [Fact]
        public void BuffersFollowTheInput_NotAFixedChunk()
        {
            DMesh3 input = BoxWithWall();
            var r = MeshRobustRepairLostArea.Measure(input, UnitBox(), 1e-6);
            // (24 triangles: 16 of them above the mean area get 25 samples, the rest 16)
            Assert.Equal(16 * 25 + 8 * 16, r.Samples);
            Assert.Equal(r.Samples, r.PeakBatchSamples);
        }

        [Fact]
        public void OneHugeTriangleAmongManySmall_IsSampledByItsArea()
        {
            // 19602 triangles of a unit sheet (y = 0) and one of 5000 (y = 1, 0 <= x, z, x + z <= 100); the result covers the sheet and the part of the huge
            // triangle with x + z <= 45 (area 1012.5): 3987.5 of it is lost. 16 samples of the huge triangle read 12 / 16 (3750, -6 %)
            DMesh3 input = Sheet(0, 100);
            int a = input.AppendVertex(new Vector3d(0, 1, 0)), b = input.AppendVertex(new Vector3d(100, 1, 0)), c = input.AppendVertex(new Vector3d(0, 1, 100));
            int huge = input.AppendTriangle(a, b, c);
            DMesh3 result = Sheet(0, 100);
            int ra = result.AppendVertex(new Vector3d(0, 1, 0)), rb = result.AppendVertex(new Vector3d(45, 1, 0)), rc = result.AppendVertex(new Vector3d(0, 1, 45));
            result.AppendTriangle(ra, rb, rc);
            var r = MeshRobustRepairLostArea.Measure(input, result, 1e-6);
            Assert.Equal(1.0 + 5000.0, r.InputArea, 6);
            Assert.InRange(r.LostArea, 3987.5 * 0.98, 3987.5 * 1.02);
            Assert.Contains(huge, r.LostTriangles);
            Assert.True(r.Samples < 2 * MeshRobustRepairLostArea.SamplesTarget, "the budget stays bounded: " + r.Samples);
            // the same, with every power of two scale: the sampling rule follows the area ratio, so the report scales exactly
            DMesh3 si = new DMesh3(input), sr = new DMesh3(result);
            foreach (DMesh3 m in new[] { si, sr })
                foreach (int v in m.VertexIndices().ToList())
                    m.SetVertex(v, m.GetVertex(v) * 1024);
            var s = MeshRobustRepairLostArea.Measure(si, sr, 1e-6 * 1024);
            Assert.Equal(BitConverter.DoubleToInt64Bits(r.LostArea * 1024 * 1024), BitConverter.DoubleToInt64Bits(s.LostArea));
            Assert.Equal(r.Samples, s.Samples);
        }

        [Fact]
        public void Samples_AreSubdivisionsPerTriangleCountAndArea()
        {
            Assert.Equal(MeshRobustRepairLostArea.MaxSubdivisions, MeshRobustRepairLostArea.SubdivisionsFor(100));
            Assert.Equal(1, MeshRobustRepairLostArea.SubdivisionsFor(10 * MeshRobustRepairLostArea.SamplesTarget));
            Assert.Equal(2, MeshRobustRepairLostArea.SubdivisionsFor(99110));
            Assert.Equal(1, MeshRobustRepairLostArea.SubdivisionsFor(0));
            // by area: no samples without area, the base for a triangle of at most the mean, more in proportion to the area above it, up to the cap
            Assert.Equal(0, MeshRobustRepairLostArea.SubdivisionsOf(0, 1, 4));
            Assert.Equal(0, MeshRobustRepairLostArea.SubdivisionsOf(double.NaN, 1, 4));
            Assert.Equal(4, MeshRobustRepairLostArea.SubdivisionsOf(1, 1, 4));
            Assert.Equal(4, MeshRobustRepairLostArea.SubdivisionsOf(0.1, 1, 4));
            Assert.Equal(8, MeshRobustRepairLostArea.SubdivisionsOf(4, 1, 4));
            Assert.Equal(MeshRobustRepairLostArea.MaxLargeSubdivisions, MeshRobustRepairLostArea.SubdivisionsOf(1e9, 1, 4));
        }
    }
}
