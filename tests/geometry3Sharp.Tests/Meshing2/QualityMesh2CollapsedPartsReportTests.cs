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
        [Fact]
        public void Oracle_CountsWhatItAcceptsOnCollapsedParts()
        {
            var input = QualityMesh2CollapsedPartsTests.Zigzag(0, 20);
            var o = new QualityMesh2Candidate().Run(input);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.True(report.CollapsedSegments > 0, report.ExcusedSummary());
            Assert.True(report.CollapsedPoints > 0, report.ExcusedSummary());
        }

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
