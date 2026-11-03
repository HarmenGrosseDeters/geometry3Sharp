// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// One toe line with its slopes (docs/design/TERRAIN.md 6). Values per segment end, so that a jump at a vertex is
    /// possible; in phase 1a the start and end value of a segment must be equal (one slope per segment).
    /// </summary>
    public class SlopeToe
    {
        /// <summary> Left / Right of the direction of travel for open toes, Inside / Outside for closed ones </summary>
        public enum Sides { Left, Right, Inside, Outside }

        /// <summary> the toe points; a closed toe does not repeat its first point </summary>
        public List<Vector3d> Points = new List<Vector3d>();
        public bool Closed;
        /// <summary> the side the slope is built on (required, never inferred) </summary>
        public Sides Side;
        /// <summary> per segment (segment i from point i to i + 1): the cut slope at its start and at its end; null = no cut </summary>
        public SlopeSpec[] CutStart, CutEnd;
        /// <summary> likewise for fill </summary>
        public SlopeSpec[] FillStart, FillEnd;

        public int SegmentCount { get { return Points == null ? 0 : (Closed ? Points.Count : Math.Max(0, Points.Count - 1)); } }

        /// <summary> one slope along the whole toe, for cut and fill (the simple standard) </summary>
        public static SlopeToe Constant(List<Vector3d> points, bool closed, Sides side, SlopeSpec slope)
        {
            var t = new SlopeToe() { Points = points, Closed = closed, Side = side };
            int n = t.SegmentCount;
            t.CutStart = Enumerable.Repeat(slope, n).ToArray();
            t.CutEnd = (SlopeSpec[])t.CutStart.Clone();
            t.FillStart = (SlopeSpec[])t.CutStart.Clone();
            t.FillEnd = (SlopeSpec[])t.CutStart.Clone();
            return t;
        }
    }

    /// <summary> a level line of the slope at a target height (TerrainSlopeDesign.TargetHeightLines) </summary>
    public class TargetHeightLine
    {
        public double Height;
        public List<Vector3d> Points = new List<Vector3d>();
        public bool Closed;

        /// <summary>
        /// the level lines of a TIN at height h: per triangle the segment between the crossings of its edges with z = h
        /// (a vertex at h counts as above, so each crossing is on an edge with one end below, and every triangle has none
        /// or two), the point on an edge interpolated once for both of its triangles; chained across shared edges, open
        /// lines (from boundary to boundary) first, then the closed ones; each line runs with the higher side on its left
        /// </summary>
        public static List<TargetHeightLine> LevelLines(DMesh3 m, double h)
        {
            var result = new List<TargetHeightLine>();
            if (!MathUtil.IsFinite(h)) return result;
            Func<int, bool> above = (v) => m.GetVertex(v).z >= h;
            var pointOf = new Dictionary<int, Vector3d>();
            Func<int, Vector3d> crossing = (e) => {
                Vector3d p;
                if (pointOf.TryGetValue(e, out p)) return p;
                Index2i ev = m.GetEdgeV(e);
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                if (ev.a > ev.b) { Vector3d t = a; a = b; b = t; }     // (the same for both triangles: from the lower id)
                // (an end exactly at h is the point itself: one of the two ends is below, the other at h or above)
                if (a.z == h) p = new Vector3d(a.x, a.y, h);
                else if (b.z == h) p = new Vector3d(b.x, b.y, h);
                else {
                    double s = (h - a.z) / (b.z - a.z);
                    p = new Vector3d(a.x + s * (b.x - a.x), a.y + s * (b.y - a.y), h);
                }
                pointOf[e] = p;
                return p;
            };
            // per triangle: the directed segment (from edge, to edge), the higher side on the left
            var next = new Dictionary<int, int>();     // from edge -> to edge
            var hasIn = new HashSet<int>();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t), te = m.GetTriEdges(t);
                int[] v = { tv.a, tv.b, tv.c }, e = { te.a, te.b, te.c };
                // (counter-clockwise in XY: the edges e[i] = (v[i], v[i+1]))
                if (ExactPredicates.Orient2d(m.GetVertex(v[0]).xy, m.GetVertex(v[1]).xy, m.GetVertex(v[2]).xy) < 0) {
                    v = new[] { tv.a, tv.c, tv.b }; e = new[] { te.c, te.b, te.a };
                }
                int from = -1, to = -1;
                for (int i = 0; i < 3; ++i) {
                    bool a0 = above(v[i]), a1 = above(v[(i + 1) % 3]);
                    // (walking counter-clockwise: entering the higher side's region on the left when the edge goes up)
                    if (a0 == false && a1) to = e[i];
                    else if (a0 && a1 == false) from = e[i];
                }
                if (from < 0 || to < 0) continue;
                next[from] = to;
                hasIn.Add(to);
            }
            var starts = next.Keys.Where(k => hasIn.Contains(k) == false).OrderBy(k => k).ToList();
            var used = new HashSet<int>();
            Action<int, bool> walk = (e0, closed) => {
                var line = new TargetHeightLine() { Height = h, Closed = closed };
                int e = e0;
                line.Points.Add(crossing(e));
                while (next.ContainsKey(e) && used.Add(e)) {
                    e = next[e];
                    if (closed && e == e0) break;
                    Vector3d q = crossing(e);
                    // (two crossings at one vertex at h: one point)
                    if (q != line.Points[line.Points.Count - 1]) line.Points.Add(q);
                }
                if (closed && line.Points.Count > 1 && line.Points[0] == line.Points[line.Points.Count - 1]) line.Points.RemoveAt(line.Points.Count - 1);
                result.Add(line);
            };
            foreach (int s0 in starts) walk(s0, false);
            foreach (int k in next.Keys.OrderBy(k => k).ToList())
                if (used.Contains(k) == false) walk(k, true);
            return result;
        }
    }

    /// <summary>
    /// A toe piece: the stable reference of the slope triangles that it supports (the group id of a triangle in
    /// TerrainSlopeDesign.SlopeSurface is the index of its piece).
    /// </summary>
    public class SlopePiece
    {
        public int Toe, Segment;
        /// <summary> the toe points at the ends of the piece, as specified </summary>
        public Vector3d P0, P1;
        public SlopeSpec Spec;
        /// <summary> Cut or Fill: the run of the piece (both in a mixed run, Direction Auto) </summary>
        public TerrainSlopeDesign.Directions Direction = TerrainSlopeDesign.Directions.Cut;
        /// <summary>
        /// with a terrain: the volume between the terrain and this piece's slope over the region (cut: excavated; fill:
        /// filled), the region's plan area and its area on the slope surface (TERRAIN.md 5.7)
        /// </summary>
        public double Volume, PlanArea, SlopeArea;
    }

    /// <summary>
    /// Slope surface (Böschung) from toe lines, as the lower envelope of the slope cones with apexes on the toe (cut;
    /// the upper envelope of the falling cones for fill): docs/design/TERRAIN.md, phase 1a
    /// (docs/design/TERRAIN_1A_PLAN.md). Mode: cone envelope (R1). Without a terrain the surface is built up to
    /// HeightLimit (cut) or down to it (fill); it ends at HeightLimit - UsedToeLowering (+ for fill), never beyond
    /// HeightLimit (the whole map is lowered by the toe lowering, the cap with it, so its lines stay). With a terrain (phase 1b) the catch lines
    /// and the composite follow.
    ///
    /// Never steeper than specified: every triangle of SlopeSurface is checked exactly, on its stored doubles, before
    /// Compute returns true: it is not steeper than the spec of the toe piece it is attributed to (its group id), and its
    /// plane lies on or below both end points of that piece (cut; on or above for fill), so it lies below every slope cone
    /// of the piece (Slope.IsSupportedBy; for fill on the triangle and the toe points mirrored by z -> -z). Compute never
    /// returns a violating triangle: it fails with SlopeViolation and ProblemTriangles. The guarantee holds for the doubles
    /// in memory; an export must write them with round-trip formatting.
    ///
    /// Conservative: the surface lies at most Tolerance below the exact cone envelope (cut). The toe vertices of the
    /// output lie UsedToeLowering below the specified toe (cut; above for fill): the margin the exact check of the
    /// support needs, measured on the output (1e-6 m at UTM in the tests, 3e-10 m near the origin), at most about
    /// 1.1 x SlopeMargin x slope x reach.
    /// </summary>
    public class TerrainSlopeDesign
    {
        public enum Directions { Auto, Cut, Fill }
        public enum Statuses
        {
            Success,
            /// <summary> a toe, a slope or a parameter is invalid: Message, ToeProblems </summary>
            InvalidInput,
            /// <summary> not implemented: a slope that changes continuously along a segment </summary>
            NotImplemented,
            /// <summary> a toe segment rises (or falls) at least as steeply as its slope: ToeProblems </summary>
            ToeSteeperThanSlope,
            /// <summary> the cone of another part of the toe passes below a toe segment by more than Tolerance: ToeProblems </summary>
            ToeUndercut,
            /// <summary> the exact check failed on ProblemTriangles (never a violating result) </summary>
            SlopeViolation,
            /// <summary>
            /// the slope does not reach the terrain within its footprint (or below a given HeightLimit): the results are
            /// kept, but partial (IsComplete = false); the catch lines that end there say so
            /// </summary>
            NoDaylight,
            /// <summary> the terrain is not a height field: triangles degenerate in XY, or overlapping in XY under the slope surface </summary>
            NotHeightField,
            /// <summary>
            /// the composite could not be built (Message): SlopeSurface and CatchLines are kept and complete, Composite and
            /// SlopeRegion are null
            /// </summary>
            CompositeFailed,
            Cancelled,
            /// <summary> an internal error (Message); no result </summary>
            Failed,
            /// <summary>
            /// RequiredCatchLineAccuracy is not reached, also with a smaller Tolerance (TerrainVerticalUncertainty or a
            /// tangential contact alone exceeds it): CatchLines are kept with their uncertainties, the rest is not returned
            /// </summary>
            CatchLineAccuracyNotReached,
            /// <summary>
            /// Direction Auto: the fill surface rises above a toe segment by more than Tolerance (the mirror of ToeUndercut;
            /// with it the fill surface would pass above the cut surface, TERRAIN.md appendix A): ToeProblems
            /// </summary>
            ToeOvertopped
        }

        // inputs
        public List<SlopeToe> Toes = new List<SlopeToe>();
        /// <summary>
        /// optional: the terrain (a TIN, height field). With it the catch lines are computed (TERRAIN.md 5.5), and
        /// HeightLimit may be NaN: cut then builds up to the highest terrain near the toe, raised while the slope
        /// reaches it, at most to the terrain's highest vertex (fill: down to its lowest). Only the terrain within the
        /// box of the slope surface must be a height field (else NotHeightField); outside it the composite copies the
        /// terrain as given (but an overlap of a cut terrain triangle, which can reach beyond that box, with one outside
        /// it is found by the composite's check: CompositeFailed)
        /// </summary>
        public DMesh3 Terrain;
        /// <summary> with Terrain: also build SlopeRegion and Composite (or Replacement) </summary>
        public bool BuildComposite = false;
        /// <summary> the output of BuildComposite: the whole changed terrain, or only the part that replaces terrain triangles </summary>
        public enum CompositeKinds
        {
            /// <summary> Composite: the whole terrain with the region replaced </summary>
            Full,
            /// <summary>
            /// Replacement: only the replaced part (SlopeRegion and the pieces of the cut terrain triangles), with
            /// ReplacedTerrainTriangles and CompositeTerrainVertices to join it to the rest of the terrain; much less
            /// output than Full on a large terrain
            /// </summary>
            Replacement
        }
        /// <summary> with BuildComposite: Full (default) or Replacement </summary>
        public CompositeKinds CompositeKind = CompositeKinds.Full;
        public Directions Direction = Directions.Cut;
        /// <summary>
        /// required without Terrain: cut builds the slope up to this height, fill down to it (it ends UsedToeLowering
        /// below it, for fill above)
        /// </summary>
        public double HeightLimit = double.NaN;
        /// <summary> required: the largest vertical deviation below the exact cone envelope (data units) </summary>
        public double Tolerance = double.NaN;
        /// <summary> the relative slope margin δ: the planes are built with slope s (1 - δ); raised if the check needs it </summary>
        public double SlopeMargin = 1e-6;
        /// <summary> with a terrain: its vertical uncertainty (data units, default 0), part of the catch line's uncertainty </summary>
        public double TerrainVerticalUncertainty = 0;
        /// <summary>
        /// with a terrain: catch-line segments whose horizontal uncertainty exceeds it are reported (IllConditionedCatchLine);
        /// default: none (an application might use 0.1 m)
        /// </summary>
        public double CatchLineWarningThreshold = double.PositiveInfinity;
        /// <summary>
        /// with a terrain: the horizontal accuracy every catch-line segment must have (NaN: none). Reached by a smaller
        /// Tolerance (UsedTolerance; the whole design again, at most three times: Attempts, PhaseSeconds and the
        /// statistics count all rounds), else CatchLineAccuracyNotReached (also when a smaller Tolerance fails)
        /// </summary>
        public double RequiredCatchLineAccuracy = double.NaN;
        /// <summary>
        /// optional: heights at which the level lines of the slope are returned (TargetHeightLines; the hook for berms,
        /// TERRAIN.md 10)
        /// </summary>
        public List<double> TargetHeights;
        public Func<bool> CancelF = () => false;

        // outputs
        public Statuses Status;
        /// <summary>
        /// what kind of result Status is (TERRAIN.md 5.6), so that "not supported by the phase 1 method" is never taken
        /// for "geometrically impossible"
        /// </summary>
        public enum StatusKinds
        {
            /// <summary> Success, or NoDaylight (a partial result, IsComplete = false; not a failure) </summary>
            None,
            /// <summary> InvalidInput, NotHeightField: the input is wrong </summary>
            InvalidInput,
            /// <summary>
            /// the phase 1 method (Euclidean cone envelope) cannot handle this design, which is not a proof that it is
            /// impossible: NotImplemented; ToeUndercut where the cone that causes it reaches the toe through the design area
            /// </summary>
            UnsupportedByMethod,
            /// <summary>
            /// no surface meets the specification, in any mode: ToeSteeperThanSlope; ToeUndercut where the path from the
            /// causing part of the toe runs outside the design area, on the slope side
            /// </summary>
            GeometricallyInfeasible,
            /// <summary> the computation could not certify its own result: SlopeViolation, CatchLineAccuracyNotReached </summary>
            NotCertified,
            /// <summary> an internal error: Failed, CompositeFailed </summary>
            Internal,
            Cancelled
        }
        /// <summary> the kind of Status </summary>
        public StatusKinds StatusKind;
        public string Message = "";
        /// <summary> the slope triangles; group id = index into Pieces. Null if Compute failed </summary>
        public DMesh3 SlopeSurface;
        public List<SlopePiece> Pieces = new List<SlopePiece>();
        /// <summary> with Terrain: where the slope meets the terrain, on the slope surface (closed, or with end reasons) </summary>
        public List<CatchLine> CatchLines = new List<CatchLine>();
        /// <summary>
        /// with Terrain and BuildComposite: the slope surface clipped to the region it excavates (cut) or fills, every
        /// triangle checked exactly against its piece (group id = index into Pieces)
        /// </summary>
        public DMesh3 SlopeRegion;
        /// <summary>
        /// with Terrain and BuildComposite: the terrain with the region replaced by SlopeRegion. Joined through shared
        /// vertices along the catch lines; the slope ends at the lowered toe (z - UsedToeLowering), with a step to the
        /// terrain there and at the lateral limits, where the caller's design goes
        /// </summary>
        public DMesh3 Composite;
        /// <summary>
        /// with Terrain, BuildComposite and CompositeKind Replacement (else null): the part of the composite that replaces
        /// the terrain triangles ReplacedTerrainTriangles. Together with the other terrain triangles, joined at the
        /// vertices that CompositeTerrainVertices names, it is the Full composite (the same triangles and vertex positions)
        /// </summary>
        public DMesh3 Replacement;
        /// <summary> with BuildComposite: the terrain triangles that the composite replaces (cut by the region), sorted </summary>
        public List<int> ReplacedTerrainTriangles = new List<int>();
        /// <summary>
        /// with BuildComposite: per vertex of Composite or Replacement, the terrain vertex at its position, or -1 (one per
        /// position: of terrain vertices that share a position, an unwelded TIN, one is named; TerrainCompositeVertices
        /// names all)
        /// </summary>
        public int[] CompositeTerrainVertices;
        /// <summary>
        /// with BuildComposite: per terrain vertex, the vertex of Composite or Replacement at its position, or -1. Joining
        /// Replacement with the terrain triangles not replaced: a vertex of them is this vertex where it is not -1
        /// </summary>
        public int[] TerrainCompositeVertices;
        /// <summary>
        /// with a terrain: the volume between the terrain and the slope surface over the region (cut: the terrain above
        /// the slope, excavated; fill: the slope above the terrain, filled), its plan area and its area on the slope
        /// surface; the sums of Pieces (TERRAIN.md 5.7). Integrated on the overlay, in doubles; the region inside the toe
        /// (the caller's design) is not included
        /// </summary>
        public double Volume, PlanArea, SlopeArea;
        /// <summary>
        /// with a terrain: the cut and the fill part of Volume and PlanArea (Direction Auto both, else the one of Direction;
        /// TERRAIN.md 7)
        /// </summary>
        public double CutVolume, FillVolume, CutArea, FillArea;
        /// <summary> the catch-line segments (line, segment) whose uncertainty exceeds CatchLineWarningThreshold </summary>
        public List<Index2i> IllConditionedCatchLine = new List<Index2i>();
        /// <summary> the catch-line segments (line, segment) where the slope and the terrain coincide (tangential or planar contact) </summary>
        public List<Index2i> TangentialContact = new List<Index2i>();
        /// <summary>
        /// the level lines of the slope at TargetHeights: of SlopeRegion where it is built (the slope as it is in the
        /// composite), else of SlopeSurface. Per height in the order given, polylines in the plane z = height, closed where
        /// they close, open where the slope ends (at a catch line, a lateral limit or the toe)
        /// </summary>
        public List<TargetHeightLine> TargetHeightLines = new List<TargetHeightLine>();
        /// <summary> the Tolerance used: Tolerance, or smaller to reach RequiredCatchLineAccuracy </summary>
        public double UsedTolerance;
        /// <summary> false if the results are partial (NoDaylight) or missing </summary>
        public bool IsComplete;
        /// <summary> (toe, segment) of the problems of InvalidInput, ToeSteeperThanSlope, ToeUndercut </summary>
        public List<Index2i> ToeProblems = new List<Index2i>();
        /// <summary>
        /// SlopeViolation: the failing triangles of the last attempt (ids of the slope surface of that attempt or, if
        /// the composite's slope triangles failed, of its SlopeRegion; neither is returned)
        /// </summary>
        public List<int> ProblemTriangles = new List<int>();
        /// <summary>
        /// an upper bound of slope / spec slope over the triangles, at most 1 on success: the double ratio rounded up
        /// where an exact check confirms it, else 1 (needles, whose double ratio is unreliable, are proven by the
        /// exact check against the spec only)
        /// </summary>
        public double MaxSlopeRatio;
        public double UsedSlopeMargin, UsedToeLowering, UsedMinFeatureSize;
        /// <summary> the angle between neighbouring cone tangent planes (radians) </summary>
        public double FacetAngle;
        /// <summary>
        /// diagnostics: seconds per phase (planes, domain, insert, simplify, realize, check; summed over the builds),
        /// the builds (more than 1 if the check raised a margin), the predicate calls of the kernel, those that needed
        /// its exact stage, and the constructions (vertex positions and heights, all exact)
        /// </summary>
        public Dictionary<string, double> PhaseSeconds = new Dictionary<string, double>();
        public int Attempts;
        /// <summary> diagnostics: the realizations (more than the builds if the measured toe lowering was raised) </summary>
        public int Realizations;
        public long PredicateCalls, ExactFallbacks, ExactConstructions;

        public bool Compute()
        {
            Status = Statuses.InvalidInput; Message = "";
            SlopeSurface = null; SlopeRegion = null; Composite = null; IsComplete = false; CatchLines = new List<CatchLine>();
            Replacement = null; ReplacedTerrainTriangles = new List<int>(); CompositeTerrainVertices = null; TerrainCompositeVertices = null;
            Pieces = new List<SlopePiece>(); ToeProblems = new List<Index2i>(); ProblemTriangles = new List<int>();
            MaxSlopeRatio = 0; UsedSlopeMargin = UsedToeLowering = UsedMinFeatureSize = FacetAngle = 0;
            Volume = PlanArea = SlopeArea = 0; CutVolume = FillVolume = CutArea = FillArea = 0;
            lastOverlay = null; lastPieces = null; TargetHeightLines = new List<TargetHeightLine>(); AutoCut = AutoFill = null;
            PhaseSeconds = new Dictionary<string, double>(); Attempts = 0; Realizations = 0; PredicateCalls = ExactFallbacks = ExactConstructions = 0;
            MergedPositions = 0; MergeLambdaRequired = 0; NeedleFlips = 0; LastRealized = null;
            MaxMergeDistance = 0; BandMerges = 0; UsedMergeRadius = UsedBandMergeRadius = 0; AttemptProblemAltitudes = new List<List<double>>();
            // (a cancelled or failed run can leave them set)
            reachedCap = capMayGrow = false;
            undercutThroughDesign = false;
            RayCount = 0; CanonicalProblem = ""; UndercutCauses = new List<SlopeMap.UndercutCause>();
            CapRounds = 0;
            IllConditionedCatchLine = new List<Index2i>(); TangentialContact = new List<Index2i>();
            double givenTolerance = Tolerance;
            UsedTolerance = Tolerance;
            try {
                // (a smaller Tolerance until RequiredCatchLineAccuracy is met, at most 3 times)
                List<CatchLine> previous = null;
                for (int round = 0; ; ++round) {
                    Status = compute();
                    if (Status != Statuses.Success && Status != Statuses.NoDaylight && Status != Statuses.CompositeFailed) {
                        // (a smaller Tolerance that fails, eg below the numerical margins: the accuracy is not reached;
                        // the previous round's catch lines show where)
                        if (round > 0 && Status != Statuses.Cancelled) {
                            Message = $"the catch line's horizontal uncertainty exceeds RequiredCatchLineAccuracy; a smaller Tolerance ({Tolerance:G3}) failed: {Status}: {Message}";
                            Status = Statuses.CatchLineAccuracyNotReached;
                            CatchLines = previous;
                        }
                        break;
                    }
                    double next;
                    int verdict = catch_line_accuracy(out next);
                    if (verdict == 0) break;
                    if (verdict == 2 || round == 3) {
                        Status = fail(Statuses.CatchLineAccuracyNotReached, $"the catch line's horizontal uncertainty exceeds RequiredCatchLineAccuracy" +
                                      (verdict == 2 ? " (the terrain's vertical uncertainty or a tangential contact alone exceeds it)" : ""));
                        break;
                    }
                    previous = CatchLines;
                    Tolerance = next; UsedTolerance = next;
                    IllConditionedCatchLine = new List<Index2i>(); TangentialContact = new List<Index2i>();
                }
            } catch (OperationCanceledException) {
                Status = Statuses.Cancelled;
            } catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is OverflowException) {
                // (an internal inconsistency; never a result)
                Status = Statuses.Failed;
                Message = e.Message;
            } finally {
                Tolerance = givenTolerance;
            }
            if (Status == Statuses.CatchLineAccuracyNotReached) {
                // (the catch lines are kept, with their uncertainties: they show where the accuracy is not reached)
                SlopeSurface = null; SlopeRegion = null; Composite = null; Pieces = new List<SlopePiece>();
                Volume = PlanArea = SlopeArea = 0; CutVolume = FillVolume = CutArea = FillArea = 0;
                Replacement = null; ReplacedTerrainTriangles = new List<int>(); CompositeTerrainVertices = null; TerrainCompositeVertices = null;
            } else if (Status != Statuses.Success && Status != Statuses.NoDaylight && Status != Statuses.CompositeFailed) {
                SlopeSurface = null; SlopeRegion = null; Composite = null; CatchLines = new List<CatchLine>();
                Volume = PlanArea = SlopeArea = 0; CutVolume = FillVolume = CutArea = FillArea = 0;
                Replacement = null; ReplacedTerrainTriangles = new List<int>(); CompositeTerrainVertices = null; TerrainCompositeVertices = null;
            }
            if (Direction != Directions.Auto) {
                bool fillRun = Direction == Directions.Fill;
                CutVolume = fillRun ? 0 : Volume; CutArea = fillRun ? 0 : PlanArea;
                FillVolume = fillRun ? Volume : 0; FillArea = fillRun ? PlanArea : 0;
            }
            if (TargetHeights != null && (Status == Statuses.Success || Status == Statuses.NoDaylight || Status == Statuses.CompositeFailed)) {
                DMesh3 m = SlopeRegion ?? SlopeSurface;
                if (m != null)
                    foreach (double h in TargetHeights)
                        TargetHeightLines.AddRange(TargetHeightLine.LevelLines(m, h));
            }
            IsComplete = Status == Statuses.Success;
            StatusKind = kind_of(Status, undercutThroughDesign);
            return Status == Statuses.Success;
        }

        Statuses fail(Statuses s, string message)
        {
            Message = message;
            return s;
        }

        // ---------------------------------------------------------------- inputs

        sealed class InPiece
        {
            public int Toe, Segment;
            public Vector3d A, B;          // specified toe points (mirrored for fill)
            public Vector3d A1, B1;        // lowered by λ
            public SlopeSpec Spec;
            public double S, Sb;           // S of the spec, the built slope
            public List<int> Strip = new List<int>();
            public List<int> FanA = new List<int>(), FanB = new List<int>();
            public List<double> AngA = new List<double>(), AngB = new List<double>();
            public double Slb;             // lower bound of the gradient along any direction away from the piece
            public AxisAlignedBox2d Reach;
        }

        // the last successful build's overlay and pieces (a mixed run builds its composite from those of its two runs)
        SlopeTerrainOverlay lastOverlay;
        List<InPiece> lastPieces;

        // a run of one direction with this one's inputs (Direction Auto)
        TerrainSlopeDesign run_of(Directions dir)
        {
            return new TerrainSlopeDesign() {
                Toes = Toes, Terrain = Terrain, Direction = dir, HeightLimit = HeightLimit, Tolerance = Tolerance,
                SlopeMargin = SlopeMargin, TerrainVerticalUncertainty = TerrainVerticalUncertainty,
                CatchLineWarningThreshold = CatchLineWarningThreshold, CancelF = CancelF,
                // (each run checks its own clipped region; the mixed composite is built and checked here)
                BuildComposite = BuildComposite, CompositeKind = CompositeKinds.Replacement,
                InsertionOrder = InsertionOrder, InsertionSeed = InsertionSeed, CheckCanonical = CheckCanonical,
                CapEstimateRounds = CapEstimateRounds, RebuildForEachLowering = RebuildForEachLowering
            };
        }

        /// <summary> tests: the two runs of the last Direction Auto (cut, fill) </summary>
        internal TerrainSlopeDesign AutoCut, AutoFill;

        // Direction Auto (TERRAIN_1C_PLAN.md, section 10): cut and fill on the terrain, each a run of its own (pieces,
        // height limit, λ, checks); the final surface is the clamp max(S_fill, min(T, S_cut)): the cut region and the
        // fill region, disjoint where neither run fails its toe check, with one composite of both
        Statuses compute_auto()
        {
            if (Terrain == null)
                return fail(Statuses.InvalidInput, "Direction Auto (mixed cut and fill) needs a Terrain");
            if (double.IsNaN(HeightLimit) == false)
                return fail(Statuses.InvalidInput, "Direction Auto takes the height limits of its two runs from the terrain: HeightLimit must be NaN");
            TerrainSlopeDesign c = run_of(Directions.Cut), f = run_of(Directions.Fill);
            AutoCut = c; AutoFill = f;
            foreach (TerrainSlopeDesign r in new[] { c, f }) {
                checkCancel();
                r.Compute();
                foreach (var kv in r.PhaseSeconds) {
                    double t0;
                    PhaseSeconds.TryGetValue(kv.Key, out t0);
                    PhaseSeconds[kv.Key] = t0 + kv.Value;
                }
                Attempts += r.Attempts; Realizations += r.Realizations; CapRounds += r.CapRounds;
                PredicateCalls += r.PredicateCalls; ExactFallbacks += r.ExactFallbacks; ExactConstructions += r.ExactConstructions;
                if (r.Status == Statuses.Cancelled) throw new OperationCanceledException();
                // (a run's own composite that fails does not stop the mixed one, which is built from both overlays)
                if (r.Status != Statuses.Success && r.Status != Statuses.NoDaylight && !(r.Status == Statuses.CompositeFailed && r.lastOverlay != null)) {
                    ToeProblems = r.ToeProblems; ProblemTriangles = r.ProblemTriangles;
                    undercutThroughDesign = r.undercutThroughDesign; UndercutCauses = r.UndercutCauses;
                    if (r == f && r.Status == Statuses.ToeUndercut)
                        return fail(Statuses.ToeOvertopped, $"fill: {r.ToeProblems.Count} toe segments are overtopped by the fill cones of other parts of the toe");
                    return fail(r.Status, (r == c ? "cut: " : "fill: ") + r.Message);
                }
            }
            int nc = c.Pieces.Count;
            Pieces = c.Pieces.Concat(f.Pieces).ToList();
            SlopeSurface = new DMesh3(false, false, false, true);
            foreach (TerrainSlopeDesign r in new[] { c, f }) {
                int off = r == c ? 0 : nc;
                var map = new Dictionary<int, int>();
                foreach (int v in r.SlopeSurface.VertexIndices()) map[v] = SlopeSurface.AppendVertex(r.SlopeSurface.GetVertex(v));
                foreach (int t in r.SlopeSurface.TriangleIndices()) {
                    Index3i tv = r.SlopeSurface.GetTriangle(t);
                    SlopeSurface.AppendTriangle(map[tv.a], map[tv.b], map[tv.c], r.SlopeSurface.GetTriangleGroup(t) + off);
                }
            }
            CatchLines = c.CatchLines.Concat(f.CatchLines).ToList();
            int lc = c.CatchLines.Count;
            IllConditionedCatchLine = c.IllConditionedCatchLine.Concat(f.IllConditionedCatchLine.Select(x => new Index2i(x.a + lc, x.b))).ToList();
            TangentialContact = c.TangentialContact.Concat(f.TangentialContact.Select(x => new Index2i(x.a + lc, x.b))).ToList();
            CutVolume = c.Volume; CutArea = c.PlanArea; FillVolume = f.Volume; FillArea = f.PlanArea;
            Volume = c.Volume + f.Volume; PlanArea = c.PlanArea + f.PlanArea; SlopeArea = c.SlopeArea + f.SlopeArea;
            MaxSlopeRatio = Math.Max(c.MaxSlopeRatio, f.MaxSlopeRatio);
            UsedSlopeMargin = Math.Max(c.UsedSlopeMargin, f.UsedSlopeMargin);
            UsedToeLowering = Math.Max(c.UsedToeLowering, f.UsedToeLowering);
            UsedMinFeatureSize = Math.Max(c.UsedMinFeatureSize, f.UsedMinFeatureSize);
            FacetAngle = Math.Max(c.FacetAngle, f.FacetAngle);
            MergedPositions = c.MergedPositions + f.MergedPositions; NeedleFlips = c.NeedleFlips + f.NeedleFlips;
            MergeLambdaRequired = Math.Max(c.MergeLambdaRequired, f.MergeLambdaRequired);
            RayCount = Math.Max(c.RayCount, f.RayCount);
            if (CheckCanonical) CanonicalProblem = c.CanonicalProblem != "" ? c.CanonicalProblem : f.CanonicalProblem;
            Statuses result = (c.Status == Statuses.NoDaylight || f.Status == Statuses.NoDaylight) ? Statuses.NoDaylight : Statuses.Success;
            if (result == Statuses.NoDaylight) Message = c.Status == Statuses.NoDaylight ? "cut: " + c.Message : "fill: " + f.Message;
            if (BuildComposite == false)
                return result;

            // the composite of both: the terrain triangles that both runs cut get one part (SlopeComposite.Partner)
            checkCancel();
            // (a toe crossing's two catch points: on one toe segment, within 1e-4 of the toes' size, each the other's nearest)
            var toeBox = AxisAlignedBox2d.Empty;
            foreach (var t in Toes) foreach (var p in t.Points) toeBox.Contain(p.xy);
            var comp = new SlopeComposite(c.lastOverlay, Pieces.Count, c.UsedMinFeatureSize, CancelF) {
                CopyTerrain = CompositeKind == CompositeKinds.Full,
                Partner = new SlopeComposite(f.lastOverlay, Pieces.Count, f.UsedMinFeatureSize, CancelF) { GroupOffset = nc },
                SegmentOfGroup = Pieces.Select(p => ((long)p.Toe << 20) + p.Segment).ToArray(),
                PartnerSnapRadius = 1e-4 * toeBox.DiagonalLength
            };
            if (comp.Build() == false)
                return fail(Statuses.CompositeFailed, "composite: " + comp.Problem);
            // the clipped slope triangles of both get the exact check (O1), each with its run's piece (fill mirrored)
            var bad = new List<int>();
            foreach (int tid in comp.Region.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, cc = Vector3d.Zero;
                comp.Region.GetTriVertices(tid, ref a, ref b, ref cc);
                int g = comp.Region.GetTriangleGroup(tid);
                bool fillPiece = g >= nc;
                InPiece pc = fillPiece ? f.lastPieces[g - nc] : c.lastPieces[g];
                if (fillPiece) { a.z = -a.z; b.z = -b.z; cc.z = -cc.z; }
                bool slopeOk = ExactPredicates.Orient2d(a.xy, b.xy, cc.xy) > 0 && Slope.IsNotSteeper(a, b, cc, pc.Spec);
                bool supportOk = Slope.IsPlaneOnOrBelow(a, b, cc, pc.A) && Slope.IsPlaneOnOrBelow(a, b, cc, pc.B);
                if (slopeOk == false || supportOk == false) bad.Add(tid);
            }
            // (final: each run raised its margin and lowering for its own region; the mixed one does not build again)
            if (bad.Count > 0) {
                ProblemTriangles = bad;
                return fail(Statuses.SlopeViolation, $"{bad.Count} triangles of the mixed composite's slope region failed the exact check");
            }
            SlopeRegion = comp.Region;
            Composite = CompositeKind == CompositeKinds.Full ? comp.Composite : null;
            Replacement = CompositeKind == CompositeKinds.Replacement ? comp.Composite : null;
            ReplacedTerrainTriangles = comp.Replaced.ToList();
            CompositeTerrainVertices = comp.TerrainVertexOf;
            TerrainCompositeVertices = comp.CompositeVertexOf;
            return result;
        }

        Statuses compute()
        {
            if (Direction == Directions.Auto)
                return compute_auto();
            bool fill = Direction == Directions.Fill;
            if (Toes == null || Toes.Count == 0)
                return fail(Statuses.InvalidInput, "no toes");
            if (!(Tolerance > 0) || double.IsInfinity(Tolerance))
                return fail(Statuses.InvalidInput, "Tolerance must be positive and finite (no default)");
            if (!(SlopeMargin > 0) || !(SlopeMargin <= 0.01))
                return fail(Statuses.InvalidInput, "SlopeMargin must be in (0, 0.01]");
            if (!(TerrainVerticalUncertainty >= 0) || double.IsInfinity(TerrainVerticalUncertainty))
                return fail(Statuses.InvalidInput, "TerrainVerticalUncertainty must be >= 0 and finite");
            if (double.IsNaN(RequiredCatchLineAccuracy) == false && !(RequiredCatchLineAccuracy > 0 && RequiredCatchLineAccuracy < double.PositiveInfinity))
                return fail(Statuses.InvalidInput, "RequiredCatchLineAccuracy must be positive and finite (or NaN: none)");
            if (double.IsNaN(CatchLineWarningThreshold) || !(CatchLineWarningThreshold > 0))
                return fail(Statuses.InvalidInput, "CatchLineWarningThreshold must be positive (+infinity: none)");
            double heightLimit = HeightLimit;
            bool localLimit = Terrain != null && double.IsNaN(HeightLimit);
            if (Terrain != null) {
                var swt = System.Diagnostics.Stopwatch.StartNew();
                Statuses ts = check_terrain();
                PhaseSeconds["terrain"] = swt.Elapsed.TotalSeconds;
                if (ts != Statuses.Success)
                    return ts;
            }
            if (Terrain != null && double.IsNaN(heightLimit)) {
                // above the highest terrain vertex (cut) and the toe, by Tolerance: the level line of the cap, at H - λ, is
                // then above the terrain by Tolerance - λ > 0 (λ is at most its bound, which the budget keeps below
                // Tolerance), also after rounding, so the region cannot reach it
                double ext = fill ? terrainZMin : terrainZMax, toeExt = fill ? double.MaxValue : double.MinValue;
                foreach (var toe in Toes)
                    if (toe != null && toe.Points != null)
                        foreach (var p in toe.Points) toeExt = fill ? Math.Min(toeExt, p.z) : Math.Max(toeExt, p.z);
                heightLimit = fill ? Math.Min(ext, toeExt) - Tolerance : Math.Max(ext, toeExt) + Tolerance;
            }
            if (MathUtil.IsFinite(heightLimit) == false)
                return fail(Statuses.InvalidInput, "HeightLimit must be finite (it may be NaN only with a Terrain)");

            // pieces and their points, mirrored for fill (z -> -z, exact)
            var pieces = new List<InPiece>();
            var toePts = new List<List<Vector3d>>();
            for (int t = 0; t < Toes.Count; ++t) {
                SlopeToe toe = Toes[t];
                if (toe == null || toe.Points == null)
                    return problem(t, -1, "toe without points");
                int n = toe.Points.Count;
                if (toe.Closed ? n < 3 : n < 2)
                    return problem(t, -1, "too few toe points");
                bool sideOk = toe.Closed ? (toe.Side == SlopeToe.Sides.Inside || toe.Side == SlopeToe.Sides.Outside)
                                         : (toe.Side == SlopeToe.Sides.Left || toe.Side == SlopeToe.Sides.Right);
                if (sideOk == false)
                    return problem(t, -1, toe.Closed ? "a closed toe needs Side Inside or Outside" : "an open toe needs Side Left or Right");
                var pts = new List<Vector3d>();
                for (int i = 0; i < n; ++i) {
                    Vector3d p = toe.Points[i];
                    if (MathUtil.IsFinite(p.x) == false || MathUtil.IsFinite(p.y) == false || MathUtil.IsFinite(p.z) == false)
                        return problem(t, i, "toe point not finite");
                    pts.Add(fill ? new Vector3d(p.x, p.y, -p.z) : p);
                }
                toePts.Add(pts);
                SlopeSpec[] start = fill ? toe.FillStart : toe.CutStart, end = fill ? toe.FillEnd : toe.CutEnd;
                int segs = toe.SegmentCount;
                if (start == null || start.Length != segs || (end != null && end.Length != segs))
                    return problem(t, -1, "one slope per segment start and end is needed for the direction");
                for (int i = 0; i < segs; ++i) {
                    Vector3d a = pts[i], b = pts[(i + 1) % n];
                    if (a.x == b.x && a.y == b.y)
                        return problem(t, i, "zero-length or vertical toe segment (in XY)");
                    if (start[i].IsValid == false)
                        return problem(t, i, "invalid slope");
                    if (end != null && (end[i].Kind != start[i].Kind || end[i].Value != start[i].Value))
                        return fail(Statuses.NotImplemented, $"toe {t} segment {i}: a slope that changes along a segment is not in phase 1a");
                    pieces.Add(new InPiece() { Toe = t, Segment = i, A = a, B = b, Spec = start[i], S = start[i].S });
                }
            }
            double H = fill ? -heightLimit : heightLimit;
            foreach (var pc in pieces)
                if (!(pc.A.z < H && pc.B.z < H))
                    return problem(pc.Toe, pc.Segment, fill ? "HeightLimit must be below the toe" : "HeightLimit must be above the toe");

            string why;
            Index2i where;
            if (ToeGeometry.Validate(toePts, Toes.Select(x => x.Closed).ToList(), out why, out where) == false)
                return problem(where.a, where.b, why);

            if (localLimit == false)
                return attempts(pieces, toePts, H, fill);
            // A local height limit (TERRAIN_1C_PLAN.md): the slope is built up to the highest terrain near the toe and
            // raised while the region reaches it, at most to the global limit. A region that does not reach the cap
            // is the region of any higher cap (it is bounded by the catch line and the toe), so the result is that of
            // the global limit up to Tolerance (not bit for bit: facets, margins and λ depend on the cap through D_max),
            // with a smaller slope surface.
            double zMin = pieces.Min(p => Math.Min(p.A.z, p.B.z)), sMin = pieces.Min(p => p.S);
            var toeBox = AxisAlignedBox2d.Empty;
            foreach (var pts in toePts) foreach (var p in pts) toeBox.Contain(p.xy);
            // (with the estimate, one pass over the terrain gives both: the class of reach 0 is the box itself)
            Func<double, double> upTo = CapEstimateRounds > 0 ? terrain_max_by_reach(toeBox, (H - zMin) / sMin, fill) : null;
            double hLocal = (upTo != null ? upTo(0) : local_terrain_max(toeBox, 0, fill)) + Tolerance;
            double toeMax = pieces.Max(p => Math.Max(p.A.z, p.B.z)) + Tolerance;
            hLocal = Math.Max(hLocal, toeMax);
            // the start, without a build: a slope of the specified ratio reaches a height h within (h - zMin) / sMin of the
            // toe, so where all terrain within that reach is below h - Tolerance (a fixed point of this iteration) such a
            // slope cannot reach a cap at h. The built surface is a little flatter (the margin δ, the facets, the rise of
            // sloped toe segments: its gradient bound Slb is below S), so this is an estimate that usually saves the
            // raise, not a guarantee; the raise below decides
            if (upTo != null) {
                for (int k = 0; k < CapEstimateRounds && hLocal < H; ++k) {
                    double next = upTo((hLocal - zMin) / sMin) + Tolerance;
                    if (!(next > hLocal)) break;
                    hLocal = next;
                }
            }
            while (true) {
                if (!(hLocal < H)) hLocal = H;
                capMayGrow = hLocal < H;
                CapRounds++;
                Statuses st;
                try { st = attempts(pieces, toePts, hLocal, fill); } finally { capMayGrow = false; }
                if (st != Statuses.NoDaylight || reachedCap == false || hLocal >= H)
                    return st;
                // twice the reach of this cap, and at least twice the height range
                double reach = 2 * (hLocal - zMin) / sMin;
                double next = local_terrain_max(toeBox, reach, fill) + Tolerance;
                hLocal = Math.Max(next, hLocal + (hLocal - zMin));
            }
        }

        // in one pass over the terrain triangles: a function of reach (up to maxReach) that is at least the highest
        // terrain point (fill: the lowest, mirrored) within that reach of the box. A triangle counts with its highest
        // corner at the distance of its XY bounding box (a triangle that meets the expanded box has its bounding box
        // meet it), its distance class rounded up: never below. (Vertices alone are not a bound: a triangle that
        // reaches into the box from outside is higher inside it than any vertex there.)
        Func<double, double> terrain_max_by_reach(AxisAlignedBox2d box, double maxReach, bool fill)
        {
            const int bins = 4096;
            var top = new double[bins + 1];
            for (int i = 0; i <= bins; ++i) top[i] = double.MinValue;
            double w = maxReach / bins;
            if (!(w > 0) || MathUtil.IsFinite(w) == false) return (r) => double.MaxValue;
            foreach (int t in Terrain.TriangleIndices()) {
                Index3i tv = Terrain.GetTriangle(t);
                Vector3d a = Terrain.GetVertex(tv.a), b = Terrain.GetVertex(tv.b), c = Terrain.GetVertex(tv.c);
                double minX = Math.Min(a.x, Math.Min(b.x, c.x)), maxX = Math.Max(a.x, Math.Max(b.x, c.x));
                double minY = Math.Min(a.y, Math.Min(b.y, c.y)), maxY = Math.Max(a.y, Math.Max(b.y, c.y));
                double d = Math.Max(Math.Max(box.Min.x - maxX, minX - box.Max.x), Math.Max(box.Min.y - maxY, minY - box.Max.y));
                int i = d <= 0 ? 0 : (int)Math.Min(bins, Math.Ceiling(d / w));
                double z = fill ? -Math.Min(a.z, Math.Min(b.z, c.z)) : Math.Max(a.z, Math.Max(b.z, c.z));
                top[i] = Math.Max(top[i], z);
            }
            for (int i = 1; i <= bins; ++i) top[i] = Math.Max(top[i], top[i - 1]);
            return (r) => r >= maxReach ? top[bins] : top[(int)Math.Min(bins, Math.Ceiling(Math.Max(r, 0) / w))];
        }

        bool undercutThroughDesign;

        // TERRAIN.md 5.5: each catch-line segment's horizontal uncertainty, (Tolerance + TerrainVerticalUncertainty) over the
        // gradient of S - T on its face plus the move of the composite's snapping (MinFeatureSize); a local linearized
        // estimate. Fills the reports; 0: fine, 1: RequiredCatchLineAccuracy needs a smaller Tolerance (next), 2: not
        // reachable (the vertical uncertainty or a tangential contact alone exceeds it)
        int catch_line_accuracy(out double next)
        {
            next = Tolerance;
            bool required = double.IsNaN(RequiredCatchLineAccuracy) == false;
            int verdict = 0;
            double eps = Tolerance + Math.Max(0, TerrainVerticalUncertainty);
            for (int li = 0; li < CatchLines.Count; ++li) {
                CatchLine l = CatchLines[li];
                l.Uncertainty.Clear();
                for (int i = 0; i < l.Gradient.Count; ++i) {
                    double g = l.Gradient[i];
                    double u = g > 0 ? eps / g + UsedMinFeatureSize : double.PositiveInfinity;
                    l.Uncertainty.Add(u);
                    if (u > CatchLineWarningThreshold) IllConditionedCatchLine.Add(new Index2i(li, i));
                    if (l.Tangential[i]) TangentialContact.Add(new Index2i(li, i));
                    if (required && !(u <= RequiredCatchLineAccuracy)) {
                        // the Tolerance that meets it here (with 10 % to spare), if any
                        double t = g > 0 ? 0.9 * ((RequiredCatchLineAccuracy - UsedMinFeatureSize) * g - Math.Max(0, TerrainVerticalUncertainty)) : 0;
                        if (!(t > 0)) verdict = 2;
                        else { next = Math.Min(next, t); if (verdict == 0) verdict = 1; }
                    }
                }
            }
            return verdict;
        }

        internal static StatusKinds kind_of(Statuses st, bool undercutThroughDesign)
        {
            switch (st) {
                case Statuses.Success: case Statuses.NoDaylight: return StatusKinds.None;
                case Statuses.InvalidInput: case Statuses.NotHeightField: return StatusKinds.InvalidInput;
                case Statuses.NotImplemented: return StatusKinds.UnsupportedByMethod;
                case Statuses.ToeSteeperThanSlope: return StatusKinds.GeometricallyInfeasible;
                case Statuses.ToeUndercut: case Statuses.ToeOvertopped:
                    return undercutThroughDesign ? StatusKinds.UnsupportedByMethod : StatusKinds.GeometricallyInfeasible;
                case Statuses.SlopeViolation: case Statuses.CatchLineAccuracyNotReached: return StatusKinds.NotCertified;
                case Statuses.Cancelled: return StatusKinds.Cancelled;
                default: return StatusKinds.Internal;
            }
        }

        // The cone of piece c.Cause passes below toe segment c.Piece at c.At; its value there comes from the point p of
        // the cause's segment nearest to At, along the straight path p -> At. Through the design area: the path leaves p
        // or reaches At from the design side of its segment, or crosses a toe segment on the way (the method's cones pass
        // through the design area; a surface restricted to the slope side need not undercut). Otherwise the path runs on
        // the slope side, where every surface within the slope would undercut: infeasible.
        bool through_design_area(List<InPiece> pieces, SlopeMap.UndercutCause c)
        {
            InPiece po = pieces[c.Cause], pk = pieces[c.Piece];
            Vector2d q = c.At;
            Vector2d a = po.A.xy, b = po.B.xy, d = b - a;
            double t = MathUtil.Clamp((q - a).Dot(d) / d.LengthSquared, 0, 1);
            Vector2d p = a + t * d;
            if (p.Distance(q) == 0) return false;
            // the slope side of a piece: left of its direction or right
            Func<InPiece, bool> slopeLeft = (pc) => {
                SlopeToe toe = Toes[pc.Toe];
                if (toe.Closed == false) return toe.Side == SlopeToe.Sides.Left;
                // (the orientation of the simple polygon, exact: Orient2d at its lowest vertex, lexicographically)
                var pts = toe.Points;
                int m = 0;
                for (int i = 1; i < pts.Count; ++i)
                    if (pts[i].x < pts[m].x || (pts[i].x == pts[m].x && pts[i].y < pts[m].y)) m = i;
                int ccw = ExactPredicates.Orient2d(pts[(m + pts.Count - 1) % pts.Count].xy, pts[m].xy, pts[(m + 1) % pts.Count].xy);
                return (toe.Side == SlopeToe.Sides.Inside) == (ccw > 0);
            };
            // (exact: the side of the path's other end against the line of the segment it starts from; p is the rounded
            // nearest point, the decisions are exact on these doubles)
            Func<InPiece, Vector2d, bool> intoDesign = (pc, other) => {
                int side = ExactPredicates.Orient2d(pc.A.xy, pc.B.xy, other);
                return slopeLeft(pc) ? side < 0 : side > 0;
            };
            if (intoDesign(po, q) || intoDesign(pk, p)) return true;
            // at an end of the cause's segment (a toe vertex) the slope side is a wedge: the path must stay on the slope
            // side of the other segment there too (conservative: either one says design, it is the method's limit)
            if (t == 0 || t == 1) {
                Vector2d corner = t == 0 ? po.A.xy : po.B.xy;
                foreach (InPiece pc in pieces)
                    // (not the undercut segment itself: q lies on it, and its side is the test of p above)
                    if (pc != po && pc != pk && pc.Toe == po.Toe && (pc.A.xy == corner || pc.B.xy == corner) && intoDesign(pc, q)) return true;
            }
            // a crossing or a touch of another toe segment on the way (a touch counts as through: conservative)
            foreach (InPiece pc in pieces) {
                if (pc == po || pc == pk) continue;
                Vector2d u = pc.A.xy, w = pc.B.xy;
                if (u == p || w == p || u == q || w == q) continue;     // (a segment that only meets the path at its ends)
                int o1 = ExactPredicates.Orient2d(p, q, u), o2 = ExactPredicates.Orient2d(p, q, w);
                int o3 = ExactPredicates.Orient2d(u, w, p), o4 = ExactPredicates.Orient2d(u, w, q);
                if (o1 * o2 <= 0 && o3 * o4 <= 0 && (o1 != 0 || o2 != 0 || o3 != 0 || o4 != 0)) return true;
                if (o1 == 0 && o2 == 0 && o3 == 0 && o4 == 0 && overlap_collinear(p, q, u, w)) return true;
            }
            // (open toes: a straight path on the slope side can leave the strip between the lateral limits, where nothing
            // is specified; it still counts as infeasible here, as the method treats the slope side as unbounded)
            return false;
        }

        // collinear segments pq and uw overlap (exact on the doubles: along the dominant axis)
        static bool overlap_collinear(Vector2d p, Vector2d q, Vector2d u, Vector2d w)
        {
            bool xs = Math.Abs(q.x - p.x) >= Math.Abs(q.y - p.y);
            double a0 = xs ? Math.Min(p.x, q.x) : Math.Min(p.y, q.y), a1 = xs ? Math.Max(p.x, q.x) : Math.Max(p.y, q.y);
            double b0 = xs ? Math.Min(u.x, w.x) : Math.Min(u.y, w.y), b1 = xs ? Math.Max(u.x, w.x) : Math.Max(u.y, w.y);
            return a0 <= b1 && b0 <= a1;
        }

        // the highest terrain vertex (fill: the lowest, mirrored) within 'reach' of the box
        double local_terrain_max(AxisAlignedBox2d box, double reach, bool fill)
        {
            box.Expand(reach);
            double m = double.MinValue;
            foreach (Vector3d v in Terrain.Vertices())
                if (box.Contains(v.xy)) m = Math.Max(m, fill ? -v.z : v.z);
            return m;
        }

        bool reachedCap, capMayGrow;

        Statuses attempts(List<InPiece> pieces, List<List<Vector3d>> toePts, double H, bool fill)
        {
            // margins (TERRAIN.md 5.4, plan "Departures" 1)
            // the check decides (5.4): a support failure (ii) raises the toe lowering λ to what the realized output
            // needs, measured; a slope or orientation failure raises the margin δ
            double delta = SlopeMargin, lambdaReq = -1;
            for (int attempt = 0; ; ++attempt) {
                checkCancel();
                BuildResult br;
                Statuses st = build(pieces, toePts, H, delta, lambdaReq, fill, out br);
                if (attempt > 0 && st == Statuses.InvalidInput)
                    return fail(Statuses.SlopeViolation, $"{ProblemTriangles.Count} triangles failed the exact check; a larger margin does not fit into Tolerance");
                if (st != Statuses.SlopeViolation) {
                    if (st == Statuses.Success || st == Statuses.NoDaylight || st == Statuses.CompositeFailed) {
                        SlopeSurface = br.Mesh;
                        CatchLines = br.CatchLines;
                        lastOverlay = br.Overlay; lastPieces = pieces;
                        SlopeRegion = br.Region;
                        Composite = CompositeKind == CompositeKinds.Full ? br.Composite : null;
                        Replacement = CompositeKind == CompositeKinds.Replacement ? br.Composite : null;
                        ReplacedTerrainTriangles = br.Replaced ?? new List<int>();
                        CompositeTerrainVertices = br.TerrainVertexOf;
                        TerrainCompositeVertices = br.CompositeVertexOf;
                        Pieces = pieces.Select(p => new SlopePiece() {
                            Toe = p.Toe, Segment = p.Segment, Spec = p.Spec, Direction = fill ? Directions.Fill : Directions.Cut,
                            P0 = fill ? new Vector3d(p.A.x, p.A.y, -p.A.z) : p.A, P1 = fill ? new Vector3d(p.B.x, p.B.y, -p.B.z) : p.B
                        }).ToList();
                        // (assigned per build: a raised cap or another accuracy round builds again)
                        Volume = PlanArea = SlopeArea = 0;
                        if (br.Volumes != null)
                            for (int i = 0; i < Pieces.Count; ++i) {
                                Pieces[i].Volume = br.Volumes[i]; Pieces[i].PlanArea = br.PlanAreas[i]; Pieces[i].SlopeArea = br.SlopeAreas[i];
                                Volume += br.Volumes[i]; PlanArea += br.PlanAreas[i]; SlopeArea += br.SlopeAreas[i];
                            }
                    }
                    return st;
                }
                ProblemTriangles = br.Bad;
                // (the altitudes on the mesh the ids are of: the realization, or the clipped region of the composite)
                if (br.BadMesh != null)
                    AttemptProblemAltitudes.Add(br.Bad.Select(t => SlopeMap.TriangleAltitude(br.BadMesh, t)).ToList());
                if (br.RebuildLambda > 0) {
                    lambdaReq = br.RebuildLambda;
                    continue;
                }
                // (the build raised λ itself, by realizing again; what is left needs a larger margin δ and a new build)
                if (attempt >= 7 || delta * 8 > 1e-3)
                    return fail(Statuses.SlopeViolation, $"{br.Bad.Count} triangles failed the exact check");
                delta *= 8;
                // (the measured lowering is kept; the next build caps it at its own bound)
                lambdaReq = Math.Max(br.Lambda, (br.MaxExcess > 0 && 4 * br.MaxExcess <= br.LambdaBound) ? Math.Max(2 * br.Lambda, 4 * br.MaxExcess) : -1);
            }
        }

        // the collapses of a build's first realization, made again by the later ones (TERRAIN.md 5.4)
        sealed class CollapseRecord
        {
            public List<Vector2d[]> Made;
        }

        sealed class BuildResult
        {
            public DMesh3 Mesh;
            public List<int> Bad = new List<int>();
            // the mesh Bad's triangle ids are of (the realization, or the composite's clipped region)
            public DMesh3 BadMesh;
            public int SlopeFailures;          // slope (i) or XY orientation
            public double MaxExcess;           // largest height of a failing triangle's plane above its toe ends (ii)
            public double Lambda, LambdaBound;
            public double MergeLambda;
            public double RebuildLambda;       // RebuildForEachLowering: build again with this lowering         // > 0: build again with at least this lowering (the realization merged)
            public List<CatchLine> CatchLines = new List<CatchLine>();
            public DMesh3 Region, Composite;
            public SlopeTerrainOverlay Overlay;
            public List<int> Replaced;
            public int[] TerrainVertexOf, CompositeVertexOf;
            public double[] Volumes, PlanAreas, SlopeAreas;
        }

        double terrainZMin, terrainZMax;

        Statuses check_terrain()
        {
            terrainZMin = double.MaxValue; terrainZMax = double.MinValue;
            foreach (Vector3d v in Terrain.Vertices()) {
                if (MathUtil.IsFinite(v.x) == false || MathUtil.IsFinite(v.y) == false || MathUtil.IsFinite(v.z) == false)
                    return fail(Statuses.InvalidInput, "a terrain vertex is not finite");
                terrainZMin = Math.Min(terrainZMin, v.z); terrainZMax = Math.Max(terrainZMax, v.z);
            }
            if (Terrain.TriangleCount == 0)
                return fail(Statuses.InvalidInput, "the terrain has no triangles");
            // (degenerate and overlapping triangles are found by the overlay, under the slope surface, where they matter)
            return Statuses.Success;
        }

        Statuses problem(int toe, int segment, string message)
        {
            ToeProblems.Add(new Index2i(toe, segment));
            return fail(Statuses.InvalidInput, $"toe {toe}" + (segment >= 0 ? $" at {segment}" : "") + ": " + message);
        }

        void checkCancel()
        {
            if (CancelF != null && CancelF())
                throw new OperationCanceledException();
        }

        static double ulp(double x)
        {
            x = Math.Abs(x);
            return ExactInterval.Up(x) - x;
        }

        // ---------------------------------------------------------------- one build

        Statuses build(List<InPiece> pieces, List<List<Vector3d>> toePts, double H, double delta, double lambdaReq, bool fill,
                       out BuildResult br)
        {
            br = new BuildResult();
            ToeProblems.Clear();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Action<string> phase = (name) => {
                double t;
                PhaseSeconds.TryGetValue(name, out t);
                PhaseSeconds[name] = t + sw.Elapsed.TotalSeconds;
                sw.Restart();
            };
            var kernel = new ExactSlopeKernel();

            // extent and numerical resolution of the output
            double sMin = pieces.Min(p => p.S), sMax = pieces.Max(p => p.S);
            double zMin = pieces.Min(p => Math.Min(p.A.z, p.B.z));
            var toeBox = AxisAlignedBox2d.Empty;
            foreach (var pts in toePts) foreach (var p in pts) toeBox.Contain(p.xy);
            double D0 = (H - zMin) / (sMin * (1 - delta));
            double maxXY = Math.Max(Math.Max(Math.Abs(toeBox.Min.x), Math.Abs(toeBox.Max.x)), Math.Max(Math.Abs(toeBox.Min.y), Math.Abs(toeBox.Max.y))) + 2 * D0;
            double maxZ = Math.Max(Math.Abs(H), pieces.Max(p => Math.Max(Math.Abs(p.A.z), Math.Abs(p.B.z))));
            // (the output coordinates and heights are rounded to nearest from the exact values, ExactSlopeKernel.Rounded /
            // Height: half an ulp; one ulp is counted. Heights near the lowered toe can exceed maxZ a little: ulp(2 maxZ))
            double exy = ulp(maxXY), eeval = ulp(2 * maxZ);
            double minFeature = 2 * (8 * exy + 4 * eeval / sMin) / delta;
            double ez = 2 * sMax * exy + eeval;
            if (MathUtil.IsFinite(D0) == false || MathUtil.IsFinite(maxXY + 4 * D0) == false)
                return fail(Statuses.InvalidInput, "the slopes are too gentle for the height: the reach is not finite");
            // the bound of the toe lowering for triangles with altitudes of at least MinFeatureSize (plan, departure 1);
            // it is reserved in the tolerance budget, but the toe is lowered only by what the output needs (measured)
            double lambdaBound = 2 * ez * (1 + 4 * 1.1 * D0 / minFeature);
            double lambda = (lambdaReq >= 0) ? lambdaReq : 4 * ez;     // (the rounding of one vertex; raised by measurement)
            if (lambda > lambdaBound) lambda = lambdaBound;
            br.Lambda = lambda; br.LambdaBound = lambdaBound;
            UsedSlopeMargin = delta; UsedMinFeatureSize = minFeature; UsedToeLowering = lambda;

            // tolerance budget: lowering, slope margin, cone facets (TERRAIN.md 5.1)
            // (and the vertices merged within 64 ulps by the realization: z, the lowest plane of all of them, is at most
            // sMax times the distance below the surface at the kept position)
            // (the triangles of a merged vertex leave their cells: below S̃ by at most 4 sMax (r + e_xy), above by at
            // most 2 sMax (r + e_xy) plus the rounding of a vertex, which the toe lowering covers, see MergeLambda;
            // r = 64 e_xy. Derivation, for a triangle whose vertex p moved to q with barycentric weight w at x: with
            // y = x - w (q - p), a point of the unmoved triangle, z_tri(x) - S̃(x) = [l_f(y) - S̃(y)] + w (z_q - l_f(p))
            // - (S̃(x) - S̃(y)), and |S̃(x) - S̃(y)| <= sMax w r; below, z_q - l_f(p) >= -3 sMax r - O(e_xy), above,
            // z_q <= S̃(q) + O(e_xy) (TERRAIN_1B_PLAN.md, reviews))
            double mergeRadius = MergeRadius(exy);
            double mergeShare = MergeShare(exy, sMax);
            double facetShare = Tolerance - lambdaBound - delta * sMax * 1.1 * D0 - mergeShare;
            if (!(facetShare > 0))
                return fail(Statuses.InvalidInput, $"Tolerance {Tolerance} is below the numerical margins ({lambdaBound + delta * sMax * 1.1 * D0 + mergeShare:G3})");
            // toe segments below the feature size at the largest margin (1e-3) cannot be realized
            double minFeatureMax = 2 * (8 * exy + 4 * eeval / sMin) / 1e-3;
            foreach (var pc in pieces)
                if (pc.A.xy.Distance(pc.B.xy) < minFeatureMax) {
                    ToeProblems.Add(new Index2i(pc.Toe, pc.Segment));
                    return fail(Statuses.InvalidInput, $"toe {pc.Toe} segment {pc.Segment} is shorter than the smallest feature that can be realized ({minFeatureMax:G3})");
                }
            // a direction is at most 3/4 theta from its nearest tangent plane (the grid is theta, and a grid direction
            // within theta / 2 of a strip direction is dropped), so the facet error is s D (1 - cos(3/4 theta))
            double c = 1 - facetShare / (sMax * 1.1 * D0);
            double theta = (c > Math.Cos(3 * Math.PI / 32)) ? Math.Acos(c) / 0.75 : Math.PI / 8;
            int nTheta = (int)Math.Ceiling(2 * Math.PI / theta);
            theta = 2 * Math.PI / nTheta;
            FacetAngle = theta;
            // the band merge (TERRAIN.md 5.4, "Repair of the band") within the facet share's slack: the facets need
            // s D (1 - cos(3/4 theta)) at the rounded angle, at most their share; the rest takes the band merge's part
            // below S̃, 4 sMax (r + l + e_xy). A lower bound of the slack: 1 - cos is exact (Sterbenz, cos >= cos(3 pi / 32)
            // > 1/2); Math.Cos is counted as off by up to 2^-49 (8 ulps of 1); each product and the difference are
            // rounded outwards. It scales with 2^k bit for bit (scale-free factor, exact power-of-two scaling of the rest)
            double oneMinusCos = ExactInterval.Up((1 - Math.Cos(0.75 * theta)) + Math.Pow(2, -49));
            double facetNeed = ExactInterval.Up(ExactInterval.Up(ExactInterval.Up(sMax * 1.1) * D0) * oneMinusCos);
            double facetSlack = ExactInterval.Down(facetShare - facetNeed);
            double bandRadius = BandMergeRadius(exy, ez, sMax, minFeature, facetSlack, lambdaBound);
            UsedMergeRadius = mergeRadius; UsedBandMergeRadius = bandRadius;
            // (the reach does not depend on λ: the map is built once per margin δ)
            double dMax = (H - zMin + lambdaBound) / (sMin * (1 - delta) * Math.Cos(theta)) * 1.01;

            // The toe lowering λ (TERRAIN_1C_PLAN.md, idea A). Lowering every plane by λ leaves the line of any two
            // planes unchanged (Nz_a D_b - Nz_b D_a does not change when both D grow by λ Nz), so the map is built once
            // from the specified toe and realized with z lowered by λ, exactly rounded; a larger λ costs a realization,
            // not a build. The cap is lowered with the planes (else the map's lines with the cap would move), so the surface
            // ends at H - λ, never above H. The checks at construction (slope, support of a plane at its piece's ends,
            // undercut against Tolerance) compare plane and toe heights, so they do not depend on λ either.

            // planes of the pieces: the strips first, then the cone tangents, which know the strips at their apex
            double rFan = 1;
            while (rFan < dMax) rFan *= 2;
            while (rFan > 2 * dMax) rFan *= 0.5;
            var stripsAt = new Dictionary<Vector3d, List<StripRef>>();
            foreach (var pc in pieces) {
                checkCancel();
                // (a new build: no planes of an earlier attempt, whose kernel is gone)
                pc.Strip.Clear(); pc.FanA.Clear(); pc.FanB.Clear(); pc.AngA.Clear(); pc.AngB.Clear();
                pc.A1 = pc.A; pc.B1 = pc.B;
                pc.Sb = pc.S * (1 - delta);
                Statuses ps = make_strips(kernel, pc, stripsAt);
                if (ps != Statuses.Success)
                    return ps;
            }
            foreach (var pc in pieces) {
                make_fans(kernel, pc, theta, nTheta, rFan, stripsAt);
                double r = (H - Math.Min(pc.A1.z, pc.B1.z)) / pc.Slb * 1.01 + 1e-9 * dMax;
                var box = new AxisAlignedBox2d(pc.A.xy, pc.A.xy);
                box.Contain(pc.B.xy);
                box.Expand(r);
                pc.Reach = box;
            }

            phase("planes");
            // the domain, a planar map of convex faces
            var box2 = toeBox;
            box2.Expand(1.05 * dMax + 1e-6 * toeBox.MaxDim);
            var map = new SlopeMap(kernel);
            string why;
            Index2i where;
            if (map.BuildDomain(toePts, Toes.Select(t => t.Closed).ToList(), Toes.Select(t => t.Side).ToList(), box2, out why, out where) == false) {
                ToeProblems.Add(where);
                return fail(Statuses.InvalidInput, why);
            }
            int cap = kernel.AddHorizontalPlane(H);
            map.LabelDomain(cap);

            phase("domain");
            // insert the pieces, in a random order (the expected work of an incremental envelope; the result does not
            // depend on the order: ties go to the lower plane id, and Simplify makes the canonical partition)
            int[] order = InsertionOrder != null ? InsertionOrder(pieces.Count) : shuffled(pieces.Count, InsertionSeed);
            foreach (int k in order) {
                checkCancel();
                map.InsertPiece(k, pieces[k].Strip, pieces[k].FanA, pieces[k].AngA, pieces[k].FanB, pieces[k].AngB,
                    pieces[k].A1, pieces[k].B1, Math.Min(pieces[k].A1.z, pieces[k].B1.z), pieces[k].Slb, pieces[k].Reach, theta);
            }

            phase("insert");
            map.Simplify();
            RayCount = Math.Max(RayCount, map.RayCount);
            if (CheckCanonical && CanonicalProblem == "") CanonicalProblem = map.CanonicalProblem();
            phase("simplify");

            // toe undercut: a face on a toe segment labelled by another piece, lower than the toe by more than Tolerance
            List<SlopeMap.UndercutCause> causes;
            var undercut = map.Undercuts(pieces.Select(p => new Vector3d[] { p.A1, p.B1 }).ToList(), Tolerance, out causes);
            UndercutCauses = causes;
            if (undercut.Count > 0) {
                foreach (int k in undercut) ToeProblems.Add(new Index2i(pieces[k].Toe, pieces[k].Segment));
                // (UnsupportedByMethod if any of them is caused through the design area)
                undercutThroughDesign = causes.Any(uc => through_design_area(pieces, uc));
                return fail(Statuses.ToeUndercut, $"{undercut.Count} toe segments are undercut by the cones of other parts of the toe");
            }

            // realization and the exact check, raising λ by measurement without a new build
            Attempts++;
            // (the kernel's work of the build itself; each realization counts its own below)
            PredicateCalls += kernel.PredicateCalls; ExactFallbacks += kernel.ExactPredicates; ExactConstructions += kernel.ExactConstructions;
            var collapsesMade = new CollapseRecord();
            for (int realization = 0; ; ++realization) {
                br = new BuildResult() { Lambda = lambda, LambdaBound = lambdaBound };
                UsedToeLowering = lambda;
                Statuses st = realize_and_check(map, kernel, cap, pieces, fill, lambda, lambdaBound, k => RepairLambda(ez, exy, sMax, k), FlipShare(exy, sMax), minFeature, mergeRadius, bandRadius,
                                                (l, k) => BandRepairLambda(ez, exy, sMax, l, k), br, phase, collapsesMade);
                Realizations++;
                if (st != Statuses.SlopeViolation)
                    return st;
                double next = -1;
                // (a band merge's need stays under λ's bound by the band radius's caps; checked, so λ never passes it: a
                // need above it goes back to the caller, which raises δ)
                if (br.MergeLambda > 0 && lambda < br.MergeLambda && (BandMerges == 0 || br.MergeLambda <= lambdaBound))
                    next = br.MergeLambda;
                // a support failure (ii) only: a larger lowering, measured on the output, while it fits under the bound;
                // anything else goes back to the caller, which raises the margin δ and builds again
                else if (realization < 7 && br.SlopeFailures == 0 && br.MaxExcess > 0 && 4 * br.MaxExcess <= lambdaBound && lambda < lambdaBound)
                    next = Math.Min(lambdaBound, Math.Max(2 * lambda, 4 * br.MaxExcess));
                if (next < 0)
                    return st;
                if (RebuildForEachLowering) {
                    // (the reference of the tests: a new map built at the new lowering; the map does not depend on λ, so
                    // the result must be the same bit for bit. Its builds count against the attempts of compute, and
                    // each build starts its realizations from 0, so on hard inputs the two paths may stop differently.
                    // Also, the realizations of one build replay the first one's collapses, which CollapseShortEdges
                    // decides by 3D length; the rebuild decides them at the new λ. The two differ only where an edge's
                    // 3D length lies within the rounding of z of MinFeatureSize, or where the order of two such
                    // lengths flips)
                    br.RebuildLambda = next;
                    return st;
                }
                lambda = next;
            }
        }

        /// <summary> tests: build the map again for every new toe lowering instead of realizing it again </summary>
        internal bool RebuildForEachLowering;
        /// <summary> tests: the order in which the pieces are inserted (a permutation of 0 .. n-1), instead of the seeded shuffle </summary>
        internal Func<int, int[]> InsertionOrder;
        internal ulong InsertionSeed = 0x5EED5EEDUL;
        /// <summary> tests: the most rays a build's Simplify cut (reflex corners of the label components) </summary>
        internal int RayCount;
        /// <summary> tests: check each build's map after Simplify (SlopeMap.CanonicalProblem; the first problem, "" if none) </summary>
        internal bool CheckCanonical;
        internal string CanonicalProblem = "";
        /// <summary> tests: the undercut causes of the last build (one per undercut segment, the first in OrderedFaces) </summary>
        internal List<SlopeMap.UndercutCause> UndercutCauses = new List<SlopeMap.UndercutCause>();

        // a permutation of 0 .. n-1 (Fisher-Yates with SplitMix64: the same on every platform)
        internal static int[] shuffled(int n, ulong seed)
        {
            var p = new int[n];
            for (int i = 0; i < n; ++i) p[i] = i;
            ulong x = seed;
            for (int i = n - 1; i > 0; --i) {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                int j = (int)(z % (ulong)(i + 1));
                int t = p[i]; p[i] = p[j]; p[j] = t;
            }
            return p;
        }

        // one realization at the lowering λ, its exact check, and (with a terrain) the overlay and the composite
        Statuses realize_and_check(SlopeMap map, ExactSlopeKernel kernel, int cap, List<InPiece> pieces, bool fill, double lambda,
                                   double lambdaBound, Func<int, double> repairLambda, double flipShare, double minFeature, double mergeRadius,
                                   double bandRadius, Func<double, int, double> bandRepairLambda, BuildResult br, Action<string> phase,
                                   CollapseRecord collapsesMade)
        {
            checkCancel();
            DMesh3 mesh;
            List<int> bad = br.Bad;
            long p0 = kernel.PredicateCalls, x0 = kernel.ExactPredicates, c0 = kernel.ExactConstructions;
            Action count = () => {
                PredicateCalls += kernel.PredicateCalls - p0; ExactFallbacks += kernel.ExactPredicates - x0; ExactConstructions += kernel.ExactConstructions - c0;
                p0 = kernel.PredicateCalls; x0 = kernel.ExactPredicates; c0 = kernel.ExactConstructions;
            };
            // (positions within a few ulps are one vertex: clusters of near-degenerate crossings that the collapse
            // below cannot always resolve; larger distances are left to it, which keeps the cells)
            // (merging needs the lowering mergeLambda; without room for it under the bound, or if the merge makes the
            // mesh non-manifold, the realization runs without it and the check decides)
            // (the merge and the needle flips each move the surface off S̃ by at most 2 sMax (r + e_xy) upwards, which the
            // lowering must cover, see RepairLambda: both only where both together fit under λ's bound)
            bool mayMerge = repairLambda(2) <= lambdaBound;
            bool mergedWithin = mayMerge;
            try {
                mesh = map.Realize(cap, fill, mayMerge ? mergeRadius : 0, lambda);
            } catch (SlopeMap.NonManifoldRealization) when (mayMerge) {
                mesh = map.Realize(cap, fill, 0, lambda);
                map.MergedCount = 0;
                mergedWithin = false;
            }
            // (the collapses decided on the build's first realization, made again on the later ones: TERRAIN.md 5.4)
            if (collapsesMade.Made == null) {
                collapsesMade.Made = new List<Vector2d[]>();
                SlopeMap.CollapseShortEdges(mesh, map.OutLabels, map.OnBarrier, minFeature, collapsesMade.Made);
            } else {
                SlopeMap.ReplayCollapses(mesh, collapsesMade.Made);
            }
            // (the targeted merge of the band, TERRAIN.md 5.4: the short edges of failing needles with different plane
            // sets, chains included, every position moved by at most the band radius; decided anew on each realization,
            // as which triangles fail depends on λ and δ, and the lowering need below is this realization's)
            // (only with the merge within r made, a band beyond r, and where an edge's triangle fails the exact checks)
            double bandLength = 0;
            BandMerges = 0;
            if (mergedWithin && bandRadius > mergeRadius) {
                Func<int, bool> fails = (t) => {
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    mesh.GetTriVertices(t, ref a, ref b, ref c);
                    InPiece pc = pieces[mesh.GetTriangleGroup(t)];
                    if (fill) { a.z = -a.z; b.z = -b.z; c.z = -c.z; }
                    return !(ExactPredicates.Orient2d(a.xy, b.xy, c.xy) > 0 && Slope.IsNotSteeper(a, b, c, pc.Spec) &&
                             Slope.IsPlaneOnOrBelow(a, b, c, pc.A) && Slope.IsPlaneOnOrBelow(a, b, c, pc.B));
                };
                BandMerges = map.BandMerge(mesh, bandRadius, lambda, fill, fails, out bandLength);
                // (the length rounded up: a bound of the move)
                if (BandMerges > 0) bandLength = ExactInterval.Up(bandLength);
            }
            int flips = mayMerge ? SlopeMap.FlipNeedles(mesh, mergeRadius, flipShare) : 0;
            if (map.DroppedTriangles > 0 && boundary_is_toe_limit_or_cap(mesh, map, cap) == false)
                return fail(Statuses.Failed, "the realization left a crack (dropped triangles of a face narrower than the rounding)");
            int repairs = (map.MergedCount > 0 || BandMerges > 0 ? 1 : 0) + (flips > 0 ? 1 : 0);
            MergedPositions = map.MergedCount; NeedleFlips = flips; MaxMergeDistance = bandLength;
            // (the lowering the repairs need: a merge within r as today; band merges by the largest move of an original
            // position (chains included, capped by the band radius), plus r if the merge within r moved positions too,
            // as a band merge can take labels the merge brought from up to r away (the moves add up); today's value
            // without band merges. Per realization: the band merges are decided on this one)
            double need;
            if (BandMerges == 0)
                need = repairs > 0 ? repairLambda(repairs) : 0;     // (today's, bit for bit)
            else
                need = bandRepairLambda(ExactInterval.Up((map.MergedCount > 0 ? mergeRadius : 0) + bandLength), flips > 0 ? 1 : 0);
            MergeLambdaRequired = need;
            if (repairs > 0 && lambda < need) {
                br.MergeLambda = need;
                phase("realize");
                count();
                return Statuses.SlopeViolation;
            }
            phase("realize");
            double maxRatio = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, cc = Vector3d.Zero;
                mesh.GetTriVertices(tid, ref a, ref b, ref cc);
                InPiece pc = pieces[mesh.GetTriangleGroup(tid)];
                if (fill) { a.z = -a.z; b.z = -b.z; cc.z = -cc.z; }
                bool slopeOk = ExactPredicates.Orient2d(a.xy, b.xy, cc.xy) > 0 && Slope.IsNotSteeper(a, b, cc, pc.Spec);
                bool supportOk = Slope.IsPlaneOnOrBelow(a, b, cc, pc.A) && Slope.IsPlaneOnOrBelow(a, b, cc, pc.B);
                if (slopeOk == false) br.SlopeFailures++;
                if (supportOk == false)
                    br.MaxExcess = Math.Max(br.MaxExcess, Math.Max(plane_excess(a, b, cc, pc.A), plane_excess(a, b, cc, pc.B)));
                if (slopeOk == false || supportOk == false) {
                    bad.Add(tid);
                    continue;
                }
                maxRatio = Math.Max(maxRatio, ratio_bound(a, b, cc, pc.Spec));
            }
            // consistently oriented: the two triangles of an interior edge run along it in opposite directions (two
            // counter-clockwise triangles on the same side of an edge would overlap; the overlay needs a height field)
            foreach (int e in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(e);
                if (et.b < 0) continue;
                Index2i ev = mesh.GetEdgeV(e);
                if (runs_along(mesh.GetTriangle(et.a), ev.a, ev.b) == runs_along(mesh.GetTriangle(et.b), ev.a, ev.b))
                    return fail(Statuses.Failed, $"the realized slope surface folds over at edge {e}");
            }
            br.Mesh = mesh;
            LastRealized = mesh;
            br.BadMesh = mesh;
            phase("check");
            count();
            if (bad.Count > 0)
                return Statuses.SlopeViolation;
            MaxSlopeRatio = maxRatio;
            if (Terrain == null)
                return Statuses.Success;

            // the overlay with the terrain (TERRAIN.md 5.5)
            checkCancel();
            var kinds = new Dictionary<int, int>();
            foreach (int e in mesh.BoundaryEdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(e);
                List<int> b0, b1;
                int kind = SlopeTerrainOverlay.KindOther;
                if (map.BarriersAt.TryGetValue(mesh.GetVertex(ev.a).xy, out b0) && map.BarriersAt.TryGetValue(mesh.GetVertex(ev.b).xy, out b1))
                    foreach (int bar in b0)
                        if (b1.Contains(bar)) { kind = bar < map.ToeBarrierCount ? SlopeTerrainOverlay.KindToe : SlopeTerrainOverlay.KindLateral; break; }
                kinds[e] = kind;
            }
            var overlay = new SlopeTerrainOverlay(mesh, Terrain, fill, kinds, CancelF);
            reachedCap = false;
            bool overlayOk = overlay.Compute();
            reachedCap = overlay.ReachedHeightLimit;
            if (overlayOk == false)
                return overlay.NotHeightField ? fail(Statuses.NotHeightField, overlay.Problem) : fail(Statuses.Failed, "overlay: " + overlay.Problem);
            br.CatchLines = overlay.CatchLines;
            br.Overlay = overlay;
            // the gradient and the contact of each catch-line segment (its uncertainty when Compute ends)
            for (int li = 0; li < overlay.CatchLines.Count; ++li) {
                CatchLine l = overlay.CatchLines[li];
                l.Gradient.Clear(); l.Tangential.Clear(); l.Uncertainty.Clear();
                foreach (int fi in overlay.CatchFaces[li]) {
                    double g; bool coplanar;
                    overlay.SegmentContact(fi, out g, out coplanar);
                    l.Gradient.Add(g); l.Tangential.Add(coplanar);
                }
            }
            overlay.Integrate(pieces.Count, out br.Volumes, out br.PlanAreas, out br.SlopeAreas);
            phase("overlay");
            // (a local height limit that the region reaches is raised and everything built again: no composite now)
            if (reachedCap && capMayGrow)
                return fail(Statuses.NoDaylight, "the region reaches the local height limit");
            foreach (var kv in overlay.PhaseSeconds) {
                double t0;
                PhaseSeconds.TryGetValue("overlay-" + kv.Key, out t0);
                PhaseSeconds["overlay-" + kv.Key] = t0 + kv.Value;
            }
            if (BuildComposite) {
                var comp = new SlopeComposite(overlay, pieces.Count, minFeature, CancelF) { CopyTerrain = CompositeKind == CompositeKinds.Full };
                if (comp.Build() == false)
                    return fail(Statuses.CompositeFailed, "composite: " + comp.Problem);
                // the clipped slope triangles get the same exact check (O1)
                foreach (int tid in comp.Region.TriangleIndices()) {
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, cc = Vector3d.Zero;
                    comp.Region.GetTriVertices(tid, ref a, ref b, ref cc);
                    InPiece pc = pieces[comp.Region.GetTriangleGroup(tid)];
                    if (fill) { a.z = -a.z; b.z = -b.z; cc.z = -cc.z; }
                    bool slopeOk = ExactPredicates.Orient2d(a.xy, b.xy, cc.xy) > 0 && Slope.IsNotSteeper(a, b, cc, pc.Spec);
                    bool supportOk = Slope.IsPlaneOnOrBelow(a, b, cc, pc.A) && Slope.IsPlaneOnOrBelow(a, b, cc, pc.B);
                    if (slopeOk == false) br.SlopeFailures++;
                    if (supportOk == false)
                        br.MaxExcess = Math.Max(br.MaxExcess, Math.Max(plane_excess(a, b, cc, pc.A), plane_excess(a, b, cc, pc.B)));
                    if (slopeOk == false || supportOk == false)
                        bad.Add(tid);
                }
                phase("composite");
                if (bad.Count > 0) {
                    br.BadMesh = comp.Region;
                    return Statuses.SlopeViolation;
                }
                br.Region = comp.Region;
                br.Composite = comp.Composite;
                br.Replaced = comp.Replaced.ToList();
                br.TerrainVertexOf = comp.TerrainVertexOf;
                br.CompositeVertexOf = comp.CompositeVertexOf;
            }
            return overlay.NoDaylight ? fail(Statuses.NoDaylight, "the slope does not reach the terrain everywhere within its footprint") : Statuses.Success;
        }

        // every boundary edge of the realized surface lies on a toe or lateral limit (both ends on a barrier) or on the
        // level line of the cap (both ends on the cap's plane): no crack inside
        static bool boundary_is_toe_limit_or_cap(DMesh3 mesh, SlopeMap map, int cap)
        {
            foreach (int e in mesh.BoundaryEdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(e);
                bool bar = map.OnBarrier[ev.a] && map.OnBarrier[ev.b];
                bool level = map.OutLabels[ev.a].Contains(cap) && map.OutLabels[ev.b].Contains(cap);
                if (bar == false && level == false) return false;
            }
            return true;
        }

        // does triangle t have the directed edge a -> b?
        static bool runs_along(Index3i t, int a, int b)
        {
            return (t.a == a && t.b == b) || (t.b == a && t.c == b) || (t.c == a && t.a == b);
        }

        // the plane of (a, b, c) at p's XY minus p.z, its positive part (for the measured toe lowering): exact, rounded
        // to nearest, so a triangle that fails the support check has an excess > 0 even where doubles cannot see it
        static double plane_excess(Vector3d a, Vector3d b, Vector3d c, Vector3d p)
        {
            ExactDyadic ax = ExactDyadic.Of(a.x), ay = ExactDyadic.Of(a.y), az = ExactDyadic.Of(a.z);
            ExactDyadic ux = ExactDyadic.Of(b.x) - ax, uy = ExactDyadic.Of(b.y) - ay, uz = ExactDyadic.Of(b.z) - az;
            ExactDyadic wx = ExactDyadic.Of(c.x) - ax, wy = ExactDyadic.Of(c.y) - ay, wz = ExactDyadic.Of(c.z) - az;
            ExactDyadic nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
            if (nz.Sign == 0) return 0;
            // (z_plane - pz) nz = az nz - nx (px - ax) - ny (py - ay) - pz nz
            ExactDyadic num = (az - ExactDyadic.Of(p.z)) * nz - nx * (ExactDyadic.Of(p.x) - ax) - ny * (ExactDyadic.Of(p.y) - ay);
            double e = ExactDyadic.Quotient(num, nz);
            return (MathUtil.IsFinite(e) && e > 0) ? e : 0;
        }

        // an upper bound of slope / spec slope (the triangle passed the exact check, so at most 1): the double ratio,
        // increased, if the exact check proves it, else 1
        static double ratio_bound(Vector3d a, Vector3d b, Vector3d c, SlopeSpec spec)
        {
            Vector3d n = (b - a).Cross(c - a);
            double r = Math.Sqrt(n.x * n.x + n.y * n.y) / Math.Abs(n.z) / spec.S * (1 + 1e-9);
            if (!(r < 1) || spec.ExactlyCheckable == false)
                return 1;
            double limit = ExactInterval.Up(r * spec.S);
            return Slope.IsNotSteeper(a, b, c, SlopeSpec.Rise(limit)) ? Math.Min(1, RatioOfLimit(limit, spec.S)) : 1;
        }

        // the merge of the realization (TERRAIN_1B_PLAN.md, reviews): positions nearer than 64 e_xy are one vertex; the
        // triangles of a moved vertex leave their cells, below S̃ by at most 4 sMax (r + e_xy), which the budget
        // reserves, and above it by at most 2 sMax (r + e_xy), which the toe lowering of a merging build covers
        internal static double MergeRadius(double exy) { return 64 * exy; }
        // the below share: a merge 4 sMax (r + e_xy), the needle flips at most FlipShare (each flip changes the surface
        // most at the crossing of the quad's diagonals; FlipNeedles adds these changes per triangle and refuses a flip
        // beyond the share)
        internal static double MergeShare(double exy, double sMax) { return 4 * sMax * (MergeRadius(exy) + exy) + FlipShare(exy, sMax); }
        // the largest change of the surface by needle flips, which FlipNeedles enforces (above and below)
        internal static double FlipShare(double exy, double sMax) { return 2 * sMax * (MergeRadius(exy) + exy); }
        // the lowering a realization with k kinds of repair needs (merge, flips): the rounding of a vertex and
        // 2 sMax (r + e_xy) above S̃ for each
        internal static double RepairLambda(double ez, double exy, double sMax, int k) { return 4 * ez + 2 * k * sMax * (MergeRadius(exy) + exy); }
        internal static double MergeLambda(double ez, double exy, double sMax) { return RepairLambda(ez, exy, sMax, 1); }
        // the merge radius of the band (TERRAIN.md 5.4): at most MinFeatureSize; within the facet share's slack the part
        // below S̃ of a band merge after a merge within r, 4 sMax (r + r_band + e_xy); within λ's bound its lowering and
        // today's flips; at least today's r (then no band merges). Rounded down (4 sMax and 2 sMax are exact)
        internal static double BandMergeRadius(double exy, double ez, double sMax, double minFeature, double facetSlack, double lambdaBound)
        {
            double r = MergeRadius(exy);
            double bySlack = ExactInterval.Down(ExactInterval.Down(ExactInterval.Down(facetSlack / (4 * sMax)) - r) - exy);
            double byLambda = ExactInterval.Down(ExactInterval.Down(ExactInterval.Down(ExactInterval.Down(lambdaBound - RepairLambdaUp(ez, exy, sMax, 1)) / (2 * sMax)) - exy) - r);
            return Math.Max(r, Math.Min(minFeature, Math.Min(bySlack, byLambda)));
        }
        // RepairLambda rounded up (an upper bound of 4 ez + 2 k sMax (r + e_xy))
        internal static double RepairLambdaUp(double ez, double exy, double sMax, int k)
        {
            return ExactInterval.Up(ExactInterval.Up(4 * ez) + ExactInterval.Up(2 * k * sMax * ExactInterval.Up(MergeRadius(exy) + exy)));
        }
        // the lowering the repairs need with band merges (TERRAIN.md 5.4), rounded up: the rounding of a vertex, the
        // merges' move l (r + the band length when both kinds merged), and today's flips (2 sMax (r + e_xy))
        internal static double BandRepairLambda(double ez, double exy, double sMax, double l, int flips)
        {
            double r = MergeRadius(exy);
            double merge = ExactInterval.Up(2 * sMax * ExactInterval.Up(Math.Max(l, r) + exy));
            double flip = flips > 0 ? ExactInterval.Up(2 * sMax * ExactInterval.Up(r + exy)) : 0;
            return ExactInterval.Up(ExactInterval.Up(ExactInterval.Up(4 * ez) + merge) + flip);
        }
        /// <summary> diagnostics: the caps tried by the local height limit (1: not raised; 0 without it) </summary>
        internal int CapRounds;
        /// <summary> tests: the rounds of the start estimate of the local height limit (0: the highest terrain at the toe) </summary>
        internal int CapEstimateRounds = 16;
        /// <summary> diagnostics of the last realization: needles flipped </summary>
        internal int NeedleFlips;
        /// <summary> diagnostics of the last realization: the largest move of an original position by band merges (an upper bound) </summary>
        internal double MaxMergeDistance;
        /// <summary> diagnostics of the last build: today's merge radius r = 64 e_xy, and the band's </summary>
        internal double UsedMergeRadius, UsedBandMergeRadius;
        /// <summary> diagnostics of the last realization: the targeted merges of the band (TERRAIN.md 5.4) </summary>
        internal int BandMerges;
        /// <summary> diagnostics: per failed build (attempt), the XY altitudes of its failing triangles </summary>
        internal List<List<double>> AttemptProblemAltitudes = new List<List<double>>();
        /// <summary> diagnostics of the last build: positions merged by the realization, and the lowering that requires </summary>
        internal int MergedPositions;
        /// <summary> tests and diagnostics: the slope surface of the last realization (also a failing one) </summary>
        internal DMesh3 LastRealized;
        internal double MergeLambdaRequired;

        /// <summary> an upper bound of limit / s: rounded to nearest, one ulp up (s &gt; 0) </summary>
        internal static double RatioOfLimit(double limit, double s)
        {
            return ExactInterval.Up(limit / s);
        }


        sealed class StripRef
        {
            public InPiece Piece;
            public Vector3d P, Q, R;      // the plane's points
            public double Angle;          // direction of its gradient
        }

        // The strip planes through both lowered end points, both sides, checked exactly: not steeper than the built
        // slope (they pass through both end points). Also ToeSteeperThanSlope.
        Statuses make_strips(ExactSlopeKernel kernel, InPiece pc, Dictionary<Vector3d, List<StripRef>> stripsAt)
        {
            Vector3d A = pc.A1, B = pc.B1;
            double dx = B.x - A.x, dy = B.y - A.y, L = Math.Sqrt(dx * dx + dy * dy);
            var sb = SlopeSpec.Rise(pc.Sb);
            // toe steeper than the built slope: dz^2 >= sb^2 (dx^2 + dy^2), exactly on the lowered points
            ExactDyadic edx = ExactDyadic.Of(B.x) - ExactDyadic.Of(A.x), edy = ExactDyadic.Of(B.y) - ExactDyadic.Of(A.y),
                edz = ExactDyadic.Of(B.z) - ExactDyadic.Of(A.z), es = ExactDyadic.Of(pc.Sb);
            if ((edz * edz - es * es * (edx * edx + edy * edy)).Sign >= 0) {
                ToeProblems.Add(new Index2i(pc.Toe, pc.Segment));
                return fail(Statuses.ToeSteeperThanSlope, $"toe {pc.Toe} segment {pc.Segment} is at least as steep as its slope");
            }
            Vector2d u = new Vector2d(dx / L, dy / L), n = new Vector2d(-u.y, u.x);
            double g = (B.z - A.z) / L;
            double gn = Math.Sqrt(Math.Max(0, pc.Sb * pc.Sb - g * g));
            foreach (double sign in new[] { 1.0, -1.0 }) {
                double rise = gn * L;
                for (int tries = 0; ; ++tries) {
                    Vector3d Q = new Vector3d(B.x + sign * n.x * L, B.y + sign * n.y * L, B.z + rise);
                    if (Slope.IsNotSteeper(A, B, Q, sb)) {
                        pc.Strip.Add(kernel.AddPlane(A, B, Q));
                        Vector2d G = g * u + sign * gn * n;
                        var sr = new StripRef() { Piece = pc, P = A, Q = B, R = Q, Angle = Math.Atan2(G.y, G.x) };
                        foreach (Vector3d apex in new[] { A, B }) {
                            List<StripRef> l;
                            if (stripsAt.TryGetValue(apex, out l) == false) { l = new List<StripRef>(); stripsAt[apex] = l; }
                            l.Add(sr);
                        }
                        break;
                    }
                    rise *= 1 - 1e-12 * Math.Pow(2, tries);     // (doubling steps: any rounding of Q is covered)
                    if (tries > 60) throw new InvalidOperationException("TerrainSlopeDesign: strip plane");
                }
            }
            // lower bound of the gradient away from the piece (prefilter of the insertion): at least
            // s (1 - g^2 / s^2) cos(2 theta) along any direction (the strip plane's component, or a cone tangent)
            pc.Slb = pc.Sb * (1 - g * g / (pc.Sb * pc.Sb));
            return Statuses.Success;
        }

        // The cone tangent planes: at B the directions d with d.u >= g / sb, at A those with d.u <= g / sb, on the global
        // angle grid, through the apex and two points at the distance rFan (the same points for the same apex,
        // direction and slope, whatever the piece). Each checked exactly: not steeper than the built slope, on or below
        // the other end point. A direction within theta / 2 of a strip plane at the same apex is replaced by a copy of
        // that strip plane if it passes the checks for this piece (else dropped): nearly coplanar planes through one
        // point would cross along arbitrary lines.
        void make_fans(ExactSlopeKernel kernel, InPiece pc, double theta, int nTheta, double rFan, Dictionary<Vector3d, List<StripRef>> stripsAt)
        {
            Vector3d A = pc.A1, B = pc.B1;
            double dx = B.x - A.x, dy = B.y - A.y, L = Math.Sqrt(dx * dx + dy * dy);
            double g = (B.z - A.z) / L;
            double phiU = Math.Atan2(dy, dx);
            double betaB = Math.Acos(Math.Max(-1, Math.Min(1, g / pc.Sb)));
            var sb = SlopeSpec.Rise(pc.Sb);
            fans(kernel, pc, B, A, phiU, betaB, theta, nTheta, rFan, sb, stripsAt[B], pc.FanB, pc.AngB);
            fans(kernel, pc, A, B, phiU + Math.PI, Math.PI - betaB, theta, nTheta, rFan, sb, stripsAt[A], pc.FanA, pc.AngA);
            pc.Slb *= Math.Cos(2 * theta) * (1 - 1e-6);
        }

        static void fans(ExactSlopeKernel kernel, InPiece pc, Vector3d apex, Vector3d other, double center, double half,
                         double theta, int nTheta, double R, SlopeSpec sb, List<StripRef> strips, List<int> planes, List<double> angles)
        {
            int m0 = (int)Math.Ceiling((center - half) / theta), m1 = (int)Math.Floor((center + half) / theta);
            var used = new HashSet<StripRef>();
            for (int m = m0; m <= m1; ++m) {
                // (the angle of grid step m, the same double for every piece)
                int mm = ((m % nTheta) + nTheta) % nTheta;
                double a = mm * (2 * Math.PI / nTheta);
                StripRef near = null;
                foreach (var sr in strips)
                    if (Math.Abs(wrap(sr.Angle - a)) < 0.5 * theta) near = sr;
                if (near != null) {
                    if (near.Piece == pc || used.Contains(near)) continue;
                    used.Add(near);
                    if (Slope.IsNotSteeper(near.P, near.Q, near.R, sb) && Slope.IsPlaneOnOrBelow(near.P, near.Q, near.R, other)
                        && Slope.IsPlaneOnOrBelow(near.P, near.Q, near.R, apex)) {
                        planes.Add(kernel.AddPlane(near.P, near.Q, near.R));
                        angles.Add(near.Angle);
                    }
                    continue;
                }
                Vector2d d = new Vector2d(Math.Cos(a), Math.Sin(a));
                double rise = pc.Sb * R;
                Vector3d p1 = new Vector3d(apex.x - d.y * R, apex.y + d.x * R, apex.z);
                for (int tries = 0; tries < 60; ++tries) {
                    Vector3d p2 = new Vector3d(apex.x + d.x * R, apex.y + d.y * R, apex.z + rise);
                    if (Slope.IsNotSteeper(apex, p1, p2, sb)) {
                        if (Slope.IsPlaneOnOrBelow(apex, p1, p2, other)) {
                            planes.Add(kernel.AddPlane(apex, p1, p2));
                            angles.Add(a);
                        }
                        break;
                    }
                    rise *= 1 - 1e-12 * Math.Pow(2, tries);     // (doubling steps: any rounding of Q is covered)
                }
            }
        }

        static double wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a <= -Math.PI) a += 2 * Math.PI;
            return a;
        }
    }


    /// <summary> exact validation of the toe polylines (TERRAIN.md 5.6): no crossings or touching, no overlaps </summary>
    internal static class ToeGeometry
    {
        public static bool Validate(List<List<Vector3d>> toes, List<bool> closed, out string why, out Index2i where)
        {
            why = ""; where = new Index2i(-1, -1);
            var segs = new List<(int toe, int i, Vector2d a, Vector2d b, int ia, int ib)>();
            for (int t = 0; t < toes.Count; ++t) {
                int n = toes[t].Count, m = closed[t] ? n : n - 1;
                for (int i = 0; i < m; ++i)
                    segs.Add((t, i, toes[t][i].xy, toes[t][(i + 1) % n].xy, i, (i + 1) % n));
            }
            // sweep in x
            var order = Enumerable.Range(0, segs.Count).OrderBy(k => Math.Min(segs[k].a.x, segs[k].b.x)).ThenBy(k => k).ToList();
            var active = new List<int>();
            foreach (int k in order) {
                var s = segs[k];
                double xmin = Math.Min(s.a.x, s.b.x);
                active.RemoveAll(j => Math.Max(segs[j].a.x, segs[j].b.x) < xmin);
                foreach (int j in active) {
                    var r = segs[j];
                    if (Math.Max(Math.Min(s.a.y, s.b.y), Math.Min(r.a.y, r.b.y)) > Math.Min(Math.Max(s.a.y, s.b.y), Math.Max(r.a.y, r.b.y)))
                        continue;
                    bool adjacent = s.toe == r.toe && (s.ia == r.ib || s.ib == r.ia);
                    if (adjacent) {
                        if (s.ia == r.ib && s.ib == r.ia) { why = "toe with two points"; where = new Index2i(s.toe, s.i); return false; }
                        // shared point p, other ends x (of s) and y (of r): overlap if y is on the ray from p towards x
                        Vector2d p = s.ia == r.ib ? s.a : s.b, x = s.ia == r.ib ? s.b : s.a, y = s.ia == r.ib ? r.a : r.b;
                        if (ExactPredicates.Orient2d(p, x, y) == 0 && dot_sign(x - p, y - p, p, x, y) > 0) {
                            why = "toe segments overlap (fold back)"; where = new Index2i(s.toe, Math.Max(s.i, r.i)); return false;
                        }
                        continue;
                    }
                    if (touch(s.a, s.b, r.a, r.b)) {
                        why = (s.toe == r.toe) ? "toe self-intersects or touches itself in XY" : $"toes {r.toe} and {s.toe} cross or touch";
                        where = new Index2i(s.toe, s.i);
                        return false;
                    }
                }
                active.Add(k);
            }
            return true;
        }

        // sign of (x - p) . (y - p), exactly
        static int dot_sign(Vector2d u, Vector2d v, Vector2d p, Vector2d x, Vector2d y)
        {
            ExactDyadic ux = ExactDyadic.Of(x.x) - ExactDyadic.Of(p.x), uy = ExactDyadic.Of(x.y) - ExactDyadic.Of(p.y);
            ExactDyadic vx = ExactDyadic.Of(y.x) - ExactDyadic.Of(p.x), vy = ExactDyadic.Of(y.y) - ExactDyadic.Of(p.y);
            return (ux * vx + uy * vy).Sign;
        }

        // closed segments intersect (touching counts)
        static bool touch(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            int o1 = ExactPredicates.Orient2d(a, b, c), o2 = ExactPredicates.Orient2d(a, b, d);
            int o3 = ExactPredicates.Orient2d(c, d, a), o4 = ExactPredicates.Orient2d(c, d, b);
            if (o1 * o2 < 0 && o3 * o4 < 0) return true;
            if (o1 == 0 && on_segment(a, b, c)) return true;
            if (o2 == 0 && on_segment(a, b, d)) return true;
            if (o3 == 0 && on_segment(c, d, a)) return true;
            if (o4 == 0 && on_segment(c, d, b)) return true;
            return false;
        }

        // p collinear with a, b: within the closed segment (coordinates compare exactly)
        static bool on_segment(Vector2d a, Vector2d b, Vector2d p)
        {
            return Math.Min(a.x, b.x) <= p.x && p.x <= Math.Max(a.x, b.x) && Math.Min(a.y, b.y) <= p.y && p.y <= Math.Max(a.y, b.y);
        }
    }
}
