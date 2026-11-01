using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The gates of <c>m2.snap</c> (explicit tolerance, O11), sharpened so that they test what snapping is for: a merged feature must be
    /// gone, not only allowed to go. Until <c>m2.snap</c> is built the mesher has only the automatic tolerance, so these rows must reject
    /// today's output; when it is built, this test flips and the rows become covered (docs/design/MESHING2D_SNAP.md, section 3c).
    /// </summary>
    public class Meshing2SnapGateTests
    {
        [Theory]
        [InlineData("f-16")]   // the near-collinear pair does not share S-B: refinement runs to the Steiner limit
        [InlineData("f-17")]   // the hole far below the tolerance stays: 8 vertices
        [InlineData("f-22/b")] // the sliver within 2^-29 stays: the Steiner limit, as without tolerance
        public void PendingSnapRow_RejectsTodaysOutput(string id)
        {
            var row = Catalogue.Rows().Single(r => r.Case != null && r.Case.Id == id);
            Assert.Equal(CaseState.Pending, row.State);
            var output = CandidateRegistry.For(row.Case.Milestone).Run(row.Case.Input);
            var report = Oracle.Check(row.Case.Input, output, row.Case.Expect);
            Assert.False(report.Ok, id + " passed, so m2.snap's gate no longer separates: " + report);
            Assert.True(report.Has(Violation.CountOutOfRange), id + ": " + report);
        }

        [Theory]
        [InlineData("f-17", 4)]
        [InlineData("f-22/b", 216)]
        public void SnapGate_AcceptsTheMergedResult(string id, int expectedVertices)
        {
            // the merged result, approximated by dropping the feature that merges (the hole ring of F-17, the returning segment of
            // F-22/b): the sharpened bound must accept it, so that the gate does not reject what m2.snap is meant to produce
            var row = Catalogue.Rows().Single(r => r.Case != null && r.Case.Id == id);
            var input = row.Case.Input;
            var merged = new Mesh2Input { MinAngleDeg = input.MinAngleDeg, SteinerLimit = input.SteinerLimit };
            if (id == "f-17") {
                var outer = input.Rings[0];
                merged.X = outer.Vertices.Select(i => input.X[i]).ToArray(); merged.Y = outer.Vertices.Select(i => input.Y[i]).ToArray();
                merged.Rings.Add(new RingInput(Enumerable.Range(0, outer.Vertices.Length).ToArray(), false));
            } else {
                merged.X = input.X; merged.Y = input.Y;
                merged.Rings.AddRange(input.Rings);
                merged.Segments.AddRange(input.Segments.Take(input.Segments.Count - 1));
            }
            var output = CandidateRegistry.For(row.Case.Milestone).Run(merged);
            Assert.InRange(output.VX.Length, 1, row.Case.Expect.MaxVertices.Value);
            Assert.InRange(output.VX.Length, expectedVertices / 2, expectedVertices * 2);
        }
    }
}
