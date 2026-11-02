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
            // with the known cases, the same input is that case; asked as another milestone, it is not
            Assert.Equal("f-1/a", MeshCommand.FromInput(c.Input, "x", null, AllCases.Where(k => k.Id == "f-1/a")).Id);
            var asM3 = MeshCommand.FromInput(c.Input, "x", Milestone.M3, AllCases.Where(k => k.Id == "f-1/a"));
            Assert.Equal("x", asM3.Id);
            Assert.Equal(Milestone.M3, asM3.Expect.Milestone);
        }

        // ---- the command as the command line runs it (MeshCommand.Execute) ----

        static List<Mesh2Case> AllCases => M1Cases.Ci().Concat(Catalogue.Rows().Where(r => r.Case != null).Select(r => r.Case)).GroupBy(c => c.Id).Select(g => g.First()).ToList();

        static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mesh2cmd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static (int exit, string stdout, string stderr) Execute(params string[] args)
        {
            var o = new StringWriter(); var e = new StringWriter();
            int exit = MeshCommand.Execute(args, o, e, AllCases, Dir);
            return (exit, o.ToString(), e.ToString());
        }

        [Fact]
        public void Execute_Case_WritesTheFileAndPrintsTheGoldenFingerprints()
        {
            string dir = TempDir(), path = Path.Combine(dir, "f1a.txt");
            var (exit, stdout, stderr) = Execute("--case", "f-1/a", "--output", path);
            Assert.True(exit == 0, stdout + stderr);
            var golden = MeshCommand.FindGolden(Dir, Reference("f-1/a"));
            Assert.Contains("triangles_fp " + golden.TrianglesFp, stdout);
            Assert.Contains("golden: same as the recorded row", stdout);
            var back = TextFormat.ReadOutput(File.ReadAllText(path), out string producer);
            Assert.Equal(golden.TrianglesFp, Fingerprint.Triangles(back));
        }

        [Fact]
        public void Execute_InputOfACase_IsJudgedWithTheCasesExpectations()
        {
            // F-9 requires a failure (crossings with noding off): from its text the same verdict as from the case, and its golden row
            string dir = TempDir(), input = Path.Combine(dir, "anything.txt"), path = Path.Combine(dir, "out.txt");
            File.WriteAllText(input, TextFormat.WriteInput(Catalogue.Rows().First(r => r.Case != null && r.Case.Id == "f-9").Case.Input));
            var (exit, stdout, stderr) = Execute("--input", input, "--output", path);
            Assert.True(exit == 0, stdout + stderr);
            Assert.Contains("the input of case f-9", stdout);
            Assert.Contains("oracle VALID", stdout);
            Assert.Contains("golden: same as the recorded row", stdout);

            // changed by one point, it is no case: default expectations, no golden row
            var other = TextFormat.ReadInput(File.ReadAllText(input));
            other.X[0] += 0.5;
            File.WriteAllText(input, TextFormat.WriteInput(other));
            (exit, stdout, stderr) = Execute("--input", input, "--output", path);
            Assert.DoesNotContain("the input of case", stdout);
            Assert.Contains("golden: no recorded row for anything", stdout);
            // also when the file is named like a case: an input that is not the case is not compared with the case's row
            string named = Path.Combine(dir, "f-2.txt");
            File.WriteAllText(named, TextFormat.WriteInput(other));
            (exit, stdout, stderr) = Execute("--input", named, "--output", path);
            Assert.Contains("golden: no recorded row for f-2", stdout);

            // Triangle files have no failure status: for F-9 none are written, exit 1
            string ele = Path.Combine(dir, "f9.ele");
            (exit, stdout, stderr) = Execute("--case", "f-9", "--output", ele);
            Assert.Equal(1, exit);
            Assert.Contains("none written", stderr);
            Assert.False(File.Exists(ele));
            Assert.False(File.Exists(Path.Combine(dir, "f9.node")));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Execute_TriangleFiles_ReadBackGiveTheGoldenFingerprints(string id)
        {
            var c = Reference(id);
            var golden = MeshCommand.FindGolden(Dir, c);
            string dir = TempDir(), path = Path.Combine(dir, "out.ele");
            var (exit, stdout, stderr) = Execute("--case", id, "--output", path);
            if (golden.Status == "Failed") {
                // Triangle files have no failure status: none written
                Assert.Equal(1, exit);
                Assert.False(File.Exists(path));
                return;
            }
            Assert.True(exit == 0, stdout + stderr);
            var back = MeshCommand.ReadTriangleFiles(Path.Combine(dir, "out"));
            Assert.Equal(golden.TrianglesFp, Fingerprint.Triangles(back));
            Assert.Equal(golden.VerticesFp, Fingerprint.Vertices(back));
            if (c.Milestone != Milestone.M1)
                Assert.Equal(golden.SubsegmentsFp, Fingerprint.Subsegments(back));
            // judged as a foreign producer (numbering by coordinates), the files pass the oracle
            var report = Oracle.Check(c.Input, back, c.Expect);
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(golden.Area2, report.Area2.ToString());
        }

        [Fact]
        public void Execute_TriangleInput_PolyAndNode()
        {
            string dir = TempDir();
            // a .node of the points of m1/grid-8x8 is that case
            var grid = Reference("m1/grid-8x8");
            var points = new g3.ConstrainedMesh2Input();
            for (int i = 0; i < grid.Input.Count; ++i) points.Points.Add(new g3.Vector2d(grid.Input.X[i], grid.Input.Y[i]));
            string node = Path.Combine(dir, "grid.node");
            File.WriteAllText(node, g3cli.TrianglePolyFormat.WriteNode(points.Points, null, null));
            var (exit, stdout, stderr) = Execute("--input", node, "--output", Path.Combine(dir, "grid.txt"));
            Assert.True(exit == 0, stdout + stderr);
            Assert.Contains("the input of case m1/grid-8x8", stdout);
            Assert.Contains("golden: same as the recorded row", stdout);

            // an L-shaped .poly with quality options from the command line: M3, valid, the concavity not meshed
            string poly = Path.Combine(dir, "l.poly");
            File.WriteAllText(poly, "6 2 0 0\n1 0 0\n2 2 0\n3 2 1\n4 1 1\n5 1 2\n6 0 2\n6 0\n1 1 2\n2 2 3\n3 3 4\n4 4 5\n5 5 6\n6 6 1\n0\n");
            (exit, stdout, stderr) = Execute("--input", poly, "--output", Path.Combine(dir, "l.txt"), "--min-angle", "30", "--max-area", "0.05");
            Assert.True(exit == 0, stdout + stderr);
            Assert.Contains("case l M3", stdout);
            Assert.Contains("area2 6", stdout);         // twice the area 3 of the L (its hull: 3.5)
            Assert.Contains("oracle VALID", stdout);
            double sin2 = double.Parse(stdout.Split('\n').First(l => l.StartsWith("min_angle_sin2 ", StringComparison.Ordinal)).Substring(15), System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(sin2 >= 0.25 - 1e-12, "the minimum angle 30 was applied: sin^2 " + sin2);     // sin^2(30) = 1/4

            // on a thin rectangle the bound matters: without it the two triangles are slivers, with it none is below 30 degrees
            string thin = Path.Combine(dir, "thin.poly");
            File.WriteAllText(thin, "4 2 0 0\n1 0 0\n2 10 0\n3 10 1\n4 0 1\n4 0\n1 1 2\n2 2 3\n3 3 4\n4 4 1\n0\n");
            double MinSin2(string output) => double.Parse(output.Split('\n').First(l => l.StartsWith("min_angle_sin2 ", StringComparison.Ordinal)).Substring(15), System.Globalization.CultureInfo.InvariantCulture);
            (exit, stdout, stderr) = Execute("--input", thin, "--output", Path.Combine(dir, "thin0.txt"), "--max-area", "100");
            Assert.True(exit == 0, stdout + stderr);
            Assert.True(MinSin2(stdout) < 0.25, stdout);
            (exit, stdout, stderr) = Execute("--input", thin, "--output", Path.Combine(dir, "thin30.txt"), "--max-area", "100", "--min-angle", "30");
            Assert.True(exit == 0, stdout + stderr);
            Assert.True(MinSin2(stdout) >= 0.25 - 1e-12, stdout);

            // with a region, the labels are the triangle attribute of the .ele file
            string labelled = Path.Combine(dir, "lr.poly");
            File.WriteAllText(labelled, File.ReadAllText(poly) + "1\n1 0.5 0.5 7 -1\n");
            (exit, stdout, stderr) = Execute("--input", labelled, "--output", Path.Combine(dir, "lr.ele"));
            Assert.True(exit == 0, stdout + stderr);
            var back = MeshCommand.ReadTriangleFiles(Path.Combine(dir, "lr"));
            Assert.NotNull(back.TriangleLabels);
            Assert.All(back.TriangleLabels, l => Assert.Equal(7, l));

            // a snap tolerance from the command line: the vertices on its grid; too small for the coordinates: usage error
            string offGrid = Path.Combine(dir, "offgrid.poly");
            File.WriteAllText(offGrid, "4 2 0 0\n1 0.1 0.2\n2 10.3 -0.2\n3 9.8 10.1\n4 0.2 9.9\n4 0\n1 1 2\n2 2 3\n3 3 4\n4 4 1\n0\n");
            (exit, stdout, stderr) = Execute("--input", offGrid, "--output", Path.Combine(dir, "snapped.txt"), "--snap-tolerance", "1");
            Assert.True(exit == 0, stdout + stderr);
            Assert.Contains("area2 200", stdout);
            var snapped = TextFormat.ReadOutput(File.ReadAllText(Path.Combine(dir, "snapped.txt")), out _);
            Assert.All(snapped.VX, x => Assert.Equal(Math.Round(x), x));
            (exit, _, stderr) = Execute("--input", offGrid, "--output", Path.Combine(dir, "snapped.txt"), "--snap-tolerance", "1e-30");
            Assert.Equal(2, exit);
            Assert.Contains("SnapTolerance", stderr);
            (exit, _, _) = Execute("--case", "f-1/a", "--output", Path.Combine(dir, "x.txt"), "--snap-tolerance", "1");
            Assert.Equal(2, exit);

            // quality options belong to Triangle input; the text format states its own
            string text = Path.Combine(dir, "f1a.txt");
            File.WriteAllText(text, TextFormat.WriteInput(Reference("f-1/a").Input));
            (exit, _, stderr) = Execute("--input", text, "--output", Path.Combine(dir, "x.txt"), "--min-angle", "20");
            Assert.Equal(2, exit);
            Assert.Contains("Triangle input", stderr);
        }

        [Fact]
        public void Execute_WithoutGoldenRows_StillMeshesAnInput()
        {
            // outside the repository there is no golden folder: an input is meshed and judged, nothing is compared
            string dir = TempDir(), input = Path.Combine(dir, "in.txt");
            File.WriteAllText(input, TextFormat.WriteInput(Reference("f-1/a").Input));
            var o = new StringWriter(); var e = new StringWriter();
            int exit = MeshCommand.Execute(new[] { "--input", input, "--output", Path.Combine(dir, "o.txt") }, o, e, AllCases, null);
            Assert.True(exit == 0, o + e.ToString());
            Assert.Contains("golden: no recorded row for f-1/a", o.ToString());
            Assert.Null(MeshCommand.FindGolden(null, Reference("f-1/a")));
        }

        [Fact]
        public void Execute_WrongUsage_IsExitCode2WithAMessage()
        {
            string dir = TempDir(), output = Path.Combine(dir, "o.txt"), input = Path.Combine(dir, "in.txt");
            File.WriteAllText(input, TextFormat.WriteInput(Reference("f-1/a").Input));
            foreach (var args in new[] {
                new[] { "--output", output },
                new[] { "--case", "f-1/a" },
                new[] { "--case", "f-1/a", "--input", input, "--output", output },
                new[] { "--case", "no/such-case", "--output", output },
                new[] { "--case", "f-1/a", "--milestone", "M3", "--output", output },
                new[] { "--input", input, "--milestone", "7", "--output", output },
                new[] { "--input", input, "--milestone", "M4", "--output", output },
                new[] { "--input", Path.Combine(dir, "missing.txt"), "--output", output },
                new[] { "--case", "f-1/a", "--output", Path.Combine(dir, "no", "dir", "o.txt") },
                new[] { "--case", "f-1/a", "--output" },
            }) {
                var (exit, stdout, stderr) = Execute(args);
                Assert.True(exit == 2, string.Join(" ", args) + " -> " + exit + " " + stderr);
                Assert.False(string.IsNullOrWhiteSpace(stderr), string.Join(" ", args));
            }
            Assert.False(File.Exists(output));
            // a bad input file is a usage error too
            File.WriteAllText(input, "mesh2-input 1\npoints 2\n");
            Assert.Equal(2, Execute("--input", input, "--output", output).exit);
            // the milestone in any case
            File.WriteAllText(input, TextFormat.WriteInput(Reference("f-1/a").Input));
            Assert.Equal(0, Execute("--input", input, "--milestone", "m2", "--output", output).exit);
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
            var ring = new Mesh2Input { X = points.X, Y = points.Y };
            ring.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
            Assert.Equal(Milestone.M2, MeshCommand.InferMilestone(ring));
            var hole = new Mesh2Input { X = points.X, Y = points.Y };
            hole.HoleSeeds.Add(new[] { 5.0, 5.0 });
            Assert.Equal(Milestone.M2, MeshCommand.InferMilestone(hole));
            Assert.Equal(Milestone.M2, MeshCommand.InferMilestone(new Mesh2Input { X = points.X, Y = points.Y, SnapTolerance = 0.5 }));
            Assert.Equal(Milestone.M3, MeshCommand.InferMilestone(new Mesh2Input { X = points.X, Y = points.Y, SizeFunction = (x, y) => Q.FromDouble(1) }));
            // an explicit milestone wins
            Assert.Equal(Milestone.M3, MeshCommand.FromInput(points, "p", Milestone.M3).Milestone);
        }
    }
}
