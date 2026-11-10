// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace g3
{
    /// <summary>
    /// Quality mesh of a domain: the constrained Delaunay triangulation of <see cref="ConstrainedDelaunayMesh2"/>, refined by
    /// Delaunay refinement until every triangle meets the minimum angle, the maximum area and the size function, or the Steiner
    /// limit is reached. Conforming: every output triangle has an empty circumcircle, also across segments, except across
    /// locked segments, which are never split (spec O2, O9).
    /// </summary>
    /// <remarks>
    /// <para>Ruppert's algorithm (1995) with Shewchuk's refinements (2002): encroached subsegments are split first, a bad
    /// triangle gets a vertex at its circumcentre unless that would encroach a subsegment, which is then split instead; a
    /// subsegment at an input vertex is split at a power of two from it (concentric shells), so that small input angles do not
    /// make refinement run away. Triangles whose small angle lies between two constrained edges are not refined for their angle.
    /// Split points are computed on the original input segment, so they stay within rounding of it however often a piece is
    /// split (spec O12).</para>
    /// <para>Without <see cref="Conforming"/> (the default), a subsegment counts as encroached only by a vertex in its diametral lens
    /// (Shewchuk 2002, section 6.4: the points that see it under at least 120 degrees, a narrower region than the diametral circle)
    /// when the angle bound is at most arcsin(sqrt 3 / 4) = 25.65 degrees, the bound the paper proves for lenses; above it the
    /// diametral circle is used. As the paper's rule says (after Chew 1993), before a subsegment is bisected the free vertices (made by
    /// refinement, on no constraint) in its open diametral circle that see its midpoint are deleted. The paper's termination proof is
    /// for circumcentres and midpoint bisection; this mesher also inserts off-centres, splits at concentric shells next to input
    /// vertices and, on the snap grid, rounds every point to a pixel centre, so termination here is measured (catalogue, corpus, Herne
    /// tiles), not proven, and the Steiner limit bounds every run. With Conforming the circle is always used (O2).</para>
    /// <para>Terminates: angles up to about 33 degrees converge on ordinary input; any input stops at the Steiner limit
    /// (default 10 x (points + segments) + 100 000, spec Q13). Pieces that are too short to split in doubles stay as they are.</para>
    /// <para>Locked segments (spec O9) keep their vertices and the mesh stays valid, but the bounds are not guaranteed near them
    /// yet: a triangle whose circumcentre lies behind a locked segment is left as it is, so its neighbourhood may keep triangles
    /// below the angle or above the area (listed in <see cref="ConstrainedMesh2.BadTriangles"/> when below the angle). Meeting
    /// the bounds there is the open work item m3.locks.</para>
    /// </remarks>
    public sealed class QualityMesh2
    {
        /// <summary> Minimum angle in degrees, at least 0 and below 60 (0: no angle bound). Default 20 (spec O3). </summary>
        public double MinAngle = 20;
        /// <summary> Maximum triangle area, or null. </summary>
        public double? MaxArea;
        /// <summary>
        /// Target size at a point: the longest edge of a triangle may be at most h(x, y) at its centroid (decision D9), or null. Must
        /// be deterministic and thread safe; it is combined with the other bounds by taking the stricter one.
        /// </summary>
        public Func<double, double, double> SizeFunction;
        /// <summary> Most Steiner points to insert, or null for 10 x (points + segments) + 100 000. Every insertion counts, also of a
        /// vertex that is deleted later (diametral lenses delete free vertices before a bisection), so the output may hold fewer Steiner
        /// points than the limit when it was reached. </summary>
        public int? SteinerLimit;
        /// <summary>
        /// Conforming Delaunay even without bounds: split subsegments until none is encroached (spec O2), encroachment by the
        /// diametral circle. Off (the default): the result is constrained Delaunay, and up to 25.65 degrees encroachment is by the
        /// diametral lens, which inserts fewer points.
        /// </summary>
        public bool Conforming;
        /// <summary> Split crossing constraints (default); off: a crossing is a failure. </summary>
        public bool ResolveCrossings = true;
        /// <summary>
        /// Snap tolerance in coordinate units (spec O11, decision D14), as <see cref="ConstrainedDelaunayMesh2.SnapTolerance"/>; 0: automatic
        /// only. Every vertex refinement adds is on the grid too (owner decision 2026-11-01): Steiner and split points are rounded to pixel
        /// centres when inserted; a triangle whose shortest edge is at most two pixels is not refined; a triangle whose rounded point is
        /// taken or within half a pixel of a constraint stays, and a subsegment without a valid split point on the grid stays; both are
        /// counted in the diagnostics.
        /// The input's own rounding moves a point by at most half a pixel per axis; refinement vertices are hot pixels too and may reroute other
        /// chains, so at a sub-pixel wedge a chain can end up to about 2 pixels from its input edge (MESHING2D_SNAP.md, D14, amended 2026-11-10).
        /// </summary>
        public double SnapTolerance;

        /// <exception cref="ArgumentException">An option or the input is invalid (an angle outside [0, 60), a non-positive area).</exception>
        public ConstrainedTriangulation2Result Compute(ConstrainedMesh2Input input, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (double.IsNaN(MinAngle) || MinAngle < 0 || MinAngle >= 60) throw new ArgumentException("MinAngle must be at least 0 and below 60 degrees (decision D12)");
            if (MaxArea != null && !(MaxArea.Value > 0) || MaxArea != null && double.IsInfinity(MaxArea.Value)) throw new ArgumentException("MaxArea must be positive and finite");
            if (SteinerLimit != null && SteinerLimit.Value < 0) throw new ArgumentException("SteinerLimit must not be negative");
            if (input.Regions != null)
                foreach (var r in input.Regions)
                    if (r != null && r.MaxArea != null && !(r.MaxArea.Value > 0)) throw new ArgumentException("a region's MaxArea must be positive");
            // On the grid, a hull domain's hull edges are snap-rounded with the segments (owner decision 2026-11-02, option C of
            // MESHING2D_HULL_GRID.md): appended as segments, their chains through hot pixels become the hull sources, so refinement
            // splits them in the envelopes of the true hull edges, as it splits segments
            int hullFrom = -1;
            if (SnapTolerance > 0 && Refines(input) && !(input.Rings ?? new List<Mesh2Ring>()).Any(r => r != null && !r.Hole)) {
                // (the input checked first, as Snapped does, so that a bad input gets its own message, not one of the hull; review)
                new ConstrainedDelaunayMesh2.Builder(input, true, cancellationToken).Validate();
                var hull = ExactHull(input.Points);
                if (hull.Count >= 3) {
                    var segments = new List<Mesh2Segment>(input.Segments ?? new List<Mesh2Segment>());
                    hullFrom = segments.Count;
                    for (int i = 0; i < hull.Count; ++i) segments.Add(new Mesh2Segment(hull[i], hull[(i + 1) % hull.Count]));
                    input = new ConstrainedMesh2Input { Points = input.Points, Segments = segments, Rings = input.Rings, HoleSeeds = input.HoleSeeds,
                        Regions = input.Regions, Heights = input.Heights };
                }
            }
            return ConstrainedDelaunayMesh2.Snapped(input, SnapTolerance, cancellationToken, (snapped, snap) => {
                var builder = new ConstrainedDelaunayMesh2.Builder(snapped, ResolveCrossings, cancellationToken) { OuterRingsCollapsed = snap?.OuterRingsCollapsed ?? false };
                if (hullFrom >= 0) { builder.HullSegmentsFrom = snap.SegmentChainStart[hullFrom]; builder.HullSegmentsTo = snap.SegmentChainStart[input.Segments.Count]; }
                var refiner = new Refiner(this, builder, snap);
                builder.refine = refiner.Run;
                builder.IsBad = refiner.BelowAngle;
                return builder.Run();
            });
        }

        /// <summary> refinement runs: an angle, an area, a size, a region area, or conforming (as Refiner.Run decides) </summary>
        bool Refines(ConstrainedMesh2Input input)
        {
            if (MinAngle > 0 || MaxArea != null || SizeFunction != null || Conforming) return true;
            foreach (var r in input.Regions ?? new List<Mesh2Region>()) if (r != null && r.MaxArea != null) return true;
            return false;
        }

        /// <summary> the corners of the convex hull of the points, counter-clockwise, collinear points left out (exact) </summary>
        static List<int> ExactHull(IList<Vector2d> points)
        {
            var order = Enumerable.Range(0, points.Count).Where(i => !double.IsNaN(points[i].x) && !double.IsNaN(points[i].y))
                .OrderBy(i => points[i].x).ThenBy(i => points[i].y).ToList();
            var unique = new List<int>();
            foreach (int i in order) if (unique.Count == 0 || points[unique[unique.Count - 1]] != points[i]) unique.Add(i);
            if (unique.Count < 3) return unique;
            var hull = new List<int>();
            for (int pass = 0; pass < 2; ++pass) {
                int start = hull.Count;
                for (int j = 0; j < unique.Count; ++j) {
                    int i = pass == 0 ? unique[j] : unique[unique.Count - 1 - j];
                    while (hull.Count >= start + 2 && ExactPredicates.Orient2d(points[hull[hull.Count - 2]], points[hull[hull.Count - 1]], points[i]) <= 0)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(i);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        internal sealed class Refiner
        {
            readonly QualityMesh2 options;
            readonly ConstrainedDelaunayMesh2.Builder b;
            DelaunayKernel2 k;
            readonly double sin2Min;
            /// <summary> encroachment by the diametral lens instead of the circle (Shewchuk 2002, section 6.4) </summary>
            readonly bool lenses;
            /// <summary> cot(theta / 2) of the angle bound theta: an off-centre lies |pq| cot(theta / 2) / 2 from the midpoint of pq </summary>
            readonly double halfCot;
            /// <summary> bad triangles are split at off-centres (Ungor 2004) instead of circumcentres </summary>
            readonly bool offCentres;

            /// <summary>
            /// benchmarks only (per thread): when set, refinement adds what it inserts and refuses here, by cause: 0 subsegments split as
            /// encroached, 1 subsegments split because a triangle's point lies behind them, 2 because the point encroaches them, 3
            /// circumcentres, 4 off-centres, 5 grid-blocked triangles, 6 subsegments with no split point on the grid (5 and 6 of the last sweep
            /// only, the others summed over all), 7 points chosen among candidates other than the default (<see cref="BestCandidate"/>; an array
            /// of 7 leaves them uncounted)
            /// </summary>
            [ThreadStatic] internal static long[] CountsForBenchmarks;
            int splitCause;

            /// <summary> benchmarks only: off-centres on or off (null: the default, on) </summary>
            [ThreadStatic] internal static bool? OffCentresForBenchmarks;
            /// <summary> tests only: the rule that keeps chains from coinciding (<see cref="MakesChainsCoincide"/>) on or off (null: the
            /// default, on); off gives the refinement of before the fix, whose outputs the oracle's tests of rule (ii') need </summary>
            [ThreadStatic] internal static bool? KeepChainsApartForTests;
            readonly bool keepChainsApart;
            /// <summary> tests and benchmarks only (per thread): the rule that keeps a split from routing a chain through another chain's vertex of
            /// refinement (a pinch) on or off (null: the default, on) </summary>
            [ThreadStatic] internal static bool? KeepPinchFreeForTests;
            readonly bool keepPinchFree;
            readonly Queue<long> segments = new Queue<long>();
            readonly TriangleQueue triangles;
            /// <summary> benchmarks and tests only (per thread): bad triangles split worst first (smallest angle first) or in the order queued
            /// (null: the default, worst first with an angle bound) </summary>
            [ThreadStatic] internal static bool? WorstFirstForBenchmarks;
            /// <summary> benchmarks and tests only (per thread): the split point of a triangle bad by its angle chosen among candidates on the
            /// bisector of its shortest edge (<see cref="BestCandidate"/>) or always the off-centre or circumcentre (null: the default, chosen) </summary>
            [ThreadStatic] internal static bool? BestCandidateForBenchmarks;
            readonly bool bestCandidate;
            /// <summary> benchmarks and tests only (per thread): the choice's candidates in the petal of the shortest edge
            /// (<see cref="PetalCandidates"/>) or step 1's on its bisector (null: the default, the petal) </summary>
            [ThreadStatic] internal static bool? PetalForBenchmarks;
            readonly bool petal;
            /// <summary> tests only (per thread): called for every choice of <see cref="BestCandidate"/> where the default point counts, with
            /// its insertion radius, the winner's (both squared, scaled), whether the winner is not the default point (only then it is
            /// used), and the winner </summary>
            [ThreadStatic] internal static Action<double, double, bool, Vector2d> PickedForTests;
            /// <summary> tests only (per thread): every angle test the choice reuses is checked against a fresh one (throws) </summary>
            [ThreadStatic] internal static bool CheckScoresForTests;
            /// <summary> tests only (per thread): the bound in <see cref="Score"/> off (every candidate's fan evaluated), to show that it
            /// leaves the choice unchanged </summary>
            [ThreadStatic] internal static bool ScoreBoundOffForTests;

            /// <summary>
            /// The queue of triangles to look at. Worst first: a binary heap keyed by the triangle's sin^2 of its smallest angle when queued
            /// (<see cref="MinSin2"/>, scale-free), ties in the order queued, so the order is deterministic; only triangles below the angle bound, the others follow in the order queued. Splitting the worst triangle
            /// first lets its new vertex remove the slightly bad triangles around it before they are split for themselves, which gives fewer
            /// triangles (measured: MESHING2D_GRADING.md; the ordering is a known heuristic, see MESHING2D_PROVENANCE.md). An entry may
            /// be stale (its slot refilled or freed); the main loop judges the triangle as it is when taken out.
            /// </summary>
            internal sealed class TriangleQueue
            {
                readonly Refiner r;
                readonly bool worstFirst;
                readonly Queue<int> fifo = new Queue<int>();
                readonly List<(double key, long seq, int t)> heap = new List<(double key, long seq, int t)>();
                long seq;

                internal TriangleQueue(Refiner r, bool worstFirst) { this.r = r; this.worstFirst = worstFirst; }

                internal int Count => heap.Count + fifo.Count;

                static bool Less((double key, long seq, int t) a, (double key, long seq, int t) b) => a.key < b.key || (a.key == b.key && a.seq < b.seq);

                internal void Enqueue(int t)
                {
                    if (!worstFirst) { fifo.Enqueue(t); return; }
                    // (a slot that is no real triangle now goes to the FIFO; the main loop drops it)
                    if (t >= r.k.TriangleSlots || r.k.IsGhost(t)) { fifo.Enqueue(t); return; }
                    double key = r.MinSin2(t);
                    // (a needle whose squared edges underflow even in the scaled frame gives 0/0: the worst, and a NaN would break the order)
                    if (double.IsNaN(key)) key = 0;
                    // only triangles below the angle bound are ordered; the others (bad by size, or not bad: most of those queued) wait in
                    // the FIFO behind them, in the order queued as before (a heap of every triangle queued cost a quarter of the time)
                    if (key >= r.sin2Min) { fifo.Enqueue(t); return; }
                    var item = (key, seq++, t);
                    heap.Add(item);
                    int i = heap.Count - 1;
                    while (i > 0) {
                        int parent = (i - 1) / 2;
                        if (!Less(item, heap[parent])) break;
                        heap[i] = heap[parent]; i = parent;
                    }
                    heap[i] = item;
                }

                internal int Dequeue()
                {
                    if (!worstFirst || heap.Count == 0) return fifo.Dequeue();
                    var top = heap[0];
                    var last = heap[heap.Count - 1];
                    heap.RemoveAt(heap.Count - 1);
                    int n = heap.Count, i = 0;
                    if (n > 0) {
                        while (true) {
                            int c = 2 * i + 1;
                            if (c >= n) break;
                            if (c + 1 < n && Less(heap[c + 1], heap[c])) ++c;
                            if (!Less(heap[c], last)) break;
                            heap[i] = heap[c]; i = c;
                        }
                        heap[i] = last;
                    }
                    return top.t;
                }
            }
            readonly HashSet<long> unsplittable = new HashSet<long>();
            readonly HashSet<long> exempt = new HashSet<long>();
            long limit, inserted, splits;
            /// <summary> vertices below this id existed before refinement: input points and crossings, the apexes of input angles </summary>
            int firstSteiner;

            /// <summary> with a snap tolerance: the snapped input's grid, on which every vertex refinement adds must lie (owner decision
            /// 2026-11-01, MESHING2D_SNAP.md section 3g); null otherwise </summary>
            readonly SnapRounding2 snap;
            readonly SnapGrid grid;
            /// <summary> triangles whose rounded circumcentre was refused (its pixel holds a vertex or is crossed by a constraint) </summary>
            internal long GridBlocked;
            /// <summary> subsegments with no valid split point on the grid </summary>
            internal long GridUnsplittable;

            internal Refiner(QualityMesh2 options, ConstrainedDelaunayMesh2.Builder builder, SnapRounding2 snap = null)
            {
                this.options = options; b = builder; this.snap = snap; grid = snap?.Grid;
                double s = Math.Sin(options.MinAngle * Math.PI / 180);
                sin2Min = s * s;
                halfCot = HalfCot(sin2Min);
                offCentres = OffCentresForBenchmarks ?? true;
                keepChainsApart = KeepChainsApartForTests ?? true;
                keepPinchFree = KeepPinchFreeForTests ?? keepChainsApart;
                deleteInCircle = DeleteInCircleForBenchmarks ?? true;
                // (without an angle bound the order and the split point stay as before: size refinement takes circumcentres)
                triangles = new TriangleQueue(this, WorstFirstForBenchmarks ?? options.MinAngle > 0);
                bestCandidate = BestCandidateForBenchmarks ?? options.MinAngle > 0;
                petal = PetalForBenchmarks ?? true;
                // lenses only for constrained Delaunay and up to the bound proven for them, arcsin(sqrt 3 / 4) = 25.65 degrees, ie
                // sin^2 <= 3/16 (exact in doubles); above it the diametral circle (owner decision 2026-11-02, rule 3i)
                lenses = LensesForBenchmarks ?? (!options.Conforming && options.MinAngle > 0 && sin2Min <= 3.0 / 16);
            }

            static int Next(int e) => e % 3 == 2 ? e - 2 : e + 1;
            static long Key(int a, int c) => a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;

            /// <summary> vertex v lies on a hull chain: one of its constrained edges carries a hull source </summary>
            bool OnHullChain(int v)
            {
                foreach (int e in k.Star(v)) {
                    if (k.Seg[e] < 0) continue;
                    foreach (int src in k.Sources[k.Seg[e]]) if (b.sources[src].Hull) return true;
                }
                return false;
            }

            bool InDomain(int t) => k.Tag[t] >= 0 && !k.IsGhost(t);

            /// <summary>
            /// the input segments (no ring, no hull source) with a constrained edge that has no domain triangle on either side and lies on
            /// the domain's boundary (the edge also carries a ring or hull source): a part that collapsed onto its boundary. A free
            /// segment outside the domain shares no edge with the boundary along that piece and does not count.
            /// </summary>
            HashSet<int> SegmentsWithoutArea()
            {
                var result = new HashSet<int>();
                int domainRings = b.outerRings + b.holeRings;
                for (int e = 0; e < 3 * k.TriangleSlots; ++e) {
                    int f = k.H[e];
                    if (f < e || k.Seg[e] < 0 || InDomain(e / 3) || InDomain(f / 3)) continue;
                    var srcs = k.Sources[k.Seg[e]];
                    if (!srcs.Any(src => b.sources[src].Hull || b.sources[src].Ring >= 0 && b.sources[src].Ring < domainRings)) continue;
                    foreach (int src in srcs) if (b.sources[src].Ring < 0 && !b.sources[src].Hull) result.Add(src);
                }
                return result;
            }

            /// <summary> test hook: constraints lifted and vertices deleted by carves, summed over runs on this thread </summary>
            [ThreadStatic] internal static long LiftedForTests, CarvedForTests;

            /// <summary>
            /// Removes what lies outside the domain before refinement (spec Q7: a hole removes everything inside its ring; O2: the
            /// output's triangles are judged against the output's vertices, owner 2026-11-03). A constraint with the outside on both
            /// sides is lifted if it is a piece of free segments (rings stay), and the triangulation made constrained Delaunay again across
            /// it; then every made vertex (a crossing, a hot pixel centre that snap rounding added) whose triangles all lie outside is
            /// deleted. Input points stay. Left in place, such content
            /// was split by the conforming queue, encroached on the domain's segments and blocked split points on the grid (T15),
            /// though nothing of it reaches the output.
            /// </summary>
            void CarveOutside()
            {
                var lifted = new List<long>();
                for (int e = 0; e < 3 * k.TriangleSlots; ++e) {
                    int f = k.H[e];
                    if (f < e || k.Seg[e] < 0) continue;
                    int t = e / 3, u = f / 3;
                    if (k.IsGhost(t) || k.IsGhost(u) || k.Tag[t] >= 0 || k.Tag[u] >= 0) continue;
                    // only pieces of free segments: a ring's edges stay, also where both sides are outside (a hole reaching beyond the
                    // outer ring, D5), so that ring parity (InsideFromScratch, the tag check) still sees every ring
                    if (k.Sources[k.Seg[e]].Any(src => b.sources[src].Ring >= 0)) continue;
                    k.Unconstrain(e);
                    lifted.Add(Key(k.V[e], k.V[Next(e)]));
                }
                if (lifted.Count == 0) return;
                LiftedForTests += lifted.Count;
                k.RestoreDelaunay(lifted);
                // made vertices: those of the constrained phase, and on the grid the hot pixel centres snap rounding appended (input
                // points stay); DeleteVertex refuses a vertex on a constraint, on the hull, or with a triangle inside the domain
                int firstMadeInput = snap?.InputCount ?? int.MaxValue;
                for (int v = firstMadeInput == int.MaxValue ? k.N + 1 : 0; v < k.VertexCount; ++v)
                    if (v != k.Infinite && OutsideOnly(v) && k.DeleteVertex(v, firstMadeInput)) ++CarvedForTests;
            }

            bool OutsideOnly(int v)
            {
                bool any = false;
                foreach (int e in k.Star(v)) {
                    int t = e / 3;
                    if (k.IsGhost(t) || k.Tag[t] >= 0) return false;
                    any = true;
                }
                return any;
            }

            internal ConstrainedTriangulation2Result Run(ConstrainedDelaunayMesh2.Builder builder)
            {
                k = b.k;
                int segmentCount = (b.input.Segments?.Count ?? 0);
                foreach (var r in b.input.Rings ?? new List<Mesh2Ring>()) segmentCount += r.Vertices.Length;
                limit = options.SteinerLimit ?? 10L * (b.n + segmentCount) + 100000;
                // (hull chains appended for refinement on the grid: refinement runs, also where the snapped input lost the region area
                // that asked for it, so that the hull is constrained and flooded as on every grid path; review)
                bool any = options.MinAngle > 0 || options.MaxArea != null || options.SizeFunction != null || HasRegionArea() || options.Conforming || b.HullFromSegments;
                if (!any) return null;
                var hullFailure = ConstrainHull();
                if (hullFailure != null) return hullFailure;
                // (Q17: what snapping already collapsed, to tell it apart from what refinement pinches; taken before the carve, which
                // lifts such pieces when the outside lies on both sides of them)
                var collapsedBySnapping = grid != null ? SegmentsWithoutArea() : null;
                CarveOutside();
                firstSteiner = k.VertexCount;
                k.Dirty = new List<int>();
                QueueAll();
                long steps = 0;
                int sweepStart = k.VertexCount;
                while (true) {
                    if (segments.Count == 0 && triangles.Count == 0) {
                        // On the grid a refusal is not final: a split point or circumcentre refused because a constraint crossed its pixel
                        // becomes valid when that constraint is split and its pieces bend away (found by the grid fuzz test). When the
                        // queues drain after a sweep that inserted vertices, the refusals are dropped and everything is looked at again;
                        // a sweep without insertions ends refinement, so the counts reported are those of the final sweep. Each further
                        // sweep needs an insertion in the one before, and insertions are bounded by the grid and the Steiner limit.
                        if (grid == null || k.VertexCount == sweepStart || (unsplittable.Count == 0 && exempt.Count == 0)) break;
                        sweepStart = k.VertexCount;
                        unsplittable.Clear(); exempt.Clear();
                        GridBlocked = 0; GridUnsplittable = 0;
                        QueueAll();
                    }
                    if ((++steps & 255) == 0) b.cancel.ThrowIfCancellationRequested();
                    // every vertex made since the input counts, crossings included (O7: the limit counts every Steiner point of the run)
                    // (at the limit an insertion is refused and reports the limit; the queues then drain without inserting, so the flag
                    // is set only when a bound was left unmet, not on a mesh that needs nothing; review of M3)
                    inserted = k.VertexCount - (k.N + 1);
                    // every step either inserts, splits or retires an entry; a cap far above that only stops a defect, which is
                    // reported as one, not as the Steiner limit (review of M3)
                    if (steps > 64 * (limit + k.TriangleSlots) + 100000)
                        return b.Fail(Triangulation2FailureKind.RefinementLimit, null, "refinement did not settle within " + steps + " steps");
                    if (segments.Count > 0) {
                        long key = segments.Dequeue();
                        int e = k.FindEdge((int)(key >> 32), (int)(key & 0xffffffff));
                        if (e < 0 || k.Seg[e] < 0 || !Encroached(e)) continue;
                        splitCause = 0;
                        var failure = SplitSegment(e);
                        if (failure != null) return failure;
                        continue;
                    }
                    int t0 = triangles.Dequeue();
                    // (a deletion frees triangle slots: an entry past the end is stale; one whose slot was refilled is judged as it is now,
                    // and the triangle that moved out of the last slot was queued under its new slot)
                    if (t0 >= k.TriangleSlots || !InDomain(t0) || !Bad(t0)) continue;
                    var fail = SplitTriangle(t0);
                    if (fail != null) return fail;
                }
                CheckTagsNow();
                k.Dirty = null;
                // a hull tip narrower than a pixel may collapse (owner decision 2026-11-02, Q-H4): both hull chains run through one hot
                // pixel next to the corner, and the corner and the vertices of the shared chain are left in no triangle; reported, as
                // snapping reports collapsed segments
                if (b.HullFromSegments) {
                    var inDomain = new HashSet<int>();
                    for (int t = 0; t < k.TriangleSlots; ++t)
                        if (InDomain(t)) for (int j = 0; j < 3; ++j) inDomain.Add(k.V[3 * t + j]);
                    int collapsed = 0;
                    for (int v = 0; v < firstSteiner; ++v) if (v != k.Infinite && !inDomain.Contains(v) && OnHullChain(v)) ++collapsed;
                    if (collapsed > 0) b.diagnostics.Add("snap grid: " + collapsed + " vertices of the hull chains in no triangle (hull tips narrower than a pixel collapsed)");
                }
                // parts narrower than a pixel may collapse (owner decision 2026-11-02, spec Q17): input segments with a piece that has no
                // triangle on either side, those without one already after snapping and those that lost theirs in refinement (a split
                // point of one side's chain landing on the other side's, say) reported apart
                if (collapsedBySnapping != null) {
                    var collapsedAfter = SegmentsWithoutArea();
                    int pinched = collapsedAfter.Count(s => !collapsedBySnapping.Contains(s));
                    if (collapsedBySnapping.Count > 0) b.diagnostics.Add("snap grid: " + collapsedBySnapping.Count + " input segments with pieces on parts that snapping collapsed (no triangle on either side after snapping)");
                    if (pinched > 0) b.diagnostics.Add("snap grid: " + pinched + " input segments with pieces on parts that refinement pinched (a triangle on a side after snapping, none after refinement)");
                }
                if (CountsForBenchmarks != null) { CountsForBenchmarks[5] += GridBlocked; CountsForBenchmarks[6] += GridUnsplittable; }
                if (grid != null && (GridBlocked > 0 || GridUnsplittable > 0))
                    b.diagnostics.Add("snap grid: " + GridBlocked + " triangles kept because the pixel of their circumcentre (or off-centre and then circumcentre) is taken or crossed by a constraint, "
                        + GridUnsplittable + " subsegments with no split point on the grid");
                // the result is a constrained Delaunay triangulation in any case, also when the limit stopped refinement
                var bad = k.NonDelaunayEdges();
                if (bad.Count > 0) k.RestoreDelaunay(bad);
                return null;
            }

            bool HasRegionArea()
            {
                foreach (var r in b.input.Regions ?? new List<Mesh2Region>()) if (r.MaxArea != null) return true;
                return false;
            }

            /// <summary> without outer rings the hull is the boundary (D13): its edges become constraints, so it does not grow </summary>
            ConstrainedTriangulation2Result ConstrainHull()
            {
                if (b.outerRings > 0 || b.OuterRingsCollapsed) return null;
                var pieces = new List<ConstrainedDelaunayMesh2.Builder.Piece>();
                // (on the grid the hull edges came in as snapped segments, hull sources already: only the flood below)
                for (int t = 0; t < k.TriangleSlots && !b.HullFromSegments; ++t) {
                    if (!k.IsGhost(t)) continue;
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.V[e] == k.Infinite || k.V[Next(e)] == k.Infinite || k.Seg[e] >= 0) continue;
                        // each hull edge its own source with its coordinates, so that splits are computed on the original edge
                        // e runs along the ghost, the domain on its right: stored reversed, the domain is on the left of A -> B
                        int a0 = k.V[Next(e)], b0 = k.V[e];
                        b.sources.Add(new ConstrainedDelaunayMesh2.Source { Ring = -1, Hull = true, AX = k.X[a0], AY = k.Y[a0], BX = k.X[b0], BY = k.Y[b0], VA = a0, VB = b0 });
                        pieces.Add(new ConstrainedDelaunayMesh2.Builder.Piece { U = a0, W = b0, Sources = new List<int> { b.sources.Count - 1 } });
                    }
                }
                // through the builder's piece insertion: a vertex within the tolerance of a hull edge becomes a vertex of the boundary
                // (O11; a point a few ulps inside a hull edge would otherwise encroach it for ever)
                var pending = new Stack<ConstrainedDelaunayMesh2.Builder.Piece>();
                long guard = 0;
                foreach (var piece in pieces) {
                    pending.Push(piece);
                    while (pending.Count > 0) {
                        if (++guard > 100000 + 16L * pieces.Count) return b.Fail(Triangulation2FailureKind.NodingLimit, null, "the hull edges did not settle");
                        var failure = b.InsertPiece(pending.Pop(), pending);
                        if (failure != null) return failure;
                    }
                }
                // what lies between such a boundary chain and the old hull edge is outside: reachable from a ghost without crossing a constraint
                var queue = new Queue<int>();
                for (int t = 0; t < k.TriangleSlots; ++t) if (k.IsGhost(t)) queue.Enqueue(t);
                while (queue.Count > 0) {
                    int t = queue.Dequeue();
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.Seg[e] >= 0) continue;
                        int t2 = k.H[e] / 3;
                        if (k.IsGhost(t2) || k.Tag[t2] < 0) continue;
                        k.Tag[t2] = -1;
                        queue.Enqueue(t2);
                    }
                }
                return null;
            }

            /// <summary>
            /// the triangles of the domain, and the constrained edges that may be encroached: of the domain, and with Conforming of every
            /// triangle, since there a vertex anywhere encroaches (Encroached), and a constraint inside a hole that is encroached and left
            /// as it is hides a vertex across the hole from the domain's edges (found with the grid fuzz with holes: a segment inside a
            /// hole, between a hole edge and a vertex on the opposite hole edge, left the domain not Delaunay across the hole)
            /// </summary>
            void QueueAll()
            {
                for (int t = 0; t < k.TriangleSlots; ++t) {
                    if (InDomain(t)) triangles.Enqueue(t);
                    if (options.Conforming ? !k.IsGhost(t) : InDomain(t)) QueueEdges(t);
                }
            }

            void QueueEdges(int t)
            {
                for (int j = 0; j < 3; ++j) {
                    int e = 3 * t + j;
                    if (k.Seg[e] >= 0) segments.Enqueue(Key(k.V[e], k.V[Next(e)]));
                }
            }

            /// <summary> the piece a split lifted: its sources, its ends, and the tags on its left and right before the split </summary>
            struct LiftedPiece
            {
                internal List<int> Sources;
                internal Vector2d U, W;
                internal int Left, Right;
            }

            int TagOf(int t) => k.IsGhost(t) ? -1 : k.Tag[t];

            /// <summary> the tag the triangle of half-edge h takes from the lifted piece, if h lies on it; int.MinValue otherwise </summary>
            int TagFromPiece(int h, LiftedPiece? lifted)
            {
                if (lifted == null || k.Seg[h] < 0) return int.MinValue;
                var piece = lifted.Value;
                // only an edge with exactly the piece's sources: one that also carries another constraint (a split point that rounded
                // onto a constraint through the same end, at a tiny angle) closed the wedge between them, and its sides need not have
                // the tags the piece's sides had (found with the corpus run, earcut's water-huge)
                var own = k.Sources[k.Seg[h]];
                if (own.Count != piece.Sources.Count) return int.MinValue;
                foreach (int s in own) if (!piece.Sources.Contains(s)) return int.MinValue;
                // on the lifted piece: both ends between its ends (the pieces of a split lie on it, the other pieces of its source do not)
                // (in a scale by a power of two: the squares do not underflow at small magnitudes)
                var d0 = piece.W - piece.U;
                int ex = ScaleOf(d0.x, d0.y, 0, 0);
                var d = ScaleB(d0, -ex); double l2 = d.LengthSquared;
                var a = ScaleB(k.Point(k.V[h]) - piece.U, -ex); var c = ScaleB(k.Point(k.V[Next(h)]) - piece.U, -ex);
                double ta = a.Dot(d), tc = c.Dot(d);
                double slack = l2 * 1e-9;
                if (ta < -slack || tc < -slack || ta > l2 + slack || tc > l2 + slack) return int.MinValue;
                return (c - a).Dot(d) > 0 ? piece.Left : piece.Right;
            }

            /// <summary>
            /// tests only: after operations compare the tags with domain membership computed from scratch (slow). Per thread, so
            /// that a test switching it on does not reach meshers that other tests run in parallel (refinement runs on the calling thread)
            /// </summary>
            [ThreadStatic] internal static bool CheckTagsForTests;
            /// <summary> tests only: splits that rerouted a piece through a vertex already there (counted per thread) </summary>
            [ThreadStatic] internal static long ReroutesForTests;
            string lastOperation;
            long operations;

            // tests only: the check recomputes the domain from scratch, so it is quadratic in the number of operations when it runs after
            // each one. On meshes of this many triangles or more it runs after operations 1, 2, 4, 8, ... and at the end of refinement (a wrong tag that heals between two checks goes unseen there; smaller meshes are checked after every operation)
            const int DenseCheckBelowTriangles = 2000;

            void CheckTags()
            {
                ++operations;
                if (!CheckTagsForTests || (b.input.HoleSeeds?.Count ?? 0) > 0) return;
                if (k.TriangleSlots >= DenseCheckBelowTriangles && (operations & (operations - 1)) != 0) return;
                CheckTagsNow();
            }

            void CheckTagsNow()
            {
                if (!CheckTagsForTests || (b.input.HoleSeeds?.Count ?? 0) > 0) return;
                var inside = b.InsideFromScratch();
                // The rule "a ghost neighbour counts as outside" in Collect reads -1 and not the ghost's stored tag, because every ghost's tag
                // is -1; with that invariant the two are the same (so are ignoring the ghost, since the sliver is also tagged from the sides
                // of the lifted piece: both mutants survive the tag tests, evidence in the PR #447). A change that breaks it fails here.
                for (int t = 0; t < inside.Length; ++t)
                    if (k.IsGhost(t) && k.Tag[t] != -1)
                        throw new InvalidOperationException("tag of ghost triangle " + t + " is " + k.Tag[t] + ", not -1, after operation " + operations + ": " + lastOperation);
                for (int t = 0; t < inside.Length; ++t)
                    if (!k.IsGhost(t) && (k.Tag[t] >= 0) != inside[t])
                        throw new InvalidOperationException("tag of triangle " + t + " (" + string.Join(" ", new[] { k.V[3 * t], k.V[3 * t + 1], k.V[3 * t + 2] }.Select(q => q + "=(" + k.X[q].ToString("R") + ", " + k.Y[q].ToString("R") + ")")) + ") is " + k.Tag[t]
                            + ", inside " + inside[t] + ", edges constrained " + k.Seg[3 * t] + " " + k.Seg[3 * t + 1] + " " + k.Seg[3 * t + 2]
                            + ", neighbour tags " + TagOf(k.H[3 * t] / 3) + " " + TagOf(k.H[3 * t + 1] / 3) + " " + TagOf(k.H[3 * t + 2] / 3)
                            + ", after operation " + operations + ": " + lastOperation);
            }

            /// <summary> after an insertion: the rewritten triangles may be bad, their constrained edges encroached </summary>
            void Collect(LiftedPiece? lifted = null)
            {
                // Tags change only across constraints. A split briefly lifts the constraint of the piece it splits, and the flips around
                // it may then cross the boundary, so the rewritten triangles take their tags anew: from the unchanged triangles they are
                // joined to by unconstrained edges (a breadth-first search into the rewritten region), and from the sides of the
                // pieces of the lifted piece, which keep the tags its sides had (a small hole or region enclosed by constraints has no
                // unchanged neighbour through an unconstrained edge; found by the review of M3). Any other rewritten triangle keeps its tag.
                var previous = new Dictionary<int, int>();
                // (a slot a deletion freed after it was written is past the end now)
                foreach (int t in k.Dirty) if (t < k.TriangleSlots && !k.IsGhost(t) && !previous.ContainsKey(t)) { previous.Add(t, k.Tag[t]); k.Tag[t] = int.MinValue; }
                var queue = new Queue<int>();
                foreach (int t in previous.Keys)
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        int fromPiece = TagFromPiece(e, lifted);
                        if (fromPiece != int.MinValue) {
                            if (k.Tag[t] == int.MinValue) { k.Tag[t] = fromPiece; queue.Enqueue(t); }
                            continue;
                        }
                        if (k.Seg[e] >= 0) continue;
                        int t2 = k.H[e] / 3;
                        if (k.Tag[t2] == int.MinValue) continue;
                        // a ghost counts as an unchanged neighbour outside: a sliver left between a rerouted boundary piece and the old,
                        // now unconstrained hull edge is outside the domain (found with M3, F-39)
                        int tag = k.IsGhost(t2) ? -1 : k.Tag[t2];
                        if (k.Tag[t] == int.MinValue) { k.Tag[t] = tag; queue.Enqueue(t); }
                    }
                while (queue.Count > 0) {
                    int t = queue.Dequeue();
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.Seg[e] >= 0) continue;
                        int t2 = k.H[e] / 3;
                        if (k.IsGhost(t2) || k.Tag[t2] != int.MinValue) continue;
                        k.Tag[t2] = k.Tag[t];
                        queue.Enqueue(t2);
                    }
                }
                foreach (var pair in previous) if (k.Tag[pair.Key] == int.MinValue) k.Tag[pair.Key] = pair.Value;
                CheckTags();
                foreach (int t in k.Dirty) {
                    if (t >= k.TriangleSlots || k.IsGhost(t)) continue;
                    triangles.Enqueue(t);
                    QueueEdges(t);
                }
                k.Dirty.Clear();
            }

            bool Locked(int e)
            {
                foreach (int s in k.Sources[k.Seg[e]]) if (b.sources[s].Locked) return true;
                return false;
            }

            /// <summary> a vertex of a triangle in the domain at the edge lies strictly inside its diametral circle (exact) </summary>
            bool Encroached(int e)
            {
                if (Locked(e) || unsplittable.Contains(Key(k.V[e], k.V[Next(e)]))) return false;
                foreach (int h in new[] { e, k.H[e] }) {
                    int t = h / 3;
                    // conforming (O2) asks for empty circumcircles against all vertices, so a vertex of a hole or of the outside
                    // encroaches too
                    if (options.Conforming ? k.IsGhost(t) : !InDomain(t)) continue;
                    int apex = k.V[Next(Next(h))];
                    if (EncroachesOn(k.Point(k.V[h]), k.Point(k.V[Next(h)]), k.Point(apex))) {
                        bool exemptHere = ApexExempt(e, apex);
                        ApexLogForTests?.Add((k.Point(k.V[e]), k.Point(k.V[Next(e)]), k.Point(apex), exemptHere));
                        if (!exemptHere) return true;
                        // the conforming mode (O2) leaves a subsegment unsplit for an exempt vertex only while it is a Delaunay edge
                        // (MESHING2D_SMALL_ANGLES_CONFORMING.md, rule C); else the exemption is revoked and the split is needed by every
                        // conforming solution
                        if (options.Conforming && ConformingDelaunayCheckForTests != false && !LocallyDelaunayAt(e)) { Note(16); return true; }
                    }
                }
                return false;
            }

            /// <summary>
            /// the subsegment e is a locally Delaunay edge of the kernel's triangulation: the apex of each of its two triangles (holes and
            /// outside included, as the conforming mode counts them) is not strictly inside the circumcircle of the other (exact). A ghost
            /// side counts as Delaunay (a hull edge: the lemma of Delaunay's theorem needs no test there). With every constrained edge
            /// locally Delaunay at the end, the kernel's triangulation is the Delaunay triangulation of all vertices (O2).
            /// </summary>
            bool LocallyDelaunayAt(int e)
            {
                int h2 = k.H[e];
                if (h2 < 0 || k.IsGhost(e / 3) || k.IsGhost(h2 / 3)) return true;
                return IsLocallyDelaunay(k.Point(k.V[e]), k.Point(k.V[Next(e)]), k.Point(k.V[Next(Next(e))]), k.Point(k.V[Next(Next(h2))]));
            }

            /// <summary>
            /// the edge ab with the apexes c and d on its two sides is locally Delaunay: d is not strictly inside the circumcircle of abc
            /// (exact; a cocircular pair counts as Delaunay, as in the oracle and in the Delaunay lemma)
            /// </summary>
            internal static bool IsLocallyDelaunay(Vector2d a, Vector2d b, Vector2d c, Vector2d d) => ExactPredicates.InCircle(a, b, c, d) <= 0;

            /// <summary> tests only: false drops the Delaunay check of rule C in the conforming mode (the exemption then breaks O2; the tests show it) </summary>
            [ThreadStatic] internal static bool? ConformingDelaunayCheckForTests;

            /// <summary> tests only: false turns the small-angle exemption of ApexExempt off </summary>
            [ThreadStatic] internal static bool? SmallAngleExemptionForTests;
            /// <summary> tests only: every vertex encroachment Encroached looks at (the subsegment's ends, the vertex, whether it is exempt) </summary>
            [ThreadStatic] internal static List<(Vector2d U, Vector2d W, Vector2d V, bool Exempt)> ApexLogForTests;

            /// <summary>
            /// The small-angle rule (MESHING2D_SMALL_ANGLES.md, owner decision 2026-11-06; in the conforming mode with the Delaunay check of Encroached, MESHING2D_SMALL_ANGLES_CONFORMING.md, owner decision 2026-11-08): subsegment e is
            /// not split for vertex v when all of these hold.
            /// - v is a vertex refinement made on exactly one source c: not an input vertex, not a crossing, no third source (Shewchuk 2002
            ///   section 6.2 applies its cure only then: not for a parent that "is an input vertex or lies on a nonincident segment").
            /// - c and a source a of e make an input angle below 60 degrees (SmallPairSin2; the bound until 2026-11-09), their closed input segments meeting exactly (PairOf).
            /// - (i) an end u of e is a vertex on c too (e starts at the apex, or at the crossing of a and c), and v lies on e's side of u;
            ///   or (ii) v and an end of e lie at distances from the apex within a factor of 1.5, on the same side (NearSmallAngle).
            /// Splitting e for such a v is the partner's follow-up in section 6.2's figure-eight cycle (a circumcentre halves a subsegment at
            /// the apex, the partner follows by encroachment, and the next free edge is shorter; found with ext/cdt/island: 38 octaves of
            /// splits towards a 2.6 degree apex of a hull edge and a segment). Only this vertex encroachment is exempt: a circumcentre or
            /// off-centre that is behind e or would encroach it still splits it, and the rule excuses no triangle.
            /// </summary>
            bool ApexExempt(int e, int v)
            {
                // the conforming mode (rule C) has no lenses and no Chew deletion; with lenses forced on by a benchmark the rule stays off there.
                // On the snap grid rule C is on too (MESHING2D_SMALL_ANGLES_CONFORMING.md section 8): a split it forces late can be refused by
                // the pinch rule of ValidGridSplit, and the oracle excuses those subsegments and may demand more (its pinch rule judges chain identity
                // from the output's subsegment graph, Oracle.OwnStretch, as the mesher goes by source)
                if (options.Conforming && lenses || !(sin2Min > 0) || SmallAngleExemptionForTests == false || k.Seg[e] < 0) return false;
                // v is a vertex refinement made on one source c, nothing else: not an input vertex, not a crossing, no third source
                // (Shewchuk 2002 section 6.2 splits for a parent that is an input vertex or lies on a nonincident segment)
                if (v < firstSteiner) return false;
                var sv = SourcesAt(v);
                if (sv.Count != 1) return false;
                int c = sv[0];
                foreach (int a in k.Sources[k.Seg[e]]) {
                    if (a == c) continue;
                    var pair = PairOf(a, c);
                    if (!pair.Meets) continue;
                    for (int end = 0; end < 2; ++end) {
                        int u = end == 0 ? k.V[e] : k.V[Next(e)], w = end == 0 ? k.V[Next(e)] : k.V[e];
                        // (i) a shell subsegment: u is on c too, and v on e's side of u (at a crossing the opposite ray is the supplement)
                        if (DotSign(k.Point(v), k.Point(w), k.Point(u)) > 0 && SourcesAt(u).Contains(c)) { Note(pair.Kind == 1 ? 8 : 9); return true; }
                        // (ii) matched radii: v and an end of e at distances from the apex within a factor of 1.5, on the same side
                        if (NearSmallAngle(a, c, k.Point(u), k.Point(v), strict: false)) { Note(pair.Kind == 1 ? 8 : 9); return true; }
                    }
                }
                return false;
            }

            /// <summary> benchmarks: one more in CountsForBenchmarks[slot] (8 apex exemptions at a shared endpoint, 9 at a T-junction or a
            /// crossing; 10-12 triangles given up: no circumcentre, a locked or unsplittable subsegment in the way, no vertex made; 13-15
            /// subsegments left unsplit off the grid: below MinRoundedSplitExponent, no progress, near another source; 16 exemptions revoked in the
            /// conforming mode because the subsegment was not locally Delaunay) </summary>
            static void Note(int slot)
            {
                var n = CountsForBenchmarks;
                if (n != null && slot < n.Length) ++n[slot];
            }

            /// <summary> sign of (a - p) . (b - p), exact: a floating-point filter, then integers </summary>
            internal static int DotSign(Vector2d a, Vector2d c, Vector2d p)
            {
                double ax = a.x - p.x, ay = a.y - p.y, cx = c.x - p.x, cy = c.y - p.y;
                double l = ax * cx, r = ay * cy, d = l + r;
                double bound = (Math.Abs(l) + Math.Abs(r)) * 4.5e-16;   // 2 roundings of the differences, 3 of the products and the sum
                if (d > bound && Math.Abs(d) > 1e-290) return 1;
                if (-d > bound && Math.Abs(d) > 1e-290) return -1;
                double[] v = { a.x, a.y, c.x, c.y, p.x, p.y };
                var m = new BigInteger[6];
                var ex = new int[6];
                int emin = int.MaxValue;
                for (int i = 0; i < 6; ++i) { ExactPoint3.Decompose(v[i], out m[i], out ex[i]); if (!m[i].IsZero) emin = Math.Min(emin, ex[i]); }
                if (emin == int.MaxValue) return 0;
                var q = new BigInteger[6];
                for (int i = 0; i < 6; ++i) q[i] = m[i].IsZero ? BigInteger.Zero : m[i] << (ex[i] - emin);
                return ((q[0] - q[4]) * (q[2] - q[4]) + (q[1] - q[5]) * (q[3] - q[5])).Sign;
            }

            /// <summary> benchmarks and tests only (per thread): lenses on or off regardless of the rule; null: the rule </summary>
            [ThreadStatic] internal static bool? LensesForBenchmarks;

            /// <summary> p encroaches on the subsegment ac: p lies in its open diametral circle, or with lenses in its closed diametral
            /// lens </summary>
            bool EncroachesOn(Vector2d a, Vector2d c, Vector2d p) => lenses ? InLens(a, c, p) : DotSign(a, c, p) < 0;

            /// <summary>
            /// p lies in the closed diametral lens of ac (Shewchuk 2002, section 6.4: the intersection of two disks of radius |ac| / sqrt 3
            /// centred on the bisector, |ac| / (2 sqrt 3) from ac on either side; its boundary sees ac under 120 degrees): the angle apc is
            /// at least 120 degrees, ie (pa . pc) &lt; 0 and 4 (pa . pc)^2 &gt;= |pa|^2 |pc|^2. Exact: a floating-point filter, then integers.
            /// The boundary counts as inside, as in the paper; points exactly on it have irrational coordinates, so in doubles it decides
            /// nothing.
            /// </summary>
            internal static bool InLens(Vector2d a, Vector2d c, Vector2d p)
            {
                if (DotSign(a, c, p) >= 0) return false;
                double ax = a.x - p.x, ay = a.y - p.y, cx = c.x - p.x, cy = c.y - p.y;
                double d = ax * cx + ay * cy, la = ax * ax + ay * ay, lc = cx * cx + cy * cy;
                double lhs = 4 * d * d, rhs = la * lc, scale = lhs + rhs;
                // with |pa|^2 and |pc|^2 each in the normal range, their errors are a few ulps, and the error of d is at most some 4 ulps of
                // |pa| |pc| (Cauchy-Schwarz; d itself may cancel), so lhs and rhs are within some 40 ulps of rhs <= scale: a relative margin
                // of 2^-40 decides safely. A factor near underflow loses its relative accuracy (review: |pa|^2 subnormal next to a large
                // |pc|^2), so each factor is checked, not only the sum; outside the range, or closer, integers decide
                if (la > 1e-280 && lc > 1e-280 && la < 1e140 && lc < 1e140 && Math.Abs(lhs - rhs) > scale * (1.0 / (1L << 40))) return lhs > rhs;
                double[] v = { a.x, a.y, c.x, c.y, p.x, p.y };
                var m = new BigInteger[6];
                var ex = new int[6];
                int emin = int.MaxValue;
                for (int i = 0; i < 6; ++i) { ExactPoint3.Decompose(v[i], out m[i], out ex[i]); if (!m[i].IsZero) emin = Math.Min(emin, ex[i]); }
                if (emin == int.MaxValue) return false;
                var q = new BigInteger[6];
                for (int i = 0; i < 6; ++i) q[i] = m[i].IsZero ? BigInteger.Zero : m[i] << (ex[i] - emin);
                BigInteger ux = q[0] - q[4], uy = q[1] - q[5], wx = q[2] - q[4], wy = q[3] - q[5];
                BigInteger dot = ux * wx + uy * wy;
                return 4 * dot * dot >= (ux * ux + uy * uy) * (wx * wx + wy * wy);
            }

            // ------------------------------------------------------------ bad triangles

            /// <summary> floor(log2 |x|) for finite x != 0, from the bits </summary>
            internal static int Exponent(double x)
            {
                long bits = BitConverter.DoubleToInt64Bits(Math.Abs(x));
                int e = (int)((bits >> 52) & 0x7FF);
                if (e != 0) return e - 1023;
                bits = BitConverter.DoubleToInt64Bits(Math.Abs(x) * 18446744073709551616.0);   // subnormal: times 2^64 (exact)
                return (int)((bits >> 52) & 0x7FF) - 1023 - 64;
            }

            /// <summary> the exponent that brings the largest of the values to [1, 2), 0 if all are zero </summary>
            static int ScaleOf(double a, double b, double c, double d)
            {
                double m = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d)));
                return m > 0 ? Exponent(m) : 0;
            }

            static Vector2d ScaleB(Vector2d v, int n) => new Vector2d(ExactPoint3.ScaleB(v.x, n), ExactPoint3.ScaleB(v.y, n));

            /// <summary>
            /// The triangle's edges from its first vertex, scaled by a power of two to unit size: the products of the angle and
            /// circumcentre computations then neither underflow nor overflow, and an input scaled by 2^k gives the same values bit
            /// for bit (review of M3: at 2^-500 the squared lengths underflowed and no triangle counted as bad)
            /// </summary>
            Vector2d Local(int t, out Vector2d q, out Vector2d r, out int ex)
            {
                var a = k.Point(k.V[3 * t]);
                var d1 = k.Point(k.V[3 * t + 1]) - a; var d2 = k.Point(k.V[3 * t + 2]) - a;
                ex = ScaleOf(d1.x, d1.y, d2.x, d2.y);
                q = ScaleB(d1, -ex); r = ScaleB(d2, -ex);
                return a;
            }

            /// <summary> the area of the scaled triangle of <see cref="Local"/> </summary>
            static double LocalArea(Vector2d q, Vector2d r) => 0.5 * (q.x * r.y - q.y * r.x);

            double Area(int t)
            {
                Local(t, out var q, out var r, out int ex);
                return ExactPoint3.ScaleB(LocalArea(q, r), 2 * ex);
            }

            static Vector2d Corner(int j, Vector2d q0, Vector2d r0) { j %= 3; return j == 0 ? Vector2d.Zero : j == 1 ? q0 : r0; }

            /// <summary> sin^2 of the smallest angle </summary>
            double MinSin2(int t)
            {
                double best = double.MaxValue;
                Local(t, out var q0, out var r0, out _);
                double area2 = 2 * LocalArea(q0, r0);
                for (int j = 0; j < 3; ++j) {
                    // (the corners 0, q0, r0 without an array: this runs for every triangle queued)
                    var p = Corner(j, q0, r0); var q = Corner(j + 1, q0, r0); var r = Corner(j + 2, q0, r0);
                    double l1 = (q - p).LengthSquared, l2 = (r - p).LengthSquared;
                    double s2 = area2 * area2 / (l1 * l2);
                    if (s2 < best) best = s2;
                }
                return best;
            }

            /// <summary>
            /// the corner of the smallest angle (ties to the first). sin^2 of that angle is the same to a few ulps whichever of two nearly
            /// equal shortest edges is taken, but the corner is not: at a near-180 degree triangle the rounded products l1 l2 can order
            /// the two small angles the wrong way, or tie, and the oracle takes the exactly shortest edge (SpansSmallInputAngle), so the
            /// corner is chosen exactly
            /// </summary>
            int SmallestAngleCorner(int t) => ShortestEdgeCorner(k.Point(k.V[3 * t]), k.Point(k.V[3 * t + 1]), k.Point(k.V[3 * t + 2]));

            /// <summary>
            /// the corner opposite the shortest edge of the triangle abc, ie the corner of its smallest angle, exact (ties: the first). A
            /// floating-point filter, then integers when the two shortest squared lengths are within 2^-40 of each other or out of range
            /// </summary>
            internal static int ShortestEdgeCorner(Vector2d a, Vector2d b, Vector2d c)
            {
                double l0 = (b - c).LengthSquared, l1 = (c - a).LengthSquared, l2 = (a - b).LengthSquared;
                int j = l1 < l0 ? 1 : 0;
                double lj = j == 1 ? l1 : l0;
                if (l2 < lj) { j = 2; lj = l2; }
                // the nearest rival of the minimum (the squared lengths each carry a relative error of a few ulps)
                double rival = j == 0 ? Math.Min(l1, l2) : j == 1 ? Math.Min(l0, l2) : Math.Min(l0, l1);
                bool inRange = l0 > 1e-280 && l1 > 1e-280 && l2 > 1e-280 && l0 < 1e140 && l1 < 1e140 && l2 < 1e140;
                if (inRange && rival - lj > rival * (1.0 / (1L << 40))) return j;
                double[] v = { a.x, a.y, b.x, b.y, c.x, c.y };
                var m = new BigInteger[6];
                var ex = new int[6];
                int emin = int.MaxValue;
                for (int i = 0; i < 6; ++i) { ExactPoint3.Decompose(v[i], out m[i], out ex[i]); if (!m[i].IsZero) emin = Math.Min(emin, ex[i]); }
                if (emin == int.MaxValue) return 0;
                var q = new BigInteger[6];
                for (int i = 0; i < 6; ++i) q[i] = m[i].IsZero ? BigInteger.Zero : m[i] << (ex[i] - emin);
                BigInteger Len2(int p1, int p2) { var dx = q[2 * p1] - q[2 * p2]; var dy = q[2 * p1 + 1] - q[2 * p2 + 1]; return dx * dx + dy * dy; }
                BigInteger e0 = Len2(1, 2), e1 = Len2(2, 0), e2 = Len2(0, 1);
                int best = 0; BigInteger bl = e0;
                if (e1 < bl) { best = 1; bl = e1; }
                if (e2 < bl) best = 2;
                return best;
            }

            /// <summary> for the output's list of bad triangles: below the minimum angle </summary>
            internal bool BelowAngle(int t) => options.MinAngle > 0 && MinSin2(t) < sin2Min;

            /// <summary> the squared length of the longest locked edge of t, in the scale of <see cref="Local"/> </summary>
            double LongestLocked2(int t, Vector2d q, Vector2d r)
            {
                double best = 0;
                var corners = new[] { Vector2d.Zero, q, r };
                for (int j = 0; j < 3; ++j) {
                    int e = 3 * t + j;
                    if (k.Seg[e] >= 0 && Locked(e)) best = Math.Max(best, (corners[(j + 1) % 3] - corners[j]).LengthSquared);
                }
                return best;
            }

            bool Bad(int t)
            {
                if (exempt.Contains(TriangleKey(t))) return false;
                // on the grid: at two pixels or less no better point can be placed, so neither angle nor size is refined (section 3g)
                if (grid != null && BelowFloor(t)) return false;
                var a0 = Local(t, out var q0, out var r0, out int ex);
                double localArea = LocalArea(q0, r0), area = ExactPoint3.ScaleB(localArea, 2 * ex);
                double localLocked2 = LongestLocked2(t, q0, r0), locked2 = ExactPoint3.ScaleB(localLocked2, 2 * ex);
                // a triangle over an edge that may not be split need not shrink below a needle (O5, O9)
                bool areaExempt = localLocked2 > 4 * localArea;
                double? maxArea = options.MaxArea;
                if (k.Tag[t] > 0) {
                    var ra = b.input.Regions[k.Tag[t] - 1].MaxArea;
                    if (ra != null) maxArea = maxArea == null ? ra : Math.Min(maxArea.Value, ra.Value);
                }
                // (a margin of 2^-40 below the bound: the area in doubles may round down below it while the exact area is above)
                if (maxArea != null && area > maxArea.Value * (1 - 1.0 / (1L << 40)) && !areaExempt) return true;
                if (options.SizeFunction != null) {
                    var a = k.Point(k.V[3 * t]); var c = k.Point(k.V[3 * t + 1]); var d = k.Point(k.V[3 * t + 2]);
                    var g = (a + c + d) / 3;
                    double h = options.SizeFunction(g.x, g.y);
                    double longest = ExactPoint3.ScaleB(Math.Max(q0.LengthSquared, Math.Max((r0 - q0).LengthSquared, r0.LengthSquared)), 2 * ex);
                    if (longest > Math.Max(h * h, locked2) * (1 - 1.0 / (1L << 40))) return true;
                }
                return BadByAngle(t);
            }

            /// <summary> t is below the angle bound and that is not forced by the input (the angle part of <see cref="Bad"/>) </summary>
            bool BadByAngle(int t)
            {
                if (options.MinAngle <= 0) return false;
                if (MinSin2(t) >= sin2Min) return false;
                int corner = SmallestAngleCorner(t);
                // the small angle lies between two constrained edges: forced by the input (O3)
                int ea = 3 * t + corner, eb = 3 * t + (corner + 2) % 3;
                if (k.Seg[ea] >= 0 && k.Seg[eb] >= 0) return false;
                // the shortest edge spans a small input angle: its ends lie on two input segments that meet at an angle below 60
                // degrees (SmallPairSin2), at about the same distance from where they meet. Such a triangle cannot be improved without refining towards
                // the apex for ever (Shewchuk 2002, small input angles)
                if (SpansSmallAngle(k.V[3 * t + (corner + 1) % 3], k.V[3 * t + (corner + 2) % 3])) return false;
                return true;
            }

            /// <summary>
            /// benchmarks only (Meshing2Benchmark gridcounts): the floor in pixels instead of two, 0 for none, to measure what the floor
            /// saves (MESHING2D_SNAP.md section 3g, item 6). Per thread, like the tag check switch
            /// </summary>
            [ThreadStatic] internal static double? FloorPixelsForBenchmarks;

            /// <summary> the shortest edge of t is at most two pixels (the angle floor on the grid, MESHING2D_SNAP.md section 3g) </summary>
            bool BelowFloor(int t)
            {
                // every vertex is a pixel centre: the edge lengths in pixels are exact from the pixel indices (doubles of k w round, so a
                // lattice edge of two pixels may differ from 2 w by a hair; review of #277)
                double f = FloorPixelsForBenchmarks ?? 2;
                for (int j = 0; j < 3; ++j) {
                    var (ax, ay) = PixelOf(k.V[3 * t + j]); var (bx, by) = PixelOf(k.V[3 * t + (j + 1) % 3]);
                    double dx = ax - bx, dy = ay - by;
                    if (dx * dx + dy * dy <= f * f) return true;
                }
                return false;
            }

            /// <summary> the pixel indices of vertex v, computed once per refiner: the kernel never moves a vertex after inserting it and
            /// never reuses an id, so the indices stay valid; the exact index is costly (most of the mesher's time on the Herne tiles).
            /// long.MinValue marks an entry not computed yet (an index of -2^63 would only be recomputed each time) </summary>
            (long x, long y) PixelOf(int v)
            {
                while (pixels.Count <= v) pixels.Add((long.MinValue, long.MinValue));
                if (pixels[v].x == long.MinValue) {
                    var p = k.Point(v);
                    long x = grid.Pixel(p.x), y = grid.Pixel(p.y);
                    pixels[v] = (x, y);
                }
                return pixels[v];
            }
            readonly List<(long x, long y)> pixels = new List<(long x, long y)>();

            /// <summary> a constrained edge crosses the open pixel of grid point c (c lies in triangle at): a vertex there would sit within half a
            /// pixel of a constraint (D14) </summary>
            bool ConstraintInPixel(int at, Vector2d c)
            {
                long kx = grid.Pixel(c.x), ky = grid.Pixel(c.y);
                double h = grid.W / 2;
                foreach (int t in TrianglesNear(at, c.x - h, c.y - h, c.x + h, c.y + h))
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.Seg[e] >= 0 && grid.CrossesInterior(k.Point(k.V[e]), k.Point(k.V[Next(e)]), kx, ky)) return true;
                    }
                return false;
            }

            /// <summary>
            /// The real triangles whose bounding box meets the box, found from triangle t0 across all edges. t0 may be a ghost (the outer
            /// side of a hull edge): the search then starts from its real neighbour (a ghost start found nothing, so a grid split of a
            /// hull edge was not checked against the constraints near it, and its pieces crossed another chain at an unrounded vertex;
            /// found with the Herne tiles at snap 0.01)
            /// </summary>
            List<int> TrianglesNear(int t0, double x0, double y0, double x1, double y1)
            {
                var result = new List<int>();
                // (marks by triangle slot with a generation and a reused stack instead of a set and a stack per call: a few allocations
                // per call, from every split point tried on the grid, were most of its time; the result list stays per call, as callers
                // keep it while asking again)
                if (nearMark.Length < k.TriangleSlots) Array.Resize(ref nearMark, Math.Max(k.TriangleSlots, 2 * nearMark.Length));
                int gen = NextGeneration(ref nearGen, nearMark);
                bool Seen(int t) { if (nearMark[t] == gen) return false; nearMark[t] = gen; return true; }
                var stack = nearStack; stack.Clear();
                Seen(t0);
                stack.Push(t0);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    if (k.IsGhost(t)) {
                        if (t == t0) for (int j = 0; j < 3; ++j) { int t2 = k.H[3 * t + j] / 3; if (Seen(t2)) stack.Push(t2); }
                        continue;
                    }
                    double lx = double.MaxValue, ly = double.MaxValue, hx = double.MinValue, hy = double.MinValue;
                    for (int j = 0; j < 3; ++j) { var p = k.Point(k.V[3 * t + j]); lx = Math.Min(lx, p.x); ly = Math.Min(ly, p.y); hx = Math.Max(hx, p.x); hy = Math.Max(hy, p.y); }
                    if (hx < x0 || lx > x1 || hy < y0 || ly > y1) continue;
                    result.Add(t);
                    for (int j = 0; j < 3; ++j) { int t2 = k.H[3 * t + j] / 3; if (Seen(t2)) stack.Push(t2); }
                }
                return result;
            }
            int[] nearMark = new int[0];
            int nearGen;
            readonly Stack<int> nearStack = new Stack<int>();

            /// <summary>
            /// The split point of constrained edge u-w on the grid (MESHING2D_SNAP.md section 3g): the point at the given fraction between
            /// u and w is taken on the input segment the edge comes from (O12), and its pixel's centre is used if it is valid; else the
            /// neighbouring pixels of the input segment's envelope between u and w, nearest first. Valid: no constraint crosses its pixel,
            /// and the new edges u-q, q-w cross no vertex's pixel and no other constraint. A vertex already at q is fine: the chain is
            /// then routed through it, as snap rounding routes a segment through every hot pixel of its envelope (a Steiner point there
            /// would otherwise block the split and leave the subsegment encroached; found by the fuzz test).
            /// </summary>
            bool GridSplitPoint(int e, Vector2d pu, Vector2d pw, List<int> src, double fraction, out Vector2d q)
            {
                q = default(Vector2d);
                // the input segments of all sources of the piece (snapped chains of several input segments can share it): the split point
                // must lie in every one's envelope, so that no chain leaves its own (found by the fuzz test)
                var origins = new List<(Vector2d a, Vector2d b)>();
                foreach (int si in src) {
                    var ss = b.sources[si];
                    var all = snap.Origins(new Vector2d(ss.AX, ss.AY), new Vector2d(ss.BX, ss.BY));
                    if (all.Count == 0) all = new List<(Vector2d a, Vector2d b)> { (new Vector2d(ss.AX, ss.AY), new Vector2d(ss.BX, ss.BY)) };
                    foreach (var o in all) if (!origins.Contains(o)) origins.Add(o);
                }
                Vector2d A = origins[0].a, B = origins[0].b;
                bool InAllEnvelopes(long px, long py, bool own)
                {
                    for (int i = own ? 0 : 1; i < origins.Count; ++i) if (!grid.CrossesInterior(origins[i].a, origins[i].b, px, py) && !OnPixelCentre(origins[i], px, py)) return false;
                    return true;
                }
                var dir = B - A;
                int es = ScaleOf(dir.x, dir.y, 0, 0);
                var dirs = ScaleB(dir, -es); double l2 = dirs.LengthSquared;
                if (!(l2 > 0)) return false;
                double Along(Vector2d p) => ScaleB(p - A, -es).Dot(dirs) / l2;
                double tu = Along(pu), tw = Along(pw), lo = Math.Min(tu, tw), hi = Math.Max(tu, tw);
                var target = A + dir * (tu + (tw - tu) * fraction);
                long kx = grid.Pixel(target.x), ky = grid.Pixel(target.y);
                var candidates = new List<(double d, Vector2d c)>();
                for (long dx = -1; dx <= 1; ++dx)
                    for (long dy = -1; dy <= 1; ++dy) {
                        var c = new Vector2d(grid.Centre(kx + dx), grid.Centre(ky + dy));
                        double tc = Along(c);
                        if (!(tc > lo && tc < hi)) continue;
                        if ((dx != 0 || dy != 0) && !grid.CrossesInterior(A, B, kx + dx, ky + dy)) continue;
                        if (!InAllEnvelopes(kx + dx, ky + dy, false)) continue;
                        candidates.Add((ScaleB(c - target, -es).LengthSquared, c));
                    }
                candidates.Sort((a, c) => a.d != c.d ? a.d.CompareTo(c.d) : a.c.x != c.c.x ? a.c.x.CompareTo(c.c.x) : a.c.y.CompareTo(c.c.y));
                foreach (var (_, c) in candidates)
                    if (c != pu && c != pw && ValidGridSplit(e, pu, pw, c)) { q = c; return true; }
                // none next to the target (vertices crowd it): the other pixels of the envelope between u and w, nearest to the target
                // first, sampled every half pixel along the input segment (at most 256 samples; found by the fuzz test on a long subsegment)
                var seenPixels = new HashSet<(long, long)>();
                for (long dx = -1; dx <= 1; ++dx) for (long dy = -1; dy <= 1; ++dy) seenPixels.Add((kx + dx, ky + dy));
                var wider = new List<(double d, Vector2d c)>();
                double span = Math.Abs(tw - tu) * Math.Sqrt(dir.LengthSquared);
                int samples = (int)Math.Min(256, Math.Max(2, Math.Ceiling(2 * span / grid.W)));
                for (int i = 1; i < samples; ++i) {
                    var sp = A + dir * (tu + (tw - tu) * i / samples);
                    long sx = grid.Pixel(sp.x), sy = grid.Pixel(sp.y);
                    for (long dx = -1; dx <= 1; ++dx)
                        for (long dy = -1; dy <= 1; ++dy) {
                            if (!seenPixels.Add((sx + dx, sy + dy))) continue;
                            var c = new Vector2d(grid.Centre(sx + dx), grid.Centre(sy + dy));
                            double tc = Along(c);
                            if (!(tc > lo && tc < hi) || !grid.CrossesInterior(A, B, sx + dx, sy + dy) || !InAllEnvelopes(sx + dx, sy + dy, false)) continue;
                            wider.Add((ScaleB(c - target, -es).LengthSquared, c));
                        }
                }
                wider.Sort((a, c) => a.d != c.d ? a.d.CompareTo(c.d) : a.c.x != c.c.x ? a.c.x.CompareTo(c.c.x) : a.c.y.CompareTo(c.c.y));
                foreach (var (_, c) in wider)
                    if (c != pu && c != pw && ValidGridSplit(e, pu, pw, c)) { q = c; return true; }
                return false;
            }

            /// <summary> the input segment passes through the centre of pixel (px, py) (a degenerate envelope contact, eg along a grid line) </summary>
            bool OnPixelCentre((Vector2d a, Vector2d b) o, long px, long py)
            {
                var c = new Vector2d(grid.Centre(px), grid.Centre(py));
                return ExactPredicates.Orient2d(o.a, o.b, c) == 0 && c.x >= Math.Min(o.a.x, o.b.x) && c.x <= Math.Max(o.a.x, o.b.x) && c.y >= Math.Min(o.a.y, o.b.y) && c.y <= Math.Max(o.a.y, o.b.y);
            }

            bool ValidGridSplit(int e, Vector2d pu, Vector2d pw, Vector2d q)
            {
                double h = grid.W;
                double x0 = Math.Min(q.x, Math.Min(pu.x, pw.x)) - h, y0 = Math.Min(q.y, Math.Min(pu.y, pw.y)) - h;
                double x1 = Math.Max(q.x, Math.Max(pu.x, pw.x)) + h, y1 = Math.Max(q.y, Math.Max(pu.y, pw.y)) + h;
                long qx = grid.Pixel(q.x), qy = grid.Pixel(q.y);
                var near = TrianglesNear(e / 3, x0, y0, x1, y1);
                var seen = new HashSet<int>();
                foreach (int t in near)
                    for (int j = 0; j < 3; ++j) {
                        int f = 3 * t + j, v = k.V[f];
                        var pv = k.Point(v);
                        if (seen.Add(v) && pv != pu && pv != pw && pv != q) {
                            var (vx, vy) = PixelOf(v);
                            if (grid.CrossesInterior(pu, q, vx, vy) || grid.CrossesInterior(q, pw, vx, vy)) return false;
                        }
                        if (k.Seg[f] < 0 || f == e || f == k.H[e]) continue;
                        var a = pv; var c = k.Point(k.V[Next(f)]);
                        // (a constraint ending at a vertex already at q leaves its pixel there: not a crossing)
                        if (a != q && c != q && grid.CrossesInterior(a, c, qx, qy)) return false;
                        if (ProperCross(pu, q, a, c) || ProperCross(q, pw, a, c)) return false;
                        // a vertex of refinement already at q reroutes the piece through it: neither new piece may run along a
                        // constraint of another chain, or the two chains coincide there and the part between them collapses, which the
                        // rounding of the input keeps (owner decision 2026-11-03 on Q17; found with the zigzag of Q17 and a fuzz of star
                        // polygons: a spike's tip). Through a vertex of the snapped input (a hot pixel of an input point, a crossing or
                        // the restore step) the chains coincide in the rounding of the input itself: snap rounding routes both through it
                        if (keepChainsApart && ((a == q && k.V[f] >= firstSteiner && (c == pu || c == pw)) || (c == q && k.V[Next(f)] >= firstSteiner && (a == pu || a == pw)))) return false;
                        // nor may the piece pass through a vertex of refinement that another chain already runs through: the two chains
                        // would meet in that vertex (a pinch), where the rounding of the input keeps them apart (owner decision
                        // 2026-11-03 on Q17, strict). Every pinch measured came this way: one chain's split made the vertex, another
                        // chain's split later chose it (star polygon fuzz, Herne)
                        if (keepPinchFree && ((a == q && k.V[f] >= firstSteiner) || (c == q && k.V[Next(f)] >= firstSteiner)) && !SharesSource(k.Seg[f], k.Seg[e])) return false;
                    }
                return true;
            }

            /// <summary> the constraints c and d share an input source (one chain, or chains that share the edge) </summary>
            bool SharesSource(int c, int d)
            {
                foreach (int s in k.Sources[c]) if (k.Sources[d].Contains(s)) return true;
                return false;
            }

            static bool ProperCross(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
            {
                if (a == c || a == d || b == c || b == d) return false;
                int o1 = ExactPredicates.Orient2d(a, b, c), o2 = ExactPredicates.Orient2d(a, b, d);
                int o3 = ExactPredicates.Orient2d(c, d, a), o4 = ExactPredicates.Orient2d(c, d, b);
                return o1 * o2 < 0 && o3 * o4 < 0;
            }

            /// <summary> the sine squared of 60 degrees: two input segments at a smaller angle make a small pair, whatever the angle bound
            /// (owner decision 2026-11-09; refinement towards an apex at less than 60 degrees may not terminate, Shewchuk 2002, 6.2) </summary>
            internal const double SmallPairSin2 = 0.75;

            /// <summary> the slack below SmallPairSin2 under which an input angle counts as small: the oracle's 2^-30 plus 2^-40 for
            /// rounding </summary>
            internal const double SmallAngleSlack = 1.0 / (1L << 30) + 1.0 / (1L << 40);

            bool SpansSmallAngle(int p, int q)
            {
                var sp = SourcesAt(p); var sq = SourcesAt(q);
                if (sp.Count == 0 || sq.Count == 0) return false;
                var P = k.Point(p); var Qv = k.Point(q);
                foreach (int a in sp)
                    foreach (int c in sq)
                        if (a != c && NearSmallAngle(a, c, P, Qv)) return true;
                return false;
            }

            /// <summary> two sources that make an input angle below the bound: whether and where their input segments meet, in a common
            /// scale (Small: the strict test in doubles SpansSmallAngle uses; Meets: the exact test ApexExempt uses) </summary>
            struct SmallPair
            {
                internal bool Small;
                internal bool Meets;   // angle small, and the closed segments meet (exact)
                internal int Kind;     // 1 a shared endpoint, 2 a T-junction or a crossing
                internal int Scale;
                internal Vector2d From, Apex;   // s1's start (unscaled), the apex relative to it (scaled)
            }

            // per ordered pair of sources (they do not change during refinement): the angle test once, not on every query
            readonly Dictionary<long, SmallPair> smallPairs = new Dictionary<long, SmallPair>();

            /// <summary> how the closed input segments s1 and s2 meet, exactly: 0 not at all (or collinear), 1 at a shared endpoint, 2 elsewhere
            /// (an end on the other's interior, or a proper crossing) </summary>
            static int MeetKind(ConstrainedDelaunayMesh2.Source s1, ConstrainedDelaunayMesh2.Source s2)
                => MeetKind(new Vector2d(s1.AX, s1.AY), new Vector2d(s1.BX, s1.BY), new Vector2d(s2.AX, s2.AY), new Vector2d(s2.BX, s2.BY));

            internal static int MeetKind(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
            {
                int o1 = ExactPredicates.Orient2d(a, b, c), o2 = ExactPredicates.Orient2d(a, b, d);
                int o3 = ExactPredicates.Orient2d(c, d, a), o4 = ExactPredicates.Orient2d(c, d, b);
                if (o1 == 0 && o2 == 0) return 0;   // collinear: no apex
                if (o1 * o2 > 0 || o3 * o4 > 0) return 0;
                return a == c || a == d || b == c || b == d ? 1 : 2;
            }

            SmallPair PairOf(int a, int c)
            {
                long key = ((long)a << 32) | (uint)c;
                if (smallPairs.TryGetValue(key, out var pair)) return pair;
                pair = new SmallPair();
                var s1 = b.sources[a]; var s2 = b.sources[c];
                // on the grid the input angles are those of the input segments, not of their snapped chains (the oracle judges the
                // input; a chain bends by up to half a pixel)
                bool same = false;
                if (grid != null) {
                    s1 = OriginOf(s1); s2 = OriginOf(s2);
                    same = s1.AX == s2.AX && s1.AY == s2.AY && s1.BX == s2.BX && s1.BY == s2.BY;
                }
                if (!same) {
                    // all in a common scale by a power of two (no underflow at small magnitudes, the same bits at any 2^k)
                    double d1x = s1.BX - s1.AX, d1y = s1.BY - s1.AY, d2x = s2.BX - s2.AX, d2y = s2.BY - s2.AY, ox = s2.AX - s1.AX, oy = s2.AY - s1.AY;
                    int ex = Math.Max(ScaleOf(d1x, d1y, d2x, d2y), ScaleOf(ox, oy, 0, 0));
                    d1x = ExactPoint3.ScaleB(d1x, -ex); d1y = ExactPoint3.ScaleB(d1y, -ex); d2x = ExactPoint3.ScaleB(d2x, -ex); d2y = ExactPoint3.ScaleB(d2y, -ex);
                    ox = ExactPoint3.ScaleB(ox, -ex); oy = ExactPoint3.ScaleB(oy, -ex);
                    double cross = d1x * d2y - d1y * d2x, l1 = Math.Sqrt(d1x * d1x + d1y * d1y), l2 = Math.Sqrt(d2x * d2x + d2y * d2y);
                    if (Math.Abs(cross) > 0 && l1 > 0 && l2 > 0) {
                        double sin = Math.Abs(cross) / (l1 * l2);
                        // small means strictly below 60 degrees (SmallPairSin2) by the oracle's slack 2^-30 on sin^2 (its AngleBound), and by 2^-40 more for
                        // the rounding of sin^2 here (about 20 units of 2^-53 by the error analysis; 200k random pairs: at most 2.75): never
                        // small where the oracle would not excuse the triangle. 2^-40 = 8192 units is a proven margin, which no mutation test
                        // can pin. Known limit: a segment about 2^1000 times shorter than the other has a subnormal scaled direction, and the
                        // bound no longer holds. A relative 1e-9 (2.5e-10 at 30 degrees) was more lenient than the oracle's 9.3e-10, and
                        // triangles at an input angle just below the bound were kept without the oracle's excuse (found by review of the
                        // small-angle design)
                        if (sin * sin < SmallPairSin2 - SmallAngleSlack) {
                            // where the lines meet
                            double t1 = (ox * d2y - oy * d2x) / cross;
                            double t2 = (ox * d1y - oy * d1x) / cross;
                            // for ApexExempt (which excuses no triangle): the closed segments meet, decided exactly (the oracle's t in
                            // [0, 1]); the test in doubles below misses a shared endpoint when t rounds to 1 + eps
                            int kind = MeetKind(s1, s2);
                            if (kind > 0) {
                                pair.Meets = true; pair.Kind = kind; pair.Scale = ex;
                                pair.From = new Vector2d(s1.AX, s1.AY); pair.Apex = new Vector2d(t1 * d1x, t1 * d1y);
                            }
                            // for SpansSmallAngle: the apex is on both segments, an input angle, not two lines that would meet far away
                            // (slightly stricter than the oracle's exact test, so that rounding never exempts what it would not)
                            if (!(t1 < 1e-12 && t1 != 0 || t1 > 1 || t2 < 1e-12 && t2 != 0 || t2 > 1 || t1 < 0 || t2 < 0)) {
                                pair.Small = true; pair.Scale = ex;
                                pair.From = new Vector2d(s1.AX, s1.AY); pair.Apex = new Vector2d(t1 * d1x, t1 * d1y);
                            }
                        }
                    }
                }
                smallPairs.Add(key, pair);
                return pair;
            }

            /// <summary>
            /// sources a and c make a small input angle (strict: Small, otherwise Meets), and p and q lie at distances from its apex within a
            /// factor of 1.5 of each other (2.24 on the squares; the oracle's SpansSmallInputAngle allows 1.5), on the same side of it
            /// </summary>
            bool NearSmallAngle(int a, int c, Vector2d P, Vector2d Qv, bool strict = true)
            {
                var pair = PairOf(a, c);
                if (!(strict ? pair.Small : pair.Meets)) return false;
                // p, q and the apex relative to s1's start, in the common scale
                var Pl = ScaleB(P - pair.From, -pair.Scale) - pair.Apex; var Ql = ScaleB(Qv - pair.From, -pair.Scale) - pair.Apex;
                double rp = Pl.LengthSquared, rq = Ql.LengthSquared;
                if (!(rp > 0) || !(rq > 0) || rp > 2.24 * rq || rq > 2.24 * rp) return false;
                // and p, q lie on the same side of the apex along their lines
                return Pl.Dot(Ql) > 0;
            }

            /// <summary> the input segment a snapped source comes from, as a source with its coordinates (itself if unknown) </summary>
            ConstrainedDelaunayMesh2.Source OriginOf(ConstrainedDelaunayMesh2.Source s)
            {
                if (!snap.TryOrigin(new Vector2d(s.AX, s.AY), new Vector2d(s.BX, s.BY), out var o)) return s;
                s.AX = o.a.x; s.AY = o.a.y; s.BX = o.b.x; s.BY = o.b.y;
                return s;
            }

            List<int> SourcesAt(int v)
            {
                var result = new List<int>();
                foreach (int e in k.Star(v)) {
                    int c = k.Seg[e];
                    if (c < 0) continue;
                    // the hull edges of a hull domain count (D13: the hull is its boundary, so an input segment at a small angle to it makes a
                    // small input angle as two segments would; found with CDT's issue-211, a segment at 0.096 degrees to a hull edge)
                    foreach (int src in k.Sources[c]) if (!result.Contains(src)) result.Add(src);
                }
                return result;
            }

            long TriangleKey(int t)
            {
                int a = k.V[3 * t], c = k.V[3 * t + 1], d = k.V[3 * t + 2];
                // the triangle's vertex set, order-free, in 64 bits (collisions only make a triangle exempt that is not)
                ulong h = (ulong)Math.Min(a, Math.Min(c, d)) * 0x9E3779B97F4A7C15UL ^ (ulong)Math.Max(a, Math.Max(c, d)) * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(a + c + d);
                return (long)h;
            }

            // ------------------------------------------------------------ splitting

            /// <summary> the circumcentre of t, or false if it cannot be computed in doubles </summary>
            bool Circumcentre(int t, out Vector2d c)
            {
                var a = Local(t, out var p, out var q, out int ex);
                double bx = p.x, by = p.y, cx = q.x, cy = q.y;
                double d = 2 * (bx * cy - by * cx);
                double b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
                double ux = (cy * b2 - by * c2) / d, uy = (bx * c2 - cx * b2) / d;
                c = new Vector2d(a.x + ExactPoint3.ScaleB(ux, ex), a.y + ExactPoint3.ScaleB(uy, ex));
                return d != 0 && !double.IsNaN(c.x) && !double.IsNaN(c.y) && !double.IsInfinity(c.x) && !double.IsInfinity(c.y)
                    && Math.Abs(c.x) <= DelaunayMesh2.CoordinateLimit && Math.Abs(c.y) <= DelaunayMesh2.CoordinateLimit;
            }

            /// <summary> the off-centre of t (<see cref="OffCentre(Vector2d, Vector2d, Vector2d, double, out Vector2d)"/>), for a triangle
            /// bad by its angle only: one bad by size alone, or whose small angle is forced by the input (O3, a small input angle), takes
            /// its circumcentre, which halves it (review: such a needle under an area bound got off-centres, which only shave it) </summary>
            bool OffCentre(int t, out Vector2d c)
            {
                c = default(Vector2d);
                if (!offCentres || double.IsInfinity(halfCot) || !BadByAngle(t)) return false;
                return OffCentre(k.Point(k.V[3 * t]), k.Point(k.V[3 * t + 1]), k.Point(k.V[3 * t + 2]), halfCot, out c);
            }

            /// <summary> cot(theta / 2) of an angle theta given by sin^2 theta, from square roots only (no Tan: its last bit may differ by
            /// runtime): (1 + cos theta) / sin theta; infinite for 0 </summary>
            internal static double HalfCot(double sin2) => sin2 > 0 ? (1 + Math.Sqrt(1 - sin2)) / Math.Sqrt(sin2) : double.PositiveInfinity;

            /// <summary>
            /// The off-centre of triangle abc (Ungor 2004): on the perpendicular bisector of the shortest edge pq, on the side of the
            /// circumcentre, where the triangle of pq and the point has the angle bound theta at the point: |pq| cot(theta / 2) / 2 from the
            /// midpoint of pq. False if the circumcentre is not farther than that (it is then the point to insert) or nothing can be
            /// computed in doubles. Computed in the triangle's frame scaled by a power of two (<see cref="Local"/>): an input scaled by
            /// 2^k gives the point scaled by 2^k, bit for bit
            /// </summary>
            internal static bool OffCentre(Vector2d a, Vector2d b, Vector2d c, double halfCot, out Vector2d point)
            {
                point = default(Vector2d);
                var d1 = b - a; var d2 = c - a;
                int ex = ScaleOf(d1.x, d1.y, d2.x, d2.y);
                var p1 = ScaleB(d1, -ex); var p2 = ScaleB(d2, -ex);
                double d = 2 * (p1.x * p2.y - p1.y * p2.x);
                if (d == 0) return false;
                double b2 = p1.LengthSquared, c2 = p2.LengthSquared;
                var u = new Vector2d((p2.y * b2 - p1.y * c2) / d, (p1.x * c2 - p2.x * b2) / d);
                // the shortest edge, opposite corner j (ties go to the first)
                var corners = new[] { Vector2d.Zero, p1, p2 };
                int j = 0; double best = double.MaxValue;
                for (int i = 0; i < 3; ++i) {
                    double l2 = (corners[(i + 2) % 3] - corners[(i + 1) % 3]).LengthSquared;
                    if (l2 < best) { best = l2; j = i; }
                }
                var m = (corners[(j + 1) % 3] + corners[(j + 2) % 3]) * 0.5;
                double off = Math.Sqrt(best) * 0.5 * halfCot, dist = (u - m).Length;
                if (!(dist > off) || double.IsInfinity(dist)) return false;
                var o = m + (u - m) * (off / dist);
                point = new Vector2d(a.x + ExactPoint3.ScaleB(o.x, ex), a.y + ExactPoint3.ScaleB(o.y, ex));
                return !double.IsNaN(point.x) && !double.IsNaN(point.y) && !double.IsInfinity(point.x) && !double.IsInfinity(point.y);
            }

            readonly Stack<ConstrainedDelaunayMesh2.Builder.Piece> pendingPieces = new Stack<ConstrainedDelaunayMesh2.Builder.Piece>();
            // (buffers of BestCandidate and Score, reused: one refiner runs on one thread)
            readonly List<Vector2d> candidateBuffer = new List<Vector2d>();
            readonly List<Vector2d> seenBuffer = new List<Vector2d>();
            readonly List<int> cavityBuffer = new List<int>();
            readonly Stack<int> stackBuffer = new Stack<int>();
            readonly List<int> boundaryBuffer = new List<int>();
            // marks by triangle slot with a generation instead of sets: a set grown by one large cavity zeroes all its buckets on every
            // clear (profiled: most of the choice's time). cavityMark[t] == cavityGen: t is in the cavity being built; badMark[t] ==
            // badGen: badValue[t] is t's BadByAngle in this choice (the candidates' cavities overlap, the mesh does not change meanwhile)
            int[] cavityMark = new int[0], badMark = new int[0];
            bool[] badValue = new bool[0];
            int cavityGen, badGen;

            static int NextGeneration(ref int gen, int[] marks)
            {
                if (gen == int.MaxValue) { Array.Clear(marks, 0, marks.Length); gen = 0; }
                return ++gen;
            }

            void EnsureMarks()
            {
                int n = k.TriangleSlots;
                if (cavityMark.Length >= n) return;
                int m = Math.Max(n, 2 * cavityMark.Length);
                Array.Resize(ref cavityMark, m); Array.Resize(ref badMark, m); Array.Resize(ref badValue, m);
            }

            // the circumcentre's pixel centre of the triangle being split, once per split (the choice needs it as its default point)
            bool ccPixelKnown, ccPixelOk;
            Vector2d ccPixel;

            bool CircumcentrePixel(int t, out Vector2d c)
            {
                if (!ccPixelKnown) {
                    ccPixelOk = grid.CircumcentrePixelCentre(k.Point(k.V[3 * t]), k.Point(k.V[3 * t + 1]), k.Point(k.V[3 * t + 2]), out ccPixel);
                    ccPixelKnown = true;
                }
                c = ccPixel;
                return ccPixelOk;
            }

            /// <summary> the number of candidates on the bisector besides the default point and the circumcentre </summary>
            const int BisectorCandidates = 6;

            /// <summary>
            /// The split point of a triangle t bad by its angle, chosen among candidates: the default point (the off-centre if there is
            /// one, else the circumcentre) and the candidates in the petal of t's shortest edge pq (<see cref="PetalCandidates"/>; grading
            /// step 2). With <see cref="PetalForBenchmarks"/> off, step 1's candidates on the perpendicular bisector of pq instead: the
            /// circumcentre and six points evenly from the nearer of the off-centre and the circumcentre to 1.3 times the circumcentre's
            /// distance from the midpoint of pq (from the circumcentre with off-centres switched off). On the grid the
            /// default point is the pixel centre refinement inserts without the choice, the others are rounded to their pixel centres. A candidate
            /// counts only where it would be inserted as it is: reached from t without crossing a constraint, encroaching on no
            /// subsegment, with t in its cavity, and on the grid with no constraint across its pixel. Of those the one wins that removes
            /// the most triangles bad by their angle (in its cavity) less those it makes (in the fan of new triangles), ties to the larger
            /// insertion radius (the distance to the nearest vertex of the fan), then to the earlier candidate. The fan's count takes the
            /// angle bound only (a new triangle at a small input angle counts as bad although refinement would leave it): it skews the
            /// score, not the mesh. When the default point wins, nothing is chosen.
            ///
            /// The floor: a candidate's insertion radius must be at least the default point's, so every point inserted is at least as far
            /// from the mesh as the point refinement would insert without the choice, and the insertion radius argument of termination
            /// (Ruppert 1995; Shewchuk 2002; Ungor 2004 for off-centres) holds as for the default point. When the default point itself
            /// does not count (it is blocked, encroaches or lies in a constraint's pixel), nothing is chosen: refinement goes on as without
            /// the choice (it splits the subsegment, or falls back to the circumcentre). Choosing among several points is the idea of
            /// Erten and Ungor 2007 (locally optimal Steiner points, SGP 2007), with step 1's score instead of their farthest point
            /// (MESHING2D_GRADING.md 4.3.1).
            /// </summary>
            bool BestCandidate(int t, Vector2d cc, bool off, Vector2d oc, out Vector2d picked)
            {
                picked = default(Vector2d);
                if (!BadByAngle(t)) return false;
                // the frame of Local: t's first vertex at the origin, scaled by a power of two to unit size, so the candidates and the scores
                // of an input scaled by 2^k are the same bit for bit
                var a0 = Local(t, out var q0, out var r0, out int ex);
                var corners = new[] { Vector2d.Zero, q0, r0 };
                int j = 0; double shortest = double.MaxValue;
                for (int i = 0; i < 3; ++i) { double l = (corners[(i + 2) % 3] - corners[(i + 1) % 3]).LengthSquared; if (l < shortest) { shortest = l; j = i; } }
                Vector2d m = (corners[(j + 1) % 3] + corners[(j + 2) % 3]) * 0.5;
                Vector2d ccLocal = ScaleB(cc - a0, -ex);
                double dcc = (ccLocal - m).Length;
                if (!(dcc > 0) || double.IsInfinity(dcc)) return false;
                Vector2d dir = (ccLocal - m) / dcc;
                // (with off-centres switched off the candidates start at the circumcentre)
                double dOff = !offCentres || double.IsInfinity(halfCot) ? dcc : Math.Sqrt(shortest) * 0.5 * halfCot;
                double lo = Math.Min(dOff, dcc), hi = dcc * 1.3;
                // the default point exactly as refinement inserts it without the choice: on the grid the off-centre's pixel centre, or the
                // circumcentre's decided exactly (not the rounded double circumcentre: review)
                Vector2d dflt = off ? oc : cc;
                if (grid != null) {
                    if (off) dflt = grid.Round(oc);
                    else if (!CircumcentrePixel(t, out dflt)) return false;
                }
                var candidates = candidateBuffer; candidates.Clear();
                candidates.Add(dflt);
                if (petal) PetalCandidates(t, a0, ex, m, dir, Math.Sqrt(shortest), candidates);
                else {
                    candidates.Add(cc);
                    for (int i = 0; i < BisectorCandidates; ++i) {
                        var l = m + dir * (lo + (hi - lo) * i / (BisectorCandidates - 1));
                        candidates.Add(new Vector2d(a0.x + ExactPoint3.ScaleB(l.x, ex), a0.y + ExactPoint3.ScaleB(l.y, ex)));
                    }
                }
                double floor = -1, bestRadius = -1; int bestScore = int.MinValue, best = -1;
                var seen = seenBuffer; seen.Clear();
                EnsureMarks();
                NextGeneration(ref badGen, badMark);
                for (int i = 0; i < candidates.Count; ++i) {
                    var p = candidates[i];
                    // (finite and in range before rounding: a far candidate's pixel index does not fit the grid's integers; review)
                    if (!(i == 0 ? Usable(p) : RoundableOnGrid(grid, p))) {
                        if (i == 0) return false;
                        continue;
                    }
                    if (grid != null && i > 0) p = grid.Round(p);
                    if (seen.Contains(p)) continue;
                    seen.Add(p);
                    // (the checks that only refuse, the encroachment and the pixel, run only for a candidate that would win: the same
                    // winner, without their cost for the others)
                    if (!Score(t, p, ex, bestScore, out int at, out int score, out double radius)) {
                        if (i == 0) return false;
                        continue;
                    }
                    if (i == 0) floor = radius;
                    if (radius < floor) continue;
                    if (!(score > bestScore || (score == bestScore && radius > bestRadius))) continue;
                    if (!Insertable(at, p)) {
                        if (i == 0) return false;
                        continue;
                    }
                    // (a point other than the default exactly on a constrained edge is not chosen: EncroachedByPoint skips locked and
                    // unsplittable edges, so nothing else would refuse it there; review)
                    if (i > 0 && OnConstrainedEdge(at, p)) continue;
                    bestScore = score; bestRadius = radius; picked = p; best = i;
                }
                if (PickedForTests != null) PickedForTests(floor, bestRadius, best > 0, picked);
                // the default point winning is no choice: refinement goes on as without it (the same point, counted as before)
                return best > 0;
            }

            /// <summary> the arc points of the petal on each side of its far end (<see cref="PetalCandidates"/>) </summary>
            const int PetalArcPoints = 3;
            /// <summary> at most this many triangles near t are searched for circumcentres in the petal </summary>
            const int PetalSearch = 64;
            readonly Queue<int> petalQueue = new Queue<int>();
            int[] petalMark = new int[0];
            int petalGen;

            /// <summary>
            /// The candidates of the choice besides the default point (<see cref="BestCandidate"/>), in the petal of t's shortest edge pq
            /// (Erten and Ungor 2007): the disk through p and q from which pq is seen under the angle bound theta or more, on t's side of
            /// pq. It lies in t's circumdisk, and its far end on the bisector is the off-centre. Their point is the petal's point farthest
            /// from every vertex, at a Voronoi vertex inside the petal or on its boundary; here the candidates are where it can lie, and
            /// step 1's score decides among them (measured better than the farthest point: MESHING2D_GRADING.md 4.3.1):
            /// - the circumcentres (Voronoi vertices) of the triangles reached from t across unconstrained edges whose circumdisks meet the
            ///   petal, at most <see cref="PetalSearch"/> of them, that lie inside it;
            /// - seven points of its boundary arc: the far end and three on each side, by the half-angle parameter s = tan(psi / 2) of
            ///   the angle psi from the bisector at the petal's centre, s = cot(theta / 2) (i / 4) |i / 4| for i = -3 ... 3, spaced
            ///   quadratically: 0 at the far end, and cot(theta / 2) = <see cref="HalfCot"/> would be p and q. Rational in s: no trigonometric function (whose last bit
            ///   may differ by runtime); the quadratic spacing puts more points near the far end (measured better than linear).
            /// In t's frame of <see cref="Local"/> (an input scaled by 2^k gives the candidates scaled by 2^k, bit for bit), returned in
            /// the mesh's coordinates
            /// </summary>
            void PetalCandidates(int t, Vector2d a0, int ex, Vector2d m, Vector2d dir, double len, List<Vector2d> candidates)
            {
                double sinT = Math.Sqrt(sin2Min), cosT = Math.Sqrt(1 - sin2Min);
                if (!(sinT > 0) || double.IsInfinity(halfCot)) return;
                double radius = 0.5 * len / sinT;
                var centre = m + dir * (0.5 * len * cosT / sinT);
                var normal = new Vector2d(-dir.y, dir.x);
                Vector2d Out(Vector2d l) => new Vector2d(a0.x + ExactPoint3.ScaleB(l.x, ex), a0.y + ExactPoint3.ScaleB(l.y, ex));
                const int arc = PetalArcPoints + 1;
                for (int i = -arc + 1; i < arc; ++i) {
                    double f = (double)i / arc, s = halfCot * f * Math.Abs(f), d = 1 + s * s;
                    candidates.Add(Out(centre + (dir * ((1 - s * s) / d) + normal * (2 * s / d)) * radius));
                }
                // the Voronoi vertices near t: breadth first across unconstrained edges, through triangles whose circumdisks meet the petal
                if (petalMark.Length < k.TriangleSlots) Array.Resize(ref petalMark, Math.Max(k.TriangleSlots, 2 * petalMark.Length));
                int gen = NextGeneration(ref petalGen, petalMark);
                var queue = petalQueue; queue.Clear();
                queue.Enqueue(t); petalMark[t] = gen;
                int budget = PetalSearch;
                while (queue.Count > 0 && budget-- > 0) {
                    int u = queue.Dequeue();
                    if (!Circumcentre(u, out var cu)) continue;
                    var lu = ScaleB(cu - a0, -ex);
                    var corner = ScaleB(k.Point(k.V[3 * u]) - a0, -ex);
                    double ru = (corner - lu).Length, dc = (lu - centre).Length;
                    if (u != t && !(dc < radius + ru)) continue;
                    if (dc <= radius && (lu - m).Dot(dir) > 0) candidates.Add(Out(lu));
                    for (int e = 3 * u; e < 3 * u + 3; ++e) {
                        int w = k.H[e] / 3;
                        if (k.Seg[e] >= 0 || petalMark[w] == gen || k.IsGhost(w) || !InDomain(w)) continue;
                        petalMark[w] = gen;
                        queue.Enqueue(w);
                    }
                }
            }

            /// <summary>
            /// The score of inserting p for triangle t (<see cref="BestCandidate"/>): the triangles of the domain bad by their angle in p's
            /// cavity less the triangles of its fan below the angle bound, and p's insertion radius squared (in the frame scaled by 2^-ex).
            /// False if p cannot be reached from t without crossing a constraint, or t is not in its cavity, or it removes fewer bad
            /// triangles than toBeat (it cannot win: its score is at most that count); the other refusals are
            /// <see cref="Insertable"/>. at: the triangle that holds p
            /// </summary>
            bool Score(int t, Vector2d p, int ex, int toBeat, out int at, out int score, out double radius)
            {
                score = 0; radius = 0;
                at = Walk(t, p, out int blocked);
                if (blocked >= 0) return false;
                if (!InCircle(at, p)) return false;
                // the cavity: the triangles whose circumcircle holds p, connected across unconstrained edges (as the insertion finds it)
                int gen = NextGeneration(ref cavityGen, cavityMark);
                var cavity = cavityBuffer; cavity.Clear(); cavity.Add(at); cavityMark[at] = gen;
                var stack = stackBuffer; stack.Clear();
                stack.Push(at);
                var boundary = boundaryBuffer; boundary.Clear();
                while (stack.Count > 0) {
                    int u = stack.Pop();
                    for (int e = 3 * u; e < 3 * u + 3; ++e) {
                        int w = k.H[e] / 3;
                        if (cavityMark[w] == gen) continue;
                        if (k.Seg[e] < 0 && !k.IsGhost(w) && InCircle(w, p)) { cavity.Add(w); cavityMark[w] = gen; stack.Push(w); }
                        else boundary.Add(e);
                    }
                }
                if (cavityMark[t] != gen) return false;
                int destroyed = 0, created = 0;
                foreach (int u in cavity) {
                    if (!InDomain(u)) continue;
                    // (the candidates' cavities overlap: each triangle judged once per choice; the mesh does not change during it)
                    if (badMark[u] != badGen) { badValue[u] = BadByAngle(u); badMark[u] = badGen; }
                    else if (CheckScoresForTests && badValue[u] != BadByAngle(u)) throw new InvalidOperationException("stale angle test of triangle " + u + " in a choice");
                    if (badValue[u]) ++destroyed;
                }
                // (a bound: the score is at most the triangles removed, so a candidate that removes fewer than the best score so far
                // cannot win, whatever its fan; refused here without the fan's angles. The same winner: BestCandidate only takes a
                // score above the best, or equal with a larger radius. The default point, scored first, meets int.MinValue)
                if (destroyed < toBeat && !ScoreBoundOffForTests) return false;
                double nearest = double.MaxValue;
                foreach (int e in boundary) {
                    // the new triangle of p and the boundary edge, in the frame scaled by 2^-ex around p
                    var u = ScaleB(k.Point(k.V[e]) - p, -ex); var w = ScaleB(k.Point(k.V[Next(e)]) - p, -ex);
                    double lu = u.LengthSquared, lw = w.LengthSquared, luw = (w - u).LengthSquared;
                    double area2 = u.x * w.y - u.y * w.x, a2 = area2 * area2;
                    double s2 = Math.Min(a2 / (lu * lw), Math.Min(a2 / (lu * luw), a2 / (lw * luw)));
                    if (s2 < sin2Min) ++created;
                    nearest = Math.Min(nearest, Math.Min(lu, lw));
                }
                score = destroyed - created;
                radius = nearest;
                return true;
            }

            /// <summary> p (in triangle at) encroaches on no subsegment, and on the grid no constraint crosses its pixel: refinement would
            /// insert it as it is (<see cref="SplitTriangle"/>) </summary>
            bool Insertable(int at, Vector2d p) => EncroachedByPoint(at, p).Count == 0 && !(grid != null && ConstraintInPixel(at, p));

            static bool Usable(Vector2d p) => !double.IsNaN(p.x) && !double.IsNaN(p.y)
                && Math.Abs(p.x) <= DelaunayMesh2.CoordinateLimit && Math.Abs(p.y) <= DelaunayMesh2.CoordinateLimit;

            /// <summary> p is finite, within the coordinate limit and, on a grid, at most 2^52 pixels from the origin in each coordinate (the
            /// bound <see cref="SnapGrid.CircumcentrePixelCentre"/> keeps: the pixel index then fits the grid's integers exactly) </summary>
            internal static bool RoundableOnGrid(SnapGrid grid, Vector2d p)
            {
                if (!Usable(p)) return false;
                if (grid == null) return true;
                const double limit = 4503599627370496.0;   // 2^52
                return Math.Abs(p.x) / grid.W < limit && Math.Abs(p.y) / grid.W < limit;
            }

            /// <summary> p lies on a constrained edge of triangle at (which holds it) </summary>
            bool OnConstrainedEdge(int at, Vector2d p)
            {
                for (int e = 3 * at; e < 3 * at + 3; ++e)
                    if (k.Seg[e] >= 0 && ExactPredicates.Orient2d(k.Point(k.V[e]), k.Point(k.V[Next(e)]), p) == 0) return true;
                return false;
            }

            bool InCircle(int t, Vector2d p) => ExactPredicates.InCircle(k.Point(k.V[3 * t]), k.Point(k.V[3 * t + 1]), k.Point(k.V[3 * t + 2]), p) > 0;

            ConstrainedTriangulation2Result SplitTriangle(int t, bool tryOffCentre = true)
            {
                if (inserted >= limit) { b.SteinerLimitReached = true; return null; }
                if (!Circumcentre(t, out var c)) { exempt.Add(TriangleKey(t)); Note(10); return null; }
                // on the grid the point is the centre of the circumcentre's pixel, decided exactly (rounded at insertion, never moved later)
                Vector2d oc = default(Vector2d);
                bool off = tryOffCentre && OffCentre(t, out oc);
                ccPixelKnown = false;
                // (a chosen point takes the path of an off-centre: rounded again, a no-op, and if the kernel refuses it after all, the
                // triangle is tried once more without the choice; counted apart)
                bool chosen = false;
                if (bestCandidate && tryOffCentre && BestCandidate(t, c, off, oc, out var picked)) { off = true; oc = picked; chosen = true; }
                if (off) c = grid != null ? grid.Round(oc) : oc;
                else if (grid != null && !CircumcentrePixel(t, out c)) { exempt.Add(TriangleKey(t)); Note(10); return null; }
                // walk from t towards c; a constrained edge on the way is split instead (it is encroached, or c is outside)
                int at = Walk(t, c, out int blocked);
                if (blocked >= 0) {
                    if (k.Seg[blocked] < 0) { exempt.Add(TriangleKey(t)); Note(11); return null; }   // (the domain is bounded by constraints: not expected)
                    if (Locked(blocked) || unsplittable.Contains(Key(k.V[blocked], k.V[Next(blocked)]))) { exempt.Add(TriangleKey(t)); Note(11); return null; }
                    splitCause = 1;
                    // t may survive the split (the new vertex can lie outside its circumcircle): look at it again, as after an
                    // encroachment split (found by lenses on the earcut water corpus input)
                    triangles.Enqueue(t);
                    return SplitSegment(blocked);
                }
                // c would encroach a subsegment of its cavity: split those instead (Ruppert; Shewchuk 2002)
                var encroached = EncroachedByPoint(at, c);
                if (encroached.Count > 0) {
                    // split one now (the others go to the queue, to be split if still encroached), then try t again
                    int first = encroached[0];
                    for (int i = 1; i < encroached.Count; ++i) segments.Enqueue(Key(k.V[encroached[i]], k.V[Next(encroached[i])]));
                    long key = Key(k.V[first], k.V[Next(first)]), tKey = TriangleKey(t);
                    triangles.Enqueue(t);
                    splitCause = 2;
                    var failure = SplitSegment(first);
                    if (failure != null) return failure;
                    // a piece too short to split stays: then t cannot be helped this way. On the grid t is tried again (it is queued): the
                    // encroachment of an unsplittable subsegment no longer stops its circumcentre, which may still fit on the grid
                    if (unsplittable.Contains(key) && grid == null) { exempt.Add(tKey); Note(11); }
                    return null;
                }
                // (a refused off-centre falls back to the circumcentre's pixel once before t counts as grid-blocked; review)
                if (grid != null && ConstraintInPixel(at, c)) { if (off) return SplitTriangle(t, false); exempt.Add(TriangleKey(t)); ++GridBlocked; return null; }
                int before = k.VertexCount;
                if (CheckTagsForTests) lastOperation = "circumcentre of triangle " + t + " at (" + c.x.ToString("R") + ", " + c.y.ToString("R") + ")";
                // (the walk above found the triangle that holds c: the kernel's point location starts there instead of at the last
                // insertion, which the worst-first order puts anywhere in the mesh; profiled: 85 steps per location on Herne)
                k.LocateHint(at);
                long walked = k.WalkSteps;
                k.InsertPoint(c.x, c.y);
                WalkStepsForTests += k.WalkSteps - walked; ++LocationsForTests;
                if (k.VertexCount == before) { k.Dirty.Clear(); if (off) return SplitTriangle(t, false); exempt.Add(TriangleKey(t)); if (grid != null) ++GridBlocked; else Note(12); return null; }
                ++inserted;
                if (CountsForBenchmarks != null) { int slot = chosen ? 7 : off ? 4 : 3; if (slot < CountsForBenchmarks.Length) ++CountsForBenchmarks[slot]; }
                if (b.heights != null) b.SetHeightOnReference(k.VertexCount - 1);
                Collect();
                return null;
            }

            /// <summary> visibility walk from t to the triangle containing c; blocked is the first constrained edge crossed, or -1 </summary>
            int Walk(int t, Vector2d c, out int blocked)
            {
                blocked = -1;
                int entry = -1;
                long guard = 4L * k.TriangleSlots + 16;
                for (long step = 0; step < guard; ++step) {
                    int moved = -1;
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + (int)((step + j) % 3);
                        if (e == entry) continue;
                        if (ExactPredicates.Orient2d(k.Point(k.V[e]), k.Point(k.V[Next(e)]), c) < 0) { moved = e; break; }
                    }
                    if (moved < 0) return t;
                    if (k.Seg[moved] >= 0) { blocked = moved; return t; }
                    entry = k.H[moved];
                    t = entry / 3;
                    if (k.IsGhost(t) || !InDomain(t)) { blocked = moved; return t; }   // (not reached: the domain is bounded by constraints)
                }
                blocked = -1;
                return t;
            }

            /// <summary> the unlocked constrained edges on the boundary of c's cavity whose diametral circle contains c </summary>
            List<int> EncroachedByPoint(int t0, Vector2d c)
            {
                var result = new List<int>();
                var seen = new HashSet<int> { t0 };
                var stack = new Stack<int>();
                stack.Push(t0);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    for (int j = 0; j < 3; ++j) {
                        int e = 3 * t + j;
                        if (k.Seg[e] >= 0) {
                            if (!Locked(e) && !unsplittable.Contains(Key(k.V[e], k.V[Next(e)])) && EncroachesOn(k.Point(k.V[e]), k.Point(k.V[Next(e)]), c)) result.Add(e);
                            continue;
                        }
                        int t2 = k.H[e] / 3;
                        if (k.IsGhost(t2) || seen.Contains(t2)) continue;
                        var p = k.Point(k.V[3 * t2]); var q = k.Point(k.V[3 * t2 + 1]); var r = k.Point(k.V[3 * t2 + 2]);
                        if (ExactPredicates.InCircle(p, q, r, c) > 0) { seen.Add(t2); stack.Push(t2); }
                    }
                }
                return result;
            }

            /// <summary>
            /// The exponent below which a piece's split point must lie exactly on its source: the piece's larger coordinate difference
            /// (<see cref="ScaleOf"/>) is below 2^-1026. The computed point A + dir * tt is off the line by its last two roundings, each at
            /// most half a unit in the last place of the result: relative to the coordinates in the normal range, but at least half the
            /// subnormal step 2^-1074 below it, whatever the coordinates. From 2^-1026 on that absolute part is at most 2^-49 of the piece,
            /// so the error is as small against the piece as a relative rounding; below, at 2^-1054 say, it is 2^-21 of it and the point
            /// lies visibly off the segment. (A piece below 2^-1026 between normal coordinates near 2^-1000, whose rounding is relative to
            /// the coordinates, is refused too: conservative, the mesh stays valid, only its refinement there is weaker.)
            /// </summary>
            internal const int MinRoundedSplitExponent = -1026;

            /// <summary> p lies exactly on segment ab (the piece's source), strictly between u and w (the piece's ends, on ab or rounded
            /// next to it at a crossing) along the coordinate in which they differ most; comparisons and exact predicates only, no products
            /// that could underflow </summary>
            internal static bool ExactlyWithin(Vector2d a, Vector2d b, Vector2d u, Vector2d w, Vector2d p)
            {
                if (ExactPredicates.Orient2d(a, b, p) != 0) return false;
                return Math.Abs(w.x - u.x) >= Math.Abs(w.y - u.y) ? p.x > Math.Min(u.x, w.x) && p.x < Math.Max(u.x, w.x)
                    : p.y > Math.Min(u.y, w.y) && p.y < Math.Max(u.y, w.y);
            }

            /// <summary>
            /// Splits the constrained edge e: at its midpoint, or, next to an input vertex, at the power of two from it closest to the
            /// middle (concentric shells). The point is computed on the first input segment of the piece, so repeated splits do not drift.
            /// </summary>
            ConstrainedTriangulation2Result SplitSegment(int e)
            {
                int u = k.V[e], w = k.V[Next(e)];
                var src = k.Sources[k.Seg[e]];
                var pu = k.Point(u); var pw = k.Point(w);
                // lengths and projections in a scale by a power of two (no underflow; the same bits at any 2^k)
                var duw = pw - pu;
                int ex = ScaleOf(duw.x, duw.y, 0, 0);
                double length = ScaleB(duw, -ex).Length;
                double fraction = 0.5;
                // concentric shells around the vertices that existed before refinement (input points and crossings): where segments
                // meet at a small angle, their splits then lie on common circles and do not encroach each other (Shewchuk 2002)
                bool uInput = u < firstSteiner, wInput = w < firstSteiner;
                if (uInput != wInput && length > 0) {
                    // the power of two nearest to half the length, from the bits (no Log or Pow: their last bit may differ by runtime)
                    ExactPoint3.Decompose(length / 2, out var mant, out int em);
                    double lower = ExactPoint3.ScaleB(1, (int)(em + ExactPoint3.BitLength(mant) - 1)), d = length / 2 - lower < 2 * lower - length / 2 ? lower : 2 * lower;
                    if (d > length / 3 && d < 2 * length / 3) fraction = uInput ? d / length : 1 - d / length;
                }
                Vector2d p;
                var s = b.sources[src[0]];
                if (grid != null) {
                    if (!GridSplitPoint(e, pu, pw, src, fraction, out p)) { unsplittable.Add(Key(u, w)); ++GridUnsplittable; return null; }
                }
                else {
                    var A = new Vector2d(s.AX, s.AY); var B = new Vector2d(s.BX, s.BY);
                    var dir = B - A;
                    int es = ScaleOf(dir.x, dir.y, 0, 0);
                    var dirs = ScaleB(dir, -es); double l2 = dirs.LengthSquared;
                    double tu = l2 > 0 ? ScaleB(pu - A, -es).Dot(dirs) / l2 : 0, tw = l2 > 0 ? ScaleB(pw - A, -es).Dot(dirs) / l2 : 0;
                    double tt = tu + (tw - tu) * fraction;
                    p = A + dir * tt;
                    // a piece whose larger coordinate difference is below 2^-1026 (MinRoundedSplitExponent): the rounding of its point
                    // includes up to half the subnormal step 2^-1074, which is no longer small against the piece (2^-21 of it at 2^-1054), so
                    // the point may lie off the segment by far more than a relative error: outside the domain on the hull, off its source on
                    // a constraint (found with the reference comparison: m1/subnormal-32 with an angle bound). Only a point exactly on the
                    // source, strictly between the piece's ends, is used; otherwise the piece is not split
                    if (ex < MinRoundedSplitExponent && !ExactlyWithin(A, B, pu, pw, p)) { unsplittable.Add(Key(u, w)); Note(13); return null; }
                }
                // (On the hull a point that rounds to the outside extends the triangulation's hull by a sliver, and Lawson's flips remove
                // the old hull edge; a point moved inwards instead would leave a sliver at an unconstrained hull edge.)
                if ((p.x == pu.x && p.y == pu.y) || (p.x == pw.x && p.y == pw.y) || double.IsNaN(p.x) || double.IsNaN(p.y)) {
                    unsplittable.Add(Key(u, w)); Note(14);
                    return null;
                }
                // a split point within the tolerance of another constraint would merge the two chains there (O11: nearly coincident
                // lines count as one), cutting off the region between them, eg the tip of a narrow wedge at a small input angle with its
                // input vertex (found with the corpus run, earcut's water): the piece stays as it is
                // (on the grid the exact checks of ValidGridSplit take its place: no other constraint in q's pixel, no crossing, and a
                // vertex already at q, possibly the end of another constraint, is a vertex the chains may share, as hot pixels are)
                if (grid == null && b.NearOtherSource(p.x, p.y, src)) { unsplittable.Add(Key(u, w)); Note(15); return null; }
                if (inserted >= limit) { b.SteinerLimitReached = true; return null; }
                // with lenses, Chew's deletion before the bisection (Shewchuk 2002, section 6.4): the free vertices in the diametral circle
                // that see its midpoint go; the piece itself is unchanged, its half-edge is found again (deletion renumbers triangles)
                if (lenses && deleteInCircle && DeleteFreeInCircle(e, pu, pw, p) > 0) {
                    e = k.FindEdge(u, w);
                    if (e < 0 || k.Seg[e] < 0) return b.Fail(Triangulation2FailureKind.RefinementLimit, p, "a subsegment was lost by the deletion before its split");
                }
                ++splits;
                if (CheckTagsForTests) lastOperation = "split of " + u + "=(" + pu.x.ToString("R") + ", " + pu.y.ToString("R") + ")-" + w + "=(" + pw.x.ToString("R") + ", " + pw.y.ToString("R") + ") (sources " + string.Join(",", src.Select(z => z + ":(" + b.sources[z].AX + "," + b.sources[z].AY + ")-(" + b.sources[z].BX + "," + b.sources[z].BY + ") ring " + b.sources[z].Ring)) + ") at (" + p.x.ToString("R") + ", " + p.y.ToString("R") + ") left " + TagOf(e / 3) + " right " + TagOf(k.H[e] / 3);
                var lifted = new LiftedPiece { Sources = new List<int>(src), U = pu, W = pw, Left = TagOf(e / 3), Right = TagOf(k.H[e] / 3) };
                var sources = k.Unconstrain(e);
                // without its constraint the edge may not be Delaunay: restore that first, Lawson's insertion assumes it (as at crossings)
                k.RestoreDelaunay(new[] { ((long)u << 32) | (uint)w });
                int before = k.VertexCount;
                { int fe = k.FindEdge(u, w); k.LocateHint(fe >= 0 ? fe / 3 : e / 3); }
                long walked = k.WalkSteps;
                int q = k.InsertPoint(p.x, p.y);
                SegmentWalkStepsForTests += k.WalkSteps - walked; ++SegmentLocationsForTests;
                bool reroute = k.VertexCount == before && q != u && q != w;
                if (k.VertexCount > before) {
                    ++inserted;
                    if (CountsForBenchmarks != null) ++CountsForBenchmarks[splitCause];
                    if (b.heights != null) b.SetHeight(q, u, w);
                } else if (grid == null || q == u || q == w) { unsplittable.Add(Key(u, w)); Note(14); }   // no new vertex: no progress from splitting this piece
                var pending = pendingPieces; pending.Clear();
                pending.Push(new ConstrainedDelaunayMesh2.Builder.Piece { U = q, W = w, Sources = sources });
                pending.Push(new ConstrainedDelaunayMesh2.Builder.Piece { U = u, W = q, Sources = new List<int>(sources) });
                long guard = 0;
                while (pending.Count > 0) {
                    if (++guard > 100000) return b.Fail(Triangulation2FailureKind.NodingLimit, p, "a segment split did not settle");
                    var failure = b.InsertPiece(pending.Pop(), pending);
                    if (failure != null) return failure;
                }
                // a split point that is a vertex already (on the grid, the vertex of another constraint, as ValidGridSplit allows)
                // reroutes the piece through it without necessarily rewriting a triangle: the triangles between the old piece and the
                // new chain change sides, and Collect retags only what is dirty, so they are marked (found with the grid fuzz with
                // holes: a hole edge rerouted through a segment's vertex inside the hole kept a domain triangle tagged as hole)
                if (reroute) { ++ReroutesForTests; if (!MarkSwept(k, u, w, sources)) for (int t = 0; t < k.TriangleSlots; ++t) k.Dirty.Add(t); }
                Collect(lifted);
                return null;
            }

            /// <summary> tests only: point-location walk steps and locations of the refiner's insertions in triangles and on subsegments,
            /// summed over runs on this thread (counted in every run: two additions per insertion) </summary>
            [ThreadStatic] internal static long WalkStepsForTests, LocationsForTests, SegmentWalkStepsForTests, SegmentLocationsForTests;

            /// <summary> tests and benchmarks only: free vertices deleted before bisections (lenses), summed over runs on this thread
            /// (counted in every run: one addition per deletion) </summary>
            [ThreadStatic] internal static long DeletedForTests;
            /// <summary> tests only (per thread): after every deletion pass, check it against a brute force over all triangles (slow) </summary>
            [ThreadStatic] internal static bool CheckDeletionForTests;
            /// <summary> tests and benchmarks only (per thread): Chew's deletion with lenses on or off (null: the default, on) </summary>
            [ThreadStatic] internal static bool? DeleteInCircleForBenchmarks;
            readonly bool deleteInCircle;

            /// <summary>
            /// Chew's deletion with lenses (Chew 1993; Shewchuk 2002, section 6.4, "Terminator Chew": "all free vertices that are inside the
            /// diametral circle of s and visible from the midpoint of s are deleted before s is bisected"): deletes every vertex made by
            /// refinement (not one of the input, its crossings or snap rounding's hot pixels: those come before firstSteiner) that lies in
            /// the open diametral circle of the subsegment e = pu pw, sees its midpoint (no other constrained edge meets the segment
            /// between them, closed: a touch blocks too), and is not on a constraint (DeleteVertex refuses those, so a vertex a chain is
            /// routed through stays). The split point p itself, if a vertex is there already (a reroute on the grid), stays. Called once
            /// the split point is found and the Steiner limit allows the split: a split refused before that deletes nothing. The midpoint
            /// is (pu + pw) / 2 rounded in doubles. The circle is local: the triangles are searched from e across unconstrained edges that
            /// come within twice the radius of the midpoint, in a frame centred on it and scaled by a power of two (no underflow at small
            /// scales; the margin is far above the rounding of these distances, and searching more only costs time: what is deleted is
            /// decided exactly). Returns the number deleted.
            /// </summary>
            int DeleteFreeInCircle(int e, Vector2d pu, Vector2d pw, Vector2d p)
            {
                var m = (pu + pw) * 0.5;
                var duw = pw - pu;
                int ex = ScaleOf(duw.x, duw.y, 0, 0);
                // (twice the radius: (2 |uw| / 2)^2 in the scaled frame)
                double reach2 = ScaleB(duw, -ex).LengthSquared;
                bool MeetsCircle(Vector2d a, Vector2d c)
                {
                    var sa = ScaleB(a - m, -ex); var d = ScaleB(c - a, -ex); double l2 = d.LengthSquared;
                    double t = l2 > 0 ? Math.Max(0, Math.Min(1, -sa.Dot(d) / l2)) : 0;
                    return (sa + d * t).LengthSquared <= reach2;
                }
                var seen = new HashSet<int>();
                var stack = new Stack<int>();
                foreach (int h in new[] { e, k.H[e] }) if (!k.IsGhost(h / 3) && seen.Add(h / 3)) stack.Push(h / 3);
                var blockers = new List<(Vector2d a, Vector2d c)>();
                var candidates = new SortedSet<int>();
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    for (int j = 0; j < 3; ++j) {
                        int f = 3 * t + j, v = k.V[f];
                        if (v >= firstSteiner && v != k.Infinite) candidates.Add(v);
                        var a = k.Point(v); var c = k.Point(k.V[Next(f)]);
                        if (k.Seg[f] >= 0) {
                            if (f != e && f != k.H[e]) blockers.Add((a, c));
                            continue;
                        }
                        int t2 = k.H[f] / 3;
                        if (k.IsGhost(t2) || seen.Contains(t2) || !MeetsCircle(a, c)) continue;
                        seen.Add(t2); stack.Push(t2);
                    }
                }
                var doomed = new List<int>();
                foreach (int v in candidates) if (k.Point(v) != p && InCircleAndVisible(pu, pw, k.Point(v), blockers)) doomed.Add(v);
                if (CheckDeletionForTests) CheckDeletion(e, pu, pw, p, doomed, true);
                int deleted = 0;
                foreach (int v in doomed) if (k.DeleteVertex(v)) ++deleted;
                DeletedForTests += deleted;
                if (CheckTagsForTests && deleted > 0) lastOperation = "deletion of " + deleted + " free vertices before the split of " + k.V[e] + "-" + k.V[Next(e)];
                if (CheckDeletionForTests) CheckDeletion(-1, pu, pw, p, doomed, false);
                return deleted;
            }

            /// <summary>
            /// tests only: the deletion pass against a brute force over every triangle and every constrained edge. Before the deletion,
            /// each vertex to be deleted lies in the open diametral circle of pu pw, sees its midpoint and is a free vertex; after it, no
            /// free vertex (made by refinement, in triangles of one tag only, none of them a ghost, on no constrained edge) other than one at
            /// p is left in the circle that sees the midpoint
            /// </summary>
            void CheckDeletion(int e, Vector2d pu, Vector2d pw, Vector2d p, List<int> doomed, bool before)
            {
                var all = new List<(Vector2d a, Vector2d c)>();
                var free = new Dictionary<int, bool>();
                var tagOf = new Dictionary<int, int>();
                for (int t = 0; t < k.TriangleSlots; ++t) {
                    bool ghost = k.IsGhost(t);
                    for (int j = 0; j < 3; ++j) {
                        int f = 3 * t + j, v = k.V[f];
                        if (v == k.Infinite) continue;
                        if (k.V[Next(f)] != k.Infinite && k.Seg[f] >= 0) {
                            var a = k.Point(v); var c = k.Point(k.V[Next(f)]);
                            // (the piece being split is no obstacle to its own midpoint)
                            if (!((a == pu && c == pw) || (a == pw && c == pu))) all.Add((a, c));
                        }
                        bool ok = !ghost && k.Seg[f] < 0 && k.Seg[3 * t + (j + 2) % 3] < 0 && v >= firstSteiner;
                        if (!ghost) { if (!tagOf.TryGetValue(v, out int tag)) tagOf[v] = k.Tag[t]; else if (tag != k.Tag[t]) ok = false; }
                        free[v] = free.TryGetValue(v, out bool was) ? was && ok : ok;
                    }
                }
                if (before) {
                    foreach (int v in doomed)
                        if (!InCircleAndVisible(pu, pw, k.Point(v), all) || !free.TryGetValue(v, out bool f) || !f)
                            throw new InvalidOperationException("deletion: vertex " + v + " at " + k.Point(v) + " is not a free vertex in the circle that sees the midpoint");
                    return;
                }
                foreach (var pair in free)
                    if (pair.Value && k.Point(pair.Key) != p && InCircleAndVisible(pu, pw, k.Point(pair.Key), all))
                        throw new InvalidOperationException("deletion: free vertex " + pair.Key + " at " + k.Point(pair.Key) + " is left in the circle of " + pu + " " + pw + " and sees its midpoint");
            }

            /// <summary> v lies in the open diametral circle of pu pw (exact) and sees its midpoint: the segment between them meets none of
            /// the blocking constrained edges, closed (exact orientations; the midpoint is (pu + pw) / 2 rounded in doubles, as the search's) </summary>
            internal static bool InCircleAndVisible(Vector2d pu, Vector2d pw, Vector2d v, List<(Vector2d a, Vector2d c)> blockers)
            {
                if (DotSign(pu, pw, v) >= 0) return false;
                var m = (pu + pw) * 0.5;
                foreach (var (a, c) in blockers) if (SegmentsMeet(m, v, a, c)) return false;
                return true;
            }

            /// <summary> the closed segments ab and cd share a point (exact orientations) </summary>
            internal static bool SegmentsMeet(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
            {
                int o1 = Math.Sign(ExactPredicates.Orient2d(a, b, c)), o2 = Math.Sign(ExactPredicates.Orient2d(a, b, d));
                int o3 = Math.Sign(ExactPredicates.Orient2d(c, d, a)), o4 = Math.Sign(ExactPredicates.Orient2d(c, d, b));
                if (o1 * o2 > 0 || o3 * o4 > 0) return false;
                if (o1 != 0 || o2 != 0 || o3 != 0 || o4 != 0) return true;
                // collinear: the boxes overlap
                return Math.Max(Math.Min(a.x, b.x), Math.Min(c.x, d.x)) <= Math.Min(Math.Max(a.x, b.x), Math.Max(c.x, d.x))
                    && Math.Max(Math.Min(a.y, b.y), Math.Min(c.y, d.y)) <= Math.Min(Math.Max(a.y, b.y), Math.Max(c.y, d.y));
            }

            /// <summary>
            /// marks dirty the triangles between a lifted piece u w and the chain from u to w that replaced it: every triangle inside
            /// the convex hull of the chain that is connected, inside it, to a triangle at a chain edge. The region between the piece
            /// and the chain lies inside that hull; each part of it is bounded by chain edges and by triangles across the line u w,
            /// which the flips that removed the edge u w rewrote (they are dirty already), so each part touches a chain edge and is
            /// reached. Marking more than the region is harmless: Collect retags a dirty triangle from the pieces and its unchanged
            /// neighbours. False when the chain is not found (not seen; the caller then marks everything).
            /// </summary>
            internal static bool MarkSwept(DelaunayKernel2 k, int u, int w, List<int> sources)
            {
                var pu = k.Point(u); var pw = k.Point(w);
                // the chain: constrained edges carrying the piece's sources, each step to a vertex between u and w along u w (exact)
                var chain = new List<int> { u };
                int prev = -1, c = u;
                while (c != w) {
                    int next = -1;
                    foreach (int h in k.Star(c)) {
                        if (k.Seg[h] < 0) continue;
                        int x = k.V[Next(h)];
                        if (x == prev || !sources.All(k.Sources[k.Seg[h]].Contains)) continue;
                        if (x == w) { next = w; break; }
                        var px = k.Point(x);
                        if (next < 0 && DotSign(px, pw, pu) > 0 && DotSign(px, pu, pw) > 0) next = x;
                    }
                    if (next < 0 || chain.Count > k.VertexCount) return false;
                    chain.Add(next); prev = c; c = next;
                }
                // its convex hull, counter-clockwise (Andrew's monotone chain, exact orientations)
                var pts = chain.Select(k.Point).Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
                var hull = new List<Vector2d>();
                foreach (var pass in new[] { pts, Enumerable.Reverse(pts).ToList() }) {
                    int start = hull.Count;
                    foreach (var p in pass) {
                        while (hull.Count >= start + 2 && ExactPredicates.Orient2d(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                        hull.Add(p);
                    }
                    hull.RemoveAt(hull.Count - 1);
                }
                if (hull.Count < 3) return true;   // the chain lies on the line u w: no region between the two
                bool Inside(int v)
                {
                    var x = k.Point(v);
                    for (int i = 0; i < hull.Count; ++i) if (ExactPredicates.Orient2d(hull[i], hull[(i + 1) % hull.Count], x) < 0) return false;
                    return true;
                }
                bool InRegion(int t) => !k.IsGhost(t) && Inside(k.V[3 * t]) && Inside(k.V[3 * t + 1]) && Inside(k.V[3 * t + 2]);
                var seen = new HashSet<int>();
                var stack = new Stack<int>();
                for (int i = 0; i + 1 < chain.Count; ++i) {
                    int h = k.FindEdge(chain[i], chain[i + 1]);
                    foreach (int t in new[] { h / 3, k.H[h] / 3 }) if (InRegion(t) && seen.Add(t)) stack.Push(t);
                }
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    k.Dirty.Add(t);
                    for (int j = 0; j < 3; ++j) { int t2 = k.H[3 * t + j] / 3; if (InRegion(t2) && seen.Add(t2)) stack.Push(t2); }
                }
                return true;
            }
        }
    }
}
