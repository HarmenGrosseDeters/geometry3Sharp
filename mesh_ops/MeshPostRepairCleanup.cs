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
    ///   - degenerate triangles (collapse needles onto a corner or the end that keeps the volume, bounded by MaxCollapseDisplacement; flip caps)
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

        /// <summary> triangles with area below DegenerateAreaFactor * diag^2 (diagonal of the bounds of the triangles) are degenerate </summary>
        public double DegenerateAreaFactor = 1e-12;
        /// <summary>
        /// the diagonal diag of DegenerateAreaFactor instead of that of the bounds of the triangles, if finite and
        /// positive (NaN, the default: the bounds of the triangles). MeshRobustRepair passes the diagonal of the largest
        /// connected component, so that a small part far from the rest does not make every triangle degenerate
        /// </summary>
        public double ReferenceDiagonal = double.NaN;
        /// <summary> triangles with longest-edge/height ratio above this are treated like degenerate ones </summary>
        public double MaxAspectRatio = 1000;
        /// <summary> edges whose two triangle normals differ by more than this (degrees) are folds </summary>
        public double FoldAngleDeg = 175;
        /// <summary>
        /// a collapse of an edge between two interior vertices (#1363) moves the discarded vertex onto the kept one, that is by the length of the edge;
        /// the collapse is skipped if that is more than this fraction of the reference diagonal (ReferenceDiagonal, else the diagonal of the bounds of the triangles: the diagonal of DegenerateAreaFactor), so a needle
        /// cannot move the surface by more. 0 or less: no bound
        /// </summary>
        public double MaxCollapseDisplacement = 1e-4;
        /// <summary>
        /// a vertex with an edge whose two triangle normals differ by more than this (degrees) is a corner or a crease: of the two ends of a collapsed
        /// edge between interior vertices the one that is a corner or a crease is kept (#1363)
        /// </summary>
        public double CreaseAngleDeg = 20;
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
            // outside an extent of [2^-100, 2^100] the squares of lengths and areas overflow or underflow: run on the
            // mesh scaled by a power of two to an extent of about 1 (exact) and scale it back, with the reference
            // diagonal; a 2^k scale then gives the unit-scale result times 2^k
            double s = MeshRepairMetrics.unit_scale_for_mesh(Mesh);
            if (s == 1) {
                apply_unit();
                return;
            }
            double reference = ReferenceDiagonal;
            MeshRepairMetrics.ScaleVertices(Mesh, s);
            ReferenceDiagonal = s * reference;
            try {
                apply_unit();
            } finally {
                ReferenceDiagonal = reference;
                MeshRepairMetrics.ScaleVertices(Mesh, 1 / s);
            }
        }

        void apply_unit()
        {
            double diag = (ReferenceDiagonal > 0 && ReferenceDiagonal < double.PositiveInfinity) ? ReferenceDiagonal
                : Math.Max(MeshRepairMetrics.TriangleBounds(Mesh).DiagonalLength, MathUtil.Epsilon);
            areaEps = DegenerateAreaFactor * diag * diag;
            foldCos = Math.Cos(FoldAngleDeg * MathUtil.Deg2Rad);
            creaseCos = Math.Cos(CreaseAngleDeg * MathUtil.Deg2Rad);
            refDiag = diag;

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

        double areaEps, foldCos, creaseCos, refDiag;


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


        // collapse an edge, rejecting collapses that flip neighbours. Between two interior vertices (#1363) onto one of its ends, the one that is a corner
        // or a crease, else the one that keeps the volume best (the midpoint moved a corner by half the edge), and not if the discarded vertex moves by more than
        // MaxCollapseDisplacement of the diagonal; an edge with a boundary vertex keeps that vertex, a boundary edge collapses to its midpoint (as before)
        bool try_collapse(int eid)
        {
            if (!Mesh.IsEdge(eid))
                return false;
            Index2i ev = Mesh.GetEdgeV(eid);
            bool bdryA = Mesh.IsBoundaryVertex(ev.a), bdryB = Mesh.IsBoundaryVertex(ev.b);
            if (bdryA && bdryB && !Mesh.IsBoundaryEdge(eid))
                return false;
            Index2i et = Mesh.GetEdgeT(eid);
            if (!bdryA && !bdryB)
                return try_collapse_to_end(ev, et);
            int keep = bdryB && !bdryA ? ev.b : ev.a;
            int discard = keep == ev.a ? ev.b : ev.a;
            Vector3d newPos = (bdryA != bdryB) ? Mesh.GetVertex(keep) : (Mesh.GetVertex(ev.a) + Mesh.GetVertex(ev.b)) * 0.5;
            if (creates_flip(keep, discard, newPos, et) || creates_flip(discard, keep, newPos, et))
                return false;
            return collapse_into(keep, discard, newPos);
        }

        bool collapse_into(int keep, int discard, Vector3d newPos)
        {
            DMesh3.EdgeCollapseInfo info;
            if (Mesh.CollapseEdge(keep, discard, out info) != MeshResult.Ok)
                return false;
            Mesh.SetVertex(keep, newPos);
            return true;
        }

        bool try_collapse_to_end(Index2i ev, Index2i et)
        {
            Vector3d pa = Mesh.GetVertex(ev.a), pb = Mesh.GetVertex(ev.b);
            // (written so that a NaN length or diagonal refuses the collapse)
            if (MaxCollapseDisplacement > 0 && !(pa.Distance(pb) <= MaxCollapseDisplacement * refDiag))
                return false;
            bool fa = is_feature(ev.a), fb = is_feature(ev.b);
            bool aFirst;
            if (fa != fb)
                aFirst = fa;
            else {
                // (the same origin for both: the volume of the patch changes, not its sum over the closed mesh)
                Vector3d o = (pa + pb) * 0.5;
                double va = Math.Abs(collapse_volume_change(ev.a, ev.b, pa, o, et)), vb = Math.Abs(collapse_volume_change(ev.b, ev.a, pb, o, et));
                aFirst = va < vb || (va == vb && ev.a < ev.b);       // (a tie, eg a needle in a plane: the end with the lower id, as before)
            }
            for (int k = 0; k < 2; ++k) {
                int keep = (k == 0) == aFirst ? ev.a : ev.b, discard = keep == ev.a ? ev.b : ev.a;
                Vector3d newPos = keep == ev.a ? pa : pb;
                if (creates_flip(keep, discard, newPos, et) || creates_flip(discard, keep, newPos, et))
                    continue;
                return collapse_into(keep, discard, newPos);
            }
            return false;
        }

        // a corner or a crease: an edge at the vertex whose two (not degenerate) triangles differ in direction by more than CreaseAngleDeg
        bool is_feature(int vid)
        {
            foreach (int eid in Mesh.VtxEdgesItr(vid)) {
                Index2i et = Mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID || is_bad(et.a) || is_bad(et.b))
                    continue;
                if (Mesh.GetTriNormal(et.a).Dot(Mesh.GetTriNormal(et.b)) < creaseCos)
                    return true;
            }
            return false;
        }

        // six times the change of the volume under the patch of the triangles of two vertices when discard moves onto keep (at newPos), seen from the origin o
        double collapse_volume_change(int keep, int discard, Vector3d newPos, Vector3d o, Index2i removedT)
        {
            double sum = 0;
            var seen = new HashSet<int>();
            for (int k = 0; k < 2; ++k) {
                int v = k == 0 ? keep : discard;
                foreach (int tid in Mesh.VtxTrianglesItr(v)) {
                    if (seen.Add(tid) == false)
                        continue;
                    Index3i t = Mesh.GetTriangle(tid);
                    Vector3d p0 = Mesh.GetVertex(t.a) - o, p1 = Mesh.GetVertex(t.b) - o, p2 = Mesh.GetVertex(t.c) - o;
                    sum -= p0.Dot(p1.Cross(p2));
                    if (tid == removedT.a || tid == removedT.b)
                        continue;
                    Vector3d q0 = (t.a == keep || t.a == discard ? newPos : Mesh.GetVertex(t.a)) - o;
                    Vector3d q1 = (t.b == keep || t.b == discard ? newPos : Mesh.GetVertex(t.b)) - o;
                    Vector3d q2 = (t.c == keep || t.c == discard ? newPos : Mesh.GetVertex(t.c)) - o;
                    sum += q0.Dot(q1.Cross(q2));
                }
            }
            return sum;
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
        /// inverted solids, and the cavities of a hollow solid that was inverted as a whole, are found. An
        /// inside-out component found so is not returned if an odd number of nearly closed open components
        /// (|winding number| above 0.75 inside it) enclose it: the cavity of an open shell. Open components can
        /// only take back a result, never add one. Returns their triangle sets (empty if
        /// there are none or no closed components). Does not modify the mesh.
        /// A component whose surface meets (intersects or touches, exactly) that of a component the majority of its
        /// interior samples lie in is not nested in it: crossing components have no nesting, and keep their
        /// orientation (they are never returned).
        /// Cost: the nesting is tested only for components whose box lies inside another one's (found by a sweep,
        /// so many components side by side are cheap), with a winding number tree per enclosing component; the
        /// surfaces only for a component that would be returned, against the components its samples lie in (so a
        /// correctly oriented mesh pays nothing for them).
        /// </summary>
        public static List<int[]> FindInvertedComponents(DMesh3 mesh)
        {
            var result = new List<int[]>();
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            Func<int[], bool> isClosed = c => c.All(t => {
                Index3i te = mesh.GetTriEdges(t);
                return !mesh.IsBoundaryEdge(te.a) && !mesh.IsBoundaryEdge(te.b) && !mesh.IsBoundaryEdge(te.c);
            });
            var closed = new List<int[]>();
            var open = new List<int[]>();
            foreach (var c in comps.Components)
                (isClosed(c.Indices) ? closed : open).Add(c.Indices);
            if (closed.Count == 0)
                return result;
            var openBodies = new OpenBodies(mesh, open);

            Vector3d center = mesh.CachedBounds.Center;
            var volAreas = closed.Select(c => MeshMeasurements.VolumeArea(mesh, c, (vid) => mesh.GetVertex(vid) - center)).ToList();
            var vols = volAreas.Select(va => va.x).ToList();
            // the orientation from the exact sign of each volume (a filtered sum, exact where the floating-point sum
            // cannot decide: a sliver far from the centre, whose volume is below the rounding of the sum, is not taken as
            // inside out by a rounded sign; one of volume exactly zero is not inside out); the floating-point volumes
            // stay for the magnitudes (sample depth, open bodies)
            var inverted = closed.Select(c => volume_sign(mesh, c) < 0).ToList();
            if (closed.Count == 1) {
                if (inverted[0] && openBodies.Encloses(closed[0], vols[0], volAreas[0].y) == false)
                    result.Add(closed[0]);
                return result;
            }
            var boxes = closed.Select(c => {
                var box = AxisAlignedBox3d.Empty;
                foreach (int t in c) box.Contain(mesh.GetTriBounds(t));
                return box;
            }).ToList();
            var trees = new DMeshAABBTree3[closed.Count];
            var subs = new DSubmesh3[closed.Count];
            var containers = enclosing_boxes(boxes);
            for (int i = 0; i < closed.Count; ++i) {
                // count enclosing components (|winding number| > 0.5, whatever their orientation). Sample
                // points lie slightly inside component i (the interiors of two components are disjoint or
                // nested, so any interior point decides), not on its surface: where components touch, a
                // surface point lies on the other component too, and there its winding number jumps between
                // 0 and 1 by rounding, so the result would depend on the unit or the position of the mesh
                List<Vector3d> samples = null;
                List<int> nestedIn = null;
                // only a component whose box contains this one's can enclose it
                foreach (int j in containers[i]) {
                    if (trees[j] == null) {
                        subs[j] = new DSubmesh3(mesh, closed[j]);
                        trees[j] = new DMeshAABBTree3(subs[j].SubMesh, true);
                    }
                    if (samples == null)
                        samples = interior_samples(mesh, closed[i], inverted[i],
                            Math.Min(1e-4 * boxes[i].DiagonalLength, 0.25 * Math.Abs(vols[i]) / volAreas[i].y));
                    // a few samples vote, so one sample near a thin feature or another component does not decide
                    int votes = samples.Count(p => Math.Abs(trees[j].FastWindingNumber(p)) > 0.5);
                    if (2 * votes > samples.Count)
                        (nestedIn ?? (nestedIn = new List<int>())).Add(j);
                }
                bool cavity = nestedIn != null && (nestedIn.Count % 2) == 1;
                // an inside-out shell that the closed components leave flagged may be the cavity of a nearly
                // closed open body: only that can take the flag back (never set one)
                if (inverted[i] == cavity || (inverted[i] && openBodies.Encloses(closed[i], vols[i], volAreas[i].y)))
                    continue;
                // nested only if the surfaces do not meet: crossing components (their interiors overlap, neither
                // encloses the other) have no nesting and keep their orientation. (Tested only for a component that
                // would be flagged, so a correctly oriented mesh pays nothing for it.)
                if (nestedIn != null && nestedIn.Any(j => surfaces_meet(mesh, closed[i], subs[j], trees[j])))
                    continue;
                result.Add(closed[i]);
            }
            return result;
        }

        /// <summary>
        /// The count of NestingReadings without the copies of the two readings: the flagged components of FindInvertedComponents that are outward by
        /// their own volume. 0 without a possible nesting (MayHaveNestedShells)
        /// </summary>
        internal static int CountFlippedByNesting(DMesh3 b)
        {
            if (b.TriangleCount == 0 || MayHaveNestedShells(b) == false)
                return 0;
            int count = 0;
            foreach (int[] comp in FindInvertedComponents(b))
                if (volume_sign(b, comp) > 0)
                    ++count;
            return count;
        }

        /// <summary>
        /// The two readings of the nesting of mesh (#1566, #1609): reading is WindingContouring.OrientedCopy(mesh) (a closed shell inside an odd number of
        /// others counts as a cavity and is reversed, FindInvertedComponents), union the plain union of the shells (a component that is outward by its own
        /// volume is not reversed for lying inside another; the nonzero winding number), count the components that differ (flagged although outward by their
        /// own volume). Both null and 0 if there is none (no shell can lie inside another, MayHaveNestedShells, or no flagged component is outward)
        /// </summary>
        internal static void NestingReadings(DMesh3 b, out DMesh3 reading, out DMesh3 union, out int count)
        {
            reading = union = null;
            count = 0;
            if (b.TriangleCount == 0)
                return;
            // (a shell can only be taken for a cavity inside another: no closed shell whose box lies in another's, no flip)
            if (MayHaveNestedShells(b) == false)
                return;
            var flagged = FindInvertedComponents(b);
            if (flagged.Count == 0)
                return;
            DMesh3 plain = new DMesh3(b, true);
            foreach (int[] comp in flagged) {
                bool outward = volume_sign(b, comp) > 0;
                if (outward)
                    ++count;
                foreach (int tid in comp) {
                    if (outward == false)
                        plain.ReverseTriOrientation(tid);
                }
            }
            if (count == 0)
                return;
            reading = WindingContouring.OrientedCopy(b);
            Vector3d c = plain.CachedBounds.Center;
            if (MeshMeasurements.VolumeArea(plain, plain.TriangleIndices(), (vid) => plain.GetVertex(vid) - c).x < 0)
                plain.ReverseOrientation(false);
            union = plain;
        }

        /// <summary>
        /// false if no closed component has its box inside the box of another closed component: then no shell lies inside another, and
        /// FindInvertedComponents flags only the components that are inside out by their own volume. Cheap (components, boxes, a sweep), no exact volume
        /// </summary>
        internal static bool MayHaveNestedShells(DMesh3 mesh)
        {
            var comp = new int[mesh.MaxTriangleID];      // (component id + 1, 0: not reached)
            var stack = new Stack<int>();
            var closedOf = new List<bool>();
            foreach (int start in mesh.TriangleIndices()) {
                if (comp[start] != 0)
                    continue;
                int id = closedOf.Count + 1;
                bool closed = true;
                comp[start] = id;
                stack.Push(start);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    Index3i te = mesh.GetTriEdges(t);
                    for (int k = 0; k < 3; ++k) {
                        Index2i et = mesh.GetEdgeT(te[k]);
                        if (et.b == DMesh3.InvalidID) {
                            closed = false;
                            continue;
                        }
                        int o = et.a == t ? et.b : et.a;
                        if (comp[o] == 0) {
                            comp[o] = id;
                            stack.Push(o);
                        }
                    }
                }
                closedOf.Add(closed);
                // (a mesh of one component, the common case, is done after this flood: no boxes)
                if (closedOf.Count == 1 && stack.Count == 0 && mesh.TriangleIndices().All(t => comp[t] != 0))
                    return false;
            }
            var boxes = new AxisAlignedBox3d[closedOf.Count];
            for (int i = 0; i < boxes.Length; ++i)
                boxes[i] = AxisAlignedBox3d.Empty;
            foreach (int t in mesh.TriangleIndices())
                if (closedOf[comp[t] - 1])
                    boxes[comp[t] - 1].Contain(mesh.GetTriBounds(t));
            var closedBoxes = new List<AxisAlignedBox3d>();
            for (int i = 0; i < boxes.Length; ++i)
                if (closedOf[i])
                    closedBoxes.Add(boxes[i]);
            if (closedBoxes.Count < 2)
                return false;
            foreach (var l in enclosing_boxes(closedBoxes))
                if (l.Count > 0)
                    return true;
            return false;
        }

        // the exact sign of the signed volume of the triangles tris (ExactPredicates.VolumeSign, relative to a vertex of
        // the first triangle)
        internal static int volume_sign(DMesh3 mesh, int[] tris)
        {
            var index = new Dictionary<int, int>();
            var xyz = new List<double>();
            var corners = new int[3 * tris.Length];
            for (int i = 0; i < tris.Length; ++i) {
                Index3i tv = mesh.GetTriangle(tris[i]);
                for (int k = 0; k < 3; ++k) {
                    if (index.TryGetValue(tv[k], out int id) == false) {
                        index[tv[k]] = id = xyz.Count / 3;
                        Vector3d p = mesh.GetVertex(tv[k]);
                        xyz.Add(p.x); xyz.Add(p.y); xyz.Add(p.z);
                    }
                    corners[3 * i + k] = id;
                }
            }
            return ExactPredicates.VolumeSign(xyz.ToArray(), corners);
        }

        // do the triangles tris of mesh meet (exactly: intersect or touch, beyond the vertices they share) a triangle
        // of the submesh sub (with its tree)?
        static bool surfaces_meet(DMesh3 mesh, int[] tris, DSubmesh3 sub, DMeshAABBTree3 tree)
        {
            DMesh3 other = sub.SubMesh;
            bool found = false;
            Index3i ia = Index3i.Zero;
            Triangle3d A = new Triangle3d();
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            var traversal = new DMeshAABBTree3.TreeTraversal() {
                NextBoxDF = (b, depth) => !found && b.Min.x <= box.Max.x && box.Min.x <= b.Max.x && b.Min.y <= box.Max.y
                    && box.Min.y <= b.Max.y && b.Min.z <= box.Max.z && box.Min.z <= b.Max.z,
                NextTriangleF = (tb) => {
                    if (found) return;
                    Index3i ib = other.GetTriangle(tb);
                    Triangle3d B = new Triangle3d(other.GetVertex(ib.a), other.GetVertex(ib.b), other.GetVertex(ib.c));
                    if (ExactTriangleIntersection.IsDegenerate(ref B)) return;
                    // (the base mesh's vertex ids: a vertex shared by the two components, a bowtie, is shared here)
                    Index3i jb = new Index3i(sub.MapVertexToBaseMesh(ib.a), sub.MapVertexToBaseMesh(ib.b), sub.MapVertexToBaseMesh(ib.c));
                    if (TriangleTriangleFilter.IntersectsBeyondShared(ia, ref A, jb, ref B))
                        found = true;
                }
            };
            foreach (int t in tris) {
                ia = mesh.GetTriangle(t);
                A = new Triangle3d(mesh.GetVertex(ia.a), mesh.GetVertex(ia.b), mesh.GetVertex(ia.c));
                if (ExactTriangleIntersection.IsDegenerate(ref A))
                    continue;
                box = new AxisAlignedBox3d(A.V0); box.Contain(A.V1); box.Contain(A.V2);
                tree.DoTraversal(traversal);
                if (found)
                    return true;
            }
            return false;
        }

        // The open components of a mesh as possible enclosing bodies of inside-out closed components (boxes and
        // winding number trees built when first needed, once)
        class OpenBodies
        {
            readonly DMesh3 mesh;
            readonly List<int[]> open;
            AxisAlignedBox3d[] boxes;
            DMeshAABBTree3[] trees;

            public OpenBodies(DMesh3 mesh, List<int[]> open) { this.mesh = mesh; this.open = open; }

            // true if an odd number of them enclose the closed inside-out component comp: nearly closed ones (eg a
            // few boundary edges left), |winding number| above 0.75 at most of its interior samples; an open sheet
            // or bowl around it (about 0.5 and below) does not. Only those whose box contains comp's are tested.
            public bool Encloses(int[] comp, double vol, double area)
            {
                if (open.Count == 0)
                    return false;
                if (boxes == null) {
                    boxes = new AxisAlignedBox3d[open.Count];
                    trees = new DMeshAABBTree3[open.Count];
                    for (int j = 0; j < open.Count; ++j) {
                        boxes[j] = AxisAlignedBox3d.Empty;
                        foreach (int t in open[j]) boxes[j].Contain(mesh.GetTriBounds(t));
                    }
                }
                var box = AxisAlignedBox3d.Empty;
                foreach (int t in comp) box.Contain(mesh.GetTriBounds(t));
                List<Vector3d> samples = null;
                int n = 0;
                for (int j = 0; j < open.Count; ++j) {
                    if (boxes[j].Contains(box) == false)
                        continue;
                    if (samples == null)
                        samples = interior_samples(mesh, comp, true, Math.Min(1e-4 * box.DiagonalLength, 0.25 * Math.Abs(vol) / area));
                    if (trees[j] == null)
                        trees[j] = new DMeshAABBTree3(new DSubmesh3(mesh, open[j]).SubMesh, true);
                    int votes = samples.Count(p => Math.Abs(trees[j].FastWindingNumber(p)) > 0.75);
                    if (2 * votes > samples.Count) n++;
                }
                return (n % 2) == 1;
            }
        }

        /// <summary>
        /// Up to 9 points just inside the closed component tris: centroids of well-shaped triangles (the
        /// largest inradius among evenly spread candidates), moved against the outward normal by
        /// min(maxOffset, inradius / 2). The caller limits maxOffset by 1e-4 * diag and by a quarter of
        /// |volume| / area (about t / 8 for a slab of thickness t), so that a point does not leave a thin
        /// component through its opposite side, and all lengths are relative to the component, so the points
        /// do not depend on the unit. inverted: the component has negative volume, its normals point inwards.
        /// If no candidate is a proper triangle, the plain centroids are returned.
        /// </summary>
        static List<Vector3d> interior_samples(DMesh3 mesh, int[] tris, bool inverted, double maxOffset)
        {
            const int nSamples = 9, nCandidates = 4 * nSamples;
            int n = Math.Min(nCandidates, tris.Length);
            var cands = new List<(double r, Vector3d c, Vector3d nrm)>(n);
            Vector3d va = Vector3d.Zero, vb = Vector3d.Zero, vc = Vector3d.Zero;
            for (int k = 0; k < n; ++k) {
                int tid = tris[(int)((long)k * tris.Length / n)];
                mesh.GetTriVertices(tid, ref va, ref vb, ref vc);
                Vector3d nrm = (vb - va).Cross(vc - va);
                double area2 = nrm.Length;
                double perim = va.Distance(vb) + vb.Distance(vc) + vc.Distance(va);
                if (!(area2 > 0) || !(perim > 0)) continue;
                cands.Add((area2 / perim, (va + vb + vc) / 3, nrm / area2));    // inradius = 2 area / perimeter
            }
            var result = new List<Vector3d>(nSamples);
            if (cands.Count == 0) {
                for (int k = 0; k < Math.Min(nSamples, tris.Length); ++k)
                    result.Add(mesh.GetTriCentroid(tris[(int)((long)k * tris.Length / Math.Min(nSamples, tris.Length))]));
                return result;
            }
            foreach (var cand in cands.OrderByDescending(x => x.r).Take(nSamples)) {
                double h = Math.Min(maxOffset, 0.5 * cand.r);
                result.Add(cand.c + (inverted ? h : -h) * cand.nrm);
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
