using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;
using geometry3Sharp.Tests.SheetExt;
using Xunit;
using Status = gs.MeshSheetExtension.ExtensionStatus;
using Rules = gs.MeshSheetExtension.Rules;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The findings of the independent review of the helper (docs/design/SHEET_EXTENSION.md, "Under automatic use"): the cost of the surface check, the exact
    /// classification of the rim, the gap behind an exit, the cleanliness of the verification cut, the attributes of the new triangles and vertices, and the mutants that survived
    /// the first tests. Procedural inputs.
    /// </summary>
    public class MeshSheetExtensionReviewTests
    {
        static readonly double[] YsWide = { -2.3, -0.5, 1.3, 3.1, 4.9, 6.7, 8.5, 10.3, 12.1 };

        static MeshSheetExtension Make(DMesh3 sheet, DMesh3 w, Action<MeshSheetExtension> set = null)
        {
            var op = new MeshSheetExtension { Sheet = sheet, Workpiece = w };
            set?.Invoke(op);
            return op;
        }

        static DMesh3 Cube() => SheetGeometry.Box(0, 0, 0, 10, 10, 10);

        // ---- 1. the cost of the surface check ---------------------------------------------------------------------------------------

        [Fact]
        public void Diagnosis_OfASheetOf80000Triangles_GrowsLinearly()
        {
            // protects against the quadratic check of the sheet's edges (a Dictionary<long, int> keyed a << 32 | b hashes a ^ b: the ids of a grid collide, 3.3 s at 80,000 triangles).
            // The limit is a ratio between two sizes of one run, no absolute time (that would fail on a loaded machine): 8 against 4 for linear and 16 for quadratic
            DMesh3 sphere = SheetGeometry.Sphere(Vector3d.Zero, 5, 14);
            double[] ms = new double[2];
            int k = 0;
            foreach (int n in new[] { 100, 200 }) {
                DMesh3 sheet = SheetGeometry.Grid(SheetGeometry.Lin(-8, 8, n), SheetGeometry.Lin(-8, 4.5, n), (x, y) => 0.5, true);
                Assert.Equal(2 * n * n, sheet.TriangleCount);
                var op = Make(sheet, sphere);
                new MeshSheetExtension { Sheet = sheet, Workpiece = sphere }.Diagnose();       // warm-up
                var sw = Stopwatch.StartNew();
                Assert.True(op.Diagnose());
                ms[k++] = sw.Elapsed.TotalMilliseconds;
            }
            Assert.True(ms[1] < 8 * Math.Max(ms[0], 20), "20,000 triangles " + ms[0] + " ms, 80,000 triangles " + ms[1] + " ms: not linear");
        }

        // ---- 2. the rim choice is exact: no dead zone between 64 rounding units and 1e-7 ------------------------------------------

        static (DMesh3 w, DMesh3 sheet) ShortBy(double shortBy, double shift)
        {
            Func<Vector3d, Vector3d> f = p => p + new Vector3d(shift, shift, shift);
            DMesh3 sheet = SheetGeometry.Grid(new[] { -2.3, 1.1, 5.0, 10 - shortBy }, YsWide, (x, y) => 5, true);
            return (SheetGeometry.Transformed(Cube(), f), SheetGeometry.Transformed(sheet, f));
        }

        [Theory]
        [InlineData(1.8e-8)]
        [InlineData(1e-7)]
        [InlineData(1e-8 * 10)]
        [InlineData(1e-10 * 10)]
        [InlineData(1e-12 * 10)]
        [InlineData(1e-14 * 10)]
        public void ARimJustShortOfTheSurface_IsContinued_AtTheOrigin_AndDiagnosed(double shortBy)
        {
            (DMesh3 w, DMesh3 sheet) = ShortBy(shortBy, 0);
            var op = Make(sheet, w);
            Assert.True(op.Compute(), shortBy + ": " + op.Status + " " + op.Report.Witness);
            Assert.True(op.Report.Extended);
            // every one of the six rim edges that are inside the cube is chosen (the distance query used to see only the first one at 1.8e-8)
            Assert.Equal(6, op.Report.ChosenEdges);
            Assert.Equal(10.0, op.Report.InsideLength, 6);
            Assert.Equal(500, SheetRunner.Volume(op.Report.Cut), 6);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Report.Cut) ?? "");
            var dg = Make(sheet, w);
            Assert.True(dg.Diagnose());
            Assert.Equal(6, dg.Report.ChosenEdges);
            Assert.Single(dg.Report.Runs);
            Assert.Equal(10.0, dg.Report.InsideLength, 6);
            Assert.True(dg.Report.MaxDepth <= 1e-5, "the rim is " + dg.Report.MaxDepth + " deep");
        }

        [Theory]
        [InlineData(1.8e-8)]
        [InlineData(1e-8 * 10)]
        [InlineData(1e-10 * 10)]
        [InlineData(1e-12 * 10)]
        [InlineData(1e-14 * 10)]
        public void ARimJustShortOfTheSurface_FarFromTheOrigin_IsContinuedOrRefusedForARealReason(double shortBy)
        {
            // at 1e6 one rounding unit is 1.2e-10: a rim 1e-13 short is rounded onto the surface (a cut as it is) or beyond it; whatever is returned is accepted by the sheet role
            (DMesh3 w, DMesh3 sheet) = ShortBy(shortBy, 1e6);
            var op = Make(sheet, w);
            bool ok = op.Compute();
            if (ok) {
                Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Report.Cut) ?? "");
                Assert.Equal(500, SheetRunner.Volume(op.Report.Cut), 3);
                var role = new ExactMeshBoolean(new List<DMesh3> { w, op.Result }, ExactMeshBoolean.Operations.Difference) {
                    AllowOpenMeshes = true, Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }
                };
                role.Compute();
                Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, role.Status);
            }
            else
                Assert.NotEqual(Status.SheetRoleRefused, op.Status);
            Assert.True(Make(sheet, w).Diagnose());
        }

        [Fact]
        public void WithTheSheetRoleOff_SuccessMeansTheHelpersOwnChecksHold_AndTheRoleWasNotAsked()
        {
            // the review's case A14b: a rim 1e-14 short with VerifyWithSheetRole = false was "extended" although the sheet role refused the result. With the exact classification of the rim
            // the sheet is continued and the role accepts it (the case disappears); the documented meaning of Success without the role is pinned here
            (DMesh3 w, DMesh3 sheet) = ShortBy(1e-14, 0);
            var op = Make(sheet, w, o => o.VerifyWithSheetRole = false);
            Assert.True(op.Compute());
            Assert.True(op.Report.Extended);
            Assert.Null(op.Report.Cut);
            Assert.Equal("", op.Report.CutStatus);
            var role = new ExactMeshBoolean(new List<DMesh3> { w, op.Result }, ExactMeshBoolean.Operations.Difference) {
                AllowOpenMeshes = true, Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }
            };
            role.Compute();
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, role.Status);
            Assert.Equal(500, SheetRunner.Volume(role.Result), 6);
        }

        // ---- 7. the overshoot floor: a gap behind the exit below twice the least overshoot ---------------------------------------------

        static DMesh3 SlotFin(double slot, double fin, double shift)
        {
            DMesh3 m = SheetGeometry.PrismXZ(new[] {
                new Vector2d(0, 0), new Vector2d(9.5 + slot + fin, 0), new Vector2d(9.5 + slot + fin, 10), new Vector2d(9.5 + slot, 10),
                new Vector2d(9.5 + slot, 2), new Vector2d(9.5, 2), new Vector2d(9.5, 10), new Vector2d(0, 10) }, 0, 10);
            return SheetGeometry.Transformed(m, p => p + new Vector3d(shift, shift, shift));
        }

        static DMesh3 SlotSheet(double shift)
        {
            return SheetGeometry.Transformed(SheetGeometry.Grid(new[] { -2.3, 1.1, 5.0, 9.4 }, new[] { 0.3, 2.0, 4.0, 6.0, 8.0, 9.7 }, (x, y) => 5, false), p => p + new Vector3d(shift, shift, shift));
        }

        [Theory]
        [InlineData(2e-13, 1e-13, 0.0)]      // the review's case A15: the new points used to land beyond the fin
        [InlineData(4e-12, 0.3, 0.0)]        // below twice the least overshoot (4.5e-12 here)
        [InlineData(1e-7, 0.3, 1e6)]         // the least overshoot grows with the coordinates: 2.3e-7 at 1e6
        [InlineData(4e-7, 0.3, 1e6)]
        public void AGapBehindTheExit_BelowTwiceTheLeastOvershoot_IsRefused_WithTheGapInTheWitness(double slot, double fin, double shift)
        {
            var op = Make(SlotSheet(shift), SlotFin(slot, fin, shift));
            Assert.False(op.Compute());
            Assert.Equal(Status.GapBelowResolution, op.Status);
            Assert.Contains("behind the exit", op.Report.Witness);
            Assert.True(op.Report.HasWitnessPoint && op.Report.WitnessVertex >= 0);
            // the mutant without the rule goes on: the strip is accepted or refused by a later rule, and where it is accepted its points are beyond the slot
            var off = Make(SlotSheet(shift), SlotFin(slot, fin, shift), o => o.Verify &= ~Rules.Gap);
            off.Compute();
            Assert.NotEqual(Status.GapBelowResolution, off.Status);
        }

        [Theory]
        [InlineData(1e-9, 0.0)]
        [InlineData(1e-4, 0.0)]
        [InlineData(1e-4, 1e6)]
        [InlineData(0.05, 1e6)]
        public void AGapAboveTheLimit_IsNotRefusedAsAGap_AndAnAcceptedStripStaysInTheSlot(double slot, double shift)
        {
            DMesh3 w = SlotFin(slot, 0.3, shift), sheet = SlotSheet(shift);
            var op = Make(sheet, w);
            bool ok = op.Compute();
            Assert.NotEqual(Status.GapBelowResolution, op.Status);
            if (!ok) return;      // refused by another rule (the outer edge cannot keep to a slot of 1e-9): never a cut through the fin
            foreach (int v in op.Result.VertexIndices())
                if (!sheet.IsVertex(v))
                    Assert.True(op.Result.GetVertex(v).x <= 9.5 + slot + shift + 1e-12 * Math.Max(1, shift), "a new point is beyond the slot: " + op.Result.GetVertex(v));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Report.Cut) ?? "");
        }

        // ---- 8. the verification cut must be clean; the curve rule's verdict is topological ----------------------------------------------

        static DMesh3 PinTouchingTheWall()
        {
            // the slot-and-fin workpiece and, as a second shell, a triangular prism pin whose edge touches the wall x = 9.5 along a line (the review's case A17)
            DMesh3 pin = SheetGeometry.PrismXZ(new[] { new Vector2d(9.5, 5), new Vector2d(9.51, 3), new Vector2d(9.51, 8) }, 4.5, 5.5);
            return SheetGeometry.Merge(SlotFin(0.05, 0.3, 0), pin);
        }

        [Fact]
        public void ACutThatTheSheetRoleAcceptsButIsNotClean_IsRefused_CutNotClean()
        {
            DMesh3 w = PinTouchingTheWall();
            var op = Make(SlotSheet(0), w);
            Assert.False(op.Compute());
            Assert.Equal(Status.CutNotClean, op.Status);
            Assert.Contains("not clean", op.Report.Witness);
            Assert.NotNull(op.Report.Cut);
            Assert.NotEqual("", MeshRobustRepair.NotCleanReason(op.Report.Cut) ?? "");
            // without the check the helper accepts it (the mutant), and without the sheet role there is no cut to look at
            var off = Make(SlotSheet(0), w, o => o.Verify &= ~Rules.CutClean);
            Assert.True(off.Compute());
            Assert.NotEqual("", MeshRobustRepair.NotCleanReason(off.Report.Cut) ?? "");
            Assert.True(Make(SlotSheet(0), w, o => o.VerifyWithSheetRole = false).Compute());
        }

        [Fact]
        public void APinOnANeck_IsCutThrough_ByAContinuationThatEndsOutside_TheCurveRuleIsTopological()
        {
            // the review's case A18: a pin Box(9.505,4.2,3, 9.512,5.0,8) on a neck Box(9.49,4.55,3, 9.506,4.65,8) in the slot of the slot-and-fin workpiece. No continuation that ends outside the
            // workpiece can avoid the neck, the strip's outer polyline is routed round the pin and the strip's curve stays one component: accepted, the whole pin above the sheet is cut through
            // (kept 506.0122 against 506.0304 for a pin that stood free). The guarantee of the rules is not "nothing but the band up to the first surface the rays meet"; it is: new points
            // outside, one connected curve per run, no contact with the sheet, itself or another tool, a clean cut that the sheet role accepts
            DMesh3 w = SheetGeometry.Union(SlotFin(0.05, 0.3, 0), SheetGeometry.Box(9.505, 4.2, 3, 9.512, 5.0, 8), SheetGeometry.Box(9.49, 4.55, 3, 9.506, 4.65, 8));
            var op = Make(SlotSheet(0), w);
            Assert.True(op.Compute(), op.Status + " " + op.Report.Witness);
            Assert.Equal(506.0122, SheetRunner.Volume(op.Report.Cut), 4);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Report.Cut) ?? "");
            Assert.Equal(1, op.Report.Runs[0].CurveComponents);
        }

        [Fact]
        public void TheCurveRuleWitness_SaysWhatWasFound_AndWhere()
        {
            SheetRun r = SheetRunner.Run(MeshSheetExtensionTests.Get("free pin in the slot"));
            Assert.Equal(Status.CurveRule, r.Status);
            Assert.Contains("2 separate curves where 1 was expected", r.Report.Witness);
            Assert.Contains("near", r.Report.Witness);
            Assert.True(r.Report.HasWitnessPoint);
        }

        // ---- 9. the outer-edge test ------------------------------------------------------------------------------------------------

        [Fact]
        public void TheOuterEdgeTest_IsTheRule_ThatRefusesAStripWhoseOuterEdgeCannotGetOut_WithTheSheetRoleOff()
        {
            // a rib on the slot wall as wide as the slot (0.049 of 0.05): the outer edge cannot be got out of the workpiece within the subdivision. With the sheet role off the outer-edge
            // test refuses it; without the test the inverted-quad test is next. (The review reported a rib on the exit wall that only this test refuses; none was found in a scan of
            // 378 ribs here: the inverted-quad, curve and self-intersection tests were always behind it. The test stays, and its status is pinned.)
            DMesh3 w = SheetGeometry.Union(SlotFin(0.05, 0.3, 0), SheetGeometry.Box(9.49, 4.7, 2, 9.549, 5.3, 8));
            var op = Make(SlotSheet(0), w, o => o.VerifyWithSheetRole = false);
            Assert.False(op.Compute());
            Assert.Equal(Status.OuterEdgeInside, op.Status);
            var off = Make(SlotSheet(0), w, o => { o.VerifyWithSheetRole = false; o.Verify &= ~Rules.OuterEdge; });
            off.Compute();
            Assert.NotEqual(Status.OuterEdgeInside, off.Status);
        }

        // ---- 3. the surviving source mutants ---------------------------------------------------------------------------------------

        [Fact]
        public void AStripThatMeetsAnOriginalTriangleOfTheSheet_IsRefused_MeetsSheet()
        {
            // the sheet z = 5 (0.1 short of the cube) and, as a second part of the same mesh, the plane x = 9.95 that crosses it: the strip meets an ORIGINAL triangle (not another tool)
            DMesh3 wall = SheetGeometry.Grid(new[] { -2.3, 5.0, 12.1 }, new[] { -2.0, 5.0, 12.0 }, (x, y) => 0, true);
            DMesh3 vert = SheetGeometry.Transformed(wall, p => new Vector3d(9.95, p.x, p.y));
            vert.ReverseOrientation();
            DMesh3 sheet = SheetGeometry.Merge(SheetGeometry.Grid(new[] { -2.3, 1.1, 5.0, 9.9 }, YsWide, (x, y) => 5, true), vert);
            var op = Make(sheet, Cube());
            Assert.False(op.Compute());
            Assert.Equal(Status.MeetsSheet, op.Status);
        }

        [Fact]
        public void AToolThatMeetsOnlyTheFirstStripTriangle_IsSeen()
        {
            // the first triangle added is the one with the least id above the sheet's: a small tool through its centre meets that triangle and no other
            SheetCaseData d = MeshSheetExtensionTests.Get("planar 0.5 %").Build();
            var first = Make(d.Sheets[0], d.W);
            Assert.True(first.Compute());
            int t0 = d.Sheets[0].MaxTriangleID;
            Assert.True(first.Result.IsTriangle(t0));
            Vector3d c = first.Result.GetTriCentroid(t0);
            DMesh3 tool = SheetGeometry.Triangles(new[] { c + new Vector3d(0, 0, 1e-3), c + new Vector3d(0.01, 0, -1e-3), c + new Vector3d(-0.01, 0, -1e-3) }, new[] { 0, 1, 2 });
            var op = Make(d.Sheets[0], d.W, o => { o.OtherTools = new List<DMesh3> { tool }; o.OtherRoles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Sheet }; });
            Assert.False(op.Compute());
            Assert.Equal(Status.MeetsOtherTool, op.Status);
        }

        [Fact]
        public void AStripWithNoCurveAtAll_IsRefused_NotOnlyOneWithTooMany()
        {
            // the chord with the subdivision capped at 0 and the outer-edge test off: the strip's polyline goes through the sphere without a curve edge (0 components); the curve rule asks
            // for exactly 1 (a mutant that asks for at most 1 accepts it)
            SheetCaseData d = SheetCases.All().First(c => c.Name.StartsWith("chord")).Build();
            var op = Make(d.Sheets[0], d.W, o => { o.VerifyWithSheetRole = false; o.MaxSubdivisionDepth = 0; o.Verify &= ~Rules.OuterEdge; });
            Assert.False(op.Compute());
            Assert.Equal(Status.CurveRule, op.Status);
            Assert.Equal(0, op.Report.Runs[0].CurveComponents);
            Assert.Contains("no curve", op.Report.Witness);
        }

        [Theory]
        [InlineData(0.04)]       // a dip of 6 % of the segment
        [InlineData(1e-5)]       // 1.5e-5: above the old window of 1e-7 of the segment
        [InlineData(1e-9)]       // 1.5e-9: inside the old window, which hid it
        [InlineData(1e-12)]
        public void AnOuterSegmentThatStartsOnTheSurfaceAndDipsInside_IsSeen_AtAnyDepthOfTheDip(double dip)
        {
            // a sheet triangle with a vertex exactly on the top face of the cube, `dip` from the edge x = 10: the outer segment from it runs through the cube's corner for its first
            // part before it leaves through the face x = 10. The contact is tested exactly, excluding only the contact at the end on the surface (the old test shortened the segment by
            // 1e-7 of its length there and did not see a dip below that)
            DMesh3 sheet = SheetGeometry.Triangles(new[] { new Vector3d(10 - dip, 5, 10), new Vector3d(10.65, 5, 9.1), new Vector3d(8.2, 5.1, 9.9) }, new[] { 0, 1, 2 });
            var op = Make(sheet, Cube(), o => { o.VerifyWithSheetRole = false; o.MaxExtension = 0.5; });
            op.Compute();
            Assert.True(op.Report.SubdivisionPoints >= 1, dip + ": " + op.Status + " " + op.Report.Witness);
        }

        // ---- the second review (PR 1550): freed ids, unused vertices, bad tools, the limit on U, statuses --------------------------------------

        // the sheet with triangles added and removed again: the ids of the removed ones are free and AppendTriangle reuses them (the last added one stays, so MaxTriangleID is high)
        static DMesh3 WithFreedIds(DMesh3 sheet)
        {
            DMesh3 m = new DMesh3(sheet);
            var added = new List<int>();
            for (int i = 0; i < 20; ++i) {
                double y = 12.2 + 0.01 * i;
                int a = m.AppendVertex(new Vector3d(-2.3, y, 5)), b = m.AppendVertex(new Vector3d(-2.2, y, 5)), c = m.AppendVertex(new Vector3d(-2.3, y + 0.005, 5));
                added.Add(m.AppendTriangle(a, b, c));
            }
            for (int i = 0; i + 1 < added.Count; ++i) Assert.Equal(MeshResult.Ok, m.RemoveTriangle(added[i], true, false));
            Assert.True(m.MaxTriangleID > m.TriangleCount);
            return m;
        }

        [Fact]
        public void AStripBuiltIntoFreedTriangleIds_IsStillCheckedAgainstOtherTools()
        {
            // the review's F1: new DMesh3(sheet) copies the free list and AppendTriangle reuses freed ids, so strip triangles got ids below the sheet's MaxTriangleID and the checks that
            // skip "the sheet's own ids" skipped them: Success with a tool through the strip. The strip's triangles are the ids AppendTriangle returned
            SheetCaseData d = MeshSheetExtensionTests.Get("planar 0.5 %").Build();
            DMesh3 sheet = WithFreedIds(d.Sheets[0]);
            var probe = Make(sheet, d.W);
            Assert.True(probe.Compute());
            int reused = probe.Result.TriangleIndices().Count(t => !sheet.IsTriangle(t) && t < sheet.MaxTriangleID);
            Assert.True(reused > 0, "the strip must reuse freed ids for this test to mean something");
            int newTri = probe.Result.TriangleIndices().First(t => !sheet.IsTriangle(t) && t < sheet.MaxTriangleID);
            Vector3d c = probe.Result.GetTriCentroid(newTri);
            DMesh3 tool = SheetGeometry.Box(c.x - 1e-3, c.y - 1e-3, c.z - 1e-3, c.x + 1e-3, c.y + 1e-3, c.z + 1e-3);
            var op = Make(sheet, d.W, o => o.OtherTools = new List<DMesh3> { tool });
            Assert.False(op.Compute());
            Assert.Equal(Status.MeetsOtherTool, op.Status);
        }

        [Fact]
        public void AStripBuiltIntoFreedTriangleIds_IsStillCheckedAgainstTheSheet()
        {
            DMesh3 wall = SheetGeometry.Grid(new[] { -2.3, 5.0, 12.1 }, new[] { -2.0, 5.0, 12.0 }, (x, y) => 0, true);
            DMesh3 vert = SheetGeometry.Transformed(wall, p => new Vector3d(9.95, p.x, p.y));
            vert.ReverseOrientation();
            DMesh3 sheet = WithFreedIds(SheetGeometry.Merge(SheetGeometry.Grid(new[] { -2.3, 1.1, 5.0, 9.9 }, YsWide, (x, y) => 5, true), vert));
            var op = Make(sheet, Cube(), o => o.VerifyWithSheetRole = false);
            Assert.False(op.Compute());
            Assert.Equal(Status.MeetsSheet, op.Status);
        }

        [Theory]
        [InlineData("planar 8 %", 0.08)]
        [InlineData("slit ends in the middle", 0.30)]
        public void AnUnusedVertexFarAway_DoesNotSwitchOffTheDepthLimit(string name, double depthShare)
        {
            // the review's F2: GetBounds() includes unused vertices, the diagonal grew and the limit with it (a 30 % deep slit read as 0.3 % deep and was extended)
            SheetCaseData d = MeshSheetExtensionTests.Get(name).Build();
            DMesh3 sheet = new DMesh3(d.Sheets[0]);
            sheet.AppendVertex(new Vector3d(100, 100, 100));
            var plain = Make(d.Sheets[0], d.W);
            plain.Compute();
            var op = Make(sheet, d.W);
            Assert.False(op.Compute());
            Assert.Equal(Status.DepthAboveLimit, op.Status);
            Assert.Equal(plain.Report.Diagonal, op.Report.Diagonal);
            Assert.InRange(op.Report.MaxDepthRelative, depthShare * 0.97, depthShare * 1.15);
            var dg = Make(sheet, d.W);
            Assert.True(dg.Diagnose());
            Assert.True(dg.Report.Runs[0].DepthAboveLimit);
            Assert.Equal(plain.Report.Diagonal, dg.Report.Diagonal);
        }

        [Fact]
        public void AnUnusedVertexFarAway_DoesNotCostDigitsInTheVolumes()
        {
            // Volume() sums against a first vertex: an unused far vertex must not be that origin
            SheetCaseData d = MeshSheetExtensionTests.Get("planar 0.5 %").Build();
            DMesh3 w = new DMesh3(d.W);
            w.AppendVertex(new Vector3d(1e9, 1e9, 1e9));
            var op = Make(d.Sheets[0], w);
            Assert.True(op.Compute());
            Assert.Equal(500, SheetRunner.Volume(op.Report.Cut), 9);
        }

        [Fact]
        public void ANullOtherTool_ABadOne_AndTooManyRoles_AreInvalidInput_AndTheReportIsThisRuns()
        {
            SheetCaseData d = MeshSheetExtensionTests.Get("planar 0.5 %").Build();
            var op = Make(d.Sheets[0], d.W);
            Assert.True(op.Compute());
            Assert.NotEmpty(op.Report.Runs);
            op.OtherTools = new List<DMesh3> { null };
            Assert.False(op.Compute());
            Assert.Equal(Status.InvalidInput, op.Status);
            Assert.Contains("OtherTools[0] is null", op.Error);
            Assert.Equal(Status.InvalidInput, op.Report.Status);
            Assert.Empty(op.Report.Runs);                     // not the previous run's
            Assert.Null(op.Result);
            DMesh3 nan = SheetGeometry.Box(20, 20, 20, 21, 21, 21);
            nan.SetVertex(nan.VertexIndices().First(), new Vector3d(double.NaN, 20, 20));
            op.OtherTools = new List<DMesh3> { nan };
            Assert.False(op.Compute());
            Assert.Equal(Status.InvalidInput, op.Status);
            Assert.Contains("OtherTools[0]", op.Error);
            op.OtherTools = new List<DMesh3> { SheetGeometry.Box(20, 20, 20, 21, 21, 21) };
            op.OtherRoles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet };
            Assert.False(op.Compute());
            Assert.Equal(Status.InvalidInput, op.Status);
            Assert.Contains("OtherRoles", op.Error);
            var fresh = Make(d.Sheets[0], d.W);
            Assert.True(fresh.Diagnose());
            fresh.OtherTools = new List<DMesh3> { null };
            Assert.False(fresh.Diagnose());
            Assert.Empty(fresh.Report.Runs);
        }

        [Fact]
        public void TheLimitOnU_IsNeverSkippedSilently()
        {
            // the review's F5: with an empty smaller part the limit was not applied and the result was Success. A finite limit is now exceeded where there is no smaller part to relate U to
            Assert.False(MeshSheetExtension.UBeyondLimit(1, 0.5, double.PositiveInfinity));
            Assert.False(MeshSheetExtension.UBeyondLimit(1, 0, double.PositiveInfinity));
            Assert.True(MeshSheetExtension.UBeyondLimit(1, 0, 1e-3));
            Assert.True(MeshSheetExtension.UBeyondLimit(1, -2, 1e-3));
            Assert.True(MeshSheetExtension.UBeyondLimit(1, double.NaN, 1e-3));
            Assert.True(MeshSheetExtension.UBeyondLimit(1, 0.5, 1));
            Assert.False(MeshSheetExtension.UBeyondLimit(0.4, 0.5, 1));
            // a limit with the cut off would be skipped: refused as InvalidInput
            SheetCaseData d = MeshSheetExtensionTests.Get("planar 0.5 %").Build();
            var op = Make(d.Sheets[0], d.W, o => { o.MaxUOverSmallerPart = 1; o.VerifyWithSheetRole = false; });
            Assert.False(op.Compute());
            Assert.Equal(Status.InvalidInput, op.Status);
            Assert.Contains("MaxUOverSmallerPart", op.Error);
        }

        [Fact]
        public void TheSmallerPart_IsTheSmallerOfTheTwo_WhicheverSideIsKept()
        {
            // the bent sheet keeps 462.5 of 1000; reversed it keeps 537.5: the smaller part is 462.5 both times
            SheetCaseData d = MeshSheetExtensionTests.Get("bent 0.5 %").Build();
            DMesh3 reversed = new DMesh3(d.Sheets[0]);
            reversed.ReverseOrientation();
            foreach (DMesh3 sheet in new[] { d.Sheets[0], reversed }) {
                var op = Make(sheet, d.W);
                Assert.True(op.Compute());
                Assert.Equal(462.5, op.Report.SmallerPart, 6);
            }
        }

        [Fact]
        public void ARimVertexWithADegenerateTriangle_HasNoDirection()
        {
            // a sheet that is one collinear triangle inside the cube: no outward normal in its plane
            DMesh3 sheet = SheetGeometry.Triangles(new[] { new Vector3d(2, 5, 5), new Vector3d(4, 5, 5), new Vector3d(6, 5, 5) }, new[] { 0, 1, 2 });
            var op = Make(sheet, Cube());
            Assert.False(op.Compute());
            Assert.Equal(Status.NoDirection, op.Status);
            Assert.True(op.Report.WitnessVertex >= 0 && op.Report.HasWitnessPoint);
            Assert.False(Make(sheet, Cube()).Diagnose());
        }

        // ---- every status has a test, or an argument ------------------------------------------------------------------------------------------

        // the value and the test that produces it; the four that are defensive (no known input produces them, see the enum's comments) are listed as such
        static readonly Dictionary<Status, string> StatusTests = new Dictionary<Status, string> {
            { Status.NotComputed, "MeshSheetExtensionTests.EmptyOrMissingInput_AndNonFiniteCoordinates_AreInvalidInput (a fresh instance)" },
            { Status.Success, "MeshSheetExtensionTests.Case_EndsAsDesigned_AndAcceptedResultsAreChecked" },
            { Status.InvalidInput, "MeshSheetExtensionTests.EmptyOrMissingInput_..., ReviewTests.ANullOtherTool_ABadOne_AndTooManyRoles_..." },
            { Status.WorkpieceNotClosed, "MeshSheetExtensionTests.OpenWorkpiece_IsRefused_WorkpieceNotClosed" },
            { Status.SheetNotSurface, "MeshSheetExtensionTests.SheetWithAnEdgeUsedByThreeTriangles_..., SeamAtARimEdge..." },
            { Status.BowtieVertex, "MeshSheetExtensionTests.BowtieRimVertex_IsRefused_WithTheVertexAndItsPosition" },
            { Status.NoDirection, "ReviewTests.ARimVertexWithADegenerateTriangle_HasNoDirection" },
            { Status.NoExit, "defensive: " },
            { Status.DepthAboveLimit, "MeshSheetExtensionTests.ARimDeeperThanTheLimit_IsRefused_WithTheDepthReported" },
            { Status.GrazingExit, "MeshSheetExtensionTests.GrazingExit_IsRefused" },
            { Status.NewPointNotOutside, "MeshSheetExtensionTests.RaysThroughAnEdgeOrAVertexOfTheWorkpiece_..." },
            { Status.OuterEdgeInside, "ReviewTests.TheOuterEdgeTest_IsTheRule_..." },
            { Status.InvertedQuad, "MeshSheetExtensionTests.NotchNarrowerThanTheDepth_IsRefused_..." },
            { Status.CurveRule, "MeshSheetExtensionTests.FreePinInTheSlot_IsRefusedByTheCurveRule_..." },
            { Status.CoplanarContact, "MeshSheetExtensionTests.FreePinWhoseTopLiesInTheStripsPlane_..." },
            { Status.MeetsSheet, "ReviewTests.AStripThatMeetsAnOriginalTriangleOfTheSheet_IsRefused_MeetsSheet" },
            { Status.MeetsItself, "MeshSheetExtensionTests.SeveralSheets_OnlyTheRefusedOneIsContinued_... (folded sheet)" },
            { Status.MeetsOtherTool, "MeshSheetExtensionTests.SeveralSheets_..., ReviewTests.AToolThatMeetsOnlyTheFirstStripTriangle_IsSeen" },
            { Status.ImprintFailed, "defensive: " },
            { Status.ConstructionFailed, "defensive: " },
            { Status.SheetRoleRefused, "MeshSheetExtensionTests.SeveralSheets_... (two shells)" },
            { Status.CutFailed, "defensive: " },
            { Status.UncertaintyAboveLimit, "MeshSheetExtensionTests.LimitOptions_AreUsed" },
            { Status.GapBelowResolution, "ReviewTests.AGapBehindTheExit_BelowTwiceTheLeastOvershoot_IsRefused_..." },
            { Status.CutNotClean, "ReviewTests.ACutThatTheSheetRoleAcceptsButIsNotClean_IsRefused_CutNotClean" },
        };

        [Fact]
        public void EveryStatus_IsProducedByATest_OrIsListedAsDefensive()
        {
            foreach (Status s in Enum.GetValues(typeof(Status)))
                Assert.True(StatusTests.ContainsKey(s), s + " has no test and no argument: add it to this table");
            Assert.Equal(Enum.GetValues(typeof(Status)).Length, StatusTests.Count);
            var defensive = StatusTests.Where(kv => kv.Value.StartsWith("defensive")).Select(kv => kv.Key).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { Status.NoExit, Status.ImprintFailed, Status.ConstructionFailed, Status.CutFailed }.OrderBy(x => x).ToArray(), defensive);
            // the statuses that the table says a test produces: the cheap ones are produced here, the rest by the tests named
            Assert.Equal(Status.NotComputed, new MeshSheetExtension().Status);
        }

        // ---- 6. attributes ----------------------------------------------------------------------------------------------------------

        [Fact]
        public void ASheetWithGroupsNormalsColoursAndNonCompactIds_KeepsItsOriginals_AndTheStripTakesTheGroupOfTheRimTriangle()
        {
            SheetCaseData d = MeshSheetExtensionTests.Get("planar 0.5 %").Build();
            DMesh3 src = d.Sheets[0];
            var sheet = new DMesh3(true, true, false, true);
            var map = new Dictionary<int, int>();
            foreach (int v in src.VertexIndices()) {
                var vi = new NewVertexInfo(src.GetVertex(v), new Vector3f(0, 0, 1 + 0.001f * v), new Vector3f(0.1f * (v % 7), 0.5f, 0.25f));
                map[v] = sheet.AppendVertex(vi);
            }
            int unused = sheet.AppendVertex(new Vector3d(100, 100, 100));         // an unused vertex
            foreach (int t in src.TriangleIndices()) {
                Index3i tv = src.GetTriangle(t);
                sheet.AppendTriangle(map[tv.a], map[tv.b], map[tv.c], 7 + t % 3);
            }
            // a triangle removed far outside the cube (all its vertices have y < -0.4 or y > 10.2): ids are not compact
            int victim = sheet.TriangleIndices().First(t => { Index3i tv = sheet.GetTriangle(t); return Enumerable.Range(0, 3).All(i => sheet.GetVertex(tv[i]).y < -0.4) && sheet.GetVertex(tv.a).x < 0; });
            Assert.True(sheet.RemoveTriangle(victim, true, false) == MeshResult.Ok);
            int maxVertexBefore = sheet.MaxVertexID;

            var op = Make(sheet, d.W);
            Assert.True(op.Compute(), op.Status + " " + op.Report.Witness);
            DMesh3 r = op.Result;
            SheetRunner.AssertOriginalKept(sheet, r);
            foreach (int v in sheet.VertexIndices()) {
                Assert.Equal(sheet.GetVertexNormal(v), r.GetVertexNormal(v));
                Assert.Equal(sheet.GetVertexColor(v), r.GetVertexColor(v));
            }
            foreach (int t in sheet.TriangleIndices()) Assert.Equal(sheet.GetTriangleGroup(t), r.GetTriangleGroup(t));
            Assert.True(r.IsVertex(unused));
            Assert.True(r.CheckValidity(true, FailMode.ReturnOnly));
            // every new triangle that has a free edge of the sheet as an edge has the group of the triangle that edge belongs to; the others of the strip have a group of the sheet
            int withRim = 0;
            var groups = new HashSet<int>(sheet.TriangleIndices().Select(t => sheet.GetTriangleGroup(t)));
            foreach (int t in r.TriangleIndices()) {
                if (sheet.IsTriangle(t)) continue;
                Assert.Contains(r.GetTriangleGroup(t), groups);
                Index3i tv = r.GetTriangle(t);
                for (int i = 0; i < 3; ++i) {
                    int a = tv[i], b = tv[(i + 1) % 3];
                    if (!sheet.IsVertex(a) || !sheet.IsVertex(b)) continue;
                    int e = sheet.FindEdge(a, b);
                    if (e >= 0 && sheet.IsBoundaryEdge(e)) {
                        Assert.Equal(sheet.GetTriangleGroup(sheet.GetEdgeT(e).a), r.GetTriangleGroup(t));
                        ++withRim;
                    }
                }
            }
            Assert.True(withRim > 0);
            // new vertices copy the normal and colour of a rim vertex (not the defaults)
            var normals = new HashSet<Vector3f>(sheet.VertexIndices().Select(v => sheet.GetVertexNormal(v)));
            var colours = new HashSet<Vector3f>(sheet.VertexIndices().Select(v => sheet.GetVertexColor(v)));
            int newVertices = 0;
            foreach (int v in r.VertexIndices()) {
                if (sheet.IsVertex(v)) continue;
                ++newVertices;
                Assert.Contains(r.GetVertexNormal(v), normals);
                Assert.Contains(r.GetVertexColor(v), colours);
            }
            Assert.True(newVertices > 0);
        }

        // ---- 4. the report's mean depth and U -------------------------------------------------------------------------------------------

        [Fact]
        public void TheMeanDepthAndU_AfterCompute_AreOverTheInsidePartOnly()
        {
            // the chord: one rim edge from x = -4 to 4 with both ends outside; the review measured the true profile at a maximum of 0.4749 and about 0.31 on average
            SheetCaseData d = SheetCases.All().First(c => c.Name.StartsWith("chord")).Build();
            var op = Make(d.Sheets[0], d.W, o => o.VerifyWithSheetRole = false);
            Assert.True(op.Compute());
            var dg = Make(d.Sheets[0], d.W);
            Assert.True(dg.Diagnose());
            SheetExtensionRun a = op.Report.Runs[0], b = dg.Report.Runs[0];
            Assert.InRange(a.MeanDepth, 0.25, 0.40);                 // (it was 0.441 over the whole edge, with the outside stretch counted)
            Assert.Equal(b.MeanDepth, a.MeanDepth, 12);
            Assert.Equal(b.U, a.U, 12);
            Assert.True(a.MeanDepth <= a.MaxDepth);
            // a planar rim has the same depth all along: the mean is the maximum
            SheetCaseData p = MeshSheetExtensionTests.Get("planar 0.5 %").Build();
            var pl = Make(p.Sheets[0], p.W, o => o.VerifyWithSheetRole = false);
            Assert.True(pl.Compute());
            Assert.Equal(pl.Report.Runs[0].MaxDepth, pl.Report.Runs[0].MeanDepth, 9);
        }
    }
}
