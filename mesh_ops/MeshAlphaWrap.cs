// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// Alpha wrapping with an offset (Portaneri, Rouxel-Labbé, Hemmer, Cohen-Steiner, Alliez, "Alpha Wrapping
    /// with an Offset", ACM TOG 2022): a closed, oriented, 2-manifold surface without self-intersections that
    /// strictly encloses any input: triangle soups, open or non-manifold meshes, degenerate triangles, point
    /// clouds. The result is computed, not repaired, so it does not depend on the input being valid.
    ///
    /// The method refines and carves a 3D Delaunay triangulation (Delaunay3), starting from a box around the
    /// input. A ball of radius Alpha "flows" in from outside through Delaunay facets it fits through (the smallest
    /// empty ball through the facet's vertices, the paper's Phi(f), larger than Alpha; TraverseByEmptyBall); a cell it
    /// reaches is carved (marked outside), unless
    ///   1. the Voronoi edge dual to the facet crosses the offset surface (the points at distance Offset from
    ///      the input): its first crossing, coming from outside, is inserted as a Steiner point;
    ///   2. the cell intersects the input: the projection of its circumcenter onto the offset surface is
    ///      inserted.
    /// The result is the boundary between the outside and the remaining cells, made manifold by moving
    /// outside cells around non-manifold edges and vertices to the inside.
    ///
    /// Guarantees (with the exact predicates of Delaunay3; the result vertices are the triangulation vertices,
    /// no rounding):
    ///   - closed, consistently oriented (outwards), 2-manifold, no self-intersections;
    ///   - the input is strictly inside: no outside cell comes closer than 1e-6 * Offset to it (the margin of
    ///     the floating-point distance tests);
    ///   - no triangle of the result touches or crosses a triangle of the input (exact tests; measured: 0 of 2.67M triangles
    ///     on 20 Thingi10K meshes), and the vertices keep the Offset from it (minimum 1.000 Offset measured);
    ///     MeshRobustRepair's simplification (SimplifyAlphaWrap) and quality pass keep the first part (they skip an operation
    ///     whose new triangles touch the input), their vertices stay at least about 0.35 Offset away (measured);
    ///   - the vertices lie on the offset surface (up to rounding), so between them the result can come closer
    ///     to the input than Offset (by the sagitta of the triangles); it stays within about Offset + Alpha
    ///     where the facets come from carving. Where the manifold step moved cells inside it can reach farther (measured
    ///     up to 8.8 (Offset + Alpha) without RefineNonManifold, 1.1 with it, on an 800 mesh Thingi10K sample).
    /// Alpha controls the detail: cavities and gaps narrower than about 2 * Alpha are not entered (they are
    /// wrapped over), and the result has triangles with a circumradius of at most about Alpha. Smaller Alpha =
    /// more detail, more triangles, more time (about 1 / Alpha^2). Offset keeps the result away from the input.
    /// Typical values: Alpha 1/100 to 1/20 of the bounding box diagonal, Offset 1/30 of Alpha.
    ///
    /// Triangle shape (derived here, not a claim of the paper; up to rounding and the contact margin, with Offset
    /// below Alpha / 2): every Steiner point is at least min(Offset, Alpha - Offset) from every other vertex (a rule 1
    /// point is the center of an empty ball larger than Alpha, unless a circumcenter on its dual edge is not finite and
    /// the edge is replaced by a long normal segment, see clamp_center; a rule 2 point lies that deep inside an empty
    /// circumsphere, see rule_projection). So a result triangle with a circumradius of at most Alpha has a min angle of
    /// at least asin(min(Offset, Alpha - Offset) / (2 Alpha)) = asin(Offset / (2 Alpha)) (0.95 degrees at the defaults, 2.9 degrees at Alpha = 10 Offset) and a max
    /// angle of at most 180 degrees minus twice that. Larger triangles, without an angle bound, come from the manifold
    /// step (outside cells moved inside, up to 179 degrees), from rejected Steiner points (Counts["rejected_points"]) and
    /// from PointLimitReached. RefineNonManifold refines at the spots where the manifold step would move cells first, which
    /// removes most of them (an 800 mesh Thingi10K sample at Alpha = 10 Offset: triangles below 0.5 degrees 28 -> 5, above
    /// 170 degrees 327 -> 40). Rule 2 points projected from the clamped dual edge (Counts["projection_fallbacks"], none
    /// measured) have no separation bound.
    ///
    /// Exception to the separation: on the retry after box corners on the result, a pinch point for an outside cell with a box corner (Counts["pinch_corner_points"], flat
    /// input: the huge cells above and below a plane of Steiner points) only lies strictly inside the cell's circumsphere, so
    /// it can be closer to a vertex than that and the triangles around it have no angle bound. The retry of Compute() after box
    /// corners on the result refines at non-manifold spots even with RefineNonManifold off.
    ///
    /// Not covered by the guarantees: with MaxSteinerPoints reached (PointLimitReached) the box corners can be
    /// vertices of the result.
    ///
    /// Supported input range (docs/QA.md 2.5a): every nonzero coordinate has a magnitude in [2^-128, 2^128] (about 3e-39
    /// to 3e38) and the extent of the input is zero (a single point) or at least 2^-40 of the largest magnitude; otherwise Compute() returns false
    /// with InvalidInput. Measured (sphere, relative alpha 5, relative offset 100): usable between 2^-160 and 2^160; at
    /// 2^+-180 and 2^+-200 the wrap degrades or runs for minutes.
    ///
    /// Implemented from the paper, no code taken over (docs/SOURCES.md).
    /// </summary>
    public class MeshAlphaWrap
    {
        public enum WrapStatus
        {
            Success = 0,
            /// <summary> no triangles and no points </summary>
            EmptyInput = 1,
            /// <summary> non-finite coordinates, finite ones whose box (with its margin) does not fit doubles, or coordinates outside the supported range (class comment) </summary>
            InvalidInput = 2,
            /// <summary> Alpha / Offset not positive (eg relative values for an input of zero size) </summary>
            InvalidParameters = 3,
            Cancelled = 4,
            /// <summary>
            /// MaxSteinerPoints reached: the refinement stopped early. Result is set and has all the guarantees
            /// (closed, manifold, encloses the input), but has less detail than requested.
            /// </summary>
            PointLimitReached = 5,
            /// <summary>
            /// internal failure (should not happen), see Error; also if the result misses the guarantees (not closed,
            /// bowtie vertices; for a Success result also box corners on it or rejected rule 1 Steiner points): Result
            /// is then kept for diagnostics
            /// </summary>
            Failed = 6
        }

        /// <summary> input triangles (any mesh: soup, open, non-manifold); vertices without triangles are used as points </summary>
        public DMesh3 Mesh;
        /// <summary> additional input points (point cloud) </summary>
        public List<Vector3d> Points = new List<Vector3d>();

        /// <summary> ball radius (detail). If not positive: bounding box diagonal / RelativeAlpha </summary>
        public double Alpha = 0;
        public double RelativeAlpha = 20;
        /// <summary> distance of the result to the input. If not positive: bounding box diagonal / RelativeOffset </summary>
        public double Offset = 0;
        public double RelativeOffset = 600;

        /// <summary> stop refining after this many Steiner points (status PointLimitReached) </summary>
        public int MaxSteinerPoints = 2000000;
        /// <summary>
        /// Refine at the non-manifold spots of the carved surface before the manifold step (default true): the outside
        /// cells that the manifold step would move inside get a Steiner point on the offset surface, and the refinement
        /// continues (up to 10 rounds). The facets of moved cells were never gates (circumradius above Alpha, caps up to
        /// 179 degrees, the result up to several Offset + Alpha from the input); with the refinement most spots become
        /// manifold by carving, which keeps the bounds of the class doc there; the spots that remain are made manifold as
        /// before, without a bound. Costs about 20 % more time at Alpha = 10 Offset. The extra points count against
        /// MaxSteinerPoints: a wrap that reaches it here ends with PointLimitReached (the guarantees of that status), where
        /// it had Success without the refinement. A rule 1 point rejected during the refinement fails the result check as
        /// any other (Failed, then the retry with the other box).
        /// false: the manifold step alone, as before (the order of its candidates differs only where the old score rounded
        /// two volumes to the same value). Default on: owner decision 2026-11-02, with the time cost above.
        /// </summary>
        public bool RefineNonManifold = true;
        const int MaxNonManifoldRounds = 10;

        /// <summary>
        /// Refine result facets whose smallest angle is below this (degrees; 0 = off) before the manifold step: such a
        /// facet gets the center of its surface Delaunay ball as a Steiner point (where its dual Voronoi edge crosses the
        /// offset surface, as rule 1), otherwise the projection of its inside cell's circumcenter onto the offset surface
        /// (as rule 2), and the refinement continues, up to 10 rounds. This is the quality rule of Delaunay refinement on
        /// surfaces (Chew 1993; Boissonnat and Oudot 2005), with the separation of the class doc as the guard: a point is
        /// used only if it is at least min(Offset, Alpha - Offset) from every vertex (the center of an empty ball of that
        /// radius, or that deep inside an empty circumsphere), so the refinement terminates and the angle bound of the class
        /// doc holds. Facets that get no such point stay. It runs after RefineNonManifold, which then runs once more (the
        /// new points can make new pinches). Costs a few percent of triangles (docs/design/ALPHA_WRAP_PAPER_NOTES.md
        /// section 7: the 1 %-worst angle 17.8 -> 20.4 degrees on Thingi10K meshes, with TraverseByEmptyBall). The extra
        /// points count against MaxSteinerPoints (PointLimitReached, as RefineNonManifold). 15 is a measured choice
        /// (section 7: 20 degrees gives more triangles, +6.8 %, and larger largest angles), not a derived bound (Chew's surface bound is 30 degrees); at 60 and
        /// above every facet counts as poor and the rounds run to their limit; NaN or a value <= 0 turns it off.
        /// </summary>
        public double RefineMinAngleDegrees = 15;
        const int MaxQualityRounds = 10;

        /// <summary>
        /// (tests) gates by the paper's Phi(f) (Definition 2: the smallest empty ball through the facet's vertices, along
        /// its dual Voronoi edge) instead of the facet's circumradius (false: the circumradius, as before). Phi(f) is at
        /// least the circumradius, so more facets are gates: the obtuse facets whose circumcircle is smaller than alpha
        /// but whose empty balls are not. It removes most result triangles above 150 degrees, and the quality refinement
        /// needs it (with the circumradius, its points leave such caps, up to 178 degrees). Costs triangles on thin sheets,
        /// as the paper's wrap does (a plate of thickness 0 at Alpha = diagonal / 300: twice the triangles, like CGAL's
        /// alpha wrap there; a plate of 1e-3: +24 %)
        /// </summary>
        internal bool TraverseByEmptyBall = true;

        /// <summary> make the result 2-manifold (otherwise it can have non-manifold edges and vertices, which DMesh3 rejects; for tests) </summary>
        internal bool MakeManifold = true;
        /// <summary> if the result misses the guarantees, wrap once more with a different box margin (off: for tests of the result check) </summary>
        internal bool RetryWithOtherBox = true;
        /// <summary> for tests: the first wrap with the box of the retry (different margins on each side), no retry </summary>
        internal bool StartWithOtherBox = false;
        /// <summary> for tests: rejects the insertion of a Steiner point of the given rule (1 or 2) when it returns true </summary>
        internal Func<int, bool> RejectInsertF;
        /// <summary> for tests: rule 2 always projects the clamped end of the dual edge (the fallback path) </summary>
        internal bool ForceProjectionFallback = false;
        bool asymmetricBox;
        // the retry after a result with box corners: the refinement at non-manifold spots runs even if RefineNonManifold is off
        // (without it the manifold step moves the cells with box corners inside, which is what failed)
        bool forceRefineNonManifold;

        /// <summary> return true to cancel (checked during the refinement and the manifold step); null = never </summary>
        public Func<bool> CancelF = () => { return false; };

        bool cancelled() { return CancelF != null && CancelF(); }

        // outputs
        public DMesh3 Result;
        public WrapStatus Status;
        /// <summary> why Compute() failed, otherwise null </summary>
        public string Error;
        /// <summary> the alpha and offset used (absolute) </summary>
        public double UsedAlpha, UsedOffset;
        /// <summary> time per phase, for diagnostics </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();
        /// <summary> Steiner points, carved cells etc, for diagnostics </summary>
        /// <summary>
        /// Number of Steiner points of the exception to the separation bound (class comment: pinch points for cells with a box corner on
        /// the retry); 0 for a result without it. Those points can have no min-angle bound around them
        /// </summary>
        public int CornerPinchPoints { get; private set; }
        public Dictionary<string, long> Counts = new Dictionary<string, long>();
        /// <summary> the triangulation of the last Compute() (for tests) </summary>
        internal Delaunay3 Triangulation { get { return dt; } }

        public MeshAlphaWrap(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public MeshAlphaWrap(IEnumerable<Vector3d> points)
        {
            Points = new List<Vector3d>(points);
        }


        const byte Inside = 0, Outside = 1;
        // cells closer than ContactTolerance * Offset to the input count as intersecting it (margin for the
        // floating-point distances): the result keeps at least that distance
        const double ContactTolerance = 1e-6;

        AlphaWrapOracle oracle;
        Delaunay3 dt;
        byte[] label = new byte[0];
        double alpha, offset, contact;
        double diag;
        // distance of each triangulation vertex to the input (NaN = not computed)
        List<double> vertexDistance = new List<double>();
        GateQueue gates = new GateQueue();
        Stopwatch phaseTimer = new Stopwatch();

        // the parts of each refinement phase (wrap, wrap.pinches, wrap.quality, wrap.pinches2) as "<phase>/<part>": the
        // time in that phase only (the wrap.intersection / .touch / .insert keys are the totals over all of them)
        static readonly string[] splitParts = { "intersection", "touch", "insert", "carve", "trial" };
        readonly long[] splitLast = new long[5];
        long ticksCarve, ticksTrial;

        void split_phase(string name)
        {
            long[] now = { ticksIntersection, ticksTouch, ticksInsert, ticksCarve, ticksTrial };
            for (int k = 0; k < now.Length; ++k) {
                if (name != null) PhaseMilliseconds[name + "/" + splitParts[k]] = (now[k] - splitLast[k]) * 1000 / Stopwatch.Frequency;
                splitLast[k] = now[k];
            }
        }

        void end_phase(string name)
        {
            if (name == "wrap" || name == "wrap.pinches" || name == "wrap.quality" || name == "wrap.pinches2")
                split_phase(name);
            PhaseMilliseconds[name] = phaseTimer.ElapsedMilliseconds;
            phaseTimer.Restart();
        }

        /// <summary> computes Result; true on Success (see Status; Result is also set for PointLimitReached) </summary>
        public bool Compute()
        {
            asymmetricBox = StartWithOtherBox;
            forceRefineNonManifold = false;
            bool ok = compute_once();
            // a result that misses the guarantees (box corners on it, from cells that stayed inside up to the box,
            // or a non-manifold remainder): once more with a slightly different margin on each side of the box. With
            // the same margin on all sides, symmetric inputs (eg a unit sphere, alpha = diagonal / 10) make nearly flat
            // cells of box corners and Steiner points; their Steiner points can be rejected, and the cells stay inside.
            if (Status == WrapStatus.Failed && Result != null && RetryWithOtherBox && !StartWithOtherBox) {
                asymmetricBox = true;
                forceRefineNonManifold = true;
                ok = compute_once();
                Counts["box_retries"] = 1;
            }
            return ok;
        }

        static bool finite(Vector3d v)
        {
            return Math.Abs(v.x) <= double.MaxValue && Math.Abs(v.y) <= double.MaxValue && Math.Abs(v.z) <= double.MaxValue;
        }

        bool compute_once()
        {
            Result = null;
            Error = null;
            PhaseMilliseconds.Clear();
            Counts.Clear();
            CornerPinchPoints = 0;
            phaseTimer.Restart();
            Status = setup();
            if (Status != WrapStatus.Success)
                return false;
            end_phase("setup");

            reset_refinement_counts();
            labelsManifold = false;
            changedSinceTrial = null;
            lastTrialVertices.Clear();
            localTrials = localFound = confirmTrials = confirmFound = 0;
            Status = refine_and_carve();
            end_phase("wrap");
            if (Status != WrapStatus.Success && Status != WrapStatus.PointLimitReached)
                return false;
            if (MakeManifold && (RefineNonManifold || forceRefineNonManifold) && Status == WrapStatus.Success) {
                // (the confirming full trial of this pass is left to the pass after the quality refinement, which runs then)
                skippedConfirm = false;
                Status = refine_non_manifold(!(SkipFirstConfirm && RefineMinAngleDegrees > 0));
                end_phase("wrap.pinches");
                if (Status != WrapStatus.Success && Status != WrapStatus.PointLimitReached)
                    return false;
            }
            // the quality refinement after the pinches: its points can make new pinches (they are many, and on the offset
            // surface near the spots the pinch refinement just resolved), so the pinch refinement runs once more after it
            // (on random soups the manifold step otherwise moved up to 4 times more cells, and the result reached 1.12
            // (offset + alpha) from the input)
            if (RefineMinAngleDegrees > 0 && Status == WrapStatus.Success) {
                Status = refine_quality();
                end_phase("wrap.quality");
                if (Status != WrapStatus.Success && Status != WrapStatus.PointLimitReached)
                    return false;
                if (MakeManifold && (RefineNonManifold || forceRefineNonManifold) && Status == WrapStatus.Success && (Counts["quality_points"] > 0 || skippedConfirm)) {
                    Status = refine_non_manifold();
                    end_phase("wrap.pinches2");
                    if (Status != WrapStatus.Success && Status != WrapStatus.PointLimitReached)
                        return false;
                }
            }

            // (the last trial run of the refinement at non-manifold spots found nothing to move and kept its labels: they
            // are what remove_pockets and make_manifold would give, see cells_moved_by_manifold_step)
            WrapStatus manifold = WrapStatus.Success;
            if (!(labelsManifold && MakeManifold)) {
                remove_pockets();
                manifold = MakeManifold ? make_manifold() : WrapStatus.Success;
            } else {
                Counts["manifold_reused"] = 1;
            }
            if (manifold != WrapStatus.Success) {
                Status = manifold;
                Error = manifold == WrapStatus.Cancelled ? null : "could not make the result manifold";
                return false;
            }
            end_phase("manifold");
            if (LocalPinchTrials) {
                Counts["local_trials"] = localTrials;
                Counts["local_trials_found"] = localFound;
                Counts["local_trials_confirmed"] = confirmTrials;
                Counts["local_trials_missed"] = confirmFound;
            }
            if (cancelled()) {
                Status = WrapStatus.Cancelled;
                return false;
            }
            Result = extract();
            end_phase("extract");
            if (Result == null) {
                Status = WrapStatus.Failed;
                return false;
            }
            Counts["result_triangles"] = Result.TriangleCount;
            CornerPinchPoints = Counts.TryGetValue("pinch_corner_points", out long cpp) ? (int)cpp : 0;
            Counts["result_box_corners"] = boxCornersInResult;
            // the guarantees, checked (topology only, cheap): a result that misses them is not reported as one that
            // has them (Result is kept for diagnostics)
            // (a rejected rule 1 point leaves a facet whose dual edge crosses the offset surface on the result: it can
            // come closer to the input than the offset, so the offset is not guaranteed)
            string violated = !Result.IsClosed() ? "not closed" : MeshIterators.BowtieVertices(Result).Any() ? "bowtie vertices"
                : (Status == WrapStatus.Success && boxCornersInResult > 0) ? "box corners on the result"
                : (Status == WrapStatus.Success && Counts["rejected_rule1"] > 0) ? "rule 1 Steiner points rejected (offset not guaranteed)" : null;
            if (violated != null) {
                Status = WrapStatus.Failed;
                Error = "result " + violated;
                return false;
            }
            return Status == WrapStatus.Success;
        }


        const double RangeMin = 2.9387358770557188e-39, RangeMax = 3.402823669209385e38;   // 2^-128, 2^128

        /// <summary> null if every nonzero coordinate has a magnitude in [2^-128, 2^128] and the extent is zero or at least 2^-40 of the largest one, otherwise why not </summary>
        static string outside_range(List<Vector3d> tris, List<Vector3d> pts)
        {
            Vector3d lo = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue), hi = -lo;
            double biggest = 0;
            foreach (var list in new[] { tris, pts })
                foreach (Vector3d p in list)
                    for (int c = 0; c < 3; ++c) {
                        double a = Math.Abs(p[c]);
                        if (!(a == 0 || (a >= RangeMin && a <= RangeMax)))
                            return "outside the supported range: a nonzero coordinate has a magnitude outside [2^-128, 2^128]";
                        biggest = Math.Max(biggest, a);
                        lo[c] = Math.Min(lo[c], p[c]);
                        hi[c] = Math.Max(hi[c], p[c]);
                    }
            double extent = Math.Max(hi.x - lo.x, Math.Max(hi.y - lo.y, hi.z - lo.z));
            if (extent > 0 && extent < biggest * 9.094947017729282e-13)
                return "outside the supported range: the extent of the input is below 2^-40 of the largest coordinate magnitude";
            return null;
        }

        WrapStatus setup()
        {
            var tris = new List<Vector3d>();
            var pts = new List<Vector3d>();
            if (Mesh != null) {
                foreach (int tid in Mesh.TriangleIndices()) {
                    Index3i t = Mesh.GetTriangle(tid);
                    tris.Add(Mesh.GetVertex(t.a)); tris.Add(Mesh.GetVertex(t.b)); tris.Add(Mesh.GetVertex(t.c));
                }
                foreach (int vid in Mesh.VertexIndices())
                    if (Mesh.GetVtxEdgeCount(vid) == 0) pts.Add(Mesh.GetVertex(vid));
            }
            if (Points != null)
                pts.AddRange(Points);
            if (tris.Count == 0 && pts.Count == 0) {
                Error = "input has no triangles and no points";
                return WrapStatus.EmptyInput;
            }
            foreach (var list in new[] { tris, pts })
                foreach (Vector3d v in list)
                    if (!MathUtil.IsFinite(v.x) || !MathUtil.IsFinite(v.y) || !MathUtil.IsFinite(v.z)) {
                        Error = "input has non-finite coordinates";
                        return WrapStatus.InvalidInput;
                    }
            // the supported range (class comment, docs/QA.md 2.5a): outside it the wrap ran for minutes (beyond 2^+-180) or
            // reported a success with an input vertex outside (coordinates of 1e-276 next to an extent of 1e17)
            string outside = outside_range(tris, pts);
            if (outside != null) {
                Error = outside;
                return WrapStatus.InvalidInput;
            }
            oracle = new AlphaWrapOracle(tris, pts);
            Counts["input_triangles"] = oracle.Triangles;
            Counts["input_segments"] = oracle.Segments;
            Counts["input_points"] = oracle.Points;

            AxisAlignedBox3d bounds = oracle.Bounds;
            diag = bounds.DiagonalLength;
            alpha = Alpha > 0 ? Alpha : (RelativeAlpha > 0 ? diag / RelativeAlpha : 0);
            offset = Offset > 0 ? Offset : (RelativeOffset > 0 ? diag / RelativeOffset : 0);
            UsedAlpha = alpha; UsedOffset = offset;
            if (!(alpha > 0) || !(offset > 0) || double.IsInfinity(alpha) || double.IsInfinity(offset)) {
                Error = "alpha and offset must be positive (relative values need an input of nonzero size)";
                return WrapStatus.InvalidParameters;
            }
            contact = ContactTolerance * offset;

            // the box keeps the offset surface strictly inside, with room for alpha balls around it
            AxisAlignedBox3d box = bounds;
            double margin = 2 * (offset + alpha) + 1e-3 * Math.Max(diag, offset);
            // (the retry of Compute: a slightly different margin on each side, so the box is not symmetric to the input)
            if (asymmetricBox) {
                box.Min -= margin * new Vector3d(1.0137, 1.0291, 1.0413);
                box.Max += margin * new Vector3d(1.0371, 1.0173, 1.0239);
            } else {
                box.Expand(margin);
            }
            // a margin below the units in the last place of the coordinates is lost (a flat input far from the origin gives
            // a box of no extent), and a box that overflows has corners that are not finite
            // (each coordinate: Vector3d.IsFinite tests their sum, which can overflow)
            if (!(finite(box.Min) && finite(box.Max) && box.Max.x > box.Min.x && box.Max.y > box.Min.y && box.Max.z > box.Min.z)) {
                Error = "the extent of the input (with the margin of the box) does not fit double coordinates";
                return WrapStatus.InvalidInput;
            }
            dt = new Delaunay3(box);
            // the cell stamps of the new triangulation start again: entries of an earlier Compute() would match
            Array.Clear(centerStamp, 0, centerStamp.Length);
            Array.Clear(touchStamp, 0, touchStamp.Length);
            vertexDistance.Clear();
            for (int v = 0; v < dt.VertexCount; ++v)
                vertexDistance.Add(double.NaN);
            ensure_labels();
            foreach (int c in dt.CellIndices())
                label[c] = Inside;
            gates.Clear();
            foreach (int c in dt.CellIndices())
                for (int i = 0; i < 4; ++i)
                    if (dt.Neighbour(c, i) < 0) push_gate(c, i);
            return WrapStatus.Success;
        }

        // per cell (valid while the cell's stamp matches): circumcenter, and whether it touches the input
        Vector3d[] centerCache = new Vector3d[0];
        int[] centerStamp = new int[0];
        sbyte[] touchCache = new sbyte[0];
        int[] touchStamp = new int[0];

        void ensure_labels()
        {
            if (label.Length < dt.MaxCellID) {
                int n = Math.Max(dt.MaxCellID, 2 * label.Length);
                Array.Resize(ref label, n);
                Array.Resize(ref centerCache, n); Array.Resize(ref centerStamp, n);
                Array.Resize(ref touchCache, n); Array.Resize(ref touchStamp, n);
            }
        }

        Vector3d circumcenter(int c)
        {
            int st = dt.CellStamp(c);
            if (centerStamp[c] != st) {
                centerCache[c] = dt.Circumcenter(c);
                centerStamp[c] = st;
            }
            return centerCache[c];
        }

        byte label_of(int c) { return c < 0 ? Outside : label[c]; }


        // ---- refinement and carving ----

        // (totals over all calls of refine_and_carve in one Compute(): the pinch refinement continues the refinement)
        long ticksIntersection, ticksTouch, ticksInsert, projectionFallbacks;
        long rule1, rule2, carved, rejected, rejected1, rejected2, popped;
        // successful insertions into this triangulation (the stars of the manifold step follow it)
        long insertions;
        int steiner;
        readonly List<int> removedCells = new List<int>(), addedCells = new List<int>();

        void reset_refinement_counts()
        {
            ticksIntersection = ticksTouch = ticksInsert = projectionFallbacks = 0;
            ticksCarve = ticksTrial = 0;
            split_phase(null);
            rule1 = rule2 = carved = rejected = rejected1 = rejected2 = popped = 0;
            insertions = 0;
            staleGates = requeuedGates = 0;
            steiner = 0;
        }

        // gates whose Phi(f) changed after they were queued (see refine_and_carve): dropped, queued again
        long staleGates, requeuedGates;

        WrapStatus refine_and_carve()
        {
            long carveStart = Stopwatch.GetTimestamp();
            WrapStatus status = refine_and_carve_loop();
            ticksCarve += Stopwatch.GetTimestamp() - carveStart;
            return status;
        }

        WrapStatus refine_and_carve_loop()
        {
            WrapStatus status = WrapStatus.Success;
            while (gates.Count > 0) {
                if ((++popped & 1023) == 0 && cancelled())
                    return WrapStatus.Cancelled;
                gates.Pop(out double key, out int c, out int i, out int stampC);
                if (!dt.IsCell(c) || dt.CellStamp(c) != stampC || label[c] != Inside)
                    continue;
                int n = dt.Neighbour(c, i);
                if (label_of(n) != Outside)
                    continue;
                // (traversable: only such gates are queued. Phi(f) also depends on the outside cell's circumcenter, and that
                // cell can have been replaced since the push while c was not: compute it again; a gate that is no longer
                // traversable is dropped, one whose radius changed goes back into the queue under its new key)
                if (TraverseByEmptyBall) {
                    double r = facet_empty_ball(c, i);
                    if (!(r > alpha)) { staleGates++; continue; }
                    if (r != key) { requeuedGates++; gates.Push(r, c, i, stampC); continue; }
                }
                Vector3d from, to;
                dual_edge(c, i, out from, out to);

                Vector3d steinerPoint;
                bool insert = false;
                int which = 0;
                long t0 = Stopwatch.GetTimestamp();
                bool crossing = rule_intersection(from, to, out steinerPoint);
                long t1 = Stopwatch.GetTimestamp();
                ticksIntersection += t1 - t0;
                bool touching = !crossing && cell_touches_input(c);
                ticksTouch += Stopwatch.GetTimestamp() - t1;
                if (crossing) {
                    insert = true; which = 1;
                } else if (touching) {
                    insert = rule_projection(c, i, to, out steinerPoint);
                    which = 2;
                    if (!insert) {
                        rejected++; rejected2++;
                        continue;       // keep the cell: the facet stays on the result
                    }
                }
                if (insert) {
                    if (steiner >= MaxSteinerPoints) {
                        status = WrapStatus.PointLimitReached;
                        break;
                    }
                    // (rejected: too close to a vertex, or outside the box; see the result check)
                    if (!insert_steiner(steinerPoint, c, which)) {
                        rejected++;
                        if (which == 1) rejected1++; else rejected2++;
                        continue;       // keep the cell: the facet stays on the result
                    }
                    if (which == 1) rule1++; else rule2++;
                    continue;
                }
                // carve
                labelsManifold = false;
                label[c] = Outside;
                carved++;
                changedSinceTrial?.Add(c);
                touched?.Add(c);
                for (int j = 0; j < 4; ++j) {
                    int m = dt.Neighbour(c, j);
                    if (m >= 0 && label[m] == Inside)
                        push_gate(m, dt.FacetTo(m, c));
                }
            }
            Counts["steiner_points"] = steiner;
            if (staleGates > 0) Counts["gates_stale"] = staleGates;
            if (requeuedGates > 0) Counts["gates_requeued"] = requeuedGates;
            Counts["steiner_intersection"] = rule1;
            Counts["steiner_projection"] = rule2;
            // rule 2 points projected from the clamped end of the dual edge (the cell's circumcenter gave no point strictly
            // inside its circumsphere): no separation bound for these
            Counts["projection_fallbacks"] = projectionFallbacks;
            // rejected: their cells stay inside with the gate facet on the result, which can then come closer to the
            // input than the offset (a rule 1 rejection; never crossing it, the outside cell was checked) or reach the
            // box (then caught by the result check)
            Counts["rejected_points"] = rejected;
            Counts["rejected_rule1"] = rejected1;
            Counts["rejected_rule2"] = rejected2;
            Counts["carved_cells"] = carved;
            Counts["gates_popped"] = popped;
            Counts["cells"] = dt.CellCount;
            PhaseMilliseconds["wrap.intersection"] = ticksIntersection * 1000 / Stopwatch.Frequency;
            PhaseMilliseconds["wrap.touch"] = ticksTouch * 1000 / Stopwatch.Frequency;
            PhaseMilliseconds["wrap.insert"] = ticksInsert * 1000 / Stopwatch.Frequency;
            return status;
        }

        // inserts a Steiner point on the offset surface (from cell hint); its new cells are inside, and their facets
        // towards outside cells are queued as gates. False if the triangulation rejects it (unchanged then)
        bool insert_steiner(Vector3d p, int hint, int which)
        {
            removedCells.Clear(); addedCells.Clear();
            // (no separation needed for correctness; tiny separations would only make slivers)
            long t2 = Stopwatch.GetTimestamp();
            var res = RejectInsertF != null && RejectInsertF(which) ? Delaunay3.InsertResult.Failed
                : dt.Insert(p, hint, 1e-9 * alpha, out int vid, removedCells, addedCells);
            ticksInsert += Stopwatch.GetTimestamp() - t2;
            if (res != Delaunay3.InsertResult.Inserted)
                return false;
            labelsManifold = false;
            steiner++;
            insertions++;
            vertexDistance.Add(offset);
            ensure_labels();
            foreach (int nc in addedCells)
                label[nc] = Inside;
            changedSinceTrial?.AddRange(addedCells);
            touched?.AddRange(addedCells);
            foreach (int nc in addedCells)
                for (int j = 0; j < 4; ++j)
                    if (label_of(dt.Neighbour(nc, j)) == Outside) push_gate(nc, j);
            return true;
        }

        void push_gate(int c, int i)
        {
            double r = TraverseByEmptyBall ? facet_empty_ball(c, i) : facet_radius(c, i);
            if (r > alpha)
                gates.Push(r, c, i, dt.CellStamp(c));
        }

        /// <summary>
        /// The paper's Phi(f) (Definition 2, Fig. 23): the radius of the smallest empty ball through the vertices of facet i
        /// of cell c. The balls through the vertices have their centers on the facet's normal line through its
        /// circumcenter; the empty ones are those with the center on the dual Voronoi edge. So Phi(f) is the facet's
        /// circumradius if the edge passes through the facet's plane, otherwise the radius at the edge's end nearer to
        /// it. An alpha ball can pass through the facet if Phi(f) is larger than alpha. At least the circumradius.
        /// </summary>
        double facet_empty_ball(int c, int i)
        {
            Index3i f = dt.FacetVertices(c, i);
            dual_edge(c, i, out Vector3d from, out Vector3d to);
            return EmptyBallRadius(dt.GetVertex(f.a), dt.GetVertex(f.b), dt.GetVertex(f.c), from, to);
        }

        /// <summary>
        /// (tests) the smallest radius of the balls through a, b, d whose centers lie on the segment [from, to] of the
        /// triangle's normal line through its circumcenter (Phi(f) of facet_empty_ball)
        /// </summary>
        internal static double EmptyBallRadius(Vector3d a, Vector3d b, Vector3d d, Vector3d from, Vector3d to)
        {
            Vector3d u = b - a, v = d - a, w = u.Cross(v);
            double w2 = w.LengthSquared;
            Vector3d x = (u.LengthSquared * v.Cross(w) + v.LengthSquared * w.Cross(u)) / (2 * w2);
            double R = x.Length;
            Vector3d fc = a + x, nrm = w / Math.Sqrt(w2);
            double t0 = (from - fc).Dot(nrm), t1 = (to - fc).Dot(nrm);
            // (a flat facet: no finite normal; NaN compares false and gives the circumradius, as before)
            if (!(Math.Min(t0, t1) > 0) && !(Math.Max(t0, t1) < 0))
                return R;
            double t = Math.Min(Math.Abs(t0), Math.Abs(t1));
            return Math.Sqrt(R * R + t * t);
        }

        /// <summary>
        /// Circumradius of facet i of cell c
        /// </summary>
        double facet_radius(int c, int i)
        {
            Index3i f = dt.FacetVertices(c, i);
            Vector3d a = dt.GetVertex(f.a), b = dt.GetVertex(f.b), d = dt.GetVertex(f.c);
            Vector3d u = b - a, v = d - a, w = u.Cross(v);
            Vector3d x = (u.LengthSquared * v.Cross(w) + v.LengthSquared * w.Cross(u)) / (2 * w.LengthSquared);
            return x.Length;
        }

        /// <summary>
        /// The Voronoi edge dual to facet i of the inside cell c, from the circumcenter of the outside cell to
        /// that of c (centers of the empty balls through the facet; a ray for a facet on the box)
        /// </summary>
        void dual_edge(int c, int i, out Vector3d from, out Vector3d to)
        {
            Index3i f = dt.FacetVertices(c, i);
            Vector3d a = dt.GetVertex(f.a), b = dt.GetVertex(f.b), d = dt.GetVertex(f.c);
            Vector3d u = b - a, v = d - a, w = u.Cross(v);
            double w2 = w.LengthSquared;
            Vector3d nrm = w / Math.Sqrt(w2);       // outwards of c
            Vector3d fc = a + (u.LengthSquared * v.Cross(w) + v.LengthSquared * w.Cross(u)) / (2 * w2);
            to = clamp_center(circumcenter(c), fc, nrm, -1);
            int n = dt.Neighbour(c, i);
            if (n < 0)
                from = fc + (Math.Max((to - fc).Dot(nrm), 0) + 4 * dt.Box.DiagonalLength) * nrm;
            else
                from = clamp_center(circumcenter(n), fc, nrm, 1);
        }

        // circumcenters of flat cells can be far away or not finite: keep them on the facet's normal line,
        // within a few box diagonals (beyond that, they are outside the box either way)
        Vector3d clamp_center(Vector3d cc, Vector3d fc, Vector3d nrm, int side)
        {
            double lim = 4 * dt.Box.DiagonalLength;
            if (double.IsNaN(cc.x + cc.y + cc.z) || double.IsInfinity(cc.x + cc.y + cc.z))
                return fc + side * lim * nrm;
            double t = (cc - fc).Dot(nrm);
            if (Math.Abs(t) > lim)
                return fc + Math.Sign(t) * lim * nrm;
            return cc;
        }

        // Rule 1: first crossing of the Voronoi edge from -> to with the offset surface
        bool rule_intersection(Vector3d from, Vector3d to, out Vector3d p)
        {
            p = Vector3d.Zero;
            double t = oracle.FirstEntry(from, to, offset);
            if (t < 0)
                return false;
            if (t > 0) {
                p = from + t * (to - from);
                return true;
            }
            // from is already within the offset: the edge crosses the surface if it leaves (bisection)
            double dto = oracle.Distance(to) - offset;
            if (!(dto > 0))
                return false;
            double lo = 0, hi = 1;
            for (int k = 0; k < 60; ++k) {
                double mid = 0.5 * (lo + hi);
                if (oracle.Distance(from + mid * (to - from)) > offset) hi = mid; else lo = mid;
            }
            p = from + hi * (to - from);
            return true;
        }

        double vertex_distance(int v)
        {
            double d = vertexDistance[v];
            if (double.IsNaN(d)) {
                d = oracle.Distance(dt.GetVertex(v));
                vertexDistance[v] = d;
            }
            return d;
        }

        // does cell c intersect the input (come closer than the contact tolerance)?
        bool cell_touches_input(int c)
        {
            int st = dt.CellStamp(c);
            if (touchStamp[c] != st) {
                touchCache[c] = (sbyte)(touches_input(c) ? 1 : 0);
                touchStamp[c] = st;
            }
            return touchCache[c] == 1;
        }

        bool touches_input(int c)
        {
            Index4i t = dt.GetCell(c);
            for (int j = 0; j < 4; ++j)
                if (vertex_distance(t[j]) < contact) return true;
            Vector3d a = dt.GetVertex(t.a), b = dt.GetVertex(t.b), d = dt.GetVertex(t.c), e = dt.GetVertex(t.d);
            // the cell is within (longest edge at v) of each vertex v: far enough from the input if a vertex is
            // farther from it than that (distance is 1-Lipschitz). (A closest point query at the centroid rejects
            // almost nothing either: near the result the cells are larger than their distance to the input.)
            double ab = a.Distance(b), ad = a.Distance(d), ae = a.Distance(e), bd = b.Distance(d), be = b.Distance(e), de = d.Distance(e);
            if (vertex_distance(t.a) - Math.Max(ab, Math.Max(ad, ae)) > contact || vertex_distance(t.b) - Math.Max(ab, Math.Max(bd, be)) > contact
                || vertex_distance(t.c) - Math.Max(ad, Math.Max(bd, de)) > contact || vertex_distance(t.d) - Math.Max(ae, Math.Max(be, de)) > contact)
                return false;
            return oracle.TetrahedronTouches(a, b, d, e, contact);
        }

        // Rule 2: projection of the circumcenter of c onto the offset surface. The projected center is the cell's own,
        // not the end of the dual edge (clamped for flat cells: dual_edge): with the true center, the point lies deep
        // inside the empty circumsphere (c touches the input, so the center is closer than the radius R to it, and
        // |center - p| = |distance - offset| <= R - min(offset, R - offset) + contact), so it removes the cell and is at
        // least min(offset, alpha - offset) - contact from every vertex (R > alpha: the gate facet is larger). The projection of the
        // clamped center of a flat cell with a box corner (circumradius above 4 box diagonals, at small alpha) missed
        // the circumsphere: the cell stayed, and the points of neighbouring flat cells landed next to each other,
        // down to 1e-6 alpha apart, which made needles on the result. If the point does not lie strictly inside the
        // circumsphere after all (center not finite, rounding), the clamped end of the dual edge is projected as before
        // (counted, see Counts["projection_fallbacks"]). The guard proves that the cell is removed, not the depth: with
        // a rounded circumcenter the separation holds up to that rounding.
        bool rule_projection(int c, int i, Vector3d to, out Vector3d p)
        {
            Vector3d cc = circumcenter(c);
            if (ForceProjectionFallback == false && MathUtil.IsFinite(cc.x) && MathUtil.IsFinite(cc.y) && MathUtil.IsFinite(cc.z)
                && project_to_offset(c, i, cc, out p) && dt.InCircumsphere(c, p) > 0)
                return true;
            projectionFallbacks++;
            return project_to_offset(c, i, to, out p);
        }

        // the point of the offset surface closest to cc (the closest input point moved by offset towards cc)
        bool project_to_offset(int c, int i, Vector3d cc, out Vector3d p)
        {
            Vector3d q;
            double d2 = oracle.DistanceSquared(cc, out q);
            // (a distance that overflows (a circumcenter beyond 1e154) gives no direction: p would be q, on the input)
            if (!MathUtil.IsFinite(d2)) {
                p = Vector3d.Zero;
                return false;
            }
            double dist = Math.Sqrt(d2);
            Vector3d dir;
            if (dist > 1e-12 * diag) {
                dir = (cc - q) / dist;
            } else {
                // circumcenter on the input: go towards the outside cell
                Index3i f = dt.FacetVertices(c, i);
                dir = AlphaWrapOracle.Unit((dt.GetVertex(f.b) - dt.GetVertex(f.a)).Cross(dt.GetVertex(f.c) - dt.GetVertex(f.a)));
            }
            p = q + offset * dir;
            return double.IsNaN(p.x + p.y + p.z) == false;
        }


        // ---- post-processing ----

        // ---- refinement at non-manifold spots ----

        // The paper's carving gives an immersed, not an embedded, surface: it can pinch at edges and vertices (Section 8).
        // The manifold step (make_manifold) moves outside cells inside until the surface is a 2-manifold, which puts
        // their facets on the result. These were never gates: circumradius above alpha (they were carved through such
        // facets), so no angle bound, and the result moves farther out (MeshRepair-400 wrap cases and an 800 mesh
        // Thingi10K sample: every result triangle below the angle bound came from them). Here, each outside cell the
        // manifold step would move inside (found by a trial run on a copy of the labels) gets a Steiner point on the offset
        // surface, and the refinement continues; its new cells are carved through gates as usual. Most pinches disappear
        // within a few rounds; the manifold step handles the rest. The points keep the separation of the class doc:
        //   - the projection of the cell's circumcenter onto the offset surface, if it lies at least
        //     min(offset, alpha - offset) inside the circumsphere (depth in floating point, inside exactly);
        //   - otherwise the first crossing of the offset surface on the dual edge of a facet towards an inside cell, if that
        //     facet's circumradius is at least that much: the point is the center of an empty ball through the facet's
        //     vertices, at least as large as the facet's circumcircle.
        // Terminates: at most MaxNonManifoldRounds rounds, each inserts at least one point or ends the loop.
        /// <summary>
        /// (tests) the pinch refinement before the quality refinement ends without the full trial that confirms a local trial
        /// which found nothing (Alpha Wrap 3.0 lever 4): the pinch refinement after the quality refinement runs then (also
        /// without quality points) and confirms with a full trial, whose labels the manifold step reuses as before. The result
        /// differs from the confirmed run only where that skipped trial would have found a spot the local trials missed
        /// (Counts confirm_skipped; a spot found later is refined after the quality points instead of before)
        /// </summary>
        internal bool SkipFirstConfirm = true;
        bool skippedConfirm;

        WrapStatus refine_non_manifold(bool confirm = true)
        {
            long rounds = 0, points = 0, cells = 0, skipped = 0;
            var pinch = new List<int>();
            var stamps = new List<int>();
            WrapStatus status = WrapStatus.Success;
            for (int round = 0; round < MaxNonManifoldRounds && status == WrapStatus.Success; ++round) {
                if (cancelled())
                    return WrapStatus.Cancelled;
                long tt = Stopwatch.GetTimestamp();
                WrapStatus trial = cells_moved_by_manifold_step(pinch, out bool local);
                ticksTrial += Stopwatch.GetTimestamp() - tt;
                if (trial == WrapStatus.Cancelled)
                    return trial;
                if (trial == WrapStatus.Success && pinch.Count > 0 && local) localFound++;
                // (a local trial that found nothing is confirmed by a full one: a local trial can miss a spot, see
                // LocalPinchTrials, and the refinement would stop with it left to the manifold step. If the full trial moves
                // nothing either, its labels are kept as usual)
                if (trial == WrapStatus.Success && pinch.Count == 0 && local && !confirm) {
                    // (the labels the confirming trial would have kept when it moved nothing: the pockets removed, see
                    // cells_moved_by_manifold_step; the quality refinement sees those)
                    skippedConfirm = true;
                    add_count("confirm_skipped", 1);
                    remove_pockets();
                } else if (trial == WrapStatus.Success && pinch.Count == 0 && local) {
                    confirmTrials++;
                    tt = Stopwatch.GetTimestamp();
                    trial = cells_moved_by_manifold_step(pinch, out local, true);
                    ticksTrial += Stopwatch.GetTimestamp() - tt;
                    if (trial == WrapStatus.Cancelled)
                        return trial;
                    if (pinch.Count > 0) confirmFound++;
                }
                // (a failing manifold step fails again after the refinement and reports it)
                if (trial != WrapStatus.Success || pinch.Count == 0)
                    break;
                cells += pinch.Count;
                stamps.Clear();
                foreach (int c in pinch)
                    stamps.Add(dt.CellStamp(c));
                int inserted = 0;
                for (int k = 0; k < pinch.Count; ++k) {
                    int c = pinch[k];
                    // (an earlier point of this round can have replaced the cell, or carving relabelled it)
                    if (!dt.IsCell(c) || dt.CellStamp(c) != stamps[k] || label[c] != Outside)
                        continue;
                    if (steiner >= MaxSteinerPoints) {
                        status = WrapStatus.PointLimitReached;
                        break;
                    }
                    if (pinch_point(c, out Vector3d p) && insert_steiner(p, c, 3))
                        inserted++;
                    else
                        skipped++;
                }
                points += inserted;
                if (inserted == 0)
                    break;
                rounds++;
                WrapStatus refined = refine_and_carve();
                if (refined != WrapStatus.Success)
                    status = refined;
                if (status != WrapStatus.Success)
                    break;
            }
            // (totals: the pinch refinement runs again after the quality refinement)
            add_count("pinch_rounds", rounds);
            add_count("pinch_points", points);
            add_count("pinch_cells", cells);
            // cells without a qualifying point (or whose point the triangulation rejected): left to the manifold step
            add_count("pinch_skipped", skipped);
            return status;
        }

        void add_count(string name, long value)
        {
            Counts.TryGetValue(name, out long before);
            Counts[name] = before + value;
        }

        // ---- quality refinement of the result facets (RefineMinAngleDegrees) ----

        // cells created or carved since the last round (null: not tracked)
        List<int> touched;

        // is the smallest angle of facet i of cell c below RefineMinAngleDegrees? sin(min angle) = shortest edge / (2 R)
        // (scale free; a degenerate facet, R infinite or NaN, counts as bad)
        bool poor_facet(int c, int i, double sinMin)
        {
            Index3i f = dt.FacetVertices(c, i);
            Vector3d a = dt.GetVertex(f.a), b = dt.GetVertex(f.b), d = dt.GetVertex(f.c);
            double e = Math.Min(a.Distance(b), Math.Min(b.Distance(d), d.Distance(a)));
            return !(e >= 2 * facet_radius(c, i) * sinMin);
        }

        // the result facets (inside cell, facet to an outside cell) of the cells in `cells` (all cells if null) that are poor
        void poor_facets(List<int> cells, double sinMin, List<int> outCells, List<int> outFacets)
        {
            outCells.Clear(); outFacets.Clear();
            // (a facet is reached from its inside and its outside cell, and a cell can be in the list twice: each facet
            // once, in the order first reached)
            var seen = new HashSet<long>();
            Action<int, int> add = (m, i) => {
                if (seen.Add(4L * m + i) && poor_facet(m, i, sinMin)) { outCells.Add(m); outFacets.Add(i); }
            };
            Action<int> visit = (c) => {
                if (!dt.IsCell(c)) return;
                if (label[c] == Inside) {
                    for (int i = 0; i < 4; ++i)
                        if (label_of(dt.Neighbour(c, i)) == Outside) add(c, i);
                } else {
                    for (int j = 0; j < 4; ++j) {
                        int m = dt.Neighbour(c, j);
                        if (m < 0 || label[m] != Inside) continue;
                        add(m, dt.FacetTo(m, c));
                    }
                }
            };
            if (cells == null) {
                foreach (int c in dt.CellIndices())
                    if (label[c] == Inside) visit(c);
            } else {
                foreach (int c in cells) visit(c);
            }
        }

        // Rounds of: the poor result facets get a Steiner point (quality_point), then the refinement and carving continue.
        // The first round looks at every result facet, the later ones at the facets of the cells created or carved since.
        // Terminates: at most MaxQualityRounds rounds; each inserts a point or ends the loop.
        WrapStatus refine_quality()
        {
            double sinMin = Math.Sin(RefineMinAngleDegrees * MathUtil.Deg2Rad);
            long rounds = 0, points = 0, facets = 0, skipped = 0;
            var cs = new List<int>(); var fs = new List<int>(); var stamps = new List<int>();
            List<int> scan = null;
            WrapStatus status = WrapStatus.Success;
            for (int round = 0; round < MaxQualityRounds; ++round) {
                if (cancelled()) { status = WrapStatus.Cancelled; break; }
                poor_facets(scan, sinMin, cs, fs);
                if (cs.Count == 0) break;
                facets += cs.Count;
                stamps.Clear();
                foreach (int c in cs) stamps.Add(dt.CellStamp(c));
                touched = new List<int>();
                int inserted = 0;
                for (int k = 0; k < cs.Count && status == WrapStatus.Success; ++k) {
                    int c = cs[k], i = fs[k];
                    // (an earlier point of this round can have replaced the cell)
                    if (!dt.IsCell(c) || dt.CellStamp(c) != stamps[k] || label[c] != Inside || label_of(dt.Neighbour(c, i)) != Outside)
                        continue;
                    if (steiner >= MaxSteinerPoints) { status = WrapStatus.PointLimitReached; break; }
                    if (quality_point(c, i, out Vector3d p) && insert_steiner(p, c, 4)) inserted++;
                    else skipped++;
                }
                points += inserted;
                if (inserted > 0) rounds++;
                // (also after the point limit: the cells of this round's points are carved as far as no further point is
                // needed, as refine_non_manifold does; the status stays PointLimitReached unless cancelled)
                if (inserted > 0 && (status == WrapStatus.Success || status == WrapStatus.PointLimitReached)) {
                    WrapStatus carve = refine_and_carve();
                    if (status == WrapStatus.Success || carve == WrapStatus.Cancelled) status = carve;
                }
                scan = touched;
                touched = null;
                if (inserted == 0 || status != WrapStatus.Success) break;
            }
            touched = null;
            // (refine_and_carve sets it; a limit reached in this loop ends it before)
            Counts["steiner_points"] = steiner;
            Counts["quality_rounds"] = rounds;
            Counts["quality_points"] = points;
            Counts["quality_facets"] = facets;
            // poor facets without a qualifying point (or whose point the triangulation rejected): they stay
            Counts["quality_skipped"] = skipped;
            return status;
        }

        // a Steiner point for the poor result facet i of the inside cell c, false if none keeps the separation:
        //   - the first crossing of its dual Voronoi edge with the offset surface (the center of its surface Delaunay ball:
        //     an empty ball through its vertices), if that ball's radius is at least min(offset, alpha - offset);
        //   - otherwise the projection of c's circumcenter onto the offset surface, if it lies at least that deep inside
        //     c's circumsphere (depth in floating point, inside exactly; as pinch_point).
        bool quality_point(int c, int i, out Vector3d p)
        {
            double sep = Math.Min(offset, alpha - offset);
            dual_edge(c, i, out Vector3d from, out Vector3d to);
            // (the ball's radius is at least the facet's circumradius R, and a facet below 30 degrees has R > its shortest
            // edge >= the separation: implied for RefineMinAngleDegrees up to 30, checked for larger values)
            if (rule_intersection(from, to, out p))
                return p.Distance(dt.GetVertex(dt.FacetVertices(c, i).a)) >= sep;
            Vector3d cc = circumcenter(c);
            if (!(MathUtil.IsFinite(cc.x) && MathUtil.IsFinite(cc.y) && MathUtil.IsFinite(cc.z)))
                return false;
            double r = cc.Distance(dt.GetVertex(dt.CellVertex(c, 0)));
            return project_to_offset(c, i, cc, out p) && cc.Distance(p) <= r - sep && dt.InCircumsphere(c, p) > 0;
        }

        // true while the labels are those of a successful make_manifold run without changes since (see
        // cells_moved_by_manifold_step); cleared by every insertion and carving: refine_non_manifold stops after such a
        // trial, but the quality refinement (RefineMinAngleDegrees) inserts and carves after it)
        bool labelsManifold;

        /// <summary>
        /// (Alpha Wrap 2.0 WP3 lever 4b) the trial runs of the manifold step after the first one start only from the vertices
        /// whose star changed since the last trial (the cells inserted or carved since; an insertion's new cells have every
        /// vertex of the cells it replaced) and those that trial found non-manifold, instead of every vertex on the result.
        /// The step processes them in another order, so it can pick other cells, and the refinement other points: not
        /// bit-identical (T1, 100 Thingi10K meshes: 6 differ, by at most 0.2 % triangles and 0.06 degrees of p01). A local
        /// trial can also miss a spot (the outer remove_pockets of a trial fills or reopens pockets, which changes stars away
        /// from the changed cells), so its labels are never kept, and a local trial that finds nothing is confirmed by a full
        /// trial before the refinement stops (Counts local_trials_confirmed, and local_trials_missed when the full one found
        /// something). The labels of that full trial are reused when it moved nothing (ReuseTrialLabels); otherwise the
        /// final manifold step is the full one. The guarantees come from that final step, as before
        /// </summary>
        internal bool LocalPinchTrials = true;
        // cells created or carved since the last trial (null before the first trial of a Compute), the vertices of the cells
        // the last trial moved, the number of local trials
        List<int> changedSinceTrial;
        readonly List<int> lastTrialVertices = new List<int>();
        long localTrials, localFound, confirmTrials, confirmFound;
        /// <summary> (tests) false: the manifold step runs again after a trial that moved nothing (the same result) </summary>
        internal bool ReuseTrialLabels = true;

        // the outside cells that remove_pockets and make_manifold would move inside, without the pockets (enclosed outside
        // regions, whose facets do not reach the result); the labels and counts are restored
        WrapStatus cells_moved_by_manifold_step(List<int> moved, out bool local, bool full = false)
        {
            moved.Clear();
            int n = dt.MaxCellID;
            byte[] saved = new byte[n];
            Array.Copy(label, saved, n);
            var savedCounts = new Dictionary<string, long>(Counts);
            remove_pockets();
            byte[] afterPockets = new byte[n];
            Array.Copy(label, afterPockets, n);
            // (LocalPinchTrials, after a first trial: only the vertices whose star changed since the last trial, or that the
            // last trial found non-manifold, can be non-manifold: the other ones were manifold there and kept their stars)
            List<int> seeds = null;
            if (LocalPinchTrials && changedSinceTrial != null && !full) {
                seeds = new List<int>(lastTrialVertices);
                foreach (int c in changedSinceTrial)
                    if (dt.IsCell(c))
                        for (int j = 0; j < 4; ++j) seeds.Add(dt.CellVertex(c, j));
                localTrials++;
            }
            local = seeds != null;
            WrapStatus status = make_manifold(seeds);
            foreach (int c in dt.CellIndices())
                if (afterPockets[c] == Outside && label[c] == Inside) moved.Add(c);
            lastTrialVertices.Clear();
            foreach (int c in moved)
                for (int j = 0; j < 4; ++j) lastTrialVertices.Add(dt.CellVertex(c, j));
            changedSinceTrial = new List<int>();
            // nothing moved in a full trial: make_manifold changed no label, so the labels and counts are those that
            // remove_pockets and make_manifold give after the refinement: kept (labelsManifold, until a later insertion or
            // carving)
            // (only a full trial: a local one need not have seen every vertex)
            if (status == WrapStatus.Success && moved.Count == 0 && ReuseTrialLabels && seeds == null) {
                labelsManifold = true;
                return status;
            }
            Array.Copy(saved, label, n);
            Counts.Clear();
            foreach (var kv in savedCounts)
                Counts[kv.Key] = kv.Value;
            return status;
        }

        // a Steiner point for the outside cell c at a pinch (see refine_non_manifold), false if neither candidate qualifies
        bool pinch_point(int c, out Vector3d p)
        {
            double depth = Math.Min(offset, alpha - offset);
            Vector3d cc = circumcenter(c);
            if (MathUtil.IsFinite(cc.x) && MathUtil.IsFinite(cc.y) && MathUtil.IsFinite(cc.z)) {
                double r = cc.Distance(dt.GetVertex(dt.CellVertex(c, 0)));
                if (project_to_offset(c, 0, cc, out p) && cc.Distance(p) <= r - depth && dt.InCircumsphere(c, p) > 0)
                    return true;
            }
            for (int j = 0; j < 4; ++j) {
                int m = dt.Neighbour(c, j);
                if (m < 0 || label[m] != Inside)
                    continue;
                int f = dt.FacetTo(m, c);
                if (!(facet_radius(m, f) >= depth))
                    continue;
                dual_edge(m, f, out Vector3d from, out Vector3d to);
                if (rule_intersection(from, to, out p))
                    return true;
            }
            // on the retry only, a cell with a box corner (flat input: the huge cells above and below a plane of Steiner points) that neither
            // candidate qualifies for: the projected center only has to lie strictly inside the circumsphere (exactly), without
            // the depth. The point can be closer to a vertex than the separation, but the manifold step would move the
            // cell inside and put the box corner on the result, which fails the wrap
            if (forceRefineNonManifold && has_corner(c) && MathUtil.IsFinite(cc.x) && MathUtil.IsFinite(cc.y) && MathUtil.IsFinite(cc.z)
                && project_to_offset(c, 0, cc, out p) && dt.InCircumsphere(c, p) > 0) {
                add_count("pinch_corner_points", 1);
                return true;
            }
            p = Vector3d.Zero;
            return false;
        }


        // outside cells not connected to the box boundary (enclosed by an insertion) become inside
        // (reached: pocketStamp[c] == pocketStampValue; the arrays are kept between the calls)
        int[] pocketStamp = new int[0];
        int pocketStampValue;
        readonly Stack<int> pocketStack = new Stack<int>();

        int remove_pockets(List<int> flipped = null)
        {
            if (pocketStamp.Length < dt.MaxCellID) {
                pocketStamp = new int[Math.Max(dt.MaxCellID, 2 * pocketStamp.Length)];
                pocketStampValue = 0;
            }
            if (++pocketStampValue == int.MaxValue) { Array.Clear(pocketStamp, 0, pocketStamp.Length); pocketStampValue = 1; }
            int[] reachedAt = pocketStamp;
            int mark = pocketStampValue;
            var stack = pocketStack;
            stack.Clear();
            foreach (int c in dt.CellIndices()) {
                if (label[c] != Outside) continue;
                for (int i = 0; i < 4; ++i)
                    if (dt.Neighbour(c, i) < 0) { reachedAt[c] = mark; stack.Push(c); break; }
            }
            while (stack.Count > 0) {
                int c = stack.Pop();
                for (int i = 0; i < 4; ++i) {
                    int n = dt.Neighbour(c, i);
                    if (n >= 0 && reachedAt[n] != mark && label[n] == Outside) { reachedAt[n] = mark; stack.Push(n); }
                }
            }
            int count = 0;
            foreach (int c in dt.CellIndices())
                if (label[c] == Outside && reachedAt[c] != mark) {
                    label[c] = Inside;
                    flipped?.Add(c);
                    count++;
                }
            Counts["pocket_cells"] = (Counts.TryGetValue("pocket_cells", out long pc) ? pc : 0) + count;
            return count;
        }

        // cells incident to each vertex (CSR), built for the triangulation starsOf after starsInsertions successful insertions
        // into it (only insertions change it: the manifold step changes labels)
        int[] starStart = new int[0], starCells = new int[0], starFill = new int[0];
        long starsInsertions = -1;
        Delaunay3 starsOf;
        void build_stars()
        {
            if (starsOf == dt && starsInsertions == insertions)
                return;
            starsOf = dt;
            starsInsertions = insertions;
            int nv = dt.VertexCount;
            if (starStart.Length < nv + 1) starStart = new int[Math.Max(nv + 1, 2 * starStart.Length)];
            else Array.Clear(starStart, 0, nv + 1);
            foreach (int c in dt.CellIndices())
                for (int j = 0; j < 4; ++j) starStart[dt.CellVertex(c, j) + 1]++;
            for (int v = 0; v < nv; ++v) starStart[v + 1] += starStart[v];
            if (starCells.Length < starStart[nv]) starCells = new int[Math.Max(starStart[nv], 2 * starCells.Length)];
            if (starFill.Length < nv) starFill = new int[Math.Max(nv, 2 * starFill.Length)];
            else Array.Clear(starFill, 0, nv);
            int[] fill = starFill;
            foreach (int c in dt.CellIndices())
                for (int j = 0; j < 4; ++j) {
                    int v = dt.CellVertex(c, j);
                    starCells[starStart[v] + fill[v]++] = c;
                }
        }

        /// <summary>
        /// Moves outside cells to the inside until every edge has at most two result facets and the result
        /// facets around every vertex form one disk: around a non-manifold edge or vertex, the outside cells are
        /// grouped into connected runs (through facets containing the edge / vertex); the run connected to the
        /// box boundary, otherwise the largest one, stays outside. If the inside cells around a vertex are not
        /// connected, outside cells move inside one at a time (cells joining inside components, small cells and
        /// cells without a box corner first). Only inside is added, so the input stays enclosed;
        /// every flip moves a cell inside for good, so this terminates. Vertices are checked from a work list (the
        /// vertices on the result, then those of flipped cells).
        /// </summary>
        // Success, Failed (no manifold result within the step limit) or Cancelled
        WrapStatus make_manifold(List<int> seedVertices = null)
        {
            build_stars();      // (the triangulation does not change here, only labels)
            long flips = 0, processed = 0;
            var queued = new bool[dt.VertexCount];
            var work = new Queue<int>();
            Action<int> enqueue_cell = (c) => {
                for (int j = 0; j < 4; ++j) {
                    int v = dt.CellVertex(c, j);
                    if (!queued[v]) { queued[v] = true; work.Enqueue(v); }
                }
            };
            // start with the vertices on the result (or the given ones: a local trial); a flip queues the vertices of the
            // flipped cell again
            if (seedVertices != null) {
                foreach (int v in seedVertices)
                    if (v >= 0 && v < queued.Length && !queued[v]) { queued[v] = true; work.Enqueue(v); }
            } else {
                foreach (int c in dt.CellIndices()) {
                    if (label[c] != Inside) continue;
                    for (int i = 0; i < 4; ++i)
                        if (label_of(dt.Neighbour(c, i)) == Outside) { enqueue_cell(c); break; }
                }
            }
            // (the edges v-x of the result facets around v and their facet counts, in the order first reached: the order the
            // dictionary of before enumerated them in; edgeSlot[x] is x's entry while edgeStamp[x] is the current check's)
            if (edgeSlot.Length < dt.VertexCount) {
                edgeSlot = new int[Math.Max(dt.VertexCount, 2 * edgeSlot.Length)];
                edgeStamp = new int[edgeSlot.Length];
                edgeStampValue = 0;
            }
            long limit = 100L * dt.VertexCount + 1000;
            while (true) {
                while (work.Count > 0) {
                    if (++processed > limit)
                        return WrapStatus.Failed;
                    if ((processed & 1023) == 0 && cancelled())
                        return WrapStatus.Cancelled;
                    int v = work.Dequeue();
                    queued[v] = false;
                    flippedCells.Clear();
                    // edges v-x: result facets around them (all contain v, so they are facets of the star of v)
                    edgeX.Clear(); edgeN.Clear();
                    if (++edgeStampValue == int.MaxValue) { Array.Clear(edgeStamp, 0, edgeStamp.Length); edgeStampValue = 1; }
                    // (and the result facets' link edges x-y, for the disk test below; hull: a facet through v on the box)
                    bool hull = false;
                    linkA.Clear(); linkB.Clear();
                    for (int k = starStart[v]; k < starStart[v + 1]; ++k) {
                        int c = starCells[k];
                        for (int i = 0; i < 4; ++i) {
                            if (dt.CellVertex(c, i) == v) continue;
                            int nb = dt.Neighbour(c, i);
                            if (nb < 0) hull = true;
                            if (label[c] != Inside || label_of(nb) != Outside) continue;
                            int first = -1;
                            for (int j = 0; j < 4; ++j) {
                                int x = dt.CellVertex(c, j);
                                if (j == i || x == v) continue;
                                if (edgeStamp[x] != edgeStampValue) {
                                    edgeStamp[x] = edgeStampValue;
                                    edgeSlot[x] = edgeX.Count;
                                    edgeX.Add(x); edgeN.Add(0);
                                }
                                edgeN[edgeSlot[x]]++;
                                if (first < 0) first = edgeSlot[x];
                                else { linkA.Add(first); linkB.Add(edgeSlot[x]); }
                            }
                        }
                    }
                    for (int e = 0; e < edgeX.Count; ++e)
                        if (edgeN[e] > 2) fix_star(v, edgeX[e]);
                    // the vertex (once its edges are manifold; otherwise it is queued again by the flips), unless its result
                    // facets form one disk (link_is_one_cycle): then the star has one inside and one outside component and
                    // fix_star would change nothing
                    if (flippedCells.Count == 0 && !(edgeX.Count == 0 || !hull && link_is_one_cycle()))
                        fix_star(v, -1);
                    flips += flippedCells.Count;
                    foreach (int c in flippedCells)
                        enqueue_cell(c);
                }
                flippedCells.Clear();
                remove_pockets(flippedCells);
                if (flippedCells.Count == 0)
                    break;
                foreach (int c in flippedCells)
                    enqueue_cell(c);
            }
            Counts["manifold_flips"] = flips;
            Counts["manifold_vertex_checks"] = processed;
            return WrapStatus.Success;
        }

        readonly List<int> flippedCells = new List<int>();
        // the link of a vertex v away from the box: a triangulated sphere, whose triangles are the cells of the star and whose
        // edges are their facets through v. The result facets through v are link edges between an inside and an outside
        // triangle. If every link vertex x has exactly two of them (edge v-x on two result facets) and they are connected,
        // they form one simple closed curve, which cuts the sphere into two connected regions (Jordan): one inside and one
        // outside component of the star, so the vertex is manifold
        readonly List<int> linkA = new List<int>(), linkB = new List<int>(), linkParent = new List<int>();
        bool link_is_one_cycle()
        {
            int n = edgeX.Count;
            for (int e = 0; e < n; ++e)
                if (edgeN[e] != 2) return false;
            linkParent.Clear();
            for (int e = 0; e < n; ++e) linkParent.Add(e);
            int parts = n;
            for (int k = 0; k < linkA.Count; ++k) {
                int a = link_root(linkA[k]), b = link_root(linkB[k]);
                if (a != b) { linkParent[a] = b; parts--; }
            }
            return parts == 1;
        }
        int link_root(int e)
        {
            while (linkParent[e] != e) { linkParent[e] = linkParent[linkParent[e]]; e = linkParent[e]; }
            return e;
        }
        int[] edgeSlot = new int[0], edgeStamp = new int[0];
        int edgeStampValue;
        readonly List<int> edgeX = new List<int>(), edgeN = new List<int>();

        static long edge_key(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        // star of vertex v (w < 0) or of edge v-w: cells containing it, connected through facets containing it
        readonly List<int> local = new List<int>();
        // the position of cell c in local while localStamp[c] is the current star's (localStampValue)
        int[] localPos = new int[0], localStamp = new int[0];
        int localStampValue;
        bool local_index(int c, out int k)
        {
            if (c < localStamp.Length && localStamp[c] == localStampValue) { k = localPos[c]; return true; }
            k = -1;
            return false;
        }
        readonly List<int> comp = new List<int>();
        readonly List<byte> compLabel = new List<byte>();
        readonly List<bool> compExterior = new List<bool>();
        // (the volume of a component only when the choice needs it: the sum of its cells' volumes in the order the search
        // reached them, compOrder[compBegin[id] ..], as it was summed during the search before; NaN until computed)
        readonly List<double> compVolume = new List<double>();
        readonly List<int> compOrder = new List<int>(), compBegin = new List<int>();
        double comp_volume(int id)
        {
            if (double.IsNaN(compVolume[id])) {
                double sum = 0;
                int end = id + 1 < compBegin.Count ? compBegin[id + 1] : compOrder.Count;
                for (int j = compBegin[id]; j < end; ++j) sum += dt.CellVolume(local[compOrder[j]]);
                compVolume[id] = sum;
            }
            return compVolume[id];
        }
        readonly List<bool> compCorner = new List<bool>();
        readonly Stack<int> compStack = new Stack<int>();

        int fix_star(int v, int w)
        {
            local.Clear();
            if (localStamp.Length < dt.MaxCellID) {
                localStamp = new int[Math.Max(dt.MaxCellID, 2 * localStamp.Length)];
                localPos = new int[localStamp.Length];
                localStampValue = 0;
            }
            if (++localStampValue == int.MaxValue) { Array.Clear(localStamp, 0, localStamp.Length); localStampValue = 1; }
            for (int k = starStart[v]; k < starStart[v + 1]; ++k) {
                int c = starCells[k];
                if (w >= 0 && dt.VertexIndexIn(c, w) < 0) continue;
                localStamp[c] = localStampValue;
                localPos[c] = local.Count;
                local.Add(c);
            }
            int n = local.Count;
            if (n == 0) return 0;
            int flipped = 0;
            for (int iter = 0; iter <= n; ++iter) {
                star_components(v, w);
                // the box boundary is outside: outside components touching it are one component (with it), and
                // an inside component touching it has the box boundary as outside neighbour
                int nIn = 0, nOut = 0;
                bool exterior = false;
                for (int k = 0; k < compLabel.Count; ++k) {
                    if (compExterior[k]) exterior = true;
                    if (compLabel[k] == Inside) nIn++;
                    else if (!compExterior[k]) nOut++;
                }
                if (exterior) nOut++;
                if (nIn == 0 || nOut == 0 || (nIn == 1 && nOut == 1))
                    return flipped;       // not on the result, or manifold
                if (nOut > 1) {
                    // keep the outside connected to the box boundary, otherwise the outside component with a box corner
                    // (moving it inside would put the corner on the result), otherwise the largest one
                    int keep = -1;
                    for (int k = 0; k < compLabel.Count; ++k) {
                        if (compLabel[k] != Outside || exterior) continue;
                        if (keep < 0 || compCorner[k] && !compCorner[keep] || compCorner[k] == compCorner[keep] && comp_volume(k) > comp_volume(keep)) keep = k;
                    }
                    for (int k = 0; k < n; ++k) {
                        int ck = comp[k];
                        if (compLabel[ck] != Outside) continue;
                        bool stays = exterior ? compExterior[ck] : ck == keep;
                        if (!stays) { label[local[k]] = Inside; flippedCells.Add(local[k]); flipped++; }
                    }
                    continue;
                }
                // several inside components, one outside: move one outside cell inside, preferring one that
                // joins inside components, then small cells and cells without a box corner (which would put
                // the corner on the result)
                int best = -1;
                bool bestCorner = false, bestJoins = false;
                double bestVolume = 0;
                for (int k = 0; k < n; ++k) {
                    int c = local[k];
                    if (label[c] != Outside) continue;
                    int firstIn = -1, joins = 0, touches = 0;
                    for (int i = 0; i < 4; ++i) {
                        int cvx = dt.CellVertex(c, i);
                        if (cvx == v || cvx == w) continue;
                        int m = dt.Neighbour(c, i);
                        if (m < 0 || label[m] != Inside || !local_index(m, out int mk)) continue;
                        touches = 1;
                        if (firstIn < 0) firstIn = comp[mk];
                        else if (comp[mk] != firstIn) joins = 1;
                    }
                    if (touches == 0) continue;
                    bool corner = false;
                    for (int i = 0; i < 4; ++i) if (dt.CellVertex(c, i) < 8) corner = true;
                    double volume = dt.CellVolume(c);
                    if (best < 0 || preferred_flip(corner, joins == 1, volume, bestCorner, bestJoins, bestVolume)) {
                        best = k; bestCorner = corner; bestJoins = joins == 1; bestVolume = volume;
                    }
                }
                if (best < 0)
                    return flipped;
                label[local[best]] = Inside;
                flippedCells.Add(local[best]);
                flipped++;
            }
            return flipped;
        }

        /// <summary>
        /// The order of the manifold step's candidates for a move inside (a before b): no box corner first, then one that
        /// joins inside components, then the smaller volume. Compared as a key, not as a sum with volume / (1 + volume),
        /// which rounded to 1 for volumes above about 2^53 (large coordinates) and then tied, and depended on the unit
        /// </summary>
        internal static bool preferred_flip(bool cornerA, bool joinsA, double volumeA, bool cornerB, bool joinsB, double volumeB)
        {
            if (cornerA != cornerB) return !cornerA;
            if (joinsA != joinsB) return joinsA;
            return volumeA < volumeB;
        }

        // connected components of equal labels in the star (local), and whether they touch the box boundary
        void star_components(int v, int w)
        {
            int n = local.Count;
            comp.Clear();
            for (int k = 0; k < n; ++k) comp.Add(-1);
            compLabel.Clear(); compExterior.Clear(); compVolume.Clear(); compCorner.Clear();
            compOrder.Clear(); compBegin.Clear();
            var stack = compStack;
            for (int k = 0; k < n; ++k) {
                if (comp[k] >= 0) continue;
                int id = compLabel.Count;
                byte lab = label[local[k]];
                compLabel.Add(lab); compExterior.Add(false); compVolume.Add(double.NaN); compCorner.Add(false);
                compBegin.Add(compOrder.Count);
                comp[k] = id;
                stack.Push(k);
                while (stack.Count > 0) {
                    int kk = stack.Pop();
                    int c = local[kk];
                    compOrder.Add(kk);
                    if (has_corner(c)) compCorner[id] = true;
                    for (int i = 0; i < 4; ++i) {
                        int cvx = dt.CellVertex(c, i);
                        if (cvx == v || cvx == w) continue;     // facet i does not contain the vertex / edge
                        int m = dt.Neighbour(c, i);
                        if (m < 0) { compExterior[id] = true; continue; }
                        if (local_index(m, out int mk) && comp[mk] < 0 && label[m] == lab) {
                            comp[mk] = id;
                            stack.Push(mk);
                        }
                    }
                }
            }
        }

        // does cell c have a box corner (vertices 0 to 7) as a vertex?
        bool has_corner(int c)
        {
            for (int i = 0; i < 4; ++i)
                if (dt.CellVertex(c, i) < 8) return true;
            return false;
        }
        int boxCornersInResult;

        DMesh3 extract()
        {
            DMesh3 mesh = new DMesh3();
            boxCornersInResult = 0;
            var map = new Dictionary<int, int>();
            int failed = 0;
            foreach (int c in dt.CellIndices()) {
                if (label[c] != Inside) continue;
                for (int i = 0; i < 4; ++i) {
                    if (label_of(dt.Neighbour(c, i)) != Outside) continue;
                    Index3i f = dt.FacetVertices(c, i);
                    Index3i t = Index3i.Zero;
                    for (int j = 0; j < 3; ++j) {
                        if (!map.TryGetValue(f[j], out int vid)) {
                            vid = mesh.AppendVertex(dt.GetVertex(f[j]));
                            map[f[j]] = vid;
                            if (f[j] < 8) boxCornersInResult++;
                        }
                        t[j] = vid;
                    }
                    if (mesh.AppendTriangle(t) < 0) failed++;
                }
            }
            if (failed > 0 && MakeManifold) {
                Error = failed + " result triangles could not be added (non-manifold)";
                return null;
            }
            return mesh;
        }


        /// <summary>
        /// The inside cells of the last successful Compute() as a tetrahedral mesh (docs/design/TET_MESHING.md, stage 0).
        /// The wrap surface is a union of facets of the triangulation, so the inside cells are a valid tetrahedral mesh of the
        /// wrapped volume whose boundary is exactly Result (same vertex positions, no rounding). Delaunay, positive
        /// volumes exactly, but without any quality bound in the interior. Null if there is no successful Result.
        /// </summary>
        public TetMesh3 ExtractTetMesh()
        {
            if (Result == null || Status != WrapStatus.Success || dt == null)
                return null;
            var tm = new TetMesh3();
            var map = new Dictionary<int, int>();
            Func<int, int> vid = (v) => {
                if (!map.TryGetValue(v, out int id)) { id = tm.Vertices.Count; tm.Vertices.Add(dt.GetVertex(v)); map[v] = id; }
                return id;
            };
            foreach (int c in dt.CellIndices()) {
                if (label[c] != Inside) continue;
                Index4i cv = dt.GetCell(c);
                tm.Tets.Add(new Index4i(vid(cv.a), vid(cv.b), vid(cv.c), vid(cv.d)));
                for (int i = 0; i < 4; ++i) {
                    if (label_of(dt.Neighbour(c, i)) != Outside) continue;
                    Index3i f = dt.FacetVertices(c, i);
                    tm.BoundaryTriangles.Add(new Index3i(vid(f.a), vid(f.b), vid(f.c)));
                }
            }
            return tm;
        }

        /// <summary> max-heap of gates (facet i of inside cell c, with the cell's stamp), by facet radius </summary>
        class GateQueue
        {
            double[] key = new double[256];
            int[] cell = new int[256], facet = new int[256], stamp = new int[256];
            int count = 0;
            public int Count { get { return count; } }
            public void Clear() { count = 0; }

            public void Push(double k, int c, int f, int s)
            {
                if (count == key.Length) {
                    int n = 2 * count;
                    Array.Resize(ref key, n); Array.Resize(ref cell, n); Array.Resize(ref facet, n); Array.Resize(ref stamp, n);
                }
                int i = count++;
                while (i > 0) {
                    int p = (i - 1) / 2;
                    if (key[p] >= k) break;
                    move(p, i);
                    i = p;
                }
                key[i] = k; cell[i] = c; facet[i] = f; stamp[i] = s;
            }

            public void Pop(out double k0, out int c, out int f, out int s)
            {
                k0 = key[0]; c = cell[0]; f = facet[0]; s = stamp[0];
                count--;
                if (count == 0) return;
                double k = key[count];
                int lc = cell[count], lf = facet[count], ls = stamp[count];
                int i = 0;
                while (true) {
                    int l = 2 * i + 1;
                    if (l >= count) break;
                    int r = l + 1;
                    int m = (r < count && key[r] > key[l]) ? r : l;
                    if (key[m] <= k) break;
                    move(m, i);
                    i = m;
                }
                key[i] = k; cell[i] = lc; facet[i] = lf; stamp[i] = ls;
            }

            void move(int from, int to)
            {
                key[to] = key[from]; cell[to] = cell[from]; facet[to] = facet[from]; stamp[to] = stamp[from];
            }
        }
    }
}
