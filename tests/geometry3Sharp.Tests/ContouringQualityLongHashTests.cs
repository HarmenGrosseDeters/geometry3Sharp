using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class ContouringQualityLongHashTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void DensePackedKeys_AvoidCollapsedHashes(int kind)
        {
            var keys = new List<long>();
            for (int a = 0; a < 2048; ++a)
                for (int b = 0; b < 32; ++b) {
                    long k = kind == 0 ? ((long)a << 32) | (uint)(a + b + 1) :
                        kind == 1 ? a * 0x100000007L + b :
                        kind == 2 ? ((long)a << 40) | (uint)b :
                        (long)(a + (1 << 20)) | ((long)(1 << 20) << 21) | ((long)(b + (1 << 20)) << 42);
                    keys.Add(k);
                }
            // The wide Winding case needs z to overlap the low bits of x's folded hash.
            if (kind == 2) keys = Enumerable.Range(0, 512).SelectMany(x => Enumerable.Range(0, 512).Select(z => ((long)x << 40) | (uint)z)).ToList();
            Assert.True(keys.Select(k => k.GetHashCode()).Distinct().Count() < keys.Count * .75);
            Assert.True(keys.Select(EdgeKeyComparer.Instance.GetHashCode).Distinct().Count() > keys.Count * .999);
            var set = new HashSet<long>(keys, EdgeKeyComparer.Instance);
            Assert.Equal(keys.Count, set.Count);
            Assert.Equal(keys.Count, keys.Count(set.Contains));
        }

        [Fact]
        public void AuditedPackedTables_UseTheMeasuredComparer()
        {
            DirectoryInfo root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md"))) root = root.Parent;
            Assert.NotNull(root);
            string[] files = { "mesh_ops/TerrainSafety.cs", "mesh_ops/TerrainSlopeMap.cs",
                "mesh_ops/TerrainSlopeComposite.cs", "mesh_ops/TerrainSlopeOverlay.cs",
                "mesh_ops/MeshGeodesicPath.cs", "mesh_ops/WindingContouring.cs", "mesh_generators/DualContouring.cs" };
            int count = 0;
            foreach (string file in files)
                foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(root.FullName, file)),
                    @"new\s+(?:HashSet|Dictionary|ConcurrentDictionary)<long[^;=\n]*?>\(([^()]*)\)")) {
                    if (file.EndsWith("TerrainSlopeOverlay.cs") && m.Groups[1].Value.Length == 0) {
                        // containerOf uses only scalar A/B vertex ids; those hashes are already distinct.
                        Assert.Equal(1, Regex.Matches(File.ReadAllText(Path.Combine(root.FullName, file)), @"new Dictionary<long, int>\(\)").Count);
                        continue;
                    }
                    Assert.Contains("EdgeKeyComparer.Instance", m.Groups[1].Value);
                    count++;
                }
            Assert.Equal(41, count);
        }
    }
}
