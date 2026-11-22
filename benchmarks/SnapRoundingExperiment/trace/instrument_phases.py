# Scratch instrumentation of mesh_ops/MeshRoundingRepair.cs for docs/research/ROUNDING_COST_2026-11.md (hard problem 3,
# parts b to d). The successor of instrument.py, whose anchors match 46529be only. Applied to a throw-away worktree,
# never committed to the library:
#     git worktree add --detach <wt> 57623890          # the commit the anchors below match
#     python3 instrument_phases.py <wt>/mesh_ops/MeshRoundingRepair.cs
#     dotnet build <wt>/benchmarks/BooleanComparison -c Release -o <tool>
# The built tool behaves as master unless an environment variable is set:
#     G3_ROUNDING_PHASES=1   one line "ROUNDING_PHASES {...}" per MeshRoundingRepair.Apply on stderr: the phases of Apply
#                            (P.*, ms, they add up to the whole Apply), and accumulated over the Apply the tree builds, the
#                            pair searches, `near` (calls, triangles returned, time) and the evaluations of proposed edits
#                            (T.*, ms; N.*, counts)
#     G3_FIRST_SEARCH=split  the first full search (intersecting_pairs_all) takes its candidates from a tree over the
#                            split-clipped sub-boxes of the triangles (SplitBoxes below) instead of one box per
#                            triangle; the pairs it reports must be the same, so the result must be bit-identical
#     G3_SPLIT_PARTS=<k>     the largest number of sub-boxes per triangle (default 8)
#     G3_FIRST_SEARCH=axes   every full search (normally one per Apply, the first) drops a pair before the pair test if the two triangles are apart along
#                            one of their 8 cached axes (AxesOf / AxesApart below; SNAP_ROUNDING_3D.md section 5.3 with the
#                            axes computed once per triangle instead of per pair)
#     G3_FIRST_SEARCH=split+axes   both
#     G3_NEAR_PENDING=cached `near` keeps the boxes of its linear list (the triangles changed since the tree of the changed
#                            triangles was built) and refreshes them when the mesh changed, instead of computing them
#                            from the mesh in every query
#     G3_CHECK_NEAR=1        the library's own check of every `near` against a scan of all triangles, and of the pair
#                            searches against a full search (CheckOnThisThread; slow, for small cases)
#     G3_TREE_MEDIAN=1       with a second argument, the path of mesh_ops/ExactMeshArrangement.cs: every TriangleBoxTree
#                            of the rounding is built by the median split, also above 4096 boxes (serially; the tree
#                            quality probe; the trees of the boolean before the rounding stay as they are)
# Each anchor must occur exactly once, so the script fails instead of patching a changed file wrongly.
import sys
path = sys.argv[1]
s = open(path).read()
def rep(old, new):
    global s
    if s.count(old) != 1: raise SystemExit("anchor not found or not unique (%d): %s" % (s.count(old), old[:80]))
    s = s.replace(old, new)

# --- Apply: phases
rep("""            Collapses = Flips = Moves = Zips = StripZips = VertexRemovals = RemovedComponents = RemovedTriangles = KeptWhenEmptied = 0;
            LockedContractions""", """            RoundingPhases.Begin();
            try { return apply_phases(); } finally { RoundingPhases.End(this); }
        }

        [MethodImpl(JitHints.Optimize)]
        bool apply_phases()
        {
            Collapses = Flips = Moves = Zips = StripZips = VertexRemovals = RemovedComponents = RemovedTriangles = KeptWhenEmptied = 0;
            LockedContractions""")
rep("""            component_scales();
            // a vertex of a triangle that is not finite""", """            RoundingPhases.Mark("setup.anchors_boundary");
            component_scales();
            RoundingPhases.Mark("setup.component_scales");
            // a vertex of a triangle that is not finite""")
rep("""            if (SharedPositionsTouch) {
                // (sized for the vertices""", """            RoundingPhases.Mark("setup.range_scan");
            if (SharedPositionsTouch) {
                // (sized for the vertices""")
rep("""            SkippedExactQueries = 0;
            if (exactF != null) {""", """            RoundingPhases.Mark("setup.position_map");
            SkippedExactQueries = 0;
            if (exactF != null) {""")
rep("""            rebuild_tree();
            lastPairs = null;""", """            RoundingPhases.Mark("setup.exact_flags");
            rebuild_tree();
            RoundingPhases.Mark("tree.first");
            lastPairs = null;""")
rep("""            List<Index2i> pairs = intersecting_pairs();
            List<int> bad = degenerate_triangles();
            InitialIntersections = pairs.Count;""", """            RoundingPhases.Mark("misc");
            List<Index2i> pairs = intersecting_pairs();
            RoundingPhases.Mark("search.first");
            List<int> bad = degenerate_triangles();
            RoundingPhases.Mark("degenerate.first");
            InitialIntersections = pairs.Count;""")
rep("""            pairBudget = pair_budget(MaxPairTests, PairTestsDefectScale, PairTestsMaxGrowth, pairs.Count + bad.Count + InitialFalseContacts);
            repair_passes(ref pairs, ref bad);""", """            pairBudget = pair_budget(MaxPairTests, PairTestsDefectScale, PairTestsMaxGrowth, pairs.Count + bad.Count + InitialFalseContacts);
            RoundingPhases.Mark("false_contacts.first");
            repair_passes(ref pairs, ref bad);
            RoundingPhases.Mark("passes.first");""")
rep("""            if (check)
                check_all_bounds();
            if (RemoveTinyComponents""", """            RoundingPhases.Mark("later_stages");
            if (check)
                check_all_bounds();
            if (RemoveTinyComponents""")
rep("""            RemainingIntersections = pairs.Count;
            RemainingDegenerate = bad.Count;
            List<List<int>> contactsLeft""", """            RoundingPhases.Mark("tiny_components");
            RemainingIntersections = pairs.Count;
            RemainingDegenerate = bad.Count;
            List<List<int>> contactsLeft""")
rep("""                RemainingDefectAt = Mesh.GetVertex(contactsLeft[0][0]);
            return IsClean;""", """                RemainingDefectAt = Mesh.GetVertex(contactsLeft[0][0]);
            RoundingPhases.Mark("tail");
            return IsClean;""")

# --- near, the tree, the searches, the evaluations
rep("""        void near(AxisAlignedBox3d query, List<int> result)
        {""", """        void near(AxisAlignedBox3d query, List<int> result)
        {
            if (RoundingPhases.On == false) { near_impl(query, result); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int builds = DirtyTreeBuilds;
            near_impl(query, result);
            long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            RoundingPhases.Add("T.near", dt); RoundingPhases.Count("N.near", 1); RoundingPhases.Count("N.near_hits", result.Count);
            RoundingPhases.Count("N.near_pending_scanned", pending.Count); RoundingPhases.Count("N.near_dirty", dirty.Count);
            if (DirtyTreeBuilds > builds) { RoundingPhases.Add("T.near_with_dirty_build", dt); RoundingPhases.Count("N.near_dirty_builds", 1); RoundingPhases.Count("N.near_dirty_build_size", dirty.Count); }
        }

        [MethodImpl(JitHints.Optimize)]
        void near_impl(AxisAlignedBox3d query, List<int> result)
        {""")
rep("""        void rebuild_tree()
        {""", """        void rebuild_tree()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            rebuild_tree_impl();
            RoundingPhases.Add("T.tree", System.Diagnostics.Stopwatch.GetTimestamp() - t0); RoundingPhases.Count("N.tree", 1);
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
            RoundingPhases.Add("T.search", System.Diagnostics.Stopwatch.GetTimestamp() - t0); RoundingPhases.Count("N.search", 1);
            return r;
        }

        [MethodImpl(JitHints.Optimize)]
        List<Index2i> intersecting_pairs_impl()
        {""")
rep("""        bool improves(double displacement = 0, double surface = 0, bool hasSurface = false, bool removesContact = false)
        {""", """        bool improves(double displacement = 0, double surface = 0, bool hasSurface = false, bool removesContact = false)
        {
            if (RoundingPhases.On == false)
                return improves_impl(displacement, surface, hasSurface, removesContact);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool ok = improves_impl(displacement, surface, hasSurface, removesContact);
            RoundingPhases.Add("T.improves", System.Diagnostics.Stopwatch.GetTimestamp() - t0); RoundingPhases.Count("N.improves", 1);
            if (ok) RoundingPhases.Count("N.improves_accepted", 1);
            return ok;
        }

        [MethodImpl(JitHints.Optimize)]
        bool improves_impl(double displacement = 0, double surface = 0, bool hasSurface = false, bool removesContact = false)
        {""")

# --- inside near: the tree of the start, the tree of the changed triangles (its build apart), the linear list
rep("""            tree.Query(query, hits);
            foreach (int i in hits) {
                int t = treeTids[i];
                if (dirty.Contains(t) == false && Mesh.IsTriangle(t))
                    result.Add(t);
            }
            if (dirty.Count == 0)
                return;""", """            long n0 = RoundingPhases.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            tree.Query(query, hits);
            foreach (int i in hits) {
                int t = treeTids[i];
                if (dirty.Contains(t) == false && Mesh.IsTriangle(t))
                    result.Add(t);
            }
            if (RoundingPhases.On) { RoundingPhases.Add("T.near_main", System.Diagnostics.Stopwatch.GetTimestamp() - n0); RoundingPhases.Count("N.near_main_raw", hits.Count); }
            if (dirty.Count == 0)
                return;
            n0 = RoundingPhases.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;""")
rep("""            // (a triangle in the tree that changed since is in pending, with its current box; one removed since is skipped)
            hits.Clear();""", """            if (RoundingPhases.On) { long n1 = System.Diagnostics.Stopwatch.GetTimestamp(); RoundingPhases.Add("T.near_dirty_build", n1 - n0); n0 = n1; }
            // (a triangle in the tree that changed since is in pending, with its current box; one removed since is skipped)
            hits.Clear();""")
rep("""            if (pending.Count > MaxPendingScanned)
                MaxPendingScanned = pending.Count;""", """            if (RoundingPhases.On) { long n1 = System.Diagnostics.Stopwatch.GetTimestamp(); RoundingPhases.Add("T.near_dirty_query", n1 - n0); n0 = n1; RoundingPhases.Count("N.near_dirty_raw", hits.Count); }
            if (pending.Count > MaxPendingScanned)
                MaxPendingScanned = pending.Count;""")
rep("""            foreach (int t in pending)
                if (Mesh.IsTriangle(t) && closed_overlap(box(Mesh.GetTriangle(t)), query))
                    result.Add(t);""", """            if (RoundingPhases.CachedPending) {
                // the boxes of the list, computed once per state of the mesh (version: every mark_dirty raises it) and
                // list length instead of once per query; the same triangles in the same order
                if (pendingBoxVersion != version || pendingBoxCount != pending.Count) {
                    if (pendingBox.Length < 6 * pending.Count) { pendingBox = new double[12 * pending.Count]; pendingBoxTid = new int[2 * pending.Count]; }
                    pendingBoxLive = 0;
                    foreach (int t in pending) {
                        if (Mesh.IsTriangle(t) == false) continue;
                        AxisAlignedBox3d pb = box(Mesh.GetTriangle(t));
                        int o = 6 * pendingBoxLive;
                        pendingBox[o] = pb.Min.x; pendingBox[o + 1] = pb.Min.y; pendingBox[o + 2] = pb.Min.z; pendingBox[o + 3] = pb.Max.x; pendingBox[o + 4] = pb.Max.y; pendingBox[o + 5] = pb.Max.z;
                        pendingBoxTid[pendingBoxLive++] = t;
                    }
                    pendingBoxVersion = version; pendingBoxCount = pending.Count;
                }
                double qx0 = query.Min.x, qy0 = query.Min.y, qz0 = query.Min.z, qx1 = query.Max.x, qy1 = query.Max.y, qz1 = query.Max.z;
                for (int i = 0, o = 0; i < pendingBoxLive; ++i, o += 6)
                    if (!(pendingBox[o + 3] < qx0 || pendingBox[o] > qx1 || pendingBox[o + 4] < qy0 || pendingBox[o + 1] > qy1 || pendingBox[o + 5] < qz0 || pendingBox[o + 2] > qz1))
                        result.Add(pendingBoxTid[i]);
            }
            else
            foreach (int t in pending)
                if (Mesh.IsTriangle(t) && closed_overlap(box(Mesh.GetTriangle(t)), query))
                    result.Add(t);""")
rep("""        void mark_dirty(int tid)
        {""", """        double[] pendingBox = new double[0];
        int[] pendingBoxTid = new int[0];
        int pendingBoxLive, pendingBoxCount = -1;
        long pendingBoxVersion = -1;
        void mark_dirty(int tid)
        {""")
rep("""            check = CheckOnThisThread;""", """            check = CheckOnThisThread || RoundingPhases.CheckNear;""")
rep("""            if (check)
                check_near(query, result);
            movedV = save;""", """            if (RoundingPhases.On) RoundingPhases.Add("T.near_pending", System.Diagnostics.Stopwatch.GetTimestamp() - n0);
            if (check)
                check_near(query, result);
            movedV = save;""")

# --- the first search from split-clipped sub-boxes (the A/B of the note, section 5)
rep("""            int[] tris = treeTids.ToArray();
            bool skip = skipExact && exactAtStart != null;
""", """            if (RoundingPhases.SplitSearch && skipExact)
                return intersecting_pairs_all_split();
            if (RoundingPhases.AxesSearch && skipExact)
                return intersecting_pairs_all_axes();
            int[] tris = treeTids.ToArray();
            bool skip = skipExact && exactAtStart != null;
""")
rep("""        [MethodImpl(JitHints.Optimize)]
        List<Index2i> intersecting_pairs_of_changed()
        {""", """        // intersecting_pairs_all with the cached separating axes in front of the pair test
        [MethodImpl(JitHints.Optimize)]
        List<Index2i> intersecting_pairs_all_axes()
        {
            int[] tris = treeTids.ToArray();
            bool skip = exactAtStart != null;
            int n = tris.Length;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var axis = new double[12 * n];
            var range = new double[8 * n];
            gParallel.ForRange((n + TriangleBlock - 1) / TriangleBlock, (b) => {
                int end = Math.Min(n, (b + 1) * TriangleBlock);
                for (int i = b * TriangleBlock; i < end; ++i) {
                    Index3i tv = Mesh.GetTriangle(tris[i]);
                    RoundingPhases.AxesOf(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c), axis, 12 * i, range, 8 * i);
                }
            });
            RoundingPhases.Add("T.axes_build", System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            long skipped = 0;
            var found = new List<Index2i>[n];
            gParallel.ForRange(n, (k) => {
                int ta = tris[k];
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
                    if (tb == ta || (tb < ta && (skip == false || exact_triangle(tb) == false))) return;
                    Index3i ib = Mesh.GetTriangle(tb);
                    Triangle3d B = triangle(ib);
                    if (RoundingPhases.AxesApart(axis, 12 * k, range, 8 * k, B.V0, B.V1, B.V2) || RoundingPhases.AxesApart(axis, 12 * i, range, 8 * i, A.V0, A.V1, A.V2)) return;
                    if (ExactTriangleIntersection.IsDegenerate(ref B)) return;
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

        // intersecting_pairs_all with the candidates from a tree over the sub-boxes of the triangles (RoundingPhases.SplitBoxes):
        // a pair is tested if a sub-box of the one overlaps a sub-box of the other, once (at the first such pair of sub-boxes)
        [MethodImpl(JitHints.Optimize)]
        List<Index2i> intersecting_pairs_all_split()
        {
            int[] tris = treeTids.ToArray();
            bool skip = exactAtStart != null;
            int n = tris.Length, parts = RoundingPhases.SplitParts;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            // (the sub-boxes per triangle in parallel, each into its own slots; then one list in triangle order)
            var sub = new double[n][];
            gParallel.ForRange((n + TriangleBlock - 1) / TriangleBlock, (b) => {
                var buffer = new double[6 * parts];
                int end = Math.Min(n, (b + 1) * TriangleBlock);
                for (int i = b * TriangleBlock; i < end; ++i) {
                    Index3i tv = Mesh.GetTriangle(tris[i]);
                    int c = RoundingPhases.SplitBoxes(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c), parts, buffer);
                    sub[i] = new double[6 * c];
                    Array.Copy(buffer, sub[i], 6 * c);
                }
            });
            var first = new int[n + 1];
            for (int i = 0; i < n; ++i)
                first[i + 1] = first[i] + sub[i].Length / 6;
            var boxes = new List<AxisAlignedBox3d>(first[n]);
            var owner = new int[first[n]];
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < sub[i].Length; j += 6) {
                    owner[boxes.Count] = i;
                    boxes.Add(new AxisAlignedBox3d(sub[i][j], sub[i][j + 1], sub[i][j + 2], sub[i][j + 3], sub[i][j + 4], sub[i][j + 5]));
                }
            var splitTree = new TriangleBoxTree(boxes);
            bool withAxes = RoundingPhases.AxesSearch;
            var axis = new double[withAxes ? 12 * n : 0];
            var range = new double[withAxes ? 8 * n : 0];
            if (withAxes)
                gParallel.ForRange((n + TriangleBlock - 1) / TriangleBlock, (b) => {
                    int end = Math.Min(n, (b + 1) * TriangleBlock);
                    for (int i = b * TriangleBlock; i < end; ++i) {
                        Index3i tv = Mesh.GetTriangle(tris[i]);
                        RoundingPhases.AxesOf(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c), axis, 12 * i, range, 8 * i);
                    }
                });
            RoundingPhases.Add("T.split_build", System.Diagnostics.Stopwatch.GetTimestamp() - t0); RoundingPhases.Count("N.split_boxes", boxes.Count);
            long skipped = 0;
            var found = new List<Index2i>[n];
            gParallel.ForRange(n, (k) => {
                int ta = tris[k];
                if (skip && exact_triangle(ta)) {
                    System.Threading.Interlocked.Increment(ref skipped);
                    return;
                }
                Index3i ia = Mesh.GetTriangle(ta);
                Triangle3d A = triangle(ia);
                if (ExactTriangleIntersection.IsDegenerate(ref A))
                    return;
                List<Index2i> local = null;
                double[] mine = sub[k];
                var hitsOf = new List<int>();
                var seen = new HashSet<int>();
                for (int j = 0; j < mine.Length; j += 6) {
                    hitsOf.Clear();
                    splitTree.Query(new AxisAlignedBox3d(mine[j], mine[j + 1], mine[j + 2], mine[j + 3], mine[j + 4], mine[j + 5]), hitsOf);
                    foreach (int h in hitsOf) {
                        int kb = owner[h];
                        int tb = tris[kb];
                        if (tb == ta || (tb < ta && (skip == false || exact_triangle(tb) == false))) continue;
                        if (mine.Length > 6 || sub[kb].Length > 6) {
                            if (seen.Add(kb) == false) continue;
                        }
                        Index3i ib = Mesh.GetTriangle(tb);
                        Triangle3d B = triangle(ib);
                        if (withAxes && (RoundingPhases.AxesApart(axis, 12 * k, range, 8 * k, B.V0, B.V1, B.V2) || RoundingPhases.AxesApart(axis, 12 * kb, range, 8 * kb, A.V0, A.V1, A.V2))) continue;
                        if (ExactTriangleIntersection.IsDegenerate(ref B)) continue;
                        bool hit = ta < tb ? intersects(ia, ref A, ib, ref B) : intersects(ib, ref B, ia, ref A);
                        if (hit) {
                            if (local == null) local = new List<Index2i>();
                            local.Add(new Index2i(Math.Min(ta, tb), Math.Max(ta, tb)));
                        }
                    }
                }
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
        {""")

# --- the trace class, and the sub-boxes (the same code as benchmarks/SnapRoundingExperiment/SplitBoxes.cs)
split = open(__file__.rsplit("/", 2)[0] + "/SplitBoxes.cs").read()
split = split[split.index("    // BEGIN shared with trace/instrument_phases.py"):split.index("    // END shared")]
s = s.rstrip()
assert s.endswith("}")
s = s[:-1] + """
    // (scratch instrumentation, benchmarks/SnapRoundingExperiment/trace/instrument_phases.py; one Apply at a time)
    internal static class RoundingPhases
    {
        internal static readonly bool On = Environment.GetEnvironmentVariable("G3_ROUNDING_PHASES") == "1";
        static readonly string firstSearch = Environment.GetEnvironmentVariable("G3_FIRST_SEARCH") ?? "";
        internal static readonly bool SplitSearch = firstSearch.StartsWith("split");
        internal static readonly bool AxesSearch = firstSearch.EndsWith("axes");
        internal static readonly bool CachedPending = Environment.GetEnvironmentVariable("G3_NEAR_PENDING") == "cached";
        internal static readonly bool CheckNear = Environment.GetEnvironmentVariable("G3_CHECK_NEAR") == "1";
        internal static readonly bool MedianTrees = Environment.GetEnvironmentVariable("G3_TREE_MEDIAN") == "1";
        internal static readonly int SplitParts = int.Parse(Environment.GetEnvironmentVariable("G3_SPLIT_PARTS") ?? "8");
        static long last, start;
        static readonly System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
        static readonly System.Collections.Generic.List<long> ticks = new System.Collections.Generic.List<long>();
        static readonly System.Collections.Generic.Dictionary<string, long> sums = new System.Collections.Generic.Dictionary<string, long>();
        static readonly System.Collections.Generic.Dictionary<string, long> counts = new System.Collections.Generic.Dictionary<string, long>();
        static int depth;

        internal static void Begin()
        {
            if (depth++ > 0) return;
            if (MedianTrees) TriangleBoxTree.ForceMedian = true;
            names.Clear(); ticks.Clear(); sums.Clear(); counts.Clear();
            start = last = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        internal static void Mark(string name)
        {
            if (On == false || depth != 1) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            names.Add(name); ticks.Add(now - last);
            last = now;
        }
        internal static void Add(string name, long dt)
        {
            if (On == false) return;
            lock (sums) { sums.TryGetValue(name, out long v); sums[name] = v + dt; }
        }
        internal static void Count(string name, long c)
        {
            if (On == false) return;
            lock (sums) { counts.TryGetValue(name, out long v); counts[name] = v + c; }
        }
        internal static void End(MeshRoundingRepair r)
        {
            if (--depth > 0) return;
            if (MedianTrees) TriangleBoxTree.ForceMedian = false;
            if (On == false) return;
            double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            var sb = new System.Text.StringBuilder("ROUNDING_PHASES {");
            sb.Append("\\"total_ms\\":").Append(((System.Diagnostics.Stopwatch.GetTimestamp() - start) * ms).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(",\\"triangles\\":").Append(r.Mesh.TriangleCount).Append(",\\"skipped_exact\\":").Append(r.SkippedExactQueries);
            sb.Append(",\\"initial_intersections\\":").Append(r.InitialIntersections).Append(",\\"initial_degenerate\\":").Append(r.InitialDegenerate);
            sb.Append(",\\"full_searches\\":").Append(r.FullPairSearches).Append(",\\"incremental_searches\\":").Append(r.IncrementalPairSearches);
            sb.Append(",\\"limit\\":").Append(r.EvaluationLimitReached ? "true" : "false");
            var p = new System.Collections.Generic.Dictionary<string, long>();
            for (int i = 0; i < names.Count; ++i) { p.TryGetValue(names[i], out long v); p[names[i]] = v + ticks[i]; }
            foreach (var kv in p)
                sb.Append(",\\"P.").Append(kv.Key).Append("\\":").Append((kv.Value * ms).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            foreach (var kv in sums)
                sb.Append(",\\"").Append(kv.Key).Append("\\":").Append((kv.Value * ms).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            foreach (var kv in counts)
                sb.Append(",\\"").Append(kv.Key).Append("\\":").Append(kv.Value);
            sb.Append("}");
            Console.Error.WriteLine(sb.ToString());
        }

""" + split + """    }
}
"""
open(path, "w").write(s)
print("instrumented", path)

if len(sys.argv) > 2:
    path = sys.argv[2]
    s = open(path).read()
    # (a field of the class itself, set by RoundingPhases for the time of an Apply: the proof-scope analyzer of the exact
    # files admits no read of the environment there)
    rep("            median = n <= MedianSplitBelow;", "            median = n <= MedianSplitBelow || ForceMedian;")
    rep("        const int MedianSplitBelow = 4096;", "        const int MedianSplitBelow = 4096;\n        internal static volatile bool ForceMedian;")
    open(path, "w").write(s)
    print("instrumented", path)
