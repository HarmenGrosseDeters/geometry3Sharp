using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;
using gs;
using Xunit;
using Roles = System.Collections.Generic.List<g3.ExactMeshBoolean.InputRole>;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRoundingRepair.VolumeBefore / VolumeAfter and SolidBooleanReport.RoundingVolumeBefore / RoundingVolumeAfter: the volume of
    /// the mesh before and after the rounding repair, so that the volume the repair moves is a number in the report
    /// (docs/research/ROUNDING_THIN_VOLUME_2026-11.md, option a1). The output of the repair is unchanged.
    /// </summary>
    public class RoundingVolumeReportTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union, I = ExactMeshBoolean.Operations.Intersection, D = ExactMeshBoolean.Operations.Difference;

        static double Pow2(int k) { return BitConverter.Int64BitsToDouble((long)(k + 1023) << 52); }
        static double Volume(DMesh3 m) => RoundingThinVolumeTests.Volume(m);
        static List<DMesh3> Plates() => RoundingThinVolumeTests.ThinPlates(RoundingThinVolumeTests.TA, 3.5e-6, 1.5e-6);
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1) => RoundingThinVolumeTests.Box(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1));
        static List<DMesh3> TwoBoxes() => new List<DMesh3> { Box(0, 0, 0, 2, 2, 2), Box(1, 0.5, 0.25, 3, 1.5, 1.75) };

        // ---- the values ---------------------------------------------------------------------------------------------------------------

        [Fact]
        public void ThinPlates_TheReportShowsTheVolumeTheRoundingMoved()
        {
            var exact = new ExactMeshBoolean(Plates(), U) { SafeRounding = false };
            Assert.True(exact.Compute());
            var op = new ExactMeshBoolean(Plates(), U);
            Assert.True(op.Compute());
            MeshRoundingRepair r = op.Rounding;
            // before: the exact union, rounded; after: the result; the difference: the 3.7e-7 of the fans, 0.7 % of the union
            Assert.Equal(Volume(exact.Result), r.VolumeBefore, 15);
            Assert.Equal(Volume(op.Result), r.VolumeAfter, 15);
            Assert.InRange(r.VolumeAfter - r.VolumeBefore, 3.69e-7, 3.71e-7);
            Assert.InRange((r.VolumeAfter - r.VolumeBefore) / r.VolumeBefore, 0.0070, 0.0073);
            // the pipeline's report has the same two numbers
            var p = new SolidBooleanPipeline(Plates(), U);
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(r.VolumeBefore, p.Report.RoundingVolumeBefore);
            Assert.Equal(r.VolumeAfter, p.Report.RoundingVolumeAfter);
            Assert.True(p.Report.Clean);
            Assert.Empty(p.Report.RoundingRemoved);
            Assert.Equal(Volume(p.Result), p.Report.RoundingVolumeAfter, 15);
        }

        [Fact]
        public void WhereTheRepairChangesNothing_TheTwoVolumesAreEqual_BitForBit()
        {
            // the plate flush on a block (shape 2)
            var p = new SolidBooleanPipeline(RoundingThinVolumeTests.PlateOnBlock(), I);
            Assert.True(p.Compute(), p.Error);
            Assert.True(p.Report.RoundingVolumeBefore > 1e-4);
            Assert.Equal(p.Report.RoundingVolumeBefore, p.Report.RoundingVolumeAfter);
            Assert.Equal(Volume(p.Result), p.Report.RoundingVolumeAfter, 12);
            // two closed boxes: the union 8 + 3 - 1.5, the intersection 1.5, the difference 6.5
            foreach (var c in new[] { new { op = U, v = 9.5 }, new { op = I, v = 1.5 }, new { op = D, v = 6.5 } }) {
                var q = new SolidBooleanPipeline(TwoBoxes(), c.op);
                Assert.True(q.Compute(), q.Error);
                Assert.Equal(c.v, q.Report.RoundingVolumeBefore, 12);
                Assert.Equal(q.Report.RoundingVolumeBefore, q.Report.RoundingVolumeAfter);
            }
            // a reduction changes the result after the rounding: the two numbers stay those of the rounding
            var reduced = new SolidBooleanPipeline(Plates(), U) { TargetTriangleCount = 100 };
            Assert.True(reduced.Compute(), reduced.Error);
            var plain = new SolidBooleanPipeline(Plates(), U);
            Assert.True(plain.Compute(), plain.Error);
            Assert.Equal(plain.Report.RoundingVolumeBefore, reduced.Report.RoundingVolumeBefore);
            Assert.Equal(plain.Report.RoundingVolumeAfter, reduced.Report.RoundingVolumeAfter);
        }

        // ---- the paths without a result -----------------------------------------------------------------------------------------------

        static void AssertNone(SolidBooleanPipeline p)
        {
            Assert.Equal(0.0, p.Report.RoundingVolumeBefore);
            Assert.Equal(0.0, p.Report.RoundingVolumeAfter);
        }

        [Fact]
        public void WhereNoRoundingRepairRan_BothAreZero()
        {
            // an open input, refused
            DMesh3 open = Box(1, 0.5, 0.25, 3, 1.5, 1.75);
            open.RemoveTriangle(0);
            var refused = new SolidBooleanPipeline(new List<DMesh3> { Box(0, 0, 0, 2, 2, 2), open }, U);
            Assert.False(refused.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, refused.Status);
            AssertNone(refused);
            // a stated sheet that the boolean refuses
            var sheet = new SolidBooleanPipeline(new List<DMesh3> { Box(0, 0, 0, 2, 2, 2), open }, D) { InputRoles = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet } };
            Assert.False(sheet.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.SheetRefused, sheet.BooleanStatus);
            AssertNone(sheet);
            // a budget
            var budget = new SolidBooleanPipeline(TwoBoxes(), U) { ConfigureBoolean = op => op.MaxPieces = 1 };
            Assert.False(budget.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.BudgetExceeded, budget.BooleanStatus);
            AssertNone(budget);
            // an invalid setting
            var invalid = new SolidBooleanPipeline(TwoBoxes(), U) { InputGrid = 3 };
            Assert.False(invalid.Compute());
            AssertNone(invalid);
            // the rounding repair switched off: a success without the two numbers
            var off = new SolidBooleanPipeline(TwoBoxes(), U) { ConfigureBoolean = op => op.SafeRounding = false };
            Assert.True(off.Compute(), off.Error);
            Assert.Equal(9.5, Volume(off.Result), 12);
            AssertNone(off);
            // cancelled
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var cancelled = new SolidBooleanPipeline(TwoBoxes(), U) { Cancellation = cts.Token };
            Assert.False(cancelled.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Cancelled, cancelled.Status);
            AssertNone(cancelled);
        }

        [Fact]
        public void AStrictRoundingRefusal_HasTheTwoVolumesOfTheRepairThatRan()
        {
            // the cube of edge 2^-11 beside the box of 1024 (MeshRoundingRepairTestsThinSolids): the repair removes the cube, which the
            // coordinates resolve, so the strict rounding of the pipeline refuses the result. (The cube is 1e-19 of the box: the two
            // numbers are set on this path, and cannot show the cube, which RoundingRemoved lists)
            var p = new SolidBooleanPipeline(new List<DMesh3> { Box(0, 0, 0, 1024, 1024, 1024), Box(1100, 0, 0, 1100 + Pow2(-11), Pow2(-11), Pow2(-11)) }, U);
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, p.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, p.BooleanStatus);
            Assert.Null(p.Result);
            Assert.Single(p.Report.RoundingRemoved);
            double box = 1024.0 * 1024 * 1024;
            Assert.InRange(p.Report.RoundingVolumeBefore, box * (1 - 1e-12), box * (1 + 1e-12));
            Assert.InRange(p.Report.RoundingVolumeAfter, box * (1 - 1e-12), box * (1 + 1e-12));
        }

        [Fact]
        public void RemovedComponents_AreInTheDifference()
        {
            // a box under a generic rotation minus its copy 4 ulps away (MeshRoundingRepairTestsThinSolids): the exact result is slivers
            // below the resolution of the coordinates, and the repair removes all of them. Before: their volume; after: nothing
            DMesh3 a = Box(-0.5, -0.5, -0.5, 0.5, 0.5, 0.5);
            MeshTransforms.Rotate(a, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.81).Normalized, 23));
            double largest = a.Vertices().Max(v => v.MaxAbs);
            double ulp = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(largest) + 1) - largest;
            var b = new DMesh3(a);
            MeshTransforms.Translate(b, 4 * ulp * new Vector3d(0.61, -0.37, 0.7).Normalized);
            var op = new ExactMeshBoolean(a, b, D);
            Assert.True(op.Compute(), op.Error);
            MeshRoundingRepair r = op.Rounding;
            Assert.Equal(0, op.Result.TriangleCount);
            Assert.True(r.RemovedComponents > 0);
            Assert.Equal(0.0, r.VolumeAfter);
            // (the slivers lie on three faces of the box, 4 ulps of 0.8 thick at most: some 1e-16 of volume)
            Assert.InRange(r.VolumeBefore, 1e-17, 1e-14);
            double listed = r.RemovedComponentInfos.Sum(i => i.Volume);
            Assert.InRange(r.VolumeBefore / listed, 0.5, 2);
            // the pipeline: the same numbers beside the list, and an empty result
            var p = new SolidBooleanPipeline(new List<DMesh3> { a, b }, D);
            p.Compute();
            Assert.Equal(r.VolumeBefore, p.Report.RoundingVolumeBefore);
            Assert.Equal(0.0, p.Report.RoundingVolumeAfter);
            Assert.Equal(r.RemovedComponents, p.Report.RoundingRemoved.Count);
        }

        [Fact]
        public void AfterTheGridStep_TheVolumesAreThoseOfTheRunThatIsTheResult()
        {
            // the flush sheet of SolidBooleanPipelineGridStepTests: the first run is refused (no repair ran), the retry on the grid cuts
            DMesh3 box = Box(0.1, 0.2, 0.3, 4.1, 4.3, 4.7);
            double u = Pow2(-50);
            var sheet = new DMesh3();
            int a = sheet.AppendVertex(new Vector3d(0.1 + 3 * u, 0.2 + 3 * u, 2.5)), b = sheet.AppendVertex(new Vector3d(4.1 - 3 * u, 0.2 + 3 * u, 2.5));
            int c = sheet.AppendVertex(new Vector3d(4.1 - 3 * u, 4.3 - 3 * u, 2.5)), d = sheet.AppendVertex(new Vector3d(0.1 + 3 * u, 4.3 - 3 * u, 2.5));
            sheet.AppendTriangle(a, b, c);
            sheet.AppendTriangle(a, c, d);
            var plain = new SolidBooleanPipeline(new List<DMesh3> { box, sheet }, D);
            Assert.False(plain.Compute());
            AssertNone(plain);
            var p = new SolidBooleanPipeline(new List<DMesh3> { box, sheet }, D) { AutomaticSteps = SolidBooleanPipeline.AutomaticStepModes.Grid };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(3, p.Report.Route.Count);
            Assert.Equal(4.0 * 4.1 * 2.2, p.Report.RoundingVolumeBefore, 9);
            Assert.Equal(p.Report.RoundingVolumeBefore, p.Report.RoundingVolumeAfter);
        }

        // ---- the repair itself --------------------------------------------------------------------------------------------------------

        [Fact]
        public void Repair_OfACleanMesh_ReportsItsVolume_AlsoFarFromTheOrigin()
        {
            DMesh3 box = Box(0, 0, 0, 2, 3, 4);
            var r = new MeshRoundingRepair(box);
            Assert.True(r.Apply());
            Assert.Equal(24.0, r.VolumeBefore);
            Assert.Equal(24.0, r.VolumeAfter);
            // far from the origin the sum about the origin loses the volume (products of 1e21 for a volume of 24); about the centre it does not
            // (a tetrahedron with coordinates on 2^-12, moved by powers of two, which is exact: about the centre of its box every product
            // is exact and the volume is the one near the origin, bit for bit; about the origin the products of 1e11 round)
            Func<Vector3d, DMesh3> tetrahedron = (shift) => {
                var t = new DMesh3();
                t.AppendVertex(shift + new Vector3d(1229, 4097, 333) / 4096);
                t.AppendVertex(shift + new Vector3d(9001, 77, 5003) / 4096);
                t.AppendVertex(shift + new Vector3d(123, 8191, 6007) / 4096);
                t.AppendVertex(shift + new Vector3d(4099, 4001, 12289) / 4096);
                t.AppendTriangle(0, 2, 1); t.AppendTriangle(0, 1, 3); t.AppendTriangle(0, 3, 2); t.AppendTriangle(1, 2, 3);
                return t;
            };
            DMesh3 near = tetrahedron(Vector3d.Zero), far = tetrahedron(new Vector3d(Pow2(38), Pow2(39), -Pow2(38)));
            var f0 = new MeshRoundingRepair(near);
            f0.Apply();
            var f = new MeshRoundingRepair(far);
            f.Apply();
            Assert.True(Math.Abs(f0.VolumeBefore) > 0.1, f0.VolumeBefore.ToString());
            Assert.Equal(f0.VolumeBefore, f.VolumeBefore);
            Assert.Equal(f.VolumeBefore, f.VolumeAfter);
            Assert.NotEqual(Volume(near), Volume(far));
            // an inside-out box: the signed volume
            DMesh3 inside = Box(0, 0, 0, 2, 3, 4);
            inside.ReverseOrientation();
            var n = new MeshRoundingRepair(inside);
            n.Apply();
            Assert.Equal(-24.0, n.VolumeBefore);
            Assert.Equal(-24.0, n.VolumeAfter);
            // an open mesh: the sum over its triangles, not a volume (MeshMeasurements.VolumeArea integrates x over the surface: the cube
            // of edge 2 without its face x = 2 lacks that face's 4 * 1 about the centre)
            DMesh3 open = Box(0, 0, 0, 2, 2, 2);
            foreach (int t in open.TriangleIndices().Where(t => open.GetTriNormal(t).x > 0.5).ToList())
                open.RemoveTriangle(t);
            Assert.Equal(10, open.TriangleCount);
            var o = new MeshRoundingRepair(open);
            o.Apply();
            Assert.Equal(4.0, o.VolumeBefore, 12);
            Assert.Equal(o.VolumeBefore, o.VolumeAfter);
            // a vertex that no triangle uses, far away or not finite, does not enter: the centre is that of the triangles' box
            DMesh3 stray = tetrahedron(new Vector3d(Pow2(38), Pow2(39), -Pow2(38)));
            stray.AppendVertex(new Vector3d(1e200, -1e200, 0));
            stray.AppendVertex(new Vector3d(double.NaN, 0, double.PositiveInfinity));
            var st = new MeshRoundingRepair(stray);
            st.Apply();
            Assert.Equal(f0.VolumeBefore, st.VolumeBefore);
            Assert.Equal(f0.VolumeBefore, st.VolumeAfter);
            // no triangle, and coordinates that are not finite
            var empty = new MeshRoundingRepair(new DMesh3());
            empty.Apply();
            Assert.Equal(0.0, empty.VolumeBefore);
            Assert.Equal(0.0, empty.VolumeAfter);
            DMesh3 nan = Box(0, 0, 0, 2, 3, 4);
            nan.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            var bad = new MeshRoundingRepair(nan);
            bad.Apply();
            Assert.True(double.IsNaN(bad.VolumeBefore) && double.IsNaN(bad.VolumeAfter));
        }

        // ---- reuse --------------------------------------------------------------------------------------------------------------------

        [Fact]
        public void Reuse_TheVolumesBelongToTheLastCall()
        {
            var p = new SolidBooleanPipeline(Plates(), U);
            Assert.True(p.Compute(), p.Error);
            double before = p.Report.RoundingVolumeBefore, after = p.Report.RoundingVolumeAfter;
            Assert.NotEqual(before, after);
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(before, p.Report.RoundingVolumeBefore);
            Assert.Equal(after, p.Report.RoundingVolumeAfter);
            // a setting that fails the next run: the numbers of the first are gone
            p.InputGrid = 3;
            Assert.False(p.Compute());
            AssertNone(p);
            p.InputGrid = 0;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(before, p.Report.RoundingVolumeBefore);
            Assert.Equal(after, p.Report.RoundingVolumeAfter);
            // the repair applied a second time to its own result: it starts from the volume it ended with, and changes nothing
            var op = new ExactMeshBoolean(Plates(), U);
            Assert.True(op.Compute());
            MeshRoundingRepair r = op.Rounding;
            double first = r.VolumeAfter;
            Assert.NotEqual(r.VolumeBefore, first);
            r.Apply();
            Assert.Equal(first, r.VolumeBefore, 15);
            Assert.Equal(r.VolumeBefore, r.VolumeAfter);
        }

        // ---- invariance ---------------------------------------------------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Volumes_AreInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            var reference = new SolidBooleanPipeline(Plates(), U);
            Assert.True(reference.Compute(), reference.Error);
            double cube = s.Scale * s.Scale * s.Scale;

            var p = new SolidBooleanPipeline(Plates().Select(m => Invariance.Transformed(m, s)).ToList(), U);
            Assert.True(p.Compute(), s.Name + ": " + p.Error);
            if (s.IsExact) {
                // a power-of-two scale: both volumes scale by its cube, bit for bit
                Assert.Equal(reference.Report.RoundingVolumeBefore * cube, p.Report.RoundingVolumeBefore);
                Assert.Equal(reference.Report.RoundingVolumeAfter * cube, p.Report.RoundingVolumeAfter);
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(p.Result, s)));
                return;
            }
            // another frame rounds the near misses to other values (and far from the origin they are a few thousand ulps): the volume
            // before the repair is the union's up to that, about the centre of the box, and the two numbers are those of the result
            Assert.InRange(p.Report.RoundingVolumeBefore / cube, 5.178e-5 * (1 - 2e-3), 5.178e-5 * (1 + 2e-3));
            Assert.Equal(Volume(Invariance.MappedBack(p.Result, s)), p.Report.RoundingVolumeAfter / cube, 9);
            Assert.True(Math.Abs(p.Report.RoundingVolumeAfter - p.Report.RoundingVolumeBefore) / cube < 0.03 * 5.178e-5, s.Name);
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
