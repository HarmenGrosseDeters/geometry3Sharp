using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The shards of tests/mutation/stryker-mesh-*.json split files at character offsets (File.cs{a..b}): each split
    /// must be at the start of its method (tests/mutation/README.md), and the shards of a file must meet there.
    /// </summary>
    public class MutationConfigTests
    {
        // the methods the splits of a file are at, in order
        static readonly Dictionary<string, string[]> SplitMethod = new Dictionary<string, string[]>() {
            ["mesh_ops/ExactMeshArrangement.cs"] = new[] { "void build_triangles(", "int candidates(", "void triangulate_touched(" },
            ["mesh_ops/ExactMeshBoolean.cs"] = new[] { "public static EdgeTable Build(", "internal static double far_term(", "void verify_traced(",
                "static double[] corner_box(", "internal static int plane_side_abs(", "internal static int orient3d_filtered(" },
            ["mesh_ops/MeshRoundingRepair.cs"] = new[] { "public bool Apply(", "Vector3d pos(", "void position_add(", "void rebuild_tree(", "List<int> degenerate_triangles(", "void ghost_add(", "static double segments_distance(", "bool try_zip(", "List<int> strip_around(", "static double ulp(", "void set_vertex(", "void fix_intersection(" },
            ["mesh_ops/PolyhedralEnvelope.cs"] = new[] { "void add(", "static long bit_length(", "public bool ContainsTriangle(" },
            ["mesh_ops/ExactMeshGeodesicDistance.cs"] = new[] { "void Add(" },
        };

        static string RepoRoot()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "tests", "mutation")) && File.Exists(Path.Combine(d.FullName, "CLAUDE.md")))
                    return d.FullName;
            return null;
        }

        [Fact]
        public void ShardSplits_AreAtTheStartOfTheirMethod()
        {
            string root = RepoRoot();
            Assert.NotNull(root);
            var spans = new Dictionary<string, List<(long a, long b)>>();
            foreach (string cfg in Directory.GetFiles(Path.Combine(root, "tests", "mutation"), "stryker-mesh-*.json"))
                foreach (Match m in Regex.Matches(File.ReadAllText(cfg), @"""\*\*/([^""{}]+)\{(\d+)\.\.(\d+)\}"""))
                    (spans.TryGetValue(m.Groups[1].Value, out var l) ? l : (spans[m.Groups[1].Value] = new List<(long, long)>()))
                        .Add((long.Parse(m.Groups[2].Value), long.Parse(m.Groups[3].Value)));
            Assert.Equal(SplitMethod.Keys.OrderBy(k => k), spans.Keys.OrderBy(k => k));
            foreach (var kv in spans) {
                // (as Stryker reads it in the mutation workflow, on Linux: the text without a byte order mark, with the
                // line endings of the repository, LF; a checkout on Windows may have converted them to CRLF)
                string text = File.ReadAllText(Path.Combine(root, kv.Key)).Replace("\r\n", "\n");
                var s = kv.Value.OrderBy(x => x.a).ToList();
                Assert.Equal(0, s[0].a);
                Assert.Equal(SplitMethod[kv.Key].Length, s.Count - 1);
                Assert.True(s[s.Count - 1].b >= text.Length, kv.Key + ": the last shard does not reach the end");
                for (int i = 1; i < s.Count; ++i) {
                    Assert.True(s[i - 1].b == s[i].a, kv.Key + ": shards do not meet");
                    int off = (int)s[i].a;
                    Assert.True(off < text.Length && text[off - 1] == '\n', kv.Key + ": split " + off + " is not at a line start");
                    // the method's first line, after its attributes (and comments between them)
                    var lines = text.Substring(off).Split('\n').Select(x => x.Trim()).ToList();
                    int k = 0;
                    while (lines[k].StartsWith("[") || lines[k].StartsWith("//"))
                        ++k;
                    string method = SplitMethod[kv.Key][i - 1];
                    Assert.True(lines[k].StartsWith(method), kv.Key + ": split " + off + " is at \"" + lines[k] + "\", not at " + method);
                }
            }
        }

        static Dictionary<string, string> TestFilters(string root)
        {
            var filters = new Dictionary<string, string>();
            foreach (string cfg in Directory.GetFiles(Path.Combine(root, "tests", "mutation"), "*.json")) {
                // every config has a filter (without one Stryker runs every test of the project), a plain JSON string
                // without escapes (the match would end at an escaped quote)
                Match m = Regex.Match(File.ReadAllText(cfg), @"""test-case-filter""\s*:\s*""((?:[^""\\]|\\.)*)""");
                Assert.True(m.Success, cfg + ": no test-case-filter");
                Assert.DoesNotContain("\\", m.Groups[1].Value);
                filters[Path.GetFileNameWithoutExtension(cfg)] = m.Groups[1].Value;
            }
            return filters;
        }

        // the terms of a filter: its |-alternatives, parentheses removed
        static string[] Alternatives(string filter) => filter.Split('|').Select(t => t.Trim('(', ')')).ToArray();

        [Fact]
        public void TestFilters_EveryTermNamesTests()
        {
            // a FullyQualifiedName term that names no test (a typo, a renamed class) silently drops those tests; a
            // negated term or a DisplayName exclusion that matches nothing silently excludes nothing (the display name
            // of a theory case comes from its data: the excluded word must occur in the test sources)
            string root = RepoRoot();
            Assert.NotNull(root);
            var names = typeof(MutationConfigTests).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods().Where(mi => mi.GetCustomAttributes(typeof(FactAttribute), true).Length > 0)
                    .Select(mi => t.FullName + "." + mi.Name)).ToList();
            string sources = string.Join("\n", Directory.GetFiles(Path.Combine(root, "tests", "geometry3Sharp.Tests"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)).Select(File.ReadAllText));
            var filters = TestFilters(root);
            Assert.True(filters.Count >= 24);
            foreach (var kv in filters)
                foreach (string alt in Alternatives(kv.Value))
                    foreach (string cond in alt.Split('&')) {
                        Match m = Regex.Match(cond, @"^FullyQualifiedName!?~(.+)$");
                        // (the coverage-based sets of tests/mutation/coverage_select.py: exact method names, and the rows of a
                        // theory whose display name contains a word of its data)
                        Match e = Regex.Match(cond, @"^FullyQualifiedName=(.+)$");
                        Match d = Regex.Match(cond, @"^DisplayName!?~([\w^.\- ]+)$");
                        Assert.True(m.Success || e.Success || d.Success, kv.Key + ": unknown condition " + cond);
                        if (m.Success)
                            Assert.True(names.Any(n => n.Contains(m.Groups[1].Value)), kv.Key + ": " + cond + " names no test");
                        else if (e.Success)
                            Assert.True(names.Contains(e.Groups[1].Value), kv.Key + ": " + cond + " names no test method");
                        else if (cond.StartsWith("DisplayName!~"))
                            Assert.True(sources.Contains("\"" + d.Groups[1].Value), kv.Key + ": " + cond + " names no test case");
                        else
                            // (a row of a theory: a string of its data, an enum value such as Union, or a number)
                            Assert.True(sources.Contains("\"" + d.Groups[1].Value) || sources.Contains("." + d.Groups[1].Value)
                                || Regex.IsMatch(sources, @"[(, ]" + Regex.Escape(d.Groups[1].Value) + @"[,)]"), kv.Key + ": " + cond + " names no test case");
                    }
        }

        [Fact]
        public void ArrangementParts_ExactMeshBooleanTests_IsTheClassItself_WithThreeNamedExtrasInPart3()
        {
            // the term `ExactMeshBooleanTests` is a substring match and pulled in 11 classes named after it (about 131 s of
            // 198 s): the coverage capture of the four parts never ended (350 min). The class itself, with the trailing dot;
            // the three tests of those classes that kill survivors of arrangement-3 (measured) are named one by one
            string root = RepoRoot();
            Assert.NotNull(root);
            var filters = TestFilters(root);
            string[] extras = {
                "FullyQualifiedName~ExactMeshBooleanTestsIssue710.TinyCubes_UnderSimilarity",
                "FullyQualifiedName~ExactMeshBooleanTestsTraced.Traced_SameThresholdsAsOwnRays_ExceptWithinTheBoundOfAHalfInteger",
                "FullyQualifiedName~ExactMeshBooleanTestsTraced.Traced_SerialEqualsParallel_RepeatedAndReused" };
            for (int i = 1; i <= 4; ++i) {
                var terms = Alternatives(filters["stryker-mesh-arrangement-" + i]).ToList();
                Assert.Contains("FullyQualifiedName~ExactMeshBooleanTests.", terms);
                Assert.DoesNotContain("FullyQualifiedName~ExactMeshBooleanTests", terms);
                Assert.Equal(i == 3 ? 3 : 0, terms.Count(t => t.StartsWith("FullyQualifiedName~ExactMeshBooleanTests") && t != "FullyQualifiedName~ExactMeshBooleanTests."));
                if (i == 3) foreach (string e in extras) Assert.Contains(e, terms);
            }
        }

        [Fact]
        public void Planar_ExactMeshBooleanTests_IsTheClassItself()
        {
            // the bare term matched the 11 classes named after it as well (as in the arrangement shards)
            string root = RepoRoot();
            Assert.NotNull(root);
            var terms = Alternatives(TestFilters(root)["stryker-mesh-planar"]).ToList();
            Assert.Contains("FullyQualifiedName~ExactMeshBooleanTests.", terms);
            Assert.DoesNotContain("FullyQualifiedName~ExactMeshBooleanTests", terms);
        }

        [Fact]
        public void RoundingParts_SharedTests_FewExtras()
        {
            // every part of the rounding shard runs the same tests; the coverage pass costs about 100-200 minutes per
            // part with them (README). rounding-7 (now rounding-8, since the split of D1) on the 19-term union of every rounding test (229 tests, 73 s instead
            // of 32 s) was still in its coverage pass after 262 minutes (master run 42567558219). Its span (set_vertex
            // to the end) adds the tests of the Exact Boolean 2.0 changes there: the tolerances of the largest
            // component and the ScaleDisparity guard (6913d092) and the exact flag cleared on a position write (4b94d093)
            string root = RepoRoot();
            Assert.NotNull(root);
            var filters = TestFilters(root);
            var shared = new HashSet<string>(Alternatives(filters["stryker-mesh-rounding-4"]));
            Assert.Equal(8, shared.Count);
            // (owner 2026-11-15, #843: the prefixes had grown the set to 339 tests, about 189 s, and every part timed out in
            // its coverage pass: ExactMeshBooleanTests is the class itself, not the open-winding classes named after it,
            // and the position search's tests, 10 s, run only where the search is, rounding-8)
            Assert.Contains("FullyQualifiedName~ExactMeshBooleanTests.", shared);
            Assert.Contains("FullyQualifiedName~MeshRoundingRepairTests&FullyQualifiedName!~MeshRoundingRepairTestsPositionSearch", shared);
            // (RoundingSerialWorkTests: per part only the cases of its code, a few seconds each: the per-triangle scans of
            // Apply and the range check, the tree's boxes, degenerate_triangles, remove_tiny_components)
            const string serialWork = "FullyQualifiedName~RoundingSerialWorkTests.";
            const string sliverStage = "FullyQualifiedName~ExactMeshImprintSliverStageTests";
            var extras = new Dictionary<string, string[]>() {
                // (the range check and for_triangles; Apply's finite check and anchors in rounding-10; the tree's boxes in
                // rounding-12)
                ["stryker-mesh-rounding-1"] = new[] { serialWork + "NotFiniteInOneTriangle", serialWork + "OutsideRangeInOneTriangle",
                    serialWork + "LargestMagnitudeInsideTheRange", serialWork + "NoPasses_OneBlock",
                    // (IsThinOrTiny, the input test of KeepWhenEmptied, #985)
                    "FullyQualifiedName~SafeRoundingLastComponentTests",
                    // (RuleLockedEdges and the final stage where they follow a contraction, #1034: the lock tests in parts 1, 10 and 5)
                    sliverStage },
                ["stryker-mesh-rounding-10"] = new[] { serialWork + "NotFiniteInOneTriangle", serialWork + "Field_SerialEqualsParallel", sliverStage,
                    // (the pair budget's growth with the defects, RC4a)
                    "FullyQualifiedName~SafeRoundingBudgetTests" },
                ["stryker-mesh-rounding-5"] = new[] { sliverStage },
                ["stryker-mesh-rounding-12"] = new[] { serialWork + "IntersectionsAcrossBlocks", serialWork + "PiercingTriangleAtABlockBorder",
                    serialWork + "Field_Clean" },
                ["stryker-mesh-rounding-3"] = new[] { serialWork + "NoPasses_OneBlock", serialWork + "NoPasses_TinyDegenerate", serialWork + "Field_Clean",
                    // (pair_budget, RC4a)
                    "FullyQualifiedName~SafeRoundingBudgetTests" },
                // (set_vertex to fix_intersection: the exact flag a position write clears, the position search)
                ["stryker-mesh-rounding-8"] = new[] {
                    "FullyQualifiedName~RoundingExactPairsTests.SkippingSearch_SameAsFull_AndSkips&DisplayName!~FoldedCluster",
                    "FullyQualifiedName~RoundingExactPairsTests.WithoutSharedPositionsTouch_NoSkip",
                    "FullyQualifiedName~MeshRoundingRepairTestsPositionSearch" },
                // (fix_intersection to the end: the tolerances of the largest component and the ScaleDisparity guard of
                // remove_tiny_components, and its component walk)
                ["stryker-mesh-rounding-9"] = new[] {
                    "FullyQualifiedName~ExactBooleanFarComponentTests",
                    "FullyQualifiedName~ExactBooleanScaleDisparityTests",
                    serialWork + "ConnectedComponents_SameOrder", serialWork + "Field_Clean", serialWork + "NoPasses_TinyDegenerate",
                    // (KeepWhenEmptied in remove_tiny_components, #985)
                    "FullyQualifiedName~SafeRoundingLastComponentTests" },
            };
            for (int i = 1; i <= 13; ++i) {
                string part = "stryker-mesh-rounding-" + i;
                var want = shared.Concat(extras.TryGetValue(part, out var e) ? e : new string[0]).OrderBy(x => x, StringComparer.Ordinal);
                Assert.Equal(want, Alternatives(filters[part]).OrderBy(x => x, StringComparer.Ordinal));
            }
        }

        // The UV parameterization shards (tests/mutation/README.md, "UV parameterization"): every file of parameterization/
        // is mutated by exactly one of them, so a new file cannot go unmeasured without this test failing.
        [Fact]
        public void UvShards_EveryParameterizationFileIsMutatedByExactlyOneShard()
        {
            string root = RepoRoot();
            Assert.NotNull(root);
            var mutated = new List<string>();
            foreach (string cfg in Directory.GetFiles(Path.Combine(root, "tests", "mutation"), "stryker-mesh-uv-*.json"))
                foreach (Match m in Regex.Matches(File.ReadAllText(cfg), @"""\*\*/(parameterization/[^""{}]+)"""))
                    mutated.Add(m.Groups[1].Value);
            var files = Directory.GetFiles(Path.Combine(root, "parameterization"), "*.cs")
                .Select(f => "parameterization/" + Path.GetFileName(f)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(files, mutated.OrderBy(x => x, StringComparer.Ordinal));
        }

        // The UV shards share one test filter. The sweeps are left out on purpose: RobustnessSweepTests (1197 tests) and
        // SymmetryInvarianceTests (411) would run against every mutant they cover, and the CI runs them on their own.
        [Fact]
        public void UvShards_ShareOneTestFilter_WithoutTheSweeps()
        {
            string root = RepoRoot();
            Assert.NotNull(root);
            var filters = TestFilters(root).Where(kv => kv.Key.StartsWith("stryker-mesh-uv-", StringComparison.Ordinal)).ToList();
            Assert.Equal(7, filters.Count);
            Assert.Single(filters.Select(kv => kv.Value).Distinct());
            Assert.DoesNotContain("RobustnessSweepTests", filters[0].Value);
            Assert.DoesNotContain("SymmetryInvarianceTests", filters[0].Value);
        }
    }
}
