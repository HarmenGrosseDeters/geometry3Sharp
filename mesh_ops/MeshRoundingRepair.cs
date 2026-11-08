// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Repairs what rounding exact vertex coordinates to double leaves behind (eg the output of
    /// ExactMeshBoolean, whose intersection points are rational): self-intersections, and degenerate
    /// triangles (needles and caps where intersection points nearly coincide), by local edits whose result
    /// is checked with exact predicates on the doubles. Only edits that create neither a new intersection nor
    /// a new degenerate triangle in their neighbourhood and remove at least one are applied, so the counts
    /// never grow and the repair terminates.
    ///
    /// The edits follow the modification stage of Milenkovic and Sacks ("Geometric rounding and feature
    /// separation in meshes", CAD 2019), applied after rounding: contract a short edge (length at most
    /// MaxDisplacement times the diagonal; the kept vertex at one of the two ends, which keeps the
    /// coordinates as they are, or at the midpoint), flip an edge (caps), and, beyond the paper, move a single
    /// vertex by a tiny amount (along the normal of the triangle it intersects or away from the base of a
    /// cap), which separates intersections that no contraction can (sheets that come within a rounding unit
    /// of each other), and zip two caps on both sides of an edge whose apexes lie on it (a flip would give two
    /// caps again: both are removed and the neighbouring edges along the segment split at the apexes), and,
    /// generalizing the zip, zip a strip: degenerate triangles and slivers connected across edges along a line (eg
    /// fans of nearly collinear points in degenerate input, where every flip gives slivers again), a disk whose
    /// boundary runs strictly monotone along the segment between its two farthest boundary vertices on both sides and
    /// all of whose vertices, the inner ones too, lie between them along it, is removed, and the triangles outside it
    /// along both sides (distinct, their third corners outside the strip) are split at the vertices of the other side,
    /// so that both share one chain (StripZips). Where the coordinates are coarse (far from the origin compared to the
    /// mesh's size, see UlpFactor), a vertex of valence 3 that the rounding put onto the position of another vertex is
    /// removed, its three triangles replaced by the one of its neighbours (VertexRemovals), and FixedVertexF is not used if
    /// FixedWhereCoarse is false (the fixed vertices carry rounding errors of the size of the bound there themselves). The
    /// contractions keep the mesh manifold (link condition, DMesh3.CollapseEdge). Edits at the boundary of open
    /// meshes stay on the boundary curve: a boundary vertex keeps its coordinates in a contraction and is
    /// removed only by contracting a boundary edge into its other end (an interior edge between two boundary
    /// vertices, which would pinch the mesh, is not contracted; an interior vertex may be contracted into a
    /// boundary vertex), and it moves only along one of its boundary edges of the input whose other end has not
    /// moved (not past that end, and over several moves towards the same end); flips and zips do not change
    /// boundary edges. So the boundary keeps its loops, every boundary vertex left stays where it was or on the
    /// input curve, and the curve moves by at most the length of a contracted edge or a move. Bowtie vertices on
    /// the boundary are not edited. No triangle may turn over in an edit (a fold, which is not an intersection),
    /// except slivers
    /// (below 100 times the degenerate area) and triangles not higher than twice the distance a vertex moves
    /// (a move cannot turn over a triangle higher than the move, so the rule matters for flips and zips: tiny
    /// triangles may turn over, they are only checked for intersections and degeneracy). Closed components that keep a
    /// degenerate triangle or were edited, and are tiny (below 1e4 times the degenerate area, eg a flat tetrahedron
    /// between intersection points that nearly coincide) or thinner than MaxDisplacement times the diagonal
    /// (eg the slivers that are the difference of two nearly coincident solids), are removed if
    /// RemoveTinyComponents, as Milenkovic and Sacks do. Open components are kept (they may close the rest of the
    /// mesh by position), so an isolated sliver that no edit can repair is left and counted in RemainingDegenerate.
    ///
    /// The degenerate test is the one of MeshRobustRepair.NotCleanReason (area below DegenerateArea times the
    /// squared diagonal, or exactly collinear corners); intersections are those of
    /// MeshRepairMetrics.SelfIntersections (exact, also between neighbours that overlap). With
    /// SharedPositionsTouch, vertices at the same position count as shared, so that sheets which touch at
    /// duplicated vertices (ExactMeshBoolean splits where solids touch) do not count as intersecting there; then
    /// no edit moves a vertex onto a position where other vertices are (which would make a new, uncounted
    /// contact), and a contraction keeps the contact points as they are (the kept vertex at its own position).
    ///
    /// Usage: construct with the mesh (modified in place), optionally set FixedVertexF to the vertices whose
    /// position should be kept where possible (eg input vertices), call Apply(). Apply() returns IsClean:
    /// RemainingDegenerate is counted again at the end, RemainingIntersections too if there were any (no
    /// edit adds one: each is checked exactly against all triangles near it), so a clean result is free of
    /// self-intersections and degenerate triangles (Milenkovic and Sacks get the same guarantee by separating
    /// the features before rounding; this works on the rounded mesh and has no guarantee that every case can
    /// be repaired).
    ///
    /// Bounded displacement: every vertex has a drift, 0 at the start. An edit gives all vertices of the triangles it
    /// removes and adds the largest drift among them plus a bound on how far it moves the surface: the distance a
    /// vertex moves (moves, slides, contractions: the kept vertex's new position from both ends), for a flip the
    /// smallest of twice the distance between the flipped edge and the new one, a bound from the distances of the
    /// corners to the other side's triangles (each bounded by the distance to the nearest edge), and 0 for a convex
    /// quad in one plane (exact), for a
    /// zip twice the distance of the apexes to the edge, for a vertex removal the distance of the vertex to the new
    /// triangle (with the vertex moved to its nearest point there, its three triangles tile the new one), for a strip zip twice the largest distance of the strip's
    /// vertices to the line through its ends (with them moved onto it, in their order along it, the old and the new
    /// triangles cover the same set). An edit after which a drift would exceed maxLength (MaxDisplacement
    /// times the diagonal, or the ulp or the absolute term, see UlpFactor) is not applied. By induction over the edits, every point of a triangle lies within the
    /// largest drift of its vertices of the surface at the start, and every point of that surface within it of the
    /// repaired one: each edit moves every point of the triangles it removes to within its bound of the triangles it
    /// adds or of a simplex spanned by their vertices, and back (a contraction of an ear, whose vertex has a single
    /// triangle, adds none: that triangle goes to the edge across, which is in an untouched triangle with both ends
    /// among the edit's vertices). Edits stay within one connected component. So the Hausdorff distance between the
    /// repaired surface and the surface at the start (the rounded mesh), both without the components removed as tiny
    /// or thin, is at most MaxDrift &lt;= maxLength, up to the rounding of the distance computations: an absolute
    /// error of a few units in the last place of the largest coordinate per edit, negligible against maxLength unless
    /// the mesh lies far from the origin compared to its size. The volume of a closed solid changes in a move or a
    /// contraction by at most the edit's bound times the area of the triangles it removes (the change is a third of
    /// the move times the area vector of the link); flips and zips change it by the volume between the old and the
    /// new triangles (a tetrahedron for a flip), measured at most a third of the same product (strip zips at most 0.18 of it, over 9039 strip
    /// zips of poked-sphere clusters with w >= 1e-13, at most 0.07 with the offsets in the surface). Removed components
    /// add their volume (thin ones: below maxLength times half their area; tiny ones: area below 1e4 times the
    /// degenerate area).
    /// Cost: one global exact self-intersection check (two or more if there are intersections), plus local
    /// work per defect.
    /// Implemented from the paper, no code taken over (docs/SOURCES.md).
    /// </summary>
    public class MeshRoundingRepair
    {
        /// <summary> the mesh, repaired in place </summary>
        public DMesh3 Mesh;
        /// <summary> a triangle is degenerate below this area, relative to the squared bounding box diagonal (as MeshRobustRepair.NotCleanReason) </summary>
        public double DegenerateArea = 1e-12;
        /// <summary>
        /// longest edge contracted and farthest vertex move, relative to the bounding box diagonal; also the bound on how
        /// far the surface moves in all edits together (see the class comment, MaxDrift)
        /// </summary>
        public double MaxDisplacement = 1e-5;
        /// <summary> vertices at the same position count as shared vertices (touching sheets, see class comment) </summary>
        public bool SharedPositionsTouch = false;
        /// <summary> vertices whose position is kept where possible (tried last as the moved vertex; a strip zip removes the inner vertices of its strip, fixed or not); null = none </summary>
        public Func<int, bool> FixedVertexF = null;
        /// <summary>
        /// whether FixedVertexF applies also where the coordinates are coarse (see UlpFactor). True by default. There the
        /// fixed vertices carry rounding errors of the size of the bound themselves, and keeping them can make the edits
        /// on the others cascade beyond it; ExactMeshBoolean, whose fixed vertices are the rounded input vertices, sets
        /// it to false
        /// </summary>
        public bool FixedWhereCoarse = true;
        /// <summary> passes over the defects </summary>
        public int MaxPasses = 4;
        /// <summary>
        /// bounds the time on meshes with many defects that cannot be repaired: proposed edits evaluated at most, per
        /// defect found at the start (plus 10000), and exact triangle pair tests in the evaluations at most
        /// (EvaluationLimitReached tells; the result is valid, the defects left are counted)
        /// </summary>
        public int MaxEvaluationsPerDefect = 50;
        /// <summary>
        /// effort budget for hopeless inputs: no edit is evaluated once the exact triangle pair tests of the evaluations
        /// exceed it (the last evaluation may pass it by its own tests; 0 stops once one pair test was made; see
        /// MaxEvaluationsPerDefect). Measured (November 2026, after #327): 5M instead of 20M lost and gained no clean result
        /// (generated families 1340 of 1512 clean, Thingi10K self-unions 336 of 799, six rotated Thingi10K booleans 0 of
        /// 18 at 5M to 80M) and cut the time of the cases that reach it (rounding of the rotated cases 193 to 96 s).
        /// ExactMeshBoolean.RoundingMaxPairTests and MeshRobustRepair.RoundingMaxPairTests pass it through
        /// </summary>
        public long MaxPairTests = 5000000;
        /// <summary> the repair stopped at MaxEvaluationsPerDefect or MaxPairTests </summary>
        public bool EvaluationLimitReached;
        /// <summary> remove closed components that are tiny or thin and keep a degenerate triangle or were edited (see class comment) </summary>
        public bool RemoveTinyComponents = true;

        /// <summary> intersecting triangle pairs and degenerate triangles before the repair </summary>
        public int InitialIntersections, InitialDegenerate;
        /// <summary> intersecting triangle pairs and degenerate triangles after the repair (exact, see class comment) </summary>
        public int RemainingIntersections, RemainingDegenerate;
        /// <summary> edits applied (see class comment) </summary>
        public int Collapses, Flips, Moves, Zips;
        /// <summary> of the Zips: strips of degenerate triangles zipped at once (see class comment) </summary>
        public int StripZips;
        /// <summary>
        /// the tolerance in units in the last place of the largest coordinate: the bound maxLength is the largest of
        /// MaxDisplacement times the diagonal, UlpFactor times ulp(largest absolute coordinate) and AbsoluteTolerance.
        /// Far from the origin compared to its size (eg UTM coordinates), a mesh's rounding errors exceed the relative
        /// bound; there the ulp term lets the edits remove what the rounding broke (owner decision of 2026-11-02, 256:
        /// 0.24 micrometres at 5.7e6). It changes nothing unless the largest coordinate exceeds about 1.75e8 times the
        /// diagonal (docs/design/SAFE_ROUNDING.md section 5). 0 turns it off.
        /// </summary>
        public double UlpFactor = 256;
        /// <summary>
        /// an absolute tolerance the caller sets (eg 1e-6 for metres in GIS), see UlpFactor; 0 = none. Like the other
        /// terms it also bounds the edges contracted and the thickness of the closed components removed as thin. Unlike
        /// the ulp term it does not switch on what applies where the coordinates are coarse (vertex removals, fixed
        /// vertices not kept)
        /// </summary>
        public double AbsoluteTolerance = 0;
        /// <summary>
        /// of the edits: vertices of valence 3 removed where the rounding put them onto the position of another
        /// vertex (only with SharedPositionsTouch, and only where the ulp term of the bound is active, see UlpFactor)
        /// </summary>
        public int VertexRemovals;
        /// <summary> the bound on the drift: the largest of the three terms (see UlpFactor) </summary>
        public double MaxLength { get { return maxLength; } }
        /// <summary>
        /// the smallest drift that a proposed edit rejected by the bound would have needed (infinity if none was
        /// rejected): how large the bound would have to be for that edit (StrictRounding of ExactMeshBoolean reports it).
        /// Edits that are not proposed (contractions of edges longer than the bound, longer moves) are not counted
        /// </summary>
        public double SmallestRejectedDrift;
        /// <summary> the centroid of a defect left after the repair (a degenerate triangle, else an intersecting one) </summary>
        public Vector3d RemainingDefectAt;
        /// <summary> the most triangles of a strip that is zipped at once </summary>
        public int MaxStripTriangles = 64;
        // strips proposed at most per degenerate triangle (see try_strip)
        const int MaxStripProposals = 32;
        /// <summary> components, and their triangles, removed as tiny or thin (RemoveTinyComponents) </summary>
        public int RemovedComponents, RemovedTriangles;
        /// <summary>
        /// the largest drift of a vertex after the repair, absolute: a bound on the Hausdorff distance between the
        /// repaired surface and the surface at the start, without the removed components (see the class comment; up to an
        /// absolute rounding error of a few ulps of the largest coordinate per edit); at most MaxDisplacement times the
        /// diagonal
        /// </summary>
        public double MaxDrift;
        /// <summary> no self-intersections and no degenerate triangles remain </summary>
        public bool IsClean { get { return RemainingIntersections == 0 && RemainingDegenerate == 0; } }

        public MeshRoundingRepair(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        double areaEps, maxLength;
        // the ulp term of the bound is active (see UlpFactor)
        bool coarse;
        // per vertex: its drift (see the class comment); for the proposed edit: its vertices, and their drift if it is
        // applied
        double[] drift;
        readonly List<int> editVerts = new List<int>();
        double editDrift;
        // triangles above this many times the degenerate area must not turn over in an edit
        const double OrientedArea = 100;
        // vertices per position (SharedPositionsTouch)
        Dictionary<Vector3d, List<int>> atPosition;
        // per vertex: the smallest id at its position (in atPosition; kept with it by position_add, position_remove)
        int[] canonId;
        // spatial index over the triangles at the last build; triangles changed since are in dirty, with an index
        // of their own (rebuilt when they change, it is small)
        TriangleBoxTree tree, dirtyTree;
        List<int> treeTids, dirtyTids;
        HashSet<int> dirty = new HashSet<int>();
        // the intersecting pairs of the last intersecting_pairs, and the triangles changed or removed since (null:
        // not computed for the current mesh)
        List<Index2i> lastPairs;
        HashSet<int> pairsStale = new HashSet<int>();

        // for tests: Apply on this thread checks the incremental intersecting pairs against a full search, and the
        // canonical ids against the dictionary (InvalidOperationException if they differ), and proposes the moves past
        // the evaluation limit in full (as before the shortcut in try_move: the same SmallestRejectedDrift)
        [ThreadStatic] internal static bool CheckOnThisThread;
        bool check;
        // the vertices at their exact position (ExactMeshBoolean: the input points of the result), or null: the full search
        // skips the pairs of two triangles that existed at the start with three such vertices and have not changed since
        // (the exact arrangement already resolved them, docs/design/ROUNDING_EXACT_PAIRS.md); used with
        // SharedPositionsTouch only, and by the next Apply only (it is cleared there). The caller's promise holds
        // at the start of that Apply: such a vertex is at its exact point (an input point at its input double, not rounded
        // or snapped); a vertex the repair moves (set_vertex, the one place that writes positions) takes its triangles
        // out of the set
        internal Func<int, bool> ExactVertexF = null;
        // per triangle id: its vertices at the start if it is such a triangle (else -1), cleared by mark_dirty
        Index3i[] exactAtStart;
        // for tests: the triangles whose queries the last full searches skipped
        internal long SkippedExactQueries;
        // for tests: the searches of intersecting_pairs in the last Apply (full, incremental, none: the mesh unchanged)
        internal int FullPairSearches, IncrementalPairSearches, UnchangedPairSearches;
        // for tests: moves past the evaluation limit whose drift try_move computed directly, in the last Apply
        internal long MovesPastTheLimit;
        // for tests: such moves are not counted in SmallestRejectedDrift (to show that they set it)
        internal bool SkipMovesPastTheLimit;
        // applied edits so far (a version of the mesh)
        long version;
        // vertices of applied edits
        HashSet<int> touched = new HashSet<int>();
        // vertices of the triangles changed in the current pass
        HashSet<int> changed = new HashSet<int>();
        // boundary vertices at the start and their positions, the boundary edges at the start (as vertex pairs,
        // the smaller id first), and the vertex a slid vertex was moved towards
        Dictionary<int, Vector3d> inputBoundary = new Dictionary<int, Vector3d>();
        HashSet<Index2i> inputBoundaryEdges = new HashSet<Index2i>();
        Dictionary<int, int> slidTowards = new Dictionary<int, int>();

        /// <summary> repair (see class comment); returns IsClean </summary>
        [MethodImpl(JitHints.Optimize)]
        public bool Apply()
        {
            Collapses = Flips = Moves = Zips = StripZips = VertexRemovals = RemovedComponents = RemovedTriangles = 0;
            MaxDrift = 0;
            drift = new double[Mesh.MaxVertexID];
            touched.Clear();
            inputBoundary.Clear();
            inputBoundaryEdges.Clear();
            slidTowards.Clear();
            foreach (int eid in Mesh.BoundaryEdgeIndices()) {
                Index2i ev = Mesh.GetEdgeV(eid);
                inputBoundaryEdges.Add(new Index2i(Math.Min(ev.a, ev.b), Math.Max(ev.a, ev.b)));
                inputBoundary[ev.a] = Mesh.GetVertex(ev.a);
                inputBoundary[ev.b] = Mesh.GetVertex(ev.b);
            }
            changed.Clear();
            SmallestRejectedDrift = double.PositiveInfinity;
            RemainingDefectAt = Vector3d.Zero;
            coarse = false;
            if (SharedPositionsTouch == false)
                atPosition = null;
            // the exact vertices: read once and cleared, also where this Apply returns early (the promise holds for the
            // mesh as the caller handed it over: this Apply may move such vertices, and the mesh may be changed before
            // another Apply, which then searches every pair); only with SharedPositionsTouch (without it the full search
            // reports vertices of one exact point at one position, duplicated where the solid touches itself, as
            // intersecting, which the lemma does not cover)
            Func<int, bool> exactF = SharedPositionsTouch ? ExactVertexF : null;
            ExactVertexF = null;
            exactAtStart = null;
            if (Mesh.TriangleCount == 0) {
                InitialIntersections = InitialDegenerate = RemainingIntersections = RemainingDegenerate = 0;
                return true;
            }
            AxisAlignedBox3d bounds = gs.MeshRepairMetrics.TriangleBounds(Mesh);
            double diag = bounds.DiagonalLength;
            areaEps = DegenerateArea * diag * diag;
            // (ulp of the largest absolute coordinate, a power of two: 2^k scales keep the same result, bit for bit)
            // (of the triangles' vertices, as the diagonal: unreferenced vertices do not count; a bound that is not finite
            // has no ulp term)
            double largest = Math.Max(bounds.Min.MaxAbs, bounds.Max.MaxAbs);
            double ulpTerm = UlpFactor * ulp(largest);
            if (!(ulpTerm >= 0 && ulpTerm < double.PositiveInfinity))
                ulpTerm = 0;
            maxLength = Math.Max(MaxDisplacement * diag, Math.Max(ulpTerm, AbsoluteTolerance));
            // (a bound that is not finite, from coordinates that are not or a diagonal that overflows, rejects every edit)
            if (!(maxLength < double.PositiveInfinity))
                maxLength = double.NaN;
            coarse = ulpTerm > MaxDisplacement * diag;
            if (SharedPositionsTouch) {
                atPosition = new Dictionary<Vector3d, List<int>>();
                canonId = new int[Mesh.MaxVertexID];
                foreach (int vid in Mesh.VertexIndices())
                    position_add(vid, Mesh.GetVertex(vid));
            }
            SkippedExactQueries = 0;
            if (exactF != null) {
                exactAtStart = new Index3i[Mesh.MaxTriangleID];
                foreach (int tid in Mesh.TriangleIndices()) {
                    Index3i tv = Mesh.GetTriangle(tid);
                    exactAtStart[tid] = (exactF(tv.a) && exactF(tv.b) && exactF(tv.c)) ? tv : new Index3i(-1, -1, -1);
                }
            }
            rebuild_tree();
            lastPairs = null;
            check = CheckOnThisThread;
            FullPairSearches = IncrementalPairSearches = UnchangedPairSearches = 0;
            MovesPastTheLimit = 0;

            List<Index2i> pairs = intersecting_pairs();
            List<int> bad = degenerate_triangles();
            InitialIntersections = pairs.Count;
            InitialDegenerate = bad.Count;
            evaluations = pairTests = 0;
            maxEvaluations = 10000 + (long)MaxEvaluationsPerDefect * (pairs.Count + bad.Count);
            EvaluationLimitReached = false;
            frozenMost = null;
            repair_passes(ref pairs, ref bad);
            // vertices that the rounding put onto the position of another (they touch: SharedPositionsTouch takes them as
            // shared, MeshRobustRepair.NotCleanReason as intersecting): a vertex of valence 3 there is removed, if the
            // result improves. Only where the coordinates are coarse (the ulp term of the bound is active): elsewhere
            // such contacts are where solids touch (ExactMeshBoolean)
            if (coarse && atPosition != null && EvaluationLimitReached == false && remove_contact_vertices() > 0) {
                // (and the passes again where the removals changed the mesh)
                bad = degenerate_triangles();
                pairs = intersecting_pairs();
                repair_passes(ref pairs, ref bad);
            }
            if (RemoveTinyComponents && (bad.Count > 0 || touched.Count > 0) && remove_tiny_components(bad) > 0) {
                bad = degenerate_triangles();
                if (pairs.Count > 0)
                    pairs = intersecting_pairs();
            }
            RemainingIntersections = pairs.Count;
            RemainingDegenerate = bad.Count;
            int at = bad.Count > 0 ? bad[0] : pairs.Count > 0 ? pairs[0].a : -1;
            if (at >= 0 && Mesh.IsTriangle(at))
                RemainingDefectAt = Mesh.GetTriCentroid(at);
            return IsClean;
        }


        // the passes over the defects (see Apply), until one changes nothing
        [MethodImpl(JitHints.Optimize)]
        void repair_passes(ref List<Index2i> pairs, ref List<int> bad)
        {
            for (int pass = 0; pass < MaxPasses && (pairs.Count > 0 || bad.Count > 0); ++pass) {
                int edits = Collapses + Flips + Moves + Zips;
                // after the first pass, only where the mesh changed since (elsewhere the same edits would fail again)
                HashSet<int> retry = (pass > 0) ? changed : null;
                changed = new HashSet<int>();
                // smallest first: their contraction often fixes the larger neighbours too
                bad.Sort((x, y) => area(x).CompareTo(area(y)));
                foreach (int t in bad) {
                    if (Mesh.IsTriangle(t) == false || is_bad(t) == false)
                        continue;
                    Index3i tv = Mesh.GetTriangle(t);
                    if ((retry == null || retry.Contains(tv.a) || retry.Contains(tv.b) || retry.Contains(tv.c)))
                        fix_degenerate(t);
                }
                // (no edit adds an intersecting pair: without pairs there are none to find)
                if (pairs.Count > 0) {
                    if (Collapses + Flips + Moves + Zips > edits)
                        pairs = intersecting_pairs();
                    foreach (Index2i p in pairs)
                        if (Mesh.IsTriangle(p.a) && Mesh.IsTriangle(p.b) && intersects(p.a, p.b))
                            fix_intersection(p.a, p.b);
                    pairs = intersecting_pairs();
                }
                bad = degenerate_triangles();
                if (Collapses + Flips + Moves + Zips == edits || EvaluationLimitReached)
                    break;
            }
        }

        // ---- geometry of the current mesh, or of a proposed edit (one vertex at another position) ----

        int movedV = -1;
        Vector3d movedPos;
        // the vertex a proposed contraction removes, or -1
        int vanishing = -1;

        Vector3d pos(int v)
        {
            return (v == movedV) ? movedPos : Mesh.GetVertex(v);
        }

        Triangle3d triangle(Index3i t)
        {
            return new Triangle3d(pos(t.a), pos(t.b), pos(t.c));
        }

        AxisAlignedBox3d box(Index3i t)
        {
            var b = new AxisAlignedBox3d(pos(t.a));
            b.Contain(pos(t.b)); b.Contain(pos(t.c));
            return b;
        }

        double area(int tid)
        {
            Index3i t = Mesh.GetTriangle(tid);
            return area(t);
        }

        double area(Index3i t)
        {
            Vector3d a = pos(t.a);
            return 0.5 * (pos(t.b) - a).Cross(pos(t.c) - a).Length;
        }

        bool is_bad(int tid)
        {
            return is_bad(Mesh.GetTriangle(tid));
        }

        bool is_bad(Index3i t)
        {
            if (area(t) < areaEps)
                return true;
            Triangle3d tri = triangle(t);
            return ExactTriangleIntersection.IsDegenerate(ref tri);
        }

        static Vector3d key(Vector3d p)
        {
            // -0.0 and 0.0 are the same position
            return new Vector3d(p.x + 0.0, p.y + 0.0, p.z + 0.0);
        }

        void position_add(int v, Vector3d p)
        {
            List<int> list;
            if (atPosition.TryGetValue(key(p), out list) == false)
                atPosition[key(p)] = list = new List<int>();
            list.Add(v);
            int least = list.Count > 1 ? Math.Min(canonId[list[0]], v) : v;
            foreach (int u in list)
                canonId[u] = least;
        }

        void position_remove(int v, Vector3d p)
        {
            List<int> list;
            if (atPosition.TryGetValue(key(p), out list) && list.Remove(v)) {
                if (list.Count == 0)
                    atPosition.Remove(key(p));
                else if (canonId[list[0]] == v) {
                    int least = list[0];
                    foreach (int u in list)
                        least = Math.Min(least, u);
                    foreach (int u in list)
                        canonId[u] = least;
                }
            }
        }

        // the vertex id that stands for all vertices at v's position (SharedPositionsTouch), else v
        [MethodImpl(JitHints.Optimize)]
        int canonical(int v)
        {
            if (atPosition == null)
                return v;
            if (check) {
                int fast = canonical_of(v, true), slow = canonical_of(v, false);
                if (fast != slow)
                    throw new InvalidOperationException("MeshRoundingRepair: canonical id differs from the dictionary");
                return fast;
            }
            return canonical_of(v, true);
        }

        [MethodImpl(JitHints.Optimize)]
        int canonical_of(int v, bool cached)
        {
            // (v is at its position in the mesh, with the smallest id there in canonId: that one, or the moved vertex
            // if it is proposed there with a smaller id; the dictionary only if the moved vertex is the smallest one
            // at v's position, which it leaves, or v is the moved vertex)
            if (cached && v != movedV) {
                int least = canonId[v];
                if (least != movedV) {
                    if (movedV >= 0 && movedV < least && key(movedPos) == key(Mesh.GetVertex(v)))
                        return movedV;
                    return least;
                }
            }
            // the smallest id at v's position: the vertices there, the moved one at its proposed position
            Vector3d p = key(pos(v));
            int best = v;
            List<int> list;
            if (atPosition.TryGetValue(p, out list))
                foreach (int u in list)
                    if (u != movedV && u < best)
                        best = u;
            if (movedV >= 0 && movedV < best && key(movedPos) == p)
                best = movedV;
            return best;
        }

        Index3i canonical(Index3i t)
        {
            return new Index3i(canonical(t.a), canonical(t.b), canonical(t.c));
        }

        // do the non-degenerate triangles t and u intersect beyond what they share (MeshRepairMetrics)
        bool intersects(Index3i t, ref Triangle3d T, Index3i u, ref Triangle3d U)
        {
            return ExactTriangleIntersection.IntersectsBeyondShared(canonical(t), ref T, canonical(u), ref U);
        }

        bool intersects(int ta, int tb)
        {
            Index3i a = Mesh.GetTriangle(ta), b = Mesh.GetTriangle(tb);
            Triangle3d A = triangle(a), B = triangle(b);
            if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B))
                return false;
            return intersects(a, ref A, b, ref B);
        }


        // ---- spatial queries ----

        [MethodImpl(JitHints.Optimize)]
        void rebuild_tree()
        {
            var boxes = new List<AxisAlignedBox3d>();
            treeTids = new List<int>();
            foreach (int tid in Mesh.TriangleIndices()) {
                treeTids.Add(tid);
                boxes.Add(box(Mesh.GetTriangle(tid)));
            }
            tree = new TriangleBoxTree(boxes);
            dirty.Clear();
            dirtyTree = null;
            version++;
        }

        void mark_dirty(int tid)
        {
            if (exactAtStart != null && tid >= 0 && tid < exactAtStart.Length)
                exactAtStart[tid] = new Index3i(-1, -1, -1);
            dirty.Add(tid);
            pairsStale.Add(tid);
            dirtyTree = null;
            version++;
            if (Mesh.IsTriangle(tid)) {
                Index3i tv = Mesh.GetTriangle(tid);
                changed.Add(tv.a); changed.Add(tv.b); changed.Add(tv.c);
            }
            // (the changed triangles are searched linearly)
            if (dirty.Count > Math.Max(1024, Mesh.TriangleCount / 16))
                rebuild_tree();
        }

        // the current triangles whose box overlaps query (each once)
        [MethodImpl(JitHints.Optimize)]
        void near(AxisAlignedBox3d query, List<int> result)
        {
            result.Clear();
            tree.Query(query, (i) => {
                int t = treeTids[i];
                if (dirty.Contains(t) == false && Mesh.IsTriangle(t))
                    result.Add(t);
            });
            if (dirty.Count == 0)
                return;
            if (dirtyTree == null) {
                dirtyTids = new List<int>();
                var boxes = new List<AxisAlignedBox3d>();
                int save = movedV;
                movedV = -1;
                foreach (int t in dirty) {
                    if (Mesh.IsTriangle(t) == false)
                        continue;
                    dirtyTids.Add(t);
                    boxes.Add(box(Mesh.GetTriangle(t)));
                }
                movedV = save;
                dirtyTree = new TriangleBoxTree(boxes);
            }
            dirtyTree.Query(query, (i) => result.Add(dirtyTids[i]));
        }

        void remove_triangle(int tid, bool removeIsolatedVertices = false)
        {
            pairsStale.Add(tid);
            Mesh.RemoveTriangle(tid, removeIsolatedVertices, false);
        }

        // the intersecting pairs of the current mesh (ta < tb, sorted). Incremental: the pairs of the last call whose
        // triangles are unchanged, and the pairs of the changed triangles (the same set as a full search: the
        // intersection test of two unchanged triangles sees the same positions, and their shared vertices are the
        // same, as canonical ids are equal exactly at equal positions)
        [MethodImpl(JitHints.Optimize)]
        List<Index2i> intersecting_pairs()
        {
            if (dirty.Count > 0)
                rebuild_tree();
            List<Index2i> pairs;
            if (lastPairs != null && pairsStale.Count == 0) {
                pairs = new List<Index2i>(lastPairs);
                UnchangedPairSearches++;
            } else if (lastPairs != null && pairsStale.Count <= treeTids.Count / 4) {
                pairs = intersecting_pairs_of_changed();
                IncrementalPairSearches++;
            } else {
                pairs = intersecting_pairs_all();
                FullPairSearches++;
            }
            if (check) {
                // (also the full search with the exact pairs skipped, against one that tests every pair)
                List<Index2i> full = intersecting_pairs_all(skipExact: false);
                bool same = full.Count == pairs.Count;
                for (int i = 0; same && i < full.Count; ++i)
                    same = full[i] == pairs[i];
                if (same == false)
                    throw new InvalidOperationException("MeshRoundingRepair: incremental or skipping intersecting pairs differ from a full search");
            }
            pairsStale.Clear();
            lastPairs = new List<Index2i>(pairs);
            return pairs;
        }

        // a triangle of the start with its vertices at their exact positions, unchanged since (its id may have been reused
        // by an edit for another triangle: then its vertices differ)
        bool exact_triangle(int tid)
        {
            if (exactAtStart == null || tid >= exactAtStart.Length || exactAtStart[tid].a < 0)
                return false;
            return Mesh.IsTriangle(tid) && Mesh.GetTriangle(tid) == exactAtStart[tid];
        }

        // the intersecting pairs of the whole mesh (ta < tb, sorted); with skipExact, the pairs of two exact triangles of
        // the start are not tested: they do not intersect beyond what they share (docs/design/ROUNDING_EXACT_PAIRS.md),
        // so the result is the same
        [MethodImpl(JitHints.Optimize)]
        List<Index2i> intersecting_pairs_all(bool skipExact = true)
        {
            int[] tris = treeTids.ToArray();
            bool skip = skipExact && exactAtStart != null;
            long skipped = 0;
            // (per triangle, in triangle order: deterministic without a lock)
            var found = new List<Index2i>[tris.Length];
            gParallel.ForRange(tris.Length, (k) => {
                int ta = tris[k];
                // (an exact triangle's pairs with the others are found from those)
                if (skip && exact_triangle(ta)) {
                    System.Threading.Interlocked.Increment(ref skipped);
                    return;
                }
                Index3i ia = Mesh.GetTriangle(ta);
                Triangle3d A = triangle(ia);
                if (ExactTriangleIntersection.IsDegenerate(ref A))
                    return;
                List<Index2i> local = null;
                tree.Query(box(ia), (i) => {
                    int tb = tris[i];
                    // (a pair of two triangles that are not skipped once, from the smaller id; one with a skipped
                    // triangle from the other)
                    if (tb == ta || (tb < ta && (skip == false || exact_triangle(tb) == false))) return;
                    Index3i ib = Mesh.GetTriangle(tb);
                    Triangle3d B = triangle(ib);
                    if (ExactTriangleIntersection.IsDegenerate(ref B)) return;
                    // (the smaller id first, as without the skip)
                    bool hit = ta < tb ? intersects(ia, ref A, ib, ref B) : intersects(ib, ref B, ia, ref A);
                    if (hit) {
                        if (local == null) local = new List<Index2i>();
                        local.Add(new Index2i(Math.Min(ta, tb), Math.Max(ta, tb)));
                    }
                });
                found[k] = local;
            });
            SkippedExactQueries += skipped;
            var pairs = new List<Index2i>();
            foreach (List<Index2i> local in found)
                if (local != null)
                    pairs.AddRange(local);
            pairs.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b.CompareTo(y.b));
            return pairs;
        }

        [MethodImpl(JitHints.Optimize)]
        List<Index2i> intersecting_pairs_of_changed()
        {
            var pairs = new List<Index2i>();
            foreach (Index2i p in lastPairs)
                if (pairsStale.Contains(p.a) == false && pairsStale.Contains(p.b) == false)
                    pairs.Add(p);
            var changedTris = new List<int>();
            foreach (int t in pairsStale)
                if (Mesh.IsTriangle(t))
                    changedTris.Add(t);
            changedTris.Sort();
            int[] tris = treeTids.ToArray();
            var found = new List<Index2i>[changedTris.Count];
            gParallel.ForRange(changedTris.Count, (k) => {
                int ta = changedTris[k];
                Index3i ia = Mesh.GetTriangle(ta);
                Triangle3d A = triangle(ia);
                if (ExactTriangleIntersection.IsDegenerate(ref A))
                    return;
                List<Index2i> local = null;
                tree.Query(box(ia), (i) => {
                    int tb = tris[i];
                    // (a pair of two changed triangles once, from the smaller one)
                    if (tb == ta || (tb < ta && pairsStale.Contains(tb))) return;
                    Index3i ib = Mesh.GetTriangle(tb);
                    Triangle3d B = triangle(ib);
                    if (ExactTriangleIntersection.IsDegenerate(ref B)) return;
                    // (the smaller id first, as in intersecting_pairs_all)
                    bool hit = ta < tb ? intersects(ia, ref A, ib, ref B) : intersects(ib, ref B, ia, ref A);
                    if (hit) {
                        if (local == null) local = new List<Index2i>();
                        local.Add(new Index2i(Math.Min(ta, tb), Math.Max(ta, tb)));
                    }
                });
                found[k] = local;
            });
            foreach (List<Index2i> local in found)
                if (local != null)
                    pairs.AddRange(local);
            pairs.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b.CompareTo(y.b));
            return pairs;
        }

        [MethodImpl(JitHints.Optimize)]
        List<int> degenerate_triangles()
        {
            var list = new List<int>();
            foreach (int tid in Mesh.TriangleIndices())
                if (is_bad(tid))
                    list.Add(tid);
            return list;
        }


        // ---- edits: proposed as triangles removed and triangles added (and one moved vertex) ----

        readonly List<int> removed = new List<int>();
        readonly List<Index3i> added = new List<Index3i>();
        readonly List<int> addedFrom = new List<int>();     // the removed triangle an added one replaces, or -1
        readonly List<int> scratch = new List<int>();

        void begin_edit()
        {
            removed.Clear(); added.Clear(); addedFrom.Clear();
            movedV = -1;
            vanishing = -1;
        }

        // intersecting pairs and degenerate triangles among the removed triangles and their neighbours, with
        // the current geometry, against the same for the added triangles with the proposed geometry: true if
        // neither count grows and their sum drops, and no added triangle turns over
        long cacheVersion = -1;
        int cacheOldI;
        readonly List<int> cacheRemoved = new List<int>();

        long evaluations, maxEvaluations, pairTests;
        // after the evaluation limit (no edit is applied any more, the mesh stays as it is): per vertex, the largest drift
        // among the vertices of its triangles (NaN: not computed yet), for try_move
        double[] frozenMost;

        // editDrift = most + step; false (and the smallest rejected drift updated) if beyond the bound
        bool within_bound(double most, double step)
        {
            editDrift = most + step;
            if (!(step >= 0) || !(editDrift <= maxLength)) {
                if (step >= 0 && editDrift < SmallestRejectedDrift)
                    SmallestRejectedDrift = editDrift;
                return false;
            }
            return true;
        }

        // (displacement: how far a vertex moves; surface, if hasSurface: a bound on how far the surface moves, for flips
        // and zips)
        [MethodImpl(JitHints.Optimize)]
        bool improves(double displacement = 0, double surface = 0, bool hasSurface = false, bool removesContact = false)
        {
            // the drift bound first (it does not count as an evaluation): the vertices of the edit get the largest
            // drift among them plus this edit's
            editVerts.Clear();
            double most = 0;
            foreach (int t in removed) {
                Index3i tv = Mesh.GetTriangle(t);
                for (int k = 0; k < 3; ++k) { editVerts.Add(tv[k]); most = Math.Max(most, drift[tv[k]]); }
            }
            foreach (Index3i tv in added)
                for (int k = 0; k < 3; ++k) { editVerts.Add(tv[k]); most = Math.Max(most, drift[tv[k]]); }
            // (a bound that is NaN, eg from distances that overflow, rejects the edit)
            double step = hasSurface ? surface : displacement;
            if (within_bound(most, step) == false)
                return false;
            if (++evaluations > maxEvaluations || pairTests > MaxPairTests) {
                EvaluationLimitReached = true;
                return false;
            }
            int saveV = movedV;
            Vector3d savePos = movedPos;
            // (SharedPositionsTouch: a vertex moved onto other vertices would touch them uncounted)
            List<int> there;
            if (atPosition != null && saveV >= 0 && savePos != Mesh.GetVertex(saveV) && atPosition.TryGetValue(key(savePos), out there))
                foreach (int u in there)
                    if (u != saveV && u != vanishing)
                        return false;
            // the cheap tests first: degenerate triangles and orientation, then the intersections
            movedV = -1;
            int oldI = 0, oldB = 0;
            var oldCross = new Vector3d[removed.Count];
            Vector3d crossSum = Vector3d.Zero;
            for (int i = 0; i < removed.Count; ++i) {
                Index3i t = Mesh.GetTriangle(removed[i]);
                Triangle3d T = triangle(t);
                oldCross[i] = (T.V1 - T.V0).Cross(T.V2 - T.V0);
                if (is_bad(t))
                    oldB++;
                // (slivers just above the degenerate area, and triangles not higher than twice the displacement
                // of the edit, may turn over: tiny folds, intersections are checked)
                double longest = Math.Max(T.V0.Distance(T.V1), Math.Max(T.V1.Distance(T.V2), T.V2.Distance(T.V0)));
                if (oldCross[i].Length < 2 * OrientedArea * areaEps || oldCross[i].Length <= 2 * displacement * longest)
                    oldCross[i] = Vector3d.Zero;
                else
                    crossSum += oldCross[i];
            }
            movedV = saveV; movedPos = savePos;
            int newI = 0, newB = 0;
            var tris = new Triangle3d[added.Count];
            var degen = new bool[added.Count];
            for (int i = 0; i < added.Count; ++i) {
                Index3i t = added[i];
                tris[i] = triangle(t);
                Vector3d cross = (tris[i].V1 - tris[i].V0).Cross(tris[i].V2 - tris[i].V0);
                // the replaced triangle's orientation (unless it is a sliver), for flips that of both
                Vector3d reference = (addedFrom[i] >= 0) ? oldCross[addedFrom[i]] : crossSum;
                if (cross.Dot(reference) <= 0 && reference != Vector3d.Zero)
                    return false;
                if (is_bad(t))
                    newB++;
                degen[i] = ExactTriangleIntersection.IsDegenerate(ref tris[i]);
                if (newB > oldB)
                    return false;
            }

            movedV = -1;
            var removedSet = new HashSet<int>(removed);
            // (the same triangles were evaluated for the last proposal, eg a move by another distance: the same count)
            bool cached = version == cacheVersion && removed.Count == cacheRemoved.Count;
            for (int i = 0; cached && i < removed.Count; ++i)
                cached = removed[i] == cacheRemoved[i];
            if (cached)
                oldI = cacheOldI;
            for (int i = 0; cached == false && i < removed.Count; ++i) {
                int r = removed[i];
                Index3i t = Mesh.GetTriangle(r);
                Triangle3d T = triangle(t);
                if (ExactTriangleIntersection.IsDegenerate(ref T))
                    continue;
                near(box(t), scratch);
                pairTests += scratch.Count;
                foreach (int x in scratch) {
                    if (x == r || (removedSet.Contains(x) && x < r))
                        continue;
                    Index3i u = Mesh.GetTriangle(x);
                    Triangle3d U = triangle(u);
                    if (ExactTriangleIntersection.IsDegenerate(ref U) == false && intersects(t, ref T, u, ref U))
                        oldI++;
                }
            }
            cacheVersion = version; cacheOldI = oldI;
            cacheRemoved.Clear(); cacheRemoved.AddRange(removed);
            movedV = saveV; movedPos = savePos;
            // (with as many degenerate triangles as before, it must remove an intersection)
            if (newB == oldB && oldI == 0 && removesContact == false)
                return false;
            for (int i = 0; i < added.Count; ++i) {
                if (degen[i])
                    continue;
                Index3i t = added[i];
                near(box(t), scratch);
                pairTests += scratch.Count;
                foreach (int x in scratch) {
                    if (removedSet.Contains(x))
                        continue;
                    Index3i u = Mesh.GetTriangle(x);
                    // (the moved vertex's triangles are all removed: u has the current geometry)
                    Triangle3d U = triangle(u);
                    if (ExactTriangleIntersection.IsDegenerate(ref U) == false && intersects(t, ref tris[i], u, ref U))
                        if (++newI > oldI) return false;
                }
                for (int j = i + 1; j < added.Count; ++j)
                    if (degen[j] == false && intersects(t, ref tris[i], added[j], ref tris[j]))
                        if (++newI > oldI) return false;
            }
            // (an edit that removes a contact may keep the counts)
            return newI + newB < oldI + oldB || (removesContact && newI + newB <= oldI + oldB);
        }

        // the proposed edit (improves) was applied: its vertices get its drift
        void commit_drift()
        {
            foreach (int v in editVerts)
                drift[v] = editDrift;
            MaxDrift = Math.Max(MaxDrift, editDrift);
        }

        // are a, b, c, d in one plane with the segments a-b and c-d crossing properly (exact): then (a, b, c), (b, a, d)
        // and (c, d, b), (d, c, a) cover the same convex quad
        static bool flat_convex(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            if (ExactPredicates.Orient3d(a, b, c, d) != 0)
                return false;
            for (int k = 0; k < 3; ++k) {
                Vector2d a2 = drop(a, k), b2 = drop(b, k), c2 = drop(c, k), d2 = drop(d, k);
                int o1 = ExactPredicates.Orient2d(a2, b2, c2), o2 = ExactPredicates.Orient2d(a2, b2, d2);
                int o3 = ExactPredicates.Orient2d(c2, d2, a2), o4 = ExactPredicates.Orient2d(c2, d2, b2);
                if (o1 == 0 && o2 == 0 && o3 == 0 && o4 == 0)
                    continue;       // (this projection maps the plane to a line)
                return o1 * o2 < 0 && o3 * o4 < 0;
            }
            return false;
        }

        static Vector2d drop(Vector3d v, int axis)
        {
            return (axis == 0) ? new Vector2d(v.y, v.z) : (axis == 1) ? new Vector2d(v.z, v.x) : new Vector2d(v.x, v.y);
        }

        // distance of the segments p0-p1 and q0-q1 (Ericson, Real-Time Collision Detection, 5.1.9; either may have zero
        // length)
        static double segments_distance(Vector3d p0, Vector3d p1, Vector3d q0, Vector3d q1)
        {
            Vector3d d1 = p1 - p0, d2 = q1 - q0, r = p0 - q0;
            double a = d1.LengthSquared, e = d2.LengthSquared, f = d2.Dot(r);
            double s, t;
            if (a <= 0 && e <= 0) {
                s = t = 0;
            } else if (a <= 0) {
                s = 0;
                t = MathUtil.Clamp(f / e, 0, 1);
            } else {
                double c = d1.Dot(r);
                if (e <= 0) {
                    t = 0;
                    s = MathUtil.Clamp(-c / a, 0, 1);
                } else {
                    double b = d1.Dot(d2), denom = a * e - b * b;
                    s = (denom > 0) ? MathUtil.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) {
                        t = 0;
                        s = MathUtil.Clamp(-c / a, 0, 1);
                    } else if (t > 1) {
                        t = 1;
                        s = MathUtil.Clamp((b - c) / a, 0, 1);
                    }
                }
            }
            // (any pair of points on the segments bounds the distance from above; these are the nearest up to rounding)
            return (p0 + s * d1).Distance(q0 + t * d2);
        }

        // distance of p to the segment a-b
        static double segment_distance(Vector3d p, Vector3d a, Vector3d b)
        {
            Vector3d ab = b - a;
            double l2 = ab.LengthSquared;
            double t = (l2 > 0) ? MathUtil.Clamp((p - a).Dot(ab) / l2, 0, 1) : 0;
            return p.Distance(a + t * ab);
        }

        // an upper bound of the distance of p to the triangle (a, b, c): to its nearest edge
        static double triangle_distance_bound(Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            return Math.Min(segment_distance(p, a, b), Math.Min(segment_distance(p, b, c), segment_distance(p, c, a)));
        }

        // (where the coordinates are coarse only with FixedWhereCoarse)
        bool is_fixed(int v)
        {
            return (coarse == false || FixedWhereCoarse) && FixedVertexF != null && FixedVertexF(v);
        }

        // contracting edge eid = (a, b), b kept at p, keeps the boundary curve (see class comment): a boundary
        // vertex keeps its coordinates and vanishes only along a boundary edge, no edge between two boundary
        // vertices across the interior, no bowties
        bool collapse_keeps_boundary(int a, int b, int eid, Vector3d p)
        {
            if (Mesh.IsBowtieVertex(a) || Mesh.IsBowtieVertex(b))
                return false;
            if (Mesh.IsBoundaryVertex(b) && p != Mesh.GetVertex(b))
                return false;
            return Mesh.IsBoundaryEdge(eid) || Mesh.IsBoundaryVertex(a) == false;
        }

        // contract edge (a, b): b is kept, at position p
        [MethodImpl(JitHints.Optimize)]
        bool try_collapse(int a, int b, Vector3d p)
        {
            int eid = Mesh.FindEdge(a, b);
            if (eid < 0 || collapse_keeps_boundary(a, b, eid, p) == false)
                return false;
            begin_edit();
            foreach (int t in Mesh.VtxTrianglesItr(a)) {
                removed.Add(t);
                Index3i tv = Mesh.GetTriangle(t);
                if (tv.a == b || tv.b == b || tv.c == b)
                    continue;
                added.Add(new Index3i(tv.a == a ? b : tv.a, tv.b == a ? b : tv.b, tv.c == a ? b : tv.c));
                addedFrom.Add(removed.Count - 1);
            }
            if (p != Mesh.GetVertex(b)) {
                foreach (int t in Mesh.VtxTrianglesItr(b)) {
                    Index3i tv = Mesh.GetTriangle(t);
                    if (tv.a == a || tv.b == a || tv.c == a)
                        continue;
                    removed.Add(t);
                    added.Add(tv);
                    addedFrom.Add(removed.Count - 1);
                }
            }
            movedV = b; movedPos = p; vanishing = a;
            bool ok = improves(Math.Max(p.Distance(Mesh.GetVertex(a)), p.Distance(Mesh.GetVertex(b))));
            movedV = -1;
            if (ok == false)
                return false;
            Vector3d pa = Mesh.GetVertex(a);
            DMesh3.EdgeCollapseInfo info;
            // (the triangles of a: removed or moved to b)
            foreach (int t in Mesh.VtxTrianglesItr(a))
                pairsStale.Add(t);
            if (Mesh.CollapseEdge(b, a, out info) != MeshResult.Ok)
                return false;
            if (atPosition != null)
                position_remove(a, pa);
            set_vertex(b, p);
            foreach (int t in Mesh.VtxTrianglesItr(b))
                mark_dirty(t);
            touched.Add(b);
            commit_drift();
            Collapses++;
            return true;
        }

        bool try_flip(int eid)
        {
            if (Mesh.IsEdge(eid) == false || Mesh.IsBoundaryEdge(eid))
                return false;
            // as DMesh3.FlipEdge: edge a -> b in t0 = (a, b, c), t1 has d; afterwards t0 = (c, d, b), t1 = (d, c, a)
            Index2i ev = Mesh.GetEdgeV(eid), et = Mesh.GetEdgeT(eid);
            int a = ev.a, b = ev.b;
            int[] t0v = Mesh.GetTriangle(et.a).array, t1v = Mesh.GetTriangle(et.b).array;
            int c = IndexUtil.orient_tri_edge_and_find_other_vtx(ref a, ref b, t0v);
            int d = IndexUtil.find_tri_other_vtx(a, b, t1v);
            if (c < 0 || d < 0 || Mesh.FindEdge(c, d) >= 0)
                return false;
            begin_edit();
            removed.Add(et.a); removed.Add(et.b);
            added.Add(new Index3i(c, d, b)); addedFrom.Add(-1);
            added.Add(new Index3i(d, c, a)); addedFrom.Add(-1);
            // how far the surface moves, the smallest of three bounds. (1) Let p on a-b and q on c-d be their nearest
            // points; with c and d moved by p - q, the segment c-d meets a-b at p (on both closed segments), so the
            // four points lie in one plane and both pairs of triangles cover the same convex quad (or triangle): twice
            // the distance of the segments. (2) A point of (c, d, b) or (d, c, a) with c replaced by its nearest point
            // in (a, b, d) lies in it: the new triangles are within the distance of c to (a, b, d) of the old ones; the
            // old ones within the distance of a to (c, d, b) or of b to (d, c, a) of the new ones, by the same
            // argument. (3) A convex quad in one plane (exact: coplanar, the diagonals cross properly): the same set, 0
            Vector3d pa = pos(a), pb = pos(b), pc = pos(c), pd = pos(d);
            double crossing = 2 * segments_distance(pa, pb, pc, pd);
            double general = Math.Max(Math.Min(triangle_distance_bound(pc, pa, pb, pd), triangle_distance_bound(pd, pa, pb, pc)),
                Math.Min(triangle_distance_bound(pa, pc, pd, pb), triangle_distance_bound(pb, pd, pc, pa)));
            double surface = Math.Min(crossing, general);
            if (surface > 0 && flat_convex(pa, pb, pc, pd))
                surface = 0;
            if (improves(0, surface, true) == false)
                return false;
            DMesh3.EdgeFlipInfo info;
            if (Mesh.FlipEdge(eid, out info) != MeshResult.Ok)
                return false;
            mark_dirty(et.a); mark_dirty(et.b);
            touched.Add(a); touched.Add(b);
            commit_drift();
            Flips++;
            return true;
        }

        // the triangle across edge (u, v) from t, or -1
        int across(int t, int u, int v)
        {
            int eid = Mesh.FindEdge(u, v);
            if (eid < 0) return -1;
            Index2i et = Mesh.GetEdgeT(eid);
            return (et.a == t) ? et.b : (et.b == t) ? et.a : -1;
        }

        // two caps on both sides of edge (a, b), T = (a, b, c) and U = (b, a, d), whose apexes lie between a and b
        // (a flip gives two caps again): T and U removed, and the edges of their neighbours that run along
        // the segment split at the other apex, so that the two sides are zipped together. With the order a, c,
        // d, b along the edge: N1 = (c, b, x) across (b, c) becomes (c, d, x), (d, b, x); N2 = (d, a, y) across
        // (a, d) becomes (d, c, y), (c, a, y).
        [MethodImpl(JitHints.Optimize)]
        bool try_zip(int eid)
        {
            if (Mesh.IsEdge(eid) == false || Mesh.IsBoundaryEdge(eid))
                return false;
            Index2i ev = Mesh.GetEdgeV(eid), et = Mesh.GetEdgeT(eid);
            int a = ev.a, b = ev.b;
            int tT = et.a, tU = et.b;
            int c = IndexUtil.orient_tri_edge_and_find_other_vtx(ref a, ref b, Mesh.GetTriangle(tT).array);
            int d = IndexUtil.find_tri_other_vtx(a, b, Mesh.GetTriangle(tU).array);
            if (c < 0 || d < 0 || c == d || Mesh.FindEdge(c, d) >= 0)
                return false;
            Vector3d pa = pos(a), ab = pos(b) - pa;
            double sc = (pos(c) - pa).Dot(ab) / ab.LengthSquared, sd = (pos(d) - pa).Dot(ab) / ab.LengthSquared;
            if (!(sc > 0 && sc < 1 && sd > 0 && sd < 1) || sc == sd)
                return false;
            int n1, n2, x, y;
            begin_edit();
            removed.Add(tT); removed.Add(tU);
            if (sc < sd) {
                n1 = across(tT, b, c); n2 = across(tU, a, d);
                if (n1 < 0 || n2 < 0) return false;
                x = IndexUtil.find_tri_other_vtx(b, c, Mesh.GetTriangle(n1).array);
                y = IndexUtil.find_tri_other_vtx(a, d, Mesh.GetTriangle(n2).array);
                added.Add(new Index3i(c, d, x)); added.Add(new Index3i(d, b, x));
                added.Add(new Index3i(d, c, y)); added.Add(new Index3i(c, a, y));
            } else {
                // order a, d, c, b: N1 = (a, c, x) across (c, a) becomes (a, d, x), (d, c, x); N2 = (b, d, y) across
                // (d, b) becomes (b, c, y), (c, d, y)
                n1 = across(tT, c, a); n2 = across(tU, d, b);
                if (n1 < 0 || n2 < 0) return false;
                x = IndexUtil.find_tri_other_vtx(c, a, Mesh.GetTriangle(n1).array);
                y = IndexUtil.find_tri_other_vtx(d, b, Mesh.GetTriangle(n2).array);
                added.Add(new Index3i(a, d, x)); added.Add(new Index3i(d, c, x));
                added.Add(new Index3i(b, c, y)); added.Add(new Index3i(c, d, y));
            }
            if (n1 == n2 || n1 == tU || n2 == tT || x < 0 || y < 0 || x == d || y == c || x == y)
                return false;
            removed.Add(n1); removed.Add(n2);
            for (int k = 0; k < 4; ++k)
                addedFrom.Add(k < 2 ? 2 : 3);
            // (the new edges must not exist yet: (d, x), (c, y))
            if (Mesh.FindEdge(d, x) >= 0 || Mesh.FindEdge(c, y) >= 0)
                return false;
            // how far the surface moves: with c and d moved onto the segment a-b, the old and the new triangles cover
            // the same set, so at most twice the larger distance of c, d to the segment
            double surface = 2 * Math.Max(segment_distance(pos(c), pa, pos(b)), segment_distance(pos(d), pa, pos(b)));
            if (improves(0, surface, true) == false)
                return false;
            var old = new List<Index3i>();
            foreach (int t in removed) {
                old.Add(Mesh.GetTriangle(t));
                remove_triangle(t);
            }
            var tids = new List<int>();
            foreach (Index3i t in added) {
                int tid = Mesh.AppendTriangle(t.a, t.b, t.c);
                if (tid < 0) {
                    // (not expected: the checks above keep the mesh manifold) undone
                    foreach (int u in tids)
                        remove_triangle(u);
                    foreach (Index3i u in old)
                        mark_dirty(Mesh.AppendTriangle(u.a, u.b, u.c));
                    return false;
                }
                tids.Add(tid);
                mark_dirty(tid);
            }
            touched.Add(c); touched.Add(d);
            commit_drift();
            Zips++;
            return true;
        }

        // a strip: the degenerate triangles and slivers (below OrientedArea times the degenerate area) connected to t0
        // across edges (at most MaxStripTriangles; or a part of them, see below), a disk whose
        // boundary is one simple loop, all of its vertices between the two loop vertices farthest apart (e0, e1) along
        // the segment between them, and the loop strictly monotone along it on both sides (the two chains from e0 to
        // e1). A flip gives slivers again, as for the zip of two caps, which this generalizes: the strip is removed and
        // the triangles outside it along both chains are split at the vertices of the other chain, so that both sides
        // share one chain, the vertices of both in their order along the segment; the strip's inner vertices are
        // removed. Each outer triangle (r, p, x) across the loop edge (p, r) becomes (s1, p, x), (s2, s1, x), ...,
        // (r, sm, x), with s1..sm the merged chain's vertices between p and r.
        [MethodImpl(JitHints.Optimize)]
        bool try_strip(int t0)
        {
            // first along the line of t0's longest edge (its vertices within maxLength / 2 of it: a strip that branches
            // is no disk with a monotone boundary, its branches are), then in all directions
            Index3i tv = Mesh.GetTriangle(t0);
            int kl = 0;
            for (int k = 1; k < 3; ++k)
                if (pos(tv[k]).DistanceSquared(pos(tv[(k + 1) % 3])) > pos(tv[kl]).DistanceSquared(pos(tv[(kl + 1) % 3])))
                    kl = k;
            Vector3d l0 = pos(tv[kl]), l1 = pos(tv[(kl + 1) % 3]);
            // (every proposal counts, also those that fail before they are evaluated: at most MaxStripProposals per
            // degenerate triangle, so the cost stays linear in MaxStripTriangles)
            int tried = 0;
            if (EvaluationLimitReached)
                return false;
            foreach (bool alongLine in new[] { true, false }) {
                List<int> strip = strip_around(t0, alongLine ? l0 : Vector3d.Zero, alongLine ? l1 : Vector3d.Zero, alongLine);
                if (strip == null)
                    return false;
                // the largest part of it (in breadth first order) that can be zipped
                for (int m = strip.Count; m >= 2 && tried < MaxStripProposals; --m) {
                    tried++;
                    if (zip_strip(strip.GetRange(0, m), out bool undone))
                        return true;
                    // (an undone edit gives the restored triangles new ids: the strip's ids are stale)
                    if (undone || EvaluationLimitReached)
                        return false;
                }
            }
            return false;
        }

        // the degenerate triangles and slivers connected to t0 across edges, breadth first (at most MaxStripTriangles;
        // alongLine: only those with their vertices within maxLength / 2 of the line through l0 and l1); null if one is
        // at the boundary
        [MethodImpl(JitHints.Optimize)]
        List<int> strip_around(int t0, Vector3d l0, Vector3d l1, bool alongLine)
        {
            // (the direction not normalized: Normalized has an absolute threshold, which would depend on the scale)
            Vector3d dir = l1 - l0;
            double len2 = dir.LengthSquared;
            Func<int, bool> near = (v) => {
                Vector3d d = pos(v) - l0;
                return len2 > 0 && (d - (d.Dot(dir) / len2) * dir).Length <= 0.5 * maxLength;
            };
            var strip = new List<int> { t0 };
            var inStrip = new HashSet<int> { t0 };
            for (int i = 0; i < strip.Count && strip.Count < MaxStripTriangles; ++i) {
                Index3i te = Mesh.GetTriEdges(strip[i]);
                for (int k = 0; k < 3 && strip.Count < MaxStripTriangles; ++k) {
                    Index2i et = Mesh.GetEdgeT(te[k]);
                    if (et.b == DMesh3.InvalidID)
                        return null;
                    int nb = (et.a == strip[i]) ? et.b : et.a;
                    // (slivers just above the degenerate area too: a flip of a cap onto one gives a degenerate triangle)
                    if (inStrip.Contains(nb) || (area(nb) >= OrientedArea * areaEps && is_bad(nb) == false))
                        continue;
                    if (alongLine) {
                        Index3i nv = Mesh.GetTriangle(nb);
                        if (near(nv.a) == false || near(nv.b) == false || near(nv.c) == false)
                            continue;
                    }
                    strip.Add(nb); inStrip.Add(nb);
                }
            }
            return strip;
        }

        // zip the strip (see try_strip) if it is a disk with a monotone boundary and the edit improves
        [MethodImpl(JitHints.Optimize)]
        bool zip_strip(List<int> strip, out bool undone)
        {
            undone = false;
            var inStrip = new HashSet<int>(strip);
            // its boundary: the directed edges of the strip's triangles whose other side is outside it
            var next = new Dictionary<int, int>();
            var outer = new Dictionary<int, int>();     // the triangle across the loop edge from v to next[v]
            var verts = new HashSet<int>();
            int innerSides = 0;
            foreach (int t in strip) {
                Index3i tv = Mesh.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    int u = tv[k], v = tv[(k + 1) % 3];
                    verts.Add(u);
                    int nb = across(t, u, v);
                    if (nb < 0)
                        return false;
                    if (inStrip.Contains(nb)) {
                        innerSides++;
                        continue;
                    }
                    if (next.ContainsKey(u))
                        return false;   // (the boundary passes u twice)
                    next[u] = v;
                    outer[u] = nb;
                }
            }
            // a disk: one loop through all boundary vertices, and Euler characteristic 1
            int n = next.Count, start = int.MaxValue, steps = 0;
            foreach (int v in next.Keys)
                start = Math.Min(start, v);
            for (int v = start; ; ) {
                int nv;
                if (next.TryGetValue(v, out nv) == false || ++steps > n)
                    return false;
                v = nv;
                if (v == start)
                    break;
            }
            if (steps != n || verts.Count - (innerSides / 2 + n) + strip.Count != 1)
                return false;
            var loop = new List<int>(n);
            for (int v = start; loop.Count < n; v = next[v])
                loop.Add(v);
            // the ends: the loop vertices farthest apart
            int i0 = 0, i1 = 0;
            double far = -1;
            for (int i = 0; i < n; ++i)
                for (int j = i + 1; j < n; ++j) {
                    double dd = pos(loop[i]).DistanceSquared(pos(loop[j]));
                    if (dd > far) { far = dd; i0 = i; i1 = j; }
                }
            Vector3d e0 = pos(loop[i0]), dir = pos(loop[i1]) - e0;
            double len2 = dir.LengthSquared;
            if (!(len2 > 0))
                return false;
            var param = new Dictionary<int, double>();
            // every vertex between the ends along the segment; w: the largest distance to the line through them
            double w = 0;
            // (relative to e0, which is exact for nearby points: e0 + s dir would carry the rounding error of the
            // coordinates' size into w)
            foreach (int v in verts) {
                Vector3d dv = pos(v) - e0;
                double s = dv.Dot(dir) / len2;
                if (!(s >= 0 && s <= 1))
                    return false;
                param[v] = s;
                w = Math.Max(w, (dv - s * dir).Length);
            }
            // both chains strictly monotone: from e0 to e1 along the loop, and against it
            var chainA = new List<int>();
            for (int i = i0; ; i = (i + 1) % n) {
                chainA.Add(loop[i]);
                if (i == i1) break;
            }
            var chainB = new List<int>();
            for (int i = i0; ; i = (i + n - 1) % n) {
                chainB.Add(loop[i]);
                if (i == i1) break;
            }
            for (int i = 1; i < chainA.Count; ++i)
                if (!(param[chainA[i]] > param[chainA[i - 1]]))
                    return false;
            for (int i = 1; i < chainB.Count; ++i)
                if (!(param[chainB[i]] > param[chainB[i - 1]]))
                    return false;
            // the merged chain, in the order along the segment (ties by id, so that it is deterministic)
            var merged = new List<int>(chainA);
            merged.AddRange(chainB.GetRange(1, chainB.Count - 2));
            merged.Sort((x, y) => { int c = param[x].CompareTo(param[y]); return c != 0 ? c : x.CompareTo(y); });
            var rank = new Dictionary<int, int>();
            for (int i = 0; i < merged.Count; ++i)
                rank[merged[i]] = i;
            // the edit: the strip and the outer triangles removed, the outer triangles split along the merged chain
            begin_edit();
            removed.AddRange(strip);
            var outerSet = new HashSet<int>();
            foreach (int p in loop) {
                int r = next[p], o = outer[p];
                int x = IndexUtil.find_tri_other_vtx(p, r, Mesh.GetTriangle(o).array);
                if (x < 0 || verts.Contains(x) || outerSet.Add(o) == false)
                    return false;
                removed.Add(o);
                int from = removed.Count - 1;
                int ip = rank[p], ir = rank[r], step = (ir > ip) ? 1 : -1;
                for (int i = ip; i != ir; i += step) {
                    added.Add(new Index3i(merged[i + step], merged[i], x));
                    addedFrom.Add(from);
                }
            }
            // manifold afterwards: every directed edge of the added triangles once, on an edge that no kept triangle
            // runs through in the same direction and at most one kept triangle uses
            var removedSet = new HashSet<int>(removed);
            var directed = new HashSet<Index2i>();
            foreach (Index3i t in added)
                for (int k = 0; k < 3; ++k) {
                    int u = t[k], v = t[(k + 1) % 3];
                    if (directed.Add(new Index2i(u, v)) == false)
                        return false;
                    int eid = Mesh.FindEdge(u, v);
                    if (eid < 0)
                        continue;
                    Index2i et = Mesh.GetEdgeT(eid);
                    int kept = 0;
                    foreach (int kt in new[] { et.a, et.b }) {
                        if (kt == DMesh3.InvalidID || removedSet.Contains(kt))
                            continue;
                        kept++;
                        if (runs(Mesh.GetTriangle(kt), u, v))
                            return false;
                    }
                    if (kept > 1)
                        return false;
                }
            // how far the surface moves: with every vertex of the strip moved onto the line through e0 and e1 (by at
            // most w, in their order along it), the strip is a segment covered by the merged chain, and each outer
            // triangle and its split cover the same set; so do the old and the new triangles: at most 2 w
            if (improves(0, 2 * w, true) == false)
                return false;
            // (the split triangles keep the group of the outer triangle they replace)
            var old = new List<Index3i>();
            var groups = new List<int>();
            foreach (int t in removed) {
                old.Add(Mesh.GetTriangle(t));
                groups.Add(Mesh.HasTriangleGroups ? Mesh.GetTriangleGroup(t) : -1);
                remove_triangle(t);
            }
            var tids = new List<int>();
            for (int i = 0; i < added.Count; ++i) {
                Index3i t = added[i];
                int tid = Mesh.AppendTriangle(t.a, t.b, t.c, groups[addedFrom[i]]);
                if (tid < 0) {
                    // (not expected: the checks above keep the mesh manifold) undone
                    foreach (int u in tids)
                        remove_triangle(u);
                    for (int j = 0; j < old.Count; ++j) {
                        int back = Mesh.AppendTriangle(old[j].a, old[j].b, old[j].c, groups[j]);
                        if (back >= 0)
                            mark_dirty(back);
                    }
                    undone = true;
                    return false;
                }
                tids.Add(tid);
                mark_dirty(tid);
            }
            // the strip's inner vertices are left without triangles
            foreach (int v in verts)
                if (rank.ContainsKey(v) == false && Mesh.IsVertex(v) && Mesh.GetVtxEdgeCount(v) == 0) {
                    if (atPosition != null)
                        position_remove(v, Mesh.GetVertex(v));
                    Mesh.RemoveVertex(v, false, false);
                }
            foreach (int v in verts)
                touched.Add(v);
            commit_drift();
            Zips++;
            StripZips++;
            return true;
        }

        // v / |v|, or 0 if v is 0 or not finite, computed as Vector3d.Normalize does (times 1 / |v|, so the bits are the
        // same where that one is above its threshold) but without the threshold: Normalized returns 0 below the absolute
        // MathUtil.Epsilon, which made the edits depend on the scale. Components below 1e-150 or above 1e150, whose
        // squares would underflow or overflow, are first scaled by an exact power of two. So 2^k v gives the same unit
        // vector as v, bit for bit
        internal static Vector3d unit(Vector3d v)
        {
            double m = Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z)));
            if (!(m > 0 && m < double.PositiveInfinity))
                return Vector3d.Zero;
            if (m < 1e-150)
                v *= 4.149515568880993E+180;        // 2^600
            else if (m > 1e150)
                v *= 2.409919865102884E-181;        // 2^-600
            double l = v.Length;
            double inv = 1.0 / l;
            return new Vector3d(v.x * inv, v.y * inv, v.z * inv);
        }

        // the unit normal of a triangle as DMesh3.GetTriNormal computes it (MathUtil.Normal: the unit edges a-b and b-c,
        // their cross product made unit), with unit() instead of Normalize: the same bits where both are longer than
        // MathUtil.Epsilon, and no threshold below it
        Vector3d tri_normal(int tid)
        {
            Index3i t = Mesh.GetTriangle(tid);
            Vector3d a = Mesh.GetVertex(t.a), b = Mesh.GetVertex(t.b), c = Mesh.GetVertex(t.c);
            return unit(unit(b - a).Cross(unit(c - b)));
        }

        // the unit in the last place of x >= 0 (a power of two; the smallest subnormal for 0 and subnormals; NaN if not
        // finite)
        static double ulp(double x)
        {
            if (!(x < double.PositiveInfinity))
                return double.NaN;
            long bits = BitConverter.DoubleToInt64Bits(x);
            int e = (int)((bits >> 52) & 0x7FF);
            // (2^(e - 1075): a normal number for e > 52, else the subnormal with that one bit)
            return e > 52 ? BitConverter.Int64BitsToDouble((long)(e - 52) << 52)
                : e > 0 ? BitConverter.Int64BitsToDouble(1L << (e - 1)) : double.Epsilon;
        }

        // the contact pass (see Apply): per group of vertices at one position (in the order of their smallest id), the
        // first vertex of valence 3 whose removal improves; repeated while it removes some (at most once per vertex)
        [MethodImpl(JitHints.Optimize)]
        int remove_contact_vertices()
        {
            int removedNow = 0;
            for (bool again = true; again; ) {
                again = false;
                var groups = new List<List<int>>();
                foreach (List<int> list in atPosition.Values)
                    if (list.Count > 1) {
                        var g = new List<int>(list);
                        g.Sort();
                        groups.Add(g);
                    }
                groups.Sort((x, y) => x[0].CompareTo(y[0]));
                foreach (List<int> g in groups)
                    foreach (int u in g)
                        if (Mesh.IsVertex(u) && try_remove_vertex(u)) {
                            removedNow++;
                            again = true;
                            break;
                        }
            }
            return removedNow;
        }

        // remove u, a vertex of valence 3 with the triangles (u, a, b), (u, b, c), (u, c, a), by the triangle (a, b, c).
        // How far the surface moves: with u moved to its nearest point q in (a, b, c), the three triangles tile (a, b, c)
        // (q in the closed triangle), so the old and the new triangles are within dist(u, (a, b, c)) of each other
        [MethodImpl(JitHints.Optimize)]
        bool try_remove_vertex(int u)
        {
            if (Mesh.GetVtxEdgeCount(u) != 3 || Mesh.IsBoundaryVertex(u) || EvaluationLimitReached)
                return false;
            int[] fan = new int[3];
            int nf = 0;
            foreach (int t in Mesh.VtxTrianglesItr(u)) {
                if (nf == 3) return false;
                fan[nf++] = t;
            }
            if (nf != 3)
                return false;
            // the ring a, b, c in the orientation of the fan
            Index3i t0 = Mesh.GetTriangle(fan[0]);
            int k0 = t0.a == u ? 0 : t0.b == u ? 1 : 2;
            int a = t0[(k0 + 1) % 3], b = t0[(k0 + 2) % 3], c = -1;
            foreach (int t in fan) {
                Index3i tv = Mesh.GetTriangle(t);
                for (int k = 0; k < 3; ++k)
                    if (tv[k] != u && tv[k] != a && tv[k] != b)
                        c = tv[k];
            }
            if (c < 0 || runs3(fan, u, b, c) == false || runs3(fan, u, c, a) == false
                || Mesh.FindTriangle(a, b, c) != DMesh3.InvalidID)
                return false;
            begin_edit();
            removed.Add(fan[0]); removed.Add(fan[1]); removed.Add(fan[2]);
            added.Add(new Index3i(a, b, c)); addedFrom.Add(-1);
            var tri = new Triangle3d(pos(a), pos(b), pos(c));
            // (the distance to the nearest point, not DistPoint3Triangle3.Get: its closed form subtracts terms of the
            // triangle's squared size and loses the distance below about 1e-8 of that size)
            var dist = new DistPoint3Triangle3(pos(u), tri);
            dist.GetSquared();
            double surface = Math.Max(pos(u).Distance(dist.TriangleClosest), dist.Get());
            if (improves(0, surface, true, true) == false)
                return false;
            int gid = Mesh.HasTriangleGroups ? Mesh.GetTriangleGroup(fan[0]) : -1;
            var old = new Index3i[3];
            var oldGid = new int[3];
            for (int i = 0; i < 3; ++i) {
                old[i] = Mesh.GetTriangle(fan[i]);
                oldGid[i] = Mesh.HasTriangleGroups ? Mesh.GetTriangleGroup(fan[i]) : -1;
                remove_triangle(fan[i]);
            }
            int tid = Mesh.AppendTriangle(a, b, c, gid);
            if (tid < 0) {
                // (not expected: (a, b, c) does not exist and each of its edges loses a fan triangle) undone
                for (int i = 0; i < 3; ++i) {
                    int back = Mesh.AppendTriangle(old[i].a, old[i].b, old[i].c, oldGid[i]);
                    if (back >= 0)
                        mark_dirty(back);
                }
                return false;
            }
            mark_dirty(tid);
            position_remove(u, Mesh.GetVertex(u));
            Mesh.RemoveVertex(u, false, false);
            touched.Add(a); touched.Add(b); touched.Add(c);
            commit_drift();
            VertexRemovals++;
            return true;
        }

        // does one of the triangles run through u, v, w (in this cyclic order)
        bool runs3(int[] tris, int u, int v, int w)
        {
            foreach (int t in tris) {
                Index3i tv = Mesh.GetTriangle(t);
                if (runs(tv, u, v) && runs(tv, v, w))
                    return true;
            }
            return false;
        }

        // does triangle t run through u then v
        static bool runs(Index3i t, int u, int v)
        {
            return (t.a == u && t.b == v) || (t.b == u && t.c == v) || (t.c == u && t.a == v);
        }

        // move the boundary vertex v to s from its input position along one of its two boundary edges (not to or
        // past their other ends). So that it stays on the input curve over several edits: only along an edge of the
        // input boundary whose other end u is where it was, and towards the same u as before if v was slid already
        [MethodImpl(JitHints.Optimize)]
        bool try_slide(int v, double s)
        {
            Vector3d p;
            if (Mesh.IsBowtieVertex(v) || inputBoundary.TryGetValue(v, out p) == false)
                return false;
            int before;
            bool slid = slidTowards.TryGetValue(v, out before);
            foreach (int eid in Mesh.VtxEdgesItr(v)) {
                if (Mesh.IsBoundaryEdge(eid) == false)
                    continue;
                Index2i ev = Mesh.GetEdgeV(eid);
                int u = (ev.a == v) ? ev.b : ev.a;
                Vector3d pu;
                if ((slid && u != before) || inputBoundaryEdges.Contains(new Index2i(Math.Min(v, u), Math.Max(v, u))) == false
                    || inputBoundary.TryGetValue(u, out pu) == false || Mesh.GetVertex(u) != pu)
                    continue;
                Vector3d d = pu - p;
                double len = d.Length;
                if (s < len && try_move(v, p + (s / len) * d)) {
                    slidTowards[v] = u;
                    return true;
                }
            }
            return false;
        }

        // move v to p (a boundary vertex only by try_slide)
        [MethodImpl(JitHints.Optimize)]
        bool try_move(int v, Vector3d p)
        {
            if (p == Mesh.GetVertex(v) || double.IsNaN(p.x + p.y + p.z))
                return false;
            if (EvaluationLimitReached && check == false) {
                // (past the limit improves rejects every move: only the smallest rejected drift is left, the same as
                // improves finds; the drift of v's triangles' vertices does not change any more)
                if (frozenMost == null) {
                    frozenMost = new double[Mesh.MaxVertexID];
                    for (int i = 0; i < frozenMost.Length; ++i)
                        frozenMost[i] = double.NaN;
                }
                double most = frozenMost[v];
                if (double.IsNaN(most)) {
                    most = 0;
                    foreach (int t in Mesh.VtxTrianglesItr(v)) {
                        Index3i tv = Mesh.GetTriangle(t);
                        for (int k = 0; k < 3; ++k)
                            most = Math.Max(most, drift[tv[k]]);
                    }
                    frozenMost[v] = most;
                }
                MovesPastTheLimit++;
                if (SkipMovesPastTheLimit == false)
                    within_bound(most, p.Distance(Mesh.GetVertex(v)));
                return false;
            }
            begin_edit();
            foreach (int t in Mesh.VtxTrianglesItr(v)) {
                removed.Add(t);
                added.Add(Mesh.GetTriangle(t));
                addedFrom.Add(removed.Count - 1);
            }
            movedV = v; movedPos = p;
            bool ok = improves(p.Distance(Mesh.GetVertex(v)));
            movedV = -1;
            if (ok == false)
                return false;
            set_vertex(v, p);
            foreach (int t in Mesh.VtxTrianglesItr(v))
                mark_dirty(t);
            touched.Add(v);
            commit_drift();
            Moves++;
            return true;
        }

        void set_vertex(int v, Vector3d p)
        {
            if (atPosition != null) {
                position_remove(v, Mesh.GetVertex(v));
                position_add(v, p);
            }
            Mesh.SetVertex(v, p);
            // (the callers mark the triangles dirty as well; here so that no position change keeps a triangle exact)
            if (exactAtStart != null)
                foreach (int t in Mesh.VtxTrianglesItr(v))
                    if (t < exactAtStart.Length)
                        exactAtStart[t] = new Index3i(-1, -1, -1);
        }

        // contract one of the edges of the triangles (shortest first, at most maxLength), the kept vertex at
        // a fixed end, else at either end or the midpoint
        [MethodImpl(JitHints.Optimize)]
        bool collapse_any(params int[] tids)
        {
            var edges = new List<KeyValuePair<double, int>>();
            foreach (int t in tids) {
                Index3i te = Mesh.GetTriEdges(t);
                for (int k = 0; k < 3; ++k) {
                    Index2i ev = Mesh.GetEdgeV(te[k]);
                    double len = Mesh.GetVertex(ev.a).Distance(Mesh.GetVertex(ev.b));
                    if (len <= maxLength)
                        edges.Add(new KeyValuePair<double, int>(len, te[k]));
                }
            }
            edges.Sort((x, y) => x.Key.CompareTo(y.Key));
            foreach (var e in edges) {
                if (Mesh.IsEdge(e.Value) == false)
                    continue;
                Index2i ev = Mesh.GetEdgeV(e.Value);
                int a = ev.a, b = ev.b;
                Vector3d pa = Mesh.GetVertex(a), pb = Mesh.GetVertex(b);
                bool fa = is_fixed(a), fb = is_fixed(b);
                // (keep b at p)
                if (fa && fb == false) {
                    if (try_collapse(b, a, pa)) return true;
                } else if (fb && fa == false) {
                    if (try_collapse(a, b, pb)) return true;
                } else {
                    if (try_collapse(a, b, pb) || try_collapse(b, a, pa)) return true;
                    if (fa == false && try_collapse(a, b, 0.5 * (pa + pb))) return true;
                }
            }
            return false;
        }

        [MethodImpl(JitHints.Optimize)]
        void fix_degenerate(int t)
        {
            if (collapse_any(t))
                return;
            // caps: flip the longest edge first; else move the corner opposite the longest edge away from it
            Index3i tv = Mesh.GetTriangle(t), te = Mesh.GetTriEdges(t);
            int[] order = { 0, 1, 2 };
            double[] len = new double[3];
            for (int k = 0; k < 3; ++k)
                len[k] = -pos(tv[k]).Distance(pos(tv[(k + 1) % 3]));
            Array.Sort(len, order);
            foreach (int k in order)
                if (try_flip(te[k]) || try_zip(te[k]))
                    return;
            if (try_strip(t))
                return;
            int kl = order[0];
            int apex = tv[(kl + 2) % 3];
            if (-len[0] <= 0)
                return;
            // height for the degenerate area, and more
            double h = 2 * areaEps / -len[0];
            if (Mesh.IsBoundaryVertex(apex)) {
                // along the boundary
                for (double s = 2 * h; s <= maxLength; s *= 4)
                    if (try_slide(apex, s))
                        return;
                return;
            }
            Vector3d e0 = pos(tv[kl]), e1 = pos(tv[(kl + 1) % 3]), c = pos(apex);
            // (unit vectors without a threshold: Normalized returns zero below the absolute MathUtil.Epsilon, which made
            // the moves depend on the scale)
            Vector3d dir = unit(e1 - e0);
            Vector3d off = (c - e0) - (c - e0).Dot(dir) * dir;
            if (unit(off) == Vector3d.Zero) {
                // on the line: towards the centroid of the apex's other neighbours
                Vector3d ctr = Vector3d.Zero;
                int n = 0;
                foreach (int u in Mesh.VtxVerticesItr(apex)) { ctr += pos(u); n++; }
                off = ctr / Math.Max(n, 1) - c;
                off -= off.Dot(dir) * dir;
            }
            off = unit(off);
            if (off == Vector3d.Zero)
                return;
            for (double s = 2 * h; s <= maxLength; s *= 4)
                if (try_move(apex, c + s * off))
                    return;
        }

        [MethodImpl(JitHints.Optimize)]
        void fix_intersection(int ta, int tb)
        {
            if (collapse_any(ta, tb))
                return;
            Index3i a = Mesh.GetTriangle(ta), b = Mesh.GetTriangle(tb);
            // (the normals without a threshold: GetTriNormal normalizes with the absolute MathUtil.Epsilon)
            Vector3d na = tri_normal(ta), nb = tri_normal(tb);
            // movable vertices first; each along the other triangle's normal (both ways) or towards the centroid
            // of its neighbours, from a few units in the last place up to maxLength
            var verts = new List<KeyValuePair<int, Vector3d>>();
            for (int pass = 0; pass < 2; ++pass)
                for (int k = 0; k < 6; ++k) {
                    int v = (k < 3) ? a[k] : b[k - 3];
                    if (is_fixed(v) == (pass == 0))
                        continue;
                    verts.Add(new KeyValuePair<int, Vector3d>(v, (k < 3) ? nb : na));
                }
            foreach (var vn in verts) {
                int v = vn.Key;
                if (Mesh.IsVertex(v) == false)
                    continue;
                Vector3d p = Mesh.GetVertex(v);
                Vector3d ctr = Vector3d.Zero;
                int n = 0;
                foreach (int u in Mesh.VtxVerticesItr(v)) { ctr += Mesh.GetVertex(u); n++; }
                Vector3d toCtr = (n > 0) ? unit(ctr / n - p) : Vector3d.Zero;
                // (the first step a few units in the last place of p; for p at the origin, of maxLength: an absolute
                // constant would make the steps depend on the scale)
                double ulp = 8 * 2.220446049250313e-16 * Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z)));
                if (!(ulp > 0))
                    ulp = MeshRoundingRepair.ulp(maxLength);
                if (Mesh.IsBoundaryVertex(v)) {
                    for (double s = ulp; s <= maxLength; s *= 16)
                        if (try_slide(v, s))
                            return;
                    continue;
                }
                for (double s = ulp; s <= maxLength; s *= 16) {
                    if (vn.Value.LengthSquared > 0.5 && (try_move(v, p + s * vn.Value) || try_move(v, p - s * vn.Value)))
                        return;
                    if (toCtr.LengthSquared > 0.5 && try_move(v, p + s * toCtr))
                        return;
                }
            }
            Index3i ea = Mesh.GetTriEdges(ta), eb = Mesh.GetTriEdges(tb);
            for (int k = 0; k < 3; ++k)
                if (try_flip(ea[k]) || (Mesh.IsTriangle(tb) && try_flip(eb[k])))
                    return;
        }

        // components with a degenerate triangle or an edited vertex that are tiny or thin
        [MethodImpl(JitHints.Optimize)]
        int remove_tiny_components(List<int> bad)
        {
            var comps = new MeshConnectedComponents(Mesh);
            comps.FindConnectedT();
            var badSet = new HashSet<int>(bad);
            int count = 0;
            foreach (var comp in comps.Components) {
                bool hasBad = false;
                double sum = 0, volume = 0;
                // (the volume relative to a point of the component, against cancellation far from the origin)
                Vector3d o = pos(Mesh.GetTriangle(comp.Indices[0]).a);
                foreach (int t in comp.Indices) {
                    Index3i tv = Mesh.GetTriangle(t);
                    hasBad |= badSet.Contains(t) || touched.Contains(tv.a) || touched.Contains(tv.b) || touched.Contains(tv.c);
                    sum += area(t);
                    volume += (pos(tv.a) - o).Dot((pos(tv.b) - o).Cross(pos(tv.c) - o)) / 6;
                }
                if (hasBad == false)
                    continue;
                // tiny, or (closed) thinner than maxLength: a slab of thickness h has about volume = h area / 2
                bool closed = true;
                foreach (int t in comp.Indices) {
                    Index3i te = Mesh.GetTriEdges(t);
                    closed &= Mesh.IsBoundaryEdge(te.a) == false && Mesh.IsBoundaryEdge(te.b) == false && Mesh.IsBoundaryEdge(te.c) == false;
                }
                // (only closed ones: an open component may close the rest by position, eg sheets that touch along an
                // edge with their own vertices; removing it would leave holes)
                if (closed == false || (sum >= 1e4 * areaEps && 2 * Math.Abs(volume) >= maxLength * sum))
                    continue;
                foreach (int t in comp.Indices)
                    remove_triangle(t, true);
                count += comp.Indices.Length;
                RemovedComponents++;
            }
            if (count > 0) {
                RemovedTriangles += count;
                if (atPosition != null) {
                    atPosition.Clear();
                    foreach (int vid in Mesh.VertexIndices())
                        position_add(vid, Mesh.GetVertex(vid));
                }
                rebuild_tree();
            }
            return count;
        }
    }
}
