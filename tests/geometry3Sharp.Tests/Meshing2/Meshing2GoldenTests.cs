using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The golden master of the 2D mesher: stored rows against recomputed ones, on net10 (Linux, Windows) and net48 (Windows). The
    /// process rules themselves are tested here too, because a golden master that can be updated silently protects nothing.
    /// </summary>
    public class Meshing2GoldenTests
    {
        static string Dir => Path.Combine(AppContext.BaseDirectory, "Meshing2");
        static GoldenFile M1File() => GoldenFile.Parse(File.ReadAllText(Path.Combine(Dir, "m1.json")));
        static IMeshing2Candidate Candidate => CandidateRegistry.For(Milestone.M1);

        [Fact]
        public void M1_RecordedRows_AreReproduced()
        {
            var file = M1File();
            Assert.True(file.Rows.Count >= 20, "the CI subset has " + file.Rows.Count + " rows");
            var problems = GoldenProcess.Check(file, M1Cases.Ci(), Candidate);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void M1_GoldenFile_IsFormattedAsTheToolWritesIt()
        {
            string text = File.ReadAllText(Path.Combine(Dir, "m1.json")).Replace("\r\n", "\n");
            Assert.Equal(text, GoldenFile.Parse(text).Write());
        }

        [Fact]
        public void M1_EveryRow_HasAReason_AndTiedRowsAreMarked()
        {
            var cases = M1Cases.Ci().ToDictionary(c => c.Id);
            foreach (var row in M1File().Rows) {
                Assert.False(string.IsNullOrWhiteSpace(row.Reason), row.Case);
                // an input built with ties must be recorded as not unique, except where an interior point breaks them
                if (cases[row.Case].Ties) Assert.False(row.Unique, row.Case);
            }
        }

        // ---- the process

        sealed class Fixed : IMeshing2Candidate
        {
            public Func<Mesh2Input, Mesh2Output> F;
            public string Name => "fixed";
            public Mesh2Output Run(Mesh2Input input) => F(input);
        }

        static Mesh2Case TriangleCase(string id = "t") => new Mesh2Case {
            Id = id, Generator = "literal", Milestone = Milestone.M1, Input = Generators.Literal(0, 0, 8, 0, 0, 8),
            Expect = new OracleExpect { Milestone = Milestone.M1 }
        };

        static Mesh2Output Good(Mesh2Input input) => new Mesh2Output {
            Status = Mesh2Status.Success, VX = input.X, VY = input.Y, InputToVertex = Enumerable.Range(0, input.Count).ToArray(), Triangles = new[] { new[] { 0, 1, 2 } }
        };

        static GoldenFile Recorded(string reason = "first")
        {
            var file = new GoldenFile();
            var problems = GoldenProcess.Update(file, new[] { TriangleCase() }, new Fixed { F = Good }, null, new Dictionary<string, string> { { "t", reason } });
            Assert.Empty(problems);
            return file;
        }

        [Fact]
        public void Update_NewRowWithoutReason_IsRefused()
        {
            var file = new GoldenFile();
            var problems = GoldenProcess.Update(file, new[] { TriangleCase() }, new Fixed { F = Good }, null, new Dictionary<string, string>());
            Assert.Contains(problems, p => p.Contains("without a reason"));
            Assert.Empty(file.Rows);
        }

        [Fact]
        public void Update_InvalidResult_IsRefusedEvenWithAReason()
        {
            var file = new GoldenFile();
            Func<Mesh2Input, Mesh2Output> flipped = i => { var o = Good(i); o.Triangles = new[] { new[] { 0, 2, 1 } }; return o; };
            var problems = GoldenProcess.Update(file, new[] { TriangleCase() }, new Fixed { F = flipped }, null, new Dictionary<string, string> { { "t", "looks fine" } });
            Assert.Contains(problems, p => p.Contains("fails the oracle"));
            Assert.Empty(file.Rows);
        }

        [Fact]
        public void Update_DifferentialFailure_IsRefusedEvenWithAReason()
        {
            var file = new GoldenFile();
            var problems = GoldenProcess.Update(file, new[] { TriangleCase() }, new Fixed { F = Good }, (c, o, r) => "differs", new Dictionary<string, string> { { "t", "fine" } });
            Assert.Contains(problems, p => p.Contains("fails the differential"));
            Assert.Empty(file.Rows);
        }

        [Fact]
        public void Update_Unchanged_NeedsNoReason_AndKeepsTheOldOne()
        {
            var file = Recorded();
            Assert.Empty(GoldenProcess.Update(file, new[] { TriangleCase() }, new Fixed { F = Good }, null, new Dictionary<string, string>()));
            Assert.Equal("first", file.Rows[0].Reason);
        }

        [Fact]
        public void Update_ChangedRow_WithoutANewReason_IsRefused()
        {
            var square = new Mesh2Case { Id = "t", Generator = "literal", Milestone = Milestone.M1, Input = Generators.Literal(0, 0, 8, 0, 8, 8, 0, 8), Expect = new OracleExpect { Milestone = Milestone.M1 } };
            Func<int[][], Fixed> mesh = t => new Fixed { F = i => { var o = Good(i); o.Triangles = t; return o; } };
            var one = mesh(new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } });
            var other = mesh(new[] { new[] { 0, 1, 3 }, new[] { 1, 2, 3 } }); // the other diagonal, both valid
            var file = new GoldenFile();
            Assert.Empty(GoldenProcess.Update(file, new[] { square }, one, null, new Dictionary<string, string> { { "t", "first" } }));
            string before = file.Write();
            var refused = GoldenProcess.Update(file, new[] { square }, other, null, new Dictionary<string, string>());
            Assert.Contains(refused, p => p.Contains("changed") && p.Contains("without a reason"));
            Assert.Equal(before, file.Write());
            Assert.Empty(GoldenProcess.Update(file, new[] { square }, other, null, new Dictionary<string, string> { { "t", "new tie rule" } }));
            Assert.Equal("new tie rule", file.Rows[0].Reason);
        }

        [Fact]
        public void Update_RemovedRow_NeedsAReason_AndIsListed()
        {
            var file = Recorded();
            var problems = GoldenProcess.Update(file, new Mesh2Case[0], new Fixed { F = Good }, null, new Dictionary<string, string>());
            Assert.Contains(problems, p => p.Contains("removed"));
            Assert.Single(file.Rows);
            Assert.Empty(GoldenProcess.Update(file, new Mesh2Case[0], new Fixed { F = Good }, null, new Dictionary<string, string> { { "t", "case dropped" } }));
            Assert.Empty(file.Rows);
            Assert.Equal("case dropped", file.Removed.Single().Reason);
        }

        [Fact]
        public void Check_ChangedResult_IsFound()
        {
            var input = new Mesh2Case {
                Id = "t", Generator = "literal", Milestone = Milestone.M1, Input = Generators.Literal(0, 0, 8, 0, 8, 8, 0, 8), Expect = new OracleExpect { Milestone = Milestone.M1 }
            };
            Func<Mesh2Input, int[][], Mesh2Output> mesh = (i, t) => { var o = Good(i); o.Triangles = t; return o; };
            var file = new GoldenFile();
            var one = new Fixed { F = i => mesh(i, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }) };
            Assert.Empty(GoldenProcess.Update(file, new[] { input }, one, null, new Dictionary<string, string> { { "t", "first" } }));
            Assert.Empty(GoldenProcess.Check(file, new[] { input }, one));
            var other = new Fixed { F = i => mesh(i, new[] { new[] { 0, 1, 3 }, new[] { 1, 2, 3 } }) }; // the other diagonal of the square: valid
            var problems = GoldenProcess.Check(file, new[] { input }, other);
            Assert.Contains(problems, p => p.Contains("triangles_fp"));
        }

        [Fact]
        public void Check_InvalidResult_IsFound_AndMissingAndOrphanRowsToo()
        {
            var file = Recorded();
            Func<Mesh2Input, Mesh2Output> flipped = i => { var o = Good(i); o.Triangles = new[] { new[] { 0, 2, 1 } }; return o; };
            Assert.Contains(GoldenProcess.Check(file, new[] { TriangleCase() }, new Fixed { F = flipped }), p => p.Contains("fails the oracle"));
            Assert.Contains(GoldenProcess.Check(file, new Mesh2Case[0], new Fixed { F = Good }), p => p.Contains("no such case"));
            Assert.Contains(GoldenProcess.Check(new GoldenFile(), new[] { TriangleCase() }, new Fixed { F = Good }), p => p.Contains("without a recorded row"));
        }

        [Fact]
        public void VerifyChanges_FindsChangesWithoutANewReason()
        {
            var before = Recorded("first");
            var same = GoldenFile.Parse(before.Write());
            Assert.Empty(GoldenProcess.VerifyChanges(before, same));

            var changed = GoldenFile.Parse(before.Write());
            changed.Rows[0].Triangles = 2;
            Assert.Contains(GoldenProcess.VerifyChanges(before, changed), p => p.Contains("without a new reason"));
            changed.Rows[0].Reason = "   ";
            Assert.NotEmpty(GoldenProcess.VerifyChanges(before, changed));
            changed.Rows[0].Reason = "a new tie rule, see the PR";
            Assert.Empty(GoldenProcess.VerifyChanges(before, changed));

            var added = GoldenFile.Parse(before.Write());
            var extra = GoldenFile.Parse(before.Write()).Rows[0];
            extra.Case = "u"; extra.Reason = "";
            added.Rows.Add(extra);
            Assert.Contains(GoldenProcess.VerifyChanges(before, added), p => p.Contains("new row"));

            var removed = new GoldenFile();
            Assert.Contains(GoldenProcess.VerifyChanges(before, removed), p => p.Contains("removed"));
            removed.Removed.Add(new RemovedRow { Case = "t", Reason = "dropped" });
            Assert.Empty(GoldenProcess.VerifyChanges(before, removed));
        }

        [Fact]
        public void FileFormat_RoundTrips_AndRejectsAnUnknownFormat()
        {
            var file = Recorded("a \"quoted\" reason");
            Assert.Equal(file.Write(), GoldenFile.Parse(file.Write()).Write());
            Assert.Throws<FormatException>(() => GoldenFile.Parse("{\"format\": 2, \"rows\": [], \"removed\": []}"));
        }

        // ---- comparison of rows from different producers

        static GoldenRow Row(Action<GoldenRow> edit = null)
        {
            var r = new GoldenRow {
                Case = "c", Milestone = "M1", Producer = "a", InputSha256 = "i", Status = "Success", Triangles = 10, Vertices = 8, Unique = true,
                TrianglesFp = "t", SubsegmentsFp = "s", VerticesFp = "v", Area2 = "100"
            };
            edit?.Invoke(r);
            return r;
        }

        [Fact]
        public void Compare_UniqueResult_NeedsTheSameFingerprint()
        {
            Assert.Empty(RowComparison.Compare(Row(), Row(r => r.Producer = "b")));
            Assert.NotEmpty(RowComparison.Compare(Row(), Row(r => r.TrianglesFp = "x")));
            Assert.NotEmpty(RowComparison.Compare(Row(), Row(r => r.InputSha256 = "x")));
            Assert.NotEmpty(RowComparison.Compare(Row(), Row(r => r.Status = "Failed")));
            Assert.NotEmpty(RowComparison.Compare(Row(), Row(r => r.Area2 = "99")));
        }

        [Fact]
        public void Compare_Ties_ComparesOnlyMetrics()
        {
            Action<GoldenRow> tied = r => r.Unique = false;
            Assert.Empty(RowComparison.Compare(Row(tied), Row(r => { tied(r); r.TrianglesFp = "x"; })));
            Assert.NotEmpty(RowComparison.Compare(Row(tied), Row(r => { tied(r); r.Triangles = 11; })));
        }

        [Fact]
        public void Compare_M3_ComparesTheCountWithinAFactor()
        {
            Action<GoldenRow> m3 = r => { r.Milestone = "M3"; r.Unique = false; r.ConstructedVertices = 5; };
            Assert.Empty(RowComparison.Compare(Row(m3), Row(r => { m3(r); r.TrianglesFp = "x"; r.Triangles = 19; })));
            Assert.NotEmpty(RowComparison.Compare(Row(m3), Row(r => { m3(r); r.Triangles = 21; })));
        }

        [Fact]
        public void Compare_M2WithConstructedVertices_DoesNotCompareRoundedCoordinates()
        {
            Action<GoldenRow> m2 = r => { r.Milestone = "M2"; r.ConstructedVertices = 2; };
            Assert.Empty(RowComparison.Compare(Row(m2), Row(r => { m2(r); r.TrianglesFp = "x"; r.Area2 = "99"; })));
            Assert.NotEmpty(RowComparison.Compare(Row(m2), Row(r => { m2(r); r.Triangles = 11; })));
        }
    }

    /// <summary>The worked examples of docs/design/MESHING2D_GOLDEN_FORMAT.md, which a re-implementation in any language checks itself against.</summary>
    public class Meshing2FormatDocumentTests
    {
        static string Document => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Meshing2", "MESHING2D_GOLDEN_FORMAT.md"));

        static Mesh2Output Square(bool otherDiagonal)
        {
            var o = new Mesh2Output {
                Status = Mesh2Status.Success, VX = new double[] { 0, 1, 1, 0 }, VY = new double[] { 0, 0, 1, 1 },
                Triangles = otherDiagonal ? new[] { new[] { 0, 1, 3 }, new[] { 1, 2, 3 } } : new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } },
                IdentityRules = false, Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1), new SubsegmentOutput(1, 2) }
            };
            return o;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Document_States_TheFingerprintsOfTheSquare(bool other)
        {
            var o = Square(other);
            Assert.Contains(Fingerprint.Triangles(o), Document);
            Assert.Contains(Fingerprint.TriangleLines(o).TrimEnd('\n').Split('\n')[0], Document);
        }

        [Fact]
        public void Document_States_TheSubsegmentAndVertexFingerprints()
        {
            var o = Square(false);
            Assert.Contains(Fingerprint.Subsegments(o), Document);
            Assert.Contains(Fingerprint.Vertices(o), Document);
        }

        [Fact]
        public void Document_States_TheHashOfTheEmptyList()
        {
            Assert.Contains("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Document);
            Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Fingerprint.Sha256(""));
        }

        [Fact]
        public void Document_States_TheRandomGeneratorVectors()
        {
            var r = new SplitMix64(1);
            Assert.Contains(r.Next().ToString("x16"), Document);
            Assert.Contains(r.Next().ToString("x16"), Document);
        }

        public static IEnumerable<object[]> ReferenceCases() => Catalogue.ReferenceCases().Select(c => new object[] { c.Id });

        static Mesh2Case Find(string id) => Catalogue.ReferenceCases().First(c => c.Id == id);

        [Theory]
        [MemberData(nameof(ReferenceCases))]
        public void Document_Lists_TheCaseWithItsGeneratorAndHash(string id)
        {
            var c = Find(id);
            Assert.Contains(id, Document);
            Assert.Contains(c.Generator, Document);
            Assert.Contains(TextFormat.InputSha256(c.Input), Document);
        }

        [Fact]
        public void Document_AllM1CiCases_AreListed()
        {
            foreach (var c in M1Cases.Ci()) Assert.Contains("`" + c.Id + "`", Document);
        }

        [Fact]
        public void Document_StatesTheCanonicalInputTextOfTheExample()
        {
            var input = Generators.Literal(0, 0, 1, 0, 0, 1);
            Assert.Contains(TextFormat.WriteInput(input).TrimEnd('\n'), Document.Replace("\r\n", "\n"));
            Assert.Contains(TextFormat.InputSha256(input), Document);
        }
    }
}
