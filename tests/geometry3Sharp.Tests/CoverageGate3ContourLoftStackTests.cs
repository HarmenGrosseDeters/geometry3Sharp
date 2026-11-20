using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>The check of ContourLoftStackRun.Finish that every contour edge is in the mesh.</summary>
    public class CoverageGate3ContourLoftStackTests
    {
        static List<Vector3d> Poly(double z, params double[] xy)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < xy.Length; i += 2) c.Add(new Vector3d(xy[i], xy[i + 1], z));
            return c;
        }

        [Fact]
        public void ACapMesherThatRoutesAContourEdgeThroughAnotherVertexOfTheRing_GivesBandFailed()
        {
            // The notch vertex (2,1) lies 0.2 above the edge (0,0)-(5,2) of its own ring and in the hot pixel (the grid 1) of that edge: snap
            // rounding splits the locked edge into (0,0)-(2,1)-(5,2). All vertices are on the grid, so the cap keeps them, but the edge is no
            // edge of the cap. The ring ends in slice 0 (CapEnds = false), no band carries its edges: only the up cap does.
            // (all coordinates are multiples of 1/16 below 2, so the working frame is not scaled and its centre is on the grid)
            const double u = 1.0 / 16;
            double[] ringXY = { 0, 0, 5, 2, 5, 6, 2, 1, 0, 6 }, farXY = { 20, 20, 24, 20, 24, 24, 20, 24 };
            var ring = Poly(0, ringXY.Select(x => x * u).ToArray());
            var far = Poly(1, farXY.Select(x => x * u).ToArray());
            var farBelow = Poly(0, farXY.Select(x => x * u).ToArray());
            IList<IList<IList<Vector3d>>> slices = new List<IList<IList<Vector3d>>> {
                new List<IList<Vector3d>> { ring, farBelow },
                new List<IList<Vector3d>> { far },
            };
            var r = new ContourLoftStack { CapEnds = false, CapMesher = new QualityMesh2 { MinAngle = 0, SnapTolerance = u } }.Compute(slices);
            Assert.Equal(ContourLoftStackStatus.BandFailed, r.Status);
            Assert.Equal("an edge of contour 0 of slice 0 is not in the mesh", r.Reason);
            Assert.Null(r.Mesh);
        }
    }
}
