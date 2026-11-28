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

        static bool same_corners(InputTriangle t, InputTriangle u)
        {
            for (int k = 0; k < 3; ++k)
                if (u.Corners[k] != t.Corners[0] && u.Corners[k] != t.Corners[1] && u.Corners[k] != t.Corners[2])
                    return false;
            return true;
        }

        // RegionClipping: per region (by its lowest triangle): true while the region is embedded, i.e. no triangle of the mesh in its plane meets another one that is not its
        // neighbour (the pairs that meet are found in the pair loop, which only ever writes false). A region that is not embedded (it overlaps itself or another region of
        // its mesh) is clipped as before and counted per triangle; written by the pair loop, read after it.
        bool[] regionClipped;

        /// <summary> RegionClipping: the key of a triangle's region for the windings of a stack: its region if embedded, else the triangle itself (valid after the pair stage) </summary>
        internal int RegionKey(int t)
        {
            int r = RegionOf[t];
            return regionClipped != null && regionClipped[r] == false ? t : r;
        }

        // RegionClipping: a clip of a flat edge of an embedded region was added to t as a constraint (the pair loop adds every clip, as before). Its crossings with the
        // constraints of t, and its ends where they are on a constraint or on a side of t that is not a flat edge of t's own region, are vertices of t: the same points
        // that every other triangle carrying that constraint segment or that side gets from its pair with the triangle that carries the flat edge (a function of the
        // constraint segment, whatever the number of coplanar layers). Crossings with other ghosts are not needed: neither is a constraint.
        void ghost_points(InputTriangle t, int ti, Segment g, BoxGrid2 grid, List<Segment> real)
        {
            // (a triangle of a region that is not embedded takes every side as a side to keep points on: its flat neighbours may be culled, and the fallback is the old rule)
            int flat = regionClipped[RegionOf[ti]] ? flatMask[ti] : 0;
            for (int e = 0; e < 2; ++e) {
                int y = e == 0 ? g.P : g.Q;
                for (int k = 0; k < 3; ++k)
                    if ((flat & (1 << k)) == 0 && Orient(t, t.Corners[k], t.Corners[(k + 1) % 3], y) == 0) {
                        t.Points.Add(y);
                        break;
                    }
            }
            grid.Query(g.P, g.Q, (k) => {
                Segment a = real[k];
                if (a.P == g.P || a.P == g.Q || a.Q == g.P || a.Q == g.Q)
                    return;
                int o1 = Orient(t, a.P, a.Q, g.P), o2 = Orient(t, a.P, a.Q, g.Q);
                if (o1 * o2 < 0) {
                    int o3 = Orient(t, g.P, g.Q, a.P), o4 = Orient(t, g.P, g.Q, a.Q);
                    if (o3 * o4 < 0)
                        t.Points.Add(PlanesPointImplicit(4 * ti, a.Line, g.Line));
                }
                for (int e = 0; e < 2; ++e) {
                    int y = e == 0 ? g.P : g.Q;
                    if (Orient(t, a.P, a.Q, y) != 0)
                        continue;
                    Direction d = line_direction(t, PlaneOf(a.Line));
                    if (compare_along(d, a.P, y) * compare_along(d, y, a.Q) > 0)
                        t.Points.Add(y);
                }
            });
        }

        /// <summary>
        /// per input triangle, its region: the lowest triangle index of the set of triangles of one mesh that lie in one exact plane and are joined
        /// through flat edges. A flat edge is an edge of two triangles of the mesh (an edge has at most two: DMesh3 holds no third triangle on an edge) that traverse it in opposite directions (consistent
        /// orientation; not "opposite normals"), lie in the same exact plane, and have their third corners strictly on opposite sides of it
        /// (exact in-plane Orient of input points, as meets_only_at_shared). A triangle with no flat edge is a region of its own. Null unless
        /// BuildRegions is set, and before the pair stage. Serial, a function of the triangles alone.
        /// </summary>
        internal int[] RegionOf { get; private set; }

        // eligible (RegionClipping, from find_intersections): the triangles that are in a candidate pair and share their exact plane (planeRep) with another such triangle; only they can have
        // a coplanar partner, so only they are put into regions (a lean version: the others are regions of their own, their edges are boundary edges and clipped as before, and the exact
        // planes of the triangles far from every other mesh are never computed). null: every triangle (BuildRegions alone, the tests).
        void build_regions(bool[] eligible = null, int[] rep = null)
        {
            var parent = new int[Triangles.Count];
            for (int i = 0; i < parent.Length; ++i) parent[i] = i;
            flatMask = RegionClipping ? new byte[Triangles.Count] : null;
            regionClipped = null;
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                // the (at most) two triangles of each edge, in the order of the mesh's triangles (DMesh3 holds no edge of three)
                var first = new Dictionary<long, int>(EdgeKeyComparer.Instance);
                foreach (int ti in MeshTriangles[mi]) {
                    if (triDegenerate[ti] || (eligible != null && eligible[ti] == false)) continue;
                    for (int k = 0; k < 3; ++k) {
                        long e = EdgeKey(triCorners[ti][k], triCorners[ti][(k + 1) % 3]);
                        int other;
                        if (first.TryGetValue(e, out other) == false)
                            first[e] = ti;
                        else if (flat_edge(other, ti, triCorners[ti][k], triCorners[ti][(k + 1) % 3], rep)) {
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
            if (RegionClipping) {
                regionClipped = new bool[parent.Length];
                for (int i = 0; i < parent.Length; ++i) regionClipped[i] = true;
                // a region with a culled triangle is not embedded for this purpose: its boundary edges on culled triangles are never paired, so a piece of another
                // triangle could cross its real boundary (the early-out of CullOutsideA, the default)
                if (Culled != null)
                    for (int i = 0; i < parent.Length; ++i)
                        if (Culled[i])
                            regionClipped[parent[i]] = false;
            }
        }

        // the edge a-b of triangle ti is a flat edge shared with triangle oi
        bool flat_edge(int oi, int ti, int a, int b, int[] rep)
        {
            // (a degenerate O has its third corner on the line a-b: Orient is 0 and the edge is not flat)
            InputTriangle T = Triangles[ti], O = Triangles[oi];
            // (the exact plane of both: planeRep says it for the eligible triangles, the planes of the others are not computed)
            if (rep != null ? rep[ti] != rep[oi] : same_plane(T.P, O.P) == false) return false;
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
