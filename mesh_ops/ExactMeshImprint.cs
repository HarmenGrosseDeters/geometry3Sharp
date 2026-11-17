// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;

namespace g3
{
    /// <summary>
    /// Exact imprint: cuts the intersection curves of the cutter meshes into the target mesh. The target's triangles are split along
    /// the curves and nothing else changes: every vertex of the target keeps its id and its position, an untouched triangle keeps
    /// its id, vertices and orientation, the pieces of a split triangle are oriented as it was and get its triangle group,
    /// and the topology (shared edges, boundary loops, seams of unwelded duplicate vertices) is the target's, so a closed manifold
    /// target gives a closed manifold result. The target may be open or defective (it is not repaired); the cutters may be open or
    /// self-intersecting (only their intersections with the target are used). Nothing is classified or removed
    /// (<see cref="MeshMeshCut.RemoveContained"/> has no counterpart). The robust replacement for the float <see cref="MeshMeshCut"/>
    /// (docs/ROBUSTNESS_SWEEP.md R6); design: docs/design/EXACT_IMPRINT.md.
    ///
    /// The intersections, the coplanar and contact curves and the triangulation of the touched target triangles are exact
    /// (ExactMeshArrangement of the target and the cutters without self-intersections, triangulating the target only). The curve points are rational:
    /// they are rounded to double, and with SafeRounding (the default) the repair of MeshRoundingRepair runs on the result with the
    /// target's vertices fixed (never moved or removed), the curve edges locked (kept, unless AllowCurveContraction and the
    /// rounding needs the contraction) and the target's own edges between untouched vertices locked. Pairs of
    /// untouched triangles are not tested by the repair (they are the target's, defects of the target included). A target that
    /// intersects itself is not repaired: near such a place the repair may still edit pieces. Where the repair leaves a defect, a
    /// final stage lets the locks of the parts of split target edges follow a contraction (the slivers of near contact, eg a
    /// cutter shifted by one ulp; docs/design/IMPRINT_SLIVER_CLEANUP.md); a result that is clean without it is the same.
    ///
    /// Usage: set Target and Cutters (or Cutter), call Compute(), read Result (a new mesh, the inputs are not modified),
    /// CurveEdges and SourceTriangle. Compute() can be called again. Result has the target's attributes for its own vertices and
    /// none for the new ones.
    ///
    /// Not covered: a T-junction of the target (a vertex on the interior of a neighbour's edge) stays a T-junction; a degenerate target triangle
    /// is kept as it is and not split, so where a split triangle shares an edge with one, the new vertices on that edge open a crack in the
    /// target's topology (counted in Counts["unsplit_degenerate_points"]; the guarantee of a closed result holds for targets without
    /// degenerate triangles at the cut).
    ///
    /// Implemented from the papers cited in ExactMeshArrangement and docs/design/EXACT_IMPRINT.md, no code taken over.
    /// </summary>
    public class ExactMeshImprint
    {
        /// <summary> result code of Compute() </summary>
        public enum ImprintStatus
        {
            /// <summary> Result is set </summary>
            Success = 0,
            /// <summary> no target, a null cutter, non-finite coordinates, or a result that could not be built (see Error) </summary>
            InvalidInput = 1,
            /// <summary> Compute() was not called yet </summary>
            NotComputed = 2,
            /// <summary> StrictRounding: the rounded result is not clean within the relative bound; Result is null </summary>
            RoundingNotClean = 3,
            /// <summary> MaxPieces: the arrangement would have more pieces than that; Result is null </summary>
            BudgetExceeded = 4,
            /// <summary> Cancellation was cancelled during Compute(); Result is null </summary>
            Cancelled = 5
        }

        /// <summary> the mesh that is cut (not modified) </summary>
        public DMesh3 Target;
        /// <summary> the meshes whose intersection curves are cut into Target (not modified); no cutter gives a copy of the target </summary>
        public List<DMesh3> Cutters;
        /// <summary> sets Cutters to the one mesh </summary>
        public DMesh3 Cutter { set { Cutters = value == null ? null : new List<DMesh3>() { value }; } }
        /// <summary> also number the curves (CurveId): the curve edges that meet at a vertex where exactly two curve edges meet are one curve </summary>
        public bool LabelCurves = false;
        /// <summary> count the self-intersecting pairs of the target into Counts["target_self_intersections"] (a full search, for the diagnosis) </summary>
        public bool CheckTargetSelfIntersections = false;
        /// <summary> repair what rounding the curve vertices to double leaves (see the class comment). If false, the vertices are only rounded to the nearest double. </summary>
        public bool SafeRounding = true;
        /// <summary> a locked curve edge shorter than the bound may be contracted where the repair needs it (Counts["curve_edges_contracted"]); if false, a defect stays instead </summary>
        public bool AllowCurveContraction = true;
        /// <summary>
        /// where the repair leaves a defect, a final stage in which the parts of a split target edge may be contracted with
        /// their locks following the contraction (the slivers of near contact, eg a cutter shifted by one ulp,
        /// docs/design/IMPRINT_SLIVER_CLEANUP.md); a result that is clean without it is the same. For tests: false gives the repair without it.
        /// </summary>
        internal bool SliverCleanup = true;
        /// <summary> for tests: the locks of the parts of split edges as before #1034 (one LockedEdgeF over the fixed set, no RuleLockedEdges, no stage) </summary>
        internal bool LegacyPartLocks = false;
        /// <summary> SafeRounding within the relative bound only; if its result is not clean, Compute() fails with RoundingNotClean (Result null) </summary>
        public bool StrictRounding = false;
        /// <summary> as <see cref="ExactMeshBoolean.RoundingUlpFactor"/>, RoundingAbsoluteTolerance </summary>
        public double RoundingUlpFactor = 256, RoundingAbsoluteTolerance = 0;
        /// <summary> as <see cref="ExactMeshBoolean.RoundingMaxDisplacement"/> </summary>
        public double RoundingMaxDisplacement = 1e-5;
        /// <summary> as <see cref="ExactMeshBoolean.RoundingMaxPairTests"/> </summary>
        public long RoundingMaxPairTests = 5000000;
        /// <summary> compute ResultExactVertices </summary>
        public bool ComputeExactVertices = false;
        /// <summary> the largest number of pieces (triangles of the arrangement) Compute() takes on; with more it stops with BudgetExceeded. 0: no limit </summary>
        public long MaxPieces;
        /// <summary> checked between the phases; a cancelled Compute() has Status Cancelled and no Result </summary>
        public System.Threading.CancellationToken Cancellation;

        /// <summary> the imprinted target, a new mesh (null if Compute() failed) </summary>
        public DMesh3 Result;
        /// <summary> result code of Compute(); Success iff it returned true </summary>
        public ImprintStatus Status = ImprintStatus.NotComputed;
        /// <summary> why Compute() failed, otherwise null </summary>
        public string Error;
        /// <summary> per triangle id of Result: the triangle of Target it comes from (-1 for ids not in use) </summary>
        public int[] SourceTriangle;
        /// <summary> the edges of Result on the intersection curves: undirected vertex pairs, key <see cref="ExactMeshArrangement.EdgeKey"/> </summary>
        public HashSet<long> CurveEdges = new HashSet<long>(EdgeKeyComparer.Instance);
        /// <summary> with LabelCurves: per curve edge (key of CurveEdges) the number of its curve, from 0 </summary>
        public Dictionary<long, int> CurveId = new Dictionary<long, int>(EdgeKeyComparer.Instance);
        /// <summary> the result has a crack the target did not have: new vertices on an edge shared with a degenerate target triangle (which is not split, Counts["unsplit_degenerate_points"]): the result is then not closed although the target was </summary>
        public bool HasCracks { get { return Counts.TryGetValue("unsplit_degenerate_points", out long n) && n > 0; } }
        /// <summary> the repair run by SafeRounding, or null </summary>
        public MeshRoundingRepair Rounding;
        /// <summary> SafeRounding ran and its result has no new self-intersection or degenerate triangle (and no contact made by the rounding) </summary>
        public bool RoundingClean { get { return Rounding != null && Rounding.IsClean; } }
        /// <summary> with ComputeExactVertices: per vertex id of Result its exact coordinates (null for ids not in use) </summary>
        public ExactPoint3[] ResultExactVertices;
        /// <summary> counts of the last Compute(): pieces, split_triangles, new_vertices, curve_edges, contact_points, rounding_* ... </summary>
        public Dictionary<string, long> Counts = new Dictionary<string, long>();
        /// <summary> milliseconds per phase of the last Compute() </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();
        /// <summary> allocated bytes per phase of the last Compute() </summary>
        public Dictionary<string, long> PhaseAllocatedBytes = new Dictionary<string, long>();

        /// <summary> the target with the curves of the cutter cut into it, or null if it failed </summary>
        public static DMesh3 Imprint(DMesh3 target, DMesh3 cutter)
        {
            var imprint = new ExactMeshImprint() { Target = target, Cutter = cutter };
            return imprint.Compute() ? imprint.Result : null;
        }

        /// <summary> replaces the content of target (ids kept) by Result; false if there is none </summary>
        public bool ApplyTo(DMesh3 target)
        {
            if (Result == null || target == null)
                return false;
            target.Copy(Result);
            return true;
        }

        bool fail(ImprintStatus status, string error)
        {
            Status = status;
            Error = error;
            Result = null; SourceTriangle = null; Rounding = null; ResultExactVertices = null;
            CurveEdges = new HashSet<long>(EdgeKeyComparer.Instance); CurveId = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            return false;
        }

        public bool Compute()
        {
            Result = null; Error = null; SourceTriangle = null; Rounding = null; ResultExactVertices = null;
            CurveEdges = new HashSet<long>(EdgeKeyComparer.Instance); CurveId = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            Counts = new Dictionary<string, long>(); PhaseMilliseconds = new Dictionary<string, long>(); PhaseAllocatedBytes = new Dictionary<string, long>();
            Status = ImprintStatus.NotComputed;
            var clock = new PhaseClock(PhaseMilliseconds, PhaseAllocatedBytes);
            if (Target == null)
                return fail(ImprintStatus.InvalidInput, "no target");
            if (Cutters != null && Cutters.Any(c => c == null))
                return fail(ImprintStatus.InvalidInput, "a cutter is null");
            var meshes = new List<DMesh3>() { Target };
            if (Cutters != null)
                meshes.AddRange(Cutters);
            foreach (DMesh3 m in meshes)
                foreach (int vid in m.VertexIndices())
                    if (m.GetVertex(vid).IsFinite == false)
                        return fail(ImprintStatus.InvalidInput, "a vertex is not finite");
            if (Cancellation.IsCancellationRequested)
                return fail(ImprintStatus.Cancelled, "cancelled");
            if (meshes.Count == 1 || Target.TriangleCount == 0) {
                // nothing to cut: a copy
                Result = new DMesh3(Target);
                SourceTriangle = identity_sources(Result);
                Status = ImprintStatus.Success;
                return true;
            }

            var mask = new bool[meshes.Count];
            mask[0] = true;
            var arr = new ExactMeshArrangement(meshes) { SelfIntersections = false, TriangulateMeshes = mask, MaxPieces = MaxPieces, Cancellation = Cancellation };
            arr.Compute();
            foreach (var kv in arr.PhaseMilliseconds) PhaseMilliseconds["arrangement." + kv.Key] = kv.Value;
            clock.Lap("setup");
            if (arr.Stopped == ExactMeshArrangement.StopReason.Cancelled || Cancellation.IsCancellationRequested)
                return fail(ImprintStatus.Cancelled, "cancelled");
            if (arr.Stopped == ExactMeshArrangement.StopReason.Budget)
                return fail(ImprintStatus.BudgetExceeded, string.Format("the arrangement would have {0} pieces, more than MaxPieces = {1}", arr.PredictedPieces, MaxPieces));
            Counts["intersecting_pairs"] = arr.IntersectingPairs;
            Counts["pieces"] = arr.Output.Count;
            Counts["points"] = arr.PointCount;
            if (CheckTargetSelfIntersections)
                Counts["target_self_intersections"] = gs.MeshRepairMetrics.SelfIntersections(Target);

            if (Cancellation.IsCancellationRequested)
                return fail(ImprintStatus.Cancelled, "cancelled");
            if (build(arr) == false)
                return false;
            if (Cancellation.IsCancellationRequested)
                return fail(ImprintStatus.Cancelled, "cancelled");
            clock.Lap("assembly");
            if (ComputeExactVertices)
                compute_exact_vertices(arr);
            if (SafeRounding && round(arr) == false)
                return false;
            clock.Lap("rounding");
            if (LabelCurves)
                label_curves();
            Status = ImprintStatus.Success;
            return true;
        }

        static int[] identity_sources(DMesh3 mesh)
        {
            var s = new int[mesh.MaxTriangleID];
            for (int i = 0; i < s.Length; ++i) s[i] = mesh.IsTriangle(i) ? i : -1;
            return s;
        }

        // ---- assembly ----

        // new vertices: per touched source triangle and new point a slot; slots joined across the target's shared edges are one vertex
        int[] slotParent = new int[0];
        int slot_find(int x) { while (slotParent[x] != x) { slotParent[x] = slotParent[slotParent[x]]; x = slotParent[x]; } return x; }

        // result vertex -> point id (canonical), for the false contacts of the rounding
        int[] vertexPoint;
        int originalMaxVertexId;

        bool build(ExactMeshArrangement arr)
        {
            // the pieces per source triangle (Output is in the order of the input triangles)
            int n = arr.Triangles.Count;
            var pieces = new List<ExactMeshArrangement.OutputTriangle>[n];
            foreach (var o in arr.Output) {
                if (o.Mesh != 0) continue;
                (pieces[o.Source] ?? (pieces[o.Source] = new List<ExactMeshArrangement.OutputTriangle>())).Add(o);
            }
            var indexOfTid = new Dictionary<int, int>();
            foreach (int ti in arr.MeshTriangles[0])
                indexOfTid[arr.Triangles[ti].Tid] = ti;
            var touched = new List<int>();     // arrangement indices of the split triangles, ascending
            foreach (int ti in arr.MeshTriangles[0].OrderBy(x => x)) {
                var ps = pieces[ti];
                if (ps == null) continue;
                var t = arr.Triangles[ti];
                if (ps.Count == 1 && same_triangle(arr, t, ps[0])) continue;
                touched.Add(ti);
            }
            Counts["split_triangles"] = touched.Count;

            // a point that is the position of a vertex of the source triangle's neighbourhood (the vertices of the triangles around its corners) is that
            // vertex (a target vertex on the edge of a split triangle, or a curve through it); vertices of other components are not (no bowtie between them)
            Dictionary<int, int> ring_points(int tid)
            {
                var ring = new Dictionary<int, int>();
                Index3i tvr = Target.GetTriangle(tid);
                foreach (int corner in new[] { tvr.a, tvr.b, tvr.c })
                    foreach (int nt in Target.VtxTrianglesItr(corner)) {
                        Index3i nv = Target.GetTriangle(nt);
                        foreach (int vid in new[] { nv.a, nv.b, nv.c }) {
                            int pp = arr.Canonical(arr.VertexPoints[0][vid]);
                            if (ring.TryGetValue(pp, out int old) == false || vid < old) ring[pp] = vid;
                        }
                    }
                return ring;
            }
            var ringOf = new Dictionary<int, Dictionary<int, int>>();
            Dictionary<int, int> ring_of(int ti) { if (ringOf.TryGetValue(ti, out var r) == false) ringOf[ti] = r = ring_points(arr.Triangles[ti].Tid); return r; }
            long reused = 0;
            // slots: (source, canonical point) of every piece corner that is not a corner of the source triangle
            var slotOf = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            var slotPoint = new List<int>();
            var slotSource = new List<int>();
            var slotsOfSource = new Dictionary<int, List<int>>();
            foreach (int ti in touched) {
                var t = arr.Triangles[ti];
                var cp = corner_points(arr, t);
                foreach (var o in pieces[ti]) {
                    foreach (int p0 in new[] { o.A, o.B, o.C }) {
                        int p = arr.Canonical(p0);
                        if (p == cp[0] || p == cp[1] || p == cp[2]) continue;
                        if (ring_of(ti).ContainsKey(p)) continue;
                        long key = ((long)ti << 32) | (uint)p;
                        if (slotOf.ContainsKey(key)) continue;
                        slotOf[key] = slotPoint.Count;
                        if (slotsOfSource.TryGetValue(ti, out var l) == false) slotsOfSource[ti] = l = new List<int>();
                        l.Add(slotPoint.Count);
                        slotPoint.Add(p);
                        slotSource.Add(ti);
                    }
                }
            }
            slotParent = new int[slotPoint.Count];
            for (int i = 0; i < slotParent.Length; ++i) slotParent[i] = i;
            // joined across the edges that the target shares (by its vertices): the points on such an edge are one vertex
            long unsplitDegenerate = 0;
            foreach (int ti in touched) {
                var t = arr.Triangles[ti];
                int tid = t.Tid;
                Index3i tv = Target.GetTriangle(tid);
                for (int k = 0; k < 3; ++k) {
                    int u = tv[k], v = tv[(k + 1) % 3];
                    int eid = Target.FindEdge(u, v);
                    if (eid < 0) continue;
                    Index2i et = Target.GetEdgeT(eid);
                    int other = et.a == tid ? et.b : et.a;
                    if (other < 0 || other == tid) continue;
                    if (slotsOfSource.TryGetValue(ti, out var mine) == false) continue;
                    var onEdge = new List<int>();
                    foreach (int s in mine)
                        if (arr.Orient(t, arr.Canonical(t.Corners[k]), arr.Canonical(t.Corners[(k + 1) % 3]), slotPoint[s]) == 0)
                            onEdge.Add(s);
                    if (onEdge.Count == 0) continue;
                    if (indexOfTid.TryGetValue(other, out int oti) == false) continue;
                    if (arr.Triangles[oti].Degenerate) {
                        // (a degenerate neighbour is not split: the target's crack stays, and G1 does not hold there; counted)
                        unsplitDegenerate += onEdge.Count;
                        continue;
                    }
                    foreach (int s in onEdge)
                        if (slotOf.TryGetValue(((long)oti << 32) | (uint)slotPoint[s], out int s2)) {
                            int ra = slot_find(s), rb = slot_find(s2);
                            if (ra != rb) slotParent[ra] = rb;
                        }
                }
            }

            Counts["unsplit_degenerate_points"] = unsplitDegenerate;
            // the new vertices, in the order of their exact points (then the smallest source triangle): deterministic
            var groups = new Dictionary<int, (int point, int minTid)>();
            for (int s = 0; s < slotPoint.Count; ++s) {
                int r = slot_find(s);
                int tid = arr.Triangles[slotSource[s]].Tid;
                if (groups.TryGetValue(r, out var g)) { if (tid < g.minTid) groups[r] = (g.point, tid); }
                else groups[r] = (slotPoint[s], tid);
            }
            var order = groups.OrderBy(kv => kv.Value.point, Comparer<int>.Create(arr.ComparePoints)).ThenBy(kv => kv.Value.minTid).Select(kv => kv.Key).ToList();

            DMesh3 result = new DMesh3(Target);
            originalMaxVertexId = Target.MaxVertexID;
            var vertexOfRoot = new Dictionary<int, int>();
            var newPoints = new List<int>();
            foreach (int r in order) {
                int p = groups[r].point;
                int vid = result.AppendVertex(arr.ToInputVector(arr.Exact(p)));
                vertexOfRoot[r] = vid;
                newPoints.Add(p);
            }
            Counts["new_vertices"] = order.Count;
            vertexPoint = new int[result.MaxVertexID];
            for (int i = 0; i < vertexPoint.Length; ++i) vertexPoint[i] = -1;
            foreach (int vid in Target.VertexIndices())
                vertexPoint[vid] = arr.Canonical(arr.VertexPoints[0][vid]);
            for (int i = 0; i < order.Count; ++i)
                vertexPoint[vertexOfRoot[order[i]]] = newPoints[i];

            // the triangles: the split ones (and the degenerate ones next to them) are replaced by their pieces
            var constraint = arr.ConstraintEdges;
            var add = new List<(int src, Index3i tri)>();
            var toRemove = new List<int>();
            var curve = new HashSet<long>(EdgeKeyComparer.Instance);
            targetSubEdges = new HashSet<long>(EdgeKeyComparer.Instance);
            foreach (int ti in touched) {
                var t = arr.Triangles[ti];
                int tid = t.Tid;
                Index3i tv = Target.GetTriangle(tid);
                var cp = corner_points(arr, t);
                toRemove.Add(tid);
                foreach (var o in pieces[ti]) {
                    int[] pts = { arr.Canonical(o.A), arr.Canonical(o.B), arr.Canonical(o.C) };
                    var v = new int[3];
                    for (int c = 0; c < 3; ++c) {
                        int k = Array.IndexOf(cp, pts[c]);
                        v[c] = k >= 0 ? tv[k] : ring_of(ti).TryGetValue(pts[c], out int tvid) ? tvid : vertexOfRoot[slot_find(slotOf[((long)ti << 32) | (uint)pts[c]])];
                        if (k < 0 && ring_of(ti).ContainsKey(pts[c])) reused++;
                    }
                    add.Add((tid, new Index3i(v[0], v[1], v[2])));
                    for (int c = 0; c < 3; ++c) {
                        if (constraint.Contains(ExactMeshArrangement.EdgeKey(pts[c], pts[(c + 1) % 3])))
                            curve.Add(ExactMeshArrangement.EdgeKey(v[c], v[(c + 1) % 3]));
                        // (a piece edge on an edge of the source triangle: a part of a target edge, kept as an edge)
                        for (int k = 0; k < 3; ++k)
                            if (on_edge(arr, t, cp, k, pts[c]) && on_edge(arr, t, cp, k, pts[(c + 1) % 3]))
                                targetSubEdges.Add(ExactMeshArrangement.EdgeKey(v[c], v[(c + 1) % 3]));
                    }
                }
            }
            // a curve that runs along an edge of an unsplit triangle (a cutter that meets the target along its edge): no piece of its own
            foreach (int ti in arr.MeshTriangles[0]) {
                var t = arr.Triangles[ti];
                if (pieces[ti] == null || pieces[ti].Count != 1 || t.Segments.Count == 0 || touched.BinarySearch(ti) >= 0) continue;
                Index3i tv = Target.GetTriangle(t.Tid);
                var cp = corner_points(arr, t);
                for (int k = 0; k < 3; ++k)
                    if (constraint.Contains(ExactMeshArrangement.EdgeKey(cp[k], cp[(k + 1) % 3])))
                        curve.Add(ExactMeshArrangement.EdgeKey(tv[k], tv[(k + 1) % 3]));
            }
            Counts["target_vertex_points"] = reused;
            var groupOf = new Dictionary<int, int>();
            foreach (int tid in toRemove)
                groupOf[tid] = Target.HasTriangleGroups ? Target.GetTriangleGroup(tid) : -1;
            foreach (int tid in toRemove)
                result.RemoveTriangle(tid, false);
            var sources = new Dictionary<int, int>();
            foreach (var a in add) {
                int tid = result.AppendTriangle(a.tri, groupOf[a.src]);
                if (tid < 0)
                    return fail(ImprintStatus.InvalidInput, "the imprinted triangles cannot form a mesh (an edge of the target with more than two pieces?)");
                sources[tid] = a.src;
            }
            SourceTriangle = identity_sources(result);
            foreach (var kv in sources) SourceTriangle[kv.Key] = kv.Value;
            // (a removed triangle id reused for a piece takes the piece's source)
            CurveEdges = curve;
            Counts["curve_edges"] = curve.Count;
            Result = result;
            return true;
        }

        // the exact points of the target edges' parts: piece edges on an edge of their source triangle (vertex pairs of the result)
        HashSet<long> targetSubEdges;

        static bool on_edge(ExactMeshArrangement arr, ExactMeshArrangement.InputTriangle t, int[] cp, int k, int p)
        {
            return p == cp[k] || p == cp[(k + 1) % 3] || arr.Orient(t, cp[k], cp[(k + 1) % 3], p) == 0;
        }

        static int[] corner_points(ExactMeshArrangement arr, ExactMeshArrangement.InputTriangle t)
        {
            return new[] { arr.Canonical(t.Corners[0]), arr.Canonical(t.Corners[1]), arr.Canonical(t.Corners[2]) };
        }

        static bool same_triangle(ExactMeshArrangement arr, ExactMeshArrangement.InputTriangle t, ExactMeshArrangement.OutputTriangle o)
        {
            var cp = corner_points(arr, t);
            return arr.Canonical(o.A) == cp[0] && arr.Canonical(o.B) == cp[1] && arr.Canonical(o.C) == cp[2];
        }

        void compute_exact_vertices(ExactMeshArrangement arr)
        {
            var exact = new ExactPoint3[Result.MaxVertexID];
            foreach (int vid in Result.VertexIndices())
                if (vertexPoint[vid] >= 0)
                    exact[vid] = arr.ToInputExact(arr.Exact(vertexPoint[vid]));
            ResultExactVertices = exact;
        }

        // ---- rounding ----

        bool round(ExactMeshArrangement arr)
        {
            int original = originalMaxVertexId;
            DMesh3 target = Target;
            var locks = new HashSet<long>(EdgeKeyComparer.Instance);
            foreach (long k in CurveEdges) locks.Add(MeshRoundingRepair.LockedKey((int)(k >> 32), (int)(k & 0xFFFFFFFF)));
            int[] point = vertexPoint;
            var subEdges = targetSubEdges;
            // (the triangles before the repair, for SourceTriangle of the ones it edits)
            var before = new Index3i[Result.MaxTriangleID];
            foreach (int tid in Result.TriangleIndices()) before[tid] = Result.GetTriangle(tid);
            var beforeSource = (int[])SourceTriangle.Clone();
            Rounding = new MeshRoundingRepair(Result) {
                SharedPositionsTouch = true,
                FixedVertexF = (v) => v < original && target.IsVertex(v), FixedHard = true, FixedWhereCoarse = true,
                PointIdF = (v) => v < point.Length ? point[v] : -1,
                // (the target's vertices are input doubles: a piece with three of them is an exact triangle of the arrangement)
                ExactVertexF = (v) => v < original && target.IsVertex(v),
                LockedEdges = locks,
                LockedEdgeF = LegacyPartLocks
                    ? (Func<int, int, bool>)((a, b) => (a < original && b < original && target.FindEdge(a, b) >= 0) || subEdges.Contains(MeshRoundingRepair.LockedKey(a, b)))
                    : (a, b) => a < original && b < original && target.FindEdge(a, b) >= 0,
                // (the parts of the target's split edges: they follow a contraction in the final stage, docs/design/IMPRINT_SLIVER_CLEANUP.md)
                RuleLockedEdges = LegacyPartLocks ? null : new HashSet<long>(subEdges, EdgeKeyComparer.Instance), RuleLocksFollow = SliverCleanup && LegacyPartLocks == false,
                AllowLockedContraction = AllowCurveContraction,
                RemoveTinyComponents = false,
                UlpFactor = StrictRounding ? 0 : RoundingUlpFactor, AbsoluteTolerance = StrictRounding ? 0 : RoundingAbsoluteTolerance,
                MaxDisplacement = RoundingMaxDisplacement, MaxPairTests = RoundingMaxPairTests
            };
            Rounding.Apply();
            if (Cancellation.IsCancellationRequested)
                return fail(ImprintStatus.Cancelled, "cancelled");
            Counts["rounding_intersections"] = Rounding.InitialIntersections;
            Counts["rounding_degenerate"] = Rounding.InitialDegenerate;
            Counts["rounding_edits"] = Rounding.Collapses + Rounding.Flips + Rounding.Moves + Rounding.Zips + Rounding.VertexRemovals;
            Counts["rounding_false_contacts"] = Rounding.InitialFalseContacts;
            Counts["curve_edges_contracted"] = Rounding.LockedContractions;
            Counts["target_edge_parts_contracted"] = Rounding.RuleLockedContractions;
            // (the final stage of SliverCleanup: whether it ran, and its contractions that reroute the chain of parts of a split edge
            // through a vertex off the edge, a target vertex or a new one)
            Counts["sliver_cleanup_stage"] = Rounding.FollowStageRan ? 1 : 0;
            Counts["split_edge_reroutes_through_target_vertex"] = Rounding.RuleLocksReroutedThroughFixed;
            Counts["split_edge_reroutes_through_new_vertex"] = Rounding.RuleLocksReroutedThroughOther;
            // (the contracted edges are gone from the lock set; the curve is what is left of them)
            var edges = new HashSet<long>(EdgeKeyComparer.Instance);
            foreach (long k in locks)
                edges.Add(ExactMeshArrangement.EdgeKey((int)(k >> 32), (int)(k & 0xFFFFFFFF)));
            CurveEdges = edges;
            Counts["curve_edges"] = edges.Count;
            if (StrictRounding && Rounding.IsClean == false)
                return fail(ImprintStatus.RoundingNotClean, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "rounding not clean within the bound {0:G3}: {1} intersecting pairs, {2} degenerate triangles, {3} contacts made by the rounding left",
                    Rounding.MaxLength, Rounding.RemainingIntersections, Rounding.RemainingDegenerate, Rounding.RemainingFalseContacts));
            rederive_sources(before, beforeSource);
            return true;
        }

        // SourceTriangle after the repair: a triangle the repair left as it was keeps its source; an edited one (a flip, a contraction,
        // a zip, ids reused) takes the source, among those of the triangles that were at its vertices, whose target triangle is nearest
        // to its centroid (an edit can span two sources); ids not in use are -1
        void rederive_sources(Index3i[] before, int[] beforeSource)
        {
            var sources = new int[Result.MaxTriangleID];
            for (int i = 0; i < sources.Length; ++i) sources[i] = -1;
            Dictionary<int, List<int>> atVertex = null;
            foreach (int tid in Result.TriangleIndices()) {
                Index3i tv = Result.GetTriangle(tid);
                if (tid < before.Length && before[tid] == tv && tid < beforeSource.Length) { sources[tid] = beforeSource[tid]; continue; }
                if (atVertex == null) {
                    atVertex = new Dictionary<int, List<int>>();
                    for (int b = 0; b < before.Length; ++b) {
                        if (b >= beforeSource.Length || beforeSource[b] < 0) continue;
                        foreach (int v in new[] { before[b].a, before[b].b, before[b].c }) {
                            if (atVertex.TryGetValue(v, out var l) == false) atVertex[v] = l = new List<int>();
                            if (l.Contains(beforeSource[b]) == false) l.Add(beforeSource[b]);
                        }
                    }
                }
                Vector3d centroid = (Result.GetVertex(tv.a) + Result.GetVertex(tv.b) + Result.GetVertex(tv.c)) / 3;
                int best = -1; double bestD = double.MaxValue;
                foreach (int v in new[] { tv.a, tv.b, tv.c }) {
                    if (atVertex.TryGetValue(v, out var cands) == false) continue;
                    foreach (int src in cands) {
                        Index3i st = Target.GetTriangle(src);
                        double d = new DistPoint3Triangle3(centroid, new Triangle3d(Target.GetVertex(st.a), Target.GetVertex(st.b), Target.GetVertex(st.c))).GetSquared();
                        if (d < bestD || (d == bestD && src < best)) { bestD = d; best = src; }
                    }
                }
                sources[tid] = best;
            }
            SourceTriangle = sources;
        }

        // ---- curves ----

        void label_curves()
        {
            var at = new Dictionary<int, List<long>>();
            foreach (long k in CurveEdges) {
                foreach (int v in new[] { (int)(k >> 32), (int)(k & 0xFFFFFFFF) }) {
                    if (at.TryGetValue(v, out var l) == false) at[v] = l = new List<long>();
                    l.Add(k);
                }
            }
            var parent = new Dictionary<long, long>(EdgeKeyComparer.Instance);
            Func<long, long> find = null;
            find = (x) => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            foreach (long k in CurveEdges) parent[k] = k;
            foreach (var kv in at)
                if (kv.Value.Count == 2) {
                    long a = find(kv.Value[0]), b = find(kv.Value[1]);
                    if (a != b) parent[a] = b;
                }
            var number = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            foreach (long k in CurveEdges.OrderBy(x => x)) {
                long r = find(k);
                if (number.TryGetValue(r, out int id) == false) number[r] = id = number.Count;
                CurveId[k] = id;
            }
        }
    }
}
