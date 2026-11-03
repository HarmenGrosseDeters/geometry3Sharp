using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The parts of <see cref="QualityMesh2CollapsedPartsTests"/> that use the change's own members (the oracle's O-H5 counts, the
    /// mesher's diagnostics), apart so that the cases themselves compile against the library without the change. The library change is
    /// the diagnostics only: of these tests, the mesher's fail on the library without it
    /// </summary>
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
