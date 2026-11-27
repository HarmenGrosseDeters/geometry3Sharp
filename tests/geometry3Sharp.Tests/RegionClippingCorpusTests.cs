using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The corpus gate of E' stage 4 (docs/design/COPLANAR_REGION_CLIPPING.md, #1630) on the procedural fuzz families: for each case and operation, ExactMeshBoolean with and without
    /// RegionClipping must give the same Status and the same NotCleanReason, an exact difference of the two results that is empty both ways, and a certificate that holds with the
    /// switch on (RegionClippingCompare.Row). The PR tier runs a few seeds per family; the Nightly tier many (the run is the check: about 12 s per family). Thingi10K and the audit
    /// corpus are run by benchmarks/BooleanComparison (region-compare) in .github/workflows/region-clipping-corpus.yml, which fetches the models at run time only.
    /// </summary>
    public class RegionClippingCorpusTests
    {
        readonly ITestOutputHelper output;
        public RegionClippingCorpusTests(ITestOutputHelper output) { this.output = output; }

        public static IEnumerable<object[]> Families() => BooleanFuzzCases.ClosedFamilies.Append(BooleanFuzzCases.OpenFamily).Append(BooleanFuzzCases.FlatEdgeFamily).Select(f => new object[] { f });

        // Known differences of the rounded results with the same exact surface (reported to the owner, benchmarks/BooleanComparison/README.md, "Region clipping corpus gate"): slab #52 is a slab of
        // thickness 2^-42 on a box face; the predicate "inside the slab, outside the box" selects exactly it, and the rounding of the result collapses it to two different meshes
        static readonly HashSet<string> RoundingSensitive = new HashSet<string> { "slab|52|pred-only-second" };

        string Run(string family, int from, int count)
        {
            var report = new StringBuilder();
            int rows = 0, bad = 0;
            double off = 0, on = 0;
            for (int seed = from; seed < from + count; ++seed) {
                FuzzCase c = BooleanFuzzCases.Make(family, seed);
                // (the three operations, and for closed inputs the predicates that read the exact winding numbers: a crossing counted twice shows only there)
                var jobs = new List<(Ops, Func<int[], bool>, string)> { (Ops.Union, null, null), (Ops.Intersection, null, null), (Ops.Difference, null, null) };
                if (c.Open == false)
                    foreach (var p in RegionClippingCompare.ExactPredicates)
                        jobs.Add((Ops.Union, p.Predicate, p.Name));
                foreach (var (op, pred, pname) in jobs) {
                    Dictionary<string, object> r = RegionClippingCompare.Row(new List<DMesh3> { c.A, c.B }, op, true, null, pred, pname);
                    ++rows;
                    if (r.TryGetValue("t_off_ms", out object a)) off += (double)a;
                    if (r.TryGetValue("t_on_ms", out object b)) on += (double)b;
                    if ((string)r["verdict"] == "same" || (string)r["verdict"] == "not compared (not clean)") continue;
                    if (RoundingSensitive.Contains(family + "|" + seed + "|" + r["op"])) { output.WriteLine($"known: {c} {r["op"]}: {r["verdict"]}"); continue; }
                    if (++bad <= 3) report.AppendLine($"{c} {r["op"]}: {r["verdict"]} {(r.ContainsKey("message") ? r["message"] : "")}\n{c.ToCode()}");
                }
            }
            output.WriteLine($"{family}: seeds {from}..{from + count - 1}, {rows} rows, {bad} differ; {off:F0} ms without, {on:F0} ms with");
            return bad == 0 ? "" : $"{bad} of {rows} rows of '{family}' differ:\n" + report;
        }

        [Theory]
        [MemberData(nameof(Families))]
        public void Smoke_TheSwitchChangesNothingButTheTriangulation(string family)
        {
            // (a few seeds per family: about 0.5 s; the Nightly test below runs many)
            Assert.Equal("", Run(family, 0, 6));
        }

        [Theory]
        [MemberData(nameof(Families))]
        [Trait("Category", "Nightly")]
        public void Nightly_TheSwitchChangesNothingButTheTriangulation(string family)
        {
            // the run is the check: 300 seeds x 3 operations x (4 booleans and a certificate) take about 12 s per family, above the 10 s of the PR tier
            int from = int.TryParse(Environment.GetEnvironmentVariable("REGION_CORPUS_FROM"), out int f) ? f : 6;
            int count = int.TryParse(Environment.GetEnvironmentVariable("REGION_CORPUS_COUNT"), out int n) ? n : 300;
            Assert.Equal("", Run(family, from, count));
        }

        [Fact]
        public void TheRow_ReportsADifference_PositiveControl()
        {
            // a change of the run with the switch on that the gate must see: another operation (the surface differs), a budget that fails it (failed differently)
            DMesh3 a = ExactMeshBooleanCoplanarWalkTests.Cylinder(6), b = ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0.25);
            var meshes = new List<DMesh3> { a, b };
            Assert.Equal("same", (string)RegionClippingCompare.Row(meshes, Ops.Union)["verdict"]);
            string other = (string)RegionClippingCompare.Row(meshes, Ops.Union, false, x => x.Operation = Ops.Intersection)["verdict"];
            Assert.Contains(other, new[] { "surface differs", "reason differs" });
            Assert.Equal("failed differently", (string)RegionClippingCompare.Row(meshes, Ops.Union, false, x => x.MaxPieces = 10)["verdict"]);
        }

        [Fact]
        public void TheExactColumn_OfTheKnownDifference_IsEqual_AndNamesTheScales()
        {
            // slab #52: the rounded results differ, the unrounded ones are the same faces; the slab (2^-42) is above the resolution of the coordinates and near the repair's tolerance
            FuzzCase c = BooleanFuzzCases.Make("slab", 52);
            var p = RegionClippingCompare.ExactPredicates.First(x => x.Name == "only-second");
            Dictionary<string, object> r = RegionClippingCompare.Row(new List<DMesh3> { c.A, c.B }, Ops.Union, false, null, p.Predicate, p.Name);
            Assert.Equal("surface differs", (string)r["verdict"]);
            Assert.Equal("equal", (string)r["exact"]);
            Assert.True((double)r["min_edge"] > (double)r["round_res"] && (double)r["min_edge"] < 4 * (double)r["repair_tol"]);
            // (and a row that is the same has no exact column)
            Assert.False(RegionClippingCompare.Row(new List<DMesh3> { c.A, c.B }, Ops.Union, false).ContainsKey("exact"));
        }

        [Fact]
        public void SameFaces_SeesTheTriangulationAsNoDifference_AndTheSizeOfAFaceAsOne()
        {
            System.Numerics.BigInteger one = 1;
            ExactPoint3 P(int x, int y, int w = 1) => ExactPoint3.Create(x, y, 0, w);
            // the square 0..2 in two triangulations (with a vertex on one side that the second has too), and a square 0..3
            var a = new List<ExactPoint3[]> { new[] { P(0, 0), P(2, 0), P(2, 2) }, new[] { P(0, 0), P(2, 2), P(0, 2) } };
            var b = new List<ExactPoint3[]> { new[] { P(0, 0), P(2, 0), P(0, 2) }, new[] { P(2, 0), P(2, 2), P(0, 2) } };
            var c = new List<ExactPoint3[]> { new[] { P(0, 0), P(3, 0), P(3, 3) }, new[] { P(0, 0), P(3, 3), P(0, 3) } };
            var d = new List<ExactPoint3[]> { new[] { P(0, 0), P(2, 0), P(0, 2) }, new[] { P(2, 0), P(0, 2), P(2, 2) } };       // (the second triangle flipped over)
            Assert.True(RegionClippingCompare.SameFaces(a, b));
            Assert.False(RegionClippingCompare.SameFaces(a, c));
            Assert.False(RegionClippingCompare.SameFaces(b, d));
            Assert.True(one == 1);
        }

        [Fact]
        public void TheFlatEdgeFamily_PutsCentroidsOnFlatDiagonals()
        {
            // (the family exists to kill a mutant of the one-crossing-per-region rule: some pieces must have a stack with two triangles of one region)
            int hits = 0;
            for (int seed = 0; seed < 40; ++seed) {
                FuzzCase c = BooleanFuzzCases.Make(BooleanFuzzCases.FlatEdgeFamily, seed);
                var neu = new ExactMeshBoolean(new List<DMesh3> { c.A, c.B }, Ops.Union) { RegionClipping = true, RecordStacks = true };
                Assert.True(neu.Compute(), neu.Error);
                foreach (List<int> st in neu.StacksPerPiece)
                    if (st != null)
                        for (int i = 0; i < st.Count; ++i)
                            for (int j = i + 1; j < st.Count; ++j)
                                if (neu.Arrangement.RegionOf[st[i]] == neu.Arrangement.RegionOf[st[j]]) { ++hits; i = st.Count; break; }
            }
            Assert.True(hits > 0, "no stack with two triangles of one region in 40 seeds");
        }

        [Fact]
        public void TheRow_OfTheCasesOfIssue1630_IsSame()
        {
            DMesh3 a = ExactMeshBooleanCoplanarWalkTests.Cylinder(6);
            foreach (DMesh3 b in new[] { ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0), ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0.25), new DMesh3(a) })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                    Dictionary<string, object> r = RegionClippingCompare.Row(new List<DMesh3> { a, b }, op);
                    Assert.Equal("same", (string)r["verdict"]);
                    Assert.Contains((string)r["cert"], new[] { "certified", "removed parts" });      // (the rounding may remove a part, as in the old rule)
                    Assert.True(r.ContainsKey("t_off_ms") && r.ContainsKey("t_on_ms") && r.ContainsKey("tris_off"));
                }
        }
    }
}
