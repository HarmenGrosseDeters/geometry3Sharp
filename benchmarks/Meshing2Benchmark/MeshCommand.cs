using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Meshing2Acceptance
{
    /// <summary>
    /// The "mesh" command: our mesher on one case, written in the plain text output format of
    /// docs/design/MESHING2D_GOLDEN_FORMAT.md, section 4, so that a reference session can compare its output file with ours. It
    /// uses the candidate and the oracle expectations of the golden rows (CandidateRegistry.For(milestone), the case's Expect), so
    /// the fingerprints it prints are those of our golden row of the case.
    /// </summary>
    public static class MeshCommand
    {
        public sealed class Result
        {
            public Mesh2Case Case;
            public string Producer;
            public Mesh2Output Output;
            public OracleReport Report;
            /// <summary> the row of section 5 for this output (reason empty) </summary>
            public GoldenRow Row;
            /// <summary> the output in the text format of section 4 </summary>
            public string Text;
        }

        /// <summary>
        /// The milestone a text input asks for when none is given: M3 with a quality option (minimum angle, area, size, Steiner limit,
        /// conforming, a region with an area), M2 with constraints (segments, rings, hole seeds, regions) or noding switched off,
        /// otherwise M1.
        /// </summary>
        public static Milestone InferMilestone(Mesh2Input input)
        {
            if (input.MinAngleDeg != null || input.MaxArea != null || input.SizeConstant != null || input.SizeFunction != null
                || input.SteinerLimit != null || input.Conforming || input.Regions.Any(r => r.MaxArea != null))
                return Milestone.M3;
            if (input.Segments.Count > 0 || input.Rings.Count > 0 || input.HoleSeeds.Count > 0 || input.Regions.Count > 0 || !input.ResolveCrossings)
                return Milestone.M2;
            return Milestone.M1;
        }

        /// <summary> A case for an input file in the text format of section 2.6, judged as its milestone (default: inferred). </summary>
        public static Mesh2Case FromInput(Mesh2Input input, string id, Milestone? milestone)
        {
            var m = milestone ?? InferMilestone(input);
            return new Mesh2Case { Id = id, Milestone = m, Input = input, Generator = "text:" + id, Expect = new OracleExpect { Milestone = m } };
        }

        /// <summary> Runs the candidate of the case's milestone, judges the result with the oracle and makes its row and text. </summary>
        public static Result Run(Mesh2Case c)
        {
            var candidate = CandidateRegistry.For(c.Milestone) ?? throw new InvalidOperationException("no candidate for " + c.Milestone);
            var output = candidate.Run(c.Input);
            var report = Oracle.Check(c.Input, output, c.Expect);
            return new Result {
                Case = c, Producer = candidate.Name, Output = output, Report = report,
                Row = GoldenRow.From(c, candidate.Name, output, report, ""),
                Text = TextFormat.WriteOutput(output, candidate.Name),
            };
        }

        /// <summary> The recorded golden row of a case in the golden file of its milestone, or null. </summary>
        public static GoldenRow FindGolden(string goldenDir, Mesh2Case c)
        {
            string path = Path.Combine(goldenDir, c.Milestone.ToString().ToLowerInvariant() + ".json");
            if (!File.Exists(path)) return null;
            return GoldenFile.Parse(File.ReadAllText(path)).Rows.FirstOrDefault(r => r.Case == c.Id);
        }

        /// <summary> The lines printed to stdout: the row's fingerprints and counts, and how they compare with the golden row. </summary>
        public static IEnumerable<string> Summary(Result r, GoldenRow golden)
        {
            yield return "case " + r.Case.Id + " " + r.Case.Milestone + " producer " + r.Producer + " input_sha256 " + r.Row.InputSha256;
            yield return "status " + r.Row.Status + ", " + r.Row.Triangles + " triangles, " + r.Row.Vertices + " vertices, " + r.Row.Subsegments + " subsegments, "
                + r.Row.ConstructedVertices + " constructed, unique " + r.Row.Unique.ToString().ToLowerInvariant();
            yield return "triangles_fp " + r.Row.TrianglesFp;
            yield return "subsegments_fp " + r.Row.SubsegmentsFp;
            yield return "vertices_fp " + r.Row.VerticesFp;
            if (r.Row.IdentityFp != null) yield return "identity_fp " + r.Row.IdentityFp;
            yield return "area2 " + r.Row.Area2;
            yield return "min_angle_sin2 " + (r.Row.MinAngleSin2 ?? "-");
            yield return "oracle " + (r.Report.Ok ? "VALID" : "INVALID: " + r.Report);
            if (golden == null) {
                yield return "golden: no recorded row for " + r.Case.Id;
            } else {
                var differences = golden.Differences(r.Row).ToList();
                yield return differences.Count == 0 ? "golden: same as the recorded row" : "golden: DIFFERS: " + string.Join("; ", differences);
            }
            if (r.Case.Input.SnapTolerance != 0)
                yield return "note: the input has an explicit snap tolerance, which the mesher does not take yet (m2.snap): the output is without snapping";
        }
    }
}
