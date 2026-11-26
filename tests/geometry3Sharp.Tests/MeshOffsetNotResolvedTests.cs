using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The statuses of an inset that may leave a part thinner than the grid (#1597): NotResolved, SuggestedCellSize, MaxCellRefinements.
    /// (New members: the regressions that compile against the library before the change are in MeshOffsetThinPartRegressionTests.)
    /// </summary>
    public class MeshOffsetNotResolvedTests
    {
        static DMesh3 Box() => new TrivialBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.35, 0.25)) }.Generate().MakeDMesh();
        static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        // no refinement: the part may be left between the nodes: NotResolved with a cell to try (a quarter of the used one), not Empty
        [Theory]
        [InlineData(-0.235)]
        [InlineData(-0.245)]
        public void NoRefinements_ThinPart_IsNotResolved_WithASuggestedCell(double d)
        {
            var op = new MeshOffset(Box(), d) { MaxCellRefinements = 0 };
            Assert.False(op.Compute());
            Assert.Equal(MeshOffset.Status.NotResolved, op.ResultStatus);
            Assert.Null(op.Result);
            Assert.Equal(0.25 * 0.25 * Math.Abs(d), op.SuggestedCellSize, 12);
            Assert.Equal(0.25 * Math.Abs(d), op.UsedCellSize, 12);
            // the suggested cell finds it (at -0.245: the part is 0.01 thick, the suggestion 0.0153)
            var again = new MeshOffset(Box(), d) { CellSize = op.SuggestedCellSize, MaxCellRefinements = 0 };
            Assert.True(again.Compute(), again.ResultStatus.ToString());
            Assert.Equal(0.0, again.SuggestedCellSize);
            Assert.InRange(Volume(again.Result), (1 + 2 * d) * (0.7 + 2 * d) * (0.5 + 2 * d) * 0.99, (1 + 2 * d) * (0.7 + 2 * d) * (0.5 + 2 * d) * 1.01);
        }

        // the provably empty inset stays Empty whatever MaxCellRefinements is: no refinement is run, no suggestion
        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(5)]
        public void ProvablyEmpty_IsEmpty_WithoutRefinement(int refinements)
        {
            var op = new MeshOffset(Box(), -0.5) { MaxCellRefinements = refinements };
            Assert.False(op.Compute());
            Assert.Equal(MeshOffset.Status.Empty, op.ResultStatus);
            Assert.Equal(0.0, op.SuggestedCellSize);
            Assert.Equal(0.125, op.UsedCellSize, 12);       // the first cell
        }

        // the refinements are counted: UsedCellSize is the final cell; a part 0.01 thick is found with two halvings, not with one
        [Fact]
        public void Refinements_AreCounted_AndUsedCellSizeIsTheFinalCell()
        {
            double d = -0.245, first = 0.25 * Math.Abs(d);
            var one = new MeshOffset(Box(), d) { MaxCellRefinements = 1 };
            Assert.False(one.Compute());
            Assert.Equal(MeshOffset.Status.NotResolved, one.ResultStatus);
            Assert.Equal(first / 2, one.UsedCellSize, 12);
            Assert.Equal(first / 8, one.SuggestedCellSize, 12);
            var two = new MeshOffset(Box(), d) { MaxCellRefinements = 2 };
            Assert.True(two.Compute());
            Assert.Equal(first / 4, two.UsedCellSize, 12);
            Assert.True(two.Result.IsClosed());
            Assert.Equal(0, two.NonManifoldTriangles);
        }

        // the node budget: a refinement needs 8 times the nodes within MaxGridNodes; without room the status is NotResolved at the first cell
        [Fact]
        public void NodeBudget_StopsTheRefinement()
        {
            var op = new MeshOffset(Box(), -0.24) { MaxGridNodes = 20000 };       // the first grid has 5236 nodes: 8 times it is above
            Assert.False(op.Compute());
            Assert.Equal(MeshOffset.Status.NotResolved, op.ResultStatus);
            Assert.Equal(0.06, op.UsedCellSize, 12);
            Assert.Equal(0.015, op.SuggestedCellSize, 12);
            Assert.True(op.RefinementLimited);
        }

        // the work budget (#1597 review): a refinement whose grid has more than MaxRefinementNodes nodes is not run, and RefinementLimited says that it
        // was the budget, not MaxCellRefinements, that stopped (the part of -0.245 needs two halvings: 5.8k, 46k, 370k nodes)
        [Fact]
        public void RefinementNodeBudget_StopsTheRefinement_AndIsReported()
        {
            double d = -0.245, first = 0.25 * Math.Abs(d);
            var op = new MeshOffset(Box(), d) { MaxRefinementNodes = 100000 };
            Assert.False(op.Compute());
            Assert.Equal(MeshOffset.Status.NotResolved, op.ResultStatus);
            Assert.True(op.RefinementLimited);
            Assert.Equal(first / 2, op.UsedCellSize, 12);                 // one halving ran (46k nodes), the second (370k) did not
            Assert.Equal(first / 8, op.SuggestedCellSize, 12);
            var plain = new MeshOffset(Box(), d) { MaxCellRefinements = 1 };
            Assert.False(plain.Compute());
            Assert.False(plain.RefinementLimited);                         // stopped by MaxCellRefinements
            var fine = new MeshOffset(Box(), d);                           // the default budget (4 million) runs it
            Assert.True(fine.Compute());
            Assert.False(fine.RefinementLimited);
        }

        // the near-limit case that is really empty (the inset 0.002 beyond the part, a cell of 0.0397 |d|; first grid 0.4 million nodes) costs the first
        // grid and one refinement, not 1 + 8 + 64 grids (12.9 s before the budget): a result of NotResolved, limited by the budget
        [Fact]
        public void NearLimitEmpty_CostsAtMostOneRefinement()
        {
            var op = new MeshOffset(Box(), -0.252) { RelativeCellSize = 0.0397 };
            Assert.False(op.Compute());
            Assert.Equal(MeshOffset.Status.NotResolved, op.ResultStatus);
            Assert.True(op.RefinementLimited);
            Assert.Equal(0.5 * 0.0397 * 0.252, op.UsedCellSize, 9);
        }

        // a refined success is certified like any other: the band bound holds at the final cell (the tolerance is half of it)
        [Theory]
        [InlineData(-0.235)]
        [InlineData(-0.245)]
        public void RefinedSuccess_IsBandCertified_AtTheFinalCell(double d)
        {
            var op = new MeshOffset(Box(), d);
            Assert.True(op.Compute(), op.ResultStatus.ToString());
            Assert.True(op.UsedCellSize < 0.25 * Math.Abs(d));             // it was refined
            Assert.True(op.BandCertified);
            Assert.True(op.BandDeviation <= 0.5 * op.UsedCellSize, "deviation " + op.BandDeviation + " cell " + op.UsedCellSize);
        }

        // nodes whose side is not known (no node farther than 1.5 cells from the input floods the part) count in the bound: a ball left at the middle of
        // a cell of a polyhedral sphere (the cell is half of the inset, the farthest a node can be from it) is not Empty (every node is within 1.5 cells
        // of the input here, so a bound over the nodes known to be inside would be 0)
        [Theory]
        [InlineData(0.0)]
        [InlineData(0.05)]
        [InlineData(0.1)]
        [InlineData(0.15)]
        [InlineData(0.2)]
        public void BallBetweenTheNodes_UnknownSideNodes_AreNotEmpty(double shift)
        {
            var g = new Sphere3Generator_NormalizedCube { Radius = 0.5, EdgeVertices = 8 };
            g.Generate();
            DMesh3 m = g.MakeDMesh();
            MeshTransforms.Translate(m, new Vector3d(shift, 0.7 * shift, 0.4 * shift));
            var op = new MeshOffset(m, -0.48) { RelativeCellSize = 0.5, MaxCellRefinements = 0 };
            Assert.False(op.Compute());
            Assert.Equal(MeshOffset.Status.NotResolved, op.ResultStatus);
        }

        // near the limit (the inset 0.01 beyond the part) the nodes cannot exclude a part: refined twice, still not decided; with four halvings it is
        // proven empty (the bound narrows with the cell): the cost of the case is the refinements, and Empty is never wrong
        [Fact]
        public void BeyondThePart_ByLessThanTheBound_IsNotResolved_ThenEmptyWhenRefinedFurther()
        {
            var two = new MeshOffset(Box(), -0.26);
            Assert.False(two.Compute());
            Assert.Equal(MeshOffset.Status.NotResolved, two.ResultStatus);
            var four = new MeshOffset(Box(), -0.26) { MaxCellRefinements = 4 };
            Assert.False(four.Compute());
            Assert.Equal(MeshOffset.Status.Empty, four.ResultStatus);
        }

        // a cancel request during the refinement is honoured
        [Fact]
        public void Cancel_DuringTheRefinement_IsHonoured()
        {
            int calls = 0;
            var probe = new MeshOffset(Box(), -0.245) { MaxCellRefinements = 0, CancelF = () => { calls++; return false; } };
            probe.Compute();
            int firstRound = calls;
            int n = 0;
            var op = new MeshOffset(Box(), -0.245) { CancelF = () => ++n > firstRound };
            Assert.False(op.Compute());
            Assert.Equal(MeshOffset.Status.Cancelled, op.ResultStatus);
        }

        // reuse: the suggestion and the reports are those of the last call
        [Fact]
        public void Reuse_SuggestedCellSizeFollowsTheLastCall()
        {
            var op = new MeshOffset(Box(), -0.245) { MaxCellRefinements = 0 };
            Assert.False(op.Compute());
            double suggestion = op.SuggestedCellSize;
            Assert.True(suggestion > 0);
            op.MaxCellRefinements = 2;
            Assert.True(op.Compute());
            Assert.Equal(0.0, op.SuggestedCellSize);
            op.MaxCellRefinements = 0;
            Assert.False(op.Compute());
            Assert.Equal(suggestion, op.SuggestedCellSize);
        }

        // invariance over Similarity.All(): the decision (NotResolved, the suggestion) of the 2^k frames is the unit frame's, scaled
        [Fact]
        public void Invariance_OfTheDecision()
        {
            var unit = new MeshOffset(Box(), -0.245) { MaxCellRefinements = 0 };
            Assert.False(unit.Compute());
            foreach (Similarity s in Similarity.All()) {
                if (!s.IsExact) continue;
                var op = new MeshOffset(Invariance.Transformed(Box(), s), -0.245 * s.Scale) { MaxCellRefinements = 0 };
                Assert.False(op.Compute());
                Assert.Equal(MeshOffset.Status.NotResolved, op.ResultStatus);
                Assert.Equal(unit.SuggestedCellSize * s.Scale, op.SuggestedCellSize);
                Assert.Equal(unit.UsedCellSize * s.Scale, op.UsedCellSize);
            }
        }
    }
}
