using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A hull domain that is a sliver (a hair wide, 1.7 long) with a tiny cluster at one end: the mesher refines the cluster and drops the
    /// tail, so the far input point is in no triangle and its segments have no chain. The tail is within the tube of nothing, but the
    /// whole domain's twice area is below tube times perimeter, the bound under which a domain may vanish altogether; the oracle excuses a
    /// part under that bound (found by the MeshFuzzPilot, reported as VertexUnused / SegmentMissing with status Success)
    /// </summary>
    public class QualityMesh2VanishingPartTests
    {
        static readonly double[] X = { -9.04844107523677E-87, -1.7763568394002505E-13, 4.689582056016661E-13, -1.7304660291117036, -3.197442310920451E-13, 4.547473508864641E-13, 8.171241461241152E-13, 9.094947017729282E-13 };
        static readonly double[] Y = { 5.780948464734603E-86, 7.105427357601002E-14, -4.547473508864641E-13, -1.4127834472400946E-104, -4.902744876744691E-13, 5.780948464734603E-86, -4.973799150320701E-13, 3.126388037344441E-13 };
        static readonly int[][] Segments = { new[] { 0, 2 }, new[] { 1, 2 }, new[] { 0, 1 }, new[] { 1, 3 }, new[] { 0, 3 }, new[] { 2, 3 }, new[] { 4, 6 }, new[] { 5, 6 }, new[] { 4, 5 }, new[] { 5, 7 }, new[] { 4, 7 }, new[] { 6, 7 } };

        static Mesh2Input Sliver(bool constrainedQuality)
        {
            var input = new Mesh2Input { X = (double[])X.Clone(), Y = (double[])Y.Clone(), MinAngleDeg = 20, ConstrainedQuality = constrainedQuality, SteinerLimit = 1500 };
            foreach (var s in Segments) input.Segments.Add(new SegmentInput(s[0], s[1]));
            return input;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Oracle_AcceptsTheDroppedTailOfASliverDomain(bool constrainedQuality)
        {
            var input = Sliver(constrainedQuality);
            var output = new QualityMesh2Candidate().Run(input);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report + " / " + report.ExcusedSummary());
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(output.Triangles.Length > 0);
            // (the tail's far point is the one dropped: the oracle reports what it excused)
            Assert.True(report.VanishedPoints + report.VanishedSegments > 0 || output.InputToVertex.All(m => m >= 0 && output.Triangles.Any(t => t.Contains(m))), report.ExcusedSummary());
        }

        /// <summary> scaled by powers of two, the verdict is the same (the tube scales with the input) </summary>
        [Fact]
        public void Oracle_VerdictOnTheSliverIsScaleInvariant()
        {
            foreach (int k in new[] { -20, 0, 20 }) {
                double f = System.Math.Pow(2, k);
                var input = Sliver(true);
                for (int i = 0; i < input.Count; ++i) { input.X[i] *= f; input.Y[i] *= f; }
                var output = new QualityMesh2Candidate().Run(input);
                var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
                Assert.True(report.Ok, "scale 2^" + k + ": " + report);
            }
        }
    }
}
