using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Meshing2Acceptance
{
    /// <summary>
    /// Judges a triangulation by the definitions in docs/design/MESHING2D_SPEC.md, using only exact integers. It shares no
    /// code with the mesher (the project references nothing of the library) and calls no validity check of the result.
    /// One entry point for all milestones; what is judged follows from the input and <see cref="OracleExpect"/>.
    /// Soundness of the local Delaunay check: in a valid triangulation, every free interior edge being locally Delaunay is
    /// equivalent to the (constrained) Delaunay property (Delaunay's lemma; Lee and Lin 1986; Chew 1989). Validity
    /// itself (orientation, edge manifold, no vertex in a triangle, no crossing, exact area) is checked first and separately.
    /// </summary>
    public static partial class Oracle
    {
        /// <summary> judge the grid split's pinch rule by the envelope approximation that the chain identity of the output's graph replaced (per thread; differential tests only) </summary>
        public static bool LegacyEnvelopeChainForDifferential { get => Run.LegacyEnvelopeChainForDifferential; set => Run.LegacyEnvelopeChainForDifferential = value; }

        /// <summary> the piece's own chain (OwnStretch) on a given graph, for the self-checks </summary>
        public static HashSet<long> StretchOfForSelfCheck(int ia, int ib, Dictionary<int, List<int>> neighbours, Func<int, bool> isHot) => Run.StretchOf(ia, ib, neighbours, isHot);

        /// <summary> the key of a subsegment in a stretch (StretchOfForSelfCheck), the same for either orientation </summary>
        public static long SubsegmentKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>
        /// The mesher's pinch rule as the oracle applies it to a candidate split pixel q (MESHING2D_HULL_GRID.md section 7): q is no
        /// split point when it is no hot pixel of the input's own rounding and a subsegment of another chain ends there (the piece routed
        /// through it would pinch the two chains). A hot pixel of the input's rounding stays a split point (snap rounding routes chains
        /// together there), and so does a pixel where only subsegments of the piece's own chain end. Given whether q is such a hot
        /// pixel and, for each subsegment ending in q's pixel, whether it belongs to the piece's own chain
        /// </summary>
        public static bool PinchRuleExcludes(bool inputHotPixel, IEnumerable<bool> subsegmentsAtQOwnChain)
            => !inputHotPixel && subsegmentsAtQOwnChain.Any(own => !own);

        /// <summary>
        /// The lobe bound of rule (ii') (2026-11-10): the nodes of a rerouted stretch, between its ends a and b, all lie within one pixel of
        /// the chord a-b (pixel centres; squared distances compared exactly)
        /// </summary>
        public static bool LobeWithinTube(Pt a, Pt b, IEnumerable<Pt> between, Q pixel)
        {
            Q tube = pixel * pixel;
            foreach (var p in between) if (Exact.Dist2ToSegment(a, b, p) > tube) return false;
            return true;
        }

        /// <summary> the lobe bound for a point: every link at its pixel ends in a neighbouring pixel (index distance at most 1 per axis) </summary>
        public static bool LinksStayAdjacent((BigInteger x, BigInteger y) pixel, IEnumerable<(BigInteger x, BigInteger y)> ends)
        {
            foreach (var q in ends) if (BigInteger.Abs(q.x - pixel.x) > 1 || BigInteger.Abs(q.y - pixel.y) > 1) return false;
            return true;
        }

        /// <summary> the smallest positive double, 2^-1074: the step of the subnormal grid </summary>
        static readonly Q SubnormalStep = new Q(1, BigInteger.One << 1074);

        /// <summary>
        /// The automatic tolerance a result may use: 2^-40 of the larger of the largest coordinate and the extent, about 5
        /// micrometres at UTM northings (spec O11: "about 5 um at UTM scale"; Q14), and never less than one step of the subnormal grid
        /// (2^-1074, decision D3 of 2026-11-05): below about 2^-1034 the relative tolerance is finer than any double can resolve, and a
        /// required construction (a crossing of two constraints) has in general no double within it (found with a procedural input of
        /// three nearly concurrent segments at 2^-1074, while fixing #388). One step suffices for a correctly rounded construction: it
        /// lies within half a step of the exact point in each coordinate, so within sqrt(2)/2 of a step; a construction rounded less
        /// carefully, off by a step in each coordinate (sqrt(2) steps), would still fail.
        /// </summary>
        public static Q AutoTolerance(Mesh2Input input)
        {
            var relative = RelativeTolerance(input);
            return input.Count == 0 ? relative : Q.Max(relative, SubnormalStep);
        }

        /// <summary> 2^-40 of the larger of the largest coordinate and the extent, without the floor of <see cref="AutoTolerance"/>: the
        /// scale of the mesher's own tolerance (2^-41 of the same magnitude), which has no floor </summary>
        internal static Q RelativeTolerance(Mesh2Input input)
        {
            Q max = Q.Zero;
            if (input.Count == 0) return max;
            Q minX = Q.FromDouble(input.X[0]), maxX = minX, minY = Q.FromDouble(input.Y[0]), maxY = minY;
            for (int i = 0; i < input.Count; ++i) {
                Q x = Q.FromDouble(input.X[i]), y = Q.FromDouble(input.Y[i]);
                max = Q.Max(max, Q.Max(Q.Abs(x), Q.Abs(y)));
                minX = Q.Min(minX, x); maxX = Q.Max(maxX, x); minY = Q.Min(minY, y); maxY = Q.Max(maxY, y);
            }
            // the larger of the largest coordinate and the extent, as documented (second review, finding 11)
            max = Q.Max(max, Q.Max(maxX - minX, maxY - minY));
            return max * new Q(1, BigInteger.One << 40);
        }

        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        public static OracleReport Check(Mesh2Input input, Mesh2Output o, OracleExpect e)
        {
            if (ForceReference && !e.Reference) e = e.WithReference();
            var (point, edge) = IndexSettings();
            return new Run(input, o, e) { PointIndex = point, EdgeIndex = edge }.Execute();
        }

        /// <summary> on this thread, every check takes the reference path (the tests run the self-checks so; --oracle reference) </summary>
        [ThreadStatic] public static bool ForceReference;

        /// <summary>
        /// tests only (per thread): every index and filter of the fast path is checked against the scan or exact test it replaces, which
        /// throws on a difference (InvalidOperationException, "fast path differs: ..."): the domain edges near a point, the sources near
        /// a triangle and a subsegment, the vertices and subsegments of a pixel, and the zone tests' filters at their call sites
        /// </summary>
        [ThreadStatic] public static bool CheckFastPathForTests;

        internal static void FastPathDiffers(string what) => throw new InvalidOperationException("fast path differs: " + what);

        // Binary64 outputs use certified geometric pruning by default. Keep the sweep for A/B diagnosis;
        // exact-coordinate/reference runs always retain their existing independent sweep.
        internal static bool UseConformityIndex(bool fast, string configured, string indexedName)
            => fast && IndexSetting("the oracle index", configured, indexedName) != "sweep";

        /// <summary>The environment variable that selects the point/triangle search: strtree (the default) or sweep.</summary>
        public const string PointIndexVariable = "G3_ORACLE_POINT_INDEX";
        /// <summary>The environment variable that selects the edge crossing search: strtree-hulls (the default) or sweep.</summary>
        public const string EdgeIndexVariable = "G3_ORACLE_EDGE_INDEX";

        /// <summary>A conformity index setting, normalized: unset, empty or blank is the default (the indexed name); case and surrounding
        /// blanks do not matter; anything but the indexed name or sweep throws with the variable, the value and the accepted values.</summary>
        internal static string IndexSetting(string variable, string value, string indexedName)
        {
            if (string.IsNullOrWhiteSpace(value)) return indexedName;
            string v = value.Trim().ToLowerInvariant();
            if (v != indexedName && v != "sweep")
                throw new InvalidOperationException(variable + "='" + value + "' is not a conformity index: use " + indexedName + " (the default) or sweep");
            return v;
        }

        /// <summary>Reads and checks both index variables (Oracle.Check does so before any work, the command line at its start), so a
        /// wrong value fails at once with a clear message instead of in the middle of a long judgment.</summary>
        public static (string point, string edge) IndexSettings() => (
            IndexSetting(PointIndexVariable, Environment.GetEnvironmentVariable(PointIndexVariable), "strtree"),
            IndexSetting(EdgeIndexVariable, Environment.GetEnvironmentVariable(EdgeIndexVariable), "strtree-hulls"));

        /// <summary> self-checks: <see cref="Run.PassesPixel"/> (O-H4's rule, and O-H5's images) </summary>
        internal static bool PassesPixel(Pt a, Pt b, Pt c, Q pixel) => Run.PassesPixel(a, b, c, pixel);
        /// <summary> self-checks: the iterated snap-rounded image of a-b among the pixels of the given hot points (of snapping, met by
        /// contact) and refined points (output vertices, met by the interior) (O-H5) </summary>
        internal static List<(BigInteger x, BigInteger y)> SnapImage(Pt a, Pt b, IEnumerable<Pt> hotPoints, Q pixel, IEnumerable<Pt> refinedPoints = null)
        {
            var hot = new HashSet<(BigInteger x, BigInteger y)>(); var list = new List<(BigInteger x, BigInteger y)>();
            foreach (var p in hotPoints.Concat(new[] { a, b })) { var n = (Run.PixelIndexOf(p.Qx, pixel), Run.PixelIndexOf(p.Qy, pixel)); if (hot.Add(n)) list.Add(n); }
            var snapped = new HashSet<(BigInteger x, BigInteger y)>(hot);
            foreach (var p in refinedPoints ?? new Pt[0]) { var n = (Run.PixelIndexOf(p.Qx, pixel), Run.PixelIndexOf(p.Qy, pixel)); if (hot.Add(n)) list.Add(n); }
            long work = 0;
            return Run.Image(a, b, hot, list, pixel, ref work, snapped);
        }
        /// <summary> test hook: <see cref="Run.ImageWorkForTests"/> </summary>
        internal static long ImageWorkForTests { get => Run.ImageWorkForTests; set => Run.ImageWorkForTests = value; }

        /// <summary> the double filter of "vertex within the tube of a source", for the self-checks: -1 farther, +1 within, 0 undecided </summary>
        public static int TubeFilter(double ax, double ay, double bx, double by, double px, double py, double tube2Lo, double tube2Hi) => Run.TubeFilter(ax, ay, bx, by, px, py, tube2Lo, tube2Hi);
        /// <summary> the double filter of "within the merge zone" (InMergeZone, distance to a source or to the apex): -1 farther, +1 within, 0 undecided </summary>
        public static int ZoneFilter(double ax, double ay, double bx, double by, double px, double py, double lo, double hi) => Run.ZoneFilter(ax, ay, bx, by, px, py, lo, hi);
        /// <summary> the bounds in doubles of a radius given by its square (the merge zone's) </summary>
        public static void RadiusBounds(Q r2, out double lo, out double hi) => Run.RadiusBounds(r2, out lo, out hi);
        /// <summary> the query box of DomainEdgesNear: every point within sqrt(d2) of p lies in it </summary>
        public static bool DistanceQueryBox(Pt p, Q d2, out double xl, out double xh, out double yl, out double yh) => Run.DistanceQueryBox(p, d2, out xl, out xh, out yl, out yh);
        /// <summary> the box in doubles around segment a-b (unknown: NaN), as the oracle's grids store it </summary>
        public static double[] SegmentBox(Pt a, Pt b) { var box = new double[4]; Run.SegmentBox(a, b, 0, box, 0); return box; }
        /// <summary> the decision of a zone test from the filter's answer and the exact test (called only when the filter cannot decide) </summary>
        public static bool WithinZone(int filter, Func<bool> exact) => Run.WithinZone(filter, exact);
        /// <summary> self-checks: <see cref="Run.AtLeastShewchukBound"/> </summary>
        internal static bool AtLeastShewchukBound(Pt q, Pt r, Pt s, Pt apex, Pt a, Pt b) => Run.AtLeastShewchukBound(q, r, s, apex, a, b);

        sealed partial class Run
        {
            readonly Mesh2Input input; readonly Mesh2Output o; readonly OracleExpect e;
            OracleReport r = new OracleReport();   // (not readonly: the check mode of the witness runs the full global pass into a scratch report)
            Pt[] inPts, v;
            Domain domain;
            Q tube, tube2;
            /// <summary> the squared tolerance the O11 rule gives the mesher (<see cref="OracleReport.O11Tolerance2"/>; a field, as the
            /// witness's check mode swaps the report) </summary>
            Q o11Tol2;
            int[] map;
            Dictionary<long, List<(int tri, int opposite, int a, int b)>> edges;
            HashSet<long> constraintEdges;
            List<(Pt a, Pt b, int marker, int sourceId, bool locked)> sources;
            HashSet<long> subsegmentSet;
            Q moved2 = Q.Zero;                 // the largest squared distance by which geometry really moved; 0 if nothing did
            int aliases;                       // merged input slots (identity rules): vertices in the list that are no vertices of the mesh
            List<Pt> effectiveSeeds = new List<Pt>();

            public Run(Mesh2Input input, Mesh2Output o, OracleExpect e)
            {
                this.input = input; this.o = o; this.e = e;
                indexSourceCoverage = SourceIndexSetting() != "scan";
                SourceWorkForTests = 0;
                SourceGridWorkForTests = 0;
            }
            /// <summary> the conformity searches (see Oracle.IndexSettings), read once before the run </summary>
            public string PointIndex, EdgeIndex;

            static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            static int KeyA(long k) => (int)(k >> 32);
            static int KeyB(long k) => (int)(uint)k;

            System.Diagnostics.Stopwatch watch;
            void Lap(string name) { if (watch != null) { Console.Error.WriteLine("ORACLE " + name + " " + watch.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms"); watch.Restart(); } }

            public OracleReport Execute()
            {
                watch = Environment.GetEnvironmentVariable("G3_ORACLE_TIMING") != null ? System.Diagnostics.Stopwatch.StartNew() : null;
                if (!CheckInput()) return r;
                Lap("input");
                if (o == null) { r.Add(Violation.StatusInconsistent, "no result"); return r; }
                if (o.Status == Mesh2Status.Failed || e.ExpectFailure) {
                    if (!e.ExpectFailure) r.Add(Violation.StatusInconsistent, "Failed on an input with a required result: " + o.FailureKind);
                    else if (o.Status != Mesh2Status.Failed) r.Add(Violation.StatusInconsistent, "a mesh instead of the required failure status");
                    if (o.Status == Mesh2Status.Failed && o.Triangles.Length != 0) r.Add(Violation.StatusInconsistent, "Failed with triangles");
                    return r;
                }
                if (!Vertices()) return r;
                Lap("vertices");
                domain = new Domain(input, !e.Reference);
                Lap("domain");
                bool degenerate = domain.Area.Sign == 0;
                tube = e.Tube ?? (e.Milestone == Milestone.M1 ? Q.Zero : Q.Max(Oracle.AutoTolerance(input), Q.FromDouble(input.SnapTolerance)));
                tube2 = tube * tube;
                // the mesher's tolerance (O11 rule) is half of this one without the subnormal floor: the mesher's own has none
                Q relativeTube = e.Tube ?? (e.Milestone == Milestone.M1 ? Q.Zero : Q.Max(Oracle.RelativeTolerance(input), Q.FromDouble(input.SnapTolerance)));
                o11Tol2 = relativeTube * relativeTube / 4;
                r.O11Tolerance2 = o11Tol2;
                if (!Indices()) return r;
                if (!Identity()) return r;
                Lap("indices, identity");
                if (o.Triangles.Length == 0) {
                    if (o.Status != Mesh2Status.Empty) r.Add(Violation.StatusInconsistent, o.Status + " with no triangles");
                    // as for the area of a mesh (Coverage): geometry may move by the tube (O11 merges points within it), so a domain whose
                    // twice area is at most 2 * tube * perimeter may vanish (found with the corpus run: earcut's infinite-loop-jhl, a
                    // sliver 1e-28 wide between an outer ring and a hole)
                    Q twice = domain.Area * 2, perimeter = domain.Perimeter2Bound * 2;
                    if (!degenerate && twice * twice > tube2 * perimeter * perimeter)
                        r.Add(Violation.EmptyOnArea, "no triangles, the domain has area " + domain.Area);
                    return r;
                }
                if (o.Status != Mesh2Status.Success) r.Add(Violation.StatusInconsistent, o.Status + " with triangles");
                if (degenerate) r.Add(Violation.TrianglesOnDegenerate, "triangles on an input without area");
                Orientation(); Lap("orientation");
                if (!r.Ok) return r; // the rest assumes positive triangles
                EdgesAndNeighbours(); Lap("edges");
                Sources(); Lap("sources");
                Filters(); Lap("filters");
                Conformity(); Lap("conformity: remaining");
                Crossings();
                InventedVertices(); Lap("crossings, invented");
                Segments(); Lap("segments");   // before Coverage: it measures how far the geometry moved
                Coverage(); Lap("coverage");
                Delaunay(); Lap("delaunay");
                Quality(); Lap("quality");
                Regions(); Lap("regions");
                Marks(); Lap("marks");
                Locate(); Lap("locate");
                return r;
            }

            bool CheckInput()
            {
                for (int i = 0; i < input.Count; ++i)
                    if (!Finite(input.X[i]) || !Finite(input.Y[i])) { r.Add(Violation.InputInvalid, "input point " + i + " is not finite"); return false; }
                foreach (var s in input.Segments)
                    if (s.A < 0 || s.A >= input.Count || s.B < 0 || s.B >= input.Count) { r.Add(Violation.InputInvalid, "segment endpoint out of range"); return false; }
                foreach (var ring in input.Rings)
                    foreach (int i in ring.Vertices)
                        if (i < 0 || i >= input.Count) { r.Add(Violation.InputInvalid, "ring vertex out of range"); return false; }
                foreach (var region in input.Regions)
                    if (region.Ring != null)
                        foreach (int i in region.Ring)
                            if (i < 0 || i >= input.Count) { r.Add(Violation.InputInvalid, "region vertex out of range"); return false; }
                inPts = new Pt[input.Count];
                for (int i = 0; i < inPts.Length; ++i) inPts[i] = Pt.FromDoubles(input.X[i], input.Y[i]);
                return true;
            }

            bool Vertices()
            {
                if (o.VX.Length != o.VY.Length) { r.Add(Violation.BadIndex, "vertex arrays differ in length"); return false; }
                for (int i = 0; i < o.VX.Length; ++i)
                    if (!Finite(o.VX[i]) || !Finite(o.VY[i])) { r.Add(Violation.NonFinite, "vertex " + i + " is not finite"); return false; }
                if (o.ExactVertices != null) {
                    if (o.ExactVertices.Length != o.VX.Length) { r.Add(Violation.BadIndex, "exact vertices differ in length"); return false; }
                    v = o.ExactVertices;
                    // the caller reads the doubles: they must be the exact vertices, rounded (one unit in the last place, or the subnormal step)
                    for (int i = 0; i < v.Length; ++i)
                        if (!NearlyRounded(o.VX[i], v[i].Qx) || !NearlyRounded(o.VY[i], v[i].Qy))
                            r.Add(Violation.DoublesNotRounded, "vertex " + i + ": the double (" + o.VX[i] + ", " + o.VY[i] + ") is not the exact " + v[i]);
                } else {
                    v = new Pt[o.VX.Length];
                    for (int i = 0; i < v.Length; ++i) v[i] = Pt.FromDoubles(o.VX[i], o.VY[i]);
                }
                return true;
            }

            static bool NearlyRounded(double d, Q exact)
            {
                Q difference = Q.Abs(Q.FromDouble(d) - exact);
                Q unit = Q.Max(Q.Abs(exact) * new Q(1, BigInteger.One << 52), new Q(1, BigInteger.One << 1074));
                return difference <= unit;
            }

            bool Indices()
            {
                if (o.Subsegments != null)
                    foreach (var s in o.Subsegments)
                        if (s.A < 0 || s.A >= v.Length || s.B < 0 || s.B >= v.Length) { r.Add(Violation.BadIndex, "subsegment vertex out of range"); return false; }
                foreach (var t in o.Triangles) {
                    if (t == null || t.Length != 3) { r.Add(Violation.BadIndex, "triangle without three vertices"); return false; }
                    foreach (int id in t)
                        if (id < 0 || id >= v.Length) { r.Add(Violation.BadIndex, "triangle vertex " + id + " out of range"); return false; }
                }
                return true;
            }

            bool Identity()
            {
                map = new int[input.Count];
                var at = new Dictionary<Pt, int>();
                // a vertex in use wins, if a foreign list repeats a coordinate
                foreach (var t in o.Triangles) foreach (int id in t) if (!at.ContainsKey(v[id])) at.Add(v[id], id);
                for (int i = 0; i < v.Length; ++i) if (!at.ContainsKey(v[i])) at.Add(v[i], i);
                if (o.IdentityRules) {
                    if (o.InputToVertex == null || o.InputToVertex.Length != input.Count) { r.Add(Violation.MappingWrong, "no mapping per input point"); return false; }
                    if (v.Length < input.Count) { r.Add(Violation.MappingWrong, "fewer vertices than input slots"); return false; }
                    var byCoordinate = new Dictionary<Pt, int>();
                    // D14 (owner, 2026-11-01): with an explicit tolerance the input is snap-rounded to a grid of pixel width tol anchored at
                    // the origin, so every input point lands on the centre of its pixel (input on the grid stays put); the restore step adds
                    // vertices but moves none
                    bool snapped = input.SnapTolerance > 0;
                    Q pixel = snapped ? Q.FromDouble(input.SnapTolerance) : Q.Zero;
                    for (int i = 0; i < input.Count; ++i) {
                        int m = o.InputToVertex[i];
                        if (m < 0 || m >= input.Count || o.InputToVertex[m] != m) { r.Add(Violation.MappingWrong, "input " + i + " maps to " + m + ", which is no surviving input slot"); return false; }
                        map[i] = m;
                        if (byCoordinate.TryGetValue(inPts[i], out int other)) {
                            if (o.InputToVertex[other] != m) r.Add(Violation.MappingWrong, "inputs " + other + " and " + i + " coincide but map differently");
                        } else byCoordinate.Add(inPts[i], i);
                        if (m == i && !e.MayMoveVertices && !snapped && !v[i].Equals(inPts[i])) r.Add(Violation.InputMoved, "input " + i + " moved to " + v[i]);
                        Q off = Exact.Len2(inPts[i], v[m]);
                        moved2 = Q.Max(moved2, off);
                        if (off > e_Tube2()) r.Add(Violation.SnapTooFar, "input " + i + " merged too far, into " + m);
                        else if (snapped && !AtPixelCentre(inPts[i], v[m], pixel))
                            r.Add(Violation.SnapTooFar, "input " + i + " is at " + v[m] + ", not at the centre of its pixel (D14)");
                    }
                    r.ConstructedVertices = v.Length - input.Count;
                    // the owner's decision of 2026-11-01: with an explicit tolerance every output vertex is on the grid, refinement's included
                    if (snapped)
                        for (int i = input.Count; i < v.Length; ++i)
                            if (!AtPixelCentre(v[i], v[i], pixel)) r.Add(Violation.OffGrid, "vertex " + i + " " + v[i] + " is not on the grid of the tolerance");
                    var isAlias = new bool[input.Count];
                    for (int i = 0; i < input.Count; ++i) isAlias[i] = map[i] != i;
                    aliases = isAlias.Count(x => x);
                    foreach (var t in o.Triangles) foreach (int id in t)
                        if (id < input.Count && isAlias[id]) r.Add(Violation.AliasUsed, "merged input slot " + id + " used by a triangle");
                } else {
                    // a foreign producer: find each input point by its coordinates
                    for (int i = 0; i < input.Count; ++i) {
                        if (!at.TryGetValue(inPts[i], out int m)) {
                            m = -1;
                            for (int k = 0; k < v.Length && m < 0; ++k) if (Exact.Len2(inPts[i], v[k]) <= e_Tube2()) m = k;
                        }
                        if (m >= 0) moved2 = Q.Max(moved2, Exact.Len2(inPts[i], v[m]));
                        map[i] = m; // -1: not among the vertices, judged below (outside the domain, or lost)
                    }
                    var inputs = new HashSet<Pt>(inPts);
                    r.ConstructedVertices = v.Count(p => !inputs.Contains(p));
                }
                if (e.Milestone == Milestone.M1 && r.ConstructedVertices != 0)
                    r.Add(Violation.SteinerNotAllowed, r.ConstructedVertices + " vertices that are not input points");
                return true;
            }

            Q e_Tube2() => tube2;

            /// <summary>
            /// D14: the centre of the pixel containing p, pixels half-open (closed on the left and bottom, Hobby 1999) on a grid of the given
            /// width anchored at the origin; w must equal it up to the rounding of the centre to doubles (2^-52 of its magnitude per axis)
            /// </summary>
            static bool AtPixelCentre(Pt p, Pt w, Q pixel)
            {
                Q Centre(Q x) { Q t = x / pixel + new Q(1, 2); var f = BigInteger.Divide(t.N, t.D); if (t.N.Sign < 0 && f * t.D != t.N) f -= 1; return new Q(f, 1) * pixel; }
                Q cx = Centre(p.Qx), cy = Centre(p.Qy);
                Q slack = Q.Max(pixel, Q.Max(Q.Abs(cx), Q.Abs(cy))) * new Q(BigInteger.One, BigInteger.One << 52);
                return Q.Abs(w.Qx - cx) <= slack && Q.Abs(w.Qy - cy) <= slack;
            }

            /// <summary>
            /// Refinement on the grid (owner decision 2026-11-01, MESHING2D_SNAP.md section 3g): with an explicit tolerance no vertex can be
            /// placed closer than the grid allows, so a triangle whose shortest edge is at most two pixels is exempt from the angle and size
            /// bounds (the measured floor of section 3g).
            /// </summary>
            bool BelowGridFloor(int[] t)
            {
                if (!(input.SnapTolerance > 0)) return false;
                // in pixels, from the pixel indices (all vertices are pixel centres, OffGrid; doubles of k w round): exact and as the mesher
                Q pixel = Q.FromDouble(input.SnapTolerance);
                Q Index(Q x) { Q q = x / pixel + new Q(1, 2); var f = BigInteger.Divide(q.N, q.D); if (q.N.Sign < 0 && f * q.D != q.N) f -= 1; return new Q(f, 1); }
                for (int k = 0; k < 3; ++k) {
                    Pt a = v[t[k]], b = v[t[(k + 1) % 3]];
                    Q dx = Index(a.Qx) - Index(b.Qx), dy = Index(a.Qy) - Index(b.Qy);
                    if (dx * dx + dy * dy <= new Q(4, 1)) return true;
                }
                return false;
            }

            /// <summary>
            /// Refinement on the grid: the point that would improve t is the centre of its circumcentre's pixel; it cannot be placed if a
            /// vertex is there already or a constraint crosses that pixel (a vertex within half a pixel of a constraint breaks D14). The
            /// circumcentre is computed exactly.
            /// </summary>
            bool GridBlocked(int[] t)
            {
                if (!(input.SnapTolerance > 0)) return false;
                Pt a = v[t[0]], b = v[t[1]], c = v[t[2]];
                Q bx = b.Qx - a.Qx, by = b.Qy - a.Qy, cx = c.Qx - a.Qx, cy = c.Qy - a.Qy, d = (bx * cy - by * cx) * 2;
                if (d.Sign == 0) return false;
                Q b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
                var centre = new Pt(a.Qx + (cy * b2 - by * c2) / d, a.Qy + (bx * c2 - cx * b2) / d);
                Q pixel = Q.FromDouble(input.SnapTolerance), half = pixel / 2;
                Q Index(Q x) { Q q = x / pixel + new Q(1, 2); var f = BigInteger.Divide(q.N, q.D); if (q.N.Sign < 0 && f * q.D != q.N) f -= 1; return new Q(f, 1); }
                Q kx = Index(centre.Qx), ky = Index(centre.Qy);
                // (the vertices by their exact pixel index, computed once: every vertex's index was recomputed in rationals for every
                // triangle, a third of the angle check on the Herne tiles. The question is whether any vertex of that pixel is at its
                // centre, so the order does not matter; the reference path scans)
                bool ScanVertices() { for (int i = 0; i < v.Length; ++i) if (Index(v[i].Qx) == kx && Index(v[i].Qy) == ky && AtPixelCentre(v[i], v[i], pixel)) return true; return false; }
                if (e.Reference) {
                    if (ScanVertices()) return true;
                } else {
                    if (verticesByPixel == null) {
                        verticesByPixel = new Dictionary<(BigInteger, BigInteger), List<int>>();
                        for (int i = 0; i < v.Length; ++i) {
                            var key = (Index(v[i].Qx).N, Index(v[i].Qy).N);
                            if (!verticesByPixel.TryGetValue(key, out var list)) verticesByPixel.Add(key, list = new List<int>());
                            list.Add(i);
                        }
                    }
                    bool found = verticesByPixel.TryGetValue((kx.N, ky.N), out var inPixel) && inPixel.Any(i => AtPixelCentre(v[i], v[i], pixel));
                    if (CheckFastPathForTests && found != ScanVertices()) FastPathDiffers("a vertex at the centre of the circumcentre's pixel");
                    if (found) return true;
                }
                Q x0 = kx * pixel - half, x1 = kx * pixel + half, y0 = ky * pixel - half, y1 = ky * pixel + half;
                // (the subsegments whose box in doubles meets the pixel's, from a grid: a subsegment whose box misses it fails the exact
                // box test below, which is kept; the reference path scans all)
                IEnumerable<SubsegmentOutput> subsegments = o.Subsegments;
                if (!e.Reference && x0.Bounds(out double bx0, out _) && x1.Bounds(out _, out double bx1) && y0.Bounds(out double by0, out _) && y1.Bounds(out _, out double by1)) {
                    if (subsegmentGrid == null) {
                        subsegmentBox = new double[4 * o.Subsegments.Count];
                        for (int k = 0; k < o.Subsegments.Count; ++k) SegmentBox(v[o.Subsegments[k].A], v[o.Subsegments[k].B], 0, subsegmentBox, 4 * k);
                        subsegmentGrid = new BoxGrid(subsegmentBox, o.Subsegments.Count);
                    }
                    var inBox = subsegmentGrid.Query(bx0, bx1, by0, by1);
                    if (CheckFastPathForTests) {
                        // the grid's superset of the boxes, and of the subsegments the exact box test keeps (a set, not only the answer)
                        BoxGrid.CheckSuperset(subsegmentBox, o.Subsegments.Count, bx0, bx1, by0, by1, inBox, "the subsegments at a pixel");
                        var got = new HashSet<int>(inBox);
                        for (int k = 0; k < o.Subsegments.Count; ++k) {
                            Pt p = v[o.Subsegments[k].A], q = v[o.Subsegments[k].B];
                            bool kept = !(Q.Max(p.Qx, q.Qx) <= x0 || Q.Min(p.Qx, q.Qx) >= x1 || Q.Max(p.Qy, q.Qy) <= y0 || Q.Min(p.Qy, q.Qy) >= y1);
                            if (kept && !got.Contains(k)) FastPathDiffers("subsegment " + k + " meets the circumcentre's pixel and is missing from the grid's answer");
                        }
                    }
                    subsegments = inBox.Select(k => o.Subsegments[k]);
                }
                bool Crosses(SubsegmentOutput s)
                {
                    Pt p = v[s.A], q = v[s.B];
                    if (Q.Max(p.Qx, q.Qx) <= x0 || Q.Min(p.Qx, q.Qx) >= x1 || Q.Max(p.Qy, q.Qy) <= y0 || Q.Min(p.Qy, q.Qy) >= y1) return false;
                    int pos = 0, neg = 0;
                    foreach (var corner in new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) }) {
                        int or = Exact.Orient(p, q, corner);
                        if (or > 0) ++pos; else if (or < 0) ++neg;
                    }
                    return pos > 0 && neg > 0;
                }
                bool crossed = subsegments.Any(Crosses);
                if (CheckFastPathForTests && !ReferenceEquals(subsegments, o.Subsegments) && crossed != o.Subsegments.Any(Crosses)) FastPathDiffers("a subsegment across the circumcentre's pixel");
                return crossed;
            }

            Dictionary<(BigInteger, BigInteger), List<int>> verticesByPixel;
            BoxGrid subsegmentGrid;
            double[] subsegmentBox;

            Dictionary<long, bool> unsplittableMemo;
            BoxGrid unsplittableSubGrid, unsplittableVertexGrid;
            double[] unsplittableSubBox;

            /// <summary>
            /// a-b is a subsegment that has no split point on the grid (refinement on the grid, MESHING2D_SNAP.md 3g): only that edge is
            /// excused from the local Delaunay test, never an unconstrained edge of a triangle next to it (review of #277)
            /// </summary>
            bool GridUnsplittableSubsegment(int a, int b)
            {
                if (!(input.SnapTolerance > 0) || e.Milestone != Milestone.M3) return false;
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!constraintEdges.Contains(key)) return false;
                unsplittableMemo = unsplittableMemo ?? new Dictionary<long, bool>();
                if (!unsplittableMemo.TryGetValue(key, out bool u)) unsplittableMemo[key] = u = GridUnsplittable(a, b);
                return u;
            }

            /// <summary>
            /// Refinement on the grid: t's rounded circumcentre lies behind a subsegment that cannot be split on the grid (the segment from
            /// t's centroid to the centre of the circumcentre's pixel crosses it). Ruppert's rule splits the subsegment instead of inserting
            /// across it; when no split point exists, t stays (found by the grid fuzz test: a circumcentre outside the domain).
            /// </summary>
            bool CircumcentreBehindGridUnsplittable(int[] t)
            {
                if (!(input.SnapTolerance > 0) || e.Milestone != Milestone.M3 || o.Subsegments == null) return false;
                Pt a = v[t[0]], b = v[t[1]], c = v[t[2]];
                Q bx = b.Qx - a.Qx, by = b.Qy - a.Qy, cx = c.Qx - a.Qx, cy = c.Qy - a.Qy, d = (bx * cy - by * cx) * 2;
                if (d.Sign == 0) return false;
                Q b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
                Q pixel = Q.FromDouble(input.SnapTolerance);
                Q Centre(Q x) { Q q = x / pixel + new Q(1, 2); var f = BigInteger.Divide(q.N, q.D); if (q.N.Sign < 0 && f * q.D != q.N) f -= 1; return new Q(f, 1) * pixel; }
                var p = new Pt(Centre(a.Qx + (cy * b2 - by * c2) / d), Centre(a.Qy + (bx * c2 - cx * b2) / d));
                return Behind(t, p, -1);
            }

            /// <summary> the segment from t's centroid to p crosses a subsegment (not one ending at vertex skip) that cannot be split on the grid </summary>
            bool Behind(int[] t, Pt p, int skip) => Behind(t, p, skip, GridUnsplittableMemo);

            bool GridUnsplittableMemo(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                unsplittableMemo = unsplittableMemo ?? new Dictionary<long, bool>();
                if (!unsplittableMemo.TryGetValue(key, out bool u)) unsplittableMemo[key] = u = GridUnsplittable(a, b);
                return u;
            }

            /// <summary> the segment from t's centroid to p crosses a subsegment (not one ending at vertex skip) for which unsplittable holds </summary>
            bool Behind(int[] t, Pt p, int skip, Func<int, int, bool> unsplittable)
            {
                Pt a = v[t[0]], b = v[t[1]], c = v[t[2]];
                var g = new Pt((a.Qx + b.Qx + c.Qx) / 3, (a.Qy + b.Qy + c.Qy) / 3);
                bool Crossed(SubsegmentOutput s)
                {
                    if (s.A == skip || s.B == skip) return false;
                    Pt sa = v[s.A], sb = v[s.B];
                    int o1 = Exact.Orient(sa, sb, g), o2 = Exact.Orient(sa, sb, p);
                    return o1 * o2 < 0 && Exact.Orient(g, p, sa) * Exact.Orient(g, p, sb) <= 0;
                }
                // p as an interval of doubles: exact for a vertex on the fast path, Pt.Bounds (containing the exact value) for a
                // constructed point such as the circumcentre of the angle-bound exemptions (before, those scanned every subsegment
                // exactly, about 7 triangles per second on a 400k-triangle output with 200k bad triangles)
                double pxl = 0, pxh = 0, pyl = 0, pyh = 0;
                bool pKnown = false;
                if (Fast && skip >= 0) { pxl = pxh = o.VX[skip]; pyl = pyh = o.VY[skip]; pKnown = true; }
                else if (Fast) pKnown = p.Bounds(false, out pxl, out pxh) && p.Bounds(true, out pyl, out pyh);
                if (pKnown) {
                    // the segment from the centroid to p lies in the box of t's vertices and p (the vertices exact in doubles on the fast
                    // path, p inside its interval), and a subsegment that crosses it has a box that meets that box (the grid returns a
                    // superset of those, in ascending order). The answer is a boolean: the order of the visits does not change it
                    EnsureUnsplittableGrids();
                    double xl = Math.Min(Math.Min(o.VX[t[0]], o.VX[t[1]]), Math.Min(o.VX[t[2]], pxl));
                    double xh = Math.Max(Math.Max(o.VX[t[0]], o.VX[t[1]]), Math.Max(o.VX[t[2]], pxh));
                    double yl = Math.Min(Math.Min(o.VY[t[0]], o.VY[t[1]]), Math.Min(o.VY[t[2]], pyl));
                    double yh = Math.Max(Math.Max(o.VY[t[0]], o.VY[t[1]]), Math.Max(o.VY[t[2]], pyh));
                    if (CheckFastPathForTests) {
                        // the premise of the query: its box holds t's vertices and p exactly (an independent check of the box above)
                        // (an infinite side, from a pad past the largest double, holds everything on that side)
                        foreach (var q in new[] { a, b, c, p })
                            if (!double.IsInfinity(xl) && q.Qx < Q.FromDouble(xl) || !double.IsInfinity(xh) && q.Qx > Q.FromDouble(xh)
                                || !double.IsInfinity(yl) && q.Qy < Q.FromDouble(yl) || !double.IsInfinity(yh) && q.Qy > Q.FromDouble(yh))
                                FastPathDiffers("the query box of a triangle and a point misses one of them");
                    }
                    var near = unsplittableSubGrid.Query(xl, xh, yl, yh);
                    if (CheckFastPathForTests) BoxGrid.CheckSuperset(unsplittableSubBox, o.Subsegments.Count, xl, xh, yl, yh, near, "the subsegments near a triangle and a point");
                    bool found = false;
                    foreach (int i in near) if (Crossed(o.Subsegments[i]) && unsplittable(o.Subsegments[i].A, o.Subsegments[i].B)) { found = true; break; }
                    if (CheckFastPathForTests && found != o.Subsegments.Any(s => Crossed(s) && unsplittable(s.A, s.B)))
                        FastPathDiffers("a point hidden from a triangle by an unsplittable subsegment");
                    return found;
                }
                foreach (var s in o.Subsegments)
                    if (Crossed(s) && unsplittable(s.A, s.B)) return true;
                return false;
            }

            /// <summary>
            /// Vertex id is hidden from t by a subsegment that cannot be split on the grid: the segment from t's centroid to id crosses it.
            /// Conforming Delaunay cannot be reached across such a piece, and constrained Delaunay is what remains; the local test still
            /// demands it of every unconstrained edge (found by the grid fuzz test: a vertex behind an unsplittable piece next to t).
            /// </summary>
            bool HiddenByGridUnsplittable(int[] t, int id)
                => input.SnapTolerance > 0 && e.Milestone == Milestone.M3 && o.Subsegments != null && Behind(t, v[id], id);

            Dictionary<long, bool> o11Memo;

            /// <summary>
            /// Subsegment a-b is an O11-unsplittable piece (without an explicit tolerance, at M3): every split point the mesher may choose
            /// for it lies within its tolerance of an input constraint that is not the piece's own, so the mesher refuses it
            /// (`NearOtherSource`: O11 merges a point within the tolerance of a constraint into that constraint). Conforming Delaunay (O2) and
            /// the angle bound cannot be reached across such a piece, as across a piece with no split point on the grid; the three
            /// exemptions below mirror the grid's, with this one predicate (found with the corpus at 25 degrees, earcut's water-huge: a
            /// ring corner of 0.84 degrees, a leg's first piece of 2^-22 whose midpoint lies 1.75e-9 from the other leg, the mesher's
            /// tolerance 1.98e-9). See <see cref="ComputeO11Unsplittable"/>; on the grid ValidGridSplit decides.
            /// </summary>
            bool O11Unsplittable(int a, int b)
            {
                if (input.SnapTolerance > 0 || e.Milestone != Milestone.M3 || tube2.Sign == 0) return false;
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                o11Memo = o11Memo ?? new Dictionary<long, bool>();
                if (!o11Memo.TryGetValue(key, out bool u)) o11Memo[key] = u = ComputeO11Unsplittable(a, b);
                return u;
            }

            /// <summary>
            /// The piece predicate, decided exactly:
            /// - its own constraints: the input constraints that hold both ends within the mesher's tolerance (a hair more): the mesher makes
            ///   such vertices vertices of the constraint and merges the sources of the shared piece; this also covers a split point that
            ///   rounding put off its own constraint, by far less than the tolerance (review of the first version); at least one (a piece of
            ///   a hull edge has none here and is judged);
            /// - its candidate split points: the midpoint, and the concentric-shell point from either end (QualityMesh2.SplitSegment: next
            ///   to a vertex that existed before refinement, the power of two from it nearest the middle, if within the middle third). Which
            ///   one the mesher took depends on which ends existed before refinement (input points and crossings), so all must qualify;
            /// - another input constraint (not a hull edge: the mesher's NearOtherSource knows input constraints only) within the mesher's
            ///   tolerance (half the tube: 2^-41 against the tube's 2^-40 of the magnitude; less 2^-7 of it, as the mesher decides in doubles)
            ///   of every candidate. With a tube given to the oracle (--tube) the tolerance follows it;
            /// - which meets each own constraint in at most one point (its ends and the own constraint's ends within the tube of the other
            ///   are one point): nearly coincident lines, eg a double-digitised border, are merged by the mesher into pieces of both, and
            ///   such a piece is split (review of the first version).
            /// </summary>
            bool ComputeO11Unsplittable(int ia, int ib)
            {
                Pt pa = v[ia], pb = v[ib];
                // the mesher's tolerance is half the tube (2^-41 against 2^-40 of the same magnitude); a constraint holding both ends within it
                // (a hair more, to be safe) is one of the piece's own: the mesher makes such vertices vertices of its chain and merges the
                // sources of the shared piece (InsertPiece, Near; second review: two constraints crossing at a tiny angle). A split point
                // counts as refused only a hair inside the tolerance, as the mesher decides in rounded doubles (second review)
                Q tol2 = o11Tol2, ownTol2 = tol2 * new Q(BigInteger.One + (BigInteger.One << 7), BigInteger.One << 7), half2 = tol2 * new Q((BigInteger.One << 7) - BigInteger.One, BigInteger.One << 7);
                var own = new List<(Pt a, Pt b)>();
                var ownIds = new HashSet<int>();
                for (int k = 0; k < sources.Count; ++k) {
                    if (!MayBeNear(k, ia) || !MayBeNear(k, ib)) continue;
                    var c = sources[k];
                    Q da = Exact.Dist2ToSegment(c.a, c.b, pa), db = Exact.Dist2ToSegment(c.a, c.b, pb);
                    if (da <= ownTol2 && db <= ownTol2) { own.Add((c.a, c.b)); ownIds.Add(k); }
                }
                if (own.Count == 0) return false;
                var candidates = new List<Pt> { new Pt((pa.Qx + pb.Qx) / 2, (pa.Qy + pb.Qy) / 2) };
                double length = Math.Sqrt(ToD(Exact.Len2(pa, pb)));
                long bits = BitConverter.DoubleToInt64Bits(length / 2);
                int field = (int)((bits >> 52) & 0x7FF);
                if (field == 0 || field == 0x7FF) return false;   // (no shell point computable: not excused)
                {
                    // the power of two at or below half the length, from the bits, and the nearer of it and its double (as the mesher's)
                    double lower = BitConverter.Int64BitsToDouble((long)field << 52), d = length / 2 - lower < 2 * lower - length / 2 ? lower : 2 * lower;
                    if (d > length / 3 && d < 2 * length / 3) {
                        Q f = Q.FromDouble(d / length);
                        candidates.Add(new Pt(pa.Qx + (pb.Qx - pa.Qx) * f, pa.Qy + (pb.Qy - pa.Qy) * f));
                        candidates.Add(new Pt(pb.Qx + (pa.Qx - pb.Qx) * f, pb.Qy + (pa.Qy - pb.Qy) * f));
                    }
                }
                for (int k = 0; k < sources.Count; ++k) {
                    // (the filter on the ends: a constraint near only the middle of the piece and farther than the tube from both its ends
                    // meets an own constraint near both of its own ends, so the coincidence test below rejects it anyway)
                    if (ownIds.Contains(k) || (!MayBeNear(k, ia) && !MayBeNear(k, ib))) continue;
                    var c = sources[k];
                    if (!candidates.All(p => Exact.Dist2ToSegment(c.a, c.b, p) <= half2)) continue;
                    if (own.Any(w => MeetInMoreThanOnePoint(w.a, w.b, c.a, c.b))) continue;
                    return true;
                }
                return false;
            }

            /// <summary> the ends of each segment that lie within the tube of the other are more than one point (nearly coincident lines).
            /// (With the tube, floor included: below about 2^-1034 the floored tube counts more lines as nearly coincident than the mesher's
            /// tolerance would, so fewer pieces are excused, never more: strict, and harmless.) </summary>
            bool MeetInMoreThanOnePoint(Pt a, Pt b, Pt c, Pt d)
            {
                var near = new List<Pt>();
                if (Exact.Dist2ToSegment(c, d, a) <= tube2) near.Add(a);
                if (Exact.Dist2ToSegment(c, d, b) <= tube2) near.Add(b);
                if (Exact.Dist2ToSegment(a, b, c) <= tube2) near.Add(c);
                if (Exact.Dist2ToSegment(a, b, d) <= tube2) near.Add(d);
                for (int i = 0; i < near.Count; ++i)
                    for (int j = i + 1; j < near.Count; ++j)
                        if (Exact.Len2(near[i], near[j]) > tube2) return true;
                return false;
            }

            /// <summary> the local test: a-b is an O11-unsplittable piece; only that edge, never an unconstrained edge next to it </summary>
            bool O11UnsplittableSubsegment(int a, int b)
            {
                if (!O11Unsplittable(a, b)) return false;
                r.O11LocalExcused++;
                return true;
            }

            int o11Pending;

            /// <summary> the global test: vertex id is hidden from t by an O11-unsplittable piece (the segment from t's centroid to id crosses it) </summary>
            bool HiddenByO11Unsplittable(int[] t, int id)
            {
                if (o.Subsegments == null || !Behind(t, v[id], id, O11Unsplittable)) return false;
                ++o11Pending;   // (counted when t ends with no violation: both paths then count the same, whatever order they visit vertices in)
                return true;
            }

            /// <summary>
            /// The angle bound: t's circumcentre lies behind an O11-unsplittable piece (the segment from t's centroid to it crosses the
            /// piece). Ruppert's rule splits the piece instead of inserting across it; no split point may be placed, so t stays
            /// </summary>
            bool CircumcentreBehindO11Unsplittable(int[] t)
            {
                // (without an explicit tolerance, at M3: the piece predicate decides that, and the exact circumcentre is computed only then)
                if (input.SnapTolerance > 0 || o.Subsegments == null) return false;
                Pt a = v[t[0]], b = v[t[1]], c = v[t[2]];
                Q bx = b.Qx - a.Qx, by = b.Qy - a.Qy, cx = c.Qx - a.Qx, cy = c.Qy - a.Qy, d = (bx * cy - by * cx) * 2;
                if (d.Sign == 0) return false;
                Q b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
                var p = new Pt(a.Qx + (cy * b2 - by * c2) / d, a.Qy + (bx * c2 - cx * b2) / d);
                if (!Behind(t, p, -1, O11Unsplittable)) return false;
                r.O11AngleExcused++;
                return true;
            }




            /// <summary> set to judge the pinch rule by the envelope approximation that OwnStretch replaced (differential tests only) </summary>
            [ThreadStatic] public static bool LegacyEnvelopeChainForDifferential;

            static long SubKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            Dictionary<int, List<int>> subsegmentNeighbours;
            readonly Dictionary<long, HashSet<long>> ownStretchMemo = new Dictionary<long, HashSet<long>>();

            /// <summary>
            /// The piece's own chain, recomputed from the output's subsegment graph and the input alone: the piece with the subsegments
            /// reached from either end through vertices that are no hot pixel of the input's rounding and have exactly two neighbouring
            /// subsegment vertices. That is the link of the snap-rounded image between two hot pixels (or two branching vertices) that
            /// the piece lies on, ie the part of one chain the mesher's "shares an input source" holds for, without the envelope
            /// approximation: two chains that run within a pixel of each other along the piece are two chains here (rule C makes that
            /// regime reachable: the zigzag of Q17). It relies on monotonicity: a straight input segment crosses each open pixel in one interval and
            /// candidates project strictly between the piece's ends, so a vertex of the piece's own source in q's pixel can only be an end of the
            /// piece (skipped as q); a stretch beyond a hot pixel is another input segment's chain. A pinch vertex (four subsegments) ends a stretch, so what lies beyond it counts as
            /// another chain's: the pinch is a failing violation of its own. Returned as subsegment keys (SubKey)
            /// </summary>
            HashSet<long> OwnStretch(int ia, int ib)
            {
                long key = SubKey(ia, ib);
                if (ownStretchMemo.TryGetValue(key, out var memo)) return memo;
                if (subsegmentNeighbours == null) {
                    subsegmentNeighbours = new Dictionary<int, List<int>>();
                    foreach (var g in o.Subsegments ?? new List<SubsegmentOutput>()) {
                        if (g.A == g.B) continue;
                        if (!subsegmentNeighbours.TryGetValue(g.A, out var la)) subsegmentNeighbours[g.A] = la = new List<int>();
                        if (!la.Contains(g.B)) la.Add(g.B);
                        if (!subsegmentNeighbours.TryGetValue(g.B, out var lb)) subsegmentNeighbours[g.B] = lb = new List<int>();
                        if (!lb.Contains(g.A)) lb.Add(g.A);
                    }
                }
                var stretch = StretchOf(ia, ib, subsegmentNeighbours, x => snapHot.Contains(PixelOf(v[x], snapPixel)));
                return ownStretchMemo[key] = stretch;
            }


            /// <summary> OwnStretch on a given graph (vertex to its neighbouring subsegment vertices) and hot-pixel test, from either end of the piece </summary>
            internal static HashSet<long> StretchOf(int ia, int ib, Dictionary<int, List<int>> neighbours, Func<int, bool> isHot)
            {
                var stretch = new HashSet<long> { SubKey(ia, ib) };
                foreach (var (start, from) in new[] { (ia, ib), (ib, ia) }) {
                    int prev = from, x = start;
                    var seen = new HashSet<int> { ia, ib };
                    while (!isHot(x) && neighbours.TryGetValue(x, out var nb) && nb.Count == 2) {
                        int next = nb[0] == prev ? nb[1] : nb[0];
                        if (nb[0] != prev && nb[1] != prev) break;   // (a piece that is no subsegment of the output: only the self-checks)
                        stretch.Add(SubKey(x, next));
                        if (!seen.Add(next)) break;
                        prev = x; x = next;
                    }
                }
                return stretch;
            }

            /// <summary> the grids over the subsegments' boxes and the vertices, in doubles (built once) </summary>
            void EnsureUnsplittableGrids()
            {
                if (unsplittableSubGrid != null) return;
                var all = o.Subsegments ?? new List<SubsegmentOutput>();
                var sb = new double[4 * all.Count];
                for (int i = 0; i < all.Count; ++i) {
                    var g = all[i];
                    sb[4 * i] = Math.Min(o.VX[g.A], o.VX[g.B]); sb[4 * i + 1] = Math.Max(o.VX[g.A], o.VX[g.B]);
                    sb[4 * i + 2] = Math.Min(o.VY[g.A], o.VY[g.B]); sb[4 * i + 3] = Math.Max(o.VY[g.A], o.VY[g.B]);
                }
                unsplittableSubBox = sb;
                unsplittableSubGrid = new BoxGrid(sb, all.Count);
                var vb = new double[4 * v.Length];
                for (int i = 0; i < v.Length; ++i) { vb[4 * i] = vb[4 * i + 1] = o.VX[i]; vb[4 * i + 2] = vb[4 * i + 3] = o.VY[i]; }
                unsplittableVertexGrid = new BoxGrid(vb, v.Length);
            }

            /// <summary>
            /// Refinement on the grid (MESHING2D_SNAP.md section 3g), recomputed from the specification: subsegment a-b can be split on the
            /// grid if some pixel centre q, other than its ends, lies in the envelope of every input constraint the subsegment belongs to
            /// (the constraint crosses q's open pixel or passes through q), projects strictly between a and b, no other constraint crosses
            /// q's pixel (one ending at q may), and the new edges a-q and q-b cross no vertex's open pixel and no other constraint. If none
            /// can, conforming Delaunay is not demanded at it.
            /// </summary>
            bool GridUnsplittable(int ia, int ib)
            {
                Q pixel = Q.FromDouble(input.SnapTolerance), half = pixel / 2;
                Pt pa = v[ia], pb = v[ib];
                SnapHot();
                // its input constraints: those whose envelope holds both ends (a hull domain's hull edges too: on the grid the mesher
                // snap-rounds them with the segments, option C, and splits their pieces as it splits a segment's; Q17)
                var own = sources.Select(s => (s.a, s.b)).Concat(domain.IsHull ? domain.Edges() : Enumerable.Empty<(Pt a, Pt b)>())
                    .Where(s => InEnvelopeOf(s.a, s.b, pa, half) && InEnvelopeOf(s.a, s.b, pb, half)).ToList();
                if (own.Count == 0) return false;
                Q Index(Q x) { Q q = x / pixel + new Q(1, 2); var f = BigInteger.Divide(q.N, q.D); if (q.N.Sign < 0 && f * q.D != q.N) f -= 1; return new Q(f, 1); }
                Q kx0 = Index(Q.Min(pa.Qx, pb.Qx)) - 1, kx1 = Index(Q.Max(pa.Qx, pb.Qx)) + 1, ky0 = Index(Q.Min(pa.Qy, pb.Qy)) - 1, ky1 = Index(Q.Max(pa.Qy, pb.Qy)) + 1;
                if ((kx1 - kx0) * (ky1 - ky0) > new Q(1 << 16, 1)) return false;   // (too long to enumerate: judged as splittable)
                // only what lies near the piece can block a split point: a constraint crossing its pixel or the new edges, a vertex whose
                // pixel they cross (boxes three pixels wider in doubles, generous to the hair of a decimal pixel; every test below stays
                // exact): the hull pieces of a domain judged here too made the whole lists per candidate too slow on the Herne tiles
                double w3 = 3 * input.SnapTolerance;
                double bx0 = Math.Min(o.VX[ia], o.VX[ib]) - w3, bx1 = Math.Max(o.VX[ia], o.VX[ib]) + w3, by0 = Math.Min(o.VY[ia], o.VY[ib]) - w3, by1 = Math.Max(o.VY[ia], o.VY[ib]) + w3;
                EnsureUnsplittableGrids();
                var subs = unsplittableSubGrid.Query(bx0, bx1, by0, by1).Select(i => o.Subsegments[i]).Where(g => !((g.A == ia && g.B == ib) || (g.A == ib && g.B == ia))).ToList();
                var nearVertices = unsplittableVertexGrid.Query(bx0, bx1, by0, by1);
                nearVertices.Sort();
                // A decimal pixel width is not exact in doubles: a vertex's doubles are k w rounded, so vertices are taken at the centre of
                // their pixel and matched by it, and a pixel corner that an edge touches on the exact lattice may be crossed or missed by a
                // hair in the mesher's doubles: such ties are decided generously, with blocking pixels a hair larger and the own envelope a
                // hair smaller (found by the grid fuzz test, w = 0.3 and 1/3)
                // (the hair grows with the coordinates: a pixel centre k w rounds by up to |k w| 2^-53; review of #277)
                Q magnitude = Q.Max(Q.Max(Q.Abs(pa.Qx), Q.Abs(pa.Qy)), Q.Max(Q.Abs(pb.Qx), Q.Abs(pb.Qy)));
                Q hair = Q.Max(half * new Q(BigInteger.One, BigInteger.One << 40), magnitude * new Q(BigInteger.One, BigInteger.One << 48)), wide = half + hair, narrow = half - hair;
                Q Snap(Q x) => Index(x) * pixel;
                bool Same(Pt a, Pt b) => Index(a.Qx) == Index(b.Qx) && Index(a.Qy) == Index(b.Qy);
                bool CrossesOpen(Pt a, Pt b, Pt c, Q h)
                {
                    Q x0 = c.Qx - h, x1 = c.Qx + h, y0 = c.Qy - h, y1 = c.Qy + h;
                    if (Q.Max(a.Qx, b.Qx) <= x0 || Q.Min(a.Qx, b.Qx) >= x1 || Q.Max(a.Qy, b.Qy) <= y0 || Q.Min(a.Qy, b.Qy) >= y1) return false;
                    int pos = 0, neg = 0;
                    foreach (var corner in new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) }) {
                        int or = Exact.Orient(a, b, corner);
                        if (or > 0) ++pos; else if (or < 0) ++neg;
                    }
                    return pos > 0 && neg > 0;
                }
                bool OnSegment(Pt a, Pt b, Pt c) => Exact.Orient(a, b, c) == 0 && c.Qx >= Q.Min(a.Qx, b.Qx) && c.Qx <= Q.Max(a.Qx, b.Qx) && c.Qy >= Q.Min(a.Qy, b.Qy) && c.Qy <= Q.Max(a.Qy, b.Qy);
                var s0 = own[0];
                Q dx = s0.b.Qx - s0.a.Qx, dy = s0.b.Qy - s0.a.Qy;
                Q Along(Pt p) => (p.Qx - s0.a.Qx) * dx + (p.Qy - s0.a.Qy) * dy;
                Q ta = Along(pa), tb = Along(pb), lo = Q.Min(ta, tb), hi = Q.Max(ta, tb);
                // a cheap filter first: a split point must lie in the envelope of every own constraint, so within a pixel's half diagonal
                // (and a margin) of the first one's line; only those few of the box's pixels get the exact tests (a diagonal hull piece
                // of the Herne tiles has a box of tens of thousands of pixels and an envelope of a few hundred)
                s0.a.Bounds(false, out double fax, out _); s0.a.Bounds(true, out double fay, out _);
                s0.b.Bounds(false, out double fbx, out _); s0.b.Bounds(true, out double fby, out _);
                double fdx = fbx - fax, fdy = fby - fay, flen = Math.Sqrt(fdx * fdx + fdy * fdy), fw = input.SnapTolerance;
                for (Q kx = kx0; kx <= kx1; kx = kx + 1)
                    for (Q ky = ky0; ky <= ky1; ky = ky + 1) {
                        if (flen > 0) {
                            kx.Bounds(out double ix, out _); ky.Bounds(out double iy, out _);
                            double cx = ix * fw, cy = iy * fw;
                            if (Math.Abs((cx - fax) * fdy - (cy - fay) * fdx) / flen > fw) continue;
                        }
                        var q = new Pt(kx * pixel, ky * pixel);
                        if (Same(q, pa) || Same(q, pb)) continue;
                        Q tq = Along(q);
                        if (!(tq > lo && tq < hi)) continue;
                        if (!own.All(s => CrossesOpen(s.a, s.b, q, narrow) || OnSegment(s.a, s.b, q))) continue;
                        // a vertex of refinement already at q: the piece rerouted through it must not run along another chain's
                        // subsegment (the two chains would coincide there, the part between them collapsed by refinement; Q17, owner
                        // 2026-11-03). Through a hot pixel of snapping (input point, crossing) they coincide in the rounding of the input
                        // itself, as the mesher allows (restore pixels included: snapHot holds the oracle's own, RestorePixels)
                        if (!snapHot.Contains(PixelOf(q, snapPixel))
                            && subs.Any(g => (Same(v[g.A], q) && (g.B == ia || g.B == ib)) || (Same(v[g.B], q) && (g.A == ia || g.A == ib)))) continue;
                        // nor through a vertex of refinement that another chain already runs through: the two chains would meet there (a
                        // pinch; Q17 strict, the mesher's pinch rule in ValidGridSplit, no broader: q no hot pixel of the input's rounding,
                        // and a subsegment ending in q's pixel that belongs to another chain, ie not in the piece's own stretch of the output's subsegment graph (OwnStretch)
                        // ends. By chain identity recomputed from the output (OwnStretch), as the mesher goes by source; self-checks 5c, 5d)
                        if (PinchRuleExcludes(snapHot.Contains(PixelOf(q, snapPixel)),
                            subs.Where(g => Same(v[g.A], q) || Same(v[g.B], q)).Select(g => LegacyEnvelopeChainForDifferential
                                ? own.Any(s => InEnvelopeOf(s.a, s.b, v[g.A], wide) && InEnvelopeOf(s.a, s.b, v[g.B], wide))
                                : OwnStretch(ia, ib).Contains(SubKey(g.A, g.B))))) continue;
                        bool ok = true;
                        foreach (var g in subs) {
                            Pt c = v[g.A], d = v[g.B];
                            if (!Same(c, q) && !Same(d, q) && CrossesOpen(c, d, q, wide)) { ok = false; break; }
                            if (Exact.ProperCross(pa, q, c, d) || Exact.ProperCross(q, pb, c, d)) { ok = false; break; }
                        }
                        if (!ok) continue;
                        foreach (int i in nearVertices) {
                            if (!ok) break;
                            if (Same(v[i], pa) || Same(v[i], pb) || Same(v[i], q)) continue;
                            var pv = new Pt(Snap(v[i].Qx), Snap(v[i].Qy));
                            if (CrossesOpen(pa, q, pv, wide) || CrossesOpen(q, pb, pv, wide)) ok = false;
                        }
                        if (ok) return false;
                    }
                return true;
            }

            /// <summary>
            /// The mesher refines (QualityMesh2.Refines, with the M3 candidate's options): an angle, an area or size bound, a region's area,
            /// or conforming Delaunay, which the candidate turns on unless the constrained-quality mode asks otherwise. Not every M3 input
            /// refines: the constrained-quality mode at angle 0, or a Steiner limit alone (review of #36, S3). The benchmark switch
            /// G3_M2_CONFORMING (QualityMesh2Candidate.ConformingForBenchmarks) is not mirrored: it is for timing measurements (mesh
            /// --no-oracle); a hull domain meshed with it at angle 0 may be judged against the wrong hull model
            /// </summary>
            bool Refining => e.Milestone == Milestone.M3 && ((input.MinAngleDeg ?? 0) > 0 || input.MaxArea != null || input.SizeFunction != null
                || input.SizeConstant != null || input.Regions.Any(g => g.MaxArea != null)
                || (!input.ConstrainedQuality && (input.Conforming || input.MinAngleDeg != null || input.MaxArea != null || input.SizeFunction != null)));

            /// <summary> option C: with a tolerance, the mesher snap-rounds a hull domain's hull edges with the segments when it refines </summary>
            bool HullRounded => input.SnapTolerance > 0 && domain.IsHull && Refining;

            /// <summary>
            /// Input point i lies on a hull tip that collapsed on the grid (D14; owner decision 2026-11-02, MESHING2D_HULL_GRID.md
            /// section 7, Q-H4): at some corner of a hull domain's hull (D13) both hull edges pass one pixel, so snap rounding routes
            /// both edges' chains through its vertex and the tip between has no area. Point i is that corner, with some other used
            /// output vertex in such a pixel, or i's own pixel is such a pixel (it lies on the shared chain). Passing a pixel is decided
            /// as for grid splits: the edge crosses its interior, a hair smaller, or passes its centre (the half-open pixels of snap
            /// rounding; a corner on a pixel boundary is not excused; review). Only where the mesher snap-rounds the hull edges (HullRounded:
            /// an explicit tolerance and refinement at M3); the mesher reports such vertices in its diagnostics.
            /// </summary>
            bool CollapsedHullTip(int i, HashSet<int> usedVertices)
            {
                if (!HullRounded) return false;
                int own = map[i];
                if (own < 0) return false;
                Q pixel = Q.FromDouble(input.SnapTolerance), half = pixel / 2;
                Q Index(Q x) { Q q = x / pixel + new Q(1, 2); var f = BigInteger.Divide(q.N, q.D); if (q.N.Sign < 0 && f * q.D != q.N) f -= 1; return new Q(f, 1); }
                Pt Centre(Pt x) => new Pt(Index(x.Qx) * pixel, Index(x.Qy) * pixel);
                Pt pc = Centre(v[own]);
                Q magnitude = Q.Max(Q.Abs(pc.Qx), Q.Abs(pc.Qy));
                Q narrow = half - Q.Max(half * new Q(BigInteger.One, BigInteger.One << 40), magnitude * new Q(BigInteger.One, BigInteger.One << 48));
                bool Passes(Pt a, Pt b, Pt c)
                {
                    if (Exact.Orient(a, b, c) == 0 && c.Qx >= Q.Min(a.Qx, b.Qx) && c.Qx <= Q.Max(a.Qx, b.Qx) && c.Qy >= Q.Min(a.Qy, b.Qy) && c.Qy <= Q.Max(a.Qy, b.Qy)) return true;
                    Q x0 = c.Qx - narrow, x1 = c.Qx + narrow, y0 = c.Qy - narrow, y1 = c.Qy + narrow;
                    if (Q.Max(a.Qx, b.Qx) <= x0 || Q.Min(a.Qx, b.Qx) >= x1 || Q.Max(a.Qy, b.Qy) <= y0 || Q.Min(a.Qy, b.Qy) >= y1) return false;
                    int pos = 0, neg = 0;
                    foreach (var corner in new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) }) {
                        int or = Exact.Orient(a, b, corner);
                        if (or > 0) ++pos; else if (or < 0) ++neg;
                    }
                    return pos > 0 && neg > 0;
                }
                foreach (var ring in domain.Outers)
                    for (int c = 0; c < ring.Length; ++c) {
                        Pt cp = ring[c], prev = ring[(c + ring.Length - 1) % ring.Length], next = ring[(c + 1) % ring.Length];
                        if (!inPts[i].Equals(cp)) {
                            if (Passes(cp, prev, pc) && Passes(cp, next, pc)) return true;
                            continue;
                        }
                        foreach (int j in usedVertices) {
                            if (j == own) continue;
                            var q = Centre(v[j]);
                            if (q.Equals(pc)) continue;
                            if (Passes(cp, prev, q) && Passes(cp, next, q)) return true;
                        }
                    }
                return false;
            }

            // ------------------------------------------------------------ O-H5: parts of the domain collapsed by snapping (Q17)
            // Owner decision 2026-11-02 (spec Q17): a part of the domain that snap rounding collapses may collapse; the oracle accepts what is
            // missing there only on a collapsed part. Decided on the snapped arrangement as snap rounding defines it (Hobby 1999,
            // Guibas and Marimont 1998; iterated, Halperin and Packer 2002). Hot pixels: the pixels of the input points, of the proper
            // crossings of the input's edges, and of every output vertex (refinement's grid points are vertices too, and every vertex
            // makes its pixel hot). The image of an edge reroutes the input segment through every hot pixel it contacts, then its links
            // between pixel centres again until none is left (iterated snap rounding). The snapped domain is the input's rule (D5) applied to the images
            // of its rings. A link is collapsed where the snapped domain has no interior on either side of it. The definition reads
            // the input, the grid and the output's vertex positions, never the output's edges or triangles. Built once, on the first
            // piece without a chain or unused input point (none for most valid meshes).

            /// <summary> the pixel index of x: floor(x / pixel + 1/2), pixels half-open [(i - 1/2) pixel, (i + 1/2) pixel) </summary>
            internal static BigInteger PixelIndexOf(Q x, Q pixel) => PixelIndex(x, pixel);

            static BigInteger PixelIndex(Q x, Q pixel)
            {
                Q q = x / pixel + new Q(1, 2);
                var f = BigInteger.Divide(q.N, q.D);
                if (q.N.Sign < 0 && f * q.D != q.N) f -= 1;
                return f;
            }

            /// <summary> edge a-b passes the pixel with centre c (O-H4's rule: its interior shrunk by a hair, or its centre) </summary>
            internal static bool PassesPixel(Pt a, Pt b, Pt c, Q pixel)
            {
                Q half = pixel / 2;
                Q magnitude = Q.Max(Q.Abs(c.Qx), Q.Abs(c.Qy));
                Q narrow = half - Q.Max(half * new Q(BigInteger.One, BigInteger.One << 40), magnitude * new Q(BigInteger.One, BigInteger.One << 48));
                if (Exact.Orient(a, b, c) == 0 && c.Qx >= Q.Min(a.Qx, b.Qx) && c.Qx <= Q.Max(a.Qx, b.Qx) && c.Qy >= Q.Min(a.Qy, b.Qy) && c.Qy <= Q.Max(a.Qy, b.Qy)) return true;
                Q x0 = c.Qx - narrow, x1 = c.Qx + narrow, y0 = c.Qy - narrow, y1 = c.Qy + narrow;
                if (Q.Max(a.Qx, b.Qx) <= x0 || Q.Min(a.Qx, b.Qx) >= x1 || Q.Max(a.Qy, b.Qy) <= y0 || Q.Min(a.Qy, b.Qy) >= y1) return false;
                int pos = 0, neg = 0;
                foreach (var corner in new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) }) {
                    int or = Exact.Orient(a, b, corner);
                    if (or > 0) ++pos; else if (or < 0) ++neg;
                }
                return pos > 0 && neg > 0;
            }

            /// <summary> the parameters in [0, 1] where segment p-q crosses a pixel boundary line, with 0 and 1, sorted </summary>
            static List<Q> PixelBreaks(Pt p, Pt q, Q pixel)
            {
                var ts = new List<Q> { Q.Zero, Q.One };
                foreach (bool y in new[] { false, true }) {
                    Q from = y ? p.Qy : p.Qx, to = y ? q.Qy : q.Qx;
                    if (from == to) continue;
                    BigInteger i0 = PixelIndex(Q.Min(from, to), pixel), i1 = PixelIndex(Q.Max(from, to), pixel);
                    for (var i = i0; i < i1; ++i) ts.Add(((new Q(i, 1) + new Q(1, 2)) * pixel - from) / (to - from));
                }
                return ts.Distinct().OrderBy(t => t).ToList();
            }

            static (BigInteger x, BigInteger y) PixelOf(Pt p, Q pixel) => (PixelIndex(p.Qx, pixel), PixelIndex(p.Qy, pixel));

            static Pt Centre((BigInteger x, BigInteger y) n, Q pixel) => new Pt(new Q(n.x, 1) * pixel, new Q(n.y, 1) * pixel);

            /// <summary> where segment p-q enters the closed square of half-width h around c, as a parameter (exact); null if it misses it </summary>
            static Q? Entry(Pt p, Pt q, Pt c, Q h)
            {
                Q t0 = Q.Zero, t1 = Q.One;
                foreach (bool y in new[] { false, true }) {
                    Q from = y ? p.Qy : p.Qx, d = (y ? q.Qy : q.Qx) - from, mid = y ? c.Qy : c.Qx;
                    if (d.Sign == 0) { if (from < mid - h || from > mid + h) return null; continue; }
                    Q a = (mid - h - from) / d, b = (mid + h - from) / d;
                    if (a > b) { var s = a; a = b; b = s; }
                    t0 = Q.Max(t0, a); t1 = Q.Min(t1, b);
                    if (t0 > t1) return null;
                }
                return t0;
            }

            /// <summary>
            /// Snap rounding's contact of segment p-q with the pixel of centre c (Hobby's half-open square, as the mesher's
            /// SnapRounding2.Meets): its open interior, or its closed left and bottom sides without the top-left and bottom-right corners,
            /// a corner only the bottom-left one (exact)
            /// </summary>
            internal static bool ContactPixel(Pt p, Pt q, Pt c, Q pixel)
            {
                Q h = pixel / 2, x0 = c.Qx - h, x1 = c.Qx + h, y0 = c.Qy - h, y1 = c.Qy + h;
                Q lx = Q.Min(p.Qx, q.Qx), hx = Q.Max(p.Qx, q.Qx), ly = Q.Min(p.Qy, q.Qy), hy = Q.Max(p.Qy, q.Qy);
                if (hx < x0 || lx > x1 || hy < y0 || ly > y1) return false;
                bool point = p.Equals(q);
                int pos = 0, neg = 0;
                if (!point)
                    foreach (var corner in new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) }) {
                        int or = Exact.Orient(p, q, corner);
                        if (or > 0) ++pos; else if (or < 0) ++neg;
                    }
                bool open = point ? p.Qx > x0 && p.Qx < x1 && p.Qy > y0 && p.Qy < y1 : hx > x0 && lx < x1 && hy > y0 && ly < y1 && pos > 0 && neg > 0;
                if (open) return true;
                if (!point && (pos == 4 || neg == 4)) return false;
                if (p.Qy == q.Qy && (p.Qy == y0 || p.Qy == y1)) return p.Qy == y0 && hx >= x0 && lx < x1;
                if (p.Qx == q.Qx && (p.Qx == x0 || p.Qx == x1)) return p.Qx == x0 && hy >= y0 && ly < y1;
                foreach (var e in new[] { p, q })
                    if (e.Qx >= x0 && e.Qx <= x1 && e.Qy >= y0 && e.Qy <= y1) return e.Qx < x1 && e.Qy < y1;
                return Exact.Orient(p, q, new Pt(x0, y0)) == 0 && lx <= x0 && x0 <= hx && ly <= y0 && y0 <= hy;
            }

            /// <summary> where segment p-q leaves the closed square of half-width h around c, as a parameter (exact; c met) </summary>
            static Q ExitOf(Pt p, Pt q, Pt c, Q h)
            {
                Q t1 = Q.One;
                foreach (bool y in new[] { false, true }) {
                    Q from = y ? p.Qy : p.Qx, d = (y ? q.Qy : q.Qx) - from, mid = y ? c.Qy : c.Qx;
                    if (d.Sign == 0) continue;
                    Q a = (mid - h - from) / d, b = (mid + h - from) / d;
                    t1 = Q.Min(t1, Q.Max(a, b));
                }
                return t1;
            }

            /// <summary>
            /// The first pass of an image: the hot pixels other than u and w (the end pixels) that the input segment p-q meets, in order of
            /// entry, then exit, as the mesher orders them. As the mesher: the hot pixels of its snapping (contact set: input points and
            /// crossings; null: all) by contact (<see cref="ContactPixel"/>), those its refinement made (output vertices) by O-H4's rule,
            /// the interior (the mesher's grid splits avoid only pixels a constraint crosses inside, ValidGridSplit). Candidates: the
            /// pixels whose interior it crosses and their neighbours (contact on a side or corner is with a neighbour of a crossed
            /// pixel), where they are fewer than the hot pixels, else the hot pixels in its box widened by one.
            /// </summary>
            static List<(BigInteger x, BigInteger y)> SegmentPassed(Pt p, Pt q, (BigInteger x, BigInteger y) u, (BigInteger x, BigInteger y) w,
                HashSet<(BigInteger x, BigInteger y)> hot, List<(BigInteger x, BigInteger y)> hotList, HashSet<(BigInteger x, BigInteger y)> contact, Q pixel, ref long work)
            {
                var pp = PixelOf(p, pixel); var pq = PixelOf(q, pixel);
                BigInteger span = BigInteger.Abs(pq.x - pp.x) + BigInteger.Abs(pq.y - pp.y);
                var candidates = new List<(BigInteger x, BigInteger y)>();
                if (9 * (span + 1) <= hotList.Count) {
                    var ts = PixelBreaks(p, q, pixel);
                    work += 9 * ts.Count;
                    var seen = new HashSet<(BigInteger x, BigInteger y)>();
                    for (int j = 0; j + 1 < ts.Count; ++j) {
                        Q t = (ts[j] + ts[j + 1]) / 2;
                        var n = PixelOf(new Pt(p.Qx + (q.Qx - p.Qx) * t, p.Qy + (q.Qy - p.Qy) * t), pixel);
                        for (int dx = -1; dx <= 1; ++dx)
                            for (int dy = -1; dy <= 1; ++dy) {
                                var m = (n.x + dx, n.y + dy);
                                if (hot.Contains(m) && seen.Add(m)) candidates.Add(m);
                            }
                    }
                } else {
                    work += hotList.Count;
                    BigInteger x0 = BigInteger.Min(pp.x, pq.x) - 1, x1 = BigInteger.Max(pp.x, pq.x) + 1, y0 = BigInteger.Min(pp.y, pq.y) - 1, y1 = BigInteger.Max(pp.y, pq.y) + 1;
                    foreach (var n in hotList) if (n.x >= x0 && n.x <= x1 && n.y >= y0 && n.y <= y1) candidates.Add(n);
                }
                var found = new List<(Q t, Q t1, (BigInteger x, BigInteger y) n)>();
                Q half = pixel / 2;
                foreach (var n in candidates) {
                    if (n.Equals(u) || n.Equals(w)) continue;
                    var c = Centre(n, pixel);
                    if (!(contact == null || contact.Contains(n) ? ContactPixel(p, q, c, pixel) : PassesPixel(p, q, c, pixel))) continue;
                    var t = Entry(p, q, c, half);
                    if (t != null) found.Add((t.Value, ExitOf(p, q, c, half), n));
                }
                return found.OrderBy(f => f.t).ThenBy(f => f.t1).Select(f => f.n).ToList();
            }

            /// <summary>
            /// The hot pixels other than u and w that the link between their centres passes, in order along it. Walks the pixels whose
            /// interior the link crosses with integers (the next pixel boundary in x or in y by comparing (2i + 1) |dy| with (2j + 1) |dx|;
            /// both at once through a corner, whose two side pixels are only touched), each hot one confirmed by O-H4's rule (the open
            /// interior, as the mesher reroutes links); work: the link's pixel count, or the hot pixel count where that is smaller (then
            /// each hot pixel in the link's box is tested).
            /// </summary>
            static List<(BigInteger x, BigInteger y)> HotPassed((BigInteger x, BigInteger y) u, (BigInteger x, BigInteger y) w,
                HashSet<(BigInteger x, BigInteger y)> hot, List<(BigInteger x, BigInteger y)> hotList, Q pixel, ref long work)
            {
                BigInteger dxB = w.x - u.x, dyB = w.y - u.y, limit = new BigInteger(int.MaxValue);
                BigInteger span = BigInteger.Abs(dxB) + BigInteger.Abs(dyB);
                if (span > hotList.Count || BigInteger.Abs(dxB) > limit || BigInteger.Abs(dyB) > limit) {
                    // the hot pixels in the link's box, each tested (more pixels crossed than hot pixels)
                    work += hotList.Count;
                    Pt pu = Centre(u, pixel), pw = Centre(w, pixel);
                    BigInteger bx0 = BigInteger.Min(u.x, w.x), bx1 = BigInteger.Max(u.x, w.x), by0 = BigInteger.Min(u.y, w.y), by1 = BigInteger.Max(u.y, w.y);
                    var hits = new List<(Q t, (BigInteger x, BigInteger y) n)>();
                    foreach (var n in hotList) {
                        if (n.x < bx0 || n.x > bx1 || n.y < by0 || n.y > by1 || n.Equals(u) || n.Equals(w)) continue;
                        var c = Centre(n, pixel);
                        if (!PassesPixel(pu, pw, c, pixel)) continue;
                        var t = Entry(pu, pw, c, pixel / 2);
                        if (t != null) hits.Add((t.Value, n));
                    }
                    return hits.OrderBy(f => f.t).Select(f => f.n).ToList();
                }
                long ax = (long)BigInteger.Abs(dxB), ay = (long)BigInteger.Abs(dyB);
                int sx = dxB.Sign, sy = dyB.Sign;
                var result = new List<(BigInteger x, BigInteger y)>();
                Pt cu = Centre(u, pixel), cw = Centre(w, pixel);
                long i = 0, j = 0;
                BigInteger x = u.x, y = u.y;
                while (i < ax || j < ay) {
                    long cx = i < ax ? (2 * i + 1) * ay : long.MaxValue, cy = j < ay ? (2 * j + 1) * ax : long.MaxValue;
                    if (cx <= cy) { x += sx; ++i; }
                    if (cy <= cx) { y += sy; ++j; }
                    ++work;
                    var n = (x, y);
                    if (n.Equals(w) || !hot.Contains(n)) continue;
                    if (PassesPixel(cu, cw, Centre(n, pixel), pixel)) result.Add(n);
                }
                return result;
            }

            /// <summary>
            /// The iterated snap-rounded image of segment a-b, as pixels from a's to b's (Hobby 1999; iterated, Halperin and Packer 2002).
            /// The first pass reroutes the segment a-b itself through every hot pixel it contacts (Hobby's half-open pixels, the mesher's
            /// rule), in order along it; each further pass reroutes every link through the hot pixels whose interior it passes and that the
            /// image does not hold yet, until a pass adds none. (A link passing a pixel the image already holds elsewhere is not rerouted to
            /// it again, so the image stays a simple pixel path; no case where this matters is known, a fuzz of 26 000 segments found none.)
            /// Terminates: the hot pixels are finite and every pass but the last adds at least one to the image, which only grows. Cost per
            /// pass: per link the smaller of its pixel count and the hot pixel count; at most one pass per hot pixel the image gains, plus one.
            /// </summary>
            internal static List<(BigInteger x, BigInteger y)> Image(Pt a, Pt b, HashSet<(BigInteger x, BigInteger y)> hot, List<(BigInteger x, BigInteger y)> hotList, Q pixel, ref long work,
                HashSet<(BigInteger x, BigInteger y)> contact = null)
            {
                var first = PixelOf(a, pixel); var last = PixelOf(b, pixel);
                var image = new List<(BigInteger x, BigInteger y)> { first };
                var held = new HashSet<(BigInteger x, BigInteger y)> { first, last };
                foreach (var n in SegmentPassed(a, b, first, last, hot, hotList, contact, pixel, ref work)) if (held.Add(n)) image.Add(n);
                if (!last.Equals(first)) image.Add(last);
                for (bool changed = true; changed; ) {
                    changed = false;
                    var next = new List<(BigInteger x, BigInteger y)> { image[0] };
                    for (int k = 0; k + 1 < image.Count; ++k) {
                        foreach (var n in HotPassed(image[k], image[k + 1], hot, hotList, pixel, ref work))
                            if (held.Add(n)) { next.Add(n); changed = true; }
                        next.Add(image[k + 1]);
                    }
                    image = next;
                }
                return image;
            }

            /// <summary> test hook: the work of building images (pixels walked, hot pixels tested), summed over checks on this thread </summary>
            [ThreadStatic] internal static long ImageWorkForTests;

            Q snapPixel;
            HashSet<(BigInteger x, BigInteger y)> hot, snapHot;
            List<(BigInteger x, BigInteger y)> hotList;
            List<(BigInteger x, BigInteger y)> snapHotList;

            /// <summary> the images of all input edges under one set of hot pixels, the snapped domain of their rings, the links at each pixel </summary>
            sealed class SnapState
            {
                public readonly Dictionary<(Pt a, Pt b), List<(BigInteger x, BigInteger y)>> Images = new Dictionary<(Pt a, Pt b), List<(BigInteger x, BigInteger y)>>();
                public Domain Domain;
                public readonly Dictionary<((BigInteger x, BigInteger y), (BigInteger x, BigInteger y)), bool> Collapsed = new Dictionary<((BigInteger x, BigInteger y), (BigInteger x, BigInteger y)), bool>();
                public readonly Dictionary<(BigInteger x, BigInteger y), List<(BigInteger x, BigInteger y)>> LinksAt = new Dictionary<(BigInteger x, BigInteger y), List<(BigInteger x, BigInteger y)>>();
            }
            /// <summary> with all hot pixels (the output vertices' too), and with those of the input alone (points, crossings) </summary>
            SnapState full, inputOnly;

            List<(Pt a, Pt b)> inputEdges;

            /// <summary> the hot pixels: those of the input points, of the proper crossings of the input's edges and of the output vertices; once </summary>
            void SnapHot()
            {
                if (hot != null) return;
                snapPixel = Q.FromDouble(input.SnapTolerance);
                hot = new HashSet<(BigInteger x, BigInteger y)>(); hotList = new List<(BigInteger x, BigInteger y)>();
                void Hot(Pt p) { var n = PixelOf(p, snapPixel); if (hot.Add(n)) hotList.Add(n); }
                foreach (var p in inPts) Hot(p);
                inputEdges = sources.Select(s => (s.a, s.b)).Concat(domain.Edges()).Distinct().ToList();
                foreach (var p in Crossings(inputEdges)) Hot(p);
                // the pixels of SRR's restore step (D14), recomputed from its specification: part of the rounding of the input
                r.RestorePixels = RestorePixels(new HashSet<(BigInteger x, BigInteger y)>(hot), new List<(BigInteger x, BigInteger y)>(hotList));
                foreach (var n in r.RestorePixels) if (hot.Add(n)) hotList.Add(n);
                snapHot = new HashSet<(BigInteger x, BigInteger y)>(hot);   // those of snapping (input points, crossings, restore): met by contact
                snapHotList = new List<(BigInteger x, BigInteger y)>(hotList);
                foreach (var p in v) Hot(p);
            }

            /// <summary> segment p-q crosses the open interior of the pixel of centre c (a point: lies in it), exactly </summary>
            static bool OpenPixel(Pt p, Pt q, Pt c, Q pixel)
            {
                Q h = pixel / 2, x0 = c.Qx - h, x1 = c.Qx + h, y0 = c.Qy - h, y1 = c.Qy + h;
                Q lx = Q.Min(p.Qx, q.Qx), hx = Q.Max(p.Qx, q.Qx), ly = Q.Min(p.Qy, q.Qy), hy = Q.Max(p.Qy, q.Qy);
                if (hx <= x0 || lx >= x1 || hy <= y0 || ly >= y1) return false;
                if (p.Equals(q)) return true;
                int pos = 0, neg = 0;
                foreach (var corner in new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) }) {
                    int or = Exact.Orient(p, q, corner);
                    if (or > 0) ++pos; else if (or < 0) ++neg;
                }
                return pos > 0 && neg > 0;
            }

            /// <summary>
            /// SRR's restore step (Belussi, Migliorini, Negri and Pelagatti 2016, ChoosePixel of their Table II) on the input alone, as D14
            /// specifies it (MESHING2D_SNAP.md section 3b; recomputed here, not taken from the mesher). Rounds: every input edge is
            /// snap-rounded through the hot pixels it contacts (Hobby's half-open pixels), in order along it; a hot pixel whose open
            /// interior a link of a chain crosses is critical. For each critical pixel and link a - b of the chain of input segment s,
            /// among the eight neighbours of the critical pixel that are not hot, whose open interior s crosses, and for which neither
            /// a - p nor p - b crosses the critical pixel's interior ("useful"): the nearest to s of those no link crosses ("free", R1),
            /// else the nearest useful one (R3); with none, the nearest pixel within two of it, not hot, whose interior s crosses and whose
            /// centre projects strictly between a's and b's (widened). Nearest: the squared distance of the pixel's centre to s, ties by
            /// the pixel's x, then y. A round's choices are made on one arrangement and all made hot at its end (their Proposition
            /// 4.17); rounds repeat until no critical pixel is left. The edges: the input segments and ring edges, and a hull domain's hull
            /// edges where the mesher snap-rounds them (HullRounded: it refines, option C)
            /// </summary>
            List<(BigInteger x, BigInteger y)> RestorePixels(HashSet<(BigInteger x, BigInteger y)> hotR, List<(BigInteger x, BigInteger y)> hotRList)
            {
                var added = new List<(BigInteger x, BigInteger y)>();
                var edgesR = new List<(Pt a, Pt b)>();
                var seenEdge = new HashSet<(Pt, Pt)>();
                void AddEdge(Pt a, Pt b)
                {
                    if (a.Equals(b)) return;
                    var key = a.Qx < b.Qx || a.Qx == b.Qx && a.Qy <= b.Qy ? (a, b) : (b, a);
                    if (seenEdge.Add(key)) edgesR.Add((a, b));
                }
                foreach (var s in sources) AddEdge(s.a, s.b);
                // (a hull of fewer than three corners, all points collinear, has no hull edges to round, as in the mesher)
                if (HullRounded) {
                    var hullEdges = domain.Edges().ToList();
                    if (hullEdges.Count >= 3) foreach (var (a, b) in hullEdges) AddEdge(a, b);
                }
                Q pixel = snapPixel;
                // every round adds at least one pixel (only pixels not hot are chosen), and SRR adds at most n (n - 1) hot pixels for n
                // edges after the first rounding (their Proposition 4.25); past that bound the step reports instead of stopping silently
                long bound = -1;
                for (long round = 0; ; ++round) {
                    // route every edge through the hot pixels it contacts
                    long work = 0;
                    var chainsR = new List<List<(BigInteger x, BigInteger y)>>();
                    foreach (var (a, b) in edgesR) {
                        var u = PixelOf(a, pixel); var w = PixelOf(b, pixel);
                        var chain = new List<(BigInteger x, BigInteger y)> { u };
                        if (!u.Equals(w)) {
                            foreach (var n in SegmentPassed(a, b, u, w, hotR, hotRList, null, pixel, ref work)) if (!chain[chain.Count - 1].Equals(n)) chain.Add(n);
                            if (!chain[chain.Count - 1].Equals(w)) chain.Add(w);
                        }
                        chainsR.Add(chain);
                    }
                    // the critical pixels: hot pixels other than its ends whose open interior a link crosses (candidates walked
                    // along the link, or taken from the hot pixels in its box when there are fewer of those)
                    var links = new List<(Pt a, Pt b)>();
                    var critical = new List<((BigInteger x, BigInteger y) cp, int edge, int link)>();
                    for (int c = 0; c < chainsR.Count; ++c)
                        for (int k = 0; k + 1 < chainsR[c].Count; ++k) {
                            var la = chainsR[c][k]; var lb = chainsR[c][k + 1];
                            Pt pa = Centre(la, pixel), pb = Centre(lb, pixel);
                            links.Add((pa, pb));
                            var candidates = new HashSet<(BigInteger x, BigInteger y)>();
                            BigInteger span = BigInteger.Abs(lb.x - la.x) + BigInteger.Abs(lb.y - la.y);
                            if (9 * (span + 1) <= hotRList.Count) {
                                var ts = PixelBreaks(pa, pb, pixel);
                                for (int j = 0; j + 1 < ts.Count; ++j) {
                                    Q t = (ts[j] + ts[j + 1]) / 2;
                                    var n = PixelOf(new Pt(pa.Qx + (pb.Qx - pa.Qx) * t, pa.Qy + (pb.Qy - pa.Qy) * t), pixel);
                                    for (int dx = -1; dx <= 1; ++dx)
                                        for (int dy = -1; dy <= 1; ++dy) { var m = (n.x + dx, n.y + dy); if (hotR.Contains(m)) candidates.Add(m); }
                                }
                            } else {
                                BigInteger x0 = BigInteger.Min(la.x, lb.x), x1 = BigInteger.Max(la.x, lb.x), y0 = BigInteger.Min(la.y, lb.y), y1 = BigInteger.Max(la.y, lb.y);
                                foreach (var n in hotRList) if (n.x >= x0 && n.x <= x1 && n.y >= y0 && n.y <= y1) candidates.Add(n);
                            }
                            foreach (var m in candidates.OrderBy(m => m.x).ThenBy(m => m.y))
                                if (!m.Equals(la) && !m.Equals(lb) && OpenPixel(pa, pb, Centre(m, pixel), pixel)) critical.Add((m, c, k));
                        }
                    // a pixel no link crosses is "free" (memoized per round; candidates are the few neighbours of critical pixels)
                    var crossedMemo = new Dictionary<(BigInteger x, BigInteger y), bool>();
                    bool Crossed((BigInteger x, BigInteger y) n)
                    {
                        if (crossedMemo.TryGetValue(n, out bool known)) return known;
                        Pt pc = Centre(n, pixel);
                        bool any = links.Any(l => OpenPixel(l.a, l.b, pc, pixel));
                        crossedMemo.Add(n, any);
                        return any;
                    }
                    if (critical.Count == 0) break;
                    if (bound < 0) { long n = links.Count; bound = n * (n - 1) + 1; }
                    if (round > bound) { r.Add(Violation.CannotJudge, "SRR's restore step did not settle within its bound of " + bound + " rounds (Proposition 4.25)"); return added; }
                    var chosenAll = new List<(BigInteger x, BigInteger y)>();
                    foreach (var (cp, c, k) in critical) {
                        var (sa, sb) = edgesR[c];
                        Pt la = Centre(chainsR[c][k], pixel), lb = Centre(chainsR[c][k + 1], pixel), ccp = Centre(cp, pixel);
                        Q Dist2((BigInteger x, BigInteger y) n)
                        {
                            Pt pc = Centre(n, pixel);
                            Q dx = sb.Qx - sa.Qx, dy = sb.Qy - sa.Qy, ux = pc.Qx - sa.Qx, uy = pc.Qy - sa.Qy, dot = ux * dx + uy * dy, len2 = dx * dx + dy * dy;
                            if (dot.Sign <= 0 || len2.Sign == 0) return ux * ux + uy * uy;
                            if (dot >= len2) { Q vx = pc.Qx - sb.Qx, vy = pc.Qy - sb.Qy; return vx * vx + vy * vy; }
                            Q cr = ux * dy - uy * dx;
                            return cr * cr / len2;
                        }
                        (BigInteger x, BigInteger y) Nearest(List<(BigInteger x, BigInteger y)> list)
                            => list.OrderBy(n => Dist2(n)).ThenBy(n => n.x).ThenBy(n => n.y).First();
                        var useful = new List<(BigInteger x, BigInteger y)>();
                        for (int dx = -1; dx <= 1; ++dx)
                            for (int dy = -1; dy <= 1; ++dy) {
                                if (dx == 0 && dy == 0) continue;
                                var n = (cp.x + dx, cp.y + dy);
                                Pt pc = Centre(n, pixel);
                                if (hotR.Contains(n) || !OpenPixel(sa, sb, pc, pixel)) continue;
                                if (OpenPixel(la, pc, ccp, pixel) || OpenPixel(pc, lb, ccp, pixel)) continue;
                                useful.Add(n);
                            }
                        var free = useful.Where(n => !Crossed(n)).ToList();
                        if (free.Count > 0) chosenAll.Add(Nearest(free));
                        else if (useful.Count > 0) chosenAll.Add(Nearest(useful));
                        else {
                            Q dxs = sb.Qx - sa.Qx, dys = sb.Qy - sa.Qy;
                            Q Along(Pt q) => (q.Qx - sa.Qx) * dxs + (q.Qy - sa.Qy) * dys;
                            Q ta = Along(la), tb = Along(lb);
                            var wider = new List<(BigInteger x, BigInteger y)>();
                            for (int dx = -2; dx <= 2; ++dx)
                                for (int dy = -2; dy <= 2; ++dy) {
                                    var n = (cp.x + dx, cp.y + dy);
                                    Pt pc = Centre(n, pixel);
                                    Q tp = Along(pc);
                                    if (!hotR.Contains(n) && OpenPixel(sa, sb, pc, pixel) && Q.Min(ta, tb) < tp && tp < Q.Max(ta, tb)) wider.Add(n);
                                }
                            if (wider.Count == 0) { r.Add(Violation.CannotJudge, "SRR's restore step found no pixel for the critical pixel " + Centre(cp, pixel)); return added; }
                            chosenAll.Add(Nearest(wider));
                        }
                    }
                    int before = added.Count;
                    foreach (var n in chosenAll) if (hotR.Add(n)) { hotRList.Add(n); added.Add(n); }
                    if (added.Count == before) { r.Add(Violation.CannotJudge, "SRR's restore step chose no new pixel in a round"); return added; }
                }
                return added;
            }

            /// <summary>
            /// Snap rounding's invariant on the output's chains (Hobby 1999): no subsegment passes a hot pixel other than its ends' (O-H4's
            /// rule: its interior shrunk by a hair, or its centre); a chain is routed through every hot pixel it meets. Because every output
            /// vertex makes its pixel hot, a Steiner point placed in the pixel of a chain that does not run through it is reported here, and
            /// cannot buy an excuse from O-H5. Only with a snap tolerance and reported subsegments. Work per subsegment: the smaller of its
            /// pixel count and the hot pixel count.
            /// </summary>
            void ChainsThroughHotPixels()
            {
                // (identity outputs only, which are held to the grid; a foreign producer, eg Triangle's files, is no snap rounder)
                if (!(input.SnapTolerance > 0) || subsegmentSet == null || !o.IdentityRules) return;
                SnapHot();
                long work = 0;
                foreach (long key in subsegmentSet) {
                    if (!edges.ContainsKey(key)) continue;
                    var pa = PixelOf(v[KeyA(key)], snapPixel); var pb = PixelOf(v[KeyB(key)], snapPixel);
                    if (pa.Equals(pb)) continue;
                    var passed = HotPassed(pa, pb, hot, hotList, snapPixel, ref work);
                    if (passed.Count > 0)
                        r.Add(Violation.ChainPassesHotPixel, "subsegment " + v[KeyA(key)] + " - " + v[KeyB(key)] + " passes the hot pixel of " + Centre(passed[0], snapPixel) + " without running through it");
                }
                ImageWorkForTests += work;
            }

            /// <summary>
            /// O-H5's two states, once: the images with all hot pixels (<see cref="full"/>), and with the input's alone
            /// (<see cref="inputOnly"/>, rule (ii'): an excuse must already hold there, so output vertices reroute only inside parts the
            /// input's rounding collapses and never create a collapse; coordinator 2026-11-03)
            /// </summary>
            void Snap()
            {
                if (full != null) return;
                SnapHot();
                long work = 0;
                full = BuildState(hot, hotList, ref work);
                // the two states are one where no output vertex lies beyond the input's hot pixels plus the restore pixels the
                // oracle computes itself (RestorePixels, Belussi et al. 2016)
                inputOnly = hot.Count == snapHot.Count ? full : BuildState(snapHot, snapHotList, ref work);
                ImageWorkForTests += work;
            }

            SnapState BuildState(HashSet<(BigInteger x, BigInteger y)> hotSet, List<(BigInteger x, BigInteger y)> hotSetList, ref long work)
            {
                var st = new SnapState();
                long w = 0;
                List<(BigInteger x, BigInteger y)> Of(Pt a, Pt b)
                {
                    if (st.Images.TryGetValue((a, b), out var known)) return known;
                    if (st.Images.TryGetValue((b, a), out var back)) { known = new List<(BigInteger x, BigInteger y)>(back); known.Reverse(); }
                    else known = Image(a, b, hotSet, hotSetList, snapPixel, ref w, snapHot);
                    st.Images[(a, b)] = known;
                    return known;
                }
                void Link((BigInteger x, BigInteger y) p, (BigInteger x, BigInteger y) q)
                {
                    if (!st.LinksAt.TryGetValue(p, out var list)) st.LinksAt.Add(p, list = new List<(BigInteger x, BigInteger y)>());
                    if (!list.Contains(q)) list.Add(q);
                }
                // the hull model: with refinement (Refining) the mesher snap-rounds a hull domain's hull edges with the segments (option C), so
                // the snapped hull is their image; without it the triangulation's boundary is the convex hull of the snapped vertices,
                // straight between hot pixel centres, and the hull edges are no chains
                bool roundedHull = !domain.IsHull || HullRounded;
                var hullEdges = roundedHull ? null : new HashSet<(Pt, Pt)>(domain.Rings[0].ring.Select((p, i) => (p, domain.Rings[0].ring[(i + 1) % domain.Rings[0].ring.Length])));
                var sourceEdges = roundedHull ? null : new HashSet<(Pt, Pt)>(sources.SelectMany(s => new[] { (s.a, s.b), (s.b, s.a) }));
                foreach (var g in inputEdges) {
                    if (!roundedHull && (hullEdges.Contains((g.a, g.b)) || hullEdges.Contains((g.b, g.a))) && !sourceEdges.Contains((g.a, g.b))) continue;
                    var image = Of(g.a, g.b);
                    for (int k = 0; k + 1 < image.Count; ++k) { Link(image[k], image[k + 1]); Link(image[k + 1], image[k]); }
                }
                var rings = new List<(Pt[] ring, bool hole)>();
                foreach (var (ring, hole) in domain.Rings) {
                    if (!roundedHull && rings.Count == 0) {
                        // every snapped vertex on the hull's boundary is a vertex of a boundary edge in the triangulation, collinear
                        // ones included; the boundary's links keep their pixels from counting as collapsed (review of #36, S1)
                        var hullRing = SnappedHull(hotSetList);
                        for (int k = 0; k < hullRing.Count; ++k) { var p = hullRing[k]; var q = hullRing[(k + 1) % hullRing.Count]; Link(p, q); Link(q, p); }
                        rings.Add((hullRing.Select(n => Centre(n, snapPixel)).ToArray(), false));
                        continue;
                    }
                    var nodes = new List<(BigInteger x, BigInteger y)>();
                    for (int i = 0; i < ring.Length; ++i) {
                        if (ring[i].Equals(ring[(i + 1) % ring.Length])) continue;
                        foreach (var n in Of(ring[i], ring[(i + 1) % ring.Length])) if (nodes.Count == 0 || !nodes[nodes.Count - 1].Equals(n)) nodes.Add(n);
                    }
                    while (nodes.Count > 1 && nodes[nodes.Count - 1].Equals(nodes[0])) nodes.RemoveAt(nodes.Count - 1);
                    rings.Add((nodes.Select(n => Centre(n, snapPixel)).ToArray(), hole));
                }
                st.Domain = new Domain(rings, !e.Reference);
                work += w;
                return st;
            }

            /// <summary>
            /// the boundary of the triangulation of the snapped vertices (a hull domain without refinement): the convex hull of the pixel
            /// centres, counter-clockwise, with the centres on its edges kept in order along them. In the full state the output's
            /// vertices take part, so it is shaped partly by the output judged; stray vertices are rejected by the other rules
            /// </summary>
            List<(BigInteger x, BigInteger y)> SnappedHull(List<(BigInteger x, BigInteger y)> pixels)
            {
                var byCentre = new Dictionary<Pt, (BigInteger x, BigInteger y)>();
                foreach (var n in pixels) byCentre[Centre(n, snapPixel)] = n;
                var corners = Exact.ConvexHull(byCentre.Keys).ToList();
                var ring = new List<(BigInteger x, BigInteger y)>();
                for (int k = 0; k < corners.Count; ++k) {
                    Pt a = corners[k], b = corners[(k + 1) % corners.Count];
                    ring.Add(byCentre[a]);
                    if (corners.Count < 2) break;
                    Q dx = b.Qx - a.Qx, dy = b.Qy - a.Qy, len = dx * dx + dy * dy;
                    var between = byCentre.Keys.Where(p => !p.Equals(a) && !p.Equals(b) && Exact.Orient(a, b, p) == 0)
                        .Select(p => (p, t: (p.Qx - a.Qx) * dx + (p.Qy - a.Qy) * dy)).Where(x => x.t.Sign > 0 && x.t < len).OrderBy(x => x.t);
                    foreach (var x in between) ring.Add(byCentre[x.p]);
                }
                return ring;
            }

            /// <summary> the points where two of the edges cross properly (exact), found through a grid of their boxes </summary>
            static List<Pt> Crossings(List<(Pt a, Pt b)> edges)
            {
                var box = new double[4 * edges.Count];
                for (int i = 0; i < edges.Count; ++i) {
                    var (a, b) = edges[i];
                    if (a.Bounds(false, out double ax0, out double ax1) && b.Bounds(false, out double bx0, out double bx1)
                        && a.Bounds(true, out double ay0, out double ay1) && b.Bounds(true, out double by0, out double by1)) {
                        box[4 * i] = Math.Min(ax0, bx0); box[4 * i + 1] = Math.Max(ax1, bx1); box[4 * i + 2] = Math.Min(ay0, by0); box[4 * i + 3] = Math.Max(ay1, by1);
                    } else for (int k = 0; k < 4; ++k) box[4 * i + k] = double.NaN;
                }
                var grid = new BoxGrid(box, edges.Count);
                var result = new List<Pt>();
                for (int i = 0; i < edges.Count; ++i) {
                    var candidates = double.IsNaN(box[4 * i]) ? Enumerable.Range(0, edges.Count).ToList()
                        : grid.Query(box[4 * i], box[4 * i + 1], box[4 * i + 2], box[4 * i + 3]);
                    foreach (int j in candidates) {
                        if (j <= i) continue;   // (a pair is met from both sides, an unknown box from every other edge)
                        var (a, b) = edges[i]; var (c, d) = edges[j];
                        if (!Exact.ProperCross(a, b, c, d)) continue;
                        Q den = (b.Qx - a.Qx) * (d.Qy - c.Qy) - (b.Qy - a.Qy) * (d.Qx - c.Qx);
                        Q t = ((c.Qx - a.Qx) * (d.Qy - c.Qy) - (c.Qy - a.Qy) * (d.Qx - c.Qx)) / den;
                        result.Add(new Pt(a.Qx + (b.Qx - a.Qx) * t, a.Qy + (b.Qy - a.Qy) * t));
                    }
                }
                return result;
            }

            /// <summary> the two points just beside the middle of link p-q (2^-40 of its length off it) </summary>
            (Pt left, Pt right) Beside((BigInteger x, BigInteger y) p, (BigInteger x, BigInteger y) q)
            {
                Pt a = Centre(p, snapPixel), b = Centre(q, snapPixel);
                // images are snap-rounded, so links meet only at pixel centres; another link through these points would have to pass
                // within 2^-40 of the length of this one's middle, which a link between lattice points does only on it
                Q mx = (a.Qx + b.Qx) / 2, my = (a.Qy + b.Qy) / 2, eps = new Q(BigInteger.One, BigInteger.One << 40);
                Q nx = -(b.Qy - a.Qy) * eps, ny = (b.Qx - a.Qx) * eps;
                return (new Pt(mx + nx, my + ny), new Pt(mx - nx, my - ny));
            }

            /// <summary> link p-q of an image is collapsed in state st: its snapped domain has no interior just beside its middle on either side </summary>
            static bool IsLink(SnapState st, (BigInteger x, BigInteger y) p, (BigInteger x, BigInteger y) q) => st.LinksAt.TryGetValue(p, out var at) && at.Contains(q);

            bool LinkCollapsed(SnapState st, (BigInteger x, BigInteger y) p, (BigInteger x, BigInteger y) q)
            {
                var key = p.x < q.x || p.x == q.x && p.y < q.y ? (p, q) : (q, p);
                if (st.Collapsed.TryGetValue(key, out bool known)) return known;
                var (l, rr) = Beside(p, q);
                bool collapsed = st.Domain.Classify(l) != Where.Inside && st.Domain.Classify(rr) != Where.Inside;
                st.Collapsed[key] = collapsed;
                return collapsed;
            }

            /// <summary>
            /// O-H5 with rule (ii') on the image of source: which links of its full image are excused, and whether output vertices
            /// rerouted any of them. The full image and the input's own image are matched at the pixels they share, in order (the ends are
            /// shared: both are input points); a link of the full image is excused if it is collapsed there and every link of the
            /// input's own image between the same two shared pixels is collapsed in the input's own rounding. So an output vertex may
            /// reroute a chain only across a stretch the input's rounding collapses, and never creates a collapse (owner 2026-11-03).
            /// Judging the input's rounding beside the full link's middle instead is unsound: a rerouted link can run outside the
            /// input's band while the band keeps area next to it (post-review B1, the wedge self-check).
            /// </summary>
            bool[] ExcusedLinks((Pt a, Pt b) source, out bool[] rerouted)
            {
                var image = full.Images[source];
                var own = inputOnly.Images[source];
                var excused = new bool[Math.Max(0, image.Count - 1)];
                rerouted = new bool[excused.Length];
                var index = new Dictionary<(BigInteger x, BigInteger y), int>();
                for (int k = 0; k < own.Count; ++k) index[own[k]] = k;
                // shared pixels in order (greedy on increasing index in the input's image; both ends are always kept)
                var at = new List<(int full, int own)>();
                for (int k = 0; k < image.Count; ++k)
                    if (index.TryGetValue(image[k], out int j) && (at.Count == 0 || j > at[at.Count - 1].own)) at.Add((k, j));
                for (int s = 0; s + 1 < at.Count; ++s) {
                    int f0 = at[s].full, f1 = at[s + 1].full, o0 = at[s].own, o1 = at[s + 1].own;
                    bool ownCollapsed = true;
                    for (int j = o0; j < o1 && ownCollapsed; ++j) ownCollapsed = LinkCollapsed(inputOnly, own[j], own[j + 1]);
                    if (!ownCollapsed) continue;
                    bool same = f1 - f0 == o1 - o0;
                    for (int k = f0; k < f1 && same; ++k) same = image[k].Equals(own[o0 + k - f0]);
                    for (int k = f0; k < f1; ++k)
                        if (LinkCollapsed(full, image[k], image[k + 1])) { excused[k] = true; rerouted[k] = !same; }
                }
                return excused;
            }

            /// <summary> point p's pixel in state st: every link there collapsed, or, with no link, its centre not inside </summary>
            bool PixelCollapsed(SnapState st, Pt p)
            {
                var n = PixelOf(p, snapPixel);
                // with links there, all collapsed implies the centre is not inside (images meet only at pixel centres, so an interior
                // around the centre would lie beside a link's middle): the centre is classified only where no link is (a corridor with a
                // ring vertex every pixel: 4000 exact classifications against the 4000 coinciding edges took 35 s)
                if (st.LinksAt.TryGetValue(n, out var at)) return at.All(q => LinkCollapsed(st, n, q));
                return st.Domain.Classify(Centre(n, snapPixel)) != Where.Inside;
            }

            /// <summary>
            /// O-H5 for an unused input point p: its pixel collapsed in the full rounding and in the input's own (rule (ii')): every link
            /// of an image at that pixel is collapsed, or, with no link there, the pixel's centre is not inside the snapped domain
            /// </summary>
            bool OnCollapsedPoint(Pt p)
            {
                if (!(input.SnapTolerance > 0)) return false;
                Snap();
                if (!PixelCollapsed(inputOnly, p)) return false;
                if (!PixelCollapsed(full, p)) {
                    // the lobe (2026-11-10, as in ChainThroughContactedPixels): the input's rounding collapses the point's pixel (a spike's tip),
                    // and output vertices rerouted the links at it so that the full image has area beside one of them; excused when every link
                    // at the pixel in the full image ends in a neighbouring pixel, i.e. the lobe lies within the pixel tube
                    var tip = PixelOf(p, snapPixel);
                    if (!full.LinksAt.TryGetValue(tip, out var tipLinks) || tipLinks.Count == 0) return false;
                    if (!LinksStayAdjacent(tip, tipLinks)) return false;
                    ++r.LobeExcuses;
                    return true;
                }
                // (iii): the links at its pixel differ between the roundings: output vertices rerouted them
                var n = PixelOf(p, snapPixel);
                full.LinksAt.TryGetValue(n, out var withAll); inputOnly.LinksAt.TryGetValue(n, out var withInput);
                if (!(withAll ?? new List<(BigInteger x, BigInteger y)>()).OrderBy(q => q.x).ThenBy(q => q.y).SequenceEqual((withInput ?? new List<(BigInteger x, BigInteger y)>()).OrderBy(q => q.x).ThenBy(q => q.y)))
                    ++r.CollapsedViaRefinement;
                return true;
            }

            static bool InEnvelopeOf(Pt a, Pt b, Pt p, Q h)
            {
                Q x0 = p.Qx - h, x1 = p.Qx + h, y0 = p.Qy - h, y1 = p.Qy + h;
                if (Q.Max(a.Qx, b.Qx) < x0 || Q.Min(a.Qx, b.Qx) > x1 || Q.Max(a.Qy, b.Qy) < y0 || Q.Min(a.Qy, b.Qy) > y1) return false;
                int pos = 0, neg = 0;
                foreach (var corner in new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) }) {
                    int or = Exact.Orient(a, b, corner);
                    if (or > 0) ++pos; else if (or < 0) ++neg;
                }
                return pos < 4 && neg < 4;
            }

            /// <summary> D14: some input constraint meets the closed square of half-width h around p (p lies in its envelope) </summary>
            bool InEnvelope(Pt p, Q h)
            {
                Q x0 = p.Qx - h, x1 = p.Qx + h, y0 = p.Qy - h, y1 = p.Qy + h;
                foreach (var s in sources) {
                    if (Q.Max(s.a.Qx, s.b.Qx) < x0 || Q.Min(s.a.Qx, s.b.Qx) > x1 || Q.Max(s.a.Qy, s.b.Qy) < y0 || Q.Min(s.a.Qy, s.b.Qy) > y1) continue;
                    int pos = 0, neg = 0;
                    foreach (var corner in new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) }) {
                        int or = Exact.Orient(s.a, s.b, corner);
                        if (or > 0) ++pos; else if (or < 0) ++neg;
                    }
                    if (pos < 4 && neg < 4) return true;
                }
                return false;
            }

            /// <summary> per piece of an input constraint: its ends and the chain of output vertices found for it (Segments) </summary>
            readonly List<(Pt from, Pt to, List<int> path)> chainPaths = new List<(Pt, Pt, List<int>)>();

            /// <summary>
            /// With an explicit tolerance (D14) a hole seed that snapping swept may be dropped: it lies in the region between a piece of an
            /// input constraint and its chain (the polygon of the piece and the chain back), or on its boundary, so constraint geometry
            /// moved across it, eg a contour that collapsed around it (clean-room answer 6; F-17/b). A seed merely near moved geometry is
            /// still obeyed (second review of #277). Exact, by the winding number.
            /// </summary>
            bool SweptBySnapping(Pt seed)
            {
                if (!(input.SnapTolerance > 0) || !o.IdentityRules) return false;
                foreach (var (from, to, path) in chainPaths) {
                    var poly = new List<Pt> { from, to };
                    for (int k = path.Count - 1; k >= 0; --k) poly.Add(v[path[k]]);
                    if (poly.Skip(2).All(p => Exact.Orient(from, to, p) == 0)) continue;   // the chain on its piece: nothing swept
                    if (seed.Qx < poly.Min(p => p.Qx) || seed.Qx > poly.Max(p => p.Qx) || seed.Qy < poly.Min(p => p.Qy) || seed.Qy > poly.Max(p => p.Qy)) continue;
                    int winding = 0;
                    for (int k = 0; k < poly.Count; ++k) {
                        Pt a = poly[k], b = poly[(k + 1) % poly.Count];
                        int or = Exact.Orient(a, b, seed);
                        if (or == 0 && seed.Qx >= Q.Min(a.Qx, b.Qx) && seed.Qx <= Q.Max(a.Qx, b.Qx) && seed.Qy >= Q.Min(a.Qy, b.Qy) && seed.Qy <= Q.Max(a.Qy, b.Qy)) return true;
                        if (a.Qy <= seed.Qy) { if (b.Qy > seed.Qy && or > 0) ++winding; }
                        else if (b.Qy <= seed.Qy && or < 0) --winding;
                    }
                    if (winding != 0) return true;
                }
                return false;
            }

            void Orientation()
            {
                r.Area2 = Q.Zero;
                if (Fast) { OrientationFast(); return; }
                foreach (var t in o.Triangles) {
                    if (t[0] == t[1] || t[1] == t[2] || t[0] == t[2]) { r.Add(Violation.NotPositive, "triangle with a repeated vertex"); continue; }
                    if (Exact.Orient(v[t[0]], v[t[1]], v[t[2]]) <= 0) { r.Add(Violation.NotPositive, "triangle " + t[0] + " " + t[1] + " " + t[2] + " is not counter-clockwise"); continue; }
                    r.Area2 += Exact.Area2(v[t[0]], v[t[1]], v[t[2]]);
                    var q = Exact.MinAngleSin2(v[t[0]], v[t[1]], v[t[2]]);
                    if (r.MinSin2 == null || q < r.MinSin2.Value) r.MinSin2 = q;
                }
            }

            /// <summary>
            /// The same results as the exact loop: the orientation by the filtered predicate; the area as the sum of the cross products
            /// x_a y_b - x_b y_a over the half-edges of the positive triangles (exactly the sum of their twice areas: each triangle's
            /// three terms add up to it), with the pairs of opposite half-edges left out, which cancel exactly; the smallest angle by
            /// the exact value of the triangles whose filtered value can be the smallest.
            /// </summary>
            void OrientationFast()
            {
                var positive = new List<int>(o.Triangles.Length);
                for (int i = 0; i < o.Triangles.Length; ++i) {
                    var t = o.Triangles[i];
                    if (t[0] == t[1] || t[1] == t[2] || t[0] == t[2]) { r.Add(Violation.NotPositive, "triangle with a repeated vertex"); continue; }
                    if (OrientV(t[0], t[1], t[2]) <= 0) { r.Add(Violation.NotPositive, "triangle " + t[0] + " " + t[1] + " " + t[2] + " is not counter-clockwise"); continue; }
                    positive.Add(i);
                }
                // the area: half-edges counted with their direction; a and b swapped cancels a and b
                var net = new Dictionary<long, int>(EdgeKeys.Instance);
                foreach (int i in positive) {
                    var t = o.Triangles[i];
                    for (int k = 0; k < 3; ++k) {
                        int a = t[k], b = t[(k + 1) % 3];
                        long key = Key(a, b);
                        int dir = a < b ? 1 : -1;
                        net.TryGetValue(key, out int n); net[key] = n + dir;
                    }
                }
                // the vertices are dyadic rationals X / W (W a power of two): over the largest W every coordinate is an integer, and the
                // sum is one integer over W^2, made a rational once (adding rationals one by one normalized each sum)
                var terms = net.Where(pair => pair.Value != 0).ToList();
                if (terms.Count > 0) {
                    BigInteger w = BigInteger.One;
                    foreach (var pair in terms) { w = BigInteger.Max(w, v[KeyA(pair.Key)].W); w = BigInteger.Max(w, v[KeyB(pair.Key)].W); }
                    BigInteger sum = BigInteger.Zero;
                    foreach (var pair in terms) {
                        Pt pa = v[KeyA(pair.Key)], pb = v[KeyB(pair.Key)];   // a < b
                        BigInteger fa = w / pa.W, fb = w / pb.W;             // exact: powers of two
                        sum += pair.Value * ((pa.X * fa) * (pb.Y * fb) - (pb.X * fb) * (pa.Y * fa));
                    }
                    r.Area2 = new Q(sum, w * w);
                }
                // the smallest angle: sin^2 in doubles with a relative error bound, then exact for the candidates
                double upper = double.PositiveInfinity;
                var lo = new double[positive.Count]; var hi = new double[positive.Count];
                for (int j = 0; j < positive.Count; ++j) {
                    var t = o.Triangles[positive[j]];
                    if (!MinSin2Bounds(t[0], t[1], t[2], out lo[j], out hi[j])) { lo[j] = double.NegativeInfinity; hi[j] = double.PositiveInfinity; }
                    upper = Math.Min(upper, hi[j]);
                }
                for (int j = 0; j < positive.Count; ++j) {
                    if (lo[j] > upper) continue;
                    var t = o.Triangles[positive[j]];
                    var q = Exact.MinAngleSin2(v[t[0]], v[t[1]], v[t[2]]);
                    if (r.MinSin2 == null || q < r.MinSin2.Value) r.MinSin2 = q;
                }
            }

            // ------------------------------------------------------------ filtered predicates on output vertices
            // On the doubles of the output (when the producer gives no exact vertices, the vertices are those doubles exactly): the
            // value in doubles and a bound on its rounding error; the exact test when the bound does not decide. Independent of the
            // mesher's predicates. Bounds (unit roundoff u = 2^-53), each with a margin of about 3:
            //   orientation (bx-ax)(cy-ay) - (by-ay)(cx-ax): |error| <= (3u + 16u^2) (|l| + |r|)  [Shewchuk 1997, ccwerrboundA];
            //     used: 1e-15 (|l| + |r|)
            //   in-circle as in InCircle below: (10u + 96u^2) * permanent  [Shewchuk 1997, iccerrboundA]; used: 1.2e-14 * permanent
            //   sin^2 of the smallest angle = det^2 / (l1 l2): relative error at most 2 e_det + 2 e_len + 3u, with e_det the orientation
            //     bound over |det| and e_len <= 3u + u^2 for a squared length of rounded differences; used: 2 e_det + 1e-14
            // Filters work only where nothing underflows or overflows: magnitudes between 1e-200 and 1e200, else exact.

            bool Fast => !e.Reference && o.ExactVertices == null;

            static double ToD(Q q) => q.Bounds(out double lo, out double hi) ? (lo + hi) / 2 : double.NaN;

            int OrientV(int a, int b, int c)
            {
                if (Fast) {
                    double ax = o.VX[a], ay = o.VY[a];
                    double l = (o.VX[b] - ax) * (o.VY[c] - ay), rr = (o.VY[b] - ay) * (o.VX[c] - ax);
                    double det = l - rr, sum = Math.Abs(l) + Math.Abs(rr), bound = 1e-15 * sum;
                    if (sum > 1e-200 && sum < 1e200 && Math.Abs(det) > bound) return det > 0 ? 1 : -1;
                }
                return Exact.Orient(v[a], v[b], v[c]);
            }

            bool ProperCrossV(int a, int b, int c, int d) =>
                OrientV(a, b, c) * OrientV(a, b, d) < 0 && OrientV(c, d, a) * OrientV(c, d, b) < 0;

            /// <summary> an interval around sin^2 of the smallest angle of triangle a b c; false if the filter cannot give one </summary>
            bool MinSin2Bounds(int a, int b, int c, out double lo, out double hi)
            {
                lo = hi = 0;
                if (!Fast) return false;
                double ax = o.VX[a], ay = o.VY[a], bx = o.VX[b], by = o.VY[b], cx = o.VX[c], cy = o.VY[c];
                double l = (bx - ax) * (cy - ay), rr = (by - ay) * (cx - ax);
                double det = l - rr, sum = Math.Abs(l) + Math.Abs(rr);
                if (!(sum > 1e-200 && sum < 1e200) || !(Math.Abs(det) > 1e-15 * sum)) return false;
                double ab = (bx - ax) * (bx - ax) + (by - ay) * (by - ay), bc = (cx - bx) * (cx - bx) + (cy - by) * (cy - by), ca = (ax - cx) * (ax - cx) + (ay - cy) * (ay - cy);
                double cross2 = det * det, abca = ab * ca, abbc = ab * bc, bcca = bc * ca;
                // every intermediate normal, far from both ends: an overflowing product gave s = 0, a subnormal cross2 a wrong interval
                // (found by the bounds review)
                bool Normal(double x) => x > 1e-200 && x < 1e200;
                if (!(Normal(ab) && Normal(bc) && Normal(ca) && Normal(cross2) && Normal(abca) && Normal(abbc) && Normal(bcca))) return false;
                double s = Math.Min(cross2 / abca, Math.Min(cross2 / abbc, cross2 / bcca));
                if (double.IsNaN(s) || double.IsInfinity(s)) return false;
                double rel = 2 * (1e-15 * sum / Math.Abs(det)) + 1e-14;
                lo = s * (1 - rel); hi = s * (1 + rel);
                return true;
            }

            void EdgesAndNeighbours()
            {
                edges = new Dictionary<long, List<(int, int, int, int)>>(2 * o.Triangles.Length + 16, EdgeKeys.Instance);
                for (int i = 0; i < o.Triangles.Length; ++i) {
                    var t = o.Triangles[i];
                    for (int k = 0; k < 3; ++k) {
                        int a = t[(k + 1) % 3], b = t[(k + 2) % 3];
                        long key = Key(a, b);
                        if (!edges.TryGetValue(key, out var uses)) edges.Add(key, uses = new List<(int, int, int, int)>(2));
                        uses.Add((i, k, a, b));
                    }
                }
                foreach (var pair in edges) {
                    var uses = pair.Value;
                    if (uses.Count > 2) r.Add(Violation.EdgeRepeated, "edge " + KeyA(pair.Key) + "-" + KeyB(pair.Key) + " in " + uses.Count + " triangles");
                    else if (uses.Count == 2 && uses[0].a == uses[1].a) r.Add(Violation.EdgeRepeated, "edge " + KeyA(pair.Key) + "-" + KeyB(pair.Key) + " runs the same way in both triangles");
                }
                if (o.Neighbours == null) {
                    if (o.IdentityRules && o.NeighboursReported && e.Milestone != Milestone.M1) r.Add(Violation.MissingReport, "no neighbours (spec R5)");
                    return;
                }
                if (o.Neighbours.Length != o.Triangles.Length) { r.Add(Violation.NeighbourWrong, "one neighbour triple per triangle"); return; }
                for (int i = 0; i < o.Triangles.Length; ++i) {
                    var t = o.Triangles[i];
                    for (int k = 0; k < 3; ++k) {
                        var uses = edges[Key(t[(k + 1) % 3], t[(k + 2) % 3])];
                        int expected = -1;
                        foreach (var u in uses) if (u.tri != i) expected = u.tri;
                        if (o.Neighbours[i] == null || o.Neighbours[i].Length != 3 || o.Neighbours[i][k] != expected) {
                            r.Add(Violation.NeighbourWrong, "triangle " + i + " corner " + k + ": expected neighbour " + expected);
                            return;
                        }
                    }
                }
            }

            /// <summary>Ranks of the coordinates: all box comparisons below are exact and cost one integer comparison.</summary>
            static int[] Ranks(double[] keys)
            {
                var order = Enumerable.Range(0, keys.Length).ToArray();
                Array.Sort(order, (a, b) => keys[a] != keys[b] ? keys[a].CompareTo(keys[b]) : a.CompareTo(b));
                var ranks = new int[keys.Length];
                int rank = 0;
                for (int i = 0; i < order.Length; ++i) {
                    if (i > 0 && keys[order[i]] != keys[order[i - 1]]) ++rank;
                    ranks[order[i]] = rank;
                }
                return ranks;
            }

            static int[] Ranks(Pt[] p, Func<Pt, Q> axis)
            {
                var order = Enumerable.Range(0, p.Length).ToArray();
                var keys = p.Select(axis).ToArray();
                Array.Sort(order, (a, b) => keys[a].CompareTo(keys[b]));
                var ranks = new int[p.Length];
                int rank = 0;
                for (int i = 0; i < order.Length; ++i) {
                    if (i > 0 && keys[order[i]] != keys[order[i - 1]]) ++rank;
                    ranks[order[i]] = rank;
                }
                return ranks;
            }

            /// <summary>Calls back for each pair of boxes whose closed ranges overlap (<see cref="BoxPairs.Overlaps"/>); in check mode the
            /// pairs are compared with the plain sweep.</summary>
            static void OverlapPairs(int[][] boxesA, int[][] boxesB, bool same, Action<int, int> hit)
            {
                if (CheckFastPathForTests) {
                    var fast = new HashSet<(int, int)>(); var swept = new HashSet<(int, int)>();
                    BoxPairs.Overlaps(boxesA, boxesB, same, (i, j) => { if (!fast.Add(same ? (Math.Min(i, j), Math.Max(i, j)) : (i, j))) FastPathDiffers("a box pair found twice"); });
                    BoxPairs.OverlapsBySweep(boxesA, boxesB, same, (i, j) => swept.Add(same ? (Math.Min(i, j), Math.Max(i, j)) : (i, j)));
                    if (!fast.SetEquals(swept)) FastPathDiffers("the overlapping box pairs");
                }
                BoxPairs.Overlaps(boxesA, boxesB, same, hit);
            }

            /// <summary>No used vertex touches a triangle other than at its own corner; no two edges cross.</summary>
            void Conformity()
            {
                // (the doubles are the exact vertices on the fast path: the same ranks)
                int[] rx = Fast ? Ranks(o.VX) : Ranks(v, p => p.Qx), ry = Fast ? Ranks(o.VY) : Ranks(v, p => p.Qy);
                var triBoxes = o.Triangles.Select(t => new[] {
                    t.Min(i => rx[i]), t.Max(i => rx[i]), t.Min(i => ry[i]), t.Max(i => ry[i]) }).ToArray();
                var used = new HashSet<int>(o.Triangles.SelectMany(t => t)).OrderBy(i => i).ToArray();
                var vertexBoxes = used.Select(i => new[] { rx[i], rx[i], ry[i], ry[i] }).ToArray();
                // (the pairs come in no particular order: the violations are collected and the first 8 in index order reported)
                List<(int ti, int id)> SweptTouches()
                {
                    var swept = new List<(int ti, int id)>();
                    OverlapPairs(triBoxes, vertexBoxes, false, (ti, vi) => {
                        var t = o.Triangles[ti];
                        int id = used[vi];
                        if (id == t[0] || id == t[1] || id == t[2]) return;
                        if (OrientV(t[0], t[1], id) >= 0 && OrientV(t[1], t[2], id) >= 0 && OrientV(t[2], t[0], id) >= 0) swept.Add((ti, id));
                    });
                    swept.Sort();
                    return swept;
                }
                List<(int ti, int id)> touching;
                if (UseConformityIndex(Fast, PointIndex, "strtree")) {
                    touching = new List<(int ti, int id)>(new StrtreePointSearch(o).FindTouches());
                    touching.Sort();
                    // (the STRtree's pruning against the sweep, pair for pair: the oracle's tests reach it through the fast path checks)
                    if (CheckFastPathForTests && !touching.SequenceEqual(SweptTouches())) FastPathDiffers("the vertices touching a triangle (STRtree)");
                } else touching = SweptTouches();
                foreach (var (ti, id) in touching.Take(8)) {
                    var t = o.Triangles[ti];
                    r.Add(Violation.VertexInTriangle, "vertex " + id + " " + v[id] + " touches triangle " + t[0] + " " + t[1] + " " + t[2]);
                }
                Lap("conformity: point-triangle");
                var keys = edges.Keys.ToArray();
                var edgeBoxes = keys.Select(k => new[] {
                    Math.Min(rx[KeyA(k)], rx[KeyB(k)]), Math.Max(rx[KeyA(k)], rx[KeyB(k)]),
                    Math.Min(ry[KeyA(k)], ry[KeyB(k)]), Math.Max(ry[KeyA(k)], ry[KeyB(k)]) }).ToArray();
                List<(long, long)> SweptCrossings()
                {
                    var swept = new List<(long, long)>();
                    OverlapPairs(edgeBoxes, edgeBoxes, true, (i, j) => {
                        int a = KeyA(keys[i]), b = KeyB(keys[i]), c = KeyA(keys[j]), d = KeyB(keys[j]);
                        if (a == c || a == d || b == c || b == d) return;
                        if (ProperCrossV(a, b, c, d)) swept.Add((Math.Min(keys[i], keys[j]), Math.Max(keys[i], keys[j])));
                    });
                    swept.Sort();
                    return swept;
                }
                List<(long, long)> crossing;
                if (UseConformityIndex(Fast, EdgeIndex, "strtree-hulls")) {
                    crossing = new List<(long, long)>(new StrtreeEdgeSearch(o, keys).FindCrossings());
                    crossing.Sort();
                    if (CheckFastPathForTests && !crossing.SequenceEqual(SweptCrossings())) FastPathDiffers("the crossing edge pairs (STRtree hulls)");
                } else crossing = SweptCrossings();
                foreach (var (k1, k2) in crossing.Take(8))
                    r.Add(Violation.EdgesCross, "edges " + KeyA(k1) + "-" + KeyB(k1) + " and " + KeyA(k2) + "-" + KeyB(k2) + " cross");
                Lap("conformity: edge-crossings");
                // every used vertex that is a corner of two triangles with the same coordinates would pass the tests above
                if (Fast) {
                    // the doubles are the exact vertices: equal doubles, equal points (0.0 and -0.0 made one)
                    var seenD = new Dictionary<(double, double), int>();
                    foreach (int id in used) {
                        var key = (o.VX[id] == 0 ? 0.0 : o.VX[id], o.VY[id] == 0 ? 0.0 : o.VY[id]);
                        if (seenD.TryGetValue(key, out int other)) r.Add(Violation.VertexInTriangle, "vertices " + other + " and " + id + " coincide");
                        else seenD.Add(key, id);
                    }
                } else {
                    var seen = new Dictionary<Pt, int>();
                    foreach (int id in used) {
                        if (seen.TryGetValue(v[id], out int other)) r.Add(Violation.VertexInTriangle, "vertices " + other + " and " + id + " coincide");
                        else seen.Add(v[id], id);
                    }
                }
                // the unused vertices: an input point that belongs to the domain must be used; a constructed vertex must be used
                var usedSet = new HashSet<int>(used);
                for (int i = 0; i < input.Count; ++i) {
                    int m = map[i];
                    bool isUsed = m >= 0 && usedSet.Contains(m);
                    if (isUsed) continue;
                    if (!InClosure(inPts[i]) || CollapsedHullTip(i, usedSet)) continue;
                    if (OnCollapsedPoint(inPts[i])) { ++r.CollapsedPoints; continue; }
                    r.Add(Violation.VertexUnused, "input " + i + " " + inPts[i] + " is in the domain but in no triangle");
                }
                if (o.IdentityRules)
                    for (int i = input.Count; i < v.Length; ++i)
                        if (!usedSet.Contains(i)) r.Add(Violation.VertexUnused, "constructed vertex " + i + " is in no triangle");
                // F-36: no vertex outside the domain, by more than the tube (on the fast path the ring edges whose box, widened by the
                // tube, misses the vertex are left out of the exact distance test: they are farther than the tube)
                var ringEdges = domain.Edges().ToList();
                var ringBox = new double[4 * ringEdges.Count];
                double reach = double.NaN;
                if (tube2.Bounds(out _, out double t2hi)) reach = Math.Sqrt(t2hi) * (1 + 1e-12) + 1e-300;
                for (int g = 0; g < ringEdges.Count; ++g) SegmentBox(ringEdges[g].a, ringEdges[g].b, reach, ringBox, 4 * g);
                foreach (int id in used) {
                    if (domain.Classify(v[id]) != Where.Outside) continue;
                    bool near = false;
                    for (int g = 0; g < ringEdges.Count && !near; ++g) {
                        if (Fast && vKnown[id] && !double.IsNaN(ringBox[4 * g])
                            && (vxh[id] < ringBox[4 * g] || vxl[id] > ringBox[4 * g + 1] || vyh[id] < ringBox[4 * g + 2] || vyl[id] > ringBox[4 * g + 3])) continue;
                        near = Exact.Dist2ToSegment(ringEdges[g].a, ringEdges[g].b, v[id]) <= tube2;
                    }
                    if (!near) r.Add(Violation.TriangleOutsideDomain, "vertex " + id + " " + v[id] + " is outside the domain");
                }
            }

            // A floating-point filter in front of the exact distance tests: each vertex as an interval of doubles around its exact
            // coordinates, each source as its bounding box widened by the tube. A vertex outside a box is farther than the tube from
            // that source, so the exact test can be skipped; everything else still goes to the exact test.
            double[] vxl, vxh, vyl, vyh;
            bool[] vKnown;
            double[] sBox;   // per source: x0, x1, y0, y1 (NaN: unknown, no filter)

            void Filters()
            {
                int n = v.Length;
                vxl = new double[n]; vxh = new double[n]; vyl = new double[n]; vyh = new double[n]; vKnown = new bool[n];
                for (int i = 0; i < n; ++i) vKnown[i] = v[i].Bounds(false, out vxl[i], out vxh[i]) && v[i].Bounds(true, out vyl[i], out vyh[i]);
                EnsureSourceBoxes();
                // the ends of each source in doubles where they are doubles exactly (NaN otherwise), and the tube squared from above
                sEnds = new double[4 * sources.Count];
                for (int k = 0; k < sources.Count; ++k) {
                    bool exact = Exactly(sources[k].a, out sEnds[4 * k], out sEnds[4 * k + 1]) & Exactly(sources[k].b, out sEnds[4 * k + 2], out sEnds[4 * k + 3]);
                    if (!exact) sEnds[4 * k] = double.NaN;
                }
                if (tube2.Bounds(out double t2lo, out double t2hi2)) { tube2Lo = t2lo; tube2Hi = t2hi2; } else tube2Lo = tube2Hi = double.NaN;
            }

            double[] sEnds;   // per source: ax, ay, bx, by in doubles (ax NaN: not doubles, no filter)
            double tube2Lo, tube2Hi;   // the tube squared, an interval in doubles around the exact value

            /// <summary> q as a double if it is one exactly (checked by converting back), NaN otherwise </summary>
            static double AsDouble(Q q)
            {
                double c = (double)q.N / (double)q.D;
                return !double.IsNaN(c) && !double.IsInfinity(c) && Q.FromDouble(c) == q ? c : double.NaN;
            }

            static bool Exactly(Pt p, out double x, out double y)
            {
                x = AsDouble(p.Qx); y = AsDouble(p.Qy);
                return !double.IsNaN(x) && !double.IsNaN(y);
            }

            /// <summary>
            /// Output vertex i against the tube of source k in doubles: -1 certainly farther, +1 certainly within, 0 undecided (the exact
            /// test decides). Bounds (u = 2^-53, margins as in OrientV):
            ///   cross = (bx-ax)(py-ay) - (by-ay)(px-ax): |error| &lt;= (3u + 16u^2)(|l| + |r|) [ccwerrboundA]; used 1e-15 (|l| + |r|);
            ///   dot = (bx-ax)(px-ax) + (by-ay)(py-ay): the same form with a sum, so the same bound; used 1e-15 (|l'| + |r'|);
            ///   uu = |b-a|^2: relative error below (1 + u)^4 - 1, about 4u (differences, squares, sum); used 1e-15.
            /// The distance to the line, cross^2 / uu, bounds the distance to the segment from below: far when (|cross| - e)^2 / uu,
            /// rounded down, exceeds the tube squared rounded up. Within when 0 &lt;= dot &lt;= uu certainly (the foot lies on the segment,
            /// so both distances agree) and (|cross| + e)^2 / uu, rounded up, is below the tube squared rounded down. Each rounding of
            /// the last steps is covered by a factor 1 +- 1e-15. Fast path and normal magnitudes only.
            /// </summary>
            int SourceTubeFilter(int k, int i)
            {
                if (!Fast || double.IsNaN(sEnds[4 * k]) || double.IsNaN(tube2Lo)) return 0;
                return TubeFilter(sEnds[4 * k], sEnds[4 * k + 1], sEnds[4 * k + 2], sEnds[4 * k + 3], o.VX[i], o.VY[i], tube2Lo, tube2Hi);
            }

            /// <summary> the filter of SourceTubeFilter on doubles: segment a-b, point p, the tube squared in [tube2Lo, tube2Hi] </summary>
            internal static int TubeFilter(double ax, double ay, double bx, double by, double px, double py, double tube2Lo, double tube2Hi)
            {
                double ux = bx - ax, uy = by - ay, vx = px - ax, vy = py - ay;
                double l = ux * vy, rr = uy * vx;
                double sum = Math.Abs(l) + Math.Abs(rr), err = 1e-15 * sum, cross = Math.Abs(l - rr);
                double uu = ux * ux + uy * uy;
                bool Normal(double x) => x > 1e-200 && x < 1e200;
                if (!Normal(uu) || !(sum < 1e200) || !(tube2Hi > 1e-200 && tube2Hi < 1e200)) return 0;
                double lower = cross - err;
                if (lower > 0 && Normal(lower) && Normal(lower * lower) && lower * lower / (uu * (1 + 1e-15)) * (1 - 1e-15) > tube2Hi) return -1;
                double upper = cross + err;
                double d1 = ux * vx, d2 = uy * vy, dot = d1 + d2, dotSum = Math.Abs(d1) + Math.Abs(d2), dotErr = 1e-15 * dotSum;
                // a product that underflowed, or a subnormal square divided by a small uu, would break the relative bounds: undecided.
                // Except a cross product that is zero exactly, through a zero factor in each product (a point on an axis-parallel
                // source's line): then the distance to the line is zero
                bool exactlyOnLine = (ux == 0 || vy == 0) && (uy == 0 || vx == 0);
                double upper2 = exactlyOnLine ? 0 : upper * upper;
                if (!Normal(dotSum) || !(exactlyOnLine || Normal(upper2))) return 0;
                bool onFoot = dot - dotErr >= 0 && dot + dotErr <= uu * (1 - 1e-15);
                if (onFoot && upper2 / (uu * (1 - 1e-15)) * (1 + 1e-15) < tube2Lo) return 1;
                return 0;
            }

            /// <summary> vertex i lies within the tube of source k: exactly, unless the filters decide it for certain </summary>
            bool NearSource(int k, int i)
            {
                if (!MayBeNear(k, i)) return false;
                int f = SourceTubeFilter(k, i);
                return f != 0 ? f > 0 : WithinTube(sources[k], v[i]);
            }

            /// <summary> the box of segment a-b in doubles, around the exact one, widened by reach; NaN where it is unknown </summary>
            internal static void SegmentBox(Pt a, Pt b, double reach, double[] box, int at)
            {
                double axl = 0, axh = 0, ayl = 0, ayh = 0, bxl = 0, bxh = 0, byl = 0, byh = 0;
                bool known = !double.IsNaN(reach) && a.Bounds(false, out axl, out axh) && a.Bounds(true, out ayl, out ayh)
                    && b.Bounds(false, out bxl, out bxh) && b.Bounds(true, out byl, out byh);
                box[at] = Math.Min(axl, bxl) - reach; box[at + 1] = Math.Max(axh, bxh) + reach;
                box[at + 2] = Math.Min(ayl, byl) - reach; box[at + 3] = Math.Max(ayh, byh) + reach;
                for (int j = 0; j < 4; ++j) if (!known || double.IsInfinity(box[at + j])) known = false;
                if (!known) for (int j = 0; j < 4; ++j) box[at + j] = double.NaN;
            }

            /// <summary> false only if vertex i is certainly farther than the tube from source k </summary>
            bool MayBeNear(int k, int i)
            {
                if (e.Reference || !vKnown[i] || double.IsNaN(sBox[4 * k])) return true;
                return !(vxh[i] < sBox[4 * k] || vxl[i] > sBox[4 * k + 1] || vyh[i] < sBox[4 * k + 2] || vyl[i] > sBox[4 * k + 3]);
            }

            /// <summary>
            /// A uniform grid over boxes in doubles (fast path only): Query returns, in ascending order, every box that may meet the
            /// query box, among them every box that does (the boxes of the cells it covers, and the unknown ones). A box not returned
            /// is apart from the query box by more than the query's widening on some axis.
            /// </summary>
            sealed class BoxGrid
            {
                readonly double x0, y0, cell; readonly int nx, ny;
                readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
                readonly List<int> unknown = new List<int>();
                readonly int[] stamp; int round;
                public BoxGrid(double[] box, int count)
                {
                    stamp = new int[count];
                    double xl = double.PositiveInfinity, xh = double.NegativeInfinity, yl = double.PositiveInfinity, yh = double.NegativeInfinity;
                    for (int i = 0; i < count; ++i) {
                        if (double.IsNaN(box[4 * i])) continue;
                        xl = Math.Min(xl, box[4 * i]); xh = Math.Max(xh, box[4 * i + 1]); yl = Math.Min(yl, box[4 * i + 2]); yh = Math.Max(yh, box[4 * i + 3]);
                    }
                    x0 = xl; y0 = yl;
                    double extent = Math.Max(xh - xl, yh - yl);
                    cell = extent > 0 && !double.IsInfinity(extent) ? extent / Math.Max(1, Math.Sqrt(count)) : 1;
                    nx = (int)Math.Min(1 << 14, Math.Max(1, Math.Ceiling((xh - xl) / cell) + 1));
                    ny = (int)Math.Min(1 << 14, Math.Max(1, Math.Ceiling((yh - yl) / cell) + 1));
                    for (int i = 0; i < count; ++i) {
                        if (double.IsNaN(box[4 * i])) { unknown.Add(i); continue; }
                        Cells(box[4 * i], box[4 * i + 1], box[4 * i + 2], box[4 * i + 3], out int i0, out int i1, out int j0, out int j1);
                        // (a box over many cells is returned always instead: long diagonal edges would fill the grid quadratically; review)
                        if ((long)(i1 - i0 + 1) * (j1 - j0 + 1) > 256) { unknown.Add(i); continue; }
                        for (int ci = i0; ci <= i1; ++ci)
                            for (int cj = j0; cj <= j1; ++cj) {
                                long key = (long)ci * ny + cj;
                                if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<int>());
                                list.Add(i);
                            }
                    }
                }
                void Cells(double xl, double xh, double yl, double yh, out int i0, out int i1, out int j0, out int j1)
                {
                    int Clamp(double v, int n) => double.IsNaN(v) ? 0 : (int)Math.Max(0, Math.Min(n - 1, Math.Floor(v)));
                    i0 = Clamp((xl - x0) / cell, nx); i1 = Clamp((xh - x0) / cell, nx);
                    j0 = Clamp((yl - y0) / cell, ny); j1 = Clamp((yh - y0) / cell, ny);
                }
                /// <summary> the boxes that may meet [xl, xh] x [yl, yh] (with the box's own coordinates outside the grid clamped to its border cells) </summary>
                /// <summary> check mode: every box of `box` that meets the query box (or is unknown) is in `result`, the grid's answer
                /// (Oracle.CheckFastPathForTests: the grid's superset property itself, independent of the exact test after it) </summary>
                public static void CheckSuperset(double[] box, int count, double xl, double xh, double yl, double yh, List<int> result, string what)
                {
                    var got = new HashSet<int>(result);
                    for (int i = 0; i < count; ++i) {
                        bool meets = double.IsNaN(box[4 * i]) || !(box[4 * i + 1] < xl || box[4 * i] > xh || box[4 * i + 3] < yl || box[4 * i + 2] > yh);
                        if (meets && !got.Contains(i)) FastPathDiffers(what + ": box " + i + " meets the query and is missing from the grid's answer");
                    }
                }

                /// <summary>Coverage only: null means restart with the exact scan.
                /// Counts copied/marked unknowns, cell visits and bucket entries,
                /// so candidate materialization cannot add unbounded work to a
                /// scan that would have stopped early. Never returns a partial list.
                /// The ordinary Query below is unchanged for other callers.</summary>
                public List<int> QueryBounded(double xl, double xh, double yl, double yh, int maxWork, out int work)
                {
                    work = 1;
                    if (unknown.Count > maxWork) return null;
                    var result = new List<int>(unknown);
                    work += unknown.Count;
                    if (work > maxWork) return null;
                    if (++round == int.MaxValue) { Array.Clear(stamp, 0, stamp.Length); round = 1; }
                    foreach (int i in unknown) {
                        if (++work > maxWork) return null;
                        stamp[i] = round;
                    }
                    Cells(xl, xh, yl, yh, out int i0, out int i1, out int j0, out int j1);
                    for (int ci = i0; ci <= i1; ++ci)
                        for (int cj = j0; cj <= j1; ++cj) {
                            if (++work > maxWork) return null;
                            if (cells.TryGetValue((long)ci * ny + cj, out var list))
                                foreach (int i in list) {
                                    if (++work > maxWork) return null;
                                    if (stamp[i] != round) { stamp[i] = round; result.Add(i); }
                                }
                        }
                    result.Sort();
                    return result;
                }

                public List<int> Query(double xl, double xh, double yl, double yh)
                {
                    var result = new List<int>(unknown);
                    if (++round == int.MaxValue) { Array.Clear(stamp, 0, stamp.Length); round = 1; }
                    foreach (int i in unknown) stamp[i] = round;
                    Cells(xl, xh, yl, yh, out int i0, out int i1, out int j0, out int j1);
                    for (int ci = i0; ci <= i1; ++ci)
                        for (int cj = j0; cj <= j1; ++cj)
                            if (cells.TryGetValue((long)ci * ny + cj, out var list))
                                foreach (int i in list) if (stamp[i] != round) { stamp[i] = round; result.Add(i); }
                    result.Sort();
                    return result;
                }
            }

            const int HullSourceIds = 1 << 30;
            List<(Pt a, Pt b, int marker, int sourceId, bool locked)> hullSources;
            double[] hullBox; // per hull source: x0, x1, y0, y1 in doubles, widened by the tube; NaN when not known

            /// <summary> vertex i may lie within the tube of hull source k: their boxes in doubles meet (the exact test decides) </summary>
            bool NearHull(int k, int i)
            {
                if (e.Reference || !vKnown[i] || double.IsNaN(hullBox[4 * k])) return true;
                return !(vxh[i] < hullBox[4 * k] || vxl[i] > hullBox[4 * k + 1] || vyh[i] < hullBox[4 * k + 2] || vyl[i] > hullBox[4 * k + 3]);
            }

            /// <summary>
            /// The hull edges of a hull domain (D13) where the mesher refines (Refining), which constrains the hull: the boundary, so an input segment at a small
            /// angle to one makes a small input angle as two segments would (found with CDT's issue-211). Split at the input points that lie
            /// exactly on them, as the mesher's hull sources are (a hull edge through a collinear input point is two sources there, and
            /// a small angle must lie on one piece; review of the fix). In the merge zone only paired with a segment and without an explicit
            /// tolerance (InMergeZone): the mesher refuses a split point of a hull edge near a segment, not one of a segment near the hull.
            /// Empty otherwise.
            /// </summary>
            List<(Pt a, Pt b, int marker, int sourceId, bool locked)> HullSources()
            {
                if (hullSources != null) return hullSources;
                hullSources = new List<(Pt, Pt, int, int, bool)>();
                if (e.Milestone == Milestone.M3 && domain.IsHull && Refining) {
                    int id = HullSourceIds;
                    foreach (var ring in domain.Outers)
                        for (int i = 0; i < ring.Length; ++i) {
                            Pt a = ring[i], b = ring[(i + 1) % ring.Length];
                            if (a.Equals(b)) continue;
                            var on = new List<(Q t, Pt p)>();
                            bool box = BoxOf(a, b, 0, out double x0, out double x1, out double y0, out double y1);
                            for (int j = 0; j < inPts.Length; ++j) {
                                if (box && (input.X[j] < x0 || input.X[j] > x1 || input.Y[j] < y0 || input.Y[j] > y1)) continue;
                                Pt p = inPts[j];
                                if (p.Equals(a) || p.Equals(b) || !Exact.OnClosedSegment(a, b, p)) continue;
                                on.Add((Exact.Len2(a, p), p));
                            }
                            on.Sort((s, t) => s.t.CompareTo(t.t));
                            Pt from = a;
                            foreach (var (_, p) in on) { if (p.Equals(from)) continue; hullSources.Add((from, p, 0, id++, false)); from = p; }
                            hullSources.Add((from, b, 0, id++, false));
                        }
                }
                hullBox = new double[4 * hullSources.Count];
                double reach = tube2.Bounds(out _, out double t2) && !double.IsInfinity(t2) ? Math.Sqrt(t2) * (1 + 1e-9) + 1e-300 : double.NaN;
                for (int k = 0; k < hullSources.Count; ++k) {
                    if (double.IsNaN(reach) || !BoxOf(hullSources[k].a, hullSources[k].b, reach, out double x0, out double x1, out double y0, out double y1))
                        x0 = x1 = y0 = y1 = double.NaN;
                    hullBox[4 * k] = x0; hullBox[4 * k + 1] = x1; hullBox[4 * k + 2] = y0; hullBox[4 * k + 3] = y1;
                }
                return hullSources;
            }

            /// <summary> the box of segment a-b in doubles (rounded outwards), widened by reach; false if a coordinate has no double bounds </summary>
            static bool BoxOf(Pt a, Pt b, double reach, out double x0, out double x1, out double y0, out double y1)
            {
                x0 = x1 = y0 = y1 = 0;
                if (!a.Bounds(false, out double ax0, out double ax1) || !b.Bounds(false, out double bx0, out double bx1)
                    || !a.Bounds(true, out double ay0, out double ay1) || !b.Bounds(true, out double by0, out double by1)) return false;
                x0 = Math.Min(ax0, bx0) - reach; x1 = Math.Max(ax1, bx1) + reach; y0 = Math.Min(ay0, by0) - reach; y1 = Math.Max(ay1, by1) + reach;
                return true;
            }

            /// <summary>The input constraints: segments and the edges of all rings.</summary>
            void Sources()
            {
                sources = new List<(Pt, Pt, int, int, bool)>();
                int id = 0;
                foreach (var s in input.Segments) {
                    if (!inPts[s.A].Equals(inPts[s.B])) sources.Add((inPts[s.A], inPts[s.B], s.Marker, id, s.Locked));
                    ++id;
                }
                foreach (var ring in input.Rings) {
                    var idx = ring.Vertices;
                    for (int i = 0; i < idx.Length; ++i) {
                        Pt a = inPts[idx[i]], b = inPts[idx[(i + 1) % idx.Length]];
                        if (!a.Equals(b)) sources.Add((a, b, ring.Marker, id, false));
                        ++id;
                    }
                }
                foreach (var region in input.Regions) {
                    if (region.Ring == null) continue;
                    for (int i = 0; i < region.Ring.Length; ++i) {
                        Pt a = inPts[region.Ring[i]], b = inPts[region.Ring[(i + 1) % region.Ring.Length]];
                        if (!a.Equals(b)) sources.Add((a, b, 0, id, false));
                        ++id;
                    }
                }
                if (o.Subsegments != null) {
                    subsegmentSet = new HashSet<long>(o.Subsegments.Select(s => Key(s.A, s.B)), EdgeKeys.Instance);
                    constraintEdges = subsegmentSet;
                } else {
                    if (o.IdentityRules && o.NeighboursReported && e.Milestone != Milestone.M1) r.Add(Violation.MissingReport, "no subsegments (spec R4)");
                    constraintEdges = new HashSet<long>(EdgeKeys.Instance);
                    foreach (var key in edges.Keys)
                        if (SourceCovers(KeyA(key), KeyB(key))) constraintEdges.Add(key);
                }
            }

            /// <summary>O10: with noding off, crossing input segments must end in a failure status, never in a mesh.</summary>
            void Crossings()
            {
                if (input.ResolveCrossings) return;
                for (int i = 0; i < sources.Count; ++i)
                    for (int j = i + 1; j < sources.Count; ++j)
                        if (Exact.ProperCross(sources[i].a, sources[i].b, sources[j].a, sources[j].b)) {
                            r.Add(Violation.StatusInconsistent, "a mesh although input segments cross with noding off");
                            return;
                        }
            }

            /// <summary>O1: M2 has no refinement. A new vertex is only a crossing: on two constraints that are not collinear.</summary>
            void InventedVertices()
            {
                if (e.Milestone != Milestone.M2) return;
                var chainEnds = new HashSet<int>((o.Subsegments ?? new List<SubsegmentOutput>()).SelectMany(q => new[] { q.A, q.B }));
                var inputs = new HashSet<Pt>(inPts);
                // on the fast path a grid over the source boxes (widened by the tube): every vertex scanned every source exactly, 37 342
                // vertices x 1 112 sources on GEOS shapely-829/1 (more than 600 s). Query returns a superset in ascending order and
                // MayBeNear drops only sources certainly farther than the tube, so `near` is the same list as from the full scan
                var sourceGrid = e.Reference ? null : new BoxGrid(sBox, sources.Count);
                foreach (int id in o.Triangles.SelectMany(t => t).Distinct()) {
                    if (inputs.Contains(v[id])) continue;
                    // a surviving input slot is an input point even where snapping moved it (D14); Identity judges how far it moved
                    if (input.SnapTolerance > 0 && o.IdentityRules && id < input.Count && map[id] == id) continue;
                    var candidates = sourceGrid != null && vKnown[id]
                        ? sourceGrid.Query(vxl[id], vxh[id], vyl[id], vyh[id])
                        : Enumerable.Range(0, sources.Count).ToList();
                    var near = candidates.Where(k => NearSource(k, id)).Select(k => sources[k]).ToList();
                    // D14: snap rounding adds the centres of hot pixels (crossings, and the restore step's pixels) as vertices of the chains;
                    // such a vertex is on a chain (an end of a subsegment) and in the envelope of an input constraint, within half a pixel
                    // per axis of it (that it lies on the grid is checked for every vertex, OffGrid)
                    if (input.SnapTolerance > 0 && chainEnds.Contains(id) && InEnvelope(v[id], Q.FromDouble(input.SnapTolerance) / 2)) continue;
                    bool crossing = near.Any(s => near.Any(q => !Collinear(s.a, s.b, q.a, q.b)));
                    if (!crossing) r.Add(Violation.SteinerNotAllowed, "vertex " + id + " " + v[id] + " is no input point and no crossing of constraints");
                }
            }

            static bool Collinear(Pt a, Pt b, Pt c, Pt d) => Exact.Orient(a, b, c) == 0 && Exact.Orient(a, b, d) == 0;

            bool WithinTube((Pt a, Pt b, int marker, int sourceId, bool locked) s, Pt p) => Exact.Dist2ToSegment(s.a, s.b, p) <= tube2;

            /// <summary>
            /// a boundary edge a-b of a hull domain (D13) that skips hull corners: a near one hull edge and b near another (within the
            /// tube), and every hull corner between them, from edge i forward to edge j, within the tube of a-b (the hull runs
            /// counter-clockwise, and so does a-b in its triangle: the corners cut off are those from i forward). A hull corner that
            /// O11 merged into a point within the tolerance, inside or on the hull, leaves the boundary to run past it, cutting the
            /// corner by at most the tube (found with the corpus run: delaunator's robustness1, hull corner 25 merged into its twin 1,
            /// 2.8e-14 away, and the boundary edge 13-15 passing 2.6e-15 from it). A corner farther than the tube is a real corner.
            /// </summary>
            bool CutsMergedHullCorners(Pt pa, Pt pb)
            {
                if (tube2.Sign == 0 || domain.Outers.Count != 1) return false;
                var ring = domain.Outers[0];
                int n = ring.Length;
                for (int i = 0; i < n; ++i) {
                    if (Exact.Dist2ToSegment(ring[i], ring[(i + 1) % n], pa) > tube2) continue;
                    for (int j = 0; j < n; ++j) {
                        if (j == i || Exact.Dist2ToSegment(ring[j], ring[(j + 1) % n], pb) > tube2) continue;
                        // the corners from the end of edge i to the start of edge j
                        bool near = true;
                        for (int k = (i + 1) % n; near; k = (k + 1) % n) {
                            near = Exact.Dist2ToSegment(pa, pb, ring[k]) <= tube2;
                            if (k == j) break;
                        }
                        if (near) return true;
                    }
                }
                return false;
            }

            /// <summary>The union of the triangles is the domain: exact area, boundary edges constrained or on the hull, centroids inside.</summary>
            void Coverage()
            {
                // Geometry moved by sqrt(moved2) at most, and only what really moved counts: the tube permits movement, it grants no area.
                // diff <= 2 * perimeter * sqrt(moved2), compared in squares to stay exact.
                foreach (var seed in input.HoleSeeds) {
                    var p = Pt.FromDoubles(seed[0], seed[1]);
                    if (domain.Classify(p) == Where.Inside) effectiveSeeds.Add(p);
                }
                // The grid (rule O-G, MESHING2D_HULL_GRID.md): with an explicit snap tolerance the output boundary is the input boundary
                // snap-rounded, so a boundary vertex may lie off the input boundary, by at most GridTubePixels pixel (the image of an input
                // edge lies in the Minkowski sum of the edge with a pixel, Guibas and Marimont 1998; D14: drift of half a pixel per axis).
                // Computed from the output mesh and the input domain alone, not from the snapped domain.
                // (not with an effective hole seed: the cell it removes is bounded by constraints that Domain.Edges() does not list)
                bool grid = input.SnapTolerance > 0 && effectiveSeeds.Count == 0;
                Q boundaryTube2 = tube2;
                // (the output boundary may run off the exact input boundary as far as the iterated image does, up to the guard
                // GridImageDriftPixels; what is measured on the vertices and the links is the movement of the area tolerance)
                Q driftTube2 = tube2;
                List<(Pt a, Pt b)> imageLinks = null;
                if (grid) {
                    Q px = Q.FromDouble(input.SnapTolerance);
                    boundaryTube2 = Q.Max(tube2, px * px * (GridTubePixels * GridTubePixels));
                    driftTube2 = Q.Max(tube2, px * px * (GridImageDriftPx * GridImageDriftPx));
                    Snap();
                    double worstImage = 0;
                    double pxD = input.SnapTolerance;
                    foreach (var kv in full.Images) {
                        if (!o.IdentityRules) break;
                        // the guard on the image itself: no node farther than GridImageDriftPx pixels from the exact source. In doubles where
                        // the magnitudes allow (FilterRange; the guard has a margin of 0.9 pixel over the largest drift measured), else exact
                        var sa = PtD(kv.Key.a); var sb = PtD(kv.Key.b);
                        double dx = sb.x - sa.x, dy = sb.y - sa.y;
                        bool inRange = !double.IsNaN(sa.x) && !double.IsNaN(sb.x) && FilterRange(Math.Max(Math.Abs(dx), Math.Abs(dy)))
                            && FilterRange(Math.Max(Math.Abs(sa.x), Math.Abs(sa.y)) + pxD) && FilterRange(pxD);
                        foreach (var n in kv.Value) {
                            double pxDist;
                            if (inRange) {
                                double cx = (double)n.x * pxD, cy = (double)n.y * pxD, len2 = dx * dx + dy * dy;
                                double t = Math.Max(0, Math.Min(1, ((cx - sa.x) * dx + (cy - sa.y) * dy) / len2));
                                double ex = sa.x + t * dx - cx, ey = sa.y + t * dy - cy;
                                pxDist = Math.Sqrt(ex * ex + ey * ey) / pxD;
                            } else {
                                Q q = Exact.Dist2ToSegment(kv.Key.a, kv.Key.b, Centre(n, snapPixel)) / (snapPixel * snapPixel);
                                q.Bounds(out _, out double qhi);
                                pxDist = Math.Sqrt(qhi);
                            }
                            if (double.IsNaN(pxDist)) pxDist = double.PositiveInfinity;   // fail closed
                            worstImage = Math.Max(worstImage, pxDist);
                        }
                    }
                    r.MaxImageDriftPixels = worstImage;
                    if (o.IdentityRules && !(worstImage <= 2.5)) r.Add(Violation.BoundaryOutsideTube, "the iterated image of an input edge drifts " + worstImage.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " pixels from it, more than the guard of 2.5");
                }
                // a boundary point at distance d2 (squared) from the exact input boundary is accepted within the pixel tube, or, farther but
                // within the drift guard, if it lies within the pixel tube of an image link; false: outside (BoundaryOutsideTube)
                bool OnImage(Pt pt)
                {
                    if (!o.IdentityRules) return false;
                    if (imageLinks == null) {
                        // (built on the first use: only a boundary point beyond the pixel tube needs it)
                        imageLinks = new List<(Pt a, Pt b)>();
                        foreach (var kv in full.Images)
                            for (int k = 0; k + 1 < kv.Value.Count; ++k) imageLinks.Add((Centre(kv.Value[k], snapPixel), Centre(kv.Value[k + 1], snapPixel)));
                    }
                    foreach (var (la, lb) in imageLinks) if (Exact.Dist2ToSegment(la, lb, pt) <= boundaryTube2) return true;
                    return false;
                }
                if (domain.IsHull || grid) {
                    // D13: the hull is the boundary, and splitting a hull edge in doubles cannot stay exactly on it (O2: a few ulps off a
                    // segment are fine): a boundary vertex within the tube of a hull edge counts as moved by its distance (found with M3)
                    var hullEdges = domain.Edges().ToList();
                    var checkedVertices = new HashSet<int>();
                    foreach (var pair in edges) {
                        if (pair.Value.Count != 1) continue;
                        foreach (int id in new[] { pair.Value[0].a, pair.Value[0].b }) {
                            if (!checkedVertices.Add(id)) continue;
                            // (the edges within the tube only: the minimum counts only when it is within the tube, and then its edge is
                            // among them)
                            var near = DomainEdgesNear(v[id], driftTube2);
                            if (CheckFastPathForTests && hullEdges.Count > 0) {
                                Q full0 = hullEdges.Select(h => Exact.Dist2ToSegment(h.a, h.b, v[id])).Aggregate(Q.Min);
                                Q got = near.Count == 0 ? Q.Zero : near.Select(h => Exact.Dist2ToSegment(h.a, h.b, v[id])).Aggregate(Q.Min);
                                if (full0 <= driftTube2 && (near.Count == 0 || got != full0)) FastPathDiffers("the nearest hull edge of boundary vertex " + id);
                            }
                            // (no hull edge at all, every input point the same: the scan's minimum of nothing threw; the domain is degenerate
                            // and reported as such, TrianglesOnDegenerate)
                            if (hullEdges.Count == 0) continue;
                            Q d = near.Count == 0 ? Q.Zero : near.Select(h => Exact.Dist2ToSegment(h.a, h.b, v[id])).Aggregate(Q.Min);
                            bool inside = near.Count > 0 && d <= (grid ? driftTube2 : tube2) && (!grid || d <= boundaryTube2 || OnImage(v[id]));
                            if (!inside) {
                                if (grid && o.IdentityRules) r.Add(Violation.BoundaryOutsideTube, "boundary vertex " + id + " at " + v[id] + " is farther than " + GridTubePixels + " pixel from the input's boundary and off its image");
                                continue;
                            }
                            moved2 = Q.Max(moved2, d);
                        }
                    }
                    if (grid && o.IdentityRules) {
                        // the boundary edges between the vertices (a chain link between two pixel centres is straight, the input boundary is not):
                        // their quarter points, as far from the input boundary as they are
                        int far = 0;
                        foreach (var pair in edges) {
                            if (pair.Value.Count != 1) continue;
                            Pt pa = v[pair.Value[0].a], pb = v[pair.Value[0].b];
                            foreach (var f in EighthPoints) {
                                var pt = new Pt(pa.Qx + (pb.Qx - pa.Qx) * f, pa.Qy + (pb.Qy - pa.Qy) * f);
                                var nearEdges = DomainEdgesNear(pt, driftTube2);
                                if (nearEdges.Count == 0) { if (far++ < 8) r.Add(Violation.BoundaryOutsideTube, "boundary edge " + pair.Value[0].a + "-" + pair.Value[0].b + " leaves the grid tube of the input's boundary near " + pt); continue; }
                                Q dd = nearEdges.Select(h => Exact.Dist2ToSegment(h.a, h.b, pt)).Aggregate(Q.Min);
                                if (dd > driftTube2 || dd > boundaryTube2 && !OnImage(pt)) { if (far++ < 8) r.Add(Violation.BoundaryOutsideTube, "boundary edge " + pair.Value[0].a + "-" + pair.Value[0].b + " leaves the grid tube of the input's boundary and its image near " + pt); continue; }
                                moved2 = Q.Max(moved2, dd);
                            }
                        }
                        InputBoundaryNearOutput(hullEdges, boundaryTube2);
                    }
                }
                // (C): the mesh area against the snapped domain with all hot pixels, a report (see OracleReport.MeshMinusSnappedArea)
                if (e.ReportSnappedArea && input.SnapTolerance > 0 && o.IdentityRules && effectiveSeeds.Count == 0) {
                    Snap();
                    r.MeshMinusSnappedArea = (r.Area2 - full.Domain.Area * 2) / (snapPixel * snapPixel * 2);
                }
                if (effectiveSeeds.Count == 0) {
                    Q diff = Q.Abs(r.Area2 - domain.Area * 2);
                    Q perimeter = domain.Perimeter2Bound * 2;
                    if (diff * diff > moved2 * perimeter * perimeter) r.Add(Violation.AreaMismatch, "twice the mesh area is " + r.Area2 + ", twice the domain's " + (domain.Area * 2) + ", moved at most sqrt(" + moved2 + ")");
                } else {
                    // A seed-only hole removes a cell of the input constraints, which Domain does not compute: the fixture must state the area.
                    if (e.StatedArea == null) r.Add(Violation.CannotJudge, "a hole seed outside all hole rings: the case must state the area");
                    foreach (var p in effectiveSeeds) if (LocateExact(p) >= 0 && !SweptBySnapping(p)) r.Add(Violation.RemovedPointCovered, "hole seed " + p + " is covered");
                }
                if (e.StatedArea != null && Q.Abs(r.Area2 - e.StatedArea.Value * 2) > e.StatedAreaTolerance * 2)
                    r.Add(Violation.AreaMismatch, "twice the mesh area is " + r.Area2 + ", the specification states area " + e.StatedArea.Value);
                foreach (var pair in edges) {
                    if (pair.Value.Count != 1) continue;
                    int a = pair.Value[0].a, b = pair.Value[0].b;
                    bool constrained = constraintEdges.Contains(pair.Key);
                    if (domain.IsHull) {
                        // on a hull edge: within the tube of it, so exactly on it if the tube is 0
                        bool hull = DomainEdgesNear(v[a], boundaryTube2).Any(s => Exact.Dist2ToSegment(s.a, s.b, v[a]) <= boundaryTube2 && Exact.Dist2ToSegment(s.a, s.b, v[b]) <= boundaryTube2)
                            || CutsMergedHullCorners(v[a], v[b]);
                        if (!hull && !(constrained && SourceCovers(a, b))) r.Add(e.Milestone == Milestone.M1 ? Violation.HullBoundary : Violation.BoundaryNotConstrained,
                            "boundary edge " + a + "-" + b + " neither on the hull nor on a constraint");
                    } else if (!constrained) r.Add(Violation.BoundaryNotConstrained, "boundary edge " + a + "-" + b + " is no constraint");
                }
                // The area tolerance is global, so a gap bounded by constraints could hide in the slack of an unrelated move (second review,
                // finding 1). The domain must therefore not continue beyond a boundary edge: probe outside its midpoint, at 1/16 of its length.
                if (effectiveSeeds.Count == 0) {
                    int gaps = 0;
                    Q half = new Q(1, 2), sixteenth = new Q(1, 16);
                    foreach (var pair in edges) {
                        if (pair.Value.Count != 1) continue;
                        Pt pa = v[pair.Value[0].a], pb = v[pair.Value[0].b];
                        Q dx = pb.Qx - pa.Qx, dy = pb.Qy - pa.Qy;
                        // a -> b is counter-clockwise in its triangle: the outside is on the right
                        var probe = new Pt((pa.Qx + pb.Qx) * half + dy * sixteenth, (pa.Qy + pb.Qy) * half - dx * sixteenth);
                        // with moved geometry the true boundary may be up to sqrt(moved2) away from the edge, the probe L/16 beyond it:
                        // (sqrt(m) + L/16)^2 <= 2 m + 2 L^2 / 256
                        Q near2 = moved2 * 2 + (dx * dx + dy * dy) * new Q(2, 256);
                        if (domain.Classify(probe) != Where.Inside || (moved2.Sign > 0 && DomainEdgesNear(probe, near2).Any(sg => Exact.Dist2ToSegment(sg.a, sg.b, probe) <= near2)) || LocateExact(probe) >= 0) continue;
                        if (gaps++ < 8) r.Add(Violation.AreaMismatch, "the domain continues uncovered beyond boundary edge " + pair.Value[0].a + "-" + pair.Value[0].b + ", at " + probe);
                    }
                }
                int reports = 0;
                foreach (var t in o.Triangles) {
                    Pt c = Centroid(t);
                    // a sliver between a moved line and its original lies outside: its centroid is then within the movement of the boundary
                    if (domain.Classify(c) == Where.Outside && !(moved2.Sign > 0 && NearBoundary(c)) && reports++ < 8)
                        r.Add(Violation.TriangleOutsideDomain, "triangle " + t[0] + " " + t[1] + " " + t[2] + " is outside the domain");
                }
                foreach (var p in e.Removed) if (LocateExact(Pt.FromDoubles(p[0], p[1])) >= 0) r.Add(Violation.RemovedPointCovered, "(" + p[0] + ", " + p[1] + ") must be in no triangle");
                foreach (var p in e.Kept) if (LocateExact(Pt.FromDoubles(p[0], p[1])) < 0) r.Add(Violation.KeptPointMissing, "(" + p[0] + ", " + p[1] + ") must be in a triangle");
            }

            /// <summary> the distance, in pixels, within which the output boundary lies from the input's and the input's from the output's (rule O-G) </summary>
            Dictionary<(BigInteger x, BigInteger y), List<int>> pixelVertices;

            const int GridTubePixels = 1;

            /// <summary>
            /// The guard on the image itself (owner decision 2026-11-10): no node of a source's iterated image lies farther than this many
            /// pixels from the exact source. Input rounding moves at most half a pixel per axis (SR with restore); refinement vertices may reroute
            /// other chains through hot pixels (Q17), so a chain at a sub-pixel wedge can end up farther: measured at most 1.6 pixels, p99.9
            /// 0.707 pixel over about 85 000 sources. A bounded drift cannot give back one pixel (Packer 2006, 2008: the bound must be about
            /// three times the deviation of snap rounding; Belussi et al. 2016, section 5.2: it collapses at small angles).
            /// </summary>
            static readonly Q GridImageDriftPx = new Q(5, 2);   // 2.5 pixels

            // the points between the ends of an edge that rule O-G samples: the distance to a set is 1-Lipschitz, so between samples a
            // chord of length L can be at most L/16 beyond the tube
            static readonly Q[] EighthPoints = Enumerable.Range(1, 7).Select(k => new Q(k, 8)).ToArray();

            /// <summary>
            /// The other direction of rule O-G: points of the input boundary (the quarter points of its edges) lie within the grid tube of an
            /// output boundary edge, or are collapsed or merged: within the tube of another input edge (a feature narrower than a pixel
            /// vanishes in snap rounding, Q17, which also drops the other edge's boundary)
            /// </summary>
            void InputBoundaryNearOutput(List<(Pt a, Pt b)> inputEdges, Q tube2Grid)
            {
                var boundary = new List<(int a, int b)>();
                foreach (var pair in edges) if (pair.Value.Count == 1) boundary.Add((pair.Value[0].a, pair.Value[0].b));
                if (boundary.Count == 0) return;
                // a grid over the output boundary edges' boxes, as the domain edges have one
                var box = new double[4 * boundary.Count];
                for (int k = 0; k < boundary.Count; ++k) SegmentBox(v[boundary[k].a], v[boundary[k].b], 0, box, 4 * k);
                var index = e.Reference ? null : new BoxGrid(box, boundary.Count);
                bool NearOutput(Pt p)
                {
                    IEnumerable<int> candidates = Enumerable.Range(0, boundary.Count);
                    if (index != null && DistanceQueryBox(p, tube2Grid, out double xl, out double xh, out double yl, out double yh)) candidates = index.Query(xl, xh, yl, yh);
                    foreach (int k in candidates) if (Exact.Dist2ToSegment(v[boundary[k].a], v[boundary[k].b], p) <= tube2Grid) return true;
                    return false;
                }
                int reports = 0;
                for (int i = 0; i < inputEdges.Count; ++i) {
                    var ed = inputEdges[i];
                    foreach (var f in EighthPoints) {
                        var p = new Pt(ed.a.Qx + (ed.b.Qx - ed.a.Qx) * f, ed.a.Qy + (ed.b.Qy - ed.a.Qy) * f);
                        if (NearOutput(p)) continue;
                        // a part of a ring edge outside the domain (a hole reaching beyond the outer ring, D5) bounds nothing: the domain has
                        // no interior just beside p on either side
                        Q eps = new Q(1, 1 << 20);
                        var side = new Pt(p.Qx - (ed.b.Qy - ed.a.Qy) * eps, p.Qy + (ed.b.Qx - ed.a.Qx) * eps);
                        var other2 = new Pt(p.Qx + (ed.b.Qy - ed.a.Qy) * eps, p.Qy - (ed.b.Qx - ed.a.Qx) * eps);
                        if (domain.Classify(side) != Where.Inside && domain.Classify(other2) != Where.Inside) continue;
                        // collapsed or merged: another input edge within two tubes of p (also a neighbour at an acute corner: a wedge up to two
                        // pixels wide collapses onto it; the area tolerance, measured on the output boundary, bounds what that can hide)
                        bool collapsed = false;
                        foreach (var other in DomainEdgesNear(p, tube2Grid * 4)) {
                            if (other.a.Equals(ed.a) && other.b.Equals(ed.b) || other.a.Equals(ed.b) && other.b.Equals(ed.a)) continue;
                            if (Exact.Dist2ToSegment(other.a, other.b, p) > tube2Grid * 4) continue;
                            collapsed = true; break;
                        }
                        if (!collapsed && reports++ < 8) r.Add(Violation.BoundaryOutsideTube, "the input boundary near " + p + " has no output boundary within " + GridTubePixels + " pixel");
                    }
                }
            }

            bool NearBoundary(Pt p) => DomainEdgesNear(p, moved2).Any(s => Exact.Dist2ToSegment(s.a, s.b, p) <= moved2);

            List<(Pt a, Pt b)> domainEdges;
            double[] domainEdgeBox;
            BoxGrid domainEdgeGrid;

            /// <summary>
            /// The domain's ring edges that may lie within sqrt(d2) of p: on the fast path those whose box in doubles (around the exact one,
            /// as <see cref="SegmentBox"/>) meets p's box widened by sqrt(d2) from above, from a grid over the edge boxes; all of them on
            /// the reference path or where a box is unknown. Equivalence: the distance from p to a segment is at least the distance from p
            /// to the segment's box, so an edge whose box misses p's widened box is farther than sqrt(d2) and fails the exact test the
            /// caller runs; the grid returns a superset of the boxes meeting the query box (its cells are monotone in the coordinates).
            /// The edges come in the order of Edges(), so a caller taking the first or the minimum sees the same as with the full scan.
            /// </summary>
            /// <summary>
            /// The box in doubles that holds every point within sqrt(d2) of p: p's box (around the exact point) widened by sqrt(d2) from above,
            /// the square root of d2's upper bound times 1 + 1e-12 plus 1e-300 (Bounds pads by 1e-13 relative and 1e-300 absolute; the
            /// square root and the subtraction round by far less). False when it cannot be computed (the caller scans)
            /// </summary>
            internal static bool DistanceQueryBox(Pt p, Q d2, out double xl, out double xh, out double yl, out double yh)
            {
                xl = xh = yl = yh = 0;
                if (d2.Sign < 0 || !d2.Bounds(out _, out double d2hi) || !p.Bounds(false, out double pxl, out double pxh) || !p.Bounds(true, out double pyl, out double pyh)) return false;
                double reach = Math.Sqrt(Math.Max(0, d2hi)) * (1 + 1e-12) + 1e-300;
                if (double.IsInfinity(reach) || double.IsNaN(reach)) return false;
                xl = pxl - reach; xh = pxh + reach; yl = pyl - reach; yh = pyh + reach;
                return !double.IsInfinity(xl) && !double.IsInfinity(xh) && !double.IsInfinity(yl) && !double.IsInfinity(yh);
            }

            List<(Pt a, Pt b)> DomainEdgesNear(Pt p, Q d2)
            {
                if (domainEdges == null) {
                    domainEdges = domain.Edges().ToList();
                    domainEdgeBox = new double[4 * domainEdges.Count];
                    for (int k = 0; k < domainEdges.Count; ++k) SegmentBox(domainEdges[k].a, domainEdges[k].b, 0, domainEdgeBox, 4 * k);
                    if (!e.Reference) domainEdgeGrid = new BoxGrid(domainEdgeBox, domainEdges.Count);
                }
                if (domainEdgeGrid == null || !DistanceQueryBox(p, d2, out double qxl, out double qxh, out double qyl, out double qyh)) return domainEdges;
                var found = domainEdgeGrid.Query(qxl, qxh, qyl, qyh);
                if (CheckFastPathForTests) {
                    BoxGrid.CheckSuperset(domainEdgeBox, domainEdges.Count, qxl, qxh, qyl, qyh, found, "the domain edges");
                    var indexed = new HashSet<int>(found);
                    for (int k = 0; k < domainEdges.Count; ++k)
                        if (Exact.Dist2ToSegment(domainEdges[k].a, domainEdges[k].b, p) <= d2 && !indexed.Contains(k)) FastPathDiffers("domain edge " + k + " within the distance missing from the index");
                }
                return found.Select(k => domainEdges[k]).ToList();
            }


            /// <summary>In the closure of the domain's interior: a boundary point of two touching holes is not, although it is on rings.</summary>
            List<(Pt a, Pt b)> closureEdges;
            double[] closureBox;
            BoxGrid closureGrid;
            Dictionary<int, List<(Pt from, Pt to)>> closurePieces;

            bool InClosure(Pt p)
            {
                var where = domain.Classify(p);
                if (where != Where.Boundary) return where == Where.Inside;
                // the ring edges through p, and their retained pieces, through a grid of the edges' boxes (each unused point on the
                // boundary scanned all ring edges and cut every edge through it at all of them: 36 s for a corridor with a ring vertex
                // every pixel, 4006 of them; review of Q17); the reference path scans
                if (closureEdges == null) {
                    closureEdges = domain.Edges().ToList();
                    closureBox = new double[4 * closureEdges.Count];
                    for (int g = 0; g < closureEdges.Count; ++g) SegmentBox(closureEdges[g].a, closureEdges[g].b, 0, closureBox, 4 * g);
                    closureGrid = e.Reference ? null : new BoxGrid(closureBox, closureEdges.Count);
                    closurePieces = new Dictionary<int, List<(Pt from, Pt to)>>();
                }
                var own = new double[4];
                SegmentBox(p, p, 0, own, 0);
                IEnumerable<int> through = closureGrid == null || double.IsNaN(own[0]) ? Enumerable.Range(0, closureEdges.Count)
                    : closureGrid.Query(own[0], own[1], own[2], own[3]);
                foreach (int g in through) {
                    var edge = closureEdges[g];
                    if (!Exact.OnClosedSegment(edge.a, edge.b, p)) continue;
                    if (!closurePieces.TryGetValue(g, out var pieces)) {
                        if (closureGrid == null || double.IsNaN(closureBox[4 * g])) pieces = RetainedPieces(edge.a, edge.b, closureEdges);
                        else {
                            // the ring edges whose boxes meet the edge's widened by its own extent; the others are farther than that
                            double reach = Math.Max(closureBox[4 * g + 1] - closureBox[4 * g], closureBox[4 * g + 3] - closureBox[4 * g + 2]);
                            var near = closureGrid.Query(closureBox[4 * g] - reach, closureBox[4 * g + 1] + reach, closureBox[4 * g + 2] - reach, closureBox[4 * g + 3] + reach);
                            pieces = near.Count == closureEdges.Count || !(reach > 0)
                                ? RetainedPieces(edge.a, edge.b, closureEdges)
                                : RetainedPieces(edge.a, edge.b, near.Select(k => closureEdges[k]).ToList(), null, reach);
                        }
                        closurePieces[g] = pieces;
                    }
                    foreach (var piece in pieces)
                        if (Exact.OnClosedSegment(piece.from, piece.to, p)) return true;
                }
                return false;
            }

            /// <summary>
            /// A point on a ring edge belongs to the domain only if the domain has interior next to it. Probes both sides at a distance
            /// below that of every ring edge not through the point.
            /// </summary>
            bool InteriorBeside(Pt m, Q ux, Q uy, List<(Pt a, Pt b)> rings, double farGap = double.PositiveInfinity)
            {
                Q limit = new Q(-1, 1);
                // (on the fast path a lower bound of each distance from doubles where it is surely positive: the probe needs only a
                // distance below every ring edge's, any smaller one gives the same answer; exact where the bound is not positive)
                var md = PtD(m);
                bool filterOn = !e.Reference && !double.IsNaN(md.x) && !double.IsNaN(md.y);
                foreach (var ring in rings) {
                    if (filterOn) {
                        var cd = PtD(ring.a); var dd = PtD(ring.b);
                        double mag = Math.Max(Math.Max(Math.Abs(md.x), Math.Abs(md.y)), Math.Max(Math.Max(Math.Abs(cd.x), Math.Abs(cd.y)), Math.Max(Math.Abs(dd.x), Math.Abs(dd.y))));
                        double low = Math.Sqrt(Dist2D(cd, dd, md.x, md.y)) - mag * 1e-12;
                        if (low > 0 && FilterRange(mag)) {
                            Q lq = Q.FromDouble(low);
                            if (limit.Sign < 0 || lq * lq < limit) limit = lq * lq;
                            continue;
                        }
                    }
                    if (Exact.OnClosedSegment(ring.a, ring.b, m)) continue;
                    Q d = Exact.Dist2ToSegment(ring.a, ring.b, m);
                    if (limit.Sign < 0 || d < limit) limit = d;
                }
                // the ring edges not in the list are farther than farGap (a lower bound, made a little smaller still)
                if (!double.IsInfinity(farGap)) {
                    Q far = Q.FromDouble(farGap * (1 - 1e-9));
                    if (limit.Sign < 0 || far * far < limit) limit = far * far;
                }
                Q step = Q.One, nn = ux * ux + uy * uy;
                while (limit.Sign >= 0 && step * step * nn >= limit) step = step * new Q(1, 2);
                Q mx = m.Qx, my = m.Qy;
                return domain.Classify(new Pt(mx - uy * step, my + ux * step)) == Where.Inside
                    || domain.Classify(new Pt(mx + uy * step, my - ux * step)) == Where.Inside;
            }

            Pt Centroid(int[] t)
            {
                Pt a = v[t[0]], b = v[t[1]], c = v[t[2]];
                Q third = new Q(1, 3);
                return new Pt((a.Qx + b.Qx + c.Qx) * third, (a.Qy + b.Qy + c.Qy) * third);
            }

            BoxGrid triangleGrid;

            int LocateExact(Pt p)
            {
                bool Contains(int i)
                {
                    var t = o.Triangles[i];
                    return Exact.Orient(v[t[0]], v[t[1]], p) >= 0 && Exact.Orient(v[t[1]], v[t[2]], p) >= 0 && Exact.Orient(v[t[2]], v[t[0]], p) >= 0;
                }
                // on the fast path only the triangles whose box may hold p, in ascending order: the first that contains p is the full
                // scan's (the coverage probes of poly2tri's debug2 scanned all 10 000 triangles each, 22 s)
                if (!e.Reference && p.Bounds(false, out double pxl, out double pxh) && p.Bounds(true, out double pyl, out double pyh)) {
                    if (triangleGrid == null) {
                        var box = new double[4 * o.Triangles.Length];
                        for (int i = 0; i < o.Triangles.Length; ++i) {
                            var t = o.Triangles[i];
                            if (t.All(j => vKnown[j])) {
                                box[4 * i] = t.Min(j => vxl[j]); box[4 * i + 1] = t.Max(j => vxh[j]);
                                box[4 * i + 2] = t.Min(j => vyl[j]); box[4 * i + 3] = t.Max(j => vyh[j]);
                            } else for (int k = 0; k < 4; ++k) box[4 * i + k] = double.NaN;
                        }
                        triangleGrid = new BoxGrid(box, o.Triangles.Length);
                    }
                    foreach (int i in triangleGrid.Query(pxl, pxh, pyl, pyh)) if (Contains(i)) return i;
                    return -1;
                }
                for (int i = 0; i < o.Triangles.Length; ++i) if (Contains(i)) return i;
                return -1;
            }

            /// <summary>
            /// Every piece of an input constraint that lies in the closed domain is a chain of output edges inside the tube.
            /// With tube 0 that is the definition "the segment is a union of output edges"; a vertex in the open interior of
            /// an output edge has already failed <see cref="Conformity"/>.
            /// </summary>
            void Segments()
            {
                ChainsThroughHotPixels(); Lap("segments: chains through hot pixels");
                Coincidences(); Lap("segments: coincidences");
                var adjacency = new Dictionary<int, List<int>>();
                IEnumerable<long> graphEdges = subsegmentSet ?? (IEnumerable<long>)edges.Keys;
                foreach (long key in graphEdges) {
                    if (subsegmentSet != null && !edges.ContainsKey(key)) { r.Add(Violation.SubsegmentNotEdge, "subsegment " + KeyA(key) + "-" + KeyB(key) + " is no mesh edge"); continue; }
                    AddAdjacent(adjacency, KeyA(key), KeyB(key));
                    AddAdjacent(adjacency, KeyB(key), KeyA(key));
                }
                var allRingEdges = domain.Edges().ToList();
                // each ring edge's box in doubles (an interval around the exact one), for the filter below
                var ringBox = new double[4 * allRingEdges.Count];
                for (int g = 0; g < allRingEdges.Count; ++g) SegmentBox(allRingEdges[g].a, allRingEdges[g].b, 0, ringBox, 4 * g);
                // on the fast path grids over the vertices and the ring edges: every source scanned all of both (sources x vertices, sources
                // x ring edges; 39 s of poly2tri's debug2 with 10 000 of each). They return supersets in ascending order, so the lists
                // below are the same as from the full scans
                BoxGrid vertexGrid = null, ringGrid = null;
                if (!e.Reference) {
                    var vbox = new double[4 * v.Length];
                    for (int i = 0; i < v.Length; ++i) {
                        if (vKnown[i]) { vbox[4 * i] = vxl[i]; vbox[4 * i + 1] = vxh[i]; vbox[4 * i + 2] = vyl[i]; vbox[4 * i + 3] = vyh[i]; }
                        else for (int j = 0; j < 4; ++j) vbox[4 * i + j] = double.NaN;
                    }
                    vertexGrid = new BoxGrid(vbox, v.Length);
                    ringGrid = new BoxGrid(ringBox, allRingEdges.Count);
                }
                for (int k = 0; k < sources.Count; ++k) {
                    var s = sources[k];
                    var near = new List<int>();
                    bool boxed = vertexGrid != null && !double.IsNaN(sBox[4 * k]);
                    var vertexCandidates = boxed ? vertexGrid.Query(sBox[4 * k], sBox[4 * k + 1], sBox[4 * k + 2], sBox[4 * k + 3]) : null;
                    if (boxed) { foreach (int i in vertexCandidates) if (MayBeNear(k, i) && InTube(s.a, s.b, v[i])) near.Add(i); }
                    else for (int i = 0; i < v.Length; ++i) if (MayBeNear(k, i) && InTube(s.a, s.b, v[i])) near.Add(i);
                    // only ring edges whose box meets the source's box widened by the tube can cut the source or lie within the tube of
                    // a point on it; the others are certainly farther (the filter skips no exact test that could succeed)
                    int kk = k;
                    bool Meets(int g) => e.Reference || double.IsNaN(sBox[4 * kk]) || double.IsNaN(ringBox[4 * g])
                        || !(ringBox[4 * g + 1] < sBox[4 * kk] || ringBox[4 * g] > sBox[4 * kk + 1] || ringBox[4 * g + 3] < sBox[4 * kk + 2] || ringBox[4 * g + 2] > sBox[4 * kk + 3]);
                    // a lower bound of the distance from any point of the source to the ring edges left out: the gap between the boxes
                    var own = new double[4];
                    SegmentBox(s.a, s.b, 0, own, 0);
                    double farGap = double.PositiveInfinity;
                    List<(Pt a, Pt b)> ringEdges;
                    if (boxed && !double.IsNaN(own[0])) {
                        // the edges in the cells around the source's box widened by w; those not returned are farther than w (a smaller
                        // lower bound than the full scan's: it only shortens InteriorBeside's probe, whose answer it does not change)
                        double w = Math.Max(Math.Max(sBox[4 * k + 1] - sBox[4 * k], sBox[4 * k + 3] - sBox[4 * k + 2]), 0);
                        var candidates = ringGrid.Query(Math.Min(sBox[4 * k], own[0]) - w, Math.Max(sBox[4 * k + 1], own[1]) + w, Math.Min(sBox[4 * k + 2], own[2]) - w, Math.Max(sBox[4 * k + 3], own[3]) + w);
                        ringEdges = new List<(Pt a, Pt b)>();
                        foreach (int g in candidates) {
                            if (Meets(g)) { ringEdges.Add(allRingEdges[g]); continue; }
                            double gap = Math.Max(Math.Max(ringBox[4 * g] - own[1], own[0] - ringBox[4 * g + 1]), Math.Max(ringBox[4 * g + 2] - own[3], own[2] - ringBox[4 * g + 3]));
                            farGap = Math.Min(farGap, gap);
                        }
                        if (candidates.Count < allRingEdges.Count) farGap = Math.Min(farGap, w);
                    } else {
                        ringEdges = Enumerable.Range(0, allRingEdges.Count).Where(Meets).Select(g => allRingEdges[g]).ToList();
                        for (int g = 0; g < allRingEdges.Count; ++g) {
                            if (Meets(g)) continue;
                            double gap = Math.Max(Math.Max(ringBox[4 * g] - own[1], own[0] - ringBox[4 * g + 1]), Math.Max(ringBox[4 * g + 2] - own[3], own[2] - ringBox[4 * g + 3]));
                            farGap = Math.Min(farGap, gap);
                        }
                    }
                    // With a snap tolerance the chain follows the source's iterated image (Q17: output vertices are hot pixels and reroute it), which
                    // can leave the tube of the exact segment at a sub-pixel wedge (up to ~1.6 pixels measured, rule O-G): the vertices at the
                    // pixel centres of the image's nodes are candidates too, in addition to the tube's (owner decision 2026-11-10, option A)
                    var imageNear = new List<int>();
                    if (input.SnapTolerance > 0 && o.IdentityRules) {
                        Snap();
                        if (full.Images.TryGetValue((s.a, s.b), out var imageNodes)) {
                            if (pixelVertices == null) {
                                // (the vertices at their pixel centres by pixel, once: not a scan of all vertices per source)
                                pixelVertices = new Dictionary<(BigInteger x, BigInteger y), List<int>>();
                                for (int i = 0; i < v.Length; ++i) {
                                    if (!AtPixelCentre(v[i], v[i], snapPixel)) continue;
                                    var key = PixelOf(v[i], snapPixel);
                                    if (!pixelVertices.TryGetValue(key, out var list)) pixelVertices.Add(key, list = new List<int>());
                                    list.Add(i);
                                }
                            }
                            var nearSet = new HashSet<int>(near);
                            foreach (var node in imageNodes)
                                if (pixelVertices.TryGetValue(node, out var inPixel)) foreach (int i in inPixel) if (nearSet.Add(i)) imageNear.Add(i);
                        }
                    }
                    foreach (var piece in RetainedPieces(s.a, s.b, ringEdges, double.IsNaN(own[0]) || !(farGap > 0) ? allRingEdges : null, farGap)) {
                        var candidates = new HashSet<int>(near);
                        foreach (int i in imageNear) candidates.Add(i);
                        // A piece ends where a ring edge cuts it. At a tiny angle that point is ill-conditioned along the lines: a crossing rounded
                        // to doubles (D1) can lie far from the exact one along them while within the tube of both. So a vertex in the tubes of the
                        // source and of a ring edge through the end also ends the chain.
                        // A ring edge along the source itself cuts nothing: it would make every vertex a start and a goal (review of M2)
                        bool Along((Pt a, Pt b) g) => InTube(g.a, g.b, s.a) && InTube(g.a, g.b, s.b)
                            || InTube(s.a, s.b, g.a) && InTube(s.a, s.b, g.b);
                        var cutFrom = ringEdges.Where(g => InTube(g.a, g.b, piece.from) && !Along(g)).ToList();
                        var cutTo = ringEdges.Where(g => InTube(g.a, g.b, piece.to) && !Along(g)).ToList();
                        bool OnCut(List<(Pt a, Pt b)> cut, int i) => tube2.Sign > 0 && cut.Any(g => InTube(g.a, g.b, v[i]));
                        var starts = candidates.Where(i => Exact.Len2(v[i], piece.from) <= tube2 || OnCut(cutFrom, i)).ToList();
                        var goals = new HashSet<int>(candidates.Where(i => Exact.Len2(v[i], piece.to) <= tube2 || OnCut(cutTo, i)));
                        var previous = new Dictionary<int, int>();
                        var queue = new Queue<int>();
                        foreach (int i in starts) { previous[i] = -1; queue.Enqueue(i); }
                        int reached = -1;
                        while (queue.Count > 0 && reached < 0) {
                            int at = queue.Dequeue();
                            if (goals.Contains(at)) { reached = at; break; }
                            if (!adjacency.TryGetValue(at, out var next)) continue;
                            foreach (int n in next)
                                if (candidates.Contains(n) && !previous.ContainsKey(n)) { previous[n] = at; queue.Enqueue(n); }
                        }
                        if (reached < 0) {
                            if (ChainAcrossCollapsed((s.a, s.b), candidates, starts, goals, adjacency)) { ++r.CollapsedSegments; continue; }
                            r.Add(Violation.SegmentMissing, "input constraint " + s.a + " - " + s.b + ": no chain from " + piece.from + " to " + piece.to);
                            continue;
                        }
                        // how far the end moved: to the exact end, or, if it ended on the cutting ring edge instead, off the two lines only
                        Q EndMove(int i, Pt end, List<(Pt a, Pt b)> cut)
                        {
                            Q d = Exact.Len2(v[i], end);
                            if (d <= tube2) return d;
                            Q best = d;
                            foreach (var g in cut) best = Q.Min(best, Q.Max(Exact.Dist2ToSegment(g.a, g.b, v[i]), Exact.Dist2ToSegment(s.a, s.b, v[i])));
                            return best;
                        }
                        moved2 = Q.Max(moved2, EndMove(reached, piece.to, cutTo));
                        int first = reached;
                        var path = new List<int>();
                        for (int at = reached; at >= 0; at = previous[at]) { moved2 = Q.Max(moved2, Exact.Dist2ToSegment(s.a, s.b, v[at])); first = at; path.Add(at); }
                        path.Reverse();
                        chainPaths.Add((piece.from, piece.to, path));
                        ChainThroughContactedPixels(piece.from, piece.to);
                        moved2 = Q.Max(moved2, EndMove(first, piece.from, cutFrom));
                        if (s.locked) CheckLocked(s, reached, previous);
                    }
                }
            }

            /// <summary>
            /// O-H5, refinement keeps the chains of the input's rounding apart (owner decision on Q17, 2026-11-03): a vertex of refinement
            /// (its pixel no hot pixel of the input's own rounding) lies on at most one chain, so it has two subsegments. Images meet only at
            /// hot pixel centres, so a link the input's rounding already shares, split by refinement, keeps two. Three mean that the chains
            /// of two input or hull edges share a link from there on (RefinementCoincidence: the strip between them collapsed, the class
            /// #360 fixed in the mesher); four or more, that they meet in the vertex (RefinementPinch: a known violation until the mesher's
            /// pinch rule, a failing rule since). Only with a snap tolerance, the identity rules and reported subsegments
            /// </summary>
            void Coincidences()
            {
                if (!(input.SnapTolerance > 0) || !o.IdentityRules || subsegmentSet == null) return;
                SnapHot();
                var degree = new Dictionary<int, int>();
                foreach (long key in subsegmentSet) {
                    if (!edges.ContainsKey(key)) continue;
                    foreach (int i in new[] { KeyA(key), KeyB(key) }) degree[i] = degree.TryGetValue(i, out int d) ? d + 1 : 1;
                }
                int coincidences = 0, pinches = 0;
                foreach (var pair in degree.OrderBy(p => p.Key)) {
                    if (pair.Value < 3 || snapHot.Contains(PixelOf(v[pair.Key], snapPixel))) continue;
                    if (pair.Value == 3) {
                        if (coincidences++ < 8) r.Add(Violation.RefinementCoincidence, "vertex " + pair.Key + " " + v[pair.Key] + " of refinement has 3 subsegments: two chains share a link from there, which the input's own rounding keeps apart");
                    } else if (pinches++ < 8)
                        r.Add(Violation.RefinementPinch, "vertex " + pair.Key + " " + v[pair.Key] + " of refinement has " + pair.Value + " subsegments: two chains meet there, which the input's own rounding keeps apart");
                }
            }

            Dictionary<(BigInteger x, BigInteger y), List<int>> verticesInPixel;
            Dictionary<int, List<int>> subsegmentsAt;

            /// <summary>
            /// Snap rounding's defining property on the input's own rounding (Hobby 1999; task 39, coordinator 2026-11-03): every hot pixel of
            /// the input's rounding (input points, crossings, restore pixels) that a retained piece of an input edge contacts, its end pixels
            /// aside, holds a vertex of the piece's chain: an output vertex in that pixel with a subsegment whose other end lies in the
            /// edge's envelope (snap rounding keeps a chain within the Minkowski sum of its segment and a pixel). O-H4 checks links only;
            /// this closes the case SRR's Listing 2 leaves (a link that only touches a restore pixel's corner while its segment crosses
            /// the pixel). A side is excused, as by O-H5, where the edge's own link of its image on that side collapses in both images
            /// (a pixel that collapses in both images lies on such links). A subsegment between two pixels of the piece's image that skips
            /// image pixels must be a link of the input's rounding (another edge's, or the hull's) or span collapsed links only (review of
            /// #36, S2). Known gap (third review of #36): another edge's link between two pixels of the image can still stand in for the
            /// piece's, where two further chains give the skipped pixel's vertex both sides (about four edges). Pixels of refinement are
            /// not demanded (#360 does not reroute through them)
            /// </summary>
            void ChainThroughContactedPixels(Pt a, Pt b)
            {
                if (!(input.SnapTolerance > 0) || snapHot == null || !o.IdentityRules || subsegmentSet == null) return;
                var u = PixelOf(a, snapPixel); var w = PixelOf(b, snapPixel);
                if (u.Equals(w)) return;
                if (verticesInPixel == null) {
                    verticesInPixel = new Dictionary<(BigInteger x, BigInteger y), List<int>>();
                    for (int i = 0; i < v.Length; ++i) {
                        var n = PixelOf(v[i], snapPixel);
                        if (!verticesInPixel.TryGetValue(n, out var list)) verticesInPixel.Add(n, list = new List<int>());
                        list.Add(i);
                    }
                    subsegmentsAt = new Dictionary<int, List<int>>();
                    foreach (long key in subsegmentSet) { AddAdjacent(subsegmentsAt, KeyA(key), KeyB(key)); AddAdjacent(subsegmentsAt, KeyB(key), KeyA(key)); }
                }
                Q half = snapPixel / 2;
                Q Along(Pt p) => (p.Qx - a.Qx) * (b.Qx - a.Qx) + (p.Qy - a.Qy) * (b.Qy - a.Qy);
                long work = 0;
                var image = Image(a, b, snapHot, snapHotList, snapPixel, ref work, snapHot);
                var index = new Dictionary<(BigInteger x, BigInteger y), int>(image.Count);
                for (int t = 0; t < image.Count; ++t) index[image[t]] = t;
                // A stretch between two pixels of the input's image is gone when the input's rounding collapses it (a spike: no area beside
                // it) and the full image does too, or, if output vertices rerouted it through further hot pixels (Q17) so that the
                // reroute makes a lobe with another chain (even-odd area in the full image that the mesh, as the input's rounding, does not
                // have), when that lobe lies within the pixel tube of the stretch: refinement never creates a collapse, but need not keep
                // what the input's rounding collapses (owner decision 2026-11-10). A stretch with area in the input's own image is judged
                // by the full image as before.
                bool Gone((BigInteger x, BigInteger y) p, (BigInteger x, BigInteger y) q)
                {
                    if (!LinkCollapsed(inputOnly, p, q)) return false;
                    if (LinkCollapsed(full, p, q)) return true;
                    var fullImage = full.Images[(a, b)];
                    int ip = fullImage.IndexOf(p), iq = fullImage.IndexOf(q);
                    if (ip < 0 || iq < 0 || ip == iq) return false;
                    if (ip > iq) { int tmp = ip; ip = iq; iq = tmp; }
                    Pt ca = Centre(fullImage[ip], snapPixel), cb = Centre(fullImage[iq], snapPixel);
                    if (!LobeWithinTube(ca, cb, Enumerable.Range(ip + 1, iq - ip - 1).Select(t => Centre(fullImage[t], snapPixel)), snapPixel)) return false;
                    ++r.LobeExcuses;
                    return true;
                }
                foreach (var h in SegmentPassed(a, b, u, w, snapHot, snapHotList, null, snapPixel, ref work)) {
                    // on the piece's chain: a vertex in the pixel with subsegments to vertices in the edge's envelope on both sides of the
                    // pixel's centre along the edge, both at the same vertex; a side also holds where the edge's own link of the image on
                    // that side collapses in both images (O-H5's rule (ii'): refinement may open area the input's rounding collapses)
                    Q along = Along(Centre(h, snapPixel));
                    if (!index.TryGetValue(h, out int k)) k = -1;
                    bool? goneBefore = null, goneAfter = null;
                    bool onChain = false;
                    if (verticesInPixel.TryGetValue(h, out var at))
                        foreach (int i in at) {
                            if (!subsegmentsAt.TryGetValue(i, out var next)) continue;
                            // (judged at the neighbour's exact pixel centre: an output vertex is that centre rounded to doubles, and a
                            // piece's end on its pixel's corner would otherwise miss the envelope by an ulp)
                            var centres = next.Select(q => Centre(PixelOf(v[q], snapPixel), snapPixel)).Where(q => InEnvelopeOf(a, b, q, half)).ToList();
                            bool before = centres.Any(q => Along(q) < along), after = centres.Any(q => Along(q) > along);
                            if (!(before && after) && k >= 0) {
                                Snap();
                                if (!before) before = goneBefore ?? (goneBefore = k == 0 || Gone(image[k - 1], h)).Value;
                                if (!after) after = goneAfter ?? (goneAfter = k == image.Count - 1 || Gone(h, image[k + 1])).Value;
                            }
                            if (before && after) { onChain = true; break; }
                        }
                    // no vertex with a side of its own needed: the edge's own links on both sides collapse in both images
                    if (!onChain && k >= 0) {
                        Snap();
                        onChain = (goneBefore ?? (goneBefore = k == 0 || Gone(image[k - 1], h)).Value) && (goneAfter ?? (goneAfter = k == image.Count - 1 || Gone(h, image[k + 1])).Value);
                    }
                    if (onChain) continue;
                    r.Add(Violation.ChainSkipsHotPixel, "the chain of " + a + " - " + b + " does not run through the hot pixel of " + Centre(h, snapPixel) + " that the input contacts");
                }
                // a subsegment between two pixels of the piece's image that skips image pixels between them is no link of this edge; it
                // must be another edge's link (or a hull link) of the input's rounding, or the edge's links it spans collapse in both
                // images. Otherwise two other chains could stand in for the skipped pixel's vertex above (review of #36, S2; the known
                // gap in the summary remains)
                for (int k = 0; k + 2 < image.Count; ++k) {
                    if (!verticesInPixel.TryGetValue(image[k], out var at)) continue;
                    foreach (int i in at) {
                        if (!subsegmentsAt.TryGetValue(i, out var next)) continue;
                        foreach (int j in next) {
                            var pj = PixelOf(v[j], snapPixel);
                            if (!index.TryGetValue(pj, out int m) || m <= k + 1) continue;
                            Snap();
                            if (IsLink(inputOnly, image[k], pj) || IsLink(full, image[k], pj)) continue;
                            bool spanGone = true;
                            for (int t = k; t < m && spanGone; ++t) spanGone = Gone(image[t], image[t + 1]);
                            if (spanGone) continue;
                            r.Add(Violation.ChainSkipsHotPixel, "subsegment " + i + "-" + j + " of the chain of " + a + " - " + b + " skips the hot pixel of " + Centre(image[k + 1], snapPixel)
                                + " that the input contacts, and is no link of the input's rounding");
                        }
                    }
                }
                ImageWorkForTests += work;
            }

            /// <summary>
            /// O-H5 in the chain search: a piece of source has a chain if one exists when the collapsed links of the source's
            /// image may be crossed without one. Each pixel of the image is a node, joined to the next one where the link between them is
            /// collapsed; the ends of such links join the candidate vertices in their pixels; the search starts and ends at the piece's own start and goal vertices (an
            /// end without an output vertex is a violation of its own). Everywhere else the chain must be there.
            /// </summary>
            bool ChainAcrossCollapsed((Pt a, Pt b) source, HashSet<int> candidates, List<int> starts, HashSet<int> goals, Dictionary<int, List<int>> adjacency)
            {
                if (!(input.SnapTolerance > 0)) return false;
                Snap();
                var image = full.Images[source];
                var node = new Dictionary<(BigInteger x, BigInteger y), int>();
                for (int k = 0; k < image.Count; ++k) node[image[k]] = -1 - k;
                // (candidate vertices join only the ends of collapsed links, and only at their pixels' centres: elsewhere, or off the
                // centre in a foreign output, two vertices in one pixel are not connected without an edge; reviews 4 and 5)
                var ends = new HashSet<int>();
                var excused = ExcusedLinks(source, out bool[] rerouted);
                for (int k = 0; k + 1 < image.Count; ++k) if (excused[k]) { ends.Add(-1 - k); ends.Add(-2 - k); }
                if (ends.Count == 0) return false;
                // the search, with the bridges of rerouted stretches or without them
                bool Search(bool viaRerouted)
                {
                    var bridge = new Dictionary<int, List<int>>();
                    void Join(int x, int y)
                    {
                        if (!bridge.TryGetValue(x, out var lx)) bridge.Add(x, lx = new List<int>()); lx.Add(y);
                        if (!bridge.TryGetValue(y, out var ly)) bridge.Add(y, ly = new List<int>()); ly.Add(x);
                    }
                    for (int k = 0; k + 1 < image.Count; ++k) if (excused[k] && (viaRerouted || !rerouted[k])) Join(-1 - k, -2 - k);
                    foreach (int i in candidates) {
                        var n = PixelOf(v[i], snapPixel);
                        // (at the centre as the grid check judges it, AtPixelCentre: a centre of a decimal pixel is no double)
                        if (node.TryGetValue(n, out int id) && ends.Contains(id) && AtPixelCentre(v[i], v[i], snapPixel)) Join(id, i);
                    }
                    var goalNodes = new HashSet<int>(goals);
                    var queue = new Queue<int>();
                    var seen = new HashSet<int>();
                    foreach (int i in starts) if (seen.Add(i)) queue.Enqueue(i);
                    while (queue.Count > 0) {
                        int at = queue.Dequeue();
                        if (goalNodes.Contains(at)) return true;
                        if (at >= 0 && adjacency.TryGetValue(at, out var next))
                            foreach (int n in next) if (candidates.Contains(n) && seen.Add(n)) queue.Enqueue(n);
                        if (bridge.TryGetValue(at, out var across))
                            foreach (int n in across) if (seen.Add(n)) queue.Enqueue(n);
                    }
                    return false;
                }
                if (!Search(true)) return false;
                // (iii): the piece needs a stretch that output vertices rerouted (it has no chain without one), reported apart
                if (rerouted.Any(x => x) && !Search(false)) ++r.CollapsedViaRefinement;
                return true;
            }

            static void AddAdjacent(Dictionary<int, List<int>> adjacency, int a, int b)
            {
                if (!adjacency.TryGetValue(a, out var list)) adjacency.Add(a, list = new List<int>());
                list.Add(b);
            }

            /// <summary>O9: a locked segment is split only at input points on it and at intersections with other constraints.</summary>
            void CheckLocked((Pt a, Pt b, int marker, int sourceId, bool locked) s, int end, Dictionary<int, int> previous)
            {
                var inputs = new HashSet<Pt>(inPts);
                for (int at = end; at >= 0; at = previous[at]) {
                    if (inputs.Contains(v[at])) continue;
                    int vertex = at;
                    bool onOther = Enumerable.Range(0, sources.Count).Any(k => MayBeNear(k, vertex) && !Collinear(s.a, s.b, sources[k].a, sources[k].b) && WithinTube(sources[k], v[vertex]));
                    if (!onOther) r.Add(Violation.LockedSplit, "locked segment " + s.a + " - " + s.b + " is split at " + v[at]);
                }
            }

            /// <summary>
            /// The parts of a-b in the closed domain, cut at all ring edges. With a filtered list of rings, either allRings is the full
            /// list, or farGap is a positive lower bound of the distance from a-b to every ring edge not in the list.
            /// </summary>
            List<(Pt from, Pt to)> RetainedPieces(Pt a, Pt b, List<(Pt a, Pt b)> rings, List<(Pt a, Pt b)> allRings = null, double farGap = double.PositiveInfinity)
            {
                if (allRings != null) farGap = double.PositiveInfinity;
                allRings = allRings ?? rings;
                var pieces = new List<(Pt, Pt)>();
                Q ax = a.Qx, ay = a.Qy, ux = b.Qx - ax, uy = b.Qy - ay, uu = ux * ux + uy * uy;
                var ts = new List<Q> { Q.Zero, Q.One };
                // (a filter in doubles on the fast path: a ring edge whose ends lie surely on one side of a-b's line, or a-b's ends surely
                // on one side of its line, neither crosses nor overlaps a-b; poly2tri's debug2 has thousands of ring edges near each
                // source, all exact before)
                var ad = PtD(a); var bd = PtD(b);
                bool filterOn = !e.Reference && !double.IsNaN(ad.x) && !double.IsNaN(bd.x);
                foreach (var ring in rings) {
                    if (filterOn) {
                        var cd = PtD(ring.a); var dd = PtD(ring.b);
                        if (SurelyOneSide(ad, bd, cd, dd) || SurelyOneSide(cd, dd, ad, bd)) continue;
                    }
                    Q cx = ring.a.Qx, cy = ring.a.Qy, wx = ring.b.Qx - cx, wy = ring.b.Qy - cy;
                    Q denom = ux * wy - uy * wx;
                    Q acx = cx - ax, acy = cy - ay;
                    if (denom.Sign == 0) {
                        if ((acx * uy - acy * ux).Sign != 0) continue; // parallel
                        foreach (var q in new[] { (acx * ux + acy * uy) / uu, ((acx + wx) * ux + (acy + wy) * uy) / uu })
                            if (q.Sign > 0 && q < Q.One) ts.Add(q);
                        continue;
                    }
                    Q t = (acx * wy - acy * wx) / denom, w = (acx * uy - acy * ux) / denom;
                    if (t.Sign > 0 && t < Q.One && w.Sign >= 0 && w <= Q.One) ts.Add(t);
                }
                ts.Sort();
                Q half = new Q(1, 2);
                Q? start = null, end = null;
                for (int i = 0; i + 1 < ts.Count; ++i) {
                    if (ts[i] == ts[i + 1]) continue;
                    Q m = (ts[i] + ts[i + 1]) * half;
                    var mid = new Pt(ax + ux * m, ay + uy * m);
                    var where = domain.Classify(mid);
                    bool keep = where == Where.Inside || (where == Where.Boundary && InteriorBeside(mid, ux, uy, allRings, farGap));
                    if (keep) {
                        if (start == null) start = ts[i];
                        end = ts[i + 1];
                    } else if (start != null) {
                        pieces.Add((new Pt(ax + ux * start.Value, ay + uy * start.Value), new Pt(ax + ux * end.Value, ay + uy * end.Value)));
                        start = null;
                    }
                }
                if (start != null) pieces.Add((new Pt(ax + ux * start.Value, ay + uy * start.Value), new Pt(ax + ux * end.Value, ay + uy * end.Value)));
                return pieces;
            }

            void Delaunay()
            {
                // O2: conforming is always on when quality is on. Not across a locked segment, which may not be split to get there (O9).
                bool quality = e.Milestone == Milestone.M3 && (input.MinAngleDeg != null || input.MaxArea != null || input.SizeFunction != null);
                // At the Steiner limit refinement stops with a valid mesh whose quality is not guaranteed (O7); conforming is part of
                // that quality (O2), so the mesh is judged as a constrained Delaunay triangulation then (found with M3, F-22/a, F-27, F-42).
                // the constrained quality mode asks for constrained Delaunay only (owner decision 2026-11-02)
                bool conforming = (input.Conforming || quality) && !input.ConstrainedQuality && !(e.Milestone == Milestone.M3 && o.SteinerLimitReached);
                foreach (var pair in edges) {
                    var uses = pair.Value;
                    if (uses.Count != 2) continue;
                    bool constrained = constraintEdges.Contains(pair.Key);
                    if (constrained && (!conforming || OnLocked(pair.Key))) continue;
                    int a = uses[0].a, b = uses[0].b;
                    int c = o.Triangles[uses[0].tri][uses[0].opposite], d = o.Triangles[uses[1].tri][uses[1].opposite];
                    int sign = InCircle(a, b, c, d);
                    if (sign > 0 && !(conforming && (ExcusedOffSegment(a, b, c, d) || Zone(new[] { a, b, c }, Q.One) || (constrained && (GridUnsplittableSubsegment(a, b) || O11UnsplittableSubsegment(a, b)))))) r.Add(Violation.NotDelaunayLocal, "edge " + a + "-" + b + " is not locally Delaunay against " + d);
                    if (sign == 0 && !constrained) { r.TieEdges++; r.Unique = false; }
                }
                long work = (long)o.Triangles.Length * v.Length;
                bool anyLocked = sources.Any(s => s.locked);
                // Every triangle against every vertex. On the fast path with a grid over the vertices: only those in the cells the
                // circumcircle's box meets can be inside it, so the pass runs at every size (the reference path only up to the limit).
                // The local criterion alone cannot replace it: the Delaunay lemma (every edge locally Delaunay, then the triangulation is
                // Delaunay) is about a triangulation of the convex hull, and O2 asks for empty circles against all vertices, also across
                // holes and concave boundaries, where the output has no triangles.
                bool wanted = e.Milestone == Milestone.M1 || (conforming && !anyLocked);
                if (wanted && (Fast ? e.GlobalDelaunayLimit > 0 : work <= e.GlobalDelaunayLimit)) {
                    r.GlobalDelaunayChecked = true;
                    var used = new HashSet<int>(o.Triangles.SelectMany(t => t));
                    if (Fast) {
                        var ids = used.ToArray();
                        // the witness (MESHING2D_FAST_ORACLE.md section 6): the pass on the output triangles that are not triangles of the
                        // oracle's own certified Delaunay triangulation of the used vertices; the full pass when it cannot be had
                        var outside = OutsideDelaunayWitness(ids, out string why);
                        LastGlobalPassForTests = outside == null ? "fallback: " + why : "witness";
                        // (check mode: both into scratch reports, which keep 200 violations each, as every report: a difference after
                        // the 200th global report would go unseen there; the tests' meshes stay far below)
                        if (outside != null && CheckFastPathForTests) {
                            var keep = r;
                            r = new OracleReport(); GlobalDelaunayIndexed(ids, conforming, null); var full = r;
                            r = new OracleReport(); GlobalDelaunayIndexed(ids, conforming, outside); var part = r;
                            r = keep;
                            var fm = full.Violations.Where(x => x.Kind == Violation.NotDelaunayGlobal).Select(x => x.Detail);
                            var pm = part.Violations.Where(x => x.Kind == Violation.NotDelaunayGlobal).Select(x => x.Detail);
                            if (!fm.SequenceEqual(pm) || full.MergeZoneExcused != part.MergeZoneExcused || full.O11GlobalExcused != part.O11GlobalExcused)
                                FastPathDiffers("the global Delaunay pass on the triangles outside the witness: " + part + " (" + part.MergeZoneExcused + ", "
                                    + part.O11GlobalExcused + ") against the full pass: " + full + " (" + full.MergeZoneExcused + ", " + full.O11GlobalExcused + ")");
                        }
                        GlobalDelaunayIndexed(ids, conforming, outside);
                    }
                    else
                        foreach (var t in o.Triangles) {
                            o11Pending = 0;
                            bool flagged = false;
                            foreach (int id in used) {
                                if (id == t[0] || id == t[1] || id == t[2]) continue;
                                if (InCircle(t[0], t[1], t[2], id) > 0 && !(conforming && (ExcusedOffSegment(t[0], t[1], t[2], id) || Zone(t, Q.One) || HiddenByGridUnsplittable(t, id) || HiddenByO11Unsplittable(t, id)))) {
                                    r.Add(Violation.NotDelaunayGlobal, "vertex " + id + " inside the circumcircle of " + t[0] + " " + t[1] + " " + t[2]);
                                    flagged = true;
                                    break;
                                }
                            }
                            if (!flagged) r.O11GlobalExcused += o11Pending;
                        }
                }
            }

            /// <summary>
            /// The exact in-circle sign, with a floating-point filter in front when the vertices are the doubles: the determinant
            /// in doubles with a bound on its rounding error (10 roundings in the differences, products and sums give less than
            /// 1.2e-15 times the permanent; the bound used is ten times that), and the exact test when the filter cannot decide.
            /// </summary>
            int InCircle(int a, int b, int c, int d)
            {
                if (Fast) {
                    double dx = o.VX[d], dy = o.VY[d];
                    double adx = o.VX[a] - dx, ady = o.VY[a] - dy, bdx = o.VX[b] - dx, bdy = o.VY[b] - dy, cdx = o.VX[c] - dx, cdy = o.VY[c] - dy;
                    double al = adx * adx + ady * ady, bl = bdx * bdx + bdy * bdy, cl = cdx * cdx + cdy * cdy;
                    double bc = bdx * cdy - cdx * bdy, ca = cdx * ady - adx * cdy, ab = adx * bdy - bdx * ady;
                    double det = al * bc + bl * ca + cl * ab;
                    double permanent = (Math.Abs(bdx * cdy) + Math.Abs(cdx * bdy)) * al + (Math.Abs(cdx * ady) + Math.Abs(adx * cdy)) * bl
                        + (Math.Abs(adx * bdy) + Math.Abs(bdx * ady)) * cl;
                    double bound = 1.2e-14 * permanent;
                    // (only where nothing underflowed or overflowed: the lifted terms and the six products zero or of normal magnitude
                    // far from both ends, so that no rounding is absolute; found by the bounds review)
                    bool Ok(double x) => x == 0 || (Math.Abs(x) > 1e-150 && Math.Abs(x) < 1e150);
                    bool terms = Ok(al) && Ok(bl) && Ok(cl) && Ok(bdx * cdy) && Ok(cdx * bdy) && Ok(cdx * ady) && Ok(adx * cdy) && Ok(adx * bdy) && Ok(bdx * ady);
                    if (terms && permanent > 1e-200 && permanent < 1e200 && Math.Abs(det) > bound) return det > 0 ? 1 : -1;
                }
                return Exact.InCircle(v[a], v[b], v[c], v[d]);
            }

            /// <summary>
            /// The global pass with a 2-d tree over the used vertices (adaptive: refinement makes the density very uneven). Per triangle
            /// the circumcircle in doubles, its radius widened by 1e-6 of itself and 1e-12 of the coordinates' magnitude: enough when
            /// the triangle is not nearly degenerate (its orientation determinant above 1e-6 of the sum of its terms) and not a needle
            /// (its shortest edge above 1e-8 of its longest): the centre's error is then about u (|b| + |q|) / |b - q| of the radius,
            /// below 1e-7. A nearly degenerate triangle or a needle is checked against all vertices. Every candidate in the circle's box
            /// goes to the filtered exact in-circle test; one report per triangle, as on the reference path. Only the triangles in subset
            /// when it is given (the triangles outside the witness, in order), all of them otherwise.
            /// </summary>
            void GlobalDelaunayIndexed(int[] used, bool conforming, List<int> subset)
            {
                double magnitude = 0;
                foreach (int id in used) magnitude = Math.Max(magnitude, Math.Max(Math.Abs(o.VX[id]), Math.Abs(o.VY[id])));
                var tree = (int[])used.Clone();
                BuildTree(tree, 0, tree.Length, 0);
                foreach (int ti in subset ?? Enumerable.Range(0, o.Triangles.Length)) {
                    var t = o.Triangles[ti];
                    o11Pending = 0;
                    bool flagged = false;
                    // t's merge zone depends on t alone: decided once per triangle, counted per vertex it excuses (a thin triangle at an
                    // unsplittable piece of the Herne tiles has thousands of vertices in its circumcircle; each recomputed it)
                    bool? inZone = null;
                    bool ZoneOnce()
                    {
                        if (inZone == null) inZone = InMergeZone(t, Q.One);
                        if (inZone.Value) r.MergeZoneExcused++;
                        return inZone.Value;
                    }
                    bool Check(int id)
                    {
                        if (id == t[0] || id == t[1] || id == t[2]) return false;
                        if (InCircle(t[0], t[1], t[2], id) > 0 && !(conforming && (ExcusedOffSegment(t[0], t[1], t[2], id) || ZoneOnce() || HiddenByGridUnsplittable(t, id) || HiddenByO11Unsplittable(t, id)))) {
                            r.Add(Violation.NotDelaunayGlobal, "vertex " + id + " inside the circumcircle of " + t[0] + " " + t[1] + " " + t[2]);
                            flagged = true;
                            return true;
                        }
                        return false;
                    }
                    double ax = o.VX[t[0]], ay = o.VY[t[0]];
                    double bx = o.VX[t[1]] - ax, by = o.VY[t[1]] - ay, qx = o.VX[t[2]] - ax, qy = o.VY[t[2]] - ay;
                    double l = bx * qy, rr = by * qx, d = l - rr, sum = Math.Abs(l) + Math.Abs(rr);
                    bool scan = !(sum > 1e-200 && sum < 1e200) || !(Math.Abs(d) > 1e-6 * sum);
                    double b2 = bx * bx + by * by, q2 = qx * qx + qy * qy, e2 = (qx - bx) * (qx - bx) + (qy - by) * (qy - by);
                    // the centre's relative error grows like u (|b| + |q|) / |b - q| and its kin: a needle is scanned
                    if (!(Math.Min(b2, Math.Min(q2, e2)) > 1e-16 * Math.Max(b2, Math.Max(q2, e2)))) scan = true;
                    double x0 = 0, x1 = 0, y0 = 0, y1 = 0;
                    if (!scan) {
                        double ux = (qy * b2 - by * q2) / (2 * d), uy = (bx * q2 - qx * b2) / (2 * d);
                        double reach = Math.Sqrt(ux * ux + uy * uy) * (1 + 1e-6) + magnitude * 1e-12;
                        double cx = ax + ux, cy = ay + uy;
                        if (double.IsNaN(reach) || double.IsInfinity(reach)) scan = true;
                        else { x0 = cx - reach; x1 = cx + reach; y0 = cy - reach; y1 = cy + reach; }
                    }
                    if (scan) { foreach (int id in used) if (Check(id)) break; }
                    else QueryTree(tree, 0, tree.Length, 0, x0, x1, y0, y1, Check);
                    if (!flagged) r.O11GlobalExcused += o11Pending;
                }
            }

            /// <summary> a 2-d tree in place: the median of [from, to) by x (depth even) or y at the middle, both halves below it </summary>
            void BuildTree(int[] ids, int from, int to, int depth)
            {
                if (to - from <= 8) return;
                int mid = (from + to) / 2;
                var vals = depth % 2 == 0 ? o.VX : o.VY;
                Array.Sort(ids, from, to - from, Comparer<int>.Create((p, q) => vals[p] != vals[q] ? vals[p].CompareTo(vals[q]) : p.CompareTo(q)));
                BuildTree(ids, from, mid, depth + 1);
                BuildTree(ids, mid + 1, to, depth + 1);
            }

            /// <summary> visits the ids in the box until visit returns true; returns whether it did </summary>
            bool QueryTree(int[] ids, int from, int to, int depth, double x0, double x1, double y0, double y1, Func<int, bool> visit)
            {
                if (to - from <= 8) {
                    for (int i = from; i < to; ++i) {
                        int id = ids[i];
                        if (o.VX[id] >= x0 && o.VX[id] <= x1 && o.VY[id] >= y0 && o.VY[id] <= y1 && visit(id)) return true;
                    }
                    return false;
                }
                int mid = (from + to) / 2, m = ids[mid];
                double key = depth % 2 == 0 ? o.VX[m] : o.VY[m], lo = depth % 2 == 0 ? x0 : y0, hi = depth % 2 == 0 ? x1 : y1;
                if (o.VX[m] >= x0 && o.VX[m] <= x1 && o.VY[m] >= y0 && o.VY[m] <= y1 && visit(m)) return true;
                if (lo <= key && QueryTree(ids, from, mid, depth + 1, x0, x1, y0, y1, visit)) return true;
                if (hi >= key && QueryTree(ids, mid + 1, to, depth + 1, x0, x1, y0, y1, visit)) return true;
                return false;
            }

            /// <summary>
            /// O2: "tiny in-circle violations caused only by a vertex placed on a segment lying a few ulps off it (collinear
            /// within about 8 ulps) are acceptable". The violation is excused only if it is that one: it disappears when the
            /// constructed vertices are put back exactly on their segments.
            /// </summary>
            bool ExcusedOffSegment(int a, int b, int c, int d)
            {
                var corrected = new[] { v[a], v[b], v[c], v[d] };
                bool any = false;
                for (int k = 0; k < 4; ++k) {
                    int id = new[] { a, b, c, d }[k];
                    var put = OnSegmentAgain(id);
                    if (put == null) continue;
                    corrected[k] = put.Value;
                    any = true;
                }
                if (!any) return false;
                return Exact.InCircle(corrected[0], corrected[1], corrected[2], corrected[3]) * Exact.Orient(corrected[0], corrected[1], corrected[2]) <= 0;
            }

            /// <summary> constructed vertex id put back exactly on the first source within 8 ulps of it, or null; once per vertex (the
            /// global pass asks for a triangle's corners again for every vertex in its circumcircle) </summary>
            Pt? OnSegmentAgain(int id)
            {
                onSegmentMemo = onSegmentMemo ?? new Dictionary<int, Pt?>();
                if (onSegmentMemo.TryGetValue(id, out var known)) return known;
                Pt? result = null;
                if (!(o.IdentityRules ? id < input.Count : IsInputPoint(v[id]))) {
                    Q ulp = new Q(BigInteger.One, BigInteger.One << 49) * Q.Max(Q.Abs(v[id].Qx), Q.Abs(v[id].Qy)); // 8 ulps = 2^3 * 2^-52
                    // (where the tube is at least 8 ulps, a source farther than the tube cannot be within 8 ulps: the box filter applies)
                    bool boxed = tube2 >= ulp * ulp;
                    for (int ks = 0; ks < sources.Count; ++ks) {
                        if (boxed && !MayBeNear(ks, id)) continue;
                        var s = sources[ks];
                        Q d2 = Exact.Dist2ToSegment(s.a, s.b, v[id]);
                        if (d2.Sign == 0 || d2 > ulp * ulp) continue;
                        Q ax = s.a.Qx, ay = s.a.Qy, ux = s.b.Qx - ax, uy = s.b.Qy - ay;
                        Q t = ((v[id].Qx - ax) * ux + (v[id].Qy - ay) * uy) / (ux * ux + uy * uy);
                        result = new Pt(ax + ux * t, ay + uy * t);
                        break;
                    }
                }
                onSegmentMemo[id] = result;
                return result;
            }
            Dictionary<int, Pt?> onSegmentMemo;

            HashSet<Pt> inputSet;
            bool IsInputPoint(Pt p) => (inputSet ?? (inputSet = new HashSet<Pt>(inPts))).Contains(p);

            bool OnLocked(long key)
            {
                Pt a = v[KeyA(key)], b = v[KeyB(key)];
                return sources.Any(s => s.locked && WithinTube(s, a) && WithinTube(s, b));
            }

            void Quality()
            {
                if (input.SteinerLimit != null && r.ConstructedVertices > input.SteinerLimit.Value)
                    r.Add(Violation.SteinerLimitExceeded, r.ConstructedVertices + " constructed, limit " + input.SteinerLimit.Value);
                if (o.SteinerLimitReached) {
                    // the limit lifts all bounds, so it must not be free to claim. It counts points that are not in the final mesh too (O7),
                    // so not all of it shows in the output: half of it must. The default limit is that of Q13.
                    long limit = input.SteinerLimit ?? 10L * (input.Count + input.Segments.Count + input.Rings.Sum(g => g.Vertices.Length)) + 100000;   // ring edges are segments too
                    if (e.Milestone != Milestone.M3) r.Add(Violation.SteinerFlagWrong, "Steiner limit reported without refinement");
                    else if (r.ConstructedVertices * 2L < limit) r.Add(Violation.SteinerFlagWrong, "Steiner limit reached with " + r.ConstructedVertices + " constructed vertices, limit " + limit);
                }
                if (o.BadTriangles != null) {
                    // a list is a set of existing triangles; without a bound nothing can be below it (second review, finding 7)
                    if (o.BadTriangles.Distinct().Count() != o.BadTriangles.Length) r.Add(Violation.BadListWrong, "a triangle is listed twice");
                    if (input.MinAngleDeg == null && o.BadTriangles.Length > 0) r.Add(Violation.BadListWrong, o.BadTriangles.Length + " triangles listed below no minimum angle");
                }
                if (input.MinAngleDeg != null && !e.InvalidAngle) AngleBound(input.MinAngleDeg.Value);
                if (e.MinTriangles != null && o.Triangles.Length < e.MinTriangles.Value) r.Add(Violation.CountOutOfRange, o.Triangles.Length + " triangles, at least " + e.MinTriangles.Value + " required");
                // merged input slots stay in the list as aliases (Q8) but are no vertices of the mesh, so they do not count
                if (e.MaxVertices != null && v.Length - aliases > e.MaxVertices.Value) r.Add(Violation.CountOutOfRange, (v.Length - aliases) + " vertices, at most " + e.MaxVertices.Value + " allowed");
                var labelArea = RegionAreaBounds();
                bool anySize = input.MaxArea != null || input.SizeFunction != null || labelArea != null;
                if (!anySize || o.SteinerLimitReached) return;
                Q? maxArea = input.MaxArea == null ? (Q?)null : Q.FromDouble(input.MaxArea.Value);
                for (int i = 0; i < o.Triangles.Length; ++i) {
                    var t = o.Triangles[i];
                    Pt a = v[t[0]], b = v[t[1]], c = v[t[2]];
                    Q area = Exact.Area2(a, b, c) * new Q(1, 2);
                    Q? bound = maxArea;
                    if (labelArea != null && labelArea[i] != null) bound = bound == null ? labelArea[i] : Q.Min(bound.Value, labelArea[i].Value);
                    Q locked2 = LongestLockedEdge2(t);
                    // O5, O9: a triangle over an edge that may not be split cannot shrink below a needle. Exempt while its height is at
                    // most half of that edge (area < L^2 / 4); beyond that a vertex would shrink it without a needle. A choice, not a spec value.
                    bool areaExempt = locked2 > area * 4;
                    if (bound != null && area > bound.Value && !areaExempt) r.Add(Violation.AreaAboveBound, "triangle " + t[0] + " " + t[1] + " " + t[2] + " has area " + area);
                    if (input.SizeFunction != null) {
                        Pt centroid = Centroid(t);
                        Q h = input.SizeFunction(centroid.Qx, centroid.Qy);
                        if (Q.Max(Exact.Len2(a, b), Q.Max(Exact.Len2(b, c), Exact.Len2(c, a))) > Q.Max(h * h, locked2))
                            r.Add(Violation.SizeAboveBound, "triangle " + t[0] + " " + t[1] + " " + t[2] + " has an edge longer than the size " + h);
                    }
                }
            }

            Q LongestLockedEdge2(int[] t)
            {
                Q best = Q.Zero;
                for (int k = 0; k < 3; ++k) {
                    long key = Key(t[(k + 1) % 3], t[(k + 2) % 3]);
                    if (constraintEdges.Contains(key) && OnLocked(key)) best = Q.Max(best, Exact.Len2(v[KeyA(key)], v[KeyB(key)]));
                }
                return best;
            }

            bool TouchesLocked(int[] t)
            {
                for (int k = 0; k < 3; ++k) {
                    long key = Key(t[(k + 1) % 3], t[(k + 2) % 3]);
                    if (!constraintEdges.Contains(key)) continue;
                    Pt a = v[KeyA(key)], b = v[KeyB(key)];
                    if (sources.Any(s => s.locked && WithinTube(s, a) && WithinTube(s, b))) return true;
                }
                return false;
            }

            void AngleBound(double degrees)
            {
                // the threshold is the sine of an angle in double; the slack is far above the runtime differences of Math.Sin
                double s = Math.Sin(degrees * Math.PI / 180);
                Q slack = new Q(1, BigInteger.One << 30);
                Q nominal = Q.FromDouble(s * s), low = nominal - slack, high = nominal + slack;
                var listed = o.BadTriangles == null ? null : new HashSet<int>(o.BadTriangles);
                if (listed == null && o.IdentityRules) r.Add(Violation.MissingReport, "no list of triangles below the minimum angle (Q11)");
                if (listed != null)
                    foreach (int id in listed)
                        if (id < 0 || id >= o.Triangles.Length) r.Add(Violation.BadListWrong, "listed triangle " + id + " does not exist");
                var around = ConstraintsAround();
                bool thresholds = low.Bounds(out double lowLo, out double lowHi) & high.Bounds(out double highLo, out double highHi);
                for (int i = 0; i < o.Triangles.Length; ++i) {
                    var t = o.Triangles[i];
                    // below the bound, and at or above the upper threshold: decided on the filtered interval where it lies clear of both
                    bool below, atHigh;
                    if (thresholds && MinSin2Bounds(t[0], t[1], t[2], out double sLo, out double sHi) && (sHi < lowLo || (sLo > lowHi && (sLo > highHi || sHi < highLo)))) {
                        below = sHi < lowLo;
                        atHigh = !below && sLo > highHi;
                    } else {
                        Q sin2 = Exact.MinAngleSin2(v[t[0]], v[t[1]], v[t[2]]);
                        below = sin2 < low;
                        atHigh = sin2 >= high;
                    }
                    if (below) r.BelowAngle++;
                    if (listed != null) {
                        if (below && !listed.Contains(i)) r.Add(Violation.BadListWrong, "triangle " + i + " is below the angle but not listed");
                        if (!below && atHigh && listed.Contains(i)) r.Add(Violation.BadListWrong, "triangle " + i + " is listed but not below the angle");
                    }
                    if (!below || o.SteinerLimitReached || !e.AngleGuaranteed) continue;
                    if (ForcedByInputAngle(t, low, around) || TouchesLocked(t) || SpansSmallInputAngle(t, SmallPairSin2(slack)) || AtPrecisionLimit(t) || Zone(t, low)) continue;
                    if (BelowGridFloor(t) || GridBlocked(t) || CircumcentreBehindGridUnsplittable(t) || CircumcentreBehindO11Unsplittable(t)) continue;
                    r.Add(Violation.AngleBelowBound, "triangle " + t[0] + " " + t[1] + " " + t[2] + " is below " + degrees + " degrees with no forced small angle");
                }
            }

            /// <summary>
            /// The sine squared below which two input segments count as a small input angle for the excuse of <see cref="SpansSmallInputAngle(int[], Q)"/>:
            /// that of 60 degrees less the slack, whatever the angle bound (owner decision 2026-11-09). Refinement towards the apex of an
            /// input angle below 60 degrees may not terminate (Shewchuk 2002, section 6.2: Ruppert's and Chew's proofs need input angles of
            /// at least 60 degrees), so the mesher stops there and the oracle excuses the triangles it leaves, as for an angle below the bound
            /// </summary>
            static Q SmallPairSin2(Q slack) => new Q(3, 4) - slack;

            Dictionary<int, List<int>> ConstraintsAround()
            {
                var around = new Dictionary<int, List<int>>();
                foreach (long key in constraintEdges) {
                    AddAdjacent(around, KeyA(key), KeyB(key));
                    AddAdjacent(around, KeyB(key), KeyA(key));
                }
                // without an outer ring the hull is the boundary (D13), constrained or not: its corners force their angles too
                // (second review, finding 2)
                if (domain.IsHull)
                    foreach (var pair in edges)
                        if (pair.Value.Count == 1 && !constraintEdges.Contains(pair.Key)) {
                            AddAdjacent(around, KeyA(pair.Key), KeyB(pair.Key));
                            AddAdjacent(around, KeyB(pair.Key), KeyA(pair.Key));
                        }
                return around;
            }

            /// <summary>
            /// O3: the small angle is forced only if the triangle's small corner lies in a wedge of two constraints that meet at an angle
            /// below the bound. Touching such a vertex with some other corner does not count.
            /// </summary>
            bool ForcedByInputAngle(int[] t, Q sin2Low, Dictionary<int, List<int>> around)
            {
                for (int k = 0; k < 3; ++k) {
                    int corner = t[k];
                    if (!around.TryGetValue(corner, out var list)) continue;
                    Pt p = v[corner], x = v[t[(k + 1) % 3]], y = v[t[(k + 2) % 3]];
                    Q cornerSin2 = CornerSin2(p, x, y);
                    if (cornerSin2 >= sin2Low || Dot(p, x, y).Sign <= 0) continue; // this corner is not the small one
                    for (int i = 0; i < list.Count; ++i)
                        for (int j = 0; j < list.Count; ++j) {
                            if (i == j) continue;
                            Pt a = v[list[i]], b = v[list[j]];
                            // a to b counter-clockwise, below the bound, with x and y inside the wedge from a to b
                            if (Exact.Orient(p, a, b) <= 0 || Dot(p, a, b).Sign <= 0 || CornerSin2(p, a, b) >= sin2Low) continue;
                            if (Exact.Orient(p, a, x) >= 0 && Exact.Orient(p, x, b) >= 0 && Exact.Orient(p, a, y) >= 0 && Exact.Orient(p, y, b) >= 0
                                && AtLeastShewchukBound(p, x, y, p, a, b) && AtLeastShewchukBound(x, p, y, p, a, b) && AtLeastShewchukBound(y, x, p, p, a, b)) return true;
                        }
                }
                return false;
            }

            /// <summary>
            /// the angle of a triangle at corner q (between q-r and q-s) is at least arctan(sin phi / (2 - cos phi)), phi the input angle
            /// at apex between apex-a and apex-b. Provenance (corrected 2026-11-06): this is the bound of Miller, Pav and Walkington
            /// ("When and why Delaunay refinement algorithms work", IJCGA 2005) for every angle of a triangle near a small input angle phi,
            /// as stated in their abstract; not yet checked against the full text. It is not Shewchuk's: Shewchuk 2002 (Theorem 12, the
            /// Terminator) proves the weaker arcsin(sin(phi/2) / sqrt 2), which this check does not use. A triangle excused for its small angle at the apex must meet it at all three corners
            /// (at the apex too: a triangle of the apex with its two neighbours on the segments has the input angle there, and a needle
            /// inside the wedge is no forced angle). Exact: with u = a - apex, w = b - apex, tan bound = |u x w| / (2 |u| |w| - u.w), and for an acute angle at q,
            /// tan angle = cross / dot, so the angle is at least the bound iff 2 cross |u| |w| >= |u x w| dot + (u.w) cross, squared
            /// when the right side is positive
            /// </summary>
            internal static bool AtLeastShewchukBound(Pt q, Pt r, Pt s, Pt apex, Pt a, Pt b)
            {
                Q dot = Dot(q, r, s);
                if (dot.Sign <= 0) return true;   // a right or obtuse angle is above any bound below 60 degrees
                Q cross = Q.Abs(Exact.Area2(q, r, s));
                Q wedgeCross = Q.Abs(Exact.Area2(apex, a, b)), wedgeDot = Dot(apex, a, b);
                Q right = wedgeCross * dot + wedgeDot * cross;
                if (right.Sign <= 0) return true;
                return 4 * cross * cross * Exact.Len2(apex, a) * Exact.Len2(apex, b) >= right * right;
            }

            /// <summary>
            /// The merge zone's radius in tube / sin(angle). The mesher refuses split points within 1 of it (O11); concentric shells put
            /// the first point beyond at less than 2, and an input vertex may lie anywhere. The corpus's 19 excused triangles (5 apexes,
            /// 0.84 to 20.6 degrees) reach at most 2.14 (an input vertex); 3 keeps a margin and leaves anything farther judged. Was 8.
            /// </summary>
            const int ZoneFactor = 3;

            /// <summary>
            /// A branch u of a and a branch w of c leave the apex with a positive dot product, and every vertex of t lies within
            /// ZoneFactor tube of the apex or has a positive dot product with both u and w
            /// </summary>
            bool AcuteSide(Pt apex, (Pt a, Pt b, int marker, int sourceId, bool locked) a, (Pt a, Pt b, int marker, int sourceId, bool locked) c, int[] t)
            {
                Q near2 = tube2 * (ZoneFactor * ZoneFactor);
                Q Dot(Pt o, Pt x, Pt y) => (x.Qx - o.Qx) * (y.Qx - o.Qx) + (x.Qy - o.Qy) * (y.Qy - o.Qy);
                foreach (var u in new[] { a.a, a.b })
                    foreach (var w in new[] { c.a, c.b }) {
                        if (u.Equals(apex) || w.Equals(apex) || Dot(apex, u, w).Sign <= 0) continue;
                        if (t.All(i => Exact.Len2(apex, v[i]) <= near2 || (Dot(apex, u, v[i]).Sign > 0 && Dot(apex, w, v[i]).Sign > 0))) return true;
                    }
                return false;
            }

            /// <summary> InMergeZone, counted in the report when it excuses </summary>
            bool Zone(int[] t, Q sin2Low)
            {
                if (!InMergeZone(t, sin2Low)) return false;
                r.MergeZoneExcused++;
                return true;
            }

            /// <summary>
            /// O11 and O3: near the apex of two input segments that meet at an angle below the bound, the segments are within the tube of
            /// each other up to tube / sin(angle) from it; a point placed there merges their chains (they count as one), so refinement
            /// cannot improve a triangle all of whose vertices lie within ZoneFactor tube / sin(angle) of the apex (found with the corpus
            /// run, earcut's water-huge: segments at 0.84 degrees, triangles of 2e-7 at the apex). The same holds for conforming (O2):
            /// there the bound is any angle (sin2Low = 1), the zone shrinking to ZoneFactor tube at right angles (found by the indexed
            /// global pass).
            /// </summary>
            bool InMergeZone(int[] t, Q sin2Low)
            {
                if (tube2.Sign == 0) return false;
                // the hull edges of a hull domain count without an explicit snap tolerance: the mesher refuses a split point of a hull
                // edge within the tube of a segment (NearOtherSource, O11), not the reverse, and two hull edges never (found with the
                // Herne tiles: a segment at 0.67 degrees to a hull edge, a hull piece at the apex left unsplit; the first review of the
                // issue-211 fix had removed the hull from the zone, seeing only the reverse). On the grid no split point is refused this
                // way (ValidGridSplit decides).
                var hull = input.SnapTolerance > 0 ? new List<(Pt a, Pt b, int marker, int sourceId, bool locked)>() : HullSources();
                var near = new List<(Pt a, Pt b, int marker, int sourceId, bool locked)>();
                // (on the fast path the sources from the grid over their boxes widened by the tube, queried with the triangle's box, and
                // each decided by NearSource, the filter in doubles before the exact test: the same list, in the same order, as the scan of
                // all sources with the exact test, which the reference path keeps; the exact distances were most of the angle check)
                if (!e.Reference && t.All(i => vKnown[i])) {
                    if (sourceGridForZones == null) sourceGridForZones = new BoxGrid(sBox, sources.Count);
                    double xl = t.Min(i => vxl[i]), xh = t.Max(i => vxh[i]), yl = t.Min(i => vyl[i]), yh = t.Max(i => vyh[i]);
                    var candidates = sourceGridForZones.Query(xl, xh, yl, yh);
                    if (CheckFastPathForTests) BoxGrid.CheckSuperset(sBox, sources.Count, xl, xh, yl, yh, candidates, "the sources near a triangle");
                    foreach (int k in candidates)
                        if (t.Any(i => NearSource(k, i))) near.Add(sources[k]);
                    if (CheckFastPathForTests) {
                        var scan = Enumerable.Range(0, sources.Count).Where(k => t.Any(i => WithinTube(sources[k], v[i]))).Select(k => sources[k]).ToList();
                        if (!scan.SequenceEqual(near)) FastPathDiffers("the sources near a triangle (merge zone)");
                    }
                } else
                    for (int k = 0; k < sources.Count; ++k)
                        if (t.Any(i => MayBeNear(k, i) && WithinTube(sources[k], v[i]))) near.Add(sources[k]);
                for (int k = 0; k < hull.Count; ++k)
                    if (t.Any(i => NearHull(k, i) && WithinTube(hull[k], v[i]))) near.Add(hull[k]);
                double tube2Hi = double.PositiveInfinity;
                if (Fast && tube2.Bounds(out _, out double t2h)) tube2Hi = t2h;
                // (the bound's sine squared from above, for the filter on the pair's sine below: no pair whose sine is surely above it
                // can qualify)
                double sin2LowHi = double.PositiveInfinity;
                if (Fast && sin2Low.Bounds(out _, out double s2h)) sin2LowHi = s2h;
                foreach (var a in near) {
                    var da = Fast ? Direction(a) : default((double x, double y));
                    for (int kc = 0; kc < sources.Count + hull.Count; ++kc) {
                        bool cHull = kc >= sources.Count;
                        var c = cHull ? hull[kc - sources.Count] : sources[kc];
                        if (a.sourceId == c.sourceId) continue;
                        if (cHull && a.sourceId >= HullSourceIds) continue;
                        // (a filter: the zone is at most ZoneFactor tube / sin away from the apex, and the apex lies on c; a source whose box,
                        // widened by that for the smallest sine possible here, misses the vertex cannot qualify. The sine is at least
                        // sin(a, c) and the pairs with a large sine are rejected below anyway, so the widening uses the double sine of the
                        // pair with a factor 2 of slack.)
                        double[] box = cHull ? hullBox : sBox; int kb = cHull ? kc - sources.Count : kc;
                        if (Fast && !double.IsInfinity(tube2Hi) && !double.IsNaN(box[4 * kb]) && vKnown[t[0]]) {
                            // (the directions in doubles, once per source: they were converted from the rationals for every pair and
                            // triangle, most of the angle check's time on the Herne tiles)
                            var dc = cHull ? Direction(c) : SourceDirection(kc);
                            // (the directions normalised to a largest coordinate of 1 first: the product of their squared lengths underflowed
                            // at directions of 1e-81 and gave an infinite sine, which skipped every pair; review)
                            double na = Math.Max(Math.Abs(da.x), Math.Abs(da.y)), nc = Math.Max(Math.Abs(dc.x), Math.Abs(dc.y));
                            double ax1 = da.x / na, ay1 = da.y / na, cx1 = dc.x / nc, cy1 = dc.y / nc;
                            double sinD = Math.Abs(ax1 * cy1 - ay1 * cx1) / Math.Sqrt((ax1 * ax1 + ay1 * ay1) * (cx1 * cx1 + cy1 * cy1));
                            // (a lower bound of the sine: the double sine has an absolute error far below 1e-13; nearly parallel sources
                            // are not filtered; found by the bounds review)
                            double sinLow = sinD - 1e-13;
                            if (sinLow > 0 && !double.IsNaN(sinD) && !double.IsInfinity(sinD) && na > 0 && nc > 0 && !double.IsInfinity(na) && !double.IsInfinity(nc)) {
                                // a sine surely at or above the bound's: the exact test below rejects the pair (sin2 >= sin2Low)
                                if (sinLow * sinLow * (1 - 1e-12) > sin2LowHi) continue;
                                double reach = 2 * ZoneFactor * Math.Sqrt(tube2Hi) / sinLow;
                                if (vxh[t[0]] < box[4 * kb] - reach || vxl[t[0]] > box[4 * kb + 1] + reach || vyh[t[0]] < box[4 * kb + 2] - reach || vyl[t[0]] > box[4 * kb + 3] + reach) continue;
                            }
                        }
                        // the pair's sine, zone and apex depend on the pair only: computed once (most of the angle check's remaining
                        // time on the Herne tiles, where many nearly parallel sources pass the filters above)
                        var pair = Pair(a, c);
                        if (!pair.crosses) continue;
                        if (pair.sin2 >= sin2Low) continue;
                        Q zone2 = pair.zone2;
                        // (depends on the pair only: before the distances, which it would otherwise follow; the conditions are all exits)
                        if (!pair.apexOnBoth) continue;
                        // (a filter in doubles both ways: the vertex surely farther from c than the zone's radius, or surely nearer; the
                        // distance's rounding is far below 2^-40 of the coordinates' magnitude, the slack allowed. All of the Herne tiles'
                        // 625 000 exact tests here came out nearer)
                        int f = 0;
                        if (Fast && vKnown[t[0]] && pair.zoneHi < double.PositiveInfinity) {
                            var ca = PtD(c.a); var cb = PtD(c.b);
                            f = ZoneFilter(ca.x, ca.y, cb.x, cb.y, o.VX[t[0]], o.VY[t[0]], pair.zoneLo, pair.zoneHi);
                        }
                        var vc = c; var vt = v[t[0]];
                        if (CheckFastPathForTests && f != 0 && (f > 0) != (Exact.Dist2ToSegment(vc.a, vc.b, vt) <= zone2)) FastPathDiffers("a vertex's distance to the merge zone's source");
                        if (!WithinZone(f, () => Exact.Dist2ToSegment(vc.a, vc.b, vt) <= zone2)) continue;
                        var apex = pair.apex;
                        if (!t.All(i => ApexWithinZone(apex, i, pair))) continue;
                        // the small angle is that between a branch of a and a branch of c leaving the apex (their directions with a
                        // positive dot product), and the triangle lies on its side: on the obtuse side (eg past a collinear split point
                        // of one source, or at a corner of nearly 180 degrees) the sources are not within the tube of each other, and the
                        // mesher refuses nothing there (review of the hull pairing); a vertex within ZoneFactor tube of the apex counts
                        // on either side
                        if (AcuteSide(apex, a, c, t)) return true;
                    }
                }
                return false;
            }

            Dictionary<(int, int), (bool crosses, Q sin2, Q zone2, double zoneHi, double zoneLo, bool apexOnBoth, Pt apex)> pairs;

            /// <summary> vertex i lies within the zone's radius of the apex (|apex - v_i|^2 &lt;= zone2), with the filter in doubles of the zone
            /// test in front both ways (the apex rounded to doubles moves by an ulp of its magnitude, inside the slack) </summary>
            bool ApexWithinZone(Pt apex, int i, (bool crosses, Q sin2, Q zone2, double zoneHi, double zoneLo, bool apexOnBoth, Pt apex) pair)
            {
                if (Fast && vKnown[i] && pair.zoneHi < double.PositiveInfinity) {
                    var ad = PtD(apex);
                    // (a point is the segment from it to itself)
                    int f = ZoneFilter(ad.x, ad.y, ad.x, ad.y, o.VX[i], o.VY[i], pair.zoneLo, pair.zoneHi);
                    if (CheckFastPathForTests && f != 0 && (f > 0) != (Exact.Len2(apex, v[i]) <= pair.zone2)) FastPathDiffers("a vertex's distance to the merge zone's apex");
                    return WithinZone(f, () => Exact.Len2(apex, v[i]) <= pair.zone2);
                }
                return Exact.Len2(apex, v[i]) <= pair.zone2;
            }
            BoxGrid sourceGridForZones;

            static (double x, double y) PtD(Pt p) => (ToD(p.Qx), ToD(p.Qy));

            /// <summary> the filters in doubles run only for magnitudes in [1e-140, 1e140]: no square of a difference or of a length
            /// overflows, none falls into the subnormals (review: Dist2D overflowed at 1.3e154 and the filter rejected a hit) </summary>
            static bool FilterRange(double mag) => mag > 1e-140 && mag < 1e140;

            double tubeHi = double.NaN;

            /// <summary>
            /// p lies within the tube of segment a-b (exact). On the fast path a filter in doubles first: a point surely farther than the
            /// tube is rejected without the exact test (the coordinates round by up to 2^-53 of the largest, the distance a little more;
            /// 1e-12 of it is the slack)
            /// </summary>
            bool InTube(Pt a, Pt b, Pt p)
            {
                if (!e.Reference) {
                    if (double.IsNaN(tubeHi)) tubeHi = tube2.Bounds(out _, out double t2h) && !double.IsInfinity(t2h) ? Math.Sqrt(t2h) * (1 + 1e-12) : double.PositiveInfinity;
                    if (!double.IsInfinity(tubeHi)) {
                        var ad = PtD(a); var bd = PtD(b); var pd = PtD(p);
                        double mag = Math.Max(Math.Max(Math.Max(Math.Abs(ad.x), Math.Abs(ad.y)), Math.Max(Math.Abs(bd.x), Math.Abs(bd.y))), Math.Max(Math.Abs(pd.x), Math.Abs(pd.y)));
                        double low = Math.Sqrt(Dist2D(ad, bd, pd.x, pd.y)) - mag * 1e-12;
                        if (!double.IsNaN(low) && FilterRange(mag) && low > tubeHi) return false;
                    }
                }
                return Exact.Dist2ToSegment(a, b, p) <= tube2;
            }

            /// <summary> c and d lie surely strictly on one side of the line through a and b (doubles with a generous error bound; false
            /// when unsure) </summary>
            static bool SurelyOneSide((double x, double y) a, (double x, double y) b, (double x, double y) c, (double x, double y) d)
            {
                double ux = b.x - a.x, uy = b.y - a.y, c1x = c.x - a.x, c1y = c.y - a.y, d1x = d.x - a.x, d1y = d.y - a.y;
                double l1 = ux * c1y, r1 = uy * c1x, l2 = ux * d1y, r2 = uy * d1x;
                double o1 = l1 - r1, o2 = l2 - r2;
                // (the coordinates are the exact points rounded once, by up to 2^-53 of the largest coordinate M; each difference is then
                // off by at most 3 such roundings, each product by that times the other factor, and the products and the difference
                // round once more: 1e-12 of M times the factors, plus 1e-12 of the terms, is far above it)
                double big = Math.Max(Math.Max(Math.Max(Math.Abs(a.x), Math.Abs(a.y)), Math.Max(Math.Abs(b.x), Math.Abs(b.y))), Math.Max(Math.Max(Math.Abs(c.x), Math.Abs(c.y)), Math.Max(Math.Abs(d.x), Math.Abs(d.y))));
                double delta = big * 1e-12;
                double e1 = delta * (Math.Abs(ux) + Math.Abs(uy) + Math.Abs(c1x) + Math.Abs(c1y) + delta) + (Math.Abs(l1) + Math.Abs(r1)) * 1e-12 + double.Epsilon;
                double e2 = delta * (Math.Abs(ux) + Math.Abs(uy) + Math.Abs(d1x) + Math.Abs(d1y) + delta) + (Math.Abs(l2) + Math.Abs(r2)) * 1e-12 + double.Epsilon;
                if (double.IsNaN(o1) || double.IsNaN(o2) || double.IsInfinity(e1) || double.IsInfinity(e2)) return false;
                return (o1 > e1 && o2 > e2) || (o1 < -e1 && o2 < -e2);
            }

            /// <summary>
            /// The radius sqrt(r2) between lo and hi in doubles: hi from above (infinite when unknown), lo from below (0 when unknown).
            /// Bounds gives r2 within 1e-13 relative; the square root halves that, and its own rounding is far inside the factors 1 +- 1e-12
            /// </summary>
            internal static void RadiusBounds(Q r2, out double lo, out double hi)
            {
                bool known = r2.Bounds(out double l2, out double h2) && !double.IsInfinity(h2);
                hi = known ? Math.Sqrt(Math.Max(0, h2)) * (1 + 1e-12) : double.PositiveInfinity;
                lo = known && l2 > 0 ? Math.Sqrt(l2) * (1 - 1e-12) : 0;
            }

            /// <summary>
            /// The double filter of "point p within radius r of segment a-b" (a == b: of a point), r given by its bounds lo &lt;= r &lt;= hi:
            /// -1 certainly farther, +1 certainly within, 0 undecided (the exact test decides). The inputs are the doubles of the exact
            /// points or within an ulp of them; the distance in doubles is then within 1e-12 of the largest magnitude m of the
            /// exact one (the rounding of the projection and the square root is a few ulps of m), which is the slack on either side.
            /// Magnitudes in [1e-140, 1e140] only (no square overflows or falls into the subnormals)
            /// </summary>
            internal static int ZoneFilter(double ax, double ay, double bx, double by, double px, double py, double lo, double hi)
            {
                if (double.IsNaN(ax) || double.IsNaN(ay) || double.IsNaN(bx) || double.IsNaN(by) || double.IsInfinity(hi)) return 0;
                double m = Math.Max(Math.Max(Math.Abs(px), Math.Abs(py)), Math.Max(Math.Max(Math.Abs(ax), Math.Abs(ay)), Math.Max(Math.Abs(bx), Math.Abs(by))));
                if (!FilterRange(m)) return 0;
                double dist = Math.Sqrt(Dist2D((ax, ay), (bx, by), px, py));
                if (dist - m * 1e-12 > hi) return -1;
                if (dist + m * 1e-12 < lo) return 1;
                return 0;
            }

            /// <summary> the decision of a zone test: the filter's where it decides (-1 farther, +1 within), else the exact test's </summary>
            internal static bool WithinZone(int filter, Func<bool> exact) => filter != 0 ? filter > 0 : exact();

            /// <summary> squared distance in doubles from (px, py) to segment ab (a filter only) </summary>
            static double Dist2D((double x, double y) a, (double x, double y) b, double px, double py)
            {
                double ux = b.x - a.x, uy = b.y - a.y, l2 = ux * ux + uy * uy;
                double t = l2 > 0 ? ((px - a.x) * ux + (py - a.y) * uy) / l2 : 0;
                t = Math.Max(0, Math.Min(1, t));
                double dx = a.x + t * ux - px, dy = a.y + t * uy - py;
                return dx * dx + dy * dy;
            }

            /// <summary> for sources a and c: whether their lines cross, the sine squared of their angle, the merge zone's radius squared
            /// (ZoneFactor tube / sin)^2, and their crossing point if it lies on both (exact; memoized per pair of source ids) </summary>
            (bool crosses, Q sin2, Q zone2, double zoneHi, double zoneLo, bool apexOnBoth, Pt apex) Pair((Pt a, Pt b, int marker, int sourceId, bool locked) a, (Pt a, Pt b, int marker, int sourceId, bool locked) c)
            {
                pairs = pairs ?? new Dictionary<(int, int), (bool, Q, Q, double, double, bool, Pt)>();
                if (pairs.TryGetValue((a.sourceId, c.sourceId), out var known)) return known;
                Q d1x = a.b.Qx - a.a.Qx, d1y = a.b.Qy - a.a.Qy, d2x = c.b.Qx - c.a.Qx, d2y = c.b.Qy - c.a.Qy;
                Q cross = d1x * d2y - d1y * d2x;
                (bool, Q, Q, double, double, bool, Pt) result;
                if (cross.Sign == 0) result = (false, Q.Zero, Q.Zero, double.PositiveInfinity, 0, false, default(Pt));
                else {
                    Q sin2 = cross * cross / ((d1x * d1x + d1y * d1y) * (d2x * d2x + d2y * d2y));
                    Q zone2 = tube2 * (ZoneFactor * ZoneFactor) / sin2;   // (ZoneFactor tube / sin)^2
                    Q t1 = ((c.a.Qx - a.a.Qx) * d2y - (c.a.Qy - a.a.Qy) * d2x) / cross, t2 = ((c.a.Qx - a.a.Qx) * d1y - (c.a.Qy - a.a.Qy) * d1x) / cross;
                    bool onBoth = !(t1.Sign < 0 || t1 > Q.One || t2.Sign < 0 || t2 > Q.One);
                    // (the zone's radius from above in doubles, for the filter; infinite when it cannot be bounded)
                    // (and from below, for the filter's other side; 0 when it cannot be bounded, so that nothing is surely within)
                    RadiusBounds(zone2, out double zoneLo, out double zoneHi);
                    result = (true, sin2, zone2, zoneHi, zoneLo, onBoth, onBoth ? new Pt(a.a.Qx + t1 * d1x, a.a.Qy + t1 * d1y) : default(Pt));
                }
                pairs[(a.sourceId, c.sourceId)] = result;
                return result;
            }

            (double x, double y)[] sourceDirections;

            /// <summary> source k's direction in doubles (fast path only; the filters around it allow for its rounding) </summary>
            (double x, double y) SourceDirection(int k)
            {
                if (sourceDirections == null) {
                    sourceDirections = new (double, double)[sources.Count];
                    for (int i = 0; i < sources.Count; ++i) sourceDirections[i] = Direction(sources[i]);
                }
                return sourceDirections[k];
            }

            static (double x, double y) Direction((Pt a, Pt b, int marker, int sourceId, bool locked) s) => (ToD(s.b.Qx - s.a.Qx), ToD(s.b.Qy - s.a.Qy));

            /// <summary>
            /// Spec 1.4: a triangle that refinement cannot improve because of floating-point precision stays as it is. Its shortest edge is
            /// at most 64 units in the last place of its largest coordinate (found with M3: three segments through one point that is no
            /// double give crossings a few ulps apart).
            /// </summary>
            bool AtPrecisionLimit(int[] t)
            {
                Q big = Q.Zero, shortest = Q.Zero; bool first = true;
                for (int k = 0; k < 3; ++k) {
                    Pt p = v[t[k]];
                    big = Q.Max(big, Q.Max(Q.Abs(p.Qx), Q.Abs(p.Qy)));
                    Q l = Exact.Len2(v[t[(k + 1) % 3]], v[t[(k + 2) % 3]]);
                    if (first || l < shortest) { shortest = l; first = false; }
                }
                Q ulp64 = big * new Q(BigInteger.One, BigInteger.One << 46);   // 64 ulps = 2^6 * 2^-52
                return shortest <= ulp64 * ulp64;
            }

            /// <summary>
            /// The shortest edge of t spans a small input angle: its ends lie in the tubes of two input constraints that meet on both of
            /// them at an angle below 60 degrees (SmallPairSin2), at distances from the apex within a factor of 1.5, on the same side of it. Such a triangle
            /// cannot be improved without refining towards the apex for ever (Shewchuk 2002, small input angles; plan, section 6). Found
            /// with M3 at inputs where several segments cross at small angles (F-4, F-39).
            /// </summary>
            bool SpansSmallInputAngle(int[] t, Q sin2Low)
            {
                // the shortest edge; on a tie every shortest edge is tried, since both opposite corners are the smallest angle and a
                // mesher may look at either (found with the grid fuzz of m2.snap: lattice triangles with two equal short edges)
                var lengths = Enumerable.Range(0, 3).Select(k => Exact.Len2(v[t[(k + 1) % 3]], v[t[(k + 2) % 3]])).ToArray();
                Q best = lengths.Aggregate(Q.Min);
                for (int k0 = 0; k0 < 3; ++k0)
                    if (!(lengths[k0] > best) && SpansSmallInputAngle(t[(k0 + 1) % 3], t[(k0 + 2) % 3], sin2Low)) return true;
                return false;
            }

            bool SpansSmallInputAngle(int ip, int iq, Q sin2Low)
            {
                Pt p = v[ip], q = v[iq];
                var sp = Enumerable.Range(0, sources.Count).Where(k => MayBeNear(k, ip) && WithinTube(sources[k], p)).Select(k => sources[k]).ToList();
                var sq = Enumerable.Range(0, sources.Count).Where(k => MayBeNear(k, iq) && WithinTube(sources[k], q)).Select(k => sources[k]).ToList();
                var hull = HullSources();
                for (int k = 0; k < hull.Count; ++k) {
                    // (a box filter in doubles first: a large hull is scanned per bad triangle; the exact test decides)
                    if (NearHull(k, ip) && WithinTube(hull[k], p)) sp.Add(hull[k]);
                    if (NearHull(k, iq) && WithinTube(hull[k], q)) sq.Add(hull[k]);
                }
                foreach (var a in sp)
                    foreach (var c in sq) {
                        if (a.sourceId == c.sourceId) continue;
                        Q d1x = a.b.Qx - a.a.Qx, d1y = a.b.Qy - a.a.Qy, d2x = c.b.Qx - c.a.Qx, d2y = c.b.Qy - c.a.Qy;
                        Q cross = d1x * d2y - d1y * d2x;
                        if (cross.Sign == 0) continue;
                        Q sin2 = cross * cross / ((d1x * d1x + d1y * d1y) * (d2x * d2x + d2y * d2y));
                        if (sin2 >= sin2Low) continue;
                        Q t1 = ((c.a.Qx - a.a.Qx) * d2y - (c.a.Qy - a.a.Qy) * d2x) / cross, t2 = ((c.a.Qx - a.a.Qx) * d1y - (c.a.Qy - a.a.Qy) * d1x) / cross;
                        if (t1.Sign < 0 || t1 > Q.One || t2.Sign < 0 || t2 > Q.One) continue;
                        var apex = new Pt(a.a.Qx + t1 * d1x, a.a.Qy + t1 * d1y);
                        Q rp = Exact.Len2(apex, p), rq = Exact.Len2(apex, q);
                        if (rp.Sign == 0 || rq.Sign == 0 || rp * 4 > rq * 9 || rq * 4 > rp * 9) continue;
                        if (Dot(apex, p, q).Sign <= 0) continue;
                        return true;
                    }
                return false;
            }

            static Q Dot(Pt p, Pt a, Pt b) => (a.Qx - p.Qx) * (b.Qx - p.Qx) + (a.Qy - p.Qy) * (b.Qy - p.Qy);

            static Q CornerSin2(Pt p, Pt a, Pt b)
            {
                Q area2 = Exact.Area2(p, a, b);
                return area2 * area2 / (Exact.Len2(p, a) * Exact.Len2(p, b));
            }

            /// <summary>The label each triangle must carry: the last matching region wins (I6, F-45).</summary>
            int[] ExpectedLabels(out Q?[] areas)
            {
                var labels = new int[o.Triangles.Length];
                areas = new Q?[labels.Length];
                var cell = CellsAcrossFreeEdges();
                var centroids = o.Triangles.Select(Centroid).ToArray();
                foreach (var region in input.Regions) {
                    Q? bound = region.MaxArea == null ? (Q?)null : Q.FromDouble(region.MaxArea.Value);
                    if (region.Ring != null) {
                        var ring = region.Ring.Select(i => inPts[i]).ToArray();
                        for (int i = 0; i < labels.Length; ++i)
                            if (Domain.InRing(ring, centroids[i], !e.Reference) == Where.Inside) { labels[i] = region.Label; areas[i] = bound; }
                    } else {
                        // on a constraint or a vertex the seed is ambiguous and ignored (decision D6)
                        Pt seed = Pt.FromDoubles(region.Seed[0], region.Seed[1]);
                        if (OnConstraintOrVertex(seed)) continue;
                        int found = -1, count = 0, foundCell = -1;
                        for (int i = 0; i < labels.Length; ++i) {
                            var t = o.Triangles[i];
                            if (Exact.Orient(v[t[0]], v[t[1]], seed) >= 0 && Exact.Orient(v[t[1]], v[t[2]], seed) >= 0 && Exact.Orient(v[t[2]], v[t[0]], seed) >= 0) {
                                if (foundCell != cell[i]) ++count;
                                found = i; foundCell = cell[i];
                            }
                        }
                        if (found < 0 || count != 1) continue;
                        for (int i = 0; i < labels.Length; ++i)
                            if (cell[i] == foundCell) { labels[i] = region.Label; areas[i] = bound; }
                    }
                }
                return labels;
            }

            bool OnConstraintOrVertex(Pt seed)
            {
                foreach (long key in constraintEdges)
                    if (Exact.OnClosedSegment(v[KeyA(key)], v[KeyB(key)], seed)) return true;
                // an input point, not a vertex the refinement placed: that one cannot make the seed ambiguous (found with M3, F-24/b)
                return inPts.Any(p => p.Equals(seed));
            }

            int[] CellsAcrossFreeEdges()
            {
                var parent = Enumerable.Range(0, o.Triangles.Length).ToArray();
                int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                foreach (var pair in edges)
                    if (pair.Value.Count == 2 && !constraintEdges.Contains(pair.Key))
                        parent[Find(pair.Value[0].tri)] = Find(pair.Value[1].tri);
                return parent.Select((_, i) => Find(i)).ToArray();
            }

            Q?[] RegionAreaBounds()
            {
                if (input.Regions.Count == 0 || input.Regions.All(x => x.MaxArea == null)) return null;
                ExpectedLabels(out var areas);
                return areas;
            }

            void Regions()
            {
                if (input.Regions.Count == 0) return;
                if (o.TriangleLabels == null) { if (o.IdentityRules) r.Add(Violation.MissingReport, "no triangle labels (spec R3)"); return; }
                if (o.TriangleLabels.Length != o.Triangles.Length) { r.Add(Violation.RegionLabelWrong, "one label per triangle"); return; }
                var expected = ExpectedLabels(out _);
                int reports = 0;
                for (int i = 0; i < expected.Length; ++i)
                    if (expected[i] != o.TriangleLabels[i] && reports++ < 8)
                        r.Add(Violation.RegionLabelWrong, "triangle " + i + " has label " + o.TriangleLabels[i] + ", expected " + expected[i]);
            }

            /// <summary>Each subsegment comes from a constraint and carries its marker: the sole nonzero marker, 0 on a conflict (R8, decision D7).</summary>
            void Marks()
            {
                if (o.Subsegments == null) return;
                // on the fast path the sources come from a grid over their boxes widened by the tube, queried with the box of the
                // subsegment's first end: a superset, in ascending order, of the sources whose box meets it, and MayBeNear drops only
                // sources certainly farther than the tube, so `covering` is the list of the full scan (as in InventedVertices)
                var grid = e.Reference ? null : new BoxGrid(sBox, sources.Count);
                foreach (var s in o.Subsegments) {
                    var candidates = grid != null && vKnown[s.A] ? grid.Query(vxl[s.A], vxh[s.A], vyl[s.A], vyh[s.A]) : Enumerable.Range(0, sources.Count).ToList();
                    if (CheckFastPathForTests && grid != null && vKnown[s.A]) BoxGrid.CheckSuperset(sBox, sources.Count, vxl[s.A], vxh[s.A], vyl[s.A], vyh[s.A], candidates, "the sources at a subsegment's end");
                    var covering = candidates.Where(k => NearSource(k, s.A) && NearSource(k, s.B)).Select(k => sources[k]).ToList();
                    if (CheckFastPathForTests && !covering.SequenceEqual(sources.Where(c => WithinTube(c, v[s.A]) && WithinTube(c, v[s.B]))))
                        FastPathDiffers("the sources covering subsegment " + s.A + "-" + s.B);
                    if (covering.Count == 0) {
                        bool onHull = domain.IsHull && DomainEdgesNear(v[s.A], tube2).Any(h => Exact.Dist2ToSegment(h.a, h.b, v[s.A]) <= tube2 && Exact.Dist2ToSegment(h.a, h.b, v[s.B]) <= tube2);
                        if (!onHull) r.Add(Violation.SubsegmentUnsourced, "subsegment " + s.A + "-" + s.B + " lies on no input constraint");
                        continue;
                    }
                    var markers = covering.Select(c => c.marker).Where(m => m != 0).Distinct().ToList();
                    int expected = markers.Count == 1 ? markers[0] : 0;
                    if (s.Marker != expected) r.Add(Violation.SubsegmentMarker, "subsegment " + s.A + "-" + s.B + " has marker " + s.Marker + ", expected " + expected);
                }
            }

            void Locate()
            {
                if (o.Locate == null) return;
                var probes = new List<double[]>();
                foreach (var t in o.Triangles.Take(100)) {
                    double cx = (o.VX[t[0]] + o.VX[t[1]] + o.VX[t[2]]) / 3, cy = (o.VY[t[0]] + o.VY[t[1]] + o.VY[t[2]]) / 3;
                    probes.Add(new[] { cx, cy });
                    probes.Add(new[] { o.VX[t[0]], o.VY[t[0]] });
                    probes.Add(new[] { (o.VX[t[0]] + o.VX[t[1]]) / 2, (o.VY[t[0]] + o.VY[t[1]]) / 2 });
                }
                double far = 2 * Math.Max(o.VX.Max(Math.Abs), o.VY.Max(Math.Abs)) + 1;
                if (far <= 1e70) probes.Add(new[] { far, far }); // beyond the accepted range a query is an argument error
                foreach (var p in probes) {
                    int got = o.Locate(p[0], p[1]);
                    var q = Pt.FromDoubles(p[0], p[1]);
                    if (got < 0) {
                        if (LocateExact(q) >= 0) r.Add(Violation.LocateWrong, "(" + p[0] + ", " + p[1] + ") is in the mesh, location says outside");
                        continue;
                    }
                    if (got >= o.Triangles.Length) { r.Add(Violation.LocateWrong, "location returned " + got); continue; }
                    var t = o.Triangles[got];
                    if (!(Exact.Orient(v[t[0]], v[t[1]], q) >= 0 && Exact.Orient(v[t[1]], v[t[2]], q) >= 0 && Exact.Orient(v[t[2]], v[t[0]], q) >= 0))
                        r.Add(Violation.LocateWrong, "(" + p[0] + ", " + p[1] + ") is not in triangle " + got);
                }
            }
        }
    }

    /// <summary>
    /// Edge keys (a &lt;&lt; 32 | b) hashed with mixing: the default hash of a long is its two halves xor-ed, a ^ b, which puts the
    /// edges of a large mesh into few buckets.
    /// </summary>
    sealed class EdgeKeys : IEqualityComparer<long>
    {
        public static readonly EdgeKeys Instance = new EdgeKeys();
        public bool Equals(long x, long y) => x == y;
        public int GetHashCode(long x) => (int)((ulong)(x * unchecked((long)0x9E3779B97F4A7C15UL)) >> 32);
    }
}
