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

        public static IEnumerable<object[]> Families() => BooleanFuzzCases.ClosedFamilies.Append(BooleanFuzzCases.OpenFamily).Select(f => new object[] { f });

        string Run(string family, int from, int count)
        {
            var report = new StringBuilder();
            int rows = 0, bad = 0;
            double off = 0, on = 0;
            for (int seed = from; seed < from + count; ++seed) {
                FuzzCase c = BooleanFuzzCases.Make(family, seed);
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                    Dictionary<string, object> r = RegionClippingCompare.Row(new List<DMesh3> { c.A, c.B }, op);
                    ++rows;
                    if (r.TryGetValue("t_off_ms", out object a)) off += (double)a;
                    if (r.TryGetValue("t_on_ms", out object b)) on += (double)b;
                    if ((string)r["verdict"] == "same") continue;
                    if (++bad <= 3) report.AppendLine($"{c} {op}: {r["verdict"]} {(r.ContainsKey("message") ? r["message"] : "")}\n{c.ToCode()}");
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
        public void TheRow_OfTheCasesOfIssue1630_IsSame()
        {
            DMesh3 a = ExactMeshBooleanCoplanarWalkTests.Cylinder(6);
            foreach (DMesh3 b in new[] { ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0), ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0.25), new DMesh3(a) })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                    Dictionary<string, object> r = RegionClippingCompare.Row(new List<DMesh3> { a, b }, op);
                    Assert.Equal("same", (string)r["verdict"]);
                    Assert.NotEqual("rejected", (string)r["cert"]);      // (certified, or the rounding removed a part as in the old rule)
                    Assert.True(r.ContainsKey("t_off_ms") && r.ContainsKey("t_on_ms") && r.ContainsKey("tris_off"));
                }
        }
    }
}
