using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #808: a valid Tolerance near the numerical margins needed more cone facets than any run time allows. The 7-gon pit of
    /// the harness case terrain.slope-design took 39 s at 1e-4 (1089 facets per turn), did not end at 3e-5, and at the
    /// smallest valid Tolerance the facet angle rounded to 0 (int.MaxValue facets). Such a Tolerance is now refused at once
    /// (TerrainSlopeDesign.MaxFanDirections; TerrainSlopeFanBoundTests). This file uses only the API of the base, so the
    /// test runs against it there (where the deadline cancels the run)
    /// </summary>
    public class TerrainSlopeFanDirectionsTests
    {
        internal static List<Vector3d> SevenGon() => new List<Vector3d> {
            new Vector3d(11, 1, 0), new Vector3d(7, 8.5, 0), new Vector3d(-2, 10.75, 0), new Vector3d(-10, 5, 0),
            new Vector3d(-9.5, -5.25, 0), new Vector3d(-2.5, -11, 0), new Vector3d(6.75, -8, 0) };

        // the pit without a terrain (slope 1:1.5 up to 8), with a deadline: a run past it is cancelled (Status Cancelled)
        internal static TerrainSlopeDesign Pit(double tol, double seconds = 2)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            return new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(SevenGon(), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)) },
                Direction = TerrainSlopeDesign.Directions.Cut, HeightLimit = 8, Tolerance = tol,
                CancelF = () => sw.Elapsed.TotalSeconds > seconds
            };
        }

        [Fact]
        public void NearTheMargins_RefusedAtOnce()
        {
            // (1e-4 is 5.7 times the margins, 1.76e-5: 1089 facets per turn, 39 s before #808)
            var d = Pit(1e-4);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            Assert.Contains("cone facets", d.Message);
            Assert.Equal(0, d.FacetAngle);
            Assert.Null(d.SlopeSurface);
        }
    }
}
