using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class DualContouringTests
    {
        // signed distance to a sphere, negative inside
        class SphereSDF : ImplicitFunction3d
        {
            public double Radius = 1;
            public double Value(ref Vector3d pt) { return pt.Length - Radius; }
        }

        static DMesh3 Box(double size)
        {
            var gen = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, size * 0.5 * Vector3d.One) };
            return gen.Generate().MakeDMesh();
        }

        [Fact]
        public void Sphere_IsClosedManifoldAndOrientedOutwards()
        {
            var dc = new DualContouring() {
                Implicit = new SphereSDF(), IsoValue = 0, InsideIsLess = true, CellSize = 0.1,
                Bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.3)
            };
            dc.Generate();
            DMesh3 mesh = dc.Mesh;

            Assert.Equal(0, dc.NonManifoldTriangles);
            Assert.True(mesh.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(mesh));
            // every triangle faces away from the center (checks the per-quad orientation, not just the global flip)
            foreach (int tid in mesh.TriangleIndices())
                Assert.True(mesh.GetTriNormal(tid).Dot(mesh.GetTriCentroid(tid)) > 0);
            foreach (int vid in mesh.VertexIndices())
                Assert.InRange(mesh.GetVertex(vid).Length, 0.98, 1.02);
        }

        [Fact]
        public void SelfIntersectionFree_OnlyIfChecked()
        {
            // a sphere has no self-intersections: known after the resolve pass, unknown without it
            Func<bool, DualContouring> run = (resolve) => {
                var dc = new DualContouring() {
                    Implicit = new SphereSDF(), IsoValue = 0, InsideIsLess = true, CellSize = 0.1,
                    Bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.3), ResolveSelfIntersections = resolve
                };
                dc.Generate();
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(dc.Mesh));
                return dc;
            };
            Assert.True(run(true).SelfIntersectionFree);
            Assert.False(run(false).SelfIntersectionFree);
            // set only by a pass that found nothing: no pass (as when the rounds run out with pairs left)
            var none = new DualContouring() {
                Implicit = new SphereSDF(), IsoValue = 0, InsideIsLess = true, CellSize = 0.1,
                Bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.3), MaxResolveRounds = 0
            };
            none.Generate();
            Assert.False(none.SelfIntersectionFree);
        }

        [Fact]
        public void Sparse_IncompleteCandidateCells_StillClosed()
        {
            // the candidate cells only need to touch the surface somewhere: cells around crossing edges are
            // added until the surface is complete (an octree can miss cells, which used to leave holes)
            var sdf = new SphereSDF();
            var bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.3);
            double cell = 0.1;
            var dc = new DualContouring() {
                Implicit = sdf, IsoValue = 0, InsideIsLess = true, CellSize = cell, Bounds = bounds,
                CandidateCells = new[] { new Vector3i(22, 13, 13) },      // the cell around (1, 0, 0)
                NodeValueF = (n) => { Vector3d p = bounds.Min + cell * new Vector3d(n.x, n.y, n.z); return sdf.Value(ref p); }
            };
            dc.Generate();
            DMesh3 mesh = dc.Mesh;
            Assert.True(mesh.TriangleCount > 500);
            Assert.True(mesh.IsClosed());
            foreach (int vid in mesh.VertexIndices())
                Assert.InRange(mesh.GetVertex(vid).Length, 0.98, 1.02);
        }

        [Fact]
        public void Solidify_Box_KeepsSharpCorners_MarchingCubesDoesNot()
        {
            DMesh3 box = Box(10);
            Vector3d[] corners = new Box3d(Vector3d.Zero, 5 * Vector3d.One).ComputeVertices();
            Func<DMesh3, double> maxCornerError = (m) =>
                corners.Max(c => m.VertexIndices().Min(v => m.GetVertex(v).Distance(c)));

            var dc = new MeshWindingNumberSolidify(box) { GridResolution = 32, Method = MeshWindingNumberSolidify.Methods.DualContouring };
            Assert.True(dc.Compute());
            var mc = new MeshWindingNumberSolidify(box) { GridResolution = 32, Method = MeshWindingNumberSolidify.Methods.MarchingCubes };
            Assert.True(mc.Compute());

            Assert.True(dc.Result.IsClosed());
            Assert.Equal(0, dc.NonManifoldTriangles);
            double volume = MeshMeasurements.VolumeArea(dc.Result, dc.Result.TriangleIndices(), dc.Result.GetVertex).x;
            Assert.InRange(volume, 990, 1010);

            double cell = box.CachedBounds.DiagonalLength / 32;
            Assert.True(maxCornerError(dc.Result) < 0.01 * cell, "dual contouring should reproduce the corners");
            Assert.True(maxCornerError(mc.Result) > 0.1 * cell, "marching cubes rounds the corners");
            Assert.True(dc.Result.TriangleCount < mc.Result.TriangleCount / 3);
        }

        // two boxes almost touching along an edge: cells there have diagonal inside corners (ambiguous faces).
        // Plain dual contouring puts one vertex in such a cell and pinches the boxes together with
        // non-manifold edges; manifold dual contouring gives each box its own vertex.
        [Fact]
        public void Solidify_BoxesTouchingDiagonally_StayTwoClosedParts()
        {
            DMesh3 mesh = Box(10);
            DMesh3 second = Box(10);
            double cell = 0.5;
            MeshTransforms.Translate(second, new Vector3d(10 + 0.3 * cell, 10 + 0.3 * cell, 0));
            MeshEditor.Append(mesh, second);

            var solid = new MeshWindingNumberSolidify(mesh) { CellSize = cell };
            Assert.True(solid.Compute());
            DMesh3 result = solid.Result;

            Assert.Equal(0, solid.NonManifoldTriangles);
            Assert.True(result.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(result));
            var comps = new MeshConnectedComponents(result);
            comps.FindConnectedT();
            Assert.Equal(2, comps.Count);
            double volume = MeshMeasurements.VolumeArea(result, result.TriangleIndices(), result.GetVertex).x;
            Assert.InRange(volume, 1990, 2010);
        }

        // exact, also between neighbours (touching only counts too)
        static int SelfIntersections(DMesh3 m)
        {
            return MeshRepairMetrics.SelfIntersections(m);
        }

        // two boxes with a gap narrower than a cell, rotated off the grid axes
        static DMesh3 BoxesWithGap(double gap)
        {
            DMesh3 mesh = Box(5);
            MeshTransforms.Scale(mesh, new Vector3d(1, 0.5, 1), Vector3d.Zero);   // 5 x 2.5 x 5, y in [-1.25, 1.25]
            DMesh3 second = new DMesh3(mesh);
            MeshTransforms.Translate(second, new Vector3d(0, 2.5 + gap, 0));
            MeshEditor.Append(mesh, second);
            MeshTransforms.Rotate(mesh, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 27));
            return mesh;
        }

        // Grid edges with both ends inside the solid can still cross a gap. The cells there merge both sides
        // and their vertices end up between the surfaces, which then cut through each other. Such hidden
        // gaps are detected and reported, and with MaxRefineLevels the cell size is halved until they are gone.
        [Theory]
        [InlineData(0.45)]
        [InlineData(0.9)]
        public void Solidify_NarrowGap_RefinesUntilNoSelfIntersections(double gapCells)
        {
            double cell = 0.25;
            DMesh3 mesh = BoxesWithGap(gapCells * cell);

            var plain = new MeshWindingNumberSolidify(mesh) { CellSize = cell, Adaptive = true, ReduceTolerance = 0, MaxRefineLevels = 0 };
            Assert.True(plain.Compute());
            Assert.True(plain.HiddenGapEdges > 0);
            Assert.True(SelfIntersections(plain.Result) > 0);

            var refined = new MeshWindingNumberSolidify(mesh) { CellSize = cell, Adaptive = true, ReduceTolerance = 0, MaxRefineLevels = 3 };
            Assert.True(refined.Compute());
            Assert.True(refined.UsedCellSize < cell);
            Assert.Equal(0, refined.HiddenGapEdges);
            Assert.True(refined.Result.IsClosed());
            Assert.Equal(0, SelfIntersections(refined.Result));
            var comps = new MeshConnectedComponents(refined.Result);
            comps.FindConnectedT();
            Assert.Equal(2, comps.Count);   // the gap is kept
        }

        [Fact]
        public void Solidify_NoHiddenGaps_NoRefinement()
        {
            var solid = new MeshWindingNumberSolidify(Box(10)) { CellSize = 0.5, Adaptive = true, MaxRefineLevels = 3 };
            Assert.True(solid.Compute());
            Assert.Equal(0, solid.HiddenGapEdges);
            Assert.Equal(0.5, solid.UsedCellSize);
        }

        [Fact]
        public void Solidify_ConcaveCreasesOfOverlappingParts_AreNotGaps()
        {
            // where two overlapping parts meet, grid edges can cross the thin outside wedge at the concave
            // crease; that is not a gap and must not trigger refinement
            DMesh3 mesh = Box(8);
            MeshTransforms.Rotate(mesh, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 33));
            DMesh3 second = Box(8);
            MeshTransforms.Translate(second, new Vector3d(3, 3, 3));
            MeshEditor.Append(mesh, second);
            var solid = new MeshWindingNumberSolidify(mesh) { GridResolution = 96, Adaptive = true, MaxRefineLevels = 2 };
            Assert.True(solid.Compute());
            Assert.Equal(0, solid.HiddenGapEdges);
            Assert.Equal(mesh.CachedBounds.DiagonalLength / 96, solid.UsedCellSize, 9);
        }

        [Fact]
        public void Solidify_BrokenBox_IsClosedAndRemovesInteriorShell()
        {
            // box with a missing triangle and a smaller box inside it
            DMesh3 mesh = Box(10);
            mesh.RemoveTriangle(mesh.TriangleIndices().First(), false, false);
            MeshEditor.Append(mesh, Box(4));
            Assert.False(mesh.IsClosed());

            var solid = new MeshWindingNumberSolidify(mesh) { GridResolution = 48 };
            Assert.True(solid.Compute());
            DMesh3 result = solid.Result;

            Assert.True(result.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(result));
            var comps = new MeshConnectedComponents(result);
            comps.FindConnectedT();
            Assert.Equal(1, comps.Count);
            double volume = MeshMeasurements.VolumeArea(result, result.TriangleIndices(), result.GetVertex).x;
            Assert.InRange(volume, 980, 1020);
        }
    }
}
