using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1693: a vertex reference count of DMesh3 is a short, so a vertex has at most 32,766 triangles (#1684). The exact Boolean builds its result in one go
    /// (ExactMeshResultBuilder.TryBuild / DMesh3.FromEdgeStructure) where no edge has more than two pieces: that path wrote (short)(1 + corners) and wrapped; it now
    /// gives null for such a vertex and the result is appended triangle by triangle (append_triangle, which takes a copy of a vertex that is full).
    /// A Boolean with such a result vertex needs at least 32,767 result triangles and an input of that size does not fit a DMesh3 (the input would have the same
    /// vertex): measured 79 s for the exact union of a cone of 65,532 triangles with a box (net10.0, 4 cores), so these tests use the internal builder directly (the serial path, append_triangle, is in ExactBooleanVertexCopyTests: a file of its own, it needs an internal member that the base does not have).
    /// </summary>
    [Collection("EdgeGroupsHooks")]
    public class ExactBooleanVertexLimitTests
    {
        const int Max = 32766;      // triangles at one vertex

        // a closed fan of n triangles (i, i + 1, c) around the vertex c = n, as the inputs of TryBuild: every point is one vertex
        internal sealed class Fan
        {
            public List<Index3i> Tris = new List<Index3i>();
            public int[] Vids; public List<int> VertexPoint = new List<int>(); public Vector3d[] Coords; public int[] UsedIndex; public long[] EdgeKeys; public int[] EdgeTri;
            public Fan(int n, Func<Vector3d, Vector3d> map = null)
            {
                int c = n;
                for (int i = 0; i < n; ++i) Tris.Add(new Index3i(i, (i + 1) % n, c));
                Vids = new int[3 * n];
                for (int t = 0; t < n; ++t) for (int k = 0; k < 3; ++k) Vids[3 * t + k] = Tris[t][k];
                Coords = new Vector3d[n + 1]; UsedIndex = new int[n + 1];
                for (int v = 0; v <= n; ++v) {
                    VertexPoint.Add(v); UsedIndex[v] = v;
                    Vector3d p = v == n ? Vector3d.Zero : new Vector3d(Math.Cos(2 * Math.PI * v / n), Math.Sin(2 * Math.PI * v / n), 0);
                    Coords[v] = map == null ? p : map(p);
                }
                ExactMeshEdgeGroups.GroupEdges(Tris.Count, (t) => Tris[t], out EdgeKeys, out EdgeTri);
            }
            public DMesh3 Build() { return ExactMeshResultBuilder.TryBuild(Tris, Vids, VertexPoint, Coords, UsedIndex, EdgeKeys, EdgeTri); }
        }

        internal static DMesh3 Appended(Fan f)
        {
            var m = new DMesh3();
            foreach (Vector3d p in f.Coords) m.AppendVertex(p);
            foreach (Index3i t in f.Tris) Assert.True(m.AppendTriangle(t) >= 0);
            return m;
        }

        static void AssertSameBuffers(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.VertexCount, b.VertexCount);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            Assert.Equal(a.EdgeCount, b.EdgeCount);
            Assert.Equal(a.MaxVertexID, b.MaxVertexID);
            for (int i = 0; i < 3 * a.TriangleCount; ++i) Assert.Equal(a.TrianglesBuffer[i], b.TrianglesBuffer[i]);
            for (int i = 0; i < 4 * a.EdgeCount; ++i) Assert.Equal(a.EdgesBuffer[i], b.EdgesBuffer[i]);
            // the positions, the counts of vertices, triangles and edges, and the edges around each vertex in their order
            for (int v = 0; v < a.VertexCount; ++v) {
                Assert.Equal(a.GetVertex(v), b.GetVertex(v));
                Assert.Equal(a.VerticesRefCounts.RawRefCounts[v], b.VerticesRefCounts.RawRefCounts[v]);
                Assert.Equal(a.VtxEdgesItr(v), b.VtxEdgesItr(v));
            }
            for (int t = 0; t < a.TriangleCount; ++t) Assert.Equal(a.TrianglesRefCounts.RawRefCounts[t], b.TrianglesRefCounts.RawRefCounts[t]);
            for (int e = 0; e < a.EdgeCount; ++e) Assert.Equal(a.EdgesRefCounts.RawRefCounts[e], b.EdgesRefCounts.RawRefCounts[e]);
        }

        [Fact]
        public void TryBuild_ForTheLastFittingVertex_BuildsTheMeshThatTheAppendsBuild()
        {
            var fan = new Fan(Max);
            int before = ExactMeshResultBuilder.Built;
            DMesh3 built = fan.Build();
            Assert.NotNull(built);
            Assert.Equal(before + 1, ExactMeshResultBuilder.Built);
            Assert.Equal(Max + 1, built.VerticesRefCounts.RawRefCounts[Max]);
            Assert.True(built.IsVertex(Max));
            AssertSameBuffers(Appended(fan), built);
        }

        [Fact]
        public void TryBuild_ForAVertexWithMoreThan32766Triangles_GivesNull_AndBuildsNothing()
        {
            var fan = new Fan(Max + 1);
            int before = ExactMeshResultBuilder.Built;
            Assert.Null(fan.Build());
            Assert.Equal(before, ExactMeshResultBuilder.Built);
        }

        [Fact]
        public void FromEdgeStructure_ForAVertexWithMoreThan32766Triangles_GivesNull()
        {
            // (the triangles alone decide: the rest of the structure is not read)
            int n = Max + 1;
            var tris = new int[3 * n];
            for (int i = 0; i < n; ++i) { tris[3 * i] = i; tris[3 * i + 1] = (i + 1) % n; tris[3 * i + 2] = n; }
            Assert.Null(DMesh3.FromEdgeStructure(new double[3 * (n + 1)], tris, new int[0], new int[3 * n], new int[n + 2], new int[0]));
        }

        [Theory]
        [InlineData(1, 1 << 13, false)]
        [InlineData(1 << 16, 1 << 13, true)]
        [InlineData(1, 1 << 4, false)]
        public void TryBuild_TheResultDoesNotDependOnTheBlocks(int parallelFrom, int blockSize, bool serial)
        {
            // the number of triangles at a vertex is a sum over the blocks of the corner slots: whatever the blocks and the schedule, the same answer
            int f = ExactMeshEdgeGroups.ParallelFrom, b = ExactMeshEdgeGroups.BlockSize;
            ExactMeshEdgeGroups.ParallelFrom = parallelFrom; ExactMeshEdgeGroups.BlockSize = blockSize;
            try {
                Assert.Null(new Fan(Max + 1).Build());
                DMesh3 built = new Fan(Max).Build();
                Assert.NotNull(built);
                AssertSameBuffers(Appended(new Fan(Max)), built);
            } finally { ExactMeshEdgeGroups.ParallelFrom = f; ExactMeshEdgeGroups.BlockSize = b; }
        }

        [Fact]
        public void TryBuild_UnderSimilarities_KeepsTheTransformedCoordinatesBitForBit()
        {
            foreach (Similarity s in Similarity.All()) {
                var fan = new Fan(Max, p => s.Apply(p));
                DMesh3 built = fan.Build();
                Assert.NotNull(built);
                for (int v = 0; v <= Max; v += 977) {
                    Vector3d expected = s.Apply(v == Max ? Vector3d.Zero : new Vector3d(Math.Cos(2 * Math.PI * v / Max), Math.Sin(2 * Math.PI * v / Max), 0));
                    Assert.Equal(expected, built.GetVertex(v));
                }
                Assert.Null(new Fan(Max + 1, p => s.Apply(p)).Build());
            }
        }
    }
}
