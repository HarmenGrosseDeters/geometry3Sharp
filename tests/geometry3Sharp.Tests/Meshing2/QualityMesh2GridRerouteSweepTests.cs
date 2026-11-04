using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// the parts of <see cref="QualityMesh2GridRerouteTests"/> that use the fix's own members (the sweep and the reroute counter), apart
    /// so that the cases themselves compile against the library without the fix and fail there
    /// </summary>
    public class QualityMesh2GridRerouteSweepTests
    {
        /// <summary>
        /// the reduced cases do take the path of a split onto a vertex already there, with the tag check after every operation (case 30 no
        /// longer does: its target was a vertex of a segment piece inside the hole, which the carve before refinement removes, T15; case
        /// 292 replaced it; 31 and 292 no longer do since a grid split may not reroute a piece onto another chain's edge, Q17: cases 60
        /// and 2384 replace them). Since the pinch rule (a split point is never another chain's vertex of refinement) 60 and 2384 do not
        /// reroute either: their target was such a vertex, a pinch. No reroute was left in 6000 random segments of this kind with the
        /// rule, so the path is checked with the pinch rule off; with it on these cases do not reroute
        /// </summary>
        [Theory]
        [InlineData(60, 20.0)]
        [InlineData(2384, 0.0)]
        public void SplitOntoAVertexAlreadyThere_TakesTheReroutePath(int which, double angle)
        {
            QualityMesh2.Refiner.ReroutesForTests = 0;
            QualityMesh2GridRerouteTests.RunChecked(QualityMesh2GridRerouteTests.Case(which, angle));
            Assert.Equal(0, QualityMesh2.Refiner.ReroutesForTests);
            QualityMesh2.Refiner.KeepPinchFreeForTests = false;
            try { QualityMesh2GridRerouteTests.RunChecked(QualityMesh2GridRerouteTests.Case(which, angle)); }
            finally { QualityMesh2.Refiner.KeepPinchFreeForTests = null; }
            Assert.True(QualityMesh2.Refiner.ReroutesForTests > 0, "the case no longer reroutes a piece through a vertex already there");
        }

        /// <summary>
        /// the behaviour change of Q17's mesher bugfix (no grid split reroutes a piece through a vertex of refinement onto another chain's
        /// edge): cases 31 and 292 took the reroute path onto another chain before; now that reroute is refused and the mesh is still
        /// VALID (the old refinement, with the rule off, reroutes more)
        /// </summary>
        [Theory]
        [InlineData(31, 0.0)]
        [InlineData(31, 20.0)]
        [InlineData(292, 20.0)]
        public void SplitOntoAnotherChain_IsRefused(int which, double angle)
        {
            var input = QualityMesh2GridRerouteTests.Case(which, angle);
            QualityMesh2.Refiner.ReroutesForTests = 0;
            var o = QualityMesh2GridRerouteTests.RunChecked(input);
            long withRule = QualityMesh2.Refiner.ReroutesForTests;
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            QualityMesh2.Refiner.KeepChainsApartForTests = false;
            QualityMesh2.Refiner.ReroutesForTests = 0;
            try { QualityMesh2GridRerouteTests.RunChecked(input); }
            finally { QualityMesh2.Refiner.KeepChainsApartForTests = null; }
            Assert.True(QualityMesh2.Refiner.ReroutesForTests > withRule, "the old refinement rerouted " + QualityMesh2.Refiner.ReroutesForTests + ", now " + withRule);
        }

        /// <summary>
        /// the behaviour change on a hull tip (Q17's mesher bugfix): hull fuzz seed 11 in the rotated frame had a hull corner of about 6
        /// degrees whose tip refinement collapsed by rerouting one hull chain onto the other through a grid point, reported as "vertices of
        /// the hull chains in no triangle"; now the tip stays, unreported, and the mesh is VALID
        /// </summary>
        [Fact]
        public void HullTipOfRefinement_IsKept()
        {
            var input = QualityMesh2HullGridTests.Moved(QualityMesh2HullGridTests.Fuzz(11), Similarity.ByName("rotated"));
            System.Collections.Generic.List<string> Diagnostics()
                => new QualityMesh2 { MinAngle = input.MinAngleDeg.Value, SnapTolerance = input.SnapTolerance, SteinerLimit = input.SteinerLimit, Conforming = true }
                    .Compute(ConstrainedDelaunayMesh2Candidate.Convert(input)).Diagnostics.ToList();
            Assert.DoesNotContain(Diagnostics(), d => d.Contains("vertices of the hull chains in no triangle"));
            var o = new Meshing2Acceptance.QualityMesh2Candidate().Run(input);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            QualityMesh2.Refiner.KeepChainsApartForTests = false;
            try { Assert.Contains(Diagnostics(), d => d.Contains("vertices of the hull chains in no triangle")); }
            finally { QualityMesh2.Refiner.KeepChainsApartForTests = null; }
        }

        /// <summary>
        /// the sweep on a kernel triangulation with a constrained chain from u to w: it marks every triangle between the line u w and
        /// the chain (centroid inside that polygon), and only triangles inside the chain's convex hull. Variant 1 has points inside the
        /// region (triangles with no corner on the chain), variant 2 a chain with a vertex off the triangle u q w (a piece split again
        /// at a nearby vertex), variant 3 a vertex on the line u w
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void MarkSwept_MarksTheRegionBetweenPieceAndChain(int variant)
        {
            // (the sweep alone, on a wide region: in refinement the region is thinner than a pixel, which changes nothing here)
            var chainPts = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(5, 3), new Vector2d(10, 0) };
            var pts = new List<Vector2d> { new Vector2d(5, -9), new Vector2d(5, 14), new Vector2d(19, 1), new Vector2d(-9, 1) };
            if (variant == 1) pts.AddRange(new[] { new Vector2d(3.5, 1), new Vector2d(5, 1.75), new Vector2d(6.5, 1), new Vector2d(5, 0.5) });
            if (variant == 2) chainPts.Insert(1, new Vector2d(2, 2));
            if (variant == 3) pts.Add(new Vector2d(4, 0));
            var all = chainPts.Concat(pts).ToList();
            var k = new DelaunayKernel2(all.Select(p => p.x).ToArray(), all.Select(p => p.y).ToArray(), Enumerable.Range(0, all.Count).ToArray());
            Assert.True(k.Build(System.Threading.CancellationToken.None));
            k.BeginConstraints();
            int Id(Vector2d p) => Enumerable.Range(0, all.Count).Single(v => k.X[v] == p.x && k.Y[v] == p.y);
            var chain = chainPts.Select(Id).ToList();
            var sources = new List<int> { 7 };
            for (int i = 0; i + 1 < chain.Count; ++i) {
                int h = k.FindEdge(chain[i], chain[i + 1]);
                Assert.True(h >= 0, "the chain edge " + i + " is an edge of the triangulation");
                k.SetConstraint(h, k.NewConstraint(new List<int>(sources)));
            }
            k.Dirty = new List<int>();
            Assert.True(QualityMesh2.Refiner.MarkSwept(k, chain[0], chain[chain.Count - 1], sources), "chain found");
            var marked = new HashSet<int>(k.Dirty);
            // the polygon between the line u w and the chain, and the chain's convex hull
            var polygon = chainPts;
            bool InPolygon(Vector2d c)
            {
                bool inside = false;
                for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                    if ((polygon[i].y > c.y) != (polygon[j].y > c.y) && c.x < (polygon[j].x - polygon[i].x) * (c.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x) inside = !inside;
                return inside;
            }
            int region = 0;
            for (int t = 0; t < k.TriangleSlots; ++t) {
                if (k.IsGhost(t)) continue;
                var c = (k.Point(k.V[3 * t]) + k.Point(k.V[3 * t + 1]) + k.Point(k.V[3 * t + 2])) / 3;
                if (InPolygon(c)) { ++region; Assert.Contains(t, marked); }
            }
            Assert.True(region > 0, "a region: " + string.Join(" ", Enumerable.Range(0, k.TriangleSlots).Where(t => !k.IsGhost(t)).Select(t => k.V[3 * t] + "," + k.V[3 * t + 1] + "," + k.V[3 * t + 2])));
            foreach (int t in marked)
                for (int j = 0; j < 3; ++j) {
                    var x = k.Point(k.V[3 * t + j]);
                    Assert.True(x.y >= 0 && x.y <= chainPts.Max(p => p.y) && x.x >= 0 && x.x <= 10, "marked triangle " + t + " reaches outside the hull");
                }
            if (variant == 1)
                Assert.Contains(marked, t => Enumerable.Range(0, 3).All(j => !chain.Contains(k.V[3 * t + j])));
            // a chain that does not carry the sources is not found: the caller then marks everything
            Assert.False(QualityMesh2.Refiner.MarkSwept(k, chain[0], chain[chain.Count - 1], new List<int> { 99 }));
        }
    }
}
