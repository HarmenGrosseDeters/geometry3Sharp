// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Vertex deletion in the constrained phase. Removing a vertex p leaves the star-shaped polygon of its link; the triangles outside
    /// it keep their empty circumcircles (they were empty with p, so they are without it), so only the polygon is triangulated anew
    /// (Devillers 1999, "On deletion in Delaunay triangulations"). Here by ear clipping with exact orientations, then
    /// Lawson's flips inside it until every new edge is locally Delaunay: the flip algorithm turns any constrained triangulation into
    /// the constrained Delaunay one (Lawson 1977; for constraints Lee and Lin 1986, Chew 1989), and the polygon's edges stay locally
    /// Delaunay throughout, so the flips never leave it. The ear search is O(k^3) in the worst case; the degree of a vertex is small
    /// in practice, and Devillers' O(k log k) priority queue would not pay for its code.
    /// </summary>
    internal sealed partial class DelaunayKernel2
    {
        /// <summary>
        /// Deletes vertex p and restores the constrained Delaunay property. Only a made vertex can be deleted: one made by the
        /// constrained phase, or an input vertex whose input index (its representative, the smallest with its coordinates) is at least
        /// <paramref name="firstMadeInput"/> (on the snap grid, a hot pixel centre that snap rounding appended to the input). It must lie
        /// inside the hull (no ghost triangle at it), have no constrained edge and one tag on all its triangles; otherwise nothing
        /// changes and the result is false.
        /// <para>Triangle slots are renumbered: the star's k triangles become k - 2, and the last two slots move into the two freed
        /// ones. A caller that holds triangle ids must not call this; <see cref="Dirty"/>, if set, receives every slot written.</para>
        /// </summary>
        internal bool DeleteVertex(int p, int firstMadeInput = int.MaxValue)
        {
            if (leaving == null || p == N || p < 0 || p >= VertexCount || leaving[p] < 0) return false;
            if (p < N && Representative[p] < firstMadeInput) return false;
            // the star counter-clockwise: triangle i is (p, ring[i], ring[i + 1]); outer[i] is the half-edge outside it across
            // ring[i] -> ring[i + 1], with that edge's constraint
            var tris = new List<int>(); var ring = new List<int>(); var outer = new List<int>(); var outerSeg = new List<int>();
            int tag = Tag[leaving[p] / 3];
            foreach (int e in Star(p)) {
                int t = e / 3;
                if (Seg[e] >= 0 || IsGhost(t) || Tag[t] != tag) return false;
                tris.Add(t);
                ring.Add(V[Next(e)]);
                outer.Add(H[Next(e)]);
                outerSeg.Add(Seg[Next(e)]);
            }
            int k = ring.Count;
            if (k < 3) throw new InvalidOperationException("DelaunayKernel2: the star of a vertex has fewer than three triangles");
            // ear clipping of the polygon ring[0..k-1]: an ear (a, b, c) turns left strictly and holds no other polygon vertex in its
            // closed triangle (a vertex on the diagonal c-a blocks it, so no triangle is degenerate); a simple polygon always has one
            // (Meisters 1975). The first ear from the start of the ring (leaving[p]) is taken: deterministic, and with cocircular ties the
            // result depends on that start too, which the history of the triangulation fixes.
            var next = new int[k]; var prev = new int[k];
            // per polygon vertex i: the half-edge outside the polygon edge from i to next[i], and its constraint
            var edgeOut = new int[k]; var edgeSeg = new int[k];
            for (int i = 0; i < k; ++i) { next[i] = (i + 1) % k; prev[i] = (i + k - 1) % k; edgeOut[i] = outer[i]; edgeSeg[i] = outerSeg[i]; }
            var made = new List<int>(k - 2);
            var diagonals = new List<long>(k - 3);
            int left = k, at = 0, sinceEar = 0;
            while (left > 3) {
                int ia = prev[at], ib = at, ic = next[at];
                if (IsEar(ring, next, ia, ib, ic)) {
                    int t = tris[made.Count];
                    MakeTriangle(t, ring[ia], ring[ib], ring[ic], tag);
                    Join(3 * t, edgeOut[ia], edgeSeg[ia]);       // a -> b
                    Join(3 * t + 1, edgeOut[ib], edgeSeg[ib]);   // b -> c
                    // the remaining polygon's edge a -> c lies across c -> a of the ear
                    edgeOut[ia] = 3 * t + 2; edgeSeg[ia] = -1;
                    next[ia] = ic; prev[ic] = ia;
                    diagonals.Add(Key(ring[ia], ring[ic]));
                    made.Add(t);
                    --left; at = ic; sinceEar = 0;
                    continue;
                }
                at = next[at];
                if (++sinceEar > left) throw new InvalidOperationException("DelaunayKernel2: the link of a deleted vertex has no ear");
            }
            {
                int ia = prev[at], ib = at, ic = next[at];
                if (Orient(ring[ia], ring[ib], ring[ic]) <= 0) throw new InvalidOperationException("DelaunayKernel2: the last ear of a deleted vertex is degenerate");
                int t = tris[made.Count];
                MakeTriangle(t, ring[ia], ring[ib], ring[ic], tag);
                Join(3 * t, edgeOut[ia], edgeSeg[ia]);
                Join(3 * t + 1, edgeOut[ib], edgeSeg[ib]);
                Join(3 * t + 2, edgeOut[ic], edgeSeg[ic]);
                made.Add(t);
            }
            leaving[p] = -1;
            foreach (int t in made) { Touch(t); if (Dirty != null) Dirty.Add(t); }
            hint = made[0];
            // the two freed slots: the last slots move into them, the larger first (so a freed last slot is simply dropped)
            int f1 = tris[k - 2], f2 = tris[k - 1];
            ReleaseSlot(Math.Max(f1, f2));
            ReleaseSlot(Math.Min(f1, f2));
            RestoreDelaunay(diagonals);
            return true;
        }

        bool IsEar(List<int> ring, int[] next, int ia, int ib, int ic)
        {
            int a = ring[ia], b = ring[ib], c = ring[ic];
            if (Orient(a, b, c) <= 0) return false;
            for (int j = next[ic]; j != ia; j = next[j]) {
                int w = ring[j];
                if (Orient(a, b, w) >= 0 && Orient(b, c, w) >= 0 && Orient(c, a, w) >= 0) return false;
            }
            return true;
        }

        void MakeTriangle(int t, int a, int b, int c, int tag)
        {
            SetTriangle(t, a, b, c);
            Tag[t] = tag;
        }

        /// <summary> frees slot s, which nothing links to any more, by moving the last slot into it </summary>
        void ReleaseSlot(int s)
        {
            int last = TriangleSlots - 1;
            if (s != last) {
                for (int j = 0; j < 3; ++j) {
                    V[3 * s + j] = V[3 * last + j];
                    Seg[3 * s + j] = Seg[3 * last + j];
                    int twin = H[3 * last + j];
                    H[3 * s + j] = twin;
                    H[twin] = 3 * s + j;
                }
                Tag[s] = Tag[last];
                Touch(s);
                if (hint == last) hint = s;
                if (Dirty != null) Dirty.Add(s);
            }
            for (int j = 0; j < 3; ++j) { V[3 * last + j] = 0; H[3 * last + j] = -1; Seg[3 * last + j] = -1; }
            Tag[last] = 0;
            TriangleSlots = last;
        }
    }
}
