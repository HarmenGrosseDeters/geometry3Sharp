// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The targeted band merge (SlopeMap.BandMerge, TERRAIN.md 5.4): its diagnostics on the repro of
    /// TerrainSlopeBandRepairTests, and its rules on constructed meshes.
    /// </summary>
    public class TerrainSlopeBandMergeTests
    {
        // the repro near the origin: band merges of edges longer than r, within the band radius, at most MinFeatureSize,
        // the lowering counting the moves; at UTM none; the band radius and length scale bit for bit in the 2^k frames
        [Fact]
        public void Repro_BandMergesAndTheirLowering()
        {
            var origin = TerrainSlopeBandRepairTests.reference.Value;
            Assert.Empty(origin.AttemptProblemAltitudes);
            Assert.True(origin.BandMerges > 0);
            Assert.True(origin.MaxMergeDistance > origin.UsedMergeRadius && origin.MaxMergeDistance <= origin.UsedBandMergeRadius &&
                        origin.UsedBandMergeRadius <= origin.UsedMinFeatureSize, $"{origin.MaxMergeDistance:R} {origin.UsedMergeRadius:R} {origin.UsedBandMergeRadius:R}");
            // (the lowering the merge needs is measured from the distance moved, far below the band radius's
            // 2 sMax r_band; λ itself is raised further by the support's measurement, as at UTM)
            Assert.True(origin.MergeLambdaRequired > 0 && origin.MergeLambdaRequired < 1e-3 * origin.UsedBandMergeRadius, $"{origin.MergeLambdaRequired:R} {origin.UsedBandMergeRadius:R}");
            Assert.True(origin.UsedToeLowering >= origin.MergeLambdaRequired);
            // (and it counts the distance moved, not today's r: 2 sMax (moved + e_xy) with sMax = 1 / 1.5. Here the merge
            // within r moved positions too, so a band merge may carry labels by r + l: both add up)
            Assert.True(origin.MergedPositions > 0);
            Assert.True(origin.MergeLambdaRequired >= 2 / 1.5 * (origin.UsedMergeRadius + origin.MaxMergeDistance), $"{origin.MergeLambdaRequired:R} {origin.MaxMergeDistance:R}");

            var utm = TerrainSlopeBandRepairTests.WavyOnWaves(new Vector3d(327000.25, 5642000.5, -340));
            Assert.True(utm.Compute(), utm.Message);
            Assert.Equal(0, utm.BandMerges);

            foreach (string name in new[] { "scale 2^10", "scale 2^-10" }) {
                Similarity s = Similarity.ByName(name);
                var d = TerrainSlopeBandRepairTests.WavyOnWaves(s, new Vector3d(0, 0, -340));
                Assert.True(d.Compute(), d.Message);
                Assert.Equal(origin.BandMerges, d.BandMerges);
                // (the band radius from the facets' slack scales bit for bit, so the band decisions are the same)
                Assert.Equal(origin.UsedBandMergeRadius * s.Scale, d.UsedBandMergeRadius);
                Assert.Equal(origin.MaxMergeDistance * s.Scale, d.MaxMergeDistance);
            }
        }

        // two crease vertices p (0, 0) and q (dx, 0), dx far below the outer ring's size, inside a ring W, S, E, N: the
        // edge pq and its two needles (p, S, q) and (q, N, p); labels per vertex, heights z = l x + 0.25 l y + l of
        // plane l
        static DMesh3 NearQuadruple(double dx, out List<List<int>> labels, out List<bool> onBarrier, int[] pLabels, int[] qLabels)
        {
            var m = new DMesh3(false, false, false, true);
            int p = m.AppendVertex(new Vector3d(0, 0, 0)), q = m.AppendVertex(new Vector3d(dx, 0, 0));
            int w = m.AppendVertex(new Vector3d(-10, 0, 0)), so = m.AppendVertex(new Vector3d(0, -10, 0));
            int e = m.AppendVertex(new Vector3d(10, 0, 0)), n = m.AppendVertex(new Vector3d(0, 10, 0));
            foreach (var t in new[] { new Index3i(w, so, p), new Index3i(p, so, q), new Index3i(q, so, e), new Index3i(e, n, q), new Index3i(q, n, p), new Index3i(p, n, w) })
                Assert.True(m.AppendTriangle(t) >= 0);
            labels = new List<List<int>> { pLabels.ToList(), qLabels.ToList(), new List<int> { 1 }, new List<int> { 2 }, new List<int> { 3 }, new List<int> { 4 } };
            onBarrier = Enumerable.Repeat(false, 6).ToList();
            return m;
        }

        static double PlaneHeight(int l, double x, double y) => l * x + 0.25 * l * y + l;

        [Fact]
        public void BandMerge_CollapsesTheShortEdgeOfANearQuadruplePoint()
        {
            List<List<int>> labels; List<bool> onBarrier;
            var m = NearQuadruple(1e-9, out labels, out onBarrier, new[] { 1, 2, 3 }, new[] { 2, 3, 4 });
            double merged;
            Assert.Equal(1, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 1e-6, false, null, out merged));
            Assert.Equal(1e-9, merged);
            Assert.Equal(5, m.VertexCount);
            Assert.Equal(4, m.TriangleCount);
            // (p kept: as many labels, smaller x; the union of the plane sets, z the lowest of them at p)
            Assert.True(m.IsVertex(0) && m.IsVertex(1) == false);
            Assert.Equal(new[] { 1, 2, 3, 4 }, labels[0].OrderBy(l => l).ToArray());
            Assert.Equal(new Vector3d(0, 0, 1), m.GetVertex(0));
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                Assert.True(ExactPredicates.Orient2d(m.GetVertex(tv.a).xy, m.GetVertex(tv.b).xy, m.GetVertex(tv.c).xy) > 0);
            }
            // fill: the height mirrored
            m = NearQuadruple(1e-9, out labels, out onBarrier, new[] { 1, 2, 3 }, new[] { 2, 3, 4 });
            Assert.Equal(1, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 1e-6, true, null, out merged));
            Assert.Equal(-1, m.GetVertex(0).z);
        }

        [Fact]
        public void BandMerge_OnlyWhereATriangleFailsAndNotAtTheBoundary()
        {
            List<List<int>> labels; List<bool> onBarrier;
            double merged;
            // the edge's two triangles (p, so, q) and (q, n, p) pass: left as they are
            var m = NearQuadruple(1e-9, out labels, out onBarrier, new[] { 1, 2, 3 }, new[] { 2, 3, 4 });
            Assert.Equal(0, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 1e-6, false, t => false, out merged));
            // one of them fails: merged
            int needle = m.TriangleIndices().First(t => { var tv = m.GetTriangle(t); return tv.a + tv.b + tv.c == 0 + 3 + 1; });
            Assert.Equal(1, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 1e-6, false, t => t == needle, out merged));
            // an end on the mesh's boundary (the cap's level line): not moved
            m = NearQuadruple(1e-9, out labels, out onBarrier, new[] { 1, 2, 3 }, new[] { 2, 3, 4 });
            m.RemoveTriangle(m.TriangleIndices().First(t => { var tv = m.GetTriangle(t); return tv.a == 0 && tv.b == 5 && tv.c == 2; }));
            Assert.True(m.IsBoundaryVertex(0));
            Assert.Equal(0, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 1e-6, false, null, out merged));
        }

        // the band radius: today's r where the facets' slack or λ's room leave none, else capped by both and by
        // MinFeatureSize (r = 64 e_xy)
        [Fact]
        public void BandMergeRadius_Caps()
        {
            double exy = Math.Pow(2, -40), ez = 3 * exy, sMax = 0.5, r = TerrainSlopeDesign.MergeRadius(exy);
            Assert.Equal(r, TerrainSlopeDesign.BandMergeRadius(exy, ez, sMax, 1e-3, -1e-9, 1));     // no slack
            Assert.Equal(r, TerrainSlopeDesign.BandMergeRadius(exy, ez, sMax, 1e-3, 1, 0));         // no λ room
            Assert.Equal(1e-3, TerrainSlopeDesign.BandMergeRadius(exy, ez, sMax, 1e-3, 1, 1));      // MinFeatureSize
            // the slack's cap: 4 sMax (r + r_band + e_xy) <= slack
            double rb = TerrainSlopeDesign.BandMergeRadius(exy, ez, sMax, 1, 1e-6, 1);
            Assert.True(rb > r && 4 * sMax * (r + rb + exy) <= 1e-6, rb.ToString("R"));
            Assert.True(4 * sMax * (r + rb + exy) > 0.999999 * 1e-6);
            // λ's cap: the lowering of such a move and today's flips within the bound
            double lb = 1e-6;
            rb = TerrainSlopeDesign.BandMergeRadius(exy, ez, sMax, 1, 1, lb);
            Assert.True(rb > r && TerrainSlopeDesign.BandRepairLambda(ez, exy, sMax, r + rb, 1) <= lb, rb.ToString("R"));
        }

        // with band merges the lowering the repairs need is at least today's RepairLambda for the same repairs (without
        // them the code takes today's value itself: designs without needles stay bitwise today's, which the benchmark's
        // output hashes show)
        [Fact]
        public void BandRepairLambda_AtLeastTodays()
        {
            double exy = Math.Pow(2, -43), ez = 2.5 * exy, sMax = 2.0 / 3, r = TerrainSlopeDesign.MergeRadius(exy);
            for (int k = 1; k <= 2; ++k)
                Assert.True(TerrainSlopeDesign.BandRepairLambda(ez, exy, sMax, r, k - 1) >= TerrainSlopeDesign.RepairLambda(ez, exy, sMax, k));
        }

        // the collapses replayed on a realization with the same XY equal the collapses decided there
        [Fact]
        public void ReplayCollapses_EqualsDecidingAgain()
        {
            List<List<int>> labels; List<bool> onBarrier;
            var a = NearQuadruple(1e-9, out labels, out onBarrier, new[] { 1, 2, 3 }, new[] { 2, 3 });
            var made = new List<Vector2d[]>();
            Assert.Equal(1, SlopeMap.CollapseShortEdges(a, labels, onBarrier, 1e-6, made));
            List<List<int>> labels2; List<bool> onBarrier2;
            var b = NearQuadruple(1e-9, out labels2, out onBarrier2, new[] { 1, 2, 3 }, new[] { 2, 3 });
            // (another lowering: z moved, XY the same)
            foreach (int v in b.VertexIndices().ToList()) b.SetVertex(v, b.GetVertex(v) + new Vector3d(0, 0, -1e-7));
            Assert.Equal(1, SlopeMap.ReplayCollapses(b, made));
            Assert.Equal(a.VertexCount, b.VertexCount);
            foreach (int v in a.VertexIndices()) Assert.Equal(a.GetVertex(v).xy, b.GetVertex(v).xy);
            foreach (int t in a.TriangleIndices()) Assert.Equal(a.GetTriangle(t), b.GetTriangle(t));
        }

        // a chain p - q - t of two short edges: one band pass merges one of them, never both (a vertex moved twice would
        // carry labels by the sum of the lengths, more than the lowering counts)
        [Fact]
        public void BandMerge_NoChainsInOnePass()
        {
            var m = new DMesh3(false, false, false, true);
            int p = m.AppendVertex(new Vector3d(0, 0, 0)), q = m.AppendVertex(new Vector3d(1e-9, 0, 0)), t = m.AppendVertex(new Vector3d(2e-9, 0, 0));
            int w = m.AppendVertex(new Vector3d(-10, 0, 0)), so = m.AppendVertex(new Vector3d(0, -10, 0));
            int e = m.AppendVertex(new Vector3d(10, 0, 0)), n = m.AppendVertex(new Vector3d(0, 10, 0));
            foreach (var tri in new[] { new Index3i(w, so, p), new Index3i(p, so, q), new Index3i(q, so, t), new Index3i(t, so, e),
                                        new Index3i(e, n, t), new Index3i(t, n, q), new Index3i(q, n, p), new Index3i(p, n, w) })
                Assert.True(m.AppendTriangle(tri) >= 0);
            var labels = new List<List<int>> { new List<int> { 1, 2, 3 }, new List<int> { 2, 3, 4 }, new List<int> { 3, 4, 5 },
                                               new List<int> { 6 }, new List<int> { 7 }, new List<int> { 8 }, new List<int> { 9 } };
            var onBarrier = Enumerable.Repeat(false, 7).ToList();
            double merged;
            Assert.Equal(1, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 1e-6, false, null, out merged));
            Assert.Equal(1e-9, merged);
            Assert.Equal(6, m.VertexCount);
            // (a second pass may take the other edge; the realization runs one)
        }

        [Fact]
        public void BandMerge_RefusesBarriersNestedSetsLongEdgesAndTurnedTriangles()
        {
            List<List<int>> labels; List<bool> onBarrier;
            double merged;
            // an end on a barrier (a toe or lateral vertex): never moved, never merged
            var m = NearQuadruple(1e-9, out labels, out onBarrier, new[] { 1, 2, 3 }, new[] { 2, 3, 4 });
            onBarrier[1] = true;
            Assert.Equal(0, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 1e-6, false, null, out merged));
            onBarrier[1] = false; onBarrier[0] = true;
            Assert.Equal(0, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 1e-6, false, null, out merged));
            // nested plane sets: the collapse's business (CollapseShortEdges), not the band merge's
            m = NearQuadruple(1e-9, out labels, out onBarrier, new[] { 1, 2, 3 }, new[] { 2, 3 });
            Assert.Equal(0, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 1e-6, false, null, out merged));
            // an edge longer than the band radius
            m = NearQuadruple(1e-9, out labels, out onBarrier, new[] { 1, 2, 3 }, new[] { 2, 3, 4 });
            Assert.Equal(0, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 5e-10, false, null, out merged));
            Assert.Equal(0.0, merged);
            Assert.Equal(6, m.VertexCount);
            // the length in XY: a steep edge (z differs by 1) of XY length 1e-9 is still 1e-9 long
            m = NearQuadruple(1e-9, out labels, out onBarrier, new[] { 1, 2, 3 }, new[] { 2, 3, 4 });
            m.SetVertex(1, new Vector3d(1e-9, 0, 1));
            Assert.Equal(1, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 1e-6, false, null, out merged));
            // a move that turns a triangle over: refused. p and q inside closed rings (a collapse of an interior edge
            // between boundary vertices is refused by DMesh3 anyway); their common neighbours are so and c only, so
            // the link condition holds. q goes onto p (as many labels, smaller x), which turns (q, n, c) clockwise. The
            // ring's vertices are on barriers, so pq is the only candidate
            m = new DMesh3(false, false, false, true);
            int p = m.AppendVertex(new Vector3d(0, 0, 0)), q = m.AppendVertex(new Vector3d(4e-9, 0, 0));
            int c = m.AppendVertex(new Vector3d(2e-9, 1e-9, 0)), so = m.AppendVertex(new Vector3d(2e-9, -10, 0));
            int ea = m.AppendVertex(new Vector3d(10, 0, 0)), n = m.AppendVertex(new Vector3d(5e-9, 10, 0));
            int n2 = m.AppendVertex(new Vector3d(-5e-9, 10, 0)), w = m.AppendVertex(new Vector3d(-10, 0, 0));
            foreach (var t in new[] { new Index3i(p, so, q), new Index3i(q, so, ea), new Index3i(q, ea, n), new Index3i(q, n, c),
                                      new Index3i(q, c, p), new Index3i(p, c, n2), new Index3i(c, n, n2), new Index3i(p, n2, w), new Index3i(p, w, so) })
                Assert.True(m.AppendTriangle(t) >= 0);
            labels = new List<List<int>> { new List<int> { 1, 2, 3 }, new List<int> { 2, 3, 4 } };
            for (int i = 0; i < 6; ++i) labels.Add(new List<int> { 5 + i });
            onBarrier = new List<bool> { false, false, true, true, true, true, true, true };
            Assert.Equal(0, SlopeMap.BandMerge(m, labels, onBarrier, PlaneHeight, 1e-6, false, null, out merged));
            Assert.Equal(8, m.VertexCount);
        }
    }
}
