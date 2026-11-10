using System;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Rule O-G, the iterated image (owner decision 2026-11-10, option A): the oracle judges a chain against the source's iterated image
    /// (Q17: output vertices are hot pixels and reroute it), which at a sub-pixel wedge leaves the one-pixel tube of the exact segment.
    /// Found by the QualityMesh2 CsCheck campaign (T5): a ring whose edges meet at 0.08 degrees.
    /// </summary>
    public class Meshing2OracleImageChainTests
    {
        /// <summary> the ring (0,0), (95,-3), (34,-1), (250,0), (180,2) in half units, pixel one half, conforming refinement at 30 degrees </summary>
        static Mesh2Input Wedge(int exponent)
        {
            double unit = Math.Pow(2, exponent) * 0.5;
            var input = new Mesh2Input {
                X = new[] { 0, 95 * unit, 34 * unit, 250 * unit, 180 * unit }, Y = new[] { 0, -3 * unit, -1 * unit, 0, 2 * unit },
                SnapTolerance = unit, MinAngleDeg = 30, SteinerLimit = 20000 };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3, 4 }, false));
            return input;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-20)]
        [InlineData(-300)]
        [InlineData(150)]
        [InlineData(30)]
        public void ChainAlongAnImageBeyondOnePixel_IsAccepted(int exponent)
        {
            var input = Wedge(exponent);
            var output = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, output.Status);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            // the image of the first edge drifts 1.59 pixels from it: beyond the tube of one pixel, within the guard of 2.5
            Assert.InRange(report.MaxImageDriftPixels.Value, 1.5, 2.5);
        }

        /// <summary> the same mesh with a chain vertex off the image: still rejected </summary>
        [Fact]
        public void ChainVertexOffTheImage_IsRejected()
        {
            var input = Wedge(0);
            var output = new QualityMesh2Candidate().Run(input);
            // move a Steiner vertex of the chain on pixel row -1 (x = 62 pixels) two rows down: off the image and off the tube
            double pixel = input.SnapTolerance;
            int v = Enumerable.Range(0, output.VX.Length).First(i => i >= input.Count && Math.Abs(output.VX[i] / pixel - 62) < 1e-9 && Math.Abs(output.VY[i] / pixel + 1) < 1e-9);
            output.VY[v] -= 2 * pixel;
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.False(report.Ok, "a chain vertex two pixels off the image must not pass");
        }
    }
}
