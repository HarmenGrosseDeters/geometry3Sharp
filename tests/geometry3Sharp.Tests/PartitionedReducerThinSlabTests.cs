using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// PartitionedReduction cut a mesh into 8 slabs at the quantiles of the vertex coordinates, each shrunk by half a gap (one mean
    /// edge length) on both sides. On a small or coarse mesh, or one with many vertices in a plane, two cuts lie closer together than
    /// the gap: the shrunk slab is then empty (min above max on the axis), but AxisAlignedBox3d(min, max) orders its corners, so the
    /// empty slab became a box between the two cuts, overlapping its neighbours. A triangle belonged to the first of them, the
    /// triangles across its edges to the next one, both regions reduced the vertices on that edge, and the stitched mesh was open:
    /// a closed skewed tetrahedron subdivided three times (256 triangles) gave an open mesh of 14 triangles (the serial Reducer:
    /// 4, closed). Found by the mesh fuzz pilot (T2, tools/MeshFuzzPilot, partitioned-reducer).
    /// The power of two contract of the class also holds for the repaired case; a subnormal coordinate (5e-312 next to
    /// coordinates of 1) breaks it because the quadric terms underflow differently at the two scales: outside the contract.
    /// </summary>
    public class PartitionedReducerThinSlabTests
    {
        // one midpoint subdivision (every triangle into four, vertices of shared edges shared)
        static DMesh3 Subdivide(DMesh3 m)
        {
            var r = new DMesh3();
            var map = new Dictionary<int, int>();
            foreach (int v in m.VertexIndices()) map[v] = r.AppendVertex(m.GetVertex(v));
            var mid = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!mid.TryGetValue(key, out int id)) mid[key] = id = r.AppendVertex(0.5 * m.GetVertex(a) + 0.5 * m.GetVertex(b));
                return id;
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                int a = map[f.a], b = map[f.b], c = map[f.c], ab = Mid(f.a, f.b), bc = Mid(f.b, f.c), ca = Mid(f.c, f.a);
                r.AppendTriangle(a, ab, ca); r.AppendTriangle(ab, b, bc); r.AppendTriangle(ca, bc, c); r.AppendTriangle(ab, bc, ca);
            }
            return r;
        }

        static DMesh3 Tetra(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            var m = new DMesh3();
            m.AppendVertex(a); m.AppendVertex(b); m.AppendVertex(c); m.AppendVertex(d);
            if ((b - a).Dot((c - a).Cross(d - a)) < 0) { m.AppendTriangle(0, 1, 2); m.AppendTriangle(0, 3, 1); m.AppendTriangle(0, 2, 3); m.AppendTriangle(1, 3, 2); }
            else { m.AppendTriangle(0, 2, 1); m.AppendTriangle(0, 1, 3); m.AppendTriangle(0, 3, 2); m.AppendTriangle(1, 2, 3); }
            return m;
        }

        static DMesh3 Skewed() => Subdivide(Subdivide(Subdivide(
            Tetra(new Vector3d(0, 0, 0), new Vector3d(1, .25, .1875), new Vector3d(0, 1.25, .1875), new Vector3d(0, .25, 1.1875)))));

        static double MaxError(DMesh3 m) { double d = m.GetBounds().DiagonalLength; return 1e-4 * d * d; }

        static PartitionedReducer Reducer(int threads) =>
            new PartitionedReducer() { MaxThreads = threads, MinTriangles = 0, Configure = r => { r.AreaWeightedQuadrics = false; } };

        [Fact]
        public void SkewedTetrahedron_Subdivided_ResultIsClosed()
        {
            DMesh3 fine = Skewed();
            Assert.True(fine.IsClosed());
            var pr = Reducer(1);
            DMesh3 result = pr.Reduce(fine, MaxError(fine));
            Assert.True(pr.UsedPartition);
            Assert.True(result.CheckValidity(true, FailMode.ReturnOnly));
            Assert.True(result.IsClosed(), $"{result.TriangleCount} triangles, {result.BoundaryEdgeIndices().Count()} boundary edges");
            Assert.True(result.TriangleCount <= fine.TriangleCount);
        }

        [Fact]
        public void SkewedTetrahedron_EveryRoundKeepsTheMeshClosed()
        {
            DMesh3 fine = Skewed();
            foreach (int rounds in new[] { 1, 2 }) {
                // (the cancel after the first round returns the mesh as the round stitched it)
                var pn = new PartitionedReduction() { Slabs = 8, Rounds = rounds, MaxThreads = 1, CancelF = () => true,
                    Configure = r => { r.AreaWeightedQuadrics = false; r.PreserveBoundaryShape = false; } };
                DMesh3 afterRound = pn.Reduce(fine, MaxError(fine));
                Assert.True(afterRound.IsClosed(), $"rounds {rounds}: {afterRound.BoundaryEdgeIndices().Count()} boundary edges");
            }
        }

        // many small closed meshes with cuts closer than the gap: tetrahedra of every skew, subdivided once to three times
        [Fact]
        public void SmallMeshes_ManyTetrahedra_AlwaysClosed_AnyThreadCount()
        {
            uint state = 12345;
            double Next() { state = state * 1664525u + 1013904223u; return (state >> 8) / (double)(1 << 24); }
            for (int i = 0; i < 40; ++i) {
                DMesh3 m = Tetra(new Vector3d(0, 0, 0), new Vector3d(1, Next(), Next()), new Vector3d(Next() - 0.5, 1.2 + Next(), Next()), new Vector3d(Next() - 0.5, Next(), 1.1 + Next()));
                for (int s = 0; s < 1 + i % 3; ++s) m = Subdivide(m);
                DMesh3 one = Reducer(1).Reduce(m, MaxError(m)), four = Reducer(4).Reduce(m, MaxError(m));
                Assert.True(one.IsClosed() && four.IsClosed(), $"case {i}: {one.TriangleCount} / {four.TriangleCount} triangles");
                Assert.Equal("", Invariance.Difference(one, four));
            }
        }

        // the 2^k contract on the repaired case: every similarity of Similarity.All() gives a closed result; the exact ones (powers of two)
        // give the scaled result bit for bit
        [Fact]
        public void SkewedTetrahedron_Similarities()
        {
            DMesh3 fine = Skewed();
            DMesh3 reference = Reducer(1).Reduce(fine, MaxError(fine));
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 moved = Invariance.Transformed(fine, sim);
                DMesh3 result = Reducer(4).Reduce(moved, MaxError(fine) * sim.Scale * sim.Scale);
                Assert.True(result.IsClosed(), sim.ToString());
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(reference, Invariance.MappedBack(result, sim)));
            }
        }
    }
}
