using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1555 (owner decision 2026-11-25): a closed component of a closed input that is inside-out and thinner than the rounding
    /// bound is dropped before the boolean instead of refusing the run with InsideOutInput (or, across a face of the solid,
    /// carving a sliver out of it). The fixture: a unit box plus a closed lens of 6 triangles (a quad of two facing up, a fan of
    /// four around a fifth vertex facing down, that vertex lifted above the quad: negative volume). Public API only, so the file
    /// compiles against the library before the change (the report entry is in SolidBooleanPipelineThinInsideOutReportTests).
    /// </summary>
    public class SolidBooleanPipelineThinInsideOutTests
    {
        const ExactMeshBoolean.Operations I = ExactMeshBoolean.Operations.Intersection;
        // the lift of the lens: 2^-20, thickness 2 |V| / A = lift / 3 = 3.2e-7, some 170 times below the bound
        // (1e-5 times the diagonal of the largest component, the second box: 5.5e-5)
        internal const double ThinLift = 1.0 / (1 << 20);
        // 2^-12: thickness 8.1e-5, above that bound: not thin, refused as before
        internal const double NotThinLift = 1.0 / (1 << 12);
        // the lens's quad in x: outside the box, and across its face x = 1
        internal const double Outside = 2, Across = 0.75;

        internal static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();
        }

        // the lens over the quad [x0, x0 + 0.5] x [0.25, 0.75] at height z, apex lifted by lift: inside-out for lift > 0
        internal static void AddLens(DMesh3 m, double x0, double z, double lift)
        {
            double x1 = x0 + 0.5, y0 = 0.25, y1 = 0.75;
            int a = m.AppendVertex(new Vector3d(x0, y0, z)), b = m.AppendVertex(new Vector3d(x1, y0, z));
            int c = m.AppendVertex(new Vector3d(x1, y1, z)), d = m.AppendVertex(new Vector3d(x0, y1, z));
            int e = m.AppendVertex(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, z + lift));
            Assert.True(m.AppendTriangle(a, b, c) >= 0 && m.AppendTriangle(a, c, d) >= 0);      // up
            Assert.True(m.AppendTriangle(e, b, a) >= 0 && m.AppendTriangle(e, c, b) >= 0
                && m.AppendTriangle(e, d, c) >= 0 && m.AppendTriangle(e, a, d) >= 0);            // down
        }

        internal static DMesh3 BoxWithLens(double x0, double lift)
        {
            DMesh3 m = Box(0, 0, 0, 1, 1, 1);
            AddLens(m, x0, 0.5, lift);
            return m;
        }

        // the other operand: holds the half x >= 0.5 of the box and both placements of the lens; the right result is that half
        internal static DMesh3 Other() { return Box(0.5, -1, -1, 4, 2, 2); }

        internal static List<DMesh3> Inputs(double x0, double lift) { return new List<DMesh3> { BoxWithLens(x0, lift), Other() }; }

        internal static void AssertSolid(SolidBooleanPipeline p)
        {
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            DMesh3 r = p.Result;
            Assert.NotNull(r);
            Assert.True(Invariance.AllFinite(r));
            Assert.True(r.IsClosed());
            Assert.True(r.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r));
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
        }

        [Fact]
        public void TheFixture_TheLensIsClosedAndInsideOut()
        {
            var lens = new DMesh3();
            AddLens(lens, Outside, 0.5, ThinLift);
            Assert.True(lens.IsClosed());
            Assert.True(Invariance.Volume(lens) < 0);
            Assert.True(Math.Abs(Invariance.Volume(lens) + 0.25 * ThinLift / 3) <= 1e-9 * 0.25 * ThinLift / 3);
            Assert.True(ExactMeshBoolean.IsWindingClosed(BoxWithLens(Across, ThinLift)));
        }

        [Theory]
        [InlineData(Outside)]
        [InlineData(Across)]
        public void ThinInsideOutLens_Default_IsDropped_TheResultIsTheHalfBox(double x0)
        {
            // before: outside the box InsideOutInput (1 cell); across its face a Success with a sliver of the lens cut out of the box
            var p = new SolidBooleanPipeline(Inputs(x0, ThinLift), I);
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.Equal(0.5, Invariance.Volume(p.Result), 14);
            Assert.Equal(12, p.Result.TriangleCount);
            Assert.Equal(0, p.Report.InsideOutCells);
            Assert.True(p.Result.GetBounds().Max.x <= 1 && p.Result.GetBounds().Min.x >= 0.5);
        }

        [Theory]
        [InlineData(Outside)]
        [InlineData(Across)]
        public void ThinInsideOutLens_AreaMajority_IsDropped(double x0)
        {
            var p = new SolidBooleanPipeline(Inputs(x0, ThinLift), I) { Orientation = ExactMeshBoolean.OrientationModes.AreaMajority };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.Equal(0.5, Invariance.Volume(p.Result), 14);
        }

        [Fact]
        public void ThinInsideOutLens_WithAllowInsideOutInputs_IsUsedAsItIs()
        {
            // the caller takes inside-out inputs as they are: nothing is dropped, the lens is an empty solid in the Intersection
            var p = new SolidBooleanPipeline(Inputs(Outside, ThinLift), I) { AllowInsideOutInputs = true };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(1, p.Report.InsideOutCells);
        }

        [Fact]
        public void ThinInsideOutLens_ByNesting_IsOrientedNotDropped()
        {
            // ByNesting derives the solid of an embedded input: the lens outside the box is a thin solid (positive) and stays
            var p = new SolidBooleanPipeline(Inputs(Outside, ThinLift), I) { Orientation = ExactMeshBoolean.OrientationModes.ByNesting };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(0.5 + 0.25 * ThinLift / 3, Invariance.Volume(p.Result), 12);
        }

        [Theory]
        [InlineData(NotThinLift)]
        [InlineData(0.25)]
        public void InsideOutLens_NotThin_IsStillRefused(double lift)
        {
            var p = new SolidBooleanPipeline(Inputs(Outside, lift), I);
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, p.Status);
            Assert.Equal(1, p.Report.InsideOutCells);
            Assert.Null(p.Result);
        }

        [Fact]
        public void ThinCavity_IsNotInsideOut_AndStays()
        {
            // the same lens inside the box (the quad x in [0.375, 0.875]) is a cavity, a crack of the solid: not inside-out, kept
            var p = new SolidBooleanPipeline(new List<DMesh3> { BoxWithLens(0.375, ThinLift), Other() }, I);
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.True(p.Result.IsClosed());
            Assert.Equal(0, p.Report.InsideOutCells);
            // the part of the cavity in x >= 0.5 is cut out of the half box
            Assert.True(Invariance.Volume(p.Result) < 0.5);
            Assert.True(p.Result.TriangleCount > 12);
        }

        [Fact]
        public void ThinCavity_InABoxClosedOnlyByPosition_Stays()
        {
            // the box with its own vertices per face (six open components, winding-closed together) and the lens inside it: the
            // cavity is decided by the winding number of the rest of the input, not by one shell closed by its edges
            DMesh3 m = new TrivialBox3Generator() { NoSharedVertices = true, Box = new Box3d(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One)) }.Generate().MakeDMesh();
            AddLens(m, 0.375, 0.5, ThinLift);
            Assert.True(ExactMeshBoolean.IsWindingClosed(m));
            Assert.False(m.IsClosed());
            var p = new SolidBooleanPipeline(new List<DMesh3> { m, Other() }, I);
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.True(p.Result.IsClosed());
            Assert.True(Invariance.Volume(p.Result) < 0.5);
            Assert.True(p.Result.TriangleCount > 12);
        }

        [Fact]
        public void ThinInsideOutLens_NextToABoxClosedOnlyByPosition_IsDropped()
        {
            DMesh3 m = new TrivialBox3Generator() { NoSharedVertices = true, Box = new Box3d(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One)) }.Generate().MakeDMesh();
            AddLens(m, Across, 0.5, ThinLift);
            var p = new SolidBooleanPipeline(new List<DMesh3> { m, Other() }, I);
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.True(p.Result.IsClosed());
            Assert.Equal(0.5, Invariance.Volume(p.Result), 14);
        }

        [Fact]
        public void AnInputOfOnlyAThinInsideOutLens_IsNotEmptied_AndStillRefused()
        {
            var lens = new DMesh3();
            AddLens(lens, 0.75, 0.5, ThinLift);
            var p = new SolidBooleanPipeline(new List<DMesh3> { lens, Other() }, I);
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, p.Status);
        }

        [Theory]
        [InlineData(Outside)]
        [InlineData(Across)]
        public void ThinInsideOutLens_Compute_Twice_SameResult(double x0)
        {
            var p = new SolidBooleanPipeline(Inputs(x0, ThinLift), I);
            Assert.True(p.Compute(), p.Error);
            DMesh3 first = p.Result;
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.Equal(first.TriangleCount, p.Result.TriangleCount);
            Assert.Equal(Invariance.Volume(first), Invariance.Volume(p.Result));
        }

        [Theory]
        [InlineData(Outside)]
        [InlineData(Across)]
        public void ThinInsideOutLens_IsDropped_UnderEverySimilarity(double x0)
        {
            var plain = new SolidBooleanPipeline(Inputs(x0, ThinLift), I);
            Assert.True(plain.Compute(), plain.Error);
            double v0 = Invariance.Volume(plain.Result);
            foreach (Similarity s in Similarity.All()) {
                var inputs = Inputs(x0, ThinLift).Select(m => Invariance.Transformed(m, s)).ToList();
                var p = new SolidBooleanPipeline(inputs, I);
                Assert.True(p.Compute(), s.Name + ": " + p.Error);
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
                Assert.True(p.Result.IsClosed(), s.Name);
                DMesh3 back = Invariance.MappedBack(p.Result, s);
                if (s.IsExact) {
                    // a scale by 2^k: the same result, scaled, bit for bit
                    Assert.Equal("", Invariance.GeometricDifference(back, plain.Result));
                    Assert.Equal(v0 * s.Scale * s.Scale * s.Scale, Invariance.Volume(p.Result));
                    Assert.Equal(v0, Invariance.Volume(back));
                } else {
                    AssertSolid(p);
                    Assert.True(Math.Abs(Invariance.Volume(back) - 0.5) < 1e-6, s.Name + ": " + Invariance.Volume(back));
                }
            }
        }
    }
}
