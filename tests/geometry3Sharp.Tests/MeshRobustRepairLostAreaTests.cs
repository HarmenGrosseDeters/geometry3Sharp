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
            Assert.InRange(r.LostAreaMaxDistance, 0.4, 0.45);        // (the wall's samples are up to half the slab's gap from the faces they are farthest from)
        }

        [Fact]
        public void BoxWithInteriorWall_Solidify_LostAreaIsTheWallsToo()
        {
            var r = Run(BoxWithWall(), SolidifyOnly);
            Assert.Equal(MeshRobustRepair.ResultPaths.Solidify, r.ResultPath);
            Assert.Equal(2 * r.Solidify.UsedCellSize, r.UsedLostAreaTolerance);
            Assert.InRange(r.LostArea, 1.9, 2.0 + 1e-9);
            Assert.InRange(r.LostAreaShare, 1.9 / 8.4, 2.0 / 8.4 + 1e-9);
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
        public void OverlappingSpheres_LostAreaIsTheCapsInsideEachOther()
        {
            // two unit spheres 1.2 apart: each loses the cap inside the other, of height 1 - 0.6, area 2 pi r h
            double caps = 2 * 2 * Math.PI * 1 * 0.4;
            var r = Run(OverlappingSpheres());
            Assert.Equal(MeshRobustRepair.ResultPaths.ExactResolve, r.ResultPath);
            Assert.InRange(r.LostArea, 0.98 * caps, 1.02 * caps);
            // the solidify (a coarse grid here): its surface is up to a cell off the exact one, and the tolerance is two cells, so the loss near the intersection circle is smaller
            var s = Run(OverlappingSpheres(), SolidifyOnly);
            Assert.Equal(MeshRobustRepair.ResultPaths.Solidify, s.ResultPath);
            Assert.InRange(s.LostArea, 0.5 * r.LostArea, r.LostArea + 1e-9);
        }

        [Fact]
        public void AlphaWrap_Path_ReportsWithTheOffsetsTolerance()
        {
            // a sheet apart from a box: the wrap covers both, nothing is lost within twice its offset
            var r = Run(BoxWithFin(3));
            Assert.Equal(MeshRobustRepair.ResultPaths.AlphaWrap, r.ResultPath);
            Assert.Equal(2 * r.AlphaWrap.UsedOffset, r.UsedLostAreaTolerance);
            Assert.Equal(0.0, r.LostArea);
            Assert.Empty(r.LostTriangleIds);
            Assert.True(r.LostAreaMaxDistance <= r.UsedLostAreaTolerance);
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

        public static IEnumerable<object[]> Names() { return Similarity.Names(); }

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
                    Assert.Equal(b.LostAreaShare, r.LostAreaShare, 6);
                    Assert.Equal(b.LostArea * s.Scale * s.Scale, r.LostArea, 6);
                }
            }
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
        public void HalfTheSurface_IsHalfTheArea_ParallelEqualsSerial_AcrossChunks()
        {
            // 6 * 47^2 * 2 = 26508 triangles: two chunks of the batch; the result is the hemisphere x <= 0
            DMesh3 input = Sphere(1, Vector3d.Zero, 48);
            Assert.True(input.TriangleCount > (1 << 14) + 1);
            DMesh3 half = new DMesh3(input);
            foreach (int t in input.TriangleIndices())
                if (input.GetTriCentroid(t).x > 0) half.RemoveTriangle(t);
            var par = MeshRobustRepairLostArea.Measure(input, half, 1e-3, true);
            var ser = MeshRobustRepairLostArea.Measure(input, half, 1e-3, false);
            var again = MeshRobustRepairLostArea.Measure(input, half, 1e-3, true);
            Assert.Equal(BitConverter.DoubleToInt64Bits(par.LostArea), BitConverter.DoubleToInt64Bits(ser.LostArea));
            Assert.Equal(BitConverter.DoubleToInt64Bits(par.LostArea), BitConverter.DoubleToInt64Bits(again.LostArea));
            Assert.Equal(BitConverter.DoubleToInt64Bits(par.MaxDistance), BitConverter.DoubleToInt64Bits(ser.MaxDistance));
            Assert.Equal(par.LostTriangles, ser.LostTriangles);
            Assert.Equal(par.LostTriangles, again.LostTriangles);
            Assert.InRange(par.LostArea / par.InputArea, 0.48, 0.52);
            // the lost triangles are the ones on the removed side
            Assert.All(par.LostTriangles, t => Assert.True(input.GetTriCentroid(t).x > -0.02));
            Assert.True(par.LostTriangles.Count > 0.45 * input.TriangleCount);
        }

        [Fact]
        public void Samples_AreSubdivisionsPerTriangleCount()
        {
            Assert.Equal(MeshRobustRepairLostArea.MaxSubdivisions, MeshRobustRepairLostArea.SubdivisionsFor(100));
            Assert.Equal(1, MeshRobustRepairLostArea.SubdivisionsFor(10 * MeshRobustRepairLostArea.SamplesTarget));
            Assert.Equal(2, MeshRobustRepairLostArea.SubdivisionsFor(99110));
            Assert.Equal(1, MeshRobustRepairLostArea.SubdivisionsFor(0));
        }
    }
}
