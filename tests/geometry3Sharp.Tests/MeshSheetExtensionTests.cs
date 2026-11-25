using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using geometry3Sharp.Tests.SheetExt;
using Xunit;
using Status = gs.MeshSheetExtension.ExtensionStatus;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// gs.MeshSheetExtension (docs/design/SHEET_EXTENSION.md): a sheet that ends short of the workpiece's surface is continued to it up to the first exit of the ray of each
    /// rim vertex, verified exactly. Every procedural and adversarial case of the design's section 8 (the cases of benchmarks/SheetExtensionProbe, plus two more) runs
    /// with the outcome the design states, and an accepted case is checked: the original triangles are kept, the new points are strictly outside, the cut with the extended
    /// sheet is clean and has the analytic volume, and both sides add up. All inputs are procedural.
    /// </summary>
    public class MeshSheetExtensionTests
    {
        internal static readonly List<SheetCase> AllCases = SheetCases.All().Concat(SheetExtraCases.All()).ToList();

        public static IEnumerable<object[]> CaseNames() => AllCases.Select(c => new object[] { c.Name });

        internal static SheetCase Get(string name) => AllCases.First(c => c.Name == name);

        static SheetRun Run(string name, Action<MeshSheetExtension> mutate = null) => SheetRunner.Run(Get(name), null, mutate);

        static string Fmt(SheetRun r) => r.Actual + " " + r.Report.Witness;

        // ---- every case ends as the design says; an accepted case is checked beyond its status -----------------------------------

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Case_EndsAsDesigned_AndAcceptedResultsAreChecked(string name)
        {
            SheetRun r = SheetRunner.Run(Get(name));
            Assert.Null(SheetRunner.Verdict(r));
            if (r.Status != Status.Success) {
                // a refusal: nothing is returned, the report has the witness (a text, and where it exists a point)
                Assert.Null(r.Result);
                Assert.False(string.IsNullOrEmpty(r.Op.Error));
                Assert.Equal(r.Status, r.Report.Status);
                return;
            }
            Assert.NotNull(r.Result);
            Assert.Null(r.Op.Error);
            SheetRunner.AssertOriginalKept(r.Sheet, r.Result);
            if (r.Actual == "unchanged") {
                // nothing added, nothing to do: a copy of the sheet
                Assert.NotSame(r.Sheet, r.Result);
                Assert.Equal(r.Sheet.TriangleCount, r.Result.TriangleCount);
                Assert.Equal(r.Sheet.VertexCount, r.Result.VertexCount);
                Assert.Equal(0, r.Report.NewTriangles);
                Assert.False(r.Report.Extended);
                Assert.Empty(r.Report.Runs);
            }
            else {
                Assert.True(r.Report.Extended);
                Assert.Equal(r.Sheet.TriangleCount + r.Report.NewTriangles, r.Result.TriangleCount);
                Assert.Equal(r.Sheet.VertexCount + r.Report.NewVertices, r.Result.VertexCount);
                Assert.True(r.Report.NewTriangles > 0);
                SheetRunner.AssertNewVerticesOutside(r.Sheet, r.Result, r.W);
                SheetRunner.AssertStripTrianglesProper(r.Sheet, r.Result);
                // the plain sheet role refuses the unextended sheet: the extension is what made it cut
                Assert.Equal("refused RimInsideWorkpiece", r.Plain);
            }
            // the sheet cut with the returned sheet, run independently of the helper: accepted, clean, the volume of the helper's own cut
            DMesh3 cut = SheetRunner.IndependentCut(r);
            Assert.Equal(SheetRunner.Volume(r.Report.Cut), SheetRunner.Volume(cut), 6);
            if (!double.IsNaN(r.Analytic))
                Assert.True(Math.Abs(SheetRunner.Volume(cut) - r.Analytic) <= 1e-6 * Math.Abs(SheetRunner.Volume(r.W)), r.Case.Name + ": " + SheetRunner.Volume(cut) + " against " + r.Analytic);
            // (with other tools the reversed sheet is not the other side of one cut)
            if (r.Others.Count == 0)
                SheetRunner.AssertBothSidesAddUp(r, 1e-9);
        }

        // ---- the named cases of the design, with what is special about each -----------------------------------------------------------

        [Theory]
        [InlineData("planar 0.1 %", 0.001, 500.0)]
        [InlineData("planar 0.5 %", 0.005, 500.0)]
        [InlineData("planar 2 %", 0.02, 500.0)]
        [InlineData("bent 0.1 %", 0.001, 462.5)]
        [InlineData("bent 0.5 %", 0.005, 462.5)]
        [InlineData("bent 2 %", 0.02, 462.5)]
        public void ThroughCuts_WithinTheLimit_GiveTheAnalyticVolume(string name, double share, double analytic)
        {
            SheetRun r = Run(name);
            Assert.Equal(Status.Success, r.Status);
            Assert.True(r.Report.Extended);
            Assert.Equal(analytic, r.Kept, 9);
            // the depth the report gives is the share the rim ends short of the surface (the bent sheet's rays run on a slope)
            Assert.InRange(r.Report.MaxDepthRelative, share * 0.99, share * 1.1);
            Assert.True(r.Report.MaxDepth <= 0.05 * r.Report.Diagonal);
            Assert.Single(r.Report.Runs);
            Assert.Equal(1, r.Report.Runs[0].CurveComponents);
            // the cut with the extended sheet and the one with the reversed sheet add up to the workpiece
            SheetRunner.AssertBothSidesAddUp(r, 1e-9);
        }

        [Theory]
        [InlineData("planar 8 %", 0.08)]
        [InlineData("bent 8 %", 0.08)]
        [InlineData("slit ends in the middle", 0.30)]
        public void ARimDeeperThanTheLimit_IsRefused_WithTheDepthReported(string name, double depthShare)
        {
            SheetRun r = Run(name);
            Assert.Equal(Status.DepthAboveLimit, r.Status);
            Assert.Null(r.Result);
            Assert.InRange(r.Report.MaxDepthRelative, depthShare * 0.97, depthShare * 1.15);
            Assert.Equal(r.Report.MaxDepth / r.Report.Diagonal, r.Report.MaxDepthRelative);
            Assert.True(r.Report.HasWitnessPoint);
            Assert.True(r.Report.WitnessEdge >= 0, "the witness names the rim edge");
            Assert.True(r.Report.WitnessVertex >= 0, "and the rim vertex");
            Assert.Contains("of the diagonal", r.Report.Witness);
            // a sheet that is a slit: no exit within the limit, though the ray does exit
            // with a larger limit the same sheet is continued (the limit is a policy, not a correctness rule)
            SheetRun wide = Run(name, op => op.MaxExtension = 0.5);
            Assert.Equal(Status.Success, wide.Status);
        }

        [Fact]
        public void SlotAndFin_TheStripStopsAtTheSlotWall_TheFinIsUntouched()
        {
            SheetRun r = Run("slot and fin");
            Assert.Equal(Status.Success, r.Status);
            Assert.Equal(506, r.Kept, 9);
            // no new point reaches the fin (x 9.55 .. 9.85): the strip ends in the slot (x 9.5 .. 9.55) at the most (the sides of the sheet at y = 0.3 and 9.7 are continued too)
            foreach (int v in r.Result.VertexIndices())
                if (!r.Sheet.IsVertex(v))
                    Assert.True(r.Result.GetVertex(v).x <= 9.55, "a new point reaches the fin: " + r.Result.GetVertex(v));
            Assert.Equal(1, r.Report.Runs[0].CurveComponents);
        }

        [Fact]
        public void FreePinInTheSlot_IsRefusedByTheCurveRule_WithTwoCurves()
        {
            SheetRun r = Run("free pin in the slot");
            Assert.Equal(Status.CurveRule, r.Status);
            Assert.Null(r.Result);
            Assert.Equal(2, r.Report.Runs[0].CurveComponents);
            Assert.Equal(Status.CurveRule, r.Report.Runs[0].Refusal);
            // the witness is a point of the second curve: on the pin (x 9.51 .. 9.515, y 4.5 .. 5.5)
            Assert.True(r.Report.HasWitnessPoint);
            Assert.InRange(r.Report.WitnessPoint.x, 9.5, 9.52);
            Assert.InRange(r.Report.WitnessPoint.y, 4.4, 5.6);
            // the candidate was built, and the plain cut with it would have cut the pin's top off (the curve rule is what protects it)
            Assert.NotNull(r.Report.Candidate);
        }

        [Fact]
        public void ThickPin_ReachingTheOuterEdge_IsRoutedRound_AndStaysUntouched()
        {
            SheetRun r = Run("thick pin touching the outer edge (routed round)");
            Assert.Equal(Status.Success, r.Status);
            Assert.Equal(506.18, r.Kept, 9);
            Assert.True(r.Report.SubdivisionPoints > 0, "the outer edge is subdivided to go round the pin");
        }

        [Fact]
        public void RibOnTheSlotWall_CannotBeRoutedRound_AndIsRefused()
        {
            SheetRun r = Run("rib on the slot wall (no way round)");
            Assert.Equal(Status.InvertedQuad, r.Status);
            Assert.Null(r.Result);
        }

        [Fact]
        public void FreePinWhoseTopLiesInTheStripsPlane_IsRefusedAsACoplanarContact()
        {
            SheetRun r = Run("free pin whose top lies in the strip's plane");
            Assert.Equal(Status.CoplanarContact, r.Status);
            Assert.True(r.Report.HasWitnessPoint);
        }

        [Fact]
        public void Chord_BothEndsOutside_IsContinuedByEdge_WithTheOuterEdgeSubdivided()
        {
            SheetRun r = Run("chord across a sphere");
            Assert.Equal(Status.Success, r.Status);
            Assert.True(r.Report.SubdivisionPoints > 0);
            // both ends of the sheet's far edge are outside the sphere (radius 5: 6.02 from the centre): chosen by edge, not by vertex
            var tree = new DMeshAABBTree3(r.W, true);
            DMesh3 s = r.Sheet;
            int chord = -1;
            foreach (int e in s.BoundaryEdgeIndices()) {
                Index2i ev = s.GetEdgeV(e);
                Vector3d a = s.GetVertex(ev.a), b = s.GetVertex(ev.b);
                if (Math.Abs(a.y - 4.5) < 1e-12 && Math.Abs(b.y - 4.5) < 1e-12 && Math.Abs(a.x - b.x) == 8) chord = e;
            }
            Assert.True(chord >= 0);
            Index2i cv = s.GetEdgeV(chord);
            Assert.True(Math.Abs(tree.WindingNumber(s.GetVertex(cv.a))) < 0.5 && Math.Abs(tree.WindingNumber(s.GetVertex(cv.b))) < 0.5);
            Assert.Contains(chord, r.Report.Runs[0].EdgeIds);
            // the rim inside is the chord of the sphere (half length sqrt(25 - 4.5^2 - 0.5^2) = 2.12, a polyhedral sphere, so not exactly)
            Assert.InRange(r.Report.Runs[0].InsideLength, 4.0, 4.5);
        }

        [Fact]
        public void RectangleShortOnFourSides_AClosedRimWhollyInside_IsOneRun_WithCornerRaysThroughBoxEdges()
        {
            SheetRun r = Run("short on four sides, rays through box edges");
            Assert.Equal(Status.Success, r.Status);
            Assert.Single(r.Report.Runs);
            Assert.Equal(r.Sheet.BoundaryEdgeIndices().Count(), r.Report.Runs[0].Edges);
            Assert.Equal(r.Report.RimLength, r.Report.Runs[0].InsideLength, 9);
            Assert.Equal(1.0, r.Report.InsideLengthShareOfRim, 9);
            Assert.Equal(1, r.Report.Runs[0].CurveComponents);
            Assert.Equal(500, r.Kept, 9);
        }

        [Fact]
        public void RimVertexExactlyOnTheSurface_EndsTwoRuns_AndMakesNoDegenerateTriangle()
        {
            SheetRun r = Run("rim vertex exactly on the surface");
            Assert.Equal(Status.Success, r.Status);
            Assert.Equal(2, r.Report.Runs.Count);
            Assert.Equal(2, r.Report.CurveCount);
            SheetRunner.AssertStripTrianglesProper(r.Sheet, r.Result);
            Assert.Equal(500, r.Kept, 9);
        }

        [Fact]
        public void GrazingExit_IsRefused()
        {
            SheetRun r = Run("exit at 3 degrees");
            Assert.Equal(Status.GrazingExit, r.Status);
            Assert.Contains("exit angle", r.Report.Witness);
            Assert.True(r.Report.HasWitnessPoint);
            Assert.Equal(Status.Success, Run("exit at 20 degrees").Status);
        }

        [Fact]
        public void RaysThroughAnEdgeOrAVertexOfTheWorkpiece_AreAcceptedOnlyIfTheExactChecksHold()
        {
            // the octahedron: the corner rays run through its vertices, the others along its equator edges; accepted here, with the exact volume of the half
            SheetRun r = Run("octahedron, rays through its vertices and edges");
            Assert.Equal(Status.Success, r.Status);
            Assert.Equal(4.0 / 3 * 1000 / 2, r.Kept, 6);
            // a ray along a reflex edge: refused, the new point would be inside the material
            Assert.Equal(Status.NewPointNotOutside, Run("ray grazes a reflex edge").Status);
            // a ray that runs on a face after its exit: the new point is on the surface, not outside
            Assert.Equal(Status.NewPointNotOutside, Run("sheet in the plane of a step face").Status);
        }

        [Fact]
        public void NotchNarrowerThanTheDepth_IsRefused_NoInvertedQuadIsReturned()
        {
            SheetRun r = Run("notch narrower than the depth");
            Assert.Equal(Status.InvertedQuad, r.Status);
            Assert.Null(r.Result);
            Assert.Null(r.Report.Candidate);
            Assert.Contains("turned against", r.Report.Witness);
            Assert.True(r.Report.WitnessEdge >= 0);
        }

        [Fact]
        public void BowtieRimVertex_IsRefused_WithTheVertexAndItsPosition()
        {
            // the spike's choice, kept: a rim vertex with more than two rim edges gets no per-wedge points, the helper refuses with a named reason
            SheetRun r = Run("bowtie vertex on the rim");
            Assert.Equal(Status.BowtieVertex, r.Status);
            Assert.True(r.Report.WitnessVertex >= 0);
            Assert.Equal(new Vector3d(9.4, 5, 5), r.Report.WitnessPoint);
            Assert.Equal(new Vector3d(9.4, 5, 5), r.Sheet.GetVertex(r.Report.WitnessVertex));
        }

        [Fact]
        public void RimOutside_NextToAnotherPartOfTheWorkpiece_IsNotContinued()
        {
            SheetRun r = Run("rim outside, next to another part (U)");
            Assert.Equal(Status.Success, r.Status);
            Assert.False(r.Report.Extended);
            Assert.Equal(0, r.Report.NewTriangles);
            Assert.Equal(530, r.Kept, 9);
        }

        [Fact]
        public void AConcaveWorkpiece_UsesTheFirstExit_TheReEntryIsNeverReached()
        {
            SheetRun r = Run("wide pocket (concave, re-entry)");
            Assert.Equal(Status.Success, r.Status);
            Assert.Equal(400, r.Kept, 9);
            // the strip ends in the pocket (x < 9), not in the wall behind it (x 9 .. 10)
            foreach (int v in r.Result.VertexIndices())
                if (!r.Sheet.IsVertex(v))
                    Assert.True(r.Result.GetVertex(v).x < 9);
        }

        [Fact]
        public void SeveralRunsOnOneSheet_AreEachContinued()
        {
            SheetRun r = Run("two runs on one sheet");
            Assert.Equal(Status.Success, r.Status);
            Assert.Equal(2, r.Report.Runs.Count);
            Assert.All(r.Report.Runs, run => Assert.Equal(1, run.CurveComponents));
            Assert.Equal(2, r.Report.CurveCount);
            Assert.Equal(500, r.Kept, 9);
        }

        [Fact]
        public void ASheetThatAlreadyCutsThrough_IsReturnedUnchanged_NothingAdded()
        {
            SheetRun r = Run("a sheet that already cuts through");
            Assert.Equal(Status.Success, r.Status);
            Assert.False(r.Report.Extended);
            Assert.Equal(0, r.Report.NewTriangles);
            Assert.Equal(0, r.Report.NewVertices);
            Assert.Equal(0, r.Report.ChosenEdges);
            Assert.Equal("Success", r.Report.CutStatus);
            Assert.Equal("", r.Plain.Replace("Success", ""));
            for (int t = 0; t < r.Sheet.MaxTriangleID; ++t)
                Assert.Equal(r.Sheet.GetTriangle(t), r.Result.GetTriangle(t));
        }

        [Fact]
        public void CurvedSheets_AreContinuedStraight_UEstimatesTheDifference()
        {
            // a curved sheet is continued by the plane of each rim triangle; the cut differs from the smooth surface the sheet stands for by less than U
            foreach (string name in new[] { "cylinder segment 0.5 %", "spherical dome (stated sheet)", "shallow tray (stated sheet)" }) {
                SheetRun r = Run(name);
                Assert.Equal(Status.Success, r.Status);
                double diff = Math.Abs(r.Kept - r.Data.Intent);
                Assert.True(diff <= r.Report.U, name + ": the cut differs from the intent by " + diff + ", U is " + r.Report.U);
                Assert.True(diff > 0.1 * r.Report.U, name + ": U would be no estimate of the difference (" + diff + " against " + r.Report.U + ")");
                Assert.True(r.Report.U > 0 && r.Report.URelative > 0 && r.Report.URelativeToWorkpiece > 0);
            }
        }

        [Fact]
        public void SeveralSheets_OnlyTheRefusedOneIsContinued_AndAStripThatMeetsAnotherToolIsRefused()
        {
            SheetRun r = Run("several sheets, one short");
            Assert.Equal(Status.Success, r.Status);
            Assert.Equal(500, r.Kept, 9);
            Assert.Equal(Status.MeetsOtherTool, Run("strip crosses another sheet").Status);
            Assert.Equal(Status.MeetsItself, Run("folded sheet, strips converge").Status);
            Assert.Equal(Status.SheetRoleRefused, Run("UncutSide Outside, two shells").Status);
            Assert.Equal(ExactMeshBoolean.SheetRefusal.UncutComponents, Run("UncutSide Outside, two shells").Report.SheetReason);
        }

        // ---- refusals of the input -------------------------------------------------------------------------------------------------

        [Fact]
        public void OpenWorkpiece_IsRefused_WorkpieceNotClosed()
        {
            SheetCaseData d = Get("planar 0.5 %").Build();
            DMesh3 open = new DMesh3(d.W);
            open.RemoveTriangle(open.TriangleIndices().First());
            var op = new MeshSheetExtension { Sheet = d.Sheets[0], Workpiece = open };
            Assert.False(op.Compute());
            Assert.Equal(Status.WorkpieceNotClosed, op.Status);
            Assert.Null(op.Result);
            Assert.False(new MeshSheetExtension { Sheet = d.Sheets[0], Workpiece = open }.Diagnose());
        }

        [Fact]
        public void EmptyOrMissingInput_AndNonFiniteCoordinates_AreInvalidInput()
        {
            SheetCaseData d = Get("planar 0.5 %").Build();
            Assert.Equal(Status.InvalidInput, Compute(null, d.W));
            Assert.Equal(Status.InvalidInput, Compute(d.Sheets[0], null));
            Assert.Equal(Status.InvalidInput, Compute(new DMesh3(), d.W));
            DMesh3 bad = new DMesh3(d.Sheets[0]);
            bad.SetVertex(bad.VertexIndices().First(), new Vector3d(double.NaN, 0, 0));
            Assert.Equal(Status.InvalidInput, Compute(bad, d.W));
            Assert.Equal(Status.NotComputed, new MeshSheetExtension().Status);
        }

        static Status Compute(DMesh3 sheet, DMesh3 w)
        {
            var op = new MeshSheetExtension { Sheet = sheet, Workpiece = w };
            Assert.False(op.Compute());
            Assert.Null(op.Result);
            return op.Status;
        }

        static DMesh3 AddTriangleAtPositions(DMesh3 sheet, Vector3d a, Vector3d b, Vector3d c)
        {
            DMesh3 m = new DMesh3(sheet);
            m.AppendTriangle(m.AppendVertex(a), m.AppendVertex(b), m.AppendVertex(c));
            return m;
        }

        [Fact]
        public void SheetWithAnEdgeUsedByThreeTriangles_IsRefused_SheetNotSurface()
        {
            // a third triangle on an interior edge of the sheet, by position (its own vertices: a DMesh3 cannot share an edge three ways)
            SheetCaseData d = Get("planar 0.5 %").Build();
            DMesh3 s = d.Sheets[0];
            int e = s.EdgeIndices().First(x => !s.IsBoundaryEdge(x));
            Index2i ev = s.GetEdgeV(e);
            DMesh3 nm = AddTriangleAtPositions(s, s.GetVertex(ev.a), s.GetVertex(ev.b), s.GetVertex(ev.a) + new Vector3d(0, 0, 1));
            var op = new MeshSheetExtension { Sheet = nm, Workpiece = d.W };
            Assert.False(op.Compute());
            Assert.Equal(Status.SheetNotSurface, op.Status);
            Assert.Contains("more than two", op.Report.Witness);
            Assert.True(op.Report.HasWitnessPoint);
            Assert.Null(op.Result);
            // the sheet role refuses it as NotSurface too
            Assert.StartsWith("refused NotSurface", SheetRunner.PlainCut(d.W, new List<DMesh3> { nm }, ExactMeshBoolean.SheetUncutSides.Refuse, out int _));
        }

        [Fact]
        public void SeamAtARimEdgeThatMeetsTheWorkpiece_IsRefused_SheetNotSurface()
        {
            // the sheet z = 5 as two welded halves that share their seam x = 5 by position only (unwelded duplicate vertices): the seam edges are free edges of the DMesh3
            DMesh3 left = SheetGeometry.Grid(new[] { -2.3, 1.1, 5.0 }, new[] { -2.3, 5.0, 12.1 }, (x, y) => 5, true);
            DMesh3 right = SheetGeometry.Grid(new[] { 5.0, 7.5, 9.9 }, new[] { -2.3, 5.0, 12.1 }, (x, y) => 5, true);
            DMesh3 two = SheetGeometry.Merge(left, right);
            var op = new MeshSheetExtension { Sheet = two, Workpiece = SheetGeometry.Box(0, 0, 0, 10, 10, 10) };
            Assert.False(op.Compute());
            Assert.Equal(Status.SheetNotSurface, op.Status);
            Assert.Contains("seam", op.Report.Witness);
            Assert.True(op.Report.WitnessEdge >= 0);
        }

        [Fact]
        public void InputsAreNotModified_AndTheResultIsAnotherMesh()
        {
            SheetCaseData d = Get("slot and fin").Build();
            DMesh3 s0 = new DMesh3(d.Sheets[0]), w0 = new DMesh3(d.W);
            var op = new MeshSheetExtension { Sheet = d.Sheets[0], Workpiece = d.W };
            Assert.True(op.Compute());
            Assert.NotSame(d.Sheets[0], op.Result);
            Assert.Equal(s0.TriangleCount, d.Sheets[0].TriangleCount);
            Assert.Equal(s0.VertexCount, d.Sheets[0].VertexCount);
            foreach (int v in s0.VertexIndices()) Assert.Equal(s0.GetVertex(v), d.Sheets[0].GetVertex(v));
            foreach (int v in w0.VertexIndices()) Assert.Equal(w0.GetVertex(v), d.W.GetVertex(v));
        }

        [Fact]
        public void VerifyWithSheetRoleOff_GivesTheSameSheet_WithoutTheCut()
        {
            SheetCaseData d = Get("planar 0.5 %").Build();
            var on = new MeshSheetExtension { Sheet = d.Sheets[0], Workpiece = d.W };
            var off = new MeshSheetExtension { Sheet = d.Sheets[0], Workpiece = d.W, VerifyWithSheetRole = false };
            Assert.True(on.Compute());
            Assert.True(off.Compute());
            Assert.Null(off.Report.Cut);
            Assert.Equal(0, off.Report.CutMs);
            Assert.Equal(on.Result.TriangleCount, off.Result.TriangleCount);
            foreach (int v in on.Result.VertexIndices()) Assert.Equal(on.Result.GetVertex(v), off.Result.GetVertex(v));
        }

        [Fact]
        public void LimitOptions_AreUsed()
        {
            // the planar 2 % sheet: refused with a limit of 1 %, continued with 3 %
            SheetCaseData d = Get("planar 2 %").Build();
            var tight = new MeshSheetExtension { Sheet = d.Sheets[0], Workpiece = d.W, MaxExtension = 0.01 };
            Assert.False(tight.Compute());
            Assert.Equal(Status.DepthAboveLimit, tight.Status);
            Assert.True(new MeshSheetExtension { Sheet = d.Sheets[0], Workpiece = d.W, MaxExtension = 0.03 }.Compute());
            // the limit on U relative to the smaller part
            var u = new MeshSheetExtension { Sheet = d.Sheets[0], Workpiece = d.W, MaxUOverSmallerPart = 1e-9 };
            Assert.False(u.Compute());
            Assert.Equal(Status.UncertaintyAboveLimit, u.Status);
        }
    }
}
