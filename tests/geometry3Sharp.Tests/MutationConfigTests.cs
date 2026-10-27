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
        // the method each split is at
        static readonly Dictionary<string, string> SplitMethod = new Dictionary<string, string>() {
            ["mesh_ops/ExactMeshArrangement.cs"] = "void build_triangles(",
            ["mesh_ops/ExactMeshBoolean.cs"] = "int front_winding(",
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
                    Assert.True(lines[k].StartsWith(SplitMethod[kv.Key]), kv.Key + ": split " + off + " is at \"" + lines[0] + "\", not at " + SplitMethod[kv.Key]);
                }
            }
        }
    }
}
