using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The regression corpus (docs/QA.md 2.7): the main pipelines over the procedural defect cases of Harness/Defects.cs,
    /// at the origin and at UTM coordinates, against a stored table of expected outcomes. DefectInputTests checks that
    /// each operation keeps its guarantees; this checks what the operations achieve, case by case: a case that was
    /// repaired clean and no longer is fails here, even when the operation still reports its status correctly.
    ///
    /// An outcome is the result's MeshRobustRepair.NotCleanReason ("clean" for "") and, for a clean result, its volume.
    /// A change in either direction fails: a case that gets better is a reason to update the table, not a silent pass.
    /// G3_CORPUS_PRINT=1 writes the current table to the test output, to paste into Expected below (dotnet test
    /// --logger "console;verbosity=detailed" shows it). One table holds on net10.0 and on net48.
    /// </summary>
    public class RegressionCorpusTests
    {
        readonly ITestOutputHelper output;
        public RegressionCorpusTests(ITestOutputHelper output) { this.output = output; }

        /// <summary> relative volume tolerance of a clean result (the table stores 4 digits) </summary>
        const double VolumeTolerance = 0.01;

        static readonly Similarity[] Placements = { Similarity.Identity, Similarity.Utm };

        static IEnumerable<(string name, Similarity placement, Func<DMesh3> make)> Corpus()
        {
            foreach (Similarity s in Placements)
                foreach (Defects.Case c in Defects.All()) {
                    Defects.Case cc = c;
                    if (s == Similarity.Identity) yield return (c.Name, s, cc.Make);
                    else yield return (c.Name + " @ " + s.Name, s, () => Invariance.Transformed(cc.Make(), s));
                }
        }

        /// <summary> the outcome of a result mesh: its NotCleanReason, or "clean" and its volume </summary>
        static string Outcome(DMesh3 m, Similarity s)
        {
            if (m == null) return "null";
            if (m.Vertices().Any(v => v.IsFinite == false)) return "non-finite";
            string reason = MeshRobustRepair.NotCleanReason(m);
            if (reason != "") return reason;
            // volume in the case's own frame: mapped back first, because far from the origin the sum of the signed
            // tetrahedra cancels and loses digits (a thin slab at UTM coordinates comes close to the tolerance)
            double vol = ResultChecks.Volume(Invariance.MappedBack(m, s));
            return "clean " + vol.ToString("G4", CultureInfo.InvariantCulture);
        }

        static DMesh3 BoxFor(Similarity s) =>
            Invariance.Transformed(new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.6, 0.4, 0.7)) }.Generate().MakeDMesh(), s);

        static readonly Dictionary<string, Func<DMesh3, Similarity, string>> Pipelines = new Dictionary<string, Func<DMesh3, Similarity, string>>() {
            ["auto repair"] = (m, s) => {
                var r = new MeshAutoRepair(m);
                return r.Apply() ? Outcome(r.Mesh, s) : "failed";
            },
            ["robust repair"] = (m, s) => {
                var r = new MeshRobustRepair(m) { SolidifyResolution = 64 };
                if (r.Apply() == false) return "failed";
                // the reported reason and the independent check must agree on "clean"
                string o = Outcome(r.Mesh, s);
                if ((r.ResultReason == "") != o.StartsWith("clean")) return o + " (reported \"" + r.ResultReason + "\")";
                return o;
            },
            ["exact union with box"] = (m, s) => {
                var eb = new ExactMeshBoolean(m, BoxFor(s), ExactMeshBoolean.Operations.Union);
                return eb.Compute() ? Outcome(eb.Result, s) : eb.Status.ToString();
            },
            ["exact resolve"] = (m, s) => Outcome(ExactMeshBoolean.Resolve(m), s),
            // (in place, as DefectInputTests runs them: three passes at the mean edge length, half the triangles)
            ["remesh"] = (m, s) => {
                var r = new Remesher(m);
                r.SetTargetEdgeLength(MeanEdgeLength(m));
                for (int k = 0; k < 3; ++k) r.BasicRemeshPass();
                return Outcome(m, s);
            },
            ["reduce"] = (m, s) => {
                new Reducer(m).ReduceToTriangleCount(m.TriangleCount / 2);
                return Outcome(m, s);
            },
            ["alpha wrap"] = (m, s) => {
                var w = new MeshAlphaWrap(m);
                w.Compute();
                return w.Status == MeshAlphaWrap.WrapStatus.Success ? Outcome(w.Result, s) : w.Status.ToString();
            },
        };

        static double MeanEdgeLength(DMesh3 m) =>
            m.EdgeCount == 0 ? 1 : m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });

        // every pipeline has a table and every table a pipeline: a removed pipeline would otherwise drop its check silently
        [Fact]
        public void EveryPipelineHasATable() =>
            Assert.Equal(Pipelines.Keys.OrderBy(k => k), Expected.Keys.OrderBy(k => k));

        public static IEnumerable<object[]> PipelineNames() => Pipelines.Keys.Select(k => new object[] { k });

        [Theory]
        [MemberData(nameof(PipelineNames))]
        public void Pipeline_MatchesTheStoredOutcomes(string pipeline)
        {
            // the cases are independent: in parallel (a few robust repairs take half a minute each), in corpus order
            var corpus = Corpus().ToList();
            var outcomes = new string[corpus.Count];
            // (half the cores: other test classes run at the same time, some with timing checks)
            var po = new System.Threading.Tasks.ParallelOptions() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };
            System.Threading.Tasks.Parallel.For(0, corpus.Count, po, i => {
                try {
                    outcomes[i] = Pipelines[pipeline](corpus[i].make(), corpus[i].placement);
                } catch (Exception ex) {
                    outcomes[i] = "threw " + ex.GetType().Name;
                }
            });
            var actual = corpus.Select((c, i) => (name: c.name, outcome: outcomes[i])).ToList();

            if (Environment.GetEnvironmentVariable("G3_CORPUS_PRINT") == "1") {
                var sb = new StringBuilder();
                foreach (var a in actual) sb.AppendLine($"                [\"{a.name}\"] = \"{a.outcome}\",");
                output.WriteLine(sb.ToString());
            }

            Dictionary<string, string> expected = Expected[pipeline];
            var diffs = new List<string>();
            foreach (var a in actual) {
                if (expected.TryGetValue(a.name, out string e) == false) diffs.Add($"{a.name}: not in the table, now \"{a.outcome}\"");
                else if (Matches(e, a.outcome) == false) diffs.Add($"{a.name}: expected \"{e}\", now \"{a.outcome}\"");
            }
            foreach (string name in expected.Keys.Except(actual.Select(a => a.name)))
                diffs.Add($"{name}: in the table, not in the corpus");
            int clean = actual.Count(a => a.outcome.StartsWith("clean")), expectedClean = expected.Values.Count(v => v.StartsWith("clean"));
            Assert.True(diffs.Count == 0, $"{pipeline}: {clean} of {actual.Count} clean (table: {expectedClean}); changed:\n" + string.Join("\n", diffs));
        }

        /// <summary> equal outcomes; for clean ones the volume within VolumeTolerance </summary>
        static bool Matches(string expected, string actual)
        {
            if (expected.StartsWith("clean ") == false || actual.StartsWith("clean ") == false) return expected == actual;
            double ve = double.Parse(expected.Substring(6), CultureInfo.InvariantCulture);
            double va = double.Parse(actual.Substring(6), CultureInfo.InvariantCulture);
            return Math.Abs(va - ve) <= VolumeTolerance * Math.Abs(ve);
        }

        // ---- the stored outcomes ----

        // Notes on the table (October 2026):
        // - "T-junction" and "sliver cap" differ between the origin and UTM: Invariance.Transformed rounds the
        //   coordinates, so at UTM the split vertex is no longer exactly on the edge. ExactMeshBoolean then sees a real
        //   gap (not closed), and the cap of MeshAutoRepair is no longer below the degenerate-area threshold.
        // - MeshAutoRepair leaves the zero-area triangles of "sliver cap" and "T-junction" at the origin (degenerate
        //   triangles), and the overlaps of the soups (self-intersections): it does not promise more.
        // - ExactMeshBoolean keeps open input open (not closed); see DefectInputTests for "random edits 6".
        // - Remesher and Reducer run on the defect input as it is, so most outcomes are the input's own defects; the
        //   table guards the closed cases, and the defects they must not add.
        static readonly Dictionary<string, Dictionary<string, string>> Expected = new Dictionary<string, Dictionary<string, string>>() {
            ["auto repair"] = new Dictionary<string, string>() {
                ["clean sphere"] = "clean 4.012",
                ["empty"] = "empty",
                ["single triangle"] = "empty",
                ["zero-area ears"] = "clean 4.012",
                ["sliver cap"] = "degenerate triangles",
                ["T-junction"] = "degenerate triangles",
                ["triangle soup"] = "clean 4.012",
                ["interior bowtie"] = "self-intersections",
                ["touching boxes"] = "self-intersections",
                ["overlapping spheres"] = "self-intersections",
                ["nested and tiny"] = "inside-out components",
                ["unused far vertex"] = "clean 4.012",
                ["open"] = "clean 3.988",
                ["inside-out"] = "clean 4.012",
                ["partly flipped"] = "clean 4.012",
                ["duplicated triangles"] = "clean 4.012",
                ["non-manifold fin"] = "clean 4.012",
                ["flat"] = "self-intersections",
                ["thin slab"] = "clean 3.954E-06",
                ["open sheet"] = "self-intersections",
                ["random edits 1"] = "clean 4.008",
                ["random edits 2"] = "clean 4.009",
                ["random edits 3"] = "clean 4.009",
                ["random edits 4"] = "clean 3.992",
                ["random edits 5"] = "clean 4.009",
                ["random edits 6"] = "clean 4.005",
                ["random edits 7"] = "clean 4.006",
                ["random edits 8"] = "clean 4.004",
                ["random edits 9"] = "clean 4.006",
                ["random edits 10"] = "clean 4.006",
                ["random edits 11"] = "clean 4.012",
                ["random edits 12"] = "clean 3.999",
                ["clean sphere @ utm"] = "clean 4.012",
                ["empty @ utm"] = "empty",
                ["single triangle @ utm"] = "empty",
                ["zero-area ears @ utm"] = "clean 4.012",
                ["sliver cap @ utm"] = "clean 4.012",
                ["T-junction @ utm"] = "clean 4.012",
                ["triangle soup @ utm"] = "clean 4.012",
                ["interior bowtie @ utm"] = "self-intersections",
                ["touching boxes @ utm"] = "self-intersections",
                ["overlapping spheres @ utm"] = "self-intersections",
                ["nested and tiny @ utm"] = "inside-out components",
                ["unused far vertex @ utm"] = "clean 4.012",
                ["open @ utm"] = "clean 3.988",
                ["inside-out @ utm"] = "clean 4.012",
                ["partly flipped @ utm"] = "clean 4.012",
                ["duplicated triangles @ utm"] = "clean 4.012",
                ["non-manifold fin @ utm"] = "clean 4.012",
                ["flat @ utm"] = "self-intersections",
                ["thin slab @ utm"] = "clean 3.954E-06",
                ["open sheet @ utm"] = "self-intersections",
                ["random edits 1 @ utm"] = "clean 4.008",
                ["random edits 2 @ utm"] = "clean 4.009",
                ["random edits 3 @ utm"] = "clean 4.009",
                ["random edits 4 @ utm"] = "clean 3.992",
                ["random edits 5 @ utm"] = "clean 4.009",
                ["random edits 6 @ utm"] = "clean 4.005",
                ["random edits 7 @ utm"] = "clean 4.006",
                ["random edits 8 @ utm"] = "clean 4.004",
                ["random edits 9 @ utm"] = "clean 4.006",
                ["random edits 10 @ utm"] = "clean 4.006",
                ["random edits 11 @ utm"] = "clean 4.012",
                ["random edits 12 @ utm"] = "clean 3.999",
            },
            ["robust repair"] = new Dictionary<string, string>() {
                ["clean sphere"] = "clean 4.012",
                ["empty"] = "failed",
                ["single triangle"] = "clean 0.0004709",
                ["zero-area ears"] = "clean 4.012",
                ["sliver cap"] = "clean 4.012",
                ["T-junction"] = "clean 4.012",
                ["triangle soup"] = "clean 4.012",
                ["interior bowtie"] = "clean 7.833",
                ["touching boxes"] = "clean 2",
                ["overlapping spheres"] = "clean 5.709",
                ["nested and tiny"] = "clean 3.755",
                ["unused far vertex"] = "clean 4.012",
                ["open"] = "clean 3.931",
                ["inside-out"] = "clean 4.012",
                ["partly flipped"] = "clean 4.026",
                ["duplicated triangles"] = "clean 4.012",
                ["non-manifold fin"] = "clean 4.012",
                ["flat"] = "clean 0.005606",
                ["thin slab"] = "clean 3.954E-06",
                ["open sheet"] = "clean 0.29",
                ["random edits 1"] = "clean 4.008",
                ["random edits 2"] = "clean 4.009",
                ["random edits 3"] = "clean 4.009",
                ["random edits 4"] = "clean 3.992",
                ["random edits 5"] = "clean 4.009",
                ["random edits 6"] = "clean 4.005",
                ["random edits 7"] = "clean 4.006",
                ["random edits 8"] = "clean 4.004",
                ["random edits 9"] = "clean 4.006",
                ["random edits 10"] = "clean 4.006",
                ["random edits 11"] = "clean 4.012",
                ["random edits 12"] = "clean 3.999",
                ["clean sphere @ utm"] = "clean 4.012",
                ["empty @ utm"] = "failed",
                ["single triangle @ utm"] = "clean 0.000471",
                ["zero-area ears @ utm"] = "clean 4.012",
                ["sliver cap @ utm"] = "clean 4.012",
                ["T-junction @ utm"] = "clean 4.012",
                ["triangle soup @ utm"] = "clean 4.012",
                ["interior bowtie @ utm"] = "clean 7.833",
                ["touching boxes @ utm"] = "clean 2",
                ["overlapping spheres @ utm"] = "clean 5.709",
                ["nested and tiny @ utm"] = "clean 3.755",
                ["unused far vertex @ utm"] = "clean 4.012",
                ["open @ utm"] = "clean 3.931",
                ["inside-out @ utm"] = "clean 4.012",
                ["partly flipped @ utm"] = "clean 4.026",
                ["duplicated triangles @ utm"] = "clean 4.012",
                ["non-manifold fin @ utm"] = "clean 4.012",
                ["flat @ utm"] = "clean 0.005604",
                ["thin slab @ utm"] = "clean 3.954E-06",
                ["open sheet @ utm"] = "clean 0.29",
                ["random edits 1 @ utm"] = "clean 4.008",
                ["random edits 2 @ utm"] = "clean 4.009",
                ["random edits 3 @ utm"] = "clean 4.009",
                ["random edits 4 @ utm"] = "clean 3.992",
                ["random edits 5 @ utm"] = "clean 4.009",
                ["random edits 6 @ utm"] = "clean 4.005",
                ["random edits 7 @ utm"] = "clean 4.006",
                ["random edits 8 @ utm"] = "clean 4.004",
                ["random edits 9 @ utm"] = "clean 4.006",
                ["random edits 10 @ utm"] = "clean 4.006",
                ["random edits 11 @ utm"] = "clean 4.012",
                ["random edits 12 @ utm"] = "clean 3.999",
            },
            ["exact union with box"] = new Dictionary<string, string>() {
                ["clean sphere"] = "clean 4.729",
                ["empty"] = "clean 1.344",
                ["single triangle"] = "clean 1.344",
                ["zero-area ears"] = "clean 4.729",
                ["sliver cap"] = "clean 4.729",
                ["T-junction"] = "clean 4.729",
                ["triangle soup"] = "clean 4.729",
                ["interior bowtie"] = "self-intersections",
                ["touching boxes"] = "clean 2.464",
                ["overlapping spheres"] = "clean 6.038",
                ["nested and tiny"] = "clean 4.729",
                ["unused far vertex"] = "clean 4.729",
                ["open"] = "not closed",
                ["inside-out"] = "clean 1.344",
                ["partly flipped"] = "clean 4.729",
                ["duplicated triangles"] = "clean 4.729",
                ["non-manifold fin"] = "clean 4.729",
                ["flat"] = "clean 1.344",
                ["thin slab"] = "clean 1.344",
                ["open sheet"] = "not closed",
                ["random edits 1"] = "clean 4.725",
                ["random edits 2"] = "not closed",
                ["random edits 3"] = "not closed",
                ["random edits 4"] = "clean 4.71",
                ["random edits 5"] = "not closed",
                ["random edits 6"] = "not closed",
                ["random edits 7"] = "clean 4.723",
                ["random edits 8"] = "not closed",
                ["random edits 9"] = "not closed",
                ["random edits 10"] = "not closed",
                ["random edits 11"] = "not closed",
                ["random edits 12"] = "not closed",
                ["clean sphere @ utm"] = "clean 4.729",
                ["empty @ utm"] = "clean 1.344",
                ["single triangle @ utm"] = "clean 1.344",
                ["zero-area ears @ utm"] = "clean 4.729",
                ["sliver cap @ utm"] = "clean 4.729",
                ["T-junction @ utm"] = "not closed",
                ["triangle soup @ utm"] = "clean 4.729",
                ["interior bowtie @ utm"] = "self-intersections",
                ["touching boxes @ utm"] = "clean 2.464",
                ["overlapping spheres @ utm"] = "clean 6.038",
                ["nested and tiny @ utm"] = "clean 4.729",
                ["unused far vertex @ utm"] = "clean 4.729",
                ["open @ utm"] = "not closed",
                ["inside-out @ utm"] = "clean 1.344",
                ["partly flipped @ utm"] = "clean 4.729",
                ["duplicated triangles @ utm"] = "clean 4.729",
                ["non-manifold fin @ utm"] = "clean 4.729",
                ["flat @ utm"] = "clean 1.344",
                ["thin slab @ utm"] = "clean 1.344",
                ["open sheet @ utm"] = "not closed",
                ["random edits 1 @ utm"] = "clean 4.725",
                ["random edits 2 @ utm"] = "not closed",
                ["random edits 3 @ utm"] = "not closed",
                ["random edits 4 @ utm"] = "clean 4.71",
                ["random edits 5 @ utm"] = "not closed",
                ["random edits 6 @ utm"] = "not closed",
                ["random edits 7 @ utm"] = "clean 4.723",
                ["random edits 8 @ utm"] = "not closed",
                ["random edits 9 @ utm"] = "not closed",
                ["random edits 10 @ utm"] = "not closed",
                ["random edits 11 @ utm"] = "not closed",
                ["random edits 12 @ utm"] = "not closed",
            },
            ["exact resolve"] = new Dictionary<string, string>() {
                ["clean sphere"] = "clean 4.012",
                ["empty"] = "empty",
                ["single triangle"] = "empty",
                ["zero-area ears"] = "clean 4.012",
                ["sliver cap"] = "clean 4.012",
                ["T-junction"] = "clean 4.012",
                ["triangle soup"] = "clean 4.012",
                ["interior bowtie"] = "self-intersections",
                ["touching boxes"] = "clean 2",
                ["overlapping spheres"] = "clean 5.709",
                ["nested and tiny"] = "clean 4.012",
                ["unused far vertex"] = "clean 4.012",
                ["open"] = "not closed",
                ["inside-out"] = "empty",
                ["partly flipped"] = "clean 4.012",
                ["duplicated triangles"] = "clean 4.012",
                ["non-manifold fin"] = "clean 4.012",
                ["flat"] = "empty",
                ["thin slab"] = "clean 4.012E-06",
                ["open sheet"] = "not closed",
                ["random edits 1"] = "clean 4.008",
                ["random edits 2"] = "not closed",
                ["random edits 3"] = "not closed",
                ["random edits 4"] = "clean 3.992",
                ["random edits 5"] = "not closed",
                ["random edits 6"] = "not closed",
                ["random edits 7"] = "clean 4.006",
                ["random edits 8"] = "not closed",
                ["random edits 9"] = "not closed",
                ["random edits 10"] = "not closed",
                ["random edits 11"] = "not closed",
                ["random edits 12"] = "not closed",
                ["clean sphere @ utm"] = "clean 4.012",
                ["empty @ utm"] = "empty",
                ["single triangle @ utm"] = "empty",
                ["zero-area ears @ utm"] = "clean 4.012",
                ["sliver cap @ utm"] = "clean 4.012",
                ["T-junction @ utm"] = "not closed",
                ["triangle soup @ utm"] = "clean 4.012",
                ["interior bowtie @ utm"] = "self-intersections",
                ["touching boxes @ utm"] = "clean 2",
                ["overlapping spheres @ utm"] = "clean 5.709",
                ["nested and tiny @ utm"] = "clean 4.012",
                ["unused far vertex @ utm"] = "clean 4.012",
                ["open @ utm"] = "not closed",
                ["inside-out @ utm"] = "empty",
                ["partly flipped @ utm"] = "clean 4.012",
                ["duplicated triangles @ utm"] = "clean 4.012",
                ["non-manifold fin @ utm"] = "clean 4.012",
                ["flat @ utm"] = "empty",
                ["thin slab @ utm"] = "clean 4.012E-06",
                ["open sheet @ utm"] = "not closed",
                ["random edits 1 @ utm"] = "clean 4.008",
                ["random edits 2 @ utm"] = "not closed",
                ["random edits 3 @ utm"] = "not closed",
                ["random edits 4 @ utm"] = "clean 3.992",
                ["random edits 5 @ utm"] = "not closed",
                ["random edits 6 @ utm"] = "not closed",
                ["random edits 7 @ utm"] = "clean 4.006",
                ["random edits 8 @ utm"] = "not closed",
                ["random edits 9 @ utm"] = "not closed",
                ["random edits 10 @ utm"] = "not closed",
                ["random edits 11 @ utm"] = "not closed",
                ["random edits 12 @ utm"] = "not closed",
            },
            ["remesh"] = new Dictionary<string, string>() {
                ["clean sphere"] = "clean 3.806",
                ["empty"] = "empty",
                ["single triangle"] = "not closed",
                ["zero-area ears"] = "clean 3.802",
                ["sliver cap"] = "clean 3.799",
                ["T-junction"] = "not closed",
                ["triangle soup"] = "not closed",
                ["interior bowtie"] = "bowties",
                ["touching boxes"] = "clean 0.9133",
                ["overlapping spheres"] = "self-intersections",
                ["nested and tiny"] = "inside-out components",
                ["unused far vertex"] = "clean 3.806",
                ["open"] = "not closed",
                ["inside-out"] = "inside-out components",
                ["partly flipped"] = "inconsistent orientation",
                ["duplicated triangles"] = "not closed",
                ["non-manifold fin"] = "not closed",
                ["flat"] = "self-intersections",
                ["thin slab"] = "clean 2.777E-06",
                ["open sheet"] = "not closed",
                ["random edits 1"] = "not closed",
                ["random edits 2"] = "not closed",
                ["random edits 3"] = "not closed",
                ["random edits 4"] = "not closed",
                ["random edits 5"] = "not closed",
                ["random edits 6"] = "not closed",
                ["random edits 7"] = "not closed",
                ["random edits 8"] = "not closed",
                ["random edits 9"] = "not closed",
                ["random edits 10"] = "not closed",
                ["random edits 11"] = "not closed",
                ["random edits 12"] = "not closed",
                ["clean sphere @ utm"] = "clean 3.806",
                ["empty @ utm"] = "empty",
                ["single triangle @ utm"] = "not closed",
                ["zero-area ears @ utm"] = "clean 3.802",
                ["sliver cap @ utm"] = "clean 3.799",
                ["T-junction @ utm"] = "not closed",
                ["triangle soup @ utm"] = "not closed",
                ["interior bowtie @ utm"] = "bowties",
                ["touching boxes @ utm"] = "clean 0.9133",
                ["overlapping spheres @ utm"] = "self-intersections",
                ["nested and tiny @ utm"] = "inside-out components",
                ["unused far vertex @ utm"] = "clean 3.806",
                ["open @ utm"] = "not closed",
                ["inside-out @ utm"] = "inside-out components",
                ["partly flipped @ utm"] = "inconsistent orientation",
                ["duplicated triangles @ utm"] = "not closed",
                ["non-manifold fin @ utm"] = "not closed",
                ["flat @ utm"] = "self-intersections",
                ["thin slab @ utm"] = "clean 2.777E-06",
                ["open sheet @ utm"] = "not closed",
                ["random edits 1 @ utm"] = "not closed",
                ["random edits 2 @ utm"] = "not closed",
                ["random edits 3 @ utm"] = "not closed",
                ["random edits 4 @ utm"] = "not closed",
                ["random edits 5 @ utm"] = "not closed",
                ["random edits 6 @ utm"] = "not closed",
                ["random edits 7 @ utm"] = "not closed",
                ["random edits 8 @ utm"] = "not closed",
                ["random edits 9 @ utm"] = "not closed",
                ["random edits 10 @ utm"] = "not closed",
                ["random edits 11 @ utm"] = "not closed",
                ["random edits 12 @ utm"] = "not closed",
            },
            ["reduce"] = new Dictionary<string, string>() {
                ["clean sphere"] = "clean 3.941",
                ["empty"] = "empty",
                ["single triangle"] = "not closed",
                ["zero-area ears"] = "clean 3.941",
                ["sliver cap"] = "clean 3.941",
                ["T-junction"] = "not closed",
                ["triangle soup"] = "not closed",
                ["interior bowtie"] = "bowties",
                ["touching boxes"] = "clean 0.6667",
                ["overlapping spheres"] = "self-intersections",
                ["nested and tiny"] = "inside-out components",
                ["unused far vertex"] = "clean 3.953",
                ["open"] = "not closed",
                ["inside-out"] = "inside-out components",
                ["partly flipped"] = "inconsistent orientation",
                ["duplicated triangles"] = "not closed",
                ["non-manifold fin"] = "not closed",
                ["flat"] = "degenerate triangles",
                ["thin slab"] = "clean 3.899E-06",
                ["open sheet"] = "not closed",
                ["random edits 1"] = "not closed",
                ["random edits 2"] = "not closed",
                ["random edits 3"] = "not closed",
                ["random edits 4"] = "not closed",
                ["random edits 5"] = "not closed",
                ["random edits 6"] = "not closed",
                ["random edits 7"] = "not closed",
                ["random edits 8"] = "not closed",
                ["random edits 9"] = "not closed",
                ["random edits 10"] = "not closed",
                ["random edits 11"] = "not closed",
                ["random edits 12"] = "not closed",
                ["clean sphere @ utm"] = "clean 3.941",
                ["empty @ utm"] = "empty",
                ["single triangle @ utm"] = "not closed",
                ["zero-area ears @ utm"] = "clean 3.941",
                ["sliver cap @ utm"] = "clean 3.941",
                ["T-junction @ utm"] = "not closed",
                ["triangle soup @ utm"] = "not closed",
                ["interior bowtie @ utm"] = "bowties",
                ["touching boxes @ utm"] = "clean 0.6667",
                ["overlapping spheres @ utm"] = "self-intersections",
                ["nested and tiny @ utm"] = "inside-out components",
                ["unused far vertex @ utm"] = "clean 3.949",
                ["open @ utm"] = "not closed",
                ["inside-out @ utm"] = "inside-out components",
                ["partly flipped @ utm"] = "inconsistent orientation",
                ["duplicated triangles @ utm"] = "not closed",
                ["non-manifold fin @ utm"] = "not closed",
                ["flat @ utm"] = "degenerate triangles",
                ["thin slab @ utm"] = "clean 3.902E-06",
                ["open sheet @ utm"] = "not closed",
                ["random edits 1 @ utm"] = "not closed",
                ["random edits 2 @ utm"] = "not closed",
                ["random edits 3 @ utm"] = "not closed",
                ["random edits 4 @ utm"] = "not closed",
                ["random edits 5 @ utm"] = "not closed",
                ["random edits 6 @ utm"] = "not closed",
                ["random edits 7 @ utm"] = "not closed",
                ["random edits 8 @ utm"] = "not closed",
                ["random edits 9 @ utm"] = "not closed",
                ["random edits 10 @ utm"] = "not closed",
                ["random edits 11 @ utm"] = "not closed",
                ["random edits 12 @ utm"] = "not closed",
            },
            ["alpha wrap"] = new Dictionary<string, string>() {
                ["clean sphere"] = "clean 4.08",
                ["empty"] = "EmptyInput",
                ["single triangle"] = "clean 0.002331",
                ["zero-area ears"] = "clean 4.08",
                ["sliver cap"] = "clean 4.08",
                ["T-junction"] = "clean 4.08",
                ["triangle soup"] = "clean 4.08",
                ["interior bowtie"] = "clean 8.232",
                ["touching boxes"] = "clean 2.039",
                ["overlapping spheres"] = "clean 5.856",
                ["nested and tiny"] = "clean 4.111",
                ["unused far vertex"] = "clean 5.642E+10",
                ["open"] = "clean 0.2628",
                ["inside-out"] = "clean 4.08",
                ["partly flipped"] = "clean 4.08",
                ["duplicated triangles"] = "clean 4.08",
                ["non-manifold fin"] = "clean 4.177",
                ["flat"] = "clean 0.02727",
                ["thin slab"] = "clean 0.02727",
                ["open sheet"] = "clean 3.25",
                ["random edits 1"] = "clean 4.077",
                ["random edits 2"] = "clean 4.078",
                ["random edits 3"] = "clean 4.078",
                ["random edits 4"] = "clean 4.065",
                ["random edits 5"] = "clean 4.078",
                ["random edits 6"] = "clean 4.075",
                ["random edits 7"] = "clean 4.076",
                ["random edits 8"] = "clean 4.073",
                ["random edits 9"] = "clean 4.075",
                ["random edits 10"] = "clean 4.075",
                ["random edits 11"] = "clean 4.08",
                ["random edits 12"] = "clean 4.07",
                ["clean sphere @ utm"] = "clean 4.079",
                ["empty @ utm"] = "EmptyInput",
                ["single triangle @ utm"] = "clean 0.002329",
                ["zero-area ears @ utm"] = "clean 4.079",
                ["sliver cap @ utm"] = "clean 4.079",
                ["T-junction @ utm"] = "clean 4.079",
                ["triangle soup @ utm"] = "clean 4.079",
                ["interior bowtie @ utm"] = "clean 8.229",
                ["touching boxes @ utm"] = "clean 2.039",
                ["overlapping spheres @ utm"] = "clean 5.856",
                ["nested and tiny @ utm"] = "clean 4.112",
                ["unused far vertex @ utm"] = "clean 5.642E+10",
                ["open @ utm"] = "clean 0.2617",
                ["inside-out @ utm"] = "clean 4.079",
                ["partly flipped @ utm"] = "clean 4.079",
                ["duplicated triangles @ utm"] = "clean 4.079",
                ["non-manifold fin @ utm"] = "clean 4.176",
                ["flat @ utm"] = "clean 0.02726",
                ["thin slab @ utm"] = "clean 0.02723",
                ["open sheet @ utm"] = "clean 3.223",
                ["random edits 1 @ utm"] = "clean 4.076",
                ["random edits 2 @ utm"] = "clean 4.078",
                ["random edits 3 @ utm"] = "clean 4.077",
                ["random edits 4 @ utm"] = "clean 4.066",
                ["random edits 5 @ utm"] = "clean 4.077",
                ["random edits 6 @ utm"] = "clean 4.074",
                ["random edits 7 @ utm"] = "clean 4.076",
                ["random edits 8 @ utm"] = "clean 4.073",
                ["random edits 9 @ utm"] = "clean 4.074",
                ["random edits 10 @ utm"] = "clean 4.074",
                ["random edits 11 @ utm"] = "clean 4.079",
                ["random edits 12 @ utm"] = "clean 4.069",
            },
        };
    }
}
