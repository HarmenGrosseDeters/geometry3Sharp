using System;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Rule (ii') and the lobe (owner decision 2026-11-10): a stretch of a chain that the input's rounding collapses (a spike) is rerouted
    /// in the full image through the hot pixel of a refinement vertex of another chain; the reroute makes a lobe with that chain, which
    /// even-odd parity turns into area in the full image although the mesh (and the input's rounding) has none. Found by the QualityMesh2
    /// CsCheck campaign (T5): a hole ring with a spike tip, ChainSkipsHotPixel and VertexUnused on a valid mesh.
    /// </summary>
    public class Meshing2OracleSpikeLobeTests
    {
        /// <summary> the hole (-12,-2), (-11,-4), (-12,-3), (-12,-4), (-3,-14), (-2,-12) in a triangle, pixel 1 (units of 2^exponent), 20 degrees </summary>
        static Mesh2Input HoleWithSpike(int exponent)
        {
            double u = Math.Pow(2, exponent);
            var x = new double[] { 19, -19, -7, -12, -11, -12, -12, -3, -2 };
            var y = new double[] { -14, 9, -28, -2, -4, -3, -4, -14, -12 };
            for (int i = 0; i < x.Length; ++i) { x[i] *= u; y[i] *= u; }
            var input = new Mesh2Input { X = x, Y = y, SnapTolerance = u, MinAngleDeg = 20, SteinerLimit = 20000 };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
            input.Rings.Add(new RingInput(new[] { 3, 4, 5, 6, 7, 8 }, true));
            return input;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-30)]
        [InlineData(40)]
        public void SpikeRerouted_ByARefinementPixel_IsExcused_AndCounted(int exponent)
        {
            var input = HoleWithSpike(exponent);
            var output = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, output.Status);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.True(report.LobeExcuses > 0, "the lobe excuse must have been used");
        }

        /// <summary> the bounds of the excuse, directly: a lobe within one pixel of the stretch's chord, links to neighbouring pixels </summary>
        [Fact]
        public void LobeBounds_AreOnePixel()
        {
            var pixel = new Q(1, 1);
            var a = new Pt(new Q(0, 1), new Q(0, 1)); var b = new Pt(new Q(10, 1), new Q(0, 1));
            Assert.True(Oracle.LobeWithinTube(a, b, new[] { new Pt(new Q(5, 1), new Q(1, 1)), new Pt(new Q(5, 1), new Q(-1, 1)) }, pixel));
            Assert.False(Oracle.LobeWithinTube(a, b, new[] { new Pt(new Q(5, 1), new Q(2, 1)) }, pixel));
            Assert.False(Oracle.LobeWithinTube(a, b, new[] { new Pt(new Q(5, 1), new Q(1, 1)), new Pt(new Q(5, 1), new Q(-3, 2)) }, pixel));
            var n = (new System.Numerics.BigInteger(-11), new System.Numerics.BigInteger(-4));
            Assert.True(Oracle.LinksStayAdjacent(n, new[] { (new System.Numerics.BigInteger(-12), new System.Numerics.BigInteger(-3)), (new System.Numerics.BigInteger(-10), new System.Numerics.BigInteger(-5)) }));
            Assert.False(Oracle.LinksStayAdjacent(n, new[] { (new System.Numerics.BigInteger(-13), new System.Numerics.BigInteger(-4)) }));
        }
    }
}
