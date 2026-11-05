using System;
using System.Linq;
using System.Numerics;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The automatic tube has a floor of one subnormal step, 2^-1074 (decision D3 of 2026-11-05). Below about 2^-1034 the relative
    /// tube (2^-40 of the magnitude) is finer than any double can resolve: a crossing of two constraints, which the mesher must make,
    /// has in general no double within it, so no mesh could pass (found with the reference comparison of m1/subnormal-32).
    /// </summary>
    public class Meshing2OracleSubnormalTubeTests
    {
        /// <summary> three segments that cross nearly in one point, with integer coordinates times 2^exponent </summary>
        static Mesh2Input Crossings(int exponent)
        {
            double s = Math.Pow(2, exponent);
            var input = Generators.Literal(new[] { 0.0, 0, 1000003, 700001, 0, 700001, 1000003, 0, 300007, -100003, 700001, 800011 }.Select(v => v * s).ToArray());
            input.Segments.Add(new SegmentInput(0, 1)); input.Segments.Add(new SegmentInput(2, 3)); input.Segments.Add(new SegmentInput(4, 5));
            return input;
        }

        [Theory]
        [InlineData(-1074)]
        [InlineData(-1060)]
        [InlineData(-1040)]
        [InlineData(-1000)]
        [InlineData(0)]
        [InlineData(200)]
        public void CrossingsAtEveryScale_AreValid(int exponent)
        {
            var input = Crossings(exponent);
            var output = CandidateRegistry.For(Milestone.M2).Run(input);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, "2^" + exponent + ": " + report);
        }

        [Fact]
        public void AutoTolerance_HasTheSubnormalStepAsItsFloor_AndIsRelativeAbove()
        {
            var step = new Q(1, BigInteger.One << 1074);
            // subnormal: the floor
            Assert.True(Oracle.AutoTolerance(Crossings(-1074)) == step);
            // normal: 2^-40 of the larger of the largest coordinate and the extent, as before
            var normal = Crossings(0);
            Assert.True(Oracle.AutoTolerance(normal) == new Q(1000003, BigInteger.One << 40));
            // at the boundary: the relative tube is 2^-1074 when the magnitude is 2^-1034
            var edge = Generators.Literal(0, 0, Math.Pow(2, -1034), 0, 0, Math.Pow(2, -1034));
            Assert.True(Oracle.AutoTolerance(edge) == step);
            var above = Generators.Literal(0, 0, Math.Pow(2, -1030), 0, 0, Math.Pow(2, -1030));
            Assert.True(Oracle.AutoTolerance(above) == new Q(16, BigInteger.One << 1074));
            // no input: no tube
            Assert.True(Oracle.AutoTolerance(new Mesh2Input()) == Q.Zero);
        }
    }
}
