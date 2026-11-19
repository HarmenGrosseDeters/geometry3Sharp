using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QuadRemesher.CollapseLength defaults to 0.6 (owner 2026-11-19; it was 0.4, docs/design/QUAD_REMESHING.md 4.5): the short edges of the
    /// ragged boundary of an open mesh collapse, the closed shapes hardly change.
    /// </summary>
    public class QuadCollapseDefaultTests
    {
        static QuadRemesher Run(DMesh3 m, double s, Action<QuadRemesher> configure = null)
        {
            var cf = new CrossField(m);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(m) { Field = cf, TargetEdgeLength = s };
            configure?.Invoke(qr);
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            return qr;
        }

        [Fact]
        public void DefaultCollapseLength_Is0Point6()
        {
            Assert.Equal(0.6, new QuadRemesher(CrossFieldShapes.UnitSphere()).CollapseLength);
        }

        [Fact]
        public void OpenPatch_RaggedBoundaryCollapses_ComparedWithTheOldDefault()
        {
            DMesh3 m = CrossFieldShapes.OpenPatch(50);
            QuadRemesher now = Run(m, 0.04), old = Run(m, 0.04, q => q.CollapseLength = 0.4);
            PolyMeshMetrics a = PolyMeshMetrics.Compute(now.Result), b = PolyMeshMetrics.Compute(old.Result);
            Assert.True(a.IsManifold && b.IsManifold);
            Assert.True(now.CollapsedEdges > old.CollapsedEdges, "collapsed " + old.CollapsedEdges + " -> " + now.CollapsedEdges);
            Assert.True(a.IrregularVertices < b.IrregularVertices, "irregular " + b.IrregularVertices + " -> " + a.IrregularVertices);
            Assert.True(a.QuadFraction > b.QuadFraction, "quads " + b.QuadFraction + " -> " + a.QuadFraction);
            Assert.True(a.QuadFraction >= 0.95, "quads " + a.QuadFraction);
        }

        // a narrow open strip of 1 x 0.1 (width 2.5 target edges): boundary vertices of the two sides are close, the wider collapse must not merge them into a broken mesh
        static DMesh3 Strip(int nx, int ny, double width)
        {
            var m = new DMesh3();
            int[,] ids = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i)
                    ids[i, j] = m.AppendVertex(new Vector3d((double)i / nx, width * j / ny, 0.1 * Math.Sin(3.0 * i / nx)));
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            return m;
        }

        // A strip this narrow (2.5 to 4 target edges) gives non-manifold vertices with the old default as well (found by the review of this change, an open item in
        // QUAD_REMESHING.md 4.2): the new default must not make it worse.
        [Theory]
        [InlineData(0.04)]
        [InlineData(0.06)]
        public void NarrowStrip_IsNotWorseThanWithTheOldDefault(double s)
        {
            DMesh3 m = Strip(100, 8, 0.1);
            var cf = new CrossField(m);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            PolyMeshMetrics Run(double collapse)
            {
                var qr = new QuadRemesher(m) { Field = cf, TargetEdgeLength = s, CollapseLength = collapse };
                Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
                Assert.True(qr.Result.Validate().IsValid);
                for (int v = 0; v < qr.Result.VertexCount; ++v) Assert.True(qr.Result.GetVertex(v).IsFinite);
                return PolyMeshMetrics.Compute(qr.Result);
            }
            PolyMeshMetrics now = Run(0.6), old = Run(0.4);
            Assert.True(now.MaxSides <= 6);
            Assert.Equal(0, now.NonManifoldEdges);
            Assert.True(now.NonManifoldVertices <= old.NonManifoldVertices, "non-manifold vertices " + old.NonManifoldVertices + " -> " + now.NonManifoldVertices);
            Assert.True(now.InconsistentEdges <= old.InconsistentEdges);
        }

        [Theory]
        [InlineData("sphere")]
        [InlineData("torus")]
        [InlineData("box")]
        public void ClosedShapes_BarelyChange(string name)
        {
            DMesh3 m = name == "sphere" ? CrossFieldShapes.UnitSphere() : name == "torus" ? CrossFieldShapes.TorusMesh() : CrossFieldShapes.FilletedBoxMesh();
            PolyMeshMetrics a = PolyMeshMetrics.Compute(Run(m, 0.2).Result), b = PolyMeshMetrics.Compute(Run(m, 0.2, q => q.CollapseLength = 0.4).Result);
            Assert.True(a.IsManifold);
            Assert.True(a.IrregularVertices <= b.IrregularVertices + 2, name + ": irregular " + b.IrregularVertices + " -> " + a.IrregularVertices);
            Assert.True(a.QuadFraction >= b.QuadFraction - 0.01, name + ": quads " + b.QuadFraction + " -> " + a.QuadFraction);
        }
    }
}
