// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// Cleanup pass to run after MeshAutoRepair (see MeshAutoRepair.PostRepairCleanup). Fixes defects
    /// that AutoRepair leaves behind or introduces:
    ///   - duplicate (two-sided) triangle pairs
    ///   - degenerate triangles (collapse needles, flip caps)
    ///   - folded edges (flip, only if it reduces the local fold count)
    ///   - bowtie vertices
    ///   - inside-out closed components that are not cavities of another component
    /// Self-intersections are not handled, and holes are not filled (it does not close a mesh).
    ///
    /// Works on any mesh, in place (topology changes, no new mesh is created). Apply() returns no status; the
    /// statistics fields tell what was changed. Deterministic.
    /// </summary>
    public class MeshPostRepairCleanup
    {
        /// <summary> the mesh to clean, modified in place </summary>
        public DMesh3 Mesh;

        /// <summary> triangles with area below DegenerateAreaFactor * diag^2 (bounding box diagonal) are degenerate </summary>
        public double DegenerateAreaFactor = 1e-12;
        /// <summary> triangles with longest-edge/height ratio above this are treated like degenerate ones </summary>
        public double MaxAspectRatio = 1000;
        /// <summary> edges whose two triangle normals differ by more than this (degrees) are folds </summary>
        public double FoldAngleDeg = 175;
        /// <summary> maximum number of degenerate-triangle / fold passes (stops earlier when a pass changes nothing) </summary>
        public int MaxPasses = 10;

        /// <summary>
        /// statistics of the last Apply(): removed triangles of duplicate pairs (both of each pair are removed),
        /// bowtie vertices found before disconnecting, fixed degenerate triangles, flipped fold edges, and
        /// inside-out components that were reversed
        /// </summary>
        public int RemovedDuplicates, DisconnectedBowties, FixedDegenerate, FixedFolds, FlippedComponents;

        /// <summary> the mesh is cleaned in place </summary>
        public MeshPostRepairCleanup(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        /// <summary>
        /// Run all cleanup steps on Mesh: remove duplicate pairs, then alternate degenerate-triangle and fold
        /// fixes (up to MaxPasses), remove isolated triangles, disconnect bowties, reverse inside-out components.
        /// </summary>
        public void Apply()
        {
            double diag = Math.Max(Mesh.CachedBounds.DiagonalLength, MathUtil.Epsilon);
            areaEps = DegenerateAreaFactor * diag * diag;
            foldCos = Math.Cos(FoldAngleDeg * MathUtil.Deg2Rad);

            RemovedDuplicates = DisconnectedBowties = FixedDegenerate = FixedFolds = FlippedComponents = 0;

            remove_duplicate_triangles();
            for (int pass = 0; pass < MaxPasses; ++pass) {
                int n = fix_degenerate_triangles();
                n += fix_folds();
                if (n == 0)
                    break;
            }
            MeshEditor.RemoveIsolatedTriangles(Mesh);
            DisconnectedBowties = MeshIterators.BowtieVertices(Mesh).Count();
            new MeshEditor(Mesh).DisconnectAllBowties();
            FlippedComponents = fix_inverted_components(Mesh);
        }

        double areaEps, foldCos;


        // Two triangles with the same vertex set must be oppositely oriented (otherwise the edges
        // would be non-manifold), so the pair is a zero-volume component of its own. Remove both.
        void remove_duplicate_triangles()
        {
            var seen = new Dictionary<Index3i, int>();
            var remove = new List<int>();
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i key = sorted(Mesh.GetTriangle(tid));
                int other;
                if (seen.TryGetValue(key, out other)) {
                    remove.Add(tid); remove.Add(other);
                    seen.Remove(key);
                } else
                    seen[key] = tid;
            }
            foreach (int tid in remove)
                if (Mesh.IsTriangle(tid) && Mesh.RemoveTriangle(tid, true, false) == MeshResult.Ok)
                    RemovedDuplicates++;
        }


        bool is_bad(int tid)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            Mesh.GetTriVertices(tid, ref a, ref b, ref c);
            double area = MathUtil.Area(a, b, c);
            if (area < areaEps)
                return true;
            double maxEdge = Math.Max(a.Distance(b), Math.Max(b.Distance(c), c.Distance(a)));
            return maxEdge * maxEdge / (2 * area) > MaxAspectRatio;
        }


        // Degenerate triangles are either "needles" (one very short edge, collapse it) or
        // "caps" (one vertex near the opposite edge, flip that edge).
        int fix_degenerate_triangles()
        {
            int nFixed = 0;
            foreach (int tid in Mesh.TriangleIndices().ToArray()) {
                if (!Mesh.IsTriangle(tid) || !is_bad(tid))
                    continue;
                Index3i tv = Mesh.GetTriangle(tid);
                Index3i te = Mesh.GetTriEdges(tid);
                double[] len = new double[3];
                for (int j = 0; j < 3; ++j)
                    len[j] = Mesh.GetVertex(tv[j]).Distance(Mesh.GetVertex(tv[(j + 1) % 3]));
                int iShort = Array.IndexOf(len, len.Min());
                int iLong = Array.IndexOf(len, len.Max());

                // needle: shortest edge much shorter than the others
                if (len[iShort] < 0.1 * len[iLong] && try_collapse(te[iShort])) {
                    nFixed++; continue;
                }
                if (try_flip(te[iLong], true)) { nFixed++; continue; }
                if (try_collapse(te[iShort])) { nFixed++; continue; }
            }
            FixedDegenerate += nFixed;
            return nFixed;
        }


        int fix_folds()
        {
            int nFixed = 0;
            foreach (int eid in Mesh.EdgeIndices().ToArray()) {
                if (!Mesh.IsEdge(eid) || Mesh.IsBoundaryEdge(eid))
                    continue;
                Index2i et = Mesh.GetEdgeT(eid);
                if (is_bad(et.a) || is_bad(et.b))
                    continue;
                if (Mesh.GetTriNormal(et.a).Dot(Mesh.GetTriNormal(et.b)) >= foldCos)
                    continue;
                if (try_flip(eid, false))
                    nFixed++;
            }
            FixedFolds += nFixed;
            return nFixed;
        }


        // flip if the two new triangles are valid, not folded against each other and not
        // flipped relative to the old ones (unless the old ones were degenerate)
        bool try_flip(int eid, bool bOldDegenerate)
        {
            if (!Mesh.IsEdge(eid) || Mesh.IsBoundaryEdge(eid))
                return false;
            Index2i ev = Mesh.GetEdgeV(eid);
            Index2i ov = Mesh.GetEdgeOpposingV(eid);
            if (ov.a == ov.b || Mesh.FindEdge(ov.a, ov.b) != DMesh3.InvalidID)
                return false;
            Index2i et = Mesh.GetEdgeT(eid);
            Vector3d oldN = (Mesh.GetTriNormal(et.a) * Mesh.GetTriArea(et.a) + Mesh.GetTriNormal(et.b) * Mesh.GetTriArea(et.b));

            Vector3d a = Mesh.GetVertex(ev.a), b = Mesh.GetVertex(ev.b), c = Mesh.GetVertex(ov.a), d = Mesh.GetVertex(ov.b);
            // new triangles share edge c-d; orientation follows the old triangles
            Vector3d n0 = MathUtil.Normal(c, d, a), n1 = MathUtil.Normal(d, c, b);
            double a0 = MathUtil.Area(c, d, a), a1 = MathUtil.Area(d, c, b);
            if (a0 < areaEps || a1 < areaEps)
                return false;
            // orient consistently with the old patch (the normal sign of c,d,a depends on winding)
            Vector3d sumN = n0 * a0 + n1 * a1;
            if (n0.Dot(n1) < foldCos || n0.Dot(n1) < 0)
                return false;
            if (!bOldDegenerate && Math.Abs(sumN.Dot(oldN.Normalized)) < 0.5 * sumN.Length)
                return false;

            int foldsBefore = local_folds(et.a, et.b);
            DMesh3.EdgeFlipInfo info;
            if (Mesh.FlipEdge(eid, out info) != MeshResult.Ok)
                return false;
            // undo if the flip produced triangles facing against the old patch, or did not reduce
            // folds on the five affected edges (degenerate fixes may keep the count)
            bool bFacingAgainst = oldN.Length > 0 && (Mesh.GetTriNormal(info.t0).Dot(oldN) < 0 || Mesh.GetTriNormal(info.t1).Dot(oldN) < 0);
            int foldsAfter = local_folds(info.t0, info.t1);
            if (bFacingAgainst || foldsAfter > foldsBefore || (!bOldDegenerate && foldsAfter == foldsBefore)) {
                Mesh.FlipEdge(info.eID, out info);
                return false;
            }
            return true;
        }


        // number of folded edges among the edges of triangles t0 and t1
        int local_folds(int t0, int t1)
        {
            var edges = new HashSet<int>();
            Index3i e0 = Mesh.GetTriEdges(t0), e1 = Mesh.GetTriEdges(t1);
            for (int j = 0; j < 3; ++j) { edges.Add(e0[j]); edges.Add(e1[j]); }
            int n = 0;
            foreach (int eid in edges) {
                Index2i et = Mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID)
                    continue;
                if (Mesh.GetTriArea(et.a) < areaEps || Mesh.GetTriArea(et.b) < areaEps)
                    continue;
                if (Mesh.GetTriNormal(et.a).Dot(Mesh.GetTriNormal(et.b)) < foldCos)
                    n++;
            }
            return n;
        }


        // collapse to the midpoint (or a boundary vertex), rejecting collapses that flip neighbours
        bool try_collapse(int eid)
        {
            if (!Mesh.IsEdge(eid))
                return false;
            Index2i ev = Mesh.GetEdgeV(eid);
            bool bdryA = Mesh.IsBoundaryVertex(ev.a), bdryB = Mesh.IsBoundaryVertex(ev.b);
            if (bdryA && bdryB && !Mesh.IsBoundaryEdge(eid))
                return false;
            int keep = bdryB && !bdryA ? ev.b : ev.a;
            int discard = keep == ev.a ? ev.b : ev.a;
            Vector3d newPos = (bdryA != bdryB) ? Mesh.GetVertex(keep) : (Mesh.GetVertex(ev.a) + Mesh.GetVertex(ev.b)) * 0.5;

            Index2i et = Mesh.GetEdgeT(eid);
            if (creates_flip(keep, discard, newPos, et) || creates_flip(discard, keep, newPos, et))
                return false;

            DMesh3.EdgeCollapseInfo info;
            if (Mesh.CollapseEdge(keep, discard, out info) != MeshResult.Ok)
                return false;
            Mesh.SetVertex(keep, newPos);
            return true;
        }

        bool creates_flip(int vid, int vOther, Vector3d newPos, Index2i removedT)
        {
            foreach (int tid in Mesh.VtxTrianglesItr(vid)) {
                if (tid == removedT.a || tid == removedT.b)
                    continue;
                Index3i t = Mesh.GetTriangle(tid);
                if (t.a == vOther || t.b == vOther || t.c == vOther)
                    continue;
                if (Mesh.GetTriArea(tid) < areaEps)
                    continue;
                Vector3d oldN = Mesh.GetTriNormal(tid);
                Vector3d p0 = t.a == vid ? newPos : Mesh.GetVertex(t.a);
                Vector3d p1 = t.b == vid ? newPos : Mesh.GetVertex(t.b);
                Vector3d p2 = t.c == vid ? newPos : Mesh.GetVertex(t.c);
                if (MathUtil.Area(p0, p1, p2) < areaEps || MathUtil.Normal(p0, p1, p2).Dot(oldN) < 0.2)
                    return true;
            }
            return false;
        }


        static Index3i sorted(Index3i t)
        {
            int[] s = { t.a, t.b, t.c }; Array.Sort(s);
            return new Index3i(s[0], s[1], s[2]);
        }


        /// <summary>
        /// Closed components whose orientation contradicts their nesting: an outer shell (inside an even
        /// number of other components) must have positive volume, a cavity (odd) negative volume. So
        /// inverted solids, and the cavities of a hollow solid that was inverted as a whole, are found.
        /// Returns their triangle sets (empty if there are none or no closed components). Does not modify the mesh.
        /// Cost: the nesting is tested only for components whose box lies inside another one's (found by a sweep,
        /// so many components side by side are cheap), with a winding number tree per enclosing component.
        /// </summary>
        public static List<int[]> FindInvertedComponents(DMesh3 mesh)
        {
            var result = new List<int[]>();
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            var closed = comps.Components.Where(c => c.Indices.All(t => {
                Index3i te = mesh.GetTriEdges(t);
                return !mesh.IsBoundaryEdge(te.a) && !mesh.IsBoundaryEdge(te.b) && !mesh.IsBoundaryEdge(te.c);
            })).Select(c => c.Indices).ToList();
            if (closed.Count == 0)
                return result;

            Vector3d center = mesh.CachedBounds.Center;
            var vols = closed.Select(c => MeshMeasurements.VolumeArea(mesh, c, (vid) => mesh.GetVertex(vid) - center).x).ToList();
            if (closed.Count == 1) {
                if (vols[0] < 0) result.Add(closed[0]);
                return result;
            }
            var boxes = closed.Select(c => {
                var box = AxisAlignedBox3d.Empty;
                foreach (int t in c) box.Contain(mesh.GetTriBounds(t));
                return box;
            }).ToList();
            var trees = new DMeshAABBTree3[closed.Count];
            var containers = enclosing_boxes(boxes);
            for (int i = 0; i < closed.Count; ++i) {
                // count enclosing components (|winding number| > 0.5, whatever their orientation); a few
                // sample triangles vote, so one triangle touching another component does not decide
                int samples = Math.Min(5, closed[i].Length), nInside = 0;
                // only a component whose box contains this one's can enclose it
                foreach (int j in containers[i]) {
                    if (trees[j] == null)
                        trees[j] = new DMeshAABBTree3(new DSubmesh3(mesh, closed[j]).SubMesh, true);
                    int votes = 0;
                    for (int k = 0; k < samples; ++k) {
                        Vector3d p = mesh.GetTriCentroid(closed[i][(int)((long)k * closed[i].Length / samples)]);
                        if (Math.Abs(trees[j].FastWindingNumber(p)) > 0.5) votes++;
                    }
                    if (2 * votes > samples) nInside++;
                }
                bool cavity = (nInside % 2) == 1;
                if ((vols[i] < 0) != cavity)
                    result.Add(closed[i]);
            }
            return result;
        }

        // for each box, the other boxes that contain it: a sweep along x, in which a box can only be contained in
        // boxes that start at or before it and do not end before it starts
        internal static List<int>[] enclosing_boxes(List<AxisAlignedBox3d> boxes)
        {
            int n = boxes.Count;
            var result = new List<int>[n];
            int[] order = Enumerable.Range(0, n).OrderBy(i => boxes[i].Min.x).ToArray();
            var active = new List<int>();
            int next = 0;
            foreach (int i in order) {
                double x = boxes[i].Min.x;
                while (next < n && boxes[order[next]].Min.x <= x)
                    active.Add(order[next++]);
                active.RemoveAll(j => boxes[j].Max.x < x);
                result[i] = new List<int>();
                foreach (int j in active)
                    if (j != i && boxes[j].Contains(boxes[i]))
                        result[i].Add(j);
            }
            for (int i = 0; i < n; ++i)
                result[i].Sort();
            return result;
        }

        static int fix_inverted_components(DMesh3 mesh)
        {
            var inverted = FindInvertedComponents(mesh);
            foreach (var c in inverted)
                foreach (int tid in c)
                    mesh.ReverseTriOrientation(tid);
            return inverted.Count;
        }
    }
}
