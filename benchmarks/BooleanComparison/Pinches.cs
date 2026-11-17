// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;
using gs;

namespace BoolCmp
{
    /// <summary>
    /// The eval's mesh of a result where the solid touches itself (docs/research/BOOLEAN_TRIAGE_2026-11.md, RC2): the result,
    /// welded by exact position (so that all methods are measured the same way), is split at its pinches the way a
    /// manifold-with-duplicated-vertices output represents them. At a welded edge with 2k > 2 triangles that run it k times
    /// in each direction, the triangles are paired by their angle around the edge: each triangle that runs the edge forward
    /// (u to v) with its neighbour on the side its normal points away from (the solid between them), which must run it
    /// backward. Then every vertex gets one copy per fan of triangles joined across its edges (manifold edges and the paired
    /// ones), so that a vertex where the solid touches itself at a point (a bowtie) is split too. Positions with more than one
    /// copy, and the welded edges that were split, are the pinches. An edge with an odd or unbalanced count, or whose
    /// angular order does not alternate, is left unpaired (counted in UnpairedEdges: its triangles usually keep boundary
    /// edges, or the pieces close on their own copies and another check, eg inside-out, names the defect).
    /// Double precision angles: the eval's oracle, not exact code.
    /// </summary>
    public static class Pinches
    {
        public sealed class Split
        {
            public DMesh3 Mesh;
            /// <summary> welded edges with more than two triangles that were split into manifold pairs </summary>
            public int PinchEdges;
            /// <summary> welded positions with more than one vertex copy (pinch edges' ends and bowtie vertices) </summary>
            public int PinchVertices;
            /// <summary> welded edges with more than two triangles that could not be paired (left with boundary edges) </summary>
            public int UnpairedEdges;
        }

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <param name="pos">the welded positions</param>
        /// <param name="tris">triangles on them, none with two equal corners</param>
        public static Split Build(IReadOnlyList<Vector3d> pos, IReadOnlyList<Index3i> tris)
        {
            var res = new Split();
            int nt = tris.Count;
            // the edge slots: slot 3 t + k is the edge of t from corner k to corner k + 1
            var slots = new Dictionary<long, List<int>>();
            for (int t = 0; t < nt; ++t)
                for (int k = 0; k < 3; ++k) {
                    long key = Key(tris[t][k], tris[t][(k + 1) % 3]);
                    if (slots.TryGetValue(key, out var l) == false)
                        slots[key] = l = new List<int>(2);
                    l.Add(3 * t + k);
                }
            var partner = new int[3 * nt];
            for (int i = 0; i < partner.Length; ++i) partner[i] = -1;
            int from(int s) => tris[s / 3][s % 3];
            int to(int s) => tris[s / 3][(s % 3 + 1) % 3];
            int apex(int s) => tris[s / 3][(s % 3 + 2) % 3];
            foreach (var kv in slots) {
                List<int> l = kv.Value;
                if (l.Count == 2) {
                    if (from(l[0]) == to(l[1]) && to(l[0]) == from(l[1])) {
                        partner[l[0]] = l[1];
                        partner[l[1]] = l[0];
                    }
                    continue;
                }
                if (l.Count < 2)
                    continue;
                int u = (int)(kv.Key >> 32), v = (int)(kv.Key & 0xFFFFFFFF);
                int forward = 0;
                foreach (int s in l)
                    if (from(s) == u) forward++;
                if (2 * forward != l.Count) { res.UnpairedEdges++; continue; }
                // angles in the plane normal to the edge, basis (a, b) with a x b = e
                Vector3d e = (pos[v] - pos[u]).Normalized;
                Vector3d a = Math.Abs(e.x) < 0.9 ? Vector3d.AxisX : Vector3d.AxisY;
                a = (a - a.Dot(e) * e).Normalized;
                Vector3d b = e.Cross(a);
                var order = new List<(double angle, int slot)>(l.Count);
                foreach (int s in l) {
                    Vector3d r = pos[apex(s)] - pos[u];
                    order.Add((Math.Atan2(r.Dot(b), r.Dot(a)), s));
                }
                order.Sort((x, y) => x.angle != y.angle ? x.angle.CompareTo(y.angle) : x.slot.CompareTo(y.slot));
                // a forward triangle's normal points to angle + 90 degrees: the solid is on the side of smaller angles, so it
                // pairs with its predecessor, which must run the edge backward
                var pairs = new List<(int, int)>();
                bool ok = true;
                for (int i = 0; i < order.Count && ok; ++i) {
                    int s = order[i].slot;
                    if (from(s) != u) continue;
                    int p = order[(i - 1 + order.Count) % order.Count].slot;
                    if (from(p) == u) ok = false;
                    else pairs.Add((s, p));
                }
                if (ok == false) { res.UnpairedEdges++; continue; }
                foreach (var (s, p) in pairs) { partner[s] = p; partner[p] = s; }
                res.PinchEdges++;
            }
            // fans: union-find over the corners (3 t + k, the corner k of t), joined across the paired edges
            var parent = new int[3 * nt];
            for (int i = 0; i < parent.Length; ++i) parent[i] = i;
            int find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void union(int x, int y) { x = find(x); y = find(y); if (x != y) parent[Math.Max(x, y)] = Math.Min(x, y); }
            for (int s = 0; s < 3 * nt; ++s) {
                int p = partner[s];
                if (p < s) continue;
                int t = s / 3, k = s % 3, tp = p / 3, kp = p % 3;
                // s runs from corner k to k + 1 of t, p the other way: corner k of t is corner kp + 1 of tp
                union(3 * t + k, 3 * tp + (kp + 1) % 3);
                union(3 * t + (k + 1) % 3, 3 * tp + kp);
            }
            var mesh = new DMesh3();
            var vid = new Dictionary<int, int>();          // root corner -> vertex
            var copies = new Dictionary<int, int>();       // welded vertex -> copies
            var corner = new int[3];
            for (int t = 0; t < nt; ++t) {
                for (int k = 0; k < 3; ++k) {
                    int root = find(3 * t + k);
                    if (vid.TryGetValue(root, out int id) == false) {
                        vid[root] = id = mesh.AppendVertex(pos[tris[t][k]]);
                        copies[tris[t][k]] = copies.TryGetValue(tris[t][k], out int c) ? c + 1 : 1;
                    }
                    corner[k] = id;
                }
                if (mesh.AppendTriangle(corner[0], corner[1], corner[2]) < 0)
                    // (an edge left unpaired: the triangle on its own vertices, as the eval did before)
                    mesh.AppendTriangle(mesh.AppendVertex(pos[tris[t].a]), mesh.AppendVertex(pos[tris[t].b]), mesh.AppendVertex(pos[tris[t].c]));
            }
            foreach (int c in copies.Values)
                if (c > 1) res.PinchVertices++;
            res.Mesh = mesh;
            return res;
        }

        /// <summary>
        /// intersecting triangle pairs of the split mesh, where triangles that touch only at positions they both have a vertex
        /// at (the pinches) do not count: MeshRoundingRepair's exact check with SharedPositionsTouch, on a copy, repairing
        /// nothing (MaxPasses 0, no component removal); -1 where the check cannot run (coordinates not finite or outside its
        /// range), so that such a result is not taken as free of intersections
        /// </summary>
        public static int SelfIntersectionsBeyondContacts(DMesh3 mesh)
        {
            if (mesh.TriangleCount == 0)
                return 0;
            var r = new MeshRoundingRepair(new DMesh3(mesh)) { SharedPositionsTouch = true, MaxPasses = 0, RemoveTinyComponents = false };
            r.Apply();
            return r.ScaleNotFinite || r.OutsideRange ? -1 : r.InitialIntersections;
        }
    }
}
