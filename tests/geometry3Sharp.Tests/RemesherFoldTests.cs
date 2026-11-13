using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue #790: with Remesher.MinTriangleQuality 0.2 a clean torus ended with 6 self-intersecting pairs. The
    /// collapses created folds (edge-adjacent triangles facing more than 90 degrees apart) without flipping any triangle:
    /// the flip test compares a changed triangle only with its own old normal, so one that already stood at 50-80
    /// degrees to a neighbour could turn a further 30-60 degrees. The next projection pushed such folds through each
    /// other. The remesher now rejects a collapse that folds two edge-adjacent triangles that were not folded before
    /// (Remesher.collapse_creates_fold), on the MinTriangleQuality path only.
    /// </summary>
    public class RemesherFoldTests
    {
        // the torus of the issue: R 1, r 0.3, a 160 x 48 parametric grid, rotated so that no symmetry lines up with the axes
        static DMesh3 Torus(int nu = 160, int nv = 48)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    m.AppendVertex(new Vector3d((1 + 0.3 * Math.Cos(v)) * Math.Cos(u), (1 + 0.3 * Math.Cos(v)) * Math.Sin(u), 0.3 * Math.Sin(v)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int a = i * nv + j, b = ((i + 1) % nu) * nv + j, c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                    m.AppendTriangle(a, b, c);
                    m.AppendTriangle(a, c, d);
                }
            var q = new Quaterniond(new Vector3d(0.3, 0.7, 0.2).Normalized, 23.7);
            foreach (int v in m.VertexIndices())
                m.SetVertex(v, q * m.GetVertex(v));
            return m;
        }

        static Remesher Remesher(DMesh3 mesh, DMesh3 input, double edge)
        {
            var r = new Remesher(mesh);
            r.SetTargetEdgeLength(edge);
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            r.MinTriangleQuality = 0.2;
            return r;
        }

        static DMesh3 Remesh(DMesh3 input, double edge, int passes = 15)
        {
            var mesh = new DMesh3(input);
            Remesher r = Remesher(mesh, input, edge);
            for (int k = 0; k < passes; ++k)
                r.BasicRemeshPass();
            return mesh;
        }

        static double Q(DMesh3 m, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            return MeshRefinerBase.TriangleQuality(ref a, ref b, ref c);
        }

        // the result checks: no self-intersections, closed, no bowties, consistently oriented, genus 1 (V - E + F = 0),
        // finite, no flat triangles, and within a fraction of the target of the input
        static void AssertGood(DMesh3 input, DMesh3 m, double edge, string name)
        {
            int pairs = MeshRepairMetrics.SelfIntersections(m);
            Assert.True(pairs == 0, $"{name}: {pairs} self-intersecting pairs");
            Assert.True(m.IsClosed(), name);
            Assert.Empty(MeshIterators.BowtieVertices(m));
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(m));
            Assert.Equal(0, m.VertexCount - m.EdgeCount + m.TriangleCount);
            Assert.True(m.Vertices().All(p => p.IsFinite), name);
            double qmin = m.TriangleIndices().Min(t => Q(m, t));
            Assert.True(qmin >= 1e-3, $"{name}: a triangle of quality {qmin}");
            var hd = new MeshHausdorffDistance(input, m) { Tolerance = 0.01 * edge };
            Assert.True(hd.Compute());
            Assert.True(hd.UpperBound < 0.3 * edge, $"{name}: {hd.UpperBound / edge} target lengths from the input");
        }

        static double Edge(DMesh3 input) => 0.02 * input.CachedBounds.Diagonal.Length;

        // master: 6 self-intersecting pairs (all from folds made by collapses, pushed through by the projection)
        [Fact]
        public void Torus_NoSelfIntersections()
        {
            DMesh3 input = Torus();
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(input));
            double edge = Edge(input);
            AssertGood(input, Remesh(input, edge), edge, "torus");
        }

        // one instance, two runs of passes (the second continues from the first): the guarantees hold after both
        [Fact]
        public void Torus_ReuseInstance()
        {
            DMesh3 input = Torus();
            double edge = Edge(input);
            var mesh = new DMesh3(input);
            Remesher r = Remesher(mesh, input, edge);
            for (int run = 0; run < 2; ++run) {
                for (int k = 0; k < 15; ++k)
                    r.BasicRemeshPass();
                AssertGood(input, mesh, edge, $"torus, run {run}");
            }
        }

        // a smaller torus (80 x 24) for the similarities: 2^k scales give the same result bit for bit; under rotations
        // and translations rounding changes which operations are taken, so the result is checked, not compared
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Torus_SameResultUnderSimilarity(string similarity)
        {
            Similarity s = Similarity.ByName(similarity);
            DMesh3 input = Torus(80, 24);
            double edge = Edge(input);
            DMesh3 m0 = Remesh(input, edge);
            DMesh3 back = Invariance.MappedBack(Remesh(Invariance.Transformed(input, s), s.ApplyLength(edge)), s);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(m0, back));
            AssertGood(input, back, edge, $"torus 80 x 24, {s.Name}");
        }

        // hand-made collapse of a (valence 3) onto b: T = (a, x, y) becomes (b, x, y), turning by 31 degrees (no flip);
        // U = (y, x, u) across the link edge x-y stands at 80 degrees to T before. Returns the fold decision for that
        // collapse, with all coordinates scaled by scale
        static bool CollapseFolds(Vector3d u, Vector3d b, double scale = 1)
        {
            var m = new DMesh3();
            int x = m.AppendVertex(scale * new Vector3d(0, 0, 0)), y = m.AppendVertex(scale * new Vector3d(1, 0, 0));
            int a = m.AppendVertex(scale * new Vector3d(0.5, 1, 0)), vb = m.AppendVertex(scale * b), vu = m.AppendVertex(scale * u);
            m.AppendTriangle(a, x, y);
            int t0 = m.AppendTriangle(a, y, vb), t1 = m.AppendTriangle(a, vb, x);
            m.AppendTriangle(y, x, vu);
            var r = new Remesher(m) { MinTriangleQuality = 0.2 };
            return r.collapse_creates_fold(a, vb, vb, m.GetVertex(vb), t0, t1);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(0x1p-40)]
        [InlineData(0x1p40)]
        public void CollapseFold_NewFoldRejected(double scale)
        {
            // U at 80 degrees to T; after the collapse 111 degrees
            Assert.True(CollapseFolds(new Vector3d(0.5, -0.17, 0.98), new Vector3d(0.5, 1, 0.6), scale));
            // the same collapse with U continuing T's plane: 31 degrees after it, no fold
            Assert.False(CollapseFolds(new Vector3d(0.5, -1, 0), new Vector3d(0.5, 1, 0.6), scale));
        }

        [Fact]
        public void CollapseFold_ExistingFoldNotBlocked()
        {
            // U already folded against T (100 degrees) and still folded after the collapse: not a new fold
            Assert.False(CollapseFolds(new Vector3d(0.5, 0.17, -0.98), new Vector3d(0.5, 1, -0.3)));
            // a collapse that unfolds it
            Assert.False(CollapseFolds(new Vector3d(0.5, 0.17, -0.98), new Vector3d(0.5, 1, 0.6)));
        }
    }
}
