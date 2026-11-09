// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The step of SlopeComposite's moves by ulps (see TerrainSlopeCompositeZeroTests).
    /// </summary>
    public class TerrainSlopeCompositeStepTests
    {
        // the step of both moves (nudge off the slope side, crossings and catch points onto it): from 0 and -0 the scale's
        // ulp, both ways, scaled by 2^k with the scale; elsewhere the next double
        [Fact]
        public void Step_FromZeroIsTheScalesUlp()
        {
            foreach (double m in new[] { 40.0, 37.3, 5.7e-14, 632117.25 }) {
                double u = SlopeComposite.UlpOfScale(m);
                Assert.Equal(ExactInterval.Up(m) - m, u);
                Assert.Equal(u, SlopeComposite.Step(0, true, u));
                Assert.Equal(-u, SlopeComposite.Step(0, false, u));
                Assert.Equal(u, SlopeComposite.Step(-0.0, true, u));
                Assert.Equal(-u, SlopeComposite.Step(-0.0, false, u));
                foreach (double k in new[] { 1024.0, 1.0 / 1024 }) {
                    double uk = SlopeComposite.UlpOfScale(m * k);
                    Assert.Equal(u * k, uk);
                    Assert.Equal(SlopeComposite.Step(0, true, u) * k, SlopeComposite.Step(0, true, uk));
                }
            }
            Assert.Equal(ExactInterval.Up(3.5), SlopeComposite.Step(3.5, true, 1));
            Assert.Equal(ExactInterval.Down(-3.5), SlopeComposite.Step(-3.5, false, 1));
        }
    }
}
