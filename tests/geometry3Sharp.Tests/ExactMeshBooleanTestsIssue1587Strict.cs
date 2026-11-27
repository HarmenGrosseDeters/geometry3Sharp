using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;
using T = geometry3Sharp.Tests.ExactMeshBooleanTestsIssue1587;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1587, the strict alternative of the rim's branch (ExactMeshBoolean.UnresolvedByFastWinding false): a piece that
    /// neither the linear segment test nor the wide bound decides makes Compute() fail with Status OpenInput and a count,
    /// never an empty or partial result that reads it as outside. A class of its own, because the switch is new: the class
    /// of the default behaviour compiles against the library before #1587 (new tests on base).
    /// </summary>
    public class ExactMeshBooleanTestsIssue1587Strict
    {
        static ExactMeshBoolean Strict(DMesh3 a, DMesh3 b, Ops op)
        {
            ExactMeshBoolean r = T.New(a, b, op);
            r.UnresolvedByFastWinding = false;
            return r;
        }

        static void AssertFails(ExactMeshBoolean r)
        {
            Assert.False(r.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, r.Status);
            Assert.Null(r.Result);
            long unresolved = r.Counts["antipodal_unresolved"];
            Assert.True(unresolved > 0);
            Assert.Equal(0L, r.Counts["antipodal_fast_winding"]);
            Assert.True(unresolved <= r.Counts["antipodal_undecided"]);
            Assert.Contains("undecided at " + unresolved + " evaluations", r.Error);
            Assert.Contains("antipodal_unresolved", r.Error);
        }

        [Theory]
        [InlineData(Ops.Intersection)]
        [InlineData(Ops.Union)]
        public void AtTheRoundingErrorOfTheOpenEdge_FailsWithOpenInput(Ops op) => AssertFails(Strict(T.OpenUnitBox(), T.SmallBox(1e-14), op));

        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        public void SliverOnTheRim_FailsWithOpenInput(int edgeVertices) => AssertFails(Strict(T.OpenUnitBox(), T.RimBox(edgeVertices), Ops.Intersection));

        [Fact]
        public void TieNextToTheEdgeOfAFlatSheet_FailsWithOpenInput() => AssertFails(Strict(T.Sheet(), T.SheetBox(), Ops.Union));

        // the wide bound still decides where it can: the strict switch changes nothing there
        [Fact]
        public void WideBound_DecidesTheSameInStrictMode()
        {
            ExactMeshBoolean strict = Strict(T.OpenUnitBox(), T.SmallBox(1e-11), Ops.Intersection);
            Assert.True(strict.Compute(), strict.Error);
            ExactMeshBoolean plain = T.Run(T.OpenUnitBox(), T.SmallBox(1e-11));
            Assert.Equal("", Invariance.Difference(plain.Result, strict.Result));
            Assert.True(strict.Counts["antipodal_wide"] > 0);
        }
    }
}
