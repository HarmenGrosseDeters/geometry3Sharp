using System.Collections.Generic;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The refinement on a snap grid keeps what the rounding of the input keeps (owner decision 2026-11-03 on Q17): a split never
    /// reroutes a piece through a vertex of refinement onto a constraint of another chain. Every vertex makes its pixel hot, and a grid
    /// split point that is a vertex already reroutes the piece through it; when a new piece then runs along the other chain's edge, the
    /// two chains coincide and the part between them collapses (a spike's tip, a band narrower than a pixel). Before the fix, 188 of
    /// the 3000 seeds of <see cref="StarPolygonFuzz"/> lost area that way (the oracle's rule (ii')). These cases compile against the
    /// library without the fix, and fail there
    /// </summary>
    public class QualityMesh2KeepsChainsApartTests
    {
        /// <summary>
        /// a spike tip both of whose edges pass a pixel that a grid point of refinement makes hot: kept (the second chain is not
        /// rerouted onto the first through it). Seeds of
        /// <see cref="StarPolygonFuzz"/>, each reduced to a single thin triangle (input points dropped while the pinch stayed)
        /// </summary>
        [Theory]
        [InlineData(393, "15.9,3.1,6.5", "8,9.9,6.2")]
        [InlineData(598, "12.1,9.4,8.5", "13.6,11.9,1.5")]
        [InlineData(373, "14.6,9.2,6.6", "9.3,9.2,7.4")]
        [InlineData(707, "3.2,6.4,15.5", "11.3,9.1,5.7")]
        [InlineData(2179, "6.5,11.2,14.5", "5.9,4.3,7.7")]
        public void RefinementKeepsASpikeTip(int seed, string xs, string ys)
        {
            var input = new Mesh2Input { X = Doubles(xs), Y = Doubles(ys), SnapTolerance = 1, MinAngleDeg = 20, SteinerLimit = 5000 };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
            var o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, "seed " + seed + ": " + report);
        }

        static double[] Doubles(string list) => list.Split(',').Select(t => double.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        /// <summary>
        /// the inputs <see cref="StarPolygonFuzz"/> seeds 0 to 2999 at M3 that the oracle rejects, by class; every other seed is VALID
        /// (all 188 refinement pinches among them). Restore: SRR's restore pixels, which the oracle's input-only state does not compute yet
        /// (interim (c)): accepted once they join it (measured outside the oracle); pinned with their counts in
        /// QualityMesh2CollapsedPartsReportTests until the oracle's own restore step. The others fail with or without rule (ii'): free
        /// points outside the ring meshed (TriangleOutsideDomain), a segment to such a point (SegmentMissing), a chain past a hot pixel
        /// (ChainPassesHotPixel), an input point or a chain dropped that the oracle does not excuse even without rule (ii'); findings of
        /// their own, as they were before the fix
        /// </summary>
        static readonly Dictionary<int, string> Rejected = new Dictionary<int, string> {
            // restore (10)
            [62] = "SegmentMissing", [161] = "VertexUnused,SegmentMissing", [787] = "VertexUnused", [818] = "VertexUnused",
            [963] = "VertexUnused", [1455] = "SegmentMissing", [2396] = "SegmentMissing", [2459] = "VertexUnused",
            [2471] = "SegmentMissing", [2923] = "VertexUnused",
            // free points outside the ring (7, two with a chain past a hot pixel as well)
            [37] = "TriangleOutsideDomain,SubsegmentUnsourced", [41] = "TriangleOutsideDomain,AreaMismatch,SubsegmentUnsourced",
            [1019] = "TriangleOutsideDomain,AreaMismatch,SubsegmentUnsourced", [1052] = "TriangleOutsideDomain,SubsegmentUnsourced",
            [1331] = "TriangleOutsideDomain,SubsegmentUnsourced", [2200] = "TriangleOutsideDomain,ChainPassesHotPixel,AreaMismatch,SubsegmentUnsourced",
            [2248] = "TriangleOutsideDomain,ChainPassesHotPixel,SubsegmentUnsourced",
            // a segment to a free point outside the ring
            [709] = "SegmentMissing",
            // a chain past a hot pixel
            [2022] = "ChainPassesHotPixel",
            // an input point or a chain dropped with or without rule (ii') (the oracle's full image does not excuse them either)
            [56] = "VertexUnused", [1013] = "VertexUnused,SegmentMissing", [2504] = "VertexUnused", [2858] = "VertexUnused",
        };

        [Fact]
        public void StarPolygonFuzz_VerdictsByClass()
        {
            var wrong = new List<string>();
            for (int seed = 0; seed < 3000; ++seed) {
                var input = StarPolygonFuzz.Make(seed);
                var o = new QualityMesh2Candidate().Run(input);
                if (o.Status != Mesh2Status.Success) { wrong.Add(seed + ": status " + o.Status); continue; }
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                string kinds = string.Join(",", report.Violations.Select(v => v.Kind).Distinct());
                string expected = Rejected.TryGetValue(seed, out var k) ? k : "";
                if (kinds != expected) wrong.Add(seed + ": expected [" + expected + "], got " + (report.Ok ? "ok" : report.ToString()));
            }
            Assert.True(wrong.Count == 0, wrong.Count + " seeds differ:\n" + string.Join("\n", wrong.Take(20)));
        }
    }
}
