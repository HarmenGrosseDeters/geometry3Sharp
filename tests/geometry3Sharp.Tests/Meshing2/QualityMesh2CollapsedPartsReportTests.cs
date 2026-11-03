using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The parts of <see cref="QualityMesh2CollapsedPartsTests"/> that use the change's own members (the oracle's O-H5 counts, the
    /// mesher's diagnostics), apart so that the cases themselves compile against the library without the change and fail there
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

        /// <summary> the mesher reports what snapping collapsed and what refinement pinched, apart </summary>
        [Fact]
        public void Mesher_ReportsSnapCollapseAndRefinementPinchApart()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(QualityMesh2CollapsedPartsTests.Zigzag(0, 20));
            var r = new QualityMesh2 { MinAngle = 20, SnapTolerance = 0.01, SteinerLimit = 50000, Conforming = true }.Compute(c);
            var lines = r.Diagnostics ?? new string[0];
            Assert.Contains(lines, l => l.Contains("input segments with pieces on parts that snapping collapsed"));
            Assert.Contains(lines, l => l.Contains("input segments with pieces on parts that refinement pinched"));
        }
    }
}
