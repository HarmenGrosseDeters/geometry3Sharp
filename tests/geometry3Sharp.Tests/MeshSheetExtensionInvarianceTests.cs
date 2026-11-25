using System;
using System.Collections.Concurrent;
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
    /// MeshSheetExtension under the similarities of Invariance.cs (Similarity.All()), reuse (Compute() again on one instance, also after a refusal) and determinism
    /// (repeated runs, serial equals parallel). The scalings by 2^k give the same extended sheet bit for bit; the other similarities (a UTM shift, scalings by 1e-3 and
    /// 1e3, rotations) give the same outcome and the same volume within a tolerance: the strip at a corner of a curved rim depends on the frame by rounding, and the
    /// bounding box diagonal that the limits are shares of is not rotation invariant.
    /// </summary>
    public class MeshSheetExtensionInvarianceTests : IClassFixture<MeshSheetExtensionInvarianceTests.BaselineFixture>
    {
        /// <summary> the identity-frame runs of the cases below, made once (a fixture's time is not a test's) </summary>
        public sealed class BaselineFixture
        {
            internal readonly Dictionary<string, SheetRun> Runs = new Dictionary<string, SheetRun>();
            public BaselineFixture()
            {
                foreach (string name in Groups.SelectMany(g => g).Concat(new[] { "slot and fin" }))
                    if (!Runs.ContainsKey(name)) Runs[name] = SheetRunner.Run(MeshSheetExtensionTests.Get(name));
            }
        }

        readonly BaselineFixture fixture;
        public MeshSheetExtensionInvarianceTests(BaselineFixture fixture) { this.fixture = fixture; }

        // the cases of the invariance runs, in three groups of about the same cost: continued, continued with corners, refused (and unchanged)
        static readonly string[][] Groups = {
            new[] { "planar 0.5 %", "bent 2 %", "slot and fin", "thick pin touching the outer edge (routed round)", "two runs on one sheet", "chord across a sphere" },
            new[] { "short on four sides, rays through box edges", "cylinder segment 0.5 %", "wide pocket (concave, re-entry)", "exit at 20 degrees", "rim vertex exactly on the surface" },
            new[] { "planar 8 %", "free pin in the slot", "notch narrower than the depth", "exit at 3 degrees", "bowtie vertex on the rim", "a sheet that already cuts through", "rim outside, next to another part (U)" }
        };

        SheetRun Baseline(string name) { return fixture.Runs[name]; }

        public static IEnumerable<object[]> FrameAndGroup()
        {
            foreach (Similarity s in Similarity.All())
                for (int g = 0; g < Groups.Length; ++g)
                    yield return new object[] { s.Name, g };
        }

        [Theory]
        [MemberData(nameof(FrameAndGroup))]
        public void SimilarityAll_GivesTheSameOutcome_TheSameVolume_AndForPowersOfTwoTheSameSheetBitForBit(string frame, int group)
        {
            Similarity s = Similarity.ByName(frame);
            foreach (string name in Groups[group]) {
                SheetRun b = Baseline(name);
                SheetRun r = SheetRunner.Run(MeshSheetExtensionTests.Get(name), p => s.Apply(p));
                string label = name + " in " + frame;
                // the same outcome, and for a refusal the same reason
                Assert.True(b.Status == r.Status, label + ": " + b.Status + " in the identity frame, " + r.Status + " here: " + r.Report.Witness);
                Assert.Equal(b.Actual, r.Actual);
                if (b.Status != Status.Success) {
                    Assert.Null(r.Result);
                    continue;
                }
                double s3 = s.Scale * s.Scale * s.Scale;
                double vw = Math.Abs(SheetRunner.Volume(r.W));
                // the volume, mapped back, is the baseline's within the rounding of the frame (the UTM shifts cost digits: 5.7e6 against a size of 10)
                double tol = s.Translation != Vector3d.Zero || s.IsRotated ? 1e-5 : 1e-9;
                Assert.True(Math.Abs(r.Kept / s3 - b.Kept) <= tol * vw / s3, label + ": kept " + r.Kept / s3 + " against " + b.Kept);
                Assert.Equal("", r.NotClean);
                Assert.Equal(b.Report.Extended, r.Report.Extended);
                Assert.Equal(b.Report.Runs.Count, r.Report.Runs.Count);
                if (s.IsExact) {
                    // a scaling by 2^k: the extended sheet is the baseline's, scaled, every vertex and triangle bit for bit; the numbers of the report scale
                    Assert.Equal(b.Result.MaxVertexID, r.Result.MaxVertexID);
                    Assert.Equal(b.Result.MaxTriangleID, r.Result.MaxTriangleID);
                    foreach (int v in b.Result.VertexIndices()) {
                        Vector3d a = b.Result.GetVertex(v) * s.Scale, c = r.Result.GetVertex(v);
                        Assert.True(a.x == c.x && a.y == c.y && a.z == c.z, label + ": vertex " + v + " differs");
                    }
                    foreach (int t in b.Result.TriangleIndices())
                        Assert.Equal(b.Result.GetTriangle(t), r.Result.GetTriangle(t));
                    Assert.Equal(b.Report.MaxDepthRelative, r.Report.MaxDepthRelative);
                    Assert.Equal(b.Report.CurveCount, r.Report.CurveCount);
                    Assert.Equal(b.Report.NewTriangles, r.Report.NewTriangles);
                    Assert.Equal(b.Report.U * s3, r.Report.U, 12 - (int)Math.Max(0, Math.Log10(Math.Max(1e-300, Math.Abs(b.Report.U * s3)))));
                }
                else if (b.Report.Extended) {
                    Assert.Equal(b.Report.CurveCount, r.Report.CurveCount);
                    Assert.InRange(r.Report.U / s3, b.Report.U * 0.9, b.Report.U * 1.1);   // U follows the strip's points of subdivision, which follow the frame
                }
            }
        }

        [Fact]
        public void TheRelativeDepth_IsTheBoundingBoxDiagonals_NotRotationInvariant()
        {
            // documented, not hidden: the measure of the sheet's size is the diagonal of its box, which a rotation changes; the absolute depth is the same
            SheetRun b = Baseline("slot and fin");
            SheetRun r = SheetRunner.Run(MeshSheetExtensionTests.Get("slot and fin"), p => Similarity.Rotated.Apply(p));
            Assert.Equal(Status.Success, r.Status);
            Assert.InRange(r.Report.MaxDepth, b.Report.MaxDepth * 0.999999, b.Report.MaxDepth * 1.000001);
            Assert.True(Math.Abs(r.Report.Diagonal / b.Report.Diagonal - 1) > 0.05, "the diagonal of the rotated box differs: " + r.Report.Diagonal + " against " + b.Report.Diagonal);
        }

        [Theory]
        [InlineData("planar 0.5 %", -60)]
        [InlineData("planar 0.5 %", 60)]
        [InlineData("slot and fin", -100)]
        [InlineData("slot and fin", 100)]
        [InlineData("planar 8 %", -100)]
        public void ExtremeTwoToTheKScales_GiveTheSameOutcome_AndTheSameSheetBitForBit(string name, int k)
        {
            // far beyond the frames of Similarity.All(): a sheet of 1e-30 or 1e30 (DMesh3.GetTriNormal alone would give the zero normal of a triangle below 1e-8: NoDirection)
            double sc = Math.Pow(2, k);
            SheetRun b = Baseline(name);
            SheetRun r = SheetRunner.Run(MeshSheetExtensionTests.Get(name), p => p * sc);
            Assert.Equal(b.Status, r.Status);
            if (b.Status != Status.Success) { Assert.Equal(b.Report.MaxDepthRelative, r.Report.MaxDepthRelative); return; }
            foreach (int v in b.Result.VertexIndices()) {
                Vector3d a = b.Result.GetVertex(v) * sc, c = r.Result.GetVertex(v);
                Assert.True(a.x == c.x && a.y == c.y && a.z == c.z, name + " 2^" + k + ": vertex " + v + " differs");
            }
            Assert.Equal(b.Result.TriangleCount, r.Result.TriangleCount);
            Assert.Equal("", r.NotClean);
        }

        [Fact]
        public void CoordinatesBeyondTheRange_AreInvalidInput_NotAnOverflow()
        {
            SheetCaseData d = MeshSheetExtensionTests.Get("planar 0.5 %").Build();
            DMesh3 far = SheetGeometry.Transformed(d.Sheets[0], p => p + new Vector3d(2e100, 0, 0));
            var op = new MeshSheetExtension { Sheet = far, Workpiece = d.W };
            Assert.False(op.Compute());
            Assert.Equal(Status.InvalidInput, op.Status);
            Assert.False(op.Diagnose());
        }

        [Theory]
        [MemberData(nameof(Frames))]
        public void TheRibOnTheSlotWall_IsRefusedOrCutRight_InEveryFrame(string frame)
        {
            // a finding of the build, kept as a test: the design says the rib is refused (a monotone strip cannot go round it). In the identity frame it is (InvertedQuad); in a rotated frame the
            // subdivision happens to find a strip that goes round, and the cut is right (the rib above the sheet is kept: 506.12). Never a wrong cut, but not frame invariant either
            Similarity s = Similarity.ByName(frame);
            SheetRun r = SheetRunner.Run(MeshSheetExtensionTests.Get("rib on the slot wall (no way round)"), p => s.Apply(p));
            double s3 = s.Scale * s.Scale * s.Scale;
            if (r.Status == Status.Success)
                Assert.True(Math.Abs(r.Kept / s3 - 506.12) <= 1e-5 * 1000, frame + ": accepted with the volume " + r.Kept / s3);
            else
                Assert.Equal(Status.InvertedQuad, r.Status);
            if (!s.IsRotated)
                Assert.Equal(Status.InvertedQuad, r.Status);
        }

        public static IEnumerable<object[]> Frames() => Similarity.Names();

        // ---- reuse -------------------------------------------------------------------------------------------------------------------

        static void AssertSameSheet(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.MaxVertexID, b.MaxVertexID);
            Assert.Equal(a.MaxTriangleID, b.MaxTriangleID);
            foreach (int v in a.VertexIndices()) {
                Vector3d p = a.GetVertex(v), q = b.GetVertex(v);
                Assert.True(p.x == q.x && p.y == q.y && p.z == q.z);
            }
            foreach (int t in a.TriangleIndices()) Assert.Equal(a.GetTriangle(t), b.GetTriangle(t));
        }

        [Fact]
        public void Compute_CanBeCalledAgain_AlsoAfterARefusal_AndGivesWhatAFreshInstanceGives()
        {
            SheetCaseData good = MeshSheetExtensionTests.Get("slot and fin").Build();
            SheetCaseData bad = MeshSheetExtensionTests.Get("free pin in the slot").Build();
            var fresh = new MeshSheetExtension { Sheet = good.Sheets[0], Workpiece = good.W };
            Assert.True(fresh.Compute());

            var op = new MeshSheetExtension { Sheet = good.Sheets[0], Workpiece = good.W };
            Assert.True(op.Compute());
            DMesh3 first = op.Result;
            Assert.True(op.Compute());
            Assert.NotSame(first, op.Result);
            AssertSameSheet(first, op.Result);
            AssertSameSheet(fresh.Result, op.Result);
            Assert.Equal(fresh.Report.U, op.Report.U);
            Assert.Equal(fresh.Report.NewTriangles, op.Report.NewTriangles);

            // a refusal on the same instance: no result, the status and the witness of this call only
            op.Sheet = bad.Sheets[0]; op.Workpiece = bad.W;
            Assert.False(op.Compute());
            Assert.Equal(Status.CurveRule, op.Status);
            Assert.Null(op.Result);
            Assert.NotNull(op.Error);
            Assert.Equal(Status.CurveRule, op.Report.Status);
            Assert.False(op.Report.Extended || op.Report.Cut != null);
            Assert.False(op.Compute());
            Assert.Equal(Status.CurveRule, op.Status);

            // and a success after it: the same as the fresh instance, with no trace of the refusal
            op.Sheet = good.Sheets[0]; op.Workpiece = good.W;
            Assert.True(op.Compute());
            Assert.Null(op.Error);
            AssertSameSheet(fresh.Result, op.Result);
            Assert.Equal(Status.Success, op.Report.Status);
            Assert.Equal("", op.Report.Witness);
            Assert.Equal(-1, op.Report.WitnessEdge);
        }

        // ---- determinism -----------------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("slot and fin")]
        [InlineData("chord across a sphere")]
        [InlineData("spherical dome (stated sheet)")]
        [InlineData("free pin in the slot")]
        public void RepeatedRuns_AndSerialAgainstParallel_GiveTheSameBits(string name)
        {
            SheetRun a = SheetRunner.Run(MeshSheetExtensionTests.Get(name));
            SheetRun b = SheetRunner.Run(MeshSheetExtensionTests.Get(name));
            SheetRun serial;
            gParallel.SerialOnThisThread = true;
            try { serial = SheetRunner.Run(MeshSheetExtensionTests.Get(name)); }
            finally { gParallel.SerialOnThisThread = false; }
            foreach (SheetRun other in new[] { b, serial }) {
                Assert.Equal(a.Status, other.Status);
                Assert.Equal(a.Report.Witness, other.Report.Witness);
                Assert.Equal(a.Report.U, other.Report.U);
                Assert.Equal(a.Report.MaxDepth, other.Report.MaxDepth);
                Assert.Equal(a.Report.NewTriangles, other.Report.NewTriangles);
                Assert.Equal(a.Report.SubdivisionPoints, other.Report.SubdivisionPoints);
                if (a.Status == Status.Success) {
                    AssertSameSheet(a.Result, other.Result);
                    Assert.Equal(a.Kept, other.Kept);
                }
                else AssertSameSheet(a.Report.Candidate, other.Report.Candidate);
            }
        }
    }
}
