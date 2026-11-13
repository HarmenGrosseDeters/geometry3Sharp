# Scratch instrumentation of mesh_ops/MeshRoundingRepair.cs for the upper bound of the rounding-search levers of
# docs/perf/EXTREME_RUNTIME_BOOLEANS.md section 8 (lever 1: the box-neighbour enumeration of the full pair searches;
# lever 2: `near`, the box query of the evaluations). Per MeshRoundingRepair.Apply, timed on the calling thread with
# Stopwatch: the whole Apply, every full search (intersecting_pairs_all; the first one separately), the incremental
# searches, the tree rebuilds, and the time and calls of near(box) summed over the threads that call it (with the
# number of distinct threads, so a sum over parallel callers can be told apart from wall time).
# Applied to a throw-away worktree, never committed to the library:
#     git worktree add --detach <wt> <commit>; python3 probe_rounding_search.py <wt>/mesh_ops/MeshRoundingRepair.cs
#     dotnet build <wt>/benchmarks/BooleanComparison -c Release -o <tool>
# Also the stages of Apply (laps on the calling thread: setup.*, tree.first, search.first, degenerate.first, passes.first,
# contacts, position_search_loop, tiny_components, final) and the summed time of improves (the evaluation of a proposed
# edit, its pair tests and near queries included), position_search, degenerate_triangles and intersecting_pairs; in
# rebuild_tree the box collection (tree_boxes) and the TriangleBoxTree constructor (tree_ctor) separately.
# With G3_ROUND_PROBE=1 every Apply prints one line "ROUND_PROBE {...}" on stderr (the last one of a `profile` process
# is the timed operation; the earlier ones are its warm-up booleans).
import sys
path = sys.argv[1]; s = open(path).read()
def rep(old, new):
    global s
    if s.count(old) != 1: raise SystemExit("anchor not unique or missing: " + old[:80])
    s = s.replace(old, new)
rep("""        [MethodImpl(JitHints.Optimize)]
        public bool Apply()
        {""", """        [MethodImpl(JitHints.Optimize)]
        public bool Apply()
        {
            if (RoundProbe.On == false) return apply0();
            RoundProbe.Begin();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return apply0(); } finally { RoundProbe.End(System.Diagnostics.Stopwatch.GetTimestamp() - t0, Mesh.TriangleCount); }
        }

        [MethodImpl(JitHints.Optimize)]
        bool apply0()
        {""")
rep("""        void near(AxisAlignedBox3d query, List<int> result)
        {""", """        void near(AxisAlignedBox3d query, List<int> result)
        {
            if (RoundProbe.On == false) { near0(query, result); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            near0(query, result);
            RoundProbe.Near(System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        }

        [MethodImpl(JitHints.Optimize)]
        void near0(AxisAlignedBox3d query, List<int> result)
        {""")
rep("""        List<Index2i> intersecting_pairs_all(bool skipExact = true)
        {""", """        List<Index2i> intersecting_pairs_all(bool skipExact = true)
        {
            if (RoundProbe.On == false) return intersecting_pairs_all0(skipExact);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return intersecting_pairs_all0(skipExact); } finally { RoundProbe.Full(System.Diagnostics.Stopwatch.GetTimestamp() - t0); }
        }

        [MethodImpl(JitHints.Optimize)]
        List<Index2i> intersecting_pairs_all0(bool skipExact = true)
        {""")
rep("""        void rebuild_tree()
        {""", """        void rebuild_tree()
        {
            if (RoundProbe.On == false) { rebuild_tree0(); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            rebuild_tree0();
            RoundProbe.Tree(System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        }

        void rebuild_tree0()
        {""")
rep("""            tree = new TriangleBoxTree(boxes);
            dirty.Clear();""", """            long probe_tb = System.Diagnostics.Stopwatch.GetTimestamp();
            if (RoundProbe.On) RoundProbe.Sum("tree_boxes", probe_tb - probe_tt0);
            tree = new TriangleBoxTree(boxes);
            if (RoundProbe.On) RoundProbe.Sum("tree_ctor", System.Diagnostics.Stopwatch.GetTimestamp() - probe_tb);
            dirty.Clear();""")
rep("""        void rebuild_tree0()
        {""", """        void rebuild_tree0()
        {
            long probe_tt0 = System.Diagnostics.Stopwatch.GetTimestamp();""")
LAPS = [('            component_scales();\n            // a vertex of a triangle that is not finite', '            if (RoundProbe.On) RoundProbe.Lap("setup.anchors");\n            component_scales();\n            if (RoundProbe.On) RoundProbe.Lap("setup.component_scales");\n            // a vertex of a triangle that is not finite'), ('            if (SharedPositionsTouch) {\n                atPosition = new Dictionary<Vector3d, List<int>>();', '            if (RoundProbe.On) RoundProbe.Lap("setup.finite_range");\n            if (SharedPositionsTouch) {\n                atPosition = new Dictionary<Vector3d, List<int>>();'), ('            rebuild_tree();\n            lastPairs = null;', '            if (RoundProbe.On) RoundProbe.Lap("setup.positions_exact");\n            rebuild_tree();\n            if (RoundProbe.On) RoundProbe.Lap("tree.first");\n            lastPairs = null;'), ('            List<Index2i> pairs = intersecting_pairs();\n            List<int> bad = degenerate_triangles();\n            InitialIntersections = pairs.Count;', '            if (RoundProbe.On) RoundProbe.Lap("setup.misc");\n            List<Index2i> pairs = intersecting_pairs();\n            if (RoundProbe.On) RoundProbe.Lap("search.first");\n            List<int> bad = degenerate_triangles();\n            if (RoundProbe.On) RoundProbe.Lap("degenerate.first");\n            InitialIntersections = pairs.Count;'), ('            repair_passes(ref pairs, ref bad);\n            // contacts the rounding made: separated', '            if (RoundProbe.On) RoundProbe.Lap("false_contacts.initial");\n            repair_passes(ref pairs, ref bad);\n            if (RoundProbe.On) RoundProbe.Lap("passes.first");\n            // contacts the rounding made: separated'), ('            for (int round = 0; round < MaxSearchRounds && MaxPasses > 0', '            if (RoundProbe.On) RoundProbe.Lap("contacts");\n            for (int round = 0; round < MaxSearchRounds && MaxPasses > 0'), ('            if (check)\n                check_all_bounds();\n            if (RemoveTinyComponents', '            if (RoundProbe.On) RoundProbe.Lap("position_search_loop");\n            if (check)\n                check_all_bounds();\n            if (RemoveTinyComponents'), ('            RemainingIntersections = pairs.Count;\n            RemainingDegenerate = bad.Count;\n            List<List<int>> contactsLeft', '            if (RoundProbe.On) RoundProbe.Lap("tiny_components");\n            RemainingIntersections = pairs.Count;\n            RemainingDegenerate = bad.Count;\n            List<List<int>> contactsLeft')]

for old, new in LAPS:
    rep(old, new)
def wrapm(sig, call, name, ret):
    # sig: the method's signature line; the original becomes <name>0
    global s
    head = "        " + sig + "\n        {"
    if s.count(head) != 1: raise SystemExit("anchor: " + sig)
    orig = sig.replace(name + "(", name + "0(", 1)
    body = ("        " + sig + "\n        {\n            if (RoundProbe.On == false) " + ("return " if ret else "") + call.replace(name + "(", name + "0(", 1) + ";" + ("" if ret else " return;") +
            "\n            long probe_t0 = System.Diagnostics.Stopwatch.GetTimestamp();\n            try { " + ("return " if ret else "") + call.replace(name + "(", name + "0(", 1) +
            "; } finally { RoundProbe.Sum(\"" + name + "\", System.Diagnostics.Stopwatch.GetTimestamp() - probe_t0); }\n        }\n\n        [MethodImpl(JitHints.Optimize)]\n        " + orig + "\n        {")
    s = s.replace(head, body)
wrapm("List<Index2i> intersecting_pairs()", "intersecting_pairs()", "intersecting_pairs", True)
wrapm("List<int> degenerate_triangles()", "degenerate_triangles()", "degenerate_triangles", True)
wrapm("bool improves(double displacement = 0, double surface = 0, bool hasSurface = false, bool removesContact = false)", "improves(displacement, surface, hasSurface, removesContact)", "improves", True)
wrapm("int position_search(List<int> bad, List<Index2i> pairs)", "position_search(bad, pairs)", "position_search", True)

rep("""                pairs = intersecting_pairs_of_changed();""", """                long probe_t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                pairs = intersecting_pairs_of_changed();
                if (RoundProbe.On) RoundProbe.Incremental(System.Diagnostics.Stopwatch.GetTimestamp() - probe_t0);""")
s = s.rstrip()
assert s.endswith("}")
s = s[:-1] + """
    internal static class RoundProbe
    {
        public static readonly bool On = System.Environment.GetEnvironmentVariable("G3_ROUND_PROBE") == "1";
        static long full, fullFirst, fullCalls, incr, incrCalls, tree, treeCalls, near, nearCalls;
        static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> nearThreads = new System.Collections.Concurrent.ConcurrentDictionary<int, bool>();
        static double ms(long t) => t * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        static long lapAt;
        static readonly System.Collections.Generic.List<string> lapNames = new System.Collections.Generic.List<string>();
        static readonly System.Collections.Generic.Dictionary<string, long> laps = new System.Collections.Generic.Dictionary<string, long>();
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long[]> sums = new System.Collections.Concurrent.ConcurrentDictionary<string, long[]>();
        public static void Begin() { full = fullFirst = fullCalls = incr = incrCalls = tree = treeCalls = near = nearCalls = 0; nearThreads.Clear(); laps.Clear(); lapNames.Clear(); sums.Clear(); lapAt = System.Diagnostics.Stopwatch.GetTimestamp(); }
        public static void Lap(string name)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (laps.ContainsKey(name) == false) { laps[name] = 0; lapNames.Add(name); }
            laps[name] += now - lapAt; lapAt = now;
        }
        public static void Sum(string name, long t)
        {
            long[] a = sums.GetOrAdd(name, _ => new long[2]);
            System.Threading.Interlocked.Add(ref a[0], t); System.Threading.Interlocked.Increment(ref a[1]);
        }
        public static void Full(long t) { if (System.Threading.Interlocked.Increment(ref fullCalls) == 1) fullFirst = t; System.Threading.Interlocked.Add(ref full, t); }
        public static void Incremental(long t) { System.Threading.Interlocked.Increment(ref incrCalls); System.Threading.Interlocked.Add(ref incr, t); }
        public static void Tree(long t) { System.Threading.Interlocked.Increment(ref treeCalls); System.Threading.Interlocked.Add(ref tree, t); }
        public static void Near(long t) { System.Threading.Interlocked.Increment(ref nearCalls); System.Threading.Interlocked.Add(ref near, t); nearThreads.TryAdd(System.Environment.CurrentManagedThreadId, true); }
        public static void End(long t, int triangles)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            Lap("final");
            var st = new System.Text.StringBuilder();
            foreach (string n in lapNames) st.Append(", \\"lap." + n + "\\": " + ms(laps[n]).ToString("F1", c));
            foreach (var kv in sums) st.Append(", \\"sum." + kv.Key + "_ms\\": " + ms(kv.Value[0]).ToString("F1", c) + ", \\"sum." + kv.Key + "_calls\\": " + kv.Value[1]);
            System.Console.Error.WriteLine("ROUND_PROBE {\\"apply_ms\\": " + ms(t).ToString("F1", c) + ", \\"triangles\\": " + triangles
                + ", \\"full_ms\\": " + ms(full).ToString("F1", c) + ", \\"full_first_ms\\": " + ms(fullFirst).ToString("F1", c) + ", \\"full_calls\\": " + fullCalls
                + ", \\"incremental_ms\\": " + ms(incr).ToString("F1", c) + ", \\"incremental_calls\\": " + incrCalls
                + ", \\"tree_ms\\": " + ms(tree).ToString("F1", c) + ", \\"tree_calls\\": " + treeCalls
                + ", \\"near_ms\\": " + ms(near).ToString("F1", c) + ", \\"near_calls\\": " + nearCalls + ", \\"near_threads\\": " + nearThreads.Count + st.ToString() + "}");
        }
    }
}
"""
open(path, "w").write(s)
