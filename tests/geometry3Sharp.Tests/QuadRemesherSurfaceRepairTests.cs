using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1689: at a fold the neighbour order of a lattice vertex in its tangent plane can disagree with the one of its neighbour (a K4 that wraps a convex
    /// corner traces figure-8 loops), and the faces are dropped. <c>QuadRemesher.SurfaceRotationRepair</c> orders the neighbours of the vertices on dropped loops by
    /// their directions along the surface (the triangles between them unfolded) and keeps the result only if fewer faces are dropped.
    /// </summary>
    public class QuadRemesherSurfaceRepairTests
    {
        // z = -|x| over x in [-3, 3], y in [-1, 2], grid 0.5: a 90 degree roof with the ridge on x = 0
        static DMesh3 Roof()
        {
            var m = new DMesh3();
            int nx = 13, ny = 7;
            for (int i = 0; i < nx; ++i) for (int j = 0; j < ny; ++j) { double x = -3 + 0.5 * i, y = -1 + 0.5 * j; m.AppendVertex(new Vector3d(x, y, -Math.Abs(x))); }
            for (int i = 0; i + 1 < nx; ++i)
                for (int j = 0; j + 1 < ny; ++j) {
                    int a = i * ny + j, b = (i + 1) * ny + j, c = (i + 1) * ny + j + 1, d = i * ny + j + 1;
                    m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
                }
            return m;
        }

        // K4 of the issue: three vertices on the left face of the roof, one on the right face, all six links
        static QuadRemesher.PolyGraph RoofK4()
        {
            Vector3d nl = new Vector3d(-1, 0, 1).Normalized, nr = new Vector3d(1, 0, 1).Normalized;
            var g = new QuadRemesher.PolyGraph();
            g.Add(new Vector3d(-2, 0, -2), nl, 0); g.Add(new Vector3d(-2, 0.5, -2), nl, 0); g.Add(new Vector3d(-1, 0.5, -1), nl, 0); g.Add(new Vector3d(1, 1, -1), nr, 0);
            for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) g.Link(i, j);
            return g;
        }

        [Fact]
        public void TheRoofK4_DropsBothLoopsWithTheTangentPlaneOrders_AndOneWithTheSurfaceDirections()
        {
            int dropped;
            DPolyMesh plain = QuadRemesher.BuildFaces(RoofK4(), 6, out dropped);
            Assert.Equal(2, dropped);                                 // the two loops of the figure-8 are dropped, no face is left
            Assert.Equal(0, plain.FaceCount);
            DPolyMesh repaired = QuadRemesher.SurfaceRotationRepair(RoofK4(), Roof(), 6, plain, ref dropped);
            Assert.Equal(1, dropped);                                 // the outer loop of the K4 (its area is not positive): as for the K4 in a plane
            Assert.Equal(3, repaired.FaceCount);                      // three triangles
            Assert.True(repaired.Validate().IsValid);
        }
    }
}
