using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The "mesh" command of benchmarks/Meshing2Benchmark (MeshCommand): our mesher on one case, written in the plain text output
    /// format of MESHING2D_GOLDEN_FORMAT.md section 4. What a reference session compares with must be what our golden rows record:
    /// the written file, read back, gives the golden row's fingerprints, and the format round-trips.
    /// </summary>
    public class Meshing2MeshCommandTests
    {
        static string Dir => Path.Combine(AppContext.BaseDirectory, "Meshing2");

        static Mesh2Case Reference(string id) => Catalogue.ReferenceCases().First(c => c.Id == id);

        // M1 (cocircular and generic), M2 (a crossing, a failure with noding off, holes), M3 (refinement with and without a limit)
        public static IEnumerable<object[]> Cases => new[] {
            "m1/grid-8x8", "f-1/a", "f-2", "f-7/a", "f-23/angle-20", "f-23/angle-30", "f-34",
        }.Select(id => new object[] { id });

        [Theory]
        [MemberData(nameof(Cases))]
        public void WrittenOutput_ReadBack_HasTheGoldenRowsFingerprints(string id)
        {
            var c = Reference(id);
            var golden = MeshCommand.FindGolden(Dir, c);
            Assert.NotNull(golden);
            var result = MeshCommand.Run(c);
            Assert.True(result.Report.Ok, result.Report.ToString());
            Assert.Equal(golden.Producer, result.Producer);
            Assert.Empty(golden.Differences(result.Row));

            // the file as a reference session reads it: same fingerprints, counts and exact values, judged by the oracle on its own
            var back = TextFormat.ReadOutput(result.Text, out string producer);
            Assert.Equal(result.Producer, producer);
            var report = Oracle.Check(c.Input, back, c.Expect);
            Assert.True(report.Ok, report.ToString());
            var row = GoldenRow.From(c, producer, back, report, "");
            Assert.Empty(golden.Differences(row));
            Assert.Equal(golden.TrianglesFp, row.TrianglesFp);
            Assert.Equal(golden.SubsegmentsFp, row.SubsegmentsFp);
            Assert.Equal(golden.VerticesFp, row.VerticesFp);
            Assert.Equal(golden.IdentityFp, row.IdentityFp);

            // the summary printed to stdout names the fingerprints and the agreement
            var summary = MeshCommand.Summary(result, golden).ToList();
            Assert.Contains("triangles_fp " + golden.TrianglesFp, summary);
            Assert.Contains("subsegments_fp " + golden.SubsegmentsFp, summary);
            Assert.Contains("vertices_fp " + golden.VerticesFp, summary);
            Assert.Contains("golden: same as the recorded row", summary);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void TextFormats_RoundTrip(string id)
        {
            var c = Reference(id);
            var result = MeshCommand.Run(c);
            // output: read and written again gives the same text, bit for bit
            var back = TextFormat.ReadOutput(result.Text, out string producer);
            Assert.Equal(result.Text, TextFormat.WriteOutput(back, producer));
            Assert.Equal(result.Output.VX, back.VX);
            Assert.Equal(result.Output.VY, back.VY);
            Assert.Equal(result.Output.InputToVertex, back.InputToVertex);
            Assert.Equal(result.Output.TriangleLabels, back.TriangleLabels);

            // input: the canonical text read back is the same input (same hash), and meshing it from the text, as
            // "mesh --input" does with the milestone it infers, gives the same row as "mesh --case"
            string text = TextFormat.WriteInput(c.Input);
            var input = TextFormat.ReadInput(text);
            Assert.Equal(text, TextFormat.WriteInput(input));
            Assert.Equal(c.Milestone, MeshCommand.InferMilestone(input));
            var fromText = MeshCommand.Run(MeshCommand.FromInput(input, "from-text", null));
            Assert.Equal(c.Milestone, fromText.Case.Milestone);
            // judged as that milestone: an M1 judgement of a refined mesh would reject its constructed vertices
            Assert.Equal(c.Milestone, fromText.Case.Expect.Milestone);
            Assert.True(fromText.Report.Ok, fromText.Report.ToString());
            Assert.Equal(result.Row.InputSha256, fromText.Row.InputSha256);
            Assert.Equal(result.Row.TrianglesFp, fromText.Row.TrianglesFp);
            Assert.Equal(result.Row.SubsegmentsFp, fromText.Row.SubsegmentsFp);
            Assert.Equal(result.Row.VerticesFp, fromText.Row.VerticesFp);
            Assert.Equal(result.Text, fromText.Text);
        }

        [Fact]
        public void Summary_ReportsADifferenceAndAMissingRow()
        {
            var c = Reference("f-1/a");
            var result = MeshCommand.Run(c);
            var golden = MeshCommand.FindGolden(Dir, c);
            var changed = GoldenRow.FromJson(golden.ToJson());
            changed.TrianglesFp = new string('0', 64);
            var summary = MeshCommand.Summary(result, changed).ToList();
            Assert.Contains(summary, l => l.StartsWith("golden: DIFFERS", StringComparison.Ordinal) && l.Contains("triangles_fp"));
            Assert.Contains("golden: no recorded row for f-1/a", MeshCommand.Summary(result, null));
            // an input file has no golden row; a case of another milestone file is not found by id
            Assert.Null(MeshCommand.FindGolden(Dir, MeshCommand.FromInput(c.Input, "f-1/a", Milestone.M1)));
        }

        [Fact]
        public void InferMilestone_FollowsTheOptions()
        {
            var points = new Mesh2Input { X = new double[] { 0, 1, 0 }, Y = new double[] { 0, 0, 1 } };
            Assert.Equal(Milestone.M1, MeshCommand.InferMilestone(points));
            var segment = new Mesh2Input { X = points.X, Y = points.Y };
            segment.Segments.Add(new SegmentInput(0, 1, 0, false));
            Assert.Equal(Milestone.M2, MeshCommand.InferMilestone(segment));
            Assert.Equal(Milestone.M2, MeshCommand.InferMilestone(new Mesh2Input { X = points.X, Y = points.Y, ResolveCrossings = false }));
            Assert.Equal(Milestone.M3, MeshCommand.InferMilestone(new Mesh2Input { X = points.X, Y = points.Y, MinAngleDeg = 0 }));
            Assert.Equal(Milestone.M3, MeshCommand.InferMilestone(new Mesh2Input { X = points.X, Y = points.Y, MaxArea = 1 }));
            Assert.Equal(Milestone.M3, MeshCommand.InferMilestone(new Mesh2Input { X = points.X, Y = points.Y, SteinerLimit = 5 }));
            Assert.Equal(Milestone.M3, MeshCommand.InferMilestone(new Mesh2Input { X = points.X, Y = points.Y, Conforming = true }));
            Assert.Equal(Milestone.M3, MeshCommand.InferMilestone(new Mesh2Input { X = points.X, Y = points.Y, SizeConstant = 0.5 }));
            var region = new Mesh2Input { X = points.X, Y = points.Y };
            region.Regions.Add(new RegionInput { Label = 1, Seed = new[] { 0.2, 0.2 } });
            Assert.Equal(Milestone.M2, MeshCommand.InferMilestone(region));
            region.Regions[0].MaxArea = 0.01;
            Assert.Equal(Milestone.M3, MeshCommand.InferMilestone(region));
            // an explicit milestone wins
            Assert.Equal(Milestone.M3, MeshCommand.FromInput(points, "p", Milestone.M3).Milestone);
        }
    }
}
