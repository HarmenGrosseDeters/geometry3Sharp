using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class AdaptiveOctreeTests
    {
        static DMesh3 Box(double size, Vector3d center = default(Vector3d))
        {
            var gen = new TrivialBox3Generator() { Box = new Box3d(center, size * 0.5 * Vector3d.One) };
            return gen.Generate().MakeDMesh();
        }

        static double Volume(DMesh3 mesh)
        {
            return MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
        }

        static WindingNumberOctree BuildOctree(DMesh3 mesh, int depth)
        {
            var spatial = new DMeshAABBTree3(mesh, true);
            var octree = new WindingNumberOctree(mesh, spatial) { MaxDepth = depth };
            octree.Build();
            return octree;
        }

        // signed distance to a sphere, negative inside
        class SphereSDF : ImplicitFunction3d
        {
            public double Radius = 1;
            public double Value(ref Vector3d pt) { return pt.Length - Radius; }
        }


        [Fact]
        public void Octree_RefinesOnlyNearSurface()
        {
            DMesh3 box = Box(10);
            WindingNumberOctree octree = BuildOctree(box, 6);
            int leafCount = 1 << 6;

            Assert.NotEmpty(octree.SurfaceCells);
            // at most two cell layers per face (faces lying exactly on cell boundaries touch both layers),
            // far below the ~80% of all cells the box volume covers
            double faceCells = 6 * Math.Pow(10 / octree.LeafCellSize, 2);
            Assert.True(octree.SurfaceCells.Count <= 2.05 * faceCells, $"{octree.SurfaceCells.Count} surface cells");

            // every leaf cell whose box straddles the surface must be a surface cell
            var surfaceCells = new HashSet<Vector3i>(octree.SurfaceCells);
            double h = octree.LeafCellSize;
            for (int k = 0; k < leafCount; ++k)
                for (int j = 0; j < leafCount; ++j)
                    for (int i = 0; i < leafCount; ++i) {
                        Vector3d min = octree.Origin + h * new Vector3d(i, j, k);
                        var cellBox = new AxisAlignedBox3d(min, min + h * Vector3d.One);
                        bool straddles = cellBox.Intersects(new AxisAlignedBox3d(5)) && !new AxisAlignedBox3d(5).Contains(cellBox);
                        if (straddles)
                            Assert.Contains(new Vector3i(i, j, k), surfaceCells);
                    }
        }

        [Fact]
        public void Octree_WindingEvaluations_ScaleWithSurfaceNotVolume()
        {
            DMesh3 box = Box(10);
            int e6 = BuildOctree(box, 6).WindingEvaluations;
            int e7 = BuildOctree(box, 7).WindingEvaluations;
            double ratio = (double)e7 / e6;
            // one level finer: surface cells x4, volume cells x8
            Assert.InRange(ratio, 3.0, 5.5);
        }

        [Fact]
        public void Octree_NodeValues_AreWindingNumbers()
        {
            DMesh3 box = Box(10);
            WindingNumberOctree octree = BuildOctree(box, 5);
            foreach (Vector3i cell in octree.SurfaceCells.Take(50)) {
                Vector3d p = octree.Origin + octree.LeafCellSize * new Vector3d(cell.x, cell.y, cell.z);
                bool inside = Math.Abs(p.x) < 5 && Math.Abs(p.y) < 5 && Math.Abs(p.z) < 5;
                Assert.Equal(inside, octree.NodeValue(cell) > 0.5);
            }
        }

        // across a grid edge that no triangle crosses, the winding number of a closed surface does not change,
        // so most node values can be copied from a neighbour instead of evaluated
        [Fact]
        public void Octree_ClosedMesh_PropagatesMostNodeValues()
        {
            DMesh3 box = Box(10);
            WindingNumberOctree octree = BuildOctree(box, 7);
            // the leaf corners dual contouring needs
            foreach (Vector3i cell in octree.SurfaceCells)
                for (int c = 0; c < 8; ++c)
                    octree.NodeValue(cell + new Vector3i(c & 1, (c >> 1) & 1, (c >> 2) & 1));

            Assert.True(octree.NodeCount > 0);
            Assert.True(octree.WindingEvaluations < 0.25 * octree.NodeCount,
                $"{octree.WindingEvaluations} evaluations for {octree.NodeCount} nodes");
            // and the values are right
            foreach (Vector3i cell in octree.SurfaceCells)
                for (int c = 0; c < 8; ++c) {
                    Vector3i node = cell + new Vector3i(c & 1, (c >> 1) & 1, (c >> 2) & 1);
                    Vector3d p = octree.Origin + octree.LeafCellSize * new Vector3d(node.x, node.y, node.z);
                    double m = Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z)));
                    if (Math.Abs(m - 5) < 1e-6)
                        continue;   // node on a face, either side is fine
                    Assert.Equal(m < 5, octree.NodeValue(node) > 0.5);
                }
        }

        // near holes the winding number varies continuously and can cross 0.5 without crossing a triangle,
        // so values must not be copied there
        [Fact]
        public void Octree_OpenMesh_NodeValuesNearHoleAreEvaluated()
        {
            DMesh3 box = Box(10);
            foreach (int tid in box.TriangleIndices().ToArray())
                if (box.GetTriCentroid(tid).z > 4.99)
                    box.RemoveTriangle(tid, false, false);
            WindingNumberOctree octree = BuildOctree(box, 6);
            var spatial = new DMeshAABBTree3(box, true);
            int wrong = 0, total = 0;
            foreach (Vector3i cell in octree.SurfaceCells)
                for (int c = 0; c < 8; ++c) {
                    Vector3i node = cell + new Vector3i(c & 1, (c >> 1) & 1, (c >> 2) & 1);
                    Vector3d p = octree.Origin + octree.LeafCellSize * new Vector3d(node.x, node.y, node.z);
                    double w = spatial.FastWindingNumber(p);
                    if (Math.Abs(w - 0.5) < 1e-4)
                        continue;   // node on the hole's iso-surface, either side is fine
                    bool exact = w > 0.5;
                    total++;
                    if (exact != (octree.NodeValue(node) > 0.5)) wrong++;
                }
            Assert.True(total > 0);
            Assert.Equal(0, wrong);
        }

        // a closed mesh with inconsistent orientation (here: the top face flipped) has no boundary edges,
        // but its edges between differently oriented triangles act like a boundary: the winding number is
        // not piecewise constant there, so values must not be copied either
        [Fact]
        public void Octree_ClosedButInconsistentlyOrientedMesh_NodeValuesAreRight()
        {
            DMesh3 box = Box(10);
            foreach (int tid in box.TriangleIndices().ToArray())
                if (box.GetTriCentroid(tid).z > 4.99)
                    box.ReverseTriOrientation(tid);
            Assert.True(box.IsClosed());
            WindingNumberOctree octree = BuildOctree(box, 6);
            var spatial = new DMeshAABBTree3(box, true);
            int wrong = 0, total = 0;
            foreach (Vector3i cell in octree.SurfaceCells)
                for (int c = 0; c < 8; ++c) {
                    Vector3i node = cell + new Vector3i(c & 1, (c >> 1) & 1, (c >> 2) & 1);
                    Vector3d p = octree.Origin + octree.LeafCellSize * new Vector3d(node.x, node.y, node.z);
                    double w = spatial.FastWindingNumber(p);
                    if (Math.Abs(w - 0.5) < 1e-4)
                        continue;
                    total++;
                    if ((w > 0.5) != (octree.NodeValue(node) > 0.5)) wrong++;
                }
            Assert.True(total > 0);
            Assert.Equal(0, wrong);
        }

        [Fact]
        public void SparseDualContouring_MatchesDense()
        {
            var sdf = new SphereSDF();
            var bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.3);
            double cell = 0.1;
            var dense = new DualContouring() { Implicit = sdf, InsideIsLess = true, CellSize = cell, Bounds = bounds };
            dense.Generate();

            // candidate cells: all cells within one cell of the surface
            var candidates = new List<Vector3i>();
            int n = (int)Math.Ceiling(bounds.Width / cell);
            for (int k = 0; k < n; ++k)
                for (int j = 0; j < n; ++j)
                    for (int i = 0; i < n; ++i) {
                        Vector3d c = bounds.Min + cell * new Vector3d(i + 0.5, j + 0.5, k + 0.5);
                        if (Math.Abs(c.Length - 1) < 1.5 * cell)
                            candidates.Add(new Vector3i(i, j, k));
                    }
            int nodeEvaluations = 0;
            var sparse = new DualContouring() {
                Implicit = sdf, InsideIsLess = true, CellSize = cell, Bounds = bounds,
                CandidateCells = candidates,
                NodeValueF = (ijk) => {
                    System.Threading.Interlocked.Increment(ref nodeEvaluations);
                    Vector3d p = bounds.Min + cell * new Vector3d(ijk.x, ijk.y, ijk.z);
                    return sdf.Value(ref p);
                }
            };
            sparse.Generate();

            // only nodes around the candidate cells are looked at
            int denseNodes = (n + 1) * (n + 1) * (n + 1);
            Assert.InRange(nodeEvaluations, 1, denseNodes / 2);

            Assert.Equal(dense.Mesh.TriangleCount, sparse.Mesh.TriangleCount);
            Assert.Equal(dense.Mesh.VertexCount, sparse.Mesh.VertexCount);
            Assert.True(sparse.Mesh.IsClosed());
            double volDense = Volume(dense.Mesh), volSparse = Volume(sparse.Mesh);
            Assert.InRange(volSparse, volDense - 1e-9, volDense + 1e-9);
        }

        [Fact]
        public void AdaptiveSolidify_Box_IsExactAndClosed()
        {
            DMesh3 box = Box(10);
            var solid = new MeshWindingNumberSolidify(box) { Adaptive = true, GridResolution = 64 };
            Assert.True(solid.Compute());
            DMesh3 result = solid.Result;
            Assert.True(solid.WindingEvaluations > 0);

            Assert.True(result.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(result));
            Assert.InRange(Volume(result), 999.5, 1000.5);
            Vector3d[] corners = new Box3d(Vector3d.Zero, 5 * Vector3d.One).ComputeVertices();
            double cell = box.CachedBounds.DiagonalLength / 64;
            foreach (Vector3d c in corners)
                Assert.True(result.VertexIndices().Min(v => result.GetVertex(v).Distance(c)) < 0.01 * cell);
        }

        [Fact]
        public void AdaptiveSolidify_OpenBox_SpansHole()
        {
            // box without its top face: the 0.5 iso-surface spans the opening
            DMesh3 box = Box(10);
            foreach (int tid in box.TriangleIndices().ToArray())
                if (box.GetTriCentroid(tid).z > 4.99)
                    box.RemoveTriangle(tid, false, false);
            Assert.False(box.IsClosed());

            var solid = new MeshWindingNumberSolidify(box) { Adaptive = true, GridResolution = 64 };
            Assert.True(solid.Compute());

            Assert.True(solid.WindingEvaluations > 0);
            Assert.True(solid.Result.IsClosed());
            Assert.InRange(Volume(solid.Result), 980, 1020);
        }

        [Fact]
        public void AdaptiveSolidify_SameAsUniform_WithFarFewerEvaluations()
        {
            // a box with a thin fin, thin enough to need fine cells
            DMesh3 mesh = Box(10);
            var fin = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(7, 0, 0), new Vector3d(2, 3, 0.25)) }.Generate().MakeDMesh();
            MeshEditor.Append(mesh, fin);
            double finVolume = 4 * 6 * 0.5;

            var uniform = new MeshWindingNumberSolidify(mesh) { GridResolution = 192 };
            Assert.True(uniform.Compute());
            var adaptive = new MeshWindingNumberSolidify(mesh) { GridResolution = 192, Adaptive = true };
            Assert.True(adaptive.Compute());

            double expected = 1000 + finVolume;
            Assert.InRange(Volume(uniform.Result), expected - 1, expected + 1);
            Assert.InRange(Volume(adaptive.Result), expected - 1, expected + 1);
            Assert.True(adaptive.Result.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(adaptive.Result));

            // the uniform grid evaluates the winding number at every node of its narrow band
            // and the propagation front; the octree only along the surface
            Assert.True(adaptive.WindingEvaluations > 0);
            Assert.True(adaptive.WindingEvaluations < 0.5 * uniform.WindingEvaluations,
                $"adaptive {adaptive.WindingEvaluations} vs uniform {uniform.WindingEvaluations}");
        }
    }
}
