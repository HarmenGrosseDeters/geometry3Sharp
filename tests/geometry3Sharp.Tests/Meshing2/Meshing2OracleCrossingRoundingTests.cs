using System;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The merge zone at a crossing of two segments a tiny angle apart (QA 2.0 T5, found by the near-contact fan): the output vertex at the
    /// crossing is the crossing rounded to doubles, which lies along the segments up to 8 ulps / sin(angle) from the exact apex, far more than
    /// 3 tube at an angle of 1e-7 radian. The oracle counts such a vertex on either side of the apex (<c>RoundingReach2</c>).
    /// </summary>
    public class Meshing2OracleCrossingRoundingTests
    {
        /// <summary> the unit square, a segment A along y = 0.5 and four segments B_i that start on A at 0.25 + 0.1 i and end at x = 0.85, scale * (i + 1) 2^-k above A </summary>
        static Mesh2Input Fan(int k, double scale)
        {
            var x = new System.Collections.Generic.List<double> { 0, 1, 1, 0 };
            var y = new System.Collections.Generic.List<double> { 0, 0, 1, 1 };
            var input = new Mesh2Input { MinAngleDeg = 30, ConstrainedQuality = true, SteinerLimit = 400000 };
            int Add(double px, double py) { x.Add(px); y.Add(py); return x.Count - 1; }
            input.Segments.Add(new SegmentInput(Add(0.125, 0.5), Add(0.875, 0.5)));
            for (int i = 0; i < 4; ++i) input.Segments.Add(new SegmentInput(Add(0.25 + 0.1 * i, 0.5), Add(0.85, 0.5 + (i + 1) * Math.Pow(2, -k) * scale)));
            input.X = x.ToArray(); input.Y = y.ToArray();
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            return input;
        }

        /// <summary> B_0 and B_2 cross at x = 0.507 at an angle of 5e-7 (k = 24), 1e-7 (26) and 3e-8 radian (28); 5, 22 and 41 violations before the rounding
        /// reach (scale 1.37). The SelfCheck items hold the pinning of the rule itself; this checks it against a real mesher output </summary>
        [Theory]
        [InlineData(24, 1.37)]
        [InlineData(26, 1.37)]
        [InlineData(28, 1.37)]
        public void FanCrossings_AtTinyAngles_AreExcusedByTheMergeZone(int k, double scale)
        {
            var input = Fan(k, scale);
            var output = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, output.Status);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.True(report.MergeZoneExcused > 0, "the merge zone must have been used");
        }
    }
}
