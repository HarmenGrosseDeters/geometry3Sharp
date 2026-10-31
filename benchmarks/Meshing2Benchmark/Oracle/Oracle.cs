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
            for (int i = 0; i < input.Count; ++i) {
                max = Q.Max(max, Q.Abs(Q.FromDouble(input.X[i])));
                max = Q.Max(max, Q.Abs(Q.FromDouble(input.Y[i])));
            }
            return max * new Q(1, BigInteger.One << 40); // the extent is at most twice the largest coordinate: within a factor 2
        }

        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        public static OracleReport Check(Mesh2Input input, Mesh2Output o, OracleExpect e)
        {
            return new Run(input, o, e).Execute();
        }

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
                domain = new Domain(input);
                bool degenerate = domain.Area.Sign == 0;
                tube = e.Tube ?? (e.Milestone == Milestone.M1 ? Q.Zero : Q.Max(Oracle.AutoTolerance(input), Q.FromDouble(input.SnapTolerance)));
                tube2 = tube * tube;
                if (!Indices()) return r;
                if (!Identity()) return r;
                if (o.Triangles.Length == 0) {
                    if (o.Status != Mesh2Status.Empty) r.Add(Violation.StatusInconsistent, o.Status + " with no triangles");
                    if (!degenerate) r.Add(Violation.EmptyOnArea, "no triangles, the domain has area " + domain.Area);
                    return r;
                }
                if (o.Status != Mesh2Status.Success) r.Add(Violation.StatusInconsistent, o.Status + " with triangles");
                if (degenerate) r.Add(Violation.TrianglesOnDegenerate, "triangles on an input without area");
                Orientation();
                if (!r.Ok) return r; // the rest assumes positive triangles
                EdgesAndNeighbours();
                Sources();
                Conformity();
                Crossings();
                InventedVertices();
                Segments();   // before Coverage: it measures how far the geometry moved
                Coverage();
                Delaunay();
                Quality();
                Regions();
                Marks();
                Locate();
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
                    for (int i = 0; i < input.Count; ++i) {
                        int m = o.InputToVertex[i];
                        if (m < 0 || m >= input.Count || o.InputToVertex[m] != m) { r.Add(Violation.MappingWrong, "input " + i + " maps to " + m + ", which is no surviving input slot"); return false; }
                        map[i] = m;
                        if (byCoordinate.TryGetValue(inPts[i], out int other)) {
                            if (o.InputToVertex[other] != m) r.Add(Violation.MappingWrong, "inputs " + other + " and " + i + " coincide but map differently");
                        } else byCoordinate.Add(inPts[i], i);
                        if (m == i && !e.MayMoveVertices && !v[i].Equals(inPts[i])) r.Add(Violation.InputMoved, "input " + i + " moved to " + v[i]);
                        Q off = Exact.Len2(inPts[i], v[m]);
                        moved2 = Q.Max(moved2, off);
                        if (off > e_Tube2()) r.Add(Violation.SnapTooFar, "input " + i + " merged too far, into " + m);
                    }
                    r.ConstructedVertices = v.Length - input.Count;
                    var isAlias = new bool[input.Count];
                    for (int i = 0; i < input.Count; ++i) isAlias[i] = map[i] != i;
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

            void Orientation()
            {
                r.Area2 = Q.Zero;
                foreach (var t in o.Triangles) {
                    if (t[0] == t[1] || t[1] == t[2] || t[0] == t[2]) { r.Add(Violation.NotPositive, "triangle with a repeated vertex"); continue; }
                    if (Exact.Orient(v[t[0]], v[t[1]], v[t[2]]) <= 0) { r.Add(Violation.NotPositive, "triangle " + t[0] + " " + t[1] + " " + t[2] + " is not counter-clockwise"); continue; }
                    r.Area2 += Exact.Area2(v[t[0]], v[t[1]], v[t[2]]);
                    var q = Exact.MinAngleSin2(v[t[0]], v[t[1]], v[t[2]]);
                    if (r.MinSin2 == null || q < r.MinSin2.Value) r.MinSin2 = q;
                }
            }

            void EdgesAndNeighbours()
            {
                edges = new Dictionary<long, List<(int, int, int, int)>>();
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
                    if (o.IdentityRules && e.Milestone != Milestone.M1) r.Add(Violation.MissingReport, "no neighbours (spec R5)");
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
                int[] rx = Ranks(v, p => p.Qx), ry = Ranks(v, p => p.Qy);
                var triBoxes = o.Triangles.Select(t => new[] {
                    t.Min(i => rx[i]), t.Max(i => rx[i]), t.Min(i => ry[i]), t.Max(i => ry[i]) }).ToArray();
                var used = new HashSet<int>(o.Triangles.SelectMany(t => t)).OrderBy(i => i).ToArray();
                var vertexBoxes = used.Select(i => new[] { rx[i], rx[i], ry[i], ry[i] }).ToArray();
                int reports = 0;
                OverlapPairs(triBoxes, vertexBoxes, false, (ti, vi) => {
                    var t = o.Triangles[ti];
                    int id = used[vi];
                    if (id == t[0] || id == t[1] || id == t[2]) return;
                    Pt a = v[t[0]], b = v[t[1]], c = v[t[2]], p = v[id];
                    if (Exact.Orient(a, b, p) >= 0 && Exact.Orient(b, c, p) >= 0 && Exact.Orient(c, a, p) >= 0 && reports++ < 8)
                        r.Add(Violation.VertexInTriangle, "vertex " + id + " " + p + " touches triangle " + t[0] + " " + t[1] + " " + t[2]);
                });
                var keys = edges.Keys.ToArray();
                var edgeBoxes = keys.Select(k => new[] {
                    Math.Min(rx[KeyA(k)], rx[KeyB(k)]), Math.Max(rx[KeyA(k)], rx[KeyB(k)]),
                    Math.Min(ry[KeyA(k)], ry[KeyB(k)]), Math.Max(ry[KeyA(k)], ry[KeyB(k)]) }).ToArray();
                reports = 0;
                OverlapPairs(edgeBoxes, edgeBoxes, true, (i, j) => {
                    int a = KeyA(keys[i]), b = KeyB(keys[i]), c = KeyA(keys[j]), d = KeyB(keys[j]);
                    if (a == c || a == d || b == c || b == d) return;
                    if (Exact.ProperCross(v[a], v[b], v[c], v[d]) && reports++ < 8)
                        r.Add(Violation.EdgesCross, "edges " + a + "-" + b + " and " + c + "-" + d + " cross");
                });
                // every used vertex that is a corner of two triangles with the same coordinates would pass the tests above
                var seen = new Dictionary<Pt, int>();
                foreach (int id in used) {
                    if (seen.TryGetValue(v[id], out int other)) r.Add(Violation.VertexInTriangle, "vertices " + other + " and " + id + " coincide");
                    else seen.Add(v[id], id);
                }
                // the unused vertices: an input point that belongs to the domain must be used; a constructed vertex must be used
                var usedSet = new HashSet<int>(used);
                for (int i = 0; i < input.Count; ++i) {
                    int m = map[i];
                    bool isUsed = m >= 0 && usedSet.Contains(m);
                    if (isUsed) continue;
                    if (InClosure(inPts[i])) r.Add(Violation.VertexUnused, "input " + i + " " + inPts[i] + " is in the domain but in no triangle");
                }
                if (o.IdentityRules)
                    for (int i = input.Count; i < v.Length; ++i)
                        if (!usedSet.Contains(i)) r.Add(Violation.VertexUnused, "constructed vertex " + i + " is in no triangle");
                // F-36: no vertex outside the domain, by more than the tube
                foreach (int id in used)
                    if (domain.Classify(v[id]) == Where.Outside && !domain.Edges().Any(s => Exact.Dist2ToSegment(s.a, s.b, v[id]) <= tube2))
                        r.Add(Violation.TriangleOutsideDomain, "vertex " + id + " " + v[id] + " is outside the domain");
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
                    subsegmentSet = new HashSet<long>(o.Subsegments.Select(s => Key(s.A, s.B)));
                    constraintEdges = subsegmentSet;
                } else {
                    if (o.IdentityRules && e.Milestone != Milestone.M1) r.Add(Violation.MissingReport, "no subsegments (spec R4)");
                    constraintEdges = new HashSet<long>();
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
                var inputs = new HashSet<Pt>(inPts);
                foreach (int id in o.Triangles.SelectMany(t => t).Distinct()) {
                    if (inputs.Contains(v[id])) continue;
                    var near = sources.Where(s => WithinTube(s, v[id])).ToList();
                    bool crossing = near.Any(s => near.Any(q => !Collinear(s.a, s.b, q.a, q.b)));
                    if (!crossing) r.Add(Violation.SteinerNotAllowed, "vertex " + id + " " + v[id] + " is no input point and no crossing of constraints");
                }
            }

            static bool Collinear(Pt a, Pt b, Pt c, Pt d) => Exact.Orient(a, b, c) == 0 && Exact.Orient(a, b, d) == 0;

            bool WithinTube((Pt a, Pt b, int marker, int sourceId, bool locked) s, Pt p) => Exact.Dist2ToSegment(s.a, s.b, p) <= tube2;

            /// <summary>The union of the triangles is the domain: exact area, boundary edges constrained or on the hull, centroids inside.</summary>
            void Coverage()
            {
                // Geometry moved by sqrt(moved2) at most, and only what really moved counts: the tube permits movement, it grants no area.
                // diff <= 2 * perimeter * sqrt(moved2), compared in squares to stay exact.
                foreach (var seed in input.HoleSeeds) {
                    var p = Pt.FromDoubles(seed[0], seed[1]);
                    if (domain.Classify(p) == Where.Inside) effectiveSeeds.Add(p);
                }
                if (effectiveSeeds.Count == 0) {
                    Q diff = Q.Abs(r.Area2 - domain.Area * 2);
                    Q perimeter = domain.Perimeter2Bound * 2;
                    if (diff * diff > moved2 * perimeter * perimeter) r.Add(Violation.AreaMismatch, "twice the mesh area is " + r.Area2 + ", twice the domain's " + (domain.Area * 2) + ", moved at most sqrt(" + moved2 + ")");
                } else {
                    // A seed-only hole removes a cell of the input constraints, which Domain does not compute: the fixture must state the area.
                    if (e.StatedArea == null) r.Add(Violation.CannotJudge, "a hole seed outside all hole rings: the case must state the area");
                    foreach (var p in effectiveSeeds) if (LocateExact(p) >= 0) r.Add(Violation.RemovedPointCovered, "hole seed " + p + " is covered");
                }
                if (e.StatedArea != null && Q.Abs(r.Area2 - e.StatedArea.Value * 2) > e.StatedAreaTolerance * 2)
                    r.Add(Violation.AreaMismatch, "twice the mesh area is " + r.Area2 + ", the specification states area " + e.StatedArea.Value);
                foreach (var pair in edges) {
                    if (pair.Value.Count != 1) continue;
                    int a = pair.Value[0].a, b = pair.Value[0].b;
                    bool constrained = constraintEdges.Contains(pair.Key);
                    if (domain.IsHull) {
                        // on a hull edge: within the tube of it, so exactly on it if the tube is 0
                        bool hull = domain.Edges().Any(s => Exact.Dist2ToSegment(s.a, s.b, v[a]) <= tube2 && Exact.Dist2ToSegment(s.a, s.b, v[b]) <= tube2);
                        if (!hull && !(constrained && SourceCovers(a, b))) r.Add(e.Milestone == Milestone.M1 ? Violation.HullBoundary : Violation.BoundaryNotConstrained,
                            "boundary edge " + a + "-" + b + " neither on the hull nor on a constraint");
                    } else if (!constrained) r.Add(Violation.BoundaryNotConstrained, "boundary edge " + a + "-" + b + " is no constraint");
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
            bool InteriorBeside(Pt m, Q ux, Q uy, List<(Pt a, Pt b)> rings)
            {
                Q limit = new Q(-1, 1);
                foreach (var ring in rings) {
                    if (Exact.OnClosedSegment(ring.a, ring.b, m)) continue;
                    Q d = Exact.Dist2ToSegment(ring.a, ring.b, m);
                    if (limit.Sign < 0 || d < limit) limit = d;
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

            int LocateExact(Pt p)
            {
                for (int i = 0; i < o.Triangles.Length; ++i) {
                    var t = o.Triangles[i];
                    if (Exact.Orient(v[t[0]], v[t[1]], p) >= 0 && Exact.Orient(v[t[1]], v[t[2]], p) >= 0 && Exact.Orient(v[t[2]], v[t[0]], p) >= 0) return i;
                }
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
                var ringEdges = domain.Edges().ToList();
                foreach (var s in sources) {
                    foreach (var piece in RetainedPieces(s.a, s.b, ringEdges)) {
                        var candidates = new HashSet<int>();
                        for (int i = 0; i < v.Length; ++i) if (Exact.Dist2ToSegment(s.a, s.b, v[i]) <= tube2) candidates.Add(i);
                        var starts = candidates.Where(i => Exact.Len2(v[i], piece.from) <= tube2).ToList();
                        var goals = new HashSet<int>(candidates.Where(i => Exact.Len2(v[i], piece.to) <= tube2));
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
                        moved2 = Q.Max(moved2, Exact.Len2(v[reached], piece.to));
                        int first = reached;
                        for (int at = reached; at >= 0; at = previous[at]) { moved2 = Q.Max(moved2, Exact.Dist2ToSegment(s.a, s.b, v[at])); first = at; }
                        moved2 = Q.Max(moved2, Exact.Len2(v[first], piece.from));
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
                    bool onOther = sources.Any(other => !Collinear(s.a, s.b, other.a, other.b) && WithinTube(other, v[at]));
                    if (!onOther) r.Add(Violation.LockedSplit, "locked segment " + s.a + " - " + s.b + " is split at " + v[at]);
                }
            }

            /// <summary>The parts of a-b in the closed domain, cut at all ring edges.</summary>
            List<(Pt from, Pt to)> RetainedPieces(Pt a, Pt b, List<(Pt a, Pt b)> rings)
            {
                var pieces = new List<(Pt, Pt)>();
                Q ax = a.Qx, ay = a.Qy, ux = b.Qx - ax, uy = b.Qy - ay, uu = ux * ux + uy * uy;
                var ts = new List<Q> { Q.Zero, Q.One };
                foreach (var ring in rings) {
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
                    bool keep = where == Where.Inside || (where == Where.Boundary && InteriorBeside(mid, ux, uy, rings));
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
                bool conforming = input.Conforming || quality;
                foreach (var pair in edges) {
                    var uses = pair.Value;
                    if (uses.Count != 2) continue;
                    bool constrained = constraintEdges.Contains(pair.Key);
                    if (constrained && (!conforming || OnLocked(pair.Key))) continue;
                    int a = uses[0].a, b = uses[0].b;
                    int c = o.Triangles[uses[0].tri][uses[0].opposite], d = o.Triangles[uses[1].tri][uses[1].opposite];
                    int sign = Exact.InCircle(v[a], v[b], v[c], v[d]);
                    if (sign > 0 && !(conforming && ExcusedOffSegment(a, b, c, d))) r.Add(Violation.NotDelaunayLocal, "edge " + a + "-" + b + " is not locally Delaunay against " + d);
                    if (sign == 0 && !constrained) { r.TieEdges++; r.Unique = false; }
                }
                long work = (long)o.Triangles.Length * v.Length;
                bool anyLocked = sources.Any(s => s.locked);
                if ((e.Milestone == Milestone.M1 || (conforming && !anyLocked)) && work <= e.GlobalDelaunayLimit) {
                    r.GlobalDelaunayChecked = true;
                    var used = new HashSet<int>(o.Triangles.SelectMany(t => t));
                    foreach (var t in o.Triangles)
                        foreach (int id in used) {
                            if (id == t[0] || id == t[1] || id == t[2]) continue;
                            if (Exact.InCircle(v[t[0]], v[t[1]], v[t[2]], v[id]) > 0 && !(conforming && ExcusedOffSegment(t[0], t[1], t[2], id))) {
                                r.Add(Violation.NotDelaunayGlobal, "vertex " + id + " inside the circumcircle of " + t[0] + " " + t[1] + " " + t[2]);
                                break;
                            }
                        }
                }
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
                    if (o.IdentityRules ? id < input.Count : inPts.Any(p => p.Equals(v[id]))) continue;
                    Q ulp = new Q(BigInteger.One, BigInteger.One << 49) * Q.Max(Q.Abs(v[id].Qx), Q.Abs(v[id].Qy)); // 8 ulps = 2^3 * 2^-52
                    foreach (var s in sources) {
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
                    long limit = input.SteinerLimit ?? 10L * (input.Count + input.Segments.Count) + 100000;
                    if (e.Milestone != Milestone.M3) r.Add(Violation.SteinerFlagWrong, "Steiner limit reported without refinement");
                    else if (r.ConstructedVertices * 2L < limit) r.Add(Violation.SteinerFlagWrong, "Steiner limit reached with " + r.ConstructedVertices + " constructed vertices, limit " + limit);
                }
                if (input.MinAngleDeg != null && !e.InvalidAngle) AngleBound(input.MinAngleDeg.Value);
                if (e.MinTriangles != null && o.Triangles.Length < e.MinTriangles.Value) r.Add(Violation.CountOutOfRange, o.Triangles.Length + " triangles, at least " + e.MinTriangles.Value + " required");
                if (e.MaxVertices != null && v.Length > e.MaxVertices.Value) r.Add(Violation.CountOutOfRange, v.Length + " vertices, at most " + e.MaxVertices.Value + " allowed");
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
                for (int i = 0; i < o.Triangles.Length; ++i) {
                    var t = o.Triangles[i];
                    Q sin2 = Exact.MinAngleSin2(v[t[0]], v[t[1]], v[t[2]]);
                    bool below = sin2 < low;
                    if (below) r.BelowAngle++;
                    if (listed != null) {
                        if (below && !listed.Contains(i)) r.Add(Violation.BadListWrong, "triangle " + i + " is below the angle but not listed");
                        if (!below && sin2 >= high && listed.Contains(i)) r.Add(Violation.BadListWrong, "triangle " + i + " is listed but not below the angle");
                    }
                    if (!below || o.SteinerLimitReached || !e.AngleGuaranteed) continue;
                    if (ForcedByInputAngle(t, low, around) || TouchesLocked(t)) continue;
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
                            if (Exact.Orient(p, a, x) >= 0 && Exact.Orient(p, x, b) >= 0 && Exact.Orient(p, a, y) >= 0 && Exact.Orient(p, y, b) >= 0) return true;
                        }
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
                            if (Domain.InRing(ring, centroids[i]) == Where.Inside) { labels[i] = region.Label; areas[i] = bound; }
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
                return o.Triangles.Any(t => t.Any(id => v[id].Equals(seed)));
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
                    var covering = sources.Where(c => WithinTube(c, v[s.A]) && WithinTube(c, v[s.B])).ToList();
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
}
