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
            var g = RoofK4();
            var rot = QuadRemesher.SurfaceRotations(g, Roof(), new[] { 0, 1, 2, 3 });
            Assert.Equal(4, rot.Count);
            DPolyMesh repaired = QuadRemesher.BuildFaces(g, 6, out dropped, rot);
            Assert.Equal(1, dropped);                                 // the outer loop of the K4 (its area is not positive): as for the K4 in a plane
            Assert.Equal(3, repaired.FaceCount);                      // three triangles
            Assert.True(repaired.Validate().IsValid);
            Assert.True(QuadRemesher.HasNoRepeatedVertex(repaired));
        }

        [Fact]
        public void TheRepairKeepsTheResultThatHasMoreBoundaryEdges()
        {
            // the isolated K4: the three triangles have 3 boundary edges, the result before has no face and none: not kept (in a closed surface the holes shrink)
            int dropped;
            DPolyMesh plain = QuadRemesher.BuildFaces(RoofK4(), 6, out dropped);
            Assert.Same(plain, QuadRemesher.SurfaceRotationRepair(RoofK4(), Roof(), 6, plain, ref dropped));
            Assert.Equal(2, dropped);
        }

        // two triangles sharing the vertex v: the outer loop of the bow-tie is a hexagon that visits v twice (a loop of up to maxSides vertices with a repeated vertex)
        static void AddBowTie(QuadRemesher.PolyGraph g, double x)
        {
            int v = g.Add(new Vector3d(x, 0, 0), Vector3d.AxisZ, 0), a = g.Add(new Vector3d(x + 1, -0.3, 0), Vector3d.AxisZ, 0), b = g.Add(new Vector3d(x + 1, 0.3, 0), Vector3d.AxisZ, 0);
            int c = g.Add(new Vector3d(x - 1, 0.3, 0), Vector3d.AxisZ, 0), d = g.Add(new Vector3d(x - 1, -0.3, 0), Vector3d.AxisZ, 0);
            g.Link(v, a); g.Link(v, b); g.Link(a, b); g.Link(v, c); g.Link(v, d); g.Link(c, d);
        }

        [Fact]
        public void ALoopWithARepeatedVertex_IsBad_EvenWhenItIsNotLongerThanMaxSides()
        {
            var g = new QuadRemesher.PolyGraph(); AddBowTie(g, 0);
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, QuadRemesher.BadLoopVertices(g, 6, null).OrderBy(x => x).ToArray());
            // a loop longer than maxLoop is not (the outer loop of an open input)
            var ring = new QuadRemesher.PolyGraph();
            for (int i = 0; i < 40; ++i) ring.Add(new Vector3d(Math.Cos(i * Math.PI / 20), Math.Sin(i * Math.PI / 20), 0), Vector3d.AxisZ, 0);
            for (int i = 0; i < 40; ++i) ring.Link(i, (i + 1) % 40);
            Assert.Equal(40, QuadRemesher.BadLoopVertices(ring, 6, null).Count);
            Assert.Empty(QuadRemesher.BadLoopVertices(ring, 6, null, 32));
        }

        [Fact]
        public void MoreBadVerticesThanTheCap_LeaveTheResultAlone()
        {
            var g = new QuadRemesher.PolyGraph();
            for (int i = 0; i < 60; ++i) AddBowTie(g, 10 * i);
            Assert.True(QuadRemesher.BadLoopVertices(QuadRemesher.CopyGraph(g), 6, null, 32).Count > 256);
            var dummy = new DPolyMesh();
            int dropped = 60;
            Assert.Same(dummy, QuadRemesher.SurfaceRotationRepair(g, Roof(), 6, dummy, ref dropped));
            Assert.Equal(60, dropped);
            Assert.False(QuadRemesher.SurfaceRepairApplies(0)); Assert.True(QuadRemesher.SurfaceRepairApplies(1));
            Assert.True(QuadRemesher.SurfaceRepairApplies(256)); Assert.False(QuadRemesher.SurfaceRepairApplies(257));
        }

        [Fact]
        public void AVertexWithAnUnreachableNeighbour_KeepsItsOrder_AndNothingIsChanged()
        {
            // the roof cut at the ridge into two sheets: no path of triangles from the left face to the right one
            var m = new DMesh3();
            foreach (int side in new[] { -1, 1 })
                for (int i = 0; i < 7; ++i) for (int j = 0; j < 7; ++j) { double x = side * 0.5 * i; m.AppendVertex(new Vector3d(x, -1 + 0.5 * j, -Math.Abs(x))); }
            for (int side = 0; side < 2; ++side)
                for (int i = 0; i < 6; ++i) for (int j = 0; j < 6; ++j) {
                    int o = side * 49, a = o + i * 7 + j, b = o + (i + 1) * 7 + j, c = o + (i + 1) * 7 + j + 1, d = o + i * 7 + j + 1;
                    if (side == 0) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); } else { m.AppendTriangle(a, c, b); m.AppendTriangle(a, d, c); }
                }
            int dropped = 2;
            DPolyMesh plain = QuadRemesher.BuildFaces(RoofK4(), 6, out dropped);
            Assert.Same(plain, QuadRemesher.SurfaceRotationRepair(RoofK4(), m, 6, plain, ref dropped));
            Assert.Equal(2, dropped);
            var tree = new DMeshAABBTree3(m, true);
            Assert.True(double.IsNaN(QuadRemesher.SurfaceAngle(m, tree, tree.FindNearestTriangle(new Vector3d(-2, 0, -2)), new Vector3d(-2, 0, -2), new Vector3d(1, 1, -1))));
        }

        [Fact]
        public void ADegenerateTriangleInThePath_GivesNoDirection()
        {
            // T0 (0,0) (1,0) (0,1), T1 = (0,1) (1,0) (0.5,0.5) has no area, T2 = (0.5,0.5) (1,0) (1,0.5): the vertex of T1 that is not on the shared edge lies on its line
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendVertex(new Vector3d(0.5, 0.5, 0)); m.AppendVertex(new Vector3d(1, 0.5, 0));
            m.AppendTriangle(0, 1, 2); m.AppendTriangle(2, 1, 3); m.AppendTriangle(3, 1, 4);
            var tree = new DMeshAABBTree3(m, true);
            var p = new Vector3d(0.2, 0.2, 0); var q = new Vector3d(0.9, 0.4, 0);
            Assert.Equal(0, tree.FindNearestTriangle(p)); Assert.Equal(2, tree.FindNearestTriangle(q));
            Assert.True(double.IsNaN(QuadRemesher.SurfaceAngle(m, tree, 0, p, q)));
            // the same without the degenerate triangle in between: a direction
            var flat = new DMesh3();
            flat.AppendVertex(new Vector3d(0, 0, 0)); flat.AppendVertex(new Vector3d(1, 0, 0)); flat.AppendVertex(new Vector3d(0, 1, 0)); flat.AppendVertex(new Vector3d(1, 1, 0));
            flat.AppendTriangle(0, 1, 2); flat.AppendTriangle(2, 1, 3);
            var t2 = new DMeshAABBTree3(flat, true);
            Assert.Equal(Math.Atan2(0.5, 0.5), QuadRemesher.SurfaceAngle(flat, t2, 0, new Vector3d(0.2, 0.2, 0), new Vector3d(0.7, 0.7, 0)), 9);
        }

        [Fact]
        public void TheAngle_IsCounterClockwiseAboutTheNormal_AlongTheUnfoldedSurface()
        {
            // over the fold of the roof (surface normal up): the unfolded way to (1, 0, -1) is 3 sqrt 2 straight on, the one to (1, 1, -1) is as far and 1 to the left (counter-clockwise)
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            Vector3d p = new Vector3d(-2, 0, -2);
            int tp = tree.FindNearestTriangle(p);
            double straight = QuadRemesher.SurfaceAngle(m, tree, tp, p, new Vector3d(1, 0, -1)), left = QuadRemesher.SurfaceAngle(m, tree, tp, p, new Vector3d(1, 1, -1));
            Assert.Equal(Math.Atan2(1, 3 * Math.Sqrt(2)), left - straight, 6);
        }

        [Fact]
        public void BoundaryEdgesAndRepeatedVertices_OfPolygonMeshes()
        {
            var two = new DPolyMesh(); // two triangles sharing an edge: 4 boundary edges
            two.AppendVertex(new Vector3d(0, 0, 0)); two.AppendVertex(new Vector3d(1, 0, 0)); two.AppendVertex(new Vector3d(0, 1, 0)); two.AppendVertex(new Vector3d(1, 1, 0));
            two.AppendFace(new[] { 0, 1, 2 }); two.AppendFace(new[] { 2, 1, 3 });
            Assert.Equal(4, QuadRemesher.BoundaryEdgeCount(two));
            Assert.True(QuadRemesher.HasNoRepeatedVertex(two));
            two.AppendFace(new[] { 0, 2, 1, 3, 0 });                 // a closing quad-like face with vertex 0 twice
            Assert.False(QuadRemesher.HasNoRepeatedVertex(two));
        }
    }
}
