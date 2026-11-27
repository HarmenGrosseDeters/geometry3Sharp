using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline with InputRepair = AutoRepairThenRobust used the mesh of MeshRobustRepair whenever it was winding closed, also where
    /// Apply() was false (ThinShellWrap: a wrap that is a thin shell around an input that has a solid; NestingAmbiguous; a lost part). The run then
    /// cut with a wrong solid and reported Success. Now a refused result is not used: Status OpenInput, Error names the ResultReason.
    /// MeshAutoRepair closes nearly every open surface before MeshRobustRepair is reached, so these tests skip it (SkipAutoRepairForTests).
    /// </summary>
    public class SolidBooleanPipelineRobustRefusalTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union;

        static DMesh3 Box(Vector3d c, double h)
        {
            var g = new TrivialBox3Generator { Box = new Box3d(c, new Vector3d(h, h, h)), NoSharedVertices = false };
            g.Generate();
            return g.MakeDMesh();
        }

        // an open bowl whose wrap (default settings of the pipeline's repair) is a thin shell: ThinShellWrap
        static DMesh3 ShellBowl() { return RobustRepairHollowWrapTests.Bowl(8, -0.2); }
        // an open bowl that the repair solidifies: a valid repair
        static DMesh3 GoodBowl() { return RobustRepairHollowWrapTests.Bowl(8, 0.1); }

        // the settings of RobustRepairHollowWrapTests (a coarse grid and wrap: a test takes a second, not ten)
        static void Cheap(MeshRobustRepair r) { r.SolidifyResolution = 80; r.SimplifyAlphaWrap = false; r.WrapRelativeAlpha = 50; r.WrapRelativeOffset = 500; }

        static SolidBooleanPipeline Pipeline(DMesh3 open, DMesh3 other = null)
        {
            return new SolidBooleanPipeline(open, other ?? Box(new Vector3d(0.2, 0, -0.3), 0.3), U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust, SkipAutoRepairForTests = true, RobustRepairSetupForTests = Cheap };
        }

        static bool RobustApply(DMesh3 m, out string reason, out bool closed)
        {
            var r = new MeshRobustRepair(new DMesh3(m));
            Cheap(r);
            bool ok = r.Apply();
            reason = r.ResultReason;
            closed = ExactMeshBoolean.IsWindingClosed(r.Mesh) && r.Mesh.TriangleCount > 0;
            return ok;
        }

        [Fact]
        public void ThinShellWrap_IsNotUsed_TheRunIsRefusedWithTheReason()
        {
            DMesh3 bowl = ShellBowl();
            // setup: the repair refuses with ThinShellWrap, and its mesh is closed (the old code took it)
            Assert.False(RobustApply(bowl, out string reason, out bool closed));
            Assert.Equal(MeshRobustRepair.ThinShellWrap, reason);
            Assert.True(closed, "setup: the refused mesh is closed");
            SolidBooleanPipeline p = Pipeline(bowl);
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.Null(p.Result);
            Assert.Empty(p.Report.RepairedInputs);
            Assert.Contains("input 0 is not closed and no allowed repair closed it", p.Error);
            Assert.Contains("MeshRobustRepair did not repair it", p.Error);
            Assert.Contains(MeshRobustRepair.ThinShellWrap, p.Error);
            var step = p.Report.Route.Single(s => s.Name == "Refused");
            Assert.Equal(0, step.Input);
            Assert.Contains(MeshRobustRepair.ThinShellWrap, step.Reason);
        }

        [Fact]
        public void NestingAmbiguous_IsNotUsed_TheRunIsRefusedWithTheReason()
        {
            // an outer cube around an inner cube oriented outward (read as a cavity: the solid is not the union), plus a separate open box so that the
            // input needs a repair
            DMesh3 outer = Box(new Vector3d(1, 1, 1), 1.0);
            MeshEditor.Append(outer, Box(new Vector3d(1, 1, 1), 0.4));
            DMesh3 open = Box(new Vector3d(5, 1, 1), 0.5);
            open.RemoveTriangle(0);
            MeshEditor.Append(outer, open);
            Assert.False(RobustApply(outer, out string reason, out bool closed));
            Assert.StartsWith(MeshRobustRepair.NestingAmbiguous, reason);
            SolidBooleanPipeline p = Pipeline(outer, Box(new Vector3d(-3, 1, 1), 0.5));
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.Contains(MeshRobustRepair.NestingAmbiguous, p.Error);
        }

        [Fact]
        public void ScaleDisparity_IsRefused_AlsoThoughTheMeshIsCleanButForTheDegenerateAreaDiagnostic()
        {
            // components far apart compared with their size: Apply() is false, the resolved mesh (closed) is in Mesh, and the pipeline does not use it
            DMesh3 scene = RobustRepairFarPartTests.Scene(1e8, 2);
            scene.RemoveTriangle(scene.MaxTriangleID - 1);       // (a triangle of the far tetrahedron: open, so that the input needs a repair)
            var direct = new MeshRobustRepair(new DMesh3(scene)) { PassThroughCleanParts = false };
            Assert.False(direct.Apply());
            Assert.Equal(MeshRobustRepair.ScaleDisparity, direct.ResultReason);
            Assert.True(ExactMeshBoolean.IsWindingClosed(direct.Mesh), "setup: the refused mesh is closed");
            var p = new SolidBooleanPipeline(scene, Box(new Vector3d(0.2, 0, -0.3), 0.3), U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust, SkipAutoRepairForTests = true,
                RobustRepairSetupForTests = r => r.PassThroughCleanParts = false };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.Contains(MeshRobustRepair.ScaleDisparity, p.Error);
        }

        [Fact]
        public void ARepairThatSucceeds_IsUsedAsBefore_SameAsTheRepairedMeshGivenClosed()
        {
            DMesh3 bowl = GoodBowl();
            var robust = new MeshRobustRepair(new DMesh3(bowl));
            Cheap(robust);
            Assert.True(robust.Apply());
            Assert.Equal("", robust.ResultReason);
            SolidBooleanPipeline p = Pipeline(bowl);
            Assert.True(p.Compute(), p.Error);
            SolidBooleanInputRepair rep = Assert.Single(p.Report.RepairedInputs);
            Assert.Equal("MeshRobustRepair", rep.Method);
            Assert.Equal(robust.Mesh.TriangleCount, rep.TrianglesAfter);
            Assert.True(p.Result.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(p.Result));
            // the same run on the repaired mesh as a closed input (no repair): the same result, bit for bit
            var closed = new SolidBooleanPipeline(new DMesh3(robust.Mesh), Box(new Vector3d(0.2, 0, -0.3), 0.3), U);
            Assert.True(closed.Compute(), closed.Error);
            Assert.Equal(closed.Result.TriangleCount, p.Result.TriangleCount);
            Assert.Equal(closed.Result.VertexCount, p.Result.VertexCount);
            foreach (int v in closed.Result.VertexIndices())
                Assert.Equal(closed.Result.GetVertex(v), p.Result.GetVertex(v));
        }

        public static IEnumerable<object[]> Names() { return Similarity.NamesExceptExtremeFrame(); }

        // (without RotatedUtmMillimetre: its design is near the frame's MinFeatureSize, three builds by design, 2-3 times the cost, and it is not
        // an additional case for the decision: the repair's own invariance tests leave it out too)
        // The repair runs the same stages bit for bit under the scales (2^k and the decimal ones), so these refuse as the identity does. Under the
        // translation and the rotation (utm, rotated) the grid of the solidify differs and the repair takes it before the wrap: the run succeeds there. That
        // is pinned so that a later change of either side is visible.
        static readonly HashSet<string> Succeeding = new HashSet<string> { "utm", "rotated" };

        [Theory, MemberData(nameof(Names))]
        public void TheRefusal_IsTheSameUnderEverySimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            SolidBooleanPipeline p = Pipeline(Invariance.Transformed(ShellBowl(), s), Invariance.Transformed(Box(new Vector3d(0.2, 0, -0.3), 0.3), s));
            bool ran = p.Compute();
            Assert.Equal(Succeeding.Contains(name), ran);
            if (s.IsExact)
                Assert.False(ran, name);
            if (ran == false) {
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
                Assert.Contains(MeshRobustRepair.ThinShellWrap, p.Error);
            } else {
                Assert.Single(p.Report.RepairedInputs);
                Assert.True(p.Result.IsClosed(), name);
                Assert.True(p.Result.CheckValidity(false, FailMode.ReturnOnly), name);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(p.Result));
            }
        }

        [Fact]
        public void ACancelDuringTheRobustRepair_GivesStatusCancelled_NotARefusal()
        {
            // the token is cancelled when the repair is set up (the pipeline checks it before the repair, so only the wiring into the repair
            // can see it): the repair stops, and the run is Cancelled, not OpenInput with a reason
            using (var cts = new System.Threading.CancellationTokenSource()) {
                var p = new SolidBooleanPipeline(ShellBowl(), Box(new Vector3d(0.2, 0, -0.3), 0.3), U) {
                    InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust, SkipAutoRepairForTests = true,
                    Cancellation = cts.Token, RobustRepairSetupForTests = r => { Cheap(r); cts.Cancel(); } };
                Assert.False(p.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.Cancelled, p.Status);
                Assert.Null(p.Result);
            }
        }

        [Fact]
        public void Reuse_ComputeTwice_RefusesTwiceTheSame()
        {
            SolidBooleanPipeline p = Pipeline(ShellBowl());
            Assert.False(p.Compute());
            string error = p.Error;
            Assert.False(p.Compute());
            Assert.Equal(error, p.Error);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.Single(p.Report.Route, s => s.Name == "Refused");
        }
    }
}
