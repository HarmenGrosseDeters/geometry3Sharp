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

        // a K4 on the roof whose face (A, B, C) is a sliver across the fold: its surface angles add up to pi, but the chord triangle of the vertices has an area vector against the
        // vertex normals (-0.7), so BuildFaces drops it as a folded face and leaves a hole
        static QuadRemesher.PolyGraph SliverK4()
        {
            Vector3d nl = new Vector3d(-1, 0, 1).Normalized, nr = new Vector3d(1, 0, 1).Normalized;
            var g = new QuadRemesher.PolyGraph();
            g.Add(new Vector3d(-3, -1, -3), nl, 0); g.Add(new Vector3d(1, 0.5, -1), nr, 0); g.Add(new Vector3d(-0.5, 0, -0.5), nl, 0); g.Add(new Vector3d(-1, 1.5, -1), nl, 0);
            for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) g.Link(i, j);
            return g;
        }

        [Fact]
        public void ASliverAcrossAFold_IsDroppedByTheAreaTest_AndKeptByTheSurfaceAngles()
        {
            int dropped;
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = SliverK4(); var seen = new List<int[]>();
            var rot = QuadRemesher.SurfaceRotations(g, m, new[] { 0, 1, 2, 3 }, tree);        // (the tangent plane orders of the fold are not consistent: the repair of 1689 first)
            DPolyMesh plain = QuadRemesher.BuildFaces(SliverK4(), 6, out dropped, rot);
            Assert.Equal("faces 2 dropped 2", "faces " + plain.FaceCount + " dropped " + dropped);              // the sliver and the outer loop
            DPolyMesh kept = QuadRemesher.BuildFaces(g, 6, out dropped, rot, loop => { seen.Add(loop); return QuadRemesher.SurfaceFaceDeviation(g, m, tree, loop) <= QuadRemesher.SurfaceSliverTolerance; });
            Assert.Equal(3, kept.FaceCount); Assert.Equal(1, dropped);                // only the outer loop stays dropped
            Assert.True(kept.Validate().IsValid);
            Assert.Equal(2, seen.Count);                                              // asked for the faces the area test dropped only, not for the two that passed it
            var sliver = seen.Single(l => l.Length == 3 && l.Contains(0) && l.Contains(1) && l.Contains(2));
            Assert.True(QuadRemesher.SurfaceFaceDeviation(g, m, tree, sliver) < 1e-6);
            Assert.InRange(QuadRemesher.SurfaceSliverTolerance, 0.1 * Math.PI / 180, 2 * Math.PI / 180);       // the measured sums are 0 to 1e-13 degrees off; a corner is off by tens of degrees
            // the same loop the other way round (an inverted face, or the outer loop of a patch): not a face (the other links of the K4 run through the side it has on its left)
            Assert.True(double.IsPositiveInfinity(QuadRemesher.SurfaceFaceDeviation(g, m, tree, Enumerable.Reverse(sliver).ToArray())));
            // alone, without other links: the angles on its left add up to (k + 2) pi, 4 pi more
            var alone = new QuadRemesher.PolyGraph();
            for (int i = 0; i < 3; ++i) alone.Add(g.Pos[i], g.Nrm[i], 0);
            alone.Link(0, 1); alone.Link(1, 2); alone.Link(2, 0);
            Assert.Equal(0.0, QuadRemesher.SurfaceFaceDeviation(alone, m, tree, sliver), 6);
            Assert.Equal(4 * Math.PI, QuadRemesher.SurfaceFaceDeviation(alone, m, tree, Enumerable.Reverse(sliver).ToArray()), 6);
        }

        [Fact]
        public void ALoopThatIsNotASimpleFaceOfTheSurface_IsNotRescued()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = new QuadRemesher.PolyGraph();
            foreach (var p in new[] { new Vector3d(-2, 0, -2), new Vector3d(-1, 1, -1), new Vector3d(-1, 0, -1), new Vector3d(-2, 1, -2) }) g.Add(p, new Vector3d(-1, 0, 1).Normalized, 0);
            // 0-1-2-3 crosses itself (0-1 and 2-3 cross); a repeated vertex; two vertices
            Assert.True(double.IsPositiveInfinity(QuadRemesher.SurfaceFaceDeviation(g, m, tree, new[] { 0, 1, 2, 3 })));
            Assert.True(double.IsPositiveInfinity(QuadRemesher.SurfaceFaceDeviation(g, m, tree, new[] { 0, 1, 0 })));
            Assert.True(double.IsPositiveInfinity(QuadRemesher.SurfaceFaceDeviation(g, m, tree, new[] { 0, 1 })));
            // the square 0-2-1-3 is simple: a face (the angles add up to 2 pi)
            Assert.True(QuadRemesher.SurfaceFaceDeviation(g, m, tree, new[] { 0, 2, 1, 3 }) < 1e-6);
        }

        [Fact]
        public void TheRescueIsNotAskedBeyondTheNumberOfSidesOfAFace()
        {
            // a square, maxSides 3: both its loops are too long, whatever rescue says
            var g = new QuadRemesher.PolyGraph();
            foreach (var p in new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0) }) g.Add(p, Vector3d.AxisZ, 0);
            g.Link(0, 1); g.Link(1, 2); g.Link(2, 3); g.Link(3, 0);
            int dropped; int asked = 0;
            DPolyMesh r = QuadRemesher.BuildFaces(g, 3, out dropped, null, loop => { asked++; return true; });
            Assert.Equal(0, r.FaceCount); Assert.Equal(2, dropped); Assert.Equal(0, asked);
            // and a rescue that says yes keeps the loop that the area test dropped (maxSides 4): the outer loop of the square, dropped by its area, is taken as a second face
            r = QuadRemesher.BuildFaces(g, 4, out dropped, null, loop => true);
            Assert.Equal(2, r.FaceCount); Assert.Equal(0, dropped);
        }

        // two links that cross on the surface (a (-1, 0.5) - c (-1, 1) and b (-3, 1) - d (3, 0.5) over the ridge of the roof, unfolded) while their chords in space pass 4 times the shorter one apart
        static QuadRemesher.PolyGraph CrossingOverTheRidge(byte kindB = 0, byte kindD = 0)
        {
            Vector3d nl = new Vector3d(-1, 0, 1).Normalized, nr = new Vector3d(1, 0, 1).Normalized;
            var g = new QuadRemesher.PolyGraph();
            g.Add(new Vector3d(-1, 0.5, -1), nl, 0); g.Add(new Vector3d(-3, 1, -3), nl, kindB); g.Add(new Vector3d(-1, 1, -1), nl, 0); g.Add(new Vector3d(3, 0.5, -3), nr, kindD);
            g.Link(0, 2); g.Link(1, 3);
            return g;
        }

        [Fact]
        public void LinksThatCrossOnTheSurface_AreFound_WhereTheirChordsInSpaceDoNotCross()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = CrossingOverTheRidge();
            Assert.Equal(0, QuadRemesher.RemoveCrossings(QuadRemesher.CopyGraph(g)));           // the 3D gap is too wide (the crossing repair of #1674)
            var found = QuadRemesher.SurfaceCrossings(g, m, tree, new[] { 0, 1, 2, 3 });
            Assert.Equal(new[] { Tuple.Create(1, 3) }, found.ToArray());                         // the longer of the two
            // the same links with the end d on the other side of the first link do not cross
            var apart = CrossingOverTheRidge(); apart.Pos[3] = new Vector3d(3, 1.5, -3); apart.Pos[1] = new Vector3d(-3, 2, -3);
            Assert.Empty(QuadRemesher.SurfaceCrossings(apart, m, tree, new[] { 0, 1, 2, 3 }));
            Assert.Empty(QuadRemesher.SurfaceCrossings(g, m, tree, new int[0]));
        }

        [Fact]
        public void ALinkOfAChain_IsKept_AndTheOtherGoes_AndNeitherIfBothAreLinks()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = CrossingOverTheRidge(1, 1);                                                 // the longer link joins two crease vertices
            Assert.Equal(new[] { Tuple.Create(0, 2) }, QuadRemesher.SurfaceCrossings(g, m, tree, new[] { 0, 1, 2, 3 }).ToArray());
            g.Kind[0] = 1; g.Kind[2] = 1;                                                       // both are links
            Assert.Empty(QuadRemesher.SurfaceCrossings(g, m, tree, new[] { 0, 1, 2, 3 }));
        }

        [Fact]
        public void TheSurfaceCrossingsAreOnlyLookedForAtFewVertices()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = CrossingOverTheRidge();
            Assert.Single(QuadRemesher.SurfaceCrossings(g, m, tree, Enumerable.Range(0, 4)));
            for (int i = 4; i < 33; ++i) g.Add(new Vector3d(-2, -1, -2), new Vector3d(-1, 0, 1).Normalized, 0);       // 33 vertices (the cap is 32)
            Assert.Empty(QuadRemesher.SurfaceCrossings(g, m, tree, Enumerable.Range(0, 33)));
            Assert.Single(QuadRemesher.SurfaceCrossings(g, m, tree, Enumerable.Range(0, 32)));
        }

        [Fact]
        public void AK4InConvexPositionOnTheRoof_IsNotPlanarWithTheSurfaceOrders_ButWithoutTheLongerDiagonal()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = CrossingOverTheRidgeK4();
            var all = new[] { 0, 1, 2, 3 };
            int dropped;
            DPolyMesh withAll = QuadRemesher.BuildFaces(CrossingOverTheRidgeK4(), 6, out dropped, QuadRemesher.SurfaceRotations(g, m, all, tree));
            Assert.True(dropped >= 1 && withAll.FaceCount <= 1);          // the two crossing links: no planar rotation system (a figure of eight), however the neighbours are ordered
            var cross = QuadRemesher.SurfaceCrossings(g, m, tree, all);
            Assert.Equal(new[] { Tuple.Create(1, 3) }, cross.ToArray());
            g.Unlink(1, 3);
            DPolyMesh planar = QuadRemesher.BuildFaces(g, 6, out dropped, QuadRemesher.SurfaceRotations(g, m, all, tree));
            Assert.Equal(2, planar.FaceCount); Assert.Equal(1, dropped);   // two triangles and the outer loop
            Assert.True(planar.Validate().IsValid);
        }

        static QuadRemesher.PolyGraph CrossingOverTheRidgeK4()
        {
            var g = CrossingOverTheRidge();
            for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) g.Link(i, j);
            return g;
        }

        static DPolyMesh Triangles(int count, bool repeatedVertex = false)
        {
            var m = new DPolyMesh();
            for (int t = 0; t < count; ++t) { int o = m.VertexCount; m.AppendVertex(new Vector3d(t, 0, 0)); m.AppendVertex(new Vector3d(t + 0.5, 1, 0)); m.AppendVertex(new Vector3d(t + 1, 0, 0)); m.AppendFace(new[] { o, o + 1, o + 2 }); }
            if (repeatedVertex) m.AppendFace(new[] { 0, 1, 0, 2 });
            return m;
        }

        [Fact]
        public void ARebuild_IsKept_OnlyWithStrictlyFewerDroppedFaces_NoMoreBoundaryEdges_AndNoRepeatedVertex()
        {
            DPolyMesh two = Triangles(2);                                  // 6 boundary edges
            Assert.True(QuadRemesher.SurfaceRebuildAccepted(two, 1, 2, 6));
            Assert.False(QuadRemesher.SurfaceRebuildAccepted(two, 2, 2, 6));       // as many dropped faces: not kept
            Assert.False(QuadRemesher.SurfaceRebuildAccepted(two, 3, 2, 6));
            Assert.False(QuadRemesher.SurfaceRebuildAccepted(two, 1, 2, 5));       // fewer dropped faces, but more boundary edges than the result before
            Assert.True(QuadRemesher.SurfaceRebuildAccepted(two, 1, 2, 7));
            Assert.False(QuadRemesher.SurfaceRebuildAccepted(Triangles(2, true), 1, 2, 100));      // a face with a repeated vertex
        }

        static QuadRemesher.PolyGraph LeftFaceGraph(params double[][] xy)
        {
            var g = new QuadRemesher.PolyGraph();
            foreach (var p in xy) g.Add(new Vector3d(p[0], p[1], -Math.Abs(p[0])), new Vector3d(-1, 0, 1).Normalized, 0);
            return g;
        }

        [Fact]
        public void ALinkInsideTheWedgeOfAFace_MakesTheLoopNoFace()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = LeftFaceGraph(new[] { -3.0, -1 }, new[] { -1.0, -1 }, new[] { -2.0, 1 }, new[] { -2.0, -0.5 });          // the triangle 0 1 2 and a vertex 3 inside it
            g.Link(0, 1); g.Link(1, 2); g.Link(2, 0);
            Assert.Equal(0.0, QuadRemesher.SurfaceFaceDeviation(g, m, tree, new[] { 0, 1, 2 }), 6);
            g.Link(0, 3);                                                                                        // a link of vertex 0 into the triangle: no face
            Assert.True(double.IsPositiveInfinity(QuadRemesher.SurfaceFaceDeviation(g, m, tree, new[] { 0, 1, 2 })));
        }

        [Fact]
        public void ALoopThatCrossesItselfInTheChart_IsNoFace_ForFiveAndSixSides()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            // the five points of a pentagon on the left face: in order a convex pentagon, in the order 0 2 4 1 3 a pentagram
            var g = LeftFaceGraph(new[] { -2.0, -1 }, new[] { -1.0, -0.5 }, new[] { -1.0, 0.5 }, new[] { -2.0, 1.0 }, new[] { -3.0, 0 });
            Assert.True(QuadRemesher.IsSimpleInChart(g, m, tree, new[] { 0, 1, 2, 3, 4 }));
            Assert.False(QuadRemesher.IsSimpleInChart(g, m, tree, new[] { 0, 2, 4, 1, 3 }));
            Assert.True(double.IsPositiveInfinity(QuadRemesher.SurfaceFaceDeviation(g, m, tree, new[] { 0, 2, 4, 1, 3 })));
            Assert.True(QuadRemesher.SurfaceFaceDeviation(g, m, tree, new[] { 0, 1, 2, 3, 4 }) < 1e-6);
            // six points: the bow-tie order 0 1 2 5 4 3 (the sides 2-5 and 3-0... cross)
            var h = LeftFaceGraph(new[] { -3.0, -1 }, new[] { -2.0, -1 }, new[] { -1.0, -1 }, new[] { -3.0, 1 }, new[] { -2.0, 1 }, new[] { -1.0, 1 });
            Assert.True(QuadRemesher.IsSimpleInChart(h, m, tree, new[] { 0, 1, 2, 5, 4, 3 }));
            Assert.False(QuadRemesher.IsSimpleInChart(h, m, tree, new[] { 0, 1, 5, 4, 2, 3 }));
            Assert.True(double.IsPositiveInfinity(QuadRemesher.SurfaceFaceDeviation(h, m, tree, new[] { 0, 1, 5, 4, 2, 3 })));
        }

        [Fact]
        public void ALoopThatVisitsAVertexTwice_IsNoFace()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = LeftFaceGraph(new[] { -3.0, -1 }, new[] { -1.0, -1 }, new[] { -2.0, 1 });
            g.Link(0, 1); g.Link(0, 2);
            Assert.True(double.IsPositiveInfinity(QuadRemesher.SurfaceFaceDeviation(g, m, tree, new[] { 0, 1, 0, 2 })));
        }

        [Fact]
        public void TheOuterLoopOfAnOpenPatch_IsNotBad_ButAFigureOfEightIs()
        {
            // a flat grid of 4 x 4 vertices: its outer loop has 12 vertices and the other orientation of the faces, and is left out
            var g = new QuadRemesher.PolyGraph();
            for (int j = 0; j < 4; ++j) for (int i = 0; i < 4; ++i) g.Add(new Vector3d(i, j, 0), Vector3d.AxisZ, 0);
            for (int j = 0; j < 4; ++j) for (int i = 0; i < 4; ++i) { if (i < 3) g.Link(j * 4 + i, j * 4 + i + 1); if (j < 3) g.Link(j * 4 + i, (j + 1) * 4 + i); }
            Assert.Empty(QuadRemesher.BadLoopVertices(QuadRemesher.CopyGraph(g), 6, null, 32));
            // the K4 on the roof with the tangent plane orders: its two loops are dropped
            Assert.NotEmpty(QuadRemesher.BadLoopVertices(RoofK4(), 6, null, 32));
        }

        [Fact]
        public void TheStillBadVertices_AreComputedWithTheSurfaceOrders()
        {
            DMesh3 m = Roof();
            var g = RoofK4();
            var rot = QuadRemesher.SurfaceRotations(g, m, new[] { 0, 1, 2, 3 });
            Assert.NotEmpty(QuadRemesher.BadLoopVertices(QuadRemesher.CopyGraph(g), 6, null, 32));
            Assert.Empty(QuadRemesher.StillBadVertices(g, 6, rot));
        }

        [Fact]
        public void TheOrdersOfTheEndsOfARemovedLink_AreComputedAgain_NotKept()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = CrossingOverTheRidgeK4();
            var all = new[] { 0, 1, 2, 3 };
            var rot = QuadRemesher.SurfaceRotations(g, m, all, tree);
            g.Unlink(1, 3);
            var rot2 = QuadRemesher.RotationsAfterRemoval(g, new[] { 1, 3 }, m, tree, rot);
            Assert.Equal(g.Adj[1].OrderBy(x => x), rot2[1].OrderBy(x => x));          // two neighbours, not the three of before
            Assert.Equal(2, rot2[1].Length); Assert.Equal(2, rot2[3].Length);
            Assert.Equal(rot[0], rot2[0]); Assert.Equal(rot[2], rot2[2]);               // the others stay
            // a vertex whose neighbour cannot be reached gets no order (not the old one, which no longer fits)
            var stale = new Dictionary<int, int[]> { [3] = new[] { 0, 1, 2 }, [0] = rot[0] };
            var sheets = TwoSheets(); var tree2 = new DMeshAABBTree3(sheets, true);
            var rot3 = QuadRemesher.RotationsAfterRemoval(g, new[] { 3 }, sheets, tree2, stale);
            Assert.False(rot3.ContainsKey(3));
            Assert.Equal(rot[0], rot3[0]);
        }

        static DMesh3 TwoSheets()
        {
            var m = new DMesh3();
            foreach (int side in new[] { -1, 1 })
                for (int i = 0; i < 7; ++i) for (int j = 0; j < 7; ++j) { double x = side * 0.5 * i; m.AppendVertex(new Vector3d(x, -1 + 0.5 * j, -Math.Abs(x))); }
            for (int side = 0; side < 2; ++side)
                for (int i = 0; i < 6; ++i) for (int j = 0; j < 6; ++j) {
                    int o = side * 49, a = o + i * 7 + j, b = o + (i + 1) * 7 + j, c = o + (i + 1) * 7 + j + 1, d = o + i * 7 + j + 1;
                    if (side == 0) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); } else { m.AppendTriangle(a, c, b); m.AppendTriangle(a, d, c); }
                }
            return m;
        }

        [Fact]
        public void TheRescueAppliesToTheFacesOfTheGraphWithoutTheRemovedLinks()
        {
            // the sliver K4 (its face 0 1 2 is a face only with the rescue) and a link between two other vertices, which is removed
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = SliverK4();
            Vector3d nl = new Vector3d(-1, 0, 1).Normalized;
            g.Add(new Vector3d(-2, 1.5, -2), nl, 0); g.Add(new Vector3d(-1.5, 1.5, -1.5), nl, 0); g.Link(4, 5);
            var rot = QuadRemesher.SurfaceRotations(g, m, new[] { 0, 1, 2, 3 }, tree);
            var links = new[] { Tuple.Create(4, 5) };
            int withRescue, withoutRescue;
            DPolyMesh a = QuadRemesher.RebuildWithoutLinks(g, links, m, tree, 6, rot, out withRescue);
            var c = QuadRemesher.CopyGraph(g); c.Unlink(4, 5);
            DPolyMesh b = QuadRemesher.BuildFaces(c, 6, out withoutRescue, QuadRemesher.RotationsAfterRemoval(c, new[] { 4, 5 }, m, tree, rot));
            Assert.Equal(2, b.FaceCount); Assert.Equal(2, withoutRescue);
            Assert.Equal(3, a.FaceCount); Assert.Equal(1, withRescue);                // the sliver is a face only with the rescue
        }

        [Fact]
        public void ALinkInConvexPositionK4_IsFoundWhereTheLongerDiagonalCrossesTheOther()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = new QuadRemesher.PolyGraph();
            Vector3d nl = new Vector3d(-1, 0, 1).Normalized, nr = new Vector3d(1, 0, 1).Normalized;
            g.Add(new Vector3d(-2, -1, -2), nl, 0); g.Add(new Vector3d(0.5, 1, -0.5), nr, 0); g.Add(new Vector3d(1, 1.5, -1), nr, 0); g.Add(new Vector3d(-1.5, -0.5, -1.5), nl, 0);
            for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) g.Link(i, j);
            Assert.Equal(new[] { Tuple.Create(0, 2) }, QuadRemesher.SurfaceCrossings(g, m, tree, new[] { 0, 1, 2, 3 }).ToArray());
        }

        [Fact]
        public void OfCrossingLinks_TheLongerGoes_ThenTheShorterOnesThatCrossedIt_StayIfTheyOnlyCrossedARemovedLink()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            // K (0-1, a link of a chain) crosses I (2-3), and J (4-5) crosses I but not K; I is the shortest, J longer than I, K the longest
            var g = LeftFaceGraph(new[] { -3.0, -1 }, new[] { -0.5, 2 }, new[] { -1.5, -1 }, new[] { -2.5, 0.5 }, new[] { -0.5, 0.5 }, new[] { -2.0, -1 });
            g.Kind[0] = 1; g.Kind[1] = 1;
            g.Link(0, 1); g.Link(2, 3); g.Link(4, 5);
            var found = QuadRemesher.SurfaceCrossings(g, m, tree, Enumerable.Range(0, 6));
            // K is the longer of (K, I) but a chain link: I goes. J crosses only I, which is gone: J stays
            Assert.Equal(new[] { Tuple.Create(2, 3) }, found.ToArray());
        }

        [Fact]
        public void OfTwoCrossingLinksOfTheSameLength_TheOneWithTheLowerNumbersGoes()
        {
            DMesh3 m = Roof(); var tree = new DMeshAABBTree3(m, true);
            var g = new QuadRemesher.PolyGraph();
            Vector3d nl = new Vector3d(-1, 0, 1).Normalized, nr = new Vector3d(1, 0, 1).Normalized;
            g.Add(new Vector3d(-1, 0, -1), nl, 0); g.Add(new Vector3d(1, 1, -1), nr, 0); g.Add(new Vector3d(-1, 1, -1), nl, 0); g.Add(new Vector3d(1, 0, -1), nr, 0);
            g.Link(0, 1); g.Link(2, 3);
            Assert.Equal(new[] { Tuple.Create(0, 1) }, QuadRemesher.SurfaceCrossings(g, m, tree, new[] { 0, 1, 2, 3 }).ToArray());
        }
    }
}
