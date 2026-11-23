using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Roles = System.Collections.Generic.List<g3.ExactMeshBoolean.InputRole>;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The sheet role through SolidBooleanPipeline (InputRoles, UncutSide): an open input with the role Sheet is the tool of a
    /// Difference as given, never repaired or counted as an open input; the boolean's refusals come out with their reason.
    /// </summary>
    public class SolidBooleanPipelineSheetTests
    {
        const ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference;

        static DMesh3 Cube(double size) => new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(size, size, size))) }.Generate().MakeDMesh();

        static DMesh3 Sheet(double z, double x0, double x1, bool up = true)
        {
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(x0, x0, z)), b = m.AppendVertex(new Vector3d(x1, x0, z)), c = m.AppendVertex(new Vector3d(x1, x1, z)), d = m.AppendVertex(new Vector3d(x0, x1, z));
            if (up) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); } else { m.AppendTriangle(a, c, b); m.AppendTriangle(a, d, c); }
            return m;
        }

        static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        static Roles SheetSecond() => new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet };

        [Theory]
        [InlineData(true, 32.0)]
        [InlineData(false, 32.0)]
        public void ACubeMinusASheet_IsHalfTheCube_WithTheDefaultSettings(bool up, double expected)
        {
            // #1211: [0,4]^3 minus the square sheet z = 2. As a sheet it is not an open input: no repair, no refusal as OpenInput
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5, up) }, D) { InputRoles = SheetSecond() };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Equal(expected, Volume(p.Result), 9);
            Assert.Empty(p.Report.RepairedInputs);
            Assert.Equal("", p.Report.NotCleanReason);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.None, p.SheetReason);
            Assert.False(p.Report.InputClosed[1]);
            // (the side that stays: z in [2, 4] for normals up)
            double zMin = p.Result.Vertices().Min(v => v.z), zMax = p.Result.Vertices().Max(v => v.z);
            Assert.Equal(up ? 2.0 : 0.0, zMin, 12);
            Assert.Equal(up ? 4.0 : 2.0, zMax, 12);
        }

        [Fact]
        public void TheSheetIsNotRepaired_EvenWithAutoRepair()
        {
            // the hazard of #1211: AutoRepair closes the sheet to a surface that encloses nothing and the cube comes back unchanged (volume 64)
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D) {
                InputRoles = SheetSecond(), InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust
            };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(32.0, Volume(p.Result), 9);
            Assert.Empty(p.Report.RepairedInputs);
        }

        [Fact]
        public void WithoutTheRole_TheOpenSheetIsStillRefusedAsOpen()
        {
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D);
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
        }

        [Fact]
        public void Refusals_ComeWithTheirReasons()
        {
            // a sheet in a union and an intersection, as the first input; a rim inside; an uncut workpiece; more roles than inputs
            foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection }) {
                var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, op) { InputRoles = SheetSecond() };
                Assert.False(p.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, p.Status);
                Assert.Equal(ExactMeshBoolean.BooleanStatus.SheetRefused, p.BooleanStatus);
                Assert.Equal(op == ExactMeshBoolean.Operations.Union ? ExactMeshBoolean.SheetRefusal.InUnion : ExactMeshBoolean.SheetRefusal.InIntersection, p.SheetReason);
                Assert.Null(p.Result);
            }
            var first = new SolidBooleanPipeline(new List<DMesh3> { Sheet(2, -1, 5), Cube(4) }, D) { InputRoles = new Roles { ExactMeshBoolean.InputRole.Sheet } };
            Assert.False(first.Compute());
            Assert.Equal(ExactMeshBoolean.SheetRefusal.AsFirstOperand, first.SheetReason);
            var rim = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, 1, 3) }, D) { InputRoles = SheetSecond() };
            Assert.False(rim.Compute());
            Assert.Equal(ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece, rim.SheetReason);
            Assert.Contains("free edge", rim.Error);
            var uncut = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(10, -1, 5) }, D) { InputRoles = SheetSecond() };
            Assert.False(uncut.Compute());
            Assert.Equal(ExactMeshBoolean.SheetRefusal.UncutComponents, uncut.SheetReason);
            // (the same with the side stated: the cube is behind the sheet at z = 10 that looks up... the cube is below it, so behind it: inside the tool)
            uncut = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(10, -1, 5) }, D) { InputRoles = SheetSecond(), UncutSide = ExactMeshBoolean.SheetUncutSides.Outside };
            Assert.True(uncut.Compute(), uncut.Error);
            Assert.Equal(64.0, Volume(uncut.Result), 9);
            var many = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D) { InputRoles = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Sheet } };
            Assert.False(many.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, many.Status);
            var badSide = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D) { InputRoles = SheetSecond(), UncutSide = (ExactMeshBoolean.SheetUncutSides)9 };
            Assert.False(badSide.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, badSide.Status);
        }

        [Fact]
        public void SheetAndAClosedSolidTool_AndMinThicknessDoesNotCountTheSheetAsThin()
        {
            // the cube [0,4]^3 minus the sheet z = 2 (the upper half stays) minus a closed cube [2,6] x [2,6] x [3,5] on the upper half
            DMesh3 tool = Cube(4);
            MeshTransforms.Translate(tool, new Vector3d(2, 2, 3));
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5), tool }, D) {
                InputRoles = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Solid },
                MinThickness = 1e-3, ThinInputs = SolidBooleanPipeline.ThinInputModes.Refuse
            };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            // (the cube's [2,4]^2 x [3,4] part goes: 2 * 2 * 1)
            Assert.Equal(32.0 - 4.0, Volume(p.Result), 9);
            Assert.Empty(p.Report.ThinInputs);
        }

        [Fact]
        public void InputGrid_SnapsTheSheetToo_AndTheCutIsStillExact()
        {
            // the sheet's z is off the grid by 2^-25; the snap moves it back onto z = 2
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2 + 1.0 / (1 << 25), -1, 5) }, D) { InputRoles = SheetSecond(), InputGrid = 1.0 / 1024 };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(32.0, Volume(p.Result), 12);
            Assert.True(p.Report.InputSnappedVertices > 0);
        }

        [Theory]
        [InlineData(ExactMeshBoolean.OrientationModes.Refuse)]
        [InlineData(ExactMeshBoolean.OrientationModes.ByNesting)]
        public void TheInsideOutCount_AndTheOrientationModes_DoNotTouchTheSheet(ExactMeshBoolean.OrientationModes mode)
        {
            // the pipeline counts inside-out components of its solids (and refuses them): a sheet is not one, even a closed one with inward normals; the cube
            // [0,4]^3 is cut by the sheet z = 2 as before
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D) { InputRoles = SheetSecond(), Orientation = mode };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Equal(0, p.Report.InsideOutCells);
            Assert.Equal(32.0, Volume(p.Result), 9);
            // a solid tool that is inside-out is still refused next to a sheet
            DMesh3 tool = Cube(1);
            MeshTransforms.Translate(tool, new Vector3d(1, 1, 1.5));
            tool.ReverseOrientation(false);
            var q = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5), tool }, D) {
                InputRoles = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Solid }, Orientation = mode
            };
            if (mode == ExactMeshBoolean.OrientationModes.Refuse) {
                Assert.False(q.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, q.Status);
                Assert.Equal(1, q.Report.InsideOutCells);
            } else {
                Assert.True(q.Compute(), q.Status + " " + q.Error);
                Assert.Equal(0, q.Report.InsideOutCells);
                Assert.Equal(32.0 - 0.5, Volume(q.Result), 9);
            }
        }
        [Fact]
        public void OpenInputError_NamesTheInputsAndTheWaysOut()
        {
            // #1211: the refusal says which input is open and how to state what it is
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D);
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.StartsWith("input 1 is not closed (InputRepair is Refuse)", p.Error);
            Assert.Contains("role Sheet (InputRoles)", p.Error);
            Assert.Contains("InputRepair = AutoRepair or AutoRepairThenRobust", p.Error);
            // two open inputs; the closed one between them is not named
            var q = new SolidBooleanPipeline(new List<DMesh3> { Sheet(1, -1, 5), Cube(4), Sheet(3, -1, 5) }, D);
            Assert.False(q.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, q.Status);
            Assert.StartsWith("inputs 0, 2 are not closed", q.Error);
            // a sheet with its role is not an open input: only the other one is named
            var r = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5), Sheet(3, -1, 5) }, D) { InputRoles = SheetSecond() };
            Assert.False(r.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, r.Status);
            Assert.StartsWith("input 2 is not closed", r.Error);
        }
    }
}
