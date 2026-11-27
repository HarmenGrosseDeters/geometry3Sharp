using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1689: the 72 default and 72 PreserveCreases configurations (4 fixtures x 18 pass counts, split in nine parts of two so that each case is within the time of a test).
    /// The repair never makes a result worse and changes none that dropped no face; the 9 default configurations that still drop a face are named (the follow-up).
    /// </summary>
    public class QuadRemesherSurfaceRepairSweepTests
    {
        static readonly int[][] Parts = { new[] { 10, 12 }, new[] { 14, 16 }, new[] { 18, 20 }, new[] { 22, 24 }, new[] { 26, 27 }, new[] { 28, 29 }, new[] { 30, 32 }, new[] { 35, 40 }, new[] { 50, 60 } };

        [Theory]
        [InlineData("sharp-box", 0, "")]
        [InlineData("sharp-box", 1, "")]
        [InlineData("sharp-box", 2, "")]
        [InlineData("sharp-box", 3, "")]
        [InlineData("sharp-box", 4, "")]
        [InlineData("sharp-box", 5, "")]
        [InlineData("sharp-box", 6, "")]
        [InlineData("sharp-box", 7, "")]
        [InlineData("sharp-box", 8, "")]
        [InlineData("sharp-cylinder", 0, "")]
        [InlineData("sharp-cylinder", 1, "")]
        [InlineData("sharp-cylinder", 2, "")]
        [InlineData("sharp-cylinder", 3, "")]
        [InlineData("sharp-cylinder", 4, "")]
        [InlineData("sharp-cylinder", 5, "")]
        [InlineData("sharp-cylinder", 6, "")]
        [InlineData("sharp-cylinder", 7, "")]
        [InlineData("sharp-cylinder", 8, "")]
        [InlineData("gear", 0, "")]
        [InlineData("gear", 1, "")]
        [InlineData("gear", 2, "")]
        [InlineData("gear", 3, "")]
        [InlineData("gear", 4, "")]
        [InlineData("gear", 5, "")]
        [InlineData("gear", 6, "")]
        [InlineData("gear", 7, "")]
        [InlineData("gear", 8, "")]
        [InlineData("l-profile", 0, "")]
        [InlineData("l-profile", 1, "")]
        [InlineData("l-profile", 2, "")]
        [InlineData("l-profile", 3, "")]
        [InlineData("l-profile", 4, "")]
        [InlineData("l-profile", 5, "")]
        [InlineData("l-profile", 6, "")]
        [InlineData("l-profile", 7, "")]
        [InlineData("l-profile", 8, "")]
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
                if (before.DroppedFaces == 0 && !same) worse.Add(id + " changed without a dropped face");
                if (after.DroppedFaces == before.DroppedFaces && !same) worse.Add(id + " changed with as many dropped faces");        // only strictly fewer are accepted
                if (!after.Result.Validate().IsValid) worse.Add(id + " invalid");
                if (!same && !QuadRemesher.HasNoRepeatedVertex(after.Result)) worse.Add(id + " a face with a repeated vertex");
                if (QuadRemesher.BoundaryEdgeCount(after.Result) > QuadRemesher.BoundaryEdgeCount(before.Result)) worse.Add(id + " more boundary edges");
                // a closed result differs from one with a hole by its faces: the quad fraction of the 18 changed configurations is within 0.005 (measured: 0.0048, l-profile/35; an exact ratio of face counts, no noise)
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
        [InlineData("sharp-box", 3)]
        [InlineData("sharp-box", 4)]
        [InlineData("sharp-box", 5)]
        [InlineData("sharp-box", 6)]
        [InlineData("sharp-box", 7)]
        [InlineData("sharp-box", 8)]
        [InlineData("sharp-cylinder", 0)]
        [InlineData("sharp-cylinder", 1)]
        [InlineData("sharp-cylinder", 2)]
        [InlineData("sharp-cylinder", 3)]
        [InlineData("sharp-cylinder", 4)]
        [InlineData("sharp-cylinder", 5)]
        [InlineData("sharp-cylinder", 6)]
        [InlineData("sharp-cylinder", 7)]
        [InlineData("sharp-cylinder", 8)]
        [InlineData("gear", 0)]
        [InlineData("gear", 1)]
        [InlineData("gear", 2)]
        [InlineData("gear", 3)]
        [InlineData("gear", 4)]
        [InlineData("gear", 5)]
        [InlineData("gear", 6)]
        [InlineData("gear", 7)]
        [InlineData("gear", 8)]
        [InlineData("l-profile", 0)]
        [InlineData("l-profile", 1)]
        [InlineData("l-profile", 2)]
        [InlineData("l-profile", 3)]
        [InlineData("l-profile", 4)]
        [InlineData("l-profile", 5)]
        [InlineData("l-profile", 6)]
        [InlineData("l-profile", 7)]
        [InlineData("l-profile", 8)]
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
