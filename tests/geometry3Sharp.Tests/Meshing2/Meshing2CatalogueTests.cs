using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The failure catalogue of MESHING2D_SPEC section 2. A row that waits for a feature passes silently only as long as the feature
    /// does not exist: once it is registered, or the production type appears, the row fails until it has been moved to covered.
    /// </summary>
    public class Meshing2CatalogueTests
    {
        readonly ITestOutputHelper log;
        public Meshing2CatalogueTests(ITestOutputHelper log) { this.log = log; }

        static readonly List<CatalogueRow> Rows = Catalogue.Rows();

        public static IEnumerable<object[]> RowIds() => Rows.Select(r => new object[] { r.Id });
        static CatalogueRow Row(string id) => Rows.First(r => r.Id == id);

        [Fact]
        public void EveryCaseOfTheSpecification_HasARow()
        {
            for (int i = 1; i <= 66; ++i) Assert.Contains(Rows, r => r.F == "F-" + i);
            Assert.All(Rows, r => Assert.InRange(int.Parse(r.F.Substring(2)), 1, 66));
            Assert.Equal(Rows.Count, Rows.Select(r => r.Id).Distinct().Count());
        }

        [Theory]
        [MemberData(nameof(RowIds))]
        public void Row_IsInItsState(string id)
        {
            var row = Row(id);
            switch (row.State) {
                case CaseState.Deferred:
                case CaseState.Blocked:
                    Assert.False(string.IsNullOrWhiteSpace(row.Note), "a row without a fixture must state its reason");
                    Assert.Null(row.Case);
                    if (row.State == CaseState.Deferred) Assert.Matches("Q\\d+", row.Note); // a decision of the owner, by number
                    break;
                case CaseState.Pending:
                    Assert.NotNull(row.Case);
                    Assert.Contains(row.Feature, Feature.All);
                    // the loud part: the feature exists, the case still says pending
                    Assert.False(CandidateRegistry.Features.Contains(row.Feature),
                        row.Id + " is still pending although the feature " + row.Feature + " is registered: run it, move it to covered, record its golden row");
                    log.WriteLine(row.Id + " pending until " + row.Feature);
                    break;
                case CaseState.Covered:
                    Assert.Contains(row.Feature, CandidateRegistry.Features);
                    if (row.Test != null) { CheckTestExists(row.Test); break; }
                    RunThroughOracle(row);
                    break;
            }
        }

        static void CheckTestExists(string test)
        {
            var parts = test.Split('.');
            var type = typeof(Meshing2CatalogueTests).Assembly.GetTypes().FirstOrDefault(t => t.Name == parts[0]);
            Assert.True(type != null, "no test class " + parts[0]);
            Assert.True(type.GetMethod(parts[1]) != null, "no test method " + test);
        }

        static void RunThroughOracle(CatalogueRow row)
        {
            var candidate = CandidateRegistry.For(row.Case.Milestone);
            Assert.True(candidate != null, row.Id + " is covered, but there is no candidate for " + row.Case.Milestone);
            var report = Oracle.Check(row.Case.Input, candidate.Run(row.Case.Input), row.Case.Expect);
            Assert.True(report.Ok, row.Id + ": " + report);
        }

        /// <summary>The fixtures that wait are not dead data: each input is readable, and what the specification states about it holds.</summary>
        [Theory]
        [MemberData(nameof(RowIds))]
        public void Fixture_IsWellFormed_AndAgreesWithTheStatedArea(string id)
        {
            var row = Row(id);
            if (row.Case == null) return;
            var input = row.Case.Input;
            Assert.All(input.X, v => Assert.False(double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 1e70));
            Assert.All(input.Y, v => Assert.False(double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 1e70));
            foreach (var s in input.Segments) { Assert.InRange(s.A, 0, input.Count - 1); Assert.InRange(s.B, 0, input.Count - 1); }
            foreach (var r in input.Rings) Assert.All(r.Vertices, v => Assert.InRange(v, 0, input.Count - 1));
            Assert.False(string.IsNullOrWhiteSpace(row.Case.Generator));
            // the text form is complete
            var parsed = TextFormat.ReadInput(TextFormat.WriteInput(input));
            Assert.Equal(TextFormat.InputSha256(input), TextFormat.InputSha256(parsed));
            if (row.Case.Expect.StatedArea == null || row.Case.Input.HoleSeeds.Count > 0) return;
            // with no tolerance stated, the oracle's own exact area must equal the area of the specification
            var domain = new Domain(input);
            var difference = Q.Abs(domain.Area - row.Case.Expect.StatedArea.Value);
            Assert.True(difference <= row.Case.Expect.StatedAreaTolerance, row.Id + ": the oracle computes " + domain.Area + ", the specification states " + row.Case.Expect.StatedArea);
        }

        [Fact]
        public void F44_TheSeedHoleIsTheStatedRemoval_InTheDomainTest()
        {
            // F-44 lists hole seeds next to rings: by Q7 the rings decide, so the area of the rings alone is the stated 64
            var row = Row("F-44");
            Assert.Equal(new Q(64, 1), new Domain(row.Case.Input).Area);
            Assert.Equal(row.Case.Expect.StatedArea, new Q(64, 1));
        }

        [Fact]
        public void Features_TheProductionTypesOfTheLibrary_AreRegistered()
        {
            var library = typeof(Vector2d).Assembly;
            foreach (var pair in CandidateRegistry.ProductionTypes) {
                bool exists = library.GetType(pair.Key) != null;
                if (exists) Assert.True(CandidateRegistry.Features.Contains(pair.Value),
                    "the library now has " + pair.Key + ", but the feature " + pair.Value + " is not registered: the gates still judge the prototype");
            }
        }

        [Fact]
        public void Features_RegisteredOnes_HaveACandidate()
        {
            if (CandidateRegistry.Features.Contains(Feature.Cdt)) Assert.NotNull(CandidateRegistry.For(Milestone.M2));
            if (CandidateRegistry.Features.Contains(Feature.Refine)) Assert.NotNull(CandidateRegistry.For(Milestone.M3));
            Assert.NotNull(CandidateRegistry.For(Milestone.M1));
        }

        [Fact]
        public void CatalogueDocument_IsInSync()
        {
            string document = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Meshing2", "MESHING2D_CATALOGUE.md")).Replace("\r\n", "\n");
            Assert.Contains(Catalogue.Markdown(), document);
        }

        [Fact]
        public void Summary_ListsTheStates()
        {
            foreach (var group in Rows.GroupBy(r => r.State)) log.WriteLine(group.Key + ": " + group.Count());
            Assert.Contains(Rows, r => r.State == CaseState.Covered);
        }
    }
}
