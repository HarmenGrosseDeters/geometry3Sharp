using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Grading of <see cref="QualityMesh2"/> with an angle bound: bad triangles are split worst first, and a triangle bad by its angle at
    /// the best of a few points on the bisector of its shortest edge (MESHING2D_GRADING.md). These tests use only the API of before the
    /// change: on it they fail (more triangles), so they show the change. The switches, the floor, invariance and determinism are in
    /// <see cref="QualityMesh2GradingLibraryTests"/>.
    /// </summary>
    public class QualityMesh2GradingTests
    {
        /// <summary>
        /// the unit square with a cluster of three points 2^-20 apart at its centre (the shape of the reference case f-11/a): the mesh
        /// grades from the cluster out to the square over 20 octaves. Before the change 256 triangles (default) and 286 (constrained
        /// quality); with it 217 and 222; the reference mesher 108
        /// </summary>
        internal static Mesh2Input Cluster(bool constrainedQuality)
        {
            double h = Math.Pow(2, -20);
            var input = new Mesh2Input {
                X = new[] { 0, 1, 1, 0, 0.5, 0.5 + h, 0.5 },
                Y = new[] { 0, 0, 1, 1, 0.5, 0.5, 0.5 + h },
                MinAngleDeg = 20, ConstrainedQuality = constrainedQuality,
            };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            return input;
        }

        [Theory]
        [InlineData(false, 235)]
        [InlineData(true, 235)]
        public void Cluster_GradesWithFewerTriangles(bool constrainedQuality, int most)
        {
            var input = Cluster(constrainedQuality);
            var o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.True(o.Triangles.Length <= most, o.Triangles.Length + " triangles, at most " + most + " expected");
        }

        /// <summary>
        /// the lens tests' fuzz (a square with a hole and random segments) at 25 and 30 degrees, both modes: every mesh passes the oracle,
        /// and the triangles summed over the seeds stay below the bound (the sums before and with the change next to each bound)
        /// </summary>
        [Theory]
        [InlineData(25.0, false, 7300)]     // before 7610, with the change 7126
        [InlineData(30.0, false, 16000)]    // before 18113, with it 14951
        [InlineData(25.0, true, 5150)]      // before 5288, with it 5053
        [InlineData(30.0, true, 15700)]     // before 17794, with it 14690
        public void Fuzz_FewerTriangles(double angle, bool constrainedQuality, int most)
        {
            long total = 0;
            for (int seed = 0; seed < 24; ++seed) {
                var input = QualityMesh2LensDeletionTests.Fuzz(5000 + seed, angle);
                input.ConstrainedQuality = constrainedQuality;
                var o = new QualityMesh2Candidate().Run(input);
                Assert.Equal(Mesh2Status.Success, o.Status);
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                Assert.True(report.Ok, "seed " + seed + ": " + report);
                total += o.Triangles.Length;
            }
            Assert.True(total <= most, total + " triangles, at most " + most + " expected");
        }
    }
}
