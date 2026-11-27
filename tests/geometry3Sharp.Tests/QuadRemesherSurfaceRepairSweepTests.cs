using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1689: the 72 default and 72 PreserveCreases configurations (4 fixtures x 18 pass counts, split in three parts so that each case is within the time of a test).
    /// The repair never makes a result worse and changes none that dropped no face; the 9 default configurations that still drop a face are named (the follow-up).
    /// </summary>
    public class QuadRemesherSurfaceRepairSweepTests
    {
        static readonly int[][] Parts = { new[] { 10, 12, 14, 16, 18, 20 }, new[] { 22, 24, 26, 27, 28, 29 }, new[] { 30, 32, 35, 40, 50, 60 } };

        [Theory]
        [InlineData("sharp-box", 0, "")]
        [InlineData("sharp-box", 1, "")]
        [InlineData("sharp-box", 2, "")]
        [InlineData("sharp-cylinder", 0, "")]
        [InlineData("sharp-cylinder", 1, "")]
        [InlineData("sharp-cylinder", 2, "")]
        [InlineData("gear", 0, "14:1 16:1")]
        [InlineData("gear", 1, "22:1 26:1 27:1 28:1")]
        [InlineData("gear", 2, "")]
        [InlineData("l-profile", 0, "18:1")]
        [InlineData("l-profile", 1, "")]
        [InlineData("l-profile", 2, "35:2 60:1")]
        public void DefaultPath_NeverWorse_UnchangedWhenNothingDrops_AndTheRemainingDropsArePinned(string name, int part, string remaining)
        {
            var worse = new List<string>(); var still = new List<string>();
            foreach (int passes in Parts[part]) {
                double s = QuadRemesherClosedOutputTests.TargetOf(name);
                var before = new QuadRemesher(QuadRemesherClosedOutputTests.Shape(name, passes)) { TargetEdgeLength = s, SkipSurfaceRepair = true };
                var after = new QuadRemesher(QuadRemesherClosedOutputTests.Shape(name, passes)) { TargetEdgeLength = s };
                Assert.Equal(QuadRemeshStatus.Success, before.Compute());
                Assert.Equal(QuadRemeshStatus.Success, after.Compute());
                string id = name + "/" + passes;
                PolyMeshMetrics mb = PolyMeshMetrics.Compute(before.Result), ma = PolyMeshMetrics.Compute(after.Result);
                bool same = before.Result.VertexCount == after.Result.VertexCount && before.Result.FaceIndices.SequenceEqual(after.Result.FaceIndices);
                if (before.DroppedFaces == 0) { if (!same) worse.Add(id + " changed without a dropped face"); }
                // a closed result differs from one with a hole by its faces: the quad fraction of the 9 changed configurations is within 0.005 (measured: 0.0046)
                if (after.DroppedFaces > before.DroppedFaces || ma.BoundaryVertices > mb.BoundaryVertices) worse.Add(id + " more dropped faces / boundary vertices");
                if (!ma.IsManifold) worse.Add(id + " not manifold");
                if (ma.QuadFraction < mb.QuadFraction - 0.005) worse.Add(id + " quads " + mb.QuadFraction.ToString("F4") + " -> " + ma.QuadFraction.ToString("F4"));
                if (after.DroppedFaces > 0) still.Add(passes + ":" + after.DroppedFaces);
            }
            Assert.True(worse.Count == 0, string.Join("; ", worse));
            Assert.Equal(remaining, string.Join(" ", still));
        }

        // PreserveCreases drops no face in the 72 configurations and the repair is not part of that path: the result does not depend on it
        [Theory]
        [InlineData("sharp-box", 0)]
        [InlineData("sharp-box", 1)]
        [InlineData("sharp-box", 2)]
        [InlineData("sharp-cylinder", 0)]
        [InlineData("sharp-cylinder", 1)]
        [InlineData("sharp-cylinder", 2)]
        [InlineData("gear", 0)]
        [InlineData("gear", 1)]
        [InlineData("gear", 2)]
        [InlineData("l-profile", 0)]
        [InlineData("l-profile", 1)]
        [InlineData("l-profile", 2)]
        public void CreasedPath_DropsNothing_AndTheRepairChangesNothing(string name, int part)
        {
            foreach (int passes in Parts[part]) {
                double s = QuadRemesherClosedOutputTests.TargetOf(name);
                var a = new QuadRemesher(QuadRemesherClosedOutputTests.Shape(name, passes)) { TargetEdgeLength = s, PreserveCreases = true };
                var b = new QuadRemesher(QuadRemesherClosedOutputTests.Shape(name, passes)) { TargetEdgeLength = s, PreserveCreases = true, SkipSurfaceRepair = true };
                Assert.Equal(QuadRemeshStatus.Success, a.Compute());
                Assert.Equal(QuadRemeshStatus.Success, b.Compute());
                Assert.Equal(0, a.DroppedFaces);
                QuadRemesherClosedOutputTests.AssertSame(b.Result, a.Result);
            }
        }
    }
}
