using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The sets and dictionaries of edge keys (ExactMeshArrangement.EdgeKey: the smaller point id in the upper 32 bits)
    /// hash with EdgeKeyComparer. long.GetHashCode is the xor of the two halves, a ^ b: the edges between nearby ids of a
    /// mesh (dense ids, as with the Devillers-Guigue prefilter, which creates no points for pairs that do not meet) share
    /// a few hundred values, and the sets degrade to lists. ExactPlanarSimplify's flip queues used the default: 519511
    /// union spent 10 s there instead of 1.3 s.
    /// </summary>
    public class EdgeKeyHashTests
    {
        // the edges of a triangulated grid of n x n points, ids row by row
        static List<long> GridEdges(int n)
        {
            var keys = new List<long>();
            for (int y = 0; y < n; ++y)
                for (int x = 0; x < n; ++x) {
                    int v = y * n + x;
                    if (x + 1 < n) keys.Add(ExactMeshArrangement.EdgeKey(v, v + 1));
                    if (y + 1 < n) keys.Add(ExactMeshArrangement.EdgeKey(v, v + n));
                    if (x + 1 < n && y + 1 < n) keys.Add(ExactMeshArrangement.EdgeKey(v, v + n + 1));
                }
            return keys;
        }

        [Fact]
        public void DenseEdgeKeys_TheComparerSpreadsThem_TheDefaultDoesNot()
        {
            List<long> keys = GridEdges(600);
            int distinctComparer = keys.Select(k => EdgeKeyComparer.Instance.GetHashCode(k)).Distinct().Count();
            int distinctDefault = keys.Select(k => k.GetHashCode()).Distinct().Count();
            Assert.True(distinctComparer > 0.999 * keys.Count, $"{distinctComparer} of {keys.Count}");
            // (the reason for the comparer: a ^ b of nearby ids takes few values)
            Assert.True(distinctDefault < 0.01 * keys.Count, $"default: {distinctDefault} of {keys.Count}");
        }

        static string RepoRoot()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "mesh_ops")) && File.Exists(Path.Combine(d.FullName, "CLAUDE.md")))
                    return d.FullName;
            return null;
        }

        [Fact]
        public void ExactBooleanFiles_EveryLongKeyedSetHasAComparer()
        {
            // the exact code (mesh_ops/Exact*, math/Exact*): every HashSet<long> and Dictionary<long, _> is made with a
            // comparer (EdgeKeyComparer for edge keys), none with the default hash of long
            string root = RepoRoot();
            Assert.NotNull(root);
            var files = Directory.GetFiles(Path.Combine(root, "mesh_ops"), "Exact*.cs").Concat(Directory.GetFiles(Path.Combine(root, "math"), "Exact*.cs")).ToList();
            var found = new List<string>();
            foreach (string f in files) {
                string text = File.ReadAllText(f);
                foreach (Match m in Regex.Matches(text, @"new\s+(HashSet<long>|Dictionary<long\s*,[^>]+>)\s*\(([^)]*)\)"))
                    if (m.Groups[2].Value.Contains("Comparer") == false)
                        found.Add(Path.GetFileName(f) + ": " + m.Value);
            }
            Assert.True(found.Count == 0, "without a comparer: " + string.Join("; ", found));
            Assert.Contains(files, f => Path.GetFileName(f) == "ExactPlanarSimplify.cs");
        }
    }
}
