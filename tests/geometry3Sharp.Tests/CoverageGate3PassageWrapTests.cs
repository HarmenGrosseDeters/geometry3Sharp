using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// PassageWrap when the underlying alpha wrap refuses the input: the pin checks and the bound pass (the wrap supports
    /// coordinates only within [2^-128, 2^128]; PassageWrap refuses input outside it at once as InvalidInput, with the
    /// wrap's wording, no result and no pin results claiming a pass.
    /// </summary>
    public class CoverageGate3PassageWrapTests
    {
        static DMesh3 Cube(double half)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d(((i & 1) == 0 ? -1 : 1) * half, ((i & 2) == 0 ? -1 : 1) * half, ((i & 4) == 0 ? -1 : 1) * half));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (var q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            return m;
        }

        static PassageWrap Run(double scale, out bool ok)
        {
            var axis = new List<Vector3d> { new Vector3d(0, 0, -0.25 * scale), new Vector3d(0, 0, 0.25 * scale) };
            var w = new PassageWrap(Cube(0.5 * scale)) { Pins = { new PassagePin(axis, 0.05 * scale) }, MaxThreads = 1 };
            ok = w.Compute();
            return w;
        }

        [Theory]
        [InlineData(140)] [InlineData(130)] [InlineData(-130)] [InlineData(-200)]
        public void AnInputOutsideTheWrapsSupportedRange_IsInvalidInputAtOnce(int exponent)
        {
            var w = Run(Math.Pow(2, exponent), out bool ok);
            Assert.False(ok);
            Assert.Equal(PassageWrap.PassageStatus.InvalidInput, w.Status);
            Assert.Contains("supported range", w.Error);
            Assert.Null(w.Result);
            Assert.Equal(0, w.FallbackSteps);
            Assert.False(w.AllPassed);
        }

        // the largest coordinate is 0.5 * scale: exactly 2^128 is inside, the next double above is outside; the same at 2^-128
        [Theory]
        [InlineData(129)] [InlineData(-127)]
        public void TheLimitsThemselves_AreInside(int exponent)
        {
            var w = Run(Math.Pow(2, exponent), out _);
            Assert.NotEqual(PassageWrap.PassageStatus.InvalidInput, w.Status);
            Assert.NotEqual(PassageWrap.PassageStatus.WrapFailed, w.Status);
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, w.WrapStatus);
            Assert.NotNull(w.Result);
        }

        [Theory]
        [InlineData(129)] [InlineData(-127)]
        public void JustOutsideTheLimits_IsInvalidInput(int exponent)
        {
            double scale = Math.Pow(2, exponent);
            // one double step beyond the limit on the large side, a halving on the small one (0.5 * scale = 2^-128 is inside)
            double outside = exponent > 0 ? BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(scale) + 1) : scale / 2;
            var w = Run(outside, out bool ok);
            Assert.False(ok);
            Assert.Equal(PassageWrap.PassageStatus.InvalidInput, w.Status);
        }

        [Fact]
        public void TheSameInputWithinTheSupportedRange_IsWrapped()
        {
            // (the pin runs through a solid cube, so the passage is not kept: the point is that the wrap itself ran and succeeded)
            var w = Run(1, out _);
            Assert.NotEqual(PassageWrap.PassageStatus.WrapFailed, w.Status);
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, w.WrapStatus);
            Assert.NotNull(w.Result);
        }
    }
}
