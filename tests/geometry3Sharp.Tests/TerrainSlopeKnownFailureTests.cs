// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Known failures of the terrain slope design, pinned with today's result until they are fixed (docs/design/TERRAIN.md
    /// §11). Each test asserts the current, wrong status; the fix turns it into a success test.
    /// </summary>
    public class TerrainSlopeKnownFailureTests
    {
        // Direction.Auto with a closed toe exactly at the terrain's height where it crosses (a square at z = 5 on the plane
        // z = 5 + 0.03 x: the toe edges meet the terrain on the line x = 0, the cut part on one side, the fill part on the
        // other). The joint triangulation of the terrain triangles that both runs replace ends with "inconsistent parity",
        // CompositeFailed (an internal status, never a wrong result). 0.1 above or below, and at other angles, it succeeds.
        // Found by the g3cli slope tests (2026-11-03)
        [Theory]
        [InlineData(30.0)]
        [InlineData(40.0)]
        public void Auto_ToeMeetingTheTerrainOnItsCrossing_CompositeFailed(double degrees)
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 16, 16, (x, y) => 5 + 0.03 * x);
            Func<double, TerrainSlopeDesign> make = (z) => new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(new List<Vector3d> { new Vector3d(-5, -5, z), new Vector3d(5, -5, z), new Vector3d(5, 5, z), new Vector3d(-5, 5, z) },
                                           true, SlopeToe.Sides.Outside, SlopeSpec.Degrees(degrees)) },
                Terrain = new DMesh3(terrain), Tolerance = 0.05, BuildComposite = true, Direction = TerrainSlopeDesign.Directions.Auto
            };
            var d = make(5);
            d.Compute();
            Assert.Equal(TerrainSlopeDesign.Statuses.CompositeFailed, d.Status);
            Assert.Equal(TerrainSlopeDesign.StatusKinds.Internal, d.StatusKind);
            Assert.Contains("inconsistent parity", d.Message);
            foreach (double z in new[] { 4.9, 5.1 }) {
                var ok = make(z);
                Assert.True(ok.Compute(), ok.Status + " " + ok.Message);
            }
        }
    }
}
