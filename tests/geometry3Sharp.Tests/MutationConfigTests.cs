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
            ["mesh_ops/ExactMeshBoolean.cs"] = new[] { "int front_winding(", "double front_generalized_winding(", "AxisAlignedBox3d input_bounds(" },
            ["mesh_ops/MeshRoundingRepair.cs"] = new[] { "void position_add(", "List<int> degenerate_triangles(", "void ghost_add(", "static double segments_distance(", "List<int> strip_around(", "static double ulp(", "void set_vertex(" },
            ["mesh_ops/PolyhedralEnvelope.cs"] = new[] { "void add(", "static long bit_length(", "public bool ContainsTriangle(" },
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
                        Match d = Regex.Match(cond, @"^DisplayName!~(\w+)$");
                        Assert.True(m.Success || d.Success, kv.Key + ": unknown condition " + cond);
                        if (m.Success)
                            Assert.True(names.Any(n => n.Contains(m.Groups[1].Value)), kv.Key + ": " + cond + " names no test");
                        else
                            Assert.True(sources.Contains("\"" + d.Groups[1].Value), kv.Key + ": " + cond + " names no test case");
                    }
        }

        [Fact]
        public void BooleanParts_AntipodalTestsOnlyInTheAntipodalPart()
        {
            // Under Stryker's coverage capture the ExactBooleanOpenInputTestsAntipodal class costs 94 minutes (22 s of tests; the
            // other ten classes of the filter together 3 minutes, README "Coverage pass"), in every part that runs it, since its hot
            // loops sit in ExactMeshBoolean.cs. So only the part that mutates the Antipodal methods (front_generalized_winding to
            // BoundarySegments, boolean-3) runs it; the others run the same other tests (the term for ExactBooleanOpenInputTests
            // has a dot at both ends, which the Antipodal class name does not have). ExactBooleanFilterTests stay with the parts
            // from front_winding on.
            string root = RepoRoot();
            Assert.NotNull(root);
            var filters = TestFilters(root);
            const string antipodal = "FullyQualifiedName~ExactBooleanOpenInputTestsAntipodal";
            var baseTerms = new HashSet<string>(Alternatives(filters["stryker-mesh-boolean-1"]));
            Assert.DoesNotContain(antipodal, baseTerms);
            Assert.Contains("FullyQualifiedName~.ExactBooleanOpenInputTests.", baseTerms);
            for (int i = 1; i <= 4; ++i) {
                var want = baseTerms.Concat(i >= 2 ? new[] { "FullyQualifiedName~ExactBooleanFilterTests" } : new string[0])
                    .Concat(i == 3 ? new[] { antipodal } : new string[0]).OrderBy(x => x, StringComparer.Ordinal);
                Assert.Equal(want, Alternatives(filters["stryker-mesh-boolean-" + i]).OrderBy(x => x, StringComparer.Ordinal));
            }
            // the tests of the Antipodal class (and only they) are matched by the dotted term's neighbours: no part but boolean-3 runs them
            var names = typeof(MutationConfigTests).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods().Where(mi => mi.GetCustomAttributes(typeof(FactAttribute), true).Length > 0).Select(mi => t.FullName + "." + mi.Name)).ToList();
            var antipodalTests = names.Where(n => n.Contains(".ExactBooleanOpenInputTestsAntipodal.")).ToList();
            Assert.NotEmpty(antipodalTests);
            foreach (string test in antipodalTests)
                for (int i = 1; i <= 4; ++i) {
                    bool runs = Alternatives(filters["stryker-mesh-boolean-" + i]).Any(t => test.Contains(t.Substring("FullyQualifiedName~".Length)));
                    Assert.Equal(i == 3, runs);
                }
            Assert.False(File.Exists(Path.Combine(root, "tests", "mutation", "stryker-mesh-boolean-5.json")));
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
            var shared = new HashSet<string>(Alternatives(filters["stryker-mesh-rounding-1"]));
            Assert.Equal(8, shared.Count);
            var extras = new Dictionary<string, string[]>() {
                ["stryker-mesh-rounding-8"] = new[] {
                    "FullyQualifiedName~ExactBooleanFarComponentTests",
                    "FullyQualifiedName~ExactBooleanScaleDisparityTests",
                    "FullyQualifiedName~RoundingExactPairsTests.SkippingSearch_SameAsFull_AndSkips&DisplayName!~FoldedCluster",
                    "FullyQualifiedName~RoundingExactPairsTests.WithoutSharedPositionsTouch_NoSkip" },
            };
            for (int i = 1; i <= 8; ++i) {
                string part = "stryker-mesh-rounding-" + i;
                var want = shared.Concat(extras.TryGetValue(part, out var e) ? e : new string[0]).OrderBy(x => x, StringComparer.Ordinal);
                Assert.Equal(want, Alternatives(filters[part]).OrderBy(x => x, StringComparer.Ordinal));
            }
        }
    }
}
