using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using geometry3Sharp.Tests.SheetExt;
using Xunit;
using Roles = System.Collections.Generic.List<g3.ExactMeshBoolean.InputRole>;
using Status = gs.MeshSheetExtension.ExtensionStatus;
using Modes = gs.SolidBooleanPipeline.SheetExtensionModes;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline.SheetExtension (docs/design/SHEET_EXTENSION.md, section 6; pipeline option C): a sheet that the sheet role refuses with
    /// RimInsideWorkpiece because its rim ends a little short of the workpiece's surface is continued by MeshSheetExtension and cut again. The workpiece is the
    /// cube [0,10]^3, the sheet a grid that bends at x = 5 (z = 5 up to there, then rising with slope 0.3; normals up, so the kept part is the one above:
    /// 462.5, a wrong side or a missed strip gives another volume) whose rim ends a share of its own diagonal short of x = 10. All inputs are procedural.
    /// </summary>
    public class SolidBooleanPipelineSheetExtensionTests
    {
        const ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference;
        const ExactMeshBoolean.SheetRefusal Rim = ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece;
        const double Kept = 462.5;
        static readonly double[] Ys = { -2.3, -0.5, 1.3, 3.1, 4.9, 6.7, 8.5, 10.3, 12.1 };

        static string Names(SolidBooleanPipeline p) => string.Join(",", p.Report.Route.Select(s => s.Name));

        static DMesh3 Cube() => SheetGeometry.Box(0, 0, 0, 10, 10, 10);

        static DMesh3 Bent(double xe, double z0 = 5, bool up = true)
        {
            return SheetGeometry.Grid(new[] { -2.3, 1.1, 5.0, (5.0 + xe) / 2, xe }, Ys, (x, y) => z0 + 0.3 * Math.Max(0, x - 5), up);
        }

        // the sheet whose rim ends `share` of its own diagonal short of x = 10 (the diagonal depends on the end: a few rounds settle it)
        static DMesh3 Short(double share, double z0 = 5, bool up = true)
        {
            double xe = 10 - share * 18;
            for (int i = 0; i < 8; ++i)
                xe = 10 - share * Bent(xe, z0, up).GetBounds().Diagonal.Length;
            return Bent(xe, z0, up);
        }

        static Roles Stated(params ExactMeshBoolean.InputRole[] r) => new Roles(r);
        static readonly ExactMeshBoolean.InputRole S = ExactMeshBoolean.InputRole.Sheet, Sol = ExactMeshBoolean.InputRole.Solid;

        static SolidBooleanPipeline Make(IEnumerable<DMesh3> inputs, Modes mode, bool stated, Action<SolidBooleanPipeline> more = null)
        {
            var p = new SolidBooleanPipeline(inputs.ToList(), D) { SheetExtension = mode };
            if (stated) p.InputRoles = new Roles(inputs.Select((m, i) => i == 0 || m.IsClosed() ? Sol : S));
            more?.Invoke(p);
            return p;
        }

        static double Volume(DMesh3 m) => SheetRunner.Volume(m);

        static void AssertCut(SolidBooleanPipeline p, double volume, double tol = 1e-6)
        {
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            DMesh3 r = p.Result;
            Assert.NotNull(r);
            Assert.True(r.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(r));
            Assert.True(r.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r));
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            Assert.True(Math.Abs(Volume(r) - volume) <= tol * 1000, "volume " + Volume(r) + " against " + volume);
        }

        // the run without the option, to compare with
        static SolidBooleanPipeline Without(IEnumerable<DMesh3> inputs, bool stated, Action<SolidBooleanPipeline> more = null) => Make(inputs, Modes.Off, stated, more);

        // ---- without the option: today's refusal ----

        [Fact]
        public void Off_IsTheDefault_AndAShortSheetIsRefusedAsToday()
        {
            var stated = new SolidBooleanPipeline(new List<DMesh3> { Cube(), Short(0.005) }, D) { InputRoles = Stated(Sol, S) };
            Assert.Equal(Modes.Off, stated.SheetExtension);
            Assert.False(stated.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, stated.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.SheetRefused, stated.BooleanStatus);
            Assert.Equal(Rim, stated.SheetReason);
            Assert.Empty(stated.Report.SheetExtensions);
            Assert.Empty(stated.Report.Route);
            var routed = new SolidBooleanPipeline(new List<DMesh3> { Cube(), Short(0.005) }, D);
            Assert.False(routed.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, routed.Status);
            Assert.Equal("SheetRole,Grid,Fallback,Refused", Names(routed));
            Assert.Empty(routed.Report.SheetExtensions);
        }

        // ---- StatedSheets: a stated short sheet is continued and cuts ----

        [Theory]
        [InlineData(0.001)]
        [InlineData(0.005)]
        [InlineData(0.02)]
        public void StatedSheets_ContinueAStatedShortSheet_AndCutWithTheAnalyticVolume(double share)
        {
            DMesh3 sheet = Short(share);
            var p = Make(new[] { Cube(), sheet }, Modes.StatedSheets, true);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertCut(p, Kept);
            Assert.Equal("SheetExtension", Names(p));
            SolidBooleanRouteStep step = p.Report.Route[0];
            Assert.Equal(1, step.Input);
            Assert.Equal(Rim, step.SheetReason);
            Assert.Contains("stated in InputRoles", step.Reason);
            Assert.Contains("continued up to the workpiece's surface", step.Change);
            Assert.Single(p.Report.SheetExtensions);
            SolidBooleanSheetExtension e = p.Report.SheetExtensions[0];
            Assert.Equal(1, e.Input);
            Assert.True(e.Stated);
            Assert.Equal(Status.Success, e.Status);
            Assert.True(e.Applied);
            Assert.True(e.InResult);
            Assert.Equal("", e.Reason);
            Assert.InRange(e.DepthRelative, share * 0.99, share * 1.1);
            Assert.Equal(e.Depth / e.Diagonal, e.DepthRelative);
            Assert.True(e.AddedTriangles > 0);
            // the strip inside the workpiece: the depth along the rays times the 10 of the cube's width
            Assert.InRange(e.AddedArea, 0.9 * e.Depth * 10, 1.1 * e.Depth * 10);
            Assert.True(p.Report.SheetExtensionMilliseconds >= 0);
            // the sheet as given is not changed
            Assert.Equal(Short(share).TriangleCount, sheet.TriangleCount);
        }

        // ---- stated or routed ----

        [Fact]
        public void StatedSheets_DoesNotExtendAnAutoRoutedTool_ButAlsoAutoRoutedToolsDoes()
        {
            foreach (Modes mode in new[] { Modes.Off, Modes.StatedSheets }) {
                var no = Make(new[] { Cube(), Short(0.005) }, mode, false);
                Assert.False(no.Compute(), mode.ToString());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, no.Status);
                Assert.Equal("SheetRole,Grid,Fallback,Refused", Names(no));
                Assert.Empty(no.Report.SheetExtensions);
            }
            var p = Make(new[] { Cube(), Short(0.005) }, Modes.AlsoAutoRoutedTools, false);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertCut(p, Kept);
            // (the grid retry comes first and does not help; then the extension; the sheet is routed again for the run with the extended sheet)
            Assert.Equal("SheetRole,Grid,SheetExtension,SheetRole", Names(p));
            SolidBooleanSheetExtension e = p.Report.SheetExtensions.Single();
            Assert.False(e.Stated);
            Assert.True(e.InResult);
            Assert.Contains("routed to the sheet role", p.Report.Route[2].Reason);
            // a sheet that is stated is extended in this mode too
            var stated = Make(new[] { Cube(), Short(0.005) }, Modes.AlsoAutoRoutedTools, true);
            Assert.True(stated.Compute(), stated.Error);
            AssertCut(stated, Kept);
            Assert.True(stated.Report.SheetExtensions.Single().Stated);
        }

        // ---- what stays refused: the run continues as without the option ----

        // the result of two runs is the same: status, error, route (without the extension step), and the mesh bit for bit
        static void AssertSameAsWithout(SolidBooleanPipeline with, SolidBooleanPipeline without)
        {
            Assert.Equal(without.Status, with.Status);
            Assert.Equal(without.BooleanStatus, with.BooleanStatus);
            Assert.Equal(without.SheetReason, with.SheetReason);
            Assert.Equal(without.Error, with.Error);
            Assert.Equal(string.Join(",", without.Report.Route.Select(s => s.Name)), string.Join(",", with.Report.Route.Where(s => s.Name != "SheetExtension").Select(s => s.Name)));
            Assert.Equal(without.Result == null, with.Result == null);
            if (without.Result != null)
                Assert.Equal("", Invariance.Difference(without.Result, with.Result));
            Assert.Equal(without.Report.ResultKind, with.Report.ResultKind);
            // every public field of the report except the clock, the route and the extension's own entries; and the steps other than SheetExtension, field by field
            foreach (System.Reflection.FieldInfo f in typeof(SolidBooleanReport).GetFields()) {
                if (f.Name.EndsWith("Milliseconds") || f.Name == "Route" || f.Name == "SheetExtensions")
                    continue;
                Assert.Equal(Flat(f.GetValue(without.Report)), Flat(f.GetValue(with.Report)));
            }
            List<SolidBooleanRouteStep> a = without.Report.Route, b = with.Report.Route.Where(s => s.Name != "SheetExtension").ToList();
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; ++i) {
                Assert.Equal(a[i].Name, b[i].Name);
                Assert.Equal(a[i].Input, b[i].Input);
                Assert.Equal(a[i].Reason, b[i].Reason);
                Assert.Equal(a[i].Change, b[i].Change);
                Assert.Equal(a[i].SheetReason, b[i].SheetReason);
                Assert.Equal(a[i].Grid, b[i].Grid);
            }
        }

        static string Flat(object v)
        {
            if (v == null) return "null";
            if (v is string || v.GetType().IsPrimitive || v.GetType().IsEnum) return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            if (v is System.Collections.IEnumerable e) return "[" + string.Join(";", e.Cast<object>().Select(Flat)) + "]";
            return string.Join(",", v.GetType().GetFields().Where(f => !f.Name.EndsWith("Milliseconds")).Select(f => f.Name + "=" + Flat(f.GetValue(v))));
        }

        [Theory]
        [InlineData(0.08, false)]
        [InlineData(0.08, true)]
        public void ASheetBeyondTheDepthLimit_IsRefused_WithTheHelpersReason_AndTheRunGoesOnAsWithoutTheOption(double share, bool stated)
        {
            Modes mode = Modes.AlsoAutoRoutedTools;
            var p = Make(new[] { Cube(), Short(share) }, mode, stated);
            var without = Without(new[] { Cube(), Short(share) }, stated);
            Assert.False(p.Compute());
            Assert.False(without.Compute());
            AssertSameAsWithout(p, without);
            SolidBooleanSheetExtension e = p.Report.SheetExtensions.Single();
            Assert.Equal(Status.DepthAboveLimit, e.Status);
            Assert.False(e.Applied);
            Assert.False(e.InResult);
            Assert.Contains("of the diagonal", e.Reason);
            Assert.InRange(e.DepthRelative, share * 0.97, share * 1.15);
            Assert.Equal(0, e.AddedArea);
            SolidBooleanRouteStep step = p.Report.Route.Single(s => s.Name == "SheetExtension");
            Assert.Contains("refused: DepthAboveLimit", step.Change);
            Assert.Contains("the run goes on without it", step.Change);
            // (the steps of a run that was not done are not kept)
            Assert.Equal(stated ? "SheetExtension" : "SheetRole,Grid,SheetExtension,Fallback,Refused", Names(p));
        }

        [Fact]
        public void ARefusedExtension_StillLetsTheHoleFillFallbackRun()
        {
            // the open tool, refused as a sheet and not extended (too deep), goes to the repair of InputRepair as without the option
            Action<SolidBooleanPipeline> repair = q => q.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair;
            var p = Make(new[] { Cube(), Short(0.08) }, Modes.AlsoAutoRoutedTools, false, repair);
            var without = Without(new[] { Cube(), Short(0.08) }, false, repair);
            bool a = p.Compute(), b = without.Compute();
            Assert.Equal(b, a);
            AssertSameAsWithout(p, without);
            Assert.Contains("Fallback", Names(p));
            Assert.Contains("InputRepair", Names(p));
            Assert.Equal(Status.DepthAboveLimit, p.Report.SheetExtensions.Single().Status);
        }

        [Fact]
        public void AStripThroughAnotherTool_IsRefusedByTheHelper_AndTheRunGoesOnAsWithout()
        {
            // a small solid tool in the gap between the rim and the surface, where the strip would pass
            DMesh3 tool = SheetGeometry.Box(9.93, 4, 6.0, 10.5, 6, 7.0);
            var p = Make(new[] { Cube(), Short(0.005), tool }, Modes.StatedSheets, true);
            var without = Without(new[] { Cube(), Short(0.005), tool }, true);
            Assert.False(p.Compute());
            Assert.False(without.Compute());
            AssertSameAsWithout(p, without);
            Assert.Equal(Rim, p.SheetReason);
            SolidBooleanSheetExtension e = p.Report.SheetExtensions.Single();
            Assert.Equal(Status.MeetsOtherTool, e.Status);
            Assert.False(e.Applied);
            // the same sheet without that tool is continued
            var free = Make(new[] { Cube(), Short(0.005), SheetGeometry.Box(9.93, 4, 8.0, 10.5, 6, 9.0) }, Modes.StatedSheets, true);
            Assert.True(free.Compute(), free.Error);
            Assert.Single(free.Report.SheetExtensions);
            Assert.True(free.Report.SheetExtensions[0].InResult);
        }

        [Fact]
        public void AStripThroughAnotherSheet_IsRefusedByTheHelper_AndTheRunGoesOnAsWithout()
        {
            // a second sheet that cuts by itself (its rim is outside the cube) and crosses the gap between the rim of the first and the surface
            var wall = new DMesh3();
            int a = wall.AppendVertex(new Vector3d(9.95, -2, -2)), b = wall.AppendVertex(new Vector3d(9.95, 12, -2)), c = wall.AppendVertex(new Vector3d(9.95, 12, 12)), d = wall.AppendVertex(new Vector3d(9.95, -2, 12));
            wall.AppendTriangle(a, b, c);
            wall.AppendTriangle(a, c, d);
            var p = Make(new[] { Cube(), Short(0.005), wall }, Modes.StatedSheets, true);
            var without = Without(new[] { Cube(), Short(0.005), wall }, true);
            Assert.False(p.Compute());
            Assert.False(without.Compute());
            AssertSameAsWithout(p, without);
            Assert.Equal(Status.MeetsOtherTool, p.Report.SheetExtensions.Single().Status);
        }

        // ---- several sheets ----

        [Fact]
        public void TwoRefusedSheets_AreEachExtendedOnce_InAtMostNPlusOneCuts()
        {
            int configured = 0;
            var p = Make(new[] { Cube(), Short(0.005, 3, true), Short(0.005, 7, false) }, Modes.StatedSheets, true, q => {
                q.AutomaticSteps = SolidBooleanPipeline.AutomaticStepModes.None;
                q.ConfigureBoolean = op => ++configured;
            });
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertCut(p, 400);
            Assert.Equal(3, configured);                                       // the cut, the cut with one sheet extended, the cut with both
            Assert.Equal(new[] { 1, 2 }, p.Report.SheetExtensions.Select(e => e.Input).ToArray());
            Assert.All(p.Report.SheetExtensions, e => { Assert.True(e.Applied); Assert.True(e.InResult); Assert.Equal(Status.Success, e.Status); });
            Assert.Equal("SheetExtension,SheetExtension", Names(p));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void OnlyTheRefusedSheetIsContinued_ASheetThatCutsIsNotTouched(bool stated)
        {
            // sheet 1 reaches through the cube (it cuts as it is), sheet 2 ends short
            DMesh3 through = SheetGeometry.Grid(new[] { -2.3, 5.0, 12.1 }, Ys, (x, y) => 3 + 0.3 * Math.Max(0, x - 5), true);
            var p = Make(new[] { Cube(), through, Short(0.005, 7, false) }, Modes.AlsoAutoRoutedTools, stated);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertCut(p, 400);
            SolidBooleanSheetExtension e = p.Report.SheetExtensions.Single();
            Assert.Equal(2, e.Input);
            Assert.Equal(stated, e.Stated);
            Assert.True(e.InResult);
        }

        [Fact]
        public void IfTheSecondSheetIsRefused_TheWholeExtensionIsGivenUp_AsWithoutTheOption()
        {
            // sheet 1 is extended and cuts; sheet 2 is too deep: the run with sheet 1 extended names sheet 2, the helper refuses it, and the first failure is the result
            var inputs = new[] { Cube(), Short(0.005, 3, true), Short(0.08, 7, false) };
            var p = Make(inputs, Modes.StatedSheets, true);
            var without = Without(new[] { Cube(), Short(0.005, 3, true), Short(0.08, 7, false) }, true);
            Assert.False(p.Compute());
            Assert.False(without.Compute());
            AssertSameAsWithout(p, without);
            Assert.Equal(2, p.Report.SheetExtensions.Count);
            Assert.True(p.Report.SheetExtensions[0].Applied);
            Assert.False(p.Report.SheetExtensions[0].InResult);
            Assert.Equal(Status.DepthAboveLimit, p.Report.SheetExtensions[1].Status);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ASheetThatIsRefusedAgainAfterItsExtension_IsNotExtendedAgain(bool stated)
        {
            // ConfigureBoolean puts the sheet as given back into every boolean (a stand-in for a run that does not accept what the helper accepted)
            DMesh3 original = Short(0.005);
            int configured = 0;
            Modes mode = Modes.AlsoAutoRoutedTools;
            var p = Make(new[] { Cube(), Short(0.005) }, mode, stated, q => {
                q.AutomaticSteps = SolidBooleanPipeline.AutomaticStepModes.None;
                q.ConfigureBoolean = op => { ++configured; op.Meshes[1] = original; };
            });
            var without = Without(new[] { Cube(), Short(0.005) }, stated, q => q.AutomaticSteps = SolidBooleanPipeline.AutomaticStepModes.None);
            Assert.False(p.Compute());
            Assert.False(without.Compute());
            Assert.Equal(2 + (stated ? 0 : 1), configured);                 // the first cut, the cut with the extended sheet, and for a routed tool the fallback
            SolidBooleanSheetExtension e = p.Report.SheetExtensions.Single();
            Assert.True(e.Applied);
            Assert.False(e.InResult);
            AssertSameAsWithout(p, without);
        }

        // ---- the order: the grid retry answers a flush rim first ----

        const double T = 1.2e6;

        // the block [T, T+10]^2 x [T, T+5] and an open box-shaped cutter whose rim at the top face sits a few ulps inside it: the flush rim of the grid retry
        static DMesh3 FlushBlock() => SheetGeometry.Box(T, T, T, T + 10, T + 10, T + 5);

        static DMesh3 FlushCutter()
        {
            var m = new DMesh3();
            Vector2d[] poly = { new Vector2d(3, 3), new Vector2d(7, 3), new Vector2d(7, 7), new Vector2d(3, 7) };
            var low = new int[4]; var rim = new int[4];
            for (int i = 0; i < 4; ++i) {
                low[i] = m.AppendVertex(new Vector3d(T + poly[i].x, T + poly[i].y, T + 2));
                double top = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(T + 5) - (1 + i % 3));
                rim[i] = m.AppendVertex(new Vector3d(T + poly[i].x, T + poly[i].y, top));
            }
            for (int i = 0; i < 4; ++i) {
                int j = (i + 1) % 4;
                m.AppendTriangle(low[i], low[j], rim[j]);
                m.AppendTriangle(low[i], rim[j], rim[i]);
            }
            m.AppendTriangle(low[0], low[2], low[1]);
            m.AppendTriangle(low[0], low[3], low[2]);
            return m;
        }

        [Fact]
        public void AFlushRim_IsCuredByTheGridRetryFirst_TheExtensionIsNotReached()
        {
            var p = new SolidBooleanPipeline(new List<DMesh3> { FlushBlock(), FlushCutter() }, D) { SheetExtension = Modes.AlsoAutoRoutedTools };
            var without = new SolidBooleanPipeline(new List<DMesh3> { FlushBlock(), FlushCutter() }, D);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.True(without.Compute(), without.Error);
            Assert.Equal("SheetRole,Grid,SheetRole", Names(p));
            Assert.Empty(p.Report.SheetExtensions);
            Assert.Equal(0, p.Report.SheetExtensionMilliseconds);
            Assert.Equal("", Invariance.Difference(without.Result, p.Result));
        }

        [Fact]
        public void AStatedSheet_IsRetriedOnTheGridFirst_WhenAutomaticStepsSaysSo_AndThenExtended()
        {
            var p = Make(new[] { Cube(), Short(0.005) }, Modes.StatedSheets, true, q => q.AutomaticSteps = SolidBooleanPipeline.AutomaticStepModes.Grid);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertCut(p, Kept);
            Assert.Equal("Grid,SheetExtension", Names(p));
            Assert.Equal(Rim, p.Report.Route[0].SheetReason);
            Assert.Contains("it failed too", p.Report.Route[0].Change);
        }

        // ---- the verification ----

        [Fact]
        public void AnExtendedCut_PassesNoInventedSurface_WithTheStripAsAReference_AndTheReportSaysWhatWasAdded()
        {
            var p = Make(new[] { Cube(), Short(0.005) }, Modes.StatedSheets, true, q => q.Verification = SolidBooleanPipeline.VerificationModes.NoInventedSurface);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertCut(p, Kept);
            Assert.Equal(0, p.Report.InventedSurfaceShare);
            Assert.Equal(0, p.Report.InventedTriangles);
            Assert.Equal("SheetExtension,Verification", Names(p));
            SolidBooleanSheetExtension e = p.Report.SheetExtensions.Single();
            Assert.True(e.InResult);
            Assert.True(e.AddedArea > 0);
            SolidBooleanRouteStep v = p.Report.Route[1];
            Assert.Contains("the strip of the extended sheet(s) is a reference surface", v.Change);
            Assert.Contains("passed", v.Change);
            // the strip is a real part of the result's surface: a result without it as a reference would have invented area of about the strip's
            Assert.True(e.AddedArea > 0.5 * e.Depth * 10);
        }

        [Fact]
        public void ARealInventedSurface_IsStillCaught_NextToAnExtendedSheet()
        {
            // the check keeps its meaning: with a tolerance of zero share, a reference that is not there is invented surface (the sheet given as the only reference)
            DMesh3 sheet = Short(0.005);
            var p = Make(new[] { Cube(), sheet }, Modes.StatedSheets, true, q => q.Verification = SolidBooleanPipeline.VerificationModes.NoInventedSurface);
            Assert.True(p.Compute(), p.Error);
            double tol = p.Report.InventedSurfaceTolerance;
            // measured against the inputs as given alone, the cut surface of the strip is farther than the tolerance from every one of them
            var m = SolidBooleanPipeline.InventedSurfaceOf(p.Result, new List<DMesh3> { Cube(), sheet }, tol, System.Threading.CancellationToken.None);
            Assert.True(m.Share > 0, "the strip is invented surface for the inputs as passed");
            Assert.InRange(m.Area, 0.5 * p.Report.SheetExtensions[0].AddedArea, 2 * p.Report.SheetExtensions[0].AddedArea);
        }

        // ---- MinThickness and thin inputs: measured on the inputs as given ----

        [Fact]
        public void MinThickness_MeasuresTheSheetAsGiven_NotWithTheStrip()
        {
            Action<SolidBooleanPipeline> thin = q => q.MinThickness = 1;
            var p = Make(new[] { Cube(), Short(0.005) }, Modes.AlsoAutoRoutedTools, false, thin);
            var without = Without(new[] { Cube(), Short(0.005) }, false, thin);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.False(without.Compute());
            Assert.Equal(Short(0.005).TriangleCount, p.Report.InputTriangles[1]);
            SolidBooleanThinInput a = p.Report.ThinInputs.Single(t => t.Index == 1), b = without.Report.ThinInputs.Single(t => t.Index == 1);
            Assert.Equal(b.Area, a.Area);
            Assert.Equal(b.Volume, a.Volume);
            Assert.Equal(b.Thickness, a.Thickness);
            Assert.False(a.Closed);
        }

        // ---- UncutSide is passed through ----

        [Fact]
        public void UncutSide_IsPassedToTheHelpersCut()
        {
            // A workpiece of two shells; the sheet touches only the cube. With several shells the sheet role does not decide an untouched part even where UncutSide
            // names a side (its refusal says which side it was given), so the helper's last check refuses; the refusal text shows that the helper got the pipeline's setting
            DMesh3 two = SheetGeometry.Union(Cube(), SheetGeometry.Box(20, 0, 0, 30, 10, 10));
            foreach (ExactMeshBoolean.SheetUncutSides side in new[] { ExactMeshBoolean.SheetUncutSides.Refuse, ExactMeshBoolean.SheetUncutSides.Outside, ExactMeshBoolean.SheetUncutSides.Inside }) {
                var p = Make(new[] { two, Short(0.005) }, Modes.StatedSheets, true, q => q.UncutSide = side);
                Assert.False(p.Compute());
                SolidBooleanSheetExtension e = p.Report.SheetExtensions.Single();
                Assert.Equal(Status.SheetRoleRefused, e.Status);
                Assert.Contains("UncutSide: " + side + ",", e.Reason);
                Assert.Equal(Rim, p.SheetReason);
            }
        }

        // ---- a value that is not defined ----

        [Fact]
        public void AnUndefinedMode_IsInvalidInput()
        {
            foreach (int v in new[] { -1, 3, 7 }) {
                var p = Make(new[] { Cube(), Short(0.005) }, (Modes)v, true);
                Assert.False(p.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
                Assert.Contains("SheetExtension is not a defined value", p.Error);
            }
        }

        // ---- reuse ----

        [Fact]
        public void ComputeTwice_OnOneInstance_GivesTheSameResult_AndOneEntryPerTry()
        {
            var p = Make(new[] { Cube(), Short(0.005) }, Modes.StatedSheets, true);
            Assert.True(p.Compute(), p.Error);
            DMesh3 first = p.Result;
            string route = Names(p);
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.Difference(first, p.Result));
            Assert.Equal(route, Names(p));
            Assert.Single(p.Report.SheetExtensions);
            Assert.Equal(2, p.Report.Route.Count + 1);
            // and with another mode on the same instance
            p.SheetExtension = Modes.Off;
            Assert.False(p.Compute());
            Assert.Empty(p.Report.SheetExtensions);
            Assert.Empty(p.Report.Route);
            p.SheetExtension = Modes.StatedSheets;
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.Difference(first, p.Result));
            Assert.Single(p.Report.SheetExtensions);
        }

        [Fact]
        public void ComputeTwice_AfterARefusedExtension_GivesTheSameRefusal()
        {
            var p = Make(new[] { Cube(), Short(0.08) }, Modes.StatedSheets, true);
            Assert.False(p.Compute());
            string error = p.Error;
            Assert.False(p.Compute());
            Assert.Equal(error, p.Error);
            Assert.Single(p.Report.SheetExtensions);
            Assert.Single(p.Report.Route);
        }

        // ---- invariance over the similarities ----

        static IEnumerable<DMesh3> InFrame(Similarity s, params DMesh3[] meshes) => meshes.Select(m => Invariance.Transformed(m, s));

        public static IEnumerable<object[]> Frames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(Frames))]
        public void Result_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            var reference = Make(new[] { Cube(), Short(0.005) }, Modes.StatedSheets, true);
            Assert.True(reference.Compute(), reference.Error);
            var p = Make(InFrame(s, Cube(), Short(0.005)).ToArray(), Modes.StatedSheets, true);
            Assert.True(p.Compute(), s.Name + ": " + p.Status + " " + p.Error);
            Assert.Equal(Names(reference), Names(p));
            SolidBooleanSheetExtension e = p.Report.SheetExtensions.Single(), r = reference.Report.SheetExtensions.Single();
            Assert.True(e.Applied && e.InResult);
            DMesh3 back = Invariance.MappedBack(p.Result, s);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(p.Result));
            Assert.True(p.Result.IsClosed());
            Assert.True(p.Report.Clean);
            double tol = s.Translation != Vector3d.Zero || s.IsRotated ? 1e-5 : 1e-9;
            Assert.True(Math.Abs(Volume(back) - Kept) <= tol * 1000, s.Name + ": " + Volume(back));
            if (s.IsExact) {
                // a scaling by 2^k: the result is the reference's, scaled, every vertex and triangle bit for bit, and so are the numbers of the report
                Assert.Equal("", Invariance.Difference(reference.Result, back));
                Assert.Equal(r.DepthRelative, e.DepthRelative);
                Assert.Equal(r.Depth * s.Scale, e.Depth);
                Assert.Equal(r.AddedTriangles, e.AddedTriangles);
                Assert.Equal(r.AddedArea * s.Scale * s.Scale, e.AddedArea, 12);
            }
        }

        [Theory]
        [MemberData(nameof(Frames))]
        public void ARimAtEightPercent_IsRefusedInEveryFrame_AndARimAtHalfAPercentIsExtendedInAutoRoutedMode(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            var deep = Make(InFrame(s, Cube(), Short(0.08)).ToArray(), Modes.AlsoAutoRoutedTools, false);
            Assert.False(deep.Compute());
            Assert.Equal(Status.DepthAboveLimit, deep.Report.SheetExtensions.Single().Status);
            var shallow = Make(InFrame(s, Cube(), Short(0.005)).ToArray(), Modes.AlsoAutoRoutedTools, false);
            Assert.True(shallow.Compute(), s.Name + ": " + shallow.Error);
            Assert.Equal("SheetRole,Grid,SheetExtension,SheetRole", Names(shallow));
        }

        // The depth limit is a share of the sheet's bounding box diagonal, which a rotation changes: a rim at 5.5 % of the diagonal in the frame of the axes is above the 5 %,
        // and the box of the rotated flat sheet is larger, so in a rotated frame the same depth is a smaller share and the sheet is continued (a tilted or curved sheet goes the
        // other way). Documented, pinned here
        [Fact]
        public void NearTheDepthLimit_TheOutcomeCanDependOnTheFrame_ButNeverTheCutOfAnExtendedSheet()
        {
            double share = 0.055;
            var identity = Make(new[] { Cube(), Short(share) }, Modes.StatedSheets, true);
            Assert.False(identity.Compute());
            SolidBooleanSheetExtension e = identity.Report.SheetExtensions.Single();
            Assert.Equal(Status.DepthAboveLimit, e.Status);
            Assert.True(e.DepthRelative > 0.05);
            // the same sheet and workpiece rotated: the box of a rotated flat sheet is larger, the same depth is a smaller share of its diagonal, and the sheet is continued
            var rotated = Make(InFrame(Similarity.Rotated, Cube(), Short(share)).ToArray(), Modes.StatedSheets, true);
            Assert.True(rotated.Compute(), rotated.Error);
            SolidBooleanSheetExtension r = rotated.Report.SheetExtensions.Single();
            Assert.True(r.InResult);
            Assert.True(r.DepthRelative < 0.05, r.DepthRelative.ToString());
            Assert.True(r.Diagonal > 1.1 * e.Diagonal);
            // the absolute depth is the same in both frames; where the sheet is continued the cut is the analytic one in every frame
            Assert.InRange(r.Depth, e.Depth * 0.999, e.Depth * 1.001);
            Assert.True(Math.Abs(Volume(Invariance.MappedBack(rotated.Result, Similarity.Rotated)) - Kept) <= 1e-5 * 1000);
            // with a limit that the frame cannot change (2^k scalings keep the diagonal's ratio) the outcome is the same
            var scaled = Make(InFrame(Similarity.Pow2Large, Cube(), Short(share)).ToArray(), Modes.StatedSheets, true);
            Assert.False(scaled.Compute());
            Assert.Equal(Status.DepthAboveLimit, scaled.Report.SheetExtensions.Single().Status);
        }

        // ---- repeated runs ----

        // a short sheet that another sheet's strip would cut through: a wall at x = 9.95 that is itself short at the top (z up to 9.95)
        static DMesh3 ShortWall()
        {
            var wall = new DMesh3();
            int a = wall.AppendVertex(new Vector3d(9.95, -2, -2)), b = wall.AppendVertex(new Vector3d(9.95, 12, -2)), c = wall.AppendVertex(new Vector3d(9.95, 12, 9.95)), d = wall.AppendVertex(new Vector3d(9.95, -2, 9.95));
            wall.AppendTriangle(a, b, c);
            wall.AppendTriangle(a, c, d);
            return wall;
        }

        [Fact]
        public void TheDecisionDoesNotDependOnTheOrderOfTheSheets_AStripThatMeetsAnotherShortSheetRefusesTheWholeExtension()
        {
            var one = Make(new[] { Cube(), Short(0.005), ShortWall() }, Modes.StatedSheets, true);
            var two = Make(new[] { Cube(), ShortWall(), Short(0.005) }, Modes.StatedSheets, true);
            Assert.False(one.Compute());
            Assert.False(two.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, one.Status);
            Assert.Equal(one.Status, two.Status);
            Assert.Equal(Rim, one.SheetReason);
            Assert.Equal(Rim, two.SheetReason);
            Assert.All(one.Report.SheetExtensions.Concat(two.Report.SheetExtensions), e => Assert.False(e.InResult));
            // order one: both are extended by the helper, and the final check refuses; order two: the helper refuses the second at once
            Assert.Equal(2, one.Report.SheetExtensions.Count(e => e.Applied));
            Assert.Contains("meets", one.Report.Route.Last().Change);
            Assert.Equal(Status.MeetsOtherTool, two.Report.SheetExtensions.Last().Status);
        }

        [Fact]
        public void ALidlessBoxTool_IsContinuedAndCutAsAnOpenPocket_InAutoRoutedMode_WhereTheRepairWouldGiveACavity()
        {
            // pinned behaviour of AlsoAutoRoutedTools (see the enum): a closed body that lost its top face just under the surface is continued to the surface
            var tool = new DMesh3(SheetGeometry.Box(3, 3, 2, 7, 7, 9.95));
            foreach (int t in tool.TriangleIndices().ToArray()) {
                Vector3d c = tool.GetTriCentroid(t);
                if (c.z > 9.9) tool.RemoveTriangle(t, true);
            }
            Assert.False(tool.IsClosed());
            var repaired = Make(new[] { Cube(), tool }, Modes.Off, false, q => q.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair);
            Assert.True(repaired.Compute(), repaired.Error);
            Assert.InRange(Volume(repaired.Result), 872.7, 872.9);
            var p = Make(new[] { Cube(), tool }, Modes.AlsoAutoRoutedTools, false, q => q.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair);
            Assert.True(p.Compute(), p.Error);
            AssertCut(p, 872.0);
            SolidBooleanSheetExtension e = p.Report.SheetExtensions.Single();
            Assert.False(e.Stated);
            Assert.True(e.AddedArea > 0);
            // StatedSheets does not do it: the tool goes to the repair
            var stated = Make(new[] { Cube(), tool }, Modes.StatedSheets, false, q => q.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair);
            Assert.True(stated.Compute(), stated.Error);
            Assert.Empty(stated.Report.SheetExtensions);
            Assert.InRange(Volume(stated.Result), 872.7, 872.9);
        }

        [Fact]
        public void TheHelperTime_IsInTheReport_AlsoWhereTheRunWentOnToTheFallback()
        {
            // the too-deep routed tool on a round workpiece (the helper takes a few milliseconds there): the helper ran and refused, the fallback's run has its own report; the time is carried over
            DMesh3 ball = SheetGeometry.Sphere(new Vector3d(0, 0, 0), 5, 40);
            DMesh3 sheet = SheetGeometry.Grid(new[] { -6.3, -2, 0, 2, 3.5 }, new[] { -6.3, -3, 0, 3, 6.3 }, (x, y) => 0, true);
            var p = Make(new[] { ball, sheet }, Modes.AlsoAutoRoutedTools, false, q => q.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair);
            p.Compute();
            Assert.Contains("Fallback", Names(p));
            Assert.Equal(Status.DepthAboveLimit, p.Report.SheetExtensions.Single().Status);
            Assert.True(p.Report.SheetExtensionMilliseconds > 0, "the helper time is carried over to the report of the fallback run");
        }

        // A refused extension gives the run without the option, bit for bit: the result mesh, every report field but the clock, and every route step but SheetExtension
        // (AssertSameAsWithout). The cases: too deep with a repair that succeeds (a non-empty result), the strip through a tool, the two-sheet strip check in both orders
        [Fact]
        public void ARefusedExtension_GivesTheRunWithoutTheOption_BitForBit_AlsoWhereTheFallbackSucceeds()
        {
            Action<SolidBooleanPipeline> repair = q => q.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair;
            Func<DMesh3> lidless = () => {
                var t = new DMesh3(SheetGeometry.Box(3, 3, 2, 7, 7, 8));
                foreach (int id in t.TriangleIndices().ToArray()) if (t.GetTriCentroid(id).z > 7.9) t.RemoveTriangle(id, true);
                return t;
            };
            var p = Make(new[] { Cube(), lidless() }, Modes.AlsoAutoRoutedTools, false, repair);
            var without = Without(new[] { Cube(), lidless() }, false, repair);
            Assert.True(p.Compute(), p.Error);
            Assert.True(without.Compute(), without.Error);
            Assert.NotNull(p.Result);
            Assert.Equal(Status.DepthAboveLimit, p.Report.SheetExtensions.Single().Status);
            AssertSameAsWithout(p, without);
            Assert.InRange(Volume(p.Result), 1000 - 16 * 6 - 0.2, 1000 - 16 * 6 + 0.2);     // the closed cavity of the repair
        }

        [Fact]
        public void TheStripCheckRefusal_GivesTheRunWithoutTheOption_BitForBit_InBothOrders()
        {
            foreach (bool wallFirst in new[] { false, true }) {
                Func<DMesh3[]> make = () => wallFirst ? new[] { Cube(), ShortWall(), Short(0.005) } : new[] { Cube(), Short(0.005), ShortWall() };
                var p = Make(make(), Modes.StatedSheets, true);
                var without = Without(make(), true);
                Assert.False(p.Compute());
                Assert.False(without.Compute());
                AssertSameAsWithout(p, without);
            }
        }

        // M18 (all other tools given the Solid role to the helper) is equivalent in what the pipeline returns: the helper only decides whether to extend, and an open mesh as a
        // Solid in its cut (AllowOpenMeshes) cuts the same and is as clean as the sheet; no case was found where the roles differ. Pinned: a fine open sheet next to a short one
        [Fact]
        public void AFineOpenSheetNextToAShortOne_IsNoObstacle_ForTheHelper()
        {
            var open = new DMesh3(SheetGeometry.Box(3, 3, 1, 7, 7, 12));
            foreach (int id in open.TriangleIndices().ToArray()) if (open.GetTriCentroid(id).z > 11.9) open.RemoveTriangle(id, true);
            var p = Make(new[] { Cube(), Short(0.005), open }, Modes.StatedSheets, true);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.True(p.Report.SheetExtensions.Single().InResult);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(p.Result));
        }

        [Fact]
        public void WithSeparateSelfContacts_TheStepRunsAfterTheExtendedBoolean()
        {
            var p = Make(new[] { Cube(), Short(0.005) }, Modes.StatedSheets, true, q => q.SeparateSelfContacts = 1e-6);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            AssertCut(p, Kept);
            Assert.True(p.Report.SheetExtensions.Single().InResult);
            Assert.Equal(0, p.Report.SelfContactsLeft);
        }

        [Fact]
        public void RepeatedRuns_GiveTheSameResult()
        {
            DMesh3 first = null;
            for (int i = 0; i < 3; ++i) {
                var p = Make(new[] { Cube(), Short(0.005) }, Modes.StatedSheets, true);
                Assert.True(p.Compute(), p.Error);
                if (first == null) first = p.Result;
                else Assert.Equal("", Invariance.Difference(first, p.Result));
            }
        }
    }
}
