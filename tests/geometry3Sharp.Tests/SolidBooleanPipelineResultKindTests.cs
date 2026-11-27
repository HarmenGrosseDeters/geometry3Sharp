using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1576: SolidBooleanReport.ResultKind (a fact next to Status: a clean solid, a solid with contacts, another defect, an exact empty
    /// result, an empty result after the pipeline's removals, no result) and SolidBooleanReport.InputsWithWindingOutsideZeroOne (the
    /// closed inputs that overlap themselves or are inside out). On master neither member exists: the repros A, B and C all read
    /// "Success" with Clean false and cannot be told apart, and a self-overlapping or inside-out input gives no signal.
    /// </summary>
    public class SolidBooleanPipelineResultKindTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union, I = ExactMeshBoolean.Operations.Intersection, D = ExactMeshBoolean.Operations.Difference;

        static DMesh3 Grid(Vector3d c, Vector3d h, int ev = 6) { return new GridBox3Generator() { Box = new Box3d(c, h), EdgeVertices = ev }.Generate().MakeDMesh(); }

        static DMesh3 Box(double cx, double cy, double cz, double h, bool flip = false)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(cx, cy, cz), new Vector3d(h, h, h)), NoSharedVertices = false }.Generate().MakeDMesh();
            if (flip)
                m.ReverseOrientation();
            return m;
        }

        static DMesh3 Together(params DMesh3[] parts)
        {
            var m = new DMesh3(parts[0]);
            for (int i = 1; i < parts.Length; ++i)
                MeshEditor.Append(m, parts[i]);
            return m;
        }

        // the repros of the issue: a is the box of half 0.5 at the origin, b a box that touches it in the plane x = 0.5 (moved by `into` into it)
        static DMesh3 ReproA() { return Grid(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5)); }
        static DMesh3 ReproB(double into = 0) { return Grid(new Vector3d(0.85 - into, 0.1, 0.2), new Vector3d(0.35, 0.3, 0.3)); }

        static SolidBooleanPipeline Run(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op, Action<SolidBooleanPipeline> set = null)
        {
            var p = new SolidBooleanPipeline(a, b, op);
            if (set != null)
                set(p);
            p.Compute();
            return p;
        }

        static DMesh3 OpenBox(double cx)
        {
            DMesh3 open = Box(cx, 0, 0, 1);
            open.RemoveTriangle(0);
            return open;
        }

        // ---- ResultKind ----

        [Fact]
        public void BeforeCompute_AndAfterAFailedRun_TheKindIsNone()
        {
            var p = new SolidBooleanPipeline(Box(0, 0, 0, 1), Box(0.5, 0, 0, 1), U);
            Assert.Equal(SolidBooleanResultKind.None, p.Report.ResultKind);
            Assert.Empty(p.Report.InputsWithWindingOutsideZeroOne);
            // an open input is refused (Status OpenInput): no result
            var f = Run(Box(0, 0, 0, 1), OpenBox(0.5), U);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, f.Status);
            Assert.Null(f.Result);
            Assert.Equal(SolidBooleanResultKind.None, f.Report.ResultKind);
            // a failure of the options
            var bad = Run(Box(0, 0, 0, 1), Box(0.5, 0, 0, 1), U, x => { x.TargetFraction = 0.5; x.TargetTriangleCount = 10; });
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, bad.Status);
            Assert.Equal(SolidBooleanResultKind.None, bad.Report.ResultKind);
        }

        [Fact]
        public void ReproA_TwoBoxesTouchingInAPlane_IntersectionIsEmptyExact()
        {
            var p = Run(ReproA(), ReproB(), I);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.Equal(0, p.Result.TriangleCount);
            Assert.Equal("empty", p.Report.NotCleanReason);
            Assert.False(p.Report.Clean);
            Assert.Empty(p.Report.RoundingRemoved);
            Assert.Equal(SolidBooleanResultKind.EmptyExact, p.Report.ResultKind);
        }

        [Fact]
        public void DisjointBoxes_Intersection_IsEmptyExact_AndMinThicknessDoesNotChangeIt()
        {
            var p = Run(Box(0, 0, 0, 1), Box(5, 0, 0, 1), I);
            Assert.Equal(SolidBooleanResultKind.EmptyExact, p.Report.ResultKind);
            var q = Run(Box(0, 0, 0, 1), Box(5, 0, 0, 1), I, x => x.MinThickness = 0.01);
            Assert.Equal(SolidBooleanResultKind.EmptyExact, q.Report.ResultKind);
        }

        [Fact]
        public void ReproB_ASphereTouchingAFace_UnionIsSolidWithContacts()
        {
            DMesh3 b = new Sphere3Generator_NormalizedCube() { Radius = 0.4, EdgeVertices = 9 }.Generate().MakeDMesh();
            MeshTransforms.Translate(b, new Vector3d(0.9, 0.15, 0.1));
            var p = Run(ReproA(), b, U);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.True(p.Result.TriangleCount > 0);
            Assert.Equal("self-intersections", p.Report.NotCleanReason);
            Assert.True(p.Report.OnlyContacts);
            Assert.Equal(SolidBooleanResultKind.SolidWithContacts, p.Report.ResultKind);
        }

        [Fact]
        public void ReproC_AnOverlapOf1e12_IntersectionIsEmptyAfterRounding_ThickerOnesAreSolids()
        {
            var p = Run(ReproA(), ReproB(1e-12), I);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.Equal(0, p.Result.TriangleCount);
            Assert.Equal("empty", p.Report.NotCleanReason);
            Assert.Single(p.Report.RoundingRemoved);
            Assert.True(p.Report.RoundingRemoved[0].Volume > 0);
            Assert.Equal(SolidBooleanResultKind.EmptyAfterRounding, p.Report.ResultKind);
            // at 1e-9 the slab is kept
            var kept = Run(ReproA(), ReproB(1e-9), I);
            Assert.True(kept.Result.TriangleCount > 0);
            Assert.Empty(kept.Report.RoundingRemoved);
            Assert.Equal(SolidBooleanResultKind.Solid, kept.Report.ResultKind);
        }

        [Fact]
        public void ARemovalFromANonEmptyResult_StaysSolid()
        {
            // a: two boxes; b: two boxes. The first pair overlaps by a lot, the second by 1e-12: the slab is removed, the rest stays
            DMesh3 a = Together(ReproA(), Grid(new Vector3d(0, 5, 0), new Vector3d(0.5, 0.5, 0.5)));
            DMesh3 b = Together(Grid(new Vector3d(0.3, 0, 0), new Vector3d(0.35, 0.3, 0.3)), Grid(new Vector3d(0.85 - 1e-12, 5.1, 0.2), new Vector3d(0.35, 0.3, 0.3)));
            var p = Run(a, b, I);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.True(p.Result.TriangleCount > 0);
            Assert.Single(p.Report.RoundingRemoved);
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
        }

        [Fact]
        public void ACleanUnion_IsSolid()
        {
            var p = Run(Box(0, 0, 0, 1), Box(0.5, 0, 0, 1), U);
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
        }

        // three crossing bars and a cube, reduced far below what the guard would allow (PreventSelfIntersections off): the result is not clean
        static SolidBooleanPipeline NotCleanRun()
        {
            var p = new SolidBooleanPipeline(new[] {
                Grid(Vector3d.Zero, new Vector3d(2, 0.3, 0.3), 8), Grid(Vector3d.Zero, new Vector3d(0.3, 2, 0.3), 8),
                Grid(Vector3d.Zero, new Vector3d(0.3, 0.3, 2), 8), Grid(new Vector3d(1.7, 1.7, 0), new Vector3d(0.3, 0.3, 0.3), 4) }, U) {
                TargetTriangleCount = 80, PreventSelfIntersections = false, MinTriangleQuality = 0
            };
            p.Compute();
            return p;
        }

        [Fact]
        public void AResultWithAnotherDefect_IsSolidNotClean()
        {
            var p = NotCleanRun();
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.True(p.Result.TriangleCount > 0);
            Assert.False(p.Report.Clean);
            Assert.False(p.Report.OnlyContacts);
            Assert.Equal("degenerate triangles", p.Report.NotCleanReason);
            Assert.Equal(SolidBooleanResultKind.SolidNotClean, p.Report.ResultKind);
        }

        [Fact]
        public void MinThickness_RemovingTheWholeResult_IsEmptyAfterRounding()
        {
            // a union of two thin plates: MinThickness removes the solid of the exact result
            var p = Run(Grid(Vector3d.Zero, new Vector3d(1, 1, 0.01)), Grid(new Vector3d(0.5, 0, 0), new Vector3d(1, 1, 0.01)), U, x => x.MinThickness = 0.1);
            Assert.Equal(0, p.Result.TriangleCount);
            Assert.True(p.Report.BooleanTriangles > 0);
            Assert.NotEmpty(p.Report.RemovedThinSolids);
            Assert.Equal(SolidBooleanResultKind.EmptyAfterRounding, p.Report.ResultKind);
        }

        [Fact]
        public void ThinInputsTreatedAsEmpty_GiveAnEmptyResult_ThatIsNotTheExactOne()
        {
            var p = Run(Grid(Vector3d.Zero, new Vector3d(1, 1, 0.01)), Box(0, 0, 0, 1), I, x => { x.MinThickness = 0.1; x.ThinInputs = SolidBooleanPipeline.ThinInputModes.TreatAsEmpty; });
            Assert.Equal(0, p.Result.TriangleCount);
            Assert.Equal(SolidBooleanResultKind.EmptyAfterRounding, p.Report.ResultKind);
        }

        static DMesh3 Sheet(double z)
        {
            var s = new DMesh3();
            int a = s.AppendVertex(new Vector3d(-3, -3, z)), b = s.AppendVertex(new Vector3d(3, -3, z)), c = s.AppendVertex(new Vector3d(3, 3, z)), d = s.AppendVertex(new Vector3d(-3, 3, z));
            s.AppendTriangle(a, b, c);
            s.AppendTriangle(a, c, d);
            return s;
        }

        [Fact]
        public void TheSheetRoute_GivesTheKindOfItsResult()
        {
            var cut = Run(Box(0, 0, 0, 1), Sheet(0.25), D);
            Assert.Single(cut.Report.Route, s => s.Name == "SheetRole");
            Assert.True(cut.Result.TriangleCount > 0);
            Assert.Equal(SolidBooleanResultKind.Solid, cut.Report.ResultKind);
            // the whole box lies behind the sheet: the exact result is empty
            var all = Run(Box(0, 0, 0, 1), Sheet(2), D, x => x.UncutSide = ExactMeshBoolean.SheetUncutSides.Inside);
            Assert.Single(all.Report.Route, s => s.Name == "SheetRole");
            Assert.Equal(0, all.Result.TriangleCount);
            Assert.Equal(SolidBooleanResultKind.EmptyExact, all.Report.ResultKind);
        }

        [Fact]
        public void WithInputGrid_TheKindIsThatOfTheSnappedRun()
        {
            var p = Run(Box(0, 0, 0, 1), Box(0.5, 0, 0, 1), U, x => x.InputGrid = 1.0 / 1024);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            var empty = Run(Box(0, 0, 0, 1), Box(5, 0, 0, 1), I, x => x.InputGrid = 1.0 / 1024);
            Assert.Equal(SolidBooleanResultKind.EmptyExact, empty.Report.ResultKind);
        }

        [Fact]
        public void Reuse_TheKindAndTheWarningFollowTheLastCompute()
        {
            var p = new SolidBooleanPipeline(ReproA(), ReproB(), I);
            Assert.True(p.Compute());
            Assert.Equal(SolidBooleanResultKind.EmptyExact, p.Report.ResultKind);
            Assert.True(p.Compute());
            Assert.Equal(SolidBooleanResultKind.EmptyExact, p.Report.ResultKind);
            p.Inputs[1] = ReproB(1e-12);
            Assert.True(p.Compute());
            Assert.Equal(SolidBooleanResultKind.EmptyAfterRounding, p.Report.ResultKind);
            // a self-overlapping input's flag does not outlive its run, a failing run resets both
            p.Operation = U;
            p.Inputs[1] = TwoOverlapping(0.5);
            p.Inputs[0] = Box(0, 0, 0, 0.5);
            Assert.True(p.Compute());
            Assert.Equal(new[] { 1 }, p.Report.InputsWithWindingOutsideZeroOne.ToArray());
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            p.Inputs[1] = OpenBox(0.5);
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanResultKind.None, p.Report.ResultKind);
            Assert.Empty(p.Report.InputsWithWindingOutsideZeroOne);
            p.Inputs[1] = Box(0.5, 0, 0, 1);
            Assert.True(p.Compute());
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
            Assert.Empty(p.Report.InputsWithWindingOutsideZeroOne);
        }

        [Fact]
        public void Kinds_AreTheSameUnderEvery2kScale_BitForBit()
        {
            // disjoint boxes (EmptyExact), an overlap (Solid), a slab of 2^-30 at the coordinate 1/2 (Solid), one of 1e-12 (EmptyAfterRounding)
            var cases = new List<(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op, SolidBooleanResultKind kind)> {
                (Box(0, 0, 0, 0.5), Box(3, 0, 0, 0.5), I, SolidBooleanResultKind.EmptyExact),
                (Box(0, 0, 0, 0.5), Box(0.5, 0, 0, 0.5), U, SolidBooleanResultKind.Solid),
                (ReproA(), ReproB(Math.Pow(2, -30)), I, SolidBooleanResultKind.Solid),
                (ReproA(), ReproB(1e-12), I, SolidBooleanResultKind.EmptyAfterRounding),
            };
            foreach (var c in cases) {
                var p0 = Run(c.a, c.b, c.op);
                Assert.Equal(c.kind, p0.Report.ResultKind);
                foreach (Similarity s in Similarity.All()) {
                    // (a frame that rotates or shifts changes the coordinates, so the rounding bound and the ulps of the slab: only the 2^k scales are compared for it)
                    if (!s.IsExact && c.kind == SolidBooleanResultKind.EmptyAfterRounding)
                        continue;
                    var p = Run(Invariance.Transformed(c.a, s), Invariance.Transformed(c.b, s), c.op);
                    Assert.True(p.Report.ResultKind == c.kind, s.Name + ": " + p.Report.ResultKind + " instead of " + c.kind);
                    if (s.IsExact) {
                        Assert.Equal("", Invariance.Difference(Invariance.Transformed(p0.Result, s), p.Result));
                        Assert.Equal(p0.Report.RoundingRemoved.Count, p.Report.RoundingRemoved.Count);
                    }
                }
            }
        }

        // ---- InputsWithWindingOutsideZeroOne ----

        static int[] Flags(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op = U)
        {
            var p = Run(a, b, op, x => x.AllowInsideOutInputs = true);
            Assert.True(p.Status == SolidBooleanPipeline.PipelineStatus.Success, p.Status + " " + p.Error);
            return p.Report.InputsWithWindingOutsideZeroOne.ToArray();
        }

        static DMesh3 TwoOverlapping(double x) { return Together(Box(x, 0, 0, 0.5), Box(x + 0.3, 0, 0, 0.5)); }

        [Fact]
        public void APlainUnion_IsNotFlagged_AndNeitherIsAProperCavity()
        {
            Assert.Empty(Flags(Box(0, 0, 0, 1), Box(0.5, 0, 0, 1)));
            // a box with a cavity (inner shell inward): winding 1 in the wall, 0 in the cavity
            DMesh3 hollow = Together(Box(3, 0, 0, 1), Box(3, 0, 0, 0.3, true));
            Assert.Empty(Flags(Box(0, 0, 0, 1), hollow));
            Assert.Empty(Flags(hollow, Box(3.5, 0, 0, 0.4), D));
        }

        [Fact]
        public void ASelfOverlappingInput_IsFlagged_ByItsIndexAsPassed()
        {
            Assert.Equal(new[] { 1 }, Flags(Box(0, 0, 0, 0.5), TwoOverlapping(0.5)));
            Assert.Equal(new[] { 0 }, Flags(TwoOverlapping(0.5), Box(0, 0, 0, 0.5)));
            Assert.Equal(new[] { 0, 1 }, Flags(TwoOverlapping(0), TwoOverlapping(0.5)));
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Union)]
        [InlineData(ExactMeshBoolean.Operations.Intersection)]
        [InlineData(ExactMeshBoolean.Operations.Difference)]
        public void ASelfOverlappingInput_IsFlagged_InEveryOperation(ExactMeshBoolean.Operations op)
        {
            Assert.Equal(new[] { 1 }, Flags(Box(0.3, 0, 0, 0.8), TwoOverlapping(0.5), op));
        }

        [Fact]
        public void TheSameBoxTwiceInOneInput_IsFlagged()
        {
            Assert.Equal(new[] { 1 }, Flags(Box(0, 0, 0, 0.5), Together(Box(0.5, 0, 0, 0.5), Box(0.5, 0, 0, 0.5))));
        }

        [Fact]
        public void AnInsideOutInput_IsFlagged_AlsoWhenTheRunIsRefused()
        {
            DMesh3 inside = Box(0.5, 0, 0, 0.5, true);
            Assert.Equal(new[] { 1 }, Flags(Box(0, 0, 0, 0.5), inside));
            Assert.Equal(new[] { 0 }, Flags(inside, Box(0, 0, 0, 0.5), D));
            // without AllowInsideOutInputs the run is refused (InsideOutInput), and the report says which input
            var refused = Run(Box(0, 0, 0, 0.5), inside, U);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, refused.Status);
            Assert.Equal(SolidBooleanResultKind.None, refused.Report.ResultKind);
            Assert.Equal(new[] { 1 }, refused.Report.InputsWithWindingOutsideZeroOne.ToArray());
        }

        [Fact]
        public void TheKindsOfTheSilentlyWrongRuns_AreFlagged()
        {
            // the classes of the spike (docs/research/PIPELINE_CERTIFICATE_SPIKE_2026-11.md): a cavity wall given outward, an inside-out box, an inside-out
            // hollow, an inside-out tool, a self-overlapping operand with a cavity given outward, two components with one inside-out
            DMesh3 outwardCavity = Together(Box(3, 0, 0, 1), Box(3, 0, 0, 0.3));
            Assert.Equal(new[] { 1 }, Flags(Box(0, 0, 0, 1), outwardCavity));
            Assert.Equal(new[] { 1 }, Flags(Box(0, 0, 0, 1), Box(1.5, 0, 0, 1, true)));
            DMesh3 insideOutHollow = Together(Box(3, 0, 0, 1, true), Box(3, 0, 0, 0.3));
            Assert.Equal(new[] { 1 }, Flags(Box(0, 0, 0, 1), insideOutHollow));
            Assert.Equal(new[] { 1 }, Flags(Box(0, 0, 0, 1), Box(0.5, 0, 0, 0.5, true), D));
            DMesh3 overlapWithCavity = Together(Box(3, 0, 0, 1), Box(3.9, 0, 0, 0.5), Box(2.5, 0, 0, 0.3));
            Assert.Equal(new[] { 1 }, Flags(Box(0, 0, 0, 1), overlapWithCavity));
            DMesh3 twoComponents = Together(Box(3, 0, 0, 1), Box(8, 0, 0, 1, true));
            Assert.Equal(new[] { 1 }, Flags(Box(0, 0, 0, 1), twoComponents));
        }

        [Fact]
        public void TheWarning_KeepsStatusCleanAndTheKindOfTheRun()
        {
            var p = Run(Box(0, 0, 0, 0.5), TwoOverlapping(0.5), U);
            Assert.Equal(new[] { 1 }, p.Report.InputsWithWindingOutsideZeroOne.ToArray());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
            Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
        }

        [Fact]
        public void TheIndexIsThatOfTheInputAsPassed_AlsoWhenAnEarlierInputIsLeftOut()
        {
            // input 0 is thinner than MinThickness and treated as empty: the boolean gets inputs 1 and 2, the flagged one is input 2
            var p = new SolidBooleanPipeline(new List<DMesh3> { Grid(new Vector3d(9, 9, 9), new Vector3d(1, 1, 0.001), 3), Box(0, 0, 0, 0.5), TwoOverlapping(0.5) }, U) {
                MinThickness = 0.1, ThinInputs = SolidBooleanPipeline.ThinInputModes.TreatAsEmpty
            };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(0, p.Report.ThinInputs[0].Index);
            Assert.Equal(new[] { 2 }, p.Report.InputsWithWindingOutsideZeroOne.ToArray());
        }

        [Fact]
        public void ASheet_IsNeverFlagged()
        {
            var p = Run(Box(0, 0, 0, 1), Sheet(0.25), D);
            Assert.Single(p.Report.Route, s => s.Name == "SheetRole");
            Assert.Empty(p.Report.InputsWithWindingOutsideZeroOne);
        }

        [Fact]
        public void TheWarning_IsTheSameInEveryFrame_AndSerialAndParallel()
        {
            DMesh3 a = Box(0, 0, 0, 0.5), b = TwoOverlapping(0.5);
            foreach (Similarity s in Similarity.All()) {
                var p = Run(Invariance.Transformed(a, s), Invariance.Transformed(b, s), U);
                Assert.True(p.Status == SolidBooleanPipeline.PipelineStatus.Success, s.Name + ": " + p.Error);
                Assert.True(new[] { 1 }.SequenceEqual(p.Report.InputsWithWindingOutsideZeroOne), s.Name);
                Assert.Equal(SolidBooleanResultKind.Solid, p.Report.ResultKind);
                var inside = Run(Invariance.Transformed(Box(0, 0, 0, 0.5), s), Invariance.Transformed(Box(0.5, 0, 0, 0.5, true), s), U, x => x.AllowInsideOutInputs = true);
                Assert.True(new[] { 1 }.SequenceEqual(inside.Report.InputsWithWindingOutsideZeroOne), s.Name + " inside-out");
                var plain = Run(Invariance.Transformed(a, s), Invariance.Transformed(Box(0.5, 0, 0, 0.5), s), U);
                Assert.Empty(plain.Report.InputsWithWindingOutsideZeroOne);
            }
            var parallel = Run(a, b, U);
            gParallel.SerialOnThisThread = true;
            try {
                var serial = Run(a, b, U);
                Assert.Equal(parallel.Report.InputsWithWindingOutsideZeroOne, serial.Report.InputsWithWindingOutsideZeroOne);
                Assert.Equal(parallel.Report.ResultKind, serial.Report.ResultKind);
                Assert.Equal("", Invariance.Difference(parallel.Result, serial.Result));
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            for (int k = 0; k < 3; ++k)
                Assert.Equal(new[] { 1 }, Run(a, b, U).Report.InputsWithWindingOutsideZeroOne.ToArray());
        }
    }
}
