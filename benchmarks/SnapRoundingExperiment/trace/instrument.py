# Scratch instrumentation of mesh_ops/MeshRoundingRepair.cs for the rounding trace of docs/design/SNAP_ROUNDING_3D.md.
# It is applied to a throw-away worktree, never committed to the library:
#     git worktree add --detach <wt> 46529bed          # the commit the anchors below match
#     python3 instrument.py <wt>/mesh_ops/MeshRoundingRepair.cs
#     dotnet build <wt>/benchmarks/BooleanComparison -c Release -o <tool>
# The built tool behaves as master unless an environment variable is set:
#     G3_ROUNDING_TRACE=1        one line "ROUNDING_TRACE {...}" per MeshRoundingRepair.Apply on stderr: the phases of Apply
#                                (P.*), accumulated times (T.*, ms), the first full search once more serially, split into
#                                traversal and pair tests by kind (diag.*, Tdiag.*), and the evaluations of proposed edits
#                                (eval.*: near queries, pair tests with and without a shared corner)
#     G3_ROUNDING_TRACE_DIST=1   also the distance of every pair of an added triangle without a shared corner (slow)
#     G3_SEPAXIS=1               the separating-axis prefilter of tools/Z3Pilot/separating_axis.py in front of the pair test
#     G3_STOP_AT_LIMIT=1         the passes stop proposing edits once the evaluation limit is reached
# Each anchor must occur exactly once, so the script fails instead of patching a changed file wrongly.
import sys
path = sys.argv[1]
s = open(path).read()
def rep(old, new, count=1):
    global s
    if s.count(old) < 1: raise SystemExit("anchor not found: " + old[:80])
    if count == 1 and s.count(old) != 1: raise SystemExit("anchor not unique (%d): %s" % (s.count(old), old[:80]))
    s = s.replace(old, new)

# --- Apply: phases
rep("""            Collapses = Flips = Moves = Zips = StripZips = VertexRemovals = RemovedComponents = RemovedTriangles = 0;
            MaxDrift = 0;
            refAnchor""", """            RoundingTrace.Begin(this);
            try { return apply_traced(); } finally { RoundingTrace.End(this); }
        }

        [MethodImpl(JitHints.Optimize)]
        bool apply_traced()
        {
            Collapses = Flips = Moves = Zips = StripZips = VertexRemovals = RemovedComponents = RemovedTriangles = 0;
            MaxDrift = 0;
            refAnchor""")
rep("""            component_scales();
            // a vertex of a triangle that is not finite""", """            RoundingTrace.Mark("setup.anchors_boundary");
            component_scales();
            RoundingTrace.Mark("setup.component_scales");
            // a vertex of a triangle that is not finite""")
rep("""            if (SharedPositionsTouch) {
                atPosition = new Dictionary<Vector3d, List<int>>();""", """            RoundingTrace.Mark("setup.range_scan");
            if (SharedPositionsTouch) {
                atPosition = new Dictionary<Vector3d, List<int>>();""")
rep("""            SkippedExactQueries = 0;
            if (exactF != null) {""", """            RoundingTrace.Mark("setup.position_map");
            SkippedExactQueries = 0;
            if (exactF != null) {""")
rep("""            rebuild_tree();
            lastPairs = null;""", """            RoundingTrace.Mark("setup.exact_flags");
            rebuild_tree();
            RoundingTrace.Mark("tree.first");
            lastPairs = null;""")
rep("""            List<Index2i> pairs = intersecting_pairs();
            List<int> bad = degenerate_triangles();
            InitialIntersections = pairs.Count;""", """            RoundingTrace.Mark("misc");
            if (RoundingTrace.On) { diag_first_search(); RoundingTrace.Mark("DIAG"); }
            List<Index2i> pairs = intersecting_pairs();
            RoundingTrace.Mark("search.first");
            List<int> bad = degenerate_triangles();
            RoundingTrace.Mark("degenerate.first");
            InitialIntersections = pairs.Count;""")
rep("""            maxEvaluations = 10000 + (long)MaxEvaluationsPerDefect * (pairs.Count + bad.Count + InitialFalseContacts);
            repair_passes(ref pairs, ref bad);""", """            maxEvaluations = 10000 + (long)MaxEvaluationsPerDefect * (pairs.Count + bad.Count + InitialFalseContacts);
            RoundingTrace.Mark("misc");
            repair_passes(ref pairs, ref bad);
            RoundingTrace.Mark("passes");""")
rep("""            if (check)
                check_all_bounds();
            if (RemoveTinyComponents""", """            RoundingTrace.Mark("contacts_and_passes2");
            if (check)
                check_all_bounds();
            if (RemoveTinyComponents""")
rep("""            RemainingIntersections = pairs.Count;
            RemainingDegenerate = bad.Count;
            List<List<int>> contactsLeft""", """            RoundingTrace.Mark("tiny_components");
            RemainingIntersections = pairs.Count;
            RemainingDegenerate = bad.Count;
            List<List<int>> contactsLeft""")

# --- improves wrapper
rep("""        bool improves(double displacement = 0, double surface = 0, bool hasSurface = false, bool removesContact = false)
        {""", """        bool improves(double displacement = 0, double surface = 0, bool hasSurface = false, bool removesContact = false)
        {
            if (RoundingTrace.On == false)
                return improves_impl(displacement, surface, hasSurface, removesContact);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            RoundingTrace.InImproves = true;
            RoundingTrace.EvalStep = hasSurface ? surface : displacement;
            RoundingTrace.EvalNon = 0; RoundingTrace.EvalNonFar = 0; RoundingTrace.EvalNonPos = 0; RoundingTrace.EvalStage = 0;
            bool ok = improves_impl(displacement, surface, hasSurface, removesContact);
            RoundingTrace.InImproves = false;
            RoundingTrace.EndEval(ok, removed.Count, added.Count, removed.Count > 0 ? removed[0] : -1, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            return ok;
        }

        [MethodImpl(JitHints.Optimize)]
        bool improves_impl(double displacement = 0, double surface = 0, bool hasSurface = false, bool removesContact = false)
        {""")
rep("""            if (++evaluations > maxEvaluations || pairTests > MaxPairTests) {
                EvaluationLimitReached = true;
                return false;
            }""", """            RoundingTrace.EvalStage = 1;
            if (++evaluations > maxEvaluations || pairTests > MaxPairTests) {
                EvaluationLimitReached = true;
                return false;
            }
            RoundingTrace.EvalStage = 2;""")
rep("""                    if (ExactTriangleIntersection.IsDegenerate(ref U) == false && intersects(t, ref T, u, ref U))
                        oldI++;""", """                    if (ExactTriangleIntersection.IsDegenerate(ref U) == false && traced_intersects(t, ref T, u, ref U, false))
                        oldI++;""")
rep("""            cacheVersion = version; cacheOldI = oldI;""", """            RoundingTrace.EvalStage = 3;
            cacheVersion = version; cacheOldI = oldI;""")
rep("""                    if (ExactTriangleIntersection.IsDegenerate(ref U) == false && intersects(t, ref tris[i], u, ref U))
                        if (++newI > oldI) return false;""", """                    if (ExactTriangleIntersection.IsDegenerate(ref U) == false && traced_intersects(t, ref tris[i], u, ref U, true))
                        if (++newI > oldI) return false;""")
rep("""            // (an edit that removes a contact may keep the counts)
            return newI + newB < oldI + oldB""", """            RoundingTrace.EvalStage = 4;
            // (an edit that removes a contact may keep the counts)
            return newI + newB < oldI + oldB""")

# --- near timing
rep("""        void near(AxisAlignedBox3d query, List<int> result)
        {""", """        void near(AxisAlignedBox3d query, List<int> result)
        {
            if (RoundingTrace.On == false) { near_impl(query, result); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            near_impl(query, result);
            RoundingTrace.Near(result.Count, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        }

        [MethodImpl(JitHints.Optimize)]
        void near_impl(AxisAlignedBox3d query, List<int> result)
        {""")

# --- tree, search, degenerate timing
rep("""        void rebuild_tree()
        {""", """        void rebuild_tree()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            rebuild_tree_impl();
            RoundingTrace.Add("T.tree", System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        }

        [MethodImpl(JitHints.Optimize)]
        void rebuild_tree_impl()
        {""")
rep("""        List<Index2i> intersecting_pairs()
        {
            if (dirty.Count > 0)
                rebuild_tree();""", """        List<Index2i> intersecting_pairs()
        {
            if (dirty.Count > 0)
                rebuild_tree();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var r = intersecting_pairs_impl();
            RoundingTrace.Add("T.search", System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            return r;
        }

        [MethodImpl(JitHints.Optimize)]
        List<Index2i> intersecting_pairs_impl()
        {""")
rep("""        List<int> degenerate_triangles()
        {""", """        List<int> degenerate_triangles()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var r = degenerate_triangles_impl();
            RoundingTrace.Add("T.degenerate", System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            return r;
        }

        [MethodImpl(JitHints.Optimize)]
        List<int> degenerate_triangles_impl()
        {""")
rep("""        void mark_dirty(int tid)
        {""", """        internal int TraceTouched { get { return touched.Count; } }
        internal long TracePairTests { get { return pairTests; } }
        void mark_dirty(int tid)
        {
            if (RoundingTrace.On) RoundingTrace.Dirty.Add(tid);""")

# --- helpers + diag + trace class
rep("""        bool intersects(int ta, int tb)
        {""", """        // (trace) a pair test inside improves: incident (a shared canonical vertex) or not, timed; for the pairs of an
        // added triangle that share no vertex, the distance of the two triangles against the step of the edit
        bool traced_intersects(Index3i t, ref Triangle3d T, Index3i u, ref Triangle3d U, bool added)
        {
            if (RoundingTrace.On == false)
                return intersects(t, ref T, u, ref U);
            Index3i ct = canonical(t), cu = canonical(u);
            bool incident = ct.a == cu.a || ct.a == cu.b || ct.a == cu.c || ct.b == cu.a || ct.b == cu.b || ct.b == cu.c || ct.c == cu.a || ct.c == cu.b || ct.c == cu.c;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool hit = intersects(t, ref T, u, ref U);
            long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            double d = -1;
            if (RoundingTrace.Dist && added && incident == false && hit == false) {
                var dist = new DistTriangle3Triangle3(T, U);
                d = Math.Sqrt(Math.Max(0, dist.GetSquared()));
            }
            RoundingTrace.Pair(added, incident, hit, dt, d);
            return hit;
        }

        // (trace) the first full search once more, serially, split into traversal and pair tests by kind
        void diag_first_search()
        {
            int[] tris = treeTids.ToArray();
            bool skip = exactAtStart != null;
            var h = new List<int>();
            long tTrav = 0, queries = 0, nhits = 0, skippedQ = 0;
            long[] cnt = new long[8], tim = new long[8];     // [incident(0/1)][undecided(0/1)] -> filter time, exact time
            long hitsInc = 0, hitsNon = 0, selfOrDup = 0, intersecting = 0, intersectingNon = 0;
            foreach (int ta in tris) {
                if (skip && exact_triangle(ta)) { skippedQ++; continue; }
                Index3i ia = Mesh.GetTriangle(ta);
                Triangle3d A = triangle(ia);
                if (ExactTriangleIntersection.IsDegenerate(ref A)) continue;
                h.Clear();
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                tree.Query(box(ia), h);
                tTrav += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                queries++; nhits += h.Count;
                Index3i ca = canonical(ia);
                foreach (int i in h) {
                    int tb = tris[i];
                    if (tb == ta || (tb < ta && (skip == false || exact_triangle(tb) == false))) { selfOrDup++; continue; }
                    Index3i ib = Mesh.GetTriangle(tb);
                    Triangle3d B = triangle(ib);
                    if (ExactTriangleIntersection.IsDegenerate(ref B)) continue;
                    Index3i cb = canonical(ib);
                    bool inc = ca.a == cb.a || ca.a == cb.b || ca.a == cb.c || ca.b == cb.a || ca.b == cb.b || ca.b == cb.c || ca.c == cb.a || ca.c == cb.b || ca.c == cb.c;
                    if (inc) hitsInc++; else hitsNon++;
                    long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                    int decision = ta < tb ? TriangleTriangleFilter.BeyondShared(ca, ref A, cb, ref B) : TriangleTriangleFilter.BeyondShared(cb, ref B, ca, ref A);
                    long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                    int k = (inc ? 0 : 4);
                    bool hit;
                    if (decision != TriangleTriangleFilter.Undecided) {
                        cnt[k]++; tim[k] += t2 - t1;
                        hit = decision == TriangleTriangleFilter.Intersecting;
                    } else {
                        hit = ta < tb ? ExactTriangleIntersection.IntersectsBeyondShared(ca, ref A, cb, ref B) : ExactTriangleIntersection.IntersectsBeyondShared(cb, ref B, ca, ref A);
                        long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
                        cnt[k + 1]++; tim[k + 1] += t2 - t1; tim[k + 2] += t3 - t2;
                    }
                    if (hit) { intersecting++; if (inc == false) intersectingNon++; }
                }
            }
            RoundingTrace.Set("diag.triangles", tris.Length); RoundingTrace.Set("diag.queries", queries); RoundingTrace.Set("diag.skipped_exact", skippedQ);
            RoundingTrace.Set("diag.box_hits", nhits); RoundingTrace.Set("diag.self_or_dup", selfOrDup);
            RoundingTrace.Set("diag.pairs_incident", hitsInc); RoundingTrace.Set("diag.pairs_nonincident", hitsNon);
            RoundingTrace.Set("diag.inc_filter_decided", cnt[0]); RoundingTrace.Set("diag.inc_undecided", cnt[1]);
            RoundingTrace.Set("diag.non_filter_decided", cnt[4]); RoundingTrace.Set("diag.non_undecided", cnt[5]);
            RoundingTrace.Set("diag.intersecting", intersecting); RoundingTrace.Set("diag.intersecting_nonincident", intersectingNon);
            RoundingTrace.Add("Tdiag.traversal", tTrav);
            RoundingTrace.Add("Tdiag.inc_filter", tim[0] + tim[1]); RoundingTrace.Add("Tdiag.inc_exact", tim[2]);
            RoundingTrace.Add("Tdiag.non_filter", tim[4] + tim[5]); RoundingTrace.Add("Tdiag.non_exact", tim[6]);
        }

        bool intersects(int ta, int tb)
        {""")

# --- scratch switches: separating-axis prefilter in the pair test, stop the passes at the evaluation limit
rep("""        bool intersects(Index3i t, ref Triangle3d T, Index3i u, ref Triangle3d U)
        {
            Index3i ct = canonical(t), cu = canonical(u);""", """        bool intersects(Index3i t, ref Triangle3d T, Index3i u, ref Triangle3d U)
        {
            if (RoundingTrace.SepAxis && SeparatingAxis.Apart(ref T, ref U))
                return false;
            Index3i ct = canonical(t), cu = canonical(u);""")
rep("""                    if ((retry == null || retry.Contains(tv.a) || retry.Contains(tv.b) || retry.Contains(tv.c)))
                        fix_degenerate(t);""", """                    if (RoundingTrace.StopAtLimit && EvaluationLimitReached)
                        break;
                    if ((retry == null || retry.Contains(tv.a) || retry.Contains(tv.b) || retry.Contains(tv.c)))
                        fix_degenerate(t);""")
rep("""                    foreach (Index2i p in pairs)
                        if (Mesh.IsTriangle(p.a) && Mesh.IsTriangle(p.b) && intersects(p.a, p.b))
                            fix_intersection(p.a, p.b);""", """                    foreach (Index2i p in pairs) {
                        if (RoundingTrace.StopAtLimit && EvaluationLimitReached)
                            break;
                        if (Mesh.IsTriangle(p.a) && Mesh.IsTriangle(p.b) && intersects(p.a, p.b))
                            fix_intersection(p.a, p.b);
                    }""")

s = s.rstrip()
assert s.endswith("}")
s = s[:-1] + """
    // scratch prototype (not for merge): two triangles strictly apart along a normal or an in-plane edge normal of one
    // of them, projections in doubles with a rounding margin (tools/Z3Pilot/separating_axis.py)
    internal static class SeparatingAxis
    {
        const double Gamma = 8 * 1.1102230246251565e-16;
        [MethodImpl(JitHints.Optimize)]
        internal static bool Apart(ref Triangle3d A, ref Triangle3d B)
        {
            Vector3d nA = (A.V1 - A.V0).Cross(A.V2 - A.V0);
            if (apart(nA, ref A, ref B)) return true;
            Vector3d nB = (B.V1 - B.V0).Cross(B.V2 - B.V0);
            if (apart(nB, ref A, ref B)) return true;
            if (apart(nA.Cross(A.V1 - A.V0), ref A, ref B) || apart(nA.Cross(A.V2 - A.V1), ref A, ref B) || apart(nA.Cross(A.V0 - A.V2), ref A, ref B)) return true;
            return apart(nB.Cross(B.V1 - B.V0), ref A, ref B) || apart(nB.Cross(B.V2 - B.V1), ref A, ref B) || apart(nB.Cross(B.V0 - B.V2), ref A, ref B);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool apart(Vector3d a, ref Triangle3d A, ref Triangle3d B)
        {
            double a0 = a.Dot(A.V0), a1 = a.Dot(A.V1), a2 = a.Dot(A.V2), b0 = a.Dot(B.V0), b1 = a.Dot(B.V1), b2 = a.Dot(B.V2);
            double ax = Math.Abs(a.x), ay = Math.Abs(a.y), az = Math.Abs(a.z);
            double mA = Gamma * Math.Max(absdot(ax, ay, az, A.V0), Math.Max(absdot(ax, ay, az, A.V1), absdot(ax, ay, az, A.V2)));
            double mB = Gamma * Math.Max(absdot(ax, ay, az, B.V0), Math.Max(absdot(ax, ay, az, B.V1), absdot(ax, ay, az, B.V2)));
            double loA = Math.Min(a0, Math.Min(a1, a2)) - mA, hiA = Math.Max(a0, Math.Max(a1, a2)) + mA;
            double loB = Math.Min(b0, Math.Min(b1, b2)) - mB, hiB = Math.Max(b0, Math.Max(b1, b2)) + mB;
            // (NaN or infinite projections compare false: not apart)
            return loB > hiA || loA > hiB;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double absdot(double ax, double ay, double az, Vector3d x)
        {
            return ax * Math.Abs(x.x) + ay * Math.Abs(x.y) + az * Math.Abs(x.z);
        }
    }

    // scratch instrumentation (not for merge): one JSON line per Apply on stderr when G3_ROUNDING_TRACE=1
    internal static class RoundingTrace
    {
        internal static readonly bool On = Environment.GetEnvironmentVariable("G3_ROUNDING_TRACE") == "1";
        internal static readonly bool SepAxis = Environment.GetEnvironmentVariable("G3_SEPAXIS") == "1";
        internal static readonly bool StopAtLimit = Environment.GetEnvironmentVariable("G3_STOP_AT_LIMIT") == "1";
        internal static readonly bool Dist = Environment.GetEnvironmentVariable("G3_ROUNDING_TRACE_DIST") == "1";
        [ThreadStatic] internal static bool InImproves;
        internal static double EvalStep;
        internal static int EvalNon, EvalNonFar, EvalNonPos, EvalStage;
        internal static readonly HashSet<int> Dirty = new HashSet<int>();
        static readonly SortedDictionary<string, double> v = new SortedDictionary<string, double>(StringComparer.Ordinal);
        static readonly Dictionary<int, int> regions = new Dictionary<int, int>();
        static long t0, last;
        static int depth;
        internal static void Begin(MeshRoundingRepair r)
        {
            if (!On || depth++ > 0) return;
            v.Clear(); regions.Clear(); Dirty.Clear();
            t0 = last = System.Diagnostics.Stopwatch.GetTimestamp();
            Set("in.triangles", r.Mesh.TriangleCount); Set("in.vertices", r.Mesh.VertexCount);
        }
        internal static void Mark(string phase)
        {
            if (!On) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            Add("P." + phase, now - last);
            last = now;
        }
        internal static void Add(string k, double x) { if (!On) return; double o; v.TryGetValue(k, out o); v[k] = o + x; }
        internal static void Set(string k, double x) { if (On) v[k] = x; }
        internal static void Near(int hits, long dt)
        {
            if (InImproves) { Add("eval.near_queries", 1); Add("eval.near_hits", hits); Add("T.eval.near", dt); }
        }
        internal static void Pair(bool added, bool incident, bool hit, long dt, double d)
        {
            string k = (added ? "new" : "old") + (incident ? ".incident" : ".nonincident");
            Add("eval.pairs." + k, 1); Add("T.eval.pairs." + k, dt);
            if (hit) Add("eval.pairs." + k + ".intersecting", 1);
            if (Dist && added && incident == false && hit == false) {
                EvalNon++;
                if (d > 0) { EvalNonPos++; Add("eval.pairs.new.nonincident.dist_pos", 1); Add("T.eval.pairs.new.nonincident.dist_pos", dt); }
                if (d > 2 * EvalStep) { EvalNonFar++; Add("eval.pairs.new.nonincident.dist_gt_2step", 1); Add("T.eval.pairs.new.nonincident.dist_gt_2step", dt); }
            }
        }
        internal static void EndEval(bool ok, int removed, int added, int region, long dt)
        {
            Add("eval.calls", 1); Add("T.eval", dt);
            Add("eval.stage" + EvalStage, 1); Add("T.eval.stage" + EvalStage, dt);
            if (ok) { Add("eval.accepted", 1); Add("eval.accepted.removed", removed); Add("eval.accepted.added", added); }
            if (EvalStage >= 3) {
                Add("eval.reached_new", 1);
                if (EvalNon == 0) Add("eval.reached_new.no_nonincident", 1);
                else if (EvalNonPos == EvalNon) Add("eval.reached_new.all_nonincident_clear", 1);
                if (EvalNon > 0 && EvalNonFar == EvalNon) Add("eval.reached_new.all_nonincident_gt_2step", 1);
            }
            int c; regions.TryGetValue(region, out c); regions[region] = c + 1;
        }
        internal static void End(MeshRoundingRepair r)
        {
            if (!On || --depth > 0) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            Mark("final");
            double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Set("T.total", now - t0);
            Set("out.triangles", r.Mesh.TriangleCount);
            Set("out.dirty_triangle_ids", Dirty.Count);
            Set("out.touched_vertices", r.TraceTouched);
            Set("out.edits", r.Collapses + r.Flips + r.Moves + r.Zips + r.VertexRemovals);
            Set("out.initial_intersections", r.InitialIntersections); Set("out.initial_degenerate", r.InitialDegenerate);
            Set("out.remaining_intersections", r.RemainingIntersections); Set("out.remaining_degenerate", r.RemainingDegenerate);
            Set("out.limit", r.EvaluationLimitReached ? 1 : 0);
            Set("out.full_searches", r.FullPairSearches); Set("out.incremental_searches", r.IncrementalPairSearches);
            Set("out.pair_tests_budget", r.TracePairTests);
            Set("eval.regions", regions.Count);
            int most = 0; foreach (var e in regions) most = Math.Max(most, e.Value);
            Set("eval.regions.max_evals", most);
            var sb = new System.Text.StringBuilder("ROUNDING_TRACE {");
            bool first = true;
            foreach (var e in v) {
                double x = (e.Key.StartsWith("T") || e.Key.StartsWith("P.")) ? e.Value * f : e.Value;
                sb.Append(first ? "" : ", ").Append('"').Append(e.Key).Append("\\": ").Append(x.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                first = false;
            }
            Console.Error.WriteLine(sb.Append('}').ToString());
        }
    }
}
"""
open(path, "w").write(s)
