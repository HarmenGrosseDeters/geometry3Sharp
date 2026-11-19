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
