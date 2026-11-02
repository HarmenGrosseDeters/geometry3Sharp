using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// CLAUDE.md for the terrain overlay of TerrainSlopeDesign (phase 1b): invariance over Similarity.All() with rotations
    /// about z, with the catch-line oracles and the composite checks (O1 on SlopeRegion) in every frame on the
    /// transformed output itself; reuse; determinism (repeated runs); cancel.
    /// </summary>
    public class TerrainSlopeCatchLineInvarianceTests
    {
        static Similarity ZUp(Similarity s) =>
            s.IsRotated ? new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation) : s;

        // a pentagon pit at z = 2 in a wavy terrain (no grazing contact), slope 1:1.5
        static TerrainSlopeDesign Make(Similarity s)
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-50, -50, 50, 50), 25, 23,
                (x, y) => 9 + 1.5 * Math.Sin(0.13 * x) * Math.Cos(0.09 * y));
            foreach (int v in terrain.VertexIndices().ToList()) terrain.SetVertex(v, s.Apply(terrain.GetVertex(v)));
            var toe = SlopeToe.Constant(TerrainSlopeCatchLineTests.Ngon(5, 9, 2, Vector2d.Zero, 0.2).Select(p => s.Apply(p)).ToList(),
                true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5));
            return new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = s.ApplyLength(0.02), BuildComposite = true };
        }

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(Similarity.Identity);
            Assert.True(d.Compute(), d.Message);
            return d;
        });

        static Vector3d Back(Similarity s, Vector3d p) => s.Rotation.Transpose() * ((p - s.Translation) / s.Scale);

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities(string name)
        {
            Similarity s = ZUp(Similarity.ByName(name));
            var d = Make(s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeCatchLineTests.AssertCatchLines(d);
            TerrainSlopeCatchLineTests.AssertComposite(d);
            var r = reference.Value;
            Assert.True(r.CatchLines.Count == d.CatchLines.Count, $"{d.CatchLines.Count} lines vs {r.CatchLines.Count}: " +
                string.Join("; ", d.CatchLines.Select(l => $"{l.Points.Count} pts closed {l.Closed} {l.StartReason}/{l.EndReason}")));
            for (int i = 0; i < r.CatchLines.Count; ++i) {
                Assert.Equal(r.CatchLines[i].Closed, d.CatchLines[i].Closed);
                Assert.Equal(r.CatchLines[i].StartReason, d.CatchLines[i].StartReason);
                Assert.Equal(r.CatchLines[i].EndReason, d.CatchLines[i].EndReason);
            }
            if (s.IsExact) {
                // 2^k: the same result, scaled, bit for bit
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
                for (int i = 0; i < r.CatchLines.Count; ++i)
                    Assert.Equal(r.CatchLines[i].Points, d.CatchLines[i].Points.Select(p => Back(s, p)).ToList());
                Assert.Equal(r.UsedToeLowering * s.Scale, d.UsedToeLowering);
            } else {
                // the same lines within the rounding of the frame (positions; the slope is the same within Tolerance)
                var refPts = r.CatchLines.SelectMany(l => l.Points).ToList();
                foreach (var l in d.CatchLines)
                    foreach (var p in l.Points) {
                        Vector3d q = Back(s, p);
                        double dist = refPts.Min(x => x.xy.Distance(q.xy));
                        // (a catch point lies on its overlay edge; neighbouring reference points are at most an edge apart)
                        Assert.True(dist < 5, $"{q}: {dist}");
                    }
                double lr = r.CatchLines.Sum(l => length(l)), ld = d.CatchLines.Sum(l => length(l)) / s.Scale;
                Assert.True(Math.Abs(lr - ld) <= 1e-3 * lr, $"catch line length {ld} vs {lr}");
            }
        }

        static double length(CatchLine l)
        {
            double sum = 0;
            int n = l.Closed ? l.Points.Count : l.Points.Count - 1;
            for (int i = 0; i < n; ++i) sum += l.Points[i].xy.Distance(l.Points[(i + 1) % l.Points.Count].xy);
            return sum;
        }

        [Fact]
        public void Reuse_AndDeterminism()
        {
            var a = Make(Similarity.Identity);
            Assert.True(a.Compute());
            DMesh3 first = a.Composite;
            var firstLines = a.CatchLines;
            Assert.True(a.Compute());
            Assert.Equal("", Invariance.Difference(first, a.Composite));
            Assert.Equal(firstLines.Count, a.CatchLines.Count);
            for (int i = 0; i < firstLines.Count; ++i) {
                Assert.Equal(firstLines[i].Points, a.CatchLines[i].Points);
                Assert.Equal(firstLines[i].Closed, a.CatchLines[i].Closed);
            }
            // another input on the same instance equals a fresh instance
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 10, 10, (x, y) => 6);
            var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(2));
            a.Toes = new List<SlopeToe> { toe }; a.Terrain = terrain;
            Assert.True(a.Compute());
            var fresh = new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = 0.02, BuildComposite = true };
            Assert.True(fresh.Compute());
            Assert.Equal("", Invariance.Difference(fresh.Composite, a.Composite));
            Assert.Equal("", Invariance.Difference(fresh.SlopeRegion, a.SlopeRegion));
            // a failure clears the outputs of the earlier success
            a.Tolerance = double.NaN;
            Assert.False(a.Compute());
            Assert.Null(a.Composite); Assert.Null(a.SlopeRegion); Assert.Empty(a.CatchLines);
            // repeated runs from fresh instances are bitwise equal (the overlay has no parallel path)
            for (int k = 0; k < 2; ++k) {
                var b = Make(Similarity.Identity);
                Assert.True(b.Compute());
                Assert.Equal("", Invariance.Difference(first, b.Composite));
                Assert.Equal(firstLines.Count, b.CatchLines.Count);
                for (int i = 0; i < firstLines.Count; ++i) Assert.Equal(firstLines[i].Points, b.CatchLines[i].Points);
            }
        }

        [Fact]
        public void Cancel_InEachStage()
        {
            // count the calls of a full run, then cancel at several points of it (the overlay and the composite too)
            var d = Make(Similarity.Identity);
            int total = 0;
            d.CancelF = () => { ++total; return false; };
            Assert.True(d.Compute());
            foreach (double frac in new[] { 0.1, 0.5, 0.8, 0.95, 0.999 }) {
                var e = Make(Similarity.Identity);
                int calls = 0, at = Math.Max(1, (int)(frac * total));
                e.CancelF = () => ++calls >= at;
                Assert.False(e.Compute());
                Assert.Equal(TerrainSlopeDesign.Statuses.Cancelled, e.Status);
                Assert.False(e.IsComplete);
                Assert.Null(e.Composite); Assert.Null(e.SlopeSurface); Assert.Empty(e.CatchLines);
            }
        }
    }
}
