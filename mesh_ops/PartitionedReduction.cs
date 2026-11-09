// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using g3;

namespace gs
{
    /// <summary>
    /// The alpha wrap simplification's Reducer on a fixed spatial partition, the regions in parallel (Alpha Wrap 2.0,
    /// option A), then once more serially on the whole mesh.
    ///
    /// The bounding box is cut into Slabs slabs along its longest axis, at the quantiles of the vertex coordinates (about
    /// the same work per slab), each shrunk by half a gap on both sides, so the shrunk slabs B_i are disjoint. A triangle
    /// with all three corners in B_i belongs to region i; every other triangle is frozen. Each region is reduced in its
    /// own copy: its triangles plus the frozen triangles whose (closed) box meets B_i, whose edges and corners are fully
    /// constrained. A collapse of two free vertices is allowed only if the new position lies in B_i (a collapse onto a
    /// pinned vertex keeps that vertex, which lies in B_i). So every triangle of region i stays inside B_i, those of
    /// other regions inside their disjoint slabs, and the frozen ones do not change: the exact self-intersection check
    /// of each copy sees every triangle that a new triangle could meet. Rounds parallel rounds are run, every second one
    /// with the cuts shifted by half a slab (its slabs cover the gaps of the round before), then a serial pass on the
    /// whole mesh. The vertex quadrics and the fold-over references (Reducer.PreventFoldOver) are those of the whole mesh
    /// at the start, carried through every round: a pinned vertex in region i's slab takes what region i's collapses
    /// added to it.
    ///
    /// Deterministic: the partition depends only on the mesh and Slabs, never on the number of cores or threads (Slabs
    /// is a fixed setting); each region is reduced on its own (no shared state, no reduction across threads); the results
    /// are put together in region order. So the result is the same for every MaxThreads (1 is the serial reference) and
    /// every run. It is not the result of one serial Reducer run: the collapses happen in another order, and every round
    /// evaluates the edges again that a guard refused before (T1, 100 meshes: 3 % fewer triangles at the median).
    ///
    /// Differences to one Reducer run, for a caller that has used that: CancelF is called from the regions' threads at
    /// the same time (it must be thread-safe); an exception in a region surfaces as AggregateException; the regions run
    /// with PreserveBoundaryShape = false (an open mesh loses boundary vertices; the wrap is closed); the result has no
    /// vertex normals, colours, UVs or triangle groups. If the slabs cannot be kept apart (the gap is lost in the rounding
    /// of the coordinates, or GapEdgeLengths is 0), the partition is not used: one serial Reducer run (SerialFallbacks).
    /// </summary>
    internal class PartitionedReduction
    {
        /// <summary> number of slabs (fixed: not derived from the core count, which would change the result) </summary>
        public int Slabs = 8;
        /// <summary> parallel rounds before the serial one (every second with the cuts shifted by half a slab) </summary>
        public int Rounds = 2;
        /// <summary> gap between the slabs, in mean edge lengths </summary>
        public double GapEdgeLengths = 1.0;
        /// <summary> threads for the regions (0: the default of the platform) </summary>
        public int MaxThreads = 0;
        /// <summary> sets the reducer options (the same for the regions and the last pass) </summary>
        public Action<Reducer> Configure;
        /// <summary> (tests) false: a pinned vertex keeps its quadric and fold-over reference from before the round (the
        /// earlier error: what the region's collapses added to it is lost, and later collapses are costed too low) </summary>
        internal bool CarryPinnedVertexData = true;
        /// <summary> cancel: checked after every round; the mesh so far is returned </summary>
        public Func<bool> CancelF = () => false;

        // counts of the last run
        public int RegionTriangles, FrozenTriangles;
        public long RegionCollapses, LastCollapses, SetupMs, RegionMs, StitchMs, LastMs;
        /// <summary> runs that fell back to one serial Reducer run (the slabs could not be kept apart) </summary>
        public int SerialFallbacks;

        /// <summary> a Reducer with given vertex quadrics (relative to origin) and a box for the new positions </summary>
        class SeededReducer : Reducer
        {
            readonly QuadricError[] seed;
            readonly Vector3d origin;
            public AxisAlignedBox3d Box = AxisAlignedBox3d.Infinite;
            public bool LimitToBox;
            public SeededReducer(DMesh3 m, QuadricError[] seed, Vector3d origin) : base(m) { this.seed = seed; this.origin = origin; }

            protected override void InitializeVertexQuadrics()
            {
                quadricOrigin = origin;
                vertQuadrics = new QuadricError[mesh.MaxVertexID];
                Array.Copy(seed, vertQuadrics, Math.Min(seed.Length, vertQuadrics.Length));
            }

            // (a run cancelled before its setup has no quadrics yet: the seeded ones)
            public QuadricError[] VertexQuadrics { get { return vertQuadrics ?? seed; } }

            protected override ProcessResult CollapseEdge(int edgeID, Vector3d vNewPos, out int collapseToV)
            {
                if (LimitToBox && mesh.IsEdge(edgeID)) {
                    Index2i ev = mesh.GetEdgeV(edgeID);
                    bool fa = constraints != null && constraints.GetVertexConstraint(ev.a).Fixed;
                    bool fb = constraints != null && constraints.GetVertexConstraint(ev.b).Fixed;
                    // (with a fixed corner the collapse goes to it, which lies in the box)
                    if (!fa && !fb && !inside(ref Box, ref vNewPos)) {
                        collapseToV = DMesh3.InvalidID;
                        return ProcessResult.Ignored_Constrained;
                    }
                }
                ProcessResult res = base.CollapseEdge(edgeID, vNewPos, out collapseToV);
                if (res == ProcessResult.Ok_Collapsed) Collapses++;
                return res;
            }
            public long Collapses;

            // the quadrics of a mesh, computed as the base Reducer does (exported for the regions)
            public static QuadricError[] Compute(DMesh3 m, Action<Reducer> configure, out Vector3d origin)
            {
                var r = new SeededReducer(m, null, Vector3d.Zero);
                configure?.Invoke(r);
                r.compute_base_quadrics();
                origin = r.quadricOrigin;
                return r.vertQuadrics;
            }

            void compute_base_quadrics() { base.InitializeVertexQuadrics(); }
        }

        static bool inside(ref AxisAlignedBox3d b, ref Vector3d p)
        {
            return p.x >= b.Min.x && p.x <= b.Max.x && p.y >= b.Min.y && p.y <= b.Max.y && p.z >= b.Min.z && p.z <= b.Max.z;
        }

        static bool overlaps(ref AxisAlignedBox3d a, ref AxisAlignedBox3d b)
        {
            return a.Min.x <= b.Max.x && b.Min.x <= a.Max.x && a.Min.y <= b.Max.y && b.Min.y <= a.Max.y
                && a.Min.z <= b.Max.z && b.Min.z <= a.Max.z;
        }

        // one round: the regions of a partition in parallel, put together; returns the mesh and its vertex quadrics
        DMesh3 round(DMesh3 m, QuadricError[] q, Vector3d[] rn, double[] rl, Vector3d origin, double maxError, bool shifted,
            out QuadricError[] resQuadrics, out Vector3d[] resNormals, out double[] resLengths)
        {
            // the partition
            AxisAlignedBox3d bounds = m.CachedBounds;
            int axis = 0;
            for (int d = 1; d < 3; ++d)
                if (bounds.Max[d] - bounds.Min[d] > bounds.Max[axis] - bounds.Min[axis]) axis = d;
            double sum = 0; int ne = 0;
            foreach (int eid in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(eid);
                sum += m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); ne++;
            }
            double gap = GapEdgeLengths * sum / Math.Max(1, ne);
            int K = Math.Max(1, Slabs);
            // slab boundaries at the quantiles of the vertex coordinates along the axis (about equal work per slab)
            var coords = new List<double>(m.VertexCount);
            foreach (int v in m.VertexIndices()) coords.Add(m.GetVertex(v)[axis]);
            coords.Sort();
            // (a shifted round: the cuts halfway between those of the first, so its slabs cover the first round's gaps)
            if (shifted) K = K + 1;
            var cut = new double[K + 1];
            cut[0] = bounds.Min[axis]; cut[K] = bounds.Max[axis];
            for (int k = 1; k < K; ++k) {
                double f = shifted ? (k - 0.5) / (K - 1) : (double)k / K;
                cut[k] = coords[(int)(f * (coords.Count - 1))];
            }
            // (the shrunk slabs must be disjoint: a vertex free in two regions would be put in twice, a crack. With the
            // gap lost in the rounding of the coordinates, or no gap, the caller runs serially)
            for (int k = 1; k < K; ++k)
                if (!(cut[k] - 0.5 * gap < cut[k] && cut[k] < cut[k] + 0.5 * gap)) {
                    resQuadrics = null; resNormals = null; resLengths = null;
                    return null;
                }
            var boxes = new AxisAlignedBox3d[K];
            for (int k = 0; k < K; ++k) {
                AxisAlignedBox3d b = bounds;
                double a0 = cut[k], a1 = cut[k + 1];
                // (the outer faces of the end slabs are the bounds: nothing lies beyond them)
                Vector3d mn = b.Min, mx = b.Max;
                mn[axis] = (k == 0) ? double.NegativeInfinity : a0 + 0.5 * gap;
                mx[axis] = (k == K - 1) ? double.PositiveInfinity : a1 - 0.5 * gap;
                for (int d = 0; d < 3; ++d) if (d != axis) { mn[d] = double.NegativeInfinity; mx[d] = double.PositiveInfinity; }
                boxes[k] = new AxisAlignedBox3d(mn, mx);
            }
            // owner of each triangle (-1: frozen)
            int NT = m.MaxTriangleID;
            int[] owner = new int[NT];
            var triBox = new AxisAlignedBox3d[NT];
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                AxisAlignedBox3d tb = new AxisAlignedBox3d(a); tb.Contain(b); tb.Contain(c);
                triBox[t] = tb;
                owner[t] = -1;
                for (int k = 0; k < K; ++k)
                    if (inside(ref boxes[k], ref a) && inside(ref boxes[k], ref b) && inside(ref boxes[k], ref c)) { owner[t] = k; break; }
                if (owner[t] < 0) FrozenTriangles++; else RegionTriangles++;
            }

            // the regions, each in its own copy
            var results = new DMesh3[K];
            var subToGlobal = new int[K][];
            var subQuadrics = new QuadricError[K][];
            var subNormals = new Vector3d[K][];
            var subLengths = new double[K][];
            var subFrozenTris = new HashSet<int>[K];
            long[] collapses = new long[K];
            Action<int> region = (k) => {
                var sub = new DMesh3();
                var g2s = new Dictionary<int, int>();
                var s2g = new List<int>();
                var frozenSub = new HashSet<int>();
                Func<int, int> vid = (gv) => {
                    int sv;
                    if (g2s.TryGetValue(gv, out sv)) return sv;
                    sv = sub.AppendVertex(m.GetVertex(gv));
                    g2s[gv] = sv; s2g.Add(gv);
                    return sv;
                };
                for (int t = 0; t < NT; ++t) {
                    if (!m.IsTriangle(t)) continue;
                    bool own = owner[t] == k;
                    if (!own && !(owner[t] < 0 && overlaps(ref triBox[t], ref boxes[k]))) continue;
                    Index3i tv = m.GetTriangle(t);
                    int st = sub.AppendTriangle(vid(tv.a), vid(tv.b), vid(tv.c));
                    // (a subset of the triangles of a valid DMesh3 is always appended; a refused one would be lost, or leave
                    // the corners of a frozen triangle free)
                    if (st < 0) throw new InvalidOperationException("PartitionedReduction: region triangle refused");
                    if (!own) frozenSub.Add(st);
                }
                var cons = new MeshConstraints();
                foreach (int st in frozenSub) {
                    Index3i tv = sub.GetTriangle(st), te = sub.GetTriEdges(st);
                    for (int j = 0; j < 3; ++j) {
                        cons.SetOrUpdateVertexConstraint(tv[j], VertexConstraint.Pinned);
                        cons.SetOrUpdateEdgeConstraint(te[j], EdgeConstraint.FullyConstrained);
                    }
                }
                var sq = new QuadricError[sub.MaxVertexID];
                var sn = new Vector3d[sub.MaxVertexID];
                var sl = new double[sub.MaxVertexID];
                for (int sv = 0; sv < s2g.Count; ++sv) { sq[sv] = q[s2g[sv]]; sn[sv] = rn[s2g[sv]]; sl[sv] = rl[s2g[sv]]; }
                var r = new SeededReducer(sub, sq, origin) { Box = boxes[k], LimitToBox = true, SeedReferenceNormals = sn, SeedReferenceLengths = sl };
                Configure?.Invoke(r);
                r.PreserveBoundaryShape = false;
                r.SetExternalConstraints(cons);
                r.ReduceToMaxQuadricError(maxError);
                results[k] = sub;
                subToGlobal[k] = s2g.ToArray();
                subQuadrics[k] = r.VertexQuadrics;
                subNormals[k] = r.ReferenceNormals; subLengths[k] = r.ReferenceLengths;
                subFrozenTris[k] = frozenSub;
                collapses[k] = r.Collapses;
            };
            if (MaxThreads == 1) {
                for (int k = 0; k < K; ++k) region(k);
            } else {
                var opt = new ParallelOptions();
                if (MaxThreads > 0) opt.MaxDegreeOfParallelism = MaxThreads;
                Parallel.For(0, K, opt, region);
            }
            foreach (long c in collapses) RegionCollapses += c;

            // put together, in region order: the frozen triangles and the regions' own triangles
            var res = new DMesh3();
            var gmap = new Dictionary<int, int>();
            var resQ = new List<QuadricError>();
            var resN = new List<Vector3d>();
            var resL = new List<double>();
            Func<int, int> gvid = (gv) => {
                int rv;
                if (gmap.TryGetValue(gv, out rv)) return rv;
                rv = res.AppendVertex(m.GetVertex(gv));
                gmap[gv] = rv; resQ.Add(q[gv]); resN.Add(rn[gv]); resL.Add(rl[gv]);
                return rv;
            };
            for (int t = 0; t < NT; ++t) {
                if (!m.IsTriangle(t) || owner[t] >= 0) continue;
                Index3i tv = m.GetTriangle(t);
                if (res.AppendTriangle(gvid(tv.a), gvid(tv.b), gvid(tv.c)) < 0)
                    throw new InvalidOperationException("PartitionedReduction: frozen triangle refused");
            }
            for (int k = 0; k < K; ++k) {
                DMesh3 sub = results[k];
                int[] s2g = subToGlobal[k];
                var smap = new Dictionary<int, int>();
                Func<int, int> svid = (sv) => {
                    int rv;
                    if (smap.TryGetValue(sv, out rv)) return rv;
                    // (a vertex of the frozen part keeps its global vertex; a free one is new)
                    if (sv < s2g.Length && gmap.ContainsKey(s2g[sv])) {
                        rv = gmap[s2g[sv]];
                        // (a pinned vertex in this region's slab has absorbed the free vertices collapsed onto it: its quadric
                        // and fold-over reference come from this region; a pinned vertex of another slab is unchanged here)
                        Vector3d pv = sub.GetVertex(sv);
                        if (CarryPinnedVertexData && inside(ref boxes[k], ref pv)) {
                            resQ[rv] = subQuadrics[k][sv];
                            if (subNormals[k] != null) { resN[rv] = subNormals[k][sv]; resL[rv] = subLengths[k][sv]; }
                        }
                    }
                    else {
                        rv = res.AppendVertex(sub.GetVertex(sv)); resQ.Add(subQuadrics[k][sv]);
                        resN.Add(subNormals[k] != null ? subNormals[k][sv] : Vector3d.Zero); resL.Add(subLengths[k] != null ? subLengths[k][sv] : 0);
                    }
                    smap[sv] = rv;
                    return rv;
                };
                foreach (int st in sub.TriangleIndices()) {
                    if (subFrozenTris[k].Contains(st)) continue;
                    Index3i tv = sub.GetTriangle(st);
                    if (res.AppendTriangle(svid(tv.a), svid(tv.b), svid(tv.c)) < 0)
                        throw new InvalidOperationException("PartitionedReduction: region triangle refused in the stitch");
                }
            }

            resQuadrics = resQ.ToArray(); resNormals = resN.ToArray(); resLengths = resL.ToArray();
            return res;
        }

        /// <summary>
        /// reduces m to the quadric error maxError (as Reducer.ReduceToMaxQuadricError); returns the reduced mesh (compact)
        /// </summary>
        public DMesh3 Reduce(DMesh3 m, double maxError)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            m = new DMesh3(m, true);
            Vector3d origin;
            QuadricError[] q = SeededReducer.Compute(m, Configure, out origin);

            RegionTriangles = FrozenTriangles = 0; RegionCollapses = 0; SerialFallbacks = 0;
            SetupMs = sw.ElapsedMilliseconds; sw.Restart();
            DMesh3 res = m;
            QuadricError[] resQ = q;
            Vector3d[] rn; double[] rl;
            Reducer.ComputeReferenceNormals(m, out rn, out rl);
            // (an empty mesh has no partition)
            for (int r = 0; r < Rounds && m.TriangleCount > 0; ++r) {
                QuadricError[] nq; Vector3d[] nn; double[] nl;
                DMesh3 next = round(res, resQ, rn, rl, origin, maxError, r % 2 == 1, out nq, out nn, out nl);
                if (next == null) {
                    // (the slabs cannot be kept apart: one serial run on the input)
                    SerialFallbacks++;
                    res = m; resQ = q;
                    Reducer.ComputeReferenceNormals(m, out rn, out rl);
                    break;
                }
                res = next; resQ = nq; rn = nn; rl = nl;
                if (CancelF()) return new DMesh3(res, true);
            }
            RegionMs = sw.ElapsedMilliseconds; sw.Restart();
            StitchMs = sw.ElapsedMilliseconds; sw.Restart();
            // the last pass, serial, on the whole mesh, with the carried quadrics
            var last = new SeededReducer(res, resQ, origin) { SeedReferenceNormals = rn, SeedReferenceLengths = rl };
            Configure?.Invoke(last);
            last.ReduceToMaxQuadricError(maxError);
            LastCollapses = last.Collapses;
            LastMs = sw.ElapsedMilliseconds;
            return new DMesh3(res, true);
        }
    }
}
