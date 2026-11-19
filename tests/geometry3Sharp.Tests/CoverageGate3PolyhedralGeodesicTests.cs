using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>The trace of PolyhedralGeodesicDistance through a partially reached run: a walk that arrives at a vertex without a record.</summary>
    public class CoverageGate3PolyhedralGeodesicTests
    {
        // a 7 x 9 strip of 2 x 2e-8 (aspect 1e8, the documented limit of the run) with alternating diagonals
        static DMesh3 Strip()
        {
            const int nx = 7, ny = 9; const double h = 1e-8;
            var m = new DMesh3();
            for (int j = 0; j < ny; ++j) for (int i = 0; i < nx; ++i) m.AppendVertex(new Vector3d(-1 + 2.0 * i / (nx - 1), -h + 2 * h * j / (ny - 1), 0));
            for (int j = 0; j < ny - 1; ++j)
                for (int i = 0; i < nx - 1; ++i) {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    if (((i + j) & 1) == 0) { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); } else { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
                }
            return m;
        }

        [Fact]
        public void APartiallyReachedRun_WhoseWalkEndsAtAVertexWithoutARecord_GivesTraceFailed()
        {
            var m = Strip();
            var g = new PolyhedralGeodesicDistance(m);
            Assert.Equal(PolyhedralGeodesicStatus.PartiallyReached, g.Compute(new[] { 31 }));
            Assert.True(g.UnreachedVertices > 0);
            // vertex 28 has a distance, but the straight leg of its path ends at a vertex the run never reached (no record): the walk cannot continue
            Assert.False(double.IsNaN(g.Distance[28]));
            var r = g.TracePath(28);
            Assert.Equal(PolyhedralGeodesicPathStatus.TraceFailed, r.Status);
            Assert.Equal(g.Distance[28], r.Distance);
            Assert.Equal(g.NearestSource[28], r.Source);
            Assert.Equal(0, r.Length);
            Assert.NotEmpty(r.Points);
            Assert.Equal(28, r.Points[0].Vertex);                    // from the target up to where the walk stopped; not a path
            Assert.NotEqual(31, r.Points[r.Points.Count - 1].Vertex);
        }
    }
}
