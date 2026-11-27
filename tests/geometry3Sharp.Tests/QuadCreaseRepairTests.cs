using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue #1452: the crease repair of the extracted graph (QuadRemesher.CreaseIndex: snap, link repair, splits of long rim edges), each
    /// step on a hand-made graph over a box or a cylinder, so that every limit is probed on both sides (the end-to-end cases are in
    /// QuadCreasePreservationTests).
    /// </summary>
    public class QuadCreaseRepairTests
    {
        static DMesh3 Box() => new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();

        static DMesh3 Cylinder(int slices) =>
            new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 1f, Height = 2f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();

        // the vertices of the top rim of the cylinder in angular order (the cylinder axis is y)
        static List<Vector3d> TopRim(DMesh3 m)
        {
            double top = m.Vertices().Max(v => v.y);
            return m.Vertices().Where(v => Math.Abs(v.y - top) < 1e-6 && Math.Abs(Math.Sqrt(v.x * v.x + v.z * v.z) - 1) < 1e-6)
                .OrderBy(v => Math.Atan2(v.z, v.x)).ToList();
        }

        // a ring graph over the rim vertices at the given indices (each linked to the next)
        static QuadRemesher.PolyGraph Ring(List<Vector3d> rim, int[] at)
        {
            var g = new QuadRemesher.PolyGraph();
            foreach (int i in at) g.Add(rim[i], Vector3d.AxisY, 1);
            for (int k = 0; k < at.Length; ++k) g.Link(k, (k + 1) % at.Length);
            return g;
        }

        // ---- snap

        [Theory]
        [InlineData(0.30, true)]       // a corner cluster 0.3 edge lengths from the corner moves onto it (limit 0.35)
        [InlineData(0.40, false)]
        public void Snap_CornerVertex_LimitIsAboutThreeTenthsOfAnEdgeLength(double offsetInEdgeLengths, bool snaps)
        {
            double s = 0.5;
            var idx = new QuadRemesher.CreaseIndex(Box(), 30, 30, s);
            var g = new QuadRemesher.PolyGraph();
            Vector3d corner = new Vector3d(1, 1, 1);
            Vector3d p = corner + offsetInEdgeLengths * s * new Vector3d(1, 1, 1).Normalized;
            g.Add(p, Vector3d.AxisY, 2);
            idx.Snap(g);
            Assert.Equal(snaps ? corner : p, g.Pos[0]);
        }

        [Theory]
        [InlineData(0.20, true)]       // a crease vertex 0.2 edge lengths from the crease moves onto it (limit 0.25)
        [InlineData(0.30, false)]
        public void Snap_CreaseVertex_LimitIsAQuarterOfAnEdgeLength(double offsetInEdgeLengths, bool snaps)
        {
            double s = 0.5;
            var idx = new QuadRemesher.CreaseIndex(Box(), 30, 30, s);
            var g = new QuadRemesher.PolyGraph();
            Vector3d p = new Vector3d(1, 1, 0.1) + offsetInEdgeLengths * s * new Vector3d(1, 1, 0).Normalized;      // next to the edge x = y = 1
            g.Add(p, Vector3d.AxisY, 1);
            idx.Snap(g);
            Assert.True(g.Pos[0].EpsilonEqual(snaps ? new Vector3d(1, 1, 0.1) : p, 1e-12), g.Pos[0].ToString());
        }

        // ---- links

        [Fact]
        public void RepairLinks_RemovesAChordOverAVertexAndJoinsTheConsecutiveOnes()
        {
            DMesh3 m = Cylinder(24);
            List<Vector3d> rim = TopRim(m);
            var idx = new QuadRemesher.CreaseIndex(m, 30, 30, 1.0);
            var g = Ring(rim, new[] { 0, 4, 8, 12, 16, 20 });
            g.Unlink(1, 2);                 // a missing link between consecutive vertices
            g.Link(0, 2);                   // a chord over vertex 1
            idx.RepairLinks(g);
            Assert.False(g.Has(0, 2), "chord");
            for (int k = 0; k < 6; ++k) Assert.True(g.Has(k, (k + 1) % 6), "link " + k);
        }

        [Theory]
        [InlineData(0.5, true)]        // the crease between two vertices is 1.05 long: 2.1 edge lengths, joined (limit 2.5)
        [InlineData(0.4, false)]       // 2.6 edge lengths
        [InlineData(0.2, false)]
        public void RepairLinks_JoinsAGapOfAtMostTwoAndAHalfEdgeLengths(double s, bool joined)
        {
            DMesh3 m = Cylinder(24);
            var idx = new QuadRemesher.CreaseIndex(m, 30, 30, s);
            var g = Ring(TopRim(m), new[] { 0, 4, 8, 12, 16, 20 });
            g.Unlink(1, 2);
            idx.RepairLinks(g);
            Assert.Equal(joined, g.Has(1, 2));
        }

        // ---- splits

        // a ring over a rim of 120 vertices (3 degrees each), the chords of steps[k] rim edges
        static QuadRemesher.PolyGraph RingBySteps(List<Vector3d> rim, params int[] steps)
        {
            var at = new List<int> { 0 }; int pos = 0;
            for (int k = 0; k < steps.Length - 1; ++k) { pos += steps[k]; at.Add(pos); }
            Assert.Equal(rim.Count, steps.Sum());
            return Ring(rim, at.ToArray());
        }

        [Theory]
        [InlineData(15, false)]       // a chord of 45 degrees is 0.076 edge lengths from the rim of radius 1 edge length: not split (limit 0.08)
        [InlineData(16, true)]        // 48 degrees: 0.0865, split
        public void SplitLongEdges_LimitIsEightHundredthsOfAnEdgeLength(int stepsPerChord, bool splits)
        {
            DMesh3 m = Cylinder(120);
            List<Vector3d> rim = TopRim(m);
            var idx = new QuadRemesher.CreaseIndex(m, 30, 30, 1.0);
            int[] steps = stepsPerChord == 15 ? Enumerable.Repeat(15, 8).ToArray() : Enumerable.Repeat(16, 7).Concat(new[] { 8 }).ToArray();
            var g = RingBySteps(rim, steps);
            int n = g.Pos.Count;
            Assert.Equal(splits, idx.SplitLongEdges(g, 100));
            Assert.Equal(splits ? n + steps.Count(x => x == 16) : n, g.Pos.Count);
        }

        [Fact]
        public void SplitLongEdges_BudgetLimitsTheNumberOfSplits_TheFarthestFirst()
        {
            DMesh3 m = Cylinder(120);
            List<Vector3d> rim = TopRim(m);
            var idx = new QuadRemesher.CreaseIndex(m, 30, 30, 1.0);
            // chords of 48, 51 and 54 degrees and the rest 7 chords of 9 degrees: the longest one is split first
            var g = RingBySteps(rim, new[] { 16, 17, 18 }.Concat(Enumerable.Repeat(3, 23)).ToArray());
            int n = g.Pos.Count;
            Assert.False(idx.SplitLongEdges(g, 0));
            Assert.Equal(n, g.Pos.Count);
            Assert.True(idx.SplitLongEdges(g, 1));
            Assert.Equal(n + 1, g.Pos.Count);
            Vector3d added = g.Pos[n];
            Vector3d mid = 0.5 * (rim[33] + rim[51]);          // the chord of 54 degrees: rim vertices 33 and 51
            Assert.True(Math.Abs(Math.Atan2(added.z, added.x) - Math.Atan2(mid.z, mid.x)) < 0.02);
        }

        [Fact]
        public void BuildWithCreaseRepair_KeepsTheResultBeforeASplitThatDropsAFace()
        {
            // a hexagon on the rim (6 sides, the most MaxFaceSides allows): a split would make it a heptagon, which BuildFaces drops
            DMesh3 m = Cylinder(24);
            var idx = new QuadRemesher.CreaseIndex(m, 30, 30, 1.0);
            var g = Ring(TopRim(m), new[] { 0, 4, 8, 12, 16, 20 });
            int dropped;
            DPolyMesh r = QuadRemesher.BuildWithCreaseRepair(g, idx, 6, out dropped, splitBudget: 1.0);
            Assert.Equal(1, r.FaceCount);
            Assert.Equal(6, r.GetFace(0).Length);
            Assert.Equal(1, dropped);       // the outer loop of the open graph
        }

        [Fact]
        public void BuildWithCreaseRepair_SplitsWhenTheFacesStayAllowed()
        {
            DMesh3 m = Cylinder(24);
            var idx = new QuadRemesher.CreaseIndex(m, 30, 30, 1.0);
            var g = Ring(TopRim(m), new[] { 0, 4, 8, 12, 16, 20 });
            int dropped;
            DPolyMesh r = QuadRemesher.BuildWithCreaseRepair(g, idx, 12, out dropped, splitBudget: 1.0);
            Assert.Equal(1, r.FaceCount);
            Assert.Equal(7, r.GetFace(0).Length);       // one split (the budget is the number of faces, 1)
        }

        [Fact]
        public void SplitBudget_IsThreePercentOfTheFaces()
        {
            // the pentagon budget (2 faces per split): a result with 90 % quads keeps 84 %
            Assert.Equal(0.03, QuadRemesher.CreaseSplitBudget);
        }
    }
}
