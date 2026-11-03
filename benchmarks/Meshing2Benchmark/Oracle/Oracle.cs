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
    public static class Oracle
    {
        /// <summary>
        /// The automatic tolerance a result may use: 2^-40 of the larger of the largest coordinate and the extent, about 5
        /// micrometres at UTM northings (spec O11: "about 5 um at UTM scale"; Q14).
        /// </summary>
        public static Q AutoTolerance(Mesh2Input input)
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
            return new Run(input, o, e).Execute();
        }

        /// <summary> on this thread, every check takes the reference path (the tests run the self-checks so; --oracle reference) </summary>
        [ThreadStatic] public static bool ForceReference;

        /// <summary> the double filter of "vertex within the tube of a source", for the self-checks: -1 farther, +1 within, 0 undecided </summary>
        public static int TubeFilter(double ax, double ay, double bx, double by, double px, double py, double tube2Lo, double tube2Hi) => Run.TubeFilter(ax, ay, bx, by, px, py, tube2Lo, tube2Hi);
        /// <summary> self-checks: <see cref="Run.AtLeastShewchukBound"/> </summary>
        internal static bool AtLeastShewchukBound(Pt q, Pt r, Pt s, Pt apex, Pt a, Pt b) => Run.AtLeastShewchukBound(q, r, s, apex, a, b);

        sealed class Run
        {
            readonly Mesh2Input input; readonly Mesh2Output o; readonly OracleExpect e;
            readonly OracleReport r = new OracleReport();
            Pt[] inPts, v;
            Domain domain;
            Q tube, tube2;
            int[] map;
            Dictionary<long, List<(int tri, int opposite, int a, int b)>> edges;
            HashSet<long> constraintEdges;
            List<(Pt a, Pt b, int marker, int sourceId, bool locked)> sources;
            HashSet<long> subsegmentSet;
            Q moved2 = Q.Zero;                 // the largest squared distance by which geometry really moved; 0 if nothing did
            int aliases;                       // merged input slots (identity rules): vertices in the list that are no vertices of the mesh
            List<Pt> effectiveSeeds = new List<Pt>();

            public Run(Mesh2Input input, Mesh2Output o, OracleExpect e) { this.input = input; this.o = o; this.e = e; }

            static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            static int KeyA(long k) => (int)(k >> 32);
            static int KeyB(long k) => (int)(uint)k;

            public OracleReport Execute()
            {
                if (!CheckInput()) return r;
                if (o == null) { r.Add(Violation.StatusInconsistent, "no result"); return r; }
                if (o.Status == Mesh2Status.Failed || e.ExpectFailure) {
                    if (!e.ExpectFailure) r.Add(Violation.StatusInconsistent, "Failed on an input with a required result: " + o.FailureKind);
                    else if (o.Status != Mesh2Status.Failed) r.Add(Violation.StatusInconsistent, "a mesh instead of the required failure status");
                    if (o.Status == Mesh2Status.Failed && o.Triangles.Length != 0) r.Add(Violation.StatusInconsistent, "Failed with triangles");
                    return r;
                }
                if (!Vertices()) return r;
                domain = new Domain(input, !e.Reference);
                bool degenerate = domain.Area.Sign == 0;
                tube = e.Tube ?? (e.Milestone == Milestone.M1 ? Q.Zero : Q.Max(Oracle.AutoTolerance(input), Q.FromDouble(input.SnapTolerance)));
                tube2 = tube * tube;
                if (!Indices()) return r;
                if (!Identity()) return r;
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
                var watch = Environment.GetEnvironmentVariable("G3_ORACLE_TIMING") != null ? System.Diagnostics.Stopwatch.StartNew() : null;
                void Lap(string name) { if (watch != null) { Console.Error.WriteLine("ORACLE " + name + " " + watch.ElapsedMilliseconds + " ms"); watch.Restart(); } }
                Orientation(); Lap("orientation");
                if (!r.Ok) return r; // the rest assumes positive triangles
                EdgesAndNeighbours(); Lap("edges");
                Sources();
                Filters();
                Conformity(); Lap("conformity");
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
                for (int i = 0; i < v.Length; ++i) if (Index(v[i].Qx) == kx && Index(v[i].Qy) == ky && AtPixelCentre(v[i], v[i], pixel)) return true;
                Q x0 = kx * pixel - half, x1 = kx * pixel + half, y0 = ky * pixel - half, y1 = ky * pixel + half;
                foreach (var s in o.Subsegments) {
                    Pt p = v[s.A], q = v[s.B];
                    if (Q.Max(p.Qx, q.Qx) <= x0 || Q.Min(p.Qx, q.Qx) >= x1 || Q.Max(p.Qy, q.Qy) <= y0 || Q.Min(p.Qy, q.Qy) >= y1) continue;
                    int pos = 0, neg = 0;
                    foreach (var corner in new[] { new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1) }) {
                        int or = Exact.Orient(p, q, corner);
                        if (or > 0) ++pos; else if (or < 0) ++neg;
                    }
                    if (pos > 0 && neg > 0) return true;
                }
                return false;
            }

            Dictionary<long, bool> unsplittableMemo;

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
                foreach (var s in o.Subsegments) {
                    if (s.A == skip || s.B == skip) continue;
                    Pt sa = v[s.A], sb = v[s.B];
                    int o1 = Exact.Orient(sa, sb, g), o2 = Exact.Orient(sa, sb, p);
                    if (o1 * o2 >= 0 || Exact.Orient(g, p, sa) * Exact.Orient(g, p, sb) > 0) continue;
                    if (unsplittable(s.A, s.B)) return true;
                }
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
                Q tol2 = tube2 / 4, ownTol2 = tol2 * new Q(BigInteger.One + (BigInteger.One << 7), BigInteger.One << 7), half2 = tol2 * new Q((BigInteger.One << 7) - BigInteger.One, BigInteger.One << 7);
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

            /// <summary> the ends of each segment that lie within the tube of the other are more than one point (nearly coincident lines) </summary>
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
                // its input constraints: those whose envelope holds both ends
                var own = sources.Where(s => InEnvelopeOf(s.a, s.b, pa, half) && InEnvelopeOf(s.a, s.b, pb, half)).ToList();
                if (own.Count == 0) return false;
                Q Index(Q x) { Q q = x / pixel + new Q(1, 2); var f = BigInteger.Divide(q.N, q.D); if (q.N.Sign < 0 && f * q.D != q.N) f -= 1; return new Q(f, 1); }
                Q kx0 = Index(Q.Min(pa.Qx, pb.Qx)) - 1, kx1 = Index(Q.Max(pa.Qx, pb.Qx)) + 1, ky0 = Index(Q.Min(pa.Qy, pb.Qy)) - 1, ky1 = Index(Q.Max(pa.Qy, pb.Qy)) + 1;
                if ((kx1 - kx0) * (ky1 - ky0) > new Q(1 << 16, 1)) return false;   // (too long to enumerate: judged as splittable)
                var subs = (o.Subsegments ?? new List<SubsegmentOutput>()).Where(g => !((g.A == ia && g.B == ib) || (g.A == ib && g.B == ia))).ToList();
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
                for (Q kx = kx0; kx <= kx1; kx = kx + 1)
                    for (Q ky = ky0; ky <= ky1; ky = ky + 1) {
                        var q = new Pt(kx * pixel, ky * pixel);
                        if (Same(q, pa) || Same(q, pb)) continue;
                        Q tq = Along(q);
                        if (!(tq > lo && tq < hi)) continue;
                        if (!own.All(s => CrossesOpen(s.a, s.b, q, narrow) || OnSegment(s.a, s.b, q))) continue;
                        bool ok = true;
                        foreach (var g in subs) {
                            Pt c = v[g.A], d = v[g.B];
                            if (!Same(c, q) && !Same(d, q) && CrossesOpen(c, d, q, wide)) { ok = false; break; }
                            if (Exact.ProperCross(pa, q, c, d) || Exact.ProperCross(q, pb, c, d)) { ok = false; break; }
                        }
                        if (!ok) continue;
                        for (int i = 0; i < v.Length && ok; ++i) {
                            if (Same(v[i], pa) || Same(v[i], pb) || Same(v[i], q)) continue;
                            var pv = new Pt(Snap(v[i].Qx), Snap(v[i].Qy));
                            if (CrossesOpen(pa, q, pv, wide) || CrossesOpen(q, pb, pv, wide)) ok = false;
                        }
                        if (ok) return false;
                    }
                return true;
            }

            /// <summary>
            /// Input point i lies on a hull tip that collapsed on the grid (D14; owner decision 2026-11-02, MESHING2D_HULL_GRID.md
            /// section 7, Q-H4): at some corner of a hull domain's hull (D13) both hull edges pass one pixel, so snap rounding routes
            /// both edges' chains through its vertex and the tip between has no area. Point i is that corner, with some other used
            /// output vertex in such a pixel, or i's own pixel is such a pixel (it lies on the shared chain). Passing a pixel is decided
            /// as for grid splits: the edge crosses its interior, a hair smaller, or passes its centre (the half-open pixels of snap
            /// rounding; a corner on a pixel boundary is not excused; review). Only with an explicit tolerance at M3, where the mesher
            /// snap-rounds the hull edges; the mesher reports such vertices in its diagnostics.
            /// </summary>
            bool CollapsedHullTip(int i, HashSet<int> usedVertices)
            {
                if (!(input.SnapTolerance > 0) || !domain.IsHull || e.Milestone != Milestone.M3) return false;
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

            /// <summary>Calls back for each pair of boxes whose closed ranges overlap, by a sweep over x.</summary>
            static void OverlapPairs(int[][] boxesA, int[][] boxesB, bool same, Action<int, int> hit)
            {
                // box = {xlo, xhi, ylo, yhi}
                var events = new List<(int x, int kind, int id)>(boxesA.Length + boxesB.Length);
                for (int i = 0; i < boxesA.Length; ++i) events.Add((boxesA[i][0], 0, i));
                if (!same) for (int i = 0; i < boxesB.Length; ++i) events.Add((boxesB[i][0], 1, i));
                events.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.kind != q.kind ? p.kind.CompareTo(q.kind) : p.id.CompareTo(q.id));
                var activeA = new List<int>(); var activeB = new List<int>();
                foreach (var ev in events) {
                    var mine = ev.kind == 0 ? boxesA[ev.id] : boxesB[ev.id];
                    var others = same ? activeA : (ev.kind == 0 ? activeB : activeA);
                    var otherBoxes = same || ev.kind == 1 ? boxesA : boxesB;
                    for (int i = others.Count - 1; i >= 0; --i) {
                        int id = others[i];
                        var box = otherBoxes[id];
                        if (box[1] < ev.x) { others[i] = others[others.Count - 1]; others.RemoveAt(others.Count - 1); continue; }
                        if (box[3] >= mine[2] && box[2] <= mine[3]) {
                            if (ev.kind == 0) hit(ev.id, id); else hit(id, ev.id);
                        }
                    }
                    (ev.kind == 0 ? activeA : activeB).Add(ev.id);
                }
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
                int reports = 0;
                OverlapPairs(triBoxes, vertexBoxes, false, (ti, vi) => {
                    var t = o.Triangles[ti];
                    int id = used[vi];
                    if (id == t[0] || id == t[1] || id == t[2]) return;
                    if (OrientV(t[0], t[1], id) >= 0 && OrientV(t[1], t[2], id) >= 0 && OrientV(t[2], t[0], id) >= 0 && reports++ < 8)
                        r.Add(Violation.VertexInTriangle, "vertex " + id + " " + v[id] + " touches triangle " + t[0] + " " + t[1] + " " + t[2]);
                });
                var keys = edges.Keys.ToArray();
                var edgeBoxes = keys.Select(k => new[] {
                    Math.Min(rx[KeyA(k)], rx[KeyB(k)]), Math.Max(rx[KeyA(k)], rx[KeyB(k)]),
                    Math.Min(ry[KeyA(k)], ry[KeyB(k)]), Math.Max(ry[KeyA(k)], ry[KeyB(k)]) }).ToArray();
                reports = 0;
                OverlapPairs(edgeBoxes, edgeBoxes, true, (i, j) => {
                    int a = KeyA(keys[i]), b = KeyB(keys[i]), c = KeyA(keys[j]), d = KeyB(keys[j]);
                    if (a == c || a == d || b == c || b == d) return;
                    if (ProperCrossV(a, b, c, d) && reports++ < 8)
                        r.Add(Violation.EdgesCross, "edges " + a + "-" + b + " and " + c + "-" + d + " cross");
                });
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
                    if (InClosure(inPts[i]) && !CollapsedHullTip(i, usedSet)) r.Add(Violation.VertexUnused, "input " + i + " " + inPts[i] + " is in the domain but in no triangle");
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
                double reach = double.NaN;
                if (tube2.Bounds(out _, out double t2hi)) reach = Math.Sqrt(t2hi) * (1 + 1e-12) + 1e-300;
                sBox = new double[4 * sources.Count];
                for (int k = 0; k < sources.Count; ++k) SegmentBox(sources[k].a, sources[k].b, reach, sBox, 4 * k);
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
            static void SegmentBox(Pt a, Pt b, double reach, double[] box, int at)
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
            /// The hull edges of a hull domain (D13) at M3, where refinement constrains the hull: the boundary, so an input segment at a small
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
                if (e.Milestone == Milestone.M3 && domain.IsHull) {
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
                        if (sources.Any(s => WithinTube(s, v[KeyA(key)]) && WithinTube(s, v[KeyB(key)]))) constraintEdges.Add(key);
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
                if (domain.IsHull) {
                    // D13: the hull is the boundary, and splitting a hull edge in doubles cannot stay exactly on it (O2: a few ulps off a
                    // segment are fine): a boundary vertex within the tube of a hull edge counts as moved by its distance (found with M3)
                    var hullEdges = domain.Edges().ToList();
                    foreach (var pair in edges) {
                        if (pair.Value.Count != 1) continue;
                        foreach (int id in new[] { pair.Value[0].a, pair.Value[0].b }) {
                            Q d = hullEdges.Select(h => Exact.Dist2ToSegment(h.a, h.b, v[id])).Aggregate(Q.Min);
                            if (d <= tube2) moved2 = Q.Max(moved2, d);
                        }
                    }
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
                        bool hull = domain.Edges().Any(s => Exact.Dist2ToSegment(s.a, s.b, v[a]) <= tube2 && Exact.Dist2ToSegment(s.a, s.b, v[b]) <= tube2)
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
                        if (domain.Classify(probe) != Where.Inside || (moved2.Sign > 0 && domain.Edges().Any(sg => Exact.Dist2ToSegment(sg.a, sg.b, probe) <= near2)) || LocateExact(probe) >= 0) continue;
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

            bool NearBoundary(Pt p) => domain.Edges().Any(s => Exact.Dist2ToSegment(s.a, s.b, p) <= moved2);

            bool SourceCovers(int a, int b) => sources.Any(s => WithinTube(s, v[a]) && WithinTube(s, v[b]));

            /// <summary>In the closure of the domain's interior: a boundary point of two touching holes is not, although it is on rings.</summary>
            bool InClosure(Pt p)
            {
                var where = domain.Classify(p);
                if (where != Where.Boundary) return where == Where.Inside;
                var rings = domain.Edges().ToList();
                foreach (var edge in rings)
                    if (Exact.OnClosedSegment(edge.a, edge.b, p))
                        foreach (var piece in RetainedPieces(edge.a, edge.b, rings))
                            if (Exact.OnClosedSegment(piece.from, piece.to, p)) return true;
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
                    foreach (var piece in RetainedPieces(s.a, s.b, ringEdges, double.IsNaN(own[0]) || !(farGap > 0) ? allRingEdges : null, farGap)) {
                        var candidates = new HashSet<int>(near);
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
                        if (reached < 0) { r.Add(Violation.SegmentMissing, "input constraint " + s.a + " - " + s.b + ": no chain from " + piece.from + " to " + piece.to); continue; }
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
                        moved2 = Q.Max(moved2, EndMove(first, piece.from, cutFrom));
                        if (s.locked) CheckLocked(s, reached, previous);
                    }
                }
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
                    if (Fast) GlobalDelaunayIndexed(used.ToArray(), conforming);
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
            /// goes to the filtered exact in-circle test; one report per triangle, as on the reference path.
            /// </summary>
            void GlobalDelaunayIndexed(int[] used, bool conforming)
            {
                double magnitude = 0;
                foreach (int id in used) magnitude = Math.Max(magnitude, Math.Max(Math.Abs(o.VX[id]), Math.Abs(o.VY[id])));
                var tree = (int[])used.Clone();
                BuildTree(tree, 0, tree.Length, 0);
                foreach (var t in o.Triangles) {
                    o11Pending = 0;
                    bool flagged = false;
                    bool Check(int id)
                    {
                        if (id == t[0] || id == t[1] || id == t[2]) return false;
                        if (InCircle(t[0], t[1], t[2], id) > 0 && !(conforming && (ExcusedOffSegment(t[0], t[1], t[2], id) || Zone(t, Q.One) || HiddenByGridUnsplittable(t, id) || HiddenByO11Unsplittable(t, id)))) {
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
                    if (o.IdentityRules ? id < input.Count : IsInputPoint(v[id])) continue;
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
                        corrected[k] = new Pt(ax + ux * t, ay + uy * t);
                        any = true;
                        break;
                    }
                }
                if (!any) return false;
                return Exact.InCircle(corrected[0], corrected[1], corrected[2], corrected[3]) * Exact.Orient(corrected[0], corrected[1], corrected[2]) <= 0;
            }

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
                    if (ForcedByInputAngle(t, low, around) || TouchesLocked(t) || SpansSmallInputAngle(t, low) || AtPrecisionLimit(t) || Zone(t, low)) continue;
                    if (BelowGridFloor(t) || GridBlocked(t) || CircumcentreBehindGridUnsplittable(t) || CircumcentreBehindO11Unsplittable(t)) continue;
                    r.Add(Violation.AngleBelowBound, "triangle " + t[0] + " " + t[1] + " " + t[2] + " is below " + degrees + " degrees with no forced small angle");
                }
            }

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
            /// at apex between apex-a and apex-b: the bound Shewchuk 2002 (section 6.3, the Terminator) proves for every angle of a
            /// triangle near a small input angle phi. A triangle excused for its small angle at the apex must meet it at all three corners
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
                        // (a filter in doubles: the vertex surely farther from c than the zone's radius; the distance's rounding is far
                        // below 2^-40 of the coordinates' magnitude, the slack allowed)
                        if (Fast && vKnown[t[0]] && pair.zoneHi < double.PositiveInfinity) {
                            var ca = PtD(c.a); var cb = PtD(c.b);
                            double px = o.VX[t[0]], py = o.VY[t[0]];
                            double m = Math.Max(Math.Max(Math.Abs(px), Math.Abs(py)), Math.Max(Math.Max(Math.Abs(ca.x), Math.Abs(ca.y)), Math.Max(Math.Abs(cb.x), Math.Abs(cb.y))));
                            if (FilterRange(m) && Math.Sqrt(Dist2D(ca, cb, px, py)) - m * 1e-12 > pair.zoneHi) continue;
                        }
                        if (Exact.Dist2ToSegment(c.a, c.b, v[t[0]]) > zone2) continue;
                        if (!pair.apexOnBoth) continue;
                        var apex = pair.apex;
                        if (!t.All(i => Exact.Len2(apex, v[i]) <= zone2)) continue;
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

            Dictionary<(int, int), (bool crosses, Q sin2, Q zone2, double zoneHi, bool apexOnBoth, Pt apex)> pairs;

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
            (bool crosses, Q sin2, Q zone2, double zoneHi, bool apexOnBoth, Pt apex) Pair((Pt a, Pt b, int marker, int sourceId, bool locked) a, (Pt a, Pt b, int marker, int sourceId, bool locked) c)
            {
                pairs = pairs ?? new Dictionary<(int, int), (bool, Q, Q, double, bool, Pt)>();
                if (pairs.TryGetValue((a.sourceId, c.sourceId), out var known)) return known;
                Q d1x = a.b.Qx - a.a.Qx, d1y = a.b.Qy - a.a.Qy, d2x = c.b.Qx - c.a.Qx, d2y = c.b.Qy - c.a.Qy;
                Q cross = d1x * d2y - d1y * d2x;
                (bool, Q, Q, double, bool, Pt) result;
                if (cross.Sign == 0) result = (false, Q.Zero, Q.Zero, double.PositiveInfinity, false, default(Pt));
                else {
                    Q sin2 = cross * cross / ((d1x * d1x + d1y * d1y) * (d2x * d2x + d2y * d2y));
                    Q zone2 = tube2 * (ZoneFactor * ZoneFactor) / sin2;   // (ZoneFactor tube / sin)^2
                    Q t1 = ((c.a.Qx - a.a.Qx) * d2y - (c.a.Qy - a.a.Qy) * d2x) / cross, t2 = ((c.a.Qx - a.a.Qx) * d1y - (c.a.Qy - a.a.Qy) * d1x) / cross;
                    bool onBoth = !(t1.Sign < 0 || t1 > Q.One || t2.Sign < 0 || t2 > Q.One);
                    // (the zone's radius from above in doubles, for the filter; infinite when it cannot be bounded)
                    double zoneHi = zone2.Bounds(out _, out double z2h) && !double.IsInfinity(z2h) ? Math.Sqrt(z2h) * (1 + 1e-12) : double.PositiveInfinity;
                    result = (true, sin2, zone2, zoneHi, onBoth, onBoth ? new Pt(a.a.Qx + t1 * d1x, a.a.Qy + t1 * d1y) : default(Pt));
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
            /// them at an angle below the bound, at distances from the apex within a factor of two, on the same side of it. Such a triangle
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
                foreach (var s in o.Subsegments) {
                    var covering = Enumerable.Range(0, sources.Count).Where(k => NearSource(k, s.A) && NearSource(k, s.B)).Select(k => sources[k]).ToList();
                    if (covering.Count == 0) {
                        bool onHull = domain.IsHull && domain.Edges().Any(h => Exact.Dist2ToSegment(h.a, h.b, v[s.A]) <= tube2 && Exact.Dist2ToSegment(h.a, h.b, v[s.B]) <= tube2);
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
