using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Sparse mode and local refinement build node positions as origin + h * index like the dense grid: a cell that reaches a node
    /// that is not a finite number is rejected up front (#1589 follow-up; the dense grid is checked since #1625), instead of handing an
    /// infinite corner to ExactPredicates.Orient2d of the face vertices.
    /// </summary>
    public class DualContouringSparseFiniteNodesTests
    {
        static DualContouring Sparse(Vector3d min, double cell, params Vector3i[] cells)
        {
            return new DualContouring {
                Implicit = null, InsideIsLess = true, CellSize = cell, ParallelCompute = false,
                Bounds = new AxisAlignedBox3d(min, min + new Vector3d(1, 1, 1)),
                CandidateCells = new List<Vector3i>(cells),
                NodeValueF = v => (new Vector3d(v.x, v.y, v.z) - new Vector3d(1.5, 1.5, 1.5)).Length - 1.2
            };
        }

        [Fact]
        public void SparseCells_ReachingAnInfiniteNode_AreRejected()
        {
            // 1.7e308 + 1e307 * 3 overflows
            var dc = Sparse(new Vector3d(1.7e308, 0, 0), 1e307, new Vector3i(2, 0, 0));
            var ex = Assert.Throws<ArgumentException>(() => dc.Generate());
            Assert.Contains("not a finite number", ex.Message);
        }

        [Fact]
        public void SparseCells_WithRefinement_ReachingAnInfiniteNode_AreRejected()
        {
            var dc = Sparse(new Vector3d(0, 0, 0), 1e307, new Vector3i(0, 0, 0));
            // fine candidate cells far out: index 1e9 times h = 1e307 / 4 overflows
            dc.SetRefinement(4, new[] { new Vector3i(0, 0, 0) }, new[] { new Vector3i(2000000000, 0, 0) }, v => 1.0);
            Assert.Throws<ArgumentException>(() => dc.Generate());
        }

        [Fact]
        public void OrdinarySparseGrid_IsNotRejected_AndGivesTheDenseSurface()
        {
            var cells = new List<Vector3i>();
            for (int i = 0; i < 4; ++i) for (int j = 0; j < 4; ++j) for (int k = 0; k < 4; ++k) cells.Add(new Vector3i(i, j, k));
            var dc = new DualContouring {
                Implicit = new ImplicitSphere3d { Origin = new Vector3d(2, 2, 2), Radius = 1.3 }, InsideIsLess = true, CellSize = 1, ParallelCompute = false, Bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(4, 4, 4)),
                CandidateCells = cells, NodeValueF = v => (new Vector3d(v.x, v.y, v.z) - new Vector3d(2, 2, 2)).Length - 1.3
            };
            dc.Generate();
            Assert.True(dc.Mesh.TriangleCount > 0);
            Assert.True(dc.Mesh.IsClosed());
        }
    }
}
