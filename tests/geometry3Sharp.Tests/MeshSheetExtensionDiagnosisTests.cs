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
    /// MeshSheetExtension.Diagnose(): the diagnosis (deliverable A of docs/design/SHEET_EXTENSION.md) without the strip, the imprint or the cut: where the rim of a
    /// sheet is inside the workpiece, how deep, how long, "no exit within the limit", and the estimate U. Procedural inputs.
    /// </summary>
    public class MeshSheetExtensionDiagnosisTests
    {
        static MeshSheetExtension Diagnose(string name, int sheet = 0, Action<MeshSheetExtension> set = null)
        {
            SheetCaseData d = MeshSheetExtensionTests.Get(name).Build();
            var op = new MeshSheetExtension { Sheet = d.Sheets[sheet], Workpiece = d.W };
            set?.Invoke(op);
            Assert.True(op.Diagnose(), op.Status + " " + op.Error);
            return op;
        }

        [Fact]
        public void Diagnosis_BuildsNothing_AndRunsNeitherTheImprintNorTheCut()
        {
            MeshSheetExtension op = Diagnose("planar 0.5 %");
            Assert.Equal(Status.Success, op.Status);
            Assert.Null(op.Result);
            Assert.Null(op.Error);
            SheetExtensionReport r = op.Report;
            Assert.True(r.Diagnosed);
            Assert.False(r.Extended);
            Assert.Equal(0, r.NewTriangles);
            Assert.Equal(0, r.NewVertices);
            Assert.Null(r.Candidate);
            Assert.Null(r.Cut);
            Assert.Equal(0, r.ImprintMs);
            Assert.Equal(0, r.CutMs);
            Assert.Equal(-1, r.Runs[0].CurveComponents);
            Assert.True(double.IsNaN(r.Runs[0].StripAreaInside));
        }

        [Theory]
        [InlineData("planar 0.1 %", 0.001)]
        [InlineData("planar 0.5 %", 0.005)]
        [InlineData("planar 2 %", 0.02)]
        public void ThePlanarRim_HasTheDepth_TheLengthInside_AndTheShareOfTheDiagonal(string name, double share)
        {
            MeshSheetExtension op = Diagnose(name);
            SheetExtensionReport r = op.Report;
            Assert.Single(r.Runs);
            SheetExtensionRun run = r.Runs[0];
            // the rim edge x = xe runs from y = -2.3 to 12.1; the part inside the cube is y in [0, 10]
            Assert.Equal(10.0, run.InsideLength, 9);
            Assert.Equal(10.0, r.InsideLength, 9);
            Assert.Equal(10.0 / r.Diagonal, run.InsideLengthRelative, 9);
            Assert.Equal(share, run.MaxDepthRelative, 6);
            Assert.Equal(run.MaxDepth, run.MeanDepth, 9);
            Assert.Equal(share * r.Diagonal, run.MaxDepth, 6);
            Assert.False(run.NoExitWithinLimit);
            Assert.False(run.GrazingExit);
            Assert.Equal(r.InsideLength / r.RimLength, r.InsideLengthShareOfRim, 12);
            // U is an estimate of the cut's change under a tilt of the continuation, relative to the workpiece's volume (the smaller part needs a cut)
            Assert.True(run.U > 0);
            Assert.Equal(run.U / 1000, run.URelativeToWorkpiece, 12);
            Assert.True(double.IsNaN(run.URelative));
        }

        [Theory]
        [InlineData("planar 0.5 %")]
        [InlineData("planar 2 %")]
        [InlineData("slot and fin")]
        [InlineData("wide pocket (concave, re-entry)")]
        [InlineData("chord across a sphere")]
        [InlineData("short on four sides, rays through box edges")]
        public void TheDiagnosis_AgreesWithTheExtensionItWouldMake(string name)
        {
            MeshSheetExtension dg = Diagnose(name);
            SheetCaseData d = MeshSheetExtensionTests.Get(name).Build();
            var op = new MeshSheetExtension { Sheet = d.Sheets[0], Workpiece = d.W, VerifyWithSheetRole = false };
            Assert.True(op.Compute());
            Assert.Equal(op.Report.Runs.Count, dg.Report.Runs.Count);
            Assert.Equal(op.Report.ChosenEdges, dg.Report.ChosenEdges);
            Assert.Equal(op.Report.RimLength, dg.Report.RimLength, 12);
            // the length inside is measured the same way
            Assert.Equal(op.Report.InsideLength, dg.Report.InsideLength, 9);
            // the mean depth and U come from the same samples in both modes: equal (the review found Compute's mean biased high on the chord and the test blamed the diagnosis);
            // the largest depth of the diagnosis is a lower bound of the build's (the strip's points are rays too)
            Assert.InRange(dg.Report.MaxDepth, 0.9 * op.Report.MaxDepth, op.Report.MaxDepth * (1 + 1e-9));
            Assert.Equal(dg.Report.U, op.Report.U, 12);
            Assert.Equal(dg.Report.Runs[0].MeanDepth, op.Report.Runs[0].MeanDepth, 12);
        }

        [Fact]
        public void TheChord_BothEndsOutside_HasADepthAndALengthInside()
        {
            MeshSheetExtension op = Diagnose("chord across a sphere");
            SheetExtensionRun run = op.Report.Runs[0];
            Assert.Equal(1, run.Edges);
            Assert.InRange(run.InsideLength, 4.0, 4.5);
            Assert.True(run.MaxDepth > 0.3, "the depth of a rim edge with both ends outside is sampled inside it: " + run.MaxDepth);
            Assert.False(run.DepthAboveLimit);
        }

        [Theory]
        [InlineData("planar 8 %", 0.08)]
        [InlineData("bent 8 %", 0.0835)]
        [InlineData("slit ends in the middle", 0.31)]
        public void ARimDeeperThanTheLimit_SaysNoExitWithinTheLimit_AndHowDeepItIs(string name, double depthShare)
        {
            MeshSheetExtension op = Diagnose(name);
            SheetExtensionRun run = op.Report.Runs[0];
            Assert.True(run.DepthAboveLimit);
            Assert.True(run.NoExitWithinLimit);
            Assert.Equal(depthShare, run.MaxDepthRelative, 2);
            Assert.Equal(depthShare, op.Report.MaxDepthRelative, 2);
            Assert.Contains("above the limit", run.Witness);
            Assert.True(op.Report.WitnessEdge >= 0 && op.Report.HasWitnessPoint);
            // the same sheet with a limit above the depth: no flag
            Assert.False(Diagnose(name, 0, o => o.MaxExtension = 0.5).Report.Runs[0].NoExitWithinLimit);
            // the length of the rim inside, relative to the diagonal, is what a caller reports as "the rim is N % of the diagonal inside the workpiece"
            Assert.InRange(op.Report.InsideLengthRelative, 0.5, 0.65);
        }

        [Fact]
        public void AGrazingExit_IsFlagged_AndTheEstimateGrows()
        {
            MeshSheetExtension graze = Diagnose("exit at 3 degrees");
            Assert.True(graze.Report.Runs[0].GrazingExit);
            MeshSheetExtension steep = Diagnose("exit at 20 degrees");
            Assert.False(steep.Report.Runs[0].GrazingExit);
            Assert.True(graze.Report.Runs[0].U > 10 * steep.Report.Runs[0].U);
        }

        [Fact]
        public void ARimThatDoesNotMeetTheInterior_HasNoRuns()
        {
            foreach (string name in new[] { "a sheet that already cuts through", "rim outside, next to another part (U)" }) {
                MeshSheetExtension op = Diagnose(name);
                Assert.Empty(op.Report.Runs);
                Assert.Equal(0, op.Report.InsideLength);
                Assert.Equal(0, op.Report.U);
            }
        }

        [Fact]
        public void SeveralRuns_AreReportedEach()
        {
            MeshSheetExtension op = Diagnose("two runs on one sheet");
            Assert.Equal(2, op.Report.Runs.Count);
            Assert.All(op.Report.Runs, run => Assert.Equal(10.0, run.InsideLength, 9));
            Assert.Equal(20.0, op.Report.InsideLength, 9);
            Assert.Equal(op.Report.Runs.Sum(x => x.U), op.Report.U, 12);
            Assert.Equal(op.Report.Runs.Max(x => x.MaxDepth), op.Report.MaxDepth, 12);
        }

        [Fact]
        public void BowtieAndEmptyInput_CannotBeDiagnosed()
        {
            SheetCaseData d = MeshSheetExtensionTests.Get("bowtie vertex on the rim").Build();
            var op = new MeshSheetExtension { Sheet = d.Sheets[0], Workpiece = d.W };
            Assert.False(op.Diagnose());
            Assert.Equal(Status.BowtieVertex, op.Status);
            Assert.True(op.Report.WitnessVertex >= 0);
            var empty = new MeshSheetExtension { Sheet = new DMesh3(), Workpiece = d.W };
            Assert.False(empty.Diagnose());
            Assert.Equal(Status.InvalidInput, empty.Status);
        }

        [Fact]
        public void Diagnosis_CanBeRepeated_AndMixedWithCompute_OnOneInstance()
        {
            SheetCaseData d = MeshSheetExtensionTests.Get("slot and fin").Build();
            var op = new MeshSheetExtension { Sheet = d.Sheets[0], Workpiece = d.W };
            Assert.True(op.Diagnose());
            double u1 = op.Report.U, g1 = op.Report.MaxDepth;
            Assert.True(op.Compute());
            Assert.NotNull(op.Result);
            Assert.False(op.Report.Diagnosed);
            Assert.True(op.Diagnose());
            Assert.Null(op.Result);
            Assert.True(op.Report.Diagnosed);
            Assert.Equal(u1, op.Report.U);
            Assert.Equal(g1, op.Report.MaxDepth);
            Assert.True(op.Compute());
        }

        [Fact]
        public void Diagnosis_IsDeterministic_AndTheTwoToTheKScalesGiveTheScaledNumbersBitForBit()
        {
            MeshSheetExtension a = Diagnose("slot and fin"), b = Diagnose("slot and fin");
            Assert.Equal(a.Report.U, b.Report.U);
            Assert.Equal(a.Report.MaxDepth, b.Report.MaxDepth);
            foreach (int k in new[] { -7, 5, 20 }) {
                double sc = Math.Pow(2, k);
                SheetCaseData d = MeshSheetExtensionTests.Get("slot and fin").Build();
                var op = new MeshSheetExtension { Sheet = SheetGeometry.Transformed(d.Sheets[0], p => p * sc), Workpiece = SheetGeometry.Transformed(d.W, p => p * sc) };
                Assert.True(op.Diagnose());
                Assert.Equal(a.Report.MaxDepth * sc, op.Report.MaxDepth);
                Assert.Equal(a.Report.MaxDepthRelative, op.Report.MaxDepthRelative);
                Assert.Equal(a.Report.InsideLength * sc, op.Report.InsideLength);
                Assert.Equal(a.Report.Runs[0].MeanDepth * sc, op.Report.Runs[0].MeanDepth);
                Assert.InRange(op.Report.U, a.Report.U * sc * sc * sc * (1 - 1e-12), a.Report.U * sc * sc * sc * (1 + 1e-12));
            }
        }

        public static IEnumerable<object[]> Frames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(Frames))]
        public void Diagnosis_AgreesInEveryFrame_OfSimilarityAll(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            MeshSheetExtension a = Diagnose("planar 0.5 %");
            SheetCaseData d = MeshSheetExtensionTests.Get("planar 0.5 %").Build();
            var op = new MeshSheetExtension { Sheet = SheetGeometry.Transformed(d.Sheets[0], p => s.Apply(p)), Workpiece = SheetGeometry.Transformed(d.W, p => s.Apply(p)) };
            Assert.True(op.Diagnose());
            Assert.Single(op.Report.Runs);
            // the lengths scale (the diagonal of a rotated sheet is not that of the box, so the ratios are not compared)
            double tol = s.Name.Contains("utm") ? 1e-4 : 1e-8;
            Assert.InRange(op.Report.InsideLength / s.Scale, a.Report.InsideLength * (1 - tol), a.Report.InsideLength * (1 + tol));
            Assert.InRange(op.Report.MaxDepth / s.Scale, a.Report.MaxDepth * (1 - tol), a.Report.MaxDepth * (1 + tol));
            Assert.InRange(op.Report.U / (s.Scale * s.Scale * s.Scale), a.Report.U * (1 - 1e-3), a.Report.U * (1 + 1e-3));
        }
    }
}
