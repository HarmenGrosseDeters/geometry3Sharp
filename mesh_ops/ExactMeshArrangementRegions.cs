using System;
using System.Collections.Generic;

namespace g3
{
    // Stage 1 of docs/design/COPLANAR_REGION_CLIPPING.md (#1630): the regions of the input triangles. Nothing in the arrangement uses them yet.
    internal partial class ExactMeshArrangement
    {
        /// <summary> compute RegionOf in find_intersections (off by default; no effect on any result) </summary>
        internal bool BuildRegions;

        /// <summary>
        /// #1630 (stage 2 of docs/design/COPLANAR_REGION_CLIPPING.md, off by default): the edges of a triangle that are flat edges of its region are not
        /// clipped into the coplanar triangles of the other meshes as constraints (the pair is still clipped, so the coplanar relation is recorded).
        /// The pieces then follow the boundaries of the regions, not every input edge; the output triangulation of coplanar clusters changes (same
        /// surface). Implies the regions. ExactMeshBoolean pairs it with the region rank of its stacks; alone it leaves the stacks to the caller.
        /// </summary>
        internal bool RegionClipping;

        // bit k of flatMask[t]: the edge from corner k to corner k+1 of triangle t is a flat edge (RegionClipping only)
        byte[] flatMask;

        /// <summary>
        /// per input triangle, its region: the lowest triangle index of the set of triangles of one mesh that lie in one exact plane and are joined
        /// through flat edges. A flat edge is an edge of two triangles of the mesh (an edge has at most two: DMesh3 holds no third triangle on an edge) that traverse it in opposite directions (consistent
        /// orientation; not "opposite normals"), lie in the same exact plane, and have their third corners strictly on opposite sides of it
        /// (exact in-plane Orient of input points, as meets_only_at_shared). A triangle with no flat edge is a region of its own. Null unless
        /// BuildRegions is set, and before the pair stage. Serial, a function of the triangles alone.
        /// </summary>
        internal int[] RegionOf { get; private set; }

        void build_regions()
        {
            var parent = new int[Triangles.Count];
            for (int i = 0; i < parent.Length; ++i) parent[i] = i;
            flatMask = RegionClipping ? new byte[Triangles.Count] : null;
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                // the (at most) two triangles of each edge, in the order of the mesh's triangles (DMesh3 holds no edge of three)
                var first = new Dictionary<long, int>(EdgeKeyComparer.Instance);
                foreach (int ti in MeshTriangles[mi]) {
                    if (triDegenerate[ti]) continue;
                    for (int k = 0; k < 3; ++k) {
                        long e = EdgeKey(triCorners[ti][k], triCorners[ti][(k + 1) % 3]);
                        int other;
                        if (first.TryGetValue(e, out other) == false)
                            first[e] = ti;
                        else if (flat_edge(other, ti, triCorners[ti][k], triCorners[ti][(k + 1) % 3])) {
                            join(parent, other, ti);
                            if (flatMask != null) {
                                flatMask[ti] |= (byte)(1 << k);
                                for (int j = 0; j < 3; ++j)
                                    if (triCorners[other][j] == triCorners[ti][(k + 1) % 3] && triCorners[other][(j + 1) % 3] == triCorners[ti][k])
                                        flatMask[other] |= (byte)(1 << j);
                            }
                        }
                    }
                }
            }
            for (int i = 0; i < parent.Length; ++i) {
                int r = i;
                while (parent[r] != r) r = parent[r];
                parent[i] = r;
            }
            RegionOf = parent;
        }

        // the edge a-b of triangle ti is a flat edge shared with triangle oi
        bool flat_edge(int oi, int ti, int a, int b)
        {
            // (a degenerate O has its third corner on the line a-b: Orient is 0 and the edge is not flat)
            InputTriangle T = Triangles[ti], O = Triangles[oi];
            if (same_plane(T.P, O.P) == false) return false;
            // O traverses the edge as b->a, and the third corners are strictly on opposite sides
            int c = -1, d = -1;
            bool reversed = false;
            for (int j = 0; j < 3; ++j) {
                int x = T.Corners[j], y = O.Corners[j];
                if (x != a && x != b) c = x;
                if (y != a && y != b) d = y;
                if (y == b && O.Corners[(j + 1) % 3] == a) reversed = true;
            }
            return reversed && c >= 0 && d >= 0 && Orient(T, a, b, c) * Orient(T, a, b, d) < 0;
        }

        // union by the lowest index as the root
        static void join(int[] parent, int a, int b)
        {
            while (parent[a] != a) a = parent[a] = parent[parent[a]];
            while (parent[b] != b) b = parent[b] = parent[parent[b]];
            if (a < b) parent[b] = a;
            else if (b < a) parent[a] = b;
        }
    }
}
