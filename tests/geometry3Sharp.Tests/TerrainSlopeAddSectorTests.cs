// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SlopeMap.add_sector, the fan planes a face takes (#1530; docs/design/SLOPE_ENVELOPE_CANONICAL.md section 7). In its own file:
    /// it calls an internal method that the base library does not have
    /// </summary>
    public class TerrainSlopeAddSectorTests
    {
        // #1530, add_sector on a hand-made fan: angles 0, t, ..., pi (t = pi / 16, theta = t), apex at the origin, a face around the
        // direction -0.6 pi at distance 10, in the fan's gap. The window (the face's directions +- 2 theta) holds no plane; the nearest
        // planes are pi going clockwise (0.4 pi away) and 0 going counter-clockwise (0.6 pi away): both are taken, nothing between.
        // And the mirror (a face at +0.6 pi... inside the fan) takes only its window. Kills the mutants that drop either end's plane
        [Theory]
        [InlineData(-0.6)]
        [InlineData(-0.4)]
        public void AddSector_TakesTheNearestPlaneBeyondEachEndOfTheWindow(double faceTurns)
        {
            var map = new SlopeMap(new ExactSlopeKernel());
            double t = Math.PI / 16;
            var fan = new List<int>(); var ang = new List<double>();
            for (int i = 0; i <= 16; ++i) { fan.Add(100 + i); ang.Add(i * t); }
            double a = faceTurns * Math.PI;
            Func<double, double, Vector2d> at = (phi, r) => new Vector2d(r * Math.Cos(phi), r * Math.Sin(phi));
            var face = new List<Vector2d> { at(a - 0.02, 10), at(a + 0.02, 10), at(a, 11) };
            var relevant = new List<int>();
            map.add_sector(relevant, fan, ang, Vector2d.Zero, face, t);
            // (the two end planes: 0 is plane 100, pi is plane 116; the window around the face holds none)
            Assert.Equal(new[] { 100, 116 }, relevant.OrderBy(x => x).ToArray());
            // inside the fan, at 0.5 pi: the window (+- 2 theta and the face's own width) and the first plane beyond each end
            var inside = new List<Vector2d> { at(0.5 * Math.PI - 0.02, 10), at(0.5 * Math.PI + 0.02, 10), at(0.5 * Math.PI, 11) };
            relevant.Clear();
            map.add_sector(relevant, fan, ang, Vector2d.Zero, inside, t);
            Assert.Equal(new[] { 105, 106, 107, 108, 109, 110, 111 }, relevant.OrderBy(x => x).ToArray());
        }
    }
}
