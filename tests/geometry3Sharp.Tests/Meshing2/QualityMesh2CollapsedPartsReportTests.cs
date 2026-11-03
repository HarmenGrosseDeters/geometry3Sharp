using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    [CollectionDefinition("Meshing2OracleTiming", DisableParallelization = true)]
    public class Meshing2OracleTimingCollection { }

    /// <summary>
    /// The parts of <see cref="QualityMesh2CollapsedPartsTests"/> that use the change's own members (the oracle's O-H5 counts, the
    /// mesher's diagnostics), apart so that the cases themselves compile against the library without the change. The library change is
    /// the diagnostics only: of these tests, the mesher's fail on the library without it
    /// </summary>
    // The oracle's wall-clock budget must not include CPU contention from other test collections.
    [Collection("Meshing2OracleTiming")]
    public class QualityMesh2CollapsedPartsReportTests
    {
        /// <summary>
        /// the mesher reports what snapping collapsed (the zigzag's teeth); what refinement pinches is reported on a line of its own,
        /// which depends on where refinement puts its points and is not asked for here
        /// </summary>
        [Fact]
        public void Mesher_ReportsWhatSnappingCollapsed()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(QualityMesh2CollapsedPartsTests.Zigzag(0, 20));
            var r = new QualityMesh2 { MinAngle = 20, SnapTolerance = 0.01, SteinerLimit = 50000, Conforming = true }.Compute(c);
            var lines = r.Diagnostics ?? new string[0];
            Assert.Contains(lines, l => l.StartsWith("snap grid: ") && l.Contains(" input segments with pieces on parts that snapping collapsed"));
        }

        /// <summary>
        /// a 20 square with a corridor 0.25 wide from x 20 to 20 + length, with a ring vertex every pixel along both walls; points and
        /// segments optional (a free point in the corridor, a segment along its middle)
        /// </summary>
        internal static Mesh2Input Corridor(int length, bool freePoint, bool middle)
        {
            var x = new List<double> { 0, 20 }; var y = new List<double> { 0, 0 };
            for (int i = 0; i <= length; ++i) { x.Add(20 + i); y.Add(9.875); }
            for (int i = length; i >= 0; --i) { x.Add(20 + i); y.Add(10.125); }
            x.Add(20); y.Add(20); x.Add(0); y.Add(20);
            var input = new Mesh2Input { SnapTolerance = 1 };
            input.Rings.Add(new RingInput(Enumerable.Range(0, x.Count).ToArray(), false));
            if (freePoint) { x.Add(20 + length / 2 + 0.3); y.Add(10.05); }
            if (middle) { x.Add(10); y.Add(10); x.Add(20 + length); y.Add(10); input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1)); }
            input.X = x.ToArray(); input.Y = y.ToArray();
            return input;
        }

        /// <summary>
        /// review 3, S1: what snapping collapses is accepted from the mesher's own output: a free point in a collapsed corridor, a
        /// segment from the square through it (its chain in the square, none in the corridor)
        /// </summary>
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void Oracle_AcceptsWhatSnappingCollapsed(bool freePoint, bool middle)
        {
            var input = Corridor(30, freePoint, middle);
            var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
            Assert.True(report.CollapsedPoints > 0, report.ExcusedSummary());
            if (middle) Assert.True(report.CollapsedSegments > 0, report.ExcusedSummary());
        }

        /// <summary>
        /// Snapped alone (M2), SRR's restore step (D14) adds hot pixels of its own, part of the rounding of the input, not refinement: they
        /// must not count against rule (ii'). Post-review B2: a star polygon (2.8, 11.4), (7.2, 6.4), (6.2, 3.5), (8.6, 6.9) with the free
        /// points (2.6, 9.6) and (9.6, 6.5); the output edge (3, 11) - (7, 6) crosses the free point's pixel (3, 10), the restore step makes
        /// (4, 10) hot, and both spike edges route through it. Rejected (VertexUnused for input 0) by rule (ii') when the input-only
        /// state lacked the restore pixels; accepted with the oracle's own restore step, which finds (4, 10) itself
        /// </summary>
        [Fact]
        public void Oracle_AcceptsTheRestoreStepOfSnapping()
        {
            var input = new Mesh2Input { X = new[] { 2.8, 7.2, 6.2, 8.6, 2.6, 9.6 }, Y = new[] { 11.4, 6.4, 3.5, 6.9, 9.6, 6.5 }, SnapTolerance = 1 };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            Assert.Equal(input.Count + 1, o.VX.Length);   // the restore pixel
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        /// <summary>
        /// the 18 inputs of the post-review's M2 fuzz (star polygons in [0, 16]² at 0.1, pixel 1, 0 to 2 free points, sometimes one
        /// segment; 3000 seeds) that rule (ii') rejected without the restore pixels: each with the last 0, 1 or 2 points free, accepted
        /// snapped alone. Small already (5 to 10 points), taken as found
        /// </summary>
        [Theory]
        [InlineData(688, "2.8,7.2,6.2,8.6,2.6,9.6", "11.4,6.4,3.5,6.9,9.6,6.5", "")]
        [InlineData(696, "13.7,11.7,7.9,1.4,8.3,1.6,2.6", "9.3,11.2,9.2,10.6,7,9.1,11.2", "")]
        [InlineData(812, "12.9,10.2,6,2,7,9.1,8.6,9.4,10.5", "10.4,11.6,10.8,10.6,1,5.1,6.5,6.6,11.6", "")]
        [InlineData(940, "14.8,14.2,10.6,10.2,7.4,2.2,7.2,11.1,2,11.4", "9,9,9.4,14.1,12.1,3.6,7.2,1.1,4.6,7.4", "")]
        [InlineData(972, "9.1,10.2,13.2,9.3,5.5,14.4", "8.1,8.3,9.7,11.1,9.4,7.5", "0-2")]
        [InlineData(1013, "14,6.4,5.3,7.6,8.6,8.2,10.2,11", "10,12.5,6.9,7.4,4,7.1,3.2,3.5", "4-3")]
        [InlineData(1190, "11.1,6,6.8,7.4,4.1,13.5,15.2", "10.1,8.3,8,7.8,6.4,7.5,3.4", "4-5")]
        [InlineData(1334, "8.1,8.1,7.7,4.2,2.6,5,5.8", "8.7,9.9,9.2,14.1,8.4,5.4,2.6", "")]
        [InlineData(1773, "9,11.8,6,6.1,5.5,8.7,10.7,9.7", "8.3,9.9,12.9,11.5,1.7,7.6,7.7,7.9", "")]
        [InlineData(2028, "6.2,2.2,10.8,8.6,10.2,12.5,8.4", "12.2,11.2,1.9,6.8,5.6,8.8,8.9", "1-2")]
        [InlineData(2061, "5.9,2.6,5.6,2.8,5.8,7.2,10,3.6", "10.8,12.9,8.6,6.2,4.4,4.2,2.7,13", "2-3")]
        [InlineData(2124, "12.5,8.4,2.9,7.2,9.1,12.5,11.6", "9.1,8.6,10.9,4,7.3,7.9,8.2", "")]
        [InlineData(2312, "10.9,8.7,14.2,9.5,9.5,8.3", "8.2,8.2,9.6,12.7,4.4,7.4", "5-4")]
        [InlineData(2503, "8.1,7.7,5.4,8.8,10.6,5.2", "11.3,9.7,12.4,5.7,6.7,10.7", "1-0")]
        [InlineData(2530, "10.8,10,6,5.9,2.7,1.8,14.6,5.9", "12.8,15.2,8.5,8.1,7.3,5.2,1.1,9.5", "")]
        [InlineData(2574, "13.4,9,6.2,12.9,8.8,9.5,11.4", "10.8,11.4,11.2,6.8,7.8,7.7,8.8", "")]
        [InlineData(2715, "9.8,4.9,3.8,14.7,8.2", "8.7,9,7.5,7.6,1.7", "3-1")]
        [InlineData(2772, "11.8,11.2,10.2,8.4,4.8,7.6,9.8,9.1,11.5", "8.1,10.7,12.3,10.3,11.7,5.3,7,7.4,10.3", "7-3")]
        public void Oracle_AcceptsTheRestoreStepOfSnapping_Fuzz(int seed, string xs, string ys, string segment)
        {
            var x = xs.Split(',').Select(t => double.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var y = ys.Split(',').Select(t => double.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            for (int free = 0; free <= 2; ++free) {
                var input = new Mesh2Input { X = x, Y = y, SnapTolerance = 1 };
                input.Rings.Add(new RingInput(Enumerable.Range(0, x.Length - free).ToArray(), false));
                if (segment.Length > 0) { var s = segment.Split('-').Select(int.Parse).ToArray(); input.Segments.Add(new SegmentInput(s[0], s[1])); }
                var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
                Assert.Equal(Mesh2Status.Success, o.Status);
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2 });
                Assert.True(report.Ok, "seed " + seed + ", " + free + " free: " + report);
            }
        }

        /// <summary>
        /// With refinement (M3), where SRR's restore step (D14) reroutes the input, the collapse belongs to the rounding of the input, not
        /// to refinement: the oracle's own restore step puts its pixels into the input-only state of rule (ii'). Of the 3000 seeds of
        /// <see cref="StarPolygonFuzz"/>, these 10 were rejected without it (pinned as known false rejections in #358 and #360: the
        /// segment pieces and input points in brackets were reported missing and unused) and are accepted now, those pieces and points
        /// excused on collapsed parts
        /// </summary>
        [Theory]
        [InlineData(62, 1, 0)]
        [InlineData(161, 1, 1)]
        [InlineData(787, 0, 1)]
        [InlineData(818, 0, 1)]
        [InlineData(963, 0, 1)]
        [InlineData(1455, 1, 0)]
        [InlineData(2396, 1, 0)]
        [InlineData(2471, 1, 0)]
        [InlineData(2459, 0, 1)]
        [InlineData(2923, 0, 1)]
        public void Oracle_AcceptsTheRestoreStepAtM3(int seed, int segmentsMissingBefore, int verticesUnusedBefore)
        {
            var input = StarPolygonFuzz.Make(seed);
            var o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, "seed " + seed + ": " + report);
            Assert.NotEmpty(report.RestorePixels);
            Assert.True(report.CollapsedSegments >= segmentsMissingBefore && report.CollapsedPoints >= verticesUnusedBefore, "seed " + seed + ": " + report.ExcusedSummary());
        }

        /// <summary>
        /// the refinement of before the fix (the mesher's rule that keeps the input image's chains apart off): its outputs reroute
        /// collapsed parts with output vertices, which the oracle's tests of rule (ii') and of the count (iii) need; with the rule on,
        /// refinement no longer puts a vertex where two chains of the input's image pass apart
        /// </summary>
        static Mesh2Output WithoutTheChainRule(Mesh2Input input)
        {
            QualityMesh2.Refiner.KeepChainsApartForTests = false;
            try { return new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.KeepChainsApartForTests = null; }
        }

        /// <summary>
        /// (iii) for points: seed 1083 of <see cref="StarPolygonFuzz"/> at M3 is accepted with 2 segment pieces and 1 input point on
        /// collapsed parts, all 3 on stretches or at a pixel where output vertices rerouted the image (rule (ii'): inside parts the
        /// input's own rounding collapses, restore pixels included). Counts exact, so a change shows. (Until the oracle's own restore
        /// step, a star whose spike tip only a restore pixel keeps stood here: the refinement before the chain rule dropped that tip,
        /// and the oracle, without the restore pixel, counted it as collapsed and rerouted; it now rejects that mesh.)
        /// </summary>
        [Fact]
        public void Oracle_CountsExcusesReroutedByOutputVertices()
        {
            var input = StarPolygonFuzz.Make(1083);
            var o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(2, report.CollapsedSegments);
            Assert.Equal(1, report.CollapsedPoints);
            Assert.Equal(3, report.CollapsedViaRefinement);
        }

        /// <summary>
        /// the refinement before the chain rule dropped a spike tip that only SRR's restore pixel (16, 11) keeps in the input's rounding:
        /// rejected now (VertexUnused). The mesher with the rule keeps it (accepted, with one input point on a collapsed part)
        /// </summary>
        [Fact]
        public void Oracle_RestorePixelKeepsASpikeTip_RefinementWithoutTheChainRuleIsRejected()
        {
            var x = new[] { 19.5, 18.4, 17.5, 11, 13.5, 3.1, 14.8, 10.8, 11.1, 16.3, 25.1 };
            var y = new[] { 16.3, 19, 28.2, 23.8, 16.5, 10.3, 12.3, -13.5, -13.6, 14.9, 10 };
            var input = new Mesh2Input { X = x, Y = y, SnapTolerance = 1, MinAngleDeg = 20, SteinerLimit = 5000 };
            input.Rings.Add(new RingInput(Enumerable.Range(0, x.Length).ToArray(), false));
            var before = Oracle.Check(input, WithoutTheChainRule(input), new OracleExpect { Milestone = Milestone.M3 });
            Assert.Equal(new[] { Violation.VertexUnused }, before.Violations.Select(v => v.Kind).ToArray());
            Assert.Equal(new[] { ((System.Numerics.BigInteger)16, (System.Numerics.BigInteger)11) }, before.RestorePixels.ToArray());
            var now = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(now.Ok, now.ToString());
            Assert.Equal(1, now.CollapsedPoints);
        }

        /// <summary>
        /// (iii) counts only pieces that need a rerouted stretch: the star (11, 9.3), (8.4, 14.4), (12.1, 6.9), (11.3, 7.6) at M3 (20°, the
        /// refinement of before the fix)
        /// has a full image of one edge rerouted near (8, 14) and an input-only image (8, 14), (11, 9), (11, 8), (12, 7); its excused
        /// piece is crossed on the identical links (11, 9) - (11, 8) - (12, 7) alone, so it is not counted (post-review of (ii'), S3: the
        /// count was 1 when any stretch of the image differed)
        /// </summary>
        [Fact]
        public void Oracle_DoesNotCountExcusesOnStretchesNotRerouted()
        {
            var input = new Mesh2Input { X = new[] { 11, 8.4, 12.1, 11.3 }, Y = new[] { 9.3, 14.4, 6.9, 7.6 }, SnapTolerance = 1, MinAngleDeg = 20, SteinerLimit = 5000 };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            var o = WithoutTheChainRule(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(1, report.CollapsedSegments);
            Assert.Equal(1, report.CollapsedPoints);
            Assert.Equal(0, report.CollapsedViaRefinement);
        }

        /// <summary>
        /// (iii) for segment pieces: seed 1742 of <see cref="StarPolygonFuzz"/> at M3 (the same mesh with and without the mesher's rule that
        /// keeps chains apart) is accepted with 2 segment pieces and 2 points on
        /// collapsed parts, and 3 excuses that need a stretch output vertices rerouted, so at least one segment piece's (a piece is counted
        /// only when its chain search fails without the rerouted stretches; post-review of (ii'), S2 and S3). Correct by the measurement
        /// with the restore pixels too. Counts exact, so a change shows
        /// </summary>
        [Fact]
        public void Oracle_CountsSegmentExcusesReroutedByOutputVertices()
        {
            var input = StarPolygonFuzz.Make(1742);
            var o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(2, report.CollapsedSegments);
            Assert.Equal(2, report.CollapsedPoints);
            Assert.Equal(3, report.CollapsedViaRefinement);
        }

        /// <summary>
        /// cost (review 3, S2: 82 s with the distance rule): 4006 ring vertices on a collapsed corridor, all unused and excused; the images
        /// are built once and each pixel's links are looked up, not scanned. Bound: a generous multiple of the measured time
        /// </summary>
        [Fact]
        public void Oracle_ManyCollapsedPointsAreCheap()
        {
            var input = Corridor(2000, false, false);
            Assert.Equal(4006, input.Count);
            var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2 });
            watch.Stop();
            Assert.True(report.Ok, report.ToString());
            Assert.True(report.CollapsedPoints >= 3999, report.ExcusedSummary());
            Assert.True(watch.Elapsed.TotalSeconds < 30, "oracle took " + watch.Elapsed.TotalSeconds + " s");
        }

        /// <summary>
        /// a free segment with a piece outside the domain, or inside a hole, has no triangle on either side there but collapsed onto
        /// nothing: not reported (review 3)
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Mesher_DoesNotReportSegmentsOutsideTheDomain(bool inHole)
        {
            var input = new Mesh2Input { SnapTolerance = 1, MinAngleDeg = 20, SteinerLimit = 50000 };
            if (inHole) {
                input.X = new double[] { 0, 40, 40, 0, 10, 30, 30, 10, 15, 25 }; input.Y = new double[] { 0, 0, 40, 40, 10, 10, 30, 30, 20, 20 };
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true));
            } else {
                input.X = new double[] { 0, 20, 20, 0, 10, 30 }; input.Y = new double[] { 0, 0, 20, 20, 10, 10 };
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            }
            input.Segments.Add(new SegmentInput(input.Count - 2, input.Count - 1));
            var c = ConstrainedDelaunayMesh2Candidate.Convert(input);
            var r = new QualityMesh2 { MinAngle = 20, SnapTolerance = 1, SteinerLimit = 50000, Conforming = true }.Compute(c);
            var lines = r.Diagnostics ?? new string[0];
            Assert.DoesNotContain(lines, l => l.Contains("parts that snapping collapsed") || l.Contains("parts that refinement pinched"));
        }
    }
}
