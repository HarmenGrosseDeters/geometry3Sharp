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

        // ---- the route: an open tool of a Difference without a stated role is tried as a sheet; the report lists the steps -----------------------

        static string Names(SolidBooleanPipeline p) => string.Join(",", p.Report.Route.Select(s => s.Name + ":" + s.Input));

        [Fact]
        public void WithoutTheRole_TheOpenToolGoesToTheSheetRole_AndTheReportSaysSo()
        {
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Equal(32.0, Volume(p.Result), 9);
            Assert.Equal("SheetRole:1", Names(p));
            SolidBooleanRouteStep step = p.Report.Route[0];
            Assert.Contains("input 1 is open", step.Reason);
            Assert.Contains("used as a sheet", step.Change);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.None, step.SheetReason);
            // the role stated: the same result, and no step (nothing was decided for the caller)
            var stated = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D) { InputRoles = SheetSecond() };
            Assert.True(stated.Compute(), stated.Error);
            Assert.Equal("", Invariance.Difference(p.Result, stated.Result));
            Assert.Empty(stated.Report.Route);
            // the role Solid stated: no sheet route, refused as open, and the report says that
            var solid = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D) { InputRoles = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Solid } };
            Assert.False(solid.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, solid.Status);
            Assert.Equal("Refused:-1", Names(solid));
            // a role stated for the first input only: the second has none, so it is routed
            var shortList = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5) }, D) { InputRoles = new Roles { ExactMeshBoolean.InputRole.Solid } };
            Assert.True(shortList.Compute(), shortList.Error);
            Assert.Equal("SheetRole:1", Names(shortList));
            // closed inputs: an empty route
            var closed = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Cube(2) }, D);
            Assert.True(closed.Compute(), closed.Error);
            Assert.Empty(closed.Report.Route);
        }

        // the cube [0,4]^3 as a soup with shared vertices, without its top face: an open workpiece
        static DMesh3 Box(double x0, double x1, double z0, double z1, bool top)
        {
            var m = new DMesh3();
            var v = new int[8];
            for (int k = 0; k < 8; ++k)
                v[k] = m.AppendVertex(new Vector3d((k & 1) == 0 ? x0 : x1, (k & 2) == 0 ? x0 : x1, (k & 4) == 0 ? z0 : z1));
            System.Action<int, int, int, int> quad = (a, b, c, d) => { m.AppendTriangle(v[a], v[b], v[c]); m.AppendTriangle(v[a], v[c], v[d]); };
            quad(0, 2, 3, 1); quad(0, 1, 5, 4); quad(2, 6, 7, 3); quad(0, 4, 6, 2); quad(1, 3, 7, 5);
            if (top)
                quad(4, 5, 7, 6);
            return m;
        }

        static DMesh3 Joined(DMesh3 a, DMesh3 b)
        {
            var m = new DMesh3(a);
            var map = new int[b.MaxVertexID];
            foreach (int v in b.VertexIndices())
                map[v] = m.AppendVertex(b.GetVertex(v));
            foreach (int t in b.TriangleIndices()) {
                Index3i f = b.GetTriangle(t);
                m.AppendTriangle(map[f.a], map[f.b], map[f.c]);
            }
            return m;
        }

        // an open tool for the cube [0,4]^3 (or, WorkpieceNotClosed, for the open box) that the sheet role refuses for the given reason
        static DMesh3 RefusedTool(ExactMeshBoolean.SheetRefusal reason)
        {
            switch (reason) {
                case ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece: return Sheet(2, 1, 3);                    // a patch inside the cube
                case ExactMeshBoolean.SheetRefusal.UncutComponents: return Sheet(10, -1, 5);                     // far above the cube
                case ExactMeshBoolean.SheetRefusal.FloatingComponent: return Joined(Sheet(2, -1, 5), Box(1, 1.5, 0.5, 1, true));    // a plate and a bubble under it
                case ExactMeshBoolean.SheetRefusal.OrientationInconsistent: {
                    // two squares side by side in z = 2, the second turned over
                    var m = new DMesh3();
                    int a = m.AppendVertex(new Vector3d(-1, -1, 2)), b = m.AppendVertex(new Vector3d(2, -1, 2)), c = m.AppendVertex(new Vector3d(2, 5, 2)), d = m.AppendVertex(new Vector3d(-1, 5, 2));
                    int e = m.AppendVertex(new Vector3d(5, -1, 2)), f = m.AppendVertex(new Vector3d(5, 5, 2));
                    m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
                    m.AppendTriangle(b, f, e); m.AppendTriangle(b, c, f);
                    return m;
                }
                case ExactMeshBoolean.SheetRefusal.NotSurface: {
                    // three half planes at the line x = y = 2 (the third with vertices of its own at the line)
                    var m = new DMesh3();
                    Vector3d a = new Vector3d(2, 2, -1), b = new Vector3d(2, 2, 5);
                    foreach (Vector3d far in new[] { new Vector3d(6, 2, 5), new Vector3d(2, 6, 5), new Vector3d(-2, -2, 5) })
                        m.AppendTriangle(m.AppendVertex(a), m.AppendVertex(b), m.AppendVertex(far));
                    return m;
                }
                case ExactMeshBoolean.SheetRefusal.SidesInconsistent: {
                    // two walls x = 1 and x = 3 joined around the outside of the cube, their normals both towards +x: the part between them is in front of one and behind the other
                    var m = new DMesh3();
                    double[][] xy = { new[] { 1.0, -1 }, new[] { 1.0, 5 }, new[] { 6.0, 5 }, new[] { 6.0, -2 }, new[] { 3.0, -2 }, new[] { 3.0, 4.5 } };
                    var low = new int[xy.Length]; var high = new int[xy.Length];
                    for (int k = 0; k < xy.Length; ++k) {
                        low[k] = m.AppendVertex(new Vector3d(xy[k][0], xy[k][1], -1));
                        high[k] = m.AppendVertex(new Vector3d(xy[k][0], xy[k][1], 5));
                    }
                    for (int k = 0; k + 1 < xy.Length; ++k) {
                        m.AppendTriangle(low[k], low[k + 1], high[k + 1]);
                        m.AppendTriangle(low[k], high[k + 1], high[k]);
                    }
                    return m;
                }
                default: return Sheet(2, -1, 5);        // (WorkpieceNotClosed: a fine sheet, the workpiece is the open one)
            }
        }

        [Theory]
        [InlineData(ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece)]
        [InlineData(ExactMeshBoolean.SheetRefusal.UncutComponents)]
        [InlineData(ExactMeshBoolean.SheetRefusal.FloatingComponent)]
        [InlineData(ExactMeshBoolean.SheetRefusal.OrientationInconsistent)]
        [InlineData(ExactMeshBoolean.SheetRefusal.NotSurface)]
        [InlineData(ExactMeshBoolean.SheetRefusal.SidesInconsistent)]
        [InlineData(ExactMeshBoolean.SheetRefusal.WorkpieceNotClosed)]
        public void ARefusedSheetRoute_FallsBack_WithItsReasonInTheReportAndTheError(ExactMeshBoolean.SheetRefusal reason)
        {
            DMesh3 workpiece = reason == ExactMeshBoolean.SheetRefusal.WorkpieceNotClosed ? Box(0, 4, 0, 4, false) : Cube(4);
            DMesh3 tool = RefusedTool(reason);
            // (the fixture: with the role stated, the boolean refuses for this reason)
            var stated = new SolidBooleanPipeline(new List<DMesh3> { workpiece, tool }, D) { InputRoles = SheetSecond() };
            Assert.False(stated.Compute());
            Assert.Equal(reason, stated.SheetReason);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, stated.Status);
            Assert.Empty(stated.Report.Route);

            // by default (InputRepair Refuse): the sheet role, the fallback with the reason, the refusal as an open input
            var p = new SolidBooleanPipeline(new List<DMesh3> { workpiece, tool }, D);
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, p.BooleanStatus);
            Assert.Null(p.Result);
            Assert.Equal("SheetRole:1,Fallback:-1,Refused:-1", Names(p));
            SolidBooleanRouteStep fallback = p.Report.Route[1];
            Assert.Equal(reason, fallback.SheetReason);
            Assert.StartsWith("the sheet role refused: " + reason + ": ", fallback.Reason);
            Assert.True(fallback.Reason.Length > ("the sheet role refused: " + reason + ": ").Length, "the boolean's text follows the reason");
            Assert.Contains("InputRepair (Refuse)", fallback.Change);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.None, p.Report.Route[0].SheetReason);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.None, p.Report.Route[2].SheetReason);
            Assert.Contains("an input is not closed (InputRepair is Refuse)", p.Error);
            Assert.Contains("tried as a sheet first, which refused: " + reason + ": ", p.Error);
            // (the run's own reason is the open input: the sheet's is in the route)
            Assert.Equal(ExactMeshBoolean.SheetRefusal.None, p.SheetReason);
            Assert.Equal(2, p.Report.InputTriangles.Length);
            Assert.False(p.Report.InputClosed[1]);
        }

        [Fact]
        public void ARefusedSheetRoute_WithAutoRepair_RepairsAsBefore_AndTheRouteNamesTheRepair()
        {
            // the bucket [1,3]^3, open at the top, inside the cube: its rim is inside (the sheet role refuses), MeshAutoRepair closes it: a cavity of 8
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Box(1, 3, 1, 3, false) }, D) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Equal(64.0 - 8.0, Volume(p.Result), 9);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(p.Result));
            Assert.Equal("SheetRole:1,Fallback:-1,InputRepair:1", Names(p));
            Assert.Null(p.Error);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.None, p.SheetReason);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece, p.Report.Route[1].SheetReason);
            Assert.Contains("InputRepair (AutoRepair)", p.Report.Route[1].Change);
            Assert.Contains("input 1 is not closed", p.Report.Route[2].Reason);
            Assert.Equal("closed by MeshAutoRepair on a copy, 10 -> 12 triangles", p.Report.Route[2].Change);
            Assert.Single(p.Report.RepairedInputs);
            // the same result as the pipeline gives for the role Solid stated (the path without the route)
            var solid = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Box(1, 3, 1, 3, false) }, D) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair, InputRoles = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Solid }
            };
            Assert.True(solid.Compute(), solid.Error);
            Assert.Equal("", Invariance.Difference(solid.Result, p.Result));
            Assert.Equal("InputRepair:1", Names(solid));
        }

        [Fact]
        public void TheRoute_OfARepairedOpenWorkpiece_AndOfARefusedOne_WithoutASheet()
        {
            // an open first input (a box without its top) in a Union: no sheet route; repaired on request, refused by default, and the route says which
            var repaired = new SolidBooleanPipeline(new List<DMesh3> { Box(0, 4, 0, 4, false), Cube(2) }, ExactMeshBoolean.Operations.Union) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair };
            Assert.True(repaired.Compute(), repaired.Status + " " + repaired.Error);
            Assert.Equal(64.0, Volume(repaired.Result), 9);
            Assert.Equal("InputRepair:0", Names(repaired));
            var refused = new SolidBooleanPipeline(new List<DMesh3> { Box(0, 4, 0, 4, false), Cube(2) }, ExactMeshBoolean.Operations.Union);
            Assert.False(refused.Compute());
            Assert.Equal("Refused:-1", Names(refused));
            Assert.Contains("InputRepair is Refuse", refused.Report.Route[0].Reason);
            Assert.DoesNotContain("as a sheet", refused.Error);
            // an open input that no allowed repair closes (a triangle far below the size any repair keeps): the step names the input
            var tiny = new DMesh3();
            tiny.AppendTriangle(tiny.AppendVertex(new Vector3d(0, 0, 0)), tiny.AppendVertex(new Vector3d(1e-300, 0, 0)), tiny.AppendVertex(new Vector3d(0, 1e-300, 0)));
            var unrepaired = new SolidBooleanPipeline(new List<DMesh3> { tiny, Cube(2) }, ExactMeshBoolean.Operations.Union) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust };
            Assert.False(unrepaired.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, unrepaired.Status);
            Assert.Equal("Refused:0", Names(unrepaired));
            Assert.Contains("no allowed repair closed it (InputRepair = AutoRepairThenRobust)", unrepaired.Report.Route[0].Reason);
        }

        [Fact]
        public void TheRoute_IsOfTheLastCompute_AndTwoOpenTools_AreBothSheets_OrBothFallBack()
        {
            // the sheet z = 1 (the part above stays) and the wall x = 3 (normal -x: the part at x < 3 stays): 4 * 3 * 3
            var wall = new DMesh3();
            int a = wall.AppendVertex(new Vector3d(3, -1, -1)), b = wall.AppendVertex(new Vector3d(3, -1, 5)), c = wall.AppendVertex(new Vector3d(3, 5, 5)), d = wall.AppendVertex(new Vector3d(3, 5, -1));
            wall.AppendTriangle(a, b, c); wall.AppendTriangle(a, c, d);
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(1, -1, 5), wall }, D);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Equal(36.0, Volume(p.Result), 9);
            Assert.Equal("SheetRole:1,SheetRole:2", Names(p));
            // the second tool replaced by a patch inside the cube: its refusal sends both tools to the fallback
            p.Inputs[2] = Sheet(2, 1, 3);
            Assert.False(p.Compute());
            Assert.Equal("SheetRole:1,SheetRole:2,Fallback:-1,Refused:-1", Names(p));
            Assert.Equal(ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece, p.Report.Route[2].SheetReason);
            // and back: the route is that of the last run
            p.Inputs[2] = wall;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("SheetRole:1,SheetRole:2", Names(p));
            // a stated sheet that refuses is not a route to fall back from: its refusal is the result
            var stated = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, 1, 3), wall }, D) { InputRoles = SheetSecond() };
            Assert.False(stated.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, stated.Status);
            Assert.Equal("SheetRole:2,Fallback:-1,Refused:-1", Names(stated));
        }

        [Fact]
        public void ARoutedSheet_IsNotThin_AndAClosedToolNextToItIsUsedAsBefore()
        {
            DMesh3 tool = Cube(4);
            MeshTransforms.Translate(tool, new Vector3d(2, 2, 3));
            var p = new SolidBooleanPipeline(new List<DMesh3> { Cube(4), Sheet(2, -1, 5), tool }, D) { MinThickness = 1e-3, ThinInputs = SolidBooleanPipeline.ThinInputModes.Refuse };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.Equal(32.0 - 4.0, Volume(p.Result), 9);
            Assert.Empty(p.Report.ThinInputs);
            Assert.Equal("SheetRole:1", Names(p));
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
    }
}
