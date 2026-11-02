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
    }

    /// <summary>
    /// Slope surface (Böschung) from toe lines, as the lower envelope of the slope cones with apexes on the toe (cut;
    /// the upper envelope of the falling cones for fill): docs/design/TERRAIN.md, phase 1a
    /// (docs/design/TERRAIN_1A_PLAN.md). Mode: cone envelope (R1). In phase 1a there is no terrain: the surface is
    /// built up to HeightLimit (cut) or down to it (fill).
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
            /// <summary> not in phase 1a: Direction Auto, a slope that changes along a segment </summary>
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
            /// <summary> the terrain is not a height field: triangles degenerate in XY or overlapping in XY </summary>
            NotHeightField,
            /// <summary>
            /// the composite could not be built (Message): SlopeSurface and CatchLines are kept and complete, Composite and
            /// SlopeRegion are null
            /// </summary>
            CompositeFailed,
            Cancelled,
            /// <summary> an internal error (Message); no result </summary>
            Failed
        }

        // inputs
        public List<SlopeToe> Toes = new List<SlopeToe>();
        /// <summary>
        /// optional: the terrain (a TIN, height field). With it the catch lines are computed (TERRAIN.md 5.5), and
        /// HeightLimit may be NaN: cut then builds up to the terrain's highest vertex, fill down to its lowest
        /// </summary>
        public DMesh3 Terrain;
        /// <summary> with Terrain: also build SlopeRegion and Composite </summary>
        public bool BuildComposite = false;
        public Directions Direction = Directions.Cut;
        /// <summary> required without Terrain: cut builds the slope up to this height, fill down to it </summary>
        public double HeightLimit = double.NaN;
        /// <summary> required: the largest vertical deviation below the exact cone envelope (data units) </summary>
        public double Tolerance = double.NaN;
        /// <summary> the relative slope margin δ: the planes are built with slope s (1 - δ); raised if the check needs it </summary>
        public double SlopeMargin = 1e-6;
        public Func<bool> CancelF = () => false;

        // outputs
        public Statuses Status;
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
            Pieces = new List<SlopePiece>(); ToeProblems = new List<Index2i>(); ProblemTriangles = new List<int>();
            MaxSlopeRatio = 0; UsedSlopeMargin = UsedToeLowering = UsedMinFeatureSize = FacetAngle = 0;
            PhaseSeconds = new Dictionary<string, double>(); Attempts = 0; Realizations = 0; PredicateCalls = ExactFallbacks = ExactConstructions = 0;
            MergedPositions = 0; MergeLambdaRequired = 0;
            try {
                Status = compute();
            } catch (OperationCanceledException) {
                Status = Statuses.Cancelled;
            } catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is OverflowException) {
                // (an internal inconsistency; never a result)
                Status = Statuses.Failed;
                Message = e.Message;
            }
            if (Status != Statuses.Success && Status != Statuses.NoDaylight && Status != Statuses.CompositeFailed) {
                SlopeSurface = null; SlopeRegion = null; Composite = null; CatchLines = new List<CatchLine>();
            }
            IsComplete = Status == Statuses.Success;
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

        Statuses compute()
        {
            if (Direction == Directions.Auto)
                return fail(Statuses.NotImplemented, "Direction Auto (mixed cut and fill) comes in phase 1b");
            bool fill = Direction == Directions.Fill;
            if (Toes == null || Toes.Count == 0)
                return fail(Statuses.InvalidInput, "no toes");
            if (!(Tolerance > 0) || double.IsInfinity(Tolerance))
                return fail(Statuses.InvalidInput, "Tolerance must be positive and finite (no default)");
            if (!(SlopeMargin > 0) || !(SlopeMargin <= 0.01))
                return fail(Statuses.InvalidInput, "SlopeMargin must be in (0, 0.01]");
            double heightLimit = HeightLimit;
            if (Terrain != null) {
                Statuses ts = check_terrain();
                if (ts != Statuses.Success)
                    return ts;
            }
            if (Terrain != null && double.IsNaN(heightLimit)) {
                // above the highest terrain vertex (cut) and the toe, by Tolerance: at the level line of the cap S̃ is then
                // above the terrain, also after rounding, so the region cannot reach it
                double ext = fill ? double.MaxValue : double.MinValue, toeExt = ext;
                foreach (Vector3d v in Terrain.Vertices()) ext = fill ? Math.Min(ext, v.z) : Math.Max(ext, v.z);
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
                        SlopeRegion = br.Region;
                        Composite = br.Composite;
                        Pieces = pieces.Select(p => new SlopePiece() {
                            Toe = p.Toe, Segment = p.Segment, Spec = p.Spec,
                            P0 = fill ? new Vector3d(p.A.x, p.A.y, -p.A.z) : p.A, P1 = fill ? new Vector3d(p.B.x, p.B.y, -p.B.z) : p.B
                        }).ToList();
                    }
                    return st;
                }
                ProblemTriangles = br.Bad;
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

        sealed class BuildResult
        {
            public DMesh3 Mesh;
            public List<int> Bad = new List<int>();
            public int SlopeFailures;          // slope (i) or XY orientation
            public double MaxExcess;           // largest height of a failing triangle's plane above its toe ends (ii)
            public double Lambda, LambdaBound;
            public double MergeLambda;
            public double RebuildLambda;       // RebuildForEachLowering: build again with this lowering         // > 0: build again with at least this lowering (the realization merged)
            public List<CatchLine> CatchLines = new List<CatchLine>();
            public DMesh3 Region, Composite;
        }

        Statuses check_terrain()
        {
            foreach (Vector3d v in Terrain.Vertices())
                if (MathUtil.IsFinite(v.x) == false || MathUtil.IsFinite(v.y) == false || MathUtil.IsFinite(v.z) == false)
                    return fail(Statuses.InvalidInput, "a terrain vertex is not finite");
            if (Terrain.TriangleCount == 0)
                return fail(Statuses.InvalidInput, "the terrain has no triangles");
            foreach (int t in Terrain.TriangleIndices()) {
                Index3i tri = Terrain.GetTriangle(t);
                if (ExactPredicates.Orient2d(Terrain.GetVertex(tri.a).xy, Terrain.GetVertex(tri.b).xy, Terrain.GetVertex(tri.c).xy) == 0)
                    return fail(Statuses.NotHeightField, $"terrain triangle {t} is degenerate in XY");
            }
            int overlaps = TerrainVolumeDifference.XYOverlaps(Terrain);
            if (overlaps > 0)
                return fail(Statuses.NotHeightField, $"{overlaps} pairs of terrain triangles overlap in XY");
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
            // (the reach does not depend on λ: the map is built once per margin δ)
            double dMax = (H - zMin + lambdaBound) / (sMin * (1 - delta) * Math.Cos(theta)) * 1.01;

            // The toe lowering λ (TERRAIN_1C_PLAN.md, idea A). Lowering every plane by λ leaves the line of any two
            // planes unchanged (Nz_a D_b - Nz_b D_a does not change when both D grow by λ Nz), so the map is built once
            // from the specified toe and realized with z lowered by λ, exactly rounded; a larger λ costs a realization,
            // not a build. The cap is not lowered (the surface ends at H - λ, never above H); its lines are the only
            // ones a lowering would move. The checks at construction (slope, support of a plane at its piece's ends,
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
            // insert the pieces
            for (int k = 0; k < pieces.Count; ++k) {
                checkCancel();
                map.InsertPiece(k, pieces[k].Strip, pieces[k].FanA, pieces[k].AngA, pieces[k].FanB, pieces[k].AngB,
                    pieces[k].A1, pieces[k].B1, Math.Min(pieces[k].A1.z, pieces[k].B1.z), pieces[k].Slb, pieces[k].Reach, theta);
            }

            phase("insert");
            map.Simplify();
            phase("simplify");

            // toe undercut: a face on a toe segment labelled by another piece, lower than the toe by more than Tolerance
            var undercut = map.Undercuts(pieces.Select(p => new Vector3d[] { p.A1, p.B1 }).ToList(), Tolerance);
            if (undercut.Count > 0) {
                foreach (int k in undercut) ToeProblems.Add(new Index2i(pieces[k].Toe, pieces[k].Segment));
                return fail(Statuses.ToeUndercut, $"{undercut.Count} toe segments are undercut by the cones of other parts of the toe");
            }

            // realization and the exact check, raising λ by measurement without a new build
            Attempts++;
            double mergeLambda = MergeLambda(ez, exy, sMax);
            for (int realization = 0; ; ++realization) {
                br = new BuildResult() { Lambda = lambda, LambdaBound = lambdaBound };
                UsedToeLowering = lambda;
                Statuses st = realize_and_check(map, kernel, cap, pieces, fill, lambda, lambdaBound, mergeLambda, minFeature, mergeRadius, br, phase);
                Realizations++;
                if (st != Statuses.SlopeViolation)
                    return st;
                double next = -1;
                if (br.MergeLambda > 0 && lambda < br.MergeLambda)
                    next = br.MergeLambda;
                // a support failure (ii) only: a larger lowering, measured on the output, while it fits under the bound;
                // anything else goes back to the caller, which raises the margin δ and builds again
                else if (realization < 7 && br.SlopeFailures == 0 && br.MaxExcess > 0 && 4 * br.MaxExcess <= lambdaBound && lambda < lambdaBound)
                    next = Math.Min(lambdaBound, Math.Max(2 * lambda, 4 * br.MaxExcess));
                if (next < 0)
                    return st;
                if (RebuildForEachLowering) {
                    // (the reference of the tests: a new build at the new lowering, as before 1c)
                    br.RebuildLambda = next;
                    return st;
                }
                lambda = next;
            }
        }

        /// <summary> tests: build the map again for every new toe lowering instead of realizing it again </summary>
        internal bool RebuildForEachLowering;

        // one realization at the lowering λ, its exact check, and (with a terrain) the overlay and the composite
        Statuses realize_and_check(SlopeMap map, ExactSlopeKernel kernel, int cap, List<InPiece> pieces, bool fill, double lambda,
                                   double lambdaBound, double mergeLambda, double minFeature, double mergeRadius, BuildResult br,
                                   Action<string> phase)
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
            bool mayMerge = mergeLambda <= lambdaBound;
            try {
                mesh = map.Realize(cap, fill, mayMerge ? mergeRadius : 0, lambda);
            } catch (SlopeMap.NonManifoldRealization) when (mayMerge) {
                mesh = map.Realize(cap, fill, 0, lambda);
                map.MergedCount = 0;
            }
            MergedPositions = map.MergedCount; MergeLambdaRequired = map.MergedCount > 0 ? mergeLambda : 0;
            if (map.MergedCount > 0 && lambda < mergeLambda) {
                br.MergeLambda = mergeLambda;
                phase("realize");
                count();
                return Statuses.SlopeViolation;
            }
            SlopeMap.CollapseShortEdges(mesh, map.OutLabels, map.OnBarrier, minFeature);
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
            if (overlay.Compute() == false)
                return fail(Statuses.Failed, "overlay: " + overlay.Problem);
            br.CatchLines = overlay.CatchLines;
            phase("overlay");
            if (BuildComposite) {
                var comp = new SlopeComposite(overlay, pieces.Count, minFeature, CancelF);
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
                if (bad.Count > 0)
                    return Statuses.SlopeViolation;
                br.Region = comp.Region;
                br.Composite = comp.Composite;
            }
            return overlay.NoDaylight ? fail(Statuses.NoDaylight, "the slope does not reach the terrain everywhere within its footprint") : Statuses.Success;
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
        internal static double MergeShare(double exy, double sMax) { return 4 * sMax * (MergeRadius(exy) + exy); }
        internal static double MergeLambda(double ez, double exy, double sMax) { return 4 * ez + 2 * sMax * (MergeRadius(exy) + exy); }
        /// <summary> diagnostics of the last build: positions merged by the realization, and the lowering that requires </summary>
        internal int MergedPositions;
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
