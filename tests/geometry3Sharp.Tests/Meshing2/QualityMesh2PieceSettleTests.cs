using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Inserting the pieces of a split subsegment settles: a vertex within the tolerance of a piece splits it only when both parts are
    /// strictly shorter. Without that, refinement in the constrained-quality mode at high angle bounds (a cascade at a small angle between
    /// a hull edge and a ring, down to pieces about as short as the tolerance) failed with NodingLimit, "a segment split did not settle":
    /// two vertices within the tolerance of each other's piece split it at each other in turn (found with ext/cdt/island at 32 and 34
    /// degrees, a public input; reproduced here with procedural jagged rings).
    /// </summary>
    public class QualityMesh2PieceSettleTests
    {
        /// <summary> a closed polyline of n points at random radii around a circle, coordinates rounded to 8 decimals, as segments
        /// (no ring: the domain is the convex hull, whose edges meet the polyline at small angles) </summary>
        internal static Mesh2Input JaggedRing(int seed, int n, double angle)
        {
            var rnd = new Random(seed);
            var xy = new double[2 * n];
            for (int i = 0; i < n; ++i) {
                double t = 2 * Math.PI * i / n, r = 0.5 + 0.35 * rnd.NextDouble();
                xy[2 * i] = Math.Round(0.5 + r * Math.Cos(t), 8); xy[2 * i + 1] = Math.Round(0.5 + r * Math.Sin(t), 8);
            }
            var input = Generators.Literal(xy);
            for (int i = 0; i < n; ++i) input.Segments.Add(new SegmentInput(i, (i + 1) % n));
            input.MinAngleDeg = angle;
            input.ConstrainedQuality = true;
            return input;
        }

        public static IEnumerable<object[]> Seeds() => SettleSeeds.Select(s => new object[] { s });
        // (seeds that failed with NodingLimit at 34 degrees before the fix, out of the first 200)
        internal static readonly int[] SettleSeeds = { 60, 130, 188 };

        [Theory]
        [MemberData(nameof(Seeds))]
        public void JaggedRing_AtHighBounds_Settles(int seed)
        {
            var input = JaggedRing(seed, 40, 34);
            var output = new QualityMesh2Candidate().Run(input);
            Assert.True(output.Status == Mesh2Status.Success, "seed " + seed + ": " + output.Status + " " + output.FailureKind);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, "seed " + seed + ": " + report);
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(Similarities))]
        public void JaggedRing_Settles_UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var input = JaggedRing(60, 40, 34);
            for (int i = 0; i < input.Count; ++i) { var q = s.Apply(new Vector3d(input.X[i], input.Y[i], 0)); input.X[i] = q.x; input.Y[i] = q.y; }
            var output = new QualityMesh2Candidate().Run(input);
            Assert.True(output.Status == Mesh2Status.Success, name + ": " + output.Status + " " + output.FailureKind);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, name + ": " + report);
        }
    }
}
