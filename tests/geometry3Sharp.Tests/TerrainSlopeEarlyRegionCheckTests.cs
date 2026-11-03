// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The composite's region is checked as soon as it is made (docs/design/TERRAIN.md 5.4, step 4, "The first λ and the
    /// composite's clipped region"): a build whose region fails the exact checks does not copy the rest of the terrain.
    /// The overlap check and the rebuilds go on as before, so the results are the same as with the check after the
    /// whole composite, bit for bit.
    /// </summary>
    public class TerrainSlopeEarlyRegionCheckTests
    {
        // a wavy 32-gon (r 100 m) on waves of amplitude 10 m (TerrainSlopeBandRepairTests.WavyOnWaves): the first
        // realization passes on the slope surface and fails on the clipped region's support, λ is raised by measurement
        static TerrainSlopeDesign Make(Similarity s, Vector3d c, bool early)
        {
            var d = TerrainSlopeBandRepairTests.WavyOnWaves(s, c, n: 32, r: 100, amp: 10, sp: 8);
            d.EarlyRegionCheck = early;
            return d;
        }

        static void AssertSame(TerrainSlopeDesign a, TerrainSlopeDesign b)
        {
            Assert.Equal(a.Status, b.Status);
            Assert.Equal(a.Attempts, b.Attempts);
            Assert.Equal(a.Realizations, b.Realizations);
            Assert.Equal(a.UsedToeLowering, b.UsedToeLowering);
            Assert.Equal(a.UsedSlopeMargin, b.UsedSlopeMargin);
            Assert.Equal(a.Volume, b.Volume);
            Same(a.SlopeSurface, b.SlopeSurface);
            Same(a.SlopeRegion, b.SlopeRegion);
            Same(a.Composite, b.Composite);
            Same(a.Replacement, b.Replacement);
            Assert.Equal(a.Message, b.Message);
            Assert.Equal(a.ProblemTriangles, b.ProblemTriangles);
            Assert.Equal(a.CatchLines.Count, b.CatchLines.Count);
            for (int i = 0; i < a.CatchLines.Count; ++i) Assert.Equal(a.CatchLines[i].Points, b.CatchLines[i].Points);
        }

        static void Same(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a == null, b == null);
            if (a != null) Assert.Equal("", Invariance.Difference(a, b));
        }

        // near the origin and at UTM: the failing build skipped the copy, λ was raised by measurement (one attempt), and
        // the result is the one of the check after the whole composite, bit for bit
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RegionFailure_SkipsTheCopy_SameResult(bool utm)
        {
            var c = utm ? new Vector3d(327000.25, 5642000.5, -340) : new Vector3d(0, 0, -340);
            var early = Make(Similarity.Identity, c, true);
            Assert.True(early.Compute(), early.Status + " " + early.Message);
            Assert.Equal(1, early.Attempts);
            Assert.True(early.Realizations >= 2);
            Assert.True(early.SkippedTerrainCopies >= 1);
            TerrainSlopeCatchLineTests.AssertCatchLines(early, false);
            TerrainSlopeCatchLineTests.AssertComposite(early);
            var late = Make(Similarity.Identity, c, false);
            Assert.True(late.Compute(), late.Status + " " + late.Message);
            Assert.Equal(0, late.SkippedTerrainCopies);
            AssertSame(late, early);
        }

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(Similarity.Identity, new Vector3d(0, 0, -340), true);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            return d;
        });

        // every frame succeeds with the same result as the late check; the 2^k frames as the identity, scaled, bit for
        // bit, with the same skips
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var early = Make(s, new Vector3d(0, 0, -340), true);
            Assert.True(early.Compute(), early.Status + " " + early.Message);
            TerrainSlopeCatchLineTests.AssertComposite(early);
            var late = Make(s, new Vector3d(0, 0, -340), false);
            late.Compute();
            AssertSame(late, early);
            var r = reference.Value;
            if (s.IsExact) {
                Assert.Equal(r.SkippedTerrainCopies, early.SkippedTerrainCopies);
                Assert.Equal(r.Realizations, early.Realizations);
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(early.Composite, s)));
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(early.SlopeRegion, s)));
            }
        }

        // the replacement only (no copy either way), and an unwelded terrain (the vertices of one grid column doubled,
        // the triangles left of it on the one copy, right of it on the other, through the region): the triangles copied
        // at cut edges keep the same decisions. Early and late give the same result in each
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReplacementAndUnweldedTerrain_SameResult(bool unwelded)
        {
            Func<bool, TerrainSlopeDesign> make = (early) => {
                var d = Make(Similarity.Identity, new Vector3d(0, 0, -340), early);
                if (unwelded) d.Terrain = Unwelded(d.Terrain);
                else d.CompositeKind = TerrainSlopeDesign.CompositeKinds.Replacement;
                return d;
            };
            var e = make(true);
            e.Compute();
            var l = make(false);
            l.Compute();
            AssertSame(l, e);
            Assert.Equal(l.ReplacedTerrainTriangles, e.ReplacedTerrainTriangles);
            Assert.Equal(l.CompositeTerrainVertices, e.CompositeTerrainVertices);
        }

        // the terrain with the vertices at the middle column doubled: the triangles on its right use the doubles
        static DMesh3 Unwelded(DMesh3 t)
        {
            var bounds = t.GetBounds();
            double xm = double.NaN, best = double.MaxValue;
            foreach (int v in t.VertexIndices()) {
                double x = t.GetVertex(v).x;
                if (Math.Abs(x - bounds.Center.x) < best) { best = Math.Abs(x - bounds.Center.x); xm = x; }
            }
            var m = new DMesh3(false, false, false, false);
            var twin = new System.Collections.Generic.Dictionary<int, int>();
            foreach (int v in t.VertexIndices()) m.AppendVertex(t.GetVertex(v));
            foreach (int v in t.VertexIndices())
                if (t.GetVertex(v).x == xm) twin[v] = m.AppendVertex(t.GetVertex(v));
            foreach (int tid in t.TriangleIndices()) {
                Index3i tv = t.GetTriangle(tid);
                double cx = (t.GetVertex(tv.a).x + t.GetVertex(tv.b).x + t.GetVertex(tv.c).x) / 3;
                Func<int, int> map = (v) => cx > xm && twin.ContainsKey(v) ? twin[v] : v;
                Assert.True(m.AppendTriangle(map(tv.a), map(tv.b), map(tv.c)) >= 0);
            }
            return m;
        }

        // a region that fails in every build, on overlays whose composite is rebuilt (catch points held after an
        // overlap; the toe crossing pit in "rotated, scale 1e-3, utm"): no design of the terrain tests or the benchmark rebuilds after a failed region, so the composite is
        // built directly, on the overlay of a design, with a check that always fails and with none. The builds, the
        // result, the region and the replaced triangles are the same; only the copy of the rest is skipped
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RebuildWithAFailingRegion_SameDecisions(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = TerrainSlopeToeCrossingTests.Make(0.07, 0.123, 2.1, false, s);
            d.Compute();
            Assert.NotNull(d.lastOverlay);
            var plain = new SlopeComposite(d.lastOverlay, d.Pieces.Count, d.UsedMinFeatureSize, null);
            var failing = new SlopeComposite(d.lastOverlay, d.Pieces.Count, d.UsedMinFeatureSize, null) { RegionCheck = r => false };
            bool a = plain.Build(), b = failing.Build();
            Assert.Equal(a, b);
            Assert.Equal(plain.Builds, failing.Builds);
            Assert.Equal(plain.Problem, failing.Problem);
            Assert.Equal(plain.Builds, failing.RegionFailures);
            Assert.True(failing.RegionFailed && failing.CopiedTerrain == false);
            Assert.Equal("", Invariance.Difference(plain.Region, failing.Region));
            Assert.Equal(plain.Replaced, failing.Replaced);
            Assert.True(failing.Composite.TriangleCount < plain.Composite.TriangleCount);
            // (the frame whose composite is rebuilt: 4 builds)
            if (name == "rotated, scale 1e-3, utm") Assert.True(plain.Builds > 1, plain.Builds.ToString());
        }

        [Fact]
        public void Reuse()
        {
            var d = Make(Similarity.Identity, new Vector3d(0, 0, -340), true);
            Assert.True(d.Compute(), d.Message);
            var c1 = d.Composite;
            int skipped = d.SkippedTerrainCopies;
            Assert.True(d.Compute(), d.Message);
            Assert.Equal(skipped, d.SkippedTerrainCopies);
            Assert.Equal("", Invariance.Difference(c1, d.Composite));
        }
    }
}
