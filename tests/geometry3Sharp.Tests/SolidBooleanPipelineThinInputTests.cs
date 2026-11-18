using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline.ThinInputs: with MinThickness every input is measured by 2 |V| / A before the boolean; thin ones
    /// are listed (Keep), count as empty solids (TreatAsEmpty), or fail the run (Refuse).
    /// </summary>
    public class SolidBooleanPipelineThinInputTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union, I = ExactMeshBoolean.Operations.Intersection, D = ExactMeshBoolean.Operations.Difference;
        const SolidBooleanPipeline.ThinInputModes Keep = SolidBooleanPipeline.ThinInputModes.Keep, Empty = SolidBooleanPipeline.ThinInputModes.TreatAsEmpty, Refuse = SolidBooleanPipeline.ThinInputModes.Refuse;

        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(min, max)) }.Generate().MakeDMesh();
        }

        static double Volume(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }

        // a unit box, and a slab 2 x 2 x 0.01 that cuts through it (2 V / A = 0.08 / 8.08)
        static DMesh3 UnitBox() { return Box(Vector3d.Zero, Vector3d.One); }
        static DMesh3 Slab() { return Box(new Vector3d(-0.5, -0.5, 0.5), new Vector3d(1.5, 1.5, 0.51)); }
        const double SlabThickness = 0.08 / 8.08;

        static SolidBooleanPipeline Run(ExactMeshBoolean.Operations op, SolidBooleanPipeline.ThinInputModes mode, params DMesh3[] inputs)
        {
            var p = new SolidBooleanPipeline(inputs, op) { MinThickness = 0.05, ThinInputs = mode };
            p.Compute();
            return p;
        }

        static void AssertEmpty(SolidBooleanPipeline p)
        {
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.Equal(0, p.Result.TriangleCount);
            Assert.Equal("empty", p.Report.NotCleanReason);
            Assert.Equal(SolidBooleanStopReason.EmptyResult, p.Report.StopReason);
            Assert.Contains("ThinInputs", p.Report.StopDetail);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.NotComputed, p.BooleanStatus);      // (the boolean did not run)
        }

        [Fact]
        public void Keep_ListsTheThinInput_AndUsesIt()
        {
            var p = Run(D, Keep, UnitBox(), Slab());
            Assert.True(p.Status == SolidBooleanPipeline.PipelineStatus.Success, p.Error);
            SolidBooleanThinInput t = Assert.Single(p.Report.ThinInputs);
            Assert.Equal(1, t.Index);
            Assert.True(t.Closed);
            Assert.Equal(SlabThickness, t.Thickness, 12);
            Assert.Equal(0.04, t.Volume, 12);
            Assert.Equal(8.08, t.Area, 12);
            Assert.Equal(1 - 0.01, Volume(p.Result), 12);       // (the slab was subtracted)
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
            // exactly at the threshold an input is not thin
            var at = new SolidBooleanPipeline(new[] { UnitBox(), Slab() }, D) { MinThickness = t.Thickness, ThinInputs = Refuse };
            Assert.True(at.Compute(), at.Error);
            Assert.Empty(at.Report.ThinInputs);
        }

        [Fact]
        public void TreatAsEmpty_ATool_IsLeftOut()
        {
            var d = Run(D, Empty, UnitBox(), Slab());
            Assert.True(d.Status == SolidBooleanPipeline.PipelineStatus.Success, d.Error);
            Assert.Single(d.Report.ThinInputs);
            Assert.Equal(1.0, Volume(d.Result), 12);
            Assert.Equal(12, d.Result.TriangleCount);
            var u = Run(U, Empty, UnitBox(), Slab());
            Assert.Equal(1.0, Volume(u.Result), 12);
            Assert.Equal(12, u.Result.TriangleCount);
            Assert.True(u.Report.Clean, u.Report.NotCleanReason);
        }

        [Fact]
        public void TreatAsEmpty_TheFirstOperandOfADifference_AnIntersection_OrOnlyThinInputs_GiveAnEmptyResult()
        {
            AssertEmpty(Run(D, Empty, Slab(), UnitBox()));
            AssertEmpty(Run(I, Empty, UnitBox(), Slab()));
            AssertEmpty(Run(I, Empty, Slab(), UnitBox()));
            AssertEmpty(Run(U, Empty, Slab(), Box(new Vector3d(5, 0, 0), new Vector3d(7, 2, 0.01))));
        }

        [Fact]
        public void Refuse_FailsWithThinInput()
        {
            var p = Run(U, Refuse, UnitBox(), Slab());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.ThinInput, p.Status);
            Assert.Null(p.Result);
            Assert.Contains("input 1", p.Error);
            Assert.Single(p.Report.ThinInputs);
            // nothing thin: the run is the ordinary one
            var q = Run(U, Refuse, UnitBox(), Box(new Vector3d(0.5, 0, 0), new Vector3d(1.5, 1, 1)));
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, q.Status);
            Assert.Empty(q.Report.ThinInputs);
            Assert.Equal(1.5, Volume(q.Result), 12);
        }

        [Fact]
        public void WithoutMinThickness_NothingIsMeasured()
        {
            var p = new SolidBooleanPipeline(UnitBox(), Slab(), U) { ThinInputs = Refuse };
            Assert.True(p.Compute(), p.Error);
            Assert.Empty(p.Report.ThinInputs);
        }

        [Fact]
        public void AnOpenThinInput_IsMeasured_AndTheIndicesStayRightWhenAnotherInputIsRepaired()
        {
            // input 0: a thin slab with one triangle missing (open); input 1: a box with one triangle missing (open, thick);
            // input 2: a box. With TreatAsEmpty the slab goes, and the repair of input 1 is reported under its own index
            DMesh3 slab = Box(new Vector3d(5, 0, 0), new Vector3d(7, 2, 0.01));
            slab.RemoveTriangle(slab.TriangleIndices().First());
            DMesh3 holed = Box(Vector3d.Zero, Vector3d.One);
            holed.RemoveTriangle(holed.TriangleIndices().First());
            DMesh3 box = Box(new Vector3d(0.5, 0, 0), new Vector3d(1.5, 1, 1));
            var p = Run(U, Empty, slab, holed, box);
            Assert.True(p.Status == SolidBooleanPipeline.PipelineStatus.Success, p.Error);
            SolidBooleanThinInput t = Assert.Single(p.Report.ThinInputs);
            Assert.Equal(0, t.Index);
            Assert.False(t.Closed);
            SolidBooleanInputRepair r = Assert.Single(p.Report.RepairedInputs);
            Assert.Equal(1, r.Index);
            Assert.Equal(1.5, Volume(p.Result), 9);
            Assert.True(p.Result.IsClosed());
            Assert.True(p.Result.Vertices().All(q => q.x <= 1.5));
        }

        [Fact]
        public void FarFromTheOrigin_TheThicknessIsMeasuredExactlyEnough()
        {
            var at = new Vector3d(2527500, 5634200, 100);
            var p = new SolidBooleanPipeline(Box(at, at + Vector3d.One), Box(at + new Vector3d(-0.5, -0.5, 0.5), at + new Vector3d(1.5, 1.5, 0.501)), U) { MinThickness = 2e-3 };
            Assert.True(p.Compute(), p.Error);
            SolidBooleanThinInput t = Assert.Single(p.Report.ThinInputs);
            double want = 2 * 4e-3 / (8 + 8e-3);
            Assert.True(Math.Abs(t.Thickness - want) <= 1e-6 * want, t.Thickness.ToString("R"));
        }

        [Fact]
        public void InvalidMode_IsRefused()
        {
            var p = new SolidBooleanPipeline(UnitBox(), Slab(), U) { MinThickness = 0.05, ThinInputs = (SolidBooleanPipeline.ThinInputModes)7 };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
            Assert.Contains("ThinInputs", p.Error);
        }

        [Fact]
        public void Compute_Twice_AndWithAChangedMode()
        {
            var p = new SolidBooleanPipeline(new[] { UnitBox(), Slab() }, D) { MinThickness = 0.05, ThinInputs = Empty };
            Assert.True(p.Compute(), p.Error);
            DMesh3 first = p.Result;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.Difference(first, p.Result));
            Assert.Single(p.Report.ThinInputs);
            p.ThinInputs = Keep;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(0.99, Volume(p.Result), 12);
            p.ThinInputs = Refuse;
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.ThinInput, p.Status);
            p.ThinInputs = Empty;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.Difference(first, p.Result));
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void ThinInputs_AreInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            var p = new SolidBooleanPipeline(Invariance.Transformed(UnitBox(), s), Invariance.Transformed(Slab(), s), D) { MinThickness = 0.05 * s.Scale, ThinInputs = Empty };
            Assert.True(p.Compute(), s.Name + ": " + p.Error);
            SolidBooleanThinInput t = Assert.Single(p.Report.ThinInputs);
            Assert.Equal(1, t.Index);
            // (the transformed input itself is rounded: about 1e-9 at UTM coordinates against a slab 1e-5 thick at scale 1e-3)
            double rel = s.IsExact ? 1e-12 : 1e-3;
            Assert.True(Math.Abs(t.Thickness - SlabThickness * s.Scale) <= rel * SlabThickness * s.Scale, s.Name + ": " + t.Thickness.ToString("R"));
            Assert.Equal(12, p.Result.TriangleCount);
            Assert.True(Math.Abs(Volume(p.Result) - Math.Pow(s.Scale, 3)) <= rel * Math.Pow(s.Scale, 3), s.Name);
            if (s.IsExact) {
                var reference = new SolidBooleanPipeline(UnitBox(), Slab(), D) { MinThickness = 0.05, ThinInputs = Empty };
                Assert.True(reference.Compute());
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(p.Result, s)));
                Assert.Equal(reference.Report.ThinInputs[0].Thickness * s.Scale, t.Thickness);
            }
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
