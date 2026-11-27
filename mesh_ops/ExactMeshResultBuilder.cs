using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// The result mesh of ExactMeshBoolean.assemble built in parallel (docs/perf/BOOLEAN_THREAD_SCALING.md, lever 2, step A.9),
    /// for the common case in which every edge of the kept pieces has one piece or two pieces running it opposite ways: then
    /// no AppendTriangle can fail or find an edge already there with a second triangle, and the vertex, triangle and edge ids of
    /// the mesh that AppendVertex / AppendTriangle would build are a function of the sorted edge groups:
    /// - vertex v: the v-th class of corner slots (ExactMeshEdgeGroups.NumberClasses);
    /// - triangle t: the t-th kept piece;
    /// - edge e: the e-th group of equal point edges in the order of its first slot (3 t + corner k of the first piece), because
    ///   AppendTriangle creates an edge at the first corner that has it, triangle by triangle, corner 0, 1, 2;
    /// - the edges at a vertex in increasing edge id (the order of the inserts).
    /// The result is the same mesh bit for bit (DMesh3.FromEdgeStructure); a layout that does not fit gets null and the caller
    /// appends serially.
    /// </summary>
    internal static class ExactMeshResultBuilder
    {
        /// <summary> for tests: TryBuild gives null, so that the result is appended serially </summary>
        internal static bool Disabled;

        /// <summary> for tests: how many results TryBuild has built (not counting the calls that gave null) </summary>
        internal static int Built;

        /// <summary>
        /// tris: the pieces as point ids; vids: the vertex of each corner slot (3 t + k); vertexPoint: the point of each vertex;
        /// coords[usedIndex[point]]: its position; edgeKeys, edgeTri: GroupEdges of tris. Null if an edge has more than two pieces,
        /// two pieces that do not run it opposite ways or end at different vertices, a piece has a vertex twice, or a vertex would get more than 32,766 triangles (its count is a short, #1693).
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static DMesh3 TryBuild(List<Index3i> tris, int[] vids, List<int> vertexPoint, Vector3d[] coords, int[] usedIndex, long[] edgeKeys, int[] edgeTri)
        {
            if (Disabled)
                return null;
            int nt = tris.Count, slots = 3 * nt, nv = vertexPoint.Count;
            // per first slot of an edge: -1 (one piece), or the second slot + 1; 0 at the other slots
            int[] link = new int[slots];
            int blocks = ExactMeshEdgeGroups.Blocks(slots), per = ExactMeshEdgeGroups.Per(slots, blocks);
            var failed = new bool[blocks];
            ExactMeshEdgeGroups.ForBlocks(blocks, [MethodImpl(JitHints.Optimize)] (int b) => failed[b] = LinkBlock(tris, vids, edgeKeys, edgeTri, link, b * per, Math.Min(slots, (b + 1) * per)));
            foreach (bool f in failed)
                if (f)
                    return null;
            // (the numbers of the edges: the first slots in slot order)
            int[] start = new int[blocks + 1];
            int tper = per;
            ExactMeshEdgeGroups.ForBlocks(blocks, [MethodImpl(JitHints.Optimize)] (int b) => {
                int c = 0;
                for (int s = b * tper, end = Math.Min(slots, (b + 1) * tper); s < end; ++s)
                    if (link[s] != 0)
                        c++;
                start[b + 1] = c;
            });
            for (int b = 0; b < blocks; ++b)
                start[b + 1] += start[b];
            int ne = start[blocks];
            int[] edges = new int[4 * ne], triEdges = new int[slots];
            // (the edge's vertices in the order of the first piece, its pieces, and the edge at both of its corner slots)
            ExactMeshEdgeGroups.ForBlocks(blocks, [MethodImpl(JitHints.Optimize)] (int b) => EdgeBlock(vids, link, edges, triEdges, start[b], b * tper, Math.Min(slots, (b + 1) * tper)));
            // (the edges at each vertex in increasing edge number: a counting sort of the (vertex, edge) pairs by vertex, the edges taken in
            // order; serial, a few ns per entry)
            int[] begin = new int[nv + 1], vertexEdges = new int[2 * ne];
            for (int e = 0; e < ne; ++e) {
                begin[edges[4 * e] + 1]++;
                begin[edges[4 * e + 1] + 1]++;
            }
            for (int v = 0; v < nv; ++v)
                begin[v + 1] += begin[v];
            int[] cursor = new int[nv];
            for (int e = 0; e < ne; ++e) {
                int u = edges[4 * e], w = edges[4 * e + 1];
                vertexEdges[begin[u] + cursor[u]++] = e;
                vertexEdges[begin[w] + cursor[w]++] = e;
            }
            double[] positions = new double[3 * nv];
            int vblocks = ExactMeshEdgeGroups.Blocks(nv), vper = ExactMeshEdgeGroups.Per(nv, vblocks);
            ExactMeshEdgeGroups.ForBlocks(vblocks, [MethodImpl(JitHints.Optimize)] (int b) => {
                for (int v = b * vper, end = Math.Min(nv, (b + 1) * vper); v < end; ++v) {
                    Vector3d p = coords[usedIndex[vertexPoint[v]]];
                    positions[3 * v] = p.x;
                    positions[3 * v + 1] = p.y;
                    positions[3 * v + 2] = p.z;
                }
            });
            DMesh3 built = DMesh3.FromEdgeStructure(positions, vids, edges, triEdges, begin, vertexEdges);
            if (built != null)       // (null: a vertex with more than 32,766 triangles, #1693)
                System.Threading.Interlocked.Increment(ref Built);
            return built;
        }

        // the corner of f (0, 1, 2: the edge from that corner to the next) that has the edge key, -1 if none
        static int CornerOf(Index3i f, long key)
        {
            if (ExactMeshArrangement.EdgeKey(f.a, f.b) == key) return 0;
            if (ExactMeshArrangement.EdgeKey(f.b, f.c) == key) return 1;
            if (ExactMeshArrangement.EdgeKey(f.c, f.a) == key) return 2;
            return -1;
        }

        // the groups of equal edges that begin in the sorted range [lo, hi) (a group belongs to the block it begins in): the link
        // of its first slot. True if the layout does not fit.
        [MethodImpl(JitHints.Optimize)]
        static bool LinkBlock(List<Index3i> tris, int[] vids, long[] edgeKeys, int[] edgeTri, int[] link, int lo, int hi)
        {
            int m = edgeKeys.Length;
            int g0 = lo;
            while (g0 > 0 && g0 < hi && edgeKeys[g0] == edgeKeys[g0 - 1])
                g0++;
            for (int g1; g0 < hi; g0 = g1) {
                g1 = g0 + 1;
                while (g1 < m && edgeKeys[g1] == edgeKeys[g0]) g1++;
                if (g1 - g0 > 2)
                    return true;
                int t1 = edgeTri[g0], k1 = CornerOf(tris[t1], edgeKeys[g0]);
                int a1 = vids[3 * t1], b1 = vids[3 * t1 + 1], c1 = vids[3 * t1 + 2];
                if (k1 < 0 || a1 == b1 || b1 == c1 || a1 == c1)
                    return true;
                if (g1 - g0 == 1) {
                    link[3 * t1 + k1] = -1;
                    continue;
                }
                int t2 = edgeTri[g0 + 1], k2 = CornerOf(tris[t2], edgeKeys[g0]);
                int a2 = vids[3 * t2], b2 = vids[3 * t2 + 1], c2 = vids[3 * t2 + 2];
                if (t2 <= t1 || k2 < 0 || a2 == b2 || b2 == c2 || a2 == c2)
                    return true;
                // (the two pieces end the edge at the same vertices and run it opposite ways)
                int u1 = vids[3 * t1 + k1], v1 = vids[3 * t1 + (k1 + 1) % 3], u2 = vids[3 * t2 + k2], v2 = vids[3 * t2 + (k2 + 1) % 3];
                if (u1 != v2 || v1 != u2)
                    return true;
                link[3 * t1 + k1] = 3 * t2 + k2 + 1;
            }
            return false;
        }

        // the edges of the first slots in the range [lo, hi), numbered from first: the vertices (smaller first), the pieces, and the
        // edge of the first slot and of the second slot
        [MethodImpl(JitHints.Optimize)]
        static void EdgeBlock(int[] vids, int[] link, int[] edges, int[] triEdges, int first, int lo, int hi)
        {
            int e = first;
            for (int s = lo; s < hi; ++s) {
                if (link[s] == 0)
                    continue;
                int t = s / 3, k = s - 3 * t;
                int u = vids[s], v = vids[3 * t + (k + 1) % 3];
                edges[4 * e] = Math.Min(u, v);
                edges[4 * e + 1] = Math.Max(u, v);
                edges[4 * e + 2] = t;
                triEdges[s] = e;
                if (link[s] < 0) {
                    edges[4 * e + 3] = DMesh3.InvalidID;
                } else {
                    int s2 = link[s] - 1;
                    edges[4 * e + 3] = s2 / 3;
                    triEdges[s2] = e;
                }
                e++;
            }
        }
    }
}
