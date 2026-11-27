using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace g3
{
    public partial class DMesh3
    {
        /// <summary>
        /// Builds the mesh that AppendVertex of every vertex (in the order of positions) followed by AppendTriangle of every
        /// triangle (in the order of tris) would build, from the edge structure the caller worked out: the same vertex, triangle and
        /// edge ids, the same corner order, the same order of the edges around each vertex, the same reference counts, normals and
        /// time stamps. The vertices and arrays are written by loops over disjoint ranges of blocks (ExactMeshEdgeGroups.ForBlocks) (the one writer of each slot).
        ///
        /// positions: 3 doubles per vertex. tris: 3 vertex ids per triangle (counter-clockwise). edges: 4 ints per edge (smaller
        /// vertex, larger vertex, first triangle, second triangle or InvalidID), numbered in the order of the first corner (triangle,
        /// then corner) that has them. triEdges: the edge id of corner k of triangle t at 3 t + k (the edge from corner k to k + 1).
        /// vertexEdgeBegin (vertexCount + 1) and vertexEdges: the edges at each vertex in increasing id (the order of the inserts).
        /// The mesh is a manifold and closed or open without isolated vertices (every edge has one or two triangles, runs different
        /// ways in two, every vertex an edge): the caller checks this; nothing is validated here, with one exception: a vertex count is a short (#1693), so a
        /// vertex with more than 32,766 triangles cannot be built: the result is then null (nothing is built), as AppendTriangle would refuse the 32,767th triangle (#1684).
        /// </summary>
        internal static DMesh3 FromEdgeStructure(double[] positions, int[] tris, int[] edges, int[] triEdges, int[] vertexEdgeBegin, int[] vertexEdges)
        {
            int nv = positions.Length / 3, nt = tris.Length / 3, ne = edges.Length / 4;
            // (the triangles at each vertex first: the count of a vertex is 1 + one for each corner, a short)
            int[] corners = new int[nv];
            int blocks = ExactMeshEdgeGroups.Blocks(tris.Length), per = ExactMeshEdgeGroups.Per(tris.Length, blocks);
            ExactMeshEdgeGroups.ForBlocks(blocks, [MethodImpl(JitHints.Optimize)] (int b) => CountCorners(tris, corners, b * per, Math.Min(tris.Length, (b + 1) * per)));
            for (int v = 0; v < nv; ++v)
                if (corners[v] > short.MaxValue - 1)
                    return null;
            DMesh3 mesh = new DMesh3();
            // (vertices: the position, a normal of +Y, an empty edge list, one count and one more for each corner; the vertex ids are
            // 0 .. nv - 1 as consecutive appends; the count of a vertex is a sum of ones, so the order of the additions does not matter)
            mesh.vertices.ResizeLikePushBack(3 * nv);
            mesh.vertices.SetFirst(positions, 3 * nv);
            mesh.normals.ResizeLikePushBack(3 * nv);
            mesh.normals.SetFirstRepeating(new float[] { 0, 1, 0 }, 3 * nv);
            mesh.vertices_refcount.AllocateBulk(nv);
            mesh.vertex_edges.BuildFrom(nv, vertexEdgeBegin, vertexEdges);
            short[] vrefs = new short[nv];
            for (int v = 0; v < nv; ++v)
                vrefs[v] = (short)(1 + corners[v]);
            mesh.vertices_refcount.RawRefCounts.SetFirst(vrefs, nv);
            // (triangles: corners and edge ids, one count each)
            mesh.triangles.ResizeLikePushBack(3 * nt);
            mesh.triangles.SetFirst(tris, 3 * nt);
            mesh.triangle_edges.ResizeLikePushBack(3 * nt);
            mesh.triangle_edges.SetFirst(triEdges, 3 * nt);
            mesh.triangles_refcount.AllocateBulk(nt);
            mesh.triangles_refcount.RawRefCounts.SetFirstRepeating(new short[] { 1 }, nt);
            // (edges: one count each)
            mesh.edges.ResizeLikePushBack(4 * ne);
            mesh.edges.SetFirst(edges, 4 * ne);
            mesh.edges_refcount.AllocateBulk(ne);
            mesh.edges_refcount.RawRefCounts.SetFirstRepeating(new short[] { 1 }, ne);
            // (one time stamp per append)
            mesh.timestamp = nv + nt;
            mesh.shape_timestamp = nv + nt;
            return mesh;
        }

        // one count on the vertex of each corner in [lo, hi)
        [MethodImpl(JitHints.Optimize)]
        static void CountCorners(int[] tris, int[] corners, int lo, int hi)
        {
            for (int i = lo; i < hi; ++i)
                Interlocked.Increment(ref corners[tris[i]]);
        }
    }
}
