using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;
using gs;
using Xunit;
using Roles = System.Collections.Generic.List<g3.ExactMeshBoolean.InputRole>;
using Auto = gs.SolidBooleanPipeline.AutomaticStepModes;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline.AutomaticSteps = Grid: a run that fails with RoundingNotClean or the sheet refusal RimInsideWorkpiece
    /// is done once more with the inputs snapped to a cell taken from the data (docs/design/PIPELINE_GRID_STEP.md).
    /// </summary>
    public class SolidBooleanPipelineGridStepTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union, D = ExactMeshBoolean.Operations.Difference;
        const ExactMeshBoolean.SheetRefusal Rim = ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece;

        static double Pow2(int k) { return BitConverter.Int64BitsToDouble((long)(k + 1023) << 52); }

        // the box [0.1, 4.1] x [0.2, 4.3] x [0.3, 4.7]: no face on a coarse grid
        static readonly Vector3d Lo = new Vector3d(0.1, 0.2, 0.3), Hi = new Vector3d(4.1, 4.3, 4.7);

        static DMesh3 Box() => new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(Lo, Hi)) }.Generate().MakeDMesh();

        // the horizontal sheet z = 2.5 over [x0, x1] x [y0, y1], normals up
        static DMesh3 Sheet(double x0, double x1, double y0, double y1)
        {
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(x0, y0, 2.5)), b = m.AppendVertex(new Vector3d(x1, y0, 2.5)), c = m.AppendVertex(new Vector3d(x1, y1, 2.5)), d = m.AppendVertex(new Vector3d(x0, y1, 2.5));
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            return m;
        }

        // one ulp of the box's largest coordinate (4.7, in [4, 8))
        static readonly double Ulp = Pow2(-50);

        // the sheet whose rim is meant to lie on the four side faces of the box, pushed 3 ulps inside on every side
        static List<DMesh3> FlushRim() => new List<DMesh3> { Box(), Sheet(Lo.x + 3 * Ulp, Hi.x - 3 * Ulp, Lo.y + 3 * Ulp, Hi.y - 3 * Ulp) };

        // a patch well inside the box: no grid puts its rim on the surface
        static List<DMesh3> PatchInside() => new List<DMesh3> { Box(), Sheet(1, 3, 1, 3) };

        static Roles SheetSecond() => new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet };

        static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        static string Names(SolidBooleanPipeline p) => string.Join(",", p.Report.Route.Select(s => s.Name + ":" + s.Input));

        // the upper part of the box, z in [2.5, 4.7]
        static readonly double UpperVolume = (Hi.x - Lo.x) * (Hi.y - Lo.y) * (Hi.z - 2.5);

        static void AssertSolid(SolidBooleanPipeline p, bool contactsAllowed = false)
        {
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            DMesh3 r = p.Result;
            Assert.NotNull(r);
            Assert.True(r.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(r));
            Assert.True(r.IsClosed());
            Assert.True(r.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(p.Report.Clean || (contactsAllowed && p.Report.OnlyContacts), p.Report.NotCleanReason);
        }

        // ---- the sheet whose rim is a few ulps inside ----

        [Fact]
        public void AFlushRimAFewUlpsInside_IsRefusedWithoutTheStep_AndCutWithIt()
        {
            var plain = new SolidBooleanPipeline(FlushRim(), D);
            Assert.Equal(Auto.None, plain.AutomaticSteps);
            Assert.False(plain.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, plain.Status);
            Assert.Equal("SheetRole:1,Fallback:-1,Refused:-1", Names(plain));
            Assert.Equal(Rim, plain.Report.Route[1].SheetReason);

            List<DMesh3> inputs = FlushRim();
            var before = inputs.Select(m => new DMesh3(m)).ToList();
            var p = new SolidBooleanPipeline(inputs, D) { AutomaticSteps = Auto.Grid };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertSolid(p);
            Assert.Equal(UpperVolume, Volume(p.Result), 9);
            Assert.Equal(0, p.Report.ContactsResult);
            Assert.Empty(p.Report.RepairedInputs);
            Assert.Null(p.Error);
            for (int i = 0; i < inputs.Count; ++i)
                Assert.Equal("", Invariance.Difference(before[i], inputs[i]));
            Assert.Equal(0.0, p.InputGrid);
            // the route: the refused sheet, the grid, the sheet again
            Assert.Equal("SheetRole:1,Grid:-1,SheetRole:1", Names(p));
            SolidBooleanRouteStep g = p.Report.Route[1];
            Assert.Equal(Pow2(-38), g.Grid);        // 2^12 ulps of 4.7
            Assert.StartsWith("the sheet role refused: RimInsideWorkpiece: ", g.Reason);
            // every vertex of the box is off the grid (8), and so are the 4 of the sheet
            Assert.Equal(12, p.Report.InputSnappedVertices);
            Assert.Equal("the inputs are snapped to the cell 2^-38 (3.637978807091713E-12), which moved 12 vertices, and the run is done again", g.Change);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.None, g.SheetReason);
            // (the removed share belongs to the sheet step of the run that cut)
            Assert.DoesNotContain("removed", p.Report.Route[0].Change);
            Assert.Contains("the Difference removed 50 % of the first input's volume", p.Report.Route[2].Change);
            Assert.Equal(0.0, p.Report.Route[0].Grid);
            Assert.Equal(0.0, p.Report.Route[2].Grid);
            // the result is the one of the snapped inputs
            int moved;
            var snapped = FlushRim().Select(m => SolidBooleanPipeline.Snapped(m, Pow2(-38), out moved)).ToList();
            var direct = new SolidBooleanPipeline(snapped, D);
            Assert.True(direct.Compute(), direct.Error);
            Assert.Equal("", Invariance.Difference(direct.Result, p.Result));
        }

        [Fact]
        public void AStatedSheetWithAFlushRim_IsRefusedWithoutTheStep_AndCutWithIt()
        {
            var plain = new SolidBooleanPipeline(FlushRim(), D) { InputRoles = SheetSecond() };
            Assert.False(plain.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, plain.Status);
            Assert.Equal(Rim, plain.SheetReason);
            Assert.Empty(plain.Report.Route);

            var p = new SolidBooleanPipeline(FlushRim(), D) { InputRoles = SheetSecond(), AutomaticSteps = Auto.Grid };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertSolid(p);
            Assert.Equal(UpperVolume, Volume(p.Result), 9);
            Assert.Equal("Grid:-1", Names(p));
            Assert.StartsWith("SheetRefused: ", p.Report.Route[0].Reason);
            Assert.Contains("lies inside the workpiece", p.Report.Route[0].Reason);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.None, p.SheetReason);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, p.BooleanStatus);
        }

        // ---- the rounding refusal ----

        [Fact]
        public void ARoundingRefusal_IsAnsweredOnTheGrid_WithTheResultOfInputGridAtThatCell()
        {
            // the fragment of SolidBooleanPipelineInputGridTests (coordinates up to 6.6e6, one ulp 2^-30): RoundingNotClean as given
            var plain = new SolidBooleanPipeline(SolidBooleanPipelineInputGridTests.Fragment(), D);
            Assert.False(plain.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, plain.BooleanStatus);
            Assert.Empty(plain.Report.Route);

            var p = new SolidBooleanPipeline(SolidBooleanPipelineInputGridTests.Fragment(), D) { AutomaticSteps = Auto.Grid };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            // (at this cell the snapped solids touch along one contact, which the final check reports as such: a closed, valid solid)
            AssertSolid(p, true);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, p.BooleanStatus);
            Assert.Equal("Grid:-1", Names(p));
            SolidBooleanRouteStep g = p.Report.Route[0];
            Assert.Equal(Pow2(-18), g.Grid);
            Assert.StartsWith("RoundingNotClean: ", g.Reason);
            Assert.Equal(53, p.Report.InputSnappedVertices);
            Assert.Contains("the cell 2^-18 (3.814697265625E-06), which moved 53 vertices", g.Change);
            Assert.True(Volume(p.Result) > 0 && Volume(p.Result) < Volume(SolidBooleanPipelineInputGridTests.Fragment()[0]));
            // the same as the caller's grid at that cell, where no step is listed
            var stated = new SolidBooleanPipeline(SolidBooleanPipelineInputGridTests.Fragment(), D) { InputGrid = Pow2(-18) };
            Assert.True(stated.Compute(), stated.Error);
            Assert.Empty(stated.Report.Route);
            Assert.Equal("", Invariance.Difference(stated.Result, p.Result));
            Assert.Equal(stated.Report.NotCleanReason, p.Report.NotCleanReason);
        }

        [Fact]
        public void ARoundingRefusalOfTheRunAfterARefusedSheet_IsAnsweredToo()
        {
            // the tool of the fragment with one triangle flipped: not closed by the winding, so it is tried as a sheet, which refuses its orientation;
            // as a solid (the boolean reorients it by AreaMajority) the rounding is refused as for the fragment
            Func<List<DMesh3>> inputs = () => {
                List<DMesh3> f = SolidBooleanPipelineInputGridTests.Fragment();
                f[1].ReverseTriOrientation(0);
                return f;
            };
            var plain = new SolidBooleanPipeline(inputs(), D) { Orientation = ExactMeshBoolean.OrientationModes.AreaMajority };
            Assert.False(plain.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, plain.BooleanStatus);
            Assert.Equal("SheetRole:1,Fallback:-1", Names(plain));
            Assert.NotEqual(Rim, plain.Report.Route[1].SheetReason);

            var p = new SolidBooleanPipeline(inputs(), D) { Orientation = ExactMeshBoolean.OrientationModes.AreaMajority, AutomaticSteps = Auto.Grid };
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertSolid(p, true);
            // (the retry is the run that failed, the one with the inputs as solids: the sheet is not tried again)
            Assert.Equal("SheetRole:1,Fallback:-1,Grid:-1", Names(p));
            Assert.StartsWith("RoundingNotClean: ", p.Report.Route[2].Reason);
            Assert.Equal(Pow2(-18), p.Report.Route[2].Grid);
        }

        // ---- the retry fails too: the first failure is the result ----

        [Fact]
        public void WhenTheRetryFailsToo_TheFirstFailureIsTheResult_WithTheAttemptInTheRoute()
        {
            var plain = new SolidBooleanPipeline(PatchInside(), D);
            Assert.False(plain.Compute());
            var p = new SolidBooleanPipeline(PatchInside(), D) { AutomaticSteps = Auto.Grid };
            Assert.False(p.Compute());
            Assert.Null(p.Result);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.Equal(plain.Error, p.Error);
            Assert.Equal(plain.BooleanStatus, p.BooleanStatus);
            // the attempt is one step, before the fallback that the run then takes as without the option; the steps of the failed retry are not listed
            Assert.Equal("SheetRole:1,Grid:-1,Fallback:-1,Refused:-1", Names(p));
            SolidBooleanRouteStep g = p.Report.Route[1];
            Assert.Equal(Pow2(-38), g.Grid);
            Assert.StartsWith("the inputs are snapped to the cell 2^-38 (3.637978807091713E-12), which moved 8 vertices, and the run is done again; it failed too (BooleanFailed: SheetRefused: ", g.Change);
            Assert.EndsWith("), so the first failure is the result", g.Change);
            Assert.Equal(Rim, p.Report.Route[2].SheetReason);
            // (the report is the one of the run without the grid)
            Assert.Equal(0, p.Report.InputSnappedVertices);

            // a stated sheet: the refusal of the first run comes back, with its reason
            var statedPlain = new SolidBooleanPipeline(PatchInside(), D) { InputRoles = SheetSecond() };
            Assert.False(statedPlain.Compute());
            var stated = new SolidBooleanPipeline(PatchInside(), D) { InputRoles = SheetSecond(), AutomaticSteps = Auto.Grid };
            Assert.False(stated.Compute());
            Assert.Null(stated.Result);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, stated.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.SheetRefused, stated.BooleanStatus);
            Assert.Equal(Rim, stated.SheetReason);
            Assert.Equal(statedPlain.Error, stated.Error);
            Assert.Equal("Grid:-1", Names(stated));
            Assert.Contains("it failed too", stated.Report.Route[0].Change);
            Assert.Equal(0, stated.Report.InputSnappedVertices);
        }

        [Fact]
        public void ARetryThatFailsForAnotherReason_LeavesTheFirstFailure()
        {
            // the retry is stopped by a budget (ConfigureBoolean runs for every boolean: the second is the retry's)
            foreach (bool sheet in new[] { false, true }) {
                Func<SolidBooleanPipeline> make = () => sheet
                    ? new SolidBooleanPipeline(FlushRim(), D) { InputRoles = SheetSecond() }
                    : new SolidBooleanPipeline(SolidBooleanPipelineInputGridTests.Fragment(), D);
                SolidBooleanPipeline plain = make();
                Assert.False(plain.Compute());
                SolidBooleanPipeline p = make();
                p.AutomaticSteps = Auto.Grid;
                int booleans = 0;
                p.ConfigureBoolean = op => { if (++booleans == 2) op.MaxPieces = 1; };
                Assert.False(p.Compute());
                Assert.Equal(2, booleans);
                Assert.Null(p.Result);
                Assert.Equal(plain.Status, p.Status);
                Assert.Equal(plain.Error, p.Error);
                Assert.Equal(sheet ? ExactMeshBoolean.BooleanStatus.SheetRefused : ExactMeshBoolean.BooleanStatus.RoundingNotClean, p.BooleanStatus);
                Assert.Equal(sheet ? Rim : ExactMeshBoolean.SheetRefusal.None, p.SheetReason);
                Assert.Equal("Grid:-1", Names(p));
                Assert.Contains("it failed too (BooleanFailed: BudgetExceeded: ", p.Report.Route[0].Change);
                Assert.Equal(0, p.Report.InputSnappedVertices);
            }
        }

        [Fact]
        public void ARetryThatFailsWithAnotherStatus_LeavesTheFirstStatus()
        {
            // the tool of the fragment with one triangle flipped, as a solid: the first boolean reorients it (set in ConfigureBoolean) and the
            // rounding is refused; the boolean of the retry keeps the pipeline's Orientation (Refuse) and refuses the orientation
            List<DMesh3> inputs = SolidBooleanPipelineInputGridTests.Fragment();
            inputs[1].ReverseTriOrientation(0);
            int booleans = 0;
            var p = new SolidBooleanPipeline(inputs, D) {
                InputRoles = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Solid }, AutomaticSteps = Auto.Grid
            };
            p.ConfigureBoolean = op => { if (++booleans == 1) op.Orientation = ExactMeshBoolean.OrientationModes.AreaMajority; };
            Assert.False(p.Compute());
            Assert.Equal(2, booleans);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, p.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, p.BooleanStatus);
            Assert.StartsWith("RoundingNotClean: ", p.Error);
            Assert.Equal("Grid:-1", Names(p));
            Assert.Contains("it failed too (InconsistentOrientation: ", p.Report.Route[0].Change);
        }

        [Fact]
        public void TheTimeOfBothRuns_IsBooleanTimeOfTheResult()
        {
            // (ConfigureBoolean runs inside the boolean's time: the wait in one run has to show in the report of the other)
            // a retry that succeeds: the first run waits
            int booleans = 0;
            var p = new SolidBooleanPipeline(FlushRim(), D) { InputRoles = SheetSecond(), AutomaticSteps = Auto.Grid };
            p.ConfigureBoolean = op => { if (++booleans == 1) Thread.Sleep(400); };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(2, booleans);
            Assert.True(p.Report.BooleanMilliseconds >= 390, "boolean " + p.Report.BooleanMilliseconds);
            Assert.True(p.Report.TotalMilliseconds >= p.Report.BooleanMilliseconds);
            // a retry that fails: the retry waits, and the report of the first run has its time
            booleans = 0;
            var q = new SolidBooleanPipeline(PatchInside(), D) { InputRoles = SheetSecond(), AutomaticSteps = Auto.Grid };
            q.ConfigureBoolean = op => { if (++booleans == 2) Thread.Sleep(400); };
            Assert.False(q.Compute());
            Assert.Equal(2, booleans);
            Assert.True(q.Report.BooleanMilliseconds >= 390, "boolean " + q.Report.BooleanMilliseconds);
            Assert.True(q.Report.TotalMilliseconds >= 390, "total " + q.Report.TotalMilliseconds);
            // a retry that is cancelled: the first run waits, and the report of the cancelled run has its time
            booleans = 0;
            var cts = new CancellationTokenSource();
            var c = new SolidBooleanPipeline(FlushRim(), D) { InputRoles = SheetSecond(), AutomaticSteps = Auto.Grid, Cancellation = cts.Token };
            c.ConfigureBoolean = op => { if (++booleans == 1) Thread.Sleep(400); else cts.Cancel(); };
            Assert.False(c.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Cancelled, c.Status);
            Assert.True(c.Report.BooleanMilliseconds >= 390, "boolean " + c.Report.BooleanMilliseconds);
        }

        [Fact]
        public void InputsOnTheCellAlready_AreNotRunAgain()
        {
            // the cube [0,4]^3 and a patch inside it at dyadic coordinates: the snap to the cell 2^-38 moves nothing, so the retry would be the
            // run that failed. (A rounding refusal of inputs on their cell is not at hand: the fragment on its cell is the cured case)
            Func<List<DMesh3>> inputs = () => new List<DMesh3> {
                new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(4, 4, 4))) }.Generate().MakeDMesh(), Sheet(1, 3, 1, 3) };
            Assert.False(SolidBooleanPipeline.OffGrid(inputs(), Pow2(-38)));
            var plain = new SolidBooleanPipeline(inputs(), D) { InputRoles = SheetSecond() };
            Assert.False(plain.Compute());
            int booleans = 0;
            var p = new SolidBooleanPipeline(inputs(), D) { InputRoles = SheetSecond(), AutomaticSteps = Auto.Grid, ConfigureBoolean = op => booleans++ };
            Assert.False(p.Compute());
            Assert.Equal(1, booleans);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, p.Status);
            Assert.Equal(Rim, p.SheetReason);
            Assert.Equal(plain.Error, p.Error);
            Assert.Equal("Grid:-1", Names(p));
            Assert.Equal(Pow2(-38), p.Report.Route[0].Grid);
            Assert.Equal("every input vertex is on the cell 2^-38 (3.637978807091713E-12) already, so the run is not done again and the first failure is the result", p.Report.Route[0].Change);
            // on the sheet route: the sheet, then the fallback as without the step
            booleans = 0;
            var routed = new SolidBooleanPipeline(inputs(), D) { AutomaticSteps = Auto.Grid, ConfigureBoolean = op => booleans++ };
            Assert.False(routed.Compute());
            Assert.Equal(2, booleans);
            Assert.Equal("SheetRole:1,Grid:-1,Fallback:-1,Refused:-1", Names(routed));
        }

        [Fact]
        public void OffGrid_IsTrueWhereTheSnapWouldMoveAVertex()
        {
            Assert.False(SolidBooleanPipeline.OffGrid(new[] { Points(0.25, -0.5, 8, 0, -0.0, 1e300) }, 0.25));
            Assert.True(SolidBooleanPipeline.OffGrid(new[] { Points(0.3, 0, 0) }, 0.25));
            Assert.True(SolidBooleanPipeline.OffGrid(new[] { Points(0, 0.3, 0) }, 0.25));
            Assert.True(SolidBooleanPipeline.OffGrid(new[] { Points(0, 0, 0.3) }, 0.25));
            Assert.True(SolidBooleanPipeline.OffGrid(new[] { Points(0, 0, 0), Points(1, 1, 1, 0, -0.1, 0) }, 0.25));
            // (a coordinate that is not finite is left where it is)
            Assert.False(SolidBooleanPipeline.OffGrid(new[] { Points(double.NaN, double.PositiveInfinity, 0.5) }, 0.25));
            Assert.False(SolidBooleanPipeline.OffGrid(new[] { new DMesh3() }, 0.25));
        }

        [Fact]
        public void ACancelledRetryOfTheRunAfterARefusedSheet_IsCancelled()
        {
            // the booleans: the sheet (refused for its orientation), the inputs as solids (the rounding is refused), the retry
            List<DMesh3> inputs = SolidBooleanPipelineInputGridTests.Fragment();
            inputs[1].ReverseTriOrientation(0);
            var cts = new CancellationTokenSource();
            int booleans = 0;
            var p = new SolidBooleanPipeline(inputs, D) { Orientation = ExactMeshBoolean.OrientationModes.AreaMajority, AutomaticSteps = Auto.Grid, Cancellation = cts.Token };
            p.ConfigureBoolean = op => { if (++booleans == 3) cts.Cancel(); };
            Assert.False(p.Compute());
            Assert.Equal(3, booleans);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Cancelled, p.Status);
            Assert.Null(p.Result);
            Assert.Equal("SheetRole:1,Fallback:-1,Grid:-1", Names(p));
        }

        [Fact]
        public void InputsWithoutACell_KeepTheirFailure_WithoutAStep()
        {
            // the flush rim at coordinates of 2^-990: the cell would not be a normal number
            double f = Pow2(-992);
            var tiny = FlushRim().Select(m => { var c = new DMesh3(m); MeshTransforms.Scale(c, f); return c; }).ToList();
            Assert.Equal(0.0, SolidBooleanPipeline.DataGrid(tiny));
            var p = new SolidBooleanPipeline(tiny, D) { InputRoles = SheetSecond(), AutomaticSteps = Auto.Grid };
            Assert.False(p.Compute());
            Assert.Equal(Rim, p.SheetReason);
            Assert.Empty(p.Report.Route);
        }

        [Fact]
        public void TheStepRunsOnce_AFailureOfTheFallbackAfterAFailedRetryIsNotRetried()
        {
            // the fragment's tool without a triangle is refused as a sheet with its rim inside, also on the grid; closed by the repair, the
            // rounding is refused: the step was spent on the sheet
            Func<List<DMesh3>> inputs = () => {
                List<DMesh3> f = SolidBooleanPipelineInputGridTests.Fragment();
                f[1].RemoveTriangle(HoleTriangle);
                return f;
            };
            var p = new SolidBooleanPipeline(inputs(), D) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair, AutomaticSteps = Auto.Grid };
            Assert.False(p.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, p.BooleanStatus);
            Assert.Equal("SheetRole:1,Grid:-1,Fallback:-1,InputRepair:1", Names(p));
            Assert.Single(p.Report.RepairedInputs);
        }
        const int HoleTriangle = 11;

        // ---- off, and where the step does not run ----

        [Fact]
        public void OnASuccess_TheStepDoesNotRun_AndTheOutputIsTheOneWithoutTheOption()
        {
            Func<List<DMesh3>> spheres = () => new List<DMesh3> {
                new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh(),
                Moved(new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh(), new Vector3d(1.2, 0.1, 0.05)) };
            var off = new SolidBooleanPipeline(spheres(), U) { TargetFraction = 0.25 };
            Assert.True(off.Compute(), off.Error);
            var on = new SolidBooleanPipeline(spheres(), U) { TargetFraction = 0.25, AutomaticSteps = Auto.Grid };
            Assert.True(on.Compute(), on.Error);
            Assert.Equal("", Invariance.Difference(off.Result, on.Result));
            Assert.Empty(on.Report.Route);
            Assert.Equal(0, on.Report.InputSnappedVertices);
            // a cut sheet: the route of the run without the option
            var cut = new SolidBooleanPipeline(new List<DMesh3> { Box(), Sheet(-1, 5, -1, 5) }, D) { AutomaticSteps = Auto.Grid };
            Assert.True(cut.Compute(), cut.Error);
            Assert.Equal("SheetRole:1", Names(cut));
            Assert.Equal(0, cut.Report.InputSnappedVertices);
        }

        static DMesh3 Moved(DMesh3 m, Vector3d by) { MeshTransforms.Translate(m, by); return m; }

        [Fact]
        public void OtherFailures_AndACallersGrid_AreNotAnswered()
        {
            // an open tool with the role Solid: OpenInput, not a failure of the step
            var open = new SolidBooleanPipeline(FlushRim(), D) { InputRoles = new Roles { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Solid }, AutomaticSteps = Auto.Grid };
            Assert.False(open.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, open.Status);
            Assert.Equal("Refused:-1", Names(open));
            // a sheet refused for another reason (it does not reach the box: nothing is cut)
            var uncut = new SolidBooleanPipeline(new List<DMesh3> { Box(), Sheet(10, 11, 10, 11) }, D) { InputRoles = SheetSecond(), AutomaticSteps = Auto.Grid };
            Assert.False(uncut.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.SheetRefused, uncut.BooleanStatus);
            Assert.NotEqual(Rim, uncut.SheetReason);
            Assert.Empty(uncut.Report.Route);
            var routed = new SolidBooleanPipeline(new List<DMesh3> { Box(), Sheet(10, 11, 10, 11) }, D) { AutomaticSteps = Auto.Grid };
            Assert.False(routed.Compute());
            Assert.Equal("SheetRole:1,Fallback:-1,Refused:-1", Names(routed));
            // InputGrid is set (to a cell so fine that it moves nothing): the caller's grid is used as stated
            var stated = new SolidBooleanPipeline(FlushRim(), D) { InputGrid = Pow2(-60), AutomaticSteps = Auto.Grid };
            Assert.False(stated.Compute());
            Assert.Equal("SheetRole:1,Fallback:-1,Refused:-1", Names(stated));
            var fragment = new SolidBooleanPipeline(SolidBooleanPipelineInputGridTests.Fragment(), D) { InputGrid = Pow2(-60), AutomaticSteps = Auto.Grid };
            Assert.False(fragment.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, fragment.BooleanStatus);
            Assert.Empty(fragment.Report.Route);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(-1)]
        public void AnUndefinedValue_IsRefused(int value)
        {
            var p = new SolidBooleanPipeline(FlushRim(), D) { AutomaticSteps = (Auto)value };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
            Assert.Contains("AutomaticSteps", p.Error);
            Assert.Null(p.Result);
            Assert.Empty(p.Report.Route);
        }

        // the reduced form of the measurement of docs/design/PIPELINE_GRID_STEP.md: boxes with faces at random coordinates, the rim of
        // the sheet on four side faces and 1 to 16 ulps inside on each. A rounding boundary of the cell separates a pair with the chance
        // noise / cell, so a few boxes may stay refused; without the step none is cut
        [Fact]
        public void TheCell_CuresRandomFlushRims()
        {
            var rnd = new Random(20261123);
            int cut = 0, n = 60;
            for (int i = 0; i < n; ++i) {
                double off = i % 2 == 0 ? 0 : 6.6e6;
                var lo = new Vector3d(off + rnd.NextDouble(), off + rnd.NextDouble(), rnd.NextDouble());
                Vector3d hi = lo + new Vector3d(3 + rnd.NextDouble(), 3 + rnd.NextDouble(), 3 + rnd.NextDouble());
                DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(lo, hi)) }.Generate().MakeDMesh();
                double ulp = SolidBooleanPipeline.DataGrid(new[] { box }) / 4096;
                Func<double> d = () => (1 + rnd.Next(16)) * ulp;
                DMesh3 sheet = Sheet(lo.x + d(), hi.x - d(), lo.y + d(), hi.y - d());
                MeshTransforms.Translate(sheet, new Vector3d(0, 0, (lo.z + hi.z) / 2 - 2.5));
                var plain = new SolidBooleanPipeline(new List<DMesh3> { box, sheet }, D);
                Assert.False(plain.Compute());
                var p = new SolidBooleanPipeline(new List<DMesh3> { box, sheet }, D) { AutomaticSteps = Auto.Grid };
                if (p.Compute()) {
                    AssertSolid(p);
                    Assert.Equal("SheetRole:1,Grid:-1,SheetRole:1", Names(p));
                    // (measured near the origin: the volume formula cancels at coordinates of 6.6e6)
                    double v = Volume(Moved(new DMesh3(p.Result), new Vector3d(-off, -off, 0))), half = (hi.x - lo.x) * (hi.y - lo.y) * (hi.z - lo.z) / 2;
                    // (the snap moves every face and the cut by at most half a cell: 3.8e-6 at 6.6e6)
                    Vector3d e = hi - lo;
                    double moved = p.Report.Route[1].Grid * 2 * (e.x * e.y + e.y * e.z + e.z * e.x);
                    Assert.True(Math.Abs(v - half) <= moved + 1e-9 * half, i + ": " + v + " vs " + half);
                    cut++;
                } else
                    Assert.Equal("SheetRole:1,Grid:-1,Fallback:-1,Refused:-1", Names(p));
            }
            Assert.True(cut >= n - 3, cut + " of " + n);
        }

        [Fact]
        public void ACancelledRetry_IsCancelled()
        {
            var cts = new CancellationTokenSource();
            int booleans = 0;
            var p = new SolidBooleanPipeline(FlushRim(), D) { AutomaticSteps = Auto.Grid, Cancellation = cts.Token };
            // (ConfigureBoolean runs for every boolean of a run: the second is the one of the retry)
            p.ConfigureBoolean = op => { if (++booleans == 2) cts.Cancel(); };
            Assert.False(p.Compute());
            Assert.Equal(2, booleans);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Cancelled, p.Status);
            Assert.Null(p.Result);
            Assert.Equal("SheetRole:1,Grid:-1,SheetRole:1", Names(p));
        }

        // ---- the cell ----

        static DMesh3 Points(params double[] xyz)
        {
            var m = new DMesh3();
            for (int i = 0; i < xyz.Length; i += 3)
                m.AppendVertex(new Vector3d(xyz[i], xyz[i + 1], xyz[i + 2]));
            return m;
        }

        [Fact]
        public void TheCell_Is2To12UlpsOfTheLargestAbsoluteCoordinateOfAllInputs()
        {
            Assert.Equal(12, SolidBooleanPipeline.GridBinades);
            Assert.Equal(Pow2(-38), SolidBooleanPipeline.DataGrid(new[] { Points(4, 0, 0) }));                   // ulp(4) = 2^-50
            Assert.Equal(Pow2(-39), SolidBooleanPipeline.DataGrid(new[] { Points(3.9999, 0, 0) }));
            Assert.Equal(Pow2(-38), SolidBooleanPipeline.DataGrid(new[] { Points(7.9999, 0, 0) }));
            // every axis, the sign, and every input count
            Assert.Equal(Pow2(-37), SolidBooleanPipeline.DataGrid(new[] { Points(1, -8, 2) }));
            Assert.Equal(Pow2(-36), SolidBooleanPipeline.DataGrid(new[] { Points(1, 2, -16, 0, 0, 0) }));
            Assert.Equal(Pow2(-35), SolidBooleanPipeline.DataGrid(new[] { Points(1, 2, 3), Points(0, 0, 0, -32, 1, 1) }));
            Assert.Equal(Pow2(-35), SolidBooleanPipeline.DataGrid(new[] { Points(32, 2, 3), Points(1, 1, 1) }));
            // coordinates that are not finite do not count
            Assert.Equal(Pow2(-38), SolidBooleanPipeline.DataGrid(new[] { Points(double.NaN, double.PositiveInfinity, double.NegativeInfinity, 4, 1, 1) }));
            // no cell: nothing but zeros, no vertex, or a cell that would not be a normal number
            Assert.Equal(0.0, SolidBooleanPipeline.DataGrid(new[] { Points(0, -0.0, 0) }));
            Assert.Equal(0.0, SolidBooleanPipeline.DataGrid(new[] { new DMesh3() }));
            Assert.Equal(Pow2(-1022), SolidBooleanPipeline.DataGrid(new[] { Points(Pow2(-982), 0, 0) }));
            Assert.Equal(0.0, SolidBooleanPipeline.DataGrid(new[] { Points(Pow2(-983), 0, 0) }));
            Assert.Equal(0.0, SolidBooleanPipeline.DataGrid(new[] { Points(double.Epsilon, 0, 0) }));
            Assert.Equal(Pow2(983), SolidBooleanPipeline.DataGrid(new[] { Points(double.MaxValue, 0, 0) }));
            Assert.Equal(-38, SolidBooleanPipeline.CellExponent(Pow2(-38)));
            Assert.Equal(983, SolidBooleanPipeline.CellExponent(Pow2(983)));
        }

        // ---- reuse ----

        [Fact]
        public void Compute_Twice_GivesTheSameResult_AndFollowsTheOption()
        {
            var p = new SolidBooleanPipeline(FlushRim(), D) { AutomaticSteps = Auto.Grid };
            Assert.True(p.Compute(), p.Error);
            DMesh3 first = p.Result;
            string route = Names(p), change = p.Report.Route[1].Change;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.Difference(first, p.Result));
            Assert.Equal(route, Names(p));
            Assert.Equal(change, p.Report.Route[1].Change);
            Assert.Equal(12, p.Report.InputSnappedVertices);
            p.AutomaticSteps = Auto.None;
            Assert.False(p.Compute());
            Assert.Equal("SheetRole:1,Fallback:-1,Refused:-1", Names(p));
            Assert.Equal(0, p.Report.InputSnappedVertices);
            p.AutomaticSteps = Auto.Grid;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.Difference(first, p.Result));
            // after a retry that failed, too
            var q = new SolidBooleanPipeline(PatchInside(), D) { AutomaticSteps = Auto.Grid };
            Assert.False(q.Compute());
            string failed = Names(q), error = q.Error;
            Assert.False(q.Compute());
            Assert.Equal(failed, Names(q));
            Assert.Equal(error, q.Error);
        }

        // ---- invariance: the cell scales with the data ----

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Result_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            var reference = new SolidBooleanPipeline(FlushRim(), D) { AutomaticSteps = Auto.Grid };
            Assert.True(reference.Compute(), reference.Error);

            var p = new SolidBooleanPipeline(FlushRim().Select(m => Invariance.Transformed(m, s)).ToList(), D) { AutomaticSteps = Auto.Grid };
            bool done = p.Compute();
            if (s.IsExact) {
                // a power-of-two scale: the cell is scaled with the data, and the result is the same, bit for bit
                Assert.True(done, s.Name + ": " + p.Error);
                AssertSolid(p);
                Assert.Equal(reference.Report.Route[1].Grid * s.Scale, p.Report.Route[1].Grid);
                Assert.Equal(reference.Report.InputSnappedVertices, p.Report.InputSnappedVertices);
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(p.Result, s)));
                return;
            }
            // another frame rounds the rim to other positions (and a rotation leaves no face parallel to an axis, so no snap puts the rim on it):
            // either the sheet cuts (as given or on the grid) and the result is the upper part, or the first failure is the result
            if (done) {
                AssertSolid(p);
                double scale = s.Scale * s.Scale * s.Scale;
                Assert.True(Math.Abs(Volume(p.Result) - UpperVolume * scale) <= 1e-6 * UpperVolume * scale, s.Name + ": " + Volume(p.Result));
                Assert.Contains(Names(p), new[] { "SheetRole:1", "SheetRole:1,Grid:-1,SheetRole:1" });
            } else {
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
                Assert.Equal("SheetRole:1,Grid:-1,Fallback:-1,Refused:-1", Names(p));
                Assert.Null(p.Result);
            }
            if (p.Report.Route.Count > 1)
                Assert.Equal(SolidBooleanPipeline.DataGrid(p.Inputs), p.Report.Route[1].Grid);
        }

        [Fact]
        public void TheFragment_ScaledByPowersOfTwo_GivesTheSameResult()
        {
            var reference = new SolidBooleanPipeline(SolidBooleanPipelineInputGridTests.Fragment(), D) { AutomaticSteps = Auto.Grid };
            Assert.True(reference.Compute(), reference.Error);
            foreach (int k in new[] { -10, 10 }) {
                double f = Pow2(k);
                var scaled = SolidBooleanPipelineInputGridTests.Fragment().Select(m => { var c = new DMesh3(m); MeshTransforms.Scale(c, f); return c; }).ToList();
                var p = new SolidBooleanPipeline(scaled, D) { AutomaticSteps = Auto.Grid };
                Assert.True(p.Compute(), "2^" + k + ": " + p.Error);
                Assert.Equal(Pow2(-18 + k), p.Report.Route[0].Grid);
                Assert.Equal(53, p.Report.InputSnappedVertices);
                DMesh3 back = new DMesh3(p.Result);
                MeshTransforms.Scale(back, 1 / f);
                Assert.Equal("", Invariance.Difference(reference.Result, back));
            }
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
