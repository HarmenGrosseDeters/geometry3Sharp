using System.Collections.Generic;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The gates of <c>m2.snap</c> (explicit tolerance, O11, D14), sharpened so that they test what snapping is for: a merged feature
    /// must be gone, not only allowed to go. Until <c>m2.snap</c> is built the mesher has only the automatic tolerance, so these rows
    /// must reject today's output; when it is built, this test flips and the rows become covered (docs/design/MESHING2D_SNAP.md,
    /// section 3c). A bound must also accept a merged result, checked by the full oracle.
    /// </summary>
    public class Meshing2SnapGateTests
    {
        static CatalogueRow Row(string id) => Catalogue.Rows().Single(r => r.Case != null && r.Case.Id == id);

        [Theory]
        [InlineData("f-16")]   // the near-collinear pair does not share S-B: refinement runs to the Steiner limit
        [InlineData("f-17")]   // the hole far below the tolerance stays: 8 vertices
        [InlineData("f-17/b")] // the contour around the hole marker stays
        [InlineData("f-22/b")] // the sliver within 2^-29 stays: the Steiner limit, as without tolerance
        public void PendingSnapRow_RejectsTodaysOutput(string id)
        {
            var row = Row(id);
            Assert.Equal(CaseState.Pending, row.State);
            var output = CandidateRegistry.For(row.Case.Milestone).Run(row.Case.Input);
            // the vertex bound alone, as the oracle's CountOutOfRange judges it (merged input slots do not count); the full oracle takes
            // minutes on F-16's 100 009 vertices (the slow rows get a status check only, Meshing2CatalogueTests)
            int aliases = Enumerable.Range(0, row.Case.Input.Count).Count(i => output.InputToVertex[i] != i);
            int vertices = output.VX.Length - aliases;
            Assert.True(vertices > row.Case.Expect.MaxVertices.Value,
                id + ": " + vertices + " vertices, within the bound " + row.Case.Expect.MaxVertices.Value + ", so the gate no longer separates");
        }

        /// <summary>
        /// the merged result of F-17 and F-17/b, built by hand: the outer square, the collapsed contour as one point at (5, 5), its pixel
        /// centre on the grid of the tolerance; the other contour points stay in the list as aliases of it (Q8)
        /// </summary>
        static Mesh2Output CollapsedHole(Mesh2Input input)
        {
            var map = new int[input.Count];
            for (int i = 0; i < input.Count; ++i) map[i] = i < 4 ? i : 4;
            Assert.Equal(5.0, input.X[4]); Assert.Equal(5.0, input.Y[4]);
            return new Mesh2Output {
                Status = Mesh2Status.Success, VX = (double[])input.X.Clone(), VY = (double[])input.Y.Clone(), InputToVertex = map,
                Triangles = new[] { new[] { 0, 1, 4 }, new[] { 1, 2, 4 }, new[] { 2, 3, 4 }, new[] { 3, 0, 4 } },
                Neighbours = SelfCheck.NeighboursOf(new[] { new[] { 0, 1, 4 }, new[] { 1, 2, 4 }, new[] { 2, 3, 4 }, new[] { 3, 0, 4 } }),
                Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1), new SubsegmentOutput(1, 2), new SubsegmentOutput(2, 3), new SubsegmentOutput(3, 0) },
            };
        }

        [Theory]
        [InlineData("f-17")]
        [InlineData("f-17/b")]
        public void SnapGate_AcceptsTheCollapsedHole(string id)
        {
            var row = Row(id);
            var output = CollapsedHole(row.Case.Input);
            Assert.Equal(row.Case.Expect.MaxVertices.Value, 5);   // the bound is tight: one vertex more is rejected
            var report = Oracle.Check(row.Case.Input, output, row.Case.Expect);
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void SnapGate_AcceptsTheMergedSliver()
        {
            // F-22/b merged: the returning segment's end A' rounded onto the grid of 2^-29 lies exactly on the first segment, so the two
            // segments share their pieces; meshed from that input and judged against the original row (A' moved by 2^-31, within the tolerance)
            var row = Row("f-22/b");
            var input = row.Case.Input;
            var merged = new Mesh2Input { X = (double[])input.X.Clone(), Y = (double[])input.Y.Clone(), MinAngleDeg = input.MinAngleDeg, SteinerLimit = input.SteinerLimit };
            merged.Rings.AddRange(input.Rings); merged.Segments.AddRange(input.Segments);
            int end = input.Segments[input.Segments.Count - 1].B;
            Assert.Equal(7 + 1.0 / 2048, input.X[end]);
            merged.Y[end] = 51 + 1.0 / 2048;
            var output = CandidateRegistry.For(row.Case.Milestone).Run(merged);
            var report = Oracle.Check(input, output, row.Case.Expect);
            Assert.True(report.Ok, report.ToString());
        }
    }
}
